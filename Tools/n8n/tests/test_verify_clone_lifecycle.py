#!/usr/bin/env python3
"""T4 (F1 + F2, plan docs/audits/2026-10-09-followups/F1F2-plan.md §3 T4): the structural invariants of the bot's line in
Create WhatsApp/Telegram cannot be broken quietly. Each mutation below is a way the graph of plan §1.4 can rot — a
generator re-adding an old edge, a hand edit in the n8n editor, a dev re-export — applied to ONE Create file at a time
(WhatsApp and Telegram) in a temp copy of the canonical set; the REAL verifier (subprocess, `--dir`) must reject it with a
failure naming that file (or the WA/TG parity). The unmodified copy must pass (a verifier red on everything detects
nothing), and the pre-F1 canon (git 5ac9b2f9: Restamp RAG Chunks, no plan, no marker gate, the public API) must fail.

    python3 Tools/n8n/tests/test_verify_clone_lifecycle.py                    # verify-clone-lifecycle.py — must pass
    python3 Tools/n8n/tests/test_verify_clone_lifecycle.py --existing-guards  # the same mutations against the guards that
                                                                             # existed before T4 (verify-reply-mode /
                                                                             # verify-auth-gate / verify-telegram-parity):
                                                                             # what slips through them is why T4 exists
No n8n, no network, nothing written outside a temp dir.
"""
import contextlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from harness import N8N, WF, Case, report

EXISTING = '--existing-guards' in sys.argv
VERIFIER = N8N / 'verify-clone-lifecycle.py'
GUARDS = [N8N / 'verify-reply-mode.py', N8N / 'verify-auth-gate.py', N8N / 'verify-telegram-parity.py']
CREATE = {'WA': 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'TG': 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json'}
PRE_F1 = '5ac9b2f9'


def run(d):
    """(rejected?, output) of the verifier(s) under test on the set in `d`."""
    tools = GUARDS if EXISTING else [VERIFIER]
    summary, full, bad = [], [], False
    for t in tools:
        r = subprocess.run([sys.executable, str(t), '--dir', str(d)], capture_output=True, text=True)
        text = (r.stdout + r.stderr).strip()
        fails = [l for l in text.splitlines() if 'FAIL' in l] or text.splitlines()[-1:]
        summary.append(f'[{t.name} exit {r.returncode}] ' + ' / '.join(x[:220] for x in fails[:2]))
        full.append(text)
        bad |= r.returncode != 0
    return bad, ' | '.join(summary), '\n'.join(full)


@contextlib.contextmanager
def canon_copy():
    d = Path(tempfile.mkdtemp(prefix='clone-life-'))
    try:
        for p in WF.glob('*.json'):
            shutil.copy(p, d / p.name)
        yield d
    finally:
        shutil.rmtree(d)


def nd(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def link(*targets):
    return {'main': [[{'node': t, 'type': 'main', 'index': 0} for t in targets]]}


def set_left(w, name, left):
    nd(w, name)['parameters']['conditions']['conditions'][0]['leftValue'] = left


def drop_protected(text):
    m = re.search(r'\["[A-Za-z0-9]{8,36}", ', text)
    return text[:m.start() + 1] + text[m.end():]


# --------------------------------------------------------------------------------------------- mutations (one file)
def m_bind_bypasses_marker(w):              # the answer with an id after ANY bind outcome (the pre-F1 shape)
    w['connections']['Bind Workflow Id'] = link('Send New Workflows Id')


def m_discard_edge_removed(w):              # a failed activation answers without deleting the new clone
    w['connections']['Activated?']['main'][1] = [{'node': 'Respond Create Failed', 'type': 'main', 'index': 0}]


def m_discard_edge_cut(w):                  # a failed Wappi webhook ends the run: no discard, no answer
    w['connections']['Webhook Set?']['main'][1] = []


def m_activate_without_never_error(w):     # a refused activation stops the run (n8n 500) — no discard
    nd(w, 'Activate Created Workflow')['parameters']['options'].pop('response')


def m_seed_saved_to_template(w):           # the plan (reuse, foreign refusal) bypassed
    w['connections']['Seed Saved?']['main'][0] = [{'node': 'Get Sample Workflow', 'type': 'main', 'index': 0}]


def m_refusal_with_id(w):                  # a refusal carrying the new clone's id (the client would bind it)
    nd(w, 'Respond Create Failed')['parameters']['assignments']['assignments'].append(
        {'id': 'mut-id', 'name': 'id', 'value': "={{ $('Clone Id').first().json.id }}", 'type': 'string'})


def m_refusal_merges_input(w):
    nd(w, 'Respond Clone Unavailable')['parameters']['includeOtherFields'] = True


def m_previous_profile_in_rule(w):         # the retired profile is not in the token: the gate would refuse every reconnect
    g = nd(w, 'Auth Gate')['parameters']
    g['jsCode'] = re.sub(r'(const RULE = \{"profiles": \[)', r'\1"PreviousProfileId", ', g['jsCode'], count=1)


def m_protected_drift_reuse(w):
    p = nd(w, 'Reuse?')['parameters']['conditions']['conditions'][0]
    p['leftValue'] = drop_protected(p['leftValue'])


def m_protected_drift_replaced(w):
    p = nd(w, 'Replaced Items')['parameters']
    p['jsCode'] = drop_protected(p['jsCode'])


def m_api_public_host(w):                  # back to DNS + Caddy on the hot path
    p = nd(w, 'Activate Created Workflow')['parameters']
    p['url'] = p['url'].replace('http://localhost:5678', 'https://n8n.choosereply.com')


def m_api_bound_credential(w):             # a live / dev re-export: a bound credential id instead of the placeholder
    nd(w, 'Delete Replaced Clone')['credentials']['httpHeaderAuth']['id'] = 'RV7m661NMLPXEcvm'


def m_bound_reads_success(w):              # «the node continued» instead of the positive marker
    set_left(w, 'Bound?', '={{ $json.success === true || $json.bound === true }}')


def m_plan_without_retry(w):
    nd(w, 'Clone Plan').pop('retryOnFail')


def m_plan_read_continues(w):              # an empty / error plan would go on to create
    set_left(w, 'Plan Read?', '={{ !$json.error }}')


def m_new_clone_skips_activation(w):
    set_left(w, 'Already Active?', '={{ $json.active === true }}')


def m_discard_reused(w):                   # a failure after REUSE would delete the bot's working clone
    set_left(w, 'Discard?', "={{ ((c) => !!c && typeof c.id === 'string')($('Clone Id').first().json) }}")


def m_probe_unchecked(w):                  # any probe answer counts as «usable»
    w['connections']['Probe Bound Clone'] = link('Reused Clone Id')


def m_restamp_back(w):                     # a stale generator re-adds the retired node on the hot path
    w['nodes'].append({'parameters': {'operation': 'executeQuery', 'query': 'select 1;', 'options': {}}, 'id': 'mut-restamp',
                       'name': 'Restamp RAG Chunks', 'type': 'n8n-nodes-base.postgres', 'typeVersion': 2.6, 'position': [0, 0],
                       'credentials': {'postgres': {'id': 'vvRrFiEXzLVqKjOx', 'name': 'Postgres'}}})
    w['connections']['Types Set?']['main'][0] = [{'node': 'Restamp RAG Chunks', 'type': 'main', 'index': 0}]
    w['connections']['Restamp RAG Chunks'] = link('Bind Workflow Id')


def m_bind_sql_drift(w):                   # a hand-edited bind statement (not clone_lineage's)
    p = nd(w, 'Bind Workflow Id')['parameters']
    p['query'] = p['query'].replace("and not r.taken", "and true", 1)


def m_answer_without_adopted(w):           # the client clears its retired keys only by adoptedProfiles
    a = nd(w, 'Send New Workflows Id')['parameters']['assignments']['assignments']
    nd(w, 'Send New Workflows Id')['parameters']['assignments']['assignments'] = [x for x in a if x['name'] != 'adoptedProfiles']


MUTATIONS = [m_bind_bypasses_marker, m_discard_edge_removed, m_discard_edge_cut, m_activate_without_never_error,
             m_seed_saved_to_template, m_refusal_with_id, m_refusal_merges_input, m_previous_profile_in_rule,
             m_protected_drift_reuse, m_protected_drift_replaced, m_api_public_host, m_api_bound_credential,
             m_bound_reads_success, m_plan_without_retry, m_plan_read_continues, m_new_clone_skips_activation,
             m_discard_reused, m_probe_unchecked, m_restamp_back, m_bind_sql_drift, m_answer_without_adopted]
LABEL = 'existing guards' if EXISTING else 'verify-clone-lifecycle'

with Case(f'T4 [{LABEL}] control: the unmodified canonical set passes') as c:
    with canon_copy() as d:
        bad, out, _ = run(d)
        c.check(not bad, f'the canonical set is rejected: {out}')

if not EXISTING:
    with Case(f'T4 [{LABEL}] the pre-F1 canon ({PRE_F1}: Restamp, no plan, no marker gate, the public API) is rejected — both files') as c:
        r = subprocess.run([sys.executable, str(VERIFIER), '--rev', PRE_F1], capture_output=True, text=True)
        out = r.stdout + r.stderr
        c.check(r.returncode == 1, f'exit {r.returncode}')
        for f in CREATE.values():
            for section in ('(1) entry', '(2) the answer only on the positive bind', '(3) checked calls', '(4) the Postgres decisions',
                            '(6) n8n API'):
                c.check(f'{f} {section}' in out, f'{f}: section {section!r} did not fail on the pre-F1 graph')

for fn in MUTATIONS:
    for ch, fname in CREATE.items():
        with Case(f'T4 [{LABEL}] NEG {fn.__name__[2:]} [{ch}] is rejected') as c:
            with canon_copy() as d:
                p = d / fname
                w = json.loads(p.read_text(encoding='utf-8'))
                before = json.dumps(w, sort_keys=True)
                fn(w)
                c.check(json.dumps(w, sort_keys=True) != before, 'the mutation did not apply')
                p.write_text(json.dumps(w, ensure_ascii=False, indent=2), encoding='utf-8')
                bad, out, full = run(d)
                c.check(bad, f'NOT detected: {out}')
                if bad and not EXISTING:
                    c.check(f'[{fname} ' in full or '[parity]' in full, f'rejected, but not for this file: {out}')

report()
