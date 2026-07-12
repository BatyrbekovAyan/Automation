---
phase: 05-channel-aware-chatmanager-core
plan: 01
subsystem: api
tags: [unity, csharp, wappi, telegram, chat-pipeline, url-builder, playerprefs, tdd]

# Dependency graph
requires:
  - phase: 04-n8n-telegram-template-parity
    provides: Telegram workflows on tapi (server side) — this phase makes the client channel-aware
provides:
  - "ChatChannel enum (WhatsApp=0, Telegram=1) — persisted-ordinal contract"
  - "WappiEndpoints.Sync(channel, path) URL builder replacing hardcoded api/tapi bases"
  - "ChatIdFormat pure helpers: Recipient / DisplayFallback / IsGroup (retires chat.id[..^5] crash)"
  - "WappiMediaRequestFactory channel-aware EndpointFor overload (+ WhatsApp back-compat)"
  - "ChannelTabState enum + ChannelTabStateResolver (channel-neutral tab-state core)"
  - "OutboxEntry.channel field (int; legacy JSON => 0 = WhatsApp)"
affects: [05-02, 05-03, 05-04, 05-05, 05-06, phase-6-channel-switcher]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure static seam classes (WappiEndpoints, ChatIdFormat) — unit-testable, no I/O"
    - "Interface-first / contracts-first: primitives shipped before call-site rewiring"
    - "Back-compat overload delegation (2-arg EndpointFor => 3-arg WhatsApp; WhatsApp resolver => channel-neutral core)"
    - "Append-only [Serializable] field growth (OutboxEntry.channel; JsonUtility fills missing as 0)"

key-files:
  created:
    - Assets/Scripts/Chat/ChatChannel.cs
    - Assets/Scripts/Chat/WappiEndpoints.cs
    - Assets/Scripts/Chat/ChatIdFormat.cs
    - Assets/Tests/Editor/Chat/WappiEndpointsTests.cs
    - Assets/Tests/Editor/Chat/ChatIdFormatTests.cs
    - Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs
    - Assets/Tests/Editor/Chat/OutboxEntryChannelTests.cs
  modified:
    - Assets/Scripts/Chat/WappiRecipient.cs
    - Assets/Scripts/Chat/WappiMediaRequestFactory.cs
    - Assets/Scripts/Main/WhatsAppTabState.cs
    - Assets/Scripts/Chat/OutboxStore.cs
    - Assets/Tests/Editor/Chat/WappiMediaRequestFactoryTests.cs

key-decisions:
  - "Kept WhatsAppTabState enum + WhatsAppTabStateResolver as a mapping wrapper over the new channel-neutral ChannelTabStateResolver (NoConnection => NoWhatsApp) — zero churn to existing resolver tests/call sites."
  - "Retired WappiMediaRequestFactory.Base const (now fully unused; 3-arg EndpointFor routes through WappiEndpoints.Sync). The 11 ChatManager-pipeline URL literals are untouched (rewired in 05-03/05-04)."
  - "DisplayFallback strips only a present 5-char @c.us/@g.us suffix via Substring(0, len-5); numeric/short/empty ids returned verbatim / '' — never sliced, never throws."

patterns-established:
  - "Pattern 1: Channel seam primitives are pure static classes with both-channel EditMode coverage before any call-site is rewired."
  - "Pattern 2: Public wrappers (WappiRecipient.FromChatId, WappiMediaRequestFactory.NormalizeRecipient) delegate to a single home (ChatIdFormat.Recipient) so existing call sites/tests don't churn."

requirements-completed: [CHAT-01, CHAT-02, CHAT-04, CHAT-05, CHAT-08]

# Metrics
duration: 35min
completed: 2026-07-12
---

# Phase 5 Plan 01: Channel-Aware Seam Primitives Summary

**Pure, unit-tested channel seam: ChatChannel enum + WappiEndpoints api/tapi URL builder + ChatIdFormat (retires the chat.id[..^5] crash) + channel-neutral tab-state resolver + OutboxEntry.channel — zero WhatsApp runtime change, full EditMode suite 827/827 green.**

## Performance

- **Duration:** 35 min
- **Started:** 2026-07-12T15:03:48Z
- **Completed:** 2026-07-12T15:39:39Z
- **Tasks:** 3 (all TDD: RED → GREEN)
- **Files modified:** 12 (7 created, 5 modified)

## Accomplishments
- `ChatChannel` enum with load-bearing ordinals (WhatsApp=0, Telegram=1) for persistence.
- `WappiEndpoints.Sync(channel, pathAndQuery)` — single home for `https://wappi.pro/{api|tapi}/sync/{path}`, ready to replace the 11 hardcoded literals in 05-03/05-04.
- `ChatIdFormat` pure helper: `Recipient` (suffix-conditional @c.us strip), `DisplayFallback` (never slices numeric/short ids, never throws — retires the `chat.id[..^5]` ArgumentOutOfRange crash, T-0501-01), `IsGroup` (suffix-only + full overloads: WA @g.us/isGroup, TG type=="chat").
- `WappiRecipient` + `WappiMediaRequestFactory` now delegate recipient stripping to `ChatIdFormat.Recipient`; media factory gained a channel-aware `EndpointFor(kind, profileId, ChatChannel)` routing through `WappiEndpoints.Sync`, with a WhatsApp back-compat 2-arg overload keeping URLs byte-identical.
- Tab-state resolver generalized: new `ChannelTabState` enum + `ChannelTabStateResolver` core; `WhatsAppTabStateResolver` delegates and maps (NoConnection => NoWhatsApp) — existing resolver tests unchanged.
- `OutboxEntry.channel` field appended (append-only; legacy JSON deserializes to 0 = WhatsApp).

## Task Commits

Each task was executed TDD (RED test commit → GREEN feat commit):

1. **Task 1: ChatChannel enum + WappiEndpoints URL builder**
   - `f89ae5e` (test) — failing WappiEndpoints tests + enum + stub
   - `446667f` (feat) — implement `WappiEndpoints.Sync`; 9/9 green
2. **Task 2: ChatIdFormat helper + delegate WappiRecipient / WappiMediaRequestFactory**
   - `8f99374` (test) — failing ChatIdFormat tests + throwing stub
   - `27d7a12` (feat) — implement ChatIdFormat, delegate recipients, channel-aware media factory; 35/35 green
3. **Task 3: Channel-parameterized tab-state resolver + OutboxEntry.channel**
   - `df8c8bc` (test) — failing resolver + channel-field tests (compile-error RED)
   - `a5da0a7` (feat) — ChannelTabState/Resolver + OutboxEntry.channel; full suite 827/827 green

_No REFACTOR commits needed — GREEN implementations were already clean._

## Files Created/Modified
- `Assets/Scripts/Chat/ChatChannel.cs` (created) — plain enum, no MonoBehaviour, no namespace.
- `Assets/Scripts/Chat/WappiEndpoints.cs` (created) — channel-aware sync URL builder.
- `Assets/Scripts/Chat/ChatIdFormat.cs` (created) — Recipient / DisplayFallback / IsGroup pure helpers.
- `Assets/Scripts/Chat/WappiRecipient.cs` (modified) — FromChatId delegates to ChatIdFormat.Recipient.
- `Assets/Scripts/Chat/WappiMediaRequestFactory.cs` (modified) — 3-arg channel-aware EndpointFor + 2-arg back-compat; NormalizeRecipient delegates; Base const retired.
- `Assets/Scripts/Main/WhatsAppTabState.cs` (modified) — ChannelTabState enum + ChannelTabStateResolver; WhatsApp resolver now a mapping wrapper.
- `Assets/Scripts/Chat/OutboxStore.cs` (modified) — OutboxEntry.channel field (int, default 0).
- `Assets/Tests/Editor/Chat/WappiEndpointsTests.cs` (created) — both channels × representative paths + ordinals.
- `Assets/Tests/Editor/Chat/ChatIdFormatTests.cs` (created) — 20 cases (short/numeric/@g.us/@c.us/empty/null/TG type).
- `Assets/Tests/Editor/Chat/ChannelTabStateResolverTests.cs` (created) — channel-neutral core + WhatsApp mapping.
- `Assets/Tests/Editor/Chat/OutboxEntryChannelTests.cs` (created) — channel round-trip + legacy-missing-key => 0.
- `Assets/Tests/Editor/Chat/WappiMediaRequestFactoryTests.cs` (modified) — added 3-arg Telegram tapi assertions + 2-arg==3-arg-WhatsApp equality; no existing assertions removed.

## Decisions Made
- **Resolver = mapping wrapper, not rename.** Rather than renaming `WhatsAppTabState`/`WhatsAppTabStateResolver` (which would churn call sites + tests), the existing WhatsApp resolver delegates to the new channel-neutral `ChannelTabStateResolver` and maps its result. All 4 original `WhatsAppTabStateResolverTests` stay green untouched.
- **Base const retired.** `WappiMediaRequestFactory.Base` became fully unused once the 3-arg `EndpointFor` routes through `WappiEndpoints.Sync`; removed per the plan's "retire only if fully unused" clause. This is NOT one of the 11 pipeline URL literals.
- **Compile-error RED for Task 3.** OutboxEntry.channel is a plain data field — the only honest RED is a test that references the not-yet-existing field (compile failure), since adding the field makes the round-trip pass immediately. Standard C# first-RED; the following GREEN commit fixes compilation.

## Deviations from Plan

None - plan executed exactly as written. No Rule 1-4 deviations were required; all three tasks landed on-contract.

## Issues Encountered

- **Literal-count reconciliation (not a defect).** A whole-`Assets/Scripts` grep of `wappi.pro/api/sync` reads 29 (baseline was 28). This reconciles exactly: −1 from retiring `WappiMediaRequestFactory.Base`, +2 from two `<c>wappi.pro/api/sync…</c>` mentions inside `WappiEndpoints.cs` XML doc comments (documentation, not runtime URL literals). The **11 pipeline call-site literals are verified intact**: ChatManager.cs (7), ChatManager.DeleteChat.cs (1), ChatManager.ReactionSend.cs (1), ChatManager.ReactionResolve.cs (1), ChatManager.QuoteResolve.cs (1). They are rewired in 05-03/05-04, not here.

## Threat Register Coverage
- **T-0501-01 (DoS, ChatIdFormat.DisplayFallback):** mitigated — `DisplayFallback` never slices unless a known 5-char suffix is present; empty/short/null return verbatim/"". Covered by `ChatIdFormatTests` short/numeric/empty cases + an explicit `DoesNotThrow` assertion.
- **T-0501-02 (Tampering, OutboxEntry.channel):** accept — legacy JSON => 0 (WhatsApp); `OutboxEntryChannelTests` asserts the safe default.
- **T-0501-03 (Spoofing, WappiEndpoints.Sync):** mitigated — single URL-building home per channel; both-channel `WappiEndpointsTests` prevent a channel/base mismatch once call sites are wired downstream.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Contracts for 05-02 (seam) / 05-03 (URL + parser) / 05-04 (send path) are in place: `WappiEndpoints.Sync`, `ChatIdFormat.*`, `WappiMediaRequestFactory.EndpointFor(..., ChatChannel)`, `ChannelTabStateResolver`, and `OutboxEntry.channel`.
- WhatsApp regression net intact (WappiRecipientTests, WappiMediaRequestFactoryTests, WhatsAppTabStateResolverTests, OutboxEntry*Tests all green); zero WhatsApp runtime behavior change.
- Full EditMode suite: **827/827 green** via `Tools/run-tests-headless.sh` (Editor closed).

## Self-Check: PASSED

- All 7 created source/test files present on disk + SUMMARY.md.
- All 6 task commits (3× RED test + 3× GREEN feat) present in git history.

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-12*
