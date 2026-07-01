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
    return wf  # implemented in Task 2


def fix_upload(wf):
    return wf  # implemented in Tasks 3 & 4


def main():
    for fname in BOT_IDS:
        wf = load(fname); fix_bot(wf); save(fname, wf); print(f"  fixed {fname}")
    wf = load(UPLOAD); fix_upload(wf); save(UPLOAD, wf); print(f"  fixed {UPLOAD}")
    print("done")


if __name__ == "__main__":
    main()
