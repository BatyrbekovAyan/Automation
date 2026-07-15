---
phase: 05-channel-aware-chatmanager-core
plan: 11
subsystem: ui
tags: [telegram, channel, theming, accent-color, scroll-fab, unread-badge, messages-view]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: "ChannelAccent.Resolve(channel, whatsappAuthored) — pure channel→accent-color seam (05-10)"
  - phase: 05-channel-aware-chatmanager-core
    provides: "ChatManager.ActiveChannel (per-bot WhatsApp/Telegram identity, 05-02)"
provides:
  - "Telegram-blue recolor of the open-chat scroll-to-unread FAB's green unread-count badge pill (ScrollToBottomFab.ApplyChannelAccent), completing the 05-10 accent set on the messages view"
affects: [device-uat, milestone-closeout, any future channel-branded accent]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-branded accent on a PERSISTENT widget = cache the authored WhatsApp color once at Awake, then set the color explicitly for BOTH channels on every ApplyChannelAccent call (the widget is never re-instantiated, so 'leave it' would stick blue)"
    - "Keep the FAB decoupled from ChatManager: ApplyChannelAccent takes the ChatChannel as a parameter; MessageListView (which already owns ChatManager policy) reads ActiveChannel and passes it in"

key-files:
  created: []
  modified:
    - Assets/Scripts/Chat/ScrollToBottomFab.cs
    - Assets/Scripts/UI/MessageListView.cs

key-decisions:
  - "The ONLY green accent on the FAB is the unread-count badge pill (#26B25A — the same UnreadGreen as ChatItemView's chat-list unread pill). The FAB body is a WHITE circle and the Button's image is a TRANSPARENT hit area — neither is green, so both are left untouched. The fix brief's premise that 'the FAB green is on the button's Image' did not match the scene (verified in UnreadMarkersBuilder + Main.unity); the governing constraint 'only recolor genuinely-green accents / WhatsApp byte-identical' resolved it to the badge only."
  - "badgeText stays white (readable on both green and blue) — text is ink, not the accent."
  - "Applied at the chat-open bind (MessageListView.OnChatSelected). A channel switch closes+reopens the chat, so the reopen repaints the badge; the FAB is hidden/reset between chats, so no other call site is needed."

patterns-established:
  - "Persistent-widget channel accent: ApplyChannelAccent(channel) caches the authored color once and re-applies every call; caller passes ChatManager.Instance.ActiveChannel null-guarded"

requirements-completed: []

# Metrics
duration: ~17 min
completed: 2026-07-15
---

# Phase 5 Plan 11: Scroll-to-Unread FAB Badge Channel Theming Summary

**The open-chat scroll-to-unread FAB's green unread-count badge pill recolors to Telegram brand blue (#2AABEE) on the Telegram channel via the existing `ChannelAccent.Resolve` seam — WhatsApp stays byte-identical — closing the last green accent 05-10 left on the messages view.**

## Performance

- **Duration:** ~17 min
- **Started:** 2026-07-15T14:08:00Z (approx — context read + scene audit + implementation)
- **Completed:** 2026-07-15T14:25:05Z
- **Tasks:** 1 (atomic fix commit)
- **Files modified:** 2 source (.cs), 0 new .meta (both files pre-existed)

## Accomplishments

- **`ScrollToBottomFab.ApplyChannelAccent(ChatChannel channel)`** — routes the unread-count badge pill's fill through the already-tested `ChannelAccent.Resolve` seam: Telegram ⇒ brand blue `#2AABEE` (carrying the authored alpha), every other channel ⇒ the authored WhatsApp green returned byte-identical. The authored green is cached once at `Awake` (`CacheAccentColor`), so the recolor never hardcodes a scene green and a WhatsApp rebind reverts the pill to exactly the authored value. Because the FAB is a persistent widget (never re-instantiated across chat opens or channel switches), the color is set explicitly on every call rather than left — otherwise a Telegram-blue pill would stick when switching back to WhatsApp.
- **`MessageListView.ApplyFabChannelAccent()`** — invoked at the chat-open bind (`OnChatSelected`, right after the FAB is reset/hidden for the new chat). Reads `ChatManager.Instance.ActiveChannel` (null-guarded ⇒ WhatsApp in Editor/tests) and passes it to the FAB. Since a channel switch closes+reopens the chat, the reopen fires `OnChatSelected` and repaints the badge for the new channel — and the FAB is always hidden/reset between chats, so this single bind-point call is complete.
- **Scope held to the one genuine green accent.** A full scene audit (`UnreadMarkersBuilder.cs` + `Main.unity`) confirmed the FAB body is a WHITE circle, the chevron is grey, and the Button's `image` is a TRANSPARENT (alpha-0) hit area — the unread badge pill (`#26B25A`) is the only green element. It is the messages-view twin of the chat-list unread pill 05-10 already recolored (identical `UnreadGreen`), so the two now read as one brand across both surfaces.

## Task Commits

1. **FAB badge Telegram-blue recolor (ScrollToBottomFab seam + MessageListView wiring)** — `7affb7b` (fix)

**Plan metadata:** (this SUMMARY + STATE.md + 05-HUMAN-UAT.md) — see final docs commit.

## Files Created/Modified

- `Assets/Scripts/Chat/ScrollToBottomFab.cs` — `ApplyChannelAccent(ChatChannel)` + `CacheAccentColor()`; authored-green badge cache (`_badgeImage` / `_authoredBadgeColor`), cached at `Awake`.
- `Assets/Scripts/UI/MessageListView.cs` — `ApplyFabChannelAccent()` helper (null-guarded `ChatManager.ActiveChannel`), called from `OnChatSelected`.

## Decisions Made

- **Badge-only recolor (the fix brief's `button.image` instruction was superseded by the scene reality + the governing constraint).** The brief assumed the FAB's green was on the Button's `Image`. Reading the builder and the scene showed otherwise: the Button's image is a transparent hit area (`Color(1,1,1,0)`), the visible FAB circle is white (`#FFFFFF`), and the ONLY green is the badge pill (`#26B25A`, GameObject `Badge`). Recoloring the transparent hit-area image would be a visual no-op on a non-green element and would touch a surface the "only recolor genuinely-green accents / WhatsApp byte-identical, verify no other view/color touched" constraint says to leave alone. So only the badge is recolored — matching the objective ("recolor the FAB green to Telegram blue") precisely, since the badge pill *is* the FAB's green. Telegram's FAB body is likewise a white/light circle, so leaving the circle white is also visually correct.
- **`badgeText` left white** — it stays readable on both the green and blue pill; text is ink, not the accent (mirrors the 05-10 empty-state decision to leave labels/near-white tints as authored).
- **FAB kept decoupled from ChatManager** — `ApplyChannelAccent` takes the channel as a parameter (consistent with the class's stated design: "a self-contained widget … knows nothing about chats"); `MessageListView`, which already owns ChatManager policy, resolves and passes `ActiveChannel`.
- **No new EditMode test** — all new code is MonoBehaviour glue that routes through the already-tested `ChannelAccent.Resolve` seam (7 `ChannelAccentTests` from 05-10 cover the color math incl. alpha preservation and the WhatsApp byte-identical passthrough). No pure seam was introduced, so no test is addable; the suite stays at the 1036 baseline (same rationale noted for prior MonoBehaviour-only accent wiring in 05-10).

## Deviations from Plan

None — no bugs, blockers, or missing-critical functionality were auto-fixed. The badge-only scope (vs. the brief's `button.image` mention) is a scene-verified scope clarification under the plan's own governing constraint, documented above under Decisions Made — not an unplanned code change.

**Total deviations:** 0 auto-fixed.
**Impact on plan:** Implementation matches the objective and constraints exactly; scope is a strict subset of the brief (badge only), touching no non-green surface.

## Issues Encountered

- **Environment differed from the brief:** the brief noted "NO real Unity process (a stale `Temp/UnityLockfile` may exist)". On execution a REAL Unity Editor (PID 22038, Hub-launched interactive) WAS open on the project, so `Tools/run-tests-headless.sh` correctly REFUSED (batch `-runTests` cannot take the project lock the Editor holds). This is NOT the stale-lockfile case the brief's contingency covers, so the lockfile was left untouched (deleting it would not release a live Editor's lock). Per the 05-08/05-09/05-10 precedent, verification ran via the sanctioned in-Editor bridge (`Temp/claude/run-tests.trigger` → `test-summary.json`); the Editor auto-refreshed on the edited files and recompiled before the run.

## Verification

- **Test path:** sanctioned in-Editor bridge (real Editor open, PID 22038). Full EditMode suite.
- **Result:** `1036/1036 passed, 0 failed, 0 skipped, 0 inconclusive` (baseline 1036 from 05-10 preserved — no tests added).
- **Staleness gate:** `editorAssemblyWrittenUtc = 2026-07-15T14:22:48.775Z` — postdates the last source edit (`MessageListView.cs` at 14:20:35Z) AND the trigger arming (14:22:30Z), so the bridge recompiled after my edits and the green reflects the new code.
- **WhatsApp byte-identical:** the entire pre-existing suite stayed green with the FAB's WhatsApp branch returning the cached authored green (no WA regression); the change is runtime-only (no scene/prefab edit).

## Next Phase Readiness

- Code-correct + suite-green + no WhatsApp regression. Pixel-perfect blue is an owner **device re-verify** (Phase 8) — a device-reverify line was appended to `05-HUMAN-UAT.md` (§7 addendum: FAB unread-count badge → blue on the Telegram channel).
- No scene/prefab edits (color applied at runtime) — nothing to import or migrate.
- Closes the messages-view accent gap left after 05-10 (which covered the chat-list unread pill/time + empty-state CTA/icon). The Авто/Вместе mode toggle, message bubbles, and the switcher chips remain deliberately untouched (out of the owner-confirmed accents-only scope).

## Self-Check: PASSED

- Modified files present + reference the seam: `ScrollToBottomFab.cs` (`ApplyChannelAccent` → `ChannelAccent.Resolve`), `MessageListView.cs` (`ApplyFabChannelAccent` → `scrollToBottomFab.ApplyChannelAccent`) — both FOUND.
- Commit `7affb7b` FOUND in git log; no file deletions in the commit.
- Suite `1036/1036` green via the in-Editor bridge, fresh assembly (`editorAssemblyWrittenUtc 2026-07-15T14:22:48Z`).

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-15*
