# Phantom Blank Cards Follow-ups Implementation Plan (M1 + M2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compact pre-existing PlayerPrefs phantom Product/Service slot data on app launch (M1), and fix the discarded `Name.Trim()` result in `SaveSettings` (M2).

**Architecture:** Three new private static helpers in `Manager.cs` (`MigrateBotPersistence`, `CompactSavedProducts`, `CompactSavedServices`) called from the very top of `LoadBots()`. The migration walks each saved bot's slot keys, compacts non-empty entries to contiguous indices, deletes orphans, and writes the corrected count. Idempotent. M2 is a 5-line surgical edit to each of the products and services save loops, replacing the no-op `Name.Trim()` with a properly-trimmed local variable used in both the predicate and the write.

**Tech Stack:** Unity 6000.3.9f1, C#, PlayerPrefs (Unity's built-in key-value store). Manual verification in Unity Play mode (no automated tests in this project).

**Spec:** [docs/superpowers/specs/2026-04-29-phantom-cards-followups-design.md](../specs/2026-04-29-phantom-cards-followups-design.md)

---

## File Structure

**Modify only:**

- `Assets/Scripts/Main/Manager.cs` — adds three private static helpers near the existing `CountNonEmptyProductCards` / `CountNonEmptyServiceCards` (around line 351-383, just above `SaveSettings`); adds a one-line `MigrateBotPersistence();` call at the top of `LoadBots()` (around line 244); rewrites the products save loop body (around line 418-428) and services save loop body (around line 456-466) to use a trimmed local Name.

**Do NOT modify:**

- Any other file in `Assets/Scripts/`.
- Any prefab.
- The existing `CountNonEmptyProductCards` / `CountNonEmptyServiceCards` helpers — their `IsNullOrEmpty(card.Name)` predicate stays as-is per spec (the divergence with the trim-aware save predicate is acknowledged and unreachable in practice).

---

## Task 1: M1 — Compacting migration

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — add three new private static helpers, one new line in `LoadBots()`.

- [ ] **Step 1 — Add `CompactSavedProducts` helper**

In `Assets/Scripts/Main/Manager.cs`, locate the existing `CountNonEmptyServiceCards` helper (around line 373-383, just above `public void SaveSettings()`). Directly AFTER `CountNonEmptyServiceCards` and BEFORE `SaveSettings`, insert this helper:

```csharp
    // Compacts a saved bot's product slot keys: walks PlayerPrefs entries
    // 0..oldCount-1, collects non-empty slots (Name key non-empty per
    // string.IsNullOrEmpty), writes them back at contiguous indices 0..N-1,
    // deletes orphans at [N..oldCount-1], and rewrites the saved
    // ProductsNumber to N. No-op if the data is already clean. Pure data
    // operation — does not touch the live UI / scene.
    private static void CompactSavedProducts(string botKey)
    {
        int oldCount = PlayerPrefs.GetInt(botKey + "ProductsNumber", 0);
        if (oldCount <= 0) return;

        var liveNames = new System.Collections.Generic.List<string>(oldCount);
        var livePrices = new System.Collections.Generic.List<string>(oldCount);
        var liveDescriptions = new System.Collections.Generic.List<string>(oldCount);

        for (int p = 0; p < oldCount; p++)
        {
            var name = PlayerPrefs.GetString(botKey + "Product" + p, "");
            if (string.IsNullOrEmpty(name)) continue;
            liveNames.Add(name);
            livePrices.Add(PlayerPrefs.GetString(botKey + "Product" + p + "Price", ""));
            liveDescriptions.Add(PlayerPrefs.GetString(botKey + "Product" + p + "Description", ""));
        }

        if (liveNames.Count == oldCount) return; // already compact

        for (int p = 0; p < liveNames.Count; p++)
        {
            PlayerPrefs.SetString(botKey + "Product" + p, liveNames[p]);
            PlayerPrefs.SetString(botKey + "Product" + p + "Price", livePrices[p]);
            PlayerPrefs.SetString(botKey + "Product" + p + "Description", liveDescriptions[p]);
        }

        for (int p = liveNames.Count; p < oldCount; p++)
        {
            PlayerPrefs.DeleteKey(botKey + "Product" + p);
            PlayerPrefs.DeleteKey(botKey + "Product" + p + "Price");
            PlayerPrefs.DeleteKey(botKey + "Product" + p + "Description");
        }

        PlayerPrefs.SetInt(botKey + "ProductsNumber", liveNames.Count);
    }
```

Use 4-space indentation (matching the surrounding `Manager` class methods — `Manager` lives at the top level with no namespace).

- [ ] **Step 2 — Add `CompactSavedServices` helper**

Directly AFTER the `CompactSavedProducts` method you just added, insert the parallel services-side helper:

```csharp
    // Service-side mirror of CompactSavedProducts. Two helpers (rather than
    // a generic) because product and service slot keys differ in name
    // ("Product" vs "Service") and live-list helper signatures differ; the
    // duplication is mechanical and isolated.
    private static void CompactSavedServices(string botKey)
    {
        int oldCount = PlayerPrefs.GetInt(botKey + "ServicesNumber", 0);
        if (oldCount <= 0) return;

        var liveNames = new System.Collections.Generic.List<string>(oldCount);
        var livePrices = new System.Collections.Generic.List<string>(oldCount);
        var liveDescriptions = new System.Collections.Generic.List<string>(oldCount);

        for (int s = 0; s < oldCount; s++)
        {
            var name = PlayerPrefs.GetString(botKey + "Service" + s, "");
            if (string.IsNullOrEmpty(name)) continue;
            liveNames.Add(name);
            livePrices.Add(PlayerPrefs.GetString(botKey + "Service" + s + "Price", ""));
            liveDescriptions.Add(PlayerPrefs.GetString(botKey + "Service" + s + "Description", ""));
        }

        if (liveNames.Count == oldCount) return; // already compact

        for (int s = 0; s < liveNames.Count; s++)
        {
            PlayerPrefs.SetString(botKey + "Service" + s, liveNames[s]);
            PlayerPrefs.SetString(botKey + "Service" + s + "Price", livePrices[s]);
            PlayerPrefs.SetString(botKey + "Service" + s + "Description", liveDescriptions[s]);
        }

        for (int s = liveNames.Count; s < oldCount; s++)
        {
            PlayerPrefs.DeleteKey(botKey + "Service" + s);
            PlayerPrefs.DeleteKey(botKey + "Service" + s + "Price");
            PlayerPrefs.DeleteKey(botKey + "Service" + s + "Description");
        }

        PlayerPrefs.SetInt(botKey + "ServicesNumber", liveNames.Count);
    }
```

Note on the saved-key naming convention: this assumes services slots are saved as `Service[i]` / `Service[i]Price` / `Service[i]Description` keyed under the bot name. If the actual save loop uses a different naming pattern (e.g., `Service` followed by a different suffix), STOP and ask before proceeding — the helpers must use the exact same key names that `SaveSettings` writes. To verify, the implementer should grep for `"Service" +` in `Manager.cs:SaveSettings` and confirm the exact format before completing this step.

- [ ] **Step 3 — Add `MigrateBotPersistence` orchestrator**

Directly AFTER `CompactSavedServices`, insert the orchestrator that iterates all saved bots:

```csharp
    // One-shot migration that runs at the top of LoadBots(). Walks every
    // saved bot (using the same enumeration LoadBots itself uses) and
    // compacts each bot's products and services slot keys. Idempotent
    // (compacting clean data is a no-op) and synchronous (PlayerPrefs is
    // synchronous). Closes the gap left by Fix C: SaveSettings's saved-
    // count is correct going forward, but pre-existing data with a mid-
    // list phantom slot would still re-create a blank card on next
    // LoadBots without compaction.
    private void MigrateBotPersistence()
    {
        for (int i = 0; i < id; i++)
        {
            string botKey = "Bot" + i.ToString();
            if (!PlayerPrefs.HasKey(botKey + "Name")) continue;
            CompactSavedProducts(botKey);
            CompactSavedServices(botKey);
        }
    }
```

This method is `private` (not `private static`) because it reads the instance field `id`. The two compaction helpers it calls remain `private static` since they only touch PlayerPrefs.

- [ ] **Step 4 — Wire `MigrateBotPersistence` into `LoadBots`**

Find the existing `LoadBots()` method (around line 243 in the dirty file):

```csharp
    public IEnumerator LoadBots()
    {
        yield return new WaitForEndOfFrame();

        for (int i = 0; i < id; i++)
        {
```

Replace with:

```csharp
    public IEnumerator LoadBots()
    {
        // One-shot migration: compact pre-existing phantom-blank Product/Service
        // slot keys before LoadBots reads them. Idempotent on clean data.
        MigrateBotPersistence();

        yield return new WaitForEndOfFrame();

        for (int i = 0; i < id; i++)
        {
```

Note: `MigrateBotPersistence()` is synchronous (no `yield`), so it runs before the `WaitForEndOfFrame` and before the per-bot loop. By the time the loop reads slot keys, the data is compacted.

- [ ] **Step 5 — Verify compile**

Switch to Unity Editor. Wait for auto-recompile. Watch the Console window. Expected: no compile errors. The `.claude/hooks/validate-cs.sh` hook should pass on each Edit.

- [ ] **Step 6 — Manual verification in Play mode (M1 scenarios)**

These scenarios require seeding PlayerPrefs with synthetic phantom data. The simplest way is via a one-off Editor menu item or directly from a temporary script. The implementer can use whichever method is least disruptive — the goal is to assert the migration runs correctly.

**Suggested approach:** Use `Edit → Clear All PlayerPrefs` first to start clean, then run a tiny scratch Editor script (or Unity Test Runner one-off) that calls `PlayerPrefs.SetInt("Bot0ProductsNumber", 3); PlayerPrefs.SetString("Bot0Product0", "Tea"); PlayerPrefs.SetString("Bot0Product2", "Coffee"); PlayerPrefs.SetString("Bot0Name", "Test"); PlayerPrefs.Save();` — note the missing `Bot0Product1` key. Also set the bot enumeration counter (likely `id` is persisted somewhere — check if needed; otherwise `Manager.Awake/Start` may need the counter to discover bot 0).

Then enter Play mode. Expected: Bot 0 loads with 2 products (`Tea` and `Coffee`), no blank phantom in the middle. Inspect PlayerPrefs after load: `Bot0ProductsNumber == 2`, `Bot0Product0 == "Tea"`, `Bot0Product1 == "Coffee"` (compacted from index 2), `Bot0Product2` keys deleted.

Other scenarios from the spec to verify:
- Clean roundtrip (no phantoms): save 3 products, reload → all 3 present, migration no-op (PlayerPrefs identical before/after).
- All-blank slots: seed `Bot0ProductsNumber = 2` with no `Product0`/`Product1` keys → after load, `ProductsNumber == 0`.
- Idempotence: run any scenario, then quit and re-launch Play mode → no further changes.
- Repeat for services.

If any scenario fails, do NOT proceed — diagnose first.

- [ ] **Step 7 — Commit (DEFERRED to user)**

Per session decision, no commits. Leave changes uncommitted.

---

## Task 2: M2 — Trim fix in SaveSettings

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — products save loop body and services save loop body inside `SaveSettings`.

- [ ] **Step 1 — Verify the products save loop's exact current shape**

Read `Manager.cs` around line 416-428 (the products save loop body). The current code (after Task 1's helper insertions, line numbers will have shifted; search for `product.GetComponent<ProductCardView>().Name.Trim()` if the line number has drifted):

```csharp
        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount; i++)
        {
            Transform product = openBotSettings.ProductsParent.transform.GetChild(i);

            product.GetComponent<ProductCardView>().Name.Trim();
            if (!product.GetComponent<ProductCardView>().Name.Equals(""))
            {
                PlayerPrefs.SetString(openBot.name + "Product" + i, product.GetComponent<ProductCardView>().Name);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Price", product.GetComponent<ProductCardView>().Price);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Description", product.GetComponent<ProductCardView>().Description);
            }
        }
```

If the loop body looks materially different (e.g., the `Trim()` line is missing, or the structure has changed), STOP and ask. Otherwise proceed.

- [ ] **Step 2 — Replace the products save loop body**

Replace the body of the for-loop above with:

```csharp
        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount; i++)
        {
            Transform product = openBotSettings.ProductsParent.transform.GetChild(i);
            var card = product.GetComponent<ProductCardView>();
            // Trim once into a local. Use the trimmed value for BOTH the
            // empty-check and the SetString write so leading/trailing
            // whitespace doesn't survive into PlayerPrefs.
            // Note: CountNonEmptyProductCards (the Fix C count helper) does
            // NOT trim — its predicate is !IsNullOrEmpty(raw). The 1-off
            // count drift in the whitespace-only case is unreachable in
            // practice (ItemEditSheet.Commit's IsNullOrWhiteSpace fallback
            // prevents whitespace-only Names from being committed).
            var name = card.Name?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                PlayerPrefs.SetString(openBot.name + "Product" + i, name);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Price", card.Price);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Description", card.Description);
            }
        }
```

Key differences from before:
- `var card = product.GetComponent<ProductCardView>();` cached locally (was 4× repeated `GetComponent<>` calls).
- `var name = card.Name?.Trim() ?? string.Empty;` actually uses the trimmed value (was `Trim()` discarded).
- Predicate is `!string.IsNullOrEmpty(name)` (was `!card.Name.Equals("")`).
- The Name `SetString` writes `name` (the trimmed local).
- `Price` and `Description` are NOT trimmed — they're not identifiers and may contain meaningful whitespace.

- [ ] **Step 3 — Verify the services save loop's exact current shape**

Read `Manager.cs` around line 454-466 (the services save loop body, after Task 1 line shifts). Current code should be:

```csharp
        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount; i++)
        {
            Transform service = openBotSettings.ServicesParent.transform.GetChild(i);

            service.GetComponent<ServiceCardView>().Name.Trim();
            if (!service.GetComponent<ServiceCardView>().Name.Equals(""))
            {
                PlayerPrefs.SetString(openBot.name + "Service" + i, service.GetComponent<ServiceCardView>().Name);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Price", service.GetComponent<ServiceCardView>().Price);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Description", service.GetComponent<ServiceCardView>().Description);
            }
        }
```

If the structure differs materially, STOP and ask. Otherwise proceed.

(If the actual key prefix in the existing code is something other than `"Service"`, that's important — the M1 helpers in Task 1 Step 2 must use the same prefix. STOP and report so Task 1 can be revised.)

- [ ] **Step 4 — Replace the services save loop body**

Replace the body of the for-loop above with:

```csharp
        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount; i++)
        {
            Transform service = openBotSettings.ServicesParent.transform.GetChild(i);
            var card = service.GetComponent<ServiceCardView>();
            var name = card.Name?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                PlayerPrefs.SetString(openBot.name + "Service" + i, name);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Price", card.Price);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Description", card.Description);
            }
        }
```

The services version omits the long explanatory comment present in the products version — the products version already documents the design choice; duplicating it on the services side would be noise. The mechanical mirror is obvious from the parallel structure.

- [ ] **Step 5 — Verify compile**

Switch to Unity Editor. Wait for auto-recompile. Watch the Console window. Expected: no compile errors.

- [ ] **Step 6 — Manual verification in Play mode (M2 scenarios)**

Press Play in Unity. Run these scenarios:

7. **Whitespace at edges roundtrip** — Bot Settings → Products → tap **+ Add** → in the name field, type `"  Tea  "` (two leading spaces, two trailing spaces). Press Done. (`ItemEditSheet.Commit` does NOT trim, so the live `card.Name` is `"  Tea  "`.) Save the bot. Quit Play mode and re-enter (or close and re-open Bot Settings to trigger LoadBots → CloseSettings reload). Expected: card displays `"Tea"` (the trimmed value loaded back).

8. **Pre-trim regression — common case** — Add a product with a normally-named value (no leading/trailing whitespace, e.g., `"Coffee"`). Save. Reload. Expected: card displays `"Coffee"` (unchanged).

9. **Repeat 7–8 for services.**

If any scenario fails, do NOT proceed — diagnose first.

- [ ] **Step 7 — Commit (DEFERRED to user)**

Per session decision, no commits.

---

## Self-Review

**1. Spec coverage:**
- Spec §"M1 — Compacting migration" → Task 1, Steps 1–4 (helpers + LoadBots wiring).
  - `CompactSavedProducts` → Task 1 Step 1. ✓
  - `CompactSavedServices` → Task 1 Step 2. ✓
  - `MigrateBotPersistence` orchestrator → Task 1 Step 3. ✓
  - `LoadBots` wiring → Task 1 Step 4. ✓
  - Edge cases (clean / all-blank / idempotent / mid-list phantom) → Task 1 Step 6 verification scenarios. ✓
- Spec §"M2 — `Name.Trim()` discarded-result fix" → Task 2 Steps 2 + 4 (products and services save loop body rewrites).
  - Products body → Task 2 Step 2. ✓
  - Services body → Task 2 Step 4. ✓
  - Local `card` variable + trimmed `name` local → both Steps. ✓
  - Comment about count-helper divergence → Task 2 Step 2 (in the products body comment, referenced from services body). ✓
  - Out-of-scope items (Price/Description not trimmed; count helper unchanged) → Task 2 Step 2 callouts. ✓
- Spec §"Out of scope" → no related tasks created. ✓
- Spec §"Constraints / properties preserved" — no PlayerPrefs schema change, no public API change, idempotent migration. Plan tasks reflect these by not introducing new public surfaces. ✓

**2. Placeholder scan:** No "TBD" / "TODO" / "fill in" anywhere. All code blocks complete. The "DEFERRED to user" commit steps are explicit, not placeholders. The Service-key-prefix verification ask in Task 1 Step 2 / Task 2 Step 3 is conditional ("if the actual key prefix differs, STOP and ask") — this is a defensive escape hatch, not a vague instruction.

**3. Type/identifier consistency:**
- `MigrateBotPersistence()` (Task 1 Step 3) is referenced from `LoadBots` (Task 1 Step 4) — name matches.
- `CompactSavedProducts(string botKey)` and `CompactSavedServices(string botKey)` (Task 1 Steps 1, 2) called by `MigrateBotPersistence` (Task 1 Step 3) — signatures match.
- Local variable names (`name`, `card`) are consistent across Task 2's products and services blocks.
- The `botKey` parameter name in M1 matches the `botKey + "ProductsNumber"` etc. concatenation pattern used inside both helpers.

**4. No-test note:** Unity project has no automated test infrastructure. Verification is manual Play-mode confirmation per spec. Steps 6 in each task encode the manual scenarios.
