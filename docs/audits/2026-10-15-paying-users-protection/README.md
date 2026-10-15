# Пакет выкладки, часть A «защита платящих» (A1–A6)

**Статус (2026-10-15):** всё в каноне на `main` (части A — коммиты `927e1067..9caddb6f`; канон пакета = `Tools/n8n/workflows` +
`Tools/n8n/sql` на `9caddb6f`, задача A7 их не меняла), **на проде НЕ выложено**. Пакет
отрепетирован на staging из прод-состояния (раздел «Репетиция»). Выкладка — **только по явному «да» владельца**; исполняет
контроллер по чек-листу ниже. Клиентская половина (A3 — триггер identity-heal, A4 — метка «Канал отключён», A6 — «Подписка»)
едет в новой сборке, не здесь. Часть B (расписание бота) в пакет НЕ входит.

План — `docs/superpowers/plans/2026-10-14-paying-users-protection-and-schedule.md` (A1–A7), спека —
`docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md`. Журнал — `docs/audits/implementation-progress.md`
(«Часть A «защита платящих» — реализовано, пакет отрепетирован, ждёт go»). Архитектура — CLAUDE.md, External APIs → «Part A».

## Прод сейчас (снимок «до», только чтение)

`python3 Tools/n8n/verify-deployed.py --base https://n8n.choosereply.com --json docs/audits/2026-10-15-paying-users-protection/prod-before.json`
(2026-10-15): канон 29, **diff 11 = ровно пакет** — DIFF ×9 + MISSING ×2 (таблица ниже); живых клонов 0, архивный «13», other 0.
Прод = канон `927e1067` (= последняя выкладка `d4a05405`, Get Usage 2026-10-14; между ними канон не менялся): графы 27
канонических файлов на `927e1067` совпадают с прод-снимком 27/27 (сохранённая и опубликованная версии) —
офлайн-сверка — `rehearsal.txt` §A (`verify-deployed.graph_hash` канона, извлечённого `git archive 927e1067`, против
`graph_hash_live`/`graph_hash_published` снимка). Если в день выкладки снимок покажет другое — **стоп и разбор**.

## Что меняется на сервере

Графы — `verify-deployed.graph_hash` («до» = прод-снимок 2026-10-15 = канон `927e1067`, «после» = канон этого пакета).

| Workflow | Id | Что | Генератор / владелец | Граф до → после |
|---|---|---|---|---|
| RevenueCat Events | `ZGYr6srzS3rSSXHp` | A1: `Dedup Gate → If Duplicate?` + журнал `rc_event_ledger` в транзакции апсерта; A2: узкий перенос плана на alias-пути | без генератора (ручная правка, `verify-telegram-parity.py` `check_rc_event_ledger`); запросы Consolidate/Transfer — `apply-billing-consolidation.py` | `8ce1e0a256a1` → `1196077677b4` |
| Delete Orphan Profiles | `2islisFH7jjLoPQM` | A4: `Live Profiles` + защита «живая строка ∧ Wappi когда-то авторизовал» | без генератора (`verify-orphan-sweep.py`) | `4b14cf5348c0` → `d617975779c3` |
| Handoff Notifier | `crHandoffNtfy001` | A5: номер владельца по правилу V2 (`OWNER_PHONE_JS`) | `build-handoff-workflows.py` | `80eebf0b71f8` → `53faf5b0fbe6` |
| Auth Session | `crAuthSession001` | A3: `claim.verify` переключает `bot_profiles.app_user_id` на биллинговый id установки | `build-auth-workflows.py` | `32ddd28b0ec6` → `faeae51618cd` |
| Set Workflow State | `0DP79STI3pafXO9B` | защищённый список +`crVerifyIdent001` +`crChannelWatch001` | `build-verify-identity.py` / `build-channel-watch.py` (патч списка); выкладка — см. шаг 3 | `9bb5e0da92e0` → `36b6a73e9773` |
| Create WA | `XuvOp7TxOImOAmlj` | только +2 id в защищённых списках | `apply-clone-lifecycle.py` | `cc578d6f095d` → `78ed47e160c0` |
| Create TG | `Uz6HBBUpAiUqVysB` | только +2 id в защищённых списках | `apply-clone-lifecycle.py` | `7463a139ffc5` → `51becea5ebee` |
| Delete Bot Files | `lmjYsdNcQA2IE5rl` | только +2 id в защищённых списках | `apply-auth-gate.py` + `apply-history-handoff.py` | `52a2799f91be` → `64f4c6dfda90` |
| Profile Lifecycle Sweep | `fXYpCXPKw92EzRz8` | только +2 id в защищённых списках | `apply-sweep-clone-deactivate.py` | `24b1a5c2f44c` → `624c8bd7733f` |
| **Verify Identity (новый)** | `crVerifyIdent001` | A3: `/webhook/VerifyIdentity` — identity-heal после переустановки | `build-verify-identity.py` | — → `3af1831f3ca4` |
| **Channel Watch (новый)** | `crChannelWatch001` | A4: каждые 10 мин, «канал отключился» владельцу через другой его канал | `build-channel-watch.py` | — → `e907de5eae43` |

`deploy-canonical.py` новые workflow не создаёт — они импортируются CLI (`n8n import:workflow`, id сохраняются). Шаблоны ботов
(`4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc`) в пакет не входят → `update-clones.py` работы не имеет (на проде клонов 0). Env не
меняется. Новая учётка: «RevenueCat API» `crRevenueCatApi1`.

### Миграции (все аддитивные и идемпотентные; пост-проверки — в файлах и одним запросом `postcheck.sql`)

| # | Файл | Зачем | Когда |
|---|---|---|---|
| M1 | `Tools/n8n/sql/2026-10-14-rc-event-ledger.sql` | `rc_event_ledger` (A1) | до RevenueCat Events: без таблицы CTE апсерта падает → каждое событие 500, RevenueCat повторяет |
| M2 | `Tools/n8n/sql/2026-10-14-channel-snapshot.sql` | `channel_snapshot` (+ `notify_attempts`) (A4) | до импорта `crChannelWatch001`: без таблицы `Live Rows` падает каждые 10 мин |
| M3 | `Tools/n8n/sql/2026-10-15-identity-heal-log.sql` | `identity_heal_log` (A3, журнал поддержки) | до импорта `crVerifyIdent001`: без таблицы каждый heal отвечает 503 |

Таблицы независимы (нет ссылок друг на друга), порядок между ними не важен — важен «до своих workflow». Ожидание `postcheck.sql`:
`ledger_cols 2 | snapshot_cols 6 | heal_cols 8 | rls t | client_grants 0 | seq_client_usage f | indexes 4`.

### Учётка «RevenueCat API» (A3)

httpHeaderAuth, заголовок `Authorization`, значение `Bearer <secretKey>` из `Tools/n8n/.secrets/revenuecat-api.json`
(gitignored; ключ v2 проверен A3 только на чтение). **Id строго `crRevenueCatApi1`** — на него ссылается канон Verify Identity,
и каждый будущий PUT `deploy-canonical.py` несёт именно его. REST n8n id не принимает (проверено на репетиции — см. ниже),
поэтому учётка создаётся CLI `n8n import:credentials` из расшифрованного JSON. **Значение никогда не печатается, не попадает
в чат, документы, логи и git**: файл собирается локально в `.secrets/` (chmod 600), идёт в контейнер потоком stdin, удаляется
везде сразу после импорта.

## Чек-лист выкладки (прод, только после «да» владельца)

Все команды — из корня репо, с явной базой: инструменты по умолчанию смотрят на прод. `<date>` = дата выкладки.

0. **Предполётное (только чтение).**
   - `git status` чистый по `Tools/n8n/` и `git diff 9caddb6f HEAD -- Tools/n8n/workflows Tools/n8n/sql` пуст (канон = пакет).
   - Снимок «до»: `python3 Tools/n8n/verify-deployed.py --base https://n8n.choosereply.com --json docs/audits/<date>-prod-before.json`
     → канон 29, diff 11, ровно ids и хэши «до» из таблицы; клонов 0. Иначе — **стоп**.
   - Бэкап (утренний ночной архив того же дня не затирать — как 10-13):
     `ssh choosereply 'cd ~/backups && D=$(date +%F) && { [ ! -f n8n-$D.tar.gz ] || mv n8n-$D.tar.gz n8n-$D-0315.tar.gz; } && ~/choosereply/backup.sh && mv n8n-$D.tar.gz n8n-$D-predeploy.tar.gz && ls -la ~/backups | tail -4'`.
   - Dry-run + точки отката: `python3 Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com` → `would_update 8`
     (`ZGYr6srzS3rSSXHp`, `2islisFH7jjLoPQM`, `crHandoffNtfy001`, `crAuthSession001`, `XuvOp7TxOImOAmlj`, `Uz6HBBUpAiUqVysB`,
     `lmjYsdNcQA2IE5rl`, `fXYpCXPKw92EzRz8`), `missing 2` (`crVerifyIdent001`, `crChannelWatch001`), `up_to_date 17`, `failed 0`;
     и `python3 Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 0DP79STI3pafXO9B --include-placeholders`
     → `would_update 1`. Все строки `rollback pointer` → `docs/audits/<date>-deploy-rollback-pointers.txt`.
   - Учётки, на которые ссылаются новые workflow: `vvRrFiEXzLVqKjOx` (Postgres — у каждого Postgres-узла канона) и
     `EuhhqAaV56DpoqAN` (WappiAuthToken — тот же id несут живые канонические Create WA/TG, шаблоны ботов, Wappi Proxy и
     Handoff Notifier; бот-canary «23» 2026-09-24 работал через него); `crRevenueCatApi1` создаётся в шаге 2. Шаг 5 печатает
     учётки импортированных workflow — проверка, что импорт не перепривязал id.
1. **Миграции** (каждая — `200` или «No item to return»; затем пост-проверка):
   ```bash
   python3 Tools/n8n/.secrets/sql-oneoff.py "$(cat docs/audits/2026-10-15-paying-users-protection/precheck.sql)"   # t | t | t
   python3 Tools/n8n/.secrets/sql-oneoff.py "$(cat Tools/n8n/sql/2026-10-14-rc-event-ledger.sql)"
   python3 Tools/n8n/.secrets/sql-oneoff.py "$(cat Tools/n8n/sql/2026-10-14-channel-snapshot.sql)"
   python3 Tools/n8n/.secrets/sql-oneoff.py "$(cat Tools/n8n/sql/2026-10-15-identity-heal-log.sql)"
   python3 Tools/n8n/.secrets/sql-oneoff.py "$(cat docs/audits/2026-10-15-paying-users-protection/postcheck.sql)"
   # → {"ledger_cols":"2","snapshot_cols":"6","heal_cols":"8","rls":true,"client_grants":"0","seq_client_usage":false,"indexes":"4"}
   ```
2. **Учётка «RevenueCat API» `crRevenueCatApi1`** (значение не печатать):
   ```bash
   python3 - <<'PY'
   import json, os, pathlib
   s = json.loads(pathlib.Path("Tools/n8n/.secrets/revenuecat-api.json").read_text())
   out = pathlib.Path("Tools/n8n/.secrets/rc-cred-import.json")
   out.write_text(json.dumps([{"id": "crRevenueCatApi1", "name": "RevenueCat API", "type": "httpHeaderAuth",
                               "data": {"name": "Authorization", "value": "Bearer " + s["secretKey"]}}]))
   os.chmod(out, 0o600); print("written", out.stat().st_size, "bytes")
   PY
   ssh choosereply 'cd ~/choosereply && docker compose exec -T n8n sh -c "umask 077; cat > /tmp/rc-cred.json"' < Tools/n8n/.secrets/rc-cred-import.json
   ssh choosereply 'cd ~/choosereply && docker compose exec -T n8n n8n import:credentials --input=/tmp/rc-cred.json; docker compose exec -T n8n rm -f /tmp/rc-cred.json'
   rm -f Tools/n8n/.secrets/rc-cred-import.json
   ```
   Ожидание: «Successfully imported 1 credential.»; локальный файл и файл в контейнере удалены.
3. **Set Workflow State — защита двух новых id ДО их импорта и включения.** До этого шага legacy-вызов (compat-окно открыто до
   2026-12-31) мог бы выключить или удалить новые workflow через SetWorkflowState. Основной путь (отрепетирован):
   `python3 Tools/n8n/deploy-canonical.py --base https://n8n.choosereply.com --only 0DP79STI3pafXO9B --include-placeholders --apply --yes`
   — `__HDR_CRED__` привязывается к учётке `n8n Admin API` живого Set Workflow State, PUT, «published by the PUT», read-back ok.
   Прецедентный путь (09-24, 10-09, V12; на staging не репетируется — его state-файл несёт прод-id учёток):
   `python3 Tools/n8n/build-client-webhooks.py --update` (переложит и Support Message без изменений и заново включит оба —
   на n8n 2.36.7 activate активного workflow снимает вебхук на миллисекунды), затем `--export` + `git diff --stat
   Tools/n8n/workflows/` — пусто.
4. **Импорт двух новых workflow** (неактивными; файлы потоком в контейнер):
   ```bash
   for f in crVerifyIdent001-Verify_Identity crChannelWatch001-Channel_Watch; do
     ssh choosereply "cd ~/choosereply && docker compose exec -T n8n sh -c 'cat > /tmp/$f.json'" < Tools/n8n/workflows/$f.json
     ssh choosereply "cd ~/choosereply && docker compose exec -T n8n n8n import:workflow --input=/tmp/$f.json; docker compose exec -T n8n rm -f /tmp/$f.json"
   done
   ```
5. **Включение через API** (как триггерные workflow 09-24) + read-back:
   ```bash
   python3 - <<'PY'
   import importlib.util
   s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
   B = "https://n8n.choosereply.com"
   for wid in ("crVerifyIdent001", "crChannelWatch001"):
       st, _ = uc.call(B, "POST", f"/workflows/{wid}/activate", {})
       st2, w = uc.call(B, "GET", f"/workflows/{wid}")
       creds = sorted({r.get("id") for n in w.get("nodes", []) for r in (n.get("credentials") or {}).values()})
       print(wid, "activate", st, "| active", w.get("active"), "| published=stored", w.get("activeVersionId") == w.get("versionId"), "| creds", creds)
   PY
   ```
   Ожидание: `activate 200 | active True | published=stored True`; учётки Verify Identity = `crRevenueCatApi1` + `vvRrFiEXzLVqKjOx`,
   Channel Watch = `EuhhqAaV56DpoqAN` + `vvRrFiEXzLVqKjOx` (импорт не перепривязал id).
6. **Smoke Verify Identity:** `curl -s https://n8n.choosereply.com/webhook/VerifyIdentity` → `404` «… Did you mean to make a POST
   request?» (вебхук зарегистрирован, исполнения нет); `curl -s -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json'
   -d '{}' https://n8n.choosereply.com/webhook/VerifyIdentity` → `401` (гейт только по токену, compat-окно его не открывает).
7. **deploy-canonical по одному** (`--base https://n8n.choosereply.com --only <id> --apply --yes`; у каждого «published by the PUT …»
   и «read-back: ok»): `ZGYr6srzS3rSSXHp` RevenueCat Events (после M1!) → `2islisFH7jjLoPQM` Delete Orphan Profiles →
   `crHandoffNtfy001` Handoff Notifier → `crAuthSession001` Auth Session → `XuvOp7TxOImOAmlj` Create WA → `Uz6HBBUpAiUqVysB`
   Create TG → `lmjYsdNcQA2IE5rl` Delete Bot Files → `fXYpCXPKw92EzRz8` Profile Lifecycle Sweep (Create/Sweep — инструмент
   привязывает `__HDR_CRED__` к `n8n Admin API` и отказывает непривязанным).
8. `python3 Tools/n8n/update-clones.py --base https://n8n.choosereply.com` → клонов 0 (архивный «13» пропущен) — применять нечего.
9. **Read-back:** `python3 Tools/n8n/verify-deployed.py --base https://n8n.choosereply.com --json docs/audits/<date>-prod-after.json`
   → exit 0, канон 29, diff 0, хэши «после» = таблица.
10. **Smoke (без изменения данных):** GET `/webhook/VerifyIdentity` и GET `/webhook/RevenueCatEvent` → «Did you mean to make a POST
    request?»; POST VerifyIdentity без токена → 401; WappiProxy без токена → 401; AuthSession `{"op":"nonsense"}` → 400;
    SetWorkflowState без токена на `crChannelWatch001` → 400 `protected_workflow` (новый защищённый id; compat-окно открыто);
    DeleteBotFiles с поддельным токеном → 401.
11. **Первый тик Channel Watch** (≤ 10 мин после шага 5): в логах n8n ошибок нет; `sql-oneoff` (только чтение)
    `select count(*) as rows, count(*) filter (where authorized) as up, count(*) filter (where not authorized and notified_at is null) as owed from channel_snapshot;`
    → строки = живые профили, которые Wappi перечислил; `owed 0` (первое наблюдение ничего не должно, уведомлений нет).
12. **Логи 15 мин:** `ssh choosereply 'cd ~/choosereply && docker compose logs --since 15m n8n | grep -iE "error|insights" | tail -20'` → пусто
    (временные workflow `sql-oneoff` ждут 35 с перед удалением — цикл insights не возникает).
13. Журнал `implementation-progress.md` «Развёртывание <date> (часть A)» + снимки + точки отката, коммит `deploy(prod): …`.
14. **Наблюдения владельца — каждое по отдельному «да», нужен живой бот (на проде сейчас ботов 0 — после новой сборки):**
    (а) A5: настоящая заявка на Telegram-боте владельца, у которого `get/status` несёт `account.phone` ≠ верхнего `phone`, —
    уведомление приходит в «Избранное» собственного Telegram-аккаунта; (б) A4: разлогинить WhatsApp у бота с WhatsApp + Telegram —
    за ≤ 10 мин одно self-message «Choose Reply: WhatsApp отключился — …» в «Избранное» Telegram, повторная привязка ничего не шлёт.

## Откат

- **Обновлённые workflow** — по точке отката: `POST /api/v1/workflows/<id>/activate {"versionId": "<pointer>"}` снова обслуживает
  прежнюю версию (путь D1, без окна деактивации; сохранённая версия при этом остаётся новой — verify-deployed покажет DIFF, пока
  канон не возвращён), либо канон `927e1067`: `git show 927e1067:Tools/n8n/workflows/<файл> >
  Tools/n8n/workflows/<файл>` → `deploy-canonical.py --base https://n8n.choosereply.com --only <id> --apply --yes` (для Set Workflow
  State + `--include-placeholders`) → вернуть канон (`git checkout -- Tools/n8n/workflows/<файл>`).
- **Новые workflow** — `POST /workflows/<id>/deactivate`, ≥ 35 с, затем `DELETE /workflows/<id>` (правило insights). Выключить один
  Channel Watch достаточно, чтобы остановить уведомления. Клиент новой сборки на 404 Verify Identity просто не ставит отметку
  и спросит снова на следующем запуске — безвредно.
- **Учётка** — оставить (не используется) или `DELETE /api/v1/credentials/crRevenueCatApi1` после удаления Verify Identity.
- **Миграции** — остаются: старый канон таблиц не читает. Журналы `rc_event_ledger` / `identity_heal_log` — история, не удалять.
- Откат RevenueCat Events снимает защиту от повторной доставки (вернётся двойное зачисление при повторе); откат Delete Orphan
  Profiles снова разрешит удалять разлогиненный канал платящего через 24 ч — откатывать их только при явной поломке.

## Что НЕ выкладывается

- **Часть B** (расписание бота: `crSetSchedule001`, миграция `bot-schedule`, шаблоны, Create WA/TG, Delete Bot Files, Set Workflow
  State, Lifecycle Sweep во второй раз) — свой пакет B3 по отдельному «да».
- **Клиент** (A3 `IdentityHealTrigger`/`IdentityHealLatch` + вызов из `Manager`, A4 `ChannelHealth` + подстрока карточки, A6
  «Подписка») — новая сборка → `Tools/store/inspect-artifact.sh` → device-pass. Сервер безопасен для старых клиентов: Verify Identity
  зовёт только новая сборка; ответы прочих вебхуков не меняются.
- Env, шаблоны ботов, клоны, Supabase `documents`, сайт.

## Репетиция на staging (2026-10-15)

Стенд: n8n 2.36.7 с task runners (как прод), Postgres `pg.sh` (5433), Wappi-мок 127.0.0.1:8765; миграции — ещё и на одноразовом
кластере (5434, scratchpad). Прод не трогался, кроме снимка `verify-deployed` (только GET). Вывод каждого шага — `rehearsal.txt`
(рядом; сырые журналы — `Tools/test-output/a7/`, gitignored).

| Шаг | Результат |
|---|---|
| Свежий кластер: миграции | `STAGING_PGDATA=<scratch> STAGING_PGPORT=5434 pg.sh init` (все миграции, три новые последними) → пост-проверка `2 \| 6 \| 8 \| t \| 0 \| f \| 4`; `pg.sh migrate` ещё дважды — без ошибок, та же пост-проверка. Затем порядок ПРОДА на том же кластере: три таблицы сброшены (= схема прода), `precheck` `t \| t \| t`, M1 → M2 → M3 по одной (`psql -f`) → та же пост-проверка; все три повторно — только NOTICE «already exists, skipping» |
| Стенд = прод | `bootstrap.py --canon-ref 927e1067` (импорт 28 = канон 27 + staging-only eval runner, 2 бота); затем удалены `crVerifyIdent001` / `crChannelWatch001` (выключение → ≥ 35 с → удаление), учётка «RevenueCat API» и три новые таблицы. `verify-deployed`: канон 29, **diff 11 — статус каждого id совпадает со снимком прода** (DIFF ×9 + MISSING ×2); клоны стенда 2, AT-CANONICAL |
| Dry-run до учётки | `deploy-canonical.py --base http://localhost:5678` → `REFUSED: credentials the canon names are missing on staging: ['RevenueCat API']` — особенность стенда (производный канон требует все названные учётки); на проде вывода нет — dry-run идёт первым |
| M1 → M2 → M3 | через Postgres-узел n8n (двойник `sql-oneoff.py` на стенде, тот же харнес, удаление ≥ 35 с): precheck `{no_ledger,no_snapshot,no_heal_log: true}`, каждая миграция `200 {"success":true}`, пост-проверка `{"ledger_cols":"2","snapshot_cols":"6","heal_cols":"8","rls":true,"client_grants":"0","seq_client_usage":false,"indexes":"4"}` |
| Учётка | REST `POST /api/v1/credentials` с полем `id` → **400 `request/body/id is read-only`** (REST id не задаёт — нужен CLI); `n8n import:credentials` расшифрованного JSON при РАБОТАЮЩЕМ сервере → «Successfully imported 1 credential.», id `crRevenueCatApi1`, файл удалён (значение на стенде — заглушка) |
| Dry-run | would_update **8** (`2islisFH7jjLoPQM`, `Uz6HBBUpAiUqVysB`, `XuvOp7TxOImOAmlj`, `ZGYr6srzS3rSSXHp`, `crAuthSession001`, `crHandoffNtfy001`, `fXYpCXPKw92EzRz8`, `lmjYsdNcQA2IE5rl`), missing **2** (`crChannelWatch001`, `crVerifyIdent001`), up_to_date 17, failed 0; `--include-placeholders --only 0DP79STI3pafXO9B` → would_update 1; у каждого `rollback pointer` |
| Set Workflow State | `--only 0DP79STI3pafXO9B --include-placeholders --apply --yes` → «published by the PUT», read-back ok |
| Импорт + включение | `n8n import:workflow` производных копий при работающем сервере → «Successfully imported 1 workflow.» ×2, оба неактивны; `POST /workflows/<id>/activate` → 200, `active`, `activeVersionId == versionId`; учётки Verify Identity — `crRevenueCatApi1` + Postgres (каноническая привязка байт в байт) |
| Smoke Verify Identity | GET → 404 «… Did you mean to make a POST request?»; POST без токена → 401 `unauthorized:missing` |
| 8 workflow | по одному в порядке шага 7: updated 1 ×8, у каждого «published by the PUT (served version = the update) — no publish call, no deactivate window», «read-back: ok», failed 0 |
| `update-clones` | клоны 2 (боты стенда) — up to date, would_update 0 (шаблоны не в пакете; на проде клонов 0) |
| Read-back | `verify-deployed`: канон 29, **diff 0**, exit 0; повторный dry-run — 27 up to date + Set Workflow State up to date |
| Smoke | GET VerifyIdentity / RevenueCatEvent — «Did you mean…» (зарегистрированы); POST VerifyIdentity без токена 401, поддельный — 401 `bad_signature`; WappiProxy без токена 401; AuthSession nonsense 400; SetWorkflowState без токена на `crChannelWatch001` / `crVerifyIdent001` → 400 `protected_workflow`; DeleteBotFiles с поддельным токеном 401 |
| Первый тик Channel Watch | 07:50Z: прошёл (`profile/all/get` api + tapi), `message/send` 0, ошибок нет; живых строк `bot_profiles` на стенде 0 → снимок пуст (первые наблюдения и уведомления — в e2e `channel_*`) |
| Insights | с момента старта сервера (bootstrap) ни одной строки «Error while saving insights…»: все временные workflow удалялись ≥ 35 с после выполнения |
| e2e на развёрнутом стенде | `derive-staging.py` (без импорта; workflows-staging = канон пакета) → **e2e-auth 63/63** (вкл. модельный шаг `retired_bot_reconnects_after_purchase` [wa]/[tg]: «Букет «Нежность» — 18 500 ₸ … к 18:00»; `sweep_removes_booked_clone`, `rc_*`, `identity_heal_*` ×3, `orphan_sweep_spares_live_profile`, `channel_*` ×5), **e2e-handoff 21/21** (вкл. `notifier_owner_number_by_v2_rule`, `bot_knows_owner_agreement_after_auto_resumes` [llm]), **e2e-reply 44/44** |
| После e2e | `verify-deployed` diff 0, exit 0; 0 строк «Error while saving insights…» с момента старта сервера до конца e2e (две строки ошибок в логе — нарочно пустой файл сценария загрузки); стенд остановлен: `bootstrap.py --stop`, мок, `pg.sh stop` |

## Остатки и device-pass (несёт сценарий B3)

**Принятые ограничения v1 (решения 2026-10-15):**

- **Один аккаунт магазина = одно устройство (A3).** Heal переносит подписку на НОВЕЙШУЮ установку; боты старой (второй живой
  телефон на том же Apple ID / Google-аккаунте или мёртвая установка до переустановки) замолкают сразу и убираются Sweep'ом через
  3 дня; обратно heal не двигает. Рычаг поддержки — отзыв установки (`a1-deploy-runbook.md` §5), журнал — `identity_heal_log`.
  Текст ограничения — CLAUDE.md, «Part A», «v1 LIMITATION».
- **Channel Watch (A4):** попытки тратятся и без маршрута (шесть прогонов без другого канала исчерпывают лимит — владелец узнаёт
  по метке в приложении); одно ложное уведомление после намеренного «Сменить номер», не привязанного до тика, или сбоя
  авторизации Wappi; массовый DOWN сверх порога пропускается молча (успешные выполнения не сохраняются — наблюдаемость
  отложена, решение финального ревью).
- **Orphan sweep (A4):** однажды авторизованный, разлогиненный профиль WhatsApp, у которого в списке Wappi нет НИ `authorized_at`,
  НИ `logouted_at`, остаётся удаляемым через 24 ч (образцы tapi хранят `logouted_at`; проверить на устройстве, только чтение).
- **A2:** `active`-алиас с истёкшим периодом всё равно переносится (зеркало авторитетно до следующего события RevenueCat).
- **A6:** в окне до ответа магазина (`EntitlementsKnown = false`) заголовок может на секунды показать «Истекла»/«заморожен» над
  пробными счётчиками при серверном expired — само проходит на `OnEntitlementChanged`.

**Device-pass (в сценарий B3, новая сборка):**

1. Карточка «Подписка» без тарифа с купленным резервом: виден блок с одной строкой «Резерв: N диалогов — заморожен до
   возобновления подписки» (строка переносится на 2 строки при 30u — решение владельца по формулировке); покупка тарифа
   возвращает счётчики, формулировка — «спишутся после квоты».
2. Реальный разлогиненный профиль WhatsApp: есть ли `authorized_at` / `logouted_at` в `profile/all/get` (только чтение).
3. «Канал отключён» → переподключить в настройках на тарифе с ОДНИМ каналом → привязка проходит (Count Channels исключает
   строки того же `bot_key` на этом канале), данные бота на месте (линия F1), метка снята.
4. Переустановка приложения при живой подписке → один вызов `/webhook/VerifyIdentity` → подписка на новом id; строка в
   `identity_heal_log`; повторный запуск в течение 24 ч — без вызова (защёлка `CrIdentityHeal.*`).
5. Наблюдения на проде по отдельному «да» (шаг 14 чек-листа): уведомление «канал отключился» в «Избранное» Telegram и
   уведомление о заявке Telegram-бота на номер `account.phone`.

**Не проверено (остатки репетиции):** реальный RevenueCat API v2 из n8n прода (на стенде — мок; ключ проверен A3 локально,
только чтение); права учётки «RevenueCat API» на проде проверятся первым настоящим heal; живой бот на проде (их 0) —
наблюдения A4/A5 ждут новой сборки.
