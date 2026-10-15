#!/usr/bin/env python3
"""A4 «защита платящих» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§2.2, §3 A4): the hourly orphan sweep (Delete Orphan Profiles, 2islisFH7jjLoPQM) must never delete a Wappi profile that
has a LIVE bot_profiles row — whatever the owner's subscription status.

Why: the sweep deleted every profile it saw unauthorised for 24 h without looking at bot_profiles. A paying owner whose
WhatsApp logged out (a re-paired phone — an ordinary event) lost the profile; the row stayed live (the liveness branch of
the lifecycle sweep skips active owners), the token kept listing it, the card said «connected» — and the bot was silent.

Contract (the workflow has no generator — a hand edit, pinned by Tools/n8n/verify-orphan-sweep.py):
  * a Postgres node `Live Profiles` (`select profile_id from bot_profiles where deleted_at is null`, the canonical Postgres
    credential, alwaysOutputData + continue on error) sits between List TG Profiles and Plan Sweep;
  * Plan Sweep excludes every live id from the candidates (stats.protectedLive), the first-seen ledger unchanged;
  * fail-safe: only rows that EACH carry a profile id prove the live set — no row at all (an empty answer), an error item
    (the node continues on error), the input passed on by a failed node, a row without a usable id → every profile is
    protected and NOTHING is deleted this run.

What runs here — no n8n, no network:
  A  the REAL Plan Sweep code of the canonical workflow (harness.run_js; `$getWorkflowStaticData` stands in for n8n's
     static data) over fixture lists: a live id is never a candidate, a dead one past the TTL is, the ledger keeps
     tracking a protected profile, dry run lists only the dead ones;
  B  the fail-safe over every shape a failed or empty read can take;
  C  the REAL `Live Profiles` statement on session-temp tables (only live rows);
  D  verify-orphan-sweep.py accepts the canon and rejects each of its mutations; every mutation that changes what Plan
     Sweep decides is ALSO caught here in behaviour (EXPOSED_BY) — a tripwire the behaviour cannot see is unproven.

    python3 Tools/n8n/tests/test_orphan_sweep.py
"""
import copy
import json
import os
import subprocess
import sys
import tempfile
import time

from harness import N8N, Case, module, node, report, run_js, sql_run, wf

FILE = '2islisFH7jjLoPQM-Delete_Orphan_Profiles.json'
SWEEP = 'fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json'
W = wf(FILE)
VERIFIER = N8N / 'verify-orphan-sweep.py'

NOW = int(time.time() * 1000)
H = 3600 * 1000
CFG = {'ttlHours': 24, 'dryRun': False}
WA = {'status': 'done', 'profiles': [
    {'profile_id': 'wa-live-old', 'name': 'a paying owner, logged out 30 h ago', 'authorized': False},
    {'profile_id': 'wa-dead-old', 'name': 'an orphan (no live row)', 'authorized': False},
    {'profile_id': 'wa-dead-new', 'name': 'an orphan tracked 1 h', 'authorized': False},
    {'profile_id': 'wa-auth', 'name': 'authorised', 'authorized': True},
    {'profile_id': 'wa-ambig', 'name': 'ambiguous flag', 'authorized': None},
]}
TG = {'status': 'done', 'profiles': [
    {'profile_id': 'tg-live-old', 'name': 'a trialing owner, logged out 2 days ago', 'authorized': False},
    {'profile_id': 'tg-dead-old', 'name': 'an orphan (no live row)', 'authorized': False},
]}
FIRST_SEEN = {'whatsapp:wa-live-old': NOW - 30 * H, 'whatsapp:wa-dead-old': NOW - 30 * H, 'whatsapp:wa-dead-new': NOW - 1 * H,
              'telegram:tg-live-old': NOW - 48 * H, 'telegram:tg-dead-old': NOW - 25 * H}
LIVE = [{'profile_id': 'wa-live-old'}, {'profile_id': 'tg-live-old'}, {'profile_id': 'some-other-bot'}]
DEAD = {'wa-dead-old', 'tg-dead-old'}                      # what the sweep may delete with a proven live set
PAST_TTL = DEAD | {'wa-live-old', 'tg-live-old'}           # every profile past the TTL (what a fail-safe protects)


def plan(graph, live, cfg=None, first_seen=None, wa=None, tg=None):
    """Plan Sweep of `graph` (its REAL code) with `live` as its input items (what Live Profiles emitted).
    Returns (stats item, static data after the run)."""
    js = node(graph, 'Plan Sweep')['parameters']['jsCode']
    wrapped = ('const __sd = ' + json.dumps({'firstSeen': dict(FIRST_SEEN if first_seen is None else first_seen)}) + ';\n'
               'const $getWorkflowStaticData = () => __sd;\n'
               'const __r = (() => {\n' + js + '\n})();\n'
               'return [{ json: { out: __r, staticData: __sd } }];')
    r = run_js(wrapped, items=[{'json': x} for x in live],
               named={'Sweep Config': cfg or CFG, 'List WA Profiles': wa or WA, 'List TG Profiles': tg or TG})
    res = r[0]['json']
    out = res['out']
    return (out[0]['json'] if isinstance(out, list) else out), res['staticData']


def ids(stats, key='candidates'):
    return {c['profileId'] for c in stats.get(key) or []}


def protected_live(stats):
    return stats.get('protectedLive')


# ------------------------------------------------------------------------------------------------------------------
# A: the live set protects; the dead are still swept
with Case('A4 A: a profile with a live bot_profiles row is never a candidate; a dead one past the TTL is (both bases)') as c:
    s, sd = plan(W, LIVE)
    c.check(ids(s) == DEAD, f'candidates {sorted(ids(s))} != {sorted(DEAD)}')
    c.check(protected_live(s) == 2, f'stats.protectedLive = {protected_live(s)!r}, expected 2 (wa-live-old, tg-live-old)')
    c.check(s.get('candidateCount') == 2, f'candidateCount {s.get("candidateCount")}')
    plats = s.get('platforms') or {}
    c.check((plats.get('whatsapp') or {}).get('protectedLive') == 1 and (plats.get('telegram') or {}).get('protectedLive') == 1,
            f'per-platform protectedLive {plats}')
    for cand in s.get('candidates') or []:
        c.check(cand['deleteUrl'].endswith('/profile/delete?profile_id=' + cand['profileId']), f'delete url {cand}')

with Case('A4 A: the first-seen ledger is unchanged in meaning — a protected profile keeps being tracked (its clock is not reset)') as c:
    s, sd = plan(W, LIVE)
    fs = sd.get('firstSeen') or {}
    c.check(fs.get('whatsapp:wa-live-old') == FIRST_SEEN['whatsapp:wa-live-old'], f'wa-live-old ledger {fs.get("whatsapp:wa-live-old")}')
    c.check(fs.get('telegram:tg-live-old') == FIRST_SEEN['telegram:tg-live-old'], f'tg-live-old ledger {fs.get("telegram:tg-live-old")}')
    c.check('whatsapp:wa-auth' not in fs, 'an authorised profile must leave the ledger')
    c.check(s.get('ledgerSize') == len(fs), f'ledgerSize {s.get("ledgerSize")} vs {len(fs)}')

with Case('A4 A: dry run reports only the dead ones as wouldDelete — a protected profile is never «would delete»') as c:
    s, _ = plan(W, LIVE, cfg={'ttlHours': 24, 'dryRun': True})
    c.check(ids(s) == set(), f'dry run must emit no candidates: {sorted(ids(s))}')
    c.check(ids(s, 'wouldDelete') == DEAD, f'wouldDelete {sorted(ids(s, "wouldDelete"))} != {sorted(DEAD)}')
    c.check(protected_live(s) == 2, f'protectedLive {protected_live(s)}')

with Case('A4 A: the guards that were there stay — under the TTL, authorised and an ambiguous flag are never candidates') as c:
    s, _ = plan(W, LIVE)
    c.check(not ({'wa-dead-new', 'wa-auth', 'wa-ambig'} & ids(s)), f'candidates {sorted(ids(s))}')
    c.check(any(x.get('profileId') == 'wa-ambig' for x in s.get('skipped') or []), 'the ambiguous flag must be reported as skipped')

with Case('A4 A: a live row protects on BOTH bases by its id alone (a WhatsApp id listed live protects even if the list is the Telegram one)') as c:
    s, _ = plan(W, [{'profile_id': 'tg-dead-old'}, {'profile_id': 'wa-dead-old'}])
    c.check(ids(s) == {'wa-live-old', 'tg-live-old'}, f'candidates {sorted(ids(s))}')
    c.check(protected_live(s) == 2, f'protectedLive {protected_live(s)}')

# ------------------------------------------------------------------------------------------------------------------
# B: the fail-safe — only rows that each carry a profile id prove the live set
ERROR_ITEM = {'message': 'Connection terminated unexpectedly', 'error': {'message': 'Connection terminated unexpectedly'}}
UNPROVEN = {
    'an empty answer (no row: alwaysOutputData emits {})': [{}],
    'no item at all': [],
    'an error item (the node continues on error)': [ERROR_ITEM],
    'the input passed on by a node that threw (List TG Profiles\' answer)': [TG],
    'rows AND an error item': [{'profile_id': 'wa-live-old'}, ERROR_ITEM],
    'a row whose id is a number': [{'profile_id': 123}],
    'a row whose id is empty': [{'profile_id': ''}],
    'a row whose id is null': [{'profile_id': None}],
    'a row without an id': [{'channel': 'whatsapp'}],
    'a row whose id is only spaces': [{'profile_id': '   '}],
}
for label, live in UNPROVEN.items():
    with Case(f'A4 B fail-safe [{label}]: nothing is deleted, every profile past the TTL is counted protected') as c:
        s, sd = plan(W, live)
        c.check(ids(s) == set(), f'candidates {sorted(ids(s))} — an unproven live set must protect every profile')
        c.check(protected_live(s) == len(PAST_TTL), f'protectedLive {protected_live(s)!r}, expected {len(PAST_TTL)}')
        c.check(str(s.get('liveRead', '')).startswith('unproven'), f'liveRead {s.get("liveRead")!r} must say the live set is unproven')
        c.check((sd.get('firstSeen') or {}).get('whatsapp:wa-dead-old') == FIRST_SEEN['whatsapp:wa-dead-old'],
                'the ledger still advances normally on a fail-safe run')

with Case('A4 B: a proven live set says so (liveRead ok, the number of live rows)') as c:
    s, _ = plan(W, LIVE)
    c.check(s.get('liveRead') == 'ok' and s.get('liveRows') == len(LIVE), f'liveRead {s.get("liveRead")!r} liveRows {s.get("liveRows")!r}')

# ------------------------------------------------------------------------------------------------------------------
# C: the statement on real SQL — only live rows
with Case('A4 C: Live Profiles returns exactly the live bot_profiles ids (retired rows, any reason, are not live)') as c:
    q = node(W, 'Live Profiles')['parameters']['query'].strip().rstrip(';')
    setup = ("create temp table bot_profiles (like public.bot_profiles including defaults);\n"
             "insert into bot_profiles (profile_id, app_user_id, channel, owner_id, deleted_at, deleted_reason) values\n"
             " ('p-live-active', 'u1', 'whatsapp', 'inst-1', null, null),\n"
             " ('p-live-trial', 'u2', 'telegram', 'inst-2', null, null),\n"
             " ('p-live-ownerless', 'u3', 'whatsapp', null, null, null),\n"
             " ('p-retired-app', 'u1', 'whatsapp', 'inst-1', now(), 'app_delete'),\n"
             " ('p-retired-trial', 'u2', 'telegram', 'inst-2', now(), 'trial_expiry'),\n"
             " ('p-retired-liveness', 'u4', 'whatsapp', 'inst-4', now(), 'liveness');\n")
    out = sql_run(f"select coalesce(json_agg(profile_id order by profile_id), '[]'::json) from ({q}) x;", setup)
    got = json.loads([l for l in out.splitlines() if l.strip()][-1])
    c.check(got == ['p-live-active', 'p-live-ownerless', 'p-live-trial'], f'live ids {got}')

# ------------------------------------------------------------------------------------------------------------------
# D: the verifier holds the hand edit; each mutation it knows is rejected, and the behavioural ones are seen here too
def run_verifier(graph):
    """verify-orphan-sweep.py --dir <a copy of the canon with `graph` as the orphan sweep> → (exit code, output)."""
    with tempfile.TemporaryDirectory() as d:
        for f in os.listdir(N8N / 'workflows'):
            if f.endswith('.json'):
                with open(N8N / 'workflows' / f, encoding='utf-8') as src, open(os.path.join(d, f), 'w', encoding='utf-8') as dst:
                    dst.write(src.read())
        with open(os.path.join(d, FILE), 'w', encoding='utf-8') as fh:
            json.dump(graph, fh, indent=2, ensure_ascii=False)
        r = subprocess.run([sys.executable, str(VERIFIER), '--dir', d], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


EXPOSED_BY = {   # verifier mutation label -> the live set under which the mutated Plan Sweep deletes what it must not
    'Plan Sweep: the live-row exclusion removed': LIVE,
    'Plan Sweep: the fail-safe removed (an unproven read protects nothing)': [ERROR_ITEM],
    'Plan Sweep: an empty answer trusted as «no live rows»': [{}],
}

vos, vos_error = None, None
try:
    vos = module('verify-orphan-sweep.py')
except Exception as e:      # an import failure is a failure of THESE cases, not a crash of the file
    vos_error = e

with Case('A4 D: verify-orphan-sweep accepts the canonical orphan sweep') as c:
    c.check(VERIFIER.exists(), f'{VERIFIER} is missing (the hand edit is not pinned)')
    if VERIFIER.exists():
        rc, out = run_verifier(W)
        c.check(rc == 0, f'canonical rejected: {out[-800:]}')

with Case('A4 D: verify-orphan-sweep knows its mutations (incl. a disabled Live Profiles) and the behaviour cases name only real ones') as c:
    muts = getattr(vos, 'MUTATIONS', None) if vos else None
    c.check(bool(muts), f'verify-orphan-sweep.py has no MUTATIONS (import error: {vos_error!r})')
    if muts:
        labels = [m[0] for m in muts]
        c.check(any('disabled' in l for l in labels), f'no disabled-node mutation among {labels}')
        c.check(set(EXPOSED_BY) <= set(labels), f'EXPOSED_BY names unknown mutations: {sorted(set(EXPOSED_BY) - set(labels))}')

if vos and getattr(vos, 'MUTATIONS', None):
    for label, mutate, expect in vos.MUTATIONS:
        with Case(f'A4 D NEG [{label}]: the verifier rejects it, naming the broken rule') as c:
            bad = copy.deepcopy(W)
            mutate(bad)
            c.check(bad != W, 'the mutation did not apply')
            rc, out = run_verifier(bad)
            c.check(rc == 1, f'exit {rc} (expected 1): {out[-600:]}')
            c.check(expect in out, f'the failure does not name {expect!r}: {out[-600:]}')
            live = EXPOSED_BY.get(label)
            if live is not None:
                s, _ = plan(bad, live)
                c.check(bool(ids(s) & {'wa-live-old', 'tg-live-old'}),
                        f'the mutated Plan Sweep did not show the defect in behaviour (no live profile became a candidate): '
                        f'candidates {sorted(ids(s))}')

report()
