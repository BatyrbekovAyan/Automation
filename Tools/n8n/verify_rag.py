#!/usr/bin/env python3
"""Assertion harness for the RAG restore + fixes migration (apply-rag-fixes.py),
the upload/delete hardening migration (apply-upload-hardening.py), and the
image OCR branch migration (apply-image-ocr.py).

Usage: python3 Tools/n8n/verify_rag.py [bot|purge|chunker|hardening|image|all]
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


PUT_CHAIN = {
    "If Whatsapp Id Exists", "Get Whatsapp Workflow", "Add Whatsapp Filter", "Update Whatsapp Workflow",
    "If Telegram Id Exists", "Get Telegram Workflow", "Add Telegram Filter", "Update Telegram Workflow",
}


def check_upload_scoping():
    wf = load(UPLOAD); ns = wf["nodes"]
    dl = find(ns, name="Data Loader")
    keys = [m["name"] for m in dl["parameters"]["options"]["metadata"]["metadataValues"]]
    assert "botWaId" in keys and "botTgId" in keys, f"Data Loader not tagging per-bot keys: {keys}"
    assert "fileId" in keys, f"Data Loader not stamping fileId (breaks per-file delete): {keys}"
    assert not any(str(k).startswith("fileName") for k in keys), f"stale fileName key still tagged: {keys}"


def check_upload_no_put_chain():
    # The upload-time clone-PATCH chain must stay gone: it addressed the bot
    # workflow by array index AND its API update dropped the clone's active
    # flag — every upload silently deactivated the bot. Scoping now lives in
    # the templates' baked $workflow.id filter (check_bot).
    wf = load(UPLOAD); names = {n["name"] for n in wf["nodes"]}
    leftover = PUT_CHAIN & names
    assert not leftover, f"{UPLOAD}: PUT chain nodes still present: {leftover}"
    for d in PUT_CHAIN:
        assert d not in wf["connections"], f"{UPLOAD}: connection for {d} not pruned"
    ext_tg = wf["connections"]["Extract Telegram Workflow Id"]["main"][0]
    targets = {(l["node"], l.get("index", 0)) for l in ext_tg}
    assert ("Switch", 0) in targets and ("Merge", 1) in targets, \
        f"Extract Telegram Workflow Id must feed Switch and Merge(1): {targets}"


def check_upload_response():
    wf = load(UPLOAD); ns = wf["nodes"]; conns = wf["connections"]
    # Unsupported types must not vanish silently: Switch needs a fallback output
    # wired to an explicit 415 response.
    sw = find(ns, name="Switch")
    assert sw["parameters"]["options"].get("fallbackOutput") == "extra", "Switch has no fallback output"
    branches = conns["Switch"]["main"]
    assert branches[-1][0]["node"] == "Respond Unsupported Type", \
        f"Switch fallback not wired to Respond Unsupported Type: {branches}"
    ru = find(ns, name="Respond Unsupported Type")
    assert ru["parameters"]["options"].get("responseCode") == 415, "unsupported-type response not 415"
    # Success response must be real JSON carrying the fileId (was: empty $json.name text).
    ok = find(ns, name="Return File Id")
    assert ok["parameters"]["respondWith"] == "json", "success response not JSON"
    assert "fileId" in ok["parameters"]["responseBody"], "success response missing fileId"


DELETE = "ZTqpumOpL1rNDOp6-Delete_File.json"


def check_upload_hardening():
    wf = load(UPLOAD); ns = wf["nodes"]; conns = wf["connections"]
    # 1. Extension routing must be case-insensitive: the app forwards the
    #    ORIGINAL filename, so "MENU.PDF" must not fall into the 415 fallback.
    for rule in find(ns, name="Switch")["parameters"]["rules"]["values"]:
        for cond in rule["conditions"]["conditions"]:
            assert ".toLowerCase()" in cond["leftValue"], \
                f"Switch rule '{rule.get('outputKey')}' extension match is case-sensitive"
    # 2. Original file archived to Supabase Storage keyed by fileId.
    store = find(ns, name="Store Original File")
    assert store is not None, "Store Original File node missing"
    assert store["parameters"]["url"].startswith("=https://") \
        and "/storage/v1/object/price-lists/" in store["parameters"]["url"], \
        f"Store Original File url wrong: {store['parameters']['url']}"
    assert "$('Extract File Id').item.json.fileId" in store["parameters"]["url"], \
        "storage object key must be the app-minted fileId"
    assert store["parameters"]["contentType"] == "binaryData" \
        and store["parameters"]["inputDataFieldName"] == "data", "store node must send the binary as-is"
    assert store["parameters"]["nodeCredentialType"] == "supabaseApi", "store node must auth via supabaseApi"
    # A storage hiccup must never fail the upload: dead-end + continue-on-error.
    assert store.get("onError") == "continueRegularOutput", "store node must not fail the upload"
    assert "Store Original File" not in conns, "store node must be a dead-end (no outgoing edges)"
    branches = conns["Switch"]["main"]
    for i, key in ((0, "txt"), (1, "pdf")):
        assert any(l["node"] == "Store Original File" for l in branches[i]), \
            f"Switch {key} branch does not archive the original"
    assert not any(l["node"] == "Store Original File" for l in branches[-1]), \
        "unsupported branch must NOT archive (nothing was ingested)"


def check_delete_hardening():
    wf = load_named(DELETE); ns = wf["nodes"]; conns = wf["connections"]
    node = find(ns, name="Delete Stored Original")
    assert node is not None, "Delete Stored Original node missing"
    assert node["parameters"]["method"] == "DELETE", "must DELETE the storage object"
    assert "/storage/v1/object/price-lists/" in node["parameters"]["url"] \
        and "$json.body.fileId" in node["parameters"]["url"], \
        f"Delete Stored Original url wrong: {node['parameters']['url']}"
    # Pre-bucket uploads have no stored object (404) — chunks must still delete.
    assert node.get("onError") == "continueRegularOutput", "storage 404 must not fail chunk deletion"
    hook = conns["Webhook"]["main"][0]
    targets = {l["node"] for l in hook}
    assert {"Delete File Chunks", "Delete Stored Original"} <= targets, \
        f"Webhook must fan out to chunks + storage delete: {targets}"
    assert conns["Delete File Chunks"]["main"][0][0]["node"] == "Respond", "chunk→respond chain broken"


def load_named(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as fh:
        return json.load(fh)


def check_image_ocr():
    wf = load(UPLOAD); ns = wf["nodes"]; conns = wf["connections"]
    sw = find(ns, name="Switch")
    rules = sw["parameters"]["rules"]["values"]
    image = [r for r in rules if r.get("outputKey") == "image"]
    assert len(image) == 1, "Switch has no image rule"
    exts = {c["rightValue"] for c in image[0]["conditions"]["conditions"]}
    assert exts == {"jpg", "jpeg", "png", "webp"}, f"image rule exts wrong: {exts}"
    assert image[0]["conditions"]["combinator"] == "or", "image rule must OR its extensions"
    for c in image[0]["conditions"]["conditions"]:
        assert ".toLowerCase()" in c["leftValue"], "image rule must be case-insensitive"

    vision = find(ns, name="Extract Price Text From Image")
    assert vision is not None, "vision node missing"
    assert "gpt-4o-mini" in json.dumps(vision["parameters"].get("modelId", "")), "vision model not pinned"
    assert "NO_PRICE_DATA" in vision["parameters"]["text"], "prompt missing the no-data marker"
    assert "contentType" in vision["parameters"]["text"], "prompt missing entity token"

    branches = conns["Switch"]["main"]
    assert len(branches) == 4, f"Switch must have 4 outputs, got {len(branches)}"
    img_targets = {l["node"] for l in branches[2]}
    assert img_targets == {"Extract Price Text From Image", "Store Original File"}, \
        f"image branch targets wrong: {img_targets}"
    assert branches[3][0]["node"] == "Respond Unsupported Type", "fallback lost its 415 wiring"

    gate = find(ns, name="Has Price Data")
    assert gate is not None, "Has Price Data gate missing"
    for cond in gate["parameters"]["conditions"]["conditions"]:
        assert cond["leftValue"] == "={{ $json[0].content[0].text }}", \
            f"Has Price Data leftValue must read the real vision output shape: {cond['leftValue']}"
    assert conns["Has Price Data"]["main"][0][0]["node"] == "Image Text", "gate true branch wrong"
    assert conns["Has Price Data"]["main"][1][0]["node"] == "Respond No Price Data", "gate false branch wrong"
    assert conns["Image Text"]["main"][0][0]["node"] == "Merge", "Image Text must feed Merge(0)"
    assert conns["Image Text"]["main"][0][0]["index"] == 0, "Image Text must feed Merge INPUT 0"

    text_node = find(ns, name="Image Text")
    text_assign = next(a for a in text_node["parameters"]["assignments"]["assignments"] if a["name"] == "text")
    assert text_assign["value"] == "={{ $json[0].content[0].text }}", \
        f"Image Text value must read the real vision output shape: {text_assign['value']}"

    r422 = find(ns, name="Respond No Price Data")
    assert r422["parameters"]["options"].get("responseCode") == 422, "no-data response not 422"
    assert "no_price_data" in r422["parameters"]["responseBody"], "no-data response missing error code"


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    if which in ("scoping", "all"):
        check_upload_scoping()
    if which in ("putchain", "all"):
        check_upload_no_put_chain()
    if which in ("response", "all"):
        check_upload_response()
    if which in ("bot", "all"):
        for f in BOTS:
            check_bot(f)
    if which in ("purge", "all"):
        check_upload_purge()
    if which in ("chunker", "all"):
        check_upload_chunker()
    if which in ("hardening", "all"):
        check_upload_hardening()
        check_delete_hardening()
    if which in ("image", "all"):
        check_image_ocr()
    print("VERIFY OK:", which)
