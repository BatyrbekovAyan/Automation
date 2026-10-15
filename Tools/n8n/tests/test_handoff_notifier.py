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

A5 (2026-10-15): the notice goes to the OWNER'S OWN number, resolved by the V2 rule of Auth Session / Claim Decide —
on Telegram `status.account.phone` (the AUTHORISED account) FIRST, the top-level `phone` (a number someone may have typed
into auth/phone) only as the fallback; WhatsApp reads the top-level phone; digits only. The rule is ONE constant of the
generator (`build-handoff-workflows.OWNER_PHONE_JS`; the A4 channel watch embeds the same text). Part A (the `A5 A` cases)
pins it through the real graph, Part D holds verify-history-handoff's owner-number tripwire to the wrong rules the driver
also catches, Part E runs the constant next to the REAL Claim Decide over every status shape (one rule, two consumers).

    python3 Tools/n8n/tests/test_handoff_notifier.py
"""
import copy
import json
import subprocess
import tempfile

from harness import N8N, Case, code, expr, module, report, run_js, wf

NTFY_FILE = 'crHandoffNtfy001-Handoff_Notifier.json'
W = wf(NTFY_FILE)
DRIVER = N8N / 'tests' / 'notifier_chain.js'
BY = {n['name']: n for n in W['nodes']}
TRIGGER = next(n['name'] for n in W['nodes'] if n['type'] == 'n8n-nodes-base.scheduleTrigger')
DUE = 'Due Notifications'

P1, P2, P3 = 'prof-wa-0001', 'prof-tg-0002', 'prof-wa-0003'
OWNER1, OWNER2 = '77010009991', '77010009992'
# A5: NAMED = a number someone typed into auth/phone (the top-level phone of a tapi status); ACCOUNT_RAW = the AUTHORISED
# account's phone as tapi may format it (only the digits may come out: ACCOUNT)
P4, P5, P6, P7 = 'prof-tg-0004', 'prof-tg-0005', 'prof-tg-0006', 'prof-wa-0007'
NAMED, ACCOUNT_RAW, ACCOUNT = '77010001111', '+7 (701) 000-99-94', '77010009994'
OWNER5, OWNER6, OWNER7 = '77010009995', '77010009996', '77010009997'


def row(id_, pid, chat, reason, channel):
    # what the DUE statement's `claimed` CTE returns: node-postgres hands a bigint id over as a string
    return {'id': str(id_), 'profile_id': pid, 'chat_id': chat, 'reason': reason, 'notify_attempts': 1, 'channel': channel}


OWNER_PHONE = {'byProfile': {
    P1: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P1, 'phone': OWNER1}},
    P2: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P2, 'phone': OWNER2}},
    # A5: tapi with an authorised account whose phone is NOT the top-level one
    P4: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P4, 'phone': NAMED, 'account': {'phone': ACCOUNT_RAW, 'user_id': 1000000000}}},
    # A5: tapi without an account object (the mock's answer when no account_phone is set) -> the top-level phone
    P5: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P5, 'phone': OWNER5}},
    # A5: tapi with an account that has no phone -> the top-level phone
    P6: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P6, 'phone': OWNER6, 'account': {'phone': None, 'user_id': 1000000000}}},
    # A5: WhatsApp never consults an `account` object (its get/status has none; if one ever appeared it is not the rule)
    P7: {'statusCode': 200, 'body': {'authorized': True, 'profile_id': P7, 'phone': OWNER7, 'account': {'phone': NAMED}}}},
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
    # A5: the owner's number by the V2 rule
    'tg_account_first': {'pg': {DUE: {'rows': [row(401, P4, '5123400001', 'order', 'telegram')]}},
                         'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'tg_top_level_fallback': {'pg': {DUE: {'rows': [row(402, P5, '5123400002', 'question', 'telegram')]}},
                              'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'tg_account_without_phone': {'pg': {DUE: {'rows': [row(403, P6, '5123400003', 'escalation', 'telegram')]}},
                                 'http': {'Owner Phone': OWNER_PHONE, 'Send Notice': SENT}},
    'wa_ignores_account': {'pg': {DUE: {'rows': [row(404, P7, '77015550004@c.us', 'order', 'whatsapp')]}},
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
A5_EXPECT = {
    'tg_account_first': [(401, P4, TAPI, ACCOUNT, '0001', 'новый заказ', 'sent')],
    'tg_top_level_fallback': [(402, P5, TAPI, OWNER5, '0002', 'вопрос клиента', 'sent')],
    'tg_account_without_phone': [(403, P6, TAPI, OWNER6, '0003', 'клиент ждёт ответа', 'sent')],
    'wa_ignores_account': [(404, P7, API, OWNER7, '0004', 'новый заказ', 'sent')],
}
A5_TITLES = {
    'tg_account_first': 'Telegram — the notice goes to status.account.phone (digits only), never to the top-level phone someone typed into auth/phone',
    'tg_top_level_fallback': 'Telegram without an account object — the top-level phone is the fallback',
    'tg_account_without_phone': 'Telegram with an account that has no phone — the top-level phone is the fallback',
    'wa_ignores_account': 'WhatsApp reads the top-level phone only — an account object is never consulted',
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

MEMORY_NODES = [n['name'] for n in W['nodes'] if n['type'] == 'n8n-nodes-base.postgres' and 'n8n_chat_histories' in n['parameters']['query']]
for name in SCENARIOS:
    with Case(f'T7 [{name}]: the dead drafts are settled inside the R9 node — one memory statement per tick, no new branch') as c:
        c.check(MEMORY_NODES == ['Settle Lost Sends'], f'nodes touching the bot memory: {MEMORY_NODES}')
        touched = [e for e in R[name]['effects'] if e['kind'] == 'db' and e['node'] in MEMORY_NODES]
        c.check(len(touched) == 1, f'memory statements this tick: {touched}')

for name, exp in EXPECT.items():
    with Case(f'F3 A [{name}]: a real request still reaches Owner Phone → (Send Notice) → Record Notify, about its own row') as c:
        for x in delivery_violations(R[name], exp):
            c.check(False, x)

for name, exp in A5_EXPECT.items():
    with Case(f'A5 A [{name}]: {A5_TITLES[name]}') as c:
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

# ------------------------------------------------------------------------------------------------------------------
# A5 D: verify-history-handoff's owner-number tripwire rejects exactly the wrong rules the driver also catches
def with_compose(w, old, new):
    """A deep copy of `w` whose Compose Notice code has `old` replaced by `new` (a mutation that does not apply is a test bug)."""
    bad = copy.deepcopy(w)
    n = next(n for n in bad['nodes'] if n['name'] == 'Compose Notice')
    assert old in n['parameters']['jsCode'], f'mutation anchor not found in Compose Notice: {old!r}'
    n['parameters']['jsCode'] = n['parameters']['jsCode'].replace(old, new, 1)
    return bad


EXPOSED_BY = {   # the verifier's wrong rule -> the scenario whose BEHAVIOUR shows the defect (a tripwire the driver cannot see is unproven)
    'only the top-level phone (the pre-A5 line)': 'tg_account_first',
    'the top-level phone first, the account second': 'tg_account_first',
    'the account first on every channel (WhatsApp included)': 'wa_ignores_account',
    'the account only, no fallback to the top-level phone': 'tg_top_level_fallback',
    'Telegram never recognised (the channel is always the WhatsApp one)': 'tg_account_first',
    'no digit-stripping': 'tg_account_first',
}

with Case('A5 D: verify-history-handoff accepts the canonical notice composer, and its tripwire rejects every wrong owner-number rule the driver also catches') as c:
    wrong_rules = getattr(vhh, 'OWNER_PHONE_WRONG', ())
    try:
        vhh.check_owner_phone_rule(copy.deepcopy(W))
    except AttributeError:
        c.check(False, 'verify-history-handoff.py has no check_owner_phone_rule (the owner-number rule is not pinned)')
    except AssertionError as e:
        c.check(False, f'canonical notifier rejected: {e}')
    c.check(bool(wrong_rules), 'verify-history-handoff.py has no OWNER_PHONE_WRONG (its tripwire is not self-checked)')
    if wrong_rules:
        c.check(set(EXPOSED_BY) == {label for label, _, _ in wrong_rules},
                f'the verifier\'s wrong rules and this test\'s EXPOSED_BY differ: {sorted(set(EXPOSED_BY) ^ {l for l, _, _ in wrong_rules})}')
    for label, old, new in wrong_rules:
        bad = with_compose(W, old, new)
        c.check(bad != W, f'{label}: mutation did not apply')
        try:
            vhh.check_owner_phone_rule(bad)
            c.check(False, f'{label}: the tripwire accepted it')
        except AssertionError:
            pass
        # the driver must see the same defect in behaviour, or the tripwire pins something unproven
        scenario = EXPOSED_BY.get(label)
        c.check(scenario is not None, f'{label}: no scenario of this test exposes it (add it to EXPOSED_BY)')
        if scenario:
            r = run(bad, {scenario: SCENARIOS[scenario]})[scenario]
            c.check(bool(delivery_violations(r, A5_EXPECT[scenario])), f'{label}: the driver did not catch it (scenario {scenario})')

# ------------------------------------------------------------------------------------------------------------------
# A5 E: the rule is ONE constant, embedded in Compose Notice, and it answers exactly like the V2 rule of Claim Decide
AUTH = wf('crAuthSession001-Auth_Session.json')


def v2_phone(channel, status):
    """What Auth Session's Claim Decide (V2, claim.start on an authorised profile) takes as the number from this get/status body."""
    named = {'Route': {'op': 'claim.start', 'profileId': 'P', 'channel': channel},
             'Verify Install': {'install_id': 'inst-A', 'verdict': 'checked'},
             'Claim Start': {'profile_id': 'P', 'owner_id': 'inst-O'}}
    out = code(AUTH, 'Claim Decide', {'json': {'statusCode': 200, 'body': dict(status, authorized=True)}}, named=named)[0]['json']
    return out.get('phone') or ''


SHAPES = (   # (label, channel, status body, the digits the owner's number must come out as)
    ('tg: account.phone differs from the top-level phone', 'telegram', {'phone': NAMED, 'account': {'phone': ACCOUNT_RAW}}, ACCOUNT),
    ('tg: only the account has a phone', 'telegram', {'account': {'phone': ACCOUNT_RAW}}, ACCOUNT),
    ('tg: account.phone is a number', 'telegram', {'phone': NAMED, 'account': {'phone': int(ACCOUNT)}}, ACCOUNT),
    ('tg: no account', 'telegram', {'phone': OWNER5}, OWNER5),
    ('tg: account is null', 'telegram', {'phone': OWNER5, 'account': None}, OWNER5),
    ('tg: account is not an object', 'telegram', {'phone': OWNER5, 'account': 'x'}, OWNER5),
    ('tg: account without a phone', 'telegram', {'phone': OWNER5, 'account': {'user_id': 1}}, OWNER5),
    ('tg: account.phone is empty', 'telegram', {'phone': OWNER5, 'account': {'phone': ''}}, OWNER5),
    ('tg: account.phone is null', 'telegram', {'phone': OWNER5, 'account': {'phone': None}}, OWNER5),
    ('tg: the top-level phone is formatted', 'telegram', {'phone': '+7 701 000-99-95'}, OWNER5),
    ('tg: account.phone has no digits (no fallback, as V2: the notice then fails as no_phone)', 'telegram', {'phone': OWNER5, 'account': {'phone': 'n/a'}}, ''),
    ('tg: no number anywhere', 'telegram', {'account': {}}, ''),
    ('wa: the top-level phone', 'whatsapp', {'phone': OWNER7}, OWNER7),
    ('wa: an account object is never consulted', 'whatsapp', {'phone': OWNER7, 'account': {'phone': NAMED}}, OWNER7),
    ('wa: the phone is formatted', 'whatsapp', {'phone': '+7 701 000-99-97'}, OWNER7),
    ('wa: no phone', 'whatsapp', {'phone': None}, ''),
)

hw, hw_error = None, None
try:
    hw = module('build-handoff-workflows.py')
except Exception as e:      # an import failure is a failure of THIS case, not a crash of the file
    hw_error = e

with Case('A5 E: build-handoff-workflows exports OWNER_PHONE_JS and Compose Notice embeds that text (one rule for the notifier and the A4 channel watch)') as c:
    c.check(hw is not None and hasattr(hw, 'OWNER_PHONE_JS'), f'build-handoff-workflows.py has no OWNER_PHONE_JS (import error: {hw_error!r})')
    if hw is not None and hasattr(hw, 'OWNER_PHONE_JS'):
        js = BY['Compose Notice']['parameters']['jsCode']
        c.check(hw.OWNER_PHONE_JS in js, 'Compose Notice does not embed OWNER_PHONE_JS verbatim')
        c.check(js.count('function ownerPhone(') == 1, 'Compose Notice must define ownerPhone once')

if hw is not None and hasattr(hw, 'OWNER_PHONE_JS'):
    for label, channel, status, want in SHAPES:
        with Case(f'A5 E [{label}]: ownerPhone and Claim Decide (V2) both give {want!r}') as c:
            wapi = 'tapi' if channel == 'telegram' else 'api'
            mine = run_js(hw.OWNER_PHONE_JS + '\nreturn [{ json: { phone: ownerPhone($json.wapi, $json.status) } }];',
                          {'json': {'wapi': wapi, 'status': status}})[0]['json']['phone']
            c.check(mine == want, f'ownerPhone({wapi!r}, {status}) = {mine!r}, expected {want!r}')
            v2 = v2_phone(channel, status)
            c.check(v2 == want, f'Claim Decide ({channel}) takes {v2!r}, expected {want!r} (the V2 rule moved)')
    with Case('A5 E: ownerPhone never throws on a body that is not an object') as c:
        for body in (None, 'oops', 7, []):
            for wapi in ('api', 'tapi'):
                mine = run_js(hw.OWNER_PHONE_JS + '\nreturn [{ json: { phone: ownerPhone($json.wapi, $json.status) } }];',
                              {'json': {'wapi': wapi, 'status': body}})[0]['json']['phone']
                c.check(mine == '', f'ownerPhone({wapi!r}, {body!r}) = {mine!r}, expected ""')

report()
