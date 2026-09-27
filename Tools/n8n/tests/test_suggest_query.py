#!/usr/bin/env python3
"""R13 (iteration 2026-09-27): the retrieval query of the «Вместе» panel must keep the NEWEST client turn(s) and the
picked direction, trimming OLD context first; exact tokens go newest-first, and the Exact Match SQL ranks rows by the
order of the tokens they matched, so the newest article wins the top 3.

Runs the REAL Prep Code body of the canonical Suggest Replies workflow (WhatsApp- and Telegram-shaped bodies — Prep
is shared), then the REAL `Exact Match` SQL with parameters from its REAL queryReplacement expression (n8n's own
engine) against a session-temporary `documents` table in the staging Postgres (BEGIN … ROLLBACK). No n8n, no network.

Contract (review R13 criteria + the refuter's objections):
  * queryText ≤ 900 always carries the latest client message and the «[направление] …» line; old turns are cut first;
  * a series of client messages keeps its NEWEST message (trailingText ≤ 500 — it is also the prompt's lastClientMessage);
  * exactTokens: the question's own codes first (newest message first), older context after; a bare number (a price)
    never pushes an article code out; three codes of an old reply do not evict the new one;
  * the SQL returns the newest article's row first, never another bot's row, and a bare-number token matches that
    number only (15000 is not 150000 / 115000).

    python3 Tools/n8n/tests/test_suggest_query.py
"""
import json
import random

from harness import Case, bind, expr, node, report, run_js, sql_json, wf

SUG = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
PREP_JS = node(SUG, 'Prep')['parameters']['jsCode']
EXACT = node(SUG, 'Exact Match')

CHANNELS = {
    'WA': {'channel': 'whatsapp', 'botWaId': 'wfR13wa0000001', 'botTgId': '-1'},
    'TG': {'channel': 'telegram', 'botWaId': '-1', 'botTgId': 'wfR13tg0000001'},
}


def m(role, text, ts):
    return {'role': role, 'text': text, 'ts': ts}


def body(ch, messages, steer=None, last=None):
    return {'v': 1, 'requestSeq': 1, 'chatId': '77010001234@c.us' if ch == 'WA' else '5550001112', 'appUserId': 'r13-owner',
            'profileId': 'r13-prof', 'businessTypeId': 'auto_parts', 'businessName': 'R13', 'ownerPrompt': '', 'catalog': '',
            'messages': messages, 'steerTowardText': steer, 'lastIncomingText': last, **CHANNELS[ch]}


def prep_many(bodies):
    js = ('const P = new Function("$json", ' + json.dumps(PREP_JS) + ');\n'
          'return ' + json.dumps(bodies, ensure_ascii=False) + '.map(b => P({body: b})[0].json);')
    return run_js(js)


def prep(b):
    return prep_many([b])[0]


def invariants(c, out):
    c.check(len(out['queryText']) <= 900, f'queryText length {len(out["queryText"])} > 900')
    c.check(len(out['trailingText']) <= 500, f'trailingText length {len(out["trailingText"])} > 500')
    up = out['queryText'].upper()
    c.check(all(tok in up for tok in out['exactTokens']), f'tokens {out["exactTokens"]} not all in queryText')


DOCS_SETUP = 'create temp table documents (id bigint primary key, content text, metadata jsonb);'


def exact_rows(prep_out, docs):
    """The REAL Exact Match statement + its REAL parameter expression over a temp `documents` table."""
    params = expr(EXACT['parameters']['options']['queryReplacement'], {}, {'Gate Decision': prep_out})
    ins = '\n'.join("insert into documents values (%d, '%s', '%s'::jsonb);" % (i, content.replace("'", "''"), json.dumps(meta))
                    for i, content, meta in docs)
    q = bind(EXACT['parameters']['query'], params).strip().rstrip(';')
    return sql_json(ins + "\nselect coalesce(json_agg(r.content), '[]'::json) from (\n" + q + "\n) r;", DOCS_SETUP), params


def scope(ch, other=False):
    """Document metadata of this channel's bot (or of ANOTHER bot on the same channel)."""
    k = 'botWaId' if ch == 'WA' else 'botTgId'
    return {k: ('wfForeign000001' if other else CHANNELS[ch][k]), 'fileId': 'r13'}


for ch in CHANNELS:
    with Case(f'[{ch}] reviewer: long history keeps «а сколько?» and the picked direction; the new article leads the tokens') as c:
        out = prep(body(ch, [m('client', 'OLD54321 ' + 'А' * 490, 1), m('business', 'Б' * 500, 2), m('client', 'а сколько?', 3)],
                        steer='Искомая модель NEW12345'))
        invariants(c, out)
        c.check(out['queryText'].endswith('\nа сколько?'), f'query tail={out["queryText"][-60:]!r}')
        c.check('[направление] Искомая модель NEW12345' in out['queryText'], 'the picked direction was pushed out of the query')
        c.check(out['exactTokens'][:1] == ['NEW12345'], f'tokens={out["exactTokens"]}')

    with Case(f'[{ch}] «а сколько?» after long turns: the question stays, the old subject survives as the head of its turn') as c:
        subject = 'Нужен масляный фильтр 90915-YZZE1 на Camry 2019. '
        out = prep(body(ch, [m('client', subject + 'Машина из Эмиратов, пробег большой, менял масло сам, хочу оригинал или хороший аналог, '
                                          'подскажите что есть и сколько по времени доставка до Караганды, если нет в наличии. ' * 2, 1),
                             m('business', 'Здравствуйте! Есть оригинал и два аналога: Mann, Bosch. ' + 'Подробности по каждому: ' * 20, 2),
                             m('client', 'а сколько?', 3)]))
        invariants(c, out)
        c.check(out['queryText'].endswith('\nа сколько?'), f'query tail={out["queryText"][-60:]!r}')
        c.check('90915-YZZE1' in out['queryText'] and '90915-YZZE1' in out['exactTokens'], f'subject lost: tokens={out["exactTokens"]}')
        c.check('[бизнес] Здравствуйте!' in out['queryText'], 'the newest context turn must be kept before older ones')

    with Case(f'[{ch}] a series of client messages keeps its NEWEST message (query + lastClientMessage)') as c:
        a1 = 'Добрый день! Ищу запчасти на Toyota Land Cruiser Prado 150, 2016 год, дизель. ' * 4
        a2 = 'Ещё нужны колодки передние и задние, желательно не самые дешёвые, но и не оригинал за космические деньги. ' * 3
        c.check(len(a1[:260].strip()) + len(a2[:260].strip()) + len('артикул NEW12345') + 2 > 500, 'premise: the series exceeds 500')
        out = prep(body(ch, [m('business', 'Здравствуйте, чем помочь?', 1), m('client', a1[:260], 2), m('client', a2[:260], 3),
                             m('client', 'артикул NEW12345', 4)]))
        invariants(c, out)
        c.check(out['trailingText'].endswith('артикул NEW12345'), f'trailing tail={out["trailingText"][-50:]!r}')
        c.check('артикул NEW12345' in out['queryText'], 'the newest message is not in the retrieval query')
        c.check(out['exactTokens'][:1] == ['NEW12345'], f'tokens={out["exactTokens"]}')
        burst = prep(body(ch, [m('business', 'Здравствуйте, чем помочь?', 1), m('client', a1[:260], 2), m('client', a2[:260], 3)],
                          last=a1[:260] + '\n' + a2[:260] + '\nартикул NEW12345'))
        c.check(burst['trailingText'].endswith('артикул NEW12345') and burst['exactTokens'][:1] == ['NEW12345'],
                f'lastIncomingText burst: tail={burst["trailingText"][-40:]!r} tokens={burst["exactTokens"]}')

    with Case(f'[{ch}] no overflow: three codes and prices of the previous reply do not evict the new article') as c:
        out = prep(body(ch, [m('client', 'Нужны свечи на Camry', 1),
                             m('business', 'Есть AN-8201 — 18500 тг, P83165 — 24500 тг, SP1376 — 9900 тг и оригинал 04465-33471', 2),
                             m('client', 'а NEW12345?', 3)]))
        invariants(c, out)
        c.check(len(out['queryText']) < 900, 'premise: no overflow')
        c.check(out['exactTokens'][:1] == ['NEW12345'], f'tokens={out["exactTokens"]}')
        c.check(not any(t.isdigit() for t in out['exactTokens']), f'a bare price outranked an article code: {out["exactTokens"]}')

    with Case(f'[{ch}] «а сколько?» with codes only in the context: the newer turn\'s code comes first') as c:
        out = prep(body(ch, [m('client', 'нужен фильтр AB-11111', 1), m('business', 'оригинала нет, есть аналог CD-22222', 2),
                             m('client', 'а сколько?', 3)]))
        invariants(c, out)
        c.check(out['exactTokens'] == ['CD-22222', 'AB-11111'], f'tokens={out["exactTokens"]}')

    with Case(f'[{ch}] SQL: the newest article\'s row comes first although three newer rows match an old price; no foreign bot') as c:
        pj = prep(body(ch, [m('client', 'Нужны свечи на Camry', 1),
                            m('business', 'Есть AN-8201 — 18500 тг, P83165 — 24500 тг, SP1376 — 9900 тг', 2),
                            m('client', 'а NEW12345?', 3)]))
        pj['exactTokens'] = ['NEW12345', 'AN-8201', '18500']          # the verifier's order: the new code, an old code, an old price
        docs = [(1, 'Свеча NEW12345 Iridium — 9900 тг', scope(ch)),
                (2, 'Масло 5W30 4л — 18500 тг', scope(ch)), (3, 'Фильтр топливный — 18500 тг', scope(ch)), (4, 'Диск тормозной — 18500 тг', scope(ch)),
                (5, 'Свеча AN-8201 — 18500 тг', scope(ch)), (6, 'Свеча NEW12345 чужого бота — 1 тг', scope(ch, other=True))]
        rows, params = exact_rows(pj, docs)
        c.check(bool(rows) and 'NEW12345' in rows[0], f'rows={rows} params={params}')
        c.check(not any('чужого бота' in r for r in rows), f'a foreign bot row leaked: {rows}')
        c.check(any('AN-8201' in r for r in rows), f'the second token got no row: {rows}')

    with Case(f'[{ch}] SQL: «а за 15000 есть получше?» keeps the article row; 15000 does not match 150000 / 115000') as c:
        pj = prep(body(ch, [m('client', 'нужен фильтр 90915-YZZE1', 1), m('business', 'есть, 4500 тг', 2), m('client', 'а за 15000 есть получше?', 3)]))
        invariants(c, pj)
        docs = [(1, 'Фильтр масляный 90915-YZZE1 Toyota — 4500 тг', scope(ch)), (2, 'Аккумулятор 100Ah — 150000 тг', scope(ch)),
                (3, 'Комплект ТО Land Cruiser — 115000 тг', scope(ch)), (4, 'Фильтр масляный Mann W712 премиум — 15000 тг', scope(ch))]
        rows, params = exact_rows(pj, docs)
        c.check(any('90915-YZZE1' in r for r in rows), f'the article row is gone: tokens={pj["exactTokens"]} rows={rows}')
        c.check(not any('150000' in r or '115000' in r for r in rows), f'a bare 15000 matched a longer number: {rows}')
        c.check(any('W712' in r for r in rows), f'the exact 15000 row is missing: {rows}')

    with Case(f'[{ch}] SQL: one article with many rows still returns three rows, newest first (unchanged)') as c:
        pj = prep(body(ch, [m('client', 'Есть NEW12345?', 1)]))
        docs = [(i, f'Свеча NEW12345 партия {i}', scope(ch)) for i in range(1, 5)]
        rows, _ = exact_rows(pj, docs)
        c.check(rows == ['Свеча NEW12345 партия 4', 'Свеча NEW12345 партия 3', 'Свеча NEW12345 партия 2'], f'rows={rows}')


with Case('fuzz (seeded, 300 chats): ≤ 900, the newest message and the direction always in the query, its code leads') as c:
    rnd = random.Random(1327)
    words = ['фильтр', 'масло', 'свеча', 'колодки', 'Camry', 'Prado', 'оригинал', 'аналог', 'доставка', 'сколько', 'есть', 'тг', 'ещё', 'нужно']
    bodies, meta = [], []
    for k in range(300):
        msgs = []
        for i in range(rnd.randint(1, 8)):
            role = rnd.choice(['client', 'business'])
            txt = ' '.join(rnd.choice(words + [f'OLD{rnd.randint(10000, 99999)}', str(rnd.randint(1000, 99999))]) for _ in range(rnd.randint(1, 90)))
            msgs.append(m(role, txt, i))
        code = f'NEW{k:03d}{rnd.randint(10, 99)}'
        newest = rnd.choice([f'а {code}?', f'{code} есть?', f'а сколько? {code}', 'а сколько?'])
        msgs.append(m('client', newest, 99))
        steer = rnd.choice([None, None, f'Направление про {code}', 'Предложить аналог подешевле'])
        bodies.append(body('WA' if k % 2 else 'TG', msgs, steer=steer))
        meta.append((newest, steer, code if code in newest else None))
    outs = prep_many(bodies)
    lost_q = lost_s = bad_tok = over = 0
    for (newest, steer, code), out in zip(meta, outs):
        over += len(out['queryText']) > 900 or len(out['trailingText']) > 500
        lost_q += not out['trailingText'].endswith(newest) or newest not in out['queryText']
        lost_s += bool(steer) and ('[направление] ' + steer) not in out['queryText']
        bad_tok += bool(code) and out['exactTokens'][:1] != [code]
    c.check(over == 0, f'{over} over budget')
    c.check(lost_q == 0, f'the newest message lost in {lost_q}/300')
    c.check(lost_s == 0, f'the direction lost in {lost_s}/300')
    c.check(bad_tok == 0, f'the newest code not first in {bad_tok}/300')

report()
