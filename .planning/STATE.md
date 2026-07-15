---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Telegram Parity
status: executing
stopped_at: Phase 8 autonomous deliverables complete + verified (10/10 must-haves, 0 gaps; code review clean 0 crit/0 warn/2 info). Phase human_needed — 3 owner gates pending (device UAT → prod copy → milestone close). Prod bagkz still DORMANT; move to prod URLs is owner-explicit-only.
last_updated: "2026-07-15T10:57:52Z"
last_activity: 2026-07-15 -- 05-10 off-plan (channel-aware accent theming, WA green → TG blue, suite 1036/1036); Phase 08 still human_needed (3 owner gates)
progress:
  total_phases: 7
  completed_phases: 5
  total_plans: 17
  completed_plans: 16
  percent: 94
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-12)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 08 (device UAT + milestone closeout) — autonomous deliverables done + verified; **human_needed**, awaiting the 3 owner-run gates (Gate A device UAT via `08-DEVICE-UAT.md` → Gate B prod copy via `08-PROD-REPLICATION.md` → Gate C close via `08-MILESTONE-CLOSE.md`). Prod stays DORMANT; the move to prod URLs is owner-explicit-only.

## Current Position

Phase: 08 (device-uat-milestone-closeout) — AUTONOMOUS COMPLETE, human_needed
Plan: 3 of 3 authored (08-DEVICE-UAT.md; 08-PROD-REPLICATION.md + 2 dev-byte-identical script tweaks; 08-MILESTONE-CLOSE.md); each ends in a blocking owner gate
Status: Awaiting 3 owner-run gates (device UAT → prod copy → milestone close). Do NOT mark phase/milestone complete until the owner signs off.
Last activity: 2026-07-15 -- Phase 08 autonomous execution complete + verified (human_needed)

Progress: [██████████] 100%

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
- [Phase 7]: 07-01 channel-aware «Вместе» payload — additive v1.1 wire (botTgId + channel appended after messages, v1 keys byte-identical; server Prep defaults absent channel=>whatsapp, Phase 4). BuildPayloadJson pure + channel-RESOLVED (profileId TG=>telegramProfileId, botWaId=whatsappWorkflowId ALWAYS for the default WA RAG branch, channel lowercase enum-derived ONLY per T-07-01-01); Run() reads ChatManager.ActiveChannel; 7 channel-matrix + additive-identity tests, 908/908 EditMode green; SUGG-01/02 client half, live TG grounding rides TPL-06 (07-HUMAN-UAT.md)
- [Phase 7]: 07-02 dashboard «Сводка» on Telegram — pure DashboardProfileMap seam collects BOTH channels' authed ids + maps id→(botName,channel) (channel from the matched LOCAL entry, never the server payload; T-07-02-01); DashboardMetrics.FilterByProfiles(ISet) set filter, FilterByProfile delegates; DashboardPage POSTs TG ids (DASH-01), one chip per bot with a HashSet filter so a dual-channel bot is a single chip (DASH-02), and channel-aware OpenChat SetActiveBot→SetActiveChannel→SwitchTab(«Чаты»)→deferred SelectChat (DASH-03); WhatsApp byte-identical, server contract + Main.unity untouched; 916/916 EditMode green
- [Phase 5]: 05-06 capture-gated media/reactions/reply — tapi media is download-only (body:null+s3Info:{} → existing serial media/download-by-id; metadata from media_info + flat name/mime; video-as-document→Video via mimetype); receive-side reactions BUILT (Q3 GO, v2 TG-REACT-RECV superseded) as a Normalize-time reactions[] map + reconcile merge preserving optimistic 'me'; ChatIdFormat classifies 'channel' dialogs group-ish (Q4); reply Q8 no-echo verified; name/isDeleted verdict-resolved (no change); WhatsApp byte-identical, 957/957 EditMode green
- [Phase 5]: 05-07 Telegram media presentation gap-closure — .tgs (application/x-tgsticker) refines to Sticker and renders a deliberate borderless placeholder + «Стикер» with NO download (gzipped Lottie undecodable; native animation = v2); video note detected by pure heuristic (square + video.mp4 + ≤60s — is_round deliberately ignored, unreliable per SHAPES.md Q2) and rendered as a circle (half-side RoundedCorners radius) + duration badge; GIF (isGif through all 4 layers) keeps the video pipeline + 'GIF' corner badge; flags minted ONLY inside ApplyTelegramMediaShape so WhatsApp is byte-identical; 988/988 EditMode green (966+22)
- [Phase 5]: 05-09 tapi status parse hardening (off-plan, from owner Editor screenshots after 05-08) — new pure WappiStatusParser (Newtonsoft JObject, throw-safe): TryGetAuthorized / TryGetPhone (top-level `phone` wins over `account.phone`; the pretty tapi body carries TWO phone keys, which the old substring scan + no-whitespace `","platform":` guard mis-read into a stored JSON blob) / IsPlausiblePhone. Wired into the two named Telegram status sites (Manager.GetTelegramProfileStatus + BotSettings.CheckTelegramAuthorization); the stale `{bot}TelegramNumber` blob self-heals to "" via IsPlausiblePhone at the two field-load sites + the EnableSave dirty-check (no re-auth). Rule 1 deviations: a THIRD Telegram status site (CheckTelegramUnauthorizationOutsideApp) THREW on the pretty body (negative Substring) → same throw-safe parser; and the dirty-check was sanitized too so the load-sanitize doesn't spuriously light Save. WhatsApp api/sync parses byte-identical (safe future adopter). Fix B: channel-switcher chip labels padded (ChannelSwitcherBuilder LabelSize 28→22 + 12px label inset), Main.unity rebuilt headlessly + grep-verified (2× fontSize-22 labels, 2× -24 insets; nav a guarded no-op). 21 new parser tests, suite 1028/1028 EditMode green. commits 584be1d + d68534f + e4f6451. Pixel-perfect look + correct number = Phase 8 device re-verify (05-HUMAN-UAT gaps 4+5)
- [Phase 5]: 05-10 channel-aware accent theming (off-plan, owner request — Phase-8-era polish) — new pure `ChannelAccent.Resolve(channel, whatsappAuthored)` seam: Telegram ⇒ brand blue #2AABEE (authored alpha preserved, matches ChannelSwitcherView.TgSelectedFill), any other channel ⇒ authored color BYTE-IDENTICAL. Wired at two accent surfaces, each caching its authored green at runtime (never hardcodes a scene green; pooled rows revert exactly on WA rebind): ChatItemView.ApplyUnreadBadge (unread pill fill + unread time tint) and EmptyStateView.ConfigureForReason (connect/create CTA fill + placeholder icon, all three reasons). Scope = accents only (owner-confirmed): bubbles, Авто/Вместе toggle, and channel/bot switcher chips deliberately untouched. Null-safe throughout (missing Image / null ChatManager ⇒ no-op). 7 new ChannelAccentTests; WhatsApp byte-identical, suite 1036/1036 EditMode green via in-Editor bridge (real Editor was open — lockfile untouched). commit 861ddfe. Pixel-perfect blue = Phase 8 device re-verify (05-HUMAN-UAT §7)
- [Phase 5]: 05-08 media device-UAT round-2 polish (off-plan, from an owner device screenshot after 05-07) — (1) video note now floats BUBBLE-FREE: the transparency decision extracted to a pure seam BubbleTransparencyPolicy.IsTransparent(isSticker,isVideoNote,isPlaceholderActive,hideBubble) + isVideoNote, so the circle floats chrome-free like native TG (time stays readable via the existing Video+no-caption white-text/timeBackground media overlay; !isPlaceholderActive keeps an UNAVAILABLE note on a visible retry card); (2) .tgs sticker now renders a deliberate sticker-slot-sized (396²) neutral rounded CARD with its OWN fill + centered «Стикер» + mid-gray glyph (the 05-07 white-silhouette placeholder was invisible on the transparent bubble → collapsed to a tiny pill on device). Telegram-only (isVideoNote default-false; card gated on TgsStickerMime), WhatsApp byte-identical; verified via the sanctioned in-Editor bridge (owner's Editor was open, headless refuses on a held lock; gated on editorAssemblyWrittenUtc 17:06:24Z), 1007/1007 EditMode green (997+10). commits 72a5909 + a27cf16. Pixel-perfect look = Phase 8 device re-verify

### Pending Todos

None yet.

### Blockers/Concerns

- [Gate/Phase 8 — 05-06-REVIEW WR-02]: TG message ids are 1-5 digit per-account/per-channel counters. The client-side vthumb cache key is now TG-namespaced (`vthumb://tg/{profileId}/{chatId}/{messageId}`), but `message/media/download` / `messages/id/get` carry NO chat id — device UAT must send videos into a TG channel post and a private chat with overlapping numeric message ids on the dev profile and confirm the download returns the right bytes; if it crosses, check whether tapi accepts a `chat_id` param on the download endpoint.
- [UAT expectation/Phase 8 — 05-06-REVIEW IN-04+IN-05, accepted v1, NOT bugs]: TG reactions are poll-reconciled with no per-reaction timestamps, so (a) receive-side reactions update open-chat bubbles only — the chat-list preview row does NOT show "X reacted…" (WhatsApp parity gap, inherent to the transport); (b) removing your own reaction can transiently flicker back for one refresh cycle when a stale `messages/get` snapshot was in flight (self-heals next poll; mitigation on the shelf: per-message reconcile suppression for one cycle post-POST).
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
| Phase 07 P01 | 7min | 3 tasks | 4 files |
| Phase 07 P02 | 7min | 2 tasks | 5 files |
| Phase 05 P06 | 42min | 3 tasks | 13 files |
| Phase 05 P07 | 25min | 3 tasks | 12 files |
| Phase 05 P08 | ~24min | 2 fixes | 3 files |
| Phase 05 P09 | ~22min | 2 fixes | 6 files |

## Session Continuity

Last session: 2026-07-15T10:57:52.000Z
Stopped at: Completed 05-10 (off-plan channel-aware accent theming — WA green → TG blue via pure ChannelAccent seam; 1036/1036 green via in-Editor bridge, real Editor was open). Phase 08 still human_needed (3 owner gates).
Resume file: None

**Planned Phase:** 8 (Device UAT + Milestone Closeout) — 3 plans — 2026-07-15T08:16:54.617Z
