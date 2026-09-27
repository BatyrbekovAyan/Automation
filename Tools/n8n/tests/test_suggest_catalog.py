#!/usr/bin/env python3
"""R12 (iteration 2026-09-27): the «Вместе» panel's catalog must keep the ASKED item and its essential conditions,
and every shortening must be visible (an ellipsis) and flagged — within the unchanged 3000-char budget.

Runs the REAL Prep (and Assemble) Code bodies of the canonical Suggest Replies workflow on synthetic requests, for a
WhatsApp- and a Telegram-shaped body (Prep is the one shared path). The Auto side of the parity case is rendered by
the REAL `Set Fields` systemMessage expression of Edit WhatsApp Workflow (n8n's own expression engine) from a
ProductsList composed exactly like Manager.CreateWhatsappWorkflowFromEdit does.

Contract (review R12 criteria + the iteration README):
  * the item the question is about keeps its WHOLE description (a closing «Деталь оплачивается отдельно.» survives);
  * the asked item survives a long catalog (the last of 32), matched by article code or by name words;
  * same-name variants are never merged or split: all of them stay, each with its own price and conditions;
  * a small catalog carries every fact the Auto prompt carries, byte for byte, with no flags;
  * whatever is shortened ends with «…» and raises a flag the prompt turns into a rule — including a description the
    client already clamped at its 600-char limit and a legacy client's catalog clamped at 2500;
  * the rendered catalog never exceeds 3000 chars.

    python3 Tools/n8n/tests/test_suggest_catalog.py
"""
import json
import random
import re

from harness import Case, N8N, expr, module, node, report, run_js, wf

SUG = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
EDIT_WA = wf('3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json')
PREP_JS = node(SUG, 'Prep')['parameters']['jsCode']
BUDGET = 3000
CUT_FLAGS = ('item_description_truncated', 'descriptions_dropped', 'catalog_incomplete')

CHANNELS = {
    'WA': {'channel': 'whatsapp', 'botWaId': 'wfR12wa0000001', 'botTgId': '-1'},
    'TG': {'channel': 'telegram', 'botWaId': '-1', 'botTgId': 'wfR12tg0000001'},
}


def body(ch, question, items=None, catalog=None, history=None, steer=None):
    b = {'v': 1, 'requestSeq': 1, 'chatId': '77010001234@c.us' if ch == 'WA' else '5550001112', 'appUserId': 'r12-owner',
         'profileId': 'r12-prof', 'businessTypeId': 'phone_repair', 'businessName': 'R12', 'ownerPrompt': '',
         'messages': (history or []) + [{'role': 'client', 'text': question, 'ts': 9}], 'steerTowardText': steer, **CHANNELS[ch]}
    if items is not None:
        b['knowledge'] = {'v': 1, 'version': 'r12', 'timezone': 'Asia/Almaty', 'tzSource': 'bot', 'description': 'Сервис R12',
                          'contacts': {'phone': '+7 700 000 00 00'}, 'items': items, 'documents': 0}
    if catalog is not None:
        b['catalog'] = catalog
    return b


def prep_many(bodies):
    """The real Prep body over many requests in ONE node process (the fuzz case needs hundreds)."""
    js = ('const P = new Function("$json", ' + json.dumps(PREP_JS) + ');\n'
          'return ' + json.dumps(bodies, ensure_ascii=False) + '.map(b => P({body: b})[0].json);')
    return run_js(js)


def prep(b):
    return prep_many([b])[0]


def item(name, price, desc, kind='service'):
    return {'kind': kind, 'name': name, 'price': price, 'description': desc}


def line_of(catalog, name, price):
    """The rendered line of one item (the catalog is «• name — price — description» lines)."""
    head = '• ' + name + (' — ' + price if price else '')
    return next((l for l in catalog.split('\n') if l == head or l.startswith(head + ' — ')), None)


def shortenings_are_marked(prep_out, items):
    """Every rendered description is either the original or ends with «…» — and then a flag says so."""
    bad = []
    marked = False
    for it in items:
        l = line_of(prep_out['catalog'], it['name'], it['price'])
        if l is None:
            continue
        head = '• ' + it['name'] + (' — ' + it['price'] if it['price'] else '')
        shown = l[len(head) + 3:] if len(l) > len(head) else ''
        if shown and shown != it['description'].strip():          # surrounding whitespace is not a fact
            if not shown.endswith('…'):
                bad.append(f'{it["name"]!r}: shortened without «…»: {shown[-40:]!r}')
            marked = True
        if not shown and it['description'] and 'descriptions_dropped' not in prep_out['knowledgeFlags']:
            bad.append(f'{it["name"]!r}: description dropped without the descriptions_dropped flag')
    flags = prep_out['knowledgeFlags']
    if marked and 'item_description_truncated' not in flags:
        bad.append(f'a description was shortened but knowledgeFlags={flags}')
    return bad


# --- data --------------------------------------------------------------------------------------------------------
COND = 'Деталь оплачивается отдельно.'
DESC_263 = ('Диагностика. ' * 18) + COND                      # the reviewer's input A (263 chars)
REPAIRS = [
    ('Замена аккумулятора iPhone 11', '12000'), ('Замена экрана Samsung A52', '28000'), ('Замена стекла камеры iPhone 12', '9000'),
    ('Чистка после попадания воды', '7000'), ('Замена динамика Redmi Note 10', '6500'), ('Прошивка Android', '5000'),
    ('Замена задней крышки iPhone 14', '15000'), ('Ремонт кнопки питания', '6000'), ('Замена шлейфа Samsung S21', '11000'),
    ('Замена микрофона Xiaomi', '5500'), ('Восстановление после падения', '8000'),
]
FILLER = ('Работаем с оригинальными и совместимыми запчастями, мастер показывает старую деталь, '
          'гарантия на работу 90 дней, срок от одного часа в зависимости от загрузки сервиса и наличия запчасти на складе. '
          'Оплата наличными, картой или через Kaspi, запись по телефону. ')
ASKED_REPAIR = item('Замена разъёма зарядки iPhone 13', '14000',
                    'Меняем нижний шлейф с разъёмом Lightning, проверяем зарядку и передачу данных. Время работы около часа, '
                    'гарантия 90 дней на работу. Цена указана за работу мастера. ' + COND)
BOUQUET_NAMES = ['Весна', 'Рассвет', 'Облако', 'Лаванда', 'Классика', 'Пион', 'Гортензия', 'Рубин', 'Шёпот', 'Аврора', 'Лето', 'Каприз',
                 'Нота', 'Мелодия', 'Жемчуг', 'Бриз', 'Кармен', 'Сакура', 'Солнце', 'Изумруд', 'Кружево', 'Вальс', 'Фиалка', 'Муза',
                 'Грация', 'Праздник', 'Роскошь', 'Верность', 'Орхидея', 'Мечта', 'Сияние']
BOUQUET_DESC = ('Сезонные цветы в крафтовой упаковке с лентой, открытка в подарок, собираем за два часа, '
                'фото готового букета присылаем перед отправкой, свежесть гарантируем трое суток. ')
NEZHNOST = item('Букет «Нежность»', '18000', '15 кустовых роз и эвкалипт в крафте. Собираем за 2 часа. Доставка по городу оплачивается отдельно.', 'product')
VARIANTS = [item('Букет «Нежность»', '12000', 'Размер S: 9 роз, высота 35 см. Доставка оплачивается отдельно.', 'product'),
            item('Букет «Нежность»', '18000', 'Размер M: 15 роз, высота 45 см. Доставка по городу бесплатно.', 'product'),
            item('Букет «Нежность»', '26000', 'Размер L: 25 роз, высота 55 см, коробка. Доставка бесплатно, открытка в подарок.', 'product')]


def bouquets(n, desc=BOUQUET_DESC):
    return [item(f'Букет «{BOUQUET_NAMES[i % len(BOUQUET_NAMES)]}»' + ('' if i < len(BOUQUET_NAMES) else f' {i}'), f'{9000 + 500 * i}',
                 desc + f'Код {1000 + i}.', 'product') for i in range(n)]


for ch in CHANNELS:
    with Case(f'[{ch}] reviewer A: a 263-char description keeps its closing condition, no false truncation flag') as c:
        out = prep(body(ch, 'Ремонт ZX98765 сколько стоит?', [item('Ремонт ZX98765', '15000', DESC_263)]))
        c.check(len(DESC_263) == 263, len(DESC_263))
        c.check(line_of(out['catalog'], 'Ремонт ZX98765', '15000') == '• Ремонт ZX98765 — 15000 — ' + DESC_263,
                f'catalog={out["catalog"]!r}')
        c.check(not any(f in out['knowledgeFlags'] for f in CUT_FLAGS), f'flags={out["knowledgeFlags"]}')

    with Case(f'[{ch}] an overflowing catalog: the ASKED repair keeps its whole description, the others are cut visibly') as c:
        items = [item(n, p, FILLER + f'Позиция {i}.') for i, (n, p) in enumerate(REPAIRS)] + [ASKED_REPAIR]
        c.check(len('\n'.join('• %s — %s — %s' % (x['name'], x['price'], x['description']) for x in items)) > BUDGET, 'premise: must overflow')
        out = prep(body(ch, 'Сколько стоит замена разъема зарядки на iPhone 13?', items))
        l = line_of(out['catalog'], ASKED_REPAIR['name'], ASKED_REPAIR['price'])
        c.check(l is not None and l.endswith(ASKED_REPAIR['description']), f'asked line={l!r}')
        c.check(len(out['catalog']) <= BUDGET, len(out['catalog']))
        c.check('item_description_truncated' in out['knowledgeFlags'], f'flags={out["knowledgeFlags"]}')
        c.check('…' in out['catalog'], 'no visible ellipsis although descriptions were shortened')
        c.check(all(line_of(out['catalog'], n, p) for n, p in REPAIRS), 'an unasked item vanished although names fit')
        for x in shortenings_are_marked(out, items):
            c.check(False, x)

    with Case(f'[{ch}] reviewer B: the asked LAST item of a 32-item catalog survives (article code in the question)') as c:
        items = [item(f'Товар {i} ' + 'X' * 100, '1000', 'Описание') for i in range(31)] + [item('ZX98765', '3200', 'Нужная позиция')]
        out = prep(body(ch, 'Ремонт ZX98765 сколько стоит?', items))
        c.check(line_of(out['catalog'], 'ZX98765', '3200') == '• ZX98765 — 3200 — Нужная позиция', f'catalog tail={out["catalog"][-200:]!r}')
        c.check(len(out['catalog']) <= BUDGET, len(out['catalog']))
        c.check('catalog_incomplete' in out['knowledgeFlags'], f'items were dropped but flags={out["knowledgeFlags"]}')

    with Case(f'[{ch}] the asked last bouquet of 32 is found by NAME words (no code) and keeps «Доставка … отдельно»') as c:
        items = bouquets(31) + [NEZHNOST]
        out = prep(body(ch, 'Здравствуйте! Сколько стоит букет Нежность?', items))
        l = line_of(out['catalog'], NEZHNOST['name'], NEZHNOST['price'])
        c.check(l == '• Букет «Нежность» — 18000 — ' + NEZHNOST['description'], f'asked line={l!r}')
        c.check(len(out['catalog']) <= BUDGET, len(out['catalog']))
        c.check(any(f in out['knowledgeFlags'] for f in CUT_FLAGS), f'flags={out["knowledgeFlags"]}')
        for x in shortenings_are_marked(out, items):
            c.check(False, x)

    with Case(f'[{ch}] a short follow-up «а сколько?» keeps the item named in the previous turn whole') as c:
        items = bouquets(31) + [NEZHNOST]
        out = prep(body(ch, 'а сколько?', items, history=[{'role': 'client', 'text': 'Букет Нежность ещё есть?', 'ts': 1},
                                                            {'role': 'business', 'text': 'Да, есть, соберём за 2 часа.', 'ts': 2}]))
        l = line_of(out['catalog'], NEZHNOST['name'], NEZHNOST['price'])
        c.check(l == '• Букет «Нежность» — 18000 — ' + NEZHNOST['description'], f'asked line={l!r}')
        c.check(len(out['catalog']) <= BUDGET, len(out['catalog']))
        for x in shortenings_are_marked(out, items):
            c.check(False, x)

    with Case(f'[{ch}] same-name variants: all three «Нежность» sizes stay, each with its own price and conditions') as c:
        items = bouquets(25)[:12] + VARIANTS + bouquets(25)[12:]
        out = prep(body(ch, 'Сколько стоит букет Нежность?', items))
        rows = [l for l in out['catalog'].split('\n') if l.startswith('• Букет «Нежность» — ')]
        c.check(len(rows) == 3, f'variant rows={rows}')
        for v in VARIANTS:
            c.check(line_of(out['catalog'], v['name'], v['price']) == '• Букет «Нежность» — ' + v['price'] + ' — ' + v['description'],
                    f'variant {v["price"]} line={line_of(out["catalog"], v["name"], v["price"])!r}')
        c.check(len(out['catalog']) <= BUDGET, len(out['catalog']))

    with Case(f'[{ch}] same-name variants too large to keep whole: none dropped or merged, each cut is marked') as c:
        long_variants = [item('Букет «Нежность»', p, f'Размер {s}. ' + BOUQUET_DESC * 4 + 'Доставка оплачивается отдельно.', 'product')
                         for s, p in (('S', '12000'), ('M', '18000'), ('L', '26000'), ('XL', '34000'))]
        items = bouquets(20) + long_variants
        out = prep(body(ch, 'Сколько стоит букет Нежность?', items))
        rows = [l for l in out['catalog'].split('\n') if l.startswith('• Букет «Нежность» — ')]
        c.check(len(rows) == 4, f'variant rows={len(rows)}')
        c.check(all(any(l.startswith('• Букет «Нежность» — ' + v['price']) for l in rows) for v in long_variants), rows)
        c.check(len(out['catalog']) <= BUDGET, len(out['catalog']))
        for x in shortenings_are_marked(out, items):
            c.check(False, x)


# --- parity with the Auto prompt ----------------------------------------------------------------------------------
def auto_system_message(products, services):
    """Edit WhatsApp Workflow's REAL systemMessage expression over a form composed like Manager does."""
    def block(prefix, xs):
        return ''.join(f'{prefix}{i + 1}: {x["name"]}\n{prefix}{i + 1} Price: {x["price"]}\n{prefix}{i + 1} Description: {x["description"]}'
                       + ('' if i == len(xs) - 1 else '\n\n') for i, x in enumerate(xs))
    form = {'Prompt': '', 'Business': 'About Business:\nСервис R12', 'BusinessType': 'phone_repair',
            'ProductsList': 'Products:\n' + block('Product', products), 'ServicesList': 'Services:\n' + block('Service', services)}
    sf = node(EDIT_WA, 'Set Fields')
    a = next(x for x in sf['parameters']['assignments']['assignments'] if x['name'] == 'nodes[5].parameters.options.systemMessage')
    return expr(a['value'], {'nodes': [{}, {}, {}, {}, {}, {'parameters': {'options': {'systemMessage': 'HEAD\nAdditional Instructions: x'}}}]},
                {'Vertical Prompt': {'verticalPrompt': 'VERTICAL'}, 'Unity Webhook': {'body': form}})


SMALL = [item('Чехол силиконовый iPhone 13', '4500', 'Мягкий силикон, цвета: чёрный, синий, прозрачный. Защитная плёнка в комплект не входит.', 'product'),
         item('Защитное стекло 9H', '3000', 'Закалённое стекло на весь экран. Наклейка бесплатно при покупке у нас; своё стекло клеим за 1000 тг.', 'product'),
         item('Замена экрана iPhone 13', '65000', 'Оригинальный дисплей, работа около 2 часов, гарантия 6 месяцев. Face ID и True Tone после замены '
              'сохраняются, защитное стекло наклеим бесплатно. Если корпус погнут, выпрямление корпуса оплачивается отдельно — '
              'это видно только после вскрытия, заранее предупредим о сумме.'),
         item('Диагностика', '0', 'Бесплатно при ремонте у нас, иначе 3000 тг.'),
         item('Замена аккумулятора iPhone 11', '18000', 'Аккумулятор повышенной ёмкости, 1 час. Сообщение «неоригинальная деталь» в настройках '
              'появится — на работу это не влияет. Гарантия 3 месяца, на вздутие по вине пользователя не распространяется.')]

for ch in CHANNELS:
    with Case(f'[{ch}] small catalog: every fact the Auto prompt carries reaches the panel verbatim, no flags') as c:
        auto = auto_system_message([x for x in SMALL if x['kind'] == 'product'], [x for x in SMALL if x['kind'] == 'service'])
        c.check(max(len(x['description']) for x in SMALL) > 200, 'premise: one description is longer than the old 200-char cut')
        out = prep(body(ch, 'Здравствуйте, сколько стоит замена экрана на 13?', SMALL))
        for x in SMALL:
            for fact in (x['name'], x['price'], x['description']):
                c.check(fact in auto, f'auto prompt lacks {fact[:40]!r} (test premise)')
                c.check(fact in out['catalog'], f'panel lacks a fact the Auto prompt has: {fact[:60]!r}')
        c.check(not any(f in out['knowledgeFlags'] for f in CUT_FLAGS), f'flags={out["knowledgeFlags"]}')

    with Case(f'[{ch}] a description the CLIENT clamped at 600 is not presented as complete') as c:
        full = ('Полная переборка платы после воды: чистка в ультразвуке, замена окислившихся компонентов, проверка всех цепей. ' * 7
                + 'Если потребуется замена контроллера питания, деталь оплачивается отдельно.')
        sent = full[:600]                     # SuggestionKnowledge.Compose clamps descriptions at 600, unmarked
        auto = auto_system_message([], [item('Ремонт после воды', '25000', full)])
        out = prep(body(ch, 'Сколько стоит ремонт после воды?', [item('Ремонт после воды', '25000', sent)]))
        l = line_of(out['catalog'], 'Ремонт после воды', '25000') or ''
        c.check('контроллера питания' not in sent, 'premise: the condition lies past the client clamp')
        c.check(full in auto and 'контроллера питания' in auto, 'premise: Auto has the whole description')
        c.check(l.endswith('…'), f'the clamped description reads as complete: {l[-60:]!r}')
        c.check('item_description_truncated' in out['knowledgeFlags'], f'flags={out["knowledgeFlags"]}')

    with Case(f'[{ch}] a legacy client catalog clamped at its 2500 limit is flagged incomplete and marked') as c:
        lines = [f'• Чехол модель {i} — {4000 + 10 * i} тг' for i in range(200)]
        legacy = '\n'.join(lines)[:2500]      # N8nSuggestionsProvider.BuildPayloadJson: Clamp(catalog, 2500)
        out = prep(body(ch, 'Чехол модель 150 есть?', catalog=legacy))
        c.check('catalog_incomplete' in out['knowledgeFlags'], f'flags={out["knowledgeFlags"]}')
        c.check(out['catalog'].endswith('…'), f'tail={out["catalog"][-30:]!r}')
        short = prep(body(ch, 'Чехол модель 1 есть?', catalog='\n'.join(lines[:5])))
        c.check(short['catalog'] == '\n'.join(lines[:5]) and 'catalog_incomplete' not in short['knowledgeFlags'],
                f'a short legacy catalog must pass unchanged: {short["knowledgeFlags"]}')

    with Case(f'[{ch}] the shortening reaches the model as a rule (Assemble)') as c:
        items = [item(n, p, FILLER + f'Позиция {i}.') for i, (n, p) in enumerate(REPAIRS)] + [ASKED_REPAIR]
        out = prep(body(ch, 'Сколько стоит замена разъема зарядки на iPhone 13?', items))
        asm = run_js(node(SUG, 'Assemble')['parameters']['jsCode'], {'json': {}}, {'Prep': out})[0]['json']
        c.check('КАТАЛОГ СОКРАЩЁН' in asm['systemPrompt'], 'no catalog rule in the prompt')
        rule = next((l for l in asm['systemPrompt'].split('\n') if l.startswith('КАТАЛОГ СОКРАЩЁН')), '')
        c.check('…' in rule, f'the rule does not explain the «…» marker: {rule!r}')
        c.check(ASKED_REPAIR['description'] in json.loads(asm['fencedData'].split('\n', 1)[1])['catalog'], 'asked description not in ДАННЫЕ')


# --- the server's limits stay in step with the client's clamps ---------------------------------------------------
with Case('server limits mirror the client clamps (SuggestionKnowledge 600, N8nSuggestionsProvider catalog 2500)') as c:
    gen = (N8N / 'apply-suggest-v2.py').read_text(encoding='utf-8')
    root = N8N.parents[1]
    sk = (root / 'Assets/Scripts/Chat/SuggestionKnowledge.cs').read_text(encoding='utf-8')
    pv = (root / 'Assets/Scripts/Chat/N8nSuggestionsProvider.cs').read_text(encoding='utf-8')
    cl_desc = re.search(r'MaxDescriptionChars\s*=\s*(\d+)', sk)
    cl_cat = re.search(r'MaxCatalogChars\s*=\s*(\d+)', pv)
    sv_desc = re.search(r'ITEM_DESC_MAX\s*=\s*(\d+)', gen)
    sv_cat = re.search(r'LEGACY_CATALOG_MAX\s*=\s*(\d+)', gen)
    c.check(cl_desc and sv_desc and cl_desc[1] == sv_desc[1], f'client {cl_desc and cl_desc[1]} vs server {sv_desc and sv_desc[1]}')
    c.check(cl_cat and sv_cat and cl_cat[1] == sv_cat[1], f'client {cl_cat and cl_cat[1]} vs server {sv_cat and sv_cat[1]}')


# --- invariants over random catalogs ----------------------------------------------------------------------------
with Case('fuzz (seeded, 200 catalogs): ≤ 3000 chars, the asked item keeps its closing condition, every cut is marked') as c:
    rnd = random.Random(20260927)
    words = ['роза', 'пион', 'экран', 'стекло', 'кабель', 'чехол', 'фильтр', 'масло', 'курс', 'урок', 'группа', 'доставка', 'гарантия',
             'оригинал', 'аналог', 'размер', 'цвет', 'срок', 'мастер', 'деталь']
    cases = []
    for k in range(200):
        n = rnd.randint(3, 90)
        its = []
        for i in range(n):
            d = ' '.join(rnd.choice(words) for _ in range(rnd.randint(0, 80)))[:rnd.choice([0, 80, 200, 400, 599])].strip()
            its.append(item(f'Позиция {k}-{i} ' + rnd.choice(words), str(rnd.randint(1, 99) * 500), d))
        a = rnd.randrange(n)
        code = f'QX{k:03d}{rnd.randint(10, 99)}'
        its[a] = item(f'Позиция {k}-{a} {code}', '7777', ' '.join(rnd.choice(words) for _ in range(rnd.randint(0, 40)))[:300] + ' ' + COND)
        ch = 'WA' if k % 2 else 'TG'
        cases.append((its, a, body(ch, f'Сколько стоит {code}?', its)))
    outs = prep_many([b for _, _, b in cases])
    lost = over = unmarked = 0
    for (its, a, _), out in zip(cases, outs):
        if len(out['catalog']) > BUDGET:
            over += 1
        l = line_of(out['catalog'], its[a]['name'], its[a]['price'])
        if not (l and l.endswith(COND)):
            lost += 1
        if shortenings_are_marked(out, its):
            unmarked += 1
    c.check(over == 0, f'{over} catalogs over budget')
    c.check(lost == 0, f'the asked item lost its condition in {lost}/200')
    c.check(unmarked == 0, f'{unmarked}/200 catalogs cut a description without «…» + flag')

report()
