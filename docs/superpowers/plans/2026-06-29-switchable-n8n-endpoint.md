# Switchable n8n Endpoint + Local Tunnel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the app's n8n base URL configurable (defaults to Cloud) and reachable from a physical device via a cloudflared tunnel, so the full bot lifecycle round-trips against local n8n.

**Architecture:** App reads `n8nBaseUrl` from `secrets.json` (empty → Cloud default), replacing ~12 hardcoded literals. Local n8n runs behind a named cloudflared tunnel; the 4 handler workflows' internal n8n-API calls are repointed to `localhost` via a committed transform script that also remaps credential ids into a gitignored `workflows-local/`.

**Tech Stack:** Unity 6 / C# (Unity test framework, EditMode), n8n 2.27.4 CLI, cloudflared, Python 3 (transform script), sqlite3.

**Spec:** `docs/superpowers/specs/2026-06-29-switchable-n8n-endpoint-design.md`

**Notes for the executor:**
- This project does **not** use worktrees (per project convention) — execute in the main working dir on `main`.
- Code commits end with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.
- EditMode tests live in `Assets/Tests/Editor/Chat/` (no asmdef; compile into `Assembly-CSharp-Editor`). Run with `Tools/run-tests-headless.sh <filter>` (Editor must be **closed**), or via the test bridge (`Temp/claude/run-tests.trigger`) if the Editor is open and focused.
- ⚠️ Unity new-file quirk: a brand-new test `.cs` can be silently excluded from compilation if written during a busy refresh ("type not found" / tests don't run). Fix = delete the `.cs` + `.meta`, let Unity register the deletion, recreate it.
- Tasks 5, 6, 8 are **🧑 USER ACTIONS** (infra/secrets/device) — the agent guides and verifies but cannot perform them.

---

## File Structure

- `Assets/Scripts/Main/Secrets.cs` — add `n8nBaseUrl` field (modify)
- `Assets/Scripts/Main/Manager.cs` — add `n8nBaseUrl` accessor + `ResolveN8nBaseUrl`; replace literals (modify)
- `Assets/Scripts/Main/BotSettings.Auth.cs` — replace the upload literal (modify)
- `Assets/StreamingAssets/secrets.json.example` — document the new field (modify)
- `Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs` — unit tests for URL resolution (create)
- `Tools/n8n/apply-dev-config.py` — derive dev-config workflows (create)
- `Tools/n8n/dev-tunnel.md` — local-run + tunnel runbook (create)
- `.gitignore` — ignore `Tools/n8n/workflows-local/` (modify)

---

## Task 1: App — configurable n8n base URL (TDD)

**Files:**
- Modify: `Assets/Scripts/Main/Secrets.cs:16`
- Modify: `Assets/Scripts/Main/Manager.cs:146`
- Test: `Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs`:

```csharp
using NUnit.Framework;

public class N8nBaseUrlTests
{
    private const string CloudDefault = "https://bagkz.app.n8n.cloud";

    [Test]
    public void EmptyConfig_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl(""));
    }

    [Test]
    public void NullConfig_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl(null));
    }

    [Test]
    public void WhitespaceConfig_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl("   "));
    }

    [Test]
    public void ConfiguredValue_IsUsedVerbatim()
    {
        Assert.AreEqual("https://abc.trycloudflare.com",
            Manager.ResolveN8nBaseUrl("https://abc.trycloudflare.com"));
    }

    [Test]
    public void TrailingSlash_IsTrimmed()
    {
        Assert.AreEqual("https://abc.trycloudflare.com",
            Manager.ResolveN8nBaseUrl("https://abc.trycloudflare.com/"));
    }
}
```

- [ ] **Step 2: Add the field + a stub so it compiles**

In `Assets/Scripts/Main/Secrets.cs`, add the field after `public string n8nAPIKey;` (line 16):

```csharp
    public string n8nAPIKey;
    public string n8nBaseUrl;
```

In `Assets/Scripts/Main/Manager.cs`, after `public static string n8nAPIKey => Secrets.Data.n8nAPIKey;` (line 146), add a **stub** that intentionally returns the value verbatim (so the empty/null/trailing tests fail):

```csharp
    public static string n8nAPIKey => Secrets.Data.n8nAPIKey;
    public static string n8nBaseUrl => ResolveN8nBaseUrl(Secrets.Data.n8nBaseUrl);

    public static string ResolveN8nBaseUrl(string configured) => configured; // STUB
```

- [ ] **Step 3: Run tests, verify they fail**

Run (Editor closed): `Tools/run-tests-headless.sh N8nBaseUrlTests`
Expected: FAIL — `EmptyConfig/NullConfig/WhitespaceConfig/TrailingSlash` fail (stub returns input unchanged; null would also throw). `ConfiguredValue_IsUsedVerbatim` passes.

- [ ] **Step 4: Implement the real resolver**

Replace the stub line in `Manager.cs`:

```csharp
    public static string ResolveN8nBaseUrl(string configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? "https://bagkz.app.n8n.cloud"
            : configured.TrimEnd('/');
```

- [ ] **Step 5: Run tests, verify they pass**

Run: `Tools/run-tests-headless.sh N8nBaseUrlTests`
Expected: PASS — all 5 tests green.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/Secrets.cs Assets/Scripts/Main/Manager.cs \
  Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs.meta
git commit -m "feat(n8n): configurable n8nBaseUrl with Cloud default

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: App — replace hardcoded Cloud URLs + fix UploadFile path

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` (8 active sites + 2 commented)
- Modify: `Assets/Scripts/Main/BotSettings.Auth.cs:510`

Within `Manager` methods, `n8nBaseUrl` is referenced unqualified (like `n8nAPIKey`). From `BotSettings.Auth.cs` use `Manager.n8nBaseUrl`.

- [ ] **Step 1: Replace the webhook POST literals in Manager.cs**

`Manager.cs:2458`:
```csharp
        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/CreateWhatsappWorkflow", form);
```
`Manager.cs:2601`:
```csharp
        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/CreateTelegramWorkflow", form);
```
`Manager.cs:2812`:
```csharp
            using UnityWebRequest editWhatsappRequest = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/EditWhatsappWorkflow", form);
```
`Manager.cs:2849`:
```csharp
            using UnityWebRequest editTelegramRequest = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/EditTelegramWorkflow", form);
```

- [ ] **Step 2: Replace the api/v1 literals in Manager.cs**

`Manager.cs:2641` and `Manager.cs:2671` (both identical — activate/deactivate):
```csharp
        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/api/v1/workflows/{id}/" + (enabled ? "activate" : "deactivate"), form);
```
`Manager.cs:2692` (delete whatsapp):
```csharp
        using UnityWebRequest whatsappRequest = UnityWebRequest.Delete($"{n8nBaseUrl}/api/v1/workflows/{whatsappWorkflowId}");
```
`Manager.cs:2710` (delete telegram):
```csharp
        using UnityWebRequest telegramRequest = UnityWebRequest.Delete($"{n8nBaseUrl}/api/v1/workflows/{telegramWorkflowId}");
```

- [ ] **Step 3: Fix the UploadFile path + base in both upload sites**

`Manager.cs:3066`:
```csharp
        string uploadUrl = $"{n8nBaseUrl}/webhook/UploadFile";
```
`BotSettings.Auth.cs:510`:
```csharp
        using UnityWebRequest www = UnityWebRequest.Post($"{Manager.n8nBaseUrl}/webhook/UploadFile", form);
```

- [ ] **Step 4: Remove the two dead commented literals**

Delete the commented-out lines at `Manager.cs:2366` and `Manager.cs:2508` (commented `// using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/Create…Workflow", form);`). They are stale duplicates that would otherwise trip the grep guard.

- [ ] **Step 5: Grep guard — zero literals remain**

Run: `grep -rn "bagkz.app.n8n.cloud" Assets/Scripts --include='*.cs'`
Expected: **no output** (exit 1). If anything prints, replace it the same way.

- [ ] **Step 6: Compile check**

If the Editor is open, trigger a recompile (test bridge / MCP `recompile_scripts`) and confirm no errors in the console. If closed, run `Tools/run-tests-headless.sh N8nBaseUrlTests` (a clean compile is required for tests to run at all).
Expected: compiles clean; the 5 tests still PASS.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Main/Manager.cs Assets/Scripts/Main/BotSettings.Auth.cs
git commit -m "refactor(n8n): route all n8n calls through n8nBaseUrl; fix UploadFile path

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: App — document the new secrets field

**Files:**
- Modify: `Assets/StreamingAssets/secrets.json.example`

- [ ] **Step 1: Add `n8nBaseUrl` to the example**

Edit `Assets/StreamingAssets/secrets.json.example` — add the field after `n8nAPIKey`:

```json
{
    "wappiAuthToken": "YOUR_WAPPI_AUTH_TOKEN",
    "n8nAPIKey": "YOUR_N8N_API_KEY",
    "n8nBaseUrl": "",
    "telegramBotToken": "YOUR_TELEGRAM_BOT_TOKEN",
```

(Empty = production Cloud. For local dev, set it to your cloudflared HTTPS URL, e.g. `https://bagkz-dev.example.com`.)

- [ ] **Step 2: Commit**

```bash
git add Assets/StreamingAssets/secrets.json.example
git commit -m "docs(n8n): add n8nBaseUrl to secrets.json.example

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Tooling — dev-config transform script

**Files:**
- Create: `Tools/n8n/apply-dev-config.py`
- Modify: `.gitignore`

- [ ] **Step 1: Add the gitignore entry**

Append to `.gitignore` under the existing n8n block:

```
# n8n derived dev-config workflows (generated by apply-dev-config.py)
Tools/n8n/workflows-local/
```

- [ ] **Step 2: Write the transform script**

Create `Tools/n8n/apply-dev-config.py`:

```python
#!/usr/bin/env python3
"""Derive local-dev n8n workflows from the canonical (Cloud-shaped) set.

Reads Tools/n8n/workflows/*.json, rewrites the n8n-API host to localhost,
fixes the trailing-space /activate typo, remaps credential ids to the local
instance's credentials (matched by credential NAME from ~/.n8n/database.sqlite),
and writes the result to Tools/n8n/workflows-local/ (gitignored).

Usage: python3 Tools/n8n/apply-dev-config.py
Then:  n8n import:workflow --separate --input=Tools/n8n/workflows-local/
"""
import json, os, sqlite3, sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(REPO, "Tools/n8n/workflows")
OUT = os.path.join(REPO, "Tools/n8n/workflows-local")
DB = os.path.expanduser("~/.n8n/database.sqlite")

CLOUD_API = "https://bagkz.app.n8n.cloud/api/v1"
LOCAL_API = "http://localhost:5678/api/v1"


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


def main():
    os.makedirs(OUT, exist_ok=True)
    cred_by_name = local_credentials()
    missing = set()
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".json"))
    for f in files:
        text = open(os.path.join(SRC, f)).read()
        text = text.replace(CLOUD_API, LOCAL_API)
        text = text.replace("/activate ", "/activate")  # trailing-space bug
        wf = json.loads(text)
        for node in wf.get("nodes", []):
            remap_credentials(node, cred_by_name, missing)
        json.dump(wf, open(os.path.join(OUT, f), "w"), indent=2, ensure_ascii=False)
        print(f"  wrote {f}")
    print(f"\n{len(files)} workflows -> {OUT}")
    print(f"local credentials found: {sorted(cred_by_name)}")
    if missing:
        print(f"\n⚠️  credentials referenced but NOT found locally (create them, re-run): {sorted(missing)}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Smoke-test the script against the canonical set**

Run: `python3 Tools/n8n/apply-dev-config.py`
Expected (before any local credentials exist): writes 7 files to `Tools/n8n/workflows-local/`, and warns that credentials like `WappiAuthToken`, `n8nAPIKey`, `OpenAi…` are not found locally yet. That warning is expected at this stage.

- [ ] **Step 4: Verify the URL rewrite + typo fix landed**

Run:
```bash
grep -rl "bagkz.app.n8n.cloud/api/v1" Tools/n8n/workflows-local/ ; echo "cloud-api refs: $?"
grep -rn "localhost:5678/api/v1" Tools/n8n/workflows-local/ | wc -l | xargs echo "localhost refs:"
grep -rn "/activate " Tools/n8n/workflows-local/ | wc -l | xargs echo "trailing-space activate (want 0):"
```
Expected: no cloud-api refs (grep exits 1), ≥1 localhost ref, 0 trailing-space activate.

- [ ] **Step 5: Commit (script + gitignore only; workflows-local/ is ignored)**

```bash
git add Tools/n8n/apply-dev-config.py .gitignore
git commit -m "tooling(n8n): apply-dev-config.py to derive local-dev workflows

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: 🧑 USER ACTION — run local n8n behind a named cloudflared tunnel

**Files:**
- Create: `Tools/n8n/dev-tunnel.md` (runbook the agent writes; the commands are run by the user)

- [ ] **Step 1: Write the runbook**

Create `Tools/n8n/dev-tunnel.md`:

```markdown
# Local n8n + cloudflared tunnel (dev)

Prereqs: `brew install cloudflared` (or download). n8n already installed (2.27.4).

## One-time: named tunnel (stable URL)
1. `cloudflared tunnel login`
2. `cloudflared tunnel create bagkz-dev`  → note the tunnel UUID
3. Route a hostname you control:
   `cloudflared tunnel route dns bagkz-dev bagkz-dev.<yourdomain>`
4. Create `~/.cloudflared/config.yml`:
   ```yaml
   tunnel: bagkz-dev
   credentials-file: /Users/ayan/.cloudflared/<UUID>.json
   ingress:
     - hostname: bagkz-dev.<yourdomain>
       service: http://localhost:5678
     - service: http_status:404
   ```

## Each dev session
1. Terminal A: `WEBHOOK_URL=https://bagkz-dev.<yourdomain> n8n start`
2. Terminal B: `cloudflared tunnel run bagkz-dev`
3. Verify: `curl -o /dev/null -w '%{http_code}\n' https://bagkz-dev.<yourdomain>/healthz` → 200

## No domain? Quick (rotating) tunnel
`cloudflared tunnel --url http://localhost:5678` prints a `https://<random>.trycloudflare.com`
URL — usable but it changes each run, so update `secrets.json` → `n8nBaseUrl` and restart n8n
with that `WEBHOOK_URL` each time.
```

- [ ] **Step 2: Commit the runbook**

```bash
git add Tools/n8n/dev-tunnel.md
git commit -m "docs(n8n): local n8n + cloudflared tunnel runbook

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 3: 🧑 User performs the runbook**

User runs the one-time setup + starts n8n (with `WEBHOOK_URL`) and the tunnel.
Verify together: `curl -o /dev/null -w '%{http_code}\n' https://<tunnel-host>/healthz` → `200`.

---

## Task 6: 🧑 USER ACTION — local n8n API key + credentials

- [ ] **Step 1: Generate the local n8n API key**

🧑 User: n8n UI → Settings → **n8n API** → Create an API key. Copy it.

- [ ] **Step 2: Create the credentials the handlers/clone-sources need**

🧑 User: n8n UI → Credentials → New, creating at minimum (for sub-project 1):
- **`n8nAPIKey`** — type *Header Auth*, header name `X-N8N-API-KEY`, value = the key from Step 1.
- **`WappiAuthToken`** — type *Header Auth*, header name `Authorization`, value = the Wappi token (from `secrets.json` → `wappiAuthToken`).

Use these exact credential **names** — the transform script matches by name.

- [ ] **Step 3: Verify the credentials exist in the local DB**

Run:
```bash
sqlite3 ~/.n8n/database.sqlite "SELECT name, type FROM credentials_entity ORDER BY name;"
```
Expected: rows for `n8nAPIKey` and `WappiAuthToken`.

---

## Task 7: Apply dev config, import, and activate

**Depends on:** Tasks 4, 6 (script exists; local credentials exist).

- [ ] **Step 1: Re-run the transform now that credentials exist**

Run: `python3 Tools/n8n/apply-dev-config.py`
Expected: 7 files written; `n8nAPIKey` and `WappiAuthToken` no longer in the "missing" list (other runtime creds may still be missing — fine for sub-project 1).

- [ ] **Step 2: Import the dev-config workflows into local n8n**

Stop n8n first (CLI uses the DB directly), then:
```bash
n8n import:workflow --separate --input=Tools/n8n/workflows-local/
```
Expected: `Successfully imported 7 workflows.`

- [ ] **Step 3: Verify ids preserved + handler URLs repointed**

```bash
sqlite3 ~/.n8n/database.sqlite "SELECT id,name,active FROM workflow_entity ORDER BY name;"
sqlite3 ~/.n8n/database.sqlite "SELECT COUNT(*) FROM workflow_entity WHERE id IN ('4wYitz5ek30SVNlT','4VN3gsFaC2HUYmcc');"
```
Expected: 7 workflows; the two clone-source ids both present (count = 2).

- [ ] **Step 4: 🧑 Activate the handler + upload workflows**

🧑 User (n8n UI, with n8n running): activate `CreateWhatsappWorkflow`, `CreateTelegramWorkflow`, `Edit Whatsapp Workflow`, `Edit Telegram Workflow`, and `Upload File`. Leave the two clone sources (`WhatsApp Bot`, `Telegram Bot`) **inactive**.
Verify: `curl -o /dev/null -w '%{http_code}\n' https://<tunnel-host>/webhook/CreateWhatsappWorkflow` (POST-only path → expect 404/405 on GET, which confirms it's registered, not a connection error).

---

## Task 8: 🧑 USER ACTION — end-to-end device verification (definition of done)

- [ ] **Step 1: Point the app at local n8n**

🧑 User: edit `Assets/StreamingAssets/secrets.json` (gitignored):
- set `n8nBaseUrl` to the tunnel URL `https://<tunnel-host>`
- set `n8nAPIKey` to the local n8n API key (from Task 6 Step 1)

- [ ] **Step 2: Build to the physical device**

🧑 User: build/run the app on the device (same as normal dev).

- [ ] **Step 3: Round-trip the bot lifecycle**

🧑 User performs in the app; verify each against local n8n:
1. **Create** a WhatsApp bot → `sqlite3 ~/.n8n/database.sqlite "SELECT name,active FROM workflow_entity;"` shows a new per-bot workflow, **active = 1**.
2. **Edit** the bot's prompt → re-query / open in n8n UI; the agent systemMessage updated.
3. **Deactivate** then **activate** from the app → the per-bot workflow's `active` flips accordingly.
4. **Delete** the bot → the per-bot workflow is removed from `workflow_entity`.

- [ ] **Step 4: Confirm production is unaffected**

🧑 User: temporarily blank `n8nBaseUrl` in `secrets.json` → app targets Cloud again (smoke check one call). Restore the tunnel URL for continued dev.

**Definition of done met when Steps 3.1–3.4 all pass against local n8n.**

---

## Self-Review

**Spec coverage:**
- Component A (configurable base URL) → Tasks 1, 2, 3 ✅
- UploadFile `/webhook-test/` → `/webhook/` fix → Task 2 Step 3 ✅
- Component B (n8n + named tunnel + WEBHOOK_URL + secrets) → Tasks 5, 8 Step 1 ✅
- Component C (local API key, handler repoint to localhost, `/activate ` fix, credential remap, gitignored derived set) → Tasks 4, 6, 7 ✅
- Source-of-truth decision (canonical committed; `workflows-local/` gitignored; transform script committed) → Task 4 ✅
- Upload File active in dev → Task 7 Step 4 ✅
- Testing (EditMode resolver test + grep guard + manual round-trip) → Task 1, Task 2 Step 5, Task 8 ✅
- Production-default safety → Task 1 (resolver) + Task 8 Step 4 ✅

**Placeholder scan:** No TBD/TODO; every code/script step shows complete content. ✅

**Type consistency:** `Manager.ResolveN8nBaseUrl(string)` and `Manager.n8nBaseUrl` are named identically across Tasks 1, 2, and the test. Credential names (`n8nAPIKey`, `WappiAuthToken`) match between Task 6 (creation) and Task 4 (script matching). Clone-source ids (`4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`) consistent with the spec. ✅
