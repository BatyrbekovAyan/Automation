# Пакет выкладки, часть B «расписание бота» (B1 + клиент B2)

**Статус (2026-10-19):** всё в каноне на `main` (часть B — сервер `7345bd95`, `60bec485`, исправления ревью `d34a3f81`
(+ решение ревью B1 о «Сводке» `38e1bac7`, документы); клиент `2eb39797`, `40816971`, `03751265`, `1b549b9e`; канон пакета =
`Tools/n8n/workflows` + `Tools/n8n/sql` на `d34a3f81` — клиентские коммиты и этот пакет канон не меняют), **выложено на прод
2026-10-19** (по «да» владельца; журнал — `docs/audits/implementation-progress.md`, «Развёртывание 2026-10-19 — часть B
«расписание бота»»; снимки «до»/«после» — `prod-before-deploy.json` / `prod-after.json`, точки отката — `rollback-pointers.txt`
рядом с этим файлом; чек-лист выполнен без отступлений — «Уроки выкладки 2026-10-19»). Выкладка — **только по явному «да»
владельца**; исполняет контроллер по чек-листу ниже. Пакет отрепетирован на
staging из прод-состояния 2026-10-17 (раздел «Репетиция», вывод каждого шага — `rehearsal.txt` рядом). Клиентская половина
(секция «Расписание работы» в настройках бота, синк, панель вне часов у «Авто»-бота, пометка «Вне графика ·» в «Сводке») едет в
новой сборке вместе с клиентом части A; сценарий device-pass обеих частей — `device-pass.md` рядом. Финальное ревью блока B —
у контроллера, после этого пакета; push — по согласию владельца.

План — `docs/superpowers/plans/2026-10-14-paying-users-protection-and-schedule.md` (B1–B3), спека —
`docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md` (§4 B1, §1). Журнал —
`docs/audits/implementation-progress.md` («Часть B «расписание бота» — реализовано, пакет отрепетирован, ждёт «да»»).
Архитектура — CLAUDE.md, External APIs → «Part B «расписание бота»» и `Main/` → «Bot schedule».

## Прод сейчас (снимок «до», только чтение)

`python3 Tools/n8n/verify-deployed.py --base https://n8n.choosereply.com --json docs/audits/2026-10-17-bot-schedule/prod-before.json`
(2026-10-17 01:14Z): канон 30, **diff 8 = ровно пакет** — DIFF ×7 + MISSING ×1 (таблица ниже); живых клонов 0, архивный «13»,
other 0. Прод = канон `3321a788` (выкладка части A 2026-10-15): графы 29 канонических файлов на `3321a788` совпадают со снимком
29/29 (сохранённая и опубликованная версии) — офлайн-сверка `rehearsal.txt` §A. Если в день выкладки снимок покажет другое —
**стоп и разбор**.

## Что меняется на сервере

Графы — `verify-deployed.graph_hash` («до» = прод-снимок 2026-10-17 = канон `3321a788`, «после» = канон этого пакета; на
staging те же workflow дают другие хэши — производный канон, адреса и учётки переписаны, см. `rehearsal.txt` R11).

| Workflow | Id | Что | Генератор / владелец | Граф до → после |
|---|---|---|---|---|
| Set Workflow State | `0DP79STI3pafXO9B` | защищённый список +`crSetSchedule001` | `build-set-schedule.py` (патч списка); выкладка — шаг 2 | `36b6a73e9773` → `dcce761854bd` |
| Create WA | `XuvOp7TxOImOAmlj` | +1 id в защищённых списках; `Bind Workflow Id` переносит `bot_schedule` по линии бота (`moved_schedules`) | `apply-clone-lifecycle.py` (SQL — `clone_lineage.py`) | `78ed47e160c0` → `b4a695f6aecf` |
| Create TG | `Uz6HBBUpAiUqVysB` | то же | `apply-clone-lifecycle.py` | `51becea5ebee` → `76df037a5cb0` |
| Delete Bot Files | `lmjYsdNcQA2IE5rl` | +1 id; `Delete Chat Memory` удаляет расписание линии (CTE `schedule`, счётчик `deletedSchedules`), его читают `Deletion Settled?` и `Respond` | `apply-auth-gate.py` + `apply-history-handoff.py` | `64f4c6dfda90` → `ab64a6555282` |
| Profile Lifecycle Sweep | `fXYpCXPKw92EzRz8` | только +1 id в защищённых списках | `apply-sweep-clone-deactivate.py` | `624c8bd7733f` → `35be6f37abeb` |
| WhatsApp Bot (шаблон) | `4wYitz5ek30SVNlT` | `Read Reply Mode`: «окно закрыто» — ещё одно ИЛИ в `suppressed` + колонка `off_hours` (fail-closed, без onError); `Record Suppressed` передаёт `off_hours`; `Outcome` ставит `off_hours` только на ЗАПИСАННОМ подавлении | `apply-history-handoff.py` + `apply-outcome-stamps.py` (предикат — `bot_schedule.py`) | `013bce231127` → `60aa43640382` |
| Telegram Bot (шаблон) | `4VN3gsFaC2HUYmcc` | то же | то же | `f22a7b12c69f` → `2433180b00c9` |
| **Set Schedule (новый)** | `crSetSchedule001` | `POST /webhook/SetSchedule` — расписание владельца: только по токену, один апсерт с охраной версии | `build-set-schedule.py` | — → `cfd76ff12bb7` |

- `deploy-canonical.py` новые workflow не создаёт — `crSetSchedule001` импортируется CLI (`n8n import:workflow`, id сохраняется).
- Шаблоны неактивны по контракту: `deploy-canonical` их сохраняет без публикации («read-back: ok», без строки «published»);
  живые клоны получают новый граф через `update-clones.py` (на проде клонов 0, архивный «13» пропускается).
- Горячий путь бота: новых Postgres-узлов нет — расписание читается в том же операторе `Read Reply Mode` (один подзапрос
  дважды: ИЛИ-терм `suppressed` и колонка `off_hours`); lease (`Acquire Send Lease`) его не читает.
- Env не меняется. **Новых учёток нет:** у `crSetSchedule001` одна учётка — Postgres `vvRrFiEXzLVqKjOx`, и на проде она есть
  (проверено 2026-10-17 `GET /credentials`, только чтение: `vvRrFiEXzLVqKjOx | postgres | Postgres`; всего 10 учёток). В отличие
  от Channel Watch части A, перепривязка id не нужна — но аудит учёток копии и GET до включения выполняются всё равно (правило
  a1-deploy-runbook §2.4, шаг 3).

### Миграция (аддитивная, идемпотентная)

| # | Файл | Зачем | Когда |
|---|---|---|---|
| MB | `Tools/n8n/sql/2026-10-14-bot-schedule.sql` | таблица `bot_schedule` (8 колонок) + функция и триггер `bot_schedule_tz_check` (BEFORE INSERT OR UPDATE OF tz: зона не из `pg_timezone_names` → `bot_schedule_bad_timezone`) + RLS без политик + revoke у `anon`/`authenticated` | **ПЕРВОЙ, до любого workflow**: `Read Reply Mode` шаблонов fail-closed — без таблицы падает каждое входящее каждого бота; бинд Create и Delete Bot Files называют таблицу; Set Schedule в неё пишет |

Проверки — файлы рядом (все только чтение; на проде через `.secrets/sql-oneoff.py`):
`precheck.sql` → `{"no_schedule":true,"no_tz_fn":true}`; `tzprobe.sql` →
`{"almaty_offset":"05:00:00","almaty_known":true,"moscow_offset":"03:00:00"}`; `postcheck.sql` →
`{"schedule_cols":"8","rls":true,"client_grants":"0","tz_trigger":true,"triggers":"1","tz_fn":true,"rows":"0"}`.
Миграция через Postgres-узел n8n (путь `sql-oneoff.py`) отрепетирована: тело функции в `$fn$…$fn$` проходит, повтор — тот же
`{"success":true}` и та же пост-проверка (`rehearsal.txt` R5).

**Проба tzdata (ревью B1).** Сервер решает «внутри окна или нет» по часам Postgres в зоне бота (`now() at time zone s.tz`) — то
есть по базе часовых поясов, с которой собран Postgres прода (Supabase). Казахстан перешёл на единый UTC+5 1 марта 2024 (tzdata
2024a): в базе старше Asia/Almaty = +06 — окна всех ботов в Asia/Almaty сдвинулись бы на час, а триггер отказывал бы зонам, которых
эта база не знает. Поэтому `tzprobe.sql` выполняется ДО миграции: `almaty_offset` ≠ `05:00:00` или `almaty_known` ≠ `true` — **СТОП**
(на проде ещё ничего не изменено). `moscow_offset` — справочно (Europe/Moscow, UTC+3, для device-pass).

## Чек-лист выкладки (прод, только после «да» владельца)

**Стоп-правило (для каждого шага).** Если вывод шага отличается от «Ожидание», записанного здесь, — **СТОП**: следующий шаг не
выполнять, на проде ничего не чинить «на ходу». Сохранить вывод; если живое состояние вредно — откат ТОЛЬКО затронутого шага
(раздел «Откат»); доложить владельцу.

**Где выполнять.** Команды — из корня рабочей копии, в которой канон = пакет. `<date>` = дата выкладки. Во всех вызовах база
указана явно: инструменты по умолчанию смотрят на прод. Секреты — только из основной копии (`.secrets/` не в git):
```bash
MAIN=/Users/ayan/Projects/Automation; SEC="$MAIN/Tools/n8n/.secrets"; B=https://n8n.choosereply.com; PKG="$MAIN/docs/audits/2026-10-17-bot-schedule"
export N8N_API_KEY="$(cat "$SEC/prod-api-key.txt")"     # verify-deployed / deploy-canonical / update-clones читают ключ отсюда; не печатать
git -C "$MAIN" diff --stat d34a3f81 HEAD -- Tools/n8n/workflows Tools/n8n/sql   # Ожидание: пусто (канон = пакет)
```
Если вывод **НЕ пуст** (на `main` легла другая правка канона) — выполнять весь чек-лист из отдельного worktree на коммите канона
пакета `d34a3f81`; файлы пакета (`precheck.sql`, `tzprobe.sql`, `postcheck.sql`) читать по `$PKG` из основной копии, снимки,
точки отката и журнал писать туда же и коммитить в основной копии; текст чек-листа читать с `main`:
```bash
git -C "$MAIN" worktree add "$MAIN-pkgB" d34a3f81 && cd "$MAIN-pkgB"
git diff --stat d34a3f81 HEAD -- Tools/n8n/workflows Tools/n8n/sql             # Ожидание: пусто
# … шаги 0–11 отсюда …
cd "$MAIN" && git worktree remove "$MAIN-pkgB"                                  # после шага 11
```

0. **Предполётное (только чтение).**
   - Снимок «до»: `python3 Tools/n8n/verify-deployed.py --base $B --json "$PKG/prod-before-deploy.json"` → Ожидание: канон 30,
     diff 8 — ровно ids и хэши «до» из таблицы (у активных сохранённая = опубликованная; у двух шаблонов `published=None` —
     неактивны); клонов 0, архивный «13», other 0. Если новая сборка уже создала бота — его клон `BEHIND` (три узла:
     Read Reply Mode, Record Suppressed, Outcome): допустимо, его обновит шаг 7 — записать их число. Любое другое расхождение — СТОП.
   - Бэкап (утренний ночной архив того же дня не затирать — как 10-13/10-15):
     `ssh choosereply 'cd ~/backups && D=$(date +%F) && { [ ! -f n8n-$D.tar.gz ] || mv n8n-$D.tar.gz n8n-$D-0315.tar.gz; } && ~/choosereply/backup.sh && mv n8n-$D.tar.gz n8n-$D-predeploy.tar.gz && ls -la ~/backups | tail -4'`.
   - Dry-run + точки отката: `python3 Tools/n8n/deploy-canonical.py --base $B` → Ожидание: `would_update 6` (`XuvOp7TxOImOAmlj`,
     `Uz6HBBUpAiUqVysB`, `lmjYsdNcQA2IE5rl`, `fXYpCXPKw92EzRz8`, `4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`), `missing 1`
     (`crSetSchedule001`), `up_to_date 21`, `failed 0`; и `python3 Tools/n8n/deploy-canonical.py --base $B --only 0DP79STI3pafXO9B
     --include-placeholders` → `would_update 1`. Все строки `rollback pointer` → `$PKG/rollback-pointers.txt` (у двух шаблонов
     `activeVersionId None` — их откатывают ТОЛЬКО каноном, раздел «Откат»).
   - Учётка нового workflow (только чтение) — Ожидание: `200 [('vvRrFiEXzLVqKjOx', 'postgres', 'Postgres')]`:
     ```bash
     python3 - <<'PY'
     import importlib.util
     s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
     st, d = uc.call("https://n8n.choosereply.com", "GET", "/credentials?limit=100")
     print(st, [(c.get("id"), c.get("type"), c.get("name")) for c in (d or {}).get("data", []) if c.get("type") == "postgres"])
     PY
     ```
   - Засечь начало выкладки для наблюдения (шаг 10): `export DEPLOY_START="$(date -u +%Y-%m-%dT%H:%M:%SZ)"`.
1. **Пред-проверка, проба tzdata, миграция, пост-проверка** — по одной, каждая через Postgres-узел n8n (`sql-oneoff.py` ждёт
   35 с перед удалением временного workflow — правило insights):
   ```bash
   python3 "$SEC/sql-oneoff.py" "$(cat "$PKG/precheck.sql")"                        # Ожидание: 200 {"no_schedule":true,"no_tz_fn":true}
   python3 "$SEC/sql-oneoff.py" "$(cat "$PKG/tzprobe.sql")"                         # Ожидание: 200 {"almaty_offset":"05:00:00","almaty_known":true,"moscow_offset":"03:00:00"}
   python3 "$SEC/sql-oneoff.py" "$(cat Tools/n8n/sql/2026-10-14-bot-schedule.sql)"  # Ожидание: 200 {"success":true}
   python3 "$SEC/sql-oneoff.py" "$(cat "$PKG/postcheck.sql")"
   # Ожидание: 200 {"schedule_cols":"8","rls":true,"client_grants":"0","tz_trigger":true,"triggers":"1","tz_fn":true,"rows":"0"}
   ```
   Пред-проверка `false` (таблица уже есть) — СТОП и разбор (кто её создал?). Проба tzdata не `05:00:00` / не `true` — СТОП до
   миграции (см. «Проба tzdata»).
2. **Set Workflow State — защита нового id ДО его импорта и включения** (до этого шага legacy-вызов без токена — compat-окно
   открыто до 2026-12-31 — мог бы выключить или удалить новый workflow). Путь части A (отрепетирован дважды):
   `python3 Tools/n8n/deploy-canonical.py --base $B --only 0DP79STI3pafXO9B --include-placeholders --apply --yes` —
   `__HDR_CRED__` привязывается к учётке `n8n Admin API` живого Set Workflow State. Ожидание: «published by the PUT (served
   version = the update) — no publish call, no deactivate window», «read-back: ok», `updated 1`, `failed 0`.
   **ТОЛЬКО запасной путь** (если первый отказал): `python3 Tools/n8n/build-client-webhooks.py --update` — на staging не
   репетируется; заново ВКЛЮЧАЕТ два живых вебхука (Support Message и Set Workflow State); запускать ТОЛЬКО из основной копии
   `$MAIN` — без `.secrets/client-webhooks-state.json` он СОЗДАСТ на проде новые учётки и workflow; затем `--export` и
   `git diff --stat Tools/n8n/workflows/` — Ожидание: пусто.
3. **Импорт `crSetSchedule001` (неактивным) из ВРЕМЕННОЙ копии** — правило a1-deploy-runbook §2.4 (урок выкладки части A:
   `import:workflow` перепривязывает ненайденный id учётки только в своём проекте; здесь id канона — прод-id, но копия и GET
   всё равно проверяются до включения):
   ```bash
   # 3a. временная копия + аудит (канон не трогается): учётки РОВНО {postgres vvRrFiEXzLVqKjOx Postgres}, граф = канон, неактивен
   IMP=<scratchpad>/import-b; mkdir -p "$IMP"; IMP="$IMP" SRC=Tools/n8n/workflows PG_ID=vvRrFiEXzLVqKjOx PG_NAME=Postgres python3 - <<'PY'
   import importlib.util, json, os, sys
   s = importlib.util.spec_from_file_location("vd", "Tools/n8n/verify-deployed.py"); vd = importlib.util.module_from_spec(s); s.loader.exec_module(vd)
   f = "crSetSchedule001-Set_Schedule.json"
   canon = json.load(open(f"Tools/n8n/workflows/{f}")); w = json.load(open(f"{os.environ['SRC']}/{f}"))
   want = {("postgres", os.environ["PG_ID"], os.environ["PG_NAME"])}
   refs = {(t, r.get("id"), r.get("name")) for n in w["nodes"] for t, r in (n.get("credentials") or {}).items()}
   good = refs == want and vd.graph_hash(w) == vd.graph_hash(canon) == "cfd76ff12bb7" and w.get("active") is False and w.get("id") == "crSetSchedule001"
   open(f"{os.environ['IMP']}/{f}", "w").write(json.dumps(w, ensure_ascii=False, indent=2))
   print(f, sorted(refs), "graph", vd.graph_hash(w), "id", w.get("id"), "active", w.get("active"), "OK" if good else "FAIL — do NOT import")
   sys.exit(0 if good else 1)
   PY
   # 3b. импорт ИЗ КОПИИ (id сохраняется), копии на VPS и в контейнере удаляются
   f=crSetSchedule001-Set_Schedule
   scp -q "$IMP/$f.json" "choosereply:/tmp/$f.json"
   ssh choosereply "cd ~/choosereply; docker compose cp /tmp/$f.json n8n:/tmp/$f.json \
     && docker compose exec -T -u root n8n chown node:node /tmp/$f.json \
     && docker compose exec -T n8n n8n import:workflow --input=/tmp/$f.json; echo \"import rc=\$?\"; \
     docker compose exec -T -u root n8n rm -f /tmp/$f.json; rm -f /tmp/$f.json"
   ```
   Ожидание: аудит 3a — `crSetSchedule001-Set_Schedule.json [('postgres', 'vvRrFiEXzLVqKjOx', 'Postgres')] graph cfd76ff12bb7 id
   crSetSchedule001 active False OK` (ровно этот вывод дал тот же код офлайн с прод-значениями — `rehearsal.txt` R7b′), иначе —
   СТОП до импорта; импорт — «Successfully imported 1 workflow.», `import rc=0` (строка CLI «Error tracking disabled because this
   release is older than 6 weeks.» — служебная, не ошибка). Форма команд 3b — та, что выполнена на проде 2026-10-15 (часть A).
   **3c. GET до включения** (только чтение) — Ожидание: `crSetSchedule001 200 active False ['vvRrFiEXzLVqKjOx'] OK`; любой другой id —
   НЕ включать, СТОП:
   ```bash
   python3 - <<'PY'
   import importlib.util
   s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
   st, w = uc.call("https://n8n.choosereply.com", "GET", "/workflows/crSetSchedule001")
   ids = {r.get("id") for n in w.get("nodes", []) for r in (n.get("credentials") or {}).values()}
   print("crSetSchedule001", st, "active", w.get("active"), sorted(ids), "OK" if st == 200 and w.get("active") is False and ids == {"vvRrFiEXzLVqKjOx"} else "FAIL — do NOT activate")
   PY
   ```
4. **Включение через API** (как новые workflow 09-24 и 10-15) + read-back — Ожидание: `crSetSchedule001 activate 200 | active
   True | published=stored True | creds ['vvRrFiEXzLVqKjOx']`:
   ```bash
   python3 - <<'PY'
   import importlib.util
   s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
   B = "https://n8n.choosereply.com"
   st, _ = uc.call(B, "POST", "/workflows/crSetSchedule001/activate", {})
   st2, w = uc.call(B, "GET", "/workflows/crSetSchedule001")
   creds = sorted({r.get("id") for n in w.get("nodes", []) for r in (n.get("credentials") or {}).values()})
   print("crSetSchedule001 activate", st, "| active", w.get("active"), "| published=stored", w.get("activeVersionId") == w.get("versionId"), "| creds", creds)
   PY
   ```
5. **Smoke Set Schedule** — проверки 9.1 (строка SetSchedule) и 9.2 из шага 9 (Ожидание — там же).
6. **deploy-canonical по одному** (`python3 Tools/n8n/deploy-canonical.py --base $B --only <id> --apply --yes`):
   `XuvOp7TxOImOAmlj` Create WA → `Uz6HBBUpAiUqVysB` Create TG → `lmjYsdNcQA2IE5rl` Delete Bot Files → `fXYpCXPKw92EzRz8` Profile
   Lifecycle Sweep → `4wYitz5ek30SVNlT` WhatsApp Bot (шаблон) → `4VN3gsFaC2HUYmcc` Telegram Bot (шаблон). Ожидание у четырёх
   активных: «published by the PUT (served version = the update) — no publish call, no deactivate window», «read-back: ok»,
   `{"updated": 1, …, "failed": 0}` (Create/Sweep — инструмент привязывает `__HDR_CRED__` к `n8n Admin API` и отказывает
   непривязанным); у двух шаблонов (`active=False`) — «read-back: ok» без строки «published» (неактивны по контракту), `updated 1`,
   `failed 0`.
7. **`python3 Tools/n8n/update-clones.py --base $B`** (dry-run) → Ожидание: клонов 0 (архивный «13» пропущен) — применять нечего.
   Если шаг 0 записал живые клоны (боты новой сборки): у каждого `~ <id> … change: ['Outcome', 'Read Reply Mode', 'Record
   Suppressed']`, промпт без изменений → `python3 Tools/n8n/update-clones.py --base $B --apply --yes` (у каждого «rollback pointer …
   before the update» → дописать в `$PKG/rollback-pointers.txt`, «published by the PUT … read-back: ok») → dry-run ещё раз → все
   «up to date». (На staging отрепетировано на двух клонах — `rehearsal.txt` R10.)
8. **Read-back:** `python3 Tools/n8n/verify-deployed.py --base $B --json "$PKG/prod-after.json"` → Ожидание: exit 0, канон 30,
   diff 0, хэши «после» = таблица; клоны (если есть) `AT-CANONICAL`.
9. **Smoke — пять проверок без изменения данных** (9.1 — четыре GET). Ни одна не может ничего записать даже при нарушенном
   предусловии: тела без id (у Set Schedule без `profileIds` Validate отказывает раньше любой записи), поддельный токен
   отбивается гейтом, проба Set Workflow State просит `activate` уже ВКЛЮЧЁННОГО защищённого workflow (худшее при сломанной
   защите — повторная регистрация его вебхука на миллисекунды; `deactivate`/`delete` в smoke не применять никогда).
   ```bash
   F='v1.eyJpIjoieCJ9.eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHg'      # поддельный токен (подпись не та)
   T="$(mktemp)"; for p in SetSchedule CreateWhatsappWorkflow CreateTelegramWorkflow DeleteBotFiles; do                   # 9.1
     printf '%s: ' "$p"; curl -s -o "$T" -w '[%{http_code}] ' "$B/webhook/$p"; grep -q 'Did you mean to make a POST request?' "$T" && echo 'Did you mean to make a POST request?' || echo '(no hint)'; done; rm -f "$T"
   curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' -d '{}' "$B/webhook/SetSchedule"                 # 9.2
   curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' -H "Authorization: Bearer $F" \
     -d '{}' "$B/webhook/SetSchedule"                                                                                         # 9.3
   curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' \
     -d '{"workflowId":"crSetSchedule001","action":"activate"}' "$B/webhook/SetWorkflowState"                                # 9.4
   curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' -H "Authorization: Bearer $F" \
     -d '{}' "$B/webhook/DeleteBotFiles"                                                                                      # 9.5
   ```
   Ожидание:
   - 9.1: по строке на вебхук `<имя>: [404] Did you mean to make a POST request?` (зарегистрирован; GET исполнения не запускает);
   - 9.2: `{"success":false,"error":"unauthorized","reason":"unauthorized:missing"} [401]` (гейт только по токену — compat-окно его не открывает);
   - 9.3: `{"success":false,"error":"unauthorized","reason":"unauthorized:bad_signature"} [401]`;
   - 9.4: `{"success":false,"error":"protected_workflow"} [400]` (legacy-вызов без токена → Validate → защищённый id; после закрытия
     compat-окна `CR_AUTH_COMPAT_UNTIL` — `[401]` до Validate);
   - 9.5: `{"success":false,"error":"unauthorized","reason":"unauthorized:bad_signature"} [401]`.
10. **Наблюдение.** Успешные выполнения Set Schedule НЕ сохраняются (`saveDataSuccessExecution: none`) — смотрим ошибки (сохраняются
    все) и логи; шаблоны на проде не исполняются, пока нет клона (первое живое исполнение `Read Reply Mode` с расписанием — первый
    бот новой сборки, `device-pass.md` §B):
    ```bash
    python3 - <<'PY'
    import importlib.util, os
    s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
    B, SINCE = "https://n8n.choosereply.com", os.environ["DEPLOY_START"]
    for wid in ("crSetSchedule001", "0DP79STI3pafXO9B", "XuvOp7TxOImOAmlj", "Uz6HBBUpAiUqVysB", "lmjYsdNcQA2IE5rl", "fXYpCXPKw92EzRz8"):
        st, d = uc.call(B, "GET", f"/executions?workflowId={wid}&status=error&limit=50")
        errs = [(e.get("id"), e.get("startedAt")) for e in (d or {}).get("data", []) if (e.get("startedAt") or "") >= SINCE]
        print(wid, "GET", st, "| error executions since", SINCE, ":", errs)
    st, d = uc.call(B, "GET", "/executions?status=error&limit=100")
    print("whole instance: error executions since", SINCE, ":", [(e.get("workflowId"), e.get("id"), e.get("startedAt")) for e in (d or {}).get("data", []) if (e.get("startedAt") or "") >= SINCE])
    PY
    ssh choosereply 'cd ~/choosereply && docker compose logs --since 15m n8n | grep -iE "error|insights" | tail -20'
    ```
    Ожидание: `GET 200 | error executions since … : []` ×6 и `whole instance … : []`; логи 15 мин — пусто (временные workflow
    `sql-oneoff` ждут 35 с перед удалением — цикл insights не возникает). Повторить запрос после ближайшего тика Profile Lifecycle
    Sweep (каждые 6 ч) и на следующее утро — снова `[]`. Иначе — СТОП: прочитать ошибку
    (`uc.call(B, "GET", "/executions/<id>?includeData=true")`), до разбора ничего не менять.
    **Открытое из части A — в тот же день, не блокирует B:** запрос шага 11 пакета A (RevenueCat Events, Verify Identity, Channel
    Watch — ошибки с её `DEPLOY_START` 2026-10-15T13:36:52Z) и 12b (первое живое событие RevenueCat — журнал доставок RevenueCat,
    придёт с новой сборкой).
11. **Журнал и флаги «DEPLOYED».** `$MAIN/docs/audits/implementation-progress.md` — новый раздел «Развёртывание <date> — часть B
    «расписание бота»» (таблица шагов, снимки до/после, точки отката, smoke; запись «… ждёт «да»» остаётся историей). В основной
    копии `$MAIN`: `grep -n -E "расписание бота.*canon, (not|NOT) deployed" CLAUDE.md Tools/n8n/README.md` — ровно 5 строк (номера —
    на коммите пакета; если что-то сдвинуло строки — по выводу grep):
    - CLAUDE.md — пункт `Main/` «Bot schedule «Расписание работы» (… — client, ships with the new build; its server half
      `crSetSchedule001` — canon, NOT deployed — prod only on the owner's «да»; …)» → «… its server half `crSetSchedule001` —
      DEPLOYED to prod <date>; …»;
    - CLAUDE.md — пункт `Dashboard/`, фраза «… the server half canon, NOT deployed — prod only on the owner's «да»» → «… the server
      half DEPLOYED to prod <date>»;
    - CLAUDE.md — заголовок блока External APIs «Part B «расписание бота» … — canon, NOT deployed — prod only on the owner's «да»:» →
      «— DEPLOYED to prod <date>:» (+ ссылки на журнал, снимки `prod-before-deploy.json` / `prod-after.json` и точки отката);
    - `Tools/n8n/README.md` — пункт Layout «Part B «расписание бота» (… — canon, NOT deployed; prod only on the owner's «да»)» и
      строка таблицы `crSetSchedule001` «(… 2026-10-17 — canon, NOT deployed; …)» → «DEPLOYED to prod <date>»;
    - и строка «Статус» этого README («на проде НЕ выложено» → «выложено <date>»). Отметки «client, new build» остаются до сборки.
    Коммит `deploy(prod): …` (точечные пути).
12. **Наблюдения владельца — по отдельному «да», на новой сборке:** `device-pass.md` §B (окно, исключающее «сейчас» → бот молчит,
    панель доступна, счётчик не растёт, в «Сводке» «Вне графика ·»; окно вернуть → отвечает) + исполнитель смотрит строку
    `bot_schedule` этого профиля (только чтение, `sql-oneoff.py`: `select profile_id, enabled, days_mask, start_min, end_min, tz,
    version from public.bot_schedule where profile_id = '<profile>';`); метку `off_hours` видно в выполнении клона (узел `Outcome`)
    — шаблоны сохранение успешных выполнений не переопределяют, действует настройка инстанса.

`unset N8N_API_KEY DEPLOY_START` по окончании.

## Откат

- **Обновлённые АКТИВНЫЕ workflow** (Set Workflow State, Create WA/TG, Delete Bot Files, Profile Lifecycle Sweep) — по точке
  отката: `POST /api/v1/workflows/<id>/activate {"versionId": "<pointer>"}` снова обслуживает прежнюю версию (путь D1, без окна
  деактивации; сохранённая версия остаётся новой — verify-deployed покажет DIFF, пока канон не возвращён), либо канон `3321a788`:
  `git show 3321a788:Tools/n8n/workflows/<файл> > Tools/n8n/workflows/<файл>` → `deploy-canonical.py --base $B --only <id> --apply
  --yes` (для Set Workflow State + `--include-placeholders`) → вернуть канон (`git checkout -- Tools/n8n/workflows/<файл>`).
- **Шаблоны (НЕАКТИВНЫ) — только каноном**, никогда не `POST /activate` с их точкой отката (это ВКЛЮЧИЛО бы шаблон, а он должен
  оставаться выключенным): `git show 3321a788:Tools/n8n/workflows/<шаблон> > Tools/n8n/workflows/<шаблон>` → `deploy-canonical.py
  --base $B --only <id> --apply --yes` → если шаг 7 обновлял живые клоны — `update-clones.py --base $B --apply --yes`, пока в дереве
  старый шаблон (или точка отката каждого клона из вывода шага 7 — клоны активны, их путь D1 годится) → `git checkout --
  Tools/n8n/workflows/<шаблон>`.
- **Новый workflow** — `POST /workflows/crSetSchedule001/deactivate`, ≥ 35 с, затем `DELETE /workflows/crSetSchedule001` (правило
  insights). Приложение новой сборки на 404 Set Schedule повторяет желание с отступом (Transient) — безвредно, расписание просто не
  доходит до сервера.
- **Миграция — остаётся** (старый канон таблицу не читает). **Никогда не удалять `bot_schedule`, пока её читает хоть один
  выложенный workflow** (шаблоны / клоны — `Read Reply Mode` fail-closed: каждое входящее каждого бота упадёт; бинд Create и
  Delete Bot Files называют её). Обратный оператор — только после отката всех читателей: `drop table public.bot_schedule; drop
  function public.bot_schedule_tz_check();`.
- Частичный откат и его эффект: откат только шаблонов (и клонов) — расписание игнорируется, боты отвечают круглосуточно, строки
  остаются (безопасное направление); откат только Create — переподключение не переносит часы (новый профиль без строки → бот
  отвечает круглосуточно, пока владелец не сохранит расписание снова); откат только Delete Bot Files — строки удалённого бота
  остаются (сироты без поведения: его профили уже списаны).

## Что НЕ выкладывается

- **Клиент** (B2 — `ScheduleModel`, `BotScheduleStore`, `ScheduleCopy`, `ScheduleFallbackPolicy`, секция настроек и
  `BotScheduleBuilder`, `ScheduleSyncService`/`ScheduleSyncQueue`, панель вне часов, пометка «Вне графика ·»; вместе с клиентом
  части A) — новая сборка → `Tools/store/inspect-artifact.sh` → `device-pass.md`. Сервер безопасен для текущих клиентов: Set
  Schedule зовёт только новая сборка; без строки расписания шаблоны ведут себя ровно как раньше (отсутствие ⇒ ответ, LOCKED);
  ответ Delete Bot Files получил одно поле (`deletedSchedules`), клиент читает только булево `success:true` (C1).
- Env, учётки, промпты, Suggest Replies (панель вне часов — чисто клиентская:
  `SuggestionsController.ResolveSemiAutoForOpenChat`), прочие workflow, сайт.

## Остатки и принятые ограничения v1

- **Решение — только на входе сообщения.** `Acquire Send Lease` расписание не читает: окно, закрывшееся во время генерации, ответ
  не отменяет; пачка сообщений через закрытие остаётся без ответа, а утром Latest+Combine складывает ночные тексты в первый вход,
  пока память их уже хранит (принято, как «Вместе» → «Авто»).
- **«Вне графика ·» в «Сводке» — клиентское приближение** (решение по ревью B1, 2026-10-17): пометка считается по ТЕКУЩЕМУ
  сохранённому расписанию бота и `lastMessageAt` строки, а не по расписанию в момент сообщения; серверная метка `off_hours` живёт
  только в Outcome выполнения, Dashboard Outcomes её не читает. `lastMessageAt` — самая новая строка истории ЛЮБОГО автора
  (`Find Changed Sessions`: `max(h.created_at)` — клиент, бот или владелец), поэтому ночное сообщение, на которое владелец
  ответил в рабочие часы, пометки не получает.
- **Fail-closed.** Неизвестная Postgres зона в строке заглушила бы бота (`at time zone` бросает на каждом сообщении) — триггер
  отказывает такой зоне при записи, tzdata имён не удаляет; база tzdata прода проверяется пробой шага 1.
- **Зона на телефоне.** Id, который устройство не разрешает, → фиксированный UTC+5 (панель вне часов и пометка считаются по
  Алматы); старая ОС со старой tzdata читает Asia/Almaty как +06 — панель и пометка на час расходятся с сервером (сервер
  авторитетен: решает по tzdata Postgres). Проверка — `device-pass.md` §C.
- **Согласованность в конечном счёте.** Панель и пометка читают СОХРАНЁННОЕ расписание; сервер отвечает по прежнему, пока
  желание не подтверждено — обычно секунды. Недоставленное (сохранено без сети, отказ 400/401/403, исчерпаны 8 попыток запуска)
  секция показывает одной строкой под строкой ошибки: «Расписание ещё не дошло до сервера», пока желание в пути (только спустя
  2 с — обычное сохранение строку не показывает), «Не удалось сохранить расписание — проверьте интернет», когда не прошла ни
  одна попытка запуска, и «Сервер не принял расписание — повторим при следующем открытии настроек» при отказе сервера;
  после подтверждения строка скрыта. Такое желание перевзводится (попытка 0, сразу, с той же версией) при возврате в
  приложение (не чаще раза в минуту), при каждом открытии настроек бота и при следующем запуске.
- **≤ 10 id** — Validate обрезает до десяти, а не отказывает 11-му (правило Set Reply Mode; у бота два профиля — теоретически).
- **Поддержка: максимальная версия** (ревью B1, класс версии Set Reply Mode). Держатель действующего токена может записать
  `{enabled:true, daysMask:0}` с версией `Number.MAX_SAFE_INTEGER` — бот этого профиля молчит всегда, а приложение строку уже не
  перезапишет (любая его следующая версия ниже или вне диапазона → 400); снять её может только Delete Bot Files (удаление бота) или
  поддержка — `delete from public.bot_schedule where profile_id = '<profile>';` через `sql-oneoff.py` (процедура —
  `a1-deploy-runbook.md` §5).
- **Вопросы владельцу** (отложены ревью, в `device-pass.md` §D): `Purchases.SyncPurchases()` при первой установке на iOS;
  перенос строки «заморожен…» (A6). Вопрос о кнопке «Авто» в чате вне часов РЕШЁН 2026-10-17 (D1): когда панель «Вместе»
  открыта только из-за расписания или квоты (кнопка «Авто» не горит, хотя чат в «Авто»), нажатие «Авто» прячет панель на это
  открытие чата и ничего не записывает (раньше записывало липкое «Авто» для чата); повторное нажатие («Авто» гаснет)
  возвращает панель без записи только после такого нажатия — после записанного «Авто» (чат был «Вместе», вопрос
  подтверждён) оно записывает «Вместе»; панель вне окна появляется и уходит, пока чат открыт (новое сообщение или проверка
  раз в минуту).

## Репетиция на staging (2026-10-17)

Стенд: n8n 2.36.7 с task runners (как прод), Postgres `pg.sh` (5433), Wappi-мок 127.0.0.1:8765. Прод не трогался, кроме снимка
`verify-deployed` и GET-проб (учётки, текст 13.10 — только чтение). Вывод каждого шага — `rehearsal.txt` (рядом; сырые журналы —
`Tools/test-output/b3/`, gitignored). Миграция — через Postgres-узел n8n, staging-двойником `sql-oneoff.py` (тот же харнес:
временный вебхук → Postgres-узел → удаление через 35 с; отличаются только база, файл ключа и учётка «Postgres Staging»).

| Шаг | Результат |
|---|---|
| R1 стенд = канон прода | `bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001 --canon-ref 3321a788` → импорт 30 (канон 29 + staging-only eval runner) + 2 бота, опубликовано 31 |
| R2 стенд = прод | `crSetSchedule001`, оставшийся от прошлого bootstrap B1, — выключен → 36 с → удалён (GET 404); таблица и функция `bot_schedule` сброшены; пред-проверка `t \| t` |
| R3 сверка | `verify-deployed`: канон 30, **diff 8 — статус каждого из 30 id совпадает со снимком прода**; 2 клона стенда `BEHIND` (на проде клонов 0) |
| R4 dry-run | would_update **6** (`4VN3gsFaC2HUYmcc`, `4wYitz5ek30SVNlT`, `Uz6HBBUpAiUqVysB`, `XuvOp7TxOImOAmlj`, `fXYpCXPKw92EzRz8`, `lmjYsdNcQA2IE5rl`), missing **1** (`crSetSchedule001`), up_to_date 21, failed 0; `--include-placeholders --only 0DP79STI3pafXO9B` → would_update 1; у каждого `rollback pointer` (у шаблонов `activeVersionId None`) |
| R5 шаг 1 | через Postgres-узел n8n: пред-проверка `{"no_schedule":true,"no_tz_fn":true}`; проба tzdata `{"almaty_offset":"05:00:00","almaty_known":true,"moscow_offset":"03:00:00"}`; миграция `200 {"success":true}`; пост-проверка `{"schedule_cols":"8","rls":true,"client_grants":"0","tz_trigger":true,"triggers":"1","tz_fn":true,"rows":"0"}`; повтор миграции и пост-проверки — то же. psql: `CREATE TRIGGER bot_schedule_tz_check BEFORE INSERT OR UPDATE OF tz ON public.bot_schedule …`; откатываемая проба — `Asia/Almaty` вставляется, `Europe/Nowhere` → `bot_schedule_bad_timezone`, после отката 0 строк |
| R6 Set Workflow State | `--only 0DP79STI3pafXO9B --include-placeholders --apply --yes` → «published by the PUT», read-back ok, updated 1 |
| R7 импорт | `derive-staging.py` (без импорта) → аудит шага 3a со staging-значениями — `[('postgres', 'stgPostgres000001', 'Postgres Staging')] graph cfd76ff12bb7 … active False OK`; тот же код офлайн с прод-значениями — `[('postgres', 'vvRrFiEXzLVqKjOx', 'Postgres')] graph cfd76ff12bb7 … OK`; `n8n import:workflow` проверенной копии при работающем сервере → «Successfully imported 1 workflow.», rc 0; GET до включения — `active False`, учётки ровно `stgPostgres000001` |
| R8 включение + smoke | `activate 200 \| active True \| published=stored True`; GET SetSchedule → 404 «Did you mean to make a POST request?»; POST `{}` без токена → 401 `unauthorized:missing` |
| R9 шесть workflow | по одному в порядке шага 6: updated 1 ×6, failed 0; четыре активных — «published by the PUT (served version = the update) — no publish call, no deactivate window», «read-back: ok»; два шаблона — «read-back: ok» (неактивны, не публикуются) |
| R10 `update-clones` | dry-run: 2 клона, у каждого `change: ['Outcome', 'Read Reply Mode', 'Record Suppressed']`, промпт custom — сохранён; `--apply --yes` → updated 2, «published by the PUT … read-back: ok»; повторный dry-run → up_to_date 2 |
| R11 read-back | `verify-deployed`: канон 30, **diff 0**, exit 0; клоны `AT-CANONICAL`; повторный dry-run — 28 + Set Workflow State up to date |
| R12 smoke | 9.1–9.5 — все ответы как в «Ожидание»; блок шага 9 выполнен ДОСЛОВНО из этого README (R12b, извлечён скриптом, отступ списка снят, `B` = стенд) — тот же вывод; строк `bot_schedule` после smoke — 0 |
| R13 наблюдение | ошибочных выполнений с начала выкладки — 0 (шесть id и весь инстанс); с запуска сервера стенда — 0 строк «Error while saving insights…» и 0 строк error |
| e2e на развёрнутом стенде | `derive-staging.py` (без импорта; workflows-staging = канон пакета) → **e2e-reply 48/48** — первый прогон сценариев расписания после исправлений ревью B1: `off_hours_suppressed_recorded` [wa]/[tg] (окно вне «сейчас», outcome `off_hours`, mode `{suppressed: true, off_hours: true}`, история `client`, память `human`, отправок 0, Count Dialog / Claim Event / AI Agent не запускались), `in_hours_replies` [wa]/[tg] (outcome `sent`, одна отправка, диалог засчитан); **e2e-auth 67/67** (вкл. `set_schedule_applies_and_refuses`, `reconnect_moves_schedule` [wa]/[tg], модельный шаг `retired_bot_reconnects_after_purchase` [wa]/[tg], `bot_delete_wipes_lineage`, `sweep_removes_booked_clone`, `identity_heal_*`, `orphan_sweep_spares_live_profile`, `channel_*`); **e2e-handoff 21/21** (вкл. модельные). После e2e — `verify-deployed` diff 0, exit 0; 0 строк «Error while saving insights…»; две строки error в логе — нарочно пустой файл сценария загрузки e2e-auth (как в части A); строк `bot_schedule` не осталось |
| Гейты | генераторы `--check` 22/22 (+ `apply-delete-history.py` RETIRED — отказ по замыслу) + верификаторы 15/15 + `verify-telegram-parity --self-test` 33/33 + `verify_rag all` + `verify-panel-prompts.js` — 40/40 exit 0, канон не тронут; `verify-sql-comments` — 99 Postgres-узлов в 31 файле; `tests/run_all.py` — 40 файлов, 1436 случаев, 0 падений (44 мин; `test_reply_schedule` 43/0, `test_set_schedule` 37/0, `test_clone_lineage_sql` 56/0, `test_retired_delete` 14/0 — с новым негативом N2: подсчёт `deletedSchedules` по чужой CTE ловит только пин-регэксп верификатора, доказано на копии) |
| После | `bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` (без `--canon-ref`) → рабочий канон: импорт 31 + 2 бота, опубликовано 31 → `verify-deployed` канон 30, diff 0, **exit 0**; затем n8n и мок остановлены, Postgres оставлен запущенным (он работал до начала задачи); 0 строк «Error while saving insights…» за всю задачу |

## Уроки выкладки 2026-10-19

- Чек-лист выполнен дословно, без отступлений и без остановок (2026-10-19 09:03–09:12 UTC; `DEPLOY_START` 09:04:52Z): вывод
  каждого шага 0–10 совпал с «Ожидание». Рабочая копия — основная (`git diff --stat d34a3f81 HEAD -- Tools/n8n/workflows
  Tools/n8n/sql` пуст на HEAD `28058a8f`), отдельный worktree не понадобился.
- Снимок «до» дня выкладки совпал со снимком пакета 2026-10-17 по всем 30 id поле в поле (status, active, хэши сохранённой /
  опубликованной / канонической версии) — между 10-17 и 10-19 прод не менялся, клонов новой сборки нет.
- У строки MISSING `verify-deployed` не считает канонический хэш (`graph_hash_canonical: null`), поэтому хэш «после» нового
  workflow (`cfd76ff12bb7`) на шаге 0 по снимку не сверить — его доказывает аудит 3a (канон = копия = `cfd76ff12bb7`) и снимок
  «после» шага 8.
- Закрыты два остатка репетиции из «Не проверено»: tzdata Postgres прода — `Asia/Almaty` = `05:00:00`, зона известна
  (`Europe/Moscow` `03:00:00`); `docker compose`-путь импорта `crSetSchedule001` — «Successfully imported 1 workflow.», `import
  rc=0`, временные копии на VPS и в контейнере удалены (проверено `ls`). Третий (запись строки настоящим клиентом и молчание
  живого бота) по-прежнему ждёт новой сборки (`device-pass.md` §B).
- Лог n8n за окно выкладки — 6 строк, все штатные: 5 × «Received request for unknown webhook … Did you mean to make a POST
  request?» (наши GET шагов 5 и 9.1) и 1 × «Deregistered all crons» (PUT Profile Lifecycle Sweep переопубликовал его расписание);
  ни одной строки error / insights (четыре временных workflow `sql-oneoff` выдержали 35 с).
- Повтор запроса шага 10 после ближайшего тика Profile Lifecycle Sweep (прежние тики — 01:00 / 07:00 UTC, следующий ≈ 13:00 UTC)
  и на следующее утро — за контроллером.

**Не проверено (остатки репетиции):** запись строки `bot_schedule` настоящим клиентом и молчание живого бота на проде (ботов 0 —
ждёт новой сборки, `device-pass.md` §B); tzdata Postgres прода (проба шага 1 — первым же шагом выкладки, только чтение);
`docker compose`-путь импорта для этого workflow (та же форма выполнена на проде 2026-10-15 для двух workflow части A).
