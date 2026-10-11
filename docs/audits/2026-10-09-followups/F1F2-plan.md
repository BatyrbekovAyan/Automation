# F1 + F2 + Q1 B + V6-unknown + V3-журнал — сводный план реализации

Дата: 2026-10-11. HEAD `92a7caa2` (после выкладки M4 + V9-r3 + V6-concurrent). Статус: **план**. Код продукта, канон, staging и
прод не менялись. Выкладка блока — только по новому явному «да» владельца.

Источники: `README.md` этой папки (решения владельца 2026-10-09 и 2026-10-11), `F1-design.md`, `F2-design.md`, `M4-design.md`,
`Q1-design.md` (+ `Q1-eval/`, `evidence/Q1c-measure.txt`, `wip/README.md`), `docs/audits/2026-10-11-independent-review.md`
(V6-concurrent и «send_unknown не сообщается модели», остаток V3 first-bind, F1/F2), CLAUDE.md (A1 правила 1–6, A2 R8 и
R8-deletefail, A3 (5) V6, A5/A6, V10, V12, M4, Q1), текущие генераторы `Tools/n8n/*.py`, тесты `Tools/n8n/tests/`, staging
`Tools/n8n/staging/`, клиент (`Manager.cs`, `BotSettings.Auth.cs`, `Bot.cs`, `Auth/ProfileRetirement.cs`, `Billing/`).

---

## 0. Коротко

**Что делаем одним блоком.**

- **F1 + F2 — один механизм «линия бота».** Create WA/TG становится операцией «обеспечить ровно один привязанный клон канала
  бота». Тот же профиль — переиспользовать привязанный клон. Новый профиль того же бота (смена профиля, переподключение после
  оплаты) — создать новый клон, привязать его, одним оператором перенести на него всё, что было у бота на сервере, и удалить
  заменённый клон. Любой сбой после Create — удалить новый клон и ответить без `id`.
- **Q1 B + V6-unknown — один механизм «пометки памяти для модели».** Писатели памяти ставят в начало строки:
  - «Владелец: » — для реплик владельца (owner_app, owner_phone);
  - «[доставка не подтверждена] » — для ответов бота с `delivery = unknown`.

  Фраза в «Память» объясняет модели обе пометки. Страж не даёт боту самому написать пометку ни клиенту, ни в свою память.
- **V3 — журнал конфликтов привязки платёжного ID.** Нужен поддержке, чтобы видеть `billingConflict` и отзывать чужую установку
  по существующей процедуре раннбука.

**Порядок.** Сервер — строго последовательно, потому что генераторы общие: T1 → T2 → T3 → T4 → T5 → T6 → T7, затем staging
(T10, T11), документы и репетиция (T12). Клиент (T8 → T9) идёт параллельно с первого дня: контракт Create закреплён в §1.7.
Выкладка — сервер раньше клиента. Новая клиентская сборка с HEAD после блока, затем device-pass.

**Объём.** 12 обязательных задач, ≈ 67–80 ч (≈ 8,5–10 рабочих дней) плюс device-pass 3–4 ч с владельцем. Опционально: F1 Tier 2
(6–8 ч) и отвязка удалённого профиля через прокси (2 ч). Платный замер ≈ $3–6.

**Ключевые решения по противоречиям проектов** (подробно — §1.2):

1. Для нового профиля всегда новый клон, а не PUT-оживление прежнего. Перенос данных одним оператором, удаление заменённого
   после положительной привязки (решение владельца по F1).
2. Перенос данных F2 (переписка, память, сводка, заявки, флаги, прайсы) делается для любого доказанного предшественника, а не
   только после оплаты. Это закрывает и F1 Q7 («данные прежнего профиля неудаляемы»).
3. «Тот же бот» доказывает БД: установка, канал, `bot_key` или явный `PreviousProfileId`. Новый бот из мастера
   (`CreateOrigin=start`) предшественника не ищет никогда. Удаление бота стирает всю его линию.
4. Клиент переносит id удалённого канала в retired-ключи (F2) только в той же сборке, где удаление читает retired-id (V12).

---

## 1. Сверка проектов F1 и F2

### 1.1 Где проекты пересекаются

| Общий элемент | F1 | F2 | В плане |
|---|---|---|---|
| Смысл Create | «обеспечить клон профиля», идемпотентно | Create + предшественник по `PreviousProfileId` | **один** генератор `apply-clone-lifecycle.py` на оба случая (рекомендация обоих проектов и решение владельца №1) |
| Источник предшественника | `workflow_id` строки того же профиля (`own`) и строк того же бота (`prior`, по `bot_key`) | удалённая строка `PreviousProfileId` | единое правило §1.3 |
| Перенос прайсов | `documents.botWaId/botTgId` и `uploaded_files` в операторе Bind | то же в `Adopt Predecessor` при смене id клона | в одном операторе `Bind Workflow Id` (§1.5) |
| Пометка прежней строки | `superseded_by`, `deleted_reason = coalesce(…, 'replaced')` | `superseded_by`, `superseded_at` | `superseded_by` + `superseded_at`, причина через `coalesce` (§1.2, п. 6–7) |
| Удаление прежнего клона | `Delete Replaced Clone` после `Bound?` | `Remove Superseded Clone` после Adopt | один узел после `Bound?`, страховка — журнал `clone_retirements` |
| `Restamp RAG Chunks` | вливается в Bind (−1 узел PG) | остаётся | вливается в Bind (правило `'-1'` второго канала сохранено) |
| Принципы | владение по БД (V12), положительный признак (R8, F3), R8-инвариант, защищённые id | те же | закреплены верификатором (T4) |
| Миграция | `superseded_by` + таблица `clone_retirements` | `superseded_by`, `superseded_at` | одна миграция M2 (§2) |

### 1.2 Противоречия и единое решение

| # | Вопрос | F1 | F2 | Решение | Почему |
|---|---|---|---|---|---|
| 1 | Клон для **нового** профиля того же бота | новый клон, перенос чанков, удаление прежнего | PUT шаблона в прежний workflow id (оживление); запасной путь — новый клон | **Всегда новый клон.** Привязать, перенести данные, внести прежний в `clone_retirements`, удалить | (а) Решение владельца по F1: заменённый клон удаляется после успешной привязки. (б) Запасной путь F2 — это и есть путь F1: PUT в удалённый или архивный клон n8n отклоняет (как с «13»). Сделав его единственным, мы убираем из графа и тестов второй путь (`Pause/Update Predecessor Clone`, `Clone Updated?`). (в) Перенос чанков и файлов, ради экономии которого F2 выбрал PUT, — один CTE в уже существующем операторе. (г) Вместе с клоном n8n удаляет его исполнения с текстами клиентов, а это соответствует обещанию «удалить по запросу» |
| 2 | Клон для **того же** профиля (повторная авторизация) | переиспользовать привязанный клон после пробы GET | не рассматривается: профиль после чистки всегда новый | **Переиспользовать** (F1 Q2): проба `GET` → `statusCode 200`, не архив, путь `Webhook` равен профилю → `Activate` (для активного это нулевая операция) | Нет конфликта путей (`WebhookPathTakenError`), нет сироты. Custom-голова V10 сохраняется. Ничего переносить не надо |
| 3 | Где в графе | `Clone Plan` сразу после `Seed Saved?[0]`, до `Get Sample` | `Reconnect?` после `Set Fields`, чтобы `Seed Saved?[0]` оставался единственным предшественником `Get Sample` | **Как в F1**, правило R8 переформулировано: `Create Workflow` и `Activate Created Workflow` достижимы только через `Seed Saved?[0]` | При переиспользовании шаблон не читается. Сбой плана останавливает граф до любого создания. Смысл R8-deletefail («сбой seed не создаёт и не активирует клон») сохраняется и проверяется на достижимость |
| 4 | Как найти «тот же бот» | по БД: та же установка, канал, непустой `bot_key`, другой профиль | явный `PreviousProfileId`; `bot_key` — только страховка; привязку по одному `bot_key` F2 отверг | **Объединение**, всегда с доказательством в БД (§1.3): `bot_key` совпал **или** профиль назван `PreviousProfileId`; при `CreateOrigin = start` (мастер, новый бот) поиск не делается вовсе | Довод F2 против одного `bot_key` — переиспользуемое имя слота. Его закрывают две вещи: новый бот из мастера не ищет предшественника, а удаление бота стирает всю линию (T5). `PreviousProfileId` нужен для строк без `bot_key` (созданных до него). Старые A1-клиенты без новых полей получают поиск по `bot_key`, как в F1 |
| 5 | Причины удаления предшественника | любая, кроме `app_delete` (включая NULL от `Retire Same Bot Slot`) | только `trial_expiry/churn_grace/liveness/channel_limit` | **любая, кроме `app_delete`**; строка не замещена другим профилем | NULL — это F1 (а3) (смена профиля), `replaced` уже замещена. `app_delete` — бот удалён владельцем, наследовать нечего |
| 6 | Что переносится | только чанки и `uploaded_files`; память — смежный Q7 «стирать при удалении» | весь набор `Delete Chat Memory` + при смене клона чанки и файлы | **Весь набор `Delete Chat Memory`** (память, история, сводка, заявки, `reply_events`, `chat_reply_state`, флаги кроме `'*'`) **+ чанки + `uploaded_files` для любого доказанного предшественника** | Решение владельца F2: переносить всегда. Тот же оператор закрывает F1 Q7 без отдельной «родословной» в удалении: «бот = его текущие id» (принцип F2 №3). Для смены номера при **том же** профиле (logout → повторная пара) память и сегодня остаётся под профилем, так что поведение единое. Набор таблиц — тестируемый инвариант (= набор `Delete Chat Memory`) |
| 7 | `superseded_by` | id нового **клона**; снимается при перепривязке | id нового **профиля** + `superseded_at`; `workflow_id` → NULL при оживлении клона | `superseded_by` = **новый профиль**, `superseded_at`. `workflow_id` прежней строки не трогается. Заменённый клон и новый id — в `clone_retirements(workflow_id, replaced_by)`. Перепривязка строки снимает `superseded_by` (F1) | Линия бота — это профили: память и история ключуются профилем. Клон между профилями больше не переходит (п. 1), обнулять `workflow_id` не нужно: по нему V12 и чистка видят клон до удаления |
| 8 | Причина прежней строки | `coalesce(deleted_reason, 'replaced')` | причина остаётся (`trial_expiry`) | `deleted_at = coalesce(…, now())`, `deleted_reason = coalesce(…, 'replaced')` | Совместимы: исходная причина важнее, NULL получает `replaced` |
| 9 | Журнал неудавшихся удалений | `clone_retirements` в той же транзакции, что привязка; ветка A чистки | нет (неудача DELETE = сирота) | **журнал F1** + ветка A чистки (T5) | Ни одного клона без записи |
| 10 | Ответ Create | `{id}` или тело `error` без `id` | `{id, adopted, reusedClone}` | `{id, created, adoptedProfiles: [...]}`; отказы — `{success:false, error}` **без `id`** (§1.7) | `FromEdit` проверяет `Contains("\"id\":")`. Список перенесённых профилей точнее булева флага: клиент чистит retired-ключи только у названного |
| 11 | Узлов Postgres на горячем пути | столько же (+`Clone Plan`, −`Restamp`) | +2 при переподключении (`Reconnect Predecessor`, `Adopt Predecessor`) | **столько же, сколько сейчас**: план (+1), Bind с переносом (= прежний Bind), `Restamp` (−1) | Каждый узел PG ≈ 1,9 с на проде |
| 12 | База API и учётка | localhost + `n8n Admin API` (`__HDR_CRED__`, привязывает `deploy-canonical.py`) | как у `Get Sample` (публичный адрес, `n8nAPIKey`) | **localhost + `__HDR_CRED__`** для `Get Sample`, `Create`, `Activate`, пробы и обоих DELETE | Убирает DNS и Caddy из горячего пути (довод опровергателя R8). Механизм привязки учётки уже работает для Sweep и Test Dialog |
| 13 | Отказ Create из настроек на клиенте | без изменений | любой отказ → id `-1`, `isOn` 0, retired-ключи не трогать | `channel_limit` (профиль сервер уже удалил) → `-1` + `isOn` 0. Любой другой отказ или нет ответа → **профиль сохраняется**, ставится `{bot}{Ch}CreatePending`, Create повторяется при следующей проверке авторизации (он теперь идемпотентен) | `-1` на спаренном профиле оставляет связанное устройство у Wappi без бота (часовая чистка сирот удаляет только неавторизованные) |
| 14 | «Сменить номер» на клиенте | follow-up Q6(2): перестать удалять клон | не рассматривает | **Делаем в блоке (T8)**: только logout, клон и его id остаются, Create переиспользует клон | Иначе гонка F1 §2.10: удаление «в огне и забыть» приходит после переиспользования, и бот молчит |
| 15 | Маркер M4 и id канала | — | перенос id в retired-ключи (текущие → `-1`) отменяет правило M4 «id не трогать» | **Перенос, как в F2**, но в той же сборке, где удаление читает retired-id, `HasServerTrace` их учитывает, а `DeleteBotLocally` их стирает | Иначе регрессия V12 (`NothingOnServer` → бот уходит с телефона, данные остаются). Перенос — одна точка вместо ~110 мест чтения id |
| 16 | V12 `Retired Clones` | не меняется | не меняется (замещённая строка с `workflow_id = NULL`) | Добавить `and bp.superseded_by is null` | `workflow_id` замещённой строки теперь сохраняется (п. 7). Клон уже в журнале, лишний deactivate не нужен |
| 17 | Legacy (compat-окно, 1.0 (4)) | переиспользовать только привязку без владельца; ничего не заменять и не переносить; отказ legacy-Create по профилю установки (Q3) | переподключение только в сессии | **Как в F1**, включая отказ `profile_owned` (Q3, +0,5 ч) | Без доказательства ничего не переносится; дыра compat-окна закрывается |
| 18 | Tier 2 F1 (бродячие клоны) | рекомендован, «только отчёт» неделю | — | опциональная задача T13, первая кандидатура на перенос | Без Tier 2 остаток — умершее исполнение между Create и Bind/Discard |

### 1.3 Единое правило «предшественник»

Предшественник — строка `bot_profiles` P, для которой одновременно:

1. вызов в режиме `session`, установка вызывающего не отозвана, `P.owner_id` — эта установка;
2. `P.channel` — канал этого Create, `P.profile_id ≠` новый профиль;
3. `CreateOrigin ≠ 'start'` (поле нового клиента; у старых клиентов его нет, тогда поиск разрешён, как в F1);
4. `P.bot_key = BotKey`, если оба непустые, **или** `P.profile_id = PreviousProfileId` (тогда `bot_key` либо пуст, либо совпал);
5. `P.deleted_reason is distinct from 'app_delete'` и `P.superseded_by is null or P.superseded_by = новый профиль` (повтор
   идемпотентен);
6. клон P (если он есть) не ведёт живая строка **другого** бота;
7. новый профиль — живая строка этой же установки и этого канала (её только что создал `Register Profile`).

Свой клон (`own`): `workflow_id` строки **нового** профиля, если привязка принадлежит вызывающему (в сессии — его установке, в
legacy — строке без владельца). Защищённые id (`build-client-webhooks.PROTECTED_IDS` + Set Workflow State + support relay) не
переиспользуются, не переносятся и не удаляются нигде: SQL, If-выражения, чистка.

`PreviousProfileId` в RULE гейта Create **не входит**. Это id удалённой строки, его нет в `p` токена, и доказывается он в
`Clone Plan` по БД (урок V12/M5).

### 1.4 Итоговая цепочка Create (WA; у TG те же имена узлов)

До `Seed Saved?` — без изменений, включая R8: отказ «Вместе» ничего не создаёт.

```
Seed Saved?[0] → Clone Plan (PG, retry ×3, continueRegularOutput, alwaysOutputData) → Plan Read? (planned === true)
  [1] → Respond Plan Unavailable {success:false, error:'clone_plan_unavailable'}
  [0] → Legacy Owned? (foreign === true) → [0] Respond Profile Owned {error:'profile_owned'}
                                          → [1] Reuse? (own_id валиден и не защищён)
        Reuse?[0] → Probe Bound Clone (GET localhost, neverError, fullResponse) → Bound Clone Usable?
              [0] → Reused Clone Id {id: own_id, created:false} → Clone Id
              [1] → Bound Clone Gone? (404 или архив) → [0] Get Sample … ; [1] Respond Clone Unavailable
        Reuse?[1] → Get Sample Workflow → Set Fields → Create Workflow → Get Created Workflow Id {created:true} → Clone Id
Clone Id → Activate Created Workflow (localhost, neverError) → Activated? → Set Wappi Webhook → Webhook Set? → Types → Types Set?
         → Bind Workflow Id (PG, один оператор §1.5, retry ×3) → Bound? (bound === true && workflow_id === Clone Id)
               [0] → Replaced Items → Has Replaced? → Delete Replaced Clone (DELETE localhost, neverError) → Send New Workflows Id
               [1] → Discard?
Любой «нет» после Clone Id → Discard? (created === true) → Discard New Clone → Discarded? → [1] Record Discard Failure (PG)
                                                                                         → Respond Create Failed {error:'create_failed'}
Send New Workflows Id (executeOnce): {id, created, adoptedProfiles}
```

Узлы, мутации и инварианты — как F1 §2.4 и §3 (E), с добавлениями плана: `Legacy Owned?`, `adoptedProfiles`, поле
`CreateOrigin`.

### 1.5 Оператор `Bind Workflow Id` (привязка + перенос, ВСЕГДА одна строка)

Параметры:

1. профиль;
2. id клона (`Clone Id`);
3. AppUserID;
4. канал;
5. установка;
6. своя прежняя привязка, если клон создан (`own_id`, иначе пусто);
7. JSON `replace_ids` из плана;
8. JSON `pred_profiles` из плана;
9. workflow id другого канала (правило `'-1'`).

Каждое условие предшественника из §1.3 **перепроверяется на записываемых строках**: урок V6-concurrent — проверять ту строку,
которую пишешь. Между планом и привязкой ≈ 10 с.

| CTE | Что делает |
|---|---|
| `bind` | upsert строки профиля → `workflow_id` = клон, владелец/claimed как сейчас, `deleted_at = null`, `superseded_by = null` |
| `p` | предшественники из `$8`, перепроверенные: установка, канал, не `app_delete`, `superseded_by is null` или = новый профиль |
| `mem` | `n8n_chat_histories.session_id` `<old>:<chat>` → `<new>:<chat>`. Id строк сохраняются: порядок памяти и водяные знаки R7/V6 верны (перенесённые строки ниже watermark любого живого прогона нового профиля) |
| `hist_*`, `outc_*`, `hand_*`, `ev_*`, `st_*` | `chat_history_events`, `conversation_outcomes` (+`session_id`), `handoff_requests`, `reply_events`, `chat_reply_state`: пара «перенести / удалить дубль». **Новая строка побеждает.** При двух предшественниках с одним ключом переносится одна (`distinct on`, самая свежая), остальные удаляются |
| `fl_*` | `reply_mode_flags`: чатовые строки — перенос/дубль; `'*'` прежнего профиля удаляется (`'*'` нового засеян Create из текущего режима клиента и авторитетен) |
| `chunks` | `documents.metadata.botWaId` (TG: `botTgId`) из `moved` (= `own_id`, если клон создан, + `replace_ids`) → новый клон; плюс правило `'-1'` второго канала (бывший `Restamp`) |
| `files` | `uploaded_files.bot_wa_id` (TG: `bot_tg_id`) из `moved` → новый клон, только при `owner_id` = установка |
| `superseded` | у строк `p` и у прежних строк с клоном из `moved`: `superseded_by` = новый профиль, `superseded_at`, `deleted_at = coalesce`, `deleted_reason = coalesce(…, 'replaced')` |
| `booked` | `clone_retirements (workflow_id, profile_id, owner_id, reason 'replaced', replaced_by = клон) on conflict do nothing` — **до** удаления |
| итог | `bound`, `workflow_id`, счётчики по таблицам, `adopted_profiles` (jsonb), `booked` |

`dialog_counts` не трогаются: это биллинг по `app_user_id`. В legacy `p` и `replace_ids` пусты, работают только `bind` и перенос
`own_id` (404 или архив). Сентинел-профиль даёт `bound = false`.

### 1.6 Что меняется вне Create

- **Profile Lifecycle Sweep:**
  - ветка A «Clone Retirements» (F1 §2.6 A): берёт строки журнала старше 10 мин, кроме id, которые ведёт живая строка, и
    защищённых; DELETE на localhost; 200/404 → `removed_at`, иначе `attempts + 1`;
  - в V12 `Retired Clones` добавляется `superseded_by is null`.
- **Delete Bot Files — удаление по линии бота.**
  - После `Owned?` вычисляется линия: все строки этой установки с `bot_key` названных строк, оба канала.
  - По ней: `app_delete`, тот же набор `Delete Chat Memory`, чанки и оригиналы по их клонам, клоны линии — в `clone_retirements`
    с причиной `bot_deleted`.
  - `Delete Ownership` (а значит, и Set Workflow State) не меняется: линия выводится из уже авторизованных строк этой установки.
  - Закрывает два случая: старые сироты до фикса (строка P1 с причиной NULL после `Retire Same Bot Slot`, клон WFold с чанками)
    и «переиспользованный слот после удаления бота».
- **Опционально (2 ч, вход из замечания M4):** прокси `profile.delete`/`profile.logout` по **своей удалённой** строке —
  `Build Request` пропускает `OWNER_OPS` к проверке `Profile Owner` (БД), а не режет по `p` токена. Тогда удаление бота с ложным
  liveness отвязывает живой профиль Wappi.

### 1.7 Клиентский контракт Create (закреплён до начала T3/T8)

- **Форма:**
  - новые поля `CreateOrigin` (`start` — мастер, `edit` — настройки) и `PreviousProfileId` (= `{bot}{Ch}RetiredProfileId` или
    пусто);
  - остальное без изменений;
  - старые клиенты новых полей не шлют.
- **Ответ** (HTTP 200, `lastNode`):
  - успех `{id, created, adoptedProfiles}`;
  - отказы `{success:false, error}` без `id`: `channel_limit` (профиль удалён сервером, как сейчас), `reply_mode_not_saved`,
    `clone_plan_unavailable`, `clone_unavailable`, `create_failed`, `profile_owned` (только legacy).
- **Классификация на клиенте** (чистый слой `CreateWorkflowResponse`):
  - `Success` — есть `id`;
  - `ProfileGone` — `channel_limit`;
  - `Retryable` — всё остальное и нет ответа.

  Таймаут Create — 45 с для обоих путей. Таймаут при уже выполненной сервером работе безвреден: повтор с тем же профилем
  переиспользует клон.

---

## 2. Схема данных: миграции одним списком

Все миграции аддитивные и идемпотентные (`if not exists`, `on conflict do nothing`), у каждой есть пост-проверка. Применяются через
штатный механизм раннбука (`.secrets/sql-oneoff.py` — только исполнителем выкладки, в план не читается).

| # | Файл | Содержимое | Когда применить | Кто читает |
|---|---|---|---|---|
| M1 | `Tools/n8n/sql/2026-10-xx-billing-conflicts.sql` | `create table if not exists billing_conflicts (app_user_id text, claimant_install_id text, holder_install_id text, first_seen_at timestamptz default now(), last_seen_at timestamptz default now(), attempts int not null default 1, primary key (app_user_id, claimant_install_id))`; индекс `(last_seen_at desc)`; RLS on; `revoke all … from anon, authenticated` | до Auth Session | `Log Billing Conflict` (T1), запрос поддержки |
| M2 | `Tools/n8n/sql/2026-10-xx-bot-lineage.sql` | `alter table bot_profiles add column if not exists superseded_by text, add column if not exists superseded_at timestamptz`; `create table if not exists clone_retirements (workflow_id text primary key, profile_id text, owner_id text, reason text not null check (reason in ('replaced','discard_failed','bot_deleted','inventory')), replaced_by text, created_at timestamptz not null default now(), removed_at timestamptz, attempts int not null default 0, last_status text)`; частичный индекс `(created_at) where removed_at is null`; индекс `bot_profiles (owner_id, channel, bot_key)`; RLS on, revoke | до Sweep, Delete Bot Files, Create | Clone Plan, Bind, Sweep (A и V12), Delete Bot Files |
| M3 | `Tools/n8n/sql/2026-10-xx-memory-marks-backfill.sql` | В `n8n_chat_histories` проставить префикс «Владелец: » строкам `type = 'ai'` с `source ∈ {owner_app, owner_phone}` без префикса; префикс «[доставка не подтверждена] » — строкам с `delivery = 'unknown'` без него. Обратный оператор (снять префиксы) — в том же файле, закомментирован | **после** выкладки шаблонов, прокси и Dashboard | память бота, классификатор Сводки |

Порядок: M1 → M2 → (workflow) → M3. Откат миграций не нужен: таблицы и колонки старый код не читает. M3 обратима своим оператором.
Если вернуть промпт, а строки оставить помеченными, получится состояние B1 из Q1 (пометка без фразы) — проба показала, что оно не
хуже прода.

---

## 3. Задачи

Формат: цель / файлы / тест сначала (что падает на HEAD) / готово, когда / зависит от / риски / оценка. «Генератор» = правка
только через генератор, затем второй прогон без диффа и `--check` 0. Каждая задача заканчивается коммитом своих файлов явными
путями, строкой в README этой папки и (для сервера) `tests/run_all.py` зелёным.

### T1. V3 — журнал конфликтов привязки (сервер) — 3–4 ч

- **Цель.** Каждый refresh, где Verify Install ответил `billing_conflict = true`, оставляет запись: кто просил, кто держит,
  первый и последний раз, сколько попыток. Ответ refresh не меняется ни байтом, задержки нет.
- **Файлы:**
  - `Tools/n8n/build-auth-workflows.py` → `crAuthSession001-Auth_Session.json`: ветка после ответа клиенту —
    `Conflict?` (If `billing_conflict === true`) → `Log Billing Conflict` (PG, `continueRegularOutput`, без retry), по образцу
    `Record Owner Send` после Respond в прокси;
  - `sql/…-billing-conflicts.sql` (M1);
  - `tests/test_billing_identity.py`, `verify-auth-gate.py`;
  - `docs/audits/a1-deploy-runbook.md` §5 — запрос поддержки и порядок решения.
- **Тест сначала.** `test_billing_identity.py` B12–B14 на настоящем SQL узлов: конфликтный refresh пишет одну строку
  (`claimant`, `holder` = держатель из `billing_bindings`); повтор увеличивает `attempts`; refresh без конфликта ничего не пишет;
  ошибка журнала (таблицы нет) не меняет ответ refresh. На HEAD: узла нет → B12 FAIL.
- **Готово, когда:**
  - B12–B14 зелёные, B1–B11 без изменений;
  - `verify-auth-gate.py` закрепляет: узел достижим только при `billing_conflict === true`, `onError = continueRegularOutput`,
    стоит после ответа;
  - `verify-sql-comments.py` OK;
  - в раннбуке — запрос и шаги («убедиться, что держатель мёртв или чужой → `revoked_at` → id перейдёт на следующем refresh»).
- **Зависит от:** ничего.
- **Риски.** Узел до Respond удлинил бы refresh на ≈ 1,9 с — поэтому он после ответа. Повторы запроса ограничены клиентом
  (≤ 1/мин при возврате в приложение), upsert по PK не даёт таблице расти.

### T2. SQL-ядро линии бота + миграция M2 — 7–8 ч

- **Цель.** Операторы `Clone Plan` (§1.3) и `Bind Workflow Id` (§1.5), запрос ветки A чистки и фрагмент «линия бота» для Delete
  Bot Files — в одном модуле. Каждый закреплён матрицей на настоящем Postgres.
- **Файлы:**
  - новый `Tools/n8n/clone_lineage.py`: константы SQL, список `PROTECTED` из `build-client-webhooks.PROTECTED_IDS`, таблица
    каналов WA/TG как в `apply-slot-count-fix.py`;
  - `sql/…-bot-lineage.sql` (M2);
  - новый `tests/test_clone_lineage_sql.py` (harness `PgSession`, temp-таблицы `like public.*` + M2).
- **Тест сначала.** Матрица требований гоняется и против текущих `Restamp RAG Chunks` + `Bind Workflow Id` канона (режим `--head`).
  На HEAD падает:
  - чанки и файлы остаются под прежним клоном (F2-8, F1 а1/а3);
  - прежний клон не назван ни одной строкой (`Delete Ownership` → `unproven`);
  - память, история и сводка остаются под старым профилем;
  - прежняя строка не помечена.

  Кейсы после — прототипы F1 (9) и F2 (9) плюс новые:
  - `CreateOrigin=start` ничего не находит;
  - `PreviousProfileId` без `bot_key`;
  - чужая установка с тем же `bot_key`;
  - `app_delete`;
  - отозванная установка;
  - строка, замещённая другим профилем;
  - **два предшественника с одним чатом** — без нарушения уникальности, побеждает самый свежий;
  - повтор после retry идемпотентен;
  - инвариант: **после переноса настоящие `Delete Ownership` + `Delete Chat Memory` по текущим id стирают всё, под старыми
    профилями — 0**;
  - legacy: ни переноса, ни замены, `foreign` для профиля установки;
  - сентинел;
  - правило `'-1'`.
- **Готово, когда:** матрица зелёная; `verify-sql-comments.check_workflow` по строкам модуля OK (параметры в комментариях названы
  словами, V1).
- **Зависит от:** ничего (новые файлы). Параллельно с T1.
- **Риски.**
  - Сложный оператор с десятком CTE: все CTE видят снимок до оператора. Решение «перенести или удалить дубль» сравнивается
    со снимком, а не с результатом соседнего CTE — отсюда тест двух предшественников.
  - Время оператора на больших историях: замерить на staging-Postgres с 10 тыс. строк памяти (цель < 1 с сверх нынешнего Bind).

### T3. Граф Create WA/TG — 7–8 ч

- **Цель.** Цепочка §1.4 в обоих канонах — одним генератором, без конфликтов с остальными генераторами Create.
- **Файлы:**
  - новый `Tools/n8n/apply-clone-lifecycle.py` (`--check`);
  - `workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json`, `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` (только генератором);
  - `apply-auth-gate.py`: `patch_create` отдаёт хвост Create новому генератору и оставляет указатель, как стадия 4 с телами Claim.
    Сегодня он ищет узел `Restamp RAG Chunks` и упал бы на новом графе;
  - `apply-reply-mode-v2.py`: `CLONE_CHAIN` → `Clone Plan`;
  - `tests/create_seed_chain.js` (расширения F1 Приложение A, обратно совместимы);
  - `tests/test_create_seed.py`;
  - новый `tests/test_create_clone.py` (граф через драйвер → SQL-реплей пройденных узлов → читающие запросы).
- **Тест сначала.** «Книги клонов» (F1 §3 B): каждый клон, созданный прогоном или названный планом, либо привязан и выдан
  клиенту, либо удалён (200/404), либо стоит в `clone_retirements`, и его берёт настоящий SQL ветки A. Сценарии × WA/TG ×
  сессия/legacy — список F1 §3 B, плюс F2:
  - R1 — жизнь → чистка → покупка → переподключение: клон новый, память и сводка перенесены, чанки находятся по
    `$workflow.id`, прежний клон удалён;
  - R4 — повтор с той же парой;
  - R9 — двухканальный бот с `TelegramWorkflowId = -1`.

  На HEAD: 14 нарушений F1 §1.3 + F2 R1 (0 чанков, память под старым профилем) → FAIL. Контракт ответа (F1 §3 C): ни одно тело
  отказа не содержит `"id"`.
- **Готово, когда:**
  - книги — 0 нарушений, остаток ровно один названный (двойной сбой Discard и журнала → Tier 2);
  - `test_create_seed.py` 53/53 с новыми именами узлов (отказ «Вместе» по-прежнему ничего не создаёт);
  - все генераторы, касающиеся Create (`apply-auth-gate`, `apply-reply-mode-v2`, `apply-slot-count-fix`,
    `apply-history-handoff`, `inject-prompts`), после нового — второй прогон без диффа.
- **Зависит от:** T2.
- **Риски.**
  - Генераторы переписывают друг друга: закрепить, кто владеет какими узлами, и проверить общим прогоном `--check`.
  - `Activate` по уже активному workflow на n8n 2.36.7 должен быть нулевой операцией — проверить на staging (T10). Если нет —
    в ветке переиспользования добавить `Already Active?` по телу пробы.

### T4. Стражи Create — 4–5 ч

- **Цель.** Структурные инварианты новой цепочки нельзя тихо сломать.
- **Файлы:**
  - новый `Tools/n8n/verify-clone-lifecycle.py` и мутационные тесты к нему (как `test_create_seed` E);
  - `verify-reply-mode.py` `check_create`: достижимость `Create Workflow` и `Activate Created Workflow` только через
    `Seed Saved?[0]`;
  - `verify-auth-gate.py` (проводка Bind);
  - `verify-telegram-parity.py` (§ Restamp (i)–(iv) → проверки нового Bind, список узлов);
  - `tests/test_verify_parity.py`.
- **Тест сначала.** Новый верификатор на каноне HEAD падает: нет `Clone Plan`; у Bind нет положительного If; `Activate` без
  `neverError`; URL на публичный адрес. Мутации, которые должны отклоняться:
  - `Bind → Send` в обход `Bound?`;
  - удаление ребра Discard;
  - `Activate` без `neverError`;
  - ребро `Seed Saved?[0] → Get Sample` в обход плана;
  - Respond отказа с `id`;
  - `PreviousProfileId` в RULE гейта;
  - список `PROTECTED` ≠ `PROTECTED_IDS`;
  - URL n8n API не localhost или учётка не `__HDR_CRED__`.
- **Готово, когда:** все мутации отклонены, канон после T3 проходит; все прежние стражи зелёные.
- **Зависит от:** T3.
- **Риски.** Минимальные.

### T5. Чистка и удаление по линии бота — 5–6 ч (+2 ч опционально)

- **Цель.** Ни один заменённый клон не остаётся без удаления. Удаление бота стирает всю его линию. V12 не трогает замещённые
  строки.
- **Файлы:**
  - `apply-sweep-clone-deactivate.py` (или новый `apply-sweep-clone-retirements.py`) → `fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json`:
    ветка A ниже существующих, флаг `retirementsDryRun` (первый прогон на проде — только лог);
  - фильтр `superseded_by is null` в `Retired Clones`;
  - генераторы Delete Bot Files (`apply-auth-gate.py` V12-цепочка + `apply-history-handoff.py` `Delete Chat Memory`)
    → `lmjYsdNcQA2IE5rl-Delete_Bot_Files.json`: фрагмент линии из `clone_lineage.py`;
  - `tests/test_retired_delete.py`, новый `tests/test_clone_retirements.py`;
  - `verify-history-handoff.py` / `verify-auth-gate.py` (закрепить фрагмент линии во всех удаляющих узлах).
  - Опционально: `build-auth-workflows.py` (прокси, `OWNER_OPS` через `Profile Owner`) + `tests/test_claim.py` / e2e.
- **Тест сначала.**
  - Удаление бота по текущим id после смены профиля (до фикса) оставляет память P1 и чанки WFold (F1 §1.4) → FAIL на HEAD.
  - Ветка A на настоящем SQL: никогда id, который ведёт живая строка; никогда защищённый; не моложе 10 мин; 200/404 → отметка,
    500 → `attempts + 1`. На HEAD ветки нет → FAIL.
  - `Retired Clones` выбирает замещённую строку `trial_expiry` → FAIL на HEAD (с M2).
- **Готово, когда:**
  - тесты зелёные;
  - удаление по линии не трогает строки другой установки и другие `bot_key`;
  - строки, ранее помеченные `app_delete`, удаляются повторно без ошибок;
  - `Deletion Settled?` по-прежнему требует все счётчики числами.
- **Зависит от:** T2, T3 (общий `apply-auth-gate.py`).
- **Риски.**
  - Удаление по линии необратимо — матрица негативов обязательна.
  - «Удалить только с телефона» (смешанное владение) оставляет свои строки этой установки. Их сотрёт удаление следующего бота
    в этом слоте — задокументировать.

### T6. Пометки памяти: Q1 B + V6-unknown (сервер) — 7–8 ч

- **Цель.** Модель видит, кто писал реплику и дошёл ли её прошлый ответ. Обычные ответы не меняются, пометки никогда не
  попадают ни к клиенту, ни в собственную память бота как «слова владельца». Подробный дизайн — §4.
- **Файлы:**
  - `apply-history-handoff.py`:
    - константы `OWNER_MEMORY_PREFIX = 'Владелец: '`, `UNDELIVERED_MEMORY_PREFIX = '[доставка не подтверждена] '`;
    - `MEMORY_ROW` для `owner_phone` в `Record Outgoing`;
    - `Record Send`: для `send_unknown` — префикс в `content` (идемпотентно);
    - `Stamp Draft`: снимает ведущую пометку с записываемой строки;
    - `TRANSCRIPT_NEW` классификатора Сводки снимает пометки из `content`;
    - блок `##memory_marks##` в хвост собственного промпта шаблонов (для ботов с пустым или legacy-типом), рядом с `TOOL_RULE`;
  - `build-auth-workflows.py` → `crWappiProxy0001`: `Record Owner Send` с префиксом (константа одна; паритет закреплён в
    `verify-history-handoff.py`);
  - `apply-reply-pipeline-v2.py` (`FINAL_CHECK_JS`, живое тело — проверить по маркерам, кто им владеет): снять ведущую пометку с
    `text` перед отправкой;
  - `Tools/n8n/prompts/_core.md` (фраза в «Память», §4.3) → `inject-prompts.py` (карты Create/Edit WA/TG; `--check`, порог кэша);
  - тесты: `tests/test_reply_memory.py`, новый `tests/test_memory_context.py` (+ драйвер `memory_context.js`), `verify-history-handoff.py`,
    `verify-reply-pipeline.py`, `verify-telegram-parity.py`, `verify-panel-prompts.js`.
- **Тест сначала.** `test_memory_context.py`: строки пишет настоящий SQL писателей (owner_app, owner_phone, `Record Send` sent и
  unknown) на temp-таблицах. Затем — установленное в staging n8n преобразование LangChain
  (`mapStoredMessagesToChatMessages` → `convertMessagesToCompletionsMessageParams`, как в доказательстве ревью
  `pipeline-delivery-context.js`) → сообщения модели. Требования:
  - owner-строки — `assistant` с «Владелец: …»;
  - unknown — с пометкой;
  - sent — без изменений.

  На HEAD все три одинаковые → FAIL (ровно контрпример ревью). Плюс:
  - `test_reply_memory`: ответ агента, начинающийся с «Владелец:», после `Stamp Draft` хранится без пометки (на HEAD — с ней,
    FAIL);
  - транскрипт Сводки не содержит «Владелец: Владелец:».
- **Готово, когда:**
  - тесты зелёные, `test_reply_memory` 44/44 + новые, `test_reply_memory_concurrent` 9/9;
  - стражи зелёные, `inject-prompts.py --check` 0;
  - генераторы — второй прогон без диффа.
- **Зависит от:** T1 (общий `build-auth-workflows.py`), T3 (общие Create JSON), T5 (общий `apply-history-handoff.py`).
- **Риски.**
  - Изменение `content` после `Stamp Draft` не должно ломать V6: `Stamp Draft` сравнивает текст только у непомеченных строк
    своего прогона, `Record Send`/`Record Cancel` действуют по `execution_id` — закрепить тестом.
  - Префикс в начале контента сдвигает префиксный кэш OpenAI только у истории, системный промпт не меняется.

### T7. Черновики потерянных прогонов + бэкфилл пометок — 3–4 ч

- **Цель.** Строка `delivery = 'draft'`, чей прогон умер, не остаётся для модели обычной своей репликой.
  - Прогон дошёл до аренды (R9 перевёл событие в `send_unknown`) → строка `unknown` + пометка.
  - Прогон умер до аренды (`claimed/generated` старше 10 мин или событие перехвачено другим прогоном) → строку удалить: клиент
    её не видел, это семантика `Record Cancel`.
  - Плюс M3 для уже накопленных строк.
- **Файлы:**
  - `build-handoff-workflows.py` → `crHandoffNtfy001` (`LOST_SEND_SQL` + CTE черновиков; запустить тот же расписанный узел);
  - `sql/…-memory-marks-backfill.sql` (M3);
  - `tests/test_reply_lost_send.py`, `tests/test_handoff_notifier.py`.
- **Тест сначала.** Черновик прогона, осевшего в `send_unknown` → на HEAD остаётся `draft` без пометки → FAIL. Черновик прогона,
  умершего в `generated` 11 мин назад → на HEAD остаётся → FAIL. Негативы:
  - черновик живого прогона (`updated_at` < 10 мин) не трогается;
  - `sent` не трогается;
  - чужой `execution_id` не трогается;
  - owner-строки не трогаются;
  - M3 идемпотентна и не трогает строки с пометкой.
- **Готово, когда:** тесты зелёные; F3-урок соблюдён (`Due Request?` не меняется); уведомитель по-прежнему один узел на тик.
- **Зависит от:** T6.
- **Риски.** Удаление строки памяти из расписания — только по положительному факту из `reply_events` (состояние + тот же
  `execution_id` + возраст). Без строки события — ничего.

### T8. Клиент: перенос id, удаление, контракт Create, доработки F1 — 7–8 ч

- **Цель.** Удалённый канал переходит на «свежий» путь, удаление не регрессирует (V12), Create из настроек идемпотентен и
  повторяем, «Сменить номер» не удаляет клон.
- **Файлы:**
  - `Assets/Scripts/Auth/ProfileRetirement.cs` — перенос при `profile_retired`: `{bot}{Ch}WorkflowId` → `{bot}{Ch}RetiredWorkflowId`,
    текущие id → `-1`, `isOn{Ch}` → 0, кроме причины `app_delete` (там id остаются: экраны предлагают только «Удалить ещё раз»);
  - новый `Assets/Scripts/Main/RetiredChannelPolicy.cs` (чистый: «канал удалён» ⇔ текущий id `-1` и есть маркер, либо маркер
    M4 относится к текущему id — так остаётся `app_delete`, который не переносится);
  - `ProfileClaimFlow.cs`;
  - `Bot.cs`: `DeleteBotLocally` стирает `RetiredWorkflowId` и `CreatePending`, карточка читает новую политику;
  - `BotServerDeletion.cs`: `HasServerTrace` с retired-id;
  - `DeleteBotFilesPayload.cs` и `Manager.DeleteBotOnServer`: второй вызов Delete Bot Files с набором retired-id, только когда
    у канала есть и текущий; клон `RetiredWorkflowId` через Set Workflow State; unlink — только текущие профили;
  - `Manager.cs` (`Create*WorkflowFromStart/FromEdit`): поля формы §1.7, таймаут 45 с, классификация `CreateWorkflowResponse`,
    `adoptedProfiles` → чистка retired-ключей, `ProfileGone` → `-1`, `Retryable` → `CreatePending`;
  - `BotSettings.Auth.cs`:
    - «Сменить номер» → только logout;
    - существующий профиль `authorized` + `CreatePending` (или нет workflow id) → повтор Create;
  - новый `Assets/Scripts/Main/CreateWorkflowResponse.cs`;
  - тесты EditMode.
- **Тест сначала:**
  - `RetiredChannelPolicyTests`: перенос идемпотентен, не трогает живой канал, `app_delete` не переносит;
  - `BotServerDeletionPolicyTests`: бот только с retired-id → `HasServerTrace = true`. На HEAD состояние «после переноса» даёт
    `NothingOnServer` → FAIL — ровно регрессия V12, которую нельзя выпустить;
  - `CreateWorkflowResponseTests`;
  - `BotDeletionWiringTests` — удаление retired-клона, нет unlink retired-профиля;
  - `ReconnectWiringTests` (страж по исходнику): `FromEdit` шлёт `PreviousProfileId`/`CreateOrigin`; чистит маркер только по
    `adoptedProfiles`; «Сменить номер» не вызывает `GetDelete*Workflow` (на HEAD вызывает → FAIL);
  - обновить `ProfileRetirementTests`: правило M4 «id не трогать» заменяется правилом переноса.
- **Готово, когда:** фильтр новых тестов зелёный, полный EditMode зелёный (сейчас 2857/2857); `bot-persistence` key-catalog
  дополнен.
- **Зависит от:** контракта §1.7 (не от серверных задач). Параллельно с T1–T7.
- **Риски.**
  - Перенос без чтения retired-id при удалении выпускать нельзя: одна сборка, один PR.
  - PlayerPrefs и поля `Bot` пишутся в одной точке с `PlayerPrefs.Save()`.

### T9. Клиент: экраны, покупка, тексты — 7–8 ч

- **Цель.** Владелец видит, что бот отключён, что после оплаты делать, и подключает канал одним путём. Тексты и экраны — §5.
- **Файлы:**
  - `BotCardModel.cs` / `Bot.cs`: «Подключить» вместо «Авто», если удалены все каналы и среди причин нет `app_delete`;
  - `BotSettings` — подсказка строки канала и баннер «Бот отключён» через аддитивный идемпотентный билдер в `Assets/Editor/`
    (образец `BusinessContactFieldsBuilder`; деструктивный `Rebuild Bot Settings Prefabs` — нет);
  - `ChatManager.BotState.cs`: `EmptyStateReason.BotChannelRetired`;
  - новый ленивый оверлей `ReconnectBotsSheet` (sortingOrder 3, образец `BillingGateSheet`; шрифты — с меток пейволла);
    подписка на `BillingService.OnEntitlementChanged` — не на `OnPaywallRequested`: у `PaywallController` единственная подписка
    остаётся;
  - повторный прогон `ProfileClaimFlow.RunAtBoot` после покупки;
  - ожидание сервера перед `profile.add` (`ReconnectSettlePolicy`, расписание `TopUpSettlePolicy` 0/2/5/10/20 с по `GetUsage`);
  - слой копий `RetiredChannelCopy` (регистрация в `FontGlyphCoverageTests`);
  - `AndroidBackRouter` / `BackNavigation.Targets()` — новый лист в своём z-порядке.
- **Тест сначала:**
  - `BotCardModelTests` — состояние «Подключить»;
  - `ReconnectSettlePolicyTests`;
  - `ReconnectWiringTests` — покупка → повторный claim → лист; ожидание стоит до `profile.add`;
  - `FontGlyphCoverageTests` — новый слой;
  - `BackNavigationTests` — лист закрывается своим «Позже».

  На HEAD всех этих сущностей нет → FAIL.
- **Готово, когда:** EditMode зелёный, сцена изменена только аддитивным билдером, сцена сохранена. Тексты показаны владельцу до
  выкладки (решение 2026-10-09).
- **Зависит от:** T8.
- **Риски.**
  - Ширина капсулы под «Подключить» — измерить; при нехватке — аддитивная правка `BotCardAutoPillBuilder`.
  - `ConfirmCardFitter`-правило для любого нового confirm-окна (проверить авторскую зазор-геометрию до подгонки).

### T10. Staging e2e блока — 6–7 ч (staging, строго последовательно)

- **Цель.** Всё серверное — на настоящем n8n 2.36.7 + мок Wappi, WA и TG.
- **Файлы:** `staging/e2e-auth.py`, `staging/e2e-handoff.py`, `staging/e2e-reply.py`, при необходимости `staging/derive-staging.py`
  (переназначение `n8n Admin API` в Create — по имени, как Sweep/Test Dialog).
- **Сценарии** (каждый сначала прогоняется на staging из HEAD-канона и падает, затем на каноне блока):
  - `reauth_same_profile_reuses_bound_clone`;
  - `reauth_new_profile_replaces_prior_and_moves_data` (память, история, сводка, чанки под новым; GET прежнего клона = 404;
    `removed_at`);
  - `retired_bot_reconnects_after_purchase` (строка удаляется SQL как чисткой + клон выключается API → `Upsert Subscriber` →
    `profile.add` → Create с `PreviousProfileId` → новый клон активен, вебхук нового профиля доходит, бот находит прайс и
    помнит прежний диалог);
  - `activation_conflict_discards_new_clone`;
  - `bind_failure_answers_no_id` (приём R8: CHECK на `bot_profiles`);
  - `legacy_create_on_owned_profile_refused`;
  - `foreign_install_same_bot_key_untouched`;
  - `wizard_create_never_adopts`;
  - `bot_delete_wipes_lineage`;
  - `sweep_removes_booked_clone`;
  - `activate_on_active_is_noop` (проверка платформы для ветки переиспользования);
  - `billing_conflict_is_logged`;
  - `owner_send_marked_in_memory` (прокси owner_app и вебхук owner_phone);
  - `unknown_delivery_marked_in_memory` (мок отвечает 5xx/таймаутом → `send_unknown` → строка с пометкой);
  - `lost_run_draft_settled`.
- **Готово, когда:**
  - полные `e2e-auth` / `e2e-handoff` / `e2e-reply` зелёные (сейчас 31/0, —, 44/0 + новые);
  - `verify-deployed --base http://localhost:5678` exit 0;
  - staging возвращён в штатное состояние (`bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001`).
- **Зависит от:** T1–T7.
- **Риски.** Стенд общий с другими сессиями: брать его только целиком, перед началом проверить, что никто не гоняет e2e.

### T11. Замер Q1 B / V6-unknown: оценщик h7, набор v1.4, замер в один день — 7–9 ч + ≈ $3–6 (staging)

- **Цель.** Доказать числами: B решает Q1 лучше A, ничего не ломает, **казахский не хуже прода**; модель правильно читает
  пометку неизвестной доставки. Протокол и критерии — §4.5.
- **Файлы:**
  - `Tools/n8n/eval/run-eval.py` (h7: роли `owner` и `bot_unknown` рендерятся так, как их хранят писатели **на измеряемом ref**:
    префиксы читаются из `apply-history-handoff.py` на `--prompts-ref`, по аналогии с `--search-ref`; новые проверки
    `no_marker_leak`, `no_tool:create_handoff`, `not_says_already`);
  - `eval/test_checks.py`;
  - `eval/scenarios.v1.json` → v1.4;
  - `eval/rescore.py`;
  - результаты `eval/results/q1b-*`, `v6u-*`;
  - `evidence/Q1B-measure.txt`.
- **Тест сначала.** `test_checks.py`:
  - роль `owner` на ref без констант рендерится как сейчас (`business`), на ref с ними — с пометкой (на HEAD ролей нет → FAIL);
  - новые проверки — положительные и отрицательные примеры;
  - пересчёт сохранённых v1.3-результатов h6 → h7 без изменения вердиктов (`rescore.py`).
- **Готово, когда:** протокол §4.5 выполнен в один день обеими сторонами параллельно, критерии §4.5 сведены в таблицу. При
  провале казахского — §4.6.
- **Зависит от:** T6, T7 (канон-ref «после»). Офлайн-часть (h7, v1.4, тесты проверок) — параллельно с T6.
- **Риски.**
  - Сдвиг модели между днями (Q1: тот же вход дал 1/8 и 8/8 в разные дни) → только «в один день».
  - Общий балл скрывает побочное действие (урок c1) → отдельная проверка `no_tool:create_handoff` на a6/q1p.

### T12. Документы, пакет выкладки, репетиция на staging — 4–5 ч

- **Цель.** Блок готов к «да» владельца.
- **Файлы:**
  - **CLAUDE.md:**
    - A1: правила линии бота; `PreviousProfileId`/`CreateOrigin` не в RULE; журнал конфликтов;
    - A2: абзац R8-deletefail — F1 закрыта, новое правило достижимости;
    - V12: чистка `clone_retirements`, удаление по линии, фильтр `superseded_by`;
    - раздел Wappi `auth/code`: исправить инвариант «no workflow exists while the code panel is reachable»;
    - A3 (5) V6: пометка `unknown`, судьба черновиков;
    - A5/A6: пометки памяти и фраза;
    - Q1: вариант B на месте A;
    - M4: перенос id;
  - `.claude/skills/bot-persistence/references/key-catalog.md` (`RetiredWorkflowId`, `CreatePending`, изменённое правило M4);
  - `docs/audits/a1-deploy-runbook.md`;
  - пакет выкладки в README этой папки (§6);
  - точки отката.
- **Репетиция:**
  1. staging `bootstrap` из итогового канона;
  2. `verify-deployed` (ожидаемо: шаблоны и клоны BEHIND, головы PROMPT-BEHIND);
  3. `update-clones.py --apply`;
  4. `verify-deployed` exit 0;
  5. `deploy-canonical.py` dry-run против staging показывает ровно ожидаемый список;
  6. `tests/run_all.py` зелёный;
  7. полный EditMode;
  8. все генераторы `--check` 0.
- **Зависит от:** T1–T11.

### T13 (опционально). F1 Tier 2 — бродячие клоны, сначала только отчёт — 6–8 ч

F1 §2.6 B без изменений: GET списка workflows (пагинация) → признаки клона `update-clones.is_clone` → SQL-вердикт с положительным
признаком → две фазы (выключить и записать `inventory`, через 24 ч удаляет ветка A), ≤ 10 действий за прогон, `strayDryRun = true`
первую неделю. Первая кандидатура на перенос: прод сейчас без клонов, а остаток без Tier 2 — только умершее исполнение между
Create и Bind/Discard.

### 3.1 Что параллельно, что строго последовательно

| Поток | Задачи | Почему так |
|---|---|---|
| Сервер, последовательно | T1 → (T2 ∥ T1) → T3 → T4 → T5 → T6 → T7 | Общие генераторы и JSON: `build-auth-workflows.py` (T1, T6), `apply-auth-gate.py` (T3, T5), `apply-history-handoff.py` (T5, T6), Create JSON (T3 — граф, T6 — карты промптов). После каждой задачи — общий прогон всех генераторов `--check` |
| Сервер, параллельно | T1 ∥ T2 | T2 — только новые файлы + M2 |
| Клиент, параллельно с сервером | T8 → T9 | Свои файлы. Между собой последовательно: общие `Bot.cs`, `BotCardModel.cs`, `Manager.cs` |
| Оценщик офлайн | h7 + v1.4 + `test_checks` (часть T11) ∥ T6 | `eval/` не пересекается с генераторами; нужны только имена констант из §4.1 |
| Staging, строго последовательно | T10 → T11 (платная часть) → T12 (репетиция) | Один стенд на всех, bootstrap перезаписывает его целиком |

Критический путь: T2 → T3 → T4 → T5 → T6 → T7 → T10 → T11 → T12 ≈ 51–60 ч. Клиент T8 + T9 (14–16 ч) укладывается внутрь.

---

## 4. Q1 B + V6-unknown: пометки памяти для модели

### 4.1 Пометки

| Источник строки памяти | Писатель | `content` для модели | Метаданные (как сейчас) |
|---|---|---|---|
| Владелец из приложения (owner_app) | `Record Owner Send` (прокси) | `Владелец: <текст>` | `type: ai`, `source: owner_app`, `message_id` |
| Владелец с телефона (owner_phone) | `Record Outgoing` (шаблоны) | `Владелец: <текст>` | `type: ai`, `source: owner_phone` |
| Ответ бота, доставка подтверждена | память агента + `Stamp Draft` + `Record Send` | `<текст>` (без изменений) | `delivery: sent`, `transport_message_id` |
| Ответ бота, доставка неизвестна | то же, `Record Send` при `send_unknown` или R9 → T7 | `[доставка не подтверждена] <текст>` | `delivery: unknown` |
| Ответ бота, отказ транспорта | `Record Send` (`send_failed`) | строка удаляется (как сейчас) | — |
| Черновик умершего до аренды прогона | T7 | строка удаляется | — |
| Клиент | без изменений | `<текст>` | `type: human` |

`chat_history_events.body`, `source` и `delivery` не меняются: пометка живёт только в `content` памяти, который читает модель.
Префиксы — константы в одном месте (`apply-history-handoff.py`), прокси берёт ту же строку, паритет закреплён.

**Почему запись в `content`, а не преобразование при чтении.** Узел памяти n8n читает `message` как есть, и LangChain передаёт
модели только `role` и `content` — это доказательство ревью `pipeline-delivery-context.json`. Альтернатива — представление
(view) с `INSTEAD OF`-триггером вместо таблицы памяти — хрупкая: узел сам создаёт таблицу (`CREATE TABLE IF NOT EXISTS`) и
пишет в неё. Пометка при записи проверена пробой B (Q1-design §7).

### 4.2 Стражи «без вреда для обычных ответов»

- **Final Check** снимает ведущие «Владелец:» и «[доставка не подтверждена]» с текста ответа: клиент пометку никогда не
  увидит.
- **Stamp Draft** снимает их же со строки памяти своего прогона. Иначе выдуманное ботом «Владелец: завтра в 15:00» на следующем
  ходе стало бы «подтверждённой договорённостью».
- Пометка неизвестной доставки ставится только на `unknown` — это редкий случай. Обычный разговор не меняется ни символом,
  системный промпт +1 фраза (≈ +60–80 токенов, порог кэша проверяет `inject-prompts.py`).
- **Классификатор Сводки** снимает пометки из `content`: метку «Владелец: » он и сейчас ставит по `source`, двойной префикс
  исчезает.
- **Старые строки** — бэкфилл M3, чтобы память удалённых ботов, которую F2 вернёт, читалась так же.

### 4.3 Фраза в промпте

Дописывается в конец абзаца «Память» `_core.md` (через `inject-prompts.py` → карты Create/Edit WA/TG) и тем же текстом —
блоком `##memory_marks##` в хвост собственного промпта шаблонов. Боты с пустым или legacy-типом бизнеса несут промпт шаблона, а
Q1 A их не покрывал. Кандидат — B2 из пробы Q1 плюс одно предложение V6:

> Реплики, которые начинаются с «Владелец:», написал сам владелец: названные в них день, время, место или человек —
> подтверждённая договорённость; клиент переспрашивает — повтори её как есть. Твой прежний ответ с пометкой «[доставка не
> подтверждена]» мог не дойти до клиента: не ссылайся на него как на уже прочитанный, а если клиент переспрашивает или пишет,
> что ответа не было, ответь заново по существу. Сами эти пометки в ответах не пиши.

Почему «как есть» остаётся: без этих слов c1 в «Цветах» начинал оформлять заказ на простой переспрос (17/20 лишних
`create_handoff`, `evidence/Q1c-measure.txt`). Риск для казахского от «как есть» — гипотеза из Q1 A. Её проверяет §4.5, план
отступления — §4.6.

### 4.4 Что не покрывается (остатки, документировать)

- Относительные даты («завтра» из вчерашнего сообщения): у модели нет метки времени реплики.
- Цена, названная владельцем в чате, против каталога: по PRIORITY побеждает каталог. Это Q1-design, вопрос 2 — вне B.
- `unknown`, который на деле дошёл, остаётся помеченным: позднего связывания с вебхуком `outgoing_message_api` нет (V7). Вред —
  повтор сути на переспрос, не выдуманный факт.
- Поздняя запись owner_app после чтения памяти конкурирующим прогоном (окно V7) — без изменений.

### 4.5 Протокол замера и критерии принятия (задаются ДО прогона)

Стенд: staging n8n, `stgEvalRunner001`, gpt-5.4-mini с параметрами шаблона, оценщик **h7**, набор **v1.4**.

- «До» — `--prompts-ref --search-ref <HEAD до T6>`: прод-версия промпта, пометок нет, роли `owner`/`bot_unknown` рендерятся
  как на проде, без префикса.
- «После» — `<коммит T7>`.
- Обе стороны — параллельно, в один день.

**Набор v1.4:**

- Q1:
  - `business`-реплики a6/q1p/q1c переводятся в роль `owner`, кроме реплики бота в `q1c_client_time_history_phone_repair`;
  - остальные `business` во всех сценариях проверяются вручную: это прежние ответы бота, и роль у них остаётся;
- новые `u1_unknown_reask` (auto_parts: прошлый ответ «Фильтр 90915-YZZE1 — 3200 тг.» с пометкой, клиент «Вы ответили? Не вижу
  сообщения»);
- `u2_unknown_new_question` (тот же прошлый ответ, клиент спрашивает о доставке);
- `u3_sent_control` (как u1, но доставлено);
- `u4_kz_unknown` (казахский u1).

| Прогон | Объём на сторону |
|---|---|
| Q1: a6 ×8 (24), q1p ×8 (48), q1c ×8 (40) | 112 |
| Казахский: 7 KZ-сценариев автоответа (`ap_sku_exact_kz`, `ws_kz_min_order`, `fl_kz_price`, `ks_kz_availability`, `ed_kz_parent`, `pr_kz_battery`, `gen_kz_pure_greeting`) ×10 + два слабейших (`pr_kz_battery`, `ks_kz_availability`) добором до ×30 | 110 |
| V6: u1–u4 ×8 | 32 |
| Регрессия: весь автоответ v1.4 ×3 | ≈ 210 |

**Критерии** (все обязательны; Fisher, односторонний, порог p < 0,05):

| # | Что | Порог |
|---|---|---|
| К1 | a6 | ≥ 23/24 |
| К2 | перефразы q1p | после ≥ 30/48 и значимо лучше «до»; ни один сценарий не хуже «до» на ≥ 3/8 |
| К3 | эхо клиентского времени (`not_mentions:15:00`) | 0/24 и не больше «до» |
| К4 | контроли q1c | ≥ 39/40 |
| К5 | лишний `create_handoff` на a6/q1p | не больше «до» (ожидается 0) |
| К6 | казахский (все проверки `lang*` истинны) | **нет значимого ухудшения против «до» (= прод)** ни в сумме 7 сценариев, ни в каждом из двух слабейших (×30), ни в сумме пяти остальных (это провал c1: 80/90 → 68/90) |
| К7 | V6 | u1 и u4 ≥ 7/8 (суть повторена, нет «как я уже писал»); u2 и u3 ≥ 7/8 без разговоров о недоставке; утечка пометок 0 во **всех** прогонах |
| К8 | регрессия ×3 | ни один чистый или нестабильный «до» сценарий не стал 0/3; проверки обещаний и передачи 100 %; нестабильных не больше «до» + 2 |

Плюс staging A6 (`bot_knows_owner_agreement_after_auto_resumes`) ×3 на клонах с промптом Create (`staging/vertical-bots.py`) и
на штатных клонах с промптом шаблона — стабильный PASS. Стоимость — по образцу Q1 A (официальные прогоны $0.74) и с учётом
×30 и регрессии: ≈ $3–6.

### 4.6 Если не выполнен К6 (казахский)

1. Заранее зарегистрированный кандидат **B2-lang**: та же фраза, но «повтори её как есть, на языке клиента». Скрининг ×4 по Q1 и
   7 KZ, затем полный протокол §4.5 заново в один день.
2. Не прошло и это — отчёт владельцу с цифрами и двумя путями:
   - (а) выложить писателей, стражи, V6 и бэкфилл **без** фразы Q1 — состояние B1, проба не хуже прода, V6-часть фразы остаётся
     отдельным предложением;
   - (б) выложить B с задокументированной ценой для казахского.

Решение в этом случае — владельца. Заранее его не прошу: это ветка на случай провала.

---

## 5. Клиент F2: экраны, тексты, сценарий, device-pass

Все тексты — только из символов статических атласов SF Pro: « » · — … ё есть; → № − ✕ не используются. Регистрация в
`FontGlyphCoverageTests`. Владелец видит тексты до выкладки (решение 2026-10-09).

### 5.1 Экраны и тексты

| Где | Состояние | Текст |
|---|---|---|
| Карточка бота, подстрока (есть с M4) | по причине | «Отключён · пробный период закончился» / «Отключён · подписка закончилась» / «Канал отключён» / «Удаление не завершено · удалите ещё раз» |
| Карточка бота, капсула | удалены все каналы, причина не `app_delete` | «Подключить» вместо «Авто»; тап → настройки бота на строке канала |
| Настройки → «Основное», баннер (до оплаты) | канал удалён, тариф неактивен | Заголовок «Бот отключён». Текст: «Пробный период закончился, и канал отключили. Настройки, прайс-листы и переписка сохранены — после оплаты подключите канал снова.» (для подписки: «Подписка закончилась, и канал отключили. …»). Кнопка «Выбрать тариф» → пейволл |
| Настройки → строка канала | канал удалён | Тумблер выключен, подсказка «Отключён · нажмите, чтобы подключить снова»; если сохранён номер — «Ранее: +7 …» |
| Настройки, `app_delete` | незавершённое удаление | Только «Удалить ещё раз», без «Подключить» |
| Чаты, пустое состояние | канал удалён | «Бот отключён» / «Подключите канал снова в настройках бота» |
| Ожидание сервера после оплаты | `GetUsage` ещё `expired` или нет свободного канала | «Подтверждаем оплату…»; не дождались за 20 с — «Оплата ещё не дошла до сервера. Попробуйте через минуту.», пара не начинается |
| Лист после покупки | есть удалённые каналы | Заголовок «Подключите ботов снова». Текст: «Тариф активен. Настройки, прайс-листы и переписка на месте — осталось заново подключить WhatsApp или Telegram.» Строки «{бот} · WhatsApp» с кнопкой «Подключить», внизу «Позже» |
| Меньший тариф | каналов больше лимита | «Подключить» на лишнем канале открывает существующий лист лимита каналов |

### 5.2 Сценарий после покупки

1. Покупка → пейволл закрывается (как сейчас).
2. `BillingService.OnEntitlementChanged(платный тариф)`:
   - повторный `ProfileClaimFlow.RunAtBoot` — не больше одного `claim.start` на профиль за прогон (M4), находит каналы, которые
     чистка удалила после старта;
   - затем список кандидатов `RetiredChannelPolicy`.
3. Кандидаты есть → лист «Подключите ботов снова». «Позже» закрывает лист, кнопки на карточках остаются.
4. «Подключить» → настройки бота → «свежий» путь `CheckWhatsappAuthorization`:
   1. клиентский гейт (удалённые каналы не считаются);
   2. ожидание сервера (§5.1);
   3. `profile.add`;
   4. экран QR/кода;
   5. пара;
   6. Create `FromEdit` с `CreateOrigin=edit`, `PreviousProfileId`;
   7. `{id, adoptedProfiles}` → retired-ключи канала стираются → карточка снова с «Авто» → синхронизация чатов.
5. Отказ Create:
   - `channel_limit` → лист лимита; текущий id `-1`, retired-ключи остаются, следующая попытка назовёт того же предшественника;
   - иначе `CreatePending` → повтор при следующем открытии настроек или включении канала.

### 5.3 Карточка

Подстрока и иконки — M4 (иконка удалённого канала серая). Капсула «Авто» скрывается и заменяется на «Подключить» только когда
у бота нет ни одного живого канала. При одном живом канале капсула «Авто» остаётся и управляет режимом живого канала, а
удалённый показан подстрокой и серой иконкой. Порядок приоритета подстрок — `BotCardModel.SublineKind` (M4): «Подключение…» →
удалённый канал → синхронизация режима.

### 5.4 Device-pass (iOS + Android, тёмная тема по умолчанию; Android дополнительно на ширине 720)

1. Бот с истёкшим триалом (на стенде или dev: строка удаляется как чисткой, клон выключается) → запуск: ровно один `claim.start`,
   подстрока «Отключён · пробный период закончился», пустое состояние чатов «Бот отключён», нет повторяющихся 403 в логе.
2. До оплаты: баннер в настройках, «Выбрать тариф» открывает пейволл.
3. Покупка в sandbox (StoreKit 1 на iOS, лицензионный тестер на Android) → «Подтверждаем оплату…» → лист → «Подключить» → QR/код
   → пара → бот:
   - отвечает ценой из прайс-листа (RAG);
   - помнит прежний диалог (спросить о прежней договорённости);
   - Сводка показывает прежние исходы.
4. Двухканальный бот на «Старте»: подключён WA → «Подключить» у TG открывает лист лимита.
5. Удаление подключённого заново бота → сервер всё стёр (проверка read-only на стенде); бот только с удалённым каналом
   (`app_delete`) → «Удалить ещё раз» работает.
6. «Сменить номер» (F1): тот же клон на сервере (новый не появился), бот отвечает, прайсы на месте.
7. «Получить код ещё раз» (WA): новый профиль → заменённый клон удалён, прайсы и память на новом.
8. Сессия Telegram слетела при выключенном канале → включение → переиспользование клона.
9. Сбой Create (сеть выключена во время Create) → «Не удалось сохранить» → повторное открытие настроек → Create повторяется
   и проходит.
10. Android: системная «Назад» закрывает новый лист; iOS: свайп-назад в настройках не ломает баннер.
11. Q1 B на живом боте: владелец отвечает из приложения «Вместе» → «Авто» → клиент переспрашивает договорённость → бот повторяет
    её. Клиент пишет по-казахски → ответ по-казахски.
12. Обновление с dev-сборки M4 (маркер без переноса) на новую сборку: маркер переносится при первом же 410, ничего не теряется.

---

## 6. Пакет выкладки блока

**До выкладки — отдельные «да» владельца:**

- на выкладку;
- на read-only инвентаризацию прода (F1 Q5: клоны, которые не называет ни одна строка `bot_profiles`; pre-A1 клоны). Ожидаемо
  пусто: на проде 0 неархивных клонов, архивный «13».

**Что меняется на сервере:**

| Workflow | Id | Что |
|---|---|---|
| Auth Session | `crAuthSession001` | журнал конфликтов (V3) |
| Wappi Proxy | `crWappiProxy0001` | «Владелец: » в `Record Owner Send`; опционально `OWNER_OPS` по своей удалённой строке |
| Create WA / TG | `XuvOp7TxOImOAmlj` / `Uz6HBBUpAiUqVysB` | цепочка линии бота (F1+F2), карта промптов (фраза) |
| Edit WA / TG | `3qax5J9u2qsT9Vao` / `TwWPW3gIyjZS3foR` | только карта промптов |
| Шаблоны WA / TG | `4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc` | `Record Outgoing`, `Record Send`, `Stamp Draft`, `Final Check`, хвост промпта шаблона |
| Profile Lifecycle Sweep | `fXYpCXPKw92EzRz8` | ветка A, фильтр V12 (опционально Tier 2) |
| Delete Bot Files | `lmjYsdNcQA2IE5rl` | удаление по линии бота |
| Dashboard Outcomes | `2htWSV5IHO8E2CgB` | снятие пометок в транскрипте |
| Handoff Notifier | `crHandoffNtfy001` | черновики потерянных прогонов (должен остаться **активным**, R9) |

Новых workflow нет, `PROTECTED_IDS` не меняется, env не меняется. Set Workflow State не меняется: `Delete Ownership` тот же.

**Порядок (сервер раньше клиента).**

1. Снимок «до» (`verify-deployed --base prod`), бэкап n8n (`server-backup.sh`) и снимок затрагиваемых таблиц средствами
   раннбука.
2. M1, M2 + пост-проверки.
3. Auth Session. Шаг независим, его можно выложить и раньше всего блока — только по отдельному «да».
4. Dashboard Outcomes: снятие пометок должно стоять раньше, чем писатели начнут их ставить.
5. Profile Lifecycle Sweep (`retirementsDryRun = true` на первые сутки) и Handoff Notifier.
6. Delete Bot Files.
7. Create WA/TG + Edit WA/TG: `deploy-canonical.py` привязывает `__HDR_CRED__` и отказывается выкладывать непривязанное.
8. Wappi Proxy + шаблоны WA/TG.
9. `update-clones.py` (V10): инвентарь инструмента → dry-run → `--apply`. Шаблоны и головы промптов изменились, все клоны станут
   BEHIND, хвосты владельцев не трогаются. Сейчас клонов на проде 0. Известный остаток D1b: при неопубликовавшем PUT
   `update-clones` делает deactivate → activate.
10. M3 (бэкфилл пометок) + пост-проверка.
11. `verify-deployed` exit 0. Smoke без изменения данных: 401/400/`protected_workflow`, Create без токена → 401. Логи 15 мин.
12. Через сутки — `retirementsDryRun = false` (если журнал ветки A чист).
13. Новая клиентская сборка с HEAD после блока (M4 клиент, C1/C1b, F1/F2 клиент) → device-pass §5.4 → живой canary-бот (пакет
    10-09 §4 + этот блок) → подача в сторы.

**Обратная совместимость (почему сервер можно раньше).**

- Ответ Create для старых клиентов содержит `id` при успехе и не содержит его при отказе.
- Старые A1-dev-клиенты получают поиск предшественника по `bot_key` (как F1).
- Сборка 1.0 (4) — legacy: переиспользование только привязки без владельца, отказ `profile_owned` по профилю установки, ничего
  не переносится. Публично её не выпускаем (решение 2026-10-09).
- Пометки памяти и журнал конфликтов от клиента не зависят.

**Что требует новой сборки:** весь клиент F2 (перенос id, удаление с retired-id, экраны, ожидание и лист после покупки) и клиентские
доработки F1 («Сменить номер» без удаления клона, повтор Create). Q1 B, V6-unknown и V3 — только сервер.

**Откат.**

- Прежние JSON каждого workflow — из файла точек отката выкладки (по образцу `2026-10-11-deploy-rollback-pointers.txt`), затем
  `deploy-canonical.py` и, для шаблонов, `update-clones.py`.
- M1 и M2 остаются: старый код их не читает.
- M3 — обратный оператор из файла (по желанию: помеченные строки при старом промпте — состояние B1, не хуже прода).
- Перенесённые данные назад не переносятся: они принадлежат живому боту, старый Create замещённые строки не видит.
- Удалённые заменённые клоны не восстанавливаются — это решение владельца по F1.
- Клиентская сборка с сервером до отката работает: новые поля Create старый сервер игнорирует. После переноса id канал идёт
  «свежим» путём и получает новый клон без переноса данных; данные остаются под retired-id, удаление их видит.

---

## 7. Объём

| Задача | Часы |
|---|---|
| T1 V3 журнал конфликтов | 3–4 |
| T2 SQL-ядро линии бота + M2 | 7–8 |
| T3 Граф Create WA/TG | 7–8 |
| T4 Стражи Create | 4–5 |
| T5 Чистка и удаление по линии | 5–6 |
| T6 Пометки памяти Q1 B + V6-unknown | 7–8 |
| T7 Черновики потерянных прогонов + M3 | 3–4 |
| T8 Клиент: перенос id, удаление, контракт Create, F1 | 7–8 |
| T9 Клиент: экраны, покупка, тексты | 7–8 |
| T10 Staging e2e | 6–7 |
| T11 Замер Q1 B / V6 (h7, v1.4) | 7–9 (+ ≈ $3–6) |
| T12 Документы, пакет, репетиция | 4–5 |
| **Итого обязательное** | **≈ 67–80 ч (≈ 8,5–10 рабочих дней)** |
| Device-pass с владельцем | 3–4 |
| Опционально: отвязка удалённого профиля через прокси (в T5) | +2 |
| Опционально: T13 F1 Tier 2 | +6–8 |

Сверка с проектами: F1 Tier 1 ≈ 23–24 ч + F2 ≈ 30–38 ч (из них M4 ≈ 7–9,5 ч уже сделан) ≈ 46–52 ч. Общий механизм экономит
≈ 8 ч: нет второго пути PUT и отдельного Adopt, одна матрица SQL, одни стражи. Добавляются удаление по линии (+3 ч), черновики
(+3 ч), Q1 B + V6 с замером (≈ 15 ч), V3 (≈ 4 ч), документы и репетиция.

**Что можно отложить, не ломая блок:**

1. T13 F1 Tier 2: остаток — умершее исполнение между Create и Bind/Discard, двойной сбой Discard и журнала.
2. Отвязка удалённого профиля через прокси: ложный liveness оставляет связанное устройство у Wappi при удалении бота из
   приложения — остаток V12 (ii).
3. Лист «Подключите ботов снова» (−3–4 ч): остаются кнопки на карточках и подсказка в настройках — вариант F2 Q3 (б).
4. Бэкфилл M3 (−1 ч): старые строки памяти читаются как сегодня.

**Отложить нельзя:**

- перенос id без удаления с retired-id (регрессия V12);
- удаление по линии бота: без него слот, переиспользованный после удаления бота, может унаследовать чужую для нового бота
  переписку той же установки;
- страж «бот не пишет пометку» (Final Check и Stamp Draft).

---

## 8. Открытые вопросы владельцу

**Настоящих продуктовых развилок, которые нужно решить до начала работ, нет.** Ваши решения 2026-10-09 и 2026-10-11 закрывают
главное:

- F1 — удалять заменённый клон;
- F2 — переносить переписку, память и сводку всегда;
- Q1 — вариантом B с замером казахского;
- V3 — принять ограничение first-bind и вести журнал.

Остальные вопросы проектов решены по их рекомендациям, как вы распорядились. Тексты экранов — §5.1, вы видите их до выкладки.

К сведению — решения, которые план принимает по аналогии с вашими:

- **Перенос данных и при смене профиля того же бота** (повторная отправка кода, «Назад» и новое включение, слетевшая сессия) —
  тем же механизмом, что после оплаты. При «Сменить номер» профиль Wappi тот же (logout → новая пара), и память там и сегодня
  остаётся. Без переноса прежние данные оставались бы под профилем, которого не видит ни бот, ни удаление по текущим id.
- **Новый бот из мастера никогда не наследует** ничего по имени слота. Удаление бота стирает всю его линию на сервере.

Понадобятся отдельные «да» (это разрешения, а не развилки):

1. Read-only инвентаризация прода перед выкладкой (F1 Q5).
2. Бюджет платного замера ≈ $3–6 (T11).
3. Выкладка блока.
4. Если в T11 не выполнится критерий казахского, — выбор из двух путей §4.6 с цифрами на руках.
