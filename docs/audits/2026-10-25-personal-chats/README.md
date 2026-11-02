# Пакет выкладки «Личные и рабочие чаты» (сервер)

**Статус (2026-11-02):** серверная половина **выложена на прод 2026-11-02** по этому пакету, шаги 0–8.2 (команды запускал
владелец): журнал — `docs/audits/implementation-progress.md` «Развёртывание 2026-11-02», рядом — снимки `prod-before.json` /
`prod-after.json`, точки отката `rollback-pointers.txt`, начало `deploy-start.txt`. Осталось: 8.3 (первый тик Sweep), canary шага 9
(вместе с device pass §6а), 8.2 — через сутки и после canary. Канон пакета — `Tools/n8n/workflows` + `Tools/n8n/sql` на `f67fca8b`
(коммиты пакета канон не меняют). Пакет отрепетирован на стенде из прод-состояния 2026-11-02 — раздел «Репетиция», вывод каждого
шага — `rehearsal.txt` рядом; правки после второго ревью проверены без стенда (там же, §H). **Команды на прод запускает владелец**
(кнопка Run, по одной; агенту запись в прод запрещена). Клиентская половина едет в новой
сборке, и **сервер выкладывается раньше неё** — без сервера новая сборка покажет все чаты без решения в «Личных» и не запросит для
них подсказки.

Спека — `docs/superpowers/specs/2026-10-25-personal-chats-design.md`, план — `docs/superpowers/plans/2026-10-25-personal-chats.md`,
живые проверки — `live-checks.md` рядом, архитектура и правила — CLAUDE.md, External APIs → «Личные и рабочие чаты». Сценарий
проверки на устройстве — `docs/audits/2026-10-19-device-pass-new-build.md` §6а.

## Что меняется для владельца сразу после выкладки

- ИИ-сотрудник молчит в личных чатах — контакты из телефона, переписки, начатые до подключения, группы, каналы, боты, 777000,
  «Избранное» — и с выкладки ничего нового о них не записывает; с новыми людьми и с клиентами, которых отметил владелец, работает
  как раньше. Сохранённое о таких чатах ДО выкладки остаётся на сервере — раздел «Сохранённое до выкладки».
- Решение по чату принимается один раз, по его первому входящему после выкладки; до этого чат ничего не получает. Первое сообщение
  такого чата ждёт ответа дольше на **≈ 5–8 с** (два чтения Wappi и два-три узла Postgres по ≈ 1,9 с) — только пока у чата нет
  решения.
- Панель подсказок есть только в рабочем чате; «Сводка» показывает только рабочие чаты.
- Через 2 дня после того, как рабочий чат стал личным, сохранённое о нём удаляется (шаг Sweep раз в 6 часов).
- Успешные выполнения шаблонов, клонов, Suggest Replies и двух новых workflow в журнал n8n больше не попадают (раньше хранились
  7 дней вместе с текстом сообщения); ошибочные — как прежде.

## Сохранённое до выкладки

До выкладки сервер записывал каждый чат ИИ-сотрудника один на один: историю (`chat_history_events`), память (`n8n_chat_histories`),
журнал ответа (`reply_events`), итоги «Сводки». На проде сейчас живой только тестовый ИИ-сотрудник «2» — значит, это его чаты
(появятся к выкладке другие — то же и с их чатами). После выкладки чат, решённый личным, не получает ничего нового, но **то, что о
нём сохранено ДО выкладки, остаётся на сервере**: такой чат никогда не был рабочим по решению сервера, поэтому часы удаления
(2 дня) для него не запускаются — они идут только у чата, который стал личным после рабочего периода. Убрать это сохранённое
владелец может, удалив ИИ-сотрудника в приложении: Delete Bot Files стирает память, историю, журнал ответа и итоги «Сводки» всей
его линии. Canary (шаг 9) это сохранённое не судит — только то, что записано о чате после решения по нему (память — после
отметки шага 9.0).

## Решения владельца, которые несёт пакет

- **«Да, восстанавливать»** (2026-11-01). QR по ошибке отсканировали не тем аккаунтом (другой номер), а в течение суток подключили
  прежний номер — прежнее подключение восстанавливается: клиенты прежнего номера остаются рабочими, у них ничего не удаляется.
- **«Хранить, как сейчас»** (2026-11-01). После настоящей смены номера рабочие чаты прежнего номера сохраняют записанную переписку —
  пока чат не станет личным (тогда она удаляется через 2 дня) или пока ИИ-сотрудника не удалят; отдельного удаления для них нет.

## Прод сейчас

Прод обслуживает канон `main` **`40bbf80b`** с 2026-11-01 (выкладка «поиск в прайсе перед ценой» — одно предложение в картах промптов
Create/Edit WA/TG; журнал `docs/audits/implementation-progress.md`, раздел «Развёртывание 2026-11-01»). Подтверждено офлайн, без
единого запроса к проду (`rehearsal.txt` §A):
1. последний снимок с хэшами (`docs/audits/2026-10-23-prod-after-naming.json`) — 30 из 30 графов (сохранённая и опубликованная
   версии) равны канону `007ca164`;
2. `git diff 007ca164 40bbf80b -- Tools/n8n/workflows Tools/n8n/sql` — ровно четыре workflow той выкладки (Create/Edit WA/TG), а
   `7a7a40a0` (из него владелец её выкладывал) и `40bbf80b` несут одинаковые `workflows`, `prompts` и `sql`;
3. снимок 2026-11-01 (`docs/audits/2026-11-01-prod-after-c2.txt`, текст без хэшей канонических строк): канон 30, diff 0, все 30
   строк OK; его карта промптов `e027b4b39537` = карта Create WA на `40bbf80b`; оба живых клона тестового ИИ-сотрудника «2»
   (WhatsApp `aND1y2ktZoNlszAW`, Telegram `TbQaKxJaBJkKN2nA`) несут ровно штампы шаблонов `40bbf80b` — `c0a7c5186fb2` и
   `62e09511fc94`.

**Пакет = ровно 12 workflow** (офлайн: графы и настройки канона на `40bbf80b` против канона пакета; на стенде — dry-run): 10 изменённых
+ 2 новых. Edit WA/TG в пакет **не входят**: их графы на `40bbf80b` и в пакете одинаковы (`caea05bb1dcc`, `cf3e82152efb`). Если в день
выкладки снимок шага 1 покажет другое — **стоп и разбор**.

## Что меняется на сервере

Графы — `verify-deployed.graph_hash` («до» = канон `40bbf80b` = прод, «после» = канон пакета; на стенде те же workflow дают другие
хэши — производная копия, адреса и учётки переписаны).

| Workflow | Id | Что | Генератор | Граф до → после |
|---|---|---|---|---|
| Set Workflow State | `0DP79STI3pafXO9B` | защищённый список + `crAudienceDec001`, `crChatAudience01` | `build-chat-audience.py`, `build-decide-audience.py` (патч списка) | `dcce761854bd` → `2a2b9eb5eba7` |
| **Decide Audience (новый)** | `crAudienceDec001` | подпроцесс шаблонов: решение по первому входящему чата без решения — момент подключения, факты Wappi (карточка чата, страница новейших 50 сообщений), правило, запись один раз, начало разговора в память | `build-decide-audience.py` | — → `4e3736a6bc7e` |
| **Chat Audience (новый)** | `crChatAudience01` | `POST /webhook/ChatAudience` приложения, только токен сессии: `list` / `set` / `connected`; после `set` — начало разговора и повторная подача ждущего сообщения | `build-chat-audience.py` | — → `30f1fc89207c` |
| Create WA | `XuvOp7TxOImOAmlj` | +2 защищённых id; `Bind Workflow Id` переносит решения и подключение по линии ИИ-сотрудника (с номером и запомненным подключением) | `apply-clone-lifecycle.py` (SQL — `clone_lineage.py`) | `789086fca8ff` → `58f80a16aafb` |
| Create TG | `Uz6HBBUpAiUqVysB` | то же | то же | `fed4629fee42` → `80e93626bd01` |
| Delete Bot Files | `lmjYsdNcQA2IE5rl` | +2 id; `Delete Chat Memory` удаляет решения и подключения линии (`deletedAudience` / `deletedConnections` читают `Deletion Settled?` и `Respond`) | `apply-auth-gate.py` + `apply-history-handoff.py` | `ab64a6555282` → `95b6407aa939` |
| Profile Lifecycle Sweep | `fXYpCXPKw92EzRz8` | +2 id; новая ветка «Wipe Personal Chats» — ПЕРВАЯ ветка `Every 6 Hours`, один узел Postgres (`audience.WIPE_SQL`), без флага пробного прогона, идёт дальше при ошибке | `apply-personal-wipe.py` + `apply-sweep-clone-deactivate.py` | `35be6f37abeb` → `6abfa98ea38c` |
| Wappi Proxy | `crWappiProxy0001` | `Record Owner Send` пишет только рабочие чаты (`business: false` иначе) | `build-auth-workflows.py` (условие «рабочий чат» — `audience.BUSINESS_SQL`; метка «Владелец: » — константа `apply-history-handoff.py`) | `c8bba5e407f4` → `21204c214a4a` |
| Suggest Replies | `9PTyYcelRQI7bGDb` | `Suggestion Gate` отказывает чату, который не рабочий (`personal_chat`; сбой базы — отказ, а не пропуск); `saveDataSuccessExecution: "none"` | `apply-suggest-v2.py` | `fc2f19c5c996` → `84d59c9c26bb` |
| Dashboard Outcomes | `2htWSV5IHO8E2CgB` | только рабочие чаты — и во входе классификатора, и в ответе | `apply-history-handoff.py` | `5f4cd7fb634d` → `13b0fa2cca2e` |
| WhatsApp Bot (шаблон) | `4wYitz5ek30SVNlT` | вход: `Read Reply Mode` (тот же один узел) читает решение и подключение → `Audience Known?` → `Business?` / `Decide Audience` → `Decided Business?`; запись истории — только рабочих чатов; lease отменяет ответ чату, ставшему личным (`cancelled_personal`); строку `messages/get`, где чат WhatsApp назван своим кодом `@lid`, проверки ответа считают строкой этого чата (раньше такой чат «Авто» не отвечал); `saveDataSuccessExecution: "none"`; 60 → 64 узла | `apply-history-handoff.py`, `apply-reply-pipeline-v2.py`, `apply-outcome-stamps.py` | `60aa43640382` → `3c53710fa0b6` |
| Telegram Bot (шаблон) | `4VN3gsFaC2HUYmcc` | то же + на входе отказ боту / каналу / группе Telegram | то же | `2433180b00c9` → `a1e3c21f0e79` |

- `deploy-canonical.py` новые workflow не создаёт — оба импортируются CLI (`n8n import:workflow`, id сохраняется).
- Шаблоны неактивны по контракту: `deploy-canonical` их сохраняет без публикации. Живые клоны получают новый граф и настройку
  журнала через `update-clones.py`: на проде это два клона ИИ-сотрудника «2» (штамп WhatsApp `c0a7c5186fb2` → `5c4c33e27468`, Telegram
  `62e09511fc94` → `cc77e05b537a`; +4 узла входа, 9 изменённых, промпт не меняется). Если владелец удалит «2» до выкладки, шаг 6
  найдёт 0 клонов — так тоже правильно.
- Env не меняется. **Новых учёток нет**: у обоих новых workflow две учётки — WappiAuthToken и Postgres. Канонический id
  WappiAuthToken (`EuhhqAaV56DpoqAN`) на проде не существует (там `ZowntFGvApDJ7UzQ`), а CLI-импорт подставляет учётку по имени
  только в своём проекте (a1-deploy-runbook §2.4) — поэтому импорт идёт из временной копии с id прода, после аудита. Postgres —
  канонический `vvRrFiEXzLVqKjOx`, он на проде есть.

### Миграция (аддитивная, идемпотентная)

| Файл | Что | Когда |
|---|---|---|
| `Tools/n8n/sql/2026-10-25-chat-audience.sql` | таблицы `chat_audience` (11 колонок) и `audience_connections` (6), индекс списка работы удаления `chat_audience_wipe_idx`, в проверке `reply_events.state` — `cancelled_personal`; RLS без политик, revoke у `anon` / `authenticated` | **ПЕРВОЙ, до любого workflow**: `Read Reply Mode` шаблонов fail-closed — без таблиц падает каждое входящее каждого ИИ-сотрудника; таблицы читают и Create, Delete Bot Files, Wappi Proxy, Suggest Replies, Dashboard Outcomes, оба новых workflow; «Wipe Personal Chats» без таблицы молча краснеет внутри зелёного выполнения |

Проверки — файлы рядом, только чтение, на проде через `sql-oneoff.py`:
- `precheck.sql` → `{"no_audience":true,"no_connections":true,"state_checks":"1","state_check_named":true,"personal_state":false}`.
  Миграция ищет проверку `reply_events` по имени `reply_events_state_check`: под другим именем она добавила бы вторую, а старая
  продолжала бы отказывать `cancelled_personal`. Поэтому пред-проверка требует ровно одну CHECK-проверку с этим именем.
- `postcheck.sql` → `{"audience_cols":"11","connection_cols":"6","restore_cols":"3","rls":true,"client_grants":"0","indexes":"3",
  "wipe_idx":true,"state_checks":"1","personal_state":true,"rows":"0"}`. `state_checks` — это
  `select count(*) from pg_constraint where conrelid = 'public.reply_events'::regclass and contype = 'c'`: ровно 1.
  `restore_cols` — три колонки возврата к прежнему номеру (`chat_audience.decided_phone`, `audience_connections.prev_phone` /
  `prev_connected_at`), все допускают NULL.
- Отрепетировано (`rehearsal.txt` §B): на свежем кластере — весь порядок `pg.sh` и порядок прода (база без этой миграции →
  пред-проверка → миграция → пост-проверка → повтор → та же пост-проверка), схемы двух путей совпали; обратный оператор файла
  возвращает схему прода байт в байт. На основном стенде — через Postgres-узел n8n (двойник `sql-oneoff.py`), оба раза
  `200 {"success":true}`.

## Чек-лист выкладки (прод, только после «да» владельца)

**Стоп-правило (для каждого шага).** Вывод шага отличается от «Ожидание» — **СТОП**: следующий шаг не выполнять, на проде ничего не
чинить «на ходу». Сохранить вывод; если живое состояние вредно — откат ТОЛЬКО затронутого шага (раздел «Откат»); доложить.

**Где выполнять.** Из основной копии `/Users/ayan/Projects/Automation`, **в которую влита ветка `worktree-personal-chats`** (там же
лежат ключ прода `Tools/n8n/.secrets/prod-api-key.txt` и `sql-oneoff.py`; инструменты берут ключ оттуда сами). Каждый блок — одна
команда с абсолютными путями. База везде указана явно: инструменты по умолчанию смотрят на прод, скрипты пакета без `--base` не
работают. Если ветку решено влить в main только после выкладки — те же команды из рабочей копии ветки: в путях
`/Users/ayan/Projects/Automation/.claude/worktrees/personal-chats` вместо `/Users/ayan/Projects/Automation` (кроме `sql-oneoff.py` —
он есть только в основной копии), а перед каждой командой инструментов и скриптов пакета — ключ прода в окружении:
`N8N_API_KEY="$(cat /Users/ayan/Projects/Automation/Tools/n8n/.secrets/prod-api-key.txt)" python3 …` (в `.secrets/` рабочей копии
ветки ключа прода нет).

**Сайт из `main` — не раньше шага 11.** Влитая в `main` ветка приносит в `docs/legal/privacy.html` абзац «Личные чаты», а сборка
сайта копирует этот файл как есть. Выложенный до шагов 1–9 сайт пообещал бы то, чего прод ещё не делает (сегодняшний прод отвечает
контактам и хранит их переписку), поэтому **до шага 11 сайт из `main` не выкладывать** — ни этим пакетом, ни другой задачей.

**Позже изменился только шаблон?** Тогда достаточно шагов 5–7 (только его строки шага 5, затем 6 и 7): точка отката печатается в
выводе шага 5.

### Шаг 0. Подготовка (ничего не меняет)

0.1. **Рабочая копия = отрепетированный пакет** (офлайн, только чтение). Инструменты выкладки читают РАБОЧУЮ КОПИЮ, а не коммит:
`deploy-canonical.py` выкладывает каждый `Tools/n8n/workflows/*.json` из неё, `update-clones.py` берёт из неё шаблоны, а основной
копией пользуются и другие сессии. Поэтому проверяется рабочая копия — канон, миграции, промпты, сами инструменты выкладки и папка
пакета; каждая проверка — отдельным блоком.

(a) Ни правок, ни лишних файлов. Пять файлов, которые пишет сам пакет, команда исключает по имени — `prod-before.json` (шаг 1.1),
`rollback-pointers.txt` (1.2), `deploy-start.txt` (1.3), `prod-after.json` (6.3), `canary-baseline.txt` (9.0): так 0.1 можно
повторить и посреди выкладки (ниже):
```bash
git -C /Users/ayan/Projects/Automation status --porcelain --untracked-files=all -- Tools/n8n/workflows Tools/n8n/sql Tools/n8n/prompts Tools/n8n/deploy-canonical.py Tools/n8n/update-clones.py Tools/n8n/verify-deployed.py Tools/n8n/verify-sql-comments.py Tools/n8n/inject-prompts.py docs/audits/2026-10-25-personal-chats ':(exclude)docs/audits/2026-10-25-personal-chats/prod-before.json' ':(exclude)docs/audits/2026-10-25-personal-chats/rollback-pointers.txt' ':(exclude)docs/audits/2026-10-25-personal-chats/deploy-start.txt' ':(exclude)docs/audits/2026-10-25-personal-chats/prod-after.json' ':(exclude)docs/audits/2026-10-25-personal-chats/canary-baseline.txt'
```
Ожидание: пусто — и до шага 1, и при повторе. Любая строка — ` M` (правка) или `??` (новый файл: `deploy-canonical.py` выложил бы
и его) — **СТОП**: в копии идёт чья-то работа; выяснить, до того ничего не выкладывать.

(b) Рабочая копия — не только HEAD — несёт ровно канон и инструменты, на которых пакет отрепетирован (`f67fca8b`):
```bash
git -C /Users/ayan/Projects/Automation diff --stat f67fca8b -- Tools/n8n/workflows Tools/n8n/sql Tools/n8n/prompts Tools/n8n/deploy-canonical.py Tools/n8n/update-clones.py Tools/n8n/verify-deployed.py Tools/n8n/verify-sql-comments.py Tools/n8n/inject-prompts.py
```
Ожидание: пусто. Не пусто — в `main` не влита ветка, или на `main` легло другое изменение канона или инструментов: **СТОП** (влить
ветку; если на `main` новый канон — пакет проверить заново).

(c) Графы рабочей копии = таблица «Что меняется на сервере» (против канона `40bbf80b`, которым прод обслуживается), а свои скрипты
и SQL пакета — ровно те версии, по которым написан этот README (читает только файлы и git). Закрепление — по id объекта git: его
печатает `git hash-object`, тот же id `git ls-tree` показывает у закоммиченного файла. Сначала — что сам `package_check.py` тот
(свой id файл нести не может, поэтому он закреплён здесь):
```bash
[ "$(git -C /Users/ayan/Projects/Automation hash-object docs/audits/2026-10-25-personal-chats/package_check.py)" = 72a8c381b8f8a83875f89d3f626765224492cb54 ] && echo "OK package_check.py" || echo "FAIL package_check.py"
```
Ожидание: `OK package_check.py`. `FAIL` — **СТОП**. Затем он сам:
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/package_check.py
```
Ожидание: 12 строк `OK   package …` с графами «до -> после» ровно как в таблице; `unchanged (…): 20`; две строки `OK   clones …
template stamp` (`c0a7c5186fb2 -> 5c4c33e27468`, `62e09511fc94 -> cc77e05b537a` — штампы шага 6.1); 12 строк `OK   own …` — свои
скрипты и SQL пакета (`canary-baseline.sql`, `canary.py`, `canary.sql`, `health.sql`, `newwf.py`, `postcheck.sql`, `precheck.sql`,
`readback.py`, `rollback.py`, `smoke.py`, `stand-canary.py`, `watch.py`), id каждого — из таблицы `OWN` в самом `package_check.py`;
`PACKAGE OK`, код 0. Любой `FAIL` — **СТОП** (`FAIL own` — скрипт или SQL пакета не той версии, лишний или пропал).

**0.1 повторить** — (a), (b) и (c) с теми же ожиданиями — перед шагом 5 и сразу после 6.3. Копия общая, выкладка идёт десятки
минут, и за это время в копии могла начаться чужая работа. Повтор после 6.3 доказывает, что `diff 0` в 6.3 сравнивал прод с
отрепетированным пакетом, а не с копией, которую успели поменять.

0.2. На проде есть обе учётки, на которые будет перепривязана копия (только чтение: id / тип / имя):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/newwf.py creds --base https://n8n.choosereply.com
```
Ожидание: `GET /credentials 200 | rows … | wanted [('httpHeaderAuth', 'ZowntFGvApDJ7UzQ', 'WappiAuthToken'), ('postgres',
'vvRrFiEXzLVqKjOx', 'Postgres')] | OK`.

0.3. **Строка WhatsApp, отправленная через API** (только чтение, одни признаки; нужно отдельное «да» владельца — читается его чат в
Wappi). Начало разговора, которое сервер дописывает в память, когда чат становится рабочим, отличает отправку ИИ-сотрудника от
сообщения владельца по меткам API у строки `messages/get` (`from_where: "api"`, непустой строкой `task_id`). У Telegram они есть; у
строк WhatsApp живая проверка ключей их не видела. Нужна ОДНА строка WhatsApp, отправленная через API: сообщение владельца из
приложения (это тоже отправка через API; «Авто» ради неё не включать) или прежний ответ «2» в «Авто» — в любом чате WhatsApp «2»,
кроме тех, с которых пойдёт canary шага 9. Затем (токен Wappi —
`~/.config/choosereply/wappi-token`, вне репозитория; печатаются только признаки):
```bash
python3 /Users/ayan/Projects/Automation/Tools/wappi/probe-audience-facts.py profiles
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/wappi/probe-audience-facts.py chats --ch wa --profile <id профиля WhatsApp «2» из вывода выше>
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/wappi/probe-audience-facts.py chat --ch wa --profile <тот же id> --handle <метка этого чата из вывода выше>
```
Смотреть строку отправки (`fromMe=1`, `t=` — её время). Метка есть, если у неё `from_where=api`, `wh_type=outgoing_message_api` или
`task_id=string`: тогда правило начала разговора на WhatsApp держится. `task_id` печатается своим типом JSON, а не «есть / нет»:
сервер считает меткой только непустую СТРОКУ, поэтому `task_id=number`, `blank`, `null` или `-` метки не дают. Ни одной метки —
на WhatsApp действует остаток «Начало разговора на WhatsApp» (раздел «Остатки»). Строки отправки нет среди 10 новейших сообщений
чата — повторить последнюю команду с `--limit 50`. Выкладку это не блокирует; результат (одни признаки) — в журнал выкладки.

0.4. Бэкап n8n (утренний ночной архив того же дня не затирать):
```bash
ssh choosereply 'cd ~/backups && D=$(date +%F) && { [ ! -f n8n-$D.tar.gz ] || mv n8n-$D.tar.gz n8n-$D-0315.tar.gz; } && ~/choosereply/backup.sh && mv n8n-$D.tar.gz n8n-$D-predeploy.tar.gz && ls -la ~/backups | tail -4'
```

### Шаг 1. Снимок и проверка пакета (только чтение)

1.1. Снимок «до»:
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/verify-deployed.py --base https://n8n.choosereply.com --json /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/prod-before.json
```
Ожидание (код 2 — «что-то отличается», здесь ровно пакет): `canonical 32 (diff 12)`; **DIFF ×10** — `0DP79STI3pafXO9B`,
`2htWSV5IHO8E2CgB`, `4VN3gsFaC2HUYmcc`, `4wYitz5ek30SVNlT`, `9PTyYcelRQI7bGDb`, `Uz6HBBUpAiUqVysB`, `XuvOp7TxOImOAmlj`, `crWappiProxy0001`,
`fXYpCXPKw92EzRz8`, `lmjYsdNcQA2IE5rl` (у шаблонов и Suggest Replies ещё `SETTINGS differ: saveDataSuccessExecution None (canonical
'none')`; у шаблонов `published=None` — неактивны); **MISSING ×2** — `crAudienceDec001`, `crChatAudience01`; остальные 20 — OK (Edit
WA/TG среди них). Клоны: два клона «2» — `BEHIND SETTINGS-BEHIND (saveDataSuccessExecution None, the template 'none')`,
`prompt=at_canonical/auto_parts`; архивный «13» не считается. Клонов 0 (владелец удалил «2») — допустимо. Другие клоны (ИИ-сотрудники
новой сборки) — допустимо, записать их число: их обновит шаг 6. Любое другое расхождение — СТОП.

**Исключение — расхождение только в настройках** (`SETTINGS differ …`) у workflow ВНЕ пакета, то есть бывшее на проде ещё до этой
выкладки. Его не чинить заодно: записать id и строку в журнал выкладки и спросить владельца — дальше только по его слову. Пакет
такой workflow не тронет: шаг 5 выкладывает каждый workflow отдельно, через `--only`. Сводный dry-run 1.2 покажет его лишней строкой
`would_update` (ожидаемо, не применять), а 6.3 — той же строкой DIFF (тогда там `diff 1`, код 2 — тоже ожидаемо).

1.2. Dry-run = ровно пакет, точки отката:
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com | tee /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback-pointers.txt
```
Ожидание: `{"considered": 30, "missing": 2, "up_to_date": 19, "would_update": 9, …, "failed": 0}`; `would_update` — ровно
`2htWSV5IHO8E2CgB` (16 → 16 узлов), `4VN3gsFaC2HUYmcc` и `4wYitz5ek30SVNlT` (60 → 64, +4; `activeVersionId None`), `9PTyYcelRQI7bGDb`
(26 → 26), `Uz6HBBUpAiUqVysB` и `XuvOp7TxOImOAmlj` (52 → 52), `crWappiProxy0001` (24 → 24), `fXYpCXPKw92EzRz8` (41 → 42, +1),
`lmjYsdNcQA2IE5rl` (19 → 19); `MISSING crAudienceDec001`, `MISSING crChatAudience01`. Edit WA/TG — `up to date`. У каждого `~` —
строка `rollback pointer`.
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 0DP79STI3pafXO9B --include-placeholders | tee -a /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback-pointers.txt
```
Ожидание: `~ 0DP79STI3pafXO9B 'Set Workflow State' active=True nodes 20 → 20 (+0 -0)`, `would_update 1`.

1.3. Засечь начало выкладки (его читает наблюдение, шаг 8):
```bash
date -u +%Y-%m-%dT%H:%M:%SZ > /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/deploy-start.txt
```

### Шаг 2. Миграция (через Postgres-узел n8n; `sql-oneoff.py` ждёт 35 с перед удалением временного workflow)

```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$(cat /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/precheck.sql)"
```
Ожидание: `200 {"no_audience":true,"no_connections":true,"state_checks":"1","state_check_named":true,"personal_state":false}`.
`false` у таблиц — СТОП (кто их создал?); `state_checks` не `"1"` или `state_check_named` не `true` — СТОП до миграции.
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$(cat /Users/ayan/Projects/Automation/Tools/n8n/sql/2026-10-25-chat-audience.sql)"
```
Ожидание: `200 {"success":true}`.
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$(cat /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/postcheck.sql)"
```
Ожидание: `200 {"audience_cols":"11","connection_cols":"6","restore_cols":"3","rls":true,"client_grants":"0","indexes":"3",
"wipe_idx":true,"state_checks":"1","personal_state":true,"rows":"0"}`.
Откат: не нужен (старые workflow таблицы не читают). Обратный оператор — в конце файла миграции, только после отката всех читателей.

### Шаг 3. Set Workflow State — защита новых id ДО их импорта

До этого шага вызов без токена (legacy-окно открыто до 2026-12-31) мог бы выключить или удалить новый workflow.
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 0DP79STI3pafXO9B --include-placeholders --apply --yes
```
Ожидание: `published by the PUT (served version = the update) — no publish call, no deactivate window`, `read-back: ok`, `updated 1`,
`failed 0` (`__HDR_CRED__` инструмент привязывает к учётке `n8n Admin API` живого Set Workflow State).
Откат: `rollback.py publish … --id 0DP79STI3pafXO9B --version <точка отката из rollback-pointers.txt>` (раздел «Откат»; в полном
откате — самым последним, после удаления новых workflow).

### Шаг 4. Два новых workflow: временная копия → импорт → GET → публикация

Порядок обязателен: **Decide Audience опубликован ДО шаблонов, клонов и любого Create.** n8n 2.36.7 отказывается публиковать
workflow, узел Execute Workflow которого зовёт неопубликованный подпроцесс («Cannot publish workflow: Node "Decide Audience"
references workflow crAudienceDec001 … which is not published») — обновление живых клонов в шаге 6 и каждый новый ИИ-сотрудник из
Create упали бы на публикации (на стенде проверено обоими путями, `rehearsal.txt` R7). А шаблон, который зовёт отсутствующий
подпроцесс, оставил бы без ответа каждый чат без решения.

4a. Временные копии + аудит (офлайн, канон не трогается; WappiAuthToken → `ZowntFGvApDJ7UzQ`):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/newwf.py copy --target prod
```
Ожидание — ровно (тот же код офлайн выдал это на репетиции):
```
crAudienceDec001-Decide_Audience.json [('httpHeaderAuth', 'ZowntFGvApDJ7UzQ', 'WappiAuthToken'), ('postgres', 'vvRrFiEXzLVqKjOx', 'Postgres')] graph 4e3736a6bc7e (canon 4e3736a6bc7e) id crAudienceDec001 active False saveDataSuccessExecution 'none' -> OK
crChatAudience01-Chat_Audience.json [('httpHeaderAuth', 'ZowntFGvApDJ7UzQ', 'WappiAuthToken'), ('postgres', 'vvRrFiEXzLVqKjOx', 'Postgres')] graph 30f1fc89207c (canon 30f1fc89207c) id crChatAudience01 active False saveDataSuccessExecution 'none' -> OK
copies in /tmp/cr-personal-chats-import
```
Любой `FAIL` — СТОП до импорта.

4b. Импорт ИЗ КОПИЙ (id сохраняются; копии на VPS и в контейнере удаляются; форма команд — выполненная на проде 2026-10-15 и
2026-10-19):
```bash
for f in crAudienceDec001-Decide_Audience crChatAudience01-Chat_Audience; do scp -q "/tmp/cr-personal-chats-import/$f.json" "choosereply:/tmp/$f.json" && ssh choosereply "cd ~/choosereply; docker compose cp /tmp/$f.json n8n:/tmp/$f.json && docker compose exec -T -u root n8n chown node:node /tmp/$f.json && docker compose exec -T n8n n8n import:workflow --input=/tmp/$f.json; echo \"import $f rc=\$?\"; docker compose exec -T -u root n8n rm -f /tmp/$f.json; rm -f /tmp/$f.json"; done
```
Ожидание: дважды «Successfully imported 1 workflow.» и `import … rc=0` (строка CLI «Error tracking disabled because this release is
older than 6 weeks.» — служебная).

4c. GET до включения (только чтение):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/newwf.py get --base https://n8n.choosereply.com
```
Ожидание: `crAudienceDec001 GET 200 | active False | creds ['ZowntFGvApDJ7UzQ', 'vvRrFiEXzLVqKjOx'] | OK` и то же для
`crChatAudience01`. Любой другой id (в том числе уцелевший `EuhhqAaV56DpoqAN`) — НЕ включать, СТОП.

4d. Публикация подпроцесса через API (n8n проверяет граф до того, как начнёт его обслуживать):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/newwf.py activate --base https://n8n.choosereply.com --id crAudienceDec001
```
Ожидание: `crAudienceDec001 activate 200 | active True | published=stored True | published graph 4e3736a6bc7e (canon 4e3736a6bc7e) |
creds ['ZowntFGvApDJ7UzQ', 'vvRrFiEXzLVqKjOx'] | OK`.

4e. Включение вебхука:
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/newwf.py activate --base https://n8n.choosereply.com --id crChatAudience01
```
Ожидание: то же с `published graph 30f1fc89207c (canon 30f1fc89207c)`.

4f. Smoke сразу после включения (ничего не пишет):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/smoke.py --base https://n8n.choosereply.com
```
Ожидание: `SMOKE OK` — 8 строк 7.1 `[404] Did you mean to make a POST request?`; 7.2 `[401] … unauthorized:missing`; 7.3 `[401] …
unauthorized:bad_signature`; 7.4 ×2 `[400] {"success": false, "error": "protected_workflow"}`.
Откат шага 4 — раздел «Откат» (новые workflow откатываются после шаблонов, клонов и изменённых workflow; позже них — только Set
Workflow State).

### Шаг 5. Изменённые workflow — по одному, каждый с чтением назад

Перед первым блоком — шаг 0.1 (a)–(c) ещё раз, ожидания те же.

Порядок: Create WA → Create TG → Delete Bot Files → Profile Lifecycle Sweep → Wappi Proxy → Suggest Replies → Dashboard Outcomes →
шаблон WhatsApp → шаблон Telegram. Ожидание у семи активных: `published by the PUT (served version = the update) — no publish call,
no deactivate window`, `read-back: ok`, `{"considered": 1, …, "updated": 1, …, "failed": 0}`; у Create и Sweep инструмент привязывает
`__HDR_CRED__` к `n8n Admin API` и отказывает непривязанным. У двух шаблонов (`active=False`) — `read-back: ok` без строки
«published» (неактивны по контракту), `updated 1`. Перед PUT каждый печатает свою `rollback pointer`.
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only XuvOp7TxOImOAmlj --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only Uz6HBBUpAiUqVysB --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only lmjYsdNcQA2IE5rl --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only fXYpCXPKw92EzRz8 --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only crWappiProxy0001 --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 9PTyYcelRQI7bGDb --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 2htWSV5IHO8E2CgB --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 4wYitz5ek30SVNlT --apply --yes
```
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 4VN3gsFaC2HUYmcc --apply --yes
```
Откат — по точке отката каждого (раздел «Откат»; шаблоны — только каноном).

### Шаг 6. Клоны, сверка, настройки

6.1. Клоны — dry-run по НАСТОЯЩЕМУ инвентарю:
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/update-clones.py --base https://n8n.choosereply.com
```
Ожидание: `prompt map applied: … as introduced in fce9987b (sha256 e027b4b39537)`; у каждого клона «2» `→ +4 -0 ~9 conn=changed`,
`add: ['Audience Known?', 'Business?', 'Decide Audience', 'Decided Business?']`, `change: ['Acquire Send Lease', 'Cancel Label',
'Final Check', 'Latest+Combine', 'Outcome', 'Read Reply Mode', 'Reconcile Check', 'Record Outgoing', 'Suppressed?']`,
`settings: saveDataSuccessExecution None → 'none' (the template's)`, промпт — без изменений (`at_canonical/auto_parts`); архивный «13»
пропущен. Клонов 0 — применять нечего, перейти к 6.3.

6.2. Применить (точки отката клонов — в файл):
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/update-clones.py --base https://n8n.choosereply.com --apply --yes | tee -a /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback-pointers.txt
```
Ожидание: у каждого `rollback pointer … before the update`, `published by the PUT (served version = the update) — no
deactivate/activate window`, `read-back: ok`; `failed 0`. Затем блок 6.1 ещё раз → у всех `up to date`.

6.3. Снимок «после»:
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/verify-deployed.py --base https://n8n.choosereply.com --json /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/prod-after.json
```
Ожидание: код 0, `canonical 32 (diff 0)`, хэши «после» = таблица; клоны `AT-CANONICAL`; `settings=True` у всех (кроме
расхождения в настройках вне пакета, записанного в 1.1 по слову владельца, — тогда `diff 1` и та же строка). Сразу затем — шаг
0.1 (a)–(c) ещё раз, ожидания те же: копия, с которой сравнивал 6.3, — всё ещё отрепетированный пакет.

6.4. Настройки — явным чтением, по одному workflow:
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/readback.py --base https://n8n.choosereply.com
```
Ожидание: `saveDataSuccessExecution = 'none' (want 'none') OK` у `4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`, `9PTyYcelRQI7bGDb`,
`crAudienceDec001`, `crChatAudience01`, `crWappiProxy0001`; `9PTyYcelRQI7bGDb … executionTimeout = 20 (want 20) OK`; у каждого живого
клона `saveDataSuccessExecution = 'none' (want 'none') OK`; `READ-BACK OK`. (verify-deployed 6.3 уже сравнил ключи настроек, которые
задаёт канон; ключ, которого канон не задаёт, он печатает «settings not in the canon» и не считает: n8n 2.36.7 сливает настройки при
PUT, и снять такой ключ выкладка не может. Это чтение — отдельная, явная проверка той единственной защиты, что не даёт тексту
личного чата осесть в журнале выполнений.)

### Шаг 7. Smoke

```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/smoke.py --base https://n8n.choosereply.com --session
```
Ожидание: 7.1–7.4 как в шаге 4f; 7.5 `AuthSession bootstrap … [200] {"success": true, "installId": "…", "token": "<hidden>"}`;
7.6 `[400] {"success": false, "error": "bad_request", "reason": "profileIds"}` — гейт принял настоящий токен сервера, Validate
сработал; `SMOKE OK`. С `--session` пишется одна строка `installs` — анонимная установка без платёжного id и без ИИ-сотрудников (как
smoke выкладки 2026-09-24); без `--session` не пишется ничего. Отозвать её (по желанию):
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "with r as (update installs set revoked_at = now() where platform = 'smoke' and app_version = 'personal-chats-pkg' and revoked_at is null returning 1) select count(*) as revoked from r"
```
Ожидание: `200 {"revoked":"1"}` — по одной на каждый прогон с `--session` (на стенде запрос отработал через Postgres-узел n8n —
`rehearsal.txt` §G).
**`list` = 200** отвечает только установке, у которой есть ИИ-сотрудник: это проверяет canary (шаг 9, п. 9.6) — и **без него сборку
не выпускать**.

### Шаг 8. Наблюдение

8.1. Ошибки с начала выкладки — у каждого workflow пакета и по всему инстансу (успешные выполнения шаблонов, клонов, Suggest Replies и
двух новых workflow не сохраняются — смотрим ошибочные):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/watch.py --base https://n8n.choosereply.com
```
Ожидание: `GET 200 | error executions since …: []` ×10 и `whole instance … []`. Иначе — СТОП, до разбора ничего не менять. У
каждого ошибочного выполнения скрипт печатает строку `error execution <id> … | node <узел> (<тип>) | error <вид ошибки> | level
<уровень>` — **в журнал выкладки пишется только это**: узел и вид ошибки. Текст ошибки может нести текст чата, а журнал лежит в
публичном репозитории; полный текст читать в интерфейсе n8n (Executions), он остаётся на сервере.

Журнал контейнера — с момента начала выкладки (`deploy-start.txt`, шаг 1.3), а не за последние полчаса: выкладка может идти дольше.
```bash
ssh choosereply "cd ~/choosereply && docker compose logs --since $(cat /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/deploy-start.txt) n8n | grep -iE 'error|insights' | tail -20"
```
Ожидание: пусто (временные workflow `sql-oneoff` ждут 35 с перед удалением — цикл insights не возникает). Строки есть — в журнал
выкладки только их число и вид (например, «Error while saving insights»), без текста.

8.2. Здоровье Decide Audience — его сбой не оставляет следа (вызов из шаблона идёт дальше при ошибке, успешные выполнения не
сохраняются): каждый новый клиент молча остался бы без ответа.
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$(cat /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/health.sql)"
```
Ожидание: `200 {"suspicious":"0","profiles":[]}`. Повторить через сутки и после canary. Профиль в списке — строку подключения
создал сам Decide Audience больше часа назад, а решений нет ни одного: посмотреть ошибки его клона и `crAudienceDec001` (8.1).
Профиль, в чат которого после первых 30 минут никто не писал, тоже попадёт в список — это не сбой.

8.3. Первый тик Sweep после выкладки (00:00 / 06:00 / 12:00 / 18:00 Алматы = 19 / 01 / 07 / 13 UTC; его успешные выполнения
сохраняются):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/watch.py --base https://n8n.choosereply.com --sweep
```
Ожидание: `execution … status success | Wipe Personal Chats: [{"wiped_chats": 0, "deleted_rows": 0}] | OK` (личных чатов старше
2 дней ещё нет). Ошибка узла (`{"error": "<вид>"}` или `{"keys": […]}` — текст ошибки скрипт не печатает) или `did not run` — СТОП и
разбор: ветка идёт дальше при ошибке, поэтому видна только здесь. До тика — «no execution yet»: повторить после него.

### Шаг 9. Canary на тестовом ИИ-сотруднике «2» — до выпуска любой сборки

Это первый вызов подпроцесса клоном, созданным через публичный API, на проде (у «2» клоны создал Create) — он и проверяется.
Действия на телефонах — владелец; чтение — запросы ниже (одни признаки: ни номеров, ни имён, ни текстов). Canary судит то, за что
отвечает выкладка: о личном чате после решения по нему не записано ничего.
- **История и журнал ответа** личного чата сверяются со временем решения по ЭТОМУ чату. Решения идут с выкладки, а шаг 9 бывает
  днями позже — так видна и строка, записанная между решением и отметкой 9.0. Всё, что «2» сохранил о чате до решения, остаётся на
  сервере и сюда не входит (раздел «Сохранённое до выкладки»).
- **Память** (`n8n_chat_histories`) колонки времени не имеет: «после» для неё узнаётся только по id, взятому заранее, — она
  судится от отметки 9.0. Каждый, кто пишет память, в том же выполнении пишет историю или журнал ответа, поэтому запись о личном
  чате и до отметки покажут они.
- От отметки судятся и решения, рабочий чат 9.1, журнал ответа по состояниям.
- **Почему не отметка шага 1.3** (начало выкладки): до шага 6.2 «2» обслуживают прежние клоны, а они записывают каждый чат один на
  один. Строки, записанные ими за время выкладки о чате, который потом решён личным, такая отметка посчитала бы, и 9.5 остановил бы
  верную выкладку (проверено на временном кластере Postgres: `rehearsal.txt` §H). У времени решения такого окна нет.

**Условия — проверить в день canary:**
- **«2» может отвечать.** В приложении: «Подписка» — не «Истекла», диалогов осталось больше нуля; на карточке «2» нет «Канал
  отключён» и «Отключён · …». План «2» мог быть только пробным или купленным в песочнице TestFlight — оба живут дни, а когда план
  кончается, Profile Lifecycle Sweep отключает ИИ-сотрудника. Без диалогов 9.1 закончится `quota_denied` (видно в 9.5) — это не сбой
  функции.
- **«2» нет** (удалён или отключён) — нужен тестовый ИИ-сотрудник из НОВОЙ сборки (TestFlight): подключить канал и выждать 30 минут
  после подключения — первые 30 минут чат без решения не получает ничего. Дальше всё то же с ним вместо «2»; у его строки подключения
  в 9.5 `number_known: true`.
- **«Авто» — только на время 9.1.** «2» подключён к настоящему аккаунту и в «Авто» ответил бы любому новому человеку, а рабочее
  правило — ИИ-сотрудник вне проверки сам не отвечает. Поэтому «Авто» включается прямо перед 9.1 и выключается («Выбрать») сразу,
  как пришёл ответ.
- **Отправитель 9.1** — номер, которого НИКОГДА не было в контактах телефона «2» и который раньше этому аккаунту не писал (второй
  телефон). **Отправитель 9.2** — номер из контактов телефона «2», который не писал «2» с начала выкладки (`deploy-start.txt`): чат,
  решённый раньше, нового решения не получит и в 9.5 не появится. Писал ли он «2» до выкладки — не важно.

- 9.0. Отметка начала — прямо перед 9.1 (только чтение: момент и последний id памяти; команда идёт ≈ 40 с — временный workflow
  удаляется через 35 с):
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$(cat /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/canary-baseline.sql)" | tee /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/canary-baseline.txt
```
Ожидание: `200 {"baseline_at":"<время>Z","memory_max_id":"<число>"}`.
- 9.1. В приложении включить «Авто» у «2». Со второго телефона (см. условия) написать «2». Ожидание: «2» отвечает. Как только ответ
  пришёл — выключить «Авто» (режим «Выбрать»).
- 9.2. Уже в «Выбрать» — написать «2» с номера из контактов (см. условия). Ожидание: ответа нет. В «Выбрать» ответа не было бы и у
  рабочего чата, поэтому молчание само ничего не доказывает. Что чат 9.2 не стал рабочим, всегда различает **9.3**: сервер даёт
  подсказки только рабочему чату, остальным отказывает (`personal_chat`). 9.5 — доказательство хранения: о личном чате после
  решения не записано ничего (сообщение рабочего чата в «Выбрать» легло бы в историю и память). Строка `personal | default |
  contact` в 9.5 этот чат не называет: её может дать и другой контакт, написавший «2» за время canary.
- 9.3. В приложении на телефоне владельца открыть чат из 9.2 и запросить подсказки. Ожидание: подсказок нет (сборка с main без этой
  функции покажет «Не удалось загрузить» — это отказ сервера `personal_chat`; новая сборка панель в личном чате не показывает). Это
  и есть проверка, которая различает всегда: рабочему чату тот же запрос даёт подсказки. С новой сборкой (тестовый ИИ-сотрудник
  из TestFlight) приложение не просит у сервера подсказки для такого чата — признак тогда в шапке чата 9.2: переключателя «Авто»
  нет (он появляется только у чата, который сервер решил рабочим, — см. 9.6).
- 9.4. (по желанию) То же в Telegram «2» — 9.1 в «Авто», затем «Выбрать» и 9.2 — до 9.5.
- 9.5. Чтение. Текст запроса собирает `canary.py` из отметки 9.0 (без неё запрос не выполняется):
```bash
SQL="$(python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/canary.py /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/canary-baseline.txt)" && python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$SQL"
```
Ожидание (решения, рабочий чат и журнал ответа — после отметки 9.0; личные чаты — после решения по каждому):
  - `decisions` — у профиля WhatsApp «2» есть `business | default | new` (9.1) и `personal | default | contact` (9.2), `chats` ≥ 1 у
    каждой. Лишние строки или чаты — за время canary «2» писали и другие люди: ожидаемо (поэтому вторая строка и не доказывает, что
    решён именно чат 9.2, — это доказывает 9.3);
  - `connections` — строка профиля «2» (`number_known: false`: сборка с main о подключении не сообщает, строку создал Decide
    Audience; у ИИ-сотрудника новой сборки — `true`);
  - `personal_after` — `{"history": 0, "memory": 0, "ledger": 0}`: о личных чатах после решения по ним не записано ничего (память —
    после отметки) — **главное правило**;
  - `business_after` — `history` и `memory` больше 0: рабочий чат 9.1 записывается, как прежде;
  - `ledger_after` — есть `sent` (ответ 9.1); `quota_denied` вместо него — у плана «2» кончились диалоги (см. условия);
  - `personal_rows_before` — любые числа: это сохранённое о нынешних личных чатах ДО решения по ним (память — до отметки); canary
    его не судит.

  `personal_after` не нули — СТОП и разбор (что-то записало личный чат после его решения; законно так бывает только в гонке длиной в
  секунды на самом переходе — ответ или запись прежнего клона, начатые, пока чат ещё не был личным). Затем 8.2 ещё раз —
  `suspicious "0"`.
- 9.6. **Ворота выпуска сборки: `list` = 200.** Новая сборка на устройстве владельца (device pass §6а), после запуска. Решающий
  признак — открыть чат из 9.1: **в шапке есть переключатель «Авто»**. Он появляется только у чата, который сервер решил рабочим, а это
  решение приложение узнаёт только из `list`. По спискам видно не всё: чат без решения приложение раскладывает своим правилом. У «2»
  решающим будет и список — чат 9.1 в рабочем списке: «2» подключён до этой сборки и о подключении не сообщает, поэтому момент
  подключения к нему тоже приходит только из `list`, а без момента приложение считает историю чата неизвестной и кладёт его в
  «Личные». Чат 9.2 в «Личных» сам по себе не доказывает ничего: контакт приложение кладёт туда и без сервера. Пока признак не
  подтверждён — **сборку не выпускать**.

### Шаг 10. Журнал и отметки «DEPLOYED»

- `docs/audits/implementation-progress.md` — новый раздел «Развёртывание <дата> — «Личные и рабочие чаты»» (таблица шагов, снимки
  `prod-before.json` / `prod-after.json`, `rollback-pointers.txt`, `deploy-start.txt`, `canary-baseline.txt`, результат 0.3, smoke,
  canary).
- Отметки «in canon, NOT deployed» → «DEPLOYED to prod <дата>»: `grep -n "NOT deployed" /Users/ayan/Projects/Automation/CLAUDE.md
  /Users/ayan/Projects/Automation/Tools/n8n/README.md` — каждая строка «Личных и рабочих чатов» (на коммите пакета — CLAUDE.md 141,
  149 ×3, 151, 153, 200, 215, 216, 221, 259, 260, 261, 276; Tools/n8n/README.md 140 ×2, 147, 155, 156, 158, 159, 162, 165, 171, 172 — если
  строки сдвинулись, по выводу grep); `docs/audits/2026-10-19-device-pass-new-build.md` — §0 («сервер на прод НЕ выложен (на
  2026-11-02)» → «сервер выложен <дата>») и заголовок §6а («сервер ещё не выложен» → «сервер выложен <дата>»); строка «Статус» этого
  файла. Отметки «client, new build» остаются до сборки. В журнал — только признаки: ни текстов чатов, ни номеров, ни имён (у ошибок —
  узел и вид, шаг 8.1). Коммит `deploy(prod): …` (точечные пути).

### Шаг 11 (по желанию). Сайт: абзац «Личные чаты» в политике конфиденциальности

Абзац в `docs/legal/privacy.html` описывает поведение сервера — опубликованный ДО выкладки, он был бы неправдой (сегодняшний прод
отвечает контактам и хранит их). Публикуется только после шагов 1–9 и **после одобрения владельцем формулировки** (черновик уже в
файле); до этого шага сайт из `main` не выкладывается вовсе (абзац «Сайт из `main` — не раньше шага 11» в начале чек-листа).
Вопрос владельцу: поднять ли строку «Действует с 27 августа 2026 г. · Версия 1.0» (§10 политики обещает сообщать об изменениях там).
```bash
cd /Users/ayan/Projects/Automation/Tools/landing && node tools/build.mjs && node tools/check.mjs
```
```bash
/Users/ayan/Projects/Automation/Tools/landing/tools/deploy.sh
```
Ожидание: только строки `DRY: …` (сборка копирует `docs/legal/privacy.html` как есть). Затем — по отдельному «да»:
```bash
/Users/ayan/Projects/Automation/Tools/landing/tools/deploy.sh --go
```
Ожидание: бэкап `site.bak-<stamp>` и команда отката напечатаны, smoke `/`, `/privacy.html`, `/terms.html` — 200, n8n `/healthz` — как
до выкладки.

## Откат

Порядок — обратный выкладке (шаги 6 → 5 → 4 → 3): шаблоны и клоны → изменённые включённые workflow (кроме Set Workflow State) →
новые workflow → Set Workflow State — самым последним и по желанию; миграция остаётся.
`<id>` и `<точка отката>` — из `rollback-pointers.txt` (строки `rollback pointer: versionId …` шагов 1.2 и 6.2). Каждая команда —
отдельный блок, по одной, как при выкладке; `rollback.py` без явного `--base` не работает, ключ берёт так же, как остальные скрипты
пакета.

**По точке — быстро** (секунды; без окна, когда вебхука нет: n8n сначала проверяет версию, при отказе продолжает служить прежняя).
Для ВКЛЮЧЁННОГО workflow — Create WA/TG, Delete Bot Files, Sweep, Wappi Proxy, Suggest Replies, Dashboard Outcomes, живые клоны;
тем же блоком — и Set Workflow State, но только в самом конце, после удаления новых workflow:
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback.py publish --base https://n8n.choosereply.com --id <id> --version <точка отката>
```
Ожидание: `<id> publish 200 | active True | served <точка отката> | stored <новая версия> | published graph <граф «до» из таблицы; у
клона — прежний> | OK`. Сохранённой остаётся новая версия: verify-deployed покажет DIFF (опубликованный граф не канон), а следующий
`deploy-canonical.py --apply` с каноном пакета опубликует её снова. У клона откат по точке недолог: сохранение настроек ИИ-сотрудника
в приложении (Edit WA/TG читает сохранённую версию клона и записывает её назад) опубликует новую версию — за точкой у клона следует
откат каноном (ниже). Шаблоны по точке НЕ откатываются: они выключены по контракту, и публикация ВКЛЮЧИЛА бы шаблон — `rollback.py`
такой вызов отклоняет (как и вызов для любого выключенного workflow). Настройка журнала не версионируется: откат версии оставляет
`saveDataSuccessExecution: "none"` — это безопасно.

**Каноном `40bbf80b` — надёжно** (и сохранённая, и опубликованная версии — прежние). Из отдельной временной копии на этом коммите:
основная копия не трогается, а инструменты — те, которыми прод выкладывался 2026-11-01. Ключ прода — из окружения (в новой копии
папки `.secrets/` нет). Копия берётся без файлов LFS (`GIT_LFS_SKIP_SMUDGE=1`): вместо картинок и шрифтов Unity в ней лежат их
короткие указатели — `deploy-canonical.py` и `update-clones.py` ни одного такого файла не читают (в `Tools/n8n` под LFS только две
картинки-образца `fixtures/*.jpg` для стенда), и посреди отката git ничего не тянет из сети (≈ 6 000 файлов, ≈ 110 МБ, секунды). Инструменты запускаются с `PYTHONDONTWRITEBYTECODE=1` — Python не оставляет в копии `__pycache__`. Если
`/tmp/cr-rollback-40bbf80b` остался от прошлой попытки — сначала блок «убрать временную копию» ниже.
```bash
GIT_LFS_SKIP_SMUDGE=1 git -C /Users/ayan/Projects/Automation worktree add --detach /tmp/cr-rollback-40bbf80b 40bbf80b
```
Ожидание: `Preparing worktree (detached HEAD 40bbf80b)`, `Updating files: 100% (5927/5927), done.`, `HEAD is now at 40bbf80b
docs(deploy): C2 «поиск в прайсе перед ценой» выложен на прод 2026-11-01` — ни строки о загрузке.
Шаблон (`4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`) или любой изменённый workflow, кроме Set Workflow State (его черёд — в самом конце,
ниже), — по одному, `--only <id>`:
```bash
PYTHONDONTWRITEBYTECODE=1 N8N_API_KEY="$(cat /Users/ayan/Projects/Automation/Tools/n8n/.secrets/prod-api-key.txt)" python3 /tmp/cr-rollback-40bbf80b/Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only <id> --apply --yes
```
Ожидание: как в шаге 5 — `read-back: ok`, `updated 1`, `failed 0` (шаблон сохраняется без публикации). Затем клоны — по шаблонам
`40bbf80b`:
```bash
PYTHONDONTWRITEBYTECODE=1 N8N_API_KEY="$(cat /Users/ayan/Projects/Automation/Tools/n8n/.secrets/prod-api-key.txt)" python3 /tmp/cr-rollback-40bbf80b/Tools/n8n/update-clones.py --base https://n8n.choosereply.com --apply --yes
```
Ожидание: у каждого клона `published by the PUT …`, `read-back: ok`; `failed 0`. Настройка журнала у шаблонов и клонов остаётся
`'none'`: n8n сливает настройки при PUT, а канон `40bbf80b` её не называет — это безопасно. Почему не из основной копии с каноном
пакета и подменённым шаблоном: её `update-clones.py` сверяет настройку журнала с шаблоном, а шаблон `40bbf80b` её не задаёт — граф
откатится, но у каждого клона будет `read-back: settings differ from the template's: saveDataSuccessExecution 'none' (want None)` и
`failed` (код 1), а следующий dry-run снова покажет клоны со строкой `settings: saveDataSuccessExecution 'none' → None (the
template's)`. Чинить там нечего — это та же безопасная `'none'`.
После отката убрать временную копию. `--force` касается только её: без него git отказывается удалять копию, в которой что-то
осталось (например, `__pycache__` от другого Python), — `contains modified or untracked files, use --force to delete it`:
```bash
git -C /Users/ayan/Projects/Automation worktree remove --force /tmp/cr-rollback-40bbf80b
```
Ожидание: без вывода; `git -C /Users/ayan/Projects/Automation worktree list` её больше не показывает. (Обе команды — добавление
копии без LFS и удаление с `--force`, в том числе с оставленным `__pycache__`, — выполнены 2026-11-02 в черновой папке, без
единого обращения к сети: `rehearsal.txt` §H.)

**Новые workflow — после шаблонов, клонов и изменённых workflow**: клон, который зовёт отсутствующий Decide Audience, оставляет без
ответа каждый чат без решения. По одному блоку, в этом порядке:
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback.py deactivate --base https://n8n.choosereply.com --id crChatAudience01
```
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback.py deactivate --base https://n8n.choosereply.com --id crAudienceDec001
```
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback.py delete --base https://n8n.choosereply.com --id crChatAudience01
```
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/rollback.py delete --base https://n8n.choosereply.com --id crAudienceDec001
```
Ожидание: `<id> deactivate 200 | active False | OK`; `delete` выжидает 36 с (правило insights: ≥ 35 с после последнего выполнения),
затем `<id> DELETE 200 | GET 404 | OK`; включённый workflow `delete` не удаляет.

**Set Workflow State — самым последним, по желанию.** Его защита двух новых id должна стоять, пока они есть (её ставили ДО их импорта,
шаг 3); после их удаления она ничего не делает, поэтому откатывать его не обязательно. Если откатывать — только теперь, блоком «По
точке» выше: `--id 0DP79STI3pafXO9B`, точка отката — строка второй команды шага 1.2 в `rollback-pointers.txt`.

- **Миграция остаётся** (старый канон таблиц не читает). **Обратный оператор** (в конце файла миграции) — только когда ни один
  выложенный workflow не читает таблицы и ни одна строка `reply_events` не в `cancelled_personal`; на свежем кластере он вернул схему
  прода байт в байт (`rehearsal.txt` §B).
- **Частичный откат и его эффект:** только шаблоны и клоны — ИИ-сотрудники снова отвечают всем (как сегодня), решения остаются в
  таблицах; только Suggest Replies — панель снова есть во всех чатах; только Dashboard Outcomes — «Сводка» снова со всеми чатами;
  только Create — переподключение не переносит решения (новый профиль решает заново); только Delete Bot Files — решения удалённого
  ИИ-сотрудника остаются (сироты без поведения); только Sweep — личные чаты не стираются через 2 дня.
- **После выпуска новой сборки — только вперёд.** `crChatAudience01` и вход шаблонов чинятся, а не откатываются, без обновления
  клиента: приложение на сервере без вебхука показывает все чаты личными, а старые шаблоны отвечают всем.

## Что НЕ выкладывается

- **Клиент** — новая сборка (`Tools/store/inspect-artifact.sh` → device pass §6а), только после сервера и шага 9.
- Edit WA/TG (равны проду), env, учётки, промпты, остальные workflow; сайт — отдельный шаг 11.

## Клиенты без этой функции после выкладки сервера

Сюда входит и сборка с `main` на телефоне владельца. После выкладки в ней:
- контакты и чаты, начатые до подключения, молчат, и перенести их в рабочие нечем (папки «Личные» в ней нет);
- «Выбрать» в таком чате показывает «Не удалось загрузить» (сервер отказывает `personal_chat`);
- свой «Авто» чата там ничего не делает (личный чат важнее);
- смену номера сервер не видит (такая сборка не сообщает о подключении).

Поэтому **ни одна сборка без этой функции не выпускается после выкладки сервера** (одобренная 1.0 (4) не выпускается и так —
решение 2026-10-09).

## Остатки и принятые ограничения

- **Ошибочное подключение (A → B → A), словами владельца:** «QR отсканировали не тем аккаунтом — подключите прежний номер в тот же
  день: прежнее подключение вернётся, клиенты останутся рабочими». Возврат позже суток или через третий номер (A → B → C → A) —
  новое подключение: каждый прежний клиент решается заново по следующему сообщению (обычно личным — по истории), а у бывших рабочих
  чатов через 2 дня удаляется сохранённое; их нужно отметить на экране «Кто из них клиенты?». После возврата нет окна в 30 минут:
  чат без действующего решения решается по следующему сообщению сразу, хотя контакты в WhatsApp могут ещё догружаться. Подробно —
  CLAUDE.md, «Known» (f).
- **Начало разговора на WhatsApp** (до результата 0.3). Если строки WhatsApp, отправленные через API, не несут меток, сервер не
  отличит прежний ответ ИИ-сотрудника, ещё лежащий на странице Wappi, от сообщения владельца с телефона: в чате, который снова стал
  рабочим после удаления через 2 дня, такой ответ может лечь в память как «Владелец: …». На Telegram метки есть.
- **Сохранённое до выкладки** о чатах, решённых личными, остаётся на сервере до удаления ИИ-сотрудника (раздел «Сохранённое до
  выкладки»): часы удаления идут только у чата, который был рабочим.
- **Задержка** первого сообщения чата без решения — ≈ 5–8 с на проде.
- **Окно первых 30 минут** после подключения нового канала: чат без решения не получает ни ответа, ни подсказок. Остаток — контакт,
  который WhatsApp догрузил позже окна, при первом сообщении станет рабочим (спека §6).
- **«Сводка» и заявки:** вебхук Handoffs ещё перечисляет открытые заявки чата, ставшего личным (приложение их скрывает; удаление через
  2 дня их стирает).
- **Журнал выполнений:** успешные выполнения ботов, клонов, Suggest Replies и двух новых workflow не сохраняются — отладка по ошибкам
  и по базе (`reply_events`, `chat_history_events`, `chat_audience`).
- **Ветка прайс-листов:** её «поиск в прайсе перед ценой» уже на `main` и влит в эту ветку (`f67fca8b`), канон пересобран генераторами. Если до выкладки на `main`
  ляжет ещё одно изменение канона — влить его и пересобрать канон генераторами, никогда не сливать JSON текстом; затем шаг 0.1 и
  ожидания шага 1 — заново.
- Остальное — CLAUDE.md, «Личные и рабочие чаты» → «Known, open» (a)–(i).

## Репетиция на стенде (2026-11-02)

Стенд: n8n 2.36.7 с task runners (как прод), Postgres `pg.sh` (5433; PG 16, прод — 17), Wappi-мок 127.0.0.1:8765 из этой ветки. Прод не
трогался ни одним запросом: прод-состояние подтверждено офлайн (§A `rehearsal.txt`). Миграция — через Postgres-узел n8n двойником
`sql-oneoff.py` (тот же харнес; отличаются база, ключ и учётка «Postgres Staging»). Скрипты пакета — с `--base http://localhost:5678`.
Вывод каждого шага — `rehearsal.txt`; сырые журналы — `Tools/test-output/task18/` (gitignored).

| Шаг | Результат |
|---|---|
| §A прод = `40bbf80b` | офлайн: 30/30 графов снимка 2026-10-23 = канон `007ca164`; между `007ca164` и `40bbf80b` — ровно 4 workflow выкладки 2026-11-01; `7a7a40a0` = `40bbf80b` по `workflows` / `prompts` / `sql`; снимок 2026-11-01 — 30 OK, его карта промптов `e027b4b39537` = карта Create WA на `40bbf80b`, штампы обоих клонов «2» = штампы шаблонов `40bbf80b` |
| §A состав пакета | офлайн: 10 изменённых + 2 новых (графы и ключи настроек канона); Edit WA/TG равны; штампы клонов после обновления — `5c4c33e27468` / `cc77e05b537a`; шаг 4a с прод-значениями — оба `OK` |
| §B миграция, свежий кластер | `pg.sh init` (все миграции, эта последней) → пост-проверка `11\|6\|3\|t\|0\|3\|t\|1\|t\|0`, `migrate` ×2 — то же; порядок прода — база со всеми миграциями, кроме этой: пред-проверка `t\|t\|1\|t\|f` → миграция → та же пост-проверка → повтор → та же; одна CHECK `reply_events_state_check` с `cancelled_personal`; три колонки возврата — NULL, последние; схемы двух путей — 0 строк различий; обратный оператор → схема прода байт в байт, пред-проверка снова `t\|t\|1\|t\|f` |
| R1 стенд = канон прода | `bootstrap.py --canon-ref 40bbf80b --bot wa:stgwa001 --bot tg:stgtg001` → импорт 31 (30 + eval runner) + 2 бота, опубликовано 33 (2 мин 8 с) |
| R2 стенд = прод | два новых workflow, оставшихся от прошлых прогонов, — выключены → 36 с → удалены (GET 404); обе таблицы удалены и проверка `reply_events` возвращена без `cancelled_personal` (обратный оператор файла); пред-проверка `t\|t\|1\|t\|f` |
| R3 сверка | `verify-deployed`: canonical 32, **diff 12 = DIFF ×10 + MISSING ×2** — ровно пакет; клоны стенда BEHIND |
| R4 dry-run | would_update **9**, missing **2**, up_to_date 19, failed 0; с `--include-placeholders` — Set Workflow State would_update 1; Edit WA/TG — up to date; у каждого — точка отката |
| R5 шаг 2 | через Postgres-узел n8n: пред-проверка `{"no_audience":true,"no_connections":true,"state_checks":"1","state_check_named":true,"personal_state":false}`; миграция `200 {"success":true}` дважды; пост-проверка `{"audience_cols":"11","connection_cols":"6","restore_cols":"3","rls":true,"client_grants":"0","indexes":"3","wipe_idx":true,"state_checks":"1","personal_state":true,"rows":"0"}` дважды |
| R6 шаг 3 | published by the PUT, read-back ok, updated 1 |
| R7 шаг 4 | аудит копий (стенд; офлайн — с прод-значениями) OK ×2; CLI-импорт при работающем сервере — «Successfully imported 1 workflow.» ×2; GET — выключены, учётки ровно ожидаемые; публикация Decide Audience → `200 \| active True \| published=stored True`, опубликованный граф = канон; Chat Audience — то же. **Проба порядка:** пока Decide Audience не опубликован, публикация workflow, который его зовёт, → `400 Cannot publish workflow: Node "Decide Audience" references workflow crAudienceDec001 … which is not published`; после публикации — `200` |
| R8 шаг 4f | smoke 12/12 |
| R9 шаг 5 | 9 × updated 1, failed 0; семь активных — «published by the PUT», шаблоны — read-back ok |
| R10 шаг 6.1–6.2 | update-clones: 2 клона `+4 -0 ~9` (ровно узлы, ожидаемые на проде) → updated 2 (опубликованы своим PUT — уже со ссылкой на опубликованный подпроцесс) → повторный dry-run up to date 2 |
| R11 шаг 6.3 | `verify-deployed`: canonical 32, **diff 0**, exit 0; клоны AT-CANONICAL; 12 id пакета «до → после» = канон; dry-run снова — 30 + 1 up to date |
| R12 шаг 6.4 | `READ-BACK OK` (на стенде шаблоны, Suggest Replies и клоны хранят успешные выполнения — `'all'` производной копии; Decide Audience, Chat Audience, Wappi Proxy — `'none'`; `executionTimeout` 20) |
| R13 шаг 7 | `smoke.py --session` 14/14: 7.5 bootstrap 200, 7.6 `[400] bad_request / profileIds` |
| R14 шаг 8 | ошибочных выполнений с начала выкладки — 0 (10 id и весь инстанс); `--sweep` на пробе с производным узлом «Wipe Personal Chats» → `[{"wiped_chats": 0, "deleted_rows": 0}] \| OK` (свой тик стенда — в 06:00 Алматы, после репетиции) |
| R16 шаг 9 на стенде | `stand-canary.py` **2/2** (WA, TG): ИИ-сотрудник из Create (публичный API) зовёт подпроцесс, импортированный CLI и опубликованный через API: новый человек → business / default / new, ответ ровно один; контакт → personal / default / contact, ответа нет, о чате не хранится ничего; панель → `personal_chat`, модель не запускалась; `list` → **200**, оба решения и момент подключения |
| R17 SQL шагов 8–9 | `health.sql` → `{"suspicious":"0","profiles":[]}`; положительный контроль — в списке ровно «немой» профиль (не тот, у кого решение, и не свежий); его строки убраны — проверено по базе стенда перед повторным прогоном: `stg-health-*` — 0 в `audience_connections` и 0 в `chat_audience`. `canary.sql` здесь — прежний, без отметки (на пустом состоянии); редакции с отметкой — в таблицах ниже (G5, H1) |
| e2e | `derive-staging.py` (без импорта) → **e2e-audience 42/42** (22:17–22:29Z); после — `verify-deployed` diff 0, exit 0; ошибочных выполнений по инстансу — 0; строк «Error while saving insights» — 0 |
| Гейты | генераторы `--check` 24/24 (без `inject-prompts.py`: на этой машине нет tiktoken; карты промптов равны main), верификаторы 19/19 (`verify-sql-comments` — 109 Postgres-узлов в 33 файлах), `probe-audience-facts.py selftest` |
| После | `bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` (рабочий канон) → `verify-deployed` diff 0, exit 0; n8n, мок и Postgres остановлены (последний прогон на стенде закончился ≈ 22:33Z) |

**Повторный прогон после ревью пакета (2026-11-02, 04:36–05:01 по Алматы = 23:36–00:01Z)** — то, что ревью изменило: шаг 0.1,
canary, наблюдение, откат, отзыв `installs`; вывод — `rehearsal.txt` §G. Канон и инструменты выкладки после первой репетиции не
менялись (`git diff --stat f67fca8b -- Tools/n8n` — только `Tools/n8n/README.md`), поэтому e2e-audience и гейты генераторов не
повторялись.

| Шаг | Результат |
|---|---|
| G1 стенд из прод-состояния заново | `bootstrap.py --canon-ref 40bbf80b` → стенд = прод (два новых workflow удалены, таблицы сняты обратным оператором) → `verify-deployed` diff 12 → dry-run = ровно пакет (точки отката записаны) → миграция через Postgres-узел (дважды, те же пред- и пост-проверки) → Set Workflow State → два новых (копии, CLI, GET, публикация) → smoke 12/12 → 9 workflow → update-clones 2 → `verify-deployed` diff 0 → `READ-BACK OK` → `smoke.py --session` 14/14 |
| G2 шаг 0.1 | (a) и (b) — пусто, (c) `package_check.py` — 12 × `OK` (графы = таблица), `unchanged …: 20`, штампы клонов, `PACKAGE OK`. Контроль: незакоммиченная правка одного параметра в файле пакета (Dashboard Outcomes) и в файле вне пакета (Delete Orphan Profiles) — (a) показывает обе ` M`, (b) — обе в diff, (c) — `FAIL` на обеих, код 1; после `git checkout` — снова `PACKAGE OK` |
| G3 шаг 7, отзыв `installs` | запрос README через Postgres-узел → `200 {"revoked":"2"}` (две smoke-установки двух прогонов стенда; на проде после одного smoke — `"1"`) |
| G4 шаг 8, наблюдение | `watch.py` с начала выкладки (`deploy-start.txt`) — 0 ошибок. Проба с ошибкой, текст которой несёт «текст чата» и «номер»: строка `node Throw Probe (n8n-nodes-base.code) \| error Error \| level info`, ни текста, ни номера в выводе. `--sweep` на пробе с производным «Wipe Personal Chats» → `[{"wiped_chats": 0, "deleted_rows": 0}] \| OK`; тот же узел с намеренно сломанным запросом (идёт дальше при ошибке) → `[{"keys": ["error", "message"]}] \| LOOK`, текст ошибки не напечатан |
| G5 шаг 9, canary | `stand-canary.py` **2/2** (WA, TG): у контакта лежат строки «до выкладки» (2 истории, 2 памяти, 1 журнала, сутки назад); отметка 9.0 сохранена файлом; 9.1 в «Авто» → business / default / new, один ответ; переключение в «Выбрать» (`allApplied`); 9.2 в «Выбрать» → personal / default / contact, ни ответа, ни новой строки; 9.5 (`canary.py` → Postgres-узел): решения после отметки — ровно эти два, `personal_after` 0 / 0 / 0, `personal_rows_before` ≥ 2 / 2 / 1, `business_after` 2 / 2, `ledger_after` `sent` 1; контрольная запись истории, памяти и журнала личного чата после решения → `personal_after` 1 / 1 / 1 — увидена |
| G6 прежний canary на тех же строках | чат, решённый личным после выкладки, с перепиской, сохранённой до неё: `canary.sql` прежней редакции пакета → `personal_*_rows` 2 / 2 / 1 — его 9.5 остановил бы верную выкладку; нынешний → `personal_after` 0 / 0 / 0, сохранённое — в `personal_rows_before` |
| G7 откат | `rollback.py publish` точкой Dashboard Outcomes → служит прежняя версия (граф «до» `5f4cd7fb634d`), сохранённой остаётся новая, `verify-deployed` — DIFF; `deploy-canonical.py --apply` публикует сохранённую на месте — снова OK. Отказы: шаблон, удаление включённого workflow, `deactivate` чужого id. Точка клона → служит прежняя (`verify-deployed`: UNPUBLISHED). Инструменты `40bbf80b` (файлы из `git archive 40bbf80b`, история git — этой копии): шаблоны → `updated 1` ×2, клоны → `updated 2`, `failed 0`, повторный dry-run — up to date 2. Новые workflow: `deactivate` ×2, `delete` ×2 (36 с, `GET 404`) |
| После | `bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` → `verify-deployed` diff 0, exit 0 (00:00:21Z); n8n (SIGTERM), мок и Postgres остановлены, порты свободны — 00:00:57Z |

G5 и G6 выполнены редакцией `canary.sql` до правки §H; её случаи — строки, сохранённые до выкладки, и запись после решения —
нынешняя редакция повторила на временном кластере (§H, S1 и S3).

**Правки после второго ревью пакета (2026-11-02, без стенда: стенд, n8n и мок не запускались)** — повторяемый шаг 0.1, закрепление
своих файлов пакета, canary по времени решения, откат каноном без LFS, порядок отката Set Workflow State, 9.2 / 9.3; вывод —
`rehearsal.txt` §H.

| Шаг | Результат |
|---|---|
| H1 canary по времени решения | временный кластер Postgres через `pg.sh` (свой каталог данных, порт 5499, все миграции ветки), три сценария, каждый в транзакции с откатом. S1 верная выкладка (строки до выкладки, запись прежнего клона во время выкладки, рабочий чат, ставший личным, 9.1, 9.2): нынешний `canary.sql` → `personal_after` 0 / 0 / 0, прежний — тоже, а прежний с отметкой шага 1.3 → 3 / 3 / 1 — ложный СТОП. S2 запись в личный чат между решением и отметкой 9.0: нынешний → `history` 1, `ledger` 1 — СТОП, прежний → 0 / 0 / 0 — пропуск. S3 запись после отметки: оба → 1 / 1 / 1. После трёх откатов — 0 строк в четырёх таблицах; кластер остановлен (`pg.sh stop`) |
| H2 шаг 0.1 (c), свои файлы | на этой копии — 12 × `OK   own`, `PACKAGE OK`, код 0; контроль на черновой копии файлов пакета: один байт в `health.sql` → `FAIL own … (want …)`, код 1; лишний `*.sql` → `FAIL own … (want nothing: not a file of the package)`, код 1 |
| H3 шаг 0.1 (a), исключение по имени | команда README на этой копии, когда в папке пакета лежат пять файлов-заменителей вывода шагов 1.1–9.0 и один лишний файл: пять пропали из вывода, лишний (`??`) и изменённые файлы (` M`) остались; прежняя команда без исключений показала все шесть `??`; так же пропадает и изменённый файл, если исключить его. Заменители удалены |
| H4 откат каноном, временная копия | `GIT_LFS_SKIP_SMUDGE=1 git worktree add --detach … 40bbf80b` в черновой папке: 5 927 файлов, 108 МБ, файлы LFS — указатели, `git status` чистый, без сети; с оставленным `__pycache__` обычный `worktree remove` отказывает, `--force` удаляет, `worktree list` чистый |

**Не проверено (остатки репетиции):** нынешний `canary.sql` — только на временном кластере (H1), не через Postgres-узел n8n на
стенде; добавление и удаление временной копии для отката — в черновой папке (H4), а сами инструменты `40bbf80b` против n8n — из
`git archive` (G7); `docker compose`-путь импорта на проде (та же форма, что выполнена 2026-10-15 и 2026-10-19);
перепривязка WappiAuthToken (на стенде у этой учётки уже id прода — поймать расхождение может только 4a/4c на проде); Postgres 17
прода (стенд — 16; миграция — обычный DDL и два блока `do`); первый тик Sweep на проде (8.3); строка WhatsApp, отправленная через API
(0.3); живой canary и ворота выпуска (шаг 9); запись настройки журнала `'none'` там, где её не было, — у шаблонов, Suggest Replies и
клонов: производные копии стенда хранят успешные выполнения, поэтому на стенде тот же переход «ключа нет → ключ есть» прошёл со
значением `'all'`, а `'none'` на проде проверяет только 6.4; бэкап (0.4) — только VPS прода; запрос отзыва `installs` (шаг 7) —
только на стенде; `docker compose logs --since <время из deploy-start.txt>` (8.1) — на стенде нет docker, форма времени — RFC 3339,
которую `--since` принимает по документации docker; отказ «не из основной копии» при откате клонов (`settings differ … (want None)`)
взят из кода `update-clones.py` (сборка цели и чтение назад), на стенде он не виден — производный шаблон стенда задаёт `'all'`;
выключение `crAudienceDec001`, пока его ещё зовёт опубликованный workflow, — порядок отката этого не допускает и не проверялся.
