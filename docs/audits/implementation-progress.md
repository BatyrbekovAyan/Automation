# Ход ремедиации аудита 2026-09-15/16 — журнал и точка продолжения

План этапов: `docs/superpowers/plans/2026-09-17-audit-remediation-plan.md`.
Статусы: planned · in_progress · locally_verified · staging_verified · deployed_verified · blocked.
Правило записи: пункт аудита → этап → изменения → доказательства → оставшиеся ограничения.

## Точка продолжения (обновляется в конце каждого этапа)

- **Дата:** 2026-09-17 · **Ветка/коммит:** `main` @ `95221844` (исходный HEAD; работа идёт на `main`
  scoped-коммитами — единый рабочий каталог, без worktree; перед каждым коммитом `git rev-parse HEAD`).
- **Текущий этап:** 1 (A1) — код ЗАВЕРШЁН и staging_verified 2026-09-17 (сервер + клиент + claim); НЕ развёрнут
  (пакет: `docs/audits/a1-deploy-runbook.md`). Следующий: этап 2 (A2).
- **Коммиты этапа 1:** d44fc037 (сервер), 5448f98d (клиент), e3aec245 (claim), далее docs.
- **Как продолжить:** `Tools/n8n/staging/pg.sh start` → `python3 Tools/n8n/staging/wappi-mock.py &` →
  `python3 Tools/n8n/staging/bootstrap.py --restart` → `python3 Tools/n8n/staging/e2e.py`; чистый харнесс —
  `node Tools/n8n/repro/run-code-node.mjs`; четыре verify-скрипта; Unity — `Tools/run-tests-headless.sh`.
  Ожидаемое состояние ДО этапов 2–5: repro 13 pass / 4 fail, e2e 7 pass / 3 fail (все fail = дефекты аудита).

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
