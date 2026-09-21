# Пакет развёртывания — аудит 2026-09-15, этапы 0–7 (подготовлен 2026-09-22)

Статус: **подготовлено, НЕ развёрнуто.** Прод (`https://n8n.choosereply.com`, n8n 2.36.7) не менялся ни на одном
этапе; всё проверено на изолированном staging (`Tools/n8n/staging/`, n8n 2.27.4, приватный Postgres, мок Wappi) —
см. `docs/audits/implementation-progress.md`. Развёртывание выполняется владельцем (или по его явному разрешению на
ЭТОТ пакет) в указанном порядке. Детали env/SQL/compat-окна — `docs/audits/a1-deploy-runbook.md` (§1, §3);
этот документ — сводный порядок с проверками и откатом.

## 0. Что входит (по слоям)

| Слой | Артефакты | Этап |
|---|---|---|
| SQL (Supabase, cred `Postgres`), аддитивно и идемпотентно | `Tools/n8n/sql/2026-09-17-auth-installs.sql`, `2026-09-17-reply-mode-version.sql`, `2026-09-17-reply-events.sql`, `2026-09-17-history-handoff.sql`, `2026-09-17-suggestion-request-keys.sql`, `2026-09-22-test-dialog.sql` | 1–5, 7 |
| n8n env | runbook §1 (`NODE_FUNCTION_ALLOW_BUILTIN=crypto`, `N8N_BLOCK_ENV_ACCESS_IN_NODE=false`, `CR_AUTH_SECRET`, `CR_AUTH_COMPAT_UNTIL`, `N8N_PAYLOAD_SIZE_MAX=32`, `EXECUTIONS_TIMEOUT=120`, `EXECUTIONS_TIMEOUT_MAX=300`) | 1, 5 |
| Новые workflows (7) | `crAuthSession001` Auth Session, `crWappiProxy0001` Wappi Proxy, `crHandoffReq0001` Handoff Request (sub-workflow инструмента), `crHandoffNtfy001` Handoff Notifier (расписание), `crHandoffsApi001` Handoffs (list/take/done/**status**), `crTestDialog0001` Test Dialog (`/webhook/TestDialog`), `crHandoffDry0001` Handoff Request Dry (sub-workflow тестового диалога) | 1, 4, 7 |
| Обновлённые workflows (по данным read-back прода 2026-09-22 — 20 DIFF) | Set Workflow State, Create/Edit WA/TG, Suggest Replies, Upload File, Delete File, Delete Bot Files, Dashboard Outcomes, Set Reply Mode, Get Usage, Support Message, Profile Lifecycle Sweep, RevenueCat Events, Delete Orphan Profiles и остальные из `docs/audits/2026-09-22-prod-readback.json` (`status: DIFF`) | 0–6 |
| Шаблоны ботов | `4wYitz5ek30SVNlT` WhatsApp Bot, `4VN3gsFaC2HUYmcc` Telegram Bot (этапы 0.5, 1, 3, 4, 6 — включая русский ответ на медиа вместо «Please send text messages») | 0–6 |
| Клоны ботов владельцев | «13» (`tqGXmiT6aJu7vUIG`, неактивен) и «15» (`OcR75Ug0upgdfrqN`, активен) — 33 узла, без штампа (read-back 2026-09-22) | 7.5 |
| Клиент (Unity) | сессия установки, прокси Wappi, подтверждаемый режим, заявки, знания v1.5, «Проверить бота», последние исходы; `secrets.json` без `wappiAuthToken` | 1–7 |

## 1. Перед началом (owner, ~10 минут)

1. **Бэкап**: `ssh choosereply && ~/choosereply/backup.sh` (ночной `~/backups/n8n-<date>.tar.gz` есть; нужен свежий).
2. **Снимок «до»** (read-only, с машины разработчика): `python3 Tools/n8n/verify-deployed.py --json docs/audits/<дата>-prod-before.json`
   — фиксирует `versionId`/`activeVersionId` каждого workflow (точки отката) и подтверждает, что список DIFF/MISSING совпадает с ожидаемым.
3. **Версии**: staging проверял n8n 2.27.4, прод — 2.36.7. Известные различия учтены (штамп клона хранится в sticky-note, а не в `meta`;
   `responseCode` — число). Неизвестные различия ловятся шагом 6 (read-back) и canary (шаг 8) — поэтому порядок ниже: сначала
   backend, затем клиент.
4. Зафиксировать `git rev-parse HEAD` пакета в журнале.

## 2. SQL (owner, cred `Postgres`, одноразовый n8n-harness как `apply-landing-leads.py`)

Порядок: `auth-installs` → `reply-mode-version` → `reply-events` → `history-handoff` → `suggestion-request-keys` → `test-dialog`.
Post-check: `select relrowsecurity from pg_class where oid in ('public.installs'::regclass, 'public.reply_events'::regclass,
'public.chat_reply_state'::regclass, 'public.test_dialog_actions'::regclass)` = true ×4; `\d bot_profiles` содержит
`owner_id, workflow_id, claimed_at`; `\d reply_mode_flags` содержит `version`.
Ничего не удаляется и не переименовывается; старые workflows продолжают работать на тех же таблицах.

## 3. Env + рестарт (owner) — runbook §1. Проверка: `/webhook/AuthSession` с `{"op":"nonsense"}` → 400 после шага 4.

## 4. Workflows (с машины разработчика, ключ `Tools/n8n/.secrets/prod-api-key.txt`, ТОЛЬКО по разрешению)

1. **Новые** — импорт 7 файлов из `Tools/n8n/workflows/` с ребиндингом credentials ПО ИМЕНИ (`Postgres`, `WappiAuthToken`,
   `OpenAi account`, `Supabase account`, `n8n Admin API`). `n8n Admin API` (httpHeaderAuth `X-N8N-API-KEY`) уже существует на проде —
   его использует Set Workflow State (`build-client-webhooks.py --deploy` подставляет id вместо `__HDR_CRED__`); Test Dialog
   ссылается на тот же placeholder — подставить тот же id. Активировать: Auth Session, Wappi Proxy, Handoffs, Test Dialog, Handoff Notifier
   (расписание). Handoff Request и Handoff Request Dry — только опубликовать (вызываются инструментом, вебхука нет).
2. **Обновлённые (20)** — `--update` по id (как `build-client-webhooks.py --update`); `Set Workflow State` первым (защищённый список
   включает новые id). После каждого — `activeVersionId == versionId` (это делает `verify-deployed.py`).
3. **Шаблоны** — `4wYitz…` и `4VN3…` одним `--update` каждый. Post-check: экспорт с прода → `python3 Tools/n8n/verify-reply-pipeline.py`,
   `verify-history-handoff.py`, `verify-reply-mode.py`, `verify-auth-gate.py` (все читают файлы, не сеть).
4. **Клоны «13»/«15»** — `python3 Tools/n8n/update-clones.py` (dry-run, печатает diff узлов и сохраняемые поля идентичности:
   имя, path вебхука, systemMessage, settings, credential-id по типам; предыдущий `versionId` — точка отката) → владелец читает →
   `python3 Tools/n8n/update-clones.py --only <id> --apply --yes --base https://n8n.choosereply.com` по одному; инструмент сам
   читает результат назад (набор узлов, path, промпт, штамп `crTemplateVersion`, прежняя активность, `activeVersionId == versionId`).
   Файлы, режимы, история, биллинг и id workflow не меняются (они в БД по profile/workflow id).
5. **Типы вебхуков у существующих профилей** — Create ставит `incoming_message + outgoing_message_phone + outgoing_message_api`
   только новым ботам. Для профилей клонов «13»/«15» (и любых ботов, созданных до пакета) — одноразовый вызов Wappi
   `POST https://wappi.pro/api/webhook/types/set?profile_id=<id>` (tapi для Telegram) с телом
   `["incoming_message","outgoing_message_phone","outgoing_message_api"]` и заголовком `Authorization: <WappiAuthToken>` —
   выполняется владельцем (токен только у него/в n8n), список профилей — `profile/all/get`. До этого история владельца с телефона
   у старых ботов не пишется (в `chat_history_events` просто нет строк `owner_phone`).

## 5. Compat-окно — runbook §3 (`CR_AUTH_COMPAT_UNTIL` = релиз клиента + 30 дней; закрыть, когда `crAuth.mode=legacy` ≈ 0).

## 6. Read-back (обязательно, read-only)

`python3 Tools/n8n/verify-deployed.py --json docs/audits/<дата>-prod-after.json` → ожидание: все канонические `OK`
(допустимые пометки: `admin-API credential id is instance-bound` у Set Workflow State, `recipient chat id replaces __CHAT_ID__`
у Support Message), клоны `at_canonical: true` со штампом, `unknown: 0`. Любой DIFF — остановиться, не продолжать клиентом.

## 7. Клиент

Релиз iOS/Android с этой ветки; **до отправки** `Tools/store/inspect-artifact.sh <aab|ipa>` — в артефакте нет `wappiAuthToken`
и `n8nAPIKey`. Экраны этапа 7 (лист «Проверка бота», строка «Проверить бота», последние исходы) на устройстве НЕ проверены —
первый device-pass делает владелец/тестовый владелец по шагу 8; до него кнопки считаются «code-complete, unverified».

## 8. Canary (тестовый владелец, свои номера, никаких клиентов)

1. Свежая установка → создание бота через прокси → «Проверить бота»: ответ приходит, `sources` названы, «Действие» отражает
   заявку/ответ, `sent:false` (в чате клиента ничего не появилось), `ID` = execution id на проде.
2. Одно входящее с тестового номера → один ответ; `reply_events` одна строка `state='sent'` с `transport_message_id`;
   «Последние ответы» в листе показывают «Отправлено».
3. Фото без текста → ответ по-русски «Напишите, пожалуйста, текстом…» (не «Please send text messages»).
4. «Вместе» во время генерации → ответ не уходит, строка `cancelled_mode`.
5. Ответ владельца с ТЕЛЕФОНА → строка `owner_phone` в `chat_history_events` (canary формы outgoing-событий — runbook §2.6:
   при `other:*` в `Outcome` поправить `RECORD_OUTGOING_PARAMS`).
6. Заявка (`create_handoff`) → одна строка `handoff_requests`, self-message на канале бота (единственный непроверенный на
   устройстве элемент), пауза чата до явного «Авто»/`done`.
7. Существующий бот (клон) после 4.4 → claim по коду → всё выше повторить на нём.

## 9. Откат (по слоям, независимо)

| Слой | Как | Потери |
|---|---|---|
| Workflows | вернуть `versionId` из снимка «до» (`workflow_publish_history` / бэкап) — по одному | нет; таблицы аддитивны |
| Клоны | `versionId` до обновления (печатается инструментом); промпт/path не менялись | нет |
| Гейты A1 | оставить, но `CR_AUTH_COMPAT_UNTIL` в будущее — старые клиенты работают | нет |
| Test Dialog / Handoffs status | деактивировать — клиент показывает честную ошибку («Не удалось проверить»), остальное не зависит | нет |
| SQL | не откатывать (аддитивно) | — |
| Клиент | предыдущий билд работает в compat-окне | — |

Откат не открывает доступ шире, чем было до A1, и не создаёт повторных отправок (транспорт отправки не менялся; ledger
не читается старыми шаблонами).

## 10. Действия только владельца (сводно)

- env-переменные и `CR_AUTH_SECRET` (§3), SQL (§2), credentials на проде (§4.1), типы вебхуков у существующих профилей (§4.5);
- ротация Wappi-токена ПОСЛЕ релиза клиента (runbook §2.10) — до неё A1 формально не закрыт;
- push-ключи (APNs .p8 / Firebase) — внешний блокер (runbook §2.11), без них push не объявляется;
- закрытие compat-окна (§5); публикация клиента в сторах; device-pass этапа 7 (§7–8).

## 11. Что НЕ сделано / не проверено (честно)

- Прод не менялся; ничего из пакета не применено.
- Клиентские экраны этапа 7 и self-message заявки — без device-pass.
- Push — нет.
- Пропуск версии n8n staging → прод (2.27 → 2.36) — закрывается read-back'ом и canary, не тестом.
- Eval промптов измерен на staging-прокси синтетическими сценариями (`Tools/n8n/eval/`), не на реальных диалогах.
