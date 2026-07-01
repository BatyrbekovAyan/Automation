# RAG Restore + Fix (dev-promotable) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore a correct, professional RAG pipeline in the canonical n8n workflows (removing the broken Cohere reranker, fixing chunking + embedding parity + a Telegram webhook bug), and stop the dev derivation from stripping RAG — so dev runs real RAG with 3 credentials and the same files promote to prod.

**Architecture:** All edits land in the git-tracked canonical `Tools/n8n/workflows/*.json` plus `Tools/n8n/apply-dev-config.py`. Structural node/connection surgery on the two bot twins and Upload File is done by one idempotent, by-name Python migration script (`Tools/n8n/apply-rag-fixes.py`) that preserves `indent=2` formatting; two trivial text edits (Telegram handler, derive script) are done directly. Dev derives from the fixed canonical via `apply-dev-config.py` (now RAG-preserving), and prod is the same canonical re-imported to Cloud.

**Tech Stack:** n8n (LangChain nodes: `vectorStoreSupabase`, `embeddingsOpenAi`, `documentDefaultDataLoader`, `textSplitterRecursiveCharacterTextSplitter`, `memoryPostgresChat`), Python 3 (stdlib `json`, `uuid`, `sqlite3`), local n8n + `apply-dev-config.py` derive pipeline.

**Spec:** `docs/superpowers/specs/2026-06-30-rag-restore-and-fix-dev-design.md`

**Design invariants (do not violate):**
- Never address n8n nodes by array index (spec risk); always by `name` or `type` suffix.
- Apply every bot edit to BOTH twins identically: `4wYitz5ek30SVNlT-WhatsApp_Bot.json` and `4VN3gsFaC2HUYmcc-Telegram_Bot.json`.
- Do not change the clone-source workflow `id` or `active` fields, or rename the workflows (CLAUDE.md: `4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc` must stay inactive and unrenamed).
- Preserve `indent=2, ensure_ascii=False` JSON formatting so diffs stay reviewable.

---

## Task 0: Confirm version-sensitive node param shapes

Three param shapes depend on the exact installed node version and cannot be assumed. Resolve them before writing the transform so later tasks use the real field names. `n8n-mcp` may be disconnected — reconnect it (restart the MCP / Claude Code) if needed, or fall back to importing a probe into local n8n.

**Files:** none (research only — record findings inline in `apply-rag-fixes.py` comments in Task 1).

- [ ] **Step 1: Get the three node schemas**

If `n8n-mcp` tools are available, load and call `get_node_types` for:
- `@n8n/n8n-nodes-langchain.embeddingsOpenAi` (v1.2) — find the parameter that pins the model.
- `@n8n/n8n-nodes-langchain.documentDefaultDataLoader` (v1.1) — find the parameter that switches text splitting to a custom (connected) splitter.
- `@n8n/n8n-nodes-langchain.textSplitterRecursiveCharacterTextSplitter` (v1) — confirm `chunkSize` / `chunkOverlap` shape.

Fallback if `n8n-mcp` is unavailable: in the local n8n UI add each node, set the fields, export the workflow, and read the resulting JSON.

- [ ] **Step 2: Record the confirmed shapes**

Fill these three answers (defaults shown are best-guess; correct them from Step 1):

1. **Embeddings model field.** Default assumption: plain string `"model": "text-embedding-3-small"`.
   If it is a resourceLocator, use instead:
   `"model": { "__rl": true, "value": "text-embedding-3-small", "mode": "list", "cachedResultName": "text-embedding-3-small" }`.
2. **Data-loader custom-splitting param.** Default assumption: top-level `"textSplittingMode": "custom"`.
   (If the installed version exposes the `ai_textSplitter` input without a mode flag — as reference workflow `3No1NVpbyJeag3bu` does at v1 — the connection alone suffices and this key is harmless/ignored.)
3. **`profile_id` on the inbound body.** Confirm `messages[0].profile_id` exists for BOTH WhatsApp and Telegram inbound payloads (inspect a real payload at `persistentDataPath/response.txt`, or an n8n execution's Webhook output). If absent on either, pick another stable per-bot field for the `sessionKey` namespace and note it here.

- [ ] **Step 3: No commit** (research task; findings are used in Task 1).

---

## Task 1: Create the migration script skeleton + verify harness

**Files:**
- Create: `Tools/n8n/apply-rag-fixes.py`

- [ ] **Step 1: Write the skeleton with shared helpers**

```python
#!/usr/bin/env python3
"""One-time, idempotent migration of the CANONICAL n8n workflows for the RAG
restore + fixes (see docs/superpowers/specs/2026-06-30-rag-restore-and-fix-dev-design.md).

Edits Tools/n8n/workflows/*.json IN PLACE, by node name/type (never index),
preserving indent=2 formatting. Re-runnable: running twice is a no-op.

Task 0 confirmed shapes (edit if your node versions differ):
  - embeddings model field: <FILL FROM TASK 0>
  - data-loader custom-splitting param: <FILL FROM TASK 0>
  - sessionKey namespace field: <FILL FROM TASK 0>
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
        json.dump(wf, f, indent=2, ensure_ascii=False)


def find(nodes, name=None, type_suffix=None):
    for n in nodes:
        if name is not None and n["name"] == name:
            return n
        if type_suffix is not None and n["type"].endswith(type_suffix):
            return n
    return None


def delete_nodes(wf, doomed):
    """Remove nodes by name and prune every dangling connection (as source key
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
```

- [ ] **Step 2: Run it — confirm it round-trips with a clean diff**

Run: `python3 Tools/n8n/apply-rag-fixes.py && git diff --stat Tools/n8n/workflows/`
Expected: prints `fixed ...` lines; `git diff --stat` shows **either no changes or only whitespace-localized changes** (transforms are still no-ops). If a file shows a full-file reformat, STOP and investigate formatting (the canonical files are `indent=2`; a big diff means the round-trip normalized something — acceptable only if functionally identical, but confirm before proceeding).

- [ ] **Step 3: Revert any no-op reformat and commit the script only**

Run: `git checkout Tools/n8n/workflows/ && git add Tools/n8n/apply-rag-fixes.py`
Run: `git commit -m "tooling(n8n): scaffold apply-rag-fixes canonical migration script"`

---

## Task 2: fix_bot() — remove reranker, fix retrieval/memory params (both twins)

**Files:**
- Modify: `Tools/n8n/apply-rag-fixes.py` (`fix_bot`)
- Modify (by running the script): `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`

- [ ] **Step 1: Implement `fix_bot`**

Replace the `fix_bot` stub with:

```python
def fix_bot(wf):
    nodes = wf["nodes"]

    # 1. Delete the unconfigured Cohere reranker + its ai_reranker connection.
    delete_nodes(wf, {"Reranker Cohere"})

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
    emb = find(nodes, name="OpenAI Embedding")
    emb["parameters"]["model"] = EMBED_MODEL   # Task 0: swap to resourceLocator form if required

    # 4. Namespaced, correct sessionKey (kills the dead `||` and the cross-bot leak).
    mem = find(nodes, name="Chat Memory")
    mem["parameters"]["sessionKey"] = (
        "={{ $('Webhook').item.json.body.messages[0].profile_id + ':' + "
        "$('Webhook').item.json.body.messages[0].from }}"
    )   # Task 0: swap profile_id for the confirmed per-bot field if it differs
    return wf
```

- [ ] **Step 2: Write the verification and run it against current (unfixed) state**

Save as `Tools/n8n/verify_rag.py`:

```python
#!/usr/bin/env python3
import json, os, sys
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
def load(f):
    with open(os.path.join(WF, f), encoding="utf-8") as fh: return json.load(fh)
def find(ns, name=None, ts=None):
    for n in ns:
        if name and n["name"]==name: return n
        if ts and n["type"].endswith(ts): return n
BOTS = ["4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json"]
def check_bot(f):
    wf = load(f); ns = wf["nodes"]
    assert find(ns, ts="rerankerCohere") is None, f"{f}: Cohere reranker still present"
    assert "Reranker Cohere" not in wf["connections"], f"{f}: Cohere connection not pruned"
    sup = find(ns, name="Supabase Vector Store")
    assert sup["parameters"]["useReranker"] is False, f"{f}: useReranker not False"
    assert sup["parameters"]["topK"] == 10, f"{f}: topK not 10"
    assert "product" in sup["parameters"]["toolDescription"].lower(), f"{f}: toolDescription not sharpened"
    emb = find(ns, name="OpenAI Embedding")
    assert "3-small" in json.dumps(emb["parameters"].get("model","")), f"{f}: retrieve embed model not pinned"
    mem = find(ns, name="Chat Memory")
    assert "||" not in mem["parameters"]["sessionKey"], f"{f}: dead || still in sessionKey"
    assert "+ ':' +" in mem["parameters"]["sessionKey"], f"{f}: sessionKey not namespaced"
if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    if which in ("bot","all"):
        for f in BOTS: check_bot(f)
    print("VERIFY OK:", which)
```

Run: `python3 Tools/n8n/verify_rag.py bot`
Expected: FAIL — `AssertionError: ...Cohere reranker still present`.

- [ ] **Step 3: Apply the transform**

Run: `python3 Tools/n8n/apply-rag-fixes.py`
Expected: prints `fixed 4wYitz5ek30SVNlT-WhatsApp_Bot.json` and `fixed 4VN3gsFaC2HUYmcc-Telegram_Bot.json`.

- [ ] **Step 4: Verify it passes + eyeball the diff**

Run: `python3 Tools/n8n/verify_rag.py bot`
Expected: `VERIFY OK: bot`.
Run: `git diff --stat Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`
Expected: a localized diff (deleted Cohere node/connection + a handful of changed param lines), NOT a full-file reformat.

- [ ] **Step 5: Commit**

Run: `git add Tools/n8n/apply-rag-fixes.py Tools/n8n/verify_rag.py Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`
Run: `git commit -m "fix(n8n): bot templates — drop Cohere reranker, topK 10, pin embed model, fix sessionKey"`

---

## Task 3: fix_upload() part A — purge the dead nodes

**Files:**
- Modify: `Tools/n8n/apply-rag-fixes.py` (`fix_upload`)
- Modify (by running the script): `Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json`

- [ ] **Step 1: Implement the purge in `fix_upload`**

Replace the `fix_upload` stub with:

```python
def fix_upload(wf):
    # Part A: delete verified-dead orphans + the disabled AI-cleaner chain.
    delete_nodes(wf, {
        "Supabase Vector Store1", "Embeddings OpenAI1", "Data Loader1",  # orphan trio
        "Prepare AI Prompt", "AI Cleaner", "Extract Clean Text",          # disabled chain
    })
    # Part B added in Task 4.
    return wf
```

- [ ] **Step 2: Extend the verifier and run against current state**

Add to `Tools/n8n/verify_rag.py` (before the `__main__` block):

```python
UPLOAD = "KoTuIlk4LMrlvnWI-Upload_File.json"
DEAD = {"Supabase Vector Store1","Embeddings OpenAI1","Data Loader1",
        "Prepare AI Prompt","AI Cleaner","Extract Clean Text"}
def check_upload_purge():
    wf = load(UPLOAD); names = {n["name"] for n in wf["nodes"]}
    leftover = DEAD & names
    assert not leftover, f"{UPLOAD}: dead nodes still present: {leftover}"
    for d in DEAD:
        assert d not in wf["connections"], f"{UPLOAD}: connection for {d} not pruned"
```

And extend the `__main__` dispatch:

```python
    if which in ("purge","all"):
        check_upload_purge()
```

Run: `python3 Tools/n8n/verify_rag.py purge`
Expected: FAIL — `AssertionError: ...dead nodes still present`.

- [ ] **Step 3: Apply + verify**

Run: `python3 Tools/n8n/apply-rag-fixes.py`
Run: `python3 Tools/n8n/verify_rag.py purge`
Expected: `VERIFY OK: purge`.

- [ ] **Step 4: Commit**

Run: `git add Tools/n8n/apply-rag-fixes.py Tools/n8n/verify_rag.py Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json`
Run: `git commit -m "fix(n8n): Upload File — delete dead orphan trio + disabled cleaner chain"`

---

## Task 4: fix_upload() part B — real chunking + insert-embed parity

Replaces the `product[N]:`-marker hack with a native recursive splitter, so plain `.txt`/prose `.pdf` chunk properly. Target ingestion flow:
`… Merge → Clean Text → Source Text (one item, content=full text) → Supabase Vector Store (insert)`, with `Recursive Character Text Splitter --ai_textSplitter--> Data Loader --ai_document--> Supabase Vector Store`.

**Files:**
- Modify: `Tools/n8n/apply-rag-fixes.py` (`fix_upload`)
- Modify (by running the script): `Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json`

- [ ] **Step 1: Add Part B to `fix_upload`**

Insert this block into `fix_upload`, right before `return wf`:

```python
    # Part B: replace the product-marker chunker with a native recursive splitter.
    nodes = wf["nodes"]; conns = wf["connections"]

    # Remove the marker hack. PDF becomes prose (no product[n]: prefixing).
    delete_nodes(wf, {"Normalize PDF", "Split into Chunks"})

    # Rewire the two edges the deletions broke:
    #   Extract from PDF -> Merge(0)         (was: -> Normalize PDF -> Merge)
    #   Source Text      -> Supabase(0)      (was: -> Split into Chunks -> Supabase)
    conns["Extract from PDF"] = {"main": [[{"node": "Merge", "type": "main", "index": 0}]]}
    conns["Source Text"] = {"main": [[{"node": "Supabase Vector Store", "type": "main", "index": 0}]]}

    # Add the recursive splitter node (idempotent) near the Data Loader.
    if find(nodes, type_suffix="textSplitterRecursiveCharacterTextSplitter") is None:
        loader = find(nodes, name="Data Loader")
        x, y = loader["position"]
        nodes.append({
            "parameters": {"chunkSize": 1000, "chunkOverlap": 150, "options": {}},
            "type": "@n8n/n8n-nodes-langchain.textSplitterRecursiveCharacterTextSplitter",
            "typeVersion": 1,
            "position": [x, y + 220],
            "id": str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-splitter")),
            "name": "Recursive Character Text Splitter",
        })
    # Wire splitter -> Data Loader (ai_textSplitter).
    conns.setdefault("Recursive Character Text Splitter", {})["ai_textSplitter"] = [
        [{"node": "Data Loader", "type": "ai_textSplitter", "index": 0}]
    ]
    # Enable custom text splitting on the loader (Task 0: key/value may differ by version).
    find(nodes, name="Data Loader")["parameters"]["textSplittingMode"] = "custom"

    # Pin the INSERT-side embedding model to match the bot retrieve side exactly.
    find(nodes, name="Embeddings OpenAI")["parameters"]["model"] = EMBED_MODEL
```

- [ ] **Step 2: Extend the verifier and run against current state**

Add to `Tools/n8n/verify_rag.py` (before `__main__`):

```python
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
    assert "3-small" in json.dumps(ins["parameters"].get("model","")), "insert embed model not pinned"
```

Extend the `__main__` dispatch:

```python
    if which in ("chunker","all"):
        check_upload_chunker()
```

Run: `python3 Tools/n8n/verify_rag.py chunker`
Expected: FAIL — `AssertionError: marker-hack nodes remain`.

- [ ] **Step 3: Apply + verify + eyeball**

Run: `python3 Tools/n8n/apply-rag-fixes.py`
Run: `python3 Tools/n8n/verify_rag.py all`
Expected: `VERIFY OK: all`.
Run: `python3 -c "import json; wf=json.load(open('Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json')); print(sorted(n['name'] for n in wf['nodes']))"`
Expected: no `Normalize PDF`, `Split into Chunks`, `*1`, `Prepare AI Prompt`, `AI Cleaner`, `Extract Clean Text`; includes `Recursive Character Text Splitter`.

- [ ] **Step 4: Commit**

Run: `git add Tools/n8n/apply-rag-fixes.py Tools/n8n/verify_rag.py Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json`
Run: `git commit -m "fix(n8n): Upload File — native recursive-splitter chunking + pin insert embed model"`

---

## Task 5: Fix the Telegram webhook-path copy-paste bug

The cloned Telegram bot's webhook `path` is built from `body.WhatsappProfileId`; it must use `body.TelegramProfileId` (the value appears exactly once).

**Files:**
- Modify: `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`

- [ ] **Step 1: Confirm the single occurrence**

Run: `grep -c "body.WhatsappProfileId" Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
Expected: `1`.

- [ ] **Step 2: Replace it (Edit tool)**

In `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`, change the one occurrence:
`={{ $('Unity Webhook').item.json.body.WhatsappProfileId }}`
→
`={{ $('Unity Webhook').item.json.body.TelegramProfileId }}`

- [ ] **Step 3: Verify**

Run: `grep -c "body.WhatsappProfileId" Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
Expected: `0`.
Run: `grep -c "body.TelegramProfileId" Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
Expected: `4`.

- [ ] **Step 4: Commit**

Run: `git add Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
Run: `git commit -m "fix(n8n): Telegram Create handler — webhook path from TelegramProfileId (was WhatsappProfileId)"`

---

## Task 6: Stop stripping RAG in the dev derivation

Remove `simplify_runtime` from `apply-dev-config.py` so the dev derivation keeps the (now-fixed) RAG stack and only rewrites hosts + remaps credentials by name.

**Files:**
- Modify: `Tools/n8n/apply-dev-config.py`

- [ ] **Step 1: Delete the RAG-strip code**

In `Tools/n8n/apply-dev-config.py`:
- Delete the entire `simplify_runtime(wf)` function (lines ~60-112).
- Delete the constants `CLONE_SOURCE_IDS`, `RAG_NODE_SUFFIXES`, `WINDOW_MEMORY_TYPE` (lines ~27-31) and their explanatory comment.
- In `main()`, delete the two lines:
  ```python
          if wf.get("id") in CLONE_SOURCE_IDS:
              simplify_runtime(wf)
  ```
- Remove the now-unused `uuid` import from the top `import` line (keep `json, os, sqlite3, sys`).

- [ ] **Step 2: Update the module docstring**

Replace the docstring's first paragraph so it reflects that dev now runs full RAG:

```python
"""Derive local-dev n8n workflows from the canonical (Cloud-shaped) set.

Reads Tools/n8n/workflows/*.json, rewrites the n8n-API host to localhost,
fixes the trailing-space /activate typo, remaps credential ids to the local
instance's credentials (matched by credential NAME from ~/.n8n/database.sqlite),
and writes the result to Tools/n8n/workflows-local/ (gitignored).

Dev now runs the FULL RAG stack (no stripping); create these local credentials
(matched by name): 'OpenAi account', 'Supabase', 'Postgres'.

Usage: python3 Tools/n8n/apply-dev-config.py
Then:  n8n import:workflow --separate --input=Tools/n8n/workflows-local/
"""
```

- [ ] **Step 3: Verify it imports and no longer strips**

Run: `python3 -c "import ast; ast.parse(open('Tools/n8n/apply-dev-config.py').read()); print('parse OK')"`
Expected: `parse OK`.
Run: `grep -c "simplify_runtime\|CLONE_SOURCE_IDS\|WINDOW_MEMORY_TYPE" Tools/n8n/apply-dev-config.py`
Expected: `0`.

- [ ] **Step 4: Commit**

Run: `git add Tools/n8n/apply-dev-config.py`
Run: `git commit -m "tooling(n8n): stop stripping RAG in dev derivation (simplify_runtime removed)"`

---

## Task 7: Integration validation (dev end-to-end gate)

Confirms the edited canonical validates, derives cleanly to dev with RAG intact, and actually answers from an uploaded document. Requires the user's local n8n + credentials + device.

**Files:** none (validation only).

- [ ] **Step 1: Structural validation of the 4 edited canonical files**

If `n8n-mcp` is available, run `validate_workflow` on each of the two bot twins, Upload File, and the Telegram handler; expect no errors (in particular, confirm the Data Loader accepts the connected splitter and the embeddings `model` is valid). If unavailable, import the four files into local n8n and confirm none of the changed nodes show a red parameter error.

- [ ] **Step 2: Create the 3 local credentials**

In local n8n, create credentials named EXACTLY: `OpenAi account` (OpenAI), `Supabase` (Supabase API), `Postgres` (Postgres). Confirm the Supabase `documents` table `embedding` column is `vector(1536)` (text-embedding-3-small is 1536-dim).

- [ ] **Step 3: Derive dev workflows and confirm RAG is intact (not stripped)**

Run: `N8N_PUBLIC_URL=<your-tunnel-https-url> python3 Tools/n8n/apply-dev-config.py`
Expected: no `⚠️ credentials referenced but NOT found locally` for OpenAI/Supabase/Postgres; no `CohereApi account` referenced.
Run: `python3 -c "import json; wf=json.load(open('Tools/n8n/workflows-local/4wYitz5ek30SVNlT-WhatsApp_Bot.json')); ts=[n['type'] for n in wf['nodes']]; print('vectorStore' , any('vectorStoreSupabase' in t for t in ts)); print('windowMemory', any('memoryBufferWindow' in t for t in ts))"`
Expected: `vectorStore True` and `windowMemory False` (RAG preserved; no dev-only window-buffer injection).

- [ ] **Step 4: Import to local n8n**

Run: `n8n import:workflow --separate --input=Tools/n8n/workflows-local/`
Then restart local n8n.

- [ ] **Step 5: End-to-end RAG test (definition of done)**

Delete + recreate a bot on-device so its per-bot runtime is cloned from the fixed template. Upload one `.txt` and one prose `.pdf` via the app. Send the bot a question answerable only from the uploaded doc.
Expected: the local n8n runtime execution is `success`; the AI Agent invokes the Supabase tool; retrieved chunks are relevant (multiple chunks per doc, not one giant blob); a correct reply is delivered via Wappi. Repeat for a Telegram bot to confirm the webhook-path fix (inbound Telegram no longer 404s).

- [ ] **Step 6: No commit** (validation only). Record results; on failure, open a debugging pass rather than forcing edits.

---

## Deferred (NOT in this plan — the later "professional" pass)

Tracked from the audit; intentionally out of scope here: HTTP retry/timeout + a shared Error Trigger workflow; prompt-injection hardening of the Create/Edit handler system-prompt interpolation; `lmChat maxTokens 200→~500` and `contextWindowLength 50→~15`; resolve `nodes[21]`/`nodes[5]` by name/type; inbound-message dedupe on `messages[0].id`; unused `n8nApi`+`httpBearerAuth` credential bindings; `Return File Id` `{{$json.name}}` empty-body bug; `Switch` default output; system-prompt typos; optional properly-configured Cohere reranker (model + `topN` 3-5).

## Self-review notes

- **Spec coverage:** bot reranker/topK/embed/sessionKey/toolDescription → Task 2; dead nodes → Task 3; native chunking + insert-embed parity → Task 4; Telegram path bug → Task 5; `simplify_runtime` removal → Task 6; validation/derive/e2e + 3 creds → Task 7; deferred backlog → Deferred section. All spec sections covered.
- **Version-sensitivity** (embeddings `model` field, loader custom-split param, `profile_id`) is isolated in Task 0 and gated by Task 7 Step 1, not guessed silently.
- **Type consistency:** `find`, `delete_nodes`, `load`, `save`, `EMBED_MODEL` defined once in Task 1 and reused verbatim in Tasks 2–4; `verify_rag.py` dispatch keys (`bot`/`purge`/`chunker`/`all`) are consistent across Tasks 2–4.
