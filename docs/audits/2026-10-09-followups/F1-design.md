# F1 — потерянные клоны: повторная авторизация и сбой после Create. Подтверждение и дизайн фикса

Дата: 2026-10-09. HEAD `d08c345d`. Статус: **подтверждены обе ветки, не исправлено** (это проектирование, код репозитория
не менялся). Источник: `docs/audits/2026-09-28-iteration/README.md` («Новые находки вне списка ревьюера», F1). Происхождение:
`verification-results.json` → `refute[R8-deletefail]` (`missed_case`, `better_design` п. 2). В CLAUDE.md (раздел A2, абзац
R8-deletefail) F1 записана как открытая.

Где это сейчас. Create WA/TG в таком виде работают на проде с 2026-09-24 (A1 + R8) и входят в пакет 2026-10-09. A1-клиент
в сторы не выпущен. Одобренный iOS 1.0 (4) собран до A1 (ресабмит 2026-09-17 ~00:50), значит, работает в compat-режиме
`legacy`. Для него F1 тоже живая: ветки ниже от режима не зависят, кроме мест, где это оговорено.

---

## 0. Коротко

- **(а) подтверждена в трёх формах, (б) — в четырёх**, на WA и на TG. Поведенческое воспроизведение: 14 из 14 фактов (§1.3).
- **Клиент (а) не закрывает.** Прежний клон удаляется только в «Сменить номер», и только когда прошли и logout, и удаление.
  Во всех остальных входах прежний id просто перезаписывается.
- **Попутно найдено.**
  - После любой замены клона бот перестаёт видеть загруженные прайс-листы: чанки остаются под старым id. При удалении бота
    они тоже не удаляются.
  - Сбой `Bind Workflow Id` всё равно отдаёт клиенту id непривязанного клона.
  - В CLAUDE.md неверен инвариант про повторную отправку кода («workflow нет, пока панель кода открыта»): на пути (а) это не так.
- **Суть фикса.** Create становится операцией «обеспечить клон профиля».
  1. Тот же профиль — переиспользовать привязанный клон, новый не создавать.
  2. Новый профиль того же бота — создать клон, привязать с положительным признаком, перенести прайсы, удалить прежний клон.
  3. Любой сбой после Create — удалить новый клон с проверкой статуса и ответить без id.
  4. Всё, что не удалось удалить сразу, записывается в журнал `clone_retirements` в той же транзакции, что и привязка.
     Журнал добирает плановая чистка.

  Узлов Postgres на горячем пути столько же, сколько сейчас.
- **Объём.** Tier 1 (обязательный) ≈ 23–24 ч. Tier 2 (инвентаризация для умерших исполнений и старых сирот) — ещё 6–8 ч.
  Клиентский follow-up — ещё 3–4 ч и device-pass.

---

## 1. Подтверждение

### 1.1 Клиент: кто вызывает Create и удаляет ли он прежний клон

Из настроек Create вызывает только `Create{Whatsapp,Telegram}WorkflowFromEdit` (`Assets/Scripts/Main/Manager.cs:3663` и
TG-близнец). У него единственный вызывающий: `On{Whatsapp,Telegram}AuthFromSettingsDone`
(`Assets/Scripts/Main/BotSettings.Auth.cs:176–190`, `363–375`). Тот срабатывает, когда общий экран авторизации сообщил
об успехе; для claim-pending профиля — только после верного кода (`Manager.cs:2750`). Ответ с `"id":` записывается в бота
поверх прежнего id (`Manager.cs:3737–3741`). Прежний id никуда не отправляется и нигде не запоминается.

Откуда приходит экран авторизации и что к этому моменту с прежним клоном:

| Вход | Профиль в Create | Прежний клон | Форма |
|---|---|---|---|
| Тумблер канала ВКЛ, профиль есть, `get/status` → `authorized:false` → `ShowWhatsappAuthFromSettings(bot.whatsappProfileId)` (`BotSettings.Auth.cs:155–161`; TG `:347`) | тот же P1 | не трогается | (а1) или (а2) |
| └ **TG, детерминированно:** канал сохранён ВЫКЛ, сессия Telegram закончилась. Фоновая проверка `CheckTelegramUnauthorizationOutsideApp` удаляет профиль только при `isOnTelegram == 1` (`:424`), поэтому профиль остаётся | P1 | неактивен | (а1) |
| └ **WA:** сессия слетела после открытия экрана, или фоновая проверка (`BotSettings.cs:338`) не успела или не дошла | P1 | активен, если канал ВКЛ | (а2) или (а1) |
| Фоновая проверка WA (TG — при `isOnTelegram==1`) видит неавторизованный профиль → `GetDeleteWhatsappProfile` (`:250`) → `DeleteWhatsappProfile` (`Manager.cs:3468`). Сбрасывается в `-1` **только id профиля**, `whatsappWorkflowId` остаётся. Потом тумблер ВКЛ → свежий профиль (`:80–110`) | новый P2 | **активен** (его никто не выключал), слушает мёртвый путь P1 | (а3) |
| «Назад» с экрана авторизации → `GetDeleteWhatsappProfile` (`:208`), затем снова включение | P2 | без изменений | (а3) |
| «Получить код ещё раз» на пути (а1)/(а2): `RecreateWhatsappProfileForNewCode` (`Manager.cs:2623`) удаляет P1, создаёт P2 и переписывает id бота | P2 | как был | (а3) |
| «Сменить номер» (`ConfirmChangeWhatsappNumber`, `:54`): экран авторизации открывается **сразу**. Параллельно `UnauthorizeWhatsapp` (`:256`) делает logout и только при успехе запускает fire-and-forget `GetDeleteWhatsappWorkflow` (`:266`). Тот сбрасывает id в `-1` только при успехе (`Manager.cs:4084`) | P1 | удаляется, если прошли оба вызова; иначе остаётся активным | ок / (а2) |
| Мастер (`FromStart`) | новый | нет | только (б) |

**Вывод:** (а) клиент закрывает только в «Сменить номер» и только при двух успешных вызовах. Но и там чанки прайсов
удалённого клона остаются под его id (§1.4).

### 1.2 Сервер: цепочка Create на HEAD (WA и TG устроены одинаково)

`Unity Webhook → Auth Gate → Authorized? → Vertical Prompt → If Has AppUserID?`, дальше две ветки сходятся в `Seed Reply Mode`:
- сессия: `Ensure Subscriber → Count Channels → Compute Slot Limit → If Slot Limit[1] → Retire Same Bot Slot → Register Profile`;
- legacy без AppUserID: сразу в `Seed Reply Mode`.

Затем: `Seed Reply Mode → Seed Saved?[0] → Get Sample Workflow → Set Fields → Create Workflow → Get Created Workflow Id →
Activate Created Workflow → Set Wappi Webhook → Set Wappi Webhook Types → Restamp RAG Chunks → Bind Workflow Id →
Send New Workflows Id`. Ответ — `responseMode: lastNode`.

1. **Ни один узел не проверяет, привязан ли к профилю клон.** `Bind Workflow Id` делает upsert
   `on conflict (profile_id) do update set workflow_id = excluded.workflow_id` (`apply-auth-gate.py`, `BIND_WORKFLOW_SQL`).
   Прежний id молча исчезает из `bot_profiles`.
2. **Id, который не называет ни одна строка, недостижим.** Токен (`p`/`w`) строится из живых строк (`Reload Ownership`,
   `Verify Install`). V12 `Delete Ownership` разрешает удалить workflow, только если его называет строка этой установки.
   Такой id нельзя изменить (Edit gate → 403) и нельзя удалить (Set Workflow State, Delete Bot Files → `unproven`).
3. **Путь вебхука клона = id профиля** (`Set Fields`: `nodes[0].parameters.path`). Два активных клона одного профиля
   невозможны: n8n 2.36.7 бросает `WebhookPathTakenError` (`n8n/dist/webhooks/webhook.service.js:140`,
   `active-workflow-manager.js:157`).
4. **У узлов после Create нет обработки ошибок.** `Get Sample`, `Create Workflow`, `Activate Created Workflow`,
   `Set Wappi Webhook`, `Set Wappi Webhook Types` идут без `onError`. Сбой любого из них после Create обрывает исполнение
   (lastNode → 500), компенсации нет. `Get Sample`, `Create` и `Activate` ходят на публичный `https://n8n.choosereply.com`:
   DNS и Caddy — та же точка отказа, которую разобрал опровергатель R8.
5. **`Bind Workflow Id` не даёт положительного признака.** У него `continueRegularOutput` + `alwaysOutputData`, и при сбое БД
   `Send New Workflows Id` всё равно отвечает `{id}`. Урок R8: «узел продолжил» не значит «записал».
6. **`Restamp RAG Chunks` переносит на новый клон только чанки с `botWaId = '-1'`** — случай, когда второй канал подключается
   к TG-боту. Чанки прежнего клона он не трогает.
7. **Строку P1 ретирует только `Retire Same Bot Slot`.** Прокси на `profile.delete` строку `bot_profiles` не ретирует
   (`Profile Owner` только проверяет владельца). `Retire Same Bot Slot` ищет по `app_user_id` и ставит `deleted_reason` = NULL.
   V12 `Retired Clones` (чистка) берёт только `trial_expiry/churn_grace`, такую строку она не видит.

### 1.3 Поведенческое воспроизведение (локально, без n8n и мока)

**Как проверял.**
1. Канонические графы Create WA/TG прогнаны через расширенную копию `Tools/n8n/tests/create_seed_chain.js`. Добавлено:
   - сбои HTTP-узлов по семантике n8n-core: без `onError` исполнение обрывается, при `continueRegularOutput` выходит элемент
     `{error}`;
   - каждый Postgres-узел записывает вычисленный `queryReplacement`.
2. Настоящий SQL каждого пройденного Postgres-узла выполнен по порядку на temp-таблицах staging-Postgres (`harness.PgSession`).
3. Затем выполнены настоящие читающие запросы: `Reload Ownership` (токен), V12 `Delete Ownership`, V12 `Retired Clones`,
   `Delete Bot Chunks` (будущее удаление бота).

n8n staging (localhost:5678) и мок не запускались. Расширенный драйвер обратно совместим: на нём `test_create_seed.py`
проходит 52/52. Скрипты — в Приложении, потому что scratchpad стирается при рестарте.

**Итог: 14 из 14 (по 7 на WA и на TG).** Кейсы составлены так, что проходят, когда дефект есть.

| Сценарий | Ответ клиенту | Что стало с клонами | Кто может достать клон после |
|---|---|---|---|
| (а1) тот же профиль, прежний клон неактивен | `{id: WFnew}` | ни один вызов не тронул WFold | ни одна строка не называет WFold; `w` = [WFnew]; `Delete Ownership(WFold)` до прогона `ok`, после — `unproven`; 2 чанка под WFold, 0 под WFnew |
| (а2) тот же профиль, прежний клон активен; 3 попытки | 500 ×3, обрыв на `Activate Created Workflow` | 3 новых непривязанных клона | каждая повторная попытка добавляет ещё один |
| (а3) новый профиль P2 того же бота | `{id: WFnew}` | WFold остаётся активным, путь P1 мёртв | строка P1 ретирована с `deleted_reason` = NULL, V12 её не берёт; WFold вне `w`. `Delete Ownership(WFold)` = `ok`, но id не знает никто: клиент его перезаписал, удаление бота шлёт P2/WFnew. `Delete Bot Chunks(WFnew)` удаляет 0 чанков, 2 остаются |
| (б) упал `Activate` (EAI_AGAIN) | 500 | новый клон неактивен и не привязан | `unproven` для всех |
| (б) упал `Set Wappi Webhook` или `Types` | 500 | новый клон **активен** и не привязан | `unproven`; на пути P он блокирует любую следующую активацию |
| (б) ошибка запроса в `Bind Workflow Id` | **`{id: WFnew}`** | активен и не привязан | у клиента есть id, но управлять им он не может: Edit → 403, удаление → `unproven` |

### 1.4 Последствия

- **Данные владельца в потерянном клоне.** Business, Prompt, Products и Services из systemMessage живут в клоне, который никто
  не найдёт. Удаление бота его не удалит — это нарушает обещание V12 «удалить данные по запросу». Исполнения клона, а в них
  тексты клиентов, хранятся вместе с ним.
- **(а2) и активная сирота из (б): канал «не сохраняется».** Каждая повторная авторизация падает на `WebhookPathTakenError`,
  пока клон-сироту не уберут руками.
- **Прайс-листы.** После любой замены клона бот перестаёт видеть загруженные прайсы. Это (а1), (а3) и даже «Сменить номер»
  с успешным удалением. Причина: поиск фильтрует по `botWaId/botTgId = $workflow.id`, а чанки лежат под прежним id.
  `Delete Bot Files` и `List Bot Originals` ищут по id, который пришлёт клиент, поэтому чанки и оригиналы в бакете переживают
  удаление бота. Это не косметика F1, а функциональный дефект той же природы, и фикс обязан его закрыть.
- **CLAUDE.md, раздел Wappi `auth/code`.** Там написано: «safe … because no n8n workflow exists while the code panel is
  reachable». На путях (а1)/(а2) панель кода открыта при живом клоне, и повторная отправка кода превращает их в (а3).
  После фикса это безопасно (§2.2), формулировку нужно поправить.

---

## 2. Дизайн фикса

### 2.1 Принципы

- **Источник истины — `bot_profiles.workflow_id` профиля.** Create = «обеспечить ровно один привязанный клон профиля»,
  идемпотентно.
- **Решения только по положительному факту (уроки R8 и F3).** Каждый вызов n8n API и Wappi выполняется с
  `fullResponse + neverError + onError: continueRegularOutput`. Следом стоит If на положительный факт: статус 200 или 404,
  `bound === true`. Отказ отвечает без поля `id`: `FromEdit` проверяет только `Contains("\"id\":")` (`Manager.cs:3737`).
- **Ни одного клона без записи.**
  - Заменяемые клоны попадают в журнал `clone_retirements` той же транзакцией, что и привязка нового, — до попытки удаления.
  - Если не удалось удалить новый клон после сбоя, это тоже записывается в журнал.
  - Плановая чистка добирает журнал по образцу V12 `clone_deactivated_id`.
- **Владение решает БД, а не токен (V12).** Что переиспользовать и что заменять, определяет SQL по `owner_id` установки
  (не отозванной), а не 12-часовой список `p`/`w`.
- **Защищённые id не трогаются никогда.** Это `build-client-webhooks.PROTECTED_IDS` (25 шт.), плюс Set Workflow State, плюс
  support relay. Они не переиспользуются и не удаляются. Фильтр стоит и в SQL, и в If-выражении перед вызовом, как `Has Clone?`
  в V12.
- **Цена по времени.** Каждый Postgres-узел стоит ≈ 1,9 с на проде, клиентский таймаут Create — 30 с. Поэтому Postgres-узлов
  на горячем пути не больше, чем сейчас: добавляется `Clone Plan` (+1), а `Restamp RAG Chunks` вливается в `Bind Workflow Id` (−1).
- **Вызовы n8n API — на `http://localhost:5678` с кредом `n8n Admin API`.** В коммите стоит плейсхолдер `__HDR_CRED__`, его
  привязывает `deploy-canonical.py`. Так убирается зависимость от DNS и Caddy (довод опровергателя R8).

### 2.2 Ветка (а)

**Тот же профиль — (а1)/(а2): переиспользовать привязанный клон** (рекомендация, вопрос Q2).
- `Clone Plan` возвращает `own_id` — `workflow_id` строки этого профиля, если привязка принадлежит вызывающему:
  - в сессии — `owner_id` = установка, и установка не отозвана;
  - в legacy — строка без владельца (§2.7).
- `Probe Bound Clone` делает GET клона. Клон годится, если статус 200, `isArchived !== true` и путь узла `Webhook` = профиль.
- Тогда клон активируется (для уже активного это нулевая операция) и Wappi-вебхук ставится заново (идемпотентно).
  Ответ — `{id: own_id}`.
- Новый клон не создаётся. Значит, нет ни конфликта путей, ни сироты, ни переноса чанков, ни вопроса «что делать с прежним».
  Custom-голова промпта (V10) сохраняется.
- Чего нет при переиспользовании: systemMessage не освежается полями формы. Несохранённые правки на экране настроек,
  как и сейчас, ложатся через «Сохранить» (Edit). Сохранённое состояние в клоне уже есть.
- Если клона нет (404) или он в архиве — создаётся новый. Прежняя привязка (`own_id`) идёт в перенос чанков и в журнал
  (`moved`, §2.5). Это лечит и «Сменить номер»: прайсы переезжают на новый клон.
- Любой другой ответ пробы — `{success:false, error:'clone_unavailable'}`, **ничего не создаётся.** Если неизвестно, жив ли
  привязанный клон, новый создавать нельзя: это дубль или конфликт путей.

**Новый профиль того же бота — (а3): создать, привязать, перенести, удалить прежний.**
«Тот же бот» — это строки `bot_profiles` (`replace_ids` в `Clone Plan`), у которых:
- тот же `owner_id` (установка вызывающего, не отозвана);
- тот же канал;
- тот же непустой `bot_key` — имя слота `BotN`, клиент шлёт его как `BotKey`;
- другой профиль.

Исключения:
- `app_delete`: такой бот удалён, его клон удалило приложение. Слот `BotN` переиспользуется новым ботом, и наследовать прайсы
  удалённого бота нельзя.
- id невалидной формы или защищённый;
- id, совпадающий с `own_id`;
- id, который водит другая **живая** строка — не этого бота.

Ретирование по `trial_expiry`, `churn_grace` или `liveness` прежним ботом не мешает. Это F2-переподключение: бот получает
свои прайсы обратно.

В `Bind Workflow Id` (§2.5) одним оператором:
- новый клон привязывается к P2;
- чанки (`documents`) и `uploaded_files` переводятся с `moved` (= `own_id` + `replace_ids`) на новый id;
- строки прежних профилей ретируются: `deleted_reason = coalesce(…, 'replaced')`, а в `superseded_by` пишется новый id;
- прежние клоны записываются в `clone_retirements`.

Только **после** положительного `bound` выполняется `Delete Replaced Clone` (DELETE каждого, статус проверяется). Удаление или
выключение — вопрос Q1. Неудачу добирает чистка (§2.6): строка журнала уже есть.

**Повторная отправка кода, «Назад», фоновая проверка** приводят к той же (а3) и закрываются тем же механизмом.

### 2.3 Ветка (б)

| Сбой | Что делает граф | Ответ |
|---|---|---|
| `Clone Plan` не дал `planned = true` (ошибка запроса, исключение, пустой ответ) | ничего не создаётся: шаблон не читается, Create не вызывается | `clone_plan_unavailable` |
| Проба привязанного клона — не 200/404/архив | ничего не создаётся | `clone_unavailable` |
| `Activate Created Workflow` ≠ 200 (сеть, `WebhookPathTakenError`, 5xx) | новый клон удаляется (`Discard New Clone`) | `create_failed` |
| `Set Wappi Webhook` или `Types` ≠ 200 | новый клон удаляется | `create_failed` |
| `Bind Workflow Id` без `bound = true` после 3 попыток | новый клон удаляется; чанки не переносились (тот же оператор) | `create_failed` |
| `Discard New Clone` ≠ 200/404 | `Record Discard Failure` пишет строку журнала (`reason='discard_failed'`) | `create_failed` |
| Сбой при **переиспользованном** клоне | клон не удаляется (он привязан и чей-то рабочий); переиспользованный клон мог остаться активным, как сегодня | `create_failed` |

Остаток (§2.10). Исполнение умерло между `Create Workflow` и `Bind`/`Discard` (рестарт, OOM). Либо одновременно лежат БД и
n8n API, и не записалась даже строка журнала. Такой клон находит только Tier 2 (§2.6, B).

### 2.4 Новая цепочка Create (WA; у TG те же имена узлов)

Без изменений до `Seed Saved?`, включая R8: отказ «Вместе» по-прежнему ничего не создаёт.

| Узел | Тип | Настройки | Вход → выходы |
|---|---|---|---|
| **Clone Plan** *(новый)* | Postgres 2.6 | `retryOnFail`, 3 попытки; `continueRegularOutput`; `alwaysOutputData` | `Seed Saved?`[0] → `Plan Read?` |
| **Plan Read?** | If | `$json.planned === true` | [0] → `Reuse?`; [1] → `Respond Plan Unavailable` |
| **Reuse?** | If | `own_id` правильной формы и не в `PROTECTED` | [0] → `Probe Bound Clone`; [1] → `Get Sample Workflow` |
| **Probe Bound Clone** | HTTP GET `localhost/api/v1/workflows/{own_id}` | admin cred, fullResponse, neverError, continueRegularOutput, таймаут 10 с, 2 попытки | → `Bound Clone Usable?` |
| **Bound Clone Usable?** | If | `statusCode == 200 && !body.isArchived && Webhook.path === профиль` | [0] → `Reused Clone Id`; [1] → `Bound Clone Gone?` |
| **Bound Clone Gone?** | If | `statusCode == 404 \|\| (200 && body.isArchived)` | [0] → `Get Sample Workflow`; [1] → `Respond Clone Unavailable` |
| **Reused Clone Id** | Set | `{id: own_id, created: false}` | → `Clone Id` |
| Get Sample Workflow, Set Fields, Create Workflow | без изменений (URL → localhost — рекомендуется) | | → `Get Created Workflow Id` |
| Get Created Workflow Id | Set | `{id: $json.id, created: true}` | → `Clone Id` |
| **Clone Id** | Set (два входа, за исполнение срабатывает один) | `{id, created}` — единый источник id для всех следующих узлов | → `Activate Created Workflow` |
| Activate Created Workflow | HTTP POST localhost `/activate` | admin cred, fullResponse, neverError, continueRegularOutput, таймаут 20 с | → **Activated?** (200): [0] → `Set Wappi Webhook`; [1] → `Discard?` |
| Set Wappi Webhook / Set Wappi Webhook Types | HTTP (Wappi) | fullResponse, neverError, continueRegularOutput | → **Webhook Set?** / **Types Set?** (200): [1] → `Discard?` |
| **Bind Workflow Id** *(новый SQL, `Restamp RAG Chunks` удалён)* | Postgres | `retryOnFail`, 3 попытки; continueRegularOutput; alwaysOutputData | → **Bound?** (`bound === true && workflow_id === Clone Id`): [0] → `Replaced Items`; [1] → `Discard?` |
| **Replaced Items** | Code | один элемент на каждый id из `replace_ids` (плюс `own_id`, если клон в архиве): форма, не в PROTECTED, ≠ Clone Id; если нечего — один `{none:true}` | → **Has Replaced?**: [0] → `Delete Replaced Clone` → `Send New Workflows Id`; [1] → `Send New Workflows Id` |
| **Delete Replaced Clone** | HTTP DELETE localhost (при Q1 = выключать — POST `/deactivate`) | fullResponse, neverError, continueRegularOutput | исход в графе не записывается: строка журнала уже есть, чистка подтвердит её 404 |
| Send New Workflows Id | Set, `executeOnce: true` | `{id: $('Clone Id').first().json.id}` | последний узел → ответ |
| **Discard?** | If | `$('Clone Id').first().json.created === true` | [0] → `Discard New Clone`; [1] → `Respond Create Failed` |
| **Discard New Clone** | HTTP DELETE localhost | fullResponse, neverError, continueRegularOutput, 2 попытки | → **Discarded?** (200/404): [0] → `Respond Create Failed`; [1] → `Record Discard Failure` → `Respond Create Failed` |
| **Record Discard Failure** | Postgres | continueRegularOutput; редкий путь | `insert … reason 'discard_failed' on conflict do nothing` |
| Respond Plan Unavailable / Respond Clone Unavailable / Respond Create Failed | Set (без includeOtherFields) | `{success:false, error:…}` — **без `id`** | конец |

Порядок `Activate → Wappi → Bind` сохранён, чтобы не трогать A2. Seed «Вместе» уже записан до `Clone Plan`, поэтому
активация (новая или переиспользуемая) по-прежнему идёт после seed.

### 2.5 SQL

Оба оператора проверены прототипом на temp-таблицах staging-Postgres: 9 из 9 кейсов (Приложение, `proto_sql.py`), включая
негативные. `verify-sql-comments.check_workflow` находок не даёт. Параметры в комментариях названы словами (V1).

**Clone Plan** (только чтение; ВСЕГДА одна строка `planned = true`, если оператор выполнился). Генератор подставляет
`PROTECTED` из `build-client-webhooks.PROTECTED_IDS` + `0DP79STI3pafXO9B` + `olmqWMfcFpCEcpiE`. Параметры —
`[crAuth.mode, crAuth.installId || '', <профиль>, '<канал>', body.BotKey || '']`.

```sql
-- F1: what this Create may reuse or replace. Read-only; ALWAYS one row with planned = true once it completed.
-- Parameters, in order: gate mode (session or legacy), install id (empty for legacy), profile id, channel, bot key.
with me as (
  select i.install_id as inst from installs i
  where $1 = 'session' and i.install_id = nullif($2, '') and i.revoked_at is null
), own as (
  select bp.workflow_id from bot_profiles bp
  where bp.profile_id = $3 and bp.channel = $4
    and bp.workflow_id ~ '^[A-Za-z0-9]{8,36}$' and bp.workflow_id not in (<PROTECTED>)
    and ((bp.owner_id = (select inst from me)) or ($1 = 'legacy' and bp.owner_id is null))
), prior as (
  select bp.profile_id, bp.workflow_id from bot_profiles bp join me on bp.owner_id = me.inst
  where bp.channel = $4 and nullif($5, '') is not null and bp.bot_key = $5 and bp.profile_id <> $3
    and bp.deleted_reason is distinct from 'app_delete'
    and bp.workflow_id ~ '^[A-Za-z0-9]{8,36}$' and bp.workflow_id not in (<PROTECTED>)
    and bp.workflow_id not in (select workflow_id from own)
    and not exists (select 1 from bot_profiles live where live.workflow_id = bp.workflow_id and live.deleted_at is null
                      and live.profile_id <> $3
                      and not (live.owner_id = me.inst and live.channel = $4 and live.bot_key = $5))
)
select true as planned, (select workflow_id from own limit 1) as own_id,
  coalesce((select jsonb_agg(distinct workflow_id) from prior), '[]'::jsonb) as replace_ids
```

**Bind Workflow Id** (заменяет `Restamp RAG Chunks` и прежний Bind; ВСЕГДА одна строка; `bound = true`, только если строка
профиля теперь называет этот клон).
- Параметры: `[<профиль>, Clone Id, AppUserID || '', '<канал>', installId || '', created ? (own_id || '') : '',
  JSON.stringify(replace_ids), <workflow id другого канала>]`.
- Для TG генератор меняет `botWaId ↔ botTgId` и `bot_wa_id → bot_tg_id`.

```sql
-- F1: bind the clone, hand it the price lists of the clones it replaces, retire their rows and book the replaced clones
-- for deletion, in ONE statement. Parameters, in order: profile id, new workflow id, app user id, channel, install id,
-- the profile's previous binding from Clone Plan (or empty), JSON array of the replaced workflow ids from Clone Plan,
-- the other channel's workflow id (the pre-F1 rule: a second channel joins a bot whose chunks carry the '-1' sentinel).
with moved as (
  select distinct s.v as wf from (
    select nullif($6, '') as v
    union all select x #>> '{}' from jsonb_array_elements($7::jsonb) x where jsonb_typeof(x) = 'string'
  ) s where s.v is not null and s.v <> $2
), bind as (
  insert into bot_profiles (profile_id, app_user_id, channel, workflow_id, owner_id, claimed_at)
  select $1, coalesce(nullif($3, ''), $1), $4, $2, nullif($5, ''), case when nullif($5, '') is null then null else now() end
  where nullif($1, '') is not null and $1 <> '-1'
  on conflict (profile_id) do update set workflow_id = excluded.workflow_id,
    owner_id = coalesce(bot_profiles.owner_id, excluded.owner_id),
    claimed_at = coalesce(bot_profiles.claimed_at, excluded.claimed_at), deleted_at = null, superseded_by = null
  returning workflow_id
), chunks as (
  update documents set metadata = jsonb_set(metadata, '{botWaId}', to_jsonb($2::text))
  where exists (select 1 from bind)
    and (metadata->>'botWaId' in (select wf from moved)
         or ($8 <> '-1' and $8 <> '' and metadata->>'botTgId' = $8 and metadata->>'botWaId' = '-1'))
  returning 1
), files as (
  update uploaded_files set bot_wa_id = $2
  where exists (select 1 from bind) and bot_wa_id in (select wf from moved)
  returning 1
), superseded as (
  update bot_profiles bp set superseded_by = $2, deleted_at = coalesce(bp.deleted_at, now()),
    deleted_reason = coalesce(bp.deleted_reason, 'replaced')
  where exists (select 1 from bind) and bp.profile_id <> $1 and nullif($5, '') is not null and bp.owner_id = $5
    and bp.workflow_id in (select wf from moved)
  returning 1
), booked as (
  insert into clone_retirements (workflow_id, profile_id, owner_id, reason, replaced_by)
  select m.wf, (select bp.profile_id from bot_profiles bp where bp.workflow_id = m.wf limit 1), nullif($5, ''), 'replaced', $2
  from moved m where exists (select 1 from bind)
  on conflict (workflow_id) do nothing
  returning 1
)
select exists (select 1 from bind where workflow_id = $2) as bound, $2::text as workflow_id,
  (select count(*) from chunks)::int as restamped_chunks, (select count(*) from files)::int as restamped_files,
  (select count(*) from superseded)::int as superseded_rows, (select count(*) from booked)::int as booked
```

Свойства оператора (каждое закреплено тестом прототипа или выводится из SQL):
- сентинел-профиль даёт `bound = false`, и ничего не переносится;
- legacy не помечает чужие строки: `superseded` требует установку;
- правило `'-1'` сохранено;
- повтор после `retryOnFail` идемпотентен;
- повторная привязка строки снимает `superseded_by`.

**Миграция** `Tools/n8n/sql/2026-10-xx-clone-retirements.sql` (аддитивная, идемпотентная; применить ДО Sweep и Create):

```sql
alter table public.bot_profiles add column if not exists superseded_by text;   -- the clone that replaced this row's one
create table if not exists public.clone_retirements (
  workflow_id text primary key,
  profile_id  text,
  owner_id    text,
  reason      text not null check (reason in ('replaced', 'discard_failed', 'inventory')),
  replaced_by text,
  created_at  timestamptz not null default now(),
  removed_at  timestamptz,
  attempts    integer not null default 0,
  last_status text
);
create index if not exists clone_retirements_open on public.clone_retirements (created_at) where removed_at is null;
alter table public.clone_retirements enable row level security;   -- like installs / uploaded_files: service role only
```

### 2.6 Чистка (Profile Lifecycle Sweep, раз в 6 ч)

**A. Ветка «Clone Retirements» (Tier 1, обязательна).** Отдельный генератор или расширение `apply-sweep-clone-deactivate.py`
по тому же образцу: `Sweep Config → Clone Retirements → dry run? → Delete Retired Clone → Clone Gone? → Stamp Removed / Stamp Kept`.

```sql
-- F1: clones this system replaced or failed to discard, not yet confirmed gone. Never one a LIVE bot_profiles row still
-- drives, never a protected id, never younger than ten minutes (an in-flight Create). Read-only; 50 per run, oldest first.
select r.workflow_id, r.reason from clone_retirements r
where r.removed_at is null
  and r.workflow_id ~ '^[A-Za-z0-9]{8,36}$' and r.workflow_id not in (<PROTECTED>)
  and r.created_at < now() - interval '10 minutes'
  and not exists (select 1 from bot_profiles live where live.workflow_id = r.workflow_id and live.deleted_at is null)
order by r.created_at limit 50;
```

- 200 или 404 — `removed_at = now()`, иначе `attempts + 1`, `last_status`. При Q1 = «выключать» действие — POST `/deactivate`.
- При Q1 = «удалить через N дней» к условию добавляется `created_at < now() - N days`.
- V12 `Retired Clones` не меняется. Строки с `superseded_by` он и так не берёт, кроме `trial_expiry/churn_grace`, где выключение
  до удаления безвредно.

**B. Ветка «Stray Clones» (Tier 2, рекомендована; сначала только отчёт).** Ловит то, о чём граф не успел записать: умершие
исполнения, двойные сбои и старые сироты, накопленные до фикса.
1. GET localhost `/api/v1/workflows` с пагинацией.
2. Code-узел оставляет клоны: по признакам `update-clones.is_clone` (узлы `Webhook` + `AI Agent` + `Chat Memory`), не
   защищённые, не в архиве, старше 30 мин.
3. Один SQL-вердикт с положительным признаком. Клон — сирота, если:
   - ни одна строка `bot_profiles` его не называет;
   - и строка профиля из его пути существует и (а) называет **другой** ненулевой клон или (б) ретирована без клона.

   Строки с `workflow_id IS NULL` у живого профиля — это pre-A1 клоны: только отчёт, никогда не действие.
4. Две фазы, как в `Delete Orphan Profiles`: при первом обнаружении — выключить (проверить статус) и записать в журнал
   (`reason='inventory'`); через 24 ч и при том же вердикте клон удаляет ветка A.

   Первая фаза особенно важна: активная сирота на пути профиля блокирует любую следующую активацию.
5. Не больше 10 действий за прогон и флаг `strayDryRun` (по умолчанию true на первую неделю).

### 2.7 Legacy (compat-окно до 2026-12-31)

- **Переиспользование — только для привязок без владельца.** Это закрывает (а1)/(а2) для одобренной сборки 1.0 (4).
  Новой эскалации нет: id клона без владельца и сейчас достижим тем, кто знает id профиля.
- **Привязку установки legacy-вызову никогда не отдаём.** Иначе token-less вызов узнал бы чужой id и смог бы удалить его через
  legacy Set Workflow State.
- **`replace_ids` в legacy пуст:** без доказательства ничего не переносится и не удаляется. Значит, (а3) для legacy-клиентов
  остаётся, но только наполовину. Прежний клон и чанки — как сегодня (остаток до выхода A1-клиента или закрытия окна).
  `own_id` (404/архив) переносится и в legacy.
- Отдельно, вне F1 (вопрос Q3): legacy Create, назвавший профиль, которым владеет установка, сегодня перепривязывает его к
  своему клону — это дыра compat-окна. Предлагается отказывать ему колонкой `foreign` в `Clone Plan` (+0,5 ч).

### 2.8 V10, оба канала, защищённые id, шаблоны

- **V10.** Переиспользование не трогает ни голову, ни хвост промпта. Custom-голова (§V10) переживает повторную авторизацию —
  сегодня она теряется. Новый клон по-прежнему собирается из карты Create (как сейчас). Шаблоны ботов не меняются, поэтому
  `update-clones.py` при выкладке не нужен.
- **Оба канала.** Один генератор с таблицей каналов (как `apply-slot-count-fix.py`): `whatsapp/WhatsappProfileId/botWaId/bot_wa_id`
  против `telegram/TelegramProfileId/botTgId/bot_tg_id`. `verify-telegram-parity.py` сверяет близнецов.
- **Защищённые id** исключаются в `Clone Plan`, в `Replaced Items`, в `Reuse?`, в ветках чистки. `verify-clone-lifecycle.py`
  сверяет список с `PROTECTED_IDS`.

### 2.9 Клиент

**Для корректности фикс клиентских изменений не требует.** Контракт ответа прежний: `{id}` или тело с `error` без `id`.
`FromEdit` идёт в `FailSavePanel`, `FromStart` — в `serverRejected`.

Follow-up (Q6, отдельной задачей). После F1 Create идемпотентен, поэтому:
- (1) Повтор Create, когда канал ВКЛ и авторизован, а у бота нет id клона (или последний Create провалился). Сегодня у
  проваленного Create из настроек нет пути повтора: тумблер → «авторизован» → ничего.
- (2) «Сменить номер» перестаёт удалять клон: переиспользование его сохранит. Уходит гонка из §2.10.

### 2.10 Остаточные риски

- Исполнение умерло между `Create Workflow` и `Bind`/`Discard`, или одновременно лежат БД и n8n API. Клон находит только
  Tier 2 (B).
- `Create Workflow` упал по таймауту, хотя n8n workflow уже создал (ответ потерян). Это тоже Tier 2. Переход на localhost
  делает такой сбой реже.
- Гонка «Сменить номер» в клиентах до follow-up. Fire-and-forget удаление WFold может прийти **после** того, как Create
  переиспользовал WFold. Тогда привязанный клон удалён и бот молчит до следующего Create. Окно: удаление уходит через одну
  сетевую задержку после logout, а Create — после того, как пользователь прошёл авторизацию (≥ 10 с). Сервер это не видит,
  снимает клиентский follow-up (2).
- Pre-A1 клоны (строка без `workflow_id`) не переиспользуются и не заменяются. Если такой клон активен на том же пути, каждый
  Create падает с `create_failed`. По CLAUDE.md таких клонов на проде нет; подтвердить инвентаризацией (Q5).
- `bot_key` — переиспользуемое имя слота. Бот, удалённый «только с телефона» старым клиентом до V12, оставил живые строки
  со слотом `BotN`. Его клон будет принят за прежний клон нового `BotN` той же установки и удалён. Это совпадает с волей
  владельца, который этого бота удалил.
- **Смежное, вне F1 (Q7).** При замене профиля (а3) память, исходы и флаги P1 (ключ — профиль) удаление бота не стирает.
  Кроме того, бот «забывает» прежние диалоги даже на том же номере. Кандидат: `Delete Bot Files` дополнительно чистит строки
  того же бота с `superseded_by`.

---

## 3. Тесты

| # | Тест | До (HEAD) | После |
|---|---|---|---|
| A | `tests/test_create_clone.py` (новый), часть SQL: настоящие `Clone Plan` и `Bind Workflow Id` на temp-таблицах. Матрица: своя привязка; прежние того же бота; чужая установка; `app_delete`; защищённый id; пустой `bot_key`; отозванная установка; id, который водит другая живая строка; legacy своя/чужая; сентинел; правило `'-1'`; идемпотентный повтор; перепривязка снимает `superseded_by` | — | 9 кейсов прототипа + 4 новых — зелёные |
| B | Та же, «книги клонов»: граф через драйвер, затем SQL-реплей по порядку, затем читающие запросы. Инвариант: **каждый клон, созданный прогоном или названный планом, либо привязан и выдан клиенту, либо удалён (200/404), либо стоит в `clone_retirements` и его берёт настоящий SQL ветки A.** Сценарии × WA/TG × сессия/legacy: свежий; переиспользование неактивного; переиспользование активного; своя привязка 404; своя в архиве; проба 500 (ничего не создано); (а3) с удалением ок и с удалением сбой; Activate (исключение / 400-конфликт / 500); Wappi url; Wappi types; Bind (queryError / исключение / пусто); Discard сбой; Discard + журнал сбой (единственный остаток → помечен как Tier 2); `Clone Plan` ×3 формы сбоя | 14 нарушений (это §1.3) | 0 нарушений, остаток — ровно один названный случай |
| C | Контракт ответа: ни одно тело отказа не содержит `"id"`; для `FromEdit` `Contains("\"id\":")` = false, для `FromStart` есть `"error"` | `bind_failed` отдаёт id | зелёный |
| D | `test_create_seed.py` (R8, 52 кейса): сценарии получают ответ `Clone Plan` с `planned`; имена узлов и проверки URL Activate → localhost | 52/52 | 52/52 (отказ «Вместе» по-прежнему ничего не создаёт; «Авто» и legacy создают клон) |
| E | `verify-clone-lifecycle.py` (новый) и мутационные тесты к нему (как `test_create_seed` E). Структура: (1) `Create Workflow` достижим только через `Plan Read?[0]` и (`Reuse?[1]` \| `Bound Clone Gone?[0]`); (2) любой путь от `Create Workflow` к концу идёт через `Bound?[0]` или через `Discard New Clone`; (3) у Activate, Wappi ×2, Probe, Delete ×2 есть neverError + fullResponse + continueRegularOutput, а за ними If по `statusCode`; (4) у Bind есть retry и положительный If; (5) ни один Respond отказа не содержит `id`; (6) URL n8n API — localhost + `__HDR_CRED__`; (7) списки PROTECTED совпадают с `PROTECTED_IDS`. Мутации, которые должны отклоняться: `Bind → Send` в обход `Bound?`; удаление ребра Discard; Activate без neverError; ребро `Seed Saved?[0] → Get Sample` в обход плана; Respond с `id` | — | все мутации отклонены |
| F | Обновлённые верификаторы: `verify-reply-mode.py` `check_create` (правило «единственный предшественник Get Sample = `Seed Saved?[0]`» → достижимость `Create Workflow` только через `Seed Saved?[0]`); `verify-auth-gate.py:258` (проводка Bind); `verify-telegram-parity.py` (§ Restamp (i)–(iv) → проверки нового Bind; список узлов ~L276); `verify-sql-comments.py` по новому SQL | зелёные на HEAD | зелёные |
| G | Ветка A чистки: настоящий SQL — никогда id, который водит живая строка; никогда защищённый; не моложе 10 мин; 200/404 ставят отметку, 500 оставляет строку и увеличивает `attempts` | — | зелёный |
| H | Staging e2e (`staging/e2e-auth.py`, WA+TG): `reauth_same_profile_reuses_bound_clone` (Create → выключить через SetWorkflowState → Create тот же профиль → тот же id, число клонов не изменилось, клон активен); `reauth_new_profile_replaces_prior_and_moves_chunks` (заглушка-чанк под WFold → Create P2 → чанки под WFnew, GET WFold = 404, `removed_at` проставлен); `activation_conflict_discards_new_clone` (активный ручной workflow на пути профиля → `create_failed`, клонов не прибавилось); `bind_failure_answers_no_id` (приём R8: CHECK на `bot_profiles` для профиля → `create_failed`, клон удалён). Книги — по списку workflows staging до и после | — | зелёные |
| I | Негатив безопасности (e2e): установка B называет свой профиль с `BotKey`, совпадающим с ботом установки A, → клон и чанки A не тронуты; legacy-вызов по профилю установки → её id не выдан | — | зелёные |
| J | Клиентский follow-up (если Q6 = да): EditMode-тесты политики повтора и проводки «Сменить номер» | — | отдельная задача |

## 4. Файлы, workflow, порядок выкладки

- **Новые:** `Tools/n8n/apply-clone-lifecycle.py` (генератор Create WA/TG, `--check`), `Tools/n8n/verify-clone-lifecycle.py`,
  `Tools/n8n/sql/2026-10-xx-clone-retirements.sql`, `Tools/n8n/tests/test_create_clone.py`. Ветку A чистки — в
  `apply-sweep-clone-deactivate.py` или в новом `apply-sweep-clone-retirements.py`.
- **Меняются:**
  - `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json`, `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` и
    `fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json` — только через генераторы.
  - `apply-auth-gate.py`: отдать хвост Create (`BIND_WORKFLOW_SQL`, проводку Restamp→Bind→Send) новому генератору и оставить
    указатель. Так уже сделано со стадией 4 и телами Claim, иначе генераторы будут переписывать друг друга.
  - `verify-auth-gate.py`, `verify-telegram-parity.py`, `verify-reply-mode.py`.
  - `tests/create_seed_chain.js` — расширения из Приложения, обратно совместимы: 52/52.
  - `tests/test_create_seed.py`, `staging/e2e-auth.py`. В `staging/derive-staging.py` — проверить, что admin-кред Create
    перепривязывается так же, как у Set Workflow State.
  - CLAUDE.md: A1 (Bind), A2 (абзац R8-deletefail: F1 закрыта), V12 (чистка), раздел Wappi `auth/code` (исправить инвариант).
  - Журнал итерации.
- **Не меняются:** шаблоны ботов (выкладка клонов не нужна), Edit WA/TG, Set Workflow State, Delete Bot Files (при Q1 = «выключать
  и хранить» — меняется, +3–4 ч), Wappi Proxy, Auth Session, клиент.
- **Порядок выкладки:**
  1. миграция SQL;
  2. Sweep — его ветка читает новую таблицу;
  3. Create WA/TG — `deploy-canonical.py` привязывает `__HDR_CRED__` и проверяет SQL-guard;
  4. read-back.

  Перед выкладкой — read-only инвентаризация сирот на проде (Q5). Откат — прежние JSON Create из
  `docs/audits/2026-10-09-deploy-rollback-pointers.txt`; таблица и колонка аддитивны.

## 5. Объём

| Часть | Часы |
|---|---|
| Генератор + оба графа Create | 6 |
| SQL + миграция | 1,5 |
| Расширение драйвера, `test_create_clone.py` (A–C), правка R8-теста | 6 |
| `verify-clone-lifecycle.py`, 3 обновлённых верификатора, мутации | 3 |
| Ветка A чистки + тест | 2 |
| Staging e2e × 2 канала | 4 |
| Документация, журнал | 1 |
| **Tier 1 итого** | **≈ 23–24 ч (≈ 3 рабочих дня)** |
| Tier 2: инвентаризация (две фазы, сначала только отчёт) | +6–8 |
| Клиентский follow-up (повтор Create, «Сменить номер» без удаления) + EditMode | +3–4, плюс device-pass |

## 6. Вопросы владельцу

**Q1. Что делать с заменённым клоном** — прежний клон бота при новом профиле (а3), а также своя привязка, которая в архиве.
- (a) **Удалять после положительной привязки нового. Рекомендую.**
- (b) Выключать и хранить. Тогда `Delete Bot Files` должен научиться находить такие клоны (+3–4 ч), а данные лежат до
  удаления бота.
- (c) Выключать сразу и удалять чисткой через N дней (например, 7). Это окно, чтобы поддержка вернула custom-голову (V10).

Почему (a):
- всё, что лежит в клоне, — данные владельца, которые есть у него в приложении и которые уже несёт новый клон;
- прайсы переносятся, а не удаляются;
- сохранённая копия, до которой ничто не дотянется, противоречит обещанию «удалить по запросу»;
- вместе с клоном n8n удаляет его исполнения, а в них тексты клиентов.

Урок V12 («выключать, не удалять») касался бота, которого владелец **не** удалял и мог вернуть. Здесь владелец сам заменил
канал бота. Единственное уникальное содержимое — custom-голова V10, но новый клон её и так не несёт. Если она ценна, это
довод за (c), а не за (b).

**Q2. Повторная авторизация того же профиля.**
- **Переиспользовать привязанный клон. Рекомендую.**
- Против: всегда пересоздавать, буквально как в журнале. Тогда нужен порядок «выключить прежний → активировать новый →
  при сбое включить прежний обратно» с проверкой каждого шага, теряется custom-голова V10 и нужен перенос чанков.

Минус переиспользования: systemMessage не освежается полями экрана, правки ложатся через «Сохранить», как сейчас.

**Q3. Compat-окно** (одобренная сборка 1.0 (4) — legacy).
- Переиспользование для привязок без владельца — **да, рекомендую.**
- Перенос и удаление прежних клонов только в сессии — **да, рекомендую.**
- Отказ legacy-Create по профилю, которым владеет установка, — закрывает перепривязку в compat-окне, вне F1, +0,5 ч.
  **Рекомендую сделать заодно.**

**Q4. Tier 2 (инвентаризация).** Сделать сейчас в режиме «только отчёт» неделю, потом включить действия — **рекомендую** —
или отложить. Без Tier 2 остаток — двойной сбой или умершее исполнение — живёт, пока клон не найдут руками.

**Q5. Разрешить read-only инвентаризацию сирот на проде до выкладки.** Список клонов, которые не называет ни одна строка
`bot_profiles`, и клонов pre-A1. **Рекомендую.** Нужен явный «да»: это чтение прода.

**Q6. Клиентский follow-up** (повтор Create и «Сменить номер» без удаления клона) — **отдельной задачей после F1, рекомендую.**

**Q7 (смежное).** Стирать ли при удалении бота память, исходы и флаги его прежних профилей (а3)? **Рекомендую да,
отдельной находкой.**

---

## Приложение. Воспроизведение и прототип

Запуск (нужен только staging-Postgres: `Tools/n8n/staging/pg.sh start`; n8n и мок не нужны). Файлы положить в одну папку,
скопировать `Tools/n8n/tests/create_seed_chain.js` в `chain.js` и применить diff ниже.

```
python3 repro_f1.py      # HEAD: 14 pass = 14 подтверждённых фактов (кейсы проходят, когда дефект есть)
python3 proto_sql.py     # прототип Clone Plan / Bind Workflow Id: 9 pass
```

Результат прогона 2026-10-09: `14 pass, 0 fail` и `9 pass, 0 fail`. `test_create_seed.py` на расширенном драйвере: `52 pass, 0 fail`.


### A. `chain.diff` — расширение драйвера `create_seed_chain.js` (обратно совместимо: `test_create_seed.py` 52/52)

```diff
--- Tools/n8n/tests/create_seed_chain.js	2026-10-07 12:50:51
+++ chain.js	2026-10-09 13:14:54
@@ -61,7 +61,7 @@
   const byName = Object.fromEntries(wf.nodes.map((n) => [n.name, n]));
   const out = {
     'Unity Webhook': [[{ json: { body: sc.body, headers: {}, params: {}, query: {} } }]],
-    'Auth Gate': [[{ json: { crAuth: { ok: true, installId: 'install-r8' } } }]],
+    'Auth Gate': [[{ json: { crAuth: sc.crAuth || { ok: true, mode: 'session', installId: 'install-r8' } } }]],
   };
   const http = Object.assign({ 'Create Workflow': { id: sc.createdId } }, sc.http || {});
   let steps = 0;
@@ -76,9 +76,12 @@
   let lastOutput = null;
   let stoppedAt = null;
 
+  const pgCalls = {};
   function seedOnce(n) {
-    const a = sc.seed[Math.min(seedCalls, sc.seed.length - 1)];
-    seedCalls++;
+    const list = n.name === 'Seed Reply Mode' ? sc.seed : ((sc.pg || {})[n.name] || [{ rows: [] }]);
+    pgCalls[n.name] = (pgCalls[n.name] || 0) + 1;
+    const a = list[Math.min(pgCalls[n.name] - 1, list.length - 1)];
+    if (n.name === 'Seed Reply Mode') seedCalls++;
     if (a.throw) throw new Error(a.throw);
     if (a.noItems) return [[]];
     if (a.queryError) {
@@ -130,11 +133,15 @@
     let outputs;
     switch (n.type) {
       case 'n8n-nodes-base.postgres':
-        if (name === 'Seed Reply Mode') {
+        {
+          let params = null;
+          try {
+            const qr = ((n.parameters || {}).options || {}).queryReplacement;
+            params = evalExpr(qr, ctx(items[0]));
+            if (typeof params === 'string') params = params.split(',');
+          } catch (e) { params = { evalError: e.message }; }
+          effects.push({ node: name, params });
           outputs = execSeed(n, items);
-        } else {
-          effects.push({ node: name });
-          outputs = [[{ json: { success: true } }]];
         }
         break;
       case 'n8n-nodes-base.if': {
@@ -156,7 +163,13 @@
         break;
       case 'n8n-nodes-base.httpRequest':
         effects.push({ node: name, method: n.parameters.method || 'GET', url: evalExpr(n.parameters.url, ctx(items[0])) });
-        outputs = [[{ json: http[name] || {} }]];
+        if (http[name] && http[name].throw) {
+          if (n.onError === 'continueRegularOutput') outputs = [[{ json: { error: { message: http[name].throw } } }]];
+          else if (n.onError === 'continueErrorOutput') outputs = [[], [{ json: { error: { message: http[name].throw } } }]];
+          else throw new Error(name + ': ' + http[name].throw);
+        } else {
+          outputs = [[{ json: http[name] || {} }]];
+        }
         break;
       default:
         throw new Error('driver: unsupported node type ' + n.type + ' (' + name + ')');
```

### B. `repro_f1.py` — воспроизведение на HEAD

```python
#!/usr/bin/env python3
"""F1 reproduction on HEAD (scratch, not committed): the canonical Create WA/TG graphs through an extended copy of
create_seed_chain.js (chain.js: HTTP failure modes, every Postgres node records its evaluated parameters), then the
REAL SQL of every Postgres node the run reached replayed in order on session TEMP tables of the staging Postgres,
then the REAL read-side statements that decide what is reachable afterwards:
  * Auth Session `Reload Ownership` (= the token's p/w),
  * Set Workflow State `Delete Ownership` (V12: may this install delete the id?),
  * Profile Lifecycle Sweep `Retired Clones` (V12: which retired clones get deactivated),
  * Delete Bot Files `Delete Bot Chunks` (what a later bot deletion removes).
Books: every clone the run created or found, and who can still reach it."""
import json
import os
import subprocess
import sys

sys.path.insert(0, '/Users/ayan/Projects/Automation/Tools/n8n/tests')
from harness import Case, PgSession, bind, node, report, wf  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DRIVER = os.path.join(HERE, 'chain.js')
CREATES = {'WA': ('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'WhatsappProfileId', 'TelegramWorkflowId', 'whatsapp', 'botWaId', 'bot_wa_id'),
           'TG': ('Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'TelegramProfileId', 'WhatsappWorkflowId', 'telegram', 'botTgId', 'bot_tg_id')}
AUTH = wf('crAuthSession001-Auth_Session.json')
SET_STATE = wf('0DP79STI3pafXO9B-Set_Workflow_State.json')
SWEEP = wf('fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json')
DBF = wf('lmjYsdNcQA2IE5rl-Delete_Bot_Files.json')

U = '$RCAnonymousID:' + '0123456789abcdef' * 2
INST = 'inst-f1-A'
WF_OLD, WF_NEW = 'WFf1old00000001', 'WFf1new00000002'
SETUP = """
create temp table bot_profiles (like public.bot_profiles including all);
create temp table installs (like public.installs including all);
create temp table subscribers (like public.subscribers including all);
create temp table reply_mode_flags (like public.reply_mode_flags including all);
create temp table documents (like public.documents including all);
create temp table uploaded_files (like public.uploaded_files including all);
"""


def drive(w, scenarios):
    tmp = os.path.join(HERE, '.f1-wf.json')
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(w, f)
    try:
        r = subprocess.run(['node', DRIVER, tmp], input=json.dumps(scenarios), text=True, capture_output=True)
    finally:
        os.unlink(tmp)
    if r.returncode != 0:
        raise RuntimeError('driver failed: ' + r.stderr.strip()[-2000:])
    return json.loads(r.stdout)


def body(field, other_field, pid):
    return {field: pid, other_field: '-1', 'Name': 'f1', 'BotKey': 'Bot0', 'AppUserID': U,
            'BusinessType': 'Цветы', 'BusinessTypeId': 'flowers', 'Business': 'About Business:\nСекретная цена поставщика',
            'Prompt': 'p', 'ProductsList': 'Products:\n', 'ServicesList': 'Services:\n', 'ReplyMode': 'auto', 'ReplyModeVersion': '1700000000000'}


def replay(pg, w, rep):
    """The real statements of every Postgres node the run reached, in order, with the parameters it evaluated."""
    done = []
    for e in rep['effects']:
        if 'params' not in e:
            continue
        n = node(w, e['node'])
        params = e['params']
        if isinstance(params, dict) and 'evalError' in params:
            raise RuntimeError(f"{e['node']}: {params['evalError']}")
        q = n['parameters']['query']
        if e['node'] == 'Seed Reply Mode':
            pg.q(bind(q, params))
        else:
            pg.x(bind(q, params))
        done.append(e['node'])
    return done


def seed_db(pg, ch, pid_old, pid_new=None, old_reason=None):
    _, _, _, channel, meta_key, file_col = CREATES[ch]
    pg.x(f"insert into installs (install_id, secret_hash) values ('{INST}', 'h');")
    pg.x(f"insert into subscribers (app_user_id, plan, status, trial_started_at) values ('{U}', 'trial', 'trialing', now());")
    pg.x(f"""insert into bot_profiles (profile_id, app_user_id, channel, bot_key, owner_id, claimed_at, workflow_id, deleted_at, deleted_reason)
            values ('{pid_old}', '{U}', '{channel}', 'Bot0', '{INST}', now(), '{WF_OLD}', {'now()' if old_reason else 'null'}, {f"'{old_reason}'" if old_reason else 'null'});""")
    if pid_new:   # the proxy's Bind Profile row of a freshly created profile (owner from birth, no bot_key, no workflow)
        pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, claimed_at) values ('{pid_new}', '{U}', '{channel}', '{INST}', now());")
    other = 'botTgId' if meta_key == 'botWaId' else 'botWaId'
    for i in range(2):
        pg.x(f"""insert into documents (content, metadata) values ('Розы {i}: 3200 тг',
                 jsonb_build_object('{meta_key}', '{WF_OLD}', '{other}', '-1', 'fileId', 'F1'));""")
    pg.x(f"insert into uploaded_files (file_id, owner_id, {file_col}) values ('F1', '{INST}', '{WF_OLD}');")


def reach(pg, wid, meta_key):
    """Who can still reach clone `wid` after the run."""
    rows = pg.q(f"select profile_id, deleted_at is not null as retired, deleted_reason from bot_profiles where workflow_id = '{wid}'")
    own = pg.q(bind(node(AUTH, 'Reload Ownership')['parameters']['query'], [INST]))[0]
    dq = node(SET_STATE, 'Delete Ownership')['parameters']['query']
    verdict = pg.q(bind(dq, ['session', INST, json.dumps([]), json.dumps([wid])]))[0]
    sweep = [r['workflow_id'] for r in pg.q(node(SWEEP, 'Retired Clones')['parameters']['query'])]
    chunks = pg.q(f"select count(*)::int c from documents where metadata->>'{meta_key}' = '{wid}'")[0]['c']
    return {'rows': rows, 'token_w': own['w'], 'delete_ownership': 'ok' if verdict.get('allowed') else 'refused', 'delete_reason': verdict.get('reason'),
            'sweep_deactivates': wid in sweep, 'chunks': chunks}


def touched(rep, name):
    return [e for e in rep['effects'] if e['node'] == name]


OK_SEED = [{'rows': [{'seeded': True, 'written': 1}]}]
CONFLICT = {'throw': 'There is a conflict with one of the webhooks. (409/400 from POST /workflows/{id}/activate)'}

out = {}
for ch, (fname, field, other_field, channel, meta_key, file_col) in CREATES.items():
    W = wf(fname)
    P1, P2 = f'{ch.lower()}-prof-f1-1', f'{ch.lower()}-prof-f1-2'
    start = 'Retire Same Bot Slot'      # the slot-allowed branch (If Slot Limit[1]) of a session client
    crauth = {'ok': True, 'mode': 'session', 'installId': INST}

    # ---- (а) re-auth of the SAME profile from the settings, prior clone saved OFF (inactive): Create succeeds
    sc = {'a_inactive': {'body': body(field, other_field, P1), 'createdId': WF_NEW, 'seed': OK_SEED, 'start': start, 'crAuth': crauth}}
    rep = drive(W, sc)['a_inactive']
    with Case(f'F1 [{ch}] (а) re-auth of the same profile, prior clone inactive: the new clone is bound, the prior one is lost') as c:
        with PgSession(SETUP) as pg:
            seed_db(pg, ch, P1)
            before = reach(pg, WF_OLD, meta_key)
            ran = replay(pg, W, rep)
            old, new = reach(pg, WF_OLD, meta_key), reach(pg, WF_NEW, meta_key)
        prior_calls = [e for e in rep['effects'] if 'url' in e and WF_OLD in (e['url'] or '')]
        out[f'{ch}:a_inactive'] = {'trace': rep['trace'], 'response': rep['response'], 'pg_replayed': ran,
                                    'before_old': before, 'after_old': old, 'after_new': new, 'calls_on_prior': prior_calls}
        c.check(rep['response'] and rep['response']['json'] == {'id': WF_NEW}, f'response {rep["response"]}')
        c.check(before['token_w'] == [WF_OLD] and before['delete_ownership'] == 'ok', f'precondition {before}')
        # the finding, stated as the facts it consists of (the case PASSES when the defect is present)
        c.check(prior_calls == [], f'something touched the prior clone: {prior_calls}')
        c.check(old['rows'] == [] and old['token_w'] == [WF_NEW] and old['delete_ownership'] != 'ok',
                f'prior clone still reachable: {old}')
        c.check(old['chunks'] == 2 and new['chunks'] == 0, f'chunks old={old["chunks"]} new={new["chunks"]}')

    # ---- (а) prior clone still ACTIVE (toggle OFF->ON without saving): the activation hits the webhook path = profile id
    sc = {f'a_active_try{i}': {'body': body(field, other_field, P1), 'createdId': f'WFf1try{i}0000000', 'seed': OK_SEED, 'start': start,
                                'crAuth': crauth, 'http': {'Activate Created Workflow': CONFLICT}} for i in range(1, 4)}
    reps = drive(W, sc)
    with Case(f'F1 [{ch}] (а) re-auth, prior clone active: every attempt leaves one more unbound clone (500 to the client)') as c:
        lost = 0
        for k, r in reps.items():
            created = len(touched(r, 'Create Workflow'))
            bound = 1 if 'Bind Workflow Id' in r['trace'] else 0
            deleted = len([e for e in r['effects'] if e.get('method') == 'DELETE'])
            lost += created - bound - deleted
            c.check(r['stoppedAt'] and r['stoppedAt']['node'] == 'Activate Created Workflow' and r['response'] is None, f'{k}: {r["stoppedAt"]}')
        out[f'{ch}:a_active'] = {'attempts': len(reps), 'lost': lost, 'stoppedAt': [r['stoppedAt'] for r in reps.values()]}
        c.check(lost == 3, f'lost={lost}')

    # ---- (а') the profile was replaced (WA: the OnEnable probe deleted the unauthorized profile; settings Back): new profile P2
    sc = {'a_replaced': {'body': body(field, other_field, P2), 'createdId': WF_NEW, 'seed': OK_SEED, 'start': start, 'crAuth': crauth}}
    rep = drive(W, sc)['a_replaced']
    with Case(f'F1 [{ch}] (а\') new profile for the same bot: prior clone stays bound to a retired row, never switched off, its price lists unreachable') as c:
        with PgSession(SETUP) as pg:
            seed_db(pg, ch, P1, pid_new=P2)
            ran = replay(pg, W, rep)
            old, new = reach(pg, WF_OLD, meta_key), reach(pg, WF_NEW, meta_key)
            p1 = pg.q(f"select deleted_at is not null as retired, deleted_reason from bot_profiles where profile_id = '{P1}'")[0]
            dbf = node(DBF, 'Delete Bot Chunks')['parameters']['query']
            # a later «удалить бота»: the client sends the ids it knows (P2 / WF_NEW) — never WF_OLD
            args = [WF_NEW, '-1'] if meta_key == 'botWaId' else ['-1', WF_NEW]
            deleted = pg.q(bind(dbf, args))[0]['deletedChunks']
            left = pg.q(f"select count(*)::int c from documents where metadata->>'{meta_key}' = '{WF_OLD}'")[0]['c']
        prior_calls = [e for e in rep['effects'] if 'url' in e and WF_OLD in (e['url'] or '')]
        out[f'{ch}:a_replaced'] = {'trace': rep['trace'], 'response': rep['response'], 'p1_row': p1, 'after_old': old, 'after_new': new,
                                    'later_bot_delete_chunks_deleted': deleted, 'chunks_left_under_old': left, 'calls_on_prior': prior_calls}
        c.check(rep['response'] and rep['response']['json'] == {'id': WF_NEW}, f'response {rep["response"]}')
        c.check(prior_calls == [] and p1['retired'] and p1['deleted_reason'] is None, f'p1={p1} calls={prior_calls}')
        c.check(not old['sweep_deactivates'] and WF_OLD not in old['token_w'], f'old={old}')
        c.check(new['chunks'] == 0 and deleted == 0 and left == 2, f'new chunks={new["chunks"]} deleted={deleted} left={left}')

    # ---- (б) a failure after Create Workflow
    fails = {'activate_failed': {'Activate Created Workflow': {'throw': 'getaddrinfo EAI_AGAIN n8n.choosereply.com'}},
             'wappi_webhook_failed': {'Set Wappi Webhook': {'throw': 'Wappi 502'}},
             'wappi_types_failed': {'Set Wappi Webhook Types': {'throw': 'ETIMEDOUT'}}}
    sc = {k: {'body': body(field, other_field, P2), 'createdId': WF_NEW, 'seed': OK_SEED, 'start': start, 'crAuth': crauth, 'http': h}
          for k, h in fails.items()}
    sc['bind_failed'] = {'body': body(field, other_field, P2), 'createdId': WF_NEW, 'seed': OK_SEED, 'start': start, 'crAuth': crauth,
                         'pg': {'Bind Workflow Id': [{'queryError': 'connection terminated unexpectedly'}]}}
    reps = drive(W, sc)
    for k, r in reps.items():
        with Case(f'F1 [{ch}] (б) {k}: the created clone ends unbound') as c:
            with PgSession(SETUP) as pg:
                seed_db(pg, ch, P1, pid_new=P2)
                # replay only what really ran; a Bind that failed wrote nothing
                r2 = dict(r, effects=[e for e in r['effects'] if not (k == 'bind_failed' and e['node'] == 'Bind Workflow Id')])
                replay(pg, W, r2)
                new = reach(pg, WF_NEW, meta_key)
            activated = bool(touched(r, 'Activate Created Workflow')) and k != 'activate_failed'
            out[f'{ch}:b_{k}'] = {'trace': r['trace'], 'stoppedAt': r['stoppedAt'], 'response': r['response'], 'new_clone': new,
                                  'new_clone_active': activated}
            c.check(len(touched(r, 'Create Workflow')) == 1 and not [e for e in r['effects'] if e.get('method') == 'DELETE'], 'create/delete')
            c.check(new['rows'] == [] and new['delete_ownership'] != 'ok' and WF_NEW not in new['token_w'], f'new={new}')
            if k == 'bind_failed':
                c.check(r['response'] and r['response']['json'] == {'id': WF_NEW}, f'bind_failed answered {r["response"]}')
            else:
                c.check(r['response'] is None and r['stoppedAt'] is not None, f'{k}: {r["stoppedAt"]}')

with open(os.path.join(HERE, 'repro-f1-head.json'), 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
report()
```

### C. `proto_sql.py` — прототип `Clone Plan` / `Bind Workflow Id` (тот же текст, что в §2.5; `<PROTECTED>` здесь сокращён до 5 id)

```python
#!/usr/bin/env python3
"""Prototype check of the two F1 statements proposed in F1-design.md (Clone Plan, Bind Clone) on session TEMP tables of
the staging Postgres. Scratch only — not the implementation."""
import json
import sys

sys.path.insert(0, '/Users/ayan/Projects/Automation/Tools/n8n/tests')
from harness import Case, PgSession, bind, report  # noqa: E402

PROTECTED = ['4wYitz5ek30SVNlT', '4VN3gsFaC2HUYmcc', 'XuvOp7TxOImOAmlj', 'Uz6HBBUpAiUqVysB', '0DP79STI3pafXO9B']
PROT = ', '.join("'%s'" % p for p in PROTECTED)
SETUP = """
create temp table bot_profiles (like public.bot_profiles including all);
alter table bot_profiles add column superseded_by text;
create temp table clone_retirements (workflow_id text primary key, profile_id text, owner_id text,
  reason text not null check (reason in ('replaced', 'discard_failed', 'inventory')), replaced_by text,
  created_at timestamptz not null default now(), removed_at timestamptz, attempts integer not null default 0, last_status text);
create temp table installs (like public.installs including all);
create temp table documents (like public.documents including all);
create temp table uploaded_files (like public.uploaded_files including all);
insert into installs (install_id, secret_hash) values ('inst-A', 'h'), ('inst-B', 'h');
insert into installs (install_id, secret_hash, revoked_at) values ('inst-R', 'h', now());
"""

CLONE_PLAN = """-- F1: what this Create may reuse or replace. Read-only; ALWAYS one row with planned = true once it completed.
-- Parameters, in order: gate mode (session or legacy), install id (empty for legacy), profile id, channel, bot key.
with me as (
  select i.install_id as inst from installs i
  where $1 = 'session' and i.install_id = nullif($2, '') and i.revoked_at is null
), own as (
  select bp.workflow_id from bot_profiles bp
  where bp.profile_id = $3 and bp.channel = $4
    and bp.workflow_id ~ '^[A-Za-z0-9]{8,36}$' and bp.workflow_id not in (PROT)
    and ((bp.owner_id = (select inst from me)) or ($1 = 'legacy' and bp.owner_id is null))
), prior as (
  select bp.profile_id, bp.workflow_id from bot_profiles bp join me on bp.owner_id = me.inst
  where bp.channel = $4 and nullif($5, '') is not null and bp.bot_key = $5 and bp.profile_id <> $3
    and bp.deleted_reason is distinct from 'app_delete'
    and bp.workflow_id ~ '^[A-Za-z0-9]{8,36}$' and bp.workflow_id not in (PROT)
    and bp.workflow_id not in (select workflow_id from own)
    and not exists (select 1 from bot_profiles live where live.workflow_id = bp.workflow_id and live.deleted_at is null
                      and live.profile_id <> $3
                      and not (live.owner_id = me.inst and live.channel = $4 and live.bot_key = $5))
)
select true as planned, (select workflow_id from own limit 1) as own_id,
  coalesce((select jsonb_agg(distinct workflow_id) from prior), '[]'::jsonb) as replace_ids""".replace('PROT', PROT)

BIND_CLONE = """-- F1: bind the clone, hand it the price lists of the clones it replaces, retire their rows and book the replaced clones
-- for deletion, in ONE statement. Parameters, in order: profile id, new workflow id, app user id, channel, install id,
-- the profile's previous binding from Clone Plan (or empty), JSON array of the replaced workflow ids from Clone Plan,
-- the other channel's workflow id (the pre-F1 rule: a second channel joins a bot whose chunks carry the '-1' sentinel).
with moved as (
  select distinct s.v as wf from (
    select nullif($6, '') as v
    union all select x #>> '{}' from jsonb_array_elements($7::jsonb) x where jsonb_typeof(x) = 'string'
  ) s where s.v is not null and s.v <> $2
), bind as (
  insert into bot_profiles (profile_id, app_user_id, channel, workflow_id, owner_id, claimed_at)
  select $1, coalesce(nullif($3, ''), $1), $4, $2, nullif($5, ''), case when nullif($5, '') is null then null else now() end
  where nullif($1, '') is not null and $1 <> '-1'
  on conflict (profile_id) do update set workflow_id = excluded.workflow_id,
    owner_id = coalesce(bot_profiles.owner_id, excluded.owner_id),
    claimed_at = coalesce(bot_profiles.claimed_at, excluded.claimed_at), deleted_at = null, superseded_by = null
  returning workflow_id
), chunks as (
  update documents set metadata = jsonb_set(metadata, '{botWaId}', to_jsonb($2::text))
  where exists (select 1 from bind)
    and (metadata->>'botWaId' in (select wf from moved)
         or ($8 <> '-1' and $8 <> '' and metadata->>'botTgId' = $8 and metadata->>'botWaId' = '-1'))
  returning 1
), files as (
  update uploaded_files set bot_wa_id = $2
  where exists (select 1 from bind) and bot_wa_id in (select wf from moved)
  returning 1
), superseded as (
  update bot_profiles bp set superseded_by = $2, deleted_at = coalesce(bp.deleted_at, now()),
    deleted_reason = coalesce(bp.deleted_reason, 'replaced')
  where exists (select 1 from bind) and bp.profile_id <> $1 and nullif($5, '') is not null and bp.owner_id = $5
    and bp.workflow_id in (select wf from moved)
  returning 1
), booked as (
  insert into clone_retirements (workflow_id, profile_id, owner_id, reason, replaced_by)
  select m.wf, (select bp.profile_id from bot_profiles bp where bp.workflow_id = m.wf limit 1), nullif($5, ''), 'replaced', $2
  from moved m where exists (select 1 from bind)
  on conflict (workflow_id) do nothing
  returning 1
)
select exists (select 1 from bind where workflow_id = $2) as bound, $2::text as workflow_id,
  (select count(*) from chunks)::int as restamped_chunks, (select count(*) from files)::int as restamped_files,
  (select count(*) from superseded)::int as superseded_rows, (select count(*) from booked)::int as booked"""


def row(pg, pid, owner, wid, bot_key='Bot0', ch='whatsapp', retired=None):
    pg.x(f"""insert into bot_profiles (profile_id, app_user_id, channel, bot_key, owner_id, workflow_id, deleted_at, deleted_reason)
             values ('{pid}', 'U', '{ch}', {f"'{bot_key}'" if bot_key else 'null'}, {f"'{owner}'" if owner else 'null'},
                     {f"'{wid}'" if wid else 'null'}, {'now()' if retired else 'null'}, {f"'{retired}'" if retired and retired != 'null' else 'null'});""")


def chunks(pg, wid, n=2, fid='F1'):
    for i in range(n):
        pg.x(f"insert into documents (content, metadata) values ('r{i}', jsonb_build_object('botWaId', '{wid}', 'botTgId', '-1', 'fileId', '{fid}'));")
    pg.x(f"insert into uploaded_files (file_id, owner_id, bot_wa_id) values ('{fid}', 'inst-A', '{wid}') on conflict do nothing;")


def plan(pg, mode, inst, pid, key='Bot0'):
    return pg.q(bind(CLONE_PLAN, [mode, inst, pid, 'whatsapp', key]))[0]


def bindc(pg, pid, new, inst, own, repl, other='-1'):
    return pg.q(bind(BIND_CLONE, [pid, new, 'U', 'whatsapp', inst, own or '', json.dumps(repl), other]))[0]


def count(pg, wid):
    return pg.q(f"select count(*)::int c from documents where metadata->>'botWaId' = '{wid}'")[0]['c']


with Case('plan: path A, own binding owned by the session install -> own_id, nothing to replace') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'P1', 'inst-A', 'WFold0000000001')
        p = plan(pg, 'session', 'inst-A', 'P1')
        c.check(p == {'planned': True, 'own_id': 'WFold0000000001', 'replace_ids': []}, p)

with Case('path A fallback (bound clone 404): bind restamps the old binding\'s chunks + files to the new clone') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'P1', 'inst-A', 'WFold0000000001'); chunks(pg, 'WFold0000000001')
        p = plan(pg, 'session', 'inst-A', 'P1')
        b = bindc(pg, 'P1', 'WFnew0000000002', 'inst-A', p['own_id'], p['replace_ids'])
        f = pg.q("select bot_wa_id from uploaded_files where file_id = 'F1'")[0]['bot_wa_id']
        c.check(b['bound'] and b['restamped_chunks'] == 2 and b['restamped_files'] == 1 and b['superseded_rows'] == 0, b)
        c.check(count(pg, 'WFold0000000001') == 0 and count(pg, 'WFnew0000000002') == 2 and f == 'WFnew0000000002', f)

with Case('path A\': new profile P2 for the same bot -> prior P1 clone replaced, chunks moved, P1 row superseded') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'P1', 'inst-A', 'WFold0000000001', retired='null')   # retired by Retire Same Bot Slot (no reason)
        row(pg, 'P2', 'inst-A', None, bot_key='Bot0'); chunks(pg, 'WFold0000000001')
        p = plan(pg, 'session', 'inst-A', 'P2')
        c.check(p == {'planned': True, 'own_id': None, 'replace_ids': ['WFold0000000001']}, p)
        b = bindc(pg, 'P2', 'WFnew0000000002', 'inst-A', p['own_id'], p['replace_ids'])
        p1 = pg.q("select superseded_by, deleted_reason, deleted_at is not null as retired from bot_profiles where profile_id = 'P1'")[0]
        c.check(b['bound'] and b['restamped_chunks'] == 2 and b['superseded_rows'] == 1, b)
        c.check(p1 == {'superseded_by': 'WFnew0000000002', 'deleted_reason': 'replaced', 'retired': True}, p1)
        r = pg.q("select workflow_id, profile_id, owner_id, reason, replaced_by from clone_retirements")
        c.check(b['booked'] == 1 and r == [{'workflow_id': 'WFold0000000001', 'profile_id': 'P1', 'owner_id': 'inst-A',
                                            'reason': 'replaced', 'replaced_by': 'WFnew0000000002'}], r)
        again = bindc(pg, 'P2', 'WFnew0000000002', 'inst-A', p['own_id'], p['replace_ids'])
        c.check(again['bound'] and again['restamped_chunks'] == 0 and again['booked'] == 0, ('retry not idempotent', again))
        # P1 comes back (Register revives it) and gets a clone of its own: the supersede mark goes away
        rb = bindc(pg, 'P1', 'WFthird00000003', 'inst-A', None, [])
        s1 = pg.q("select superseded_by, deleted_at is null as live from bot_profiles where profile_id = 'P1'")[0]
        c.check(rb['bound'] and s1 == {'superseded_by': None, 'live': True}, s1)

with Case('path A\' after a liveness / trial_expiry retirement: still replaced, the original reason is kept') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'P1', 'inst-A', 'WFold0000000001', retired='liveness')
        row(pg, 'P2', 'inst-A', None)
        p = plan(pg, 'session', 'inst-A', 'P2')
        b = bindc(pg, 'P2', 'WFnew0000000002', 'inst-A', p['own_id'], p['replace_ids'])
        p1 = pg.q("select superseded_by, deleted_reason from bot_profiles where profile_id = 'P1'")[0]
        c.check(p['replace_ids'] == ['WFold0000000001'] and p1 == {'superseded_by': 'WFnew0000000002', 'deleted_reason': 'liveness'}, (p, p1))

with Case('negatives: another install\'s row, a deleted bot (app_delete), a protected id, an empty bot key, a revoked install') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'PB', 'inst-B', 'WFforeign000001')                     # same bot key, other owner
        row(pg, 'PD', 'inst-A', 'WFdeleted000001', retired='app_delete')
        row(pg, 'PX', 'inst-A', '4wYitz5ek30SVNlT')                    # tampered: the WA template id
        row(pg, 'P2', 'inst-A', None)
        p = plan(pg, 'session', 'inst-A', 'P2')
        c.check(p['replace_ids'] == [], p)
        c.check(plan(pg, 'session', 'inst-A', 'P2', key='')['replace_ids'] == [], 'empty bot key')
        row(pg, 'PR', 'inst-R', 'WFrevoked000001')
        c.check(plan(pg, 'session', 'inst-R', 'PR')['own_id'] is None, 'revoked install reused its binding')
        c.check(plan(pg, 'session', 'inst-A', 'PX')['own_id'] is None, 'protected id reused')

with Case('negatives: a prior workflow another LIVE profile drives is never replaced') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'P1', 'inst-A', 'WFshared0000001', retired='null')
        row(pg, 'PO', 'inst-A', 'WFshared0000001', bot_key='Bot7')      # another bot of the same owner, live
        row(pg, 'P2', 'inst-A', None)
        c.check(plan(pg, 'session', 'inst-A', 'P2')['replace_ids'] == [], 'shared workflow replaced')

with Case('legacy: reuse only an owner-less binding, never replace anything') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'PL', None, 'WFlegacy0000001')
        row(pg, 'PO', 'inst-A', 'WFowned00000001')
        row(pg, 'PQ', None, 'WFlegold0000001', retired='null')
        c.check(plan(pg, 'legacy', '', 'PL')['own_id'] == 'WFlegacy0000001', 'legacy own')
        c.check(plan(pg, 'legacy', '', 'PO')['own_id'] is None, 'legacy reused an owned binding')
        c.check(plan(pg, 'legacy', '', 'PL')['replace_ids'] == [], 'legacy replaced')
        # bind in legacy mode marks nothing superseded even if handed ids
        b = bindc(pg, 'PL', 'WFlegnew0000002', '', None, ['WFlegold0000001'])
        c.check(b['bound'] and b['superseded_rows'] == 0, b)

with Case('bind: a sentinel profile binds nothing and says so (bound = false)') as c:
    with PgSession(SETUP) as pg:
        b = bindc(pg, '-1', 'WFnew0000000002', 'inst-A', None, [])
        c.check(b['bound'] is False and b['restamped_chunks'] == 0, b)

with Case('bind: the pre-F1 restamp rule (second channel joins a Telegram-only bot) still holds') as c:
    with PgSession(SETUP) as pg:
        row(pg, 'P2', 'inst-A', None)
        pg.x("insert into documents (content, metadata) values ('t', jsonb_build_object('botWaId', '-1', 'botTgId', 'TGwf00000000001'));")
        b = bindc(pg, 'P2', 'WFnew0000000002', 'inst-A', None, [], other='TGwf00000000001')
        c.check(b['restamped_chunks'] == 1 and count(pg, 'WFnew0000000002') == 1, b)

report()
```
