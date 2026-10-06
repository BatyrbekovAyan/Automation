#!/usr/bin/env python3
"""Read-only product review: canonical node bodies + session-TEMP tables in PG127.0.0.1:5433.
No HTTP requests, no production services, no non-temp database mutations.
Run: python3 docs/audits/2026-09-28-review-evidence/auth-residuals.py
"""
import datetime as dt
import json
import subprocess
import sys
import time
from pathlib import Path
ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'Tools/n8n/tests'))
from harness import PgSession, bind, code, expr, node, wf
W = wf('crAuthSession001-Auth_Session.json')
P = wf('crWappiProxy0001-Wappi_Proxy.json')
U = wf('KoTuIlk4LMrlvnWI-Upload_File.json')
D = wf('ZTqpumOpL1rNDOp6-Delete_File.json')
G = wf('jtbssfzXbOxwTK4k-Get_Usage.json')
C = wf('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json')
Q = lambda w, n: node(w, n)['parameters']['query']
IF = lambda w, n: node(w, n)['parameters']['conditions']['conditions'][0]['leftValue']
SETUP = '''create temp table installs (like public.installs including all);
create temp table bot_profiles (like public.bot_profiles including all);
alter table bot_profiles add column if not exists claim_pair_by text, add column if not exists claim_pair_at timestamptz;
create temp table billing_bindings (app_user_id text primary key, install_id text not null, bound_at timestamptz not null default now());
create temp table subscribers (like public.subscribers including all);
create temp table dialog_counts (like public.dialog_counts including all);'''
results = {}

# R1: no claimant completes authentication. A merely asks the proxy for QR. The actual legacy
# owner then independently reconnects the existing Wappi profile. Its status is the only external fixture.
with PgSession(SETUP) as pg:
    pg.x("insert into installs (install_id, secret_hash) values ('auth-review-attacker','h');")
    pg.x("insert into bot_profiles (profile_id, app_user_id, channel, workflow_id) values ('legacy-victim','victim-billing','whatsapp','victim-workflow');")
    route = {'op':'claim.start','profileId':'legacy-victim','channel':'whatsapp','installId':'auth-review-attacker'}
    inst = {'verdict':'checked','install_id':'auth-review-attacker','app_user_id':None}
    start = pg.q(bind(Q(W,'Claim Start'), ['legacy-victim','auth-review-attacker','','whatsapp']))[0]
    dec = code(W,'Claim Decide',{'json':{'statusCode':200,'body':{'authorized':False}}}, named={'Route':route,'Verify Install':inst,'Claim Start':start})[0]['json']
    pending = pg.q(bind(Q(W,'Reload Ownership'), ['auth-review-attacker']))[0]
    gate = {'crAuth':{'ok':True,'mode':'session','installId':'auth-review-attacker','profiles':pending['p'],'workflows':pending['w'],'claimPending':pending['c'],'appUserId':None},'body':{'op':'qr','channel':'whatsapp','profile_id':'legacy-victim'}}
    br = code(P,'Build Request',{'json':gate})[0]['json']
    assert br['ok'] and br['claimPairing']
    params = expr(node(P,'Record Claim Pairing')['parameters']['options']['queryReplacement'],br,{'Auth Gate':gate,'Build Request':br})
    recorded = pg.q(bind(Q(P,'Record Claim Pairing'),params))
    # Ensure a real later timestamp rather than a mocked future time. No request completes A's QR.
    pg.x('select pg_sleep(1.2);')
    owner_reauth = int(float(pg.q('select extract(epoch from clock_timestamp()) as t')[0]['t']))
    status = {'authorized':True,'phone':'77001112233','authorized_at':dt.datetime.fromtimestamp(owner_reauth,dt.timezone.utc).isoformat()}
    route['op']='claim.status'
    start = pg.q(bind(Q(W,'Claim Start'),['legacy-victim','auth-review-attacker','','whatsapp']))[0]
    dec = code(W,'Claim Decide',{'json':{'statusCode':200,'body':status}},named={'Route':route,'Verify Install':inst,'Claim Start':start})[0]['json']
    assert expr(IF(W,'Bind Reauth?'),dec,{'Claim Decide':dec}) is True
    bound = pg.q(bind(Q(W,'Bind Reauthed Profile'),['legacy-victim','auth-review-attacker',dec['authorizedEpoch']]))
    owns = pg.q(bind(Q(W,'Reload Ownership'),['auth-review-attacker']))[0]
    results['R1_independent_legacy_owner_reauth']={'attacker_action':'request QR only; never scan/finish; no code submitted','external_status_fixture':'legacy owner independently reconnects same profile after A requested QR','pair_recorded':recorded,'decision':dec['claimMethod'],'bound':bound,'attacker_profiles':owns['p'],'attacker_workflows':owns['w']}
    assert owns['p']==['legacy-victim'] and owns['w']==['victim-workflow']

# R2: actual existing subscriber ID (synthetic) held by a live installation with no bots. A second
# arbitrary install naming that ID steals binding; both already-issued tokens continue to pass the gate.
R = '$RCAnonymousID:' + '0123456789abcdef'*2
with PgSession(SETUP) as pg:
    def bootstrap(install):
        route = code(W,'Route',{'json':{'body':{'op':'bootstrap','appUserId':R,'platform':'review','appVersion':'review'}}})[0]['json']
        # Route generates real random install id; label only for report.
        row = pg.q(bind(Q(W,'Create Install'),[route['installId'],route['secretHash'],route['appUserId'],route['platform'],route['appVersion']]))[0]
        token = code(W,'Issue Token',{'json':row},named={'Route':route})[0]['json']['token']
        allowed = code(G,'Auth Gate',{'json':{'headers':{'authorization':'Bearer '+token},'body':{'appUserId':R}}})[0]['json']['crAuth']
        return route,row,token,allowed
    pg.x(bind("insert into subscribers (app_user_id,plan,status,topup_balance) values ($1,'business','active',7);",[R]))
    vr,v,vtoken,vg = bootstrap('victim')
    ar,a,atoken,ag = bootstrap('attacker')
    still = code(G,'Auth Gate',{'json':{'headers':{'authorization':'Bearer '+vtoken},'body':{'appUserId':R}}})[0]['json']['crAuth']
    # New profile can be requested using stolen billing rights; the Build Request gate needs no owned bot.
    proxy = code(P,'Build Request',{'json':{'crAuth':ag,'body':{'op':'profile.add','channel':'whatsapp','name':'review-bot'}}})[0]['json']
    results['R2_no_live_bots_binding_takeover']={'victim_install':v['install_id'],'attacker_install':a['install_id'],'attacker_owned_profiles':a['p'],'attacker_billing_id':a['app_user_id'],'attacker_conflict':a['billing_conflict'],'attacker_GetUsage_gate':ag['ok'],'victim_old_GetUsage_gate':still['ok'],'attacker_profile_add_allowed':proxy['ok'],'bindings':pg.q('select app_user_id, install_id from billing_bindings'),'install_billing_after':pg.q('select install_id, app_user_id from installs')}
    assert a['app_user_id']==R and ag['ok'] and still['ok'] and proxy['ok']
    # Canonical profile.add registration plus reloaded token enables Create against this billing account.
    pg.q(bind(Q(P,'Bind Profile'),['attacker-profile',R,'whatsapp',a['install_id']]))
    reloaded=pg.q(bind(Q(W,'Reload Ownership'),[a['install_id']]))[0]
    tok=code(W,'Issue Token',{'json':reloaded},named={'Route':ar})[0]['json']['token']
    cr=code(C,'Auth Gate',{'json':{'headers':{'authorization':'Bearer '+tok},'body':{'WhatsappProfileId':'attacker-profile','TelegramWorkflowId':'-1','AppUserID':R}}})[0]['json']['crAuth']
    results['R2_no_live_bots_binding_takeover']['attacker_CreateWhatsappWorkflow_gate']=cr['ok']
    assert cr['ok']
    usage=pg.q(bind(Q(G,'Read Usage'),[R]))[0]
    bot=wf('4wYitz5ek30SVNlT-WhatsApp_Bot.json')
    quota=pg.q(bind(Q(bot,'Count Dialog'),['attacker-profile','attacker-customer-chat']))[0]
    debits=pg.q('select app_user_id,chat_id from dialog_counts')
    results['R2_no_live_bots_binding_takeover']['victim_usage_read']=usage
    results['R2_no_live_bots_binding_takeover']['attacker_chat_quota_result']=quota
    results['R2_no_live_bots_binding_takeover']['dialog_count_debited_to']=debits
    assert quota['allowed'] and debits[0]['app_user_id']==R

# R3: the request names its OWN workflow, but an existing legacy file ID belongs to a different workflow.
# Registry grants ownership solely because owner_id is null. The result reaches x-upsert storage and permits
# Delete File to remove victim chunks. All database state below is session-temp; the graph driver stubs HTTP.
FILE='22222222-2222-4222-8222-222222222222'
UPLOAD_SETUP='''create temp table uploaded_files (file_id text primary key, owner_id text, bot_wa_id text, bot_tg_id text, content_type text, file_name text, created_at timestamptz default now(), deleted_at timestamptz);
create temp table documents (id int primary key, metadata jsonb);'''
with PgSession(UPLOAD_SETUP) as pg:
    pg.x(bind("insert into uploaded_files (file_id,owner_id,bot_wa_id) values ($1,null,'victim-workflow');",[FILE]))
    pg.x(bind("insert into documents (id,metadata) values (1,jsonb_build_object('fileId',$1::text,'botWaId','victim-workflow'));",[FILE]))
    verdict=pg.q(bind(Q(U,'Register Upload'),[FILE,'auth-review-attacker','attacker-workflow','-1','products','replacement.txt']))[0]
    row=pg.q('select file_id,owner_id,bot_wa_id from uploaded_files')[0]
    scenario={'claims':{'i':'auth-review-attacker','u':R,'p':['attacker-profile'],'w':['attacker-workflow'],'c':[]},'fields':{'whatsappWorkflowId':'attacker-workflow','telegramWorkflowId':'-1','contentType':'products','fileId':FILE},'pg':{'Register Upload':verdict},'file':{'text':'Attacker replacement price\n','name':'replacement.txt','ext':'txt','mime':'text/plain'}}
    driver=ROOT/'Tools/n8n/tests/upload_chain.js'
    run=subprocess.run(['node',str(driver),'chain',str(ROOT/'Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json')],input=json.dumps({'ownerless_victim_file':scenario}),text=True,capture_output=True,check=True)
    chain=json.loads(run.stdout)['ownerless_victim_file']
    removed=pg.q(bind(Q(D,'Delete File Chunks'),[FILE,'session','auth-review-attacker',json.dumps(['attacker-workflow'])]))[0]
    results['R3_ownerless_foreign_file_takeover']={'registry_verdict':verdict,'row_after_adoption':row,'upload_error':chain['error'],'storage_request':chain['storeOriginal'],'side_effects':chain['sideEffects'],'delete_verdict':removed,'victim_chunks_remaining':pg.q('select count(*) as n from documents')[0]['n']}
    assert verdict['allowed'] and row['bot_wa_id']=='victim-workflow' and chain['storeOriginal']['gotFile'] and removed['allowed'] and removed['deletedChunks']==1

# R11: independently replay canonical Issue Token transient errors and explicit revoke.
results['R11_issue_token_fault_classification']={}
for label,row in [('db_error',{'error':'synthetic DB outage'}),('passed_through',{'op':'refresh','installId':'existing'}),('empty',{}),('revoked',{'verdict':'checked','install_id':None})]:
    out=code(W,'Issue Token',{'json':row},named={'Route':{'op':'refresh','nowSec':int(time.time())}})[0]['json']
    results['R11_issue_token_fault_classification'][label]=out
    assert out['_status']==(401 if label=='revoked' else 503)
print(json.dumps(results,ensure_ascii=False,indent=2))
