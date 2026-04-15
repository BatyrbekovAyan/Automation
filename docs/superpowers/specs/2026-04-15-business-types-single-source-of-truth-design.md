# Business Types — Single Source of Truth — Design

**Date:** 2026-04-15
**Status:** Approved (pending review)

## Goal

Make `BusinessTypesSO` the single source of truth for business types so adding a new type is a one-step Inspector edit (no code edits, no scene edits, no dropdown wiring). The selector buttons and dropdown options become runtime-generated from the SO; PlayerPrefs stores a stable string id instead of an int index, so reordering the SO is safe.

## Background

Today, business types are coupled by **position only** across three independent lists:
- `Manager.BusinessTypesList` (List<Button>) — 7 hand-wired buttons in the scene; index = type id
- `BotSettings.BusinessTypeDropdown` (TMP_Dropdown) — 7 hand-typed options on the prefab; same index order
- `BusinessIconsSO.entries` — 7 entries; same index order

PlayerPrefs stores `botName + "BusinessType"` as the int index. n8n form posts send the human label.

This is brittle: any reorder/insert breaks every saved bot, and adding a type requires editing all three lists plus the icon defaults plus running the editor builder.

## Migration assumption

The project is pre-launch — no real users, no real PlayerPrefs to preserve. No migration code is required. Existing dev PlayerPrefs that hold an int will read as empty string after the format change and fall back to the first entry.

## Design

### 1. Renamed and expanded ScriptableObject

`BusinessIconsSO` → `BusinessTypesSO`. Asset path: `Assets/Data/BusinessIcons.asset` → `Assets/Data/BusinessTypes.asset`.

```csharp
[CreateAssetMenu(menuName = "Automation/Business Types", fileName = "BusinessTypes")]
public class BusinessTypesSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public string id;          // stable kebab-case key, e.g. "beauty_salon"
        public string displayName; // user-facing, e.g. "Beauty Salon"
        public Sprite sprite;
        public Color tileColor;
    }

    [SerializeField] private Entry[] entries;

    public Entry[] All => entries ?? System.Array.Empty<Entry>();
    public int Count => entries == null ? 0 : entries.Length;

    public bool TryGetById(string id, out Entry entry)
    {
        if (!string.IsNullOrEmpty(id) && entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].id == id)
                {
                    entry = entries[i];
                    return true;
                }
            }
        }
        entry = default;
        return false;
    }

    public bool TryGetByIndex(int index, out Entry entry)
    {
        if (entries != null && index >= 0 && index < entries.Length)
        {
            entry = entries[index];
            return true;
        }
        entry = default;
        return false;
    }

    public int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id) || entries == null) return -1;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].id == id) return i;
        return -1;
    }
}
```

### 2. Default seed contents

`BotsPageSetup.EnsureBusinessTypesAsset()` (renamed from `EnsureBusinessIconsAsset`) seeds these defaults on first run. Existing users of the asset (in this dev environment) keep their tile colors / sprites; the bootstrap only fills empty `id` / `displayName` slots.

| Index | id | displayName | Sprite (filename) | tileColor |
|-------|----|----|----|----|
| 0 | `car_service` | Car Service | CarService.png | #8E8E93 |
| 1 | `cafe` | Cafe | Cafe.png | #FF9500 |
| 2 | `beauty_salon` | Beauty Salon | BeautySalon.png | #FF375F |
| 3 | `dentist` | Dentist | Dentist.png | #30B0C7 |
| 4 | `real_estate` | Real Estate | RealEstate.png | #5856D6 |
| 5 | `tour_agency` | Tour Agency | TourAgency.png | #32ADE6 |
| 6 | `flowers` | Flowers | Flowers.png | #FF2D55 |

### 3. Scene change — runtime-generated buttons

The 7 hand-wired buttons under the business selector popup get deleted from the scene. Replaced with:

- **`BusinessTypesParent`** (RectTransform) — empty container, holds the runtime-instantiated buttons. Same parent that previously held the 7 fixed buttons.
- **`BusinessTypeButtonTemplate`** (GameObject, kept inactive) — a single button prefab clone that lives in `BusinessTypesParent` as the first child. It defines the button's visual style (background, label TMP, layout). Stays `SetActive(false)`; copies of it get instantiated and activated.

`Manager.cs` adds two new serialized fields:
```csharp
[SerializeField] private RectTransform BusinessTypesParent;
[SerializeField] private GameObject BusinessTypeButtonTemplate;
[SerializeField] private BusinessTypesSO businessTypes;
```

The existing `[SerializeField] private List<Button> BusinessTypesList = new();` becomes a private (non-serialized) runtime list:
```csharp
private readonly List<Button> businessTypeButtons = new();
```

### 4. Runtime population

New method `Manager.PopulateBusinessTypes()`, called from `Start()` before the existing `WireFingerUp` loop:

```csharp
private void PopulateBusinessTypes()
{
    // Clear any previously-instantiated buttons (everything except the template).
    for (int i = BusinessTypesParent.childCount - 1; i >= 0; i--)
    {
        var child = BusinessTypesParent.GetChild(i).gameObject;
        if (child == BusinessTypeButtonTemplate) continue;
        DestroyImmediate(child);
    }

    businessTypeButtons.Clear();

    foreach (var entry in businessTypes.All)
    {
        var go = Instantiate(BusinessTypeButtonTemplate, BusinessTypesParent);
        go.SetActive(true);
        go.name = entry.id;

        // Label TMP — first TextMeshProUGUI in children.
        var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = entry.displayName;

        var btn = go.GetComponent<Button>();
        var captured = entry.id;
        PopupUI.WireFingerUp(btn, () => ChooseBusiness(captured));
        businessTypeButtons.Add(btn);
    }

    // Initialize selection to the first entry, matching the prior behavior.
    selectedBusinessId = businessTypes.Count > 0 ? businessTypes.All[0].id : "";
    if (businessTypeButtons.Count > 0)
        businessButtonDefaultColor = businessTypeButtons[0].GetComponent<Image>().color;
}
```

### 5. ChooseBusiness signature change

The current method takes a `Button`; the new method takes the `id` string and looks up the entry / button.

```csharp
private string selectedBusinessId;

public void ChooseBusiness(string id)
{
    if (!businessTypes.TryGetById(id, out var entry)) return;
    selectedBusinessId = id;

    // Re-color buttons: chosen one highlighted, others default.
    for (int i = 0; i < businessTypeButtons.Count; i++)
    {
        var btn = businessTypeButtons[i];
        var img = btn.GetComponent<Image>();
        img.color = (btn.gameObject.name == id) ? entry.tileColor : businessButtonDefaultColor;
    }
}
```

(The `private GameObject businessType;` field is deleted — `selectedBusinessId` replaces it.)

### 6. Dropdown population

The BotSettings prefab's `BusinessTypeDropdown` (TMP_Dropdown) currently has 7 hand-typed options. Those are deleted. Manager adds a helper called whenever a BotSettings is created (both `LoadBots` and `CreateBotFromForm` paths):

```csharp
private void PopulateBusinessDropdown(TMP_Dropdown dd)
{
    dd.options.Clear();
    foreach (var entry in businessTypes.All)
        dd.options.Add(new TMP_Dropdown.OptionData(entry.displayName));
    dd.RefreshShownValue();
}
```

### 7. PlayerPrefs format

| Site | Before | After |
|------|--------|-------|
| All `BusinessType` reads/writes | `GetInt`/`SetInt`, value is index | `GetString`/`SetString`, value is `entry.id` |

Specific Manager.cs sites that change:

- **Line 272** (recreate-on-load):
  ```csharp
  var savedId = PlayerPrefs.GetString(recreatedBot.name + "BusinessType", "");
  recreatedBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(savedId));
  ```
  Plus a `PopulateBusinessDropdown(recreatedBotSettings.BusinessTypeDropdown)` call earlier in the same block, before this line.

- **Line 330** (SaveSettings):
  ```csharp
  var savedEntry = businessTypes.All[openBotSettings.BusinessTypeDropdown.value];
  PlayerPrefs.SetString(openBot.name + "BusinessType", savedEntry.id);
  ```

- **Line 433** (open settings):
  ```csharp
  var savedId = PlayerPrefs.GetString(openBot.name + "BusinessType", "");
  openBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(savedId));
  ```

- **Line 487** (dirty-check):
  ```csharp
  businessTypes.All[openBotSettings.BusinessTypeDropdown.value].id != PlayerPrefs.GetString(openBot.name + "BusinessType", "") ||
  ```

- **Line 760** (new bot dropdown sync after instantiation):
  ```csharp
  PopulateBusinessDropdown(newBotSettings.BusinessTypeDropdown);
  newBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(selectedBusinessId));
  ```

- **Line 795** (save new bot):
  ```csharp
  PlayerPrefs.SetString(newBot.name + "BusinessType", selectedBusinessId);
  ```

### 8. Bot.cs change

`Bot.cs:239` switches from int-by-index to string-by-id. Also rename the SO field type.

```csharp
[SerializeField] private BusinessTypesSO businessTypes; // renamed from businessIcons

private void ApplyBusinessIcon()
{
    if (businessTypes == null) return;

    var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
    if (!businessTypes.TryGetById(id, out var entry))
    {
        Debug.LogWarning($"[Bot] No business type entry for id '{id}' on '{transform.name}'");
        return;
    }

    if (BotIconImage != null && entry.sprite != null) BotIconImage.sprite = entry.sprite;
    if (BotIconTile != null) BotIconTile.color = entry.tileColor;
}
```

### 9. n8n form posts

All five sites that include `BusinessType` in n8n form posts (lines 1900, 1991, 2030, 2121, 2278) standardize on sending `displayName`. Today they're inconsistent — three use `Dropdown.options[].text` (which is the displayName) and two use `businessType.name` (which would become the id after refactor, breaking n8n).

Replace lines 1900 and 2030's `form.AddField("BusinessType", businessType.name)` with:
```csharp
form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt) ? bt.displayName : "");
```

The other three sites already use `Dropdown.options[Dropdown.value].text` — that keeps working because Dropdown options now contain displayNames.

### 10. BotsPageSetup.cs changes

- Rename `EnsureBusinessIconsAsset` → `EnsureBusinessTypesAsset`, return type `BusinessTypesSO`.
- Update `BusinessIconsAssetPath` → `BusinessTypesAssetPath = "Assets/Data/BusinessTypes.asset"`.
- Update `BusinessIconDefaults` to a `(string id, string displayName, string fileName, Color tile)` tuple array matching the seed table in §2.
- Bootstrap loop sets `id` and `displayName` only if currently empty/null (don't clobber user edits).
- Update the wire line `so.FindProperty("businessIcons").objectReferenceValue = EnsureBusinessIconsAsset();` to use the new property name `businessTypes`.

The asset rename `Assets/Data/BusinessIcons.asset` → `Assets/Data/BusinessTypes.asset` is handled by deleting the old asset and letting `EnsureBusinessTypesAsset()` create the new one. (Pre-launch — no data loss concern.)

### 11. Adding a business type after this lands

1. Open `Assets/Data/BusinessTypes.asset` in the Inspector.
2. Increment array size; fill in:
   - `id` (kebab-case, never change after launch),
   - `displayName`,
   - `sprite` (drag in the new PNG, drop into `Assets/Images/BusinessIcons/` first if needed),
   - `tileColor`.
3. Press Play. Selector popup and dropdown both show the new type. Selecting it and saving stores the new id; reopening shows the new type pre-selected with the right icon.

No code edits, no scene edits, no menu items to run.

## Data flow

```
BusinessTypesSO.entries  ────────────────┐
                                         ├──► Manager.PopulateBusinessTypes()
                                         │       └─ instantiates buttons in BusinessTypesParent
                                         │       └─ each button.name = entry.id, label = displayName
                                         │
                                         ├──► Manager.PopulateBusinessDropdown(dd)
                                         │       └─ dd.options = [displayName for each entry]
                                         │
                                         └──► Bot.ApplyBusinessIcon()
                                                 └─ TryGetById(PlayerPrefs.GetString(...))
                                                 └─ assigns sprite + tile color

User picks a business type:
  selector button (popup) → ChooseBusiness(id) → selectedBusinessId
  OR dropdown.value change in BotSettings → translated to id at save time

Save:
  selectedBusinessId (new bot) or businessTypes.All[dd.value].id (settings)
  → PlayerPrefs.SetString(botName + "BusinessType", id)

Load:
  PlayerPrefs.GetString → IndexOf(id) → dd.value
  PlayerPrefs.GetString → TryGetById → Bot icon visuals
```

## Edge cases

- **Empty SO** (`Count == 0`): selector popup shows no buttons, dropdown shows no options, `selectedBusinessId` is `""`. Saving a new bot writes empty string. Bot.cs logs a warning and skips icon/tile assignment. Acceptable — empty SO is a configuration error, not a runtime crash.
- **Saved id no longer in SO** (entry deleted post-launch): `IndexOf` returns -1, `Mathf.Max(0, -1)` clamps dropdown to first entry; Bot.cs logs a warning and leaves the prefab's default tile color/icon. The bot's PlayerPrefs id is left as-is (not silently rewritten).
- **Duplicate ids in SO**: `TryGetById` returns the first match. Editor-only validation could be added later but is out of scope.
- **Template button missing**: `PopulateBusinessTypes` no-ops if `BusinessTypeButtonTemplate` is null and logs an error.
- **No SO assigned on Manager**: `PopulateBusinessTypes` no-ops with a logged error; Bot.cs already guards `if (businessTypes == null) return;`.

## Files touched

| File | Change |
|------|--------|
| `Assets/Scripts/Main/BusinessIconsSO.cs` → `BusinessTypesSO.cs` | Rename file + class. New `Entry` shape with id+displayName. New `TryGetById` / `TryGetByIndex` / `IndexOf` methods. |
| `Assets/Data/BusinessIcons.asset` → `BusinessTypes.asset` | Delete old, recreated by editor builder with new shape. |
| `Assets/Scripts/Main/Bot.cs` | Field rename `businessIcons` → `businessTypes`. `ApplyBusinessIcon` uses `GetString` + `TryGetById`. |
| `Assets/Scripts/Main/Manager.cs` | New: `BusinessTypesParent`, `BusinessTypeButtonTemplate`, `businessTypes` serialized fields; `businessTypeButtons` private list; `selectedBusinessId` private string; `PopulateBusinessTypes()`, `PopulateBusinessDropdown()`. Removed: `BusinessTypesList` serialized field; `private GameObject businessType`. Changed: `ChooseBusiness(string id)`, all 6 `BusinessType` PlayerPrefs sites, n8n form sites for lines 1900 and 2030. |
| `Assets/Scripts/Editor/BotsPageSetup.cs` | Rename references; update bootstrap defaults to seed id+displayName. |
| `Assets/Scenes/Main.unity` | Delete the 7 hand-wired business type buttons. Add `BusinessTypesParent` (already exists as the parent transform) wiring on Manager + a single `BusinessTypeButtonTemplate` child (kept inactive). |
| `Assets/Prefabs/BotSettings.prefab` | Clear the 7 hand-typed dropdown options (Manager populates at runtime). |
| `Manager` scene component | Re-wire: remove `BusinessTypesList`; assign `BusinessTypesParent`, `BusinessTypeButtonTemplate`, `businessTypes`. |

## Out of scope

- Migrating existing PlayerPrefs from int → string id (pre-launch, not needed).
- Custom Inspector for the SO (default array editor is sufficient).
- Editor-time validation (warn on duplicate ids, missing sprites). Could be added later if it becomes a real annoyance.
- Localization of `displayName` (single-language per current app state).
- Animated transitions when icons change (already handled by existing card render).
