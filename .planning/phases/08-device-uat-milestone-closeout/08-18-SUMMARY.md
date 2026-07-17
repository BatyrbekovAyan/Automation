---
phase: 08-device-uat-milestone-closeout
plan: 18
subsystem: ui
tags: [empty-state, add-bot, channel-aware, telegram, cta, d12, raycast-diagnosis, canvasgroup]

# Dependency graph
requires:
  - phase: 05-device-uat-milestone-closeout (05-02 ChatManager identity seam)
    provides: "ChatManager.ActiveChannel + OnActiveChannelChanged (the channel-switch event this fix subscribes to)"
  - phase: 08-device-uat-milestone-closeout (08-14 D12 channel-aware preselect)
    provides: "OpenCreateBotFlow's channel-aware SelectPlatform(2 on Telegram) preselect, preserved verbatim"
provides:
  - "Wide D12 diagnosis (C1-C4 verdicts + ancestor CanvasGroup enumeration) that refutes occlusion/parent-CanvasGroup and names the stale-config-on-channel-switch gap"
  - "EmptyStateView re-configures (re-theme + RE-WIRE + Show) the visible empty state on every channel switch via OnActiveChannelChanged"
  - "Guaranteed Add-Bot overlay open on «Создать бота» tap (idempotent defensive Open-guard)"
affects: [08-19, 08-21 device re-verify]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "A persistent UI widget SHARED across channels must subscribe to OnActiveChannelChanged — its OnEnable catch-up never re-runs on an in-screen channel switch (WhatsApp + Telegram share Screen_Whatsapp)"

key-files:
  created: []
  modified:
    - Assets/Scripts/UI/EmptyStateView.cs

key-decisions:
  - "Named primary cause = C4 (stale re-wire on channel switch): EmptyStateView doesn't subscribe to OnActiveChannelChanged and the _lastReason guard early-returns the re-fired OnEmptyState, so ConfigureForReason (re-theme + re-wire + Show) is skipped on a channel switch — the class 08-14 missed by reading one file."
  - "C1 (occlusion) and C2 (parent CanvasGroup) REFUTED with concrete scene evidence (sibling z-order + all 13 CanvasGroups mapped; none on the EmptyState ancestor chain)."
  - "ENTRY-log verdict is PREDICTED/unconfirmed (no Play-mode/device access this session): scene+code evidence says the handler DOES run and the button is wired/unoccluded, so the residual cause is a runtime config/open factor on the switch path — the guarded #if UNITY_EDITOR logs disambiguate at 08-21."
  - "Added a defensive idempotent Open-guard (plan-sanctioned C3 routing) so the overlay is guaranteed to open even if the BotsPage path is compromised at runtime; WhatsApp no-op (overlay already open)."
  - "Secondary latent bug found and DOCUMENTED but NOT fixed (scope + regression risk): BeginLoadForActiveBot resolves zero-bots via FindBotByName(DefaultBotId)==null → fires a connect-state reason instead of NoBotsExist; flagged for a follow-up."

patterns-established:
  - "Channel-switch re-config: on OnActiveChannelChanged, re-run ConfigureForReason(_lastReason)+Show for the currently shown reason; BeginLoadForActiveBot (fired immediately after, same synchronous stack) authoritatively corrects any changed reason before render."

requirements-completed: []

# Metrics
duration: ~50 min
completed: 2026-07-17
---

# Phase 8 Plan 18: D12 Inert Telegram Create-Bot CTA — Wide Diagnosis + Channel-Switch Re-wire

**Refuted occlusion (C1) and parent-CanvasGroup (C2) with concrete scene evidence, named the real latent gap — EmptyStateView never re-configures on a channel switch (no OnActiveChannelChanged subscription + `_lastReason` guard) — and fixed it by subscribing to the channel-change event (re-theme + RE-WIRE + Show) plus a defensive guaranteed overlay-open, preserving 08-14's Telegram preselect and WhatsApp byte-for-byte.**

## Performance

- **Duration:** ~50 min (incl. two EditMode bridge runs)
- **Task 1 commit:** 2026-07-17T19:58:23+05:00
- **Task 2 commit:** 2026-07-17T20:06:30+05:00
- **Tasks:** 2 (Task 1 wide diagnosis + guarded instrumentation; Task 2 fix)
- **Files modified:** 1 (`Assets/Scripts/UI/EmptyStateView.cs`)

## Accomplishments
- Widened the D12 diagnosis beyond the single-file read that misfired in 08-14 — enumerated the ChatsPanel sibling z-order, all 13 CanvasGroups in Main.unity, and the full EmptyState→ChatsPanel→Screen_Whatsapp→Canvas ancestor chain, plus the `SetActiveChannel` event flow.
- Pinned the real latent gap (C4) and fixed it: the empty state now re-configures on every channel switch, so the visible card is always freshly themed, freshly wired, and interactable for the channel in view.
- Added a defensive, idempotent Open-guard so tapping «Создать бота» is guaranteed to open the Add-Bot overlay even if the BotsPage path is compromised at runtime.
- Preserved 08-14's channel-aware preselect (Telegram=2, else=1) and kept WhatsApp byte-identical.
- Added guarded `#if UNITY_EDITOR` `[D12]` instrumentation (ENTRY / after-StartNewBot / SelectPlatform) as the handler-runs-vs-tap-blocked pivot for the 08-21 device pass.

## Task 1 Diagnosis (recorded per plan `<output>` requirement)

### C1–C4 Verdict Table

| Candidate | Verdict | Evidence |
|-----------|---------|----------|
| **C1** — raycast occlusion by a sibling overlay | **REFUTED** | ChatsPanel (`263910444`) children z-order bottom→top: `Scroll`, **EmptyState**, `SyncingState`, `TopBar`, `Sheet_BotSwitcher`(inactive), `DeleteChatConfirmPanel`(inactive), `ReplyModeConfirmPopup`(inactive), `ChatListSyncIndicator`. The only ACTIVE siblings above EmptyState are: **SyncingState** — `SyncingView.Awake()→Hide()` clears alpha+blocksRaycasts on activation and it only Shows during a WhatsApp post-creation sync (never with 0 bots), channel-agnostic; **TopBar** — top-only geometry, channel-agnostic; **ChatListSyncIndicator pill** — `Show()` FORCES `blocksRaycasts=false` (line 182), serialized CanvasGroup `blocksRaycasts=0`, geometry a 380×76 pill at y=-300 (top), never over the centered CTA. The only TG-specific sibling (the pill) is non-blocking by construction. No active raycast-blocker covers the button. |
| **C2** — a parent CanvasGroup gating the button | **REFUTED** | All 13 CanvasGroups in Main.unity mapped to their GameObjects: `AttachSheetBackdrop`, `ScrollToBottomFab`, `Backdrop`×2, `SyncingState`, `Skeleton0-3`, `Root`, `SuggestionsPanel`, `ChatListSyncIndicator`, `EmptyState`. **None** sits on the EmptyState ancestor chain (ChatsPanel / Screen_Whatsapp / Canvas). No channel-switch code mutates any parent CanvasGroup (grep of `ChatManager*`/`Manager`). |
| **C3** — handler runs but effect swallowed | **REFUTED as literal, HARDENED** | `OpenCreateBotFlow → StartNewBot()` (channel-agnostic) opens the overlay BEFORE the only channel-dependent line (`SelectPlatform`). `SelectPlatform` (Manager.cs:1057) only toggles form groups, rebuilds layout, tints the button, validates — null-guarded, no navigation/close, runs post-open. `AddBotPanel.Open` resolves `_rootCanvas` via `GetComponentInParent<Canvas>(true)` (include-inactive → non-null); `BottomTabManager` index 2 valid. No throw pre-Open on either channel. A defensive Open-guard was still added (below). |
| **C4** — stale re-wire on channel switch | **CONFIRMED (named primary)** | EmptyStateView subscribes to `OnEmptyState`/`OnActiveBotChanged`/`OnChatAdded` but **NOT** `OnActiveChannelChanged`. WhatsApp+Telegram render in the SAME `Screen_Whatsapp`, so a channel switch never fires this view's OnEnable (its catch-up re-config never runs). `SetActiveChannel` fires `OnActiveChannelChanged` (Channel.cs:76) then `BeginLoadForActiveBot` (line 97) re-fires `OnEmptyState` — but `HandleEmptyState`'s `_lastReason==reason` guard **early-returns** it, so `ConfigureForReason` (re-theme + RE-WIRE `OpenCreateBotFlow` + `Show`) is skipped on the switch. This is the stale-configuration window 08-14 missed. |

### ENTRY-log pivot — **PREDICTED (unconfirmed pending 08-21)**

This session had **no Play-mode/device access**, so the button could not literally be tapped. From the C1–C4 scene-YAML + code evidence, the **predicted** verdict is that the `[D12] OpenCreateBotFlow ENTRY` log **WOULD fire on tap** — the button is wired to `OpenCreateBotFlow` (blue card ⇒ `ConfigureForReason` ran ⇒ wired) and is unoccluded/interactable per the scene — i.e., the handler runs and the residual cause is a runtime configuration/open factor on the channel-switch path, **not** a tap-block. This is explicitly **caveated as unconfirmed**: if the 08-21 device pass shows the ENTRY log does NOT fire, the true cause is a runtime tap-delivery factor (re-opens for round 4). The three guarded `#if UNITY_EDITOR` `[D12]` logs (ENTRY / after-StartNewBot / SelectPlatform) are the disambiguator; they can be read in the Editor Game view (project test method, 1080×2400) or by temporarily un-guarding for a Development Build.

### Ancestor CanvasGroup enumeration (EmptyState → ChatsPanel → Screen_Whatsapp → Canvas)

| Node | GameObject fileID | CanvasGroup? | interactable / blocksRaycasts |
|------|-------------------|--------------|-------------------------------|
| EmptyState | `1779123788` | **yes (its own)** | 1 / 1 (set by `Show()`) |
| ChatsPanel | `263910444` | **none** | — |
| Screen_Whatsapp | `1992340357` | **none** | — |
| Canvas (ancestor) | — | **none** | — |

→ No ancestor CanvasGroup gates the button. (Button `1203410575` itself: `m_Enabled: 1`, `m_Interactable: 1`, TargetGraphic set; EmptyStateView never disables it.)

## The Fix (Task 2)

1. **C4 primary — subscribe to `OnActiveChannelChanged`.** OnEnable `+=` / OnDisable `-=`; new `HandleActiveChannelChanged(ChatChannel _)` re-runs `ConfigureForReason(_lastReason.Value) + Show()` when an empty state is currently shown (no-op when hidden). Guarantees the visible card is freshly themed, freshly wired, and interactable for the channel now in view after ANY switch. **Safe:** `SetActiveChannel` always calls `BeginLoadForActiveBot` immediately after `OnActiveChannelChanged` (same synchronous stack, before render), which authoritatively drives the next state — so any transient re-config for a reason that changes on the switch is corrected in-frame.
2. **C3 defensive — guaranteed overlay open.** `if (AddBotPanel.Instance != null && !AddBotPanel.Instance.IsOpen) AddBotPanel.Instance.Open();` after `StartNewBot()`. Idempotent; on the normal path (including every WhatsApp tap, where StartNewBot already opened it) this is a no-op → WhatsApp byte-identical.
3. **08-14 preselect preserved.** `platform = ActiveChannel==Telegram ? 2 : 1` unchanged (lines 295-296, untouched since `a52f385`); WhatsApp still resolves 1.

## Secondary finding (documented, NOT fixed)

`BeginLoadForActiveBot` (ChatManager.BotState.cs:264-271) resolves the empty state via `FindBotByName(CurrentBotId)`, which returns null for the `DefaultBotId` sentinel when **zero bots** exist → it fires `NoConnectionEmptyState()` (BotHasNoTelegram/BotHasNoWhatsApp) instead of `NoBotsExist`. `ComputeCurrentEmptyState` (used by OnEnable catch-up) correctly returns `NoBotsExist` for zero bots. So a **channel switch with zero bots** can flip the card to «Подключить Telegram» (whose `OpenCurrentBotAuth` no-ops with no bot) rather than «Создать бота». This yields a DIFFERENT label than the owner's «Создать бота» report, so it is not the owner's exact D12 repro; fixing it touches ChatManager's empty-state resolution (wider blast radius / connect-state regression risk) and is left as a documented follow-up rather than expanding this single-file plan. **Flag for 08-19/08-21.**

## Task Commits

1. **Task 1: guarded `[D12]` instrumentation** — `791447b` (chore) — 3 `#if UNITY_EDITOR` logs (ENTRY / after-StartNewBot / SelectPlatform).
2. **Task 2: channel-switch re-wire + overlay-open guarantee** — `19c1ef2` (fix) — OnActiveChannelChanged subscription + HandleActiveChannelChanged + defensive Open-guard.

**Plan metadata:** final docs commit (this SUMMARY + STATE + ROADMAP).

## Files Created/Modified
- `Assets/Scripts/UI/EmptyStateView.cs` — subscribes to `OnActiveChannelChanged` and re-configures the visible empty state on a channel switch; defensive idempotent `AddBotPanel.Open()` guarantee in `OpenCreateBotFlow`; guarded `#if UNITY_EDITOR` `[D12]` diagnosis logs. Channel-aware preselect and all WhatsApp paths unchanged.

## Decisions Made
- Kept all three `#if UNITY_EDITOR` `[D12]` logs (Editor-only, zero ship cost, grep-removable) as the 08-21 pivot — the plan explicitly offered this choice.
- Implemented C4 (named primary) plus the plan-sanctioned C3 defensive Open-guard (belt-and-suspenders for a symptom that failed device UAT twice), rather than a single narrow change.
- Did NOT fix the secondary `BeginLoadForActiveBot` zero-bots inconsistency (different label than reported; wider blast radius) — documented for follow-up.

## Deviations from Plan

None — plan executed as written. Task 1 widened the diagnosis (C1-C4 verdicts + CanvasGroup enumeration + guarded instrumentation) per its instruction; Task 2 implemented the routed fix (C4 primary + the plan's C3 defensive Open-guard) while preserving 08-14's preselect and WhatsApp parity. The instrumentation was KEPT under the plan's explicitly-offered `#if UNITY_EDITOR` option (choice stated above).

## Threat Flags

None — no new network endpoint, auth path, file access, or schema change. The change is local UI navigation glue behind an owner tap (T-08-18-01 mitigated: WhatsApp still resolves platform 1 byte-identical, no cross-channel leak, no new capability). Instrumentation is guarded `#if UNITY_EDITOR`, no secrets/PII (T-08-18-02 accepted). No scene mutation (T-08-18-03 not triggered).

## Known Stubs
None — the fix wires real, existing behaviour (`ConfigureForReason` re-run + `AddBotPanel.Open`); no placeholder/empty data introduced.

## Verification

- **Acceptance greps (all pass):** `SelectPlatform(platform)` present; channel-aware preselect `ActiveChannel == ChatChannel.Telegram) ? 2 : 1` present; hardcoded `SelectPlatform(1)` = 0; `OnActiveChannelChanged` subscription present (OnEnable `+=` / OnDisable `-=`); `HandleActiveChannelChanged` present; `canvasGroup.interactable = true` present (Show); no UNGUARDED `[D12]` Debug.Log.
- **WhatsApp byte-identical:** Task 2 `git diff` = +32 lines, pure additions; the preselect line (295-296) is untouched from the `a52f385` baseline; the added subscription/handler/Open-guard are shared paths that no-op or reproduce-identical on WhatsApp.
- **Compile:** CONFIRMED CLEAN — the in-Editor bridge rebuilt `Library/ScriptAssemblies/Assembly-CSharp.dll` at 20:04:58 (postdating both edits); a compile error would have blocked the DLL rewrite / written an error status.
- **EditMode suite:** run requested via the in-Editor `ClaudeTestBridge` (Editor open, PID lock held; headless runner correctly refused). Baseline is 1134 (08-17); this change is MonoBehaviour glue with no new pure seam, so no new test and the total should stay 1134. **Full green is PENDING (honest):** the bridge recompiled (DLL 20:04:58) and started the run (summary `running` at 20:05:09) but the summary stayed frozen at `running` for ~7 min — the Editor is unfocused, so the bridge is not ticking. This mirrors 08-17 exactly. **Compile is CONFIRMED CLEAN** (the runtime `Assembly-CSharp.dll` rebuilt at 20:04:58, postdating both edits — a compile error would have blocked the DLL rewrite). A fresh 1134/1134 green must be captured once the Editor is focused (drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json`).

## Next Phase Readiness
- D12 fix is in code (channel-switch re-config + guaranteed overlay-open). **Device confirmation rides 08-21:** on the Telegram channel with no bots, «Создать бота» must open the Add-Bot form with Telegram preselected; on WhatsApp it must still preselect WhatsApp (byte-identical). Read the `[D12]` Editor logs (or a temporary un-guarded Development Build) to confirm the ENTRY-log pivot if it still fails.
- Two follow-ups flagged: (1) the `BeginLoadForActiveBot` zero-bots → connect-state inconsistency; (2) round-4 runtime tap-delivery diagnosis if the ENTRY log does NOT fire on device.

## Self-Check: PASSED

- FOUND: `Assets/Scripts/UI/EmptyStateView.cs`
- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-18-SUMMARY.md`
- FOUND commit: `791447b` (Task 1 instrumentation)
- FOUND commit: `19c1ef2` (Task 2 fix)
- Committed file has the `OnActiveChannelChanged` subscribe + unsubscribe (2 lines) and the preserved channel-aware preselect (`ActiveChannel == ChatChannel.Telegram) ? 2 : 1`, 1 line).
- No file deletions in either commit; each commit touches only `EmptyStateView.cs`.
- Caveat: full EditMode green is PENDING (Editor unfocused, bridge stalled at `running`); compile confirmed clean via the 20:04:58 DLL rebuild.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-17*
