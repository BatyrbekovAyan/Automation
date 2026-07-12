---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Telegram Parity
status: defining_requirements
stopped_at: Milestone v1.1 started — defining requirements
last_updated: "2026-07-12T00:00:00.000Z"
last_activity: 2026-07-12
progress:
  total_phases: 0
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-12)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** v1.1 Telegram Parity — bring the Telegram channel (Wappi tapi) to full parity with WhatsApp. Design: `docs/superpowers/specs/2026-07-12-telegram-parity-design.md`; research: `.planning/research/telegram-parity/`.

## Current Position

Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements
Last activity: 2026-07-12 — Milestone v1.1 started

## Performance Metrics

**Velocity:**

- Total plans completed (v1.0): 8
- Average duration: ~11min/plan (v1.0 phase 2 sample)

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work (v1.1 design, spec §2):

- [D1]: In-screen channel switcher (TopBar CenterZone segmented pill); Telegram bottom tab + Screen_Telegram placeholder removed; per-bot channel persistence `{botId}ActiveChatChannel`.
- [D2]: Dashboard Telegram inclusion sequenced last (server needs zero changes; chips + deep-link need the channel concept).
- [D3]: Suggestions = additive v1.1 contract (`channel` + `botTgId` fields, `botWaId` kept) + channel-branched RAG filter (single-key invariant preserved); per-bot «Вместе» default stays bot-scoped.
- [D4]: `ChatChannel` enum + `ActiveChannel` on ChatManager; `WappiEndpoints.Sync(channel, path)` builder replaces 11 URL literals; Telegram cache under `BotCache/{botId}/telegram/` (no WhatsApp migration); `OutboxEntry` gains channel; no Telegram post-auth sync window initially.
- [D5]: Confirmed tapi divergences from official docs — type:"text", numeric chat ids, last_time/last_timestamp swap, no isGroup (dialog type=="chat"), reply via dedicated endpoint, reaction needs recipient, no chat/delete (feature-gate), native avatars, 2FA auth branch required.
- [D6]: Live shape capture is a USER-ASSISTED gate (secrets.json deny-ruled for Claude) — `Tools/tapi/capture-shapes.sh` run by owner; 13 open items in `.planning/research/telegram-parity/tapi-shapes.md` §11.
- [D7]: Telegram_Bot template fixes (tapi bases, text type, length_seconds fallback, sessionKey→chatId) + RAG re-stamp nodes in BOTH Create orchestrators; WhatsApp template untouched.

### Pending Todos

None yet.

### Blockers/Concerns

- [Gate]: tapi media message shapes (messages/get) are undocumented — Normalize/media work blocked until the owner runs the capture script against an authorized dev Telegram profile.
- [Constraint]: Assume Wappi response-crossing bugs apply to tapi — keep serial media queue + `_chatFetchesInFlight` gate; reset on channel switch like bot switch.
- [Constraint]: Bot workflow clones stay INACTIVE except during active testing (real contacts!); dev n8n (localhost:5678) + tunnel must be running for template e2e; prod bagkz stays dormant.
- [Risk]: Any existing dev Telegram workflow clones carry wrong api/sync URLs — recreate after template fix.

## Deferred Items

Items acknowledged and carried forward:

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| Feedback | FB-01 thumbs-up/down to improve ranking | Deferred to v2 | v1.0 Init |
| Insight | FB-02 per-chat/per-bot suggestion analytics | Deferred to v2 | v1.0 Init |
| uat_gap | Phase 01: 01-HUMAN-UAT.md — 4 pending device scenarios | partial | v1.0 close 2026-07-11 |
| uat_gap | Phase 02: 02-HUMAN-UAT.md — 4 pending device scenarios | partial | v1.0 close 2026-07-11 |
| verification_gap | Phase 01: 01-VERIFICATION.md awaits device confirmation | human_needed | v1.0 close 2026-07-11 |
| Polish | POL-01 streaming/animated suggestion reveal | Deferred to v2 | v1.0 Init |
| Milestone | Prod bagkz replication (Suggest Replies + all Telegram fixes, one bulk copy) | pending | v1.1 start 2026-07-12 |
| Milestone | Server-side «Вместе» suppression | pending | v1.0 close |

Note: POL-02 "Telegram chat support for the panel" graduates from deferred to v1.1 scope (SUGG requirements).

## Session Continuity

Last session: 2026-07-12
Stopped at: Milestone v1.1 started — defining requirements
Resume file: None
