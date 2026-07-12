---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Telegram Parity
status: planning
stopped_at: Completed 04-01-PLAN.md
last_updated: "2026-07-12T13:39:22.953Z"
last_activity: 2026-07-12
progress:
  total_phases: 6
  completed_phases: 1
  total_plans: 3
  completed_plans: 2
  percent: 67
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-12)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 4 (n8n Telegram Template Parity, dev) — 04-01 complete; 04-02 next (Unity form fields + owner deploy/e2e gate)

## Current Position

Phase: 4 of 8 (n8n telegram template parity (dev))
Plan: 2 of 2 (04-01 complete; 04-02 next)
Status: In progress
Last activity: 2026-07-12

Progress: [███████░░░] 67%

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
- Phase 3 shape-capture tooling shipped: read-only Tools/tapi/capture-shapes.sh + pre-filled 13-question SHAPES.md; Q9-Q13 verdicts DEFERRED (not observable read-only). Owner-run capture is the phase-closing human gate (03-HUMAN-UAT.md), blocking Phase 5 CHAT-03/CHAT-07 media/Normalize.
- [Phase 4]: Telegram_Bot template moved onto tapi (send/mark-read/typing + text routing + length_seconds voice fallback + chatId sessionKey); node order preserved
- [Phase 4]: RAG re-stamp added to both Create orchestrators (parameterized UPDATE, cred vvRrFiEXzLVqKjOx) preserving the { id } response; Suggest_Replies given additive channel-branched RAG (botWaId | botTgId), verifier verify-telegram-parity.py green

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
| Phase 03 P01 | 10 min | 2 tasks | 5 files |
| Phase 04 P01 | 15min | 3 tasks | 5 files |

## Session Continuity

Last session: 2026-07-12T13:39:00.326Z
Stopped at: Completed 04-01-PLAN.md
Resume file: None

**Planned Phase:** 4 (n8n Telegram Template Parity (dev)) — 2 plans — 2026-07-12T13:23:46.594Z
