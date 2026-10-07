#!/usr/bin/env python3
"""V9, panel part (iteration 2026-09-28): the «Вместе» panel answers a Kazakh client in Kazakh, and an emotional
reaction without a request is not a «thank you».

Runs the REAL Prep, Assemble, Validate and Validate 2 Code bodies of the canonical Suggest Replies workflow
(WhatsApp- and Telegram-shaped bodies — the panel path is shared). No n8n, no network, no model: the model's answer is
fed to the validators as the `choices[0].message.content` it would return.

Contract (review V9 + the iteration README, refuter's corrections):
  * Prep names the client's language from its NEWEST message: `kz` (a Kazakh-specific letter and no Russian-only
    word), `mixed` (both), `ru` (no Kazakh-specific letter — Kazakh written without them stays unenforced, documented);
  * for `kz` the prompt carries «ЯЗЫК КЛИЕНТА — КАЗАХСКИЙ» after the niche block (whose Russian quotes used to win), never
    for `ru` / `mixed`;
  * the STRICT Validate (before the one retry) refuses a Kazakh client's set if any card's text is Russian (no
    Kazakh-specific letter in three or more Cyrillic words — a short «Бар, 35000 тг» is not refused), naming the language
    in the violation the retry receives; Russian / mixed clients keep today's checks byte for byte;
  * the LENIENT Validate 2 (after the retry) drops the Russian cards when at least one Kazakh card remains, otherwise
    keeps all — a language miss never costs the owner the round;
  * the ТРИВИАЛЬНЫЕ rule no longer contradicts ВОЗДЕРЖАНИЕ: thanks / «ок» / a confirmation still get 4 cards, a joke,
    compliment or emotional reaction without a request is NOT a thank-you (owner gate N2_banter_abstain, 68e1f3eb).

    python3 Tools/n8n/tests/test_suggest_lang.py
"""
import json

from harness import Case, node, report, run_js, wf

SUG = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')
PREP_JS = node(SUG, 'Prep')['parameters']['jsCode']
CODE = {n: node(SUG, n)['parameters']['jsCode'] for n in ('Assemble', 'Validate', 'Validate 2')}

CHANNELS = {
    'WA': {'channel': 'whatsapp', 'botWaId': 'wfV9wa00000001', 'botTgId': '-1'},
    'TG': {'channel': 'telegram', 'botWaId': '-1', 'botTgId': 'wfV9tg00000001'},
}

KZ_Q = 'iPhone 11 экранын ауыстыру қанша тұрады?'           # scenarios.v1.json pn_kz_question
MIXED_Q = 'Сәлем! порошок 10 кг бар ма, сколько за мешок?'  # pn_mixed_lang
RU_Q = 'Сколько стоит замена экрана iPhone 11?'
KZ_CARDS = [{'text': 'Сәлеметсіз бе! iPhone 11 экранын ауыстыру 35000 тг, нақты баға диагностикадан кейін.', 'label': 'Цена', 'move': 'Ответ'},
            {'text': 'Телефоныңыздың моделін нақтылап жібересіз бе?', 'label': 'Модель', 'move': 'Уточнить'},
            {'text': 'Бүгін әкелсеңіз, шебер қарап береді.', 'label': 'Привезти', 'move': 'К заказу'},
            {'text': 'Тексеріп, 15 минутта жазамын.', 'label': 'Пауза', 'move': 'Отложить'}]
RU_CARDS = [{'text': 'Замена экрана iPhone 11 стоит 35000 тг, точная сумма после диагностики.', 'label': 'Цена', 'move': 'Ответ'},
            {'text': 'Уточните, пожалуйста, модель телефона.', 'label': 'Модель', 'move': 'Уточнить'},
            {'text': 'Можете привезти сегодня, мастер посмотрит.', 'label': 'Привезти', 'move': 'К заказу'},
            {'text': 'Проверю наличие экрана и напишу через 15 минут.', 'label': 'Пауза', 'move': 'Отложить'}]


def body(ch, msgs, vertical='phone_repair'):
    return {'v': 1, 'requestSeq': 7, 'chatId': '77010001234@c.us' if ch == 'WA' else '5550001112', 'appUserId': 'v9-owner',
            'profileId': 'v9-prof', 'businessTypeId': vertical, 'businessName': 'V9', 'ownerPrompt': '', 'catalog': '',
            'messages': [{'role': r, 'text': t, 'ts': i} for i, (r, t) in enumerate(msgs)], 'steerTowardText': None,
            'lastIncomingText': None, **CHANNELS[ch]}


def prep_many(bodies):
    js = ('const P = new Function("$json", ' + json.dumps(PREP_JS) + ');\n'
          'return ' + json.dumps(bodies, ensure_ascii=False) + '.map(b => P({body: b})[0].json);')
    return run_js(js)


def assemble(prep_out):
    return run_js(CODE['Assemble'], {'json': {}}, {'Prep': prep_out})[0]['json']


def validate(name, prep_out, cards):
    """The validator over the model's answer `{"suggestions": cards}` (None = an unparseable answer)."""
    content = 'not json' if cards is None else json.dumps({'suggestions': cards}, ensure_ascii=False)
    asm = {'requestSeq': prep_out.get('requestSeq'), 'invalid': False}
    out = run_js(CODE[name], {'json': {'choices': [{'message': {'content': content}}]}}, {'Assemble': asm, 'Prep': prep_out})
    return out['json'] if isinstance(out, dict) else out[0]['json']


for ch in CHANNELS:
    kz, mixed, ru, kz_after_ru, ru_after_kz, emoji_after_kz = prep_many([
        body(ch, [('client', KZ_Q)]), body(ch, [('client', MIXED_Q)]), body(ch, [('client', RU_Q)]),
        body(ch, [('client', 'Здравствуйте, ремонтируете телефоны?'), ('client', KZ_Q)]),
        body(ch, [('client', KZ_Q), ('business', 'Иә, жөндейміз.'), ('client', RU_Q)]),
        body(ch, [('client', KZ_Q), ('client', '🙏')]),
    ])

    with Case(f'[{ch}] Prep names the client language from its newest message') as c:
        c.check(kz.get('clientLang') == 'kz', f'kz question → {kz.get("clientLang")!r}')
        c.check(mixed.get('clientLang') == 'mixed', f'mixed question → {mixed.get("clientLang")!r}')
        c.check(ru.get('clientLang') == 'ru', f'russian question → {ru.get("clientLang")!r}')
        c.check(kz_after_ru.get('clientLang') == 'kz', f'a Kazakh question after a Russian turn → {kz_after_ru.get("clientLang")!r}')
        c.check(ru_after_kz.get('clientLang') == 'ru', f'a Russian question after a Kazakh one → {ru_after_kz.get("clientLang")!r}')
        c.check(emoji_after_kz.get('clientLang') == 'kz', f'an emoji after a Kazakh question keeps the run\'s language → {emoji_after_kz.get("clientLang")!r}')

    with Case(f'[{ch}] Assemble: the Kazakh rule rides the prompt for a Kazakh client only, after the niche block') as c:
        sp = assemble(kz)['systemPrompt']
        rule = next((l for l in sp.split('\n') if l.startswith('ЯЗЫК КЛИЕНТА — КАЗАХСКИЙ')), '')
        c.check(bool(rule), 'no Kazakh rule for a Kazakh client')
        c.check('label' in rule and 'по-русски' in rule, f'the rule must keep label Russian (owner-facing): {rule!r}')
        c.check(sp.index('НИША (') < sp.index('ЯЗЫК КЛИЕНТА — КАЗАХСКИЙ') < sp.index('БЕЗОПАСНОСТЬ:'),
                'the language rule must come after the niche block (its Russian quotes) and before the closing rules')
        for other, name in ((ru, 'ru'), (mixed, 'mixed')):
            c.check('ЯЗЫК КЛИЕНТА' not in assemble(other)['systemPrompt'], f'a {name} client got the Kazakh rule')

    with Case(f'[{ch}] strict Validate: Russian cards for a Kazakh client fail the set (→ the one retry), with the reason') as c:
        v = validate('Validate', kz, RU_CARDS)
        c.check(v['ok'] is False and 'казах' in v['violation'], f'ok={v["ok"]} violation={v["violation"]!r}')
        v = validate('Validate', kz, KZ_CARDS[:3] + RU_CARDS[3:])
        c.check(v['ok'] is False, f'one Russian card among Kazakh ones must still fail: ok={v["ok"]}')
        v = validate('Validate', kz, KZ_CARDS)
        c.check(v['ok'] is True and v['violation'] == '', f'Kazakh cards: ok={v["ok"]} violation={v["violation"]!r}')
        v = validate('Validate', kz, KZ_CARDS[:3] + [{'text': 'Бар, 35000 тг', 'label': 'Коротко', 'move': 'Ответ'}])
        c.check(v['ok'] is True, f'a short card without Kazakh-specific letters must not fail the set: {v["violation"]!r}')
        v = validate('Validate', kz, [])
        c.check(v['ok'] is True and v['abstain'] is True, f'abstain stays valid: {v}')

    with Case(f'[{ch}] strict Validate: Russian and mixed clients keep today\'s verdicts') as c:
        for prep_out, name in ((ru, 'ru'), (mixed, 'mixed')):
            v = validate('Validate', prep_out, RU_CARDS)
            c.check(v['ok'] is True and v['violation'] == '', f'{name}: ok={v["ok"]} violation={v["violation"]!r}')
        dup = [dict(RU_CARDS[0]), dict(RU_CARDS[1], label='Цена')]
        v = validate('Validate', ru, dup)
        c.check(v['ok'] is False and 'заголовки повторяются' in v['violation'], f'title clash: {v["violation"]!r}')
        v = validate('Validate', kz, [dict(KZ_CARDS[0]), dict(KZ_CARDS[1], label='Цена')])
        c.check(v['ok'] is False and 'заголовки повторяются' in v['violation'], f'kz title clash keeps its own reason: {v["violation"]!r}')
        v = validate('Validate', ru, None)
        c.check(v['ok'] is False and v['violation'] == 'ответ не распознан', f'parse failure: {v["violation"]!r}')

    with Case(f'[{ch}] lenient Validate 2: Russian cards are dropped when a Kazakh one remains, never an error') as c:
        v = validate('Validate 2', kz, KZ_CARDS[:2] + RU_CARDS[2:])
        c.check(v['ok'] is True and [i['text'] for i in v['items']] == [x['text'] for x in KZ_CARDS[:2]], f'items={v["items"]}')
        v = validate('Validate 2', kz, RU_CARDS)
        c.check(v['ok'] is True and len(v['items']) == 4, f'all-Russian after the retry is kept, not an error: ok={v["ok"]} n={len(v["items"])}')
        v = validate('Validate 2', ru, RU_CARDS)
        c.check(v['ok'] is True and len(v['items']) == 4, f'russian client unchanged: {v}')
        v = validate('Validate 2', kz, [dict(KZ_CARDS[0]), dict(KZ_CARDS[1], label='Цена'), RU_CARDS[2]])
        c.check(v['ok'] is True and [i['text'] for i in v['items']] == [KZ_CARDS[0]['text']], f'title dedupe + language filter: {v["items"]}')


with Case('ТРИВИАЛЬНЫЕ agrees with ВОЗДЕРЖАНИЕ: thanks keep 4 cards, an emotional reaction is not a thank-you') as c:
    out = prep_many([body('WA', [('client', 'ахаха ну ты даёшь, красавчик 😂')], vertical='kaspi_seller')])[0]
    sp = assemble(out)['systemPrompt']
    trivial = next((l for l in sp.split('\n') if l.startswith('ТРИВИАЛЬНЫЕ СООБЩЕНИЯ')), '')
    abstain = next((l for l in sp.split('\n') if l.startswith('ВОЗДЕРЖАНИЕ')), '')
    c.check('воздержание только для сообщений не по адресу бизнеса' not in trivial,
            'the old tail «воздержание только для сообщений не по адресу бизнеса» still overrides ВОЗДЕРЖАНИЕ')
    c.check('НЕ повод для пустого массива' in trivial and '4 коротких варианта' in trivial, f'thanks must keep 4 cards: {trivial!r}')
    c.check('эмоциональная реакция' in trivial and 'ВОЗДЕРЖАНИЕ' in trivial, f'the reaction must route to ВОЗДЕРЖАНИЕ: {trivial!r}')
    c.check('ахаха ну ты даёшь, красавчик' in abstain, 'ВОЗДЕРЖАНИЕ lost its owner-approved example (68e1f3eb)')

report()
