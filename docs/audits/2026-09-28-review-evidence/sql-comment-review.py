#!/usr/bin/env python3
"""Read-only live definition check + local TEMP-table proof. Never invokes a live webhook."""
import json,sys,subprocess,pathlib,re
sys.path.insert(0,'Tools/n8n/tests')
from harness import module,wf,node,code,sql_run
out=pathlib.Path('docs/audits/2026-09-28-review-evidence')
w=wf('ZTqpumOpL1rNDOp6-Delete_File.json'); n=node(w,'Delete File Chunks'); query=n['parameters']['query']
value='review-nonexistent-file\n; SELECT 928 AS independent_review_sentinel; --'
gate=code(w,'Auth Gate',{'json':{'headers':{},'body':{'fileId':value}}},env={'CR_AUTH_COMPAT_UNTIL':'2099-01-01'})
js="""const fs=require('fs'), path=require('path'), os=require('os');
const base=path.join(os.homedir(),'.n8n-staging/app/node_modules/n8n-nodes-base');
const pgp=require(require.resolve('pg-promise',{paths:[base]}))();
const x=JSON.parse(fs.readFileSync(0,'utf8')); process.stdout.write(pgp.as.format(x.query,x.values));"""
r=subprocess.run(['node','-e',js],input=json.dumps({'query':query,'values':[value,'legacy','','[]']}),text=True,capture_output=True,check=True)
setup='CREATE TEMP TABLE documents(id bigserial, metadata jsonb); CREATE TEMP TABLE uploaded_files(file_id text,owner_id text,deleted_at timestamptz);'
local=sql_run(r.stdout,setup)
u=module('update-clones.py'); status,live=u.call(u.DEFAULT_BASE,'GET','/workflows/ZTqpumOpL1rNDOp6')
if status!=200: raise RuntimeError('readback failed '+str(status))
pub=live.get('activeVersion') or {}; pn=node(pub,'Delete File Chunks')
comments=[]
for path in pathlib.Path('Tools/n8n/workflows').glob('*.json'):
 doc=json.loads(path.read_text())
 for nd in doc['nodes']:
  q=nd.get('parameters',{}).get('query','')
  if not isinstance(q,str): continue
  lines=[x for x in q.splitlines() if re.search(r'--.*\$\d',x)]
  if lines: comments.append({'workflow':path.name,'node':nd['name'],'lines':lines})
result={'gate_without_token_in_open_compat':gate[0]['json'].get('crAuth'), 'local_postgres_result':local.strip(),'sentinel_executed_as_separate_sql': '928' in local.splitlines(),'published_active':live['active'],'published_query_matches_local':pn['parameters']['query']==query,'published_version':live.get('activeVersionId'),'live_webhook_invoked':False,'method':'Installed pg-promise formats actual workflow SQL; execute only in local Postgres with TEMP tables and ROLLBACK. Sentinel is a read-only SELECT.','other_parameter_comments':comments}
(out/'sql-comment-proof.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(result,ensure_ascii=False,indent=2))
