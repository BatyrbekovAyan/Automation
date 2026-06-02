# Scroll-to-bottom FAB + Unread separator — Design

- **Date:** 2026-06-02
- **Status:** Approved design, ready for implementation plan
- **Area:** Chat message list UI (`Assets/Scripts/UI/MessageListView.cs`, `Assets/Scripts/Chat/`, `Assets/Scripts/Main/ChatManager.cs`)

## Goal

Add two coupled chat affordances, matching WhatsApp behavior:

1. **Scroll-to-bottom FAB** — a floating button (bottom-right of the message list) that appears only when the user has scrolled up off the bottom. Tapping it animates back to the newest message. It carries a count badge of unread messages below the fold.
2. **Unread separator** — a full-width divider ("N UNREAD MESSAGES") inserted into the message stream marking where this visit's unread messages begin, so opening a busy chat lands the user on "what's new."

## Background & constraints

- The message list is owned by `MessageListView` ([MessageListView.cs](../../../Assets/Scripts/UI/MessageListView.cs)). Content is laid out **newest-at-bottom** (spawned backwards with `SetAsFirstSibling()`). "At bottom" is `scrollRect.verticalNormalizedPosition <= 0.05f`; initial load ends by jumping to bottom (`= 0f`, MessageListView.cs:626).
- `OnScroll` (MessageListView.cs:156) is already wired to `scrollRect.onValueChanged` and used for infinite-scroll pagination. FAB visibility + badge updates hook in here.
- Content children are a heterogeneous mix: `MessageItemView` bubbles, `SenderSpacer` GameObjects, and `DateSeparatorView`s. No persistent list of bubbles is kept today.
- **Unread is chat-level only.** Wappi returns `ChatDialog.unread_count`; it is surfaced as `ChatViewModel.UnreadCount`. There is **no per-message read/unread state**. `SelectChat` optimistically zeroes the count at [ChatManager.cs:345](../../../Assets/Scripts/Main/ChatManager.cs:345) (the line after it is read at :344) and fires `MarkChatAsRead`.
- Leaving a chat destroys all bubbles via `HandleSlideOutComplete` (MessageListView.cs:119) — the natural reset point.
- **Canvas sizing:** the main Canvas uses `CanvasScaler` = Scale With Screen Size, reference **1080×1920**, Match = **Width (0)** ([Main.unity:1272](../../../Assets/Scenes/Main.unity:1272)). All sizes below are in reference units where **~3 units = 1dp**. Verify in Game view at 1080×2400.

## Decisions (locked)

| Decision | Choice |
|---|---|
| Unread detection | **Server count on open** — snapshot `unread_count` at open, place separator above that many newest *incoming* messages. No live separator. |
| Open position | **Land at the separator** (first unread near top of viewport). |
| FAB badge | **Count of unread messages below the fold.** Read history above the separator never inflates it. Increments on live arrivals while scrolled up; 0 → FAB hides. |
| Code structure | **Separate components** — `ScrollToBottomFab` + `UnreadSeparatorView`; `MessageListView` orchestrates; `ChatManager.UnreadOnOpen` snapshot. |
| Visual direction | **WhatsApp-native** — full-width green-tinted separator bar; white FAB, grey chevron, green badge. |

## Architecture & components

### 1. `ScrollToBottomFab` (new MonoBehaviour + prefab/scene object)

A dumb, reusable widget that knows nothing about chats or scrolling.

- Serialized refs: `Button button`, `CanvasGroup canvasGroup`, `GameObject badgeRoot`, `TMP_Text badgeText`.
- `public bool IsShown { get; private set; }`
- `public void Show()` / `public void Hide()` — DOTween fade on `canvasGroup` (0.2s), toggles `interactable`/`blocksRaycasts`. No-op if already in target state.
- `public void SetCount(int count)` — sets `badgeText`, activates `badgeRoot` only when `count > 0` (clamps display, e.g. "99+").
- `public event Action OnClicked` — wired from `button.onClick` in `Awake`; press feedback via `DOPunchScale`.

### 2. `UnreadSeparatorView` (new MonoBehaviour + prefab)

Modeled on `DateSeparatorView` ([DateSeparatorView.cs](../../../Assets/Scripts/Chat/DateSeparatorView.cs)).

- Serialized ref: `TMP_Text label`.
- `public void SetCount(int count)` → `"{count} UNREAD MESSAGES"`, singular `"1 UNREAD MESSAGE"`.

### 3. `MessageListView` (orchestrator — edits)

- New serialized refs: `UnreadSeparatorView unreadSeparatorPrefab`, `ScrollToBottomFab scrollToBottomFab`.
- New private state: `List<RectTransform> _unreadBubbles`, `RectTransform _unreadSeparatorInstance`, throttle timestamp.
- Subscribes to `scrollToBottomFab.OnClicked` in `OnEnable`, unsubscribes in `OnDisable`.
- Resets `_unreadBubbles`/separator ref + hides FAB in `OnChatSelected` and `HandleSlideOutComplete`.

### 4. `ChatManager` (one field — edit)

- `public int UnreadOnOpen { get; private set; }`
- In `SelectChat`, set `UnreadOnOpen = chatLookup.TryGetValue(...) ? selectedVm.UnreadCount : 0;` **before** the zeroing at ChatManager.cs:345.
- Rationale: avoids changing the `OnChatSelected` (`Action<string>`) signature, which has other subscribers (ChatManager.cs:103).

## Behavior & data flow

### On chat open (initial build — `UpdateListRoutine`, `!isLoadMore`)
1. `N = ChatManager.Instance.UnreadOnOpen`.
2. After bubbles spawn, find placement by walking **from the newest bubble backwards, counting incoming messages** until N incoming counted. Insert `unreadSeparatorPrefab` immediately above that bubble; call `SetCount(N)`. If fewer than N incoming exist in the loaded set, place at the very top of the loaded set.
3. Collect bubbles below the separator into `_unreadBubbles`.
4. If `N > 0`: scroll so the separator sits near the top of the viewport (instead of `= 0f` at :626); `fab.Show()`; `fab.SetCount(countBelowFold)`. If `N == 0`: keep jump-to-bottom; FAB hidden; no separator.

### On scroll (`OnScroll`, after existing pagination block)
- **FAB visibility:** show when `verticalNormalizedPosition > 0.05f` AND content is scrollable (`content.height > viewport.height`); hide otherwise.
- **Badge:** count `_unreadBubbles` whose top edge is below the viewport's bottom edge (world-corner comparison). Iterate newest→oldest, stop at first visible. Throttle to ~0.05s.

### On live arrival (`AppendLiveMessagesRoutine`, MessageListView.cs:246)
- Append new bubbles to `_unreadBubbles`. After layout settles, refresh FAB visibility + badge.
- If at bottom: existing slide-up reveals them → visible → not below fold → badge stays 0, FAB hidden (geometry handles it).
- If scrolled up: below fold → badge increments. Separator is unchanged (static open-snapshot marker).

### Tap FAB (`OnClicked`)
- Animate `verticalNormalizedPosition → 0` (~0.3s, OutCubic, DOTween); kill the tween if the user grabs the scroll mid-animation. On arrival: `SetCount(0)`, `Hide()`, zero scroll velocity.

### Lifecycle
- Separator persists for the visit. Leaving destroys everything (`HandleSlideOutComplete`); reopening the same chat finds unread already zeroed → `N == 0` → no separator ("what's new *this* visit").

## Visual spec (reference units, 1080×1920, ~3u = 1dp)

- **FAB:** 120×120 white circle (~40dp); touch target ≥132 (44dp) via padded hit area; chevron is an **Image sprite** (~52u, `#54656F`) — *not* a TMP glyph; circle via RoundedCorners; soft shadow. Anchored bottom-right, 48u margins, above the input bar.
- **Badge:** pill min 48u tall, fill `#26B25A` (the app's unread green, `ChatItemView.UnreadTimeColor`, [ChatItemView.cs:402](../../../Assets/Scripts/UI/ChatItemView.cs:402)); white text 28u bold; hidden at 0; anchored top-right of FAB.
- **Separator:** full-width bar, fill `#26B25A` @ 12% alpha; label 32u (matches DateSeparator) bold UPPERCASE `#1E7E45`, +0.6 tracking; vertical padding ~16–20u.
- **Motion (DOTween):** FAB fade 0.2s; scroll-to-bottom ~0.3s OutCubic; press `DOPunchScale`.

## How it's built

A `[MenuItem]` editor builder (`Assets/Editor/UnreadMarkersBuilder.cs`) following the `AttachmentPreviewScreenBuilder` pattern constructs (a) the `ScrollToBottomFab` GameObject anchored in the chat panel and (b) the `UnreadSeparator` prefab modeled on [DateSeparator.prefab](../../../Assets/Prefabs/DateSeparator.prefab). Per past UI-builder lessons: set rounded corners explicitly, set TMP alignment, use sprite icons not TMP glyphs, and size in reference units.

## Edge cases

- **N == 0:** no separator; FAB purely scroll-driven (badge hidden).
- **N ≥ loaded messages:** separator at top of loaded set; pagination loads older (read) messages above it — boundary stays correct.
- **Mixed tail (own messages interleaved):** placement counts *incoming* only, so the line sits before the first unread incoming message.
- **Content not scrollable (short chat):** FAB never shows.
- **Rapid scroll:** badge recompute throttled (~0.05s) and early-exits; no per-frame full scans.
- **Pagination shifts:** `_unreadBubbles` holds RectTransform refs (stable), not sibling indices (which shift when older messages prepend).
- **Chat switch mid-animation:** `OnChatSelected`/`HandleSlideOutComplete` stop coroutines, clear `_unreadBubbles`, hide FAB.

## Testing (EditMode, matching `AttachmentDisplayFormatTests` pattern)

Extract pure helpers so the math is testable without a live scene:
- `UnreadSeparatorPlacement.IndexForUnreadCount(IReadOnlyList<bool> isIncomingNewestFirst, int n)` → separator index; covers N=0, N≥count, mixed tail.
- `ScrollFabMath.CountBelowFold(IReadOnlyList<float> bubbleTopY, float viewportBottomY)` → badge count; covers all-below, all-visible, partial.
- `UnreadSeparatorView` label pluralization (1 vs N).

## Out of scope (YAGNI)

- No per-message read receipts or persisted read state.
- No live separator that moves for messages arriving while scrolled up (badge handles "new below"; separator stays the open-snapshot marker).
- No "jump to first unread" beyond the initial open landing.
- No reuse of the FAB outside the chat list (built reusable, but only wired here).

## Files

**New:**
- `Assets/Scripts/Chat/ScrollToBottomFab.cs`
- `Assets/Scripts/Chat/UnreadSeparatorView.cs`
- `Assets/Scripts/Chat/UnreadSeparatorPlacement.cs` (pure helper)
- `Assets/Scripts/Chat/ScrollFabMath.cs` (pure helper)
- `Assets/Editor/UnreadMarkersBuilder.cs`
- `Assets/Prefabs/UnreadSeparator.prefab`
- `Assets/Tests/Editor/Chat/UnreadSeparatorPlacementTests.cs`
- `Assets/Tests/Editor/Chat/ScrollFabMathTests.cs`

**Modified:**
- `Assets/Scripts/UI/MessageListView.cs` (orchestration)
- `Assets/Scripts/Main/ChatManager.cs` (`UnreadOnOpen` snapshot)
- `Assets/Scenes/Main.unity` (FAB instance + prefab refs wired by the builder)
