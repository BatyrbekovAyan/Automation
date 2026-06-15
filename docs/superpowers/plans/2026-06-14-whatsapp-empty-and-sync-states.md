# WhatsApp empty-state + post-creation sync screen — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the WhatsApp "create your first bot" empty state, stop it showing once a bot exists, and add a fixed 5-minute "syncing" screen after a WhatsApp bot connects before its chats appear.

**Architecture:** The WhatsApp tab becomes a 3-state content area resolved per active bot (No bots → No WhatsApp → Syncing → Ready). The decision logic is split into two pure, unit-testable static helpers (`WhatsAppSyncGate`, `WhatsAppTabStateResolver`). `ChatManager` reads a new per-bot `Bot{N}WhatsappSyncUntil` PlayerPrefs epoch and routes a connected-but-syncing bot to a new `SyncingView` instead of the chat list, revealing chats automatically when the window elapses.

**Tech Stack:** Unity 6 (C#), TMPro, DOTween, NUnit EditMode tests (`Assets/Tests/Editor/Chat/`, no asmdef → `Assembly-CSharp-Editor`).

**Spec:** `docs/superpowers/specs/2026-06-14-whatsapp-empty-and-sync-states-design.md`

---

## Project conventions for this plan

- **No worktrees.** Work in the main checkout.
- **Tests:** run EditMode tests with the test bridge (Editor open: drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`) or `Tools/run-tests-headless.sh '<filter>'` (Editor closed). A missing type = compile error = the run fails; that is the expected "red".
- **New `.cs` files need their Unity-generated `.meta`.** After creating a script, let Unity import (recompile via the bridge / MCP, or open the Editor) so the `.meta` exists, then stage **both** `.cs` and `.meta`.
- **Commits are per-task and require the user's go-ahead.** Commit steps below stage exact paths; run them only when the user approves.
- **UI/scene tasks** (7, 8) are not unit-testable — they use `.claude/skills/unity-ui-builder/SKILL.md` as the REQUIRED sub-skill and are verified visually in the Editor at 1080×2400. Sizes are 1080×1920 canvas reference units. TMP icon glyphs do **not** render in this project — icons must be `Image` sprites. Never put `UISprite.psd` on surfaces — use a null sprite + RoundedCorners.

## File structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Scripts/Main/WhatsAppSyncGate.cs` | Pure window math: is-syncing, remaining, progress fraction, countdown label | **Create** |
| `Assets/Scripts/Main/WhatsAppTabState.cs` | `WhatsAppTabState` enum + `WhatsAppTabStateResolver.Resolve(...)` precedence | **Create** |
| `Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs` | EditMode tests for both pure helpers | **Create** |
| `Assets/Scripts/Main/ChatManager.cs` | Window constant, two new events | Modify |
| `Assets/Scripts/Main/ChatManager.BotState.cs` | `IsWhatsAppSyncing`, resolver wiring, syncing branch + wait routine, cancel-on-switch | Modify |
| `Assets/Scripts/Main/Manager.cs` | Write `Bot{N}WhatsappSyncUntil` after bot creation | Modify |
| `Assets/Scripts/Main/Bot.cs` | Clear `WhatsappSyncUntil` on delete | Modify |
| `Assets/Scripts/UI/EmptyStateView.cs` | Updated NoBots copy | Modify |
| `Assets/Scripts/UI/SyncingView.cs` | Syncing-screen controller + countdown coroutine + spinner | **Create** |
| `Assets/Scenes/Main.unity` | Restyle `EmptyState` (Direction A); add `SyncingState` + wire `SyncingView` | Modify |

---

## Task 1: Pure helpers (sync gate + state resolver) with EditMode tests

**Files:**
- Create: `Assets/Scripts/Main/WhatsAppSyncGate.cs`
- Create: `Assets/Scripts/Main/WhatsAppTabState.cs`
- Test: `Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs`:

```csharp
using NUnit.Framework;

public class WhatsAppSyncGateTests
{
    [Test] public void IsSyncing_FutureEpoch_True()  => Assert.IsTrue(WhatsAppSyncGate.IsSyncing(2000L, 1000L));
    [Test] public void IsSyncing_PastEpoch_False()   => Assert.IsFalse(WhatsAppSyncGate.IsSyncing(1000L, 2000L));
    [Test] public void IsSyncing_EqualEpoch_False()  => Assert.IsFalse(WhatsAppSyncGate.IsSyncing(1000L, 1000L));

    [Test] public void RemainingMs_Clamped()
    {
        Assert.AreEqual(3000L, WhatsAppSyncGate.RemainingMs(5000L, 2000L));
        Assert.AreEqual(0L,    WhatsAppSyncGate.RemainingMs(1000L, 5000L));
    }

    [Test] public void ProgressFraction_StartHalfEnd()
    {
        Assert.AreEqual(0f,   WhatsAppSyncGate.ProgressFraction(1_000_000L + 300_000L, 1_000_000L, 300), 0.001f);
        Assert.AreEqual(0.5f, WhatsAppSyncGate.ProgressFraction(1_000_000L + 150_000L, 1_000_000L, 300), 0.001f);
        Assert.AreEqual(1f,   WhatsAppSyncGate.ProgressFraction(1_000_000L,            2_000_000L, 300), 0.001f);
    }

    [Test] public void ProgressFraction_ZeroWindow_Full()
        => Assert.AreEqual(1f, WhatsAppSyncGate.ProgressFraction(0L, 0L, 0), 0.001f);

    [Test] public void FormatCountdown_Buckets()
    {
        Assert.AreEqual("Finishing up…",          WhatsAppSyncGate.FormatCountdown(0L));
        Assert.AreEqual("Less than a minute left", WhatsAppSyncGate.FormatCountdown(30_000L));
        Assert.AreEqual("Less than a minute left", WhatsAppSyncGate.FormatCountdown(60_000L));
        Assert.AreEqual("About 2 min left",        WhatsAppSyncGate.FormatCountdown(90_000L));
        Assert.AreEqual("About 5 min left",        WhatsAppSyncGate.FormatCountdown(300_000L));
    }
}

public class WhatsAppTabStateResolverTests
{
    [Test] public void NoBots_WinsOverEverything()
        => Assert.AreEqual(WhatsAppTabState.NoBots, WhatsAppTabStateResolver.Resolve(0, true, true));

    [Test] public void NoWhatsApp_WhenBotLacksProfile()
        => Assert.AreEqual(WhatsAppTabState.NoWhatsApp, WhatsAppTabStateResolver.Resolve(1, false, false));

    [Test] public void Syncing_WhenConnectedAndInWindow()
        => Assert.AreEqual(WhatsAppTabState.Syncing, WhatsAppTabStateResolver.Resolve(1, true, true));

    [Test] public void Ready_WhenConnectedAndWindowClosed()
        => Assert.AreEqual(WhatsAppTabState.Ready, WhatsAppTabStateResolver.Resolve(1, true, false));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `Tools/run-tests-headless.sh 'WhatsApp'` (or drop the bridge trigger if the Editor is open).
Expected: FAIL — compile errors, `WhatsAppSyncGate` / `WhatsAppTabState` / `WhatsAppTabStateResolver` do not exist.

- [ ] **Step 3: Implement `WhatsAppSyncGate`**

Create `Assets/Scripts/Main/WhatsAppSyncGate.cs`:

```csharp
using System;
using UnityEngine;

/// <summary>
/// Pure, side-effect-free math for the fixed post-creation WhatsApp sync window.
/// No PlayerPrefs or singletons so the logic stays unit-testable in EditMode.
/// </summary>
public static class WhatsAppSyncGate
{
    /// <summary>True while the fixed sync window is still open.</summary>
    public static bool IsSyncing(long syncUntilUnixMs, long nowUnixMs) => nowUnixMs < syncUntilUnixMs;

    /// <summary>Milliseconds left in the window, never negative.</summary>
    public static long RemainingMs(long syncUntilUnixMs, long nowUnixMs) =>
        Math.Max(0L, syncUntilUnixMs - nowUnixMs);

    /// <summary>0..1 fraction of the window elapsed, for the progress bar.</summary>
    public static float ProgressFraction(long syncUntilUnixMs, long nowUnixMs, int windowSeconds)
    {
        if (windowSeconds <= 0) return 1f;
        long windowMs = (long)windowSeconds * 1000L;
        float elapsed = windowMs - RemainingMs(syncUntilUnixMs, nowUnixMs);
        return Mathf.Clamp01(elapsed / windowMs);
    }

    /// <summary>Human-friendly countdown label for the syncing screen.</summary>
    public static string FormatCountdown(long remainingMs)
    {
        if (remainingMs <= 0L) return "Finishing up…";
        int totalSeconds = (int)((remainingMs + 999L) / 1000L); // round up to whole seconds
        if (totalSeconds <= 60) return "Less than a minute left";
        int minutes = (totalSeconds + 59) / 60;                 // round up to whole minutes
        return $"About {minutes} min left";
    }
}
```

- [ ] **Step 4: Implement `WhatsAppTabState` + resolver**

Create `Assets/Scripts/Main/WhatsAppTabState.cs`:

```csharp
/// <summary>The four mutually-exclusive states of the WhatsApp tab content area.</summary>
public enum WhatsAppTabState
{
    NoBots,     // No bots exist at all
    NoWhatsApp, // Active bot exists but has no WhatsApp profile
    Syncing,    // Active bot connected, still inside the fixed sync window
    Ready,      // Show the chat list
}

/// <summary>Pure precedence resolver for the WhatsApp tab. Order matters.</summary>
public static class WhatsAppTabStateResolver
{
    public static WhatsAppTabState Resolve(int botCount, bool activeBotHasWhatsApp, bool isSyncing)
    {
        if (botCount <= 0) return WhatsAppTabState.NoBots;
        if (!activeBotHasWhatsApp) return WhatsAppTabState.NoWhatsApp;
        if (isSyncing) return WhatsAppTabState.Syncing;
        return WhatsAppTabState.Ready;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `Tools/run-tests-headless.sh 'WhatsApp'`
Expected: PASS — 11 tests green (`WhatsAppSyncGateTests` + `WhatsAppTabStateResolverTests`).

- [ ] **Step 6: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/Main/WhatsAppSyncGate.cs Assets/Scripts/Main/WhatsAppSyncGate.cs.meta \
        Assets/Scripts/Main/WhatsAppTabState.cs Assets/Scripts/Main/WhatsAppTabState.cs.meta \
        Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs Assets/Tests/Editor/Chat/WhatsAppSyncTests.cs.meta
git commit -m "feat(chat): pure WhatsApp sync-window + tab-state helpers with tests"
```

---

## Task 2: ChatManager — window constant + sync events + IsWhatsAppSyncing

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs:91-92` (events block)
- Modify: `Assets/Scripts/Main/ChatManager.BotState.cs` (add reader near line 143)

- [ ] **Step 1: Add the constant and events to `ChatManager.cs`**

After line 92 (`public event Action<EmptyStateReason> OnEmptyState;`) add:

```csharp

    /// <summary>Fixed post-creation WhatsApp sync window. Single source of truth.</summary>
    public const int WhatsAppSyncWindowSeconds = 300;

    /// <summary>Fires with the sync-window end (Unix ms) when the active bot is syncing.</summary>
    public event Action<long> OnWhatsAppSyncing;

    /// <summary>Fires when the active bot's sync window has elapsed and chats are about to load.</summary>
    public event Action OnWhatsAppSyncReady;
```

- [ ] **Step 2: Add the per-bot reader to `ChatManager.BotState.cs`**

After `GetActiveProfileId()` (ends line 143) add:

```csharp

    /// <summary>PlayerPrefs key suffix holding a bot's sync-window end (Unix ms).</summary>
    private const string SyncUntilKeySuffix = "WhatsappSyncUntil";

    private static long NowUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// True when the given bot is still inside its fixed post-creation sync window.
    /// Missing/unparseable key (e.g. bots created before this feature) ⇒ not syncing.
    /// </summary>
    public bool IsWhatsAppSyncing(string botId, out long syncUntilUnixMs)
    {
        syncUntilUnixMs = 0L;
        if (string.IsNullOrEmpty(botId)) return false;
        string raw = PlayerPrefs.GetString(botId + SyncUntilKeySuffix, "0");
        if (!long.TryParse(raw, out syncUntilUnixMs)) { syncUntilUnixMs = 0L; return false; }
        return WhatsAppSyncGate.IsSyncing(syncUntilUnixMs, NowUnixMs());
    }
```

- [ ] **Step 3: Compile**

Recompile via the bridge / MCP (or open the Editor). Expected: no errors; existing `WhatsAppSyncTests` still pass.

- [ ] **Step 4: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/Main/ChatManager.cs Assets/Scripts/Main/ChatManager.BotState.cs
git commit -m "feat(chat): WhatsApp sync window constant, events, and per-bot reader"
```

---

## Task 3: ChatManager.BotState — resolver wiring, syncing branch, wait routine

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.BotState.cs` (`ComputeCurrentEmptyState` 152-167, `SetActiveBot` 104-125, `BeginLoadForActiveBot` 173-191)

- [ ] **Step 1: Route `ComputeCurrentEmptyState` through the resolver**

Replace the body of `ComputeCurrentEmptyState()` (lines 152-167) with:

```csharp
    public EmptyStateReason? ComputeCurrentEmptyState()
    {
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        int botCount = root != null ? root.childCount : 0;

        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        bool hasWhatsApp = bot != null && IsValidProfileId(bot.whatsappProfileId);
        bool syncing = hasWhatsApp && IsWhatsAppSyncing(CurrentBotId, out _);

        return WhatsAppTabStateResolver.Resolve(botCount, hasWhatsApp, syncing) switch
        {
            WhatsAppTabState.NoBots => EmptyStateReason.NoBotsExist,
            WhatsAppTabState.NoWhatsApp => EmptyStateReason.BotHasNoWhatsApp,
            _ => (EmptyStateReason?)null, // Syncing / Ready are not empty-card states
        };
    }
```

This is what fixes the stale empty state: a connected bot now resolves to Syncing/Ready (→ `null`), so `EmptyStateView` hides itself on tab activation instead of showing "create your first bot".

- [ ] **Step 2: Extract chat-loading and add the syncing branch in `BeginLoadForActiveBot`**

Replace `BeginLoadForActiveBot()` (lines 173-191) with:

```csharp
    private Coroutine _syncWaitRoutine;

    private void BeginLoadForActiveBot()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null || !IsValidProfileId(bot.whatsappProfileId))
        {
            OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
            return;
        }

        if (IsWhatsAppSyncing(CurrentBotId, out long syncUntilUnixMs))
        {
            OnWhatsAppSyncing?.Invoke(syncUntilUnixMs);
            if (_syncWaitRoutine != null) StopCoroutine(_syncWaitRoutine);
            _syncWaitRoutine = StartCoroutine(WaitForWhatsAppSyncRoutine(syncUntilUnixMs));
            return;
        }

        LoadChatsForActiveBot();
    }

    private void LoadChatsForActiveBot()
    {
        string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
        string cachedJson = "";
        if (File.Exists(cachePath))
        {
            cachedJson = File.ReadAllText(cachePath);
            ParseChatsJson(cachedJson, true);
        }

        StartCoroutine(SyncAllChats(cachePath, cachedJson));
    }

    private IEnumerator WaitForWhatsAppSyncRoutine(long syncUntilUnixMs)
    {
        while (WhatsAppSyncGate.IsSyncing(syncUntilUnixMs, NowUnixMs()))
            yield return new WaitForSecondsRealtime(1f);

        OnWhatsAppSyncReady?.Invoke();
        _syncWaitRoutine = null;
        LoadChatsForActiveBot();
    }
```

- [ ] **Step 3: Cancel the wait routine when switching bots**

In `SetActiveBot()` (line 104), immediately after the `if (botId == CurrentBotId) return;` guard (line 107), add:

```csharp
        if (_syncWaitRoutine != null) { StopCoroutine(_syncWaitRoutine); _syncWaitRoutine = null; }
```

Note: `SetActiveBot` already calls `StopAllCoroutines()` (line 121) which also stops the wait routine, but clearing the handle keeps state consistent and avoids a stale non-null `_syncWaitRoutine`.

- [ ] **Step 4: Compile**

Recompile via the bridge / MCP. Expected: no errors; `WhatsAppSyncTests` still pass (logic helpers unchanged).

- [ ] **Step 5: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/Main/ChatManager.BotState.cs
git commit -m "feat(chat): gate active-bot load behind the WhatsApp sync window"
```

---

## Task 4: Manager — stamp the sync window when a WhatsApp bot is created

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` (`CreateBotFromForm()`, after the persistence block ~line 1218)

- [ ] **Step 1: Write the sync-until epoch after persistence**

In `CreateBotFromForm()`, find the persistence block that ends with `PlayerPrefs.SetInt("ids", ++id); PlayerPrefs.Save();` (just before `ResetAddBotForm();`). Immediately after `PlayerPrefs.Save();` and before `ResetAddBotForm();`, add:

```csharp
            if (useWhatsapp)
            {
                long syncUntil = DateTimeOffset.UtcNow
                    .AddSeconds(ChatManager.WhatsAppSyncWindowSeconds)
                    .ToUnixTimeMilliseconds();
                PlayerPrefs.SetString(newBot.name + "WhatsappSyncUntil", syncUntil.ToString());
                PlayerPrefs.Save();
            }
```

Anchoring here means WhatsApp auth has already completed in the wizard, so the 5-minute window lines up with when Wappi's sync actually starts. If `Manager.cs` lacks `using System;`, fully-qualify as `System.DateTimeOffset`.

- [ ] **Step 2: Compile**

Recompile via the bridge / MCP. Expected: no errors.

- [ ] **Step 3: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "feat(chat): start the 5-min sync window when a WhatsApp bot is created"
```

---

## Task 5: Bot — clear the sync key on delete

**Files:**
- Modify: `Assets/Scripts/Main/Bot.cs` (`DeleteBot()`, in the PlayerPrefs deletion block)

- [ ] **Step 1: Delete the key alongside the other per-bot keys**

In `DeleteBot()`, inside the block that deletes the bot's PlayerPrefs keys (next to `PlayerPrefs.DeleteKey(transform.name + "Name");`), add:

```csharp
        PlayerPrefs.DeleteKey(transform.name + "WhatsappSyncUntil");
```

- [ ] **Step 2: Compile**

Recompile via the bridge / MCP. Expected: no errors.

- [ ] **Step 3: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/Main/Bot.cs
git commit -m "fix(chat): clear WhatsappSyncUntil when a bot is deleted"
```

---

## Task 6: SyncingView controller

**Files:**
- Create: `Assets/Scripts/UI/SyncingView.cs`

- [ ] **Step 1: Create the controller**

Create `Assets/Scripts/UI/SyncingView.cs`:

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Drives the WhatsApp "Setting things up" syncing screen. Shows while the active
/// bot is inside its fixed sync window, ticking a time-based progress bar and
/// countdown, then hides when ChatManager signals the window has elapsed.
/// Sibling of EmptyState under ChatsPanel; CanvasGroup-toggled like EmptyStateView.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SyncingView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private RectTransform spinner;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Image progressFill;          // Image.type = Filled, Horizontal, fillAmount 0
    [SerializeField] private TextMeshProUGUI countdownLabel;
    [SerializeField] private TextMeshProUGUI footnoteLabel;

    private CanvasGroup canvasGroup;
    private Coroutine tickRoutine;
    private Tween spinnerTween;
    private long syncUntilUnixMs;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        ApplyCopy();
        Hide();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance == null) return;
        ChatManager.Instance.OnWhatsAppSyncing += HandleSyncing;
        ChatManager.Instance.OnWhatsAppSyncReady += HandleReady;

        // Catch up: tab re-opened or app relaunched mid-window — resume without an event.
        if (ChatManager.Instance.IsWhatsAppSyncing(ChatManager.Instance.CurrentBotId, out long untilMs))
            HandleSyncing(untilMs);
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnWhatsAppSyncing -= HandleSyncing;
            ChatManager.Instance.OnWhatsAppSyncReady -= HandleReady;
        }
        StopTicking();
    }

    private void HandleSyncing(long untilMs)
    {
        syncUntilUnixMs = untilMs;
        Show();
        StopTicking();
        StartSpinner();
        tickRoutine = StartCoroutine(TickRoutine());
    }

    private void HandleReady() => Hide();

    private IEnumerator TickRoutine()
    {
        while (true)
        {
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long remaining = WhatsAppSyncGate.RemainingMs(syncUntilUnixMs, now);
            if (progressFill != null)
                progressFill.fillAmount =
                    WhatsAppSyncGate.ProgressFraction(syncUntilUnixMs, now, ChatManager.WhatsAppSyncWindowSeconds);
            if (countdownLabel != null)
                countdownLabel.text = WhatsAppSyncGate.FormatCountdown(remaining);
            if (remaining <= 0L) yield break;
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    private void StartSpinner()
    {
        if (spinner == null) return;
        spinnerTween?.Kill();
        spinner.localEulerAngles = Vector3.zero;
        spinnerTween = spinner
            .DOLocalRotate(new Vector3(0f, 0f, -360f), 1f, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true);
    }

    private void StopTicking()
    {
        if (tickRoutine != null) { StopCoroutine(tickRoutine); tickRoutine = null; }
        spinnerTween?.Kill();
        spinnerTween = null;
    }

    private void ApplyCopy()
    {
        if (titleLabel != null) titleLabel.text = "Setting things up";
        if (bodyLabel != null) bodyLabel.text = "We're importing your chats and messages from WhatsApp.";
        if (footnoteLabel != null) footnoteLabel.text = "You can keep using the app. Chats appear here when ready.";
    }

    private void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        StopTicking();
    }
}
```

- [ ] **Step 2: Compile**

Recompile via the bridge / MCP. Expected: no errors. (Scene wiring happens in Task 8.)

- [ ] **Step 3: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/UI/SyncingView.cs Assets/Scripts/UI/SyncingView.cs.meta
git commit -m "feat(chat): SyncingView controller for the post-creation sync screen"
```

---

## Task 7: Restyle the empty state (Direction A · Welcoming hero)

**REQUIRED SUB-SKILL:** `.claude/skills/unity-ui-builder/SKILL.md`

**Files:**
- Modify: `Assets/Scripts/UI/EmptyStateView.cs:100-110` (NoBots copy)
- Modify: `Assets/Scenes/Main.unity` — `Canvas/ScreenContainer/Screen_Whatsapp/ChatsPanel/EmptyState`

- [ ] **Step 1: Update the NoBots copy in code**

In `EmptyStateView.ConfigureForReason`, replace the three `NoBotsExist` strings (lines 102-104) with:

```csharp
                if (titleLabel != null) titleLabel.text = "Create your first bot";
                if (bodyLabel != null) bodyLabel.text = "An AI assistant that answers your customers on WhatsApp, day and night.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Create a bot";
```

Leave the `BotHasNoWhatsApp` case and the `OpenCreateBotFlow` wiring unchanged.

- [ ] **Step 2: Restyle `EmptyState` in place (keep existing serialized refs)**

Modify the existing children in the scene — do **not** destroy/recreate the root or the wired children (`EmptyStateView` refs `iconImage`, `titleLabel`, `bodyLabel`, `primaryButton`, `primaryButtonLabel` must survive). Per unity-ui-builder (1080×1920 ref units, dp×3):

- **Icon circle** (new, behind the existing `Icon`): an `Image`, null sprite + RoundedCorners fully rounded (or a circle sprite), diameter ~252, fill `#DFF3EA`. Center it in the upper-middle of the content area.
- **Icon** (existing `iconImage`): a bot/chat sprite (existing project asset — TMP glyphs don't render), ~120 square, tint `#008069`, centered in the circle.
- **Title** (existing `titleLabel`): Title style per the skill's type scale, weight 500, color near-black, centered, ~24 below the circle.
- **Body** (existing `bodyLabel`): Body style, muted (~70% opacity black), centered, max ~720 wide, line spacing comfortable.
- **PrimaryButton** (existing `primaryButton`): full-width pill (with horizontal margins) pinned in the thumb zone (bottom ~120–160 from the panel bottom), height ~144, fill `#008069`, RoundedCorners pill radius, label (existing `primaryButtonLabel`) white, weight 500, with a leading "+" — use an `Image` plus-glyph sprite if a TMP icon won't render. DOTween press feedback `.DOPunchScale(Vector3.one * 0.05f, 0.2f)` on click is a nice touch.

- [ ] **Step 3: Verify visually**

Open the Editor, enter Play with zero bots (or clear bot PlayerPrefs), open the WhatsApp tab. At 1080×2400: the hero icon, headline "Create your first bot", supportive body, and the green pill CTA in the thumb zone should match Direction A. Tapping the CTA still opens the create-bot flow.

- [ ] **Step 4: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/UI/EmptyStateView.cs Assets/Scenes/Main.unity
git commit -m "feat(ui): redesign WhatsApp empty state (welcoming hero)"
```

---

## Task 8: Build the SyncingState screen and wire SyncingView

**REQUIRED SUB-SKILL:** `.claude/skills/unity-ui-builder/SKILL.md`

**Files:**
- Modify: `Assets/Scenes/Main.unity` — add `Canvas/ScreenContainer/Screen_Whatsapp/ChatsPanel/SyncingState`

- [ ] **Step 1: Create the `SyncingState` GameObject**

Under `ChatsPanel`, as a sibling of `EmptyState`, add `SyncingState`:
- RectTransform stretched to cover the chat-list content region (same footprint as `EmptyState` — below the WhatsApp top bar so the bar and bottom tabs stay visible/usable).
- Add `CanvasGroup` and the `SyncingView` component.

- [ ] **Step 2: Build the children (per unity-ui-builder, 1080×1920 ref units)**

- **Spinner**: an `Image` of a ring/arc (colored top segment so rotation reads as spinning; `#008069` on a `#DFF3EA` track), ~186 square, centered upper-middle. Assign its `RectTransform` to `SyncingView.spinner`.
- **Title**: TMP "Setting things up" (set at runtime by `ApplyCopy`, but author placeholder text), Title style, weight 500, centered, ~24 under the spinner. Assign to `SyncingView.titleLabel`.
- **Body**: TMP, Body style, muted, centered, max ~720 wide. Assign to `SyncingView.bodyLabel`.
- **ProgressTrack**: an `Image`, fill `#ECECEC`, height ~16, RoundedCorners, width ~720 centered, ~24 under the body. Child **ProgressFill**: an `Image`, fill `#008069`, RoundedCorners, `Image.type = Filled`, `Fill Method = Horizontal`, `Fill Origin = Left`, `fillAmount = 0`, same rect as the track. Assign ProgressFill to `SyncingView.progressFill`.
- **CountdownLabel**: TMP, color `#008069`, weight 500, centered, ~12 under the track. Assign to `SyncingView.countdownLabel`.
- **Footnote**: TMP, Caption style, muted (~50–60% black), centered, pinned ~120 from the bottom of the region. Assign to `SyncingView.footnoteLabel`.

Set the `CanvasGroup` alpha to 0 in the scene (it starts hidden; `Awake` also hides it).

- [ ] **Step 3: Verify visually + behaviorally**

This depends on Task 9's accelerated window for a quick check. With a short test window, create a WhatsApp bot and open the WhatsApp tab: the spinner rotates, the progress bar fills over the window, the countdown decrements, and the screen hides → chat list loads when the window ends. Confirm the WhatsApp top bar and bottom tabs remain usable throughout.

- [ ] **Step 4: Commit (on user go-ahead)**

```bash
git add Assets/Scenes/Main.unity
git commit -m "feat(ui): add WhatsApp syncing screen (progress + reassurance)"
```

---

## Task 9: End-to-end verification

**Files:** none (verification only)

- [ ] **Step 1: Temporarily shorten the window for testing**

In `ChatManager.cs`, change `WhatsAppSyncWindowSeconds = 300` to `30` and recompile. (Restored in Step 7.)

- [ ] **Step 2: Fresh-install path**

Clear bot PlayerPrefs (or use a clean profile). Open the WhatsApp tab → redesigned "Create your first bot" empty state. Confirm it is the only thing shown.

- [ ] **Step 3: Create → sync → chats**

Create a WhatsApp bot and complete auth. Switch to the WhatsApp tab → the syncing screen shows (not the empty state). After ~30s the screen hides and the chat list loads. ✅ verifies Tasks 3, 4, 6, 8 and the auto-switch fix.

- [ ] **Step 4: Switch-tabs and relaunch mid-window**

Create another WhatsApp bot; while syncing, switch to another bottom tab and back — syncing resumes with correct remaining time. Force-quit and relaunch mid-window — the syncing screen resumes from the persisted epoch. ✅ verifies catch-up + persistence.

- [ ] **Step 5: Existing-bot and Telegram-only paths**

A bot created before this feature (no `WhatsappSyncUntil` key) opens straight to chats — no syncing screen. A Telegram-only bot shows the unchanged "WhatsApp not connected" card. ✅ verifies the resolver precedence and missing-key default.

- [ ] **Step 6: Delete cleanup**

Delete a still-syncing bot and recreate it; confirm the new bot starts a fresh window (no stale key leakage). ✅ verifies Task 5.

- [ ] **Step 7: Restore the window and run tests**

Restore `WhatsAppSyncWindowSeconds = 300`, recompile, and run `Tools/run-tests-headless.sh 'WhatsApp'` → all pure-helper tests pass.

- [ ] **Step 8: Commit (on user go-ahead)**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "chore(chat): restore 5-min WhatsApp sync window after verification"
```

---

## Self-review notes

- **Spec coverage:** empty-state redesign (Task 7) · auto-switch fix (Task 3, `ComputeCurrentEmptyState` resolver → `null` for connected bots) · syncing screen (Tasks 6, 8) · fixed 5-min gate + per-bot key + anchor + persistence + existing-bot default + delete cleanup (Tasks 2, 3, 4, 5) · scope = WhatsApp chat area only, other tabs usable (Task 8 footprint). All covered.
- **Type consistency:** `WhatsAppSyncGate` / `WhatsAppTabState` / `WhatsAppTabStateResolver`, `WhatsAppSyncWindowSeconds`, `OnWhatsAppSyncing(long)` / `OnWhatsAppSyncReady`, `IsWhatsAppSyncing(string, out long)`, `_syncWaitRoutine`, `LoadChatsForActiveBot`, key suffix `"WhatsappSyncUntil"` — used identically across tasks.
- **No placeholders:** all code steps contain full code; UI steps reference unity-ui-builder with explicit hierarchy, metrics, colors, and serialized-ref assignments.
