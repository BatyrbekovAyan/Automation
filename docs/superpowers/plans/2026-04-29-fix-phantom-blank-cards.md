# Fix Phantom Blank Product/Service Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop newly-added Product/Service cards from persisting as phantom blank entries when the user dismisses the edit sheet without pressing Done, and harden `Manager.SaveSettings` against a related count drift.

**Architecture:** Fix A — `ItemEditSheet` gains an `isNewlyAdded` flag tracked through `Show` → `Commit` / `MaybeHide`; on non-Done dismiss the bound card is routed through the existing `OnProductDeleted` / `OnServiceDeleted` events, which already have full destroy-and-relayout handlers in `BotSettings`. Fix C — `Manager.SaveSettings` swaps `childCount` for a count of cards with non-empty `Name`, matching the predicate that already gates the per-slot key writes. No PlayerPrefs schema change.

**Tech Stack:** Unity 6000.3.9f1, C#, TextMeshPro, DOTween. Manual verification in Unity Play mode (no automated tests in this project).

**Spec:** [docs/superpowers/specs/2026-04-29-fix-phantom-blank-cards-design.md](../specs/2026-04-29-fix-phantom-blank-cards-design.md)

---

## File Structure

**Modify only:**

- `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` — add `private bool isNewlyAdded;`, extend both `Show` overloads with an optional `bool isNewlyAdded = false` parameter, clear the flag in `Commit()`, branch in `MaybeHide()` to route through the existing delete events when the flag is set.
- `Assets/Scripts/Main/BotSettings.cs` — `AddProduct()` and `AddService()` change the trailing `Show(card)` call to `Show(card, isNewlyAdded: true)`. Two character-level edits.
- `Assets/Scripts/Main/Manager.cs` — replace `childCount` in two `PlayerPrefs.SetInt(... "ProductsNumber"/"ServicesNumber" ...)` calls with calls to two new private static helpers (`CountNonEmptyProductCards`, `CountNonEmptyServiceCards`).

**Do NOT modify:**

- `Assets/Scripts/Main/BotSettings/ProductCardView.cs` / `ServiceCardView.cs` — no API change.
- Any prefab.
- Any other file.

---

## Task 1: Fix A — destroy newly-added card on non-Done dismiss

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`
- Modify: `Assets/Scripts/Main/BotSettings.cs`

### Step 1 — Add `isNewlyAdded` field in ItemEditSheet

In `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`, the file currently declares `private ProductCardView boundProduct;` and `private ServiceCardView boundService;` together near the other instance fields (around line 149). Add a third field directly under them.

Find:

```csharp
        private ProductCardView boundProduct;
        private ServiceCardView boundService;
```

Replace with:

```csharp
        private ProductCardView boundProduct;
        private ServiceCardView boundService;
        // True when the currently-bound card was opened via AddProduct/AddService
        // (i.e., it has never been committed). Cleared on Commit() and consumed by
        // MaybeHide() to auto-discard the card if the user dismisses the sheet
        // without pressing Done. Default-false Show overload preserves the
        // existing tap-to-edit-existing-card behavior.
        private bool isNewlyAdded;
```

- [ ] **Step 2 — Extend the `Show(ProductCardView card)` overload**

In the same file, find the existing `Show(ProductCardView card)` method (around line 376):

```csharp
        public void Show(ProductCardView card)
        {
            boundProduct = card;
            boundService = null;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }
```

Replace with:

```csharp
        public void Show(ProductCardView card, bool isNewlyAdded = false)
        {
            boundProduct = card;
            boundService = null;
            this.isNewlyAdded = isNewlyAdded;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }
```

- [ ] **Step 3 — Extend the `Show(ServiceCardView card)` overload**

In the same file, find the existing `Show(ServiceCardView card)` method (around line 384):

```csharp
        public void Show(ServiceCardView card)
        {
            boundService = card;
            boundProduct = null;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }
```

Replace with:

```csharp
        public void Show(ServiceCardView card, bool isNewlyAdded = false)
        {
            boundService = card;
            boundProduct = null;
            this.isNewlyAdded = isNewlyAdded;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }
```

- [ ] **Step 4 — Clear `isNewlyAdded` in `Commit()`**

In the same file, the current `Commit()` method (around line 489) starts with `var name = nameField.Value;`. Insert a single line at the top of the method body that clears the flag, since a committed card is no longer "newly added uncommitted".

Find:

```csharp
        private void Commit()
        {
            var name = nameField.Value;
            var price = priceField.Value;
            var desc = descField.Value;
```

Replace with:

```csharp
        private void Commit()
        {
            isNewlyAdded = false;
            var name = nameField.Value;
            var price = priceField.Value;
            var desc = descField.Value;
```

- [ ] **Step 5 — Modify `MaybeHide()` to route through the delete events**

In the same file, the current `MaybeHide()` method (around line 549) reads:

```csharp
        private void MaybeHide()
        {
            if (kbDismissingAtScrimPress) return;
            Hide();
        }
```

Replace with:

```csharp
        private void MaybeHide()
        {
            if (kbDismissingAtScrimPress) return;
            if (isNewlyAdded)
            {
                // Capture before Hide(): Hide() nulls boundProduct/boundService
                // as part of its existing teardown, so we must snapshot the
                // references before invoking it.
                var product = boundProduct;
                var service = boundService;
                Hide();
                if (product != null) OnProductDeleted?.Invoke(product);
                if (service != null) OnServiceDeleted?.Invoke(service);
                return;
            }
            Hide();
        }
```

The `OnProductDeleted` / `OnServiceDeleted` events are already wired in `BotSettings.WireProductsAndServices` to `DeleteProductCard` / `DeleteServiceCard`, which handle `SetActive(false)` + `Destroy` + `RebuildTabLayout` + `EnableSave`. No new code in `BotSettings` for the destroy plumbing.

- [ ] **Step 6 — Update `BotSettings.AddProduct` to flag newly-added**

In `Assets/Scripts/Main/BotSettings.cs`, find the last line of `AddProduct()` (around line 507):

```csharp
        if (card != null && productEditSheet != null) productEditSheet.Show(card);
```

Replace with:

```csharp
        if (card != null && productEditSheet != null) productEditSheet.Show(card, isNewlyAdded: true);
```

- [ ] **Step 7 — Update `BotSettings.AddService` to flag newly-added**

In the same file, find the last line of `AddService()` (around line 535):

```csharp
        if (card != null && serviceEditSheet != null) serviceEditSheet.Show(card);
```

Replace with:

```csharp
        if (card != null && serviceEditSheet != null) serviceEditSheet.Show(card, isNewlyAdded: true);
```

- [ ] **Step 8 — Verify compile**

Switch to Unity Editor. Wait for auto-recompile. Watch the Console window. Expected: no compile errors. The `.claude/hooks/validate-cs.sh` hook also runs after each Edit and should pass.

Note specifically that the existing `BotSettings.cs:466,472` lines (`card.OnEditRequested += c => productEditSheet.Show(c);` and the service equivalent) compile unchanged because the new `isNewlyAdded` parameter is optional and defaults to `false`.

- [ ] **Step 9 — Manual verification in Play mode (Fix A scenarios)**

Press Play in Unity. Run the following scenarios from the spec. Each should match the expected outcome.

1. **Newly added + tap-outside-immediately** — Bot Settings → Products → tap **+ Add** → without typing, tap outside the sheet (in the dim scrim area) → Expected: card disappears from list immediately, sheet slides off.
2. **Newly added + tap-outside-after-typing** — tap **+ Add** → type "Tea" in name → tap outside without Done → Expected: card disappears (typed text is discarded — strict cancel).
3. **Newly added + Done** — tap **+ Add** → press Done immediately without typing → Expected: card stays with `New Product` / `0` / *(empty)* (placeholders kick in — same as before this fix).
4. **Existing card re-edit + tap-outside-unchanged** — tap an existing populated card → tap outside without typing → Expected: card preserved with its original values.
5. **Existing card re-edit + tap-outside-after-typing** — tap an existing card → modify name → tap outside without Done → Expected: card preserved with the ORIGINAL values; typed changes discarded.
6. **Newly added + Done + re-open + tap-outside** — tap **+ Add** → Done with no typing → tap the new card to re-open it → tap outside → Expected: card preserved (the `isNewlyAdded` flag was cleared by the first Done, so the dismiss behaves like an existing-card dismiss).
7. **Repeat 1–6 on Services tab.**
8. **iOS keyboard scrim-reroute regression** — In a real iOS build (or in Editor with the existing keyboard heuristic exercised), confirm a gesture that triggers `kbDismissingAtScrimPress = true` (touching inside the sheet during keyboard dismissal, which iOS may re-target to the scrim) does NOT destroy the card. The new logic checks `kbDismissingAtScrimPress` before the new `isNewlyAdded` branch, so this is structural — but worth a sanity check.

If any scenario fails, do NOT proceed — diagnose first.

- [ ] **Step 10 — Commit (DEFERRED to user)**

Per the in-session decision, commits are deferred. Leave changes uncommitted in the working tree alongside the existing branch WIP. The user will commit when their feature branch is ready.

---

## Task 2: Fix C — defensive non-empty count in SaveSettings

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs`

- [ ] **Step 1 — Add `CountNonEmptyProductCards` helper**

Open `Assets/Scripts/Main/Manager.cs`. Find a location near the other product/service-related helpers, or near `SaveSettings` itself. The exact placement is at the implementer's discretion — anywhere in the `Manager` class body, in a private-static helper region, is fine. The two helpers below should sit together.

Add this method:

```csharp
        // Counts only cards whose Name is non-empty. Mirrors the predicate at
        // line ~388 (`!card.Name.Equals("")`) that gates whether per-slot
        // PlayerPrefs keys are written. Decoupling the saved count from
        // ProductsParent.transform.childCount prevents the count drift that
        // would otherwise let an in-list blank card poison save→reload (the
        // skipped slot would hydrate as an empty card on next LoadBots).
        private static int CountNonEmptyProductCards(Transform parent)
        {
            if (parent == null) return 0;
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var card = parent.GetChild(i).GetComponent<ProductCardView>();
                if (card != null && !string.IsNullOrEmpty(card.Name)) count++;
            }
            return count;
        }
```

- [ ] **Step 2 — Add `CountNonEmptyServiceCards` helper**

Add this method directly below `CountNonEmptyProductCards`:

```csharp
        // Service-side mirror of CountNonEmptyProductCards. Two helpers
        // (rather than a generic) because ProductCardView and ServiceCardView
        // do not share a common base/interface exposing Name; introducing one
        // would be a larger refactor unrelated to this fix.
        private static int CountNonEmptyServiceCards(Transform parent)
        {
            if (parent == null) return 0;
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var card = parent.GetChild(i).GetComponent<ServiceCardView>();
                if (card != null && !string.IsNullOrEmpty(card.Name)) count++;
            }
            return count;
        }
```

- [ ] **Step 3 — Replace the products count write at `Manager.cs:418`**

In `Manager.SaveSettings`, find the existing line (around line 418):

```csharp
        PlayerPrefs.SetInt(openBot.name + "ProductsNumber", openBotSettings.ProductsParent.transform.childCount);
```

Replace with:

```csharp
        PlayerPrefs.SetInt(openBot.name + "ProductsNumber", CountNonEmptyProductCards(openBotSettings.ProductsParent));
```

Note: `openBotSettings.ProductsParent` is a `RectTransform` (which `is-a` `Transform`), so it implicitly converts and the helper signature `Transform parent` accepts it without a cast.

- [ ] **Step 4 — Replace the services count write at `Manager.cs:456`**

Find the existing line (around line 456):

```csharp
        PlayerPrefs.SetInt(openBot.name + "ServicesNumber", openBotSettings.ServicesParent.transform.childCount);
```

Replace with:

```csharp
        PlayerPrefs.SetInt(openBot.name + "ServicesNumber", CountNonEmptyServiceCards(openBotSettings.ServicesParent));
```

- [ ] **Step 5 — Verify compile**

Switch to Unity Editor. Wait for auto-recompile. Watch the Console window. Expected: no compile errors. The `validate-cs.sh` hook should pass on each Edit.

- [ ] **Step 6 — Manual verification in Play mode (Fix C scenarios)**

Press Play in Unity. Run these scenarios from the spec:

9. **Save→Reload roundtrip with no phantom cards (baseline)** — Add Product, Done with values "Tea / 500 / Loose-leaf" → tap the bot's main Save button → exit and re-enter Bot Settings (or restart Play mode) → Expected: only the Tea card visible. (Confirms the helpers don't undercount real cards.)
10. **Save→Reload roundtrip with a manually-cleared card** — Add Product, Done with placeholders → re-open the new card → manually clear all three input fields → press Done → tap the bot's main Save button → exit and re-enter Bot Settings → Expected: card with manually-cleared empty Name does NOT reappear. (Confirms Fix C eliminates the count drift.)

Also re-run the still-relevant Fix A scenarios from Task 1 to confirm no regression:
- Scenario 3 (newly added + Done immediately) — placeholder-fallback card should still save & reload correctly.
- Scenario 4 (existing card re-edit + tap-outside-unchanged) — existing populated cards with real values should still survive save & reload.

If any scenario fails, do NOT proceed — diagnose first.

- [ ] **Step 7 — Commit (DEFERRED to user)**

Same as Task 1 Step 10 — commits are deferred to the user.

---

## Self-Review

**1. Spec coverage:**
- Spec §"Fix A — destroy newly-added card on non-Done dismiss" → Task 1, all steps.
  - Field declaration → Task 1 Step 1.
  - Show overloads → Task 1 Steps 2–3.
  - `Commit()` flag clear → Task 1 Step 4.
  - `MaybeHide()` rewrite with `kbDismissingAtScrimPress` short-circuit preserved → Task 1 Step 5.
  - `BotSettings` call sites → Task 1 Steps 6–7.
- Spec §"Fix C — defensive non-empty count in SaveSettings" → Task 2 Steps 1–4.
  - `CountNonEmptyProductCards` → Step 1.
  - `CountNonEmptyServiceCards` → Step 2.
  - Products count write replacement → Step 3.
  - Services count write replacement → Step 4.
- Spec §"Verification" scenarios 1–8 → Task 1 Step 9.
- Spec §"Verification" scenarios 9–10 → Task 2 Step 6.
- Spec §"Out of scope" — none of the excluded items appears as a task. ✓
- Spec §"Constraints / properties preserved" — plan does not modify ProductCardView / ServiceCardView, no PlayerPrefs schema change, no new public events. ✓

**2. Placeholder scan:** No "TBD" / "TODO" / "fill in" anywhere. All code blocks are concrete. All commands have expected outcomes. The two "DEFERRED to user" commit steps are explicit, not placeholders.

**3. Type/identifier consistency:** `isNewlyAdded` (PascalCase property would be wrong here — it's a private field, so lowerCamelCase is correct and matches surrounding convention in `ItemEditSheet`). `CountNonEmptyProductCards` / `CountNonEmptyServiceCards` are referenced in Task 2 Step 3/4 exactly as defined in Step 1/2. The `Show(card, isNewlyAdded: true)` named-argument syntax in Task 1 Steps 6–7 matches the parameter name introduced in Steps 2–3.

**4. No-test note:** This Unity project has no automated test infrastructure. Verification is manual Play-mode confirmation per the spec. The standard TDD step pattern from the writing-plans template has been adapted accordingly — each task ends in a manual scenario walkthrough rather than `pytest`/`go test` invocation.
