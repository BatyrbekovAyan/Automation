#!/usr/bin/env python3
"""D1 (independent review 2026-10-11, docs/audits/2026-10-11-independent-review.md §D1): a re-run of
deploy-canonical.py must REPAIR an active workflow whose STORED version is canonical while the version n8n SERVES
(`activeVersion`) is an older one, and confirm the repair by reading the instance back. That is the state n8n 2.36
leaves behind when a new version was saved but its publication failed: `WorkflowService.update` saves the version
FIRST and publishes it second (`activateWorkflow(versionId)`), so a refused publish answers 400 with the save
already persisted. Until D1 the tool compared only the stored graph, printed «up to date» and exited 0.

OFFLINE by construction: the instance is an in-memory model of the n8n 2.36.7 public API — GET /workflows/{id},
PUT (publishIfActive = true), POST /workflows/{id}/activate {versionId?}, POST /workflows/{id}/deactivate — with the
semantics of WorkflowService.update / activateWorkflow / deactivateWorkflow / _addToActiveWorkflowManager as read
from the installed n8n 2.36.7 (activate validates BEFORE it touches the live version; a trigger-registration failure
rolls the workflow back to INACTIVE). It is monkeypatched into the REAL tool's `uc.call`; any other request raises.
No network, no API key, no staging n8n.

    python3 Tools/n8n/tests/test_deploy_canonical.py
"""
import contextlib
import copy
import io
import json
import sys

from harness import Case, module, report

BASE = 'https://prod.fake.invalid'   # never contacted: every request goes to FakeN8n.call
SET_REPLY = 'SCLcpn6DMDG3Z4VN'       # an active canonical webhook (the review's fixture)
TEMPLATE_WA = '4wYitz5ek30SVNlT'     # a bot template: canonical, INACTIVE by contract
TEST_DIALOG = 'crTestDialog0001'     # carries the `n8n Admin API` credential placeholder
SUPPORT, SWS = 'olmqWMfcFpCEcpiE', '0DP79STI3pafXO9B'
BIND = {'__HDR_CRED__': 'HdrAdminCred0001', '__TG_CRED__': 'TgSupportCred001', '__CHAT_ID__': '-1001234567890'}
STICKY = 'n8n-nodes-base.stickyNote'

VD = module('verify-deployed.py')
# ONE snapshot of the canon, read by the tool's own canonical_files(), shared by the fake instance and every run of the
# tool: other sessions may regenerate canonical JSONs while this suite runs, and a fake built from one read and a tool
# run against a later one would report that edit as drift.
CANON_FILES = module('deploy-canonical.py').canonical_files()
CANON = {wid: w for wid, (_, w) in CANON_FILES.items()}


def bound(w):
    """What prod holds: the deploy-time placeholders bound (credential ids + the support chat id)."""
    t = json.dumps(w, ensure_ascii=False)
    for k, v in BIND.items():
        t = t.replace(k, v)
    return json.loads(t)


def stale(w):
    """An OLDER version of the workflow: its first real node switched off (the review's fixture)."""
    x = copy.deepcopy(w)
    next(n for n in x['nodes'] if n.get('type') != STICKY)['disabled'] = True
    return x


def h(w):
    return VD.graph_hash(w)


# ---------------------------------------------------------------------------------------------- fake n8n 2.36.7
class FakeN8n:
    """`publish` = what POST /activate does: ok | refuse (400 from validation, nothing touched) | rollback (400 after
    the live version was unregistered: n8n rolls the workflow back to INACTIVE) | noop (200, served version unchanged).
    `put_publish` = what a PUT's own publish of an active workflow does: ok | refuse (400, the new version SAVED)."""

    def __init__(self, publish='ok', put_publish='ok', report_published_graph=True):
        self.w, self.versions, self.calls, self.n = {}, {}, [], 0
        self.publish, self.put_publish, self.report_published_graph = publish, put_publish, report_published_graph

    def _save(self, wid, wf):
        self.n += 1
        vid = f'{wid[:6]}-v{self.n}'
        self.versions[(wid, vid)] = {'nodes': copy.deepcopy(wf['nodes']), 'connections': copy.deepcopy(wf.get('connections') or {})}
        return vid

    def add(self, wf, active, published=None):
        """`wf` = the stored (draft) version; an active workflow SERVES `published` (default: the stored one)."""
        wid = wf['id']
        av = self._save(wid, published) if (active and published is not None) else None
        vid = self._save(wid, wf)
        self.w[wid] = {'id': wid, 'name': wf['name'], 'nodes': copy.deepcopy(wf['nodes']),
                       'connections': copy.deepcopy(wf.get('connections') or {}), 'settings': copy.deepcopy(wf.get('settings') or {}),
                       'isArchived': False, 'versionId': vid, 'activeVersionId': (av or vid) if active else None}

    def view(self, wid):
        e = copy.deepcopy(self.w[wid])
        av = e['activeVersionId']
        e['active'] = av is not None
        e['activeVersion'] = ({'versionId': av, **copy.deepcopy(self.versions[(wid, av)])}
                              if av and self.report_published_graph else None)
        return e

    def served(self, wid):
        """(active, the graph n8n serves or None)."""
        av = self.w[wid]['activeVersionId']
        return (av is not None), (self.versions[(wid, av)] if av else None)

    def writes(self):
        return [(m, p) for m, p, _ in self.calls if m != 'GET']

    def call(self, base, method, path, body=None):
        p = path.split('?')[0]
        self.calls.append((method, p, copy.deepcopy(body)))
        parts = p.strip('/').split('/')
        if parts[0] != 'workflows' or len(parts) < 2:
            raise AssertionError(f'unexpected request {method} {path}')
        wid = parts[1]
        if wid not in self.w:
            return 404, {'message': 'Not Found'}
        e = self.w[wid]
        if method == 'GET' and len(parts) == 2:
            return 200, self.view(wid)
        if method == 'PUT' and len(parts) == 2:
            vid = self._save(wid, body)
            e.update(name=body['name'], nodes=copy.deepcopy(body['nodes']), connections=copy.deepcopy(body.get('connections') or {}),
                     settings=copy.deepcopy(body.get('settings') or {}), versionId=vid)
            if e['activeVersionId']:
                if self.put_publish == 'refuse':
                    return 400, {'message': 'There is a conflict with one of the webhooks (the update was saved)'}
                e['activeVersionId'] = vid
            return 200, self.view(wid)
        if method == 'POST' and parts[2:] == ['activate']:
            vid = (body or {}).get('versionId') or e['versionId']
            if (wid, vid) not in self.versions:
                return 404, {'message': 'Version not found'}
            if self.publish == 'refuse':
                return 400, {'message': 'There is a conflict with one of the webhooks'}
            if self.publish == 'rollback':
                e['activeVersionId'] = None
                return 400, {'message': 'Workflow could not be activated (rolled back)'}
            if self.publish == 'ok':
                e['activeVersionId'] = vid
            return 200, self.view(wid)
        if method == 'POST' and parts[2:] == ['deactivate']:
            e['activeVersionId'] = None
            return 200, self.view(wid)
        raise AssertionError(f'unexpected request {method} {path}')


# ---------------------------------------------------------------------------------------------- runner
def run_main(mod, argv):
    buf, old, code = io.StringIO(), sys.argv, 0
    sys.argv = argv
    try:
        with contextlib.redirect_stdout(buf):
            mod.main()
    except SystemExit as e:
        code = e.code if isinstance(e.code, int) else (0 if e.code is None else 1)
    finally:
        sys.argv = old
    return code, buf.getvalue()


def deploy(fake, *args, only=(SET_REPLY,)):
    dc = module('deploy-canonical.py')
    dc.uc.call = fake.call
    dc.canonical_files = lambda: copy.deepcopy(CANON_FILES)
    argv = ['deploy-canonical.py', '--base', BASE, *[x for o in only for x in ('--only', o)], *args]
    code, out = run_main(dc, argv)
    lines = [l for l in out.strip().splitlines() if l.startswith('{')]
    summary = json.loads(lines[-1]) if lines else {}
    return code, out, summary


def counts(s, *keys):
    """The summary's counters; a key the implementation does not report counts as 0 (HEAD before D1 had no
    `republished`/`would_publish` — it never published — so the ordinary-path controls read the same on both)."""
    return {k: s.get(k, 0) for k in keys}


APPLY = ('--apply', '--yes')
PUBLISH = ('POST', f'/workflows/{SET_REPLY}/activate')

# ---------------------------------------------------------------------------------------------- D1: the counterexample
with Case('D1 stored canonical, PUBLISHED version older: --apply publishes the stored version in place, read-back confirms (exit 0, republished=1)') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n()
    fake.add(w, active=True, published=stale(w))
    old_av, stored_vid = fake.w[SET_REPLY]['activeVersionId'], fake.w[SET_REPLY]['versionId']
    c.check(h(fake.served(SET_REPLY)[1]) != h(w), 'premise: the served graph is NOT the canonical one')
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0, f'exit {code}\n{out[-1200:]}')
    c.check(counts(s, 'republished', 'up_to_date', 'updated', 'failed') == {'republished': 1, 'up_to_date': 0, 'updated': 0, 'failed': 0}, s)
    pubs = [b for m, p, b in fake.calls if (m, p) == PUBLISH]
    c.check(pubs == [{'versionId': stored_vid}], f'exactly one publish, pinned to the STORED versionId {stored_vid}: {fake.calls}')
    c.check(fake.writes() == [PUBLISH], f'no PUT and no deactivate — the old version serves until the new one does: {fake.writes()}')
    last_pub = max((i for i, x in enumerate(fake.calls) if x[:2] == PUBLISH), default=-1)
    c.check(last_pub >= 0 and any(m == 'GET' and p == f'/workflows/{SET_REPLY}' for m, p, _ in fake.calls[last_pub + 1:]),
            f'the publish must be confirmed by READING the instance afterwards: {[x[:2] for x in fake.calls]}')
    active, graph = fake.served(SET_REPLY)
    c.check(active and fake.w[SET_REPLY]['activeVersionId'] == fake.w[SET_REPLY]['versionId'] == stored_vid and h(graph) == h(w),
            f'the instance must serve the canonical stored version: {fake.w[SET_REPLY]["activeVersionId"]} vs {stored_vid}')
    c.check(old_av in out, f'the rollback pointer (the previously published version {old_av}) must be printed:\n{out[-800:]}')
    c.check('read-back' in out and 'ok' in out.split('read-back', 1)[1][:200], f'the read-back verdict must be printed:\n{out[-800:]}')

with Case('D1 dry-run: the same state is «would publish» with the rollback pointer, nothing is written (exit 0)') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n()
    fake.add(w, active=True, published=stale(w))
    old_av, stored_vid = fake.w[SET_REPLY]['activeVersionId'], fake.w[SET_REPLY]['versionId']
    code, out, s = deploy(fake)
    c.check(code == 0, f'exit {code}\n{out[-800:]}')
    c.check(counts(s, 'would_publish', 'up_to_date', 'would_update') == {'would_publish': 1, 'up_to_date': 0, 'would_update': 0}, s)
    c.check(fake.writes() == [], f'a dry-run never writes: {fake.writes()}')
    c.check('would publish' in out and old_av in out and stored_vid in out,
            f'the plan must name the version it would publish ({stored_vid}) and the rollback pointer ({old_av}):\n{out[-800:]}')

with Case('D1 origin: a PUT whose own publish was refused (400, version saved) fails the run; the RE-RUN repairs it and confirms') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(put_publish='refuse')
    fake.add(stale(w), active=True)                 # the instance serves and stores the old graph: an ordinary DIFF
    code1, out1, s1 = deploy(fake, *APPLY)
    c.check(code1 == 1 and s1.get('failed') == 1, f'run 1 must fail honestly: exit {code1} {s1}\n{out1[-600:]}')
    active, graph = fake.served(SET_REPLY)
    c.check(h(fake.w[SET_REPLY]) == h(w) and active and h(graph) == h(stale(w)),
            'premise after run 1: stored = canonical (the PUT saved it), served = the old version')
    fake.put_publish, fake.calls = 'ok', []
    code2, out2, s2 = deploy(fake, *APPLY)
    c.check(code2 == 0 and s2.get('republished') == 1 and s2.get('up_to_date') == 0, f'run 2 must repair: exit {code2} {s2}\n{out2[-800:]}')
    c.check(fake.writes() == [PUBLISH], f'run 2 publishes only (no second PUT): {fake.writes()}')
    active, graph = fake.served(SET_REPLY)
    c.check(active and h(graph) == h(w), 'after run 2 the canonical graph is served')
    code3, out3, s3 = deploy(fake, *APPLY)
    c.check(code3 == 0 and s3.get('up_to_date') == 1 and s3.get('republished', 0) == 0 and fake.writes() == [PUBLISH],
            f'run 3 is a no-op (idempotent): exit {code3} {s3} writes={fake.writes()}')

with Case('D1 an instance that reports version ids only (no activeVersion graph): activeVersionId != versionId is republished, then confirmed by the ids') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(report_published_graph=False)
    fake.add(w, active=True, published=stale(w))
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0 and s.get('republished') == 1, f'exit {code} {s}\n{out[-800:]}')
    c.check(fake.w[SET_REPLY]['activeVersionId'] == fake.w[SET_REPLY]['versionId'], 'served = stored after the publish')

with Case('D1 an active workflow whose stored version still carries an unbound placeholder is NOT published (refused, exit 1)') as c:
    w = copy.deepcopy(CANON[TEST_DIALOG])           # the committed `__HDR_CRED__` left in place, as a forgotten bind would
    c.check('__HDR_CRED__' in json.dumps(w), 'premise: Test Dialog carries the admin credential placeholder')
    fake = FakeN8n()
    fake.add(w, active=True, published=stale(bound(w)))
    code, out, s = deploy(fake, *APPLY, only=(TEST_DIALOG,))
    c.check(code == 1 and s.get('failed') == 1 and s.get('republished', 0) == 0, f'exit {code} {s}\n{out[-800:]}')
    c.check(fake.writes() == [], f'nothing may be published or written: {fake.writes()}')
    c.check('__HDR_CRED__' in out, f'the refusal must name the placeholder:\n{out[-600:]}')
    fake2 = FakeN8n()                                # control: the same state with the credential bound IS published
    fake2.add(bound(w), active=True, published=stale(bound(w)))
    code2, out2, s2 = deploy(fake2, *APPLY, only=(TEST_DIALOG,))
    c.check(code2 == 0 and s2.get('republished') == 1, f'bound control: exit {code2} {s2}\n{out2[-600:]}')

# ---------------------------------------------------------------------------------------------- D1: honest failures
with Case('D1 NEG publish refused (400, n8n validated before switching): exit 1, failed=1, republished=0, the old served version is named') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(publish='refuse')
    fake.add(w, active=True, published=stale(w))
    old_av = fake.w[SET_REPLY]['activeVersionId']
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 1, f'exit {code}\n{out[-800:]}')
    c.check(counts(s, 'failed', 'republished', 'up_to_date') == {'failed': 1, 'republished': 0, 'up_to_date': 0}, s)
    c.check(fake.writes() == [PUBLISH], f'one publish attempt, no deactivate (the old version keeps serving): {fake.writes()}')
    c.check('failed 400' in out and old_av in out.split('failed 400', 1)[1],
            f'the failure and the version STILL served ({old_av}) must be printed after it:\n{out[-800:]}')
    c.check(fake.served(SET_REPLY)[0], 'premise: a refused publish leaves the workflow active on its old version')

with Case('D1 NEG publish answered 200 but the served version did not change: the read-back fails the run (exit 1)') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(publish='noop')
    fake.add(w, active=True, published=stale(w))
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 1 and s.get('failed') == 1 and s.get('republished', 0) == 0, f'exit {code} {s}\n{out[-800:]}')
    c.check('FAILED' in out, f'the read-back must say FAILED:\n{out[-600:]}')

with Case('D1 NEG publish rolled back by n8n (trigger registration failed → INACTIVE): exit 1, the read-back names the outage') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(publish='rollback')
    fake.add(w, active=True, published=stale(w))
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 1 and s.get('failed') == 1 and s.get('republished', 0) == 0, f'exit {code} {s}\n{out[-800:]}')
    c.check('INACTIVE' in out, f'the output must say the workflow is now inactive:\n{out[-600:]}')

# ---------------------------------------------------------------------------------------------- the ordinary paths
with Case('NEG ordinary DIFF: PUT, then (active) deactivate → activate, read-back ok — updated=1, exit 0, the path unchanged') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n()
    fake.add(stale(w), active=True)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0 and counts(s, 'updated', 'republished', 'failed') == {'updated': 1, 'republished': 0, 'failed': 0}, f'exit {code} {s}\n{out[-800:]}')
    c.check(fake.writes() == [('PUT', f'/workflows/{SET_REPLY}'), ('POST', f'/workflows/{SET_REPLY}/deactivate'), PUBLISH],
            f'the ordinary path: {fake.writes()}')
    active, graph = fake.served(SET_REPLY)
    c.check(active and h(fake.w[SET_REPLY]) == h(w) and h(graph) == h(w), 'stored and served are canonical')

with Case('NEG ordinary DIFF dry-run: would_update=1, would_publish=0, nothing written') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n()
    fake.add(stale(w), active=True)
    code, out, s = deploy(fake)
    c.check(code == 0 and counts(s, 'would_update', 'would_publish') == {'would_update': 1, 'would_publish': 0} and fake.writes() == [],
            f'exit {code} {s} writes={fake.writes()}')

with Case('NEG fully matching (stored = published = canonical): up_to_date=1, only GETs, exit 0') as c:
    fake = FakeN8n()
    fake.add(bound(CANON[SET_REPLY]), active=True)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0 and counts(s, 'up_to_date', 'republished', 'updated') == {'up_to_date': 1, 'republished': 0, 'updated': 0}, f'exit {code} {s}')
    c.check(fake.writes() == [], f'no writes: {fake.writes()}')

with Case('NEG inactive bot template with a canonical stored graph: up_to_date, never published or activated') as c:
    fake = FakeN8n()
    fake.add(bound(CANON[TEMPLATE_WA]), active=False)
    code, out, s = deploy(fake, *APPLY, only=(TEMPLATE_WA,))
    c.check(code == 0 and counts(s, 'up_to_date', 'republished', 'would_publish') == {'up_to_date': 1, 'republished': 0, 'would_publish': 0}, f'exit {code} {s}')
    c.check(fake.writes() == [] and not fake.served(TEMPLATE_WA)[0], f'the template stays inactive, untouched: {fake.writes()}')

with Case('NEG inactive bot template whose stored graph differs: PUT only, it stays inactive (no publish)') as c:
    w = bound(CANON[TEMPLATE_WA])
    fake = FakeN8n()
    fake.add(stale(w), active=False)
    code, out, s = deploy(fake, *APPLY, only=(TEMPLATE_WA,))
    c.check(code == 0 and s.get('updated') == 1 and fake.writes() == [('PUT', f'/workflows/{TEMPLATE_WA}')] and not fake.served(TEMPLATE_WA)[0],
            f'exit {code} {s} writes={fake.writes()}')

with Case('NEG a workflow that must run but is INACTIVE (canonical stored): reported, never activated, not counted up to date, exit 0') as c:
    fake = FakeN8n()
    fake.add(bound(CANON[SET_REPLY]), active=False)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0 and counts(s, 'inactive_must_run', 'up_to_date', 'republished') == {'inactive_must_run': 1, 'up_to_date': 0, 'republished': 0},
            f'exit {code} {s}\n{out[-600:]}')
    c.check(fake.writes() == [] and not fake.served(SET_REPLY)[0] and 'INACTIVE' in out, f'reported only: {fake.writes()}\n{out[-600:]}')

with Case('NEG whole healthy canonical set (placeholders bound, templates inactive): --apply writes NOTHING, every workflow up to date') as c:
    fake = FakeN8n()
    for wid, w in CANON.items():
        fake.add(bound(w), active=wid not in (TEMPLATE_WA, '4VN3gsFaC2HUYmcc'))
    code, out, s = deploy(fake, *APPLY, only=())
    c.check(code == 0 and s.get('considered', 0) > 20 and s.get('up_to_date') == s.get('considered')
            and s.get('republished', 0) == 0 and s.get('updated') == 0, f'exit {code} {s}')
    c.check(fake.writes() == [], f'no writes on a healthy instance: {fake.writes()[:5]}')
    c.check(not any(p.endswith((SUPPORT, SWS)) for _, p, _ in fake.calls), 'the two placeholder workflows stay out of scope (build-client-webhooks.py --update)')

report()
