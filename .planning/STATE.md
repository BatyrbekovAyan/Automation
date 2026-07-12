---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Telegram Parity
status: roadmap_complete
stopped_at: Roadmap created — 6 phases (3-8), 29/29 requirements mapped; ready to plan Phase 3
last_updated: "2026-07-12T00:00:00.000Z"
last_activity: 2026-07-12
progress:
  total_phases: 6
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-12)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** v1.1 Telegram Parity — bring the Telegram channel (Wappi tapi) to full parity with WhatsApp. Design: `docs/superpowers/specs/2026-07-12-telegram-parity-design.md`; research: `.planning/research/telegram-parity/`; roadmap: `.planning/ROADMAP.md` (Phases 3-8).

## Current Position

Phase: 3 of 8 (tapi Live-Shape Capture) — first v1.1 phase
Plan: — (not yet planned)
Status: Ready to plan
Last activity: 2026-07-12 — Roadmap created (6 phases 3-8, 29/29 requirements mapped)

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**

- Total plans completed (v1.0): 8
- Average duration: ~11min/plan (v1.0 phase 2 sample)

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work (v1.1 design, spec §2):

- [D1]: In-screen channel switcher (TopBar CenterZone segmented pill); Telegram bottom tab + Screen_Telegram placeholder removed; per-bot channel persistence `{botId}ActiveChatChannel` → Phase 6.
- [D2]: Dashboard Telegram inclusion sequenced last (server zero changes; chips + deep-link need the channel concept) → Phase 7; explicit cut line if scope shrinks.
- [D3]: Suggestions = additive v1.1 contract (`channel` + `botTgId`, `botWaId` kept) + channel-branched RAG (single-key invariant) → Phase 7 (client) / Phase 4 (workflow branch).
- [D4]: `ChatChannel` enum + `ActiveChannel`; `WappiEndpoints.Sync(channel, path)` replaces 11 URL literals; Telegram cache under `BotCache/{botId}/telegram/` (no WA migration); `OutboxEntry` gains channel → Phase 5.
- [D5]: Confirmed tapi divergences (type:"text", numeric ids, last_time/last_timestamp swap, no isGroup, reply endpoint, reaction recipient, no chat/delete, native avatars, 2FA branch) → Phases 3 (verify) + 5 (implement).
- [D6]: Live shape capture is a USER-ASSISTED gate (`secrets.json` deny-ruled) — `Tools/tapi/capture-shapes.sh`; 13 open items in tapi-shapes.md §11 → Phase 3.
- [D7]: Telegram_Bot template fixes + RAG re-stamp in BOTH Create orchestrators; WhatsApp template untouched → Phase 4.

### Pending Todos

None yet.

### Blockers/Concerns

- [Gate/Phase 3]: tapi media message shapes (messages/get) undocumented — Normalize/media work (Phase 5 CHAT-03) blocked until the owner runs the capture script against an authorized dev Telegram profile.
- [Gate/Phase 4]: TPL-06 e2e needs dev n8n (localhost:5678) + tunnel + a real authorized Telegram profile (user-assisted).
- [Constraint]: Assume Wappi response-crossing bugs apply to tapi — keep serial media queue + `_chatFetchesInFlight` gate; reset on channel switch like bot switch.
- [Constraint]: Bot workflow clones stay INACTIVE except during active testing (real contacts!); prod bagkz stays dormant.
- [Risk]: Any existing dev Telegram workflow clones carry wrong api/sync URLs — recreate after template fix.

## Deferred Items

Items acknowledged and carried forward:

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| Feedback | FB-01 thumbs-up/down to improve ranking | Deferred to v2 | v1.0 Init |
| Insight | FB-02 per-chat/per-bot suggestion analytics | Deferred to v2 | v1.0 Init |
| uat_gap | Phase 01: 01-HUMAN-UAT.md — 4 pending device scenarios | partial → Phase 8 | v1.0 close 2026-07-11 |
| uat_gap | Phase 02: 02-HUMAN-UAT.md — 4 pending device scenarios | partial → Phase 8 | v1.0 close 2026-07-11 |
| verification_gap | Phase 01: 01-VERIFICATION.md awaits device confirmation | human_needed → Phase 8 | v1.0 close 2026-07-11 |
| Polish | POL-01 streaming/animated suggestion reveal | Deferred to v2 | v1.0 Init |
| Milestone | Prod bagkz replication (Suggest Replies + all Telegram fixes) | pending → Phase 8 checklist | v1.1 start 2026-07-12 |
| Milestone | Server-side «Вместе» suppression | pending (v2 SUPPRESS-01) | v1.0 close |

Note: POL-02 "Telegram chat support for the panel" graduated to v1.1 scope (SUGG-01/02, Phase 7).

## Session Continuity

Last session: 2026-07-12
Stopped at: Roadmap created (Phases 3-8, 29/29 mapped) — ready to plan Phase 3
Resume file: None
