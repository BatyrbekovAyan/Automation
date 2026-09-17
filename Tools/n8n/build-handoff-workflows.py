#!/usr/bin/env python3
"""Stage 4 (A5): the REAL handoff — three canonical workflows, generated (edit here, re-run; ids fixed):

  crHandoffReq0001-Handoff_Request.json   sub-workflow the bot templates' `Create Handoff` TOOL calls:
                                          validates, upserts handoff_requests (unique per triggering event —
                                          a retried tool call or re-delivered event never makes a second
                                          request), pauses auto-replies in that chat, returns {ok, ...} to
                                          the model. The model may say «передал» ONLY on ok:true.
  crHandoffNtfy001-Handoff_Notifier.json  schedule (every 2 min): retried delivery of the owner notice
                                          (a self-message on the bot's own channel — no APNs/FCM needed,
                                          no client text in it); attempts/backoff on the request row, a
                                          failed delivery never loses the request.
  crHandoffsApi001-Handoffs.json          /webhook/Handoffs — the app's pending list + take/done
                                          (A1-gated, owner-scoped). `done` clears the chat's pause.

    python3 Tools/n8n/build-handoff-workflows.py            # write workflows/*.json
    python3 Tools/n8n/build-handoff-workflows.py --check    # exit 2 if the committed JSON is stale
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
_spec = importlib.util.spec_from_file_location("build_auth", os.path.join(HERE, "build-auth-workflows.py"))
auth = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(auth)

REQ_ID, REQ_NAME = "crHandoffReq0001", "Handoff Request"
NTFY_ID, NTFY_NAME = "crHandoffNtfy001", "Handoff Notifier"
API_ID, API_NAME = "crHandoffsApi001", "Handoffs"
MAX_NOTIFY_ATTEMPTS = 6


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "handoff:" + ":".join(parts)))


# ---------------------------------------------------------------------------------------------
VALIDATE_HANDOFF_JS = r"""// HANDOFF REQUEST / Validate (generated — build-handoff-workflows.py). The model fills reason /
// name / phone / summary; profile, chat and the triggering message id come from the calling run.
const j = $input.first().json || {};
const s = (v, max) => (typeof v === 'string' ? v.trim().slice(0, max) : '');
const profileId = s(j.profile_id, 128), chatId = s(j.chat_id, 128), messageId = s(j.source_message_id, 128);
if (!profileId || !chatId || !messageId) return [{ json: { ok: false, error: 'bad_request', detail: 'missing ids' } }];
let reason = s(j.reason, 32).toLowerCase();
if (!['order', 'question', 'escalation'].includes(reason)) reason = 'question';
const phone = s(j.phone, 40).replace(/[^\d+()\- ]/g, '').trim();
const name = s(j.name, 80);
const summary = s(j.summary, 500);
const data = {};
if (name) data.name = name;
if (phone) data.phone = phone;
return [{ json: { ok: true, profileId, chatId, messageId, reason, data: JSON.stringify(data), summary: summary || null } }];
"""

UPSERT_HANDOFF_SQL = """-- A5: the request row is the fact «нужен человек». One row per triggering event: a retried tool
-- call (same run) or a re-delivered event (same message id) lands on the same row. The chat is
-- paused for auto-replies until the owner acts (Read Reply Mode ORs handoff_paused).
with up as (
  insert into handoff_requests (profile_id, chat_id, source_message_id, reason, data, summary, created_by)
  values ($1, $2, $3, $4, $5::jsonb, $6, 'ai')
  on conflict (profile_id, chat_id, source_message_id) do update
    set data = handoff_requests.data || excluded.data,
        summary = coalesce(excluded.summary, handoff_requests.summary),
        updated_at = now()
  returning id, (xmax = 0) as created, status
), pause as (
  insert into chat_reply_state (profile_id, chat_id, handoff_paused) values ($1, $2, true)
  on conflict (profile_id, chat_id) do update set handoff_paused = true
  returning 1
)
select id, created, status from up;"""

TOOL_RESULT_JS = r"""// HANDOFF REQUEST / Tool Result: what the model sees. ok:true ONLY when the row is written.
const j = $input.first().json || {};
if (j.ok === false) return [{ json: { ok: false, error: j.error || 'bad_request', note: 'Заявку записать не удалось — не обещай клиенту передачу.' } }];
if (j.id === undefined || j.id === null) return [{ json: { ok: false, error: 'db_error', note: 'Заявку записать не удалось — не обещай клиенту передачу; предложи контакт из данных бизнеса.' } }];
return [{ json: { ok: true, handoff_id: Number(j.id), created: j.created !== false, status: j.status || 'open',
                  note: 'Заявка записана, владелец получит уведомление. Можно сказать клиенту, что заявка передана.' } }];
"""


def build_request():
    n, c = [], {}
    n.append({"parameters": {"inputSource": "workflowInputs", "workflowInputs": {"values": [
        {"name": k, "type": "string"} for k in ("profile_id", "chat_id", "source_message_id", "reason", "name", "phone", "summary")]}},
        "id": nid(REQ_ID, "trigger"), "name": "When Executed by Another Workflow", "type": "n8n-nodes-base.executeWorkflowTrigger",
        "typeVersion": 1.1, "position": [0, 200]})
    n.append(auth.code("Validate Handoff", VALIDATE_HANDOFF_JS, [220, 200]))
    n.append(auth.if_true("Valid?", "={{ $json.ok }}", [440, 200]))
    up = auth.postgres("Upsert Handoff", UPSERT_HANDOFF_SQL,
                       "={{ [ $json.profileId, $json.chatId, $json.messageId, $json.reason, $json.data, $json.summary ] }}", [660, 100])
    up["alwaysOutputData"] = True
    up["onError"] = "continueRegularOutput"
    n.append(up)
    n.append(auth.code("Tool Result", TOOL_RESULT_JS, [880, 200]))
    auth.edge(c, "When Executed by Another Workflow", "Validate Handoff")
    auth.edge(c, "Validate Handoff", "Valid?")
    auth.edge(c, "Valid?", "Upsert Handoff", 0)
    auth.edge(c, "Valid?", "Tool Result", 1)
    auth.edge(c, "Upsert Handoff", "Tool Result")
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(REQ_ID, REQ_NAME, n, c, settings)


# ---------------------------------------------------------------------------------------------
DUE_SQL = """-- A5: claim a batch of due notices (attempt counted BEFORE the send, next try in 10 min, hard cap
-- __MAX__ attempts); the request row itself is never touched by a delivery failure.
with due as (
  select id from handoff_requests
  where notify_state in ('pending', 'failed') and notify_attempts < __MAX__ and notify_next_at <= now() and status <> 'done'
  order by created_at limit 20
  for update skip locked
), claimed as (
  update handoff_requests h
    set notify_attempts = h.notify_attempts + 1, notify_next_at = now() + interval '10 minutes', updated_at = now()
  from due where h.id = due.id
  returning h.id, h.profile_id, h.chat_id, h.reason, h.notify_attempts,
    coalesce((select b.channel from bot_profiles b where b.profile_id = h.profile_id order by b.created_at desc nulls last limit 1), 'whatsapp') as channel
)
select * from claimed;""".replace("__MAX__", str(MAX_NOTIFY_ATTEMPTS))

COMPOSE_NOTICE_JS = r"""// HANDOFF NOTIFIER / Compose Notice (runs PER ITEM — one notice per due request): a self-message
// on the bot's own channel. No client text, no client number — the owner opens the app for the details.
const row = $('Due Notifications').item.json;
const st = $json || {};
const body = (st.body && typeof st.body === 'object') ? st.body : st;
const phone = String(body.phone || '').replace(/\D/g, '');
const chat = String(row.chat_id || '');
const tail = chat.replace(/\D/g, '').slice(-4) || '…';
const what = row.reason === 'order' ? 'новый заказ' : (row.reason === 'escalation' ? 'клиент ждёт ответа' : 'вопрос клиента');
const text = 'Choose Reply: ' + what + ' (чат …' + tail + '). Автоответ в этом чате приостановлен — откройте приложение → Сводка → «Нужен владелец».';
const base = row.channel === 'telegram' ? 'https://wappi.pro/tapi/' : 'https://wappi.pro/api/';
return { json: { id: row.id, skip: !phone, error: phone ? '' : 'no_phone', url: base + 'sync/message/send', profileId: row.profile_id,
                 recipient: phone, body: text } };
"""

SETTLE_NOTICE_JS = r"""// HANDOFF NOTIFIER / Settle Notice (runs PER ITEM): sent | failed (+ error). A failure keeps the
// request; the next sweep retries until the attempt cap.
const c = $('Compose Notice').item.json;
if (c.skip) return { json: { id: c.id, state: 'failed', error: c.error || 'no_phone' } };
const r = $json || {};
const status = Number(r.statusCode) || 0;
const body = (r.body && typeof r.body === 'object') ? r.body : {};
const ok = status === 200 && (body.status === 'done' || !!body.message_id);
return { json: { id: c.id, state: ok ? 'sent' : 'failed', error: ok ? '' : ('http ' + status + ' ' + JSON.stringify(body).slice(0, 200)) } };
"""

RECORD_NOTIFY_SQL = """update handoff_requests
   set notify_state = $2, notify_last_error = nullif($3, ''),
       notified_at = case when $2 = 'sent' then now() else notified_at end,
       updated_at = now()
 where id = $1::bigint
returning id, notify_state, notify_attempts;"""


def build_notifier():
    n, c = [], {}
    n.append({"parameters": {"rule": {"interval": [{"field": "minutes", "minutesInterval": 2}]}}, "id": nid(NTFY_ID, "trigger"),
              "name": "Every 2 Minutes", "type": "n8n-nodes-base.scheduleTrigger", "typeVersion": 1.2, "position": [0, 200]})
    n.append(auth.postgres("Due Notifications", DUE_SQL, "={{ [] }}", [220, 200]))
    n.append(auth.http_wappi("Owner Phone", "GET", "={{ ($json.channel === 'telegram' ? 'https://wappi.pro/tapi/' : 'https://wappi.pro/api/') + 'sync/get/status' }}", [440, 200],
                             query_expr="={{ JSON.stringify({ profile_id: $json.profile_id }) }}", timeout=20000))
    n.append(auth.code("Compose Notice", COMPOSE_NOTICE_JS, [660, 200]))
    n[-1]["parameters"]["mode"] = "runOnceForEachItem"     # one notice per due request, never only the first
    n.append(auth.if_true("Skip?", "={{ $json.skip }}", [880, 200]))
    n.append(auth.http_wappi("Send Notice", "POST", "={{ $json.url }}", [1100, 300],
                             query_expr="={{ JSON.stringify({ profile_id: $json.profileId }) }}",
                             body_expr="={{ JSON.stringify({ recipient: $json.recipient, body: $json.body }) }}", timeout=30000))
    n.append(auth.code("Settle Notice", SETTLE_NOTICE_JS, [1320, 200]))
    n[-1]["parameters"]["mode"] = "runOnceForEachItem"
    n.append(auth.postgres("Record Notify", RECORD_NOTIFY_SQL, "={{ [ $json.id, $json.state, $json.error ] }}", [1540, 200]))
    auth.edge(c, "Every 2 Minutes", "Due Notifications")
    auth.edge(c, "Due Notifications", "Owner Phone")
    auth.edge(c, "Owner Phone", "Compose Notice")
    auth.edge(c, "Compose Notice", "Skip?")
    auth.edge(c, "Skip?", "Settle Notice", 0)
    auth.edge(c, "Skip?", "Send Notice", 1)
    auth.edge(c, "Send Notice", "Settle Notice")
    auth.edge(c, "Settle Notice", "Record Notify")
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(NTFY_ID, NTFY_NAME, n, c, settings)


# ---------------------------------------------------------------------------------------------
VALIDATE_API_JS = r"""// HANDOFFS / Validate (generated). Owner-scoped by the gate (profileIds ⊆ token.p); defensive
// re-intersection here so the SQL only ever sees the caller's own profiles.
const wh = $input.first().json || {};
const b = wh.body || {};
const owned = new Set((wh.crAuth && wh.crAuth.profiles) || []);
const op = typeof b.op === 'string' ? b.op : 'list';
if (!['list', 'take', 'done'].includes(op)) return [{ json: { ok: false, error: 'bad_request', detail: 'op' } }];
const ids = Array.isArray(b.profileIds) ? b.profileIds.filter(x => typeof x === 'string' && x && x !== '-1' && owned.has(x)).slice(0, 20) : [];
if (ids.length === 0) return [{ json: { ok: false, error: 'bad_request', detail: 'profileIds' } }];
const id = Number(b.id);
if (op !== 'list' && !(Number.isInteger(id) && id > 0)) return [{ json: { ok: false, error: 'bad_request', detail: 'id' } }];
return [{ json: { ok: true, op, profileIds: JSON.stringify(ids), id: op === 'list' ? 0 : id } }];
"""

HANDOFFS_SQL = """-- A5: the owner's pending requests (+ take/done in the same statement). `done` is the explicit
-- owner action that also lifts the chat's pause — unless another request in that chat is still open.
with p as (
  select jsonb_array_elements_text($1::jsonb) as profile_id
), taken as (
  update handoff_requests h set status = 'taken', taken_at = coalesce(h.taken_at, now()), updated_at = now()
  where $2 = 'take' and h.id = $3::bigint and h.status = 'open' and h.profile_id in (select profile_id from p)
  returning h.id
), done as (
  update handoff_requests h set status = 'done', done_at = now(), updated_at = now()
  where $2 = 'done' and h.id = $3::bigint and h.status <> 'done' and h.profile_id in (select profile_id from p)
  returning h.id, h.profile_id, h.chat_id
), unpause as (
  update chat_reply_state c set handoff_paused = false
  from done d
  where c.profile_id = d.profile_id and c.chat_id = d.chat_id
    and not exists (select 1 from handoff_requests o
                    where o.profile_id = d.profile_id and o.chat_id = d.chat_id and o.status <> 'done' and o.id <> d.id)
  returning 1
)
select
  coalesce((select json_agg(json_build_object(
      'id', h.id, 'profileId', h.profile_id, 'chatId', h.chat_id, 'reason', h.reason,
      'status', case when $2 = 'take' and h.id = $3::bigint then 'taken' else h.status end,
      'summary', h.summary, 'data', h.data,
      'createdAt', (extract(epoch from h.created_at) * 1000)::bigint,
      'notifyState', h.notify_state,
      'paused', coalesce(c.handoff_paused, false)) order by h.created_at desc)
    from handoff_requests h
    left join chat_reply_state c on c.profile_id = h.profile_id and c.chat_id = h.chat_id
    where h.profile_id in (select profile_id from p) and h.status <> 'done'
      and not ($2 = 'done' and h.id = $3::bigint)), '[]'::json) as requests,
  coalesce((select json_agg(json_build_object('profileId', c.profile_id, 'chatId', c.chat_id))
    from chat_reply_state c
    where c.profile_id in (select profile_id from p) and c.handoff_paused
      and not exists (select 1 from done d where d.profile_id = c.profile_id and d.chat_id = c.chat_id
                      and not exists (select 1 from handoff_requests o where o.profile_id = d.profile_id and o.chat_id = d.chat_id and o.status <> 'done' and o.id <> d.id))),
    '[]'::json) as "pausedChats",
  (select count(*) from taken) + (select count(*) from done) as changed;"""


def build_api():
    n, c = [], {}
    n.append(auth.webhook("Handoffs", [0, 300]))
    n.append(auth.code("Auth Gate", auth.gate_code({"profiles": ["profileIds"], "workflows": [], "appUser": [], "legacy": False}), [220, 300]))
    n.append(auth.if_true("Authorized?", "={{ $json.crAuth.ok }}", [440, 300]))
    n.append(auth.respond("Respond 401", [660, 520], body="={{ { success: false, error: 'unauthorized', reason: $json.crAuth.reason } }}",
                          code_expr="={{ String($json.crAuth.reason || '').startsWith('forbidden') ? 403 : 401 }}"))
    n.append(auth.code("Validate", VALIDATE_API_JS, [660, 200]))
    n.append(auth.if_true("Valid?", "={{ $json.ok }}", [880, 200]))
    n.append(auth.respond_fixed("Respond 400", [1100, 420], {"success": False, "error": "bad_request"}, 400))
    q = auth.postgres("Handoffs Query", HANDOFFS_SQL, "={{ [ $json.profileIds, $json.op, $json.id ] }}", [1100, 100])
    q["alwaysOutputData"] = True
    q["onError"] = "continueRegularOutput"
    n.append(q)
    n.append(auth.code("Shape Response", r"""const r = $input.first().json || {};
if (r.requests === undefined) return [{ json: { success: false, error: 'db_error', _status: 500 } }];
return [{ json: { success: true, requests: r.requests || [], pausedChats: r.pausedChats || [], changed: Number(r.changed) || 0, _status: 200 } }];""", [1320, 100]))
    n.append(auth.respond("Respond", [1540, 100], body="={{ { success: $json.success, error: $json.error, requests: $json.requests, pausedChats: $json.pausedChats, changed: $json.changed } }}",
                          code_expr="={{ $json._status }}"))
    auth.edge(c, "Webhook", "Auth Gate"); auth.edge(c, "Auth Gate", "Authorized?")
    auth.edge(c, "Authorized?", "Validate", 0); auth.edge(c, "Authorized?", "Respond 401", 1)
    auth.edge(c, "Validate", "Valid?")
    auth.edge(c, "Valid?", "Handoffs Query", 0); auth.edge(c, "Valid?", "Respond 400", 1)
    auth.edge(c, "Handoffs Query", "Shape Response"); auth.edge(c, "Shape Response", "Respond")
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(API_ID, API_NAME, n, c, settings)


def main():
    stale = []
    for wf, fname in ((build_request(), f"{REQ_ID}-Handoff_Request.json"), (build_notifier(), f"{NTFY_ID}-Handoff_Notifier.json"), (build_api(), f"{API_ID}-Handoffs.json")):
        path = os.path.join(WF, fname)
        text = json.dumps(wf, indent=2, ensure_ascii=False) + "\n"
        if "--check" in sys.argv:
            if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
                stale.append(fname)
        else:
            with open(path, "w", encoding="utf-8") as fh:
                fh.write(text)
            print("wrote", fname, f"({len(wf['nodes'])} nodes)")
    if "--check" in sys.argv:
        print("handoff workflows:", "OK" if not stale else f"STALE {stale}")
        sys.exit(2 if stale else 0)


if __name__ == "__main__":
    main()
