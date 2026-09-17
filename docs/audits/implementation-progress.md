# Ход ремедиации аудита 2026-09-15/16 — журнал и точка продолжения

План этапов: `docs/superpowers/plans/2026-09-17-audit-remediation-plan.md`.
Статусы: planned · in_progress · locally_verified · staging_verified · deployed_verified · blocked.
Правило записи: пункт аудита → этап → изменения → доказательства → оставшиеся ограничения.

## Точка продолжения (обновляется в конце каждого этапа)

- **Дата:** 2026-09-17 · **Ветка/коммит:** `main` @ `95221844` (исходный HEAD; работа идёт на `main`
  scoped-коммитами — единый рабочий каталог, без worktree; перед каждым коммитом `git rev-parse HEAD`).
- **Текущий этап:** 0 (in_progress).
- **Следующий шаг:** см. раздел «Этап 0» ниже — открытые пункты 0.3–0.5.

## Baseline (этап 0.1) — зафиксировано 2026-09-17

- Git: HEAD `95221844` («docs(store): ревью №2 …»). Рабочие изменения на старте — только untracked:
  `.agents/skills/*`, `.codex/`, `AGENTS.md` (копия CLAUDE.md для Codex), `d.html`, `z.html`,
  `docs/audits/`, `docs/gtm/`. Ничего из этого не трогаем и не коммитим вместе с задачей.
- Unity Editor закрыт (нет `Temp/UnityLockfile`, только Unity Hub) → тесты через `Tools/run-tests-headless.sh`.
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
| 0.3 smoke SuggestReplies | in_progress | — | — | нужен изолированный staging (Postgres + Wappi-мок) |
| 0.4 репро-харнесс | planned | — | — | — |
| 0.5 диагностика исходов | planned | — | baseline-метрики сняты ДО изменений | — |

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
