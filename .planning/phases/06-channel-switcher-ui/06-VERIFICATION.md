---
phase: 06-channel-switcher-ui
verified: 2026-07-13T11:42:24Z
status: human_needed
score: 4/4 must-haves verified (structural); 6 human verification items open
overrides_applied: 0
human_verification:
  - test: "Pill placement + styling: open Main.unity in Play mode at 1080x2400, look at Screen_Whatsapp/ChatsPanel/TopBar/CenterZone"
    expected: "WhatsApp | Telegram segmented pill sits centered in the TopBar, matches the neighbouring ModeToggle's corner rounding / font / height / fill-vs-transparent segment language; vertical alignment reads clean next to ModeToggle"
    why_human: "Visual appearance (corner rounding read, alignment offset, font weight match) cannot be judged from scene YAML — structural values (radius 38/32, #EFEFF0 track, header font GUID a2b0b38b6764047da9250bcff1b0f432, 340x76 track) are confirmed present but 'looks right next to ModeToggle' is a human visual judgment"
  - test: "On a both-channels bot, tap Telegram then WhatsApp in the pill; re-tap the already-selected chip"
    expected: "Chat list swaps with the full reset choreography (clear then reload), no crossed lists, no half-loaded rows, no visible flicker; re-tapping the active chip is a no-op with no reload flash"
    why_human: "Real-time UI behavior under async network/cache timing cannot be verified headless; SetActiveChannel's reset choreography was built+tested in Phase 5, but the actual tap-triggered visual swap requires Play mode or device"
  - test: "On a WhatsApp-only bot, inspect the Telegram chip, then tap it; repeat on a Telegram-only bot with the WhatsApp chip"
    expected: "Muted chip reads clearly faded (~40% alpha) yet obviously tappable (not greyed-dead); tapping it selects that channel and surfaces the connect empty state (BotHasNoTelegram/BotHasNoWhatsApp CTA), not a blank screen; both chips always visible"
    why_human: "Legibility of a 40%-alpha fade and 'looks tappable vs looks dead' is a visual judgment; ChannelSwitcherModel's Muted logic and the Button's interactable=1 are code-verified, but the perceptual read needs a human eye"
  - test: "Open a Telegram-only bot fresh, then open a WhatsApp-only bot fresh"
    expected: "Telegram-only bot opens with Telegram already selected (filled) and WhatsApp muted, no manual tap; WhatsApp-only bot opens WhatsApp-selected, Telegram muted"
    why_human: "Auto-select on bot open is an end-to-end runtime flow (05-02 ChannelResolver -> ChatManager.ActiveChannel -> binder OnEnable catch-up) that requires opening real bot data in Play mode/device to confirm"
  - test: "Look at the bottom nav bar; tap Сводка, Bots, and Profile in turn"
    expected: "Exactly 4 tabs, no Telegram tab; tab 0 reads «Чаты»; each of Сводка/Bots/Profile lands on its correct screen; no blank pink Telegram screen reachable anywhere"
    why_human: "Structurally confirmed (tabs array = 4 entries, tab 0 m_text/tabName = «Чаты», BotsTabIndex=2/WhatsAppTabIndex=0 match, all SwitchTab consumers use constants), but confirming actual tap-to-screen routing and 'no dead screen reachable' end-to-end is a human navigation pass"
  - test: "On a both-channels bot, select Telegram, then stop and re-enter Play mode (or relaunch on device)"
    expected: "The bot reopens on Telegram — the per-bot {botId}ActiveChatChannel choice survives the restart"
    why_human: "Cross-session PlayerPrefs persistence through an actual restart cannot be exercised by static code/scene inspection; requires a live stop/relaunch cycle"
---

# Phase 6: Channel Switcher UI Verification Report

**Phase Goal:** The owner flips between the active bot's WhatsApp and Telegram chats within one screen via a TopBar segmented control, with muted/connect affordances for an unconnected channel, per-bot channel persistence, and the Telegram bottom tab retired.
**Verified:** 2026-07-13T11:42:24Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

Method used: structural-only (grep + fileID cross-reference against the committed `Assets/Scenes/Main.unity`, code reads of every phase-6 file, commit-hash verification, and one pre-existing fresh headless test run inspected for currency). No network calls, no scene builders, no Play-mode runs were performed, per task constraints.

### Observable Truths

All 4 truths come from `ROADMAP.md` Phase 6 Success Criteria (the contract), cross-checked against `06-01-PLAN.md`/`06-02-PLAN.md` frontmatter `must_haves.truths` (which decompose each SC into implementation-level sub-truths — no scope reduction found, PLAN truths are a superset of the roadmap SCs).

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Owner flips between WhatsApp/Telegram via a TopBar segmented control — full list-reset choreography, mid-flight-safe (no crossed lists) [SWITCH-01] | VERIFIED (structural) | Pill exists under `Screen_Whatsapp/ChatsPanel/TopBar/CenterZone` (`Main.unity:52153`, `CenterZone m_IsActive: 1` at `:85153`); `ChannelSwitcherView.OnChipTapped` (`ChannelSwitcherView.cs:96-107`) calls `ChatManager.Instance?.SetActiveChannel(channel)` unconditionally (no-ops on same channel per 05-02); all 6 required serialized refs stamped with correct, non-crossed fileIDs (verified below). The reset-choreography logic itself is Phase-5-owned and out of this phase's scope. **Visual/timing correctness of the swap (no flicker/crossing) is UNTESTABLE headless — see Human Verification #2.** |
| 2 | Unconnected channel's chip renders visibly muted; tapping it shows that channel's empty state with a connect CTA [SWITCH-02] | VERIFIED (structural) | `ChannelSwitcherModel.StateFor` (`ChannelSwitcherModel.cs:23-29`) computes `Muted` from connectivity only, never suppressed by `Selected` (locked by Tests A-F, all passing). `ChannelSwitcherView.ApplyChip` (`:145-164`) fades fill+label+icon to `MutedAlpha=0.40f` when muted. Both `WaChip`/`TgChip` Buttons have `m_Interactable: 1` in the committed scene (`:27169`, `:28885`) — muted chips are never made non-interactable, confirmed by code AND scene. **Perceptual legibility of the 40% fade and confirming the empty-state CTA actually renders is UNTESTABLE headless — see Human Verification #3.** |
| 3 | Last-used channel persists per bot across restarts; a bot with only one connected channel auto-selects it [SWITCH-03] | VERIFIED (structural) | `ChannelSwitcherView.Refresh()` (`:117-130`) reads `ChatManager.Instance.ActiveChannel` as sole source of truth (zero local persistence added in this phase, by design — 05-02 owns `{botId}ActiveChatChannel` + `ResolveChannelForBot`). `OnEnable` (`:60-71`) does an immediate late-activation catch-up `Refresh()`, so a bot switch while the screen was inactive is picked up. `ChannelResolutionTests` (05-02) remained green inside the same 901/901 suite run. **The actual persist-across-restart and auto-select behaviors are end-to-end runtime flows — UNTESTABLE headless — see Human Verification #4 and #6.** |
| 4 | Telegram bottom-nav tab and the `Screen_Telegram` placeholder are removed; tab 0 reads «Чаты» [SWITCH-04] | VERIFIED (structural) | `Main.unity` `BottomTabManager.tabs` array has exactly 4 entries (`:135725-135757`: Чаты/Сводка/Bots/Profile); tab 0 `tabName: "Чаты"` = «Чаты» AND its live TMP `labelText` (fileID 1373230419) `m_text: "Чаты"` = «Чаты» (both the inspector field and the actual rendered text were set). Zero occurrences of `Screen_Telegram`, fileID `163358610`, or `TelegramTab` anywhere in `Main.unity` or the rest of `Assets/` (excluding an untracked, pre-phase `Assets/_Recovery/0.unity` autosave — see Anti-Patterns note). `BottomTabManager.BotsTabIndex=2`/`WhatsAppTabIndex=0` match the 4-tab scene; `defaultTabIndex: 0` confirmed in scene. **Confirming actual tap-to-screen routing on device/Play mode is UNTESTABLE headless — see Human Verification #5.** |

**Score:** 4/4 truths verified structurally. All 4 also carry a behavioral/visual component that only a human pass can close — this is the phase's own designed escalation gate (`06-HUMAN-UAT.md`), not a verification shortfall.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Assets/Scripts/UI/ChannelSwitcherModel.cs` | Pure selection/muted decision seam; exports `ChannelSwitcherModel`, `ChannelChipState`; ≥20 lines | ✓ VERIFIED | 50 lines. Static class + readonly struct, no MonoBehaviour. `Selected = chip==active` (equality only), `Muted = !ownChannelConnected` (connectivity only, never suppressed by selection) — exactly per spec. Consumed by `ChannelSwitcherView.Refresh` and covered by 6 tests (A-F). |
| `Assets/Scripts/UI/ChannelSwitcherView.cs` | TopBar segmented-pill binder; contains `SetActiveChannel`; ≥60 lines | ✓ VERIFIED | 176 lines. `SetActiveChannel` called at line 106. Event-driven (`OnEnable`/`OnDisable` symmetric subscribe/unsubscribe, no `Update`); every serialized ref and `Manager`/`ChatManager`/bot lookup null-guarded. |
| `Assets/Scripts/Main/BottomTabManager.cs` | Post-restructure tab-index constants; contains `BotsTabIndex = 2` | ✓ VERIFIED | Line 76: `public const int BotsTabIndex = 2;`. `WhatsAppTabIndex = 0` unchanged (line 82). `defaultTabIndex` field initializer now reads `= WhatsAppTabIndex` (IN-02 fix), scene value confirmed `defaultTabIndex: 0`. |
| `Assets/Tests/Editor/Chat/ChannelSwitcherModelTests.cs` | Connectivity x active x chip matrix; contains `ChannelSwitcherModel` | ✓ VERIFIED | 103 lines, 6 `[Test]` methods (A-F, including the IN-03 Telegram-mirror fix). `results.xml`: 7 matches (class + 6 cases), 0 failures. |
| `Assets/Tests/Editor/Chat/TabIndexShiftTests.cs` | Guard test locking `BotsTabIndex==2`/`WhatsAppTabIndex==0` | ✓ VERIFIED | 29 lines, 4 `[Test]` methods. `results.xml`: 5 matches, 0 failures. |
| `Assets/Editor/ChannelSwitcherBuilder.cs` | Idempotent switcher builder + nav restructure, headless entry; contains `BuildHeadless`; ≥120 lines | ✓ VERIFIED | 405 lines. `BuildHeadless()` at line 76, `[MenuItem]` at line 61. Guarded, idempotent tab-1 deletion (lines 245-289) verified by code read matching REVIEW's description exactly. |
| `Tools/run-editor-builder.sh` | Editor-closed headless `-executeMethod` runner; contains `executeMethod` | ✓ VERIFIED | 153 lines, executable (`-x` confirmed). `-executeMethod "${METHOD}"` at line 109. WR-03/IN-05 fixes (sentinel auto-derivation, anchored lock-guard) present in the current file. |
| `Assets/Scenes/Main.unity` | ChannelSwitcher pill under CenterZone; Screen_Telegram removed; 4-tab bar; «Чаты» label; contains `ChannelSwitcher` | ✓ VERIFIED | Confirmed at fileID granularity (not just name-grep): `ChannelSwitcher` root (803382779, active) with `ChannelSwitcherView` component stamping 6 non-zero, non-crossed refs; `WaChip`/`TgChip` each own their respective Button/Fill/Label subtree (verified via `m_Father` RectTransform chase); `waLabel.m_text="WhatsApp"`, `tgLabel.m_text="Telegram"`. Zero `Screen_Telegram`/`163358610`/`TelegramTab`. |
| `.planning/phases/06-channel-switcher-ui/06-HUMAN-UAT.md` | Open owner visual-pass gate; contains `1080` | ✓ VERIFIED (present, intentionally OPEN) | 119 lines, 6-point 1080x2400 checklist + deferred-polish section + blank Resume section (`Result: PASS / ISSUES → ____`). This is the phase's designed escalation gate — its OPEN state is expected, not a defect. |

**9/9 artifacts VERIFIED** at exists+substantive+wired levels (Main.unity additionally verified at Level 4 fileID cross-reference to rule out crossed/duplicate refs).

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `ChannelSwitcherView.cs` | `ChatManager.SetActiveChannel` | chip Button onClick | ✓ WIRED | `OnChipTapped` (line 106) calls `ChatManager.Instance?.SetActiveChannel(channel)`; wired via `WireChip`'s `button.onClick.AddListener` in `Awake` (lines 85-90), mirroring `ReplyModeToggleBinder`. |
| `ChannelSwitcherView.cs` | Bot connectivity predicate | `Manager.FindBotByName` + `Bot.UnauthedProfileSentinel` | ✓ WIRED | Line 135: `profileId != Bot.UnauthedProfileSentinel`, copied verbatim from `BotSwitcherRowView`; called from `Refresh()` (line 120) via `Manager.Instance.FindBotByName(botId)`, null-guarded. |
| `ChannelSwitcherView.cs` | `ChatManager` events | `OnActiveBotChanged` + `OnActiveChannelChanged` subscription | ✓ WIRED | Lines 65-66 (`OnEnable`), lines 77-78 (`OnDisable`) — symmetric subscribe/unsubscribe confirmed. |
| `ChannelSwitcherBuilder.cs` | `ChannelSwitcherView` serialized refs | `SerializedObject` `SetRef` | ✓ WIRED | Lines 137-143 stamp all 6 refs; **cross-verified in the committed scene** — `waChipButton`(428859467)/`tgChipButton`(455921293)/`waChipFill`(201419614)/`tgChipFill`(22284518)/`waLabel`(766733600)/`tgLabel`(10928635) all non-zero and each traced by `m_Father` RectTransform back to its correct `WaChip`/`TgChip` parent (no crossing). |
| `ChannelSwitcherBuilder.cs` | `BottomTabManager.tabs` | guarded `DeleteArrayElementAtIndex(1)` when `tabs[1]` is Telegram | ✓ WIRED | Lines 256-288: identity guard (`tabName=="Telegram"` OR `screenPanel.name=="Screen_Telegram"`) before any delete; **confirmed executed** — committed scene shows `tabs` array shrunk to 4 entries with Telegram absent. |
| `NavRestructureBuilder.cs` | `ReorderScreens` order list | `Screen_Telegram` entry removed | ✓ WIRED | `ReorderScreens` function exists (line 449, called line 211); zero `Screen_Telegram` string occurrences anywhere in the file. |

**6/6 key links WIRED**, all confirmed at the fileID/cross-reference level where a scene mutation was involved (not name-presence alone).

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|---------------------|--------|
| `ChannelSwitcherView.Refresh()` | `waConnected`/`tgConnected`/`active` | `Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId)` → `Bot.whatsappProfileId`/`telegramProfileId` (PlayerPrefs-backed, real per-bot state, not hardcoded); `ChatManager.Instance.ActiveChannel` (05-02 auto-resolved) | Yes | ✓ FLOWING — no static/empty fallback; degrades to a *computed* WhatsApp-selected default only when `Manager`/`ChatManager`/bot is genuinely absent (null-guard, not a stub) |

### Behavioral Spot-Checks

Step 7b: **SKIPPED** — no runnable entry points reachable without launching the Unity Editor (Play mode) or a device build, both explicitly out of scope per this verification's hard rule (no scene builders, no network calls, no Play-mode runs). The equivalent behavioral checks are captured as Human Verification items #2-#6 below.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|--------------|--------|----------|
| SWITCH-01 | 06-01 (audit half) + 06-02 (pill + wiring) | Owner flips channels via TopBar segmented control, full reset choreography, mid-flight-safe | SATISFIED (code-complete); visual/behavioral confirmation pending human UAT #1-#2 | Pill built + wired (see Truth 1) |
| SWITCH-02 | 06-01 | Unconnected channel's chip visibly muted, tappable, reaches connect CTA | SATISFIED (code-complete); visual confirmation pending human UAT #3 | Model + binder logic verified (see Truth 2) |
| SWITCH-03 | 06-01 | Last-used channel persists per bot; single-channel bot auto-selects | SATISFIED (code-complete, read-only consumer of 05-02); runtime confirmation pending human UAT #4/#6 | Binder reads `ActiveChannel` read-only (see Truth 3) |
| SWITCH-04 | 06-01 (audit half) + 06-02 (scene removal) | Telegram tab + Screen_Telegram removed, tab 0 «Чаты» | SATISFIED (code-complete); routing confirmation pending human UAT #5 | Scene mutation verified at fileID level (see Truth 4) |

No orphaned requirements: `REQUIREMENTS.md`'s Phase 6 traceability rows (SWITCH-01..04) match exactly the union of `requirements:` declared across `06-01-PLAN.md` and `06-02-PLAN.md` frontmatter.

**Observation (not a gap):** `REQUIREMENTS.md` marks all 4 SWITCH-0x rows `[x]` **Complete**. Per this phase's own design (`06-HUMAN-UAT.md`: "the requirements it validates... are only *proven* on a green pass here"), "Complete" should be read as *code-complete*, not *fully verified* — the open human gate is the final proof step. This is a pre-existing documentation-convention nuance (same pattern used in Phases 3/4), not a phase-6 defect; flagged for awareness only.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| — | — | None found | — | Clean scan across all 8 phase-6 files (`ChannelSwitcherModel.cs`, `ChannelSwitcherView.cs`, `BottomTabManager.cs`, `ChannelSwitcherBuilder.cs`, `NavRestructureBuilder.cs`, `ChannelSwitcherModelTests.cs`, `TabIndexShiftTests.cs`, `run-editor-builder.sh`): zero TODO/FIXME/XXX/HACK/PLACEHOLDER markers, zero `interactable=false` on either chip Button (confirmed both code and scene, required for SWITCH-02), zero empty-implementation patterns. |

**Info (unrelated to this phase, noted for hygiene):** an untracked `Assets/_Recovery/0.unity` (Unity crash/autosave recovery folder, dated 2026-07-07 — before phase 6 work began 2026-07-13) still contains stale `TelegramTab`/`Screen_Telegram` objects. It is not part of `EditorBuildSettings`, not referenced by any script, and not tracked by git (`?? Assets/_Recovery/` in `git status`). Irrelevant to the committed `Main.unity` this phase verifies, but worth a manual cleanup pass since it sits under `Assets/`.

**Info:** The code-review pass (`06-REVIEW.md`) found 0 critical / 3 warning / 5 info issues, all 8 marked `FIXED` in commits `7bcd0f5`..`1446b42`. Spot-verified 4 of the 8 fixes directly in the current file contents (WR-01 identity guard in `NavRestructureBuilder.cs`, WR-02/IN-01 identity-based dashboard-tab resolution, WR-03 sentinel auto-derivation + IN-05 anchored lock-guard in `run-editor-builder.sh`, IN-04 fill-fade in `ChannelSwitcherView.cs`) — all confirmed present as described.

### Human Verification Required

The following 6 items are transcribed from `.planning/phases/06-channel-switcher-ui/06-HUMAN-UAT.md`, which the phase's own plans designed as the closing gate (status currently OPEN — `Result: PASS / ISSUES → ____________________` unfilled). They cannot be verified by static code/scene inspection.

#### 1. Pill placement + styling (SWITCH-01 surface)
**Test:** In the Unity Game view at 1080x2400, Play mode, look at the TopBar CenterZone.
**Expected:** The WhatsApp | Telegram pill matches the ModeToggle visual language (rounded track + rounded selected fill, header font, comparable height), sits centered between the bot-switcher identity and ModeToggle, vertical alignment reads clean.
**Why human:** Visual styling match and alignment offset are perceptual judgments; structural values (radius, track color, font GUID) are confirmed but "looks right" is not.

#### 2. Switch swaps the chat list — no crossing, no flicker (SWITCH-01)
**Test:** On a both-channels bot, tap Telegram then WhatsApp; re-tap the already-selected chip.
**Expected:** Full reset choreography (clear then reload), no crossed lists, no flicker; re-tap is a no-op.
**Why human:** Real-time async behavior under actual network/cache timing needs a live run.

#### 3. Unconnected channel = muted but tappable -> connect empty state (SWITCH-02)
**Test:** On a WhatsApp-only bot, inspect and tap the Telegram chip; repeat on a Telegram-only bot.
**Expected:** Muted chip reads clearly faded yet obviously tappable; tapping shows the connect empty-state CTA, not a blank screen.
**Why human:** Perceptual legibility of a 40% alpha fade and "looks tappable" cannot be judged from YAML/C#.

#### 4. Single-channel bot auto-selects its live channel (SWITCH-03)
**Test:** Open a Telegram-only bot fresh, then a WhatsApp-only bot fresh.
**Expected:** Each opens with its live channel already selected and the other chip muted, no manual tap needed.
**Why human:** End-to-end runtime flow through real bot data requires Play mode/device.

#### 5. Bottom tab bar: 4 tabs, tab 0 «Чаты» (SWITCH-04)
**Test:** Look at the bottom nav; tap Сводка, Bots, Profile in turn.
**Expected:** Exactly 4 tabs, tab 0 reads «Чаты»; each tap lands on the correct screen; no blank Telegram screen reachable.
**Why human:** Tap-to-screen routing end-to-end needs a live navigation pass, even though the index math is structurally confirmed.

#### 6. Last-used channel persists across restart (SWITCH-03)
**Test:** On a both-channels bot, select Telegram, then stop and re-enter Play mode (or relaunch on device).
**Expected:** The bot reopens on Telegram.
**Why human:** Cross-session PlayerPrefs persistence through an actual restart cannot be exercised statically.

### Gaps Summary

No structural gaps found. Every artifact declared in `06-01-PLAN.md` and `06-02-PLAN.md` frontmatter exists, is substantive, and is correctly wired — verified not just by name-presence grep but by chasing fileID cross-references through the committed `Main.unity` (ref-stamping, parent/child RectTransform ownership, and label text) to rule out the "crossed/duplicate ref" failure mode that name-only grep would miss. `BottomTabManager` constants match the committed 4-tab scene exactly. All `SwitchTab` call sites route through constants (zero hardcoded literals). The full EditMode suite is fresh (901/901 green, `Tools/test-output/headless-summary.json` generated at 16:30:05, i.e. *after* every one of the phase's commits including all 8 review-fix commits — the working tree has had zero code changes since, confirmed via `git log`/`git status`). All 8 `06-REVIEW.md` findings show fixed in the actual file contents, spot-checked directly.

The phase is **code-complete** but not yet **owner-confirmed**: `06-HUMAN-UAT.md` is a deliberate, planned escalation gate (matching the Phase 3/4 precedent already established in this project) covering exactly the things headless verification structurally cannot reach — visual styling match, flicker-free swap timing, muted-chip legibility, auto-select and cross-restart persistence behavior, and end-to-end tab routing. Per the gates taxonomy, this is the intended Escalation Gate for this phase, not a verification failure. Status is `human_needed`; the 6 items above are exactly `06-HUMAN-UAT.md`'s checklist. Once the owner runs that checklist and records PASS, this phase closes — a re-verification pass at that point only needs to confirm the Resume section was filled with a PASS result (no code should have changed).

---

_Verified: 2026-07-13T11:42:24Z_
_Verifier: Claude (gsd-verifier)_
