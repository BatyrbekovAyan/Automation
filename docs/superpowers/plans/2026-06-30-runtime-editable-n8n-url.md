# Runtime-editable n8n URL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the dev change the n8n base URL on the device (no rebuild) via an in-app field, overriding `secrets.json`.

**Architecture:** `Manager.n8nBaseUrl` resolves an on-device PlayerPrefs override first, then `secrets.json`, then the Cloud default. The override is set through a new `TMP_InputField` in the existing Profile → Edit popup, shown only in Development builds.

**Tech Stack:** Unity 6 / C# (EditMode tests via the in-Editor test runner), TMPro, PlayerPrefs.

**Spec:** `docs/superpowers/specs/2026-06-30-runtime-editable-n8n-url-design.md`

**Notes for the executor:**
- No worktrees (project convention); execute on `main`. Commits end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Unity Editor is open → run EditMode tests via the MCP (`mcp__mcp-unity__run_tests`, EXACT class filter `N8nBaseUrlTests`) or the test bridge; recompile via `mcp__mcp-unity__recompile_scripts`. MCP calls can time out when Unity is unfocused — retry; a domain reload briefly drops the MCP connection (wait ~6s, retry).
- The new EditMode tests go in the EXISTING `Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs` (already imported) to avoid the new-file import quirk.
- Current resolver (`Manager.cs:150-155`):
  ```csharp
  public static string n8nBaseUrl => ResolveN8nBaseUrl(Secrets.Data.n8nBaseUrl);
  public static string ResolveN8nBaseUrl(string configured) =>
      string.IsNullOrWhiteSpace(configured) ? "https://bagkz.app.n8n.cloud" : configured.TrimEnd('/');
  ```

---

## File Structure
- `Assets/Scripts/Main/Manager.cs` — add `DevN8nBaseUrlKey`, a 2-arg `ResolveN8nBaseUrl` overload, update `n8nBaseUrl` (modify)
- `Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs` — add override-precedence tests (modify)
- `Assets/Scripts/Main/ProfilePage.cs` — add `editN8nUrlInput`, load/save/gate in the edit popup (modify)
- `Assets/Scenes/Main.unity` (edit popup) — add the input field, wire the serialized ref (modify, in Editor)

---

## Task 1: Resolution precedence (TDD)

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs`
- Test: `Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs`

- [ ] **Step 1: Add failing tests** for the 2-arg overload to `N8nBaseUrlTests.cs` (append inside the class, before the closing brace):

```csharp
    // --- override precedence (2-arg) ---
    [Test]
    public void Override_WinsOverConfigured()
    {
        Assert.AreEqual("https://override.example",
            Manager.ResolveN8nBaseUrl("https://override.example", "https://configured.example"));
    }

    [Test]
    public void BlankOverride_FallsBackToConfigured()
    {
        Assert.AreEqual("https://configured.example",
            Manager.ResolveN8nBaseUrl("", "https://configured.example"));
    }

    [Test]
    public void WhitespaceOverride_FallsBackToConfigured()
    {
        Assert.AreEqual("https://configured.example",
            Manager.ResolveN8nBaseUrl("   ", "https://configured.example"));
    }

    [Test]
    public void BothBlank_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl("", ""));
    }

    [Test]
    public void Override_TrailingSlashAndWhitespaceTrimmed()
    {
        Assert.AreEqual("https://override.example",
            Manager.ResolveN8nBaseUrl("  https://override.example/  ", "x"));
    }
```

- [ ] **Step 2: Add the 2-arg overload as a STUB** (so it compiles and the override tests fail). In `Manager.cs`, replace the current resolver block with:

```csharp
    public static string n8nBaseUrl => ResolveN8nBaseUrl(Secrets.Data.n8nBaseUrl);

    public static string ResolveN8nBaseUrl(string configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? "https://bagkz.app.n8n.cloud"
            : configured.TrimEnd('/');

    public static string ResolveN8nBaseUrl(string overrideUrl, string configured) =>
        ResolveN8nBaseUrl(configured); // STUB — ignores override; replaced in Step 4
```

- [ ] **Step 3: Run tests — verify the override tests FAIL**

Recompile (`mcp__mcp-unity__recompile_scripts`), then run `N8nBaseUrlTests` (EditMode).
Expected: `Override_WinsOverConfigured` and `Override_TrailingSlashAndWhitespaceTrimmed` FAIL (stub returns configured); the other new + original tests PASS.

- [ ] **Step 4: Implement the real overload + delegate the 1-arg version**

Replace the resolver block again with:

```csharp
    public const string DevN8nBaseUrlKey = "DevN8nBaseUrl";

    public static string n8nBaseUrl =>
        ResolveN8nBaseUrl(PlayerPrefs.GetString(DevN8nBaseUrlKey, ""), Secrets.Data.n8nBaseUrl);

    public static string ResolveN8nBaseUrl(string configured) => ResolveN8nBaseUrl(null, configured);

    public static string ResolveN8nBaseUrl(string overrideUrl, string configured)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl)) return overrideUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');
        return "https://bagkz.app.n8n.cloud";
    }
```

- [ ] **Step 5: Run tests — verify ALL pass**

Recompile, run `N8nBaseUrlTests` (EditMode).
Expected: all tests PASS (original 5 + 5 new = 10).

- [ ] **Step 6: Commit**

```bash
cd /Users/ayan/Projects/Automation
git add Assets/Scripts/Main/Manager.cs Assets/Tests/Editor/Chat/N8nBaseUrlTests.cs
git commit -m "feat(n8n): n8nBaseUrl honors on-device DevN8nBaseUrl override

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: ProfilePage wiring (load / save / gate)

**Files:**
- Modify: `Assets/Scripts/Main/ProfilePage.cs`

The new `editN8nUrlInput` ref is assigned to a real UI object in Task 3; everything here is null-guarded so it compiles and runs before the UI exists.

- [ ] **Step 1: Add the serialized field** after `editEmailInput` (currently `ProfilePage.cs:45`):

```csharp
    [SerializeField] private TMP_InputField  editEmailInput;
    [SerializeField] private TMP_InputField  editN8nUrlInput;   // dev-only n8n URL override
```

- [ ] **Step 2: Load + gate in `OpenEditPopup`** — after the `editEmailInput.text = ...` line (currently `:167`), add:

```csharp
        editEmailInput.text = PlayerPrefs.GetString(KeyEmail, DefaultEmail);
        if (editN8nUrlInput != null)
        {
            editN8nUrlInput.gameObject.SetActive(Debug.isDebugBuild);
            editN8nUrlInput.text = PlayerPrefs.GetString(Manager.DevN8nBaseUrlKey, "");
        }
```

- [ ] **Step 3: Save in `SaveProfile`** — after the `editEmailInput`/`newEmail` save line (currently `:186`), add the override save before `PlayerPrefs.Save();`:

```csharp
        if (!string.IsNullOrEmpty(newEmail)) PlayerPrefs.SetString(KeyEmail, newEmail);
        if (editN8nUrlInput != null) PlayerPrefs.SetString(Manager.DevN8nBaseUrlKey, editN8nUrlInput.text.Trim());
        PlayerPrefs.Save();
```

(Note: the override is saved even when blank — blank clears it, which is the intended "fall back to secrets/Cloud" behavior. Name/email keep their non-empty guard, unchanged.)

- [ ] **Step 4: Recompile — verify clean**

Run `mcp__mcp-unity__recompile_scripts`.
Expected: compiles with 0 errors. `N8nBaseUrlTests` still pass (run once to confirm no regression).

- [ ] **Step 5: Commit**

```bash
cd /Users/ayan/Projects/Automation
git add Assets/Scripts/Main/ProfilePage.cs
git commit -m "feat(profile): dev-only n8n URL field in the edit popup (load/save override)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Add the input field to the edit popup (Unity Editor)

**Files:**
- Modify: `Assets/Scenes/Main.unity` (the Profile edit popup)

This is Editor work (the field must exist + be wired to `ProfilePage.editN8nUrlInput`). Do it in the open Editor — manually or via the mcp-unity tools — then **save the scene** (see [[project_unity_builder_scene_save]]).

- [ ] **Step 1: Duplicate the email input for style consistency**

In the Hierarchy, find the edit-popup's email `TMP_InputField` (the object assigned to `ProfilePage.editEmailInput`). Duplicate it; rename the copy to `EditN8nUrlInput`. (MCP: `mcp__mcp-unity__duplicate_gameobject` then rename, or do it by hand.)

- [ ] **Step 2: Position it below the email field**

Move `EditN8nUrlInput` directly under the email field in the popup layout (match the vertical spacing between name→email). If the popup uses a VerticalLayoutGroup, sibling order alone places it; otherwise set its anchoredPosition one row lower.

- [ ] **Step 3: Set the placeholder text**

On `EditN8nUrlInput`, set its Placeholder child (TextMeshPro) text to: `n8n URL (dev) — blank = secrets/Cloud`. Clear its input Text value.

- [ ] **Step 4: Wire the serialized reference**

Select the ProfilePage GameObject; assign `EditN8nUrlInput` to the **Edit N8n Url Input** field in the Inspector (the `editN8nUrlInput` serialized field from Task 2). (MCP: `mcp__mcp-unity__update_component` on ProfilePage, or assign by hand.)

- [ ] **Step 5: Save the scene + verify the wiring**

Save the scene (`mcp__mcp-unity__save_scene` or ⌘S in the Editor). Then verify the reference is set:
```bash
cd /Users/ayan/Projects/Automation
grep -c "editN8nUrlInput" Assets/Scripts/Main/ProfilePage.cs   # field exists in code
grep -c "EditN8nUrlInput" Assets/Scenes/Main.unity              # object exists in scene (>=1)
```
Expected: both ≥ 1. Confirm in the Editor that ProfilePage's `Edit N8n Url Input` slot is not "None".

- [ ] **Step 6: Commit**

```bash
cd /Users/ayan/Projects/Automation
git add Assets/Scenes/Main.unity
git commit -m "feat(profile): add dev n8n URL input to the edit popup (scene)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: 🧑 End-to-end verification (definition of done)

- [ ] **Step 1: Build & install a Development build** on the device (Unity Build Settings → Development Build checked).

- [ ] **Step 2: Set the URL in-app**

🧑 Profile → Edit → the **n8n URL (dev)** field is visible → paste the current tunnel URL (the value in `secrets.json` `n8nBaseUrl`) → Save.

- [ ] **Step 3: Create a bot → confirm it hits the override URL**

🧑 Create a bot, then:
```bash
sqlite3 -header -column ~/.n8n/database.sqlite "SELECT id, status, datetime(startedAt) FROM execution_entity WHERE workflowId='XuvOp7TxOImOAmlj' ORDER BY startedAt DESC LIMIT 2;"
```
Expected: a fresh `CreateWhatsappWorkflow` execution (proves the app reached n8n via the in-app URL).

- [ ] **Step 4: Change the URL with NO rebuild**

🧑 Restart the tunnel to get a new URL (or use any new value), update n8n + the in-app field to the new URL (Profile → Edit → paste → Save), create another bot.
Expected: a new execution appears for the new URL — **without rebuilding the app.** This is the definition of done.

- [ ] **Step 5: Clear-to-fallback check**

🧑 Profile → Edit → clear the field → Save. `Manager.n8nBaseUrl` now returns `secrets.json`'s value (or Cloud default if blank). Confirms the precedence chain.

---

## Self-Review

**Spec coverage:**
- Resolution precedence (override → configured → Cloud, trimmed) → Task 1 ✅
- `DevN8nBaseUrlKey` + live PlayerPrefs read in `n8nBaseUrl` → Task 1 Step 4 ✅
- In-app field in Profile → Edit popup, load/save/clear, dev-build gating → Tasks 2 & 3 ✅
- Existing 1-arg tests preserved (1-arg delegates) → Task 1 Step 4/5 ✅
- DoD (set URL in-app, create bot, change URL without rebuild, clear-to-fallback) → Task 4 ✅
- Production safety (no override + blank secrets → Cloud default) → covered by `BothBlank_FallsBackToCloud` ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete content. Task 3 is Editor work with exact steps + a grep/Inspector verification.

**Type/name consistency:** `Manager.DevN8nBaseUrlKey`, `ResolveN8nBaseUrl(overrideUrl, configured)`, `editN8nUrlInput`, scene object `EditN8nUrlInput`, placeholder text, and `CloudDefault` (already a const in `N8nBaseUrlTests`) are used consistently across Tasks 1–4.
