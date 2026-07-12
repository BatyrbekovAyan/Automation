---
phase: 01-polished-suggestions-panel-on-mock-data
verified: 2026-06-25T13:05:00Z
status: human_needed
score: 17/17 must-haves verified (programmatic); 4 runtime/visual items routed to human
overrides_applied: 0
human_verification:
  - test: "Per-chat semi-auto persistence survives an app restart"
    expected: "Flip a chat to semi-auto, fully quit and relaunch the app (device build) → the same chat reopens with the toggle lit and the panel shown; other chats stay manual/no-panel."
    why_human: "PlayerPrefs round-trip is unit-tested (SemiAutoStoreTests 5/5), but an actual app-restart cycle on device can only be confirmed by running the build — SC-1 explicitly requires 'survives an app restart.'"
  - test: "Panel renders all visual states at a fixed footprint with no layout pop"
    expected: "In Play Mode (1080×2400): toggle on → 4 shimmer skeletons → 4 ranked RU cards; «Рекомендуем» badge on the TOP card only; one card holds the 209-char reply truncated to ~2 lines + ellipsis without widening the card; empty («Нет предложений») and error («Не удалось загрузить» + «Обновить») states render at the SAME footprint; rounded corners on sheet/cards/chip/badge."
    why_human: "Visual appearance, truncation rendering, and no-jank state transitions (SC-2 / PANEL-04 / PANEL-06) are not EditMode-testable. User reported 'seems working' this session; a deliberate pass over each state confirms the contract."
  - test: "End-to-end card-tap hand-off + re-cluster, and incoming auto-populate never overwrites a draft"
    expected: "Tap a card → its RU text loads into the composer (editable, overwrites any draft) AND a fresh steered set of 4 appears; nothing auto-sends (only the Send button delivers). Start typing a draft, then trigger an incoming message → the cards refresh but the composer draft is NOT touched (INT-01/INT-02/INT-04)."
    why_human: "Real-time interaction loop and composer state behavior require Play Mode / a live incoming message. Code paths verified (inputField.text set ONLY in HandleCardTapped; HandleLive issues a card-only request), but the runtime UX must be observed."
  - test: "Stale/out-of-order/crossed responses never render under rapid picks + chat switches"
    expected: "Rapidly tap several cards and/or switch chats mid-request (mock latency ~1s) → no stale or crossed set ever appears; the newest request for the current chat wins; superseded/foreign responses are silently discarded (SC-5 / DATA-03)."
    why_human: "The guard predicate is unit-tested (SuggestionSequenceGuardTests 5/5) and wired in OnResult, and the mock can emit out-of-order seqs, but the concurrency outcome under real rapid interaction is only observable in Play Mode / on device."
---

# Phase 1: Polished Suggestions Panel on Mock Data — Verification Report

**Phase Goal:** A fully functional, visually polished Reply Suggestions Panel — per-chat semi-auto toggle, 4 ranked cards, pick-to-composer hand-off, manual refresh, and the re-cluster steering loop — running end-to-end against a `MockSuggestionsProvider` behind the `ISuggestionsProvider` seam. Demoable and shippable with NO backend, with a hard seam-purity contract (nothing above the seam references n8n / UnityWebRequest / Wappi).

**Verified:** 2026-06-25T13:05:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria + merged PLAN must-haves)

| #   | Truth | Status | Evidence |
| --- | ----- | ------ | -------- |
| 1 | SC-1: Owner flips a chat into semi-auto via a per-chat toggle; panel appears; state survives restart + bot switch; other chats stay manual | ✓ VERIFIED (code) / ? runtime | `SemiAutoToggle` view + `SuggestionsController.HandleToggle` persist via `SemiAutoStore.Set` (keyed `{botId}_semiAuto_{chatId}`, default OFF); `RestoreForActiveChat`/`ResetForNoOpenChat` restore on chat-open/bot-switch; SemiAutoStoreTests 5/5 (key scheme, default-OFF, round-trip, bot+chat isolation). Restart cycle → human. |
| 2 | SC-2: 4 cards best-first, reply text + intent label, "Recommended" on top card only, no numeric %, clean truncation, loading/empty/error without jank | ✓ VERIFIED (code) / ? visual | Mock returns 4 best-first items (lead label «Приветствие»); `SuggestionCard.Setup(item, i==0)` badge top-only; `SuggestionsPanel.Render` switches Ok/Empty/Error at fixed footprint; long reply = 209 chars (>120); MockSuggestionsProviderTests 8/8. No `%`/score in any view (confirmed by read). Visual no-jank → human. |
| 3 | SC-3: Tapping a card loads text into composer to edit, never auto-sends; incoming auto-populates without overwriting a draft; manual refresh | ✓ VERIFIED (code) / ? runtime | `HandleCardTapped` sets `inputField.text` (ONLY occurrence) + `ActivateInputField`, no `SendTextMessage`; `HandleLive` issues card-only request (never writes composer); `HandleManualRefresh` re-issues. Live behavior → human. |
| 4 | SC-4: Picking a card regenerates a fresh steered set of 4; owner refines or sends via existing Send button | ✓ VERIFIED | `HandleCardTapped` → `IssueRequest(steerTowardText: replyText, …)`; mock `BuildSteeredSet` returns a different ordered set (steered lead «Запись» ≠ fresh «Приветствие»); send path untouched (no `SendTextMessage`). |
| 5 | SC-5: Rapid picks/chat switches never render stale/crossed sets — out-of-order/superseded discarded via correlation/sequence guard; nothing above the seam references n8n/UnityWebRequest/Wappi | ✓ VERIFIED (code) / ? runtime | `SuggestionSequenceGuard.IsCurrent` wired in `OnResult` (seq + captured chat + `_semiAutoOn`); `_requestSeq++` supersedes on toggle-off/disable/bot-change; mock emits stale seqs (`forcedEchoSeq`/`simulateOutOfOrder`); SuggestionSequenceGuardTests 5/5. **Seam grep CLEAN across all 11 above-seam files.** Concurrency under real rapid input → human. |

**Score:** 17/17 phase requirements satisfied programmatically; SC-1/2/3/5 carry runtime/visual confirmations routed to human (Wave 2/3 are not EditMode-testable).

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `Assets/Scripts/Chat/ISuggestionsProvider.cs` | DATA-01 seam | ✓ VERIFIED | `void Request(SuggestionRequest, Action<SuggestionResult>)`; no forbidden tokens. |
| `Assets/Scripts/Chat/SuggestionRequest/Result/Item/Status.cs` | value objects | ✓ VERIFIED | `[Serializable]` request DTO; enum Ok/Empty/Error; `{text,intentLabel}`. |
| `Assets/Scripts/Chat/SuggestionSequenceGuard.cs` | DATA-03 predicate | ✓ VERIFIED | `IsCurrent(...)` seq + Ordinal chat compare; mirrors CrossChatResponseGuard. |
| `Assets/Scripts/Chat/MockSuggestionsProvider.cs` | DATA-02 mock | ✓ VERIFIED | 4 ranked RU replies (one 209 chars), `WaitForSeconds` latency, steered set, error/empty/out-of-order; pure `BuildResult`. |
| `Assets/Scripts/Chat/SemiAutoStore.cs` | SEMI-02 persistence | ✓ VERIFIED | `{botId}_semiAuto_{chatId}`, default OFF, `PlayerPrefs.Save()`, injectable seam. |
| `Assets/Scripts/Main/ChatManager.Suggestions.cs` | DATA-04 accessors | ✓ VERIFIED | `CurrentChatId => currentChatId`; `WaitForChatFetchesDrain() => WaitForChatFetchesToDrain()`; private members confirmed still private (L139/L1300). |
| `Assets/Scripts/UI/SuggestionCard.cs` | PANEL-02/03/06 | ✓ VERIFIED + WIRED | `Setup(SuggestionItem, bool)`; badge top-only; in-scene refs (cardButton/replyText/intentLabel/recommendedBadge) all non-null. |
| `Assets/Scripts/UI/SuggestionsPanel.cs` | PANEL-01..05 | ✓ VERIFIED + WIRED | 5-state machine + `DOAnchorPosY` slide + shimmer; 185 lines; all 9 in-scene refs resolve non-null. |
| `Assets/Scripts/UI/SemiAutoToggle.cs` | SEMI-01 view | ✓ VERIFIED + WIRED | `OnToggled`/`SetLit` (DOColor green/grey); toggleButton + iconImage wired. iconImage sprite = null (color-driven indicator; non-blocking, documented). |
| `Assets/Editor/SuggestionsPanelBuilder.cs` | [MenuItem] builder | ✓ VERIFIED | 409 lines; direct `Nobi.UiRoundedCorners` (no buggy reflection); VerticalLayoutGroup; SerializedObject wiring; all RU copy verbatim. |
| `Assets/Scripts/Chat/SuggestionsController.cs` | live mediator | ✓ VERIFIED + WIRED | 180 lines; guard in OnResult; Awake/OnEnable subscription split; in-scene `_panel`/`_toggle`/`_bottomPanel` all resolve to real components; hosted on MessagesBottomPanel GO (correct active-while-open lifecycle host). |
| `Assets/Editor/SuggestionsControllerWirer.cs` | [MenuItem] wirer | ✓ VERIFIED | FindFirstObjectByType (incl. inactive) for all 3 refs; SerializedObject apply; missing-dep guard. |
| `Assets/Tests/Editor/Chat/*Tests.cs` (×3) | EditMode coverage | ✓ VERIFIED | 18 [Test] methods, 25 real assertions; confirmed green this session (5/8/5). |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | -- | --- | ------ | ------- |
| MockSuggestionsProvider | ISuggestionsProvider | implements interface | ✓ WIRED | `class MockSuggestionsProvider : ISuggestionsProvider`; `WaitForSeconds` latency present. |
| ChatManager.Suggestions | private currentChatId / WaitForChatFetchesToDrain | read-only / public wrapper | ✓ WIRED | Both private members exist (L139/L1300), unchanged; partial compiles (no CS0102). |
| SuggestionsController | ChatManager.OnLiveMessagesReceived | OnEnable subscribe | ✓ WIRED | Event declared ChatManager.cs:55; subscribed OnEnable, unsubscribed OnDisable. |
| SuggestionsController | ChatManager.OnChatSelected / OnActiveBotChanged | Awake subscribe | ✓ WIRED | Events declared L53/L98; symmetric OnDestroy teardown. |
| SuggestionsController | MessagesBottomPanel.inputField | set .text on card tap | ✓ WIRED | `inputField` is public TMP_InputField (MessagesBottomPanel.cs:11); set only in HandleCardTapped. |
| SuggestionsController | ISuggestionsProvider | Request with monotonic seq | ✓ WIRED | `_provider.Request(req, result => OnResult(seq, chatId, result))`. |
| SuggestionsPanelBuilder | Nobi.UiRoundedCorners | direct using + AddComponent | ✓ WIRED | direct using; no `Type.GetType(...Assembly-CSharp)` reflection form. |
| SuggestionsPanel | DOTween | DOAnchorPosY slide | ✓ WIRED | Show/Hide use `DOAnchorPosY` + DOFade. |
| SuggestionCard | SuggestionItem | Setup(SuggestionItem, bool) | ✓ WIRED | Binds item.text/intentLabel; badge SetActive(isTop). |
| Controller / Panel / Toggle | Main.unity (serialized refs) | SerializedObject wiring | ✓ WIRED | All fileIDs resolve to genuine, type-correct components; none `{fileID: 0}`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| SuggestionsPanel | `result.items` (4 cards) | Controller `IssueRequest` → `_provider.Request` → MockSuggestionsProvider.BuildResult | Yes — 4 hardcoded RU replies (intentional Phase-1 mock; Phase 2 swaps provider) | ✓ FLOWING |
| SuggestionCard | `item.text` / `item.intentLabel` | SuggestionsPanel.RenderCards → Setup | Yes — bound from result items | ✓ FLOWING |
| SemiAutoToggle | lit color | `SetLit(SemiAutoStore.IsOn(...))` on restore | Yes — reads persisted per-chat bool | ✓ FLOWING |
| Composer | `inputField.text` | `HandleCardTapped(replyText)` | Yes — real picked reply | ✓ FLOWING |

Note: the mock's static RU content is the intended Phase-1 data source (the seam exists specifically so Phase 2 swaps a live provider on one line). Static content here is the spec, not a hollow stub.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Long reply > 120 chars exists | python len() on booking reply | 209 chars | ✓ PASS |
| Steered lead differs from fresh lead | label compare (Приветствие vs Запись) | differ | ✓ PASS |
| Seam purity (11 above-seam files) | grep UnityWebRequest/Networking/wappi/n8n/X-N8N | 0 hits | ✓ PASS |
| Controller: no SendTextMessage / _chatFetchesInFlight | grep | 0 hits | ✓ PASS |
| Mock named exactly once in controller | grep -c | 1 | ✓ PASS |
| Commits exist | git cat-file (4ab9c38/40f9fad/b7d8d40/958e80f) | all present | ✓ PASS |
| EditMode suites (18 tests) | Unity Test Runner (Editor open; headless refuses on lock) | 5/8/5 green this session (provided context) | ✓ PASS |
| Full Play Mode loop | requires Play Mode / device | not runnable headlessly | ? SKIP → human |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
| ----------- | ----------- | ----------- | ------ | -------- |
| SEMI-01 | 03, 04 | Per-chat semi-auto toggle | ✓ SATISFIED | Toggle view + HandleToggle persist/show; ? runtime visual |
| SEMI-02 | 02 | Per-chat persistence across restart/bot-switch | ✓ SATISFIED | SemiAutoStore + tests; restart → human |
| SEMI-03 | 04 | Panel only in semi-auto chats; others manual | ✓ SATISFIED | RestoreForActiveChat default OFF; HandleLive gated on `_semiAutoOn` |
| PANEL-01 | 03 | Sheet above composer in WhatsApp chat | ✓ SATISFIED | Panel parented sibling of quickReplyPanel; in-scene |
| PANEL-02 | 03 | 4 cards, reply text + intent label | ✓ SATISFIED | RenderCards spawns per item; chip label wired |
| PANEL-03 | 03 | Best-first, Recommended top-only, no % | ✓ SATISFIED | Setup(item, i==0); no numeric % in views |
| PANEL-04 | 03 | Loading/empty/error without jank | ✓ SATISFIED (code) | 5-state machine fixed footprint; ? visual |
| PANEL-05 | 03 | Dismiss/collapse → free typing | ✓ SATISFIED | Hide() slide-out; composer independent |
| PANEL-06 | 03 | Long text truncates cleanly | ✓ SATISFIED (code) | 209-char reply; Ellipsis/2-line per builder; ? visual |
| INT-01 | 04 | Card tap → composer, never auto-send | ✓ SATISFIED | inputField.text set; no SendTextMessage |
| INT-02 | 04 | Incoming auto-populates, never overwrites draft | ✓ SATISFIED | HandleLive card-only; ? runtime |
| INT-03 | 04 | Manual refresh | ✓ SATISFIED | HandleManualRefresh → IssueRequest |
| INT-04 | 04 | Pick re-clusters fresh steered set | ✓ SATISFIED | HandleCardTapped steerTowardText; mock steered set |
| DATA-01 | 01 | ISuggestionsProvider seam | ✓ SATISFIED | Pure interface, no forbidden tokens |
| DATA-02 | 01 | MockSuggestionsProvider stub data | ✓ SATISFIED | Ranked RU, latency, steer, correlation echo |
| DATA-03 | 01, 04 | Reject stale/out-of-order | ✓ SATISFIED | Guard + tests + wired OnResult |
| DATA-04 | 02 | ChatManager current-chat accessor + drain hook | ✓ SATISFIED | CurrentChatId + WaitForChatFetchesDrain |

**Orphaned requirements:** None. All 17 phase requirement IDs are claimed by at least one plan and verified. (SEMI-01 split across Plans 03/04; DATA-03 across Plans 01/04 — intentional logic/view + wiring splits.)

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| SemiAutoToggle (in-scene) | — | iconImage `m_Sprite: {fileID: 0}` (null sprite) | ℹ️ Info | Documented non-blocking placeholder; lit/unlit indicator is color-driven via DOColor on the Image, which works on a null/white sprite. Assign a real icon when convenient (no code change). |

No TODO/FIXME/HACK/PLACEHOLDER, no NotImplementedException, no empty returns, no stub handlers across the 15 source/editor files.

### Human Verification Required

See frontmatter `human_verification` for the 4 items. Summary:

1. **Restart persistence (SC-1)** — quit/relaunch a device build, confirm the lit toggle + panel restore for the semi-auto chat.
2. **Visual state machine (SC-2 / PANEL-04 / PANEL-06)** — Play Mode pass over skeleton/cards/empty/error at a fixed footprint; badge top-only; 209-char reply truncates to ~2 lines; rounded corners.
3. **Hand-off + auto-populate (INT-01/02/04)** — card tap fills composer + re-clusters (no auto-send); incoming refreshes cards without clobbering an in-progress draft.
4. **Stale-response discard under load (SC-5 / DATA-03)** — rapid picks + chat switches never render a crossed/stale set.

The user reported the running loop "seems working" this session (toggle → skeleton → cards, tap → composer + re-cluster, no auto-send, per-chat persistence). These items formalize the roadmap-contract behaviors for a deliberate sign-off; nothing here is a known defect.

### Gaps Summary

No blocking gaps. Every artifact exists, is substantive, is wired (all in-scene serialized refs resolve to type-correct, non-null components), and data flows from the mock through the controller into the panel and composer. The seam-purity invariant — the load-bearing Phase-1 contract — is grep-clean across all 11 above-the-seam files, and the mock is named on exactly one swappable line. All 17 requirements are accounted for with no orphans. Wave-1 logic is covered by 18 green EditMode tests with real assertions.

The phase is **goal-complete at the code/wiring level**. Status is `human_needed` (not `passed`) solely because the Wave-2 visual states and Wave-3 live interaction loop are not EditMode-testable — by the verifier's standing rule, visual appearance, user-flow completion, real-time behavior, and app-restart persistence always require human confirmation. The user's in-session "seems working" is strong positive signal; the four items above complete the formal sign-off.

---

_Verified: 2026-06-25T13:05:00Z_
_Verifier: Claude (gsd-verifier)_
