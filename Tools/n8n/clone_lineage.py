#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""F1 + F2 «линия бота» — the SQL core (task T2 of docs/audits/2026-10-09-followups/F1F2-plan.md, §1.3, §1.5, §1.6).

Create WA/TG becomes «ensure exactly one bound clone for this channel of this bot». The same profile reuses its bound
clone; a new profile of the same bot (a re-pairing, a reconnect after a purchase) gets a new clone that takes over
everything the bot had on the server, and the clone it replaces is booked for deletion. Everything that decides it lives
here, in ONE module, so the generators that wire it (apply-clone-lifecycle.py → Create WA/TG, the sweep's branch A,
Delete Bot Files) never carry their own copy:

  CLONE_PLAN_SQL         read-only: own_id (reuse), foreign (refuse), pred_profiles (adopt), replace_ids (replace) — the
                         predecessor rule of plan §1.3, proven in the DATABASE (never by the token's 12-hour lists);
  BIND_WORKFLOW_SQL[ch]  ONE statement: bind + move memory / history / outcomes / handoffs / reply ledger / chat state /
                         chat flags of the predecessors, chunks + file registry of the replaced clones, the pre-F1 '-1'
                         rule, mark the predecessors superseded, book the replaced clones — every condition re-checked on
                         the rows it writes, on the version a concurrent statement committed (≈ 10 s pass between the
                         plan and the bind: the V6-concurrent lesson). A concurrent bind of the same profile that
                         committed first wins: this one answers bound = false instead of overwriting a binding it never
                         saw. Chunks are matched by metadata->>key, the expression the prod RAG store indexes;
  RECORD_DISCARD_FAILURE_SQL   books a new clone a failed Create could not delete;
  CLONE_RETIREMENTS_SQL + STAMP_RETIREMENT_*_SQL   the sweep's branch A (DELETE the booked clones, confirm 200/404);
  lineage_cte(...)       «the bot's line» for Delete Bot Files (T5): every row of the install with the named rows' bot key,
                         both channels, plus the superseded chain;
  DBF_LINE_PARAMS + LINE_*_SQL   Delete Bot Files by the line (T5): the stored originals, the app_delete retire + the
                         bot_deleted bookings, the chunks — every deleting node reads the same fragment with the same
                         parameters (Delete Chat Memory, owned by apply-history-handoff.py, composes DBF_LINE_HEAD too).

Protected ids (build-client-webhooks.PROTECTED_IDS + Set Workflow State + the support relay) are never reused, replaced,
moved, booked or bound. Every statement names its parameters in words (V1: no $ + digit in a comment or literal).
Behaviour is pinned by tests/test_clone_lineage_sql.py on the real staging Postgres; schema = sql/2026-10-11-bot-lineage.sql.

    python3 Tools/n8n/clone_lineage.py --check        # every statement passes verify-sql-comments
    python3 Tools/n8n/clone_lineage.py --print NAME   # print one statement (names: --list)
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SET_WORKFLOW_STATE_ID = "0DP79STI3pafXO9B"
SUPPORT_RELAY_ID = "olmqWMfcFpCEcpiE"

# One row per channel, as in apply-slot-count-fix.py: the Create canon, the form fields, the chunk metadata keys
# (this channel's / the other's) and the file-registry column.
CHANNELS = {
    "whatsapp": {"short": "WA", "create_file": "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json", "profile_field": "WhatsappProfileId",
                 "other_workflow_field": "TelegramWorkflowId", "meta_key": "botWaId", "other_meta_key": "botTgId",
                 "file_column": "bot_wa_id"},
    "telegram": {"short": "TG", "create_file": "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json", "profile_field": "TelegramProfileId",
                 "other_workflow_field": "WhatsappWorkflowId", "meta_key": "botTgId", "other_meta_key": "botWaId",
                 "file_column": "bot_tg_id"},
}

# The seven tables Delete Chat Memory clears (Delete Bot Files) — the bind moves EXACTLY these (pinned by the test against
# the canon). dialog_counts are billing usage records keyed by the app user, not chat content: never moved, never deleted.
MOVED_TABLES = ("n8n_chat_histories", "chat_history_events", "conversation_outcomes", "handoff_requests", "reply_events",
                "chat_reply_state", "reply_mode_flags")


def protected_ids():
    spec = importlib.util.spec_from_file_location("bcw", os.path.join(HERE, "build-client-webhooks.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return sorted(set(m.PROTECTED_IDS) | {SET_WORKFLOW_STATE_ID, SUPPORT_RELAY_ID})


PROTECTED = protected_ids()
PROT = ", ".join("'%s'" % i for i in PROTECTED)
ID_FORM = "'^[A-Za-z0-9]{8,36}$'"          # a clone id as n8n issues it (Set Workflow State's Validate uses the same)


def _sql(text, **kw):
    out = text.replace("<PROT>", PROT).replace("<ID>", ID_FORM)
    for k, v in kw.items():
        out = out.replace("<" + k + ">", v)
    assert "<" + "PROT>" not in out and "<ID>" not in out
    return out


def _json_strings(param):
    """The string elements of a JSON-array parameter (anything else — not an array, a number, null — is no element)."""
    return ("select x #>> '{}' as v from jsonb_array_elements(case when jsonb_typeof(" + param + "::jsonb) = 'array' then "
            + param + "::jsonb else '[]'::jsonb end) x where jsonb_typeof(x) = 'string'")


# ------------------------------------------------------------------------------------------------ Clone Plan (§1.3)

CLONE_PLAN_PARAMS = ("gate mode", "install id", "profile id", "channel", "bot key", "create origin", "previous profile id")

CLONE_PLAN_SQL = _sql("""-- F1 + F2 (plan 2026-10-11 §1.3): what this Create may reuse, must refuse, replace and adopt. Read-only; ALWAYS one
-- row with planned = true once it completed (the graph gates on that positive field, never on «the node continued»).
--   own_id         the profile's own bound clone when the binding is the caller's (session: its install, not revoked;
--                  legacy: a row without an owner) and the clone is not booked for deletion (a superseded profile that
--                  comes back gets a new clone — reusing a booked one would hand the sweep a clone a live row drives).
--                  The graph probes it and reuses it while it still serves the profile.
--   foreign        the profile belongs to an install that is not the caller (any legacy call, a revoked install, a token
--                  older than a claim by code): the graph refuses, nothing is created.
--   pred_profiles  the earlier profiles of THIS bot on this channel, proven here: owned by the caller's live install,
--                  never for a wizard create (a new bot in a reused slot), the same bot key or the named previous profile
--                  (whose bot key is empty or the same), never a deleted bot (app_delete), never one another profile
--                  already took over, never one bound to a protected id or to a clone a live row of another bot drives,
--                  and the new profile is the caller's live row of this channel.
--   replace_ids    their clones (well formed, never protected, never the profile's own binding): booked and deleted once
--                  the new clone is bound.
-- Parameters, in order: gate mode, install id, the new profile id, channel, bot key, create origin, previous profile id.
with me as (
  select i.install_id as inst from installs i
  where $1 = 'session' and i.install_id = nullif($2, '') and i.revoked_at is null
), target as (
  select bp.channel, bp.owner_id, bp.workflow_id from bot_profiles bp where bp.profile_id = $3
), own as (
  select t.workflow_id from target t
  where t.channel = $4 and t.workflow_id ~ <ID> and t.workflow_id not in (<PROT>)
    and not exists (select 1 from clone_retirements r where r.workflow_id = t.workflow_id)
    and ((t.owner_id = (select inst from me)) or ($1 = 'legacy' and t.owner_id is null))
), cand as (
  select bp.profile_id, bp.workflow_id from bot_profiles bp join me on bp.owner_id = me.inst
  where bp.channel = $4 and bp.profile_id <> $3
    and coalesce($6, '') <> 'start'
    and ((nullif($5, '') is not null and bp.bot_key = $5)
      or (nullif($7, '') is not null and bp.profile_id = $7
          and (nullif(bp.bot_key, '') is null or nullif($5, '') is null or bp.bot_key = $5)))
    and bp.deleted_reason is distinct from 'app_delete'
    and (bp.superseded_by is null or bp.superseded_by = $3)
    and (bp.workflow_id is null or bp.workflow_id not in (<PROT>))
    and exists (select 1 from bot_profiles nw where nw.profile_id = $3 and nw.owner_id = me.inst
                  and nw.channel = $4 and nw.deleted_at is null)
), pred as (
  select c.profile_id, c.workflow_id from cand c
  where not exists (select 1 from bot_profiles live where live.workflow_id = c.workflow_id and live.deleted_at is null
                      and live.profile_id <> $3 and live.profile_id not in (select profile_id from cand))
)
select true as planned,
  (select workflow_id from own limit 1) as own_id,
  exists (select 1 from target t where t.owner_id is not null and t.owner_id is distinct from (select inst from me)) as "foreign",
  coalesce((select jsonb_agg(distinct p.workflow_id order by p.workflow_id) from pred p
            where p.workflow_id ~ <ID> and p.workflow_id is distinct from (select t.workflow_id from target t)),
           '[]'::jsonb) as replace_ids,
  coalesce((select jsonb_agg(p.profile_id order by p.profile_id) from pred p), '[]'::jsonb) as pred_profiles""")


# ------------------------------------------------------------------------------------------- Bind Workflow Id (§1.5)

BIND_WORKFLOW_PARAMS = ("profile id", "clone id", "app user id", "channel", "install id",
                        "the profile's previous binding when the clone was created (or empty)", "JSON array of the clones to replace",
                        "JSON array of the predecessor profiles", "the other channel's workflow id")

_BIND_TEMPLATE = """-- F1 + F2 (plan 2026-10-11 §1.5): bind the clone to the profile and hand it everything the bot had on the server, in
-- ONE statement that ALWAYS answers one row; bound = true only when the profile's row now names this clone. The plan ran
-- about ten seconds earlier, so every condition is re-checked on the rows written here — on the version a concurrent
-- statement committed, not only on this statement's snapshot (a row moved or marked meanwhile is never taken back).
--   bind     the profile's row: never a sentinel, never a malformed or protected clone id, never a row of the other
--            channel or of another owner (a legacy call binds only a row without one), and only while the row still
--            names the binding this statement saw: a concurrent Create of the same profile that bound its clone after
--            this statement began wins, and this one answers bound = false (the graph discards its clone; the client's
--            retry reuses the winner). Whatever binding it overwrites is replaced too, so no clone is left unbound.
--   p        the predecessors the plan named that still qualify: this install's (not revoked), this channel, a bot key
--            compatible with the new profile's, never a deleted bot, never taken over by another profile, never bound to a
--            protected id or to a clone a live row of another bot drives — and the new profile is this install's row.
--   moved    the replaced clones: the overwritten binding, the plan's own binding (a clone that is gone) and the
--            predecessors' clones the plan named; never protected, never this clone, never one a live row outside this
--            bot drives.
--   The predecessors' memory, history, outcomes, handoffs, reply ledger, chat state and chat flags move to the new profile
--   (row ids kept: the memory order and the run watermarks stay right). Where the new profile already holds the same key
--   its row wins; among several predecessors the freshest. The predecessors' '*' rows go: the new one was seeded from the
--   client's current mode. dialog_counts stay (billing records, not chat content).
--   The moved clones' chunks and file registry go to the new clone — this install's rows and owner-less ones (a compat
--   upload), so a file's row and its chunks keep naming ONE clone (the V4 evidence of Delete File / Register Upload);
--   a file row of another install never. Plus the pre-F1 rule: a second channel joining a bot takes the other
--   channel's chunks that carry the -1 sentinel.
--   The predecessor rows and the rows bound to a moved clone are marked superseded (their own reason kept, a missing one
--   becomes replaced) unless another profile took them over meanwhile; adopted_profiles names only the predecessors
--   that are marked for THIS profile. The moved clones are booked in clone_retirements BEFORE the graph deletes them.
-- Parameters, in order: profile id, clone id, app user id, channel, install id, the profile's previous binding when the
-- clone was created (or empty), JSON array of the clones to replace, JSON array of the predecessor profiles, the other
-- channel's workflow id.
with me as (
  select i.install_id as inst from installs i where i.install_id = nullif($5, '') and i.revoked_at is null
), cur as (
  select bp.workflow_id, bp.owner_id, bp.bot_key from bot_profiles bp where bp.profile_id = $1
), bind as (
  insert into bot_profiles as bp (profile_id, app_user_id, channel, workflow_id, owner_id, claimed_at)
  select $1, coalesce(nullif($3, ''), $1), $4, $2, nullif($5, ''), case when nullif($5, '') is null then null else now() end
  where nullif($1, '') is not null and $1 <> '-1' and $2 ~ <ID> and $2 not in (<PROT>)
  on conflict (profile_id) do update set workflow_id = excluded.workflow_id,
    owner_id = coalesce(bp.owner_id, excluded.owner_id), claimed_at = coalesce(bp.claimed_at, excluded.claimed_at),
    deleted_at = null, superseded_by = null, superseded_at = null
  where bp.channel = excluded.channel and (bp.owner_id is null or bp.owner_id = excluded.owner_id)
    and bp.workflow_id is not distinct from (select c.workflow_id from cur c)
  returning bp.workflow_id
), p0 as (
  select bp.profile_id as pid, bp.workflow_id as wf, bp.superseded_by as sup
  from bot_profiles bp join me on bp.owner_id = me.inst
  where exists (select 1 from bind)
    and bp.profile_id in (<PREDS>)
    and bp.profile_id <> $1 and bp.channel = $4
    and bp.deleted_reason is distinct from 'app_delete'
    and (bp.superseded_by is null or bp.superseded_by = $1)
    and (bp.workflow_id is null or bp.workflow_id not in (<PROT>))
    and exists (select 1 from cur where cur.owner_id = me.inst
                  and (nullif(bp.bot_key, '') is null or nullif(cur.bot_key, '') is null or bp.bot_key = cur.bot_key))
), p as (
  select p0.pid, p0.wf, p0.sup from p0
  where not exists (select 1 from bot_profiles live where live.workflow_id = p0.wf and live.deleted_at is null
                      and live.profile_id <> $1 and live.profile_id not in (select pid from p0))
), moved as (
  select distinct s.wf from (
    select nullif($6, '') as wf
    union all select cur.workflow_id from cur
    union all select p.wf from p where p.wf in (<REPLACE>)
  ) s
  where exists (select 1 from bind) and s.wf ~ <ID> and s.wf not in (<PROT>) and s.wf <> $2
    and not exists (select 1 from bot_profiles live where live.workflow_id = s.wf and live.deleted_at is null
                      and live.profile_id <> $1 and live.profile_id not in (select pid from p))
), chunks_moved as (
  update documents d set metadata = jsonb_set(d.metadata, '{<KEY>}', to_jsonb($2::text))
  from moved m
  where exists (select 1 from moved) and d.metadata->>'<KEY>' = m.wf           -- the expression index of the RAG store
  returning 1
), chunks_sentinel as (
  update documents d set metadata = jsonb_set(d.metadata, '{<KEY>}', to_jsonb($2::text))
  where exists (select 1 from bind) and $9 ~ <ID>
    and d.metadata->>'<OTHER>' = $9 and d.metadata->>'<KEY>' = '-1'
  returning 1
), files as (
  update uploaded_files u set <FCOL> = $2
  where exists (select 1 from moved) and u.<FCOL> in (select wf from moved)
    and (u.owner_id is null or u.owner_id = (select inst from me))
  returning 1
), superseded as (
  update bot_profiles bp set superseded_by = $1,
    superseded_at = case when bp.superseded_by is not distinct from $1 then coalesce(bp.superseded_at, now()) else now() end,
    deleted_at = coalesce(bp.deleted_at, now()), deleted_reason = coalesce(bp.deleted_reason, 'replaced')
  where exists (select 1 from bind) and bp.profile_id <> $1 and bp.owner_id = (select inst from me)
    and (bp.profile_id in (select pid from p) or bp.workflow_id in (select wf from moved))
    and (bp.superseded_by is null or bp.superseded_by = $1)
    and (bp.superseded_by is distinct from $1 or bp.deleted_at is null or bp.deleted_reason is null)
  returning bp.profile_id
), booked as (
  insert into clone_retirements (workflow_id, profile_id, owner_id, reason, replaced_by)
  select m.wf,
    (select bp.profile_id from bot_profiles bp where bp.workflow_id = m.wf
      order by (bp.profile_id = $1) desc, (bp.owner_id is not distinct from nullif($5, '')) desc, bp.created_at desc, bp.profile_id
      limit 1),
    nullif($5, ''), 'replaced', $2
  from moved m
  on conflict (workflow_id) do nothing
  returning 1
), mem as (
  update n8n_chat_histories h set session_id = $1 || substr(h.session_id, length(split_part(h.session_id, ':', 1)) + 1)
  where exists (select 1 from p) and split_part(h.session_id, ':', 1) in (select pid from p)
  returning 1
), hist_r as (
  select e.id, e.profile_id as pid,
    row_number() over (partition by e.chat_id, e.message_id order by e.created_at desc, e.id desc) as rn,
    exists (select 1 from chat_history_events x where x.profile_id = $1 and x.chat_id = e.chat_id
              and x.message_id = e.message_id) as taken
  from chat_history_events e where e.profile_id in (select pid from p)
), hist_mv as (
  update chat_history_events e set profile_id = $1 from hist_r r
  where e.id = r.id and e.profile_id = r.pid and r.rn = 1 and not r.taken
  returning 1
), hist_dup as (
  delete from chat_history_events e using hist_r r where e.id = r.id and e.profile_id = r.pid and (r.rn > 1 or r.taken)
  returning 1
), outc_r as (
  select o.session_id as sid, row_number() over (partition by o.chat_id
      order by o.updated_at desc, o.outcome_at desc, o.session_id desc) as rn,
    exists (select 1 from conversation_outcomes x where x.session_id = $1 || ':' || o.chat_id) as taken
  from conversation_outcomes o where o.profile_id in (select pid from p)
), outc_mv as (
  update conversation_outcomes o set profile_id = $1, session_id = $1 || ':' || o.chat_id
  from outc_r r where o.session_id = r.sid and r.rn = 1 and not r.taken
  returning 1
), outc_dup as (
  delete from conversation_outcomes o using outc_r r where o.session_id = r.sid and (r.rn > 1 or r.taken)
  returning 1
), hand_r as (
  select h.id, h.profile_id as pid,
    row_number() over (partition by h.chat_id, h.source_message_id order by h.updated_at desc, h.id desc) as rn,
    exists (select 1 from handoff_requests x where x.profile_id = $1 and x.chat_id = h.chat_id
              and x.source_message_id = h.source_message_id) as taken
  from handoff_requests h where h.profile_id in (select pid from p)
), hand_mv as (
  update handoff_requests h set profile_id = $1 from hand_r r
  where h.id = r.id and h.profile_id = r.pid and r.rn = 1 and not r.taken
  returning 1
), hand_dup as (
  delete from handoff_requests h using hand_r r where h.id = r.id and h.profile_id = r.pid and (r.rn > 1 or r.taken)
  returning 1
), ev_r as (
  select e.profile_id as pid, e.chat_id, e.message_id,
    row_number() over (partition by e.chat_id, e.message_id order by e.updated_at desc, e.profile_id desc) as rn,
    exists (select 1 from reply_events x where x.profile_id = $1 and x.chat_id = e.chat_id
              and x.message_id = e.message_id) as taken
  from reply_events e where e.profile_id in (select pid from p)
), ev_mv as (
  update reply_events e set profile_id = $1 from ev_r r
  where e.profile_id = r.pid and e.chat_id = r.chat_id and e.message_id = r.message_id and r.rn = 1 and not r.taken
  returning 1
), ev_dup as (
  delete from reply_events e using ev_r r
  where e.profile_id = r.pid and e.chat_id = r.chat_id and e.message_id = r.message_id and (r.rn > 1 or r.taken)
  returning 1
), st_r as (
  select s.profile_id as pid, s.chat_id,
    row_number() over (partition by s.chat_id
      order by s.newest_message_time desc nulls last, s.last_sent_at desc nulls last, s.profile_id desc) as rn,
    exists (select 1 from chat_reply_state x where x.profile_id = $1 and x.chat_id = s.chat_id) as taken
  from chat_reply_state s where s.profile_id in (select pid from p)
), st_mv as (
  update chat_reply_state s set profile_id = $1 from st_r r
  where s.profile_id = r.pid and s.chat_id = r.chat_id and r.rn = 1 and not r.taken
  returning 1
), st_dup as (
  delete from chat_reply_state s using st_r r
  where s.profile_id = r.pid and s.chat_id = r.chat_id and (r.rn > 1 or r.taken)
  returning 1
), fl_r as (
  select f.profile_id as pid, f.chat_id,
    row_number() over (partition by f.chat_id order by f.updated_at desc, f.version desc, f.profile_id desc) as rn,
    (f.chat_id = '*' or exists (select 1 from reply_mode_flags x where x.profile_id = $1 and x.chat_id = f.chat_id)) as taken
  from reply_mode_flags f where f.profile_id in (select pid from p)
), fl_mv as (
  update reply_mode_flags f set profile_id = $1 from fl_r r
  where f.profile_id = r.pid and f.chat_id = r.chat_id and r.rn = 1 and not r.taken
  returning 1
), fl_dup as (
  delete from reply_mode_flags f using fl_r r
  where f.profile_id = r.pid and f.chat_id = r.chat_id and (r.rn > 1 or r.taken)
  returning 1
)
select exists (select 1 from bind where workflow_id = $2) as bound, $2::text as workflow_id,
  coalesce((select jsonb_agg(a.pid order by a.pid) from (
     select p.pid from p where p.sup = $1
     union select s.profile_id from superseded s where s.profile_id in (select pid from p)) a), '[]'::jsonb) as adopted_profiles,
  coalesce((select jsonb_agg(wf order by wf) from moved), '[]'::jsonb) as retired_ids,
  ((select count(*) from chunks_moved) + (select count(*) from chunks_sentinel))::int as restamped_chunks,
  (select count(*) from files)::int as restamped_files,
  (select count(*) from superseded)::int as superseded_rows,
  (select count(*) from booked)::int as booked,
  (select count(*) from mem)::int as moved_memory,
  (select count(*) from hist_mv)::int as moved_history,
  (select count(*) from outc_mv)::int as moved_outcomes,
  (select count(*) from hand_mv)::int as moved_handoffs,
  (select count(*) from ev_mv)::int as moved_events,
  (select count(*) from st_mv)::int as moved_states,
  (select count(*) from fl_mv)::int as moved_flags,
  ((select count(*) from hist_dup) + (select count(*) from outc_dup) + (select count(*) from hand_dup)
   + (select count(*) from ev_dup) + (select count(*) from st_dup) + (select count(*) from fl_dup))::int as dropped_rows"""


def bind_workflow_sql(channel):
    c = CHANNELS[channel]
    return _sql(_BIND_TEMPLATE, KEY=c["meta_key"], OTHER=c["other_meta_key"], FCOL=c["file_column"],
                PREDS=_json_strings("$8"), REPLACE=_json_strings("$7"))


BIND_WORKFLOW_SQL = {ch: bind_workflow_sql(ch) for ch in CHANNELS}


# ---------------------------------------------------------------------- the discard journal and the sweep (§1.4, §1.6)

RECORD_DISCARD_FAILURE_SQL = _sql("""-- F1 (plan 2026-10-11 §1.4): the new clone of a failed Create could not be deleted (Discard New Clone answered
-- neither 200 nor 404). Book it, so the sweep deletes it later. Never a malformed or protected id; idempotent.
-- Parameters, in order: the clone id, the profile id, install id.
insert into clone_retirements (workflow_id, profile_id, owner_id, reason)
select $1, nullif($2, ''), nullif($3, ''), 'discard_failed'
where $1 ~ <ID> and $1 not in (<PROT>)
on conflict (workflow_id) do nothing
returning workflow_id;""")

CLONE_RETIREMENTS_SQL = _sql("""-- F1 (plan 2026-10-11 §1.6, the sweep's branch A): booked clones not yet confirmed gone. Never one a LIVE bot_profiles
-- row drives, never a malformed or protected id, never younger than ten minutes (a Create still in flight). Least tried
-- first, so a clone that keeps failing never starves the rest; at most 50 per run. Read-only.
select r.workflow_id, r.reason, r.attempts from clone_retirements r
where r.removed_at is null
  and r.workflow_id ~ <ID> and r.workflow_id not in (<PROT>)
  and r.created_at < now() - interval '10 minutes'
  and not exists (select 1 from bot_profiles live where live.workflow_id = r.workflow_id and live.deleted_at is null)
order by r.attempts, r.created_at, r.workflow_id
limit 50;""")

STAMP_RETIREMENT_REMOVED_SQL = """-- F1 (plan 2026-10-11 §1.6): the clone answered 200 or 404 to DELETE, it is gone and never picked again.
-- Parameters, in order: workflow id, the HTTP status.
update clone_retirements set removed_at = now(), last_status = $2
where workflow_id = $1 and removed_at is null
returning workflow_id;"""

STAMP_RETIREMENT_KEPT_SQL = """-- F1 (plan 2026-10-11 §1.6): the DELETE did not confirm the clone gone; count the attempt, the next run tries again.
-- Parameters, in order: workflow id, the HTTP status (or the error).
update clone_retirements set attempts = attempts + 1, last_status = $2
where workflow_id = $1 and removed_at is null
returning workflow_id, attempts;"""


# ------------------------------------------------------------------------------ the bot's line (Delete Bot Files, §1.6)

def lineage_cte(mode, install, profiles_json, workflows_json):
    """CTEs `lineage_profiles(profile_id)` and `lineage_workflows(workflow_id)` for a statement that starts with
    `with recursive`. The arguments are SQL expressions (usually placeholders) for the gate mode, the install id and the
    JSON arrays of the profile / workflow ids the delete names. Session (install not revoked): the named ids plus every row
    of this install reachable from the named rows of this install by the bot key (both channels) or by the superseded
    chain, either direction — never another install's row, never another bot key, never a key-less row outside the
    chain. Any other mode or a revoked install: the named ids only (the pre-F1 behaviour). Sentinels ('', '-1', 'null',
    'undefined') and non-strings are no ids; a protected or malformed id never reaches lineage_workflows."""
    return _sql("""lin_me as (    -- the bot's line (plan 2026-10-11 §1.6): who deletes
  select i.install_id as inst from installs i
  where <MODE> = 'session' and i.install_id = nullif(<INST>, '') and i.revoked_at is null
), lin_ids as (
  select 'p'::text as kind, v from (<PROFILES>) a
  union all
  select 'w'::text, v from (<WORKFLOWS>) b
), lin_named as (
  select kind, v as id from lin_ids where v not in ('', '-1', 'null', 'undefined')
), lin_rows (profile_id) as (
  select bp.profile_id from bot_profiles bp join lin_me on bp.owner_id = lin_me.inst
  where exists (select 1 from lin_named n where (n.kind = 'p' and n.id = bp.profile_id) or (n.kind = 'w' and n.id = bp.workflow_id))
  union
  select bp.profile_id from lin_rows r
  join bot_profiles cur on cur.profile_id = r.profile_id
  join bot_profiles bp on bp.owner_id = cur.owner_id
    and ((nullif(bp.bot_key, '') is not null and bp.bot_key = cur.bot_key)
      or bp.superseded_by = cur.profile_id or bp.profile_id = cur.superseded_by)
), lineage_profiles as (
  select id as profile_id from lin_named where kind = 'p'
  union
  select profile_id from lin_rows
), lineage_workflows as (
  select id as workflow_id from lin_named where kind = 'w' and id ~ <ID> and id not in (<PROT>)
  union
  select bp.workflow_id from bot_profiles bp join lin_rows r on r.profile_id = bp.profile_id
  where bp.workflow_id ~ <ID> and bp.workflow_id not in (<PROT>)
)""", MODE=mode, INST=install, PROFILES=_json_strings(profiles_json), WORKFLOWS=_json_strings(workflows_json))


LINEAGE_PARAMS = ("gate mode", "install id", "JSON array of the profile ids", "JSON array of the workflow ids")


# -------------------------------------------------------- Delete Bot Files by the bot's line (T5, plan §1.6, F1 §1.4)
# The app names the bot's CURRENT ids; every deleting node of Delete Bot Files derives the bot's line from them with the
# SAME fragment (lineage_cte) and the SAME parameters — exactly the form Delete Ownership proved (a JSON string per id;
# a 'null', a number or a missing field is no id). Delete Ownership itself does not change: the line grows only through
# rows this install owns, so whatever it reaches the caller has proven it may delete.
DBF_LINE_PARAMS = ("={{ [ $('Auth Gate').first().json.crAuth.mode, ($('Auth Gate').first().json.crAuth.installId || ''), "
                   "JSON.stringify([ ($('Auth Gate').first().json.body || {}).waProfileId ?? null, "
                   "($('Auth Gate').first().json.body || {}).tgProfileId ?? null ]), "
                   "JSON.stringify([ ($('Auth Gate').first().json.body || {}).botWaId ?? null, "
                   "($('Auth Gate').first().json.body || {}).botTgId ?? null ]) ] }}")
DBF_LINE_HEAD = "with recursive " + lineage_cte("$1", "$2", "$3", "$4")
# The line's clones whose chunks / originals / booking this delete may touch: never one a LIVE row OUTSIDE the line still
# drives (another bot's price lists stay where they are), never a protected or malformed id (lineage_workflows).
_LINE_CLONES = """, line_clones as (     -- the line's clones that no live row outside the line drives
  select w.workflow_id from lineage_workflows w
  where not exists (select 1 from bot_profiles live where live.workflow_id = w.workflow_id and live.deleted_at is null
                      and live.profile_id not in (select profile_id from lineage_profiles))
), line_chunks as (     -- their chunks, one join per key: the RAG store's expression indexes (never a scan of the store)
  select d.id, d.metadata->>'fileId' as file_id from documents d join line_clones c on d.metadata->>'botWaId' = c.workflow_id
  union
  select d.id, d.metadata->>'fileId' from documents d join line_clones c on d.metadata->>'botTgId' = c.workflow_id
)"""
_LINE_PARAMS_WORDS = "-- Parameters, in order: gate mode, install id, JSON array of the profile ids, JSON array of the workflow ids.\n"

LINE_LIST_ORIGINALS_SQL = ("""-- V12 + T5: the deleted bot's stored price-list originals — of every clone of the bot's line, not only the current one
-- (a re-paired or reconnected bot left its earlier clone's files behind) — listed BEFORE anything is deleted: the
-- storage delete runs first and a failure stops the chain with the chunks still in place, so the retry finds the same
-- files. Session: the line (clone_lineage); legacy: the named ids only. Read-only.
""" + _LINE_PARAMS_WORDS + DBF_LINE_HEAD + _LINE_CLONES + """
select coalesce(array_agg(distinct c.file_id) filter (where coalesce(c.file_id, '') <> ''), '{}'::text[]) as "fileIds"
from line_chunks c;""")

LINE_RETIRE_SQL = ("""-- App-delete slot release (2026-08-27) by the bot's line (T5): synchronously with the app's delete action every row of
-- the line is retired as app_delete — the ONLY slot release for status='active' owners (the sweep never touches them)
-- and the mark that keeps a deleted bot's rows from ever being adopted by a later Create (Clone Plan / Bind refuse
-- app_delete). Rows are matched by profile id only, never by app_user_id (it drifts between create and delete).
--   session: every row of the line this install owns; one retired earlier (a replaced predecessor, a trial or a
--            subscription that ended, an orphan of the pre-F1 re-pairing) becomes app_delete too, its retirement time
--            kept. The line's clones are booked in clone_retirements (bot_deleted) for the sweep's branch A — never one
--            a live row outside the line drives, never a protected id; a clone already booked keeps its booking.
--   legacy:  the named rows still alive only (the pre-T5 behaviour), nothing booked.
-- Always one row: retiredProfiles (rows changed now) and bookedClones (bookings added now). Deletion Settled? reads
-- retiredProfiles only: the bookings are made by this same statement, so they cannot fail apart from it; bookedClones
-- is not part of the webhook's answer.
""" + _LINE_PARAMS_WORDS + DBF_LINE_HEAD + _LINE_CLONES + """, retired as (
  update bot_profiles bp set deleted_at = coalesce(bp.deleted_at, now()), deleted_reason = 'app_delete'
  where bp.profile_id in (select profile_id from lineage_profiles)
    and ((exists (select 1 from lin_me) and bp.owner_id = (select inst from lin_me)
          and (bp.deleted_at is null or bp.deleted_reason is distinct from 'app_delete'))
      or (not exists (select 1 from lin_me) and bp.deleted_at is null))
  returning bp.profile_id
), booked as (
  insert into clone_retirements (workflow_id, profile_id, owner_id, reason)
  select c.workflow_id,
    (select bp.profile_id from bot_profiles bp join lin_rows r on r.profile_id = bp.profile_id
      where bp.workflow_id = c.workflow_id order by bp.created_at desc, bp.profile_id limit 1),
    (select inst from lin_me), 'bot_deleted'
  from line_clones c
  where exists (select 1 from lin_me)
    and exists (select 1 from bot_profiles bp join lin_rows r on r.profile_id = bp.profile_id where bp.workflow_id = c.workflow_id)
  on conflict (workflow_id) do nothing
  returning 1
)
select (select count(*)::int from retired) as "retiredProfiles", (select count(*)::int from booked) as "bookedClones";""")

LINE_DELETE_CHUNKS_SQL = ("""-- V12 + T5: the deleted bot's RAG chunks — of every clone of the bot's line (a predecessor's clone kept its chunks when a
-- re-pairing replaced it before F1) — and the file ids they carried. Session: the line; legacy: the named ids only.
""" + _LINE_PARAMS_WORDS + DBF_LINE_HEAD + _LINE_CLONES + """, deleted as (     -- by primary key (an id array: never a scan of the store)
  delete from documents d where d.id = any (array(select id from line_chunks)) returning d.id
)
select (select count(*)::int from deleted) as "deletedChunks",
  coalesce((select array_agg(distinct file_id) from line_chunks where file_id is not null and file_id <> ''), '{}'::text[]) as "fileIds";""")


def statements():
    """Every statement of the module, by name (the guards — verify-sql-comments, T4's verifier — iterate this)."""
    out = {"Clone Plan": CLONE_PLAN_SQL}
    out.update({"Bind Workflow Id (%s)" % ch: sql for ch, sql in BIND_WORKFLOW_SQL.items()})
    out.update({"Record Discard Failure": RECORD_DISCARD_FAILURE_SQL, "Clone Retirements": CLONE_RETIREMENTS_SQL,
                "Stamp Retirement Removed": STAMP_RETIREMENT_REMOVED_SQL, "Stamp Retirement Kept": STAMP_RETIREMENT_KEPT_SQL,
                "Bot Line": ("-- the bot's line, as Delete Bot Files will run it. Parameters, in order: gate mode, install id, JSON\n"
                             "-- array of the profile ids, JSON array of the workflow ids.\nwith recursive "
                             + lineage_cte("$1", "$2", "$3", "$4")
                             + "\nselect (select coalesce(jsonb_agg(profile_id order by profile_id), '[]'::jsonb) from lineage_profiles) as profiles,"
                             "\n  (select coalesce(jsonb_agg(workflow_id order by workflow_id), '[]'::jsonb) from lineage_workflows) as workflows")})
    out.update({"List Bot Originals (line)": LINE_LIST_ORIGINALS_SQL, "Retire Bot Profiles (line)": LINE_RETIRE_SQL,
                "Delete Bot Chunks (line)": LINE_DELETE_CHUNKS_SQL})
    return out


def main(argv):
    stmts = statements()
    if "--list" in argv:
        print("\n".join(stmts))
        return 0
    if "--print" in argv:
        name = argv[argv.index("--print") + 1]
        print(stmts[name])
        return 0
    if "--check" in argv:
        spec = importlib.util.spec_from_file_location("vsc", os.path.join(HERE, "verify-sql-comments.py"))
        vsc = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(vsc)
        bad = [(name, *p) for name, q in stmts.items() for p in vsc.check_query(q)]
        for b in bad:
            print("SQL GUARD:", b)
        print(f"clone_lineage: {len(stmts)} statements, {len(bad)} findings")
        return 1 if bad else 0
    print(__doc__)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
