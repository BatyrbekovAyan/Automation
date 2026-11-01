# Пакет выкладки «Личные и рабочие чаты» (сервер)

**Статус (2026-11-02):** серверная половина — в каноне ветки `worktree-personal-chats`; **на прод НЕ выложено**. Канон пакета —
`Tools/n8n/workflows` + `Tools/n8n/sql` на `f67fca8b` (коммиты пакета канон не меняют). Пакет отрепетирован на стенде из
прод-состояния 2026-11-02 — раздел «Репетиция», вывод каждого шага — `rehearsal.txt` рядом. **Выкладка — только по явному «да»
владельца; команды запускает владелец** (кнопка Run, по одной; агенту запись в прод запрещена). Клиентская половина едет в новой
сборке, и **сервер выкладывается раньше неё** — без сервера новая сборка покажет все чаты без решения в «Личных» и не запросит для
них подсказки.

Спека — `docs/superpowers/specs/2026-10-25-personal-chats-design.md`, план — `docs/superpowers/plans/2026-10-25-personal-chats.md`,
живые проверки — `live-checks.md` рядом, архитектура и правила — CLAUDE.md, External APIs → «Личные и рабочие чаты». Сценарий
проверки на устройстве — `docs/audits/2026-10-19-device-pass-new-build.md` §6а.

## Что меняется для владельца сразу после выкладки

- ИИ-сотрудник молчит в личных чатах — контакты из телефона, переписки, начатые до подключения, группы, каналы, боты, 777000,
  «Избранное» — и ничего о них не хранит; с новыми людьми и с клиентами, которых отметил владелец, работает как раньше.
- Решение по чату принимается один раз, по его первому входящему после выкладки; до этого чат ничего не получает. Первое сообщение
  такого чата ждёт ответа дольше на **≈ 5–8 с** (два чтения Wappi и два-три узла Postgres по ≈ 1,9 с) — только пока у чата нет
  решения.
- Панель подсказок есть только в рабочем чате; «Сводка» показывает только рабочие чаты.
- Через 2 дня после того, как рабочий чат стал личным, сохранённое о нём удаляется (шаг Sweep раз в 6 часов).
- Успешные выполнения шаблонов, клонов, Suggest Replies и двух новых workflow в журнал n8n больше не попадают (раньше хранились
  7 дней вместе с текстом сообщения); ошибочные — как прежде.

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
| Wappi Proxy | `crWappiProxy0001` | `Record Owner Send` пишет только рабочие чаты (`business: false` иначе) | `apply-history-handoff.py` | `c8bba5e407f4` → `21204c214a4a` |
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

**Позже изменился только шаблон?** Тогда достаточно шагов 5–7 (только его строки шага 5, затем 6 и 7): точка отката печатается в
выводе шага 5.

### Шаг 0. Подготовка (ничего не меняет)

0.1. Канон рабочей копии = пакет:
```bash
git -C /Users/ayan/Projects/Automation diff --stat f67fca8b HEAD -- Tools/n8n/workflows Tools/n8n/sql
```
Ожидание: пусто. Не пусто — в main не влита ветка или на main лёг другой канон: **СТОП** (влить ветку; если на main новый канон —
пакет проверить заново).

0.2. На проде есть обе учётки, на которые будет перепривязана копия (только чтение: id / тип / имя):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/newwf.py creds --base https://n8n.choosereply.com
```
Ожидание: `GET /credentials 200 | rows … | wanted [('httpHeaderAuth', 'ZowntFGvApDJ7UzQ', 'WappiAuthToken'), ('postgres',
'vvRrFiEXzLVqKjOx', 'Postgres')] | OK`.

0.3. **Строка WhatsApp, отправленная через API** (только чтение, одни признаки; нужно отдельное «да» владельца — читается его чат в
Wappi). Начало разговора, которое сервер дописывает в память, когда чат становится рабочим, отличает отправку ИИ-сотрудника от
сообщения владельца по меткам API у строки `messages/get` (`from_where: "api"`, непустой `task_id`). У Telegram они есть; у строк
WhatsApp живая проверка ключей их не видела. Нужна ОДНА строка WhatsApp, отправленная через API: ответ «2» в «Авто» или сообщение
владельца из приложения — в любом чате WhatsApp «2», кроме того, с которого пойдёт canary шага 9. Затем (токен Wappi —
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
Смотреть строку отправки (`fromMe=1`, `t=` — её время): `from_where=api` или `task_id=yes` — метка есть, правило начала разговора на
WhatsApp держится. `from_where=<none>` и `task_id=-` — метки нет: тогда на WhatsApp действует остаток «Начало разговора на WhatsApp»
(раздел «Остатки»). Выкладку это не блокирует; результат — в журнал выкладки.

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
Откат: `POST /workflows/0DP79STI3pafXO9B/activate {"versionId": "<точка отката из rollback-pointers.txt>"}`.

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
Откат шага 4 — раздел «Откат» (новые workflow откатываются ПОСЛЕДНИМИ).

### Шаг 5. Изменённые workflow — по одному, каждый с чтением назад

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
Ожидание: код 0, `canonical 32 (diff 0)`, хэши «после» = таблица; клоны `AT-CANONICAL`; `settings=True` у всех.

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
**`list` = 200** отвечает только установке, у которой есть ИИ-сотрудник: это проверяет canary (шаг 9, п. 9.6) — и **без него сборку
не выпускать**.

### Шаг 8. Наблюдение

8.1. Ошибки с начала выкладки — у каждого workflow пакета и по всему инстансу (успешные выполнения шаблонов, клонов, Suggest Replies и
двух новых workflow не сохраняются — смотрим ошибочные):
```bash
python3 /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/watch.py --base https://n8n.choosereply.com
```
Ожидание: `GET 200 | error executions since …: []` ×10 и `whole instance … []`. Иначе — СТОП: прочитать ошибку
(`GET /executions/<id>?includeData=true`), до разбора ничего не менять.
```bash
ssh choosereply 'cd ~/choosereply && docker compose logs --since 30m n8n | grep -iE "error|insights" | tail -20'
```
Ожидание: пусто (временные workflow `sql-oneoff` ждут 35 с перед удалением — цикл insights не возникает).

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
2 дней ещё нет). Ошибка узла (`{"error": …}`) или `did not run` — СТОП и разбор: ветка идёт дальше при ошибке, поэтому видна только
здесь. До тика — «no execution yet»: повторить после него.

### Шаг 9. Canary на тестовом ИИ-сотруднике «2» — до выпуска любой сборки

Это первый вызов подпроцесса клоном, созданным через публичный API, на проде (у «2» клоны создал Create) — он и проверяется.
Действия на телефонах — владелец; чтение — запросы ниже (одни признаки: ни номеров, ни имён, ни текстов).
- 9.1. «Авто» у «2» включено. Со второго телефона — номера, которого НИКОГДА не было в контактах телефона «2» и который раньше этому
  WhatsApp не писал, — написать «2». Ожидание: «2» отвечает.
- 9.2. Написать «2» с номера, сохранённого в контактах телефона «2». Ожидание: ответа нет.
- 9.3. В приложении на телефоне владельца открыть чат из 9.2 и запросить подсказки. Ожидание: подсказок нет (сборка с main без этой
  функции покажет «Не удалось загрузить» — это отказ сервера `personal_chat`; новая сборка панель в личном чате не показывает).
- 9.4. (по желанию) То же в Telegram «2».
- 9.5. Чтение:
```bash
python3 /Users/ayan/Projects/Automation/Tools/n8n/.secrets/sql-oneoff.py "$(cat /Users/ayan/Projects/Automation/docs/audits/2026-10-25-personal-chats/canary.sql)"
```
Ожидание: в `decisions` у профиля WhatsApp «2» — `business | default | new` (9.1) и `personal | default | contact` (9.2), `chats` 1 у
каждой; в `connections` — его строка (`number_known: false`: сборка с main о подключении не сообщает, строку создал Decide Audience);
`personal_history_rows`, `personal_memory_rows`, `personal_ledger_rows` — `"0"`; в `ledger_last_day` есть `sent`. Затем 8.2 ещё раз —
`suspicious "0"`.
- 9.6. **Ворота выпуска сборки: `list` = 200.** Новая сборка на устройстве владельца (device pass §6а): после запуска чат из 9.1 — в
  рабочем списке, чат из 9.2 — в «Личных». Так приложение показывает только то, что вернул `list` с токеном его установки. Пока это не
  подтверждено — **сборку не выпускать**.

### Шаг 10. Журнал и отметки «DEPLOYED»

- `docs/audits/implementation-progress.md` — новый раздел «Развёртывание <дата> — «Личные и рабочие чаты»» (таблица шагов, снимки
  `prod-before.json` / `prod-after.json`, `rollback-pointers.txt`, `deploy-start.txt`, результат 0.3, smoke, canary).
- Отметки «in canon, NOT deployed» → «DEPLOYED to prod <дата>»: `grep -n "NOT deployed" /Users/ayan/Projects/Automation/CLAUDE.md
  /Users/ayan/Projects/Automation/Tools/n8n/README.md` — каждая строка «Личных и рабочих чатов» (на коммите пакета — CLAUDE.md 141,
  149 ×3, 151, 153, 200, 215, 216, 221, 259, 260, 261, 276; Tools/n8n/README.md 140 ×2, 147, 155, 156, 158, 159, 162, 165, 171, 172 — если
  строки сдвинулись, по выводу grep); `docs/audits/2026-10-19-device-pass-new-build.md` §0 и заголовок §6а («сервер на прод НЕ
  выложен» → «выложен <дата>»); строка «Статус» этого файла. Отметки «client, new build» остаются до сборки. Коммит
  `deploy(prod): …` (точечные пути).

### Шаг 11 (по желанию). Сайт: абзац «Личные чаты» в политике конфиденциальности

Абзац в `docs/legal/privacy.html` описывает поведение сервера — опубликованный ДО выкладки, он был бы неправдой (сегодняшний прод
отвечает контактам и хранит их). Публикуется только после шагов 1–9 и **после одобрения владельцем формулировки** (черновик уже в
файле). Вопрос владельцу: поднять ли строку «Действует с 27 августа 2026 г. · Версия 1.0» (§10 политики обещает сообщать об изменениях
там).
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

- **Обновлённые АКТИВНЫЕ workflow** (Set Workflow State, Create WA/TG, Delete Bot Files, Sweep, Wappi Proxy, Suggest Replies, Dashboard
  Outcomes) — по точке отката, публикацией прежней версии на месте (без окна, когда вебхука нет):
  `POST /api/v1/workflows/<id>/activate {"versionId": "<точка отката>"}`; сохранённая версия остаётся новой — verify-deployed покажет
  DIFF, пока канон не возвращён. Либо канон `40bbf80b`: `git show 40bbf80b:Tools/n8n/workflows/<файл> > Tools/n8n/workflows/<файл>` →
  `deploy-canonical.py --base https://n8n.choosereply.com --only <id> --apply --yes` (для Set Workflow State `--include-placeholders`)
  → вернуть канон (`git checkout -- Tools/n8n/workflows/<файл>`). Настройка журнала не версионируется: откат версии оставляет
  `saveDataSuccessExecution: "none"` — это безопасно.
- **Шаблоны (НЕАКТИВНЫ) — только каноном** `40bbf80b` тем же путём, никогда `POST /activate` с их точкой (это ВКЛЮЧИЛО бы шаблон).
  Затем клоны: `update-clones.py --base … --apply --yes`, пока в дереве старый шаблон, или точка отката каждого клона из
  `rollback-pointers.txt` (клоны активны — публикация прежней версии годится).
- **Новые workflow — откатываются ПОСЛЕДНИМИ**, после шаблонов и клонов: клон, который зовёт отсутствующий Decide Audience, оставляет
  без ответа каждый чат без решения. `POST /workflows/<id>/deactivate`, ≥ 35 с, `DELETE /workflows/<id>` (правило insights).
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
| R17 SQL шагов 8–9 | `health.sql` → `{"suspicious":"0","profiles":[]}`; положительный контроль — в списке ровно «немой» профиль (не тот, у кого решение, и не свежий); `canary.sql` выполняется через Postgres-узел |
| e2e | `derive-staging.py` (без импорта) → **e2e-audience 42/42** (22:17–22:29Z); после — `verify-deployed` diff 0, exit 0; ошибочных выполнений по инстансу — 0; строк «Error while saving insights» — 0 |
| Гейты | генераторы `--check` 24/24 (без `inject-prompts.py`: на этой машине нет tiktoken; карты промптов равны main), верификаторы 19/19 (`verify-sql-comments` — 109 Postgres-узлов в 33 файлах), `probe-audience-facts.py selftest` |
| После | `bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` (рабочий канон) → `verify-deployed` diff 0, exit 0; n8n, мок и Postgres остановлены (последний прогон на стенде закончился ≈ 22:33Z) |

**Не проверено (остатки репетиции):** `docker compose`-путь импорта на проде (та же форма, что выполнена 2026-10-15 и 2026-10-19);
перепривязка WappiAuthToken (на стенде у этой учётки уже id прода — поймать расхождение может только 4a/4c на проде); Postgres 17
прода (стенд — 16; миграция — обычный DDL и два блока `do`); первый тик Sweep на проде (8.3); строка WhatsApp, отправленная через API
(0.3); живой canary и ворота выпуска (шаг 9).
