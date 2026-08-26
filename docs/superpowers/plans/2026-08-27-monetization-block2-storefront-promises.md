# Блок 2 «обещания витрины» — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Закрыть все строки пейволла «Во всех тарифах» (алерты, недельный отчёт, Сводка «Всё время» + CSV, расписание, голосовые в «Вместе») и пять технических хвостов Блока 1 — по спеке `docs/superpowers/specs/2026-08-27-monetization-block2-storefront-promises-design.md`.

**Architecture:** Вся детекция — серверная (n8n, 10-минутный Alert Sweep поверх готовой классификации Сводки; горячий путь ботов трогается ровно один раз — гейтом расписания). Доставка — «сообщение себе» через собственный Wappi-профиль клиента (несущее допущение, проверяется Task 0). Клиент получает только тумблеры, UI расписания, экспорт и сверку профилей — всё через pure-seam + EditMode-тесты, паттерны Блока 1.

**Tech Stack:** n8n (localhost:5678 dev) + Supabase Postgres (cred `vvRrFiEXzLVqKjOx`) + Wappi api/tapi + Unity 6 C# (EditMode-тесты, additive-билдеры).

## Global Constraints

- Канонические JSONы в `Tools/n8n/workflows/` НИКОГДА не абсорбируют dev-значения (localhost/trycloudflare/dev-cred/active-флаги) — правки только хирургией от закоммиченного базового файла; `python3 Tools/n8n/verify-telegram-parity.py` обязан печатать `ALL PARITY ASSERTS PASSED` после каждой серверной задачи; live == canonical проверяется по нодам после publish.
- Новые парити-ассерты НЕГАТИВНО тестируются (мутация на scratch-копии через `--dir` → обязан быть `PARITY FAIL`, exit 1), включая `disabled: true` на новых нодах (ловушка Task 19: выключенная нода проходит все структурные проверки и прикидывается работающей в runData).
- Пробы (`Tools/n8n/probe-billing.py` и новые) ассертят ЗНАЧЕНИЯ и ветку выхода, никогда — присутствие ноды; фикстуры `probe2_*` создаются и удаляются пробой; счёт владельца (`$RCAnonymousID:c333c6a5…c2b9a`, профиль `dec53892-d97f`, клон `jICKoC6QKucHcryV`) не трогать.
- Гейт доставки: алерты/отчёты только аккаунтам `subscribers.status in ('active','trialing','grace')`. Часовой пояс всех временных правил — литерал `Asia/Almaty` (как в Count Dialog).
- RU-only UI: `RuPlural.Pick` для счётных существительных; никаких `ToString` через ambient-культуру (`CultureInfo.InvariantCulture` на числах); NBSP U+00A0 в суммах через `PaywallCopy.Number`; в тест-литералах ` `-эскейпы; кириллицу в правках вносить python-байтовыми правками, если Edit-вывод деградирует.
- Unity: PlayerPrefs-ключи per bot по `transform.name`; билдеры ТОЛЬКО аддитивные (destructive `Tools/Rebuild Bot Settings Prefabs` запрещён); сцена коммитится СРАЗУ после прогона билдера; сериализованные ссылки через SerializedObject; Editor открыт → mcp-unity `run_tests` с ТОЧНЫМ фильтром класса (10-сек ложный таймаут — читать Unity-сторону), recompile через mcp-unity + поллинг mtime `Library/ScriptAssemblies`; полный сьют перед пушем через бридж (`Temp/claude/run-tests.trigger`, гейт на `total > 0`).
- Никаких секретов в коммитах; RC/Wappi/n8n ключи — `Assets/StreamingAssets/secrets.json` (gitignored) и n8n-credentials; печатать значения запрещено.
- Коммиты: точечные пути (`git add <files>`), трейлер `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`, БЕЗ push (консент на пуш — по завершении блока).
- Каждая задача: отчёт `.superpowers/sdd/task-B2-<N>-report.md` + ≤15 строк резюме; находки — в `.superpowers/sdd/progress.md`.

---

## File Structure (создаваемое/изменяемое)

**n8n (канон + live dev):**
- `Tools/n8n/workflows/<id>-Alert_Sweep.json` — новый (Task 3a/3b)
- `Tools/n8n/workflows/<id>-Weekly_Report.json` — новый (Task 4)
- `Tools/n8n/workflows/<id>-Set_Alert_Prefs.json` — новый (Task 1)
- `Tools/n8n/workflows/<id>-Set_Schedule.json` — новый (Task 5)
- `Tools/n8n/workflows/<id>-Verify_Identity.json` — новый (Task 11)
- `4wYitz5ek30SVNlT-WhatsApp_Bot.json` + `4VN3gsFaC2HUYmcc-Telegram_Bot.json` — узел `Schedule Gate` (Task 5)
- `9PTyYcelRQI7bGDb-Suggest_Replies.json` — транскрипция + кэш (Task 7)
- `ZGYr6srzS3rSSXHp-RevenueCat_Events.json` — журнал `rc_events` (Task 10), снапшот-фолбэк (Task 14)
- `Tools/n8n/sql/2026-08-27-block2-*.sql` — миграции (по одной на задачу, см. задачи)
- `Tools/n8n/probe-block2.py` — новая проба блока (self-send, sweep, report, schedule, voice, verify)
- `Tools/n8n/verify-telegram-parity.py` — новые ассерты (Schedule Gate, транскрипция, rc_events-гейт)

**Unity:**
- `Assets/Scripts/Main/ProfileSubPages.Notifications.cs` — 4 тумблера алертов (Task 2)
- `Assets/Scripts/Main/AlertPrefs.cs` — новый (Task 2, паттерн NotifPrefs + синк)
- `Assets/Scripts/Billing/ScheduleModel.cs` — новый pure seam (Task 6)
- `Assets/Scripts/Main/BotSettings.Schedule.cs` — новый partial (Task 6)
- `Assets/Editor/BotScheduleBuilder.cs` — новый additive-билдер (Task 6)
- `Assets/Scripts/Main/Dashboard/DashboardMetrics.cs` + `DashboardPage.cs` + `DashboardPageBuilder.cs` — «Всё время» (Task 8)
- `Assets/Scripts/Main/Dashboard/DashboardCsv.cs` — новый pure seam (Task 9)
- `Assets/Scripts/Billing/ProfileReconcile.cs` — новый pure seam + `Manager` sweep-корутина (Task 12)
- `Assets/Scripts/Billing/SubscriptionPageRows.cs` + `ProfileSubPages.Subscription.cs` (Task 13)
- `Assets/Scripts/Billing/BillingService.cs`/`PaywallController.cs` — identity-verify триггер (Task 11, клиентская половина)
- Тесты: `Assets/Tests/Editor/Billing/` (по классу на seam)

---

### Task 0: Спайк self-send — ГЕЙТ ВСЕГО БЛОКА

**Files:** Create: `Tools/n8n/probe-block2.py` (режим `--selfsend`).

**Interfaces:** Produces: подтверждённый рецепт self-send для обеих баз — точный URL/тело `message/send` и формат recipient'а «сам себе» (WA: свой номер из `profile/all/get`; TG: собственный id/username через `tapi` профиль) — записывается в отчёт и в шапку `probe-block2.py` как константы-комментарии.

- [ ] Скриптом (используя `wappiAuthToken` из secrets.json, профили — СОЗДАТЬ одноразовый пробный профиль НЕЛЬЗЯ без авторизации: использовать живой профиль владельца `dec53892-d97f` ТОЛЬКО для чтения его собственного номера; отправку самому себе выполнить С СОГЛАСИЯ владельца на один тестовый месседж — контроллер спрашивает владельца перед прогоном) проверить: `POST {base}/sync/message/send` c `recipient = <свой номер>` доставляет сообщение в «Сообщение себе».
- [ ] Проверить оба варианта поведения: сообщение видно в списке чатов приложения? (наш ChatManager получит чат с самим собой — зафиксировать, фильтруется ли он `fromMe`-логикой пайплайна бота: ожидание — бот-воркфлоу его игнорирует, т.к. `fromMe=true`; если НЕТ — зафиксировать как риск для Task 3).
- [ ] Результат в `.superpowers/sdd/task-B2-0-report.md`: рецепт обеих баз ИЛИ вердикт «невозможно» → СТОП блока, решение (алерт-бот) возвращается владельцу.
- [ ] Commit: `feat(n8n): probe-block2 --selfsend — delivery seam recipe`

### Task 1: Схема преференций + вебхук Set Alert Prefs

**Files:** Create: `Tools/n8n/sql/2026-08-27-block2-alert-prefs.sql`, `Tools/n8n/workflows/<id>-Set_Alert_Prefs.json`. Modify: `Tools/n8n/probe-block2.py` (режим `--prefs`), `Tools/n8n/README.md` (таблица воркфлоу).

**Interfaces:** Produces: таблица `alert_prefs(app_user_id text primary key, ready_to_buy boolean not null default true, owner_needed boolean not null default true, channel_state boolean not null default true, weekly_report boolean not null default true, updated_at timestamptz not null default now())` + RLS deny-all по паттерну биллинг-схемы; вебхук `POST /webhook/SetAlertPrefs`, тело `{"appUserId": string, "readyToBuy": bool, "ownerNeeded": bool, "channelState": bool, "weeklyReport": bool}` → upsert on conflict do update → `{"success": true}`; малформ (пустой appUserId / не-bool) → `{"success": false, "error": "bad_request"}` БЕЗ записи (паттерн Set Reply Mode).

- [ ] Миграция применена SQL-харнессом (паттерн Task 16), post-check-запросы в файле миграции.
- [ ] Воркфлоу по образцу `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` (валидация → один upsert массив-формой `queryReplacement` — НЕ запятая-форма, ловушка Task 7 Блока 1), Postgres cred по id `vvRrFiEXzLVqKjOx`.
- [ ] `--prefs` проба: upsert значения → чтение через прямой SQL-харнесс → точные значения; малформ → bad_request и нет строки; повторный upsert меняет только updated_at-поля. Фикстура `probe2_prefs_*` удаляется.
- [ ] Parity green; publish; live == canonical. Commit: `feat(n8n): alert prefs — schema + SetAlertPrefs webhook`

### Task 2: Тумблеры алертов в «Уведомлениях» (Unity)

**Files:** Create: `Assets/Scripts/Main/AlertPrefs.cs`, `Assets/Tests/Editor/Billing/AlertPrefsTests.cs`. Modify: `Assets/Scripts/Main/ProfileSubPages.Notifications.cs`, `Assets/Editor/ProfileSubPagesBuilder.cs` — НЕТ: builder деструктивный для этой страницы? ПРОВЕРИТЬ: `ProfileSubPagesBuilder` пере-эмитит страницы — тумблеры добавлять АДДИТИВНЫМ мини-билдером `Assets/Editor/AlertPrefsTogglesBuilder.cs` (Create), клонируя существующий `ToggleRow` страницы «Уведомления» (паттерн BusinessContactFieldsBuilder — клонировать соседа, не строить с нуля).

**Interfaces:** Consumes: вебхук Task 1. Produces: `static class AlertPrefs` (паттерн `NotifPrefs`: PlayerPrefs-ключи `AlertPrefReadyToBuy|OwnerNeeded|ChannelState|WeeklyReport`, default true, инъектируемые Func/Action-семы для тестов) + `AlertPrefs.SyncToServer(MonoBehaviour host)` — корутина POST на `{n8nBaseUrl}/webhook/SetAlertPrefs` c `BillingIdentity.AppUserId`, fire-and-forget с одним ретраем; вызывается при каждом изменении тумблера.

- [ ] Тесты первым: дефолты true; флип пишет ключ; композиция тела запроса (pure-функция `AlertPrefs.PayloadJson()` — точный JSON, ассерт строкой).
- [ ] 4 `ToggleRow` на странице «Уведомления» (заголовки RU: «Клиент готов купить», «Нужен владелец», «Канал отключился», «Недельный отчёт», секция-заголовок «Алерты в мессенджер»), builder аддитивен+идемпотентен, сцена коммитится сразу.
- [ ] mcp-unity run_tests `AlertPrefsTests` зелёный; Game-view скрин самопроверкой. Commit: `feat(billing-ui): alert toggles in Notifications + server sync`

### Task 3a: Alert Sweep — скелет + алерты по исходам

**Files:** Create: `Tools/n8n/workflows/<id>-Alert_Sweep.json`, `Tools/n8n/sql/2026-08-27-block2-first-reply-at.sql` (сюда же — колонка для Task 4: `alter table dialog_counts add column if not exists first_reply_at timestamptz not null default now();`). Modify: `Tools/n8n/probe-block2.py` (`--sweep-outcomes`), `Tools/n8n/README.md`.

**Interfaces:** Consumes: `alert_prefs` (Task 1), рецепт self-send (Task 0), классификационная цепочка `2htWSV5IHO8E2CgB-Dashboard_Outcomes.json` (Find Changed Sessions → Classify → Parse → Aggregate → Apply Silence Rule → Upsert Outcomes — скопировать узлы, НЕ трогая сам Dashboard Outcomes). Produces: воркфлоу `Alert Sweep` (Schedule Trigger `*/10 * * * *`, active), для Task 3b — его скелет и таблица доставки.

Логика: `Eligible Accounts` (Postgres: `select s.app_user_id from subscribers s where s.status in ('active','trialing','grace') and exists (select 1 from bot_profiles bp where bp.app_user_id = s.app_user_id and bp.deleted_at is null)`) → профили аккаунта → классификация изменившихся сессий (как в Dashboard Outcomes) → **до** `Upsert Outcomes` узел `Detect Transitions` (Code): по каждой классифицированной сессии прочитать текущий `conversation_outcomes.outcome`; алерт-кандидат если новый исход ∈ {`order_collected`,`owner_needed`} И старый ≠ новому. → `Upsert Outcomes` → `Prefs Gate` (join alert_prefs: `ready_to_buy`/`owner_needed`) → `Compose Alert` (Code, RU-тексты: `🔥 Клиент готов купить`/`⚠️ Нужен владелец` + `+7…` из chatId без `@c.us` + «бот …» не доступен серверу — НЕ включать имя бота, только канал; резюме из классификации в кавычках) → `Send Self` (HTTP по рецепту Task 0; WA-профиль аккаунта приоритетно, иначе TG; `onError: continueRegularOutput` — сбой доставки не валит свип).

- [ ] `--sweep-outcomes` проба: сеять фикстуру диалога с изменившейся сессией (паттерн e2e из `project_n8n_postgres_node_gotchas` — сеять РЕАЛЬНУЮ плоскую форму `{type,content}`), прогнать live-исполнение, ассертить: (1) переход в order_collected → ровно ОДИН Send Self c точным текстом; (2) повторный прогон без изменений → ноль отправок (дедуп переходом); (3) prefs off → ноль отправок, но Upsert Outcomes ПРОИЗОШЁЛ (классификация не зависит от преференций); (4) expired-аккаунт → не классифицируется вовсе.
- [ ] Отправку в пробе НЕ слать владельцу: `Send Self` в пробном прогоне направлять на фикстурный несуществующий профиль и ассертить попытку вызова + tolerated-ошибку (паттерн Task 19 `--refused-delete`).
- [ ] Parity green (+ассерт: Alert Sweep не содержит dev-URL; классификационные ноды байт-совпадают с Dashboard Outcomes — щит от дрейфа копии). Publish. Commit: `feat(n8n): Alert Sweep — outcome transition alerts`

### Task 3b: Alert Sweep — алерты каналов

**Files:** Create: `Tools/n8n/sql/2026-08-27-block2-channel-snapshot.sql`. Modify: `<id>-Alert_Sweep.json`, `Tools/n8n/probe-block2.py` (`--sweep-channels`).

**Interfaces:** Consumes: скелет Task 3a. Produces: `channel_snapshot(profile_id text primary key, channel text not null, app_user_id text not null, authorized boolean not null, seen_at timestamptz not null default now())`.

Логика в том же свипе, отдельная ветка от Schedule Trigger: `List WA/TG Profiles` (`profile/all/get`, оба base, `onError: continueRegularOutput` + retry — паттерн Lifecycle Sweep Branch B) → `Diff Channel State` (Code: join к `bot_profiles` alive для app_user_id; сравнение с `channel_snapshot`; переходы true→false = «отключился», false→true = «снова в сети»; НОВЫЙ профиль без снапшота — записать, НЕ алертить) → upsert снапшота → prefs gate (`channel_state`) → Compose (RU: `🔌 {WhatsApp|Telegram} отключился. Бот не отвечает — переподключите канал в приложении.` / `✅ {WhatsApp|Telegram} снова в сети.`) → Send Self **через ДРУГОЙ живой канал** (down-канал сам себя не доставит); другого канала нет → пропуск отправки (снапшот всё равно обновлён).
- [ ] Пустой/невалидный список профилей от Wappi → вся ветка каналов СКИПАЕТСЯ без записи снапшота (empty-list floor, ловушка Task 12 Блока 1 — иначе один сбой Wappi «отключит» всем каналы).
- [ ] `--sweep-channels`: фикстурные строки снапшота + bot_profiles → диф → точные тексты/направления; recovery-переход; new-profile-no-alert; empty-list floor негативно.
- [ ] Parity + publish. Commit: `feat(n8n): Alert Sweep — channel up/down alerts`

### Task 4: Недельный отчёт

**Files:** Create: `Tools/n8n/workflows/<id>-Weekly_Report.json`. Modify: `Tools/n8n/probe-block2.py` (`--weekly`), README.

**Interfaces:** Consumes: `first_reply_at` (Task 3a-миграция), delivery seam, `alert_prefs.weekly_report`, `conversation_outcomes`, `dialog_counts`, `Get_Usage`-квоты (карта квот дублируется НЕ вручную: quota-CASE скопировать из Get Usage и добавить парити-ассерт байт-совпадения). Produces: воркфлоу `Weekly Report`, Schedule Trigger `0 9 * * 1` TZ `Asia/Almaty`.

Одна SQL-агрегация на аккаунт за прошлую Пн–Вс неделю (границы по `Asia/Almaty`): диалогов всего = count dialog_counts; ночных = count where `(first_reply_at at time zone 'Asia/Almaty')::time >= '22:00' or < '08:00'`; исходы = counts по conversation_outcomes c `outcome_at` в окне; квота = used текущего месяца против плановой + `topup_balance`. Ноль диалогов за неделю → аккаунт пропускается. Текст RU (`RuPlural`-логика на сервере — Code-узел с той же тройной формой, тест в пробе): заголовок `📊 Неделя {d1}–{d2}`, строки «Диалогов: N (ночью: M)», «Заказы: N · Нужен владелец: N · Закрыто: N», «Квота: X из Y» (+ « · резерв: Z» если >0).
- [ ] `--weekly`: фикстуры на границе недели и границе 22:00/08:00 → точный текст; ноль-аккаунт пропущен; prefs off → пропущен; RuPlural-формы (1/2/5 диалогов) ассертятся строкой.
- [ ] Parity + publish. Commit: `feat(n8n): weekly report to self`

### Task 5: Расписание — сервер (схема, вебхук, гейт в шаблонах)

**Files:** Create: `Tools/n8n/sql/2026-08-27-block2-bot-schedule.sql`, `Tools/n8n/workflows/<id>-Set_Schedule.json`. Modify: оба бот-шаблона (узел `Schedule Gate`), `verify-telegram-parity.py` (ассерты), `probe-block2.py` (`--schedule`).

**Interfaces:** Produces: `bot_schedule(profile_id text primary key, enabled boolean not null, days_mask int not null, start_min int not null, end_min int not null, updated_at timestamptz not null default now())` (days_mask бит 0 = Пн … бит 6 = Вс; start_min/end_min ∈ [0,1440)); вебхук `POST /webhook/SetSchedule` тело `{"profileIds": [..], "enabled": bool, "daysMask": int, "startMin": int, "endMin": int}` — фан-аут по профилям, upsert (валидация диапазонов до записи, паттерн Set Reply Mode; `"-1"`/пустые id отфильтровываются). Контракт «в окне»: если `start_min == end_min` → окно 24ч; если `start_min < end_min` → внутри [start,end); если `start_min > end_min` (через полночь) → внутри [start,1440)∪[0,end), при этом ДЕНЬ определяется моментом НАЧАЛА суток по Алматы (ночная часть пн-окна 22–06 приходится на вт-утро и валидна, если включён ПН).

`Schedule Gate` в шаблонах: НОВЫЙ узел сразу после `Suppressed?` FALSE-ветки, ДО `Debounce Wait` (и значит до `Count Dialog`): Postgres-read строки bot_schedule по `{{ $workflow.id }}`-профилю — НЕТ: профиль в шаблоне — вебхук-путь; читать по тому же ключу, каким Suppressed? читает reply_mode_flags (скопировать его биндинг); `enabled=false`/нет строки → проход; вне окна → dead-end (пустой выход, как Suppressed? TRUE). `onError: continueRegularOutput` + fail-open (ошибка БД → проход — боты не умирают от нашей БД, полярность Count Dialog).
- [ ] Оба шаблона байт-идентичны по узлу (расширить `check_dialog_metering_shared` или добавить парный ассерт) + негативные мутации: гейт удалён / перенесён ПОСЛЕ Count Dialog (квота горела бы вне окна) / fail-closed / `disabled`.
- [ ] Живой pinned-прогон на TG-шаблоне (техника Task 9/15b): внутри окна → полный ответ; вне окна → dead-end до Count Dialog (счётчик не изменился — ассерт значением used до/после); ЧЕРЕЗ ПОЛНОЧЬ ОБА плеча (23:00 при окне 22–06 → проход; 12:00 → dead-end).
- [ ] `--schedule`: вебхук-валидация (малформ → bad_request), upsert, фан-аут, сентинел-фильтр.
- [ ] Parity + publish обоих шаблонов. Commit: `feat(n8n): bot schedule — table, webhook, hot-path gate before quota`

### Task 6: Расписание — Unity UI

**Files:** Create: `Assets/Scripts/Billing/ScheduleModel.cs`, `Assets/Scripts/Main/BotSettings.Schedule.cs`, `Assets/Editor/BotScheduleBuilder.cs`, `Assets/Tests/Editor/Billing/ScheduleModelTests.cs`.

**Interfaces:** Consumes: вебхук Task 5. Produces: `static class ScheduleModel`: `int DaysMaskToggle(int mask, int day)`, `bool IsValid(int startMin, int endMin, int mask)` (mask==0 при enabled → invalid), `string FormatWindow(int startMin, int endMin)` (`"09:00–21:00"`, InvariantCulture, минуты нулями), `bool IsInsideWindow(int nowMin, int dowMonday0, int mask, int startMin, int endMin)` — ТОЧНО тот же контракт «через полночь», что в §5 (day-of-START семантика), пиновано тестами-близнецами к серверным кейсам; PlayerPrefs-ключи `"{botName}ScheduleEnabled|DaysMask|StartMin|EndMin"` через bot-persistence-паттерн.

- [ ] Тесты первым: маска, полночь оба плеча, day-of-start, формат.
- [ ] UI на вкладке «Основное»: секция «Расписание работы» — `ToggleRow` + два поля времени (пикеры часов/минут НЕ строить: два `TMP_InputField` HH:MM с валидацией на blur, keypad Default по инвариантам ввода) + 7 чипов «Пн…Вс» (паттерн PromptSuggestionChip-стиля, свой мини-компонент); builder аддитивный, клонирует существующие контролы вкладки; сцена/префабы коммитятся сразу.
- [ ] Изменение → PlayerPrefs + `SetSchedule` POST по обоим profileId бота (skip `"-1"`); сохранение через существующий dirty-механизм BotSettings.
- [ ] run_tests `ScheduleModelTests` зелёный; Game-view самопроверка. Commit: `feat(billing-ui): bot schedule UI + server sync`

### Task 7: Голосовые в Suggest_Replies

**Files:** Create: `Tools/n8n/sql/2026-08-27-block2-transcripts.sql`. Modify: `9PTyYcelRQI7bGDb-Suggest_Replies.json`, `probe-block2.py` (`--voice`), `verify-telegram-parity.py`.

**Interfaces:** Produces: `transcripts(message_id text primary key, text text not null, created_at timestamptz not null default now())`; в Suggest_Replies после гейта подписки: `Has Voice?` (Code: в messages запроса есть элементы-плейсхолдеры `[голосовое сообщение]` с messageId) → для каждого: `Read Transcript Cache` → мисс: `Download Audio` + `Transcribe Audio` (клонировать узлы из бот-шаблона, тот же cred) → `Store Transcript` → подстановка текста ВМЕСТО плейсхолдера в контекст генерации. Клиент УЖЕ шлёт messageId в запросе панели? ПРОВЕРИТЬ первым шагом: если messages запроса не несут id голосовых — добавить поле в запрос (клиентская правка `N8nSuggestionsProvider` + additive-поле, сервер толерантен к отсутствию: нет id → плейсхолдер остаётся как есть, деградация в сегодняшнее поведение).
- [ ] `--voice`: кэш-хит (посеянная строка) → LLM-контекст содержит текст транскрипта (ассерт значением из execution), Download НЕ вызывался; кэш-мисс с фикстурным недоступным audio → tolerated-ошибка, плейсхолдер остался, раунд НЕ упал (`onError: continueRegularOutput` на цепочке); повторный запрос того же голосового → ровно один Store.
- [ ] Живая транскрипция реального голосового — device-pass пункт (в пробе нечем послать настоящий voice).
- [ ] Parity (узлы гейта подписки не сдвинуты; инжекторы промптов по именам узлов не сломаны — прогнать `inject-panel-prompts.py --check` и `node Tools/n8n/verify-panel-prompts.js`) + publish. Commit: `feat(n8n): voice transcripts in suggestions — cached by message_id`

### Task 8: Сводка «Всё время»

**Files:** Modify: `Assets/Scripts/Main/Dashboard/DashboardMetrics.cs` (+enum), `DashboardPage.cs`, `Assets/Editor/DashboardPageBuilder.cs` (4-й сегмент аддитивно — НЕ пере-эмитить страницу: клонировать сегмент-кнопку), `Assets/Tests/Editor/Chat/DashboardMetricsTests.cs` (или создать рядом с существующими тестами Dashboard).

**Interfaces:** Produces: `DashboardPeriod.All` (enum-добавление В КОНЕЦ — сериализация), окно = `[DateTimeOffset.UnixEpoch, now]`, `HasPreviousWindow(DashboardPeriod) => period != All` — дельты и стрелки скрываются у All.

- [ ] Тесты первым: окно All, HasPreviousWindow, счёты по всем строкам без фильтра дат.
- [ ] UI: сегмент «Всё время», паттерн выбора как у трёх существующих; сцена коммитится сразу.
- [ ] run_tests точным классом. Commit: `feat(dashboard): all-time period`

### Task 9: Экспорт CSV

**Files:** Create: `Assets/Scripts/Main/Dashboard/DashboardCsv.cs`, `Assets/Tests/Editor/Chat/DashboardCsvTests.cs`. Modify: `DashboardPage.cs` (кнопка «Экспорт» в шапке листа), `DashboardPageBuilder.cs`-аддитивная кнопка ЛИБО отдельный мини-билдер.

**Interfaces:** Consumes: строки, уже отрисованные страницей (`DashboardPage` держит текущий отфильтрованный список + резолверы `TryGetChatTitle`). Produces: `static byte[] DashboardCsv.Build(IEnumerable<DashboardCsvRow> rows)` где `struct DashboardCsvRow { public string Date, Bot, Channel, Client, Status, Summary; }` — UTF-8 **с BOM** (EF BB BF), разделитель `;` (ru-locale Excel, прецедент конвертеров), RFC-4180-эскейп кавычками, header RU: `Дата;Бот;Канал;Клиент;Статус;Резюме`.

- [ ] Тесты первым: BOM-байты, эскейп `;`/`"`/переноса строки, кириллица, пустой список = только header.
- [ ] Кнопка → `Build` по текущему фильтру/периоду → временный файл в `Application.temporaryCachePath` → `NativeShare` (пакет уже в проекте). Имя файла `svodka_{yyyy-MM-dd}.csv` (InvariantCulture).
- [ ] run_tests. Commit: `feat(dashboard): CSV export via share sheet`

### Task 10: Журнал идемпотентности RC-вебхука (ПЕРВЫЙ из теххвостов — §9)

**Files:** Create: `Tools/n8n/sql/2026-08-27-block2-rc-events.sql`. Modify: `ZGYr6srzS3rSSXHp-RevenueCat_Events.json`, `Tools/n8n/probe-billing.py` (`--dedup`), `verify-telegram-parity.py`.

**Interfaces:** Produces: `rc_events(event_id text primary key, received_at timestamptz not null default now())`. Схема — РОВНО такая (порядок несёт корректность, не менять): узел `Dedup Gate` между Webhook и Map Event только ЧИТАЕТ (`select 1 from rc_events where event_id = $1`); найдено → ack-no-op 200 (RC перестаёт ретраить, нулевые записи); не найдено → обычная цепочка, а ЗАПИСЬ в `rc_events` выполняется дополнительной CTE ВНУТРИ `Upsert Subscriber`-стейтмента — в одной транзакции с эффектом события, так что «помечено обработанным» и «обработано» атомарны. Почему не insert-гейтом в начале: вставка до upsert'а при сбое между ними заставила бы ретрай RC увидеть «дубль» и молча ПОТЕРЯТЬ событие. `event.id` отсутствует (синтетика/старые пробы) → проход без записи (fail-open: редкий дубль лучше потерянного события). Ветки без Upsert (No-Op, CANCELLATION) журнал не пишут — их повтор и так no-op.
- [ ] `--dedup`: первый POST фикстуры → строки записаны; ДОСЛОВНЫЙ повтор → 200, значения НЕ изменились (включая topup_balance при NON_RENEWING — это и есть закрываемый баг), rc_events одна строка; событие без id → обрабатывается как раньше.
- [ ] Parity-ассерт: CTE присутствует в Upsert, Dedup Gate читает-не-пишет; негативные мутации. Publish. Commit: `fix(n8n): RC webhook idempotency — event-id ledger in the upsert transaction`

### Task 11: Identity-verify при запуске

**Files:** Create: `Tools/n8n/workflows/<id>-Verify_Identity.json`. Modify: `Assets/Scripts/Billing/BillingService.cs` (детект склейки) + `Manager.cs` (вызов после Initialize), `probe-block2.py` (`--verify`), README. RC REST-ключ — НОВЫЙ секрет: n8n-credential (Header Auth, `Authorization: Bearer <rc secret api key>`), владелец создаёт ключ в RC-дашборде (инструкция в отчёте задачи; в чат/репо ключ не попадает).

**Interfaces:** Consumes: консолидационный примитив RC Events (тот же SQL — вынести буквально тем же текстом, парити-ассерт байт-совпадения `Consolidate Aliases`-стейтмента между воркфлоу). Produces: `POST /webhook/VerifyIdentity` тело `{"appUserId": string}` → n8n вызывает `GET https://api.revenuecat.com/v1/subscribers/{app_user_id}` СЕРВЕРНЫМ ключом → из ответа берёт `subscriber.original_app_user_id` + `subscriber.aliases`?? — ФАКТ-ЧЕК первым шагом задачи: точную форму ответа RC API проверить по документации и живым вызовом на фикстурном id; если aliases в v1-API отсутствуют — использовать endpoint, который их отдаёт, или зафиксировать BLOCKED с вариантами. Затем: alias-набор ≠ {appUserId} → выполнить консолидацию (сумма+retire, как в RC Events) → `{"success": true, "consolidated": N}`; сеть/RC-ошибка → `{"success": false}` и НИКАКИХ записей (fail-closed на запись, fail-open для клиента — он просто живёт до следующего RC-события). Клиент: после `Initialize`, когда `CustomerInfo.OriginalAppUserId != BillingService.AppUserId`, один вызов за запуск (латч в памяти), fire-and-forget.
- [ ] `--verify`: фикстурные subscriber-строки + подмена RC-вызова недоступным хостом → success:false, нет записей; (живой RC-вызов — с реальным id владельца ТОЛЬКО читающий, консолидация на его строках уже no-op — допустимо и ассертится no-op'ом).
- [ ] Клиентский тест: триггер-предикат pure-seam (`IdentityMismatch(current, original) => bool` — null/пустой original → false).
- [ ] Parity + publish. Commit: `feat(billing): launch identity-verify — RC-sourced alias consolidation`

### Task 12: Сверка профилей при запуске (клиент)

**Files:** Create: `Assets/Scripts/Billing/ProfileReconcile.cs`, `Assets/Tests/Editor/Billing/ProfileReconcileTests.cs`. Modify: `Assets/Scripts/Main/Manager.cs` (корутина в `Start` рядом со свипами PendingProfileLedger/PendingUploads).

**Interfaces:** Produces: `static class ProfileReconcile`: `enum Verdict { Keep, Disconnect }`; `Verdict Judge(long httpStatus, string body)` — `Keep` при 200+`"authorized":true`‑подобном теле И при ЛЮБОЙ сетевой/5xx-ошибке (fail-open: оффлайн не разлогинивает ботов); `Disconnect` только при УВЕРЕННОМ отказе (200 с `authorized:false` без признаков временности, 400 `Profile not found`). Разбор — через bounded-семы (`WappiStatusParser`-паттерн, НИКОГДА руками Substring — правило онбординга).

- [ ] Тесты первым: матрица статусов/тел, оффлайн-кейсы Keep.
- [ ] Корутина: для каждого бота, у каждого канала с profileId ≠ ""/"-1": `get/status` (существующий эндпоинт; тайминг — после LoadBots, не блокируя UI); `Disconnect` → канальный toggle-стор бота выключить + profileId → `"-1"` НЕ СТИРАТЬ — ПРОВЕРИТЬ существующий контракт «не подключён» (что именно читает Bot.cs для тинта иконок) и использовать РОВНО его; workflow-id не трогать.
- [ ] run_tests; Editor-прогон с фикстурным мёртвым id. Commit: `feat(billing): startup profile reconcile — dead channels stop lying`

### Task 13: «Подписка»: резерв + честное состояние

**Files:** Modify: `Assets/Scripts/Billing/SubscriptionPageRows.cs`, `Assets/Scripts/Main/ProfileSubPages.Subscription.cs`, `Assets/Tests/Editor/Billing/SubscriptionPageRowsTests.cs`; при необходимости строка-лейбл — аддитивный мини-билдер.

**Interfaces:** Produces: `SubscriptionPageRows.State(...)` получает `int reserve` и `bool serverSaysExpired` (та же семантика, что `ServerAccountStatus.SaysExpired`): (а) при `reserve > 0` — строка «Из резерва осталось N диалогов» (`RuPlural`, `PaywallCopy.Number`); при statuse истёкшем — «резерв заморожен до возобновления подписки» (N-2 ревью 17); (б) контрадикция (server expired × локально свежий триал) → статус-строка «Истекла», НЕ «Пробный».

- [ ] Тесты первым (матрица состояний, точные строки с ` `).
- [ ] run_tests + Game-view. Commit: `fix(billing-ui): subscription page — reserve line + honest expired state`

### Task 14: Топ-ап первым событием после переустановки — узкий снапшот-фолбэк

**Files:** Modify: `ZGYr6srzS3rSSXHp-RevenueCat_Events.json` (`Consolidate Aliases`), `probe-billing.py` (кейс A4), `verify-telegram-parity.py`.

**Interfaces:** Правило (ревью Task 17, дословно): переносить plan/status/current_period_end/product_id из alias-строки ТОЛЬКО когда получатель `trialing` с `current_period_end IS NULL` И событие само не несёт плана (NON_RENEWING_PURCHASE-путь) — строго-суженный снапшот, не способный ничего даунгрейдить; во всех остальных случаях поведение НЕ меняется.
- [ ] Кейс A4 в пробе: реинсталл-фикстура, первым событием топ-ап → у получателя plan/status/period от alias-строки + суммированный баланс; затем RENEWAL со своим планом → «своё» побеждает (greatest/liveness-гейты не тронуты — негативные кейсы A1/A2/T3 остаются зелёными).
- [ ] Parity + publish. Commit: `fix(n8n): topup-first reinstall carries the plan — narrow trialing-only snapshot`

### Task 15: Device pass Блока 2 + финальное ревью + пуш

- [ ] Сценарий владельцу (RU): включить алерты → диалог с исходом «заказ» со второго номера → алерт в «Сообщении себе» ≤10 мин; выключить канал (logout в приложении Wappi-профиля НЕ делать — проверить «канал отключился» переводом телефона-бота в авиарежим на 15+ мин ЛИБО отвязкой устройства WhatsApp) → алерт через второй канал; расписание: окно, исключающее «сейчас» → бот молчит, подсказки живут, счётчик не растёт → окно вернуть → отвечает; голосовое от клиента → подсказки панели учитывают его содержание; Сводка «Всё время» + экспорт CSV открывается в файле; недельный отчёт — принудительный прогон воркфлоу вручную контроллером.
- [ ] Финальное whole-block ревью (opus, интеграционный проход по образцу Блока 1: сквозные трассы, парити всех новых воркфлоу, копии quota-CASE, гигиена секретов/RU).
- [ ] Полный EditMode-сьют через бридж (гейт `total > 0`), пуш по консенту владельца.

---

## Self-check против спеки

§2 delivery+гейт → T0/T3a; §3 свип/дедуп/каналы/тумблеры → T1/T2/T3a/T3b; §4 отчёт+ночные → T4 (+first_reply_at в T3a); §5 Сводка → T8/T9; §6 расписание → T5/T6; §7 голосовые → T7; §8.1–8.5 → T11/T10/T12/T13/T14 (порядок §9: журнал T10 раньше T11 — соблюдён); §9 порядок → нумерация задач; §10 экономика — фиксация в отчётах T3a/T7 фактического LLM-расхода на прогон.
