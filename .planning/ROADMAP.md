# Roadmap: Automation — WhatsApp/Telegram AI Bot Manager

## Overview

v1.0 shipped the semi-auto «Вместе» reply path on WhatsApp. v1.1 Telegram Parity brings the Telegram channel (Wappi tapi) up to the same bar: a Telegram-authed bot gets a working chat client, live n8n auto-replies, «Вместе» suggestions, and dashboard inclusion — all on the existing single-scene, single-`ChatManager` architecture. The journey is dependency-driven (design spec §6): a small user-assisted **shape-capture gate** goes first so parser work builds against real tapi JSON, not guesses; **n8n template fixes** run independently on dev; the **channel-aware ChatManager core** is the largest phase and unblocks the **switcher UI**; **suggestions + dashboard** light up last (dashboard is the explicit cut line); and a **device-UAT closeout** carries in the v1.0 deferred UAT and updates the prod-replication checklist (prod bagkz stays dormant). Two phases are user-assisted gates (`secrets.json` is deny-ruled for Claude): live shape capture and the n8n e2e proof both require the owner to drive a real dev Telegram profile.

## Milestones

- ✅ **v1.0 Reply Suggestions** — Phases 1-2 (shipped 2026-07-11)
- 🚧 **v1.1 Telegram Parity** — Phases 3-8 (in progress)
- 📋 **v1.2 Reply-Trigger Discipline** — Phases 9-10 (planned; specs + plans committed, starts after v1.1 closes)

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
- [x] **Phase 4: n8n Telegram Template Parity (dev)** - Telegram_Bot template onto tapi bases (outbound URLs, `type:"text"`, sessionKey, voice duration) + RAG re-stamp on late channel auth; proven e2e against a real dev Telegram profile via tunnel. (completed 2026-07-12)
- [x] **Phase 5: Channel-Aware ChatManager Core** - The channel seam (`ChatChannel`, `SetActiveChannel`, `WappiEndpoints` builder, per-channel caches), all tapi parser/send divergences, and the Telegram 2FA auth fix — WhatsApp behavior unchanged, full suite green. (all 7 plans complete 2026-07-14; capture-gated 05-06 shipped media/reactions-receive/reply per SHAPES.md verdicts; gap-closure 05-07 shipped the .tgs/кружок/GIF presentation treatments off the device-UAT gaps; off-plan round-2 polish 05-08 made the video note bubble-free + the .tgs a sized placeholder card after an owner device screenshot; 1007/1007 EditMode green)
- [x] **Phase 6: Channel Switcher UI** - In-screen TopBar segmented WhatsApp|Telegram control with muted/connect affordances, per-bot channel persistence, and removal of the Telegram bottom tab. (code-complete 2026-07-13; owner visual UAT gate open in 06-HUMAN-UAT.md)
- [x] **Phase 7: «Вместе» Suggestions + Dashboard on Telegram** - Channel-aware suggestions payload + channel-branched RAG filter, and «Сводка» Telegram inclusion (bot-level chips, channel-aware deep-link). Dashboard is the milestone's cut line. (code-complete 2026-07-13; 916/916 EditMode green; live TG grounding proof owner-gated in 07-HUMAN-UAT.md)
- [ ] **Phase 8: Device UAT + Milestone Closeout** - On-device end-to-end Telegram pass (incl. carried v1.0 deferred UAT) + prod-replication checklist update; prod bagkz stays dormant. (Gate A run 2026-07-16 → ISSUES; defects D1–D9 in gap closure, plans 08-04..08-10)

### 📋 v1.2 Reply-Trigger Discipline (Planned)

- [ ] **Phase 9: Semi-Auto Suppression Flag** - Wire the «Вместе» toggle to the server: `reply_mode_flags` table + `/webhook/SetReplyMode` sync + a fail-closed gate in both bot templates so a semi-auto chat gets no auto-reply while suggestions still work.
- [ ] **Phase 10: Message Batching / Debounce** - A pre-generation debounce+dedupe stage in both bot templates so a multi-fragment customer message gets ONE combined reply, plus a client-side debounce in `SuggestionsController.HandleLive` so suggestions coalesce the same way.

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
**Plans**: 7 plans
Plans:
- [x] 05-01-PLAN.md — Foundations: ChatChannel enum, WappiEndpoints URL builder, ChatIdFormat (recipient/display-fallback/isGroup), channel-parameterized tab-state resolver, OutboxEntry.channel + full EditMode coverage (wave 1)
- [x] 05-02-PLAN.md — ChatManager identity seam: ActiveChannel + persistence, SetActiveChannel reset choreography, OnActiveChannelChanged, channel-aware GetActiveProfileId/GetCacheRoot/empty-state/sync-gate, channel resolution, EmptyStateView Telegram copy (wave 2)
- [x] 05-03-PLAN.md — URL builder wiring (8 literals) + tapi parser divergences: ChatDialog last_time/type, "text"→Chat, DisplayFallback, groupness, delivery-tick mapping, Telegram delete guard (wave 3)
- [x] 05-04-PLAN.md — Send-path branches: tapi message/reply, reaction recipient, mark-read no-mark_all body, channel-aware media EndpointFor, outbox channel snapshot + retry rebuild (wave 4)
- [x] 05-05-PLAN.md — Telegram 2FA auth fix (TGAUTH-01): pure TelegramAuthResponseParser + detail:"2fa" cloud-password branch in code + QR flows, tapi/sync/auth/2fa (wave 1, independent — Manager.cs only)
- [x] 05-06-PLAN.md — CAPTURE-GATED (autonomous:false, depends on all): media Normalize port (body:null + s3Info:{} → download-by-id; media_info dims/duration; document+video/mp4 → Video), reactions-RECEIVE (GO — reactions[] on every message → Normalize-time map + reconcile refresh), "channel" groupness, reply Q8 lock (no echo bug), name/isDeleted verdict-resolved (no change) — capture gate opened 2026-07-13; 957/957 EditMode green (wave 5)
- [x] 05-07-PLAN.md — GAP CLOSURE (05-HUMAN-UAT device gaps): .tgs (application/x-tgsticker) → Sticker + borderless placeholder «Стикер» (no futile download); video note via pure IsVideoNote heuristic (square + video.mp4 + ≤60s, is_round ignored) → circular bubble + duration badge; GIF (isGif 4-layer flag) → "GIF" corner badge — all minted inside the Telegram gate, WhatsApp byte-identical; 988/988 EditMode green (executed 2026-07-14)
- [x] 05-08 (off-plan round-2 polish, no PLAN.md) — device-UAT follow-up on an owner screenshot after 05-07: (1) video note floats BUBBLE-FREE via a pure BubbleTransparencyPolicy.IsTransparent seam + isVideoNote (circle no longer inside the green bubble; time stays readable on the existing media overlay); (2) .tgs sticker renders a deliberate sticker-slot-sized (396²) neutral rounded CARD with its own fill + «Стикер» caption + mid-gray glyph (the 05-07 white-silhouette placeholder was invisible on the transparent bubble). Telegram-only, WhatsApp byte-identical; 1007/1007 EditMode green (997+10), commits 72a5909 + a27cf16 (executed 2026-07-14)
**Flags**: CAPTURE-GATED — 05-06 blocked on the Phase-3 owner capture run (SHAPES.md verdicts); RESOLVED 2026-07-13 (owner ran capture-shapes.sh, all verdicts recorded), executed 2026-07-14. Q2 media re-run done 2026-07-14 (sticker/note/GIF observed → 05-HUMAN-UAT gaps → closed by 05-07, refined by 05-08); on-device visual confirmation of the treatments (incl. the 05-08 note-float + sticker-card) rides Phase 8.
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
**Plans**: 2 plans
Plans:
- [x] 06-01-PLAN.md — Runtime: ChannelSwitcherModel (pure) + ChannelSwitcherView binder + tab-index-shift audit (BotsTabIndex 3→2) + EditMode tests (SWITCH-01/02/03, SWITCH-04 audit half)
- [x] 06-02-PLAN.md — ChannelSwitcherBuilder (pill into CenterZone) + nav restructure (remove Telegram tab, «Чаты», delete Screen_Telegram) + run-editor-builder.sh headless run + immediate scene commit + 06-HUMAN-UAT.md (SWITCH-01/04)
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
**Plans**: 2 plans
Plans:
- [x] 07-01-PLAN.md — Channel-aware «Вместе» payload (additive `botTgId` + `channel`, channel-resolved profile id; `botWaId` always) + pure channel-selection matrix tests + SUGG live-verification note (client half; server RAG branch shipped Phase 4) (SUMMARY 2026-07-13; 908/908 EditMode green; SUGG-01/02 client half; live TG grounding rides TPL-06 in 07-HUMAN-UAT.md)
- [x] 07-02-PLAN.md — «Сводка» Telegram inclusion: pure both-channel profile map (`DashboardProfileMap`), bot-level chips (`FilterByProfiles` set semantics; dual-channel bot ⇒ one chip), channel-aware row deep-link (`SetActiveBot`→`SetActiveChannel`→`SwitchTab`→`SelectChat`) + tests (SUMMARY 2026-07-13; 916/916 EditMode green; DASH-01/02/03; server contract + Main.unity untouched)
**UI hint**: yes

### Phase 8: Device UAT + Milestone Closeout
**Goal**: The whole Telegram parity milestone is validated on a real device end-to-end and the prod-replication path is documented — carrying in the v1.0 deferred device UAT — with prod bagkz still dormant.
**Depends on**: Phases 3-7 (the full Telegram surface must exist).
**Requirements**: none new — closeout of carried items (v1.0 deferred device UAT + prod-replication checklist)
**Success Criteria** (what must be TRUE):
  1. A Telegram-authed bot is exercised on-device end-to-end — auth (incl. 2FA), chat list/history/media, send/reply/reaction, channel switch, auto-reply, «Вместе», dashboard — with results recorded in a HUMAN-UAT doc.
  2. The carried v1.0 deferred device-UAT scenarios (Phases 01–02) are run or explicitly re-deferred with a reason.
  3. The prod bagkz bulk-replication checklist is updated to cover the Telegram template fixes + Suggest Replies channel-awareness (one bulk copy when dev is signed off; prod stays dormant this milestone).
**Plans**: 3 plans + 7 gap-closure plans (08-04..08-10, from Gate-A defects D1–D9)
Plans:
- [ ] 08-01-PLAN.md — Consolidated owner-run device-UAT runbook (`08-DEVICE-UAT.md`) aggregating every open milestone gate (auth/2FA, chat+media incl. the 05-07/08 treatments, 05-09 fixes, vthumb probe, switcher, auto-reply e2e, live «Вместе»+dashboard, carried v1.0); owner-run gate (Gate A RUN 2026-07-16 → ISSUES, D1–D9 filed)
- [ ] 08-02-PLAN.md — Prod bagkz replication runbook (`08-PROD-REPLICATION.md`) + prod-targetable verify/deployer tweaks; one-shot bulk copy, templates INACTIVE, header-auth follow-up flagged
- [ ] 08-03-PLAN.md — Milestone-close prep (`08-MILESTONE-CLOSE.md`): gated checklist + carried-forward roll-forward, points at `/gsd-complete-milestone`
- [x] 08-04-PLAN.md — [gap] D5 (high): open-chat live poll — incoming renders in the open chat + «Вместе» refresh + fresh suggestions payload, BOTH channels (wave 1) (SUMMARY 2026-07-16; pure OpenChatLivePollGate 3s + self-gating OpenChatLivePollRoutine reusing SyncLatestMessages — no new messages/get caller; foreground-gated; re-kicked after StopAllCoroutines in SetActiveBot/SetActiveChannel; cascades to bubbles + cards + payload with zero wiring change; 1043/1043 EditMode green FRESH; commits 5990ab1/d45f4ed/a6a708c; device re-verify rides 08-10)
- [x] 08-05-PLAN.md — [gap] D7 (high): TG service-dialog dedup (`ChatIdFormat.CanonicalKey`) + cross-channel cache isolation (wave 2, depends 08-04) (SUMMARY 2026-07-16; diagnosis-first — (a) two id-forms of the 777000 service dialog: read-only capture shows ONLY bare 777000, the twin is server/device-side [no in-app @c.us appender], leading hypothesis 777000@c.us, EXACT form to confirm at 08-10; (b) bleed = a pre-CHAT-11 legacy WhatsApp cache file still holding the TG dialog [today's sync path is channel-correct]. Fix: pure ChatIdFormat.CanonicalKey(id, channel) [WhatsApp VERBATIM/byte-identical; Telegram strips a spurious @c.us/@g.us twin] + IsForeignToChannel(id, channel) [WhatsApp drops a bare no-'@' Telegram-form id — '@'-test keeps every genuine WA jid; Telegram never rejects]; ParseChatsJson keys chatLookup/serverIds/isDeleted/merge by the canonical id AND constructs the surviving VM with it [vm.ChatId==key ⇒ bare tapi id on TG, byte-identical on WA, every downstream chatLookup consumer resolves unchanged]; GetChat+ShouldNotify+DisplayFallback+IsGroup route through it. WhatsApp byte-identical; 1091/1091 EditMode green FRESH [1065+26]; commits f379a5f/cc04503/1c9d8fe; device re-verify D7 + EXACT twin-form capture ride 08-10)
- [x] 08-06-PLAN.md — [gap] D1+D2 (medium): Telegram allowed-reaction set + clean 400 revert + reaction-removal tombstone (wave 1) (SUMMARY 2026-07-16; pure TelegramReactionCatalog allowed-set + VS16-normalizing IsAllowed + TG-safe quick 6 [😂→😁/😮→🔥] + FilterCategories; channel-gated bar/picker; PostReactionRoutine reverts pill+preview on 400; D2 path (b) confirmed → empty-emoji "me" tombstone [StampRemovalTombstone] + Merge removal branch suppresses the stale echo within grace; WhatsApp byte-identical; 1063/1063 EditMode green FRESH [1043+20]; commits 7fb0445/bdec75c/b0ea849/8d5e5aa; device re-verify B9/B13 rides 08-10)
- [x] 08-07-PLAN.md — [gap] D3 (medium): incoming video-note bubble-free transparency + duration-badge rounded-corner refresh (wave 1) (SUMMARY 2026-07-16; diagnosis-first root cause (ii): incoming кружок IS isVideoNote [round crop proves it] but is placeholder-first [tapi body:null+s3Info:{} → download-by-id] unlike the outgoing note 05-08 verified [inline s3Info.url], so isPlaceholderActive stays true under the visible circle → opaque "white bubble"; fixed at the transparency call site — UpdateBubbleVisuals treats a note whose round media surface is showing as non-placeholder so the circle floats, card-only states keep the opaque retry card; D3a badge corners refreshed after layout via RefreshCorners + one-frame-deferred RefreshBadgeCornersDeferred; pure BubbleTransparencyPolicy/heuristic seams untouched, no ChatManager.cs edit, WhatsApp byte-identical; 1063/1063 EditMode green FRESH; commits 161b540 [D3b] + 89479db [D3a]; device re-verify E1 float + B5 badge corners rides 08-10)
- [x] 08-08-PLAN.md — [gap] D4+D6 (medium): SwipeToDelete lifecycle null-guard (no bot-creation NRE) + TG swipe affordance removed (wave 1) (SUMMARY 2026-07-16; D6: lazy Rt accessor + `_scroll ??=` back every deref so ChatItemView.Bind→ResetClosed→SetContentX no longer NREs when a fresh-sync row is Instantiated+Bound before Awake [inactive list panel]; Awake still assigns on the normal path, channel-agnostic. D4: pure ChatRowSwipePolicy.Enabled [WhatsApp-only, mirrors ActiveChannelSupportsChatDelete] — Bind gates the affordance per ActiveChannel: Telegram snaps shut + disables SwipeToDelete [no drag callbacks] + hides the red button; WhatsApp byte-identical [added enabled=true/SetActive(true) are idempotent no-ops]; 2 ChatRowSwipePolicy tests; 1065/1065 EditMode green FRESH [1063+2]; commits a12f467/fea922d/991bd2f; device re-verify D4/D6 rides 08-10)
- [ ] 08-09-PLAN.md — [gap] D8+D9 (low): RU empty-state copy + Telegram chat-list sync indicator (wave 3, depends 08-05)
- [ ] 08-10-PLAN.md — [gap] Consolidated owner device re-verify of D1–D9 on one build (checkpoint:human-verify; wave 4, depends 08-04..08-09)
**Flags**: DEVICE + USER-ASSISTED. No new v1.1 REQs (closeout phase). Gap-closure round in progress (D1–D9).

### Phase 9: Semi-Auto Suppression Flag (v1.2)
**Goal**: When a chat is in «Вместе» (semi-auto), the bot's autonomous n8n reply workflow stands down for that chat — no auto-reply, message stays unread, suggestions panel still works — identically on WhatsApp and Telegram. The «Бот работает/пауза» activation switch is untouched.
**Depends on**: v1.1 close (milestone boundary only — technically independent: the channel seam (`ChatChannel`/`ProfileIdForChannel`) landed in Phase 5 and the tapi'd Telegram template in Phase 4). Needs dev n8n + tunnel + real WA/TG profiles for the e2e gates.
**Requirements**: SUP-01, SUP-02, SUP-03, SUP-04, SUP-05 (to be formalized in the v1.2 REQUIREMENTS.md at milestone start; definitions locked in `09-CONTEXT.md`)
**Success Criteria** (what must be TRUE):
  1. A chat flipped to «Вместе» gets NO auto-reply and stays unread while the suggestions panel still populates; flipping back to «Авто» restores auto-replies — proven on BOTH channels (owner dev e2e).
  2. The bot-wide default (`'*'` row) suppresses never-opened chats when the bot default is «Вместе»; a per-chat override beats the default; absence of any row → the bot replies (a never-toggled chat is never silenced).
  3. The gate is fail-closed with zero extra error wiring (a genuine Postgres read error halts the reply), and the app re-asserts the flag on chat open so a lost write self-heals.
  4. A freshly created bot inherits the gate via template cloning (verified on a new bot's cloned workflow); existing dev clones recreated.
  5. EditMode payload/hook tests green; n8n curl matrix (upsert, precedence, absence→reply, malformed→clean error) green.
**Plans**: TBD (near-executable task breakdown already exists: `docs/superpowers/plans/2026-07-13-semi-auto-suppression-flag.md`)
**Flags**: USER-ASSISTED e2e (dev n8n + tunnel + real profiles; bot clones active only during test windows). Template-change propagation: recreate dev clones; folds into the prod bagkz bulk copy. Out of scope: message batching/debounce (own design → Phase 10).

### Phase 10: Message Batching / Debounce (v1.2)
**Goal**: A customer's multi-fragment message gets ONE combined auto-reply (not one per fragment), and «Вместе» suggestions coalesce the same way — a pre-generation debounce+dedupe+combine stage in both bot templates, and a debounce timer in the suggestions client.
**Depends on**: Phase 9 (edits the same reply-path region; the suppression gate must sit BEFORE the debounce). Needs dev n8n + tunnel + real WA/TG profiles for the e2e gates.
**Requirements**: BATCH-01, BATCH-02, BATCH-03 (to be formalized in the v1.2 REQUIREMENTS.md; definitions locked in `10-CONTEXT.md`)
**Success Criteria** (what must be TRUE):
  1. Two+ text fragments sent within the debounce window produce exactly ONE bot reply whose agent input is the concatenation; aborted fragments never generate (verified via execution runData: earlier fragments abort at Is Latest?). Proven on BOTH channels (owner dev e2e).
  2. A single complete message still gets one reply after the window; a bot/owner reply between fragments bounds the combined run; a new chat with no prior bot reply combines within the fetch limit.
  3. The debounce sits AFTER the Phase-9 suppression gate (a semi-auto chat skips the whole path — no wait); the humanizer pauses are unchanged.
  4. Suggestions coalesce: rapid incoming fragments issue exactly ONE live request after the ~2.5s client window; manual refresh and card-pick re-cluster still fire immediately.
  5. EditMode debounce-gate test green (rapid incomings → one request; manual refresh immediate); n8n curl matrix (two fragments → one combined reply; single message → one reply; bot-reply boundary) green.
**Plans**: TBD (design: `docs/superpowers/specs/2026-07-15-message-batching-debounce-design.md`)
**Flags**: USER-ASSISTED e2e (dev n8n + tunnel + real profiles). Adds ~window-length latency to EVERY auto-reply (the tuning knob). v1 batches text only (media message = own trigger). Same both-template propagation + bulk-copy story as Phase 9.

## Progress

**Execution Order:**
Phases execute in numeric order: 3 → 4 → 5 → 6 → 7 → 8. Phases 3 and 4 are user-assisted gates and can overlap the non-gated parts of Phase 5 (URL/seam/parser work); the media Normalize port waits on Phase 3 samples.

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Polished Suggestions Panel on Mock Data | v1.0 | 4/4 | Complete | 2026-06-25 |
| 2. n8n Live Wiring | v1.0 | 4/4 | Complete | 2026-07-10 |
| 3. tapi Live-Shape Capture | v1.1 | 1/1 | Complete    | 2026-07-12 |
| 4. n8n Telegram Template Parity (dev) | v1.1 | 2/2 | Complete    | 2026-07-12 |
| 5. Channel-Aware ChatManager Core | v1.1 | 7/7 | Complete    | 2026-07-14 |
| 6. Channel Switcher UI | v1.1 | 2/2 | Complete    | 2026-07-13 |
| 7. «Вместе» Suggestions + Dashboard on Telegram | v1.1 | 2/2 | Complete    | 2026-07-13 |
| 8. Device UAT + Milestone Closeout | v1.1 | 4/10 | Gap closure (D1–D9) | - |
