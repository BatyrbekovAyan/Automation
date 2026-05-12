# WhatsApp Chats List Search Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a scroll-with-content search bar above the WhatsApp chats list that live-filters chats by `Title` and `LastMessage` (case- and culture-insensitive), with the search row sitting as the first child inside the existing ScrollRect content so it scrolls naturally with the list.

**Architecture:** Two new files (`ChatSearchBar.cs` runtime script + `ChatsSearchBarBuilder.cs` editor menu) plus surgical edits to `ChatListView.cs` and `ChatItemView.cs`. Filter is applied by toggling `SetActive` on already-instantiated `ChatItemView`s — no list rebuilds, no avatar reload churn. The editor builder is strictly additive: it only adds one new GameObject and never mutates any existing UI.

**Tech Stack:** Unity 6 (6000.3.9f1), URP, TextMeshPro, UnityEngine.UI, RoundedCorners package (already in project).

**Reference spec:** [docs/superpowers/specs/2026-05-12-whatsapp-chat-search-bar-design.md](../specs/2026-05-12-whatsapp-chat-search-bar-design.md)

**Project verification reality:** This Unity project has no automated UI test framework. After each task, verification is via:
- C# compile check (the `validate-cs.sh` hook on `.claude/hooks/` runs automatically after every `Edit`/`Write` to a `.cs` file). If it logs no errors, the code compiles.
- Where applicable, a manual Editor check is described in the task.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `Assets/Scripts/UI/ChatItemView.cs` | Modify | Expose `Vm` getter; route bubble-to-top through `ChatListView.RaiseToTop` |
| `Assets/Scripts/UI/ChatListView.cs` | Modify | Add `RaiseToTop`, rewrite `ClearChatList`, add filter subscription/state, apply filter on `AddChat` |
| `Assets/Scripts/UI/ChatSearchBar.cs` | Create | Owns the input + clear button; fires `OnQueryChanged` |
| `Assets/Editor/ChatsSearchBarBuilder.cs` | Create | `[MenuItem]` that programmatically builds the search row inside `ChatsPanel.content` as first sibling; idempotent and additive-only |
| `Assets/Scenes/Main.unity` | Modify | One new GameObject (`ChatsSearchBar`) inserted by running the builder; nothing else changes |

---

## Task 1: Expose `Vm` getter on `ChatItemView`

The filter logic in `ChatListView` (added in Task 5) needs to read the bound `ChatViewModel` of each instantiated item without re-binding. Today the field is private — this task exposes a read-only getter. Smallest possible change, zero behavior impact.

**Files:**
- Modify: `Assets/Scripts/UI/ChatItemView.cs:20`

- [ ] **Step 1: Add a read-only `Vm` getter next to the existing `vm` field**

Open `Assets/Scripts/UI/ChatItemView.cs`. Find the field declaration on line 20:

```csharp
    private ChatViewModel vm;
    private string chatId;
```

Replace with:

```csharp
    private ChatViewModel vm;
    public ChatViewModel Vm => vm;
    private string chatId;
```

- [ ] **Step 2: Verify compile**

The `.claude/hooks/validate-cs.sh` hook runs automatically after the edit. Confirm no C# errors were logged. If errors appear, fix them before moving on.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/ChatItemView.cs
git commit -m "$(cat <<'EOF'
refactor(chat): expose read-only Vm getter on ChatItemView

Prep for chat-list search filter, which needs to read each item's
ViewModel without re-binding.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add `RaiseToTop` to `ChatListView` and route `ChatItemView.OnLastMessageChanged` through it

Today, when a chat receives a new message, `ChatItemView.OnLastMessageChanged` calls `transform.SetAsFirstSibling()` to bubble the row to the top of the list. Once we add the search bar as the first child of `content` (Task 7), that call would push the chat above the search bar.

Fix it now — before the search bar exists — by routing the move through a new `ChatListView.RaiseToTop` method that's header-aware. Until the search bar is in the scene, `RaiseToTop` behaves identically to `SetAsFirstSibling` (it picks sibling index 0 because no header is present), so this change is a pure refactor with no observable behavior delta.

**Files:**
- Modify: `Assets/Scripts/UI/ChatListView.cs`
- Modify: `Assets/Scripts/UI/ChatItemView.cs`

- [ ] **Step 1: Add `RaiseToTop` to `ChatListView`**

Open `Assets/Scripts/UI/ChatListView.cs`. Add this method directly above `OnDestroy()` (around line 76):

```csharp
    // Header-aware bubble-to-top. When a ChatsSearchBar header sits at
    // sibling index 0, chat rows must land at index 1 so the search row
    // stays pinned to the top of the scroll content.
    public void RaiseToTop(ChatItemView item)
    {
        if (item == null || content == null) return;

        int firstChatIndex = 0;
        if (content.childCount > 0)
        {
            var first = content.GetChild(0);
            if (first != null && first.GetComponent<ChatSearchBar>() != null)
                firstChatIndex = 1;
        }

        item.transform.SetSiblingIndex(firstChatIndex);
    }
```

- [ ] **Step 2: Cache `parentList` in `ChatItemView.Bind` and call `RaiseToTop` in `OnLastMessageChanged`**

Open `Assets/Scripts/UI/ChatItemView.cs`. Add a private field next to the other fields (under the `Vm` getter you added in Task 1):

```csharp
    private ChatViewModel vm;
    public ChatViewModel Vm => vm;
    private string chatId;
    private ChatListView parentList;
    private Coroutine avatarLoadCoroutine;
```

Inside `Bind(ChatViewModel model)`, cache the parent reference once. Add this block at the very top of the method body (before the existing `if (vm != null)` unsubscribe block):

```csharp
public void Bind(ChatViewModel model)
{
    if (parentList == null)
        parentList = GetComponentInParent<ChatListView>();

    if (vm != null)
    {
        vm.OnUpdated -= OnVmUpdated;
        vm.OnLastMessageChanged -= OnLastMessageChanged;
    }
    // ... rest of Bind unchanged
```

Then locate `OnLastMessageChanged` (around line 161):

```csharp
    private void OnLastMessageChanged(ChatViewModel vmRef)
    {
        // Move this row to the top of the list — fires only when the last message actually changed
        transform.SetAsFirstSibling();
    }
```

Replace with:

```csharp
    private void OnLastMessageChanged(ChatViewModel vmRef)
    {
        // Move this row to the top — header-aware via ChatListView so a
        // ChatsSearchBar at sibling index 0 isn't pushed out of the way.
        if (parentList != null)
            parentList.RaiseToTop(this);
        else
            transform.SetAsFirstSibling();
    }
```

- [ ] **Step 3: Verify compile**

Confirm `validate-cs.sh` logs no errors.

- [ ] **Step 4: Manual editor sanity check**

Open Unity Editor. Enter Play mode on `Main.unity`. Navigate to the WhatsApp chats list. Send/receive a real message to one of the chats (or trigger a fresh `OnLastMessageChanged` by any normal means). Expected: the chat row still bubbles to the top of the list exactly as it did before. No visual regression. Exit Play mode.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/ChatListView.cs Assets/Scripts/UI/ChatItemView.cs
git commit -m "$(cat <<'EOF'
refactor(chat): route bubble-to-top through ChatListView.RaiseToTop

Replaces transform.SetAsFirstSibling() in ChatItemView with a call to
a new header-aware RaiseToTop on the parent ChatListView. Behavior is
identical until a ChatSearchBar header is added at sibling index 0,
at which point chat rows will land at index 1 instead of pushing the
search bar out of view.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Rewrite `ChatListView.ClearChatList` to iterate `itemsByChatId.Values`

Today `ClearChatList()` indiscriminately destroys every child of `content`. Once the search bar lives inside `content` (Task 7), that loop would destroy it on every bot switch. Switch to iterating the items the view actually tracks. This is a strict improvement regardless of the search feature — the new version is also more defensive against any future non-item siblings.

**Files:**
- Modify: `Assets/Scripts/UI/ChatListView.cs:25-35`

- [ ] **Step 1: Replace the foreach with a dictionary walk**

Open `Assets/Scripts/UI/ChatListView.cs`. Locate `ClearChatList` (around line 25):

```csharp
    void ClearChatList()
    {
        // 1. Wipe the unified chat list
        foreach (Transform child in content) 
        {
            Destroy(child.gameObject);
        }
        
        // 2. Clear the dictionary so we don't hold "ghost" references in memory!
        itemsByChatId.Clear();
    }
```

Replace with:

```csharp
    void ClearChatList()
    {
        // Destroy only the items this view tracks — leaves any non-item
        // siblings (e.g. ChatsSearchBar header) intact across bot switches.
        foreach (var item in itemsByChatId.Values)
        {
            if (item != null) Destroy(item.gameObject);
        }
        itemsByChatId.Clear();
    }
```

- [ ] **Step 2: Verify compile**

Confirm `validate-cs.sh` logs no errors.

- [ ] **Step 3: Manual editor sanity check**

Open Unity Editor. Play `Main.unity`. Open the WhatsApp page. Switch between active bots (or trigger any normal flow that fires `OnChatListCleared`). Expected: chat list clears and re-populates exactly as before; no orphan items remain. Exit Play mode.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/ChatListView.cs
git commit -m "$(cat <<'EOF'
refactor(chat): clear only tracked items in ChatListView

Replaces the foreach-Destroy-every-child loop with a walk over
itemsByChatId.Values. Defensive improvement that becomes a hard
requirement once the search bar header lives inside the same
VerticalLayoutGroup.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Create `ChatSearchBar.cs` runtime script

A standalone MonoBehaviour that owns the input field + clear button and exposes a single `OnQueryChanged(string)` event. Compiles in isolation. Nothing else references it yet — wiring happens in Task 5, the GameObject is built in Task 6, the scene placement happens in Task 7.

**Files:**
- Create: `Assets/Scripts/UI/ChatSearchBar.cs`

- [ ] **Step 1: Create the file with full content**

Create `Assets/Scripts/UI/ChatSearchBar.cs` with:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatSearchBar : MonoBehaviour
{
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Button clearButton;
    [SerializeField] private GameObject clearIcon;

    public event Action<string> OnQueryChanged;
    public string CurrentQuery { get; private set; } = "";

    private void Awake()
    {
        if (input != null)
            input.onValueChanged.AddListener(HandleChanged);

        if (clearButton != null)
            clearButton.onClick.AddListener(Clear);

        if (clearIcon != null)
            clearIcon.SetActive(false);
    }

    private void HandleChanged(string raw)
    {
        var trimmed = string.IsNullOrEmpty(raw) ? "" : raw.Trim();

        if (clearIcon != null)
            clearIcon.SetActive(trimmed.Length > 0);

        if (trimmed == CurrentQuery) return; // dedupe noisy IME events
        CurrentQuery = trimmed;
        OnQueryChanged?.Invoke(trimmed);
    }

    public void Clear()
    {
        if (input != null) input.text = "";
    }

    private void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(HandleChanged);
        if (clearButton != null) clearButton.onClick.RemoveListener(Clear);
    }
}
```

- [ ] **Step 2: Verify compile**

Confirm `validate-cs.sh` logs no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/ChatSearchBar.cs
git commit -m "$(cat <<'EOF'
feat(chat): add ChatSearchBar runtime component

Owns the input field + clear button and emits OnQueryChanged when
the trimmed query changes. Standalone — wiring into ChatListView
and scene placement land in follow-up commits.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Wire `ChatListView` to filter on `OnQueryChanged`

`ChatListView` finds the `ChatSearchBar` at runtime via `GetComponentInChildren<ChatSearchBar>(true)`, subscribes to its `OnQueryChanged` event, stores the current query, and applies the filter both reactively (on each change) and proactively (on each new `AddChat`, so newly-arrived chats during an active filter respect it). Matching uses `CultureInfo.InvariantCulture.CompareInfo.IndexOf(..., CompareOptions.IgnoreCase)` for proper Unicode case-folding — the chats often have Cyrillic titles.

Until Task 7 places a `ChatSearchBar` in the scene, `GetComponentInChildren` returns null and the filter wiring is dormant. No observable behavior change in this task on its own.

**Files:**
- Modify: `Assets/Scripts/UI/ChatListView.cs`

- [ ] **Step 1: Add the using directive and new fields**

Open `Assets/Scripts/UI/ChatListView.cs`. At the top, add the `System.Globalization` using to the existing using block:

```csharp
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
```

Add new private fields above `Start()`:

```csharp
    private Dictionary<string, ChatItemView> itemsByChatId = new();

    private ChatSearchBar searchBar;
    private string currentQuery = "";
    private static readonly CompareInfo Ci = CultureInfo.InvariantCulture.CompareInfo;
```

- [ ] **Step 2: Discover and subscribe to `ChatSearchBar` in `Start`**

Locate `Start()` (around line 13). Add the search-bar discovery + subscription block after the existing manager subscriptions, before the `foreach (var chat in manager.Chats)` loop:

```csharp
    void Start()
    {
        var manager = ChatManager.Instance;
        manager.OnChatAdded += AddChat;
        manager.OnChatListCleared += ClearChatList;
        manager.OnEmptyState += HandleEmptyState;
        manager.OnActiveBotChanged += HandleActiveBotChanged;

        searchBar = GetComponentInChildren<ChatSearchBar>(true);
        if (searchBar != null)
            searchBar.OnQueryChanged += ApplyFilter;

        foreach (var chat in manager.Chats)
            AddChat(chat);
    }
```

- [ ] **Step 3: Apply the current filter to newly-added chats**

Locate `AddChat(ChatViewModel vm)` (around line 37). Replace its body with the version below — the only addition is the `ApplyMatchToItem(item, vm)` call at the end:

```csharp
    void AddChat(ChatViewModel vm)
    {
        // Real data came in — make sure our content panel is visible.
        if (content != null && !content.gameObject.activeSelf)
        {
            content.gameObject.SetActive(true);
        }

        // --- THE FIX: Everything goes into the normalContent now! ---
        var item = Instantiate(prefab, content);
        item.Bind(vm);
        itemsByChatId[vm.ChatId] = item;

        // Since Manager sends them in order, SetAsLastSibling
        // puts them in the correct sequence. Empty chats will naturally pile at the bottom.
        item.transform.SetAsLastSibling();
        item.transform.localScale = Vector3.one;

        // Apply the active filter so newly-arriving chats respect any query
        // the user has typed (e.g. after a bot switch with a query still set).
        ApplyMatchToItem(item, vm);

        // Row movement on update is handled inside ChatItemView.OnVmUpdated, which
        // unsubscribes itself in OnDestroy. Don't re-subscribe here — that leaks closures.
    }
```

- [ ] **Step 4: Add `ApplyFilter`, `ApplyMatchToItem`, and `Matches`**

Add these methods directly above `RaiseToTop` (which you added in Task 2):

```csharp
    private void ApplyFilter(string query)
    {
        currentQuery = query ?? "";
        foreach (var kvp in itemsByChatId)
        {
            var item = kvp.Value;
            if (item == null) continue;
            ApplyMatchToItem(item, item.Vm);
        }
    }

    private void ApplyMatchToItem(ChatItemView item, ChatViewModel vm)
    {
        if (item == null) return;
        bool match = Matches(vm, currentQuery);
        if (item.gameObject.activeSelf != match)
            item.gameObject.SetActive(match);
    }

    private static bool Matches(ChatViewModel vm, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        if (vm == null) return false;

        if (!string.IsNullOrEmpty(vm.Title)
            && Ci.IndexOf(vm.Title, q, CompareOptions.IgnoreCase) >= 0)
            return true;

        if (!string.IsNullOrEmpty(vm.LastMessage)
            && Ci.IndexOf(vm.LastMessage, q, CompareOptions.IgnoreCase) >= 0)
            return true;

        return false;
    }
```

- [ ] **Step 5: Re-evaluate match when an existing item's last message changes**

When a live message updates a `ChatViewModel.LastMessage`, the row's match against the current query may flip. Today `ChatItemView.OnLastMessageChanged` only does the bubble-to-top — we also need to re-run the filter for that item.

The cleanest hook is inside `ChatListView.RaiseToTop`, which is already called from `ChatItemView.OnLastMessageChanged` (Task 2). Extend it to re-apply the filter:

Open `Assets/Scripts/UI/ChatListView.cs`. Locate `RaiseToTop` and replace with:

```csharp
    public void RaiseToTop(ChatItemView item)
    {
        if (item == null || content == null) return;

        int firstChatIndex = 0;
        if (content.childCount > 0)
        {
            var first = content.GetChild(0);
            if (first != null && first.GetComponent<ChatSearchBar>() != null)
                firstChatIndex = 1;
        }

        item.transform.SetSiblingIndex(firstChatIndex);

        // The chat's last message just changed — its visibility under the
        // active query may have flipped (e.g. it now matches and should
        // appear, or no longer matches and should hide).
        ApplyMatchToItem(item, item.Vm);
    }
```

- [ ] **Step 6: Unsubscribe in `OnDestroy`**

Locate `OnDestroy()` (around line 76) and extend it to unsubscribe the search bar:

```csharp
    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatAdded -= AddChat;
            ChatManager.Instance.OnChatListCleared -= ClearChatList;
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
        }

        if (searchBar != null)
            searchBar.OnQueryChanged -= ApplyFilter;
    }
```

- [ ] **Step 7: Verify compile**

Confirm `validate-cs.sh` logs no errors.

- [ ] **Step 8: Manual editor sanity check**

Open Unity Editor. Play `Main.unity`. Open the WhatsApp page. Expected: list behaves exactly as before — no search bar is visible yet because none exists in the scene. `Start()` calls `GetComponentInChildren<ChatSearchBar>(true)` which returns null, so the subscription block is skipped. Verify no `NullReferenceException` in the console. Exit Play mode.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/UI/ChatListView.cs
git commit -m "$(cat <<'EOF'
feat(chat): wire ChatListView to filter on ChatSearchBar query

Discovers a ChatSearchBar in the chat list's content via runtime
GetComponentInChildren, subscribes to OnQueryChanged, and toggles
SetActive on each tracked ChatItemView using a culture-invariant
case-insensitive Contains over Title and LastMessage. Newly-added
chats respect the active query. Filter re-evaluates per item when
its last message changes (via RaiseToTop). Dormant until a search
bar GameObject is added to the scene.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Create `ChatsSearchBarBuilder.cs` editor menu

A `[MenuItem("Tools/UI/Build Chats Search Bar")]` that programmatically constructs the search-bar GameObject hierarchy per the spec §3 visuals and parents it as the first sibling of `ChatsPanel.ChatListView.content`. Strict rules:

1. **No mutation of any existing object.** It only creates one new tree and parents it.
2. **Idempotent.** If a `ChatsSearchBar` already exists, log and abort.
3. **Validating.** Verifies the active selection is `ChatsPanel`, has a `ChatListView`, and that `ChatListView.content` is non-null with a `VerticalLayoutGroup`. Aborts cleanly otherwise.
4. **Reversible.** Deleting the resulting `ChatsSearchBar` GameObject reverts the change.

**Files:**
- Create: `Assets/Editor/ChatsSearchBarBuilder.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Editor/ChatsSearchBarBuilder.cs` with the full content below. Reuses field types from the runtime script and matches the visual spec from §3 of the design doc (pill height 80, row 112, corner radius 24, `#EFEFF0` fill, etc.).

```csharp
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ChatsSearchBarBuilder
{
    private const string SearchBarName = "ChatsSearchBar";

    [MenuItem("Tools/UI/Build Chats Search Bar")]
    public static void Build()
    {
        var selection = Selection.activeGameObject;
        if (selection == null || selection.name != "ChatsPanel")
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: select the ChatsPanel GameObject in the Hierarchy first.");
            return;
        }

        var listView = selection.GetComponent<ChatListView>();
        if (listView == null)
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: selected GameObject has no ChatListView component.");
            return;
        }

        var content = listView.content;
        if (content == null)
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: ChatListView.content is unassigned.");
            return;
        }

        if (content.GetComponent<VerticalLayoutGroup>() == null)
        {
            Debug.LogError(
                "ChatsSearchBarBuilder: ChatListView.content has no VerticalLayoutGroup.");
            return;
        }

        var existing = content.Find(SearchBarName);
        if (existing != null)
        {
            Debug.Log(
                $"ChatsSearchBarBuilder: '{SearchBarName}' already exists under "
                + $"{content.name}. Aborting — delete it first if you want to rebuild.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Register the operation as a single undo step.
        Undo.SetCurrentGroupName("Build Chats Search Bar");
        int undoGroup = Undo.GetCurrentGroup();

        var row = CreateRow(content);
        var pill = CreatePill(row.transform);
        var magnifier = CreateMagnifier(pill.transform);
        var input = CreateInput(pill.transform);
        var clearButton = CreateClearButton(pill.transform, out var clearIcon);

        var bar = row.AddComponent<ChatSearchBar>();
        var so = new SerializedObject(bar);
        so.FindProperty("input").objectReferenceValue = input;
        so.FindProperty("clearButton").objectReferenceValue = clearButton;
        so.FindProperty("clearIcon").objectReferenceValue = clearIcon;
        so.ApplyModifiedPropertiesWithoutUndo();

        row.transform.SetAsFirstSibling();

        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = row;
        EditorUtility.SetDirty(content);
        Debug.Log("ChatsSearchBarBuilder: built ChatsSearchBar under " + content.name);
    }

    private static GameObject CreateRow(Transform parent)
    {
        var go = new GameObject(SearchBarName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create ChatsSearchBar");
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 112);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 112;
        le.preferredHeight = 112;

        return go;
    }

    private static GameObject CreatePill(Transform parent)
    {
        var go = new GameObject("Pill",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(HorizontalLayoutGroup));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(-32, 80); // full width − 32 (16 each side), 80 tall

        var img = go.GetComponent<Image>();
        img.color = HexColor("#EFEFF0");
        img.raycastTarget = true;

        // Project ships the RoundedCorners package. The component name lives in
        // the Nobi.UiRoundedCorners namespace; we look it up by type name to
        // avoid a hard compile dependency in this editor script.
        var roundedType = System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners, Assembly-CSharp")
                         ?? System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners");
        if (roundedType != null)
        {
            var rounded = go.AddComponent(roundedType);
            var radiusProp = roundedType.GetField("radius");
            if (radiusProp != null) radiusProp.SetValue(rounded, 24f);
        }
        else
        {
            Debug.LogWarning(
                "ChatsSearchBarBuilder: ImageWithRoundedCorners type not found — "
                + "pill will render as a hard rectangle. Add the rounded-corner "
                + "component manually if needed.");
        }

        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(20, 16, 0, 0);
        hlg.spacing = 16;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        return go;
    }

    private static GameObject CreateMagnifier(Transform parent)
    {
        var go = new GameObject("Magnifier",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = HexColor("#8E8E93");
        img.raycastTarget = false;
        // Built-in Unity sprite used as a placeholder glyph. Replace with the
        // project's magnifier sprite in the Inspector after build if a custom
        // asset exists.
        var fallback = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (fallback != null) img.sprite = fallback;

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 32;
        le.preferredWidth = 32;
        le.minHeight = 32;
        le.preferredHeight = 32;

        return go;
    }

    private static TMP_InputField CreateInput(Transform parent)
    {
        var go = new GameObject("Input",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(TMP_InputField), typeof(LayoutElement));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(1, 1, 1, 0); // transparent background, pill provides fill
        img.raycastTarget = true;

        var le = go.GetComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minHeight = 60;
        le.preferredHeight = 60;

        // Text viewport
        var viewport = new GameObject("Text Area",
            typeof(RectTransform), typeof(RectMask2D));
        viewport.layer = LayerMask.NameToLayer("UI");
        viewport.transform.SetParent(go.transform, false);
        var viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;

        // Placeholder
        var placeholder = new GameObject("Placeholder",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        placeholder.layer = LayerMask.NameToLayer("UI");
        placeholder.transform.SetParent(viewport.transform, false);
        var placeholderRt = (RectTransform)placeholder.transform;
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;
        var placeholderTmp = placeholder.GetComponent<TextMeshProUGUI>();
        placeholderTmp.text = "Search";
        placeholderTmp.fontSize = 30;
        placeholderTmp.color = HexColor("#8E8E93");
        placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderTmp.raycastTarget = false;
        placeholderTmp.enableWordWrapping = false;
        placeholderTmp.overflowMode = TextOverflowModes.Ellipsis;

        // Active text
        var text = new GameObject("Text",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        text.layer = LayerMask.NameToLayer("UI");
        text.transform.SetParent(viewport.transform, false);
        var textRt = (RectTransform)text.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var textTmp = text.GetComponent<TextMeshProUGUI>();
        textTmp.text = "";
        textTmp.fontSize = 30;
        textTmp.color = HexColor("#111111");
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;
        textTmp.raycastTarget = false;
        textTmp.enableWordWrapping = false;
        textTmp.overflowMode = TextOverflowModes.Ellipsis;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = viewportRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.fontAsset = textTmp.font;
        input.caretWidth = 2;
        input.customCaretColor = true;
        input.caretColor = HexColor("#00A884");
        input.selectionColor = new Color(0, 0.66f, 0.52f, 0.25f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.text = "";

        return input;
    }

    private static Button CreateClearButton(Transform parent, out GameObject clearIcon)
    {
        var go = new GameObject("ClearButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = HexColor("#C7C7CC");
        img.raycastTarget = true;
        var fallback = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (fallback != null) img.sprite = fallback;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 40;
        le.preferredWidth = 40;
        le.minHeight = 40;
        le.preferredHeight = 40;

        // Expand hit area to 80×80 via a transparent child Image.
        var hit = new GameObject("HitArea",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        hit.layer = LayerMask.NameToLayer("UI");
        hit.transform.SetParent(go.transform, false);
        var hitRt = (RectTransform)hit.transform;
        hitRt.anchorMin = new Vector2(0.5f, 0.5f);
        hitRt.anchorMax = new Vector2(0.5f, 0.5f);
        hitRt.pivot = new Vector2(0.5f, 0.5f);
        hitRt.sizeDelta = new Vector2(80, 80);
        var hitImg = hit.GetComponent<Image>();
        hitImg.color = new Color(0, 0, 0, 0);
        hitImg.raycastTarget = true;

        // "✕" glyph child — toggled via clearIcon SetActive
        var x = new GameObject("X",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        x.layer = LayerMask.NameToLayer("UI");
        x.transform.SetParent(go.transform, false);
        var xRt = (RectTransform)x.transform;
        xRt.anchorMin = Vector2.zero;
        xRt.anchorMax = Vector2.one;
        xRt.offsetMin = Vector2.zero;
        xRt.offsetMax = Vector2.zero;
        var xTmp = x.GetComponent<TextMeshProUGUI>();
        xTmp.text = "✕"; // ✕
        xTmp.fontSize = 28;
        xTmp.color = Color.white;
        xTmp.alignment = TextAlignmentOptions.Center;
        xTmp.raycastTarget = false;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colors.selectedColor = Color.white;
        btn.colors = colors;

        clearIcon = go; // toggle the whole ClearButton GameObject's visibility
        return btn;
    }

    private static Color HexColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.magenta;
    }
}
```

- [ ] **Step 2: Verify compile**

Confirm `validate-cs.sh` logs no errors. The editor script will only be compiled by Unity when the Editor reloads.

- [ ] **Step 3: Verify Unity picks up the menu item**

Open Unity Editor (or trigger a recompile via `Cmd+R` if it's already open). Look at the top menu: `Tools > UI > Build Chats Search Bar` should appear. **Do not run it yet** — that happens in Task 7. If the menu item is missing, check the console for compile errors and fix them.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/ChatsSearchBarBuilder.cs
git commit -m "$(cat <<'EOF'
feat(editor): add ChatsSearchBarBuilder menu

[MenuItem("Tools/UI/Build Chats Search Bar")] that programmatically
constructs the search row inside the ChatsPanel.ChatListView.content
VerticalLayoutGroup as first sibling. Strictly additive — validates
the selection, idempotent (aborts if a search bar already exists),
and registers a single undo group so deleting the resulting
GameObject is a clean revert.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Run the builder, save the scene, and verify the feature end-to-end

This is a Unity Editor step — you'll open the project, select the ChatsPanel, run the menu, save the scene, and walk the §8 verification checks from the spec.

**Files:**
- Modify: `Assets/Scenes/Main.unity` (one new GameObject inserted; nothing else changes)

- [ ] **Step 1: Open Unity and run the builder**

1. Open `Automation.sln` via Unity Hub (Unity 6000.3.9f1).
2. Open `Assets/Scenes/Main.unity`.
3. In the Hierarchy, expand `Canvas → Screen_Whatsapp` and select the `ChatsPanel` GameObject.
4. From the top menu, click `Tools → UI → Build Chats Search Bar`.
5. Expected console output: `ChatsSearchBarBuilder: built ChatsSearchBar under <content-name>`.
6. The Hierarchy now shows a new `ChatsSearchBar` GameObject as the first child of `ChatsPanel.ChatListView.content`. The selection automatically jumps to it.

If you see an error in the console instead, fix the issue (e.g., wrong selection, missing component) and re-run.

- [ ] **Step 2: Verify the builder is idempotent**

Run `Tools → UI → Build Chats Search Bar` a second time. Expected console output: `ChatsSearchBarBuilder: 'ChatsSearchBar' already exists under <content-name>. Aborting...`. No duplicate is created. The selection jumps to the existing search bar.

- [ ] **Step 3: Visual check in Scene + Game view**

1. In the Game view, set the resolution to **1080 × 2400** (Portrait).
2. The search bar should sit above the first chat row inside the scrolling chat list area:
   - Light-gray rounded pill (`#EFEFF0`, 24 px radius), 80 px tall, 16 px inset from each side of the row.
   - Magnifier glyph on the left (gray `#8E8E93`).
   - "Search" placeholder in `#8E8E93` text.
   - No clear (X) button visible.
3. If the pill renders as a hard rectangle, the RoundedCorners component wasn't found by reflection — open the `Pill` GameObject and add `ImageWithRoundedCorners` manually with `radius = 24`.

- [ ] **Step 4: Save the scene**

`File → Save` (or `Cmd+S`). This is when the new GameObject becomes part of the `.unity` file.

- [ ] **Step 5: Play-mode verification — the full §8 checklist**

Enter Play mode and walk through the spec's verification checks. For each, the expected result is in the table below:

| Check | Expected |
|---|---|
| Open the WhatsApp page | Search bar visible at the top of the chat list with the §3 visual spec. |
| Scroll the chat list down | Search bar scrolls out of view together with the chats; scrolling back to the top brings it back. |
| Tap the search bar | TMP_InputField gains focus; system keyboard appears; caret renders teal (`#00A884`). |
| Type `"a"` | Chats whose Title or LastMessage contains "a" (case-insensitive) remain visible. Others hide; VLG reflows. Clear button (X) appears on the right of the pill. |
| Type Cyrillic letters that match a Cyrillic-titled chat | That chat remains visible (Invariant culture IgnoreCase). |
| Tap the X clear button | Field clears; all chats reappear; keyboard stays up. |
| Switch active bot mid-filter | New bot's chats load; filter still applied to the new list. |
| Send a message to a visible chat | Chat moves to sibling index 1 (top of *chats*), search bar stays at index 0. |
| Send a message to a hidden chat that the query now matches | The chat appears at the top of the filtered list (sibling index 1). |
| Backspace until empty | All chats reappear; clear button hides. |

If any check fails, exit Play mode, fix the issue, and re-test. Do not move to the next step until every check passes.

- [ ] **Step 6: Commit the scene change**

```bash
git add Assets/Scenes/Main.unity
git commit -m "$(cat <<'EOF'
feat(chat): place ChatsSearchBar in WhatsApp chats list

Built via Tools/UI/Build Chats Search Bar — adds a single new
ChatsSearchBar GameObject as the first child of the chats list's
VerticalLayoutGroup content. No existing UI is modified.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-review (run before declaring the plan done)

The plan author already walked the four self-review checks:

- **Spec coverage:** Every section of the design doc maps to at least one task.
  - §3 visual design → Task 6 (builder constructs row/pill/magnifier/input/clear-button per spec).
  - §4.1 ChatSearchBar.cs → Task 4.
  - §4.2 ChatsSearchBarBuilder.cs → Task 6.
  - §4.3 ChatListView edits → Tasks 2, 3, 5.
  - §4.4 ChatItemView edits → Tasks 1, 2.
  - §5 data flow → covered transitively by Tasks 2 and 5 (filter on add, filter on query change, filter on last-message change via RaiseToTop).
  - §6 edge cases — Cyrillic via `Ci` (Task 5); whitespace via `.Trim()` (Task 4); empty state untouched (no work needed); no-results placeholder explicitly out of scope.
  - §7 file-by-file summary — mirrors File Structure table at the top of this plan.
  - §8 verification — Task 7 Step 5 walks the full checklist.

- **Placeholder scan:** No "TBD", "TODO", "implement later", "similar to Task N" anywhere. Every code step shows the full code to write.

- **Type consistency:** Verified — `OnQueryChanged` event signature, `CurrentQuery` property, `Matches`/`ApplyFilter`/`ApplyMatchToItem`/`RaiseToTop` names match across Tasks 2, 5, and 6.

- **Scope:** Single feature, single page, ~7 atomic tasks. Fits one plan.
