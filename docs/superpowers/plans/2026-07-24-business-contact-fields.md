# Business Contact Fields — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add five structured contact fields (Телефон, Часы работы, Адрес, Instagram, Email) to the BotSettings «Бизнес» tab, persist them per-bot, and fold them into the bot-knowledge payload already sent to n8n.

**Architecture:** New fields reuse the existing `EditableField` card primitive; the «Бизнес» tab is switched from non-scrollable to scrollable. Values persist in PlayerPrefs keyed by the bot GameObject name (existing bot-persistence pattern), mirrored at every site the existing `Business` key is touched. A pure static `ComposeBusinessKnowledge(...)` builds the description + a labeled `Контакты:` block that rides the existing `Business` form field — so no n8n workflow changes.

**Tech Stack:** Unity 6000.3.9f1, C#, TMPro, PlayerPrefs, NUnit EditMode tests via the project test bridge, `[MenuItem]` Editor builders (`BotSettingsRebuilder`).

## Global Constraints

- Unity `6000.3.9f1`; C#; TMPro for all text (never legacy `Text`). — verbatim from project rules.
- Bot data persists in `PlayerPrefs` keyed by `transform.name` + suffix (e.g. `Bot0Phone`). — bot-persistence pattern.
- **No n8n workflow changes.** New data rides the existing `Business` form field.
- New UI uses the existing `EditableField` primitive only — no new visual component.
- Editor builders destroy-and-rebuild: **re-stamp every serialized ref via `SerializedObject`** or refs silently null out.
- After running a builder: **save the scene and commit the regenerated `BotSettings.prefab` (+ `Main.unity` if changed) immediately** — a parallel session saving the scene will clobber uncommitted component adds.
- Brand-new `.cs` files are silently excluded from compile until an `Assets/Refresh` runs and the `.meta` appears — refresh + verify before compiling/testing a new file.
- Verify via the project test bridge (`Tools/run-tests-headless.sh` when the Editor is closed, else drop `Temp/claude/run-tests.trigger` and read `Temp/claude/test-summary.json`). Never trust Play-Mode green.
- Commits are per-task **with user consent** (project's Unity execution loop); end commit messages with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Stage both `.cs` and `.meta`.
- Bot workflows are activated only during supervised testing (real contacts) — never auto-activate.

## File Structure

- `Assets/Scripts/Main/Manager.cs` (modify) — `ComposeBusinessKnowledge` (pure + `BotSettings` overload); mirror the 5 keys at recreate/save/revert/dirty-check; swap 3 send-sites.
- `Assets/Scripts/Main/BotSettings.cs` (modify) — 5 `EditableField` serialized refs; wire `OnCommitted` in `WireFields()`.
- `Assets/Scripts/Main/Bot.cs` (modify) — delete the 5 keys in `DeleteBot()`.
- `Assets/Editor/BotSettingsRebuilder.cs` (modify) — `BuildBusinessTab`; drop `"Business"` from `nonScrollableTabs`; stamp 5 refs; set phone/email keyboards.
- `Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs` (create) — compose unit tests + a prefab-wiring guard test.
- `Assets/Prefabs/BotSettings.prefab`, `Assets/Scenes/Main.unity` (regenerated) — commit builder output.

---

### Task 1: `ComposeBusinessKnowledge` pure helper + unit tests

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` (add a static method near the business helpers, just above `EnableSave()` ~line 895)
- Test: `Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs` (create)

**Interfaces:**
- Produces: `public static string Manager.ComposeBusinessKnowledge(string description, string phone, string hours, string address, string instagram, string email)` — returns `"About Business:\n<description>"`, optionally followed by `"\n\nКонтакты:\n"` + newline-joined non-empty `"<label>: <value>"` lines in the order Телефон, Часы работы, Адрес, Instagram, Email.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs`:

```csharp
using NUnit.Framework;

public class BusinessKnowledgeComposeTests
{
    [Test]
    public void DescriptionOnly_NoContactBlock()
    {
        var result = Manager.ComposeBusinessKnowledge("Магазин", "", "", "", "", "");
        Assert.AreEqual("About Business:\nМагазин", result);
        StringAssert.DoesNotContain("Контакты:", result);
    }

    [Test]
    public void AllFields_LabeledBlockInOrder()
    {
        var result = Manager.ComposeBusinessKnowledge(
            "Магазин", "+7700", "9-19", "Алматы", "@shop", "a@b.kz");
        var expected =
            "About Business:\nМагазин\n\n" +
            "Контакты:\n" +
            "Телефон: +7700\n" +
            "Часы работы: 9-19\n" +
            "Адрес: Алматы\n" +
            "Instagram: @shop\n" +
            "Email: a@b.kz";
        Assert.AreEqual(expected, result);
    }

    [Test]
    public void PartialFields_OnlyNonEmptyLines()
    {
        var result = Manager.ComposeBusinessKnowledge(
            "Магазин", "+7700", "", "", "", "a@b.kz");
        var expected =
            "About Business:\nМагазин\n\n" +
            "Контакты:\n" +
            "Телефон: +7700\n" +
            "Email: a@b.kz";
        Assert.AreEqual(expected, result);
    }

    [Test]
    public void ContactsAllEmpty_HeaderAndDescriptionOnly()
    {
        var result = Manager.ComposeBusinessKnowledge("", "", "", "", "", "");
        Assert.AreEqual("About Business:\n", result);
    }
}
```

- [ ] **Step 2: Import the new test file**

The file is brand-new, so Unity will not compile it until the asset DB refreshes. Run `Assets/Refresh` (Editor: menu Assets ▸ Refresh, or mcp-unity `execute_menu_item` `"Assets/Refresh"`) and confirm `Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs.meta` now exists.

Run: `ls Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs.meta`
Expected: the `.meta` path is listed (file imported).

- [ ] **Step 3: Run tests to verify they fail**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: FAIL — compile error `'Manager' does not contain a definition for 'ComposeBusinessKnowledge'` (or the 4 tests error out).

- [ ] **Step 4: Implement the helper**

In `Assets/Scripts/Main/Manager.cs`, add just above `public void EnableSave()`:

```csharp
// Builds the free-text business knowledge sent to n8n as the "Business" field:
// the description (existing format) plus a labeled contact block. Empty contact
// lines — and the whole block if no contact is set — are omitted. Pure/static
// so it is unit-testable without a Manager instance.
public static string ComposeBusinessKnowledge(
    string description, string phone, string hours, string address, string instagram, string email)
{
    var builder = new System.Text.StringBuilder();
    builder.Append("About Business:\n").Append(description ?? "");

    var contactLines = new System.Collections.Generic.List<string>();
    void AddContact(string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            contactLines.Add($"{label}: {value.Trim()}");
    }
    AddContact("Телефон", phone);
    AddContact("Часы работы", hours);
    AddContact("Адрес", address);
    AddContact("Instagram", instagram);
    AddContact("Email", email);

    if (contactLines.Count > 0)
        builder.Append("\n\nКонтакты:\n").Append(string.Join("\n", contactLines));

    return builder.ToString();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS — 4/4 (`DescriptionOnly_NoContactBlock`, `AllFields_LabeledBlockInOrder`, `PartialFields_OnlyNonEmptyLines`, `ContactsAllEmpty_HeaderAndDescriptionOnly`).

- [ ] **Step 6: Commit** (with user consent)

```bash
git add Assets/Scripts/Main/Manager.cs Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs.meta
git commit -m "feat(business): add ComposeBusinessKnowledge helper + tests"
```

---

### Task 2: Declare the 5 contact fields on `BotSettings` and wire save-on-edit

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings.cs:49-52` (field declarations) and `:452-455` (`WireFields`)

**Interfaces:**
- Produces: `public EditableField BotSettings.PhoneField`, `.HoursField`, `.AddressField`, `.InstagramField`, `.EmailField` — assigned by the builder (Task 6); consumed by Manager (Tasks 3, 5) and the prefab-wiring test (Task 7).

- [ ] **Step 1: Add the serialized field references**

In `Assets/Scripts/Main/BotSettings.cs`, replace the `#region Serialized — Business / Prompt` block (lines 49-52):

```csharp
    #region Serialized — Business / Prompt
    [SerializeField] public EditableTextArea BusinessField;
    [SerializeField] public EditableTextArea PromptField;
    #endregion
```

with:

```csharp
    #region Serialized — Business / Prompt
    [SerializeField] public EditableTextArea BusinessField;
    [SerializeField] public EditableTextArea PromptField;
    [SerializeField] public EditableField PhoneField;
    [SerializeField] public EditableField HoursField;
    [SerializeField] public EditableField AddressField;
    [SerializeField] public EditableField InstagramField;
    [SerializeField] public EditableField EmailField;
    #endregion
```

- [ ] **Step 2: Wire save-on-commit for each new field**

In `WireFields()`, after the `PromptField` wiring (line 454-455), add (null-guarded, because refs are unassigned until the builder in Task 6 runs and re-stamps the prefab):

```csharp
        if (PhoneField != null)
            PhoneField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (HoursField != null)
            HoursField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (AddressField != null)
            AddressField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (InstagramField != null)
            InstagramField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (EmailField != null)
            EmailField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
```

- [ ] **Step 3: Verify it compiles**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS 4/4 (a green run proves the project still compiles after the edit; no new behavior to assert here — persistence is verified on device in Task 8).

- [ ] **Step 4: Commit** (with user consent)

```bash
git add Assets/Scripts/Main/BotSettings.cs
git commit -m "feat(business): declare 5 contact field refs + wire save-on-edit"
```

---

### Task 3: Persist the 5 keys at every Business touch-point in `Manager`

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — recreate (~416), save (~742), revert (~851), dirty-check (~909)

**Interfaces:**
- Consumes: `BotSettings.PhoneField/HoursField/AddressField/InstagramField/EmailField` (Task 2).
- Produces: per-bot PlayerPrefs keys `<botName>Phone`, `<botName>Hours`, `<botName>Address`, `<botName>Instagram`, `<botName>Email`.

Note: no seeding is added in the new-bot create flow (~1426/1464) — the wizard has no source for these, unset keys default to `""` via `GetString(key, "")`, and the dirty-check compares against `""`, so a fresh bot is correctly non-dirty.

- [ ] **Step 1: Recreate — load fields from prefs**

In `Assets/Scripts/Main/Manager.cs`, after line 416 (`recreatedBotSettings.BusinessField.Value = PlayerPrefs.GetString(recreatedBot.name + "Business", "");`) and its `PromptField` sibling (417), add:

```csharp
                recreatedBotSettings.PhoneField.Value     = PlayerPrefs.GetString(recreatedBot.name + "Phone", "");
                recreatedBotSettings.HoursField.Value     = PlayerPrefs.GetString(recreatedBot.name + "Hours", "");
                recreatedBotSettings.AddressField.Value   = PlayerPrefs.GetString(recreatedBot.name + "Address", "");
                recreatedBotSettings.InstagramField.Value = PlayerPrefs.GetString(recreatedBot.name + "Instagram", "");
                recreatedBotSettings.EmailField.Value     = PlayerPrefs.GetString(recreatedBot.name + "Email", "");
```

- [ ] **Step 2: Save — write fields to prefs**

After line 742 (`PlayerPrefs.SetString(openBot.name + "Business", openBotSettings.BusinessField.Value);`), add:

```csharp
        PlayerPrefs.SetString(openBot.name + "Phone",     openBotSettings.PhoneField.Value);
        PlayerPrefs.SetString(openBot.name + "Hours",     openBotSettings.HoursField.Value);
        PlayerPrefs.SetString(openBot.name + "Address",   openBotSettings.AddressField.Value);
        PlayerPrefs.SetString(openBot.name + "Instagram", openBotSettings.InstagramField.Value);
        PlayerPrefs.SetString(openBot.name + "Email",     openBotSettings.EmailField.Value);
```

- [ ] **Step 3: Revert on close — restore fields from prefs**

After line 851 (`openBotSettings.BusinessField.Value = PlayerPrefs.GetString(openBot.name + "Business", "");`) and its `PromptField` sibling (852), add:

```csharp
        openBotSettings.PhoneField.Value     = PlayerPrefs.GetString(openBot.name + "Phone", "");
        openBotSettings.HoursField.Value     = PlayerPrefs.GetString(openBot.name + "Hours", "");
        openBotSettings.AddressField.Value   = PlayerPrefs.GetString(openBot.name + "Address", "");
        openBotSettings.InstagramField.Value = PlayerPrefs.GetString(openBot.name + "Instagram", "");
        openBotSettings.EmailField.Value     = PlayerPrefs.GetString(openBot.name + "Email", "");
```

- [ ] **Step 4: Dirty-check — mark settings changed when a field differs**

In `EnableSave()`, the `if (...)` condition ends at line 910 with:

```csharp
            !openBotSettings.PromptField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Prompt", "")))
        {
```

Change that closing line so the five new comparisons are OR'd in before the brace:

```csharp
            !openBotSettings.PromptField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Prompt", "")) ||
            !openBotSettings.PhoneField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Phone", "")) ||
            !openBotSettings.HoursField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Hours", "")) ||
            !openBotSettings.AddressField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Address", "")) ||
            !openBotSettings.InstagramField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Instagram", "")) ||
            !openBotSettings.EmailField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Email", "")))
        {
```

- [ ] **Step 5: Verify it compiles**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS 4/4 (compile check; persistence round-trip is verified on device in Task 8).

- [ ] **Step 6: Commit** (with user consent)

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "feat(business): persist phone/hours/address/instagram/email per bot"
```

---

### Task 4: Delete the 5 keys on bot deletion

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs:197-198`

- [ ] **Step 1: Add the delete lines**

In `DeleteBot()`, after line 198 (`PlayerPrefs.DeleteKey(transform.name + "Prompt");`), add:

```csharp
            PlayerPrefs.DeleteKey(transform.name + "Phone");
            PlayerPrefs.DeleteKey(transform.name + "Hours");
            PlayerPrefs.DeleteKey(transform.name + "Address");
            PlayerPrefs.DeleteKey(transform.name + "Instagram");
            PlayerPrefs.DeleteKey(transform.name + "Email");
```

(These sit inside the existing `if (PlayerPrefs.HasKey(transform.name + "Name"))` block; `DeleteKey` on an absent key is a safe no-op.)

- [ ] **Step 2: Verify it compiles**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS 4/4.

- [ ] **Step 3: Commit** (with user consent)

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "feat(business): wipe contact keys on bot delete"
```

---

### Task 5: Feed the contact fields into the n8n `Business` payload

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — add `ComposeBusinessKnowledge(BotSettings)` overload (near the Task 1 method); swap send-sites at ~3183, ~3341, ~3582.

**Interfaces:**
- Consumes: the pure `ComposeBusinessKnowledge(string,...)` (Task 1) and `BotSettings` contact fields (Task 2).
- Produces: `public static string Manager.ComposeBusinessKnowledge(BotSettings s)`.

- [ ] **Step 1: Add the `BotSettings` overload**

In `Assets/Scripts/Main/Manager.cs`, directly beneath the Task 1 pure method, add:

```csharp
// Convenience overload: pulls the six values off a BotSettings instance.
public static string ComposeBusinessKnowledge(BotSettings s) =>
    ComposeBusinessKnowledge(
        s.BusinessField.Value, s.PhoneField.Value, s.HoursField.Value,
        s.AddressField.Value, s.InstagramField.Value, s.EmailField.Value);
```

- [ ] **Step 2: Swap CreateWhatsappWorkflow-from-Edit (line 3183)**

Replace line 3183:

```csharp
        form.AddField("Business", "About Business:\n" + openBotSettings.BusinessField.Value);
```

with:

```csharp
        form.AddField("Business", ComposeBusinessKnowledge(openBotSettings));
```

- [ ] **Step 3: Swap CreateTelegramWorkflow-from-Edit (line 3341)**

Line 3341 is identical to the old 3183. Replace:

```csharp
        form.AddField("Business", "About Business:\n" + openBotSettings.BusinessField.Value);
```

with:

```csharp
        form.AddField("Business", ComposeBusinessKnowledge(openBotSettings));
```

- [ ] **Step 4: Swap the shared Edit-workflow form (line 3582)**

Replace line 3582:

```csharp
        form.AddField("Business", openBotSettings.BusinessField.Value);
```

with:

```csharp
        form.AddField("Business", ComposeBusinessKnowledge(openBotSettings));
```

(This deliberately standardizes the Edit-workflow payload onto the `About Business:` + `Контакты:` format — a safe change since the value is free-text prompt context. The `Create…FromStart` sites at 3090/3245 stay `""`.)

- [ ] **Step 5: Verify it compiles**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS 4/4.

- [ ] **Step 6: Commit** (with user consent)

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "feat(business): send contact info to bot via Business payload"
```

---

### Task 6: Build the new UI in `BotSettingsRebuilder` and stamp the refs

**Files:**
- Modify: `Assets/Editor/BotSettingsRebuilder.cs` — `nonScrollableTabs` (488), the build call (306) + a new `BuildBusinessTab`/`ApplyKeyboard`, and the SerializedObject stamping block (346-347).

**Interfaces:**
- Consumes: `BotSettings.PhoneField/HoursField/AddressField/InstagramField/EmailField` property names (Task 2), `CreateEditableField`, `BuildBusinessOrPromptTab`, `AddSectionHeader`, `Sv`, `RewireEditableField` (existing).

- [ ] **Step 1: Make the Business tab scrollable**

At line 488 change:

```csharp
        var nonScrollableTabs = new HashSet<string> { "Business", "Prompt" };
```

to:

```csharp
        var nonScrollableTabs = new HashSet<string> { "Prompt" };
```

- [ ] **Step 2: Add `BuildBusinessTab` and `ApplyKeyboard`**

Immediately after `BuildBusinessOrPromptTab` (ends line 602), add:

```csharp
    private struct BusinessTabRefs
    {
        public EditableTextArea description;
        public EditableField phone;
        public EditableField hours;
        public EditableField address;
        public EditableField instagram;
        public EditableField email;
    }

    private static BusinessTabRefs BuildBusinessTab(GameObject tab, FocusScrim scrim)
    {
        var refs = new BusinessTabRefs
        {
            description = BuildBusinessOrPromptTab(tab, "ОПИСАНИЕ БИЗНЕСА", "Описание", scrim)
        };

        AddSectionHeader(tab, "КОНТАКТЫ И ИНФОРМАЦИЯ");
        refs.phone     = CreateEditableField(tab, "Телефон",     scrim, multiline: false);
        refs.hours     = CreateEditableField(tab, "Часы работы", scrim, multiline: false);
        refs.address   = CreateEditableField(tab, "Адрес",       scrim, multiline: false);
        refs.instagram = CreateEditableField(tab, "Instagram",   scrim, multiline: false);
        refs.email     = CreateEditableField(tab, "Email",       scrim, multiline: false);

        ApplyKeyboard(refs.phone, TMP_InputField.ContentType.Standard, TouchScreenKeyboardType.PhonePad);
        ApplyKeyboard(refs.email, TMP_InputField.ContentType.EmailAddress, TouchScreenKeyboardType.EmailAddress);
        return refs;
    }

    private static void ApplyKeyboard(
        EditableField field, TMP_InputField.ContentType contentType, TouchScreenKeyboardType keyboard)
    {
        var input = field.GetComponentInChildren<TMP_InputField>();
        if (input == null) return;
        input.contentType = contentType;
        input.keyboardType = keyboard;
    }
```

(If `using TMPro;` is not already at the top of the file, add it.)

- [ ] **Step 3: Call `BuildBusinessTab` instead of the generic builder**

Replace line 306:

```csharp
            var businessField = BuildBusinessOrPromptTab(tabs["Business"].content, "ОПИСАНИЕ БИЗНЕСА", "Описание", mainScrim);
```

with:

```csharp
            var businessRefs = BuildBusinessTab(tabs["Business"].content, mainScrim);
```

- [ ] **Step 4: Stamp all Business refs**

Replace line 346:

```csharp
            so.FindProperty("BusinessField").objectReferenceValue = businessField;
```

with:

```csharp
            so.FindProperty("BusinessField").objectReferenceValue  = businessRefs.description;
            so.FindProperty("PhoneField").objectReferenceValue     = businessRefs.phone;
            so.FindProperty("HoursField").objectReferenceValue     = businessRefs.hours;
            so.FindProperty("AddressField").objectReferenceValue   = businessRefs.address;
            so.FindProperty("InstagramField").objectReferenceValue = businessRefs.instagram;
            so.FindProperty("EmailField").objectReferenceValue     = businessRefs.email;
```

- [ ] **Step 5: Verify the editor assembly compiles**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS 4/4 (a green run requires `Assembly-CSharp-Editor` — which contains the builder — to compile). If it fails with a `BotSettingsRebuilder.cs` compile error, fix before proceeding.

- [ ] **Step 6: Commit** (with user consent)

```bash
git add Assets/Editor/BotSettingsRebuilder.cs
git commit -m "feat(business): build contact fields into Business tab (scrollable)"
```

---

### Task 7: Regenerate the prefab, verify the wiring, commit the UI churn

**Files:**
- Regenerate: `Assets/Prefabs/BotSettings.prefab`, `Assets/Scenes/Main.unity`
- Modify: `Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs` (add the prefab-wiring guard)

- [ ] **Step 1: Run the rebuild + scrollable builders (Editor open)**

Run the menu items in order (Editor menu, or mcp-unity `execute_menu_item`):
1. `Tools/Rebuild Bot Settings Prefabs`
2. `Tools/BotSettings/Build Scrollable Business+Prompt`

Then save the scene (`File ▸ Save`, or mcp-unity `save_scene`).

- [ ] **Step 2: Add the prefab-wiring guard test**

Append to `Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs` (inside the class):

```csharp
    [Test]
    public void BotSettingsPrefab_HasAllContactFieldRefs()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
            "Assets/Prefabs/BotSettings.prefab");
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");

        var settings = prefab.GetComponent<BotSettings>();
        Assert.IsNotNull(settings, "BotSettings component missing on prefab");
        Assert.IsNotNull(settings.BusinessField,  "BusinessField not wired");
        Assert.IsNotNull(settings.PhoneField,     "PhoneField not wired");
        Assert.IsNotNull(settings.HoursField,     "HoursField not wired");
        Assert.IsNotNull(settings.AddressField,   "AddressField not wired");
        Assert.IsNotNull(settings.InstagramField, "InstagramField not wired");
        Assert.IsNotNull(settings.EmailField,     "EmailField not wired");
    }
```

- [ ] **Step 3: Run the guard (and full compose file) to prove the refs stamped**

Run: `Tools/run-tests-headless.sh 'BusinessKnowledgeComposeTests'`
Expected: PASS 5/5 including `BotSettingsPrefab_HasAllContactFieldRefs`. A failure here means the SerializedObject stamping (Task 6 Step 4) did not take — re-run the builder; do not hand-edit the prefab.

- [ ] **Step 4: Commit the regenerated assets + guard test** (with user consent)

```bash
git add Assets/Prefabs/BotSettings.prefab Assets/Prefabs/BotSettings.prefab.meta Assets/Scenes/Main.unity Assets/Tests/Editor/BusinessKnowledgeComposeTests.cs
git commit -m "feat(business): regenerate BotSettings prefab with contact fields"
```

(Commit immediately — an uncommitted scene/prefab add is vulnerable to clobber by a parallel session saving `Main.unity`. If `git status` shows large unrelated `Main.unity` churn, that layout-zeroing/material-regen diff is benign; verify the `BotSettings` prefab carries the 5 new `EditableField` children by GUID grep if in doubt.)

---

### Task 8: Full regression + on-device verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full EditMode suite**

Run: `Tools/run-tests-headless.sh`
Expected: PASS — the prior baseline (≈1214) + 5 new tests, 0 failures. Investigate any regression before shipping.

- [ ] **Step 2: On-device / Editor Play-mode manual checklist**

Build to a device (or Play in Editor) and confirm:
- The «Бизнес» tab shows `Описание` then a `КОНТАКТЫ И ИНФОРМАЦИЯ` section with Телефон, Часы работы, Адрес, Instagram, Email.
- The tab **scrolls** with all six cards; dragging over the description scrolls the tab, tapping it edits, and a long description still scrolls internally.
- Tapping `Телефон` shows a numeric/phone keypad; `Email` shows the email keyboard.
- Editing any field enables the Save button (dirty-check); closing without saving reverts it (revert path).
- Values survive Save → close → reopen and an app restart (recreate path).
- Deleting the bot removes the values (create a second bot, confirm no cross-read of the first bot's contacts).
- With the bot workflow activated **for a supervised test only**, the bot answers a contact question (e.g. «во сколько работаете?») using the entered hours — confirming the payload reached n8n.

- [ ] **Step 3: Report results**

Summarize pass/fail per checklist item with the actual test output. Do not claim completion without the device pass (UI/persistence are not covered by the automated suite).

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- Field set (5 fields) → Tasks 2, 6. UI section + scrollable tab → Task 6. `EditableField` primitive + keyboards → Task 6.
- Data model / 6 lifecycle touch-points → Tasks 3 (recreate/save/revert/dirty) + 4 (delete); create-seed intentionally omitted (documented in Task 3).
- Feed-the-bot compose helper + 3 send-sites, no n8n change → Tasks 1 + 5. Bot-card subtitle unchanged → untouched by design (no task edits lines 384/722).
- Testing (compose unit tests, prefab-wiring guard, manual/device) → Tasks 1, 7, 8.
- Risks (serialized-ref wipe, scene clobber, new-file import, nested scroll) → Global Constraints + Tasks 6/7 + Task 8 checklist.

**2. Placeholder scan** — no TBD/TODO; every code step shows complete code; every run step shows the exact command and expected result.

**3. Type consistency** — `ComposeBusinessKnowledge(string,string,string,string,string,string)` (Task 1) and the `ComposeBusinessKnowledge(BotSettings)` overload (Task 5) match their call-sites; field names `PhoneField/HoursField/AddressField/InstagramField/EmailField` are identical across BotSettings declaration (Task 2), Manager persistence (Task 3), the compose overload (Task 5), the builder `FindProperty` stamps (Task 6), and the prefab guard test (Task 7). Compose label/format strings in the impl (Task 1 Step 4) are byte-identical to the test expectations (Task 1 Step 1).
