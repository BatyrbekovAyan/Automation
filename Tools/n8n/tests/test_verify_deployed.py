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


def share_book(uc):
    """Every fresh import of update-clones.py gets the ONE prompt book read from git at the top of this file (V10) —
    the history is the same for every case, re-reading it per case would only cost time."""
    if BOOK is not None:
        uc._PROMPT_BOOK = BOOK
    return uc


def verify(instance, base=BASE):
    vd = module('verify-deployed.py')
    share_book(vd.uc)
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


def update_dry_run(instance, extra=()):
    uc = share_book(module('update-clones.py'))
    uc.list_workflows, uc.call = fake_api(instance)
    code, out = run_main(uc, ['update-clones.py', '--base', BASE, *extra])
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
# V10: the prompt history (every committed Create/Edit PROMPTS map + every committed template systemMessage), read
# from git ONCE and shared by every fresh module import (share_book). None on an implementation without it.
BOOK = UC_FOR_STAMPS.prompt_book() if hasattr(UC_FOR_STAMPS, 'prompt_book') else None

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

def staged(d, w):
    """A canonical workflow exactly as derive-staging.py deploys it to staging: hosts derived AND (R3, 2026-09-27)
    the Supabase writers neutralised AND (V9, 2026-09-28) the price search reading the staging documents stub."""
    x = json.loads(d.derive_file(json.dumps(w, ensure_ascii=False)))
    d.isolate_supabase_writers(x)
    d.isolate_supabase_readers(x)
    return x


with Case('D1 staging base: derived set, Support Message left unbound ON PURPOSE, a staging bot clone -> exit 0; inactive webhook still -> 2') as c:
    d = derive_module()
    inst = {wid: deployed(staged(d, w), active=wid not in TEMPLATE_IDS.values())
            for wid, w in CANON.items()}
    bot = create_style_clone('whatsapp', 'stgwa1234567890a', 'Staging WA', 'stgwa001')
    inst[bot['id']] = deployed(json.loads(d.derive_file(json.dumps(bot, ensure_ascii=False))), active=True)
    code, out, rep = verify(inst, base='http://localhost:5678')
    c.check(code == 0, f'exit {code}: {brief(rep)}')
    inst[PROXY] = deployed(inst[PROXY], active=False)
    code, out, rep = verify(inst, base='http://localhost:5678')
    c.check(code == 2 and canon_row(rep, PROXY)['status'] != 'OK', f'exit {code}: {canon_row(rep, PROXY)}')

with Case('R3 NEG staging base: an Upload File copy that still WRITES to the real Supabase (writers not neutralised) -> DIFF, exit 2') as c:
    d = derive_module()
    inst = {wid: deployed(staged(d, w), active=wid not in TEMPLATE_IDS.values()) for wid, w in CANON.items()}
    upload_id = 'KoTuIlk4LMrlvnWI'
    inst[upload_id] = deployed(json.loads(d.derive_file(json.dumps(CANON[upload_id], ensure_ascii=False))), active=True)
    code, out, rep = verify(inst, base='http://localhost:5678')
    c.check(code == 2 and canon_row(rep, upload_id)['status'] != 'OK', f'exit {code}: {canon_row(rep, upload_id)}')

with Case('V9 NEG staging base: a Price List Search copy still searching the real Supabase project (reader not redirected) -> DIFF, exit 2') as c:
    d = derive_module()
    inst = {wid: deployed(staged(d, w), active=wid not in TEMPLATE_IDS.values()) for wid, w in CANON.items()}
    search_id = 'crPriceSearch001'
    inst[search_id] = deployed(json.loads(d.derive_file(json.dumps(CANON[search_id], ensure_ascii=False))), active=True)
    code, out, rep = verify(inst, base='http://localhost:5678')
    c.check(code == 2 and canon_row(rep, search_id)['status'] != 'OK', f'exit {code}: {canon_row(rep, search_id)}')

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

# ---------------------------------------------------------------------------------------------- V10 prompt head
# A clone's systemMessage = `[=]Business Type: <label>\n\n<HEAD>\n` + `\nAdditional Instructions: …`. Only the tail (from
# the FIRST marker on) is the owner's; the HEAD is shared template content — `vertical + _core + _universal` from the
# Create/Edit `Vertical Prompt` map of the day, or the template's own prompt for a legacy id. update-clones must carry
# the tail over and bring a head of an EARLIER map to today's, recognising that head by CONTENT (the label is free
# text: e2e-auth creates flowers bots labelled «Цветы»), and leave any head no map ever wrote alone. Every fixture
# prompt below is composed by the REAL Set Fields expression of the ref named (n8n's own engine), never by hand.
from harness import expr   # noqa: E402

CREATE_FILES = {'whatsapp': 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'telegram': 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json'}
EDIT_FILES = {'whatsapp': '3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json', 'telegram': 'TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json'}
TEMPLATE_FILES = {'whatsapp': '4wYitz5ek30SVNlT-WhatsApp_Bot.json', 'telegram': '4VN3gsFaC2HUYmcc-Telegram_Bot.json'}
PROD_REF = 'f08546f1'   # the canon deployed to prod on 2026-09-24 — the epoch of «23»'s head (review V10)
OLD_REF = '95221844'    # before stage 4: the template's own prompt carried no create_handoff rule yet
CHANNELS = ('whatsapp', 'telegram')
SM_FIELD = 'nodes[5].parameters.options.systemMessage'
MARK = '\nAdditional Instructions: '


def at_ref(ref, fname):
    """A workflow JSON at a git ref (None = the working tree = today's canon)."""
    if ref is None:
        return json.loads((WF / fname).read_text(encoding='utf-8'))
    return json.loads(subprocess.run(['git', '-C', str(ROOT), 'show', f'{ref}:Tools/n8n/workflows/{fname}'],
                                     capture_output=True, text=True, check=True).stdout)


def prompts_map(w):
    js = nd(w, 'Vertical Prompt')['parameters']['jsCode']
    return json.loads(js.split('const PROMPTS = ', 1)[1].split(';\n// PROMPTS-END', 1)[0])


def sm_expr(w):
    return next(a['value'] for a in nd(w, 'Set Fields')['parameters']['assignments']['assignments'] if a['name'] == SM_FIELD)


def form(label, vid, prompt='Отвечай коротко. Постоянным клиентам — скидка 5%.'):
    """The settings form the app posts (Manager.cs): BusinessType = the displayName, BusinessTypeId = the id."""
    return {'BusinessType': label, 'BusinessTypeId': vid, 'Prompt': prompt,
            'Business': 'About Business:\nАвтозапчасти у рынка «Алтын», Алматы\n\nКонтакты:\nТелефон: +7 701 000 00 00',
            'ProductsList': 'Product1: Колодки 1234-ABC\nProduct1 Price: 18000', 'ServicesList': 'Service1: Подбор по VIN'}


def created_sm(ch, b, ref=None):
    """What Create at `ref` composes for this form: its own Vertical Prompt map, the template of the same ref as the
    legacy fallback (`slice(1)`), evaluated by n8n's engine."""
    create = at_ref(ref, CREATE_FILES[ch])
    vp = prompts_map(create).get(b['BusinessTypeId'], '')
    return expr(sm_expr(create), at_ref(ref, TEMPLATE_FILES[ch]),
                {'Unity Webhook': {'body': b}, 'Vertical Prompt': {'verticalPrompt': vp}})


def edited_sm(ch, prior_sm, b, ref=None):
    """What Edit at `ref` writes on a settings save of a clone whose prompt is `prior_sm`."""
    edit = at_ref(ref, EDIT_FILES[ch])
    vp = prompts_map(edit).get(b['BusinessTypeId'], '')
    clone = {'nodes': [{}] * 5 + [{'parameters': {'options': {'systemMessage': prior_sm}}}]}
    return expr(sm_expr(edit), clone, {'Unity Webhook': {'body': b}, 'Vertical Prompt': {'verticalPrompt': vp}})


def prompt_clone(ch, wid, sm, graph_ref=None):
    """A clone of the template at `graph_ref` (None = today's) carrying the prompt `sm`, Create-style (no sticky)."""
    t = at_ref(graph_ref, TEMPLATE_FILES[ch])
    t['id'], t['name'] = wid, f'bot {wid}'
    nd(t, 'Webhook')['parameters']['path'] = 'prof' + wid
    nd(t, 'Webhook')['webhookId'] = str(uuid.uuid4())
    nd(t, 'AI Agent')['parameters']['options']['systemMessage'] = sm
    t['settings'] = {k: v for k, v in (t.get('settings') or {}).items() if k != 'binaryMode'}
    return t


def sm_of(w):
    return nd(w, 'AI Agent')['parameters']['options']['systemMessage']


def built(clone, ch, **kw):
    """update-clones.build_target against today's template of the channel."""
    uc = share_book(module('update-clones.py'))
    return uc.build_target(clone, CANON[TEMPLATE_IDS[ch]], **kw)


def where(a, b):
    """First differing offset of two prompts, for a readable failure."""
    if a == b:
        return 'identical'
    i = next((k for k in range(min(len(a), len(b))) if a[k] != b[k]), min(len(a), len(b)))
    return f'differ at {i} (len {len(a)} vs {len(b)}): {a[max(0, i - 40):i + 60]!r} vs {b[max(0, i - 40):i + 60]!r}'


def prow(row):
    keys = ('prompt_state', 'prompt_vertical', 'prompt_label', 'prompt_since', 'published_prompt_state', 'up_to_date', 'at_canonical')
    return {k: (row or {}).get(k) for k in keys}


NOW = {ch: prompts_map(at_ref(None, CREATE_FILES[ch])) for ch in CHANNELS}
PROD = {ch: prompts_map(at_ref(PROD_REF, CREATE_FILES[ch])) for ch in CHANNELS}
AP = form('Автозапчасти', 'auto_parts')

with Case('V10 premise: today\'s map is inject-prompts\' composition (WA ≡ TG) and every vertical head of the prod epoch differs from it') as c:
    ip = module('inject-prompts.py')
    c.check(NOW['whatsapp'] == NOW['telegram'] == ip.load_prompts(), 'Create WA/TG maps must equal load_prompts() (run inject-prompts.py)')
    c.check(PROD['whatsapp'] == PROD['telegram'], 'the prod-epoch maps of WA and TG must agree')
    stale = [v for v in ip.VERTICALS if PROD['whatsapp'][v] != NOW['whatsapp'][v]]
    c.check(stale == list(ip.VERTICALS), f'every vertical changed since {PROD_REF} (d7796c32): {stale}')
    c.check(created_sm('whatsapp', AP, PROD_REF) != created_sm('whatsapp', AP), 'premise: Create then and Create today differ')

for ch in CHANNELS:
    tag = ch[:2]
    with Case(f'V10 T1 [{ch}] canonical graph + auto_parts head of {PROD_REF} -> PROMPT-BEHIND (auto_parts), not up to date, exit 2') as c:
        wid = f'v10t1{tag}000000001'
        inst = healthy()
        inst[wid] = deployed(prompt_clone(ch, wid, created_sm(ch, AP, PROD_REF)), active=True)
        code, out, rep = verify(inst)
        row = clone_row(rep, wid)
        c.check(code == 2, f'exit {code}: {brief(rep)}')
        c.check(row is not None and row.get('prompt_state') == 'behind' and row.get('prompt_vertical') == 'auto_parts'
                and clone_ok(row) is False, f'row: {prow(row)}')
        c.check('PROMPT-BEHIND' in out, 'the report must name the stale prompt:\n' + out[-700:])

    with Case(f'V10 T2 [{ch}] graph AND head of {PROD_REF}: dry-run names the vertical; build_target = what Create composes today, owner tail byte-identical') as c:
        wid = f'v10t2{tag}000000001'
        b = form('Автозапчасти', 'auto_parts', prompt='Пиши кратко.\nAdditional Instructions: этот текст тоже владельца')
        old_sm = created_sm(ch, b, PROD_REF)
        clone = prompt_clone(ch, wid, old_sm, graph_ref=PROD_REF)
        code, out, s = update_dry_run({wid: deployed(clone, active=True)})
        c.check(s.get('would_update') == 1, s)
        c.check('auto_parts' in out and 'behind' in out, 'the dry-run must name the vertical and that its head is behind:\n' + out[-900:])
        target = built(clone, ch)
        want = created_sm(ch, b)
        c.check(sm_of(target) == want, 'the refreshed prompt must be exactly what Create composes today: ' + where(sm_of(target), want))
        c.check(sm_of(target)[sm_of(target).index(MARK):] == old_sm[old_sm.index(MARK):], 'the owner tail (from the FIRST marker) must be byte-identical')
        c.check(sm_of(target).startswith('Business Type: Автозапчасти\n\n') and NOW[ch]['auto_parts'] in sm_of(target), 'label line kept, today\'s head in')

        with Case(f'V10 T3 [{ch}] the T2 target, deployed, is up to date by graph AND prompt (verify exit 0, no second update)') as c3:
            after = dict(target, id=wid)
            inst = healthy()
            inst[wid] = deployed(after, active=True)
            code, out, rep = verify(inst)
            row = clone_row(rep, wid)
            c3.check(code == 0 and clone_ok(row) is True and row.get('prompt_state') == 'at_canonical', f'exit {code}: {prow(row)}')
            code, out, s = update_dry_run({wid: deployed(after, active=True)})
            c3.check(s.get('up_to_date') == 1 and s.get('would_update') == 0, s)

    with Case(f'V10 T5 [{ch}] stored prompt at today\'s head, PUBLISHED prompt of {PROD_REF} -> not up to date, exit 2, planned for update') as c:
        wid = f'v10t5{tag}000000001'
        stored = prompt_clone(ch, wid, created_sm(ch, AP))
        pub = prompt_clone(ch, wid, created_sm(ch, AP, PROD_REF))
        inst = healthy()
        inst[wid] = deployed(stored, active=True, published=pub)
        code, out, rep = verify(inst)
        row = clone_row(rep, wid)
        c.check(code == 2 and row is not None and clone_ok(row) is False, f'exit {code}: {prow(row)}')
        c.check(row is not None and row.get('prompt_state') == 'at_canonical' and row.get('published_prompt_state') == 'behind', prow(row))
        code, out, s = update_dry_run({wid: deployed(stored, active=True, published=pub)})
        c.check(s.get('would_update') == 1, s)

    with Case(f'V10 T6 [{ch}] Edit grammar (a settings save at {PROD_REF}) -> refreshed to exactly what Edit writes today, exit 2 before') as c:
        wid = f'v10t6{tag}000000001'
        saved = form('Автозапчасти', 'auto_parts', prompt='Не обещай доставку в выходные.')
        edit_sm = edited_sm(ch, created_sm(ch, AP, PROD_REF), saved, PROD_REF)
        c.check(MARK + 'Не обещай' in edit_sm and '  \n\nAbout' not in edit_sm, 'premise: the fixture carries the Edit grammar')
        clone = prompt_clone(ch, wid, edit_sm, graph_ref=PROD_REF)
        inst = healthy()
        inst[wid] = deployed(prompt_clone(ch, wid, edit_sm), active=True)
        code, out, rep = verify(inst)
        c.check(code == 2 and prow(clone_row(rep, wid))['prompt_state'] == 'behind', f'exit {code}: {prow(clone_row(rep, wid))}')
        want = edited_sm(ch, edit_sm, saved)
        c.check(sm_of(built(clone, ch)) == want, 'must equal what the next Edit save writes: ' + where(sm_of(built(clone, ch)), want))

    with Case(f'V10 T8 [{ch}] e2e-auth form (label «Цветы», id flowers) with the flowers head of {PROD_REF} -> behind(flowers) by CONTENT, refreshed') as c:
        wid = f'v10t8{tag}000000001'
        fl = form('Цветы', 'flowers')
        clone = prompt_clone(ch, wid, created_sm(ch, fl, PROD_REF), graph_ref=PROD_REF)
        inst = healthy()
        inst[wid] = deployed(prompt_clone(ch, wid, created_sm(ch, fl, PROD_REF)), active=True)
        code, out, rep = verify(inst)
        row = clone_row(rep, wid)
        c.check(code == 2 and row is not None and row.get('prompt_state') == 'behind' and row.get('prompt_vertical') == 'flowers'
                and row.get('prompt_label') == 'Цветы', f'exit {code}: {prow(row)}')
        want = created_sm(ch, fl)
        c.check(sm_of(built(clone, ch)) == want, where(sm_of(built(clone, ch)), want))
        lying = prompt_clone(ch, wid, created_sm(ch, fl, PROD_REF).replace('Business Type: Цветы', 'Business Type: Автозапчасти', 1))
        inst[wid] = deployed(lying, active=True)
        code, out, rep = verify(inst)
        c.check(prow(clone_row(rep, wid))['prompt_vertical'] == 'flowers', 'the label decides nothing — the head is flowers: ' + str(prow(clone_row(rep, wid))))
        c.check('label' in out, 'a label that is not the vertical\'s app label is reported:\n' + out[-700:])

    with Case(f'V10 T10 [{ch}] legacy id (generic head) of the {OLD_REF} template -> generic behind, refreshed to today\'s template prompt') as c:
        wid = f'v10ta{tag}000000001'
        legacy = form('', '')
        old_sm = created_sm(ch, legacy, OLD_REF)
        c.check('create_handoff' not in old_sm and 'create_handoff' in created_sm(ch, legacy), 'premise: the old generic head lacks the handoff rule')
        inst = healthy()
        inst[wid] = deployed(prompt_clone(ch, wid, old_sm), active=True)
        code, out, rep = verify(inst)
        row = clone_row(rep, wid)
        c.check(code == 2 and row is not None and row.get('prompt_state') == 'generic_behind' and clone_ok(row) is False, f'exit {code}: {prow(row)}')
        clone = prompt_clone(ch, wid, old_sm, graph_ref=OLD_REF)
        want = created_sm(ch, legacy)
        c.check(sm_of(built(clone, ch)) == want, where(sm_of(built(clone, ch)), want))

with Case('V10 T4 preservation: heads no map wrote, today\'s heads and non-grammar prompts are kept byte-identical; verify exit 0') as c:
    inst = healthy()
    fixtures = {
        'v10p1wa000000001': ('whatsapp', create_style_clone('whatsapp', 'v10p1wa000000001', 'Цветы бот', 'profP1')),   # «Цветы», no marker (e2e fixture shape)
        'v10p2wa000000001': ('whatsapp', prompt_clone('whatsapp', 'v10p2wa000000001', '=Business Type: Автозапчасти\n\n(edited in Bot Settings)')),
        'v10p3tg000000001': ('telegram', prompt_clone('telegram', 'v10p3tg000000001', created_sm('telegram', form('', '')))),   # legacy id, generic head of today
        'v10p4wa000000001': ('whatsapp', prompt_clone('whatsapp', 'v10p4wa000000001', sm_of(old_clone('x', 'x', 'x')))),   # the 95221844 template prompt, leading '='
        'v10p5tg000000001': ('telegram', prompt_clone('telegram', 'v10p5tg000000001', created_sm('telegram', form('Автозапчасти', 'auto_parts', prompt='Кратко.\nAdditional Instructions: тоже текст владельца')))),
        'v10p6wa000000001': ('whatsapp', prompt_clone('whatsapp', 'v10p6wa000000001', created_sm('whatsapp', form('dentist', 'dentist')))),   # pre-vertical raw id
    }
    states = {}
    for wid, (ch, w) in fixtures.items():
        inst[wid] = deployed(w, active=True)
        t = built(w, ch)
        c.check(sm_of(t) == sm_of(w), f'{wid}: build_target must keep the prompt verbatim: ' + where(sm_of(t), sm_of(w)))
    code, out, rep = verify(inst)
    c.check(code == 0, f'exit {code}: {brief(rep)}')
    for wid in fixtures:
        states[wid] = prow(clone_row(rep, wid))['prompt_state']
    c.check(states == {'v10p1wa000000001': 'custom', 'v10p2wa000000001': 'custom', 'v10p3tg000000001': 'generic',
                       'v10p4wa000000001': 'custom', 'v10p5tg000000001': 'at_canonical', 'v10p6wa000000001': 'generic'}, states)

with Case('V10 T9 a head edited by hand under a known label -> custom: never refreshed automatically, exit 0, reported') as c:
    for ch in CHANNELS:
        wid = f'v10t9{ch[:2]}000000001'
        sm = created_sm(ch, AP)
        hand = sm.replace(NOW[ch]['auto_parts'], NOW[ch]['auto_parts'] + '\n\nДоп. правило, вписанное руками в редакторе n8n.', 1)
        w = prompt_clone(ch, wid, hand)
        c.check(sm_of(built(w, ch)) == hand, f'{ch}: kept verbatim')
        inst = healthy()
        inst[wid] = deployed(w, active=True)
        code, out, rep = verify(inst)
        c.check(code == 0 and prow(clone_row(rep, wid))['prompt_state'] == 'custom', f'{ch}: exit {code}: {prow(clone_row(rep, wid))}')
        c.check('custom' in out, out[-500:])
        code, out, s = update_dry_run({wid: deployed(w, active=True)})
        c.check(s.get('up_to_date') == 1 and s.get('would_update') == 0, f'{ch}: a custom head alone never plans a PUT: {s}')

with Case('V10 T11 --set-vertical is the ONLY way a custom head is replaced (explicit operator decision)') as c:
    for ch in CHANNELS:
        wid = f'v10tb{ch[:2]}000000001'
        fl = form('Цветы', 'flowers')
        mine = created_sm(ch, fl).replace(NOW[ch]['flowers'], 'Мой собственный промпт, написанный владельцем вручную.', 1)
        w = prompt_clone(ch, wid, mine)
        code, out, s = update_dry_run({wid: deployed(w, active=True)})
        c.check(s.get('up_to_date') == 1, f'{ch}: without the flag the custom head is kept: {s}')
        code, out, s = update_dry_run({wid: deployed(w, active=True)}, extra=['--set-vertical', f'{wid}=flowers'])
        c.check(s.get('would_update') == 1 and 'set-vertical' in out, f'{ch}: {s}\n' + out[-500:])
        want = created_sm(ch, fl)
        got = sm_of(built(w, ch, set_vertical='flowers'))
        c.check(got == want, f'{ch}: ' + where(got, want))

with Case('V10 NEG the prompt history is unreadable (no git) -> the tools refuse, never judge every head «custom»') as c:
    m = module('update-clones.py')
    m.REPO = tempfile.mkdtemp()
    try:
        m.prompt_book()
        c.check(False, 'prompt_book() returned without a history')
    except SystemExit as e:
        c.check('history' in str(e), str(e))


def fake_api_rw(instance, publish_on_put):
    """n8n 2.36's write side for update-clones --apply: PUT stores the draft and — on an active workflow, by default —
    publishes it (`publish_on_put`); deactivate/activate re-publish. Every call is recorded."""
    calls = []

    def list_workflows(base):
        return [{'id': k, 'name': v.get('name'), 'active': v.get('active')} for k, v in instance.items()]

    def publish(w):
        w['activeVersionId'] = w['versionId']
        w['activeVersion'] = {'versionId': w['versionId'], 'nodes': copy.deepcopy(w['nodes']), 'connections': copy.deepcopy(w['connections'])}

    def call(base, method, path, body=None):
        calls.append((method, path))
        wid = path.split('?')[0].split('/')[2]
        w = instance[wid]
        if method == 'GET':
            return 200, copy.deepcopy(w)
        if method == 'PUT':
            for k in ('name', 'nodes', 'connections', 'settings'):
                w[k] = copy.deepcopy(body[k])
            w['versionId'] = f'ver-put-{len(calls)}'
            if w['active'] and publish_on_put:
                publish(w)
            return 200, copy.deepcopy(w)
        if path.endswith('/deactivate'):
            w['active'], w['activeVersionId'], w['activeVersion'] = False, None, None
            return 200, {}
        if path.endswith('/activate'):
            w['active'] = True
            publish(w)
            return 200, {}
        raise AssertionError(f'unexpected {method} {path}')
    return list_workflows, call, calls


for publish_on_put in (True, False):
    with Case(f'V10 apply: PUT {"publishes" if publish_on_put else "does NOT publish"} -> deactivate/activate {"never" if publish_on_put else "exactly once"}; read-back ok, prompt refreshed') as c:
        wid = 'v10apwa000000001'
        inst = {wid: deployed(prompt_clone('whatsapp', wid, created_sm('whatsapp', AP, PROD_REF), graph_ref=PROD_REF), active=True)}
        uc = share_book(module('update-clones.py'))
        uc.list_workflows, uc.call, calls = fake_api_rw(inst, publish_on_put)
        code, out = run_main(uc, ['update-clones.py', '--base', BASE, '--only', wid, '--apply', '--yes'])
        s = json.loads(out.strip().splitlines()[-1])
        cycles = [p for m, p in calls if p.endswith('/deactivate') or p.endswith('/activate')]
        c.check(code == 0 and s.get('updated') == 1 and 'read-back: ok' in out, f'{s}\n' + out[-600:])
        c.check(cycles == ([] if publish_on_put else [f'/workflows/{wid}/deactivate', f'/workflows/{wid}/activate']), f'publish window: {cycles}')
        w = inst[wid]
        c.check(w['active'] and w['activeVersionId'] == w['versionId'], 'the updated version is the published one')
        c.check(sm_of(w) == created_sm('whatsapp', AP) and sm_of(w['activeVersion']) == created_sm('whatsapp', AP), 'stored and published prompt refreshed')


report()
