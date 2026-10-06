#!/usr/bin/env python3
"""V1 (review 2026-09-28): a query parameter must never be able to leave its SQL literal — not through a comment
that names it, not through a literal that contains it. Behavioural, with the REAL formatter.

The n8n Postgres node formats every query on the client with pg-promise (`pgp.helpers.concat([{query, values,
options:{partial:true}}])`), which fills `$N` everywhere in the text — comments included. Seven nodes named their
parameters in `--` comments («$1 fileId, $2 gate mode…»), so a value with a newline ended the comment and its tail
ran as a separate statement: Delete File (no token in the compat window, any bootstrap token after it), Suggest
Replies, Create WA/TG, the new Auth Session, and RevenueCat Events (secret-holder only, latent).

What runs here — no n8n server, no network, never production:
  * the installed pg-promise (the staging n8n's own copy, = prod's version) formats the REAL query of each node;
  * a Postgres-faithful tokenizer (verify-sql-comments.lex) decides whether an injected marker ended up in CODE;
  * the formatted SQL is executed in the local staging Postgres on session-temp tables inside BEGIN … ROLLBACK,
    and the marker table must not exist afterwards; the legitimate values still give the pinned result rows.

Not vacuous after the fix: the seven nodes are PINNED by name (a rename fails here, it does not silently drop out),
every one of them must still take its parameters in code, and a POSITIVE CONTROL — synthetic queries that carry
the placeholder in a line comment, a block comment and a literal — must be caught by both the tokenizer and the
execution. A corpus-wide pass then formats EVERY Postgres node of every canonical workflow with each payload in
each parameter position (static, no database).

    python3 Tools/n8n/tests/test_sql_comment_injection.py
"""
import json
import os
import subprocess

from harness import WF, Case, module, node, report, sql_run, wf

guard = module('verify-sql-comments.py')

MARKER = 'cr_inj_marker'
CREATE = f'create temp table {MARKER}(x int)'
PAYLOADS = {   # how a value escapes each context; pg-promise doubles ' but leaves \n, \r and */ alone
    'newline': f'x\n; {CREATE}; --',
    'carriage-return': f'x\r; {CREATE}; --',
    'block-close': f'x */ ; {CREATE}; /* ',
    'quote': f"'; {CREATE}; select '",
}

_FORMAT = r"""const fs=require('fs'), path=require('path'), os=require('os');
const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const pgp = require(require.resolve('pg-promise', {paths: [path.join(N, 'n8n-nodes-base')]}))();
const jobs = JSON.parse(fs.readFileSync(0, 'utf8'));
process.stdout.write(JSON.stringify(jobs.map(j => pgp.helpers.concat([{query: j.query, values: j.values, options: {partial: true}}]))));"""


def pg_format(jobs):
    """[(query, values)] -> the exact text the n8n Postgres node (queryBatching 'single') hands to the server."""
    r = subprocess.run(['node', '-e', _FORMAT], input=json.dumps([{'query': q, 'values': v} for q, v in jobs]),
                       text=True, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('pg-promise format failed: ' + r.stderr.strip()[-1500:])
    return json.loads(r.stdout)


def marker_in_code(sql):
    return any(kind == 'code' and MARKER in sql[a:b] for kind, a, b, _ in guard.lex(sql))


def marker_created(formatted, setup):
    """Execute in the local staging Postgres (temp tables, rolled back); True when the marker table now exists."""
    out = sql_run(formatted + "\n;\nselect 'MARKER=' || (to_regclass('pg_temp." + MARKER + "') is not null)::text;", setup)
    last = [l for l in out.splitlines() if l.startswith('MARKER=')]
    return last[-1] == 'MARKER=true'


def run_legit(formatted, setup, after):
    out = sql_run(formatted + '\n;\n' + after, setup)
    return [l for l in out.splitlines() if l.strip()]


def code_placeholders(q):
    """Highest $N the query's CODE uses (what the node's queryReplacement must supply)."""
    hi = 0
    for kind, a, b, _ in guard.lex(q):
        if kind == 'code':
            for m in guard.PLACEHOLDER_TOKEN.finditer(q[a:b]):
                hi = max(hi, int(m.group(0)[1:]))
    return hi


LIKE = 'create temp table {0} (like public.{0} including all);'
UTC = "set local timezone = 'UTC';"
GUID = '3f2b8c1e-9d4a-4b6f-8e21-5a7c0d9e1f42'
RC_ID = '$RCAnonymousID:0123456789abcdef0123456789abcdef'

# The seven nodes whose comments named their parameters before V1 (reviewed HEAD 841d6cb7 — and prod f08546f1, except
# the new Create Install). `legit` are the values the node's queryReplacement evaluates to in a normal call; `text` the
# positions the node uses as TEXT in code (a marker value there must stay an ordinary literal and run); `json` the
# ::jsonb positions (a valid JSON value may carry raw newlines — the R7-part-2 symptom: it must run exactly like the
# compact one); `expect` the rows the legit call gives — pinned from HEAD before the fix, so a comment edit that
# changed behaviour fails.
REG_SETUP = UTC + LIKE.format('subscribers') + LIKE.format('bot_profiles')
REG_AFTER = ("select profile_id, app_user_id, channel, coalesce(bot_key, '-') from bot_profiles order by 1;\n"
             "select app_user_id, plan, status from subscribers order by 1;")
PINNED = [
    dict(file='ZTqpumOpL1rNDOp6-Delete_File.json', node='Delete File Chunks', text=[0, 1, 2], json=[3],
         setup=UTC + LIKE.format('documents') + LIKE.format('uploaded_files') +
         f"""insert into documents (id, content, metadata) values
               (9101, 'chunk', '{{"fileId": "{GUID}", "botWaId": "wfOwnWA00000001"}}'),
               (9102, 'other', '{{"fileId": "other-file", "botWaId": "wfOwnWA00000001"}}');
             insert into uploaded_files (file_id, owner_id, bot_wa_id) values ('{GUID}', 'inst-A', 'wfOwnWA00000001');""",
         legit=[GUID, 'session', 'inst-A', '["wfOwnWA00000001"]'],
         after="select count(*) from documents; select count(*) from uploaded_files where deleted_at is not null;",
         expect=['1|t', '1', '1']),
    dict(file='crAuthSession001-Auth_Session.json', node='Create Install', text=[0, 1, 2, 3, 4],
         setup=UTC + LIKE.format('installs') + LIKE.format('bot_profiles') + LIKE.format('billing_bindings'),
         legit=['inst-v1-legit', 'a' * 64, 'device-fallback-id', 'ios', '1.0.0'],
         after="select install_id, app_user_id, platform, app_version from installs order by 1;",
         expect=['inst-v1-legit||1|{}|{}|{}|f', 'inst-v1-legit||ios|1.0.0']),
    dict(file='9PTyYcelRQI7bGDb-Suggest_Replies.json', node='Suggestion Gate', text=[0, 1],
         setup=UTC + LIKE.format('subscribers') + LIKE.format('suggestion_counts') + LIKE.format('suggestion_request_keys') +
         f"insert into subscribers (app_user_id, plan, status) values ('{RC_ID}', 'business', 'active');",
         legit=[RC_ID, 'chat1|tail|'],
         after="select app_user_id, n from suggestion_counts order by 1; select count(*) from suggestion_request_keys;",
         expect=['t|active|1|t', f'{RC_ID}|1', '1']),
    # $3 (channel) is a constant of the node's own expression and CHECK-constrained — not a text the caller controls
    dict(file='XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', node='Register Profile', text=[0, 1, 3],
         setup=REG_SETUP, legit=['wa-profile-1', RC_ID, 'whatsapp', 'Bot0'], after=REG_AFTER,
         expect=[f'wa-profile-1|{RC_ID}|whatsapp|Bot0', f'{RC_ID}|trial|trialing']),
    dict(file='Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', node='Register Profile', text=[0, 1, 3],
         setup=REG_SETUP, legit=['tg-profile-1', RC_ID, 'telegram', 'Bot0'], after=REG_AFTER,
         expect=[f'tg-profile-1|{RC_ID}|telegram|Bot0', f'{RC_ID}|trial|trialing']),
    dict(file='ZGYr6srzS3rSSXHp-RevenueCat_Events.json', node='Transfer Subscriber', text=[], json=[0, 1],
         setup=UTC + LIKE.format('subscribers') + LIKE.format('dialog_counts') +
         """insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, product_id)
              values ('old-id', 'business', 'active', '2099-01-01T00:00:00Z', 5, 'cr_business_m');""",
         legit=['["new-id"]', '["old-id"]'],
         after="select app_user_id, plan, status, topup_balance from subscribers order by 1;",
         expect=['1|1|1|1|1|0|5', 'new-id|business|active|5', 'old-id|business|expired|0']),
    dict(file='ZGYr6srzS3rSSXHp-RevenueCat_Events.json', node='Consolidate Aliases', text=[0, 2, 3, 6], json=[1],
         setup=UTC + LIKE.format('subscribers') + LIKE.format('dialog_counts') +
         """insert into subscribers (app_user_id, plan, status, topup_balance) values ('alias-id', 'start', 'active', 3);""",
         legit=['live-id', '["alias-id"]', 'business', 'active', '2099-02-01T00:00:00Z', 2, 'cr_business_m'],
         after="select app_user_id, status, topup_balance from subscribers order by 1;",
         expect=['live-id|business|active|2099-02-01 00:00:00+00|2|cr_business_m|1|1|0|3', 'alias-id|expired|0', 'live-id|trialing|3']),
]


def query_of(p):
    n = node(wf(p['file']), p['node'])
    return n, n['parameters']['query']


# ------------------------------------------------------------------------------------------- positive control
CONTROLS = [
    ('line comment', '-- the caller id is $1\nselect $1::text as v', 'newline'),
    ('line comment, CR', '-- the caller id is $1\nselect $1::text as v', 'carriage-return'),
    ('block comment', '/* the caller id is $1 */ select $1::text as v', 'block-close'),
    ('string literal', "select 'caller $1' as v", 'quote'),
]
ctl_fmt = pg_format([(q, [PAYLOADS[p]]) for _, q, p in CONTROLS])
for (title, q, p), formatted in zip(CONTROLS, ctl_fmt):
    with Case(f'positive control: a placeholder in a {title} lets the value out — tokenizer AND execution catch it') as c:
        c.check(marker_in_code(formatted), f'tokenizer missed the escape: {formatted!r}')
        c.check(marker_created(formatted, ''), f'execution did not create the marker: {formatted!r}')
        c.check(any(r[0] in ('placeholder-in-comment', 'placeholder-in-literal') for r in guard.check_query(q)),
                f'verify-sql-comments passes the control query: {guard.check_query(q)}')
with Case('positive control: the same placeholder in CODE keeps every payload a literal') as c:
    formatted = pg_format([('select $1::text as v', [v]) for v in PAYLOADS.values()])
    for (name, _), f in zip(PAYLOADS.items(), formatted):
        c.check(not marker_in_code(f), f'{name}: {f!r}')
        c.check(not marker_created(f, ''), f'{name}: executed {f!r}')

# ------------------------------------------------------------------------------------------- the seven nodes
def pinned_node_runs_its_parameters(p, title):
    with Case(f'{title}: pinned node still runs its own parameters in code') as c:
        n, q = query_of(p)
        c.check(n['type'] == guard.POSTGRES_TYPE and n['parameters'].get('operation', 'executeQuery') == 'executeQuery',
                f"not a Postgres executeQuery node: {n['type']}")
        c.check(code_placeholders(q) == len(p['legit']), f'code uses ${code_placeholders(q)}, fixture has {len(p["legit"])} values')


def no_payload_reaches_code(p, title):
    with Case(f'{title}: no injected value reaches code, in any parameter position (real pg-promise)') as c:
        n, q = query_of(p)
        jobs, labels = [], []
        for pos in range(len(p['legit'])):
            for name, payload in PAYLOADS.items():
                vals = list(p['legit'])
                vals[pos] = payload
                jobs.append((q, vals))
                labels.append(f'${pos + 1} {name}')
        for label, f in zip(labels, pg_format(jobs)):
            c.check(not marker_in_code(f), f'{label}: the value escapes into code')


def newline_text_runs_no_extra_statement(p, title):
    if not p['text']:
        return   # every parameter is ::jsonb — covered by the multi-line JSON case instead (no vacuous PASS here)
    with Case(f'{title}: executed with a newline value in each TEXT parameter, no extra statement runs') as c:
        n, q = query_of(p)
        for pos in p['text']:
            vals = list(p['legit'])
            vals[pos] = PAYLOADS['newline']
            formatted = pg_format([(q, vals)])[0]
            try:
                c.check(not marker_created(formatted, p['setup']), f'${pos + 1}: the injected statement RAN')
            except RuntimeError as e:
                c.check(False, f'${pos + 1}: the formatted statement broke: {str(e)[-300:]}')


def legit_rows_pinned(p, title):
    with Case(f'{title}: the legitimate call gives the pinned rows') as c:
        n, q = query_of(p)
        got = run_legit(pg_format([(q, p['legit'])])[0], p['setup'], p['after'])
        c.check(got == p['expect'], f'got {got!r}, pinned {p["expect"]!r}')


def multiline_json_same_rows(p, title):
    if not p.get('json'):
        return
    with Case(f'{title}: a multi-line (pretty-printed) JSON parameter gives the same rows as the compact one') as c:
        n, q = query_of(p)
        vals = list(p['legit'])
        for pos in p['json']:
            vals[pos] = json.dumps(json.loads(vals[pos]), indent=1)
            c.check('\n' in vals[pos], f'${pos + 1}: the pretty value has no newline: {vals[pos]!r}')
        try:
            got = run_legit(pg_format([(q, vals)])[0], p['setup'], p['after'])
            c.check(got == p['expect'], f'got {got!r}, pinned {p["expect"]!r}')
        except RuntimeError as e:
            c.check(False, f'the multi-line value broke the statement: {str(e)[-300:]}')


for p in PINNED:
    title = f"{p['file'].split('-', 1)[1].rsplit('.', 1)[0]} / {p['node']}"
    for case in (pinned_node_runs_its_parameters, no_payload_reaches_code, newline_text_runs_no_extra_statement,
                 legit_rows_pinned, multiline_json_same_rows):
        case(p, title)

# ------------------------------------------------------------------------------------------- every node, statically
with Case('corpus: in EVERY Postgres node of every canonical workflow, no payload in any position reaches code') as c:
    jobs, labels, nodes = [], [], 0
    for f in sorted(os.listdir(WF)):
        if not f.endswith('.json'):
            continue
        for n, q in guard.postgres_nodes(wf(f)):
            if not isinstance(q, str):
                c.check(False, f'{f} / {n["name"]}: query is not a string')
                continue
            nodes += 1
            for pos in range(max(code_placeholders(q), 1)):
                for name, payload in PAYLOADS.items():
                    vals = ['v'] * max(code_placeholders(q), 1)
                    vals[pos] = payload
                    jobs.append((q, vals))
                    labels.append(f'{f} / {n["name"]} ${pos + 1} {name}')
    c.check(nodes >= 70, f'only {nodes} Postgres nodes scanned — the canonical set has 72')
    for label, f in zip(labels, pg_format(jobs)):
        c.check(not marker_in_code(f), f'{label}: the value escapes into code')

report()
