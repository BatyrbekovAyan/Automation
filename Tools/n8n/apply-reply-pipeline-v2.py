#!/usr/bin/env python3
"""Idempotent migration (A3/A4/A7, audit stage 3): once-only event processing, a final check right
before the send, honest send states, and non-blocking decorative actions — on BOTH bot templates.

Chain after this migration (the pre-existing nodes keep their names; new ones are marked *):
  If → Read Reply Mode → Suppressed? →[reply] *Claim Event → *Claimed? →[new] Debounce Wait → Fetch Recent
  → Latest+Combine (now also aborts on an OWNER row newer than the trigger, stable time+order sort)
  → Is Latest? →[winner] Count Dialog → Quota Decision → If Quota Allows → Input type → … → AI Agent
  → *Generated? → Pause Before Reading → Mark Read (fail-open) → … → Typing (fail-open) → … → Retrieve Answer
  → *Final Fetch → *Final Check (newer client msg → superseded; owner replied → cancelled_owner; trigger
  missing from the fetch → cancelled_fetch) → *Final OK? → *Acquire Send Lease (ONE statement: current mode,
  incoming generation, 90-s per-chat lease, event → send_pending; V5: a claimed SAME-second message missing from
  the snapshot refuses it) → *Peer Recheck? →[once] *Wait Peer Recheck → *Recheck Fetch → Final Check (again)
  | → *Send Allowed? → HTTP Request (send,
  fullResponse, never throws) → *Settle Send (sent | send_failed | send_unknown) → [*Send Unknown? → *Wait
  Reconcile → *Reconcile Fetch → *Reconcile Check (echo found ⇒ sent, else stays send_unknown, NEVER a
  blind resend)] → *Record Send → Outcome.  Every refusal/cancel → *Cancel Label → *Record Cancel (event
  state + drop the AI history rows a cancelled generation wrote) → Outcome.
Pauses are clamped (reading ≤ 8 s, typing ≤ 12 s, listening ≤ 12 s).

    python3 Tools/n8n/apply-reply-pipeline-v2.py            # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-reply-pipeline-v2.py --check
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))


def _module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_").replace(".py", ""), os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m
WF = os.path.join(HERE, "workflows")
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
WAPPI_CRED = {"id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken"}
BOTS = {"4wYitz5ek30SVNlT-WhatsApp_Bot.json": "https://wappi.pro/api/sync/",
        "4VN3gsFaC2HUYmcc-Telegram_Bot.json": "https://wappi.pro/tapi/sync/"}
LEASE_SECONDS = 90
TRIG = "$('Webhook').first().json.body.messages[0]"
FETCH_LIMIT = 15        # rows messages/get returns (Fetch Recent / Final Fetch / Reconcile Fetch / Recheck Fetch)
# V5 (iteration 2026-09-28): the re-read the lease asks for when a same-second peer is missing from the snapshot.
# The wait outlasts the PEER's own path to its Latest+Combine verdict — debounce (the batching generator's constant)
# + Fetch Recent + one Postgres node (≈1.9 s on prod) — so by the second lease the peer's ledger state already says
# whether it will answer (alive ⇒ A refuses) or stopped without a reply (A sends). The re-read is ONE short fetch
# without retries (an error ⇒ cancelled_fetch): the execution timeout (120 s on prod) must survive a slow Wappi.
DEBOUNCE_SECONDS = _module("apply-message-batching.py").DEBOUNCE_SECONDS
FETCH_RECENT_BUDGET_SECONDS = 2
PG_NODE_BUDGET_SECONDS = 2
PEER_RECHECK_SECONDS = DEBOUNCE_SECONDS + FETCH_RECENT_BUDGET_SECONDS + PG_NODE_BUDGET_SECONDS
RECHECK_FETCH_TIMEOUT_MS = 10000

# SUPERSEDED COPIES: CLAIM_SQL / RECORD_SEND_SQL / RECORD_CANCEL_SQL / CANCEL_LABEL_JS below only seed a template that
# does not have the nodes yet. Once stage 4 re-shaped them (STAGE4_OWNED, ensure() skips them), the source of truth
# is apply-history-handoff.py — fix Claim / Record Send / Record Cancel / Cancel Label THERE (R6/R7, 2026-09-27).
CLAIM_SQL = """-- A4: the atomic right to process THIS event. A re-delivered event finds its row and is refused
-- (claimed=false → duplicate); a claim left behind by a crashed run (>10 min, no send started) may be
-- re-claimed. Each claim bumps the chat's incoming generation — the final check compares against it.
-- history_watermark lets a cancelled generation's AI rows be dropped from memory (A3.7).
with ev as (
  insert into reply_events (profile_id, chat_id, message_id, execution_id, state)
  values ($1, $2, $3, $4, 'claimed')
  on conflict (profile_id, chat_id, message_id) do update
    set state = 'claimed', execution_id = excluded.execution_id, updated_at = now()
    where reply_events.state in ('claimed', 'generated') and reply_events.updated_at < now() - interval '10 minutes'
  returning message_id
), st as (
  insert into chat_reply_state (profile_id, chat_id, incoming_generation)
  select $1, $2, 1 where exists (select 1 from ev)
  on conflict (profile_id, chat_id) do update set incoming_generation = chat_reply_state.incoming_generation + 1
  returning incoming_generation
)
select exists(select 1 from ev) as claimed,
       coalesce((select incoming_generation from st), (select incoming_generation from chat_reply_state where profile_id = $1 and chat_id = $2), 0)::bigint as generation,
       coalesce((select max(id) from n8n_chat_histories where session_id = $1 || ':' || $2), 0)::bigint as history_watermark;"""

ACQUIRE_SQL = """-- A3/A4: the last word before the transport call, in ONE statement: the CURRENT mode (a «Вместе» flip
-- during generation cancels), the incoming generation captured at claim time (a newer claimed event
-- supersedes), and a 90-s per-chat lease (two runs of the same chat cannot send at once; other chats are
-- untouched). Only a run that holds the lease marks its event send_pending. Nothing here spans the LLM or
-- the transport call — the lease is what covers the send itself.
-- V5: a client message of the trigger's OWN second, claimed by its own run but absent from the snapshot the
-- Final Check decided on, is seen by neither freshness signal (the generation counts strictly newer seconds,
-- the snapshot's order only orders the rows it holds). It refuses the lease (peer_unseen) — only when that
-- snapshot was complete for the second (a full page of one second proves nothing), never for a strictly newer
-- or older second, never for a peer whose run already ended without a reply. On the first pass the run re-reads
-- the chat once (recheck); on the second the refusal stands. No lock is taken while that happens.
with mode as (
  select coalesce((select suppressed from reply_mode_flags
                   where profile_id = $1 and chat_id in ($2, '*')
                   order by (chat_id = '*') limit 1), false) as suppressed
), cur as (
  select incoming_generation, lock_token, lock_expires_at from chat_reply_state where profile_id = $1 and chat_id = $2
), peer as (
  select exists (
    select 1 from reply_events r
    join chat_history_events h on h.profile_id = r.profile_id and h.chat_id = r.chat_id
                              and h.message_id = r.message_id and h.source = 'client'
    where r.profile_id = $1 and r.chat_id = $2 and r.message_id <> $3
      and r.state in ('claimed', 'generated', 'send_pending', 'sent', 'send_unknown')
      and $9::boolean and $7::bigint > 0 and h.sent_at = to_timestamp($7::bigint)
      and not exists (select 1 from jsonb_array_elements_text($6::jsonb) s(id) where s.id = r.message_id)
  ) as unseen
), lease as (
  update chat_reply_state c set lock_token = $5, lock_expires_at = now() + interval '__LEASE__ seconds'
  where c.profile_id = $1 and c.chat_id = $2
    and (c.lock_token is null or c.lock_expires_at < now() or c.lock_token = $5)
    and c.incoming_generation = $4::bigint
    and not (select suppressed from mode)
    and not (select unseen from peer)
  returning 1
), ev as (
  update reply_events set state = 'send_pending', updated_at = now()
  where profile_id = $1 and chat_id = $2 and message_id = $3 and exists (select 1 from lease)
  returning 1
)
select exists(select 1 from lease) as allowed,
       (select suppressed from mode) as suppressed,
       (select incoming_generation from cur)::bigint as generation_now,
       (select lock_token from cur) is not null and (select lock_expires_at from cur) > now() and (select lock_token from cur) <> $5 as lock_busy,
       (select unseen from peer) as peer_unseen,
       (select unseen from peer) and not $8::boolean and not (select suppressed from mode)
         and coalesce((select incoming_generation from cur) = $4::bigint, false)
         and not coalesce((select lock_token from cur) is not null and (select lock_expires_at from cur) > now()
                          and (select lock_token from cur) <> $5, false) as recheck;""".replace("__LEASE__", str(LEASE_SECONDS))
# $6 = the ids the Final Check saw, $7 = the trigger's own Wappi time (0 = unknown → the rule is off), $8 = this is
# the pass after the one re-read, $9 = that snapshot was complete for the trigger's second. Every element always
# has a value: n8n drops an undefined one and shifts every later placeholder.
ACQUIRE_PARAMS_TAIL = (", $json.generation, $execution.id, JSON.stringify($json.seenIds || []), Number(" + TRIG + ".time) || 0, "
                       "$json.recheck === true, $json.seenComplete === true ] }}")

RECORD_SEND_SQL = """-- A7: the event's final transport state and the lease release. send_unknown stays send_unknown —
-- an operator-visible uncertainty, never a silent retry.
with ev as (
  update reply_events set state = $4, transport_message_id = nullif($5, ''), error = nullif($6, ''), updated_at = now()
  where profile_id = $1 and chat_id = $2 and message_id = $3 returning 1
), rel as (
  update chat_reply_state set lock_token = null, lock_expires_at = null,
    last_sent_at = case when $4 = 'sent' then now() else last_sent_at end,
    last_sent_message_id = case when $4 = 'sent' then nullif($5, '') else last_sent_message_id end
  where profile_id = $1 and chat_id = $2 and lock_token = $7 returning 1
)
select $4::text as outcome, nullif($5, '')::text as transport_message_id, exists(select 1 from ev) as recorded, exists(select 1 from rel) as released;"""

RECORD_CANCEL_SQL = """-- A3.7: a refused / cancelled reply is recorded as such, the lease (if this run held it) is released, and
-- the AI rows a cancelled GENERATION already wrote to memory are dropped — the customer never saw them,
-- so the bot must not remember having said them.
with ev as (
  update reply_events set state = $4, updated_at = now()
  where profile_id = $1 and chat_id = $2 and message_id = $3 returning 1
), hist as (
  delete from n8n_chat_histories
  where $6::boolean and session_id = $1 || ':' || $2 and id > $5::bigint and message->>'type' = 'ai'
    and (message->'additional_kwargs'->>'source') is null returning 1
), rel as (
  update chat_reply_state set lock_token = null, lock_expires_at = null
  where profile_id = $1 and chat_id = $2 and lock_token = $7 returning 1
)
select $4::text as outcome, (select count(*) from hist)::int as dropped_ai_rows, exists(select 1 from ev) as recorded;"""

FINAL_CHECK_JS = r"""// FINAL CHECK (A3, stage 3): re-read the chat RIGHT BEFORE sending. Same rules as Latest+Combine, on a
// fresh snapshot: a client message newer than the trigger → this text is stale (superseded — the newer
// event's own run answers); an owner message newer than the trigger → the owner took over (cancelled).
// A snapshot that does not even contain the trigger (crossed/short fetch) is never sent on.
const wh = $('Webhook').first().json;
const trig = wh.body.messages[0];
let claim = {};
try { claim = $('Claim Event').first().json || {}; } catch (e) { claim = {}; }
let text = '';
try { text = String($('Retrieve Answer').first().json.text || ''); } catch (e) {
  try { text = String($('Ask to Send Text').first().json.text || ''); } catch (e2) { text = ''; }
}
const fetchedAll = ($json.messages || []).slice();
const fetched = fetchedAll.filter(m => !(m && m.chatId && trig.chatId && m.chatId !== trig.chatId));
fetched.forEach((m, i) => { if (m) m.__i = i; });
fetched.sort((a, b) => ((b.time || 0) - (a.time || 0)) || ((a.__i || 0) - (b.__i || 0)));   // newest first, Wappi order breaks ties
const trigIdx = fetched.findIndex(m => m && m.id === trig.id);
let ok = true, outcome = null;
if (!text.trim()) { ok = false; outcome = 'generation_failed'; }
else if (trigIdx < 0) { ok = false; outcome = 'cancelled_fetch'; }
else if (fetched.slice(0, trigIdx).some(m => m && m.fromMe === false)) { ok = false; outcome = 'superseded'; }
else if (fetched.slice(0, trigIdx).some(m => m && m.fromMe === true)) { ok = false; outcome = 'cancelled_owner'; }
// V5 (iteration 2026-09-28): what this snapshot SAW, for the lease's same-second peer rule. The rule needs a
// snapshot COMPLETE for the trigger's second: messages/get returns at most __LIMIT__ rows, so a full page whose every
// row is at or after that second (an album of more photos than fit) proves nothing about a row it lacks.
const seenIds = fetched.filter(m => m && m.id !== undefined && m.id !== null).map(m => String(m.id));
const seenComplete = fetchedAll.length < __LIMIT__ || fetched.some(m => m && Number(m.time || 0) > 0 && Number(m.time || 0) < Number(trig.time || 0));
// The pass AFTER the lease asked for its one re-read (on the first pass the lease has not run: the reference throws).
let recheck = false;
try { recheck = $('Acquire Send Lease').first().json.peer_unseen === true; } catch (e) { recheck = false; }
return [{ json: { ok, outcome, text, generation: Number(claim.generation) || 0, historyWatermark: Number(claim.history_watermark) || 0,
                  sendStartedAt: Math.floor(Date.now() / 1000), seenIds, seenComplete, recheck }, pairedItem: { item: 0 } }];
""".replace("__LIMIT__", str(FETCH_LIMIT))

SETTLE_JS = r"""// SETTLE SEND (A7): classify the transport call — never assume delivery from a missing answer.
const r = $input.first().json || {};
const body = (r.body && typeof r.body === 'object') ? r.body : {};
const status = Number(r.statusCode) || 0;
let state, transportMessageId = '', error = '';
if (!status && r.error) { state = 'send_unknown'; error = String(r.error.message || r.error).slice(0, 300); }
else if (status === 200 && (body.status === 'done' || body.message_id)) { state = 'sent'; transportMessageId = String(body.message_id || ''); }
else if (status >= 400 || body.status === 'error') { state = 'send_failed'; error = ('http ' + status + ' ' + JSON.stringify(body)).slice(0, 300); }
else { state = 'send_unknown'; error = ('unexpected ' + JSON.stringify(r)).slice(0, 300); }
const fc = $('Final Check').first().json;
return [{ json: { state, unknown: state === 'send_unknown', transportMessageId, error, text: fc.text, sendStartedAt: fc.sendStartedAt }, pairedItem: { item: 0 } }];
"""

RECONCILE_JS = r"""// RECONCILE (A7): the transport may have accepted the message although its answer was lost. Look for
// OUR text as an outgoing row after the send started; found ⇒ sent (adopt its id), else the state stays
// send_unknown for the owner to see. Never resend here.
// R10: Final Check's rules, stricter — here a false «sent» hides a lost reply. Rows naming a FOREIGN chat are
// dropped (Wappi/tapi cross concurrent responses); the window must hold the TRIGGER (a crossed window without
// chatId fields fails here); the echo must be newer than the trigger in the window's order (time desc, Wappi
// order on ties); a row the transport marks as sent from the phone (tapi from_where) is the owner's, not ours;
// and EXACTLY ONE distinct row may match — none or several (the owner typed the same text) stay send_unknown.
const s = $('Settle Send').first().json;
const trig = $('Webhook').first().json.body.messages[0];
const fetchedAll = ($json.messages || []).filter(m => m && typeof m === 'object');
const fetched = fetchedAll.filter(m => !(m.chatId && trig.chatId && m.chatId !== trig.chatId));
fetched.forEach((m, i) => { m.__i = i; });
fetched.sort((a, b) => ((b.time || 0) - (a.time || 0)) || ((a.__i || 0) - (b.__i || 0)));
const trigIdx = fetched.findIndex(m => m.id === trig.id);
const ours = m => m.fromMe === true && typeof m.body === 'string' && m.from_where !== 'phone'
  && m.body.trim() === String(s.text).trim() && (m.time || 0) >= (Number(s.sendStartedAt) - 5);
const cands = [];
for (const m of (trigIdx < 0 ? [] : fetched.slice(0, trigIdx))) {
  if (ours(m) && !cands.some(x => String(x.id) === String(m.id))) cands.push(m);
}
const hit = cands.length === 1 ? cands[0] : null;
const why = trigIdx < 0 ? 'reconcile: window without the trigger' : (cands.length > 1 ? 'reconcile: ambiguous echo (' + cands.length + ' rows)' : '');
return [{ json: { state: hit ? 'sent' : 'send_unknown', transportMessageId: hit ? String(hit.id || '') : '',
                  error: hit ? '' : [s.error, why].filter(Boolean).join(' | ').slice(0, 300), reconciled: !!hit,
                  foreignFetched: fetchedAll.length - fetched.length }, pairedItem: { item: 0 } }];
"""

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
// Only a run that reached the generator can have written AI memory rows to drop.
const generated = prev === 'Final OK?' || prev === 'Send Allowed?';
return [{ json: { outcome, historyWatermark: watermark, dropAiHistory: generated }, pairedItem: { item: 0 } }];
"""

LATEST_COMBINE_PATCH_OLD = """fetched.sort((a, b) => (b.time || 0) - (a.time || 0));

const isText = (m) => m && (m.type === 'chat' || m.type === 'text');
const incoming = fetched.filter(m => m && m.fromMe === false);
const newestIncoming = incoming[0];

let abort = false;
let combinedText = null;

if (!newestIncoming || newestIncoming.id !== triggeringId) {
  abort = true;
} else if (isText(newestIncoming)) {"""
LATEST_COMBINE_PATCH_NEW = """// Stable newest-first order: time desc, then Wappi's own order (newest first) breaks equal timestamps —
// two rows in the same second no longer shuffle on every fetch (stage 3).
fetched.forEach((m, i) => { if (m) m.__i = i; });
fetched.sort((a, b) => ((b.time || 0) - (a.time || 0)) || ((a.__i || 0) - (b.__i || 0)));

const isText = (m) => m && (m.type === 'chat' || m.type === 'text');
const incoming = fetched.filter(m => m && m.fromMe === false);
const newestIncoming = incoming[0];
// A3 (stage 3): an OWNER row newer than the trigger means the owner already answered (from the phone or
// the app) — an auto-reply on top of it is exactly the double reply the audit reproduced. Abort.
const trigIdx = fetched.findIndex(m => m && m.id === triggeringId);
const ownerReplied = trigIdx >= 0 && fetched.slice(0, trigIdx).some(m => m && m.fromMe === true);

let abort = false;
let combinedText = null;

if (!newestIncoming || newestIncoming.id !== triggeringId || ownerReplied) {
  abort = true;
} else if (isText(newestIncoming)) {"""


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "reply-pipeline-v2:" + ":".join(parts)))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


STAGE4_OWNED = ("Claim Event", "Record Send", "Record Cancel", "Cancel Label")   # apply-history-handoff.py re-shapes these


def ensure(wf, spec):
    existing = node(wf, spec["name"])
    if existing is None:
        wf["nodes"].append(spec)
        return True
    if spec["name"] in STAGE4_OWNED:
        blob = json.dumps(existing.get("parameters", {}), ensure_ascii=False)
        if "chat_history_events" in blob or "recordHuman" in blob:
            return False   # already re-shaped by stage 4 — stage 3 never regresses it
    changed = False
    for k in ("parameters", "type", "typeVersion", "credentials", "alwaysOutputData", "onError", "retryOnFail", "maxTries", "waitBetweenTries"):
        if k in spec and existing.get(k) != spec[k]:
            existing[k] = spec[k]
            changed = True
    return changed


def set_edges(wf, src, outputs):
    """outputs: list (per output index) of lists of target names."""
    want = {"main": [[{"node": t, "type": "main", "index": 0} for t in outs] for outs in outputs]}
    if wf["connections"].get(src) != want:
        wf["connections"][src] = want
        return True
    return False


def pg(name, query, params, pos, fname):
    return {"parameters": {"operation": "executeQuery", "query": query, "options": {"queryReplacement": params}},
            "id": nid(fname, name), "name": name, "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": pos,
            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"}


def code(name, js, pos, fname):
    return {"parameters": {"jsCode": js}, "id": nid(fname, name), "name": name, "type": "n8n-nodes-base.code", "typeVersion": 2, "position": pos}


def if_true(name, expr, pos, fname):
    return {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                          "conditions": [{"id": nid(fname, name, "cond"), "leftValue": expr, "rightValue": "",
                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                          "combinator": "and"}, "options": {}},
            "id": nid(fname, name), "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": pos}


def fetch(name, base, pos, fname):
    return {"parameters": {"url": base + "messages/get", "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
                           "sendQuery": True, "queryParameters": {"parameters": [
                               {"name": "profile_id", "value": "={{ " + TRIG + ".profile_id }}"},
                               {"name": "chat_id", "value": "={{ " + TRIG + ".chatId }}"},
                               {"name": "limit", "value": str(FETCH_LIMIT)}]},
                           "options": {"timeout": 20000}},
            "id": nid(fname, name), "name": name, "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2, "position": pos,
            "credentials": {"httpHeaderAuth": dict(WAPPI_CRED)}, "retryOnFail": True, "maxTries": 3, "waitBetweenTries": 1000,
            "alwaysOutputData": True, "onError": "continueRegularOutput"}


def recheck_fetch(name, base, pos, fname):
    """V5: the lease's one re-read — the same messages/get, short and without retries (an error ⇒ cancelled_fetch)."""
    n = fetch(name, base, pos, fname)
    n["parameters"]["options"] = {"timeout": RECHECK_FETCH_TIMEOUT_MS}
    n["retryOnFail"] = False
    del n["maxTries"], n["waitBetweenTries"]
    return n


def patch(fname, base):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    key = "={{ [ " + TRIG + ".profile_id, " + TRIG + ".from, " + TRIG + ".id"

    # --- claim (after the mode gate: suppressed events are not reply jobs) ---
    ch |= ensure(wf, pg("Claim Event", CLAIM_SQL, key + ", $execution.id ] }}", [-3360, 520], fname))
    ch |= ensure(wf, if_true("Claimed?", "={{ $json.claimed }}", [-3152, 520], fname))
    ch |= set_edges(wf, "Suppressed?", [["Outcome"], ["Claim Event"]])
    ch |= set_edges(wf, "Claim Event", [["Claimed?"]])
    ch |= set_edges(wf, "Claimed?", [["Debounce Wait"], ["Outcome"]])

    # --- Latest+Combine: owner-newer abort + stable sort ---
    lc = node(wf, "Latest+Combine")
    js = lc["parameters"]["jsCode"]
    if LATEST_COMBINE_PATCH_OLD in js:
        lc["parameters"]["jsCode"] = js.replace(LATEST_COMBINE_PATCH_OLD, LATEST_COMBINE_PATCH_NEW)
        ch = True
    else:
        assert LATEST_COMBINE_PATCH_NEW in js, f"{fname}: Latest+Combine has an unexpected shape"

    # --- cancel/refusal paths ---
    ch |= ensure(wf, code("Cancel Label", CANCEL_LABEL_JS, [-2500, 620], fname))
    ch |= ensure(wf, pg("Record Cancel", RECORD_CANCEL_SQL,
                        key + ", $json.outcome, $json.historyWatermark, $json.dropAiHistory, $execution.id ] }}", [-2300, 620], fname))
    ch |= set_edges(wf, "Is Latest?", [["Cancel Label"], ["Count Dialog"]])
    ch |= set_edges(wf, "If Quota Allows", [["Input type"], ["Cancel Label"]])
    ch |= set_edges(wf, "Cancel Label", [["Record Cancel"]])
    ch |= set_edges(wf, "Record Cancel", [["Outcome"]])

    # --- generation guard ---
    ai = node(wf, "AI Agent")
    for k, v in (("onError", "continueRegularOutput"), ("alwaysOutputData", True)):
        if ai.get(k) != v:
            ai[k] = v
            ch = True
    ch |= ensure(wf, if_true("Generated?", "={{ typeof $json.output === 'string' && $json.output.trim().length > 0 && !$json.error }}", [-2080, -144], fname))
    ch |= set_edges(wf, "AI Agent", [["Generated?"]])
    ch |= set_edges(wf, "Generated?", [["Pause Before Reading"], ["Cancel Label"]])

    # --- decorative actions fail open; pauses clamped ---
    for name in ("Mark Read", "Typing"):
        n = node(wf, name)
        for k, v in (("onError", "continueRegularOutput"), ("alwaysOutputData", True)):
            if n.get(k) != v:
                n[k] = v
                ch = True
        if n["parameters"].get("options", {}).get("timeout") != 15000:
            n["parameters"]["options"] = {"timeout": 15000}
            ch = True
    ciw = node(wf, "Count Input Words")
    if "Math.min(" not in ciw["parameters"]["jsCode"]:
        ciw["parameters"]["jsCode"] = ciw["parameters"]["jsCode"].replace(
            "json: { wordCount: countWords($('Webhook').first().json.body.messages[0].body) }",
            "json: { wordCount: Math.min(countWords($('Webhook').first().json.body.messages[0].body), 12) }   // reading pause ≤ 8 s (stage 3 delay budget)")
        ch = True
    cow = node(wf, "Count Output Words")
    if "Math.min(" not in cow["parameters"]["jsCode"]:
        cow["parameters"]["jsCode"] = cow["parameters"]["jsCode"].replace(
            "json: { wordCount: countWords($('AI Agent').first().json.output) }",
            "json: { wordCount: Math.min(countWords($('AI Agent').first().json.output), 20) }   // typing pause ≤ 12 s (stage 3 delay budget)")
        ch = True
    lp = node(wf, "Listening Pause")
    if "Math.min(" not in lp["parameters"]["amount"]:
        m = "$('Webhook').item.json.body.messages[0]"
        src = f"{m}.media_info?.duration ?? {m}.length_seconds ?? 0" if "length_seconds" in lp["parameters"]["amount"] else f"{m}.media_info?.duration ?? 0"
        lp["parameters"]["amount"] = "={{ Math.min(Number(" + src + ") || 0, 10) + 2 }}"   # listening pause <= 12 s
        ch = True

    # --- final check + lease + send + settle ---
    ch |= ensure(wf, fetch("Final Fetch", base, [-64, -160], fname))
    ch |= ensure(wf, code("Final Check", FINAL_CHECK_JS, [128, -160], fname))
    ch |= ensure(wf, if_true("Final OK?", "={{ $json.ok }}", [320, -160], fname))
    ch |= ensure(wf, pg("Acquire Send Lease", ACQUIRE_SQL, key + ACQUIRE_PARAMS_TAIL, [512, -160], fname))
    ch |= ensure(wf, if_true("Send Allowed?", "={{ $json.allowed }}", [704, -160], fname))
    send = node(wf, "HTTP Request")
    bp = send["parameters"]["bodyParameters"]["parameters"]
    for p in bp:
        if p["name"] == "body" and p["value"] != "={{ $('Final Check').first().json.text }}":
            p["value"] = "={{ $('Final Check').first().json.text }}"
            ch = True
    want_opts = {"timeout": 30000, "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}
    if send["parameters"].get("options") != want_opts:
        send["parameters"]["options"] = want_opts
        ch = True
    for k, v in (("onError", "continueRegularOutput"), ("alwaysOutputData", True)):
        if send.get(k) != v:
            send[k] = v
            ch = True
    ch |= ensure(wf, code("Settle Send", SETTLE_JS, [1088, 288], fname))
    ch |= ensure(wf, if_true("Send Unknown?", "={{ $json.unknown }}", [1280, 288], fname))
    ch |= ensure(wf, {"parameters": {"amount": 3}, "id": nid(fname, "Wait Reconcile"), "name": "Wait Reconcile", "type": "n8n-nodes-base.wait",
                      "typeVersion": 1.1, "position": [1472, 420], "webhookId": nid(fname, "Wait Reconcile", "wh")})
    ch |= ensure(wf, fetch("Reconcile Fetch", base, [1664, 420], fname))
    ch |= ensure(wf, code("Reconcile Check", RECONCILE_JS, [1856, 420], fname))
    ch |= ensure(wf, pg("Record Send", RECORD_SEND_SQL, key + ", $json.state, $json.transportMessageId, $json.error, $execution.id ] }}", [2048, 288], fname))
    ch |= set_edges(wf, "Retrieve Answer", [["Final Fetch"]])
    ch |= set_edges(wf, "Ask to Send Text", [["Final Fetch"]])
    ch |= set_edges(wf, "Final Fetch", [["Final Check"]])
    ch |= set_edges(wf, "Final Check", [["Final OK?"]])
    ch |= set_edges(wf, "Final OK?", [["Acquire Send Lease"], ["Cancel Label"]])
    # V5: the lease's one re-read loops back into the Final Check (appended nodes: nodes[0]/[5] stay Webhook/AI Agent)
    ch |= ensure(wf, if_true("Peer Recheck?", "={{ $json.recheck === true }}", [704, -400], fname))
    ch |= ensure(wf, {"parameters": {"amount": PEER_RECHECK_SECONDS}, "id": nid(fname, "Wait Peer Recheck"), "name": "Wait Peer Recheck",
                      "type": "n8n-nodes-base.wait", "typeVersion": 1.1, "position": [896, -400], "webhookId": nid(fname, "Wait Peer Recheck", "wh")})
    ch |= ensure(wf, recheck_fetch("Recheck Fetch", base, [1088, -400], fname))
    ch |= set_edges(wf, "Acquire Send Lease", [["Peer Recheck?"]])
    ch |= set_edges(wf, "Peer Recheck?", [["Wait Peer Recheck"], ["Send Allowed?"]])
    ch |= set_edges(wf, "Wait Peer Recheck", [["Recheck Fetch"]])
    ch |= set_edges(wf, "Recheck Fetch", [["Final Check"]])
    ch |= set_edges(wf, "Send Allowed?", [["HTTP Request"], ["Cancel Label"]])
    ch |= set_edges(wf, "HTTP Request", [["Settle Send"]])
    ch |= set_edges(wf, "Settle Send", [["Send Unknown?"]])
    ch |= set_edges(wf, "Send Unknown?", [["Wait Reconcile"], ["Record Send"]])
    ch |= set_edges(wf, "Wait Reconcile", [["Reconcile Fetch"]])
    ch |= set_edges(wf, "Reconcile Fetch", [["Reconcile Check"]])
    ch |= set_edges(wf, "Reconcile Check", [["Record Send"]])
    ch |= set_edges(wf, "Record Send", [["Outcome"]])

    if ch:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return ch


def check():
    bad = []
    for f in BOTS:
        wf = json.load(open(os.path.join(WF, f), encoding="utf-8"))
        need = ("Claim Event", "Claimed?", "Generated?", "Final Fetch", "Final Check", "Final OK?", "Acquire Send Lease",
                "Send Allowed?", "Settle Send", "Send Unknown?", "Wait Reconcile", "Reconcile Fetch", "Reconcile Check",
                "Record Send", "Cancel Label", "Record Cancel", "Peer Recheck?", "Wait Peer Recheck", "Recheck Fetch")
            # Claim / Record Send / Record Cancel / Cancel Label are re-shaped by apply-history-handoff.py (stage 4) — not compared here.
        if any(node(wf, n) is None for n in need) or LATEST_COMBINE_PATCH_NEW not in node(wf, "Latest+Combine")["parameters"]["jsCode"] \
                or node(wf, "Acquire Send Lease")["parameters"]["query"] != ACQUIRE_SQL or node(wf, "Final Check")["parameters"]["jsCode"] != FINAL_CHECK_JS \
                or not node(wf, "Acquire Send Lease")["parameters"]["options"]["queryReplacement"].endswith(ACQUIRE_PARAMS_TAIL) \
                or node(wf, "Wait Peer Recheck")["parameters"].get("amount") != PEER_RECHECK_SECONDS \
                or node(wf, "Reconcile Check")["parameters"]["jsCode"] != RECONCILE_JS:
            bad.append(f)
    return bad


def main():
    if "--check" in sys.argv:
        bad = check()
        print("reply pipeline v2:", "OK" if not bad else f"MISSING/STALE in {bad}")
        sys.exit(2 if bad else 0)
    for f, base in BOTS.items():
        print(f"{'patched' if patch(f, base) else 'up to date'}  {f}")


if __name__ == "__main__":
    main()
