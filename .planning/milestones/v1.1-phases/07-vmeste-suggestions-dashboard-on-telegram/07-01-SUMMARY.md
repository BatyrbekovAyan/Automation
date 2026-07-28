---
phase: 07-vmeste-suggestions-dashboard-on-telegram
plan: 01
subsystem: api
tags: [suggestions, vmeste, telegram, n8n, wire-contract, rag, unity, editmode-tests]

# Dependency graph
requires:
  - phase: 04-n8n-telegram-template-parity-dev
    provides: "Suggest_Replies channel-branched RAG (botWaId | botTgId) + channel-defaulting Prep (server half)"
  - phase: 05-telegram-chat-pipeline
    provides: "ChatManager.ActiveChannel seam (per-bot persisted channel identity)"
provides:
  - "Channel-aware «Вместе» suggestions payload: profileId channel-resolved, botTgId + channel added additively, botWaId always sent"
  - "Additive v1.1 wire contract on SuggestRepliesRequestDto (v1 keys byte-identical; strip channel+botTgId => exact v1)"
  - "Channel-selection matrix + additive-identity EditMode coverage (7 new tests, 908/908 green)"
  - "07-HUMAN-UAT.md: SUGG live-grounding proof recorded as owner-gated (rides Phase-4 TPL-06)"
affects: [07-02 dashboard telegram, phase-08 prod replication, server-side vmeste suppression]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Additive wire-contract extension: append ADD-only keys after the frozen set; prove byte-identity by JObject strip + DeepEquals against a hand-built v1 shape"
    - "Channel-resolved id selection inside a pure static builder (channel arg drives the pick; unit-tested matrix) — never a bare unconditional whatsappProfileId read"

key-files:
  created:
    - .planning/phases/07-vmeste-suggestions-dashboard-on-telegram/07-HUMAN-UAT.md
  modified:
    - Assets/Scripts/Chat/SuggestRepliesDtos.cs
    - Assets/Scripts/Chat/N8nSuggestionsProvider.cs
    - Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs

key-decisions:
  - "Kept the frozen v1 keys byte-identical and appended botTgId + channel after messages (ADD-only); server Prep defaults an absent channel to whatsapp"
  - "profileId is channel-RESOLVED in the pure builder (Telegram => telegramProfileId); botWaId stays whatsappWorkflowId ALWAYS for the server's default WA RAG branch"
  - "channel wire value derived ONLY from the ChatChannel enum as lowercase constants (T-07-01-01 mitigation), unit-locked by ChannelField_IsLowercaseEnumDerived"
  - "Kept the test Build helper's profileId/botWaId param names (mapped to the WA args) so the two named-arg call sites stay valid verbatim"

patterns-established:
  - "Additive-identity test: strip the new keys, DeepEquals a hand-built v1 JObject, assert exactly the 12 v1 keys remain"

requirements-completed: [SUGG-01, SUGG-02]

# Metrics
duration: 7min
completed: 2026-07-13
---

# Phase 7 Plan 01: Channel-Aware «Вместе» Suggestions Payload Summary

**«Вместе» suggestions now select the Telegram profile/workflow ids for a Telegram chat and send the additive v1.1 wire contract (`channel` + `botTgId`, `botWaId` still always sent) — proven byte-identical to v1 for WhatsApp by a strip-and-DeepEquals test.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-07-13T12:19:42Z
- **Completed:** 2026-07-13T12:27:24Z
- **Tasks:** 3
- **Files modified:** 3 (2 runtime + 1 test); 1 doc created

## Accomplishments
- `SuggestRepliesRequestDto` gained `botTgId` + `channel` as ADD-only keys after `messages`; the frozen v1 keys are untouched.
- `BuildPayloadJson` is now channel-aware and pure: `profileId` is channel-resolved (`telegramProfileId` on Telegram), `botWaId` == `whatsappWorkflowId` ALWAYS (server's default WA RAG branch / backward compat), `botTgId` == `telegramWorkflowId`, and `channel` is the lowercase enum-derived constant.
- `N8nSuggestionsProvider.Run()` reads `ChatManager.ActiveChannel` at request-build time and passes both channel id-pairs into the pure builder — Empty short-circuit, drain-gate, chat-mismatch guard, POST/Content-Type, and `requestSeq`/`MapResponse` semantics all UNCHANGED.
- 7 new EditMode tests lock the channel-selection matrix (WA chat / TG chat / TG-only bot), `botWaId` always-present, `botTgId` carriage, the `""`/`"-1"` sentinel passthrough, the lowercase-enum-derived `channel` (T-07-01-01 mitigation), and the additive-identity invariant. Full suite 908/908 green (901 baseline + 7).
- `07-HUMAN-UAT.md` records that the live TG RAG-grounding proof is owner-gated and rides the Phase-4 TPL-06 dev-n8n session.

## Task Commits

Each task was committed atomically:

1. **Task 1: Additive wire contract + channel-aware payload builder** - `5faa2c2` (feat)
2. **Task 2: Channel-selection matrix + additive-identity tests** - `25ad7fe` (test)
3. **Task 3: SUGG live-verification gate note (07-HUMAN-UAT.md) + full suite** - `a5d676e` (docs)

**Plan metadata:** committed separately (this SUMMARY + STATE + ROADMAP + REQUIREMENTS).

_Note: Task 1 is a coordinated signature migration (production + test helper move together to compile), so it landed as one `feat` commit rather than a RED/GREEN split; the existing 16 tests were the green regression guard. Task 2 added the new coverage as a `test` commit._

## Files Created/Modified
- `Assets/Scripts/Chat/SuggestRepliesDtos.cs` - Appended `botTgId` + `channel` to `SuggestRepliesRequestDto`; updated the class doc to note the v1.1 additive contract.
- `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` - `BuildPayloadJson` signature is now channel-aware (`ChatChannel` + both profile/workflow id pairs); channel-resolved `profileId`, always-`botWaId`, new `botTgId`/`channel`; `Run()` reads `cm.ActiveChannel` and passes the bot's raw channel fields.
- `Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs` - `Build` helper updated to the new signature (channel defaults WhatsApp; WA args keep the old param names); added 7 channel-matrix + additive-identity tests; imported `System.Linq`.
- `.planning/phases/07-vmeste-suggestions-dashboard-on-telegram/07-HUMAN-UAT.md` - SUGG live-verification gate note (owner-gated, rides TPL-06).

## Decisions Made
- **Additive over v2:** kept every frozen v1 key byte-identical and appended the two new keys — the server Prep already defaults an absent `channel` to whatsapp (Phase 4), so no coordinated cutover is needed. Executable proof is `WhatsAppRequest_AdditivelyIdenticalToV1`.
- **`botWaId` always sent:** even on Telegram, `botWaId` carries `whatsappWorkflowId` so the server's default RAG branch and any legacy consumer keep working; the Telegram RAG is scoped by the new `botTgId`.
- **Enum-derived `channel` only:** the wire value is one of two lowercase constants derived from `ChatChannel`, never a free-form/user string (threat T-07-01-01), and this is unit-locked.
- **Helper param names preserved:** kept `profileId`/`botWaId` on the test `Build` helper (mapped to the WA args) so the `profileId: "pid7"` and `botWaId: "-1"` named-arg call sites stay valid — no CS1739 (plan-checker advisory honored).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None. Both scoped runs (16 → 23) and the full suite (908) were green on first execution; no auto-fixes required.

## TDD Gate Compliance
Plan `type: execute` (not `type: tdd`), so plan-level RED/GREEN gate validation does not apply. Both tasks are marked `tdd="true"`: Task 1 was a signature migration that must move production + test-helper together to compile, so it committed as a single `feat` with the pre-existing 16 tests as the green guard (no artificial non-compiling RED); Task 2 committed the new coverage as a `test` commit. All 7 new assertions passed on first run against the Task-1 implementation.

## Threat Surface
No new security surface beyond the plan's threat register. The two added fields ride the already-authenticated HTTPS SuggestReplies webhook. T-07-01-01 (channel selecting the RAG key) is mitigated as planned — client emits only enum-derived lowercase constants, locked by `ChannelField_IsLowercaseEnumDerived`. No threat flags.

## User Setup Required
None - no external service configuration required by this plan. (The live end-to-end TG grounding proof is owner-gated and recorded in `07-HUMAN-UAT.md`; it rides the Phase-4 TPL-06 dev-n8n session and is not a task here.)

## Next Phase Readiness
- SUGG-01/SUGG-02 client half is code-complete and unit-green; the suggestions stack is now fully channel-agnostic above the payload seam.
- 07-02 (dashboard on Telegram) is unblocked — it depends on the same `ChatChannel`/`ActiveChannel` seam, not on this plan's payload changes.
- Open (owner-gated, tracked): live TG RAG-grounding proof on `07-HUMAN-UAT.md` (rides TPL-06); prod bagkz replication of the Suggest_Replies channel branch remains a Phase-8 checklist item.

## Self-Check: PASSED

- All 5 key files verified present on disk (2 runtime, 1 test, 1 UAT doc, this SUMMARY).
- All 3 task commits verified in git history (`5faa2c2` feat, `25ad7fe` test, `a5d676e` docs).
- Full EditMode suite 908/908 green; plan verification greps all matched; `Main.unity` unchanged.

---
*Phase: 07-vmeste-suggestions-dashboard-on-telegram*
*Completed: 2026-07-13*
