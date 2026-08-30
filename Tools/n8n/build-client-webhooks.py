#!/usr/bin/env python3
"""Deploy + export the two client-credential-replacement webhooks (2026-08-31).

Store-submission blocker fix: the app used to ship the n8n instance-admin REST key
(X-N8N-API-KEY on /api/v1/workflows activate/deactivate/DELETE) and the Telegram
support-bot token (api.telegram.org/bot<token>/sendMessage) inside the APK/IPA in
plaintext (StreamingAssets/secrets.json). Both are replaced by auth-free webhooks in
the app's existing URL-is-the-secret posture (see UsageClient.cs), so the admin key
and bot token never leave the server:

  POST /webhook/SetWorkflowState   body { workflowId, action: activate|deactivate|delete }
      Validates the id (^[A-Za-z0-9]{8,36}$), whitelists the action, REFUSES the
      canonical infra workflow ids (+ itself + the support relay) with 400
      `protected_workflow`, then calls n8n's own REST API on localhost:5678 with a
      server-side httpHeaderAuth credential. 200 {success:true} / 400 / 500.

  POST /webhook/SupportMessage     body { text }
      Trims/validates text (400 on empty, clamps to 4000 chars) and forwards to the
      owner's support chat via a server-side telegramApi credential. 200/400/500.

Canonical exports live in workflows/<id>-Set_Workflow_State.json and
<id>-Support_Message.json once first-deployed; when they exist this script imports
them VERBATIM (only rebinding credential ids / the support chat id, which are
masked to __TG_CRED__/__HDR_CRED__/__CHAT_ID__ in the committed files). Until then
--deploy builds them from the embedded templates.

Targets PROD (https://n8n.choosereply.com) by default — unlike the older dev-first
deployers — because these webhooks exist to strip prod credentials from the client.
Override with N8N_BASE_URL for the local dev instance.

Usage:
  python3 Tools/n8n/build-client-webhooks.py --deploy            # create creds + both workflows, activate
  python3 Tools/n8n/build-client-webhooks.py --update            # PUT canonical content onto stored ids
  python3 Tools/n8n/build-client-webhooks.py --export            # re-export canonical JSONs (masked)
  python3 Tools/n8n/build-client-webhooks.py --dry-run           # print payloads, no network

Key sources (in order): N8N_API_KEY env -> .secrets/prod-api-key.txt (when the base
is choosereply.com) -> Assets/StreamingAssets/secrets.json (legacy n8nAPIKey, no
longer present after the strip). Support-bot values: TELEGRAM_BOT_TOKEN /
SUPPORT_CHAT_ID env -> .secrets/support-bot.json -> secrets.json legacy keys.
Created credential/workflow ids are remembered in .secrets/client-webhooks-state.json.
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SECRETS = os.path.join(REPO, "Assets/StreamingAssets/secrets.json")
SUPPORT_STASH = os.path.join(HERE, ".secrets", "support-bot.json")
STATE_PATH = os.path.join(HERE, ".secrets", "client-webhooks-state.json")
PROD_KEY_PATH = os.path.join(HERE, ".secrets", "prod-api-key.txt")
BASE = os.environ.get("N8N_BASE_URL", "https://n8n.choosereply.com").rstrip("/")
WF_DIR = os.path.join(HERE, "workflows")

# The 17 canonical infra workflow ids (Tools/n8n/workflows/ filenames, ids preserved
# by the 2026-08-27 migration) — SetWorkflowState refuses to touch any of them. Bot
# workflows cloned from the two templates get fresh ids and are the only allowed targets.
PROTECTED_IDS = [
    "2htWSV5IHO8E2CgB",  # Dashboard Outcomes
    "2islisFH7jjLoPQM",  # Delete Orphan Profiles
    "3qax5J9u2qsT9Vao",  # Edit Whatsapp Workflow
    "4VN3gsFaC2HUYmcc",  # Telegram Bot (template)
    "4wYitz5ek30SVNlT",  # WhatsApp Bot (template)
    "9PTyYcelRQI7bGDb",  # Suggest Replies
    "KoTuIlk4LMrlvnWI",  # Upload File
    "SCLcpn6DMDG3Z4VN",  # Set Reply Mode
    "TwWPW3gIyjZS3foR",  # Edit Telegram Workflow
    "Uz6HBBUpAiUqVysB",  # CreateTelegramWorkflow
    "XuvOp7TxOImOAmlj",  # CreateWhatsappWorkflow
    "ZGYr6srzS3rSSXHp",  # RevenueCat Events
    "ZTqpumOpL1rNDOp6",  # Delete File
    "fXYpCXPKw92EzRz8",  # Profile Lifecycle Sweep
    "hHwvfOvTCS42pXnq",  # Landing Lead
    "jtbssfzXbOxwTK4k",  # Get Usage
    "lmjYsdNcQA2IE5rl",  # Delete Bot Files
]


def read_json(path):
    with open(path) as f:
        return json.load(f)


def api_key():
    k = os.environ.get("N8N_API_KEY")
    if k:
        return k.strip()
    if "choosereply.com" in BASE and os.path.exists(PROD_KEY_PATH):
        with open(PROD_KEY_PATH) as f:
            return f.read().strip()
    legacy = read_json(SECRETS).get("n8nAPIKey") if os.path.exists(SECRETS) else None
    if legacy:
        return legacy
    sys.exit("no n8n API key: set N8N_API_KEY or provide .secrets/prod-api-key.txt")


def support_values():
    token = os.environ.get("TELEGRAM_BOT_TOKEN", "")
    chat = os.environ.get("SUPPORT_CHAT_ID", "")
    if not (token and chat) and os.path.exists(SUPPORT_STASH):
        stash = read_json(SUPPORT_STASH)
        token = token or stash.get("telegramBotToken", "")
        chat = chat or stash.get("supportChatId", "")
    if not (token and chat) and os.path.exists(SECRETS):
        legacy = read_json(SECRETS)
        token = token or legacy.get("telegramBotToken", "")
        chat = chat or legacy.get("supportChatId", "")
    if not (token and chat):
        sys.exit("no support-bot values: set TELEGRAM_BOT_TOKEN + SUPPORT_CHAT_ID "
                 "or provide .secrets/support-bot.json")
    # Stash for future runs — secrets.json loses these keys after the client strip.
    os.makedirs(os.path.dirname(SUPPORT_STASH), exist_ok=True)
    with open(SUPPORT_STASH, "w") as f:
        json.dump({"telegramBotToken": token, "supportChatId": chat}, f, indent=2)
    return token, chat


def call(method, path, body=None):
    req = urllib.request.Request(f"{BASE}{path}", method=method)
    req.add_header("X-N8N-API-KEY", api_key())
    data = None
    if body is not None:
        req.add_header("Content-Type", "application/json")
        data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(req, data, timeout=30) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode()[:400]}


def load_state():
    return read_json(STATE_PATH) if os.path.exists(STATE_PATH) else {}


def save_state(state):
    os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
    with open(STATE_PATH, "w") as f:
        json.dump(state, f, indent=2)


# ---------------------------------------------------------------- templates

def validate_code(support_wf_id):
    ids = PROTECTED_IDS + ([support_wf_id] if support_wf_id else [])
    id_list = ",\n  ".join(f"'{i}'" for i in ids)
    return (
        "// Canonical infra ids are off-limits: this webhook may only touch the bot\n"
        "// workflows the app itself creates (fresh ids from the two templates).\n"
        f"const PROTECTED = new Set([\n  {id_list}\n]);\n"
        "PROTECTED.add($workflow.id);\n"
        "const body = $input.first().json.body || {};\n"
        "const id = typeof body.workflowId === 'string' ? body.workflowId.trim() : '';\n"
        "const action = typeof body.action === 'string' ? body.action.trim() : '';\n"
        "const ACTIONS = ['activate', 'deactivate', 'delete'];\n"
        "if (!/^[A-Za-z0-9]{8,36}$/.test(id) || !ACTIONS.includes(action))\n"
        "  return [{ json: { ok: false, error: 'bad_request' } }];\n"
        "if (PROTECTED.has(id))\n"
        "  return [{ json: { ok: false, error: 'protected_workflow' } }];\n"
        "return [{ json: { ok: true, id, action } }];"
    )


SUPPORT_VALIDATE = (
    "const body = $input.first().json.body || {};\n"
    "const raw = typeof body.text === 'string' ? body.text.trim() : '';\n"
    "return [{ json: { ok: raw.length > 0, text: raw.slice(0, 4000) } }];"
)


def if_true_node(name, left):
    return {
        "name": name, "type": "n8n-nodes-base.if", "typeVersion": 2.2,
        "position": [440, 0],
        "parameters": {
            "conditions": {
                "options": {"caseSensitive": True, "leftValue": "",
                            "typeValidation": "strict", "version": 2},
                "combinator": "and",
                "conditions": [{
                    "leftValue": left, "rightValue": True,
                    "operator": {"type": "boolean", "operation": "true",
                                 "singleValue": True},
                }],
            },
            "options": {},
        },
    }


def respond_node(name, body_expr, code=None):
    node = {
        "name": name, "type": "n8n-nodes-base.respondToWebhook",
        "typeVersion": 1.5, "position": [1000, 0],
        "parameters": {"respondWith": "json", "responseBody": body_expr,
                       "options": {}},
    }
    if code:
        node["parameters"]["options"]["responseCode"] = code
    return node


def support_template():
    return {
        "name": "Support Message",
        "settings": {"executionOrder": "v1"},
        "nodes": [
            {"name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2.1,
             "position": [0, 0], "webhookId": str(uuid.uuid4()),
             "parameters": {"httpMethod": "POST", "path": "SupportMessage",
                            "authentication": "none",
                            "responseMode": "responseNode", "options": {}}},
            {"name": "Validate", "type": "n8n-nodes-base.code", "typeVersion": 2,
             "position": [220, 0],
             "parameters": {"mode": "runOnceForAllItems", "language": "javaScript",
                            "jsCode": SUPPORT_VALIDATE}},
            if_true_node("Valid?", "={{ $json.ok }}"),
            {"name": "Send To Support Chat", "type": "n8n-nodes-base.telegram",
             "typeVersion": 1.2, "position": [700, -80],
             "parameters": {"chatId": "__CHAT_ID__", "text": "={{ $json.text }}",
                            "additionalFields": {"appendAttribution": False}},
             "credentials": {"telegramApi": {"id": "__TG_CRED__",
                                             "name": "Support Bot"}}},
            respond_node("Respond OK", "={{ { \"success\": true } }}"),
            respond_node("Respond Bad Request",
                         "={{ { \"success\": false, \"error\": \"bad_request\" } }}",
                         400),
        ],
        "connections": {
            "Webhook": {"main": [[{"node": "Validate", "type": "main", "index": 0}]]},
            "Validate": {"main": [[{"node": "Valid?", "type": "main", "index": 0}]]},
            "Valid?": {"main": [
                [{"node": "Send To Support Chat", "type": "main", "index": 0}],
                [{"node": "Respond Bad Request", "type": "main", "index": 0}],
            ]},
            "Send To Support Chat": {"main": [[{"node": "Respond OK", "type": "main",
                                                "index": 0}]]},
        },
    }


def state_template(support_wf_id):
    api = "http://localhost:5678/api/v1/workflows"
    http_common = {
        "authentication": "genericCredentialType",
        "genericAuthType": "httpHeaderAuth",
        "options": {"timeout": 20000},
    }
    creds = {"httpHeaderAuth": {"id": "__HDR_CRED__", "name": "n8n Admin API"}}
    delete_check = {
        "name": "Delete?", "type": "n8n-nodes-base.if", "typeVersion": 2.2,
        "position": [660, -80],
        "parameters": {
            "conditions": {
                "options": {"caseSensitive": True, "leftValue": "",
                            "typeValidation": "strict", "version": 2},
                "combinator": "and",
                "conditions": [{
                    "leftValue": "={{ $json.action }}", "rightValue": "delete",
                    "operator": {"type": "string", "operation": "equals"},
                }],
            },
            "options": {},
        },
    }
    return {
        "name": "Set Workflow State",
        "settings": {"executionOrder": "v1"},
        "nodes": [
            {"name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2.1,
             "position": [0, 0], "webhookId": str(uuid.uuid4()),
             "parameters": {"httpMethod": "POST", "path": "SetWorkflowState",
                            "authentication": "none",
                            "responseMode": "responseNode", "options": {}}},
            {"name": "Validate", "type": "n8n-nodes-base.code", "typeVersion": 2,
             "position": [220, 0],
             "parameters": {"mode": "runOnceForAllItems", "language": "javaScript",
                            "jsCode": validate_code(support_wf_id)}},
            if_true_node("Valid?", "={{ $json.ok }}"),
            delete_check,
            {"name": "Delete Workflow", "type": "n8n-nodes-base.httpRequest",
             "typeVersion": 4.2, "position": [880, -140],
             "parameters": {"method": "DELETE",
                            "url": "=" + api + "/{{ $json.id }}",
                            **http_common},
             "credentials": creds},
            {"name": "Toggle Workflow", "type": "n8n-nodes-base.httpRequest",
             "typeVersion": 4.2, "position": [880, -20],
             "parameters": {"method": "POST",
                            "url": "=" + api + "/{{ $json.id }}/{{ $json.action }}",
                            "sendBody": True, "contentType": "json",
                            "specifyBody": "json", "jsonBody": "{}",
                            **http_common},
             "credentials": creds},
            respond_node("Respond OK", "={{ { \"success\": true } }}"),
            respond_node("Respond Rejected",
                         "={{ { \"success\": false, \"error\": $json.error } }}",
                         400),
        ],
        "connections": {
            "Webhook": {"main": [[{"node": "Validate", "type": "main", "index": 0}]]},
            "Validate": {"main": [[{"node": "Valid?", "type": "main", "index": 0}]]},
            "Valid?": {"main": [
                [{"node": "Delete?", "type": "main", "index": 0}],
                [{"node": "Respond Rejected", "type": "main", "index": 0}],
            ]},
            "Delete?": {"main": [
                [{"node": "Delete Workflow", "type": "main", "index": 0}],
                [{"node": "Toggle Workflow", "type": "main", "index": 0}],
            ]},
            "Delete Workflow": {"main": [[{"node": "Respond OK", "type": "main",
                                           "index": 0}]]},
            "Toggle Workflow": {"main": [[{"node": "Respond OK", "type": "main",
                                           "index": 0}]]},
        },
    }


# ---------------------------------------------------------------- deploy plumbing

def canonical_path(state_key, state):
    wf_id = state.get(state_key)
    if not wf_id:
        return None
    suffix = ("Support_Message.json" if state_key == "supportWorkflowId"
              else "Set_Workflow_State.json")
    return os.path.join(WF_DIR, f"{wf_id}-{suffix}")


def load_workflow(state_key, state, template):
    path = canonical_path(state_key, state)
    if path and os.path.exists(path):
        return read_json(path)
    return template


def bind(workflow, tg_cred, hdr_cred, chat_id):
    text = json.dumps(workflow)
    text = text.replace("__TG_CRED__", tg_cred or "__TG_CRED__")
    text = text.replace("__HDR_CRED__", hdr_cred or "__HDR_CRED__")
    text = text.replace("__CHAT_ID__", chat_id or "__CHAT_ID__")
    return json.loads(text)


def mask(workflow, tg_cred, hdr_cred, chat_id):
    text = json.dumps(workflow, indent=2, ensure_ascii=False)
    if tg_cred:
        text = text.replace(tg_cred, "__TG_CRED__")
    if hdr_cred:
        text = text.replace(hdr_cred, "__HDR_CRED__")
    if chat_id:
        text = text.replace(chat_id, "__CHAT_ID__")
    return text


def ensure_credentials(state, dry):
    if not state.get("telegramCredId"):
        token, chat = support_values()
        state["supportChatId"] = chat
        if dry:
            print("would create telegramApi credential 'Support Bot'")
        else:
            code, resp = call("POST", "/api/v1/credentials",
                              {"name": "Support Bot", "type": "telegramApi",
                               "data": {"accessToken": token}})
            if code not in (200, 201):
                sys.exit(f"telegram credential create failed [{code}]: {resp}")
            state["telegramCredId"] = resp["id"]
            print(f"created telegramApi credential {resp['id']}")
    if not state.get("headerCredId"):
        if dry:
            print("would create httpHeaderAuth credential 'n8n Admin API'")
        else:
            code, resp = call("POST", "/api/v1/credentials",
                              {"name": "n8n Admin API", "type": "httpHeaderAuth",
                               "data": {"name": "X-N8N-API-KEY", "value": api_key()}})
            if code not in (200, 201):
                sys.exit(f"header credential create failed [{code}]: {resp}")
            state["headerCredId"] = resp["id"]
            print(f"created httpHeaderAuth credential {resp['id']}")
    save_state(state)


def create_or_update(state, state_key, workflow, update):
    payload = {k: workflow[k] for k in ("name", "nodes", "connections", "settings")}
    wf_id = state.get(state_key)
    if wf_id and update:
        code, resp = call("PUT", f"/api/v1/workflows/{wf_id}", payload)
        if code != 200:
            sys.exit(f"update {payload['name']} failed [{code}]: {resp}")
        print(f"updated {payload['name']} ({wf_id})")
    elif not wf_id:
        code, resp = call("POST", "/api/v1/workflows", payload)
        if code not in (200, 201):
            sys.exit(f"create {payload['name']} failed [{code}]: {resp}")
        wf_id = resp["id"]
        state[state_key] = wf_id
        save_state(state)
        print(f"created {payload['name']} ({wf_id})")
    code, resp = call("POST", f"/api/v1/workflows/{wf_id}/activate", {})
    if code != 200:
        sys.exit(f"activate {payload['name']} failed [{code}]: {resp}")
    print(f"activated {payload['name']} ({wf_id})")
    return wf_id


def export(state):
    tg, hdr, chat = (state.get("telegramCredId"), state.get("headerCredId"),
                     state.get("supportChatId"))
    for key in ("supportWorkflowId", "stateWorkflowId"):
        wf_id = state.get(key)
        if not wf_id:
            continue
        code, resp = call("GET", f"/api/v1/workflows/{wf_id}")
        if code != 200:
            sys.exit(f"export GET {wf_id} failed [{code}]: {resp}")
        keep = {k: resp[k] for k in ("name", "nodes", "connections", "settings",
                                     "staticData", "pinData", "triggerCount", "meta",
                                     "id", "active") if k in resp}
        path = canonical_path(key, state)
        with open(path, "w") as f:
            f.write(mask(keep, tg, hdr, chat) + "\n")
        print(f"exported {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--deploy", action="store_true")
    ap.add_argument("--update", action="store_true")
    ap.add_argument("--export", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    state = load_state()

    if args.dry_run:
        support = load_workflow("supportWorkflowId", state, support_template())
        wf_state = load_workflow("stateWorkflowId", state,
                                 state_template(state.get("supportWorkflowId")))
        print(json.dumps(support, indent=2)[:1500])
        print(json.dumps(wf_state, indent=2)[:1500])
        return

    if args.deploy or args.update:
        ensure_credentials(state, dry=False)
        tg, hdr = state["telegramCredId"], state["headerCredId"]
        chat = state["supportChatId"]
        support = bind(load_workflow("supportWorkflowId", state, support_template()),
                       tg, hdr, chat)
        create_or_update(state, "supportWorkflowId", support, args.update)
        # The state webhook's protected list bakes the support relay's real id.
        wf_state = bind(load_workflow("stateWorkflowId", state,
                                      state_template(state["supportWorkflowId"])),
                        tg, hdr, chat)
        create_or_update(state, "stateWorkflowId", wf_state, args.update)
        export(state)
    elif args.export:
        export(state)
    else:
        ap.print_help()


if __name__ == "__main__":
    main()
