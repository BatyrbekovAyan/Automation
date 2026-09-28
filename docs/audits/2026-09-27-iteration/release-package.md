# Пакет выкладки — итерация 2026-09-27

**Статус: ПОДГОТОВЛЕН, НЕ РАЗВЁРНУТ.** Прод меняется только по явному разрешению владельца на ЭТОТ пакет
(разрешение от 24.09 на него не распространяется). Порядок — сервер раньше клиента.

Базовая линия: канон на `f08546f1` (stage 0) = прод-состояние от 24.09 (последний до-итерационный workflow-коммит —
хотфикс `94374500`, развёрнут 24.09). Значит содержимое пакета = ровно диффы `f08546f1..HEAD`.

Ограничения окружения (из памяти проекта): прод-SQL только через `Tools/n8n/.secrets/sql-oneoff.py`; существующие
workflow — `deploy-canonical.py` (dry-run → `--apply --yes`, точка отката versionId, read-back); `Set Workflow State`
и `Support Message` — `build-client-webhooks.py --update`; auto-mode classifier блокирует прод-меняющие команды —
деплой в режиме с подтверждениями; `n8n export:credentials --decrypted` запрещён.

## 1. Миграции БД (новые, additive/idempotent)

Применять через `sql-oneoff.py`, в порядке:

1. `Tools/n8n/sql/2026-09-27-claim-pairing.sql` (R1) — колонки `claim_pair_by/at`.
2. `Tools/n8n/sql/2026-09-27-billing-bindings.sql` (R2) — таблица `billing_bindings` + seed.
   **Сначала read-only запрос на дубли** из шапки миграции (ожидается 0 строк; если нет — seed решит по последнему `last_seen`).
3. `Tools/n8n/sql/2026-09-27-reply-order.sql` (R6) — колонка `chat_reply_state.newest_message_time`.

`2026-09-17-reply-events.sql` в этой итерации изменён ТОЛЬКО в комментарии — **переприменять не нужно** (уже на проде с 24.09).

## 2. Workflow (все изменённые — вместе, одним заходом)

20 канонических workflow отличаются от прода (`git diff --name-only f08546f1..HEAD -- Tools/n8n/workflows/`). Большинство —
16 gated, перегенерированных из общего `auth/gate.js` (R3): их ОБЯЗАТЕЛЬНО выкладывать одной группой, иначе гейт разойдётся.

- Существующие канонические → `deploy-canonical.py` (dry-run, затем `--apply --yes`; точка отката — versionId каждого).
- `Set Workflow State`, `Support Message` → `build-client-webhooks.py --update`.
- Новых workflow с фиксированными id в этой итерации НЕТ (`import:workflow` не требуется).
- Клоны живых ботов на проде отсутствуют («13» архивирован, «15» удалён) — `update-clones.py` применять нечего; новый бот
  от нового клиента соберётся уже из новых шаблонов.
- Точный список к выкладке брать из `verify-deployed.py --base <prod>` (строки DIFF) — он же критерий готовности.

Затрагиваемые группы: Auth Session, Wappi Proxy, все gated вебхуки, оба шаблона ботов, Create/Edit WA+TG, Suggest Replies,
Upload File, Delete File, Delete Bot Files, Dashboard Outcomes, Get Usage, Set Reply Mode, Handoffs, Handoff Notifier, Test Dialog.

## 3. Read-back

`python3 Tools/n8n/verify-deployed.py --base <prod>` → **exit 0** (canonical без DIFF, клонов нет).

## 4. Canary на проде (нужен новый бот от нового клиента)

- **Загрузка маленького .txt прайса** (R3 — приоритет: сломано с 24.09): строка `documents` с текстом и id бота, ответ
  `{success:true,fileId}`.
- Test Dialog (R15): ответ по опубликованной версии, без побочных эффектов.
- Заявка → self-message уведомителя (R9 зависит от активности `crHandoffNtfy001`).

## 5. Активности

`crHandoffNtfy001` (Handoff Notifier) **обязан быть активен** — на нём держится очистка `send_pending` (R9). Проверить после деплоя.

## 6. Клиент (после сервера)

Новая сборка нужна для R2, R3 (клиентская проверка тела), R4 (клиентская часть), R5, R11, R16. Старые билды (в т.ч. на
ревью App Store) живут в compat-окне (`CR_AUTH_COMPAT_UNTIL=2026-12-31`) на прямом Wappi-токене и продолжают работать.
Device-pass до релиза: холодный старт R5, строка ID R16.

## 7. Откат

- Workflow: `deploy-canonical.py` печатает versionId до каждого PUT — откат восстановлением предыдущей версии.
- Миграции additive (новые таблица/колонки, `if not exists`) — на старый код влияния не имеют; откат не требуется, при
  желании — drop новых объектов (на них пока никто не ссылается).
- Клиент: не выкладывать до подтверждения сервера; откат = прежний билд.

## 8. Порядок

1) read-only проверка дублей (R2) → 2) три миграции → 3) workflow-группа + `build-client-webhooks --update` →
4) read-back `verify-deployed` exit 0 → 5) активность `crHandoffNtfy001` → 6) canary (.txt upload, Test Dialog, заявка) →
7) только затем сборка и раскатка клиента.

## Отдельно (вне полосы этой итерации, решение владельца)

Формат SQL-запросов в ряде узлов (`$N` в теле) — потенциальный риск при некоторых значениях; воспроизведение и разбор:
`evidence/S1-sql-comment-injection-staging.txt`. Узкий фикс и его выкладку выполнять отдельно и только с разрешения владельца;
в объём R1–R16 не входит.
