# Обучение ИИ-сотрудника в процентах — план исполнения

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Капсула «Авто» во всех четырёх местах заливается по проценту обучения 0–100 % (цель «8 из 10» = 16 угаданных из 20 подряд → 100 %), «Авто» открывается с 70 %, процент не падает, числа считает сервер.

**Architecture:** Сервер (Get Usage, генератор `apply-learning-b.py`) считает по истории ИИ-сотрудника лучшее окно из 20 подряд отправок (скользящая сумма) и условия «3 чата / 2 дня» по всей истории, отдаёт `bestMatches/progress/unlockAt/capped/needChats/needDays` рядом с прежними полями. Клиент впитывает каждый снимок в память телефона (`LearnProgressStore`, внутри `UsageStore.Apply`), а единственная точка `AutoReadinessPolicy.StateFor(botKey)` отдаёт состояние гейту, окну и всем четырём кнопкам. Чистое правило `AutoChipLook` и компонент `AutoChipProgress` (срез через `RectMask2D`) рисуют заливку через единственный `ReplyModeToggleBinder.PaintChip`; аддитивный builder добавляет слой в две префабы и сцену; после отправки с подсказками Get Usage перечитывается не чаще раза в минуту.

**Tech Stack:** n8n canonical JSON + Python-генераторы и верификаторы (`Tools/n8n/*.py`), Postgres через `Tools/n8n/tests/harness.py`, Unity 6 C# (EditMode NUnit, `Tools/run-tests-headless.sh`, `Tools/run-editor-builder.sh`), стенд `Tools/n8n/staging/`.

**Spec:** `docs/superpowers/specs/2026-10-22-learning-progress-percent-design.md` (предыдущий слой — `docs/superpowers/specs/2026-10-14-learning-loop-readiness-design.md`).

## Global Constraints

- Числа ОДИН раз в `Tools/n8n/apply-learning-b.py`: `WINDOW = 20`, `TARGET_MATCHES = 16`, `UNLOCK_PERCENT = 70`, `MIN_CHATS = 3`, `MIN_DAYS = 2`; `MIN_MATCHES = math.ceil(UNLOCK_PERCENT * TARGET_MATCHES / 100)` = 12 (выводится). `MIN_TOTAL` удаляется. У клиента после Task 6 нет ни одного порога.
- Процент: `сырое = round(100 × лучшее / 16)` (половина вверх, Postgres `round` над numeric), не больше 100; `progress = условия ? сырое : min(сырое, 69)`; `ready = условия и лучшее ≥ 12`; `capped = не условия и сырое ≥ 70`; условия = в истории ≥ 3 разных личных чатов и ≥ 2 разных дней (`Asia/Almaty`). История = отправки по фильтру B (`owner_app`, `origin_set > 0`, не группы, линия бота), порядок `created_at, id`.
- Замок — подсказка: `Locked` только если строка есть, `progress != null`, `ready == false` и «Авто уже открывался» = 0. Процент на телефоне = `max(память, progress)`, обрезанный в 0–100; однажды открывшийся «Авто» не запирается.
- Подпись `Учится · {N} %` (между числом и `%` неразрывный пробел ` `) — только на карточке и в шапке чатов; нет при 100 %, при «данных нет» и при «Подключить». В шторке и в чате — только заливка.
- Роли `LearnFillOff`, `LearnFillOn`, `LearnTrackOn` дописываются в КОНЕЦ `ThemeRole` (после `Scrim`); тёмные значения ровно `#26365F`, `#123324`, `#25473D`.
- Заливка — `RectMask2D` переменной ширины над `LearnFill` постоянного размера (размер базового `Fill`); никакого стенсильного `Mask`.
- UI только по-русски; каждая строка — в `AutoReadinessCopy` и зарегистрирована в `FontGlyphCoverageTests` (в атласах SF Pro нет `−`, `№`, `✕`, `→`).
- Никаких `$`+цифра внутри SQL-комментариев (`verify-sql-comments.py`); параметры в комментариях называются словами.
- Сцена — источник правды: только аддитивные идемпотентные builder'ы; `Tools/Rebuild Bot Settings Prefabs` и прежние билдеры шапки НЕ запускать. Builder и тесты — при закрытом Editor (`Tools/run-editor-builder.sh`, `Tools/run-tests-headless.sh`). Сцену и префабы коммитить сразу после прогона builder'а. Новый `.cs` коммитится вместе со своим `.meta`.
- Unity-тесты: `Tools/run-tests-headless.sh "<regex>"`; итог читать по атрибутам `total`/`failed` в `Tools/test-output/results.xml` (`headless-summary.json` бывает неверным).
- Прод — только по явному «да» владельца; сервер выкладывается раньше выпуска сборки.
- Коммит после каждой задачи; сообщение заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Окно ухудшилось после 70 % или 100 %** — процент и замок не откатываются: на сервере держит лучшее окно, на телефоне — память. Пины: Task 1 `a ready bot stays ready…`, Task 2 `Absorb_KeepsTheBestPercent_AndNeverForgetsAnUnlock`.
2. **Владелец отвечает 20+ раз в день** — последние 20 ответов за один день, но день из истории засчитан, без потолка 69 %. Пин: Task 1 `busy owner…`.
3. **Старый сервер без `progress`** — обычное «Авто» без заливки, замка нет. Пины: Task 2 `StateFor_OldServerWithoutProgress_IsNoData`, Task 5 `On_NoData_IsTodaysLook` / `Off_NoData_IsTodaysLook`.
4. **Мусор от сервера (`progress` > 100 или < 0)** — показ обрезается в 0–100. Пин: Task 2 `DisplayPercent_IsTheBestOfMemoryAndServer_Clamped0To100`.
5. **Новый ИИ-сотрудник в том же слоте после удаления** — память пустая. Пины: Task 2 `Clear_RemovesBothKeys_UnderTheAgreedNames`, `LocalWipe_ClearsTheLearningMemory`.

---

### Task 1: Сервер — лучшее окно, условия по истории и процент в Get Usage

**Files:**
- Modify: `Tools/n8n/apply-learning-b.py` (константы, `window_columns`, `progress_sql`, `readiness_columns`, `READINESS_SQL`, `READINESS_COLUMN`)
- Modify: `Tools/n8n/tests/test_learning_b.py`
- Modify: `Tools/n8n/verify-learning-b.py`
- Modify: `Tools/n8n/report-learning.py` (`READINESS_SQL`, `READINESS_COLS`, печать порогов, `selftest`)
- Modify: `Tools/n8n/staging/e2e-learning.py` (`readiness_in_get_usage`, запуск — в Task 10)
- Regenerate: `Tools/n8n/workflows/jtbssfzXbOxwTK4k-Get_Usage.json` (узел `Read Usage`)

**Interfaces:**
- Consumes: CTE `line_bots`, `line_profiles` и `send_filter(e)` из B (без изменений).
- Produces:
  - `window_columns(partition: str, e: str) -> str` — `rn` (новые первыми, текущее окно для отчёта) и `roll` (угаданные в 20 отправках, заканчивающихся этой);
  - `progress_sql(best: str, cond: str) -> str`;
  - `readiness_columns(s: str) -> str` — все выходные колонки;
  - строка ответа Get Usage: `{botKey, matches, total, chats, days, ready, bestMatches, progress, unlockAt, capped, needChats, needDays}`. Поля `matches/total/chats/days` — по текущему окну из 20; остальные — по истории.

- [ ] **Step 1: Переписать ожидания в `test_learning_b.py` (падающий тест)**

В `seed` добавить профиль `p-b2` (whatsapp, `Bot2`, владелец `inst-B`). Helper `pick(row, *keys)` возвращает словарь только с нужными ключами. Случаи — с прежними заголовками, где смысл тот же:

```python
with Case('constants: spec §2 numbers, the client holds none') as c:
    c.check((LB.WINDOW, LB.TARGET_MATCHES, LB.UNLOCK_PERCENT, LB.MIN_MATCHES, LB.MIN_CHATS, LB.MIN_DAYS) == (20, 16, 70, 12, 3, 2))
    c.check(not hasattr(LB, 'MIN_TOTAL'), 'MIN_TOTAL is gone')

with Case('progress table of spec §2 (round half up, cap 69 without the conditions)') as c:
    rows = pg.q('select m, c, ' + LB.progress_sql('m', 'c') + ' as p from (select g as m, b as c '
                'from generate_series(0, 20) g cross join (values (true), (false)) v(b)) t order by m, c desc')
    met = [0, 6, 13, 19, 25, 31, 38, 44, 50, 56, 63, 69, 75, 81, 88, 94, 100, 100, 100, 100, 100]
    c.check([r['p'] for r in rows if r['c']] == met)
    c.check([r['p'] for r in rows if not r['c']] == [min(p, 69) for p in met])
```

- **`fresh install…`** — у каждого бота: `{'matches': 0, 'total': 0, 'chats': 0, 'days': 0, 'ready': False, 'bestMatches': 0, 'progress': 0, 'unlockAt': 70, 'capped': False, 'needChats': 3, 'needDays': 2}`.
- **`ready: 12 matches across both channels + the superseded row, 3 chats, 2 days`** — Bot0: `{'matches': 12, 'total': 12, 'chats': 3, 'days': 2, 'ready': True, 'bestMatches': 12, 'progress': 75, 'unlockAt': 70, 'capped': False, 'needChats': 0, 'needDays': 0}`.
- **`a ready bot stays ready: eleven newer misses move the window, not the best window`** (было «the window is the last 20…») — 11 промахов на Bot0: `pick(...,'matches','total','bestMatches','progress','ready') == {'matches': 9, 'total': 20, 'bestMatches': 12, 'progress': 75, 'ready': True}`.
- **`anti-gaming: 12 matches in ONE chat on one day are capped at 69`** — Bot1: `{'bestMatches': 12, 'progress': 69, 'capped': True, 'ready': False, 'needChats': 2, 'needDays': 1}`.
- **`busy owner: 25 sends today in 3 chats + 1 miss yesterday → the day counts from history`** — Bot2: `{'days': 1, 'bestMatches': 20, 'progress': 100, 'ready': True, 'capped': False, 'needDays': 0}`.
- **`groups and sends without a set…`**, **`legacy mode…`**, **`the rest of Read Usage is untouched…`** — прежние, без изменений.
- **`report-learning's readiness table agrees with Get Usage…`** — сравнивать ключи `matches, total, chats, days, ready, bestMatches, progress, capped, needChats, needDays` с колонками отчёта `matches, total, chats, days, ready, best_matches, progress, capped, need_chats, need_days`.

Проверку «AutoReadiness.cs зеркалит Window/MinMatches» удалить.

- [ ] **Step 2: Убедиться, что тест падает**

Run: `Tools/n8n/staging/pg.sh start; python3 Tools/n8n/tests/test_learning_b.py`
Expected: FAIL на `constants…` (AttributeError `TARGET_MATCHES`) и на новых случаях; итоговая строка `… fail`.

- [ ] **Step 3: Генератор `apply-learning-b.py`**

- Константы из Global Constraints, `MIN_TOTAL` удалить.
- Алгоритм — дословно:

```python
def window_columns(partition, e):
    return (f"row_number() over (partition by {partition} order by {e}.created_at desc, {e}.id desc) as rn,\n"
            f"         sum(coalesce({e}.origin_top1, 0)) over (partition by {partition} order by {e}.created_at, {e}.id\n"
            f"             rows between {WINDOW - 1} preceding and current row) as roll")

def progress_sql(best, cond):
    raw = f"round(100.0 * {best} / {TARGET_MATCHES})"
    return f"(case when {cond} then least(100, {raw}) else least({UNLOCK_PERCENT - 1}, {raw}) end)::int"
```

- `readiness_columns(s)`:
  - оконные поля — те же выражения, что в B, но каждое с `filter (where {s}.rn <= {WINDOW})`;
  - `best = coalesce(max({s}.roll), 0)`;
  - `cond = (count(distinct {s}.chat_id) >= {MIN_CHATS} and count(distinct ({s}.created_at at time zone 'Asia/Almaty')::date) >= {MIN_DAYS})` — без фильтра окна;
  - колонки `best_matches`, `progress` (`progress_sql(best, cond)`), `ready` (`cond and best >= {MIN_MATCHES}`), `capped` (`not cond and round(100.0 * best / {TARGET_MATCHES}) >= {UNLOCK_PERCENT}`), `need_chats` и `need_days` (`greatest(0, порог − число по истории)::int`).
- В `READINESS_SQL`:
  - CTE `sends` выбирает `lp.bot_key, e.chat_id, e.origin_top1, e.created_at` и `window_columns('lp.bot_key', 'e')`;
  - `readiness` присоединяет ВСЕ строки: `left join sends s on s.bot_key = lb.bot_key`, без `rn <= 20`.
- `READINESS_COLUMN` дополняется парами `'bestMatches', r.best_matches, 'progress', r.progress, 'unlockAt', {UNLOCK_PERCENT}, 'capped', r.capped, 'needChats', r.need_chats, 'needDays', r.need_days`.

Затем перегенерировать канон.

Run: `python3 Tools/n8n/apply-learning-b.py && python3 Tools/n8n/apply-learning-b.py --check`
Expected: `patched  jtbssfzXbOxwTK4k-Get_Usage.json`, затем `learning B: OK`.

- [ ] **Step 4: Отчёт, верификатор, e2e**

- **`report-learning.py`:**
  - `sends` строится через `LB.window_columns('bp.owner_id, bp.bot_key', 'e')`, итоговый select берёт всю историю (убрать `where rn <= {LB.WINDOW}`), колонки — через `LB.readiness_columns('s')`;
  - `READINESS_COLS = ["owner_id","bot_key","matches","total","chats","days","ready","best_matches","progress","capped","need_chats","need_days"]`;
  - строка порогов: `readiness: window {WINDOW}, target {TARGET_MATCHES}, unlock {UNLOCK_PERCENT}% (= {MIN_MATCHES} matches), chats ≥ {MIN_CHATS}, days ≥ {MIN_DAYS} (history)`;
  - пример строки в `selftest` — под новые колонки.
- **`verify-learning-b.py`:**
  - фрагменты `s.rn <= {WINDOW}`, `>= {MIN_MATCHES}`, `>= {MIN_CHATS}`, `>= {MIN_DAYS}` остаются;
  - добавить `rows between {WINDOW - 1} preceding and current row`, `/ {TARGET_MATCHES}`, `least({UNLOCK_PERCENT - 1},`, `'progress', r.progress`, `'needChats', r.need_chats`, `'unlockAt', {UNLOCK_PERCENT}`.
- **`e2e-learning.py` `readiness_in_get_usage`:**
  - сначала 11 отправок с `origin_top1 = 1` в три чата, затем сдвиг первого чата на день; ожидание: `total 11, bestMatches 11, progress 69, ready False, capped False, needChats 0, needDays 0`;
  - групповая отправка — без изменений;
  - ещё одна отправка — `bestMatches 12, progress 75, ready True, unlockAt 70`;
  - legacy — по-прежнему `[]`.

- [ ] **Step 5: Прогнать всё серверное**

Run: `python3 Tools/n8n/tests/test_learning_b.py && python3 Tools/n8n/verify-learning-b.py && python3 Tools/n8n/verify-sql-comments.py && python3 Tools/n8n/verify-auth-gate.py | tail -1 && python3 Tools/n8n/report-learning.py --selftest`
Expected: `N pass, 0 fail`, `learning B verify: OK`, без нарушений комментариев, verify-auth-gate зелёный, `selftest OK`.

- [ ] **Step 6: Commit**

```bash
git add Tools/n8n/apply-learning-b.py Tools/n8n/tests/test_learning_b.py Tools/n8n/verify-learning-b.py Tools/n8n/report-learning.py Tools/n8n/staging/e2e-learning.py Tools/n8n/workflows/jtbssfzXbOxwTK4k-Get_Usage.json
git commit -m "feat(learning): процент обучения в Get Usage — лучшее окно и условия по истории"
```

### Task 2: Клиент — новые поля, память лучшего процента и вердикт

**Files:**
- Modify: `Assets/Scripts/Billing/AutoReadiness.cs`
- Create: `Assets/Scripts/Billing/LearnProgressStore.cs`
- Modify: `Assets/Scripts/Billing/UsageStore.cs` (`Apply`)
- Modify: `Assets/Scripts/Main/AutoLockGate.cs`, `Assets/Scripts/Main/Bot.cs` (вызов вердикта; `DeleteBotLocally`)
- Test: `Assets/Tests/Editor/Billing/AutoReadinessPolicyTests.cs`, `Assets/Tests/Editor/Billing/LearnProgressStoreTests.cs` (новый), `Assets/Tests/Editor/Billing/UsageStoreTests.cs`, `Assets/Tests/Editor/Chat/BotDeletionWiringTests.cs`

**Interfaces:**
- Consumes: ответ Task 1.
- Produces:

```csharp
[Serializable] public class AutoReadinessEntry {        // прежние поля B + новые
    public int bestMatches; public int? progress; public int unlockAt; public bool capped; public int needChats; public int needDays;
}
public readonly struct LearningState {
    public LearningState(int percent, int unlockAt, bool capped, int needChats, int needDays, bool locked);
    public int Percent { get; } public int UnlockAt { get; } public bool Capped { get; }
    public int NeedChats { get; } public int NeedDays { get; } public bool Locked { get; }
    public bool Trained => Percent >= 100;
}
public static class AutoReadinessPolicy {
    public static AutoReadinessVerdict Verdict(AutoReadinessEntry entry, bool unlockedBefore);   // заменяет Verdict(entry)
    public static int? DisplayPercent(AutoReadinessEntry entry, int? peak);                     // 0..100; null = данных нет
    public static LearningState? StateFor(string botKey);    // AutoReadinessStore + LearnProgressStore; null = данных нет
    // Window, MinMatches, Progress(entry) остаются до Task 6
}
public static class LearnProgressStore {   // по образцу BotScheduleStore
    public const string PeakSuffix = "LearnPeak", UnlockedSuffix = "LearnUnlocked";
    public static int? Peak(string botName); public static bool Unlocked(string botName);
    public static void Absorb(UsageSnapshot snapshot);   // для строк с progress != null: peak = max(peak, clamp 0..100); unlocked |= ready
    public static void Clear(string botName);
}
```

- **`StateFor`.** Строка есть и `progress != null`: `Percent = DisplayPercent(...)`, остальное — из строки, `Locked` — по `Verdict`. Строки нет (или `progress == null`), но память есть: `Percent = память`, `Locked = false`, `UnlockAt = 0`. Иначе — `null`.
- **`UsageStore.Apply`.** Вызывает `LearnProgressStore.Absorb(snapshot)` между `Current = snapshot` и `OnUsageChanged?.Invoke()`.
- **`AutoLockGate.TryEnable`.** Решает по `StateFor(botKey)?.Locked == true` (лист — прежний до Task 3).
- **`Bot.RefreshCardState`.** Считает `locked` так же.
- **`Bot.DeleteBotLocally`.** Добавить `LearnProgressStore.Clear(transform.name);` рядом с `BotScheduleStore.Clear`.

- [ ] **Step 1: Падающие тесты**

```csharp
// AutoReadinessPolicyTests (Row(progress, ready, …) — helper; прежние тесты одноаргументного Verdict переписать на новый)
[Test] public void Verdict_LocksOnlyAListedBotWithProgress_NotReady_NeverUnlocked() {
    Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(null, false));
    Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Row(progress: null, ready: false), false));
    Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Row(progress: 75, ready: true), false));
    Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Row(progress: 56, ready: false), true));
    Assert.AreEqual(AutoReadinessVerdict.Locked, AutoReadinessPolicy.Verdict(Row(progress: 0, ready: false), false));
}
[Test] public void DisplayPercent_IsTheBestOfMemoryAndServer_Clamped0To100() {
    Assert.AreEqual(75, AutoReadinessPolicy.DisplayPercent(Row(progress: 56), 75));
    Assert.AreEqual(81, AutoReadinessPolicy.DisplayPercent(Row(progress: 81), 75));
    Assert.AreEqual(100, AutoReadinessPolicy.DisplayPercent(Row(progress: 130), null));
    Assert.AreEqual(0, AutoReadinessPolicy.DisplayPercent(Row(progress: -5), null));
    Assert.AreEqual(44, AutoReadinessPolicy.DisplayPercent(Row(progress: null), 44));
    Assert.AreEqual(44, AutoReadinessPolicy.DisplayPercent(null, 44));
    Assert.IsNull(AutoReadinessPolicy.DisplayPercent(null, null));
}
```

- **`StateFor_CarriesTheServerFacts_AndUnlockSticks`** (бот `LearnStateBot`; снимки через `UsageStore.Apply(UsageStore.Parse(json))`):
  - строка `progress 69, unlockAt 70, capped true, needChats 2, needDays 1, ready false` → `Percent 69, Capped, NeedChats 2, NeedDays 1, Locked == true`;
  - затем `progress 75, ready true` → `Locked == false`;
  - затем `progress 63, ready false` → `Percent 75`, `Locked == false`.
- **`StateFor_OldServerWithoutProgress_IsNoData`** — строка без поля `progress` → `StateFor` == null.
- **`LearnProgressStoreTests`:**
  - `Absorb_KeepsTheBestPercent_AndNeverForgetsAnUnlock` — 56 → 75 (ready) → 63: память 75, `Unlocked == true`;
  - `Absorb_IgnoresRowsWithoutProgress_AndClampsGarbage` — 130 → 100;
  - `Clear_RemovesBothKeys_UnderTheAgreedNames` — `LearnStoreBotLearnPeak`, `LearnStoreBotLearnUnlocked`.
  - Тестовые боты чистить в `[SetUp]`/`[TearDown]`.
- **`UsageStoreTests`:**
  - `Apply_AbsorbsTheLearningMemory_BeforeRaisingTheEvent` — подписчик внутри события видит новую `Peak`;
  - `Parse_ReadsTheNewReadinessFields_AndAMissingProgressIsNull`.
- **`BotDeletionWiringTests.LocalWipe_ClearsTheLearningMemory`** — тело `DeleteBotLocally` содержит `LearnProgressStore.Clear(transform.name)`; тот же приём, что в `LocalWipe_ClearsTheRetiredChannelMarkers`.

- [ ] **Step 2: Убедиться, что тесты падают** — Run: `Tools/run-tests-headless.sh "AutoReadinessPolicyTests|LearnProgressStoreTests|UsageStoreTests|BotDeletionWiringTests"` → компиляция падает (нет типов) или FAIL.

- [ ] **Step 3: Реализовать по Interfaces** (`AutoReadinessStore.Current(` по-прежнему читается только в `AutoReadiness.cs`, `AutoLockGate.cs` и `Bot.cs` — список `AutoLockWiringTests.OnlyTheGateAndTheCard_ReadTheReadinessStore` не трогать).

- [ ] **Step 4: Тесты зелёные** — Run: `Tools/run-tests-headless.sh "AutoReadinessPolicyTests|LearnProgressStoreTests|UsageStoreTests|BotDeletionWiringTests|AutoLockGateTests|AutoLockWiringTests"` → `results.xml`: `failed="0"`.

- [ ] **Step 5: Commit** — `git add` изменённые файлы + `LearnProgressStore.cs(.meta)`, `LearnProgressStoreTests.cs(.meta)`; сообщение `feat(learning): память лучшего процента и вердикт по StateFor`.

### Task 3: Тексты и окно «ещё учится» с процентом и полосой

**Files:**
- Modify: `Assets/Scripts/Main/AutoReadinessCopy.cs`, `Assets/Scripts/Main/AutoLockSheet.cs`, `Assets/Scripts/Main/AutoLockGate.cs`
- Test: `Assets/Tests/Editor/Chat/AutoLockSheetTests.cs`, `Assets/Tests/Editor/Chat/AutoLockGateTests.cs`, `Assets/Tests/Editor/FontGlyphCoverageTests.cs` (образцы на месте прежних `SheetBody(7, …)`)

**Interfaces:**
- Consumes: `LearningState`, `AutoReadinessPolicy.StateFor` (Task 2).
- Produces:

```csharp
public static class AutoReadinessCopy {      // SheetTitle, Understood, EnableAnyway — без изменений; PillText — до Task 6
    public const string MarkerLabel = "Авто";
    public static string Caption(int percent);   // "Учится · 45 %"
    public static string Percent(int percent);   // "45 %"
    public static string SheetBody(int unlockAt, bool capped, int needChats, int needDays, bool perChat);
}
public static class AutoLockSheet {
    public static void Show(TMP_FontAsset font, LearningState state, bool perChat, Action proceed);
    // CardWidth 720, CardHeight 664; TitleTop 56, TitleHeight 60, TitleSideInset 48;
    // PercentTop 132, PercentHeight 84 (72pt Bold, AccentText); BarTop 228, BarHeight 16, BarSideInset 56 (ширина 608);
    // MarkerWidth 4, MarkerHeight 32 (по центру полосы, InkPrimary); MarkerLabelTop 256, MarkerLabelHeight 34 (26pt, InkSecondary);
    // BodyTop 320, BodyHeight 180, BodySideInset 56; ButtonY 44, ButtonHeight 96, ButtonWidth 300
}
```

Новые элементы карточки `Content`:
- `Percent` — TMP;
- `Bar` — трек `Border`, скругление 8;
- `Bar/Fill` — `AccentFill`; `anchorMax.x` = процент / 100, как в `FirstStepsCard.SetProgress`;
- `Marker` — по `UnlockAt / 100` ширины полосы;
- `MarkerLabel` — центр под отметкой.

`ConfirmCardFitter` по-прежнему двигает только тело; заголовок помещается в одну строку.

Гейт: `LearningState? state = StateFor(botKey); if (state?.Locked != true) { proceed?.Invoke(); return; } AutoLockSheet.Show(font, state.Value, perChat, proceed);`

- [ ] **Step 1: Падающие тесты**

```csharp
[Test] public void Copy_IsPercentBased_NoForbiddenGlyphs() {
    Assert.AreEqual("Учится · 45 %", AutoReadinessCopy.Caption(45));
    Assert.AreEqual("45 %", AutoReadinessCopy.Percent(45));
    Assert.AreEqual("«Авто» откроется на 70 %. Процент растёт, когда панель угадывает ваш ответ: отвечайте в режиме «Выбрать» — каждая отправка учит ИИ-сотрудника.",
        AutoReadinessCopy.SheetBody(70, false, 0, 0, false));
    const string almost = "Почти готово: ИИ-сотрудник уже угадывает ваши ответы, но нужны ответы ещё ";
    Assert.AreEqual(almost + "в 2 других чатах.", AutoReadinessCopy.SheetBody(70, true, 2, 0, false));
    Assert.AreEqual(almost + "в 1 другом чате.", AutoReadinessCopy.SheetBody(70, true, 1, 0, false));
    Assert.AreEqual(almost + "в 1 другой день.", AutoReadinessCopy.SheetBody(70, true, 0, 1, false));
    Assert.AreEqual(almost + "в 2 других дня.", AutoReadinessCopy.SheetBody(70, true, 0, 2, false));
    Assert.AreEqual(almost + "в 2 других чатах и в 1 другой день.", AutoReadinessCopy.SheetBody(70, true, 2, 1, false));
    StringAssert.EndsWith(" Включить авто-режим в этом чате можно и сейчас.", AutoReadinessCopy.SheetBody(70, false, 0, 0, true));
    // + прежняя проверка: ни одного из № → − ✕
}
```

- **`Show_PaintsThePercent_TheBar_AndTheMarker`** — `Show(font, new LearningState(45, 70, false, 0, 0, true), false, …)`:
  - `Percent` = `"45 %"`;
  - `Bar/Fill` `anchorMax.x` = 0.45;
  - центр `Marker` на 0.70 ширины `Bar` (±0.5u);
  - `MarkerLabel` = `"Авто"`;
  - `Body` = `SheetBody(70, false, 0, 0, false)`.
- **`Show_Capped_SaysWhatIsMissing`** — `LearningState(69, 70, true, 2, 0, true)` → тело равно `almost + "в 2 других чатах."`.
- **`Geometry_StacksTopToBottom_WithoutOverlap`** — `TitleTop + TitleHeight <= PercentTop`, `PercentTop + PercentHeight <= BarTop`, `BarTop + BarHeight <= MarkerLabelTop`, `MarkerLabelTop + MarkerLabelHeight <= BodyTop`. `ConfirmCardLayoutTests.AutoLockSheet_AuthoredBodyBox_ClearsTheButtons` проходит без правки: зазор 664 − 140 − 500 = 24.
- Прежние `Show_*` и `Back_IsUnderstood_AndAReshowRepaintsTheProgress` — на новую сигнатуру (повторный показ с 75 → `Percent` = `"75 %"`).
- `AutoLockGateTests` — запертое состояние создаётся снимком `progress 45, ready false`, «открытые» случаи включают память «уже открывался».

- [ ] **Step 2: Убедиться, что падает** — Run: `Tools/run-tests-headless.sh "AutoLockSheetTests|AutoLockGateTests"` → FAIL.

- [ ] **Step 3: Реализовать** копию (формы через `RuPlural.Pick(n, "другом чате", "других чатах", "других чатах")` и `RuPlural.Pick(n, "другой день", "других дня", "других дней")`), лист и гейт; старый `SheetBody(int matches, bool perChat)` удалить.

- [ ] **Step 4: Тесты зелёные** — Run: `Tools/run-tests-headless.sh "AutoLockSheetTests|AutoLockGateTests|ConfirmCardLayoutTests|ConfirmCardFitterTests|FontGlyphCoverageTests|BackNavigationTests"` → `failed="0"`.

- [ ] **Step 5: Commit** — `feat(learning): окно «ещё учится» с процентом, полосой и отметкой 70 %`.

### Task 4: Три роли темы для заливки обучения

**Files:**
- Modify: `Assets/Scripts/Theme/ThemeRole.cs`, `Assets/Scripts/Theme/ThemeAsset.cs`, `Assets/Editor/ThemeAssetsBuilder.cs`
- Regenerate: `Assets/Resources/Theme/Theme_Light.asset`, `Assets/Resources/Theme/Theme_Dark.asset`
- Create: `Assets/Tests/Editor/ContrastMath.cs` (перенос `Channel/Luminance/Contrast` из `NavTabPaletteTests.cs:33-43`; `NavTabPaletteTests` переключить на него)
- Create: `Assets/Tests/Editor/Chat/LearnPaletteTests.cs`

**Interfaces:**
- Produces: `ThemeRole.LearnFillOff`, `ThemeRole.LearnFillOn`, `ThemeRole.LearnTrackOn` (после `Scrim`, в этом порядке); поля `ThemeAsset.learnFillOff = Hex("#D5DCEC")`, `learnFillOn = Hex("#CBEBD9")`, `learnTrackOn = Hex("#EEF8F2")` (светлые значения по умолчанию); `ThemeAssetsBuilder.BuildHeadless()` с логом `[ThemeAssetsBuilder] Headless build + save complete.`; `internal static class ContrastMath { float Luminance(Color); float Contrast(Color, Color); }`.

- [ ] **Step 1: Падающие тесты `LearnPaletteTests`** (SetUp как в `NavTabPaletteTests`: `ThemePrefs.GetInt = (k, d) => d; Theme.ResetForTests();`, проверки на `Theme.Light` и `Theme.Dark`):
  - `Dark_UsesTheSpecValues` — `#26365F`, `#123324`, `#25473D` (сравнение по 8-битным каналам);
  - `Dark_FillOn_EqualsPositiveBg` и `Dark_TrackIsLighterThanTheFill` (`Luminance(LearnTrackOn) > Luminance(LearnFillOn)`);
  - `Labels_Read_OnEveryLearnColour_InBothThemes` — `Contrast(InkSecondary, LearnFillOff) >= 4.5`, `Contrast(PositiveInk, LearnFillOn) >= 4.5`, `Contrast(PositiveInk, LearnTrackOn) >= 4.5`;
  - `TheFillEdge_IsVisible_InBothThemes` — `Contrast(LearnFillOn, LearnTrackOn) >= 1.15`.

- [ ] **Step 2: Убедиться, что падает** — Run: `Tools/run-tests-headless.sh "LearnPaletteTests"` → компиляция падает.

- [ ] **Step 3: Реализовать** роли, поля, `Resolve`, `SeedLight` (= значения по умолчанию) и `SeedDark` (значения спеки), `BuildHeadless`; затем записать ассеты.

Run: `Tools/run-editor-builder.sh ThemeAssetsBuilder.BuildHeadless`
Expected: exit 0; в `Theme_*.asset` появились `learnFillOff`, `learnFillOn`, `learnTrackOn`.

- [ ] **Step 4: Тесты зелёные** — Run: `Tools/run-tests-headless.sh "LearnPaletteTests|NavTabPaletteTests|ThemeFoundationTests"` → `failed="0"` (`EveryRole_ResolvesOnFreshAsset_NoMagentaSentinel` покрывает новые роли).

- [ ] **Step 5: Commit** — с `.asset`, `.cs` и `.meta`; `feat(theme): роли заливки обучения LearnFillOff/LearnFillOn/LearnTrackOn`.

### Task 5: Чистое правило вида капсулы «Авто»

**Files:**
- Create: `Assets/Scripts/UI/AutoChipLook.cs`
- Create: `Assets/Tests/Editor/Chat/AutoChipLookTests.cs`

**Interfaces:**
- Consumes: `ThemeRole` (Task 4), `AutoReadinessCopy.Caption` (Task 3).
- Produces:

```csharp
public readonly struct AutoChipPaint {
    public ThemeRole Ring { get; } public ThemeRole Base { get; } public ThemeRole Ink { get; }
    public ThemeRole Lamp { get; } public bool LampSolid { get; } public ThemeRole Fill { get; } public float FillFraction { get; }   // 0 = заливки нет
}
public static class AutoChipLook {
    public static AutoChipPaint For(bool on, int? percent);
    public static string Caption(int? percent);   // null при null и при >= 100; иначе AutoReadinessCopy.Caption(percent)
}
```

- [ ] **Step 1: Падающие тесты** (матрица спеки §4.3):
  - **`Off_NoData_IsTodaysLook`** — `For(false, null)`: Ring `Border`, Base `Surface`, Ink `InkSecondary`, Lamp `InkTertiary`, `LampSolid == false`, `FillFraction == 0`.
  - **`Off_Learning_FillsBlue`** — `For(false, 45)`: то же + Fill `LearnFillOff`, `FillFraction == 0.45f`.
  - **`Off_Trained_IsAllBlue`** — `For(false, 100)`: `FillFraction == 1f`, Fill `LearnFillOff`.
  - **`On_NoData_IsTodaysLook`** — `For(true, null)`: Ring и Base `PositiveBg`, Ink `PositiveInk`, Lamp `PositiveInk`, `LampSolid`, `FillFraction == 0`.
  - **`On_Learning_IsDarkGreenOverLighterGreen`** — `For(true, 30)`: Ring и Base `LearnTrackOn`, Fill `LearnFillOn`, `FillFraction == 0.30f`, Ink `PositiveInk`.
  - **`On_Trained_IsExactlyToday`** — `For(true, 100)` равно `For(true, null)`.
  - **`Caption_HidesAtNoDataAndAtHundred`** — `Caption(null)` и `Caption(100)` → null; `Caption(0)` → `"Учится · 0 %"`; `Caption(45)` → `"Учится · 45 %"`.

- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "AutoChipLookTests"`.
- [ ] **Step 3: Реализовать** `For` и `Caption` (процент обрезать в 0–100 перед долей).
- [ ] **Step 4: PASS** — тот же прогон, `failed="0"`.
- [ ] **Step 5: Commit** — `feat(learning): правило вида капсулы «Авто» по проценту обучения`.

### Task 6: Заливка — компонент, общий рисовальщик и четыре места

**Files:**
- Create: `Assets/Scripts/UI/AutoChipProgress.cs`
- Modify: `Assets/Scripts/UI/ReplyModeToggleBinder.cs` (новая `PaintChip`, процент выбранного ИИ-сотрудника, `learnCaption`, подписка на `UsageStore.OnUsageChanged`)
- Modify: `Assets/Scripts/Main/Bot.cs` (лицо `Learning` удаляется; поля `autoPillProgress`, `learnCaption`)
- Modify: `Assets/Scripts/Main/BotCardModel.cs` (`BotPillKind { Auto, Connect }`, `PillKind(bool connect)`)
- Modify: `Assets/Scripts/UI/BotSwitcherRowView.cs` (`chipProgress`, подписка на `OnUsageChanged`)
- Modify: `Assets/Scripts/UI/SemiAutoToggle.cs` (`progress`, подписка на `OnUsageChanged`; `SetLit` и `HandlePressed` не трогать — их пинит `ScheduleFallbackPolicyTests`)
- Modify: `Assets/Scripts/Billing/AutoReadiness.cs` (удалить `Window`, `MinMatches`, `Progress`), `Assets/Scripts/Main/AutoReadinessCopy.cs` (удалить `PillText`)
- Test: `Assets/Tests/Editor/Chat/AutoChipProgressTests.cs` (новый), `Assets/Tests/Editor/Chat/AutoChipPaintWiringTests.cs` (новый), `BotCardModelTests.cs`, `AutoReadinessPolicyTests.cs` (удалить `Progress_IsMatchesClampedToTheWindow`), `FontGlyphCoverageTests.cs` (удалить образец `PillText`)

**Interfaces:**
- Consumes: `AutoChipLook` (Task 5), `AutoReadinessPolicy.StateFor` (Task 2).
- Produces:

```csharp
public class AutoChipProgress : MonoBehaviour {
    [SerializeField] RectTransform clip;      // RectMask2D, прижат влево; ширина = доля × ширина baseFill
    [SerializeField] Image fill;              // скруглённая, постоянного размера = размер baseFill
    [SerializeField] RectTransform baseFill;  // существующий внутренний Fill капсулы
    public float AppliedFraction { get; }
    public void Apply(float fraction, Color color, bool animate);   // 0 → срез нулевой ширины
}
// ReplyModeToggleBinder — единственная перегрузка (прежняя удаляется)
public static void PaintChip(bool on, int? percent, Image ring, Image fill, AutoChipProgress progress,
    TextMeshProUGUI chipLabel, Image lampRing, Image lampCore, bool animate, float duration = AnimDuration);
```

- **`PaintChip`.** Берёт цвета из `AutoChipLook.For(on, percent)`: ring ← `Ring`, fill ← `Base`, надпись ← `Ink`, лампа ← `Lamp`, сердцевина лампы ← `LampSolid ? Lamp : Base`. Затем `progress?.Apply(paint.FillFraction, Theme.Color(paint.Fill), animate)`. `progress == null` безопасен: до Task 7 слоя нет.
- **`Apply`.** Анимирует только рост на активном объекте: DOTween около 0,4 с, `OutCubic`, по `sizeDelta.x` среза. Иначе ставит сразу.
- **Процент в четырёх местах:**
  - карточка — `StateFor(transform.name)`;
  - шапка — `StateFor(currentBotId)`;
  - строка шторки — `StateFor(botId)`;
  - чат — `StateFor(ChatManager.Instance?.CurrentBotId)`.
- **Подпись** (`learnCaption`, только `Bot` и `ReplyModeToggleBinder`). Текст — `AutoChipLook.Caption(percent)`; активна, когда текст не null и не «Подключить».
- **«Подключить».** `PaintConnectPill` + `autoPillProgress?.Apply(0, Color.clear, false)`, подпись скрыта.
- **Ширина капсулы.** `ApplyPillFace` ставит текст «Подключить» или авторское «Авто»; ширина — через `BotCardModel.PillWidth(kind, labelWidth)`; лампа скрыта только у «Подключить».

- [ ] **Step 1: Падающие тесты**

```csharp
[Test] public void ThereIsOnePaintChip_AndItTakesThePercentAndTheProgressLayer() {
    var all = typeof(ReplyModeToggleBinder).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "PaintChip").ToArray();
    Assert.AreEqual(1, all.Length);
    var types = all[0].GetParameters().Select(p => p.ParameterType).ToArray();
    CollectionAssert.Contains(types, typeof(int?)); CollectionAssert.Contains(types, typeof(AutoChipProgress));
}
[TestCase("Scripts/Main/Bot.cs")] [TestCase("Scripts/UI/ReplyModeToggleBinder.cs")]
[TestCase("Scripts/UI/BotSwitcherRowView.cs")] [TestCase("Scripts/UI/SemiAutoToggle.cs")]
public void EveryAutoChip_PaintsThroughTheSharedPainter_WithTheBotsLearningState(string file) {
    string src = Src(file);
    StringAssert.Contains("PaintChip(", src); StringAssert.Contains("AutoReadinessPolicy.StateFor(", src);
    StringAssert.Contains("UsageStore.OnUsageChanged", src);
}
```

- **`AutoChipProgressTests`.** Иерархия строится в коде: `Pill`, под ним `Fill` 184×70; `LearnClip` с `RectMask2D`, под ним `LearnFill`.
  - `Apply_SizesTheClip_AndKeepsTheFillAtTheBaseSize` — `Apply(0.45f, c, false)` → `clip.sizeDelta.x == 0.45f * 184` (±0.01), `fill.rectTransform.sizeDelta == (184, 70)`, `fill.color == c`, `AppliedFraction == 0.45f`;
  - `Apply_Zero_HidesTheFill` — ширина 0.
- **`BotCardModelTests`.** `PillKind_ConnectOutranksEverything_ElseAuto`; `PillWidth_AutoKeepsItsAuthoredWidth_ConnectFitsItsLabel` — прежний; `TheRealCapsuleLabel_FitsTheLearningFace_OnceTheCapsuleGrows` удалить.

- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "AutoChipPaintWiringTests|AutoChipProgressTests|BotCardModelTests"`.
- [ ] **Step 3: Реализовать** по Interfaces; удалить `Window/MinMatches/Progress/PillText` и их образцы в тестах.
- [ ] **Step 4: PASS** — Run: `Tools/run-tests-headless.sh "AutoChipPaintWiringTests|AutoChipProgressTests|BotCardModelTests|AutoReadinessPolicyTests|AutoLockWiringTests|ReconnectWiringTests|ScheduleFallbackPolicyTests|FontGlyphCoverageTests|AutoButtonModelTests"` → `failed="0"`.
- [ ] **Step 5: Commit** — `feat(learning): заливка обучения в капсуле «Авто» — один рисовальщик для четырёх мест`.

### Task 7: Слой заливки и подпись в сцене и префабах (builder)

**Files:**
- Create: `Assets/Editor/AutoChipLearnLayerBuilder.cs`
- Modify: `Assets/Editor/BotCardAutoPillBuilder.cs`, `Assets/Editor/ChatsTopBarRestyleBuilder.cs` — после пересборки своих капсул вызывают `Wire*` ниже, чтобы повторный запуск старого билдера не стирал слой
- Regenerate: `Assets/Prefabs/Bot.prefab`, `Assets/Prefabs/BotSwitcherRow.prefab`, `Assets/Scenes/Main.unity`
- Create: `Assets/Tests/Editor/Chat/AutoChipLearnLayerPremiseTests.cs`

**Interfaces:**
- Consumes: `AutoChipProgress` и сериализуемые поля Task 6.
- Produces:

```csharp
public static class AutoChipLearnLayerBuilder {
    public const float CaptionGap = 2f, CaptionFontSize = 28f; public static readonly Vector2 CaptionSize = new Vector2(220f, 34f);
    [MenuItem("Tools/Learning/Add Progress Fill To Auto Chips")] public static void Build();
    public static void BuildHeadless();   // открывает Main.unity, правит обе префабы и сцену, сохраняет; лог «[AutoChipLearnLayerBuilder] Headless build + save complete.»
    public static AutoChipProgress EnsureLayer(Transform pill);
    public static TextMeshProUGUI EnsureCaption(Transform chipRoot, TMP_FontAsset font);
    public static void WireBotCard(Bot bot); public static void WireHeader(ReplyModeToggleBinder binder);
    public static void WireSwitcherRow(BotSwitcherRowView row); public static void WireConversationChip(SemiAutoToggle chip);
}
```

**Слой** (`EnsureLayer`). `Pill` находится через сериализованную ссылку на `Fill` её владельца (`fill.transform.parent`), не по имени.
- Сразу после `Fill`, до `Content`, — `LearnClip`: anchors (0,0)–(0,1), pivot (0,0.5), `anchoredPosition` (вставка Fill, 0), `sizeDelta` (0, −2·вставка), `RectMask2D`.
- Внутри — `LearnFill`: `Image` с `raycastTarget = false` и `ImageWithRoundedCorners` с радиусом базового `Fill` (35 у карточки и шапки, 27 у строки и чата); anchors и pivot (0,0.5); размер = внутренний размер `Fill` (184×70 и 164×54).
- На `Pill` — `AutoChipProgress` со ссылками.
- Повторный вызов ничего не дублирует.

**Подпись** (`EnsureCaption`, только карточка и шапка) — `LearnCaption`, дочерний объект корня капсулы (`AutoPill` или `AutoButton`):
- TMP шрифтом надписи капсулы, `CaptionFontSize`, по центру, без переноса, `raycastTarget = false`;
- `ThemedColor(InkTertiary)`;
- anchors (0.5,0), pivot (0.5,1), `anchoredPosition` (0, `CaptionGap`), `sizeDelta` = `CaptionSize`;
- по умолчанию неактивна.

Ссылки (`autoPillProgress`, `learnCaption`, `progress`, `chipProgress`) ставятся через `SerializedObject`.

- [ ] **Step 1: Падающие тесты** (загрузка префаб — `PrefabUtility.LoadPrefabContents`, сцены — как в `ConfirmCardScenePremiseTests`):
  - **`BotPrefab_AutoPill_HasTheLearnLayer_AndTheCaption`:**
    - дети `Pill` по порядку: `Ring, Fill, LearnClip, Content`;
    - у `LearnClip` есть `RectMask2D`;
    - `LearnFill` 184×70, радиус 35;
    - у `Bot` проставлены `autoPillProgress` и `learnCaption`;
    - `CaptionGap + CaptionSize.y + 10 <= 78` — подпись помещается в свободные 78u под капсулой;
    - объекта с именем `Label` вне `Content` нет.
  - **`SwitcherRowPrefab_Chip_HasTheLearnLayer_AndNoCaption`** — `LearnFill` 164×54, радиус 27, `chipProgress` проставлен.
  - **`Scene_HeaderPill_AndConversationChip_HaveTheLearnLayer`** — у шапки есть подпись, у чата нет; ссылки проставлены.
  - **`EnsureLayer_IsIdempotent`** — два вызова на тестовой иерархии дают один `LearnClip`.

- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "AutoChipLearnLayerPremiseTests"`.
- [ ] **Step 3: Реализовать builder и вызовы из двух старых билдеров**, затем прогнать при закрытом Editor.

Run: `Tools/run-editor-builder.sh AutoChipLearnLayerBuilder.BuildHeadless`
Expected: exit 0, в `Tools/test-output/builder.log` строка `[AutoChipLearnLayerBuilder] Headless build + save complete.`

- [ ] **Step 4: PASS** — Run: `Tools/run-tests-headless.sh "AutoChipLearnLayerPremiseTests|BotCardModelTests|FontGlyphCoverageTests|BotsHeaderIconsLayoutTests"` → `failed="0"`.
- [ ] **Step 5: Commit сразу** (сцена делится с другими сессиями) — `git add` builder, оба билдера, обе префабы, `Main.unity`, тесты и `.meta`; `feat(learning): слой заливки и подпись в капсулах «Авто» (builder)`.

### Task 8: Перечитывать процент после ответов в чате

**Files:**
- Create: `Assets/Scripts/Billing/LearnRefresh.cs`
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`PostTextMessageRoutine`, ветка успеха рядом с `SuggestionPickStats.RecordFromOrigin`, ~L2349)
- Create: `Assets/Tests/Editor/Billing/LearnRefreshTests.cs`

**Interfaces:**

```csharp
public static class LearnRefresh {
    public const long MinIntervalMs = 60L * 1000;  public const float DelaySeconds = 5f;
    public static bool ShouldFetch(long nowMs, long lastMs);   // как ChannelHealth.ShouldRun: lastMs <= 0 || прошла минута || часы ушли назад
    public static void AfterLearningSend();   // проверяет ShouldFetch, ставит отметку, запускает на Manager.Instance: WaitForSeconds(DelaySeconds) → UsageClient.FetchRoutine()
}
```

В `ChatManager.PostTextMessageRoutine` (успех): `if (origin != null && origin.kind != SendOriginKind.None) LearnRefresh.AfterLearningSend();`. Повторы из outbox идут через этот же метод.

- [ ] **Step 1: Падающие тесты**
  - **`ShouldFetch_AtMostOncePerMinute`** — четыре `TestCase`, как в `ChannelHealthTests.A_launch_and_a_return_check_at_most_once_a_minute`: `(0, t) → true`, `(t, t + 59_999) → false`, `(t, t + 60_000) → true`, `(t, t − 1) → true`.
  - **`TheSendAck_RefreshesOnlyAfterASendWithAShownSet`** — в теле `PostTextMessageRoutine` строка `LearnRefresh.AfterLearningSend()` стоит после `response.status == "done"` и под условием, содержащим `SendOriginKind.None`.
  - **`TheRefresh_IsHostedOnTheManager_AndUsesTheUsageClient`** — `LearnRefresh.cs` содержит `Manager.Instance` и `UsageClient.FetchRoutine()`.
- [ ] **Step 2: FAIL** — Run: `Tools/run-tests-headless.sh "LearnRefreshTests"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: PASS** — Run: `Tools/run-tests-headless.sh "LearnRefreshTests|SendOriginWiringTests|SendPathWiringTests|UsageClientTests"` → `failed="0"`.
- [ ] **Step 5: Commit** — `feat(learning): перечитывать процент после ответа с подсказками, не чаще раза в минуту`.

### Task 9: Документы и полные прогоны

**Files:**
- Modify: `CLAUDE.md` — абзац «Learning B — теневой счёт и мягкий замок «Авто»» переписать под новые правила: проценты, лучшее окно, условия по истории, память телефона, четыре места заливки, `LearnRefresh`. Фраза «требует колонок A» остаётся (её пинит `verify-learning-b.py`).
- Modify: `docs/superpowers/specs/2026-10-14-learning-loop-readiness-design.md` — одна строка под заголовком: лицо капсулы и пороги заменены спекой 2026-10-22.

- [ ] **Step 1: Правки документов.**
- [ ] **Step 2: Полный EditMode** — Run: `Tools/run-tests-headless.sh` → в `Tools/test-output/results.xml` `failed="0"`.
- [ ] **Step 3: Все серверные тесты** — Run: `Tools/n8n/staging/pg.sh start; python3 Tools/n8n/tests/run_all.py` → каждая строка `OK`.
- [ ] **Step 4: Commit** — `docs(learning): CLAUDE.md и пометка в спеке B`.

### Task 10: Стенд, репетиция и прод (прод — только по «да» владельца)

- [ ] **Step 1: Стенд.**
  1. Поднять Postgres: `Tools/n8n/staging/pg.sh start`.
  2. Импортировать канон: `python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` (не `--restart`).
  3. Запустить мок: `WAPPI_MOCK_LOG=Tools/test-output/wappi-mock.jsonl python3 Tools/n8n/staging/wappi-mock.py &`.
  4. Прогнать сценарии: `python3 Tools/n8n/staging/e2e-learning.py`.

  Expected: все сценарии PASS, включая `readiness_in_get_usage` с числами Task 1.
- [ ] **Step 2: Репетиция с прод-состояния.**
  1. `python3 Tools/n8n/staging/bootstrap.py --canon-ref <sha, которым сейчас обслуживается прод> --bot wa:stgwa001 --bot tg:stgtg001`
  2. `python3 Tools/n8n/deploy-canonical.py --base http://localhost:5678` — dry-run называет ровно `jtbssfzXbOxwTK4k`.
  3. То же с `--apply --yes`.
  4. `python3 Tools/n8n/verify-deployed.py --base http://localhost:5678` — diff 0.
  5. `python3 Tools/n8n/staging/derive-staging.py` (без импорта).
  6. `e2e-learning.py --only readiness_in_get_usage` — PASS.
- [ ] **Step 3: Прод — СТОП, нужен явный «да» владельца.** После «да»:
  1. проверка колонок A на проде через `.secrets/sql-oneoff.py`: `information_schema.columns` для `origin_set` и `origin_top1` = 2;
  2. `python3 Tools/n8n/deploy-canonical.py --only jtbssfzXbOxwTK4k` (dry-run), записать указатель отката — текущий `activeVersionId`;
  3. `--apply --yes`;
  4. `verify-deployed.py` — diff 0;
  5. запись в `docs/audits/implementation-progress.md` с указателем отката.
- [ ] **Step 4: Список проверки на устройстве** (новая сборка): карточка, шапка чатов, шторка, открытый чат; тёмная и светлая тема; 1080×2400; заливка растёт после ответов в чате; окно на 45 % и в состоянии `capped`. Дописать пункты в `docs/audits/2026-10-19-device-pass-new-build.md`.
