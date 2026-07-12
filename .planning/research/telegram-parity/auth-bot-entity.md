# auth-bot-entity

## Summary
Telegram is already at full parity with WhatsApp for auth (QR + phone/code via wappi tapi), profile lifecycle, n8n workflow create/edit/enable/delete, PlayerPrefs persistence, PendingProfileLedger orphan cleanup, and the bot activation toggle (which fires BOTH channel workflows with sentinel guards). Nothing Telegram-side in Manager.cs is stubbed — all coroutines are live and mirror their WhatsApp twins. The gap is entirely in the chat surface: ChatManager has zero channel concept — CurrentBotId is the only selection axis, GetActiveProfileId returns only whatsappProfileId, all 12 chat endpoints hardcode https://wappi.pro/api/sync/, the empty-state enum is literally BotHasNoWhatsApp, and the per-bot cache root has no channel dimension. A Telegram-only bot today creates fine, shows a normal card on BotsPage, works in BotSettings, but the Chats tab permanently shows the "WhatsApp not connected / Connect WhatsApp" empty card. Channel selection would naturally hook into GetActiveProfileId + BeginLoadForActiveBot + GetCacheRoot + WhatsAppTabStateResolver, with the BotSwitcher sheet chips being the only existing channel-aware chats-tab UI (informational only).

## Open questions
- Whether wappi tapi's get/status response actually contains the `"phone":"...","platform":` shape the manual parse assumes (Manager.cs:2448-2455) — could not verify since authenticated API calls are off-limits; the code ships this way and presumably worked on device.
- Whether the stale comment at Manager.cs:3185-3189 ('create-workflow POST commented out') reflects any remaining runtime edge — code shows the POST live at :2880; '' ids still occur transiently because FromStart create is fire-and-forget, so the sentinel treatment remains needed either way.
- isOnTelegram read-default inconsistency (default 1 in LoadBots/CloseSettings Manager.cs:387/:807 vs default 0 in SaveWorkflows' flip check :3235) — appears benign because the key is always written at creation, but pre-existing installs missing the key would render the toggle ON while behaving as OFF for the flip check.

## Report
# Telegram parity inventory — what already exists

All paths below are under `/Users/ayan/Projects/Automation/`.

## 1. Telegram methods in Manager.cs (`Assets/Scripts/Main/Manager.cs`, 3643 lines) — ALL COMPLETE, none stubbed

### State / UI fields
- SerializeFields: `TelegramAuth` page :19, QR/code panels :46-51, success panel/back button :57-58, code buttons :60-63, number/code inputs :67-68, `TelegramQRCodeImage` :71, status text :73, wizard `platformTelegramGroup` :88, `telegramOptionButton` :105
- `TelegramBrandColor` #2AABEE :116; save-gate flags `CreateTelegramWorkflowFromEditSuccess`/`EditTelegramWorkflowSaved`/`EnableTelegramWorkflowSaved` :121-123
- Wizard-local `telegramProfileId = "-1"` :140; `selectedPlatform` (0=none,1=WA,2=TG,3=Both) :142; `telegramAuthCompleted` :147; `_telegramStatusCoroutine` :150
- Button wiring :289-299 (back→CancelBotCreation, GetTelegramCode, SendTelegramCode, ChangeTelegramNumber, input listeners)

### Auth flow (wizard + settings shared page) — complete
- `ShowTelegramAuth()` :1672-1710 — resets QR+code panels, honors `TelegramCooldownFinishTime` PlayerPref, activates `TelegramAuth`, starts QR loop
- `OpenTelegramQRPanel()` :2095-2165 — loops `GET https://wappi.pro/tapi/sync/auth/qr?profile_id=`, paints texture, then starts status polling :2137-2138
- `GetTelegramCode()` :2215-2292 — `POST tapi/sync/auth/phone` with JSON `{"phone":"7"+input}` (proper UploadHandlerRaw + Content-Type json). On success: shows code entry + `TelegramCodeTimer` (30s cooldown). **Parity nuance: unlike WhatsApp (`GetWhatsappCode` :1823 → `RecreateWhatsappProfileForNewCode` delete+recreate for the 2-min repeat-code cooldown), Telegram resend is a plain re-request — no recreate needed.**
- `SendTelegramCode()` :2306-2381 — `POST tapi/sync/auth/code` `{"auth_code":...}`; success = response `detail` startswith `auth_success` :2356; then polls status. Button texts here are hardcoded English ("Authorizing..", "Authorization Complete") — cosmetic gap vs RU elsewhere.
- `GetTelegramProfileStatus()` :2419-2466 — 5s poll `GET tapi/sync/get/status`; manual substring parse of `"authorized":true`; extracts `phone` (assumes `","platform":` delimiter :2448-2455); `ShowAuthSuccess` → `telegramAuthCompleted = true` :2459. Exits if `TelegramAuth` deactivated :2426.
- Panel helpers: `CloseTelegramQRPanel` :2167, `SetTelegramCodeEntryTexts` :2177, `OpenTelegramCodePanel` :2185, `TelegramNumberInputChanged` :2203 (min 10 digits), `TelegramCodeInputChanged` :2294 (min 5), `CloseTelegramCodePanel` :2382, `ChangeTelegramNumber` :2399, `RebuildTelegramAuthLayout` :1541
- Settings-mode reuse: `OpenTelegramAuthFromSettings(profileId, onDone, onBack)` :919-935 (rewires back button to `OnSettingsAuthBackPressed` :967-977); `LastAuthedTelegramNumber` :940, `LastAuthedTelegramProfileId` :942; `WaitForSettingsAuthCompletion(whatsapp:false)` :951-965; `EndSettingsAuth` :979-997 restores wizard back-wiring

### Profile lifecycle — complete
- `CreateTelegramProfile(name, localId)` :2648-2690 — `POST tapi/profile/add?name=`; parses `profile_id`; localId=true→wizard field, false→`openBot`'s Bot; `PendingProfileLedger.MarkTelegramPending` :2685. (Failure branch is silently empty :2660-2663 — same as WA twin.)
- `DeleteTelegramProfile(id, localId)` :2692-2720 — `POST tapi/profile/delete`; resets to "-1" (+PlayerPrefs `TelegramProfileId` when !localId :2713); `ClearTelegramIfMatches` :2716
- Public wrappers: `GetCreateTelegramProfile` :2475, `GetDeleteTelegramProfile` :2485

### n8n workflow lifecycle — complete
- `CreateTelegramWorkflowFromStart(bot)` :2864-2917 — `POST {n8nBaseUrl}/webhook/CreateTelegramWorkflow` with `Name`/`BusinessType`/`BusinessTypeId`/`TelegramProfileId` + empty Business/Prompt/lists; failure → deletes profile :2888; success → card Active, `telegramWorkflowId` + PlayerPrefs `TelegramWorkflowId`/`TelegramProfileId` :2906-2908, `MarkTelegramClaimed` :2909. (Historical inverted-check bug documented+fixed :2883-2885; cruft `yield break; //to be deleted` :2916.)
- `CreateTelegramWorkflowFromEdit()` :2919-3003 — full form incl. products/services; save pill; `MarkTelegramClaimed` :2986; `FailSavePanel` on failure :2996-3000. Wrapper `GetCreateTelegramWorkflow` :2496.
- `EnableTelegramWorkflow(id, enabled)` :3052-3086 — sentinel skip (`""`/`"-1"`) :3055-3060; `POST /api/v1/workflows/{id}/(de)activate` with pinned `Content-Type: application/json` :3071; always settles gate flag. Wrapper `GetEnableTelegramWorkflow` :2506.
- `DeleteTelegramWorkflow(id, deletingBot)` :3106-3122 — `DELETE /api/v1/workflows/{id}`; !deletingBot resets openBot + PlayerPrefs to "-1". **No sentinel guard** — `DeleteProfilesAndWorkflows` :2529-2536 fires it with "-1" (harmless 404). Wrapper :2516.
- `SaveWorkflows(waWid, tgWid)` :3124-3264 — Telegram branch :3227-3260: sentinel skip :3228-3232, Enable only when `isOnTelegram` PlayerPref flips vs toggle :3235-3242, then `POST /webhook/EditTelegramWorkflow` :3247. `Saved()` AND-gate includes both TG flags :3266-3294. (Comment :3185-3189 claiming the FromStart create POST "is commented out" is STALE — the POST is live at :2880/:2749; the ""-as-sentinel treatment it justifies is still correct because create runs fire-and-forget.)
- `GetSaveSettings` :2522-2527; `DeleteBotFilesOnServer(waWid, tgWid)` :2543-2551 maps null/empty tgWid → `Bot.UnauthedProfileSentinel`

### Wizard + cleanup
- `SelectPlatform(mode)` :1045-1077 (2=Telegram, 3=Both; brand-color button :1082-1096)
- `CreateBotFromForm()` :1306-1449 — `useTelegram = selectedPlatform==2||3` :1316; TG auth step :1335-1351; ids/prefs persisted :1373, 1400-1408 (non-TG bots get `TelegramWorkflowId="-1"`), `isOnTelegram` :1416, `TelegramNumber` :1423; `SetActiveBot(newBot.name)` :1442-1445 regardless of channel
- `CancelBotCreation` :1471-1486 — stops TG status poll, deletes pending TG profile if != "-1"
- Launch sweep `LoadBots` :433-436 (`TryGetPendingTelegram` → delete); quit settle `SettlePendingProfilesBeforeQuit` :464-496 (blocking `tapi/profile/delete` :481, ledger cleared only on confirmed success :494-495)
- `SendToTelegram` :3329+ is the support-form Bot-API sender — unrelated to the channel

## 2. Bot.cs Telegram fields + PlayerPrefs (`Assets/Scripts/Main/Bot.cs`)
- `public const string UnauthedProfileSentinel = "-1"` :67; `telegramProfileId` :70; `telegramWorkflowId` :73 (plain public fields, not serialized-persisted — rehydrated from PlayerPrefs)
- PlayerPrefs suffixes (key = `transform.name` + suffix, e.g. `Bot0TelegramProfileId`):
  - `TelegramProfileId` (string, default "-1" — Manager.cs:374), `TelegramWorkflowId` (string, default "-1" — :376), `isOnTelegram` (int 0/1; **read default 1** in LoadBots :387 and CloseSettings :807, but **default 0** in SaveWorkflows' flip check :3235), `TelegramNumber` (string, default "")
  - Deleted in `DeleteBot()` at Bot.cs:175 (`isOnTelegram`), :178 (`TelegramNumber`), :183 (`TelegramWorkflowId`), :184 (`TelegramProfileId`)
- Global (not per-bot): `TelegramCooldownFinishTime` (TelegramCodeTimer.cs:20-27, Manager.cs:1343/1692); ledger keys `lastCreatedTelegramProfileId`/`...Saved` (PendingProfileLedger.cs:18-19)
- Sentinel semantics: `"-1"` = channel never authed / no profile / no workflow. `""` = workflow-create still in flight (create is fire-and-forget) — treated identically everywhere that matters: `EnableTelegramWorkflow` :3055, `SaveWorkflows` :3228, `DeleteBotFilesOnServer` :2546, `ChatManager.IsValidProfileId` (BotState.cs:135-136 — null/empty OR sentinel)
- `DeleteBot()` flow (Bot.cs:166-253): PlayerPrefs wipe → `ChatManager.PurgeCacheForBot` :241 → `Manager.DeleteBotFilesOnServer(whatsappWorkflowId, telegramWorkflowId)` :247 (coroutine lives on Manager since the Bot destroys itself) → `DeleteProfilesAndWorkflows(waPid, tgPid, waWid, tgWid)` :249 (deletes BOTH profiles + BOTH workflows) → destroys paired BotSettings + self :251-252

## 3. How the Chats tab picks its bot; channel concept (`Assets/Scripts/Main/ChatManager.BotState.cs`)
- `CurrentBotId` :14 (default `"_default"`), persisted under PlayerPrefs `"LastSelectedBotForChats"` :98
- Startup: `InitializeActiveBotNextFrame` :267-275 → `ResolveInitialActiveBot` :283-303 (persisted choice if still exists, else first child of `Manager.BotsRoot` (Manager.cs:25), else `OnEmptyState(NoBotsExist)`)
- Switch: `SetActiveBot(botId)` :104-129 — persists, clears chats, fires `OnActiveBotChanged`, `StopAllCoroutines`, `BeginLoadForActiveBot`. Callers: BotSwitcherSheet.HandleRowTap (`Assets/Scripts/UI/BotSwitcherSheet.cs:167` — bottom sheet on the chats top bar), Manager.CreateBotFromForm (Manager.cs:1444), DashboardPage deep-link (`Assets/Scripts/Main/Dashboard/DashboardPage.cs:407-408`), PurgeCacheForBot fallback (BotState.cs:68)
- **There is NO channel concept.** WhatsApp is hardwired at every layer:
  - `GetActiveProfileId()` :142-147 returns ONLY `bot.whatsappProfileId` — the single choke point every chat coroutine uses
  - `BeginLoadForActiveBot()` :197-215 gates on `whatsappProfileId` + the `"WhatsappSyncUntil"` key (:150) → `OnEmptyState(BotHasNoWhatsApp)` :202
  - `ComputeCurrentEmptyState()` :174-189 → `WhatsAppTabStateResolver.Resolve` (`Assets/Scripts/Main/WhatsAppTabState.cs:11-20`; states NoBots/NoWhatsApp/Syncing/Ready)
  - `EmptyStateReason` enum = `{NoBotsExist, BotHasNoWhatsApp}` only (`ChatManager.cs:2064-2068`)
  - `GetCacheRoot()` :20-26 = `persistentDataPath/BotCache/{botId}/` — per-bot only, `chats.json` has no channel dimension
  - All chat endpoints hardcode `https://wappi.pro/api/sync/`: ChatManager.cs:391 (chats/filter), :525/:1102/:1175 (messages/get), :1812 (media/download), :1933 (message/send), :2023 (mark/read); ChatManager.DeleteChat.cs:52; ChatManager.QuoteResolve.cs:96; ChatManager.ReactionResolve.cs:74; ChatManager.ReactionSend.cs:66
- Natural hook points for channel selection: `GetActiveProfileId` (swap per active channel), `BeginLoadForActiveBot`/`RefreshActiveBotChats` (:237-247), `GetCacheRoot` (add channel subdir), `LastSelectedBotPrefKey` (add channel companion key), `WhatsAppTabStateResolver` + `EmptyStateReason` (generalize), and a base-URL selector (`api/sync` vs `tapi/sync`)
- Only channel-aware UI on the chats surface today: `BotSwitcherRowView` chips (`Assets/Scripts/UI/BotSwitcherRowView.cs:63-67` — `IsConnected(bot.whatsappProfileId)` / `IsConnected(bot.telegramProfileId)` :79-80, WA green / TG blue, gray when "-1"). Purely informational; tapping a row selects a BOT, never a channel. `BotSwitcherTitleBinder` shows only the bot name.

## 4. Activation toggle — YES, both workflows
`Bot.EnableBot(bool)` (`Assets/Scripts/Main/Bot.cs:255-269`) unconditionally calls `Manager.Instance.GetEnableWhatsappWorkflow(whatsappWorkflowId, enabled)` at **Bot.cs:267** AND `Manager.Instance.GetEnableTelegramWorkflow(telegramWorkflowId, enabled)` at **Bot.cs:268**. The `""`/`"-1"` sentinel skip inside `EnableWhatsappWorkflow` (Manager.cs:3011-3016) / `EnableTelegramWorkflow` (Manager.cs:3055-3060) makes the missing-channel call a no-op instead of a 404. Settings-save path also flips both independently via `SaveWorkflows` (Manager.cs:3198-3200 WA, :3235-3237 TG).

## 5. Telegram-only bot today (isOnTelegram=1, whatsappProfileId="-1") — traced paths
- **Creation**: fully supported. `selectedPlatform==2` → only the TG auth step runs (Manager.cs:1316, 1335-1351); WhatsApp ids written as "-1" (:1394-1398), `isOnWhatsapp=0` (:1415), no `WhatsappSyncUntil` (:1431-1438 gated on useWhatsapp). Bot still becomes the active chats bot (:1442-1445).
- **BotsPage card**: indistinguishable from a WhatsApp bot. The card (Bot.cs fields :9-25) shows name, business desc, Status text, activation switch + «Бот работает/на паузе» footer, business icon — **no channel indicator exists on the card** (BotCardFooterBuilder.cs contains zero whatsapp/telegram references). `CreateTelegramWorkflowFromStart` marks it Active and enables Edit/switch (Manager.cs:2892-2898). Toggle works (both Enable calls; WA no-ops).
- **Chats tab**: dead end by design. `BeginLoadForActiveBot` sees `whatsappProfileId=="-1"` → `OnEmptyState(BotHasNoWhatsApp)` (BotState.cs:200-203); `SyncAllChats` has the same guard (ChatManager.cs:385-390). `EmptyStateView` renders the English "WhatsApp not connected / Connect WhatsApp to this bot to see its chats. / Connect WhatsApp" card (`Assets/Scripts/UI/EmptyStateView.cs:117-127`); the CTA `OpenCurrentBotAuth` (:146-164) switches to the Bots tab and invokes the bot's EditButton → opens BotSettings. Telegram chats are never fetched — no TG chat pipeline exists at all.
- **BotSwitcher sheet**: row shows WA chip gray/disconnected, TG chip blue/connected (BotSwitcherRowView.cs:63-67).
- **BotSettings**: General tab shows WhatsappToggle off + hidden WhatsappNumberField (Value=="" → SetActive(false), Manager.cs:394), TelegramToggle on + TelegramNumberField visible (:387, 392, 395). `OnEnable` probes BOTH channels (`BotSettings.cs:364-365`); the WhatsApp probe hits `get/status?profile_id=-1` (`BotSettings.Auth.cs:184`) but its mutation guard requires `!profileId.Equals("-1")` (:200) so nothing happens. Turning WhatsApp ON runs the fresh-auth path: create profile → shared auth page → on done `GetCreateWhatsappWorkflow` (`BotSettings.Auth.cs:68-93, 144-154`). Tab structure (General|Business|Products|Services|Prompts) is channel-agnostic (`BotSettings.cs:8-23, 38-47`).
- **Dashboard «Сводка»**: WhatsApp-only in v1 per CLAUDE.md (out of the asked scope but relevant to "which surfaces are WA-only").

## 6. PendingProfileLedger tapi/Telegram coverage — CONFIRMED (`Assets/Scripts/Main/PendingProfileLedger.cs`)
- Dedicated channel keys: `lastCreatedTelegramProfileId` / `lastCreatedTelegramProfileIdSaved` :18-19 (legacy names kept verbatim); API surface `MarkTelegramPending`/`MarkTelegramClaimed`/`ClearTelegramIfMatches`/`TryGetPendingTelegram` :33-39
- Mark pending: `CreateTelegramProfile` (Manager.cs:2685). Claimed: `CreateTelegramWorkflowFromStart` :2909 and `CreateTelegramWorkflowFromEdit` :2986. Cleared-if-matches: `DeleteTelegramProfile` :2716 and quit settle :495
- Sweeps: launch (`LoadBots` Manager.cs:433-436 → `DeleteTelegramProfile`) and quit (`SettlePendingProfilesBeforeQuit` :464-496, blocking `POST tapi/profile/delete` :481, ≤2s budget :486-492, ledger cleared only on confirmed success)
- Third layer: server-side hourly n8n `Delete Orphan Profiles` sweep covers `tapi/profile/all/get` per CLAUDE.md (workflow 2islisFH7jjLoPQM, dev)

## Notable parity gaps / cruft found in passing (not chat-pipeline)
- `SendTelegramCode`/`GetTelegramCode` status texts are hardcoded English mixed with RU labels (Manager.cs:2221, 2310, 2358, 2367)
- Stale comment at Manager.cs:3185-3189 says the FromStart create POST is "commented out" — it is live (:2880)
- `DeleteTelegramWorkflow` (and its WA twin) lack the `""`/`"-1"` sentinel guard that Enable has → guaranteed 404 spam when deleting a single-channel bot (harmless)
- `GetTelegramProfileStatus` phone parse assumes `"phone":` … `","platform":` adjacency in the tapi status response (Manager.cs:2448-2455) — fragile manual substring parsing, same style as WA twin
