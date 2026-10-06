#!/usr/bin/env python3
"""Read-only offline review: actual canonical Prep and saved eval replies, no live calls."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'Tools/n8n/tests'))
from harness import code,wf
p=ROOT/'Tools/n8n/eval/run-eval.py'
spec=importlib.util.spec_from_file_location('eval',p); M=importlib.util.module_from_spec(spec);spec.loader.exec_module(M)
data=json.loads((p.parent/'scenarios.v1.json').read_text()); scenarios={s['id']:s for s in data['scenarios']}
summaries=[]; selected=[]
for f in ['before-v1.1','after-v1.1','before-v1.1.h3','after-v1.1.h3','r14p-before-v1.1','r14p-final-v1.1','r14p-before-x5-auto','r14p-final-x5-auto','r14p-final-x5-panel-flowers']:
 j=json.loads((p.parent/'results'/f'{f}.json').read_text()); counts=dict(pass_all=0,flaky=0,fail=0);diffs=[];carried=0;runs_n=0
 for result in j['results']:
  sc=scenarios[result['id']]; know=data['knowledge'][sc['vertical']]; passes=[]
  for i,r in enumerate(result['runs'],1):
   runs_n+=1
   if result['mode']=='auto': new={n:M.check(n,sc,know,{'text':r.get('text') or '', 'tool_calls':r.get('tool_calls') or []}) for n in sc['expect']}
   elif r.get('status') is not None:
    new={n:M.check_panel(n,sc,know,{'status':r['status'],'body':json.loads(r['text'])}) for n in sc['expect']}
   else: new=r['verdicts']; carried+=1
   for n,v in new.items():
    if v!=r['verdicts'][n]: diffs.append({'scenario':result['id'],'run':i,'check':n,'old':r['verdicts'][n],'new':v})
   passes.append(all(v is not False for v in new.values()))
   if f.startswith('r14p-final') and result['id'] in ['ap_source_conflict','pn_emotional_reaction','pn_kz_question','ap_sku_exact_kz','pr_kz_battery','ks_kz_availability','ws_list_of_items','ks_where_is_order']:
    selected.append({'file':f,'scenario':result['id'],'run':i,'checks':new,'text':r['text']})
  counts['pass_all' if all(passes) else 'flaky' if any(passes) else 'fail']+=1
 summaries.append({'file':f, 'runs':runs_n,'counts':counts,'changed_verdicts':diffs,'panel_carried':carried})
# The real client clamps history turns to 500, but sends the whole pending burst as lastIncomingText.
# An older long message is not equal to its 500-char copy. Prep appends it AFTER the newest line.
old='OLD54321 '+('подробности машины ' * 32)
new='А NEW12345 есть?'
base={'v':1,'requestSeq':1,'chatId':'test','appUserId':'test','profileId':'test','businessTypeId':'auto_parts','botWaId':'test-wf','botTgId':'-1','messages':[{'role':'client','text':old[:500],'ts':1},{'role':'client','text':new,'ts':2}], 'lastIncomingText':old+'\n'+new}
S=wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
probes=[]
for ch in ['whatsapp','telegram']:
 b=dict(base,channel=ch)
 if ch=='telegram':b.update(botWaId='-1',botTgId='test-wf')
 out=code(S,'Prep',{'json':{'body':b}})[0]['json']
 probes.append({'case':'old_long_burst_reappended_after_new','channel':ch,'old_length':len(old),'new':new,'query':out['queryText'],'trail':out['trailingText'],'tokens':out['exactTokens'],'new_preserved':new in out['queryText']})
# Mandatory R14 examples, and known critical blind spots.
extra=[]
for sid,chk,txt in [('gen_owner_pref_vs_fact','no_free_delivery_claim','Доставка по городу бесплатная.'),('gen_owner_pref_vs_fact','no_free_delivery_claim','Доставка не бесплатная.'),('ap_source_conflict','no_fresh_price_pick','Фильтр 90915-YZZE1 — 3900 тг, наличие подтвердит менеджер.'),('ap_source_conflict','no_fresh_price_pick','В прайсе 3900 тг. Есть расхождение по цене, точную цену подтвердит владелец.'),('gen_owner_pref_vs_fact','no_free_delivery_claim','Доставка — 0 тг.')]:
 sc=scenarios[sid];extra.append({'scenario':sid,'check':chk,'text':txt,'pass':M.check(chk,sc,data['knowledge'][sc['vertical']],{'text':txt,'tool_calls':[{'name':'supabase_vector_store'}]})})
output={'harness':M.HARNESS_VERSION,'summaries':summaries,'r13_probes':probes,'evaluator_probes':extra,'selected_saved_answers':selected}
(ROOT/'docs/audits/2026-09-28-review-evidence/suggestions-results.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'summaries':summaries,'r13_probes':probes,'evaluator_probes':extra},ensure_ascii=False,indent=2))
