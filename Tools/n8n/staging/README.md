# STAGING — изолированный стенд для серверных проверок (аудит 2026-09, этап 0.3)

Зачем: инструкция ремедиации запрещает использовать production как тестовый стенд и требует
«подменённую отправку» — тестовое сообщение физически не может попасть реальному клиенту. Локальный
dev-инстанс n8n (`~/.n8n`) для этого НЕ подходит: его Postgres-credential смотрит на ту же Supabase-БД,
что и прод. Стенд собран из четырёх частей, все локальные, ничего system-wide:

| Часть | Что | Где |
|---|---|---|
| n8n | отдельный home `~/.n8n-staging` (`N8N_USER_FOLDER`; состояние лежит в `~/.n8n-staging/.n8n/` — свой SQLite, тот же encryptionKey, что у dev, bootstrap копирует `~/.n8n/config` туда ДО первого импорта → зашифрованный экспорт credentials импортируется без расшифровки); версия — прод-совместимая из `~/.n8n-staging/app` (npm, 2.36.7) или глобальная dev (2.27.4) как fallback | `n8n-staging.sh` |
| Postgres | PostgreSQL 16 из wheel `pgserver` (`~/.venvs/pgstaging`), кластер `~/.n8n-staging/pgdata`, порт 5433, БД `choosereply_staging`, роль `n8n_staging` (владелец таблиц = как прод-cred), роли `anon/authenticated/service_role` для дословного применения RLS-миграций | `pg.sh init\|start\|stop\|psql\|migrate` |
| Wappi-мок | `127.0.0.1:8765` вместо `https://wappi.pro/`: пишет все вызовы (JSONL), отдаёт историю из фикстур, «эхо» отправленных, fault-injection (`accept_then_drop`, 5xx, задержка) | `wappi-mock.py` |
| workflows | производные копии канона: хосты → localhost/мок, credentials → по имени, Postgres → **только** cred «Postgres Staging» (иначе отказ), staging-боты `--bot wa:<pid>` с webhook path = profile id | `derive-staging.py` → `Tools/n8n/workflows-staging/` (gitignored) |

Что НЕ изолировано (осознанно): OpenAI (реальный ключ dev-инстанса, копейки за прогон) и RAG-ретривер
(реальная Supabase через API-cred, ТОЛЬКО чтение с тестовыми bot-id, которым не соответствует ни один
chunk — фильтр по `botWaId/botTgId`). ПИСАТЬ в настоящую Supabase staging не может (R3, 2026-09-27):
`derive-staging.py` (`isolate_supabase_writers`) заменяет в staging-копиях вставку в векторное хранилище
Upload File на вставку в staging-таблицу `documents` (без эмбеддингов), а запись/удаление в Storage
(Store Original File, оба Delete Stored Original) — на NoOp, и отказывает, если писатель остался.
Поэтому загрузка прайса гоняется сквозным сценарием `e2e-auth.py upload_price_list_multipart`.

## Подъём (один раз)
```bash
Tools/n8n/staging/pg.sh init                                  # Postgres + миграции
n8n export:credentials --all --output=/tmp/dev-creds.json      # ЗАШИФРОВАННЫЙ экспорт dev (значения не раскрываются);
#   оставить 4 записи по имени: OpenAi account, Supabase, WappiAuthToken, RevenueCat Webhook
python3 Tools/n8n/staging/bootstrap.py --creds-file /tmp/dev-creds-4.json --bot wa:stgwa001 --bot tg:stgtg001
#   ключ шифрования, «Postgres Staging», owner + API key, n8n-API creds, derive → import → publish → start
```
## Каждый сеанс
```bash
Tools/n8n/staging/pg.sh start
WAPPI_MOCK_LOG=Tools/test-output/wappi-mock.jsonl python3 Tools/n8n/staging/wappi-mock.py &
python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001   # переимпорт канона + старт
python3 Tools/n8n/staging/e2e.py                              # сценарии этапа 0.4/3
python3 Tools/n8n/staging/vertical-bots.py --vertical education  # клоны с промптом Create (ниша + _core) вместо промпта шаблона —
#   для модельных e2e, которые должны видеть правку Tools/n8n/prompts/ (Q1 A6); --prompts-ref <ref> — голова старой карты;
#   назад к промпту шаблона — обычный bootstrap.py --bot …
python3 Tools/n8n/staging/bootstrap.py --stop                 # остановить сервер
```
## Прогон «до»: канон на другом git-ref (F1F2-plan T10)
```bash
python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001 --canon-ref 92a7caa2   # канон, каким он был на ref
python3 Tools/n8n/staging/e2e-auth.py --only reauth_new_profile          # сценарии — из рабочего дерева: ждём FAIL
python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001                       # вернуть рабочий канон
python3 Tools/n8n/verify-deployed.py --base http://localhost:5678                                # exit 0
```
`--canon-ref` берёт ТОЛЬКО `Tools/n8n/workflows/*.json` на этом ref (`git archive` во временный каталог); правила
вывода, изоляция и STAGING-ONLY workflows — из рабочего дерева; `_manifest.json` записывает `canonRef`. Новые
миграции на стенде остаются (они аддитивны, старый канон их не читает).
Проверка изоляции: `grep -c 'wappi.pro\|n8n.choosereply.com' Tools/n8n/workflows-staging/*.json` → везде 0
(derive-staging.py сам отказывает при любом остатке).

## Репетиция выкладки из прод-состояния (F1F2-plan T12)
```bash
python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001 --canon-ref <ref прода>   # стенд = прод
#   (клоны с промптом Create по старой карте: vertical-bots.py --vertical <id> --prompts-ref <ref> --dry-run → bootstrap … --prompt-file)
python3 Tools/n8n/verify-deployed.py --base http://localhost:5678            # DIFF = ровно пакет, клоны BEHIND
python3 Tools/n8n/deploy-canonical.py --base http://localhost:5678           # dry-run: ровно пакет (производный канон, T12)
python3 Tools/n8n/deploy-canonical.py --base http://localhost:5678 --only <id> --apply --yes   # по шагам пакета
python3 Tools/n8n/update-clones.py --base http://localhost:5678 [--apply --yes]
python3 Tools/n8n/staging/derive-staging.py --bot wa:stgwa001 --bot tg:stgtg001   # БЕЗ импорта: e2e читают производные узлы с диска
python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001        # вернуть рабочий канон → verify-deployed 0
```
`deploy-canonical.py` для local-базы выкладывает ПРОИЗВОДНЫЙ канон (адреса → localhost/мок, учётки по имени, писатели Supabase
выключены) и отказывает до первого запроса, если нет «Postgres Staging» или любой названной учётки. Всегда указывайте `--base`:
по умолчанию инструмент смотрит на прод. Миграции — на отдельном одноразовом кластере: `STAGING_PGDATA=<scratch> STAGING_PGPORT=5434
pg.sh init` (все миграции по порядку, затем `pg.sh migrate` ещё раз — идемпотентность), затем `pg.sh stop`.

## Часть A «защита платящих» (2026-10-14/15) — что знать о стенде
- **RevenueCat.** `derive-staging.py` переводит `https://api.revenuecat.com/` на мок (`<мок>/revenuecat/`) и ОТКАЗЫВАЕТ, если
  производный файл ещё называет `api.revenuecat.com` (`FORBIDDEN_HOSTS` — один список с `deploy-canonical.py`). Мок отвечает
  v2 `…/customers/<id>/aliases` так, как сценарий задал через `POST /__mock/rc_aliases {customer_id, aliases | null, status?, body?}`
  (сброс — вместе с остальным моком). Учётка «RevenueCat API» на стенде — заглушка (`Bearer stg-mock-…`); bootstrap создаёт её
  с id `crRevenueCatApi1` — тем же, что у канона и прода, поэтому производный Verify Identity привязан к ней байт в байт (на стенде,
  поднятом до 2026-10-15, она может носить старый id `stgRevenueCatApi1` — привязка по имени, работает одинаково).
- **Wappi-мок.** `profile/all/get` различает каналы (профиль с известным каналом виден только в своей базе; пустой список —
  `profiles: null`, как у Wappi); режим отказа `body` отдаёт заданный JSON с заданным статусом. `fixtures.py delete` /
  `sweep-mock-profiles` чистят и `channel_snapshot` — иначе перевыданный id `profile-NNNNNN` унаследует чужое «true».
- **Channel Watch активен и на стенде** (каждые 10 мин зовёт `profile/all/get`). Сценарии `channel_*` гоняют производные узлы
  через `WatchProbe`, который на время пробы выключает настоящий watch и включает обратно; сценарии, считающие ВСЕ вызовы мока
  в доле секунды, изредка (≈1 %) ловят тик — при FAIL такого сценария перезапустить его отдельно.
- **Insights.** n8n сбрасывает insights раз в 30 с; сброс, где упомянут workflow, удалённый после своего выполнения, падает
  и повторяется до рестарта («Error while saving insights metadata and raw data» каждые 30 с). `e2e-auth.py` (и наборы,
  которые берут его помощники: e2e-handoff, e2e-suggest, e2e-test-dialog) поэтому сразу ВЫКЛЮЧАЕТ отработавшие пробы и клоны,
  а удаляет их одной пачкой в конце прогона, ≥ 35 с после последнего выключения (`defer_delete` / `flush_deferred_deletes`).
  Остальные места, удаляющие отработавший workflow сразу (проба цены в `e2e-test-dialog.py`, уборка `e2e-clone-update.py`),
  по-прежнему запускают этот цикл — шум только стенда, лечится рестартом (`bootstrap.py`); на проде — правило ≥ 35 с всегда.
- **Репетиция пакета A от прод-состояния** — `docs/audits/2026-10-15-paying-users-protection/README.md` (новые workflow
  импортируются CLI `import:workflow` при работающем сервере и включаются через API, как на проде; учётка — `import:credentials`
  с фиксированным id).
