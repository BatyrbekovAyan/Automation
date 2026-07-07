# Profile Sub-Pages — Design Spec

**Date:** 2026-07-07
**Status:** Design approved via mockups (artifact `https://claude.ai/code/artifact/af766d8c-1ba0-4e44-81c7-1ce8c995b56f`, v2 `approved-q1-q3`). Spec awaiting owner review before implementation planning.

## 1. Goal

Replace the five `Debug.Log` stubs on the Profile tab (`ProfilePage.cs:272-276`) with real pages before release: **Аккаунт, Уведомления, Конфиденциальность, Поддержка, О приложении**. Every page shows only functionality that genuinely works today — no faked features.

## 2. Approved decisions

| # | Decision |
|---|----------|
| Q1 | Product name stays **«Automation»** for now. It appears on the About page as a single UI string constant — cheap to rename before release. |
| Q2 | **No push-notification row at all** (not even a «Скоро» placeholder). The Notifications page shows only working in-app toggles. |
| Q3 | Support channel = **Telegram** via the existing dormant `Manager.SendToTelegram()`. No public email row. |

## 3. Non-goals

- No push infrastructure (FCM/APNs), no user accounts/auth, no cloud sync.
- No hosted privacy-policy/terms links, no website row, no "rate the app" row — added later as one-line rows when the URLs exist.
- No FAQ search, no ticketing system.

## 4. Architecture

- **Navigation:** each page is a full-screen panel **inside `Screen_Profile`** (panels live inside their screen, not at canvas root). Opening slides the panel in from the right (`DOAnchorPos`, 0.3s OutCubic); back slides out (0.25s InCubic). Back = header chevron + swipe-right-to-back (reuse the existing swipe-back component pattern). `ProfilePage`'s five row buttons are rewired from stubs to `ProfileSubPages.Open(page)`.
- **Builder:** one idempotent delete-and-rebuild `[MenuItem]` editor script `Assets/Editor/ProfileSubPagesBuilder.cs` (pattern: `BotSettingsRebuilder`). Edit-Mode only; save the scene after building; rewire all serialized consumers via `SerializedObject`.
- **Runtime controller:** `Assets/Scripts/Main/ProfileSubPages.cs` (partial classes per page if it grows: `ProfileSubPages.Support.cs` etc.). `[SerializeField]` refs stamped by the builder. Confirmations via existing `PopupUI` (scrim 0.5, card r=48, buttons r=36).
- **Header per page:** match `Screen_Profile/Header` construction — h=300 (safe area baked in, never `Screen.safeArea`), white bg, 2u #E4E6EB bottom hairline, title TMP 48 Semibold #1A1A2E bottom-centered (y=60, h=60); add back button: `chevron-left.png` tinted #1B7CEB, 60×60, left x=40, vertically aligned with the title row.

### Visual tokens (all in 1080×1920 canvas reference units)

Page bg #F0F2F5 · cards #FFFFFF r=40 (Rect.png + RoundedCorners) · content gutter 44, card gap 32 · row h=150, HLG pad L44 R54 T27 B27 spacing 40 · icon squircle 100×100 r=28, white sprite icon inside · row label TMP 42 Medium #1A1A2E · secondary/captions #65676B · dividers 2u #E4E6EB full-width · chevron-right.png 32×32 #65676B · section caption ~30 semibold uppercase #65676B (new pattern, mirrors mockup) · toggles: existing ToggleRow sprites, ON #25D366, OFF #E0E0E0 · danger button h=144 r=40 bg #FFCED5 label 44 Semibold #E53935 · primary button r=40 bg #1B7CEB white label · icons reuse `Assets/Images/New/` (Account/Bell/Security/Help/Info) + `Assets/Images/Chat/chevron-*.png`.

## 5. Pages

### 5.1 Аккаунт

- **Profile card** (same layout as main-screen ProfileCard): avatar circle #1B7CEB with initial, name (PlayerPrefs `ProfileName`), email (`ProfileEmail`), pencil button (#E8F2FD circle) → opens the **existing** edit popup on ProfilePage. No second editor.
- **«Данные» card:** one non-tappable info row (no chevron), smartphone icon on #607D8B: «Хранятся на этом устройстве» + subtitle «Боты, чаты и настройки не привязаны к облачному аккаунту — они живут только в этом приложении.»
- **«Опасная зона»:** standalone danger button «Удалить все данные» + centered caption «Удалит всех ботов, историю и настройки на этом устройстве. Действие нельзя отменить.» Tap → PopupUI confirm → wipe → navigate back to tab 0.
- **The main Profile screen's «Выйти» button is removed**; this page's honestly-named action replaces it. (Flagged for owner: this changes the main Profile screen — the mockups showed only the sub-page.)
- **Wipe completeness fix (in scope):** the current `ConfirmLogout()` suffix list misses per-item keys (`Products{n}`, `Services{n}` entries) and `UploadedFilesStore` records. The new wipe must enumerate ALL per-bot keys — consult `.claude/skills/bot-persistence/SKILL.md` for the full key inventory during planning.

### 5.2 Уведомления

- One card «В приложении» with three icon+toggle rows (defaults ON):
  | Row | Icon bg | PlayerPrefs key | Wired to |
  |---|---|---|---|
  | Звук новых сообщений | #FF9800 | `NotifSoundEnabled` | short AudioClip on incoming live message (ChatManager live path) |
  | Вибрация | #9C27B0 | `NotifVibrationEnabled` | `Handheld.Vibrate()` / platform bridge on incoming live message |
  | Счётчик непрочитанных | #1B7CEB | `NotifUnreadBadgeEnabled` | unread badge rendering in chat list (ChatItemView) |
- Footnote under card: «Действуют, пока приложение открыто.»
- Per Q2: nothing else on the page.
- Sound/vibration fire on incoming live messages while the app is open, suppressed when that chat is currently open on screen.

### 5.3 Конфиденциальность

- **«Ваши данные» card** — two non-tappable disclosure rows:
  - #1B7CEB smartphone: «На этом устройстве» / «Боты и их настройки, история чатов, кэш фото и видео.»
  - #607D8B cloud: «Обрабатываются на серверах» / «Сообщения клиентов — чтобы ИИ мог на них ответить; прайс-листы — в защищённом хранилище.»
- **«Управление» card** — two action rows:
  - «Очистить кэш медиа» + live size value (e.g. «128 МБ») + chevron. Size computed in a coroutine (never main-thread block) from the MediaCacheManager disk cache directory; formatted КБ/МБ/ГБ. Tap → PopupUI confirm → delete cache files → toast «Освобождено N МБ» → size refreshes to 0.
  - «Очистить историю чатов» + chevron. Tap → PopupUI confirm → clear ChatHistoryCache (all chats) + delete `all_chats_cache.json`; list re-syncs from server on next open.
- Footnote: «Переписка в WhatsApp и Telegram не удаляется — очищаются только локальные копии в приложении.»
- No policy-URL row until a hosted page exists.

### 5.4 Поддержка

- **«Частые вопросы» card** — accordion, first item expanded by default. Q&A content = plain string pairs in one editable place in the controller:
  1. «Почему бот не отвечает?» → check the green activation switch on the bot card; paused bots don't reply until re-enabled.
  2. «Как подключить WhatsApp?» → scan QR during bot creation, or enter the pairing code.
  3. «Не приходит код подтверждения» → WhatsApp allows a repeat code at most every 2 minutes; wait for the timer and re-request.
  4. «Что такое режим „Вместе"?» → bot proposes replies, you send them.
  5. «Как загрузить прайс-лист?» → BotSettings → Промпты → Прайс-листы; Excel/Word/PDF/photo supported.
  (Final RU copy as in the mockups; edit freely at implementation.)
- Expand/collapse: chevron rotates, answer height animates (DOTween).
- **CTA** «Написать в поддержку» — primary blue full-width button pinned in the thumb zone.

### 5.5 Поддержка — sheet «Написать в поддержку»

- Bottom sheet (AttachSheet pattern: white, top corners 60u, grabber #C7C7CC, scrim 0.5, KeyboardAwarePanel behavior):
  - multiline input, placeholder «Опишите проблему или вопрос…»
  - single-line input, placeholder «Телефон или @telegram для ответа»
  - caption: «К сообщению добавятся версия приложения и модель устройства — так мы быстрее разберёмся.»
  - button «Отправить» — disabled while the message field is empty and while sending.
- **Send path:** `Manager.SendToTelegram`, hardened: (a) `chat_id` moves from the hardcoded literal in Manager.cs to `secrets.json` (new key, e.g. `telegramSupportChatId`); (b) message composed as `text + "\nКонтакт: {contact}" + "\n— Automation v{Application.version} · {platform} {device model}"`.
- Success → close sheet + toast «Отправлено! Мы свяжемся с вами». Failure → sheet stays open, error toast «Не удалось отправить — проверьте интернет».

### 5.6 О приложении

- Centered hero: app icon (rounded square, #1B7CEB gradient, white robot glyph), name **«Automation»** (single UI constant), «Версия {Application.version}» (mockup shows «1.0 (1)»; build-number suffix only if trivially available cross-platform, else version only).
- Value-prop card: «ИИ-ассистент для вашего WhatsApp и Telegram. Отвечает клиентам 24/7 — вы видите каждый диалог и можете вмешаться в любой момент.»
- «Документы» card: one row «Лицензии открытого ПО» → opens a sixth, trivial sub-panel (same header + one white card with scrollable static text) listing: DOTween, NativeFilePicker, NativeGallery, NativeShare, NativeCamera, RoundedCorners (Nobi), unity.webp, Newtonsoft.Json, NuGetForUnity, Twemoji graphics (CC-BY 4.0).
- Footer captions: «© 2026 SynergySoft» / «Сделано для бизнеса в Казахстане и СНГ» (KZ line removable on request).

## 6. Error/empty states

- Cache size while computing: value shows «…» then the number.
- Empty media cache: value «0 МБ», row disabled (label and value tinted #C7C7CC, no confirm popup).
- Support send failure: non-destructive (text preserved in the sheet).

## 7. Testing (EditMode, `Assets/Tests/Editor/Chat/` conventions)

- Notification prefs: defaults ON; toggle write/read round-trip.
- Wipe completeness: seed fake bot prefs incl. `Products{n}`/`Services{n}` items + UploadedFilesStore entries → wipe → assert no `Bot*` keys remain.
- Cache-size formatter: bytes → КБ/МБ/ГБ strings.
- Support message composer: appends version/platform/device, includes contact only when non-empty.
- UI verification: Game view 1080×2400 against the mockups + existing Profile screen; device pass for vibration/sound.

## 8. Build order (for the implementation plan)

1. Builder skeleton: 6 panels (5 pages + licenses), header pattern, slide navigation, ProfilePage row rewiring, main-screen «Выйти» removal.
2. Account (incl. wipe fix) → 3. Notifications → 4. Privacy → 5. Support + sheet + SendToTelegram hardening → 6. About + licenses.
Each step: builder section + controller logic + tests where logic exists; scene saved after every builder run.
