# Google Play Console — подача Choose Reply (Android-трек)

Собрано 2026-09-06 по итогам Android-аудита (пять направлений: настройки сборки,
биллинг, плагины/разрешения, runtime-UX, листинг/Data Safety). Всё, что можно было
сделать в репозитории, — сделано и закоммичено; ниже — что уже есть, что делает
владелец в консолях, и точные ответы на формы. Тексты для форм Play — копировать
блоками.

## 0. Что уже сделано в репозитории

| Что | Где | Как воспроизвести |
|---|---|---|
| Player Settings под Play: targetSdk **36** (требование Play для новых приложений с 31.08.2026), minSdk 25, категория приложения **productivity** (было «game» — дефолт Unity 6.3), App Bundle ON, адаптивная иконка | `Assets/Editor/StoreAndroidSettingsApplier.cs` | `Tools/Store Compliance/Apply Android Store Settings` (или headless `-executeMethod StoreAndroidSettingsApplier.ApplyHeadless`) |
| Адаптивная иконка Android: фон + передний план из ТОГО ЖЕ векторного концепта U9, что и мастер `Icon.png` (знак ×0.63 в 72dp-окне маски, градиент растянут на видимое окно) | `Assets/Images/Icon_android_{bg,fg}.png` (432), превью под масками `Tools/icon-lab/appicon/out/android/adaptive_preview.png` | `node Tools/icon-lab/appicon/android.js` |
| Манифест: одна launcher-activity (`UnityPlayerGameActivity`, entry = GameActivity), `android:exported="true"`, дубль `WAKE_LOCK` убран | `Assets/Plugins/Android/AndroidManifest.xml` | — |
| Биллинг на Play: цены запрашиваются двумя запросами (`subs` + `inapp` — Play Billing делит по типу), ключи цен нормализуются (`sub.start.month:monthly` → `sub.start.month`), **смена тарифа — ЗАМЕНА подписки** (`oldSku` + ImmediateWithTimeProration; без этого Play оформлял бы ВТОРУЮ подписку рядом с первой), отмена листа покупки больше не показывается как ошибка | `Assets/Scripts/Billing/StorePurchaseRules.cs`, `RevenueCatBackend.cs`, `PlanCatalog.SubscriptionSkus` | тесты `AndroidBillingPathTests` |
| На устройстве никогда не выбирается `FakeBillingBackend` (раньше пустой `androidKey` = все тарифы «покупались» бесплатно) | `BillingService.SelectBackendKind` | — |
| Страж сборки: релизная сборка без ключа RevenueCat для платформы или без разрешённой Android-зависимости RevenueCat в `mainTemplate.gradle` **падает**; Development Build только предупреждает | `Assets/Editor/StoreBillingKeyGuard.cs` | — |
| Точка сборки .aab: применяет настройки, тянет keystore из переменных окружения, запускает EDM4U-резолв, собирает release | `Assets/Editor/StoreAndroidBuild.cs` | `Tools/Store/Build Android App Bundle` или headless (см. §1.4) |
| Системная кнопка/жест «Назад» на Android теперь работает на каждом экране (роутер по стеку поверхностей; в корне — сворачивание приложения) | `Assets/Scripts/Main/AndroidBackRouter.cs`, `BackNavigation.cs` | тесты `BackNavigationTests`; на устройстве — §5 |
| Клавиатура на Android: композер чата и подъём полей читают высоту IME через проверенный JNI-замер (`KeyboardInset`), а не `TouchScreenKeyboard.area` (нулевой rect = подъём на весь экран); baked-зона home-bar вычитается вместо `safeArea.y`, который в immersive-режиме равен 0 | `KeyboardAwarePanel`, `FocusedFieldKeyboardLift`, `KeyboardLiftMath.BakedHomeBarCanvasPx` | на устройстве — §5 |
| .docx в пикере прайс-листов на Android (раньше туда уходил iOS-UTI, и все .docx были серыми) | `BotSettings.Auth.cs` | — |
| Витринная графика Play: 7 скриншотов 1080×1920 (9:16, Android-хром, шрифт системный — SF Pro лицензирован только под Apple), feature graphic 1024×500, иконка 512 | `Tools/store/listing/play-phone/*.png`, `Tools/store/listing/play/{feature-graphic,icon-512}.png` (gitignored) | `python3 Tools/store/compose-listing.py play` → `python3 Tools/store/play-graphics.py` |
| 16 KB page size: `libwebp` arm64 выровнен на 0x4000, AAR-плагины без нативного кода, exoplayer — Java; Unity 6000.3 собирает свои .so с 16K | проверено `readelf`-скриптом 2026-09-05 | после первой сборки — `zipalign -c -P 16 -v 4` (§1.5) |
| `*.keystore`/`*.jks` в `.gitignore` | `.gitignore` | — |

## 1. Перед первой сборкой (владелец)

1. **Ключ RevenueCat для Android.** RevenueCat → Project → Apps → «+ New» → Google Play
   (package `com.synergysoft.choosereply`) → скопировать **public SDK key** (`goog_…`) в
   `Assets/StreamingAssets/secrets.json` → `revenueCat.androidKey`. Пока ключа нет,
   релизная сборка падает по `StoreBillingKeyGuard` (осознанно).
2. **Upload-keystore** (один раз, хранить ВНЕ репозитория, пароли — в Связке ключей):
   ```bash
   keytool -genkeypair -v -keystore ~/Keys/choosereply-upload.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000
   ```
   Play App Signing (дефолт для новых приложений) хранит ключ подписи у Google; upload-ключ
   при утере меняется через поддержку Play. В Player Settings хранится только ПУТЬ —
   пароли передаются сборке через окружение (п. 4).
3. **Android Resolver (EDM4U).** В редакторе: Assets → External Dependency Manager →
   Android Resolver → **Force Resolve**. Должно появиться в `Assets/Plugins/Android/mainTemplate.gradle`
   между `// Android Resolver Dependencies Start … End`:
   `implementation 'com.revenuecat.purchases:purchases-hybrid-common:18.23.0'` (+ androidx.annotation)
   и файл `ProjectSettings/AndroidResolverDependencies.xml`. **Закоммитить оба.** Без этого
   `PurchasesWrapper.java` не компилируется, а если резолв сработает молча не до конца —
   RevenueCat падает в `Configure` и приложение живёт в вечном «пробном» режиме
   (страж сборки это теперь ловит).
4. **Сборка .aab** (release, без Development Build — иначе водяной знак):
   ```bash
   export CR_UPLOAD_KEYSTORE=~/Keys/choosereply-upload.jks CR_UPLOAD_KEYSTORE_PASS='…' CR_UPLOAD_KEY_ALIAS=upload CR_UPLOAD_KEY_PASS='…'
   /Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -buildTarget Android -executeMethod StoreAndroidBuild.BuildAab -logFile Tools/test-output/android-build.log
   ```
   Редактор при этом должен быть закрыт (project lock). Результат:
   `../Builds/Android/ChooseReply-1.0-1.aab`. Из редактора то же самое:
   `Tools/Store/Build Android App Bundle` (переменные окружения должны быть у процесса
   Unity — запускать Hub/Unity из терминала с `export`).
   Переключение build target на Android переимпортирует текстуры (десятки минут на этом
   Mac); обратно на iOS — быстро (артефакты кэшируются).
5. **Проверки артефакта** (владелец, один раз):
   - `bundletool build-apks --bundle=….aab --output=….apks --mode=universal`, распаковать
     `universal.apk` и `zipalign -c -P 16 -v 4 universal.apk` (16 KB) — ожидаем «Verification successful».
   - `aapt2 dump permissions universal.apk` — НЕ должно быть `READ_MEDIA_*`, `RECORD_AUDIO`,
     `CAMERA`, `com.google.android.gms.permission.AD_ID`. Если AD_ID пришёл транзитивно —
     добавить в манифест `<uses-permission android:name="com.google.android.gms.permission.AD_ID" tools:node="remove"/>`.
   - В `unityLibrary/src/main/AndroidManifest.xml` экспортированного проекта — ровно одна
     activity с категорией LAUNCHER.
6. **Bump перед каждой загрузкой:** `AndroidBundleVersionCode` (Player Settings →
   Other → Version Code) — Play требует монотонный рост; `bundleVersion` = маркетинговая версия.

## 2. Play Console: приложение и листинг

Создать приложение: название **Choose Reply**, язык по умолчанию **ru-RU**, тип
**Приложение** (не игра), **Бесплатно** (подписки внутри). Категория **Бизнес**.
Контакт: `synergyexpertgroup@gmail.com`, сайт `https://choosereply.com`.

### 2.1 Тексты (лимиты соблюдены)

**Название (≤30):** `Choose Reply`
— только бренд; «WhatsApp»/«Telegram» в названии Play читает как заявление о связи.

**Краткое описание (≤80, сейчас 76):**
```
ИИ отвечает вашим клиентам в мессенджерах, пока вы заняты. 5 дней бесплатно.
```

**Полное описание (≤4000):**
```
Choose Reply — ИИ-ассистент, который отвечает клиентам вашего бизнеса в WhatsApp и Telegram. Вы подключаете свой собственный номер, описываете бизнес и загружаете прайс-лист — дальше ассистент отвечает на вопросы клиентов сам, а вы видите каждый диалог и в любой момент можете вмешаться.

КАК ЭТО РАБОТАЕТ
1. Создайте бота и подключите свой аккаунт WhatsApp или Telegram — по QR-коду или коду, как при входе на компьютере.
2. Расскажите о бизнесе: чем занимаетесь, товары и услуги, контакты, часы работы.
3. Загрузите прайс-лист — файлом или просто фото. Ассистент будет отвечать по вашим ценам.
4. Включите режим «Авто» — и клиенты получают ответы круглосуточно.

ДВА РЕЖИМА РАБОТЫ
• «Авто» — ассистент отвечает клиентам сам, без вашего участия.
• «Вместе» — ассистент предлагает варианты ответа, а вы выбираете и отправляете подходящий одним касанием. Удобно, когда хочется контролировать каждое слово.
Режим переключается для бота целиком или для отдельного чата.

ЧТО УМЕЕТ CHOOSE REPLY
• Отвечает на вопросы о товарах, услугах, ценах, адресе и графике работы.
• Понимает голосовые сообщения клиентов.
• Работает по вашему прайс-листу: файлы, таблицы и фотографии.
• Показывает все чаты подключённых аккаунтов прямо в приложении: читайте, отвечайте сами, отправляйте фото и файлы.
• «Сводка» — итоги диалогов: кто готов купить, кому нужен ваш ответ, где вопрос закрыт.
• Несколько ботов для разных направлений бизнеса — в одном приложении.
• Ваши инструкции ассистенту: тон общения, что говорить и о чём молчать.

ДЛЯ КОГО
Для малого бизнеса, где клиенты пишут в мессенджеры: автозапчасти, оптовая торговля, цветы, продавцы на маркетплейсах, образование, ремонт техники — и любые другие услуги, где важно быстро ответить.

ПОДКЛЮЧЕНИЕ И КОНТРОЛЬ
Подключается только ваш собственный аккаунт — так же, как WhatsApp Web. Ассистент отвечает только на входящие сообщения ваших клиентов; массовых рассылок нет. Отвязать аккаунт можно в любой момент — в приложении или в самом мессенджере («Связанные устройства»).

ТАРИФЫ
Первые 5 дней — бесплатно, без привязки карты: пробный период начинается при подключении первого канала. Дальше — подписка Google Play на выбор: «Старт», «Бизнес» или «Сеть» (на месяц или на год); тарифы отличаются числом ботов, каналов и диалогов ИИ в месяц. Если диалогов не хватило, можно докупить пакет. Подписка продлевается автоматически; отменить её можно в любой момент в настройках подписок Google Play. Точные цены показаны в приложении.

ВАШИ ДАННЫЕ
Вы видите каждый диалог, можете отключить ассистента или отвязать аккаунт в любой момент — из приложения или из самого мессенджера. Все данные можно удалить одной кнопкой в разделе «Аккаунт».

Условия использования: https://choosereply.com/terms.html
Политика конфиденциальности: https://choosereply.com/privacy.html
Поддержка: synergyexpertgroup@gmail.com

Choose Reply — независимое приложение, не связанное с WhatsApp LLC, Meta Platforms или Telegram. WhatsApp и Telegram — товарные знаки их владельцев.
```
Осознанно: цен в тексте нет (Play локализует цены сам; расхождение с чеком — риск),
слов «App Store», «iPhone», «Apple», «неофициальный», названий шлюзов — нет; заявлены
только функции из чек-листа пейволла (`PaywallRows.AllPlansFeatures`).

### 2.2 Графика

| Слот Play | Файл | Требование |
|---|---|---|
| Значок приложения | `Tools/store/listing/play/icon-512.png` | 512×512 PNG, Play накладывает свою маску |
| Feature graphic (обязателен) | `Tools/store/listing/play/feature-graphic.png` | 1024×500, без альфы |
| Скриншоты телефона (2–8) | `Tools/store/listing/play-phone/01-vmeste … 07-plans.png` | 1080×1920 (9:16), порядок как для ASC |
| Планшетные скриншоты | не загружать (приложение телефонное; без них Play лишь не продвигает на планшетах) | — |

Перегенерация после любого изменения UI: `Tools/Store/Capture Screenshots` (редактор,
Game view 1284×2778) → `python3 Tools/store/compose-listing.py` (оба стора) →
`python3 Tools/store/play-graphics.py`.

## 3. Policy → App content (ответы)

| Раздел | Ответ |
|---|---|
| Privacy policy | `https://choosereply.com/privacy.html` |
| Ads | **No**, приложение не содержит рекламы |
| App access | **All or some functionality is restricted** → инструкции из §3.1 (логина нет; нужен реальный аккаунт мессенджера; демо-видео) |
| Content rating (IARC) | категория **Utility, Productivity, Communication, or Other**; насилие/секс/язык/наркотики/азарт — **No**; «пользователи могут общаться или обмениваться контентом» — **Yes** (владелец переписывается с клиентами в WhatsApp/Telegram); «делится геопозицией» — No; «покупки цифровых товаров» — **Yes**; «UGC, который видят другие» — No. Ожидаемый рейтинг: Everyone/3+ с элементами «Users Interact», «In-App Purchases» |
| Target audience | **18 и старше** (ничего младше не отмечать; Families-политика не применяется) |
| News app | No |
| COVID-19 contact tracing / status | No |
| Data safety | §4 |
| Government app | No |
| Financial features | «My app doesn't provide any financial features» (подписки Play Billing — не финансовая функция) |
| Health | No |
| Advertising ID | **No** (код GAID не читает; RevenueCat собирает GAID только по явному `collectDeviceIdentifiers()`, которого нет) — подтвердить по манифесту первой сборки, §1.5 |
| Photo and video permissions | форма **не требуется**: `READ_MEDIA_IMAGES/VIDEO` нет; NativeGallery 1.9.1 на Android 13+ обходится без storage-разрешений (ACTION_PICK/GET_CONTENT). Не обновлять yasirkula-плагины на версии, добавляющие `READ_MEDIA_*`, без пересмотра |
| Foreground service | нет |
| App category | Business |

### 3.1 App access — текст (EN, вставить в поле инструкций)

```
There is no login or account system in Choose Reply — every screen is reachable without credentials.

The core flow (linking a messenger account and receiving customer messages) requires a REAL WhatsApp or Telegram account on a live phone number, authorized from the messenger app via QR code or pairing code — exactly like linking WhatsApp Web. It cannot be reproduced with test credentials, so we recorded the complete end-to-end flow with a real account:

Demo video: https://choosereply.com/review/923843a986255cba/ChooseReply-demo-2026-09.mp4
(recorded on the iOS build of the same project; the Android build is identical in UI and flow)

What you can test directly in the review build:
1. First-run onboarding.
2. The bot creation wizard up to the WhatsApp/Telegram authorization screen (QR + pairing code UI).
3. The paywall: Profile → «Подписка» → «Изменить тариф». Monthly/yearly toggle, three tiers, feature list, Terms of Use and Privacy Policy links, «Восстановить покупки» (Restore purchases).
4. Purchases go exclusively through Google Play Billing: 3 subscription tiers × monthly/yearly plus one consumable dialog top-up. Test purchases work with any account listed under Play Console → Setup → License testing — tell us the reviewer account and we will add it.

The 5-day trial is app-level functionality: it starts when the user first connects a messenger channel, requires no payment method, and the trial button does not initiate a purchase. The app connects only accounts that belong to the user, replies to inbound customer messages of the user's own business, and never sends bulk or unsolicited messages; the account can be unlinked at any time from the app or from the messenger's own "Linked devices" screen.
```
Правило из `app-review-notes.md` сохраняется: без номеров гайдлайнов, без имени
шлюза-вендора, без анализа условий мессенджеров.

## 4. Data safety (форма)

**Обзор:** собирает данные — **Yes**; передаёт третьим лицам — **No** (Wappi, OpenAI,
Supabase, n8n, RevenueCat, Google — обработчики по нашим инструкциям, политика §5);
шифрование в транзите — **Yes** (все эндпоинты https); пользователь может запросить
удаление — **Yes** («Удалить все данные» в приложении + запрос на
`synergyexpertgroup@gmail.com`, политика обещает 30 дней); аккаунтов приложение не
создаёт (URL удаления аккаунта не требуется — в поле инструкций описать путь
Профиль → Аккаунт → «Удалить все данные»).

Все типы ниже: **Collected = Yes, Shared = No, Ephemeral = No, Purpose = App functionality**
(без Analytics / Advertising / Personalization).

| Категория → тип | Обязательно? | Откуда в коде |
|---|---|---|
| Personal info → Phone number | required | номер владельца при подключении (`auth/code`), номера клиентов как chatId (`N8nSuggestionsProvider`), телефон бизнеса в `ComposeBusinessKnowledge` |
| Personal info → Email address | optional | контакт бизнеса (`ComposeBusinessKnowledge`) |
| Personal info → Address | optional | адрес бизнеса |
| Personal info → Name | optional | имя бота/бизнеса; имя в обращении в поддержку |
| Messages → Other in-app messages | required | последние 24 сообщения чата → `/webhook/SuggestReplies`; исходящие → Wappi |
| Photos and videos → Photos, Videos | optional | вложения чата, фото прайс-листов |
| Files and docs | optional | прайс-листы, документы чата |
| Audio → Voice or sound recordings | optional | приложение НЕ записывает звук (RECORD_AUDIO нет); голосовые клиентов приходят через шлюз и распознаются на сервере — безопасное сверх-декларирование, как в политике §3. Нигде не описывать как использование микрофона |
| App activity → App interactions | optional | `pickStats` — счётчики выбранных подсказок «Вместе» |
| App activity → Other user-generated content | optional | промпт, описание бизнеса, каталог |
| Device or other IDs | required | анонимный appUserID RevenueCat; до его получения — обезличенный идентификатор устройства (`SystemInfo.deviceUniqueIdentifier`) |
| Financial info → Purchase history | required | статус подписки/покупок (RevenueCat / Play Billing) |
| Location, Contacts, Calendar, Health, Web browsing, Installed apps, Crash logs, Diagnostics | **не собираются** | аналитики и crash-SDK нет (UnityConnect выключен) |

Безопасность: encrypted in transit — Yes; deletion mechanism — Yes; независимого аудита
безопасности — No.

**Политика конфиденциальности дополнена (2026-09-06, §3):** идентификатор установки
(RevenueCat / device-id фолбэк), статистика выбора подсказок, метаданные обращения в
поддержку (контакт по желанию, версия, платформа, модель устройства). Копии
`docs/legal/privacy.html` и `Tools/landing/privacy.html` идентичны; **перезалить на VPS**
(владелец): `scp Tools/landing/privacy.html choosereply:~/choosereply/site/privacy.html`.

## 5. Биллинг: Play Console + RevenueCat

**Play Console → Monetize → Products.** Шесть подписок и один товар — идентификаторы
ПОБУКВЕННО как в `PlanCatalog` (приложение покупает по голому product id):

| Product ID | Тип | Период | Цена (KZT, как в ASC) |
|---|---|---|---|
| `sub.start.month` | Subscription, один base plan (auto-renewing, 1 month) | месяц | 9 990 |
| `sub.start.year` | Subscription, один base plan (1 year) | год | как в PlanCatalog |
| `sub.business.month` | Subscription | месяц | 19 990 |
| `sub.business.year` | Subscription | год | как в PlanCatalog |
| `sub.network.month` | Subscription | месяц | 39 900 |
| `sub.network.year` | Subscription | год | как в PlanCatalog |
| `topup.dialogs.500` | In-app product (one-time) | — | 3 900 |

Ловушка Play: у одной подписки может быть несколько base plan. Приложение и RevenueCat
работают с ГОЛЫМИ product id, поэтому у каждой подписки ровно **один** base plan, и он
помечен **Backwards compatible** — иначе RevenueCat не разрешит id без суффикса
`:basePlanId`. Не делать «3 подписки × 2 base plan» — идентификаторы разъедутся с
`PlanCatalog`.

**RevenueCat:** Apps → Google Play app (package `com.synergysoft.choosereply`) →
загрузить **service account JSON** (Google Cloud → сервисный аккаунт с доступом к Play
Developer API, права «View financial data» + «Manage orders and subscriptions»); включить
**Real-time developer notifications** (RevenueCat выдаёт Pub/Sub topic → Play Console →
Monetization setup → RTDN). Продукты импортировать и привязать к entitlement’ам
`tier_start` / `tier_business` / `tier_network` (месяц + год к одному entitlement); топ-ап —
без entitlement (начисляется сервером по вебхуку `RevenueCatEvent`, он store-agnostic,
менять не нужно). Consumable-режим топ-апа задаётся в RevenueCat (Play сам не помечает
товар consumable).

**License testing:** Play Console → Setup → License testing → добавить Google-аккаунт
владельца (и ревьюера по запросу) — тестовые покупки во внутреннем треке бесплатны.

**Поведение смены тарифа на Play:** покупка другого тарифа/периода идёт как замена
(`oldSku` = текущая подписка, ImmediateWithTimeProration — остаток зачитывается). После
теста на лицензионном аккаунте в RevenueCat → Customer должна остаться ОДНА активная
подписка.

## 6. Что проверить на устройстве (тесты этого не покрывают)

- Иконка под круглой/squircle/rounded масками лаунчера (превью: `adaptive_preview.png`).
- Системная кнопка/жест «Назад»: закрывает чат, настройки бота, пейволл, любой попап/лист,
  страницы профиля, дриллдаун сводки; в корне — сворачивает приложение. Во время
  slide-in чата и под LoadingPanel — проглатывается.
- Клавиатура: композер чата и листы поднимаются ровно на высоту IME, без 92u-полосы
  над клавиатурой и без «подъёма на весь экран» (это была слабость старого чтения
  `TouchScreenKeyboard.area`). Проверить с gesture-nav и с 3-кнопочной навигацией.
- Пикер: фото/файлы без запроса разрешений на Android 13+; .docx выбирается.
- Пейволл: цены приходят из Play (формат Play, «9 990,00 ₸»); отмена листа покупки —
  без красного уведомления; смена тарифа — одна активная подписка.
- Тёмная клавиатура следует системной теме (на Android — из настроек ОС, это ожидаемо).
- Слой выделения текста (долгое нажатие, пины, меню) — проверен только на iOS.

## 7. Известные Android-пробелы (не блокеры подачи)

- **Immersive fullscreen + baked-инсеты под iPhone.** Статус-бар скрыт, верхние ~150u
  TopBar на Android пустуют; при 3-кнопочной навигации панель навигации скрыта до свайпа
  снизу. Решение — осознанное: (а) оставить как есть или (б) `androidStartInFullscreen: 0`
  + рантайм-сдвиг контента шапки/низа на Android. Принять ДО пересъёмки Play-скриншотов
  с Android-сборки.
- Левые полосы back-swipe (чат 175u, настройки 200u) начинаются у самого края, который
  Android 10+ отдаёт системному жесту; теперь жест уходит в роутер «Назад» — то же действие.
- Видео из галереи отправляются без конвертации (на Android галерея почти всегда
  MP4/H.264, проблема iOS .mov/HEVC здесь не воспроизводится) — наблюдать.
- Открытие документа из чата: QuickLook только на iOS; на Android — системный share.
- «Купить» для уже купленного тарифа: Play вернёт ITEM_ALREADY_OWNED (на iOS StoreKit
  скажет «уже подписан»); косметика пейволла — отдельная задача.

## 8. Порядок подачи

1. §1 (ключ, keystore, резолв, сборка, проверки артефакта).
2. Play Console: создать приложение → Store listing (§2) → App content (§3) → Data safety (§4)
   → Monetize (§5) → Setup → License testing.
3. Internal testing → загрузить .aab → установить на своём устройстве → §6.
4. Production → загрузить тот же .aab → **managed publishing** ON → отправить на ревью.
5. После одобрения — релиз кнопкой (как в `submission-checklist.md` §3).
