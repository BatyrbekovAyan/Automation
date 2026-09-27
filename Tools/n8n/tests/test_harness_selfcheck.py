#!/usr/bin/env python3
"""The harness itself: a Code body runs with $/$input, SQL runs in a rolled-back session."""
from harness import Case, report, run_js, sql_json

with Case('harness: Code body sees $input and named nodes') as c:
    out = run_js("return [{json: {a: $input.first().json.x + $('N').first().json.y}}];", {'json': {'x': 1}}, {'N': {'y': 2}})
    c.check(out[0]['json']['a'] == 3, out)
with Case('harness: SQL temp table rolled back') as c:
    v = sql_json("create temp table t(x int); insert into t values (7); select json_build_object('x', (select x from t));")
    c.check(v == {'x': 7}, v)
report()
