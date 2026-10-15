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
    `put_publish` = what a PUT's own publish of an active workflow does: ok | refuse (400, the new version SAVED) |
    skip (200, the new version saved, the served one unchanged — an n8n whose PUT does not publish)."""

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
                if self.put_publish == 'ok':
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
# D1b (2026-10-11, the D1 follow-up): after the PUT of an ACTIVE workflow the tool reads back whether the PUT already
# published the saved version (n8n 2.36 does) and only when it did not publishes THAT version in place —
# POST /activate {versionId}, never deactivate → activate (a window without the webhook; on a failed activate the
# workflow stayed off) — then confirms by reading back (active, activeVersionId == versionId, published graph = canon).
DEACTIVATE = ('POST', f'/workflows/{SET_REPLY}/deactivate')
PUT_ = ('PUT', f'/workflows/{SET_REPLY}')

with Case('D1b ordinary DIFF, the PUT published (n8n 2.36): neither deactivate nor activate — PUT only, read-back ok, updated=1, exit 0') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n()
    fake.add(stale(w), active=True)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0 and counts(s, 'updated', 'republished', 'failed') == {'updated': 1, 'republished': 0, 'failed': 0}, f'exit {code} {s}\n{out[-800:]}')
    c.check(fake.writes() == [PUT_], f'the PUT published: nothing else may be written: {fake.writes()}')
    last_put = max(i for i, x in enumerate(fake.calls) if x[:2] == PUT_)
    c.check(any(m == 'GET' and p == f'/workflows/{SET_REPLY}' for m, p, _ in fake.calls[last_put + 1:]),
            f'what the PUT published must be READ back: {[x[:2] for x in fake.calls]}')
    active, graph = fake.served(SET_REPLY)
    c.check(active and h(fake.w[SET_REPLY]) == h(w) and h(graph) == h(w), 'stored and served are canonical')
    c.check('deactivate' not in out.lower() or 'no deactivate' in out.lower(), f'no deactivate window may be announced:\n{out[-600:]}')

with Case('D1b ordinary DIFF, the PUT saved but did NOT publish: only POST /activate {versionId: the saved version}, no deactivate; read-back ok, updated=1') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(put_publish='skip')
    fake.add(stale(w), active=True)
    old_av = fake.w[SET_REPLY]['activeVersionId']
    code, out, s = deploy(fake, *APPLY)
    saved = fake.w[SET_REPLY]['versionId']
    c.check(code == 0 and counts(s, 'updated', 'republished', 'failed') == {'updated': 1, 'republished': 0, 'failed': 0}, f'exit {code} {s}\n{out[-800:]}')
    c.check(fake.writes() == [PUT_, PUBLISH], f'PUT, then ONE in-place publish, never deactivate: {fake.writes()}')
    pubs = [b for m, p, b in fake.calls if (m, p) == PUBLISH]
    c.check(pubs == [{'versionId': saved}] and saved != old_av, f'the publish must name the version the PUT saved ({saved}): {pubs}')
    active, graph = fake.served(SET_REPLY)
    c.check(active and fake.w[SET_REPLY]['activeVersionId'] == saved and h(graph) == h(w), 'served = the saved canonical version')

with Case('D1b ids-only instance (no activeVersion graph): a PUT that published (activeVersionId == versionId) needs no publish') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(report_published_graph=False)
    fake.add(stale(w), active=True)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 0 and s.get('updated') == 1 and fake.writes() == [PUT_], f'exit {code} {s} writes={fake.writes()}\n{out[-600:]}')
    fake2 = FakeN8n(report_published_graph=False, put_publish='skip')    # and one that did not: published in place
    fake2.add(stale(w), active=True)
    code2, out2, s2 = deploy(fake2, *APPLY)
    c.check(code2 == 0 and s2.get('updated') == 1 and fake2.writes() == [PUT_, PUBLISH]
            and fake2.w[SET_REPLY]['activeVersionId'] == fake2.w[SET_REPLY]['versionId'], f'exit {code2} {s2} writes={fake2.writes()}')

with Case('D1b NEG the PUT did not publish and the in-place publish is REFUSED: failed=1, exit 1, the workflow stays ACTIVE on its old version (never deactivated)') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(put_publish='skip', publish='refuse')
    fake.add(stale(w), active=True)
    old_av = fake.w[SET_REPLY]['activeVersionId']
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 1 and counts(s, 'failed', 'updated') == {'failed': 1, 'updated': 0}, f'exit {code} {s}\n{out[-800:]}')
    c.check(DEACTIVATE not in fake.writes() and fake.writes() == [PUT_, PUBLISH], f'no deactivate, one publish attempt: {fake.writes()}')
    active, graph = fake.served(SET_REPLY)
    c.check(active and fake.w[SET_REPLY]['activeVersionId'] == old_av and h(graph) == h(stale(w)),
            'the old version keeps serving — the webhook never went offline')
    c.check('FAILED' in out and old_av in out.split('FAILED', 1)[1], f'the failure must name what is served now ({old_av}):\n{out[-800:]}')

with Case('D1b NEG the in-place publish answered 200 but nothing changed: the read-back fails the run (exit 1)') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(put_publish='skip', publish='noop')
    fake.add(stale(w), active=True)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 1 and s.get('failed') == 1 and s.get('updated', 0) == 0 and DEACTIVATE not in fake.writes(),
            f'exit {code} {s} writes={fake.writes()}\n{out[-600:]}')

with Case('D1b NEG the in-place publish was rolled back by n8n (trigger registration failed → INACTIVE): exit 1, the output names the outage') as c:
    w = bound(CANON[SET_REPLY])
    fake = FakeN8n(put_publish='skip', publish='rollback')
    fake.add(stale(w), active=True)
    code, out, s = deploy(fake, *APPLY)
    c.check(code == 1 and s.get('failed') == 1 and DEACTIVATE not in fake.writes(), f'exit {code} {s} writes={fake.writes()}')
    c.check('INACTIVE' in out, f'the output must say the workflow is now inactive:\n{out[-600:]}')

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

# ---------------------------------------------------------------------------------------------- staging (T12, 2026-10-12)
# The rehearsal of a deploy on staging (F1F2-plan T12) runs THIS tool against the staging n8n. Staging holds the DERIVED
# copies bootstrap.py imports (derive-staging.py: n8n self-calls → localhost, Wappi → the mock, Supabase writers
# neutralised, the price search on the staging stub, credentials re-bound BY NAME to staging's own). A local base must be
# compared with — and PUT — that same derivation: compared with the raw canon every derived workflow read as «would
# update» (15 of 25 on the real staging, while verify-deployed.py reported one DIFF), and --apply would have PUT the
# production URLs and the real Wappi host into the isolated stand. A production base never reads staging state.
STG_BASE = 'http://localhost:5678'
CREATE_WA, UPLOAD, PRICE = 'XuvOp7TxOImOAmlj', 'KoTuIlk4LMrlvnWI', 'crPriceSearch001'
DERIVE = module('update-clones.py')._derive_module()
STG_CREDS = {}
for _w in CANON.values():
    for _n in _w.get('nodes') or []:
        for _ctype, _ref in (_n.get('credentials') or {}).items():
            if isinstance(_ref, dict) and _ref.get('name'):
                STG_CREDS.setdefault(_ref['name'], (f'stg{len(STG_CREDS):04d}cred', _ctype))
STG_CREDS[DERIVE.PG_STAGING_NAME] = ('stgPgStaging0001', 'postgres')
STG_CREDS.pop('Support Bot', None)                      # left dangling on purpose by derive-staging.py (ALLOWED_MISSING)
STG_ADMIN = STG_CREDS['n8n Admin API'][0]


def staged(w):
    """What bootstrap.py imports for canonical `w`: derive-staging.py's own steps, in its order."""
    x = json.loads(DERIVE.derive_file(json.dumps(w, ensure_ascii=False)))
    DERIVE.isolate_supabase_writers(x)
    DERIVE.isolate_supabase_readers(x)
    missing = set()
    for n in x.get('nodes') or []:
        DERIVE.remap(n, STG_CREDS, missing)
    return x


def stg_deploy(fake, *args, only=(CREATE_WA,), creds=None):
    DERIVE.staging_credentials = lambda: dict(STG_CREDS if creds is None else creds)
    dc = module('deploy-canonical.py')
    dc.uc.call = fake.call
    dc.uc._derive_module = lambda: DERIVE
    dc.canonical_files = lambda: copy.deepcopy(CANON_FILES)
    argv = ['deploy-canonical.py', '--base', STG_BASE, *[x for o in only for x in ('--only', o)], *args]
    code, out = run_main(dc, argv)
    lines = [l for l in out.strip().splitlines() if l.startswith('{')]
    return code, out, (json.loads(lines[-1]) if lines else {})


PROD_HOSTS = ('n8n.choosereply.com', 'wappi.pro', 'api.revenuecat.com')   # + RevenueCat (A3 identity-heal, 2026-10-15)


def puts(fake):
    return [(p, b) for m, p, b in fake.calls if m == 'PUT']


with Case('STG premise: the derivation of the parsed canon equals the derivation of the file text bootstrap.py reads') as c:
    for wid, (path, w) in CANON_FILES.items():
        a = json.loads(DERIVE.derive_file(open(path, encoding='utf-8').read()))
        b = json.loads(DERIVE.derive_file(json.dumps(w, ensure_ascii=False)))
        c.check(a == b, f'{wid}: derive_file(file text) != derive_file(json.dumps)')
    c.check(any(h(staged(w)) != h(w) for w in CANON.values()), 'premise: the derivation changes the graph of some workflow')

with Case('STG a healthy staging (exactly what bootstrap.py imports): the dry-run is «up to date» for every workflow, nothing would be PUT') as c:
    fake = FakeN8n()
    for wid, w in CANON.items():
        fake.add(staged(w), active=wid not in (TEMPLATE_WA, '4VN3gsFaC2HUYmcc'))
    code, out, s = stg_deploy(fake, only=())
    c.check(code == 0 and s.get('considered', 0) > 20 and s.get('up_to_date') == s.get('considered')
            and s.get('would_update', 0) == 0 and s.get('would_publish', 0) == 0, f'exit {code} {s}\n{out[-1500:]}')
    c.check(fake.writes() == [], f'a dry-run never writes: {fake.writes()}')

with Case('STG --apply on staging PUTs the DERIVED canon: no production host, staging credentials bound, isolation kept, read-back ok') as c:
    fake = FakeN8n()
    for wid in (CREATE_WA, UPLOAD, PRICE):
        fake.add(stale(staged(CANON[wid])), active=True)
    for wid in (SWS, SUPPORT):
        fake.add(bound(CANON[wid]), active=True)
    code, out, s = stg_deploy(fake, *APPLY, only=(CREATE_WA, UPLOAD, PRICE))
    c.check(code == 0 and counts(s, 'updated', 'failed') == {'updated': 3, 'failed': 0}, f'exit {code} {s}\n{out[-1500:]}')
    sent = puts(fake)
    c.check(len(sent) == 3, f'one PUT per workflow: {[p for p, _ in sent]}')
    for p, body in sent:
        t = json.dumps(body, ensure_ascii=False)
        c.check(not any(x in t for x in PROD_HOSTS), f'{p}: a PUT to staging carries a production host')
        c.check('__HDR_CRED__' not in t, f'{p}: the admin placeholder must be bound to staging\'s credential')
        c.check(DERIVE.supabase_writers_left(body) == [], f'{p}: a PUT to staging would write to the real Supabase')
        pg = {(n.get('credentials') or {}).get('postgres', {}).get('id') for n in body['nodes'] if (n.get('credentials') or {}).get('postgres')}
        c.check(pg <= {'stgPgStaging0001'}, f'{p}: Postgres must be bound to «Postgres Staging» only: {pg}')
    create = next(b for p, b in sent if p.endswith(CREATE_WA))
    admin = {(n.get('credentials') or {}).get('httpHeaderAuth', {}).get('id') for n in create['nodes']
             if ((n.get('credentials') or {}).get('httpHeaderAuth') or {}).get('name') == 'n8n Admin API'}
    c.check(admin == {STG_ADMIN}, f'Create\'s n8n API calls must use staging\'s `n8n Admin API`: {admin}')
    for wid in (CREATE_WA, UPLOAD, PRICE):
        active, graph = fake.served(wid)
        c.check(active and h(graph) == h(staged(CANON[wid])), f'{wid}: staging must SERVE the derived canonical graph')

with Case('STG NEG a staging without «Postgres Staging» is refused before any request (a set bound to the shared DB is the failure mode)') as c:
    fake = FakeN8n()
    fake.add(stale(staged(CANON[CREATE_WA])), active=True)
    fake.add(bound(CANON[SWS]), active=True)          # the admin credential is readable: only the derivation may refuse
    no_pg = {k: v for k, v in STG_CREDS.items() if k != DERIVE.PG_STAGING_NAME}
    code, out, s = stg_deploy(fake, *APPLY, creds=no_pg)
    c.check(code not in (0, None) and fake.writes() == [], f'exit {code}, writes {fake.writes()}\n{out[-600:]}')

with Case('STG NEG a credential the canon names is missing on staging: refused, nothing written') as c:
    fake = FakeN8n()
    fake.add(stale(staged(CANON[CREATE_WA])), active=True)
    fake.add(bound(CANON[SWS]), active=True)
    no_admin = {k: v for k, v in STG_CREDS.items() if k != 'n8n Admin API'}
    code, out, s = stg_deploy(fake, *APPLY, creds=no_admin)
    c.check(code not in (0, None) and fake.writes() == [], f'exit {code}, writes {fake.writes()}\n{out[-600:]}')

with Case('STG NEG a production base never derives and never reads staging state: the raw (bound) canon is up to date there') as c:
    def boom():
        raise AssertionError('a production deploy read the staging credentials')
    DERIVE.staging_credentials = boom
    fake = FakeN8n()
    fake.add(bound(CANON[CREATE_WA]), active=True)
    code, out, s = deploy(fake, *APPLY, only=(CREATE_WA,))
    c.check(code == 0 and s.get('up_to_date') == 1 and fake.writes() == [], f'exit {code} {s}\n{out[-600:]}')


# ---------------------------------------------------------------------------------------------- A3 guard parity (2026-10-15)
# Verify Identity (crVerifyIdent001) calls RevenueCat's API with the «RevenueCat API» credential. derive-staging.py points
# that host at the mock and refuses a derived file that still names it; a deploy to a LOCAL base must refuse the same —
# ONE forbidden-host list (derive-staging.FORBIDDEN_HOSTS) for both tools.
VERIFY_IDENTITY = 'crVerifyIdent001'


def stg_apply_verify_identity():
    fake = FakeN8n()
    fake.add(stale(staged(CANON[VERIFY_IDENTITY])), active=True)
    fake.add(bound(CANON[SWS]), active=True)
    code, out, s = stg_deploy(fake, *APPLY, only=(VERIFY_IDENTITY,))
    return fake, code, out, s


with Case('STG A3 --apply of Verify Identity on staging: the PUT body calls the RevenueCat MOCK, never the real host, with '
          'staging\'s «RevenueCat API» credential; read-back ok (the positive control of the guard below)') as c:
    fake, code, out, s = stg_apply_verify_identity()
    sent = puts(fake)
    c.check(code == 0 and counts(s, 'updated', 'failed') == {'updated': 1, 'failed': 0} and len(sent) == 1, f'exit {code} {s}\n{out[-1200:]}')
    body = sent[0][1] if sent else {'nodes': []}
    t = json.dumps(body, ensure_ascii=False)
    c.check(not any(x in t for x in PROD_HOSTS), 'a PUT to staging carries a production host')
    c.check(DERIVE.MOCK + '/revenuecat/v2/projects/' in t, 'Verify Identity must call the RevenueCat mock on staging')
    rc_cred = {(n.get('credentials') or {}).get('httpHeaderAuth', {}).get('id') for n in body['nodes']
               if ((n.get('credentials') or {}).get('httpHeaderAuth') or {}).get('name') == 'RevenueCat API'}
    c.check(rc_cred == {STG_CREDS['RevenueCat API'][0]}, f'«RevenueCat API» must be bound to staging\'s credential: {rc_cred}')

with Case('STG NEG A3 guard parity: a staging derivation that leaves api.revenuecat.com is refused before any request, as '
          'derive-staging.py refuses it — one forbidden-host list for both tools') as c:
    c.check({DERIVE.CANON_HOST, DERIVE.WAPPI, 'wappi.pro', DERIVE.RC_API, 'api.revenuecat.com'} <= set(DERIVE.FORBIDDEN_HOSTS),
            f'derive-staging.FORBIDDEN_HOSTS must name every production host: {getattr(DERIVE, "FORBIDDEN_HOSTS", None)}')
    real = DERIVE.derive_file
    DERIVE.derive_file = lambda text: real(text).replace(DERIVE.MOCK + '/revenuecat/', DERIVE.RC_API + '/')
    try:
        fake, code, out, s = stg_apply_verify_identity()
    finally:
        DERIVE.derive_file = real
    c.check(code not in (0, None) and fake.writes() == [] and puts(fake) == [],
            f'a derivation that kept the real RevenueCat host was deployed to staging: exit {code}, writes {fake.writes()}\n{out[-600:]}')


report()
