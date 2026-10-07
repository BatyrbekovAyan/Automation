#!/usr/bin/env python3
"""V9 (iteration 2026-09-28): a catalog/price-list price conflict is reconciled AT SEARCH TIME — for the bot (Авто), the
Test Dialog and the «Вместе» panel — against the catalog of the bot's PUBLISHED prompt, without a table and without
rewriting `documents` (owner decision, iteration README).

Runs the REAL Code bodies of the canonical workflows (the Price List Search sub-workflow's Guard / Reconcile Prices, the
Suggest Replies Prep / Assemble, Set Workflow State's Validate) and asserts the REAL wiring of the templates (WA + TG),
the Test Dialog, the staging derivation and the eval runner's simulated search:
  * the scenario ap_source_conflict (catalog 3200, price list 3900) gets exactly one mark under the row, naming the item
    and both prices, «Цены расходятся»; the base documents of all six verticals come back byte for byte;
  * the converter's columns (all price columns — опт/розн, «Цена, тг:»), OCR rows, PDF text without a currency, a price
    before the code, «тг/шт» catalog prices — marked; «от», a range — a soft mark with the catalog price only; the price
    on the next line, a longer code, an item without an article, «договорная», a grade/size (5W-30, 205/55R16), an
    analogue mention — never «Цены расходятся»; a same-article accessory — a soft «may be another item» mark;
  * idempotent (a second pass and a forged mark line), a corrected catalog clears the mark, `]` in a name is escaped;
  * the bot tool and the Test Dialog tool call the sub-workflow with the right ids, filter key and prompt; the panel
    marks its RAG and exact-match rows with the SAME function and tells the model what a mark means.

    python3 Tools/n8n/tests/test_price_search.py
"""
import copy
import importlib.util
import json
import os

from harness import Case, N8N, has_node, module, node, report, run_js, wf

SEARCH_FILE = 'crPriceSearch001-Price_List_Search.json'
BOTS = {'WA': ('4wYitz5ek30SVNlT-WhatsApp_Bot.json', 'botWaId'), 'TG': ('4VN3gsFaC2HUYmcc-Telegram_Bot.json', 'botTgId')}
DATA = json.load(open(os.path.join(N8N, 'eval', 'scenarios.v1.json'), encoding='utf-8'))
SC = {s['id']: s for s in DATA['scenarios']}
MARK = '[Сверка с каталогом: '


def load(name):
    try:
        return wf(name)
    except FileNotFoundError:
        return None


SEARCH = load(SEARCH_FILE)
SUG = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
TD = wf('crTestDialog0001-Test_Dialog.json')
RESULT_JS = node(SEARCH, 'Reconcile Prices')['parameters']['jsCode'] if SEARCH else None
GUARD_JS = node(SEARCH, 'Guard')['parameters']['jsCode'] if SEARCH else None


def compose(vertical, products, services):
    """The clone's system message exactly as Create WA/TG `Set Fields` composes it (the eval runner's mirror)."""
    know = DATA['knowledge'][vertical]
    return (f"Business Type: {know['businessType']}\n\n<vertical prompt>\n\nAdditional Instructions:   \n\n"
            f"About Business: {know['business']}\n\nProducts:\n{products}\n\nServices:\n{services}")


def prompt_of(vertical, products=None, services=None):
    know = DATA['knowledge'][vertical]
    return compose(vertical, know['products'] if products is None else products, know['services'] if services is None else services)


def search(docs, prompt, ok=True, error=False):
    """The REAL Reconcile Prices body over a vector-store result (load mode: {document: {pageContent, metadata}, score})."""
    hits = [{'document': {'pageContent': d, 'metadata': {'botWaId': 'wfV9price00001'}}, 'score': 0.8} for d in docs]
    if error:
        hits = [{'error': 'supabase: timeout'}]
    if not hits:
        hits = [{}]                                   # alwaysOutputData on an empty result
    named = {'Guard': {'ok': ok, 'query': 'q', 'filterKey': 'botWaId', 'botId': 'wfV9price00001', 'prompt': prompt},
             'Search Price Lists': hits}
    return run_js(RESULT_JS, {'json': {}}, named)[0]['json']


def one(doc, prompt):
    return search([doc], prompt)['documents'][0]


def marks(text):
    return [l for l in text.split('\n') if l.startswith(MARK)]


AP = prompt_of('auto_parts')
CONFLICT_DOC = SC['ap_source_conflict']['docs_override'][0]

# ---------------------------------------------------------------------------------------------------------------
with Case('sub-workflow: Guard → Valid? → Search Price Lists (the bot\'s own documents, fail-open) → Reconcile Prices') as c:
    c.check(SEARCH is not None, f'{SEARCH_FILE} missing')
    if SEARCH:
        trig = node(SEARCH, 'When Executed by Another Workflow')
        c.check([v['name'] for v in trig['parameters']['workflowInputs']['values']] == ['query', 'botId', 'filterKey', 'prompt'], 'trigger inputs')
        s = node(SEARCH, 'Search Price Lists')
        p = s['parameters']
        c.check(s['type'] == '@n8n/n8n-nodes-langchain.vectorStoreSupabase' and p['mode'] == 'load' and p['topK'] == 10, 'same search as the old tool: load, topK 10')
        c.check(p['tableName']['value'] == 'documents' and p['options']['queryName'] == 'match_documents', 'documents / match_documents')
        mv = p['options']['metadata']['metadataValues']
        c.check(mv == [{'name': '={{ $json.filterKey }}', 'value': '={{ $json.botId }}'}], f'filter: {mv}')
        c.check(s.get('onError') == 'continueRegularOutput' and s.get('alwaysOutputData') is True, 'search fails open')
        conns = SEARCH['connections']
        c.check(conns['OpenAI Embedding']['ai_embedding'][0][0]['node'] == 'Search Price Lists', 'embedding wired')
        c.check(node(SEARCH, 'OpenAI Embedding')['parameters']['model'] == 'text-embedding-3-small', 'embedding model = the old tool\'s')
        c.check(conns['Valid?']['main'][0][0]['node'] == 'Search Price Lists' and conns['Valid?']['main'][1][0]['node'] == 'Reconcile Prices', 'Valid? branches')
        c.check(conns['Search Price Lists']['main'][0][0]['node'] == 'Reconcile Prices', 'search → reconcile')
        c.check(SEARCH['settings'].get('saveDataSuccessExecution') == 'none', 'prompt + prices are not kept in executions')

for ch, (fname, key) in BOTS.items():
    with Case(f'[{ch}] template: the price-list tool goes through the sub-workflow with the clone\'s own id, key and PUBLISHED prompt') as c:
        bot = wf(fname)
        t = node(bot, 'Supabase Vector Store')
        p = t['parameters']
        c.check(t['type'] == '@n8n/n8n-nodes-langchain.toolWorkflow' and t['typeVersion'] == 2.2, f"type {t['type']}")
        c.check(p['workflowId']['value'] == 'crPriceSearch001', 'target')
        v = p['workflowInputs']['value']
        c.check(v.get('query') == "={{ $fromAI('input', 'Query to search for. Required', 'string') }}", f"query = the old tool's argument: {v.get('query')}")
        c.check(v.get('botId') == '={{ $workflow.id }}' and v.get('filterKey') == key, f"ids: {v.get('botId')} {v.get('filterKey')}")
        c.check(v.get('prompt') == "={{ $('AI Agent').params.options.systemMessage }}", f"prompt: {v.get('prompt')}")
        c.check('product' in p['description'].lower() and 'pricing' in p['description'], 'the old description, verbatim')
        c.check(bot['connections']['Supabase Vector Store'] == {'ai_tool': [[{'node': 'AI Agent', 'type': 'ai_tool', 'index': 0}]]}, 'ai_tool → AI Agent')
        c.check(not has_node(bot, 'OpenAI Embedding') and 'OpenAI Embedding' not in bot['connections'], 'the retired embedding sub-node is gone')
        c.check(bot['nodes'][0]['name'] == 'Webhook' and bot['nodes'][5]['name'] == 'AI Agent', 'Create/Edit patch nodes[0]/nodes[5] by index')
        c.check(not any(n['type'] == '@n8n/n8n-nodes-langchain.vectorStoreSupabase' for n in bot['nodes']), 'no un-reconciled search left in the template')

with Case('Test Dialog: its tool is the same sub-workflow, fed with the bot\'s id/key (Validate) and the published prompt it read (Compose)') as c:
    t = node(TD, 'Supabase Vector Store')
    v = t['parameters'].get('workflowInputs', {}).get('value', {})
    c.check(t['type'] == '@n8n/n8n-nodes-langchain.toolWorkflow' and t['parameters'].get('workflowId', {}).get('value') == 'crPriceSearch001', f"type {t['type']}")
    c.check(v.get('botId') == "={{ $('Validate').first().json.workflowId }}" and v.get('filterKey') == "={{ $('Validate').first().json.filterKey }}", f'ids {v}')
    c.check(v.get('prompt') == "={{ $('Compose').first().json.systemMessage }}", f"prompt {v.get('prompt')}")
    c.check(not has_node(TD, 'OpenAI Embedding Tool') and TD['connections']['Supabase Vector Store']['ai_tool'][0][0]['node'] == 'AI Agent', 'wiring')

with Case('Guard: a search runs only for a real bot id under botWaId / botTgId') as c:
    g = lambda **k: run_js(GUARD_JS, {'json': dict({'query': 'фильтр 90915-YZZE1', 'botId': 'wfV9price00001', 'filterKey': 'botWaId', 'prompt': 'P'}, **k)})[0]['json']
    c.check(g()['ok'] is True and g()['prompt'] == 'P', 'valid call')
    for bad in ({'botId': '-1'}, {'botId': ''}, {'filterKey': 'fileId'}, {'query': '  '}, {'botId': "x' or 1=1"}):
        c.check(g(**bad)['ok'] is False, f'{bad} must not search')

# ---------------------------------------------------------------------------------------------------------------
with Case('ap_source_conflict: one mark under the price-list row — the item, 3200 and 3900, «Цены расходятся»') as c:
    out = one(CONFLICT_DOC, AP)
    m = marks(out)
    c.check(len(m) == 1 and out.split('\n')[0] == CONFLICT_DOC and out.split('\n')[1] == m[0] if m else False, out)
    c.check(m and '«Фильтр масляный Toyota 90915-YZZE1»' in m[0] and '3200' in m[0] and '3900' in m[0] and 'Цены расходятся' in m[0], m)

with Case('base documents of every vertical, at their own catalog prices, come back byte for byte') as c:
    for vertical, know in DATA['knowledge'].items():
        docs = know['docs']
        out = search(docs, prompt_of(vertical))['documents']
        c.check(out == docs, f'{vertical}: {out}')

with Case('price-list shapes the converter / OCR / PDF / free text produce: a different price is marked') as c:
    rows = {
        'converter': 'product[7]: Артикул: 90915-yzze1; Наименование: Фильтр масляный; Цена: 3900; ',
        'ocr': 'product[1]: Название: Фильтр масляный 90915-YZZE1; Цена: 3 900 тг;',
        'comma unit': 'product[3]: Артикул: 90915-YZZE1; Наименование: Фильтр; Цена, тг: 3900; ',
        'pdf, no currency': 'Фильтр масляный 90915-YZZE1 3 900',
        'pdf, leader dots': 'Фильтр масляный 90915-YZZE1 .......... 3900',
        'price before the code': '3900 тг — Фильтр масляный 90915-YZZE1',
        'decimals': 'product[2]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный; Цена: 3900.00; ',
        'code without dash': 'Фильтр масляный 90915YZZE1 — 3900 тг',
        'kazakh label': '90915-YZZE1 фильтр бағасы: 3900 теңге',
    }
    for k, row in rows.items():
        m = marks(one(row, AP))
        c.check(len(m) == 1 and '3200' in m[0] and '3900' in m[0].replace(' ', '').replace(' ', '') and 'Цены расходятся' in m[0], f'{k}: {m}')

with Case('all price columns are compared: опт/розн — no mark when either equals the catalog, both named when neither does') as c:
    both = 'product[3]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена опт: 3500; Цена розн: 3900; '
    m = marks(one(both, AP))
    c.check(len(m) == 1 and 'Цена опт 3500' in m[0] and 'Цена розн 3900' in m[0] and 'Цены расходятся' in m[0], m)
    c.check(not marks(one(both.replace('3500', '3200'), AP)), 'опт = catalog → no mark')
    c.check(len(marks(one('product[3]: Код: 90915-YZZE1; Товар: Фильтр масляный; Розница: 3900; Опт: 3500; ', AP))) == 1, 'Розница/Опт headers')
    two = 'product[1]: Название: Фильтр масляный 90915-YZZE1; Цена: 4500 тг; Цена: 3900 тг;'
    m = marks(one(two, AP))
    c.check(len(m) == 1 and '4500' in m[0] and '3900' in m[0], f'two labelled prices: {m}')

with Case('ambiguous prices («от», a range) get the catalog price only — never «Цены расходятся»') as c:
    for row in ('Фильтр масляный 90915-YZZE1 — от 3500 тг', 'Фильтр масляный 90915-YZZE1 — 3500–3900 тг', 'Фильтр масляный 90915-YZZE1 — 3500 - 3900 тг'):
        m = marks(one(row, AP))
        c.check(len(m) == 1 and '3200' in m[0] and 'Цены расходятся' not in m[0] and 'однозначно' in m[0], f'{row}: {m}')

with Case('no mark: price on the next line, a longer code, a bare year, an analogue mention or column, no article, «договорная»') as c:
    for row in ('Фильтр масляный 90915-YZZE1\nЦена: 3900 тг', 'Фильтр масляный 90915-YZZE1X — 3900 тг', 'Фильтр 90915-YZZE1/2 — 3900 тг',
                'Фильтр масляный 90915-YZZE1 Camry 2019', 'Фильтр Sakura C-1139 (аналог 90915-YZZE1) — 1400 тг',
                'product[8]: Артикул: C-1139; Наименование: Фильтр Sakura; Аналог: 90915-YZZE1; Цена: 1400;'):
        c.check(not marks(one(row, AP)), f'{row!r}')
    pr = prompt_of('phone_repair')
    c.check(not marks(one('Замена экрана iPhone 11 — 39000 тг', pr)), 'an item without an article does not take part')
    dog = prompt_of('auto_parts', products='Product1: Фильтр масляный Toyota 90915-YZZE1\nProduct1 Price: договорная\nProduct1 Description: x')
    c.check(not marks(one(CONFLICT_DOC, dog)), 'a negotiable catalog price does not take part')
    ot = prompt_of('auto_parts', products='Product1: Фильтр масляный Toyota 90915-YZZE1\nProduct1 Price: от 3000 тг\nProduct1 Description: x')
    c.check(not marks(one(CONFLICT_DOC, ot)), 'an «от» catalog price does not take part')

with Case('grades, sizes and specs are not articles; an accessory with the same model code is only a soft mark') as c:
    oil = prompt_of('auto_parts', products='Product1: Масло Mobil 1 5W-30 4л\nProduct1 Price: 9500\nProduct1 Description: x')
    c.check(not marks(one('Масло Castrol Edge 5W-30 4л — 12000 тг', oil)), 'viscosity')
    tire = prompt_of('auto_parts', products='Product1: Шина Hankook 205/55R16\nProduct1 Price: 28000\nProduct1 Description: x')
    c.check(not marks(one('Шина Bridgestone Turanza 205/55R16 — 32000 тг', tire)), 'tyre size')
    ks = prompt_of('kaspi_seller')
    m = marks(one('Чехол для JBL 510BT — 2000 тг', ks))
    c.check(len(m) == 1 and 'Цены расходятся.]' not in m[0] and 'может быть о другой позиции' in m[0], f'accessory: {m}')
    m = marks(one('90915-YZZE1 — 3900 тг', AP))
    c.check(len(m) == 1 and 'может быть о другой позиции' in m[0], f'code only, no shared word: {m}')

with Case('adversarial (own review): analogue rows, promo prices, joined specs, two amounts — never a false «Цены расходятся»') as c:
    c.check(not marks(one('Фильтр JS Asakashi (аналог) 90915-YZZE1 — 1400 тг', AP)), 'an analogue naming the OEM number in brackets')
    m = marks(one('Аналог фильтра Toyota 90915-YZZE1 от JS — 1400 тг', AP))
    c.check(len(m) == 1 and 'Цены расходятся.]' not in m[0] and 'может быть о другой позиции' in m[0], f'analogue word on the item part: {m}')
    c.check(not marks(one('Фильтр масляный 90915-YZZE1 — 3900 тг, по акции 3200 тг', AP)), 'the catalog price is among the amounts')
    m = marks(one('Фильтр масляный 90915-YZZE1 — 3900 тг, доставка 1500 тг', AP))
    c.check(len(m) == 1 and 'Цены расходятся' not in m[0], f'two amounts after the article: soft only: {m}')
    nb = prompt_of('kaspi_seller', products='Product1: Ноутбук HP 16GB/512GB\nProduct1 Price: 300000\nProduct1 Description: x')
    c.check(not marks(one('Ноутбук Lenovo 16GB/512GB — 350000 тг', nb)), 'a -/ joined spec is no article')
    c.check(not marks(one('https://kaspi.kz/shop/p/jbl-tune-510bt-12345/ — 19990 тг', prompt_of('kaspi_seller'))), 'the model code inside a URL slug')
    pr = prompt_of('auto_parts')
    c.check(not marks(one('Фильтры для Toyota (90915-YZZE1, 90915-10003): цены ниже', pr)), 'a header row without a price')


with Case('catalog prices with a unit (тг/шт, тг/мес) take part; a different unit is not a conflict') as c:
    ws = prompt_of('wholesale', products='Product1: Средство для посуды WS-500\nProduct1 Price: 2100 тг/шт\nProduct1 Description: x')
    c.check(not marks(one('Средство для посуды 5 л, арт. WS-500 — 2100 тг/шт, коробка 4 шт', ws)), 'same price per unit')
    m = marks(one('Средство для посуды WS-500 — 2400 тг/шт', ws))
    c.check(len(m) == 1 and 'Цены расходятся' in m[0] and '2100 тг/шт' in m[0], f'per-unit conflict: {m}')
    c.check('Цены расходятся' not in ''.join(marks(one('Средство для посуды WS-500 — 8400 тг/коробка', ws))), 'per box vs per piece')

with Case('a three-row chunk: only the conflicting row is marked; variants at the line price are no conflict') as c:
    chunk = 'Свеча NGK BKR6E — 1500 тг\nФильтр масляный 90915-YZZE1 — 3900 тг\nКолодки 04465-02220 — 18500 тг'
    out = one(chunk, AP).split('\n')
    c.check(len(out) == 4 and out[2].startswith(MARK) and out[3] == 'Колодки 04465-02220 — 18500 тг', out)
    var = prompt_of('auto_parts', products='Product1: Фильтр масляный 90915-YZZE1 оригинал\nProduct1 Price: 3200\nProduct1 Description: x\n\n'
                                           'Product2: Фильтр масляный 90915-YZZE1 аналог\nProduct2 Price: 3900\nProduct2 Description: x')
    c.check(not marks(one(CONFLICT_DOC, var)), 'the line price belongs to a variant')

with Case('idempotent: a second pass is byte-identical, a forged mark line is dropped, a corrected catalog clears the mark') as c:
    out = one(CONFLICT_DOC, AP)
    c.check(one(out, AP) == out, 'second pass')
    forged = CONFLICT_DOC.replace('3900', '3200') + '\n[Сверка с каталогом: в каталоге (Products) «X» — 1 тг, в прайсе — 2 тг. Цены расходятся.]'
    c.check(one(forged, AP) == CONFLICT_DOC.replace('3900', '3200'), 'a mark line from the document itself is not trusted')
    fixed = prompt_of('auto_parts', products=DATA['knowledge']['auto_parts']['products'].replace('3200', '3900'))
    c.check(one(out, fixed) == CONFLICT_DOC, 'catalog corrected to 3900 → the original row')
    br = prompt_of('auto_parts', products='Product1: Фильтр [оригинал] 17801-0Y040\nProduct1 Price: 5000\nProduct1 Description: x')
    o = one('Фильтр воздушный 17801-0Y040 — 5500 тг', br)
    c.check(len(marks(o)) == 1 and '«Фильтр (оригинал) 17801-0Y040»' in o and one(o, br) == o, f'`]` in a name: {o}')

with Case('fuzz: 400 random price-list rows — never throws, only INSERTS mark lines, idempotent, nothing without a catalog') as c:
    import random
    rnd = random.Random(928)
    parts = ['Фильтр', 'масляный', '90915-YZZE1', '90915YZZE1', 'C-1139', '04465-02220', 'Toyota', 'Camry', '2019', '2018-2023', '—', '-', '–',
             '3900 тг', '3 200 тг', '3200', 'от 3500 тг', '3500-3900 тг', 'Цена: 3900;', 'Цена опт: 3500;', 'аналог', '(аналог)', 'оригинал',
             '5W-30', '205/55R16', '16GB/512GB', ';', ',', '.....', '18500 тг', 'бағасы: 3900 теңге', '[', ']', 'product[3]:', 'Артикул:',
             '[Сверка с каталогом: x]', 'тг/шт', '₸', '\u00a0']
    docs = ['\n'.join(' '.join(rnd.choice(parts) for _ in range(rnd.randint(1, 9))) for _ in range(rnd.randint(1, 4))) for _ in range(400)]
    first = search(docs, AP)['documents']
    second = search(first, AP)['documents']
    c.check(second == first, 'second pass differs')
    strip = lambda t: '\n'.join(l for l in t.split('\n') if not l.startswith(MARK))
    bad = [(d, o) for d, o in zip(docs, first) if strip(o) != strip(d)]
    c.check(not bad, f'a row was changed, not annotated: {bad[:2]}')
    import re as _re
    full_mark = _re.compile(r'^\s*\[Сверка с каталогом: .*\]\s*$')
    plain = search(docs, 'no catalog')['documents']
    want = ['\n'.join(l for l in d.split('\n') if not full_mark.match(l)) for d in docs]
    c.check(plain == want, 'without a catalog a row only loses forged mark lines')


with Case('search failure, an invalid call and an empty result never read as «позиции нет»') as c:
    r = search([], AP, error=True)
    c.check(r['documents'] == [] and 'не значит, что позиции нет' in r.get('note', ''), r)
    c.check(search([CONFLICT_DOC], AP, ok=False) == {'documents': []}, 'invalid call: no search')
    c.check(search([], AP) == {'documents': []}, 'empty result')
    c.check(search([CONFLICT_DOC], 'no catalog here')['documents'] == [CONFLICT_DOC], 'a prompt without a catalog changes nothing')

# ---------------------------------------------------------------------------------------------------------------
PREP_JS = node(SUG, 'Prep')['parameters']['jsCode']
ASM_JS = node(SUG, 'Assemble')['parameters']['jsCode']
CH = {'WA': ({'channel': 'whatsapp', 'botWaId': 'wfV9price00001', 'botTgId': '-1'}, 'Retrieve RAG'),
      'TG': ({'channel': 'telegram', 'botWaId': '-1', 'botTgId': 'wfV9price00002'}, 'Retrieve RAG TG')}


def panel(ch, items=None, catalog=None, rag=(), exact=()):
    ids, rag_node = CH[ch]
    b = {'v': 1, 'requestSeq': 1, 'chatId': 'v9@c.us', 'appUserId': 'v9-owner', 'profileId': 'v9-prof', 'businessTypeId': 'auto_parts',
         'businessName': 'V9', 'ownerPrompt': '', 'messages': [{'role': 'client', 'text': 'Camry 2019, фильтр масляный 90915-YZZE1 сколько?', 'ts': 9}], **ids}
    if items is not None:
        b['knowledge'] = {'v': 1, 'version': 'v9', 'timezone': 'Asia/Almaty', 'tzSource': 'bot', 'description': 'Запчасти', 'contacts': {},
                          'items': items, 'documents': 1}
    if catalog is not None:
        b['catalog'] = catalog
    p = run_js(PREP_JS, {'json': {'body': b}})[0]['json']
    named = {'Prep': p, rag_node: [{'document': {'pageContent': d}} for d in rag] or [{}], 'Exact Match': [{'content': d} for d in exact] or [{}]}
    a = run_js(ASM_JS, {'json': {}}, named)[0]['json']
    fenced = json.loads(a['fencedData'].split('\n', 1)[1])
    return p, a, fenced


ITEMS = [{'kind': 'product', 'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг', 'description': 'Оригинал'},
         {'kind': 'service', 'name': 'Замена масла', 'price': '5000 тг', 'description': ''}]
for ch in CH:
    with Case(f'[{ch}] panel: RAG and exact-match rows are reconciled with the SAME function and the prompt says what a mark means') as c:
        p, a, f = panel(ch, ITEMS, rag=[CONFLICT_DOC], exact=[CONFLICT_DOC])
        c.check(p.get('catalogItems') and p['catalogItems'][0]['name'] == 'Фильтр масляный Toyota 90915-YZZE1', f"Prep catalogItems: {p.get('catalogItems')}")
        c.check(len(marks(f['ragChunks'])) == 1 and len(marks(f['exactMatches'])) == 1, f"{f['ragChunks']!r} / {f['exactMatches']!r}")
        c.check('РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'], 'rule')
        p, a, f = panel(ch, ITEMS, rag=[CONFLICT_DOC.replace('3900', '3200')], exact=[CONFLICT_DOC.replace('3900', '3200')])
        c.check(not marks(f['ragChunks'] + f['exactMatches']) and 'РАСХОЖДЕНИЕ ЦЕН' not in a['systemPrompt'], 'same price: no mark, no rule')
        p, a, f = panel(ch, catalog='• Фильтр масляный Toyota 90915-YZZE1 — 3200 тг\n• Замена масла — 5000 тг', exact=[CONFLICT_DOC])
        c.check(len(marks(f['exactMatches'])) == 1 and 'РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'], f"legacy client catalog: {f['exactMatches']!r}")

with Case('ONE function: the sub-workflow and the panel carry the generator\'s RECONCILE_JS byte for byte') as c:
    ps = module('build-price-search.py')
    c.check(RESULT_JS is not None and RESULT_JS.startswith(ps.RECONCILE_JS), 'sub-workflow')
    c.check(ps.RECONCILE_JS in ASM_JS, 'panel Assemble')

with Case('Set Workflow State refuses to touch the price search') as c:
    ss = wf('0DP79STI3pafXO9B-Set_Workflow_State.json')
    v = next(n for n in ss['nodes'] if 'const PROTECTED' in n['parameters'].get('jsCode', ''))
    out = run_js("const $workflow = {id: '0DP79STI3pafXO9B'};\n" + v['parameters']['jsCode'], {'json': {'body': {'workflowId': 'crPriceSearch001', 'action': 'deactivate'}}})[0]['json']
    c.check(out == {'ok': False, 'error': 'protected_workflow'}, out)

with Case('staging derivation: the price search reads the staging documents stub (no embeddings there), never the real project') as c:
    spec = importlib.util.spec_from_file_location('derive', os.path.join(N8N, 'staging', 'derive-staging.py'))
    d = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(d)
    s = copy.deepcopy(SEARCH)
    d.isolate_supabase_readers(s)
    sp = node(s, 'Search Price Lists')
    c.check(sp['type'] == 'n8n-nodes-base.postgres' and 'from documents' in sp['parameters']['query'], sp['type'])
    c.check(not any(n['type'].startswith('@n8n/n8n-nodes-langchain.') for n in s['nodes']) and 'OpenAI Embedding' not in s['connections'], 'sub-nodes gone')
    c.check(sp.get('onError') == 'continueRegularOutput' and sp.get('alwaysOutputData') is True, 'still fail-open')
    other = copy.deepcopy(SUG)
    d.isolate_supabase_readers(other)
    c.check(other == SUG, 'only the price search is redirected')

with Case('eval runner: the simulated search runs the canonical Reconcile Prices when the measured template routes to it') as c:
    spec = importlib.util.spec_from_file_location('run_eval', os.path.join(N8N, 'eval', 'run-eval.py'))
    ev = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ev)
    mp = ev.model_params('worktree')
    c.check(mp.get('search_code') == RESULT_JS, 'reconciler taken from the canonical sub-workflow')
    r = ev.simulate_search(SC['ap_source_conflict']['docs_override'], AP, mp)
    c.check(len(marks(r['documents'][0])) == 1, r)
    old = dict(mp, search_code=None)
    c.check(ev.simulate_search(SC['ap_source_conflict']['docs_override'], AP, old) == {'documents': SC['ap_source_conflict']['docs_override']}, 'a template without the sub-workflow: raw documents')

report()
