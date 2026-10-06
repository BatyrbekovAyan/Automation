#!/usr/bin/env python3
"""Read-only review of live workflow source; synthetic inputs, session temp tables only.
No Wappi/n8n network calls; no shared staging rows are changed.
"""
import json, subprocess, sys, time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'Tools/n8n/tests'))
from harness import PgSession, bind, code, node, wf
from reply_chain import SETUP,TEMPLATES,CHATS,Run,msg,memory,ledger,record_outgoing
out=[]
for ch,w in TEMPLATES.items():
    # R6: message B has become known before the last send gate; same-second A is still admitted.
    with PgSession(SETUP) as pg:
        a,b=msg(ch,'A',100,'Сколько стоит?'),msg(ch,'B',100,'Нет, отмените заказ.')
        ra,rb=Run(w,pg,a,'A'),Run(w,pg,b,'B')
        ra.claim();ra.latest([a]);ra.generate('Ваш заказ принят.');ra.final([a])
        rb.claim()
        lease=ra.lease()
        if lease.get('allowed'): ra.send_ok('sent-A')
        rb.latest([msg(ch,'sent-A',101,'Ваш заказ принят.',from_me=True),b,a])
        out.append({'case':'R6 same-second newer claim after final snapshot','channel':ch,'old_lease':lease,'outcomes':ledger(pg,ch)})
    # R7: an actually lost message remains an unmarked assistant statement in model memory.
    with PgSession(SETUP) as pg:
        a=msg(ch,'UNKNOWN',100,'Когда доставка?');r=Run(w,pg,a,'unknown')
        r.claim();r.latest([a]);r.generate('Доставка завтра в 10:00.');r.final([a]);r.lease()
        r.send({'error':{'message':'socket hang up'}},reconcile_snapshot=[a])
        out.append({'case':'R7 unknown delivery retained as ordinary assistant statement','channel':ch,'ledger':ledger(pg,ch),'memory':memory(pg,ch)})
    # R7: original event's LLM finishes last with the same text. The new delivered row, not the old
    # draft, is the first matching row above the old watermark. A marked owner row makes order visible.
    with PgSession(SETUP) as pg:
        a,b=msg(ch,'OLD',100,'Запишите на вторник.'),msg(ch,'NEW',130,'Лучше на среду.')
        ra,rb=Run(w,pg,a,'old'),Run(w,pg,b,'new');answer='Запрос передан менеджеру.'
        ra.claim();ra.latest([a]);rb.claim();rb.latest([b,a]);rb.generate(answer);rb.final([b,a]);rb.lease();rb.send_ok('new-sent')
        sent_memory=memory(pg,ch)
        record_outgoing(w,pg,ch,'owner-msg','Среда занята, уточню другой день.',140,'outgoing_message_phone')
        ra.generate(answer);before=memory(pg,ch);ra.final([b,a]);after=memory(pg,ch)
        sent_ai_id=next(r['id'] for r in sent_memory if r['type']=='ai')
        out.append({'case':'R7 same output reversed LLM completion deletes delivered row','channel':ch,'sent_ai_id':sent_ai_id,'sent_ai_survives':any(r['id']==sent_ai_id for r in after),'cancel':ra.cancel,'before':before,'after':after})
    # R10: no confirmation from transport is available. There is exactly one manual/foreign row.
    t=1790000000;trig=msg(ch,'TRIGGER',t,'Фото',mtype='image');text='Напишите, пожалуйста, текстом.'
    settle={'state':'send_unknown','text':text,'sendStartedAt':t+30,'error':'socket hang up'}
    scenarios={
      'sole unmarked manual reply':msg(ch,'manual-only',t+31,text,from_me=True),
      'explicit foreign profile same chat':dict(msg(ch,'foreign-prof',t+31,text,from_me=True),profile_id='OTHER_PROFILE'),
      'outgoing_phone type marker':dict(msg(ch,'manual-marker',t+31,text,from_me=True),wh_type='outgoing_message_phone'),
    }
    for title,row in scenarios.items():
        res=code(w,'Reconcile Check',{'json':{'messages':[row,trig]}},{'Webhook':{'body':{'messages':[trig]}},'Settle Send':settle})[0]['json']
        out.append({'case':'R10 '+title,'channel':ch,'result':res,'candidate':row})
# R4: a queued action from a five-minutes-fast phone precedes a new handoff in real time.
srm=wf('SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json');hq=wf('crHandoffReq0001-Handoff_Request.json')
setup='''create temp table reply_mode_flags (like public.reply_mode_flags including all);
create temp table chat_reply_state (like public.chat_reply_state including all);
create temp table handoff_requests (like public.handoff_requests including all);'''
with PgSession(setup) as pg:
    now=int(time.time()*1000);p='review-clock';chat='review-chat'
    pg.q(bind(node(hq,'Upsert Handoff')['parameters']['query'],[p,chat,'new-handoff','question','{}','synthetic']))
    result=pg.q(bind(node(srm,'Upsert Reply Mode')['parameters']['query'],[json.dumps([p]),chat,False,now+5*60000]))
    out.append({'case':'R4 phone five minutes ahead closes request created after actual decision','response':result,'requests':pg.q('select status from handoff_requests'),'state':pg.q('select handoff_paused from chat_reply_state')})
# R8: the graph treats failed cleanup as terminal failure without preserving clone id.
for ch,file,field in [('WA','XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json','WhatsappProfileId'),('TG','Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json','TelegramProfileId')]:
    sc={'cleanup_fails':{'body':{field:'review-profile','ReplyMode':'semi','ReplyModeVersion':'123','AppUserID':'$RCAnonymousID:review','BotKey':'review'},'createdId':'review-orphan-clone','seed':[{'queryError':'database unavailable'}],'http':{'Delete Unseeded Workflow':{'error':{'message':'503 temporarily unavailable'}}}}}
    run=subprocess.run(['node',str(ROOT/'Tools/n8n/tests/create_seed_chain.js'),str(ROOT/'Tools/n8n/workflows'/file)],input=json.dumps(sc),text=True,capture_output=True,check=True)
    out.append({'case':'R8 seed fail plus delete fail leaves no recoverable clone id in response','channel':ch,'graph':json.loads(run.stdout)})
print(json.dumps(out,ensure_ascii=False,indent=2))
