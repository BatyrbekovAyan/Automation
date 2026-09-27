# D1 + D2 — проверочные инструменты развёртывания (итерация 2026-09-27)

Область: `Tools/n8n/verify-deployed.py` и функции сравнения клонов в `Tools/n8n/update-clones.py` (D1),
`Tools/n8n/verify-telegram-parity.py` (D2). Канонические workflow и генераторы не менялись: дефект был в самих
проверках. Прод, staging n8n, Wappi, RevenueCat и OpenAI не вызывались. Коммитов нет, так требует полоса:
список файлов для коммита — в конце.

## D1 — `verify-deployed.py` / `update-clones.py`

### Что было не так (подтверждено на HEAD `dbbeb0b1`)

- Клон считался актуальным только по sticky-заметке `Template Version`. У клона, который приложение создаёт через
  Create WA/TG, заметки нет, поэтому он навсегда попадал в BEHIND («23» на проде). Обратный случай тоже не ловился:
  правильная заметка поверх изменённого графа давала AT-CANONICAL.
- Архивный клон («13») считался отставшим, и гейт давал exit 2 постоянно. 24.09 такой красный результат приняли
  как норму.
- Опубликованную версию клона не проверяли вообще. У канонических workflow сверялись только `activeVersionId == versionId`,
  а сам опубликованный граф не сравнивался.
- `SUBSTITUTED` разрешал любые отличия графа у Support Message и Set Workflow State, включая connections и typeVersion.
  В итоге обход Auth Gate в SWS и устаревший список PROTECTED давали OK.
- `active` попадал в отчёт, но на итог не влиял: неактивный Wappi Proxy давал OK.
- `graph_hash` не учитывал `disabled`/`onError`/`alwaysOutputData`/`retry*`/`executeOnce`, поэтому узел, выключенный
  руками, давал OK. Непривязанные плейсхолдеры (`__CHAT_ID__`, `__HDR_CRED__`) тоже не обнаруживались.

### Что сделано

**Одно определение графа** (`update-clones.py`). Его используют все три инструмента, включая `deploy-canonical.py`
через `vd.graph_hash`:
- `node_signature(n)` — name, type, typeVersion, parameters и поведенческие ключи со значением не по умолчанию.
  Id узлов, позиции, credentials и webhookId в подпись не входят.
- `graph_stamp(wf)` — хеш подписей и connections. Идентичность клона обнулена: `Webhook.path` и
  `AI Agent.options.systemMessage`. `template_stamp = graph_stamp`, значение sticky теперь просто метка.
- `published_graph(full)` — граф, который n8n реально обслуживает (`activeVersion.nodes/connections`). Проверено по
  исходникам n8n 2.36.7: `GET /workflows/{id}` делает `includeActiveVersion: true`. Если `activeVersion` в ответе
  нет, используется запасной путь `activeVersionId == versionId`.
- `clone_state(full, tpl_stamp)` возвращает `graph_stamp`, `published_stamp`, `stamp` (что утверждает заметка),
  `stamp_consistent`, `at_canonical`, `published_at_canonical`, `up_to_date`, `archived`.
- В `update-clones.py` решение «up to date» теперь принимается по графу, то есть по сохранённому и опубликованному.
  Dry-run печатает «sticky claims X but the graph is Y» и «stored graph is canonical but the PUBLISHED one is …».
  `node_diff` использует ту же подпись. **Путь применения не менялся**: PUT, deactivate/activate, `verify_after`.

**`verify-deployed.py`:**
- `SUBSTITUTIONS = {Support Message: [(Send To Support Chat, chatId, __CHAT_ID__)]}`. Живое значение возвращается к
  плейсхолдеру до хеширования, поэтому любое другое отличие даёт DIFF. Вне staging подстановка **обязательна**:
  пустое значение или плейсхолдер даёт DIFF. Для Set Workflow State исключения нет вообще: его `__HDR_CRED__` —
  это id credential, а credentials в хеш не входят.
- Неподставленные `__PLACEHOLDER__` в credentials.id и parameters, как в черновике, так и в опубликованной версии,
  вне staging дают DIFF.
- Контракт активности: активны все канонические workflow, кроме двух шаблонов (`EXPECTED_INACTIVE` выводится из
  `uc.TEMPLATES`, тот же контракт, что `ACTIVE_CANONICAL` в `staging/bootstrap.py`). Неактивный обязательный
  workflow даёт DIFF, включённый шаблон — тоже DIFF.
- Канонический workflow в архиве даёт DIFF. Опубликованный граф, отличный от канона, даёт DIFF («PUBLISHED graph
  differs»). Черновик, изменённый только косметически (например, сдвинуты позиции), при каноническом опубликованном
  графе даёт OK.
- Клоны: вердикт `up_to_date` определяется по графам. Архивные клоны выводятся отдельным списком `archived` и в exit
  не учитываются. Неактивный неархивный клон учитывается: владелец включит канал, и опубликуется сохранённый граф.
- Любая ошибка даёт exit 2, как и раньше. Инструмент по-прежнему ничего не записывает.

**Что изменено относительно исходного fix_design, по замечаниям опровергающего:**
- Опубликованная версия сравнивается **по графу** `activeVersion`, а не только по равенству id.
- Проверено, что исправление не даёт ложный красный на штатных сценариях: свежий TG-клон с новыми id узлов и
  credential, бот после Edit (другой systemMessage и settings), выключенный клон, staging-база, где Support Message
  намеренно не привязан.

### Поведенческий тест: `Tools/n8n/tests/test_verify_deployed.py`

Работает офлайн. Фейковый n8n собирается из `Tools/n8n/workflows/*.json` и фактов прода из
`docs/audits/2026-09-25-review-evidence/production-readback.json`. Подменяются `list_workflows` и `call` у
**настоящих** инструментов. Любой не-GET вызов падает, поэтому тест не может «пройти» за счёт записи. Всего 25 случаев:
- позитивные:
  - прод 25.09: «23» без sticky — AT-CANONICAL, «13» (граф 33 узла из `95221844`) — в `archived`, exit 0;
  - свежий TG-клон, отредактированный WA-бот и выключенный клон;
  - косметический черновик;
  - staging-база;
  - `deploy-canonical` видит здоровый набор как up to date;
- негативные, все должны давать exit 2 и помечать именно целевую строку:
  - поддельная правильная sticky поверх изменённого графа;
  - неактивный Wappi Proxy;
  - включённый шаблон;
  - другой опубликованный граф — у клона и у канонического workflow;
  - лишняя правка в Support Message;
  - перепривязка connections Support Message, скрытая раньше за SUBSTITUTED;
  - `__CHAT_ID__` не подставлен;
  - обход Auth Gate в SWS;
  - устаревший PROTECTED;
  - узел `disabled`;
  - непривязанный `__HDR_CRED__`;
  - архивный канонический workflow;
  - живой клон на старом графе (исключение для архива действует не на всех);
- update-clones:
  - свежий «23» → up_to_date;
  - дрейф под sticky → would_update;
  - устаревший опубликованный граф → would_update;
  - результат `build_target` → up_to_date (без бесконечного would_update);
  - архивный клон пропускается;
  - `deploy-canonical`: отключённый руками узел → would_update.

| | Результат |
|---|---|
| До исправления (`evidence/D1-before.txt`, файлы инструментов = HEAD) | **5 pass, 20 fail**, exit 1 |
| После (`evidence/D1-after.txt`) | **25 pass, 0 fail**, exit 0 |

## D2 — `verify-telegram-parity.py`

### Что было не так

- Проверка требовала старую связь `Delete Refused Profile → Respond Channel Limit`. После хотфикса 94374500 в
  цепочке есть `Retire Refused Profile`, поэтому скрипт был красным на каноне (exit 1).
- `main()` останавливался на первой ошибке, и 8 последующих проверок не выполнялись. Это касалось и
  TG-мутаций: до проверки TG дело не доходило.
- Проверка ничего не различала. Она была красной на любом входе, а на **полном откате хотфикса** (Create WA/TG
  из `94374500^`) становилась **зелёной**.

### Что сделано (только `verify-telegram-parity.py`)

- **Полный контракт `Retire Refused Profile`:**
  - не отключён, postgres/executeQuery, `onError = continueRegularOutput`;
  - выдаёт элемент при 0 строк (`_emits_on_zero_rows`). Это зеркало правила n8n 2.36 `isSelectQuery` и
    `findTopLevelCommand`: CTE `with … select` при 0 строк ничего не выдаёт;
  - один оператор `update bot_profiles set … where …`;
  - в WHERE обязательны `profile_id = nullif($1,'')`, `deleted_at is null`, `workflow_id is null`;
  - в WHERE запрещены ` or `, `app_user_id`, `owner_id`, подзапросы и `in (`;
  - в SET обязательны `deleted_at = now()` и `deleted_reason = 'channel_limit'`;
  - `queryReplacement` — ровно поле профиля своего канала;
  - credential закреплён через `POSTGRES_NODE_NAMES`.
- **Связи цепочки:**
  - `If Slot Limit` имеет два выхода, `[0] = [Delete]`;
  - в каждой паре цепочки ровно один выход и одна цель: `Delete → Retire → Respond`;
  - `Respond` — терминальный узел;
  - из ветки превышения лимита достижима **только** цепочка отказа, то есть отказ не доходит до создания workflow;
  - из разрешённой ветки узлы цепочки недостижимы;
  - у каждого узла цепочки единственный родитель.
- Контракт `Delete Refused Profile` и тела отказа сохранён. Для `Respond` добавлена проверка «не отключён».
  Отсутствующий узел цепочки теперь называет файл.
- **Все проверки выполняются всегда.** На каждую упавшую выводится `PARITY FAIL: [метка] …`, в конце —
  `PARITY FAILED: n of 14`. `ALL PARITY ASSERTS PASSED` печатается только если всё зелёное.
- **`--self-test` [`--dir`]:**
  1. Копирует каталог во временную папку и проверяет, что немутированная копия проходит **все** проверки.
  2. По одному применяет 16 обходов к Create WA и к Create TG: старая связка без Retire; Delete → Respond в обход
     Retire; Retire → Retire Same Bot Slot (отказ уходит в создание workflow); разветвление; превышение лимита ведёт
     в создание; Retire отключён; Retire без onError; выход ошибок в создание; WHERE без `workflow_id is null`;
     WHERE по владельцу; Retire на разрешённой ветке; CTE-select; поле другого канала; dev-credential; у Respond
     появилась исходящая связь; Respond с includeOtherFields.
  3. Каждый обход должен уронить проверку, которая называет этот файл.

  Результат: 33/33, exit 0.

### Поведенческий тест: `Tools/n8n/tests/test_verify_parity.py`

- Настоящий верификатор запускается подпроцессом через `--dir` на копиях:
  - канон → exit 0;
  - ранний дефект (Telegram-шаблон) и поздний (Suggest Replies) → оба в выводе;
  - `--self-test` → exit 0;
  - **откат хотфикса из git** → exit 1;
  - 15 независимых от self-test мутаций × WA/TG. Каждая засчитывается, только если сам канон при этом зелёный:
    верификатор, красный на всём, ничего не обнаруживает.
- Настоящий SQL `Count Channels` и `Retire Refused Profile` из канона выполняется на временных копиях
  `bot_profiles`/`subscribers` в локальном staging Postgres (BEGIN…ROLLBACK):
  - с Retire: отказанный p2 списан, после удаления бота 1 новый канал принят (`c=0`). Без Retire, то есть в старой
    связке, «призрак» p2 блокирует законный канал (`c=1`);
  - просроченный владелец повторно отправляет Create для живого привязанного p1, Wappi-delete не прошёл, затем
    владелец продлевает «Старт». Канон: p1 считается, новый канал отклонён (`c=1`). Без `workflow_id is null`:
    слот p1 освобождается, новый канал принят (`c=0`) — два живых канала на тарифе с одним.

| | Результат |
|---|---|
| До (`evidence/D2-before.txt`: сам верификатор exit 1 + тест) | **2 pass, 34 fail**, exit 1 (прошли только две SQL-предпосылки) |
| После (`evidence/D2-after.txt`: верификатор, `--self-test`, тест) | верификатор `ALL PARITY ASSERTS PASSED`, self-test 33/33, тест **36 pass, 0 fail** |

**Уточнение к выводам проверяющего.** Его SQL-сценарий S4 («Retire на разрешённой ветке между Register и Bind → обход
лимита») моделировал Bind Workflow Id как `update … set workflow_id`. Настоящий `Bind Workflow Id` делает
`on conflict … deleted_at = null` и тем самым отменяет такое списание. Поэтому вред от Retire на разрешённой ветке
реален, но ýже заявленного:
- окно, в котором параллельный Create видит заниженный `c`;
- случай, когда Bind не выполнился;
- неверная метка `deleted_reason`.

Структурный запрет всё равно остаётся: эта мутация ловится проверкой достижимости. В SQL-тест включены только точные
сценарии.

## Прогнанные проверки

- `python3 Tools/n8n/tests/test_verify_deployed.py` → 25/25.
- `python3 Tools/n8n/tests/test_verify_parity.py` → 36/36.
- `python3 Tools/n8n/verify-telegram-parity.py` → ALL PARITY ASSERTS PASSED; `--self-test` → 33/33.
- `python3 Tools/n8n/apply-slot-count-fix.py --check` → OK для WA и TG (генератор хотфикса согласен с каноном).
- `python3 Tools/n8n/tests/test_harness_selfcheck.py` → 2/2; `py_compile` всех изменённых файлов.
- `check_suggest_replies()` вызывается внутри процесса так же, как в плане learning-loop, и работает.

**Честная пометка.** Один раз я по ошибке запустил `apply-slot-count-fix.py --help`. У скрипта нет argparse, поэтому
он отработал в режиме записи. Скрипт напечатал `up-to-date` для обоих Create и ничего не изменил: `git status` по
`Tools/n8n/workflows/` чистый.

## Остаточные ограничения и что нужно от других

1. **Проверка на проде только чтением.** Нужно разрешение владельца на один прогон `verify-deployed.py`, только GET.
   Ожидается exit 0: «23» AT-CANONICAL, «13» в archived, Support Message OK. Риски первого прогона:
   - на проде может остаться неподставленный `__TG_CRED__`;
   - n8n может нормализовать поведенческие ключи при round-trip.

   Если хоть один из них проявится, гейт честно покажет DIFF, и это нужно будет разобрать, а не глушить.
2. **Приёмка на staging (не моя полоса).** Нужен `Tools/n8n/staging/e2e-clone-update.py`: повторный запуск после
   apply обязан дать `up_to_date: 1` уже по графу, после настоящего PUT и публикации в n8n. Это единственная
   проверка того, что round-trip n8n сохраняет подпись и не возникает бесконечного would_update. Офлайн это покрыто
   случаем `build_target`.
3. **`deploy-canonical.py` теперь различает и поведенческие ключи** (общий `graph_hash`). Узел, отключённый руками,
   попадёт в would_update, и PUT вернёт канон. Это намеренно. Сам файл не менялся.
4. **Sticky-значение сменилось** (`5a8d64886018` → `1e1d6c1b1b15` для WA): теперь это `graph_stamp`. Клонов со
   sticky на проде нет. Клон со старой заметкой, но каноническим графом получает up_to_date с пометкой
   «sticky claims …».
5. **Не сравниваются `settings` канонических workflow** (замечание опровергающего): `executionTimeout: 20` у
   Suggest Replies, `saveDataSuccessExecution: none` у Wappi Proxy и Test Dialog. Их дрейф по-прежнему даст OK.
   Предлагаемая доработка: сравнивать подмножество ключей из схемы публичного API (`executionTimeout`, `saveData*`,
   `saveManualExecutions`, `executionOrder`, `callerPolicy`), которые есть в канонических settings. `binaryMode`,
   `credentialResolverId` и `availableInMCP` игнорировать: API их отбрасывает или не принимает. Включать это стоит
   только после прогона на проде только чтением. В этот раз не сделано, чтобы не рисковать ложным красным гейтом без
   данных прода.
6. **`verify-telegram-parity.py` не проверяет первую половину хотфикса** — исключение `$4` в `Count Channels`. Её
   покрывает `apply-slot-count-fix.py --check`. Режим `--dir` против прод-экспорта по-прежнему падает на прод-id
   credential: это известное отдельное ограничение.
7. **Документы вне полосы уже устарели** и должны обновиться в группе D3/документации:
   - `Tools/n8n/README.md:322-330` («Idempotent by stamp», описание verify-deployed);
   - `docs/audits/release-package.md` строки 25, 53–55, 69 (ожидания гейта: exit 0, архивные отдельно);
   - оговорка в плане learning-loop («полный прогон parity не считать критерием») больше не нужна.

## Файлы (не закоммичены — полоса запрещает коммит)

- `Tools/n8n/update-clones.py`, `Tools/n8n/verify-deployed.py`, `Tools/n8n/tests/test_verify_deployed.py`
  - предлагаемый коммит: `fix(n8n-tools): D1 — verify-deployed/update-clones судят по реальному и опубликованному графу, SUBSTITUTIONS, контракт активности`
- `Tools/n8n/verify-telegram-parity.py`, `Tools/n8n/tests/test_verify_parity.py`
  - предлагаемый коммит: `fix(n8n-tools): D2 — parity под цепочку хотфикса, все проверки без остановки, --self-test`
- `docs/audits/2026-09-27-iteration/evidence/D1-before.txt`, `D1-after.txt`, `D2-before.txt`, `D2-after.txt`,
  `docs/audits/2026-09-27-iteration/D1-D2-tooling.md`
