import json, sys
sys.path.insert(0, '/Users/ayan/Projects/Automation/Tools/n8n/tests')
from harness import node, run_js, wf
SUG = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
PREP_JS = node(SUG, 'Prep')['parameters']['jsCode']
ASM_JS = node(SUG, 'Assemble')['parameters']['jsCode']
def panel(items=None, catalog=None, rag=(), exact=(), q='Camry 2019, фильтр масляный 90915-YZZE1 сколько?'):
    b = {'v': 1, 'requestSeq': 1, 'chatId': 'v9@c.us', 'appUserId': 'v9-owner', 'profileId': 'v9-prof', 'businessTypeId': 'auto_parts',
         'businessName': 'V9', 'ownerPrompt': '', 'messages': [{'role': 'client', 'text': q, 'ts': 9}],
         'channel': 'whatsapp', 'botWaId': 'wfV9price00001', 'botTgId': '-1'}
    if items is not None:
        b['knowledge'] = {'v': 1, 'version': 'v9', 'timezone': 'Asia/Almaty', 'tzSource': 'bot', 'description': 'Запчасти', 'contacts': {}, 'items': items, 'documents': 1}
    if catalog is not None:
        b['catalog'] = catalog
    p = run_js(PREP_JS, {'json': {'body': b}})[0]['json']
    named = {'Prep': p, 'Retrieve RAG': [{'document': {'pageContent': d}} for d in rag] or [{}], 'Exact Match': [{'content': d} for d in exact] or [{}]}
    a = run_js(ASM_JS, {'json': {}}, named)[0]['json']
    fenced = json.loads(a['fencedData'].split('\n', 1)[1])
    return p, a, fenced
ITEMS = [{'kind': 'product', 'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг', 'description': 'Оригинал'}]
filler = lambda n, tag: '\n'.join(f'Позиция {tag}{i} — {1000+i} тг' for i in range(n))
# 4 chunks of ~950 chars; the conflicting row is the LAST line of chunk 4
chunks = [filler(40, 'a'), filler(40, 'b'), filler(40, 'c'), filler(38, 'd') + '\nФильтр масляный 90915-YZZE1 — 3900 тг']
print('chunk lens', [len(c) for c in chunks], 'sum+seps', sum(len(c) for c in chunks) + 5*3)
p, a, f = panel(ITEMS, rag=chunks)
rc = f['ragChunks']
print('ragChunks len', len(rc))
print('TAIL:', repr(rc[-260:]))
print('rule pushed:', 'РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'], '| full mark present:', 'Цены расходятся.]' in rc, '| conflict row present:', '90915-YZZE1 — 3900' in rc)
# without the reconcile the conflicting row itself: is it inside the 4000 window?
raw = '\n---\n'.join(chunks)
print('raw join len', len(raw), 'row inside 4000 before V9:', raw[:4000].endswith('3900 тг') or '90915-YZZE1 — 3900 тг' in raw[:4000])
# content displaced by marks: a chunk set where marks push the last price row out
chunks2 = ['Фильтр масляный 90915-YZZE1 — 3900 тг\n' * 1 + filler(30, 'e'), 'Колодки 04465-02220 — 18500 тг\n' + filler(35, 'f'), filler(36, 'g'), filler(40, 'h') + '\nСвеча NGK BKR6E — 1500 тг']
raw2 = '\n---\n'.join(chunks2)
print('raw2 len', len(raw2))
p, a, f = panel(ITEMS + [{'kind': 'product', 'name': 'Колодки Toyota 04465-02220', 'price': '19000 тг'}], rag=chunks2)
print('last row before V9 visible:', 'BKR6E — 1500' in raw2[:4000], '| after V9 visible:', 'BKR6E — 1500' in f['ragChunks'])
print('---- sized to the 4000 cap ----')
row = 'Фильтр масляный 90915-YZZE1 — 3900 тг'
base = [filler(40, 'a'), filler(40, 'b'), filler(40, 'c')]
pad = 3990 - (sum(len(x) for x in base) + 3*5 + len(row) + 1)
c4 = ('x' * pad) + '\n' + row
chunks3 = base + [c4]
raw3 = '\n---\n'.join(chunks3)
print('raw3 len', len(raw3), '| row fully inside 4000 before V9:', raw3[:4000].endswith(row))
p, a, f = panel(ITEMS, rag=chunks3)
rc = f['ragChunks']
print('after: len', len(rc), '| tail:', repr(rc[-120:]))
print('rule pushed:', 'РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'], '| complete mark visible:', 'Цены расходятся.]' in rc)
# mark displaces the next chunk's content
row1 = 'Фильтр масляный 90915-YZZE1 — 3900 тг'
c1 = row1 + '\n' + filler(30, 'e')
last = 'Свеча NGK BKR6E — 1500 тг'
mid = [filler(40, 'f'), filler(40, 'g')]
pad = 3995 - (len(c1) + sum(len(x) for x in mid) + 3*5 + len(last) + 1)
c4 = ('y' * pad) + '\n' + last
chunks4 = [c1] + mid + [c4]
raw4 = '\n---\n'.join(chunks4)
print('raw4 len', len(raw4), '| last row visible before V9:', last in raw4[:4000])
p, a, f = panel(ITEMS, rag=chunks4)
print('| last row visible after V9:', last in f['ragChunks'])
