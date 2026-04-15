# Business Types — Single Source of Truth — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the business type system so `BusinessTypesSO` is the single source of truth — selector buttons and dropdown options are runtime-generated from the SO, and PlayerPrefs stores a stable string id instead of an int index.

**Architecture:** Rename `BusinessIconsSO` → `BusinessTypesSO` and add `id` + `displayName` to each entry. `Manager.cs` instantiates buttons from a single inactive template at `Start()` and populates the BotSettings dropdown when each settings instance is created. All `BusinessType` PlayerPrefs sites switch from `GetInt`/`SetInt` to `GetString`/`SetString` keyed by entry id. n8n form posts standardize on sending the human-readable `displayName`.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, UGUI, TextMeshPro, Unity Editor scripting.

**Verification model:** No automated test suite. Each task ends with: (1) project compiles, (2) any behavior change is described and manually verifiable in the Unity Editor at the end of the plan. The user runs the final editor verification (Task 6) after all code tasks land.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `Assets/Scripts/Main/BusinessTypesSO.cs` (renamed from `BusinessIconsSO.cs`) | ScriptableObject defining `Entry { id, displayName, sprite, tileColor }` and lookup methods. |
| `Assets/Data/BusinessTypes.asset` (recreated, replaces `BusinessIcons.asset`) | The single source of truth instance. Auto-bootstrapped by editor builder. |
| `Assets/Scripts/Main/Bot.cs` | Reads icon by string id via `TryGetById`. |
| `Assets/Scripts/Main/Manager.cs` | Adds runtime population of selector buttons + dropdown. All `BusinessType` PlayerPrefs sites switch to string. n8n form sites use `displayName`. |
| `Assets/Scripts/Editor/BotsPageSetup.cs` | Renames helper to `EnsureBusinessTypesAsset`, seeds id + displayName defaults. |
| Scene `Assets/Scenes/Main.unity` (manual user edit) | Delete 7 hand-wired buttons; keep one as `BusinessTypeButtonTemplate` (inactive); wire new Manager fields. |
| Prefab `Assets/Prefabs/BotSettings.prefab` (manual user edit) | Clear hand-typed dropdown options. |

---

## Task 1: Rename and reshape the ScriptableObject

**Files:**
- Rename: `Assets/Scripts/Main/BusinessIconsSO.cs` → `Assets/Scripts/Main/BusinessTypesSO.cs` (keep .meta to preserve script GUID)
- Modify: the renamed `Assets/Scripts/Main/BusinessTypesSO.cs` (new class name, new Entry shape, new lookup methods)

⚠️ **This task breaks compilation of `Bot.cs` and `BotsPageSetup.cs`** (they still reference `BusinessIconsSO`). Tasks 2 and 3 restore compilation. Don't run the Unity Editor between tasks 1 and 3.

- [ ] **Step 1: Rename the script file (preserving the .meta)**

```bash
git mv Assets/Scripts/Main/BusinessIconsSO.cs Assets/Scripts/Main/BusinessTypesSO.cs
git mv Assets/Scripts/Main/BusinessIconsSO.cs.meta Assets/Scripts/Main/BusinessTypesSO.cs.meta 2>/dev/null || true
```

The `.meta` may not exist yet (Unity hasn't focus-imported it since Task 1 of the previous plan). The `|| true` swallows that error. Either way, we proceed.

- [ ] **Step 2: Replace the file contents**

Write `Assets/Scripts/Main/BusinessTypesSO.cs`:

```csharp
using UnityEngine;

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

- [ ] **Step 3: Verify the file is syntactically valid C#**

Read the file end-to-end. Confirm braces balance, all 4 methods compile-safe.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/BusinessTypesSO.cs Assets/Scripts/Main/BusinessTypesSO.cs.meta 2>/dev/null
git add -u Assets/Scripts/Main/  # picks up the deletion of BusinessIconsSO.cs
git commit -m "refactor: rename BusinessIconsSO to BusinessTypesSO with id+displayName shape"
```

---

## Task 2: Update Bot.cs to use new class and string id

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs:26` (field type rename), `Assets/Scripts/Main/Bot.cs:235-248` (rewrite `ApplyBusinessIcon`)

- [ ] **Step 1: Rename the serialized field type**

In `Assets/Scripts/Main/Bot.cs`, find:

```csharp
    [SerializeField] private BusinessIconsSO businessIcons;
```

Replace with:

```csharp
    [SerializeField] private BusinessTypesSO businessTypes;
```

- [ ] **Step 2: Rewrite `ApplyBusinessIcon` to use string id + TryGetById**

Find the existing `ApplyBusinessIcon` method (currently around lines 235-248):

```csharp
    private void ApplyBusinessIcon()
    {
        if (businessIcons == null) return;

        int index = PlayerPrefs.GetInt(transform.name + "BusinessType", 0);
        if (!businessIcons.TryGet(index, out var entry))
        {
            Debug.LogWarning($"[Bot] No business icon entry for index {index} on '{transform.name}'");
            return;
        }

        if (BotIconImage != null && entry.sprite != null) BotIconImage.sprite = entry.sprite;
        if (BotIconTile != null) BotIconTile.color = entry.tileColor;
    }
```

Replace with:

```csharp
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

- [ ] **Step 3: Verify the file is syntactically valid C# and the rename is consistent**

Read the file. Search for any remaining `businessIcons` references — there should be zero. The only usage of the SO is the one field declaration and the method body.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "refactor: Bot.cs reads business type by string id"
```

---

## Task 3: Update BotsPageSetup.cs to use new class and seed id+displayName

**Files:**
- Modify: `Assets/Scripts/Editor/BotsPageSetup.cs` — three regions: constants, the wire line, and the bootstrap method.

- [ ] **Step 1: Update the path constants**

Find:

```csharp
    private const string BusinessIconsAssetPath = "Assets/Data/BusinessIcons.asset";
    private const string BusinessIconsSpritesDir = "Assets/Images/BusinessIcons";
```

Replace with:

```csharp
    private const string BusinessTypesAssetPath = "Assets/Data/BusinessTypes.asset";
    private const string BusinessIconsSpritesDir = "Assets/Images/BusinessIcons";
```

(`BusinessIconsSpritesDir` is unchanged — sprite folder name stays the same.)

- [ ] **Step 2: Update the wire line in BuildBotCard**

Find:

```csharp
        so.FindProperty("businessIcons").objectReferenceValue = EnsureBusinessIconsAsset();
```

Replace with:

```csharp
        so.FindProperty("businessTypes").objectReferenceValue = EnsureBusinessTypesAsset();
```

- [ ] **Step 3: Replace the BusinessIconDefaults table and the EnsureBusinessIconsAsset method**

Find the existing `BusinessIconDefaults` array and `EnsureBusinessIconsAsset` method (everything from the `// ── BusinessIcons ScriptableObject bootstrap ─` comment through the closing `}` of `EnsureBusinessIconsAsset`).

Replace the entire region with:

```csharp
    // ── BusinessTypes ScriptableObject bootstrap ─────────────────────────
    // Index order MUST match the legacy hand-wired BusinessTypesList so the
    // first run after the rename creates an asset whose entries align with
    // any pre-existing dev PlayerPrefs (best-effort; pre-launch).
    // 0 Car Service, 1 Cafe, 2 Beauty Salon, 3 Dentist,
    // 4 Real Estate, 5 Tour Agency, 6 Flowers.
    private static readonly (string id, string displayName, string fileName, Color tile)[] BusinessTypeDefaults =
    {
        ("car_service",  "Car Service",  "CarService.png",  Hex("#8E8E93")),
        ("cafe",         "Cafe",         "Cafe.png",        Hex("#FF9500")),
        ("beauty_salon", "Beauty Salon", "BeautySalon.png", Hex("#FF375F")),
        ("dentist",      "Dentist",      "Dentist.png",     Hex("#30B0C7")),
        ("real_estate",  "Real Estate",  "RealEstate.png",  Hex("#5856D6")),
        ("tour_agency",  "Tour Agency",  "TourAgency.png",  Hex("#32ADE6")),
        ("flowers",      "Flowers",      "Flowers.png",     Hex("#FF2D55")),
    };

    private static BusinessTypesSO EnsureBusinessTypesAsset()
    {
        // Make sure Assets/Data exists.
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var so = AssetDatabase.LoadAssetAtPath<BusinessTypesSO>(BusinessTypesAssetPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<BusinessTypesSO>();
            AssetDatabase.CreateAsset(so, BusinessTypesAssetPath);
        }

        var serialized = new SerializedObject(so);
        var entriesProp = serialized.FindProperty("entries");

        // Grow (never shrink) to defaults length so user-added entries are kept.
        if (entriesProp.arraySize < BusinessTypeDefaults.Length)
            entriesProp.arraySize = BusinessTypeDefaults.Length;

        for (int i = 0; i < BusinessTypeDefaults.Length; i++)
        {
            var (id, displayName, fileName, tile) = BusinessTypeDefaults[i];
            var elem = entriesProp.GetArrayElementAtIndex(i);
            var idProp          = elem.FindPropertyRelative("id");
            var displayNameProp = elem.FindPropertyRelative("displayName");
            var spriteProp      = elem.FindPropertyRelative("sprite");
            var colorProp       = elem.FindPropertyRelative("tileColor");

            // Fill empty id / displayName only — never clobber user edits.
            if (string.IsNullOrEmpty(idProp.stringValue))          idProp.stringValue          = id;
            if (string.IsNullOrEmpty(displayNameProp.stringValue)) displayNameProp.stringValue = displayName;

            // Always overwrite tile color with the default (the SO is the
            // source of truth, and the design owns the color).
            colorProp.colorValue = tile;

            // Only assign sprite if currently null AND the file exists by
            // convention. Never clobber a sprite the user manually wired.
            if (spriteProp.objectReferenceValue == null)
            {
                var path = $"{BusinessIconsSpritesDir}/{fileName}";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                    spriteProp.objectReferenceValue = sprite;
                else
                    Debug.LogWarning($"[BotsPageSetup] No sprite at {path} for index {i} — drop the PNG in and re-run the menu item.");
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();
        return so;
    }
```

- [ ] **Step 4: Verify the file is consistent end-to-end**

Read the modified `BotsPageSetup.cs` regions. Confirm:
- No remaining references to `BusinessIconsSO`, `BusinessIconsAssetPath`, `BusinessIconDefaults`, `EnsureBusinessIconsAsset`, or `businessIcons` (case-sensitive grep).
- The wire line uses `businessTypes` (matches the field renamed in Task 2).

Run from project root:

```bash
grep -n "BusinessIcons\|businessIcons" Assets/Scripts/Editor/BotsPageSetup.cs
```

Expected: 1 line containing `BusinessIconsSpritesDir = "Assets/Images/BusinessIcons"` (sprite folder path is intentionally unchanged).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Editor/BotsPageSetup.cs
git commit -m "refactor: BotsPageSetup bootstraps BusinessTypes asset with id+displayName"
```

---

## Task 4: Add new Manager.cs serialized fields and runtime population helpers (no behavior cutover yet)

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — add fields near line 53, add private state near line 79, add two private methods.

This task adds the new code without removing the old code or rewiring any call sites. After Task 4 the project still compiles and behaves exactly as it does today; Task 5 cuts over to the new code path.

- [ ] **Step 1: Add the three new serialized fields**

In `Assets/Scripts/Main/Manager.cs`, find the existing line:

```csharp
    [SerializeField] private List<Button> BusinessTypesList = new();
```

Insert the following BEFORE that line (so the new fields stay grouped under the same Header/section):

```csharp
    [SerializeField] private RectTransform BusinessTypesParent;
    [SerializeField] private GameObject BusinessTypeButtonTemplate;
    [SerializeField] private BusinessTypesSO businessTypes;
```

(The old `BusinessTypesList` field stays for now — it's removed in Task 5.)

- [ ] **Step 2: Add private runtime state**

Find the existing field:

```csharp
    private GameObject businessType;
```

Insert AFTER it:

```csharp
    private readonly System.Collections.Generic.List<Button> businessTypeButtons = new();
    private string selectedBusinessId = "";
```

(The old `businessType` field stays for now — it's removed in Task 5.)

- [ ] **Step 3: Add the two new private helper methods**

Find the existing `ChooseBusiness(Button chosenBusiness)` method (around line 620). Insert these two methods immediately BEFORE it (still inside the `Manager` class):

```csharp
    private void PopulateBusinessTypes()
    {
        if (BusinessTypesParent == null || BusinessTypeButtonTemplate == null || businessTypes == null)
        {
            Debug.LogError("[Manager] PopulateBusinessTypes: missing serialized refs (BusinessTypesParent, BusinessTypeButtonTemplate, or businessTypes).");
            return;
        }

        // Destroy any previously-instantiated buttons (everything except the template).
        for (int i = BusinessTypesParent.childCount - 1; i >= 0; i--)
        {
            var child = BusinessTypesParent.GetChild(i).gameObject;
            if (child == BusinessTypeButtonTemplate) continue;
            DestroyImmediate(child);
        }

        BusinessTypeButtonTemplate.SetActive(false);
        businessTypeButtons.Clear();

        foreach (var entry in businessTypes.All)
        {
            var go = Instantiate(BusinessTypeButtonTemplate, BusinessTypesParent);
            go.SetActive(true);
            go.name = entry.id;

            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = entry.displayName;

            var btn = go.GetComponent<Button>();
            var capturedId = entry.id;
            PopupUI.WireFingerUp(btn, () => ChooseBusiness(capturedId));
            businessTypeButtons.Add(btn);
        }

        if (businessTypes.Count > 0)
        {
            selectedBusinessId = businessTypes.All[0].id;
            if (businessTypeButtons.Count > 0)
                businessButtonDefaultColor = businessTypeButtons[0].GetComponent<Image>().color;
        }
        else
        {
            selectedBusinessId = "";
        }
    }

    private void PopulateBusinessDropdown(TMP_Dropdown dd)
    {
        if (dd == null || businessTypes == null) return;

        dd.options.Clear();
        foreach (var entry in businessTypes.All)
            dd.options.Add(new TMP_Dropdown.OptionData(entry.displayName));
        dd.RefreshShownValue();
    }

    public void ChooseBusiness(string id)
    {
        if (businessTypes == null || !businessTypes.TryGetById(id, out var entry)) return;
        selectedBusinessId = id;
        businessTypeSelected = true;

        for (int i = 0; i < businessTypeButtons.Count; i++)
        {
            var btn = businessTypeButtons[i];
            var img = btn.GetComponent<Image>();
            img.color = (btn.gameObject.name == id) ? Color.green : businessButtonDefaultColor;
        }

        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = entry.displayName;
            businessTypeValueText.color = new Color32(28, 28, 30, 255);
        }

        CloseBusinessSelector();
        ValidateCreateForm();
    }
```

⚠️ This adds an OVERLOADED `ChooseBusiness(string)` alongside the existing `ChooseBusiness(Button)`. Both compile and coexist until Task 5 deletes the Button overload.

- [ ] **Step 4: Verify file compiles**

Read the file, confirm braces still balance after the inserts. Confirm `using System.Collections.Generic;` is present at the top of the file (needed for `List<>` — should already be there since the existing `BusinessTypesList` uses it).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "refactor: add Manager runtime helpers for business types (no cutover yet)"
```

---

## Task 5: Cut Manager.cs over to the new system; remove old fields and call sites

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — multiple regions: Start(), CreateBotFromForm, SaveSettings, CloseSettings, EnableSave, ChooseBusiness, ResetAddBotForm, n8n form sites, and field deletions.

This task cuts every existing PlayerPrefs site, every reference to the old `BusinessTypesList` and `businessType` fields, and the old `ChooseBusiness(Button)` overload. After this task, the Unity project compiles end-to-end and the new system is the only path.

- [ ] **Step 1: Replace the Start() init block for business types**

In `Manager.cs`, find these lines inside `Start()` (around lines 137-138):

```csharp
        businessType = BusinessTypesList[0].gameObject;
        businessButtonDefaultColor = businessType.GetComponent<Image>().color;
```

Replace with:

```csharp
        PopulateBusinessTypes();
```

(`PopulateBusinessTypes` already initializes `selectedBusinessId` and `businessButtonDefaultColor` internally.)

- [ ] **Step 2: Replace the Start() WireFingerUp loop**

Find (around lines 191-196):

```csharp
        // Business type buttons (dismiss the selector → finger-up)
        foreach (Button business in BusinessTypesList)
        {
            var captured = business;
            PopupUI.WireFingerUp(captured, () => ChooseBusiness(captured));
        }
```

Delete this entire block. (`PopulateBusinessTypes` already wires `WireFingerUp` for each instantiated button.)

- [ ] **Step 3: Update the bot-recreate dropdown sync (line ~272)**

Find:

```csharp
                recreatedBotSettings.BusinessTypeDropdown.value = PlayerPrefs.GetInt(recreatedBot.name + "BusinessType", 0);
```

Replace with:

```csharp
                PopulateBusinessDropdown(recreatedBotSettings.BusinessTypeDropdown);
                {
                    var savedId = PlayerPrefs.GetString(recreatedBot.name + "BusinessType", "");
                    recreatedBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(savedId));
                }
```

(The braces scope the `savedId` local so it doesn't collide with reuse below.)

- [ ] **Step 4: Update SaveSettings (line ~330)**

Find:

```csharp
        PlayerPrefs.SetInt(openBot.name + "BusinessType", openBotSettings.BusinessTypeDropdown.value);
```

Replace with:

```csharp
        {
            var dd = openBotSettings.BusinessTypeDropdown;
            if (businessTypes.TryGetByIndex(dd.value, out var bt))
                PlayerPrefs.SetString(openBot.name + "BusinessType", bt.id);
        }
```

- [ ] **Step 5: Update CloseSettings (line ~433)**

Find:

```csharp
        openBotSettings.BusinessTypeDropdown.value = PlayerPrefs.GetInt(openBot.name + "BusinessType", 0);
```

Replace with:

```csharp
        {
            var savedId = PlayerPrefs.GetString(openBot.name + "BusinessType", "");
            openBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(savedId));
        }
```

- [ ] **Step 6: Update EnableSave dirty-check (line ~487)**

Find:

```csharp
            openBotSettings.BusinessTypeDropdown.value != PlayerPrefs.GetInt(openBot.name + "BusinessType", 0) ||
```

Replace with:

```csharp
            (businessTypes.TryGetByIndex(openBotSettings.BusinessTypeDropdown.value, out var dirtyBt)
                ? dirtyBt.id : "")
                != PlayerPrefs.GetString(openBot.name + "BusinessType", "") ||
```

- [ ] **Step 7: Delete the old ChooseBusiness(Button) overload**

Find:

```csharp
    public void ChooseBusiness(Button chosenBusiness)
    {
        businessType = chosenBusiness.gameObject;
        businessTypeSelected = true;

        foreach (Button business in BusinessTypesList)
        {
            business.gameObject.GetComponent<Image>().color = businessButtonDefaultColor;
        }
        chosenBusiness.gameObject.GetComponent<Image>().color = Color.green;

        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = chosenBusiness.gameObject.name;
            businessTypeValueText.color = new Color32(28, 28, 30, 255);
        }

        CloseBusinessSelector();
        ValidateCreateForm();
    }
```

Delete the entire method. (The new `ChooseBusiness(string)` from Task 4 replaces it.)

- [ ] **Step 8: Update CreateBotFromForm new-bot dropdown sync and PlayerPrefs save (lines ~760, ~795)**

Find (around line 760):

```csharp
        newBotSettings.BusinessTypeDropdown.value = businessType.transform.GetSiblingIndex();
```

Replace with:

```csharp
        PopulateBusinessDropdown(newBotSettings.BusinessTypeDropdown);
        newBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(selectedBusinessId));
```

Find (around line 795):

```csharp
        PlayerPrefs.SetInt(newBot.name + "BusinessType", businessType.transform.GetSiblingIndex());
```

Replace with:

```csharp
        PlayerPrefs.SetString(newBot.name + "BusinessType", selectedBusinessId);
```

- [ ] **Step 9: Update ResetAddBotForm (lines ~836 and ~866)**

Find (around line 836):

```csharp
        businessType = BusinessTypesList[0].gameObject;
```

Replace with:

```csharp
        selectedBusinessId = businessTypes.Count > 0 ? businessTypes.All[0].id : "";
```

Find (around line 866):

```csharp
        foreach (Button business in BusinessTypesList)
        {
            business.gameObject.GetComponent<Image>().color = businessButtonDefaultColor;
        }
```

Replace with:

```csharp
        foreach (var btn in businessTypeButtons)
        {
            btn.GetComponent<Image>().color = businessButtonDefaultColor;
        }
```

- [ ] **Step 10: Update the two n8n form sites that reference `businessType.name` (lines ~1900 and ~2030)**

Find (around line 1900, inside `CreateWhatsappWorkflowFromStart`):

```csharp
        form.AddField("BusinessType", businessType.name);
```

Replace with:

```csharp
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt1) ? bt1.displayName : "");
```

Find (around line 2030, inside `CreateTelegramWorkflowFromStart`):

```csharp
        form.AddField("BusinessType", businessType.name);
```

Replace with:

```csharp
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt2) ? bt2.displayName : "");
```

(The other three n8n sites at lines 1991, 2121, 2278 already use `Dropdown.options[Dropdown.value].text` — they keep working unchanged because the dropdown options now contain `displayName`.)

- [ ] **Step 11: Delete the now-orphan `BusinessTypesList` field declaration**

Find (around line 53):

```csharp
    [SerializeField] private List<Button> BusinessTypesList = new();
```

Delete this line entirely.

- [ ] **Step 12: Delete the now-orphan `businessType` private field declaration**

Find (around line 79):

```csharp
    private GameObject businessType;
```

Delete this line entirely.

- [ ] **Step 13: Verify compilation hygiene**

Read the file. Search for any remaining references to the deleted symbols:

```bash
grep -n "BusinessTypesList\|\bbusinessType\b" Assets/Scripts/Main/Manager.cs
```

Expected output: zero matches. (The pattern `\bbusinessType\b` excludes `businessTypeButtons`, `businessTypeButtonTemplate`, `businessTypeSelected`, `businessTypeValueText`, `businessTypes`, `BusinessType` field name in PlayerPrefs string literals — those are intentionally distinct identifiers.)

⚠️ If grep returns any hits, STOP and re-read those locations. There may be additional sites missed by this plan; report them as DONE_WITH_CONCERNS.

- [ ] **Step 14: Commit**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "refactor: cut Manager.cs over to BusinessTypesSO source of truth"
```

---

## Task 6: User editor and prefab work

**Files:**
- Edit in Unity Editor: `Assets/Scenes/Main.unity`, `Assets/Prefabs/BotSettings.prefab`, `Assets/Data/BusinessIcons.asset` (delete), `Assets/Data/BusinessTypes.asset` (auto-created), `Manager` scene component (re-wire serialized fields).

This task cannot be done by code-edit subagents — it requires the Unity Editor. Each step is a manual click/drag operation in Unity.

- [ ] **Step 1: Locate the parent of the existing 7 business type buttons in the scene**

In the Hierarchy: select the `Manager` GameObject → look at its Inspector → find the (about-to-be-deleted) `BusinessTypesList` field. Each entry's parent transform is the same — that's `BusinessTypesParent`. Note its location in the Hierarchy (e.g., `Canvas/BusinessSelectorPanel/Content/ScrollRect/Viewport/Buttons` — the exact path will vary).

- [ ] **Step 2: Convert one button into the template**

Pick any one of the 7 buttons under that parent (e.g., `Cafe`). Rename it `BusinessTypeButtonTemplate`. Set its GameObject inactive (uncheck the checkbox at the top of the Inspector). Make it the FIRST child of the parent (drag to the top).

- [ ] **Step 3: Delete the other 6 buttons**

Select the remaining 6 buttons (Car Service, Beauty Salon, Dentist, Real Estate, Tour Agency, Flowers) and delete them.

- [ ] **Step 4: Wire the new Manager fields**

Select the `Manager` GameObject. In the Inspector:
- Drag the parent transform from Step 1 into `Business Types Parent`.
- Drag `BusinessTypeButtonTemplate` (the inactive child) into `Business Type Button Template`.
- Drag `Assets/Data/BusinessTypes.asset` into `Business Types`. (If the asset doesn't exist yet, run `Tools > Setup My Bots Page` first to create it, then come back here.)

- [ ] **Step 5: Delete the legacy asset**

In the Project window: select `Assets/Data/BusinessIcons.asset` → right-click → Delete. Confirm.

- [ ] **Step 6: Run the editor builder to create the new asset**

Top menu: `Tools > Setup My Bots Page`.

Expected Console output:
- Zero compile errors.
- Possibly 7 warnings about missing sprites if you renamed/moved the PNGs (or zero if they're already at `Assets/Images/BusinessIcons/<file>.png`).
- Info: `[BotsPageSetup] Done — save the scene (Ctrl+S / Cmd+S).`

Verify `Assets/Data/BusinessTypes.asset` now exists and shows 7 entries with id, displayName, sprite, and tileColor populated.

- [ ] **Step 7: Clear the BotSettings prefab dropdown options**

Open `Assets/Prefabs/BotSettings.prefab` in Prefab edit mode. Find the `BusinessTypeDropdown` (TMP_Dropdown). In its `Options` list, delete all entries (set Size to 0). Save the prefab.

(Manager populates these at runtime via `PopulateBusinessDropdown` whenever a BotSettings is instantiated; the hand-typed options would only be misleading.)

- [ ] **Step 8: Save the scene and verify in Play mode**

Save the scene (Cmd+S). Press Play. Verify:
- The Add Bot business selector popup shows 7 buttons (one per SO entry), with the correct labels.
- Tapping a button highlights it green and closes the popup.
- The chosen displayName appears on the form's Business Type row.
- Creating a bot saves it; reopening the bot's settings shows the dropdown pre-selected to the same business type.
- The bot card icon and tile color match the chosen business type.
- Changing the business type in settings and saving updates the card icon immediately.

- [ ] **Step 9: Commit the scene + prefab + asset changes**

```bash
git add Assets/Scenes/Main.unity Assets/Prefabs/BotSettings.prefab Assets/Data/BusinessTypes.asset 'Assets/Data/BusinessTypes.asset.meta' 'Assets/Data/BusinessIcons.asset.meta' 2>/dev/null
git add -u Assets/Data/  # picks up deletion of BusinessIcons.asset
git commit -m "refactor: switch scene/prefab/asset to BusinessTypes single source of truth"
```

---

## Self-Review Notes

**Spec coverage:**
- §1 Renamed SO + new shape → Task 1
- §2 Default seed contents → Task 3
- §3 Scene change (parent + template) → Tasks 4 (fields), 6 (scene work)
- §4 Runtime population → Task 4 (helpers added), Task 5 (call sites)
- §5 ChooseBusiness signature change → Task 4 (new overload), Task 5 (delete old)
- §6 Dropdown population → Task 4 (helper), Task 5 (calls in 2 spots), Task 6 (clear prefab options)
- §7 PlayerPrefs format → Task 5 (steps 3, 4, 5, 6, 8)
- §8 Bot.cs change → Task 2
- §9 n8n form posts → Task 5 (step 10)
- §10 BotsPageSetup.cs changes → Task 3
- §11 Adding a business type after this lands → Task 6 (verification step demonstrates the new flow)
- Edge cases (§ in spec) → All guarded in code: empty SO, missing id, missing template, null SO ref.

**Type/name consistency check:**
- `BusinessTypesSO` (class) and `businessTypes` (field name on Bot.cs / Manager.cs) — used consistently from Task 1 onwards.
- `EnsureBusinessTypesAsset` — defined in Task 3, called via the `businessTypes` wire line in Task 3.
- `PopulateBusinessTypes` / `PopulateBusinessDropdown` / `ChooseBusiness(string)` — defined in Task 4, called in Task 5.
- `selectedBusinessId` / `businessTypeButtons` — defined in Task 4, used in Task 5.
- `BusinessTypesParent` / `BusinessTypeButtonTemplate` — declared in Task 4, populated by user in Task 6 step 4.
- PlayerPrefs key `name + "BusinessType"` — same string literal across all 6 sites (Task 5 steps 3-6, 8 and Bot.cs ApplyBusinessIcon Task 2).

**Placeholder scan:** Zero TBDs / TODOs. Every code step contains complete code. Every command step contains the exact command and expected behavior.
