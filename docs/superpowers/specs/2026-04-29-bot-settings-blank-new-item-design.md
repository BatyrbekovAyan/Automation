# Blank new Product/Service items, with placeholder fallbacks on save

## Problem

In **Bot Settings → Products / Services**, tapping **Add New** instantiates a card from `Product.prefab` / `Service.prefab` and immediately opens `ItemEditSheet` to edit it. The prefabs ship with display labels:

- Name: `Название` (Russian: "Name")
- Price: `0` + `₸` symbol
- Description: `Описание` (Russian: "Description")

Because `ItemEditSheet.Show` binds `card.Name`, `card.Price`, `card.Description` directly into the three input fields, the user sees those prefab defaults pre-typed in the inputs and has to manually delete them before entering real values.

## Desired behavior

1. **On Add** — the edit sheet should open with all three input fields **blank**.
2. **On Save (Done)** — when committing the user's input back to the card:
   - If **Name** is empty/whitespace → fall back to `"New Product"` (for products) or `"New Service"` (for services).
   - If **Price** is empty/whitespace → fall back to `"0"`.
   - If **Description** is empty/whitespace → leave empty (description has no placeholder).

## Design

### Scope

Two source files change. The Unity prefabs are not touched, so the Unity-Editor-time card preview keeps showing the existing labels (which is the correct behavior for the prefab, not for runtime cards).

### Files

#### `Assets/Scripts/Main/BotSettings.cs`

In `AddProduct()` (line 475) and `AddService()` (line 500), after instantiating the card and before calling `productEditSheet.Show(card)` / `serviceEditSheet.Show(card)`, blank the card's three properties:

```csharp
card.Name = string.Empty;
card.Price = string.Empty;
card.Description = string.Empty;
```

This makes the subsequent `BindFields` call in `ItemEditSheet.Show` push empty strings into the input fields.

#### `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`

Add private constants for the placeholder values at the top of the class:

```csharp
private const string ProductNamePlaceholder = "New Product";
private const string ServiceNamePlaceholder = "New Service";
private const string PricePlaceholder = "0";
```

Modify `Commit()` (line 483) so that when the bound card is written, empty/whitespace name and price values fall back to the placeholders. The product vs. service branch already exists, so the correct name placeholder is naturally available in each branch.

```csharp
private void Commit()
{
    var name  = nameField.Value;
    var price = priceField.Value;
    var desc  = descField.Value;

    if (boundProduct != null)
    {
        boundProduct.Name        = string.IsNullOrWhiteSpace(name)  ? ProductNamePlaceholder : name;
        boundProduct.Price       = string.IsNullOrWhiteSpace(price) ? PricePlaceholder       : price;
        boundProduct.Description = desc;
    }
    else if (boundService != null)
    {
        boundService.Name        = string.IsNullOrWhiteSpace(name)  ? ServiceNamePlaceholder : name;
        boundService.Price       = string.IsNullOrWhiteSpace(price) ? PricePlaceholder       : price;
        boundService.Description = desc;
    }
    OnAnyCommitted?.Invoke();
    Hide();
}
```

`Description` is written through unchanged so empty strings remain empty.

### Why placeholders apply *on commit*, not on bind

The user can also edit *existing* items by tapping their card. We must NOT overwrite an existing item's empty (deliberately cleared) field with a placeholder when the sheet opens — only when the user explicitly chooses to save. Putting the fallback inside `Commit()` covers both new-item and existing-item flows correctly: if a user clears a name and presses Done, the placeholder still kicks in, which is consistent with the new-item path.

### Out of scope

- Hint text inside the empty input fields (`TMP_InputField.placeholder`). Could be added separately if desired, but is not required by the request.
- Localizing the placeholders. Existing prefab text is Russian; the user explicitly chose English placeholders (option C).
- Numeric validation on the Price field.

## Verification

Manual checks in Unity Play mode:

1. Open Bot Settings → Products → tap **+** → confirm Name / Price / Description inputs are all empty.
2. Tap **Done** without typing anything → confirm the new product card shows `New Product`, `0`, and an empty description.
3. Add another product, type only a Name → confirm card shows that name, `0`, and empty description.
4. Repeat 1–3 on the Services tab → confirm fallback name is `New Service`.
5. Edit an existing product, clear all three fields manually, press Done → confirm placeholders apply (consistent with new-item flow).
6. Edit an existing product, change only the description, press Done → confirm name and price are preserved (not overwritten by placeholders since they are non-empty).
