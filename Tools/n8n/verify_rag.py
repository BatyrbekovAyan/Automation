#!/usr/bin/env python3
"""Assertion harness for the RAG restore + fixes migration (apply-rag-fixes.py).

Usage: python3 Tools/n8n/verify_rag.py [bot|purge|chunker|all]
"""
import json, os, sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
BOTS = ["4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json"]
UPLOAD = "KoTuIlk4LMrlvnWI-Upload_File.json"
DEAD = {"Supabase Vector Store1", "Embeddings OpenAI1", "Data Loader1",
        "Prepare AI Prompt", "AI Cleaner", "Extract Clean Text"}


def load(f):
    with open(os.path.join(WF, f), encoding="utf-8") as fh:
        return json.load(fh)


def find(ns, name=None, ts=None):
    for n in ns:
        if name and n["name"] == name:
            return n
        if ts and n["type"].endswith(ts):
            return n
    return None


RETRIEVE_FILTER_KEY = {
    "4wYitz5ek30SVNlT-WhatsApp_Bot.json": "botWaId",
    "4VN3gsFaC2HUYmcc-Telegram_Bot.json": "botTgId",
}


def check_bot(f):
    wf = load(f); ns = wf["nodes"]
    assert find(ns, ts="rerankerCohere") is None, f"{f}: Cohere reranker still present"
    assert "Reranker Cohere" not in wf["connections"], f"{f}: Cohere connection not pruned"
    sup = find(ns, name="Supabase Vector Store")
    assert sup["parameters"]["useReranker"] is False, f"{f}: useReranker not False"
    assert sup["parameters"]["topK"] == 10, f"{f}: topK not 10"
    assert "product" in sup["parameters"]["toolDescription"].lower(), f"{f}: toolDescription not sharpened"
    mv = sup["parameters"].get("options", {}).get("metadata", {}).get("metadataValues", [])
    assert len(mv) == 1 and mv[0]["name"] == RETRIEVE_FILTER_KEY[f], f"{f}: retrieve filter key wrong: {mv}"
    assert "$workflow.id" in mv[0]["value"], f"{f}: retrieve filter not self-scoped to $workflow.id"
    emb = find(ns, name="OpenAI Embedding")
    assert "3-small" in json.dumps(emb["parameters"].get("model", "")), f"{f}: retrieve embed model not pinned"
    mem = find(ns, name="Chat Memory")
    assert "||" not in mem["parameters"]["sessionKey"], f"{f}: dead || still in sessionKey"
    assert "+ ':' +" in mem["parameters"]["sessionKey"], f"{f}: sessionKey not namespaced"


def check_upload_purge():
    wf = load(UPLOAD); names = {n["name"] for n in wf["nodes"]}
    leftover = DEAD & names
    assert not leftover, f"{UPLOAD}: dead nodes still present: {leftover}"
    for d in DEAD:
        assert d not in wf["connections"], f"{UPLOAD}: connection for {d} not pruned"


def check_upload_chunker():
    wf = load(UPLOAD); ns = wf["nodes"]; conns = wf["connections"]
    names = {n["name"] for n in ns}
    assert "Normalize PDF" not in names and "Split into Chunks" not in names, "marker-hack nodes remain"
    sp = find(ns, ts="textSplitterRecursiveCharacterTextSplitter")
    assert sp is not None, "recursive splitter not added"
    assert sp["parameters"]["chunkSize"] == 1000, "chunkSize not 1000"
    assert conns["Recursive Character Text Splitter"]["ai_textSplitter"][0][0]["node"] == "Data Loader", "splitter not wired to loader"
    assert conns["Extract from PDF"]["main"][0][0]["node"] == "Merge", "PDF not rewired to Merge"
    assert conns["Source Text"]["main"][0][0]["node"] == "Supabase Vector Store", "Source Text not rewired to Supabase"
    ins = find(ns, name="Embeddings OpenAI")
    assert "3-small" in json.dumps(ins["parameters"].get("model", "")), "insert embed model not pinned"


def check_upload_scoping():
    wf = load(UPLOAD); ns = wf["nodes"]
    dl = find(ns, name="Data Loader")
    keys = [m["name"] for m in dl["parameters"]["options"]["metadata"]["metadataValues"]]
    assert "botWaId" in keys and "botTgId" in keys, f"Data Loader not tagging per-bot keys: {keys}"
    assert "fileId" in keys, f"Data Loader not stamping fileId (breaks per-file delete): {keys}"
    assert not any(str(k).startswith("fileName") for k in keys), f"stale fileName key still tagged: {keys}"
    aw = find(ns, name="Add Whatsapp Filter")["parameters"]["assignments"]["assignments"][0]["value"]
    at = find(ns, name="Add Telegram Filter")["parameters"]["assignments"]["assignments"][0]["value"]
    assert "botWaId" in aw and "fileName" not in aw, "Add Whatsapp Filter not using stable botWaId"
    assert "botTgId" in at and "fileName" not in at, "Add Telegram Filter not using stable botTgId"


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    if which in ("scoping", "all"):
        check_upload_scoping()
    if which in ("bot", "all"):
        for f in BOTS:
            check_bot(f)
    if which in ("purge", "all"):
        check_upload_purge()
    if which in ("chunker", "all"):
        check_upload_chunker()
    print("VERIFY OK:", which)
