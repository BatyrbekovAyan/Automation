# Подпроект B «Теневой счёт и замок «Авто»» — план исполнения

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** «Авто» предлагается включить только когда панель «Вместе» угадала ответ владельца ≥10 раз из последних 20 отправок с показанным набором; до этого капсула показывает «Учится · N/20», а попытка включить открывает лист «Бот ещё учится» с «Включить всё равно».

**Architecture:** Сервер считает готовность по `bot_key` установки одним CTE в `Read Usage` (Get Usage) и отдаёт `readiness: [{botKey, matches, total, chats, days, ready}]`; клиент хранит её в `UsageSnapshot.readiness`, чистая `AutoReadinessPolicy` запирает ТОЛЬКО по положительному вердикту сервера, один `AutoLockGate.TryEnable` стоит на четырёх точках включения «Авто» и показывает `AutoLockSheet`; капсула карточки получает третье состояние `Learning`. Генератор `apply-learning-b.py` владеет узлами Get Usage; данные уже пишет A — миграций нет.

**Tech Stack:** n8n canonical JSON + Python-генераторы/верификаторы (`Tools/n8n/*.py`), Postgres (Supabase) через харнесс `Tools/n8n/tests/harness.py` (`Case`, `PgSession`, `bind`, `node`, `report`, `wf`), Unity 6 C# (EditMode NUnit, headless `Tools/run-tests-headless.sh`), staging `Tools/n8n/staging/`.

**Spec:** `docs/superpowers/specs/2026-10-14-learning-loop-readiness-design.md` (§0 — решения владельца; A: `docs/superpowers/specs/2026-09-25-learning-loop-foundation-design.md`).

## Global Constraints

- Пороги ОДИН раз: `WINDOW = 20`, `MIN_MATCHES = 10`, `MIN_TOTAL = 10`, `MIN_CHATS = 3`, `MIN_DAYS = 2` в `Tools/n8n/apply-learning-b.py`; `report-learning.py` импортирует их, клиент дублирует только `Window = 20` и `MinMatches = 10` для подписей (`AutoReadinessPolicy`) — и `tests/test_learning_b.py` сверяет оба числа с модулем.
- Замок — подсказка, не защита: `Locked` только если запись бота есть и `ready == false`; нет снимка / нет записи / старый сервер / legacy → `Open`. Включённое «Авто» замок не трогает; отключение и серверная реконсиляция через гейт не идут.
- Знаменатель: `source = 'owner_app'`, `origin_set > 0`, не группы (`chat_id not like '%@g.us'`, `chat_id not like '-%'`); окно — последние 20 по `created_at desc, id desc` на `bot_key`; оба канала и вытесненные строки линии (`bot_profiles.owner_id = installId`, тот же `bot_key`). В список попадают боты, у которых есть хоть одна живая строка (`deleted_at is null`).
- Legacy (`crAuth.mode ≠ 'session'`) или пустой `installId` → `readiness = []`. Старый клиент поле игнорирует; старый сервер → `readiness == null` на клиенте.
- Никаких `$N` внутри SQL-комментариев или литералов (`verify-sql-comments.py`): параметры в комментариях называются словами; ни одного нового вебхука, узла на горячем пути ботов, миграции.
- Генераторы идемпотентны с `--check`; маркеры `LEARNING-B-*`. Все существующие верификаторы остаются зелёными: `verify-auth-gate.py` (Get Usage gated, RULE прежний), `verify-sql-comments.py`, `verify-learning-a.py`.
- UI только по-русски; каждая строка в `AutoReadinessCopy`, зарегистрированном в `FontGlyphCoverageTests` (SF Pro: «·» U+00B7 и «/» в атласе есть; «№», «→», «−», «✕» — нет).
- Новый `.cs` получает `.meta` при первом headless-прогоне — коммитить вместе с файлом. Unity-тесты только через `Tools/run-tests-headless.sh "<regex>"` (Editor закрыт); результат — `Tools/test-output/headless-summary.json` (`failed`).
- Стенд: `Tools/n8n/staging/pg.sh start` (+ `migrate` однократно) + `python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001` (НЕ `--restart` — он не переимпортирует канон); мок `WAPPI_MOCK_LOG=Tools/test-output/wappi-mock.jsonl python3 Tools/n8n/staging/wappi-mock.py &`. Прод — только по явному «go» владельца.
- Коммит после каждой задачи; каждое сообщение заканчивается `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

1. **Бот уже в «Авто» (нулевой счёт или «Включить всё равно»)** — капсула показывает «Авто», замок его не выключает и не мешает выключить; после ручного выключения следующий тап на включение снова встречает замок (так задумано). Пины: `BotCardModelTests.PillKind_…` («Авто» никогда не переименовывается), `AutoLockWiringTests.DisablingAndServerReconcile_StayOutsideTheGate`.
2. **Сеть недоступна / Get Usage упал / поля нет** — вердикт `Open`, капсула «Авто». Пины: `AutoReadinessPolicyTests.NullEntry_IsOpen_AndProgressZero`, `UsageStoreTests.Parse_ReadsReadiness_AndTreatsAMissingFieldAsNull`, `AutoLockGateTests.OpenVerdict_ProceedsAtOnce_NoSheet`.
3. **Два канала одного бота и вытесненный профиль** — одна сумма, без удвоения. Пин: `test_learning_b.py` «ready: 10 matches across both channels + the superseded row».
4. **Группы (WA `@g.us`, TG отрицательный id), отправки без набора, чужая установка с тем же `bot_key`** — не в счёте. Пин: `test_learning_b.py` «groups and sends without a set…», e2e `readiness_in_get_usage` (группа).
5. **Граница окна (21-я отправка)** — старые совпадения выпадают, `ready` может вернуться в `false` для бота в «Вместе». Пин: `test_learning_b.py` «the window is the last 20».

---

### Task 1: Сервер — `readiness` в Get Usage (SQL-тест, генератор, верификатор)

**Files:**
- Create: `Tools/n8n/tests/test_learning_b.py`
- Create: `Tools/n8n/apply-learning-b.py`
- Create: `Tools/n8n/verify-learning-b.py`
- Regenerate: `Tools/n8n/workflows/jtbssfzXbOxwTK4k-Get_Usage.json` (узлы `Read Usage`, `Shape Response`)

**Interfaces:**
- Consumes: `chat_history_events.origin_set/origin_top1/created_at/id` (A), `bot_profiles.owner_id/bot_key/deleted_at`, `Auth Gate` Get Usage (`$('Auth Gate').first().json.crAuth.installId` — `null` в legacy, `crAuth.mode`).
- Produces: константы `apply_learning_b.WINDOW/MIN_MATCHES/MIN_TOTAL/MIN_CHATS/MIN_DAYS`, `READINESS_SQL` (CTE-блок), `READINESS_COLUMN`, `check(wf)`; ответ Get Usage `readiness: [{botKey: string, matches: int, total: int, chats: int, days: int, ready: bool}]` (по `botKey`, `[]` для legacy).

- [ ] **Step 1: Падающий SQL-тест — создать `Tools/n8n/tests/test_learning_b.py`**

```python
#!/usr/bin/env python3
"""Learning B (2026-10-14): the readiness CTE of Get Usage's Read Usage, run as real SQL on session temp tables of the staging
Postgres (tests/harness.py). One bot = one bot_key of the install (both channels + the superseded row of its line); the window is
the last 20 owner_app sends WITH a shown set in personal chats; ready = total >= 10 and matches >= 10 and chats >= 3 and days >= 2.
Needs `Tools/n8n/staging/pg.sh start` (+ `migrate` once)."""
import importlib.util
import itertools
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from harness import Case, PgSession, bind, node, report, wf  # noqa: E402

N8N = Path(__file__).resolve().parents[1]
ROOT = N8N.parents[1]
_spec = importlib.util.spec_from_file_location("apply_learning_b", N8N / "apply-learning-b.py")
LB = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LB)

LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = ''.join(LIKE.format(t) for t in ('installs', 'subscribers', 'dialog_counts', 'bot_profiles', 'chat_history_events'))
GU = wf('jtbssfzXbOxwTK4k-Get_Usage.json')
INST, OTHER, U = 'inst-B', 'inst-X', 'u-B'
_mid = itertools.count(1)


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')   # the temp tables shadow the UNqualified names only


def readiness(pg, mode='session', inst=INST):
    row = pg.q(bind(Q(GU, 'Read Usage'), [U, inst, mode]))[0]
    r = row['readiness']
    return json.loads(r) if isinstance(r, str) else r


def seed(pg):
    pg.x(f"insert into installs (install_id, secret_hash) values ('{INST}', 'h'), ('{OTHER}', 'h');")
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, bot_key) values "
         f"('p-wa', '{U}', 'whatsapp', '{INST}', 'wWa000000001', 'Bot0'), ('p-tg', '{U}', 'telegram', '{INST}', 'wTg000000001', 'Bot0'), "
         f"('p-old', '{U}', 'whatsapp', '{INST}', 'wOld00000001', 'Bot0'), ('p-b1', '{U}', 'whatsapp', '{INST}', 'wB1000000001', 'Bot1'), "
         f"('p-x', 'u-X', 'whatsapp', '{OTHER}', 'wX0000000001', 'Bot0');")
    pg.x("update bot_profiles set superseded_by = 'p-wa', superseded_at = now(), deleted_at = now(), deleted_reason = 'replaced' where profile_id = 'p-old';")


def send(pg, pid, chat, top1, days_ago=0, origin_set=3):
    pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, origin_kind, origin_set, origin_top1, created_at) values "
         f"('{pid}', '{chat}', 'm{next(_mid)}', 'owner_app', 't', 'card', {origin_set}, {top1}, now() - interval '{days_ago} days');")


def bot(pg, key, **kw):
    return next((e for e in readiness(pg, **kw) if e['botKey'] == key), None)


with PgSession(SETUP) as pg:
    with Case('constants: spec §2 numbers, mirrored by the client for its copy') as c:
        c.check((LB.WINDOW, LB.MIN_MATCHES, LB.MIN_TOTAL, LB.MIN_CHATS, LB.MIN_DAYS) == (20, 10, 10, 3, 2), 'spec §2 constants')
        cs_path = ROOT / 'Assets' / 'Scripts' / 'Billing' / 'AutoReadiness.cs'
        cs = cs_path.read_text(encoding='utf-8') if cs_path.exists() else ''
        c.check((not cs) or (f'Window = {LB.WINDOW};' in cs and f'MinMatches = {LB.MIN_MATCHES};' in cs),
                'AutoReadinessPolicy.Window/MinMatches mirror the module (once Task 3 exists)')
    with Case('fresh install: every bot_key with a live row is listed with zeros and ready=false') as c:
        seed(pg)
        r = readiness(pg)
        c.check([e['botKey'] for e in r] == ['Bot0', 'Bot1'], f'rows: {r}')
        c.check(all(e['total'] == 0 and e['matches'] == 0 and e['chats'] == 0 and e['days'] == 0 and e['ready'] is False for e in r), f'zeros: {r}')
    with Case('ready: 10 matches across both channels + the superseded row, 3 chats, 2 days') as c:
        for _ in range(4): send(pg, 'p-wa', '7701@c.us', 1, days_ago=1)
        for _ in range(3): send(pg, 'p-tg', '123456', 1)
        for _ in range(3): send(pg, 'p-old', '7703@c.us', 1)
        c.check(bot(pg, 'Bot0') == {'botKey': 'Bot0', 'matches': 10, 'total': 10, 'chats': 3, 'days': 2, 'ready': True}, f"{bot(pg, 'Bot0')}")
    with Case('groups and sends without a set are not counted; another install with the same bot_key is a different bot') as c:
        send(pg, 'p-wa', '120363@g.us', 1); send(pg, 'p-tg', '-100777', 1); send(pg, 'p-wa', '7701@c.us', 1, origin_set=0)
        send(pg, 'p-x', '7701@c.us', 1)
        e = bot(pg, 'Bot0')
        c.check(e['total'] == 10 and e['matches'] == 10 and e['ready'] is True, f'{e}')
        other = readiness(pg, inst=OTHER)
        c.check([(x['botKey'], x['total']) for x in other] == [('Bot0', 1)], f'inst-X sees only its own send: {other}')
    with Case('the window is the last 20: eleven newer misses push the ready bot back under the threshold') as c:
        for _ in range(11): send(pg, 'p-wa', '7701@c.us', 0)
        c.check(bot(pg, 'Bot0') == {'botKey': 'Bot0', 'matches': 9, 'total': 20, 'chats': 3, 'days': 2, 'ready': False}, f"{bot(pg, 'Bot0')}")
    with Case('anti-gaming: 10 matches in ONE chat on one day are not ready') as c:
        for _ in range(10): send(pg, 'p-b1', '7709@c.us', 1)
        e = bot(pg, 'Bot1')
        c.check(e['matches'] == 10 and e['total'] == 10 and e['chats'] == 1 and e['days'] == 1 and e['ready'] is False, f'{e}')
    with Case('legacy mode or an empty install id → []') as c:
        c.check(readiness(pg, mode='legacy') == [], 'legacy caller')
        c.check(readiness(pg, inst='') == [], 'no install id')
    with Case('the rest of Read Usage is untouched: plan/used/bots columns still answer') as c:
        row = pg.q(bind(Q(GU, 'Read Usage'), [U, INST, 'session']))[0]
        c.check(row['plan'] == 'trial' and row['used'] == 0 and row['bots_registered'] == 2, f'{row}')
report()
```

- [ ] **Step 2: Прогнать — должен упасть**

Run: `Tools/n8n/staging/pg.sh start; python3 Tools/n8n/tests/test_learning_b.py`
Expected: падает на импорте модуля (`apply-learning-b.py` нет — `FileNotFoundError`); после Step 3, но до применения генератора — `KeyError: 'readiness'` (колонки в ответе нет).

- [ ] **Step 3: Генератор — создать `Tools/n8n/apply-learning-b.py`**

```python
#!/usr/bin/env python3
"""Learning loop, подпроект B «Теневой счёт и замок «Авто»» (2026-10-14) — идемпотентный патч Get Usage:

  Read Usage      — CTE-блок readiness (маркеры -- LEARNING-B-BEGIN / -- LEARNING-B-END) + колонка readiness (jsonb)
                    первой в итоговом select (маркер -- LEARNING-B-COL); параметры: второй = installId, третий = mode
                    из Auth Gate (null в legacy → '').
  Shape Response  — поле readiness (строка с маркером // LEARNING-B) — массив как есть, [] если не массив.

Пороги — ОДИН раз здесь; report-learning.py импортирует их, клиентская AutoReadinessPolicy зеркалит Window/MinMatches
(tests/test_learning_b.py сверяет).

    python3 Tools/n8n/apply-learning-b.py            # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-learning-b.py --check    # exit 2 если не применено
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
GET_USAGE = "jtbssfzXbOxwTK4k-Get_Usage.json"

WINDOW = 20        # the last N owner sends with a shown set, per bot
MIN_MATCHES = 10   # card #1 matched the sent text (origin_top1 = 1)
MIN_TOTAL = 10     # at least this many sends with a set in the window
MIN_CHATS = 3      # … spread over this many personal chats
MIN_DAYS = 2       # … on this many calendar days (Asia/Almaty)

# The comments name the parameters in WORDS (verify-sql-comments.py: no dollar-digit inside a comment).
READINESS_SQL = f"""-- LEARNING-B-BEGIN (generated by Tools/n8n/apply-learning-b.py — edit there)
-- Readiness of «Авто» per bot of the calling install (second parameter = install id, third = gate mode): the last {WINDOW}
-- owner_app sends WITH a shown set in personal chats, both channels and the superseded rows of the bot's line.
line_bots as (
  select distinct bp.bot_key
  from bot_profiles bp
  where $3::text = 'session' and $2::text <> '' and bp.owner_id = $2::text and bp.bot_key is not null and bp.deleted_at is null
), line_profiles as (
  select bp.profile_id, bp.bot_key
  from bot_profiles bp
  join line_bots lb on lb.bot_key = bp.bot_key
  where bp.owner_id = $2::text
), sends as (
  select lp.bot_key, e.chat_id, e.origin_top1, e.created_at,
         row_number() over (partition by lp.bot_key order by e.created_at desc, e.id desc) as rn
  from chat_history_events e
  join line_profiles lp on lp.profile_id = e.profile_id
  where e.source = 'owner_app' and coalesce(e.origin_set, 0) > 0
    and e.chat_id not like '%@g.us' and e.chat_id not like '-%'
), readiness as (
  select lb.bot_key,
         (count(s.bot_key) filter (where s.origin_top1 = 1))::int as matches,
         count(s.bot_key)::int as total,
         count(distinct s.chat_id)::int as chats,
         count(distinct (s.created_at at time zone 'Asia/Almaty')::date)::int as days,
         (count(s.bot_key) >= {MIN_TOTAL} and count(s.bot_key) filter (where s.origin_top1 = 1) >= {MIN_MATCHES}
          and count(distinct s.chat_id) >= {MIN_CHATS}
          and count(distinct (s.created_at at time zone 'Asia/Almaty')::date) >= {MIN_DAYS}) as ready
  from line_bots lb
  left join sends s on s.bot_key = lb.bot_key and s.rn <= {WINDOW}
  group by lb.bot_key
),
-- LEARNING-B-END
"""
READINESS_COLUMN = ("  (select coalesce(jsonb_agg(jsonb_build_object('botKey', r.bot_key, 'matches', r.matches, 'total', r.total, "
                    "'chats', r.chats, 'days', r.days, 'ready', r.ready) order by r.bot_key), '[]'::jsonb) from readiness r) as readiness, -- LEARNING-B-COL\n")
SUB_ANCHOR = "), sub as ("
SELECT_ANCHOR = "select\n  coalesce(sub.plan, 'trial') as plan,"
PARAMS = ("={{ [ $('Webhook').item.json.body.appUserId, ($('Auth Gate').first().json.crAuth.installId || ''), "
          "($('Auth Gate').first().json.crAuth.mode || '') ] }}")
SHAPE_LINE = "    readiness: Array.isArray(row.readiness) ? row.readiness : [],   // LEARNING-B: per-bot readiness of «Авто» (apply-learning-b.py)\n"
SHAPE_ANCHOR = "    interval,\n"


def load(fname):
    return json.load(open(os.path.join(WF, fname), encoding="utf-8"))


def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
        fh.write("\n")


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def strip_block(sql):
    """The query as it was before B (the block and the column line removed) — the base every patch is applied to."""
    sql = re.sub(r"-- LEARNING-B-BEGIN.*?-- LEARNING-B-END\n", "", sql, flags=re.S)
    return "\n".join(line for line in sql.split("\n") if "LEARNING-B-COL" not in line)


def patched_query(sql):
    base = strip_block(sql)
    assert SUB_ANCHOR in base and SELECT_ANCHOR in base, "Read Usage: anchors drifted — update apply-learning-b.py"
    out = base.replace(SUB_ANCHOR, "), " + READINESS_SQL + "sub as (", 1)
    return out.replace(SELECT_ANCHOR, "select\n" + READINESS_COLUMN + "  coalesce(sub.plan, 'trial') as plan,", 1)


def patched_js(js):
    base = "\n".join(line for line in js.split("\n") if "LEARNING-B" not in line)
    assert SHAPE_ANCHOR in base, "Shape Response: anchor `interval,` drifted — update apply-learning-b.py"
    return base.replace(SHAPE_ANCHOR, SHAPE_ANCHOR + SHAPE_LINE, 1)


def patch(wf):
    changed = False
    ru = node(wf, "Read Usage")
    want = patched_query(ru["parameters"]["query"])
    if ru["parameters"]["query"] != want:
        ru["parameters"]["query"] = want
        changed = True
    opts = ru["parameters"].setdefault("options", {})
    if opts.get("queryReplacement") != PARAMS:
        opts["queryReplacement"] = PARAMS
        changed = True
    sr = node(wf, "Shape Response")
    want_js = patched_js(sr["parameters"]["jsCode"])
    if sr["parameters"]["jsCode"] != want_js:
        sr["parameters"]["jsCode"] = want_js
        changed = True
    return changed


def check(wf):
    ru, sr = node(wf, "Read Usage"), node(wf, "Shape Response")
    if ru is None or sr is None:
        return False
    q, js = ru["parameters"]["query"], sr["parameters"]["jsCode"]
    return (q == patched_query(q) and "-- LEARNING-B-BEGIN" in q
            and ru["parameters"].get("options", {}).get("queryReplacement") == PARAMS
            and js == patched_js(js) and SHAPE_LINE in js)


def main():
    wf = load(GET_USAGE)
    if "--check" in sys.argv:
        ok = check(wf)
        print("learning B:", "OK" if ok else f"MISSING/STALE in {GET_USAGE}")
        sys.exit(0 if ok else 2)
    changed = patch(wf)
    if changed:
        save(GET_USAGE, wf)
    print(f"{'patched' if changed else 'up to date'}  {GET_USAGE}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Применить, проверить идемпотентность и тесты**

Run: `python3 Tools/n8n/apply-learning-b.py && python3 Tools/n8n/apply-learning-b.py --check && python3 Tools/n8n/apply-learning-b.py && python3 Tools/n8n/verify-sql-comments.py && python3 Tools/n8n/verify-auth-gate.py | tail -1 && python3 Tools/n8n/tests/test_learning_b.py`
Expected: `patched  jtbssfzXbOxwTK4k-Get_Usage.json` → `learning B: OK` → `up to date …`; verify-sql-comments без ошибок (новый CTE: ни одного `$`+цифра в комментариях); `ALL AUTH GATE ASSERTS PASSED` (Get Usage gated как прежде); `test_learning_b.py` → 8 cases pass, 0 fail. Если `jsonb` приходит строкой — `readiness()` в тесте уже делает `json.loads`. Проверить `git diff` канона: изменились ТОЛЬКО `Read Usage.parameters.query`, его `options.queryReplacement` и `Shape Response.parameters.jsCode`.

- [ ] **Step 5: Верификатор — создать `Tools/n8n/verify-learning-b.py`**

```python
#!/usr/bin/env python3
"""Структурные проверки learning B (2026-10-14). Без сети. Exit 1 при любой проблеме."""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
_spec = importlib.util.spec_from_file_location("apply_learning_b", os.path.join(HERE, "apply-learning-b.py"))
LB = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LB)
problems = []


def expect(cond, msg):
    if not cond:
        problems.append(msg)
        print("FAIL", msg)


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


wf = json.load(open(os.path.join(WF, "jtbssfzXbOxwTK4k-Get_Usage.json"), encoding="utf-8"))
ru, sr, ag = node(wf, "Read Usage"), node(wf, "Shape Response"), node(wf, "Auth Gate")
q = ru["parameters"]["query"]
params = ru["parameters"].get("options", {}).get("queryReplacement", "")
expect(LB.check(wf), "get usage: apply-learning-b.py --check is not satisfied (stale canon)")
for frag in ("line_bots as (", "line_profiles as (", "sends as (", "readiness as (", "row_number() over (partition by lp.bot_key",
             "chat_id not like '%@g.us'", "chat_id not like '-%'", "coalesce(e.origin_set, 0) > 0", "source = 'owner_app'",
             f"s.rn <= {LB.WINDOW}", f">= {LB.MIN_MATCHES}", f">= {LB.MIN_CHATS}", f">= {LB.MIN_DAYS}", "$3::text = 'session'",
             "bp.deleted_at is null"):
    expect(frag in q, f"get usage: Read Usage lacks `{frag}`")
expect(q.count("-- LEARNING-B-BEGIN") == 1 and q.count("-- LEARNING-B-END") == 1 and q.count("LEARNING-B-COL") == 1, "get usage: marker count")
expect("crAuth.installId" in params and "crAuth.mode" in params and "appUserId" in params,
       "get usage: Read Usage params must carry appUserId + installId + mode (Auth Gate)")
expect("row.readiness" in sr["parameters"]["jsCode"] and "LEARNING-B" in sr["parameters"]["jsCode"], "get usage: Shape Response lacks readiness")
expect(ag is not None and wf["connections"]["Authorized?"]["main"][0][0]["node"] == "Read Usage", "get usage: gate wiring changed")
expect(ru.get("onError") == "continueErrorOutput", "get usage: Read Usage must keep its error output (Respond Error)")
rep = open(os.path.join(HERE, "report-learning.py"), encoding="utf-8").read()
expect("apply-learning-b" in rep and "readiness" in rep, "report: report-learning.py must import the module's constants and print readiness (Task 2)")
print("learning B verify:", "OK" if not problems else f"{len(problems)} problem(s)")
sys.exit(1 if problems else 0)
```

Run: `python3 Tools/n8n/verify-learning-b.py`
Expected: один `FAIL report: …` (Task 2 ещё не сделан), остальное без FAIL, `learning B verify: 1 problem(s)`, exit 1.

- [ ] **Step 6: Commit**

```bash
git add Tools/n8n/apply-learning-b.py Tools/n8n/verify-learning-b.py Tools/n8n/tests/test_learning_b.py Tools/n8n/workflows/jtbssfzXbOxwTK4k-Get_Usage.json
git commit -m "feat(n8n): learning B — готовность «Авто» по боту в ответе Get Usage (CTE readiness: окно 20, ≥10 совпадений, ≥3 чата, ≥2 дня)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Отчёт — таблица готовности в `report-learning.py`

**Files:**
- Modify: `Tools/n8n/report-learning.py` (`json_sql`, `parse_rows`, `run_staging`, `run_prod` получают параметры колонок/сортировки; новый `READINESS_SQL`; вторая таблица в `main`)

**Interfaces:**
- Consumes: `apply_learning_b.WINDOW/MIN_*` (Task 1).
- Produces: вторая таблица отчёта `install | bot | match/total | chats | days | ready` по всем установкам стенда или прода (`--prod`).

- [ ] **Step 1: Верификатор падает на отчёте**

Run: `python3 Tools/n8n/verify-learning-b.py 2>&1 | grep report`
Expected: `FAIL report: report-learning.py must import the module's constants and print readiness (Task 2)`.

- [ ] **Step 2: Правки `report-learning.py`**

Импорт — после `HERE = os.path.dirname(os.path.abspath(__file__))`:

```python
import importlib.util

_spec_b = importlib.util.spec_from_file_location("apply_learning_b", os.path.join(HERE, "apply-learning-b.py"))
LB = importlib.util.module_from_spec(_spec_b); _spec_b.loader.exec_module(LB)
```

После `COLS = [...]` добавить (этот запрос НЕ проходит через `%`-подстановку — одиночные `%` в LIKE):

```python
# learning B: the lock's own arithmetic per (install, bot_key), across every install — the same window and thresholds
# the Get Usage CTE decides with (constants imported, never retyped). Not windowed by --days: the lock is not either.
READINESS_SQL = f"""
with sends as (
  select bp.owner_id, bp.bot_key, e.chat_id, e.origin_top1, e.created_at,
         row_number() over (partition by bp.owner_id, bp.bot_key order by e.created_at desc, e.id desc) as rn
  from chat_history_events e
  join bot_profiles bp on bp.profile_id = e.profile_id
  where bp.bot_key is not null and bp.owner_id is not null
    and e.source = 'owner_app' and coalesce(e.origin_set, 0) > 0
    and e.chat_id not like '%@g.us' and e.chat_id not like '-%'
)
select owner_id, bot_key,
       (count(*) filter (where origin_top1 = 1))::int as matches, count(*)::int as total,
       count(distinct chat_id)::int as chats,
       count(distinct (created_at at time zone 'Asia/Almaty')::date)::int as days,
       (count(*) >= {LB.MIN_TOTAL} and count(*) filter (where origin_top1 = 1) >= {LB.MIN_MATCHES}
        and count(distinct chat_id) >= {LB.MIN_CHATS}
        and count(distinct (created_at at time zone 'Asia/Almaty')::date) >= {LB.MIN_DAYS}) as ready
from sends where rn <= {LB.WINDOW}
group by owner_id, bot_key
"""
READINESS_COLS = ["owner_id", "bot_key", "matches", "total", "chats", "days", "ready"]
```

Заменить четыре функции (сигнатуры с колонками и сортировкой; поведение первой таблицы байт в байт прежнее):

```python
def json_sql(sql, order="r.profile_id"):
    """ONE row, ONE column: {"rows":[...]} — the same contract for psql (prints json as text) and the prod harness
    (an n8n webhook whose Response Data may be firstEntryJson → {"payload":…} or allEntries → [{"payload":…}])."""
    return (f"select json_build_object('rows', coalesce(json_agg(row_to_json(r) order by {order}), '[]'::json)) as payload from ("
            + sql.rstrip().rstrip(";") + ") r")


def parse_rows(text, cols=COLS):
    obj = json.loads(text)
    if isinstance(obj, list):
        obj = obj[0] if obj else {}
    payload = obj.get("payload", obj) if isinstance(obj, dict) else obj
    if isinstance(payload, str):
        payload = json.loads(payload)
    rows = (payload or {}).get("rows") or []
    return [[("" if r.get(c) is None else str(r.get(c))) for c in cols] for r in rows]


def run_staging(sql, cols=COLS, order="r.profile_id"):
    out = subprocess.run([os.path.join(HERE, "staging", "pg.sh"), "psql", "-tAc", json_sql(sql, order)], capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(out.stderr.strip())
    return parse_rows(out.stdout.strip(), cols)


def run_prod(sql, cols=COLS, order="r.profile_id"):
    out = subprocess.run([sys.executable, os.path.join(HERE, ".secrets", "sql-oneoff.py"), json_sql(sql, order)], capture_output=True, text=True)
    lines = [l for l in out.stdout.strip().splitlines() if l.strip()]
    if out.returncode != 0 or not lines:
        sys.exit((out.stderr.strip() or out.stdout.strip() or "harness produced no output")[:300])
    status, _, body = lines[-1].partition(" ")      # the harness prints ONE line: `200 <body>`
    if status != "200":
        sys.exit(f"harness -> {lines[-1][:300]}")
    try:
        return parse_rows(body, cols)
    except json.JSONDecodeError:
        sys.exit("harness output is not JSON — most likely truncated by sql-oneoff.py's b[:1500]; lift that limit first (deploy checklist 1a)")
```

В `selftest()` перед `print("selftest OK")` добавить:

```python
    rb = parse_rows('{"rows":[{"owner_id":"i","bot_key":"Bot0","matches":7,"total":12,"chats":3,"days":2,"ready":false}]}', READINESS_COLS)
    assert rb == [["i", "Bot0", "7", "12", "3", "2", "False"]], rb
```

В `main()` заменить хвост начиная с `rows = run_prod(sql) if a.prod else run_staging(sql)`:

```python
    run = run_prod if a.prod else run_staging
    rows = run(sql)
    print(f"{'profile':18} {'sends':>5} {'card':>5} {'edit':>5} {'typed':>5} {'none':>5} {'top1':>6} {'edit%':>5} {'sets':>5} {'lrn3/lrn':>9} {'tokens':>8}")
    for r in rows:
        pid, sends, card, edited, typed, none, top1, with_set, edit_mean, sets, lrn, lrn3, tokens = r
        top1_share = f"{int(top1) / int(with_set):.0%}" if int(with_set or 0) else "—"
        print(f"{pid[:18]:18} {sends:>5} {card:>5} {edited:>5} {typed:>5} {none:>5} {top1_share:>6} {edit_mean or '—':>5} {sets:>5} {lrn3 + '/' + lrn:>9} {tokens:>8}")
    if not rows:
        print("(no rows in the window)")

    # learning B: what the «Авто» lock sees — every install's bots over the lock's own window (not --days)
    rows_b = run(READINESS_SQL, READINESS_COLS, order="r.owner_id, r.bot_key")
    print(f"\nreadiness: window {LB.WINDOW}, ready = matches ≥ {LB.MIN_MATCHES}, total ≥ {LB.MIN_TOTAL}, chats ≥ {LB.MIN_CHATS}, days ≥ {LB.MIN_DAYS}")
    print(f"{'install':16} {'bot':6} {'match/total':>11} {'chats':>5} {'days':>4} {'ready':>5}")
    for owner_id, bot_key, matches, total, chats, days, ready in rows_b:
        print(f"{owner_id[:16]:16} {bot_key[:6]:6} {matches + '/' + total:>11} {chats:>5} {days:>4} {'yes' if ready.lower() == 'true' else 'no':>5}")
    if not rows_b:
        print("(no bot has a send with a shown set yet)")
```

- [ ] **Step 3: Проверить**

Run: `python3 Tools/n8n/report-learning.py --selftest && python3 Tools/n8n/report-learning.py --days 1 && python3 Tools/n8n/verify-learning-b.py && python3 Tools/n8n/verify-learning-a.py | tail -1`
Expected: `selftest OK`; обе таблицы печатаются (на стенде — строки e2e или заглушки «(no …)»); `learning B verify: OK`, exit 0; verify-learning-a без изменений.

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/report-learning.py
git commit -m "feat(n8n): report-learning — таблица готовности «Авто» по ботам теми же порогами, что Get Usage

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Клиент — модель, магазин и чистая политика

**Files:**
- Create: `Assets/Scripts/Billing/AutoReadiness.cs`
- Modify: `Assets/Scripts/Billing/UsageStore.cs` (класс `UsageSnapshot`: поле `readiness`)
- Test: `Assets/Tests/Editor/Billing/AutoReadinessPolicyTests.cs` (новый), `Assets/Tests/Editor/Billing/UsageStoreTests.cs` (+1 тест)

**Interfaces:**
- Consumes: `UsageStore.Current` (static get), `UsageStore.Apply(UsageSnapshot)`, `UsageStore.ResetSeamsForTests()` (internal — тесты уже вызывают его из `UsageStoreTests`), `UsageStore.Parse(string)`.
- Produces: `public class AutoReadinessEntry { string botKey; int matches; int total; int chats; int days; bool ready; }`; `public enum AutoReadinessVerdict { Open, Locked }`; `public static class AutoReadinessPolicy { const int Window = 20; const int MinMatches = 10; static AutoReadinessVerdict Verdict(AutoReadinessEntry entry); static int Progress(AutoReadinessEntry entry); }`; `public static class AutoReadinessStore { static AutoReadinessEntry Current(string botKey); }`; `UsageSnapshot.readiness` (`List<AutoReadinessEntry>`, null у старого сервера).

- [ ] **Step 1: Падающие тесты — создать `Assets/Tests/Editor/Billing/AutoReadinessPolicyTests.cs`**

```csharp
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Learning B: the lock is a hint, never a guard — Locked ONLY on the server's positive verdict.</summary>
public class AutoReadinessPolicyTests
{
    [SetUp] public void SetUp() => UsageStore.ResetSeamsForTests();
    [TearDown] public void TearDown() => UsageStore.ResetSeamsForTests();

    private static AutoReadinessEntry Entry(string key, int matches, int total, bool ready) =>
        new AutoReadinessEntry { botKey = key, matches = matches, total = total, chats = 3, days = 2, ready = ready };

    [Test]
    public void NullEntry_IsOpen_AndProgressZero()
    {
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(null), "no snapshot / no row / old server → never locked");
        Assert.AreEqual(0, AutoReadinessPolicy.Progress(null));
    }

    [Test]
    public void ReadyFalse_IsLocked_ReadyTrue_IsOpen()
    {
        Assert.AreEqual(AutoReadinessVerdict.Locked, AutoReadinessPolicy.Verdict(Entry("Bot0", 7, 12, false)));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(Entry("Bot0", 12, 15, true)));
        Assert.AreEqual(AutoReadinessVerdict.Locked, AutoReadinessPolicy.Verdict(Entry("Bot0", 0, 0, false)), "a brand-new bot starts locked");
    }

    [Test]
    public void Progress_IsMatchesClampedToTheWindow()
    {
        Assert.AreEqual(7, AutoReadinessPolicy.Progress(Entry("Bot0", 7, 12, false)));
        Assert.AreEqual(AutoReadinessPolicy.Window, AutoReadinessPolicy.Progress(Entry("Bot0", 25, 20, true)), "the server's window bounds the number we print");
        Assert.AreEqual(0, AutoReadinessPolicy.Progress(Entry("Bot0", -3, 0, false)), "a garbage negative never prints");
        Assert.AreEqual(20, AutoReadinessPolicy.Window);
        Assert.AreEqual(10, AutoReadinessPolicy.MinMatches);
    }

    [Test]
    public void Store_FindsTheBotsRow_ByBotKey_AndNothingElse()
    {
        UsageStore.Apply(new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry> { Entry("Bot0", 3, 5, false), Entry("Bot1", 11, 14, true) } });
        Assert.AreEqual(3, AutoReadinessStore.Current("Bot0").matches);
        Assert.IsTrue(AutoReadinessStore.Current("Bot1").ready);
        Assert.IsNull(AutoReadinessStore.Current("Bot7"), "a bot the server did not list");
        Assert.IsNull(AutoReadinessStore.Current(""), "no key");
        Assert.IsNull(AutoReadinessStore.Current(null));
    }

    [Test]
    public void Store_WithoutSnapshot_OrWithoutTheField_IsNull_AndOpen()
    {
        Assert.IsNull(AutoReadinessStore.Current("Bot0"), "no snapshot yet");
        UsageStore.Apply(new UsageSnapshot { success = true });   // an old server: no `readiness` field → null list
        Assert.IsNull(AutoReadinessStore.Current("Bot0"));
        Assert.AreEqual(AutoReadinessVerdict.Open, AutoReadinessPolicy.Verdict(AutoReadinessStore.Current("Bot0")));
    }
}
```

В `Assets/Tests/Editor/Billing/UsageStoreTests.cs` добавить перед закрывающей скобкой класса:

```csharp
    // --- learning B ----------------------------------------------------------------------

    [Test] public void Parse_ReadsReadiness_AndTreatsAMissingFieldAsNull()
    {
        var with = UsageStore.Parse("{\"success\":true,\"plan\":\"trial\",\"readiness\":[{\"botKey\":\"Bot0\",\"matches\":7,\"total\":12,\"chats\":3,\"days\":2,\"ready\":false}]}");
        Assert.AreEqual(1, with.readiness.Count);
        Assert.AreEqual("Bot0", with.readiness[0].botKey);
        Assert.AreEqual(7, with.readiness[0].matches);
        Assert.AreEqual(12, with.readiness[0].total);
        Assert.IsFalse(with.readiness[0].ready);

        var without = UsageStore.Parse(ValidJson);
        Assert.IsNull(without.readiness, "an old server never breaks the snapshot");
        Assert.AreEqual(1000, without.quota);
    }
```

- [ ] **Step 2: Компиляция падает**

Run: `Tools/run-tests-headless.sh "AutoReadinessPolicyTests|UsageStoreTests"`
Expected: ошибки компиляции `The type or namespace name 'AutoReadinessEntry' could not be found`, `'UsageSnapshot' does not contain a definition for 'readiness'`.

- [ ] **Step 3: Реализация**

`Assets/Scripts/Billing/UsageStore.cs`, класс `UsageSnapshot`, после `public string interval;`:

```csharp

    /// <summary>
    /// Learning B (2026-10-14): per-bot readiness of «Авто» — the server's verdict over the last 20 owner sends with a shown
    /// set (Get Usage `readiness`). Null on a payload from an older server; an empty list for a legacy (token-less) caller.
    /// Read through <see cref="AutoReadinessStore.Current"/>, judged by <see cref="AutoReadinessPolicy"/>.
    /// </summary>
    public System.Collections.Generic.List<AutoReadinessEntry> readiness;
```

Создать `Assets/Scripts/Billing/AutoReadiness.cs`:

```csharp
using System;
using System.Collections.Generic;

/// <summary>One bot's row of Get Usage `readiness` (wire model; field names match the JSON verbatim).</summary>
[Serializable]
public class AutoReadinessEntry
{
    public string botKey;   // the bot's PlayerPrefs key (Bot0, …) = bot_profiles.bot_key
    public int matches;     // card #1 matched the sent text, within the window
    public int total;       // owner sends WITH a shown set, within the window (≤ Window)
    public int chats;       // distinct personal chats among them
    public int days;        // distinct calendar days among them
    public bool ready;      // the server's verdict: total ≥ 10, matches ≥ 10, chats ≥ 3, days ≥ 2
}

public enum AutoReadinessVerdict { Open, Locked }

/// <summary>
/// Learning B: whether enabling «Авто» should first show «Бот ещё учится». The lock is a HINT, never a guard —
/// Locked only on the server's positive verdict (the bot is listed and not ready); no snapshot, no row, an old server or a
/// legacy caller all read as Open. The thresholds the server decides with live in Tools/n8n/apply-learning-b.py; the two
/// numbers mirrored here are for the copy only (tests/test_learning_b.py keeps them in step).
/// </summary>
public static class AutoReadinessPolicy
{
    public const int Window = 20;
    public const int MinMatches = 10;

    public static AutoReadinessVerdict Verdict(AutoReadinessEntry entry) =>
        entry != null && !entry.ready ? AutoReadinessVerdict.Locked : AutoReadinessVerdict.Open;

    /// <summary>Matches to print on the capsule and the sheet — clamped to the window the server counted.</summary>
    public static int Progress(AutoReadinessEntry entry) =>
        entry == null ? 0 : Math.Clamp(entry.matches, 0, Window);
}

/// <summary>Reads a bot's readiness row out of the last Get Usage snapshot (<see cref="UsageStore.Current"/>).</summary>
public static class AutoReadinessStore
{
    public static AutoReadinessEntry Current(string botKey)
    {
        if (string.IsNullOrEmpty(botKey)) return null;
        List<AutoReadinessEntry> rows = UsageStore.Current?.readiness;
        if (rows == null) return null;
        foreach (AutoReadinessEntry row in rows)
            if (row != null && row.botKey == botKey) return row;
        return null;
    }
}
```

- [ ] **Step 4: Тесты зелёные**

Run: `Tools/run-tests-headless.sh "AutoReadinessPolicyTests|UsageStoreTests|EntitlementGateTests"`
Expected: `failed = 0`; появились `Assets/Scripts/Billing/AutoReadiness.cs.meta` и `Assets/Tests/Editor/Billing/AutoReadinessPolicyTests.cs.meta`. Затем `python3 Tools/n8n/tests/test_learning_b.py` — кейс constants теперь проверяет и C# (pass).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Billing/AutoReadiness.cs Assets/Scripts/Billing/AutoReadiness.cs.meta Assets/Scripts/Billing/UsageStore.cs Assets/Tests/Editor/Billing/AutoReadinessPolicyTests.cs Assets/Tests/Editor/Billing/AutoReadinessPolicyTests.cs.meta Assets/Tests/Editor/Billing/UsageStoreTests.cs
git commit -m "feat(billing): learning B — UsageSnapshot.readiness, AutoReadinessStore и чистая AutoReadinessPolicy (заперто только по вердикту сервера)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Копия, лист «Бот ещё учится», Back-роутер

**Files:**
- Create: `Assets/Scripts/Main/AutoReadinessCopy.cs`
- Create: `Assets/Scripts/Main/AutoLockSheet.cs`
- Modify: `Assets/Scripts/Main/AndroidBackRouter.cs` (строка после `new BackTarget("bot-activation-confirm", …)`)
- Modify: `Assets/Tests/Editor/FontGlyphCoverageTests.cs` (список `constTypes` в `SeamStrings()` + три composed samples)
- Test: `Assets/Tests/Editor/Chat/AutoLockSheetTests.cs` (новый), `Assets/Tests/Editor/Chat/BackNavigationTests.cs` (+1 тест)

**Interfaces:**
- Consumes: `AutoReadinessPolicy.Window/MinMatches` (Task 3); `PopupUI.Show(GameObject)`, `PopupUI.Hide(GameObject)`, `PopupUI.WireFingerUp(Button|GameObject, Action)`, `PopupUI.AbsorbEvents(Component)`, `PopupUI.DefaultBackdropAlpha`; `ConfirmCardFitter.Baseline`, `ConfirmCardFitter.Capture/Fit(RectTransform, TMP, TMP, ref Baseline)`; `Theme.Color(ThemeRole)`; `DelayedFingerUpAction` (тест).
- Produces: `AutoReadinessCopy.{SheetTitle, Understood, EnableAnyway, SheetBody(int matches, bool perChat), PillText(int matches)}`; `AutoLockSheet.Show(TMP_FontAsset font, int matches, bool perChat, Action proceed)`, `AutoLockSheet.IsShowing`, `AutoLockSheet.Dismiss()`, `AutoLockSheet.EnableAnyway()`, `internal AutoLockSheet.PanelForTests`, `internal AutoLockSheet.ResetForTests()`.

- [ ] **Step 1: Падающие тесты — создать `Assets/Tests/Editor/Chat/AutoLockSheetTests.cs`**

```csharp
using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

/// <summary>
/// Learning B: the soft lock's sheet — copy, the two actions, Back = «Понятно». The taps go through the very
/// DelayedFingerUpAction the sheet wired (PopupUI.WireFingerUp's real-release event), as ReconnectBotsSheetTests does.
/// </summary>
public class AutoLockSheetTests
{
    [SetUp] public void SetUp() => AutoLockSheet.ResetForTests();
    [TearDown] public void TearDown() => AutoLockSheet.ResetForTests();

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform hit = Find(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static void Tap(Transform target)
    {
        Assert.IsNotNull(target, "tap target");
        var handler = target.GetComponent<DelayedFingerUpAction>();
        Assert.IsNotNull(handler, $"{target.name} is wired through PopupUI.WireFingerUp");
        var field = typeof(DelayedFingerUpAction).GetField("OnRealRelease", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "DelayedFingerUpAction.OnRealRelease backing field");
        ((Action)field.GetValue(handler))?.Invoke();
    }

    [Test]
    public void Copy_NamesTheProgressAndTheThreshold_NoForbiddenGlyphs()
    {
        Assert.AreEqual("Бот ещё учится", AutoReadinessCopy.SheetTitle);
        string body = AutoReadinessCopy.SheetBody(7, perChat: false);
        StringAssert.Contains("10 раз из последних 20", body);
        StringAssert.Contains("сейчас 7 из 20", body);
        StringAssert.DoesNotContain("в этом чате", body);
        StringAssert.Contains("Включить авто-режим в этом чате можно и сейчас.", AutoReadinessCopy.SheetBody(7, perChat: true));
        Assert.AreEqual("Учится · 7/20", AutoReadinessCopy.PillText(7));
        Assert.AreEqual("Понятно", AutoReadinessCopy.Understood);
        Assert.AreEqual("Включить всё равно", AutoReadinessCopy.EnableAnyway);
        foreach (string s in new[] { AutoReadinessCopy.SheetTitle, body, AutoReadinessCopy.PillText(0), AutoReadinessCopy.Understood, AutoReadinessCopy.EnableAnyway })
            foreach (char forbidden in new[] { '№', '→', '−', '✕' })
                Assert.IsFalse(s.Contains(forbidden), $"«{forbidden}» is not in the SF Pro atlases: {s}");
    }

    [Test]
    public void Show_PaintsTheBody_ThenUnderstoodClosesWithoutProceeding()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, 7, perChat: false, () => proceeded++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        Assert.AreEqual(AutoReadinessCopy.SheetBody(7, perChat: false), Find(panel, "Body").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(AutoReadinessCopy.Understood, Find(Find(panel, "DismissButton"), "Label").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(AutoReadinessCopy.EnableAnyway, Find(Find(panel, "EnableButton"), "Label").GetComponent<TextMeshProUGUI>().text);

        Tap(Find(panel, "DismissButton"));
        Assert.IsFalse(AutoLockSheet.IsShowing);
        Assert.AreEqual(0, proceeded, "«Понятно» (and Back) never enables «Авто»");
    }

    [Test]
    public void Show_ThenEnableAnywayTap_ProceedsExactlyOnce_AndCloses()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, 3, perChat: true, () => proceeded++);
        Tap(Find(AutoLockSheet.PanelForTests.transform, "EnableButton"));
        Assert.AreEqual(1, proceeded, "the soft lock stays soft: the override runs the ordinary enable path");
        Assert.IsFalse(AutoLockSheet.IsShowing);
        AutoLockSheet.EnableAnyway();   // a second call after the close is inert
        Assert.AreEqual(1, proceeded);
    }

    [Test]
    public void Back_IsUnderstood_AndAReshowRepaintsTheProgress()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, 2, perChat: false, () => proceeded++);
        AutoLockSheet.Dismiss();                       // what AndroidBackRouter calls
        Assert.IsFalse(AutoLockSheet.IsShowing);
        AutoLockSheet.Show(null, 9, perChat: false, () => proceeded++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        StringAssert.Contains("сейчас 9 из 20", Find(AutoLockSheet.PanelForTests.transform, "Body").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(0, proceeded);
    }
}
```

В `Assets/Tests/Editor/Chat/BackNavigationTests.cs` добавить тест (рядом с `TheReconnectSheet_IsOnTheRouter_AndBackDoesWhatItsLaterDoes`; `File`, `Path`, `Application` уже в usings):

```csharp
    /// <summary>
    /// Learning B: the «Бот ещё учится» sheet (a runtime overlay, sortingOrder 3) joins the router in its z-order — above the
    /// scene popups and the paywall — and Back does what its own «Понятно» does: <c>AutoLockSheet.Dismiss</c>, which closes
    /// the sheet and enables nothing. Source guard; the dismiss behaviour itself is pinned on the real sheet by AutoLockSheetTests.
    /// </summary>
    [Test]
    public void TheAutoLockSheet_IsOnTheRouter_AndBackIsUnderstood()
    {
        string router = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/AndroidBackRouter.cs"));
        const string entry = "new BackTarget(\"auto-lock-sheet\", () => AutoLockSheet.IsShowing, AutoLockSheet.Dismiss)";
        int sheet = router.IndexOf(entry, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(sheet, 0, "the learning sheet must be a Back target, closed by its own «Понятно» action");
        foreach (string below in new[] { "new BackTarget(\"popup\"", "new BackTarget(\"paywall\"", "new BackTarget(\"bot-settings\"" })
            Assert.Less(sheet, router.IndexOf(below, System.StringComparison.Ordinal), $"the overlay sheet sits above {below}");
        Assert.Greater(sheet, router.IndexOf("new BackTarget(\"loading\"", System.StringComparison.Ordinal),
            "a loading cover still swallows Back first");
    }
```

- [ ] **Step 2: Компиляция падает**

Run: `Tools/run-tests-headless.sh "AutoLockSheetTests|BackNavigationTests"`
Expected: `The name 'AutoLockSheet' does not exist`, `'AutoReadinessCopy' does not exist`.

- [ ] **Step 3: Реализация**

`Assets/Scripts/Main/AutoReadinessCopy.cs`:

```csharp
/// <summary>
/// Learning B (2026-10-14): every string of the soft «Авто» lock — the capsule's learning face and the «Бот ещё учится»
/// sheet. RU only (house rule); the numbers come from AutoReadinessPolicy so the copy and the verdict cannot drift.
/// Registered in FontGlyphCoverageTests (const strings + the three composed samples). No counted nouns here — «раз» and
/// «из» do not decline with the number — so RuPlural is not needed.
/// </summary>
public static class AutoReadinessCopy
{
    public const string SheetTitle = "Бот ещё учится";
    public const string Understood = "Понятно";
    public const string EnableAnyway = "Включить всё равно";

    public static string SheetBody(int matches, bool perChat)
    {
        string core = $"Авто включится, когда панель угадает ваш ответ {AutoReadinessPolicy.MinMatches} раз из последних "
                    + $"{AutoReadinessPolicy.Window} — сейчас {matches} из {AutoReadinessPolicy.Window}. "
                    + "Отвечайте через «Вместе»: каждая отправка учит бота.";
        return perChat ? core + " Включить авто-режим в этом чате можно и сейчас." : core;
    }

    /// <summary>The capsule's learning face: «Учится · 7/20».</summary>
    public static string PillText(int matches) => $"Учится · {matches}/{AutoReadinessPolicy.Window}";
}
```

`Assets/Scripts/Main/AutoLockSheet.cs` — устройство `BotActivationConfirm` один в один (те же builders, тот же canvas), плюс флаг `hiding` из `ReconnectBotsSheet` (PopupUI.Hide деактивирует панель в OnComplete твина — в EditMode он не доходит):

```csharp
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Learning B: the soft lock's sheet, shown by AutoLockGate when the server says the bot is not ready. Built lazily on
/// its own overlay canvas (sortingOrder 3) exactly like BotActivationConfirm — the gate fires from four screens, and the
/// house rule puts a scene sheet inside the screen that raises it. «Понятно» (and Back) close it; «Включить всё равно»
/// runs the caller's `proceed` (the ordinary enable path: its confirm, then CommitMode) exactly once. Buttons are wired
/// ONCE through PopupUI.WireFingerUp; the pending action is swapped per Show.
/// </summary>
public static class AutoLockSheet
{
    public const float CardWidth = 720f;
    public const float CardHeight = 480f;   // 40u taller than BotActivationConfirm: the body is four lines at 32pt
    public const float TitleTop = 56f, TitleHeight = 60f, TitleSideInset = 48f;
    public const float BodyTop = 136f, BodyHeight = 180f, BodySideInset = 56f;
    public const float ButtonY = 44f, ButtonHeight = 96f, ButtonWidth = 300f;
    private const int OverlaySortingOrder = 3;   // above screens, below SelectionOverlay (4)

    private static GameObject panel;
    private static Image cardImage, dismissImage, enableImage;
    private static TextMeshProUGUI titleTmp, bodyTmp, dismissLabel, enableLabel;
    private static Action pending;
    private static bool hiding;
    private static ConfirmCardFitter.Baseline baseline;

    /// <summary>True while the sheet is up and not already leaving (Back router).</summary>
    public static bool IsShowing => panel != null && panel.activeSelf && !hiding;

    /// <summary>EditMode seam: the built panel (null until the first <see cref="Show"/>).</summary>
    internal static GameObject PanelForTests => panel;

    /// <summary>Raise the sheet for a bot with <paramref name="matches"/> of the window; <paramref name="proceed"/> runs only on «Включить всё равно».</summary>
    public static void Show(TMP_FontAsset font, int matches, bool perChat, Action proceed)
    {
        EnsureBuilt(font);
        pending = proceed;
        hiding = false;
        bodyTmp.text = AutoReadinessCopy.SheetBody(matches, perChat);
        Paint();
        PopupUI.Show(panel);
        // After Show: TMP measures only on an active object (ConfirmCardFitter's rule).
        ConfirmCardFitter.Fit((RectTransform)cardImage.transform, titleTmp, bodyTmp, ref baseline);
    }

    /// <summary>«Понятно», a tap outside the card and the system Back: close, enable nothing.</summary>
    public static void Dismiss()
    {
        if (!IsShowing) return;
        pending = null;
        hiding = true;
        PopupUI.Hide(panel);
    }

    /// <summary>«Включить всё равно»: close, then run the ordinary enable path once.</summary>
    public static void EnableAnyway()
    {
        if (!IsShowing) return;
        Action proceed = pending;
        pending = null;
        hiding = true;
        PopupUI.Hide(panel);
        proceed?.Invoke();
    }

    /// <summary>EditMode seam: destroys the overlay and forgets every static, like a fresh play-mode enter.</summary>
    internal static void ResetForTests()
    {
        if (panel != null) UnityEngine.Object.DestroyImmediate(panel.transform.root.gameObject);
        panel = null;
        cardImage = dismissImage = enableImage = null;
        titleTmp = bodyTmp = dismissLabel = enableLabel = null;
        pending = null;
        hiding = false;
        baseline = default;
    }

    private static void EnsureBuilt(TMP_FontAsset font)
    {
        if (panel != null) return;

        var canvasGo = new GameObject("AutoLockSheetCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;

        panel = NewChild(canvasGo.transform, "Panel", typeof(Image));
        StretchFull((RectTransform)panel.transform);
        var backdrop = panel.GetComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, PopupUI.DefaultBackdropAlpha);
        backdrop.raycastTarget = true;
        PopupUI.WireFingerUp(panel, Dismiss);   // tap outside the card = «Понятно»

        BuildCard(font);
        panel.SetActive(false);
    }

    private static void BuildCard(TMP_FontAsset font)
    {
        var card = NewChild(panel.transform, "Content", typeof(Image));
        var cardRt = (RectTransform)card.transform;
        cardRt.sizeDelta = new Vector2(CardWidth, CardHeight);
        cardImage = card.GetComponent<Image>();
        cardImage.raycastTarget = true;
        AddRounded(card, 40f);
        PopupUI.AbsorbEvents(cardImage);

        titleTmp = BuildTmp(card.transform, "Title", AutoReadinessCopy.SheetTitle, 44f, FontStyles.Bold, font);
        titleTmp.textWrappingMode = TextWrappingModes.Normal;
        SetTopStretch((RectTransform)titleTmp.transform, top: TitleTop, height: TitleHeight, sideInset: TitleSideInset);

        bodyTmp = BuildTmp(card.transform, "Body", AutoReadinessCopy.SheetBody(0, perChat: false), 32f, FontStyles.Normal, font);
        bodyTmp.enableWordWrapping = true;
        SetTopStretch((RectTransform)bodyTmp.transform, top: BodyTop, height: BodyHeight, sideInset: BodySideInset);

        (dismissImage, dismissLabel) = BuildButton(card.transform, "DismissButton", AutoReadinessCopy.Understood,
            font, anchoredX: -170f, Dismiss);
        (enableImage, enableLabel) = BuildButton(card.transform, "EnableButton", AutoReadinessCopy.EnableAnyway,
            font, anchoredX: 170f, EnableAnyway);

        ConfirmCardFitter.Capture(cardRt, titleTmp, bodyTmp, ref baseline);
    }

    private static (Image, TextMeshProUGUI) BuildButton(Transform card, string name, string label,
        TMP_FontAsset font, float anchoredX, Action action)
    {
        var go = NewChild(card, name, typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(anchoredX, ButtonY);
        rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        AddRounded(go, 48f);

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = go.GetComponent<Image>();
        PopupUI.WireFingerUp(button, action);

        // 30pt, not the confirm's 34: «Включить всё равно» must fit a 300u button on one line.
        var tmp = BuildTmp(go.transform, "Label", label, 30f, FontStyles.Bold, font);
        StretchFull((RectTransform)tmp.transform);
        return (go.GetComponent<Image>(), tmp);
    }

    // Theme snapshot at show time (the sheet is transient). «Понятно» is the primary action — keep learning in «Вместе»;
    // the override is the quiet secondary button.
    private static void Paint()
    {
        if (cardImage != null) cardImage.color = Theme.Color(ThemeRole.Surface);
        if (titleTmp != null) titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        if (bodyTmp != null) bodyTmp.color = Theme.Color(ThemeRole.InkSecondary);
        if (dismissImage != null) dismissImage.color = Theme.Color(ThemeRole.AccentFill);
        if (dismissLabel != null) dismissLabel.color = Theme.Color(ThemeRole.AccentOnFill);
        if (enableImage != null) enableImage.color = Theme.Color(ThemeRole.Background);
        if (enableLabel != null) enableLabel.color = Theme.Color(ThemeRole.InkSecondary);
    }

    // ---- build helpers (BotActivationConfirm's, verbatim) ------------------

    private static GameObject NewChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetTopStretch(RectTransform rt, float top, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
    }

    private static TextMeshProUGUI BuildTmp(Transform parent, string name, string text,
        float fontSize, FontStyles style, TMP_FontAsset font)
    {
        var go = NewChild(parent, name, typeof(TextMeshProUGUI));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = -2f;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    // RoundedCorners lives in its own UPM assembly — scan loaded assemblies
    // (project memory: Type.GetType against Assembly-CSharp silently fails).
    private static Type cachedRoundedType;

    private static void AddRounded(GameObject go, float radius)
    {
        if (cachedRoundedType == null)
        {
            const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                cachedRoundedType = asm.GetType(fullName);
                if (cachedRoundedType != null) break;
            }
            if (cachedRoundedType == null) return;
        }

        Component rc = go.GetComponent(cachedRoundedType) ?? go.AddComponent(cachedRoundedType);
        cachedRoundedType.GetField("radius")?.SetValue(rc, radius);
        cachedRoundedType.GetField("image")?.SetValue(rc, go.GetComponent<Image>());
    }
}
```

`Assets/Scripts/Main/AndroidBackRouter.cs` — после строки `new BackTarget("bot-activation-confirm", () => BotActivationConfirm.IsShowing, BotActivationConfirm.Cancel),`:

```csharp
            new BackTarget("auto-lock-sheet", () => AutoLockSheet.IsShowing, AutoLockSheet.Dismiss),   // learning B: Back = «Понятно»
```

`Assets/Tests/Editor/FontGlyphCoverageTests.cs`: в массив `constTypes` после строки `typeof(RetiredChannelCopy),    // F2 reconnect screens…`:

```csharp
            typeof(AutoReadinessCopy),     // learning B: capsule «Учится · N/20» + sheet «Бот ещё учится» (2026-10-14)
```

и после `yield return ("RetiredChannelCopy.SheetRow(tg)", …);`:

```csharp
        // learning B: the soft-lock sheet body mid-way and the capsule's learning face (2026-10-14)
        yield return ("AutoReadinessCopy.SheetBody(bot)", AutoReadinessCopy.SheetBody(7, perChat: false));
        yield return ("AutoReadinessCopy.SheetBody(chat)", AutoReadinessCopy.SheetBody(7, perChat: true));
        yield return ("AutoReadinessCopy.PillText", AutoReadinessCopy.PillText(7));
```

- [ ] **Step 4: Тесты зелёные**

Run: `Tools/run-tests-headless.sh "AutoLockSheetTests|BackNavigationTests|FontGlyphCoverageTests|ConfirmCardFitterTests"`
Expected: `failed = 0`; новые `.meta` для `AutoReadinessCopy.cs`, `AutoLockSheet.cs`, `AutoLockSheetTests.cs`. Если `AutoLockSheetTests` падает на `Find(panel, "Body")` — панель строится при первом `Show`, имена узлов `Panel/Content/{Title,Body,DismissButton/Label,EnableButton/Label}`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/AutoReadinessCopy.cs Assets/Scripts/Main/AutoReadinessCopy.cs.meta Assets/Scripts/Main/AutoLockSheet.cs Assets/Scripts/Main/AutoLockSheet.cs.meta Assets/Scripts/Main/AndroidBackRouter.cs Assets/Tests/Editor/FontGlyphCoverageTests.cs Assets/Tests/Editor/Chat/AutoLockSheetTests.cs Assets/Tests/Editor/Chat/AutoLockSheetTests.cs.meta Assets/Tests/Editor/Chat/BackNavigationTests.cs
git commit -m "feat(ui): learning B — лист «Бот ещё учится» (AutoLockSheet: «Понятно» / «Включить всё равно»), копия AutoReadinessCopy, Back-роутер

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Гейт `AutoLockGate` и четыре точки включения

**Files:**
- Create: `Assets/Scripts/Main/AutoLockGate.cs`
- Modify: `Assets/Scripts/Main/Bot.cs` (`OnAutoPillTapped`, строки 426–433), `Assets/Scripts/UI/ReplyModeToggleBinder.cs` (`RequestEnableAuto`, строки 135–141), `Assets/Scripts/UI/SemiAutoToggle.cs` (`HandlePressed`, строки 99–102)
- Test: `Assets/Tests/Editor/Chat/AutoLockGateTests.cs` (новый), `Assets/Tests/Editor/Chat/AutoLockWiringTests.cs` (новый, source-guard)

**Interfaces:**
- Consumes: `AutoReadinessStore.Current`, `AutoReadinessPolicy.Verdict/Progress` (Task 3), `AutoLockSheet.Show/IsShowing/Dismiss/EnableAnyway/ResetForTests` (Task 4), `ChatManager.Instance.CurrentBotId` (public getter, `ChatManager.BotState.cs`).
- Produces: `public static class AutoLockGate { static void TryEnable(string botKey, TMP_FontAsset font, bool perChat, Action proceed); }` — единственная дверь к включению «Авто» владельцем.

- [ ] **Step 1: Падающие тесты**

`Assets/Tests/Editor/Chat/AutoLockGateTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Learning B: the one door to enabling «Авто» — straight through on Open, the sheet on Locked.</summary>
public class AutoLockGateTests
{
    [SetUp] public void SetUp() { UsageStore.ResetSeamsForTests(); AutoLockSheet.ResetForTests(); }
    [TearDown] public void TearDown() { UsageStore.ResetSeamsForTests(); AutoLockSheet.ResetForTests(); }

    private static void Snapshot(params AutoReadinessEntry[] rows) =>
        UsageStore.Apply(new UsageSnapshot { success = true, readiness = new List<AutoReadinessEntry>(rows) });

    [Test]
    public void OpenVerdict_ProceedsAtOnce_NoSheet()
    {
        int n = 0;
        AutoLockGate.TryEnable("Bot0", null, perChat: false, () => n++);           // no snapshot at all
        Snapshot(new AutoReadinessEntry { botKey = "Bot0", matches = 12, total = 15, chats = 3, days = 2, ready = true });
        AutoLockGate.TryEnable("Bot0", null, perChat: false, () => n++);           // ready
        AutoLockGate.TryEnable("Bot9", null, perChat: false, () => n++);           // not listed
        AutoLockGate.TryEnable(null, null, perChat: false, () => n++);             // no key (a chat with no bot context)
        Assert.AreEqual(4, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }

    [Test]
    public void LockedVerdict_ShowsTheSheet_AndProceedsOnlyOnEnableAnyway()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = "Bot0", matches = 7, total = 12, chats = 3, days = 2, ready = false });
        AutoLockGate.TryEnable("Bot0", null, perChat: false, () => n++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Assert.AreEqual(0, n, "the lock defers the enable");
        AutoLockSheet.EnableAnyway();
        Assert.AreEqual(1, n);
    }

    [Test]
    public void LockedVerdict_Understood_EnablesNothing()
    {
        int n = 0;
        Snapshot(new AutoReadinessEntry { botKey = "Bot0", matches = 0, total = 0, chats = 0, days = 0, ready = false });
        AutoLockGate.TryEnable("Bot0", null, perChat: true, () => n++);
        AutoLockSheet.Dismiss();
        Assert.AreEqual(0, n);
        Assert.IsFalse(AutoLockSheet.IsShowing);
    }
}
```

`Assets/Tests/Editor/Chat/AutoLockWiringTests.cs` (source-guard; `MethodBody` — тот же хелпер, что в `SendOriginWiringTests`):

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Learning B source guard: every path that turns «Авто» ON for the owner goes through AutoLockGate.TryEnable BEFORE it
/// commits — the card capsule, the chats header + bot-switcher row (RequestEnableAuto), the per-chat toggle. Disabling and
/// the server reconciliation stay outside the gate. EditMode cannot tap a capsule, so the wiring is pinned by text.
/// </summary>
public class AutoLockWiringTests
{
    [Test]
    public void TheCapsule_TheHeaderPaths_AndThePerChatToggle_GoThroughTheGate_BeforeTheyCommit()
    {
        string card = MethodBody("Scripts/Main/Bot.cs", "OnAutoPillTapped");
        Assert.Greater(card.IndexOf("ReplyMode.Auto"), card.IndexOf("AutoLockGate.TryEnable("), "card capsule: the gate, then CommitMode(Auto)");
        Assert.GreaterOrEqual(card.IndexOf("AutoLockGate.TryEnable("), 0, "card capsule");

        string header = MethodBody("Scripts/UI/ReplyModeToggleBinder.cs", "RequestEnableAuto");
        Assert.GreaterOrEqual(header.IndexOf("AutoLockGate.TryEnable("), 0, "chats header + switcher row share RequestEnableAuto");
        Assert.Greater(header.IndexOf("ReplyMode.Auto"), header.IndexOf("AutoLockGate.TryEnable("), "header: the gate, then CommitMode(Auto)");

        string chat = MethodBody("Scripts/UI/SemiAutoToggle.cs", "HandlePressed");
        Assert.GreaterOrEqual(chat.IndexOf("AutoLockGate.TryEnable("), 0, "per-chat toggle");
        Assert.IsTrue(chat.Contains("perChat: true"), "the per-chat sheet says «в этом чате»");
        Assert.Greater(chat.IndexOf("OnToggled?.Invoke(false)"), chat.IndexOf("AutoLockGate.TryEnable("), "per-chat: the gate, then the enable");
    }

    [Test]
    public void DisablingAndServerReconcile_StayOutsideTheGate()
    {
        Assert.IsFalse(MethodBody("Scripts/UI/ReplyModeToggleBinder.cs", "DisableAuto").Contains("AutoLockGate"), "switching back to «Вместе» is instant");
        Assert.IsFalse(MethodBody("Scripts/UI/ReplyModeToggleBinder.cs", "ReconcileFromServer").Contains("AutoLockGate"), "a server-side truth is not the owner's tap");
    }

    [Test]
    public void OnlyTheGateAndTheCard_ReadTheReadinessStore()
    {
        var users = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string file = Path.GetFileName(path);
            if (file == "AutoLockGate.cs" || file == "Bot.cs" || file == "AutoReadiness.cs") continue;
            if (File.ReadAllText(path).Contains("AutoReadinessStore.Current(")) users.Add(file);
        }
        Assert.IsEmpty(users, "only the gate (the lock) and the card (its face) read readiness: " + string.Join(", ", users));
    }

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update AutoLockWiringTests");
        string source = File.ReadAllText(path);
        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!return\b)[\w<>.]+[ \t]+" + Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"{method} not found in {relativePath}");
        int open = source.IndexOf('{', decl.Index);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}
```

(`DisableAuto` — expression-bodied `=> CommitMode(...)`; хелпер ищет `{` после объявления и возьмёт тело СЛЕДУЮЩЕГО метода — если `Assert.IsFalse` для `DisableAuto` окажется ложно-зелёным/красным из-за этого, заменить проверку на `Regex.IsMatch(binderSource, @"DisableAuto\(string botName\) => CommitMode\(botName, ReplyMode\.Semi\);")` — выключение остаётся одной строкой без гейта.)

- [ ] **Step 2: Компиляция падает**

Run: `Tools/run-tests-headless.sh "AutoLockGateTests|AutoLockWiringTests"`
Expected: `The name 'AutoLockGate' does not exist`.

- [ ] **Step 3: Реализация**

`Assets/Scripts/Main/AutoLockGate.cs`:

```csharp
using System;
using TMPro;

/// <summary>
/// Learning B: the ONE door to enabling «Авто» by the owner's hand. Open verdict → the ordinary path runs at once;
/// Locked (the server listed the bot and says not ready) → the «Бот ещё учится» sheet, whose «Включить всё равно» runs
/// the same path. Pinned by AutoLockWiringTests: the capsule, RequestEnableAuto (chats header + switcher row) and the
/// per-chat toggle all call this before they commit; DisableAuto and ReconcileFromServer never do.
/// </summary>
public static class AutoLockGate
{
    public static void TryEnable(string botKey, TMP_FontAsset font, bool perChat, Action proceed)
    {
        AutoReadinessEntry entry = AutoReadinessStore.Current(botKey);
        if (AutoReadinessPolicy.Verdict(entry) == AutoReadinessVerdict.Open)
        {
            proceed?.Invoke();
            return;
        }
        AutoLockSheet.Show(font, AutoReadinessPolicy.Progress(entry), perChat, proceed);
    }
}
```

`Assets/Scripts/Main/Bot.cs`, в `OnAutoPillTapped` заменить

```csharp
        if (AutoButtonModel.ConfirmRequired(ReplyModeToggleBinder.GetMode(transform.name)))
            BotActivationConfirm.Show(BotName != null ? BotName.font : null, () =>
            {
                if (this != null)   // card may die before confirm
                    ReplyModeToggleBinder.CommitMode(transform.name, ReplyModeToggleBinder.ReplyMode.Auto);
            });
        else
            ReplyModeToggleBinder.DisableAuto(transform.name);
```

на

```csharp
        if (AutoButtonModel.ConfirmRequired(ReplyModeToggleBinder.GetMode(transform.name)))
        {
            string botKey = transform.name;
            TMP_FontAsset font = BotName != null ? BotName.font : null;
            // learning B: the soft lock first (the server's verdict), then the activation confirm — the same two-step on every path.
            AutoLockGate.TryEnable(botKey, font, perChat: false, () =>
                BotActivationConfirm.Show(font, () =>
                {
                    if (this != null)   // card may die before confirm
                        ReplyModeToggleBinder.CommitMode(botKey, ReplyModeToggleBinder.ReplyMode.Auto);
                }));
        }
        else
            ReplyModeToggleBinder.DisableAuto(transform.name);
```

(`using TMPro;` в Bot.cs уже есть.)

`Assets/Scripts/UI/ReplyModeToggleBinder.cs`, `RequestEnableAuto` целиком:

```csharp
    public static void RequestEnableAuto(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;

        // learning B: the soft lock first (the server's verdict), then the shared confirm; «Включить всё равно» re-enters here.
        TMP_FontAsset font = instance != null && instance.confirmTitle != null ? instance.confirmTitle.font : null;
        AutoLockGate.TryEnable(botName, font, perChat: false, () =>
        {
            if (!ShowConfirm(EnableTitle, EnableBody, () => CommitMode(botName, ReplyMode.Auto)))
                CommitMode(botName, ReplyMode.Auto);
        });
    }
```

`Assets/Scripts/UI/SemiAutoToggle.cs`, в `HandlePressed` заменить

```csharp
        if (!ReplyModeToggleBinder.ShowConfirm(EnableTitle, EnableBody, () => OnToggled?.Invoke(false)))
            OnToggled?.Invoke(false);  // no popup wired — commit straight away
```

на

```csharp
        // learning B: the per-chat enable meets the same soft lock (the sheet says «в этом чате»), then its own confirm.
        string botKey = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : null;
        AutoLockGate.TryEnable(botKey, label != null ? label.font : null, perChat: true, () =>
        {
            if (!ReplyModeToggleBinder.ShowConfirm(EnableTitle, EnableBody, () => OnToggled?.Invoke(false)))
                OnToggled?.Invoke(false);  // no popup wired — commit straight away
        });
```

- [ ] **Step 4: Тесты зелёные (и соседние гарды)**

Run: `Tools/run-tests-headless.sh "AutoLockGateTests|AutoLockWiringTests|SendOriginWiringTests|ReconnectWiringTests|BackNavigationTests|AutoButtonModelTests|ReplyModeSyncWiringTests"`
Expected: `failed = 0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/AutoLockGate.cs Assets/Scripts/Main/AutoLockGate.cs.meta Assets/Scripts/Main/Bot.cs Assets/Scripts/UI/ReplyModeToggleBinder.cs Assets/Scripts/UI/SemiAutoToggle.cs Assets/Tests/Editor/Chat/AutoLockGateTests.cs Assets/Tests/Editor/Chat/AutoLockGateTests.cs.meta Assets/Tests/Editor/Chat/AutoLockWiringTests.cs Assets/Tests/Editor/Chat/AutoLockWiringTests.cs.meta
git commit -m "feat(ui): learning B — AutoLockGate на четырёх точках включения «Авто» (капсула, шапка чатов, переключатель ботов, пер-чат)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Капсула — третье состояние «Учится · N/20»

**Files:**
- Modify: `Assets/Scripts/Main/BotCardModel.cs` (enum `BotPillKind`, `PillKind(...)`, перегрузка `PillWidth`)
- Modify: `Assets/Scripts/Main/Bot.cs` (поля после `cardStateReady`, `OnEnable`/`OnDisable`, `RefreshCardState`, `ApplyPillFace`, новый `HandleUsageChanged`)
- Test: `Assets/Tests/Editor/Chat/BotCardModelTests.cs` (+2 теста; хелпер `FindDeep` там уже есть)

**Interfaces:**
- Consumes: `AutoReadinessStore.Current`, `AutoReadinessPolicy.Verdict/Progress`, `AutoReadinessCopy.PillText`, `UsageStore.OnUsageChanged`, `BotCardModel.ShowConnectPill/PillWidth(bool,float)/AutoPillWidth/ConnectPillPadding`.
- Produces: `public enum BotPillKind { Auto, Connect, Learning }`; `BotCardModel.PillKind(bool connect, bool autoOn, bool locked)`; `BotCardModel.PillWidth(BotPillKind kind, float labelPreferredWidth)`.

- [ ] **Step 1: Падающие тесты — добавить в `BotCardModelTests`**

```csharp
    // ---- learning B: the capsule's third face ----

    [Test]
    public void PillKind_ConnectOutranksLearning_LearningOnlyWhileSemiAndLocked()
    {
        Assert.AreEqual(BotPillKind.Connect, BotCardModel.PillKind(connect: true, autoOn: false, locked: true));
        Assert.AreEqual(BotPillKind.Learning, BotCardModel.PillKind(connect: false, autoOn: false, locked: true));
        Assert.AreEqual(BotPillKind.Auto, BotCardModel.PillKind(connect: false, autoOn: true, locked: true), "an enabled «Авто» is never relabelled");
        Assert.AreEqual(BotPillKind.Auto, BotCardModel.PillKind(connect: false, autoOn: false, locked: false), "Semi without a lock is the plain capsule");
        Assert.AreEqual(BotCardModel.AutoPillWidth, BotCardModel.PillWidth(BotPillKind.Auto, 400f), "«Авто» keeps its authored width");
        Assert.AreEqual(BotCardModel.PillWidth(true, 200f), BotCardModel.PillWidth(BotPillKind.Learning, 200f), "learning measures like «Подключить»");
        Assert.AreEqual(BotCardModel.PillWidth(true, 200f), BotCardModel.PillWidth(BotPillKind.Connect, 200f));
    }

    [Test]
    public void TheRealCapsuleLabel_FitsTheLearningFace_OnceTheCapsuleGrows()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bot.prefab");
        Assert.IsNotNull(prefab, "Bot.prefab");
        var card = Object.Instantiate(prefab);
        try
        {
            Transform pill = FindDeep(card.transform, "AutoPill");
            Assert.IsNotNull(pill, "the C2 capsule (BotCardAutoPillBuilder)");
            var label = FindDeep(pill, "Label").GetComponent<TMPro.TextMeshProUGUI>();
            float learning = label.GetPreferredValues(AutoReadinessCopy.PillText(17)).x;   // the widest two-digit progress
            Assert.Greater(learning, label.rectTransform.sizeDelta.x, "«Учится · 17/20» does not fit the authored «Авто» box — the runtime must grow it");
            float width = BotCardModel.PillWidth(BotPillKind.Learning, learning);
            TestContext.Out.WriteLine($"«{AutoReadinessCopy.PillText(17)}» {learning:F1}u → capsule {width:F0}u (authored {BotCardModel.AutoPillWidth:F0}u)");
            Assert.GreaterOrEqual(width, learning + 2f * BotCardModel.ConnectPillPadding - 0.01f, "the label sits inside the pill");
            Assert.LessOrEqual(width, 360f, $"the grown capsule ({width:F0}u) must leave the name column room on a 1080 card");
        }
        finally
        {
            Object.DestroyImmediate(card);
        }
    }
```

- [ ] **Step 2: Компиляция падает**

Run: `Tools/run-tests-headless.sh "BotCardModelTests"`
Expected: `The type or namespace name 'BotPillKind' could not be found`.

- [ ] **Step 3: Реализация**

`Assets/Scripts/Main/BotCardModel.cs` — enum над классом и два метода рядом с `PillWidth(bool connect, float labelPreferredWidth)`:

```csharp
/// <summary>The capsule's face on the bot card: the authored «Авто», «Подключить» (F2) or «Учится · N/20» (learning B).</summary>
public enum BotPillKind { Auto, Connect, Learning }
```

```csharp
    /// <summary>Learning B: which face the capsule wears. A retired channel outranks everything but «Подключение…»; the lock
    /// paints only a bot in «Вместе» the server says is not ready — an enabled «Авто» is never relabelled.</summary>
    public static BotPillKind PillKind(bool connect, bool autoOn, bool locked)
    {
        if (connect) return BotPillKind.Connect;
        if (!autoOn && locked) return BotPillKind.Learning;
        return BotPillKind.Auto;
    }

    /// <summary>Measured width for every face but the authored «Авто» (the lamp is hidden, the label is alone in the pill).</summary>
    public static float PillWidth(BotPillKind kind, float labelPreferredWidth) =>
        PillWidth(kind != BotPillKind.Auto, labelPreferredWidth);
```

`Assets/Scripts/Main/Bot.cs`:

после `    private bool cardStateReady;` (строка 108):

```csharp
    private BotPillKind pillKind = BotPillKind.Auto;   // learning B: the capsule's face, decided in RefreshCardState
    private int learningMatches;                        // learning B: the N of «Учится · N/20»
```

в `OnEnable` после `ProfileRetirement.Changed += HandleRetirementChanged;`:

```csharp
        UsageStore.OnUsageChanged += HandleUsageChanged;   // learning B: a fresh Get Usage can flip the capsule's face
```

в `OnDisable` после `ProfileRetirement.Changed -= HandleRetirementChanged;`:

```csharp
        UsageStore.OnUsageChanged -= HandleUsageChanged;
```

рядом с `HandleRetirementChanged`:

```csharp
    // learning B: «Учится · N/20» ↔ «Авто» follows the server's verdict, which arrives with Get Usage and no other card event.
    private void HandleUsageChanged()
    {
        if (cardStateReady) RefreshCardState(animatePill: false);
    }
```

в `RefreshCardState` сразу после `showsConnect = BotCardModel.ShowConnectPill(whatsappProfileId, waRetired, telegramProfileId, tgRetired);`:

```csharp
        // learning B: the capsule's third face — «Учится · N/20» while «Авто» is off and the server says the bot is not ready.
        AutoReadinessEntry readiness = AutoReadinessStore.Current(transform.name);
        learningMatches = AutoReadinessPolicy.Progress(readiness);
        pillKind = BotCardModel.PillKind(showsConnect, AutoOn, AutoReadinessPolicy.Verdict(readiness) == AutoReadinessVerdict.Locked);
```

в `ApplyPillFace` заменить

```csharp
        string text = showsConnect ? RetiredChannelCopy.ConnectPill : authoredPillText;
        float labelWidth = showsConnect ? Mathf.Ceil(autoPillLabel.GetPreferredValues(text).x) : authoredLabelWidth;
        float pillWidth = BotCardModel.PillWidth(showsConnect, labelWidth);
```

на

```csharp
        string text = pillKind == BotPillKind.Connect ? RetiredChannelCopy.ConnectPill
                    : pillKind == BotPillKind.Learning ? AutoReadinessCopy.PillText(learningMatches)
                    : authoredPillText;
        float labelWidth = pillKind == BotPillKind.Auto ? authoredLabelWidth : Mathf.Ceil(autoPillLabel.GetPreferredValues(text).x);
        float pillWidth = BotCardModel.PillWidth(pillKind, labelWidth);
```

и строку `if (autoPillDotRing != null) autoPillDotRing.gameObject.SetActive(!showsConnect);` на

```csharp
        if (autoPillDotRing != null) autoPillDotRing.gameObject.SetActive(pillKind == BotPillKind.Auto);   // the lamp belongs to «Авто» alone
```

Покраска в `RefreshCardState` не меняется: `showsConnect` → `PaintConnectPill()`, иначе `PaintChip(AutoOn, …)` — `Learning` всегда идёт с `AutoOn == false`, то есть красится как выключенное «Авто» (спека §3).

- [ ] **Step 4: Тесты зелёные**

Run: `Tools/run-tests-headless.sh "BotCardModelTests|AutoLockWiringTests|ReconnectWiringTests|BotsHeaderIconsLayoutTests"`
Expected: `failed = 0` (`AutoLockWiringTests.OnlyTheGateAndTheCard_ReadTheReadinessStore` допускает `Bot.cs`).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/BotCardModel.cs Assets/Scripts/Main/Bot.cs Assets/Tests/Editor/Chat/BotCardModelTests.cs
git commit -m "feat(ui): learning B — капсула бота показывает «Учится · N/20», пока сервер не признал бота готовым

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Стенд, полный прогон, документация, чек-лист деплоя

**Files:**
- Modify: `Tools/n8n/staging/e2e-learning.py` (`send` получает `recipient`; +1 сценарий)
- Modify: `CLAUDE.md` (абзац «Learning B» после абзаца «Learning A» в разделе n8n), `Tools/n8n/README.md` (строка 133: счётчик сценариев A + строка B), `docs/audits/implementation-progress.md` (запись)

**Interfaces:**
- Consumes: `e2e_auth.hook(name, token, body) → (status, json)`, `e2e_auth.proxy(token, body)`, `owner_and_profile(ctx) → (owner, pid)` (`owner.token`, `owner.app_user_id`), `psql(sql) → text` (из `e2e_suggest`), `ORIGIN`, декоратор `scenario(id, title)`; прокси принимает `recipient` по `ID` (`@g.us` допустим) и пишет `chat_id = recipient + '@c.us'` для WA без `@`.
- Produces: e2e `readiness_in_get_usage`; документация; чек-лист деплоя одного workflow.

- [ ] **Step 1: Сценарий — `e2e-learning.py`**

`send` целиком:

```python
def send(owner, pid, extra=None, recipient="77010001234"):
    body = {"op": "message.send", "channel": "whatsapp", "profile_id": pid, "recipient": recipient, "body": "Здравствуйте! Есть в наличии."}
    body.update(extra or {})
    return e2e_auth.proxy(owner.token, body)
```

Перед `def main():` добавить:

```python
@scenario("readiness_in_get_usage", "B: 10 owner sends with a shown set, card #1 matched, over 3 personal chats on 2 days → Get Usage readiness ready=true for the bot; at 9 it is false; a group send never counts")
def readiness_in_get_usage(ctx):
    owner, pid = owner_and_profile(ctx)
    psql(f"update bot_profiles set bot_key = 'Bot0' where profile_id = '{pid}';")   # a real bot gets it from Create's Register Profile

    def bot0():
        st, d = e2e_auth.hook("GetUsage", owner.token, {"appUserId": owner.app_user_id})
        rows = d.get("readiness") if isinstance(d, dict) else None
        return st, next((r for r in rows or [] if r.get("botKey") == "Bot0"), None)

    chats = ["77010001001", "77010001002", "77010001003"]
    for i in range(9):
        send(owner, pid, dict(ORIGIN, origin_top1=1), recipient=chats[i % 3])
    time.sleep(2)                                           # Record Owner Send runs after the proxy's Respond
    psql(f"update chat_history_events set created_at = created_at - interval '1 day' where profile_id = '{pid}' and chat_id like '77010001001%';")
    st1, nine = bot0()
    send(owner, pid, dict(ORIGIN, origin_top1=1), recipient="120363000000@g.us")   # a group: excluded from the window
    time.sleep(2)
    st2, after_group = bot0()
    send(owner, pid, dict(ORIGIN, origin_top1=1), recipient=chats[1])
    time.sleep(2)
    st3, ten = bot0()
    ok = (st1 == 200 and nine is not None and nine.get("total") == 9 and nine.get("ready") is False
          and st2 == 200 and after_group is not None and after_group.get("total") == 9
          and st3 == 200 and ten is not None and ten.get("total") == 10 and ten.get("matches") == 10
          and ten.get("chats") == 3 and ten.get("days") == 2 and ten.get("ready") is True)
    return ok, f"nine={nine} after_group={after_group} ten={ten}"
```

- [ ] **Step 2: Стенд и прогон**

Run:
```bash
Tools/n8n/staging/pg.sh start; Tools/n8n/staging/pg.sh migrate
python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001
WAPPI_MOCK_LOG=Tools/test-output/wappi-mock.jsonl python3 Tools/n8n/staging/wappi-mock.py &
python3 Tools/n8n/staging/e2e-learning.py --only origin_absent      # warm-up after a re-import (fresh-stand transient 403s)
python3 Tools/n8n/staging/e2e-learning.py
python3 Tools/n8n/staging/e2e-auth.py --only hooks_reject_foreign_ids
python3 Tools/n8n/staging/e2e-auth.py --only billing_id_first_bind
python3 Tools/n8n/tests/run_all.py
```
Expected: `e2e-learning.py` 11 pass, 0 fail (`readiness_in_get_usage`: nine total 9 ready false → the group ignored → ten ready true); обе e2e-auth сцены с Get Usage — PASS (лишнее поле `readiness` ничего не ломает); `run_all.py` — все test_*.py зелёные, включая `test_learning_b`. `--only` в обоих e2e — подстрока одного id, не список.

- [ ] **Step 3: Полный headless EditMode**

Run: `Tools/run-tests-headless.sh`
Expected: `failed = 0`; `total` ≥ 3018 + новые тесты (≈ 3040).

- [ ] **Step 4: Документация**

`CLAUDE.md`, после абзаца «**Learning A — «показано → отправлено»…**» в разделе n8n добавить пункт:

```
  - **Learning B — теневой счёт и мягкий замок «Авто» (2026-10-14, `docs/superpowers/specs/2026-10-14-learning-loop-readiness-design.md`):** Get Usage отдаёт `readiness: [{botKey, matches, total, chats, days, ready}]` — по каждому `bot_key` установки с живой строкой (оба канала + вытесненные строки линии) последние 20 отправок владельца из приложения С ПОКАЗАННЫМ НАБОРОМ (`origin_set > 0`, не группы); `ready = total ≥ 10 ∧ matches ≥ 10 ∧ chats ≥ 3 ∧ days ≥ 2` — пороги ОДИН раз в `apply-learning-b.py` (генератор узлов `Read Usage`/`Shape Response`, маркеры `LEARNING-B-*`; `verify-learning-b.py`; `tests/test_learning_b.py` на реальном SQL; `report-learning.py` печатает ту же таблицу). Legacy/без `installId` → `[]`. Клиент: `UsageSnapshot.readiness` → `AutoReadinessStore` → чистая `AutoReadinessPolicy` — `Locked` ТОЛЬКО если бот в списке и `ready=false` (нет снимка/записи/старый сервер → `Open`: замок — подсказка, не защита); `AutoLockGate.TryEnable` — ЕДИНСТВЕННАЯ дверь к включению «Авто» владельцем (капсула `Bot.OnAutoPillTapped`, `ReplyModeToggleBinder.RequestEnableAuto` = шапка чатов + строка переключателя, пер-чат `SemiAutoToggle.HandlePressed`; `DisableAuto`/`ReconcileFromServer` мимо гейта — пинит `AutoLockWiringTests`); при `Locked` — лист `AutoLockSheet` «Бот ещё учится» («Понятно» / «Включить всё равно» → прежний confirm → `CommitMode`), Back = «Понятно» (`AndroidBackRouter`). Капсула: третье состояние `BotPillKind.Learning` «Учится · N/20» (`AutoReadinessCopy.PillText`, ширина по измеренной подписи как у «Подключить»; приоритет Connect > Learning > Auto; перерисовка на `UsageStore.OnUsageChanged`). Включённое «Авто» замок не трогает; «Включить всё равно» ничего не записывает (сигнал для C). Деплой — один workflow Get Usage через `deploy-canonical.py --only jtbssfzXbOxwTK4k`; миграций нет.
```

`Tools/n8n/README.md`, строка 133: `staging/e2e-learning.py` (8 сценариев) → `(10 сценариев A + 1 B)`; следующей строкой:

```
- **Learning B (2026-10-14, `docs/superpowers/specs/2026-10-14-learning-loop-readiness-design.md`):** `apply-learning-b.py` (Get Usage: CTE `readiness` + поле ответа; пороги 20/10/10/3/2 в одном месте), `verify-learning-b.py`, `tests/test_learning_b.py`, таблица готовности в `report-learning.py`, e2e `readiness_in_get_usage`. Клиент: `AutoReadinessPolicy`/`AutoLockGate`/`AutoLockSheet`, капсула «Учится · N/20».
```

`docs/audits/implementation-progress.md` — в конец (числа — фактические):

```
## Learning B «Теневой счёт и замок «Авто»» — реализовано, ждёт go на деплой (2026-10-14; коммиты <первый>…HEAD)
Get Usage отдаёт readiness по ботам (окно 20, ≥10 совпадений, ≥3 чата, ≥2 дня); клиент: AutoReadinessPolicy/Store, AutoLockGate на
четырёх точках, лист «Бот ещё учится», капсула «Учится · N/20». Стенд: e2e-learning 11/11, e2e-auth (Get Usage) без регрессий;
Unity EditMode <total>/<total>; n8n test_learning_b 8/8, run_all OK. Деплой: один workflow (Get Usage) по «go»; клиент — в сборке вместе с A.
```

- [ ] **Step 5: Commit**

```bash
git add Tools/n8n/staging/e2e-learning.py CLAUDE.md Tools/n8n/README.md docs/audits/implementation-progress.md
git commit -m "docs+e2e(learning-b): сценарий readiness_in_get_usage, CLAUDE.md/README, журнал — готово к деплою, ждёт go

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 6: Чек-лист деплоя (НЕ выполнять без явного «go» владельца)**

1. Репетиция на стенде с прод-состояния: `python3 Tools/n8n/staging/bootstrap.py --canon-ref <sha прода> --bot wa:stgwa001 --bot tg:stgtg001` → `python3 Tools/n8n/deploy-canonical.py --base http://localhost:5678` (dry-run: ровно `jtbssfzXbOxwTK4k`) → `--apply --yes` → `python3 Tools/n8n/verify-deployed.py --base http://localhost:5678` (diff 0) → `python3 Tools/n8n/staging/derive-staging.py` без импорта → `e2e-learning.py --only readiness`.
2. Прод: `python3 Tools/n8n/verify-deployed.py --json docs/audits/<date>-prod-before.json` → `python3 Tools/n8n/deploy-canonical.py --only jtbssfzXbOxwTK4k` (dry-run, прочитать diff, записать rollback pointer = `activeVersionId`) → `--apply --yes` → `verify-deployed.py --json docs/audits/<date>-prod-after.json` (diff 0) → GET-проба `https://n8n.choosereply.com/webhook/GetUsage` отвечает «Did you mean to make a POST request?». Миграций и `update-clones.py` нет.
3. Клиент — та же сборка, что и A; калибровка порога по `report-learning.py --prod` после первых недель.
