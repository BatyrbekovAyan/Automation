# Обучение «Вместе → Авто», подпроект A «Фундамент» — план реализации

> **Изменения в репо после написания плана (2026-10-13, отдельная сессия «Авто = продолжение подсказок») — учесть до старта.**
> Отчёт: `Tools/n8n/eval/results/agreement/cmp-1013-conditions-status.md`; кратко — CLAUDE.md, абзац «Авто = продолжение подсказок».
>
> - **Suggest Replies.**
>   - LLM и LLM Retry работают на gpt-5.4-mini (не 4o-mini).
>   - В Assemble добавлено правило «УСЛОВИЯ» — маркеры `PANEL-CONDITIONS`, сразу после «ФАКТЫ». Его пишет `apply-suggest-v2.py`.
>   - В «ВОЗДЕРЖАНИЕ» добавлен пример рекламного спама: изменены только примеры, смысл тот же.
>   - Ниша Kaspi в панели: «отложите» → «Отложить».
>   - Маркер `LEARNING-A-OFFTOPIC-RULE` этого плана с ними не пересекается.
> - **`_core.md`.** Новый абзац «Правило владельца об условии» после «Данные бизнеса». Правило «Границы темы» из этого плана — отдельный абзац.
>   - Урок, оплаченный при измерении: фраза о том, КОГДА вызывать `create_handoff`, порождает лишние заявки в соседних случаях — проверять по `no_tool:create_handoff`.
> - **`wholesale.md`.** Строка «СКИДКИ И ОТСРОЧКА» учитывает правило владельца.
> - **Eval.**
>   - Набор v1.6 (+8 сценариев: `pn_*_unknown`, `*_policy_known`), харнесс h9.
>   - Критерий плана «общий счёт не хуже 54/6/3» устарел. Брать «до» на текущем HEAD в том же чередующемся запуске (`--side before=HEAD --side after=worktree`).
>   - На 2026-10-13: авто 119/124 чистых, лишних заявок 0/53, панель 18/18 × 5.
> - **Готовый замер «Авто против карточки №1»** — `Tools/n8n/eval/results/agreement/cmp-1013-auto-vs-panel.py`. Судья gpt-5.4; на 2026-10-13 same/close/different = 38/25/3 из 66. Это офлайн-вариант «теневого счёта» спеки — можно опереться при калибровке замка.
> - **Staging.** Перед eval ждать регистрации и `EvalRun`, и `SuggestReplies`: GET отвечает «Did you mean to make a POST request?».

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** После A на каждой отправке из приложения записана пара «показано → отправлено», в обоих режимах действует правило трёх классов «не по теме», панель в стадии learning держит ≥3 разных хода; замка нет, экраны не меняются.

**Architecture:** Клиент помечает отправку происхождением (`SendOrigin`: карточка / правка / своё / без набора) через чистый seam и передаёт метку в существующий вызов прокси; прокси валидирует её отдельной секцией `meta` allow-list'а и пишет в `chat_history_events` тем же узлом `Record Owner Send`; Suggest Replies пишет показанный набор узлом ПОСЛЕ `Respond`; правило «не по теме» живёт в `_core.md` (единый источник 6 вертикалей) и в 6 панельных промптах; стадия `learning|trained` считается в `Gate Decision` по `pickStats`.

**Tech Stack:** Unity 6 C# (EditMode NUnit, без asmdef — `Assembly-CSharp-Editor`), Newtonsoft JSON, n8n workflow JSON + Python-патчеры (`apply-*.py`/`verify-*.py`), Postgres (Supabase), staging-стенд `Tools/n8n/staging/` (private n8n + pgserver + Wappi-mock), eval-харнесс `Tools/n8n/eval/run-eval.py`.

**Спека:** `docs/superpowers/specs/2026-09-25-learning-loop-foundation-design.md` (решения владельца §0).

## Global Constraints

- **Только чтение репо + staging.** Прод не трогать: ни SQL, ни deploy, ни webhooks — деплой на прод только по явному «go» владельца, отдельным шагом (см. Task 11).
- **Горячий путь:** ни одного нового Postgres-узла до `Respond` ни в Suggest Replies (executionTimeout 20 с), ни в Wappi Proxy, ни в бот-шаблонах (≈1.9 с на узел на проде).
- **Метка происхождения** — ТОЛЬКО через новую секцию `meta` в OPS прокси (валидируется как `body`, никогда не копируется в `jsonBody`). Все поля опциональны: клиент без метки (compat-окно до 2026-12-31) проходит как сегодня, 4xx на отсутствие метки запрещён.
- **Не трогать:** `Validate`/`Validate 2` (асимметрия 94e772d), json_schema LLM (без enum label / maxLength), смысл правила ВОЗДЕРЖАНИЕ (только примеры), цепочку A3/A4/A7 в бот-шаблонах, `PREP_RETURN_NEW`/marker-блоки `apply-suggest-v2.py` (стадия считается в `Gate Decision`, не в `Prep`).
- **Каждое изменение промпта** — before/after на одном наборе `scenarios.v1.json` с `--repeat 3` для `key`; критерий: общий счёт не хуже 54/6/3, `ot_*` ≥ 2/3, abstain на личное ≥ 6/8.
- **Новые таблицы/колонки:** RLS + `revoke all … from anon, authenticated`; ключ `profile_id`; удаление вместе с ботом; retention 180 дней.
- **Unity:** Editor ЗАКРЫТ → `Tools/run-tests-headless.sh "<regex>"` (скрипт отказывает при открытом Editor; headless-импорт сам подхватывает новые `.cs` — после прогона проверить, что рядом появились `.meta`); Editor ОТКРЫТ → сначала `Assets → Refresh` (иначе новый файл молча не компилируется), затем пустой файл `Temp/claude/run-tests.trigger` и читать `Temp/claude/test-summary.json`. Всегда смотреть на `total` (фильтр, давший 0/0, — ложная зелень). Стейджить `.cs` вместе с `.meta`. Команды grep с `--include` писать в кавычках (`--include='*.cs'`), иначе zsh падает на глоббинге.
- **Другие сессии:** перед КАЖДОЙ задачей `git status --short` по её файлам; чужие незакоммиченные правки в тех же файлах — стоп и вопрос владельцу (правило project_multi_session_unity_hazard). На 2026-10-13 файлы плана чисты; правило остаётся на каждую задачу.
- **Коммиты:** по-русски, префикс `feat/fix/docs/test(scope):`, последняя строка `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Коммит и push — только после явного согласия владельца на задачу (правило проекта); в плане «commit» = подготовить коммит в этой сессии по согласию.
- **Строки UI:** в A новых строк UI нет; любая строка-константа для сервера/логов — по-русски, глифы из SF Pro не нужны.

## Отклонения от спеки (зафиксированы при чтении кода)

1. `suggestion_sets.tail_message_id` → `tail_ts bigint` (в payload панели нет id сообщений, есть `ts`).
2. Стадия `learning|trained` считается в Code-узле `Gate Decision` (не в `Prep`): `Prep` возвращает объект, который `apply-suggest-v2.py --check` пинит дословно. `Assemble` и `Record Set` читают `$('Gate Decision').first().json.stage`.
3. Без плейсхолдеров ни в панели, ни в `_core.md`: имя бизнеса уже есть в данных (панель — блок ДАННЫЕ; бот — строка «Название:» под «About Business:», которую пишет `Manager.ComposeBusinessKnowledge`), правило просит назвать его оттуда. Подстановка в голову промпта запрещена V10 (см. отклонение 13).
4. Int-правило `take` в прокси: слот 4 — DEFAULT, поэтому «опциональный int без дефолта» записывается как `['origin_index', 'int', 3, undefined, 'opt']`.
5. Перед `Record Set` стоит If-узел `Has Set?` (набор пишется только при непустом успешном ответе).
6. Удаление `suggestion_sets` при удалении бота — CTE `sets` ВНУТРИ `Delete Chat Memory` (генератор `apply-history-handoff.py`, `DELETE_NEW_TAIL`) и счётчик `deletedSuggestionSets` в `Deletion Settled?`/`Respond` (`apply-auth-gate.py`): V12 пинит «после `Respond` ничего не выполняется», так что отдельного узла быть не может (перепроверка 2026-10-13, отклонение 15).
7. Тишина «Авто» на спам в A НЕ допускается промптом: пустой `output` агента уходит в `Generated?`=false → `Cancel Label` → `generation_failed` в `reply_events` (`apply-reply-pipeline-v2.py:317/:319/:169`), т.е. молчание превратилось бы в ложные сбои генерации. В A спам получает одно нейтральное слово на языке клиента; настоящий пропуск отправки (свой outcome `skipped_spam`, verify-reply-pipeline, e2e-reply) — подпроект C.
8. Связь панель → отправка сделана статическим `SendOriginTracker` (не через `ChatManager.Suggestions.cs`, как в спеке §3.2): `SuggestionsController` и `ChatManager` — разные MonoBehaviour без ссылок друг на друга, открытый чат ровно один.
9. Правило «посторонний вопрос → ровно одна карточка «Отказ»» живёт в ОБЩИХ правилах Assemble (маркер `LEARNING-A-OFFTOPIC-RULE`), а не в шести панельных нишах: заголовок ниши явно отдаёт «формат, длину и число карточек» общим правилам, и РЕЖИМ НАБОРА при одном направлении требовал бы 4 варианта. `prompts/panel/*.md` в A не трогаются.
10. Метка происхождения резолвится ОДИН раз при первой попытке и хранится в `OutboxStore.OutboxEntry.originJson`; повтор из аутбокса несёт ту же метку (спека §5 говорила «повтор не переклассифицируется» — без хранения метка терялась бы: `NULL` в `origin_*` должен означать только легаси-клиент или медиа).
11. Telegram `message.reply`: клиент не шлёт `recipient`, и `Record Owner Send` писал `chat_id = ''` (дефект A6 с 2026-09-24). Секция `meta` этого op получает `chat_id` (в Wappi не уходит), `OWNER_SEND_PARAMS` берёт чат из `recipient || meta.chat_id`; клиент кладёт `chat_id` в аргументы TG-ответа. Строки истории TG-ответов, записанные до правки, остаются с пустым `chat_id` — решение о зачистке за владельцем.
12. `Record Set` стоит после `Respond`, но внутри `executionTimeout 20` всего execution: ответ клиенту уходит всегда, а набор у самой границы таймаута может не записаться (best-effort). Долю запросов без набора смотреть в `report-learning.py`; таймаут ради журнала не поднимать.

**Перепроверка 2026-10-13 (после блока T1–T12, V9/V10, Q1):**
13. **Имя бизнеса НЕ подставляется в «голову» промпта.** V10 (`update-clones.py`/`verify-deployed.py`, `inject-prompts.parse_system_message`) классифицирует голову клона ПО СОДЕРЖИМОМУ против коммитнутых карт (`prompt_book`): голова с подставленным именем = `custom` у каждого клона — её никогда не обновят и каждый бот будет отчитываться «custom prompt». Поэтому имя едет в ХВОСТЕ (владельческая часть, которую Edit переписывает): `Manager.ComposeBusinessKnowledge` получает имя бота и пишет первой строкой под «About Business:» строку «Название: …»; правило в `_core.md` ссылается на неё («его название — строка «Название» в About Business ниже»). `inject-prompts.py` не меняется (только ЗАПУСКАЕТСЯ — карты пересобираются из-за нового абзаца). Бонус: общий префикс для prompt caching остаётся общим. Старые боты получают строку при следующем сохранении настроек; до того бот говорит «этого бизнеса».
14. **Примеры воздержания из Task 2 УБРАНЫ.** V9 (2026-09-28) уже стабилизировал ВОЗДЕРЖАНИЕ (pn_emotional_reaction 0/8 → 8/8; правило теперь прямо называет рекламный спам), а правило охраняется владельческим гейтом `N2_banter_abstain`. Трогать его без измеренной нужды нельзя; страж «примеры не в eval» и теги `holdout`/`in_prompt` исчезают вместе с ними. В Assemble добавляется только общее правило ПОСТОРОННИЙ ВОПРОС + оговорка в ХОЛОДНОМ СТАРТЕ.
15. **Наборы панели удаляются ВНУТРИ `Delete Chat Memory`, не узлом после `Respond`.** `verify-auth-gate.py` (V12) пинит `edges(Respond) == []` и `Delete Chat Memory → Deletion Settled? → Respond`: после ответа ничего не выполняется, а `success:true` даётся только когда каждый счётчик — число (проверено dry-run'ом: узел после Respond валит верификатор). Поэтому CTE `sets` добавляется в `DELETE_NEW_TAIL` (`apply-history-handoff.py`, владелец statement'а; линия бота — та же `lineage_profiles`, T5), счётчик `deletedSuggestionSets` — в `DELETION_SETTLED` (`apply-auth-gate.py`) и в тело `Respond`. Ноль новых узлов и латентности; наборы вытесненных профилей (F1) уходят с ботом; деплой — миграция ПЕРЕД Delete Bot Files (иначе 503 → клиентский Retry, бот остаётся).
16. **Eval по правилам стадии 6 / h8–h9:** сравнение «до/после» идёт ОДНИМ интерлив-прогоном (`--side before=HEAD --side after=worktree`, никогда двумя последовательными), каждый новый Авто-сценарий несёт `no_marker_leak` и `no_tool:create_handoff` и регистрируется в `eval/test_checks.py` (`NO_HANDOFF`, с причиной), новые проверки получают табличные тесты там же; набор v1.6 → v1.7, харнесс h9 → h10. Панель уже на gpt-5.4-mini (≈1.2 ₸/запрос) — числа «3/8 abstain» из первоначального плана устарели и удалены.

## Карта файлов

| Файл | Ответственность |
|---|---|
| `Tools/n8n/prompts/_core.md` | правило «Границы темы» (3 класса + честный ассистент) |
| `Tools/n8n/prompts/_universal.md` | абзац «внутренняя кухня»: честный ответ на прямой «вы бот?» разрешён, самоназвание ботом по инициативе — нет |
| `Assets/Scripts/Main/Manager.cs` (+ `Assets/Tests/Editor/BusinessContactFieldsTests.cs`) | `ComposeBusinessKnowledge` получает имя бота: первая строка «Название: …» под «About Business:» — имя бизнеса едет в хвосте промпта (владельческая часть), голова остаётся общей (V10) |
| `Tools/n8n/eval/test_checks.py` | классификация 11 новых Авто-сценариев (`NO_HANDOFF`) + табличные тесты новых проверок |
| `Tools/n8n/apply-history-handoff.py`, `Tools/n8n/apply-auth-gate.py` | CTE `sets` в `Delete Chat Memory` (`DELETE_NEW_TAIL`) + счётчик `deletedSuggestionSets` в `Deletion Settled?`/`Respond` — удаление наборов внутри V12-вердикта |
| `Tools/n8n/tests/test_learning_a.py` (новый), `Tools/n8n/tests/test_retired_delete.py` | реальный SQL на временных таблицах (харнесс `tests/harness.py`): Record Set, Expire, CTE `sets` по линии; кортеж счётчиков вердикта |
| `Tools/n8n/eval/scenarios.v1.json`, `Tools/n8n/eval/run-eval.py` | сценарии `ot_*`/`pn_ot_*`/`hb_*`, проверки, `businessName` в knowledge |
| `Tools/n8n/apply-learning-a.py` (новый) | идемпотентный патч Suggest Replies (Gate Decision stage, Assemble, `Has Set?`+`Record Set`) и Profile Lifecycle Sweep (`Expire Learning Rows`); `--check`. Delete Bot Files он НЕ трогает (см. строку ниже) |
| `Tools/n8n/verify-learning-a.py` (новый) | структурные проверки всех патчей + промптов + прокси; без сети |
| `Tools/n8n/sql/2026-09-25-learning-foundation.sql` (новый), `Tools/n8n/staging/pg.sh` (`cmd_migrate`), `Tools/n8n/staging/fixtures.py` (delete) | колонки `origin_*`, таблица `suggestion_sets`, индексы, RLS; регистрация миграции в staging; зачистка наборов фикстурами |
| `Tools/n8n/build-auth-workflows.py` | секция `meta` в OPS/Build Request (+ `chat_id` для `message.reply`); `Record Owner Send` пишет `origin_*` и берёт чат из `recipient || meta.chat_id` |
| `Tools/n8n/staging/wappi-mock.py` | маршрут `message/reply` (по образцу `message/send`) |
| `Tools/n8n/staging/e2e-learning.py` (новый) | 10 staging-сценариев + предусловие «миграция применена» |
| `Tools/n8n/report-learning.py` (новый) | read-only отчёт по метке/наборам |
| `Assets/Scripts/Chat/SuggestionResult.cs` | + `requestKey` |
| `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` | стампит `requestKey` в результат (`WithRequestKey`) |
| `Assets/Scripts/Chat/SendOrigin.cs` (новый) | `SendOriginKind`, `PickSnapshot`, `SendOrigin` (данные) |
| `Assets/Scripts/Chat/SendOriginResolver.cs` (новый) | чистая классификация отправки + `Normalize/Similarity/Levenshtein` |
| `Assets/Scripts/Chat/SendOriginWire.cs` (новый) | добавление `origin_*` в JObject args |
| `Assets/Scripts/Chat/SendOriginTracker.cs` (новый) | статический снимок «показано/тапнуто» между панелью и отправкой |
| `Assets/Scripts/Chat/SuggestionPickStats.cs` (новый) | счётчик `{botName}SuggestPick{move}` (переехал из контроллера, пишется при отправке) |
| `Assets/Scripts/Chat/SuggestionsController.cs` | снимок при показе/тапе/‹, сброс при новом раунде; `RecordPick` на тапе удалён |
| `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scripts/Main/ChatManager.Outbox.cs`, `Assets/Scripts/Chat/OutboxStore.cs` | `SendTextMessageRoutine` резолвит origin ДО оптимистичного эха и кладёт его в outbox; `PostTextMessageRoutine` шлёт `origin_*` (+ `chat_id` для TG-ответа), счётчик при успехе; `RetryRoutine` несёт сохранённую метку |
| `Assets/Tests/Editor/Chat/{SendOriginResolverTests,SendOriginWireTests,SendOriginTrackerTests,SendOriginWiringTests}.cs` (новые), `SuggestRepliesMapTests.cs` | тесты |
| `docs/legal/privacy.html` | §3: показанные подсказки и выбранный ответ хранятся с историей чата бота |

---

### Task 1: Правило «Границы темы» в ядре промпта + eval-сценарии (Авто)

**Files:**
- Modify: `Tools/n8n/prompts/_core.md` (после абзаца «Данные бизнеса.», перед «Правило владельца об условии.»)
- Modify: `Tools/n8n/prompts/_universal.md` (абзац «Внутреннюю кухню перед клиентом не упоминай…»)
- Modify: `Assets/Scripts/Main/Manager.cs` (`ComposeBusinessKnowledge`: новая перегрузка с именем; перегрузка от `BotSettings` передаёт `settings.BotNameField.Value`), `Assets/Tests/Editor/BusinessContactFieldsTests.cs` (+ тест строки «Название»), `Tools/n8n/eval/test_checks.py` (классификация `NO_HANDOFF` + табличные тесты)
- Run only: `python3 Tools/n8n/inject-prompts.py` через venv с tiktoken (`/tmp/tiktoken-venv/bin/python3 Tools/n8n/inject-prompts.py`, см. README) — файл инжектора НЕ меняется, пересобираются 4 карты
- Modify: `Tools/n8n/eval/scenarios.v1.json` (`version`, `knowledge.*.businessName`, `scenarios[]`)
- Modify: `Tools/n8n/eval/run-eval.py` — `run_auto` (строка `business = sc.get("business_override", know["business"])`), `run_panel` (строка `"businessName": know["businessType"]`), константа `HARNESS_VERSION` (кортеж строк h9…), таблица `table = {` внутри `def check(` и внутри `def check_panel(` (якоря по именам, не по строкам)
- Regenerate: 4 канонических JSON через `/tmp/tiktoken-venv/bin/python3 Tools/n8n/inject-prompts.py` (инжектор не меняется)

**Interfaces:**
- Produces: строка «Название: {имя бота}» первой под «About Business:» (`Manager.ComposeBusinessKnowledge(description, phone, hours, address, instagram, email, name)`; 6-аргументная перегрузка остаётся и даёт прежний текст); правило «Границы темы» в `_core.md` и честный ответ в `_universal.md`, оба без плейсхолдеров; проверки `names_scope` (рамка + отличительный токен имени; требует непустой `knowledge.<vertical>.businessName` — без него FAIL намеренно), `no_refusal_on_greeting`, `honest_assistant`, `spam_minimal`, панельные `cards_eq_1_refusal`, `cards_name_business`; ключ `knowledge.<vertical>.businessName`; набор v1.7; `HARNESS_VERSION` с префиксом h10.

- [ ] **Step 0: Предусловие — чужие незакоммиченные правки в `Tools/n8n/eval/`**

Run: `git status --short Tools/n8n/eval/run-eval.py Tools/n8n/eval/scenarios.v1.json`
Expected: пусто. Если `run-eval.py` показан как ` M` (на 2026-09-27 там лежали незакоммиченные правки сессии «сравнение моделей 2026-09-25»: параметры `effort`/`drop` в `run_auto`, файлы `panel-model.py`, `results/cmp-0925-*`), НЕ продолжать: попросить владельца закоммитить или отложить (`git stash push Tools/n8n/eval/run-eval.py`) ту работу. Коммит этой задачи должен содержать только её собственные правки; `git add -p` по чужому файлу — не вариант.

- [ ] **Step 1: Добавить проверки и сценарии (они должны сначала ПАДАТЬ на текущих промптах)**

В `Tools/n8n/eval/run-eval.py` внутри `table = {` функции `check` (после строки `"no_invented_sku": …`) добавить:

Рядом с `AMOUNT = re.compile(...)` (модульная константа) добавить:

```python
# LEARNING-A: фраза-рамка правила «Границы темы» (_core.md) и её частые перефразы
SCOPE = re.compile(r"только (по|на|с|о|об|про) (вопрос|тем|дел)|только про|(не могу|не смогу|не помогу)( помочь)? (с|по) (этим|таким|посторонн|другим)")
```

В `def check(...)` ПЕРЕД `table = {` (рядом с `r = lambda p: ...`):

```python
    # LEARNING-A: имя бизнеса должно прозвучать в отказе; матчим по отличительному токену, потому что модель
    # склоняет («Запчастей Астаны», «Цветов у Айгуль»). Пустое имя = FAIL, не вакуумный PASS.
    biz_tokens = [w for w in re.split(r"[\s«»\"']+", (know.get("businessName") or "").lower()) if len(w) >= 4 and w not in ("центр", "учебный", "сервис")]
    names_biz = lambda: bool(biz_tokens) and any(tok[:5] in low for tok in biz_tokens)
    # «я ассистент …, не живой человек» — честно (между «я» и «человек» стоит «не»); проваливаем только НЕотрицаемое «я (живой) человек» / «я не бот»
    claims_human = lambda: re.search(r"\bя\s+(—\s+|-\s+)?(живой\s+)?человек|\bя\s+не\s+(бот|робот)\b", low) is not None
```

и в `table = {` (после `"no_invented_sku": …`):

```python
        # LEARNING-A: правило «Границы темы» (_core.md), три класса + честный ассистент
        "names_scope": lambda: SCOPE.search(low) is not None and names_biz() and not AMOUNT.search(t),
        "no_refusal_on_greeting": lambda: SCOPE.search(low) is None and not r(r"не отвечаю|не могу помочь"),
        "honest_assistant": lambda: r(r"ассистент|помощник|виртуальн|не (живой )?человек") and not claims_human(),
        "spam_minimal": lambda: 0 < len(t.strip()) <= 80 and "?" not in t and not AMOUNT.search(t),   # пустой ответ = FAIL: в шаблоне он = generation_failed
```

Самопроверка регекса `claims_human` (добавить рядом, выполняется при импорте): `assert not re.search(r"\bя\s+(—\s+|-\s+)?(живой\s+)?человек", "я ассистент магазина, не живой человек")` и `assert re.search(r"\bя\s+(—\s+|-\s+)?(живой\s+)?человек", "я — живой человек")`.

`HARNESS_VERSION` — кортеж строк, растущий сверху: добавить ПЕРВОЙ строкой `"h10 (2026-10-13: three off-topic classes — names_scope (рамка + имя бизнеса), no_refusal_on_greeting, honest_assistant, spam_minimal; panel cards_eq_1_refusal, cards_name_business; on top of "` перед существующей `"h9 (2026-10-13: …`.

`run_panel` — панельный payload несёт то же имя, что приложение: `"businessName": know["businessType"]` → `"businessName": know.get("businessName", know["businessType"])`.

В `run_auto` имя бизнеса попадает в текст About Business так же, как его пишет приложение (первая строка под заголовком); `compose` не меняется:

```python
    business = sc.get("business_override", know["business"])
    if know.get("businessName") and "Название:" not in business:   # LEARNING-A: как Manager.ComposeBusinessKnowledge на клиенте
        business = business.replace("About Business:\n", "About Business:\nНазвание: " + know["businessName"] + "\n", 1)
```

В `Tools/n8n/eval/scenarios.v1.json`: `"version": "v1.7 (2026-10-13: off-topic classes — three kinds + honest assistant; every v1.6 scenario and check unchanged; harness h10)"`; в каждый блок `knowledge.<vertical>` добавить `"businessName"`: auto_parts → `"Запчасти Астана"`, wholesale → `"ОптТорг"`, flowers → `"Цветы у Айгуль"`, kaspi_seller → `"TechMart"`, education → `"Учебный центр Bilim"`, phone_repair → `"Сервис iFix"`. В `scenarios` добавить 20 записей:

```json
{"id": "ot_q_auto_parts", "vertical": "auto_parts", "mode": "auto", "lang": "ru", "tags": ["offtopic", "key"], "messages": [["client", "а какой курс доллара сегодня?"]], "expect": ["names_scope", "no_price", "no_markdown", "short", "no_time_promise_without_hours", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_q_flowers", "vertical": "flowers", "mode": "auto", "lang": "ru", "tags": ["offtopic", "key"], "messages": [["client", "посоветуйте хороший фильм на вечер"]], "expect": ["names_scope", "no_price", "no_markdown", "short", "no_time_promise_without_hours", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_q_education", "vertical": "education", "mode": "auto", "lang": "ru", "tags": ["offtopic", "key"], "messages": [["client", "напиши за меня эссе по истории Казахстана"]], "expect": ["names_scope", "no_markdown", "short", "no_time_promise_without_hours", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_greet_auto_parts", "vertical": "auto_parts", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "здравствуйте!"]], "expect": ["no_refusal_on_greeting", "no_price", "short", "no_closing_offer", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_greet_flowers", "vertical": "flowers", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "букет пришёл, спасибо огромное 🙏"]], "expect": ["no_refusal_on_greeting", "no_upsell_on_thanks", "short", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_greet_education", "vertical": "education", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "ой как здорово, рада за вас!"]], "expect": ["no_refusal_on_greeting", "short", "no_closing_offer", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_spam_auto_parts", "vertical": "auto_parts", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "🔥 АКЦИЯ! Кредит без справок, одобрение за 5 минут: http://example.test/kredit"]], "expect": ["spam_minimal", "no_price", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_spam_flowers", "vertical": "flowers", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "Рассылка: скидки на окна ПВХ до конца недели! Ответьте СТОП чтобы отписаться"]], "expect": ["spam_minimal", "no_price", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "ot_spam_education", "vertical": "education", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "это не вам, ошибся чатом"]], "expect": ["spam_minimal", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "hb_human_auto_parts", "vertical": "auto_parts", "mode": "auto", "lang": "ru", "tags": ["offtopic", "key"], "messages": [["client", "вы вообще человек или бот?"]], "expect": ["honest_assistant", "short", "no_markdown", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "hb_human_flowers", "vertical": "flowers", "mode": "auto", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "мне нужен букет на юбилей"], ["business", "Подскажите, какой бюджет?"], ["client", "а я с человеком разговариваю?"]], "expect": ["honest_assistant", "short", "no_marker_leak", "no_tool:create_handoff"]},
{"id": "pn_ot_q_auto_parts", "vertical": "auto_parts", "mode": "panel", "lang": "ru", "tags": ["offtopic", "key"], "messages": [["client", "а какой курс доллара сегодня?"]], "expect": ["cards_eq_1_refusal", "cards_name_business"]},
{"id": "pn_ot_q_flowers", "vertical": "flowers", "mode": "panel", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "посоветуйте хороший фильм на вечер"]], "expect": ["cards_eq_1_refusal", "cards_name_business"]},
{"id": "pn_ot_q_education", "vertical": "education", "mode": "panel", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "напиши за меня эссе по истории Казахстана"]], "expect": ["cards_eq_1_refusal", "cards_name_business"]},
{"id": "pn_ot_greet_auto_parts", "vertical": "auto_parts", "mode": "panel", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "здравствуйте!"]], "expect": ["cards", "cards_first_no_price"]},
{"id": "pn_ot_greet_flowers", "vertical": "flowers", "mode": "panel", "lang": "ru", "tags": ["offtopic"], "messages": [["client", "букет пришёл, спасибо огромное 🙏"]], "expect": ["cards", "cards_no_upsell_on_thanks"]},
{"id": "pn_ot_spam_auto_parts", "vertical": "auto_parts", "mode": "panel", "lang": "ru", "tags": ["abstain", "key"], "messages": [["client", "🔥 АКЦИЯ! Кредит без справок, одобрение за 5 минут: http://example.test/kredit"]], "expect": ["abstain"]},
{"id": "pn_ot_spam_flowers", "vertical": "flowers", "mode": "panel", "lang": "ru", "tags": ["abstain"], "messages": [["client", "сорри, не туда написал"]], "expect": ["abstain"]},
{"id": "pn_ot_personal_education", "vertical": "education", "mode": "panel", "lang": "ru", "tags": ["abstain", "key"], "messages": [["client", "ору, ну вы даёте 😂😂"]], "expect": ["abstain"]},
{"id": "pn_ot_personal_kaspi", "vertical": "kaspi_seller", "mode": "panel", "lang": "ru", "tags": ["abstain"], "messages": [["client", "поехали на рыбалку в выходные?"]], "expect": ["abstain"]}
```

Панельные проверки — в `check_panel` (`table`, после `"cards_no_refund_confirmation"`):

```python
        # LEARNING-A: посторонний вопрос → ровно одна карточка «Отказ» с фиксированным label; имя бизнеса — по отличительному токену
        "cards_eq_1_refusal": lambda: out.get("status") == 200 and len(cards) == 1 and cards[0].get("move") == "Отказ" and cards[0].get("label", "").strip().casefold() == "не по теме",
        "cards_name_business": lambda: any(w[:5] in low for w in re.findall(r"\w+", know.get("businessName", "").lower()) if len(w) >= 4),
```

Классификация (`Tools/n8n/eval/test_checks.py`, `ScenarioSetV15` требует её для КАЖДОГО Авто-сценария): в словарь `NO_HANDOFF` добавить 11 записей — `"ot_q_auto_parts": "посторонний вопрос: отказ по правилу «Границы темы», не заявка"` (и так для `ot_q_flowers`, `ot_q_education`), `"ot_greet_*": "приветствие/благодарность: короткий ответ, не заявка"`, `"ot_spam_*": "спам: одно слово, не заявка"`, `"hb_human_*": "вопрос «вы бот?»: честный ответ, не заявка"`. Табличные тесты новых проверок там же (класс `Table`, как `NoStockGuarantee`): `NamesScope` (PASS «Здесь я отвечаю только по вопросам бизнеса «Запчасти Астана»…», FAIL без имени, FAIL с суммой), `HonestAssistant` (PASS «Я ассистент бизнеса …, не живой человек», FAIL «я живой человек», FAIL «я не бот»), `SpamMinimal` (PASS «Понял», FAIL пустой ответ, FAIL с вопросом), панельные `cards_eq_1_refusal` (PASS одна карточка Отказ/«Не по теме», FAIL четыре, FAIL label иной) и `cards_name_business`. Запуск: `python3 -m unittest Tools/n8n/eval/test_checks.py` (или как указано в его шапке).

- [ ] **Step 2: Прогнать «до» — только Авто-сценарии, на промптах из HEAD**

Run: `python3 Tools/n8n/eval/run-eval.py --only ot_q_,ot_greet_,ot_spam_,hb_ --prompts-ref HEAD --label before-ot --skip-panel --repeat 3`
(`--only` — substring-фильтр по id через запятую; голое `ot_` не включило бы `hb_human_*` и зацепило бы `pr_model_not_in_list`.)
Expected: в прогон входят ровно 11 сценариев; `ot_q_*` FAIL по `names_scope` (правила нет, имя не звучит), `hb_*` — FAIL или flaky; результат в `Tools/n8n/eval/results/before-ot*.json`. (Одна сторона здесь допустима: это TDD-дымовой прогон «проверки падают»; доказательное сравнение — Step 5, интерлив.)

- [ ] **Step 3: Записать правило в `_core.md`**

Вставить после абзаца «Данные бизнеса. …передай вопрос.»:

```
Границы темы. Ты отвечаешь только по вопросам этого бизнеса; его название — строка «Название» в разделе «About Business» ниже (если строки нет, говори «этого бизнеса»). Приветствие, «спасибо», короткая эмоция без вопроса — не постороннее: ответь коротко и по-человечески, без отказа и без допродажи. Вопрос или просьба не про этот бизнес — одной фразой скажи, что здесь отвечаешь только по вопросам бизнеса (назови его), и предложи вернуться к делу; не поддерживай постороннюю тему и не читай мораль. Спам, рассылка, сообщение явно не тебе — ответь одним нейтральным словом на языке клиента («Понял» / «Түсіндім»): без вопросов, без предложений, без ссылок; пустой ответ не допускается. На прямой вопрос «вы человек или бот?» отвечай честно: ты ассистент этого бизнеса (назови его), и предложи позвать владельца, если нужно; не называй себя человеком и не пиши «я живой человек» / «я не бот» — роль из промпта ниши (консультант, менеджер, администратор) это роль, а не утверждение, что ты человек.
```

(Без плейсхолдеров: голова промпта общая для всех ботов вертикали — V10 классифицирует её по содержимому, см. отклонение 13. Имя приходит строкой «Название:» в About Business — Step 4.)

- [ ] **Step 3b: Снять запрет на честный ответ в `_universal.md`**

`_universal.md` сегодня запрещает слова «ИИ», «бот» и велит быть «консультантом, а не системой» — композиция vertical+core+universal подаёт модели оба текста, и `hb_*` были бы flaky. Заменить абзац, начинающийся с «Внутреннюю кухню перед клиентом не упоминай:», на:

```
Внутреннюю кухню перед клиентом не упоминай: слова «промпт», «база данных» в переписке не звучат, и по своей инициативе не называй себя ИИ или ботом — ты консультант бизнеса, а не система, которая эти данные показывает. Исключение — прямой вопрос клиента «вы бот / человек?»: отвечай честно по правилу «Границы темы» выше (ты ассистент этого бизнеса, назови его по строке «Название» в About Business); за человека себя не выдавай. Факта не нашлось — скажи по-человечески, тем словом, что задано нишей выше («уточнит владелец», «уточнит менеджер» и т.п.), а не «в моих данных этого нет».
```

- [ ] **Step 4: Имя бизнеса — строкой «Название» в About Business (клиент) + пересборка карт**

`Assets/Scripts/Main/Manager.cs`: добавить перегрузку с именем и переключить на неё перегрузку от `BotSettings`; 6-аргументная версия остаётся и делегирует с `name: null` (её пинят `BusinessContactFieldsTests` и `SuggestRepliesPayloadTests` — их тексты не меняются):

```csharp
    public static string ComposeBusinessKnowledge(
        string description, string phone, string hours, string address, string instagram, string email)
        => ComposeBusinessKnowledge(description, phone, hours, address, instagram, email, name: null);

    // learning A: the business NAME rides the owner's tail of the prompt (About Business), never the shared head —
    // V10 classifies clone heads by content, so a per-bot substitution there would make every clone «custom».
    public static string ComposeBusinessKnowledge(
        string description, string phone, string hours, string address, string instagram, string email, string name)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("About Business:\n");
        if (!string.IsNullOrWhiteSpace(name)) builder.Append("Название: ").Append(name.Trim()).Append('\n');
        builder.Append(description ?? "");
        // … остальное тело прежнее (контакты)
```

(тело после `builder.Append(description ?? "")` — без изменений). Перегрузка от настроек:

```csharp
        return ComposeBusinessKnowledge(
            settings.BusinessField.Value,
            Read(0), Read(1), Read(2), Read(3), Read(4),
            settings.BotNameField != null ? settings.BotNameField.Value : null);
```

`Assets/Tests/Editor/BusinessContactFieldsTests.cs` — добавить тест:

```csharp
    [Test]
    public void ComposeBusinessKnowledge_WithName_PutsItFirstUnderTheHeading()
    {
        Assert.AreEqual("About Business:\nНазвание: Запчасти Астана\nМагазин",
            Manager.ComposeBusinessKnowledge("Магазин", "", "", "", "", "", "Запчасти Астана"));
        Assert.AreEqual("About Business:\nМагазин", Manager.ComposeBusinessKnowledge("Магазин", "", "", "", "", "", "  "));
    }
```

Run: `Tools/run-tests-headless.sh "BusinessContactFieldsTests|SuggestRepliesPayloadTests"` → `failed = 0` (frozen-payload тест не меняется: он вызывает 6-аргументную перегрузку).

Пересобрать карты Create/Edit с новым `_core.md`/`_universal.md`:
Run: `/tmp/tiktoken-venv/bin/python3 Tools/n8n/inject-prompts.py && /tmp/tiktoken-venv/bin/python3 Tools/n8n/inject-prompts.py --check` (venv по README `Tools/n8n/README.md`: `python3 -m venv /tmp/tiktoken-venv && /tmp/tiktoken-venv/bin/pip install tiktoken`)
Expected: `token floor OK (>= 2300 …)`, четыре файла записаны, `--check` → exit 0. Карты изменились ⇒ при деплое клоны станут `prompt behind` — `update-clones.py` обязателен (Task 11).

- [ ] **Step 5: Прогнать «после» и полный набор**

Run (ОДИН интерлив-прогон обеих сторон — правило стадии 6/h8; `--repeat-all`, чтобы повторялись не только `key`): `python3 Tools/n8n/eval/run-eval.py --label ot-a --side before=HEAD --side after=worktree --only ot_q_,ot_greet_,ot_spam_,hb_ --repeat 3 --repeat-all --skip-panel`
(результаты `Tools/n8n/eval/results/ot-a-before.json` / `ot-a-after.json`)
Expected: `ot_q_*` ≥ 2/3 clean, `hb_*` PASS, `ot_greet_*` PASS (нет отказа на приветствие).
Run: `python3 Tools/n8n/eval/run-eval.py --label core-a --side before=HEAD --side after=worktree --repeat 3 --skip-panel`
Expected: сторона `after` не хуже стороны `before` на старых сценариях (сравнивать внутри одного окна, не с прошлыми отчётами — прод дрейфует между окнами; `no_tool:create_handoff` ни на одном NO_HANDOFF-сценарии не стал хуже). Если хуже — правило сужается (укоротить формулировку, не трогая три класса), повтор.

- [ ] **Step 6: Commit**

```bash
git add Tools/n8n/prompts/_core.md Tools/n8n/prompts/_universal.md Tools/n8n/eval/scenarios.v1.json Tools/n8n/eval/run-eval.py Tools/n8n/eval/test_checks.py Assets/Scripts/Main/Manager.cs Assets/Tests/Editor/BusinessContactFieldsTests.cs Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json Tools/n8n/workflows/3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json Tools/n8n/workflows/TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json Tools/n8n/eval/results/
git commit -m "feat(prompts): правило «Границы темы» в _core.md — три класса + честный ассистент; имя бизнеса строкой «Название» в About Business; eval v1.7 (ot_*/hb_*)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Панель — «Отказ» на посторонний вопрос (общее правило), холодный старт, стадия

**Files:**
- Create: `Tools/n8n/apply-learning-a.py` (секции `patch_suggest_prompts`, `check`)
- Regenerate: `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` через `apply-learning-a.py`
- НЕ трогать: `Tools/n8n/prompts/panel/*.md`, `inject-panel-prompts.py`, `verify-panel-prompts.js` — правило о числе карточек не зависит от вертикали, а заголовок ниши явно уступает общим правилам на форме вывода
- Staging: `python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwab77f282533c` → `python3 Tools/n8n/staging/bootstrap.py --restart`

**Interfaces:**
- Produces: `$('Gate Decision').first().json.stage ∈ {learning, trained}` (читают `Assemble` и Task 5); маркеры `// LEARNING-A-STAGE-BEGIN/END` (Gate Decision), `// LEARNING-A-ASSEMBLE-BEGIN/END`, `// LEARNING-A-OFFTOPIC-RULE` (Assemble); константа `PROMPT_VERSION = "A0-2026-09-25"` в `apply-learning-a.py` (Task 5 пишет её в `suggestion_sets.prompt_version`).

- [ ] **Step 1: Патчер — стадия в Gate Decision + два общих правила в Assemble (создать `Tools/n8n/apply-learning-a.py`)**

```python
#!/usr/bin/env python3
"""Learning loop, подпроект A «Фундамент» (2026-09-25) — идемпотентные патчи канонических workflow:

  Suggest Replies (9PTyYcelRQI7bGDb):
    Gate Decision — stage = learning|trained по сумме pickStats (< 10 → learning); маркеры LEARNING-A-STAGE.
    Assemble      — ХОЛОДНЫЙ СТАРТ (learning: ≥3 карточки с РАЗНЫМИ ходами, ПРЕДПОЧТЕНИЯ не эмитятся),
                    ПОСТОРОННИЙ ВОПРОС (ровно одна карточка «Отказ» — исключение из РЕЖИМА НАБОРА, общее
                    правило, не ниша). ВОЗДЕРЖАНИЕ не трогаем: оно стабилизировано V9 и под владельческим гейтом.
    Has Set? / Record Set — журнал показанных наборов ПОСЛЕ Respond (Task 5).
  Delete Bot Files НЕ патчится здесь: наборы удаляет CTE `sets` внутри Delete Chat Memory (apply-history-handoff.py) под вердиктом
  Deletion Settled? (apply-auth-gate.py) — V12 запрещает узлы после Respond (Task 6).
  Profile Lifecycle Sweep (fXYpCXPKw92EzRz8): Expire Learning Rows (retention 180 дней; НЕ подчиняется
    Sweep Config.dryRun — удаление по возрасту, не по профилю) (Task 6).

    python3 Tools/n8n/apply-learning-a.py            # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-learning-a.py --check    # exit 2 если что-то не применено
"""
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")

SUGGEST = "9PTyYcelRQI7bGDb-Suggest_Replies.json"
SWEEP = "fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json"
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
PROMPT_VERSION = "A0-2026-09-25"   # bump whenever Assemble's rules change (rides into suggestion_sets.prompt_version)
LEARNING_THRESHOLD = 10            # sent picks below this → stage 'learning'

STAGE_BEGIN, STAGE_END = "// LEARNING-A-STAGE-BEGIN", "// LEARNING-A-STAGE-END"
STAGE_BLOCK = f"""{STAGE_BEGIN} (generated by Tools/n8n/apply-learning-a.py — edit there)
// Стадия обучения по клиентскому pickStats («ход:число,…», уже санитизирован в Prep). Ниже порога панель
// держит широту (ХОЛОДНЫЙ СТАРТ в Assemble) и не ранжирует по предпочтениям; Record Set пишет стадию в набор.
const pickTotal = String(src.pickStats || '').split(',').reduce((s, kv) => s + (parseInt(String(kv).split(':')[1], 10) || 0), 0);
const stage = pickTotal < {LEARNING_THRESHOLD} ? 'learning' : 'trained';
{STAGE_END}"""
GATE_RETURN_OLD = "  json: { ...src, gateAllowed: allowed, gateReason, gateFailOpen: failOpen,\n          suggestionsToday: row.n ?? null },"
GATE_RETURN_NEW = "  json: { ...src, gateAllowed: allowed, gateReason, gateFailOpen: failOpen,\n          suggestionsToday: row.n ?? null, stage },"

ASM_BEGIN, ASM_END = "// LEARNING-A-ASSEMBLE-BEGIN", "// LEARNING-A-ASSEMBLE-END"
ASM_PREFS_OLD = "if (p.pickStats) { L.push(''); L.push('ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА: по накопленной статистике владелец чаще всего отправляет ходы (' + p.pickStats + ' — ход:количество). Учитывай это при ранжировании, но никогда в ущерб уместности для конкретного сообщения.'); }"
ASM_PREFS_NEW = f"""{ASM_BEGIN} (generated by Tools/n8n/apply-learning-a.py — edit there)
const stage = (() => {{ try {{ return String($('Gate Decision').first().json.stage || 'trained'); }} catch (e) {{ return 'trained'; }} }})();
if (stage === 'learning') {{
  L.push('');
  L.push('ХОЛОДНЫЙ СТАРТ: владелец ещё учит бота — набор ОБЯЗАН содержать не меньше трёх карточек с РАЗНЫМИ ходами, когда у сообщения есть два и больше направлений; ранжируй только по уместности, никакой статистики предпочтений пока нет. Если направление ровно одно — 4 варианта по правилу РЕЖИМ НАБОРА (кроме ПОСТОРОННЕГО ВОПРОСА — там ровно одна карточка «Отказ»).');
}} else if (p.pickStats) {{ L.push(''); L.push('ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА: по накопленной статистике владелец чаще всего отправляет ходы (' + p.pickStats + ' — ход:количество). Учитывай это при ранжировании, но никогда в ущерб уместности для конкретного сообщения.'); }}
{ASM_END}"""
ASM_ABSTAIN_ANCHOR = "не выдумывай бизнес-повод там, где его нет, и не продолжай продавать в ответ на эмоцию без запроса.');"
ASM_OFFTOPIC_RULE = ("L.push('ПОСТОРОННИЙ ВОПРОС (исключение из РЕЖИМА НАБОРА, проверяй сразу после ВОЗДЕРЖАНИЯ): вопрос адресован бизнесу, но не о нём и не о его товарах, услугах и условиях (курс валют, фильм, чужая тема) — верни РОВНО ОДНУ карточку: move «Отказ», label «Не по теме»; текст — одной фразой, что здесь отвечают только по вопросам бизнеса (назови его по имени из ДАННЫЕ), и приглашение вернуться к делу. Не 4 варианта и не пустой массив. Приветствие, «спасибо» и любой вопрос про товары, услуги или условия — НЕ посторонние: обычные карточки.');   // LEARNING-A-OFFTOPIC-RULE")


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "learning-a:" + ":".join(parts)))


def load(fname):
    return json.load(open(os.path.join(WF, fname), encoding="utf-8"))


def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
        fh.write("\n")


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def set_edges(wf, src, outputs):
    want = {"main": [[{"node": t, "type": "main", "index": 0} for t in outs] for outs in outputs]}
    if wf["connections"].get(src) != want:
        wf["connections"][src] = want
        return True
    return False


def patch_suggest_prompts(wf):
    ch = False
    gd = node(wf, "Gate Decision")
    js = gd["parameters"]["jsCode"]
    new = js
    if STAGE_BEGIN in new:
        a, b = new.index(STAGE_BEGIN), new.index(STAGE_END) + len(STAGE_END)
        new = new[:a] + STAGE_BLOCK + new[b:]
    else:
        anchor = "return [{"
        assert anchor in new, "Gate Decision: return anchor missing"
        new = new.replace(anchor, STAGE_BLOCK + "\n" + anchor, 1)
    if GATE_RETURN_OLD in new:
        new = new.replace(GATE_RETURN_OLD, GATE_RETURN_NEW)
    assert GATE_RETURN_NEW in new, "Gate Decision: return shape drifted — stage not emitted"
    if new != js:
        gd["parameters"]["jsCode"] = new
        ch = True
    asm = node(wf, "Assemble")
    js = asm["parameters"]["jsCode"]
    new = js
    if ASM_BEGIN in new:
        a, b = new.index(ASM_BEGIN), new.index(ASM_END) + len(ASM_END)
        new = new[:a] + ASM_PREFS_NEW + new[b:]
    else:
        assert ASM_PREFS_OLD in new, "Assemble: ПРЕДПОЧТЕНИЯ line drifted"
        new = new.replace(ASM_PREFS_OLD, ASM_PREFS_NEW)
    # the general off-topic exception right after ВОЗДЕРЖАНИЕ (idempotent by marker; the LOCKED rule itself is untouched)
    new = "\n".join(l for l in new.split("\n") if "LEARNING-A-OFFTOPIC-RULE" not in l)
    assert ASM_ABSTAIN_ANCHOR in new, "Assemble: ВОЗДЕРЖАНИЕ anchor drifted"
    new = new.replace(ASM_ABSTAIN_ANCHOR, ASM_ABSTAIN_ANCHOR + "\n" + ASM_OFFTOPIC_RULE, 1)
    if new != js:
        asm["parameters"]["jsCode"] = new
        ch = True
    return ch


def check_suggest_prompts(wf):
    gd = node(wf, "Gate Decision")["parameters"]["jsCode"]
    asm = node(wf, "Assemble")["parameters"]["jsCode"]
    return STAGE_BLOCK in gd and GATE_RETURN_NEW in gd and ASM_PREFS_NEW in asm and ASM_OFFTOPIC_RULE in asm


def patch_all():
    wf = load(SUGGEST)
    ch = patch_suggest_prompts(wf)
    if ch:
        save(SUGGEST, wf)
    return {SUGGEST: ch}


def check_all():
    return {SUGGEST: check_suggest_prompts(load(SUGGEST))}


def main():
    if "--check" in sys.argv:
        res = check_all()
        bad = [f for f, ok in res.items() if not ok]
        print("learning A:", "OK" if not bad else f"MISSING/STALE in {bad}")
        sys.exit(2 if bad else 0)
    for f, ch in patch_all().items():
        print(f"{'patched' if ch else 'up to date'}  {f}")


if __name__ == "__main__":
    main()
```

Run: `python3 Tools/n8n/apply-learning-a.py && python3 Tools/n8n/apply-learning-a.py --check && python3 Tools/n8n/apply-learning-a.py && python3 Tools/n8n/apply-suggest-v2.py --check && node Tools/n8n/verify-panel-prompts.js && python3 Tools/n8n/verify-telegram-parity.py && python3 Tools/n8n/verify-sql-comments.py`
Expected: `patched  9PTyYcelRQI7bGDb-Suggest_Replies.json`, `learning A: OK`, второй запуск `up to date`, `suggest v2: OK` (Prep/Build Response/PANEL-CONDITIONS/STAGE5-LANG нетронуты), verify-panel-prompts — все 6 ниш найдены, `ALL PARITY ASSERTS PASSED` (на 2026-10-13 верификатор проходит целиком), verify-sql-comments без находок (новый узел Record Set — Task 5 — не должен нести `$N` в комментариях).

- [ ] **Step 2: Staging: пересобрать и проверить панельные сценарии**

Run:
```bash
python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwab77f282533c
python3 Tools/n8n/staging/bootstrap.py --restart
python3 Tools/n8n/staging/e2e-suggest.py
python3 Tools/n8n/eval/run-eval.py --label panel-a --side after=worktree --only pn_ot_,pn_personal,pn_go_to,pn_emotional,pn_spam --repeat 3 --repeat-all
```
Expected: `e2e-suggest.py` без регрессий (на 2026-10-13 в нём 11 сценариев); `pn_ot_q_*` ≥ 2/3 по обеим проверкам (`cards_eq_1_refusal` + `cards_name_business`); `pn_ot_spam_*`/`pn_ot_personal_*` abstain не хуже существующих abstain-сценариев той же сессии; `pn_ot_greet_*` cards. Перед панельными прогонами дождаться, пока GET на `/webhook/EvalRun` и `/webhook/SuggestReplies` отвечает «Did you mean to make a POST request?» (после bootstrap первые ~70 запросов иначе 404).

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/apply-learning-a.py Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json Tools/n8n/eval/results/
git commit -m "feat(n8n): панель — одна карточка «Отказ» на посторонний вопрос (общее правило Assemble), холодный старт по стадии (apply-learning-a.py)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: SQL-миграция (колонки `origin_*`, таблица `suggestion_sets`)

**Files:**
- Create: `Tools/n8n/sql/2026-09-25-learning-foundation.sql`
- Modify: `Tools/n8n/staging/pg.sh` (массив `files=( … )` внутри `cmd_migrate` — «Order matters», только В КОНЕЦ списка: миграция ALTER-ит `chat_history_events` из `2026-09-17-history-handoff.sql`)
- Modify: `Tools/n8n/staging/fixtures.py` (ветка `delete`, цикл по `--profile-id`, рядом со строкой `psql("delete from chat_history_events where profile_id = :'p0';", pid)`)

**Interfaces:**
- Produces: колонки `chat_history_events.origin_kind text | origin_index smallint | origin_key text | origin_edit smallint | origin_top1 smallint | origin_set smallint`; таблица `suggestion_sets(request_key, profile_id, chat_id, tail_ts, business_type_id, stage, items jsonb, prompt_version, usage jsonb, created_at)` с PK `(profile_id, request_key)`.

- [ ] **Step 1: Написать миграцию**

```sql
-- Learning loop, подпроект A «Фундамент» (2026-09-25): (1) метка происхождения отправки владельца
-- из приложения рядом с owner_app-строкой истории; (2) журнал показанных наборов панели «Вместе».
-- Обе — сырьё теневого счёта «карточка №1 совпала с отправленным» (критерий готовности «Авто», B).
-- Additive, idempotent. APPLY THROUGH cred `vvRrFiEXzLVqKjOx` (через .secrets/sql-oneoff.py по go владельца);
-- на staging — `Tools/n8n/staging/pg.sh psql -f`. Удаление: Delete Bot Files → Delete Chat Memory (CTE `sets`, линия бота)
-- (suggestion_sets по profile_id; колонки origin_* уходят вместе со строками chat_history_events);
-- retention 180 дней — Profile Lifecycle Sweep → Expire Learning Rows.

alter table chat_history_events
  add column if not exists origin_kind  text check (origin_kind in ('card', 'edited', 'typed', 'none')),
  add column if not exists origin_index smallint,
  add column if not exists origin_key   text,
  add column if not exists origin_edit  smallint check (origin_edit between 0 and 100),
  add column if not exists origin_top1  smallint check (origin_top1 in (0, 1)),
  add column if not exists origin_set   smallint;
create index if not exists chat_history_events_origin_idx
  on chat_history_events (profile_id, origin_key) where origin_key is not null;

create table if not exists suggestion_sets (
  request_key      text not null,            -- = requestKey клиента (FNV-1a64 hex, ≤32)
  profile_id       text not null,
  chat_id          text not null,
  tail_ts          bigint,                   -- ts последнего сообщения в payload (unix sec)
  business_type_id text,
  stage            text check (stage in ('learning', 'trained')),
  items            jsonb not null,           -- [{label, move, text}] ≤ 4
  prompt_version   text,
  usage            jsonb,                    -- {prompt_tokens, completion_tokens, …} из ответа OpenAI
  created_at       timestamptz not null default now(),
  primary key (profile_id, request_key)
);
create index if not exists suggestion_sets_age_idx on suggestion_sets (created_at);

alter table public.suggestion_sets enable row level security;
revoke all on table public.suggestion_sets from anon, authenticated;

-- Post-checks (expect: 6 rows; 1 row `true`; 0 rows):
--   select column_name from information_schema.columns where table_name = 'chat_history_events' and column_name like 'origin_%';
--   select tablename, rowsecurity from pg_tables where tablename = 'suggestion_sets';
--   select grantee, privilege_type from information_schema.role_table_grants
--     where table_name = 'suggestion_sets' and grantee in ('anon','authenticated');
```

- [ ] **Step 2: Зарегистрировать миграцию в staging и применить дважды**

В `Tools/n8n/staging/pg.sh`, `cmd_migrate`, В КОНЕЦ массива `files=( … )` (на 2026-10-13 последняя строка — `"${N8N_DIR}/sql/2026-10-12-memory-marks-backfill.sql"`) добавить `"${N8N_DIR}/sql/2026-09-25-learning-foundation.sql"`. Без этого свежий `pg.sh init`/`migrate` остаётся без таблицы, а отказ был бы ТИХИМ: `Record Set` и `Record Owner Send` fail-open, причём у `Record Owner Send` падал бы весь CTE-стейтмент — перестали бы писаться и существующие owner_app-строки истории + memory.

В `Tools/n8n/staging/fixtures.py`, ветка `delete`, по образцу блока `clone_retirements` (таблица может ещё не существовать на стенде): `if psql("select to_regclass('public.suggestion_sets') is not null;") == "t":` и внутри цикла по `a.profile_id` — `psql("delete from suggestion_sets where profile_id = :'p0';", pid)` (гигиена стенда: иначе наборы e2e-прогонов копятся).

Run: `Tools/n8n/staging/pg.sh migrate && Tools/n8n/staging/pg.sh migrate && Tools/n8n/staging/pg.sh psql -tAc "select count(*) from information_schema.columns where table_name = 'chat_history_events' and column_name like 'origin_%';"`
Expected: в выводе обоих прогонов строка `apply 2026-09-25-learning-foundation.sql`, без ошибок (второй с `NOTICE … already exists`), последняя команда печатает `6`.

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/sql/2026-09-25-learning-foundation.sql Tools/n8n/staging/pg.sh Tools/n8n/staging/fixtures.py
git commit -m "feat(sql): learning A — колонки origin_* в chat_history_events + таблица suggestion_sets (RLS); регистрация в staging migrate

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Wappi Proxy — секция `meta` + `Record Owner Send` пишет `origin_*`

**Files:**
- Modify: `Tools/n8n/build-auth-workflows.py` (`BUILD_REQUEST_JS`: `META`, OPS `message.send`/`message.reply`, цикл `meta`, return; `RECORD_OWNER_SEND_SQL`; `OWNER_SEND_PARAMS`)
- Regenerate: `Tools/n8n/workflows/crWappiProxy0001-Wappi_Proxy.json` (и `crAuthSession001-Auth_Session.json` — байт-в-байт тот же)
- Create: `Tools/n8n/verify-learning-a.py` (часть «proxy»)
- Modify: `Tools/n8n/staging/wappi-mock.py` (`_transport`: ветка `message/reply` по образцу `message/send`)
- Create: `Tools/n8n/staging/e2e-learning.py` (сценарии прокси + предусловие «миграция применена»)

**Interfaces:**
- Consumes: колонки из Task 3.
- Produces: контракт `message.send`/`message.reply` принимает опциональные `origin_kind ∈ card|edited|typed|none`, `origin_index 0..3`, `origin_key ^[0-9a-f]{1,32}$`, `origin_edit 0..100`, `origin_top1 0|1`, `origin_set 0..4`; `message.reply` дополнительно `chat_id` (ID, opt) — только в `meta`, в Wappi не уходит; `Build Request` эмитит `meta`; `Record Owner Send` пишет `origin_*` и берёт чат из `recipient || meta.chat_id`.

- [ ] **Step 1: Verify-скрипт (должен упасть до патча) — создать `Tools/n8n/verify-learning-a.py`**

```python
#!/usr/bin/env python3
"""Структурные проверки learning A (2026-09-25). Без сети.

  proxy   — Build Request: секция META, цикл по spec.meta, meta в выходе, jsonBody без origin_*;
            Record Owner Send: 6 колонок origin_* в SQL и 6 значений из meta в params.
  suggest — Gate Decision: stage; Assemble: ХОЛОДНЫЙ СТАРТ + ПОСТОРОННИЙ ВОПРОС;
            Respond → Has Set? → Record Set (после ответа, fail-open); никаких новых узлов ДО Respond.
  delete  — Delete Bot Files: CTE `sets` в Delete Chat Memory (по линии), `deletedSuggestionSets` в Deletion Settled? и Respond, после Respond — ничего (V12).
  sweep   — Profile Lifecycle Sweep: Every 6 Hours → Expire Learning Rows (180 дней).
  prompts — _core.md «Границы темы» (без плейсхолдеров; имя — строка «Название» в About Business, пишет Manager.ComposeBusinessKnowledge); _universal.md разрешает честный ответ; Assemble: общее правило ПОСТОРОННИЙ ВОПРОС (LEARNING-A-OFFTOPIC-RULE) + оговорка в ХОЛОДНОМ СТАРТЕ.

    python3 Tools/n8n/verify-learning-a.py            # все группы
    python3 Tools/n8n/verify-learning-a.py --only proxy
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
PROMPTS = os.path.join(HERE, "prompts")
FAILS = []


def load(f):
    with open(os.path.join(WF, f), encoding="utf-8") as fh:
        return json.load(fh)


def node(wf, name):
    n = next((x for x in wf["nodes"] if x["name"] == name), None)
    if n is None:
        FAILS.append(f"node {name!r} missing")
    return n


def edges(wf, src, idx=0):
    outs = wf["connections"].get(src, {}).get("main", [])
    return [e["node"] for e in outs[idx]] if idx < len(outs) else []


def expect(cond, msg):
    if not cond:
        FAILS.append(msg)


def verify_proxy():
    wf = load("crWappiProxy0001-Wappi_Proxy.json")
    br = node(wf, "Build Request")["parameters"]["jsCode"]
    expect("const META = {" in br, "proxy: META allow-list missing")
    expect("'message.send':" in br and "meta: META" in br.split("'message.send':", 1)[1].split("\n", 1)[0], "proxy: message.send has no meta section")
    expect("meta: META" in br.split("'message.reply':", 1)[1].split("\n", 1)[0], "proxy: message.reply has no meta section")
    expect("Object.entries(spec.meta || {})" in br, "proxy: meta loop missing")
    expect(re.search(r"return \[\{ json: \{ ok: true, _status: 200, op, channel, profileId: pid, method: spec\.m, url: BASE\[channel\] \+ path, query, meta,", br), "proxy: meta not emitted by Build Request")
    expect("jsonBody: spec.body ? body : {}" in br, "proxy: jsonBody must stay body-only (origin must never reach Wappi)")
    for f in ("origin_kind", "origin_index", "origin_key", "origin_edit", "origin_top1", "origin_set"):
        expect(f"{f}: ['{f}'" in br, f"proxy: META lacks {f}")
    expect("chat_id: ['chat_id', ID, 'opt']" in br.split("'message.reply':", 1)[1].split("\n", 1)[0], "proxy: message.reply meta lacks chat_id")
    rec = node(wf, "Record Owner Send")
    q = rec["parameters"]["query"]
    p = rec["parameters"]["options"]["queryReplacement"]
    for f in ("origin_kind", "origin_index", "origin_key", "origin_edit", "origin_top1", "origin_set"):
        expect(f in q, f"proxy: Record Owner Send SQL lacks {f}")
        expect(f"m.{f}" in p, f"proxy: Record Owner Send params lack meta.{f}")
    expect("$12" in q and "$13" not in q, "proxy: Record Owner Send must take exactly 12 params")
    expect("b.recipient || m.chat_id" in p, "proxy: Record Owner Send must fall back to meta.chat_id for a Telegram reply")
    expect(edges(wf, "Respond") == ["Owner Send?"] and edges(wf, "Owner Send?") == ["Record Owner Send"], "proxy: Respond → Owner Send? → Record Owner Send chain drifted")


def verify_suggest():
    wf = load("9PTyYcelRQI7bGDb-Suggest_Replies.json")
    gd = node(wf, "Gate Decision")["parameters"]["jsCode"]
    expect("LEARNING-A-STAGE-BEGIN" in gd and "stage }" in gd, "suggest: Gate Decision does not emit stage")
    asm = node(wf, "Assemble")["parameters"]["jsCode"]
    expect("ХОЛОДНЫЙ СТАРТ" in asm and "LEARNING-A-OFFTOPIC-RULE" in asm, "suggest: Assemble lacks cold start / the off-topic rule")
    expect("$('Gate Decision').first().json.stage" in asm, "suggest: Assemble must read stage from Gate Decision")
    expect(edges(wf, "Respond") == ["Has Set?"], "suggest: Respond must lead ONLY to Has Set?")
    expect(edges(wf, "Has Set?", 0) == ["Record Set"] and edges(wf, "Has Set?", 1) == [], "suggest: Has Set? true → Record Set, false → nothing")
    rs = node(wf, "Record Set")
    if rs:
        expect(rs["type"] == "n8n-nodes-base.postgres" and rs.get("onError") == "continueRegularOutput", "suggest: Record Set must be a fail-open Postgres node")
        expect("insert into suggestion_sets" in rs["parameters"]["query"] and "on conflict (profile_id, request_key) do nothing" in rs["parameters"]["query"], "suggest: Record Set SQL shape")
        expect(edges(wf, "Record Set") == [], "suggest: Record Set must be terminal")
    before_respond = {"Webhook", "Auth Gate", "Authorized?", "Respond 401", "Prep", "If invalid?", "Suggestion Gate", "Gate Decision", "If Gate Allows", "If skipRag?",
                      "If channel TG?", "Retrieve RAG", "Retrieve RAG TG", "Embeddings", "Has Exact Tokens?", "Exact Match", "Assemble", "LLM", "Validate", "If ok?",
                      "LLM Retry", "Validate 2", "Build Response", "Respond"}
    names = {n["name"] for n in wf["nodes"]}
    expect(names - before_respond <= {"Has Set?", "Record Set"}, f"suggest: unexpected nodes on the hot path: {sorted(names - before_respond - {'Has Set?', 'Record Set'})}")


def verify_delete():
    wf = load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")
    q = node(wf, "Delete Chat Memory")["parameters"]["query"]
    expect("delete from public.suggestion_sets" in q.lower() and "lineage_profiles" in q, "delete: Delete Chat Memory must delete suggestion_sets by the bot's line (apply-history-handoff.DELETE_NEW_TAIL)")
    expect("deletedSuggestionSets" in node(wf, "Deletion Settled?")["parameters"]["conditions"]["conditions"][0]["leftValue"], "delete: Deletion Settled? must judge deletedSuggestionSets (apply-auth-gate.DELETION_SETTLED)")
    expect('"deletedSuggestionSets"' in node(wf, "Respond")["parameters"]["responseBody"], "delete: Respond must report deletedSuggestionSets")
    expect(edges(wf, "Respond") == [], "delete: nothing may run after the answer (V12) — no Delete Learning Rows node")


def verify_sweep():
    wf = load("fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json")
    expect("Expire Learning Rows" in edges(wf, "Every 6 Hours"), "sweep: Every 6 Hours → Expire Learning Rows missing")
    n = node(wf, "Expire Learning Rows")
    if n:
        q = n["parameters"]["query"]
        expect("interval '180 days'" in q and "suggestion_sets" in q and "origin_key = null" in q and "origin_kind is not null" in q, "sweep: Expire Learning Rows SQL shape")


def verify_prompts():
    core = open(os.path.join(PROMPTS, "_core.md"), encoding="utf-8").read()
    expect("Границы темы." in core and "строка «Название»" in core and "вы человек или бот" in core and "не называй себя человеком" in core, "prompts: _core.md lacks the three-class rule")
    expect("{businessName}" not in core, "prompts: no placeholder in the shared head — V10 classifies heads by content (deviation 13)")
    universal = open(os.path.join(PROMPTS, "_universal.md"), encoding="utf-8").read()
    expect("вы бот / человек" in universal and "за человека себя не выдавай" in universal and "{businessName}" not in universal, "prompts: _universal.md still forbids the honest answer (or carries a placeholder)")
    asm = node(load("9PTyYcelRQI7bGDb-Suggest_Replies.json"), "Assemble")["parameters"]["jsCode"]
    expect("LEARNING-A-OFFTOPIC-RULE" in asm and "РОВНО ОДНУ карточку" in asm and "label «Не по теме»" in asm, "prompts: Assemble lacks the general off-topic rule")
    expect("кроме ПОСТОРОННЕГО ВОПРОСА" in asm, "prompts: cold start must not re-assert «4 варианта» for an off-topic question")
    mgr = open(os.path.join(os.path.dirname(os.path.dirname(HERE)), "Assets", "Scripts", "Main", "Manager.cs"), encoding="utf-8").read()
    expect('Append("Название: ")' in mgr, "prompts: Manager.ComposeBusinessKnowledge does not write the «Название:» line the rule relies on")


GROUPS = {"proxy": verify_proxy, "suggest": verify_suggest, "delete": verify_delete, "sweep": verify_sweep, "prompts": verify_prompts}


def main():
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    for name, fn in GROUPS.items():
        if only and only != name:
            continue
        fn()
    for f in FAILS:
        print("FAIL", f)
    print("learning A verify:", "OK" if not FAILS else f"{len(FAILS)} problem(s)")
    sys.exit(1 if FAILS else 0)


if __name__ == "__main__":
    main()
```

Run: `python3 Tools/n8n/verify-learning-a.py --only proxy`
Expected: `FAIL proxy: META allow-list missing` … `learning A verify: N problem(s)`, exit 1.

- [ ] **Step 2: Патч `build-auth-workflows.py`**

(а) В `BUILD_REQUEST_JS` перед строкой `const OPS = {` вставить:

```js
// LEARNING-A (2026-09-25): происхождение отправки владельца из приложения. Валидируется как body,
// эмитится в выход как `meta`, НИКОГДА не копируется в jsonBody — Wappi этих полей не видит.
// Все поля опциональны: старый клиент (compat-окно) не шлёт ничего и проходит как раньше.
// Int-правило take: слот 4 — DEFAULT, поэтому «опциональный int без дефолта» = [.., undefined, 'opt'].
const META = { origin_kind: ['origin_kind', /^(card|edited|typed|none)$/, 'opt'],
               origin_index: ['origin_index', 'int', 3, undefined, 'opt'],
               origin_key: ['origin_key', /^[0-9a-f]{1,32}$/, 'opt'],
               origin_edit: ['origin_edit', 'int', 100, undefined, 'opt'],
               origin_top1: ['origin_top1', 'int', 1, undefined, 'opt'],
               origin_set: ['origin_set', 'int', 4, undefined, 'opt'] };
```

(б) Две строки OPS (`message.reply` несёт в meta ещё и `chat_id`: клиент не шлёт `recipient` для TG-ответа, а история и память должны знать чат):

```js
  'message.send':      { m: 'POST', path: 'sync/message/send', q: { profile_id: 'pid' }, body: { recipient: ['recipient', ID], body: ['body', 'text', 8192], quoted_message_id: ['quoted_message_id', ID, 'opt'] }, meta: META },
  'message.reply':     { m: 'POST', path: 'sync/message/reply', q: { profile_id: 'pid' }, body: { recipient: ['recipient', ID, 'opt'], body: ['body', 'text', 8192], message_id: ['message_id', ID] }, only: 'telegram', meta: { ...META, chat_id: ['chat_id', ID, 'opt'] } },
```

(в) После цикла `for (const [k, rule] of Object.entries(spec.body || {})) …` добавить:

```js
const meta = {};
for (const [k, rule] of Object.entries(spec.meta || {})) { const e = take(meta, k, rule); if (e) return deny(400, 'bad_param', e); }
```

(г) В `return [{ json: { ok: true, _status: 200, op, channel, profileId: pid, method: spec.m, url: BASE[channel] + path, query,` добавить `meta,` после `query,` (одна строка: `… url: BASE[channel] + path, query, meta,`).

(д) `RECORD_OWNER_SEND_SQL` — заменить ТОЛЬКО CTE `ev` (CTE `mem` с T6 несёт плейсхолдер `__OWNER_CONTENT__` и трейлинг `.replace("__OWNER_CONTENT__", OWNER_CONTENT_SQL % "$4")` — не трогать):

```sql
with ev as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id,
                                   origin_kind, origin_index, origin_key, origin_edit, origin_top1, origin_set)
  values ($1, $2, $3, 'owner_app', $4, $5, now(), $6,
          $7::text, $8::smallint, $9::text, $10::smallint, $11::smallint, $12::smallint)
  on conflict (profile_id, chat_id, message_id) do update set source = 'owner_app', body = excluded.body, execution_id = excluded.execution_id,
    origin_kind = excluded.origin_kind, origin_index = excluded.origin_index, origin_key = excluded.origin_key,
    origin_edit = excluded.origin_edit, origin_top1 = excluded.origin_top1, origin_set = excluded.origin_set
  returning 1
), mem as (
```
(остальное без изменений).

(е) `OWNER_SEND_PARAMS` — заменить целиком:

```python
OWNER_SEND_PARAMS = """={{ (r => { const b = r.jsonBody || {}; const m = r.meta || {}; const nz = (v) => (v === undefined || v === null || v === '') ? null : v;
  const rec = String(b.recipient || m.chat_id || ''); const chat = (r.channel === 'whatsapp' && rec && !rec.includes('@')) ? rec + '@c.us' : rec;
  const text = r.op === 'media.send' ? ('[' + String(r.kind || 'file') + ']' + (b.caption ? ' ' + String(b.caption) : '')) : String(b.body || '');
  return [ r.profileId, chat, String($('Wappi Call').first().json.body.message_id || ''), text.slice(0, 4000), r.op === 'media.send' ? String(r.kind || 'file') : 'chat', $execution.id,
           nz(m.origin_kind), nz(m.origin_index), nz(m.origin_key), nz(m.origin_edit), nz(m.origin_top1), nz(m.origin_set) ]; })($('Build Request').first().json) }}"""
```

Run: `python3 Tools/n8n/build-auth-workflows.py && python3 Tools/n8n/build-auth-workflows.py --check && python3 Tools/n8n/verify-auth-gate.py && python3 Tools/n8n/verify-learning-a.py --only proxy`
Expected: `wrote crWappiProxy0001-Wappi_Proxy.json (16 nodes)` (и Auth Session), `auth workflows: up to date`, verify-auth-gate без ошибок, `learning A verify: OK`.

- [ ] **Step 3: Staging e2e — создать `Tools/n8n/staging/e2e-learning.py` (сценарии прокси)**

```python
#!/usr/bin/env python3
"""STAGING scenarios for learning A (2026-09-25): origin meta on the proxy, the shown-set journal,
stage, deletion. Wappi = mock, Postgres = staging.

    python3 Tools/n8n/staging/e2e-learning.py
    python3 Tools/n8n/staging/e2e-learning.py --only origin
"""
import argparse
import importlib.util
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from e2e import (Ctx, http, node_output, BASE)  # noqa: E402


def _load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), os.path.join(HERE, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


e2e_auth = _load("e2e-auth")
e2e_suggest = _load("e2e-suggest")
psql, payload, knowledge, m, call, owner_for = (e2e_suggest.psql, e2e_suggest.payload, e2e_suggest.knowledge,
                                                e2e_suggest.m, e2e_suggest.call, e2e_suggest.owner_for)
KEY = "0123456789abcdef0123456789abcdef"
ORIGIN = {"origin_kind": "card", "origin_index": 0, "origin_key": KEY, "origin_edit": 0, "origin_top1": 1, "origin_set": 3}
SCENARIOS = []


def require_migration():
    """Fail LOUDLY when the learning-A migration is missing: Record Set / Record Owner Send are fail-open
    Postgres nodes, so a missing table would silently turn every scenario into a false negative."""
    cols = psql("select count(*) from information_schema.columns where table_name = 'chat_history_events' and column_name like 'origin_%';")
    tbl = psql("select to_regclass('public.suggestion_sets') is not null;")
    if cols != "6" or tbl != "t":
        sys.exit(f"learning-A migration not applied on staging (origin cols={cols}, suggestion_sets={tbl}) — run Tools/n8n/staging/pg.sh migrate")


def owner_and_profile(ctx, status="trialing", channel="whatsapp"):
    """Every Owner registers itself in e2e_auth.OWNERS (R2/V3) — the cleanup below reuses that list."""
    if channel == "telegram":
        owner = e2e_auth.Owner("sg", ctx.run_id)
        pid = owner.add_profile("telegram")
        owner.refresh()
    else:
        owner, pid = owner_for(ctx, status=status)
    return owner, pid


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


def send(owner, pid, extra=None):
    body = {"op": "message.send", "channel": "whatsapp", "profile_id": pid, "recipient": "77010001234", "body": "Здравствуйте! Есть в наличии."}
    body.update(extra or {})
    return e2e_auth.proxy(owner.token, body)


@scenario("origin_recorded_on_tg_reply", "A: Telegram message.reply carries origin + chat_id in meta → owner_app row has origin_* AND chat_id, memory session = pid:chat; Wappi body has neither")
def tg_reply(ctx):
    owner, pid = owner_and_profile(ctx, channel="telegram")
    _, since = e2e_auth.mock_calls(0, "message/reply")
    body = {"op": "message.reply", "channel": "telegram", "profile_id": pid, "chat_id": "123456", "body": "Да, есть.", "message_id": "m1"}
    body.update(ORIGIN)
    st, d = e2e_auth.proxy(owner.token, body)
    mid = (d or {}).get("message_id") or ""
    time.sleep(2)
    row = origin_row(pid, mid)
    chat = psql(f"select chat_id from chat_history_events where profile_id = '{pid}' and message_id = '{mid}';")
    mem = psql(f"select count(*) from n8n_chat_histories where session_id = '{pid}:123456' and message->'additional_kwargs'->>'message_id' = '{mid}';")
    calls, _ = e2e_auth.mock_calls(since, "message/reply")
    bodies = [json.dumps(c.get("body") or {}) for c in calls]
    ok = st == 200 and mid and row == f"card|0|{KEY}|0|1|3" and chat == "123456" and mem == "1" and bodies and all("origin_" not in b and "chat_id" not in b for b in bodies)
    return ok, f"http={st} row={row!r} chat_id={chat!r} memory_rows={mem} forwarded={bodies[:1]}"


def origin_row(pid, mid):
    return psql(f"select coalesce(origin_kind,'∅')||'|'||coalesce(origin_index::text,'∅')||'|'||coalesce(origin_key,'∅')||'|'||coalesce(origin_edit::text,'∅')||'|'||coalesce(origin_top1::text,'∅')||'|'||coalesce(origin_set::text,'∅') "
                f"from chat_history_events where profile_id = '{pid}' and message_id = '{mid}';")


@scenario("origin_recorded_on_app_send", "A: message.send with origin meta → owner_app row carries the six origin_* columns")
def origin_recorded(ctx):
    owner, pid = owner_and_profile(ctx)
    st, d = send(owner, pid, ORIGIN)
    mid = (d or {}).get("message_id") or ""
    time.sleep(2)
    row = origin_row(pid, mid)
    ok = st == 200 and mid and row == f"card|0|{KEY}|0|1|3"
    return ok, f"http={st} message_id={mid!r} row={row!r} (want card|0|{KEY}|0|1|3)"


@scenario("origin_absent_is_legacy_ok", "A: message.send WITHOUT origin (compat client) → 200 and all origin_* null")
def origin_absent(ctx):
    owner, pid = owner_and_profile(ctx)
    st, d = send(owner, pid)
    mid = (d or {}).get("message_id") or ""
    time.sleep(2)
    row = origin_row(pid, mid)
    ok = st == 200 and mid and row == "∅|∅|∅|∅|∅|∅"
    return ok, f"http={st} row={row!r}"


@scenario("origin_bogus_is_400_no_transport", "A: origin_kind outside the enum → 400 bad_param and NO Wappi call")
def origin_bogus(ctx):
    owner, pid = owner_and_profile(ctx)
    _, total0 = e2e_auth.mock_calls(0, "message/send")
    st, d = send(owner, pid, {"origin_kind": "bogus"})
    _, total1 = e2e_auth.mock_calls(0, "message/send")
    ok = st == 400 and (d or {}).get("error") == "bad_param" and total1 == total0
    return ok, f"http={st} body={d} mock_sends {total0}->{total1}"


@scenario("wappi_never_sees_origin", "A: the transport body forwarded to Wappi has no origin_* keys")
def wappi_clean(ctx):
    owner, pid = owner_and_profile(ctx)
    _, since = e2e_auth.mock_calls(0, "message/send")
    st, _ = send(owner, pid, ORIGIN)
    calls, _ = e2e_auth.mock_calls(since, "message/send")
    bodies = [json.dumps(c.get("body") or {}) for c in calls]
    ok = st == 200 and bodies and all("origin_" not in b for b in bodies)
    return ok, f"http={st} forwarded_bodies={bodies[:1]}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:44} {title}")
        return
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    require_migration()
    ctx = Ctx()
    passed = failed = 0
    try:
        for id_, title, fn in SCENARIOS:
            if a.only and a.only not in id_:
                continue
            t0 = time.time()
            try:
                ok, detail = fn(ctx)
            except Exception as e:
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok
            failed += (not ok)
            print(f"{'PASS' if ok else 'FAIL'}  {id_} ({time.time() - t0:.0f}s) — {title}", flush=True)
            print(f"      {detail}", flush=True)
    finally:
        ctx.cleanup()
        for o in e2e_auth.OWNERS:   # identities made through e2e_auth.Owner are invisible to Ctx.cleanup() (same loop as e2e-auth.py)
            try:
                print(f"   cleanup {o.tag}:", e2e_auth.fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for pid in o.profiles for x in ("--profile-id", pid)])))
            except Exception as e:
                print(f"   cleanup {o.tag} failed:", e)
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
```

В `Tools/n8n/staging/wappi-mock.py`, `_transport`, после ветки `if tail == "message/send": … return 200, {...}` добавить маршрут ответа-цитаты (Telegram): чат берётся из `recipient`, иначе ищется по `message_id` цитируемого сообщения, иначе пусто — как и у реального транспорта, история чата получает echo-строку:

```python
            if tail == "message/reply":
                recipient = b.get("recipient") or ""
                quoted = b.get("message_id")
                chat_id = recipient
                if not chat_id and quoted:
                    for key, rows in STATE["messages"].items():
                        if any(m.get("id") == quoted for m in rows):
                            chat_id = key.split(":", 1)[1] if ":" in key else key
                            break
                mid = _new_id("sent")
                row = {"id": mid, "chatId": chat_id, "from": chat_id, "fromMe": True, "type": "chat",
                       "body": b.get("body", ""), "time": _now(), "senderName": "mock-bot", "quoted_message_id": quoted,
                       "from_where": "api", "task_id": uuid.uuid4().hex, "isFromAPI": True}   # the API marks, as message/send (V7)
                if isinstance(echo, dict):
                    row.update(echo)
                if chat_id:
                    STATE["messages"].setdefault(_key(pid, chat_id), []).append(row)
                return 200, {"status": "done", "message_id": row["id"], "time": row["time"]}
```
(Форма `_key(pid, chat_id)`, `STATE["messages"]`, метки `from_where/task_id/isFromAPI` и слияние `echo` — те же, что у `message/send` строкой выше (V7); в docstring маршрутов добавить строку `POST message/reply?profile_id {body,message_id[,recipient]} -> {status:"done", message_id, time}`.)

Run:
```bash
python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwab77f282533c
python3 Tools/n8n/staging/bootstrap.py --restart
python3 Tools/n8n/staging/e2e-learning.py --only origin
python3 Tools/n8n/staging/e2e-learning.py --only wappi
python3 Tools/n8n/staging/e2e-auth.py
```
Expected: `origin_recorded_on_app_send`, `origin_absent_is_legacy_ok`, `origin_bogus_is_400_no_transport`, `origin_recorded_on_tg_reply` PASS; `wappi_never_sees_origin` PASS; `e2e-auth.py` без регрессий. Если `require_migration` останавливает прогон — сначала Task 3 Step 2.

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/build-auth-workflows.py Tools/n8n/workflows/crWappiProxy0001-Wappi_Proxy.json Tools/n8n/workflows/crAuthSession001-Auth_Session.json Tools/n8n/verify-learning-a.py Tools/n8n/staging/e2e-learning.py Tools/n8n/staging/wappi-mock.py
git commit -m "feat(n8n): Wappi Proxy — секция meta (origin_*, chat_id для TG-ответа) в allow-list, Record Owner Send пишет происхождение отправки

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Suggest Replies — `Has Set?` + `Record Set` после `Respond`

**Files:**
- Modify: `Tools/n8n/apply-learning-a.py` (функции `patch_suggest_nodes`, `check_suggest_nodes`, регистрация в `patch_all`/`check_all`)
- Regenerate: `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`
- Modify: `Tools/n8n/staging/e2e-learning.py` (+3 сценария)

**Interfaces:**
- Consumes: `stage` из `Gate Decision` (Task 2), таблица из Task 3, `PROMPT_VERSION`.
- Produces: строка `suggestion_sets` на каждый успешный непустой ответ панели.

- [ ] **Step 1: Сценарии (падают до патча) — добавить в `e2e-learning.py` перед `def main`**

```python
@scenario("set_recorded_after_respond", "A: a successful panel answer writes ONE suggestion_sets row (after Respond, same request_key, stage learning)")
def set_recorded(ctx):
    owner, pid = owner_and_profile(ctx)
    know = knowledge([{"kind": "product", "name": "Свеча зажигания", "price": "1500 тг", "description": ""}])
    body = payload(owner, pid, [m("client", "свечи есть? сколько стоят?")], know=know, request_key=KEY, bot_wa="-1")
    st, d, ex = call(owner, body)
    cards = (d or {}).get("suggestions") or []
    time.sleep(2)
    row = psql(f"select stage||'|'||jsonb_array_length(items)||'|'||coalesce(prompt_version,'∅')||'|'||(usage is not null)::text from suggestion_sets where profile_id = '{pid}' and request_key = '{KEY}';")
    gd = node_output(ex, "Gate Decision") or {}
    ok = st == 200 and cards and row == f"learning|{len(cards)}|A0-2026-09-25|true" and gd.get("stage") == "learning"
    return ok, f"http={st} cards={len(cards)} row={row!r} gate_stage={gd.get('stage')!r}"


@scenario("set_not_recorded_on_refusal", "A: a refused call (expired subscriber → error envelope) writes NO set")
def set_not_recorded(ctx):
    owner, pid = owner_and_profile(ctx, status="expired")
    body = payload(owner, pid, [m("client", "свечи есть?")], know=knowledge([]), request_key="fedcba9876543210fedcba9876543210", bot_wa="-1")
    st, d, _ = call(owner, body)
    time.sleep(2)
    n = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    ok = (d or {}).get("error") and n == "0"
    return ok, f"http={st} error={(d or {}).get('error')!r} sets={n}"


@scenario("set_not_recorded_on_abstain", "A: an abstain (personal message) writes NO set — inconclusive (not FAIL) when the model produced cards anyway")
def set_not_recorded_abstain(ctx):
    owner, pid = owner_and_profile(ctx)
    body = payload(owner, pid, [m("client", "поехали на рыбалку в выходные?")], know=knowledge([]), request_key="00000000000000000000000000000003", bot_wa="-1")
    st, d, _ = call(owner, body)
    time.sleep(2)
    n = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    if (d or {}).get("suggestions"):
        return True, f"INCONCLUSIVE: model returned {len(d['suggestions'])} cards (abstain flaky) — set written={n}"
    ok = st == 200 and (d or {}).get("abstain") is True and n == "0"
    return ok, f"http={st} abstain={(d or {}).get('abstain')} sets={n}"


@scenario("stage_trained_when_ten_picks", "A: pickStats summing to ≥ 10 → Gate Decision stage 'trained' and Assemble emits ПРЕДПОЧТЕНИЯ, not ХОЛОДНЫЙ СТАРТ")
def stage_trained(ctx):
    owner, pid = owner_and_profile(ctx)
    body = payload(owner, pid, [m("client", "свечи есть? сколько стоят?")], know=knowledge([{"kind": "product", "name": "Свеча зажигания", "price": "1500 тг", "description": ""}]), request_key="00000000000000000000000000000001", bot_wa="-1")
    body["pickStats"] = "Ответ:7,Уточнить:4"
    st, _, ex = call(owner, body)
    gd = node_output(ex, "Gate Decision") or {}
    sp = (node_output(ex, "Assemble") or {}).get("systemPrompt", "")
    ok = st == 200 and gd.get("stage") == "trained" and "ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА" in sp and "ХОЛОДНЫЙ СТАРТ" not in sp
    return ok, f"http={st} stage={gd.get('stage')!r} prefs={'ПРЕДПОЧТЕНИЯ ВЛАДЕЛЬЦА' in sp} cold={'ХОЛОДНЫЙ СТАРТ' in sp}"
```

Run: `python3 Tools/n8n/staging/e2e-learning.py --only set_`
Expected: оба `set_*` FAIL (таблица пуста — узла ещё нет).

- [ ] **Step 2: Узлы в патчере — добавить в `apply-learning-a.py`**

После `check_suggest_prompts` вставить:

```python
RECORD_SET_SQL = """-- Learning A: журнал показанных наборов панели «Вместе» — знаменатель теневого счёта. Пишется ПОСЛЕ
-- Respond (responseNode), fail-open; повтор с тем же request_key (retry после таймаута) — не второй набор.
insert into suggestion_sets (request_key, profile_id, chat_id, tail_ts, business_type_id, stage, items, prompt_version, usage)
select $1, $2, $3, $4::bigint, $5, $6, $7::jsonb, $8, $9::jsonb
where $1 <> '' and $2 <> ''            -- a legacy client without requestKey writes nothing (never a '' primary key)
on conflict (profile_id, request_key) do nothing
returning 1;"""
RECORD_SET_PARAMS = ("={{ (g => { const msgs = Array.isArray(g.messages) ? g.messages : []; const last = msgs.length ? msgs[msgs.length - 1] : null;"
                     " const usage = (() => { try { const r = $('LLM Retry').first().json; if (r && r.usage) return r.usage; } catch (e) {}"
                     " try { return $('LLM').first().json.usage || null; } catch (e) { return null; } })();"
                     " const items = ($json.suggestions || []).slice(0, 4).map(s => ({ label: String(s.label || ''), move: String(s.move || ''), text: String(s.text || '').slice(0, 300) }));"
                     " return [ String(g.requestKey || ''), String(g.profileId || ''), String(g.chatId || ''), last && last.ts ? Number(last.ts) : null,"
                     " String(g.businessTypeId || ''), String(g.stage || 'trained'), JSON.stringify(items), '" + PROMPT_VERSION + "', usage ? JSON.stringify(usage) : null ]; })($('Gate Decision').first().json) }}")
# Has Set? must NOT reference $('Gate Decision'): on the `If invalid?` path that node never ran and the reference
# would throw inside the If (after the response, but as an errored execution). Record Set DOES read Gate Decision —
# it is safe only because Has Set? filters invalid / refused / abstain (empty suggestions) first. Never weaken this.
HAS_SET_EXPR = "={{ Array.isArray($json.suggestions) && $json.suggestions.length > 0 && !$json.abstain && !$json.error }}"


def patch_suggest_nodes(wf):
    ch = False
    if node(wf, "Has Set?") is None:
        wf["nodes"].append({"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                          "conditions": [{"id": nid("has-set", "cond"), "leftValue": HAS_SET_EXPR, "rightValue": "",
                                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}], "combinator": "and"}, "options": {}},
                            "id": nid("has-set"), "name": "Has Set?", "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": [2860, 300], "executeOnce": True})
        ch = True
    if node(wf, "Record Set") is None:
        wf["nodes"].append({"parameters": {"operation": "executeQuery", "query": RECORD_SET_SQL, "options": {"queryReplacement": RECORD_SET_PARAMS}},
                            "id": nid("record-set"), "name": "Record Set", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": [3080, 300],
                            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput", "executeOnce": True})
        ch = True
    rs = node(wf, "Record Set")
    if rs["parameters"]["query"] != RECORD_SET_SQL or rs["parameters"]["options"].get("queryReplacement") != RECORD_SET_PARAMS:
        rs["parameters"]["query"] = RECORD_SET_SQL
        rs["parameters"]["options"]["queryReplacement"] = RECORD_SET_PARAMS
        ch = True
    hs = node(wf, "Has Set?")
    if hs["parameters"]["conditions"]["conditions"][0]["leftValue"] != HAS_SET_EXPR:
        hs["parameters"]["conditions"]["conditions"][0]["leftValue"] = HAS_SET_EXPR
        ch = True
    ch |= set_edges(wf, "Respond", [["Has Set?"]])
    ch |= set_edges(wf, "Has Set?", [["Record Set"], []])
    return ch


def check_suggest_nodes(wf):
    rs, hs = node(wf, "Record Set"), node(wf, "Has Set?")
    return (rs is not None and hs is not None and rs["parameters"]["query"] == RECORD_SET_SQL
            and rs["parameters"]["options"].get("queryReplacement") == RECORD_SET_PARAMS
            and wf["connections"].get("Respond") == {"main": [[{"node": "Has Set?", "type": "main", "index": 0}]]}
            and wf["connections"].get("Has Set?") == {"main": [[{"node": "Record Set", "type": "main", "index": 0}], []]})
```

и переписать `patch_all`/`check_all`:

```python
def patch_all():
    out = {}
    wf = load(SUGGEST)
    ch = patch_suggest_prompts(wf) | patch_suggest_nodes(wf)
    if ch:
        save(SUGGEST, wf)
    out[SUGGEST] = ch
    return out


def check_all():
    wf = load(SUGGEST)
    return {SUGGEST: check_suggest_prompts(wf) and check_suggest_nodes(wf)}
```

Run: `python3 Tools/n8n/apply-learning-a.py && python3 Tools/n8n/apply-learning-a.py --check && python3 Tools/n8n/verify-learning-a.py --only suggest && python3 Tools/n8n/apply-suggest-v2.py --check`
Expected: `patched`, `learning A: OK`, `learning A verify: OK`, `suggest v2: OK`.

- [ ] **Step 3: Staging**

Run:
```bash
python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwab77f282533c
python3 Tools/n8n/staging/bootstrap.py --restart
python3 Tools/n8n/staging/e2e-learning.py --only set_
python3 Tools/n8n/staging/e2e-learning.py --only stage_
python3 Tools/n8n/staging/e2e-suggest.py
```
Expected: `set_recorded_after_respond` PASS, `set_not_recorded_on_refusal` PASS, `stage_trained_when_ten_picks` PASS, `e2e-suggest.py` без регрессий (11 сценариев на 2026-10-13) (латентность `deadline` без изменений — узел после ответа).

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/apply-learning-a.py Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json Tools/n8n/staging/e2e-learning.py
git commit -m "feat(n8n): Suggest Replies — журнал показанных наборов (Has Set? → Record Set после Respond)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Удаление с ботом (CTE в `Delete Chat Memory`) + retention 180 дней

**Почему не отдельный узел после `Respond` (перепроверка 2026-10-13):** `verify-auth-gate.py` (V12) пинит `Delete Chat Memory → Deletion Settled? → Respond` и `edges(Respond) == []` — «после ответа ничего не выполняется», а `success:true` даётся только когда КАЖДЫЙ счётчик удаления — число. Узел `Delete Learning Rows` после `Respond` валит этот верификатор (проверено dry-run'ом патчера на копии канона). Поэтому наборы панели удаляются ТЕМ ЖЕ statement'ом, что и остальная память бота: CTE `sets` в `DELETE_NEW_TAIL` (`apply-history-handoff.py` — владелец `Delete Chat Memory`), счётчик `deletedSuggestionSets` в `Deletion Settled?` (`apply-auth-gate.py` — владелец вердикта) и в теле `Respond`. Ноль новых узлов, ноль добавленной латентности, линия бота — та же `lineage_profiles` (T5). Следствие для деплоя: без миграции (таблицы `suggestion_sets`) `Delete Chat Memory` падает → `Deletion Settled?` false → 503 → клиентский Retry, бот остаётся на телефоне и ничего не теряется — миграция идёт ПЕРЕД Delete Bot Files (как M2 перед T5).

**Files:**
- Modify: `Tools/n8n/apply-history-handoff.py` — `DELETE_NEW_TAIL` (+CTE `sets`, +счётчик; прежний хвост остаётся как `DELETE_T5_TAIL` для распознавания формы), `RESPOND_NEW_TAIL` (+`deletedSuggestionSets`; прежний — `RESPOND_T5_TAIL`), `patch_delete_bot`
- Modify: `Tools/n8n/apply-auth-gate.py` — кортеж счётчиков в `DELETION_SETTLED` (+`"deletedSuggestionSets"`)
- Modify: `Tools/n8n/apply-learning-a.py` (`EXPIRE_SQL`, `patch_sweep`/`check_sweep`, `patch_all`/`check_all` += SWEEP; узла для Delete Bot Files НЕТ)
- Modify: `Tools/n8n/tests/test_retired_delete.py` — `'suggestion_sets'` в `SETUP`, `'deletedSuggestionSets'` в кортеже счётчиков кейса `Deletion Settled?`
- Modify: `Tools/n8n/verify-learning-a.py` — `verify_delete` (см. ниже)
- Create: `Tools/n8n/tests/test_learning_a.py`
- Regenerate: `Tools/n8n/workflows/lmjYsdNcQA2IE5rl-Delete_Bot_Files.json` (через `apply-history-handoff.py` + `apply-auth-gate.py`), `Tools/n8n/workflows/fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json` (через `apply-learning-a.py`)
- Modify: `Tools/n8n/staging/e2e-learning.py` (+1 сценарий)

**Interfaces:**
- Consumes: `suggestion_sets`, `origin_*`, `clone_lineage.lineage_cte` (через `DBF_LINE_HEAD`, уже в `Delete Chat Memory`).
- Produces: `Delete Chat Memory` возвращает ещё `deletedSuggestionSets`; `Deletion Settled?` требует его числом; `Respond` отдаёт его клиенту (клиент читает только `success:true` — C1, лишний ключ безвреден); `Sweep: Every 6 Hours → Expire Learning Rows` (вторая ветка рядом с `Sweep Config`, dryRun не подчиняется).

- [ ] **Step 1: Сценарий (падает до патча)**

```python
@scenario("delete_bot_clears_sets", "A: DeleteBotFiles for the profile removes its suggestion_sets rows (history rows go with chat_history_events)")
def delete_clears(ctx):
    owner, pid = owner_and_profile(ctx)
    body = payload(owner, pid, [m("client", "свечи есть? сколько стоят?")], know=knowledge([{"kind": "product", "name": "Свеча", "price": "1500 тг", "description": ""}]), request_key=KEY)
    call(owner, body)
    time.sleep(2)
    n0 = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    st, d = e2e_auth.hook("DeleteBotFiles", owner.token, {"botWaId": "-1", "botTgId": "-1", "waProfileId": pid, "tgProfileId": "-1", "appUserId": owner.app_user_id})
    time.sleep(3)
    n1 = psql(f"select count(*) from suggestion_sets where profile_id = '{pid}';")
    ok = n0 == "1" and st == 200 and (d or {}).get("success") is True and (d or {}).get("deletedSuggestionSets") == 1 and n1 == "0"
    return ok, f"sets before/after = {n0}/{n1}, delete http={st} body={d}"
```

Run: `python3 Tools/n8n/staging/e2e-learning.py --only delete_`
Expected: FAIL (`n1 == "1"`, в ответе нет `deletedSuggestionSets`).

- [ ] **Step 2: Генераторы — CTE `sets` в Delete Chat Memory, счётчик в вердикте, retention в патчере**

(а) `Tools/n8n/apply-history-handoff.py`. Сразу ПОСЛЕ существующего определения `DELETE_NEW_TAIL = """), flags AS ( … "deletedChatStates";"""` (и ДО `DELETE_CHAT_MEMORY_SQL = …`, который его конкатенирует) добавить:

```python
# Learning A (2026-10-13): the «Вместе» panel's shown sets (suggestion_sets) are the bot's data too — deleted by the SAME
# statement and judged by the SAME verdict (V12: nothing runs after the answer; success only when every counter is a
# number). The T5 shape stays recognisable so the patcher can bring a T5 canon forward.
DELETE_T5_TAIL = DELETE_NEW_TAIL
DELETE_NEW_TAIL = DELETE_T5_TAIL.replace(
    ")\nSELECT (SELECT count(*)::int FROM hist)        AS \"deletedMemoryRows\",",
    "), sets AS (          -- learning A: the panel's shown sets of every profile of the line\n"
    "  DELETE FROM public.suggestion_sets s\n"
    "  WHERE s.profile_id IN (SELECT profile_id FROM valid)\n"
    "  RETURNING 1\n"
    ")\nSELECT (SELECT count(*)::int FROM hist)        AS \"deletedMemoryRows\",", 1).replace(
    "       (SELECT count(*)::int FROM states)      AS \"deletedChatStates\";",
    "       (SELECT count(*)::int FROM states)      AS \"deletedChatStates\",\n"
    "       (SELECT count(*)::int FROM sets)        AS \"deletedSuggestionSets\";", 1)
assert DELETE_NEW_TAIL != DELETE_T5_TAIL and DELETE_NEW_TAIL.count("suggestion_sets") == 1, "learning A: the sets CTE did not land"
```

В `patch_delete_bot` строку `assert DELETE_OLD_TAIL in q or DELETE_NEW_TAIL in q, "Delete Chat Memory has an unexpected shape"` заменить на `assert DELETE_OLD_TAIL in q or DELETE_T5_TAIL in q or DELETE_NEW_TAIL in q, "Delete Chat Memory has an unexpected shape"`.

Сразу ПОСЛЕ существующего `RESPOND_NEW_TAIL = (…'"deletedChatStates": $json.deletedChatStates ?? null } }}')` добавить:

```python
RESPOND_T5_TAIL = RESPOND_NEW_TAIL
RESPOND_NEW_TAIL = RESPOND_T5_TAIL.replace('"deletedChatStates": $json.deletedChatStates ?? null } }}',
                                           '"deletedChatStates": $json.deletedChatStates ?? null, "deletedSuggestionSets": $json.deletedSuggestionSets ?? null } }}', 1)
assert RESPOND_NEW_TAIL != RESPOND_T5_TAIL
```

и в `patch_delete_bot` блок

```python
    if RESPOND_OLD_TAIL in body:
        r["parameters"]["responseBody"] = body.replace(RESPOND_OLD_TAIL, RESPOND_NEW_TAIL)
        ch = True
```

заменить на

```python
    for old in (RESPOND_OLD_TAIL, RESPOND_T5_TAIL):   # the pre-T5 and the T5 answer shapes both come forward
        if old in body and RESPOND_NEW_TAIL not in body:
            body = body.replace(old, RESPOND_NEW_TAIL, 1)
            r["parameters"]["responseBody"] = body
            ch = True
```

(`check()` уже требует `RESPOND_NEW_TAIL in body` и `query == DELETE_CHAT_MEMORY_SQL` — менять не нужно.)

(б) `Tools/n8n/apply-auth-gate.py`, константа `DELETION_SETTLED`: в кортеж `("deletedMemoryRows", "deletedOutcomes", "deletedReplyFlags", "deletedHistoryEvents", "deletedHandoffs", "deletedReplyEvents", "deletedChatStates")` добавить последним `"deletedSuggestionSets"`. `patch_delete_bot_files` переписывает `leftValue` узла через `ensure_node(... if_spec(S, "Deletion Settled?", DELETION_SETTLED, …))` — идемпотентно.

(в) `Tools/n8n/apply-learning-a.py` — добавить (после `check_suggest_nodes`, перед `patch_all`):

```python
EXPIRE_SQL = """-- Learning A retention (180 дней): наборы панели и метки происхождения на строках истории.
-- Строки chat_history_events НЕ удаляются — только их origin_* обнуляются (по origin_kind, чтобы и «none» истекал).
-- Ветка висит прямо на триггере и НЕ подчиняется Sweep Config.dryRun: удаление по возрасту, не по профилю.
with sets as (delete from public.suggestion_sets where created_at < now() - interval '180 days' returning 1),
     origins as (update public.chat_history_events
                   set origin_kind = null, origin_index = null, origin_key = null, origin_edit = null, origin_top1 = null, origin_set = null
                 where origin_kind is not null and created_at < now() - interval '180 days' returning 1)
select (select count(*)::int from sets) as expired_sets, (select count(*)::int from origins) as expired_origins;"""


def patch_sweep(wf):
    ch = False
    if node(wf, "Expire Learning Rows") is None:
        trig = node(wf, "Every 6 Hours")
        wf["nodes"].append({"parameters": {"operation": "executeQuery", "query": EXPIRE_SQL, "options": {}},
                            "id": nid("expire-learning"), "name": "Expire Learning Rows", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                            "position": [trig["position"][0] + 220, trig["position"][1] + 400], "credentials": {"postgres": dict(PG_CRED)}, "onError": "continueRegularOutput"})
        ch = True
    n = node(wf, "Expire Learning Rows")
    if n["parameters"]["query"] != EXPIRE_SQL:
        n["parameters"]["query"] = EXPIRE_SQL
        ch = True
    outs = wf["connections"].get("Every 6 Hours", {}).get("main", [[]])
    if not any(e["node"] == "Expire Learning Rows" for e in outs[0]):
        outs[0].append({"node": "Expire Learning Rows", "type": "main", "index": 0})
        wf["connections"]["Every 6 Hours"] = {"main": outs}
        ch = True
    return ch


def check_sweep(wf):
    n = node(wf, "Expire Learning Rows")
    return n is not None and n["parameters"]["query"] == EXPIRE_SQL and any(e["node"] == "Expire Learning Rows" for e in wf["connections"].get("Every 6 Hours", {}).get("main", [[]])[0])
```

и расширить `patch_all`/`check_all` (константа `SWEEP` уже в шапке файла):

```python
def patch_all():
    out = {}
    wf = load(SUGGEST)
    ch = patch_suggest_prompts(wf) | patch_suggest_nodes(wf)
    if ch:
        save(SUGGEST, wf)
    out[SUGGEST] = ch
    wf = load(SWEEP)
    ch = patch_sweep(wf)
    if ch:
        save(SWEEP, wf)
    out[SWEEP] = ch
    return out


def check_all():
    return {SUGGEST: check_suggest_prompts(load(SUGGEST)) and check_suggest_nodes(load(SUGGEST)),
            SWEEP: check_sweep(load(SWEEP))}
```

(г) `Tools/n8n/tests/test_retired_delete.py`: в `SETUP` (кортеж таблиц) добавить `'suggestion_sets'`; в кейсе про `Deletion Settled?` (строка `cond(DBF, 'Deletion Settled?', {**{k: 0 for k in ('deletedMemoryRows', …, 'deletedChatStates')}}, …)`) добавить в кортеж `'deletedSuggestionSets'` — иначе условие читает `typeof undefined` и кейс «ошибочный item никогда не success» перестанет различать причину.

(д) `Tools/n8n/verify-learning-a.py` — `verify_delete` целиком:

```python
def verify_delete():
    wf = load("lmjYsdNcQA2IE5rl-Delete_Bot_Files.json")
    q = node(wf, "Delete Chat Memory")["parameters"]["query"]
    expect("delete from public.suggestion_sets" in q.lower() and "lineage_profiles" in q, "delete: Delete Chat Memory must delete suggestion_sets by the bot's line (apply-history-handoff.DELETE_NEW_TAIL)")
    expect("deletedSuggestionSets" in node(wf, "Deletion Settled?")["parameters"]["conditions"]["conditions"][0]["leftValue"], "delete: Deletion Settled? must judge deletedSuggestionSets (apply-auth-gate.DELETION_SETTLED)")
    expect('"deletedSuggestionSets"' in node(wf, "Respond")["parameters"]["responseBody"], "delete: Respond must report deletedSuggestionSets")
    expect(edges(wf, "Respond") == [], "delete: nothing may run after the answer (V12) — no Delete Learning Rows node")
```

Run:
```bash
python3 Tools/n8n/apply-history-handoff.py && python3 Tools/n8n/apply-auth-gate.py && python3 Tools/n8n/apply-learning-a.py
python3 Tools/n8n/apply-history-handoff.py --check && python3 Tools/n8n/apply-auth-gate.py --check && python3 Tools/n8n/apply-learning-a.py --check
python3 Tools/n8n/verify-auth-gate.py && python3 Tools/n8n/verify-history-handoff.py && python3 Tools/n8n/verify-sql-comments.py && python3 Tools/n8n/verify-clone-lifecycle.py && python3 Tools/n8n/verify-learning-a.py
```
Expected: `Delete_Bot_Files.json` и `Profile_Lifecycle_Sweep.json` изменены (Delete Bot Files — ТОЛЬКО `Delete Chat Memory.query`, `Deletion Settled?.leftValue`, `Respond.responseBody`; узлов/рёбер не прибавилось — `git diff --stat` + `jq '.nodes | length'` до/после одинаковы), три `--check` → OK, `verify-auth-gate` → `ALL AUTH GATE ASSERTS PASSED` (V12 «nothing runs after the answer» и `check_delete_line` проходят — `Deletion Settled?` читается из обновлённого `apply-auth-gate.DELETION_SETTLED`), `verify-history-handoff` OK, `verify-sql-comments` без находок (в новом CTE нет `$N` в комментарии), `learning A verify: OK` (`delete` и `sweep` зелёные; `proxy`/`prompts` — после Task 4 / Task 1).

- [ ] **Step 2b: Тест SQL в харнессе (`Tools/n8n/tests/test_learning_a.py`, конвенция репо: реальный SQL на временных таблицах стенда, без n8n)**

```python
#!/usr/bin/env python3
"""Learning A (2026-10-13): the SQL of the learning journal, run as real SQL on session temp tables of the staging Postgres
(tests/harness.py) — Record Set (insert / dedupe / empty key), Expire Learning Rows (age), and the sets CTE of Delete Chat
Memory (the bot's LINE: a superseded sibling goes, a neighbour of another bot key stays; legacy = the named ids only).
Needs `Tools/n8n/staging/pg.sh migrate` (the public tables are the templates of the temp ones)."""
import importlib.util
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from harness import Case, PgSession, bind, node, report, wf  # noqa: E402

N8N = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("apply_learning_a", N8N / "apply-learning-a.py")
LA = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LA)

# the tables Delete Chat Memory touches (as tests/test_retired_delete.py) + the learning journal
LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = ''.join(LIKE.format(t) for t in ('installs', 'bot_profiles', 'n8n_chat_histories', 'chat_history_events', 'handoff_requests',
                                         'reply_events', 'chat_reply_state', 'conversation_outcomes', 'reply_mode_flags', 'suggestion_sets'))
INST = 'inst-learning-a'
DBF = wf('lmjYsdNcQA2IE5rl-Delete_Bot_Files.json')
SUGGEST = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')   # the temp tables shadow the UNqualified names only


def seed(pg):
    # the seeders of tests/test_retired_delete.py: installs (install_id, secret_hash); bot_profiles needs app_user_id + channel
    pg.x(f"insert into installs (install_id, secret_hash) values ('{INST}', 'h') on conflict do nothing;")
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, bot_key) values "
         f"('p-old', 'u', 'whatsapp', '{INST}', 'wOld00000001', 'k1'), ('p-new', 'u', 'whatsapp', '{INST}', 'wNew00000001', 'k1'), "
         f"('p-other', 'u', 'whatsapp', '{INST}', 'wOth00000001', 'k2');")
    pg.x("update bot_profiles set superseded_by = 'p-new', superseded_at = now(), deleted_at = now(), deleted_reason = 'replaced' where profile_id = 'p-old';")
    for pid in ('p-old', 'p-new', 'p-other'):
        pg.x(f"insert into suggestion_sets (request_key, profile_id, chat_id, items, stage) values ('k-{pid}', '{pid}', 'c', '[]'::jsonb, 'learning');")


def line_args(mode, named):
    return [mode, INST if mode == 'session' else '', json.dumps([named, None]), json.dumps([None, None])]


with PgSession(SETUP) as pg:
    with Case('Record Set: insert, dedupe on the same key, empty key writes nothing') as c:
        sql = Q(SUGGEST, 'Record Set')
        args = ['0123456789abcdef', 'p-rs', 'c', 1754800000, 'auto_parts', 'learning', json.dumps([{"label": "Цена", "move": "Ответ", "text": "3200 тг"}]), LA.PROMPT_VERSION, None]
        c.check(len(pg.q(bind(sql, args))) == 1, 'first insert returns one row')
        c.check(pg.q(bind(sql, args)) == [], 'the same (profile, request_key) again returns nothing (on conflict do nothing)')
        c.check(pg.q("select count(*)::int as n from suggestion_sets where profile_id = 'p-rs'")[0]['n'] == 1, 'exactly one set per request key')
        pg.q(bind(sql, ['', 'p-rs', 'c', None, 'auto_parts', 'learning', '[]', LA.PROMPT_VERSION, None]))
        c.check(pg.q("select count(*)::int as n from suggestion_sets where request_key = ''")[0]['n'] == 0, 'an empty request key is never a primary key')
    with Case('Expire Learning Rows: a 181-day set and origin go, a young one stays') as c:
        pg.x("insert into suggestion_sets (request_key, profile_id, chat_id, items, created_at) values ('old', 'p-exp', 'c', '[]', now() - interval '181 days'), ('young', 'p-exp', 'c', '[]', now());")
        pg.x("insert into chat_history_events (profile_id, chat_id, message_id, source, body, origin_kind, origin_key, created_at) values ('p-exp', 'c', 'm1', 'owner_app', 't', 'none', null, now() - interval '181 days');")
        r = pg.q(LA.EXPIRE_SQL.replace('public.', ''))[0]
        c.check(r['expired_sets'] == 1 and r['expired_origins'] == 1, f'counts {r}')
        c.check(pg.q("select request_key from suggestion_sets where profile_id = 'p-exp'") == [{'request_key': 'young'}], 'young set kept')
        c.check(pg.q("select origin_kind from chat_history_events where message_id = 'm1'")[0]['origin_kind'] is None, 'origin_kind nulled (none expires too)')
    with Case('Delete Chat Memory (session): the sets of the line go — named + superseded sibling — the neighbour stays') as c:
        seed(pg)
        r = pg.q(bind(Q(DBF, 'Delete Chat Memory'), line_args('session', 'p-new')))[0]
        c.check(r['deletedSuggestionSets'] == 2, f'line = p-new + superseded p-old: {r}')
        c.check(all(isinstance(r[k], int) for k in r), f'every counter a number (Deletion Settled? reads typeof === number): {r}')
        c.check(pg.q("select count(*)::int as n from suggestion_sets where profile_id in ('p-old', 'p-new')")[0]['n'] == 0, 'both rows of the line gone')
        c.check(pg.q("select count(*)::int as n from suggestion_sets where profile_id = 'p-other'")[0]['n'] == 1, 'p-other (another bot key, same install) untouched')
    with Case('Delete Chat Memory (legacy): the named ids only') as c:
        pg.x("delete from suggestion_sets where profile_id like 'p-%'; delete from bot_profiles where profile_id like 'p-%';"); seed(pg)
        r = pg.q(bind(Q(DBF, 'Delete Chat Memory'), line_args('legacy', 'p-new')))[0]
        c.check(r['deletedSuggestionSets'] == 1 and pg.q("select count(*)::int as n from suggestion_sets where profile_id = 'p-old'")[0]['n'] == 1, f'legacy: only p-new: {r}')
report()
```

Run: `Tools/n8n/staging/pg.sh migrate && python3 Tools/n8n/tests/test_learning_a.py && python3 Tools/n8n/tests/test_retired_delete.py && python3 Tools/n8n/tests/run_all.py`
Expected: `4 pass, 0 fail`; `test_retired_delete.py` зелёный с расширенным кортежем счётчиков; `run_all.py` — все файлы OK. Сидеры повторяют `tests/test_retired_delete.py` (`installs (install_id, secret_hash)`, `bot_profiles` с `app_user_id`/`channel`); линия строится по `owner_id` + `bot_key` / `superseded_by` (`clone_lineage.lineage_cte`), поэтому p-old (тот же ключ, вытеснен) уходит, p-other (другой ключ) остаётся.

- [ ] **Step 3: Staging**

Run:
```bash
python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwab77f282533c
python3 Tools/n8n/staging/bootstrap.py --restart
python3 Tools/n8n/staging/e2e-learning.py
python3 Tools/n8n/staging/e2e-handoff.py
python3 Tools/n8n/staging/e2e-auth.py --only bot_delete_wipes_lineage,retired_bot_owner_can_delete
```
Expected: `e2e-learning.py` 10 pass, 0 fail (`set_not_recorded_on_abstain` может быть INCONCLUSIVE — это PASS с пометкой); `e2e-handoff.py` без регрессий; оба delete-сценария e2e-auth PASS (ответ Delete Bot Files по-прежнему `success:true`, теперь с `deletedSuggestionSets`).

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/apply-history-handoff.py Tools/n8n/apply-auth-gate.py Tools/n8n/apply-learning-a.py Tools/n8n/verify-learning-a.py Tools/n8n/workflows/lmjYsdNcQA2IE5rl-Delete_Bot_Files.json Tools/n8n/workflows/fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json Tools/n8n/staging/e2e-learning.py Tools/n8n/tests/test_learning_a.py Tools/n8n/tests/test_retired_delete.py
git commit -m "feat(n8n): learning A — наборы панели удаляются с ботом внутри Delete Chat Memory (CTE sets, счётчик в вердикте V12), retention 180 дней (Expire Learning Rows), тест SQL в харнессе

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: `SuggestionResult.requestKey` — ключ набора доезжает до клиента

**Files:**
- Modify: `Assets/Scripts/Chat/SuggestionResult.cs`
- Modify: `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` (корутина `Run`: вызов `BuildPayloadJson` + `MapResponse`; комментарий над `BuildPickStats` про `SuggestionsController.RecordPick` → `SuggestionPickStats.RecordFromOrigin at the successful send`)
- Test: `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs`

**Interfaces:**
- Produces: `SuggestionResult.requestKey` (string, null для Empty/Error) и `public static SuggestionResult N8nSuggestionsProvider.WithRequestKey(SuggestionResult r, string key)`.

- [ ] **Step 1: Падающий тест — добавить в конец класса `SuggestRepliesMapTests`**

```csharp
    // --- learning A: the set's request key rides the result so a send can name the set it came from ---

    [Test]
    public void WithRequestKey_StampsOkResultsOnly_AndIsNullSafe()
    {
        var ok = N8nSuggestionsProvider.MapResponse(
            "{\"v\":1,\"requestSeq\":1,\"suggestions\":[{\"text\":\"ок\",\"label\":\"Ответ\",\"move\":\"Ответ\"}]}", 5);
        Assert.IsNull(ok.requestKey, "MapResponse itself never knows the key");
        Assert.AreSame(ok, N8nSuggestionsProvider.WithRequestKey(ok, "0123456789abcdef"));
        Assert.AreEqual("0123456789abcdef", ok.requestKey);

        var empty = N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":1,\"suggestions\":[],\"abstain\":true}", 5);
        N8nSuggestionsProvider.WithRequestKey(empty, "0123456789abcdef");
        Assert.IsNull(empty.requestKey, "an abstain/empty result has no set to name");

        Assert.IsNull(N8nSuggestionsProvider.WithRequestKey(null, "x"));
    }
```

- [ ] **Step 2: Запустить — должен не компилироваться**

Run (Editor закрыт): `Tools/run-tests-headless.sh "SuggestRepliesMapTests"`
Expected: компиляция падает: `'SuggestionResult' does not contain a definition for 'requestKey'`.

- [ ] **Step 3: Реализация**

`SuggestionResult.cs` — добавить поле:

```csharp
    public string requestKey;           // learning A: the set's request key (N8nSuggestionsProvider.RequestKey); null on Empty/Error
```

`N8nSuggestionsProvider.cs` — в приватной корутине `Run` (не в `Request`, которая лишь стартует `Run`; перед `string json = BuildPayloadJson(`):

```csharp
        string requestKey = RequestKey(req, msgs);   // learning A: computed once — rides the payload AND the result
```

в вызове `BuildPayloadJson(...)` заменить `requestKey:        RequestKey(req, msgs));` на `requestKey:        requestKey);`, а последнюю строку метода:

```csharp
        cb?.Invoke(WithRequestKey(MapResponse(www.downloadHandler.text, req.requestSeq), requestKey));   // requestSeq stamped from the REQUEST
```

и рядом с `MapResponse` добавить:

```csharp
    /// <summary>learning A: names the SET a result came from. Only an Ok result has a set; Empty/Error stay unnamed.</summary>
    public static SuggestionResult WithRequestKey(SuggestionResult r, string requestKey)
    {
        if (r != null && r.status == SuggestionStatus.Ok) r.requestKey = requestKey;
        return r;
    }
```

- [ ] **Step 4: Тесты зелёные**

Run: `Tools/run-tests-headless.sh "SuggestRepliesMapTests|SuggestionCacheTests|SuggestRepliesPayloadTests"`
Expected: `total > 0`, `failed = 0` (payload frozen-object тест не меняется: payload не трогали).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionResult.cs Assets/Scripts/Chat/N8nSuggestionsProvider.cs Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs
git commit -m "feat(suggest): SuggestionResult.requestKey — ключ показанного набора доезжает до клиента

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Чистые seam'ы `SendOrigin` / `SendOriginResolver` / `SendOriginWire`

**Files:**
- Create: `Assets/Scripts/Chat/SendOrigin.cs`
- Create: `Assets/Scripts/Chat/SendOriginResolver.cs`
- Create: `Assets/Scripts/Chat/SendOriginWire.cs`
- Test: `Assets/Tests/Editor/Chat/SendOriginResolverTests.cs`, `Assets/Tests/Editor/Chat/SendOriginWireTests.cs`

**Interfaces:**
- Produces:
  - `enum SendOriginKind { None, Card, Edited, Typed }`
  - `class PickSnapshot { string shownKey; int shownSize; string top1Text; string pickKey; int pickIndex=-1; string pickText; string pickMove; int pickSetSize; bool HasSet; bool HasPick }`
  - `class SendOrigin { SendOriginKind kind; int index; string requestKey; int editPercent; bool top1Match; int setSize; string pickedMove; string KindWire }`
  - `static SendOrigin SendOriginResolver.Resolve(string sentText, PickSnapshot snap)`; `static string Normalize(string)`; `static double Similarity(string, string)`; `static int Levenshtein(string, string)`; `const double Top1Threshold = 0.8`
  - `static void SendOriginWire.Append(JObject args, SendOrigin origin)`

- [ ] **Step 1: Падающие тесты — создать `Assets/Tests/Editor/Chat/SendOriginResolverTests.cs`**

```csharp
using NUnit.Framework;

// EditMode coverage for the learning-A send classifier (pure C#, no Unity). The snapshot is what the
// panel showed this turn (fresh set) plus the card the owner tapped (any round); the resolver names
// the send: card (sent as tapped) / edited (tapped, then changed) / typed (set shown, nothing tapped)
// / none (no set). top1Match compares against the FRESH set's card #1 — the shadow «Авто» answer.
public class SendOriginResolverTests
{
    private static PickSnapshot Shown(string top1 = "Здравствуйте! Есть в наличии, 1500 тг.")
        => new PickSnapshot { shownKey = "0123456789abcdef", shownSize = 3, top1Text = top1 };

    private static PickSnapshot Picked(int index, string text, string move = "Ответ")
    {
        var s = Shown();
        s.pickKey = s.shownKey; s.pickSetSize = 3; s.pickIndex = index; s.pickText = text; s.pickMove = move;
        return s;
    }

    [Test]
    public void NoSnapshot_OrNoSet_IsNone()
    {
        var o = SendOriginResolver.Resolve("привет", null);
        Assert.AreEqual(SendOriginKind.None, o.kind);
        Assert.AreEqual("none", o.KindWire);
        Assert.IsNull(o.requestKey);
        Assert.AreEqual(SendOriginKind.None, SendOriginResolver.Resolve("привет", new PickSnapshot()).kind);
    }

    [Test]
    public void SetShown_NothingTapped_IsTyped_WithTop1Judged()
    {
        var o = SendOriginResolver.Resolve("Добрый день, да, есть", Shown());
        Assert.AreEqual(SendOriginKind.Typed, o.kind);
        Assert.AreEqual(-1, o.index);
        Assert.AreEqual("0123456789abcdef", o.requestKey);
        Assert.AreEqual(3, o.setSize);
        Assert.IsFalse(o.top1Match);

        var same = SendOriginResolver.Resolve("здравствуйте! есть в наличии, 1500 тг", Shown());
        Assert.AreEqual(SendOriginKind.Typed, same.kind, "typed verbatim card #1 is still typed (no tap) — but top1 matches");
        Assert.IsTrue(same.top1Match);
    }

    [Test]
    public void TappedAndSentAsIs_IsCard_EditZero()
    {
        var o = SendOriginResolver.Resolve("Уточните, пожалуйста, год выпуска?", Picked(1, "Уточните, пожалуйста, год выпуска?", "Уточнить"));
        Assert.AreEqual(SendOriginKind.Card, o.kind);
        Assert.AreEqual(1, o.index);
        Assert.AreEqual(0, o.editPercent);
        Assert.AreEqual("Уточнить", o.pickedMove);
        Assert.IsFalse(o.top1Match, "card #2 was sent, not card #1");
    }

    [Test]
    public void TappedThenChanged_IsEdited_WithPercent()
    {
        var o = SendOriginResolver.Resolve("Уточните год выпуска и модель?", Picked(1, "Уточните, пожалуйста, год выпуска?", "Уточнить"));
        Assert.AreEqual(SendOriginKind.Edited, o.kind);
        Assert.Greater(o.editPercent, 0);
        Assert.LessOrEqual(o.editPercent, 100);
    }

    [Test]
    public void Card1TappedAndSent_TopMatches()
    {
        var o = SendOriginResolver.Resolve("Здравствуйте! Есть в наличии, 1500 тг.", Picked(0, "Здравствуйте! Есть в наличии, 1500 тг."));
        Assert.AreEqual(SendOriginKind.Card, o.kind);
        Assert.IsTrue(o.top1Match);
    }

    [Test]
    public void Normalize_TrimsLowersCollapsesAndStripsEdgePunctuation()
    {
        Assert.AreEqual("есть в наличии", SendOriginResolver.Normalize("  Есть   в наличии!!! "));
        Assert.AreEqual("", SendOriginResolver.Normalize(null));
        Assert.AreEqual("а 1500 тг", SendOriginResolver.Normalize("…а 1500 тг."));
    }

    [Test]
    public void Similarity_IsOneMinusLevenshteinOverMaxLength()
    {
        Assert.AreEqual(1.0, SendOriginResolver.Similarity("abc", "ABC "), 1e-9);
        Assert.AreEqual(1.0, SendOriginResolver.Similarity("", ""), 1e-9);
        Assert.AreEqual(0.0, SendOriginResolver.Similarity("abc", ""), 1e-9);
        Assert.AreEqual(1.0 - 1.0 / 4.0, SendOriginResolver.Similarity("kitn", "kite"), 1e-9);
        Assert.AreEqual(3, SendOriginResolver.Levenshtein("kitten", "sitting"));
    }
}
```

Создать `Assets/Tests/Editor/Chat/SendOriginWireTests.cs`:

```csharp
using Newtonsoft.Json.Linq;
using NUnit.Framework;

// The six origin_* fields ride the SAME proxy call as the message (OPS meta section, never jsonBody).
public class SendOriginWireTests
{
    private static JObject Args() => JObject.FromObject(new { profile_id = "p", body = "текст", recipient = "7701", quoted_message_id = (string)null });

    [Test]
    public void NullOrigin_AddsNothing()
    {
        var a = Args();
        SendOriginWire.Append(a, null);
        Assert.IsNull(a["origin_kind"]);
        SendOriginWire.Append(null, new SendOrigin());   // no throw
    }

    [Test]
    public void Card_WritesAllSix()
    {
        var a = Args();
        SendOriginWire.Append(a, new SendOrigin { kind = SendOriginKind.Card, index = 2, requestKey = "0123456789abcdef", editPercent = 0, top1Match = false, setSize = 4 });
        Assert.AreEqual("card", (string)a["origin_kind"]);
        Assert.AreEqual(2, (int)a["origin_index"]);
        Assert.AreEqual("0123456789abcdef", (string)a["origin_key"]);
        Assert.AreEqual(0, (int)a["origin_edit"]);
        Assert.AreEqual(0, (int)a["origin_top1"]);
        Assert.AreEqual(4, (int)a["origin_set"]);
        Assert.AreEqual("текст", (string)a["body"], "the message itself is untouched");
    }

    [Test]
    public void Typed_HasNoIndexOrEdit_ButKeyTopAndSet()
    {
        var a = Args();
        SendOriginWire.Append(a, new SendOrigin { kind = SendOriginKind.Typed, requestKey = "abc", top1Match = true, setSize = 3 });
        Assert.AreEqual("typed", (string)a["origin_kind"]);
        Assert.AreEqual(JTokenType.Null, a["origin_index"].Type);
        Assert.AreEqual(JTokenType.Null, a["origin_edit"].Type);
        Assert.AreEqual(1, (int)a["origin_top1"]);
        Assert.AreEqual(3, (int)a["origin_set"]);
    }

    [Test]
    public void None_IsKindNoneWithNulls()
    {
        var a = Args();
        SendOriginWire.Append(a, new SendOrigin());
        Assert.AreEqual("none", (string)a["origin_kind"]);
        foreach (string k in new[] { "origin_index", "origin_key", "origin_edit", "origin_top1", "origin_set" })
            Assert.AreEqual(JTokenType.Null, a[k].Type, k);
    }
}
```

- [ ] **Step 2: Компиляция падает**

Run: `Tools/run-tests-headless.sh "SendOrigin"`
Expected: ошибки компиляции `The type or namespace name 'PickSnapshot' could not be found` и т.п.

- [ ] **Step 3: Реализация — `Assets/Scripts/Chat/SendOrigin.cs`**

```csharp
/// <summary>
/// Learning A (2026-09-25): where an outgoing text came from, relative to the «Вместе» panel.
/// Card = tapped and sent as is; Edited = tapped, then changed; Typed = a set was on screen, nothing
/// tapped; None = no set this turn. Only Card/Edited count as a preference (SuggestionPickStats).
/// </summary>
public enum SendOriginKind { None, Card, Edited, Typed }

/// <summary>
/// What the panel showed this turn (the FRESH set — its card #1 is the shadow «Авто» answer) and the
/// card the owner tapped, if any (from ANY round: fresh, drill or ‹). Plain data, filled by
/// SendOriginTracker, consumed once per send by SendOriginResolver.
/// </summary>
public sealed class PickSnapshot
{
    public string shownKey;        // request key of the fresh set (N8nSuggestionsProvider.RequestKey)
    public int shownSize;          // cards in the fresh set (1–4)
    public string top1Text;        // items[0].text of the fresh set — the shadow answer

    public string pickKey;         // request key of the set the tap came from (fresh or a drill/back round)
    public int pickSetSize;        // cards in THAT set
    public int pickIndex = -1;     // index of the tapped card in THAT set; -1 = nothing tapped
    public string pickText;        // its text (what WriteComposerRoutine put in the composer)
    public string pickMove;        // its move, already resolved by SuggestionsController.ResolvePickStatsMove (may be null)

    public bool HasSet => !string.IsNullOrEmpty(shownKey) && shownSize > 0;
    public bool HasPick => pickIndex >= 0 && !string.IsNullOrEmpty(pickText);
}

/// <summary>The classified origin of one send. Serialised by SendOriginWire into the proxy call.</summary>
public sealed class SendOrigin
{
    public SendOriginKind kind = SendOriginKind.None;
    public int index = -1;         // tapped card index (Card/Edited only)
    public string requestKey;      // the set the send is attributed to (pick's set, else the shown set)
    public int editPercent;        // round((1 − similarity(sent, tapped)) × 100); 0 for Card
    public bool top1Match;         // similarity(sent, fresh card #1) ≥ SendOriginResolver.Top1Threshold
    public int setSize;            // cards in the attributed set
    public string pickedMove;      // move of the tapped card (Card/Edited), for the preference counter

    public string KindWire => kind == SendOriginKind.Card ? "card"
                            : kind == SendOriginKind.Edited ? "edited"
                            : kind == SendOriginKind.Typed ? "typed" : "none";
}
```

`Assets/Scripts/Chat/SendOriginResolver.cs`:

```csharp
using System;
using System.Text;

/// <summary>
/// Pure classifier for learning A. similarity = 1 − Levenshtein(a, b) / max(|a|, |b|) on normalised
/// strings (trim, lower, whitespace collapsed, edge punctuation stripped). EditMode-tested; no Unity.
/// </summary>
public static class SendOriginResolver
{
    public const double Top1Threshold = 0.8;

    public static SendOrigin Resolve(string sentText, PickSnapshot snap)
    {
        var o = new SendOrigin();
        if (snap == null || !snap.HasSet) return o;                       // None: nothing was on screen this turn
        o.top1Match = Similarity(sentText, snap.top1Text) >= Top1Threshold;
        if (!snap.HasPick)
        {
            o.kind = SendOriginKind.Typed;
            o.requestKey = snap.shownKey;
            o.setSize = snap.shownSize;
            return o;
        }
        o.requestKey = string.IsNullOrEmpty(snap.pickKey) ? snap.shownKey : snap.pickKey;
        o.setSize = snap.pickSetSize > 0 ? snap.pickSetSize : snap.shownSize;
        o.index = snap.pickIndex;
        o.pickedMove = snap.pickMove;
        o.editPercent = (int)Math.Round((1.0 - Similarity(sentText, snap.pickText)) * 100.0);
        o.kind = o.editPercent == 0 ? SendOriginKind.Card : SendOriginKind.Edited;
        return o;
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (char c in s.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        int start = 0, end = sb.Length;
        while (start < end && IsEdgePunct(sb[start])) start++;
        while (end > start && IsEdgePunct(sb[end - 1])) end--;
        return sb.ToString(start, end - start).Trim();
    }

    private static bool IsEdgePunct(char c) => c == '.' || c == ',' || c == '!' || c == '?' || c == '…' || c == ';' || c == ':' || c == '"' || c == '«' || c == '»' || c == '\'';

    public static double Similarity(string a, string b)
    {
        string na = Normalize(a), nb = Normalize(b);
        int max = Math.Max(na.Length, nb.Length);
        if (max == 0) return 1.0;
        return 1.0 - (double)Levenshtein(na, nb) / max;
    }

    public static int Levenshtein(string a, string b)
    {
        a = a ?? ""; b = b ?? "";
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            var t = prev; prev = cur; cur = t;
        }
        return prev[b.Length];
    }
}
```

`Assets/Scripts/Chat/SendOriginWire.cs`:

```csharp
using Newtonsoft.Json.Linq;

/// <summary>
/// Puts the six origin_* fields into the proxy args of message.send / message.reply. They are the OPS
/// `meta` section server-side: validated like the body, never forwarded to Wappi. Absent values are
/// JSON null, which the proxy's `take` treats as «not sent» (every field is optional).
/// </summary>
public static class SendOriginWire
{
    public static void Append(JObject args, SendOrigin o)
    {
        if (args == null || o == null) return;
        bool set = o.kind != SendOriginKind.None;
        bool pick = o.kind == SendOriginKind.Card || o.kind == SendOriginKind.Edited;
        args["origin_kind"] = o.KindWire;
        args["origin_index"] = pick ? (JToken)o.index : JValue.CreateNull();
        args["origin_key"] = set && !string.IsNullOrEmpty(o.requestKey) ? (JToken)o.requestKey : JValue.CreateNull();
        args["origin_edit"] = pick ? (JToken)o.editPercent : JValue.CreateNull();
        args["origin_top1"] = set ? (JToken)(o.top1Match ? 1 : 0) : JValue.CreateNull();
        args["origin_set"] = set ? (JToken)o.setSize : JValue.CreateNull();
    }
}
```

- [ ] **Step 4: Assets/Refresh, затем тесты**

В Editor: `Assets → Refresh`; проверить `ls Assets/Scripts/Chat/SendOrigin*.cs.meta` (3 файла). Затем:
Run: `Tools/run-tests-headless.sh "SendOriginResolverTests|SendOriginWireTests"`
Expected: `total = 11` (7 resolver + 4 wire), `failed = 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SendOrigin.cs Assets/Scripts/Chat/SendOrigin.cs.meta Assets/Scripts/Chat/SendOriginResolver.cs Assets/Scripts/Chat/SendOriginResolver.cs.meta Assets/Scripts/Chat/SendOriginWire.cs Assets/Scripts/Chat/SendOriginWire.cs.meta Assets/Tests/Editor/Chat/SendOriginResolverTests.cs Assets/Tests/Editor/Chat/SendOriginResolverTests.cs.meta Assets/Tests/Editor/Chat/SendOriginWireTests.cs Assets/Tests/Editor/Chat/SendOriginWireTests.cs.meta
git commit -m "feat(chat): SendOrigin — чистая классификация отправки (карточка/правка/своё) и её проводка в args прокси

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Снимок панели + счётчик предпочтений при отправке (контроллер)

**Files:**
- Create: `Assets/Scripts/Chat/SendOriginTracker.cs`
- Create: `Assets/Scripts/Chat/SuggestionPickStats.cs`
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs` (`OnResult` :525-543, `HandleCardTapped` :546-563, `HandleBack` :578-593, `RecordPick` :598-608 — удалить, `StartFreshRound` :620-628, место `_currentRendered = cached;` ≈:411)
- Test: `Assets/Tests/Editor/Chat/SendOriginTrackerTests.cs`, `Assets/Tests/Editor/Chat/SendOriginWiringTests.cs`

**Interfaces:**
- Consumes: `SuggestionResult.requestKey` (Task 7), `PickSnapshot`/`SendOrigin`/`SendOriginResolver` (Task 8).
- Produces: `static class SendOriginTracker { PickSnapshot Current; void SetShown(string key, int size, string top1Text, bool fresh); void SetPick(string key, int setSize, int index, string text, string move); void Clear(); SendOrigin ResolveAndClear(string sentText) }`; `static class SuggestionPickStats { void Record(string botName, string move); bool CountsAsPreference(SendOrigin); void RecordFromOrigin(string botName, SendOrigin) }`.

- [ ] **Step 1: Падающие тесты — `Assets/Tests/Editor/Chat/SendOriginTrackerTests.cs`**

```csharp
using NUnit.Framework;

// The tracker is the one handoff between the panel (what was shown / tapped) and the send path.
// Static state — every test starts from Clear().
public class SendOriginTrackerTests
{
    [SetUp] public void Reset() => SendOriginTracker.Clear();

    [Test]
    public void FreshSet_ReplacesSnapshot_DrillDoesNot()
    {
        SendOriginTracker.SetShown("aaaa", 3, "Первая", fresh: true);
        SendOriginTracker.SetShown("bbbb", 4, "Другая первая", fresh: false);   // a drill round: the fresh #1 stays the shadow answer
        Assert.AreEqual("aaaa", SendOriginTracker.Current.shownKey);
        Assert.AreEqual("Первая", SendOriginTracker.Current.top1Text);
        SendOriginTracker.SetShown("cccc", 2, "Новая", fresh: true);            // a new incoming → new turn
        Assert.AreEqual("cccc", SendOriginTracker.Current.shownKey);
        Assert.IsFalse(SendOriginTracker.Current.HasPick);
    }

    [Test]
    public void PickFromADrillRound_KeepsItsOwnKey_ButTop1FromTheFreshSet()
    {
        SendOriginTracker.SetShown("aaaa", 3, "Первая", fresh: true);
        SendOriginTracker.SetShown("bbbb", 4, "Вариант первой", fresh: false);
        SendOriginTracker.SetPick("bbbb", 4, 2, "Третий вариант", "Ответ");
        var o = SendOriginTracker.ResolveAndClear("Третий вариант");
        Assert.AreEqual(SendOriginKind.Card, o.kind);
        Assert.AreEqual("bbbb", o.requestKey);
        Assert.AreEqual(4, o.setSize);
        Assert.AreEqual(2, o.index);
        Assert.IsFalse(o.top1Match);
        Assert.IsNull(SendOriginTracker.Current, "a send consumes the snapshot");
    }

    [Test]
    public void PickWithoutSnapshot_CreatesOne()
    {
        SendOriginTracker.SetPick("aaaa", 3, 0, "Текст", "Ответ");   // ‹ after a send restored a round and the owner tapped again
        Assert.IsTrue(SendOriginTracker.Current.HasSet);
        Assert.IsTrue(SendOriginTracker.Current.HasPick);
        Assert.AreEqual(SendOriginKind.Card, SendOriginTracker.ResolveAndClear("Текст").kind);
    }

    [Test]
    public void DrillWithoutSnapshot_HasNoTop1()
    {
        SendOriginTracker.SetShown("bbbb", 4, "Вариант первой", fresh: false);   // ‹ or a drill round after a Clear
        Assert.IsTrue(SendOriginTracker.Current.HasSet);
        Assert.IsNull(SendOriginTracker.Current.top1Text, "a non-fresh set never becomes the shadow answer");
        Assert.IsFalse(SendOriginTracker.ResolveAndClear("Вариант первой").top1Match);
    }

    [Test]
    public void HidingThePanel_DropsAShownSet_ButKeepsATappedCard()
    {
        SendOriginTracker.SetShown("aaaa", 3, "x", fresh: true);
        Assert.IsFalse(SendOriginTracker.Current.HasPick, "no tap yet — the controller may Clear on hide");
        SendOriginTracker.SetPick("aaaa", 3, 1, "Текст", "Ответ");
        Assert.IsTrue(SendOriginTracker.Current.HasPick, "a tapped card is already in the composer — the controller must keep it on hide");
    }

    [Test]
    public void IgnoresEmptyKeysAndTexts()
    {
        SendOriginTracker.SetShown(null, 3, "x", fresh: true);
        SendOriginTracker.SetShown("aaaa", 0, "x", fresh: true);
        Assert.IsNull(SendOriginTracker.Current);
        SendOriginTracker.SetShown("aaaa", 3, "x", fresh: true);
        SendOriginTracker.SetPick("aaaa", 3, 1, "", "Ответ");
        Assert.IsFalse(SendOriginTracker.Current.HasPick);
    }

    [Test]
    public void PreferenceCounter_OnlyCardAndEditedWithAValidMove()
    {
        Assert.IsTrue(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Card, pickedMove = "Ответ" }));
        Assert.IsTrue(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Edited, pickedMove = "Уточнить" }));
        Assert.IsFalse(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Typed, pickedMove = "Ответ" }));
        Assert.IsFalse(SuggestionPickStats.CountsAsPreference(new SendOrigin { kind = SendOriginKind.Card, pickedMove = "Цена" }));
        Assert.IsFalse(SuggestionPickStats.CountsAsPreference(null));
    }
}
```

`Assets/Tests/Editor/Chat/SendOriginWiringTests.cs` (SOURCE-guard по образцу `SendPathWiringTests` — EditMode не может нажать карточку, поэтому пинится проводка):

```csharp
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source-invariant guard for learning A (the SendPathWiringTests pattern). The seams are pure and
/// tested, but they act only where the call sites put them: the tap must snapshot (not count), the
/// send must resolve BEFORE any yield and ride the proxy call, and the preference counter must move
/// to the successful send. Revert any one site and every seam test stays green while the data lies.
/// </summary>
public class SendOriginWiringTests
{
    [Test]
    public void Tap_Snapshots_AndNoLongerCounts()
    {
        string tap = MethodBody("Scripts/Chat/SuggestionsController.cs", "HandleCardTapped");
        Assert.IsTrue(tap.Contains("SendOriginTracker.SetPick("), "a tap must record the picked card for the send");
        Assert.IsFalse(tap.Contains("RecordPick("), "a tap is not a preference — the counter moved to the successful send (learning A)");
        string src = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Chat/SuggestionsController.cs"));
        Assert.IsFalse(Regex.IsMatch(src, @"void\s+RecordPick\s*\("), "RecordPick lives in SuggestionPickStats now");
    }

    [Test]
    public void ShownSets_AreSnapshotted_AndAFreshRoundClears()
    {
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "OnResult").Contains("SendOriginTracker.SetShown("), "a rendered set must be snapshotted");
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "StartFreshRound").Contains("SendOriginTracker.Clear("), "a fresh round starts a new turn");
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "HandleToggle").Contains("SendOriginTracker.Clear("), "toggle-OFF hides the set — it can no longer explain a send");
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "HandleReplyModeServerNewer").Contains("SendOriginTracker.Clear("), "a server-side switch to «Авто» hides the set too");
    }

    [Test]
    public void Send_ResolvesBeforeOptimisticEcho_RidesTheProxyCall_AndCountsOnSuccess()
    {
        string send = MethodBody("Scripts/Main/ChatManager.cs", "SendTextMessageRoutine");
        int resolveAt = send.IndexOf("SendOriginTracker.ResolveAndClear(");
        int echoAt    = send.IndexOf("OnLiveMessagesReceived?.Invoke(");
        int yieldAt   = send.IndexOf("yield return");
        Assert.IsTrue(resolveAt >= 0, "the send must classify its origin");
        Assert.IsTrue(echoAt >= 0, "SendTextMessageRoutine no longer announces the optimistic bubble — re-derive where the snapshot is cleared");
        Assert.IsTrue(resolveAt < echoAt, "classify BEFORE the optimistic bubble is announced — SuggestionsController.HandleLive folds it as an answered run and StartFreshRound clears the snapshot SYNCHRONOUSLY, in the same call stack");
        Assert.IsTrue(yieldAt < 0 || resolveAt < yieldAt, "…and before the first yield (the poll's own echo is the second, later reset)");
        Assert.IsTrue(send.Contains("originJson ="), "the resolved origin must be persisted in the outbox entry so a retry carries it");
        string post = MethodBody("Scripts/Main/ChatManager.cs", "PostTextMessageRoutine");
        Assert.IsTrue(post.Contains("SendOriginWire.Append("), "origin_* must ride the same WappiProxy.Send args");
        Assert.IsTrue(post.Contains("chat_id = ChatIdFormat.Recipient(chatId)"), "a Telegram reply must name its chat in meta (the client sends no recipient there)");
        Assert.IsTrue(post.Contains("SuggestionPickStats.RecordFromOrigin("), "the preference counter fires on the SUCCESSFUL send");
        Assert.IsTrue(post.IndexOf("SuggestionPickStats.RecordFromOrigin(") > post.IndexOf("response.status == \"done\""), "…and only inside the success branch");
        string retry = MethodBody("Scripts/Main/ChatManager.Outbox.cs", "RetryRoutine");
        Assert.IsTrue(retry.Contains("retryOrigin"), "a retry must carry the origin saved at the first attempt (it is never re-resolved: the snapshot was consumed)");
    }

    // ---- source helpers (SendPathWiringTests) --------------------------------------------

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update SendOriginWiringTests");
        string source = File.ReadAllText(path);
        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!return\b)[\w<>.]+[ \t]+" +
            Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"{method} not found in {relativePath}");
        int open = source.IndexOf('{', decl.Index);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}
```

- [ ] **Step 2: Компиляция падает**

Run: `Tools/run-tests-headless.sh "SendOriginTrackerTests|SendOriginWiringTests"`
Expected: `SendOriginTracker` not found.

- [ ] **Step 3: Реализация — `Assets/Scripts/Chat/SendOriginTracker.cs`**

```csharp
/// <summary>
/// Learning A: the ONE handoff between the panel and the send path. The panel calls SetShown when a
/// set renders (fresh = round 1 of a turn: its card #1 is the shadow «Авто» answer; a drill/‹ round
/// keeps the fresh snapshot) and SetPick when a card is tapped; a fresh round Clears; hiding the panel
/// (toggle OFF, server flip to «Авто») drops a shown set but keeps a tapped card; the send calls
/// ResolveAndClear ONCE, before it announces the optimistic bubble through OnLiveMessagesReceived —
/// SuggestionsController.HandleLive folds that echo as an answered run and clears the snapshot
/// SYNCHRONOUSLY. Static on purpose: SuggestionsController and ChatManager are separate MonoBehaviours
/// with no reference to each other, and there is exactly one open chat.
/// </summary>
public static class SendOriginTracker
{
    private static PickSnapshot _snapshot;

    public static PickSnapshot Current => _snapshot;

    public static void SetShown(string requestKey, int setSize, string top1Text, bool fresh)
    {
        if (string.IsNullOrEmpty(requestKey) || setSize <= 0) return;
        if (!fresh)
        {
            if (_snapshot != null) return;   // drill / back: the turn's fresh #1 stays the shadow answer, a pending tap stays
            _snapshot = new PickSnapshot { shownKey = requestKey, shownSize = setSize, top1Text = null };   // a non-fresh set never becomes the shadow #1
            return;
        }
        _snapshot = new PickSnapshot { shownKey = requestKey, shownSize = setSize, top1Text = top1Text };
    }

    public static void SetPick(string requestKey, int setSize, int index, string text, string move)
    {
        if (string.IsNullOrEmpty(text) || index < 0) return;
        if (_snapshot == null)   // ‹ after a send restored a round: attribute to that set, no shadow #1 known
            _snapshot = new PickSnapshot { shownKey = requestKey, shownSize = setSize, top1Text = null };
        _snapshot.pickKey = requestKey;
        _snapshot.pickSetSize = setSize;
        _snapshot.pickIndex = index;
        _snapshot.pickText = text;
        _snapshot.pickMove = move;
    }

    public static void Clear() => _snapshot = null;

    /// <summary>Classifies the send and consumes the snapshot — a second message in the same turn is «none».</summary>
    public static SendOrigin ResolveAndClear(string sentText)
    {
        SendOrigin o = SendOriginResolver.Resolve(sentText, _snapshot);
        _snapshot = null;
        return o;
    }
}
```

`Assets/Scripts/Chat/SuggestionPickStats.cs` (тело бывшего `SuggestionsController.RecordPick`, теперь пишется при УСПЕШНОЙ отправке):

```csharp
using UnityEngine;

/// <summary>
/// Preference counters `{botName}SuggestPick{move}` (PlayerPrefs, the closed 6-move taxonomy). Learning A
/// moved the write from the TAP (which also fired a paid drill) to the SUCCESSFUL SEND, and only for a
/// card sent as is or edited — a set the owner ignored is not a preference. Still per bot, per device:
/// the server counts the same thing from chat_history_events.origin_* (B moves the prompt's pickStats there).
/// </summary>
public static class SuggestionPickStats
{
    public static void Record(string botName, string move)
    {
        if (string.IsNullOrEmpty(botName) || !SuggestionMoves.IsMove(move)) return;
        string key = botName + "SuggestPick" + move;
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence)
    }

    public static bool CountsAsPreference(SendOrigin o)
        => o != null && (o.kind == SendOriginKind.Card || o.kind == SendOriginKind.Edited) && SuggestionMoves.IsMove(o.pickedMove);

    public static void RecordFromOrigin(string botName, SendOrigin o)
    {
        if (CountsAsPreference(o)) Record(botName, o.pickedMove);
    }
}
```

`SuggestionsController.cs`:

(а) `OnResult` — после строки `if (result != null && result.status == SuggestionStatus.Ok) _currentRendered = result;`:

```csharp
        if (result != null && result.status == SuggestionStatus.Ok && result.items != null && result.items.Count > 0)
            SendOriginTracker.SetShown(result.requestKey, result.items.Count, result.items[0].text, fresh: freshSet);   // learning A
```

(б) `HandleCardTapped` — заменить строку `RecordPick(picked);` на:

```csharp
        // learning A: a tap is a SNAPSHOT for the send, not a preference — SuggestionPickStats counts on the successful send.
        int pickedIndex = (picked != null && _currentRendered?.items != null) ? _currentRendered.items.IndexOf(picked) : -1;
        SendOriginTracker.SetPick(_currentRendered?.requestKey, _currentRendered?.items?.Count ?? 0, pickedIndex, replyText, ResolvePickStatsMove(picked));
```

(в) `HandleBack` — после `_currentRendered = previous;`:

```csharp
        if (previous?.items != null && previous.items.Count > 0)
            SendOriginTracker.SetShown(previous.requestKey, previous.items.Count, previous.items[0].text, fresh: false);   // learning A: no-op while the turn's snapshot lives
```

(г) Удалить метод `RecordPick` целиком (строки `// Preference learning v1 under the drill redesign…` до закрывающей скобки метода). `ResolvePickStatsMove` оставить. В комментарии над `HandleCardTapped` фразу «…and count the pick's MOVE» заменить на «…and snapshot the pick for the send (learning A)». В `N8nSuggestionsProvider.cs` комментарий над `BuildPickStats` («written by SuggestionsController.RecordPick under …») → «written by SuggestionPickStats.RecordFromOrigin at the successful send (learning A) under …».

(д′) Скрытие панели без нового раунда должно отпустить показанный набор — пока `_semiAutoOn == false`, ни `HandleLive`, ни `DebounceLoop` не вызывают `StartFreshRound`, так что снимок пережил бы новые входящие и следующее ручное сообщение ушло бы как `typed` против набора, которого на экране нет. Тапнутую карточку при этом сохраняем: её текст уже в композере и всё ещё классифицируется как card/edited. В `HandleToggle`, ветка `else`, сразу после `HidePanel();`:

```csharp
            if (!(SendOriginTracker.Current?.HasPick ?? false)) SendOriginTracker.Clear();   // learning A: a hidden set cannot explain a send; a tapped card can
```

и в `HandleReplyModeServerNewer` заменить `if (_semiAutoOn) ShowPanel(claimSlotFromKeyboard: false); else HidePanel();` на:

```csharp
        if (_semiAutoOn) ShowPanel(claimSlotFromKeyboard: false);
        else { HidePanel(); if (!(SendOriginTracker.Current?.HasPick ?? false)) SendOriginTracker.Clear(); }   // learning A
```

(д) `StartFreshRound` — первой строкой тела:

```csharp
        SendOriginTracker.Clear();   // learning A: a new turn — the previous set can no longer explain a send
```

(е) Место восстановления из кэша (`grep -n "_currentRendered = cached;" Assets/Scripts/Chat/SuggestionsController.cs`, ≈ строка 411) — сразу после этой строки:

```csharp
        if (cached?.items != null && cached.items.Count > 0)
            SendOriginTracker.SetShown(cached.requestKey, cached.items.Count, cached.items[0].text, fresh: true);   // learning A: a re-opened chat shows its cached fresh set
```

- [ ] **Step 4: Assets/Refresh, тесты**

Run: `grep -rn "RecordPick" Assets/ --include='*.cs'` → ожидаемо только комментарии/`SuggestionPickStats`/тест-строки в `SendOriginWiringTests`.
Run: `Tools/run-tests-headless.sh "SendOrigin|SuggestionDrillRoundsTests|SuggestionRoundStackTests|SuggestionCacheTests"`
Expected: `failed = 1` — ровно `SendOriginWiringTests.Send_ResolvesBeforeOptimisticEcho_RidesTheProxyCall_AndCountsOnSuccess` (ChatManager — Task 10); всё остальное PASS (`SendOriginTrackerTests` 8/8).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SendOriginTracker.cs Assets/Scripts/Chat/SendOriginTracker.cs.meta Assets/Scripts/Chat/SuggestionPickStats.cs Assets/Scripts/Chat/SuggestionPickStats.cs.meta Assets/Scripts/Chat/SuggestionsController.cs Assets/Tests/Editor/Chat/SendOriginTrackerTests.cs Assets/Tests/Editor/Chat/SendOriginTrackerTests.cs.meta Assets/Tests/Editor/Chat/SendOriginWiringTests.cs Assets/Tests/Editor/Chat/SendOriginWiringTests.cs.meta
git commit -m "feat(suggest): снимок показанного набора и тапа для отправки; счётчик предпочтений переезжает с тапа на отправку

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Отправка несёт `origin_*` (ChatManager)

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`SendTextMessageRoutine`, `PostTextMessageRoutine`)
- Modify: `Assets/Scripts/Chat/OutboxStore.cs` (`OutboxEntry`: append-only поле `originJson`)
- Modify: `Assets/Scripts/Main/ChatManager.Outbox.cs` (`RetryRoutine`: восстановить метку из `entry.originJson`)
- Modify: `Assets/Scripts/Chat/SendOrigin.cs` (поле `botId`)
- Test: `Assets/Tests/Editor/Chat/SendOriginWiringTests.cs` (уже написан в Task 9), `SendPathWiringTests` (не меняется — должен остаться зелёным)

**Interfaces:**
- Consumes: `SendOriginTracker.ResolveAndClear`, `SendOriginWire.Append`, `SuggestionPickStats.RecordFromOrigin`, `ChatManager.CurrentBotId` (объявлен в партиале `ChatManager.BotState.cs:14`, `public string CurrentBotId { get; private set; }`).

- [ ] **Step 1: Убедиться, что тест падает**

Run: `Tools/run-tests-headless.sh "SendOriginWiringTests"`
Expected: `Send_ResolvesBeforeOptimisticEcho_RidesTheProxyCall_AndCountsOnSuccess` FAIL («the send must classify its origin»).

- [ ] **Step 2: Реализация**

В начале `ChatManager.cs` проверить `using Newtonsoft.Json.Linq;` (`grep -n "Newtonsoft.Json.Linq" Assets/Scripts/Main/ChatManager.cs`); если нет — добавить рядом с `using Newtonsoft.Json;`.

`SendOrigin.cs` — добавить поле (сериализуется в outbox вместе с остальными): `public string botId;   // bot the send was made on — the preference counter must not credit a bot the owner switched to mid-flight`.

`SendTextMessageRoutine` — сразу после `string sendCacheRoot = GetCacheRoot();` (ДО `OnLiveMessagesReceived?.Invoke(...)`, который стоит ниже в этом же методе):

```csharp
    // learning A: classify the send against what the panel showed/tapped — BEFORE the optimistic
    // OnLiveMessagesReceived echo below (not merely before the first yield): that Invoke is SYNCHRONOUS,
    // SuggestionsController.HandleLive folds the outgoing bubble to Cancel → StartFreshRound →
    // SendOriginTracker.Clear, so a resolve placed after it always reads `none`. Consumed once per send;
    // the retry path reads the copy persisted in the outbox entry. Pinned by SendOriginWiringTests.
    SendOrigin sendOrigin = SendOriginTracker.ResolveAndClear(text);
    sendOrigin.botId = CurrentBotId;   // the coroutine outlives a bot switch (it runs on Manager.Instance)
```

в `Outbox.Add(new OutboxStore.OutboxEntry { … channel = (int)ActiveChannel, })` добавить после `channel`:

```csharp
        originJson      = JsonConvert.SerializeObject(sendOrigin),   // learning A: a retry carries the same origin (never re-resolved)
    });
```

и последнюю строку метода:

```csharp
    yield return PostTextMessageRoutine(chatId, text, tempId, activeProfileId, sendCacheRoot, quotedId, ActiveChannel, sendOrigin);
```

`OutboxStore.cs`, класс `OutboxEntry`, после `public int channel;` (append-only; `JsonUtility` заполняет отсутствующее поле null — старые файлы outbox читаются как «метки не было»):

```csharp
        // --- appended for learning A (append-only; JsonUtility fills missing as null) ---
        public string originJson;        // JsonConvert-serialised SendOrigin resolved at the FIRST attempt; null = legacy/no panel
```

`ChatManager.Outbox.cs`, `RetryRoutine` — перед `try`:

```csharp
        // learning A: the origin was resolved once, at the first attempt (the panel snapshot is consumed there);
        // a retry carries that copy. A broken string degrades to null (= legacy), never to an exception.
        SendOrigin retryOrigin = null;
        if (!string.IsNullOrEmpty(entry.originJson))
        {
            try { retryOrigin = JsonConvert.DeserializeObject<SendOrigin>(entry.originJson); } catch (Exception) { retryOrigin = null; }
        }
```

и вызов текстового повтора: `yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot, entry.quotedMessageId, (ChatChannel)entry.channel, retryOrigin);` (проверить `using Newtonsoft.Json;` и `using System;` в этом партиале).

`PostTextMessageRoutine` — сигнатура получает последний параметр `SendOrigin origin = null`:

```csharp
private IEnumerator PostTextMessageRoutine(
    string chatId,
    string text,
    string tempId,
    string profileId,
    string sendCacheRoot,
    string quotedMessageId = null,
    ChatChannel channel = ChatChannel.WhatsApp,
    SendOrigin origin = null)
```

блок сборки `args` — заменить на:

```csharp
    string op;
    JObject args;
    if (telegramReply)
    {
        op = WappiProxy.Op.MessageReply;
        // chat_id rides the proxy's meta section (never forwarded to Wappi): the reply body carries no recipient,
        // and without it Record Owner Send wrote chat_id = '' for every Telegram quote-reply (A6 defect).
        args = JObject.FromObject(new { profile_id = profileId, body = text, message_id = quotedMessageId, chat_id = ChatIdFormat.Recipient(chatId) });
    }
    else
    {
        string recipient = ChatIdFormat.Recipient(chatId);
        op = WappiProxy.Op.MessageSend;
        args = JObject.FromObject(new { profile_id = profileId, body = text, recipient, quoted_message_id = quotedMessageId });
    }
    SendOriginWire.Append(args, origin);   // learning A: origin_* ride the same call (OPS meta, never forwarded to Wappi); null adds nothing
```

в ветке успеха — после `Outbox.RemoveAt(sendCacheRoot, chatId, tempId);`:

```csharp
        SuggestionPickStats.RecordFromOrigin(origin?.botId ?? CurrentBotId, origin);   // learning A: a card sent (as is or edited) is the preference, not the tap; credited to the bot it was sent on
```

- [ ] **Step 3: Тесты**

Run: `Tools/run-tests-headless.sh "SendOriginWiringTests|SendPathWiringTests|AuthWiringGuardTests|OutgoingSendCacheTests"`
Expected: все PASS (`SendPathWiringTests` — LiveCacheFor/PersistSendCache нетронуты; `AuthWiringGuardTests` — новых `SendWebRequest` нет).
Затем полный прогон: `Tools/run-tests-headless.sh` → `failed = 0`, `total ≥ 2679 + новые`.

- [ ] **Step 4: Ручная проверка в Editor (Play, Wappi через прокси на staging невозможен — только формирование тела)**

В Play Mode открыть чат с включённым «Вместе», дождаться карточек, тапнуть карточку №2, отправить. В Console найти лог `[Wappi] message/send …` (только при ошибке) либо временно поставить точку останова на `WappiProxy.Send` и убедиться, что тело содержит `"origin_kind":"card","origin_index":1`. Не коммитить отладочные строки.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs Assets/Scripts/Main/ChatManager.Outbox.cs Assets/Scripts/Chat/OutboxStore.cs Assets/Scripts/Chat/SendOrigin.cs
git commit -m "feat(chat): отправка несёт origin_* (карточка/правка/своё) в message.send/reply, chat_id для TG-ответа, метка переживает retry через outbox; счётчик предпочтений — при успешной отправке

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: Отчёт, политика, документация, чек-лист деплоя

**Files:**
- Create: `Tools/n8n/report-learning.py`
- Modify: `docs/legal/privacy.html:71-72`
- Modify: `Tools/n8n/README.md` (строка в списке инструментов), `CLAUDE.md` (абзац в разделе n8n про `origin_*`/`suggestion_sets`)
- Modify: `docs/audits/implementation-progress.md` (запись «learning A — готово к деплою, ждёт go»)

**Interfaces:**
- Consumes: `chat_history_events.origin_*`, `suggestion_sets`.

- [ ] **Step 1: Отчёт — создать `Tools/n8n/report-learning.py`**

```python
#!/usr/bin/env python3
"""Read-only learning-A report: how owners answer relative to the «Вместе» panel. NO message text is
printed — only counts, shares and token totals per profile (profile ids are the bot's own Wappi ids).

    python3 Tools/n8n/report-learning.py                 # staging (Tools/n8n/staging/pg.sh)
    python3 Tools/n8n/report-learning.py --days 14
    python3 Tools/n8n/report-learning.py --prod          # prod through .secrets/sql-oneoff.py — OWNER GO ONLY

Columns: sends (owner_app rows with an origin), card/edited/typed/none shares, top1 (share of sends with a
shown set whose text matched the fresh card #1), edit (mean origin_edit over edited), sets (suggestion_sets
rows), learning_sets_3moves (share of learning-stage sets with ≥3 distinct moves), tokens (sum of usage).
Known limits: groups (@g.us) are EXCLUDED — the panel has no gate in groups, group sends are not learning
data; `sets` is best-effort (Record Set runs after Respond but inside the 20-s execution timeout, so a set at
the deadline may be missing) — compare `sets` with the panel's successful answers when calibrating B.
"""
import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

SQL = """
with s as (
  select profile_id,
         count(*) filter (where origin_kind is not null) as sends,
         count(*) filter (where origin_kind = 'card')   as card,
         count(*) filter (where origin_kind = 'edited') as edited,
         count(*) filter (where origin_kind = 'typed')  as typed,
         count(*) filter (where origin_kind = 'none')   as none,
         count(*) filter (where origin_top1 = 1)        as top1_hits,
         count(*) filter (where origin_kind in ('card','edited','typed')) as with_set,
         round(avg(origin_edit) filter (where origin_kind = 'edited'))    as edit_mean
  from chat_history_events
  where source = 'owner_app' and chat_id not like '%%@g.us' and created_at >= now() - interval '%(days)s days'
  group by profile_id
), t as (
  select profile_id,
         count(*) as sets,
         count(*) filter (where stage = 'learning') as learning_sets,
         count(*) filter (where stage = 'learning' and (select count(distinct x->>'move') from jsonb_array_elements(items) x) >= 3) as learning_sets_3moves,
         coalesce(sum((usage->>'prompt_tokens')::int + (usage->>'completion_tokens')::int), 0) as tokens
  from suggestion_sets
  where created_at >= now() - interval '%(days)s days'
  group by profile_id
)
select coalesce(s.profile_id, t.profile_id) as profile_id, coalesce(s.sends,0) sends, coalesce(s.card,0) card, coalesce(s.edited,0) edited,
       coalesce(s.typed,0) typed, coalesce(s.none,0) none, coalesce(s.top1_hits,0) top1_hits, coalesce(s.with_set,0) with_set, s.edit_mean,
       coalesce(t.sets,0) sets, coalesce(t.learning_sets,0) learning_sets, coalesce(t.learning_sets_3moves,0) learning_sets_3moves, coalesce(t.tokens,0) tokens
from s full outer join t on t.profile_id = s.profile_id
order by 1;
"""


COLS = ["profile_id", "sends", "card", "edited", "typed", "none", "top1_hits", "with_set", "edit_mean", "sets", "learning_sets", "learning_sets_3moves", "tokens"]


def json_sql(sql):
    """ONE row, ONE column: {"rows":[...]} — the same contract for psql (prints json as text) and the prod harness
    (an n8n webhook whose Response Data may be firstEntryJson → {"payload":…} or allEntries → [{"payload":…}])."""
    return "select json_build_object('rows', coalesce(json_agg(row_to_json(r) order by r.profile_id), '[]'::json)) as payload from (" + sql.rstrip().rstrip(";") + ") r"


def parse_rows(text):
    obj = json.loads(text)
    if isinstance(obj, list):
        obj = obj[0] if obj else {}
    payload = obj.get("payload", obj) if isinstance(obj, dict) else obj
    if isinstance(payload, str):
        payload = json.loads(payload)
    rows = (payload or {}).get("rows") or []
    return [[("" if r.get(c) is None else str(r.get(c))) for c in COLS] for r in rows]


def run_staging(sql):
    out = subprocess.run([os.path.join(HERE, "staging", "pg.sh"), "psql", "-tAc", json_sql(sql)], capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(out.stderr.strip())
    return parse_rows(out.stdout.strip())


def run_prod(sql):
    out = subprocess.run([sys.executable, os.path.join(HERE, ".secrets", "sql-oneoff.py"), json_sql(sql)], capture_output=True, text=True)
    lines = [l for l in out.stdout.strip().splitlines() if l.strip()]
    if out.returncode != 0 or not lines:
        sys.exit((out.stderr.strip() or out.stdout.strip() or "harness produced no output")[:300])
    status, _, body = lines[-1].partition(" ")      # the harness prints ONE line: `200 <body>`
    if status != "200":
        sys.exit(f"harness -> {lines[-1][:300]}")
    try:
        return parse_rows(body)
    except json.JSONDecodeError:
        sys.exit("harness output is not JSON — most likely truncated by sql-oneoff.py's b[:1500]; lift that limit first (deploy checklist 1a)")


def selftest():
    sample = '{"rows":[{"profile_id":"p","sends":3,"card":1,"edited":1,"typed":1,"none":0,"top1_hits":2,"with_set":3,"edit_mean":40,"sets":3,"learning_sets":3,"learning_sets_3moves":2,"tokens":900}]}'
    a = parse_rows(sample)
    b = parse_rows("[" + '{"payload":' + sample + "}" + "]")
    c = parse_rows('{"payload":' + sample + "}")
    assert a == b == c and a[0][0] == "p" and a[0][-1] == "900", (a, b, c)
    print("selftest OK")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=14)
    ap.add_argument("--prod", action="store_true", help="prod through the self-deleting SQL harness (owner go only)")
    ap.add_argument("--selftest", action="store_true", help="check the JSON parser against the three shapes the harness/psql can produce")
    a = ap.parse_args()
    if a.selftest:
        selftest()
        return
    sql = SQL % {"days": a.days}
    rows = run_prod(sql) if a.prod else run_staging(sql)
    print(f"{'profile':18} {'sends':>5} {'card':>5} {'edit':>5} {'typed':>5} {'none':>5} {'top1':>6} {'edit%':>5} {'sets':>5} {'lrn3/lrn':>9} {'tokens':>8}")
    for r in rows:
        pid, sends, card, edited, typed, none, top1, with_set, edit_mean, sets, lrn, lrn3, tokens = r
        top1_share = f"{int(top1) / int(with_set):.0%}" if int(with_set or 0) else "—"
        print(f"{pid[:18]:18} {sends:>5} {card:>5} {edited:>5} {typed:>5} {none:>5} {top1_share:>6} {edit_mean or '—':>5} {sets:>5} {lrn3 + '/' + lrn:>9} {tokens:>8}")
    if not rows:
        print("(no rows in the window)")


if __name__ == "__main__":
    main()
```

Run: `python3 Tools/n8n/report-learning.py --selftest && python3 Tools/n8n/report-learning.py --days 1`
Expected: `selftest OK`; таблица по staging (e2e-прогон чистит свои профили в finally — для живых строк выполнить руками один `set_recorded`-подобный вызов через хелперы `e2e-suggest.py` без cleanup, либо смотреть таблицу за окно, пока прогон идёт); `top1` считается, `sets` ≥ 1 у профиля с набором.

- [ ] **Step 2: Политика §3**

В `docs/legal/privacy.html` заменить строки 71–72:

```html
  <li><b>Статистика использования подсказок</b> в режиме «Вместе»: какие варианты ответа
      Приложение предложило и какой из них (или свой текст) вы отправили. Хранится вместе с историей
      чата вашего бота, используется для ранжирования подсказок и оценки готовности режима «Авто»,
      удаляется вместе с ботом; срок хранения — до 180 дней.</li>
```

Run: `grep -n "180 дней" docs/legal/privacy.html`
Expected: одна строка. Публикация страницы на сайте — шаг владельца (`Tools/landing/tools/deploy.sh`, сначала dry-run).

- [ ] **Step 3: Документация**

`Tools/n8n/README.md` — в список инструментов аудита добавить строку:

```
- **Learning A (2026-09-25, `docs/superpowers/specs/2026-09-25-learning-loop-foundation-design.md`):** `apply-learning-a.py` (Suggest Replies: stage в Gate Decision, ХОЛОДНЫЙ СТАРТ + общее правило ПОСТОРОННИЙ ВОПРОС в Assemble, `Has Set?` → `Record Set` ПОСЛЕ Respond; Sweep: `Expire Learning Rows` 180 дней), удаление наборов — CTE `sets` в `Delete Chat Memory` (`apply-history-handoff.py`) + счётчик в `Deletion Settled?` (`apply-auth-gate.py`), `verify-learning-a.py`, SQL `sql/2026-09-25-learning-foundation.sql`, staging `staging/e2e-learning.py` (8 сценариев), `report-learning.py` (только чтение). Прокси: секция `meta` (origin_*) в `build-auth-workflows.py` — валидируется, в Wappi не уходит, пишется `Record Owner Send`.
```

`CLAUDE.md` — в раздел «### n8n (Workflow Automation)» после абзаца про `/webhook/SuggestReplies` добавить:

```
  - **Learning A — «показано → отправлено» (2026-09-25):** каждая отправка из приложения несёт в `message.send`/`message.reply` опциональную секцию `origin_*` (kind card|edited|typed|none, index, key, edit 0–100, top1 0|1, set; `message.reply` ещё и `chat_id`, потому что клиент не шлёт recipient для TG-ответа и до этого `chat_id` в истории был пустым) — валидируется allow-list'ом `meta` в Wappi Proxy (`build-auth-workflows.py`), НИКОГДА не уходит в Wappi, пишется `Record Owner Send` в `chat_history_events.origin_*`; клиент считает её чистым `SendOriginResolver` из снимка `SendOriginTracker` (панель: `SetShown` при рендере, `SetPick` при тапе, `Clear` при новом раунде и при скрытии панели без тапа; отправка: `ResolveAndClear` ДО синхронного `OnLiveMessagesReceived`-эха оптимистичного пузыря — `HandleLive` → `StartFreshRound` → `Clear` в том же стеке; метка сохраняется в `OutboxEntry.originJson`, повтор несёт её же). Панель пишет показанный набор в `suggestion_sets` узлом `Record Set` ПОСЛЕ `Respond` (после ответа, но внутри 20-с бюджета execution — best-effort у границы); стадия `learning|trained` (< 10 отправленных выборов) считается в `Gate Decision` и включает ХОЛОДНЫЙ СТАРТ в Assemble (≥3 разных хода, без ПРЕДПОЧТЕНИЙ). Счётчик `{botName}SuggestPick{move}` пишется при УСПЕШНОЙ отправке (`SuggestionPickStats`), не при тапе, и кредитуется боту, на котором отправляли (`SendOrigin.botId`). Правило «Границы темы» (три класса + честный ассистент; спам — одно слово, НЕ пустой ответ: пустой output = generation_failed) живёт в `_core.md` БЕЗ плейсхолдеров (V10 классифицирует голову клона по содержимому — подстановка сделала бы каждый клон `custom`): имя бизнеса едет в хвосте как строка «Название: …» под «About Business:» (`Manager.ComposeBusinessKnowledge` с именем бота; старые боты получают её при следующем сохранении настроек), а `_universal.md` разрешает честный ответ на прямой «вы бот?»; в панели «ровно одна карточка «Отказ» на посторонний вопрос» — ОБЩЕЕ правило Assemble (маркер `LEARNING-A-OFFTOPIC-RULE`), не ниша: ниша уступает на форме вывода, а число карточек — форма. ВОЗДЕРЖАНИЕ панели не трогается (V9 + владельческий гейт `N2_banter_abstain`). Стражи: `SendOriginWiringTests` (source), `verify-learning-a.py`, `staging/e2e-learning.py`. Удаление: CTE `sets` внутри `Delete Chat Memory` (`apply-history-handoff.DELETE_NEW_TAIL`, та же линия бота) + счётчик `deletedSuggestionSets` в `Deletion Settled?`/`Respond` (`apply-auth-gate.DELETION_SETTLED`) — после `Respond` ничего не выполняется (V12); retention 180 дней — `Expire Learning Rows` в Profile Lifecycle Sweep (не подчиняется dryRun). Eval: только интерлив-сравнения (`--side`), каждый Авто-сценарий классифицирован в `eval/test_checks.py`.
```

`docs/audits/implementation-progress.md` — в конец:

```
## Learning A «Фундамент» — реализовано, ждёт go на деплой (2026-09-XX)
Правило «Границы темы» + eval v1.7 (before/after: <числа из results/ot-a-*.json>), панель «Отказ»/холодный старт, origin_* через meta прокси, suggestion_sets после Respond, CTE sets в Delete Chat Memory, Expire Learning Rows, клиент SendOrigin*/SuggestionPickStats, privacy §3. Staging: e2e-learning 10/10, e2e-suggest 6/6, e2e-auth/e2e-handoff без регрессий; Unity <total>/<total>.
```
(даты и числа — фактические на момент записи.)

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/report-learning.py docs/legal/privacy.html Tools/n8n/README.md CLAUDE.md docs/audits/implementation-progress.md
git commit -m "docs(learning-a): отчёт report-learning.py, privacy §3, README/CLAUDE.md, журнал

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 5: Чек-лист деплоя на прод (НЕ выполнять без явного «go» владельца; выполнять в этом порядке)**

1. `python3 Tools/n8n/.secrets/sql-oneoff.py "$(cat Tools/n8n/sql/2026-09-25-learning-foundation.sql)"` → post-checks из файла (по одному запросу; харнесс отдаёт Response Data вебхука — если приходит только первая строка, это дефолт firstEntryJson, а не «1 row»).
1a. В `Tools/n8n/.secrets/sql-oneoff.py` (вне репо) заменить `print(s, b[:1500])` на `print(s, b)` — иначе `report-learning.py --prod` получит обрезанный JSON.
1b. Репетиция на staging с прод-состояния (T12): `python3 Tools/n8n/staging/bootstrap.py --canon-ref <sha прода> --bot wa:… --bot tg:…` → `deploy-canonical.py --base http://localhost:5678 …` dry-run = ровно пакет → `--apply` пошагово → `update-clones.py` → `verify-deployed.py --base …` exit 0; затем `derive-staging.py` без импорта и e2e.
2. SQL ОБЯЗАТЕЛЬНО раньше прокси и раньше Delete Bot Files: `Record Owner Send` fail-open, и INSERT в несуществующие колонки молча остановил бы запись owner_app + memory; а `Delete Chat Memory` без таблицы `suggestion_sets` падает → `Deletion Settled?` false → 503 → у клиента Retry (бот остаётся на телефоне, ничего не теряется, но удалить бота нельзя до миграции). Затем `python3 Tools/n8n/deploy-canonical.py --only crWappiProxy0001 --only 9PTyYcelRQI7bGDb --only lmjYsdNcQA2IE5rl --only fXYpCXPKw92EzRz8 --only XuvOp7TxOImOAmlj --only Uz6HBBUpAiUqVysB --only 3qax5J9u2qsT9Vao --only TwWPW3gIyjZS3foR` (dry-run, читать diff) → та же команда с `--apply --yes` (D1: активный workflow публикуется своим PUT, read-back обязателен).
3. `python3 Tools/n8n/verify-deployed.py --json docs/audits/<date>-prod-readback.json` (read-back) + `python3 Tools/n8n/verify-learning-a.py` по локальному канону (флага `--dir` у verify-deployed нет).
4. `python3 Tools/n8n/update-clones.py` ОБЯЗАТЕЛЬНО: `_core.md`/`_universal.md` изменились → головы всех клонов `behind` (V10), иначе живые боты остаются на старом правиле. На 2026-10-13 живых клонов на проде нет — команда отработает вхолостую, но шаг в пакете остаётся.
5. Сайт: `Tools/landing/tools/deploy.sh` (dry-run → live) с обновлённой `privacy.html` ДО выпуска клиента с меткой.
6. Клиент: TestFlight/internal-track сборка; первый прогон `python3 Tools/n8n/report-learning.py --prod --days 3` через 2–3 дня работы бота владельца.
