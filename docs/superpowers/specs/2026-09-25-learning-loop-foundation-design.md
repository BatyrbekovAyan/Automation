# Обучение «Вместе → Авто», подпроект A «Фундамент» — дизайн

**Дата:** 2026-09-25 · **Статус:** дизайн утверждён владельцем в диалоге 2026-09-25 (разбор видения: `2026-09-25-learning-loop-vision-analysis.md`). Реализация — по отдельному плану (writing-plans).
**Программа:** A «Фундамент» (этот документ) → B «Мягкий замок и прогресс» → C «Стиль и память ответов» → D «Опыт ниши, единый ранжировщик». Каждый подпроект получает свою спецификацию.

## 0. Решения владельца (зафиксированы 2026-09-25)

| № | Решение | Выбор |
|---|---|---|
| 1 | Критерий «бот обучен» | доля совпадений карточки №1 панели с реально отправленным ответом владельца по последним 20 отправкам из приложения |
| 2 | Порог | **10 из 20** (стартовая калибровка; пересматривается по данным подпроекта A) |
| 3 | Форма замка «Авто» | **мягкий**: прогресс на капсуле + лист с объяснением + «Включить всё равно»; на триале жёсткого запрета нет никогда |
| 4 | Хранение | тексты показанных карточек и метка «карточка / правка / своё» хранятся по `profile_id` рядом с историей чата, с правкой privacy §3 |
| 5 | «Не по теме» | три класса: приветствие/спасибо/эмоция → коротко по-человечески; посторонний вопрос → одна фраза с названием бизнеса (в панели — одна карточка «Отказ»); спам/личное → тишина (панель: abstain) |
| 6 | Общая база по вертикали | сырые переписки между бизнесами — никогда; только обезличенные агрегаты (подпроект D) после новой политики, opt-in и юриста |
| 7 | Профиль стиля | видимый, с кнопкой «Не так» (подпроект C) |
| 8 | Честность | на прямой вопрос «вы человек или бот?» — «ассистент {название бизнеса}» |

Допущения, принятые без отдельного вопроса (владелец может отменить): ответы с телефона (`owner_phone`) идут в стиль (C), но не в счёт готовности (нет показанного набора); групповые чаты исключаются из любого обучения; существующие профили считаются разблокированными (сейчас живых ботов на проде нет); потолка по дням нет — его роль выполняет «Включить всё равно»; drill по тапу в A не трогаем (кандидат на B); срок хранения наборов и меток — 180 дней.

## 1. Цель и границы A

**Цель.** После A на каждой отправке из приложения есть измеримая пара «что показали → что ушло», в обоих режимах действует правило трёх классов «не по теме», а панель в первые дни держит широту набора. Экраны приложения не меняются, замка нет, ничего не блокируется.

**В A входит:** промпты + eval (фаза 0); метка происхождения отправки; журнал показанных наборов; счётчик предпочтений по отправке; удаление вместе с ботом; read-only отчёт; правка privacy §3.

**В A не входит:** замок и прогресс (B); экран стиля, «Как раньше», один блок стиля в оба промпта, `now` в зоне бота для Авто, «Поправить» под автоответом (C); приоры ниши, единый ранжировщик (D); отключение drill по тапу; редактируемое поле «Что отвечать не по теме» (в A фраза — из шаблона с подстановкой названия бизнеса; поле — в C).

## 2. Архитектура (обзор)

```
клиент                                   сервер (n8n + Postgres)
─────────────────────────────            ──────────────────────────────────────────────
SuggestionsController                    Suggest Replies (9PTyYcelRQI7bGDb)
  тап → PickSnapshot{key,index,text}       Prep: stage=learning|trained (по pickStats)
                                           Assemble: правило «не по теме» (панель),
MessagesBottomPanel → ChatManager                    блок ХОЛОДНЫЙ СТАРТ
  SendTextMessage(text)                    … → Build Response → Respond → **Record Set**
    SendOriginResolver.Resolve(...)                                         (после ответа, fail-open)
    → origin_* в args WappiProxy.Send                                       → suggestion_sets
                                          Wappi Proxy (crWappiProxy0001)
                                            Build Request: spec.meta (валидация, не в Wappi)
                                            … → Respond → Record Owner Send
                                                 → chat_history_events(+origin_*)
                                          Бот-шаблоны WA/TG: _core.md с правилом трёх классов
                                          Delete Bot Files: Delete Chat Memory + suggestion_sets
```

## 3. Компоненты

### 3.1 Промпты (сервер, фаза 0)

**`Tools/n8n/prompts/_core.md`** — один новый абзац после «Данные бизнеса» (единый источник для 6 вертикалей, инжектируется `inject-prompts.py`):

> Границы темы. Ты отвечаешь только по вопросам {businessName}. Приветствие, «спасибо», короткая эмоция без вопроса — не постороннее: ответь коротко и по-человечески, без отказа и без допродажи. Вопрос или просьба не про этот бизнес — одной фразой скажи, что здесь отвечаешь только по вопросам {businessName}, и предложи вернуться к делу; не поддерживай постороннюю тему и не читай мораль. Спам, рассылка, сообщение явно не тебе — не отвечай. На прямой вопрос «вы человек или бот?» отвечай честно: ты ассистент {businessName}, и предложи позвать владельца, если нужно.

Подстановка `{businessName}` — в Code-ноде `Vertical Prompt` Create/Edit (она уже читает `$('Unity Webhook').first().json.body`; поле формы `Name` — имя бота, которым владелец называет бизнес): `.replace(/\{businessName\}/g, name || 'этого бизнеса')` перед тем, как `Set Fields` склеит systemMessage (`XuvOp7TxOImOAmlj`, поля `nodes[5]…systemMessage`); в панельных промптах ту же замену делает Assemble по `p.businessName`. Ответ на казахском — по общему правилу языка (оно выше стиля и остаётся как есть).

**`Tools/n8n/prompts/panel/<id>.md` × 6** — одна строка в каждом (инжектируется `inject-panel-prompts.py`, блок marker-wrapped): «Посторонний ВОПРОС, адресованный бизнесу, — ровно одна карточка с ходом «Отказ», label «Не по теме»: одной фразой, что здесь отвечают только по вопросам {businessName}. Личное, эмоции без запроса, спам — по правилу ВОЗДЕРЖАНИЕ.»

**Assemble (Suggest Replies), три правки через новый `apply-learning-a.py` (паттерн `apply-suggest-v2.py`, marker-wrapped, `--check`):**
1. ВОЗДЕРЖАНИЕ (строка 50): добавить 3–4 контрастных примера в каждую сторону (личное/эмоция → пусто; «сколько стоит доставка?» после шутки → карточки), потому что сейчас правило срабатывает 3/8 в eval. Формулировка остаётся LOCKED по смыслу (remediation S2), меняются только примеры.
2. ХОЛОДНЫЙ СТАРТ: если `stage === 'learning'` — «набор обязан содержать не меньше трёх карточек с РАЗНЫМИ ходами (при двух и более направлениях); ранжируй только по уместности». Блок ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА (строка 68) в этой стадии не эмитится.
3. Prep: `stage = (сумма счётчиков из pickStats) < 10 ? 'learning' : 'trained'` (pickStats уже приходит и санитизируется на строке 19). Отдаётся в Record Set.

**Eval (`Tools/n8n/eval`)** — scenarios.v1.json → v1.2: по 3 вертикали (auto_parts, flowers, education) × 3 класса × 2 режима = 18 сценариев (`ot_q_*`, `ot_greet_*`, `ot_spam_*`, панельные `pn_ot_*`) + 2 сценария «вы бот?». Новые проверки в `run-eval.py`: auto — `names_business_only` (ответ содержит название бизнеса и слово «только», не содержит цен/₸/обещаний), `no_refusal_on_greeting` (нет «только по вопросам» на приветствии), `honest_assistant` (на «вы бот?» есть «ассистент»); panel — `cards_eq_1_refusal` (ровно одна карточка с move «Отказ»), `abstain` (уже есть). Обязательно: before/after на одном наборе, `--repeat 3` для key; критерий — общий счёт не хуже 54/6/3, `ot_*` ≥ 2/3, abstain на личное ≥ 6/8.

### 3.2 Метка происхождения отправки (клиент + прокси)

**Клиент.**
- `SuggestionResult` получает поле `requestKey` (провайдер стамплит ключ, который уже считает `N8nSuggestionsProvider.RequestKey`, на результат при `MapResponse`).
- `SuggestionsController.HandleCardTapped` вместо `RecordPick` сохраняет `PickSnapshot { requestKey, index, text, move, setSize, top1Text }` (индекс — позиция в `_currentRendered.items`; `top1Text` — текст карточки №1 набора, из которого тапнули) и сбрасывает его в `StartFreshRound` (новое входящее, смена чата/бота, answered run). Если тапа не было, но набор на экране есть — контроллер отдаёт «набор показан» с `requestKey/setSize/top1Text` и без индекса.
- Новый чистый seam `Assets/Scripts/Chat/SendOriginResolver.cs`: `Resolve(sentText, snapshot) → SendOrigin { kind: card|edited|typed|none, index, requestKey, editPercent, top1Match, setSize }`. Правила: `card` — нормализованный текст равен тексту тапнутой карточки; `edited` — тап был, текст отличается (`editPercent` = нормализованный Левенштейн ×100, 0–100); `typed` — набор показан, тапа не было; `none` — набора не было. `top1Match` = сходство с `top1Text` ≥ 0.8, где сходство = 1 − Левенштейн(a, b) / max(|a|, |b|) на нормализованных строках (нормализация: trim, lower, схлопывание пробелов, снятие пунктуации в концах); `editPercent` = round((1 − сходство с тапнутой карточкой) × 100). Чистый C#, без Unity.
- `ChatManager.SendTextMessage` спрашивает контроллер (через `ChatManager.Suggestions.cs`, где уже живёт связь с панелью) и прокидывает `SendOrigin` в `PostTextMessageRoutine`, который добавляет в `args` поля `origin_kind, origin_index, origin_key, origin_edit, origin_top1, origin_set` для op `message.send` и `message.reply`. Медиа-отправки метку не несут (A).
- `RecordPick` переезжает в момент успешной отправки (200 + message_id) и считает ТОЛЬКО `card`/`edited`. Клиентский `pickStats` остаётся как есть (legacy-fallback до B, когда счёт переедет на сервер).

**Прокси (`Tools/n8n/build-auth-workflows.py`, `BUILD_REQUEST_JS`).** В OPS добавляется секция `meta` (та же функция `take`, тот же allow-list-подход, что `q`/`body`), которая эмитится в выход `Build Request` как `meta` и **никогда не попадает в `jsonBody`** (Wappi её не видит):

```
'message.send':  { …, meta: META }, 'message.reply': { …, meta: META }
META = { origin_kind: ['origin_kind', /^(card|edited|typed|none)$/, 'opt'],
         origin_index: ['origin_index', 'int', 3, 'opt'], origin_key: ['origin_key', /^[0-9a-f]{1,32}$/, 'opt'],
         origin_edit: ['origin_edit', 'int', 100, 'opt'], origin_top1: ['origin_top1', 'int', 1, 'opt'],   // 0/1, не bool: bool-правило take опускает false
         origin_set: ['origin_set', 'int', 4, 'opt'] }
```
`int`-правило `take` подставляет default только если он задан — здесь default не задан, поэтому отсутствующее поле остаётся отсутствующим (легаси-клиент без метки проходит как сегодня). `origin_top1` — int 0/1, потому что bool-правило `take` эмитит поле только при true и «false» было бы неотличимо от «нет метки».

**Record Owner Send** (`RECORD_OWNER_SEND_SQL` + `OWNER_SEND_PARAMS`, тот же узел, тот же запрос — новых Postgres-узлов нет): `insert … (…, origin_kind, origin_index, origin_key, origin_edit, origin_top1, origin_set)` со значениями из `$('Build Request').first().json.meta` (null, если поля нет). `on conflict … do update` дописывает те же колонки.

**SQL `Tools/n8n/sql/2026-09-25-learning-foundation.sql`** (additive, idempotent, RLS-хвост как в `2026-09-17-suggestion-request-keys.sql`):
```
alter table chat_history_events
  add column if not exists origin_kind  text check (origin_kind in ('card','edited','typed','none')),
  add column if not exists origin_index smallint, add column if not exists origin_key text,
  add column if not exists origin_edit  smallint, add column if not exists origin_top1 smallint check (origin_top1 in (0,1)),
  add column if not exists origin_set   smallint;
create index if not exists chat_history_events_origin_idx on chat_history_events (profile_id, origin_key) where origin_key is not null;
```

### 3.3 Журнал показанных наборов (сервер)

```
create table if not exists suggestion_sets (
  request_key      text not null,            -- = requestKey клиента (FNV-1a64 hex)
  profile_id       text not null,
  chat_id          text not null,
  tail_message_id  text,
  business_type_id text,
  stage            text,                     -- learning | trained
  items            jsonb not null,           -- [{label, move, text}] ≤ 4
  prompt_version   text,                     -- стамп версии Assemble (константа в apply-скрипте)
  usage            jsonb,                    -- {prompt_tokens, completion_tokens, cached_tokens} из ответа OpenAI
  created_at       timestamptz not null default now(),
  primary key (profile_id, request_key)
);
create index if not exists suggestion_sets_age_idx on suggestion_sets (created_at);
alter table public.suggestion_sets enable row level security;
revoke all on table public.suggestion_sets from anon, authenticated;
```
Узел **`Record Set`** (Postgres, `continueOnFail: true`, `alwaysOutputData`) подключается `Respond → Record Set`; узел `Respond` (respondToWebhook) пропускает item дальше, поэтому запись идёт ПОСЛЕ отдачи ответа и вне `executionTimeout 20`. Пишет только при `j.ok && !j.abstain && items.length > 0`; `usage` читается выражением прямо из ответа HTTP-ноды (`$('LLM Retry').first().json.usage` если retry был, иначе `$('LLM').first().json.usage`) — `Build Response` не меняется. Дедуп: `on conflict (profile_id, request_key) do nothing` (retry после таймаута — тот же ключ). Retention: `delete from suggestion_sets where created_at < now() - interval '180 days'` — добавляется в существующий `Profile Lifecycle Sweep` (раз в 6 ч) вместе с зачисткой `chat_history_events.origin_*` старше 180 дней (только колонки, не строки истории).

### 3.4 Удаление вместе с ботом

`Delete Bot Files` → узел `Delete Chat Memory`: новый CTE `sets as (delete from public.suggestion_sets s where s.profile_id in (select profile_id from valid) returning 1)` + столбец `deletedSuggestionSets` в SELECT. Колонки `origin_*` удаляются вместе со строками `chat_history_events` (уже в CTE `hist_events`).

### 3.5 Отчёт (только чтение)

`Tools/n8n/report-learning.py` (через `.secrets/sql-oneoff.py`-харнесс, read-only SELECT): по каждому `profile_id` за окно N дней — число отправок с меткой, доли `card/edited/typed/none`, доля `origin_top1` среди отправок с показанным набором, средний `origin_edit`, доля наборов `stage=learning` с ≥3 разными `move`, `usage` в токенах. Это калибровочная база для порога B. Прод — только по явному go владельца.

### 3.6 Политика

`docs/legal/privacy.html` §3, пункт «Статистика использования подсказок»: «…(показанные подсказки и выбранный/отправленный ответ хранятся вместе с историей чата бота, удаляются вместе с ботом; срок хранения — до 180 дней)». §4 не меняется (цель та же — «формирование ответов и подсказок ИИ в чатах пользователя»). Data safety / App Privacy: категории не меняются (Messages уже Collected, Shared = No). Публикация страницы — до выпуска клиента с меткой (владелец, `Tools/landing/tools/deploy.sh`).

## 4. Поток данных

1. Входящее → панель запрашивает набор → Prep считает `stage` → Assemble (правило трёх классов, холодный старт) → LLM → Validate → Build Response → **Respond** → **Record Set** (`suggestion_sets`, ключ = `requestKey`).
2. Владелец тапает карточку → `PickSnapshot`; правит или нет; нажимает Send.
3. `SendTextMessage` → `SendOriginResolver.Resolve` → `PostTextMessageRoutine` добавляет `origin_*` в args → `WappiProxy.Send('message.send')`.
4. Прокси: Auth Gate → Build Request (валидирует `meta`, в Wappi уходит только `body`) → Wappi Call → Respond → Record Owner Send пишет `owner_app` + `origin_*`.
5. Успех отправки на клиенте → `RecordPick` (только card/edited) → PlayerPrefs (legacy pickStats).
6. Отчёт джойнит `chat_history_events(origin_key)` × `suggestion_sets(request_key)` по `profile_id`.

## 5. Обработка ошибок и инварианты

- Запись набора и метки — best-effort: ошибка БД не влияет ни на ответ панели, ни на отправку (узлы после Respond, `continueOnFail`).
- Легаси-клиент (compat-окно до 2026-12-31) не шлёт `origin_*` → прокси принимает, колонки null. Никаких 4xx на отсутствие метки.
- `origin_*` — самоотчёт клиента; при мягком замке это допустимо; сервер хранит оба текста (карточки в `suggestion_sets`, ответ в `chat_history_events`) и может пересчитать совпадение в B.
- Группы: `Record Owner Send` пишет строку как сегодня, но отчёт и все будущие выборки обучения фильтруют `chat_id not like '%@g.us'` (панель в группах гейта не имеет — это фиксируется в отчёте как известное ограничение).
- Горячий путь: ноль новых Postgres-узлов до Respond в обоих workflow (≈1.9 с каждый на проде). `Record Set` и `Record Owner Send` — после ответа.
- Стадия `learning` считается по клиентскому pickStats (после переезда RecordPick на Send счётчик растёт медленнее — это ожидаемо и делает стадию честнее). Сервер не читает БД для стадии в A.
- Правила `_core.md` меняются только с before/after eval; правило воздержания — только примерами; `Validate`/`Validate 2` не трогаются (асимметрия 94e772d).

## 6. Тестирование

**Unity EditMode (`Assets/Tests/Editor/Chat/`):**
- `SendOriginResolverTests` — чистые: card / edited (edit 0–100) / typed / none; нормализация; top1Match порог 0.8; сброс снимка на новом входящем; индекс и setSize.
- `SendOriginWiringTests` — SOURCE-guard по образцу `SendPathWiringTests`: `PostTextMessageRoutine` передаёт `origin_*` для `message.send`/`message.reply`; `HandleCardTapped` НЕ вызывает `RecordPick`; `RecordPick` вызывается только после успешной отправки.
- `SuggestRepliesPayloadTests` — payload не изменился (frozen-object тест остаётся зелёным); `SuggestionResult.requestKey` стамплится в `MapResponse`-обёртке.
- `AuthWiringGuardTests` — без изменений (новых `SendWebRequest` нет).
- `FontGlyphCoverageTests` — новых UI-строк в A нет.

**Сервер:**
- `verify-learning-a.py` (паттерн `verify-suggest-*`/`verify-auth-gate.py`): в канонических JSON — `Record Set` подключён после `Respond`, `continueOnFail`; в `BUILD_REQUEST_JS` есть `meta` для двух ops и `jsonBody` не содержит `origin_*`; в `RECORD_OWNER_SEND_SQL` шесть новых колонок; в `Delete Chat Memory` есть CTE `sets`.
- `node verify-panel-prompts.js` + собранный `_core` через `inject-prompts.py --check`.
- Staging e2e (`Tools/n8n/staging/e2e-learning.py`, run-scoped fixtures): (1) панель → набор записан по `request_key`; (2) отправка с `origin_kind=card` → строка `owner_app` с шестью колонками; (3) отправка без метки (легаси) → колонки null, 200; (4) `origin_kind=bogus` → 400 `bad_param`; (5) Delete Bot Files → `suggestion_sets` пуст; (6) abstain → набор не пишется; (7) `stage=learning` при pickStats пустом; e2e-suggest / e2e-auth / e2e-reply остаются зелёными.
- Eval: before/after v1.2 (см. 3.1).

## 7. Порядок и оценка

| Шаг | Содержание | Оценка |
|---|---|---|
| A0 | `_core.md` + 6 panel-промптов + Assemble (примеры воздержания, холодный старт) + Prep stage + eval v1.2 + before/after | 1–2 дня |
| A1 | SQL-миграция; `meta` в Build Request; Record Owner Send; Record Set; Delete Chat Memory; verify + staging e2e | 3–4 дня |
| A2 | `SuggestionResult.requestKey`, `PickSnapshot`, `SendOriginResolver`, проводка в ChatManager, RecordPick на Send, тесты | 2–3 дня |
| A3 | `report-learning.py`; privacy §3; журнал деплоя | 1 день |

Деплой на прод — только по явному go владельца: SQL через `.secrets/sql-oneoff.py`, Wappi Proxy и Suggest Replies через `deploy-canonical.py`, промпты через `inject-*.py` + `update-clones.py` для бот-шаблонов (клонов на проде нет), сайт — `Tools/landing/tools/deploy.sh`.

## 8. Что перейдёт в B/C/D (не терять)

B: `AutoReadinessPolicy` (10 из 20, ≥10 отправок в ≥3 личных чатах за ≥2 дня), Locked-состояние капсулы в 4 точках, лист «Бот учится» с «Включить всё равно», readiness в ответе `Handoffs list` или `Get Usage` (не `status`: периодический опрос — `list`), серверный пересчёт `top1` по хранимым текстам, копирайт стора/онбординга/лендинга, drill по тапу в стадии learning. C: экран «Что бот понял» (признаки только из `owner_app/owner_phone`, без групп, без коротких «ок/👍», маскирование ПДн других клиентов), `reply_exemplars` + «Как раньше» с проверкой `knowledge_version`, один блок стиля в оба промпта, `now` в зоне бота, «Поправить», редактируемая фраза «не по теме» на KZ/RU. D: `vertical_priors` (k≥5–10), opt-in, политика, юрист, ReplyRank, replay в eval.
