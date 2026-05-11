# Chat Row — Unread Badge (Phase 1 of WhatsApp-page parity)

## Problem

The chat list on `Screen_Whatsapp` lacks an unread-count badge on each row. Real WhatsApp shows a green pill with the number of unread messages on the right side of every chat with unread activity. This is one of the most recognizable WhatsApp visual elements and is the #1 gap flagged in the page audit.

The Wappi `/chats/filter` response already includes `unread_count` per dialog. Our current `ChatDialog.cs` model does not capture this field, so the data is silently dropped by `JsonUtility.FromJson`. The UI prefab (`Assets/Prefabs/ChatItem.prefab`) has no GameObject for a badge and `ChatItemView.cs` has no binding for it.

## Goal

Add a WhatsApp-iOS-faithful unread badge to every chat row in `Screen_Whatsapp`. Show the WhatsApp-native `unread_count` from Wappi. Hide when count is zero. Reset to zero optimistically when the user opens a chat (matches WhatsApp's instant feel).

## Non-goals

- No badge on `Screen_Telegram` (separate page; out of scope for Phase 1).
- No "pinned chat" indicator (📌). Distinct feature, future phase.
- No mute indicator (🔕). Distinct feature, future phase.
- No animation on count change. WhatsApp does not animate the badge.
- No `wapi_unread_count` rendering. User chose `unread_count` for max WhatsApp-fidelity.
- No archived-chats handling beyond existing logic.

## Data model — Wappi response (confirmed)

Single dialog from `https://wappi.pro/api/sync/chats/filter?profile_id={id}`:

```json
{
  "id": "77472714618@c.us",
  "name": "Zhanym",
  "thumbnail": "...",
  "last_timestamp": "2026-05-11T06:54:37Z",
  "last_message_data": "Оплатил",
  "isArchived": false,
  "unread_count": 0,        // ← new field this spec captures
  "wapi_unread_count": 2,   // ← intentionally ignored
  ...
}
```

## Target file changes

Five files. Four edits, one new builder.

### 1. `Assets/Scripts/Chat/ChatDialog.cs` — add field

Add one field after `isArchived`:

```csharp
public int unread_count;
```

Snake_case matches the JSON key exactly, so `JsonUtility.FromJson` will populate it without aliasing. No other changes.

### 2. `Assets/Scripts/UI/ChatViewModel.cs` — expose count

Add to the existing ViewModel:

```csharp
public int UnreadCount { get; private set; }

public void UpdateUnreadCount(int count)
{
    if (UnreadCount == count) return;
    UnreadCount = count;
    NotifyUpdated();
}
```

Update the constructor signature to accept the initial count:

```csharp
public ChatViewModel(string chatId, string title, string avatarUrl,
                     string lastMessage, long lastTime, int unreadCount)
```

Set `UnreadCount = unreadCount` in the constructor body. No other behavior changes.

### 3. `Assets/Scripts/Main/ChatManager.cs` — wire the data

Two touch-points inside the existing `ParseChatsJson` method:

- **Create path** (`new ChatViewModel(...)` around line 93): pass `chat.unread_count` as the new sixth argument.
- **Merge path** (`existingVm.UpdateLastMessage(...)` around line 88): add a sibling call `existingVm.UpdateUnreadCount(chat.unread_count);` immediately after.

One new touch-point inside `SelectChat` (around line 152): once we've resolved the `ChatViewModel` for the selected chat, call `vm.UpdateUnreadCount(0)` to reset optimistically. Place this immediately after the existing chat-resolution logic, before any UI transition starts. (If the next server sync returns a non-zero count, the badge will reappear — that's correct behavior if new messages arrived during the open.)

### 4. `Assets/Scripts/UI/ChatItemView.cs` — render

Two new `[SerializeField]` references:

```csharp
[SerializeField] private GameObject unreadBadge;
[SerializeField] private TextMeshProUGUI unreadCountText;
```

(Note: existing fields in this file are `public`, not `[SerializeField] private`. To minimize diff and keep the file consistent with itself, add the new fields as `public TextMeshProUGUI unreadCountText` / `public GameObject unreadBadge` matching the existing style. The ui-scripts rule prefers `[SerializeField] private` for new code, but mirroring local convention takes precedence in this file to keep the surrounding code coherent.)

In `Bind(ChatViewModel model)` (after the existing `timeText` assignment, around line 39), call a new private method:

```csharp
ApplyUnreadBadge(vm.UnreadCount);
```

In the existing `OnVmUpdated` handler, also call `ApplyUnreadBadge(vm.UnreadCount)` so live updates repaint the badge.

New private method:

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

Null-guard on `unreadBadge` so rows whose prefab hasn't been rebuilt yet degrade gracefully (no NRE on legacy prefab instances during dev).

### 5. `Assets/Editor/ChatItemUnreadBadgeBuilder.cs` — new builder

New file. Pattern mirrors `Assets/Editor/BotSettingsStickyAddButtonBuilder.cs` (idempotent, prefab-contents load, scene-safe).

## Target row structure

```
ChatItem (prefab root)
└── HorizontalLayoutGroup
    ├── Avatar (140×140)
    └── TextBlock (VerticalLayoutGroup)
        ├── TopRow
        │   ├── Name
        │   └── Time
        ├── Message (TMP, margin.right: 80px)   ← existing; unchanged
        ├── UnreadBadge                          ← NEW
        │   └── CountText (TMP)
        └── Divider (NativeHairline)             ← existing; unchanged
```

`UnreadBadge` is a direct child of `TextBlock`, **but** carries `LayoutElement.ignoreLayout = true` so it is invisible to `VerticalLayoutGroup`. Its `RectTransform` is anchored to the **bottom-right of TextBlock** and floats over the right edge of the `Message` element. The `Message` element's existing 80px right margin already keeps message text from running into the badge area, so no change to `Message` is required.

## Layout details

All values are unscaled (raw RectTransform). The prefab uses 1080×2400 reference scale; the row is 200px tall with 40px left/right padding.

**UnreadBadge** (the green pill):
- `RectTransform.anchorMin = (1, 0)`
- `RectTransform.anchorMax = (1, 0)`
- `RectTransform.pivot = (1, 0.5)`
- `RectTransform.anchoredPosition = (-8, 60)` — 8px from right edge of TextBlock; 60px up from bottom to center on the Message line
- `RectTransform.sizeDelta = (60, 60)` — square base; ContentSizeFitter widens for multi-digit counts
- `Image.color = #25D366` (WhatsApp green; sRGB)
- `Image.raycastTarget = false` — badge is non-interactive
- `LayoutElement.ignoreLayout = true`
- `ContentSizeFitter.horizontalFit = PreferredSize` so badge widens to fit "99+"
- `ContentSizeFitter.verticalFit = Unconstrained` (fixed height)
- `HorizontalLayoutGroup` on the badge itself:
  - `padding = (16, 16, 0, 0)` — 16px left/right; lets pill shape stay round for single digit, extends for wider numbers
  - `childAlignment = MiddleCenter`
  - `childControlWidth = true`, `childControlHeight = true`
  - `childForceExpandWidth = false`, `childForceExpandHeight = false`
- `RoundedCorners` script component (existing package): `radius = 30`. Since `radius >= height/2`, the rendered shape auto-pills.

**CountText** (TMP child of badge):
- `TextMeshProUGUI`
- `text` default: empty (set by `ApplyUnreadBadge`)
- `fontSize = 36` (scales appropriately at 1080×2400 reference)
- `fontStyle = Bold`
- `color = #FFFFFF`
- `alignment = TextAlignmentOptions.Center`
- `enableWordWrapping = false`
- `overflowMode = Overflow` (won't truncate "99+")

## Editor tool — `ChatItemUnreadBadgeBuilder.cs`

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class ChatItemUnreadBadgeBuilder
{
    private const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";
    private const string BadgeName = "UnreadBadge";
    private const string CountTextName = "CountText";

    [MenuItem("Tools/Chat List/Add Unread Badge To ChatItem")]
    public static void Build()
    {
        // 1. Load prefab contents via PrefabUtility.LoadPrefabContents
        // 2. Find TextBlock by name (recursive)
        // 3. If existing UnreadBadge under TextBlock, DestroyImmediate it (idempotent rebuild)
        // 4. Create UnreadBadge GameObject with components in this exact order:
        //    RectTransform, Image, LayoutElement, HorizontalLayoutGroup, ContentSizeFitter
        // 5. Apply RectTransform anchors/pivot/sizeDelta/anchoredPosition per Layout details
        // 6. Apply Image color = WhatsApp green, raycastTarget=false
        // 7. Apply LayoutElement.ignoreLayout = true
        // 8. Apply HorizontalLayoutGroup padding/alignment per Layout details
        // 9. Apply ContentSizeFitter horizontalFit=PreferredSize, verticalFit=Unconstrained
        // 10. Add RoundedCorners component, set radius=30 via reflection or direct API
        // 11. Create CountText child:
        //     - RectTransform with stretch anchors inside parent
        //     - TextMeshProUGUI with default text="", fontSize=36, bold, white, centered, no-wrap
        // 12. Locate ChatItemView component on prefab root
        // 13. Use SerializedObject + FindProperty to set unreadBadge and unreadCountText refs
        //     (NOT direct field assignment — SerializedObject ensures the prefab serializes correctly)
        // 14. Apply modified properties, save prefab via PrefabUtility.SaveAsPrefabAsset
        // 15. PrefabUtility.UnloadPrefabContents
        // 16. EditorUtility.SetDirty + AssetDatabase.SaveAssets
        // 17. Debug.Log success with badge GameObject path
    }
}
#endif
```

**Surgical guarantees** (the user's explicit requirement):

- Touches only **two** GameObjects: creates `UnreadBadge` and its child `CountText`. Wires two `ChatItemView` properties. Nothing else.
- **Does not modify** `Avatar`, `Name`, `Time`, `Message`, `Divider`, `TextBlock`'s `VerticalLayoutGroup` settings, or the root `HorizontalLayoutGroup`.
- **Idempotent**: re-running the menu item destroys any existing `UnreadBadge` first, then rebuilds. Safe to invoke repeatedly during dev.
- **No scene modification needed**. Chat rows are spawned at runtime by `ChatListView` from this prefab; updating the prefab updates all instances automatically.
- Uses `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` rather than direct asset edits — Unity's recommended path for editor-time prefab modification.

## Optimistic reset details

When `ChatManager.SelectChat(chatId)` is called, the badge for that chat resets to 0 locally **before** any server round-trip:

1. User taps a chat row → `ChatItemView.OnClick()` → `ChatManager.SelectChat(chatId)`
2. `SelectChat` resolves the `ChatViewModel` from `chatLookup` (existing code)
3. **New**: `vm.UpdateUnreadCount(0)` immediately after resolution
4. `OnUpdated` fires → existing event chain repaints the row → badge hides
5. User sees the chat open with badge already cleared

This matches WhatsApp's UX. If a new message arrives during the open (rare), the next `SyncAllChats` poll will re-populate `unread_count > 0` and the badge will reappear on row update. No race condition because the merge path in `ParseChatsJson` calls `UpdateUnreadCount` unconditionally with the server value.

## Behavior matrix

| `unread_count` | Badge visible? | Text |
|---|---|---|
| 0 | No | — |
| 1 | Yes | "1" |
| 23 | Yes | "23" |
| 99 | Yes | "99" |
| 100 | Yes | "99+" |
| 500 | Yes | "99+" |
| (negative — defensive) | No | — |

## Testing

Manual verification on device (1080×2400 game-view) after running the builder once:

1. Open `Main.unity`, select `Screen_Whatsapp`, enable in editor.
2. Connect a Wappi bot with at least one chat that has unread messages on WhatsApp (send messages from another phone to your linked number).
3. Verify badge appears on the right side of the row, in line with the message preview.
4. Verify count matches the WhatsApp app on your phone.
5. Tap the chat — badge should disappear immediately (optimistic reset).
6. Read messages on the WhatsApp phone app, force a re-sync — badge should stay hidden (matches `unread_count=0`).
7. Receive a new message — badge should reappear with correct count on next sync.
8. Test "99+" cap by mocking a high count locally if needed.
9. Verify no layout shift on `Message` text or `Time` text — badge is layout-isolated.
10. Verify the builder is idempotent: re-run from the menu, verify no duplicate badge.

## Risks

- **Wappi field schema change**: if Wappi renames `unread_count` server-side, the field silently goes to 0 (JsonUtility behavior). Detection: badge stops appearing on chats with known unread messages. Mitigation: visible to user immediately during testing; trivial rename if it happens.
- **Performance with 500+ chats**: badge adds two GameObjects per row. At 500 rows that's 1000 new GameObjects, but they're parented inside the existing virtualization-free chat list. Acceptable — the existing chat list already instantiates all rows up-front (no virtualization), so per-row cost is the bottleneck regardless of this addition.
- **Optimistic-reset stale flash**: if user taps a row and Wappi's next sync arrives before they exit the chat, the badge could briefly reappear on the closed chat list view. Acceptable — matches WhatsApp's actual behavior in the same edge case.

## Out of scope (Phase 2)

The audit also identified read-receipt ticks (✓ / ✓✓ / blue ✓✓) and media-type icons (📷 📹 🎤) in the last-message preview. These will be implemented in a separate Phase 2 spec because they share data extraction (last message metadata) and rendering layer (inline TMP `<sprite>` tags) with each other, but neither shares anything with the unread badge.
