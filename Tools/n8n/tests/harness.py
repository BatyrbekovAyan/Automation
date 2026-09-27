#!/usr/bin/env python3
"""Shared harness for BEHAVIOURAL regression tests of generated n8n workflows (iteration 2026-09-27).

Why: structural verifiers (verify-*.py) and the older Code-node suite passed while the defects R1–R16 of the
2026-09-25 review were present. These tests execute the REAL node bodies taken from the canonical workflow JSONs
(which the generators write) against synthetic inputs, and run the REAL SQL against session-temporary tables in the
local staging Postgres (inside BEGIN … ROLLBACK). No n8n server, no network, no production.

    from harness import wf, node, code, sql_run, bind, module
    w = wf('crAuthSession001-Auth_Session.json')
    out = code(w, 'Issue Token', {'json': {...}}, named={'Route': {...}})
    rows = sql_run(setup_sql + bind(node(w, 'Verify Install')['parameters']['query'], [...]))

Each test file is a plain script: `python3 Tools/n8n/tests/test_<area>.py` → exit 0 when every case passes,
non-zero with a readable list of failures otherwise. `python3 Tools/n8n/tests/run_all.py` runs every test_*.py.
"""
import importlib.util
import json
import os
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
N8N = ROOT / 'Tools' / 'n8n'
WF = N8N / 'workflows'
PG = N8N / 'staging' / 'pg.sh'


def wf(name):
    """A canonical workflow JSON by file name (e.g. '4wYitz5ek30SVNlT-WhatsApp_Bot.json')."""
    return json.loads((WF / name).read_text(encoding='utf-8'))


def node(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def has_node(w, name):
    return any(n['name'] == name for n in w['nodes'])


_DRIVER = r'''const fs=require('fs'); const v=JSON.parse(fs.readFileSync(0,'utf8'));
const env = Object.assign({CR_AUTH_SECRET:'synthetic-test-secret', CR_AUTH_COMPAT_UNTIL:'2000-01-01'}, v.env||{});
const named = v.named||{};
const dollar = name => { const x = named[name];
  const items = Array.isArray(x) ? x.map(j => ({json:j})) : [{json: x === undefined ? {} : x}];
  if (x === undefined && v.strict) throw new Error('node not executed: '+name);
  return {first:()=>items[0], all:()=>items, last:()=>items[items.length-1], item: items[0], isExecuted: x !== undefined}; };
const items = Array.isArray(v.items) ? v.items : [v.item];
const fn=new Function('$input','$','$env','require','Buffer','$json','$execution','$binary', v.js);
let result = fn({first:()=>items[0], all:()=>items, last:()=>items[items.length-1], item: items[0]}, dollar, env, require, Buffer,
  (items[0]||{}).json, {id: v.executionId || 'synthetic-exec'}, (items[0]||{}).binary);
Promise.resolve(result).then(r => process.stdout.write(JSON.stringify(r)));'''


def run_js(js, item=None, named=None, env=None, items=None, strict=False, execution_id='synthetic-exec'):
    """Execute a Code-node body. `item` = {'json':…, 'binary':…}; `named` = {'Node Name': json or [json, …]}."""
    payload = {'js': js, 'item': item or {'json': {}}, 'items': items, 'named': named or {}, 'env': env or {},
               'strict': strict, 'executionId': execution_id}
    r = subprocess.run(['node', '-e', _DRIVER], input=json.dumps(payload), text=True, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('node failed: ' + r.stderr.strip()[-2000:])
    return json.loads(r.stdout)


def code(w, name, item=None, named=None, env=None, items=None, strict=False):
    return run_js(node(w, name)['parameters']['jsCode'], item, named, env, items, strict)


def module(filename):
    """Import a generator/tool module from Tools/n8n by file name (hyphens allowed)."""
    spec = importlib.util.spec_from_file_location(re.sub(r'\W', '_', filename), N8N / filename)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def sql_literal(v):
    if v is None:
        return 'NULL'
    if isinstance(v, bool):
        return 'true' if v else 'false'
    if isinstance(v, (int, float)):
        return str(v)
    if isinstance(v, (list, dict)):
        v = json.dumps(v, ensure_ascii=False)
    return "'" + str(v).replace("'", "''") + "'"


def bind(query, values):
    """Substitute $1..$N like n8n's queryReplacement does (values already evaluated)."""
    return re.sub(r'\$(\d+)(?!\d)', lambda m: sql_literal(values[int(m[1]) - 1]), query)


def sql_run(body, setup=''):
    """Run SQL in ONE session inside BEGIN … ROLLBACK with temp tables first on the search_path.
    Returns a list of result sets; each is a list of dict rows (via json_agg-free psql CSV parsing is avoided:
    every statement whose rows you need must be a SELECT … and is captured with \\gset-free JSON output)."""
    script = ("\\set ON_ERROR_STOP on\nbegin;\nset local search_path = pg_temp, public;\n" + setup + "\n" + body +
              "\nrollback;\n")
    r = subprocess.run([str(PG), 'psql', '-X', '-q', '-A', '-t', '-P', 'pager=off'], input=script, text=True,
                       capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('psql failed: ' + (r.stderr or r.stdout).strip()[-2000:])
    return r.stdout


def sql_json(body, setup=''):
    """Like sql_run, but `body` must end with ONE select whose single column is a json value; returns it parsed.
    Earlier statements' output (e.g. RETURNING rows) is ignored: only the LAST non-empty line is parsed."""
    out = [l for l in sql_run(body, setup).splitlines() if l.strip()]
    return json.loads(out[-1]) if out else None


class Case:
    """Tiny collector: `with Case('R3 binary survives gate') as c: c.check(cond, detail)`."""
    results = []

    def __init__(self, title):
        self.title = title
        self.fails = []

    def __enter__(self):
        return self

    def check(self, cond, detail=''):
        if not cond:
            self.fails.append(detail or 'assertion failed')
        return cond

    def __exit__(self, et, ev, tb):
        if et is not None:
            self.fails.append(f'EXCEPTION {et.__name__}: {ev}')
        Case.results.append((self.title, list(self.fails)))
        return True


def report(exit_on_fail=True):
    bad = [(t, f) for t, f in Case.results if f]
    for t, f in Case.results:
        print(('FAIL ' if f else 'PASS ') + t)
        for x in f:
            print('      - ' + str(x)[:600])
    print(f'\n{len(Case.results) - len(bad)} pass, {len(bad)} fail')
    if exit_on_fail and bad:
        raise SystemExit(1)
