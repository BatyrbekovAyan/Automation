# Bot Switcher Avatar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the empty `Avatar` slot in the WhatsApp header's `BotSwitcherTitle` and in each `BotSwitcherRowView` row with the active bot's business identity badge — a tinted circle plus the business icon sprite.

**Architecture:** The data source is `BusinessTypesSO`, keyed by the per-bot PlayerPrefs `{botId}+"BusinessType"` string — same source already feeding `Bot.BotIconTile` / `Bot.BotIconImage` on the BotsPage card. Two new getters on `Bot` (`GetBusinessIconSprite`/`GetBusinessIconTint`) centralize the lookup. `BotSwitcherTitleBinder` and `BotSwitcherRowView` consume those getters. Two new surgical editor menu items restructure the existing `Avatar` GameObject to a tile + child `IconSprite` hierarchy with `ImageWithRoundedCorners`, leaving prior post-build customizations on the title and row intact.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, TextMeshPro, `Nobi.UiRoundedCorners.ImageWithRoundedCorners` (already in project), URP. No new packages.

**Reference spec:** [docs/superpowers/specs/2026-05-08-bot-switcher-avatar-design.md](../specs/2026-05-08-bot-switcher-avatar-design.md)

---

## File Structure

| File | Role |
| --- | --- |
| `Assets/Scripts/Main/Bot.cs` | Owner of `BusinessTypesSO` reference and PlayerPrefs key conventions. Hosts the two new accessors so callers don't hold their own SO ref. |
| `Assets/Scripts/UI/BotSwitcherTitleBinder.cs` | Runtime binder for the Whatsapp header title — already updates the bot name, now also updates the avatar tile + icon on `OnEnable` and on `OnActiveBotChanged`. |
| `Assets/Scripts/UI/BotSwitcherRowView.cs` | Runtime view for each row in the bot switcher bottom sheet — `Bind()` populates name/status/highlight; now also populates tile + icon. |
| `Assets/Editor/BotSwitcherTitleAvatarRebuilder.cs` (new) | Surgical menu item `Tools/Bot Switcher/Rebuild Title Avatar`. Scoped only to the title's Avatar GameObject. |
| `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs` (new) | Surgical menu item `Tools/Bot Switcher/Rebuild Row Avatar`. Scoped only to the row template's Avatar GameObject. |

The existing `Assets/Editor/Screen_WhatsappHeaderRebuilder.cs` and `Assets/Editor/BotSwitcherSheetBuilder.cs` are **not** modified — the user has post-build customizations there and the existing `FindProperty("avatarImage")` wiring still resolves because we keep `avatarImage` as a field name.

---

## Verification approach

This is a Unity project with no automated test framework set up. Verification per task is:

1. **C# compile check** — the `.claude/hooks/validate-cs.sh` hook runs on every Edit/Write. Unity itself reports compile errors in its Console when focused. After each code change, switch to Unity briefly and confirm the Console has no errors.
2. **Editor menu items** — after creating each new menu item, run it from the Unity menu bar and confirm: no errors, the rebuilt Avatar appears as expected in the Hierarchy, the binder/row view's serialized fields are populated.
3. **End-to-end** — final task drives the feature in Play mode.

When a step says "Verify Unity compiles", that means: switch to Unity, watch the bottom-right spinner finish, then check the Console tab is empty of red errors.

---

## Task 1: Add `Bot` business-icon accessors

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs:23-27` (just below the existing `Header("Business Icon")` block) and append two methods + one constant near `RefreshBusinessIcon` (line 261).

These two getters centralize the BusinessTypesSO lookup so the title binder and row view don't need their own `BusinessTypesSO` reference. They handle the unset/missing cases by returning a neutral fallback rather than null/zero, so the avatar always renders something.

- [ ] **Step 1: Add the `NeutralTile` constant + the two accessors to `Bot.cs`**

Insert these three members at the top of the existing `Bot` class, immediately after the `[Header("Business Icon")]` block (right after line 27 `[SerializeField] private BusinessTypesSO businessTypes;`):

```csharp
    private static readonly Color NeutralTile = new Color(0.85f, 0.85f, 0.85f);

    /// <summary>
    /// Returns the bot's business icon sprite, or null when no business type
    /// is set (mid-wizard) or the SO has no entry for the saved id. Cheap —
    /// PlayerPrefs read + dictionary lookup; safe to call from OnEnable.
    /// </summary>
    public Sprite GetBusinessIconSprite()
    {
        if (businessTypes == null) return null;
        var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
        if (string.IsNullOrEmpty(id)) return null;
        return businessTypes.TryGetById(id, out var entry) ? entry.sprite : null;
    }

    /// <summary>
    /// Returns the bot's business icon tile color, or NeutralTile when no
    /// business type is set or the SO has no matching entry. Callers can
    /// always assign the result to an Image.color without null-checking.
    /// </summary>
    public Color GetBusinessIconTint()
    {
        if (businessTypes == null) return NeutralTile;
        var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
        if (string.IsNullOrEmpty(id)) return NeutralTile;
        return businessTypes.TryGetById(id, out var entry) ? entry.tileColor : NeutralTile;
    }
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity. Wait for the recompile spinner to clear. Open the Console tab and confirm there are no red errors.

If the existing `BusinessTypesSO.TryGetById(string id, out var entry)` signature differs from what's used here, fix to match — that method is already called from `Bot.ApplyBusinessIcon` (line 273), so the signature is established.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "feat(bot): add GetBusinessIconSprite/Tint accessors

Centralize BusinessTypesSO lookup on Bot so callers (BotSwitcherTitleBinder,
BotSwitcherRowView) don't need their own SO reference. Returns a neutral
gray fallback when business type is unset, so avatars always render."
```

---

## Task 2: Wire avatar into `BotSwitcherTitleBinder`

**Files:**
- Modify: `Assets/Scripts/UI/BotSwitcherTitleBinder.cs` (full rewrite — file is 56 lines)

Add two serialized Image fields and an `ApplyAvatar` helper. `UpdateTitle` already runs on `OnEnable` and on every `OnActiveBotChanged`, so we hook the avatar refresh in there next to the existing name refresh — no new event subscriptions.

- [ ] **Step 1: Replace the file contents**

Overwrite `Assets/Scripts/UI/BotSwitcherTitleBinder.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class BotSwitcherTitleBinder : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image avatarIcon;

    private static readonly Color NeutralTile = new Color(0.85f, 0.85f, 0.85f);

    private Button rowButton;

    private void Awake()
    {
        if (nameLabel == null)
        {
            Transform t = transform.Find("BotName");
            if (t != null) nameLabel = t.GetComponent<TextMeshProUGUI>();
        }

        rowButton = GetComponent<Button>();
        if (rowButton != null)
        {
            BotSwitcherSheet sheet = FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include);
            if (sheet != null)
            {
                rowButton.onClick.RemoveAllListeners();
                rowButton.onClick.AddListener(sheet.Open);
            }
        }
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged += UpdateTitle;
            UpdateTitle(ChatManager.Instance.CurrentBotId);
        }
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged -= UpdateTitle;
        }
    }

    private void UpdateTitle(string botId)
    {
        Bot bot = !string.IsNullOrEmpty(botId) && Manager.Instance != null
            ? Manager.Instance.FindBotByName(botId) : null;

        if (nameLabel != null)
            nameLabel.text = bot != null ? PlayerPrefs.GetString(botId + "Name", botId) : "Bot";

        ApplyAvatar(bot);
    }

    private void ApplyAvatar(Bot bot)
    {
        if (avatarImage != null)
            avatarImage.color = bot != null ? bot.GetBusinessIconTint() : NeutralTile;

        if (avatarIcon != null)
        {
            Sprite sprite = bot != null ? bot.GetBusinessIconSprite() : null;
            avatarIcon.sprite = sprite;
            avatarIcon.enabled = sprite != null;
        }
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity. Wait for the recompile. Confirm the Console has no red errors.

The serialized fields `avatarImage` and `avatarIcon` will appear in the Inspector when you select the `BotSwitcherTitle` GameObject — they'll be empty for now; Task 4's editor menu wires them.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/BotSwitcherTitleBinder.cs
git commit -m "feat(ui): bot avatar in BotSwitcherTitleBinder

Add avatarImage (tile) and avatarIcon serialized fields. Resolve the
active Bot via Manager.FindBotByName, apply business tint to avatarImage
and business sprite to avatarIcon. Refresh runs on OnEnable and on every
OnActiveBotChanged, same path as the existing name refresh — no new
event subscriptions. Inspector refs wired by the new title-avatar
rebuilder menu item (next task)."
```

---

## Task 3: Wire avatar into `BotSwitcherRowView`

**Files:**
- Modify: `Assets/Scripts/UI/BotSwitcherRowView.cs` (full rewrite — file is 79 lines)

Drop the unused `avatarFallback` field. Keep `avatarImage` (now semantically the tile — preserves the existing `BotSwitcherSheetBuilder.cs:262` wiring). Add `avatarIcon`. Replace the "future avatar fetcher will overwrite" placeholder with a real call to the `Bot` getters.

- [ ] **Step 1: Replace the file contents**

Overwrite `Assets/Scripts/UI/BotSwitcherRowView.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

public class BotSwitcherRowView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image avatarIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI subLineLabel;
    [SerializeField] private Image statusDot;
    [SerializeField] private Image selectedBackground;
    [SerializeField] private Image selectedAccentBar;
    [SerializeField] private Button rowButton;

    [Header("Style")]
    [SerializeField] private Color statusConnectedColor = new Color(0.13f, 0.78f, 0.42f);
    [SerializeField] private Color statusDisconnectedColor = new Color(0.6f, 0.6f, 0.6f);

    private static readonly Color NeutralTile = new Color(0.85f, 0.85f, 0.85f);

    private string botId;
    private System.Action<string> onTap;

    public void Bind(Bot bot, bool isSelected, System.Action<string> tapHandler)
    {
        if (bot == null) return;

        botId = bot.transform.name;
        onTap = tapHandler;

        string botDisplayName = PlayerPrefs.GetString(botId + "Name", botId);
        if (nameLabel != null)
        {
            nameLabel.text = botDisplayName;
            nameLabel.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
        }

        bool waConnected = !string.IsNullOrEmpty(bot.whatsappProfileId)
                           && bot.whatsappProfileId != Bot.UnauthedProfileSentinel;
        if (subLineLabel != null)
        {
            subLineLabel.text = waConnected ? "WhatsApp connected" : "WhatsApp not connected";
        }
        if (statusDot != null)
        {
            statusDot.color = waConnected ? statusConnectedColor : statusDisconnectedColor;
        }

        if (avatarImage != null)
            avatarImage.color = bot.GetBusinessIconTint();

        if (avatarIcon != null)
        {
            Sprite sprite = bot.GetBusinessIconSprite();
            avatarIcon.sprite = sprite;
            avatarIcon.enabled = sprite != null;
        }

        if (selectedBackground != null) selectedBackground.gameObject.SetActive(isSelected);
        if (selectedAccentBar != null) selectedAccentBar.gameObject.SetActive(isSelected);

        if (rowButton != null)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(HandleTap);
        }
    }

    private void HandleTap()
    {
        if (string.IsNullOrEmpty(botId)) return;

        transform.DOPunchScale(Vector3.one * 0.04f, 0.18f, 1, 0.5f);
        onTap?.Invoke(botId);
    }

    private void OnDestroy()
    {
        if (rowButton != null) rowButton.onClick.RemoveAllListeners();
    }
}
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity. Wait for the recompile. Confirm the Console has no red errors.

The `BotSwitcherSheetBuilder.cs:262` line `so.FindProperty("avatarImage").objectReferenceValue = avImage;` continues to resolve — `avatarImage` still exists. The new `avatarIcon` field is unwired until Task 5's row-avatar rebuilder runs; until then `avatarIcon` is null and `Bind` skips the icon write defensively.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/BotSwitcherRowView.cs
git commit -m "feat(ui): bot avatar in BotSwitcherRowView

Drop unused avatarFallback. Reuse avatarImage as the tile (color
overwritten with Bot.GetBusinessIconTint per row). Add avatarIcon for
the foreground business glyph (sprite from Bot.GetBusinessIconSprite,
component disabled when null). Field name avatarImage retained so the
existing BotSwitcherSheetBuilder wiring still resolves; Task 5's
surgical rebuilder upgrades the row's Avatar GameObject and wires
avatarIcon."
```

---

## Task 4: Create `BotSwitcherTitleAvatarRebuilder` editor menu

**Files:**
- Create: `Assets/Editor/BotSwitcherTitleAvatarRebuilder.cs`

Surgical menu item that operates only on `Screen_Whatsapp/ChatsPanel/TopBar/BotSwitcherTitle/Avatar`. Preserves the existing RectTransform / LayoutElement values (so the user's 44×44 sizing stays). Replaces only the visual config and child structure, then wires the binder's two new fields.

- [ ] **Step 1: Create the file**

Write `Assets/Editor/BotSwitcherTitleAvatarRebuilder.cs`:

```csharp
#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical rebuilder for ONLY the Avatar GameObject inside the WhatsApp
/// header's BotSwitcherTitle. Does not touch BotName, Chevron, or any
/// other sibling — Screen_WhatsappHeaderRebuilder owns the title shell;
/// this menu owns just the Avatar's visual config and child hierarchy.
///
/// Preserves the existing Avatar's RectTransform (size, anchors, position)
/// and LayoutElement (preferredWidth/Height) so post-build sizing tweaks
/// survive. Re-run this after resizing the Avatar to refresh the rounded
/// corner radius.
/// </summary>
public static class BotSwitcherTitleAvatarRebuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string HeaderChildName = "TopBar";
    private const string TitleName = "BotSwitcherTitle";
    private const string AvatarName = "Avatar";
    private const string IconChildName = "IconSprite";
    private const float IconChildScale = 0.64f;

    [MenuItem("Tools/Bot Switcher/Rebuild Title Avatar")]
    public static void Rebuild()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[BotSwitcherTitleAvatarRebuilder] Could not find '{ScreenName}' in any open scene. Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        Transform header = chatsPanel != null ? chatsPanel.Find(HeaderChildName) : null;
        Transform title = header != null ? header.Find(TitleName) : null;
        Transform avatar = title != null ? title.Find(AvatarName) : null;
        if (avatar == null)
        {
            Debug.LogError($"[BotSwitcherTitleAvatarRebuilder] Path '{ScreenName}/{ChatsPanelName}/{HeaderChildName}/{TitleName}/{AvatarName}' not found. Run 'Tools/Bot Switcher/Rebuild Whatsapp Header' first to create the title shell.");
            return;
        }

        BotSwitcherTitleBinder binder = title.GetComponent<BotSwitcherTitleBinder>();
        if (binder == null)
        {
            Debug.LogError($"[BotSwitcherTitleAvatarRebuilder] '{TitleName}' has no BotSwitcherTitleBinder. Re-run 'Tools/Bot Switcher/Rebuild Whatsapp Header' to attach it.");
            return;
        }

        // 1. Tile Image — use existing if present, else add. Preserve color
        //    only loosely (runtime overwrites it); ensure raycastTarget on
        //    so the parent button still receives clicks through this child.
        Image tileImage = avatar.GetComponent<Image>();
        if (tileImage == null) tileImage = avatar.gameObject.AddComponent<Image>();
        // Unity's Image renders nothing without a sprite. Built-in UISprite is
        // a flat 9-sliced rounded rect; ImageWithRoundedCorners then masks it
        // to a true circle at our chosen radius. The tile color is overwritten
        // at runtime by the binder/row view per the active bot's business type.
        if (tileImage.sprite == null)
            tileImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        tileImage.type = Image.Type.Simple;
        tileImage.color = new Color(0.85f, 0.85f, 0.85f);
        tileImage.raycastTarget = true;

        // 2. ImageWithRoundedCorners — radius derives from current size so
        //    the user's 44×44 (or any size) becomes a circle automatically.
        var roundedExisting = avatar.GetComponents<ImageWithRoundedCorners>();
        for (int i = 1; i < roundedExisting.Length; i++) Object.DestroyImmediate(roundedExisting[i]);
        ImageWithRoundedCorners rounded = avatar.GetComponent<ImageWithRoundedCorners>();
        if (rounded == null) rounded = avatar.gameObject.AddComponent<ImageWithRoundedCorners>();
        RectTransform avRT = avatar.GetComponent<RectTransform>();
        rounded.radius = avRT.sizeDelta.x * 0.5f;
        rounded.Validate();
        rounded.Refresh();

        // 3. Wipe children, create the IconSprite child centered at 64% size.
        for (int i = avatar.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(avatar.GetChild(i).gameObject);
        }

        GameObject iconGO = new GameObject(IconChildName, typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(avatar, false);
        RectTransform iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = avRT.sizeDelta * IconChildScale;
        Image iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = null;
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

        // 4. Wire the binder's avatarImage + avatarIcon — leave nameLabel alone.
        var so = new SerializedObject(binder);
        so.FindProperty("avatarImage").objectReferenceValue = tileImage;
        so.FindProperty("avatarIcon").objectReferenceValue = iconImage;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(binder);
        EditorUtility.SetDirty(avatar);
        EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
        Selection.activeGameObject = avatar.gameObject;

        Debug.Log($"[BotSwitcherTitleAvatarRebuilder] Rebuilt {AvatarName} at radius {rounded.radius:F1}px (size {avRT.sizeDelta.x:F0}×{avRT.sizeDelta.y:F0}). Re-run after resizing.");
    }

    private static GameObject FindGameObjectByNameIncludeInactive(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name) return all[i].gameObject;
        }
        return null;
    }
}
#endif
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity. Wait for the recompile. Confirm the Console has no red errors.

The `using Nobi.UiRoundedCorners;` import works because `MessageItemView.cs` already uses it.

- [ ] **Step 3: Run the menu item and verify the result**

In the Unity Editor:

1. Make sure the Main scene is open.
2. Click `Tools → Bot Switcher → Rebuild Title Avatar`.
3. Look at the Console — you should see a single log line: `[BotSwitcherTitleAvatarRebuilder] Rebuilt Avatar at radius X px ...`
4. In the Hierarchy, navigate to `Screen_Whatsapp/ChatsPanel/TopBar/BotSwitcherTitle/Avatar`. It should now have:
   - An `Image` component (sprite=None, color=light gray, raycastTarget=on)
   - An `ImageWithRoundedCorners` component with `radius = half of width`
   - One child named `IconSprite` with an `Image` (sprite=None, raycastTarget=off, preserveAspect=on)
5. Select `BotSwitcherTitle` in the Hierarchy and confirm the `Bot Switcher Title Binder` Inspector now shows the `Avatar Image` field pointing at `Avatar`'s Image and the `Avatar Icon` field pointing at `IconSprite`'s Image.
6. The `BotName` and `Chevron` siblings should be unchanged.

If any of those checks fail, do not commit — fix the menu item and re-run.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/BotSwitcherTitleAvatarRebuilder.cs
git commit -m "feat(editor): BotSwitcherTitleAvatarRebuilder menu item

Surgical rebuild of the Avatar GameObject inside BotSwitcherTitle —
preserves RectTransform/LayoutElement so post-build sizing survives,
adds ImageWithRoundedCorners with radius=half-width for circle, creates
single centered IconSprite child at 64% size, wires the binder's
avatarImage + avatarIcon. Does not touch BotName/Chevron siblings."
```

---

## Task 5: Create `BotSwitcherRowAvatarRebuilder` editor menu

**Files:**
- Create: `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs`

> **Pivot during execution:** The original plan assumed the row template lived as an inactive in-scene holder under `Canvas/BotSwitcherRowPrefabHolder/...`. Mid-implementation we discovered the user had extracted it into a standalone `Assets/Prefabs/BotSwitcherRow.prefab` asset. Task 5 was rewritten to operate on the prefab asset via `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` instead of editing the scene. End behavior is identical from the user's perspective; the route differs because the row template now lives in a different place. The two earlier subagent reviews approved the original scene-based version; the rewrite was reviewed by the controller before commit.

Same surgical pattern as Task 4 — only restructures the Avatar GameObject (Image, `ImageWithRoundedCorners`, single `IconSprite` child) and rewires `avatarImage` + `avatarIcon` on `BotSwitcherRowView`. Leaves `nameLabel`, `subLineLabel`, `statusDot`, `selectedBackground`, `selectedAccentBar`, `rowButton` untouched. Runs against whatever prefab `BotSwitcherSheet.rowPrefab` points at, so it follows wherever the user keeps the prefab in `Assets/`.

- [ ] **Step 1: Create the file**

Write `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs`:

```csharp
#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical rebuilder for ONLY the Avatar GameObject inside the bot switcher
/// row prefab asset. Resolves the prefab via BotSwitcherSheet.rowPrefab in
/// the open scene, loads it via PrefabUtility, restructures the Avatar, and
/// saves the prefab back to disk. Scene instances inherit the change
/// automatically through the prefab system — no scene edit needed.
///
/// Does not touch SelectedBackground, SelectedAccentBar, the Stack/Name/SubLine
/// subtree, or rowButton — only the Avatar's visual config and child hierarchy.
/// Preserves the existing Avatar's RectTransform and LayoutElement so
/// post-build sizing tweaks survive. Re-run after resizing to refresh the
/// rounded corner radius.
/// </summary>
public static class BotSwitcherRowAvatarRebuilder
{
    private const string AvatarName = "Avatar";
    private const string IconChildName = "IconSprite";
    private const float IconChildScale = 0.64f;

    [MenuItem("Tools/Bot Switcher/Rebuild Row Avatar")]
    public static void Rebuild()
    {
        // 1. Locate a BotSwitcherSheet in any open scene to discover the row prefab reference.
        BotSwitcherSheet sheet = Object.FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include);
        if (sheet == null)
        {
            Debug.LogError("[BotSwitcherRowAvatarRebuilder] No BotSwitcherSheet found in any open scene. Open the Main scene first.");
            return;
        }

        // 2. Read the rowPrefab serialized reference and resolve its asset path.
        var sheetSO = new SerializedObject(sheet);
        SerializedProperty rowPrefabProp = sheetSO.FindProperty("rowPrefab");
        if (rowPrefabProp == null || rowPrefabProp.objectReferenceValue == null)
        {
            Debug.LogError("[BotSwitcherRowAvatarRebuilder] BotSwitcherSheet.rowPrefab is unwired. Wire it to a BotSwitcherRow prefab asset in the Inspector first.");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(rowPrefabProp.objectReferenceValue);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError($"[BotSwitcherRowAvatarRebuilder] BotSwitcherSheet.rowPrefab references '{rowPrefabProp.objectReferenceValue.name}', which is not a saved prefab asset. Save the row template as a prefab in Assets/ and re-wire the field.");
            return;
        }

        // 3. Load the prefab in an editable in-memory scene.
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            BotSwitcherRowView rowView = prefabRoot.GetComponent<BotSwitcherRowView>();
            if (rowView == null)
            {
                Debug.LogError($"[BotSwitcherRowAvatarRebuilder] Prefab at '{prefabPath}' has no BotSwitcherRowView component on the root.");
                return;
            }

            Transform avatar = prefabRoot.transform.Find(AvatarName);
            if (avatar == null)
            {
                Debug.LogError($"[BotSwitcherRowAvatarRebuilder] Prefab at '{prefabPath}' has no child named '{AvatarName}'. Re-run 'Tools/Bot Switcher/Build Sheet' to regenerate the row template.");
                return;
            }

            // 4. Tile Image — use existing if present, else add. UISprite is a flat
            //    9-sliced rounded rect; ImageWithRoundedCorners then masks it to a
            //    true circle. The tile color is overwritten at runtime per active bot.
            Image tileImage = avatar.GetComponent<Image>();
            if (tileImage == null) tileImage = avatar.gameObject.AddComponent<Image>();
            if (tileImage.sprite == null)
                tileImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            tileImage.type = Image.Type.Simple;
            tileImage.color = new Color(0.85f, 0.85f, 0.85f);
            tileImage.raycastTarget = true;

            // 5. ImageWithRoundedCorners — radius from current size.
            var roundedExisting = avatar.GetComponents<ImageWithRoundedCorners>();
            for (int i = 1; i < roundedExisting.Length; i++) Object.DestroyImmediate(roundedExisting[i]);
            ImageWithRoundedCorners rounded = avatar.GetComponent<ImageWithRoundedCorners>();
            if (rounded == null) rounded = avatar.gameObject.AddComponent<ImageWithRoundedCorners>();
            RectTransform avRT = avatar.GetComponent<RectTransform>();
            rounded.radius = avRT.sizeDelta.x * 0.5f;
            rounded.Validate();
            rounded.Refresh();

            // 6. Wipe children, create IconSprite at 64% size.
            for (int i = avatar.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(avatar.GetChild(i).gameObject);
            }

            GameObject iconGO = new GameObject(IconChildName, typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(avatar, false);
            RectTransform iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.5f, 0.5f);
            iconRT.anchorMax = new Vector2(0.5f, 0.5f);
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.anchoredPosition = Vector2.zero;
            iconRT.sizeDelta = avRT.sizeDelta * IconChildScale;
            Image iconImage = iconGO.GetComponent<Image>();
            iconImage.sprite = null;
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;

            // 7. Wire avatarImage + avatarIcon on BotSwitcherRowView. Leave
            //    nameLabel/subLineLabel/statusDot/selectedBackground/etc. alone.
            var so = new SerializedObject(rowView);
            so.FindProperty("avatarImage").objectReferenceValue = tileImage;
            so.FindProperty("avatarIcon").objectReferenceValue = iconImage;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 8. Save the prefab back. Scene instances pick up the change via the
            //    prefab system — no scene edit, no MarkSceneDirty.
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

            Debug.Log($"[BotSwitcherRowAvatarRebuilder] Rebuilt {AvatarName} at radius {rounded.radius:F1}px (size {avRT.sizeDelta.x:F0}×{avRT.sizeDelta.y:F0}) in '{prefabPath}'. Re-run after resizing.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
#endif
```

- [ ] **Step 2: Verify Unity compiles**

Switch to Unity. Wait for the recompile. Confirm the Console has no red errors.

- [ ] **Step 3: Run the menu item and verify the result**

In the Unity Editor:

1. Make sure the Main scene is open and the `BotSwitcherSheet` in the scene has its `rowPrefab` Inspector field wired to `Assets/Prefabs/BotSwitcherRow.prefab` (or wherever the row prefab asset lives).
2. Click `Tools → Bot Switcher → Rebuild Row Avatar`.
3. Look at the Console — you should see: `[BotSwitcherRowAvatarRebuilder] Rebuilt Avatar at radius X px ... in 'Assets/Prefabs/BotSwitcherRow.prefab'`
4. Open the prefab (double-click the asset). Inside, navigate to the `Avatar` child of the root. It should now have:
   - An `Image` component (sprite=`UI/Skin/UISprite`, color=light gray, raycastTarget=on)
   - An `ImageWithRoundedCorners` component with `radius = half of Avatar's width`
   - One child named `IconSprite` with an `Image` (sprite=None, raycastTarget=off, preserveAspect=on)
5. Still in prefab edit mode, select the root `BotSwitcherRow` and confirm the `Bot Switcher Row View` Inspector shows the `Avatar Image` field pointing at the Avatar's Image and the new `Avatar Icon` field pointing at `IconSprite`'s Image.
6. `SelectedBackground`, `SelectedAccentBar`, `Name`, `SubLine/StatusDot`, `SubLine/SubText`, `rowButton` should be unchanged.

If any of those fail, fix the menu item before committing.

- [ ] **Step 4: Commit the prefab change**

The menu item modifies the prefab asset, not the scene — so the diff is on `Assets/Prefabs/BotSwitcherRow.prefab` (and possibly `.meta` if Unity touched it). Commit:

```bash
git add Assets/Prefabs/BotSwitcherRow.prefab
git commit -m "scene(prefab): wire bot-switcher row avatar after rebuild

Ran Tools/Bot Switcher/Rebuild Row Avatar to drop the old avatar Image
config and populate the new avatarImage (tile) + avatarIcon refs on
BotSwitcherRowView, plus add the centered IconSprite child. Scene
instances pick up the change automatically via the prefab system."
```

(The C# file `BotSwitcherRowAvatarRebuilder.cs` itself was already committed when Task 5 was implemented.)

---

## Task 6: End-to-end verification in the Unity Editor

**Files:** none (manual)

This task drives the feature in Play mode against real bot data. The previous tasks only proved compile + Inspector wiring — this proves the avatar actually renders the right sprite and tint at runtime.

- [ ] **Step 1: Pre-flight scene check**

In the Unity Editor:

1. Open the Main scene.
2. Confirm Task 4 and Task 5 menu items have been run (the Avatar GameObjects in the title and in the row prefab have `IconSprite` children and the binder/row view fields are populated).
3. Confirm there is at least one bot in the scene with a `BusinessType` set in PlayerPrefs. If not, you can either:
   - Run the bot creation wizard end-to-end so a bot lands with a business type, or
   - Set one manually: with Play mode stopped, find an existing `Bot{N}` GameObject under `BotsParent`, then call `PlayerPrefs.SetString("Bot0BusinessType", "<some-id-from-BusinessTypesSO>")` from a temporary `[MenuItem]` or via the Inspector if there's already a bot row.

- [ ] **Step 2: Enter Play mode and verify the title avatar**

1. Press Play.
2. The app boots with the Whatsapp tab as the default. The header `BotSwitcherTitle` should now show:
   - A circular tinted background — color matches the active bot's `BusinessTypesSO` `tileColor`.
   - The business glyph centered inside the circle.
   - The bot's name to the right (already worked).
3. If the active bot has no `BusinessType` set, the avatar is a neutral light gray circle with no glyph (icon `Image.enabled = false`).

- [ ] **Step 3: Verify the bot switcher rows**

1. Still in Play mode, tap the `BotSwitcherTitle` to open the bottom sheet.
2. Each row's avatar should mirror the title pattern: circular tinted background + centered glyph (or neutral gray when no business type).
3. The currently active bot's row has the selected highlight (left accent bar + tinted background) — already worked.

- [ ] **Step 4: Verify live updates**

1. Tap a different bot in the sheet. The sheet closes; the title's avatar should swap to the newly selected bot's tint + glyph immediately (driven by `OnActiveBotChanged`).
2. Stop Play mode. Open BotSettings for one of the bots (BotsPage → Edit on a bot card). Change its business type via the dropdown. Save.
3. Switch back to the Whatsapp tab. The title avatar should reflect the new business type (the tab activation re-runs `BotSwitcherTitleBinder.OnEnable` → `UpdateTitle`).
4. Open the bot switcher sheet again. The row for that bot should also show the new business type.

- [ ] **Step 5: Verify the no-business-type fallback**

1. With Play mode stopped, manually clear a bot's business type: `PlayerPrefs.SetString("Bot0BusinessType", "")` (or whichever bot id), or call `PlayerPrefs.DeleteKey(...)`. Save with `PlayerPrefs.Save()`.
2. Press Play.
3. If that bot is the active bot: the title avatar shows a neutral light gray circle with no glyph.
4. Open the sheet: that bot's row shows the same neutral state. Other bots with business types still render correctly.

- [ ] **Step 6: Smoke-check that nothing else regressed**

While you're in Play mode:

1. Confirm the `BotName` text label and the chevron (`v`) still render in the title.
2. Tap the title — sheet still opens.
3. Select a bot — sheet still closes, chats still load (the `OnActiveBotChanged` chain is shared with chat loading).
4. Switch to the Bots tab and back — title still works.
5. Switch to a Telegram tab if present — Whatsapp tab still works after returning.

- [ ] **Step 7: Commit any verification fixes**

If steps 2–6 surfaced bugs that required code changes (not just adjustments to test data), fix them and commit. If everything passed, no commit is needed.

---

## Out-of-scope reminders

These are intentionally not in this plan; if you find yourself reaching for them, the answer is "ship without it":

- Real WhatsApp profile pictures (GreenAPI / Wappi avatar fetch).
- Per-bot custom avatar uploads.
- Updating any other surface that shows bot identity (Profile page, etc.).
- Refactoring the existing `Bot.BotIconTile` / `Bot.BotIconImage` rendering on the BotsPage card.
- Modifying `Screen_WhatsappHeaderRebuilder.cs` or `BotSwitcherSheetBuilder.cs` — the new menu items are surgical so the existing builders stay clean.
