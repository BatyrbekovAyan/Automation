# Chat Row — Unread Badge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WhatsApp-iOS-faithful unread-count badge to every chat row on `Screen_Whatsapp`, populated from Wappi's confirmed `unread_count` field per dialog. The badge appears as a green pill on the right side of each row, hidden when count is zero, and resets optimistically when the user opens the chat.

**Architecture:** Five files change. The data layer (`ChatDialog`, `ChatViewModel`) gains an `unread_count` / `UnreadCount` field. `ChatManager.ParseChatsJson` passes it through on create and merge. `ChatManager.SelectChat` resets to 0 optimistically. `ChatItemView` exposes two new prefab refs and renders the badge via a single `ApplyUnreadBadge(int)` method called from `Bind` and `OnVmUpdated`. A new editor builder `ChatItemUnreadBadgeBuilder` surgically adds the badge GameObject (with `LayoutElement.ignoreLayout = true`) to `Assets/Prefabs/ChatItem.prefab`, floating it over the existing `Message` element's 80 px right margin so it never disturbs the `VerticalLayoutGroup`.

**Tech Stack:** Unity 6000.3.9f1, C#, Unity UI (RectTransform, Image, HorizontalLayoutGroup, LayoutElement, ContentSizeFitter), TextMeshPro, `Nobi.UiRoundedCorners.ImageWithRoundedCorners`, `JsonUtility` for JSON parsing, `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` for prefab editing, `SerializedObject` for safe ref-wiring.

**Testing approach:** This is a Unity mobile UI feature. There is no automated test harness in the project — verification is manual in the Unity Editor Game view at 1080×2400 (mobile aspect). Each task ends with a compile check (no Console errors in Unity) and, for the final task, Play-mode visual checks against a real Wappi profile.

---

## File Structure

- **Modify:** `Assets/Scripts/Chat/ChatDialog.cs` — add `unread_count` field. Single responsibility: serializable model mirroring Wappi response.
- **Modify:** `Assets/Scripts/UI/ChatViewModel.cs` — add `UnreadCount` property, `UpdateUnreadCount(int)` method, and constructor parameter (with default `0` so existing call sites keep compiling between tasks).
- **Modify:** `Assets/Scripts/Main/ChatManager.cs` — three touch-points inside `ParseChatsJson` and `SelectChat`. Single responsibility: drive ViewModel state from server data.
- **Modify:** `Assets/Scripts/UI/ChatItemView.cs` — add two prefab refs and `ApplyUnreadBadge(int)` method. Single responsibility: render the chat row.
- **Create:** `Assets/Editor/ChatItemUnreadBadgeBuilder.cs` — editor-only menu item that adds the badge GameObject to `Assets/Prefabs/ChatItem.prefab` and wires the refs on the `ChatItemView` component. Single responsibility: idempotent prefab construction.

---

## Task 1: Capture `unread_count` from Wappi in `ChatDialog`

**Why:** Smallest possible first step — add the field the JSON parser will populate. The field is unused until Task 2 consumes it, so this task is independently committable and zero-risk.

**Files:**
- Modify: `Assets/Scripts/Chat/ChatDialog.cs`

- [ ] **Step 1: Add the `unread_count` field**

Open `Assets/Scripts/Chat/ChatDialog.cs`. After the existing `public bool isArchived;` line, add one line. The full file should read:

```csharp
using System;

[Serializable]
public class ChatDialog
{
    public string id;
    public bool isGroup;
    public string name;
    public string thumbnail;
    public string last_message_data;
    public string last_timestamp;
    public bool isArchived;
    public int unread_count;
}
```

The snake_case name is required: `JsonUtility.FromJson` matches field names character-for-character against JSON keys, and Wappi's response uses `unread_count`.

- [ ] **Step 2: Verify compile**

Switch to Unity. Wait for the script reload. Check the Console: no errors.
Expected: Clean compile.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/ChatDialog.cs
git commit -m "feat(chat): capture unread_count from Wappi /chats/filter response"
```

---

## Task 2: Expose `UnreadCount` on `ChatViewModel`

**Why:** Surface the field on the UI-facing ViewModel so views can bind to it. Use a default-valued constructor parameter so the existing single call site in `ChatManager` continues to compile between this task and Task 3.

**Files:**
- Modify: `Assets/Scripts/UI/ChatViewModel.cs`

- [ ] **Step 1: Add the `UnreadCount` property, the update method, and the constructor parameter**

Open `Assets/Scripts/UI/ChatViewModel.cs`. Replace the entire file with:

```csharp
using System;
using UnityEngine;

public class ChatViewModel
{
    public string ChatId { get; }
    public string Title { get; }
    public string AvatarUrl { get; }
    
    public Sprite AvatarSprite { get; set; }
    public string LastMessage { get; private set; }
    public long LastMessageTime { get; private set; }
    public int UnreadCount { get; private set; }
    
    // Added for UI display
    public string LastMessageTimeString { get; private set; }
    // public string OnlineStatus { get; set; }
    public event Action<ChatViewModel> OnUpdated;

    public ChatViewModel(string chatId, string title, string avatarUrl,
                         string lastMessage, long lastTime, int unreadCount = 0)
    {
        ChatId = chatId;
        Title = title;
        AvatarUrl = avatarUrl;
        LastMessage = lastMessage;
        LastMessageTime = lastTime;
        UnreadCount = unreadCount;
        LastMessageTimeString = FormatTimestamp(lastTime);
        
        // OnlineStatus = "tap here for contact info";
    }
    
    public void UpdateLastMessage(string message, long time)
    {
        if (this.LastMessage == message && this.LastMessageTime == time) return;

        LastMessage = message;
        LastMessageTime = time;
        LastMessageTimeString = FormatTimestamp(time);
        
        // This will now ONLY fire if a chat genuinely received a new message!
        NotifyUpdated();
    }

    public void UpdateUnreadCount(int count)
    {
        if (count < 0) count = 0;
        if (UnreadCount == count) return;
        UnreadCount = count;
        NotifyUpdated();
    }

    private string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0) return "";
        DateTime dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().DateTime;
        DateTime now = DateTime.Now.Date;
        TimeSpan diff = now - dt.Date;

        if (diff.Days == 0) return dt.ToString("HH:mm");
        if (diff.Days == 1) return "Yesterday";
        if (diff.Days < 7) return dt.ToString("dddd");
        return dt.ToString("dd.MM.yy");
    }
    
    public void NotifyUpdated()
    {
        OnUpdated?.Invoke(this);
    }
}
```

Three additions on top of the current file:
1. `public int UnreadCount { get; private set; }` property (after `LastMessageTime`).
2. New constructor parameter `int unreadCount = 0` (with default so existing single call site in `ChatManager` keeps compiling).
3. `UpdateUnreadCount(int count)` method that clamps to ≥0, no-ops on equal value, and fires `OnUpdated` on change.

- [ ] **Step 2: Verify compile**

Switch to Unity. Wait for the script reload. Check the Console: no errors.
Expected: Clean compile. The existing `new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime)` call in `ChatManager.cs:93` keeps working because the new parameter has a default.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/ChatViewModel.cs
git commit -m "feat(chat): expose UnreadCount on ChatViewModel with optimistic-reset path"
```

---

## Task 3: Wire `ChatManager` to feed and reset the unread count

**Why:** Pull the per-dialog `unread_count` from the parsed JSON into the ViewModel for both newly added chats and existing ones, and reset to 0 when the user opens a chat (optimistic UX matching WhatsApp).

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Update `ParseChatsJson` to pass and merge `unread_count`**

Open `Assets/Scripts/Main/ChatManager.cs`. Locate the existing `ParseChatsJson` method (starts around line 60). Find the inner loop body (currently lines ~75-98) and replace it with the updated version below.

Current code (around lines 75-98):

```csharp
        foreach (var chat in response.dialogs)
        {
            long unixTime = 0;
            if (DateTimeOffset.TryParse(chat.last_timestamp, out var dto)) unixTime = dto.ToUnixTimeSeconds();

            string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
            string lastMsg = string.IsNullOrEmpty(chat.last_message_data) ? "" : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.last_message_data);

            if (chatLookup.TryGetValue(chat.id, out var existingVm))
            {
                // --- THE SMART MERGE ---
                // The chat is already on the screen! Do not destroy the prefab!
                // Just quietly update the text and time. The UI will catch the event and refresh seamlessly.
                existingVm.UpdateLastMessage(lastMsg, unixTime);
            }
            else
            {
                // This is a brand new chat we haven't seen before, spawn it!
                var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime);
                Chats.Add(chatVM);
                chatLookup[chat.id] = chatVM;
                OnChatAdded?.Invoke(chatVM);
            }
        }
```

Replace it with:

```csharp
        foreach (var chat in response.dialogs)
        {
            long unixTime = 0;
            if (DateTimeOffset.TryParse(chat.last_timestamp, out var dto)) unixTime = dto.ToUnixTimeSeconds();

            string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
            string lastMsg = string.IsNullOrEmpty(chat.last_message_data) ? "" : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.last_message_data);

            if (chatLookup.TryGetValue(chat.id, out var existingVm))
            {
                // --- THE SMART MERGE ---
                // The chat is already on the screen! Do not destroy the prefab!
                // Just quietly update the text, time, and unread count. The UI will catch the event and refresh seamlessly.
                existingVm.UpdateLastMessage(lastMsg, unixTime);
                existingVm.UpdateUnreadCount(chat.unread_count);
            }
            else
            {
                // This is a brand new chat we haven't seen before, spawn it!
                var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime, chat.unread_count);
                Chats.Add(chatVM);
                chatLookup[chat.id] = chatVM;
                OnChatAdded?.Invoke(chatVM);
            }
        }
```

Two changes:
1. Add `existingVm.UpdateUnreadCount(chat.unread_count);` after the existing `UpdateLastMessage` call.
2. Pass `chat.unread_count` as the new sixth argument to the `ChatViewModel` constructor.

- [ ] **Step 2: Optimistic-reset on `SelectChat`**

Locate the existing `SelectChat(string chatId)` method (starts around line 152). Read the current method body to find where it resolves the `ChatViewModel` from the lookup (it accesses `chatLookup` to find the selected chat).

Add this block immediately after the chat lookup is resolved and before any UI transition is triggered. Look for the line that does `chatLookup.TryGetValue` or equivalent inside `SelectChat`, and insert immediately after the successful-lookup branch:

```csharp
            // Optimistic local reset — match WhatsApp's instant feel.
            // If the next sync returns a non-zero count, the badge re-appears.
            if (chatLookup.TryGetValue(chatId, out var selectedVm))
            {
                selectedVm.UpdateUnreadCount(0);
            }
```

**Implementation note for the executor:** The exact line number depends on the current shape of `SelectChat`. The rule is: this block must run **before** any code that hides the chat list / opens the message list, so the badge's hide animation (driven by `OnUpdated`) plays while the list is still visible to the user. If `SelectChat` does not currently use `chatLookup.TryGetValue`, add the lookup as shown above — the dictionary already exists and is the canonical source of ViewModels by chat ID.

- [ ] **Step 3: Verify compile**

Switch to Unity. Wait for the script reload. Check the Console: no errors.
Expected: Clean compile.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): feed unread_count into ChatViewModel; optimistic reset on open"
```

---

## Task 4: Render the badge in `ChatItemView`

**Why:** Add the rendering hooks before the prefab has the badge GameObject. The new fields default to `null` until Task 6 wires them via the builder; null-guards in `ApplyUnreadBadge` make this safe.

**Files:**
- Modify: `Assets/Scripts/UI/ChatItemView.cs`

- [ ] **Step 1: Add the two prefab-ref fields**

Open `Assets/Scripts/UI/ChatItemView.cs`. Locate the existing field block at the top of the class (lines ~10-16):

```csharp
public class ChatItemView : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public Image avatarImage;
    public Image defaultAvatar;
    public Button button;
    public TextMeshProUGUI lastMessageText;

    public TextMeshProUGUI timeText;
```

Add two new public fields after `timeText` (mirroring the file's existing `public` convention; the ui-scripts rule normally prefers `[SerializeField] private` but local consistency wins here to keep the diff minimal):

```csharp
public class ChatItemView : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public Image avatarImage;
    public Image defaultAvatar;
    public Button button;
    public TextMeshProUGUI lastMessageText;

    public TextMeshProUGUI timeText;

    public GameObject unreadBadge;
    public TextMeshProUGUI unreadCountText;
```

- [ ] **Step 2: Call `ApplyUnreadBadge` from `Bind`**

Locate the existing `Bind(ChatViewModel model)` method. Find the line that sets `timeText.text` (around line 38-39):

```csharp
        titleText.text = vm.Title;
        
        if (timeText != null)
            timeText.text = vm.LastMessageTimeString;
```

Add a single call immediately after the `timeText` block:

```csharp
        titleText.text = vm.Title;
        
        if (timeText != null)
            timeText.text = vm.LastMessageTimeString;

        ApplyUnreadBadge(vm.UnreadCount);
```

- [ ] **Step 3: Call `ApplyUnreadBadge` from the update handler**

Locate the existing `OnVmUpdated` method (the handler subscribed to `vm.OnUpdated` in `Bind`). Search for `private void OnVmUpdated` or `OnVmUpdated(` in the file. Inside its body, add a call to `ApplyUnreadBadge(vm.UnreadCount)` so badge repaints fire whenever `NotifyUpdated()` fires from the ViewModel — including the optimistic reset from `SelectChat` and the merge-time update from `ParseChatsJson`.

The exact placement: at the end of the existing `OnVmUpdated` body, right before the closing brace.

```csharp
        // ... existing logic that re-applies title, last message, time, etc. ...
        ApplyUnreadBadge(vm.UnreadCount);
    }
```

- [ ] **Step 4: Add the `ApplyUnreadBadge` method**

Add this private method near the bottom of the class, just before the closing brace:

```csharp
    private void ApplyUnreadBadge(int count)
    {
        if (unreadBadge == null) return;
        if (count <= 0)
        {
            unreadBadge.SetActive(false);
            return;
        }
        unreadBadge.SetActive(true);
        if (unreadCountText != null)
        {
            unreadCountText.text = count > 99 ? "99+" : count.ToString();
        }
    }
```

Null-guard on `unreadBadge` keeps the file safe to compile before Task 6 has wired the prefab. Null-guard on `unreadCountText` is defensive in case the builder ever wires the badge GameObject but misses the child TMP (the builder will always set both, but the guard is cheap).

- [ ] **Step 5: Verify compile**

Switch to Unity. Wait for the script reload. Check the Console: no errors.
Expected: Clean compile. Inspector on `ChatItem.prefab` (open the prefab to inspect) shows the two new fields as `None` — that's correct; Task 6 wires them.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/ChatItemView.cs
git commit -m "feat(chat): render unread badge with 99+ cap and null-guard"
```

---

## Task 5: Write the `ChatItemUnreadBadgeBuilder` editor script

**Why:** The builder is the single source of truth for the badge's UI structure. Writing it before running it lets us code-review the structure before any prefab change lands.

**Files:**
- Create: `Assets/Editor/ChatItemUnreadBadgeBuilder.cs`

- [ ] **Step 1: Create the builder file**

Write the file at `Assets/Editor/ChatItemUnreadBadgeBuilder.cs`:

```csharp
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nobi.UiRoundedCorners;

/// <summary>
/// Editor maintenance for ChatItem.prefab.
///
/// Adds an UnreadBadge GameObject to the right side of the chat row, anchored
/// to the bottom-right of TextBlock and floating over the existing Message
/// element's 80 px right margin. The badge is layout-isolated via
/// LayoutElement.ignoreLayout = true so it never disturbs the VerticalLayoutGroup
/// that owns the Name / Message rows.
///
/// Target row structure (TextBlock children, after this builder runs):
///
///   TextBlock (VerticalLayoutGroup)
///     TopRow
///     Message                              ← unchanged
///     UnreadBadge                          ← NEW. ignoreLayout. Anchored bottom-right.
///       CountText (TMP, white, bold)
///     Divider                              ← unchanged
///
/// Idempotent — re-running destroys any existing UnreadBadge and rebuilds.
/// </summary>
public static class ChatItemUnreadBadgeBuilder
{
    private const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";
    private const string TextBlockName = "TextBlock";
    private const string BadgeName = "UnreadBadge";
    private const string CountTextName = "CountText";

    private static readonly Color WhatsAppGreen = new Color32(0x25, 0xD3, 0x66, 0xFF);

    [MenuItem("Tools/Chat List/Add Unread Badge To ChatItem")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[ChatItemUnreadBadge] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var textBlock = FindChildRecursive(prefabRoot.transform, TextBlockName);
            if (textBlock == null)
            {
                Debug.LogError($"[ChatItemUnreadBadge] '{TextBlockName}' not found under {PrefabPath}");
                return;
            }

            var existing = textBlock.Find(BadgeName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var badge = BuildBadge(textBlock);
            var countText = BuildCountText(badge.transform);

            WireChatItemViewRefs(prefabRoot, badge, countText);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log($"[ChatItemUnreadBadge] Built badge under {PrefabPath} → TextBlock/{BadgeName}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static GameObject BuildBadge(Transform parent)
    {
        var badge = new GameObject(
            BadgeName,
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(ImageWithRoundedCorners));
        badge.transform.SetParent(parent, false);

        var rt = (RectTransform)badge.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-8f, 60f);
        rt.sizeDelta = new Vector2(60f, 60f);

        var image = badge.GetComponent<Image>();
        image.color = WhatsAppGreen;
        image.raycastTarget = false;

        var le = badge.GetComponent<LayoutElement>();
        le.ignoreLayout = true;

        var hlg = badge.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = badge.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var rounded = badge.GetComponent<ImageWithRoundedCorners>();
        var radiusField = typeof(ImageWithRoundedCorners).GetField(
            "radius", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (radiusField != null) radiusField.SetValue(rounded, 30f);

        return badge;
    }

    private static TextMeshProUGUI BuildCountText(Transform parent)
    {
        var go = new GameObject(CountTextName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 36f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static void WireChatItemViewRefs(GameObject prefabRoot, GameObject badge, TextMeshProUGUI countText)
    {
        var view = prefabRoot.GetComponent<ChatItemView>();
        if (view == null)
        {
            Debug.LogError($"[ChatItemUnreadBadge] No ChatItemView component on prefab root.");
            return;
        }

        var so = new SerializedObject(view);
        var badgeProp = so.FindProperty("unreadBadge");
        var countProp = so.FindProperty("unreadCountText");

        if (badgeProp == null || countProp == null)
        {
            Debug.LogError(
                "[ChatItemUnreadBadge] ChatItemView is missing 'unreadBadge' or 'unreadCountText' fields. " +
                "Did Task 4 land?");
            return;
        }

        badgeProp.objectReferenceValue = badge;
        countProp.objectReferenceValue = countText;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindChildRecursive(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
#endif
```

Key implementation notes:
- **`Nobi.UiRoundedCorners.ImageWithRoundedCorners`** is the rounded-corners component used by the rest of the project's builders (`BotSwitcherRowAvatarRebuilder.cs:2`). The `radius` field is set via reflection because some package versions expose it as private with `[SerializeField]` — the reflection path works regardless.
- **`PrefabUtility.LoadPrefabContents` + `SaveAsPrefabAsset` + `UnloadPrefabContents`** is the canonical Unity 6 way to edit a prefab from an editor script without leaking. The `try/finally` ensures unload even if an exception fires mid-build.
- **`SerializedObject.FindProperty` + `objectReferenceValue`** is the correct way to write prefab refs — `ApplyModifiedPropertiesWithoutUndo` flushes them to the prefab. Direct field assignment would not persist on prefab save.
- **`FindChildRecursive`** is needed because `TextBlock` is a nested child of the prefab root, not a direct child — the existing prefab grep confirms `m_Name: TextBlock` exists at line 15 of the prefab but is not a root.
- **Idempotency** is handled by destroying any pre-existing `UnreadBadge` before building.

- [ ] **Step 2: Verify compile**

Switch to Unity. Wait for the script reload. Check the Console: no errors. Confirm the menu item appears under `Tools → Chat List → Add Unread Badge To ChatItem`.
Expected: Clean compile. Menu item visible.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/ChatItemUnreadBadgeBuilder.cs Assets/Editor/ChatItemUnreadBadgeBuilder.cs.meta
git commit -m "feat(editor): surgical builder for chat-row unread badge"
```

---

## Task 6: Run the builder and visually verify

**Why:** Build the prefab change and verify the badge looks and behaves correctly against a live Wappi profile with real unread chats.

**Files:**
- Modify: `Assets/Prefabs/ChatItem.prefab` — written by the builder.

- [ ] **Step 1: Run the builder**

In Unity, click `Tools → Chat List → Add Unread Badge To ChatItem`.
Watch the Console.
Expected: One log line `[ChatItemUnreadBadge] Built badge under Assets/Prefabs/ChatItem.prefab → TextBlock/UnreadBadge`. No errors. No warnings.

- [ ] **Step 2: Inspect the prefab**

In the Project window, double-click `Assets/Prefabs/ChatItem.prefab` to open it in Prefab Mode. In the Hierarchy:

1. Confirm `TextBlock → UnreadBadge` exists as a child of `TextBlock`.
2. Confirm `UnreadBadge → CountText` exists as a child of the badge.
3. Click the prefab root and inspect the `ChatItemView` component. Confirm `Unread Badge` field shows `UnreadBadge` and `Unread Count Text` shows `CountText (TextMeshProUGUI)`.
4. Click `UnreadBadge`. Verify in the Inspector:
   - `RectTransform`: anchor (1, 0)–(1, 0), pivot (1, 0.5), anchoredPosition (-8, 60), sizeDelta starts (60, 60) but ContentSizeFitter will resize horizontally.
   - `Image`: color = green (#25D366), Raycast Target unchecked.
   - `LayoutElement`: Ignore Layout checked.
   - `HorizontalLayoutGroup`: padding (16, 16, 0, 0), alignment middle-center.
   - `ContentSizeFitter`: horizontal Preferred Size, vertical Unconstrained.
   - `ImageWithRoundedCorners`: present.

Expected: All fields match. The badge should appear as a small green circle in the Scene view, anchored to the bottom-right of TextBlock.

- [ ] **Step 3: Set the badge text manually in Prefab Mode for a quick layout sanity check**

Still in Prefab Mode, click `CountText`, set its `Text` to `99+` in the Inspector. Watch the Scene view: the badge should auto-widen to a horizontal pill (because of ContentSizeFitter + the inner HorizontalLayoutGroup's 16 px L/R padding). The corners should stay rounded (auto-pilled by the 30 px radius exceeding half-height). Set the text back to empty when done.

Expected: Pill shape, white "99+" centered, no clipping. Confirm there is no overlap with the `Message` element above it.

- [ ] **Step 4: Run the app against a real Wappi profile**

Save the prefab and exit Prefab Mode. Open `Assets/Scenes/Main.unity`. Enter Play mode.

Pre-conditions for the visual check:
- A bot is created and connected to a WhatsApp profile via Wappi.
- At least one chat on that profile has unread messages (send messages from another phone to the linked number, do NOT open them on the phone's WhatsApp app).

In Play mode, navigate to `Screen_Whatsapp`. Verify:

1. **Badge appears** on rows whose `unread_count > 0`. The count should match what the phone's WhatsApp app shows for that chat.
2. **Badge hides** on rows with `unread_count == 0`.
3. **Badge is positioned correctly**: bottom-right of the row, in line with the `Message` preview text. No overlap with the `Time` text. No layout shift on `Name` or `Message`.
4. **Tap behavior**: tap a chat with a badge. The badge should disappear immediately as the chat opens (optimistic reset).
5. **Re-sync behavior**: tap back to the chat list. Wait for the next sync (or trigger one). Confirm the badge stays hidden if the WhatsApp app on the phone has also been opened, or reappears with new count if new messages arrived during the open.
6. **"99+" cap**: if a chat has ≥100 unread, confirm the text reads `99+`.

Expected: All six checks pass. Note any deviations and resolve before committing the prefab.

- [ ] **Step 5: Commit the prefab**

```bash
git add Assets/Prefabs/ChatItem.prefab
git commit -m "feat(prefab): unread badge on ChatItem rows — built via Tools menu"
```

---

## Self-Review Notes

Spec coverage check:

| Spec section | Implemented in |
|---|---|
| Add `unread_count` field to `ChatDialog` | Task 1 |
| Add `UnreadCount` property + `UpdateUnreadCount` method to `ChatViewModel` | Task 2 |
| Constructor signature change with default | Task 2 (default avoids breaking compile between tasks) |
| Wire `ParseChatsJson` (create + merge paths) | Task 3 Step 1 |
| Optimistic reset in `SelectChat` | Task 3 Step 2 |
| Add `unreadBadge` + `unreadCountText` fields to `ChatItemView` | Task 4 Step 1 |
| `ApplyUnreadBadge` from `Bind` and `OnVmUpdated` | Task 4 Steps 2–3 |
| `ApplyUnreadBadge` method with 99+ cap and null-guards | Task 4 Step 4 |
| Builder file with all RectTransform / Image / Layout values | Task 5 |
| `LayoutElement.ignoreLayout = true` | Task 5 (`BuildBadge` line `le.ignoreLayout = true`) |
| `ImageWithRoundedCorners` radius = 30 | Task 5 (`BuildBadge`, reflection-set) |
| WhatsApp green `#25D366` | Task 5 (`WhatsAppGreen` constant) |
| `ContentSizeFitter.horizontalFit = PreferredSize` | Task 5 (`BuildBadge`) |
| Wire `SerializedObject` refs | Task 5 (`WireChatItemViewRefs`) |
| Surgical guarantees (no modification to existing GameObjects) | Task 5 — builder only adds new GameObjects + wires two new fields |
| Idempotent rebuild | Task 5 (`if (existing != null) DestroyImmediate`) |
| 99+ cap behavior matrix | Task 4 Step 4 + Task 6 Step 4 (verification) |
| Manual test checklist | Task 6 Step 4 |

No spec section unmapped. No `TBD` / `TODO` placeholders. Type and field names consistent across tasks (`unread_count`, `UnreadCount`, `UpdateUnreadCount`, `unreadBadge`, `unreadCountText`, `ApplyUnreadBadge`, `WhatsAppGreen`).
