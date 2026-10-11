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
Проверка изоляции: `grep -c 'wappi.pro\|n8n.choosereply.com' Tools/n8n/workflows-staging/*.json` → везде 0
(derive-staging.py сам отказывает при любом остатке).
