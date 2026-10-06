#!/usr/bin/env python3
"""Local-only proof using actual Route, pg-promise and Create Install. Temp tables + ROLLBACK."""
import json,sys,subprocess,pathlib
sys.path.insert(0,'Tools/n8n/tests')
from harness import wf,node,code,sql_run,module
w=wf('crAuthSession001-Auth_Session.json')
value='review-billing\n; SELECT 929 AS independent_bootstrap_sentinel; --'
r=code(w,'Route',{'json':{'body':{'op':'bootstrap','appUserId':value}}})[0]['json']
q=node(w,'Create Install')['parameters']['query']
js="""const fs=require('fs'),path=require('path'),os=require('os'); const pgp=require(require.resolve('pg-promise',{paths:[path.join(os.homedir(),'.n8n-staging/app/node_modules/n8n-nodes-base')]}))(); const x=JSON.parse(fs.readFileSync(0,'utf8'));process.stdout.write(pgp.as.format(x.q,x.values));"""
p=subprocess.run(['node','-e',js],input=json.dumps({'q':q,'values':[r['installId'],r['secretHash'],r['appUserId'],'test','test']}),capture_output=True,text=True,check=True)
setup='CREATE TEMP TABLE installs (LIKE public.installs INCLUDING ALL); CREATE TEMP TABLE bot_profiles (LIKE public.bot_profiles INCLUDING ALL); CREATE TEMP TABLE billing_bindings(app_user_id text PRIMARY KEY,install_id text,bound_at timestamptz default now());'
res=sql_run(p.stdout,setup)
out={'route_invalid':r['invalid'],'newline_preserved': '\n' in r['appUserId'],'sentinel_executed_as_separate_sql':'929' in res.splitlines(),'unauthenticated_endpoint':'AuthSession bootstrap','scope':'local TEMP tables + ROLLBACK; no live webhook invoked'}
u=module('update-clones.py'); status,live=u.call(u.DEFAULT_BASE,'GET','/workflows/crAuthSession001')
if status==200:
 pq=node(live['activeVersion'],'Create Install')['parameters']['query']; out['published_has_new_vulnerable_comment']='billing id ($3)' in pq
status,clone=u.call(u.DEFAULT_BASE,'GET','/workflows/dt6hHFUVrW07unTj')
if status==200:
 out['current_clone']={'id':clone['id'],'name':clone['name'],'active':clone['active'],'archived':clone.get('isArchived'), 'graph_stamp':u.graph_stamp(clone),'canonical_stamp':u.graph_stamp(u.canonical('whatsapp'))}
pathlib.Path('docs/audits/2026-09-28-review-evidence/bootstrap-comment-proof.json').write_text(json.dumps(out,ensure_ascii=False,indent=2)+'\n'); print(json.dumps(out,ensure_ascii=False,indent=2))
