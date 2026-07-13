---
phase: 07-vmeste-suggestions-dashboard-on-telegram
plan: 02
subsystem: ui
tags: [dashboard, svodka, telegram, channel-aware, deep-link, unity, editmode-tests, pure-seam]

# Dependency graph
requires:
  - phase: 05-telegram-chat-pipeline
    provides: "ChatManager.SetActiveChannel / ActiveChannel seam (per-bot persisted channel, per-channel caches)"
  - phase: 06-channel-switcher-ui
    provides: "BottomTabManager.WhatsAppTabIndex now the «Чаты» tab (constant unchanged); Telegram tab removed"
  - phase: 04-n8n-telegram-template-parity-dev
    provides: "Dashboard_Outcomes keys sessions by identical profileId:chatId for Telegram — server needs ZERO changes"
provides:
  - "DashboardProfileMap pure seam: both-channel AuthedProfiles / ProfileToBot (→ (botName,channel)) / BotChips (one-per-bot set) / TryResolve local-map resolver"
  - "DashboardMetrics.FilterByProfiles(ISet) set-membership filter; FilterByProfile(string) delegates (back-compat)"
  - "«Сводка» counts/lists/filters/deep-links Telegram: DASH-01 (both-channel POST), DASH-02 (bot-level chips), DASH-03 (channel-aware deep-link)"
  - "8 new EditMode tests (6 DashboardProfileMap + 2 FilterByProfiles); full suite 916/916 green"
affects: [phase-08 prod replication, dashboard polish backlog (row channel badge / per-channel avatar+title)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Extract inline WhatsApp-only collection logic into a pure static seam (BotProfiles struct in, List/Dictionary out) fed by a thin impure adapter that snapshots live bots — mirrors SessionChatMap / DashboardTimeFormat"
    - "Channel resolved from WHICH local-map entry matched a server-returned profileId (never from the payload) — server value can't force off-device navigation (T-07-02-01)"
    - "Set-based bot filter (ISet<string>): a dual-channel bot is one chip whose set carries both profile ids; single-id overload delegates"

key-files:
  created:
    - Assets/Scripts/Main/Dashboard/DashboardProfileMap.cs
    - Assets/Tests/Editor/Chat/DashboardProfileMapTests.cs
  modified:
    - Assets/Scripts/Main/Dashboard/DashboardMetrics.cs
    - Assets/Scripts/Main/Dashboard/DashboardPage.cs
    - Assets/Tests/Editor/Chat/DashboardMetricsTests.cs

key-decisions:
  - "profileId → (botName, channel) is ONE dictionary keyed by the globally-unique GUID id (no collision), so channel falls out of the matched entry — no second map, no server channel field"
  - "One chip per bot with a HashSet<string> of that bot's authed id(s); chip 'on' = _botFilter.SetEquals(chip set); chips-row hidden on ≤1 BOT (not ≤1 profile)"
  - "OpenChat order is load-bearing: SetActiveBot (restores persisted channel) → SetActiveChannel(row channel) → SwitchTab(«Чаты») → deferred SelectChat; SetActiveChannel no-ops when unchanged so a WhatsApp row on a WA-active bot is byte-identical"
  - "SessionChatMap left untouched (now unused by DashboardPage) — a small pure-but-tested class is acceptable; production resolves via DashboardProfileMap.TryResolve"
  - "Telegram row cosmetics (raw-id name + silhouette when the chat isn't in the active WhatsApp lookup) are the CONTEXT §Row-cosmetics accepted v1 degradation — no channel badge / per-channel avatar this phase"

patterns-established:
  - "Genuine TDD RED for a brand-new Unity type via compile scaffold: real struct/API signatures with stub bodies + full tests → assembly compiles, new assertions fail (RED) → fill implementation (GREEN)"

requirements-completed: [DASH-01, DASH-02, DASH-03]

# Metrics
duration: 7min
completed: 2026-07-13
---

# Phase 7 Plan 02: «Сводка» Dashboard on Telegram Summary

**«Сводка» now counts, lists, filters, and deep-links Telegram conversations with ZERO server changes: a new pure `DashboardProfileMap` seam collects both channels' profile ids, builds one chip per bot (a dual-channel bot ⇒ a single set-valued chip), and resolves each outcome row's channel from the local map so a Telegram row lands on the Telegram chat via `SetActiveBot → SetActiveChannel → SwitchTab → SelectChat`.**

## Performance

- **Duration:** 7 min
- **Started:** 2026-07-13T12:37:29Z
- **Completed:** 2026-07-13T12:44:50Z
- **Tasks:** 2 (Task 1 as RED→GREEN)
- **Files modified:** 5 (2 runtime + 1 new runtime, 1 test extended + 1 new test)

## Accomplishments
- **DASH-01 — Telegram counts/lists:** `AuthedProfiles()` and `ProfileToBot()` now route through `DashboardProfileMap`, collecting both `whatsappProfileId` and `telegramProfileId` (WA-then-TG per bot, sentinel/empty skipped). The POSTed `profileIds` list is unchanged in shape but now carries Telegram ids, so Telegram sessions are classified and rendered.
- **DASH-02 — bot-level chips:** `BuildChips()` iterates `DashboardProfileMap.BotChips()` (one entry per bot, a `HashSet` of its 1–2 authed ids). `_botFilter` is now `ISet<string>`; `Render()`/`OpenStatusList()` filter via `DashboardMetrics.FilterByProfiles`. A dual-channel bot shows exactly ONE chip covering both profiles — never two same-named chips — and the chips row hides on ≤1 bot.
- **DASH-03 — channel-aware deep-link:** `OpenChat()` resolves `(botName, channel)` from the local map via `TryResolve` and runs `SetActiveBot → SetActiveChannel(channel) → SwitchTab(WhatsAppTabIndex) → deferred SelectChat` in that exact order. A WhatsApp row on a WA-active bot is byte-identical to before (`SetActiveChannel` no-ops when unchanged); an unknown/forged profileId early-returns (no navigation, no NRE).
- **Pure seam + set filter:** `DashboardProfileMap` (both-channel collect/map/chips + `TryResolve`) and `DashboardMetrics.FilterByProfiles(ISet)` (single-id `FilterByProfile` delegates for back-compat).
- **Coverage:** 8 new EditMode tests (both-channel collect, dual-channel single chip, TG-only, sentinel skip, per-matched-id channel resolution, TryResolve miss/null, FilterByProfiles set-subset + null/empty). Full suite **916/916 green** (908 baseline + 8).

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): failing DashboardProfileMap + FilterByProfiles tests** - `5ede19d` (test)
2. **Task 1 (GREEN): implement DashboardProfileMap seam + FilterByProfiles set filter** - `6000afb` (feat)
3. **Task 2: wire DashboardPage to both channels (DASH-01/02/03)** - `892ee7f` (feat)

**Plan metadata:** committed separately (this SUMMARY + STATE + ROADMAP + REQUIREMENTS).

## Files Created/Modified
- `Assets/Scripts/Main/Dashboard/DashboardProfileMap.cs` (new) - Pure static seam + `BotProfiles`/`DashboardProfileRef` structs. `AuthedProfiles` / `ProfileToBot` / `BotChips` / `TryResolve`, both channels, `Bot.UnauthedProfileSentinel`-guarded.
- `Assets/Scripts/Main/Dashboard/DashboardMetrics.cs` - Added `FilterByProfiles(IEnumerable, ISet<string>)` (null/empty ⇒ all; else set membership); `FilterByProfile(string)` now delegates.
- `Assets/Scripts/Main/Dashboard/DashboardPage.cs` - `BotDescriptors()` impure adapter; `AuthedProfiles`/`ProfileToBot` delegate to the seam; `_botFilter` → `ISet<string>`; bot-level `BuildChips`/`AddChip`/`SetBotFilter`; `Render`/`OpenStatusList` use `FilterByProfiles`; `SpawnRows` bot-count `showBotTag`; `BindRow` reads the struct; channel-aware `OpenChat`.
- `Assets/Tests/Editor/Chat/DashboardProfileMapTests.cs` (new) - 6 tests covering the seam.
- `Assets/Tests/Editor/Chat/DashboardMetricsTests.cs` - 2 `FilterByProfiles` set-semantics tests; existing `FilterByProfileNullReturnsAll` stays green (delegation preserved).

## Decisions Made
- **One dictionary, channel from the matched entry:** profile ids are globally-unique GUIDs, so a single `profileId → (botName, channel)` map has no collisions and the channel simply falls out of whichever id matched — no second map and no server-side channel field (server contract untouched).
- **Set-based filter for dual-channel correctness:** `_botFilter` holds a bot's profile-id set; `FilterByProfiles` uses set membership so a dual-channel bot's WhatsApp AND Telegram rows both surface under its single chip.
- **Load-bearing OpenChat order:** bot first (its persisted channel restore fires inside `SetActiveBot`), then explicit `SetActiveChannel` override, then tab, then deferred select.
- **SessionChatMap kept as-is:** it is now unused by production (resolution moved to `DashboardProfileMap.TryResolve`) but stays green per plan; a small pure-but-tested class is acceptable.

## Deviations from Plan

None - plan executed exactly as written.

_Note: Task 1 (`tdd="true"`) landed as a genuine RED→GREEN split rather than the sibling 07-01's single-commit signature migration: because this task introduces a **new** pure type + **new** behavior, a real RED (5 new assertions failing against compile-scaffold stubs, existing `FilterByProfile` coverage staying green) proves the tests are not vacuous. The plan-checker WARNING (BindRow `map` param → `Dictionary<string, DashboardProfileRef>`) and INFO (accepted cross-bot double-load; one-line comment at the `SetActiveChannel` call) were both applied as specified._

## TDD Gate Compliance
Plan `type: execute`, so plan-level RED/GREEN gate validation does not apply. Task 1 (`tdd="true"`) satisfied the cycle explicitly: `test(07-02)` RED commit (`5ede19d`, 5 new assertions failing) precedes the `feat(07-02)` GREEN commit (`6000afb`, scoped 15/15 green). No REFACTOR commit — the GREEN implementation was already clean.

## Issues Encountered
None. RED failed exactly as intended (5 new assertions), GREEN passed on first run (scoped 15/15), and the full suite (916/916) was green on first run — no auto-fixes required.

## Threat Surface
No new security surface beyond the plan's threat register.
- **T-07-02-01 (mitigate) — deep-link resolving a server profileId:** implemented as planned. `OpenChat` resolves only through the locally-built `DashboardProfileMap`; an unknown/forged id ⇒ `TryResolve` false ⇒ early-return; `channel` comes from the matched LOCAL entry, never the server payload. Locked by `TryResolve_MissOrNullReturnsFalse` + `ProfileToBot_ResolvesChannelPerMatchedId`.
- **T-07-02-02 (accept) — chatId to SelectChat:** unchanged soft-fail-to-list behavior.
- **T-07-02-03 (accept) — Telegram ids in the POST:** same owner-owned GUID data class as the WhatsApp ids, over the already-authenticated HTTPS webhook; server Prep also strips `-1`.

No new endpoints, auth paths, file access, or schema changes. `DashboardModels.cs` (wire shape), `Main.unity`, and `Tools/n8n` are byte-unchanged. No threat flags.

## Accepted v1 Limitation (not a blocking stub)
Telegram outcome rows fall back to the raw chat id for the display name and the deterministic colored silhouette for the avatar when the chat isn't in the active WhatsApp lookup (`ChatDisplayName` / `BindRow` avatar path unchanged). This is the CONTEXT §Row-cosmetics **accepted v1 degradation** — the dashboard's goal (count / list / filter / deep-link Telegram) is fully achieved; the row channel badge + per-channel avatar/title resolution are deferred to the polish backlog (CONTEXT §Deferred).

## User Setup Required
None - no external service configuration required by this plan. (The live TG dashboard proof rides the same owner-gated dev-n8n session as Phase 4/07-01; not a task here.)

## Next Phase Readiness
- The dashboard is Telegram-inclusive and unit-green; this closes the milestone's dashboard cut line (D2).
- Ready for Phase 8 prod replication (the Dashboard_Outcomes workflow needs no changes — only the client now sends Telegram ids).
- Open (carried): dashboard polish backlog (row channel badge / per-channel avatar+title); Phase 6 owner visual UAT (`06-HUMAN-UAT.md`); live TG grounding proof (`07-HUMAN-UAT.md`, owner-gated).

## Self-Check: PASSED

- All 6 key files verified present on disk (2 runtime created/modified, 1 runtime modified, 1 test created, 1 test extended, this SUMMARY).
- All 3 task commits verified in git history (`5ede19d` test RED, `6000afb` feat GREEN, `892ee7f` feat wiring).
- Full EditMode suite 916/916 green; all plan verification greps matched (`SetActiveChannel`, `FilterByProfiles`, `DashboardProfileMap`); `DashboardModels.cs` / `Main.unity` / `Tools/n8n` byte-unchanged; zero file deletions.

---
*Phase: 07-vmeste-suggestions-dashboard-on-telegram*
*Completed: 2026-07-13*
