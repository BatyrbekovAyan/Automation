# ui-scaffolding

## Summary
Screen_Telegram exists in Main.unity as a completely empty, inactive placeholder (one GameObject, a pink Image, zero children) — but it is LIVE in navigation: BottomTabManager tabs[1] ("Telegram", tabRoot active) points at it, so tapping the Telegram tab today shows a blank pink screen. The real chats screen is Screen_Whatsapp = ChatsPanel (hosts ChatListView component + TopBar + empty/syncing states + Sheet_BotSwitcher) + MessagesPanel (full message stack + 5 overlay panels), all driven by the root-level ChatManager singleton whose serialized ChatListPanel/MessageListPanel point at those two panels. Bot switching is already done in-screen via a bottom sheet (BotSwitcherTitle button in TopBar → BotSwitcherSheet → ChatManager.SetActiveBot), which performs a full, well-documented state-reset choreography that a channel switch would clone almost verbatim. A separate Screen_Telegram would force duplicating both panel subtrees (~7 overlay panels), collide with three singletons (ChatManager.Instance, SwipeToBack.Instance, FindFirstObjectByType-bound BotSwitcherSheet), and make every ChatManager event render into two lists. Recommendation: (a) channel switcher inside the existing chats screen, with the inactive TopBar CenterZone (or a ModeToggle-style segmented pill) as the natural plug-in point, and repoint/remove the existing Telegram tab.

## Open questions
- Wappi tapi/sync endpoint parity (chats/filter, messages/get, message/send, media/download shapes) could not be verified — external API calls are off-limits per task constraints; parity of RawMessage JSON shape for Telegram is unknown.
- Product decision needed: remove the live Telegram tab (tabs[1], Main.unity:135599) and fold into an in-screen switcher, or keep the tab and repoint it at Screen_Whatsapp with a channel preset — both are mechanically easy via the NavRestructureBuilder rewire pattern.
- Whether Telegram chats have avatars via a Wappi endpoint (GreenApiAvatarFetcher is WhatsApp-only) and whether reactions/quoted-reply recovery (messages/id/get) exist on tapi — determines how much of the message pipeline must be feature-gated per channel.
- Whether the per-bot reply-mode ModeToggle (Авто/Вместе) in TopBar RightZone applies to Telegram at all (suggestions are WhatsApp-only in v1) — the switcher design must decide to hide or disable it on the Telegram channel.

## Report
# Telegram chat-UI surface assessment

## 1. Screen_Telegram: what exists

Scene evidence (`/Users/ayan/Projects/Automation/Assets/Scenes/Main.unity`):
- GameObject `Screen_Telegram` = fileID 163358610 (YAML line 10651, `m_Name` at 10663), **`m_IsActive: 0`**.
- Components: RectTransform 163358613 (line 10707) with **`m_Children: []`** (line 10718) — zero children, full-stretch anchors under ScreenContainer; CanvasRenderer 163358612; Image 163358611 with a flat pink color `{r:1, g:0.682, b:0.784}` and no sprite — a bare placeholder.
- Scene-wide references to it: only 3 — its own component blocks, ScreenContainer's child list (line 73307), and **BottomTabManager `tabs[1].screenPanel` (line 135599)**.

**It is NOT fully dead**: the bottom nav is a live 5-tab bar — tabs from the scene: 0 `Whatsapp`→Screen_Whatsapp, 1 `Telegram`→Screen_Telegram (tabRoot `TelegramTab` **active=1**), 2 `Сводка`→Screen_Dashboard, 3 `Bots`→Screen_Bots, 4 `Profile`→Screen_Profile; `defaultTabIndex: 0`. Tapping "Telegram" today activates the empty pink screen.

`NavRestructureBuilder` (`/Users/ayan/Projects/Automation/Assets/Editor/NavRestructureBuilder.cs`) only touches Screen_Telegram in `ReorderScreens` (L417–441): its name appears in the deterministic child-order list (L422) so it sorts after Screen_Whatsapp; a missing screen just logs a warning (L436). The builder never builds/populates Screen_Telegram — verdict: **empty scaffolding as a screen, but wired into live navigation**.

ScreenContainer child order (rt 1131169389, line 73294): Screen_Whatsapp(active), Screen_Telegram(inactive), Screen_Dashboard, Screen_Bots, Screen_Profile, Screen_New, WhatsappAuth, TelegramAuth — matches the ReorderScreens invariant (auth pages last).

## 2. Chats screen composition

- **Screen_Whatsapp** (go 1992340357) has exactly two children: **ChatsPanel** (go 263910444) and **MessagesPanel** (go 1815923766).
- **ChatsPanel** carries the `ChatListView` component directly on itself (scene comp 263910449 = guid of `Assets/Scripts/UI/ChatListView.cs`) plus Image + LayoutElement. Children:
  - `Scroll/Viewport/...` — the chat list ScrollRect (rows spawned by ChatListView into `content`).
  - `EmptyState` [`EmptyStateView`] — hero/title/body/PrimaryButton; texts are WhatsApp-hardcoded ("WhatsApp not connected", `/Users/ayan/Projects/Automation/Assets/Scripts/UI/EmptyStateView.cs:108-121`).
  - `SyncingState` [`SyncingView`] — post-auth sync window UI.
  - `TopBar` (250 units tall, top-anchored — height is intentional per project memory): `Background/Line[PixelSnapLine]`, `LeftZone`[HorizontalLayoutGroup] → **`BotSwitcherTitle`** [Button + `BotSwitcherTitleBinder` + Avatar + BotName + Chevron], **`CenterZone` (INACTIVE, 360×140 center slot holding an unused Title TMP)**, `RightZone` → **`ModeToggle`** [`ReplyModeToggleBinder`, the Авто/Вместе segmented pill] + `NewChatButton` (inactive).
  - `Sheet_BotSwitcher` (inactive) [`BotSwitcherSheet`] — Backdrop + bottom-anchored Panel with RowScroll + SheetDragDismiss.
  - `DeleteChatConfirmPanel`, `ReplyModeConfirmPopup` (both inactive).
- **MessagesPanel** children: `MovingArea`, `TopBar` (per-chat header), `VideoPlayerPanel`, `PhotoViewerPanel`, `AttachmentPreviewScreen`, `EmojiPickerOverlay`, `ReactionBarOverlay`.
- **ChatManager** lives on a root-level scene GameObject `ChatManager` (go 905228176, transform father=0) together with IOSAudioFix, LinkScraper, `ChatListPreWarmer`, NotificationFx. Its serialized refs (scene): `ChatListPanel: 263910444` (ChatsPanel), `MessageListPanel: 1815923766` (MessagesPanel); fields declared at `/Users/ayan/Projects/Automation/Assets/Scripts/Main/ChatManager.cs:40-41`.
- **Tab wiring**: `/Users/ayan/Projects/Automation/Assets/Scripts/Main/BottomTabManager.cs` — `WhatsAppTabIndex = 0` (L80), `SwitchTab` toggles `screenPanel.SetActive` (L240-241) and on entering tab 0 calls `ChatManager.Instance?.RefreshActiveBotChats()` via `TabRefreshGate.ShouldRefreshChats` (L170-171; gate in `/Users/ayan/Projects/Automation/Assets/Scripts/Main/TabRefreshGate.cs`).
- `ChatListPreWarmer` (`/Users/ayan/Projects/Automation/Assets/Scripts/Main/ChatListPreWarmer.cs:7-8`) briefly activates Screen_Whatsapp at boot to pre-spawn row prefabs — Screen_Whatsapp-only.
- Views are event-driven: `ChatListView.Start` subscribes to `OnChatAdded/OnChatListCleared/OnEmptyState/OnActiveBotChanged/OnChatSelected/OnChatRemoved` (`/Users/ayan/Projects/Automation/Assets/Scripts/UI/ChatListView.cs:22-38`); `MessageListView` subscribes to `OnChatSelected` etc. in Awake (`/Users/ayan/Projects/Automation/Assets/Scripts/UI/MessageListView.cs:88-98`). `ChatItemView` binds a `ChatViewModel` and is channel-agnostic (no WhatsApp-specific logic beyond a UX comment, `/Users/ayan/Projects/Automation/Assets/Scripts/UI/ChatItemView.cs`).

## 3. Current bot-switch mechanism (exact)

In-screen bottom sheet, not BotsPage tap-through:
1. `TopBar/LeftZone/BotSwitcherTitle` is a Button; `BotSwitcherTitleBinder.Awake` wires it to `BotSwitcherSheet.Open` found via `FindFirstObjectByType` (`/Users/ayan/Projects/Automation/Assets/Scripts/UI/BotSwitcherTitleBinder.cs:22-31`); title/avatar refresh on `OnActiveBotChanged` (L34-61).
2. `BotSwitcherSheet.PopulateRows` spawns one `BotSwitcherRowView` per Bot under `Manager.Instance.BotsRoot`, marking the row matching `ChatManager.Instance.CurrentBotId` selected (`/Users/ayan/Projects/Automation/Assets/Scripts/UI/BotSwitcherSheet.cs:127-163`).
3. Row tap → `ChatManager.Instance.SetActiveBot(botId)` then `Close()` (BotSwitcherSheet.cs:165-169).
4. `SetActiveBot` (`/Users/ayan/Projects/Automation/Assets/Scripts/Main/ChatManager.BotState.cs:104-129`) persists to PlayerPrefs `"LastSelectedBotForChats"` (L98), clears + reloads (details in §4).
5. Startup selection: `ResolveInitialActiveBot` (BotState.cs:283-303) — persisted choice, else first bot, else `OnEmptyState(NoBotsExist)`.

## 4. Option (a): channel switcher inside the existing chats screen

**Plug-in point**: `TopBar/CenterZone` is currently inactive and reserves an unused 360×140 center slot (anchors 0.5/0.5, pos y=-60) — the natural home for a WhatsApp|Telegram segmented control; the neighboring `ModeToggle` (`ReplyModeToggleBinder`) is the exact visual precedent for a two-state pill in this TopBar. Builder pattern to follow: `/Users/ayan/Projects/Automation/Assets/Editor/Screen_WhatsappHeaderRebuilder.cs` (built `BotSwitcherTitle` into `ChatsPanel/TopBar`, `[MenuItem("Tools/Bot Switcher/Rebuild Whatsapp Header")]` L21).

**State to reset on channel switch** — mirror `SetActiveBot` (BotState.cs:109-128), which is the proven full-reset choreography:
- stop `_syncWaitRoutine`; `_outbox = null` (per-bot disk outbox re-load); `Chats.Clear()` + `chatLookup.Clear()` + `OnChatListCleared`; `StopAllCoroutines()`; `_chatFetchesInFlight = 0`; `_chatListSyncing = false`; `ClearVideoThumbQueue()`; `ClearMediaDownloadQueue()`; then `BeginLoadForActiveBot()`.
- Per-chat state (already reset by `SelectChat`, ChatManager.cs:489-500): `currentChatId`, `_lastFetchedServerPage`, `_servedFromStore`, `seenMessageIds`, `_reactions` (ReactionStore), `_activeChatCache`, `_cachedQueue`, plus `_pendingFirstBatch`/`_pendingLiveSyncMessages` (L468-470). If a chat is open when the channel flips, the switch must also route through `ShowChatList` (ChatManager.cs:422).

**Channel-parametrization surface** (the real work):
- `GetCacheRoot()` = `BotCache/{CurrentBotId}` (BotState.cs:20-26) — needs a channel segment; `chats.json`, `ChatHistoryCache`, and outbox all live under it.
- `GetActiveProfileId()` hardcodes `bot.whatsappProfileId` (BotState.cs:142-147) — must select `telegramProfileId` per channel (`Bot.cs` already has the field).
- 11 hardcoded `https://wappi.pro/api/sync/` URLs → per-channel base (`tapi/sync`): ChatManager.cs:391, 525, 1102, 1175, 1812, 1933, 2023; ChatManager.DeleteChat.cs:52; ChatManager.ReactionSend.cs:66; ChatManager.ReactionResolve.cs:74; ChatManager.QuoteResolve.cs:96.
- Sync-window key suffix `"WhatsappSyncUntil"` (BotState.cs:150) + `WhatsAppSyncGate`/`IsWhatsAppSyncing` — per-channel key needed.
- Empty/syncing states: `WhatsAppTabStateResolver` (`/Users/ayan/Projects/Automation/Assets/Scripts/Main/WhatsAppTabState.cs:11`), `ComputeCurrentEmptyState` (BotState.cs:174-189), `EmptyStateView` hardcoded strings (EmptyStateView.cs:108-121, WhatsApp platform preselect L140).
- `TabRefreshGate` refresh currently keyed to tab 0 only (BottomTabManager.cs:170-171).
- Persist a `LastSelectedChannel` pref alongside `LastSelectedBotForChats` (BotState.cs:98).
- Per-channel feature gates: reactions, quote recovery (`messages/id/get`), avatars (GreenApiAvatarFetcher is WhatsApp-only), delete-chat, mark/read, suggestions/Вместе mode — availability on tapi unverified.

## 5. Option (b): separate Screen_Telegram — duplication forced

- `ChatManager` is a hard singleton (`Instance = this`, ChatManager.cs:196-198) whose serialized panels point at Screen_Whatsapp's ChatsPanel/MessagesPanel. A second screen means either a **second ChatManager instance** (breaks `ChatManager.Instance` for every consumer: ChatListView, MessageListView, ChatItemView, BotSwitcherTitleBinder, BotSwitcherSheet, DashboardPage via `ChatManager.Dashboard.cs`, reaction/quote/suggestion resolvers, BottomTabManager refresh) or **runtime retargeting** of the one manager's panels — which is the channel-switch state machine anyway, plus scene surgery.
- Full view-stack duplication: ChatsPanel subtree (Scroll, EmptyState, SyncingState, TopBar incl. BotSwitcherTitle + ModeToggle, Sheet_BotSwitcher, DeleteChatConfirmPanel, ReplyModeConfirmPopup) AND MessagesPanel subtree (MovingArea, chat TopBar, VideoPlayerPanel, PhotoViewerPanel, AttachmentPreviewScreen, EmojiPickerOverlay, ReactionBarOverlay).
- Singleton collisions beyond ChatManager: `SwipeToBack.Instance` + static `SwipeToBack.OnSlideOutComplete` (ChatManager.cs:200-211, 227); `BotSwitcherTitleBinder` binds to the first `BotSwitcherSheet` found via `FindFirstObjectByType` (BotSwitcherTitleBinder.cs:25) — two sheets bind nondeterministically.
- Event fan-out bug class: a second `ChatListView` subscribing to the same `ChatManager.Instance` events would render the SAME chats into both screens unless every event becomes channel-scoped.
- `ChatListPreWarmer` pre-warms only Screen_Whatsapp (ChatListPreWarmer.cs:7-8); a second screen needs its own pre-warm.
- Shared/static caches (`MediaCacheManager.Instance`, `ChatHistoryCache`, `QuotedMessageCache`) are fine either way.

## Recommendation: (a) channel switcher in the existing chats screen

Code-structure evidence:
1. **The swap machinery already exists and is proven**: `SetActiveBot` (BotState.cs:104-129) is a complete "change data source, reuse all views" reset; `SetActiveChannel` is a near-clone. Bot switching — a strictly bigger data swap than channel switching — is already done in-screen via a bottom sheet, not a separate screen.
2. **The architecture is one-singleton, event-driven**: every view binds to `ChatManager.Instance` events. A separate screen either duplicates 2 panel subtrees + ~7 overlay panels + collides with 3 singleton/lookup patterns, or degenerates into retargeting the same manager (i.e., the switcher with extra scene surgery).
3. **Caching is already per-bot-namespaced** (`BotCache/{botId}`) — adding a channel segment is one line in `GetCacheRoot`; option (b) still needs the same channel-aware manager work PLUS the duplicated hierarchy.
4. The dominant cost — channel-parametrizing ~11 URL sites, profile-id resolution, sync-gate keys, and empty states — is **identical under both options**; option (b) adds only liabilities on top.
5. Housekeeping for (a): remove or repoint the live Telegram tab (tabs[1], scene line 135599) via the `NavRestructureBuilder`-style SerializedObject rewire (NavRestructureBuilder.cs:384-405 shows the exact idiom), and delete the dead Screen_Telegram placeholder (or keep it out of `ReorderScreens`' list, NavRestructureBuilder.cs:422). Rename of Screen_Whatsapp → Screen_Chats is optional and touches many scene refs (ChatListPreWarmer, tab config) — defer unless asked.
