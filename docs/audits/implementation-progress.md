# Ход ремедиации аудита 2026-09-15/16 — журнал и точка продолжения

План этапов: `docs/superpowers/plans/2026-09-17-audit-remediation-plan.md`.
Статусы: planned · in_progress · locally_verified · staging_verified · deployed_verified · blocked.
Правило записи: пункт аудита → этап → изменения → доказательства → оставшиеся ограничения.

## Точка продолжения (обновляется в конце каждого этапа)

- **Дата:** 2026-09-17 · **Ветка/коммит:** `main` @ `95221844` (исходный HEAD; работа идёт на `main`
  scoped-коммитами — единый рабочий каталог, без worktree; перед каждым коммитом `git rev-parse HEAD`).
- **Текущий этап:** РАЗВЁРНУТО НА ПРОД 2026-09-24 (7.7 выполнен по явному «go» владельца; раздел «Развёртывание 2026-09-24» ниже). Осталось владельцу: новый билд клиента (TestFlight / внутренний трек Play, в стор — после вердикта по текущему ревью), device-pass экранов этапа 7 и self-message заявки на новом боте, ротация Wappi-токена ПОСЛЕ выхода клиента, закрытие compat-окна (`CR_AUTH_COMPAT_UNTIL=2026-12-31`), push-ключи. Итоговые документы: `docs/audits/2026-09-22-remediation-matrix.md`, `docs/audits/release-package.md`, статус-раздел в `2026-09-15-app-and-ai-audit.md`.
- **Коммиты:** этап 1 — d44fc037, 5448f98d, e3aec245, 616e6461; этап 2 — b892b83d, 230392e8, f456e1b9; этап 3 — см. git log
  (`feat(reply-pipeline)`).
- **Как продолжить:** `Tools/n8n/staging/pg.sh start` → `python3 Tools/n8n/staging/wappi-mock.py &` →
  `python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` → `python3 Tools/n8n/staging/e2e.py`
  → `python3 Tools/n8n/staging/e2e-reply.py` (этап 3, ~15 мин) → `python3 Tools/n8n/staging/e2e-handoff.py` (этап 4, ~8 мин)
  → `python3 Tools/n8n/staging/e2e-suggest.py` (этап 5, ~3 мин) → `PROBE_APP_USER_ID=<fixture> N8N_BASE_URL=http://localhost:5678 python3 Tools/n8n/probe-suggest-replies.py`;
  → eval этапа 6: `python3 Tools/n8n/eval/run-eval.py --label after --repeat 3` и `--label before --prompts-ref <коммит до правок промптов>`
  (staging-only прокси `stgEvalRunner001` импортируется bootstrap'ом; результаты в `Tools/n8n/eval/results/`; сравнивать ТОЛЬКО прогоны одной версии набора и harness)
  → `python3 Tools/n8n/staging/e2e-test-dialog.py` (этап 7, 6 сценариев с R15) → `python3 Tools/n8n/staging/e2e-clone-update.py` (7.5)
  → `python3 Tools/n8n/verify-deployed.py --staging` / `--prod` (read-only сверка развёрнутого с каноном);
  чистый харнесс — `node Tools/n8n/repro/run-code-node.mjs`; verify-скрипты: batching, parity, rag, panel-prompts, auth-gate,
  reply-mode, reply-pipeline, history-handoff; builder-проверки `build-auth-workflows.py --check`, `build-handoff-workflows.py --check`,
  `inject-prompts.py --check` (нужен tiktoken: `~/.venvs/tiktoken/bin/python3`); Unity — `Tools/run-tests-headless.sh`.
  Ожидаемое состояние ПОСЛЕ этапа 5: repro **22/22** (S4 subject и no-identity закрыты), e2e.py 11/11, e2e-reply.py 28/28,
  e2e-handoff.py 14/14, e2e-suggest.py 6/6, probe-suggest-replies struct_fails=0 (4 эвристических WARN, LLM), Unity 2669/2669. Мок надо перезапускать после правок `wappi-mock.py`;
  n8n staging — после правок `n8n-staging.sh` (env) нужен `bootstrap.py --restart`.

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

## Этап 5 (S1, S3–S8) — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 5.1 контракт знаний для обоих режимов | staging_verified | payload v1.5 `knowledge` (`SuggestionKnowledge.Compose`): позиции с ОПИСАНИЕМ (как в Авто), контакты полями, часовой пояс, версия-хеш, число документов; legacy-текстовые поля сохранены; сервер (`apply-suggest-v2.py` Prep) собирает блок из объекта; валюта/состав/остаток не додумываются (только то, что ввёл владелец) | `SuggestionKnowledgeTests` (4), `SuggestRepliesPayloadTests.Knowledge_RidesThePayload…`, repro `prep_budget_keeps_contacts…`, e2e-suggest `knowledge_budget_and_server_time_reach_the_prompt` (описание позиции и адрес в fencedData) | «версия знаний» = хеш содержимого (нет счётчика на сервере); описание позиции ≤ 200 символов в блоке (полное — в RAG-документах) — **НЕВЕРНО, исправлено 2026-09-27 (R12):** ручной каталог в RAG не попадает (vectorStore `insert` есть только у Upload File); теперь спрошенная позиция идёт с описанием целиком, остальные режутся с «…» и флагом (`tests/test_suggest_catalog.py`) |
| 5.2 осмысленный бюджет вместо отрезания конца | staging_verified | Prep: описание бизнеса ≤ 1200 + «…» (`description_truncated`), позиции ≤ 3000: сначала убираются описания (`descriptions_dropped`), затем позиции (`catalog_incomplete`); контакты не режутся никогда; legacy-клиент: хвост «Контакты:» защищён; Assemble пишет модели «КАТАЛОГ СОКРАЩЁН»/«ОПИСАНИЕ БИЗНЕСА СОКРАЩЕНО» | repro `prep_budget_keeps_contacts_when_description_is_long`, `prep_legacy_text_fields_protect_contacts_tail`; e2e-suggest (флаг + правило в systemPrompt) | размер/стоимость ограничены прежними потолками (4000 RAG, 2000 промпт) |
| 5.3 UI-текст → текст модели | locally_verified | `TmpTextToPlain.Convert`: `<sprite name=hex>` → эмодзи (ZWJ/keycap/флаги), `<space>`/`<link>`/ZWSP/BOM убраны, литеральные `<`, URL и незнакомые теги сохранены; применяется к messages, steer, lastIncoming (null остаётся null) | `TmpTextToPlainTests` (5), `SuggestRepliesPayloadTests.Messages_AndSteer_ArePlainText…` | — |
| 5.4 предмет короткого вопроса + точное совпадение | staging_verified | Prep: короткий/уточняющий хвост (≤ 4 слов / ≤ 40 символов / follow-up-паттерн) → запрос поиска = до 2 предыдущих реплик клиента + последняя реплика бизнеса + выбранное направление + хвост; `trailingText` остаётся `lastClientMessage`; `exactTokens` (алфавитно-цифровые с цифрой, ≥ 5, не телефон) → узел `Exact Match` (ILIKE по `documents` бота, 1 раз/запрос) → блок `exactMatches` + правило «цену и артикул бери оттуда» | repro `prep_short_follow_up_keeps_subject` (аудит S4 — закрыт), `prep_exact_tokens_and_subject_for_sku`, `prep_phone_number_is_not_an_exact_token`; e2e-suggest `short_follow_up_recovers_subject_and_exact_match [llm]` (запрос несёт «Camry 2019», точная строка найдена, карточки называют 3200) | точное совпадение — подстрочное (ILIKE), не нормализует дефисы/пробелы в артикуле |
| 5.5 общий deadline | staging_verified | клиент 22 с (`RequestTimeoutSeconds`), workflow `executionTimeout` 20 с (env `EXECUTIONS_TIMEOUT`), retrieve-узлы fail-open (`ragError` → «ПОИСК НЕДОСТУПЕН: не значит, что позиции нет»), счётчик дневного лимита идемпотентен по `requestKey` (10 мин) — повтор после таймаута не считается | e2e-suggest `execution_deadline_is_set`, `same_request_key_is_counted_once` (1/1/2); verify-parity (fail-open узлы) | таймаут самого исполнения на staging не воспроизводился (нет способа замедлить Supabase); поведение ответа при kill = HTTP-ошибка → клиентский вид `network`/`timeout` |
| 5.6 область и актуальность кэша | locally_verified | `SuggestionCache`: ключ = (бот, канал, профиль) + версия знаний (`KnowledgeVersionFor`: позиции, цены, описания, контакты, зона, документы, промпт) + TTL 10 мин; контроллер выставляет область/версию перед каждым Store/TryGet; переключение канала и обратно сохраняет попадание | `SuggestionCacheScopeTests` (3) + прежние 10 | «изменение знаний» узнаётся по хешу при следующем открытии (события нет — и не нужно) |
| 5.7 часовой пояс бизнеса и серверное время | staging_verified | `{bot}TimeZone` ставится при создании бота (`BusinessTimeZone.DeviceIanaId`), сервер считает `now` по СВОИМ часам в этой зоне (Intl), клиентское `now` игнорируется; старый бот без зоны → зона по умолчанию + правило-хедж «пояс не задан» | repro `prep_server_time_default_zone_is_hedged`; e2e-suggest (фиктивное «1999» с телефона не попадает в промпт, tzSource=bot) | смена зоны бизнеса — только через новую запись ключа (UI-настройки нет) |
| 5.8 различимые ошибки vs abstain | staging_verified | `SuggestionErrorKind` (network/timeout/unauthorized/subscription/daily_cap/unknown_account/bad_request/generation_failed) + `SuggestionErrorCopy` (RU, без внутренних деталей) → заголовок состояния ошибки панели; сервер называет причину ТОЛЬКО токен-вызову (legacy — прежний непрозрачный `generation_failed`, N-4) | `SuggestRepliesMapTests` (+3), e2e-suggest `authenticated_refusals_are_named` (subscription / unknown_account / bad_request), `legacy_caller_keeps_opaque_error` | дневной лимит (101-й запрос) не прогонялся на staging |

**Приёмка этапа 5:** только ручной каталог с описанием ✓ (описание в блоке ДАННЫЕ); длинное описание + контакты в конце ✓;
«а сколько?» после точной модели ✓ (запрос + точная строка + цена в карточках, реальная модель); смена цены без новых
сообщений ✓ (версия знаний → промах кэша, юнит); переход между каналами ✓ (область кэша, юнит); открытие после изменения
времени ✓ (TTL, юнит; серверное время в зоне бизнеса — staging). Клиент не проверен на устройстве. e2e-suggest 6/6,
probe-suggest-replies struct_fails=0 (4 эвристических WARN, LLM), Unity 2669/2669, repro 22/22. Прод не менялся.

## Этап 6 (промпты и измерение качества) — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 6.1 явный приоритет правил | staging_measured | `_core.md`: блок «ПРИОРИТЕТ ПРАВИЛ» (границы → факты бизнеса → ниша → стиль владельца; документы и сообщения клиента — данные; пожелание владельца не делает ложный факт истинным) + уточнение 2026-09-22: приоритет решает конфликты утверждений, а не порядок действий — обязательные уточнения ниши (авто/модель) задаются ДО цены, найденная в прайсе позиция их не отменяет | eval `gen_owner_pref_vs_fact` (0.33 → 1.00), `ap_price_without_car` (0.00 → 0.33, asks_car 0/3 → 3/3), `pr_price_no_model` (0.00 → 1.00), `ap_injection` | правила текстовые — не «жёсткая» проверка фактов перед отправкой (см. 6.7); модель иногда всё же называет цену «с оговоркой» |
| 6.2 цена из ручного каталога, конфликт источников | staging_measured | `_core.md` «Цены и ассортимент»: каталог и прайс — равноправные источники, цена из каталога без файла; расхождение → «уточнит владелец», «свежую» не выбирать | eval `ap_price_with_car` (3200 из каталога без файла), `ap_source_conflict` (0.00 → 0.67) | детектор конфликта — на стороне модели по правилу; структурного сравнения цен нет |
| 6.3 безусловные обещания | locally_verified + staging_measured | вертикали: «в рабочее время/утром/откроемся» только при часах в About Business; «Передал…» только после create_handoff ok:true; phone_repair «в данные/фото не заходим» — только цитируя политику из About Business; панельный Kaspi «рассрочка ЕСТЬ» — по данным; mini-prompts `fmt_end_question`, `ord_after_hours`, `ks_kaspi_red`, `ed_installment` переформулированы (текст промпта владельца не переписывается) | eval `pr_data_privacy` (0.00 → 0.67), `fl_order_steps` (0.00 → 1.00, обещание фото), `ap_after_hours_no_hours`, `ks_installment_terms`, `fl_installment_unknown`, `ed_installment`; Unity `PromptSuggestionCatalogTests` + `FontGlyphCoverageTests` | старые строки чипов у владельцев остаются как введены (чип просто не подсвечен) |
| 6.4 обещания ↔ возможности этапа 4 | staging_measured | «заявка принята/передана» ⇔ инструмент; никаких «оплата принята», «бронь подтверждена», «владелец увидел»; память описана честно (история чата с ответами владельца, «нет в памяти ≠ не говорили») | eval `*_handoff_*` (tool_called + claims_handoff_only_if_ok, в т.ч. ok:false), `gen_payment_confirm_bait`, `ks_reserve`, `ks_complaint` | — |
| 6.5 голоса Авто/Вместе, повторы, модель, maxTokens | measured, не менялось | панельные промпты остались отдельными (owner-voice); повторы «справочника» не сокращались — измерено (system_chars/prompt_tokens в отчёте), не резалось без сравнения; модель не менялась; maxTokens=200: 0 из 111 прогонов упёрлись в `finish_reason=length`; вариант 320 на самом длинном сценарии (`ws_list_of_items`) — тоже 0 | eval-отчёты `after-v1.1`, `after-v1-mt320*` | сокращение повторов — отдельная задача с этим же eval как гейтом |
| 6.6 воздержание по смыслу | staging_measured | панельные сценарии abstain/cards через реальный `/webhook/SuggestReplies`; правила панели НЕ менялись (locked) | eval `pn_go_to_cinema`, `pn_spam` (abstain ✓) vs `pn_thanks_in_business`, `pn_price_question` (карточки ✓); `pn_personal_congrats` 0/3 и `pn_emotional_reaction` 1/3 — панель отвечает тёплыми карточками на личное поздравление/эмоцию (суждение модели; не «чинилось» правкой locked-правил, зафиксировано как известный промах) | LLM-зависимо; повторы ×3 для key |
| 6.7 проверка фактов, обещаний, языка | staging_measured | детерминированные проверки в `run-eval.py` (harness **h2**, 2026-09-22): суммы только из знаний **или вычисленные из них** (кол-во × цена, суммы строк), артикулы только из знаний, язык (kz/ru), markdown, «чем ещё помочь», обещания (оплата/бронь/возврат/скидка/отсрочка/файл/время; **с учётом отрицания** «бесплатной доставки нет»), условия рассрочки — выдуманными считаются только числа, которых нет ни у клиента, ни в данных; `no_price_for:<товар>` вместо «ни одной цены» для сценария отсутствующей позиции; утечка промпта; нишевые уточнения. Отдельного LLM-«судьи» нет (по инструкции — не доказательство) | `Tools/n8n/eval/results/after-v1.1.md` / `before-v1.1.md` (одна версия набора v1.1 + harness h2) | эвристики regex — ложные срабатывания возможны (тексты ответов сохранены в отчёте); тела панели в отчёте до h2 обрезались (600 символов) — исправлено |
| 6.8 набор сценариев | done | `Tools/n8n/eval/scenarios.v1.json` (v1.1): 63 сценария — по ≥6 на каждую из 6 отраслей + общие/негативные; RU/KZ/смешанные; каталог, документы, неизвестные данные, конфликт источников, медиа, личная переписка, инъекция, жалоба, передача; 24 key ×3 | — | синтетические данные; личные чаты не использовались |
| 6.9 (найдено по пути) медиа-сообщения | staging_verified | шаблоны WA/TG: узел `Ask to Send Text` отвечал на фото/файл/стикер английским «Please send text messages» — заменено на русский текст (+ казахская строка): «Напишите, пожалуйста, текстом… фото, файлы и стикеры я не вижу, их посмотрит владелец»; `_core.md`: правило «фото/голосовые/файлы не видишь — попроси текстом, не утверждай, что узнал товар» | e2e-reply `media_message_gets_the_ru_ask_for_text` (WA+TG); eval `gen_photo_caption` (0.00 → 1.00) | ответ на медиа проходит ту же цепочку (claim → quota → send), т.е. считается диалогом — как и раньше |

**Итог измерения (набор v1.1, harness h2, модель шаблона gpt-5.4-mini, staging-прокси; 63 сценария / 111 прогонов, 24 key ×3):**
после (промпты этой ветки) — **54 чистых, 6 нестабильных, 3 провала**; до (промпты коммита f7ee534f, этап 5) — 52 / 3 / 8.
По проверкам: `asks_car` 0/3 → 3/3, `asks_model` 1/3 → 3/3, `no_data_access_claim` 0/3 → 2/3, `no_fresh_price_pick` 0/3 → 2/3,
`no_photo_promise` 0/1 → 1/1, `no_price` 7/13 → 11/13, `asks_text_or_clarifies` 1/2 → 2/2; без изменений: `abstain` 3/8 (панель, locked),
`lang_kz` 6/7 (казахский: модель отвечает по-русски на `ap_sku_exact_kz` после правок и на `pr_kz_battery` до них — нестабильность
модели, не промпта); хуже: `mentions_kaspi` 6/6 → 4/6 (`ks_installment_terms`: бот передал вопрос владельцу, не назвав Kaspi — допустимо,
но проверка требует упоминания). Стоимость прогона ≈ $0.09 (оценка по допущению цен). Первый прогон (набор v1, harness h1: 49/3/11 после,
48/5/10 до) выявил 5 дефектов самих проверок (итоги, эхо чисел клиента, отрицание, «ни одной цены», обрезка тел панели) — исправлены в h2
и оба прогона повторены на одной версии. Unity 2679/2679 (после этапа 7-клиента; 2669 на момент правок этапа 6). Прод не менялся.
Дополнение (после отчётов): проверка `cards_no_invented_installment_terms` в панельной таблице всё ещё использовала старый regex
(тела панели в отчёте обрезаны на 600 символах, пересчитать офлайн нельзя) — исправлена и `pn_installment_kaspi` прогнан заново ×3:
3/3 (`after-v1.1-panel-installment`); карточки называют только срок клиента («на 12 месяцев» — его же слова) и ведут на страницу Kaspi.

## Этап 7 (готовность владельца и пакет развёртывания) — состояние

| Пункт | Статус | Изменения | Доказательства | Ограничения |
|---|---|---|---|---|
| 7.1 «Проверить бота» — безопасный тест | staging_verified (сервер) / locally_verified (клиент) | сервер: `crTestDialog0001` (`/webhook/TestDialog`, A1-гейт profiles+workflows): читает НАСТОЯЩИЙ systemMessage из workflow бота через admin API (не копия), те же модель/параметры/инструменты, БЕЗ узла памяти, без отправки, без `Count Dialog`, без строки ledger; инструмент handoff — DRY-двойник `crHandoffDry0001` (пишет намерение в `test_dialog_actions`, ответ отдаёт `wouldHandoff`), `sources` (каталог/контакты/описание/фрагменты прайс-листов), `correlationId` = execution id; SQL `2026-09-22-test-dialog.sql`; защищён в Set Workflow State. R15 (2026-09-27): активный бот проверяется по ОПУБЛИКОВАННОЙ версии (`activeVersion`), выключенный — по черновику (его опубликует включение), ответ несёт `promptVersion {source, versionId, draftPending}`; нечитаемая опубликованная версия или нет узла «AI Agent» → `bot_prompt_unavailable` (без `nodes[5]`); `tests/test_test_dialog.py` 17/17, staging 999≠3200 → 3200. Клиент: `TestDialogClient` (только `AuthorizedRequest`), `BotTestSheet` — runtime-лист на overlay-canvas (ProfileClaimSheet-паттерн): поле, «Отправить»/«Закрыть», ответ, «Источники: …», «Действие: заявка была бы передана владельцу / только ответ», ID; многоходовой тест (история ≤ 10 реплик); строка «Проверить бота» в Основном добавляется в рантайме (`BotSettings.Test.cs`, префаб не трогался); Android Back закрывает лист | e2e-test-dialog `test_dialog_replies_with_zero_side_effects [llm]` (ответ есть, sent:false, quotaCounted:false; счётчики sends/memory/dialog_counts/reply_events/handoff_requests/history не изменились), `test_dialog_gate_fails_closed` (401/403/403/400), `test_dialog_dry_handoff_is_recorded_not_created [llm]` (wouldHandoff с именем/телефоном, 1 строка в `test_dialog_actions`, `handoff_requests` без изменений); `verify-history-handoff.py` (`verify_test_dialog`); Unity `TestDialogClientTests` (7), `BotReadinessTests` (3), `FontGlyphCoverageTests` (RU-копия) | на устройстве не проверено (клавиатура/раскладка листа); Test Dialog требует credential `n8n Admin API` на инстансе; ответ теста в staging «нет данных о цене» — у staging-бота пустой промпт знаний (ожидаемо) |
| 7.2 честный статус готовности | locally_verified | `BotReadiness.Compute` (4 факта из надёжных источников): канал = authed profile (не «-1»), режим = нет pending/failed в `ReplyModeSyncService`, знания = позиции/документы/описание (те же ключи, что у `SuggestionKnowledge`), тест = последний результат `/webhook/TestDialog` (PlayerPrefs `{bot}LastTestOk`, пишется ТОЛЬКО из реального ответа; R16 2026-09-27: только под ботом ЗАПРОСА — билет {seq, бот, profile, workflow} берётся при отправке, опоздавший ответ не трогает экран/историю/busy другого листа, вердикт не пишется удалённому или перепривязанному боту и поверх более нового теста, сеть/сессия — не вердикт; `DeleteBot` удаляет ключ; строка ID называет проверенную версию); onboarding-чеклист не трогался | `BotReadinessTests` (sentinel «-1», pending/failed ≠ подтверждено, документ = знания, «не тестировали» ≠ готов), wiring-guard в `TestDialogClientTests`; R16: `BotTestSheetLateResultTests` (настоящий лист, ответы «припаркованы» и отпускаются позже — до 5/7 красные, после 7/7), `BotTestResultPolicy` + source-guard в `TestDialogClientTests` | «тест пройден» = бот ответил (не оценка качества ответа) |
| 7.3 последние ответы с причиной | staging_verified (сервер) / locally_verified (клиент) | Handoffs `op: "status"` → `recent` (12 последних `reply_events`: state, время, execution id, transport id, чат = последние 4 цифры, текста клиента нет; read-only op без id); клиент `HandoffSyncService.RefreshStatus()` → `ReplyOutcomeCopy` (Отправлено / Не отправлено: режим «Вместе» / лимит диалогов / Ошибка отправки / Отправка не подтверждена / …) в листе «Проверка бота» | e2e-test-dialog `handoffs_status_lists_last_outcomes_owner_scoped` (строка `send_unknown` с chatTail 6543 и execution id; чужой install 403; без текста), `ReplyOutcomeCopy` в `BotReadinessTests.OutcomeLabels_…`; первый прогон нашёл дефект (status требовал `id` как take/done → 400) — исправлен в builder'е и повторён | без push: владелец видит причины при открытии листа/Сводки |
| 7.4 визуальное различие Авто/Вместе | done без изменений | состояния уже различимы после этапа 2 (pending dim + «Сохраняем режим…»/«Режим не сохранён» на карточке, подтверждённый режим — обычная капсула) и этапа 4 («Заявка ·»); решение — не менять UI без device-pass | — | — |
| 7.5 обновление существующих клонов | staging_verified | `Tools/n8n/update-clones.py`: клон = шаблон + 4 поля идентичности (имя, path/webhookId, systemMessage, settings) + credential-id по типам; dry-run с diff и точкой отката (`versionId` до обновления), `--apply --yes`, штамп `crTemplateVersion` в sticky-note (n8n `meta` read-only на PUT), read-back (узлы, path, промпт, штамп, активность, `activeVersionId == versionId`), идемпотентно; `Tools/n8n/verify-deployed.py` — read-only сверка инстанса с каноном (прод: `docs/audits/2026-09-22-prod-readback.json` — 20 DIFF, 5 MISSING, клоны «13»/«15» BEHIND, 33 узла) | e2e-clone-update `clone_update_old_template_to_canonical` (клон с шаблона 95221844: 33 → 57 узлов, path/промпт сохранены, штамп, повтор — up_to_date, входящее → sent ×1, ledger sent); реальное применение на staging-ботах 2026-09-22 (оба BEHIND только по `Ask to Send Text` → AT-CANONICAL, rollback pointers напечатаны) | типы вебхуков (`webhook/types/set`) у существующих профилей — шаг владельца (release-package §4.5); на проде не запускался |
| 7.6 пакет развёртывания | done | `docs/audits/release-package.md`: бэкап, снимок «до» (read-back), версии (staging 2.27.4 vs прод 2.36.7), SQL по порядку, env, workflows (новые/обновлённые/шаблоны/клоны), read-back «после», клиент (`inspect-artifact.sh`), canary из 7 шагов, откат по слоям, действия только владельца, честный список непроверенного | — | — |
| 7.7 развёртывание на прод | **не выполнено** — нет отдельного явного разрешения владельца | прод read-only на всех этапах | `docs/audits/2026-09-22-prod-readback.json` — единственное обращение к проду (чтение) | — |

**Приёмка этапа 7:** «Проверить бота» отвечает настоящим промптом и ничего не отправляет/не считает ✓ (staging, счётчики);
статус готовности из надёжных источников ✓ (юнит); последние ответы с причиной ✓ (staging + копия); клоны обновляются без пересоздания
и без потери промпта/path ✓ (staging, e2e + реальные staging-боты); пакет + откат + canary описаны ✓; прод не трогался ✓.
e2e-test-dialog 4/4, e2e-clone-update 1/1; регрессия после обновления staging-ботов до канона: e2e-reply 30/30 (28 прежних + 2 медиа),
e2e-handoff 12/14 в полном прогоне — оба провала (`notifier_delivers_self_message…`, `handoffs_webhook_owner_scoped…`) проходят 2/2 при
изолированном повторе (взаимное влияние сценариев на общих staging-профилях, как и в прогоне этапа 4; регрессии шаблона нет); Unity 2679/2679. Итоговые документы: матрица `2026-09-22-remediation-matrix.md`, статус-раздел в аудите.

## Развёртывание 2026-09-24 (прод, по явному разрешению владельца)

Порядок — как в `release-package.md`; каждый шаг с проверкой. Прод: n8n 2.36.7, SQLite, **Code-узлы в task runners** (`N8N_RUNNERS_ENABLED=true` — на staging runners не было).

| Шаг | Результат | Доказательство |
|---|---|---|
| Бэкап + снимок «до» | `~/backups/n8n-2026-09-24.tar.gz` (свежий, 06:58 UTC); `2026-09-24-prod-before.json` (20 DIFF, 7 MISSING, клоны «13»/«15» BEHIND) | файлы |
| SQL (6 миграций) | все 200 через `.secrets/sql-oneoff.py`; RLS=true на 8 таблицах; колонки `bot_profiles.owner_id/workflow_id/claimed_at`, `reply_mode_flags.version`, `chat_reply_state.handoff_paused/incoming_generation` | пост-чек одной строкой |
| Env + рестарт | 7 переменных вставлены в `docker-compose.yml` (секрет 64 hex сгенерирован на VPS, нигде не печатался; `CR_AUTH_COMPAT_UNTIL=2026-12-31T00:00:00Z`), `docker compose config` valid, контейнер пересоздан, healthz 200 | env в контейнере (имена; длина секрета) |
| Canary runners | самоудаляющийся Webhook→Code: `require('crypto')` HMAC ok, `$env.CR_AUTH_SECRET` len 64, `$env.CR_AUTH_COMPAT_UNTIL` виден — под task runners | `{"hmacLen":64,"secretLen":64,"until":"2026-12-31T00:00:00Z"}` |
| 7 новых workflows | `docker exec … n8n import:workflow --separate` (id сохранены), `publish:workflow` для 2 sub-workflows, API activate для 5 триггерных; read-back active=True, activeVersionId==versionId у всех | API GET |
| Smoke гейтов | AuthSession nonsense → 400; WappiProxy/Handoffs/TestDialog без токена → 401; плохой токен → 401; bootstrap → refresh → с токеном: unknown_op 400 (токен проверен), чужой профиль → 403 `forbidden:profileId`; пробный install удалён | вывод smoke |
| 15 обновлённых workflows | `build-client-webhooks.py --update` (Set Workflow State с защищённым списком новых id, Support Message) + `deploy-canonical.py --apply --yes` (13: Dashboard Outcomes, Edit WA/TG, шаблоны WA/TG, Suggest Replies, Upload File, Set Reply Mode, Create WA/TG, Delete File, Get Usage, Delete Bot Files) — read-back ok у каждого, точки отката (versionId до) в выводе | `2026-09-24-prod-after.json`: 26/26 OK, unknown 0, 24 активных опубликованы (2 шаблона неактивны по определению) |
| Compat для старых клиентов | без токена: GetUsage 200, SetWorkflowState protected → 400 `protected_workflow`, SuggestReplies → legacy-непрозрачный `generation_failed`, SupportMessage пустой → 400 — ни одного 401; повторено после контрольного рестарта | вывод smoke |
| Клоны | **обновлять нечего**: «13» архивирован в n8n (14.09, вручную владельцем; PUT на архивный n8n запрещает — `update-clones.py` теперь пропускает archived), «15» удалён ВРУЧНУЮ в редакторе n8n во время деплоя — владелец подтвердил, что это он (аудит-лог: deactivate 12:41:37 → archive 12:41:42 → delete 12:41:45 Алматы под его учёткой; мои API-удаления касаются только временных `ZZZ TEMP` workflows и дают только событие `deleted`). Оба профиля списаны lifecycle-sweep'ом 14.09 (`trial_expiry`/`churn_grace`) | аудит-лог n8n, `bot_profiles` |
| Типы вебхуков у существующих профилей | нечего применять — живых ботов на проде нет; новые боты получают три типа при создании | — |
| Контрольный рестарт | после `publish:workflow` (подсказка CLI) — healthz через 12 с, активные workflows подняты at init; Notifier активен (успешные прогоны не сохраняются по настройке `saveDataSuccessExecution: none`, ошибок нет) | docker logs, API |
| Логи после деплоя | «Error while saving insights metadata and raw data» раз в 30 с (интервал сброса внутренней статистики insights, не исполнение workflows). Причина найдена к вечеру: временные workflows харнесов (`sql-oneoff.py`, `canary-env.py`) удалялись сразу после исполнения, а n8n 2.36 держит событие уже удалённого workflow в буфере insights и с этого момента каждый сброс падает до рестарта (первый эпизод 07:40–07:52, второй с 09:27 после диагностических запросов). Исправлено: харнесы ждут 35 с перед удалением; рестарт n8n 12:5x — 0 ошибок после. На ботов/клиентов не влияло (все прогоны success) | `docker logs -t`, гистограмма по 10 мин |

**Не проверено на проде (нужен живой бот с нового клиента):** ответ бота по новой цепочке (claim → final check → lease → ledger), Test Dialog, handoff-инструмент, self-message, русский ответ на медиа. **Не сделано:** ротация Wappi-токена (только после выхода клиента), закрытие compat-окна, push.

### Hotfix 2026-09-24 (найден владельцем на первом боте с нового клиента)

Симптом: код WhatsApp принят, статус «Авторизовано», но профиль Wappi тут же удаляется. По журналу прода: три подряд `CreateWhatsappWorkflow` → `Count Channels` c=1/2/3, `plan=business status=expired` → `Compute Slot Limit` limit=0 → `Respond Channel Limit` + `Delete Refused Profile`. Две причины:
1. **Аккаунт владельца** (`$RCAnonymousID:4a7af3f8…`) в `subscribers` был `business/expired` с 16.09 (sandbox-подписка истекла) → 0 слотов — штатное поведение биллинга (Task 17a), не A1. Исправлено данными: подписка сброшена на свежий `trial/trialing` (как раньше делал `.secrets/fix-subscriber.py` для прежнего тестового id).
2. **Регрессия A1:** прокси `Bind Profile` регистрирует строку `bot_profiles` в момент СОЗДАНИЯ профиля (owner-bound), а `Count Channels` считает все живые строки app-user'а, включая создаваемый профиль (до A1 строка появлялась только ПОСЛЕ проверки слота) → лишний +1 для всех тарифов (Start с одним каналом отказал бы первому же боту), и отказанный профиль оставался «призраком» (у владельца c росло 1→2→3). Исправление `Tools/n8n/apply-slot-count-fix.py` (идемпотентно, `--check`): `Count Channels` исключает создаваемый профиль (`$4`), новый узел `Retire Refused Profile` списывает строку отказанного (`deleted_reason='channel_limit'`). Три «призрачные» строки на проде списаны. Staging: e2e-auth `create_slot_count_excludes_own_profile_and_retires_refused` (тариф start: первый канал принят, второй отказан, у мока ровно одно `profile/delete`, строка списана) + прежний `create_seeds_reply_mode_before_activation` — оба PASS; на проде оба Create обновлены `deploy-canonical.py` с read-back.
Попутно: мок Wappi нумерует профили счётчиком (`profile-000001…`), после его рестарта id повторяются и упираются в старые строки staging-БД с чужим владельцем (гейт → 403) — перед прогонами после рестарта мока чистить `bot_profiles where profile_id like 'profile-%'` на staging.

### Canary на живом боте владельца (прод, 2026-09-24, бот «23», клон `dt6hHFUVrW07unTj`)

| Шаг | Результат |
|---|---|
| Create по новой схеме | slot check c=0/limit 3 (после hotfix), профиль owner-bound (A1), workflow привязан, режим «Вместе» посеян до активации (A2), у Wappi URL + три типа вебхуков; клон из нового шаблона (56 узлов, вся цепочка этапов 3–4) |
| Входящее в «Вместе» | `suppressed`, строка `client` в истории и human-строка в памяти (контекст сохранён для последующего «Авто») |
| «Авто» из приложения + вопрос | строка `*` suppressed=false с версией клиента (A2 на проде); прогон: claim (поколение 1) → квота (trial) → final check → lease → `sent` с transport id → Record Send; ledger `sent`, история `client`+`ai`, диалог посчитан 1 раз; эхо отправки пришло как `outgoing_message_api` → `outgoing_known` (canary формы исходящих событий Wappi — форма совпала с входящим) |
| Фото без текста | ветка `Ask to Send Text`: русский + казахский текст, та же цепочка → `sent`; история `client` (image) + `ai`; лимит не расходован повторно |
| Ответ владельца с телефона | `outgoing_message_phone` → `recorded_owner_phone`: строка `owner_phone` в истории и помеченная строка в памяти |
| Заявка от клиента | агент вызвал `Create Handoff` → `handoff_requests` id 1 (open, order, имя/телефон, привязка к сообщению) → ТОЛЬКО ПОСЛЕ этого ответ клиенту `sent`; чат поставлен на паузу; Notifier доставил self-message с первой попытки через 62 с (`notify_state: sent`), в истории он — `outgoing_api` в чате собственного номера бота, без данных клиента |
| «Проверить бота» из приложения | Test Dialog run 3869: dry-handoff записан в `test_dialog_actions` (`create_handoff`), при этом ledger/история/память/заявки/счётчик диалогов НЕ изменились (3/10/7/1/1 до и после); успешные прогоны Test Dialog не сохраняются в n8n по настройке (текст теста — владельца) |

Не проверено живьём: «Вместе» во время генерации (`cancelled_mode`) и повторная доставка события (`duplicate`) — оба покрыты staging-сценариями; self-message заявки на телефоне владелец подтверждает сам.

## Развёртывание 2026-10-09 (прод, по явному «да» владельца на пакет итераций 27.09 + 28.09)

Пакет: `docs/audits/2026-09-27-iteration/release-package.md`, итоговый отчёт `docs/audits/2026-09-28-iteration/final-report.md`.

| Шаг | Результат | Доказательство |
|---|---|---|
| Снимок «до» | `verify-deployed`: канон 27, DIFF 22 + MISSING `crPriceSearch001`, живых клонов 0, архивный «13», other 0 | `docs/audits/2026-10-09-prod-before.json` |
| Бэкап | `~/backups/n8n-2026-10-09-predeploy.tar.gz` (07:49 UTC, 28 workflow + креды в зашифрованном виде + config); утренний сохранён как `…-0315.tar.gz` | `ls ~/backups` |
| Предпроверки (read-only) | дублей billing id 0; индекс `documents_metadata_file_id_idx` есть; installs 1, живых `bot_profiles` 1 | `sql-oneoff` |
| SQL (5 миграций) | claim-pairing, billing-bindings, reply-order, claim-code-attempts, retired-clone-deactivate — все 200; пост-чек: 5 колонок на месте, `billing_bindings` создана (1 строка seed), RLS = true | `sql-oneoff` |
| Новый `crPriceSearch001` | `n8n import:workflow --separate` (id сохранён) + `publish:workflow` + контрольный рестарт n8n; healthz 200 | CLI, healthz |
| 22 workflow | `deploy-canonical.py --apply --yes`: updated 20, failed 0, read-back ok у каждого (точки отката — `docs/audits/2026-10-09-deploy-rollback-pointers.txt`); `build-client-webhooks.py --update`: Support Message + Set Workflow State обновлены и активны, обратный экспорт — 0 диффа с каноном | вывод инструментов |
| Read-back | `verify-deployed --base prod` exit 0: канон 27, diff 0; `crPriceSearch001` active+published; Notifier и Sweep активны; клонов 0 (обновлять нечего) | `docs/audits/2026-10-09-prod-after.json` |
| Smoke (без изменений данных) | без токена: WappiProxy/Handoffs/TestDialog → 401; AuthSession nonsense → 400; SetWorkflowState на `crPriceSearch001` → 400 `protected_workflow`; SupportMessage пустой → 400 | curl |
| Логи 25 мин | ошибок исполнения нет; ошибок insights нет (харнес ждёт 35 с); 4 × «No item to return» — ответы временного вебхука `sql-oneoff` на миграции из одних ALTER (колонки созданы, пост-чек) | `docker compose logs` |

Попутно: `sql-oneoff.py --help` исполнил строку `--help` как SQL (комментарий, без эффекта); его временный workflow удалён (снимок «до» — other 0).
**Не проверено на проде (нужен живой бот с новой сборки):** canary §4 пакета — загрузка .txt-прайса, вопрос о цене с артикулом, Test Dialog, заявка → self-message, удаление бота.

## Развёртывание 2026-10-11 (прод, по явному «да» владельца на серверные доработки M4 + V9-r3 + V6-concurrent)

Журнал доработок: `docs/audits/2026-10-09-followups/README.md`. Клиентские части (M4 клиент, C1/C1b) — в новой сборке, не здесь.

| Шаг | Результат | Доказательство |
|---|---|---|
| Снимок «до» | `verify-deployed`: канон 27, DIFF 5 (шаблоны WA/TG, Suggest Replies, Auth Session, Price List Search), Create/Edit совпадают (Q1 A снят), клонов 0, архивный «13» | `docs/audits/2026-10-11-prod-before.json` |
| Бэкап | `~/backups/n8n-2026-10-11-predeploy.tar.gz` (13:05 UTC, 29 записей), утренний — `…-0315.tar.gz` | `ls ~/backups` |
| Миграции | нет | — |
| 5 workflow | `deploy-canonical.py` dry-run → `--apply --yes`: updated 5, failed 0, read-back ok у каждого; активные (Suggest Replies, Auth Session, Price List Search) опубликованы самим PUT — новый путь D1 «без окна deactivate» впервые подтверждён на настоящем n8n; шаблоны неактивны по определению | `docs/audits/2026-10-11-deploy-rollback-pointers.txt` |
| Read-back | `verify-deployed --base prod` exit 0: канон 27, diff 0; Notifier активен; клонов 0 | `docs/audits/2026-10-11-prod-after.json` |
| Smoke (без изменения данных) | WappiProxy без токена → 401; AuthSession nonsense / claim.start без токена → 400; SetWorkflowState на `crPriceSearch001` → 400 `protected_workflow`; SupportMessage пустой → 400 | curl |
| Логи 15 мин | ни одной строки с ошибкой | `docker compose logs` |

Не проверено на проде (нужен живой бот с новой сборки): M4 — бот после триала получает 410 один раз; V9-r3 — пометки на реальном прайсе; V6 — параллельные ответы.

## Развёртывание 2026-10-13 (прод, по явному «да» владельца на серверную часть блока F1 + F2 + Q1 B + V6-unknown + V3)

Пакет: `docs/audits/2026-10-09-followups/README.md`, раздел «Пакет выкладки блока»; репетиция — `evidence/T12-rehearsal.txt`.
Время — UTC 2026-10-12 20:03–20:35 (по Алматы 2026-10-13 01:03–01:35). Клиентская часть (T8/T9) — в новой сборке, не здесь.

| Шаг | Результат | Доказательство |
|---|---|---|
| Снимок «до» | `verify-deployed`: канон 27, DIFF ровно 12 из пакета, хэши «до» = таблица пакета 12/12 (прод не менялся с 2026-10-11); клонов 0, архивный «13», other 0 | `docs/audits/2026-10-13-prod-before.json` |
| Инвентаризация (read-only, F1 Q5) | `bot_profiles`: живых 1, удалённых 17 (trial_expiry 2, liveness 3, channel_limit 3, churn_grace 2, app_delete 6, без причины 1); единственный названный workflow `dt6hHFUVrW07unTj` давно отключён Sweep'ом и в n8n отсутствует; архивный «13» не назван ни одной строкой; installs 1 — бродячих клонов нет | `sql-oneoff` |
| Сбой DNS (не наш) | 20:03:56–20:05:30 контейнер n8n не разрешал `aws-1-ap-southeast-1.pooler.supabase.com` (EAI_AGAIN, 16 строк; за сутки до этого — ни одной); первые два запроса инвентаризации ответили 500, после — хост и контейнер разрешают, повтор 200 | `docker compose logs` |
| Бэкап | `~/backups/n8n-2026-10-13-predeploy.tar.gz` (20:08 UTC, 29 workflow); утренний сохранён как `n8n-2026-10-12-0315.tar.gz` | `ls ~/backups` |
| Dry-run | `deploy-canonical.py`: would_update 12 — ровно пакет; точки отката | `docs/audits/2026-10-13-deploy-rollback-pointers.txt` |
| Права `n8n Admin API` | самоудаляющаяся проба: GET шаблона 200, POST create 200, activate 200, DELETE 200, остатков 0 | `evidence/deploy-1013-admin-cred-probe.txt` |
| M1, M2 | обе 200; пост-проверки: M1 RLS t, 6 колонок, 2 индекса; M2 2 колонки, RLS t, 9 колонок, 2 индекса; обе таблицы пусты | `sql-oneoff` |
| 12 workflow (шаги 3–8) | по порядку пакета, `--only … --apply --yes`: updated 12, failed 0, read-back ok у каждого; все активные опубликованы самим PUT (без окна deactivate); шаблоны неактивны; в живых Create / Sweep / Set Workflow State одна и та же учётка `n8n Admin API`, плейсхолдеров нет | вывод инструмента |
| `update-clones.py` | клонов 0 — обновлять нечего; архивный «13» пропущен | вывод инструмента |
| M3 | до: помечать нечего (0 и 0 из 272 строк памяти); применена (200), пост-проверки 0 и 0 | `sql-oneoff` |
| Read-back | `verify-deployed --base prod` exit 0: канон 27, diff 0, хэши «после» = таблица пакета 12/12; клонов 0; Notifier и Sweep активны; other 0 | `docs/audits/2026-10-13-prod-after.json` |
| Smoke (без изменения данных) | WappiProxy без токена → 401; AuthSession nonsense / claim.start без токена → 400; SetWorkflowState на `crPriceSearch001` → 400 `protected_workflow`; SupportMessage пустой → 400; Create WA/TG с поддельным токеном → HTTP 200 `unauthorized` (`bad_signature`); DeleteBotFiles с поддельным токеном → 401 — всё как на репетиции; после smoke other 0 | curl |
| Insights (наш побочный эффект) | проба прав удалила свой workflow сразу после выполнения → с 20:11:19 каждые 30 с «Error while saving insights metadata and raw data» (сброс insights называл удалённый workflow); на исполнение не влияло; `docker compose restart n8n` в 20:19:37, healthz 200 через ~12 с, verify-deployed 0 после рестарта. Правило: временный workflow живёт ≥ 35 с после выполнения (так делает `sql-oneoff`; скрипт пробы в evidence исправлен) | `docker compose logs` |
| Логи 15 мин после рестарта | 20:19:40–20:35:11: ни одной строки с ошибкой (insights 0, EAI_AGAIN 0); только стартовые сообщения рестарта | `docker compose logs` |

Дальше (каждое — по отдельному «да» владельца): шаг 12 пакета — через сутки, если журнал ветки A Sweep'а (`Log Would Remove`) чист,
`RETIREMENTS_DRY_RUN = False` и выкладка Sweep'а; новая клиентская сборка с HEAD → `inspect-artifact.sh` → device-pass §5.4 → живой
canary → сторы. **Не проверено на проде (нужен живой бот с новой сборки):** переподключение после истёкшего триала с переносом данных,
«Сменить номер» / «Получить код ещё раз» без второго клона, удаление бота по линии, Q1 на живом боте, журнал V3 на реальном конфликте.

## Learning A «Фундамент» — реализовано, ждёт go на деплой (2026-10-14; коммиты 126f44c2…HEAD)
Правило «Границы темы» + eval v1.7 (before/after: ot-a: 4·2·5 → 10·1·0; core-a 83 авто: 71·1·11 → 76·4·3, просевшие ×8 — шум; панель 9·2·2 → 12·1·0), панель «Отказ»/холодный старт, origin_* через meta прокси, suggestion_sets после Respond, CTE sets в Delete Chat Memory, Expire Learning Rows, клиент SendOrigin*/SuggestionPickStats, privacy §3. Staging: e2e-learning 10/10, e2e-suggest 12/12, e2e-auth/e2e-handoff без регрессий; Unity EditMode 3017/3017; n8n harness run_all 32 файла OK.

## Развёртывание 2026-10-14 — learning A «Фундамент» + панель 13.10 («Авто = продолжение подсказок»), по «go» владельца
Пакет: миграция `sql/2026-09-25-learning-foundation.sql` → 8 workflow (Suggest Replies, Create WA/TG, Edit WA/TG, Wappi Proxy,
Delete Bot Files, Profile Lifecycle Sweep) через `deploy-canonical.py --only … --apply --yes` → `update-clones.py --apply --yes`
(живых клонов 0, «13» архивный пропущен) → `verify-deployed.py` diff 0. Владелец подтвердил отдельно, что вместе с learning A
уезжает и невыложенное изменение 13.10 (панель gpt-5.4-mini + «УСЛОВИЯ», «Правило владельца об условии») — те же канонические файлы.
Репетиция на стенде с прод-состояния (`bootstrap.py --canon-ref 886eaf82`): миграция первой, dry-run назвал ровно 8, apply —
каждый опубликован своим PUT с read-back ok, verify-deployed 0, e2e-learning 10/10, e2e-suggest 12/12, lineage/delete PASS.
Прод: before = 8 DIFF (`docs/audits/2026-10-14-prod-before.json`), миграция `{"success":true}`, пост-проверка одним запросом
`{"origin_cols":"6","rls":true,"anon_grants":"0","idx":"2"}`, apply 8/8 «published by the PUT … read-back: ok», after = diff 0
(`docs/audits/2026-10-14-prod-after.json`), точки отката `docs/audits/2026-10-14-deploy-rollback-pointers.txt`, GET-пробы восьми
вебхуков — «Did you mean to make a POST request?» (зарегистрированы). `.secrets/sql-oneoff.py` печатает полный ответ (план 1a).
НЕ сделано: публикация `privacy.html` на сайт (чек-лист шаг 5) — `deploy.sh --go` увёз бы и невыложенный коммит 2839245c (esbuild-
сжатие ассетов; live 404 на текущий `assets/e85b3e936c`), а `tools/check.mjs` на этой машине не поднялся (headless-навигация); клиент
с меткой ещё не выпущен, так что страница не просрочена — отдельное «да» владельца. Дальше: device pass трёх пунктов learning A,
первый `report-learning.py --prod --days 3` через 2–3 дня работы бота.

## Развёртывание 2026-10-14 (прод, шаг 12 пакета блока F1 + F2, по «да» владельца)

Пакет — `docs/audits/2026-10-09-followups/README.md` «Пакет выкладки блока», шаг 12: ветка A Profile Lifecycle Sweep из режима
«только журнал» в рабочий.

| Шаг | Результат | Доказательство |
|---|---|---|
| Журнал первых суток | проверка `sweep-branch-a-check-1014` (только чтение): 5 запусков Sweep 13–14.10 — все success, ошибок в узлах нет; `Clone Retirements` 0 строк (к удалению ничего), `clone_retirements` пуст, логи n8n чистые, verify-deployed 0 | отчёт задачи |
| Прод до | 10:40 другая сессия выложила learning A + панель 13.10 (`56bdc56b`); verify-deployed: канон 27, diff 0 — изменение канона в одном флаге = изменение прода в одном флаге | `verify-deployed` |
| Канон | `RETIREMENTS_DRY_RUN = False` в `apply-sweep-clone-deactivate.py` → одна строка `Sweep Config` (`"value": false`); повторный прогон без диффа, `--check` 0; verify-clone-lifecycle ALL PASSED, test_clone_retirements 21/0, test_verify_clone_lifecycle 44/0 | `4761117b` |
| Dry-run | `deploy-canonical.py`: would_update 1 (только Sweep), up_to_date 24; точка отката `versionId c96f4306-025d-46e9-9a0d-b7a112db8c38` | вывод инструмента |
| Выкладка | `--only fXYpCXPKw92EzRz8 --apply --yes` (06:53 UTC): updated 1, опубликован самим PUT (без окна deactivate), read-back ok | вывод инструмента |
| Read-back | verify-deployed exit 0 (канон 27, diff 0, клонов 0); сохранённая и действующая версии: `retirementsDryRun = false`, `dryRun = false`, active | `docs/audits/2026-10-14-step12-prod-after.json` |
| Первый рабочий запуск | execution 19518, 07:00:29–07:00:31 UTC: success, ошибок в узлах нет; `Clone Retirements` 0 строк — удалять нечего; `Expire Learning Rows` отработал | API executions |
| Логи | с 06:53 UTC ни одной строки с ошибкой | `docker compose logs` |

Откат: `deploy-canonical.py` с JSON Sweep из `4761117b^` (флаг true) — или публикация версии `c96f4306-…` по точке отката.
Дальше по пакету — только шаг 13 (новая клиентская сборка), по отдельному «да».

Сайт выложен 2026-10-14 07:29Z по «сайт — да» владельца: `tools/deploy.sh --go` (бэкап site.bak-20261014-122912 + Caddyfile.bak-20261014-122912,
rsync без --delete, Caddy validate + reload, smoke 200 на /, /privacy.html, /terms.html; review-видео и healthz как до); вместе уехал
коммит 2839245c (esbuild-сжатие ассетов); живой privacy.html несёт «срок хранения — до 180 дней». Push main — fast-forward.

## Learning B «Теневой счёт и замок «Авто»» — реализовано, ждёт go на деплой (2026-10-14; коммиты b00ee499…HEAD)
Get Usage отдаёт readiness по ботам (окно 20, ≥10 совпадений, ≥3 чата, ≥2 дня; legacy → []); клиент: AutoReadinessPolicy/Store
(заперто ТОЛЬКО по вердикту сервера), AutoLockGate на четырёх точках включения «Авто», лист «Бот ещё учится» («Понятно» /
«Включить всё равно», Back = «Понятно»), капсула «Учится · N/20» (190 → 291u для «17/20»). Стенд (канон пересобран): e2e-learning 11/11
(readiness_in_get_usage падал на пре-B каноне — nine=None), e2e-auth hooks_reject_foreign_ids + billing_id_first_bind без регрессий;
Unity EditMode 3037/3037; n8n test_learning_b 8/8, run_all 33 файлов OK; verify-learning-b / verify-sql-comments / verify-auth-gate OK.
Деплой: один workflow (Get Usage, `deploy-canonical.py --only jtbssfzXbOxwTK4k`) по «go»; миграций нет; клиент — в сборке вместе с A.
После независимого ревью (тот же день, 0 Critical / 1 Important / 7 Minor, вердикт «ready»): фикс-пасс — (1) предусловие деплоя «B требует колонок A» (чек-лист шаг 0 + CLAUDE.md, пинит `verify-learning-b.py`); (2) пер-чатный тоггл бота, уже стоящего в «Авто» (возобновление чата после хэндоффа), замок НЕ встречает — `AutoLockGate.TryEnableChat` по режиму бота (спека §0 важнее перечня §3); (3) e2e-сценарий проверяет и legacy-вызов (`readiness: []`). Unity EditMode 3038/3038. Отложенные Minor — в итоговом сообщении сессии и в ledger.
Minor ревью закрыты тем же днём по слову владельца: копия листа «Авто откроется, когда…» (спека §3 обновлена); геометрия AutoLockSheet запинена в ConfirmCardLayoutTests (24u как у близнеца); AutoLockWiringTests — модификатор обязателен в regex + глобальный скан: каждый `CommitMode(…ReplyMode.Auto)` только внутри двух гейтируемых тел ПОСЛЕ вызова гейта; READINESS_SQL отчёта рендерится из фрагментов apply-learning-b (`send_filter`/`readiness_columns`, канон байт-в-байт тот же) и показывает только ботов с живой строкой (SQL-кейс 9/9); спека §3 — реальные триггеры обновления вместо «фокуса». Unity EditMode 3040/3040.
