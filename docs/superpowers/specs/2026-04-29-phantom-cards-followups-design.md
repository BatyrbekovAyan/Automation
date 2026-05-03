# Phantom Blank Cards — Follow-up Cleanups (M1 migration + M2 trim fix)

## Context

Two cleanups identified during the final review of the phantom-blank-cards bugfix ([2026-04-29-fix-phantom-blank-cards-design.md](2026-04-29-fix-phantom-blank-cards-design.md)). Both touch only `Manager.cs`.

- **M1** — One-shot compacting migration for pre-existing PlayerPrefs phantom data and orphan keys.
- **M2** — Fix the `card.Name.Trim()` discarded-result bug in `SaveSettings`.

## M1 — Compacting migration

### Why it matters

The earlier final review hypothesized that Fix C ("count of non-empty cards in `SaveSettings`") would self-heal pre-existing phantom data over time. On closer inspection that property is **incomplete**: saving compacts the saved-count but does not compact the per-slot key indices, so a phantom in the middle of the list (e.g., `Product0=Tea`, `Product1=ABSENT`, `Product2=Coffee`) survives a save→reload cycle even after Fix C. Trace:

- Save loop iterates live `childCount = 3`. Writes `Product0=Tea`, skips `Product1` (blank Name), writes `Product2=Coffee`. Cleanup at `Manager.cs:431` only triggers when saved-count > childCount, which is false here, so no key deletion. With Fix C, `ProductsNumber := 2` (count of non-empty).
- Reload runs `LoadBots` iterating `p=0..1`. `p=0`: reads `Product0=Tea` ✓. `p=1`: reads `PlayerPrefs.GetString("Product1", "")` → returns the default `""` because `Product1` was never written → instantiates a blank phantom card.

So pre-existing data with mid-list phantoms persists across reloads under Fix C alone. M1 closes this by compacting the saved keys at app launch.

### What it does

A new private static method `MigrateBotPersistence()` called from the very top of `LoadBots()` (before any per-bot instantiation), iterating the same `for (int i = 0; i < id; i++) if (HasKey("Bot"+i+"Name"))` pattern that `LoadBots` itself uses.

For each saved bot, the migration runs the same compacting routine for products and for services:

1. Read `ProductsNumber` from PlayerPrefs (default 0 if absent).
2. Walk indices `0..ProductsNumber-1`. Read each slot's three keys (`Product[i]`, `Product[i]Price`, `Product[i]Description`). Collect entries where `Name` (the first key) is non-empty per `string.IsNullOrEmpty`.
3. If the collected count equals `ProductsNumber`, no migration needed for that bot's products. Skip to services.
4. Otherwise, write the collected entries back at compacted indices `0..N-1` (overwriting the existing keys at those slots).
5. Delete orphan keys at indices `N..ProductsNumber-1` (the trailing range now beyond the new count).
6. Write `ProductsNumber := N` (the new compacted count).
7. Repeat steps 1–6 for services using `ServicesNumber` / `Service[i]` keys.

### Why this design

- **Run at app launch (top of `LoadBots`), not at save time.** Save-side compaction would require restructuring the existing save loop (which iterates live UI children, not saved keys). Launch-side migration operates on PlayerPrefs only — pure data, no UI dependency, no entanglement with the save loop's `childCount` semantics.
- **Inside `LoadBots` rather than a separate `Awake` migration.** Avoids a second walk over `0..id`. The migration runs once per app launch, before LoadBots reads the slots, so the in-memory bot list reflects the migrated data without a second pass.
- **No "migration done" flag.** The migration is idempotent (compacting clean data is a no-op) and cheap (~3 PlayerPrefs reads per slot, no GetComponent calls). Skipping flag bookkeeping is YAGNI for a one-shot data fix.
- **Use `string.IsNullOrEmpty`, not `IsNullOrWhiteSpace`.** Matches the predicate at `Manager.cs:422,460` (`!Name.Equals("")`) for consistency with the per-slot save logic. A pre-existing whitespace-only saved Name is theoretically possible if some prior save path didn't trim — it would be preserved by the migration. Acceptable: M2 (the Trim fix) lands in the same change and going forward whitespace-only Names won't be saved.

### Files / methods

- `Assets/Scripts/Main/Manager.cs` adds three private static methods near the existing `CountNonEmptyProductCards` / `CountNonEmptyServiceCards` helpers (which sit just above `SaveSettings`):
  - `MigrateBotPersistence()` — orchestrator; iterates bots, calls the per-bot helpers.
  - `CompactSavedProducts(string botKey)` — products-side compaction for one bot.
  - `CompactSavedServices(string botKey)` — services-side compaction for one bot.

  The two compaction helpers share their structure but differ in the keys they touch (`Product[i]` / `Product[i]Price` / `Product[i]Description` vs. the service equivalents) and in their count-key name (`ProductsNumber` vs. `ServicesNumber`). Two helpers (rather than one generic) for the same reason `CountNonEmptyXxxCards` chose two helpers — there's no shared schema across products and services beyond the ad-hoc string-key naming convention.

- `LoadBots()` (line ~243): one-line addition near the very top — `MigrateBotPersistence();` before the existing `yield return new WaitForEndOfFrame();`. The migration is synchronous (PlayerPrefs is synchronous) so no coroutine concerns.

### Edge cases handled

| Scenario | Behavior |
|---|---|
| Bot has no products (`ProductsNumber` absent or 0) | Migration reads 0, no work. |
| Bot has all-blank products | Migration finds 0 non-empty → writes `ProductsNumber := 0`, deletes all old slot keys. |
| Bot already clean | Migration finds N non-empty out of N saved → no writes. Idempotent. |
| Mid-list phantom (the target case) | Compacted; phantom slot disappears on next reload. |
| End-of-list phantom (already handled by Fix C's existing cleanup) | Same migration logic still runs; no harm. |
| `ProductsNumber` set but slot keys absent (data corruption) | Treated as blank slot → skipped → orphan keys deleted (none exist) → count shrunk. Self-corrects. |

## M2 — `Name.Trim()` discarded-result fix

### Why it matters

`Manager.cs:421` and `:459` call `card.Name.Trim()` and discard the result. The original author's intent was almost certainly to trim whitespace before saving — strings are immutable in C#, so `Trim()` returns a new string and does not mutate the receiver. Today the line is functionally a no-op.

This is a pre-existing bug, called out in the original phantom-blank-cards spec as out of scope. Now in scope as a paired follow-up.

### Fix

At `Manager.cs:418-428` (the products save loop body), replace:

```csharp
        product.GetComponent<ProductCardView>().Name.Trim();
        if (!product.GetComponent<ProductCardView>().Name.Equals(""))
        {
            PlayerPrefs.SetString(openBot.name + "Product" + i, product.GetComponent<ProductCardView>().Name);
            PlayerPrefs.SetString(openBot.name + "Product" + i + "Price", product.GetComponent<ProductCardView>().Price);
            PlayerPrefs.SetString(openBot.name + "Product" + i + "Description", product.GetComponent<ProductCardView>().Description);
        }
```

with:

```csharp
        var card = product.GetComponent<ProductCardView>();
        var name = card.Name?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(name))
        {
            PlayerPrefs.SetString(openBot.name + "Product" + i, name);
            PlayerPrefs.SetString(openBot.name + "Product" + i + "Price", card.Price);
            PlayerPrefs.SetString(openBot.name + "Product" + i + "Description", card.Description);
        }
```

The local `card` is also a small DRY improvement — the original called `GetComponent<ProductCardView>()` four times in 8 lines. Identical replacement at the services save loop body (`Manager.cs:456-466` after the M1 helper insertion shifts line numbers).

### Why only Name is trimmed

`Price` and `Description` are not identifiers — they may legitimately contain leading/trailing whitespace (multi-line description with trailing newline; price formatted with leading currency symbol). Only `Name` is the "identity" field that determines whether the slot is empty/non-empty, so only `Name` is trimmed.

### Count-helper divergence (acknowledged, not fixed)

After M2, the per-slot save predicate (`!IsNullOrEmpty(name.Trim())`) is stricter than `CountNonEmptyProductCards` (`!IsNullOrEmpty(card.Name)`). A live card with `Name = "   "` would be counted by the helper but skipped by the save loop, producing a 1-off count drift.

This case is unreachable through normal flow:

- `BotSettings.AddProduct/AddService` set `card.Name = string.Empty` (not whitespace).
- `ItemEditSheet.Commit` uses `IsNullOrWhiteSpace` for the placeholder fallback, so a user typing `"   "` gets `"New Product"` instead.
- The only way `card.Name = "   "` could occur is if some currently-non-existent code path mutates it, or if pre-existing PlayerPrefs data contains whitespace-only saved names — and M1's migration runs at launch, removing such phantoms before they reach `LoadBots`.

So whitespace-only is unreachable in practice. A code comment will note the divergence so a future reader doesn't "fix" it spuriously by changing the helper to match.

## Out of scope

- Trimming `Price` / `Description` (intentionally preserved as-is — they're not identifiers).
- Updating `CountNonEmptyProductCards` / `CountNonEmptyServiceCards` to use `IsNullOrWhiteSpace` (would require changing the existing approved spec; the divergence is benign in practice).
- Rewriting the save loop's structure or extracting helpers beyond the local `card` variable.
- Adding a one-shot migration version flag for skipping subsequent runs (idempotent migration; flag is YAGNI).
- Migrating other PlayerPrefs schema concerns unrelated to product/service slot drift.
- Modifying `LoadBots`'s loop body (the per-bot instantiation logic) — the only change there is the one-line `MigrateBotPersistence();` call at the top.
- Re-doing prior approved specs.

## Verification

Manual checks in Unity Play mode (no automated tests):

### M1 migration scenarios

1. **Clean roundtrip** — Save a bot with 3 products, all named. Quit Play mode and re-enter. Expected: all 3 reappear correctly. (Confirms migration is a no-op on clean data.)
2. **Mid-list phantom self-heal** — In a fresh dev environment, manually inject a phantom: stop Play, programmatically (via the Editor's `PlayerPrefs.SetInt` and `SetString` from a scratch Editor script) set `Bot0ProductsNumber = 3`, `Bot0Product0 = "Tea"`, `Bot0Product2 = "Coffee"`, omit `Bot0Product1` keys. Enter Play mode. Expected: Bot 0 loads with 2 products (`Tea`, `Coffee`) — no blank phantom. After save→reload: still 2 products at compacted indices.
3. **All-blank slots self-heal** — Set `Bot0ProductsNumber = 2`, omit both `Bot0Product0` and `Bot0Product1` keys (e.g. they were skipped by a buggy save). Enter Play mode. Expected: Bot 0 loads with 0 products. PlayerPrefs `Bot0ProductsNumber` is now 0.
4. **End-of-list orphan cleanup** — Set `Bot0ProductsNumber = 2` but leave a stale `Bot0Product3 = "OldStaleData"` lying around. Enter Play mode. Expected: `Bot0Product3` is deleted by the migration's orphan-cleanup loop. (Edge case: the migration shouldn't go beyond the saved count, so this orphan would only be cleaned if it falls within the original `0..ProductsNumber-1` range. If it sits at index 3 with `ProductsNumber = 2`, the existing cleanup at `Manager.cs:431` handles it on the next save. Worth confirming.)
5. **Idempotence** — Run scenarios 1–4, then close and re-open the app a second time. Expected: no further changes (migration is no-op on already-clean data).
6. **Repeat 1–5 for services.**

### M2 trim scenarios

7. **Whitespace at edges** — In the live UI, type `"  Tea  "` into a new Product's name field, press Done. (Note: `ItemEditSheet.Commit` does NOT trim; the live `card.Name` will be `"  Tea  "`.) Save the bot. Reload. Expected: card displays `"Tea"` (the trimmed save value loaded back).
8. **Whitespace-only name** — In the live UI, type `"   "` into a new Product's name field. (Wait — `ItemEditSheet.Commit` uses `IsNullOrWhiteSpace`, so this path triggers the `New Product` placeholder. The whitespace-only card never enters the save flow. Confirms the divergence is unreachable through Commit. Skippable scenario.)
9. **Pre-trim regression** — Save a bot with a normally-named product (no leading/trailing whitespace). Reload. Expected: name unchanged. (Confirms M2 doesn't break the common case.)

If any scenario fails, do NOT proceed — diagnose first.

## Constraints / properties preserved

- No PlayerPrefs schema change. Same key names, same value semantics.
- No new public fields, methods, or events on any non-`Manager` type.
- No change to `LoadBots`'s loop body — only a single one-line orchestrator call at the top.
- No change to `BotSettings` / `ItemEditSheet` / card view classes.
- Idempotent: running migration on clean data has no observable effect.
- The `CountNonEmptyXxxCards` helpers from the prior fix are unchanged.
