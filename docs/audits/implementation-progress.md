# Ход ремедиации аудита 2026-09-15/16 — журнал и точка продолжения

План этапов: `docs/superpowers/plans/2026-09-17-audit-remediation-plan.md`.
Статусы: planned · in_progress · locally_verified · staging_verified · deployed_verified · blocked.
Правило записи: пункт аудита → этап → изменения → доказательства → оставшиеся ограничения.

## Точка продолжения (обновляется в конце каждого этапа)

- **Дата:** 2026-09-17 · **Ветка/коммит:** `main` @ `95221844` (исходный HEAD; работа идёт на `main`
  scoped-коммитами — единый рабочий каталог, без worktree; перед каждым коммитом `git rev-parse HEAD`).
- **Текущий этап:** 4 (A5/A6) — код ЗАВЕРШЁН и staging_verified 2026-09-17 (push = внешний блокер, runbook §2.11); НЕ развёрнут.
  Следующий: этап 5 (S1, S3–S6 — контракт знаний и подготовка подсказок).
- **Коммиты:** этап 1 — d44fc037, 5448f98d, e3aec245, 616e6461; этап 2 — b892b83d, 230392e8, f456e1b9; этап 3 — см. git log
  (`feat(reply-pipeline)`).
- **Как продолжить:** `Tools/n8n/staging/pg.sh start` → `python3 Tools/n8n/staging/wappi-mock.py &` →
  `python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` → `python3 Tools/n8n/staging/e2e.py`
  → `python3 Tools/n8n/staging/e2e-reply.py` (этап 3, ~15 мин) → `python3 Tools/n8n/staging/e2e-handoff.py` (этап 4, ~8 мин);
  чистый харнесс — `node Tools/n8n/repro/run-code-node.mjs`; verify-скрипты: batching, parity, rag, panel-prompts, auth-gate,
  reply-mode, reply-pipeline, history-handoff; builder-проверки `build-auth-workflows.py --check`, `build-handoff-workflows.py --check`,
  `inject-prompts.py --check` (нужен tiktoken: `~/.venvs/tiktoken/bin/python3`); Unity — `Tools/run-tests-headless.sh`.
  Ожидаемое состояние ПОСЛЕ этапа 4: repro 15 pass / 2 fail (S4 subject, no-identity — цели этапа 5), e2e.py 11/11,
  e2e-reply.py 28/28, e2e-handoff.py 14/14 (прогон 2: 13/14, notifier исправлен на per-item и повторён 1/1), Unity 2651/2651. Мок надо перезапускать после правок `wappi-mock.py`.

## Baseline (этап 0.1) — зафиксировано 2026-09-17

- Git: HEAD `95221844` («docs(store): ревью №2 …»). Рабочие изменения на старте — только untracked:
  `.agents/skills/*`, `.codex/`, `AGENTS.md` (копия CLAUDE.md для Codex), `d.html`, `z.html`,
  `docs/audits/`, `docs/gtm/`. Ничего из этого не трогаем и не коммитим вместе с задачей.
- Unity Editor закрыт (нет `Temp/UnityLockfile`, только Unity Hub) → тесты через `Tools/run-tests-headless.sh`.
  **Baseline C#-suite 2026-09-17: 2593/2593 passed, 0 failed, 0 skipped (headless, 30,4 с)** — `Tools/test-output/results.xml`.
- PROD n8n: `https://n8n.choosereply.com`, версия **2.36.7** (Docker `docker.n8n.io/n8nio/n8n`, VPS `ssh choosereply`,
  диск 9/40 ГБ). 21 workflow: 19 канонических + 2 клона WhatsApp-шаблона: `tqGXmiT6aJu7vUIG` «13» (active=false)
  и `OcR75Ug0upgdfrqN` «15» (active=true; versionId == activeVersionId `cccb7e10…`). У всех активных
  канонических `versionId == activeVersionId` (опубликовано то, что читается). Полный инвентарь с
  typeVersion узлов: `~/backups/choosereply-n8n/2026-09-17-stage0/_inventory.json`; полные определения —
  там же (вне git, файлы 0600, каталог 0700). Ночной бэкап на VPS: cron 03:15 → `~/backups/n8n-YYYY-MM-DD.tar.gz`
  (последний 2026-09-17, 14 дней ротации; off-site копии нет).
- Канонические JSON ↔ прод: параметры и связи совпадают у всех 19; отличия только (а) credential-id
  (прод: WappiAuthToken `ZowntFGvApDJ7UzQ`, OpenAi `WNHwKWlO2E9OClkA`, Supabase `vrywn6AxQMlvbbzC`,
  n8n account `hG8UUsDx8EHS1xbB`, n8nAPIKey `nAmEcPrSbxIVDPFd`, Support Bot `VJkldzm5hNSnzsnm`,
  n8n Admin API `jGvQStWZQ3KHkrO7`; канон несёт старые Cloud-id) и (б) `Support Message` chatId
  (штатная подстановка `__CHAT_ID__`). Клоны «13»/«15» = шаблон + свой webhook path + systemMessage
  + прод-credential-id; связи и набор узлов идентичны шаблону.
- Версии узлов бот-шаблона: webhook 2.1, httpRequest 4.2, code 2, if 2.2, switch 3.2, set 3.4, wait 1.1,
  postgres 2.6, langchain agent 1.8 / lmChatOpenAi 1.2 / memoryPostgresChat 1.3 / vectorStoreSupabase 1.3 /
  embeddingsOpenAi 1.2 / openAi 1.8. Модель автоответа `gpt-5.4-mini`, maxTokens 200; панель — LLM через httpRequest.
- Миграции (по репо; фактическое состояние прод-БД напрямую не читалось — Supabase MCP не авторизован):
  `Tools/n8n/supabase/`: 2026-07-02 harden-rag-store, price-list-originals-bucket, 2026-07-07 conversation-outcomes,
  2026-07-19 reply-mode-flags; `Tools/n8n/sql/`: 2026-08-21 billing-schema, billing-rls, bot-key, deleted-reason,
  2026-08-22 product-id, 2026-08-26 suggestion-counts, 2026-08-27 landing-leads.
- Локальный dev: n8n **2.27.4** (`~/.n8n`, остановлен с 2026-08-27); его Postgres-credential `vvRrFiEXzLVqKjOx`
  указывает на ТУ ЖЕ Supabase-БД, что и прод → как staging без изоляции БД непригоден; Docker/psql на машине нет.
- Baseline-метрики прод-выполнений (только чтение, без текста): `docs/audits/2026-09-17-baseline-metrics.json`.
  Бот «15»: 232 выполнения (11–14.09) — filtered 130, suppressed 102; бот «13»: 305 (10–14.09) — filtered 177,
  suppressed 128; Suggest Replies: 1 (generation_failed, smoke аудитора). **Ни одной генерации/отправки в
  удержанном окне** — т.е. до этапа 3 нет прод-данных о задержке генерации; Read Reply Mode p50 ≈ 1,9 с (сетевой
  RTT до Supabase-пулера с VPS) — это цена одного gate-чтения.

## Этап 0 — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 0.1 baseline | locally_verified | `Tools/n8n/baseline-metrics.py` (новый, read-only) | инвентарь + бэкап вне git; метрики в `docs/audits/2026-09-17-baseline-metrics.json` | состояние прод-БД (миграции) не прочитано напрямую |
| 0.2 verify-скрипты | locally_verified | `verify-message-batching.py`: winner → Count Dialog → Quota Decision → If Quota Allows → Input type, прямой обход гейта = FAIL; `verify-telegram-parity.py`: `CANON_HOST=https://n8n.choosereply.com` (+ `lstrip("=")` для expression-url), запрет `bagkz.app.n8n.cloud` | все 4 команды exit 0 целиком (см. журнал ниже) | `--dir <prod-export>` по-прежнему падает на credential-id (прод-id отличаются по замыслу) — режим go/no-go для прод-реэкспорта требует cred-профиля (взять в этап 7) |
| 0.3 smoke SuggestReplies | staging_verified | staging-стенд `Tools/n8n/staging/` (pg.sh, wappi-mock.py, derive-staging.py, n8n-staging.sh, bootstrap.py, fixtures.py, e2e.py, README); `probe-suggest-replies.py`: identity ТОЛЬКО из `PROBE_APP_USER_ID` (без неё — отказ запускать quality-пробы), новый негативный `IDENTITY_no_app_user_id_refused`, `--refusal-only` | staging: 19 канон + 2 staging-бота (n8n 2.27.4 local, Postgres 16.2 :5433, Wappi-мок :8765; в производных JSON `wappi.pro`/`n8n.choosereply.com` отсутствуют — derive отказывает при остатке); probe с fixture `probe-<runid>`: **struct_fails=0, heuristic_warns=2** (G_kazakh_mirror, R_explore_direction_diversity — сэмплинг), refusal-probe OK; `suggestion_counts` строка создана только для fixture и удалена по её id | n8n 2.36.7 (прод) собрать локально не удалось (`isolated-vm` не собирается под Node 25.9) — стенд на 2.27.4 при тех же typeVersion узлов; Support Bot cred намеренно отсутствует |
| 0.4 репро-харнесс | staging_verified (RED как задумано) | `Tools/n8n/repro/run-code-node.mjs` + `scenarios.json` — 11 сценариев × шаблоны, чистый JS без n8n | `node Tools/n8n/repro/run-code-node.mjs` → **13 pass, 4 fail**: FAIL = `combine_human_reply_after_trigger_aborts` (WA+TG, A3), `prep_short_follow_up_keeps_subject` (S4), `prep_missing_identity_is_marked_invalid_before_the_gate` (0.3); контрольные (newer incoming, foreign chat, burst, owner-reply-before-trigger) — PASS | staging `e2e.py`: **7 pass / 3 fail** — FAIL = `setreplymode_invalid_is_4xx` (A2: HTTP 200 + success:false), `bot_duplicate_delivery_sends_once` (A4: 2 отправки на мок), `bot_human_reply_during_debounce_aborts` (A3: 1 отправка); PASS = single→1 send, newer incoming→0, suppressed→0, SR no-identity/unknown→отказ без LLM (gate=missing_app_user_id/unknown_account), SR с identity→4 карточки |
| 0.5 диагностика исходов | staging_verified | `apply-outcome-stamps.py` (идемпотентно): узел `Outcome` в обоих шаблонах на 5 терминальных ветках (If[false]=filtered, Suppressed?[true]=suppressed, Is Latest?[false]=superseded, If Quota Allows[false]=quota_denied, HTTP Request→sent) с `correlationId=$execution.id`, `stage`, ids, `transportMessageId`; verify-скрипты закрепляют проводку; `baseline-metrics.py` читает штамп | staging exec 6: `{"outcome":"sent","stage":"HTTP Request","transportMessageId":"sent-000001",…}`, exec 10: `superseded`, exec 13: `suppressed`; baseline-метрики сняты ДО (см. 0.1) | send_failed/send_unknown/generation_failed добавит этап 3 (сейчас HTTP Request без onError останавливает выполнение — как и было); прод-клоны 13/15 штамп получат при обновлении клонов (этап 7) |

**Приёмка этапа 0 — выполнена:** baseline зафиксирован (git/n8n/узлы/миграции/клоны/метрики); 4 verify-скрипта
зелёные без ослабления (host закреплён точно, путь через billing-gate закреплён по узлам); дефекты воспроизводятся
детерминированно (repro + staging e2e); тестовая отправка физически не может уйти клиенту (транспорт = мок на
127.0.0.1, в производном наборе нет ни одного внешнего хоста; отправка сценария легла в мок как `sent-000001`).

## Журнал проверок

- 2026-09-17 · до правок: `verify_rag.py all` → OK; `verify-panel-prompts.js` → PASS; `verify-message-batching.py`
  → FAIL «Is Latest? main[1] (winner) does not proceed to Input type»; `verify-telegram-parity.py` → FAIL
  «Activate Created Workflow's url is not the canonical bagkz host».
- 2026-09-17 · после правок: `verify_rag.py all` → `VERIFY OK: all` (exit 0); `verify-panel-prompts.js` → `PASS`
  (exit 0); `verify-message-batching.py` → `ALL BATCHING ASSERTS PASSED` (exit 0); `verify-telegram-parity.py`
  → `ALL PARITY ASSERTS PASSED` (exit 0, все 14 групп ассертов, включая dialog-metering, model id, Suggest Replies
  и RC transfer carry gate — т.е. за первой устаревшей ошибкой других не скрывалось). Первый прогон после правки
  хоста упал на `=https://…` (expression-url с ведущим `=`) — исправлено `lstrip("=")`, старый `in`-ассерт
  этого не различал.
- 2026-09-17 · Unity headless full suite: 2593/2593 passed (baseline до изменений C#).
- 2026-09-17 · `node Tools/n8n/repro/run-code-node.mjs` → 13 pass / 4 fail (A3 ×2 шаблона, S4, no-identity) — RED по замыслу.
- 2026-09-17 · staging `python3 Tools/n8n/staging/e2e.py` → 7 pass / 3 fail (A2 4xx, A4 dup, A3 human) — RED по замыслу;
  `bot_suppressed_mode_no_send` первый раз падал из-за бага харнесса (режим сбрасывался повторным `ctx.bot`), исправлено, PASS.
- 2026-09-17 · staging `PROBE_APP_USER_ID=<fixture> probe-suggest-replies.py` → struct_fails=0, heuristic_warns=2; `--refusal-only` → OK.
- 2026-09-17 · после `apply-outcome-stamps.py`: batching/parity/rag/panel — все 4 зелёные (verifiers расширены на штамп).

## Этап 1 (A1) — состояние

Дизайн: `docs/superpowers/specs/2026-09-17-a1-server-identity-design.md`. Карта клиентских вызовов (агент, 2026-09-17):
60 сайтов — 33 Wappi с общим токеном в 8 файлах (19 hardcoded-литералов мимо `WappiEndpoints`), 12 n8n-вебхуков без auth,
медиа/аватары/превью — по подписанным URL без заголовка. Факты, определившие дизайн: Wappi — один токен аккаунта, у вебхуков
нет подписи; прод n8n — Postgres-узел ≈1,9 с (Get Usage p50 1,92 с) → гейт без БД (HMAC capability-токен).

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 1.1 карта endpoint→действие→объект→права | locally_verified | таблица вебхуков (`webhook path / auth / поля`) + карта клиента | этот журнал, спец | — |
| 1.2 identity/сессия | staging_verified | `sql/2026-09-17-auth-installs.sql` (installs, bot_profiles.owner_id/workflow_id/claim_*, uploaded_files), `auth/token.js` (+`token.test.mjs`), `build-auth-workflows.py` → `crAuthSession001-Auth_Session.json` (bootstrap/refresh/claim.start/claim.verify/claim.status) | `e2e-auth.py`: bootstrap+refresh OK, wrong secret/unknown install → 401, malformed → 400 | env на проде: `NODE_FUNCTION_ALLOW_BUILTIN=crypto`, `N8N_BLOCK_ENV_ACCESS_IN_NODE=false`, `CR_AUTH_SECRET`, `CR_AUTH_COMPAT_UNTIL` (owner-шаг, этап 7); claim-потоки (код в самосообщении / re-auth) требуют device-проверки |
| 1.3 владелец ↔ объекты | staging_verified | `apply-auth-gate.py`: гейт на 12 вебхуках (RULE per workflow), `Bind Workflow Id` в Create WA/TG, `Register Upload` (uploaded_files), owner-scoped `Delete File Chunks` + `Allowed?` перед удалением из bucket; `verify-auth-gate.py` | `e2e-auth.py hooks_reject_foreign_ids`: чужие ids → 403 (SetReplyMode/Dashboard/GetUsage/SetWorkflowState/DeleteBotFiles), свои → 200; compat CLOSED → без токена 401; compat open → legacy 200 | Create/Edit (`lastNode`) отказывают телом `{success:false,error:"unauthorized"}` при HTTP 200 — как их же `channel_limit`; новый клиент читает тело |
| 1.4 Wappi через сервер | staging_verified | `crWappiProxy0001-Wappi_Proxy.json`: allow-list 19 операций, валидация параметров, `profile_id ∈ p` (auth-ops — и `∈ c`), `profile.add` привязывает профиль владельцу и возвращает обновлённый токен, `fullResponse` Wappi пробрасывается, успешные выполнения не сохраняются | `e2e-auth.py`: без/поддельный/мусорный токен → 401 и 0 вызовов транспорта; изоляция A/B по 10 операциям → 403 и 0 вызовов; свои ops → транспорт с серверным credential; unknown op/channel mismatch → 400 | медиа-байты клиент качает по подписанным URL напрямую (как сейчас); `media.send` через n8n требует `N8N_PAYLOAD_SIZE_MAX≥32` на проде |
| 1.5 callback | staging_verified | `If` шаблонов: `profile_id == path вебхука` (оба шаблона) | `e2e.py bot_callback_profile_mismatch_filtered`: outcome=filtered, gate не читался, 0 отправок | подписи у Wappi нет (документация проверена 2026-09-17); rate-limit — Caddy без плагина (owner: xcaddy) |
| 1.6 миграция/compat | staging_verified | `CR_AUTH_COMPAT_UNTIL` в гейте (legacy только до даты), SetWorkflowState защищает новые infra-id; наследуемые профили — claim по коду в «Избранное» (`claim.start/verify`, код действует 10 мин, повторный start не шлёт второй) или по re-auth (`claim.status` привязывает только при `authorized_at > claim_pending_at`); переустановка = перенос владения по тому же доказательству | `e2e-auth.py claim_by_code_transfers_ownership` (код ушёл на свой номер, повтор reuse, чужой код 403, владелец B потерял профиль после verify A), `claim_by_reauth_binds_after_pairing` (qr разрешён по c-capability, chats 403, привязка только после спаривания); compat open → legacy 200, closed → 401 | самосообщение WA/TG не подтверждено документацией — device-проверка (canary, runbook §2.8); rate-limit вебхуков отложен |
| 1.7 ротация токена | planned (owner-шаг) | runbook §2.10 | — | после закрытия compat-окна: новый токен в кабинете Wappi → credential `WappiAuthToken` на n8n; старые APK/IPA теряют прямой Wappi |
| 1.8 клиент | locally_verified | `Assets/Scripts/Auth/{InstallSession,AuthClient,AuthorizedRequest,ProfileClaimCopy,ProfileClaimPolicy,ProfileClaimSheet,ProfileClaimFlow}.cs`, `Chat/WappiProxy.cs`; 33 Wappi-сайта + 12 n8n-сайтов переведены; `Secrets.wappiAuthToken` удалён; Create* → ForceRefresh; Edit* читает `{success:false}`; claim-карточка на boot + хук в WA/TG status-поллерах | headless EditMode **2625/2625**; `AuthWiringGuardTests` (source guard: 0 `wappi.pro`/`Authorization` вне helper'а, каждый прямой `SendWebRequest` в allow-list, каждый `/webhook/`-файл через AuthorizedRequest/WappiProxy) | device-пасс не проводился (Editor закрыт, устройств нет): клавиатура карточки кода, первый запуск с наследуемыми ботами, задержка bootstrap (2 Postgres-узла ≈ 4 с на проде) — в canary; `Tools/store/inspect-artifact.sh` ждёт реальный билд |
- 2026-09-17 · A1 итог: verify_rag/panel/batching/parity/auth-gate — все зелёные; `apply-*.py --check` OK; token.test.mjs OK;
  repro 13/4 (по замыслу); staging `e2e-auth.py` **10/10**; Unity headless **2625/2625**. Прод не менялся.

## Этап 2 (A2) — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 2.1 желаемое vs подтверждённое + pending/error | locally_verified | `ReplyModeSyncQueue` (desired/confirmed с версиями), `ReplyModeSyncService` (persist, батчи, события); UI: pill в шапке чатов и per-chat чип приглушены (alpha 0,55) пока нет подтверждения, карточка бота — «Сохраняем режим…» (мигает) / «Режим не сохранён»; дизайн панели не менялся | Unity 2640/2640, `ReplyModeSyncQueueTests` (10), `ReplyModeSyncWiringTests` (4) | device-пасс не проводился |
| 2.2 подтверждение по телу/области/версии | staging_verified | Set Reply Mode v2: `Validate` (одна item, version; legacy → серверный now), ONE-statement upsert `where version < excluded.version` с отчётом per profile, `Shape Response` (`allApplied`, db_error → 500), bad_request → **400**; клиент подтверждает только по `applied` своей версии | e2e-auth: `replymode_invalid_is_400`, `replymode_reordered_versions_keep_newest`, `replymode_bot_default_covers_both_channels_atomically`, `replymode_legacy_body_still_writes`; e2e.py `setreplymode_invalid_is_4xx` теперь PASS | — |
| 2.3 очередь последнего состояния | locally_verified | backoff 2/5/15/60 с → 5 мин, ≤8 попыток за запуск, парковка и повтор при следующем запуске; устаревший ответ игнорируется; серверное более новое значение принимается (`OnServerNewer` → `ReconcileFromServer` / `SemiAutoStore.Set` без обратной записи); 403/400 — стоп без повторов (владение решает claim) | queue-тесты (restart-reload, reorder, backoff cap) | «сеть пропала» проверено юнит-тестами очереди и структурно, не на устройстве |
| 2.4 приоритеты override/default/quota | locally_verified | не тронуты: `RestoreForActiveChat` по-прежнему пушит только явный override (TryGetOverride), наследуемые чаты ничего не пишут; QuotaFallbackPolicy без изменений | существующие тесты SemiAutoStore/AutoButtonModel зелёные | — |
| 2.5 окно начальной активации | staging_verified | Create WA/TG: `Seed Reply Mode` (пишет заявленный `ReplyMode`, version-guarded) строго между `Get Created Workflow Id` и `Activate Created Workflow`; клиент шлёт `ReplyMode`/`ReplyModeVersion` во всех 4 формах (start+edit, WA+TG); legacy-клиент без поля — прежняя семантика | e2e-auth `create_seeds_reply_mode_before_activation` (строка '*' есть после create, workflow_id привязан, токен знает клон, delete OK); `verify-reply-mode.py` | посев fail-open при сбое БД (клиентская очередь дописывает); re-auth через настройки использует ту же форму |

**Приёмка этапа 2:** обрыв сети/перезапуск — очередь (юнит); 200 с ошибкой — `success:false` не подтверждает + 400 на
bad_request (staging); два быстрых переключения с обратным порядком — версии (staging reorder + юнит ignore-stale);
частичный сбой одного канала — единая транзакция/один запрос на оба профиля (staging both-channels) + 403 всего запроса при
чужом профиле (A1); первое входящее во время создания — посев до активации (staging create). Интерфейс не утверждает
серверное состояние без подтверждения (dim/subline). Не проверено на устройстве.

## Этап 3 (A3/A4/A7) — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 3.1 атомарная регистрация события | staging_verified | `sql/2026-09-17-reply-events.sql`: `reply_events (profile, chat, message_id, state, execution_id, transport_message_id, error)` + `chat_reply_state (incoming_generation, lock_token, lock_expires_at, last_sent_*)`, RLS default-deny; узел `Claim Event` (один upsert: повторная доставка → `claimed=false` → `duplicate` ДО debounce/LLM/typing; устаревший claim >10 мин в claimed/generated переиспользуется; каждый claim поднимает поколение чата и фиксирует watermark памяти) | e2e-reply `dup_parallel_once`, `dup_sequential_once` (второй прогон не выполняет ни Debounce, ни AI Agent, ни Mark Read/Typing/send), `restart_stale_claim_reclaims` (11-мин claim → ответ отправлен; 1-мин claim → duplicate) — WA и TG | состояние `generated` зарезервировано (не пишется — лишний DB-узел ≈1,9 с на проде); событие, умершее между lease и Record, остаётся `send_pending` (Wappi не доставляет повторно; lease истекает за 90 с) |
| 3.2 порядок в рамках чата | staging_verified | lease 90 с на (profile, chat) в `Acquire Send Lease`; другой чат не блокируется; просроченный lease не блокирует; `cancelled_busy` при живом чужом lease | `two_chats_parallel_independent` (2 чата → 2 отправки), `expired_lock_does_not_block` (lock снят после отправки), `live_lock_blocks_send` (0 отправок, ledger cancelled_busy) | — |
| 3.3 проверка режима/поколения/владельца перед отправкой | staging_verified | `Final Fetch` → `Final Check` (новее клиент → superseded; новее владелец → cancelled_owner; снимок без триггера → cancelled_fetch; стабильная сортировка time+порядок Wappi); `Latest+Combine` — то же правило владельца внутри debounce; `Acquire Send Lease` ОДНИМ запросом перечитывает текущий режим (`cancelled_mode`) и поколение (`superseded`) | repro `combine_human_reply_after_trigger_aborts` (WA+TG) теперь PASS; e2e.py `bot_human_reply_during_debounce_aborts`, `bot_newer_incoming_supersedes`; e2e-reply `newer_incoming_during_llm_supersedes`, `owner_reply_before_send_cancels`, `auto_off_mid_flight_cancels` — 0 отправок, ledger-состояние, AI-строк памяти 0 | окно между `Acquire Send Lease` и самим send (~2 с на проде) — единственное, где новое входящее уже не отменяет; его ответ придёт следующим |
| 3.4 короткие транзакции | staging_verified | ни один DB-запрос не охватывает LLM или транспорт: Claim / Lease / Record — по одному statement; lease покрывает только send | структура: `verify-reply-pipeline.py` (SQL-контракты, отсутствие send в Code-узлах) | — |
| 3.5 состояния sent / send_failed / send_unknown | staging_verified | send: `fullResponse` + `neverError` + 30 с + `onError: continue`; `Settle Send` классифицирует; `Record Send` пишет состояние и `transport_message_id`, снимает lease; `Outcome` читает записанное состояние | `send_500_is_send_failed` (ровно 1 попытка, error `http 500 …`), `accept_then_drop_reconciles_sent`, `send_timeout_reconciles_from_echo`, `dropped_without_echo_stays_send_unknown` (состояние остаётся send_unknown в ledger и в Outcome) | видимость send_unknown для владельца в приложении — этап 4/7 (статус последнего исхода); сейчас — ledger + `baseline-metrics.py` |
| 3.6 идемпотентность транспорта / сверка | staging_verified | у Wappi нет ключа идемпотентности (документация 2026-09-17) ⇒ сверка: `Wait Reconcile` 3 с → `Reconcile Fetch` → `Reconcile Check` (наш текст как исходящее эхо после начала send ⇒ sent с id эха; иначе send_unknown). Повторной отправки нет нигде в цепочке | `accept_then_drop_reconciles_sent` (1 вызов send, reconciled=true), `send_timeout_reconciles_from_echo` (Settle=send_unknown → Outcome sent), `dropped_without_echo…` (reconciled=false) | сверка по тексту+времени, не по id: одинаковый текст, отправленный владельцем в те же 5 с, дал бы ложное «sent» (маловероятно, отмечено) |
| 3.7 Mark Read / Typing не блокируют | staging_verified | `Mark Read`, `Typing`: `onError: continueRegularOutput`, `alwaysOutputData`, timeout 15 с; `AI Agent` fail-open → `Generated?` → `generation_failed` | `typing_failure_still_sends` (typing 500 → ответ отправлен) — WA и TG | сбой самого LLM не воспроизводился на staging (OpenAI не мокается) — ветка проверена структурно |
| 3.8 бюджет задержки | locally_verified | клампы: чтение ≤ 8 с (12 слов), печать ≤ 12 с (20 слов), прослушивание ≤ 12 с; итог на проде ≈ 8 (debounce) + 5×1,9 (DB) + LLM + ≤ 21 (паузы) ≈ 30–45 с | `verify-message-batching.py` (клампы), выборочно по длительностям staging-прогона (12–35 с при DB ≈ 0) | 1,9 с/узел Postgres на проде (Supabase pooler) — сам по себе риск; вынесено в runbook §2 |
| 3.9 отменённая генерация не в истории | staging_verified | `Record Cancel` удаляет AI-строки `n8n_chat_histories` выше watermark для отменённой генерации (`dropAiHistory` только с `Final OK?`/`Send Allowed?`); `Cancel Label` именует каждую причину | сценарии 3.3: `ai_memory_rows == 0` после superseded / cancelled_owner / cancelled_mode | человеческие строки (входящее клиента) остаются — правильно: следующий ответ должен их видеть |

**Приёмка этапа 3** (по инструкции): последовательные и параллельные дубли ✓; перезапуск после захвата события ✓ (stale-claim);
два клиента одновременно ✓; новый вход во время LLM и перед отправкой ✓; ручной ответ ✓ (в debounce и перед отправкой);
выключение авто ✓; сбой typing ✓; истёкшая блокировка ✓; Wappi принял сообщение, ответ потерян ✓ (accept_then_drop, timeout);
нет повторных отправок ✓ (в каждом сценарии считаются вызовы `message/send` мока); неопределённый результат не скрывается ✓
(`dropped_without_echo_stays_send_unknown`). WA и TG — одинаковые сценарии, e2e-reply 28/28. Клиент не менялся (Unity 2640/2640
от этапа 2 остаётся в силе). Прод не менялся.

## Этап 4 (A5/A6) — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 4.1 согласованная история с ролями/источниками | staging_verified | `chat_history_events` (source ∈ client/ai/owner_app/owner_phone/outgoing_api, ключ = id транспорта); писатели: `Claim Event` (клиент, до решения о генерации), `Record Suppressed` («Вместе»: клиент → история + human-строка памяти), `Record Send` (ai), `Record Outgoing` (телефон владельца → история + ai-строка памяти с меткой source), Wappi Proxy `Record Owner Send` после ответа (owner_app → история + память). Запись не зависит от разрешения генерации; outgoing не считается AI | e2e-handoff `outgoing_phone_recorded` (WA+TG: recorded_owner_phone, повтор → outgoing_known, 1 строка памяти), `outgoing_api_history_only`, `suppressed_incoming_recorded` (WA+TG), `auto_reply_records_client_and_ai_rows` (client,ai / human,ai без дублей), `proxy_send_records_owner_app_row` | форма outgoing-ивентов Wappi в документации не показана — узел построен по форме входящего (canary в runbook §2.6); существующие профили получат outgoing-типы только после прогона `webhook/types/set` (этап 7.5) |
| 4.2 фактические callbacks WA/TG | staging_verified (моком) | Create WA/TG подписывают `incoming_message + outgoing_message_phone + outgoing_message_api`; шаблоны маршрутизируют по `wh_type` (`Is Incoming?`/`Is Outgoing?`), чужой профиль отфильтрован до записи (`Outgoing Bound?`), группы не пишутся; двойной записи нет: память клиента пишет либо memory-узел (агент запущен), либо `Record Suppressed`/`Record Cancel` (агент не запускался), дедуп по `message_id` в `additional_kwargs` | `outgoing_foreign_profile_filtered`, `verify-history-handoff.py` (writers/sources/дедуп), `verify-message-batching.py` (маршрутизация) | границы покрытия: сообщения владельца с телефона у ботов без outgoing-подписки не видны (честно: строк owner_phone нет); сверка истории по `messages/get` не добавлялась |
| 4.3 текстовая память vs структурированное состояние заявки | staging_verified | заявка = строка `handoff_requests` (reason, data{name, phone}, summary, status, notify_*) — отдельно от памяти; агент читает известные данные из Chat Memory (правило промпта: не спрашивать телефон повторно — было) | `handoff_tool…` (data.phone из инструмента), структура SQL | «что осталось уточнить» не моделируется отдельно (summary + data) — достаточно для заявки, не для многошагового сбора |
| 4.4 надёжный handoff | staging_verified | инструмент агента `Create Handoff` → sub-workflow `crHandoffReq0001`: upsert по уникальному ключу (profile, chat, message id триггера), пауза `handoff_paused` в чате; `Read Reply Mode` OR'ит паузу; снять паузу может только владелец: `SetReplyMode` для ЭТОГО чата suppressed=false (закрывает заявки чата) или `Handoffs done`; промпт: «Передал владельцу» только при ok:true, «увидел/в курсе» — никогда | `handoff_tool_creates_request_once_and_pauses [llm]` (заявка 1 шт., повтор события → без второй, новое входящее → suppressed, явный «Авто» → unpaused + done → ответ снова уходит), `handoffs_webhook_owner_scoped_list_take_done` | инструмент вызывает модель — детерминированность не гарантирована (сценарий отмечен [llm]); заявка, созданная во время отменённой генерации, остаётся (owner видит и закрывает) |
| 4.5 сводка читает достоверный источник | staging_verified | `Fetch Outcomes`: FULL OUTER JOIN открытых заявок; `source=event` перекрывает классификатор (order → order_collected, иначе owner_needed), `handoffId/Status/Reason`; транскрипт классификатора помечает строки владельца «Владелец:» | `dashboard_shows_open_handoff_as_event`; клиент: `DashboardOutcome.IsEvent`, метка «Заявка ·» (`HandoffCopy.RowSummary`) | — |
| 4.6 уведомление владельца + список ожидающих | staging_verified (мок) / **push — внешний блокер** | Notifier `crHandoffNtfy001` (каждые 2 мин): self-message на собственный канал бота, без текста клиента и его номера; попытки/бэкофф на строке заявки; приложение: `HandoffSyncService` (list на старте/раз в 3 мин/при открытии «Сводки», зеркало пауз в per-chat «Вместе» — только пауза, никогда возобновление; звук/вибрация при новой заявке), `OpenChat` из заявки → `take` (реальный просмотр) | `notifier_delivers_self_message_and_survives_failure` (500 → failed/1, заявка цела; вторая → sent/1; получатель = свой номер; тело без утечки), Unity `HandoffModelsTests` (7), `HandoffWiringTests` (4) | APNs/FCM: нет ключей, пакета и device-теста — runbook §2.11 с точными шагами; self-message на устройстве не проверен; deep-link из уведомления в чат не реализован (нет URL-схемы) |
| 4.7 удаление/очистка | staging_verified | Delete Bot Files: + `chat_history_events`, `handoff_requests`, `reply_events`, `chat_reply_state` (по profile id); `dialog_counts` не трогаются | `delete_bot_wipes_history_and_requests` (0/0/0/0, счётчики в ответе) | — |

**Приёмка этапа 4:** договорённость вручную → после явного «Авто» бот знает её ✓ (реальная модель: «Завтра в 15:00, спросить Марину»);
заявка один раз при повторе события ✓; ошибка уведомления не теряет заявку ✓; приложение закрыто → уведомление — self-message
на канале бота (проверено моком, на устройстве НЕ проверено; push — внешний блокер) → **этап частично готов** по формулировке
инструкции; denied notification permission — push отсутствует, честный fallback = список заявок + self-message; удаление/изоляция ✓.
e2e-handoff 14/14 (прогон 2: 13/14, notifier исправлен на per-item и повторён 1/1); Unity 2651/2651. Прод не менялся.
