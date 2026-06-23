# Codebase Concerns

**Analysis Date:** 2026-06-23

## Tech Debt

**God-Object Manager.cs:**
- Issue: `Manager.cs` (3238 lines) serves as the hub for bot creation, chat CRUD, all external API calls (Wappi, n8n, Green API), workflow management, and core orchestration. Single point of failure; changes touch cross-cutting concerns (UI, networking, persistence).
- Files: `Assets/Scripts/Main/Manager.cs`
- Impact: Difficult to test, high change risk, makes the codebase harder to navigate. Adding any new external service or bot lifecycle feature requires modifications here.
- Fix approach: Extract API integrations into dedicated service classes (`WappiService`, `N8nService`, `GreenApiService`) with dependency injection. Move bot CRUD to a `BotRepository`. Extract workflow state machine to `WorkflowStateManager`. This is a multi-phase refactor.

**PlayerPrefs-Based Entity Persistence:**
- Issue: All bot data persisted entirely in PlayerPrefs using GameObject.name as key (e.g., `Bot0Name`, `Bot0Products0`, `Bot0Product0Price`). String keys are untyped and prone to collision. No schema versioning; adding/removing fields breaks backward compatibility silently.
- Files: `Assets/Scripts/Main/Bot.cs` (deletion at lines 156-221 shows the scope), `Assets/Scripts/UI/BotSwitcherTitleBinder.cs`, `Assets/Scripts/UI/BotSwitcherRowView.cs`
- Impact: Data loss risk if key names mismatch between read and write sites. No migration path for schema changes. Difficult to debug (keys are opaque strings). No transactions—partial writes during app crash leave invalid state.
- Fix approach: Migrate to a JSON-based bot repository (`BotRepository.cs`) serialized to `persistentDataPath/bots.json` with version field. Provide migration functions for legacy PlayerPrefs keys. Implement atomic writes via .tmp + File.Replace pattern (already used by `OutboxStore`). Add unit tests for round-trip serialization.

**Large Monolithic Views:**
- Issue: `MessageItemView.cs` (4451 lines) handles message rendering across all types (text, image, video, audio, sticker, document, quoted reply, reactions, link preview). Single component for ~10 distinct concerns (media binding, text layout, reactions, quoted-card rendering, link scraping, gesture handling).
- Files: `Assets/Scripts/UI/MessageItemView.cs`
- Impact: Hard to understand code flow, difficult to test individual concerns, high risk of regression. Adding a new message type or media handler requires deep edits.
- Fix approach: Extract media-specific logic into `IMessageMediaBinder` implementations (`ImageMediaBinder`, `VideoMediaBinder`, `AudioMediaBinder`, etc.). Move reactions to `ReactionBubbleHandler`. Move link preview to `LinkPreviewHandler`. Move quoted-reply rendering to `QuotedCardRenderer`. Keep the main class as a composition/coordinator.

**Manager.cs Partial Classes Span Multiple Files:**
- Issue: `ChatManager` is split across `ChatManager.cs` (2052 lines), `ChatManager.BotState.cs`, `ChatManager.DeleteChat.cs`, `ChatManager.MediaSend.cs`, `ChatManager.QuoteResolve.cs`, `ChatManager.ReactionResolve.cs`. Partial classes make the real scope of the class hidden; code navigation requires opening multiple files.
- Files: `Assets/Scripts/Main/ChatManager*.cs`
- Impact: Code review is harder; understanding the full lifecycle requires reading all partials. IDE search can miss cross-partial references.
- Fix approach: This is acceptable for now as long as each partial focuses on one concern (media send, quote resolution, etc.). Document this in CLAUDE.md. If the class grows further, consider extracting services instead of adding more partials.

## Known Bugs

**Wappi Server Bug: Concurrent /media/download Crosses Responses:**
- Symptoms: Wrong image or video appears in message bubble; frequently "previous chat's media in current chat."
- Files: No explicit guard in download code—vulnerability is at the protocol level.
- Trigger: Opening multiple chats rapidly or media bubbles loading in background during chat-open.
- Workaround: `ChatManager.MediaSend.cs` and media-load paths must serialize downloads strictly (one at a time). Currently no centralizing queue; downloads scattered across `MessageItemView` and platform bridges may race.
- Current status: Documented in CLAUDE.md memory (`project_wappi_media_download_crossing.md`). Not yet fully serialized in the codebase—download queue needs implementation/audit.

**Wappi Server Bug: Concurrent /messages/get Crosses Responses:**
- Symptoms: Another chat's messages appear in the open chat; "previous chat's data spliced into current chat."
- Files: `Assets/Scripts/Main/ChatManager.cs` (guard at lines 563, 1107, 1194); `Assets/Scripts/Chat/CrossChatResponseGuard.cs`
- Trigger: Chat-list row-resolve backfill races with chat-open fetch when both hit messages/get concurrently.
- Workaround: `_chatFetchesInFlight` gate (lines 137, 515, 517, 1092, 1094, 1165, 1167) ensures message/get calls wait for previous fetches to finish. `CrossChatResponseGuard.IsForDifferentChat()` validates response belongs to current chatId and discards if crossed.
- Current status: **Confirmed and guarded**. The backfill drain waits on `_chatFetchesInFlight > 0` (line 1303, 3-second timeout); guards check response chatId at lines 563, 1107, 1194. Risk is LOW if the pattern is consistent; HIGH if new message fetches bypass the gate.

**Quoted Reply Card Complexity & Data Loss:**
- Symptoms: (1) empty quoted-card on first chat entry; (2) bubble too wide on re-entry (full text instead of snippet); (3) quote body duplicates sender's own text; (4) real quote text missing, shows as "Message" placeholder.
- Files: `Assets/Scripts/Chat/ReplyParser.cs`, `Assets/Scripts/Chat/QuotedMessageCache.cs`, `Assets/Scripts/Main/ChatManager.QuoteResolve.cs`, `Assets/Scripts/UI/MessageItemView.cs`
- Cause: Wappi's `reply_message` snapshot is present in only ~50% of replies and its body sometimes echoes the reply sender's own text instead of the quoted original. Extraction requires: (1) cache-by-id lookup (unreliable on first load); (2) snapshot extraction (lossy); (3) fallback fetch via `messages/id/get` (slow, adds API load).
- Current status: **496/496 unit tests green** (per CLAUDE.md memory). Four-layer mitigation in place: `ReplyParser.BackfillFromCache` on open, `ReplyParser.FromSnapshot` body-echo detection, `QuotedMessageCache` persistent caching, `ChatManager.QuoteResolve.cs` async fetch with retries. But the complexity is high—any change to payload parsing or cache invalidation risks silent failures.

**Reply Snapshot Body Echo (Confirmed Wappi Quirk):**
- Symptoms: Quoted message shows the replying message's text instead of the original.
- Files: `Assets/Scripts/Chat/ReplyParser.cs` line 30 detects it: `snapshot body == own raw body`; blanks text to trigger fetch.
- Trigger: Wappi API behavior; not reproducible on demand.
- Current status: **Detected and handled**. Logic is in `ReplyParser.FromSnapshot()`. Risk is LOW as long as body-echo detection stays in place.

## Security Considerations

**Secrets in Assets/StreamingAssets/secrets.json (Gitignored):**
- Risk: API keys, tokens, and service credentials stored in plaintext JSON. If the .gitignore is accidentally removed or a contributor pushes secrets, the keys are exposed.
- Files: `Assets/Scripts/Main/Secrets.cs` (loader), `Assets/StreamingAssets/secrets.json` (gitignored but exists in dev builds).
- Current mitigation: `.gitignore` entry prevents commits; CLAUDE.md forbids hardcoding keys; `Secrets.cs` uses lazy-load pattern to defer access.
- Recommendations:
  1. Add a pre-commit hook that scans staged files for patterns like `"sk-"`, `"api_key"`, `"wappiAuthToken"` and fails the commit if found (already in `.claude/hooks/` but verify it runs).
  2. Provide `Assets/StreamingAssets/secrets.json.example` with dummy values so contributors know the shape (verify this exists).
  3. Consider using environment variables at runtime instead of JSON (needed for CI/CD; not urgent for mobile dev builds).
  4. Document in CLAUDE.md that `secrets.json` is NOT committed and must be set up locally per developer.

**No Request Timeout Mitigation:**
- Risk: Network requests that hang indefinitely can freeze the UI. `UnityWebRequest` has a timeout field but it's not uniformly set across all API calls.
- Files: Spot check in `ChatManager.cs` line 378, `ChatManager.MediaSend.cs` line 332, others scattered across `MessageItemView`, `EmojiPatchService`, etc.
- Current mitigation: Most coroutine-based sends use `using` blocks so they eventually dispose. No explicit timeout set.
- Recommendations: Add a networking convention (already in `.claude/rules/networking.md`) that ALL `UnityWebRequest` calls set `request.timeout = 30`. Audit all API coroutines and add if missing. Consider a timeout wrapper utility.

**No Input Validation on Bot Names & Product Names:**
- Risk: User-provided strings (bot name, product name, description, business type) are persisted in PlayerPrefs and Wappi without sanitization. Could cause injection attacks if Wappi ever reflects these values back in responses or if they're used in n8n workflow payloads.
- Files: `Assets/Scripts/Main/Manager.cs` (bot creation), `Assets/Scripts/Main/BotSettings.cs` (product/service editing)
- Current mitigation: TMP input fields have no explicit validation rules shown.
- Recommendations: Add input validators for length, character set, and escape-sequence handling before persistence. Validate before sending to Wappi/n8n. Add unit tests for edge cases (empty strings, very long strings, special characters, Unicode).

## Performance Bottlenecks

**MessageItemView Media Decode Loop (Per-Frame Budgeting):**
- Problem: `MessageItemView.AcquireDecodeSlot()` (rough location ~line 2263) decodes JPEG/WebP media in a per-frame loop to avoid freezing. With 50+ messages and async downloads, decode queue can back up and cause visible jank during scroll.
- Files: `Assets/Scripts/UI/MessageItemView.cs`
- Cause: Synchronous JPEG/WebP decode happens on main thread; even with per-frame throttling, large images on slow devices block the frame.
- Improvement path: (1) Profile on low-end Android device (e.g., Moto G7) to quantify freeze. (2) If significant, use `Job` system or a background decode thread (carefully—WebP.dll may not be thread-safe). (3) Consider pre-scaling images to screen width before decode to reduce memory pressure.

**Chat List Sync Pagination & Memory:**
- Problem: `ChatManager.SyncAllChats()` fetches the full chat list into memory without pagination. If a user has 1000+ chats, the response JSON is large and the `Chats` list grows unbounded.
- Files: `Assets/Scripts/Main/ChatManager.cs` (SyncAllChats + ParseChatsJson)
- Cause: API endpoint returns all chats in one response; no server-side pagination support.
- Improvement path: (1) Confirm Wappi's `chats/filter` endpoint does NOT support pagination (ask Wappi). (2) If it does, implement lazy-loading with `loadMore` pagination similar to `GetMessagesRoutine`. (3) If not, consider caching on-disk and only loading active bots' chats to memory. (4) Profile on a test account with 100+ chats to see impact.

**Emoji Patch Service Download Concurrency:**
- Problem: `EmojiPatchService.cs` (line ~328) downloads emoji sprite assets asynchronously. If multiple chats have missing emojis, downloads queue in sequence but the dequeue check uses polling (`yield return null` waiting).
- Files: `Assets/Scripts/Chat/EmojiPatchService.cs`
- Cause: Naive polling pattern instead of event-based completion signaling.
- Improvement path: Replace the `while (!done) yield return null` pattern with Unity event callbacks or `await` (if Task-based). Document this as a low-priority optimization—emoji load is infrequent.

## Fragile Areas

**ReplyParser & QuotedMessageCache Coordination:**
- Files: `Assets/Scripts/Chat/ReplyParser.cs`, `Assets/Scripts/Chat/QuotedMessageCache.cs`, `Assets/Scripts/Main/ChatManager.QuoteResolve.cs`, `Assets/Scripts/UI/MessageItemView.cs`
- Why fragile: Quoted message resolution spans 4 layers (parser, cache, fetch, render) with multiple fallback paths (snapshot → cache → API fetch → placeholder). Changes to one layer (e.g., cache TTL or fetch retry logic) silently affect all others. Test coverage exists but is integration-heavy.
- Safe modification: Before changing ReplyParser or QuotedMessageCache, (1) run `Assets/Tests/Editor/Chat/` tests headless via `Tools/run-tests-headless.sh`. (2) Manually open a chat with 5+ replies where snapshots are missing (force a slow network condition to see retries). (3) Verify quoted cards show correct text, sender, and thumbnail. (4) Check that cache is actually written to disk at `persistentDataPath/quoted_messages.json`.
- Test coverage: ReplyParser has unit tests (parser logic). QuotedMessageCache has save/load logic tested. ChatManager.QuoteResolve integration with Wappi is not fully mocked (risk).

**CrossChatResponseGuard & _chatFetchesInFlight Gate:**
- Files: `Assets/Scripts/Chat/CrossChatResponseGuard.cs`, `Assets/Scripts/Main/ChatManager.cs` (lines 137, 515, 517, 1092, 1094, 1165, 1167, 1303)
- Why fragile: The gate is a reference counter (`_chatFetchesInFlight`) incremented at request start and decremented at response end. If ANY new messages/get coroutine is added that skips the increment/decrement, or if an exception path forgets to decrement, the gate locks permanently (3-second timeout then proceeds, leaving stale data).
- Safe modification: (1) Any new messages/get call MUST wrap with `_chatFetchesInFlight++` at start and `Mathf.Max(0, _chatFetchesInFlight--)` at response. (2) Use try/finally to ensure decrement on error. (3) Add a comment linking the new coroutine to the gate's purpose. (4) Search for existing calls and ensure all increment/decrement in pairs.
- Test coverage: Gate is exercised during chat-open + pagination tests, but there's no explicit test for "gate locks if decrement is skipped." Consider adding a test that verifies backfill correctly waits for open-fetch to complete.

**Bot PlayerPrefs Keys & Deletion Cascade:**
- Files: `Assets/Scripts/Main/Bot.cs` (DeleteBot at lines 154-232), `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scripts/Chat/ChatHistoryCache.cs`
- Why fragile: Bot deletion requires clearing ~20+ PlayerPrefs keys, evicting `ChatHistoryCache`, and calling `Manager.DeleteProfilesAndWorkflows()`. If any key is missing from the deletion list, orphaned data lingers. If the Wappi delete fails partway through, keys are deleted locally but profiles remain active on Wappi.
- Safe modification: (1) Maintain a complete list of all PlayerPrefs keys written by Bot creation (in CLAUDE.md Skills or a constant). (2) Verify all keys are deleted in Bot.DeleteBot(). (3) Add a unit test that creates a bot, writes all expected keys, deletes, and asserts all keys are gone. (4) Wrap Wappi profile/workflow deletes in a transaction or add a "rollback" mechanism (store deletion intent + retry on app restart).
- Test coverage: No explicit test for complete bot deletion. The outbox recovery system handles persisted messages, but bot metadata cleanup is untested.

**Media Cache Aliasing & URL Identity:**
- Files: `Assets/Scripts/Chat/MediaCacheManager.cs`, `Assets/Scripts/Chat/MediaUrlIdentity.cs`
- Why fragile: `MediaCacheManager.TryAliasCachedImage()` (line 100) copies a file from one MD5-hashed key to another if two URLs name the same file. The caller must verify identity via `MediaUrlIdentity.SameFile()` before aliasing. If identity check is skipped, cache fills with junk.
- Safe modification: (1) Always call `MediaUrlIdentity.SameFile()` before `TryAliasCachedImage()`. (2) Log every alias operation: `Debug.Log($"[MediaCache] Aliased {fromUrl} -> {toUrl}")` for auditing. (3) Add assertions in tests that verify aliased URLs resolve to the same texture.
- Test coverage: SameFile logic has unit tests. TryAliasCachedImage is not explicitly tested; consider adding a test that aliases a file and verifies both URLs load the same image.

**OutboxStore Persistence & Atomic Writes:**
- Files: `Assets/Scripts/Chat/OutboxStore.cs`
- Why fragile: Outbox (unacked sends) are persisted via .tmp + File.Replace, which is atomic. BUT: the in-memory `_byChatId` cache can diverge from disk if an exception occurs during Save. If an exception is caught and the write is abandoned, the next Load sees old data. If the exception is not logged clearly, it silently fails.
- Safe modification: (1) Verify all Persist() calls are wrapped in try/catch with clear error logs. (2) After any exception during Persist, flag the in-memory cache as stale (e.g., remove from `_byChatId`) so the next LoadOrCache re-reads from disk. (3) Add integration test: create outbox entry, force an exception during save, restart, verify entry is recovered from disk or lost cleanly (not half-written).
- Test coverage: OutboxStore has unit tests for add/remove/update. Persistence edge cases (corruption, mid-write crash) are not tested.

## Scaling Limits

**Message History Cache (Per-Chat, Max 100 Messages):**
- Current capacity: 100 messages per chat in `ChatHistoryCache` (in-memory, not persisted to disk).
- Limit: If a user opens a chat, reads 100 messages, then scrolls back further, the cache is empty and another server fetch is needed. For users on slow networks, this is jarring.
- Scaling path: (1) Increase cap to 200-500 (memory cost vs UX tradeoff—test on low-memory devices). (2) Persist cache to disk (`chatHistory_{chatId}.json`) so reopening the chat re-loads cached messages instantly. (3) Implement a background sync that fetches older messages while the chat is open, extending the cache in the background.

**Wappi Concurrent API Limits:**
- Current capacity: No explicit rate limiting in the codebase. Wappi likely has throttles (per-profile or global).
- Limit: If many coroutines fire simultaneously (media downloads, quote fetches, reaction resolves), the cumulative request rate may hit Wappi's ceiling, causing 429 errors.
- Scaling path: (1) Add a `RequestQueue` utility that limits concurrent Wappi requests to N at a time (e.g., 5). (2) Audit all Wappi calls and route through the queue. (3) Log 429 responses and implement exponential backoff + retry.

**Bot Entity Count & PlayerPrefs Size:**
- Current capacity: PlayerPrefs on Android is limited (typically ~2 MB per app). With 20+ keys per bot, a large number of bots + products/services can exhaust this.
- Limit: Adding bots beyond ~100 (rough estimate depending on products/services per bot) will hit PlayerPrefs size limits.
- Scaling path: Migrate from PlayerPrefs to JSON-based repository (same fix as Tech Debt section). JSON files have no inherent size limit on modern devices.

## Dependencies at Risk

**Newtonsoft.Json (Newtonsoft.Json NuGet Package):**
- Risk: Third-party JSON parser; if newer versions introduce breaking changes or bugs, deserialization could fail silently (returning null or default values without warnings).
- Impact: Chat data, outbox data, and API responses rely on `JsonConvert.DeserializeObject<T>`. A parsing failure goes unnoticed if not logged.
- Current mitigation: Most calls check for null responses before using. Some calls (e.g., `ChatManager.Normalize`) assume successful parsing.
- Migration plan: (1) Ensure all `JsonConvert.Deserialize` calls are wrapped in try/catch with error logs. (2) Add JSON schema validation for critical responses (chats, messages). (3) Consider using Unity's built-in `JsonUtility` for simple DTOs (limited but safe). (4) Audit the NuGet version pinning in `Packages/nuget-packages/packages.config` to ensure no accidental upgrades.

**DOTween (Animation Library):**
- Risk: Third-party animation library; if it's abandoned or has performance regressions, all UI transitions will degrade.
- Impact: All page transitions, button presses, and list animations use DOTween. UI would become janky.
- Current mitigation: DOTween is actively maintained. Usage is straightforward (no complex animation chains).
- Migration plan: If needed, transition to Unity's built-in animation system (`Animator` + state machine) or Tween alternatives. Low risk for now.

**NativeGallery & NativeFilePicker (Platform Bridges):**
- Risk: Third-party plugins for iOS/Android media access; if they're abandoned or have OS compatibility issues, media selection could fail.
- Impact: Users cannot send photos/videos from the device gallery.
- Current mitigation: Both are in `Assets/Plugins/` and actively maintained. Fallback is missing (if plugin fails, there's no alternative picker).
- Migration plan: (1) Add error handling for picker failures (show a message instead of silent failure). (2) Monitor NativeGallery release notes for iOS/Android OS compatibility. (3) Test on latest iOS/Android versions before each release.

## Missing Critical Features

**Download Queue Serialization:**
- Problem: Media downloads are scattered across `MessageItemView`, `ChatItemView`, platform bridges, and emoji service. No central queue. Concurrent downloads risk the Wappi server bug (responses cross-deliver files).
- Blocks: Cannot reliably deliver media to the correct message; users see wrong images/videos in chats.
- Priority: **HIGH** — This is a known bug that affects user experience. Implement `MediaDownloadQueue` singleton that serializes all downloads.

**Request Rate Limiting & Retry Queue:**
- Problem: No throttling on Wappi requests. If the app fires many simultaneous API calls (pagination + backfill + quote fetches), it could exceed Wappi's rate limits. No exponential backoff for 429 responses.
- Blocks: Graceful handling of API rate limits; retries are manual or fail silently.
- Priority: **MEDIUM** — Only impacts high-frequency usage patterns (e.g., opening many chats rapidly). Can be deferred to a "polish" phase.

**Bot Data Migration Framework:**
- Problem: No schema versioning for bot data in PlayerPrefs. If a new version of the app changes what fields are persisted, old data is lost.
- Blocks: Cannot ship breaking bot schema changes without data loss.
- Priority: **MEDIUM** — Necessary before major bot feature additions. Can be deferred if bot schema is stable.

**End-to-End Encryption for Media Uploads:**
- Problem: Media is sent to Wappi over HTTPS but is not encrypted at rest on Wappi's servers (if CLAUDE.md doesn't specify, assume unencrypted).
- Blocks: User privacy; sensitive media is exposed to server compromise.
- Priority: **LOW** — Out of scope for this phase; requires Wappi API extension or a custom upload handler. Document as a future security enhancement.

## Test Coverage Gaps

**QuotedMessageCache Persistence:**
- What's not tested: Save/load edge cases (corrupt JSON, missing file, permission errors). Eviction policy when cache exceeds Capacity. TTL expiration logic (depends on passed-in time).
- Files: `Assets/Scripts/Chat/QuotedMessageCache.cs`
- Risk: A corrupted cache file could cause crashes or silent message loss. TTL edge case (time jumps backward) could cause cache inconsistency.
- Priority: **MEDIUM** — Add tests for: (1) save/load with missing directory, (2) load corrupted JSON, (3) eviction when entries exceed Capacity, (4) TTL boundary (resolvedAt + TtlSeconds == now).

**ChatManager.BotState Bot Switching:**
- What's not tested: Bot switching while a chat is open. Media/quote caches should be cleared or swapped. Outbox should be scoped to the new bot. Messages from the old bot should not leak into the new bot's view.
- Files: `Assets/Scripts/Main/ChatManager.BotState.cs`, `Assets/Scripts/Chat/ChatHistoryCache.cs`, `Assets/Scripts/Chat/MediaCacheManager.cs`, `Assets/Scripts/Chat/OutboxStore.cs`
- Risk: Silent data corruption or data leakage between bots.
- Priority: **HIGH** — This is a critical user flow (switching between bots). Add integration tests that simulate: (1) open bot A chat, (2) switch to bot B, (3) open a chat in bot B, (4) verify message list shows bot B's messages only.

**OutboxStore Atomic Writes Edge Cases:**
- What's not tested: App crash during Persist (can happen if app is force-killed mid-save). File.Replace atomicity on all OS/filesystem combinations. Corruption recovery.
- Files: `Assets/Scripts/Chat/OutboxStore.cs`
- Risk: Outbox entries are orphaned or duplicated after a crash, leading to retransmissions or lost messages.
- Priority: **MEDIUM** — Add test that: (1) creates outbox entry, (2) simulates a write-mid-flight (e.g., manually delete .tmp file during save), (3) restart app, (4) verify outbox is in a valid state (no duplicates, no orphans).

**ChatManager Media URL Caching & Expiry:**
- What's not tested: Media URLs expire on Wappi after a timeout. If an old URL is in cache, downloads fail. Cache invalidation strategy when URLs change.
- Files: `Assets/Scripts/Chat/MediaCacheManager.cs`, `Assets/Scripts/UI/MessageItemView.cs`
- Risk: Users see broken/expired media that was previously working.
- Priority: **LOW** — Only impacts long-running chats (hours+). Document the expiry window in CLAUDE.md and add a manual "refresh cache" flow if needed.

**RawMessage → NormalizedMessage Pipeline:**
- What's not tested: All message type conversions (image, video, audio, sticker, document, unknown). Message field mutations (text truncation, emoji conversion, link extraction). Null/empty field handling.
- Files: `Assets/Scripts/Chat/RawMessage.cs`, `Assets/Scripts/Chat/NormalizedMessage.cs`, `Assets/Scripts/Main/ChatManager.cs` (Normalize method)
- Risk: Message data is silently dropped or corrupted during normalization. Edge cases in emoji or link handling cause crashes.
- Priority: **MEDIUM** — Add parameterized tests for each message type + edge cases (null body, very long text, malformed media URL, etc.).

---

*Concerns audit: 2026-06-23*
