# Phase 1: Polished Suggestions Panel on Mock Data - Pattern Map

**Mapped:** 2026-06-23
**Files analyzed:** 15 new + 2 modified (ChatManager accessors)
**Analogs found:** 17 / 17 (every new file has a verified in-repo analog; only the interface + RU mock *content* is true greenfield)

> This is a brownfield, greenfield-SEAM phase. There is no existing suggestions code. Every new file is a *measured reshape* of a proven analog so it inherits the project's concurrency discipline, lifecycle conventions, and builder pattern. Planner: cite the analog file + line range in each plan action.

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Assets/Scripts/Chat/ISuggestionsProvider.cs` | interface seam | request-response (callback) | `.claude/rules/networking.md` `FetchData(param, Action<T>)` convention | role-match (no existing C# interface seam; models the callback convention) |
| `Assets/Scripts/Chat/SuggestionRequest.cs` | data model (value object) | transform | `RawMessage` / `OutboxStore.OutboxEntry` (`[Serializable]` public-field DTO) | exact |
| `Assets/Scripts/Chat/SuggestionResult.cs` | data model (value object) | transform | `ChatsResponse` / `OutboxStore.OutboxEntry` | exact |
| `Assets/Scripts/Chat/SuggestionItem.cs` | data model (value object) | transform | `MessageReaction` / small DTO | exact |
| `Assets/Scripts/Chat/MockSuggestionsProvider.cs` | provider (plain C# + runner) | request-response + simulated latency | `ChatManager.QuoteResolve.cs` (serial guarded coroutine fetch) | role-match (mock, no UWR) |
| `Assets/Scripts/Chat/SuggestionsController.cs` | controller (MonoBehaviour mediator) | event-driven + request-response | `MessageListView.cs` (subscription lifecycle) + `ChatManager.QuoteResolve.cs` (capture+discard guard) | exact (two-analog composite) |
| `Assets/Scripts/Chat/SuggestionSequenceGuard.cs` | utility (pure C# guard) | transform/predicate | `CrossChatResponseGuard.cs` (static pure discard predicate) | exact |
| `Assets/Scripts/Chat/SemiAutoStore.cs` | persistence (plain C# store) | CRUD (PlayerPrefs) | `OutboxStore.cs` (injectable-root store) + bot-persistence PlayerPrefs key scheme | exact |
| `Assets/Scripts/UI/SuggestionsPanel.cs` | MonoBehaviour view | event-driven render (spawn/clear) | `QuickReplyPanel.cs` (SetReplies/Clear/Hide) + `MessagesBottomPanel.cs` (DOTween slide motion) | exact (two-analog composite) |
| `Assets/Scripts/UI/SuggestionCard.cs` | MonoBehaviour view (item) | request-response (tap callback) | `QuickReplyButton.cs` (single-button item, DROP arrow) | exact |
| `Assets/Scripts/UI/SemiAutoToggle.cs` | MonoBehaviour view (control) | event-driven + CRUD read/write | `MessageHeaderView.cs` (Awake `OnChatSelected` host) + `SemiAutoStore` | role-match |
| `Assets/Editor/SuggestionsPanelBuilder.cs` | Editor builder | build-time construction | `ChatsSearchBarBuilder.cs` (GO tree + SerializedObject wiring) + `BotSwitcherSheetBuilder.cs` (RoundedCorners) | exact |
| `Assets/Tests/Editor/Chat/SuggestionSequenceGuardTests.cs` | test | transform/predicate | `CrossChatResponseGuardTests.cs` | exact |
| `Assets/Tests/Editor/Chat/SemiAutoStoreTests.cs` | test | CRUD | `OutboxStoreTests.cs` (injectable root + SetUp/TearDown) | exact |
| `Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs` | test | transform | `OutboxStoreTests.cs` shape; logic-only assertions | exact |
| `Assets/Scripts/Main/ChatManager.Suggestions.cs` (MODIFIED — new partial) | accessor addition | state-exposure | `ChatManager.BotState.cs` (partial-class accessor pattern) | exact |

> **Placement (per STRUCTURE.md):** seam + value objects + mock + controller + guard + store → `Assets/Scripts/Chat/`; panel + card + toggle views → `Assets/Scripts/UI/`; builder → `Assets/Editor/`; tests → `Assets/Tests/Editor/Chat/` (no asmdef, compiles into `Assembly-CSharp-Editor`).

---

## Pattern Assignments

### `Assets/Scripts/Chat/ISuggestionsProvider.cs` (interface seam, request-response)

**Analog:** `.claude/rules/networking.md` callback convention (the project's universal async-result shape) + value-object pattern from `OutboxStore.OutboxEntry`.

**Core pattern — pure C# interface, callback return, NO Unity/UWR/n8n types:**
```csharp
public interface ISuggestionsProvider
{
    // steerTowardText null = fresh refresh; set = re-cluster (INT-04/D-01).
    // requestSeq rides through to the result so the controller can reject stale (DATA-03).
    void Request(SuggestionRequest request, System.Action<SuggestionResult> callback);
}
```

**Convention to follow:** `System.Action<T>` callback is the project's universal async convention (`FetchData(param, callback)` per networking rule + CONVENTIONS.md "Callback Pattern"). A coroutine return would force the consumer to be a MonoBehaviour and leak the runner — keep the interface pure C#. Value objects `[System.Serializable]` with public fields so a Phase-2 `N8nSuggestionsProvider` can JsonConvert the same shape (RESEARCH Pattern 1, A1).

**Anti-pattern (Pitfall 1):** NO `using UnityEngine.Networking;`, no `wappi`, no `n8n`/`X-N8N` anywhere in this file or any consumer above the seam.

---

### `Assets/Scripts/Chat/SuggestionRequest.cs` / `SuggestionResult.cs` / `SuggestionItem.cs` (data models, transform)

**Analog:** `RawMessage` / `OutboxStore.OutboxEntry` — `[System.Serializable]`, public fields for JSON parsing (CONVENTIONS.md "Serialization"; STRUCTURE.md "New API Endpoint" → models go in `Assets/Scripts/Chat/`).

**Shape to mirror (each its own `.cs` file — CONVENTIONS.md "each class lives in its own .cs file"):**
```csharp
[System.Serializable]
public class SuggestionRequest
{
    public string chatId;            // captured active chat (scoping)
    public string lastIncomingText;  // trigger msg (INT-02) or null (manual/pick)
    public string steerTowardText;   // picked reply for re-cluster (INT-04/D-01); null = fresh
    public long   requestSeq;        // monotonic; echoed back for the guard (DATA-03)
}

public class SuggestionResult
{
    public System.Collections.Generic.List<SuggestionItem> items; // ranked best-first (PANEL-03)
    public long requestSeq;          // echoed correlation id
    public SuggestionStatus status;  // Ok | Empty | Error  (drives PANEL-04)
}

public class SuggestionItem { public string text; public string intentLabel; }  // matches Phase-2 { text, label }
public enum SuggestionStatus { Ok, Empty, Error }
```

**Convention:** keep DTOs JsonConvert-friendly (public fields), no behavior. RU intent labels live in the MOCK, not these models (UI-SPEC copy rules).

---

### `Assets/Scripts/Chat/MockSuggestionsProvider.cs` (provider, request-response + simulated latency)

**Analog:** `ChatManager.QuoteResolve.cs` L82-153 (serial coroutine fetch, capture-and-discard) — model the *coroutine latency + serial discipline*, NOT the UnityWebRequest.

**Pattern to replicate (plain C# + injected runner — A4):**
- Plain C# class implementing `ISuggestionsProvider`. Ranking / steer / error / out-of-order *decision logic* is pure (EditMode-testable).
- Constructor takes a `MonoBehaviour runner` so latency runs via `runner.StartCoroutine(...)` + `WaitForSeconds` (CLAUDE.md: coroutines only, NEVER async/await in MonoBehaviours).
- D-15 adversarial path: deliberately invoke the callback OUT OF ORDER / superseded (lower `requestSeq` arriving late) and a simulated `SuggestionStatus.Error` path — these exercise the controller guard + the PANEL-04 error state.
- D-14 content: ranked Russian replies (greeting/price/availability/booking/decline) with `intentLabel` ∈ «Приветствие» «Цена» «Наличие» «Запись»/«Заказ» «Вежливый отказ»; include ≥1 deliberately long reply for PANEL-06 truncation.

**Latency coroutine shape (model on QuoteResolve drain, minus the network):**
```csharp
private IEnumerator RespondAfterLatency(SuggestionRequest req, System.Action<SuggestionResult> cb)
{
    yield return new WaitForSeconds(_latencySeconds);   // skeleton genuinely exercised (D-15)
    cb?.Invoke(BuildResult(req));                        // BuildResult = pure, testable
}
```

**Anti-pattern:** do NOT make the mock a MonoBehaviour with hard-coded ranking logic (kills EditMode testability); do NOT touch `_chatFetchesInFlight` (Pitfall 2 — mock has no Wappi call).

---

### `Assets/Scripts/Chat/SuggestionsController.cs` (controller, event-driven + request-response)

**Analog A (subscription lifecycle):** `MessageListView.cs` L75-154.
**Analog B (capture + discard guard):** `ChatManager.QuoteResolve.cs` L82-153.

**Subscription lifecycle — the load-bearing convention (Pitfall 3):**
```csharp
// Awake: events that may fire while the panel GO is INACTIVE (between chats / during Prep)
void Awake() {
    if (ChatManager.Instance != null) {
        ChatManager.Instance.OnChatSelected   += HandleChatSelected;   // L490 fires during Prep
        ChatManager.Instance.OnActiveBotChanged += HandleBotChanged;   // L121
    }
}
void OnDestroy() { /* symmetric unsubscribe */ }

// OnEnable/OnDisable: events that only fire while ACTIVE and call StartCoroutine
void OnEnable()  { if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived += HandleLive; }
void OnDisable() { if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived -= HandleLive; }
```
> Verified on `MessageListView.cs` L82-92 (comment: "OnChatSelected subscription lives in Awake … delivery works even when the chat panel is inactive") and L102-136. `MessageHeaderView.cs` L30-45 does the identical Awake/OnDestroy split.

**Correlation/sequence guard — adapted from QuoteResolve capture (DATA-03):**
```csharp
private long   _requestSeq;     // increments per Request issued (global counter, A6)
private string _activeChatId;   // captured at issue time

private void IssueRequest(string steerTowardText, string lastIncomingText)
{
    long seq = ++_requestSeq;
    string chatId = ChatManager.Instance.CurrentChatId;   // DATA-04 accessor
    _activeChatId = chatId;
    _panel.ShowSkeleton();                                  // D-12: skeleton EVERY load
    var req = new SuggestionRequest { chatId = chatId, steerTowardText = steerTowardText,
                                      lastIncomingText = lastIncomingText, requestSeq = seq };
    _provider.Request(req, result => OnResult(seq, chatId, result));
}

private void OnResult(long seq, string capturedChatId, SuggestionResult result)
{
    if (!SuggestionSequenceGuard.IsCurrent(seq, _requestSeq, capturedChatId, ChatManager.Instance.CurrentChatId))
        return;                                            // superseded / chat switched → DISCARD
    _panel.Render(result);                                 // skeleton → cards | empty | error
}
```
> Maps onto QuoteResolve's `if (GetActiveProfileId() != profileId) break;` (L91) and `if (GetActiveProfileId() == profileId && ...) Apply else discard` (L142-145). Suggestions have NO `chatId` payload (so NOT a `CrossChatResponseGuard`-style payload compare) → monotonic seq + captured chat is the correct guard (RESEARCH A2).

**Auto-populate trigger (INT-02) — incoming-only, never writes composer (Pitfall 7):**
```csharp
private void HandleLive(List<MessageViewModel> msgs)
{
    if (!_semiAutoOn) return;                              // SEMI-03
    bool anyIncoming = msgs != null && msgs.Exists(m => m != null && m.isIncoming);
    if (!anyIncoming) return;                              // outgoing echoes also flow through this event
    IssueRequest(steerTowardText: null, lastIncomingText: LastIncoming(msgs)?.text);
}
```
> `OnLiveMessagesReceived` verified `ChatManager.cs` L55 ("only ever adds", L94 comment), fires L751/L1014. The "never overwrite dirty draft" rule (INT-02) is satisfied by the automatic path NEVER touching `inputField.text` — do NOT build a dirty-detection branch (RESEARCH Pattern 3 / Anti-Patterns).

**Composer hand-off (INT-01 / D-01) — set text, never send (D-03):**
```csharp
_bottomPanel.inputField.text = card.replyText;   // public on MessagesBottomPanel L11; fires onValueChanged → Send button shows
_bottomPanel.inputField.ActivateInputField();    // optional focus (matches reply-preview UX, MessagesBottomPanel L161)
// DO NOT call ChatManager.SendTextMessage (L1822) — only the Send button's PointerDown does (MessagesBottomPanel L113-128).
```

**Convention:** `[SerializeField] private` for `_panel`, `_bottomPanel`, provider wiring (ui-scripts rule — refs are `[SerializeField] private`, never public; consuming `inputField` which is already public is fine).

---

### `Assets/Scripts/Chat/SuggestionSequenceGuard.cs` (utility, pure predicate)

**Analog:** `CrossChatResponseGuard.cs` L10-38 — a `static` pure-function discard predicate, conservative ("never discard on missing data"), fully unit-testable.

**Pattern to mirror (static, no Unity types):**
```csharp
public static class SuggestionSequenceGuard
{
    /// True only when this result is the newest request AND the chat hasn't changed.
    public static bool IsCurrent(long resultSeq, long currentSeq, string capturedChatId, string currentChatId)
    {
        if (resultSeq != currentSeq) return false;                 // superseded / out-of-order
        if (capturedChatId != currentChatId) return false;         // chat switched under us
        return true;
    }
}
```
> Same structure as `CrossChatResponseGuard.IsForDifferentChat(...)`: a single static bool predicate the controller calls; the controller holds the state, the guard is stateless and headless-testable (mirrors how QuoteResolve holds state, CrossChatResponseGuard is the pure decision).

---

### `Assets/Scripts/Chat/SemiAutoStore.cs` (persistence, CRUD)

**Analog:** `OutboxStore.cs` (injectable root for testability — confirmed via `OutboxStoreTests.cs` L21 `new OutboxStore(() => _tempRoot)`) + bot-persistence PlayerPrefs key scheme.

**Key scheme (SEMI-02, verified key form):**
```csharp
private static string Key(string botId, string chatId) => $"{botId}_semiAuto_{chatId}";
public static bool IsOn(string botId, string chatId)
    => PlayerPrefs.GetInt(Key(botId, chatId), 0) == 1;     // default OFF (SEMI-03)
public static void Set(string botId, string chatId, bool on)
{
    PlayerPrefs.SetInt(Key(botId, chatId), on ? 1 : 0);
    PlayerPrefs.Save();                                     // mobile gets killed — flush (bot-persistence skill)
}
```
- `botId = ChatManager.Instance.CurrentBotId` (already public, `ChatManager.BotState.cs` L14).
- `chatId = ChatManager.Instance.CurrentChatId` (added by DATA-04).
- **Testability note (RESEARCH Environment §):** PlayerPrefs in EditMode writes the editor registry. Either keep these as thin `static` PlayerPrefs wrappers and have `SemiAutoStoreTests` use **unique key prefixes per test + DeleteKey cleanup**, OR (preferred, matches `OutboxStore`) take an injectable get/set delegate seam so the test can substitute an in-memory dictionary. Decide in the plan; `OutboxStore`'s `Func<string>` root injection is the precedent.
- **Orphaned keys on bot delete:** explicitly accepted this milestone — do NOT add enumeration/cleanup (STATE.md, RESEARCH Runtime State Inventory).

---

### `Assets/Scripts/UI/SuggestionsPanel.cs` (MonoBehaviour view, event-driven render)

**Analog A (spawn/clear lifecycle):** `QuickReplyPanel.cs` L56-110.
**Analog B (DOTween slide + hide-on-complete):** `MessagesBottomPanel.cs` L166-182.

**Spawn/clear lifecycle to reshape (QuickReplyPanel → vertical stack of 4 cards):**
```csharp
public void SetSuggestions(List<SuggestionItem> items) { Clear(); gameObject.SetActive(true); /* build 4 cards, badge on index 0 */ }
public void Clear() { foreach (var c in _cards) if (c) Destroy(c.gameObject); _cards.Clear();
                      for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject); }
public void Hide()  { /* DOTween slide-out THEN */ Clear(); gameObject.SetActive(false); }
```
> Reshape from QuickReplyPanel's two `HorizontalLayoutGroup` rows (2×2 grid, L64-88) to ONE `VerticalLayoutGroup` of 4 (D-04). DROP the dual-action arrow (D-01 single tap does both). Add `ShowSkeleton()` (D-12: 4 shimmer placeholders matching card shape, shown first-load AND every re-cluster) and `Render(SuggestionResult)` dispatching the 5-state machine (Skeleton/Cards/Empty/Error — PANEL-04, fixed footprint, no layout pop).

**DOTween motion to mirror (panel show/hide tied to toggle, D-10/D-11):**
```csharp
// Show (toggle on): slide-up + fade — model on MessagesBottomPanel.ShowPreview L166-172
rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _hiddenY);
_tween = rt.DOAnchorPosY(_restY, 0.25f).SetEase(Ease.OutCubic);   // UI-SPEC: 0.25s OutCubic
// Hide (toggle off): slide-down + fade, deactivate on complete — model on HidePreview L175-182
_tween = rt.DOAnchorPosY(_hiddenY, 0.20f).SetEase(Ease.InCubic).OnComplete(() => gameObject.SetActive(false));
```
> Capture rest/hidden Y once before any slide (MessagesBottomPanel L72-78 metrics-ready pattern). Kill the tween in `OnDisable` (`_tween?.Kill()`, L102-103).

**Convention:** all UI refs `[SerializeField] private` (ui-scripts rule); `OnLiveMessagesReceived`-driven render via the controller (event-driven, no polling). Skeleton/cards use neutral greys + RoundedCorners (UI-SPEC Color); never `UISprite.psd` on surfaces.

---

### `Assets/Scripts/UI/SuggestionCard.cs` (MonoBehaviour view item, tap callback)

**Analog:** `QuickReplyButton.cs` L6-44 — single-button item with a `Setup(...)` + `Action<string>` click event; DROP the arrow.

**Pattern to mirror (whole card = one tap target):**
```csharp
public class SuggestionCard : MonoBehaviour
{
    [SerializeField] private Button cardButton;        // was mainButton; NO arrowButton (D-01)
    [SerializeField] private TextMeshProUGUI replyText;
    [SerializeField] private TextMeshProUGUI intentLabel;   // inside the IntentChip
    [SerializeField] private GameObject recommendedBadge;   // top card only (PANEL-03/D-07)
    public event Action<string> OnTapped;

    public void Setup(SuggestionItem item, bool isTop)
    {
        replyText.text = item.text;
        intentLabel.text = item.intentLabel;
        recommendedBadge.SetActive(isTop);              // badge on index 0 ONLY
        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() => OnTapped?.Invoke(item.text));
    }
}
```
> Mirrors QuickReplyButton's `Setup` + `mainButton.onClick.RemoveAllListeners(); AddListener(() => OnMainClicked?.Invoke(_text));` (L32-36). Reply text: TMP `overflowMode = Ellipsis`, 2-line `maxVisibleLines` cap, EXPLICIT width clamp (Pitfall 6 — do NOT rely on NoWrap; same class of bug as the quoted-card width blow-out). Card press feedback: `DOPunchScale` 0.97 / 0.15s OutQuad (UI-SPEC Animation).

---

### `Assets/Scripts/UI/SemiAutoToggle.cs` (MonoBehaviour control, event-driven + CRUD)

**Analog:** `MessageHeaderView.cs` L30-45 (open-chat header host; `OnChatSelected` subscription in `Awake`, `currentChatId` tracking) + `SemiAutoStore` read/write.

**Pattern to mirror:**
- Sits in the open-chat `TopBar` (Main.unity L14989/15028, hosts `MessageHeaderView` — NOT `ChatsPanel/TopBar`, RESEARCH A5). Add as a sibling of the existing header children.
- Subscribe `OnChatSelected` (+ `OnActiveBotChanged`) in `Awake`, unsubscribe in `OnDestroy` — exactly `MessageHeaderView.cs` L30-45 (the panel/header is inactive between chats; `OnChatSelected` fires during Prep before activation).
- On chat-open / bot-switch: read `SemiAutoStore.IsOn(CurrentBotId, CurrentChatId)` → set lit state + drive panel show/hide.
- On tap: flip, `SemiAutoStore.Set(...)`, show panel + first `IssueRequest` (on) / `panel.Hide()` (off, D-11).
- Icon is an `Image` + sprite (NOT a TMP glyph — UI-SPEC anti-pattern). Lit = WhatsApp green `#25D366`; off = neutral grey `#54656F`. `DOColor` 0.15s OutQuad (UI-SPEC). Accessible label «Полуавтоматический режим» wired into the `Selectable` accessible name.

---

### `Assets/Editor/SuggestionsPanelBuilder.cs` (Editor builder, build-time construction)

**Analog A (build tree + SerializedObject wiring + guards):** `ChatsSearchBarBuilder.cs` L10-77.
**Analog B (RoundedCorners via direct `using`):** `BotSwitcherSheetBuilder.cs` L2/L195/L565-568.

**Builder skeleton to mirror (`ChatsSearchBarBuilder` L10-77):**
```csharp
[MenuItem("Tools/UI/Build Suggestions Panel")]
public static void Build()
{
    // 1. Validate selection (must be the MessagesPanel host); error + return if wrong.
    // 2. "already exists? abort" guard (ChatsSearchBarBuilder L44-52).
    Undo.SetCurrentGroupName("Build Suggestions Panel");
    int undoGroup = Undo.GetCurrentGroup();
    // 3. Build GO tree: panel sheet → header row (refresh control) → VerticalLayoutGroup of 4 cards + skeleton.
    // 4. Wire refs via SerializedObject (rewire consumers — see below).
    Undo.CollapseUndoOperations(undoGroup);
    EditorUtility.SetDirty(host);
}
```

**SerializedObject ref wiring (ChatsSearchBarBuilder L64-69) — builders MUST rewire serialized consumers:**
```csharp
var panel = panelGo.AddComponent<SuggestionsPanel>();
var so = new SerializedObject(panel);
so.FindProperty("...").objectReferenceValue = ...;
so.ApplyModifiedPropertiesWithoutUndo();
```

**RoundedCorners — use the DIRECT `using` form (Pitfall 5), NOT runtime reflection:**
```csharp
// Model on BotSwitcherSheetBuilder.cs L2/195/565-568 (compile-time ref works in an Editor script):
using Nobi.UiRoundedCorners;
var rounded = cardGo.AddComponent<ImageWithRoundedCorners>(); rounded.radius = 24f;   // ~24u cards
var topRounded = panelGo.AddComponent<ImageWithIndependentRoundedCorners>();
topRounded.r = new Vector4(TopCornerRadius, TopCornerRadius, 0f, 0f);                  // top-only sheet
```
> NOTE: `ChatsSearchBarBuilder` L122-129 uses the *reflection* fallback (`Type.GetType("...,Assembly-CSharp")`) which is the BUGGY form (the type is in its own UPM assembly — project memory `project_roundedcorners_assembly`). Prefer `BotSwitcherSheetBuilder`'s direct `using Nobi.UiRoundedCorners;` form. Radius ≈ half min-dimension for pills/chips/badge.

**Build, not hand-edit:** construct the panel/cards/chip/badge/skeleton + the top-bar toggle via this `[MenuItem]`; never hand-edit Main.unity hierarchy (STRUCTURE.md "New Page"; CLAUDE.md builder convention). Account for the Editor-closed `BuildHeadless` variant if the planner needs an unfocused run (project memory `project_builder_rewire_consumers`).

---

### `Assets/Scripts/Main/ChatManager.Suggestions.cs` (MODIFIED — new partial, accessor addition)

**Analog:** `ChatManager.BotState.cs` L10-14 (partial-class accessor pattern; `public string CurrentBotId { get; private set; }`).

**Add (DATA-04) — minimal, additive, keep existing private members:**
```csharp
public partial class ChatManager
{
    public string CurrentChatId => currentChatId;                          // expose private L139 read-only
    public IEnumerator WaitForChatFetchesDrain() => WaitForChatFetchesToDrain();  // public hook over private L1300
}
```
> `currentChatId` verified private at `ChatManager.cs` L139; `WaitForChatFetchesToDrain()` private at L1300. `CurrentBotId` is ALREADY public (`ChatManager.BotState.cs` L14) — do NOT re-add. Use a new partial file (`ChatManager.Suggestions.cs`) to isolate the diff, matching the `.BotState`/`.QuoteResolve`/`.Outbox` partial convention.
> **Pitfall 2:** the public drain hook lets a Phase-2 provider WAIT on `_chatFetchesInFlight` without incrementing it. Document that the suggestions provider must NEVER do `_chatFetchesInFlight++` (it is not a `messages/get` caller — CONCERNS.md).

---

### Tests (`Assets/Tests/Editor/Chat/`)

**Analog:** `CrossChatResponseGuardTests.cs` (pure predicate) + `OutboxStoreTests.cs` (injectable root + `[SetUp]`/`[TearDown]`).

**`SuggestionSequenceGuardTests.cs`** — model on `CrossChatResponseGuardTests.cs`: `[Test]` methods asserting `IsCurrent`:
- newest seq + same chat → current (keep);
- older/out-of-order seq → discard;
- chat switched under request → discard;
- conservative edge cases (mirrors the "never discard on missing data" tests).

**`SemiAutoStoreTests.cs`** — model on `OutboxStoreTests.cs` L8-21 (`[SetUp]`/`[TearDown]` + injectable seam): key scheme `{botId}_semiAuto_{chatId}`, persist→restore, bot-switch isolation (different botId = independent key), default-off read. Use the injectable get/set delegate (or unique key prefix + `PlayerPrefs.DeleteKey` cleanup) to avoid polluting the editor registry (RESEARCH Environment §).

**`MockSuggestionsProviderTests.cs`** — model on `OutboxStoreTests.cs` shape; assert PURE logic only (latency excluded): ranked best-first order, steer behavior (steered set differs / re-clusters toward the pick), error-path returns `SuggestionStatus.Error`, adversarial out-of-order emission.

> Test convention: no asmdef (compiles into `Assembly-CSharp-Editor`); `using NUnit.Framework;`; PascalCase `[Subject]Tests.cs`; 5-15 line methods (CONVENTIONS.md, TESTING.md). Run via `Tools/run-tests-headless.sh` (Editor closed) or `Temp/claude/run-tests.trigger` (Editor open). Watch the new-file import quirk (Pitfall 4): if a new test's type isn't found, delete `.cs`+`.meta`, let Unity register the deletion, recreate.

---

## Shared Patterns

### Subscription Lifecycle (Awake vs OnEnable)
**Source:** `MessageListView.cs` L82-154, `MessageHeaderView.cs` L30-45.
**Apply to:** `SuggestionsController.cs`, `SemiAutoToggle.cs`.
```csharp
void Awake()  { ChatManager.Instance.OnChatSelected += ...; ChatManager.Instance.OnActiveBotChanged += ...; }  // may fire while INACTIVE
void OnEnable() { ChatManager.Instance.OnLiveMessagesReceived += ...; }                                        // only fires while ACTIVE (StartCoroutine-safe)
void OnDisable(){ ChatManager.Instance.OnLiveMessagesReceived -= ...; }
void OnDestroy(){ ChatManager.Instance.OnChatSelected -= ...; ChatManager.Instance.OnActiveBotChanged -= ...; }
```
Always null-guard `ChatManager.Instance` before subscribing (every analog does). No polling.

### Capture-and-Discard Concurrency Guard
**Source:** `ChatManager.QuoteResolve.cs` L82-153 (capture id at start, `break`/discard on change after the async hop); `CrossChatResponseGuard.cs` (the stateless predicate form).
**Apply to:** `SuggestionsController.cs` (issue) + `SuggestionSequenceGuard.cs` (decide). Capture `requestSeq` + `chatId` at issue, discard any callback where `seq != _requestSeq` or `capturedChatId != CurrentChatId`. NEVER hand-roll a fresh flag soup.

### DOTween UI Motion (never Animator)
**Source:** `MessagesBottomPanel.cs` L166-182 (`DOAnchorPosY` + `.OnComplete(SetActive(false))`, `_tween?.Kill()` in OnDisable).
**Apply to:** `SuggestionsPanel` (slide show/hide), `SuggestionCard` (DOPunchScale tap), `SemiAutoToggle` (DOColor), skeleton shimmer (looping tween). Durations/eases per UI-SPEC Animation table. `using DG.Tweening;`.

### RoundedCorners (direct `using` in the builder)
**Source:** `BotSwitcherSheetBuilder.cs` L2/195/565-568.
**Apply to:** `SuggestionsPanelBuilder.cs` for all card/chip/badge/panel surfaces. Direct `using Nobi.UiRoundedCorners;` + `AddComponent<ImageWithRoundedCorners>()` — NOT `Type.GetType(...,Assembly-CSharp)` (that's the buggy form in ChatsSearchBarBuilder L122). Null-sprite Image + RoundedCorners; never `UISprite.psd` on surfaces.

### Builder Construction + SerializedObject Rewiring
**Source:** `ChatsSearchBarBuilder.cs` L10-77.
**Apply to:** `SuggestionsPanelBuilder.cs`. Validate selection → "already exists? abort" guard → `Undo.SetCurrentGroupName`/`CollapseUndoOperations` → build tree → wire ALL serialized refs via `new SerializedObject(comp)` + `FindProperty(...).objectReferenceValue` + `ApplyModifiedPropertiesWithoutUndo()` → `EditorUtility.SetDirty`. Builders MUST rewire every serialized consumer.

### PlayerPrefs Per-Chat Persistence
**Source:** bot-persistence skill + `OutboxStore.cs` injectable-root pattern (`OutboxStoreTests.cs` L21).
**Apply to:** `SemiAutoStore.cs`. Key `{botId}_semiAuto_{chatId}`, `PlayerPrefs.Save()` after writes, default OFF. Derive botId/chatId from live ChatManager accessors. Keep an injectable seam for headless testing.

### `System.Action<T>` Callback Async Convention
**Source:** networking rule + CONVENTIONS.md "Callback Pattern".
**Apply to:** `ISuggestionsProvider.Request(...)`, `MockSuggestionsProvider`. Async result via callback, never async/await; the seam stays pure C# (the consumer supplies the coroutine runner).

---

## No Analog Found

None. Every new file maps to a verified in-repo analog. The only genuinely greenfield material is:
- The `ISuggestionsProvider` interface declaration itself (no existing C# interface seam in the repo — but it follows the established `Action<T>` callback convention, so the *shape* is conventional).
- The Russian mock CONTENT (D-14) — copy, not a code pattern.
- The `IntentChip` / `RecommendedBadge` / `SkeletonCard` visual sub-components — no exact analog component, but they are built with the same RoundedCorners + TMP + DOTween primitives the builder analogs use (treat UI-SPEC as the contract; reuse the builder/RoundedCorners patterns above).

---

## Metadata

**Analog search scope:** `Assets/Scripts/Chat/`, `Assets/Scripts/UI/`, `Assets/Scripts/Main/`, `Assets/Editor/`, `Assets/Tests/Editor/Chat/`, `.claude/rules/`, `.planning/codebase/`.
**Files scanned (read in full or targeted):** QuickReplyPanel.cs, QuickReplyButton.cs, MessagesBottomPanel.cs, ChatManager.QuoteResolve.cs, CrossChatResponseGuard.cs, MessageHeaderView.cs, MessageListView.cs, ChatManager.BotState.cs, ChatManager.cs (grep), ChatsSearchBarBuilder.cs, CrossChatResponseGuardTests.cs, OutboxStoreTests.cs, STRUCTURE.md, CONVENTIONS.md, networking/ui-scripts/editor-scripts/unity-general/csharp-quality rules, 01-CONTEXT.md, 01-RESEARCH.md, 01-UI-SPEC.md.
**Pattern extraction date:** 2026-06-23

## PATTERN MAPPING COMPLETE
