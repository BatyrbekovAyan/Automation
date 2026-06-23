# Codebase Structure

**Analysis Date:** 2026-06-23

## Directory Layout

```
Automation/
├── .claude/                        # Project-scoped Claude configuration (skills, rules, hooks)
├── .planning/                      # GSD workflow (phases, codebase docs)
├── Assets/
│   ├── Scenes/
│   │   └── Main.unity              # Single persistent scene — all UI canvas-based
│   ├── Scripts/
│   │   ├── Main/                   # Orchestration, pages, bot entity, settings, managers
│   │   │   ├── Manager.cs          # God-object: bot CRUD, API orchestration, auth flows
│   │   │   ├── ChatManager.cs      # Chat UI system (+ partials: .Outbox, .BotState, .MediaSend, etc.)
│   │   │   ├── Bot.cs              # Per-bot entity MonoBehaviour (profiles, workflows, persistence)
│   │   │   ├── BotSettings.cs      # 5-tab bot config UI (General | Business | Products | Services | Prompts)
│   │   │   ├── BotSettings.Auth.cs # QR/code auth flows (partial class)
│   │   │   ├── BotSettings/        # Reusable form primitives (EditableField, ScrollableTextArea, etc.)
│   │   │   ├── BotsPage.cs         # Bots list page (all/active filter)
│   │   │   ├── ProfilePage.cs      # User profile page
│   │   │   ├── BottomTabManager.cs # WhatsApp/Telegram/Profile tabs
│   │   │   ├── Secrets.cs          # Lazy-loaded API keys from secrets.json
│   │   │   ├── BusinessTypesSO.cs  # ScriptableObject for business type icons/colors
│   │   │   ├── PopupUI.cs          # Shared popup dialogs
│   │   │   └── [Input/Layout helpers]: SnappyFlickScrollRect, EventAbsorber, SwipeToBackBotSettings, WhatsappCodeTimer, etc.
│   │   ├── Chat/                   # Message pipeline, media, caching, gestures, reactions, replies
│   │   │   ├── [Data Models]: RawMessage, NormalizedMessage, ChatDialog, ChatsResponse, MessagesResponseRaw, MessageType, MessageReaction, DeliveryStatus, etc.
│   │   │   ├── [Caching]: ChatHistoryCache, MediaCacheManager, QuotedMessageCache, ReactionTargetCache, ChatListPreWarmer
│   │   │   ├── [Normalization]: ReplyParser, ReactionParser, UnicodeEmojiConverter, ChatPreviewFormatter
│   │   │   ├── [Gesture Handlers]: SwipeToBack, SwipeToClose, SwipeToReply, SwipeToDelete, DragShield, ClickPassthrough, ScrollClickBlocker
│   │   │   ├── [Controllers]: ChatSender (message send), VideoController, AudioController, EmojiPickerController, ReactionBarController, MessageBubbleLongPress
│   │   │   ├── [Media Processing]: VideoThumbnailExtractor, VideoThumbQueue, VideoConverter, JpegThumbnailDecoder, ResizeEdgeRepair, WebPSignature
│   │   │   ├── [Rendering Helpers]: MediaBubbleSize, AudioBubbleMath, ScrollFabMath, MediaGhostMatch, OutlineFrame, NativeHairline, MirrorSize
│   │   │   ├── [Platform Bridges]: AndroidBridge, IOSBridge, IOSAudioFix, GreenApiAvatarFetcher
│   │   │   ├── [UI Builders]: ExpandableInput, MessagesBottomPanel, KeyboardAwarePanel, QuickReplyPanel, QuickReplyButton, PhotoViewer, AttachmentPreviewScreen, AttachSheet, EmojiPickerController
│   │   │   ├── [Views]: MessageHeaderView, DateSeparatorView, UnreadSeparatorView
│   │   │   ├── [Utilities]: LinkScraper, TMPLinkHandler, WrappableLinkFormatter, ChatTicksFallbackRegistrar, Base64Encoder
│   │   │   ├── [Outbox/Reactions]: OutboxStore, OutgoingReaction, ReactionStore, ReactionSummary, ReactionEmojiCatalog, ReactionTargetResolver, MessageMediaMerge
│   │   │   └── [Scrolling/Layout]: ScrollTargetMath, ServerPageMath, ScrollToBottomFab, FirstScreenBudget, KeyboardScrollFix
│   │   ├── UI/                     # ViewModels and list/item view renderers
│   │   │   ├── ChatViewModel.cs    # Chat list binding model (title, last message, unread, avatar)
│   │   │   ├── MessageViewModel.cs # Message bubble binding model (text, media, reactions, delivery, quote)
│   │   │   ├── ChatListView.cs     # Prefab spawner for chat rows (driven by ChatManager events)
│   │   │   ├── ChatItemView.cs     # Per-chat-row view (title, avatar, last message, unread badge)
│   │   │   ├── MessageListView.cs  # Prefab spawner for message bubbles (driven by ChatManager events)
│   │   │   ├── MessageItemView.cs  # Per-message bubble view (text, media, reactions, link taps, delivery ticks)
│   │   │   ├── ReactionPillView.cs # Reaction emoji pill renderer
│   │   │   ├── AudioWaveform.cs    # Audio bubble waveform visualization
│   │   │   ├── BotSwitcherSheet.cs # Bottom sheet for switching active bot
│   │   │   ├── BotSwitcherRowView.cs # Bot row in switcher
│   │   │   ├── BotSwitcherTitleBinder.cs # Bind active bot name to top bar
│   │   │   ├── ChatSearchBar.cs    # Chat list search/filter
│   │   │   ├── ChatDeleteConfirm.cs # Swipe-to-delete confirmation dialog
│   │   │   ├── EmptyStateView.cs   # Placeholder (no chats, no messages)
│   │   │   ├── SyncingView.cs      # "Syncing…" spinner
│   │   │   └── [Layout/Sizing]: TMPMaxWidthLayoutElement, SheetDragDismiss
│   │   └── Converters/             # Document-to-text utilities
│   │       ├── TableToTextConverter.cs
│   │       └── XmlToTextConverter.cs
│   ├── Editor/                     # Programmatic UI builders (run via [MenuItem])
│   │   ├── [BotSettings Builders]: BotSettingsRebuilder, BotSettingsConfirmChangePopupBuilder, BotSettingsDeleteBotPopupBuilder, BotSettingsScrollableTextAreaBuilder, BotSettingsStickyAddButtonBuilder
│   │   ├── [UI Builders]: ChatDeleteConfirmBuilder, ChatDeleteConfirmInstaller, ChatsSearchBarBuilder, BotSwitcherSheetBuilder, BotSwitcherTitleAvatarRebuilder, BotSwitcherTitleNameClamper, BotSwitcherRowView, EmptyStateViewBuilder, PreviewDescriptionBuilder
│   │   ├── [Reaction Builders]: ReactionBarBuilder, MessageReactionPillBuilder, ChatTicksSpriteAssetBuilder
│   │   ├── [Attachment Builders]: AttachSheetBuilder, OutlineFrameBuilder, SheetDragDismissWirer, SwipeToReplyAttacher
│   │   ├── [Wiring]: BotSettingsSwipeWirer, FixIOSBuildSettings
│   │   ├── [Utilities]: ArchitectureExporter, ClaudeTestBridge (test runner bridge), InputFieldMigrator, PreparingSpinnerWirer, ChatItemDeleteButtonTweakBuilder, UnreadMarkersBuilder, ChatItemUnreadBadgeBuilder, AssignChatBackground, Screen_WhatsappHeaderRebuilder
│   │   └── [Hooks]: validate-cs.sh (C# quality), gsd-* (GSD workflow enforcement)
│   ├── Prefabs/                    # Reusable prefab templates
│   │   ├── ChatItem.prefab         # Chat row (avatar, title, last msg, unread badge)
│   │   ├── MessageText.prefab      # Text message bubble
│   │   ├── MessageImage.prefab     # Image bubble (with caption)
│   │   ├── MessageVideo.prefab     # Video bubble (play button, thumbnail)
│   │   ├── MessageAudio.prefab     # Audio bubble (waveform, duration)
│   │   ├── MessageDocument.prefab  # Document bubble (icon, filename, size)
│   │   ├── MessageSticker.prefab   # Sticker bubble (transparent bg)
│   │   ├── BotCard.prefab          # Bot in bot list (icon, name, status, toggle)
│   │   ├── BotSettings.prefab      # Settings screen (shared for all bots)
│   │   ├── ReactionPill.prefab     # Emoji reaction pill
│   │   └── [Dialog Prefabs]: ChatDeleteConfirm, ChatSearchBar, BotSwitcherSheet, EmptyState, PreparingSpinner, AttachSheet, OutlineFrame
│   ├── Resources/                  # Runtime-loaded (not for SerializeField refs)
│   │   └── [Emoji/Sprite Assets]
│   ├── StreamingAssets/            # secrets.json (gitignored), language strings, other static data
│   ├── Settings/                   # URP, audio, UI canvas settings, input actions
│   ├── Images/                     # Textures (LFS)
│   ├── Animations/                 # DOTween scripts (state machine configs)
│   ├── Plugins/                    # Native plugins (NativeFilePicker, NativeGallery, DOTween, unity.webp)
│   ├── Tests/                      # EditMode tests
│   │   └── Editor/Chat/            # Chat system tests (message pipeline, normalization, caching)
│   ├── TextMesh Pro/               # TMP font assets, materials
│   └── unity.webp/                 # WebP codec plugin
├── ProjectSettings/                # Unity project config (quality, layers, tags, input, build settings)
├── Packages/
│   └── nuget-packages/             # NuGet packages (NuGetForUnity managed)
│       ├── NuGet.config            # NuGet feed config
│       ├── packages.config         # Installed package list
│       └── InstalledPackages/      # Package assemblies (.dll)
├── Tools/                          # Build scripts, test runners, utilities
│   ├── run-tests-headless.sh       # Headless EditMode test runner (Editor closed)
│   ├── test-output/                # Test results (gitignored)
│   └── [Asset generation]: pack_param.py (doodle BG packing), assign-emoji-sprite-codepoints.py
├── .claude/                        # Project skills, rules, hooks
├── .planning/                      # GSD workflow docs
├── Library/                        # Unity cache (gitignored)
├── Logs/                           # Build/runtime logs (gitignored)
├── Temp/                           # Temp files (gitignored)
├── CLAUDE.md                       # Architecture guide (this file)
└── Automation.sln                  # Visual Studio solution (auto-generated, gitignored)
```

## Directory Purposes

**Assets/Scripts/Main/:**
- Purpose: Orchestration, page state management, bot entity lifecycle, settings UI
- Contains: Manager (API hub), ChatManager (chat lifecycle), Bot (per-bot entity), BotSettings (config screens), pages (BotsPage, ProfilePage)
- Key files: `Manager.cs`, `ChatManager.cs`, `Bot.cs`, `BotSettings.cs`, `Secrets.cs`
- Who edits: Feature work on bot creation/settings, chat tab management, auth flows

**Assets/Scripts/Chat/:**
- Purpose: Message pipeline, caching, normalization, media handling, reactions, replies, gestures
- Contains: 100+ files covering RawMessage→NormalizedMessage→MessageViewModel, media download/cache, emoji/link/reaction resolvers, gesture handlers (swipe, long-press), platform bridges
- Key files: `RawMessage.cs`, `NormalizedMessage.cs`, `ChatHistoryCache.cs`, `MediaCacheManager.cs`, `ReplyParser.cs`, `ReactionStore.cs`, `VideoThumbnailExtractor.cs`, `SwipeToBack.cs`
- Who edits: Message display, media, reactions, replies, performance optimizations, gesture logic

**Assets/Scripts/UI/:**
- Purpose: ViewModel binding models and list/item view renderers
- Contains: ChatViewModel, MessageViewModel, ChatListView, MessageListView, ChatItemView, MessageItemView, reaction/audio/swipe UI views
- Key files: `ChatViewModel.cs`, `MessageViewModel.cs`, `ChatListView.cs`, `MessageItemView.cs`
- Who edits: Chat row display, message bubble rendering, attachment UI, empty states, search

**Assets/Scripts/Converters/:**
- Purpose: Document format conversion (tables, XML to plain text)
- Contains: TableToTextConverter, XmlToTextConverter
- Who edits: Document parsing, rich-text extraction

**Assets/Editor/:**
- Purpose: Programmatic UI construction via [MenuItem] commands
- Contains: 30+ builders (BotSettingsRebuilder, ChatDeleteConfirmBuilder, etc.), wiring scripts, build fixes
- Key files: `ArchitectureExporter.cs` (codebase analysis), `ClaudeTestBridge.cs` (test runner), builder pattern tools
- Who edits: Adding new UI screens (via builder pattern), rewiring serialized refs, build system tweaks

**Assets/Prefabs/:**
- Purpose: Reusable MonoBehaviour templates (chat rows, message bubbles, dialogs)
- Contains: ChatItem, MessageText/Image/Video/Audio/Document/Sticker, BotCard, BotSettings, ReactionPill, dialogs
- Key files: ChatItem.prefab, MessageText.prefab, BotSettings.prefab, ChatDeleteConfirm.prefab
- Who edits: Adding new message types, tweaking bubble layouts/colors, dialog UI

**Assets/StreamingAssets/:**
- Purpose: Shipped-with-app static data (API keys via secrets.json, language strings)
- Contains: secrets.json (gitignored, API keys), language/locale data
- Key files: `secrets.json.example` (template)
- Who edits: Adding new API keys, language strings

**Assets/Tests/Editor/Chat/:**
- Purpose: EditMode tests for message pipeline, caching, normalization
- Contains: Message parsing tests, reply resolution tests, reaction store tests, cache validation
- Who edits: Adding test coverage for new message types, normalization logic, reply/reaction edge cases

**Assets/Settings/:**
- Purpose: Universal Render Pipeline config, Audio Mixer, UI Canvas settings, Input Action Map
- Contains: URP asset, audio settings, canvas reference resolution
- Who edits: Graphics quality tuning, audio mixer tweaks, input handling

**ProjectSettings/:**
- Purpose: Unity project metadata (quality levels, layers, tags, input, build settings)
- Contains: QualitySettings.asset, PhysicsSettings.asset, InputManager.asset, PlayerSettings.asset (iOS/Android build config)
- Who edits: Build target config, quality levels, input mappings

**Packages/nuget-packages/:**
- Purpose: NuGet package management (Newtonsoft.Json, etc.)
- Contains: NuGet.config (feed config), packages.config (installed list), InstalledPackages/ (assemblies)
- Key files: `NuGet.config`, `packages.config`
- Who edits: Adding/updating NuGet dependencies

**Tools/:**
- Purpose: Build automation, test runners, asset generation
- Contains: `run-tests-headless.sh` (headless test runner), `pack_param.py` (doodle BG packing), emoji sprite codepoint assigner
- Who edits: Test automation, build pipeline, asset tooling

**.claude/:**
- Purpose: Project-scoped Claude Code configuration (skills, rules, hooks)
- Contains: `SKILL.md` files (unity-ui-builder, unity-api-integration, bot-persistence, chat-data-flow, mobile-app-ui-design), `rules/*.md` (unity-general, networking, ui-scripts, editor-scripts), `hooks/*.sh` (validate-cs.sh, gsd-*)
- Who edits: Defining project-wide coding standards, skill-driven patterns

**.planning/:**
- Purpose: GSD workflow state and codebase analysis
- Contains: Phase plans, codebase maps (ARCHITECTURE.md, STRUCTURE.md, STACK.md, CONVENTIONS.md, TESTING.md, CONCERNS.md, INTEGRATIONS.md)
- Who edits: GSD tool output only (never hand-edit)

## Key File Locations

**Entry Points:**

- **Application Start:** `Assets/Scenes/Main.unity` (single scene)
- **First Code:** `Manager.Awake()` → `Manager.Start()` → `LoadBots()`, `ChatManager.Awake()` → `ChatManager.Start()` → `ShowChatList(true)`
- **Chat Open:** `ChatItemView.OnPointerClick()` → `ChatManager.SelectChat(chatId)` → `OpenChatRoutine()`
- **Message Send:** `ExpandableInput.BuildAndSend()` → `ChatManager.SendMessage()`

**Configuration:**

- **API Keys & Tokens:** `Assets/StreamingAssets/secrets.json` (gitignored; load via `Secrets.cs`)
- **Bot Config:** `PlayerPrefs` (keys = `{botName}FieldName`, e.g., `Bot0Name`, `Bot0Products0`)
- **Chat Cache:** `persistentDataPath/all_chats_cache.json`, `persistentDataPath/{botId}_{chatId}.json`
- **Media Cache:** `persistentDataPath/media/{hash_of_url}`
- **Build Settings:** `ProjectSettings/PlayerSettings.asset` (iOS/Android target config)

**Core Logic:**

- **Message Pipeline:** `ChatManager.Normalize()` (RawMessage → NormalizedMessage), `ChatManager.CreateViewModel()` (NormalizedMessage → MessageViewModel)
- **Chat Sync:** `ChatManager.OpenChatRoutine()` (Prep/Slide/Populate state machine), `ChatManager.SyncLatestMessages()` (live updates)
- **Message Render:** `MessageItemView.OnEnable()` (bind VM), `MessageItemView.BindData()` (set text/media/reactions)
- **Quote Resolution:** `ReplyParser.FromSnapshot()` (detect echo), `ChatManager.QuoteResolve.cs` (fetch missing quotes)
- **Reaction Store:** `ReactionStore._byMessageAndReactor` (Dict dedup), `OnMessageReactionsChanged` event (re-render pills)

**Testing:**

- **Test Harness:** `Assets/Editor/ClaudeTestBridge.cs` (trigger via `Temp/claude/run-tests.trigger`)
- **Headless Runner:** `Tools/run-tests-headless.sh` (Editor closed, batch mode)
- **Test Output:** `Tools/test-output/` (gitignored)
- **Test Code:** `Assets/Tests/Editor/Chat/` (no asmdef, compiles into Assembly-CSharp-Editor)

## Naming Conventions

**Files:**
- PascalCase: `Manager.cs`, `ChatManager.cs`, `MessageItemView.cs`, `ReplyParser.cs`
- Suffix patterns: `*View.cs` (UI renderer), `*ViewModel.cs` (binding model), `*Manager.cs` (orchestrator singleton), `*Parser.cs` (data transformer), `*Cache.cs` (persistence), `*Builder.cs` (Editor tool)
- Partial files: `ChatManager.Outbox.cs`, `ChatManager.BotState.cs`, `BotSettings.Auth.cs` (split large components)

**Directories:**
- `Assets/Scripts/{Main,Chat,UI,Converters}/` — functional groups
- `Assets/Scripts/Main/BotSettings/` — subcomponent primitives
- `Assets/Editor/` — Editor-only tools
- `Assets/Prefabs/` — shared templates
- `.claude/skills/`, `.claude/rules/`, `.claude/hooks/` — project configuration

**Classes & Methods:**
- PascalCase: `public class ChatManager`, `public void SendMessage()`, `public Property => field;`
- Private fields: `private int _field` or `private int field` (both patterns used; prefer `_` prefix for clarity)
- Coroutines: `public IEnumerator SendMessageRoutine()`

**Variables & Properties:**
- camelCase locals: `var messages = new List<MessageViewModel>()`
- Public properties: `public string ChatId { get; }` (read-only by default)
- Booleans: `isIncoming`, `hasQuote`, `fromMe` (prefix with `is`/`has`/`from`)

**Constants:**
- UPPER_SNAKE_CASE: `public const int MessagesPerPage = 50;`, `private const string UnauthedProfileSentinel = "-1";`

## Where to Add New Code

**New Message Type (e.g., "GIF"):**
1. Add to `MessageType` enum in `Assets/Scripts/Chat/MessageType.cs`
2. Add case in `ChatManager.Normalize()` (map from raw type to MessageType)
3. Create `MessageGif.prefab` in `Assets/Prefabs/`
4. Add renderer in `MessageItemView.BindData()` (switch on messageType)
5. Add Editor builder if custom layout: `Assets/Editor/MessageGifBuilder.cs` (follow pattern from `MessageReactionPillBuilder.cs`)
6. Add EditMode test: `Assets/Tests/Editor/Chat/MessageGifNormalizationTests.cs`

**New API Endpoint (Wappi, n8n, or GreenAPI):**
1. Add response model class to `Assets/Scripts/Chat/` (e.g., `NewResponseModel.cs`), marked `[System.Serializable]`
2. Add coroutine to `Manager.cs` or `ChatManager.cs` following the pattern:
   ```csharp
   private IEnumerator FetchNewEndpoint(string param, System.Action<T> callback)
   {
       string url = $"{BASE_URL}endpoint/{param}";
       using (var request = UnityWebRequest.Get(url))
       {
           request.SetRequestHeader("Authorization", wappiAuthToken);
           request.timeout = 30;
           yield return request.SendWebRequest();
           
           if (request.result != UnityWebRequest.Result.Success)
           {
               Debug.LogError($"[{request.responseCode}] {url}: {request.error}");
               callback?.Invoke(default);
               yield break;
           }
           
           var data = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
           callback?.Invoke(data);
       }
   }
   ```
3. Hook the coroutine into existing flows (bot creation, message send, etc.)
4. Add error logging + UI feedback (e.g., "Connection failed" pill)

**New Page (e.g., Settings screen):**
1. Create script `Assets/Scripts/Main/SettingsPage.cs` with `[SerializeField]` references to UI elements
2. Create prefab `Assets/Prefabs/SettingsPage.prefab` and drag script onto root
3. Add panel GO to Main.unity canvas (sibling to other pages)
4. Wire navigation: Manager.ShowSettingsPage() → page.SetActive(true), hide others
5. If using swipe-back gestures: add `SwipeToBackBotSettings` child, wire via `Assets/Editor/SwipeToBackWirer.cs` builder
6. If using custom form primitives: reuse from `Assets/Scripts/Main/BotSettings/` or create new builder in Editor

**New Gesture Handler (swipe, long-press, etc.):**
1. Create script `Assets/Scripts/Chat/MyGestureHandler.cs` with Input.touches polling
2. Implement direction detection + momentum (see `SwipeToBack.cs` for reference)
3. Wire into UI element that needs the gesture (MessageItemView, ChatItemView, etc.)
4. Test on device (touch input differs from mouse in Editor)

**New Unit Test:**
1. Create `Assets/Tests/Editor/Chat/MyComponentTests.cs` (no asmdef needed)
2. Use Unity.TestFramework (NUnit3 runner)
3. Pattern:
   ```csharp
   [TestFixture]
   public class MyComponentTests
   {
       [Test]
       public void TestBehavior()
       {
           var component = new MyComponent();
           var result = component.DoSomething();
           Assert.AreEqual(expected, result);
       }
   }
   ```
4. Run via `Tools/run-tests-headless.sh` (Editor closed) or `Temp/claude/run-tests.trigger` (Editor open)

**Utilities & Helpers:**
- Shared logic (Emoji conversion, link scraping, math): `Assets/Scripts/Chat/UtilityName.cs`
- Per-platform: `Assets/Scripts/Chat/AndroidBridge.cs` or `IOSBridge.cs`
- Data transformation: `Assets/Scripts/Converters/` or inline in `ChatManager.Normalize()`
- Caching: follow `ChatHistoryCache.cs` pattern (serialize to JSON, load from disk)

## Special Directories

**Assets/Temp/ (gitignored):**
- Purpose: Runtime temp files (test trigger, summaries, media cache, debug dumps)
- Generated: Yes (at runtime)
- Committed: No
- Examples: `Temp/claude/run-tests.trigger`, `Temp/claude/test-summary.json`, `Temp/UnityLockfile`

**Library/ (gitignored):**
- Purpose: Unity cache (compiled assemblies, asset metadata, editor state)
- Generated: Yes (Unity maintains automatically)
- Committed: No

**Assets/Editor/BuildHeadless/ (if exists):**
- Purpose: Standalone CLI build output (for CI/CD)
- Generated: By build script
- Committed: No

**ProjectSettings/:**
- Purpose: Unity project settings (version-controlled)
- Generated: No (user config, checked in)
- Committed: Yes

---

*Structure analysis: 2026-06-23*
