#!/usr/bin/env python3
"""Derive local-dev n8n workflows from the canonical (Cloud-shaped) set.

Reads Tools/n8n/workflows/*.json, rewrites the n8n-API host to localhost,
fixes the trailing-space /activate typo, remaps credential ids to the local
instance's credentials (matched by credential NAME from ~/.n8n/database.sqlite),
and writes the result to Tools/n8n/workflows-local/ (gitignored).

Usage: python3 Tools/n8n/apply-dev-config.py
Then:  n8n import:workflow --separate --input=Tools/n8n/workflows-local/
"""
import json, os, sqlite3, sys, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(REPO, "Tools/n8n/workflows")
OUT = os.path.join(REPO, "Tools/n8n/workflows-local")
DB = os.path.expanduser("~/.n8n/database.sqlite")

CLOUD_API = "https://bagkz.app.n8n.cloud/api/v1"
LOCAL_API = "http://localhost:5678/api/v1"
# Public callback host registered WITH Wappi for incoming messages — must be the
# internet-reachable tunnel URL (Wappi cannot reach localhost). Passed via env so
# the dynamic quick-tunnel URL is not baked into the committed canonical workflows.
CLOUD_WEBHOOK = "https://bagkz.app.n8n.cloud/webhook/"
PUBLIC = os.environ.get("N8N_PUBLIC_URL", "").rstrip("/")

# Per-bot runtime clone sources. For DEV we simplify these to OpenAI-only (strip the
# Supabase/Cohere/Postgres RAG stack) so bots reply without standing up those services.
CLONE_SOURCE_IDS = {"4wYitz5ek30SVNlT", "4VN3gsFaC2HUYmcc"}
RAG_NODE_SUFFIXES = ("memoryPostgresChat", "vectorStoreSupabase", "rerankerCohere", "embeddingsOpenAi")
WINDOW_MEMORY_TYPE = "@n8n/n8n-nodes-langchain.memoryBufferWindow"


def local_credentials():
    if not os.path.exists(DB):
        sys.exit(f"local n8n DB not found at {DB} — start n8n once first")
    con = sqlite3.connect(DB)
    rows = con.execute("SELECT id, name FROM credentials_entity").fetchall()
    con.close()
    by_name = {}
    for cid, name in rows:
        by_name.setdefault(name, cid)
    return by_name


def remap_credentials(node, cred_by_name, missing):
    creds = node.get("credentials")
    if not isinstance(creds, dict):
        return
    for cred_type, ref in creds.items():
        if not isinstance(ref, dict):
            continue
        name = ref.get("name")
        if name in cred_by_name:
            ref["id"] = cred_by_name[name]
        elif name:
            missing.add(name)


def simplify_runtime(wf):
    """Strip the RAG stack (Postgres memory + Supabase vector store + Cohere reranker +
    OpenAI embeddings) from a clone-source bot template and replace the Postgres chat
    memory with an in-memory window buffer, so the per-bot runtime answers with OpenAI
    only. Reuses the Postgres node's per-conversation sessionKey. Idempotent."""
    nodes = wf.get("nodes", [])
    agent_name = next((n["name"] for n in nodes if n["type"].endswith(".agent")), None)

    # capture the Postgres memory's session params + position before deleting it
    pg = next((n for n in nodes if n["type"].endswith("memoryPostgresChat")), None)
    if pg:
        p = pg.get("parameters", {})
        session_params = {
            "sessionIdType": p.get("sessionIdType", "customKey"),
            "sessionKey": p.get("sessionKey", ""),
            "contextWindowLength": p.get("contextWindowLength", 10),
        }
        position = pg.get("position", [-2240, 32])
    else:
        session_params = {"contextWindowLength": 10}
        position = [-2240, 32]

    # delete the RAG nodes
    doomed = {n["name"] for n in nodes if any(n["type"].endswith(s) for s in RAG_NODE_SUFFIXES)}
    if doomed:
        wf["nodes"] = [n for n in nodes if n["name"] not in doomed]
        conns = wf.get("connections", {})
        for src in list(conns.keys()):
            if src in doomed:
                del conns[src]
                continue
            for ctype, branches in list(conns[src].items()):
                conns[src][ctype] = [
                    [link for link in branch if link.get("node") not in doomed]
                    for branch in branches
                ]
        wf["connections"] = conns

    # inject the window-buffer memory (idempotent) wired to the AI Agent
    already = any(n.get("type") == WINDOW_MEMORY_TYPE for n in wf["nodes"])
    if agent_name and not already:
        wf["nodes"].append({
            "id": str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-windowmem")),
            "name": "Window Memory",
            "type": WINDOW_MEMORY_TYPE,
            "typeVersion": 1.3,
            "position": position,
            "parameters": session_params,
        })
        wf.setdefault("connections", {}).setdefault("Window Memory", {})["ai_memory"] = [
            [{"node": agent_name, "type": "ai_memory", "index": 0}]
        ]
    return wf


def main():
    os.makedirs(OUT, exist_ok=True)
    cred_by_name = local_credentials()
    missing = set()
    webhook_unrewritten = []
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".json"))
    for f in files:
        text = open(os.path.join(SRC, f)).read()
        text = text.replace(CLOUD_API, LOCAL_API)
        if PUBLIC:
            text = text.replace(CLOUD_WEBHOOK, PUBLIC + "/webhook/")
        elif CLOUD_WEBHOOK in text:
            webhook_unrewritten.append(f)
        text = text.replace("/activate ", "/activate")  # trailing-space bug
        wf = json.loads(text)
        if wf.get("id") in CLONE_SOURCE_IDS:
            simplify_runtime(wf)
        for node in wf.get("nodes", []):
            remap_credentials(node, cred_by_name, missing)
        json.dump(wf, open(os.path.join(OUT, f), "w"), indent=2, ensure_ascii=False)
        print(f"  wrote {f}")
    print(f"\n{len(files)} workflows -> {OUT}")
    print(f"local credentials found: {sorted(cred_by_name)}")
    print(f"public webhook host (N8N_PUBLIC_URL): {PUBLIC or '(unset)'}")
    if missing:
        print(f"\n⚠️  credentials referenced but NOT found locally (create them, re-run): {sorted(missing)}")
    if webhook_unrewritten:
        print(f"\n⚠️  N8N_PUBLIC_URL not set — Wappi callback host left as Cloud in: {webhook_unrewritten}")
        print("    Set N8N_PUBLIC_URL=<tunnel-https-url> and re-run, or local-dev bots will")
        print("    register incoming webhooks against PRODUCTION Cloud n8n.")


if __name__ == "__main__":
    main()
