# WhatsApp Chats List Search Bar — Design

**Date:** 2026-05-12
**Scope:** `Screen_Whatsapp/ChatsPanel`
**Goal:** Add a search bar above the chats list that filters chats by title and last-message preview, matching WhatsApp iOS's scroll-with-content behavior.

---

## 1. Summary

The WhatsApp chats page currently has no way to find a specific chat in the list. This spec adds a search input that:

- Sits as the first element inside the chat list's scroll content (so it scrolls away with the list, exactly like WhatsApp iOS).
- Live-filters the visible chats as the user types — case- and culture-insensitive `Contains` match against `ChatViewModel.Title` OR `ChatViewModel.LastMessage`.
- Is implemented as a single additive change: one new runtime script, one new editor builder, one new prefab-like GameObject in the scene, plus surgical edits to `ChatListView` and `ChatItemView`.

Filter chips ("Unread", "Photos", etc.), a "no results" placeholder, and full message-history search are explicitly **out of scope** for v1 and can be bolted on later without rework.

## 2. User-visible behavior

| Action | Result |
|---|---|
| Open the WhatsApp page | A light-gray pill-shaped search bar is the first thing inside the chat list, above the first chat row. Placeholder text: "Search". |
| Scroll the chat list down | The search bar scrolls out of view together with the chats — it is part of the scroll content, not pinned. |
| Tap the search bar | TMP_InputField focuses; the system keyboard rises via existing `KeyboardAwarePanel` / `KeyboardScrollFix` infrastructure. |
| Type `"as"` | Chats whose `Title` or `LastMessage` contains `"as"` (case-insensitive, Unicode-aware) remain visible. Others hide. The VerticalLayoutGroup reflows. |
| Tap the X clear button | Field clears, all chats reappear, keyboard stays up. |
| Backspace until empty | Same as the clear button — all chats reappear. |
| Switch active bot while a query is active | New bot's chats load, then the cached query is applied — filter survives the bot switch. |
| Receive a live message on a hidden chat that the query now matches | The chat appears at the top of the filtered list. |
| Receive a live message on a visible chat | The chat row moves to the top of the chats — sibling index `1`, not `0` — search bar stays at index `0`. |

There is no "no results" empty state in v1 — if zero chats match, the area below the search bar is simply empty, matching WhatsApp's behavior.

## 3. Visual design

Sized for the project's 1080×2400 canvas; spacing strictly on the 4-px grid.

| Element | Spec |
|---|---|
| Row container (the new `ChatsSearchBar` GameObject) | full width × **112 px** tall; 16 px top + 16 px bottom padding |
| Search pill | full width − 32 px (16 each side); **80 px** tall; corner radius **24 px** via the RoundedCorners package |
| Pill background fill | `#EFEFF0` (iOS system search style) |
| Magnifier icon | 32 px square, 20 px from the pill's left edge; tint `#8E8E93` |
| Placeholder text | "Search" — TMP, 30 px, color `#8E8E93`, 16 px to the right of the magnifier |
| Active text | TMP, 30 px, color `#111111` |
| Clear button (X) | 40 px circle on the right edge of the pill (with 16 px right padding inside the pill); fill `#C7C7CC` with a white "✕"; hit area expanded to 80×80 to meet the 44 dp minimum touch target; **hidden when the input is empty** |
| Caret (cursor) color | `#00A884` — the project's existing teal, consistent with the avatar palette in `ChatItemView.AvatarColors` |

If the chats panel's existing background is anything other than white, the pill gray will need a small nudge for contrast — to be checked once during build, not parameterized in v1.

The pill itself is a `HorizontalLayoutGroup` with children `[Magnifier(Image), TMP_InputField, ClearButton]`.

## 4. Architecture

Two new files, three small edits to existing files. **No existing GameObject in `ChatsPanel` is moved, reparented, resized, or otherwise mutated** — the only structural change to the scene is adding the new `ChatsSearchBar` as the first child of the `content` VerticalLayoutGroup.

### 4.1 New runtime script — `Assets/Scripts/UI/ChatSearchBar.cs`

```csharp
public class ChatSearchBar : MonoBehaviour
{
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Button clearButton;
    [SerializeField] private GameObject clearIcon; // shown only when input has text

    public event Action<string> OnQueryChanged;
    public string CurrentQuery { get; private set; } = "";

    private void Awake()
    {
        input.onValueChanged.AddListener(HandleChanged);
        clearButton.onClick.AddListener(Clear);
        clearIcon.SetActive(false);
    }

    private void HandleChanged(string raw)
    {
        var trimmed = raw?.Trim() ?? "";
        clearIcon.SetActive(trimmed.Length > 0);
        if (trimmed == CurrentQuery) return; // dedupe noisy IME events
        CurrentQuery = trimmed;
        OnQueryChanged?.Invoke(trimmed);
    }

    public void Clear() => input.text = "";

    private void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(HandleChanged);
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
```

### 4.2 New editor builder — `Assets/Editor/ChatsSearchBarBuilder.cs`

A `[MenuItem("Tools/UI/Build Chats Search Bar")]` that constructs the row programmatically per §3, following the same pattern as `BotSettingsScrollableTextAreaBuilder` and `BotSettingsRebuilder`.

**Strict constraints — these are the rules the builder must obey:**

1. **No mutation of any existing object.** The builder creates exactly one new GameObject tree (`ChatsSearchBar` + its children) and parents it. It does **not** modify any `RectTransform`, `Image`, `LayoutGroup`, `ScrollRect`, or any component on `ChatsPanel`, `TopBar`, the scroll wrapper, or any existing sibling. It does **not** wire any SerializeField on any pre-existing script. (Runtime discovery — see §4.3 — replaces SerializeField wiring.)
2. **Idempotent.** Before doing anything, it checks `content.Find("ChatsSearchBar")`. If a search bar already exists there, it logs a message and aborts — no duplicate, no destroy.
3. **Validating.** Before touching the scene, it verifies:
   - The active selection is a GameObject named `ChatsPanel`,
   - It has a `ChatListView` component,
   - `ChatListView.content` is non-null and has a `VerticalLayoutGroup`.

   If any check fails, it aborts with a clear console message. It never silently mutates.
4. **Reversible.** Deleting the `ChatsSearchBar` GameObject after running the builder fully reverts the scene change.

### 4.3 Edits to `Assets/Scripts/UI/ChatListView.cs`

The existing `ChatListView` gains filter responsibility. Changes:

- Cache the search bar via `GetComponentInChildren<ChatSearchBar>(true)` in `Start()`. No SerializeField added — runtime discovery keeps the builder zero-touch.
- Store `currentQuery` and subscribe to `OnQueryChanged`.
- `AddChat` applies the current filter to newly-instantiated items, so new chats arriving during an active filter (e.g., after a bot switch or a fresh `OnLastMessageChanged`) respect it.
- **Rewrite `ClearChatList()`** to iterate `itemsByChatId.Values` rather than `foreach (Transform child in content)`. The old version indiscriminately destroyed every child of `content` — including, now, the search bar. The new version only destroys items the view actually tracks. This is a strict improvement regardless of the search feature.
- New method `RaiseToTop(ChatItemView item)`: computes the correct sibling index (`1` if a search-bar header exists at index 0, else `0`) and calls `item.transform.SetSiblingIndex(firstChatIndex)`.
- Unsubscribe `OnQueryChanged` in `OnDestroy`.

The filter match itself:

```csharp
private static readonly CompareInfo CI = CultureInfo.InvariantCulture.CompareInfo;

private static bool Matches(ChatViewModel vm, string q)
{
    if (string.IsNullOrEmpty(q)) return true;
    if (vm.Title != null
        && CI.IndexOf(vm.Title, q, CompareOptions.IgnoreCase) >= 0) return true;
    if (vm.LastMessage != null
        && CI.IndexOf(vm.LastMessage, q, CompareOptions.IgnoreCase) >= 0) return true;
    return false;
}
```

`CompareInfo.IndexOf` with `CompareOptions.IgnoreCase` handles full Unicode case folding — important because chats often have Cyrillic titles.

### 4.4 Edits to `Assets/Scripts/UI/ChatItemView.cs`

Two one-line additions:

- Expose `public ChatViewModel Vm => vm;` so `ChatListView.ApplyFilter` can re-evaluate match per item without a re-bind.
- Replace `transform.SetAsFirstSibling()` inside `OnLastMessageChanged` with `_parentList.RaiseToTop(this);`, where `_parentList` is the `ChatListView` cached once during `Bind` via `GetComponentInParent<ChatListView>()`.

This is the §4 "gotcha" from brainstorming: today `SetAsFirstSibling()` moves a freshly-updated chat to sibling index 0. With the search bar now at index 0, that would push the chat above the search bar. Routing through `ChatListView.RaiseToTop` makes the move header-aware.

## 5. Data flow

```
TMP_InputField.onValueChanged
        │
        ▼
ChatSearchBar.HandleChanged(raw)
   - Trim, dedupe
   - Toggle clear icon
   - Fire OnQueryChanged(trimmed)
        │
        ▼
ChatListView (subscriber)
   - currentQuery = trimmed
   - foreach (id, item) in itemsByChatId:
        item.gameObject.SetActive(Matches(item.Vm, trimmed))
        │
        ▼
VerticalLayoutGroup reflows
ScrollRect adjusts content size via ContentSizeFitter
```

For chats arriving after the filter is set (`AddChat`):

```
ChatManager.OnChatAdded(vm)
        │
        ▼
ChatListView.AddChat(vm)
   - Instantiate prefab into content
   - Apply Matches(vm, currentQuery) → SetActive
   - itemsByChatId[vm.ChatId] = item
```

For live message updates on chats already in the list:

```
ChatViewModel.UpdateLastMessage
        │
        ├─▶ OnUpdated → ChatItemView.OnVmUpdated (existing — refreshes text/avatar)
        │
        └─▶ OnLastMessageChanged → ChatItemView (existing — bubble-to-top)
                │
                ▼
              ChatItemView calls _parentList.RaiseToTop(this)
                │
                ▼
              ChatListView.RaiseToTop:
                - SetSiblingIndex(searchBarPresent ? 1 : 0)
                - Re-evaluate Matches(vm, currentQuery) → SetActive
                  (so a chat that newly matches becomes visible)
```

## 6. Edge cases & decisions

| Case | Decision |
|---|---|
| Whitespace-only query | Treated as empty (we `.Trim()` in `HandleChanged`). |
| Non-ASCII / Cyrillic query | Handled via `CompareInfo.IndexOf` with `CompareOptions.IgnoreCase`, invariant culture. Works for the existing Cyrillic chat titles in the scene. |
| Emoji in query or title | Passes through unchanged — Unicode `IndexOf` matches codepoint sequences. |
| Zero matches | No placeholder. Area below the search bar is empty, mirroring WhatsApp. |
| Existing `OnEmptyState` (no chats at all) | Already hides `content`. The search bar lives inside `content`, so it hides with it — correct. `OnActiveBotChanged` re-enables `content` and the search bar returns. |
| ~100 chats per keystroke | Each keystroke = N `SetActive` calls + one VLG rebuild. Trivial at this scale. |
| 500+ chats (hypothetical) | Add a 120 ms debounce on `OnQueryChanged`. **Do not preempt** — only add if testing shows stutter. |
| Hidden items still hold avatar sprites | Intentional. That's why Approach A (toggle visibility) was chosen over Approach B (rebuild list) — preserves cached avatars, no flicker. |
| Search across full message history | Out of scope for v1. Title + LastMessage only. |
| Filter chips (Unread / Photos / etc.) | Out of scope for v1. The architecture (single `OnQueryChanged` event) does not preclude adding them later. |

## 7. File-by-file change summary

| File | Change |
|---|---|
| `Assets/Scripts/UI/ChatSearchBar.cs` | **New.** Owns the input + clear button, fires `OnQueryChanged`. |
| `Assets/Editor/ChatsSearchBarBuilder.cs` | **New.** `[MenuItem]` that programmatically builds the search bar row inside `ChatsPanel.ChatListView.content` as first sibling. Idempotent, validating, additive-only. |
| `Assets/Scripts/UI/ChatListView.cs` | **Edit.** Discover `ChatSearchBar` at runtime, subscribe to `OnQueryChanged`, apply filter on add and on query change, rewrite `ClearChatList()` to use `itemsByChatId.Values`, add `RaiseToTop(ChatItemView)`. |
| `Assets/Scripts/UI/ChatItemView.cs` | **Edit.** Expose `public ChatViewModel Vm => vm`; replace `SetAsFirstSibling()` with `_parentList.RaiseToTop(this)` (cache `_parentList` in `Bind`). |
| `Assets/Scenes/Main.unity` | **Edit.** New child GameObject `ChatsSearchBar` appears as the first sibling of `ChatsPanel/ScrollRect/Viewport/content`. No existing GameObject is modified. |

## 8. Verification (manual)

The project has no automated UI test framework. Verification is by inspection in the Unity Editor's Game view (1080×2400) and on-device.

Checks to perform after build:

1. Search bar renders above the first chat with the §3 visual spec.
2. Scrolling the chat list down scrolls the search bar out of view; scrolling to the top brings it back.
3. Tapping the input opens the system keyboard; caret is teal.
4. Typing `"a"` filters; typing `"as"` further narrows; typing nonsense empties the list (no chats, no placeholder).
5. Typing Cyrillic letters that match a Cyrillic-titled chat returns that chat.
6. Tapping the X clear button restores the full list; keyboard stays up.
7. Switching active bot while a query is active loads the new bot's chats already filtered.
8. Sending a message into the app:
   - To a hidden chat the query now matches → chat becomes visible at top of the filtered list.
   - To a visible chat → chat moves to top of *chats* (sibling index 1), search bar remains at sibling index 0.
9. Running the editor builder twice does not create a duplicate (logs and aborts).
10. Deleting the `ChatsSearchBar` GameObject fully reverts the scene change with no orphan references in `ChatListView`.

## 9. Out of scope

- Filter chips below the input (Unread / Photos / Videos / Links / GIFs / Documents / Polls / Audio).
- Full message-history search (would require new Wappi endpoint usage or local indexing).
- "No chats found" empty state.
- Recent searches list.
- Animating the search bar (e.g., expand-from-icon on focus). Static placement only.
- Pinning the search bar to the top on focus (WhatsApp does a subtle pin; v1 just scrolls naturally with content per the brainstorming choice).

Any of these can be added later without revisiting v1 — the architecture is additive.
