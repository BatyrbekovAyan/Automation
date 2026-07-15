---
phase: 05-channel-aware-chatmanager-core
plan: 10
subsystem: ui
tags: [telegram, channel, theming, accent-color, chat-list, empty-state, unread-badge]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: ChatManager.ActiveChannel (per-bot WhatsApp/Telegram identity, 05-02)
  - phase: 06-channel-switcher-ui
    provides: ChannelSwitcherView.TgSelectedFill #2AABEE brand-blue precedent + Hex helper
provides:
  - "ChannelAccent.Resolve(channel, whatsappAuthored) — pure channel→accent-color seam (TG ⇒ #2AABEE, WA ⇒ passthrough byte-identical, authored alpha preserved)"
  - "Telegram-blue recolor of the chat-row unread pill + unread time tint (ChatItemView)"
  - "Telegram-blue recolor of the empty-state connect/create CTA + placeholder icon (EmptyStateView), all three reasons"
affects: [device-uat, milestone-closeout, any future channel-branded accent]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure channel→color seam mirroring ChannelResolver/ChannelSwitcherModel (EditMode-unit-testable, no MonoBehaviour)"
    - "Cache the authored (WhatsApp) color at runtime, recolor FROM it via the seam — never hardcode a scene green; guarantees exact WhatsApp render + reversible pooled-row reuse"

key-files:
  created:
    - Assets/Scripts/Chat/ChannelAccent.cs
    - Assets/Tests/Editor/Chat/ChannelAccentTests.cs
  modified:
    - Assets/Scripts/UI/ChatItemView.cs
    - Assets/Scripts/UI/EmptyStateView.cs

key-decisions:
  - "Recolor is 'accents only' (owner-confirmed): unread badge/time + empty-state CTA/icon. Bubbles, Авто/Вместе toggle, and channel/bot switcher chips deliberately untouched."
  - "Alpha preserved on the Telegram branch (only the hue shifts) so a semi-transparent accent stays semi-transparent; WhatsApp branch returns the caller's color byte-identical."
  - "EmptyStateView placeholder icon (#25D366) recolored alongside the CTA for a coherent blue empty state on Telegram — it is a green accent explicitly invited by scope ('an icon tint … if one is green'). IconCircle light tint (#DFF3EA) + text labels left as authored (not accents)."

patterns-established:
  - "Channel-branded accent = ChannelAccent.Resolve(ChatManager.Instance.ActiveChannel, cachedAuthoredColor), null-guarded on ChatManager for Editor/tests"

requirements-completed: []

# Metrics
duration: ~18 min
completed: 2026-07-15
---

# Phase 5 Plan 10: Channel-Aware Accent Theming Summary

**On the Telegram channel the WhatsApp-green accents (chat-row unread pill + time tint, empty-state connect CTA + icon) recolor to Telegram brand blue #2AABEE via a pure `ChannelAccent.Resolve` seam — WhatsApp stays byte-identical.**

## Performance

- **Duration:** ~18 min
- **Started:** 2026-07-15T10:40:00Z (approx — context read + implementation)
- **Completed:** 2026-07-15T10:57:52Z
- **Tasks:** 1 (atomic feature commit)
- **Files modified:** 4 source (.cs) + 2 generated .meta

## Accomplishments

- **Pure seam `ChannelAccent.Resolve(ChatChannel, Color)`** — Telegram ⇒ brand blue `#2AABEE` (carrying the caller's authored alpha); every other channel ⇒ the caller's authored color returned unchanged. Side-effect-free, input-only, EditMode-unit-testable alongside `ChannelResolver` / `ChannelSwitcherModel`. Blue kept identical to `ChannelSwitcherView.TgSelectedFill` / `Manager.TelegramBrandColor` so all Telegram accents read as one brand.
- **ChatItemView** — `ApplyUnreadBadge` now routes both the unread pill fill and the unread time tint through the seam. The pill Image's authored green is cached once (lazily, before any write), so the recolor never hardcodes a scene green and a pooled row that went blue on a Telegram bind reverts to the exact authored green on a WhatsApp rebind.
- **EmptyStateView** — the connect/create CTA fill and the placeholder icon tint recolor through the seam at the tail of `ConfigureForReason` (runs on every OnEnable/OnEmptyState, incl. after a channel switch). Authored greens cached at Awake. Applies across all three reasons (NoBotsExist / BotHasNoWhatsApp / BotHasNoTelegram) — coherent because `BotHasNoTelegram` only surfaces on the Telegram channel and `BotHasNoWhatsApp` only on WhatsApp.
- **7 new `ChannelAccentTests`** — TG⇒blue (both authored greens), WA⇒passthrough byte-identical (named color + arbitrary color), alpha preserved on both branches (opaque + semi-transparent), and brand parity with the switcher's `#2AABEE`.

## Task Commits

1. **ChannelAccent seam + tests + ChatItemView & EmptyStateView wiring** — `861ddfe` (feat)

**Plan metadata:** (this SUMMARY + STATE.md + 05-HUMAN-UAT.md) — see final docs commit.

## Files Created/Modified

- `Assets/Scripts/Chat/ChannelAccent.cs` — pure channel→accent-color seam (`Resolve` + `TelegramBlue`).
- `Assets/Tests/Editor/Chat/ChannelAccentTests.cs` — 7 pure NUnit tests for the seam.
- `Assets/Scripts/UI/ChatItemView.cs` — unread pill + time tint recolor; authored-green cache (`CacheUnreadBadgeColor`).
- `Assets/Scripts/UI/EmptyStateView.cs` — CTA + icon recolor (`ApplyChannelAccent`), authored greens cached at Awake (`CacheAccentColors`).

## Decisions Made

- **Scope held to "accents only"** (owner-confirmed): recolored the unread badge/time and the empty-state CTA + icon. Deliberately did NOT touch `MessageItemView.outgoingColor` (bubbles), `ReplyModeToggleBinder` greens (mode, not channel), `ChannelSwitcherView`, or `BotSwitcherRowView` (already brand-correct).
- **Cache-and-resolve instead of hardcoding** the scene green — the seam maps FROM each caller's cached authored color, so WhatsApp renders exactly as authored and a reused (pooled) row reverts correctly on a WhatsApp rebind.
- **Alpha preserved on the Telegram branch** — only the hue shifts; a semi-transparent authored accent stays semi-transparent.
- **Empty-state icon included in the recolor.** The placeholder icon is an authored `#25D366` green accent (builder: `iconImage.color = Brand`). Scope explicitly invited "an icon tint … if one is green", and leaving it green next to a blue CTA on Telegram would be incoherent. It is one null-guarded line and trivially reversible if the owner prefers the CTA-only treatment. The `IconCircle` near-white tint (`#DFF3EA`) and the text labels are left as authored — they are tint/ink, not "the accent", and routing a near-white tint through the seam would wrongly saturate it to full blue.

## Deviations from Plan

None - plan executed exactly as written. (The empty-state icon recolor is within the plan's stated scope — "any green accent it drives — e.g. an icon tint … if one is green" — not a deviation; documented above as a scope decision.)

## Issues Encountered

- **Environment changed since the plan was written:** the plan noted a STALE `Temp/UnityLockfile` with NO real Unity process. On execution a REAL Unity Editor (PID 5583) WAS open with the project. Per the plan's own contingency ("If a real Editor is open, use the sanctioned in-Editor bridge"), I used the bridge (`Temp/claude/run-tests.trigger` → `test-summary.json`) and did NOT delete the lockfile or run headless. The Editor auto-refreshed on the new files and recompiled; the run executed against fresh assemblies (`editorAssemblyWrittenUtc 2026-07-15T10:55:56Z`, postdating my last edit).

## Verification

- **Test path:** sanctioned in-Editor bridge (real Editor open). Full EditMode suite.
- **Result:** `1036/1036 passed, 0 failed, 0 skipped` (baseline 1029 + 7 new `ChannelAccentTests`).
- **Staleness gate:** `editorAssemblyWrittenUtc = 2026-07-15T10:55:56Z` — postdates the last source edit (armed 10:55:36Z), and the live DLL mtime matches, so the green reflects current code.
- **WhatsApp byte-identical:** the entire pre-existing 1029-test suite stayed green with the WhatsApp branch returning the cached authored color (no WA regression).

## Next Phase Readiness

- Code-correct + suite-green + no WhatsApp regression. Pixel-perfect blue is an owner **device re-verify** (Phase 8) — a device-reverify line was appended to `05-HUMAN-UAT.md` (unread badge + empty-state accent → blue on the Telegram channel).
- No scene/prefab edits (all colors applied at runtime) — nothing to import or migrate.

## Self-Check: PASSED

- Created files exist on disk: `ChannelAccent.cs`, `ChannelAccentTests.cs`, `05-10-SUMMARY.md` — all FOUND.
- Modified files present: `ChatItemView.cs`, `EmptyStateView.cs` — both FOUND, both reference `ChannelAccent.Resolve`.
- Commit `861ddfe` FOUND in git log.
- Suite `1036/1036` green via the in-Editor bridge, fresh assembly (`editorAssemblyWrittenUtc 2026-07-15T10:55:56Z`).

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-15*
