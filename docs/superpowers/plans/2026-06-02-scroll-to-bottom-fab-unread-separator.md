# Scroll-to-bottom FAB + Unread Separator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WhatsApp-style scroll-to-bottom FAB (with unread-below-fold count badge) and an "N UNREAD MESSAGES" separator that marks where this visit's unread messages begin, landing the user there on open.

**Architecture:** Two dumb, reusable widgets (`ScrollToBottomFab`, `UnreadSeparatorView`) plus two pure, unit-tested helpers (`UnreadSeparatorPlacement`, `ScrollFabMath`). `MessageListView` orchestrates: it snapshots the open-time unread count from `ChatManager.UnreadOnOpen`, places the separator, lands at it, and drives FAB visibility/badge from scroll geometry. An editor `[MenuItem]` builder constructs the FAB in-scene and the separator prefab, then wires `MessageListView`'s refs.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, uGUI (`ScrollRect`, `VerticalLayoutGroup`, `CanvasGroup`), TextMeshPro, DOTween, `Nobi.UiRoundedCorners.ImageWithRoundedCorners`, `UnityEngine.InputSystem` (`Pointer.current`), NUnit EditMode tests.

---

## Executor notes (project-specific — read first)

This project does **not** use git worktrees and runs tests/compiles inside the user's already-open Unity Editor. Adapt the writing-plans defaults accordingly:

- **Compile:** "Run" steps that say *compile* mean: switch focus to the Unity Editor, let it auto-recompile, and confirm **0 errors** in the Console. There is no `dotnet build` / CLI compile in the loop.
- **EditMode tests:** Run via **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** (or right-click the specific test class ▸ Run). New tests live next to `Assets/Tests/Editor/Chat/AttachmentDisplayFormatTests.cs` — that folder compiles into `Assembly-CSharp-Editor`, which already sees runtime classes in `Assembly-CSharp` (confirmed: `DateSeparatorView` is `Assembly-CSharp::DateSeparatorView`). **No asmdef changes are needed.**
- **`.meta` files:** Unity generates a `.meta` for every new `.cs`/`.prefab` on import. Switch to Unity once after creating a file so the `.meta` appears, then stage **both** the asset and its `.meta` in the commit.
- **Commits / pushes:** ask the user for consent per task before committing (and never push unless asked). The commit commands below are ready to run once consent is given.
- **Reference-unit sizing:** the main Canvas is Scale-With-Screen-Size, 1080×1920, Match = Width → **~3 reference units = 1dp**. Every size in the builder is already expressed in reference units. Verify the result in Game view at **1080×2400**.

Spec: [docs/superpowers/specs/2026-06-02-scroll-to-bottom-fab-unread-separator-design.md](../specs/2026-06-02-scroll-to-bottom-fab-unread-separator-design.md)

---

## File Structure

**New runtime (compile into `Assembly-CSharp`):**
- `Assets/Scripts/Chat/UnreadSeparatorPlacement.cs` — pure static: where the separator sits in a newest-first bubble list.
- `Assets/Scripts/Chat/ScrollFabMath.cs` — pure static: count of bubbles below the viewport fold.
- `Assets/Scripts/Chat/UnreadSeparatorView.cs` — `MonoBehaviour` view + `static FormatLabel(int)`.
- `Assets/Scripts/Chat/ScrollToBottomFab.cs` — `MonoBehaviour` widget (Show/Hide/SetCount/OnClicked).

**New tests (compile into `Assembly-CSharp-Editor`):**
- `Assets/Tests/Editor/Chat/UnreadSeparatorPlacementTests.cs`
- `Assets/Tests/Editor/Chat/ScrollFabMathTests.cs`
- `Assets/Tests/Editor/Chat/UnreadSeparatorViewTests.cs`

**New editor + asset:**
- `Assets/Editor/UnreadMarkersBuilder.cs` — `[MenuItem]` builder.
- `Assets/Prefabs/UnreadSeparator.prefab` — produced by the builder.

**Modified:**
- `Assets/Scripts/Main/ChatManager.cs` — add `UnreadOnOpen` snapshot.
- `Assets/Scripts/UI/MessageListView.cs` — orchestration (refs, state, placement, landing, FAB/badge, live-arrival, tap).
- `Assets/Scenes/Main.unity` — FAB instance + wired `MessageListView` refs (written by the builder).

**Dependency order:** Task 1 (ChatManager) and Tasks 2–5 (helpers + widgets) are independent and come first; Task 6 (MessageListView) references all new types; Task 7 (builder) references MessageListView's new fields and the new widget/prefab; Task 8 is manual in-Editor verification.

---

## Task 1: `ChatManager.UnreadOnOpen` snapshot

Capture the server unread count at open **before** the optimistic zeroing, so `MessageListView` can read it when bubbles are built. No signature change to `OnChatSelected` (it has other subscribers).

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (add property near other public state; set inside `SelectChat`, around lines 342–351)

- [ ] **Step 1: Add the `UnreadOnOpen` property**

Add this public auto-property alongside ChatManager's other public members (e.g., just above `public void SelectChat(string chatId)` at [ChatManager.cs:322](../../../Assets/Scripts/Main/ChatManager.cs:322)):

```csharp
/// <summary>
/// Server-reported unread count captured at the instant a chat is opened, BEFORE the
/// optimistic local zeroing in SelectChat. MessageListView reads this when it builds
/// bubbles to place the "N unread" separator and seed the scroll-to-bottom badge.
/// 0 when the chat was already read (or unknown).
/// </summary>
public int UnreadOnOpen { get; private set; }
```

- [ ] **Step 2: Set it before the zeroing**

In `SelectChat`, the current block is:

```csharp
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            bool hadUnread = selectedVm.UnreadCount > 0;
            selectedVm.UpdateUnreadCount(0);

            if (hadUnread)
            {
                StartCoroutine(MarkChatAsRead(chatId));
            }
        }
```

Replace it with (capture `UnreadOnOpen` first; reset to 0 when the chat isn't in the lookup):

```csharp
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            UnreadOnOpen = selectedVm.UnreadCount;
            bool hadUnread = selectedVm.UnreadCount > 0;
            selectedVm.UpdateUnreadCount(0);

            if (hadUnread)
            {
                StartCoroutine(MarkChatAsRead(chatId));
            }
        }
        else
        {
            UnreadOnOpen = 0;
        }
```

- [ ] **Step 3: Compile**

Switch to Unity, let it recompile. Expected: **0 Console errors**. (`UnreadOnOpen` is read by `MessageListView` only in Task 6 — fine for now.)

- [ ] **Step 4: Commit** (ask consent first)

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): snapshot UnreadOnOpen before optimistic zeroing"
```

---

## Task 2: `UnreadSeparatorPlacement` pure helper (TDD)

Decides where the separator sits in a **newest-first** list of bubble incoming-flags: walk from newest, count incoming until `n` reached, return how many bubbles fall **below** the separator (newer side). Fewer than `n` incoming → place above everything.

**Files:**
- Create: `Assets/Scripts/Chat/UnreadSeparatorPlacement.cs`
- Test: `Assets/Tests/Editor/Chat/UnreadSeparatorPlacementTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/UnreadSeparatorPlacementTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class UnreadSeparatorPlacementTests
{
    private static List<bool> Incoming(params bool[] flags) => new List<bool>(flags);

    [Test]
    public void ZeroUnread_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, true, true), 0));
    }

    [Test]
    public void NegativeUnread_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, false), -3));
    }

    [Test]
    public void NullList_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(null, 2));
    }

    [Test]
    public void EmptyList_ReturnsZero()
    {
        Assert.AreEqual(0, UnreadSeparatorPlacement.IndexForUnreadCount(new List<bool>(), 3));
    }

    [Test]
    public void AllIncoming_TwoUnread_TwoBubblesBelow()
    {
        // newest-first [in,in,in,in], n=2 → separator above the 2nd-newest incoming
        Assert.AreEqual(2, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, true, true, true), 2));
    }

    [Test]
    public void SingleIncoming_OneUnread_ReturnsOne()
    {
        Assert.AreEqual(1, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true), 1));
    }

    [Test]
    public void MixedTail_OutgoingNewerThanUnread_CountsIncomingOnly()
    {
        // newest-first [out,out,in,in,out,in], n=2
        // walk: out, out, in(1), in(2==n) at index 3 → 4 bubbles below
        Assert.AreEqual(4, UnreadSeparatorPlacement.IndexForUnreadCount(
            Incoming(false, false, true, true, false, true), 2));
    }

    [Test]
    public void FewerIncomingThanUnread_PlacesAtTop()
    {
        // [in,out,in], n=5 → only 2 incoming → top = count = 3
        Assert.AreEqual(3, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, false, true), 5));
    }

    [Test]
    public void ExactIncomingCount_PlacesAtTop()
    {
        // [in,out,in], n=2 → in(1), in(2==n) at index 2 → 3 below
        Assert.AreEqual(3, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(true, false, true), 2));
    }

    [Test]
    public void NoIncoming_PlacesAtTop()
    {
        Assert.AreEqual(2, UnreadSeparatorPlacement.IndexForUnreadCount(Incoming(false, false), 1));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

In Unity ▸ Test Runner ▸ EditMode, run `UnreadSeparatorPlacementTests`.
Expected: **compile error / FAIL** — `UnreadSeparatorPlacement` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/UnreadSeparatorPlacement.cs`:

```csharp
using System.Collections.Generic;

/// <summary>
/// Pure placement math for the "N unread" separator. The message list is laid out
/// newest-at-bottom, so callers pass bubble incoming-flags newest-first (index 0 = newest).
/// Returns the number of bubbles that fall BELOW the separator (on the newer side): the
/// separator is positioned immediately above the Nth-newest incoming message, so all N
/// unread incoming messages — plus any newer outgoing ones — sit below it. When fewer than
/// N incoming messages are loaded, the separator goes above everything (returns the full
/// count). n &lt;= 0 or a null/empty list returns 0 (caller draws no separator).
/// </summary>
public static class UnreadSeparatorPlacement
{
    public static int IndexForUnreadCount(IReadOnlyList<bool> isIncomingNewestFirst, int n)
    {
        if (isIncomingNewestFirst == null || n <= 0) return 0;

        int incomingSeen = 0;
        for (int i = 0; i < isIncomingNewestFirst.Count; i++)
        {
            if (isIncomingNewestFirst[i]) incomingSeen++;
            if (incomingSeen == n) return i + 1;
        }

        return isIncomingNewestFirst.Count;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Unity ▸ Test Runner ▸ EditMode ▸ run `UnreadSeparatorPlacementTests`.
Expected: **all 10 tests PASS**.

- [ ] **Step 5: Commit** (ask consent first; switch to Unity once so the `.meta` files exist)

```bash
git add Assets/Scripts/Chat/UnreadSeparatorPlacement.cs Assets/Scripts/Chat/UnreadSeparatorPlacement.cs.meta \
        Assets/Tests/Editor/Chat/UnreadSeparatorPlacementTests.cs Assets/Tests/Editor/Chat/UnreadSeparatorPlacementTests.cs.meta
git commit -m "feat(chat): add UnreadSeparatorPlacement helper + tests"
```

---

## Task 3: `ScrollFabMath` pure helper (TDD)

Counts how many tracked unread bubbles sit entirely below the viewport's bottom edge. World Y increases upward, so "below the fold" means a bubble's top-edge world Y is strictly below the viewport-bottom world Y. Order-independent.

**Files:**
- Create: `Assets/Scripts/Chat/ScrollFabMath.cs`
- Test: `Assets/Tests/Editor/Chat/ScrollFabMathTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/ScrollFabMathTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class ScrollFabMathTests
{
    private static List<float> Tops(params float[] ys) => new List<float>(ys);

    [Test]
    public void AllBelowFold_CountsAll()
    {
        Assert.AreEqual(3, ScrollFabMath.CountBelowFold(Tops(-10f, -20f, -30f), 0f));
    }

    [Test]
    public void AllVisible_CountsZero()
    {
        Assert.AreEqual(0, ScrollFabMath.CountBelowFold(Tops(10f, 20f, 30f), 0f));
    }

    [Test]
    public void Partial_CountsOnlyBelow()
    {
        Assert.AreEqual(2, ScrollFabMath.CountBelowFold(Tops(-5f, 5f, -15f, 25f), 0f));
    }

    [Test]
    public void BoundaryExactlyAtFold_NotCounted()
    {
        // top exactly at the viewport bottom is "at" the fold, not below it
        Assert.AreEqual(1, ScrollFabMath.CountBelowFold(Tops(0f, -1f), 0f));
    }

    [Test]
    public void PositiveViewportBottom_ComparesCorrectly()
    {
        Assert.AreEqual(2, ScrollFabMath.CountBelowFold(Tops(140f, 160f, 100f), 150f));
    }

    [Test]
    public void EmptyList_CountsZero()
    {
        Assert.AreEqual(0, ScrollFabMath.CountBelowFold(new List<float>(), 0f));
    }

    [Test]
    public void NullList_CountsZero()
    {
        Assert.AreEqual(0, ScrollFabMath.CountBelowFold(null, 0f));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Unity ▸ Test Runner ▸ EditMode ▸ run `ScrollFabMathTests`.
Expected: **compile error / FAIL** — `ScrollFabMath` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/ScrollFabMath.cs`:

```csharp
using System.Collections.Generic;

/// <summary>
/// Pure math for the scroll-to-bottom badge: how many tracked unread bubbles are still
/// below the fold (i.e. the user has not scrolled down to them). World Y increases upward,
/// so a bubble is below the fold when its top edge (world Y) is strictly below the
/// viewport's bottom edge. A bubble whose top edge sits exactly on the fold counts as
/// visible (not below). Order-independent; pass the top-edge world Y of each tracked bubble.
/// </summary>
public static class ScrollFabMath
{
    public static int CountBelowFold(IReadOnlyList<float> bubbleTopY, float viewportBottomY)
    {
        if (bubbleTopY == null) return 0;

        int count = 0;
        for (int i = 0; i < bubbleTopY.Count; i++)
        {
            if (bubbleTopY[i] < viewportBottomY) count++;
        }
        return count;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Unity ▸ Test Runner ▸ EditMode ▸ run `ScrollFabMathTests`.
Expected: **all 7 tests PASS**.

- [ ] **Step 5: Commit** (ask consent first; switch to Unity once for `.meta`)

```bash
git add Assets/Scripts/Chat/ScrollFabMath.cs Assets/Scripts/Chat/ScrollFabMath.cs.meta \
        Assets/Tests/Editor/Chat/ScrollFabMathTests.cs Assets/Tests/Editor/Chat/ScrollFabMathTests.cs.meta
git commit -m "feat(chat): add ScrollFabMath below-fold helper + tests"
```

---

## Task 4: `UnreadSeparatorView` widget (TDD on the pure label)

A view modeled on `DateSeparatorView`. The pluralization is a `static` method so it's unit-testable without a live scene/font.

**Files:**
- Create: `Assets/Scripts/Chat/UnreadSeparatorView.cs`
- Test: `Assets/Tests/Editor/Chat/UnreadSeparatorViewTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/UnreadSeparatorViewTests.cs`:

```csharp
using NUnit.Framework;

public class UnreadSeparatorViewTests
{
    [TestCase(1,  "1 UNREAD MESSAGE")]
    [TestCase(2,  "2 UNREAD MESSAGES")]
    [TestCase(3,  "3 UNREAD MESSAGES")]
    [TestCase(0,  "0 UNREAD MESSAGES")]
    [TestCase(99, "99 UNREAD MESSAGES")]
    public void FormatLabel_Pluralizes(int count, string expected)
    {
        Assert.AreEqual(expected, UnreadSeparatorView.FormatLabel(count));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Unity ▸ Test Runner ▸ EditMode ▸ run `UnreadSeparatorViewTests`.
Expected: **compile error / FAIL** — `UnreadSeparatorView` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/UnreadSeparatorView.cs`:

```csharp
using UnityEngine;
using TMPro;

/// <summary>
/// Full-width "N UNREAD MESSAGES" divider inserted into the message stream at the
/// open-time unread boundary. Modeled on DateSeparatorView. The label text is built by
/// the pure static FormatLabel so pluralization is unit-testable.
/// </summary>
public class UnreadSeparatorView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    public void SetCount(int count)
    {
        if (label != null) label.text = FormatLabel(count);
    }

    public static string FormatLabel(int count) =>
        count == 1 ? "1 UNREAD MESSAGE" : $"{count} UNREAD MESSAGES";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Unity ▸ Test Runner ▸ EditMode ▸ run `UnreadSeparatorViewTests`.
Expected: **all 5 cases PASS**.

- [ ] **Step 5: Commit** (ask consent first; switch to Unity once for `.meta`)

```bash
git add Assets/Scripts/Chat/UnreadSeparatorView.cs Assets/Scripts/Chat/UnreadSeparatorView.cs.meta \
        Assets/Tests/Editor/Chat/UnreadSeparatorViewTests.cs Assets/Tests/Editor/Chat/UnreadSeparatorViewTests.cs.meta
git commit -m "feat(chat): add UnreadSeparatorView with testable label"
```

---

## Task 5: `ScrollToBottomFab` widget

A dumb, reusable FAB that knows nothing about chats. DOTween fade for show/hide; `DOPunchScale` press feedback; `OnClicked` event. Serialized refs are wired by the builder in Task 7.

**Files:**
- Create: `Assets/Scripts/Chat/ScrollToBottomFab.cs`

- [ ] **Step 1: Write the implementation**

Create `Assets/Scripts/Chat/ScrollToBottomFab.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Floating "scroll to newest" button with an unread-count badge. A self-contained widget:
/// it raises OnClicked and exposes Show/Hide/SetCount, but knows nothing about chats or
/// scrolling — MessageListView owns that policy. Starts hidden.
/// </summary>
public class ScrollToBottomFab : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private TMP_Text badgeText;

    public bool IsShown { get; private set; }

    public event Action OnClicked;

    private Tween _fadeTween;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        IsShown = false;
        if (badgeRoot != null) badgeRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
        _fadeTween?.Kill();
    }

    public void Show()
    {
        if (IsShown) return;
        IsShown = true;
        if (canvasGroup == null) return;

        _fadeTween?.Kill();
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        _fadeTween = canvasGroup.DOFade(1f, 0.2f).SetEase(Ease.OutQuad);
    }

    public void Hide()
    {
        if (!IsShown) return;
        IsShown = false;
        if (canvasGroup == null) return;

        _fadeTween?.Kill();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        _fadeTween = canvasGroup.DOFade(0f, 0.2f).SetEase(Ease.OutQuad);
    }

    public void SetCount(int count)
    {
        if (badgeRoot != null) badgeRoot.SetActive(count > 0);
        if (badgeText != null) badgeText.text = count > 99 ? "99+" : count.ToString();
    }

    private void HandleClick()
    {
        transform.DOPunchScale(Vector3.one * 0.08f, 0.2f, 6, 0.8f);
        OnClicked?.Invoke();
    }
}
```

- [ ] **Step 2: Compile**

Switch to Unity, let it recompile. Expected: **0 Console errors**.

- [ ] **Step 3: Commit** (ask consent first; switch to Unity once for `.meta`)

```bash
git add Assets/Scripts/Chat/ScrollToBottomFab.cs Assets/Scripts/Chat/ScrollToBottomFab.cs.meta
git commit -m "feat(chat): add ScrollToBottomFab widget"
```

---

## Task 6: `MessageListView` orchestration

Wire everything together: new refs/state, reset on chat switch, separator placement + land-at-separator on open, FAB visibility/badge on scroll, live-arrival tracking, and tap-to-bottom. All edits are to [Assets/Scripts/UI/MessageListView.cs](../../../Assets/Scripts/UI/MessageListView.cs).

**Files:**
- Modify: `Assets/Scripts/UI/MessageListView.cs`

- [ ] **Step 1: Add the DOTween import**

The file's `using` block (lines 1–7) has no DOTween. Add it after line 7 (`using UnityEngine.InputSystem;`):

```csharp
using DG.Tweening;
```

- [ ] **Step 2: Add serialized refs**

After the `dateSeparatorPrefab` field ([MessageListView.cs:22](../../../Assets/Scripts/UI/MessageListView.cs:22)), add:

```csharp
    [Header("Unread Markers")]
    [SerializeField] private UnreadSeparatorView unreadSeparatorPrefab;
    [SerializeField] private ScrollToBottomFab scrollToBottomFab;
```

- [ ] **Step 3: Add private state**

After `private readonly List<MessageViewModel> pendingLiveMessages = ...;` (line 46), add:

```csharp
    // --- Unread markers state (per chat visit) ---
    // Incoming bubbles below the open-snapshot separator + any live incoming arrivals,
    // tracked by RectTransform so pagination prepends (which shift sibling indices) don't
    // break the badge. The separator instance is a content child destroyed by Clear().
    private readonly List<RectTransform> _unreadBubbles = new List<RectTransform>();
    private RectTransform _unreadSeparatorInstance;
    private float _lastFabRefreshTime;
    private Tween _scrollToBottomTween;
```

- [ ] **Step 4: Subscribe/unsubscribe to FAB clicks**

In `OnEnable`, after the `scrollRect.onValueChanged.AddListener(OnScroll);` block (after line 88), add:

```csharp
        if (scrollToBottomFab != null)
        {
            scrollToBottomFab.OnClicked += HandleScrollToBottomClicked;
        }
```

In `OnDisable`, after the `scrollRect.onValueChanged.RemoveListener(OnScroll);` block (after line 109), add:

```csharp
        if (scrollToBottomFab != null)
        {
            scrollToBottomFab.OnClicked -= HandleScrollToBottomClicked;
        }
```

- [ ] **Step 5: Reset unread state on chat exit and chat switch**

In `HandleSlideOutComplete` ([MessageListView.cs:119](../../../Assets/Scripts/UI/MessageListView.cs:119)), after `Clear();` (line 122), add:

```csharp
        ResetUnreadState();
```

In `OnChatSelected`, after the trailing `Clear();` (line 153), add:

```csharp
        ResetUnreadState();
```

Then add the helper itself (place it right after `OnChatSelected`, before `OnScroll`):

```csharp
    void ResetUnreadState()
    {
        _unreadBubbles.Clear();
        _unreadSeparatorInstance = null; // its GameObject is a content child destroyed by Clear()
        _scrollToBottomTween?.Kill();
        _scrollToBottomTween = null;

        if (scrollToBottomFab != null)
        {
            scrollToBottomFab.SetCount(0);
            scrollToBottomFab.Hide();
        }
    }
```

- [ ] **Step 6: Cancel auto-scroll on user grab + refresh FAB in `OnScroll`**

In `OnScroll` ([MessageListView.cs:156](../../../Assets/Scripts/UI/MessageListView.cs:156)), add the grab-cancel guard at the very top of the method (before the `if (!isLoadingData && hasMoreMessages)` block):

```csharp
        // If the user grabs the list mid auto-scroll, cancel the tween so we don't fight them.
        if (_scrollToBottomTween != null && _scrollToBottomTween.IsActive()
            && Pointer.current != null && Pointer.current.press.isPressed)
        {
            _scrollToBottomTween.Kill();
            _scrollToBottomTween = null;
        }
```

And add the FAB refresh as the **last** line of `OnScroll` (after the existing pagination `if` block closes, before the method's closing brace at line 183):

```csharp
        RefreshFab();
```

- [ ] **Step 7: Add the FAB/badge engine**

Add these three methods (place them after `OnScroll`, before `HandleBatchMessages`):

```csharp
    void RefreshFab()
    {
        if (scrollToBottomFab == null || scrollRect == null) return;

        var contentRt = (RectTransform)content;
        bool scrollable = contentRt.rect.height > scrollRect.viewport.rect.height + 1f;
        bool scrolledUp = scrollRect.verticalNormalizedPosition > 0.05f;

        if (scrollable && scrolledUp) scrollToBottomFab.Show();
        else scrollToBottomFab.Hide();

        // Throttle the heavier below-fold recompute (~20 Hz). Show/Hide above is cheap
        // (no-op when already in the target state) so it stays responsive every event.
        if (Time.unscaledTime - _lastFabRefreshTime < 0.05f) return;
        _lastFabRefreshTime = Time.unscaledTime;

        scrollToBottomFab.SetCount(ComputeBelowFoldCount());
    }

    int ComputeBelowFoldCount()
    {
        if (scrollRect == null || _unreadBubbles.Count == 0) return 0;

        Vector3[] vp = new Vector3[4];
        scrollRect.viewport.GetWorldCorners(vp); // 0=BL, 1=TL, 2=TR, 3=BR
        float viewportBottomWorldY = vp[0].y;

        var tops = new List<float>(_unreadBubbles.Count);
        Vector3[] c = new Vector3[4];
        for (int i = 0; i < _unreadBubbles.Count; i++)
        {
            var rt = _unreadBubbles[i];
            if (rt == null) continue;
            rt.GetWorldCorners(c);
            tops.Add(c[1].y); // top-left world Y
        }

        return ScrollFabMath.CountBelowFold(tops, viewportBottomWorldY);
    }

    void HandleScrollToBottomClicked()
    {
        if (scrollRect == null) return;

        _scrollToBottomTween?.Kill();
        scrollRect.velocity = Vector2.zero;

        _scrollToBottomTween = DOTween.To(
                () => scrollRect.verticalNormalizedPosition,
                v => scrollRect.verticalNormalizedPosition = v,
                0f, 0.3f)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                scrollRect.verticalNormalizedPosition = 0f;
                scrollRect.velocity = Vector2.zero;
                _scrollToBottomTween = null;
                if (scrollToBottomFab != null)
                {
                    scrollToBottomFab.SetCount(0);
                    scrollToBottomFab.Hide();
                }
            });
    }
```

- [ ] **Step 8: Track live incoming arrivals + refresh at the end of `AppendLiveMessagesRoutine`**

`AppendLiveMessagesRoutine` ends at [MessageListView.cs:345](../../../Assets/Scripts/UI/MessageListView.cs:345). The current tail is:

```csharp
    if (scrollRect) scrollRect.velocity = Vector2.zero;

    if (scrollRect && wasAtBottom && startNorm > 0f)
        yield return StartCoroutine(SlideUpRevealRoutine(startNorm));
    else if (scrollRect && wasAtBottom)
        scrollRect.verticalNormalizedPosition = 0f;
}
```

Replace it with (track only **incoming** new bubbles; refresh FAB after the scroll settles — geometry handles the at-bottom case where they're visible → not counted):

```csharp
    if (scrollRect) scrollRect.velocity = Vector2.zero;

    if (scrollRect && wasAtBottom && startNorm > 0f)
        yield return StartCoroutine(SlideUpRevealRoutine(startNorm));
    else if (scrollRect && wasAtBottom)
        scrollRect.verticalNormalizedPosition = 0f;

    // Track live incoming arrivals for the badge. When at bottom they slide into view and
    // sit above the fold (not counted); when scrolled up they're below the fold (counted).
    foreach (var item in newlyAddedItems)
    {
        if (item != null && item.BoundVm != null && item.BoundVm.isIncoming)
            _unreadBubbles.Add((RectTransform)item.transform);
    }

    Canvas.ForceUpdateCanvases();
    RefreshFab();
}
```

- [ ] **Step 9: Replace the jump-to-bottom with separator placement + landing**

In `UpdateListRoutine`, the current tail-of-initial-build block is ([MessageListView.cs:624-627](../../../Assets/Scripts/UI/MessageListView.cs:624)):

```csharp
        if (!isLoadMore)
        {
            if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
        }
```

Replace it with:

```csharp
        if (!isLoadMore)
        {
            PlaceUnreadSeparatorAndLand();
        }
```

- [ ] **Step 10: Add the placement + landing methods**

Add these methods after `UpdateListRoutine` (before `Clear()` at line 651):

```csharp
    // Called at the end of the initial build (!isLoadMore). Reads ChatManager.UnreadOnOpen,
    // inserts the separator above the oldest unread incoming message, tracks the unread
    // bubbles for the badge, and either lands at the separator (N > 0) or jumps to the
    // newest message (N == 0).
    void PlaceUnreadSeparatorAndLand()
    {
        int n = ChatManager.Instance != null ? ChatManager.Instance.UnreadOnOpen : 0;

        _unreadBubbles.Clear();

        if (n <= 0 || unreadSeparatorPrefab == null)
        {
            if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
            RefreshFab();
            return;
        }

        // Content is ordered oldest→newest by sibling index (backwards spawn + SetAsFirstSibling),
        // so walk children high→low to build a newest-first view, skipping spacers/date separators.
        var bubblesNewestFirst = new List<RectTransform>();
        var isIncomingNewestFirst = new List<bool>();
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var bubble = content.GetChild(i).GetComponent<MessageItemView>();
            if (bubble == null || bubble.BoundVm == null) continue;
            bubblesNewestFirst.Add((RectTransform)bubble.transform);
            isIncomingNewestFirst.Add(bubble.BoundVm.isIncoming);
        }

        int belowCount = UnreadSeparatorPlacement.IndexForUnreadCount(isIncomingNewestFirst, n);

        // Track ONLY the unread incoming bubbles below the separator (own messages aren't unread).
        for (int i = 0; i < belowCount && i < bubblesNewestFirst.Count; i++)
        {
            if (isIncomingNewestFirst[i]) _unreadBubbles.Add(bubblesNewestFirst[i]);
        }

        var sep = Instantiate(unreadSeparatorPrefab, content);
        sep.SetCount(n);
        _unreadSeparatorInstance = (RectTransform)sep.transform;

        bool placeAtTop = bubblesNewestFirst.Count == 0
                          || belowCount <= 0
                          || belowCount >= bubblesNewestFirst.Count;
        if (placeAtTop)
        {
            sep.transform.SetAsFirstSibling();
        }
        else
        {
            // Insert immediately above the oldest unread bubble (= just below the newest read one).
            var oldestUnread = bubblesNewestFirst[belowCount - 1];
            sep.transform.SetSiblingIndex(oldestUnread.GetSiblingIndex());
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());

        ScrollSeparatorToTop();

        Canvas.ForceUpdateCanvases();
        RefreshFab();
    }

    // Scrolls so the separator's top edge sits at the viewport's top edge. Pivot- and
    // canvas-scale-agnostic: convert the separator's top world corner into content-local
    // space, measure its distance from the content's top edge, normalize against scrollable
    // height. verticalNormalizedPosition: 1 = top, 0 = bottom.
    void ScrollSeparatorToTop()
    {
        if (scrollRect == null || _unreadSeparatorInstance == null) return;

        Canvas.ForceUpdateCanvases();

        var contentRt = (RectTransform)content;
        float scrollableH = contentRt.rect.height - scrollRect.viewport.rect.height;
        if (scrollableH <= 1f)
        {
            scrollRect.verticalNormalizedPosition = 0f; // too short to scroll; stay at bottom
            return;
        }

        Vector3[] corners = new Vector3[4];
        _unreadSeparatorInstance.GetWorldCorners(corners); // 1 = top-left
        Vector3 sepTopLocal = contentRt.InverseTransformPoint(corners[1]);

        float distanceFromTop = Mathf.Clamp(contentRt.rect.yMax - sepTopLocal.y, 0f, scrollableH);
        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - distanceFromTop / scrollableH);
    }
```

- [ ] **Step 11: Compile**

Switch to Unity, let it recompile. Expected: **0 Console errors**. (The two new serialized refs are unwired until Task 7 — that's expected; null-guards keep it safe.)

- [ ] **Step 12: Commit** (ask consent first)

```bash
git add Assets/Scripts/UI/MessageListView.cs
git commit -m "feat(chat): orchestrate unread separator + scroll-to-bottom FAB in MessageListView"
```

---

## Task 7: `UnreadMarkersBuilder` editor script

A `[MenuItem]` builder (AttachmentPreviewScreenBuilder pattern) that (a) builds the `ScrollToBottomFab` GameObject in the chat panel, (b) builds the `UnreadSeparator.prefab`, and (c) wires `MessageListView`'s two refs. Per UI-builder lessons: rounded corners via `ImageWithRoundedCorners`, centered TMP alignment, **Image sprite** chevron (not a TMP glyph), every size in reference units.

**Files:**
- Create: `Assets/Editor/UnreadMarkersBuilder.cs`
- Produces: `Assets/Prefabs/UnreadSeparator.prefab`
- Modifies: `Assets/Scenes/Main.unity` (FAB instance + wired refs)

- [ ] **Step 1: Write the builder**

Create `Assets/Editor/UnreadMarkersBuilder.cs`:

```csharp
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// <summary>
/// Builds the scroll-to-bottom FAB (in the chat panel, beside the message ScrollRect) and
/// the UnreadSeparator prefab, then wires MessageListView's serialized refs. All sizes are
/// in 1080×1920 reference units (~3u = 1dp). Idempotent: destroys any prior FAB first.
///
/// Run: Tools ▸ Chat ▸ Build Unread Markers. Afterward, assign the chevron sprite on
/// ScrollToBottomFab/Circle/Chevron in the inspector (a down-chevron, ~52u, #54656F).
/// </summary>
public static class UnreadMarkersBuilder
{
    private const string FabName       = "ScrollToBottomFab";
    private const string SeparatorPath = "Assets/Prefabs/UnreadSeparator.prefab";

    // Reference units (dp × 3).
    private const float FabHitSize    = 132f; // 44dp touch target
    private const float FabCircleSize = 120f; // 40dp visible circle
    private const float ChevronSize   = 52f;
    private const float BadgeMinSize  = 48f;
    private const float RightMargin   = 48f;
    private const float BottomMargin  = 160f; // clears the input bar; nudge in-scene if needed
    private const float SeparatorHeight = 72f;

    private static readonly Color UnreadGreen     = new Color32(0x26, 0xB2, 0x5A, 0xFF); // #26B25A
    private static readonly Color SeparatorBarBg  = new Color32(0x26, 0xB2, 0x5A, 0x1F); // #26B25A @ ~12%
    private static readonly Color SeparatorLabel  = new Color32(0x1E, 0x7E, 0x45, 0xFF); // #1E7E45
    private static readonly Color ChevronGrey     = new Color32(0x54, 0x65, 0x6F, 0xFF); // #54656F
    private static readonly Color White           = Color.white;

    [MenuItem("Tools/Chat/Build Unread Markers")]
    public static void Build()
    {
        var mlv = Object.FindFirstObjectByType<MessageListView>(FindObjectsInactive.Include);
        if (mlv == null)
        {
            Debug.LogError("[UnreadMarkersBuilder] MessageListView not found in the open scene.");
            return;
        }
        if (mlv.scrollRect == null)
        {
            Debug.LogError("[UnreadMarkersBuilder] MessageListView.scrollRect is not assigned.");
            return;
        }

        Transform panel = mlv.scrollRect.transform.parent; // chat panel that holds the scroll view + input bar
        if (panel == null)
        {
            Debug.LogError("[UnreadMarkersBuilder] Could not resolve the chat panel (scrollRect has no parent).");
            return;
        }

        var fab = BuildFab(panel);
        var separatorPrefab = BuildSeparatorPrefab();

        WireMessageListView(mlv, fab, separatorPrefab);

        EditorSceneManager.MarkSceneDirty(mlv.gameObject.scene);
        Debug.Log("[UnreadMarkersBuilder] Built FAB + UnreadSeparator.prefab and wired MessageListView. " +
                  "Assign the down-chevron sprite on ScrollToBottomFab/Circle/Chevron in the inspector.");
    }

    // ── FAB ───────────────────────────────────────────────────────────────────────
    private static ScrollToBottomFab BuildFab(Transform panel)
    {
        var existing = panel.Find(FabName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // Root = transparent hit area (≥132u touch target) + Button + CanvasGroup + script.
        var root = NewChild(panel, FabName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button),
            typeof(ScrollToBottomFab));
        root.transform.SetAsLastSibling(); // render above the message list
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = new Vector2(1f, 0f);
        rootRt.anchorMax = new Vector2(1f, 0f);
        rootRt.pivot     = new Vector2(1f, 0f);
        rootRt.sizeDelta = new Vector2(FabHitSize, FabHitSize);
        rootRt.anchoredPosition = new Vector2(-RightMargin, BottomMargin);

        var hitImg = root.GetComponent<Image>();
        hitImg.color = new Color(1f, 1f, 1f, 0f); // invisible, but raycastable
        hitImg.raycastTarget = true;

        var cg = root.GetComponent<CanvasGroup>();
        cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;

        var btn = root.GetComponent<Button>();
        btn.targetGraphic = hitImg;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        // Visible white circle (centered), rounded to a circle + soft shadow.
        var circle = NewChild(root.transform, "Circle",
            typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners), typeof(Shadow));
        var circleRt = (RectTransform)circle.transform;
        circleRt.anchorMin = circleRt.anchorMax = new Vector2(0.5f, 0.5f);
        circleRt.pivot = new Vector2(0.5f, 0.5f);
        circleRt.sizeDelta = new Vector2(FabCircleSize, FabCircleSize);
        var circleImg = circle.GetComponent<Image>();
        circleImg.color = White;
        circleImg.raycastTarget = false;
        var circleRounded = circle.GetComponent<ImageWithRoundedCorners>();
        circleRounded.radius = FabCircleSize / 2f;
        circleRounded.Validate();
        circleRounded.Refresh();
        var shadow = circle.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
        shadow.effectDistance = new Vector2(0f, -4f);

        // Chevron (Image sprite — NOT a TMP glyph; sprite assigned by user in inspector).
        var chevron = NewChild(circle.transform, "Chevron", typeof(RectTransform), typeof(Image));
        var chevronRt = (RectTransform)chevron.transform;
        chevronRt.anchorMin = chevronRt.anchorMax = new Vector2(0.5f, 0.5f);
        chevronRt.pivot = new Vector2(0.5f, 0.5f);
        chevronRt.sizeDelta = new Vector2(ChevronSize, ChevronSize);
        var chevronImg = chevron.GetComponent<Image>();
        chevronImg.color = ChevronGrey;
        chevronImg.raycastTarget = false;
        chevronImg.preserveAspect = true;

        // Badge pill (top-right of FAB), grows with text.
        var badge = NewChild(root.transform, "Badge",
            typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var badgeRt = (RectTransform)badge.transform;
        badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(1f, 1f);
        badgeRt.pivot = new Vector2(1f, 1f);
        badgeRt.sizeDelta = new Vector2(BadgeMinSize, BadgeMinSize);
        badgeRt.anchoredPosition = new Vector2(-6f, -6f);
        var badgeImg = badge.GetComponent<Image>();
        badgeImg.color = UnreadGreen;
        badgeImg.raycastTarget = false;
        var badgeRounded = badge.GetComponent<ImageWithRoundedCorners>();
        badgeRounded.radius = BadgeMinSize / 2f;
        badgeRounded.Validate();
        badgeRounded.Refresh();
        var badgeHlg = badge.GetComponent<HorizontalLayoutGroup>();
        badgeHlg.padding = new RectOffset(12, 12, 0, 0);
        badgeHlg.childAlignment = TextAnchor.MiddleCenter;
        badgeHlg.childControlWidth = badgeHlg.childControlHeight = true;
        badgeHlg.childForceExpandWidth = badgeHlg.childForceExpandHeight = false;
        var badgeFitter = badge.GetComponent<ContentSizeFitter>();
        badgeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        badgeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var badgeText = NewChild(badge.transform, "BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        var badgeTmp = badgeText.GetComponent<TextMeshProUGUI>();
        badgeTmp.text = "";
        badgeTmp.fontSize = 28f;
        badgeTmp.fontStyle = FontStyles.Bold;
        badgeTmp.color = White;
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.enableWordWrapping = false;
        badgeTmp.overflowMode = TextOverflowModes.Overflow;
        badgeTmp.raycastTarget = false;

        // Wire the ScrollToBottomFab serialized refs.
        var fabScript = root.GetComponent<ScrollToBottomFab>();
        var so = new SerializedObject(fabScript);
        SetRef(so, "button",      btn);
        SetRef(so, "canvasGroup", cg);
        SetRef(so, "badgeRoot",   badge);
        SetRef(so, "badgeText",   badgeTmp);
        so.ApplyModifiedPropertiesWithoutUndo();

        badge.SetActive(false); // hidden until count > 0
        return fabScript;
    }

    // ── Separator prefab ────────────────────────────────────────────────────────────
    private static UnreadSeparatorView BuildSeparatorPrefab()
    {
        // Root: full-width bar (Image bg), flexibleWidth so the message list's
        // VerticalLayoutGroup stretches it edge-to-edge; fixed height via LayoutElement.
        var root = new GameObject("UnreadSeparator",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(UnreadSeparatorView));
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(0f, SeparatorHeight);
        var bar = root.GetComponent<Image>();
        bar.color = SeparatorBarBg;
        bar.raycastTarget = false;
        var le = root.GetComponent<LayoutElement>();
        le.minHeight = SeparatorHeight;
        le.preferredHeight = SeparatorHeight;
        le.flexibleWidth = 1f;

        var labelGo = NewChild(root.transform, "Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "0 UNREAD MESSAGES";
        label.fontSize = 32f;
        label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        label.characterSpacing = 6f; // ~+0.6 tracking
        label.color = SeparatorLabel;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        var view = root.GetComponent<UnreadSeparatorView>();
        var so = new SerializedObject(view);
        SetRef(so, "label", label);
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(root, SeparatorPath, out bool ok);
        Object.DestroyImmediate(root);
        if (!ok || saved == null)
        {
            Debug.LogError($"[UnreadMarkersBuilder] Failed to save prefab at {SeparatorPath}");
            return null;
        }
        return saved.GetComponent<UnreadSeparatorView>();
    }

    private static void WireMessageListView(MessageListView mlv, ScrollToBottomFab fab, UnreadSeparatorView sepPrefab)
    {
        var so = new SerializedObject(mlv);
        SetRef(so, "scrollToBottomFab",   fab);
        SetRef(so, "unreadSeparatorPrefab", sepPrefab);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── helpers ────────────────────────────────────────────────────────────────────
    private static GameObject NewChild(Transform parent, string name, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void SetRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p == null)
        {
            Debug.LogWarning($"[UnreadMarkersBuilder] Property '{propertyName}' not found on {so.targetObject.GetType().Name}.");
            return;
        }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[UnreadMarkersBuilder] {so.targetObject.GetType().Name}.{propertyName} set to null — assign manually.");
    }
}
#endif
```

- [ ] **Step 2: Compile**

Switch to Unity, let it recompile. Expected: **0 Console errors**.

- [ ] **Step 3: Run the builder**

In Unity: **Tools ▸ Chat ▸ Build Unread Markers**.
Expected Console: `[UnreadMarkersBuilder] Built FAB + UnreadSeparator.prefab and wired MessageListView...`
Then verify in the Hierarchy: `…/<chat panel>/ScrollToBottomFab` exists with `Circle` (+ `Chevron`) and `Badge` (+ `BadgeText`); the `Assets/Prefabs/UnreadSeparator.prefab` asset exists; and on the `MessageListView` component, **Unread Separator Prefab** and **Scroll To Bottom Fab** are both assigned.

- [ ] **Step 4: Assign the chevron sprite**

Select `ScrollToBottomFab/Circle/Chevron` and drop a down-chevron sprite into the `Image ▸ Source Image` slot (color stays `#54656F`). If the project has no chevron sprite, import one (a simple 24×24 chevron-down PNG/SVG) and assign it.

- [ ] **Step 5: Commit** (ask consent first; switch to Unity once for `.meta`s)

```bash
git add Assets/Editor/UnreadMarkersBuilder.cs Assets/Editor/UnreadMarkersBuilder.cs.meta \
        Assets/Prefabs/UnreadSeparator.prefab Assets/Prefabs/UnreadSeparator.prefab.meta \
        Assets/Scenes/Main.unity
git commit -m "feat(chat): add UnreadMarkers editor builder; wire FAB + separator into scene"
```

---

## Task 8: In-Editor verification (Game view, 1080×2400)

No automated coverage for the live scene — verify the integrated behavior by hand in Play mode. (Pure helpers are already covered by Tasks 2–4.)

- [ ] **Step 1: Enter Play mode and open a chat with unread messages**

Set Game view to **1080×2400**. Open a chat whose list shows a non-zero unread badge.
Expected: the view **lands at the "N UNREAD MESSAGES" separator** (separator near the top of the viewport), the FAB is **visible** bottom-right, and its badge shows the count of unread still below the fold.

- [ ] **Step 2: Verify the separator placement**

Expected: the separator sits **immediately above the first unread incoming message**; read history is above it. The label reads "N UNREAD MESSAGES" (or "1 UNREAD MESSAGE" for one).

- [ ] **Step 3: Tap the FAB**

Expected: smooth ~0.3s scroll to the newest message (OutCubic), a subtle press punch, then the FAB **fades out** and the badge clears. Grabbing/dragging the list mid-animation **cancels** the auto-scroll.

- [ ] **Step 4: Scroll up in a long chat (no unread)**

Open an already-read chat and scroll up.
Expected: **no separator**; the FAB appears once scrolled off the bottom and hides at the bottom; badge stays hidden (0). In a chat too short to scroll, the FAB never appears.

- [ ] **Step 5: Live arrival while scrolled up**

Scroll up, then receive a new incoming message (or simulate one).
Expected: the badge **increments**; the separator does **not** move. Scrolling down to the bottom (or tapping the FAB) clears the badge and hides the FAB. When at the bottom, a new arrival slides into view and the badge stays 0.

- [ ] **Step 6: Chat switch resets state**

Switch to another chat (and back).
Expected: no stale separator or FAB badge leaks across chats; reopening the now-read chat shows **no separator** (unread already zeroed → N == 0).

- [ ] **Step 7: Commit any inspector nudges** (ask consent first)

If you nudged the FAB position or assigned the chevron sprite after the Task 7 commit:

```bash
git add Assets/Scenes/Main.unity Assets/Prefabs/UnreadSeparator.prefab Assets/Prefabs/UnreadSeparator.prefab.meta
git commit -m "chore(chat): tune FAB placement + chevron sprite after in-Editor review"
```

---

## Self-review

**Spec coverage:**
- Scroll-to-bottom FAB (appears scrolled-up, count badge, tap → animate to bottom) → Tasks 5 (widget), 6 (visibility/badge/tap), 7 (build/wire). ✓
- Unread separator (snapshot count, place above newest N incoming, land there) → Tasks 1 (snapshot), 2 (placement math), 4 (view), 6 (insertion + landing), 7 (prefab). ✓
- Badge = unread below fold, increments on live arrivals, 0 → hide → Tasks 3 (math), 6 (`ComputeBelowFoldCount`, live-arrival tracking). ✓
- Separate components + `ChatManager.UnreadOnOpen` → Tasks 1, 4, 5. ✓
- WhatsApp-native visuals in reference units (green `#26B25A`, label `#1E7E45`, chevron sprite `#54656F`, FAB 120u/132u touch, badge 28u) → Task 7. ✓
- Lifecycle/reset, pagination-stable tracking (RectTransform refs), throttle, tween-kill-on-grab → Task 6. ✓
- Tests matching `AttachmentDisplayFormatTests` pattern (placement, below-fold, pluralization) → Tasks 2–4. ✓

**Placeholder scan:** No TBD/TODO; every code step is complete. The only deliberately manual bit is the chevron **sprite** asset (Task 7 Step 4) — by design (Image sprite, not a TMP glyph) and consistent with the AttachmentPreviewScreenBuilder "assign sprite refs" pattern.

**Type consistency:** `IndexForUnreadCount(IReadOnlyList<bool>, int)`, `CountBelowFold(IReadOnlyList<float>, float)`, `UnreadSeparatorView.SetCount(int)`/`FormatLabel(int)`, `ScrollToBottomFab.Show()/Hide()/SetCount(int)/OnClicked`, `ChatManager.UnreadOnOpen` — names/signatures are identical across the tasks that define and call them. Serialized-ref names used by the builder (`button`, `canvasGroup`, `badgeRoot`, `badgeText`, `label`, `scrollToBottomFab`, `unreadSeparatorPrefab`) match the field declarations in Tasks 4–6.

**Assumptions to confirm at execution:** (1) `MessageListView.scrollRect.transform.parent` is the chat panel that should host the FAB — verify the FAB isn't clipped by the viewport mask and sits above the input bar (nudge `BottomMargin` if needed). (2) The message list's `VerticalLayoutGroup` stretches `flexibleWidth=1` children to full width (DateSeparator relies on the same) — confirm the separator bar spans edge-to-edge.
