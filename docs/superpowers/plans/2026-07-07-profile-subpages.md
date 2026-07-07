# Profile Sub-Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline execution chosen — Unity Editor is open, builder/tests must run through this session's MCP/bridge). Steps use checkbox syntax.
> Deviation note: the editor builder (~900 lines) is specified here as an exact construction table + helper contract rather than inline code — its correctness gate is the Editor console + Game-view/device check, and duplicating it in the plan risks divergence.

**Goal:** Replace the five Profile-tab stubs with real pages (Аккаунт, Уведомления, Конфиденциальность, Поддержка + sheet, О приложении + licenses) per the approved spec.

**Architecture:** Six full-screen panels built by an idempotent editor builder inside `Screen_Profile`, driven by a `ProfileSubPages` controller (partials per page). Pure logic (formatting, wipe plan, notify policy, message composer, prefs seams) lives in small static classes with EditMode tests. Server/scene teardown reuses `Bot.DeleteBot()`; history/media clearing gets a new `ChatManager` partial; support form reuses hardened `SendToTelegram`.

**Tech stack:** Unity 6 uGUI + TMP + DOTween, Nobi RoundedCorners, NUnit EditMode, resvg-js for icon PNGs, python for a WAV.

## Global constraints (from spec + recon)

- All sizes in 1080×1920 canvas reference units, authored directly (no ×2.5 scale). Tokens: bg `#F0F2F5`, card `#FFFFFF` r=40, row h=150 (HLG pad L44 R54 T27 B27, spacing 40), icon squircle 100×100 r=28, label TMP 42 Medium `#1A1A2E`, secondary `#65676B`, divider 2u `#E4E6EB`, chevron 32×32 `#65676B`, section caption 30 Semibold uppercase `#65676B`, danger `#FFCED5`/`#E53935` h=144 r=40, primary `#1B7CEB` white h=144 r=40, toggle ON `#25D366`.
- Fonts by GUID: Regular `e0cdfe2d…`, Medium `d091b0ca…`, Semibold `a2b0b38b…`, Bold `1cd71582…` (default font's weight table is empty — always assign the specific SDF asset).
- Icons are `Image`+sprite ONLY (TMP glyphs don't render). RoundedCorners via direct `Nobi.UiRoundedCorners` reference + `Validate()`/`Refresh()` after `Canvas.ForceUpdateCanvases()`.
- Headers: h=300 white + 2u hairline (safe area baked; never `Screen.safeArea`); title TMP 48 Semibold bottom row (y=60, h=60); back chevron `Assets/Images/Chat/chevron-left.png` tinted `#1B7CEB` 60×60 centered at (70, 90).
- Tests: no namespace, `<Type>Tests` class, `Scenario_Expected` names, classic asserts; PlayerPrefs isolation via injectable static seams (SemiAutoStore pattern) or `TESTBOT_` keys.
- Editor is OPEN → recompile/builder via mcp-unity; tests via mcp-unity `run_tests` with EXACT class-name filter (fallback: ClaudeTestBridge trigger, Editor focused). Scene MUST be saved after the builder runs.
- Russian UI copy exactly as in the approved mockups.

---

### Task 1: Pure logic + seams + tests

**Files:**
- Create: `Assets/Scripts/Chat/CacheSizeFormatter.cs` — `public static class CacheSizeFormatter { public static string FormatBytes(long bytes) }` → `"0 МБ"` for <0/0, `"512 КБ"`, `"128 МБ"`, `"1,3 ГБ"` (ru comma, one decimal for ГБ only, МБ/КБ rounded).
- Create: `Assets/Scripts/Main/NotifPrefs.cs` — static; keys `NotifSoundEnabled`/`NotifVibrationEnabled`/`NotifUnreadBadgeEnabled`, default 1; injectable seams `public static Func<string,int,int> GetInt = PlayerPrefs.GetInt;` `public static Action<string,int> SetIntAndSave = (k,v) => { PlayerPrefs.SetInt(k,v); PlayerPrefs.Save(); };` properties `SoundEnabled`/`VibrationEnabled`/`UnreadBadgeEnabled` (get/set bool).
- Create: `Assets/Scripts/Chat/IncomingNotifyPolicy.cs` — pure: `public static bool ShouldNotify(bool isInitialLoad, bool lastIdChanged, bool lastMessageIsMine, int unreadCount, string chatId, string openChatId, bool chatPanelVisible)` → true only when `!isInitialLoad && lastIdChanged && !lastMessageIsMine && unreadCount > 0 && !(chatPanelVisible && chatId == openChatId)`.
- Create: `Assets/Scripts/Main/SupportMessageComposer.cs` — pure: `public static string Compose(string message, string contact, string version, string platform, string deviceModel)` → message + optional `\nКонтакт: {contact}` + `\n— Automation v{version} · {platform} · {deviceModel}`; null/whitespace contact omitted; trims.
- Create: `Assets/Scripts/Main/LocalDataWipe.cs` — pure plan + runner:
  - `public static List<string> DiskWipeTargets(string persistentDataPath)` → `BotCache` dir, `response.txt`, `link_metadata.json`, `all_chats_cache.json` (legacy), + `public static bool IsStickerFile(string fileName)` (`sticker_*.webp` glob match).
  - `public static void DeleteDiskData(string persistentDataPath)` (Directory/File deletes, try/catch per target, sticker glob sweep).
- Test: `Assets/Tests/Editor/Chat/CacheSizeFormatterTests.cs`, `NotifPrefsTests.cs` (seam-swapped, defaults ON, round-trip), `IncomingNotifyPolicyTests.cs` (each guard flips the verdict; open-chat suppression), `SupportMessageComposerTests.cs` (contact omitted/included, meta suffix exact), `LocalDataWipeTests.cs` (targets list content; sticker glob true/false cases).

**Interfaces produced:** exactly the signatures above (consumed by Tasks 2–4).

- [ ] Write the five logic files and five test files (test code written first per file, then impl)
- [ ] Recompile via mcp-unity; expect clean console
- [ ] Run the five test classes via mcp-unity `run_tests` exact filters; expect all green

### Task 2: Resolver-cache Clear APIs + ChatManager privacy clear + notify hook + badge gate

**Files:**
- Modify: `Assets/Scripts/Chat/QuotedMessageCache.cs`, `Assets/Scripts/Chat/ReactionTargetCache.cs` — add `public static void Clear(string baseDir)` → `_mem.Remove(baseDir);` + delete `PathFor(baseDir)` file (try/catch IO).
- Create: `Assets/Scripts/Main/ChatManager.PrivacyClear.cs` (partial) —
  - `public long ComputeLocalCacheBytes()`-style coroutine `public IEnumerator ComputeMediaCacheSize(Action<long> done)` — time-sliced (yield every 64 files) over every `BotCache/*/media` dir.
  - `public void ClearAllMediaCaches()` — `ClearVideoThumbQueue()`; `MediaCacheManager.Instance.ClearCache()` for the active bot; `Directory.Delete(BotCache/{bot}/media, true)` for every other bot dir.
  - `public void ClearAllLocalHistory()` — recon §6b sequence: stop `_syncWaitRoutine` + `StopAllCoroutines()`, reset `_chatFetchesInFlight`/`_chatListSyncing`, `ClearVideoThumbQueue()`, `ClearMediaDownloadQueue()`, per-bot-dir delete of `messages/`, `chats.json`, `quoted_messages.json`, `reaction_targets.json`, `outbox_*.json` (+ `response.txt` at root), `QuotedMessageCache.Clear`/`ReactionTargetCache.Clear` per baseDir, in-memory: `Chats.Clear(); chatLookup.Clear(); seenMessageIds.Clear(); _reactions.Clear(); _activeChatCache = null; _cachedQueue = null; _pendingFirstBatch = null; _pendingLiveSyncMessages = null; _outbox = null;`, then `OnChatListCleared?.Invoke(); BeginLoadForActiveBot();`
- Modify: `Assets/Scripts/Main/ChatManager.cs` `ParseChatsJson` — in the smart-merge branch (~line 300) and brand-new-chat branch, call `NotificationFx.OnIncomingDetected(...)` guarded by `IncomingNotifyPolicy.ShouldNotify(isInitialLoad, lastIdChanged, mergedIsMine, chat.unread_count, chat.id, CurrentChatId, MessageListPanel != null && MessageListPanel.activeSelf)`.
- Create: `Assets/Scripts/Chat/NotificationFx.cs` — MonoBehaviour (added by builder to ChatManager's GO): `[SerializeField] private AudioClip incomingClip;` lazy `AudioSource`; `public static NotificationFx Instance;` `public static void OnIncomingDetected()` → if `NotifPrefs.SoundEnabled` PlayOneShot; if `NotifPrefs.VibrationEnabled` `Handheld.Vibrate()` (`#if UNITY_ANDROID || UNITY_IOS`, skip editor).
- Modify: `Assets/Scripts/UI/ChatItemView.cs` `ApplyUnreadBadge` — first line `if (!NotifPrefs.UnreadBadgeEnabled) count = 0;`
- Test: `Assets/Tests/Editor/Chat/ResolverCacheClearTests.cs` — Clear removes the file and the in-memory entry (uses temp dir baseDir; both caches).

- [ ] Write files/edits + test; recompile clean; ResolverCacheClearTests green

### Task 3: SendToTelegram hardening + secrets

**Files:**
- Modify: `Assets/Scripts/Main/Secrets.cs` — add `public string supportChatId;` to `SecretsData`.
- Modify: `Assets/Scripts/Main/Manager.cs` — add `public static string supportChatId => Secrets.Data.supportChatId;` next to `telegramBotToken` (line ~154); replace `SendToTelegram` with the recon §6 hardened version (public, `Action<bool>` callback, guards empty message/token/chatId, `timeout = 30`, chat_id from `supportChatId`).
- Modify: `Assets/StreamingAssets/secrets.json.example` — add `"supportChatId": "YOUR_TELEGRAM_CHAT_ID",`.
- Modify: `Assets/StreamingAssets/secrets.json` (local, gitignored) — add `"supportChatId": "1038376805"` (migrating the previously hardcoded value). NOTE: `telegramBotToken` is absent from this local file — the send fails gracefully until the owner adds it; flag in the final report.

- [ ] Apply edits; recompile clean (no test — network coroutine; composer covered in Task 1)

### Task 4: ProfileSubPages controller + SwipeToBackPanel + ProfilePage rewiring

**Files:**
- Create: `Assets/Scripts/Main/ProfileSubPages.cs` — singleton `Instance`; `public enum Page { Account, Notifications, Privacy, Support, About, Licenses }`; serialized: 6 panel RectTransforms, per-page refs (stamped by builder); `public void Open(Page p)` — SetActive + `DOAnchorPosX(0, 0.3f).SetEase(Ease.OutCubic)` from x=canvasWidth; `public void Back()` — `DOAnchorPosX(width, 0.25f).SetEase(Ease.InCubic)` → SetActive(false); wires back buttons + swipe strips in `Start`; shared confirm popup helper `Confirm(title, message, confirmLabel, Action onConfirm)` via PopupUI; transient toast helper `ShowToast(RectTransform panel, string text)` (lazy TMP label, DOTween fade per AttachmentPreviewScreen.ShowSizeError pattern).
- Create: `Assets/Scripts/Main/ProfileSubPages.Account.cs` — binds name/email from PlayerPrefs on open (reuses ProfilePage keys/defaults); pencil → `ProfilePage.Instance.OpenEditPopupPublic()`; wipe button → `Confirm("Удалить все данные?", "Удалит всех ботов, историю и настройки на этом устройстве. Действие нельзя отменить.", "Удалить", RunWipe)`; `RunWipe()`: iterate `Manager.Instance` BotsParent children **backwards** → `GetComponent<Bot>()?.DeleteBot()`; `PlayerPrefs.DeleteAll(); PlayerPrefs.Save();` `LocalDataWipe.DeleteDiskData(Application.persistentDataPath);` close panel; `ProfilePage.Instance.NavigateToWhatsAppTab();`
- Create: `Assets/Scripts/Main/ProfileSubPages.Notifications.cs` — three ToggleRow refs; init from NotifPrefs quiet (`SetIsOnQuiet`); onValueChanged → NotifPrefs setters; badge toggle additionally iterates `ChatManager.Instance.Chats` → `vm.NotifyUpdated()`.
- Create: `Assets/Scripts/Main/ProfileSubPages.Privacy.cs` — on open: size label «…» then `StartCoroutine(ChatManager.Instance.ComputeMediaCacheSize(b => sizeLabel.text = CacheSizeFormatter.FormatBytes(b)))`; media row (disabled state when 0 bytes: label+value `#C7C7CC`, non-interactable) → Confirm → `ClearAllMediaCaches()` → toast `Освобождено {formatted}` → recompute; history row → Confirm → `ClearAllLocalHistory()` → toast «История чатов очищена».
- Create: `Assets/Scripts/Main/ProfileSubPages.Support.cs` — FAQ item views (question Button + answer container; expand/collapse: chevron `DORotate`, answer `LayoutElement.preferredHeight` tween 0↔preferred, one open at a time, first open by default); CTA opens sheet (backdrop CanvasGroup fade 0→1 0.22 OutQuad + sheet `DOAnchorPosY` bottom-slide 0.25 OutCubic; backdrop tap + grabber-area drag skipped — backdrop tap closes); send button interactable only when message non-empty && !sending; on send → `StartCoroutine(Manager.Instance.SendToTelegram(SupportMessageComposer.Compose(msg, contact, Application.version, Application.platform.ToString(), SystemInfo.deviceModel), ok => …))` → success: clear fields, close sheet, toast «Отправлено! Мы свяжемся с вами»; failure: keep sheet, toast «Не удалось отправить — проверьте интернет». FAQ content = `private static readonly (string q, string a)[] Faq` with the five approved RU pairs.
- Create: `Assets/Scripts/Main/ProfileSubPages.About.cs` — on open set `versionLabel.text = $"Версия {Application.version}"`; licenses row → `Open(Page.Licenses)`; licenses text = static string listing DOTween, NativeFilePicker, NativeGallery, NativeShare, NativeCamera, RoundedCorners (Nobi), unity.webp, Newtonsoft.Json, NuGetForUnity, Twemoji (CC-BY 4.0).
- Create: `Assets/Scripts/Main/SwipeToBackPanel.cs` — generic right-swipe-to-dismiss modeled on SwipeToBackBotSettings mechanics (IInitializePotentialDrag/IBeginDrag/IDrag/IEndDrag on a left-edge strip; horizontal-intent detection; `[SerializeField] RectTransform panelToSlide;` `public Action OnCommitted;` commit threshold 0.4 width or flick >1000; snap Lerp speed 10, min 1500).
- Modify: `Assets/Scripts/Main/ProfilePage.cs` — five stubs → `ProfileSubPages.Instance?.Open(ProfileSubPages.Page.X)`; delete logout fields/wiring/methods (keep `bottomTabManager`); add `public void NavigateToWhatsAppTab() => bottomTabManager?.SwitchTab(BottomTabManager.WhatsAppTabIndex);` and `public void OpenEditPopupPublic() => OpenEditPopup();`; ConfirmLogout logic deleted (superseded by ProfileSubPages.Account.RunWipe).

- [ ] Write all files/edits; recompile clean (UI verification comes after Task 6 builds the panels)

### Task 5: Assets — icons + sound

**Files:**
- Create: `Tools/profile_icons/*.svg` (white glyphs, 24×24 viewBox from the approved mockups): `speaker, vibrate, unread, smartphone, cloud, media, bubble, trash, doc, send, robot`.
- Create: `Tools/render_profile_icons.js` — resvg-js loop → `Assets/Images/New/PS_<Name>.png` at 256px width, transparent bg.
- Create: matching `.meta` files from the Account.png.meta template (textureType 8, single sprite, PPU 100, no mips, bilinear, clamp, alphaIsTransparency, fresh uuid4 GUIDs).
- Create: `Tools/make_notification_sound.py` → `Assets/Audio/notification_pop.wav` (44.1k mono 16-bit, ~0.18s two-tone E6→A6 soft pop, −6dB peak); let Unity generate the .meta on refresh.

- [ ] `cd Tools && npm install @resvg/resvg-js && node render_profile_icons.js`; run python script; verify PNG dims + WAV plays (afplay)

### Task 6: ProfileSubPagesBuilder + build + wire

**Files:**
- Create: `Assets/Editor/ProfileSubPagesBuilder.cs` — menu `Tools/Profile Sub-Pages/Build` + `BuildHeadless()`. Sequence:
  1. Locate `Screen_Profile` via `FindFirstObjectByType<ProfilePage>(FindObjectsInactive.Include)`.
  2. Idempotent: destroy existing `ProfileSubPages` component root + any panel named `SubPages`; destroy `LogoutButton` + `LogoutPopup` GameObjects under Screen_Profile (grep scene consumers first — only ProfilePage referenced them, whose fields are now deleted).
  3. Build `SubPages` root (StretchFill, inactive) with 6 panels; each panel: full-screen RectTransform (anchored stretch, pivot 0.5), bg Image `#F0F2F5`, header per Global Constraints (title per page), ScrollRect content column (gutter 44, spacing 32, top pad 50, bottom pad 96), left-edge swipe strip (150 wide, alpha-0 raycast Image + `SwipeToBackPanel` + `ClickPassthrough.deliverPressToAllBehind = true`), inactive by default.
  4. Page content per the approved mockups (construction table in spec §5.1–5.6): Account (profile card 264h + Данные info card + danger button 144h + fine print), Notifications (3 ToggleRow cards в one grouped card + footnote), Privacy (2 info rows card + 2 action rows card + footnote), Support (FAQ card of 5 accordion items + bottom-pinned CTA 144h + sheet: bottom-anchored panel, top-corners 60 via `ImageWithIndependentRoundedCorners`, grabber 108×12 `#C7C7CC`, title 48 Bold, message ScrollableTextArea-style TMP_InputField 280h bg `#F0F2F5` r=28, contact input 120h, caption 30, primary send 144h, `KeyboardAwarePanel` if its serialized contract allows — else document skip), About (hero: 252 icon square r=60 `#1B7CEB` + PS_Robot child, name 56 Bold, version 36, value-prop card, Документы card, footer captions), Licenses (title + one card with TMP 36 static text).
  5. Add `ProfileSubPages` to SubPages root; SerializedObject-stamp every ref (panels, toggles, labels, buttons, sheet parts, confirm popup, FAQ items).
  6. Build shared ConfirmPopup (PopupUI pattern: scrim + card 900w r=48, title 48 Bold, message 40 `#636366`, Отмена `#E4E6EB` / confirm `#EB4545` r=28 buttons) under SubPages root.
  7. Add `NotificationFx` to ChatManager's GameObject, stamp `incomingClip` = `Assets/Audio/notification_pop.wav`.
  8. `Canvas.ForceUpdateCanvases()` → Validate/Refresh all rounded corners; `EditorSceneManager.MarkSceneDirty` + interactive save (headless: `SaveScene`).

- [ ] Write builder; recompile via MCP; check console clean
- [ ] Run menu item via MCP (re-run after server-restart log per memory); read console for `[ProfileSubPagesBuilder]` success line; save scene via MCP
- [ ] Spot-check hierarchy via MCP `get_gameobject` (panels exist, refs non-null)

### Task 7: Full verification

- [ ] Run ALL new test classes via MCP exact filters (CacheSizeFormatterTests, NotifPrefsTests, IncomingNotifyPolicyTests, SupportMessageComposerTests, LocalDataWipeTests, ResolverCacheClearTests) — green
- [ ] Run the full suite via bridge trigger if Editor focus available (guard against regressions in ChatManager/ChatItemView edits); else run key existing classes via MCP (ReactionTargetCacheTests, QuotedFieldsCacheTests, SemiAutoStoreTests, UploadedFilesStoreTests)
- [ ] Honest report: what's device-only (vibration, keyboard behavior, real Telegram send — token missing locally), Game-view check request at 1080×2400, commit consent question
