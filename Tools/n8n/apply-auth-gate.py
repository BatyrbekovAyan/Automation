#!/usr/bin/env python3
"""Idempotent migration (A1, audit stage 1): put the crAuth gate in front of every CLIENT webhook,
bind the Wappi callback to its registered profile in both bot templates, register uploads and
make file deletion owner-scoped. Edits the canonical Tools/n8n/workflows/*.json by node NAME;
re-runnable; `--check` exits 2 when any file lacks the current gate.

Per gated workflow the splice is:   Webhook -> Auth Gate (Code) -> Authorized? (If)
                                     true  -> the node(s) the Webhook used to feed
                                     false -> Respond 401 (responseNode) | Respond Unauthorized (Set, lastNode)
The gate code comes from Tools/n8n/auth/gate.js (+ token.js) with a per-workflow RULE (which body
fields must be ⊆ the token's profiles / workflows, which must equal its billing id, and whether
token-less LEGACY calls are admitted while CR_AUTH_COMPAT_UNTIL lies in the future).

    python3 Tools/n8n/apply-auth-gate.py            # patch in place
    python3 Tools/n8n/apply-auth-gate.py --check
"""
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
AUTH = os.path.join(HERE, "auth")
sys.path.insert(0, HERE)
import clone_lineage as cl  # noqa: E402  (the bot's line: Delete Bot Files deletes by it, T5)
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}

GATED = {
    # V12 (iteration 2026-09-28): the two DELETING webhooks no longer take ownership from the token's lists — those carry
    # LIVE rows only, so the owner of a bot the sweep retired was refused its own delete, and a stale token could still
    # name a profile another install has since claimed. Their gate verifies the token; Delete Ownership decides in SQL
    # (patch_delete_bot_files / patch_set_workflow_state). Activate keeps the token rule, as an explicit If (Token Owns?).
    "0DP79STI3pafXO9B-Set_Workflow_State.json":     {"rule": {"legacy": True}},
    "SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json":         {"rule": {"profiles": ["profileIds"], "legacy": True}},
    "9PTyYcelRQI7bGDb-Suggest_Replies.json":        {"rule": {"profiles": ["profileId"], "workflows": ["botWaId", "botTgId"], "appUser": ["appUserId"], "legacy": True}},
    "2htWSV5IHO8E2CgB-Dashboard_Outcomes.json":     {"rule": {"profiles": ["profileIds"], "legacy": True}},
    "jtbssfzXbOxwTK4k-Get_Usage.json":              {"rule": {"appUser": ["appUserId"], "legacy": True}},
    # Upload File is multipart: Unity's WWWForm files even its TEXT fields as binary parts, so `body` is empty
    # and the gate's RULE cannot see them. Their ownership + the fileId shape are checked AFTER the Extract chain
    # decodes them (patch_upload -> "Upload Fields Valid?"); the gate only has to carry the binary through (R3).
    "KoTuIlk4LMrlvnWI-Upload_File.json":            {"rule": {"workflows": ["whatsappWorkflowId", "telegramWorkflowId"], "legacy": True}},
    "ZTqpumOpL1rNDOp6-Delete_File.json":            {"rule": {"legacy": True}},
    # R2: DeleteBotFiles' appUserId is audit-only (the retire goes by profile id) — as a gated field it would lock a
    # billing-conflicted install (u = null) out of deleting its own bot. V12: its profile/workflow ids are decided by
    # Delete Ownership (the database), see above.
    "lmjYsdNcQA2IE5rl-Delete_Bot_Files.json":       {"rule": {"legacy": True}},
    # R2: in session mode Create must name the token's own billing id — an empty AppUserID used to skip Ensure
    # Subscriber / Count Channels / Register Profile and let the dialog quota fail open.
    "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": {"webhook": "Unity Webhook", "rule": {"profiles": ["WhatsappProfileId"], "workflows": ["TelegramWorkflowId"], "appUser": ["AppUserID"], "appUserRequired": ["AppUserID"], "legacy": True}},
    "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": {"webhook": "Unity Webhook", "rule": {"profiles": ["TelegramProfileId"], "workflows": ["WhatsappWorkflowId"], "appUser": ["AppUserID"], "appUserRequired": ["AppUserID"], "legacy": True}},
    "3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json": {"webhook": "Unity Webhook", "rule": {"workflows": ["WhatsappWorkflowId", "TelegramWorkflowId"], "legacy": True}},
    "TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json": {"webhook": "Unity Webhook", "rule": {"workflows": ["WhatsappWorkflowId", "TelegramWorkflowId"], "legacy": True}},
    "olmqWMfcFpCEcpiE-Support_Message.json":        {"rule": {"legacy": True}},
}
BOTS = ["4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json"]

# V4 (iteration 2026-09-28): ONE predicate of PROVEN ownership for a file that is NOT registered to the caller,
# generated into BOTH Register Upload and Delete File Chunks so the two can never drift (verify-auth-gate.py pins the
# fragment in both). The evidence is every workflow id the file is known under — its registry row's bot_wa_id /
# bot_tg_id and the botWaId / botTgId of its chunks — minus the sentinels; `proof` = at least one id is known AND
# every one of them is among the token's workflows. ALL, not SOME: in the compat window a token-less call can add a
# chunk of its own workflow to any fileId, and a legacy Create re-stamps Telegram-only chunks with its clone. The
# placeholders are the caller's (fileId, JSON array of the token's workflow ids); the text has no `$` + digit in it.
def file_evidence(file_param, workflows_param):
    return f"""file_known AS (
  SELECT k.wf FROM (
    SELECT u.bot_wa_id AS wf FROM uploaded_files u WHERE u.file_id = {file_param}
    UNION ALL SELECT u.bot_tg_id FROM uploaded_files u WHERE u.file_id = {file_param}
    UNION ALL SELECT d.metadata->>'botWaId' FROM documents d WHERE d.metadata->>'fileId' = {file_param}
    UNION ALL SELECT d.metadata->>'botTgId' FROM documents d WHERE d.metadata->>'fileId' = {file_param}
  ) k WHERE k.wf IS NOT NULL AND k.wf NOT IN ('', '-1')
), file_proof AS (
  SELECT count(*) > 0
     AND count(*) FILTER (WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements_text({workflows_param}::jsonb) t(w)
                                            WHERE t.w = file_known.wf)) = 0 AS proof
  FROM file_known
)"""


REGISTER_UPLOAD_SQL = """-- A1: ownership registry for price-list files (the chunks carry only botWaId/botTgId, the stored
-- original nothing). owner_id is NULL for legacy-mode uploads. R3 (2026-09-27): runs IN SEQUENCE before the ingest
-- and DECIDES it — `allowed` = the row belongs to the caller now. V4 (2026-09-28): the registry row decides FIRST
-- (the conflict clause below): the caller's own row is re-used, another install's row is refused, an owner-less row
-- is adopted by a session only with proof (file_proof — every workflow id the file is known under is the token's).
-- A fileId with NO row is registered to a session caller only when it has no chunk at all (a new upload; a fileId
-- with chunks but no row is somebody's pre-A1 file). A token-less legacy call keeps the A1 behaviour: any fileId
-- without an owned row is (re)registered with no owner. Parameters, in order: fileId, install id (empty for legacy),
-- WhatsApp workflow id, Telegram workflow id, content type, file name, JSON array of the token's workflow ids.
with """ + file_evidence("$1", "$7") + """, reg as (
  insert into uploaded_files (file_id, owner_id, bot_wa_id, bot_tg_id, content_type, file_name)
  select $1, nullif($2, ''), nullif(nullif($3, ''), '-1'), nullif(nullif($4, ''), '-1'), $5, $6
  where nullif($2, '') is null
     or exists (select 1 from uploaded_files u where u.file_id = $1)
     or not exists (select 1 from documents d where d.metadata->>'fileId' = $1)
  on conflict (file_id) do update set owner_id = coalesce(uploaded_files.owner_id, excluded.owner_id), deleted_at = null
    where uploaded_files.owner_id = excluded.owner_id
       or (uploaded_files.owner_id is null and (excluded.owner_id is null or (select proof from file_proof)))
  returning file_id
)
select exists (select 1 from reg) as allowed;"""

# R3: Extract From File (text, options.keepSource unset) emits ONLY its destination key in json, so at
# "Extract Telegram Workflow Id" `$json` is {telegramWorkflowId}: every other decoded part is read from the node
# that decoded it (a `$json.fileId` here is undefined -> SQL NULL file_id -> the insert failed silently). V4: the
# token's workflows come from Auth Gate for the same reason — via `$json` they would be '[]' and every proven
# adoption would be refused silently (test_upload.py pins the evaluated 7th value).
REGISTER_UPLOAD_PARAMS = ("={{ [ $('Extract File Id').first().json.fileId, ($('Auth Gate').first().json.crAuth.installId || ''), "
                          "($('Extract Whatsapp Workflow Id').first().json.whatsappWorkflowId || ''), ($json.telegramWorkflowId || ''), "
                          "($('Extract Content Type').first().json.contentType || ''), "
                          "((($('Get File Name').first().binary || {}).data || {}).fileName || ''), "
                          "JSON.stringify($('Auth Gate').first().json.crAuth.workflows || []) ] }}")

# R3: the multipart ids the gate cannot see. fileId must be a client-minted GUID in EVERY mode (it is spliced
# into the service-role storage URL — nothing path-like; every shipped client sends Guid.NewGuid()); in session
# mode each workflow id must be a sentinel (''/'-1', like the gate) or one the token owns. One boolean on an
# If node: an If passes the item through untouched, binary included, and nothing with a side effect runs
# before it. (No regex quantifier braces: `}}` would end the n8n expression early.)
UPLOAD_FIELDS_VALID = ("={{ ((f) => f.length === 36 && /^[0-9a-f-]+$/i.test(f) && f.split('-').map((p) => p.length).join() === '8,4,4,4,12')"
                       "(String($('Extract File Id').first().json.fileId || '')) && "
                       "($('Auth Gate').first().json.crAuth.mode !== 'session' || "
                       "[$('Extract Whatsapp Workflow Id').first().json.whatsappWorkflowId, $json.telegramWorkflowId]"
                       ".every((v) => v === undefined || v === null || v === '' || v === '-1' || "
                       "($('Auth Gate').first().json.crAuth.workflows || []).includes(v))) }}")
UPLOAD_FORBIDDEN_BODY = "={{ { \"success\": false, \"error\": \"forbidden\" } }}"
# The registry verdict is a SQL row; the ingest reads the FILE from its input item — re-emit the validated
# upload (json + binary) once the registry allowed it. n8n v1 runs siblings by canvas position, so the registry
# can only come first by being IN the chain (as a sibling branch it ran last, after the response: staging 2026-09-27).
UPLOAD_ITEM_JS = """// UPLOAD / Upload Item (generated by Tools/n8n/apply-auth-gate.py, R3): the registry allowed this upload —
// hand the validated item (json + the file in binary) to the ingest, which reads the file from its input.
return $('Upload Fields Valid?').all().map((it, i) => ({ json: it.json, binary: it.binary, pairedItem: { item: i } }));"""
UPLOAD_REGISTERED = "={{ $json.allowed === true }}"
UPLOAD_REFUSED_BODY = "={{ { \"success\": false, \"error\": $json.allowed === false ? \"forbidden\" : \"db_unavailable\" } }}"
UPLOAD_REFUSED_CODE = "={{ $json.allowed === false ? 403 : 503 }}"
UNREADABLE_BODY = "={{ { \"success\": false, \"error\": \"unreadable_file\" } }}"

# V1 (2026-09-28): an SQL comment never names a parameter as `$` + digit — the Postgres node formats the query with
# pg-promise, which fills `$N` inside comments too, and a newline in the value ends the comment (verify-sql-comments.py).
DELETE_CHUNKS_SQL = """-- A1: owner-scoped delete. Parameters, in order: fileId, gate mode (session|legacy), install id, JSON
-- array of the token's workflow ids. A session may delete a file registered to its install; V4 (2026-09-28): a file
-- registered to NO install (an owner-less compat-window row, or a pre-A1 file without a row) only with proof
-- (file_proof — every workflow id the file is known under is the token's); a file registered to another install
-- never. legacy mode = old behaviour until the compat window closes. `allowed` gates the storage delete downstream.
WITH """ + file_evidence("$1", "$4") + """, own AS (
  SELECT 1
  WHERE $2 = 'legacy'
     OR EXISTS (SELECT 1 FROM uploaded_files u WHERE u.file_id = $1 AND u.owner_id = $3)
     OR (NOT EXISTS (SELECT 1 FROM uploaded_files u WHERE u.file_id = $1 AND u.owner_id IS NOT NULL)
         AND (SELECT proof FROM file_proof))
), deleted AS (
  DELETE FROM documents
  WHERE metadata->>'fileId' = $1 AND $1 <> '' AND EXISTS (SELECT 1 FROM own)
  RETURNING id
), marked AS (
  UPDATE uploaded_files SET deleted_at = now() WHERE file_id = $1 AND EXISTS (SELECT 1 FROM own)
  RETURNING file_id
)
SELECT (SELECT count(*)::int FROM deleted) AS "deletedChunks", EXISTS (SELECT 1 FROM own) AS "allowed";"""

DELETE_CHUNKS_PARAMS = "={{ [ $json.body.fileId, $json.crAuth.mode, ($json.crAuth.installId || ''), JSON.stringify($json.crAuth.workflows || []) ] }}"

# Create WA/TG `Bind Workflow Id` (A1: the clone id + the creating install bound to the profile row) moved to the bot's
# line (F1 + F2, plan 2026-10-11 §1.5, T3): its ONE statement is clone_lineage.BIND_WORKFLOW_SQL and the node, its wiring
# and the retired `Restamp RAG Chunks` belong to Tools/n8n/apply-clone-lifecycle.py — like stage 4 owns the Claim
# bodies. patch_create below is that pointer; this file only gates Create (Auth Gate / Authorized? / RULE).


# V12 (iteration 2026-09-28): ownership for a DELETING call (Delete Bot Files, Set Workflow State delete/deactivate) is
# decided in the database by ONE statement, generated into both workflows. Not from the token: its lists hold LIVE rows
# only (the owner of a retired bot was refused its own delete) and live for 12 h (a stale token still named a profile
# another install had since claimed by code — M5). Not a new token claim either: every other gate would have to ignore
# it. The ids are checked in exactly the form the deleting nodes read them (a JSON string; the sentinels are '' and '-1'
# as in the gate) — M3: a 'null', a number or a missing field is no id and proves nothing.
DELETE_OWNERSHIP_SQL = """-- V12: may this install delete these ids? Decided here, never from the token's lists (live rows only, up to 12 h
-- old). A session may delete when its install is not revoked and EVERY id the delete will use is a string that belongs
-- to a bot_profiles row whose owner is this install, retired or not (a workflow id: the row it is bound to). A row
-- without an owner (never claimed) is never proof; at least one real id must be named. A token-less legacy call keeps
-- the old behaviour while the compat window is open. Read-only. `reason` says why a refusal happened: owned_elsewhere
-- (another install owns one of the ids) or unproven. Parameters, in order: gate mode, install id, JSON array of the
-- profile ids, JSON array of the workflow ids.
with ids as (
  select 'p'::text as kind, v #>> '{}' as id, jsonb_typeof(v) as t from jsonb_array_elements($3::jsonb) v
  union all
  select 'w'::text, v #>> '{}', jsonb_typeof(v) from jsonb_array_elements($4::jsonb) v
), named as (
  select kind, id from ids where t = 'string' and id not in ('', '-1')
), verdict as (
  select case
    when $1 = 'legacy' then 'ok'
    when $1 is distinct from 'session' or coalesce($2, '') = '' then 'unproven'
    when not exists (select 1 from installs i where i.install_id = $2 and i.revoked_at is null) then 'unproven'
    when exists (select 1 from ids where t <> 'string') then 'unproven'
    when not exists (select 1 from named) then 'unproven'
    when not exists (select 1 from named n where not exists (
           select 1 from bot_profiles bp where bp.owner_id = $2
              and ((n.kind = 'p' and bp.profile_id = n.id) or (n.kind = 'w' and bp.workflow_id = n.id)))) then 'ok'
    when exists (select 1 from named n join bot_profiles bp
                   on (n.kind = 'p' and bp.profile_id = n.id) or (n.kind = 'w' and bp.workflow_id = n.id)
                 where bp.owner_id is not null and bp.owner_id <> $2) then 'owned_elsewhere'
    else 'unproven' end as v
)
select (v = 'ok') as allowed, case when v = 'ok' then null else v end as reason from verdict;"""

DBF_OWNERSHIP_PARAMS = ("={{ [ $json.crAuth.mode, ($json.crAuth.installId || ''), "
                        "JSON.stringify([ ($json.body || {}).waProfileId ?? null, ($json.body || {}).tgProfileId ?? null ]), "
                        "JSON.stringify([ ($json.body || {}).botWaId ?? null, ($json.body || {}).botTgId ?? null ]) ] }}")
# Set Workflow State's Validate trims the id before it reaches the admin API: the check sees the same trimmed string.
SWS_OWNERSHIP_PARAMS = ("={{ [ $json.crAuth.mode, ($json.crAuth.installId || ''), '[]', "
                        "JSON.stringify([ typeof ($json.body || {}).workflowId === 'string' ? $json.body.workflowId.trim() : "
                        "(($json.body || {}).workflowId ?? null) ]) ] }}")
OWNED = "={{ $json.allowed === true }}"
OWNER_REFUSED_BODY = ("={{ { \"success\": false, \"error\": $json.allowed === false ? \"forbidden\" : \"db_unavailable\", "
                      "\"reason\": $json.allowed === false ? ($json.reason || \"unproven\") : null } }}")
OWNER_REFUSED_CODE = "={{ $json.allowed === false ? 403 : 503 }}"     # a DB fault is 503 — for the client a 403 is final

# V12 / M1: Delete Bot Files answers success only when the deletes really happened, and the stored originals go FIRST:
# they used to be deleted after the response from file ids the chunk delete returned, so a storage failure was never
# seen and a retry (the chunks already gone) could not find the files again.
# T5 (F1 + F2, plan 2026-10-11 §1.6): every deleting node reads the BOT'S LINE, not only the ids the app names — a
# re-paired / reconnected bot's earlier profiles and clones (the pre-F1 orphans of F1 §1.4, a superseded predecessor, the
# other channel's retired row) go with it, every line row becomes app_delete (never adopted again) and the line's clones
# are booked for the sweep's branch A. One fragment, one parameter set: clone_lineage (Delete Chat Memory composes the
# same head in apply-history-handoff.py). Delete Ownership above is unchanged — the line grows only through this install's
# own rows.
LIST_ORIGINALS_SQL = cl.LINE_LIST_ORIGINALS_SQL
LIST_ORIGINALS_PARAMS = cl.DBF_LINE_PARAMS
RETIRE_LINE_SQL = cl.LINE_RETIRE_SQL
DELETE_CHUNKS_LINE_SQL = cl.LINE_DELETE_CHUNKS_SQL
DBF_LINE_PARAMS = cl.DBF_LINE_PARAMS
SPLIT_FILE_IDS_JS = """// DELETE BOT FILES / Split File Ids (generated by Tools/n8n/apply-auth-gate.py, V12): one item per stored original to
// delete, or ONE marker item when there is none ({none}) or the listing failed ({listError}) — the chain goes on to
// Originals Settled either way (a node with no input items would end the execution without an answer).
const row = $('List Bot Originals').first().json || {};
if (!Array.isArray(row.fileIds)) return [{ json: { listError: true } }];
const ids = row.fileIds.filter((f) => typeof f === 'string' && f !== '');
return ids.length ? ids.map((fileId) => ({ json: { fileId } })) : [{ json: { none: true } }];"""
HAS_ORIGINAL = "={{ typeof $json.fileId === 'string' && $json.fileId !== '' }}"
ORIGINALS_SETTLED_JS = """// DELETE BOT FILES / Originals Settled (generated by Tools/n8n/apply-auth-gate.py, V12): did every stored original go?
// 200/204 = deleted, 404 (or the storage API's 400 «not_found») = already gone; no response, any other status or a failed
// listing = NOT settled. Re-emits the gate's item (the request body) for the deleting statements downstream.
const gate = $('Auth Gate').first().json;
let error = null, deleted = 0;
for (const it of $input.all()) {
  const r = it.json || {};
  if (r.none) continue;
  if (r.listError) { error = error || 'db_unavailable'; continue; }
  const s = Number(r.statusCode);
  const b = (r.body && typeof r.body === 'object') ? r.body : {};
  const gone = s === 200 || s === 204 || s === 404 || (s === 400 && (String(b.statusCode) === '404' || b.error === 'not_found'));
  if (r.error || !gone) error = error || 'storage_unavailable'; else deleted += 1;
}
return [{ json: { ...gate, originals: { ok: !error, error, deleted } }, pairedItem: { item: 0 } }];"""
# A token-less legacy client never reads the answer and cannot retry: a storage failure does not stop ITS delete.
ORIGINALS_DELETED = "={{ $json.originals.ok === true || $json.crAuth.mode !== 'session' }}"
# Spelled out field by field: n8n's expression sandbox rewrites a computed member access (`$json[k]`).
DELETION_SETTLED = ("={{ typeof $('Retire Bot Profiles').first().json.retiredProfiles === 'number' && "
                    "typeof $('Delete Bot Chunks').first().json.deletedChunks === 'number' && "
                    + " && ".join(f"typeof $json.{k} === 'number'" for k in ("deletedMemoryRows", "deletedOutcomes", "deletedReplyFlags",
                                                                           "deletedHistoryEvents", "deletedHandoffs", "deletedReplyEvents",
                                                                           "deletedChatStates", "deletedSuggestionSets")) + " }}")
DELETE_FAILED_BODY = "={{ { \"success\": false, \"error\": ($json.originals && $json.originals.error) || \"db_unavailable\" } }}"

# Set Workflow State: which calls the database decides, the token rule kept for everything else (activate), the item
# Validate needs back after the SQL row, and an idempotent delete (404 = already gone; F1 / a hand-deleted clone).
OWNERSHIP_ROUTE = ("={{ $json.crAuth.mode === 'session' && ['deactivate', 'delete'].includes(String(($json.body || {}).action ?? '').trim()) }}")
TOKEN_OWNS = ("={{ $json.crAuth.mode !== 'session' || ((v) => v === undefined || v === null || v === '' || v === '-1' || "
              "(typeof v === 'string' && ($json.crAuth.workflows || []).includes(v)))(($json.body || {}).workflowId) }}")
TOKEN_REFUSED_BODY = "={{ { \"success\": false, \"error\": \"unauthorized\", \"reason\": \"forbidden:workflowId\" } }}"
OWNER_ITEM_JS = """// SET WORKFLOW STATE / Owner Item (generated by Tools/n8n/apply-auth-gate.py, V12): Delete Ownership allowed this call —
// hand Validate the request again (after the SQL node `$json` is its verdict row, not the body).
return [{ json: $('Auth Gate').first().json, pairedItem: { item: 0 } }];"""
WORKFLOW_GONE = "={{ [200, 404].includes(Number($json.statusCode)) }}"
WORKFLOW_DELETE_FAILED_BODY = "={{ { \"success\": false, \"error\": \"delete_failed\", \"status\": $json.statusCode ?? null } }}"


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "crauth-gate:" + ":".join(parts)))


def gate_js(rule):
    tok = open(os.path.join(AUTH, "token.js"), encoding="utf-8").read()
    a, b = tok.index("// CRAUTH-TOKEN-BEGIN"), tok.index("// CRAUTH-TOKEN-END") + len("// CRAUTH-TOKEN-END")
    src = open(os.path.join(AUTH, "gate.js"), encoding="utf-8").read()
    return src.replace("// __CRAUTH_TOKEN__", tok[a:b]).replace("__RULE__", json.dumps(rule, ensure_ascii=False))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def conn(wf, src):
    return wf["connections"].setdefault(src, {"main": [[]]})["main"]


def ensure_node(wf, spec):
    existing = node(wf, spec["name"])
    if existing is None:
        wf["nodes"].append(spec)
        return True
    changed = False
    for k in ("parameters", "type", "typeVersion", "credentials", "alwaysOutputData", "onError"):
        if k in spec and existing.get(k) != spec[k]:
            existing[k] = spec[k]
            changed = True
    return changed


def gate_workflow(fname, cfg):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    wh_name = cfg.get("webhook", "Webhook")
    wh = node(wf, wh_name)
    assert wh and wh["type"] == "n8n-nodes-base.webhook", f"{fname}: webhook node {wh_name!r} missing"
    last_node_mode = wh["parameters"].get("responseMode", "onReceived") == "lastNode"
    x, y = wh["position"]
    changed = False
    changed |= ensure_node(wf, {"parameters": {"jsCode": gate_js(cfg["rule"])}, "id": nid(fname, "gate"), "name": "Auth Gate",
                                "type": "n8n-nodes-base.code", "typeVersion": 2, "position": [x + 200, y + 260]})
    changed |= ensure_node(wf, {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                              "conditions": [{"id": nid(fname, "cond"), "leftValue": "={{ $json.crAuth.ok }}", "rightValue": "",
                                                                              "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                                              "combinator": "and"}, "options": {}},
                                "id": nid(fname, "if"), "name": "Authorized?", "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": [x + 400, y + 260]})
    if last_node_mode:
        deny = "Respond Unauthorized"
        changed |= ensure_node(wf, {"parameters": {"assignments": {"assignments": [
            {"id": nid(fname, "deny-success"), "name": "success", "value": False, "type": "boolean"},
            {"id": nid(fname, "deny-error"), "name": "error", "value": "unauthorized", "type": "string"},
            {"id": nid(fname, "deny-reason"), "name": "reason", "value": "={{ $json.crAuth.reason }}", "type": "string"}]}, "options": {}},
            "id": nid(fname, "deny"), "name": deny, "type": "n8n-nodes-base.set", "typeVersion": 3.4, "position": [x + 600, y + 460]})
    else:
        deny = "Respond 401"
        changed |= ensure_node(wf, {"parameters": {"respondWith": "json",
                                                   "responseBody": "={{ { \"success\": false, \"error\": \"unauthorized\", \"reason\": $json.crAuth.reason } }}",
                                                   "options": {"responseCode": "={{ String($json.crAuth.reason || '').startsWith('forbidden') ? 403 : 401 }}"}},
                                    "id": nid(fname, "deny"), "name": deny, "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "position": [x + 600, y + 460]})
    # Rewire: whatever the webhook fed becomes the Authorized? true-branch.
    wh_out = conn(wf, wh_name)
    originals = [c for c in wh_out[0] if c["node"] != "Auth Gate"]
    if originals or wh_out[0] != [{"node": "Auth Gate", "type": "main", "index": 0}]:
        wh_out[0] = [{"node": "Auth Gate", "type": "main", "index": 0}]
        changed = True
    if conn(wf, "Auth Gate")[0] != [{"node": "Authorized?", "type": "main", "index": 0}]:
        wf["connections"]["Auth Gate"] = {"main": [[{"node": "Authorized?", "type": "main", "index": 0}]]}
        changed = True
    auth_out = conn(wf, "Authorized?")
    while len(auth_out) < 2:
        auth_out.append([])
    if originals:
        auth_out[0] = originals
        changed = True
    if auth_out[1] != [{"node": deny, "type": "main", "index": 0}]:
        auth_out[1] = [{"node": deny, "type": "main", "index": 0}]
        changed = True
    return wf, path, changed


def patch_upload(wf):
    """R3: validate the decoded multipart fields, then register the upload IN SEQUENCE (the registry decides
    whether this fileId may be written) and only then ingest; an unreadable PDF answers 422, never a bare 200.
      Extract Telegram Workflow Id -> Upload Fields Valid? --true--> Register Upload -> Upload Registered?
          --true--> Upload Item -> <the ingest fan-out: Switch, Merge(1)>
          --false-> Respond Upload Refused (403 forbidden / 503 db_unavailable)
      Upload Fields Valid? --false--> Respond Upload Forbidden (403)
      Extract from PDF --error output--> Respond Unreadable File (422 unreadable_file)"""
    tg = node(wf, "Extract Telegram Workflow Id")
    x, y = tg["position"]
    changed = ensure_node(wf, {"parameters": {"operation": "executeQuery", "query": REGISTER_UPLOAD_SQL,
                                              "options": {"queryReplacement": REGISTER_UPLOAD_PARAMS}},
                               "id": nid("upload", "register"), "name": "Register Upload", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                               "position": [x + 200, y - 220],
                               "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"})
    changed |= ensure_node(wf, {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                              "conditions": [{"id": nid("upload", "fields-cond"), "leftValue": UPLOAD_FIELDS_VALID, "rightValue": "",
                                                                              "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                                              "combinator": "and"}, "options": {}},
                                "id": nid("upload", "fields"), "name": "Upload Fields Valid?", "type": "n8n-nodes-base.if", "typeVersion": 2.2,
                                "position": [x + 100, y + 220]})
    changed |= ensure_node(wf, {"parameters": {"respondWith": "json", "responseBody": UPLOAD_FORBIDDEN_BODY, "options": {"responseCode": 403}},
                                "id": nid("upload", "forbidden"), "name": "Respond Upload Forbidden", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5,
                                "position": [x + 300, y + 420]})
    changed |= ensure_node(wf, {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                              "conditions": [{"id": nid("upload", "registered-cond"), "leftValue": UPLOAD_REGISTERED, "rightValue": "",
                                                                              "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                                              "combinator": "and"}, "options": {}},
                                "id": nid("upload", "registered"), "name": "Upload Registered?", "type": "n8n-nodes-base.if", "typeVersion": 2.2,
                                "position": [x + 400, y - 220]})
    changed |= ensure_node(wf, {"parameters": {"respondWith": "json", "responseBody": UPLOAD_REFUSED_BODY, "options": {"responseCode": UPLOAD_REFUSED_CODE}},
                                "id": nid("upload", "refused"), "name": "Respond Upload Refused", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5,
                                "position": [x + 600, y - 20]})
    changed |= ensure_node(wf, {"parameters": {"jsCode": UPLOAD_ITEM_JS}, "id": nid("upload", "item"), "name": "Upload Item",
                                "type": "n8n-nodes-base.code", "typeVersion": 2, "position": [x + 600, y - 220]})
    pdf = node(wf, "Extract from PDF")
    changed |= ensure_node(wf, {"parameters": {"respondWith": "json", "responseBody": UNREADABLE_BODY, "options": {"responseCode": 422}},
                                "id": nid("upload", "unreadable"), "name": "Respond Unreadable File", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5,
                                "position": [pdf["position"][0] + 300, pdf["position"][1] + 160]})
    if pdf.get("onError") != "continueErrorOutput":
        pdf["onError"] = "continueErrorOutput"
        changed = True
    # Wiring. The ingest fan-out moves (once) from wherever it hangs today to Upload Item.
    chain = ("Upload Fields Valid?", "Register Upload", "Upload Registered?", "Upload Item", "Respond Upload Forbidden", "Respond Upload Refused")
    fanout = None
    for src in ("Upload Item", "Upload Fields Valid?", "Extract Telegram Workflow Id"):
        outs = wf["connections"].get(src, {}).get("main", [])
        rest = [c for c in (outs[0] if outs else []) if c["node"] not in chain]
        if rest:
            fanout = rest
            break
    assert fanout, "Upload File: the ingest fan-out (Switch, Merge) was not found"
    want = {
        "Extract Telegram Workflow Id": {"main": [[{"node": "Upload Fields Valid?", "type": "main", "index": 0}]]},
        "Upload Fields Valid?": {"main": [[{"node": "Register Upload", "type": "main", "index": 0}],
                                          [{"node": "Respond Upload Forbidden", "type": "main", "index": 0}]]},
        "Register Upload": {"main": [[{"node": "Upload Registered?", "type": "main", "index": 0}]]},
        "Upload Registered?": {"main": [[{"node": "Upload Item", "type": "main", "index": 0}],
                                        [{"node": "Respond Upload Refused", "type": "main", "index": 0}]]},
        "Upload Item": {"main": [fanout]},
    }
    for src, conns in want.items():
        if wf["connections"].get(src) != conns:
            wf["connections"][src] = conns
            changed = True
    pdf_out = wf["connections"].setdefault("Extract from PDF", {"main": [[]]})["main"]
    while len(pdf_out) < 2:
        pdf_out.append([])
    if pdf_out[1] != [{"node": "Respond Unreadable File", "type": "main", "index": 0}]:
        pdf_out[1] = [{"node": "Respond Unreadable File", "type": "main", "index": 0}]
        changed = True
    return changed


def patch_delete_file(wf):
    """Owner-scoped chunk delete; the storage delete and the 200 only after `allowed`."""
    changed = False
    dc = node(wf, "Delete File Chunks")
    if dc["parameters"]["query"] != DELETE_CHUNKS_SQL or dc["parameters"]["options"].get("queryReplacement") != DELETE_CHUNKS_PARAMS:
        dc["parameters"]["query"] = DELETE_CHUNKS_SQL
        dc["parameters"]["options"]["queryReplacement"] = DELETE_CHUNKS_PARAMS
        changed = True
    changed |= ensure_node(wf, {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                              "conditions": [{"id": nid("delete", "allowed-cond"), "leftValue": "={{ $json.allowed }}", "rightValue": "",
                                                                              "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                                              "combinator": "and"}, "options": {}},
                                "id": nid("delete", "allowed"), "name": "Allowed?", "type": "n8n-nodes-base.if", "typeVersion": 2.2,
                                "position": [dc["position"][0] + 220, dc["position"][1]]})
    changed |= ensure_node(wf, {"parameters": {"respondWith": "json",
                                               "responseBody": "={{ { \"success\": false, \"error\": \"forbidden\", \"fileId\": $('Webhook').first().json.body.fileId } }}",
                                               "options": {"responseCode": 403}},
                                "id": nid("delete", "forbidden"), "name": "Respond Forbidden", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5,
                                "position": [dc["position"][0] + 440, dc["position"][1] + 200]})
    # Authorized? true -> ONLY Delete File Chunks (the storage delete no longer runs in parallel).
    auth_out = conn(wf, "Authorized?")
    if auth_out[0] != [{"node": "Delete File Chunks", "type": "main", "index": 0}]:
        auth_out[0] = [{"node": "Delete File Chunks", "type": "main", "index": 0}]
        changed = True
    if conn(wf, "Delete File Chunks")[0] != [{"node": "Allowed?", "type": "main", "index": 0}]:
        wf["connections"]["Delete File Chunks"] = {"main": [[{"node": "Allowed?", "type": "main", "index": 0}]]}
        changed = True
    want = {"main": [[{"node": "Respond", "type": "main", "index": 0}, {"node": "Delete Stored Original", "type": "main", "index": 0}],
                     [{"node": "Respond Forbidden", "type": "main", "index": 0}]]}
    if wf["connections"].get("Allowed?") != want:
        wf["connections"]["Allowed?"] = want
        changed = True
    dso = node(wf, "Delete Stored Original")
    if dso["parameters"]["url"] != "=https://mnwsdbqvehrkeqwnwpqb.supabase.co/storage/v1/object/price-lists/{{ $('Webhook').first().json.body.fileId }}":
        dso["parameters"]["url"] = "=https://mnwsdbqvehrkeqwnwpqb.supabase.co/storage/v1/object/price-lists/{{ $('Webhook').first().json.body.fileId }}"
        changed = True
    return changed


def if_spec(scope, name, left, position):
    return {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                          "conditions": [{"id": nid(scope, name, "cond"), "leftValue": left, "rightValue": "",
                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                          "combinator": "and"}, "options": {}},
            "id": nid(scope, name), "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": position}


def respond_spec(scope, name, body, code, position):
    return {"parameters": {"respondWith": "json", "responseBody": body, "options": {"responseCode": code}},
            "id": nid(scope, name), "name": name, "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "position": position}


def pg_spec(scope, name, query, params, position):
    return {"parameters": {"operation": "executeQuery", "query": query, "options": {"queryReplacement": params}},
            "id": nid(scope, name), "name": name, "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": position,
            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"}


def wire(wf, want):
    changed = False
    for src, outs in want.items():
        conns = {"main": [[{"node": t, "type": "main", "index": 0} for t in out] for out in outs]}
        if wf["connections"].get(src) != conns:
            wf["connections"][src] = conns
            changed = True
    return changed


def set_keys(n, **kv):
    changed = False
    for k, v in kv.items():
        if n.get(k) != v:
            n[k] = v
            changed = True
    return changed


def report_status(n, timeout=None):
    """An HTTP node whose status the next node judges: full response, never an error for a non-2xx."""
    opts = n["parameters"].setdefault("options", {})
    want = {"response": {"fullResponse": True, "neverError": True}}
    changed = False
    if opts.get("response") != want:
        opts["response"] = want
        changed = True
    if timeout is not None and opts.get("timeout") != timeout:
        opts["timeout"] = timeout
        changed = True
    return changed | set_keys(n, onError="continueRegularOutput")


def line_query(n, query):
    """T5: a deleting Postgres node of Delete Bot Files runs `query` over the bot's line with the line parameters."""
    want = {"query": query, "queryReplacement": DBF_LINE_PARAMS}
    p = n["parameters"]
    if p.get("operation") == "executeQuery" and p.get("query") == want["query"] and \
            (p.get("options") or {}).get("queryReplacement") == want["queryReplacement"]:
        return False
    p["operation"] = "executeQuery"
    p["query"] = query
    p.setdefault("options", {})["queryReplacement"] = DBF_LINE_PARAMS
    return True


def patch_delete_bot_files(wf):
    """V12: ownership in the database, the stored originals first, success only on real counters; T5: every deleting
    node (List Bot Originals, Retire Bot Profiles, Delete Bot Chunks — and Delete Chat Memory from apply-history-handoff)
    reads the bot's line (clone_lineage) with the line parameters.
      Authorized? -> Delete Ownership -> Owned? --true--> List Bot Originals -> Split File Ids -> Has Original?
          --true--> Delete Stored Original -> Originals Settled ; --false--> Originals Settled
        Originals Settled -> Originals Deleted? --true--> Retire Bot Profiles -> Delete Bot Chunks -> Delete Chat Memory
          -> Deletion Settled? --true--> Respond ; --false--> Respond Delete Failed (503)
        Originals Deleted? --false--> Respond Delete Failed (503) ; Owned? --false--> Respond Owner Refused (403 / 503)"""
    x, y = node(wf, "Authorized?")["position"]
    S = "dbf"
    changed = ensure_node(wf, pg_spec(S, "Delete Ownership", DELETE_OWNERSHIP_SQL, DBF_OWNERSHIP_PARAMS, [x + 220, y - 200]))
    changed |= ensure_node(wf, if_spec(S, "Owned?", OWNED, [x + 440, y - 200]))
    changed |= ensure_node(wf, respond_spec(S, "Respond Owner Refused", OWNER_REFUSED_BODY, OWNER_REFUSED_CODE, [x + 660, y]))
    changed |= ensure_node(wf, pg_spec(S, "List Bot Originals", LIST_ORIGINALS_SQL, LIST_ORIGINALS_PARAMS, [x + 660, y - 200]))
    split = node(wf, "Split File Ids")
    if split["parameters"].get("jsCode") != SPLIT_FILE_IDS_JS:
        split["parameters"]["jsCode"] = SPLIT_FILE_IDS_JS
        changed = True
    changed |= set_keys(split, position=[x + 880, y - 200])
    changed |= ensure_node(wf, if_spec(S, "Has Original?", HAS_ORIGINAL, [x + 1100, y - 200]))
    dso = node(wf, "Delete Stored Original")
    changed |= report_status(dso) | set_keys(dso, position=[x + 1320, y - 300])
    changed |= ensure_node(wf, {"parameters": {"jsCode": ORIGINALS_SETTLED_JS}, "id": nid(S, "Originals Settled"), "name": "Originals Settled",
                                "type": "n8n-nodes-base.code", "typeVersion": 2, "position": [x + 1540, y - 200]})
    changed |= ensure_node(wf, if_spec(S, "Originals Deleted?", ORIGINALS_DELETED, [x + 1760, y - 200]))
    for name, dx in (("Retire Bot Profiles", 1980), ("Delete Bot Chunks", 2200), ("Delete Chat Memory", 2420)):
        changed |= set_keys(node(wf, name), alwaysOutputData=True, onError="continueRegularOutput", position=[x + dx, y - 200])
    for name, query in (("Retire Bot Profiles", RETIRE_LINE_SQL), ("Delete Bot Chunks", DELETE_CHUNKS_LINE_SQL)):
        changed |= line_query(node(wf, name), query)      # Delete Chat Memory: apply-history-handoff.py (same head)
    changed |= ensure_node(wf, if_spec(S, "Deletion Settled?", DELETION_SETTLED, [x + 2640, y - 200]))
    changed |= ensure_node(wf, respond_spec(S, "Respond Delete Failed", DELETE_FAILED_BODY, 503, [x + 2860, y]))
    changed |= set_keys(node(wf, "Respond"), position=[x + 2860, y - 200])
    changed |= wire(wf, {
        "Authorized?": [["Delete Ownership"], [node_deny(wf)]],
        "Delete Ownership": [["Owned?"]],
        "Owned?": [["List Bot Originals"], ["Respond Owner Refused"]],
        "List Bot Originals": [["Split File Ids"]],
        "Split File Ids": [["Has Original?"]],
        "Has Original?": [["Delete Stored Original"], ["Originals Settled"]],
        "Delete Stored Original": [["Originals Settled"]],
        "Originals Settled": [["Originals Deleted?"]],
        "Originals Deleted?": [["Retire Bot Profiles"], ["Respond Delete Failed"]],
        "Retire Bot Profiles": [["Delete Bot Chunks"]],
        "Delete Bot Chunks": [["Delete Chat Memory"]],
        "Delete Chat Memory": [["Deletion Settled?"]],
        "Deletion Settled?": [["Respond"], ["Respond Delete Failed"]],
    })
    if "Respond" in wf["connections"]:      # the storage delete used to hang off the response
        del wf["connections"]["Respond"]
        changed = True
    return changed


def node_deny(wf):
    return "Respond Unauthorized" if node(wf, "Respond Unauthorized") else "Respond 401"


def patch_set_workflow_state(wf):
    """V12: delete/deactivate in session mode are decided by Delete Ownership; activate keeps the token rule (Token Owns?,
    the gate's own check for this one field); the delete is idempotent and a real failure is reported.
      Authorized? -> Ownership Route? --true--> Delete Ownership -> Owned? --true--> Owner Item -> Validate
                                      --false-> Token Owns? --true--> Validate ; --false--> Respond Forbidden (403)
      Owned? --false--> Respond Owner Refused (403 / 503)
      Delete Workflow -> Workflow Gone? --true--> Respond OK ; --false--> Respond Delete Failed (502)"""
    x, y = node(wf, "Authorized?")["position"]
    S = "sws"
    changed = ensure_node(wf, if_spec(S, "Ownership Route?", OWNERSHIP_ROUTE, [x + 220, y]))
    changed |= ensure_node(wf, pg_spec(S, "Delete Ownership", DELETE_OWNERSHIP_SQL, SWS_OWNERSHIP_PARAMS, [x + 440, y - 200]))
    changed |= ensure_node(wf, if_spec(S, "Owned?", OWNED, [x + 660, y - 200]))
    changed |= ensure_node(wf, {"parameters": {"jsCode": OWNER_ITEM_JS}, "id": nid(S, "Owner Item"), "name": "Owner Item",
                                "type": "n8n-nodes-base.code", "typeVersion": 2, "position": [x + 880, y - 200]})
    changed |= ensure_node(wf, respond_spec(S, "Respond Owner Refused", OWNER_REFUSED_BODY, OWNER_REFUSED_CODE, [x + 880, y - 400]))
    changed |= ensure_node(wf, if_spec(S, "Token Owns?", TOKEN_OWNS, [x + 440, y + 200]))
    changed |= ensure_node(wf, respond_spec(S, "Respond Forbidden", TOKEN_REFUSED_BODY, 403, [x + 660, y + 400]))
    dw = node(wf, "Delete Workflow")
    changed |= report_status(dw, timeout=20000)
    dx, dy = dw["position"]
    changed |= ensure_node(wf, if_spec(S, "Workflow Gone?", WORKFLOW_GONE, [dx + 220, dy]))
    changed |= ensure_node(wf, respond_spec(S, "Respond Delete Failed", WORKFLOW_DELETE_FAILED_BODY, 502, [dx + 440, dy - 160]))
    changed |= wire(wf, {
        "Authorized?": [["Ownership Route?"], [node_deny(wf)]],
        "Ownership Route?": [["Delete Ownership"], ["Token Owns?"]],
        "Delete Ownership": [["Owned?"]],
        "Owned?": [["Owner Item"], ["Respond Owner Refused"]],
        "Owner Item": [["Validate"]],
        "Token Owns?": [["Validate"], ["Respond Forbidden"]],
        "Delete Workflow": [["Workflow Gone?"]],
        "Workflow Gone?": [["Respond OK"], ["Respond Delete Failed"]],
    })
    return changed


def patch_create(wf, profile_field, channel):
    """F1 + F2 (T3): nothing to do here any more — Bind Workflow Id and the whole clone chain after the reply-mode seed
    are written by Tools/n8n/apply-clone-lifecycle.py (clone_lineage.BIND_WORKFLOW_SQL). Kept as the pointer, so the
    two generators never rewrite each other's nodes."""
    return False


def patch_bot(fname):
    """Callback binding: the profile named in the body must be the registered profile (= webhook path)."""
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    cond_list = node(wf, "If")["parameters"]["conditions"]["conditions"]
    binding = {"id": "c0a11bac-0000-4000-8000-0000000000a1",
               "leftValue": "={{ String($json.webhookUrl || '').split('/').pop() }}",
               "rightValue": "={{ $json.body.messages[0].profile_id }}",
               "operator": {"type": "string", "operation": "equals"}}
    changed = False
    if not any(c.get("id") == binding["id"] for c in cond_list):
        cond_list.append(binding)
        changed = True
    elif next(c for c in cond_list if c["id"] == binding["id"]) != binding:
        cond_list[[c["id"] for c in cond_list].index(binding["id"])] = binding
        changed = True
    if changed:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def check_all():
    bad = []
    for fname, cfg in GATED.items():
        wf = json.load(open(os.path.join(WF, fname), encoding="utf-8"))
        g = node(wf, "Auth Gate")
        if g is None or g["parameters"]["jsCode"] != gate_js(cfg["rule"]) or node(wf, "Authorized?") is None:
            bad.append(fname)
        elif fname.startswith("KoTuIlk4LMrlvnWI") and patch_upload(json.loads(json.dumps(wf))):
            bad.append(fname + " (upload field check / registry)")
        elif fname.startswith("ZTqpumOpL1rNDOp6") and patch_delete_file(json.loads(json.dumps(wf))):
            bad.append(fname + " (owner-scoped delete)")
        elif fname.startswith("lmjYsdNcQA2IE5rl") and patch_delete_bot_files(json.loads(json.dumps(wf))):
            bad.append(fname + " (V12 database ownership / honest answer)")
        elif fname.startswith("0DP79STI3pafXO9B") and patch_set_workflow_state(json.loads(json.dumps(wf))):
            bad.append(fname + " (V12 database ownership / idempotent delete)")
    for fname in BOTS:
        wf = json.load(open(os.path.join(WF, fname), encoding="utf-8"))
        if not any(c.get("id") == "c0a11bac-0000-4000-8000-0000000000a1" for c in node(wf, "If")["parameters"]["conditions"]["conditions"]):
            bad.append(fname)
    return bad


def main():
    if "--check" in sys.argv:
        bad = check_all()
        print("auth gate:", "OK" if not bad else f"MISSING/STALE in {bad}")
        sys.exit(2 if bad else 0)
    for fname, cfg in GATED.items():
        wf, path, changed = gate_workflow(fname, cfg)
        if fname.startswith("KoTuIlk4LMrlvnWI"):
            changed |= patch_upload(wf)
        if fname.startswith("ZTqpumOpL1rNDOp6"):
            changed |= patch_delete_file(wf)
        if fname.startswith("lmjYsdNcQA2IE5rl"):
            changed |= patch_delete_bot_files(wf)
        if fname.startswith("0DP79STI3pafXO9B"):
            changed |= patch_set_workflow_state(wf)
        if fname.startswith("XuvOp7TxOImOAmlj"):
            changed |= patch_create(wf, "WhatsappProfileId", "whatsapp")
        if fname.startswith("Uz6HBBUpAiUqVysB"):
            changed |= patch_create(wf, "TelegramProfileId", "telegram")
        if changed:
            with open(path, "w", encoding="utf-8") as fh:
                json.dump(wf, fh, indent=2, ensure_ascii=False)
                fh.write("\n")
        print(f"{'patched' if changed else 'up to date'}  {fname}")
    for fname in BOTS:
        print(f"{'patched' if patch_bot(fname) else 'up to date'}  {fname} (callback binding)")


if __name__ == "__main__":
    main()
