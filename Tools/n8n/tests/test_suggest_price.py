#!/usr/bin/env python3
"""V9-r2, panel part (adversarial review of V9, 2026-10-08 — F4, F5): the «Вместе» panel's price reconciliation.

Runs the REAL Prep and Assemble Code bodies of the canonical Suggest Replies workflow (WhatsApp- and Telegram-shaped
bodies). No n8n, no network, no model.

Contract:
  * F4 — the hard rule «РАСХОЖДЕНИЕ ЦЕН» (never call either price final) is pushed ONLY when a firm «… Цены расходятся.]»
    mark is in the data the model sees; soft marks (the row may be about another item, the row's price could not be
    pinned) get their own soft rule «СВЕРКА ЦЕН» instead — a price list that AGREES with the catalog never makes the model
    refuse the catalog price;
  * F5 — the ragChunks (4000) / exactMatches (2000) windows are cut at LINE boundaries on the price-list text BEFORE
    the marks are added: a row the window held before V9 is never pushed out by a mark, a mark never stands without its
    row (or is cut in half), and the rules follow only the marks that are in the window;
  * one call: the RAG chunks and the exact-match rows are reconciled together — a row with the article at the catalog
    price clears the other rows naming that article in both blocks;
  * V9-r3 (independent review 2026-10-09, S1/S2): another pack size of the same item gets the soft rule, never the hard
    one; only the item's own row with the catalog price as a PRICE clears the article — an accessory or a stock number at
    the catalog price never takes the hard rule away from a genuine conflict.

    python3 Tools/n8n/tests/test_suggest_price.py
"""
import json

from harness import Case, node, report, run_js, wf

SUG = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
PREP_JS = node(SUG, 'Prep')['parameters']['jsCode']
ASM_JS = node(SUG, 'Assemble')['parameters']['jsCode']
CH = {'WA': ({'channel': 'whatsapp', 'botWaId': 'wfV9price00001', 'botTgId': '-1'}, 'Retrieve RAG'),
      'TG': ({'channel': 'telegram', 'botWaId': '-1', 'botTgId': 'wfV9price00002'}, 'Retrieve RAG TG')}
MARK = '[Сверка с каталогом: '
HARD, SOFT = 'РАСХОЖДЕНИЕ ЦЕН', 'СВЕРКА ЦЕН'
ITEMS = [{'kind': 'product', 'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг', 'description': 'Оригинал'},
         {'kind': 'service', 'name': 'Замена масла', 'price': '5000 тг', 'description': ''}]
CONFLICT_ROW = 'Фильтр масляный 90915-YZZE1 — 3900 тг, оригинал'
# V9-r3 (independent review 2026-10-09, S1/S2): the review's control row and a catalog that sells the item in a pack of 2
CONTROL_ROW = 'Фильтр масляный Toyota 90915-YZZE1 — 3900 тг'
PACK2_ITEMS = [{'kind': 'product', 'name': 'Фильтр масляный Toyota 90915-YZZE1 упаковка 2 шт', 'price': '6400 тг', 'description': ''}]


def panel(ch, rag=(), exact=(), items=ITEMS):
    ids, rag_node = CH[ch]
    b = {'v': 1, 'requestSeq': 1, 'chatId': 'v9@c.us', 'appUserId': 'v9-owner', 'profileId': 'v9-prof', 'businessTypeId': 'auto_parts',
         'businessName': 'V9', 'ownerPrompt': '', 'messages': [{'role': 'client', 'text': 'Camry 2019, фильтр масляный 90915-YZZE1 сколько?', 'ts': 9}], **ids,
         'knowledge': {'v': 1, 'version': 'v9', 'timezone': 'Asia/Almaty', 'tzSource': 'bot', 'description': 'Запчасти', 'contacts': {},
                       'items': items, 'documents': 1}}
    p = run_js(PREP_JS, {'json': {'body': b}})[0]['json']
    named = {'Prep': p, rag_node: [{'document': {'pageContent': d}} for d in rag] or [{}], 'Exact Match': [{'content': d} for d in exact] or [{}]}
    a = run_js(ASM_JS, {'json': {}}, named)[0]['json']
    fenced = json.loads(a['fencedData'].split('\n', 1)[1])
    return a['systemPrompt'], fenced['ragChunks'], fenced['exactMatches']


def marks(text):
    return [l for l in text.split('\n') if l.startswith(MARK)]


def firm(m):
    return m.endswith('. Цены расходятся.]')


filler = lambda n, tag: '\n'.join(f'Позиция {tag}{i} — {1000 + i} тг' for i in range(n))

for ch in CH:
    with Case(f'[{ch}] F4: a firm conflict pushes the hard rule; soft marks only — the soft rule, never the hard one') as c:
        sp, rag, ex = panel(ch, rag=[CONFLICT_ROW], exact=[CONFLICT_ROW])
        c.check(HARD in sp and all(firm(m) for m in marks(rag + '\n' + ex)), 'ap_source_conflict: the hard rule')
        analogue = 'Фильтр масляный Knecht OC 91 (90915-YZZE1) — 1400 тг'
        sp, rag, ex = panel(ch, rag=[analogue])
        c.check(len(marks(rag)) == 1 and not firm(marks(rag)[0]) and HARD not in sp and SOFT in sp, f'an analogue row alone: soft mark, soft rule: {rag!r}')
        # panel_probe3: a price list that AGREES with the catalog plus its cross-reference rows — no hard rule
        agree = ('product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3200; \n'
                 'product[2]: Артикул: OC 91; Бренд: Knecht; Наименование: Фильтр масляный; Применимость: 90915-YZZE1; Цена: 1400; ')
        sp, rag, ex = panel(ch, rag=[agree])
        c.check(HARD not in sp and not marks(rag), f'agreeing price list: no mark, no hard rule: {rag!r}')
        agree2 = 'Фильтр масляный Toyota 90915-YZZE1 (оригинал) — 3200 тг\nФильтр масляный Knecht OC 91 (90915-YZZE1) — 1400 тг'
        sp, rag, ex = panel(ch, rag=[agree2], exact=[agree2])
        c.check(HARD not in sp and SOFT not in sp and not marks(rag + ex), f'agreeing chunk with analogues: {rag!r} / {ex!r}')
        sp, rag, ex = panel(ch, rag=['Фильтр масляный 90915-YZZE1 — от 3500 тг'], exact=[analogue])
        c.check(HARD not in sp and SOFT in sp and len(marks(rag + '\n' + ex)) == 2, 'unclear + maybe: the soft rule only')
        sp, rag, ex = panel(ch, rag=[CONFLICT_ROW + '\n' + analogue])
        c.check(HARD in sp and SOFT in sp, 'a firm conflict and a soft mark: both rules')

    with Case(f'[{ch}] one call: an exact-match row at the catalog price clears the RAG rows naming that article') as c:
        sp, rag, ex = panel(ch, rag=['Фильтр масляный Mahle OC 217 (90915-YZZE1) — 1600 тг'], exact=['Фильтр масляный Toyota 90915-YZZE1 — 3200 тг'])
        c.check(not marks(rag + ex) and HARD not in sp and SOFT not in sp, f'{rag!r} / {ex!r}')

    with Case(f'[{ch}] V9-r3 S1: another pack size of the same item (review: pack_size_changed / _text / box_vs_pack) — the soft rule, never «{HARD}»') as c:
        for row in ('product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota упаковка 10 шт; Цена: 32000 тг',
                    'Фильтр масляный Toyota 90915-YZZE1 упаковка 10 шт — 32000 тг',
                    'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена за коробку: 32000 тг',
                    'Фильтр масляный Toyota 90915-YZZE1 — 3900 тг'):
            sp, rag, ex = panel(ch, rag=[row], items=PACK2_ITEMS)
            c.check(len(marks(rag)) == 1 and not firm(marks(rag)[0]) and HARD not in sp and SOFT in sp, f'{row!r}: {rag!r}')
        sp, rag, ex = panel(ch, rag=['Фильтр масляный Toyota 90915-YZZE1 упаковка 2 шт — 7000 тг'], items=PACK2_ITEMS)
        c.check(len(marks(rag)) == 1 and firm(marks(rag)[0]) and HARD in sp, f'the same pack at another price stays a conflict: {rag!r}')

    with Case(f'[{ch}] V9-r3 S2: an accessory / stock number at the catalog price never erases the genuine conflict (review: control_conflict, '
              f'cross_reference_at_catalog_price, stock_equals_catalog)') as c:
        wrench = 'Ключ для фильтра 90915-YZZE1 — 3200 тг'
        for rag_in, ex_in in (([CONTROL_ROW, wrench], []), ([CONTROL_ROW], [wrench]), ([wrench], [CONTROL_ROW]), ([CONTROL_ROW + '\n' + wrench], [])):
            sp, rag, ex = panel(ch, rag=rag_in, exact=ex_in)
            c.check(HARD in sp and any(firm(m) for m in marks(rag + '\n' + ex)), f'{rag_in} / {ex_in}: {rag!r} / {ex!r}')
        for row in ('product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3900 тг; Остаток: 3200',
                    'Фильтр масляный Toyota 90915-YZZE1 — 3900 тг; Остаток: 3200 шт'):
            sp, rag, ex = panel(ch, rag=[row])
            c.check(HARD in sp and len(marks(rag)) == 1 and firm(marks(rag)[0]), f'a stock number is not the price: {rag!r}')
        sp, rag, ex = panel(ch, rag=[CONTROL_ROW, wrench, 'Фильтр масляный Toyota 90915-YZZE1 (оригинал) — 3200 тг'])
        c.check(not marks(rag) and HARD not in sp and SOFT not in sp, f'the item\'s own row at the catalog price still settles the article: {rag!r}')

    with Case(f'[{ch}] F5: the conflicting row at the end of a full window keeps its WHOLE mark, and the rule follows the window') as c:
        row = 'Фильтр масляный 90915-YZZE1 — 3900 тг'
        base = [filler(40, 'a'), filler(40, 'b'), filler(40, 'c')]
        pad = 3990 - (sum(len(x) for x in base) + 3 * 5 + len(row) + 1)
        chunks = base + [('x' * pad) + '\n' + row]
        raw = '\n---\n'.join(chunks)
        c.check(raw[:4000].endswith(row), 'premise: before V9 the row was the last line of the 4000 window')
        sp, rag, ex = panel(ch, rag=chunks)
        c.check(row in rag.split('\n') and any(firm(m) for m in marks(rag)) and HARD in sp, f'row + complete mark + rule: {rag[-200:]!r}')
        c.check(all(m.endswith(']') for m in marks(rag)), f'no mark is cut: {rag[-200:]!r}')
        c.check(len(rag) <= 4000 + 1500, f'the marks have a bounded room: {len(rag)}')

    with Case(f'[{ch}] F5: a row the window held before V9 is not pushed out by a mark above it') as c:
        row1 = 'Фильтр масляный 90915-YZZE1 — 3900 тг'
        c1 = row1 + '\n' + filler(30, 'e')
        last = 'Свеча NGK BKR6E — 1500 тг'
        mid = [filler(40, 'f'), filler(40, 'g')]
        pad = 3995 - (len(c1) + sum(len(x) for x in mid) + 3 * 5 + len(last) + 1)
        chunks = [c1] + mid + [('y' * pad) + '\n' + last]
        c.check(last in '\n---\n'.join(chunks)[:4000], 'premise: before V9 the last row was in the window')
        sp, rag, ex = panel(ch, rag=chunks)
        c.check(last in rag.split('\n') and any(firm(m) for m in marks(rag)), f'last row still visible: {rag[-120:]!r}')

    with Case(f'[{ch}] F5: the window is cut at a line boundary — never half a row; a row past the window is not marked') as c:
        rows = [f'Позиция z{i} — {2000 + i} тг' for i in range(200)]
        chunks = ['\n'.join(rows[:100]), '\n'.join(rows[100:]) + '\n' + CONFLICT_ROW]
        sp, rag, ex = panel(ch, rag=chunks)
        lines = [l for l in rag.split('\n') if l != '---']
        c.check(all(l in rows or l == CONFLICT_ROW or l.startswith(MARK) for l in lines), f'only whole rows: {lines[-3:]}')
        c.check(CONFLICT_ROW not in rag and not marks(rag) and HARD not in sp, 'the conflicting row is past the window: no mark, no rule')
        ex_rows = [f'Артикул Q{i:05d} — {3000 + i} тг' for i in range(120)]
        sp, rag, ex = panel(ch, exact=['\n'.join(ex_rows), CONFLICT_ROW])
        ex_lines = [l for l in ex.split('\n') if l != '---']
        c.check(len(ex) <= 2000 and all(l in ex_rows for l in ex_lines) and HARD not in sp, f'exactMatches: whole rows within 2000: {ex[-80:]!r}')

    with Case(f'[{ch}] F5: a single row longer than the whole window is still shown (cut), as before V9 — never an empty window') as c:
        giant = 'Прайс ' + ' '.join(f'поз{i} {1000 + i} тг;' for i in range(600))
        sp, rag, ex = panel(ch, rag=[giant])
        c.check(len(rag) == 4000 and giant.startswith(rag), f'{len(rag)}')

report()
