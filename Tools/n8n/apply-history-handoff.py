#!/usr/bin/env python3
"""Idempotent migration (A5/A6, audit stage 4) — one history with explicit sources + the real handoff.

Bot templates (both):
  Webhook → *Is Incoming? →[yes] If (unchanged chain) | [no] *Is Outgoing? →[yes] *Outgoing Bound? →
  *Record Outgoing (owner_phone rows into chat_history_events AND the agent memory; outgoing_api rows
  only into the history) → Outcome; Suppressed?[yes] → *Record Suppressed (client row + memory human
  row — history recording no longer depends on generation) → Outcome; Claim Event also records the
  client row; Record Send records the ai row; Record Cancel records the human row when the agent
  never ran (quota / generation failure); V6: Generated?[yes] → *Stamp Draft (ties the agent's memory
  row to THIS execution) → Pause Before Reading, and Record Send / Record Cancel act only on the row
  carrying their execution id; Read Reply Mode ORs the handoff pause; *Create Handoff
  tool (→ crHandoffReq0001) wired into the AI Agent + the tool rule appended to the system prompt.
Create WA/TG: subscribe to outgoing_message_phone + outgoing_message_api.
Dashboard Outcomes: owner rows labelled «Владелец:» for the classifier; open handoffs are REAL
  events (source='event') that outrank the classifier (order → order_collected, else owner_needed).
Delete Bot Files: wipes chat_history_events / handoff_requests / reply_events / chat_reply_state.
Set Workflow State: the three handoff workflow ids are protected.
T6 (Q1 B + V6-unknown, follow-ups 2026-10-11): the memory marks — owner rows open with «Владелец: » (Record Outgoing
here, Record Owner Send in the Wappi Proxy), the bot's reply of unknown delivery with «[доставка не подтверждена] »
(Record Send); Stamp Draft cuts a leading run of marks the model itself wrote; the «Сводка» transcript reads the
content without them; the templates' own prompt ends with the ##memory_marks## rule (_core.md carries the same text).

    python3 Tools/n8n/apply-history-handoff.py          # patch in place (re-runnable)
    python3 Tools/n8n/apply-history-handoff.py --check
"""
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
sys.path.insert(0, HERE)
import clone_lineage as cl  # noqa: E402  (Delete Chat Memory deletes by the bot's line, T5)
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
BOTS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")
CREATES = ("XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json")
DASHBOARD = "2htWSV5IHO8E2CgB-Dashboard_Outcomes.json"
DELETE_BOT = "lmjYsdNcQA2IE5rl-Delete_Bot_Files.json"
SET_STATE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
HANDOFF_IDS = ("crHandoffReq0001", "crHandoffNtfy001", "crHandoffsApi001")
M = "$('Webhook').first().json.body.messages[0]"
# body text for the history: never a media payload (Wappi puts base64 into `body` for media)
BODY_TEXT = "(['chat','text'].includes(String(m.type)) ? String(m.body || '').slice(0, 4000) : ('[' + String(m.type || 'media') + ']' + (m.caption ? ' ' + String(m.caption).slice(0, 1000) : '')))"

MEMORY_ROW = "jsonb_build_object('type', %s, 'content', %s, 'additional_kwargs', jsonb_build_object('source', %s, 'message_id', %s), 'response_metadata', '{}'::jsonb)"

# ---------------------------------------------------------------------------------------------- memory marks (T6)
# Q1 B + V6-unknown (follow-ups 2026-10-11, F1F2-plan §4): the memory node hands the model ONLY role + content — the
# rows' additional_kwargs (source, delivery) never reach it (independent review 2026-10-11, pipeline-delivery-context).
# An owner's reply therefore read as the bot's OWN earlier answer, and a reply of unknown delivery as a delivered one.
# The writers now put the fact INTO the content the model reads: «Владелец: » on owner rows (owner_app — the Wappi
# Proxy's Record Owner Send, build-auth-workflows.py takes this very constant — and owner_phone — Record Outgoing),
# «[доставка не подтверждена] » on the bot's reply whose delivery is unknown (Record Send; a lost run — the Handoff
# Notifier, T7). The history (chat_history_events.body), source and delivery are unchanged: the marks live only in the
# memory content. ONE place for the strings: the eval harness (run-eval.py) reads them from this file at a git ref.
OWNER_MEMORY_PREFIX = "Владелец: "
UNDELIVERED_MEMORY_PREFIX = "[доставка не подтверждена] "
# A LEADING run of marks the model itself wrote (it copies what it sees in memory): Final Check strips it from the text
# that goes to the client, Stamp Draft from the bot's own memory row — a made-up «Владелец: завтра в 15:00» must never
# become a confirmed agreement on the next turn. One pattern for JavaScript (Final Check) and Postgres ARE (Stamp Draft,
# the «Сводка» transcript): no `$` (pg-promise), no flags (the staging/prod collation cannot fold Cyrillic case), so the
# capitalised and lower-case first letters are spelled out. Only the OPENING of the text: the word anywhere else, or a
# mark on a later line, is the reply's own text.
MEMORY_MARKS_LEAD_RE = r"^(\s*([Вв]ладелец\s*:|\[\s*[Дд]оставка не подтверждена\s*\]))+\s*"
# The one rule the model gets for both marks (plan §4.3): appended to the «Память» paragraph of prompts/_core.md (the
# Create/Edit vertical maps, inject-prompts.py) and, as the `##memory_marks##` block, to the templates' own prompt —
# what a bot with an empty or legacy business type carries. verify-history-handoff.py pins both copies to this text.
MEMORY_MARKS_TEXT = ("Реплики, которые начинаются с «Владелец:», написал сам владелец: названные в них день, время, место или человек — "
                     "подтверждённая договорённость; клиент переспрашивает — повтори её как есть. Твой прежний ответ с пометкой "
                     "«[доставка не подтверждена]» мог не дойти до клиента: не ссылайся на него как на уже прочитанный, а если клиент "
                     "переспрашивает или пишет, что ответа не было, ответь заново по существу. Сами эти пометки в ответах не пиши.")
MEMORY_MARKS_RULE = "\n\n##memory_marks##\n" + MEMORY_MARKS_TEXT


def sql_text(s):
    """A Python string as a standard SQL literal (standard_conforming_strings: a backslash is a plain character)."""
    assert "$" not in s, "never a dollar sign in a Postgres node's literal (pg-promise formats the whole query text)"
    return "'" + s.replace("'", "''") + "'"


OWNER_CONTENT_SQL = sql_text(OWNER_MEMORY_PREFIX) + " || %s"          # owner rows: the mark + the text as sent
MARKS_LEAD_SQL = sql_text(MEMORY_MARKS_LEAD_RE)
_UNDELIVERED = sql_text(UNDELIVERED_MEMORY_PREFIX)
_UNDELIVERED_LEN = len(UNDELIVERED_MEMORY_PREFIX)
# Record Send's content for the row it marks: unknown → the mark, once (a repeated Record Send never doubles it);
# sent → without it (a late real result after the R9 sweep marked the row unknown).
RECORD_SEND_CONTENT_SQL = (
    "case when $4 = 'sent'\n"
    "             then case when left(coalesce(message->>'content', ''), " + str(_UNDELIVERED_LEN) + ") = " + _UNDELIVERED + "\n"
    "                       then substr(message->>'content', " + str(_UNDELIVERED_LEN + 1) + ") else coalesce(message->>'content', '') end\n"
    "             when left(coalesce(message->>'content', ''), " + str(_UNDELIVERED_LEN) + ") = " + _UNDELIVERED + " then message->>'content'\n"
    "             else " + _UNDELIVERED + " || coalesce(message->>'content', '') end")

# NEVER write a positional placeholder (dollar sign + digit) inside a SQL `--` comment of these statements: n8n's Postgres node
# formats the query with pg-promise, which substitutes the placeholder THERE TOO — a multi-line reply text inlined into a
# comment line escapes it and the statement dies with a syntax error, leaving the ledger at send_pending (R7 part 2,
# 2026-09-27, found by the staging e2e run). verify-reply-pipeline.py enforces it for every Postgres node of the templates.

CLAIM_SQL = """-- A4: the atomic right to process THIS event. A re-delivered event finds its row and is refused
-- (claimed=false → duplicate); a claim left behind by a crashed run (>10 min, no send started) may be
-- re-claimed. The chat's incoming generation — what the lease compares against — counts NEWER MESSAGES,
-- not claim arrivals (R6): it bumps only when this event's own Wappi time (0 = unknown) is strictly newer
-- than the newest the chat has seen, or when either time is unknown (the old always-bump rule). A late,
-- OLDER message therefore never refuses the newer message's reply (Latest+Combine already stops the older
-- run); at an equal time Wappi's order — seen by Latest+Combine / Final Check, not here — picks the winner.
-- history_watermark lets a cancelled generation's AI rows be dropped from memory (A3.7).
-- A6: the client's message enters the ONE history right here, before any generation decision.
with ev as (
  insert into reply_events (profile_id, chat_id, message_id, execution_id, state)
  values ($1, $2, $3, $4, 'claimed')
  on conflict (profile_id, chat_id, message_id) do update
    set state = 'claimed', execution_id = excluded.execution_id, updated_at = now()
    where reply_events.state in ('claimed', 'generated') and reply_events.updated_at < now() - interval '10 minutes'
  returning message_id
), st as (
  insert into chat_reply_state (profile_id, chat_id, incoming_generation, newest_message_time)
  select $1, $2, 1, nullif($8::bigint, 0) where exists (select 1 from ev)
  on conflict (profile_id, chat_id) do update
    set incoming_generation = chat_reply_state.incoming_generation
          + case when excluded.newest_message_time is null or chat_reply_state.newest_message_time is null
                      or excluded.newest_message_time > chat_reply_state.newest_message_time then 1 else 0 end,
        newest_message_time = greatest(chat_reply_state.newest_message_time, excluded.newest_message_time)
  returning incoming_generation
), hist as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id)
  values ($1, $2, $3, 'client', $5, $6, to_timestamp($7::bigint), $4)
  on conflict (profile_id, chat_id, message_id) do nothing
  returning 1
)
select exists(select 1 from ev) as claimed,
       coalesce((select incoming_generation from st), (select incoming_generation from chat_reply_state where profile_id = $1 and chat_id = $2), 0)::bigint as generation,
       coalesce((select max(id) from n8n_chat_histories where session_id = $1 || ':' || $2), 0)::bigint as history_watermark;"""
# $7 = the history row's sent_at (server clock when the webhook has no time); $8 = the message's OWN Wappi time for the
# generation rule, 0 when unknown — never the server clock, which would compare n8n time against Wappi time (R6).
CLAIM_PARAMS = "={{ (m => [ m.profile_id, m.from, m.id, $execution.id, " + BODY_TEXT + ", String(m.type || ''), Number(m.time) || Math.floor(Date.now() / 1000), Number(m.time) || 0 ])(" + M + ") }}"

RECORD_SUPPRESSED_SQL = """-- A6: «Вместе» — no generation, but the history and the agent's memory still learn what the client
-- said, so a later return to «Авто» continues the same conversation (no double write: the memory
-- node only writes when the agent runs, which it does not on this branch).
with ev as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id)
  values ($1, $2, $3, 'client', $4, $5, to_timestamp($6::bigint), $7)
  on conflict (profile_id, chat_id, message_id) do nothing
  returning 1
), mem as (
  insert into n8n_chat_histories (session_id, message)
  select $1 || ':' || $2, __ROW__
  where exists (select 1 from ev)
    and not exists (select 1 from n8n_chat_histories x where x.session_id = $1 || ':' || $2 and x.message->'additional_kwargs'->>'message_id' = $3)
  returning 1
)
select 'suppressed'::text as outcome, exists(select 1 from ev) as recorded, exists(select 1 from mem) as remembered;""".replace(
    "__ROW__", MEMORY_ROW % ("'human'", "$4", "'client'", "$3"))
RECORD_SUPPRESSED_PARAMS = "={{ (m => [ m.profile_id, m.from, m.id, " + BODY_TEXT + ", String(m.type || ''), Number(m.time) || Math.floor(Date.now() / 1000), $execution.id ])(" + M + ") }}"

RECORD_OUTGOING_SQL = """-- A6: what the business side sent. outgoing_message_phone = the owner from the phone (history +
-- memory, so the bot knows the agreement); outgoing_message_api = fallback history row only — the
-- AI's own sends (Record Send) and the app's sends (Wappi Proxy) upgrade it to their real source, and
-- their memory rows are written by those writers, never here. Group chats are not recorded.
-- T6 (Q1 B): the owner's memory row opens with the owner mark (the model sees role + content only); the history keeps
-- the text as sent.
with ev as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id)
  select $1, $2, $3, case when $7 = 'outgoing_message_phone' then 'owner_phone' else 'outgoing_api' end, $4, $5, to_timestamp($6::bigint), $8
  where $2 not like '%@g.us'
  on conflict (profile_id, chat_id, message_id) do nothing
  returning source
), mem as (
  insert into n8n_chat_histories (session_id, message)
  select $1 || ':' || $2, __ROW__
  where exists (select 1 from ev where source = 'owner_phone')
    and not exists (select 1 from n8n_chat_histories x where x.session_id = $1 || ':' || $2 and x.message->'additional_kwargs'->>'message_id' = $3)
  returning 1
)
select case when exists (select 1 from ev) then 'recorded_' || (select source from ev limit 1)
            when $2 like '%@g.us' then 'ignored_group' else 'outgoing_known' end as outcome,
       exists(select 1 from mem) as remembered;""".replace("__ROW__", MEMORY_ROW % ("'ai'", OWNER_CONTENT_SQL % "$4", "'owner_phone'", "$3"))
RECORD_OUTGOING_PARAMS = "={{ (m => [ m.profile_id, String(m.chatId || m.to || ''), String(m.id || ''), " + BODY_TEXT + ", String(m.type || ''), Number(m.time) || Math.floor(Date.now() / 1000), String(m.wh_type || ''), $execution.id ])(" + M + ") }}"

RECORD_SEND_SQL = """-- A7: the event's final transport state and the lease release. send_unknown stays send_unknown —
-- an operator-visible uncertainty, never a silent retry. A6: a sent reply is an `ai` history row.
-- R7/V6: a reply the transport REFUSED (send_failed) never reached the client, so its memory row goes; sent and
-- send_unknown KEEP it and say so in the row — delivery sent (with the transport id) or unknown: for Wappi
-- «unknown» usually means «delivered, the answer got lost» (iteration decision 2026-09-27). Only the row Stamp
-- Draft tied to THIS execution, never a row chosen by its text: another run's identical reply is not ours (V6,
-- 2026-09-28). A run whose stamp did not happen touches nothing in memory. Nothing else is ever touched here.
-- T6 (V6-unknown): delivery never reaches the model (it reads role + content), so a reply of unknown delivery also
-- carries the undelivered mark at the start of its content — put once (a repeated Record Send never doubles it), taken
-- off again when a late real result says sent. The history row (what the app shows) never carries it.
with ev as (
  update reply_events set state = $4, transport_message_id = nullif($5, ''), error = nullif($6, ''), updated_at = now()
  where profile_id = $1 and chat_id = $2 and message_id = $3 returning 1
), rel as (
  update chat_reply_state set lock_token = null, lock_expires_at = null,
    last_sent_at = case when $4 = 'sent' then now() else last_sent_at end,
    last_sent_message_id = case when $4 = 'sent' then nullif($5, '') else last_sent_message_id end
  where profile_id = $1 and chat_id = $2 and lock_token = $7 returning 1
), hist as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id)
  select $1, $2, $5, 'ai', $8, 'chat', now(), $7 where $4 = 'sent' and nullif($5, '') is not null
  on conflict (profile_id, chat_id, message_id) do update set source = 'ai', body = excluded.body, execution_id = excluded.execution_id
  returning 1
), mem as (
  delete from n8n_chat_histories
  where (message->'additional_kwargs'->>'execution_id') = $7
    and $4 = 'send_failed' and session_id = $1 || ':' || $2 and id > $9::bigint and message->>'type' = 'ai'
    and (message->'additional_kwargs'->>'source') is null
  returning 1
), mark as (
  update n8n_chat_histories
  set message = jsonb_set(message, '{additional_kwargs}', (message->'additional_kwargs')
        || jsonb_build_object('delivery', case when $4 = 'sent' then 'sent' else 'unknown' end, 'transport_message_id', nullif($5, '')))
      || jsonb_build_object('content', __CONTENT__)
  where (message->'additional_kwargs'->>'execution_id') = $7
    and $4 <> 'send_failed' and session_id = $1 || ':' || $2 and id > $9::bigint and message->>'type' = 'ai'
    and (message->'additional_kwargs'->>'source') is null and jsonb_typeof(message->'additional_kwargs') = 'object'
  returning 1
)
select $4::text as outcome, nullif($5, '')::text as transport_message_id, exists(select 1 from ev) as recorded, exists(select 1 from rel) as released,
       (select count(*) from mem)::int as dropped_ai_rows, (select count(*) from mark)::int as marked_ai_rows;""".replace(
    "__CONTENT__", RECORD_SEND_CONTENT_SQL)
RECORD_SEND_PARAMS_TAIL = (", $json.state, $json.transportMessageId, $json.error, $execution.id, $('Final Check').first().json.text, "
                           "$('Final Check').first().json.historyWatermark ] }}")

RECORD_CANCEL_SQL = """-- A3.7: a refused / cancelled reply is recorded as such, the lease (if this run held it) is released, and
-- the AI row a cancelled GENERATION already wrote to memory is dropped — the customer never saw it, so the
-- bot must not remember having said it. R7/V6: ONLY this run's own row — the one Stamp Draft tied to this
-- execution — never a NEWER run's reply written (or sent) meanwhile, even one with the very same text. No text
-- fallback: a run whose stamp did not happen drops nothing. Owner rows (source-marked) and human rows stay.
-- A6: when the agent never ran (quota / generation failure), the client's message still enters memory.
with ev as (
  update reply_events set state = $4, updated_at = now()
  where profile_id = $1 and chat_id = $2 and message_id = $3 returning 1
), hist as (
  delete from n8n_chat_histories
  where (message->'additional_kwargs'->>'execution_id') = $7
    and $6::boolean and session_id = $1 || ':' || $2 and id > $5::bigint and message->>'type' = 'ai'
    and (message->'additional_kwargs'->>'source') is null
  returning 1
), rel as (
  update chat_reply_state set lock_token = null, lock_expires_at = null
  where profile_id = $1 and chat_id = $2 and lock_token = $7 returning 1
), mem as (
  insert into n8n_chat_histories (session_id, message)
  select $1 || ':' || $2, __ROW__
  where $8::boolean and nullif($9, '') is not null
    and not exists (select 1 from n8n_chat_histories x where x.session_id = $1 || ':' || $2 and x.message->'additional_kwargs'->>'message_id' = $3)
  returning 1
)
select $4::text as outcome, (select count(*) from hist)::int as dropped_ai_rows, exists(select 1 from ev) as recorded, exists(select 1 from mem) as remembered;""".replace(
    "__ROW__", MEMORY_ROW % ("'human'", "$9", "'client'", "$3"))
RECORD_CANCEL_PARAMS_TAIL = ", $json.outcome, $json.historyWatermark, $json.dropAiHistory, $execution.id, $json.recordHuman, $json.humanText, $json.aiText ] }}"

# V6 (iteration 2026-09-28): the Chat Memory node writes the agent's turn inside the AI Agent with no mark of the run
# that wrote it, and the run used to find «its» row only at the very end (Record Send / Record Cancel, 15–33 s later)
# by text — a NEWER run of the chat with the same text was then deleted or confirmed in its place. Stamp Draft ties
# the row to this execution right after the agent; everything later acts on that id only.
STAMP_DRAFT_SQL = """-- V6: tie the agent's memory row to THIS execution, right after the agent wrote it — the first ai row above
-- the run's watermark that no writer marked (source) and no run stamped yet, whose content is exactly the agent's
-- output. delivery starts as draft; Record Send turns it into sent or unknown (a refused one is removed). Rows
-- below the watermark, owner rows and rows another run stamped are never touched. Returns the stamped row id
-- (null = no stamp: the later records then touch nothing in memory).
-- V6-concurrent: ownership is exclusive. The candidate is LOCKED as it is chosen, and a row another run's stamp
-- holds right now is skipped (skip locked): the loser of a race takes the next matching row or none, never waits
-- to overwrite. The update re-checks every condition on the row it writes, so a row stamped meanwhile is never
-- stamped again.
-- T6 (Q1 B + V6-unknown): the bot never keeps a memory mark of its own — a leading run of «Владелец:» /
-- «[доставка не подтверждена]» the model wrote is cut from the row it stamps (Final Check cuts it from what is sent),
-- or a made-up «Владелец: завтра в 15:00» would read as the owner's confirmed agreement on the next turn. The text is
-- matched BEFORE the cut (the agent's output as written); everything later acts by the execution id.
with draft as (
  update n8n_chat_histories
  set message = jsonb_set(jsonb_set(message, '{additional_kwargs}',
        (case when jsonb_typeof(message->'additional_kwargs') = 'object' then message->'additional_kwargs' else '{}'::jsonb end)
        || jsonb_build_object('execution_id', $4::text, 'event_id', $3::text, 'delivery', 'draft')),
        '{content}', to_jsonb(regexp_replace(message->>'content', __MARKS__, '')))
  where id = (select id from n8n_chat_histories
              where session_id = $1 || ':' || $2 and id > $5::bigint and message->>'type' = 'ai'
                and (message->'additional_kwargs'->>'source') is null
                and (message->'additional_kwargs'->>'execution_id') is null
                and message->>'content' = $6
              order by id
              limit 1
              for update skip locked)
    and session_id = $1 || ':' || $2 and id > $5::bigint and message->>'type' = 'ai'
    and (message->'additional_kwargs'->>'source') is null
    and (message->'additional_kwargs'->>'execution_id') is null
    and message->>'content' = $6
  returning id
)
select (select id from draft) as draft_row_id;""".replace("__MARKS__", MARKS_LEAD_SQL)
STAMP_DRAFT_PARAMS = ("={{ (m => [ m.profile_id, m.from, m.id, $execution.id, Number($('Claim Event').first().json.history_watermark) || 0, "
                      "String($('AI Agent').first().json.output || '') ])(" + M + ") }}")


CANCEL_LABEL_JS = r"""// CANCEL LABEL: one place that names why a reply did not go out, from whichever gate refused it.
const j = $input.first().json || {};
const prev = $prevNode.name;
let outcome;
if (prev === 'Is Latest?') outcome = 'superseded';
else if (prev === 'If Quota Allows') outcome = 'quota_denied';
else if (prev === 'Generated?') outcome = 'generation_failed';
else if (prev === 'Final OK?') outcome = j.outcome || 'cancelled_fetch';
else if (prev === 'Send Allowed?') outcome = j.suppressed === true ? 'cancelled_mode' : (j.lock_busy === true ? 'cancelled_busy' : 'superseded');
else outcome = 'cancelled_fetch';
let watermark = 0;
try { watermark = Number($('Claim Event').first().json.history_watermark) || 0; } catch (e) { watermark = 0; }
// Only a run that reached the generator can have written an AI memory row to drop — and it drops ONLY ITS OWN
// row (R7/V6): Record Cancel deletes the row Stamp Draft tied to this execution id; aiText is the gate (no agent
// output, nothing to drop). The media branch (Ask to Send Text) reaches the same gates without running the agent —
// there $('AI Agent') throws, aiText stays null and nothing is dropped.
const generated = prev === 'Final OK?' || prev === 'Send Allowed?';
let aiText = null;
if (generated) {
  try { const o = $('AI Agent').first().json.output; aiText = (typeof o === 'string' && o.trim()) ? o : null; } catch (e) { aiText = null; }
}
// A6: when the agent never ran, the client's message still has to reach memory (it is the memory
// node that writes it on a normal run).
const recordHuman = prev === 'If Quota Allows' || prev === 'Generated?';
let humanText = '';
if (recordHuman) {
  try { humanText = String($('Audio').first().json.text || ''); } catch (e) { humanText = ''; }
  if (!humanText) { try { humanText = String($('Text').first().json.text || ''); } catch (e) { humanText = ''; } }
  if (!humanText) { try { humanText = String($('Latest+Combine').first().json.combinedText || ''); } catch (e) { humanText = ''; } }
  if (!humanText) { try { const m = $('Webhook').first().json.body.messages[0]; humanText = ['chat', 'text'].includes(String(m.type)) ? String(m.body || '') : '[' + String(m.type || 'media') + ']'; } catch (e) { humanText = ''; } }
}
return [{ json: { outcome, historyWatermark: watermark, dropAiHistory: generated && aiText !== null, aiText: aiText || '', recordHuman,
                  humanText: humanText.slice(0, 4000) }, pairedItem: { item: 0 } }];
"""

READ_MODE_SQL = """select (
  coalesce(
    (select suppressed from reply_mode_flags
     where profile_id = $1 and chat_id in ($2, '*')
     order by (chat_id = '*')   -- specific chat_id sorts before the '*' default
     limit 1),
    false)
  -- A5: a chat with a pending handoff is paused until the owner acts (done / explicit «Авто» for the chat)
  or coalesce((select handoff_paused from chat_reply_state where profile_id = $1 and chat_id = $2), false)
) as suppressed;"""

TOOL_RULE = ("\n\n##create_handoff##\n"
             "Передача владельцу — это ДЕЙСТВИЕ, не фраза. Когда клиент готов к заказу/брони, просит связаться, или вопрос надо передать человеку — "
             "вызови инструмент create_handoff (reason: order | question | escalation; name и phone — если клиент их назвал; summary — суть одной-двумя фразами). "
             "Ответ ok:true — только тогда говори «Передал владельцу, он свяжется». Ответ ok:false или ошибка — не обещай передачу: скажи, что не удалось передать автоматически, "
             "и предложи контакт из данных бизнеса. Никогда не утверждай, что владелец уже увидел или в курсе.")

FROM_AI = {
    "reason": "={{ $fromAI('reason', 'Причина: order — заказ, бронь или покупка; question — вопрос, на который нет ответа в данных бизнеса; escalation — жалоба, спор, срочное', 'string', 'question') }}",
    "name": "={{ $fromAI('name', 'Имя клиента, если он его назвал; иначе пустая строка', 'string', '') }}",
    "phone": "={{ $fromAI('phone', 'Телефон клиента, если он его назвал; иначе пустая строка', 'string', '') }}",
    "summary": "={{ $fromAI('summary', 'Суть заявки одной-двумя фразами: что нужно клиенту и какие детали уже известны', 'string') }}",
}
TOOL_INPUTS = {"profile_id": "={{ " + M + ".profile_id }}", "chat_id": "={{ " + M + ".from }}", "source_message_id": "={{ " + M + ".id }}", **FROM_AI}
TOOL_DESCRIPTION = ("Передать заявку владельцу бизнеса: создаёт запись заявки и уведомление владельцу, ставит чат на паузу автоответов. "
                    "Вызывай, когда клиент готов к заказу или брони, просит связаться, или вопрос нужно передать человеку. "
                    "Возвращает {ok:true} — тогда можно сказать клиенту «Передал владельцу»; {ok:false} — передачу не обещай.")


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "history-handoff:" + ":".join(parts)))


def with_memory_marks(sm):
    """The template prompt with exactly one `##memory_marks##` block, the current text, at the very end."""
    head = "\n\n##memory_marks##\n"
    i = sm.find(head)
    if i >= 0:
        j = sm.find("\n\n##", i + len(head))
        sm = sm[:i] + (sm[j:] if j >= 0 else "")
    return sm.rstrip() + MEMORY_MARKS_RULE


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def ensure(wf, spec):
    existing = node(wf, spec["name"])
    if existing is None:
        wf["nodes"].append(spec)
        return True
    changed = False
    for k in ("parameters", "type", "typeVersion", "credentials", "alwaysOutputData", "onError", "retryOnFail", "maxTries", "waitBetweenTries"):
        if k in spec and existing.get(k) != spec[k]:
            existing[k] = spec[k]
            changed = True
    return changed


def set_edges(wf, src, outputs, kind="main"):
    want = [[{"node": t, "type": kind, "index": 0} for t in outs] for outs in outputs]
    cur = wf["connections"].setdefault(src, {})
    if cur.get(kind) != want:
        cur[kind] = want
        return True
    return False


def set_query(n, sql, params=None):
    ch = False
    if n["parameters"].get("query") != sql:
        n["parameters"]["query"] = sql
        ch = True
    if params is not None and n["parameters"]["options"].get("queryReplacement") != params:
        n["parameters"]["options"]["queryReplacement"] = params
        ch = True
    return ch


def pg(name, query, params, pos, fname):
    return {"parameters": {"operation": "executeQuery", "query": query, "options": {"queryReplacement": params}},
            "id": nid(fname, name), "name": name, "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": pos,
            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"}


def if_true(name, expr, pos, fname):
    return {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                          "conditions": [{"id": nid(fname, name, "cond"), "leftValue": expr, "rightValue": "",
                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                          "combinator": "and"}, "options": {}},
            "id": nid(fname, name), "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": pos}


def tool_node(fname):
    schema = [{"id": k, "displayName": k, "required": False, "defaultMatch": False, "display": True, "canBeUsedToMatch": True, "type": "string", "removed": False}
              for k in TOOL_INPUTS]
    return {"parameters": {"description": TOOL_DESCRIPTION, "workflowId": {"__rl": True, "value": "crHandoffReq0001", "mode": "id"},
                           "workflowInputs": {"mappingMode": "defineBelow", "value": dict(TOOL_INPUTS), "matchingColumns": [], "schema": schema,
                                              "attemptToConvertTypes": False, "convertFieldsToString": False}},
            "id": nid(fname, "Create Handoff"), "name": "Create Handoff", "type": "@n8n/n8n-nodes-langchain.toolWorkflow", "typeVersion": 2.2,
            "position": [-1900, 200]}


def patch_bot(fname):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    # (1) event routing before the group/binding filter
    ch |= ensure(wf, if_true("Is Incoming?", "={{ Array.isArray($json.body && $json.body.messages) && !!$json.body.messages[0] && (!$json.body.messages[0].wh_type || $json.body.messages[0].wh_type === 'incoming_message') }}", [-4400, 400], fname))
    ch |= ensure(wf, if_true("Is Outgoing?", "={{ Array.isArray($json.body && $json.body.messages) && !!$json.body.messages[0] && ['outgoing_message_phone', 'outgoing_message_api'].includes(String($json.body.messages[0].wh_type)) }}", [-4200, 700], fname))
    ch |= ensure(wf, if_true("Outgoing Bound?", "={{ String($json.webhookUrl || '').split('/').pop() === String($json.body.messages[0].profile_id) }}", [-4000, 700], fname))
    ch |= ensure(wf, pg("Record Outgoing", RECORD_OUTGOING_SQL, RECORD_OUTGOING_PARAMS, [-3800, 700], fname))
    ch |= ensure(wf, pg("Record Suppressed", RECORD_SUPPRESSED_SQL, RECORD_SUPPRESSED_PARAMS, [-3560, 700], fname))
    ch |= set_edges(wf, "Webhook", [["Is Incoming?"]])
    ch |= set_edges(wf, "Is Incoming?", [["If"], ["Is Outgoing?"]])
    ch |= set_edges(wf, "Is Outgoing?", [["Outgoing Bound?"], ["Outcome"]])
    ch |= set_edges(wf, "Outgoing Bound?", [["Record Outgoing"], ["Outcome"]])
    ch |= set_edges(wf, "Record Outgoing", [["Outcome"]])
    ch |= set_edges(wf, "Suppressed?", [["Record Suppressed"], ["Claim Event"]])
    ch |= set_edges(wf, "Record Suppressed", [["Outcome"]])
    # (2) the one history: claim / send / cancel statements
    ch |= set_query(node(wf, "Claim Event"), CLAIM_SQL, CLAIM_PARAMS)
    # V6: the agent's memory row is tied to this execution right after the agent (appended node; overrides stage 3's
    # Generated? → Pause Before Reading edge, stage 3 → stage 4 order)
    ch |= ensure(wf, pg("Stamp Draft", STAMP_DRAFT_SQL, STAMP_DRAFT_PARAMS, [-2000, -360], fname))
    ch |= set_edges(wf, "Generated?", [["Stamp Draft"], ["Cancel Label"]])
    ch |= set_edges(wf, "Stamp Draft", [["Pause Before Reading"]])
    rs = node(wf, "Record Send")
    head = rs["parameters"]["options"]["queryReplacement"].split(", $json.state")[0]
    ch |= set_query(rs, RECORD_SEND_SQL, head + RECORD_SEND_PARAMS_TAIL)
    rc = node(wf, "Record Cancel")
    head = rc["parameters"]["options"]["queryReplacement"].split(", $json.outcome")[0]
    ch |= set_query(rc, RECORD_CANCEL_SQL, head + RECORD_CANCEL_PARAMS_TAIL)
    cl = node(wf, "Cancel Label")
    if cl["parameters"]["jsCode"] != CANCEL_LABEL_JS:
        cl["parameters"]["jsCode"] = CANCEL_LABEL_JS
        ch = True
    # (3) handoff pause in the mode gate
    ch |= set_query(node(wf, "Read Reply Mode"), READ_MODE_SQL)
    # (4) the tool + the rule
    ch |= ensure(wf, tool_node(fname))
    ch |= set_edges(wf, "Create Handoff", [["AI Agent"]], kind="ai_tool")
    ai = node(wf, "AI Agent")
    sm = ai["parameters"]["options"]["systemMessage"]
    if "##create_handoff##" not in sm:
        ai["parameters"]["options"]["systemMessage"] = sm.rstrip() + TOOL_RULE
        ch = True
    # T6: the memory-marks rule for a bot that carries the TEMPLATE's own prompt (an empty or legacy business type —
    # no _core.md there); always the LAST block, replaced in place when the text changes
    sm = ai["parameters"]["options"]["systemMessage"]
    want = with_memory_marks(sm)
    if sm != want:
        ai["parameters"]["options"]["systemMessage"] = want
        ch = True
    if ch:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return ch


WEBHOOK_TYPES = "[\n  \"incoming_message\",\n  \"outgoing_message_phone\",\n  \"outgoing_message_api\"\n]"


def patch_create(fname):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    n = node(wf, "Set Wappi Webhook Types")
    if n["parameters"].get("jsonBody") == WEBHOOK_TYPES:
        return False
    n["parameters"]["jsonBody"] = WEBHOOK_TYPES
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    return True


TRANSCRIPT_OLD = "(CASE WHEN m.message->>'type' = 'ai' THEN 'Бот: ' ELSE 'Клиент: ' END)"
TRANSCRIPT_NEW = "(CASE WHEN m.message->>'type' = 'ai' THEN (CASE WHEN m.message->'additional_kwargs'->>'source' LIKE 'owner%' THEN 'Владелец: ' ELSE 'Бот: ' END) ELSE 'Клиент: ' END)"
# T6: the transcript labels a row by its source, so the memory marks come off the content (no «Владелец: Владелец:», no
# «[доставка не подтверждена]» in a «Бот:» line).
CONTENT_OLD = "coalesce(m.message->>'content', m.message->'data'->>'content', '')"
CONTENT_NEW = "regexp_replace(" + CONTENT_OLD + ", " + MARKS_LEAD_SQL + ", '')"
FETCH_OUTCOMES_SQL = """-- «Сводка» reads ONE source: the classifier's verdict per session, outranked by a REAL event — an
-- open/taken handoff (order → order_collected, question/escalation → owner_needed), marked
-- source='event' so the app can tell a fact from a classification.
WITH ids AS (
  SELECT jsonb_array_elements_text(convert_from(decode($1, 'base64'), 'UTF8')::jsonb) AS profile_id
), o AS (
  SELECT c.* FROM public.conversation_outcomes c WHERE c.profile_id IN (SELECT profile_id FROM ids)
), h AS (
  SELECT DISTINCT ON (r.profile_id, r.chat_id) r.*
  FROM public.handoff_requests r
  WHERE r.profile_id IN (SELECT profile_id FROM ids) AND r.status <> 'done'
  ORDER BY r.profile_id, r.chat_id, r.created_at DESC
)
SELECT coalesce(o.profile_id, h.profile_id) AS "profileId",
       coalesce(o.chat_id, h.chat_id)       AS "chatId",
       CASE WHEN h.id IS NOT NULL THEN (CASE WHEN h.reason = 'order' THEN 'order_collected' ELSE 'owner_needed' END) ELSE o.outcome END AS outcome,
       coalesce(h.summary, o.summary)       AS summary,
       (extract(epoch from coalesce(h.created_at, o.outcome_at)) * 1000)::bigint      AS "outcomeAt",
       (extract(epoch from coalesce(o.last_message_at, h.created_at)) * 1000)::bigint AS "lastMessageAt",
       CASE WHEN h.id IS NOT NULL THEN 'event' ELSE 'classifier' END AS source,
       h.id     AS "handoffId",
       h.status AS "handoffStatus",
       h.reason AS "handoffReason"
FROM o
FULL OUTER JOIN h ON h.profile_id = o.profile_id AND h.chat_id = o.chat_id
ORDER BY coalesce(o.last_message_at, h.created_at) DESC;"""


def patch_dashboard():
    path = os.path.join(WF, DASHBOARD)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    fc = node(wf, "Find Changed Sessions")
    if TRANSCRIPT_OLD in fc["parameters"]["query"]:
        fc["parameters"]["query"] = fc["parameters"]["query"].replace(TRANSCRIPT_OLD, TRANSCRIPT_NEW)
        ch = True
    if CONTENT_NEW not in fc["parameters"]["query"]:
        assert fc["parameters"]["query"].count(CONTENT_OLD) == 1, "Find Changed Sessions has an unexpected transcript shape"
        fc["parameters"]["query"] = fc["parameters"]["query"].replace(CONTENT_OLD, CONTENT_NEW)
        ch = True
    fo = node(wf, "Fetch Outcomes")
    if fo["parameters"]["query"] != FETCH_OUTCOMES_SQL:
        fo["parameters"]["query"] = FETCH_OUTCOMES_SQL
        ch = True
    if ch:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return ch


DELETE_OLD_TAIL = """), flags AS (
  DELETE FROM public.reply_mode_flags f
  WHERE f.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
)
SELECT (SELECT count(*)::int FROM hist)     AS "deletedMemoryRows",
       (SELECT count(*)::int FROM outcomes) AS "deletedOutcomes",
       (SELECT count(*)::int FROM flags)    AS "deletedReplyFlags";"""
DELETE_NEW_TAIL = """), flags AS (
  DELETE FROM public.reply_mode_flags f
  WHERE f.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
), hist_events AS (   -- stage 4: the one history
  DELETE FROM public.chat_history_events e
  WHERE e.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
), handoffs AS (      -- stage 4: the bot's requests (and their pending notices)
  DELETE FROM public.handoff_requests r
  WHERE r.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
), events AS (        -- stage 3: the event ledger + per-chat state
  DELETE FROM public.reply_events r
  WHERE r.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
), states AS (
  DELETE FROM public.chat_reply_state s
  WHERE s.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
)
SELECT (SELECT count(*)::int FROM hist)        AS "deletedMemoryRows",
       (SELECT count(*)::int FROM outcomes)    AS "deletedOutcomes",
       (SELECT count(*)::int FROM flags)       AS "deletedReplyFlags",
       (SELECT count(*)::int FROM hist_events) AS "deletedHistoryEvents",
       (SELECT count(*)::int FROM handoffs)    AS "deletedHandoffs",
       (SELECT count(*)::int FROM events)      AS "deletedReplyEvents",
       (SELECT count(*)::int FROM states)      AS "deletedChatStates";"""


# T5 (F1 + F2, plan 2026-10-11 §1.6): the deleted bot's memory goes with its WHOLE line — the earlier profiles of a
# re-paired / reconnected bot (the pre-F1 orphans, a superseded predecessor, the other channel's retired row) — not only
# the two profile ids the app names. The head is clone_lineage's (the same fragment and parameters as Delete Bot Files'
# other deleting nodes, apply-auth-gate.py); this file keeps owning the deletes (the seven tables, DELETE_NEW_TAIL).
DELETE_CHAT_MEMORY_SQL = """-- Privacy deletion (2026-09-01, store audit / Data Safety «можно запросить удаление»), by the bot's line (T5):
-- a deleted bot's server-side conversation memory goes with it — under every profile of its line (session: this
-- install's rows of the same bot key, both channels, and the superseded chain; legacy: the named ids only). Chat
-- transcripts key off session_id = '<profileId>:<chatId>' (both bot templates' Chat Memory nodes); the history,
-- outcomes, handoffs, reply ledger, chat state and flags carry profile_id directly. Sentinels ('-1', '', 'null',
-- 'undefined') are no ids. dialog_counts stay UNTOUCHED on purpose: they are the owner's billing usage records
-- (Task 17a moves them with the money on transfer), not chat content.
-- Parameters, in order: gate mode, install id, JSON array of the profile ids, JSON array of the workflow ids.
""" + cl.DBF_LINE_HEAD + """, valid AS (
  SELECT profile_id FROM lineage_profiles
), hist AS (
  DELETE FROM public.n8n_chat_histories h
  WHERE split_part(h.session_id, ':', 1) IN (SELECT profile_id FROM valid)
  RETURNING 1
), outcomes AS (
  DELETE FROM public.conversation_outcomes c
  WHERE c.profile_id IN (SELECT profile_id FROM valid)
  RETURNING 1
""" + DELETE_NEW_TAIL
DELETE_CHAT_MEMORY_PARAMS = cl.DBF_LINE_PARAMS


RESPOND_OLD_TAIL = '"deletedReplyFlags": $json.deletedReplyFlags ?? null } }}'
RESPOND_NEW_TAIL = ('"deletedReplyFlags": $json.deletedReplyFlags ?? null, "deletedHistoryEvents": $json.deletedHistoryEvents ?? null, '
                    '"deletedHandoffs": $json.deletedHandoffs ?? null, "deletedReplyEvents": $json.deletedReplyEvents ?? null, '
                    '"deletedChatStates": $json.deletedChatStates ?? null } }}')


def patch_delete_bot():
    path = os.path.join(WF, DELETE_BOT)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    n = node(wf, "Delete Chat Memory")
    q = n["parameters"]["query"]
    if q != DELETE_CHAT_MEMORY_SQL:
        assert DELETE_OLD_TAIL in q or DELETE_NEW_TAIL in q, "Delete Chat Memory has an unexpected shape"
        n["parameters"]["query"] = DELETE_CHAT_MEMORY_SQL
        ch = True
    if n["parameters"].setdefault("options", {}).get("queryReplacement") != DELETE_CHAT_MEMORY_PARAMS:
        n["parameters"]["options"]["queryReplacement"] = DELETE_CHAT_MEMORY_PARAMS
        ch = True
    r = node(wf, "Respond")
    body = r["parameters"]["responseBody"]
    if RESPOND_OLD_TAIL in body:
        r["parameters"]["responseBody"] = body.replace(RESPOND_OLD_TAIL, RESPOND_NEW_TAIL)
        ch = True
    else:
        assert RESPOND_NEW_TAIL in body, "Delete Bot Files Respond has an unexpected shape"
    if ch:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return ch


def patch_set_state():
    path = os.path.join(WF, SET_STATE)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    for n in wf["nodes"]:
        js = n["parameters"].get("jsCode", "")
        if "const PROTECTED = new Set([" in js and "'crHandoffReq0001'" not in js:
            n["parameters"]["jsCode"] = js.replace("  'crWappiProxy0001'\n]);", "  'crWappiProxy0001',\n  'crHandoffReq0001',\n  'crHandoffNtfy001',\n  'crHandoffsApi001'\n]);")
            assert "'crHandoffReq0001'" in n["parameters"]["jsCode"], "Set Workflow State protected list has an unexpected shape"
            ch = True
    if ch:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return ch


def check():
    bad = []
    for f in BOTS:
        wf = json.load(open(os.path.join(WF, f), encoding="utf-8"))
        ok = all(node(wf, n) for n in ("Is Incoming?", "Is Outgoing?", "Outgoing Bound?", "Record Outgoing", "Record Suppressed", "Create Handoff", "Stamp Draft")) \
            and node(wf, "Stamp Draft")["parameters"]["query"] == STAMP_DRAFT_SQL \
            and node(wf, "Stamp Draft")["parameters"]["options"]["queryReplacement"] == STAMP_DRAFT_PARAMS \
            and wf["connections"].get("Generated?", {}).get("main", [[]])[0] == [{"node": "Stamp Draft", "type": "main", "index": 0}] \
            and node(wf, "Claim Event")["parameters"]["query"] == CLAIM_SQL and node(wf, "Record Send")["parameters"]["query"] == RECORD_SEND_SQL \
            and node(wf, "Record Outgoing")["parameters"]["query"] == RECORD_OUTGOING_SQL \
            and node(wf, "Record Suppressed")["parameters"]["query"] == RECORD_SUPPRESSED_SQL \
            and node(wf, "Record Cancel")["parameters"]["query"] == RECORD_CANCEL_SQL and node(wf, "Read Reply Mode")["parameters"]["query"] == READ_MODE_SQL \
            and "##create_handoff##" in node(wf, "AI Agent")["parameters"]["options"]["systemMessage"] \
            and node(wf, "AI Agent")["parameters"]["options"]["systemMessage"].endswith(MEMORY_MARKS_RULE) \
            and with_memory_marks(node(wf, "AI Agent")["parameters"]["options"]["systemMessage"]) == node(wf, "AI Agent")["parameters"]["options"]["systemMessage"] \
            and wf["connections"].get("Create Handoff", {}).get("ai_tool") == [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]]
        if not ok:
            bad.append(f)
    for f in CREATES:
        wf = json.load(open(os.path.join(WF, f), encoding="utf-8"))
        if node(wf, "Set Wappi Webhook Types")["parameters"].get("jsonBody") != WEBHOOK_TYPES:
            bad.append(f)
    wf = json.load(open(os.path.join(WF, DASHBOARD), encoding="utf-8"))
    if node(wf, "Fetch Outcomes")["parameters"]["query"] != FETCH_OUTCOMES_SQL or TRANSCRIPT_NEW not in node(wf, "Find Changed Sessions")["parameters"]["query"] \
            or CONTENT_NEW not in node(wf, "Find Changed Sessions")["parameters"]["query"]:
        bad.append(DASHBOARD)
    wf = json.load(open(os.path.join(WF, DELETE_BOT), encoding="utf-8"))
    dcm = node(wf, "Delete Chat Memory")["parameters"]
    if dcm["query"] != DELETE_CHAT_MEMORY_SQL or (dcm.get("options") or {}).get("queryReplacement") != DELETE_CHAT_MEMORY_PARAMS \
            or RESPOND_NEW_TAIL not in node(wf, "Respond")["parameters"]["responseBody"]:
        bad.append(DELETE_BOT)
    wf = json.load(open(os.path.join(WF, SET_STATE), encoding="utf-8"))
    if not any("'crHandoffsApi001'" in n["parameters"].get("jsCode", "") for n in wf["nodes"]):
        bad.append(SET_STATE)
    return bad


def main():
    if "--check" in sys.argv:
        bad = check()
        print("history/handoff:", "OK" if not bad else f"MISSING/STALE in {bad}")
        sys.exit(2 if bad else 0)
    for f in BOTS:
        print(f"{'patched' if patch_bot(f) else 'up to date'}  {f}")
    for f in CREATES:
        print(f"{'patched' if patch_create(f) else 'up to date'}  {f}")
    print(f"{'patched' if patch_dashboard() else 'up to date'}  {DASHBOARD}")
    print(f"{'patched' if patch_delete_bot() else 'up to date'}  {DELETE_BOT}")
    print(f"{'patched' if patch_set_state() else 'up to date'}  {SET_STATE}")


if __name__ == "__main__":
    main()
