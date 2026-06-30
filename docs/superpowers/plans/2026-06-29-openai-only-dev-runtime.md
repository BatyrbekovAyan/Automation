# OpenAI-only Dev Bot Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Derive a simplified per-bot dev runtime that replies with OpenAI only (chat + Whisper) and in-memory window memory — no Supabase/Cohere/Postgres — so a bot created on-device against local n8n actually answers.

**Architecture:** Add a `simplify_runtime()` step to `Tools/n8n/apply-dev-config.py`, applied only to the 2 clone-source templates when deriving `Tools/n8n/workflows-local/`. It strips the 4 RAG nodes + their connections and injects a `memoryBufferWindow` node wired `ai_memory → AI Agent`, reusing the Postgres node's per-conversation `sessionKey`. The canonical `Tools/n8n/workflows/` is never modified.

**Tech Stack:** Python 3 (transform script), n8n 2.27.4 CLI, sqlite3, cloudflared tunnel (already running).

**Spec:** `docs/superpowers/specs/2026-06-29-openai-only-dev-runtime-design.md`

**Notes for the executor:**
- No worktrees (project convention); execute on `main`. Commits end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- The n8n MCP is currently disconnected — do NOT rely on it. The `memoryBufferWindow` schema below was taken from a real export (`Tools/n8n/reference/n2zIzL…AI_Powered_WhatsApp_Chatbot…json`): type `@n8n/n8n-nodes-langchain.memoryBufferWindow`, `typeVersion` 1.3.
- Stop n8n before any `n8n import:workflow` (CLI writes the SQLite DB). n8n + the cloudflared quick tunnel currently run detached; tunnel host is in `secrets.json` → `n8nBaseUrl`.
- Clone-source ids: `4wYitz5ek30SVNlT` (WhatsApp Bot), `4VN3gsFaC2HUYmcc` (Telegram Bot). Both have an `AI Agent` node and a `Chat Memory` (`memoryPostgresChat`) node feeding it via `ai_memory`.

---

## File Structure

- `Tools/n8n/apply-dev-config.py` — add `simplify_runtime()` + wire it into the derive loop (modify)
- `Tools/n8n/workflows-local/` — derived output (gitignored; regenerated, not committed)
- No app/C# changes.

---

## Task 1: Verify current derived clone sources still carry RAG (red)

Establishes the failing baseline before implementing the strip.

**Files:** none (read-only check)

- [ ] **Step 1: Re-derive then assert the invariant we WANT (expect FAIL)**

Run:
```bash
cd /Users/ayan/Projects/Automation
TUN=$(python3 -c "import json;print(json.load(open('Assets/StreamingAssets/secrets.json'))['n8nBaseUrl'])")
N8N_PUBLIC_URL="$TUN" python3 Tools/n8n/apply-dev-config.py >/dev/null
python3 - <<'PY'
import json
RAG=("memoryPostgresChat","vectorStoreSupabase","rerankerCohere","embeddingsOpenAi")
WIN="@n8n/n8n-nodes-langchain.memoryBufferWindow"
ok=True
for wid in ("4wYitz5ek30SVNlT","4VN3gsFaC2HUYmcc"):
    import glob
    f=glob.glob(f"Tools/n8n/workflows-local/{wid}-*.json")[0]
    wf=json.load(open(f))
    rag=[n["name"] for n in wf["nodes"] if any(n["type"].endswith(s) for s in RAG)]
    win=[n for n in wf["nodes"] if n["type"]==WIN]
    print(wid, "RAG nodes:", rag, "| window-memory:", len(win))
    if rag or len(win)!=1: ok=False
print("INVARIANT HOLDS:", ok)
PY
```
Expected: **INVARIANT HOLDS: False** — RAG nodes still present, no window-memory node. (This is the red state.)

---

## Task 2: Implement `simplify_runtime()` in apply-dev-config.py (green)

**Files:**
- Modify: `Tools/n8n/apply-dev-config.py`

- [ ] **Step 1: Add the import + constants near the top**

In `Tools/n8n/apply-dev-config.py`, add `import uuid` to the imports line (it currently has `import json, os, sqlite3, sys`), and add these constants after the existing `PUBLIC = ...` line:

```python
CLONE_SOURCE_IDS = {"4wYitz5ek30SVNlT", "4VN3gsFaC2HUYmcc"}
RAG_NODE_SUFFIXES = ("memoryPostgresChat", "vectorStoreSupabase", "rerankerCohere", "embeddingsOpenAi")
WINDOW_MEMORY_TYPE = "@n8n/n8n-nodes-langchain.memoryBufferWindow"
```

- [ ] **Step 2: Add the `simplify_runtime` function** (above `def main()`)

```python
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
```

- [ ] **Step 3: Call it in the derive loop, BEFORE credential remap**

In `main()`, the loop currently parses `wf = json.loads(text)` then runs `for node in wf.get("nodes", []): remap_credentials(...)`. Insert the simplify call between the parse and the remap so the credential pass only sees surviving nodes:

```python
        wf = json.loads(text)
        if wf.get("id") in CLONE_SOURCE_IDS:
            simplify_runtime(wf)
        for node in wf.get("nodes", []):
            remap_credentials(node, cred_by_name, missing)
```

(If the current code references `wf` by another local name, match it; the key point is `simplify_runtime(wf)` runs only for the 2 clone-source ids, before `remap_credentials`.)

- [ ] **Step 4: Re-derive and assert the invariant now HOLDS (green)**

Run:
```bash
cd /Users/ayan/Projects/Automation
TUN=$(python3 -c "import json;print(json.load(open('Assets/StreamingAssets/secrets.json'))['n8nBaseUrl'])")
N8N_PUBLIC_URL="$TUN" python3 Tools/n8n/apply-dev-config.py >/dev/null
python3 - <<'PY'
import json, glob
RAG=("memoryPostgresChat","vectorStoreSupabase","rerankerCohere","embeddingsOpenAi")
WIN="@n8n/n8n-nodes-langchain.memoryBufferWindow"
ok=True
for wid in ("4wYitz5ek30SVNlT","4VN3gsFaC2HUYmcc"):
    wf=json.load(open(glob.glob(f"Tools/n8n/workflows-local/{wid}-*.json")[0]))
    names={n["name"] for n in wf["nodes"]}
    rag=[n["name"] for n in wf["nodes"] if any(n["type"].endswith(s) for s in RAG)]
    win=[n for n in wf["nodes"] if n["type"]==WIN]
    agent=next((n["name"] for n in wf["nodes"] if n["type"].endswith(".agent")),None)
    # ai_memory edge from Window Memory -> AI Agent
    mem_edge = any(l.get("node")==agent
                   for b in wf.get("connections",{}).get("Window Memory",{}).get("ai_memory",[])
                   for l in b)
    # AI Agent still has a language model feeding it
    lm_edge = any("ai_languageModel" in outs and any(l.get("node")==agent for b in outs["ai_languageModel"] for l in b)
                  for outs in wf.get("connections",{}).values())
    print(wid,"RAG:",rag,"| window:",len(win),"| mem_edge:",mem_edge,"| lm_edge:",lm_edge)
    if rag or len(win)!=1 or not mem_edge or not lm_edge: ok=False
print("INVARIANT HOLDS:", ok)
PY
```
Expected: **INVARIANT HOLDS: True** — for each clone source: no RAG nodes, exactly one window-memory node, `ai_memory` edge into `AI Agent`, and the OpenAI `ai_languageModel` edge intact.

- [ ] **Step 5: Assert handlers + Upload File are NOT simplified**

Run:
```bash
cd /Users/ayan/Projects/Automation
python3 - <<'PY'
import json
# Upload File MUST keep its Supabase vector store (its core purpose)
wf=json.load(open("Tools/n8n/workflows-local/KoTuIlk4LMrlvnWI-Upload_File.json"))
has_supabase=any("vectorStoreSupabase" in n["type"] for n in wf["nodes"])
has_window=any(n["type"]=="@n8n/n8n-nodes-langchain.memoryBufferWindow" for n in wf["nodes"])
print("Upload File keeps Supabase:", has_supabase, "| no window injected:", not has_window)
assert has_supabase and not has_window, "Upload File was wrongly simplified"
print("OK — only clone sources were simplified")
PY
```
Expected: `Upload File keeps Supabase: True | no window injected: True` then `OK`.

- [ ] **Step 6: Commit**

```bash
cd /Users/ayan/Projects/Automation
git add Tools/n8n/apply-dev-config.py
git commit -m "tooling(n8n): simplify_runtime strips RAG for OpenAI-only dev bots

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Deploy the simplified clone sources to local n8n

**Depends on:** Task 2. Requires the local `OpenAi account` credential (Step 1, user action).

- [ ] **Step 1: 🧑 USER — create the OpenAI credential**

In the local n8n UI (`http://localhost:5678`) → Credentials → New → **OpenAI**, name it exactly **`OpenAi account`**, paste your OpenAI API key, Save. Verify:
```bash
sqlite3 ~/.n8n/database.sqlite "SELECT name,type FROM credentials_entity WHERE name='OpenAi account';"
```
Expected: one row `OpenAi account|openAiApi`.

- [ ] **Step 2: Re-derive with the credential present (remaps OpenAi id into the simplified templates)**

```bash
cd /Users/ayan/Projects/Automation
TUN=$(python3 -c "import json;print(json.load(open('Assets/StreamingAssets/secrets.json'))['n8nBaseUrl'])")
N8N_PUBLIC_URL="$TUN" python3 Tools/n8n/apply-dev-config.py
```
Expected: the "missing credentials" warning no longer lists `OpenAi account`, `CohereApi account`, `Postgres`, or `Supabase` for the clone sources (Cohere/Postgres/Supabase are gone; OpenAi now resolves). It may still list them for `Upload File` — that's fine.

- [ ] **Step 3: Stop n8n, import the 2 clone sources, restart**

```bash
PID=$(lsof -nP -iTCP:5678 -sTCP:LISTEN -t | head -1); [ -n "$PID" ] && kill "$PID"
for i in $(seq 1 20); do lsof -nP -iTCP:5678 -sTCP:LISTEN -t >/dev/null 2>&1 || break; sleep 1; done
cd /Users/ayan/Projects/Automation
n8n import:workflow --separate --input=Tools/n8n/workflows-local/
TUN=$(python3 -c "import json;print(json.load(open('Assets/StreamingAssets/secrets.json'))['n8nBaseUrl'])")
nohup env WEBHOOK_URL="$TUN" n8n start > /tmp/n8n.log 2>&1 & disown
for i in $(seq 1 60); do lsof -nP -iTCP:5678 -sTCP:LISTEN -t >/dev/null 2>&1 && break; sleep 1; done
```
Expected: `Successfully imported 7 workflows.` then n8n back up. The 2 clone sources stay inactive (active flag unchanged by import).

- [ ] **Step 4: Verify the imported clone sources in the DB are RAG-free with window memory**

```bash
sqlite3 ~/.n8n/database.sqlite "SELECT name FROM workflow_entity WHERE id IN ('4wYitz5ek30SVNlT','4VN3gsFaC2HUYmcc') AND (nodes LIKE '%vectorStoreSupabase%' OR nodes LIKE '%rerankerCohere%' OR nodes LIKE '%memoryPostgresChat%');" | sed 's/^/  STILL HAS RAG: /'
echo "(blank above = good)"
sqlite3 ~/.n8n/database.sqlite "SELECT COUNT(*) FROM workflow_entity WHERE id IN ('4wYitz5ek30SVNlT','4VN3gsFaC2HUYmcc') AND nodes LIKE '%memoryBufferWindow%';" | xargs echo "clone sources with window memory (want 2):"
```
Expected: no "STILL HAS RAG" lines; window-memory count = 2.

---

## Task 4: 🧑 End-to-end verification (definition of done)

- [ ] **Step 1: Delete bot 23 and create a fresh bot**

🧑 User: in the app, delete bot 23 (it was cloned from the old RAG template with Cloud cred ids), then create a new bot. The new per-bot workflow clones the simplified template with the local `OpenAi account` cred id.

- [ ] **Step 2: Confirm the new bot was created clean on local**

```bash
sqlite3 -header -column ~/.n8n/database.sqlite "SELECT id, name, active, datetime(createdAt) FROM workflow_entity WHERE id NOT IN ('XuvOp7TxOImOAmlj','Uz6HBBUpAiUqVysB','3qax5J9u2qsT9Vao','TwWPW3gIyjZS3foR','KoTuIlk4LMrlvnWI','4wYitz5ek30SVNlT','4VN3gsFaC2HUYmcc') ORDER BY createdAt DESC LIMIT 3;"
```
Expected: a new active per-bot workflow with a recent timestamp.

- [ ] **Step 3: Message the bot, then confirm the runtime SUCCEEDED**

🧑 User: send the bot a WhatsApp message. Then:
```bash
DB=~/.n8n/database.sqlite
NEWID=$(sqlite3 "$DB" "SELECT id FROM workflow_entity WHERE id NOT IN ('XuvOp7TxOImOAmlj','Uz6HBBUpAiUqVysB','3qax5J9u2qsT9Vao','TwWPW3gIyjZS3foR','KoTuIlk4LMrlvnWI','4wYitz5ek30SVNlT','4VN3gsFaC2HUYmcc') ORDER BY createdAt DESC LIMIT 1;")
sqlite3 -header -column "$DB" "SELECT id, status, datetime(startedAt) FROM execution_entity WHERE workflowId='$NEWID' ORDER BY startedAt DESC LIMIT 5;"
```
Expected: the most recent execution status = **`success`** (no `Credential ... does not exist` error), and the bot replied in WhatsApp.

**Definition of done met when Step 3 shows a `success` execution and a real reply arrives.**

---

## Self-Review

**Spec coverage:**
- Component 1 (`simplify_runtime` on the 2 clone sources: delete 4 RAG nodes + prune connections + inject window memory reusing Postgres sessionKey) → Task 2 ✅
- Scope guard (handlers + Upload File untouched) → Task 2 Step 5 ✅
- Component 2 (local `OpenAi account` credential) → Task 3 Step 1 ✅
- Component 3 (re-derive, import inactive clone sources, recreate bot 23) → Tasks 3 & 4 ✅
- DoD (recreated bot replies; runtime `success`) → Task 4 Step 3 ✅
- "Don't guess node schema" → schema taken from real export, stated in header/Task 2 Step 2 ✅
- Idempotency + AI Agent validity (ai_languageModel intact, single window node) → Task 2 Step 4 asserts ✅

**Placeholder scan:** No TBD/TODO; every code step has complete content. Manual steps are marked 🧑 with exact verify commands.

**Type/name consistency:** `simplify_runtime`, `CLONE_SOURCE_IDS`, `RAG_NODE_SUFFIXES`, `WINDOW_MEMORY_TYPE`, node name `"Window Memory"`, and connection key `ai_memory` are used consistently across Tasks 2–3. Clone-source ids match the spec and sub-project 1.
