#!/usr/bin/env python3
"""Independent audit reproductions. Run from any directory with Python 3 + Node.

Reads committed workflows; executes their Code bodies against synthetic inputs.
SQL uses the existing LOCAL staging PostgreSQL, session-local temporary tables,
and ROLLBACK. No n8n HTTP requests, LLM calls, Wappi sends or production writes.
Output records current behaviour; it is not a suite declaring that behaviour correct.
"""
import importlib.util
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
WF = ROOT / 'Tools/n8n/workflows'


def workflow(name):
    return json.loads((WF / name).read_text())


def node(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def run_js(js, item, named=None):
    driver = r'''const fs=require('fs'); const v=JSON.parse(fs.readFileSync(0,'utf8'));
const fn=new Function('$input','$','$env','require','Buffer','$json','$execution',v.js);
const result=fn({first:()=>v.item,all:()=>[v.item]},
 name=>({first:()=>({json:(v.named||{})[name]||{}})}),
 {CR_AUTH_SECRET:'synthetic-review-secret',CR_AUTH_COMPAT_UNTIL:'2000-01-01'},
 require,Buffer,v.item.json,{id:'synthetic-review'});
process.stdout.write(JSON.stringify(result));'''
    r = subprocess.run(['node', '-e', driver], input=json.dumps({'js': js, 'item': item, 'named': named or {}}),
                       text=True, capture_output=True, check=True)
    return json.loads(r.stdout)


def code(w, name, item, named=None):
    return run_js(node(w, name)['parameters']['jsCode'], item, named)


def module(filename):
    spec = importlib.util.spec_from_file_location('review_module', ROOT / 'Tools/n8n' / filename)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def sql_literal(v):
    if v is None:
        return 'NULL'
    if isinstance(v, bool):
        return 'true' if v else 'false'
    if isinstance(v, (int, float)):
        return str(v)
    return "'" + v.replace("'", "''") + "'"


def bind(query, values):
    return re.sub(r'\$(\d+)', lambda m: sql_literal(values[int(m[1]) - 1]), query) + '\n'


out = {'scope': 'synthetic Code nodes + local PostgreSQL temporary tables'}
auth = workflow('crAuthSession001-Auth_Session.json')
usage = workflow('jtbssfzXbOxwTK4k-Get_Usage.json')
upload = workflow('KoTuIlk4LMrlvnWI-Upload_File.json')
route = code(auth, 'Route', {'json': {'body': {'op': 'bootstrap', 'appUserId': 'review-victim-billing'}}})[0]['json']
issued = code(auth, 'Issue Token', {'json': {'install_id': route['installId'], 'app_user_id': route['appUserId'],
                    'p': [], 'w': [], 'c': [], 'token_version': 1}}, {'Route': route})[0]['json']
headers = {'authorization': 'Bearer ' + issued['token']}
gated = code(usage, 'Auth Gate', {'json': {'headers': headers, 'body': {'appUserId': 'review-victim-billing'}}})[0]
out['billing_self_declared_accepted_compat_closed'] = gated['json']['crAuth']['ok']
up = code(upload, 'Auth Gate', {'json': {'headers': headers, 'body': {}},
           'binary': {'data': {'data': 'c3ludGhldGlj', 'fileName': 'price.txt', 'fileExtension': 'txt'}}})[0]
out['upload_binary_survives_gate'] = 'binary' in up
out['refresh_db_error_response'] = code(auth, 'Issue Token', {'json': {'error': 'synthetic DB outage'}},
                                      {'Route': {'op': 'refresh'}})[0]['json']
claim = code(auth, 'Claim Decide', {'json': {'statusCode': 200, 'body': {'authorized': True,
              'phone': '77000000000', 'authorized_at': '2026-09-24T10:01:00Z'}}},
             {'Route': {'op': 'claim.status', 'profileId': 'review-victim', 'channel': 'whatsapp'},
              'Verify Install': {'install_id': 'review-attacker'},
              'Claim Start': {'profile_id': 'review-victim', 'owner_id': 'review-owner',
                              'claim_pending_epoch': 1790244000}})[0]['json']
out['unrelated_pairing_claim_method'] = claim['claimMethod']

suggest = workflow('9PTyYcelRQI7bGDb-Suggest_Replies.json')
base = {'v': 1, 'chatId': 'review', 'appUserId': 'review', 'botWaId': 'review-bot',
        'messages': [{'role': 'client', 'text': 'Ремонт ZX98765 сколько стоит?'}]}
description = ('Диагностика. ' * 18) + 'Деталь оплачивается отдельно.'
prep = code(suggest, 'Prep', {'json': {'body': {**base, 'knowledge': {'items': [
    {'name': 'Ремонт ZX98765', 'price': '15000', 'description': description}]}}}})[0]['json']
out['description'] = {'length': len(description), 'condition_preserved': 'Деталь оплачивается отдельно' in prep['catalog'],
                      'flags': prep['knowledgeFlags']}
items = [{'name': f'Товар {i} ' + ('X' * 100), 'price': '1000', 'description': 'Описание'} for i in range(31)]
items.append({'name': 'ZX98765', 'price': '3200', 'description': 'Нужная позиция'})
prep = code(suggest, 'Prep', {'json': {'body': {**base, 'knowledge': {'items': items}}}})[0]['json']
out['selected_item'] = {'preserved': 'ZX98765' in prep['catalog'], 'flags': prep['knowledgeFlags']}
prep = code(suggest, 'Prep', {'json': {'body': {**base, 'steerTowardText': 'Искомая модель NEW12345',
    'messages': [{'role': 'client', 'text': 'OLD54321 ' + 'А' * 490},
                 {'role': 'business', 'text': 'Б' * 500}, {'role': 'client', 'text': 'а сколько?'}]}}})[0]['json']
out['retrieval_query'] = {'length': len(prep['queryText']), 'latest_preserved': 'а сколько?' in prep['queryText'],
                          'steer_preserved': 'NEW12345' in prep['queryText'], 'exactTokens': prep['exactTokens']}
test = workflow('crTestDialog0001-Test_Dialog.json')
agent = lambda s: {'name': 'AI Agent', 'parameters': {'options': {'systemMessage': s}}}
comp = code(test, 'Compose', {'json': {'nodes': [agent('DRAFT PRICE 999')],
                'activeVersion': {'nodes': [agent('PUBLISHED PRICE 3200')]}}},
            {'Validate': {'history': [], 'text': 'Цена?'}})[0]['json']
out['test_dialog_selected_prompt'] = comp['systemMessage']

wa = workflow('4wYitz5ek30SVNlT-WhatsApp_Bot.json')
tg = workflow('4VN3gsFaC2HUYmcc-Telegram_Bot.json')
for name in ['Claim Event', 'Acquire Send Lease', 'Record Send', 'Record Cancel']:
    assert node(wa, name)['parameters']['query'] == node(tg, name)['parameters']['query']
query = lambda name, values: bind(node(wa, name)['parameters']['query'], values)
sql = '''begin;
create temp table reply_events (profile_id text,chat_id text,message_id text,received_at timestamptz default now(),updated_at timestamptz default now(),state text,execution_id text,transport_message_id text,error text,primary key(profile_id,chat_id,message_id));
create temp table chat_reply_state(profile_id text,chat_id text,incoming_generation bigint default 0,lock_token text,lock_expires_at timestamptz,last_sent_at timestamptz,last_sent_message_id text,handoff_paused bool default false,primary key(profile_id,chat_id));
create temp table n8n_chat_histories(id serial primary key,session_id varchar(255),message jsonb);
create temp table chat_history_events(profile_id text,chat_id text,message_id text,source text,body text,msg_type text,sent_at timestamptz,execution_id text,unique(profile_id,chat_id,message_id));
create temp table reply_mode_flags(profile_id text,chat_id text,suppressed bool,version bigint,updated_at timestamptz default now(),primary key(profile_id,chat_id));
create temp table handoff_requests(profile_id text,chat_id text,status text,done_at timestamptz,updated_at timestamptz);
create temp table bot_profiles(profile_id text,owner_id text,claimed_at timestamptz,claim_pending_by text,claim_pending_at timestamptz);
set local search_path=pg_temp, public;
select 'CLAIM TRANSFER';
insert into bot_profiles values('review-victim','review-owner',null,'review-attacker','2026-09-24 10:00:00+00');
'''
auth_module = module('build-auth-workflows.py')
sql += bind(auth_module.BIND_REAUTH_SQL, ['review-victim', 'review-attacker', 1790244060])
sql += "select owner_id from bot_profiles;\nselect 'STALE RESUME';\n"
sql += "insert into reply_mode_flags values('review','resume',false,200,now()); insert into chat_reply_state(profile_id,chat_id,handoff_paused) values('review','resume',true); insert into handoff_requests values('review','resume','open',null,now());\n"
sql += bind(module('apply-reply-mode-v2.py').UPSERT_SQL, ['["review"]', 'resume', False, 100])
sql += "select handoff_paused from chat_reply_state where chat_id='resume'; select status from handoff_requests;\nselect 'CANCEL DELETES NEWER SENT MEMORY';\n"
sql += query('Claim Event', ['review','memory','old','old-run','old question','chat',100])
sql += "insert into n8n_chat_histories(session_id,message) values ('review:memory','{\"type\":\"ai\",\"content\":\"old draft\",\"additional_kwargs\":{}}');\n"
sql += query('Claim Event', ['review','memory','new','new-run','new question','chat',101])
sql += "insert into n8n_chat_histories(session_id,message) values ('review:memory','{\"type\":\"ai\",\"content\":\"new SENT reply\",\"additional_kwargs\":{}}');\n"
sql += query('Record Send', ['review','memory','new','sent','transport-id','','new-run','new SENT reply'])
sql += query('Record Cancel', ['review','memory','old','superseded',0,True,'old-run',False,''])
sql += "select count(*) as memory_rows_left from n8n_chat_histories where session_id='review:memory';\nselect 'FAILED SEND REMAINS IN MEMORY';\n"
sql += query('Claim Event', ['review','failed','failed','failed-run','question','chat',100])
sql += "insert into n8n_chat_histories(session_id,message) values ('review:failed','{\"type\":\"ai\",\"content\":\"never delivered\",\"additional_kwargs\":{}}');\n"
sql += query('Record Send', ['review','failed','failed','send_failed','','http 400','failed-run','never delivered'])
sql += "select count(*) as assistant_memory_rows from n8n_chat_histories where session_id='review:failed';\nselect 'OLDER EVENT INVALIDATES LATEST';\n"
sql += query('Claim Event', ['review','order','latest','latest-run','latest','chat',101])
sql += query('Claim Event', ['review','order','older','older-run','older','chat',100])
sql += query('Acquire Send Lease', ['review','order','latest',1,'latest-run'])
sql += "select 'CRASHED SEND STAYS PENDING'; insert into reply_events(profile_id,chat_id,message_id,state,execution_id,updated_at) values('review','crash','message','send_pending','dead',now()-interval '1 day');\n"
sql += query('Claim Event', ['review','crash','message','retry','question','chat',100])
sql += "select state from reply_events where chat_id='crash'; rollback;\n"
r = subprocess.run([str(ROOT/'Tools/n8n/staging/pg.sh'), 'psql', '-X', '-q', '-P', 'pager=off'],
                   input=sql, text=True, capture_output=True)
out['sql_exit'] = r.returncode
out['sql_output'] = r.stdout + r.stderr
out['wa_tg'] = {}
rows = [dict(id='latest',chatId='order',fromMe=False,type='chat',body='latest',time=101),
        dict(id='older',chatId='order',fromMe=False,type='chat',body='older',time=100)]
for channel, w in [('wa', wa), ('tg', tg)]:
    results = {}
    for row in rows:
        v = code(w, 'Latest+Combine', {'json': {'messages': rows}}, {'Webhook': {'body': {'messages': [row]}}})[0]['json']
        results[row['id'] + '_aborts'] = v['abort']
    v = code(w, 'Reconcile Check', {'json': {'messages': [dict(id='foreign',chatId='OTHER_CHAT',fromMe=True,body='Спасибо',time=100)]}},
             {'Settle Send': {'text': 'Спасибо', 'sendStartedAt': 100, 'error': 'lost response'}})[0]['json']
    results['foreign_echo'] = v
    out['wa_tg'][channel] = results

out['eval_saved_examples'] = [r for r in json.loads((ROOT/'Tools/n8n/eval/results/after-v1.1.json').read_text())['results']
                            if r['id'] in ['ap_source_conflict', 'gen_owner_pref_vs_fact']]
print(json.dumps(out, ensure_ascii=False, indent=2))
