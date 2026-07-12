# Requirements: Automation — v1.1 Telegram Parity

**Defined:** 2026-07-12
**Core Value:** The owner stays in control along the automation↔semi-auto spectrum — on either channel.
**Design spec:** `docs/superpowers/specs/2026-07-12-telegram-parity-design.md`

## v1.1 Requirements

### Shape Verification (user-assisted gate)

- [x] **VER-01
**: Owner can run `Tools/tapi/capture-shapes.sh` (read-only; token stays local) against an authorized dev Telegram profile and produce sanitized samples in `Tools/tapi/samples/`
- [x] **VER-02
**: The 13 open tapi shape questions (`.planning/research/telegram-parity/tapi-shapes.md` §11) each get a recorded verdict (SHAPES.md), including the reactions-receive go/no-go decision

### Telegram Chat Client (CHAT)

- [x] **CHAT-01
**: Owner can see a Telegram-authed bot's chat list with names, avatars, unread counts, and correct timestamps (tapi `chats/filter`; `last_time` fallback; `type=="chat"` groupness)
- [x] **CHAT-02
**: Owner can open a Telegram chat and read paginated history (text renders via `type:"text"` mapping; numeric chat ids never sliced/crash)
- [ ] **CHAT-03**: Telegram media messages (image, video, voice, document — sticker/GIF per capture verdicts) render with thumbnails, durations, and downloads per captured shapes
- [x] **CHAT-04
**: Owner can send text to a Telegram chat (tapi `message/send`), with outbox retry rebuilding the correct channel URL
- [x] **CHAT-05
**: Owner can send media (image/video/document) to a Telegram chat (channel-aware `WappiMediaRequestFactory`)
- [ ] **CHAT-06**: Owner can send a quoted reply in a Telegram chat (dedicated tapi `message/reply` endpoint)
- [ ] **CHAT-07**: Incoming Telegram replies render quoted cards (snapshot + `messages/id/get` recovery)
- [x] **CHAT-08
**: Owner can send/remove emoji reactions in Telegram chats (recipient-required tapi body)
- [ ] **CHAT-09**: Opening an unread Telegram chat marks it read on tapi (no `mark_all` query)
- [x] **CHAT-10
**: Swipe-to-delete is hidden on the Telegram channel (no tapi endpoint); WhatsApp behavior unchanged
- [x] **CHAT-11
**: WhatsApp and Telegram caches for one bot are isolated (`BotCache/{botId}/` vs `BotCache/{botId}/telegram/`); cached chats open offline per channel; purge/privacy clears cover both

### Channel Switcher (SWITCH)

- [ ] **SWITCH-01**: Owner can flip between the active bot's WhatsApp and Telegram chats via a TopBar segmented control (full list reset choreography, mid-flight-safe)
- [ ] **SWITCH-02**: An unconnected channel's chip is visibly muted; tapping it shows that channel's empty state with a connect CTA (no more permanent "WhatsApp not connected" dead end for Telegram-only bots)
- [ ] **SWITCH-03**: Last-used channel persists per bot across restarts; a bot with only one connected channel auto-selects it
- [ ] **SWITCH-04**: Telegram bottom-nav tab and the Screen_Telegram placeholder are removed; tab 0 reads «Чаты»

### n8n Telegram Template (TPL)

- [x] **TPL-01
**: A Telegram bot's replies actually arrive in Telegram — template outbound nodes on tapi bases (`message/send`, `message/mark/read`, `chats/typing/start`)
- [x] **TPL-02
**: Telegram text messages route through the AI agent (Switch matches `type:"text"`)
- [x] **TPL-03
**: Voice input works: transcription download + humanizer pause use `length_seconds` fallback
- [x] **TPL-04
**: Telegram session memory keys on `profile_id + ':' + chatId` (stable vs username-y `from`)
- [x] **TPL-05
**: Files uploaded before a channel's auth become RAG-retrievable once that channel's workflow is created (re-stamp of `"-1"` sentinel metadata in BOTH Create orchestrators)
- [x] **TPL-06
**: Dev e2e proof with a real Telegram profile via tunnel (clone active only during the test window)

### Telegram Auth (TGAUTH)

- [x] **TGAUTH-01
**: 2FA-protected Telegram accounts can authorize — `detail:"2fa"` handled in both code and QR flows with a cloud-password step (`tapi/sync/auth/2fa`)

### «Вместе» Suggestions (SUGG)

- [ ] **SUGG-01**: Suggestions populate for Telegram chats — provider payload carries channel-appropriate profile/workflow ids + `channel` field (additive v1.1 contract)
- [ ] **SUGG-02**: Telegram suggestions are RAG-grounded via the `botTgId` metadata filter (channel-branched vector-store node; single-key invariant)

### Dashboard (DASH)

- [ ] **DASH-01**: «Сводка» counts and lists Telegram conversations (telegram profile ids in the POSTed list + profile→bot map)
- [ ] **DASH-02**: Bot filter chips are bot-level — a dual-channel bot gets ONE chip covering both profiles
- [ ] **DASH-03**: A Telegram outcome row deep-links to that Telegram chat (channel-aware SetActiveBot/channel/SelectChat)

## v2 Requirements

- **TG-REACT-RECV**: Render incoming Telegram reactions (only if capture shows a viable transport; else stays out)
- **SUPPRESS-01**: Server-side «Вместе» suppression (carried deferred item)
- **PROD-01**: Prod bagkz bulk replication (all dev workflows incl. Telegram fixes)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Official Telegram business-bots path | PARKED — client-side Premium paywall kills try-before-buy (memory `telegram-channel-strategy`) |
| WhatsApp template sessionKey change | Session-continuity risk for shipped bots outweighs symmetry |
| Reactions-receive on Telegram (if no transport) | tapi documents no `type:"reaction"` message; go/no-go recorded in VER-02 |
| Telegram post-auth sync window | Not written at auth; add only if device testing shows tapi needs one |
| `WappiUnitySync.cs` port | Dead code (no references); no tapi `messages/all/get` exists |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| VER-01 | Phase 3 | Complete |
| VER-02 | Phase 3 | Complete |
| CHAT-01 | Phase 5 | Complete |
| CHAT-02 | Phase 5 | Complete |
| CHAT-03 | Phase 5 | Pending |
| CHAT-04 | Phase 5 | Complete |
| CHAT-05 | Phase 5 | Complete |
| CHAT-06 | Phase 5 | Pending |
| CHAT-07 | Phase 5 | Pending |
| CHAT-08 | Phase 5 | Complete |
| CHAT-09 | Phase 5 | Pending |
| CHAT-10 | Phase 5 | Pending |
| CHAT-11 | Phase 5 | Complete |
| TGAUTH-01 | Phase 5 | Pending |
| SWITCH-01 | Phase 6 | Pending |
| SWITCH-02 | Phase 6 | Pending |
| SWITCH-03 | Phase 6 | Pending |
| SWITCH-04 | Phase 6 | Pending |
| TPL-01 | Phase 4 | Complete |
| TPL-02 | Phase 4 | Complete |
| TPL-03 | Phase 4 | Complete |
| TPL-04 | Phase 4 | Complete |
| TPL-05 | Phase 4 | Complete |
| TPL-06 | Phase 4 | Complete |
| SUGG-01 | Phase 7 | Pending |
| SUGG-02 | Phase 7 | Pending |
| DASH-01 | Phase 7 | Pending |
| DASH-02 | Phase 7 | Pending |
| DASH-03 | Phase 7 | Pending |

**Coverage:**
- v1.1 requirements: 29 total (enumerated REQ-IDs; the earlier "24 total" header was a miscount — CHAT has 11 items + SUGG/DASH add 5)
- Mapped to phases: 29 ✓
- Unmapped: 0 ✓
- No orphans, no duplicates. Phase 8 (Device UAT + Closeout) owns no v1.1 REQ by design — it closes carried v1.0 deferred UAT + the prod-replication checklist.

---
*Requirements defined: 2026-07-12*
*Last updated: 2026-07-12 (roadmap traceability — 29/29 mapped to Phases 3-8)*
