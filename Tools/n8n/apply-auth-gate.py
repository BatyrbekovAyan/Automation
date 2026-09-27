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
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}

GATED = {
    "0DP79STI3pafXO9B-Set_Workflow_State.json":     {"rule": {"workflows": ["workflowId"], "legacy": True}},
    "SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json":         {"rule": {"profiles": ["profileIds"], "legacy": True}},
    "9PTyYcelRQI7bGDb-Suggest_Replies.json":        {"rule": {"profiles": ["profileId"], "workflows": ["botWaId", "botTgId"], "appUser": ["appUserId"], "legacy": True}},
    "2htWSV5IHO8E2CgB-Dashboard_Outcomes.json":     {"rule": {"profiles": ["profileIds"], "legacy": True}},
    "jtbssfzXbOxwTK4k-Get_Usage.json":              {"rule": {"appUser": ["appUserId"], "legacy": True}},
    # Upload File is multipart: Unity's WWWForm files even its TEXT fields as binary parts, so `body` is empty
    # and the gate's RULE cannot see them. Their ownership + the fileId shape are checked AFTER the Extract chain
    # decodes them (patch_upload -> "Upload Fields Valid?"); the gate only has to carry the binary through (R3).
    "KoTuIlk4LMrlvnWI-Upload_File.json":            {"rule": {"workflows": ["whatsappWorkflowId", "telegramWorkflowId"], "legacy": True}},
    "ZTqpumOpL1rNDOp6-Delete_File.json":            {"rule": {"legacy": True}},
    "lmjYsdNcQA2IE5rl-Delete_Bot_Files.json":       {"rule": {"profiles": ["waProfileId", "tgProfileId"], "workflows": ["botWaId", "botTgId"], "appUser": ["appUserId"], "legacy": True}},
    "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": {"webhook": "Unity Webhook", "rule": {"profiles": ["WhatsappProfileId"], "workflows": ["TelegramWorkflowId"], "appUser": ["AppUserID"], "legacy": True}},
    "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": {"webhook": "Unity Webhook", "rule": {"profiles": ["TelegramProfileId"], "workflows": ["WhatsappWorkflowId"], "appUser": ["AppUserID"], "legacy": True}},
    "3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json": {"webhook": "Unity Webhook", "rule": {"workflows": ["WhatsappWorkflowId", "TelegramWorkflowId"], "legacy": True}},
    "TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json": {"webhook": "Unity Webhook", "rule": {"workflows": ["WhatsappWorkflowId", "TelegramWorkflowId"], "legacy": True}},
    "olmqWMfcFpCEcpiE-Support_Message.json":        {"rule": {"legacy": True}},
}
BOTS = ["4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json"]

REGISTER_UPLOAD_SQL = """-- A1: ownership registry for price-list files (the chunks carry only botWaId/botTgId, the stored
-- original nothing). owner_id is NULL for legacy-mode uploads; Delete File then falls back to the
-- chunk metadata. Runs as a side branch BEFORE the ingest chain (connection order) so the file is
-- registered by the time the client can ask to delete it.
insert into uploaded_files (file_id, owner_id, bot_wa_id, bot_tg_id, content_type, file_name)
values ($1, nullif($2, ''), nullif(nullif($3, ''), '-1'), nullif(nullif($4, ''), '-1'), $5, $6)
on conflict (file_id) do update set owner_id = coalesce(uploaded_files.owner_id, excluded.owner_id), deleted_at = null;"""

# R3: Extract From File (text, options.keepSource unset) emits ONLY its destination key in json, so at
# "Extract Telegram Workflow Id" `$json` is {telegramWorkflowId}: every other decoded part is read from the node
# that decoded it (a `$json.fileId` here is undefined -> SQL NULL file_id -> the insert failed silently).
REGISTER_UPLOAD_PARAMS = ("={{ [ $('Extract File Id').first().json.fileId, ($('Auth Gate').first().json.crAuth.installId || ''), "
                          "($('Extract Whatsapp Workflow Id').first().json.whatsappWorkflowId || ''), ($json.telegramWorkflowId || ''), "
                          "($('Extract Content Type').first().json.contentType || ''), "
                          "((($('Get File Name').first().binary || {}).data || {}).fileName || '') ] }}")

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

DELETE_CHUNKS_SQL = """-- A1: owner-scoped delete. $1 fileId, $2 gate mode (session|legacy), $3 install id, $4 JSON array of
-- the token's workflow ids. A session may delete a file it uploaded (uploaded_files) or, for a LEGACY
-- file with no registry row, one whose chunks belong to its own workflows. legacy mode = old behaviour
-- until the compat window closes. `allowed` gates the storage delete downstream.
WITH own AS (
  SELECT 1
  WHERE $2 = 'legacy'
     OR EXISTS (SELECT 1 FROM uploaded_files u WHERE u.file_id = $1 AND u.owner_id = $3)
     OR (NOT EXISTS (SELECT 1 FROM uploaded_files u WHERE u.file_id = $1)
         AND EXISTS (SELECT 1 FROM documents d WHERE d.metadata->>'fileId' = $1
                     AND (d.metadata->>'botWaId' IN (SELECT jsonb_array_elements_text($4::jsonb))
                       OR d.metadata->>'botTgId' IN (SELECT jsonb_array_elements_text($4::jsonb)))))
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

BIND_WORKFLOW_SQL = """-- A1: remember which clone drives this profile (ownership of workflow ids derives from it) and, in
-- session mode, stamp the creating install as owner if the profile has none yet.
insert into bot_profiles (profile_id, app_user_id, channel, workflow_id, owner_id, claimed_at)
select $1, coalesce(nullif($3, ''), $1), $4, $2, nullif($5, ''), case when nullif($5, '') is null then null else now() end
where nullif($1, '') is not null and $1 <> '-1'
on conflict (profile_id) do update set workflow_id = excluded.workflow_id,
  owner_id = coalesce(bot_profiles.owner_id, excluded.owner_id),
  claimed_at = coalesce(bot_profiles.claimed_at, excluded.claimed_at), deleted_at = null;"""


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
    """Validate the decoded multipart fields, then register every upload (the side branch that runs first —
    ownership for Delete File) and only then ingest. Extract Telegram Workflow Id -> Upload Fields Valid?:
    true -> [Register Upload, <the ingest fan-out>], false -> Respond Upload Forbidden (403)."""
    tg = node(wf, "Extract Telegram Workflow Id")
    changed = ensure_node(wf, {"parameters": {"operation": "executeQuery", "query": REGISTER_UPLOAD_SQL,
                                              "options": {"queryReplacement": REGISTER_UPLOAD_PARAMS}},
                               "id": nid("upload", "register"), "name": "Register Upload", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                               "position": [tg["position"][0] + 200, tg["position"][1] - 220],
                               "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"})
    changed |= ensure_node(wf, {"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                              "conditions": [{"id": nid("upload", "fields-cond"), "leftValue": UPLOAD_FIELDS_VALID, "rightValue": "",
                                                                              "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                                              "combinator": "and"}, "options": {}},
                                "id": nid("upload", "fields"), "name": "Upload Fields Valid?", "type": "n8n-nodes-base.if", "typeVersion": 2.2,
                                "position": [tg["position"][0] + 100, tg["position"][1] + 220]})
    changed |= ensure_node(wf, {"parameters": {"respondWith": "json", "responseBody": UPLOAD_FORBIDDEN_BODY, "options": {"responseCode": 403}},
                                "id": nid("upload", "forbidden"), "name": "Respond Upload Forbidden", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5,
                                "position": [tg["position"][0] + 300, tg["position"][1] + 420]})
    gate = [{"node": "Upload Fields Valid?", "type": "main", "index": 0}]
    outs = conn(wf, "Extract Telegram Workflow Id")[0]
    if outs != gate:                                   # first run: the old fan-out moves behind the check
        fanout = [c for c in outs if c["node"] not in ("Upload Fields Valid?", "Register Upload")]
        wf["connections"]["Extract Telegram Workflow Id"] = {"main": [gate]}
        changed = True
    else:
        fanout = [c for c in conn(wf, "Upload Fields Valid?")[0] if c["node"] != "Register Upload"]
    want = {"main": [[{"node": "Register Upload", "type": "main", "index": 0}] + fanout,
                     [{"node": "Respond Upload Forbidden", "type": "main", "index": 0}]]}
    if wf["connections"].get("Upload Fields Valid?") != want:
        wf["connections"]["Upload Fields Valid?"] = want
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


def patch_create(wf, profile_field, channel):
    """Bind the created clone's id (and the creating install) to the profile row."""
    src = node(wf, "Restamp RAG Chunks")
    changed = ensure_node(wf, {"parameters": {"operation": "executeQuery", "query": BIND_WORKFLOW_SQL,
                                              "options": {"queryReplacement": "={{ [ $('Unity Webhook').first().json.body." + profile_field + ", $('Get Created Workflow Id').first().json.id, ($('Unity Webhook').first().json.body.AppUserID || ''), '" + channel + "', ($('Auth Gate').first().json.crAuth.installId || '') ] }}"}},
                               "id": nid("create", channel, "bind"), "name": "Bind Workflow Id", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                               "position": [src["position"][0] + 200, src["position"][1] + 200],
                               "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"})
    if conn(wf, "Restamp RAG Chunks")[0] != [{"node": "Bind Workflow Id", "type": "main", "index": 0}]:
        wf["connections"]["Restamp RAG Chunks"] = {"main": [[{"node": "Bind Workflow Id", "type": "main", "index": 0}]]}
        changed = True
    if conn(wf, "Bind Workflow Id")[0] != [{"node": "Send New Workflows Id", "type": "main", "index": 0}]:
        wf["connections"]["Bind Workflow Id"] = {"main": [[{"node": "Send New Workflows Id", "type": "main", "index": 0}]]}
        changed = True
    return changed


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
