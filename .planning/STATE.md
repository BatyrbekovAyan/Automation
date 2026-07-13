---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Telegram Parity
status: executing
stopped_at: Completed 06-02-PLAN.md
last_updated: "2026-07-13T11:02:43.839Z"
last_activity: 2026-07-13
progress:
  total_phases: 6
  completed_phases: 3
  total_plans: 11
  completed_plans: 10
  percent: 91
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-12)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 6 (Channel Switcher UI) — **code-complete** (both plans executed). 06-01 shipped the runtime half (pure `ChannelSwitcherModel` + event-driven `ChannelSwitcherView` binder + `BotsTabIndex` 3→2 locked by `TabIndexShiftTests`). 06-02 built the scene half **headlessly**: a `ChannelSwitcherBuilder` builds the WhatsApp|Telegram segmented pill into `Screen_Whatsapp/ChatsPanel/TopBar/CenterZone` (two independent brand fills per the 06-01 binder contract, text-only chips mirroring ModeToggle), stamps all 6 `ChannelSwitcherView` refs via SerializedObject, and guardedly removes the Telegram bottom tab (tabs 5→4) + `Screen_Telegram` + `TelegramTab` and relabels tab 0 «Чаты»; new `Tools/run-editor-builder.sh` (Editor-closed, sentinel verdict) ran it, scene committed immediately (`8f1d25f`), suite **900/900 green** against the real 4-tab scene. SWITCH-01/04 marked. **Only the owner visual UAT gate (`06-HUMAN-UAT.md`) remains before phase close.** Next: Phase 7 («Вместе» suggestions + dashboard on Telegram).

## Current Position

Phase: 6 of 8 (channel switcher UI)
Plan: 2 of 2 complete (06-02 ChannelSwitcherBuilder + nav restructure — headless pill build into CenterZone, 6 refs stamped, Telegram tab + Screen_Telegram removed, tab 0 «Чаты»; scene 8f1d25f; 900/900 EditMode green)
Status: Phase 6 code-complete — owner visual UAT gate open (06-HUMAN-UAT.md); Phase 7 next
Last activity: 2026-07-13

Progress: [█████████░] 91%

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
- [Phase 4]: Unity create-workflow forms now send the opposite channel's workflow id (sentinel-guarded '-1') — enables the 04-01 RAG re-stamp on late channel auth (TPL-05 client half)
- [Phase 4]: 04-HUMAN-UAT.md is the TPL-06 owner gate (dev n8n + tunnel + import-by-literal-id + Postgres cred pre-flight + text/voice/memory/pre-auth re-stamp e2e); closes the phase
- [Phase 5]: Telegram 2FA fix (TGAUTH-01) — pure TelegramAuthResponseParser classifier + detail:2fa cloud-password branch in both code and QR flows posting tapi/sync/auth/2fa {pwd_code}; password never logged/persisted, cleared after submit; no new scene objects; 839/839 EditMode green
- [Phase 5]: 05-02 ChatManager identity seam — ActiveChannel persisted per bot ({botId}ActiveChatChannel), SetActiveChannel reuses SetActiveBot reset choreography + OnActiveChannelChanged; channel-aware GetActiveProfileId/GetCacheRoot (BotCache/{botId}/telegram/ isolation, CHAT-11)/empty-state/sync-gate; ResolveChannelForBot auto-selects connected channel on switch/startup; BotHasNoTelegram empty state; WhatsApp byte-identical, 854/854 EditMode green
- [Phase 5]: 05-03 read pipeline + tapi parser divergences — 8 non-send chat URLs via WappiEndpoints.Sync(ActiveChannel); ActiveChannelSupportsChatDelete no-ops DeleteChat on Telegram; ParseMessageType 'text'=>Chat + last_timestamp->last_time fallback + DisplayFallback retires chat.id[..^5] + ChatIdFormat.IsGroup groupness; ChatViewModel.IsGroup at construction; pending/undelivered/error ticks; pure MessageTypeParser/ChatDialogTime seams; WhatsApp byte-identical, 878/878 EditMode green
- [Phase 5]: 05-04 send-path channel branches — Telegram quoted reply => tapi message/reply {body, message_id} (no recipient); reaction body gains required recipient (NullValueHandling.Ignore, WA byte-identical); mark-read drops mark_all on Telegram; media EndpointFor 3-arg via (ChatChannel)entry.channel; text+media outbox snapshot channel, text retry rebuilds URL from entry.channel; last api/sync literals in ChatManager.cs retired; WhatsApp byte-identical, 888/888 EditMode green
- [Phase 6]: 06-01 channel-switcher runtime — pure ChannelSwitcherModel (selected=equality, muted=own-channel connectivity, both can hold) + event-driven ChannelSwitcherView binder (reads ChatManager.ActiveChannel read-only so SWITCH-03 persistence flows through; muted chips stay tappable for SWITCH-02's connect empty state; every ref null-guarded; field names are the 06-02 builder's SerializedObject contract); BottomTabManager.BotsTabIndex 3→2 locked by TabIndexShiftTests (all SwitchTab consumers already constant-based, no literals); no scene mutation; SWITCH-01/04 land in 06-02; 900/900 EditMode green
- [Phase 6]: 06-02 channel switcher scene half — headless ChannelSwitcherBuilder builds the WhatsApp|Telegram segmented pill into TopBar CenterZone (two independent brand fills per 06-01 binder contract, text-only chips mirroring ModeToggle) + stamps all 6 ChannelSwitcherView refs via SerializedObject; guarded nav restructure removes Telegram tab (tabs 5→4) + Screen_Telegram + TelegramTab, relabels tab 0 «Чаты»; run-editor-builder.sh (Editor-closed, sentinel verdict); scene committed immediately 8f1d25f; 900/900 EditMode green; SWITCH-01/04 marked; owner UAT gate open

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
| Phase 04 P02 | ~10min | 2 tasks | 2 files |
| Phase 05 P01 | 35min | 3 tasks | 12 files |
| Phase 05 P05 | 26min | 3 tasks | 4 files |
| Phase 05 P02 | 19min | 3 tasks | 6 files |
| Phase 05 P03 | 27min | 3 tasks | 13 files |
| Phase 05 P04 | 9min | 3 tasks | 7 files |
| Phase 06 P01 | 21min | 3 tasks | 5 files |
| Phase 06 P02 | 10min | 3 tasks | 5 files |

## Session Continuity

Last session: 2026-07-13T11:02:26.643Z
Stopped at: Completed 06-02-PLAN.md
Resume file: None

**Planned Phase:** 6 (Channel Switcher UI) — 2 plans — 2026-07-12T19:35:08.582Z
