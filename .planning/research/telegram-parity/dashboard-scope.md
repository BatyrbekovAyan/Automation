# dashboard-scope

## Summary
Including Telegram in the «Сводка» dashboard is nearly free on the server and cheap-but-not-zero on the client. The Dashboard_Outcomes workflow needs NO changes: every query keys sessions by split_part(session_id, ':', 1) and the Telegram_Bot template writes chat memory with the exact same `profileId:chatId` session key as WhatsApp (both templates line 592, identical expression). The client needs two ~3-line edits (DashboardPage.AuthedProfiles and ProfileToBot currently read only bot.whatsappProfileId) to light up counts/funnel/rows for Telegram. The real costs are (a) the bot-chip filter model, which is single-profileId and would produce two same-named chips per dual-channel bot, and (b) row deep-link, which hardwires the WhatsApp tab and a WhatsApp-only ChatManager — a Telegram row would silently land on the bot's WhatsApp chat list. Bigger blocker: the canonical Telegram_Bot template still calls WhatsApp API bases (message/send, mark/read, typing all at wappi.pro/api/sync/), so no real Telegram conversations exist to classify until template parity ships — dashboard inclusion is downstream of that work regardless.

## Open questions
- Exact format of the Wappi tapi webhook `from` field (numeric Telegram user id vs @username vs id-with-suffix) — could not verify without calling the authenticated API (off-limits); determines how bad the raw-id display-name fallback looks and whether a channel can be inferred from chatId shape alone.
- Whether the tapi webhook payload shape matches body.messages[0].{profile_id, from, body} as the cloned template assumes — unverified against a real Telegram delivery.
- Whether chat-memory writes still occur when the Telegram template's send node fails (it currently posts to the WhatsApp api/sync base) — likely yes since memory attaches to the AI Agent node, but unverified; affects whether any Telegram sessions already exist in dev n8n_chat_histories.
- Whether Telegram group chats produce a from != chatId pattern matching the WhatsApp group guard (If node) — if not, group sessions could leak into n8n_chat_histories and would NOT be excluded by the '%@g.us' filter in Find Changed Sessions.

## Report
# Telegram in «Сводка» dashboard — cost analysis

## 1. How DashboardPage builds the profileIds list — WhatsApp-only

`/Users/ayan/Projects/Automation/Assets/Scripts/Main/Dashboard/DashboardPage.cs`

- `AuthedProfiles()` (lines 81–95) iterates `Manager.Instance.BotsRoot` and collects ONLY `bot.whatsappProfileId` (line 90), guarded against null/empty and `Bot.UnauthedProfileSentinel` (`"-1"`, defined `Assets/Scripts/Main/Bot.cs:67`; `telegramProfileId` field exists at Bot.cs:70 but is never read here).
- POST body is `{ profileIds = profiles }` serialized at DashboardPage.cs:128, sent to `{Manager.n8nBaseUrl}/webhook/DashboardOutcomes` (line 127).
- The companion map `ProfileToBot()` (lines 97–111) likewise maps ONLY `whatsappProfileId → bot GameObject name` (lines 106–108).

Fix = add `bot.telegramProfileId` (same sentinel guard) to both methods; the workflow's Prep node already strips `"-1"` defensively (see below), so even sending the raw sentinel would be harmless.

## 2. Dashboard_Outcomes workflow — session-id format and Telegram match

`/Users/ayan/Projects/Automation/Tools/n8n/workflows/2htWSV5IHO8E2CgB-Dashboard_Outcomes.json`

- **Prep** node (jsCode at line 23): filters `profileIds` to non-empty strings `!== '-1'`, base64-encodes the JSON array (comma-split workaround). Channel-agnostic.
- **Find Changed Sessions** (Postgres query at line 38): assumes session id format **`profileId:chatId`** — `split_part(h.session_id, ':', 1)` = profile_id, `substr(h.session_id, position(':' in h.session_id) + 1)` = chat_id (first-colon split, so colons inside chat_id are safe). Joins on `ids.profile_id = split_part(...)`. Group exclusion is the WhatsApp idiom `s.chat_id NOT LIKE '%@g.us'` — Telegram group ids would NOT match this filter, but the Telegram template's group guard (If node, `from == chatId`, Telegram_Bot.json If node params) drops group messages before they ever reach chat memory, so this is inert in practice (re-verify against real tapi payloads).
- **Apply Silence Rule** (line 189) and **Fetch Outcomes** (line 214): both key by `split_part(c.session_id, ':', 1) IN (decoded ids)` — channel-agnostic given the ids.
- **Classify** prompt (line ~110s, node "Classify") speaks only «Клиент:»/«Бот:» — channel-neutral.
- **Telegram template session key**: `/Users/ayan/Projects/Automation/Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json:592` — Chat Memory (`memoryPostgresChat`) `sessionKey = "={{ $('Webhook').item.json.body.messages[0].profile_id + ':' + $('Webhook').item.json.body.messages[0].from }}"`. This is **byte-identical** to the WhatsApp template (`4wYitz5ek30SVNlT-WhatsApp_Bot.json:592`). So Telegram sessions in `n8n_chat_histories` match the dashboard's assumed `profileId:chatId` pattern exactly. CreateTelegramWorkflow clones this template by literal id (Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json references `api/v1/workflows/4VN3gsFaC2HUYmcc`), so clones inherit the key.

**Conclusion: zero workflow changes needed** to classify Telegram sessions — just include telegram profile ids in the POSTed list.

**Caveat found in passing** — the Telegram_Bot template's action endpoints are still WhatsApp bases: `message/send` → `https://wappi.pro/api/sync/message/send` (Telegram_Bot.json:23), `message/mark/read` → `api/sync` (line 268), `chat/typing/start` → `api/sync` (line 313). Should be `tapi/sync`. Until template parity ships, Telegram bots can't actually converse, so there are no Telegram sessions to classify — dashboard inclusion is gated on that work, not on the dashboard itself. (Memory writes attach to the AI Agent, so sessions likely get written even when the send node fails, but that's unverified.)

## 3. SessionChatMap — resolution and what breaks

`/Users/ayan/Projects/Automation/Assets/Scripts/Main/Dashboard/SessionChatMap.cs:7-11` — `ResolveBotName` is a pure dictionary lookup; nothing WhatsApp-specific in the class itself. The WhatsApp-only-ness lives entirely in the dictionary supplied by `DashboardPage.ProfileToBot()` (DashboardPage.cs:106-108).

With a Telegram profileId in an outcome row today:
- `ResolveBotName` returns null → `OpenChat` early-returns silently (DashboardPage.cs:404-405) — row tap does nothing.
- `BindRow` botTag: `botTag.gameObject.SetActive(showBotTag)` runs but `map.TryGetValue(r.profileId, …)` misses, so the tag is shown with unset/template text (DashboardPage.cs:379-381) — minor cosmetic bug, auto-fixed once the map includes telegram ids.
- Bot chips (`BuildChips`, DashboardPage.cs:270-290) never offer the Telegram profile as a filter, and `FilterByProfile` (Dashboard/DashboardMetrics.cs:34-38, single-string equality on `r.profileId`) means a WhatsApp-profileId filter would hide the same bot's Telegram rows.

Fix for resolution = add telegramProfileId entries to the same map (both channels of one bot map to the same bot name — no key collision since Wappi profile ids are globally unique GUIDs).

## 4. Row deep-link — what it needs, what happens for Telegram

`DashboardPage.OpenChat` (DashboardPage.cs:402-418) needs: (a) bot name via `SessionChatMap.ResolveBotName` (line 404), (b) `ChatManager.SetActiveBot(botName)` (line 408 → ChatManager.BotState.cs:104), (c) tab switch hardwired to `BottomTabManager.WhatsAppTabIndex` (line 411; const `= 0` at `Assets/Scripts/Main/BottomTabManager.cs:80`), (d) deferred `SelectChat(chatId)` on ChatManager (lines 417-424). **There is no channel parameter anywhere** — the outcome row carries only `profileId` + `chatId` (`Dashboard/DashboardModels.cs:34-35`).

ChatManager is a WhatsApp-only surface: `BeginLoadForActiveBot` requires `bot.whatsappProfileId` (`Assets/Scripts/Main/ChatManager.BotState.cs:200`), `GetActiveProfileId` returns only `whatsappProfileId` (lines 142-147), sync/empty-states are all WhatsApp (`IsWhatsAppSyncing`, `EmptyStateReason.BotHasNoWhatsApp`). A Telegram outcome row (once the bot resolves) would: switch to the WhatsApp tab, load the bot's WHATSAPP chat list, and `SelectChat(telegramChatId)` would find no matching chat → user lands on the wrong channel's chat list with no error (intentional soft-fail comment at DashboardPage.cs:414-416). So: **broken-but-graceful**. Correct deep-link requires a Telegram chat UI (doesn't exist) + channel detection (which profile map matched) + a channel-aware tab/`SetActiveBot` path — or an explicit v1 decision that Telegram rows don't deep-link (disable tap / show a hint).

Row cosmetics for Telegram: `ChatDisplayName` (DashboardPage.cs:453-460) falls back to `WappiRecipient.FromChatId`, which passes non-`@c.us` ids through unchanged (`Assets/Scripts/Chat/WappiRecipient.cs:12-16`) → row name = raw Telegram chat id. Title/avatar/local-time helpers (`Assets/Scripts/Main/ChatManager.Dashboard.cs:8-53`) read `chatLookup`, which holds only the active bot's WhatsApp chats → Telegram rows always take the colored-silhouette + server-time fallbacks (functional, slightly degraded).

## 5. Verdict — itemized

**Free (no changes):**
- Dashboard_Outcomes workflow — all 4 SQL touchpoints (Find Changed Sessions line 38, Upsert line 176 region, Apply Silence Rule line 189, Fetch Outcomes line 214) are channel-agnostic; session-key format matches (Telegram_Bot.json:592 == WhatsApp_Bot.json:592). Prep already filters the `-1` sentinel (line 23).
- `conversation_outcomes` schema, silence rule, classify prompt, response shape, DashboardStore cache, period/metrics math.

**Trivial client edits (~6 lines total):**
- `AuthedProfiles()` — also collect `telegramProfileId` (DashboardPage.cs:90-92).
- `ProfileToBot()` — also map `telegramProfileId → t.name` (DashboardPage.cs:106-108). This alone lights up counts/funnel/legend/status rows/recent list/bot tag for Telegram, and un-breaks the botTag cosmetic bug (379-381).

**Real design/implementation work (NOT free):**
- **Chip filter model**: `_botFilter` is one profileId; `FilterByProfile` is string equality (DashboardMetrics.cs:34-38); `BuildChips` iterates the map per-profile (DashboardPage.cs:279-289) → a dual-channel bot gets two identically-labeled chips and each hides half its rows. Needs bot-level filtering (botName → set of profileIds) — small refactor across DashboardPage + DashboardMetrics.
- **Deep-link**: needs channel detection + a Telegram chat surface (does not exist; ChatManager.BotState.cs:142-147, 200 are WhatsApp-only) or an explicit no-tap policy for Telegram rows (DashboardPage.cs:402-424, BottomTabManager.cs:80).
- **Row display**: raw-id fallback name (WappiRecipient.cs:15) and no avatar/title/local-time for Telegram chats (ChatManager.Dashboard.cs) — acceptable v1 degradation, or add a channel badge to disambiguate.

**External gate (bigger than all of the above):** the Telegram_Bot template still targets WhatsApp API bases (Telegram_Bot.json:23, 268, 313 — `wappi.pro/api/sync/...` instead of `tapi/sync`), so no real Telegram sessions exist in `n8n_chat_histories` yet. Dashboard inclusion is moot until Telegram template parity ships; once it does, the two-line client change is the only hard requirement for aggregate numbers.

**Recommendation shape:** include-now for aggregates is cheap (2 small edits, zero server work) IF you accept degraded rows (raw-id names, no deep-link, no per-bot chip correctness for dual-channel bots). Otherwise defer full inclusion to the same phase that builds the Telegram chat UI, since chips + deep-link both want a channel concept the dashboard data model (`DashboardModels.cs:32-42` — no channel field) doesn't have yet.
