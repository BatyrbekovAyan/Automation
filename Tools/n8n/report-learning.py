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
"""
import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

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


def json_sql(sql):
    """ONE row, ONE column: {"rows":[...]} — the same contract for psql (prints json as text) and the prod harness
    (an n8n webhook whose Response Data may be firstEntryJson → {"payload":…} or allEntries → [{"payload":…}])."""
    return "select json_build_object('rows', coalesce(json_agg(row_to_json(r) order by r.profile_id), '[]'::json)) as payload from (" + sql.rstrip().rstrip(";") + ") r"


def parse_rows(text):
    obj = json.loads(text)
    if isinstance(obj, list):
        obj = obj[0] if obj else {}
    payload = obj.get("payload", obj) if isinstance(obj, dict) else obj
    if isinstance(payload, str):
        payload = json.loads(payload)
    rows = (payload or {}).get("rows") or []
    return [[("" if r.get(c) is None else str(r.get(c))) for c in COLS] for r in rows]


def run_staging(sql):
    out = subprocess.run([os.path.join(HERE, "staging", "pg.sh"), "psql", "-tAc", json_sql(sql)], capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(out.stderr.strip())
    return parse_rows(out.stdout.strip())


def run_prod(sql):
    out = subprocess.run([sys.executable, os.path.join(HERE, ".secrets", "sql-oneoff.py"), json_sql(sql)], capture_output=True, text=True)
    lines = [l for l in out.stdout.strip().splitlines() if l.strip()]
    if out.returncode != 0 or not lines:
        sys.exit((out.stderr.strip() or out.stdout.strip() or "harness produced no output")[:300])
    status, _, body = lines[-1].partition(" ")      # the harness prints ONE line: `200 <body>`
    if status != "200":
        sys.exit(f"harness -> {lines[-1][:300]}")
    try:
        return parse_rows(body)
    except json.JSONDecodeError:
        sys.exit("harness output is not JSON — most likely truncated by sql-oneoff.py's b[:1500]; lift that limit first (deploy checklist 1a)")


def selftest():
    sample = '{"rows":[{"profile_id":"p","sends":3,"card":1,"edited":1,"typed":1,"none":0,"top1_hits":2,"with_set":3,"edit_mean":40,"sets":3,"learning_sets":3,"learning_sets_3moves":2,"tokens":900}]}'
    a = parse_rows(sample)
    b = parse_rows("[" + '{"payload":' + sample + "}" + "]")
    c = parse_rows('{"payload":' + sample + "}")
    assert a == b == c and a[0][0] == "p" and a[0][-1] == "900", (a, b, c)
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
    rows = run_prod(sql) if a.prod else run_staging(sql)
    print(f"{'profile':18} {'sends':>5} {'card':>5} {'edit':>5} {'typed':>5} {'none':>5} {'top1':>6} {'edit%':>5} {'sets':>5} {'lrn3/lrn':>9} {'tokens':>8}")
    for r in rows:
        pid, sends, card, edited, typed, none, top1, with_set, edit_mean, sets, lrn, lrn3, tokens = r
        top1_share = f"{int(top1) / int(with_set):.0%}" if int(with_set or 0) else "—"
        print(f"{pid[:18]:18} {sends:>5} {card:>5} {edited:>5} {typed:>5} {none:>5} {top1_share:>6} {edit_mean or '—':>5} {sets:>5} {lrn3 + '/' + lrn:>9} {tokens:>8}")
    if not rows:
        print("(no rows in the window)")


if __name__ == "__main__":
    main()
