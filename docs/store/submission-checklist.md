# Чеклист подачи в App Store / Google Play

Статус на 2026-09-06. Порядок: сначала «Блокеры», дальше можно параллельно.
Подача ≠ релиз: релизим вручную (manual release) только после Блока 2 (правило запуска Блока 1 §7).

## 0. Блокеры (без них подача невозможна или бессмысленна)

- [x] **Финальное имя приложения: «Choose Reply»** (решение владельца 2026-08-27).
  Вписано: `ProjectSettings.productName`, `ProfileSubPages.ProductName` + сцена,
  юр-документы, review notes. Осталось: использовать «Choose Reply» в store listing.
- [x] **Юр-страницы опубликованы на choosereply.com** (2026-08-28). Домен живёт на
  собственном VPS (Caddy, авто-HTTPS): `https://choosereply.com/privacy.html` и
  `/terms.html` отдают 200 — ИМЕННО по этим путям, они вшиты в приложение
  (`LegalLinks`) и в review notes. Реквизиты оператора заполнены в обоих файлах:
  ТОО «Synergy Expert Group», БИН 180940030158, руководитель Батырбеков Аян,
  г. Астана, проспект Бауыржана Момышулы, 7-87. Источник — `docs/legal/*.html`;
  копии для деплоя лежат в `Tools/landing/` и должны быть байт-в-байт теми же
  (после правки юр-текста копировать заново и перезаливать scp на VPS).
  Осталось: проверить открытие ссылок с телефона.
- [x] **URL вписаны в приложение** (`LegalLinks.TermsUrl`/`PrivacyUrl` =
  `https://choosereply.com/{terms,privacy}.html`), билдер перезапущен, ряд ссылок
  на пейволле активен. Осталось: глазами убедиться на устройстве/в Editor, что обе
  ссылки открываются (страницы должны быть уже залиты).
- [x] **Иконка приложения** — финал (три полосы, «ink») стоит в `Assets/Images/Icon.png`
  (1024×1024, c3f2e06). Per-platform слоты iOS пустые ОСОЗНАННО: Unity генерит все
  размеры из дефолтной иконки, включая 1024 для ASC.
- [x] **Скриншоты ГОТОВЫ (2026-09-03)** — витринные кадры с рамкой телефона и подписями,
  в двух размерах: `Tools/store/listing/ios-6.9/` (1320×2868 — то, что ASC просит для
  iPhone) и `ios-6.5/` (1284×2778, fallback). Пайплайн из двух команд, повторять после
  ЛЮБОЙ правки UI: `Tools/Store/Capture Screenshots` в Unity (сырые кадры) →
  `python3 Tools/store/compose-listing.py` (рамка + подписи + `preview-sheet.png`).
  Порядок загрузки в ASC (первые три видны в результатах поиска — в них главная фишка,
  результат и знакомый экран):

  | № | Файл | Подпись | Экран |
  |---|------|---------|-------|
  | 1 | `01-vmeste.png` | ИИ предлагает — вы выбираете · Готовые ответы под каждое сообщение клиента | панель «Вместе» |
  | 2 | `02-auto.png` | Отвечает клиентам за вас · Цены, наличие, заявка — по вашему прайсу, 24/7 | тред с автоответом |
  | 3 | `03-chats.png` | Все чаты на одном экране · WhatsApp и Telegram на вашем номере | список чатов |
  | 4 | `04-bots.png` | Бот под каждый бизнес · Включайте «Авто» одним тапом | Мои Боты |
  | 5 | `05-pricelist.png` | Загрузите прайс — бот знает цены · PDF, Excel, фото — разберёт сам | настройки → Продукты |
  | 6 | `06-dashboard.png` | Сводка за неделю · Сколько заявок собрал бот и где нужны вы | Сводка · 7 дней |
  | 7 | `07-plans.png` | Все функции в каждом тарифе · Платите только за масштаб — от 9 990 ₸ в месяц | пейволл (тарифы) |

  Цены на кадре 7 — fallback-текст `PlanCatalog` (в Editor нет StoreKit); они равны сетке,
  подтверждённой в ASC 2026-08-25 (9 990/19 990/39 900 ₸/мес, 99 000/198 990/399 990 ₸/год),
  а на устройстве пейволл показывает цены самого магазина через RevenueCat, так что
  разойтись они не могут. Сверено 2026-09-03. Не выкладывать: профиль, промпт.
  Для ревью подписок (скриншот покупки на каждый SKU) подходит сырой
  `Tools/store/screenshots/05-paywall.png` — он закрывает три месячных SKU; для годовых
  нужен тот же экран с переключателем «Год» (в драйвере такого кадра пока нет), для
  топ-апа — страница «Подписка» в профиле. Android phone — когда дойдёт Android-трек
  (композер отдаст 1080×2340 сменой `CANVAS`). iPad-скриншоты НЕ нужны: сборка
  iPhone-only (2026-09-01, `Tools/Store Compliance/Apply iOS Store Settings`).
  Демо-данные вымышленные, кроме номера клиента: `+7 702 699-88-44` — номер владельца
  (его решение 2026-09-03).
- [x] **Демо-видео записано и выложено (2026-09-05)** — режим A (iOS Simulator,
  итоги и ловушки в `demo-video-plan.md`), v4 одобрен владельцем. Непубличная ссылка
  `https://choosereply.com/review/923843a986255cba/ChooseReply-demo-2026-09.mp4`
  (отдаёт 200, проверено 2026-09-06) уже вписана в `app-review-notes.md`.
- [x] **Политика перезалита на VPS (владелец, 2026-09-06; live == repo проверено).** Было:
  живая страница отставала от репо (нет трёх пунктов §3 — идентификатор установки,
  статистика подсказок, обращения в поддержку), а App Privacy labels «User ID / Device
  ID» опираются именно на них. `scp Tools/landing/privacy.html choosereply:~/choosereply/site/privacy.html`,
  затем `curl -s https://choosereply.com/privacy.html | cmp - Tools/landing/privacy.html`.

## 1. App Store Connect (после активации Apple Developer, оплачен 2026-08-22)

- [x] App Record создан (владелец, 2026-09-03): bundle id `com.synergysoft.choosereply`,
  имя «Choose Reply», RU primary locale; сторфронты KZ + KG/UZ/TJ/AZ/AM/GE/MD; Paid Apps
  Agreement / банк / налоговая форма заполнены.
- [x] **Листинг (страница версии 1.0)** заполнен владельцем 2026-09-06 — текст для копирования блоками:
  `app-store-listing.md` (subtitle / promo / description с EULA-строкой и нейтральным
  дисклеймером / keywords / URL / copyright). Скриншоты 6.9″ — 7 файлов из
  `Tools/store/listing/ios-6.9/` в порядке таблицы выше.
- [ ] **Privacy Policy URL** (App Information, обязательное поле) —
  `https://choosereply.com/privacy.html`; там же License Agreement = стандартная EULA
  Apple (своя ссылка на Условия уже в описании), Content Rights = стороннего контента нет.
- [x] **App Privacy (labels)** — заполнены владельцем 2026-09-03 по матрице ниже.
  Матрица ПЕРЕПИСАНА 2026-09-01 под правду кода (аудит 2026-08-30:
  старые ответы «Identifiers: нет» / «not linked» опровергались трафиком — приложение
  шлёт RevenueCat appUserID/IDFV-фолбэк на свой сервер ВМЕСТЕ с текстами сообщений,
  и сервер джойнит их; Google/Apple карают за расхождение декларации с трафиком):
  - **User Content**: Messages (тексты чатов клиентов; идут через Wappi → наш n8n →
    OpenAI как процессоры), Photos or Videos (прайс-фото), Other User Content
    (файлы прайс-листов), **Audio Data** (голосовые клиентов — транскрибируются
    сервисом) — purpose App Functionality, **LINKED to identity** (связаны с
    идентификатором установки на сервере), no tracking.
  - **Contact Info**: Phone Number (номер владельца при подключении; номера клиентов
    как chat id), Email Address + Physical Address (контакты бизнеса, введённые
    владельцем и отправляемые в workflow) — App Functionality, linked, no tracking.
  - **Identifiers**: **User ID** (RevenueCat appUserID) + **Device ID**
    (SystemInfo.deviceUniqueIdentifier как фолбэк до резолва RC) — App Functionality,
    linked, no tracking.
  - **Purchases**: Purchase History (RevenueCat) — App Functionality, linked, no tracking.
  - Tracking: **No** (кросс-приложенческого/рекламного трекинга нет — это правда).
  - После первого Xcode-архива: Product → Generate Privacy Report и сверить лейблы
    с агрегированными манифестами SDK.
- [x] **Подписки** (владелец, 2026-09-03: цены сверены, RU-локализация названий и
  описаний, скриншоты для ревью приняты, RevenueCat — entitlement ids/продукты/вебхук на
  месте). Было: subscription group + 7 SKU заведены Блоком 1 — сверить цены
  (месяц 9 990 / 19 990 / 39 900 ₸; год 99 000 / 198 990 / 399 990 ₸; топ-ап 3 900 ₸),
  добавить RU-локализацию названий SKU, приложить юр-URL к группе (Terms).
- [x] Каждому SKU — скриншот для ревью (с РЕЛИЗНОЙ сборки, оригиналы ≥640×920 без
  мессенджер-сжатия; приняты 2026-09-03).
- [ ] На странице версии 1.0 в блоке «In-App Purchases and Subscriptions» ОТМЕТИТЬ все
  7 SKU — первая подача SKU идёт только вместе с версией; иначе они останутся
  «Ready to Submit» и пейволл в ревью покажет ₸-фолбэк без покупки.
- [ ] **Review Notes**: вставить текст из `app-review-notes.md` (ссылка на демо-видео уже
  внутри). «Sign-in required» НЕ отмечать (объяснение в notes, почему логин невозможен);
  контакт для ревью — имя/телефон/e-mail владельца.
- [x] Export compliance: только HTTPS → «standard encryption», exempt. Автоматизировано
  2026-09-01: `FixIOSBuildSettings` пишет `ITSAppUsesNonExemptEncryption=false` в
  Info.plist каждой сборки — ASC не будет задавать вопрос на каждую загрузку.
- [ ] **Release: Manually release this version** (подача ≠ релиз до Блока 2).
- [ ] Ротация ключей, бывавших в старых сборках (n8n admin key → `.secrets/prod-api-key.txt`
  + credential «n8n Admin API»; BotFather revoke → credential «Support Bot»; Green API
  revoke) — по желанию: обязательна только если сборка до 2026-08-31 покидала ваш Mac.
- [ ] После активации аккаунта — подать заявку в **Apple Small Business Program**
  (15% вместо 30%; спека Блока 1 §10.3). Учесть: применится со СЛЕДУЮЩЕГО месяца
  после одобрения.
- [ ] **Сборка и TestFlight-прогон (до сабмита):** шаги 1–2 ЗАКРЫТЫ 2026-09-06 — архив
  билда 2 (`Unity-iPhone 06.09.2026, 19.31`: 1.0 (2), четыре RU-строки назначения, без
  dev-маркеров) проверен с Mac; загрузить его через Organizer → Distribute App. Билд 1 в
  ASC (1.0 (1), английские строки) НЕ прикреплять к версии. Для билда 1 писем ITMS-9105x
  не было и вопрос про шифрование не задавался — для билда 2 ожидается то же.
  2026-09-06 вечер: билд 2 ЗАГРУЖЕН и обработан; TestFlight-прогон сокращён решением владельца
  до главного — sandbox-покупка «Старт»/месяц + «Восстановить покупки» ПРОШЛИ на устройстве
  (первое подтверждение живой связки StoreKit → RevenueCat → право). Остальные пункты шага 4
  и Privacy Report пропущены осознанно (ссылки/QR/старт проверены другими путями).
  1. Unity: `Tools/Store Compliance/Apply iOS Store Settings` (строки назначения → Player
     Settings + json трёх плагинов) и `Tools/Store Compliance/Bump iOS Build Number` (каждая
     загрузка в ASC — новый номер; число из Xcode Unity перезапишет). Платформа iOS,
     `iPhoneSdkVersion` = Device (988 — проверено 2026-09-06 после симуляторных сборок),
     **Development Build ВЫКЛ**, Append в `Builds/Built - iOS Automation`. После экспорта
     проверить 4 строки в `Info.plist` (все на русском, микрофон НЕ пустой — см. CLAUDE.md):
     `plutil -p "Builds/Built - iOS Automation/Info.plist" | grep UsageDescription`.
     ФАКТ 2026-09-06: билд 1 ушёл в ASC с английскими строками микрофона/фото (NativeShare без
     файла настроек + Append-слияние старого plist) — заменяется билдом 2 ДО сабмита.
  2. Xcode 26.6 (стоит, `xcode-select` → Xcode.app): Team/подпись автоматическая, Version 1.0
     Build 1 (из Player Settings), Product → Archive → Distribute → App Store Connect → Upload.
  3. После обработки билда: письма ITMS-9105x (privacy manifest) — если пришли, добавить
     декларации и перезалить; вопрос export compliance задаваться не должен (plist-ключ).
  4. TestFlight на телефоне: онбординг → визард до QR/кода через VPN с выходом в США
     (QR-экран — единственное, что ревьюер трогает с сетью). ФАКТ 2026-09-06: wappi.pro
     доступен из США/ЕС/Ирана/KZ одинаково (check-host.net us1/us2/us3/de1/nl1/uk1/ir1/kz1:
     TLS ок, Let's Encrypt, nginx без WAF/CDN, тот же 404 на GET к API —
     https://check-host.net/check-report/4a434456k35, корень 4a434467kb31,
     choosereply.com 4a434473k67b). «Unable to complete SSL connection» на бесплатном VPN —
     это VPN подменяет/ломает TLS, не гео-блок: проверять ТОЛЬКО через VPN без перехвата
     (ProtonVPN free, сервер US; сначала Safari → https://wappi.pro без предупреждения о
     сертификате, затем QR-экран); Профиль → Подписка → Изменить тариф:
     цены из StoreKit (не ₸-фолбэк), **sandbox-покупка + «Восстановить покупки»**, обе
     юр-ссылки открываются; «О приложении» — 3 ряда документов; «Удалить все данные».
  5. Xcode: Product → Generate Privacy Report по архиву — сверить с labels.
- [ ] При первом Xcode-экспорте: убедиться, что в архиве есть `PrivacyInfo.xcprivacy`
  (Unity 6 генерирует свой; RevenueCat несёт свой; плагины yasirkula — проверить
  версии; недостающие required-reason API Apple подсветит письмом ITMS-91053 —
  тогда добавить недостающие декларации и перезалить).
- [ ] ⚠️ Guideline 2.3.10: в iOS-сборке и iOS-метаданных НЕ упоминать
  Android/Google Play (в коде уже учтено: fine-print ветвится по платформе).

## 2. Google Play Console

- [x] **Тип аккаунта проверен:** аккаунт создан ДО 2023 года (владелец, 2026-08-27) →
  требование обязательного closed test (введено для личных аккаунтов, созданных
  после 13.11.2023) **не применяется** — можно идти сразу в production-трек.
  Internal testing перед продакшеном — по желанию, не обязателен.
- [x] **Android-трек в репозитории ЗАКРЫТ 2026-09-06** — всё, что не требует консолей:
  Player Settings под Play (targetSdk 36, категория productivity, AAB, адаптивная
  иконка), манифест с одной launcher-activity, биллинг на Play (subs/inapp, замена
  подписки при смене тарифа, ключи цен, отмена без ошибки), страж релизной сборки
  без ключа/зависимости RevenueCat, точка сборки .aab, системная кнопка «Назад»,
  клавиатура Android через JNI-замер, .docx в пикере, Play-графика (7 скриншотов
  9:16 + feature graphic + иконка 512), тексты листинга, ответы на все формы —
  **см. `docs/store/play-console.md`** (там же порядок подачи и device-чек).
- [ ] **Владелец до сборки** (play-console.md §1): `revenueCat.androidKey` в secrets.json;
  upload-keystore (`keytool`, вне репо); Android Resolver → Force Resolve и коммит
  `mainTemplate.gradle` + `AndroidResolverDependencies.xml`; сборка
  `Tools/Store/Build Android App Bundle` (release, keystore из env); проверки артефакта
  (16 KB `zipalign -c -P 16`, `aapt2 dump permissions` без READ_MEDIA_*/AD_ID).
- [ ] Создать приложение: `com.synergysoft.choosereply`, RU listing — тексты и графика
  готовы (play-console.md §2).
- [ ] **Data Safety** — точная матрица в play-console.md §4 (дополнено: App interactions =
  pickStats, метаданные поддержки, device-id фолбэк — политика §3 дополнена 2026-09-06,
  копию `Tools/landing/privacy.html` ПЕРЕЗАЛИТЬ на VPS). Исходная сверка с Apple (2026-09-01):
  собираются Messages, Photos and videos, Files and docs, **Voice or sound
  recordings**, **Phone number**, Email + Address (введённые владельцем),
  **Device or other IDs** (RevenueCat ID + device-id фолбэк), Purchase history —
  всё purpose «App functionality», шифрование в транзите. Wappi/n8n/OpenAI/Supabase —
  процессоры по нашим инструкциям ⇒ «Data shared» = **No** (политика §5 это
  фиксирует). «Можно запросить удаление» = **Yes** честно ЧЕРЕЗ: «Удалить все
  данные» в приложении (локальное + RAG/оригиналы/bot_profiles) + запрос на
  synergyexpertgroup@gmail.com (политика обещает 30-дневный SLA). Серверная память диалогов
  чистится при удалении бота с 2026-09-01 (DeleteBotFiles → Delete Chat Memory,
  PROBE GREEN) — оговорка снята. No ads, no tracking, данные не продаются.
- [x] **Серверная задача ЗАКРЫТА 2026-09-01**: `DeleteBotFiles` теперь чистит и
  серверную память диалогов — `n8n_chat_histories`, `conversation_outcomes`,
  `reply_mode_flags` по profile id (`apply-delete-history.py`, PROBE GREEN на
  проде). «Удалить все данные» = локальное + RAG/оригиналы + bot_profiles +
  память диалогов; ответ «можно запросить удаление» честен без оговорок.
- [ ] Privacy Policy URL — тот же.
- [ ] App content: Ads No, App access (EN-текст в play-console.md §3.1 + демо-видео),
  IARC-опросник (ответы в §3), Target audience 18+, Advertising ID No.
- [ ] Подписки: 6 продуктов `sub.*.month/year` (по ОДНОМУ base plan каждый, помечен
  Backwards compatible) + `topup.dialogs.500`; RevenueCat Google app + service-account
  JSON + RTDN; License testing — play-console.md §5.
- [ ] App signing by Google Play — включить (дефолт).
- [ ] Staged rollout / managed publishing — включить managed publishing
  (аналог manual release).

## 3. После одобрения (НЕ делать до Блока 2)

- [ ] **Хвост на первое обновление (найдено 2026-09-06 при sandbox-проверке):** докупка
  «500 диалогов» зачисляется (RevenueCat → n8n `RevenueCat Events` → `subscribers.topup_balance`,
  GetUsage отдаёт `topupBalance`), но на странице «Подписка» резерв НЕ ВИДЕН, пока квота не
  исчерпана — счётчик печатает только «N из квоты» (резерв = резерв, решение 2026-08-26), а
  подсказка с резервом живёт лишь на вкладке «Боты» в состоянии Reserve/Warn. Ревьюеру
  хватает подтверждения «Диалоги начислены»; пользователю — нет. Нужна строка «+ N в резерве»
  под счётчиком (аддитивно через SubscriptionPageBuilder + seam в SubscriptionPageRows).

- [ ] Релиз кнопкой — только когда задачи 0–5 Блока 2 закрыты (каждая строка
  пейволла «Во всех тарифах» работает).
- [ ] Обновление с клиентскими кусками Блока 2 подать апдейтом — ревью апдейтов
  быстрое; серверные куски Блока 2 (n8n) ревью не требуют вообще.

## Известные риски первого ревью (готовые ответы — в app-review-notes.md)

1. **Автоматизация WhatsApp неофициальным путём** (Guideline 5.2.2). Митигция:
   честные notes — приложение работает с СОБСТВЕННЫМ аккаунтом владельца бизнеса,
   пользователь сам подключает свой номер; прецеденты таких приложений в сторе есть.
   Возможен запрос разъяснений — отвечать по тексту notes, не переформулировать.
2. **Триал вне StoreKit** (5 дней app-level, без карты). Митигция: notes объясняют,
   что оплата запрашивается только через IAP; триал ничего не продаёт.
3. **Ревьюер не может авторизовать WhatsApp** — поэтому демо-видео обязательно.
