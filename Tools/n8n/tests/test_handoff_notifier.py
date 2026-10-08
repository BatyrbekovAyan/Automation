#!/usr/bin/env python3
"""F3 (iteration 2026-09-28, final regression 2026-10-08): the Handoff Notifier must not call Wappi — or write the
database — when there is nothing to notify.

`Due Notifications` (Postgres, alwaysOutputData + onError continueRegularOutput) answers ONE item on every tick whatever
happened: an empty `{}` when no request is due, `{message, error}` when the statement failed, the trigger's own item when
the node threw outside the query runner. On HEAD that item went straight into `Owner Phone`, so every 2 minutes the
notifier called `GET sync/get/status` with NO profile_id (on prod since 2026-09-24, ≈720 calls a day; the staging mock
log shows it at :18 of every even minute) and ran Compose Notice → Skip? → Settle Notice → Record Notify against a
non-existent id. Contract: only a real request row (a positive id AND a profile id) reaches the transport or the
database; the R9 branch off the same schedule (Settle Lost Sends) is untouched; a real request still goes Owner Phone →
Send Notice → Record Notify — one notice per request, each about ITS OWN row (the paired-item walk through the gate).

Part A runs the REAL canonical graph through notifier_chain.js (n8n's own expression engine, n8n-core's
alwaysOutputData / continue-on-error / paired-item semantics). Part B evaluates the gate's condition with n8n's engine
over every shape the Postgres node can emit. Part C holds verify-history-handoff.py's structural tripwire to its
negative cases and cross-checks them with the driver (a tripwire the driver would not also catch is unproven).

    python3 Tools/n8n/tests/test_handoff_notifier.py
"""
import copy
import json
import subprocess
import tempfile

from harness import N8N, Case, expr, module, report, wf

NTFY_FILE = 'crHandoffNtfy001-Handoff_Notifier.json'
W = wf(NTFY_FILE)
DRIVER = N8N / 'tests' / 'notifier_chain.js'
BY = {n['name']: n for n in W['nodes']}
TRIGGER = next(n['name'] for n in W['nodes'] if n['type'] == 'n8n-nodes-base.scheduleTrigger')
DUE = 'Due Notifications'

P1, P2, P3 = 'prof-wa-0001', 'prof-tg-0002', 'prof-wa-0003'
OWNER1, OWNER2 = '77010009991', '77010009992'


def row(id_, pid, chat, reason, channel):
    # what the DUE statement's `claimed` CTE returns: node-postgres hands a bigint id over as a string
    return {'id': str(id_), 'profile_id': pid, 'chat_id': chat, 'reason': reason, 'notify_attempts': 1, 'channel': channel}


OWNER_PHONE = {'byProfile': {
    P1: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P1, 'phone': OWNER1}},
    P2: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P2, 'phone': OWNER2}}},
    # what the staging mock (and a real Wappi for an unknown id) answers: no phone
    'default': {'statusCode': 200, 'body': {'authorized': True, 'profile_id': '', 'phone': None}}}
SENT = {'default': {'statusCode': 200, 'body': {'status': 'done', 'message_id': 'notice-1'}}}

SCENARIOS = {
    'no_due': {'pg': {DUE: {'empty': True}}, 'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'db_query_error': {'pg': {DUE: {'queryError': 'Connection terminated unexpectedly'}}, 'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'db_thrown': {'pg': {DUE: {'throw': 'timeout exceeded when trying to connect'}}, 'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'one_due': {'pg': {DUE: {'rows': [row(101, P1, '77011234567@c.us', 'order', 'whatsapp')]}},
                'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'two_due': {'pg': {DUE: {'rows': [row(201, P1, '77015554567@c.us', 'order', 'whatsapp'),
                                      row(202, P2, '5123498765', 'question', 'telegram')]}},
                'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'no_phone': {'pg': {DUE: {'rows': [row(301, P3, '77017770000@c.us', 'escalation', 'whatsapp')]}},
                 'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
}


def run(graph, scenarios=SCENARIOS):
    """One schedule tick per scenario of `graph` (a workflow dict) through notifier_chain.js."""
    with tempfile.NamedTemporaryFile('w', suffix='.json', encoding='utf-8') as f:
        json.dump(graph, f, ensure_ascii=False)
        f.flush()
        r = subprocess.run(['node', str(DRIVER), f.name], input=json.dumps(scenarios), text=True, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('driver failed: ' + r.stderr.strip()[-1500:])
    return json.loads(r.stdout)


def http_calls(r, node=None):
    return [e for e in r['effects'] if e['kind'] == 'http' and (node is None or e['node'] == node)]


def db_calls(r, node):
    return [e for e in r['effects'] if e['kind'] == 'db' and e['node'] == node]


def quiet_violations(r):
    """What a tick with nothing to notify must NOT do: any transport call, any notice write."""
    v = []
    if r['stoppedAt']:
        v.append(f'stopped at {r["stoppedAt"]}')
    for e in http_calls(r):
        v.append(f'transport call {e["node"]} {e["method"]} {e["url"]} query={e.get("query")}')
    for e in db_calls(r, 'Record Notify'):
        v.append(f'Record Notify ran with {e["params"]}')
    return v


def r9_violations(r):
    """The R9 branch is untouched: it runs once per tick, BEFORE the notice branch, and leads nowhere."""
    v = []
    if r['runs'].get('Settle Lost Sends') != 1:
        v.append(f'Settle Lost Sends ran {r["runs"].get("Settle Lost Sends")} times')
    t = r['trace']
    if 'Settle Lost Sends' in t and DUE in t and t.index('Settle Lost Sends') > t.index(DUE):
        v.append(f'Settle Lost Sends ran after {DUE}: {t}')
    if 'Settle Lost Sends' in t and t.index('Settle Lost Sends') + 1 < len(t) and t[t.index('Settle Lost Sends') + 1] != DUE:
        v.append(f'something ran off Settle Lost Sends: {t}')
    if db_calls(r, 'Settle Lost Sends') != [{'node': 'Settle Lost Sends', 'kind': 'db', 'params': [], 'item': 0}]:
        v.append(f'Settle Lost Sends params changed: {db_calls(r, "Settle Lost Sends")}')
    return v


def delivery_violations(r, expected):
    """expected: [(row id, profile, base, owner phone, chat tail, reason word, state)] — one notice per request,
    each built from ITS OWN row."""
    v = []
    if r['stoppedAt']:
        return [f'stopped at {r["stoppedAt"]}']
    ph = sorted((e['url'], (e.get('query') or {}).get('profile_id')) for e in http_calls(r, 'Owner Phone'))
    want_ph = sorted((base + 'sync/get/status', pid) for _, pid, base, *_ in expected)
    if ph != want_ph:
        v.append(f'Owner Phone calls {ph} != {want_ph}')
    sends = sorted(((e.get('query') or {}).get('profile_id'), e['url'], (e.get('body') or {}).get('recipient'), (e.get('body') or {}).get('body', ''))
                   for e in http_calls(r, 'Send Notice'))
    want_sends = sorted((pid, base + 'sync/message/send', phone) for _, pid, base, phone, _, _, state in expected if state == 'sent')
    if [s[:3] for s in sends] != want_sends:
        v.append(f'Send Notice calls {[s[:3] for s in sends]} != {want_sends}')
    for _, pid, _, _, tail, what, state in expected:
        if state != 'sent':
            continue
        body = next((s[3] for s in sends if s[0] == pid), '')
        if f'(чат …{tail})' not in body or what not in body or not body.startswith('Choose Reply:'):
            v.append(f'notice for {pid} is not about its own row: {body!r}')
    rec = sorted(tuple(e['params']) for e in db_calls(r, 'Record Notify'))
    want_rec = sorted((str(id_), state, '' if state == 'sent' else 'no_phone') for id_, _, _, _, _, _, state in expected)
    if rec != want_rec:
        v.append(f'Record Notify {rec} != {want_rec}')
    return v


API, TAPI = 'https://wappi.pro/api/', 'https://wappi.pro/tapi/'
EXPECT = {
    'one_due': [(101, P1, API, OWNER1, '4567', 'новый заказ', 'sent')],
    'two_due': [(201, P1, API, OWNER1, '4567', 'новый заказ', 'sent'), (202, P2, TAPI, OWNER2, '8765', 'вопрос клиента', 'sent')],
    'no_phone': [(301, P3, API, None, '0000', 'клиент ждёт ответа', 'failed')],
}

# ------------------------------------------------------------------------------------------------------------------
R = run(W)

for name in ('no_due', 'db_query_error', 'db_thrown'):
    with Case(f'F3 A [{name}]: a tick with nothing due calls no Wappi endpoint and writes no notice state') as c:
        for x in quiet_violations(R[name]):
            c.check(False, x)
        c.check(R[name]['runs'].get(DUE) == 1, f'{DUE} must still run: {R[name]["runs"]}')

for name in SCENARIOS:
    with Case(f'F3 A [{name}]: the R9 lost-send sweep off the same schedule is unchanged (once, first, leads nowhere)') as c:
        for x in r9_violations(R[name]):
            c.check(False, x)

for name, exp in EXPECT.items():
    with Case(f'F3 A [{name}]: a real request still reaches Owner Phone → (Send Notice) → Record Notify, about its own row') as c:
        for x in delivery_violations(R[name], exp):
            c.check(False, x)

# ------------------------------------------------------------------------------------------------------------------
gates = [e['node'] for e in (W['connections'].get(DUE, {}).get('main') or [[]])[0]]
GATE = gates[0] if len(gates) == 1 and BY.get(gates[0], {}).get('type') == 'n8n-nodes-base.if' else None

with Case(f'F3 B: {DUE} feeds exactly one If gate, and its condition passes ONLY a real request row (n8n expression engine)') as c:
    c.check(GATE is not None, f'{DUE} must feed exactly one If node, found {gates}')
    if GATE:
        conds = BY[GATE]['parameters']['conditions']['conditions']
        c.check(len(conds) == 1 and conds[0]['operator'] == {'type': 'boolean', 'operation': 'true', 'singleValue': True},
                f'{GATE}: one boolean-true condition expected: {conds}')
        lv = conds[0]['leftValue']
        shapes = {
            'empty result (alwaysOutputData)': ({}, False),
            'query error (continueOnFail)': ({'message': 'Connection terminated', 'error': {'message': 'Connection terminated'}}, False),
            'thrown error (the trigger item passes on)': (dict(timestamp='2026-10-08T17:38:00.004+05:00', Hour='17'), False),
            'non-SELECT success marker': ({'success': True}, False),
            'id 0': ({'id': '0', 'profile_id': P1}, False),
            'negative id': ({'id': '-5', 'profile_id': P1}, False),
            'id not a number': ({'id': 'abc', 'profile_id': P1}, False),
            'no profile': ({'id': '12'}, False),
            'empty profile': ({'id': '12', 'profile_id': ''}, False),
            'null profile': ({'id': '12', 'profile_id': None}, False),
            'non-string profile': ({'id': '12', 'profile_id': 12}, False),
            'row (bigint as string)': (row(12, P1, 'c@c.us', 'order', 'whatsapp'), True),
            'row (numeric id)': (dict(row(12, P1, 'c@c.us', 'order', 'whatsapp'), id=12), True),
        }
        for label, (j, want) in shapes.items():
            got = expr(lv, j)
            c.check(got is want, f'{label}: {GATE} evaluates {got!r}, expected {want}')

# ------------------------------------------------------------------------------------------------------------------
vhh = module('verify-history-handoff.py')
link = lambda *dst: {'main': [[{'node': d, 'type': 'main', 'index': 0} for d in dst]]}


def mutations():
    g = GATE or '(no gate)'

    def pre_fix(w):         # the HEAD shape until 2026-10-08: the Postgres item goes straight to the transport
        w['connections'][DUE] = link('Owner Phone')

    def bypass(w):          # the gate exists but a second edge goes around it
        w['connections'][DUE] = link(g, 'Owner Phone')

    def false_branch(w):    # what the gate refuses is sent on anyway
        w['connections'][g]['main'] = [[{'node': 'Owner Phone', 'type': 'main', 'index': 0}],
                                       [{'node': 'Owner Phone', 'type': 'main', 'index': 0}]]

    def weak_condition(w):  # an always-true gate
        next(n for n in w['nodes'] if n['name'] == g)['parameters']['conditions']['conditions'][0]['leftValue'] = '={{ true }}'

    def swapped_outputs(w):  # what the gate passes goes nowhere, what it refuses goes to the transport
        w['connections'][g]['main'] = [[], [{'node': 'Owner Phone', 'type': 'main', 'index': 0}]]

    return (('pre-fix (no gate)', pre_fix, 'no_due'), ('second edge around the gate', bypass, 'no_due'),
            ('false branch wired on', false_branch, 'no_due'), ('always-true condition', weak_condition, 'no_due'),
            ('gate outputs swapped', swapped_outputs, 'no_due'), ('gate outputs swapped', swapped_outputs, 'one_due'))


with Case('F3 C: verify-history-handoff accepts the canonical notifier and its tripwire rejects every bypass the driver also catches') as c:
    try:
        vhh.check_notifier_gate(copy.deepcopy(W))
    except AttributeError:
        c.check(False, 'verify-history-handoff.py has no check_notifier_gate (the gate is not pinned)')
    except AssertionError as e:
        c.check(False, f'canonical notifier rejected: {e}')
    if GATE:
        for label, mutate, scenario in mutations():
            bad = copy.deepcopy(W)
            mutate(bad)
            c.check(bad != W, f'{label}: mutation did not apply')
            try:
                vhh.check_notifier_gate(bad)
                c.check(False, f'{label}: the tripwire accepted it')
            except AssertionError:
                pass
            except AttributeError:
                pass
            # the driver must see the same defect in behaviour, or the tripwire pins something unproven
            r = run(bad, {scenario: SCENARIOS[scenario]})[scenario]
            seen = quiet_violations(r) if scenario == 'no_due' else delivery_violations(r, EXPECT[scenario])
            c.check(bool(seen), f'{label}: the driver did not catch it (scenario {scenario})')

report()
