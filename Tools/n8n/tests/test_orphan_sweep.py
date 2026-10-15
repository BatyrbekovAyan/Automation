#!/usr/bin/env python3
"""A4 «защита платящих» (2026-10-14; fix round 1 2026-10-15; spec
docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md §2.2, §3 A4): the hourly orphan sweep
(Delete Orphan Profiles, 2islisFH7jjLoPQM) must never delete a Wappi profile that Wappi ONCE AUTHORISED and that has a LIVE
bot_profiles row — whatever the owner's subscription status — and must keep sweeping every never-authorised profile.

Why: the sweep deleted every profile it saw unauthorised for 24 h without looking at bot_profiles. A paying owner whose
WhatsApp logged out (a re-paired phone — an ordinary event) lost the profile; the row stayed live (the liveness branch of
the lifecycle sweep skips active owners), the token kept listing it, the card said «connected» — and the bot was silent.
But since A1 the Wappi Proxy registers the row at profile/add, so an abandoned wizard's never-paired profile has a live row
too (and bills every day): «live row ⇒ protected» alone would end the sweep's own job (coordinator ruling, fix round 1).

Contract (the workflow has no generator — a hand edit, pinned by Tools/n8n/verify-orphan-sweep.py):
  * a Postgres node `Live Profiles` (`select profile_id from bot_profiles where deleted_at is null`, the canonical Postgres
    credential, alwaysOutputData + continue on error) sits between List TG Profiles and Plan Sweep;
  * Plan Sweep protects (stats.protectedLive) a profile with a live row AND «once authorised» — a parseable authorized_at OR
    logouted_at on its list entry; a never-authorised profile is a candidate after the TTL exactly as before, row or no row;
    the first-seen ledger unchanged;
  * the live read: rows that each carry a profile id = the live set; ONE empty item = a successful query with no live row —
    proof: nothing protected; anything else (an error item, the input passed on by a failed node, a row without a usable
    id, no item) = a blind run: every profile protected, NOTHING deleted.

What runs here — no n8n, no network:
  A  the REAL Plan Sweep code of the canonical workflow (harness.run_js; `$getWorkflowStaticData` stands in for n8n's
     static data) over fixture lists: a once-authorised live profile is never a candidate (either signal), a never-authorised
     one is (row or no row), an unparseable timestamp is no signal, the ledger keeps tracking a protected profile, dry run;
  B  the live read: every failed shape deletes nothing; an empty read is a normal run;
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


def iso(ms):
    return time.strftime('%Y-%m-%dT%H:%M:%S', time.gmtime(ms / 1000)) + '+00:00'


LOGIN = iso(NOW - 30 * 24 * H)          # authorised a month ago
LOGOUT_30H, LOGOUT_48H = iso(NOW - 30 * H), iso(NOW - 48 * H)   # logged out past the 24 h TTL
CFG = {'ttlHours': 24, 'dryRun': False}
WA = {'status': 'done', 'profiles': [
    {'profile_id': 'wa-paid-out', 'name': 'a paying owner, logged out 30 h ago', 'authorized': False,
     'authorized_at': LOGIN, 'logouted_at': LOGOUT_30H},
    {'profile_id': 'wa-wizard', 'name': 'an abandoned wizard: profile/add, never paired', 'authorized': False},
    {'profile_id': 'wa-garbage-ts', 'name': 'unparseable timestamps', 'authorized': False, 'authorized_at': 'garbage', 'logouted_at': ''},
    {'profile_id': 'wa-orphan', 'name': 'once authorised, no live row', 'authorized': False, 'authorized_at': LOGIN, 'logouted_at': LOGOUT_30H},
    {'profile_id': 'wa-dead-new', 'name': 'an orphan tracked 1 h', 'authorized': False},
    {'profile_id': 'wa-auth', 'name': 'authorised', 'authorized': True, 'authorized_at': LOGIN},
    {'profile_id': 'wa-ambig', 'name': 'ambiguous flag', 'authorized': None},
]}
TG = {'status': 'done', 'profiles': [
    {'profile_id': 'tg-paid-out', 'name': 'a trialing owner, logged out 2 days ago (no authorized_at on the entry)', 'authorized': False,
     'logouted_at': LOGOUT_48H},
    {'profile_id': 'tg-wizard', 'name': 'an abandoned wizard', 'authorized': False},
    {'profile_id': 'tg-orphan', 'name': 'never authorised, no live row', 'authorized': False},
]}
FIRST_SEEN = {'whatsapp:wa-paid-out': NOW - 30 * H, 'whatsapp:wa-wizard': NOW - 30 * H, 'whatsapp:wa-garbage-ts': NOW - 30 * H,
              'whatsapp:wa-orphan': NOW - 30 * H, 'whatsapp:wa-dead-new': NOW - 1 * H,
              'telegram:tg-paid-out': NOW - 48 * H, 'telegram:tg-wizard': NOW - 25 * H, 'telegram:tg-orphan': NOW - 25 * H}
LIVE = [{'profile_id': p} for p in ('wa-paid-out', 'wa-wizard', 'wa-garbage-ts', 'tg-paid-out', 'tg-wizard', 'some-other-bot')]
PROTECTED = {'wa-paid-out', 'tg-paid-out'}                                          # live row + once authorised
SWEPT = {'wa-wizard', 'wa-garbage-ts', 'wa-orphan', 'tg-wizard', 'tg-orphan'}       # what a proven live set lets go
PAST_TTL = PROTECTED | SWEPT                                                         # what a blind run protects


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
# A: a once-authorised live profile is protected; a never-authorised one is swept as before, row or no row
with Case('A4 A: a once-authorised profile with a live row is never a candidate; never-authorised ones (row or no row) and rowless ones are (both bases)') as c:
    s, sd = plan(W, LIVE)
    c.check(ids(s) == SWEPT, f'candidates {sorted(ids(s))} != {sorted(SWEPT)}')
    c.check(protected_live(s) == 2, f'stats.protectedLive = {protected_live(s)!r}, expected 2 (wa-paid-out, tg-paid-out)')
    c.check(s.get('candidateCount') == len(SWEPT), f'candidateCount {s.get("candidateCount")}')
    plats = s.get('platforms') or {}
    c.check((plats.get('whatsapp') or {}).get('protectedLive') == 1 and (plats.get('telegram') or {}).get('protectedLive') == 1,
            f'per-platform protectedLive {plats}')
    for cand in s.get('candidates') or []:
        c.check(cand['deleteUrl'].endswith('/profile/delete?profile_id=' + cand['profileId']), f'delete url {cand}')

with Case('A4 A: an abandoned wizard\'s profile (profile/add registered a live row, never paired) is swept after the TTL as before') as c:
    s, _ = plan(W, LIVE)
    c.check({'wa-wizard', 'tg-wizard'} <= ids(s), f'candidates {sorted(ids(s))} — a never-authorised profile must not be protected by its row')

ONCE = {   # list-entry timestamps -> once authorised?
    'authorized_at only': ({'authorized_at': LOGIN}, True),
    'logouted_at only (an entry that omits authorized_at)': ({'logouted_at': LOGOUT_30H}, True),
    'both': ({'authorized_at': LOGIN, 'logouted_at': LOGOUT_30H}, True),
    'neither (profile/add, never paired)': ({}, False),
    'both null': ({'authorized_at': None, 'logouted_at': None}, False),
    'unparseable strings': ({'authorized_at': 'garbage', 'logouted_at': 'not a date'}, False),
    'empty strings': ({'authorized_at': '', 'logouted_at': ''}, False),
    'numbers (not a timestamp string)': ({'authorized_at': 1700000000, 'logouted_at': 0}, False),
}
for label, (fields, once) in ONCE.items():
    with Case(f'A4 A «once authorised» [{label}]: a live, unauthorised profile past the TTL is {"protected" if once else "a candidate"}') as c:
        wa = {'status': 'done', 'profiles': [dict({'profile_id': 'wa-x', 'name': 'x', 'authorized': False}, **fields)]}
        s, _ = plan(W, [{'profile_id': 'wa-x'}], wa=wa, tg={'status': 'done', 'profiles': None}, first_seen={'whatsapp:wa-x': NOW - 30 * H})
        c.check((ids(s) == set()) if once else (ids(s) == {'wa-x'}), f'candidates {sorted(ids(s))}')
        c.check(protected_live(s) == (1 if once else 0), f'protectedLive {protected_live(s)}')

with Case('A4 A: the recently-active guard still comes first — a once-authorised live profile logged out 2 h ago is under the TTL, not «protected»') as c:
    wa = {'status': 'done', 'profiles': [{'profile_id': 'wa-x', 'name': 'x', 'authorized': False, 'authorized_at': LOGIN, 'logouted_at': iso(NOW - 2 * H)}]}
    s, _ = plan(W, [{'profile_id': 'wa-x'}], wa=wa, tg={'status': 'done', 'profiles': None}, first_seen={'whatsapp:wa-x': NOW - 30 * H})
    c.check(ids(s) == set() and protected_live(s) == 0 and (s['platforms']['whatsapp'] or {}).get('underTtl') == 1, f'stats {s.get("platforms")}')

with Case('A4 A: the first-seen ledger is unchanged in meaning — a protected profile keeps being tracked (its clock is not reset)') as c:
    s, sd = plan(W, LIVE)
    fs = sd.get('firstSeen') or {}
    c.check(fs.get('whatsapp:wa-paid-out') == FIRST_SEEN['whatsapp:wa-paid-out'], f'wa-paid-out ledger {fs.get("whatsapp:wa-paid-out")}')
    c.check(fs.get('telegram:tg-paid-out') == FIRST_SEEN['telegram:tg-paid-out'], f'tg-paid-out ledger {fs.get("telegram:tg-paid-out")}')
    c.check('whatsapp:wa-auth' not in fs, 'an authorised profile must leave the ledger')
    c.check(s.get('ledgerSize') == len(fs), f'ledgerSize {s.get("ledgerSize")} vs {len(fs)}')

with Case('A4 A: dry run reports only the sweepable ones as wouldDelete — a protected profile is never «would delete»') as c:
    s, _ = plan(W, LIVE, cfg={'ttlHours': 24, 'dryRun': True})
    c.check(ids(s) == set(), f'dry run must emit no candidates: {sorted(ids(s))}')
    c.check(ids(s, 'wouldDelete') == SWEPT, f'wouldDelete {sorted(ids(s, "wouldDelete"))} != {sorted(SWEPT)}')
    c.check(protected_live(s) == 2, f'protectedLive {protected_live(s)}')

with Case('A4 A: the guards that were there stay — under the TTL, authorised and an ambiguous flag are never candidates') as c:
    s, _ = plan(W, LIVE)
    c.check(not ({'wa-dead-new', 'wa-auth', 'wa-ambig'} & ids(s)), f'candidates {sorted(ids(s))}')
    c.check(any(x.get('profileId') == 'wa-ambig' for x in s.get('skipped') or []), 'the ambiguous flag must be reported as skipped')

# ------------------------------------------------------------------------------------------------------------------
# B: the live read — a failed read is a blind run (nothing deleted); an empty read is proof (a normal run)
ERROR_ITEM = {'message': 'Connection terminated unexpectedly', 'error': {'message': 'Connection terminated unexpectedly'}}
FAILED = {
    'an error item (the node continues on error)': [ERROR_ITEM],
    'the input passed on by a node that threw (List TG Profiles\' answer)': [TG],
    'rows AND an error item': [{'profile_id': 'wa-paid-out'}, ERROR_ITEM],
    'an empty item next to rows': [{'profile_id': 'wa-paid-out'}, {}],
    'two empty items': [{}, {}],
    'no item at all': [],
    'a row whose id is a number': [{'profile_id': 123}],
    'a row whose id is empty': [{'profile_id': ''}],
    'a row whose id is null': [{'profile_id': None}],
    'a row without an id': [{'channel': 'whatsapp'}],
    'a row whose id is only spaces': [{'profile_id': '   '}],
}
for label, live in FAILED.items():
    with Case(f'A4 B failed read [{label}]: a blind run — nothing is deleted, every profile past the TTL is counted protected') as c:
        s, sd = plan(W, live)
        c.check(ids(s) == set(), f'candidates {sorted(ids(s))} — a failed live read must protect every profile')
        c.check(protected_live(s) == len(PAST_TTL), f'protectedLive {protected_live(s)!r}, expected {len(PAST_TTL)}')
        c.check(str(s.get('liveRead', '')).startswith('unproven'), f'liveRead {s.get("liveRead")!r} must say the live read failed')
        c.check((sd.get('firstSeen') or {}).get('whatsapp:wa-wizard') == FIRST_SEEN['whatsapp:wa-wizard'],
                'the ledger still advances normally on a blind run')

with Case('A4 B empty read: ONE empty item (a successful query, no live row) is proof — nothing protected, a normal sweep') as c:
    s, _ = plan(W, [{}])
    c.check(ids(s) == PAST_TTL, f'candidates {sorted(ids(s))} != {sorted(PAST_TTL)} — an empty result is proof, not doubt')
    c.check(protected_live(s) == 0, f'protectedLive {protected_live(s)!r}')
    c.check(s.get('liveRead') == 'ok: no live row' and s.get('liveRows') == 0, f'liveRead {s.get("liveRead")!r} liveRows {s.get("liveRows")!r}')

with Case('A4 B: a proven live set says so (liveRead ok, the number of live ids)') as c:
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


EXPOSED_BY = {   # verifier mutation label -> (the live read, «the mutated Plan Sweep decided wrongly» on that read)
    'Plan Sweep: the protection removed': (LIVE, lambda s: bool(ids(s) & PROTECTED)),
    'Plan Sweep: the fail-safe removed (a failed read protects nothing)': ([ERROR_ITEM], lambda s: bool(ids(s))),
    'Plan Sweep: an empty read treated as a failure (no live row = nothing swept)': ([{}], lambda s: ids(s) != PAST_TTL),
    'Plan Sweep: the once-authorised term removed (every live row protected)': (LIVE, lambda s: not ({'wa-wizard', 'tg-wizard'} <= ids(s))),
    'Plan Sweep: the logouted_at signal dropped': (LIVE, lambda s: 'tg-paid-out' in ids(s)),
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
            seen = EXPOSED_BY.get(label)
            if seen is not None:
                live, wrong = seen
                s, _ = plan(bad, live)
                c.check(wrong(s), f'the mutated Plan Sweep did not show the defect in behaviour: candidates {sorted(ids(s))}')

report()
