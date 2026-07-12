# Roadmap: Automation — WhatsApp/Telegram AI Bot Manager

## Overview

v1.0 shipped the semi-auto «Вместе» reply path on WhatsApp. v1.1 Telegram Parity brings the Telegram channel (Wappi tapi) up to the same bar: a Telegram-authed bot gets a working chat client, live n8n auto-replies, «Вместе» suggestions, and dashboard inclusion — all on the existing single-scene, single-`ChatManager` architecture. The journey is dependency-driven (design spec §6): a small user-assisted **shape-capture gate** goes first so parser work builds against real tapi JSON, not guesses; **n8n template fixes** run independently on dev; the **channel-aware ChatManager core** is the largest phase and unblocks the **switcher UI**; **suggestions + dashboard** light up last (dashboard is the explicit cut line); and a **device-UAT closeout** carries in the v1.0 deferred UAT and updates the prod-replication checklist (prod bagkz stays dormant). Two phases are user-assisted gates (`secrets.json` is deny-ruled for Claude): live shape capture and the n8n e2e proof both require the owner to drive a real dev Telegram profile.

## Milestones

- ✅ **v1.0 Reply Suggestions** — Phases 1-2 (shipped 2026-07-11)
- 🚧 **v1.1 Telegram Parity** — Phases 3-8 (in progress)

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Continuous numbering across milestones — v1.1 starts at Phase 3.

<details>
<summary>✅ v1.0 Reply Suggestions (Phases 1-2) — SHIPPED 2026-07-11</summary>

- [x] Phase 1: Polished Suggestions Panel on Mock Data (4/4 plans) — completed 2026-06-25
- [x] Phase 2: n8n Live Wiring (4/4 plans) — completed 2026-07-10

Full details: `.planning/milestones/v1.0-ROADMAP.md`

</details>

### 🚧 v1.1 Telegram Parity (In Progress)

- [x] **Phase 3: tapi Live-Shape Capture** - User-assisted read-only capture script + sanitized samples + recorded verdicts on the 13 open shape questions (incl. reactions-receive go/no-go); gates the media/Normalize parser work. (completed 2026-07-12)
- [ ] **Phase 4: n8n Telegram Template Parity (dev)** - Telegram_Bot template onto tapi bases (outbound URLs, `type:"text"`, sessionKey, voice duration) + RAG re-stamp on late channel auth; proven e2e against a real dev Telegram profile via tunnel.
- [ ] **Phase 5: Channel-Aware ChatManager Core** - The channel seam (`ChatChannel`, `SetActiveChannel`, `WappiEndpoints` builder, per-channel caches), all tapi parser/send divergences, and the Telegram 2FA auth fix — WhatsApp behavior unchanged, full suite green.
- [ ] **Phase 6: Channel Switcher UI** - In-screen TopBar segmented WhatsApp|Telegram control with muted/connect affordances, per-bot channel persistence, and removal of the Telegram bottom tab.
- [ ] **Phase 7: «Вместе» Suggestions + Dashboard on Telegram** - Channel-aware suggestions payload + channel-branched RAG filter, and «Сводка» Telegram inclusion (bot-level chips, channel-aware deep-link). Dashboard is the milestone's cut line.
- [ ] **Phase 8: Device UAT + Milestone Closeout** - On-device end-to-end Telegram pass (incl. carried v1.0 deferred UAT) + prod-replication checklist update; prod bagkz stays dormant.

## Phase Details

### Phase 3: tapi Live-Shape Capture
**Goal**: Ground all Telegram parser/media work in real tapi response shapes — the owner produces sanitized live samples and every open shape question gets a recorded verdict, so downstream Normalize/media work builds against facts, not undocumented guesses.
**Depends on**: Nothing (first v1.1 phase). Requires an authorized dev Telegram profile (owner authorizes one via the existing in-app flow if none exists). `secrets.json` is deny-ruled for Claude, so capture is owner-driven.
**Requirements**: VER-01, VER-02
**Success Criteria** (what must be TRUE):
  1. The owner can run `Tools/tapi/capture-shapes.sh` against an authorized dev Telegram profile (read-only; token never leaves the machine) and get sanitized JSON samples in `Tools/tapi/samples/` covering chats/filter, messages/get across each media type, and a reply via messages/id/get.
  2. All 13 open tapi shape questions (`.planning/research/telegram-parity/tapi-shapes.md` §11) have a recorded verdict in SHAPES.md — including the media-body shape (body vs s3Info vs attaches), sticker/video-note/GIF `type` strings, and the `last_timestamp`/`last_time` JsonUtility binding behavior.
  3. The reactions-receive go/no-go decision is recorded (viable tapi transport vs deferred to v2), so Phase 5 knows whether to build receive-side reactions at all.
**Plans**: 1 plan
Plans:
- [x] 03-01-PLAN.md — Read-only tapi shape-capture script + 13-question SHAPES.md verdict checklist, gitignore, README, human-gate note (SUMMARY 2026-07-12; owner capture run tracked in 03-HUMAN-UAT.md)
**Flags**: USER-ASSISTED GATE — blocks the Normalize/media parts of Phase 5 (CHAT-03; media recovery in CHAT-07). URL/seam/parser-non-media work in Phase 5 can proceed in parallel with this capture.

### Phase 4: n8n Telegram Template Parity (dev)
**Goal**: A Telegram-authed bot actually converses — the Telegram_Bot template runs on tapi bases, routes text through the AI agent, keys session memory stably, and files uploaded before channel auth become RAG-retrievable — proven end-to-end against a real dev Telegram profile.
**Depends on**: Nothing hard on client work (n8n changes are independent and can run in parallel with Phases 3 and 5). TPL-06 e2e needs dev n8n (localhost:5678) + tunnel + a real authorized Telegram profile; benefits from Phase 3's auth/group verdicts but is not blocked by them.
**Requirements**: TPL-01, TPL-02, TPL-03, TPL-04, TPL-05, TPL-06
**Success Criteria** (what must be TRUE):
  1. A message sent to a Telegram-authed bot gets an AI reply delivered back in Telegram — the template's outbound nodes (`message/send`, `message/mark/read`, `chats/typing/start`) all hit tapi bases.
  2. Telegram text messages (`type:"text"`) route through the AI agent instead of the "Ask to Send Text" fallback, and voice input transcribes with a correct humanizer pause (`length_seconds` fallback for the missing `media_info`).
  3. Session memory stays coherent across a multi-turn Telegram conversation (sessionKey on `profile_id + ':' + chatId`, not a username-y `from`).
  4. A file uploaded before the Telegram channel was authed becomes RAG-retrievable once the Telegram workflow is created (re-stamp of the `"-1"` sentinel metadata in BOTH Create orchestrators; Unity passes the opposite channel's workflow id in the create form).
  5. The whole flow is proven end-to-end on dev with a real Telegram profile via tunnel — the clone is active only during the test window, then deactivated.
**Plans**: 2 plans
Plans:
- [x] 04-01-PLAN.md — Fix Telegram_Bot.json onto tapi (TPL-01..04) + RAG re-stamp in both Create orchestrators (TPL-05 server) + Suggest_Replies channel branch (D3) + structural verifier
- [x] 04-02-PLAN.md — Manager.cs opposite-channel workflow-id form fields (TPL-05 client) + 04-HUMAN-UAT.md owner deploy/e2e gate (TPL-06)
**Flags**: USER-ASSISTED e2e (dev n8n + tunnel + real TG profile). Prod bagkz stays dormant. Any existing dev Telegram workflow clones carry wrong `api/sync` URLs and must be recreated after the template fix.

### Phase 5: Channel-Aware ChatManager Core
**Goal**: A Telegram-authed bot has a fully working in-app chat client at WhatsApp parity — list, paginated history, media, send, quoted replies, reactions-send, mark-read, cache isolation — plus the Telegram 2FA auth fix, all on the new channel seam with every existing WhatsApp behavior unchanged.
**Depends on**: Phase 3 (media/Normalize branches consume the captured samples). The seam, URL builder, non-media parsers, send/reply/reaction/markread branches, caches, and 2FA fix can start in parallel with Phase 3; only the media Normalize port waits on samples.
**Requirements**: CHAT-01, CHAT-02, CHAT-03, CHAT-04, CHAT-05, CHAT-06, CHAT-07, CHAT-08, CHAT-09, CHAT-10, CHAT-11, TGAUTH-01
**Success Criteria** (what must be TRUE):
  1. The owner sees a Telegram bot's chat list (names, avatars, unread counts, correct timestamps via `last_time` fallback), opens any chat, and reads paginated history — text renders via the `type:"text"` mapping and numeric chat ids never slice/crash or amputate names.
  2. Telegram media (image/video/voice/document, plus sticker/GIF per the capture verdicts) renders with thumbnails, durations, and downloads; the owner can send text, send media, send a quoted reply (dedicated tapi `message/reply`), and send/remove emoji reactions (recipient-required tapi body).
  3. Opening an unread Telegram chat marks it read (no `mark_all` query); incoming Telegram replies render quoted cards (snapshot + `messages/id/get` recovery); swipe-to-delete is hidden on Telegram (no tapi endpoint) while WhatsApp delete is unchanged.
  4. One bot's WhatsApp and Telegram caches are isolated (`BotCache/{botId}/` vs `.../telegram/`), each channel opens offline from its own cache, purge/privacy clears cover both, and the existing 787-test suite stays green.
  5. A 2FA-protected Telegram account can authorize via a cloud-password step (`detail:"2fa"` → `tapi/sync/auth/2fa`) in both the code and QR flows.
**Plans**: TBD
**UI hint**: yes

### Phase 6: Channel Switcher UI
**Goal**: The owner flips between the active bot's WhatsApp and Telegram chats within one screen via a TopBar segmented control, with muted/connect affordances for an unconnected channel, per-bot channel persistence, and the Telegram bottom tab retired.
**Depends on**: Phase 5 (needs `ChatManager.ActiveChannel`, `SetActiveChannel` reset choreography, the channel-parameterized empty-state resolver, and per-channel caches).
**Requirements**: SWITCH-01, SWITCH-02, SWITCH-03, SWITCH-04
**Success Criteria** (what must be TRUE):
  1. The owner flips between the active bot's WhatsApp and Telegram chats via a TopBar segmented control — full list-reset choreography, mid-flight-safe (no crossed lists).
  2. An unconnected channel's chip renders visibly muted; tapping it shows that channel's empty state with a connect CTA (no more permanent "not connected" dead end for single-channel bots).
  3. The last-used channel persists per bot across restarts, and a bot with only one connected channel auto-selects it.
  4. The Telegram bottom-nav tab and the `Screen_Telegram` placeholder are removed, and tab 0 reads «Чаты».
**Plans**: TBD
**UI hint**: yes

### Phase 7: «Вместе» Suggestions + Dashboard on Telegram
**Goal**: The two remaining Telegram-aware surfaces light up — «Вместе» suggestions populate and stay RAG-grounded in Telegram chats, and the «Сводка» dashboard counts, filters (bot-level chips), and deep-links Telegram conversations.
**Depends on**: Phase 5 (channel-aware ChatManager: open-chat channel, `SetActiveBot`/channel/`SelectChat` deep-link) AND Phase 4 (channel-branched RAG vector-store node). Dashboard (DASH-*) is the explicit cut line if the milestone must shrink.
**Requirements**: SUGG-01, SUGG-02, DASH-01, DASH-02, DASH-03
**Success Criteria** (what must be TRUE):
  1. Suggestions populate for a Telegram chat — the provider payload carries channel-appropriate profile/workflow ids + a `channel` field (additive v1.1 contract; `botWaId` still sent for backward compat).
  2. Telegram suggestions are RAG-grounded via the `botTgId` metadata filter (channel-branched vector-store node; the single-key match-filter invariant is preserved).
  3. «Сводка» counts and lists Telegram conversations (telegram profile ids in the POSTed list + profile→bot map).
  4. A dual-channel bot shows exactly ONE filter chip covering both its profiles, and a Telegram outcome row deep-links straight to that Telegram chat (channel-aware `SetActiveBot`/channel/`SelectChat`).
**Plans**: TBD
**UI hint**: yes

### Phase 8: Device UAT + Milestone Closeout
**Goal**: The whole Telegram parity milestone is validated on a real device end-to-end and the prod-replication path is documented — carrying in the v1.0 deferred device UAT — with prod bagkz still dormant.
**Depends on**: Phases 3-7 (the full Telegram surface must exist).
**Requirements**: none new — closeout of carried items (v1.0 deferred device UAT + prod-replication checklist)
**Success Criteria** (what must be TRUE):
  1. A Telegram-authed bot is exercised on-device end-to-end — auth (incl. 2FA), chat list/history/media, send/reply/reaction, channel switch, auto-reply, «Вместе», dashboard — with results recorded in a HUMAN-UAT doc.
  2. The carried v1.0 deferred device-UAT scenarios (Phases 01–02) are run or explicitly re-deferred with a reason.
  3. The prod bagkz bulk-replication checklist is updated to cover the Telegram template fixes + Suggest Replies channel-awareness (one bulk copy when dev is signed off; prod stays dormant this milestone).
**Plans**: TBD
**Flags**: DEVICE + USER-ASSISTED. No new v1.1 REQs (closeout phase).

## Progress

**Execution Order:**
Phases execute in numeric order: 3 → 4 → 5 → 6 → 7 → 8. Phases 3 and 4 are user-assisted gates and can overlap the non-gated parts of Phase 5 (URL/seam/parser work); the media Normalize port waits on Phase 3 samples.

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Polished Suggestions Panel on Mock Data | v1.0 | 4/4 | Complete | 2026-06-25 |
| 2. n8n Live Wiring | v1.0 | 4/4 | Complete | 2026-07-10 |
| 3. tapi Live-Shape Capture | v1.1 | 1/1 | Complete    | 2026-07-12 |
| 4. n8n Telegram Template Parity (dev) | v1.1 | 2/2 | Verifying (TPL-06 owner gate open) | - |
| 5. Channel-Aware ChatManager Core | v1.1 | 0/TBD | Not started | - |
| 6. Channel Switcher UI | v1.1 | 0/TBD | Not started | - |
| 7. «Вместе» Suggestions + Dashboard on Telegram | v1.1 | 0/TBD | Not started | - |
| 8. Device UAT + Milestone Closeout | v1.1 | 0/TBD | Not started | - |
