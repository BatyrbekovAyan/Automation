# Phase 5: Channel-Aware ChatManager Core - Context

**Gathered:** 2026-07-12
**Status:** Ready for planning
**Source:** Design-spec express path (docs/superpowers/specs/2026-07-12-telegram-parity-design.md §D4, §D5; .planning/research/telegram-parity/chatmanager-pipeline.md; autonomous session)

<domain>
## Phase Boundary

Make the ChatManager chat pipeline channel-aware and implement every tapi divergence that does NOT require live-captured media samples, plus the Telegram 2FA auth fix. The capture-gated work (media Normalize port, sticker/GIF type strings, reactions-receive go/no-go, quoted-media specifics) is isolated in ONE final plan that consumes `Tools/tapi/SHAPES.md` verdicts and is BLOCKED until the owner's Phase-3 capture run — it must be `autonomous: false` with an explicit human-action checkpoint.

NOT in this phase: any scene/UI construction (channel switcher = Phase 6; this phase may add code-side hooks the switcher will call), suggestions payload (Phase 7), dashboard (Phase 7).

**Unity verification environment:** the Editor is CLOSED — run EditMode tests headlessly via `Tools/run-tests-headless.sh` (never the Editor bridge). Brand-new .cs files get their .meta generated during the headless launch's import; commit .cs + .meta together (project rule).

</domain>

<decisions>
## Implementation Decisions

### Channel seam (locked, design D4)
- `ChatChannel` enum: `WhatsApp = 0, Telegram = 1` — new file `Assets/Scripts/Chat/ChatChannel.cs` (plain enum, no MonoBehaviour).
- `WappiEndpoints` static class — new file `Assets/Scripts/Chat/WappiEndpoints.cs`: `Sync(ChatChannel, string pathAndQuery)` → `https://wappi.pro/{api|tapi}/sync/{path}`; replaces the 11 hardcoded literals (ChatManager.cs:391, 525, 1102, 1175, 1812, 1933, 2023; DeleteChat.cs:52; ReactionSend.cs:66; ReactionResolve.cs:74; QuoteResolve.cs:96). `WappiMediaRequestFactory.EndpointFor` gains a channel parameter.
- `ChatManager.ActiveChannel` (public read) lives in ChatManager.BotState.cs beside `CurrentBotId`; persisted per bot in PlayerPrefs key `{botId}ActiveChatChannel` (int). `SetActiveChannel(ChatChannel)` mirrors `SetActiveBot`'s full reset choreography (stop `_syncWaitRoutine`, `_outbox = null`, clear Chats/chatLookup, OnChatListCleared, StopAllCoroutines, zero `_chatFetchesInFlight`, `_chatListSyncing = false`, clear vthumb + media queues, BeginLoadForActiveBot); if a chat is open, route through `ShowChatList` first. Fires a new `OnActiveChannelChanged` event (Phase 6 consumes it).
- Channel resolution on bot switch/startup: restore `{botId}ActiveChatChannel`; if that channel is unconnected but the other IS connected, auto-select the connected one (persist the correction). Default for missing key: WhatsApp.
- `GetActiveProfileId()` → switch on ActiveChannel over `whatsappProfileId`/`telegramProfileId` (same `IsValidProfileId` guard; zero call-site churn).
- `GetCacheRoot()` → WhatsApp keeps legacy `BotCache/{botId}/`; Telegram = `BotCache/{botId}/telegram/`. No migration. `PurgeCacheForBot`/privacy clears already recurse — verify with a test, don't restructure.
- `OutboxStore.OutboxEntry` gains `channel` (int; absent in legacy JSON → 0 = WhatsApp). Retry paths rebuild URLs via `WappiEndpoints` from `entry.channel`.
- Concurrency guards unchanged (assume Wappi crossing bugs on tapi): `_chatFetchesInFlight`, `CrossChatResponseGuard`, serial media/vthumb queues; all reset on channel switch exactly as on bot switch.

### Empty/tab states (locked)
- `EmptyStateReason` gains `BotHasNoTelegram`. `OnEmptyState` consumers updated (EmptyStateView copy: mirror the existing English WhatsApp strings — "Telegram not connected" / "Connect Telegram to this bot to see its chats." / "Connect Telegram"; CTA reuses `OpenCurrentBotAuth` which opens BotSettings).
- `WhatsAppTabStateResolver`/`WhatsAppTabState` generalize to a channel-parameterized resolver (pure function; keep a compatibility path so existing tests keep passing or update them in the same commit — suite must stay green either way).
- Post-auth sync window: key becomes channel-qualified in the READ path (`WhatsappSyncUntil` stays for WA; NO Telegram window is written at auth — `IsWhatsAppSyncing` applies only when ActiveChannel==WhatsApp; Telegram path skips the sync-gate entirely).
- `RefreshActiveBotChats` + `TabRefreshGate`: keep tab-0 trigger; refresh applies to the active channel.

### tapi divergences implementable WITHOUT capture (locked, design D5)
- `ParseMessageType`: add `"text"` → `MessageType.Chat` (safe: WA never sends "text").
- `ChatDialog` (Assets/Scripts/Chat/ChatDialog.cs): add `public string last_time;` and `public string type;`. Chat-list time parse: try `last_timestamp` (RFC3339 string), fall back to `last_time` (RFC3339 string) — JsonUtility silently leaves type-mismatched bindings empty, which makes the fallback channel-free.
- Groupness: new pure helper `ChatIdFormat` — new file `Assets/Scripts/Chat/ChatIdFormat.cs`: `Recipient(id)` (strip `@c.us` only when suffix present — suffix-conditional, NOT channel-conditional), `DisplayFallback(id)` (never slice numeric/short ids — replaces the crash-prone `chat.id[..^5]` at ChatManager.cs:288), `IsGroup(chatId, dialogType, dialogIsGroup)` (WA: `@g.us` suffix or isGroup flag; TG: `type == "chat"`). Existing `WappiRecipient.FromChatId` and `WappiMediaRequestFactory.NormalizeRecipient` delegate to `ChatIdFormat.Recipient` (single home; keep the public wrappers so call sites/tests don't churn). `ChatViewModel.IsGroup` and `MessageListView`'s suffix checks route through the same logic (ChatViewModel gains the groupness at construction from the dialog — planner picks the minimal-churn wiring).
- Send text: URL via WappiEndpoints; recipient via ChatIdFormat.
- Send reply on Telegram: `POST tapi/sync/message/reply` body `{body, message_id}` (no recipient) — branch in `PostTextMessageRoutine` when ActiveChannel==Telegram AND quotedMessageId non-null; WA path unchanged (`quoted_message_id` on message/send).
- Reactions send on Telegram: body gains required `recipient` (from ChatIdFormat.Recipient(chatId)); WA body unchanged.
- Mark-read on Telegram: `POST tapi/sync/message/mark/read?profile_id=...` body `{message_id}` — NO `mark_all` query; WA keeps mark_all=true.
- Chat delete: gated OFF for Telegram — expose `ChatManager.ActiveChannelSupportsChatDelete => ActiveChannel == ChatChannel.WhatsApp`; `DeleteChat` early-returns (no server call, no optimistic removal) on Telegram; the swipe-action UI gate itself is Phase 6, but the guard lives here now.
- Delivery ticks: `DeliveryTickFormatter` maps `pending` → Sent-equivalent and `undelivered`/`error` → failed-equivalent per its existing enum surface (planner reads the file; minimal mapping, no UI change).
- Avatars: NO change (dialog `thumbnail` path is channel-agnostic; GreenApiAvatarFetcher stays dormant/WhatsApp-only).
- `messages/id/get` quote recovery + reaction resolve queues: URLs via WappiEndpoints (they follow ActiveChannel); their message-shape handling is already null-tolerant — deeper TG-specific handling only if SHAPES verdicts demand it (capture-gated plan).

### Telegram 2FA fix (locked, design D5 — TGAUTH-01)
- `Manager.SendTelegramCode` (~:2306): a 200 response with `detail:"2fa"` switches the existing code-entry panel into cloud-password mode — RU labels set from code («Облачный пароль», «Введите пароль от Telegram» style copy consistent with existing RU strings), input validation relaxed (arbitrary non-empty string, not min-5-digits), submit posts `POST tapi/sync/auth/2fa` `{"pwd_code": ...}` (UploadHandlerRaw + explicit `Content-Type: application/json`), success = `detail` startswith `auth_success` → same `ShowAuthSuccess` path.
- `OpenTelegramQRPanel` (~:2095): a QR response with `detail:"2fa"` shows the same password mode instead of a broken QR texture.
- NO new scene objects — reuse existing panels/labels via code. Wrong password → re-prompt with error status text (parse the failure `detail`; exact wrong-password shape is unverified — handle any non-success as re-prompt).
- English status strings in the TG auth flow may be RU-ified ONLY where touched (don't sweep the file).

### Capture-gated plan (final, BLOCKED — consumes Tools/tapi/SHAPES.md)
- Scope: media Normalize branches for TG (`messages/get` media body/s3Info/attaches shapes), sticker/video-note/GIF type strings, voice duration source, reactions-RECEIVE go/no-go implementation (or explicit v2 deferral note), reply-snapshot echo-bug guard applicability, `isDeleted` stickiness handling, dialog `name`/`thumbnail` presence fallbacks (backfill from `user.FirstName`/`last_message_sender.pushname` if verdicts say name is empty in chats/filter).
- Plan frontmatter: `autonomous: false`, depends_on all other plans; first task = human-action checkpoint "Run Tools/tapi/capture-shapes.sh and record SHAPES.md verdicts (03-HUMAN-UAT)". If verdicts are still `PENDING CAPTURE`, the executor returns at the checkpoint — the phase then closes as human_needed (established pattern).

### Testing (locked)
- EditMode tests in `Assets/Tests/Editor/Chat/` (no asmdef — Assembly-CSharp-Editor). New coverage: WappiEndpoints (both channels × representative paths), ChatIdFormat (recipient/display-fallback/isGroup incl. short + numeric + @g.us + type=="chat" cases), channel-parameterized tab-state resolver, OutboxEntry channel round-trip (legacy JSON without channel → WhatsApp), ParseMessageType "text" (extract to a pure testable seam if it isn't — follow existing test patterns), ChatDialog last_time fallback parse.
- Full suite MUST stay green: run `Tools/run-tests-headless.sh` (Editor closed) at each plan completion; a plan is not complete on a red suite.
- Pure logic extracted to static/pure classes where the existing codebase already does so (WhatsAppSyncGate, CrossChatResponseGuard precedents).

### Claude's Discretion
- Exact event signatures, minimal wiring for ChatViewModel groupness, whether resolver rename vs wrapper, internal file organization of the new partial code (prefer a new `ChatManager.Channel.cs` partial if BotState.cs would bloat).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Design + research (the ground truth for every touchpoint)
- `docs/superpowers/specs/2026-07-12-telegram-parity-design.md` — §D4 (seam), §D5 (divergence table)
- `.planning/research/telegram-parity/chatmanager-pipeline.md` — the full touchpoint inventory with file:line for all 11 URLs, 17 GetActiveProfileId call sites, cache keying, guards, `@c.us` sites
- `.planning/research/telegram-parity/tapi-shapes.md` — §1 endpoint table (exact tapi params), §4 message shape, §9 auth/2FA, §10 parser impact list
- `.planning/research/telegram-parity/auth-bot-entity.md` — §1 Manager.cs TG auth inventory (line refs), §5 Telegram-only bot behavior today
- `Tools/tapi/SHAPES.md` — verdict checklist (capture-gated plan's input; check verdict status before implementing)

### Project skills (read SKILL.md first)
- `.claude/skills/chat-data-flow/SKILL.md` — MANDATORY for any RawMessage/NormalizedMessage/ViewModel/ChatManager-event change
- `.claude/skills/bot-persistence/SKILL.md` — PlayerPrefs key conventions (`{botId}ActiveChatChannel` must follow them)
- `.claude/skills/unity-api-integration/SKILL.md` — UnityWebRequest/coroutine conventions
- `.claude/rules/networking.md`, `.claude/rules/unity-general.md` — auto-apply path rules

### Test harness
- `Tools/run-tests-headless.sh` — headless EditMode runner (Editor closed; outputs Tools/test-output/)
- `Assets/Tests/Editor/Chat/` — existing test patterns to follow

</canonical_refs>

<specifics>
## Specific Ideas

- The channel seam must leave EVERY WhatsApp behavior byte-identical: same URLs, same cache paths (legacy root), same sync-window behavior, same reply/reaction bodies. The full existing suite is the regression net.
- `SetActiveBot` currently persists `LastSelectedBotForChats` — `SetActiveChannel` persists `{botId}ActiveChatChannel` in the same PlayerPrefs style (Save() immediately).
- ChatManager.cs:288's `[..^5]` slice throws on ids shorter than 5 chars — the docs even show `id:""` for TG; DisplayFallback must handle empty string.
- Outbox entries snapshot profileId at send time (ChatManager.cs:1909, MediaSend.cs:204) — snapshot channel at the same sites.
- N8nSuggestionsProvider bypasses GetActiveProfileId (reads bot.whatsappProfileId directly) — DO NOT touch it this phase (Phase 7); note it in the seam plan so nobody "fixes" it early.
</specifics>

<deferred>
## Deferred Ideas

- Channel switcher UI + swipe-delete UI gating + EmptyStateView per-channel PLATFORM preselect → Phase 6.
- Suggestions provider channel-awareness → Phase 7. Dashboard → Phase 7.
- Telegram post-auth sync window → only if device testing shows tapi needs one.
- `WappiUnitySync.cs` — dead code, ignore.
</deferred>

---

*Phase: 05-channel-aware-chatmanager-core*
*Context gathered: 2026-07-12 via design-spec express path*
