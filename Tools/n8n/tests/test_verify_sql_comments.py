#!/usr/bin/env python3
"""V1 (review 2026-09-28): the generic SQL guard (Tools/n8n/verify-sql-comments.py) — its rules on fixtures, its
tokenizer against the cases a line regex gets wrong, its CLI, and the canonical corpus.

Every BAD fixture must raise exactly the rule it is about; every CLEAN fixture — chosen so that a sloppy tokenizer
WOULD flag it (a `--` inside a literal, an E'\\'' escape, a dollar-quoted body holding `--`) — must raise nothing.
The corpus run is not vacuous: it must read every Postgres node of the canonical set (72 at the time of writing)
and find nothing; the CLI must exit non-zero on a workflow carrying a placeholder in a comment and name the node.

    python3 Tools/n8n/tests/test_verify_sql_comments.py
"""
import json
import os
import subprocess
import sys
import tempfile

from harness import N8N, Case, module, report

guard = module('verify-sql-comments.py')
CLI = str(N8N / 'verify-sql-comments.py')

BAD = [   # (title, query, the rule it must raise)
    ('placeholder in a line comment', "-- $1 = fileId\nselect $1::text", 'placeholder-in-comment'),
    ('placeholder in a line comment ended by CR', "-- the id ($3)\rselect $3::text", 'placeholder-in-comment'),
    ('placeholder in backticks inside a comment', "-- `topup_balance + $5` is not idempotent\nselect $5::int", 'placeholder-in-comment'),
    ('placeholder in a range inside a comment', "-- $3..$7 = event payload\nselect $3, $7", 'placeholder-in-comment'),
    ('placeholder in a block comment', "/* id $2 */ select $1, $2", 'placeholder-in-comment'),
    ('placeholder in a NESTED block comment (Postgres nests)', "/* a /* b */ still a comment $1 */ select $1", 'placeholder-in-comment'),
    ('$0 in a comment (stricter than pg-promise on purpose)', "-- $0 is unused\nselect 1", 'placeholder-in-comment'),
    ('placeholder in a string literal', "select 'id: $1' as v", 'placeholder-in-literal'),
    ('placeholder in an E-string', "select E'it\\'s $1' as v", 'placeholder-in-literal'),
    ('placeholder in a $$ body', "select $$body $1$$ as v", 'placeholder-in-literal'),
    ('placeholder in a $tag$ body', "select $fn$ body $1 $fn$ as v", 'placeholder-in-literal'),
    ('placeholder in a quoted identifier', 'select 1 as "col$1"', 'placeholder-in-literal'),
    ('{{ in code', "select {{ $json.id }}::text", 'expression-braces'),
    ('{{ inside a comment', "-- see {{ $json.x }}\nselect 1", 'expression-braces'),
    ('leading = (the whole query is an expression)', "=select 1", 'leading-equals'),
    ('raw modifier', "select $1:raw", 'placeholder-modifier'),
    ('caret modifier (= raw)', "select $1^", 'placeholder-modifier'),
    ('hash modifier (= raw), the mistaken «#1» fix', "select $1# from t", 'placeholder-modifier'),
    ('tilde modifier (= SQL name)', "select $1~ from t", 'placeholder-modifier'),
    ('name modifier', "select * from $2:name", 'placeholder-modifier'),
    ('alias modifier', "select 1 as $1:alias", 'placeholder-modifier'),
    ('json modifier', "select $1:json", 'placeholder-modifier'),
    ('csv modifier', "select ($1:csv)", 'placeholder-modifier'),
    ('list modifier', "select ($1:list)", 'placeholder-modifier'),
    ('value modifier', "select '$1:value'", 'placeholder-in-literal'),
    ('value modifier in code', "select x from t where y = $12:value", 'placeholder-modifier'),
    ('unterminated block comment', "select 1 /* never closed", 'unterminated'),
    ('unterminated string', "select 'never closed", 'unterminated'),
    ('unterminated dollar quote', "select $q$ never closed", 'unterminated'),
]

CLEAN = [   # each one trips a line-regex or a naive tokenizer
    ('parameters in code with casts', "select $1::text, $2::jsonb, $3::timestamptz, $1::json"),
    ('a comment naming the parameter in words', "-- first parameter = fileId, second = gate mode\nselect $1, $2"),
    ('-- inside a string literal is not a comment', "select '--not a comment', $1"),
    ('doubled quote inside a literal', "select 'it''s', $1"),
    ('E-string with an escaped quote, then a placeholder in code', "select E'\\'' || $1"),
    ('-- inside a dollar-quoted body is not a comment', "select $tag$ -- not a comment $tag$, $1"),
    ('dollar sign without a digit in literals and comments', "-- price in $ only\nselect 'USD $ 5', '$', $1"),
    ('$ inside an identifier is not a dollar quote', "select a$b$, $1 from t"),
    ('/* */ without placeholders', "/* a /* nested */ note */ select $1"),
    ('double-colon cast is not the :json modifier', "select $1::json, $2::name"),
    ('trailing comment after code', "select $1 -- the id\n, $2"),
    ('Cyrillic prose like the real comments', "-- Гард на profile id (первый параметр) ниже: сентинел «-1»\nselect $1 where $1 <> '-1'"),
    ('minus before a placeholder (pg-promise wraps a negative number in parentheses)', "select 10-$1"),
    ('a line comment running to the end of the query', "select $1\n-- done"),
]

for title, q, rule in BAD:
    with Case(f'guard flags: {title}') as c:
        got = [p[0] for p in guard.check_query(q)]
        c.check(rule in got, f'{q!r}: expected {rule}, got {got}')
        c.check(set(got) <= {rule, 'unterminated'}, f'{q!r}: unexpected extra rules {got}')

for title, q in CLEAN:
    with Case(f'guard passes: {title}') as c:
        got = guard.check_query(q)
        c.check(got == [], f'{q!r}: {got}')

with Case('guard: a non-string executeQuery query is a finding, not a skip') as c:
    found, count = guard.check_workflow({'nodes': [{'name': 'Q', 'type': guard.POSTGRES_TYPE,
                                                    'parameters': {'operation': 'executeQuery'}}]})
    c.check(count == 1 and [f[1] for f in found] == ['not-a-string'], (count, found))

with Case('guard: memory nodes without a query are not counted; other Postgres operations are not executeQuery') as c:
    found, count = guard.check_workflow({'nodes': [
        {'name': 'Chat Memory', 'type': '@n8n/n8n-nodes-langchain.memoryPostgresChat', 'parameters': {'tableName': 'x'}},
        {'name': 'Insert', 'type': guard.POSTGRES_TYPE, 'parameters': {'operation': 'insert', 'table': 'x'}},
        {'name': 'Sel', 'type': guard.POSTGRES_TYPE, 'parameters': {'operation': 'executeQuery', 'query': 'select $1'}}]})
    c.check(count == 1 and found == [], (count, found))

with Case('guard: the line numbers point at the offending line') as c:
    got = guard.check_query("select 1,\n  $1\n-- the third line names $2\n, $2")
    c.check([(p[0], p[1], p[2]) for p in got] == [('placeholder-in-comment', 3, '$2')], got)

with Case('corpus: every Postgres node of the canonical and staging-only workflows is clean (72+ nodes read)') as c:
    findings, nodes, files = guard.scan(list(guard.DEFAULT_PATHS))
    c.check(findings == [], '\n'.join(' :: '.join(map(str, f)) for f in findings[:20]))
    c.check(nodes >= 72, f'only {nodes} Postgres nodes read from {files} files')

with Case('CLI: default run exits 0 and reports the node count') as c:
    r = subprocess.run([sys.executable, CLI], capture_output=True, text=True)
    c.check(r.returncode == 0, r.stdout[-1500:] + r.stderr[-500:])
    c.check('Postgres node(s)' in r.stdout, r.stdout[-500:])


def run_on(nodes):
    with tempfile.TemporaryDirectory() as d:
        with open(os.path.join(d, 'wfTest-Probe.json'), 'w', encoding='utf-8') as fh:
            json.dump({'id': 'wfTest', 'name': 'Probe', 'nodes': nodes, 'connections': {}}, fh)
        return subprocess.run([sys.executable, CLI, d], capture_output=True, text=True)


with Case('CLI: a workflow with a placeholder in a comment exits 1 and names file, node, line and token') as c:
    r = run_on([{'name': 'Delete Things', 'type': guard.POSTGRES_TYPE,
                 'parameters': {'operation': 'executeQuery', 'query': "-- id\n-- $1 fileId\ndelete from t where id = $1"}}])
    c.check(r.returncode == 1, f'exit {r.returncode}: {r.stdout}')
    c.check('wfTest-Probe.json :: Delete Things :: line 2 :: placeholder-in-comment $1' in r.stdout, r.stdout)

with Case('CLI: a clean workflow exits 0; one with no Postgres node at all exits 1 (nothing was checked)') as c:
    ok = run_on([{'name': 'Fine', 'type': guard.POSTGRES_TYPE,
                  'parameters': {'operation': 'executeQuery', 'query': "-- fileId (first parameter)\nselect $1"}}])
    c.check(ok.returncode == 0, ok.stdout)
    empty = run_on([{'name': 'Code', 'type': 'n8n-nodes-base.code', 'parameters': {'jsCode': 'return [];'}}])
    c.check(empty.returncode == 1 and 'no Postgres node' in empty.stdout, empty.stdout)

def deploy_canonical_offline(only):
    """deploy-canonical.py against a dead local port (never an instance): returns (exit code, all output)."""
    env = dict(os.environ, N8N_BASE_URL='http://127.0.0.1:9', N8N_API_KEY='test-only-never-valid')
    r = subprocess.run([sys.executable, str(N8N / 'deploy-canonical.py'), '--base', 'http://127.0.0.1:9', '--only', only],
                       capture_output=True, text=True, env=env, timeout=120)
    return r.returncode, r.stdout + r.stderr


with Case('pre-deploy: deploy-canonical refuses a canonical workflow whose SQL fails the guard, before any request') as c:
    target = N8N / 'workflows' / 'ZTqpumOpL1rNDOp6-Delete_File.json'
    original = target.read_bytes()
    try:
        w = json.loads(original)
        n = next(x for x in w['nodes'] if x['name'] == 'Delete File Chunks')
        n['parameters']['query'] = '-- $1 fileId\n' + n['parameters']['query']
        target.write_text(json.dumps(w, indent=2, ensure_ascii=False), encoding='utf-8')
        code, out = deploy_canonical_offline('ZTqpumOpL1rNDOp6')
    finally:
        target.write_bytes(original)
    c.check(code != 0 and 'refusing to deploy' in out, f'exit {code}: {out[-600:]}')
    c.check('Delete File Chunks :: line 1 :: placeholder-in-comment $1' in out, out[-600:])
    c.check('Connection refused' not in out and 'URLError' not in out, f'it contacted the instance before refusing: {out[-400:]}')

with Case('pre-deploy: a clean canonical workflow passes the guard and goes on to the instance (here: a dead port)') as c:
    code, out = deploy_canonical_offline('ZTqpumOpL1rNDOp6')
    c.check('refusing to deploy' not in out, out[-600:])
    c.check('Connection refused' in out or 'URLError' in out, f'exit {code}: {out[-600:]}')

with Case('the older template-local check (verify-reply-pipeline.py (13)) still passes alongside the generic one') as c:
    r = subprocess.run([sys.executable, str(N8N / 'verify-reply-pipeline.py')], capture_output=True, text=True)
    c.check(r.returncode == 0, r.stdout[-800:])

report()
