# Telegram Channel Parity — Design

**Date:** 2026-07-12
**Status:** Approved for planning (autonomous session — decisions made with evidence, veto points flagged in §9)
**Strategy context (locked, do not re-litigate):** Telegram ships on Wappi tapi (`https://wappi.pro/tapi/...`), same architecture as WhatsApp. The official Telegram business-bots path is PARKED (client-side Premium paywall, no trial — see memory `telegram-channel-strategy`).
**Research artifacts:** `.planning/research/telegram-parity/` — 7 deep-read reports (chatmanager-pipeline, auth-bot-entity, suggestions-vmeste, n8n-templates, ui-scaffolding, dashboard-scope, tapi-shapes) + extracted Wappi docs text (`tg-docs.txt`, `wa-docs.txt`, source: wappi.pro/telegram-api-documentation, fetched 2026-07-12). The 13 live-verify items are enumerated in `tapi-shapes.md` §11.

## 1. Goal

Bring the Telegram channel to full parity with WhatsApp: a Telegram-authed bot gets a working chat client (list, messages, media, send, replies, reactions where supported), working n8n auto-replies, «Вместе» suggestions, and dashboard inclusion — all on the existing single-scene, single-`ChatManager` architecture.

What already works (verified 2026-07-12, do NOT redo): tapi auth QR + phone/code (`Manager.cs` — all coroutines live, none stubbed), Bot entity Telegram fields + PlayerPrefs, n8n workflow lifecycle (create/edit/enable/delete with sentinel guards), activation toggle fires both channels, `PendingProfileLedger` + hourly orphan sweep cover tapi.

## 2. Decisions

### D1 — Chat UI: in-screen channel switcher (NOT a separate screen)

A segmented WhatsApp|Telegram pill in the chats-screen TopBar `CenterZone` (currently an inactive 360×140 slot; visual precedent = the neighboring `ModeToggle` pill). The Telegram bottom-nav tab (`tabs[1]` → empty pink `Screen_Telegram` placeholder) is removed; tab 0 is relabeled «Чаты». `Screen_Telegram` is deleted from the scene and from `NavRestructureBuilder.ReorderScreens`.

Evidence for (a) over a separate screen:
- `ChatManager` is a hard singleton whose serialized panels point at Screen_Whatsapp's ChatsPanel/MessagesPanel; every view binds to its events. A second screen duplicates 2 panel subtrees + ~7 overlay panels and collides with `ChatManager.Instance`, `SwipeToBack.Instance`, and `FindFirstObjectByType`-bound `BotSwitcherSheet`.
- `SetActiveBot` (ChatManager.BotState.cs:104-129) is a proven full "swap data source, reuse all views" reset; `SetActiveChannel` is a near-clone. Bot switching — a strictly bigger swap — already happens in-screen via a bottom sheet.
- The dominant cost (channel-parametrizing ~11 URL sites, profile resolution, cache keys, empty states) is identical under both options; the separate screen adds only liabilities.

Switcher behavior:
- Both chips always visible. The unconnected channel's chip renders muted with a "not connected" affordance; tapping it shows that channel's empty state with the connect CTA (reuses `EmptyStateView` CTA → BotSettings). This keeps Telegram discoverable.
- Active channel persisted per bot: PlayerPrefs `{botId}ActiveChatChannel` (companion to `LastSelectedBotForChats`). On bot switch, restore that bot's last channel; if the persisted channel is unconnected but the other one is, auto-select the connected one.
- `ModeToggle` (Авто/Вместе) stays put and applies to both channels (D3).

### D2 — Dashboard «Сводка»: include Telegram, sequenced last

Server: zero changes — `Dashboard_Outcomes` keys sessions by `split_part(session_id, ':', 1)` and both bot templates write the identical `profile_id:chatId`-style session key (byte-identical memory node expression). Client: `AuthedProfiles()` + `ProfileToBot()` gain `telegramProfileId` (~6 lines) for aggregates; the real work is (a) bot-level chip filtering (today one chip per profileId → dual-channel bots get two same-named chips that each hide half the rows) and (b) channel-aware deep-link (today hardwired to the WhatsApp tab). Both need the channel concept, so this phase runs after the chat surface ships. If the milestone must shrink, this phase is the cut line.

### D3 — «Вместе» suggestions: additive v1.1 contract + channel-branched RAG

- Client (`N8nSuggestionsProvider.cs:75-76`, the only channel-bound lines above the seam): select profile/workflow id by the open chat's channel; add `channel: "whatsapp"|"telegram"` and `botTgId` fields to the payload. `botWaId` stays populated (backward compat; server Prep tolerates unknown fields).
- Workflow (`9PTyYcelRQI7bGDb`): Prep validates `channel` + computes `skipRag` from the channel-appropriate workflow id; an If-branch routes to one of two vector-store nodes — metadata filter `botWaId` vs `botTgId` (preserves the single-key match-filter invariant). Assemble prompt line «…со своего WhatsApp» → channel-neutral.
- RAG data already supports this: Upload File stamps every chunk with BOTH `botWaId` and `botTgId`.
- Per-bot «Вместе» default (`{botName}ReplyMode`) stays bot-scoped across channels; per-chat overrides (`{botId}_semiAuto_{chatId}`) can't collide — id namespaces are disjoint. No storage change.
- Server-side «Вместе» suppression remains a separate deferred item (carried from v1.0) — NOT part of this milestone.

### D4 — Channel seam in ChatManager (architecture)

- `ChatChannel` enum (`WhatsApp`, `Telegram`) + `ChatManager.ActiveChannel` alongside `CurrentBotId`. `SetActiveChannel(channel)` clones the `SetActiveBot` reset choreography (stop coroutines, clear list/queues/gates, `_outbox = null`, `BeginLoadForActiveBot`); if a chat is open, route through `ShowChatList` first.
- `WappiEndpoints.Sync(channel, pathAndQuery)` static builder → `https://wappi.pro/{api|tapi}/sync/{path}` replaces the 11 hardcoded literals (ChatManager.cs:391, 525, 1102, 1175, 1812, 1933, 2023; DeleteChat.cs:52; ReactionSend.cs:66; ReactionResolve.cs:74; QuoteResolve.cs:96) + `WappiMediaRequestFactory.EndpointFor(kind, profileId, channel)`.
- `GetActiveProfileId()` body → channel switch over `whatsappProfileId`/`telegramProfileId` (same `IsValidProfileId` guard, zero call-site churn).
- Cache: `GetCacheRoot()` → WhatsApp keeps the legacy root `BotCache/{botId}/` (no migration); Telegram uses `BotCache/{botId}/telegram/`. This channel-scopes chats.json, messages/, media/, outbox, and both resolver caches. `PurgeCacheForBot`/privacy clears already delete recursively — still correct.
- `OutboxStore.OutboxEntry` gains a `channel` field so cross-session retries rebuild the right URL (entries already snapshot `profileId`).
- Empty/tab states: `EmptyStateReason` gains `BotHasNoTelegram`; `WhatsAppTabStateResolver` generalizes to a channel-parameterized resolver (keep the pure-function shape; preserve existing test coverage). `EmptyStateView` gets per-channel copy + CTA. Sync-window key becomes channel-qualified; Telegram starts with NO post-auth sync window (not written at auth; add later only if device testing shows tapi needs one).
- Chat-id handling consolidates into one helper (`ChatIdFormat`): recipient normalization (strip `@c.us` only when the suffix is present — replaces `WappiRecipient.FromChatId`, `NormalizeRecipient`, and the crash-prone `chat.id[..^5]` display fallback at ChatManager.cs:288), and `IsGroup` (WhatsApp: `@g.us` suffix; Telegram: `ChatDialog.type == "chat"`).
- Concurrency guards (`_chatFetchesInFlight`, `CrossChatResponseGuard`, serial media/vthumb queues) are transport-agnostic — keep them for tapi (assume the Wappi crossing bugs apply until proven otherwise); reset on channel switch exactly as on bot switch.

### D5 — tapi divergences (confirmed from official docs, wappi.pro/telegram-api-documentation)

Handled in this design:
| Divergence | Handling |
|---|---|
| Text messages are `type:"text"` (WA: `"chat"`) | Add `"text"` case to `ParseMessageType` (safe — WA never sends it). Without this every Telegram text is dropped as Unknown. |
| Chat ids are bare numeric strings (no `@c.us`); doc example even shows `id:""` | `ChatIdFormat` helper (above); suffix-conditional stripping; display fallback never slices |
| Dialog `last_timestamp` is a NUMBER on tapi (string RFC3339 on WA); tapi's RFC3339 lives in `last_time` | Add `string last_time` to `ChatDialog`; parse: try `last_timestamp`, fall back to `last_time`. JsonUtility silently ignores type-mismatched bindings, so each channel populates exactly one — no channel flag needed |
| No `isGroup` on tapi; groupness = dialog `type:"chat"` | Add `type` to `ChatDialog`; `ChatIdFormat.IsGroup` |
| Replies: no documented `quoted_message_id` on tapi `message/send`; dedicated `POST tapi/sync/message/reply {body, message_id}` | Channel branch in `PostTextMessageRoutine` |
| Reactions: tapi requires `recipient` in the body | Channel branch in `PostReactionRoutine` (add recipient) |
| mark/read: no `mark_all` query documented on tapi | Channel branch: body `{message_id}` only for Telegram |
| `chat/delete` does not exist on tapi | Feature-gate: disable swipe-to-delete on the Telegram channel |
| Avatars: native (`dialog.thumbnail`, same fs.wappi.pro scheme) | Existing avatar path is channel-agnostic; `GreenApiAvatarFetcher` stays WhatsApp-only (it's dormant anyway) |
| Auth: `auth/code` can return `detail:"2fa"` → needs `POST tapi/sync/auth/2fa` | **Bug fix**: handle the 2fa branch in `SendTelegramCode` + QR flow, add a cloud-password входной step. Today 2FA-protected accounts (very common) cannot auth at all |
| Voice duration: webhook has flat `length_seconds`, no `media_info` | Template expression fallback; client Normalize per live capture |
| Delivery statuses add `pending`/`undelivered`/`error` | Map in `DeliveryTickFormatter` (minor) |
| Telegram resend-code | Plain re-request (already implemented); do NOT port the WhatsApp delete+recreate hack unless live testing shows FLOOD_WAIT issues |

Deferred pending live capture (§D6): media message shapes in `messages/get` (body vs s3Info vs attaches — decides the Normalize port), sticker/video-note/GIF type strings, incoming-reaction transport (no `type:"reaction"` documented on tapi — receive-side reactions may not ship in v1), group payload semantics, `isDeleted` stickiness, reply-snapshot echo bug existence.

### D6 — Live shape verification is a user-assisted gate

`secrets.json` is deny-ruled for Claude, so live probing is impossible from this session. Phase 1 delivers `Tools/tapi/capture-shapes.sh` — a read-only curl/jq script the OWNER runs locally (token never leaves their machine): profile list → authorized TG profile → chats/get + chats/filter + messages/get across chats containing each media type + messages/id/get on a reply → sanitized samples into `Tools/tapi/samples/`. The 13 MUST-VERIFY items from the docs research become a checklist; parser/Normalize work consumes the samples. Requires an authorized dev Telegram profile (owner authorizes one via the existing in-app flow if none exists).

### D7 — n8n changes (dev n8n only; prod bagkz stays dormant)

`Telegram_Bot.json` (template `4VN3gsFaC2HUYmcc`) — confirmed unpatched WhatsApp clone; fixes:
1. `message/send` :23, `message/mark/read` :268, `chat/typing/start` :313 → tapi bases (`tapi/sync/message/send`, `tapi/sync/message/mark/read`, `tapi/sync/chats/typing/start` — note plural "chats"; no typing/stop exists on tapi).
2. Both `Input type` Switch nodes: add `"text"` (keep `"chat"` — harmless).
3. `Listening Pause`: `media_info.duration` → fallback expression covering `length_seconds`.
4. Chat Memory sessionKey: `profile_id + ':' + from` → `profile_id + ':' + chatId` (Telegram `from` can be a username; `chatId` is stable). WhatsApp template untouched (session-continuity risk for shipped bots outweighs symmetry).
5. mark/read body/query per tapi docs.
6. Re-validate the group guard (`from == chatId`) against live capture.

Orchestrators (`CreateTelegramWorkflow`, `Edit_Telegram_Workflow`): already correct (tapi webhook registration, Vertical Prompt byte-parity with WhatsApp — md5-verified). One addition to BOTH Create orchestrators: **RAG re-stamp** — after creating the workflow, UPDATE `documents.metadata` chunks whose opposite-channel key is the `"-1"` sentinel for this bot (match by the known channel's workflow id), so files uploaded before a channel's auth become retrievable. (Unity must pass the other channel's workflow id in the create form for the match key.)

`Suggest_Replies` (`9PTyYcelRQI7bGDb`): per D3.

Invariants carried: keep both bot templates inactive; never change literal template ids; bodyless n8n POSTs need explicit `Content-Type: application/json`; bot workflow clones stay INACTIVE except during active testing (real contacts!).

## 3. Component inventory (what changes where)

Client (C#):
- `ChatManager.BotState.cs` — ActiveChannel, SetActiveChannel, channel-aware GetActiveProfileId/GetCacheRoot/BeginLoadForActiveBot/ComputeCurrentEmptyState/RefreshActiveBotChats, per-bot channel persistence.
- `ChatManager.cs` + partials (DeleteChat, ReactionSend, ReactionResolve, QuoteResolve, MediaSend, Outbox, VideoThumbs) — URL builder adoption, send/reply/reaction/markread branches, ParseMessageType `"text"`, ParseChatsJson id/time/group handling, delete-chat gate.
- `WappiEndpoints` (new), `ChatIdFormat` (new), `WappiMediaRequestFactory`, `WappiRecipient` (absorbed), `ChatDialog` (+`last_time`, `type`), `DeliveryTickFormatter`, `OutboxStore`.
- `WhatsAppTabState.cs` → channel-parameterized resolver; `EmptyStateView`, `SyncingView` — per-channel copy/wiring.
- `Manager.cs` — Telegram 2FA auth branch (`auth/2fa`), pass opposite workflow id into create-workflow forms (re-stamp).
- `N8nSuggestionsProvider.cs` — channel-aware ids + payload fields.
- `DashboardPage.cs`, `DashboardMetrics.cs`, `DashboardModels.cs` — telegram profiles, bot-level chips, channel-aware deep-link (last phase).
- New Editor builder: `ChatChannelSwitcherBuilder` (TopBar CenterZone pill) + `NavRestructureBuilder` update (remove Telegram tab, relabel tab 0, drop Screen_Telegram). Scene mutations committed immediately after each builder run (parallel-scene-clobber rule).

Tests: EditMode coverage for `WappiEndpoints`, `ChatIdFormat`, channel-parameterized tab-state resolver, `ParseMessageType`, ChatDialog time/group parsing (sample-driven), suggestions payload builder, outbox channel retry. Existing 787-test suite must stay green.

## 4. Data flow (Telegram, after parity)

```
wappi.pro/tapi/sync (profile = bot.telegramProfileId)
  └── chats/filter → ChatsResponse (JsonUtility; + last_time/type fields) → ChatViewModel
  └── messages/get → MessagesResponseRaw (JsonConvert) → RawMessage (type:"text"→Chat)
        └── Normalize (media branches per captured shapes) → NormalizedMessage
              └── MessageViewModel → existing views (unchanged)
Cache: BotCache/{botId}/telegram/{chats.json, messages/, media/, outbox_*}
n8n:   tapi webhook → Telegram_Bot clone (tapi outbound) → n8n_chat_histories (profile:chatId)
       → DashboardOutcomes (unchanged) → «Сводка»
```

## 5. Error handling

- Channel switch mid-flight: reuse the SetActiveBot reset semantics (kill coroutines, zero gates, clear queues) — the known-good pattern for exactly this class of race.
- Telegram-only bot on the chats tab: auto-selects Telegram; WhatsApp chip shows connect CTA (no more permanent "WhatsApp not connected" dead end).
- tapi endpoint gaps degrade explicitly: delete-chat hidden on Telegram; reactions-receive deferred if capture shows no transport; unknown message types keep the existing drop-silently behavior.
- 2FA: explicit UI step; wrong-password path re-prompts (shape verified in capture phase).

## 6. Phasing (GSD milestone)

1. **tapi live-shape capture** (user-assisted gate) — capture script + samples + SHAPES.md verdicts on the 13 open items. Blocks Normalize/media work; everything URL/seam-level can proceed in parallel.
2. **n8n Telegram parity (dev)** — template fixes, re-stamp nodes, Suggest Replies channel-awareness; e2e against a dev TG profile via tunnel.
3. **Channel-aware ChatManager core** — seam, URLs, parsers, send paths, caches, empty states, 2FA fix; EditMode tests.
4. **Channel switcher UI** — builders, nav restructure, empty-state copy; device-visual pass.
5. **«Вместе» + Dashboard Telegram** — provider payload, dashboard client work.
6. **Device UAT + closeout** — detailed device pass (incl. carried v1.0 deferred UAT), prod-replication checklist update (prod stays dormant).

## 7. Out of scope

- Official Telegram business-bots path (PARKED — revisit triggers in memory).
- Server-side «Вместе» suppression (separate deferred item).
- WhatsApp template sessionKey change, `WappiUnitySync.cs` (dead code), PlayerPrefs→DB migration, Manager.cs god-object breakup.
- Reactions-receive on Telegram IF live capture shows no viable transport (decision recorded in phase 1).

## 8. Risks

- **Media shapes undocumented** (highest): mitigated by the capture gate; Normalize work doesn't start until samples exist.
- **Wappi crossing bugs on tapi unknown**: assume present; keep serial queues + fetch gate.
- **Existing Telegram workflow clones** (if any were created on dev) carry the wrong `api/sync` URLs — must be recreated after template fix.
- **Scene churn**: builder runs committed immediately; per-fileID verification (project memory).

## 9. Owner veto points (decisions taken autonomously — cheap to reverse now, expensive later)

1. D1: in-screen switcher + removing the Telegram bottom tab (vs keeping two chat tabs).
2. D2: dashboard inclusion in-milestone (vs defer entirely).
3. D4: Telegram cache subdir with no WhatsApp migration; no Telegram post-auth sync window initially.
4. D7: Telegram template sessionKey switch to `chatId` (WhatsApp left as-is).
