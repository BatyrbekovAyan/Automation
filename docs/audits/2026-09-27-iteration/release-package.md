# Пакет выкладки — итерации 2026-09-27 и 2026-09-28 (один пакет)

**Статус: ПОДГОТОВЛЕН, НЕ РАЗВЁРНУТ.** Прод меняется только по явному разрешению владельца на ЭТОТ пакет
(разрешение от 24.09 на него не распространяется). Порядок — сервер раньше клиента. Итоговый отчёт итерации 2026-09-28:
`docs/audits/2026-09-28-iteration/final-report.md`.

Базовая линия: канон на `f08546f1` = прод-состояние от 24.09 (последний до-итерационный workflow-коммит — хотфикс `94374500`,
развёрнут 24.09). Содержимое пакета = ровно диффы `f08546f1..HEAD`: 23 workflow (22 существующих + новый `crPriceSearch001`),
5 миграций к применению, новых переменных окружения и учётных данных НЕТ (проверено по канону: набор `$env.*` и id
credentials тот же, что на `f08546f1`; новый под-workflow использует те же OpenAI/Supabase, что шаблоны).

Ограничения окружения (из памяти проекта): прод-SQL только через `Tools/n8n/.secrets/sql-oneoff.py`; существующие
workflow — `deploy-canonical.py` (dry-run → `--apply --yes`, точка отката versionId, read-back); `Set Workflow State`
и `Support Message` — `build-client-webhooks.py --update`; auto-mode classifier блокирует прод-меняющие команды —
деплой в режиме с подтверждениями; `n8n export:credentials --decrypted` запрещён.

## 1. Миграции БД (additive, idempotent; `if not exists`)

Применять через `sql-oneoff.py`, в этом порядке, ДО workflow:

1. `Tools/n8n/sql/2026-09-27-claim-pairing.sql` (R1) — колонки `bot_profiles.claim_pair_by/at`.
2. `Tools/n8n/sql/2026-09-27-billing-bindings.sql` (R2, V3) — таблица `billing_bindings` + seed.
   **Сначала read-only запрос на дубли** из шапки миграции (ожидается 0 строк; если нет — seed решит по последнему `last_seen`).
3. `Tools/n8n/sql/2026-09-27-reply-order.sql` (R6) — колонка `chat_reply_state.newest_message_time`.
4. `Tools/n8n/sql/2026-10-07-claim-code-attempts.sql` (V2) — колонка `bot_profiles.claim_code_attempts`.
5. `Tools/n8n/sql/2026-10-07-retired-clone-deactivate.sql` (V12) — колонка `bot_profiles.clone_deactivated_id`.

`2026-09-17-reply-events.sql` изменён ТОЛЬКО в комментарии — **переприменять не нужно** (на проде с 24.09).

## 2. Workflow (все — одним заходом)

**2.1. Новый `crPriceSearch001` (Price List Search; V9 + V9-r2) — ПЕРВЫМ, отдельно.** `deploy-canonical.py` новые workflow не
импортирует (печатает `MISSING crPriceSearch001 … import it with n8n import:workflow`). Импорт: `docker exec … n8n
import:workflow --separate` с файлом `Tools/n8n/workflows/crPriceSearch001-Price_List_Search.json` (id сохраняется — на него
ссылаются по литералу), затем `publish:workflow` (под-workflow с триггером «Execute Workflow»; активация через API не нужна),
read-back: `activeVersionId == versionId`. Обязательно ДО шаблонов WA/TG, ДО обновления клонов и ДО Test Dialog — их
инструмент «Supabase Vector Store» теперь toolWorkflow на `crPriceSearch001`; без опубликованного под-workflow поиск по прайсам
у бота и в Test Dialog отвечает ошибкой инструмента. Suggest Replies (панель) несёт ту же функцию сверки в своём Assemble и от
под-workflow не зависит.

**2.2. 22 существующих канонических.** Из них 16 несут общий код гейта (`auth/gate.js`, R3) — выкладывать одной группой,
иначе гейт разойдётся.
- `deploy-canonical.py` (dry-run, затем `--apply --yes`; точка отката — versionId каждого). Он сам подставляет id учётки
  `n8n Admin API` (из живого Set Workflow State) вместо `__HDR_CRED__` и не выкладывает workflow без неё — это касается Test Dialog
  и Profile Lifecycle Sweep.
- `Set Workflow State`, `Support Message` → `build-client-webhooks.py --update` (там же защищённый список, в нём `crPriceSearch001`).
- Точный список к выкладке — строки DIFF из `verify-deployed.py --base <prod>`; он же критерий готовности (§3).

Что везут отдельные workflow (подробности — в CLAUDE.md и `docs/audits/2026-09-28-iteration/`):
- **Auth Session / Wappi Proxy:** R1, R2, R11, V2 (подтверждение владения только кодом), V3 (billing id привязывается только
  на refresh), V12 (отвязка профиля — по владению в БД). V3 выкладывать вместе с новым клиентом: клиент делает refresh сразу после
  bootstrap; A1-сборка без этой части получит billing-идентичность лишь на первом плановом refresh (до ~10 ч). Потеря секрета
  без «Удалить все данные» — отзыв мёртвой установки поддержкой (`docs/audits/a1-deploy-runbook.md` §5).
- **Gated-вебхуки (в т.ч. Get Usage, Dashboard Outcomes, Handoffs, Support Message):** R2/R3 (общий гейт). **V1** (значения,
  попадавшие в SQL через комментарии запросов) — Create WA/TG, Suggest Replies, Auth Session, Delete File, RevenueCat Events;
  страж `verify-sql-comments.py` по всем Postgres-узлам.
- **Upload File + Delete File:** V4 (файл, не зарегистрированный на звонящего, берётся/удаляется только если ВСЕ известные id его
  workflow — из токена). Только вместе с R3: именно R3 делает Register Upload достижимым. Перед выкладкой read-only убедиться, что
  на проде есть индекс `documents_metadata_file_id_idx` (`Tools/n8n/supabase/2026-07-02-harden-rag-store.sql`).
- **Delete Bot Files, Set Workflow State, Profile Lifecycle Sweep:** V12 (удаление бота по владению в БД, честный ответ; чистка
  выключает клон бота, удалённого после триала/подписки; данные хранятся — решение владельца). Миграция §1.5 — до Sweep.
- **Шаблоны WA/TG:** R6, R7, R10, V5, V6, V7, V9 (поиск по прайсам через `crPriceSearch001`).
- **Create/Edit WA/TG:** R8, R8-deletefail (сохранение режима до создания клона; только Create), R14 (карта промптов).
- **Set Reply Mode:** R4, R4-clock (возраст решения считает сервер; без поля — прежнее поведение).
- **Suggest Replies:** R12, R13, R14, V8, V9 (казахский клиент, «ТРИВИАЛЬНЫЕ»), V9-r2 (окна по строкам, жёсткое правило
  только при твёрдой пометке).
- **Handoff Notifier:** R9 (`Settle Lost Sends`), F3 (тик без заявки не зовёт Wappi). После PUT должен остаться АКТИВНЫМ (§5).
- **Test Dialog:** R15, V9.

**2.3. Клоны ботов — инвентарь только по выводу инструмента** (V10). Список клонов берётся ТОЛЬКО из
`verify-deployed.py --base <prod>` (строки `clone` / `archived`), снятого непосредственно перед выкладкой. Последний известный
инвентарь (2026-10-06, после уборки «23» по разрешению владельца, `docs/audits/2026-09-28-iteration/evidence/V12-retired-bot-undeletable.txt`):
27 workflow, из не-канонических только архивный «13», живых клонов нет. Если вывод покажет живые клоны, каждый обновляется
`update-clones.py`: dry-run, затем `--only <id> --apply --yes` по разрешению владельца на этот запуск. Обновление клона = граф
шаблона + ГОЛОВА промпта (вертикаль + `_core` + `_universal` по сегодняшней карте Create/Edit); хвост владельца (Additional
Instructions, About Business, товары, услуги) переносится байт-в-байт; голова определяется по содержанию (история карт в git),
не по метке; головы `custom` не трогаются (только явным `--set-vertical`). Запускать ПОСЛЕ выкладки новых Create/Edit.

## 3. Read-back

`python3 Tools/n8n/verify-deployed.py --base <prod>` → **exit 0**: канонические workflow без DIFF, `crPriceSearch001` на месте
и опубликован, каждый живой клон `AT-CANONICAL` по графу И по голове промпта (сохранённой и опубликованной).
`PROMPT-BEHIND` / `PROMPT-UNPUBLISHED` дают exit 2, как отставший граф. `prompt=custom` — предупреждение, решает владелец.

## 4. Canary на проде (нужен новый бот от нового клиента)

- **Загрузка маленького .txt прайса** (R3 — приоритет: на проде сломано с 24.09): строка `documents` с текстом и id бота,
  ответ `{success:true,fileId}`.
- Вопрос о цене позиции с артикулом — поиск по прайсам отвечает, без ошибки инструмента (V9).
- Test Dialog (R15): ответ по опубликованной версии, без побочных эффектов.
- Заявка → self-message уведомителя (R9/F3 зависят от активности `crHandoffNtfy001`).
- Удаление тестового бота из приложения — сервер подтверждает, бот уходит с телефона (V12).

## 5. Активности

`crHandoffNtfy001` (Handoff Notifier) **обязан быть активен** — на нём держится очистка `send_pending` (R9). Проверить после деплоя.
`fXYpCXPKw92EzRz8` (Profile Lifecycle Sweep) — активен, как и до выкладки.

## 6. Клиент (после сервера)

Новая сборка нужна для R2, R3 (клиентская проверка тела), R4/R4-clock (клиентская часть), R5, R11, R16, V3, V11, V12 (удаление
по вердикту сервера, «Удалить только с телефона»). Device-pass до релиза: холодный старт с неотправленным режимом (R5), строка ID
(R16), три состояния окна удаления бота (V12), «Проверить бота» — закрыть до ответа (V11), «Удалить все данные» сохраняет
установку (V3).

**Одобренная App Store сборка 1.0 (4) — клиент ДО A1** (собрана 2026-09-16 на `fa4eecb8`, A1-клиент — `5448f98d`, 2026-09-17):
она берёт мастер-токен Wappi из `secrets.json` внутри приложения и ходит на сервер по бестокенному legacy-пути, который
открыт только до `CR_AUTH_COMPAT_UNTIL` (2026-12-31). Legacy-ветки сервера в пакете сохранены и покрыты тестами (полного
прогона запросов именно 1.0 (4) против пакета не делали), но публичный релиз
именно этой сборки выносит токен Wappi в открытый доступ (извлекается из IPA; проверка — `Tools/store/inspect-artifact.sh`) и
даёт приложение, которое перестанет работать при закрытии окна. Решение владельца; рекомендация — в итоговом отчёте.

## 7. Откат

- Workflow: `deploy-canonical.py` печатает versionId до каждого PUT — откат восстановлением предыдущей версии.
  `update-clones.py` для клона делает то же (`rollback pointer: versionId …`). `crPriceSearch001` — новый: откат шаблонов на
  прежнюю версию снимает зависимость от него, сам он может остаться.
- Миграции additive (новые таблица/колонки) — на старый код влияния не имеют; откат не требуется.
- Клиент: не выкладывать до подтверждения сервера; откат = прежний билд.

## 8. Порядок

1) read-only проверка дублей (R2) и индекса `documents_metadata_file_id_idx` (V4) → 2) миграции §1 по порядку →
3) импорт + `publish:workflow` `crPriceSearch001`, read-back → 4) workflow-группа (`deploy-canonical`: шаблоны, Create/Edit,
gated, Test Dialog, Sweep, Notifier) + `build-client-webhooks --update` → 5) инвентарь клонов по `verify-deployed`; для каждого
живого клона `update-clones` dry-run → `--only <id> --apply --yes` → 6) read-back `verify-deployed` exit 0 → 7) активность
`crHandoffNtfy001` → 8) canary §4 → 9) только затем сборка и раскатка клиента.

## Вне пакета (решения владельца, записаны в `docs/audits/2026-09-28-iteration/README.md`)

- **M4** — бесконечный `claim.start` у бота, удалённого чисткой: закрыть до выпуска нового клиента.
- **F1** — лишние клоны при повторной авторизации и сбоях после Create.
- **F2** — нет пути повторного подключения после покупки.
- **Q1** — бот не всегда опирается на договорённость владельца из истории чата (давнее, есть на проде; итерация промпта).
