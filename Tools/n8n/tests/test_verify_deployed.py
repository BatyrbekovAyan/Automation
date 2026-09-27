#!/usr/bin/env python3
"""D1 (review 2026-09-25): the post-deploy gate must judge the REAL graph — of every bot clone and of the
PUBLISHED version n8n actually serves — never a sticky label, and must not wave through drift behind a
blanket «SUBSTITUTED» exemption or an unexpectedly inactive webhook.

OFFLINE and READ-ONLY by construction: the n8n instance is an in-memory fake built from the committed
canonical exports (Tools/n8n/workflows/*.json) plus the prod facts saved in
docs/audits/2026-09-25-review-evidence/production-readback.json; `list_workflows`/`call` of the REAL
tools (verify-deployed.py, update-clones.py) are monkeypatched, and any non-GET call raises — so a test
that «passes» by writing to the instance is impossible. No network, no API key.

    python3 Tools/n8n/tests/test_verify_deployed.py
"""
import contextlib
import copy
import io
import json
import os
import subprocess
import sys
import tempfile
import uuid

from harness import ROOT, WF, Case, module, report

BASE = 'https://prod.fake.invalid'           # non-local → the prod code path (no staging derivation); never contacted
TEMPLATE_IDS = {'whatsapp': '4wYitz5ek30SVNlT', 'telegram': '4VN3gsFaC2HUYmcc'}
BIND = {'__HDR_CRED__': 'HdrAdminCred0001', '__TG_CRED__': 'TgSupportCred001', '__CHAT_ID__': '-1001234567890'}
READBACK = ROOT / 'docs' / 'audits' / '2026-09-25-review-evidence' / 'production-readback.json'
STICKY = 'n8n-nodes-base.stickyNote'
SWS, SUPPORT, PROXY, TEST_DIALOG, SET_REPLY = ('0DP79STI3pafXO9B', 'olmqWMfcFpCEcpiE', 'crWappiProxy0001',
                                              'crTestDialog0001', 'SCLcpn6DMDG3Z4VN')


# ---------------------------------------------------------------------------------------------- fake instance
def canon():
    out = {}
    for p in sorted(WF.glob('*.json')):
        w = json.loads(p.read_text(encoding='utf-8'))
        out[w['id']] = w
    return out


CANON = canon()


def real(w):
    return [n for n in w['nodes'] if n.get('type') != STICKY]


def nd(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def link(*targets):
    return {'main': [[{'node': t, 'type': 'main', 'index': 0} for t in targets]]}


def bound(w):
    """The deploy-time substitution build-client-webhooks.py --update performs (credential ids + recipient chat)."""
    t = json.dumps(w, ensure_ascii=False)
    for k, v in BIND.items():
        t = t.replace(k, v)
    return json.loads(t)


def deployed(w, active, archived=False, published=None):
    """What n8n 2.36's public GET /workflows/{id} returns: the stored (draft) graph plus, for an active workflow,
    `activeVersion` = the graph it SERVES. `published` (a workflow) = a published graph different from the draft."""
    x = copy.deepcopy(w)
    x['active'], x['isArchived'], x['versionId'] = active, archived, 'ver-draft'
    if active:
        pub = published if published is not None else x
        x['activeVersionId'] = 'ver-draft' if published is None else 'ver-published'
        x['activeVersion'] = {'versionId': x['activeVersionId'], 'nodes': copy.deepcopy(pub['nodes']),
                              'connections': copy.deepcopy(pub['connections'])}
    else:
        x['activeVersionId'], x['activeVersion'] = None, None
    return x


def healthy():
    """Prod on 2026-09-25 for the canonical set: every workflow at its export, placeholders bound, all active but
    the two clone templates (docs/audits/2026-09-24-prod-after.json)."""
    return {wid: deployed(bound(w), active=wid not in TEMPLATE_IDS.values()) for wid, w in CANON.items()}


def create_style_clone(channel, wid, name, profile, fresh_ids=False):
    """Exactly what Create WA/TG's `Set Fields` + POST produce: the template graph wearing a name, the Webhook
    path (= profile id), the composed systemMessage and the settings minus binaryMode. No sticky note."""
    t = copy.deepcopy(CANON[TEMPLATE_IDS[channel]])
    t['id'], t['name'] = wid, name
    nd(t, 'Webhook')['parameters']['path'] = profile
    nd(t, 'Webhook')['webhookId'] = str(uuid.uuid4())
    nd(t, 'AI Agent')['parameters']['options']['systemMessage'] = f'=Business Type: Цветы\n\nОписание бота «{name}»…'
    t['settings'] = {k: v for k, v in (t.get('settings') or {}).items() if k != 'binaryMode'}
    if fresh_ids:   # n8n may re-id nodes and a prod instance binds its own credential ids — neither is the graph
        for n in t['nodes']:
            n['id'] = str(uuid.uuid4())
            for ref in (n.get('credentials') or {}).values():
                ref['id'] = 'inst' + ref.get('id', '')[:8]
    return t


def old_clone(wid, name, profile):
    """A clone made from the 33-node template of 95221844 — the shape of prod's archived «13»."""
    text = subprocess.run(['git', '-C', str(ROOT), 'show', '95221844:Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json'],
                          capture_output=True, text=True, check=True).stdout
    t = json.loads(text)
    t['id'], t['name'] = wid, name
    nd(t, 'Webhook')['parameters']['path'] = profile
    return t


def drifted(w):
    """A hand edit in the n8n editor: the pre-send Final Check neutered (sends even when superseded)."""
    x = copy.deepcopy(w)
    nd(x, 'Final Check')['parameters']['jsCode'] = 'return $input.all(); // hot-fixed by hand'
    return x


def stamped(tool, w, channel):
    """The sticky update-clones writes, with the value THIS implementation computes for the template — i.e. a
    sticky that tells the truth about the template version, whatever the graph under it looks like."""
    x = copy.deepcopy(w)
    x['nodes'].append(tool.stamp_node(tool.template_stamp(CANON[TEMPLATE_IDS[channel]])))
    return x


# ---------------------------------------------------------------------------------------------- runners
def fake_api(instance):
    def list_workflows(base):
        return [{'id': k, 'name': v.get('name'), 'active': v.get('active'), 'isArchived': v.get('isArchived')}
                for k, v in instance.items()]

    def call(base, method, path, body=None):
        if method != 'GET':
            raise AssertionError(f'WRITE attempted on a read-only check: {method} {path}')
        wid = path.split('?')[0].split('/')[2]
        return (200, copy.deepcopy(instance[wid])) if wid in instance else (404, {'message': 'not found'})
    return list_workflows, call


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


def derive_module():
    """staging/derive-staging.py with its sqlite credential lookup stubbed (the derivation itself is pure text)."""
    d = module('staging/derive-staging.py')
    d.staging_credentials = lambda: {}
    return d


def verify(instance, base=BASE):
    vd = module('verify-deployed.py')
    vd.uc.list_workflows, vd.uc.call = fake_api(instance)
    vd.uc._DERIVE = derive_module()   # only read on a staging base; never touches the staging DB
    fd, out_json = tempfile.mkstemp(suffix='.json')
    os.close(fd)
    try:
        code, out = run_main(vd, ['verify-deployed.py', '--base', base, '--json', out_json])
        rep = json.load(open(out_json, encoding='utf-8'))
    finally:
        os.remove(out_json)
    return code, out, rep


def update_dry_run(instance):
    uc = module('update-clones.py')
    uc.list_workflows, uc.call = fake_api(instance)
    code, out = run_main(uc, ['update-clones.py', '--base', BASE])
    return code, out, json.loads(out.strip().splitlines()[-1])


def canon_row(rep, wid):
    return next(r for r in rep['canonical'] if r['id'] == wid)


def clone_row(rep, wid):
    return next((c for c in rep.get('clones', []) if c['id'] == wid), None)


def clone_ok(row):
    """The clone's verdict, whichever report shape the implementation writes."""
    return row.get('up_to_date', row.get('at_canonical'))


def brief(rep):
    return {'canonical_not_ok': [(r['id'], r.get('note')) for r in rep['canonical'] if r.get('status') != 'OK'],
            'clones': [(c['name'], clone_ok(c), c.get('stamp')) for c in rep.get('clones', [])],
            'archived': [c['name'] for c in rep.get('archived', [])]}


UC_FOR_STAMPS = module('update-clones.py')

# ---------------------------------------------------------------------------------------------- positive cases
with Case('D1 control: healthy canonical set, no clones -> exit 0') as c:
    code, out, rep = verify(healthy())
    c.check(code == 0, f'exit {code}: {brief(rep)}')

with Case('D1 prod 2026-09-25 (production-readback.json): fresh «23» without sticky is AT-CANONICAL, archived «13» listed apart -> exit 0') as c:
    rb = json.loads(READBACK.read_text(encoding='utf-8'))
    inst = healthy()
    tpl_nodes = {ch: len(real(CANON[t])) for ch, t in TEMPLATE_IDS.items()}
    for row in rb['clones']:
        c.check(row['stamp'] is None, f'readback premise: clone {row["name"]} carried no sticky on prod')
        if row['nodes'] == tpl_nodes[row['channel']]:
            inst[row['id']] = deployed(create_style_clone(row['channel'], row['id'], row['name'], 'prof' + row['name']), active=row['active'])
        else:   # «13»: the pre-stage-3 graph; archived by the owner on 2026-09-14 (implementation-progress.md)
            inst[row['id']] = deployed(old_clone(row['id'], row['name'], 'prof' + row['name']), active=row['active'], archived=True)
    code, out, rep = verify(inst)
    c.check(code == 0, f'exit {code}: {brief(rep)}')
    live = [r for r in rb['clones'] if r['nodes'] == tpl_nodes[r['channel']]]
    for r in live:
        row = clone_row(rep, r['id'])
        c.check(row is not None and clone_ok(row) is True, f'«{r["name"]}» must be at canonical by its graph: {row}')
    c.check([x['name'] for x in rep.get('archived', [])] == [r['name'] for r in rb['clones'] if r not in live],
            f'archived clones must be reported separately: {brief(rep)}')
    c.check(canon_row(rep, SUPPORT)['status'] == 'OK', f'Support Message with its chat id bound is OK: {canon_row(rep, SUPPORT)}')

with Case('D1 fresh TG clone (re-ided nodes, instance credential ids) + edited WA bot + inactive (channels off) clone -> exit 0') as c:
    inst = healthy()
    inst['tgFresh000000001'] = deployed(create_style_clone('telegram', 'tgFresh000000001', 'tg', 'profT', fresh_ids=True), active=True)
    edited = create_style_clone('whatsapp', 'waEdited00000001', 'ed', 'profE')
    nd(edited, 'AI Agent')['parameters']['options']['systemMessage'] = '=Business Type: Автозапчасти\n\n(edited in Bot Settings)'
    edited['settings'] = {'executionOrder': 'v1', 'callerPolicy': 'workflowsFromSameOwner'}
    inst['waEdited00000001'] = deployed(edited, active=True)
    inst['waOff00000000001'] = deployed(create_style_clone('whatsapp', 'waOff00000000001', 'off', 'profO'), active=False)
    code, out, rep = verify(inst)
    c.check(code == 0, f'exit {code}: {brief(rep)}')
    for wid in ('tgFresh000000001', 'waEdited00000001', 'waOff00000000001'):
        row = clone_row(rep, wid)
        c.check(row is not None and clone_ok(row) is True, f'{wid} must be at canonical: {row}')

with Case('D1 draft moved in the editor (positions only) while the published graph is canonical -> OK, exit 0') as c:
    inst = healthy()
    w = copy.deepcopy(inst[SET_REPLY])
    moved = copy.deepcopy(w)
    for n in moved['nodes']:
        n['position'] = [n['position'][0] + 40, n['position'][1]]
    inst[SET_REPLY] = deployed(moved, active=True, published=w)   # versionId != activeVersionId, both graphs canonical
    code, out, rep = verify(inst)
    c.check(code == 0 and canon_row(rep, SET_REPLY)['status'] == 'OK', f'exit {code}: {canon_row(rep, SET_REPLY)}')

with Case('D1 staging base: derived set, Support Message left unbound ON PURPOSE, a staging bot clone -> exit 0; inactive webhook still -> 2') as c:
    d = derive_module()
    inst = {wid: deployed(json.loads(d.derive_file(json.dumps(w, ensure_ascii=False))), active=wid not in TEMPLATE_IDS.values())
            for wid, w in CANON.items()}
    bot = create_style_clone('whatsapp', 'stgwa1234567890a', 'Staging WA', 'stgwa001')
    inst[bot['id']] = deployed(json.loads(d.derive_file(json.dumps(bot, ensure_ascii=False))), active=True)
    code, out, rep = verify(inst, base='http://localhost:5678')
    c.check(code == 0, f'exit {code}: {brief(rep)}')
    inst[PROXY] = deployed(inst[PROXY], active=False)
    code, out, rep = verify(inst, base='http://localhost:5678')
    c.check(code == 2 and canon_row(rep, PROXY)['status'] != 'OK', f'exit {code}: {canon_row(rep, PROXY)}')

# ---------------------------------------------------------------------------------------------- negative cases
with Case('D1 NEG forged correct sticky on a drifted clone graph -> BEHIND, «sticky claims … graph is …», exit 2') as c:
    inst = healthy()
    inst['cloneDrift000001'] = deployed(stamped(UC_FOR_STAMPS, drifted(create_style_clone('whatsapp', 'cloneDrift000001', 'drift', 'profD')), 'whatsapp'), active=True)
    code, out, rep = verify(inst)
    row = clone_row(rep, 'cloneDrift000001')
    c.check(code == 2, f'exit {code}: {brief(rep)}')
    c.check(row is not None and clone_ok(row) is False, f'the drifted clone must not pass on its sticky: {row}')
    c.check('sticky claims' in out, 'the report must say the sticky claims one version while the graph is another:\n' + out[-800:])

with Case('D1 NEG inactive required webhook (Wappi Proxy imported, never activated) -> DIFF, exit 2') as c:
    inst = healthy()
    inst[PROXY] = deployed(inst[PROXY], active=False)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, PROXY)['status'] != 'OK', f'exit {code}: {canon_row(rep, PROXY)}')

with Case('D1 NEG a clone template switched on (templates must stay inactive) -> DIFF, exit 2') as c:
    inst = healthy()
    inst[TEMPLATE_IDS['whatsapp']] = deployed(inst[TEMPLATE_IDS['whatsapp']], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, TEMPLATE_IDS['whatsapp'])['status'] != 'OK', f'exit {code}: {canon_row(rep, TEMPLATE_IDS["whatsapp"])}')

with Case('D1 NEG clone: canonical draft, but the PUBLISHED graph is the drifted one -> not at canonical, exit 2') as c:
    inst = healthy()
    fresh = stamped(UC_FOR_STAMPS, create_style_clone('whatsapp', 'cloneUnpub000001', 'unpub', 'profU'), 'whatsapp')
    inst['cloneUnpub000001'] = deployed(fresh, active=True, published=drifted(fresh))
    code, out, rep = verify(inst)
    row = clone_row(rep, 'cloneUnpub000001')
    c.check(code == 2 and row is not None and clone_ok(row) is False, f'exit {code}: {row}')

with Case('D1 NEG canonical workflow: canonical draft, published graph has the Auth Gate bypassed -> DIFF, exit 2') as c:
    inst = healthy()
    pub = copy.deepcopy(inst[SET_REPLY])
    pub['connections']['Webhook'] = link(next(t['node'] for t in pub['connections']['Authorized?']['main'][0]))
    inst[SET_REPLY] = deployed(inst[SET_REPLY], active=True, published=pub)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, SET_REPLY)['status'] != 'OK', f'exit {code}: {canon_row(rep, SET_REPLY)}')

with Case('D1 NEG Support Message: chat id bound AND an extra edit (Validate clamp removed) -> DIFF, exit 2') as c:
    inst = healthy()
    nd(inst[SUPPORT], 'Validate')['parameters']['jsCode'] = 'return [{ json: { ok: true, text: String($input.first().json.body.text) } }];'
    inst[SUPPORT] = deployed(inst[SUPPORT], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, SUPPORT)['status'] != 'OK', f'exit {code}: {canon_row(rep, SUPPORT)}')

with Case('D1 NEG Support Message: connections rewired (Webhook -> Send, no gate/validation) hidden behind SUBSTITUTED -> DIFF, exit 2') as c:
    inst = healthy()
    inst[SUPPORT]['connections']['Webhook'] = link('Send To Support Chat')
    inst[SUPPORT] = deployed(inst[SUPPORT], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, SUPPORT)['status'] != 'OK', f'exit {code}: {canon_row(rep, SUPPORT)}')

with Case('D1 NEG Support Message deployed with the placeholder chat id (substitution not applied) -> DIFF, exit 2') as c:
    inst = healthy()
    nd(inst[SUPPORT], 'Send To Support Chat')['parameters']['chatId'] = '__CHAT_ID__'
    inst[SUPPORT] = deployed(inst[SUPPORT], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, SUPPORT)['status'] != 'OK', f'exit {code}: {canon_row(rep, SUPPORT)}')

with Case('D1 NEG Set Workflow State: Auth Gate bypassed (Webhook -> Validate) -> DIFF, exit 2') as c:
    inst = healthy()
    inst[SWS]['connections']['Webhook'] = link('Validate')
    inst[SWS] = deployed(inst[SWS], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, SWS)['status'] != 'OK', f'exit {code}: {canon_row(rep, SWS)}')

with Case('D1 NEG Set Workflow State: PROTECTED list lost the stage-7 ids -> DIFF, exit 2') as c:
    inst = healthy()
    v = nd(inst[SWS], 'Validate')['parameters']
    v['jsCode'] = v['jsCode'].replace("'crTestDialog0001'", "'x'").replace("'crHandoffDry0001'", "'y'")
    c.check('crTestDialog0001' not in v['jsCode'], 'premise: the mutation removed the id')
    inst[SWS] = deployed(inst[SWS], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, SWS)['status'] != 'OK', f'exit {code}: {canon_row(rep, SWS)}')

with Case('D1 NEG a node disabled by hand (Wappi Proxy Auth Gate) -> DIFF, exit 2') as c:
    inst = healthy()
    nd(inst[PROXY], 'Auth Gate')['disabled'] = True
    inst[PROXY] = deployed(inst[PROXY], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, PROXY)['status'] != 'OK', f'exit {code}: {canon_row(rep, PROXY)}')

with Case('D1 NEG Test Dialog imported with its credential placeholder unbound -> DIFF, exit 2') as c:
    inst = healthy()
    n = next(n for n in inst[TEST_DIALOG]['nodes'] if (n.get('credentials') or {}).get('httpHeaderAuth'))
    n['credentials']['httpHeaderAuth']['id'] = '__HDR_CRED__'
    inst[TEST_DIALOG] = deployed(inst[TEST_DIALOG], active=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, TEST_DIALOG)['status'] != 'OK', f'exit {code}: {canon_row(rep, TEST_DIALOG)}')

with Case('D1 NEG an ARCHIVED canonical workflow is not a running one -> DIFF, exit 2') as c:
    inst = healthy()
    inst[PROXY] = deployed(inst[PROXY], active=True, archived=True)
    code, out, rep = verify(inst)
    c.check(code == 2 and canon_row(rep, PROXY)['status'] != 'OK', f'exit {code}: {canon_row(rep, PROXY)}')

with Case('D1 NEG the archive exemption is not blanket: a LIVE clone on the old graph still fails -> exit 2') as c:
    inst = healthy()
    inst['oldLive00000001'] = deployed(old_clone('oldLive00000001', 'old', 'profL'), active=True)
    inst['oldArch00000001'] = deployed(old_clone('oldArch00000001', 'arch', 'profA'), active=False, archived=True)
    code, out, rep = verify(inst)
    row = clone_row(rep, 'oldLive00000001')
    c.check(code == 2 and row is not None and clone_ok(row) is False, f'exit {code}: {brief(rep)}')

# ---------------------------------------------------------------------------------------------- update-clones
with Case('D1 update-clones: fresh «23» (no sticky, canonical graph) is up to date — no needless PUT + re-activate of a live bot') as c:
    code, out, s = update_dry_run({'dt6hHFUVrW07unTj': deployed(create_style_clone('whatsapp', 'dt6hHFUVrW07unTj', '23', 'prof23'), active=True)})
    c.check(s.get('up_to_date') == 1 and s.get('would_update') == 0, s)

with Case('D1 update-clones: forged sticky over a drifted graph is planned for update (drift is repairable)') as c:
    code, out, s = update_dry_run({'cloneDrift000001': deployed(stamped(UC_FOR_STAMPS, drifted(create_style_clone('whatsapp', 'cloneDrift000001', 'd', 'pD')), 'whatsapp'), active=True)})
    c.check(s.get('would_update') == 1 and s.get('up_to_date') == 0, s)
    c.check('sticky claims' in out, 'the dry-run must name the lying sticky:\n' + out[-600:])

with Case('D1 update-clones: canonical draft + stale PUBLISHED graph is planned for update (the apply re-publishes)') as c:
    fresh = stamped(UC_FOR_STAMPS, create_style_clone('whatsapp', 'cloneUnpub000001', 'u', 'pU'), 'whatsapp')
    code, out, s = update_dry_run({'cloneUnpub000001': deployed(fresh, active=True, published=drifted(fresh))})
    c.check(s.get('would_update') == 1 and s.get('up_to_date') == 0, s)

with Case('D1 update-clones: a clone rebuilt by build_target is up to date by its GRAPH (no endless would_update)') as c:
    old = old_clone('rebuilt000000001', 'r', 'pR')
    target = UC_FOR_STAMPS.build_target(old, CANON[TEMPLATE_IDS['whatsapp']])
    after = dict(target, id='rebuilt000000001')
    code, out, s = update_dry_run({'rebuilt000000001': deployed(after, active=True)})
    c.check(s.get('up_to_date') == 1 and s.get('would_update') == 0, s)

with Case('D1 deploy-canonical (shares graph_hash): healthy set is up to date; a hand-disabled node is planned for re-deploy') as c:
    def deploy_dry_run(instance):
        dc = module('deploy-canonical.py')
        dc.uc.list_workflows, dc.uc.call = fake_api(instance)
        code, out = run_main(dc, ['deploy-canonical.py', '--base', BASE])
        return json.loads(out.strip().splitlines()[-1])
    s = deploy_dry_run(healthy())
    c.check(s.get('would_update') == 0 and s.get('up_to_date') == s.get('considered') > 0, s)
    inst = healthy()
    nd(inst[PROXY], 'Auth Gate')['disabled'] = True
    s = deploy_dry_run(inst)
    c.check(s.get('would_update') == 1, s)

with Case('D1 update-clones: archived clones are skipped, never counted as clones') as c:
    code, out, s = update_dry_run({'tqGXmiT6aJu7vUIG': deployed(old_clone('tqGXmiT6aJu7vUIG', '13', 'p13'), active=False, archived=True)})
    c.check(s.get('archived') == 1 and s.get('clones') == 0, s)

report()
