#!/usr/bin/env python3
"""D2 (review 2026-09-25): verify-telegram-parity.py must describe the channel-limit refusal as it ships since the
2026-09-24 hotfix — If Slot Limit[over] -> Delete Refused Profile -> Retire Refused Profile -> Respond Channel Limit —
and must still DISTINGUISH a bypass of that refusal from the healthy set. A verifier that is red on the canonical
files distinguishes nothing (and, worse, went green on a full rollback of the hotfix).

What is executed:
  * the REAL verifier (subprocess, `--dir <temp copy>`) on the canonical set, on the pre-hotfix Create files from
    git, and on one-mutation-at-a-time copies of Create WA and Create TG (an oracle independent of the verifier's
    own `--self-test`, which is run as well);
  * the REAL `Count Channels` / `Retire Refused Profile` SQL from the canonical Create WA on session-temporary copies
    of bot_profiles/subscribers in the local staging Postgres (BEGIN … ROLLBACK) — proving the chain's rules are
    behaviour, not decoration: without the retire a refused profile keeps a slot; without its `workflow_id is null`
    guard a refusal frees a LIVE bot's slot (a second channel on a one-channel plan).
No n8n, no network, nothing written outside a temp dir.

    python3 Tools/n8n/tests/test_verify_parity.py
"""
import contextlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from harness import N8N, ROOT, WF, Case, bind, node, report, sql_json

PARITY = N8N / 'verify-telegram-parity.py'
CREATE = {'WA': ('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'WhatsappProfileId', 'TelegramProfileId'),
          'TG': ('Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'TelegramProfileId', 'WhatsappProfileId')}
HOTFIX = '94374500'   # fix(n8n): Count Channels … Retire Refused Profile — deployed 2026-09-24


def run_parity(*args):
    r = subprocess.run([sys.executable, str(PARITY), *args], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


@contextlib.contextmanager
def canon_copy():
    d = Path(tempfile.mkdtemp(prefix='parity-'))
    try:
        for p in WF.glob('*.json'):
            shutil.copy(p, d / p.name)
        yield d
    finally:
        shutil.rmtree(d)


def mutate(d, fname, fn, *extra):
    p = d / fname
    w = json.loads(p.read_text(encoding='utf-8'))
    fn(w, *extra)
    p.write_text(json.dumps(w, ensure_ascii=False, indent=2), encoding='utf-8')


def link(*targets):
    return {'main': [[{'node': t, 'type': 'main', 'index': 0} for t in targets]]}


def nd(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


# ---- one-mutation bypasses of the refusal (each is a way the chain can silently stop refusing / stop freeing) ----
def m_old_wiring(w, *_):                      # the pre-hotfix shape: no retire, Delete -> Respond
    w['nodes'] = [n for n in w['nodes'] if n['name'] != 'Retire Refused Profile']
    w['connections'].pop('Retire Refused Profile', None)
    w['connections']['Delete Refused Profile'] = link('Respond Channel Limit')


def m_retire_bypassed(w, *_):                 # retire still present, but nothing runs it
    w['connections']['Delete Refused Profile'] = link('Respond Channel Limit')
    w['connections'].pop('Retire Refused Profile', None)


def m_retire_into_creation(w, *_):            # the refusal continues into workflow creation
    w['connections']['Retire Refused Profile'] = link('Retire Same Bot Slot')


def m_fanout_into_creation(w, *_):
    w['connections']['Delete Refused Profile'] = link('Retire Refused Profile', 'Retire Same Bot Slot')


def m_over_limit_to_creation(w, *_):
    w['connections']['If Slot Limit']['main'][0] = [{'node': 'Retire Same Bot Slot', 'type': 'main', 'index': 0}]


def m_retire_disabled(w, *_):
    nd(w, 'Retire Refused Profile')['disabled'] = True


def m_retire_no_on_error(w, *_):              # a Postgres hiccup would abort before channel_limit is answered
    nd(w, 'Retire Refused Profile').pop('onError', None)


def m_retire_error_output_to_creation(w, *_):
    nd(w, 'Retire Refused Profile')['onError'] = 'continueErrorOutput'
    w['connections']['Retire Refused Profile']['main'].append([{'node': 'Retire Same Bot Slot', 'type': 'main', 'index': 0}])


def m_where_without_workflow_guard(w, *_):    # frees a LIVE bound bot's slot (see the SQL case below)
    p = nd(w, 'Retire Refused Profile')['parameters']
    p['query'] = p['query'].replace(' and workflow_id is null', '')


def m_where_by_owner(w, *_):
    nd(w, 'Retire Refused Profile')['parameters']['query'] = (
        "update bot_profiles\nset deleted_at = now(), deleted_reason = 'channel_limit'\n"
        "where app_user_id = (select app_user_id from bot_profiles where profile_id = nullif($1,'')) and deleted_at is null;")


def m_retire_on_allowed_branch(w, *_):
    w['connections']['Register Profile']['main'][0].append({'node': 'Retire Refused Profile', 'type': 'main', 'index': 0})


def m_retire_cte_select(w, *_):               # classified as a SELECT by n8n: zero rows -> NO item -> no response
    nd(w, 'Retire Refused Profile')['parameters']['query'] = (
        "with r as (update bot_profiles set deleted_at = now(), deleted_reason = 'channel_limit' "
        "where profile_id = nullif($1,'') and deleted_at is null and workflow_id is null returning 1) select * from r;")


def m_retire_other_channel_field(w, own, other):
    nd(w, 'Retire Refused Profile')['parameters']['options']['queryReplacement'] = \
        "={{ [ $('Unity Webhook').first().json.body.%s ] }}" % other


def m_respond_not_terminal(w, *_):
    w['connections']['Respond Channel Limit'] = link('Get Sample Workflow')


def m_respond_merges_input(w, *_):
    nd(w, 'Respond Channel Limit')['parameters']['includeOtherFields'] = True


MUTATIONS = [m_old_wiring, m_retire_bypassed, m_retire_into_creation, m_fanout_into_creation, m_over_limit_to_creation,
             m_retire_disabled, m_retire_no_on_error, m_retire_error_output_to_creation, m_where_without_workflow_guard,
             m_where_by_owner, m_retire_on_allowed_branch, m_retire_cte_select, m_retire_other_channel_field,
             m_respond_not_terminal, m_respond_merges_input]

# ---------------------------------------------------------------------------------------------- verifier cases
with Case('D2 the canonical set passes: exit 0, «ALL PARITY ASSERTS PASSED»') as c:
    rc, out = run_parity()
    c.check(rc == 0 and 'ALL PARITY ASSERTS PASSED' in out, f'exit {rc}:\n{out[-700:]}')

with Case('D2 one red check does not hide the rest: an early (Telegram bot) AND a late (Suggest Replies) defect are both reported') as c:
    with canon_copy() as d:
        mutate(d, '4VN3gsFaC2HUYmcc-Telegram_Bot.json',
               lambda w: nd(w, 'Mark Read')['parameters']['queryParameters']['parameters'].append({'name': 'mark_all', 'value': 'true'}))
        mutate(d, '9PTyYcelRQI7bGDb-Suggest_Replies.json',
               lambda w: w['connections']['If invalid?']['main'].__setitem__(1, [{'node': 'If skipRag?', 'type': 'main', 'index': 0}]))
        rc, out = run_parity('--dir', str(d))
    c.check(rc == 1, f'exit {rc}')
    c.check('Mark Read still has mark_all' in out, 'the early defect must be reported:\n' + out[-900:])
    c.check('valid branch does not enter the subscription gate' in out, 'the LATE defect must be reported too:\n' + out[-900:])

with Case('D2 --self-test (the verifier proves on mutated copies that it still catches a refusal bypass) exits 0') as c:
    rc, out = run_parity('--self-test')
    c.check(rc == 0, f'exit {rc}:\n{out[-1200:]}')

with Case(f'D2 a ROLLBACK of the hotfix (Create WA/TG from {HOTFIX}^, no Retire) is detected, exit 1') as c:
    with canon_copy() as d:
        for fname, _, _ in CREATE.values():
            text = subprocess.run(['git', '-C', str(ROOT), 'show', f'{HOTFIX}^:Tools/n8n/workflows/{fname}'],
                                  capture_output=True, text=True, check=True).stdout
            c.check('Retire Refused Profile' not in text, f'premise: {fname} before the hotfix has no retire')
            (d / fname).write_text(text, encoding='utf-8')
        rc, out = run_parity('--dir', str(d))
    c.check(rc == 1 and 'Retire Refused Profile' in out, f'exit {rc}:\n{out[-900:]}')

TG_BOT = '4VN3gsFaC2HUYmcc-Telegram_Bot.json'


def m_tool_filter_wa(w):                       # the Telegram tool searching under the WhatsApp key: another bot's chunks
    nd(w, 'Supabase Vector Store')['parameters']['workflowInputs']['value']['filterKey'] = 'botWaId'


def m_tool_unreconciled(w):                    # V9 rollback: n8n's retrieve-as-tool again (hits reach the model verbatim)
    t = nd(w, 'Supabase Vector Store')
    t['type'] = '@n8n/n8n-nodes-langchain.vectorStoreSupabase'
    t['parameters'] = {'mode': 'retrieve-as-tool', 'toolDescription': 'x', 'topK': 10, 'useReranker': False,
                       'options': {'metadata': {'metadataValues': [{'name': 'botTgId', 'value': '={{ $workflow.id }}'}]}}}


for fn, title in ((m_tool_filter_wa, 'the Telegram price-list tool filtering under botWaId'),
                  (m_tool_unreconciled, 'the Telegram price-list tool rolled back to retrieve-as-tool (no reconciliation)')):
    with Case(f'V9 NEG {title} is detected') as c:
        with canon_copy() as d:
            mutate(d, TG_BOT, fn)
            rc, out = run_parity('--dir', str(d))
        c.check(rc == 1 and ('crPriceSearch001' in out or 'retrieve filter' in out), f'exit {rc}:\n{out[-600:]}')

CONTROL_RC, CONTROL_OUT = run_parity()
for fn in MUTATIONS:
    for ch, (fname, own, other) in CREATE.items():
        with Case(f'D2 NEG {fn.__name__[2:]} [{ch}] is detected while the canonical control passes') as c:
            with canon_copy() as d:
                mutate(d, fname, fn, own, other)
                rc, out = run_parity('--dir', str(d))
            c.check(CONTROL_RC == 0, f'control (canonical) exit {CONTROL_RC} — a verifier red on everything detects nothing')
            fails = [l for l in out.splitlines() if l.startswith('PARITY FAIL') or l.startswith('FAIL')]
            c.check(rc == 1 and any(fname in l for l in fails), f'exit {rc}; failures: {fails[:3]}')

# ---------------------------------------------------------------------------------------------- SQL premises
W = json.loads((WF / CREATE['WA'][0]).read_text(encoding='utf-8'))
CC = node(W, 'Count Channels')['parameters']['query'].strip().rstrip(';')
RETIRE = node(W, 'Retire Refused Profile')['parameters']['query'].strip()
RETIRE_NO_GUARD = RETIRE.replace(' and workflow_id is null', '')
SETUP = ('create temp table bot_profiles (like public.bot_profiles including all);\n'
         'create temp table subscribers (like public.subscribers including all);\n'
         'create temp table obs (k text, v int);\n')


def count_channels(key, profile, bot_key):
    """Observe Count Channels' `c` for a Create of `profile` (the real query, bound like the node binds it)."""
    return f"insert into obs select '{key}', c from ({bind(CC, ['u1', 'whatsapp', bot_key, profile])}) x;\n"


def row(pid, bot_key=None, workflow=None):
    return ("insert into bot_profiles (profile_id, app_user_id, channel, bot_key, owner_id, workflow_id) values "
            f"('{pid}', 'u1', 'whatsapp', {repr(bot_key) if bot_key else 'null'}, 'inst1', {repr(workflow) if workflow else 'null'});\n")


def ghost_scenario(retire_sql):
    """Plan «start» (1 channel). Bot 1 live. A 2nd channel is refused (its row exists since the proxy's Bind Profile).
    Then the owner deletes bot 1 in the app — the slot is free, and a new channel must be accepted."""
    body = ("insert into subscribers (app_user_id, plan, status) values ('u1', 'start', 'active');\n"
            + row('p1', 'Bot1', 'wf1') + row('p2') + count_channels('p2', 'p2', 'Bot2')
            + (bind(retire_sql, ['p2']) + '\n' if retire_sql else '')
            + "update bot_profiles set deleted_at = now(), deleted_reason = 'app_delete' where profile_id = 'p1';\n"
            + row('p3') + count_channels('p3', 'p3', 'Bot3') + "select json_object_agg(k, v) from obs;")
    return sql_json(body, SETUP)


def guard_scenario(retire_sql):
    """An EXPIRED owner (limit 0) re-sends Create for bot 1's live, bound profile p1: refused. The Wappi delete fails
    (the node is best-effort), so bot 1 keeps running. The owner renews «start» (1 channel) and adds a new bot."""
    body = ("insert into subscribers (app_user_id, plan, status) values ('u1', 'start', 'expired');\n"
            + row('p1', 'Bot1', 'wf1') + count_channels('p1', 'p1', 'Bot1')
            + bind(retire_sql, ['p1']) + '\n'
            + "update subscribers set status = 'active' where app_user_id = 'u1';\n"
            + row('p5') + count_channels('p5', 'p5', 'Bot5') + "select json_object_agg(k, v) from obs;")
    return sql_json(body, SETUP)


with Case('D2 SQL: the canonical retire frees the refused profile\'s slot; the pre-hotfix chain (no retire) leaves a ghost') as c:
    fixed, old = ghost_scenario(RETIRE), ghost_scenario(None)
    c.check(fixed == {'p2': 1, 'p3': 0}, f'with Retire: p2 refused at c=1, p3 accepted at c=0 — got {fixed}')
    c.check(old == {'p2': 1, 'p3': 1}, f'without Retire the refused p2 keeps counting: a legit channel is refused — got {old}')

with Case('D2 SQL: `workflow_id is null` is what keeps a refusal from freeing a LIVE bot\'s slot') as c:
    fixed, bypass = guard_scenario(RETIRE), guard_scenario(RETIRE_NO_GUARD)
    c.check(fixed == {'p1': 0, 'p5': 1}, f'canonical: bot 1 still counts, p5 refused on a 1-channel plan — got {fixed}')
    c.check(bypass == {'p1': 0, 'p5': 0}, f'without the guard bot 1\'s slot is freed and p5 accepted = 2 live channels — got {bypass}')

report()
