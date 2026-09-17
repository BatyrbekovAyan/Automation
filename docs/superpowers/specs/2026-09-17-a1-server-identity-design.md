# A1 — серверная identity, проверка владения и вынос транспортного секрета (этап 1)

Основание: аудит A1 (`docs/audits/2026-09-15-app-and-ai-audit.md`), карта вызовов клиента (60 сайтов:
33 Wappi с общим токеном, 12 n8n-вебхуков без auth; `docs/audits/implementation-progress.md`, этап 1).

## Факты, определившие дизайн
- Wappi: ОДИН токен аккаунта на все профили, ротация только в кабинете, у вебхуков НЕТ подписи/секрета/IP-списка,
  у `message/send` нет idempotency-key (есть `messages/id/get`). Значит клиент не может держать scoped-credential →
  все Wappi-операции идут через сервер как **разрешённые операции** (не прокси произвольного URL).
- Прод n8n: каждый Postgres-узел ≈ 1,9 с (Get Usage p50 1,92 с; VPS Астана ↔ Supabase-пулер). Auth-гейт на
  каждом запросе (в т.ч. опрос чата каждые 3 с) НЕ может ходить в БД → **подписанные capability-токены**
  (HMAC-SHA256, проверка в Code-узле без БД). Требует env на сервере: `NODE_FUNCTION_ALLOW_BUILTIN=crypto`,
  `N8N_BLOCK_ENV_ACCESS_IN_NODE=false`, `CR_AUTH_SECRET` (+`CR_AUTH_SECRET_PREV` на время ротации),
  `CR_AUTH_COMPAT_UNTIL` — проверено на staging 2026-09-17 (`require('crypto')` и `$env` работают только с ними).
- RevenueCat `app_user_id` — биллинговый идентификатор: остаётся ключом `subscribers`/`dialog_counts`, но
  НЕ доказательством владения; владение профилями/workflows — по install.

## Модель
**Install** (устройство/установка) — `installs(install_id, secret_hash, app_user_id, platform, app_version,
created_at, last_seen_at, revoked_at, token_version)`. Клиент хранит `installId` + `installSecret` (выдаётся ОДИН раз
при bootstrap, на сервере только sha256). Простой старт без регистрации сохранён: bootstrap = один POST без данных.
Переустановка = новый install; старые профили НЕ присваиваются автоматически (см. «Наследие»).

**Access-token** `v1.<b64url(payload)>.<b64url(hmac)>`, payload `{i: installId, u: appUserId|null, p: [profileIds],
w: [workflowIds], e: exp(unix, +12ч), n: tokenVersion}`. Обновляется через `AuthSession/refresh` (install_secret →
DB-проверка + актуальный список владений). Отзыв: `revoked_at`/`token_version` (действует при следующем refresh,
горизонт ≤ 12 ч) или ротация `CR_AUTH_SECRET` (мгновенно для всех). Токен в заголовке `Authorization: Bearer …`.

**Владение**: `bot_profiles.owner_id` (install), `bot_profiles.workflow_id` (клон-workflow профиля); RAG-чанки →
`botWaId/botTgId` = workflow → владелец; история `n8n_chat_histories` → `profile_id:chat_id` → владелец; файлы —
новая таблица `uploaded_files(file_id, owner_id, workflow ids)`; биллинг — `installs.app_user_id`.

## Endpoint-контракт (все клиентские вебхуки)
`Auth Gate` (Code) → `Authorized?` (If) → прежняя цепочка | `Respond 401` `{success:false,error:"unauthorized"}`.
Проверки владения по правилу вебхука (ids запроса ⊆ claims токена; сентинелы `""`/`-1` пропускаются):
SetWorkflowState `workflowId∈w`; SetReplyMode `profileIds⊆p`; SuggestReplies `profileId∈p, botWaId/botTgId∈w,
appUserId==u`; DashboardOutcomes `profileIds⊆p`; GetUsage `appUserId==u`; UploadFile `whatsappWorkflowId/
telegramWorkflowId∈w` + запись `uploaded_files`; DeleteFile — владелец по `uploaded_files` (legacy-файлы без записи —
по `botWaId/botTgId` чанков ∈ w); DeleteBotFiles `waProfileId/tgProfileId∈p, botWaId/botTgId∈w, appUserId==u`;
Create WA/TG `WhatsappProfileId/TelegramProfileId∈p`, второй workflow ∈ w, `AppUserID==u`, после создания
`bot_profiles.workflow_id`; Edit WA/TG `*WorkflowId∈w`; SupportMessage — любой валидный install.
Ошибка личности/прав = отказ (401/403) ДО любого платного/побочного узла. Fail-open биллинговых счётчиков
(Count Dialog, Suggestion Gate) НЕ меняется — отдельный пункт оценки риска в журнале.

**Wappi Proxy** `/webhook/WappiProxy` `{op, channel, …}` + Bearer: allowlist операций из карты клиента —
`profile.add`(регистрирует профиль владельцу), `profile.delete`, `profile.logout`, `status`, `qr`, `auth.code`,
`tg.auth.phone|code|2fa`, `chats.filter`, `messages.get`, `messages.id.get`, `media.download` (возвращает
подписанный URL — байты медиа клиент качает напрямую, как и сейчас), `message.send`, `message.reply`,
`message.mark_read`, `chat.delete`, `message.reaction`, `media.send`(img|video|document, b64). Параметры
валидируются по op; `profile_id ∈ p` (кроме `profile.add`); код и тело ответа Wappi пробрасываются (fullResponse).
Выполнения прокси не сохраняются (`saveDataSuccessExecution: none`) — опрос чатов не раздувает БД.

**Callback Wappi → бот**: `If` шаблона дополнительно требует `body.messages[0].profile_id == path вебхука`
(зарегистрированный профиль), иначе `filtered`. Подписи у Wappi нет — это предел механизма; размер входа —
`N8N_PAYLOAD_SIZE_MAX`, частота — Caddy rate-limit (xcaddy, owner-шаг) и лог `x-forwarded-for` (`N8N_PROXY_HOPS` уже задан).

## Наследие и миграция
- **Совместимость**: до `CR_AUTH_COMPAT_UNTIL` запрос БЕЗ токена на 12 вебхуках проходит в режиме `legacy`
  (лог `crAuth.mode=legacy`), после — 401. Прямой доступ старых клиентов к Wappi заканчивается ротацией токена
  в кабинете Wappi (owner-шаг после окончания окна) и заменой credential `WappiAuthToken` на сервере.
- **Наследуемые профили** (owner_id null): новый клиент вызывает `profile.claim` → если профиль АВТОРИЗОВАН,
  сервер шлёт 6-значный код в чат самому себе (`message/send` на `phone` из `get/status`), клиент показывает поле
  ввода, `profile.claim.verify` → `owner_id`; если профиль НЕ авторизован — привязка при `authorized_at > claim_at`
  после re-pair через прокси. Оба пути требуют device-проверки (самосообщение WA/TG не подтверждено документацией).
- Артефакт: `Assets/StreamingAssets/secrets.json` теряет `wappiAuthToken`; `Secrets.Data.wappiAuthToken` удаляется
  (компилятор находит все 33 сайта); скрипт `Tools/store/inspect-artifact.sh` проверяет реальный APK/IPA.

## Порядок развёртывания (этап 7 исполняет, этап 1 готовит)
SQL (аддитивно) → env на VPS → новые workflows (Auth Session, Wappi Proxy) → гейтированные версии 12 вебхуков с
compat-окном → шаблоны ботов + обновление клонов → релиз клиента → конец окна (401) → ротация Wappi-токена.
Откат: снять гейт = вернуть предыдущие версии workflows (activeVersion), таблицы аддитивны — данных не теряет.
