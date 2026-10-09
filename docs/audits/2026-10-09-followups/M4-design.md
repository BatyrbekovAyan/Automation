# M4 — бесконечный `claim.start` у бота, удалённого чисткой: подтверждение и дизайн фикса

Дата: 2026-10-09. HEAD `d08c345d`. Статус: **подтверждено, не исправлено** (это проектирование, код не менялся).
Источник находки: `docs/audits/2026-09-28-iteration/README.md` («Новые находки вне итерации»), опровергатель V12 в
`docs/audits/2026-09-28-iteration/verification-results.json` (`refute[V12]`, пункт M4).

Где это сейчас: серверная половина (Auth Session) работает на проде с 2026-09-24 (A1) и не менялась пакетом 2026-10-09.
Клиент A1 в сторы не выпущен, поэтому сегодня затронуты только dev-сборки (телефон владельца). После выпуска A1-клиента
затронут основной сценарий воронки: **любая** установка, где бот пережил окончание триала без оплаты.

---

## 1. Подтверждение

### 1.1 Трасса на HEAD

Клиент:

1. `Manager.LoadBots` (`Assets/Scripts/Main/Manager.cs:652`) запускает `ProfileClaimFlow.RunAtBoot(BotsRoot)`.
2. `RunAtBoot` (`Assets/Scripts/Auth/ProfileClaimFlow.cs`) вызывает `EnsureSession`, затем `while (true)`:
   `ProfileClaimPolicy.NextUnowned(ConnectedProfiles(...), auth.Claims, Snoozed)`.
   - `ConnectedProfiles` — каждый `whatsappProfileId`/`telegramProfileId` бота, если он не `""` и не `"-1"`. Чистка клиенту
     ничего не сообщает, поэтому у «зомби» id в PlayerPrefs остаются.
   - `p` токена строится только из живых строк (`deleted_at is null` в `VERIFY_INSTALL_SQL` и `RELOAD_OWNERSHIP_SQL`,
     `Tools/n8n/build-auth-workflows.py`). Id удалённого профиля в `p` не попадает никогда, поэтому `NextUnowned` его возвращает.
3. `ClaimOne` → `AuthClient.Claim("claim.start")` → `POST /webhook/AuthSession`.

Сервер (`crAuthSession001`):

4. `Route` → `Verify Install` (PG №1) → `Claim Op?` → `Install Verified?` → `Claim Start` (PG №2). Это upsert. При конфликте
   и `owner_id = $2` поля `claim_pending_*` не меняются, `deleted_at` не трогается. Возвращается `owner_id` = эта установка.
5. `Needs Status?` (op ≠ `claim.verify`) → `Claim Status`: Wappi `GET sync/get/status` по удалённому профилю, с серверным
   (общим) credential.
6. `Claim Decide`: `else if (row.owner_id === inst.install_id) { out.claimMethod = 'owned'; }`. Проверки `deleted_at` нет,
   и эта ветка стоит **до** проверки ответа транспорта. Поэтому ответ Wappi ни на что не влияет: 200 `authorized:false` и 404
   дают одно и то же.
7. `Send Code?` = false → `Reload Ownership` (PG №3) возвращает `p` без профиля → `Issue Token` отдаёт `success:true`,
   `claimMethod:'owned'` и новый токен с `p = []`.

Снова клиент:

8. `AuthClient.Claim`: при `Success` вызывает `AdoptToken`, а тот — `PlayerPrefs.Save()`.
9. `ClaimOne`: `Success && Owned` → `finished(true)` (комментарий в коде: «already ours (token was just refreshed)»).
   Цикл `RunAtBoot` идёт на следующий круг, `NextUnowned` возвращает тот же профиль → шаг 3.

Уточнение к формулировке README: это не «на каждом старте». Это **бесконечный цикл внутри одного запуска**, без паузы, пока
приложение на экране. В фоне Unity на iOS и Android приостанавливается, при возврате цикл продолжается. При следующем запуске
всё начинается заново.

### 1.2 Воспроизведение (поведенческое, локально)

Скрипт: настоящий канон `crAuthSession001` через `Tools/n8n/tests/harness.py`.
- SQL `Claim Start`, `Reload Ownership`, `Verify Claim Code`, `Store Claim Code` — на temp-таблицах staging Postgres.
- `Claim Decide` и `Issue Token` — через node.
- If-узлы — движком выражений n8n.

Клиент отзеркален 1:1: `NextUnowned`, `ClaimOne`, `RunAtBoot`. n8n staging и мок не использовались, прод не трогался.
Код скрипта — в приложении A, полный вывод — в приложении B.

Сценарий A: у установки два бота. Первый — WA-канал, удалённый чисткой: строка `owner_id` = эта установка, `deleted_at` задан.
Второй — legacy TG-бот без владельца, которому действительно нужен код.

| Канон сервера × клиент | Что происходит |
|---|---|
| HEAD × HEAD | Ходы 1…6 (обрезано на 6): **все** по `Z-wa`. Каждый: `200 owned`, Wappi `get/status` вызван, код не отправлен, токен `p=[]`, строка `bot_profiles` перезаписана (новый `xmin`). До `L-tg` очередь **не доходит никогда**. Одинаково для `trial_expiry`, `churn_grace`, `liveness`, `app_delete`, а также при ответе Wappi 404 |
| фикс сервера × HEAD-клиент | `410 profile_retired` (+`retiredReason`), Wappi не вызывается. Старый клиент считает ответ «не годен» и выходит из цикла: **1 запрос за запуск**, но `L-tg` по-прежнему голодает |
| HEAD-сервер × фикс клиента | `owned`, но id нет в токене. Набор «спрошено в этом прогоне» не даёт повтора, `L-tg` получает лист кода. Но `Z-wa` спрашивается снова при **каждом** запуске, и честного состояния нет |
| фикс × фикс | Запуск 1: `Z-wa` → `410` → маркер сохранён → дальше `L-tg` (лист кода). Запуск 2: `Z-wa` не спрашивается вовсе |

Сценарий B — удалённая строка, которой эта установка **не** владеет. Это legacy-строка без владельца (компат-период,
до A1) или строка другой установки. Запись, которой владеет установка, — сценарий A.

- **B1** (HEAD): `Claim Start` записывает `claim_pending_by` на удалённую строку, и она попадает в `c` токена. Wappi отвечает
  «не авторизован» → `reauth`. Владелец видит лист «Подключите канал заново» **при каждом запуске**, и это тупик:
  - прокси `qr` по этому профилю проходит `Build Request` (`claimPairing`);
  - но `Record Claim Pairing` требует `deleted_at is null` и возвращает 0 строк;
  - итог — 403 `claim_not_pending` (проверено тем же harness);
  - а настройки бота для не-`-1` id идут в ветку «существующий профиль» → `status` → 403.

  Если Wappi отвечает не-200, будет `502 transport_unavailable`: тихая остановка цикла на каждом запуске, следующие боты голодают.
- **B2** (HEAD): ложное срабатывание liveness — профиль у Wappi на самом деле жив и авторизован. Тогда:
  - `claim.start` **отправляет код в чат** удалённого профиля;
  - `claim.verify` записывает `owner_id` на удалённую строку;
  - `p` её по-прежнему не содержит;
  - со следующего запуска это сценарий A, бесконечный цикл.
- С фиксом сервера оба случая дают `410 profile_retired`: без Wappi, без `pending`, без кода.

Регрессия существующих проверок с фиксом сервера (патч применён к копии канона в памяти, репозиторий не менялся):
- закоммиченный `Tools/n8n/tests/test_claim.py` (S1–S16 и негативы V2) — результат в приложении B.3;
- `verify-sql-comments.py` — OK;
- `verify-auth-gate.py` падает ровно на одном закреплённом выражении `Needs Status?` («V2: claim.verify must not call Wappi»):
  при фиксе это выражение меняется, и закрепление нужно обновить (§3).

### 1.3 Что видит владелец и сколько это стоит

Владелец не видит ничего:
- на ветке `Owned` нет ни листа, ни строки в логе;
- карточка бота выглядит живой (`Status` = сохранённое «Active», иконка канала цветная);
- чаты этого канала молча застыли на кеше: `chats.filter` через прокси → 403, `SyncAllChats` при неуспехе делает `yield break`;
- нажатие «Авто» даёт «Режим не сохранён» (SetReplyMode → 403).

Если «зомби» в списке карточек стоит раньше других непринятых ботов, их лист подтверждения в этом запуске **не появится
никогда** (голодание). Их чаты тоже 403 и тоже застывают.

Стоимость одного круга:
- клиент: 1 `POST /webhook/AuthSession` и `PlayerPrefs.Save()` (запись на диск);
- сервер: одно исполнение n8n, 3 Postgres-оператора (около 1,9 с каждый на проде) и 1 Wappi `get/status` с общим
  серверным токеном;
- в БД 2–3 перезаписи строк: `installs.last_seen_at` и при наличии billing id — `billing_bindings` в `Verify Install`, а `bot_profiles` перезаписывается upsert'ом `Claim Start` даже без изменений, это dead tuple.

Итого круг ≈ 6–8 с, то есть около **450–600 кругов в час** на одну установку, пока приложение открыто. Это 450–600 исполнений n8n
(исполнения сохраняются — ревью ссылалось на номера exec), 1350–1800 операторов PG и 450–600 вызовов Wappi.

Побочных эффектов нет:
- код в чат удалённого профиля в сценарии A не отправляется (ветка `owned` до `Send Code?` не доходит);
- ошибок нет ни на клиенте, ни на сервере;
- поэтому мониторинг этого не видит.

Риск, который нельзя проверить без прода: у общего Wappi-токена есть лимит частоты. Одна «зомби»-установка, которая
крутит `get/status`, может замедлять прокси для **всех** владельцев.

### 1.4 Как бот становится «зомби»

| Причина (`deleted_reason`) | Кто ставит | Достижимо? |
|---|---|---|
| `trial_expiry`, `churn_grace` | Profile Lifecycle Sweep (каждые 6 ч), профиль у Wappi удалён | **Основной случай**: триал закончился, бот остался на телефоне |
| `liveness` | та же чистка: профиль исчез из списка Wappi (кандидаты — живые строки старше часа у владельцев без активной подписки; `owner_id` остаётся) | Да |
| `app_delete` | Delete Bot Files в V12 — частичное удаление: unlink Wappi прошёл, DBF удалил строки, затем удаление клона упало временно → `Retry`, бот остался на телефоне | Да, до повторного успешного удаления |
| `channel_limit` | `Retire Refused Profile` в Create | Практически нет: клиент сбрасывает id в `-1` и удаляет профиль (`Manager.cs` ~3620) |

---

## 2. Дизайн фикса

Принцип:
- сервер отвечает «owned» только на живую строку, иначе он обещает то, чего не выдаёт токен;
- клиент не спрашивает один и тот же профиль дважды за прогон и запоминает удалённый канал.

Нужны обе половины. Только сервер — цикл превращается в один запрос за запуск, но голодание остаётся. Только клиент — по
одному лишнему запросу каждый запуск и нет честного состояния. Это показывает таблица §1.2.

### 2.1 Сервер — `crAuthSession001`, генератор `Tools/n8n/build-auth-workflows.py`

Четыре правки. Все проверены как патч канона в памяти, приложение A, `m4_patch.py`.

1. **`CLAIM_START_SQL`.** Удалённая строка не получает `pending`. В обоих `case` добавляется первая ветка:
   `when bot_profiles.deleted_at is not null then bot_profiles.claim_pending_by` (и `…claim_pending_at`).
   В `returning` добавляется `(deleted_at is not null) as retired, deleted_reason`. Комментарий — без `$`+цифра (правило V1).
2. **`Needs Status?`.** Новое выражение: `={{ $('Route').first().json.op !== 'claim.verify' && $json.retired !== true }}`.
   Удалённый профиль у Wappi не спрашиваем: спрашивать нечего, и это лишний вызов общим токеном.
3. **`CLAIM_DECIDE_JS`.** Сразу после ветки `db_error` и **до** `owned` и `isVerify`:
   ```js
   else if (row.retired === true) {                     // M4: a retired profile has nothing to claim — never 'owned'
     out.statusCode = 410; out.claimError = 'profile_retired';
     if (row.owner_id === inst.install_id) out.retiredReason = row.deleted_reason || null;   // the owner learns why
   }
   ```
4. **`ISSUE_TOKEN_JS`.** Ветка `claimError` передаёт `retiredReason`, если он есть. Токен в этом ответе не выдаётся, как и в
   любом отказе.

Контракт ответа: `410 {success:false, error:"profile_retired", retiredReason?: "trial_expiry"|"churn_grace"|"liveness"|"app_delete"|"channel_limit"}`.
Причина отдаётся только **владельцу** строки. Чужой установке и для строки без владельца — тот же 410 без причины: факт
удаления id, который запрашивающий уже знает, ничего не раскрывает.

Что сохраняется:
- `claim.verify` и `claim.status` по удалённой строке получают тот же 410;
- `Claim Verify?` требует `!claimError`, поэтому `Verify Claim Code` не выполняется — инвариант V2 «owner_id пишет только
  `Verify Claim Code`» и его счётчик попыток не затронуты;
- `pending` на удалённой строке больше не берётся — S15 (захват только через `claim.start`) не меняется;
- миграций нет: `deleted_at` и `deleted_reason` уже есть;
- `crWappiProxy0001` не меняется (генератор перезапишет его байт-в-байт тем же).

Сознательно не трогаем агрегат `c` в `VERIFY_INSTALL_SQL` / `RELOAD_OWNERSHIP_SQL` (горячий путь refresh): после фикса
удалённая строка больше не получает `pending`, а `pending`, взятый до выкладки, живёт не больше суток и ничего не открывает
(`Record Claim Pairing` требует `deleted_at is null`). Добавить туда `and bp.deleted_at is null` можно, но это отдельная
необязательная гигиена.

Старые A1-клиенты (dev-сборки) с новым сервером: `profile_retired` не входит в `usable` → `finished(false)` → цикл
останавливается. То есть сервер можно выложить **первым**: это строгое улучшение (1 запрос за запуск вместо бесконечного цикла).
Клиент 1.0 (4), собранный до A1, `AuthSession` не вызывает и не затронут.

### 2.2 Клиент

1. **`ProfileClaimPolicy`** (чистый слой, EditMode):
   - `public const string ErrorRetired = "profile_retired"; public static bool IsRetired(string error)`;
   - `RetiredMarkerApplies(string markerId, string profileId, TokenClaims claims)`: истина только если маркер записан для
     **этого же** id и токен его **не** перечисляет. Если токен перечисляет — побеждает токен (самовосстановление, если
     строку когда-нибудь «оживят», см. F2);
   - небольшой чистый `ProfileClaimRun` (или параметр `skip`) с набором «спрошено в этом прогоне». Это структурная защита от
     любого будущего ответа «да, но токен не выдал»: не больше одного `claim.start` на профиль за прогон, цикл ограничен
     числом подключённых профилей.
2. **`ProfileClaimFlow`**:
   - `RunAtBoot`: id добавляется в `asked` **до** `ClaimOne`; `NextUnowned` пропускает `Snoozed ∪ asked ∪ помеченные`.
     `ConnectedProfiles` берёт ещё ключ бота (`bot.name`), чтобы прочитать маркер.
   - `ClaimOne`: при `start?.Error == "profile_retired"` вызывает `ProfileRetirement.Mark(botKey, channel, profileId, retiredReason)`,
     обновляет карточку и вызывает `finished(true)`. **Идёт к следующему боту**, а не останавливается.
   - При `Owned` без id в токене (`!auth.Owns(c.ProfileId)`): `Debug.LogWarning` (аномалия сервера). Повтора не будет за счёт `asked`.
   - `AuthClient.Claim` сейчас не читает `retiredReason`: в `ClaimResult` добавить поле `RetiredReason` и разбирать его.
3. **Маркер** (`ProfileRetirement`, новый маленький файл в `Assets/Scripts/Auth/`). Ключи PlayerPrefs по правилам
   bot-persistence (префикс — `transform.name`):
   - `{bot}WhatsappRetiredProfileId`, `{bot}WhatsappRetiredReason`;
   - `{bot}TelegramRetiredProfileId`, `{bot}TelegramRetiredReason`.

   Удаляются в `Bot.DeleteBotLocally`; полная очистка — через `DeleteAll`. Id профиля и workflow у бота **не трогаются**: на них
   стоит удаление по V12 (см. §2.4).
4. **Честное состояние — минимум** (вопрос 1 к владельцу). В подстроке карточки (`BotCardModel.SublineText`) — короткий текст
   по причине, иконка удалённого канала — `Muted`. Модального окна нет, обещания «подключите заново» нет, пока не готов F2.
   Тексты — в чистый слой копий (`ProfileClaimCopy` или `ProfileRetirementCopy`), регистрация в `FontGlyphCoverageTests`.
   Только символы из статических атласов SF Pro (· « » — есть; № − ✕ → — нет).

### 2.3 Отвергнутые варианты

- **Ответ `owned` с флагом `retired`.** Старые A1-клиенты продолжили бы цикл: флаг они не читают, а `owned` у них —
  «продолжай».
- **Список удалённых id `r` в токене** (`VERIFY_INSTALL_SQL`, `RELOAD_OWNERSHIP_SQL`, `Issue Token`, `Compose Add Response`,
  `InstallSession.TryParse`). Клиент узнавал бы об удалении при каждом refresh, без `claim.start`. Но это смена формата токена и
  трёх агрегатов на горячем пути refresh (R11/V3), а также рост токена. Для M4 избыточно: каждый удалённый профиль и так ровно
  один раз попадает в `NextUnowned`, потому что его нет в `p`. Вариант стоит держать в уме для F2, если ему понадобится
  обнаружение «на лету».
- **Сбросить id канала в `-1` при `profile_retired`.** Настройки сразу пошли бы по «свежему» пути, и F2 был бы наполовину
  решён. Но удаление по V12 потеряло бы id: `HasServerTrace` = false → `NothingOnServer` → бот уходит с телефона, а
  память, история и клон на сервере остаются. Ровно та беда, которую закрыл V12.
- **Удалить бота локально при `profile_retired`.** Противоречит решению владельца по V12: бот уходит с телефона только по
  действию владельца. Кроме того, бизнес-данные бота (каталог, промпт) существуют только на телефоне.
- **Только «snooze» на клиенте.** Лишний запрос на каждом запуске, и нет причины для честного состояния.

### 2.4 Согласованность с V12 и F2

- **V12.** Удаление «зомби» владельцем работает как сейчас:
  1. `profile.delete` через прокси (`Profile Owner` смотрит в БД, своя удалённая строка допускается);
  2. `DeleteBotFiles` и `SetWorkflowState(delete)` (`Delete Ownership` — своя строка, удалённая или нет);
  3. id для этого нужны — маркер их не трогает.

  `profile_retired` в claim и вердикт удаления — разные эндпоинты, разные правила, конфликта нет. Добавить в e2e проверку
  «после `410 profile_retired` удаление этого же бота — 200» (§3).
- **F2** (переподключение после покупки — не решаем, но и не мешаем). Маркер «какой канал, какой id, почему» — готовый вход
  для F2. Естественная точка для F2 — `BotSettings.Auth.CheckWhatsappAuthorization` и её Telegram-двойник: сегодня для id ≠
  `-1` они идут в ветку «существующий профиль» → `status` → 403, это и есть тупик F2. Для помеченного канала они могли бы идти
  по «свежему» пути (новый профиль, проверка `CanConnectChannel`).
  - Новый профиль получит **новый** id, маркер перестанет совпадать сам. Чистить его можно при успешном Create.
  - Если F2 решит «оживлять» строку (`deleted_at = null`), маркер уступит токену (`RetiredMarkerApplies`).
  - Ограничение, которое F2 должен учесть: заменив удалённый id новым, приложение теряет id, по которым V12 удаляет старую
    память, историю и клон. Значит, F2 должен либо сначала удалить серверные следы старого id (`DeleteBotFiles`), либо хранить
    прежние id (это пересекается с F1 — лишние клоны).
- **V2/V3/R1.** Ничего из привязки по коду не меняется: удалённая строка просто выходит из оборота claim.

---

## 3. Тесты (до → после)

**Сервер**

- `Tools/n8n/tests/test_claim.py` — новые кейсы:
  - **S17** «M4: `claim.start`, `claim.status` и `claim.verify` по СВОЕЙ удалённой строке (`trial_expiry`, `churn_grace`,
    `liveness`, `app_delete`) → 410 `profile_retired` + `retiredReason`; Wappi `get/status` не вызван; `claim_pending_*` не
    записан; код не отправлен; `Verify Claim Code` не выполнен».
    На HEAD: `200 owned`, `status_called=True` — FAIL.
  - **S18** «M4: удалённая строка без владельца или другой установки → 410 без причины; `c` токена её не содержит; при
    `authorized:true` от Wappi (ложный liveness) код в чат не уходит, `owner_id` не пишется».
    На HEAD: `reauth`, `pending` записан, `c=['R-wa']`; для authorized — код отправлен и `owner_id` записан на удалённую строку — FAIL.
  - **S19** — инвариант, который M4 нарушал: «любой ответ `claimMethod:'owned'` выдаёт токен, в `p` которого есть этот
    профиль». Матрица состояний строки: своя живая / своя удалённая ×4 причины / чужая живая / чужая удалённая / без владельца
    живая / без владельца удалённая.
    На HEAD падает на «своя удалённая».
  - S1–S16 остаются зелёными (проверено патчем, приложение B.3).
  - В блок «V2 negative» добавить мутацию «убрать ветку `retired`» → верификатор должен упасть.
- `Tools/n8n/verify-auth-gate.py`:
  - закрепление `Needs Status?` обновить на новое выражение (смысл «`claim.verify` никогда не зовёт Wappi» сохраняется);
  - новые закрепления: `Claim Start` возвращает `retired`; в `Claim Decide` ветка `row.retired === true` стоит раньше `owned`.
- `Tools/n8n/verify-sql-comments.py` — без изменений (уже OK на патче).
- `Tools/n8n/staging/e2e-auth.py` — сценарий `claim_on_retired_profile_is_refused` (WA+TG):
  1. профиль создаётся через прокси;
  2. строка удаляется SQL'ом как чисткой (`deleted_at`, `deleted_reason='trial_expiry'`);
  3. `claim.start` → 410 `profile_retired` + `retiredReason`, мок **не** получил `get/status` по этому профилю;
  4. затем удаление того же бота (`DeleteBotFiles`, `SetWorkflowState delete`) — 200 (согласованность с V12).

  Запуск — исполнителем, на staging, когда стенд свободен.

**Клиент (EditMode)**

- `Assets/Tests/Editor/Auth/ProfileClaimPolicyTests.cs`:
  - `IsRetired_RecognisesProfileRetired`;
  - `NextUnowned_SkipsARetiredMarkedProfile_AndReachesTheNextBot`;
  - `RetiredMarker_AppliesOnlyToTheSameId_AndYieldsToTheToken`;
  - **`ClaimRun_AsksEachProfileAtMostOnce_WhenTheServerSaysOwnedButNeverGrantsIt`** — регрессионный тест M4 на чистом
    `ProfileClaimRun`. На HEAD такого слоя нет; поведение «до» зафиксировано зеркалом в приложении B.1.
- `Assets/Tests/Editor/Auth/ProfileClaimWiringTests.cs` (защита по исходнику):
  - `RunAtBoot_NeverAsksTheSameProfileTwiceInOneRun`;
  - `ClaimOne_OnProfileRetired_MarksTheChannelAndMovesOn` (`IsRetired(` → `ProfileRetirement.Mark(` → `finished(true)`);
  - `AuthClient_Claim_ReadsRetiredReason`.
- `Assets/Tests/Editor/Chat/BotDeletionWiringTests.cs` (или рядом): `DeleteBotLocally` удаляет четыре новых ключа.
- `Assets/Tests/Editor/Chat/BotCardModelTests.cs`: подстрока и иконка удалённого канала.
- `Assets/Tests/Editor/FontGlyphCoverageTests.cs`: регистрация нового слоя копий и его форм по причинам.

---

## 4. Файлы

- **Сервер:**
  - `Tools/n8n/build-auth-workflows.py` → перегенерировать `Tools/n8n/workflows/crAuthSession001-Auth_Session.json`
    (`--check` после);
  - `Tools/n8n/verify-auth-gate.py`;
  - `Tools/n8n/tests/test_claim.py`;
  - `Tools/n8n/staging/e2e-auth.py`.
- **Клиент:**
  - `Assets/Scripts/Auth/ProfileClaimPolicy.cs`, `ProfileClaimFlow.cs`, `AuthClient.cs` (`ClaimResult.RetiredReason`);
  - новый `Assets/Scripts/Auth/ProfileRetirement.cs` (+ `.meta`);
  - `ProfileClaimCopy.cs` или новый слой копий;
  - `Assets/Scripts/Main/Bot.cs` (`DeleteBotLocally`, подстрока), `Assets/Scripts/Main/BotCardModel.cs`;
  - тесты из §3.
- **Документация:**
  - CLAUDE.md, раздел «A1 identity + transport» — правило: «claim никогда не отвечает `owned` на удалённую строку;
    `profile_retired` (410); клиент — один `claim.start` на профиль за прогон и маркер канала»;
  - `.claude/skills/bot-persistence/references/key-catalog.md` — новые ключи;
  - строка в пакете выкладки.

## 5. Выкладка

- Затрагивается **один** workflow: `crAuthSession001` (Auth Session). Обновление существующего — `Tools/n8n/deploy-canonical.py`
  (PUT по id, затем read-back), сверка `verify-auth-gate.py` / `verify-deployed.py`.
- SQL-миграций нет, env не меняется, прокси и Create не трогаются.
- Порядок:
  1. сервер — можно отдельно и раньше: старые A1-сборки сразу перестают крутиться;
  2. клиент — в A1-сборке до подачи в сторы, вместе с device-pass: бот с истёкшим триалом → запуск → один запрос →
     подстрока «отключён» → удаление бота работает.
- Откат — прежний JSON `crAuthSession001`. Состояние не мигрирует: маркеры клиента при откате сервера просто перестанут
  появляться у новых «зомби».

## 6. Объём

| Часть | Оценка |
|---|---|
| Сервер: генератор, канон, верификатор, S17–S19, негатив, `run_all` | 2–3 ч |
| e2e-auth сценарий + прогон на staging | 1 ч |
| Клиент: политика, `ProfileClaimRun`, flow, маркер, `AuthClient`, удаление ключей | 2–3 ч |
| Клиент: подстрока карточки + копии + тесты (EditMode через headless/bridge) | 1,5–2 ч |
| Документация (CLAUDE.md, key-catalog, пакет) | 0,5 ч |
| **Итого** | **≈ 7–9,5 ч** (около 1–1,5 дня), из них серверная половина, которая одна гасит цикл у всех клиентов, — **≈ 3–4 ч**. Device-pass — отдельно, в общем проходе A1-сборки |

## 7. Вопросы владельцу

1. **Что показывать у бота с удалённым каналом?**
   - (а) **рекомендую:** только подстрока карточки, без окна и без обещания «подключите заново» до F2;
   - (б) разовое окно при обнаружении («Бот «X» отключён: пробный период закончился. Данные бота сохранены на телефоне»),
     с кнопкой «Тарифы» (откроет paywall);
   - (в) ничего не показывать, только перестать спрашивать — бот выглядит живым, а чаты застыли; не рекомендую.
2. **Тексты по причинам.** Предлагаю (все символы есть в атласах):
   - `trial_expiry`: «Отключён · пробный период закончился»;
   - `churn_grace`: «Отключён · подписка закончилась»;
   - `liveness` / `channel_limit` / без причины: «Канал отключён»;
   - `app_delete`: «Удаление не завершено · удалите ещё раз».
3. **Чаты удалённого канала сейчас или с F2?** Пустое состояние «Бот отключён» вместо застывшего кеша и остановка 403-вызовов
   `chats.filter`/SetReplyMode (очередь режима переотправляет «неудавшееся» желание на каждом запуске и снова получает 403).
   Рекомендую с F2: это один экран с переподключением. Если F2 не скоро — взять в M4 как +1–2 ч.
4. **410 для чужой или бесхозной удалённой строки.** Рекомендую отвечать одинаково (`profile_retired`) без причины. Альтернатива —
   общий отказ, но тогда legacy-клиент получит лист «подключите заново», а это тупик (B1).
5. **Выкладывать серверную половину отдельно и раньше клиента?** Рекомендую да: она безопасна для всех существующих сборок.
6. **(Необязательно, только чтение прода, по вашему «да».)** Посчитать исполнения `AuthSession` с `claim.start` по профилю
   `9ab38322…` за 2026-09-29…10-06. Это покажет, крутил ли ваш телефон цикл, пока «23» был «зомби», и даст реальную цену круга.

---

## Приложение A. Код воспроизведения

Файлы лежали в scratchpad сессии; ключевые части приведены здесь, чтобы повтор не зависел от scratchpad.
Запуск: `python3 m4_repro.py [--patched]` и `python3 m4_client_fix.py [--patched]` (из папки со скриптами, при запущенном
`Tools/n8n/staging/pg.sh`).

`m4_patch.py` — предлагаемый серверный фикс, применяется к копии канона в памяти:

```python
def patch_session(w):
    w = copy.deepcopy(w)
    cs = _node(w, 'Claim Start')['parameters']; q = cs['query']
    q = _sub(q, "  claim_pending_by = case\n      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_by",
             "  claim_pending_by = case\n      when bot_profiles.deleted_at is not null then bot_profiles.claim_pending_by\n"
             "      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_by")
    q = _sub(q, "  claim_pending_at = case\n      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_at",
             "  claim_pending_at = case\n      when bot_profiles.deleted_at is not null then bot_profiles.claim_pending_at\n"
             "      when bot_profiles.owner_id = $2 then bot_profiles.claim_pending_at")
    q = _sub(q, "as code_locked;", "as code_locked,\n  (deleted_at is not null) as retired, deleted_reason;")
    cs['query'] = q
    _node(w, 'Needs Status?')['parameters']['conditions']['conditions'][0]['leftValue'] = \
        "={{ $('Route').first().json.op !== 'claim.verify' && $json.retired !== true }}"
    cd = _node(w, 'Claim Decide')['parameters']
    cd['jsCode'] = _sub(cd['jsCode'],
        "else if (row.error || !row.profile_id) { out.statusCode = 500; out.claimError = 'db_error'; }\n",
        "else if (row.error || !row.profile_id) { out.statusCode = 500; out.claimError = 'db_error'; }\n"
        "else if (row.retired === true) {\n"
        "  out.statusCode = 410; out.claimError = 'profile_retired';\n"
        "  if (row.owner_id === inst.install_id) out.retiredReason = row.deleted_reason || null;\n}\n")
    it = _node(w, 'Issue Token')['parameters']
    it['jsCode'] = _sub(it['jsCode'],
        "  return [{ json: { success: false, error: claimInfo.claimError, _status: claimInfo.statusCode || 400 }, pairedItem: { item: 0 } }];",
        "  const refusal = { success: false, error: claimInfo.claimError, _status: claimInfo.statusCode || 400 };\n"
        "  if (claimInfo.retiredReason) refusal.retiredReason = claimInfo.retiredReason;\n"
        "  return [{ json: refusal, pairedItem: { item: 0 } }];")
    return w
```

`m4_repro.py` — проход одного `claim.*` по канону (копия `test_claim.claim()`) и клиент HEAD, отзеркаленный 1:1:

```python
def next_unowned(connected, token_p, snoozed):                 # ProfileClaimPolicy.NextUnowned
    for c in connected:
        if c['pid'] in ('', '-1') or c['pid'] in snoozed or c['pid'] in token_p: continue
        return c

def claim_one(pg, c, snoozed, wappi):                           # ProfileClaimFlow.ClaimOne (HEAD)
    r = claim(pg, 'claim.start', 'inst-O', c['pid'], wappi[c['pid']], channel=c['ch']); i = r['issued']
    usable = i.get('success') or i.get('error') in ('code_send_failed', 'claim_code_locked')
    if not usable: return False, r, 'stop'
    if i.get('success') and i.get('claimMethod') == 'owned': return True, r, 'owned -> finished(true) -> next loop turn'
    snoozed.add(c['pid']); return False, r, 'sheet -> snoozed, stop'

def run_at_boot(pg, connected, wappi, max_turns=6):             # ProfileClaimFlow.RunAtBoot
    token_p, snoozed, turns = [], set(), 0
    while turns < max_turns:                                    # the real loop is while(true); capped for the demo
        nxt = next_unowned(connected, token_p, snoozed)
        if nxt is None: break
        turns += 1
        done, r, verdict = claim_one(pg, nxt, snoozed, wappi)
        token_p = r['reload'].get('p') or []                    # AuthClient.Claim adopts the response token
        if not done: break
```

Строки в temp-таблицах:
- `Z-wa`: `owner_id='inst-O'`, `deleted_at = now()-2d`, `deleted_reason ∈ {trial_expiry, churn_grace, liveness, app_delete}`;
- `L-tg`: legacy без владельца;
- `R-wa`: удалённая строка без владельца.

Ответы Wappi:
- для удалённого профиля — `{statusCode:200, body:{authorized:false}}` (CLAUDE.md: «status shows unauthorized») либо 404;
- для живого — `authorized:true`.

`m4_client_fix.py` — предлагаемый клиент, отзеркаленный: `asked` + маркер, при `profile_retired` идёт дальше.

## Приложение B. Вывод

### B.1 HEAD-сервер × HEAD-клиент (`m4_repro.py`)

```
=== A[trial_expiry]: own retired WA channel first, a legacy TG bot second
  turn 1..6: Z-wa: http 200 success=True claimMethod=owned error=None retiredReason=None | Wappi get/status called=True code sent=False | token p=[] c=[] | row rewritten=True pending_by=None | client: owned -> finished(true) -> next loop turn
=== A[churn_grace] / A[liveness] / A[app_delete]: идентично, 6 из 6 ходов по Z-wa, L-tg не достигнут
=== A2: same, Wappi answers 404 for the deleted profile
  turn 1..3: Z-wa: http 200 success=True claimMethod=owned ... Wappi get/status called=True ... client: owned -> finished(true) -> next loop turn
=== B1: retired OWNER-LESS row, Wappi says unauthorized
  turn 1: R-wa: http 200 success=True claimMethod=reauth | Wappi get/status called=True code sent=False | token p=[] c=['R-wa'] | row rewritten=True pending_by=inst-O | client: reauth sheet -> snoozed, stop
B1 HEAD: proxy qr Build Request ok = True claimPairing = True | Record Claim Pairing rows = [] -> Pairing Recorded? = False (403 claim_not_pending)
=== B2: retired owner-less row whose Wappi profile is in fact still authorised (liveness false positive)
  claim.start: 200 claimMethod=code error=None code sent into the chat=True pending_by=inst-O
  claim.verify with that code: success=True -> owner_id=inst-O retired=True token p=[]
```

### B.2 Фикс сервера (в памяти) × HEAD-клиент (`m4_repro.py --patched`)

```
=== A[trial_expiry|churn_grace|liveness|app_delete], A2 (404):
  turn 1: Z-wa: http 410 success=False error=profile_retired retiredReason=<причина> | Wappi get/status called=False code sent=False | token p=[] c=[] | pending_by=None | client: stop: claim.start failed (profile_retired)
=== B1: turn 1: R-wa: http 410 error=profile_retired retiredReason=None | Wappi get/status called=False | c=[] | pending_by=None | client: stop
=== B2: claim.start: 410 error=profile_retired code sent into the chat=False pending_by=None
```

Клиент из обеих колонок (`m4_client_fix.py`):

```
server canon: HEAD | client: proposed fix (mirrored)
  launch 1: turn 1: Z-wa: http 200 owned | Wappi status=True | client: owned BUT not in the token -> asked-set stops a repeat
            turn 2: L-tg: http 200 code | Wappi status=True | client: CODE SHEET shown for this bot (owner presses «Позже») -> snoozed, stop
  launch 2: (то же самое — Z-wa спрашивается каждый запуск)
server canon: PATCHED | client: proposed fix (mirrored)
  launch 1: turn 1: Z-wa: http 410 profile_retired | Wappi status=False | client: retired (trial_expiry) -> marker saved, finished(true) -> next bot
            turn 2: L-tg: http 200 code | Wappi status=True | client: CODE SHEET shown for this bot (owner presses «Позже») -> snoozed, stop
  launch 2: turn 1: L-tg: http 200 code | ... (Z-wa больше не спрашивается)
```

### B.3 Регрессия: закоммиченный `test_claim.py` на пропатченном каноне

```
PASS R1 S1 … PASS V2 S16 (все 15 сценариев S1–S5, S7–S16)
PASS V2 negative: verify-auth-gate.py rejects an Auth Session that binds on a pairing again, or lets claim.verify call Wappi
16 pass, 0 fail
```

Как прогонялось: `harness.WF` указывал на временную копию `Tools/n8n/workflows/`, в которой Auth Session заменён выводом
`m4_patch.patch_session`. Сам закоммиченный `test_claim.py` не менялся. S14 проверяет `Needs Status?` для `claim.start` с пустым
`$json`: `undefined !== true` → true, значит, Wappi по-прежнему опрашивается для живых строк.

Оговорка: блок «V2 negative» гоняет `verify-auth-gate.py` по закоммиченному канону, а не по пропатченному. Пропатченный канон
отдельным прогоном `verify-auth-gate.py --dir <копия>` падает ровно на закреплённом выражении `Needs Status?`
(«AUTH GATE FAIL: V2: claim.verify must not call Wappi»): это ожидаемая правка закрепления из §3. Проверка
`verify-sql-comments.py` на пропатченном Auth Session: «OK 7 Postgres node(s) … no placeholder outside code».
