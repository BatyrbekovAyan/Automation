# Ход ремедиации аудита 2026-09-15/16 — журнал и точка продолжения

План этапов: `docs/superpowers/plans/2026-09-17-audit-remediation-plan.md`.
Статусы: planned · in_progress · locally_verified · staging_verified · deployed_verified · blocked.
Правило записи: пункт аудита → этап → изменения → доказательства → оставшиеся ограничения.

## Точка продолжения (обновляется в конце каждого этапа)

- **Дата:** 2026-09-17 · **Ветка/коммит:** `main` @ `95221844` (исходный HEAD; работа идёт на `main`
  scoped-коммитами — единый рабочий каталог, без worktree; перед каждым коммитом `git rev-parse HEAD`).
- **Текущий этап:** 0 — ЗАКРЫТ 2026-09-17 (locally_verified + staging_verified). Следующий: этап 1 (A1).
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
