#!/usr/bin/env python3
"""Learning loop, подпроект B «Теневой счёт и замок «Авто»» (2026-10-14) — идемпотентный патч Get Usage:

  Read Usage      — CTE-блок readiness (маркеры -- LEARNING-B-BEGIN / -- LEARNING-B-END) + колонка readiness (jsonb)
                    первой в итоговом select (маркер -- LEARNING-B-COL); параметры: второй = installId, третий = mode
                    из Auth Gate (null в legacy → '').
  Shape Response  — поле readiness (строка с маркером // LEARNING-B) — массив как есть, [] если не массив.

Learning progress percent (2026-10-23, spec docs/superpowers/specs/2026-10-22-learning-progress-percent-design.md §2–§3): the row also
carries the BEST window of the history (bestMatches), the percent 0-100 (progress), the unlock mark (unlockAt), the anti-gaming flag
(capped) and what is still missing (needChats / needDays). `ready` now means «best window and the history conditions». matches / total /
chats / days keep describing the CURRENT window of the last 20 sends — the report reads them to calibrate.

The numbers live ONCE here; the client holds none (it draws the percent the server sends). report-learning.py and verify-learning-b.py
import them. tests/test_learning_b.py runs the whole Read Usage query as real SQL.

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


def half_up_percent(matches: int, target: int) -> int:
    """round(100 * matches / target) with halves rounded UP, capped at 100 — the percent the owner sees once the conditions are met
    (progress_sql's numeric round(), which rounds half away from zero: up, for these non-negative values). Integer arithmetic only:
    Python's round() rounds halves to even and would split from Postgres — 2 of 16 = 12.5 is 13 % on the screen, round() says 12."""
    return min(100, (200 * matches + target) // (2 * target))


def min_matches(target: int, unlock: int, window: int) -> int:
    """The fewest matches m in 0..window whose half_up_percent reaches `unlock` — so `best >= m` is exactly «the shown percent reaches
    the unlock». ValueError when no m does: such a calibration could never open «Авто», and it fails at import, never on a phone."""
    for m in range(window + 1):
        if half_up_percent(m, target) >= unlock:
            return m
    raise ValueError(f"learning B: no m in 0..{window} reaches {unlock} % at target {target} — «Авто» could never open")


def unlock_mismatches(min_count: int, target: int, unlock: int, window: int) -> list:
    """Every best in 0..window where `best >= min_count` (the ready rule) and «the percent reaches the unlock» disagree; [] = consistent."""
    return [b for b in range(window + 1) if (b >= min_count) != (half_up_percent(b, target) >= unlock)]


WINDOW = 20           # «best» is measured over any N consecutive owner sends with a shown set, per bot
TARGET_MATCHES = 16   # card #1 matched the sent text (origin_top1 = 1) this many times within WINDOW = 100 % («8 out of 10»)
UNLOCK_PERCENT = 70   # «Авто» opens from this percent
MIN_MATCHES = min_matches(TARGET_MATCHES, UNLOCK_PERCENT, WINDOW)   # derived, never typed: the fewest matches that reach UNLOCK_PERCENT (12)
MIN_CHATS = 3         # the HISTORY spans this many personal chats …
MIN_DAYS = 2          # … and this many calendar days (Asia/Almaty); otherwise progress stops at UNLOCK_PERCENT - 1

# ready ⇔ progress >= unlockAt for every best 0..WINDOW (spec §3). Checked at import: --check and verify-learning-b.py import this
# module, so a recalibration (or a typed MIN_MATCHES) that breaks it fails offline, before anything is deployed.
_MISMATCHES = unlock_mismatches(MIN_MATCHES, TARGET_MATCHES, UNLOCK_PERCENT, WINDOW)
if _MISMATCHES:
    raise ValueError(f"learning B: MIN_MATCHES = {MIN_MATCHES} disagrees with the shown percent at best = {_MISMATCHES} "
                     f"(target {TARGET_MATCHES}, unlock {UNLOCK_PERCENT} %)")


def send_filter(e):
    """The predicate of «an owner send that counts» (e = alias of chat_history_events) — ONE text for Read Usage and the report."""
    return (f"{e}.source = 'owner_app' and coalesce({e}.origin_set, 0) > 0\n"
            f"    and {e}.chat_id not like '%@g.us' and {e}.chat_id not like '-%'")


def window_columns(partition, e):
    """rn — the send's rank, newest first (rn <= WINDOW is the CURRENT window the report reads); roll — the matches among the WINDOW
    sends ending at this one, oldest first (its max over a bot is the BEST window of the history; the first sends form shorter windows)."""
    return (f"row_number() over (partition by {partition} order by {e}.created_at desc, {e}.id desc) as rn,\n"
            f"         sum(coalesce({e}.origin_top1, 0)) over (partition by {partition} order by {e}.created_at, {e}.id\n"
            f"             rows between {WINDOW - 1} preceding and current row) as roll")


def progress_sql(best, cond):
    """The percent 0-100 of the best window against the target, round half up; without the history conditions it stops one short of the unlock."""
    raw = f"round(100.0 * {best} / {TARGET_MATCHES})"
    return f"(case when {cond} then least(100, {raw}) else least({UNLOCK_PERCENT - 1}, {raw}) end)::int"


def readiness_columns(s):
    """Every output column over the sends of one bot (s = their alias, carrying window_columns' rn / roll) — ONE arithmetic for both renderers.
    matches / total / chats / days: the current window; the rest: the whole history (the conditions) and its best window."""
    win = f"{s}.rn <= {WINDOW}"
    best = f"coalesce(max({s}.roll), 0)"
    chats = f"count(distinct {s}.chat_id)"
    days = f"count(distinct ({s}.created_at at time zone 'Asia/Almaty')::date)"
    cond = f"({chats} >= {MIN_CHATS} and {days} >= {MIN_DAYS})"
    return (f"(count({s}.bot_key) filter (where {win} and {s}.origin_top1 = 1))::int as matches,\n"
            f"         (count({s}.bot_key) filter (where {win}))::int as total,\n"
            f"         ({chats} filter (where {win}))::int as chats,\n"
            f"         ({days} filter (where {win}))::int as days,\n"
            f"         {best}::int as best_matches,\n"
            f"         {progress_sql(best, cond)} as progress,\n"
            f"         ({cond} and {best} >= {MIN_MATCHES}) as ready,\n"
            f"         (not {cond} and round(100.0 * {best} / {TARGET_MATCHES}) >= {UNLOCK_PERCENT}) as capped,\n"
            f"         greatest(0, {MIN_CHATS} - {chats})::int as need_chats,\n"
            f"         greatest(0, {MIN_DAYS} - {days})::int as need_days")


# The comments name the parameters in WORDS (verify-sql-comments.py: no dollar-digit inside a comment).
READINESS_SQL = f"""-- LEARNING-B-BEGIN (generated by Tools/n8n/apply-learning-b.py — edit there)
-- Readiness of «Авто» per bot of the calling install (second parameter = install id, third = gate mode). History = every owner_app
-- send WITH a shown set in personal chats, both channels and the superseded rows of the bot's line. best = the most card-1 matches in
-- any {WINDOW} consecutive sends of the history (rolling sum); progress = round(100 * best / {TARGET_MATCHES}), held at {UNLOCK_PERCENT - 1} until
-- the history holds {MIN_CHATS} chats and {MIN_DAYS} days. matches / total / chats / days describe the CURRENT window (the last {WINDOW} sends).
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
         {window_columns('lp.bot_key', 'e')}
  from chat_history_events e
  join line_profiles lp on lp.profile_id = e.profile_id
  where {send_filter('e')}
), readiness as (
  select lb.bot_key,
         {readiness_columns('s')}
  from line_bots lb
  left join sends s on s.bot_key = lb.bot_key
  group by lb.bot_key
),
-- LEARNING-B-END
"""
READINESS_COLUMN = ("  (select coalesce(jsonb_agg(jsonb_build_object('botKey', r.bot_key, 'matches', r.matches, 'total', r.total, "
                    "'chats', r.chats, 'days', r.days, 'ready', r.ready, 'bestMatches', r.best_matches, 'progress', r.progress, "
                    f"'unlockAt', {UNLOCK_PERCENT}, 'capped', r.capped, 'needChats', r.need_chats, 'needDays', r.need_days) "
                    "order by r.bot_key), '[]'::jsonb) from readiness r) as readiness, -- LEARNING-B-COL\n")
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
