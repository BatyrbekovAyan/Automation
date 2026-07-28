---
phase: 08-device-uat-milestone-closeout
plan: 34
subsystem: ui
tags: [reactions, telegram, whatsapp, tapi, merge, reconcile, d2-view, d15, tdd, jsonutility]

# Dependency graph
requires:
  - phase: 08 (08-30)
    provides: confirmation-clears-grace Merge + [D2-merge] Editor log + WR-01 tombstone drop-on-confirmed-absence
  - phase: 08 (08-31)
    provides: Editor-only secret-free [D15-probe] inside the messages/id/get drain (QuoteResolve.cs)
  - phase: 08 (08-33)
    provides: round-6 verdicts — D2-view residual pinned EXACT (differ-during-grace), D15 probe never fired
provides:
  - "MessageReaction.displacedEmoji — a JsonUtility-serializable field carrying the owner's pre-tap emoji ON the optimistic entry (survives the ChatHistoryCache disk round-trip a memory map would lose)"
  - "TelegramReactionMerge displaced discrimination (CR-01a): a differing server 'me' is suppressed ONLY when it equals the displaced pre-tap emoji; any THIRD value is a genuine external own-change and is adopted (freshness consumed) — the round-6 🥺→🔥 age=9s defect is closed"
  - "TelegramReactionMerge.Reconcile(cached, server, now, out renderChanged) seam (CR-02): RefreshCachedMessageReactions ALWAYS adopts the reconciled list; renderChanged gates the event only — so the confirm/fold freshness-consumption lands through all three live-poll call sites"
  - "WR-01 revert pin: a null-displaced fresh optimistic 'me' (first-ever reaction or failed-POST revert) adopts any differing echo instead of being pinned for the window"
  - "D15 (IN-01) deterministic Editor-only [D15-probe] one-shot on the first WhatsApp type:reaction raw — enqueues raw.stanzaId into the existing authed messages/id/get drain (no new secret/endpoint)"
affects: [08-35 owner re-verify, Gate A milestone v1.1 close]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Optimistic-reconcile discrimination by a DISPLACED prior value carried ON the persisted entry (not a memory-side map) — freshness + displaced both survive relaunch via JsonUtility"
    - "A pure Reconcile seam wrapping Merge with an out-param render gate, so the always-adopt vs repaint decision is EditMode-testable through the exact semantics the call site uses"

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-34-SUMMARY.md
  modified:
    - Assets/Scripts/Chat/MessageReaction.cs
    - Assets/Scripts/Chat/TelegramReactionMerge.cs
    - Assets/Scripts/Main/ChatManager.ReactionSend.cs
    - Assets/Scripts/Main/ChatManager.cs
    - Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs
    - Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs

key-decisions:
  - "Displaced value rides ON the MessageReaction entry (JsonUtility field), NOT a ChatManager map — a map vanishes on relaunch while the freshness time survives, resurrecting the bug (08-REVIEW CR-01 candidate a)"
  - "CR-01a and CR-02 shipped together in one plan — either alone leaves the defect live (CR-01's confirm/fold consumption never lands through the call sites without CR-02's always-adopt)"
  - "displacedEmoji null (first-ever reaction or a failed-POST revert entry) degrades to adopt-on-differ — SameEmoji(serverEmoji, null) is false, so a null-displaced fresh me self-corrects (WR-01)"
  - "D15 probe reuses the existing authed messages/id/get seam (no new endpoint/secret); Editor-only #if UNITY_EDITOR, one-shot per session; WhatsApp/player builds byte-identical"

patterns-established:
  - "Pattern: when a reconcile grace cannot rely on a confirming intermediate echo (current-state-only transport), discriminate the stale echo from a genuine change by the DISPLACED prior value carried on the optimistic entry, not by waiting for a same-value echo or a timer"

requirements-completed: []

# Metrics
duration: 11min
completed: 2026-07-21
---

# Phase 8 Plan 34: D2-View Displaced-Emoji Discrimination + Reconcile Seam + D15 Probe Summary

**TelegramReactionMerge now suppresses a differing server echo ONLY when it matches the displaced pre-tap emoji (adopting any third value), RefreshCachedMessageReactions always adopts the Reconcile seam so the freshness-consumption finally lands through the live-poll call sites (CR-02), and the [D15-probe] fires deterministically on the first WhatsApp reaction raw — closing the milestone v1.1 #1 defect open across six device rounds.**

## Performance

- **Duration:** 11 min
- **Started:** 2026-07-21T09:31:13Z
- **Completed:** 2026-07-21T09:42:41Z
- **Tasks:** 3 (2 TDD RED→GREEN + 1 Editor-only glue)
- **Files modified:** 6

## Accomplishments

- **CR-01a — displaced-emoji discrimination (the D2-view root fix).** `MessageReaction` gains a JsonUtility-serializable `displacedEmoji` field; `ReactionSend` stamps the pre-tap `priorEmoji` on the optimistic add/change entry (`StampDisplaced`) and on the removal tombstone (`StampRemovalTombstone(..., priorEmoji)`). `Merge` now suppresses a differing server "me" ONLY when it equals `displacedEmoji` (the genuine stale echo); any THIRD value (neither the optimistic nor the displaced emoji) is a genuinely newer external own-change and is adopted at once (server element `time=0` ⇒ freshness consumed). The removal tombstone is carried only while the server still echoes the removed emoji; a differing "me" echo drops it so an external re-add applies. This closes the exact round-6 capture (`🥺→🔥` at age=9s, which was suppressed for the full 90s grace).
- **CR-02 — the Reconcile seam.** New pure `Reconcile(cached, server, now, out renderChanged)` wraps `Merge`; `RefreshCachedMessageReactions` now `cached.reactions = merged` UNCONDITIONALLY and gates the event/repaint on `renderChanged`. This makes the confirm-adopt (`time=0`) and un-mapped-echo fold (re-key to "me") — which change only `time`/identity, not the `(reactorKey, emoji)` multiset — actually land through all three live-poll call sites (753/1231/1331). The old `SameReactions(cached.reactions, merged)` discard-guard, which had silently made the shipped 08-30 fix dead code, is gone.
- **WR-01 pin.** A null-displaced fresh optimistic "me" (a first-ever reaction, or a failed-POST revert entry that `ReactionStore.ApplyToMessage` creates without setting displaced) adopts any differing echo — a ghost-landed sent emoji or a mid-flight external change self-corrects instead of being pinned. Pinned by `Merge_RevertShapedFreshMe_NullDisplaced_DifferingEchoAdopts`.
- **D15 (IN-01) — deterministic probe trigger.** An Editor-only one-shot `_d15ProbeArmed` arms on the first WhatsApp `type:"reaction"` raw of the session and enqueues `raw.stanzaId` into the existing serial quote-resolve drain, so the `[D15-probe]` reaction-state key report (`reactionsKey`/`reactionKey` booleans) fires through the already-authed `messages/id/get` seam without owner choreography — no new request, no token handling.
- **Full EditMode suite green at 1191/1191** (fresh baseline 1184 + 7: Task 1 +6, Task 2 +1, Task 3 +0). WhatsApp byte-identical: Merge/Reconcile are Telegram-only, the new field is set only on the Telegram path, and the D15 probe is `#if UNITY_EDITOR` + WhatsApp-only.

## Task Commits

Each task committed atomically (TDD tasks = RED then GREEN):

1. **Task 1: CR-01a displaced discrimination + WR-01 pin** — `5ac43d5` (test/RED) → `6aff076` (feat/GREEN)
2. **Task 2: CR-02 Reconcile seam** — `1de1252` (test/RED) → `d2576e7` (feat/GREEN)
3. **Task 3: D15 deterministic probe trigger** — `d939c19` (feat)

**Plan metadata:** (docs commit — SUMMARY/STATE/ROADMAP)

## Files Created/Modified

- `Assets/Scripts/Chat/MessageReaction.cs` — added the JsonUtility-serializable `displacedEmoji` field (Telegram-only; the pre-tap state the optimistic entry replaced).
- `Assets/Scripts/Chat/TelegramReactionMerge.cs` — displaced-discriminated differ+tombstone suppression; new `StampDisplaced`; `StampRemovalTombstone` gains a `displacedEmoji` arg; new pure `Reconcile(...)` seam; class-summary doc rewritten.
- `Assets/Scripts/Main/ChatManager.ReactionSend.cs` — pass `priorEmoji` to the removal tombstone; stamp displaced on the Telegram add/change path.
- `Assets/Scripts/Main/ChatManager.cs` — `RefreshCachedMessageReactions` adopts `Reconcile`'s list unconditionally with `renderChanged` gating the event; Editor-only `_d15ProbeArmed` one-shot + probe insertion after the existing `[D15]` log.
- `Assets/Tests/Editor/Chat/TelegramReactionMergeTests.cs` — Me/Removal helpers gain a `displaced` param; 6 new CR-01a behavioral tests + the CR-02 through-the-seam test; existing tombstone/change tests + both `StampRemovalTombstone_*` updated to carry/assert displaced.
- `Assets/Tests/Editor/Chat/TelegramReactionReceiveTests.cs` — local `Me()` helper gains `displaced`; `Merge_FreshEmojiChange_BeatsStaleServerEcho` fixture carries the pre-tap 👍 in place (assertions unchanged; stays green under both RED and GREEN; count flat).

## Decisions Made

See key-decisions frontmatter. In short: the displaced value rides on the persisted `MessageReaction` entry (not a memory map that would vanish on relaunch); CR-01a and CR-02 ship together because either alone leaves the defect live; a null displaced degrades safely to adopt-on-differ; the D15 probe reuses the existing authed seam with zero new secrets.

## Deviations from Plan

None - plan executed exactly as written. All three tasks followed the read_first/action/acceptance_criteria verbatim; every grep acceptance count matched on the first check; the TDD RED gates failed for exactly the traced reasons (Task 1: 4 behavioral tests on unconditional suppress; Task 2: the seam test at the step1 time assertion) and GREEN closed them.

## Known Stubs

None. The `displacedEmoji` field is fully wired end-to-end (stamped by ReactionSend on both the add/change and removal paths, read by Merge's two gates). No placeholder or empty-value flows to the UI.

## Threat Flags

None. No new network endpoint, secret, auth path, or scene mutation. `MessageReaction` gains an inert `displacedEmoji` field that JsonUtility serializes as `""` on both channels but is only ever SET on the Telegram path; WhatsApp reactions flow through `ReactionStore` and never reach Merge/Reconcile. The D15 probe reuses the existing authed `messages/id/get` seam (Authorization already set from `Manager.wappiAuthToken`); the executor never handled a token. All mitigations in the plan's threat register (T-08-34-01 displaced discrimination + CR-02 landing) are implemented and pinned by tests.

## Issues Encountered

None. The Unity Editor was reported OPEN in the plan but was actually closed (no lockfile, no Temp/claude bridge dir, only Unity Hub running) — so verification ran via the sanctioned headless runner `Tools/run-tests-headless.sh` (the Editor-CLOSED counterpart to the in-Editor bridge), which cold-launches Unity in batch mode and parses the NUnit3 result. RED and GREEN gates were confirmed on each run; the `-testFilter TelegramReactionMerge` fast pass was used for the per-task RED/GREEN checks and the full suite for the sibling-fixture confirmation (the filter does not match `TelegramReactionReceiveTests`).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **08-35 owner re-verify** is next (checkpoint:human-verify, wave 18): D2-view rapid-change repro + stale-echo sanity (Editor Play-Mode sufficient) + ONE Android build for the Gate A invariants sweep; the deterministic `[D15-probe]` both-false finalizes the CLAUDE.md platform-limit note. On all-PASS, Gate A → PASS + re-aggregates I.3 #10 + unblocks Gates B/C.
- The `[D2-merge]` / `[D15-probe]` / `[D15]` Editor diagnostics stay armed for the 08-35 pass; all are tagged for removal at phase close (08-REVIEW IN-02/IN-03).
- Documented residual (accepted v1): an external own-change BACK TO the displaced emoji within the window is indistinguishable from the stale echo and stays suppressed until a confirming echo of the optimistic emoji or window expiry.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-34-SUMMARY.md`
- FOUND commit `5ac43d5` (Task 1 RED), `6aff076` (Task 1 GREEN)
- FOUND commit `1de1252` (Task 2 RED), `d2576e7` (Task 2 GREEN)
- FOUND commit `d939c19` (Task 3)
- Full EditMode suite green 1191/1191 (baseline 1184 + 7); all grep acceptance criteria across all three tasks passed; six named files modified, no deletions, no unrelated files staged.

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-21*
