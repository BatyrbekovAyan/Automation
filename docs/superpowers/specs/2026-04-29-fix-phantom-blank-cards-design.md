# Fix phantom blank Product/Service cards

## Problem

After the recently-implemented "blank new Product/Service items" feature ([2026-04-29-bot-settings-blank-new-item-design.md](2026-04-29-bot-settings-blank-new-item-design.md)), a reachable user path produces phantom blank cards in a bot's Products / Services list:

1. Open Bot Settings → Products → tap **+ Add**.
2. The edit sheet slides up. Instead of pressing Done, tap outside the sheet (scrim tap) to dismiss.
3. The blank card remains in `ProductsParent` with `Name = "" / Price = "" / Description = ""`.
4. Save the bot. Reload (e.g., open Settings again). The card reappears as a blank entry.

Identical bug for Services.

### Root causes

Two independent issues compose into the visible bug:

**1. `ItemEditSheet.MaybeHide` does not destroy a newly-added uncommitted card.**
`MaybeHide` (the scrim-tap dismiss path) calls `Hide()`, which only nulls `boundProduct`/`boundService` and slides the sheet off — the card itself survives in `ProductsParent`. There is no signal in `ItemEditSheet` distinguishing "user opened an existing card and dismissed" from "user just tapped + Add and dismissed without ever committing".

**2. `Manager.SaveSettings` count drift.**
`Manager.cs:388` skips `PlayerPrefs` key writes for cards where `Name.Equals("")`, but `Manager.cs:418` writes `ProductsNumber = ProductsParent.transform.childCount` regardless. On `LoadBots`, the loop iterates `0 .. ProductsNumber-1` and `PlayerPrefs.GetString(... "Product"+p, "")` returns the default `""` for the skipped slot, recreating the phantom card. Identical pattern at `Manager.cs:425/456` for services.

This was latent before the new-item-blank feature: pre-feature, freshly-added cards carried prefab default text (`"Название"` / `"0"` / `"Описание"`), so `!"Название".Equals("")` was true, the slot was written, and the count drift never manifested. The new-item-blank feature exposed the latent bug.

## Solution (A + C)

Two surgical fixes:

- **Fix A:** Destroy the newly-added card if the user dismisses without pressing Done.
- **Fix C:** Make `Manager.SaveSettings` write a non-empty card count, decoupling it from `childCount`.

A handles the user-visible UX. C is a defensive backstop against any future code path that could leave a blank card in the list (drag-drop, undo, programmatic insertion, etc.) — even if A misses one, C prevents poisoning of saved data.

## Design

### Fix A — destroy newly-added card on non-Done dismiss

**`Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`**

Add a private field tracking whether the currently-bound card was opened as a freshly-added blank or as an existing card:

```csharp
private bool isNewlyAdded;
```

Extend the two `Show` overloads to accept an optional flag, defaulting to `false` so existing call sites (re-editing an existing card) need no change:

```csharp
public void Show(ProductCardView card, bool isNewlyAdded = false)
{
    boundProduct = card;
    boundService = null;
    this.isNewlyAdded = isNewlyAdded;
    BindFields(card.Name, card.Price, card.Description);
    SlideIn();
}

public void Show(ServiceCardView card, bool isNewlyAdded = false)
{
    boundService = card;
    boundProduct = null;
    this.isNewlyAdded = isNewlyAdded;
    BindFields(card.Name, card.Price, card.Description);
    SlideIn();
}
```

`Commit()` clears the flag at the top — once the user commits, the card is no longer "newly added uncommitted":

```csharp
private void Commit()
{
    isNewlyAdded = false;
    // ... existing logic unchanged ...
}
```

`MaybeHide()` — the scrim-tap dismiss path — checks the flag. If set and a card is bound, capture the card reference, call `Hide()` (which nulls bindings as part of its existing teardown), then route through the existing `OnProductDeleted` / `OnServiceDeleted` events:

```csharp
private void MaybeHide()
{
    if (kbDismissingAtScrimPress) return;
    if (isNewlyAdded)
    {
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

The capture-before-Hide ordering matters: `Hide()` nulls `boundProduct`/`boundService` as part of its existing teardown, so we must snapshot the references first.

Reusing `OnProductDeleted` / `OnServiceDeleted` means the existing `BotSettings.DeleteProductCard` / `DeleteServiceCard` handlers do all the cleanup (`SetActive(false)` → `Destroy` → `RebuildTabLayout` → `EnableSave`). No new code in `BotSettings` for the destroy path.

**`Assets/Scripts/Main/BotSettings.cs`**

Two call sites change. `AddProduct()` ends with:

```csharp
if (card != null && productEditSheet != null) productEditSheet.Show(card, isNewlyAdded: true);
```

`AddService()` ends with:

```csharp
if (card != null && serviceEditSheet != null) serviceEditSheet.Show(card, isNewlyAdded: true);
```

The two existing re-edit call sites in `RegisterProductCard` / `RegisterServiceCard` (`BotSettings.cs:466,472`) keep their default-argument `Show(c)` form, which means `isNewlyAdded = false` → existing cards are never auto-destroyed.

### Fix C — defensive non-empty count in SaveSettings

**`Assets/Scripts/Main/Manager.cs`**

Replace the two `childCount` writes with a count of cards whose `Name` is non-empty, matching the same predicate at lines 388/425 that gates whether the keys are written.

At line 418 (products), replace:

```csharp
PlayerPrefs.SetInt(openBot.name + "ProductsNumber", openBotSettings.ProductsParent.transform.childCount);
```

with:

```csharp
PlayerPrefs.SetInt(openBot.name + "ProductsNumber", CountNonEmptyProductCards(openBotSettings.ProductsParent));
```

At line 456 (services), the parallel change:

```csharp
PlayerPrefs.SetInt(openBot.name + "ServicesNumber", CountNonEmptyServiceCards(openBotSettings.ServicesParent));
```

Add two private helper methods on `Manager` (placed near the other product/service-related helpers, exact location at the implementer's discretion):

```csharp
private static int CountNonEmptyProductCards(Transform parent)
{
    int count = 0;
    for (int i = 0; i < parent.childCount; i++)
    {
        var card = parent.GetChild(i).GetComponent<ProductCardView>();
        if (card != null && !string.IsNullOrEmpty(card.Name)) count++;
    }
    return count;
}

private static int CountNonEmptyServiceCards(Transform parent)
{
    int count = 0;
    for (int i = 0; i < parent.childCount; i++)
    {
        var card = parent.GetChild(i).GetComponent<ServiceCardView>();
        if (card != null && !string.IsNullOrEmpty(card.Name)) count++;
    }
    return count;
}
```

### Why two helpers (not one generic)

`ProductCardView` and `ServiceCardView` do not share a common interface or base class with a `Name` property — they're parallel siblings. A generic helper would require either (a) introducing a shared interface (out of scope, larger refactor), or (b) reflection (over-engineered for two call sites). Two near-identical helpers is the YAGNI choice.

### Why `IsNullOrEmpty` (not `IsNullOrWhiteSpace`)

`Manager.cs:388` uses `!product.Name.Equals("")`, which matches `IsNullOrEmpty` semantics. Using `IsNullOrWhiteSpace` in the count would be inconsistent with the predicate that decides whether the keys are written — could produce a count higher than the actual key writes if a card had whitespace-only `Name`. Stay aligned with the existing predicate.

(The new-item-blank feature uses `IsNullOrWhiteSpace` in `ItemEditSheet.Commit` for the placeholder fallback. That's at the input layer — by the time `SaveSettings` runs, any committed card has either user input or a placeholder, neither of which is whitespace-only. So the divergence between the two layers is fine in practice.)

## Verification

Manual checks in Unity Play mode (no automated test infrastructure exists):

### Fix A scenarios

1. **Newly added + tap-outside-immediately** — Add Product → tap outside the sheet without typing → expected: card disappears from list.
2. **Newly added + tap-outside-after-typing** — Add Product → type "Tea" in name → tap outside → expected: card disappears (strict cancel; user must press Done to keep).
3. **Newly added + Done** — Add Product → press Done immediately → expected: card stays with placeholder values (`New Product / 0 / empty`). Same as before this fix.
4. **Existing card re-edit + tap-outside-unchanged** — Tap an existing populated card → tap outside without typing → expected: card preserved with original values. Same as before this fix.
5. **Existing card re-edit + tap-outside-after-typing** — Tap an existing card → modify name → tap outside without Done → expected: card preserved with ORIGINAL values (typed changes discarded). Same as before this fix.
6. **Newly added + Done + re-open + tap-outside** — Add Product → Done → tap the new card → tap outside without typing → expected: card preserved (the `isNewlyAdded` flag was cleared by the first Done).
7. **Repeat 1–6 on Services tab.**
8. **iOS keyboard scrim-reroute regression** — Verify the existing keyboard-dismiss-during-touch heuristic still works: with the keyboard up inside the sheet, performing a gesture that triggers `kbDismissingAtScrimPress` must NOT destroy the card. (The new logic checks `kbDismissingAtScrimPress` first, so this is structural — but worth confirming.)

### Fix C scenarios

9. **Save→Reload roundtrip with no phantom cards** — Add Product, Done with values "Tea / 500 / Loose-leaf" → Save bot → close & reopen Settings → expected: only the Tea card visible. (Baseline.)
10. **Save→Reload roundtrip with a manually-cleared card** — Add Product, Done with placeholders → re-open the card, manually clear all three fields, press Done → Save bot → close & reopen Settings → expected: card with the manually-cleared empty Name does NOT reappear. (Confirms Fix C.)

Note: scenario 10 only triggers C; scenarios 1–8 only trigger A. There's no realistic single scenario that exercises both fixes at once because A prevents the entry from existing in the list, and C is a backstop for paths that bypass A.

## Out of scope

- **One-shot migration of existing PlayerPrefs phantom data.** Forward-only fix. If a user has phantom blank cards saved from a prior session, they can manually delete them via the existing delete-card UI. YAGNI for a one-shot migration.
- **Auto-save-on-dismiss (Option B from the original spawn task).** Rejected during brainstorming — strict cancel-on-dismiss is the chosen UX.
- **`Name.Trim()` discarded-result bug at `Manager.cs:387/425`.** Real bug, but a separate concern. Keeping the patch surface tight.
- **`LoadBots` skipping empty saved entries.** Defensive but redundant given Fix C. YAGNI.
- **Refactoring `Manager.SaveSettings` more broadly.** Manager.cs is already a god-object; we touch only the two count-write lines plus the two helper methods. Don't expand the change.

## Constraints / properties preserved

- No new fields on `ProductCardView` / `ServiceCardView`.
- No new public events on `ItemEditSheet`.
- No PlayerPrefs schema change. The data format (`{botName}ProductsNumber` + `{botName}Product{i}` / `Price{i}` / `Description{i}`) is unchanged. Only the count semantics shift.
- No animation changes. The destroy path on cancel reuses existing `DeleteProductCard`'s `SetActive(false)` + `Destroy` + `RebuildTabLayout` sequence.
- No interaction with the Manager.Cs save-blocked event flow (`OnAnyCommitted` etc.) — those continue to fire from `Commit()` only.
