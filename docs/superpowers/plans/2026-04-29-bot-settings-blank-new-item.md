# Blank New Product/Service Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Open the Add Product / Add Service edit sheet with blank input fields, and on Save fall back to placeholder values for empty Name/Price (Description stays empty).

**Architecture:** Two surgical edits, no new files. (1) `BotSettings.AddProduct` / `AddService` blank the freshly-instantiated card's three properties before opening `ItemEditSheet`, so the sheet binds empty strings into the inputs. (2) `ItemEditSheet.Commit` applies `IsNullOrWhiteSpace` fallbacks to placeholder constants when writing back to the bound card. Prefabs are NOT touched, so the Unity-Editor-time card preview is unaffected.

**Tech Stack:** Unity 6000.3.9f1, C#, TextMeshPro, DOTween. Manual verification in Unity Play mode (no automated tests in this project).

**Spec:** [docs/superpowers/specs/2026-04-29-bot-settings-blank-new-item-design.md](../specs/2026-04-29-bot-settings-blank-new-item-design.md)

---

## File Structure

**Modify only:**
- `Assets/Scripts/Main/BotSettings.cs` — `AddProduct()` (line 475) and `AddService()` (line 500) get a 3-line block clearing `card.Name/Price/Description` before the existing `Show(card)` call.
- `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` — add three private const placeholders, rewrite `Commit()` (line 483) to apply `IsNullOrWhiteSpace` fallbacks.

**Do NOT modify:**
- `Assets/Prefabs/Product.prefab`, `Assets/Prefabs/Service.prefab` — keep their existing `"Название" / "0" / "₸" / "Описание"` labels for the Editor preview.
- `ProductCardView.cs`, `ServiceCardView.cs` — their `Name`/`Price`/`Description` setters already handle null safely.

---

## Task 1: Blank the new card on Add

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings.cs:475-498` (`AddProduct`)
- Modify: `Assets/Scripts/Main/BotSettings.cs:500-519` (`AddService`)

- [ ] **Step 1: Edit `AddProduct` to clear the freshly-instantiated card**

In `Assets/Scripts/Main/BotSettings.cs`, locate `public void AddProduct()` (around line 475). After the `RegisterProductCard(card);` line and BEFORE the `var anim = go.GetComponent<Animation>();` line, insert the three-line clear block. The full updated method body:

```csharp
public void AddProduct()
{
    var go = Instantiate(ProductPrefab,
                         ProductPrefab.transform.position,
                         ProductPrefab.transform.rotation,
                         ProductsParent);

    var card = go.GetComponent<ProductCardView>();
    RegisterProductCard(card);

    // Blank the prefab's display defaults ("Название" / "0" / "Описание")
    // so ItemEditSheet.Show binds empty strings into the input fields —
    // the user shouldn't have to delete pre-typed text before entering theirs.
    if (card != null)
    {
        card.Name = string.Empty;
        card.Price = string.Empty;
        card.Description = string.Empty;
    }

    var anim = go.GetComponent<Animation>();
    if (anim != null) anim.Play();

    RebuildTabLayout(ProductsParent);
    // Append + auto-scroll so the newly added card is visible at the
    // bottom of the viewport once the user dismisses the edit sheet.
    ScrollTabToBottom(Product);

    Manager.Instance.EnableSave();

    // Open the edit sheet right away so the user can fill in the new
    // item's fields without an extra tap.
    if (card != null && productEditSheet != null) productEditSheet.Show(card);
}
```

- [ ] **Step 2: Edit `AddService` with the same clear block**

In the same file, locate `public void AddService()` (around line 500). Insert the same `card != null` clear block in the same position (after `RegisterServiceCard(card);`, before `var anim`). Full method:

```csharp
public void AddService()
{
    var go = Instantiate(ServicePrefab,
                         ServicePrefab.transform.position,
                         ServicePrefab.transform.rotation,
                         ServicesParent);

    var card = go.GetComponent<ServiceCardView>();
    RegisterServiceCard(card);

    if (card != null)
    {
        card.Name = string.Empty;
        card.Price = string.Empty;
        card.Description = string.Empty;
    }

    var anim = go.GetComponent<Animation>();
    if (anim != null) anim.Play();

    RebuildTabLayout(ServicesParent);
    ScrollTabToBottom(Service);

    Manager.Instance.EnableSave();

    if (card != null && serviceEditSheet != null) serviceEditSheet.Show(card);
}
```

- [ ] **Step 3: Verify the edit compiles**

Switch to Unity Editor, let it auto-recompile, watch the Console window. Expected: no compile errors. (`validate-cs.sh` hook also runs after the Edit tool — should pass.)

- [ ] **Step 4: Manual verification in Play mode (Add only — fallback comes in Task 2)**

In Unity Editor, press Play. Open Bot Settings → Products tab → tap **+ Add**. Expected: edit sheet slides up, all three input fields (Name, Price, Description) are **completely empty**, no Russian placeholder text. Repeat on Services tab. Expected: same behavior.

Note: at this point, pressing Done with all empty fields will produce a card with empty labels — that's expected; Task 2 wires the fallbacks. Just confirm the *opening* state is blank.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/BotSettings.cs
git commit -m "$(cat <<'EOF'
fix(bot-settings): blank Product/Service edit sheet on Add

Prefab display defaults ("Название" / "0" / "Описание") were leaking
into the edit sheet's input fields via card.Name/Price/Description,
forcing the user to delete pre-typed text. Clear the freshly
instantiated card so the sheet binds empty strings.

Placeholder fallbacks for empty Name/Price on Save come next in
ItemEditSheet.Commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Placeholder fallback on Commit

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` — add three constants near the top of the class, rewrite `Commit()` (line 483).

- [ ] **Step 1: Add placeholder constants**

In `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`, add three private constants. Put them right after the existing `selectRecencyFramesForBlurSkip` constant block (around line 127), before the `lastBlurredField` field declaration. The exact insertion:

```csharp
        // Placeholder values written into the bound card by Commit() when
        // the corresponding input field is empty/whitespace. Description has
        // no placeholder — it intentionally stays empty when not filled.
        private const string ProductNamePlaceholder = "New Product";
        private const string ServiceNamePlaceholder = "New Service";
        private const string PricePlaceholder = "0";
```

- [ ] **Step 2: Rewrite `Commit()` with placeholder fallbacks**

Replace the entire existing `Commit()` method (around line 483-499) with:

```csharp
        private void Commit()
        {
            var name = nameField.Value;
            var price = priceField.Value;
            var desc = descField.Value;

            if (boundProduct != null)
            {
                boundProduct.Name = string.IsNullOrWhiteSpace(name) ? ProductNamePlaceholder : name;
                boundProduct.Price = string.IsNullOrWhiteSpace(price) ? PricePlaceholder : price;
                boundProduct.Description = desc;
            }
            else if (boundService != null)
            {
                boundService.Name = string.IsNullOrWhiteSpace(name) ? ServiceNamePlaceholder : name;
                boundService.Price = string.IsNullOrWhiteSpace(price) ? PricePlaceholder : price;
                boundService.Description = desc;
            }
            OnAnyCommitted?.Invoke();
            Hide();
        }
```

Notes for the editor:
- `OnAnyCommitted?.Invoke();` and `Hide();` keep their original behavior.
- `Description` is written through unchanged so empty stays empty, per spec.
- Fallback runs in `Commit` (not in `Show/BindFields`) so an existing item the user deliberately clears and saves also gets the placeholder applied — consistent UX with the new-item path.

- [ ] **Step 3: Verify the edit compiles**

Switch to Unity Editor, let it auto-recompile, watch Console. Expected: no compile errors.

- [ ] **Step 4: Manual verification in Play mode — full flow**

Press Play in Unity. Run through these checks (matches the spec's verification list):

1. Bot Settings → Products → **+ Add** → confirm Name / Price / Description inputs are empty. Press **Done** without typing. Expected card display: `New Product` / `0` / *(empty description)*.
2. Add another product, type only a Name (e.g. `"Coffee"`). Press Done. Expected card: `Coffee` / `0` / *(empty)*.
3. Add a third product, type Name `"Tea"`, Price `"500"`, Description `"Loose-leaf"`. Press Done. Expected card: `Tea` / `500` / `Loose-leaf`.
4. Repeat steps 1–3 on Services tab. Expected: empty-name fallback is `New Service` (NOT `New Product`).
5. Tap an existing product card to re-open the edit sheet. Manually clear all three fields, press Done. Expected: card now shows `New Product` / `0` / *(empty)* — placeholders apply on commit regardless of whether the item is new or existing.
6. Tap an existing product card. Edit only the description, leave Name and Price unchanged. Press Done. Expected: Name and Price are preserved exactly (NOT overwritten by placeholders, because they were non-empty going in).

If any of the six checks fails, do NOT commit — diagnose first.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/ItemEditSheet.cs
git commit -m "$(cat <<'EOF'
feat(bot-settings): placeholder fallbacks for empty Name/Price on save

When the user saves a Product/Service edit with an empty Name or
Price field, fall back to placeholder values:
  - Product Name empty → "New Product"
  - Service Name empty → "New Service"
  - Price empty        → "0"
  - Description empty  → stays empty (per spec)

Fallback runs in Commit (not in Show/BindFields) so it applies to
both new items and existing items the user deliberately clears.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**
- Spec §"Desired behavior" point 1 (blank on Add) → Task 1, Steps 1–2.
- Spec §"Desired behavior" point 2.a (Name fallback) → Task 2, Step 2 (`ProductNamePlaceholder` / `ServiceNamePlaceholder`).
- Spec §"Desired behavior" point 2.b (Price fallback) → Task 2, Step 2 (`PricePlaceholder`).
- Spec §"Desired behavior" point 2.c (Description stays empty) → Task 2, Step 2 (`boundProduct.Description = desc` with no fallback).
- Spec §"Why placeholders apply *on commit*" — covered by manual verification step 5–6.
- Spec §"Out of scope" — no related tasks created (correct).

**2. Placeholder scan:** No "TBD" / "TODO" / "fill in" in any task. All code blocks are complete. All commit messages are written out.

**3. Type/identifier consistency:** `ProductNamePlaceholder`, `ServiceNamePlaceholder`, `PricePlaceholder` are referenced in Task 2 Step 2 exactly as defined in Task 2 Step 1. `card.Name / card.Price / card.Description` match the existing `ProductCardView` / `ServiceCardView` API.
