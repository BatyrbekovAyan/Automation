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
4. `Tools/n8n/sql/2026-10-07-claim-code-attempts.sql` (итерация 2026-09-28, V2) — колонка `bot_profiles.claim_code_attempts`;
   ДО `crAuthSession001` (на неё ссылаются Claim Start / Store / Verify / Clear Claim Code).

`2026-09-17-reply-events.sql` в этой итерации изменён ТОЛЬКО в комментарии — **переприменять не нужно** (уже на проде с 24.09).

## 2. Workflow (все изменённые — вместе, одним заходом)

20 канонических workflow отличаются от прода (`git diff --name-only f08546f1..HEAD -- Tools/n8n/workflows/`). Большинство —
16 gated, перегенерированных из общего `auth/gate.js` (R3): их ОБЯЗАТЕЛЬНО выкладывать одной группой, иначе гейт разойдётся.

- Существующие канонические → `deploy-canonical.py` (dry-run, затем `--apply --yes`; точка отката — versionId каждого).
- `Set Workflow State`, `Support Message` → `build-client-webhooks.py --update`.
- **Новый workflow `crPriceSearch001` (Price List Search; итерация 2026-09-28, V9 + V9-r2) — импортируется ОТДЕЛЬНО и ПЕРВЫМ.**
  `deploy-canonical.py` новые workflow не импортирует (печатает `MISSING crPriceSearch001 … import it with n8n import:workflow`).
  Импорт: `docker exec … n8n import:workflow --separate` с файлом `Tools/n8n/workflows/crPriceSearch001-Price_List_Search.json`
  (id сохраняется — на него ссылаются по литералу), затем `publish:workflow` (это под-workflow с триггером «Execute Workflow»;
  активация через API не нужна), read-back: `activeVersionId == versionId`. Порядок обязателен: ДО шаблонов WA/TG, ДО обновления
  клонов (`update-clones.py`) и ДО Test Dialog — их инструмент «Supabase Vector Store» теперь toolWorkflow на `crPriceSearch001`;
  без опубликованного под-workflow поиск по прайсам у бота и в Test Dialog отвечает ошибкой инструмента. Идентификатор — в
  защищённом списке Set Workflow State (`build-client-webhooks.py --update`). Suggest Replies (панель) несёт ту же функцию сверки
  в своём Assemble и от под-workflow не зависит. Других новых workflow с фиксированными id в этой итерации нет.
- **Клоны ботов — инвентарь только по выводу инструмента** (итерация 2026-09-28, V10). Прежняя строка «клонов нет, обновлять
  нечего» была перенесена из журнала деплоя 24.09 и на 28.09 была ложной: «23» / `dt6hHFUVrW07unTj` был активен, с графом
  и головой промпта шаблона 24.09. Поэтому список клонов берётся ТОЛЬКО из `verify-deployed.py --base <prod>` (строки
  `clone` / `archived`), снятого непосредственно перед выкладкой. Последний известный инвентарь (2026-10-06, после ручной
  уборки «23» по разрешению владельца, `docs/audits/2026-09-28-iteration/evidence/V12-retired-bot-undeletable.txt`):
  27 workflow, из не-канонических только архивный «13», живых клонов нет. Если вывод покажет живые клоны, каждый
  обновляется `update-clones.py`: dry-run, затем `--only <id> --apply --yes` по разрешению владельца на этот запуск.
  Обновление клона = граф шаблона + ГОЛОВА промпта (вертикаль + `_core` + `_universal` по сегодняшней карте Create/Edit).
  Хвост владельца (Additional Instructions, About Business, товары, услуги) переносится байт-в-байт. Голова определяется по
  содержанию (история карт в git), а не по метке `Business Type:`. Головы `custom` (правленые руками, ни одна карта их не
  писала) не трогаются; заменить такую голову можно только явно, `--set-vertical <id>=<вертикаль>`, по решению владельца.
  Запускать ПОСЛЕ выкладки новых Create/Edit: иначе следующее сохранение настроек владельцем через старую карту вернёт
  старую голову. Инструмент печатает версию карты, которую применяет.
- **Upload File + Delete File несут V4** (итерация 2026-09-28: файл, не зарегистрированный на звонящего, берётся/удаляется
  только если ВСЕ известные id его workflow — из токена). Выкладываются только вместе с R3: именно R3 делает Register Upload
  достижимым. Миграций нет; перед выкладкой read-only убедиться, что на проде есть индекс `documents_metadata_file_id_idx`
  (`Tools/n8n/supabase/2026-07-02-harden-rag-store.sql`) — обе проверки читают `documents` по `fileId`.
- **V12** (итерация 2026-09-28, 2026-10-07: удаление бота владельцем после окончания триала, чистка клонов): SQL
  `Tools/n8n/sql/2026-10-07-retired-clone-deactivate.sql` ДО Profile Lifecycle Sweep; Delete Bot Files, Wappi Proxy и Sweep —
  `deploy-canonical.py` (он теперь сам подставляет id `n8n Admin API` из живого Set Workflow State вместо `__HDR_CRED__` и
  не выкладывает workflow без него — это касается и Test Dialog); Set Workflow State — `build-client-webhooks.py --update`.
  Сервер раньше нового клиента (клиент удаляет бота только по вердикту сервера и полагается на 503-vs-403 и идемпотентный
  delete). Данные ботов, удалённых Sweep'ом, хранятся (решение владельца); бэкфилла нет — живых клонов нет.
- **Auth Session несёт V3** (итерация 2026-09-28: billing id привязывается ТОЛЬКО на refresh — bootstrap ничего не
  привязывает; переходит ТОЛЬКО от держателя, отозванного сервером, «у держателя нет живых ботов» больше не освобождает).
  Тот же узел, что R2: миграция `2026-09-27-billing-bindings.sql` до него, без изменений. Выкладывать вместе с новым
  клиентом: клиент делает refresh сразу после bootstrap; A1-сборка без этой части получит billing-идентичность лишь на
  первом плановом refresh (до ~10 ч). Потеря секрета без «Удалить все данные» — отзыв мёртвой установки поддержкой
  (`docs/audits/a1-deploy-runbook.md` §5).
- **Handoff Notifier несёт R9 и F3** (итерация 2026-09-28, 2026-10-08: гейт `Due Request?` — тик без заявки к отправке или со сбоем БД больше не зовёт Wappi `get/status` без профиля и не пишет Record Notify; ветка R9 без изменений) — `deploy-canonical.py`, после PUT workflow должен остаться активным (§5).
- Точный список к выкладке брать из `verify-deployed.py --base <prod>` (строки DIFF) — он же критерий готовности.

Затрагиваемые группы: Auth Session, Wappi Proxy, все gated вебхуки, оба шаблона ботов, Create/Edit WA+TG, Suggest Replies,
Upload File, Delete File, Delete Bot Files, Dashboard Outcomes, Get Usage, Set Reply Mode, Handoffs, Handoff Notifier, Test Dialog,
Profile Lifecycle Sweep (V12), Price List Search `crPriceSearch001` (V9 — новый, импорт отдельно, см. выше).

## 3. Read-back

`python3 Tools/n8n/verify-deployed.py --base <prod>` → **exit 0**. Это значит: канонические workflow без DIFF, и каждый живой
клон `AT-CANONICAL` по графу И по голове промпта, сохранённой и опубликованной. `PROMPT-BEHIND` / `PROMPT-UNPUBLISHED`
дают exit 2, как отставший граф. `prompt=custom` — предупреждение и не блокирует; по каждому такому клону решает владелец.

## 4. Canary на проде (нужен новый бот от нового клиента)

- **Загрузка маленького .txt прайса** (R3 — приоритет: сломано с 24.09): строка `documents` с текстом и id бота, ответ
  `{success:true,fileId}`.
- Test Dialog (R15): ответ по опубликованной версии, без побочных эффектов.
- Заявка → self-message уведомителя (R9 зависит от активности `crHandoffNtfy001`).

## 5. Активности

`crHandoffNtfy001` (Handoff Notifier) **обязан быть активен** — на нём держится очистка `send_pending` (R9). Проверить после деплоя.

## 6. Клиент (после сервера)

Новая сборка нужна для R2, R3 (клиентская проверка тела), R4 (клиентская часть), R5, R11, R16, V3 (refresh после
bootstrap, повтор при возврате в приложение, «Удалить все данные» сохраняет установку). Старые билды (в т.ч. на
ревью App Store) живут в compat-окне (`CR_AUTH_COMPAT_UNTIL=2026-12-31`) на прямом Wappi-токене и продолжают работать.
Device-pass до релиза: холодный старт R5, строка ID R16.

## 7. Откат

- Workflow: `deploy-canonical.py` печатает versionId до каждого PUT — откат восстановлением предыдущей версии.
  `update-clones.py` для клона делает то же (`rollback pointer: versionId …`).
- Миграции additive (новые таблица/колонки, `if not exists`) — на старый код влияния не имеют; откат не требуется, при
  желании — drop новых объектов (на них пока никто не ссылается).
- Клиент: не выкладывать до подтверждения сервера; откат = прежний билд.

## 8. Порядок

1) read-only проверка дублей (R2) → 2) миграции §1 по порядку → 3) импорт + `publish:workflow` нового `crPriceSearch001` (V9),
read-back → 4) workflow-группа (`deploy-canonical`: шаблоны, Create/Edit с новой картой промптов, gated, Test Dialog) +
`build-client-webhooks --update` → 5) инвентарь клонов по `verify-deployed`; для каждого
живого клона `update-clones` dry-run → `--only <id> --apply --yes` → 6) read-back `verify-deployed` exit 0 (канон + клоны по
графу и промпту) → 7) активность `crHandoffNtfy001` → 8) canary (.txt upload, Test Dialog, заявка; на обновлённом клоне —
входящее → `sent`, Test Dialog отвечает по опубликованной версии с новой головой; вопрос о цене позиции с артикулом —
поиск по прайсам отвечает, без ошибки инструмента) → 9) только затем сборка и раскатка клиента.

## Отдельно (вне полосы этой итерации, решение владельца)

Формат SQL-запросов в ряде узлов (`$N` в теле) — потенциальный риск при некоторых значениях; воспроизведение и разбор:
`evidence/S1-sql-comment-injection-staging.txt`. Узкий фикс и его выкладку выполнять отдельно и только с разрешения владельца;
в объём R1–R16 не входит.
