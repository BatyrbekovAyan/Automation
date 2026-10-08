# A1 — пакет развёртывания (готовится этапом 1, исполняется этапом 7 по отдельному разрешению)

Статус: **РАЗВЁРНУТО 2026-09-24** (см. `release-package.md` и журнал). Исторически: прод (`https://n8n.choosereply.com`) не трогался до этой даты; всё ниже
проверено на изолированном staging (`Tools/n8n/staging/`), см. `docs/audits/implementation-progress.md`.
**Сводный порядок всех этапов (0–7) с проверками, canary и откатом — `docs/audits/release-package.md` (2026-09-22);**
этот файл остаётся источником для env (§1), compat-даты (§3) и деталей A1.

## 0. Что меняется
| Слой | Изменение | Артефакт |
|---|---|---|
| Postgres (Supabase, cred `Postgres`) | + `installs`, + `uploaded_files`, + колонки `bot_profiles.owner_id/workflow_id/claimed_at/claim_*` (аддитивно, идемпотентно); **A2:** + `reply_mode_flags.version`; **A3/A4/A7:** + `reply_events` (журнал событий/состояний ответа), + `chat_reply_state` (поколение входящих + 90-с lease на чат), RLS default-deny; **A5/A6:** + `chat_history_events` (единая история с источником client/ai/owner_app/owner_phone/outgoing_api), + `handoff_requests` (заявки + состояние уведомления), + `chat_reply_state.handoff_paused`; **S3:** + `suggestion_request_keys` (идемпотентный дневной счётчик подсказок) | `Tools/n8n/sql/2026-09-17-auth-installs.sql`, `…-reply-mode-version.sql`, `…-reply-events.sql`, `…-history-handoff.sql`, `…-suggestion-request-keys.sql` |
| n8n env (docker-compose) | `NODE_FUNCTION_ALLOW_BUILTIN=crypto`, `N8N_BLOCK_ENV_ACCESS_IN_NODE=false`, `CR_AUTH_SECRET=<64 hex>`, `CR_AUTH_COMPAT_UNTIL=<ISO, см. §3>`, (опц.) `CR_AUTH_SECRET_PREV`, `N8N_PAYLOAD_SIZE_MAX=32`; **этап 5:** `EXECUTIONS_TIMEOUT=120`, `EXECUTIONS_TIMEOUT_MAX=300` (иначе per-workflow `executionTimeout` Suggest Replies = 20 с не действует) | §1 |
| n8n workflows (новые) | `crAuthSession001` Auth Session (`/webhook/AuthSession`), `crWappiProxy0001` Wappi Proxy (`/webhook/WappiProxy`); **этап 4:** `crHandoffReq0001` Handoff Request (цель инструмента `create_handoff`, без вебхука), `crHandoffNtfy001` Handoff Notifier (расписание каждые 2 мин — self-message владельцу), `crHandoffsApi001` Handoffs (`/webhook/Handoffs`, list/take/done, A1-гейт) | `Tools/n8n/workflows/crAuth*.json`, `crHandoff*.json` (`build-handoff-workflows.py`) |
| n8n workflows (обновлённые, 12 + этап 6: Create/Edit WA/TG (карта промптов `inject-prompts.py`) и Suggest Replies (панельные промпты `inject-panel-prompts.py`) + Suggest Replies этапом 5: контракт знаний с бюджетом, восстановление предмета + точное совпадение артикула (`Has Exact Tokens?`/`Exact Match`), серверное время в зоне бизнеса, идемпотентный счётчик, RAG fail-open, виды ошибок для токен-вызовов, `executionTimeout` 20 с — `apply-suggest-v2.py`) | Auth Gate → Authorized? → 401/403 перед первым узлом; Create WA/TG + `Bind Workflow Id`; Upload File + `Register Upload`; Delete File owner-scoped; Set Workflow State защищает новые id; **A2:** Set Reply Mode v2 (версионный атомарный upsert, отчёт per profile, 400/500), Create WA/TG + `Seed Reply Mode` до активации | `Tools/n8n/apply-auth-gate.py`, `Tools/n8n/apply-reply-mode-v2.py` (идемпотентно) |
| шаблоны ботов + клоны «13»/«15» | `If` требует `profile_id == path вебхука`; узел `Outcome` (этап 0.5); **этап 4:** маршрутизация `wh_type` (`Is Incoming?`/`Is Outgoing?`/`Outgoing Bound?`), `Record Outgoing` (телефон владельца → история + память), `Record Suppressed` («Вместе» → история + память клиента), история в `Claim Event`/`Record Send`/`Record Cancel`, `Read Reply Mode` учитывает паузу заявки, инструмент `Create Handoff` у AI Agent + правило в системном промпте; **этап 3:** `Claim Event`/`Claimed?` (однократная обработка события), `Generated?`, `Final Fetch`/`Final Check`/`Final OK?` (финальная проверка контекста перед отправкой), `Acquire Send Lease`/`Send Allowed?` (режим + поколение + lease одним запросом), send с `fullResponse/neverError/30 с`, `Settle Send` → `sent | send_failed | send_unknown`, `Wait Reconcile`/`Reconcile Fetch`/`Reconcile Check` (эхо ⇒ sent, иначе send_unknown — БЕЗ повторной отправки), `Record Send`/`Cancel Label`/`Record Cancel` (журнал + удаление AI-строк памяти отменённой генерации), Mark Read/Typing/AI Agent fail-open, паузы ≤ 8/12/12 с | `Tools/n8n/apply-reply-pipeline-v2.py` (идемпотентно); клоны — этап 7 |
| клиент | сессия установки (AuthClient), все вызовы с `Authorization: Bearer`, Wappi только через прокси, `wappiAuthToken` удалён из `secrets.json`, claim наследуемых ботов | коммиты 5448f98d, далее |

## 1. Env на VPS (owner, ~5 минут, не в git)
```bash
ssh choosereply
cd ~/choosereply && cp docker-compose.yml docker-compose.yml.bak-$(date +%F)
# в секцию environment сервиса n8n добавить:
#   - NODE_FUNCTION_ALLOW_BUILTIN=crypto
#   - N8N_BLOCK_ENV_ACCESS_IN_NODE=false
#   - CR_AUTH_SECRET=<openssl rand -hex 32>
#   - CR_AUTH_COMPAT_UNTIL=2026-12-31T00:00:00Z     # см. §3 — дата закрытия legacy-доступа
#   - N8N_PAYLOAD_SIZE_MAX=32                        # media.send через прокси (b64 до ~24 МБ)
#   - EXECUTIONS_TIMEOUT=120                         # этап 5: включает per-workflow таймауты (Suggest Replies = 20 с)
#   - EXECUTIONS_TIMEOUT_MAX=300
docker compose up -d n8n && docker compose logs --tail 20 n8n
```
Проверка: `curl -s -X POST https://n8n.choosereply.com/webhook/AuthSession -H 'Content-Type: application/json' -d '{"op":"nonsense"}'`
→ `400 {"success":false,"error":"bad_request"}` (после шага 2). Секрет нигде в репо не появляется.

## 2. Порядок развёртывания (этап 7)
1. **Бэкап** — ночной `~/backups/n8n-<date>.tar.gz` уже есть; перед деплоем снять свежий: `~/choosereply/backup.sh`.
2. **SQL** — `2026-09-17-auth-installs.sql`, `2026-09-17-reply-mode-version.sql`, `2026-09-17-reply-events.sql`, `2026-09-17-history-handoff.sql`, `2026-09-17-suggestion-request-keys.sql` через cred `Postgres` (одноразовый n8n-harness, как `apply-landing-leads.py`).
   Post-check: `select relrowsecurity from pg_class where oid in ('public.installs'::regclass, 'public.reply_events'::regclass, 'public.chat_reply_state'::regclass)` = true×3.
   Стоимость на проде: каждый Postgres-узел ≈ 1,9 с (pooler) — путь ответа этапа 3 добавляет 3 узла на отправку (Claim, Lease, Record) и 2 на отказ; суммарная задержка ответа ≤ ~40 с при клампах пауз (8/12/12 с).
3. **Env** — §1, рестарт n8n.
4. **Новые workflows** — импорт `crAuthSession001`, `crWappiProxy0001`, `crHandoffReq0001`, `crHandoffNtfy001`, `crHandoffsApi001` с ребиндингом credential-id по имени
   (Postgres → `vvRrFiEXzLVqKjOx`, WappiAuthToken → `ZowntFGvApDJ7UzQ`), публикация/активация (Handoff Request вызывается инструментом — тоже опубликовать; Notifier — расписание); smoke: bootstrap → refresh →
   `WappiProxy status` на СВОЁМ профиле (read-only); `Handoffs list` на своих профилях → `{success:true, requests:[]}`.
   **Wappi Proxy** обновлён (`--update`): после ответа пишет отправки приложения в историю/память (Record Owner Send).
5. **Гейтированные 12 workflows** — `--update` по id (как `build-client-webhooks.py --update`), `activeVersion == versionId`
   проверить чтением. Compat-окно открыто ⇒ старые клиенты продолжают работать (legacy mode, лог `crAuth.mode`).
6. **Шаблоны ботов** (`4wYitz…`, `4VN3…`) — `--update` (этапы 0.5 + 1 + 3 + 4 вместе: один шаблон = один `--update`); **типы webhook у существующих профилей** — Create ставит `incoming_message + outgoing_message_phone + outgoing_message_api` только новым ботам; для существующих профилей (клоны «13»/«15» и все боты владельцев) нужен одноразовый прогон `webhook/types/set` по списку профилей (`profile/all/get` → set) — часть управляемого обновления клонов (этап 7.5); до него история владельца с телефона у старых ботов не пишется (границы покрытия честно видны: строк `owner_phone` нет).
   **Canary для формы outgoing-событий:** документация Wappi не показывает пример `outgoing_message_*`; реализация опирается на форму входящего (`wh_type`, `id`, `chatId`/`to`, `body`, `type`, `time`). Первый реальный outgoing-ивент на проде проверить по `Outcome` (`recorded_owner_phone` / `outgoing_known`, а не `other:*`) и по строке в `chat_history_events`; при расхождении — поправить `RECORD_OUTGOING_PARAMS` в `apply-history-handoff.py` (один узел). клоны «13»/«15» — управляемое обновление по имени узлов (этап 7.5). До обновления клонов старые клоны продолжают работать по прежней схеме (без журнала `reply_events`) — таблицы аддитивны.
   Post-check шаблонов: `python3 Tools/n8n/verify-reply-pipeline.py` на экспортах с прода; smoke на тестовом владельце — одно входящее ⇒ одна строка `reply_events.state='sent'` с `transport_message_id`.
   **Этап 7 (2026-09-22):** + SQL `2026-09-22-test-dialog.sql`; + workflows `crTestDialog0001` (Test Dialog, `/webhook/TestDialog`, credential `n8n Admin API` — тот же `__HDR_CRED__`, что у Set Workflow State) и `crHandoffDry0001` (sub-workflow, только опубликовать); Handoffs получил op `status`; Set Workflow State защищает оба новых id. Клоны «13»/«15» обновляются `Tools/n8n/update-clones.py` (dry-run → `--apply --yes --only <id>`; печатает `versionId` до обновления как точку отката; читает результат назад), read-back всего инстанса — `Tools/n8n/verify-deployed.py --json …`.
   **V12 (итерация 2026-09-28, 2026-10-07) — удаление бота владельцем и чистка клонов; СЕРВЕР раньше клиента:**
   SQL `2026-10-07-retired-clone-deactivate.sql` (колонка `bot_profiles.clone_deactivated_id`; до Sweep). Workflows:
   Delete Bot Files (`lmjYsdNcQA2IE5rl`) и Wappi Proxy (`crWappiProxy0001`) — `deploy-canonical.py --only <id>`;
   Set Workflow State (`0DP79STI3pafXO9B`) — `build-client-webhooks.py --update` (placeholder-workflow);
   Profile Lifecycle Sweep (`fXYpCXPKw92EzRz8`) — `deploy-canonical.py --only fXYpCXPKw92EzRz8`: инструмент сам
   подставляет id credential `n8n Admin API` из живого Set Workflow State вместо `__HDR_CRED__` и отказывается без него
   (так же теперь и Test Dialog). Миграция токена не нужна (token.js/gate.js не менялись). Post-check: `verify-deployed.py`
   (инвентарь: активные клоны, привязанные к удалённым строкам, после первого прогона Sweep — 0; бэкфилл не нужен, живых
   клонов на 2026-10-06 нет). Smoke: удаление бота с истёкшим триалом session-клиентом → 200 + счётчики; чужой
   токен → 403; повторное удаление удалённого клона → 200. Новый клиент (часть c — удаление только по вердикту
   сервера, «Удалить только с телефона») выпускать ПОСЛЕ сервера: он полагается на 503-vs-403 и идемпотентный delete.
   Данные ботов, удалённых Sweep'ом, ХРАНЯТСЯ (решение владельца 2026-10-07); автоудаление — отдельное решение.
   Остатки V12: строки без владельца (legacy, никогда не заявленные) и бот после переустановки без claim удаляются
   только по запросу в поддержку (приложение честно говорит это и даёт «Удалить только с телефона»); ложное
   срабатывание liveness-ветки Sweep не удаляет ещё живой профиль Wappi; определённый отказ Wappi на `profile/delete`
   живого профиля даёт продолжение удаления (профиль может остаться привязанным — серверной сети для `app_delete` нет);
   F1 (лишние клоны при повторной авторизации) и M4 (бесконечный `claim.start` у «зомби»-бота после окончания
   триала) — отдельные находки.
7. **Клиент** — релиз iOS/Android с A1; **до отправки** прогнать `Tools/store/inspect-artifact.sh <aab|ipa>` — артефакт
   без `wappiAuthToken`.
8. **Canary** — тестовый владелец: свежая установка → создание бота через прокси → чат → удаление; существующий бот
   → claim по коду из «Избранного» (device-проверка самосообщения WA/TG — единственный непроверенный элемент).
9. **Закрытие окна** — когда доля старых клиентов ≈ 0 (по `crAuth.mode=legacy` в логах): `CR_AUTH_COMPAT_UNTIL` в прошлое,
   рестарт; token-less → 401 (проверено на staging).
10. **Ротация Wappi-токена** — кабинет Wappi → новый токен → обновить credential `WappiAuthToken` на n8n (значение только
    в n8n) → старые APK/IPA теряют прямой доступ. До этого A1 формально НЕ закрыт (инструкция §Этап 1.6–1.7).

### 2.11 Уведомления владельца — что есть и чего нет
- **Есть (этап 4):** заявка = строка `handoff_requests` (создаётся инструментом агента ОДИН раз на событие); уведомление владельцу — self-message на собственный номер/аккаунт бота (WhatsApp: «Сообщения себе», Telegram: «Избранное»), доставляется расписанием каждые 2 мин, до 6 попыток с интервалом 10 мин; текст без переписки клиента и без его номера (только вид заявки и последние 4 цифры чата). В приложении: список ожидающих заявок на «Сводке» (метка «Заявка ·», реальный источник `event`), пауза автоответа в чате до явного «Авто» владельца или `done`, звук/вибрация при появлении новой заявки (по настройкам «Уведомления»).
- **Нет (внешний блокер):** push (APNs/FCM). Нужны: Apple Push key (.p8) + Team/Key ID, Firebase-проект + `google-services.json`, пакет уведомлений в Unity (com.unity.mobile.notifications или Firebase Messaging), регистрация токена устройства через `/webhook/AuthSession` (новый op `push.register`, attach к install), доставка из Notifier'a (APNs/FCM вместо/вместе с self-message), device-тест на реальных устройствах с тестовым владельцем. Без ключей push НЕ объявляется готовым; self-message — единственный проверенный на staging (моком) канал для закрытого приложения, на устройстве не проверен.
- **Отказ в разрешении на уведомления (когда появится push):** честный fallback уже существует — self-message на канале бота + список заявок в приложении; UI не обещает push.

## 3. Compat-дата
Предложение: `CR_AUTH_COMPAT_UNTIL` = дата релиза клиента + 30 дней. До неё legacy-вызовы без токена проходят
на 12 вебхуках (как сегодня), после — 401. Прокси legacy-режима не имеет (старые клиенты ходят в Wappi напрямую
до ротации токена).

## 4. Откат
- Workflows: вернуть предыдущие версии (`versionId` из бэкапа / `workflow_publish_history`), либо оставить гейты и
  передвинуть `CR_AUTH_COMPAT_UNTIL` в будущее — доступ старых клиентов восстанавливается без потери данных.
- SQL аддитивна: таблицы/колонки остаются, ничего не ломают старым workflows.
- Клиент: предыдущий билд продолжает работать в compat-окне (прямой Wappi + legacy-вебхуки).
- Откат не открывает неавторизованный доступ дольше, чем он был до A1, и не дублирует отправки (транспорт не менялся).
- Этап 3: откат шаблонов = предыдущая версия workflow; строки `reply_events`/`chat_reply_state` остаются (аддитивно, ничего не читает их кроме новых узлов). Просроченный lease (90 с) не блокирует после отката, потому что старый шаблон его не читает.

## 5. Остаточные риски / owner-действия
- **Самосообщение** (`message/send` на собственный номер) для claim-кода — не подтверждено документацией Wappi; при
  неудаче на устройстве владелец использует путь re-auth (подключить канал заново) — реализован.
- **Rate-limit** на вебхуках: Caddy без плагина; DB-счётчик стоит ~1,9 с — отложено; bootstrap ограничен 60/мин.
- **PlayerPrefs** как хранилище install-секрета: per-install, отзываемо; при рутованном устройстве утекает только
  свой install (не общий токен, как раньше).
- **RevenueCat app_user_id** остаётся биллинговым ключом. Привязка к install (V3, итерация 2026-09-28): только refresh
  (секрет доказан; bootstrap ничего не привязывает), один install на id, переход — ТОЛЬКО от отозванного держателя.
- **Потеря секрета установки без «Удалить все данные»** (порча PlayerPrefs, ручная очистка; «Удалить все данные» установку
  сохраняет): новая установка получает `billingConflict` (без плана/квоты/слотов), пока поддержка не отзовёт мёртвую:
  убедиться по `bot_profiles`/`installs.last_seen_at`, что держатель действительно мёртв, затем через `sql-oneoff.py`
  `update installs set revoked_at = now() where install_id = '<мёртвая установка>';` — id перейдёт на следующем refresh новой
  (клиент повторяет его при возврате в приложение). Ботов новая установка забирает кодом из чата бота (claim).
