#!/usr/bin/env python3
"""One-time, idempotent migration of the CANONICAL n8n workflows for the RAG
restore + fixes (see docs/superpowers/specs/2026-06-30-rag-restore-and-fix-dev-design.md).

Edits Tools/n8n/workflows/*.json IN PLACE, by node name/type (never index),
preserving indent=2 / ensure_ascii=False formatting. Re-runnable: running twice is a no-op.

Task 0 confirmed shapes (via n8n-mcp get_node_types on the live instance):
  - embeddingsOpenAi v1.2: `model` is a plain string (node default already text-embedding-3-small).
  - documentDefaultDataLoader v1.1: `textSplittingMode: 'custom'` exposes the ai_textSplitter sub-node input.
  - textSplitterRecursiveCharacterTextSplitter v1: `chunkSize` / `chunkOverlap` are plain numbers.
  - inbound body carries messages[0].profile_id on BOTH WhatsApp and Telegram (used for sessionKey namespace).
"""
import json, os, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
BOT_IDS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")
UPLOAD = "KoTuIlk4LMrlvnWI-Upload_File.json"
EMBED_MODEL = "text-embedding-3-small"


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as f:
        return json.load(f)


def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)  # match source: no trailing newline


def find(nodes, name=None, type_suffix=None):
    for n in nodes:
        if name is not None and n["name"] == name:
            return n
        if type_suffix is not None and n["type"].endswith(type_suffix):
            return n
    return None


def delete_nodes(wf, doomed):
    """Remove nodes by name and prune every dangling connection (as a source key
    and inside other sources' output branches). Mirrors apply-dev-config.py."""
    wf["nodes"] = [n for n in wf["nodes"] if n["name"] not in doomed]
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


def fix_bot(wf):
    # 1. Delete the unconfigured Cohere reranker + its ai_reranker connection.
    delete_nodes(wf, {"Reranker Cohere"})
    nodes = wf["nodes"]

    # 2. Supabase retrieval tool: reranker off, smaller topK, sharper tool description.
    sup = find(nodes, name="Supabase Vector Store")
    sup["parameters"]["useReranker"] = False
    sup["parameters"]["topK"] = 10
    sup["parameters"]["toolDescription"] = (
        "Retrieve product details, prices, services, and any uploaded catalog or "
        "document content for this business. Use whenever the customer asks about "
        "products, pricing, availability, or specifics that may be in uploaded documents."
    )

    # 3. Pin the retrieve-side embedding model (MUST match the Upload File insert side).
    find(nodes, name="OpenAI Embedding")["parameters"]["model"] = EMBED_MODEL

    # 4. Namespaced, correct sessionKey (kills the dead `||` and the cross-bot leak).
    find(nodes, name="Chat Memory")["parameters"]["sessionKey"] = (
        "={{ $('Webhook').item.json.body.messages[0].profile_id + ':' + "
        "$('Webhook').item.json.body.messages[0].from }}"
    )
    return wf


def fix_upload(wf):
    # Part A: delete verified-dead orphans + the disabled AI-cleaner chain.
    delete_nodes(wf, {
        "Supabase Vector Store1", "Embeddings OpenAI1", "Data Loader1",  # orphan trio
        "Prepare AI Prompt", "AI Cleaner", "Extract Clean Text",          # disabled chain
    })
    # Part B: replace the product-marker chunker with a native recursive splitter.
    # Remove the marker hack first (PDF becomes prose, no product[n]: prefixing).
    delete_nodes(wf, {"Normalize PDF", "Split into Chunks"})
    nodes = wf["nodes"]; conns = wf["connections"]

    # Rewire the two edges the deletions broke:
    #   Extract from PDF -> Merge(0)     (was: -> Normalize PDF -> Merge)
    #   Source Text      -> Supabase(0)  (was: -> Split into Chunks -> Supabase)
    conns["Extract from PDF"] = {"main": [[{"node": "Merge", "type": "main", "index": 0}]]}
    conns["Source Text"] = {"main": [[{"node": "Supabase Vector Store", "type": "main", "index": 0}]]}

    # Add the recursive splitter node (idempotent) near the Data Loader.
    if find(nodes, type_suffix="textSplitterRecursiveCharacterTextSplitter") is None:
        x, y = find(nodes, name="Data Loader")["position"]
        nodes.append({
            "parameters": {"chunkSize": 1000, "chunkOverlap": 150, "options": {}},
            "type": "@n8n/n8n-nodes-langchain.textSplitterRecursiveCharacterTextSplitter",
            "typeVersion": 1,
            "position": [x, y + 220],
            "id": str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-splitter")),
            "name": "Recursive Character Text Splitter",
        })
    # Wire splitter -> Data Loader (ai_textSplitter) and enable custom splitting.
    conns.setdefault("Recursive Character Text Splitter", {})["ai_textSplitter"] = [
        [{"node": "Data Loader", "type": "ai_textSplitter", "index": 0}]
    ]
    find(nodes, name="Data Loader")["parameters"]["textSplittingMode"] = "custom"

    # Pin the INSERT-side embedding model to match the bot retrieve side exactly.
    find(nodes, name="Embeddings OpenAI")["parameters"]["model"] = EMBED_MODEL

    # Part C: per-bot document scoping. The tutorial tagged each file with a UNIQUE key
    # (fileName / fileName1 / ...), so a bot's 2nd+ file was invisible to retrieval. Replace
    # that with STABLE per-bot keys = the bot's own workflow ids. Each chunk carries both the
    # WhatsApp and Telegram workflow ids; each clone's Supabase retrieval filters by its own
    # platform id -> every file a bot uploads is retrievable, and other bots stay isolated.
    nodes = wf["nodes"]
    find(nodes, name="Add Whatsapp Filter")["parameters"]["assignments"]["assignments"][0]["value"] = (
        "={{ { metadataValues: [ { name: \"botWaId\", value: "
        "$('Extract Whatsapp Workflow Id').item.json.whatsappWorkflowId } ] } }}"
    )
    find(nodes, name="Add Telegram Filter")["parameters"]["assignments"]["assignments"][0]["value"] = (
        "={{ { metadataValues: [ { name: \"botTgId\", value: "
        "$('Extract Telegram Workflow Id').item.json.telegramWorkflowId } ] } }}"
    )
    find(nodes, name="Data Loader")["parameters"]["options"]["metadata"]["metadataValues"] = [
        {"name": "botWaId", "value": "={{ $('Extract Whatsapp Workflow Id').item.json.whatsappWorkflowId }}"},
        {"name": "botTgId", "value": "={{ $('Extract Telegram Workflow Id').item.json.telegramWorkflowId }}"},
        {"name": "contentType", "value": "={{ $('Extract Content Type').item.json.contentType }}"},
        {"name": "source", "value": "blob"},
    ]
    return wf


def main():
    for fname in BOT_IDS:
        wf = load(fname); fix_bot(wf); save(fname, wf); print(f"  fixed {fname}")
    wf = load(UPLOAD); fix_upload(wf); save(UPLOAD, wf); print(f"  fixed {UPLOAD}")
    print("done")


if __name__ == "__main__":
    main()
