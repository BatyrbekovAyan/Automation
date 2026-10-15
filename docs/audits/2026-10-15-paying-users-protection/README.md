# Пакет выкладки, часть A «защита платящих» (A1–A6)

**Статус (2026-10-15):** всё в каноне на `main` (части A — коммиты `927e1067..9caddb6f` + исправления финального ревью
`7aa6f091` (I-1), `2303cff7` (I-2, клиент), `9cd33a6c` (M-5); канон пакета = `Tools/n8n/workflows` + `Tools/n8n/sql` на `9cd33a6c`),
**выложено на прод 2026-10-15** (по «да» владельца; журнал — `docs/audits/implementation-progress.md`, «Развёртывание 2026-10-15 —
часть A «защита платящих»»; снимки «до»/«после» — `prod-before-deploy.json` / `prod-after.json`, точки отката — `rollback-pointers.txt`
рядом с этим файлом; при выкладке исправлен шаг 4 — перепривязка учётки Channel Watch, см. «Уроки выкладки 2026-10-15»).
Пакет отрепетирован на staging из прод-состояния (раздел «Репетиция»); финальное ревью после этого
поменяло два графа — RevenueCat Events (I-1) и Verify Identity (I-1 + M-5) — и миграцию M3 (колонка `hint`), поэтому они
перерепетированы отдельно (раздел «Повторная репетиция после финального ревью»). Выкладка — **только по явному «да» владельца**; исполняет
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
| RevenueCat Events | `ZGYr6srzS3rSSXHp` | A1: `Dedup Gate → If Duplicate?` + журнал `rc_event_ledger` в транзакции апсерта; A2: узкий перенос плана на alias-пути — в никогда не платившую затравку без периода, с финального ревью (I-1) и в погашенную свипом (`trial`/`expired`) | без генератора (ручная правка, `verify-telegram-parity.py` `check_rc_event_ledger`); запросы Consolidate/Transfer — `apply-billing-consolidation.py` | `8ce1e0a256a1` → `a3c52f30dcf7` |
| Delete Orphan Profiles | `2islisFH7jjLoPQM` | A4: `Live Profiles` + защита «живая строка ∧ Wappi когда-то авторизовал» | без генератора (`verify-orphan-sweep.py`) | `4b14cf5348c0` → `d617975779c3` |
| Handoff Notifier | `crHandoffNtfy001` | A5: номер владельца по правилу V2 (`OWNER_PHONE_JS`) | `build-handoff-workflows.py` | `80eebf0b71f8` → `53faf5b0fbe6` |
| Auth Session | `crAuthSession001` | A3: `claim.verify` переключает `bot_profiles.app_user_id` на биллинговый id установки | `build-auth-workflows.py` | `32ddd28b0ec6` → `faeae51618cd` |
| Set Workflow State | `0DP79STI3pafXO9B` | защищённый список +`crVerifyIdent001` +`crChannelWatch001` | `build-verify-identity.py` / `build-channel-watch.py` (патч списка); выкладка — см. шаг 3 | `9bb5e0da92e0` → `36b6a73e9773` |
| Create WA | `XuvOp7TxOImOAmlj` | только +2 id в защищённых списках | `apply-clone-lifecycle.py` | `cc578d6f095d` → `78ed47e160c0` |
| Create TG | `Uz6HBBUpAiUqVysB` | только +2 id в защищённых списках | `apply-clone-lifecycle.py` | `7463a139ffc5` → `51becea5ebee` |
| Delete Bot Files | `lmjYsdNcQA2IE5rl` | только +2 id в защищённых списках | `apply-auth-gate.py` + `apply-history-handoff.py` | `52a2799f91be` → `64f4c6dfda90` |
| Profile Lifecycle Sweep | `fXYpCXPKw92EzRz8` | только +2 id в защищённых списках | `apply-sweep-clone-deactivate.py` | `24b1a5c2f44c` → `624c8bd7733f` |
| **Verify Identity (новый)** | `crVerifyIdent001` | A3: `/webhook/VerifyIdentity` — identity-heal после переустановки (перенос плана — по тому же правилу I-1; журнал хранит подсказку клиента `hint` — M-5) | `build-verify-identity.py` | — → `b329f4831d9e` |
| **Channel Watch (новый)** | `crChannelWatch001` | A4: каждые 10 мин, «канал отключился» владельцу через другой его канал | `build-channel-watch.py` | — → `e907de5eae43` |

`deploy-canonical.py` новые workflow не создаёт — они импортируются CLI (`n8n import:workflow`, id сохраняются). Шаблоны ботов
(`4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc`) в пакет не входят → `update-clones.py` работы не имеет (на проде клонов 0). Env не
меняется. Новая учётка: «RevenueCat API» `crRevenueCatApi1`.

### Миграции (все аддитивные и идемпотентные; пост-проверки — в файлах и одним запросом `postcheck.sql`)

| # | Файл | Зачем | Когда |
|---|---|---|---|
| M1 | `Tools/n8n/sql/2026-10-14-rc-event-ledger.sql` | `rc_event_ledger` (A1) | до RevenueCat Events: без таблицы CTE апсерта падает → каждое событие 500, RevenueCat повторяет |
| M2 | `Tools/n8n/sql/2026-10-14-channel-snapshot.sql` | `channel_snapshot` (+ `notify_attempts`) (A4) | до импорта `crChannelWatch001`: без таблицы `Live Rows` падает каждые 10 мин |
| M3 | `Tools/n8n/sql/2026-10-15-identity-heal-log.sql` | `identity_heal_log` (A3, журнал поддержки; 9 колонок — с финального ревью и `hint`, подсказка клиента, M-5) | до импорта `crVerifyIdent001`: без таблицы каждый heal отвечает 503 |

Таблицы независимы (нет ссылок друг на друга), порядок между ними не важен — важен «до своих workflow». Ожидание `postcheck.sql`:
`ledger_cols 2 | snapshot_cols 6 | heal_cols 9 | rls t | client_grants 0 | seq_client_usage f | indexes 4`.

### Учётка «RevenueCat API» (A3)

httpHeaderAuth, заголовок `Authorization`, значение `Bearer <secretKey>` из `Tools/n8n/.secrets/revenuecat-api.json`
(gitignored; ключ v2 проверен A3 только на чтение). **Id строго `crRevenueCatApi1`** — на него ссылается канон Verify Identity,
и каждый будущий PUT `deploy-canonical.py` несёт именно его. REST n8n id не принимает (проверено на репетиции — см. ниже),
поэтому учётка создаётся CLI `n8n import:credentials` из расшифрованного JSON. **Значение никогда не печатается, не попадает
в чат, документы, логи и git**: файл собирается локально в `.secrets/` (chmod 600), идёт на VPS и в контейнер (`docker compose cp`,
владелец `node`), все три копии удаляются сразу после импорта — и при сбое импорта — с проверкой удаления (шаг 2).

## Чек-лист выкладки (прод, только после «да» владельца)

**Стоп-правило (для каждого шага).** Если вывод шага отличается от «Ожидание», записанного здесь, — **СТОП**: следующий шаг не
выполнять, на проде ничего не чинить «на ходу». Сохранить вывод; если живое состояние вредно — откат ТОЛЬКО затронутого шага
(раздел «Откат»); доложить владельцу.

**Где выполнять.** Команды — из корня рабочей копии, в которой канон = пакет. `<date>` = дата выкладки. Во всех вызовах база
указана явно: инструменты по умолчанию смотрят на прод. Секреты — только из основной копии (`.secrets/` не в git):
```bash
MAIN=/Users/ayan/Projects/Automation; SEC="$MAIN/Tools/n8n/.secrets"; B=https://n8n.choosereply.com
export N8N_API_KEY="$(cat "$SEC/prod-api-key.txt")"     # verify-deployed / deploy-canonical / update-clones читают ключ отсюда; не печатать
git -C "$MAIN" diff --stat 9cd33a6c HEAD -- Tools/n8n/workflows Tools/n8n/sql   # Ожидание: пусто (канон = пакет)
```
Если вывод **НЕ пуст** (на `main` уже легла часть B или другая правка канона) — выполнять весь чек-лист из отдельного worktree на
коммите канона пакета `9cd33a6c` (там же `precheck.sql`/`postcheck.sql` с `heal_cols 9`; снимки, точки отката и журнал писать в
`$MAIN/docs/audits/…`, коммитить в основной копии; текст чек-листа читать с `main`):
```bash
git -C "$MAIN" worktree add "$MAIN-pkgA" 9cd33a6c && cd "$MAIN-pkgA"
git diff --stat 9cd33a6c HEAD -- Tools/n8n/workflows Tools/n8n/sql             # Ожидание: пусто
# … шаги 0–12 отсюда (sql-oneoff и ключ RevenueCat — по путям "$SEC/…", как ниже) …
cd "$MAIN" && git worktree remove "$MAIN-pkgA"                                  # после шага 13
```

0. **Предполётное (только чтение).**
   - Снимок «до»: `python3 Tools/n8n/verify-deployed.py --base $B --json "$MAIN/docs/audits/<date>-prod-before.json"` →
     Ожидание: канон 29, diff 11 — ровно ids и хэши «до» из таблицы; клонов 0. Иначе — СТОП.
   - Бэкап (утренний ночной архив того же дня не затирать — как 10-13):
     `ssh choosereply 'cd ~/backups && D=$(date +%F) && { [ ! -f n8n-$D.tar.gz ] || mv n8n-$D.tar.gz n8n-$D-0315.tar.gz; } && ~/choosereply/backup.sh && mv n8n-$D.tar.gz n8n-$D-predeploy.tar.gz && ls -la ~/backups | tail -4'`.
   - Dry-run + точки отката: `python3 Tools/n8n/deploy-canonical.py --base $B` → Ожидание: `would_update 8` (`ZGYr6srzS3rSSXHp`,
     `2islisFH7jjLoPQM`, `crHandoffNtfy001`, `crAuthSession001`, `XuvOp7TxOImOAmlj`, `Uz6HBBUpAiUqVysB`, `lmjYsdNcQA2IE5rl`,
     `fXYpCXPKw92EzRz8`), `missing 2` (`crVerifyIdent001`, `crChannelWatch001`), `up_to_date 17`, `failed 0`; и
     `python3 Tools/n8n/deploy-canonical.py --base $B --only 0DP79STI3pafXO9B --include-placeholders` → `would_update 1`. Все строки
     `rollback pointer` → `$MAIN/docs/audits/<date>-deploy-rollback-pointers.txt`. (Исправления финального ревью набор не меняют —
     RevenueCat Events по-прежнему среди 8, Verify Identity среди 2 missing; повторный dry-run от прод-состояния дал ровно это.)
   - Учётки, на которые ссылаются новые workflow: `vvRrFiEXzLVqKjOx` (Postgres — у каждого Postgres-узла канона), WappiAuthToken
     (Channel Watch) и `crRevenueCatApi1` (Verify Identity; создаётся в шаге 2). **WappiAuthToken на проде — `ZowntFGvApDJ7UzQ`**
     (единственная httpHeaderAuth-учётка с этим именем; её несут все живые workflow). Канон несёт id `EuhhqAaV56DpoqAN`, которого на
     проде НЕТ: PUT публичного API (`deploy-canonical`) перепривязывает его по имени сам (поэтому живые Create WA/TG, Notifier, Auth
     Session несут `ZowntFGvApDJ7UzQ`), а новый workflow перед импортом перепривязывается явно — шаг 4 (правило a1-deploy-runbook
     §2.4). Проверка (только чтение): `uc.call(B, "GET", "/credentials?limit=100")` — id/тип/имя, без значений. (До 2026-10-15 здесь
     стояло «тот же id несут живые …» — неверно, см. «Уроки выкладки 2026-10-15».)
   - Засечь начало выкладки для наблюдения (шаги 11–12): `export DEPLOY_START="$(date -u +%Y-%m-%dT%H:%M:%SZ)"`.
1. **Миграции** (каждая — `200` или «No item to return»; затем пост-проверка):
   ```bash
   python3 "$SEC/sql-oneoff.py" "$(cat docs/audits/2026-10-15-paying-users-protection/precheck.sql)"   # Ожидание: t | t | t
   python3 "$SEC/sql-oneoff.py" "$(cat Tools/n8n/sql/2026-10-14-rc-event-ledger.sql)"
   python3 "$SEC/sql-oneoff.py" "$(cat Tools/n8n/sql/2026-10-14-channel-snapshot.sql)"
   python3 "$SEC/sql-oneoff.py" "$(cat Tools/n8n/sql/2026-10-15-identity-heal-log.sql)"
   python3 "$SEC/sql-oneoff.py" "$(cat docs/audits/2026-10-15-paying-users-protection/postcheck.sql)"
   # Ожидание: {"ledger_cols":"2","snapshot_cols":"6","heal_cols":"9","rls":true,"client_grants":"0","seq_client_usage":false,"indexes":"4"}
   ```
2. **Учётка «RevenueCat API» `crRevenueCatApi1`** (значение не печатать нигде). На staging отрепетирован сам CLI
   `n8n import:credentials` при работающем сервере (id сохраняется; REST отвечает 400 «id is read-only»); доставка файла в контейнер
   на staging (без Docker) не репетировалась — основной путь ниже использует `docker compose cp`, которым каждую ночь пользуется
   `backup.sh` на этом VPS (в обратную сторону):
   ```bash
   # 2a. файл локально, 600, значение не выводится
   SEC="$SEC" python3 - <<'PY'
   import json, os, pathlib
   sec = pathlib.Path(os.environ["SEC"])
   s = json.loads((sec / "revenuecat-api.json").read_text())
   out = sec / "rc-cred-import.json"
   out.write_text(json.dumps([{"id": "crRevenueCatApi1", "name": "RevenueCat API", "type": "httpHeaderAuth",
                               "data": {"name": "Authorization", "value": "Bearer " + s["secretKey"]}}]))
   os.chmod(out, 0o600); print("written", out.stat().st_size, "bytes")
   PY
   # 2b. на VPS → в контейнер (владелец node) → импорт → удалить ВСЕ копии (удаление выполняется и при сбое импорта)
   scp -q "$SEC/rc-cred-import.json" choosereply:/tmp/rc-cred-import.json
   ssh choosereply 'cd ~/choosereply; chmod 600 /tmp/rc-cred-import.json
     docker compose cp /tmp/rc-cred-import.json n8n:/tmp/rc-cred.json \
       && docker compose exec -T -u root n8n chown node:node /tmp/rc-cred.json \
       && docker compose exec -T n8n n8n import:credentials --input=/tmp/rc-cred.json
     echo "import rc=$?"
     docker compose exec -T -u root n8n rm -f /tmp/rc-cred.json; rm -f /tmp/rc-cred-import.json
     docker compose exec -T n8n sh -c "test ! -e /tmp/rc-cred.json && echo container-copy-removed"
     test ! -e /tmp/rc-cred-import.json && echo host-copy-removed
     docker compose exec -T n8n sh -c "n8n export:credentials --id=crRevenueCatApi1 --output=/tmp/c.json >/dev/null 2>&1; grep -c crRevenueCatApi1 /tmp/c.json; rm -f /tmp/c.json"'
   rm -f "$SEC/rc-cred-import.json"; test ! -e "$SEC/rc-cred-import.json" && echo local-copy-removed
   ```
   Ожидание: «Successfully imported 1 credential.», `import rc=0`, `container-copy-removed`, `host-copy-removed`, `1` (учётка с id
   `crRevenueCatApi1` есть; экспорт зашифрован и печатается только счётчик, файл удалён), `local-copy-removed`.
   НЕ репетировано (запасная форма без копии на диске VPS): `ssh choosereply 'cd ~/choosereply && docker compose exec -T n8n sh -c
   "umask 077; cat > /tmp/rc-cred.json"' < "$SEC/rc-cred-import.json"`, затем импорт и те же удаления/проверки.
   **2c. Проба учётки — только HTTP-статус** (финальное ревью M-1: права ключа на проде иначе проверил бы лишь первый настоящий
   heal). Временный workflow `Webhook → Owner (Postgres, только чтение) → RevenueCat (GET v2 aliases с учёткой crRevenueCatApi1) →
   Status` печатает ТОЛЬКО число найденных строк владельца и HTTP-статус RevenueCat (ни id, ни тела ответа, ни значения ключа;
   успешные выполнения не сохраняются) и удаляется ≥ 35 с после выполнения (правило insights). Id владельца в репозитории — только
   префикс (`docs/audits/implementation-progress.md`: `$RCAnonymousID:4a7af3f8…`), полный id проба берёт сама из `subscribers`:
   ```bash
   B="$B" RC=https://api.revenuecat.com OWNER_PREFIX=4a7af3f8 PG_ID=vvRrFiEXzLVqKjOx PG_NAME=Postgres python3 - <<'PY'
   import importlib.util, json, os, re, secrets, time, urllib.error, urllib.request
   s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
   B, RC, PREFIX = os.environ["B"], os.environ["RC"].rstrip("/"), os.environ["OWNER_PREFIX"]
   assert re.fullmatch(r"[0-9a-f]{8,32}", PREFIX), "OWNER_PREFIX: hex only"
   path = "rc-cred-probe-" + secrets.token_hex(12)
   owner_sql = ("select app_user_id, count(*) over () as n from subscribers where starts_with(app_user_id, chr(36) || "
                "'RCAnonymousID:" + PREFIX + "') order by updated_at desc nulls last limit 1")
   url = "={{ '" + RC + "/v2/projects/0f8c9529/customers/' + encodeURIComponent($json.app_user_id || 'none') + '/aliases?limit=1' }}"
   status_js = ("const o = $('Owner').first().json || {};\n"
                "return [{ json: { owner_rows: Number(o.n) || 0, status: Number($input.first().json.statusCode) || null } }];")
   wf = {"name": "ZZZ TEMP rc-cred-probe (Part A step 2c, auto-delete)",
         "settings": {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveManualExecutions": False},
         "nodes": [
           {"id": "h", "name": "Hook", "type": "n8n-nodes-base.webhook", "typeVersion": 2, "position": [0, 0],
            "parameters": {"httpMethod": "POST", "path": path, "responseMode": "lastNode", "options": {}}},
           {"id": "o", "name": "Owner", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": [220, 0], "alwaysOutputData": True,
            "parameters": {"operation": "executeQuery", "query": owner_sql, "options": {}},
            "credentials": {"postgres": {"id": os.environ["PG_ID"], "name": os.environ["PG_NAME"]}}},
           {"id": "r", "name": "RevenueCat", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2, "position": [440, 0],
            "alwaysOutputData": True, "onError": "continueRegularOutput",
            "parameters": {"method": "GET", "url": url, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
                           "options": {"timeout": 10000, "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}},
            "credentials": {"httpHeaderAuth": {"id": "crRevenueCatApi1", "name": "RevenueCat API"}}},
           {"id": "s", "name": "Status", "type": "n8n-nodes-base.code", "typeVersion": 2, "position": [660, 0], "parameters": {"jsCode": status_js}}],
         "connections": {"Hook": {"main": [[{"node": "Owner", "type": "main", "index": 0}]]},
                         "Owner": {"main": [[{"node": "RevenueCat", "type": "main", "index": 0}]]},
                         "RevenueCat": {"main": [[{"node": "Status", "type": "main", "index": 0}]]}}}
   st, created = uc.call(B, "POST", "/workflows", wf)
   wid = created.get("id") if st in (200, 201) and isinstance(created, dict) else None
   if not wid:
       raise SystemExit(f"probe not created: {st}")
   try:
       act, ans = uc.call(B, "POST", f"/workflows/{wid}/activate", {})[0], {}
       for _ in range(5):
           time.sleep(1.5)
           try:
               with urllib.request.urlopen(urllib.request.Request(f"{B}/webhook/{path}", data=b"{}", method="POST",
                                                                  headers={"Content-Type": "application/json"}), timeout=30) as r:
                   ans = json.loads(r.read().decode() or "{}"); break
           except urllib.error.HTTPError as e:
               if e.code != 404:
                   ans = {"probe_http": e.code}; break
       print("activate", act, "| owner rows", ans.get("owner_rows"), "| RevenueCat HTTP status", ans.get("status"),
             *(["| probe webhook", ans["probe_http"]] if "probe_http" in ans else []))
   finally:
       uc.call(B, "POST", f"/workflows/{wid}/deactivate", {})
       time.sleep(36)                                   # a ran workflow lives >= 35 s before it is deleted (insights)
       print("temp workflow deleted", uc.call(B, "DELETE", f"/workflows/{wid}")[0], "-> GET", uc.call(B, "GET", f"/workflows/{wid}")[0])
   PY
   ```
   Ожидание: `activate 200 | owner rows 1 | RevenueCat HTTP status 200`, затем `temp workflow deleted 200 -> GET 404`. Статус `404`
   (RevenueCat не знает этого клиента в проекте `0f8c9529`) ключ тоже доказывает: авторизация проходит раньше поиска клиента. **`401` /
   `403` — СТОП** (ключ неверен или без права на customers/aliases): шаги 3+ не выполнять, учётку пересоздать по 2a–2b с верным ключом
   и повторить 2c; живого вреда нет — учётку пока ничто не использует. Статус пуст (`None`: таймаут/сеть) или нет вывода проб — СТОП
   и повторить пробу один раз; второй пустой ответ — СТОП, доложить. Ошибку исполнения здесь не прочитать: узел RevenueCat идёт
   дальше при ошибке (`continueRegularOutput`), выполнение завершается успешно, а успешные не сохраняются
   (`saveDataSuccessExecution: none`). Heredoc запускать в той же оболочке, где задана `B` (из отдельного скрипта — `export B`).
   `owner rows 0` при `404` — ключ исправен, но строки владельца по префиксу нет: доложить, не чинить. Отрепетировано на staging (мок RevenueCat, учётка-заглушка) — «Повторная репетиция».
3. **Set Workflow State — защита двух новых id ДО их импорта и включения** (до этого шага legacy-вызов без токена — compat-окно
   открыто до 2026-12-31 — мог бы выключить или удалить новые workflow). Путь по решению контроллера (отрепетирован):
   `python3 Tools/n8n/deploy-canonical.py --base $B --only 0DP79STI3pafXO9B --include-placeholders --apply --yes` —
   `__HDR_CRED__` привязывается к учётке `n8n Admin API` живого Set Workflow State. Ожидание: «published by the PUT (served version =
   the update) — no publish call, no deactivate window», «read-back: ok», `updated 1`, `failed 0`.
   **ТОЛЬКО запасной путь** (если первый отказал): `python3 Tools/n8n/build-client-webhooks.py --update` — на staging не
   репетируется; заново ВКЛЮЧАЕТ два живых вебхука (Support Message и Set Workflow State; на n8n 2.36.7 activate активного workflow
   снимает его вебхук на миллисекунды); запускать ТОЛЬКО из основной копии `$MAIN` — без `.secrets/client-webhooks-state.json` он
   СОЗДАСТ на проде новые учётки и workflow; затем `--export` и `git diff --stat Tools/n8n/workflows/` — Ожидание: пусто.
4. **Импорт двух новых workflow** (неактивными) — с перепривязкой учётки по правилу a1-deploy-runbook §2.4 (добавлено при выкладке
   2026-10-15; «Уроки выкладки 2026-10-15»). Канон Channel Watch несёт WappiAuthToken `EuhhqAaV56DpoqAN`, которого на проде нет;
   `import:workflow` n8n 2.36.7 заменяет ненайденный id учёткой с тем же именем только В ПРОЕКТЕ импорта
   (`replaceInvalidCredentials` → `findByNameAndTypeInProject`), а если не заменит — каждый прогон упадёт на вызове Wappi. Поэтому
   импорт идёт из ВРЕМЕННЫХ копий (scratchpad, не канон), где id перепривязан явно, а учётки обеих копий проверены до импорта:
   ```bash
   # 4a. временные копии + аудит (канон не трогается): CW EuhhqAaV56DpoqAN -> ZowntFGvApDJ7UzQ (имя WappiAuthToken то же), VI как есть
   IMP=<scratchpad>/import; mkdir -p "$IMP"; IMP="$IMP" python3 - <<'PY'
   import copy, importlib.util, json, os, sys
   s = importlib.util.spec_from_file_location("vd", "Tools/n8n/verify-deployed.py"); vd = importlib.util.module_from_spec(s); s.loader.exec_module(vd)
   DEV, PROD = "EuhhqAaV56DpoqAN", "ZowntFGvApDJ7UzQ"
   WANT = {"crChannelWatch001-Channel_Watch": {("httpHeaderAuth", PROD, "WappiAuthToken"), ("postgres", "vvRrFiEXzLVqKjOx", "Postgres")},
           "crVerifyIdent001-Verify_Identity": {("httpHeaderAuth", "crRevenueCatApi1", "RevenueCat API"), ("postgres", "vvRrFiEXzLVqKjOx", "Postgres")}}
   ok = True
   for f, want in WANT.items():
       canon = json.load(open(f"Tools/n8n/workflows/{f}.json")); w = copy.deepcopy(canon)
       for n in w["nodes"]:
           for t, ref in (n.get("credentials") or {}).items():
               if ref.get("id") == DEV:
                   assert t == "httpHeaderAuth" and ref.get("name") == "WappiAuthToken"; ref["id"] = PROD
       text = json.dumps(w, ensure_ascii=False, indent=2)
       refs = {(t, r.get("id"), r.get("name")) for n in w["nodes"] for t, r in (n.get("credentials") or {}).items()}
       good = refs == want and DEV not in text and vd.graph_hash(w) == vd.graph_hash(canon) and w.get("active") is False
       ok &= good; open(f"{os.environ['IMP']}/{f}.json", "w").write(text)
       print(f, sorted(refs), "graph", vd.graph_hash(w), "OK" if good else "FAIL")
   sys.exit(0 if ok else 1)
   PY
   # 4b. импорт ИЗ КОПИЙ (id сохраняются)
   for f in crVerifyIdent001-Verify_Identity crChannelWatch001-Channel_Watch; do
     scp -q "$IMP/$f.json" "choosereply:/tmp/$f.json"
     ssh choosereply "cd ~/choosereply; docker compose cp /tmp/$f.json n8n:/tmp/$f.json \
       && docker compose exec -T -u root n8n chown node:node /tmp/$f.json \
       && docker compose exec -T n8n n8n import:workflow --input=/tmp/$f.json; echo \"import rc=\$?\"; \
       docker compose exec -T -u root n8n rm -f /tmp/$f.json; rm -f /tmp/$f.json"
   done
   ```
   Ожидание: аудит 4a — `OK` ×2 (CW `[('httpHeaderAuth', 'ZowntFGvApDJ7UzQ', 'WappiAuthToken'), ('postgres', 'vvRrFiEXzLVqKjOx',
   'Postgres')]` graph `e907de5eae43`; VI `crRevenueCatApi1` + `vvRrFiEXzLVqKjOx` graph `b329f4831d9e`), иначе — СТОП до импорта;
   импорт ×2 — «Successfully imported 1 workflow.», `import rc=0` (строка CLI «Error tracking disabled because this release is older
   than 6 weeks.» — служебная, не ошибка). НЕ репетировано (запасная форма потоком):
   `ssh choosereply "cd ~/choosereply && docker compose exec -T n8n sh -c 'cat > /tmp/$f.json'" < "$IMP/$f.json"`.
   **4c. GET до включения** (только чтение): у каждого нового workflow `active False` и учётки РОВНО: Verify Identity —
   `{crRevenueCatApi1, vvRrFiEXzLVqKjOx}`, Channel Watch — `{ZowntFGvApDJ7UzQ, vvRrFiEXzLVqKjOx}`; любой другой id (в т.ч. уцелевший
   `EuhhqAaV56DpoqAN`) — НЕ включать, СТОП:
   ```bash
   python3 - <<'PY'
   import importlib.util
   s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
   WANT = {"crVerifyIdent001": {"crRevenueCatApi1", "vvRrFiEXzLVqKjOx"}, "crChannelWatch001": {"ZowntFGvApDJ7UzQ", "vvRrFiEXzLVqKjOx"}}
   for wid, want in WANT.items():
       st, w = uc.call("https://n8n.choosereply.com", "GET", f"/workflows/{wid}")
       ids = {r.get("id") for n in w.get("nodes", []) for r in (n.get("credentials") or {}).values()}
       print(wid, st, "active", w.get("active"), sorted(ids), "OK" if st == 200 and w.get("active") is False and ids == want else "FAIL — do NOT activate")
   PY
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
   Channel Watch = `ZowntFGvApDJ7UzQ` + `vvRrFiEXzLVqKjOx` (id прода после перепривязки шага 4; до 2026-10-15 здесь стояло
   `EuhhqAaV56DpoqAN` — неверно: такого id на проде нет).
6. **Smoke Verify Identity** — запросы 10.1a и 10.2 из шага 10 (Ожидание — там же).
7. **deploy-canonical по одному** (`python3 Tools/n8n/deploy-canonical.py --base $B --only <id> --apply --yes`): `ZGYr6srzS3rSSXHp`
   RevenueCat Events (после M1!) → `2islisFH7jjLoPQM` Delete Orphan Profiles → `crHandoffNtfy001` Handoff Notifier →
   `crAuthSession001` Auth Session → `XuvOp7TxOImOAmlj` Create WA → `Uz6HBBUpAiUqVysB` Create TG → `lmjYsdNcQA2IE5rl` Delete Bot Files
   → `fXYpCXPKw92EzRz8` Profile Lifecycle Sweep. Ожидание у каждого: «published by the PUT (served version = the update) — no publish
   call, no deactivate window», «read-back: ok», `{"updated": 1, …, "failed": 0}` (Create/Sweep — инструмент привязывает
   `__HDR_CRED__` к `n8n Admin API` и отказывает непривязанным).
8. `python3 Tools/n8n/update-clones.py --base $B` → Ожидание: клонов 0 (архивный «13» пропущен) — применять нечего.
9. **Read-back:** `python3 Tools/n8n/verify-deployed.py --base $B --json "$MAIN/docs/audits/<date>-prod-after.json"` → Ожидание:
   exit 0, канон 29, diff 0, хэши «после» = таблица.
10. **Smoke — шесть проверок без изменения данных** (10.1–10.6; 10.1 — два GET). Ни одна не может ничего изменить даже при нарушенном
    предусловии: тела без id, а проба Set Workflow State просит `activate` уже ВКЛЮЧЁННОГО защищённого workflow (худшее при сломанной защите —
    повторная регистрация расписания Channel Watch; `deactivate`/`delete` в smoke не применять никогда).
    ```bash
    F='v1.eyJpIjoieCJ9.eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHg'      # поддельный токен (подпись не та)
    curl -s -w ' [%{http_code}]\n' "$B/webhook/VerifyIdentity"                                                      # 10.1a
    curl -s -w ' [%{http_code}]\n' "$B/webhook/RevenueCatEvent"                                                     # 10.1b
    curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' -d '{}' "$B/webhook/VerifyIdentity"   # 10.2
    curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' \
      -d '{"op":"status","channel":"whatsapp"}' "$B/webhook/WappiProxy"                                             # 10.3
    curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' -d '{"op":"nonsense"}' "$B/webhook/AuthSession"   # 10.4
    curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' \
      -d '{"workflowId":"crChannelWatch001","action":"activate"}' "$B/webhook/SetWorkflowState"                    # 10.5
    curl -s -w ' [%{http_code}]\n' -X POST -H 'Content-Type: application/json' -H "Authorization: Bearer $F" \
      -d '{}' "$B/webhook/DeleteBotFiles"                                                                            # 10.6
    ```
    Ожидание:
    - 10.1a / 10.1b: `[404]`, тело содержит «This webhook is not registered for GET requests. Did you mean to make a POST request?»
      (вебхук зарегистрирован; GET исполнения не запускает);
    - 10.2: `{"success":false,"error":"unauthorized","reason":"unauthorized:missing"} [401]` (гейт только по токену, compat-окно его не открывает);
    - 10.3: `{"success":false,"error":"unauthorized"} [401]` (у прокси нет legacy-режима);
    - 10.4: `{"success":false,"error":"bad_request"} [400]`;
    - 10.5: `{"success":false,"error":"protected_workflow"} [400]` (legacy-вызов без токена → Validate → защищённый id; проверка не
      зависит от action; после закрытия compat-окна `CR_AUTH_COMPAT_UNTIL` — `[401]` до Validate);
    - 10.6: `{"success":false,"error":"unauthorized","reason":"unauthorized:bad_signature"} [401]`.
    (На staging 2026-10-15 те же ответы; в первой репетиции 10.5 шла с `deactivate`/`delete`, в повторной — ровно эти запросы, 10.5
    с `activate` на оба новых id: `[400] protected_workflow`.)
11. **Первый тик Channel Watch и наблюдение** (≤ 10 мин после шага 5). Успешные выполнения Verify Identity и Channel Watch НЕ
    сохраняются (`saveDataSuccessExecution: none`) — поэтому смотрим ошибки (сохраняются все), снимок и логи:
    ```bash
    python3 - <<'PY'
    import importlib.util, os
    s = importlib.util.spec_from_file_location("uc", "Tools/n8n/update-clones.py"); uc = importlib.util.module_from_spec(s); s.loader.exec_module(uc)
    B, SINCE = "https://n8n.choosereply.com", os.environ["DEPLOY_START"]
    for wid in ("ZGYr6srzS3rSSXHp", "crVerifyIdent001", "crChannelWatch001"):
        st, d = uc.call(B, "GET", f"/executions?workflowId={wid}&status=error&limit=50")
        errs = [(e.get("id"), e.get("startedAt")) for e in (d or {}).get("data", []) if (e.get("startedAt") or "") >= SINCE]
        print(wid, "GET", st, "| error executions since", SINCE, ":", errs)
    PY
    python3 "$SEC/sql-oneoff.py" "select count(*) as rows, count(*) filter (where authorized) as up, count(*) filter (where not authorized and notified_at is null) as owed from channel_snapshot;"
    ```
    Ожидание: `GET 200 | error executions since … : []` ×3; снимок: `rows` = живые профили, которые перечислил Wappi, `owed 0`
    (первое наблюдение ничего не должно — уведомлений нет). Любая ошибка в списке → СТОП: прочитать её
    (`uc.call(B, "GET", "/executions/<id>?includeData=true")`), до разбора ничего не менять.
12. **Логи 15 мин + повтор запроса шага 11:** `ssh choosereply 'cd ~/choosereply && docker compose logs --since 15m n8n | grep -iE
    "error|insights" | tail -20'` → Ожидание: пусто (временные workflow `sql-oneoff` ждут 35 с перед удалением — цикл insights не
    возникает); запрос шага 11 через 15 мин и на следующее утро — снова `[]` ×3. Иначе — СТОП (стоп-правило).
    **12b. Первое живое событие RevenueCat — по журналу доставок самого RevenueCat** (финальное ревью M-1; событие может прийти
    позже окна выкладки — на проде ботов 0, покупки и продления пойдут с новой сборкой; тогда пункт закрывается при первом событии
    записью в журнал): дашборд RevenueCat → проект → **Integrations → Webhooks** → вебхук на
    `https://n8n.choosereply.com/webhook/RevenueCatEvent` → история отправленных им событий с кодом ответа нашего сервера (подписи
    интерфейса RevenueCat могут отличаться). Ожидание: у первого события после `DEPLOY_START` — `200`; у повторной доставки того же
    события — тоже `200` (её отвечает `Dedup Gate` по журналу `rc_event_ledger`, второго начисления нет). Любой не-2xx, таймаут или
    серия повторов → СТОП: найти исполнение RevenueCat Events по времени события (запрос шага 11 для `ZGYr6srzS3rSSXHp`; ошибки
    сохраняются) и разобрать до любых правок; RevenueCat сам повторяет не-2xx, и повтор безопасен (журнал).
13. **Журнал и флаги «DEPLOYED».** `$MAIN/docs/audits/implementation-progress.md` — новый раздел «Развёртывание <date> — часть A
    «защита платящих»» (таблица шагов, снимки до/после, точки отката, smoke; запись «… ждёт go» остаётся историей). Перевести каждую
    отметку «canon, NOT deployed» части A в «DEPLOYED to prod <date>»:
    в основной копии `$MAIN`: `grep -n -E "защита платящих.*canon, (not|NOT) deployed" CLAUDE.md Tools/n8n/README.md` — ровно 7 строк
    (номера — на `9cd33a6c`; если часть B сдвинула строки — по выводу grep):
    - `CLAUDE.md:139` — «A5/A6»: номер владельца в Notifier, «(A5 «защита платящих», 2026-10-15 — canon, not deployed)»;
    - `CLAUDE.md:167` — заголовок блока «Part A … — canon, NOT deployed:» (вместе с фразой «…; prod only on the owner's «да»» в том же
      абзаце — заменить ссылками на журнал, снимки и точки отката);
    - `CLAUDE.md:217` — `Delete Orphan Profiles`: «Live-profile protection (A4 …, 2026-10-15 — canon, not deployed)»;
    - `CLAUDE.md:218` — пункт `Channel Watch` «(… 2026-10-15 — canon, not deployed)»;
    - `Tools/n8n/README.md:138` — пункт набора «Part A «защита платящих» (… — canon, NOT deployed)»;
    - `Tools/n8n/README.md:161` — строка RevenueCat Events «A1/A2 «защита платящих» (2026-10-14/15 — canon, NOT deployed; …)»;
    - `Tools/n8n/README.md:199` — раздел Delete Orphan Profiles «Since A4 … (2026-10-15 — canon, not deployed)»;
    - и строка «Статус» этого README («на проде НЕ выложено» → «выложено <date>»). `Tools/n8n/staging/README.md` отметки о
      выкладке не несёт — менять нечего. Отметки «client, new build» (карточка бота, «Подписка», триггер heal) остаются до сборки.
    Коммит `deploy(prod): …` (точечные пути).
14. **Наблюдения владельца — каждое по отдельному «да», нужен живой бот (на проде сейчас ботов 0 — после новой сборки):**
    (а) A5: настоящая заявка на Telegram-боте владельца, у которого `get/status` несёт `account.phone` ≠ верхнего `phone`, —
    уведомление приходит в «Избранное» собственного Telegram-аккаунта; (б) A4: разлогинить WhatsApp у бота с WhatsApp + Telegram —
    за ≤ 10 мин одно self-message «Choose Reply: WhatsApp отключился — …» в «Избранное» Telegram, повторная привязка ничего не шлёт.

`unset N8N_API_KEY DEPLOY_START` по окончании.

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
  «Подписка», финальное ревью I-2 — проба настроек и опросчики привязки действуют только по `ChannelHealth.Judge` с эхом, `2303cff7`)
  — новая сборка → `Tools/store/inspect-artifact.sh` → device-pass. Сервер безопасен для старых клиентов: Verify Identity
  зовёт только новая сборка; ответы прочих вебхуков не меняются.
- Env, шаблоны ботов, клоны, Supabase `documents`, сайт.

## Репетиция на staging (2026-10-15)

Стенд: n8n 2.36.7 с task runners (как прод), Postgres `pg.sh` (5433), Wappi-мок 127.0.0.1:8765; миграции — ещё и на одноразовом
кластере (5434, scratchpad). Прод не трогался, кроме снимка `verify-deployed` (только GET). Вывод каждого шага — `rehearsal.txt`
(рядом; сырые журналы — `Tools/test-output/a7/`, gitignored).

Это ПЕРВАЯ репетиция — канон `9caddb6f`, до исправлений финального ревью: хэши «после» RevenueCat Events / Verify Identity тогда
были `1196077677b4` / `3af1831f3ca4`, пост-проверка — `heal_cols 8`. Финальное ревью поменяло ровно эти два графа и M3; их
повторная репетиция — следующий раздел и `rehearsal.txt` §F.

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

### Повторная репетиция после финального ревью (2026-10-15, канон `9cd33a6c`)

Финальное ревью после первой репетиции поменяло графы RevenueCat Events (I-1) и Verify Identity (I-1 + M-5) и миграцию M3
(`hint`); остальные девять графов — те же хэши. Повторены: гейты, M3 на свежем кластере, RED на настоящем n8n и путь пакета от
прод-состояния (полный порядок чек-листа — он дёшев; e2e — сценарии двух изменённых графов). Вывод каждого шага —
`rehearsal.txt` §F; сырые журналы — `Tools/test-output/final-fix/` (gitignored).

| Шаг | Результат |
|---|---|
| Гейты на `9cd33a6c` | генераторы `--check` 21/21 + верификаторы 13/13 + `--self-test` 33/33 + `verify_rag` + `verify-panel-prompts.js` + `verify-sql-comments` (98 узлов / 30 файлов) — 38/38 exit 0; `tests/run_all.py` — 38 файлов, 1349 случаев, 0 падений; Unity EditMode полный 3184/3184 |
| M3 (`hint`) на свежем кластере | `pg.sh init` + `migrate` ×2, порядок прода M1 → M2 → M3 ×2 и обновление с прежнего M3 (таблица без `hint` с одной строкой → новый файл → 9 колонок, строка цела, `hint` NULL) — пост-проверка `2 \| 6 \| 9 \| t \| 0 \| f \| 4` каждый раз |
| RED на n8n 2.36.7 | стенд с каноном `df5aa5f1` (пакет до исправлений) → e2e `identity_heal`: 1 pass, 3 fail — `identity_heal_after_sweep_demotion`: heal 200 `consolidated 1`, а владелец остаётся `trial/expired` (блокировка I-1); у журнала нет подсказки (M-5) |
| Стенд = прод | `--canon-ref 927e1067`, два новых workflow, учётка и три таблицы удалены (≥ 35 с после выключения) → `verify-deployed`: канон 29, diff 11, статус каждого id = снимок прода |
| M1 → M2 → M3 | через Postgres-узел n8n (двойник `sql-oneoff.py`): precheck `t/t/t`, каждая `200 {"success":true}`, пост-проверка `{…"heal_cols":"9"…}` |
| Учётка + проба 2c | CLI `import:credentials` с id `crRevenueCatApi1` (заглушка) → проба шага 2c — текст README как есть (отступ списка снят): владелец-фикстура `owner rows 1 \| RevenueCat HTTP status 200`, префикс без строки `owner rows 0 \| 404`; мок видел два GET с `Bearer`; оба временных workflow удалены (GET 404), insights — 0 |
| Dry-run | would_update 8 (в т.ч. RevenueCat Events, точка отката), missing 2 (в т.ч. Verify Identity), up_to_date 17; SWS с `--include-placeholders` — would_update 1 = пакет |
| Выкладка по чек-листу | SWS → импорт + API-включение двух новых (учётки `crRevenueCatApi1` + Postgres) → smoke VI → 8 по одному: все «published by the PUT», «read-back: ok» → `update-clones` 0 → `verify-deployed` diff 0, exit 0 (RevenueCat Events и Verify Identity `graph=True published=True`) → повторный dry-run: 27 + SWS up to date |
| Smoke (шаг 10) | все шесть ответов как в «Ожидание» (10.5 — `activate`, оба новых id: `[400] protected_workflow`) |
| e2e | `identity_heal_*` 4/4 (вкл. `identity_heal_after_sweep_demotion`: владелец → `business/active/500/<период алиаса>/sub.business.month`; журнал с `hint`), `rc_*` 2/2, `retired_bot_reconnects_after_purchase` [wa]/[tg] 2/2 (`--no-llm`) |
| После | `verify-deployed` diff 0, exit 0; 0 строк ошибок (в т.ч. insights) в журнале n8n с момента старта сервера стенда; `message/send` на мок — 0; стенд остановлен (`bootstrap.py --stop`, мок, `pg.sh stop`) |

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
- **Клиент (финальное ревью I-2, новая сборка):** проба настроек удаляет профиль и опросчики привязки продвигаются только по
  `ChannelHealth.Judge` с эхом опрошенного профиля; защитный опрос перед пересозданием профиля по «Получить другой код»
  (`Manager.CheckWhatsappAuthorized`) по-прежнему читает статус без проверки эха — перекрёстный `false` чужого профиля там
  позволит пересоздать только что привязанный профиль (остаток, вне объёма исправления).

**Device-pass (в сценарий B3, новая сборка):**

1. Карточка «Подписка» без тарифа с купленным резервом: виден блок с одной строкой «Резерв: N диалогов — заморожен до
   возобновления подписки» (строка переносится на 2 строки при 30u — решение владельца по формулировке); покупка тарифа
   возвращает счётчики, формулировка — «спишутся после квоты».
2. Реальный разлогиненный профиль WhatsApp: есть ли `authorized_at` / `logouted_at` в `profile/all/get` (только чтение).
3. «Канал отключён» → переподключить в настройках на тарифе с ОДНИМ каналом → привязка проходит (Count Channels исключает
   строки того же `bot_key` на этом канале), данные бота на месте (линия F1), метка снята.
4. Переустановка приложения при живой подписке → один вызов `/webhook/VerifyIdentity` → подписка на новом id; строка в
   `identity_heal_log` (с `hint`); повторный запуск в течение 24 ч — без вызова (защёлка `CrIdentityHeal.*`). Отдельно (I-1): на
   iOS переустановить, дождаться, пока свип погасит триал новой установки (4 д 17 ч после первого бота: `trial/expired`), затем
   «Восстановить» — подписка всё равно переезжает; бот снова отвечает только после переподключения через F2 «Подключите ботов
   снова» (канал уже отключён свипом), не сразу после «Восстановить».
5. Наблюдения на проде по отдельному «да» (шаг 14 чек-листа): уведомление «канал отключился» в «Избранное» Telegram и
   уведомление о заявке Telegram-бота на номер `account.phone`.

**Уроки выкладки 2026-10-15** (журнал — `docs/audits/implementation-progress.md`, «Развёртывание 2026-10-15 — часть A»):

- **Id учётки при импорте нового workflow.** Чек-лист предполагал, что живые workflow несут WappiAuthToken `EuhhqAaV56DpoqAN` и
  импорт его сохранит; на проде такого id нет — единственная учётка с этим именем `ZowntFGvApDJ7UzQ`. PUT публичного API
  перепривязывает по имени сам (поэтому `deploy-canonical` этого не замечает: `graph_hash` id учёток не включает), импорт CLI —
  только в своём проекте. Найдено в предполётной проверке (только чтение), до любых изменений; решение контроллера — правило
  a1-deploy-runbook §2.4: временная копия с id прода, аудит учёток, импорт из копии, GET до включения (шаг 4). **Репетиция на
  staging этого поймать не может:** учётка WappiAuthToken стенда уже имеет id `ZowntFGvApDJ7UzQ`, а `derive-staging.py`
  перепривязывает канон по имени ещё до импорта — проверять id учёток нового workflow нужно на проде, `GET /credentials`
  (только чтение), в предполётном шаге.
- **Тик Channel Watch на проде в выполнениях не виден** (успешные не сохраняются): доказательство, что он идёт, — счётчики Postgres
  `pg_stat_user_tables` у `channel_snapshot` (PG 17: `idx_scan` +2 за тик, `last_idx_scan` = время тика; шаг 11 выкладки —
  13:50 и 14:00:03Z). На день выкладки Wappi перечислял 0 профилей на ОБЕИХ базах (проба только чтения тем же `GET
  profile/all/get`), поэтому снимок пуст верно; единственная живая строка `bot_profiles` — реликт до A1 (Telegram, без владельца,
  без workflow) — пропускается полом пустого списка (так же её не списывает liveness Sweep'а).

**Не проверено (остатки репетиции):** реальный RevenueCat API v2 из n8n прода (на стенде — мок; ключ проверен A3 локально,
только чтение) — права учётки «RevenueCat API» на проде проверяет проба шага 2c сразу после импорта (только HTTP-статус; при
выкладке 2026-10-15 — `owner rows 1 | RevenueCat HTTP status 200`), а
первое живое событие RevenueCat — журнал доставок RevenueCat (шаг 12b); живой бот на проде (их 0) —
наблюдения A4/A5 ждут новой сборки.
