#!/usr/bin/env python3
"""Read-only learning-A report: how owners answer relative to the «Вместе» panel. NO message text is
printed — only counts, shares and token totals per profile (profile ids are the bot's own Wappi ids).

    python3 Tools/n8n/report-learning.py                 # staging (Tools/n8n/staging/pg.sh)
    python3 Tools/n8n/report-learning.py --days 14
    python3 Tools/n8n/report-learning.py --prod          # prod through .secrets/sql-oneoff.py — OWNER GO ONLY

Columns: sends (owner_app rows with an origin), card/edited/typed/none shares, top1 (share of sends with a
shown set whose text matched the fresh card #1), edit (mean origin_edit over edited), sets (suggestion_sets
rows), learning_sets_3moves (share of learning-stage sets with ≥3 distinct moves), tokens (sum of usage).
Known limits: groups (@g.us) are EXCLUDED — the panel has no gate in groups, group sends are not learning
data; `sets` is best-effort (Record Set runs after Respond but inside the 20-s execution timeout, so a set at
the deadline may be missing) — compare `sets` with the panel's successful answers when calibrating B.

Learning B (2026-10-14): a second table, `readiness`, prints what the «Авто» lock sees per (install, bot_key) —
the same window and thresholds Get Usage decides with, imported from apply-learning-b.py (never retyped).
Progress percent (2026-10-23): it also prints the best window of the history, the percent the app shows, the anti-gaming
flag and what is still missing — read it 2-3 weeks after a release to calibrate TARGET_MATCHES / UNLOCK_PERCENT.
"""
import argparse
import importlib.util
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

_spec_b = importlib.util.spec_from_file_location("apply_learning_b", os.path.join(HERE, "apply-learning-b.py"))
LB = importlib.util.module_from_spec(_spec_b); _spec_b.loader.exec_module(LB)

SQL = """
with s as (
  select profile_id,
         count(*) filter (where origin_kind is not null) as sends,
         count(*) filter (where origin_kind = 'card')   as card,
         count(*) filter (where origin_kind = 'edited') as edited,
         count(*) filter (where origin_kind = 'typed')  as typed,
         count(*) filter (where origin_kind = 'none')   as none,
         count(*) filter (where origin_top1 = 1)        as top1_hits,
         count(*) filter (where origin_kind in ('card','edited','typed')) as with_set,
         round(avg(origin_edit) filter (where origin_kind = 'edited'))    as edit_mean
  from chat_history_events
  where source = 'owner_app' and chat_id not like '%%@g.us' and created_at >= now() - interval '%(days)s days'
  group by profile_id
), t as (
  select profile_id,
         count(*) as sets,
         count(*) filter (where stage = 'learning') as learning_sets,
         count(*) filter (where stage = 'learning' and (select count(distinct x->>'move') from jsonb_array_elements(items) x) >= 3) as learning_sets_3moves,
         coalesce(sum((usage->>'prompt_tokens')::int + (usage->>'completion_tokens')::int), 0) as tokens
  from suggestion_sets
  where created_at >= now() - interval '%(days)s days'
  group by profile_id
)
select coalesce(s.profile_id, t.profile_id) as profile_id, coalesce(s.sends,0) sends, coalesce(s.card,0) card, coalesce(s.edited,0) edited,
       coalesce(s.typed,0) typed, coalesce(s.none,0) none, coalesce(s.top1_hits,0) top1_hits, coalesce(s.with_set,0) with_set, s.edit_mean,
       coalesce(t.sets,0) sets, coalesce(t.learning_sets,0) learning_sets, coalesce(t.learning_sets_3moves,0) learning_sets_3moves, coalesce(t.tokens,0) tokens
from s full outer join t on t.profile_id = s.profile_id
order by 1;
"""


COLS = ["profile_id", "sends", "card", "edited", "typed", "none", "top1_hits", "with_set", "edit_mean", "sets", "learning_sets", "learning_sets_3moves", "tokens"]

# learning B: the lock's own arithmetic per (install, bot_key), across every install — rendered from the SAME fragments
# the Get Usage CTE is generated from (apply-learning-b.send_filter / window_columns / readiness_columns), never retyped; like
# the CTE, only bots with a LIVE row are listed (review 2026-10-14 #7). Reads the WHOLE history (best window, progress and the
# chats/days conditions are history-wide); matches / total / chats / days are the current window of the last WINDOW sends.
# Not windowed by --days: the lock is not either.
# This query does NOT go through the %-substitution above — single % in LIKE.
READINESS_SQL = f"""
with sends as (
  select bp.owner_id, bp.bot_key, e.chat_id, e.origin_top1, e.created_at,
         {LB.window_columns('bp.owner_id, bp.bot_key', 'e')}
  from chat_history_events e
  join bot_profiles bp on bp.profile_id = e.profile_id
  where bp.bot_key is not null and bp.owner_id is not null
    and exists (select 1 from bot_profiles live where live.owner_id = bp.owner_id and live.bot_key = bp.bot_key and live.deleted_at is null)
    and {LB.send_filter('e')}
)
select s.owner_id, s.bot_key,
         {LB.readiness_columns('s')}
from sends s
group by s.owner_id, s.bot_key
"""
READINESS_COLS = ["owner_id", "bot_key", "matches", "total", "chats", "days", "ready", "best_matches", "progress", "capped", "need_chats", "need_days"]


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


def selftest():
    sample = '{"rows":[{"profile_id":"p","sends":3,"card":1,"edited":1,"typed":1,"none":0,"top1_hits":2,"with_set":3,"edit_mean":40,"sets":3,"learning_sets":3,"learning_sets_3moves":2,"tokens":900}]}'
    a = parse_rows(sample)
    b = parse_rows("[" + '{"payload":' + sample + "}" + "]")
    c = parse_rows('{"payload":' + sample + "}")
    assert a == b == c and a[0][0] == "p" and a[0][-1] == "900", (a, b, c)
    rb = parse_rows('{"rows":[{"owner_id":"i","bot_key":"Bot0","matches":12,"total":12,"chats":3,"days":1,"ready":false,'
                    '"best_matches":12,"progress":69,"capped":true,"need_chats":0,"need_days":1}]}', READINESS_COLS)
    assert rb == [["i", "Bot0", "12", "12", "3", "1", "False", "12", "69", "True", "0", "1"]], rb
    print("selftest OK")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=14)
    ap.add_argument("--prod", action="store_true", help="prod through the self-deleting SQL harness (owner go only)")
    ap.add_argument("--selftest", action="store_true", help="check the JSON parser against the three shapes the harness/psql can produce")
    a = ap.parse_args()
    if a.selftest:
        selftest()
        return
    sql = SQL % {"days": a.days}
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
    print(f"\nreadiness: window {LB.WINDOW}, target {LB.TARGET_MATCHES}, unlock {LB.UNLOCK_PERCENT}% (= {LB.MIN_MATCHES} matches), "
          f"chats ≥ {LB.MIN_CHATS}, days ≥ {LB.MIN_DAYS} (history)")
    print(f"(match/total, chats, days: the last {LB.WINDOW} sends; best, prog%, ready, capped, need c/d: the whole history)")
    print(f"{'install':16} {'bot':6} {'match/total':>11} {'chats':>5} {'days':>4} {'best':>4} {'prog%':>5} {'ready':>5} {'capped':>6} {'need c/d':>8}")
    for owner_id, bot_key, matches, total, chats, days, ready, best, progress, capped, need_chats, need_days in rows_b:
        print(f"{owner_id[:16]:16} {bot_key[:6]:6} {matches + '/' + total:>11} {chats:>5} {days:>4} {best:>4} {progress:>5} "
              f"{'yes' if ready.lower() == 'true' else 'no':>5} {'yes' if capped.lower() == 'true' else 'no':>6} {need_chats + '/' + need_days:>8}")
    if not rows_b:
        print("(no bot has a send with a shown set yet)")


if __name__ == "__main__":
    main()
