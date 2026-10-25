# Личные и рабочие чаты — план исполнения

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ИИ-сотрудник не отвечает, не подсказывает и ничего не хранит в личных чатах (контакты, переписки до подключения, группы, каналы, боты). Он работает с новыми людьми и с клиентами, которых отметил владелец, а вид чата владелец меняет одним действием.

**Architecture:** Сервер решает вид чата один раз — на первом входящем от чата без решения. Шаблоны читают решение новым столбцом того же запроса `Read Reply Mode`. Чат без решения они отдают подпроцессу `Decide Audience` (`crAudienceDec001`): он берёт факты из Wappi (контакт, флаги Telegram, самое старое сообщение чата) и пишет решение в `chat_audience`. Правило, константы и SQL-фрагменты живут в одном модуле `Tools/n8n/audience.py`, таблица случаев `Tools/n8n/fixtures/audience-cases.json` общая для сервера и приложения. Владелец меняет решения через вебхук `ChatAudience` (`crChatAudience01`: `list` / `set` / `connected`). Писатели истории и памяти, аренда отправки, подсказки и «Сводка» пропускают всё, кроме рабочих чатов. Через 2 дня Sweep стирает данные чатов, которые владелец сделал личными. Приложение держит зеркало решений и очередь желаний (по образцу `ReplyModeSyncQueue`), прячет личные чаты в папку «Личные» и строит экраны эскиза 009 аддитивным `PersonalChatsBuilder`.

**Tech Stack:** n8n canonical JSON + Python-генераторы и верификаторы (`Tools/n8n/*.py`), Postgres через `Tools/n8n/tests/harness.py`, стенд `Tools/n8n/staging/` (Postgres, мок Wappi), Unity 6 C# (EditMode NUnit, `Tools/run-tests-headless.sh`, `Tools/run-editor-builder.sh`).

**Spec:** `docs/superpowers/specs/2026-10-25-personal-chats-design.md`; эскиз `.planning/sketches/009-personal-chats/`.

## Global Constraints

- Виды — `personal` / `business`; источники — `default` / `owner`; причины — `nonhuman` / `owner` / `contact` / `history` / `new`. Порядок правила: не человек → решение владельца → контакт → переписка до подключения → новый.
- Константы — один раз в `Tools/n8n/audience.py` и в `constants` таблицы случаев: `SETTLE_MINUTES = 30`, `FRESH_HOURS = 24`, `WIPE_DAYS = 2`. Копии в `ChatAudienceRule` сверяются с таблицей тестом.
- Чего не знаем (Wappi не ответил, чат не найден, запись не удалась) — личное, решение не пишется. Рабочим чат считается только по положительному полю записанной строки, никогда по «узел прошёл».
- Вычисленное решение (`default`) действует, только если `decided_at >= connected_at` текущего подключения. Решение владельца действует всегда. Вычисленное никогда не перезаписывает решение владельца.
- Ключ чата — `from` входящего webhook = `ChatManager.CurrentChatId`, тот же ключ, что у `reply_mode_flags` по чату.
- Ни текст, ни имя из личного или ещё не решённого чата не попадает в `chat_history_events`, `n8n_chat_histories`, `conversation_outcomes`, «Сводку», ИИ (бот, подсказки, классификатор «Сводки») и журнал успешных выполнений n8n.
- `Read Reply Mode` остаётся ОДНИМ узлом Postgres и остаётся fail-closed. Новые узлы шаблонов дописываются в КОНЕЦ `nodes`: Create/Edit правят `nodes[0]` и `nodes[5]` по индексу.
- Запросы фактов к Wappi: `fullResponse` + `neverError`, тайм-аут 10 с, credential `WappiAuthToken`, НИКОГДА `mark_all` — чтение не должно отмечать сообщения прочитанными.
- Новые таблицы: RLS deny-all, `revoke all … from anon, authenticated`; обе входят в `clone_lineage.MOVED_TABLES` и в `Delete Chat Memory`.
- Новые id `crAudienceDec001` и `crChatAudience01` — в `build-client-webhooks.PROTECTED_IDS` и в `staging/bootstrap.py ACTIVE_CANONICAL`; у обоих `saveDataSuccessExecution: "none"`.
- SQL: ни `$`+цифра в комментариях и литералах, ни `{{` в запросах, ни модификаторов pg-promise (`verify-sql-comments.py`); параметры в комментариях называются словами.
- Клиент:
  - каждый запрос идёт через `AuthorizedRequest.Send`;
  - все строки живут в `PersonalChatsCopy` и зарегистрированы в `FontGlyphCoverageTests` (в атласах SF Pro нет `−`, `№`, `✕`, `→`);
  - названия — по `docs/design/2026-10-21-naming-sotrudnik-vybrat.md`, слово «Выбрать» на этих экранах не показывается;
  - числа с существительными — через `RuPlural.Pick`.
- Сцена — источник правды:
  - только аддитивный идемпотентный `PersonalChatsBuilder`, при закрытом Editor;
  - `Tools/Rebuild Bot Settings Prefabs` не запускать;
  - сцену и префабы коммитить сразу после прогона builder'а, новый `.cs` — вместе с `.meta`.
- Unity-тесты:
  - запуск — `Tools/run-tests-headless.sh "<regex>"`, итог — атрибуты `total` / `failed` в `Tools/test-output/results.xml`;
  - тесты не меряют текст шрифтами SF Pro Medium/Bold: замер перезаписывает LFS-ассеты по 82 МБ. После прогона — `git status` на `Assets/TextMesh Pro/Fonts/`.
- Живые данные (Task 0): печатаются только признаки — без имён, номеров и текстов.
- Прод — только по явному «да» владельца, команды `--apply` запускает владелец. Сервер выкладывается раньше выпуска сборки.
- Коммит после каждой задачи; сообщение заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Решения строителя (спека §10)

- **Имена.** Таблицы `chat_audience` и `audience_connections`; вебхук `/webhook/ChatAudience` (`crChatAudience01`); подпроцесс `crAudienceDec001`. Клиент: `ChatAudienceStore`, `ChatAudienceSyncService`, `PersonalChatsCopy`.
- **Факты.**
  - Telegram — `chats/get?limit=20&offset=0&order=desc`: пишущий чат стоит наверху;
  - история — `messages/get?limit=1&order=asc`, то есть самое старое сообщение чата;
  - окончательно — после Task 0.
- **Подключение.**
  - `audience_connections(profile_id, phone, connected_at)` переезжает по линии.
  - Приложение сообщает о каждом подтверждённом подключении операцией `connected`. Сервер сам берёт номер из `get/status`; если номер другой, отсчёт начинается заново.
  - Если сообщение пришло раньше, чем `connected`, строку создаёт `Decide Audience` с моментом линии и пустым номером.
- **Ответ на ждущее сообщение.**
  - После ответа приложению `set` подаёт последнее сообщение чата заново в webhook ИИ-сотрудника — если оно от клиента и не старше суток.
  - Дальше идёт обычный путь шаблона: режим «Авто» проверяет сам шаблон, а `reply_events` не даст ответить дважды.
  - При «Выбрать» это сообщение записывается в историю и память, как любое сообщение рабочего чата.
- **Начало разговора.** Когда чат впервые становится рабочим — решением `Decide Audience` или владельцем через `set`, — сервер берёт из Wappi последние 10 сообщений чата и дописывает в историю и память те, которых там ещё нет. Сообщения владельца получают отметку «Владелец: » (`OWNER_CONTENT_SQL`). Иначе ИИ-сотрудник не знает, что владелец уже написал клиенту первым (спека §4.3).
- **Журнал выполнений.** Если Task 0 покажет, что инстанс хранит успешные выполнения, шаблоны получают `saveDataSuccessExecution: "none"`. Ошибочные выполнения сохраняются как раньше.
- **«Сводка».** Dashboard Outcomes пропускает личные чаты и в классификаторе, и в выдаче: память чата, который сделали личным, не уходит в ИИ «Сводки» в оставшиеся 2 дня.
- **Экран «Кто из них клиенты?».** Флаг `{bot}AudienceOnboarding{Whatsapp|Telegram}` ставит приложение после первого подключения канала и после смены номера — сохранённый номер против только что подключённого. Экран показывается, когда открыт экран чатов этого ИИ-сотрудника на этом канале.
- **Ошибка записи.** Тост «Не удалось перенести чат — проверьте интернет», чат возвращается на место.
- **Показ чата без решения.** Приложение считает, что переписка до подключения была, если последнее сообщение чата старше `connectedAt`. Это только показ — решение принимает сервер.

## Review Focus

1. **Два первых сообщения нового чата приходят одновременно.** Решение одно, и оба выполнения читают его. Пины: Task 2 `race_two_first_messages_one_decision`, Task 3 `second_run_reads_the_first_decision`.
2. **«Сменить номер» на том же профиле.** Вычисленные решения прежнего номера перестают действовать, отметки владельца остаются, 30 минут отсчитываются заново. Пины: Task 2 `number_change_voids_defaults_keeps_owner`, Task 7 `connected_other_phone_resets`, Task 6 `own_row_wins_when_the_number_changed`.
3. **Чат сделали личным, пока ответ готовится.** Отправки нет. Пины: Task 4 `lease_refuses_a_chat_turned_personal`, Task 9 `make_personal_cancels_inflight`.
4. **Wappi не ответил при сборе фактов.** Ответа нет, ничего не записано, следующее сообщение пробует снова. Пины: Task 3 `history_500_is_personal_unknown_not_written`, Task 9 `wappi_failure_then_next_message_decides`.
5. **Случайное «Сделать личным».** Если его отменили в пределах 2 дней, память цела. Если чат оставался личным 2 дня, данные стёрты, а `dialog_counts` целы. Пины: Task 8 `returned_within_two_days_untouched`, `wipes_after_two_days_keeps_dialog_counts`.

---

### Task 0: Проверки на живом номере и журнал выполнений (спека §7) — вместе с владельцем

**Files:**
- Create: `docs/audits/2026-10-25-personal-chats/live-checks.md`
- Create: `Tools/wappi/probe-audience-facts.py`. Скрипт только читает: по `profile_id` и `chat_id` печатает признаки — есть ли `contact_name`, флаги `user.*`, тип чата, формат id (цифры заменены на `N`), время самого старого сообщения относительно подключения и ключи строки `messages/get`.

От результатов зависят Task 3 (сбор фактов), подписи экрана подключения в Task 14 и шаг журнала в Task 4. Остальные задачи строятся параллельно.

- [ ] **Step 1: Подготовка (владелец).** Тестовый ИИ-сотрудник подключён к WhatsApp и Telegram с выключенным «Авто»; есть второй телефон с номером не из контактов.
- [ ] **Step 2: Проверки 1–9 таблицы §7 и проверка 10.** Проверка 10 — ключи строки `messages/get` против ключей входящего webhook, по обоим каналам; они нужны повторной подаче (Task 7). Действия на телефонах — владелец, чтение — `probe-audience-facts.py` и журнал выполнений n8n.
- [ ] **Step 3: Журнал на проде (только чтение, с разрешения владельца).** Значения `EXECUTIONS_DATA_SAVE_ON_SUCCESS` и `EXECUTIONS_DATA_MAX_AGE` из окружения контейнера n8n.
- [ ] **Step 4: Записать решения в `live-checks.md`:**
  - **D1, контакт WhatsApp.** По умолчанию — `contact_name` во входящем webhook, запасной вариант — `contact.FullName` из `chats/filter`. Если надёжного признака нет ни там, ни там — СТОП, вопрос владельцу (спека §7);
  - **D2, флаги Telegram.** `chats/get?limit=20&offset=0&order=desc` находит пишущий чат на первой странице; `user.Contact/Bot/Support/Self` и `type` совпадают с тем, что видно на телефоне;
  - **D3, «не люди».** Какие из них приходят входящими (`@newsletter`, `status@broadcast`, боты, 777000, «Избранное») и чем помечены (`chat_type`, `is_bot`, выполняется ли `from == chatId`);
  - **D4, история.** Видит ли `messages/get?limit=1&order=asc` сообщение до подключения сразу после подключения или через сколько минут; оставить `SETTLE_MINUTES = 30` или поднять;
  - **D5, журнал.** Значение не `none` — Task 4 Step 6 ставит шаблонам `saveDataSuccessExecution: "none"`;
  - **D4a, порядок страницы.** `messages/get?order=desc` на `api` и `tapi` отдаёт новые первыми, а сообщения одной секунды — в порядке Wappi (на этом стоит `BACKFILL_SQL`);
  - **D6, повторная подача.** Соответствие полей строки `messages/get` полям webhook, которые читают шаблоны: `profile_id, id, body, type, from, chatId, time, caption, chat_type, is_bot`.
- [ ] **Step 5: Commit** — `docs(personal-chats): проверки на живом номере (§7)`.

### Task 1: Правило, константы и общая таблица случаев

**Files:**
- Create: `Tools/n8n/audience.py`
- Create: `Tools/n8n/fixtures/audience-cases.json`
- Create: `Tools/n8n/tests/test_audience_rule.py`

**Interfaces:**
- Produces (`audience.py`):

```python
SETTLE_MINUTES, FRESH_HOURS, WIPE_DAYS = 30, 24, 2
WA_CONTACT_SOURCE = "webhook"            # Task 0 D1: "webhook" | "chats_filter"
WA_NONHUMAN_SUFFIXES = ("@g.us", "@newsletter", "@broadcast")
TG_SERVICE_IDS = ("777000",)
TG_PERSON_TYPE = "user"                  # тип личного чата Telegram, сравнивается точно
TG_NONHUMAN_TYPES = ("channel", "chat")  # известные «не люди»; любой другой тип — personal/unknown
FIXTURE = os.path.join(HERE, "fixtures", "audience-cases.json")
RULE_JS: str    # function decideAudience(f) → {kind: 'personal'|'business', reason: 'nonhuman'|'owner'|'contact'|'history'|'new'|'unknown'}
```

- Таблица: объект `{"constants": {"settleMinutes": 30, "freshHours": 24, "wipeDays": 2}, "cases": [...]}`. У каждого случая ровно 10 ключей: `name`, `channel` (`whatsapp|telegram`), `chatId`, `chatType`, `flags` (`{bot, support, self}` или `null` — карточка Telegram не получена), `contact` (`true|false|null`), `history` (`true|false|null`), `owner` (`null|personal|business`), `expectKind`, `expectReason`.

Правило (одинаково в обоих двойниках):
1. **Не человек → `personal/nonhuman`.**
   - WhatsApp: `chatId` кончается одним из `WA_NONHUMAN_SUFFIXES`, или `flags.bot` / `flags.self` истинны.
   - Telegram: `chatId` входит в `TG_SERVICE_IDS`; `chatType` входит в `TG_NONHUMAN_TYPES` (точное сравнение); или истинен любой из `flags.bot/support/self`.
2. **Telegram, `flags === null` → `personal/unknown`:** нельзя исключить бота.
   - **2a. Telegram, `chatType !== TG_PERSON_TYPE` → `personal/unknown`** (решение исполнения 2026-10-25): пустой, незнакомый или иначе написанный тип не записывается.
3. **`owner` → `{owner}/owner`.**
4. **`contact === true` → `personal/contact`.**
5. **`history === true` → `personal/history`.**
6. **`contact === null` или `history === null` → `personal/unknown`.**
7. **Иначе → `business/new`.**

- [ ] **Step 1: Таблица и падающий тест.** Не меньше 24 случаев: `wa_group`, `wa_group_owner_business_still_nonhuman`, `wa_newsletter`, `wa_status_broadcast`, `wa_self`, `wa_contact`, `wa_contact_owner_business`, `wa_history`, `wa_new`, `wa_history_unknown`, `wa_lid_no_contact_new`, `wa_lid_no_contact_history`, `wa_owner_personal_on_new`, `tg_contact`, `tg_bot`, `tg_support`, `tg_777000_flags_unknown`, `tg_self`, `tg_channel`, `tg_group_chat`, `tg_flags_unknown_history_true` (→ unknown), `tg_new`, `tg_contact_unknown_history_unknown`, `tg_owner_business_on_contact`. В тесте:
  - `every_case_matches` — `decideAudience` в node (через `harness.code`, как `test_set_schedule.py` гоняет Validate) даёт `(expectKind, expectReason)`;
  - `constants_match_the_fixture`;
  - `cases_have_exactly_ten_keys_and_unique_names`;
  - `rule_js_is_pure` — в `RULE_JS` нет `$(`, `require(`, `Date`.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_audience_rule.py` → ImportError / `… fail`.
- [ ] **Step 3: Реализовать `audience.py`** (константы и `RULE_JS`).
- [ ] **Step 4: PASS** — тот же запуск → `N pass, 0 fail`.
- [ ] **Step 5: Commit** — `feat(personal-chats): правило «личный / рабочий» и общая таблица случаев`.

### Task 2: Миграция и запросы решений (реальный SQL)

**Files:**
- Create: `Tools/n8n/sql/2026-10-25-chat-audience.sql` (заголовок, `-- TABLE-BEGIN/-- TABLE-END`, закомментированные post-checks и обратная миграция — по образцу `sql/2026-10-14-bot-schedule.sql`)
- Modify: `Tools/n8n/audience.py` (SQL)
- Modify: `Tools/n8n/staging/pg.sh` — миграция после `bot_schedule`
- Modify: `Tools/n8n/staging/fixtures.py` — блоки `delete` и `sweep-mock-profiles` чистят обе таблицы по `profile_id`
- Create: `Tools/n8n/tests/test_audience_sql.py`

**Interfaces:**

```sql
chat_audience(profile_id text, chat_id text,
  kind text check (kind in ('personal','business')), source text check (source in ('default','owner')),
  reason text check (reason in ('nonhuman','owner','contact','history','new')),
  version bigint not null default 0, decided_at timestamptz not null default now(),
  personal_since timestamptz, wiped_at timestamptz, updated_at timestamptz not null default now(),
  primary key (profile_id, chat_id))      -- + частичный индекс (personal_since) where source = 'owner' and kind = 'personal' and wiped_at is null
audience_connections(profile_id text primary key, phone text, connected_at timestamptz not null, updated_at timestamptz not null default now())
-- CHECK состояния reply_events (имя — как в sql/2026-09-17-reply-events.sql) получает 'cancelled_personal'
```

```python
def VALID_SQL(a="a", c="c") -> str              # a.source = 'owner' or a.decided_at >= coalesce(c.connected_at, '-infinity')
def AUDIENCE_SQL(profile="$1", chat="$2") -> str  # скалярный подзапрос: kind действующей строки, иначе NULL
def BUSINESS_SQL(profile="$1", chat="$2") -> str  # coalesce(AUDIENCE_SQL = 'business', false)
def LINE_CONNECTED_AT_SQL(profile="$1") -> str    # самый ранний bot_profiles.created_at по цепочке superseded_by к профилю; now(), если строки нет
ENSURE_CONNECTION_SQL  # параметр: профиль → всегда одна строка {connected_at_ms, phone}; создаёт строку подключения, если её нет (момент линии, номер NULL)
DECIDE_WRITE_SQL       # профиль, чат, вид, причина → одна строка {kind, reason, source} — действующая строка ПОСЛЕ записи
OWNER_SET_SQL          # профиль, JSON [{chatId, kind, version}] → по строке на чат в порядке запроса {chat_id, applied, kind, version, refused, became_business}
CONNECTED_SQL          # профиль, цифры номера → одна строка {connected_at_ms}
LIST_SQL               # JSON [id профилей] → {profile_id, connected_at_ms, decisions jsonb} — только действующие решения
def BACKFILL_SQL(memory_row: str, owner_content_sql: str) -> str   # профиль, чат, id сообщения-повода, JSON [{id, fromMe, body, type, time}] → одна строка {added}
# memory_row / owner_content_sql — MEMORY_ROW и OWNER_CONTENT_SQL из apply-history-handoff.py; они передаются аргументами:
# audience.py не импортирует apply-history-handoff.py — тот импортирует его
```

Что закрепляется:
- **`DECIDE_WRITE_SQL`** — `insert … on conflict (profile_id, chat_id) do update … where chat_audience.source = 'default' and not <valid>`. Он заменяет только недействующую вычисленную строку, затем читает действующую строку (победителя гонки). Строку владельца не трогает никогда.
- **`OWNER_SET_SQL` — когда применяется.** Применяется, если строки нет, строка вычисленная (даже действующая) или `version < excluded.version`. Отказ `nonhuman` — если `chatId` «не человек» по суффиксу или по `TG_SERVICE_IDS`, либо если в строке записана причина `nonhuman`.
- **`OWNER_SET_SQL` — даты.** `personal_since`: рабочий → личный ставит `now()`, личный → личный оставляет как было, → рабочий обнуляет. `wiped_at` обнуляется, когда меняется `personal_since` и когда чат становится рабочим.
- **`OWNER_SET_SQL` — `became_business`.** Истинно, если запись применена, вид — `business`, а прежний действующий вид был не `business` (нет строки и недействующая строка тоже считаются «не рабочим»).
- **`CONNECTED_SQL`.** Строки нет → вставка (момент линии, номер). Номер в строке пуст → дописать номер, момент оставить. Номер тот же или пришёл пустой → без изменений. Номер другой → `connected_at = now()` и новый номер.
- **`BACKFILL_SQL`.**
  - Пишет только если чат рабочий (`BUSINESS_SQL`).
  - Пропускает сообщение-повод и всё, чей id уже есть в `chat_history_events` этого чата: туда попадают ответы ИИ, отправки из приложения и с телефона.
  - На входе — страница `messages/get` с `order=desc` ровно в том виде, в каком её вернул Wappi (новые первыми). Пишет её развёрнутой, по позиции в странице, и по времени не пересортировывает: сообщения одной секунды и сообщения без времени остаются на своих местах. Сообщение владельца → история `owner_phone` и память `ai` с отметкой «Владелец: » (`owner_content_sql`). Сообщение клиента → история `client` и память `human` той же формы (`memory_row`), что пишет `Record Suppressed`. Обе строки несут `source` и `message_id`.

- [ ] **Step 1: Падающие тесты** (временные таблицы `like public.X including all`, как в `test_set_schedule.py`):
  - `default_written_once` — второе решение по тому же чату ничего не меняет;
  - `race_two_first_messages_one_decision` — два соединения `PgSession`, `personal/history` против `business/new`: одна строка, оба ответа равны записанной;
  - `owner_outranks_default_and_default_never_overwrites_owner`;
  - `owner_version_guard` — старая версия не применяется, в ответе версия сервера;
  - `number_change_voids_defaults_keeps_owner` — после `CONNECTED_SQL` с другим номером `AUDIENCE_SQL` даёт NULL для вычисленной строки и вид владельца для его строки; затем `DECIDE_WRITE_SQL` заменяет устаревшую строку;
  - `same_or_empty_phone_keeps_the_moment`;
  - `owner_set_refuses_nonhuman` — по `@g.us`, по `777000`, по записанной причине;
  - `personal_since_rules`;
  - `line_moment_follows_superseded_by` — три профиля в цепочке, момент = самый ранний `created_at`;
  - `backfill_adds_missing_in_time_order_with_owner_mark` — сообщение-повод и уже записанные id пропущены, строки владельца начинаются с «Владелец: »;
  - `backfill_writes_nothing_for_a_personal_or_undecided_chat`;
  - `migration_postchecks` — RLS включён, у `anon`/`authenticated` нет прав, `reply_events` принимает `cancelled_personal` и по-прежнему отказывает `bogus`.
- [ ] **Step 2: FAIL** — Run: `Tools/n8n/staging/pg.sh start; python3 Tools/n8n/tests/test_audience_sql.py` → `… fail`.
- [ ] **Step 3: Миграция, `pg.sh`, `fixtures.py`, запросы в `audience.py`;** перезапустить `pg.sh`, чтобы миграция применилась.
- [ ] **Step 4: PASS** — тот же запуск и `python3 Tools/n8n/tests/test_audience_rule.py` → `0 fail`.
- [ ] **Step 5: Commit** — `feat(personal-chats): таблицы решений и подключений, запросы с реальным SQL`.

### Task 3: Подпроцесс `Decide Audience` (`crAudienceDec001`)

Ждёт Task 0 (D1–D4).

**Files:**
- Create: `Tools/n8n/build-decide-audience.py` (`--check` → exit 2, если что-то устарело), `Tools/n8n/workflows/crAudienceDec001-Decide_Audience.json`
- Create: `Tools/n8n/verify-personal-chats.py` — проверки подпроцесса; Task 7 и Task 8 дописывают свои
- Create: `Tools/n8n/tests/test_decide_audience.py`
- Modify: `Tools/n8n/build-client-webhooks.py` (`PROTECTED_IDS` += `crAudienceDec001`), `Tools/n8n/staging/bootstrap.py` (`ACTIVE_CANONICAL`, перед `stgEvalRunner001`)
- Modify: Set Workflow State — генератор дописывает id после якоря `  'crSetSchedule001'\n]);`. После смены `PROTECTED_IDS` перезапустить `apply-clone-lifecycle.py`, `apply-history-handoff.py`, `apply-sweep-clone-deactivate.py`.

**Interfaces:**
- Consumes: из Task 1 — `RULE_JS` и константы; из Task 2 — `ENSURE_CONNECTION_SQL`, `DECIDE_WRITE_SQL`, `BACKFILL_SQL`.
- Produces:
  - **Входы** `When Executed by Another Workflow` (форма — как у `crPriceSearch001` в `build-price-search.py`): `channel` (`whatsapp|telegram`), `profileId`, `chatId`, `messageId`, `chatType`, `isBot` (boolean), `contactName`, `connectedAtMs` (number или null), `connectionPhone`.
  - **Выход** — один элемент `{audience: 'business'|'personal', outcome: 'business'|'personal_settling'|'personal_unknown'|'personal_nonhuman'|'personal_contact'|'personal_history'}`.
  - `audience = 'business'` — ТОЛЬКО если строка, которую вернул `Write Decision`, говорит `business`.

Граф (имена точные):
1. `When Executed by Another Workflow` → `Has Connection?` (If: `connectedAtMs` — число) → [`Plan Facts`] | [`Ensure Connection` (PG; `alwaysOutputData` + continue) → `Plan Facts`].
2. `Plan Facts` (Code). Момент берётся из входа или из `Ensure Connection`, если тот выполнялся (`isExecuted`); строки нет → `personal_unknown`. Затем:
   - `Date.now() < connectedAtMs + SETTLE_MINUTES × 60000` → стоп `personal_settling`, никаких вызовов (константа подставляется генератором из `audience.py`);
   - не человек по входу (суффикс WhatsApp, `777000`, `chatType`, `isBot`, «Избранное» WhatsApp — цифры `chatId` равны цифрам номера подключения) → сразу к `Decide`;
   - решает, нужны ли `needCard` (Telegram; WhatsApp — только при D1 = `chats_filter`) и `needHistory` (не «не человек» и не контакт).
3. `Need Card?` → `Chat Card`:
   - Telegram — `tapi/sync/chats/get?profile_id=…&limit=20&offset=0&order=desc`;
   - WhatsApp при D1 = `chats_filter` — `api/sync/chats/filter?profile_id=…&client_name=<цифры chatId>`.

   Затем `Read Card` (Code): диалог с `id == chatId` → флаги, контакт, `type`; диалог не найден → `flags = null`.
4. `Need History?` → `Oldest Message` (`{api|tapi}/sync/messages/get?profile_id=…&chat_id=…&limit=1&order=asc`).
5. `Decide` (Code): `RULE_JS` над фактами.
   - `history` — время самого старого сообщения меньше момента подключения.
   - Не 200, `status` не `done`, массив пуст или его нет → `history = null`.
   - На выходе `persist = reason !== 'unknown'`.
6. `Persist?` (If: `$json.persist === true`) → `Write Decision` (PG; continue) → `Became Business?` (If: прочитанная строка говорит `business`).
   - Если да: `Recent Messages` (`{api|tapi}/sync/messages/get?…&limit=10&order=desc`) → `Backfill` (PG `BACKFILL_SQL`; continue) → `Shape`. Страница уходит в `Backfill` ровно как её вернул Wappi, новые первыми — без сортировки.
   - Если нет: сразу `Shape` (Code).

   Сбой `Recent Messages` или `Backfill` решения не меняет: рабочий чат остаётся рабочим. Все стопы ведут прямо в `Shape` со своим `outcome`.

- [ ] **Step 1: Падающие тесты.**
  - **Как.** Проход по графу с подставными ответами Wappi и реальным SQL — по образцу `tests/notifier_chain.js` / `tests/rc_events_chain.py`; оба канала.
  - **Сценарии:**
    - `settling_stops_before_any_call_and_writes_nothing`, `settling_boundary_29_and_31_minutes`, `wa_contact_written_without_history_call`, `wa_new_person_business_after_one_history_call`;
    - `business_backfills_the_owners_first_message`, `backfill_failure_keeps_business`;
    - `wa_old_chat_personal_history`, `wa_newsletter_nonhuman_no_calls`, `wa_self_nonhuman`;
    - `tg_bot_from_chat_card`, `tg_777000_no_calls`, `tg_channel_type_no_calls`, `tg_dialog_not_on_page_is_personal_unknown_not_written`;
    - `history_500_is_personal_unknown_not_written`, `empty_history_is_unknown`, `write_failure_is_personal_unknown`;
    - `second_run_reads_the_first_decision`, `no_url_carries_mark_all`, `missing_connection_is_created_with_the_line_moment`.
  - **Негативы `verify-personal-chats.py`** — на временных копиях: снятый `neverError`, `mark_all` в URL, `Persist?` по «узел прошёл», лишний узел Postgres.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_decide_audience.py` → `… fail`.
- [ ] **Step 3: Реализовать генератор и сгенерировать workflow.** Проверки верификатора:
  - входы — ровно девять;
  - у каждого HTTP-узла `fullResponse` + `neverError`, `timeout ≤ 10000`, credential `WappiAuthToken`, нет `mark_all`;
  - `Persist?`, `Became Business?` и `Shape` судят по положительным полям;
  - `Decide` содержит `audience.RULE_JS` дословно, запросы равны тексту модуля;
  - узлов Postgres три (`Ensure Connection`, `Write Decision`, `Backfill`); первый — только без строки подключения;
  - `settings.saveDataSuccessExecution == "none"`;
  - id есть в `PROTECTED_IDS` и в списке Set Workflow State.

  Run: `python3 Tools/n8n/build-decide-audience.py && python3 Tools/n8n/build-decide-audience.py --check && python3 Tools/n8n/verify-personal-chats.py`
  Expected: `wrote …`, затем exit 0, затем `personal chats verify: OK`.
- [ ] **Step 4: PASS** — `test_decide_audience.py`, `verify-sql-comments.py`, `verify-clone-lifecycle.py`, `verify-auth-gate.py` → зелёные.
- [ ] **Step 5: Commit** — `feat(personal-chats): подпроцесс Decide Audience — факты из Wappi и запись решения`.

### Task 4: Шаблоны — решение на входе, запись только рабочих, отмена отправки

**Files:**
- Modify: `Tools/n8n/apply-history-handoff.py`:
  - `READ_MODE_SQL` — столбцы `audience`, `connected_at_ms`, `connection_phone`;
  - `patch_bot` — четыре новых узла и рёбра; `Suppressed?` читает `$('Read Reply Mode').first().json.suppressed`;
  - `RECORD_OUTGOING_SQL` — только рабочие, исход `ignored_personal`;
  - `CANCEL_LABEL_JS` — `personal` → `cancelled_personal`, первым.
- Modify: `Tools/n8n/apply-reply-pipeline-v2.py` — `ACQUIRE_SQL`: CTE `aud` с `BUSINESS_SQL`, `and not personal` в `allowed`, столбец `personal`, повторное чтение (`recheck`) не просится, когда `personal`.
- Modify: `Tools/n8n/apply-outcome-stamps.py` — в `byPrev` добавить `'Business?': 'personal'` и `'Decided Business?': 'personal_unknown'`.
- Modify проверки:
  - `verify-history-handoff.py` — новый `check_audience_gate`;
  - `verify-telegram-parity.py` — новые узлы в `TG_BOT_NODE_NAMES`, равенство развилки в двух шаблонах;
  - `verify-reply-pipeline.py` — `STATES` += `cancelled_personal`;
  - `verify-message-batching.py` — текст аренды;
  - `tests/test_reply_schedule.py` — якоря.
- Modify: `Tools/n8n/staging/derive-staging.py` — копии шаблонов на стенде сохраняют успешные выполнения (D5).
- Create: `Tools/n8n/tests/test_reply_audience.py`
- Regenerate: `workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json`. Порядок генераторов: `apply-reply-pipeline-v2.py` → `apply-history-handoff.py` → `apply-outcome-stamps.py`; второй прогон — без изменений.

**Interfaces:**
- Consumes: из Task 2 — `AUDIENCE_SQL`, `BUSINESS_SQL`; из Task 3 — подпроцесс и его выход.
- Новые узлы (в конец `nodes`):
  - `Audience Known?` If `={{ $json.audience === 'personal' || $json.audience === 'business' }}` → [`Business?`] | [`Decide Audience`];
  - `Business?` If `={{ $json.audience === 'business' }}` → [`Suppressed?`] | [`Outcome`];
  - `Decide Audience` — executeWorkflow `crAudienceDec001`, ждёт завершения, `onError: continueRegularOutput`. Входы — из `$('Webhook').first().json.body.messages[0]` и `$json` узла `Read Reply Mode`: `channel` (константа шаблона), `profileId = profile_id`, `chatId = from`, `messageId = id`, `chatType = chat_type`, `isBot = is_bot === true`, `contactName = contact_name || ''`, `connectedAtMs`, `connectionPhone`;
  - `Decided Business?` If `={{ $json.audience === 'business' }}` → [`Suppressed?`] | [`Outcome`];
  - ребро `Read Reply Mode` → `Audience Known?` (вместо `Suppressed?`).

- [ ] **Step 1: Падающие тесты** (реальный SQL + `reply_chain.Run`, оба канала):
  - `read_mode_returns_audience_in_one_row` — NULL / `personal` / `business`; устаревшая вычисленная строка после смены номера → NULL; строка владельца → его вид;
  - `personal_writes_nothing` — проход от `Webhook` с личной строкой: ни одной новой строки в `chat_history_events`, `n8n_chat_histories`, `reply_events`, `dialog_counts`, `chat_reply_state`; `Outcome` = `personal`;
  - `undecided_goes_to_decide_then_business_continues` и `undecided_personal_ends_with_its_outcome`;
  - `suppressed_reads_the_mode_row_after_decide` — чат «Выбрать», решённый рабочим, идёт в `Record Suppressed`;
  - `lease_refuses_a_chat_turned_personal` — `cancelled_personal`, отправки нет, строка `Record Cancel`;
  - `record_outgoing_skips_personal_and_undecided` — owner_phone в личном → `ignored_personal`, в рабочем → записано;
  - негативы `check_audience_gate`:
    - узел записи достижим из ложной ветки `Audience Known?` в обход истинной `Decided Business?`;
    - в `Read Reply Mode` нет `audience`;
    - лишний узел Postgres между `If` и `Audience Known?`.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_reply_audience.py && python3 Tools/n8n/verify-history-handoff.py` → `… fail`.
- [ ] **Step 3: Реализовать в генераторах, перегенерировать оба шаблона.**
- [ ] **Step 4: PASS.**

  Run: `python3 Tools/n8n/tests/test_reply_audience.py && python3 Tools/n8n/tests/run_all.py && for v in verify-history-handoff verify-telegram-parity verify-reply-pipeline verify-message-batching verify-sql-comments; do python3 Tools/n8n/$v.py | tail -1; done`
  Expected: всё зелёное.
- [ ] **Step 5: Commit** — `feat(personal-chats): решение на входе шаблонов, отмена отправки и запись только рабочих`.
- [ ] **Step 6 (только при D5 «сохраняются»): журнал без личных сообщений.**
  - `patch_bot` ставит обоим шаблонам `settings.saveDataSuccessExecution = "none"`, `derive-staging.py` ставит копиям на стенде `"all"`, верификатор закрепляет обе стороны.
  - Проверить, что `Create Workflow` (`apply-clone-lifecycle.py`) переносит `settings` шаблона в клон, а `update-clones.py` сравнивает это поле. Если нет — дописать перенос этого одного поля и случай в `tests/test_verify_deployed.py`.
  - Цена, которую записать в коммит: на проде не видно успешных выполнений ботов и их отметок `Outcome`; ошибочные выполнения видны.
  - Commit — `feat(personal-chats): успешные выполнения ботов не сохраняются`.

### Task 5: Прокси, подсказки и «Сводка» пропускают всё, кроме рабочих чатов

**Files:**
- Modify: `Tools/n8n/build-auth-workflows.py` — `RECORD_OWNER_SEND_SQL`: `ev` и `mem` пишутся только при `BUSINESS_SQL` от первого и второго параметров (`values` → `select … where`).
- Modify: `Tools/n8n/apply-suggest-v2.py`:
  - `GATE_SQL` и `GATE_PARAMS` получают `profileId` и `chatId` из Prep (третий и четвёртый параметры) и CTE `aud`;
  - для не рабочего чата отказ `personal_chat` — раньше любого счётчика;
  - Build Response называет этот отказ только вызывающему с сессией (правило N-4).
- Modify: `Tools/n8n/apply-history-handoff.py` — `Find Changed Sessions` и `FETCH_OUTCOMES_SQL` берут только сессии, чей (профиль, чат) рабочий; профиль и чат разбираются из `session_id` (`split_part` по первому `:`).
- Create: `Tools/n8n/tests/test_personal_readers.py` — три запроса на реальном SQL. Прежние пины — `test_memory_context.py` (отметки владельца, классификатор «Сводки») и `test_suggest_query.py` — должны остаться зелёными.
- Regenerate: `crWappiProxy0001-Wappi_Proxy.json`, `9PTyYcelRQI7bGDb-Suggest_Replies.json`, `2htWSV5IHO8E2CgB-Dashboard_Outcomes.json`.

- [ ] **Step 1: Падающие тесты:**
  - `owner_app_in_personal_or_undecided_writes_nothing` и `owner_app_in_business_writes_history_and_memory`;
  - `suggest_refused_for_personal_and_undecided_counts_nothing` — ни строки в `suggestion_request_keys` и `suggestion_counts`;
  - `suggest_business_unchanged`;
  - `dashboard_skips_personal_in_classifier_and_fetch`.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_personal_readers.py` → `… fail`.
- [ ] **Step 3: Реализовать, перегенерировать три workflow.**
- [ ] **Step 4: PASS** — Run: `python3 Tools/n8n/tests/run_all.py` и `verify-auth-gate.py`, `verify-history-handoff.py`, `verify-sql-comments.py` → зелёные.
- [ ] **Step 5: Commit** — `feat(personal-chats): прокси, подсказки и «Сводка» не пишут и не читают личные чаты`.

### Task 6: Линия ИИ-сотрудника — перенос решений и удаление вместе с ним

**Files:**
- Modify: `Tools/n8n/clone_lineage.py`:
  - `MOVED_TABLES` += `chat_audience`, `audience_connections`;
  - CTE переноса в `_BIND_TEMPLATE`;
  - счётчики `moved_audience`, `moved_connections`, они же входят в `dropped_rows`.
- Modify: `Tools/n8n/apply-history-handoff.py` — `DELETE_NEW_TAIL` удаляет обе таблицы для всех профилей линии; в ответе `deletedAudience`, `deletedConnections`.
- Modify: `Tools/n8n/apply-auth-gate.py` — `DELETION_SETTLED`: оба счётчика должны быть числами.
- Modify тесты и проверки:
  - `tests/test_clone_lineage_sql.py`, `test_create_clone.py`, `test_clone_retirements.py` (L6 — список таблиц), `test_learning_a.py` (`MEMORY_TABLES`), `test_retired_delete.py` (D1 — счётчики, N2 — негативы);
  - `verify-history-handoff.py` (список T5), `verify-auth-gate.py` (счётчики).
- Regenerate: Create WA/TG (`apply-clone-lifecycle.py`), Delete Bot Files (`apply-auth-gate.py` + `apply-history-handoff.py`).

Правила переноса:
- **`chat_audience`, по чату.** На новом профиле остаётся одна строка. Строка владельца важнее вычисленной; из равных — собственная строка нового профиля; среди предшественников — самая свежая. Вычисленные строки переезжают как есть: перенесённое подключение сохраняет их действующими.
- **`audience_connections`.** Собственная строка нового профиля остаётся, если её номер известен и отличается от известного номера самой ранней строки предшественников — это смена номера. Иначе на новом профиле — строка с самой ранней `connected_at` и её номером. Остальные строки удаляются.

- [ ] **Step 1: Падающие тесты:**
  - `reconnect_moves_decisions_and_keeps_the_moment`;
  - `owner_on_predecessor_beats_default_on_new`;
  - `earliest_connection_wins_over_a_lazy_row_of_the_new_profile`;
  - `own_row_wins_when_the_number_changed`;
  - M5 — `sorted(MOVED_TABLES)` равен таблицам `Delete Chat Memory`;
  - `bot_delete_wipes_both_tables_for_the_line`, счётчики в `Respond`;
  - `deletion_settled_refuses_a_non_number_counter`.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_clone_lineage_sql.py && python3 Tools/n8n/tests/test_retired_delete.py`.
- [ ] **Step 3: Реализовать, перегенерировать Create WA/TG и Delete Bot Files.**
- [ ] **Step 4: PASS** — Run: `python3 Tools/n8n/tests/run_all.py` и `verify-clone-lifecycle.py`, `verify-auth-gate.py`, `verify-history-handoff.py` → зелёные.
- [ ] **Step 5: Commit** — `feat(personal-chats): решения переезжают по линии и удаляются вместе с ИИ-сотрудником`.

### Task 7: Вебхук `ChatAudience` (`crChatAudience01`)

**Files:**
- Create: `Tools/n8n/build-chat-audience.py` (`--check`), `Tools/n8n/workflows/crChatAudience01-Chat_Audience.json`
- Create: `Tools/n8n/tests/test_chat_audience.py`
- Modify: `Tools/n8n/verify-personal-chats.py` — проверки вебхука
- Modify: `build-client-webhooks.py` (`PROTECTED_IDS`), `staging/bootstrap.py` (`ACTIVE_CANONICAL`), Set Workflow State (якорь `  'crAudienceDec001'\n]);`); затем перезапустить три зависимых генератора, как в Task 3.

**Interfaces:**

```
POST /webhook/ChatAudience   Authorization: Bearer <токен сессии>
RULE = {"profiles": ["profileIds", "profileId"], "workflows": [], "appUser": [], "legacy": False}   # auth.gate_code(RULE), как build-set-schedule.py
{op:"list", profileIds:[≤ 20]}                         → 200 {success:true, profiles:[{profileId, connectedAt:<unix ms>|null, decisions:[{chatId, kind, source, reason, version}]}]}
{op:"set", profileId, chats:[{chatId, kind, version} ≤ 50]} → 200 {success:true, results:[{chatId, applied, kind, version, refused:null|"nonhuman"}]}
{op:"connected", profileId}                             → 200 {success:true, connectedAt}  | 409 not_authorized | 502 wappi_unavailable
отказы: 401/403 — защита; 400 bad_request + reason; 500 db_unavailable
```

Граф:
1. **Вход.** `Webhook` → `Auth Gate` → `Authorized?` → `Validate`. Validate берёт id заново из тела: отбрасывает `-1` и `''`, пересекает с `crAuth.profiles`, убирает повторы. Лимиты — по операции; `kind` ∈ {personal, business}; `version` — положительное безопасное целое; `chatId` — непустой, ≤ 128 символов, без пробелов.
2. **Развилка по `op`** — тремя If.
3. **list:** `List` (PG `LIST_SQL`) → `Shape List` → `Respond`.
4. **set:** `Set` (PG `OWNER_SET_SQL`) → `Shape Set` → `Respond`, затем начало разговора и повторная подача:
   - `Replay Items` (Code: строки с `became_business`);
   - `Recent Messages` — `{api|tapi}/sync/messages/get?…&limit=10&order=desc`, без `mark_all`; страница уходит в `Backfill` ровно как её вернул Wappi, новые первыми;
   - `Replay Plan` (Code): повод для повторной подачи — самое новое сообщение, если оно от клиента (не от нас) и `time ≥ now − FRESH_HOURS × 3600`. Тело webhook собирается из строки по записи D6;
   - `Backfill` (PG `BACKFILL_SQL`; continue) — id повода передаётся, только если подача будет: несвежее или уже отвеченное сообщение попадает в историю;
   - `Replay?` → `Replay` (POST `http://localhost:5678/webhook/<profileId>`, `neverError`, 10 с).
5. **connected:** `Channel` (PG: канал профиля из `bot_profiles`) → `Status` (`{api|tapi}/sync/get/status`) → `Phone` (Code: `OWNER_PHONE_JS` из `build-handoff-workflows.py` дословно; эхо профиля и `authorized === true`) → `Connected` (PG `CONNECTED_SQL`) → `Shape Connected` → `Respond`.
6. **Ответы.** `Shape *` судят по положительным полям строк. Успешные выполнения не сохраняются.

- [ ] **Step 1: Падающие тесты** (настоящие JS защиты и Validate, выражения n8n, SQL на временных таблицах, подставной Wappi):
  - защита и Validate: только профили токена, лимиты, неверные `kind` и `version`;
  - `list_returns_only_valid_decisions_and_the_moment`;
  - `set_version_guard_and_nonhuman_refusal`;
  - `set_business_with_fresh_waiting_message_replays_once`;
  - `set_business_backfills_before_the_replay`;
  - `stale_waiting_message_not_replayed` (25 ч) — но попадает в историю через `Backfill`;
  - `owner_answered_last_not_replayed`;
  - `set_personal_never_replays`;
  - `respond_comes_before_the_replay`;
  - `connected_first_time_takes_the_line_moment`;
  - `connected_other_phone_resets`;
  - `connected_unauthorized_is_409`.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_chat_audience.py`.
- [ ] **Step 3: Реализовать генератор, сгенерировать workflow, дописать проверки.** Проверки:
  - `legacy: False`;
  - `Respond` стоит раньше цепочки повторной подачи;
  - URL повторной подачи — localhost;
  - `mark_all` нигде нет;
  - `saveDataSuccessExecution: "none"`;
  - id в списках защиты.

  Run: `python3 Tools/n8n/build-chat-audience.py && python3 Tools/n8n/build-chat-audience.py --check && python3 Tools/n8n/verify-personal-chats.py`
  Expected: exit 0, `personal chats verify: OK`.
- [ ] **Step 4: PASS** — `test_chat_audience.py`, `run_all.py`, `verify-auth-gate.py`, `verify-sql-comments.py` → зелёные.
- [ ] **Step 5: Commit** — `feat(personal-chats): вебхук ChatAudience — список, решения владельца, подключение`.

### Task 8: Шаг Sweep — стереть данные чата через 2 дня личного

**Files:**
- Create: `Tools/n8n/apply-personal-wipe.py` (`--check`). Он владеет узлом `Wipe Personal Chats` в `fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json` — по образцу `apply-learning-a.py` `patch_sweep`: ещё одна цель `Every 6 Hours`, место под `Expire Learning Rows`, от `dryRun` не зависит.
- Modify: `Tools/n8n/audience.py` — `WIPE_SQL`
- Modify: `Tools/n8n/verify-personal-chats.py` — проверка узла
- Create: `Tools/n8n/tests/test_personal_wipe.py`

`WIPE_SQL` — один запрос:
- берёт до 200 строк `source = 'owner' and kind = 'personal' and wiped_at is null and personal_since <= now() − WIPE_DAYS`, старые первыми;
- удаляет их данные из `n8n_chat_histories` (`session_id = профиль || ':' || чат`), `chat_history_events`, `conversation_outcomes`, `handoff_requests`, `reply_events`, `chat_reply_state`, `suggestion_sets` (по профилю и чату) и строку этого чата в `reply_mode_flags` (никогда `'*'`);
- ставит `wiped_at = now()`;
- возвращает число затронутых чатов и строк.

`dialog_counts` не трогается никогда.

- [ ] **Step 1: Падающие тесты:**
  - `not_before_two_days`;
  - `wipes_after_two_days_keeps_dialog_counts`;
  - `returned_within_two_days_untouched`;
  - `idempotent_second_run_deletes_nothing`;
  - `other_chats_and_star_flag_untouched`;
  - `default_personal_rows_never_selected`.
- [ ] **Step 2: FAIL** — Run: `python3 Tools/n8n/tests/test_personal_wipe.py`.
- [ ] **Step 3: Реализовать, перегенерировать Sweep.**

  Run: `python3 Tools/n8n/apply-personal-wipe.py && python3 Tools/n8n/apply-personal-wipe.py --check`
  Expected: `patched …Profile_Lifecycle_Sweep.json`, затем exit 0.
- [ ] **Step 4: PASS** — тест, `verify-personal-chats.py`, `verify-clone-lifecycle.py`, `verify-sql-comments.py` → зелёные.
- [ ] **Step 5: Commit** — `feat(personal-chats): через 2 дня личного чата его данные стираются`.

### Task 9: Стенд — мок Wappi и сценарии e2e (WhatsApp и Telegram)

**Files:**
- Modify: `Tools/n8n/staging/wappi-mock.py` — маршрут `POST /__mock/audience` задаёт по профилю:
  - диалоги `chats/get` (id, `type`, флаги `user`);
  - для каждого чата — самое старое и самое новое сообщение (`messages/get?order=asc|desc&limit=1`);
  - номер `get/status`;
  - режим сбоя (500) для каждого маршрута.
- Modify: `Tools/n8n/staging/e2e-reply.py` — `event()` принимает `contact_name`, `chat_type`, `is_bot`.
- Create: `Tools/n8n/staging/e2e-audience.py` — реестр `@scenario` / `@for_channels`.

Сценарии (оба канала, если не сказано иное):
- `contact_no_reply_nothing_written`, `new_person_replies`, `old_chat_no_reply`, `settle_window_no_reply_no_write`;
- `wappi_failure_then_next_message_decides`, `make_business_fresh_replies_once`, `make_business_stale_no_reply`, `make_personal_cancels_inflight`;
- `tg_bot_777000_channel_no_reply` (Telegram), `wa_newsletter_no_reply` (WhatsApp);
- `owner_app_send_in_personal_not_recorded`, `suggest_refused_for_personal`;
- `reconnect_keeps_decisions`, `delete_bot_wipes_decisions`, `number_change_voids_defaults`;
- `wipe_after_two_days` (`personal_since` сдвигается назад), `dashboard_hides_personal`.

- [ ] **Step 1: Сценарии на каноне ДО этой работы — должны упасть.** Run: `python3 Tools/n8n/staging/bootstrap.py --canon-ref <sha до Task 1> --bot wa:stgwa001 --bot tg:stgtg001`, затем `python3 Tools/n8n/staging/e2e-audience.py`. Expected: FAIL (контакту отвечает, вебхука нет).
- [ ] **Step 2: Те же сценарии на рабочем каноне.**

  Run:
  1. `Tools/n8n/staging/pg.sh start`
  2. `python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001`
  3. `WAPPI_MOCK_LOG=Tools/test-output/wappi-mock.jsonl python3 Tools/n8n/staging/wappi-mock.py &`
  4. дождаться, пока `ChatAudience` и webhook'и ботов отвечают на GET «Did you mean to make a POST request?»;
  5. `python3 Tools/n8n/staging/e2e-audience.py`

  Expected: все PASS.
- [ ] **Step 3: Прежние наборы зелёные:** `e2e-reply.py`, `e2e-handoff.py`, `e2e-auth.py`, `e2e-suggest.py`, `e2e-learning.py`.
- [ ] **Step 4: Commit** — `test(personal-chats): мок Wappi и сценарии e2e для двух каналов`.

### Task 10: Приложение — факты чата и правило

**Files:**
- Modify: `Assets/Scripts/Chat/ChatDialog.cs`:
  - `[Serializable] public class ChatDialogContact { public string FullName; }`;
  - `[Serializable] public class ChatDialogUser { public bool Contact; public bool Bot; public bool Support; public bool Self; }`;
  - поля `public ChatDialogContact contact; public ChatDialogUser user;` (`type` уже есть).

  Поле `Bot` внутри `ChatDialogUser` законно, но затеняет класс `Bot`, поэтому в этом классе только четыре поля. JsonUtility не оставляет эти объекты null: если объекта нет, он читается как «всё false / пустое имя».
- Modify: `Assets/Scripts/UI/ChatViewModel.cs` — `public AudienceFacts Facts { get; private set; }`
- Modify: `Assets/Scripts/Main/ChatManager.cs` — `ParseChatsJson` строит факты
- Create: `Assets/Scripts/Chat/ChatAudienceRule.cs`
- Create: `Assets/Tests/Editor/Chat/ChatAudienceRuleTests.cs`, `Assets/Tests/Editor/Chat/ChatDialogAudienceParseTests.cs`

**Interfaces:**

```csharp
public enum AudienceKind { Personal, Business }
public enum AudienceReason { NonHuman, Owner, Contact, History, New, Unknown }
public enum NonHumanKind { None, Group, Channel, Bot, Service, Self }
public readonly struct AudienceFacts {
    public AudienceFacts(bool telegram, string chatId, string chatType, bool? bot, bool? support, bool? self, bool? contact);
    public bool Telegram { get; } public string ChatId { get; } public string ChatType { get; }
    public bool? Bot { get; } public bool? Support { get; } public bool? Self { get; } public bool? Contact { get; }
}
public static class ChatAudienceRule {
    public const int SettleMinutes = 30, FreshHours = 24, WipeDays = 2;
    public static NonHumanKind NonHuman(in AudienceFacts f);
    public static (AudienceKind Kind, AudienceReason Reason) Decide(in AudienceFacts f, bool? history, AudienceKind? owner);
    public static bool? HistoryBeforeConnection(long lastMessageUnixSec, long? connectedAtMs);  // < момента → true; иначе false; момент неизвестен → null
}
```

Как приложение берёт факты:
- **WhatsApp:**
  - `contact` = `!string.IsNullOrWhiteSpace(contact.FullName)`;
  - `self` — цифры `ChatId` равны цифрам сохранённого `{bot}WhatsappNumber`;
  - `bot = false`.
- **Telegram** — `user.*` и `type`.

- [ ] **Step 1: Падающие тесты:**
  - `EveryFixtureCase_MatchesTheServer` — `TestCaseSource` читает `Path.Combine(Application.dataPath, "..", "Tools", "n8n", "fixtures", "audience-cases.json")` (Newtonsoft, `DateParseHandling.None`, как `ScheduleModelTests`);
  - `Constants_MatchTheFixture`;
  - `NonHuman_GivesTheKindForTheSubline` — `@g.us` → Group, `@newsletter` и `channel` → Channel, Bot, `777000` → Service, Self;
  - `HistoryBeforeConnection_ThreeCases`;
  - `WhatsappContactFullName_Parses`, `TelegramUserFlags_Parse`, `MissingObjects_ReadAsAllFalse`.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "ChatAudienceRuleTests|ChatDialogAudienceParseTests"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: PASS** — Run: `Tools/run-tests-headless.sh "ChatAudienceRuleTests|ChatDialogAudienceParseTests|ChatManager"` → `failed="0"`.
- [ ] **Step 5: Commit** — `feat(personal-chats): факты чата и правило в приложении по общей таблице`.

### Task 11: Приложение — зеркало решений, очередь желаний и синхронизация

**Files:**
- Create: `Assets/Scripts/Chat/ChatAudienceRequest.cs` — чистые тела запросов и разбор ответов
- Create: `Assets/Scripts/Chat/ChatAudienceStore.cs` — зеркало, кэш `persistentDataPath/audience_cache.json`, наложение неподтверждённых желаний
- Create: `Assets/Scripts/Chat/ChatAudienceSyncQueue.cs` — чистая очередь, PlayerPrefs `CrAudience.Queue`
- Create: `Assets/Scripts/Chat/ChatAudienceSyncService.cs`
- Create: `Assets/Scripts/Chat/ChatAudienceConnectPolicy.cs`
- Create: `Assets/Scripts/UI/PersonalChatsOnboarding.cs` — пока только флаги экрана подключения; показ добавит Task 14
- Modify: `Assets/Scripts/Main/Manager.cs`:
  - мастер — после `Authorized` до `Create*FromStart`: `ReportConnected` и `PersonalChatsOnboarding.Request`;
  - `DeleteBotOnServer` — `Hold` перед первым запросом к серверу, `Release`, если сервер оставил ИИ-сотрудника.
- Modify: `Assets/Scripts/Main/BotSettings.Auth.cs` — в `OnWhatsappAuthFromSettingsDone` и в его двойнике для Telegram, ДО того как сохранённый номер перезаписывается: `ReportConnected`; `PersonalChatsOnboarding.Request`, если подключение первое или номер сменился.
- Modify: `Assets/Scripts/Main/Bot.cs` — `DeleteBotLocally`: `ForgetBot` и `PersonalChatsOnboarding.Clear` для обоих каналов
- Modify: `Assets/Scripts/Main/ProfileSubPages.Account.cs` — `FinishWipe`: `ForgetAll()` перед `PlayerPrefs.DeleteAll`
- Create тесты: `Assets/Tests/Editor/Chat/ChatAudienceRequestTests.cs`, `ChatAudienceSyncQueueTests.cs`, `ChatAudienceStoreTests.cs`, `ChatAudienceWiringTests.cs`

**Interfaces:**

```csharp
public sealed class AudienceView {
    public AudienceKind Kind; public AudienceReason Reason; public NonHumanKind NonHuman;
    public bool Decided;   // у сервера действующее решение
    public bool Pending;   // желание владельца ещё не подтверждено
    public bool CanMove => NonHuman == NonHumanKind.None;
}
public static class ChatAudienceStore {
    public static event Action OnChanged;
    public static void ApplyList(IReadOnlyList<AudienceProfileState> profiles);   // заменяет эти профили, пишет кэш
    public static void ApplyConnected(string profileId, long connectedAtMs);
    public static void ApplyDesire(string profileId, string chatId, AudienceKind kind);          // рисуется сразу
    public static void ApplyDesireResult(string profileId, string chatId, AudienceKind serverKind, bool applied);
    public static long? ConnectedAtMs(string profileId);
    public static AudienceKind? KindOf(string profileId, string chatId);      // желание, иначе решение сервера, иначе null (без правила)
    public static AudienceView ViewOf(string profileId, ChatViewModel chat);  // KindOf, иначе правило по фактам и оценке истории
    public static bool AllowsAssistant(string profileId, string chatId);      // KindOf == Business
    public static void ForgetProfile(string profileId); public static void ClearAll();
}
public class ChatAudienceSyncService : MonoBehaviour {      // RuntimeInitializeOnLoadMethod(AfterSceneLoad); первая отправка — после ReplyModeSendGate.CanSend
    public static event Action<string, IReadOnlyList<string>> OnWriteFailed;   // (botName, chatIds): сервер не принял, чаты вернулись на место
    public static void MakeBusiness(string botName, string profileId, IReadOnlyList<string> chatIds);
    public static void MakePersonal(string botName, string profileId, IReadOnlyList<string> chatIds);
    public static void ReportConnected(string profileId);
    public static void RequestList(bool force = false);
    public static void Hold(string botName); public static bool IsSending(string botName); public static void Release(string botName);
    public static void ForgetBot(string botName); public static void ForgetAll();
}
public static class ChatAudienceConnectPolicy {
    public static bool IsFirstConnection(string previousProfileId, string retiredProfileId);   // оба пусты или "-1"
    public static bool NumberChanged(string storedNumber, string authedNumber);              // цифры обоих известны и различаются
}
public static class PersonalChatsOnboarding {
    public static string FlagKey(string botName, bool telegram);   // "{bot}AudienceOnboardingWhatsapp" | "{bot}AudienceOnboardingTelegram"
    public static void Request(string botName, bool telegram);
    public static bool IsRequested(string botName, bool telegram);
    public static void Clear(string botName, bool telegram);
}
```

Поведение:
- **Очередь** (модель `ReplyModeSyncQueue`):
  - одно желание на `(profileId|chatId)`, версия `max(nowMs, last + 1)`;
  - паузы 2 / 5 / 15 / 60 / 300 с, не больше 8 попыток за запуск;
  - заново взводится при возвращении в приложение (не чаще раза в минуту) и при запуске;
  - 400/401/403 → отказ;
  - `applied:false` → принять вид сервера;
  - записи `connected` — отдельный список той же очереди, уходят первыми.
- **Чтение списка.** При запуске (после ворот отправки), на `ChatManager.OnActiveBotChanged`, на `OnApplicationFocus(true)` не чаще раза в минуту и через 2 с после каждого доставленного пакета.
- **Неудача записи.** Желание снимается, затем `OnChanged` и `OnWriteFailed`; тост на это событие подписывает Task 12.

- [ ] **Step 1: Падающие тесты:**
  - `SetBody_ShapesTheContract`, `ListAnswer_ParsesDecisionsAndMoment`, `ConnectedAnswer_Parses`;
  - `Queue_OneDesirePerChat_VersionGrows`, `Queue_SurvivesRestart`, `Queue_RejectedOn403_NotRetriedUntilRearmed`;
  - `Store_ServerDecisionBeatsRule_DesireBeatsServer`, `Store_UndecidedUsesTheRule`, `Store_AllowsAssistantOnlyForBusiness`, `Store_ForgetProfile`;
  - `ConnectPolicy_FirstConnectionAndNumberChanged`, `OnboardingFlags_RequestAndClear`;
  - стражи исходников:
    - успешные пути мастера и настроек вызывают `ChatAudienceSyncService.ReportConnected(`;
    - `DeleteBotOnServer` вызывает `Hold(` до первого запроса и `Release(`;
    - `DeleteBotLocally` вызывает `ForgetBot(`;
    - `FinishWipe` вызывает `ForgetAll()` раньше `PlayerPrefs.DeleteAll`;
    - сервис отправляет только через `AuthorizedRequest.Send`.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "ChatAudience"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: PASS** — Run: `Tools/run-tests-headless.sh "ChatAudience|AuthWiringGuardTests|BotDeletionWiringTests|ReplyModeSyncWiringTests"` → `failed="0"`.
- [ ] **Step 5: Commit** — `feat(personal-chats): зеркало решений и синхронизация с сервером`.

### Task 12: Список чатов — рабочие чаты, строка «Личные», счётчики, уведомления, поиск

**Files:**
- Create: `Assets/Scripts/UI/PersonalChatsCopy.cs` — ВСЕ строки функции (таблица ниже)
- Create: `Assets/Scripts/UI/PersonalChatsListModel.cs` — чистая модель
- Create: `Assets/Scripts/UI/PersonalChatsRowView.cs`
- Create: `Assets/Scripts/UI/AudienceToast.cs` — ленивый оверлей, свой canvas `sortingOrder 3`, держится 3 с; `public static void Show(string text)`; подписан на `ChatAudienceSyncService.OnWriteFailed` и показывает `WriteFailed`
- Create: `Assets/Editor/PersonalChatsBuilder.cs` — меню `Tools/Personal Chats/Build`, headless `BuildHeadless`, лог `[PersonalChatsBuilder] Headless build + save complete.`; в этой задаче — `EnsureListRow`, `EnsureChatItemLock`
- Modify:
  - `Assets/Scripts/UI/ChatListView.cs` — `ApplyMatchToItem` по модели; на `ChatAudienceStore.OnChanged` фильтр применяется заново; обновление строки «Личные»;
  - `ChatItemView.cs` — `SetAudienceMarks(bool lockMark, bool greyBadge)`;
  - `ChannelSwitcherView.cs` — `ActiveUnreadTotal` только по рабочим;
  - `BotChatStats.cs` — непрочитанные только по рабочим;
  - `Assets/Scripts/Chat/IncomingNotifyPolicy.cs` — параметр `bool chatIsPersonal` → `false`;
  - `ChatManager.cs` — оба вызова `ShouldNotify` передают `view.Kind == AudienceKind.Personal`.
- Modify: `Assets/Tests/Editor/FontGlyphCoverageTests.cs` — `typeof(PersonalChatsCopy)` в список швов
- Create тесты: `Assets/Tests/Editor/Chat/PersonalChatsListModelTests.cs`, `PersonalChatsCopyTests.cs`, `PersonalChatsBuilderPremiseTests.cs`; дополнить `IncomingNotifyPolicyTests.cs`.

**Interfaces:**

```csharp
public static class PersonalChatsListModel {
    public static bool Visible(AudienceView v, bool searchActive);          // рабочий, или идёт поиск
    public static bool ShowLock(AudienceView v, bool searchActive);         // поиск и личный
    public static bool RowVisible(int personalCount, bool searchActive);    // есть личные и поиска нет
    public static int PersonalUnreadChats(IEnumerable<(AudienceView v, int unread)> chats);
    public static int WorkingUnreadTotal(IEnumerable<(AudienceView v, int unread)> chats);
}
public class PersonalChatsRowView : MonoBehaviour {
    public static event Action Tapped;
    public void Bind(int personalUnreadChats);   // 0 — счётчик скрыт
}
```

**Строка «Личные»** — объект `PersonalChatsRow` в `Content` списка, сразу после `ChatsSearchBar`. `ApplyOrderNow` оставляет чужих детей на месте.
- Высота — как у `ChatItem`.
- Замок `Assets/Images/Icons/Lock.png` с `ThemedColor(InkTertiary)` в круге.
- Заголовок «Личные» шрифтом имени чата.
- Серый счётчик (`InkTertiary`) — число личных чатов с непрочитанными; при 0 счётчик скрыт.
- Нажатие поднимает событие `PersonalChatsRowView.Tapped`; открытие папки на него подписывает Task 14.

**Замок в строке чата** — `AudienceLock` в `ChatItem.prefab`, по умолчанию неактивен.

`PersonalChatsCopy` (точные строки):

```
RowTitle = "Личные";  FolderTitle = "Личные";  Edit = "Изменить";  Cancel = "Отмена"
FolderHint = "Контакты из телефона, все, кто писал до подключения, группы и каналы. ИИ-сотрудник здесь не участвует, уведомлений нет."
FolderFooter = "Свайп по чату — сделать его рабочим"
EditTitle = "Отметьте клиентов";  EditSearch = "Поиск по личным";  MakeBusinessMany(n) = "Сделать рабочими · " + n
SelectAtLeastOne = "Отметьте хотя бы один чат"
OnboardingConnected(telegram) = "WhatsApp подключён" | "Telegram подключён"
OnboardingTitle = "Кто из них клиенты?"
OnboardingBody = "Все ваши контакты и все, с кем вы переписывались до подключения, — личные: ИИ-сотрудник их не трогает. Отметьте клиентов, с ними он будет работать."
OnboardingSearch = "Поиск по чатам";  OnboardingSection = "НЕДАВНИЕ ЧАТЫ"
SublineContact = "в контактах";  SublineHistory = "не в контактах, писал раньше"
OnboardingDone(n) = "Готово · клиентов: " + n;  OnboardingLater = "Отмечу позже"
OnboardingLoading(telegram) = "Загружаем чаты из WhatsApp…" | "Загружаем чаты из Telegram…"
OnboardingNoContacts(telegram) = "WhatsApp на телефоне, похоже, не видит контакты. Клиентов можно отметить вручную." (Telegram — то же со словом «Telegram»)
NonHumanGroup = "Группа · ИИ-сотрудник в группах не отвечает";  NonHumanChannel = "Канал · ИИ-сотрудник каналам не отвечает"
NonHumanBot = "Бот · ИИ-сотрудник ботам не отвечает";  NonHumanService = "Служебные уведомления · ИИ-сотрудник им не отвечает"
NonHumanSelf = "Избранное · сообщения самому себе"
SwipeToPersonal = "В личные";  SwipeToBusiness = "В рабочие";  SwipeDelete = "Удалить"
MovedToPersonal = "Чат перенесён в «Личные»";  MovedToBusiness = "Чат стал рабочим"
WriteFailed = "Не удалось перенести чат — проверьте интернет";  SaveFirst = "Сначала сохраните изменения"
Pill = "Личный";  MakeBusinessTitle = "Сделать чат рабочим?";  MakeBusinessYes = "Сделать рабочим";  MakeBusinessNo = "Оставить личным"
MakeBusinessAutoFresh = "У вас включено «Авто»: ИИ-сотрудник сразу ответит на последнее сообщение и дальше будет отвечать сам. Выключить «Авто» в этом чате можно отдельно."
MakeBusinessAutoNoFresh = "У вас включено «Авто»: ИИ-сотрудник будет отвечать сам на новые сообщения. Выключить «Авто» в этом чате можно отдельно."
MakeBusinessStaleNote = "На сообщения старше суток он не отвечает — начнёт с нового."
MakeBusinessManual = "ИИ-сотрудник будет подсказывать ответы, а отправлять их будете вы."
MakePersonalAction = "Сделать личным";  ContactMark = "в контактах";  NewMark = "новый"
MakePersonalBody(days) = "ИИ-сотрудник перестанет участвовать в этом чате. Через " + days + " " + RuPlural.Pick(days, "день", "дня", "дней") + " он забудет эту переписку."
SettingsHeader = "ЛИЧНЫЕ ЧАТЫ";  SettingsRow = "Личные чаты"
SettingsHint = "Контакты из телефона и все, кто писал до подключения. ИИ-сотрудник в них не участвует. Внутри можно отметить клиентов — с ними он будет работать. Ботам, каналам и рассылкам он не отвечает никогда."
```

- [ ] **Step 1: Падающие тесты:**
  - `Visible_WorkingAlways_PersonalOnlyInSearch`, `ShowLock_OnlySearchAndPersonal`, `RowVisible_NeedsPersonalChatsAndNoSearch`;
  - `PersonalUnreadChats_CountsChatsNotMessages`, `WorkingUnreadTotal_SkipsPersonal`;
  - `IncomingNotifyPolicy`: `Personal_NeverNotifies`;
  - `MakePersonalBody_SaysTwoDays` («Через 2 дня»);
  - `StaleNote_MatchesFreshHours` — `ChatAudienceRule.FreshHours == 24`, пока текст говорит «суток»;
  - `FontGlyphCoverageTests` — строки покрыты;
  - builder: `PersonalChatsRow` — сразу после `ChatsSearchBar`, замок `Lock.png`, `PersonalChatsRowView` со ссылками; `AudienceLock` в `ChatItem.prefab` неактивен.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "PersonalChatsListModelTests|PersonalChatsCopyTests|IncomingNotifyPolicyTests|PersonalChatsBuilderPremiseTests|FontGlyphCoverageTests"`.
- [ ] **Step 3: Реализовать код, затем builder при закрытом Editor.**

  Run: `Tools/run-editor-builder.sh PersonalChatsBuilder.BuildHeadless`
  Expected: exit 0, в `Tools/test-output/builder.log` строка `[PersonalChatsBuilder] Headless build + save complete.`
- [ ] **Step 4: PASS** — тот же запуск тестов и `ChannelSwitcher|BotChatStats` → `failed="0"`.
- [ ] **Step 5: Commit сразу:** код, builder, `Main.unity`, `ChatItem.prefab`, тесты, `.meta` — `feat(personal-chats): рабочий список, строка «Личные», счётчики и уведомления`.

### Task 13: Свайп «В личные» / «В рабочие»

**Files:**
- Modify: `Assets/Scripts/Chat/SwipeToDelete.cs`:
  - `[SerializeField] RectTransform moveButton`;
  - `public void SetActions(bool move, bool delete)` — показывает видимые кнопки справа налево («Удалить» крайняя);
  - `revealWidth` = сумма ширин видимых; без действий компонент выключается.
- Modify: `Assets/Scripts/Chat/ChatRowSwipePolicy.cs` — `Enabled(channel)` заменяется на `Actions`, вызывающие места обновить.
- Modify: `ChatItemView.cs` (`Bind` читает `Actions`; `SetMoveLabel(string)`), `ChatListView.cs` (`RequestMove(chat)` → `ChatAudienceSyncService.MakePersonal` + тост `MovedToPersonal`)
- Modify: `PersonalChatsBuilder.cs` — `EnsureChatItemMoveButton`: `MoveButton` 200u слева от `DeleteButton` в `ChatItem.prefab`; надпись TMP, фон `ThemedColor(AccentFill)`; ссылка `moveButton` проставлена.
- Create тесты: `Assets/Tests/Editor/Chat/ChatRowSwipePolicyTests.cs` (переписать), `SwipeActionsLayoutTests.cs`; дополнить `PersonalChatsBuilderPremiseTests.cs`.

**Interfaces:**

```csharp
public static class ChatRowSwipePolicy {
    public static (bool Move, bool Delete) Actions(ChatChannel channel, bool inFolder, NonHumanKind nonHuman);
    // список: Move = nonHuman == None («В личные»), Delete = channel == WhatsApp; папка: Move = nonHuman == None («В рабочие»), Delete = false
}
public static class SwipeActionsLayout {
    public static (float revealWidth, float moveX, float deleteX) Solve(bool move, bool delete, float moveWidth, float deleteWidth);
}
```

- [ ] **Step 1: Падающие тесты:** `Actions_Matrix` (оба канала × список/папка × «не человек»), `Solve_OneOrTwoButtons`, `Solve_NoButtons_ZeroWidth`; builder — `MoveButton` есть, 200u, стоит перед `DeleteButton`, ссылка проставлена.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "ChatRowSwipePolicyTests|SwipeActionsLayoutTests|PersonalChatsBuilderPremiseTests"`.
- [ ] **Step 3: Реализовать, прогнать builder.**
- [ ] **Step 4: PASS** — тот же запуск и `ChatDelete` → `failed="0"`.
- [ ] **Step 5: Commit сразу** (префаб общий) — `feat(personal-chats): свайп «В личные» и «В рабочие»`.

### Task 14: Папка «Личные», «Изменить» и «Кто из них клиенты?»

Ждёт Task 0 (D1 — подписи «в контактах»).

**Files:**
- Modify: `PersonalChatsBuilder.cs` — `EnsureFolderPanel`: `Screen_Messanger/PersonalChatsPanel` сразу после `ChatsPanel` и раньше `MessagesPanel`. Состав:
  - `Header` (`Back`, `Title`, `Edit`), `StatusLine`, `Body`;
  - `Search` — `TMP_InputField` с единой настройкой клавиатуры (инварианты `BusinessContactFieldsBuilder`);
  - `SectionHeader`, `Scroll/Viewport/Content`, `LoadingLabel`, `NoContactsNote`;
  - `BottomBar` (`Primary`, `Secondary`), `Footer`;
  - `SwipeToBackPanel`, у которого `OnCommitted` → `PersonalChatsFolderView.Close` (образец `ProfileSubPages`);
  - в `ChatItem.prefab` — `SelectCheck`, по умолчанию неактивен.
- Create: `Assets/Scripts/UI/PersonalChatsFolderModel.cs`, `PersonalChatsFolderView.cs`
- Modify: `Assets/Scripts/UI/PersonalChatsOnboarding.cs` (из Task 11) — показ экрана
- Modify: `ChatItemView.cs` — `SetSelectable(bool selectable, bool selected)`; в режимах выбора нажатие переключает галочку
- Modify: `Assets/Scripts/Main/AndroidBackRouter.cs` — папка в `Targets()` после списка чатов и раньше открытого чата. Back: обычный режим — закрыть; «Изменить» — «Отмена»; режим подключения — «Отмечу позже».
- Create тесты: `Assets/Tests/Editor/Chat/PersonalChatsFolderModelTests.cs`, `PersonalChatsOnboardingTests.cs`; дополнить `BackNavigationTests.cs` и `PersonalChatsBuilderPremiseTests.cs`.

**Interfaces:**

```csharp
public enum FolderMode { Browse, Edit, Onboarding }
public sealed class FolderRow { public ChatViewModel Chat; public AudienceView View; public string Subline; public bool Selectable; }
public static class PersonalChatsFolderModel {
    public static IReadOnlyList<FolderRow> Rows(IReadOnlyList<(ChatViewModel chat, AudienceView view)> chats, FolderMode mode, string query);
    // Browse — все личные (и «не люди»), новые сверху; Edit/Onboarding — только CanMove; query — по названию и номеру
    public static string Subline(AudienceView v, ChatViewModel c, FolderMode mode);  // «не человек» — его строка; Onboarding — «в контактах» / «не в контактах, писал раньше»; иначе последнее сообщение
    public static string PrimaryLabel(FolderMode mode, int selected);   // Edit — MakeBusinessMany(n), Onboarding — OnboardingDone(n)
    public static bool ShowNoContactsNote(IReadOnlyList<FolderRow> rows, bool loaded);
}
public class PersonalChatsFolderView : MonoBehaviour {
    public static PersonalChatsFolderView Instance { get; }
    public void Open(FolderMode mode);   // активный ИИ-сотрудник и канал экрана чатов
    public void Close(); public bool IsOpen { get; } public bool TryBack();
}
public static class PersonalChatsOnboarding {   // FlagKey / Request / IsRequested / Clear — из Task 11
    public static bool ShouldShow(bool flagSet, bool chatsScreenVisible, bool botActive, bool channelActive);
    public static void TryShow();      // из OnEnable экрана чатов, на OnActiveBotChanged и при смене канала
}
```

Поведение:
- **Вход.** `PersonalChatsRowView.Tapped` (Task 12) → `Open(FolderMode.Browse)`.
- **Строки** — из `ChatItem.prefab` через `ChatItemView.Bind`, серый счётчик. Нажатие в режиме `Browse` открывает чат прежним путём.
- **Обновление.** Пока папка открыта, она перестраивается на `OnChatAdded`, `OnChatRemoved` и `ChatAudienceStore.OnChanged`.
- **«Изменить».** Без отмеченных — тост `SelectAtLeastOne`. Иначе `MakeBusiness(отмеченные)` + тост `MovedToBusiness`.
- **Режим подключения.**
  - Пока строк нет — `LoadingLabel`, и раз в 10 с `ChatManager.RefreshActiveBotChats()`.
  - «Готово» → `MakeBusiness(отмеченные)` и `Clear`; «Отмечу позже» → `Clear`.

- [ ] **Step 1: Падающие тесты:**
  - `Rows_Browse_IncludesNonHumans_EditOmitsThem`, `Rows_Query_FiltersByTitleAndNumber`, `Subline_NonHumanAndOnboardingMarks`;
  - `PrimaryLabel_UsesTheCount` — «Сделать рабочими · 2», «Готово · клиентов: 0»;
  - `NoContactsNote_OnlyWhenLoadedAndNoContact`;
  - `Onboarding_ShowsOnce_ClearsOnDoneAndLater`;
  - `Back_ClosesFolder_EditIsCancel_OnboardingIsLater`;
  - builder:
    - панель стоит после `ChatsPanel` и перед `MessagesPanel`;
    - `OnCommitted` связан;
    - `SelectCheck` неактивен;
    - поле поиска настроено как остальные поля.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "PersonalChatsFolder|PersonalChatsOnboarding|BackNavigationTests|PersonalChatsBuilderPremiseTests"`.
- [ ] **Step 3: Реализовать, прогнать builder.**
- [ ] **Step 4: PASS** — тот же запуск и `BusinessContactFieldsTests|FontGlyphCoverageTests` → `failed="0"`.
- [ ] **Step 5: Commit сразу** — `feat(personal-chats): папка «Личные», «Изменить» и экран «Кто из них клиенты?»`.

### Task 15: Открытый чат — плашка «Личный», два окна, подсказки и клавиша ✦

**Files:**
- Create: `Assets/Scripts/UI/PersonalChatSheets.cs` — два ленивых окна по образцу `AutoLockSheet`: свой canvas `sortingOrder 3`, scaler 1080×1920, шрифты передаёт вызывающий, `ConfirmCardFitter` после `Show`
- Create: `Assets/Scripts/UI/MakeBusinessCopy.cs` — чистый выбор текста
- Modify: `Assets/Scripts/Chat/MessageHeaderView.cs`:
  - личный чат: показана `PersonalPill`, скрыт `SemiAutoToggle`; нажатие на плашку → окно «Сделать чат рабочим?»;
  - рабочий чат: нажатие на `NameTap` → окно «Сделать личным».
- Modify: `PersonalChatsBuilder.cs` — `EnsureHeaderParts`:
  - `MessagesPanel/TopBar/RightZone/PersonalPill` (неактивна; замок и «Личный»);
  - `MessagesPanel/TopBar/LeftZone/NameTap` — прозрачная кнопка над `Avatar` и `Text`, не заходит на `BackButton`.
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs`:
  - `ResolveSemiAutoForOpenChat` возвращает `false`, `ShowPanel` выходит сразу, обработчик нажатия капсулы ничего не делает, если `!ChatAudienceStore.AllowsAssistant(профиль, чат)`;
  - на `ChatAudienceStore.OnChanged` открытый чат пересчитывается;
  - `public bool WouldSuggestIgnoringAudience()` — то же решение без условия про вид чата: сохранённый режим, расписание, квота.
- Modify: `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` — `Request` без сети отдаёт пустой результат, если `!AllowsAssistant`; отказ сервера `personal_chat` → пусто, без текста ошибки.
- Modify: `AndroidBackRouter.cs` — два окна: Back = «Оставить личным» / «Отмена».
- Create тесты: `Assets/Tests/Editor/Chat/MakeBusinessCopyTests.cs`, `PersonalChatSheetsWiringTests.cs`, `SuggestionsAudienceGuardTests.cs`; дополнить `BackNavigationTests.cs` и `PersonalChatsBuilderPremiseTests.cs`.

**Interfaces:**

```csharp
public static class MakeBusinessCopy {
    public static (string Body, string Note) For(bool autoOn, bool lastIsClient, double lastAgeHours);
    // autoOn && lastIsClient && age ≤ FreshHours → (AutoFresh, StaleNote); autoOn && lastIsClient → (AutoNoFresh, StaleNote);
    // autoOn → (AutoNoFresh, null); иначе → (Manual, null)
}
public static class PersonalChatSheets {
    public static void AskMakeBusiness(string botName, string profileId, ChatViewModel chat, bool autoOn, TMP_FontAsset regular, TMP_FontAsset semibold);
    public static void AskMakePersonal(string botName, string profileId, ChatViewModel chat, string numberLine, TMP_FontAsset regular, TMP_FontAsset semibold);
    public static bool IsShowing { get; } public static bool TryCloseTop();
}
```

- **`autoOn`** = `!SuggestionsController.WouldSuggestIgnoringAudience()`; `lastIsClient` = `!chat.IsLastMessageMine`; возраст — от `chat.LastMessageTime`.
- **Строка номера в окне «Сделать личным»** — номер в той же форме, что сейчас в шапке, плюс « · в контактах» при `Facts.Contact == true` или « · новый» при причине `New`.
- **«Сделать личным»** → `MakePersonal` и тост `MovedToPersonal`. Сразу появляется плашка, панель уходит; список отфильтруется, пока чат открыт поверх него.

- [ ] **Step 1: Падающие тесты:**
  - `For_AutoFresh`, `For_AutoStaleHasTheNote`, `For_AutoOwnerLastNoNote`, `For_Manual`;
  - стражи исходников:
    - кнопки окон вызывают `ChatAudienceSyncService.MakeBusiness(` и `MakePersonal(`;
    - `AllowsAssistant(` есть в `ResolveSemiAutoForOpenChat`, в `ShowPanel` и в `N8nSuggestionsProvider.Request`;
  - `Back_ClosesBothSheets`;
  - builder: `NameTap` не пересекает `BackButton`, `PersonalPill` неактивна.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "MakeBusinessCopyTests|PersonalChatSheetsWiringTests|SuggestionsAudienceGuardTests|BackNavigationTests|PersonalChatsBuilderPremiseTests"`.
- [ ] **Step 3: Реализовать, прогнать builder.**
- [ ] **Step 4: PASS** — тот же запуск и `Suggestion|SemiAuto|ConfirmCard` → `failed="0"`.
- [ ] **Step 5: Commit сразу** — `feat(personal-chats): плашка «Личный», окна «Сделать рабочим / личным», подсказки только в рабочих`.

### Task 16: Настройки → «Основное» и «Сводка»

**Files:**
- Modify: `PersonalChatsBuilder.cs` — `EnsureSettingsSection` в `BotSettings.prefab`, по образцу `BotScheduleBuilder`. Якоря ищутся через сериализованные ссылки `BotSettings`. Состав:
  - `SectionHeader_ЛИЧНЫЕ ЧАТЫ`;
  - `PersonalChatsRow` (название, число, «›»);
  - подсказка — сразу перед `SectionHeader_РАСПИСАНИЕ`;
  - ссылки проставлены.
- Create: `Assets/Scripts/Main/BotSettings.PersonalChats.cs` — число и нажатие
- Create: `Assets/Scripts/UI/PersonalChatsCount.cs` — число по обоим каналам ИИ-сотрудника: активный список и кэши `BotCache`, тот же источник, что у `BotChatStats`, через `ChatAudienceStore.ViewOf`
- Modify: `Assets/Scripts/Dashboard/DashboardPage.cs` — строки, у которых `ChatAudienceStore.KindOf(профиль, чат) == Personal`, убираются до показа
- Create тесты: `Assets/Tests/Editor/Chat/PersonalChatsCountTests.cs`, `DashboardAudienceFilterTests.cs`, `PersonalChatsSettingsWiringTests.cs`; дополнить `PersonalChatsBuilderPremiseTests.cs`.

Нажатие на строку:
- если есть несохранённые правки — тост `SaveFirst`, больше ничего;
- иначе:
  1. тот же вызов, что у шеврона «назад» (`BotSettings.RequestBack()`);
  2. `ChatManager.Instance.SetActiveBot(bot)`;
  3. вкладка «Чаты» через `BottomTabManager.SwitchTab`;
  4. канал экрана чатов, если он у этого ИИ-сотрудника подключён, иначе другой;
  5. `PersonalChatsFolderView.Instance.Open(FolderMode.Browse)`.

- [ ] **Step 1: Падающие тесты:**
  - `Count_SumsBothChannels_SkipsWorking`;
  - `Dashboard_HidesPersonalRows_KeepsUnknown`;
  - страж исходника — порядок вызовов при нажатии и ветка `SaveFirst`;
  - builder — раздел стоит над расписанием, подсказка равна `SettingsHint`, ссылки проставлены.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "PersonalChatsCount|DashboardAudienceFilter|PersonalChatsSettings|PersonalChatsBuilderPremiseTests"`.
- [ ] **Step 3: Реализовать, прогнать builder** — он правит `BotSettings.prefab`.
- [ ] **Step 4: PASS** — тот же запуск и `BotSchedule|BotSettings|Dashboard` → `failed="0"`.
- [ ] **Step 5: Commit сразу** — `feat(personal-chats): строка «Личные чаты» в настройках и «Сводка» без личных`.

### Task 17: Документы и полные прогоны

**Files:**
- Modify: `CLAUDE.md`:
  - абзац «Личные и рабочие чаты» в External APIs: `audience.py`, таблицы, `Decide Audience`, `ChatAudience`, шаг Sweep, писатели, аренда, подсказки, «Сводка», правила этого плана;
  - абзац в Architecture → `Chat/`: зеркало, синхронизация, список, папка, окна, builder.
- Modify: `docs/legal/privacy.html` — одна строка о том, что личные чаты ИИ не обрабатывает. Черновик: «Личные чаты — контакты из телефона и переписки до подключения — ИИ-сотрудник не обрабатывает: их сообщения не передаются ИИ и не сохраняются на сервере.» Формулировку сверить с владельцем до выкладки сайта.
- Modify: `docs/audits/2026-10-19-device-pass-new-build.md` — пункты проверки на устройстве:
  - «Кто из них клиенты?» на настоящем списке чатов;
  - папка, «Изменить» и свайпы;
  - плашка и оба окна;
  - строка настроек;
  - Android Back;
  - обе темы, 1080×2400.

- [ ] **Step 1: Правки документов.**
- [ ] **Step 2: Полный EditMode** — Run: `Tools/run-tests-headless.sh` → `failed="0"` в `results.xml`; `git status` чист на `Assets/TextMesh Pro/Fonts/`.
- [ ] **Step 3: Все серверные тесты и проверки** — Run: `Tools/n8n/staging/pg.sh start; python3 Tools/n8n/tests/run_all.py`, затем каждый генератор с `--check` и все `verify-*.py`, которые трогал план → `OK`.
- [ ] **Step 4: Commit** — `docs(personal-chats): CLAUDE.md, политика конфиденциальности, проверка на устройстве`.

### Task 18: Пакет выкладки, репетиция с прод-состояния и прод (прод — только по «да» владельца)

**Files:**
- Create: `docs/audits/2026-10-25-personal-chats/README.md` — пакет выкладки:
  - одна миграция;
  - два новых workflow;
  - изменённые: оба шаблона, Create WA/TG, Delete Bot Files, Set Workflow State, Profile Lifecycle Sweep, Wappi Proxy, Suggest Replies, Dashboard Outcomes;
  - порядок, проверки, указатели отката.

Порядок в README:
1. **Снимок и проверка пакета.** Снимок `prod-before.json`; dry-run `deploy-canonical.py` называет ровно пакет.
2. **Миграция** через `.secrets/sql-oneoff.py` — precheck и postcheck.
3. **Set Workflow State** — новые защищённые id.
4. **Новые workflow.** Импорт `crAudienceDec001` и `crChatAudience01` из временной копии с прод-id `WappiAuthToken` (`ZowntFGvApDJ7UzQ`) и Postgres. Проверка GET; затем публикация подпроцесса и активация вебхука (a1-deploy-runbook §2.4).
5. **Изменённые workflow.** Create WA/TG, Delete Bot Files, Sweep, Wappi Proxy, Suggest Replies, Dashboard Outcomes, затем оба шаблона — через `deploy-canonical.py --apply --yes`, каждый с чтением назад.
6. **Клоны и сверка.** `update-clones.py` (на проде 0 живых клонов), затем `verify-deployed.py` → diff 0.
7. **Смоук.** `ChatAudience list` с токеном сессии отвечает 200; подпроцесс опубликован.
8. **Наблюдение** за выполнениями Sweep.

- [ ] **Step 1: Репетиция на стенде с прод-состояния.**
  1. `python3 Tools/n8n/staging/bootstrap.py --canon-ref <sha, которым обслуживается прод> --bot wa:stgwa001 --bot tg:stgtg001`
  2. dry-run `deploy-canonical.py --base http://localhost:5678` = ровно пакет; затем шаги README с `--apply --yes`
  3. `update-clones.py --base http://localhost:5678`
  4. `verify-deployed.py --base http://localhost:5678` → 0
  5. `derive-staging.py` (без импорта)
  6. `e2e-audience.py` → PASS

  Записать `rehearsal.txt` рядом с README.
- [ ] **Step 2: Commit** — `docs(personal-chats): пакет выкладки и репетиция`.
- [ ] **Step 3: Прод — СТОП, нужен явный «да» владельца.** После «да» владелец запускает команды README кнопкой Run. Записать в `docs/audits/implementation-progress.md`: снимки `prod-before.json` / `prod-after.json` и указатели отката.
