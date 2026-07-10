#!/usr/bin/env python3
"""Build + deploy the shared "Suggest Replies" n8n workflow (semi-auto Phase 2).

The 12th canonical workflow: a shared, always-active webhook (POST /webhook/SuggestReplies)
that takes the frozen v1 request contract, optionally runs tenant-scoped RAG pre-retrieval
(Supabase Vector Store, single-key botWaId filter, topK 5), calls one LLM (gpt-4o-mini,
strict structured JSON), validates the output (exactly 4 distinct enum-labeled moves,
hard-clamped, markdown-stripped, one retry then a safe error payload), and responds in-band
echoing requestSeq.

Mirrors the Dashboard Outcomes skeleton (Webhook -> Code -> httpRequest json_schema ->
Code parse -> Respond). RAG uses the vectorStoreSupabase node in `load` (Get Many) mode
with an embeddingsOpenAi sub-node (text-embedding-3-small — MUST match the Upload File
index model). Reads the n8n API key from Assets/StreamingAssets/secrets.json (n8nAPIKey)
or env N8N_API_KEY; deploys to the local dev instance (http://localhost:5678) by default.

Usage:
  python3 Tools/n8n/build-suggest-replies.py --stage front [--id-file PATH]
  python3 Tools/n8n/build-suggest-replies.py --stage full --update <id> [--id-file PATH]
  python3 Tools/n8n/build-suggest-replies.py --export <id> <out.json>

Credential ids are resolved by NAME from the target instance's SQLite DB (dev) so the
committed export carries the ids that actually work on the instance it was built on
(matching the Dashboard Outcomes precedent); prod replication remaps by credential name.
"""
import argparse
import json
import os
import sqlite3
import sys
import time
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SECRETS = os.path.join(REPO, "Assets/StreamingAssets/secrets.json")
DEV_DB = os.path.expanduser("~/.n8n/database.sqlite")
BASE = os.environ.get("N8N_BASE_URL", "http://localhost:5678").rstrip("/")

# Fallback credential ids (dev instance, resolved 2026-07-10). resolve_cred() overrides
# these from the live DB when available so the script is portable to prod replication.
FALLBACK_CREDS = {
    "openAiApi": ("WNHwKWlO2E9OClkA", "OpenAi account"),
    "supabaseApi": ("vrywn6AxQMlvbbzC", "Supabase"),
}

ENUM_LABELS = ["Ответ", "Уточнить", "Вариант", "К заказу", "Отложить", "Отказ"]


def api_key():
    k = os.environ.get("N8N_API_KEY")
    if k:
        return k
    with open(SECRETS) as f:
        return json.load(f)["n8nAPIKey"]


def resolve_cred(cred_type):
    """Return (id, name) for a credential type, preferring the live DB by name."""
    want_id, want_name = FALLBACK_CREDS[cred_type]
    if os.path.exists(DEV_DB):
        try:
            con = sqlite3.connect(DEV_DB)
            row = con.execute(
                "SELECT id, name FROM credentials_entity WHERE type=? ORDER BY name LIMIT 1",
                (cred_type,),
            ).fetchone()
            con.close()
            if row:
                return row[0], row[1]
        except Exception:
            pass
    return want_id, want_name


def req(method, path, body=None):
    url = f"{BASE}/api/v1{path}"
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method=method)
    r.add_header("X-N8N-API-KEY", api_key())
    r.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            raw = resp.read().decode()
            return resp.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        return e.code, {"error": e.read().decode()}


# ---------------------------------------------------------------------------
# Node code blocks (raw strings so JS \n escapes survive into the jsCode).
# ---------------------------------------------------------------------------
PREP_JS = r"""const b = $json.body || {};
let invalid = false;
if (b.v !== 1) invalid = true;
if (typeof b.chatId !== 'string' || !b.chatId) invalid = true;
if (!Array.isArray(b.messages) || b.messages.length === 0) invalid = true;

let messages = Array.isArray(b.messages) ? b.messages : [];
messages = messages.slice(-12).map(m => ({
  role: (m && m.role === 'client') ? 'client' : 'business',
  text: String((m && m.text) || '').slice(0, 500),
  ts: (m && typeof m.ts === 'number') ? m.ts : 0
}));

const ownerPrompt = String(b.ownerPrompt || '').slice(0, 500);
const catalog = String(b.catalog || '').slice(0, 1500);
const businessName = String(b.businessName || '');
const businessTypeId = String(b.businessTypeId || '');
const botWaId = (typeof b.botWaId === 'string') ? b.botWaId : '';
const steerTowardText = (b.steerTowardText === null || b.steerTowardText === undefined)
  ? null : String(b.steerTowardText).slice(0, 500);
const lastIncomingText = (b.lastIncomingText === null || b.lastIncomingText === undefined)
  ? null : String(b.lastIncomingText);

let queryText = '';
if (lastIncomingText && lastIncomingText.trim()) {
  queryText = lastIncomingText.trim();
} else {
  for (let i = messages.length - 1; i >= 0; i--) {
    if (messages[i].role === 'client' && messages[i].text.trim()) { queryText = messages[i].text.trim(); break; }
  }
}
queryText = queryText.slice(0, 500);

const skipRag = (botWaId === '' || botWaId === '-1' || !queryText);
const requestSeq = (b.requestSeq === undefined || b.requestSeq === null) ? 0 : b.requestSeq;

return [{ json: {
  v: 1, requestSeq, invalid,
  profileId: String(b.profileId || ''),
  chatId: String(b.chatId || ''),
  botWaId, businessTypeId, businessName, ownerPrompt, catalog,
  steerTowardText, lastIncomingText, messages, queryText, skipRag
} }];"""

STUB_JS = r"""const p = $('Prep').first().json;
return [{ json: { v: 1, requestSeq: p.requestSeq, suggestions: [], skipRag: p.skipRag, invalid: p.invalid, _stub: true } }];"""


def n(node_id, name, ntype, tv, pos, params, creds=None, extra=None):
    node = {
        "parameters": params,
        "type": ntype,
        "typeVersion": tv,
        "position": pos,
        "id": node_id,
        "name": name,
    }
    if creds:
        node["credentials"] = creds
    if extra:
        node.update(extra)
    return node


def bool_if_condition(expr, cid):
    return {
        "conditions": {
            "options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
            "conditions": [
                {
                    "id": cid,
                    "leftValue": expr,
                    "rightValue": "",
                    "operator": {"type": "boolean", "operation": "true", "singleValue": True},
                }
            ],
            "combinator": "and",
        },
        "options": {},
    }


def rag_nodes():
    """The conditional RAG pair: vectorStoreSupabase (load) + embeddingsOpenAi sub-node."""
    oa_id, oa_name = resolve_cred("openAiApi")
    sb_id, sb_name = resolve_cred("supabaseApi")
    retrieve = n(
        "c1000000-0000-4000-8000-000000000201",
        "Retrieve RAG",
        "@n8n/n8n-nodes-langchain.vectorStoreSupabase",
        1.3,
        [700, 200],
        {
            "mode": "load",
            "prompt": "={{ $json.queryText }}",
            "tableName": {"__rl": True, "value": "documents", "mode": "list", "cachedResultName": "documents"},
            "topK": 5,
            "includeDocumentMetadata": True,
            "options": {
                "queryName": "match_documents",
                "metadata": {"metadataValues": [{"name": "botWaId", "value": "={{ $json.botWaId }}"}]},
            },
        },
        creds={"supabaseApi": {"id": sb_id, "name": sb_name}},
        extra={"alwaysOutputData": True},
    )
    embed = n(
        "c1000000-0000-4000-8000-000000000202",
        "Embeddings",
        "@n8n/n8n-nodes-langchain.embeddingsOpenAi",
        1.2,
        [700, 400],
        {"options": {}, "model": "text-embedding-3-small"},
        creds={"openAiApi": {"id": oa_id, "name": oa_name}},
    )
    return retrieve, embed


def build_front():
    webhook = n(
        "c1000000-0000-4000-8000-000000000101",
        "Webhook",
        "n8n-nodes-base.webhook",
        2.1,
        [0, 0],
        {"httpMethod": "POST", "path": "SuggestReplies", "responseMode": "responseNode", "options": {}},
        extra={"webhookId": "b3f7a1c0-1111-4aaa-9bbb-000000000001"},
    )
    prep = n("c1000000-0000-4000-8000-000000000102", "Prep", "n8n-nodes-base.code", 2, [220, 0], {"jsCode": PREP_JS})
    if_skip = n(
        "c1000000-0000-4000-8000-000000000103",
        "If skipRag?",
        "n8n-nodes-base.if",
        2.2,
        [440, 0],
        bool_if_condition("={{ $json.skipRag }}", "1a2b3c4d-0001-4000-8000-000000000001"),
    )
    retrieve, embed = rag_nodes()
    stub = n("c1000000-0000-4000-8000-000000000104", "Stub Response", "n8n-nodes-base.code", 2, [960, 0], {"jsCode": STUB_JS})
    respond = n(
        "c1000000-0000-4000-8000-000000000105",
        "Respond",
        "n8n-nodes-base.respondToWebhook",
        1.5,
        [1180, 0],
        {"respondWith": "json", "responseBody": "={{ $json }}", "options": {}},
    )
    nodes = [webhook, prep, if_skip, retrieve, embed, stub, respond]
    connections = {
        "Webhook": {"main": [[{"node": "Prep", "type": "main", "index": 0}]]},
        "Prep": {"main": [[{"node": "If skipRag?", "type": "main", "index": 0}]]},
        "If skipRag?": {"main": [
            [{"node": "Stub Response", "type": "main", "index": 0}],   # TRUE  -> skip RAG
            [{"node": "Retrieve RAG", "type": "main", "index": 0}],    # FALSE -> RAG
        ]},
        "Retrieve RAG": {"main": [[{"node": "Stub Response", "type": "main", "index": 0}]]},
        "Embeddings": {"ai_embedding": [[{"node": "Retrieve RAG", "type": "ai_embedding", "index": 0}]]},
        "Stub Response": {"main": [[{"node": "Respond", "type": "main", "index": 0}]]},
    }
    return nodes, connections


def workflow_payload(stage):
    if stage == "front":
        nodes, connections = build_front()
    else:
        raise SystemExit(f"unknown stage: {stage}")
    return {
        "name": "Suggest Replies",
        "nodes": nodes,
        "connections": connections,
        "settings": {"executionOrder": "v1"},
    }


def deploy(stage, update_id=None, id_file=None):
    payload = workflow_payload(stage)
    if update_id:
        code, resp = req("PUT", f"/workflows/{update_id}", payload)
        wid = update_id
        action = "updated"
    else:
        code, resp = req("POST", "/workflows", payload)
        wid = resp.get("id")
        action = "created"
    if code not in (200, 201) or not wid:
        print(f"DEPLOY FAILED (HTTP {code}): {json.dumps(resp)[:800]}")
        sys.exit(1)
    print(f"workflow {action}: id={wid}")
    ac, ar = req("POST", f"/workflows/{wid}/activate")
    if ac != 200:
        print(f"ACTIVATE FAILED (HTTP {ac}): {json.dumps(ar)[:400]}")
        sys.exit(1)
    print(f"activated: {wid}")
    if id_file:
        with open(id_file, "w") as f:
            f.write(wid)
    time.sleep(2)  # let the production webhook path register
    print(f"webhook: {BASE}/webhook/SuggestReplies")
    return wid


def export_canonical(wid, out_path):
    code, wf = req("GET", f"/workflows/{wid}")
    if code != 200:
        print(f"EXPORT GET FAILED (HTTP {code})")
        sys.exit(1)
    canonical = {
        "name": wf["name"],
        "nodes": wf["nodes"],
        "connections": wf["connections"],
        "settings": wf.get("settings", {"executionOrder": "v1"}),
        "staticData": wf.get("staticData"),
        "pinData": wf.get("pinData", {}),
        "triggerCount": wf.get("triggerCount", 1),
        "meta": wf.get("meta", {}) or {},
        "id": wf["id"],
        "active": wf.get("active", True),
    }
    with open(out_path, "w") as f:
        json.dump(canonical, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"exported canonical -> {out_path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", choices=["front", "full"], default="front")
    ap.add_argument("--update", dest="update_id", default=None)
    ap.add_argument("--id-file", default=None)
    ap.add_argument("--export", nargs=2, metavar=("ID", "OUT"), default=None)
    args = ap.parse_args()
    if args.export:
        export_canonical(args.export[0], args.export[1])
        return
    deploy(args.stage, update_id=args.update_id, id_file=args.id_file)


if __name__ == "__main__":
    main()
