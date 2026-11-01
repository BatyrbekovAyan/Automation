#!/usr/bin/env python3
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §3, plan
docs/superpowers/plans/2026-10-25-personal-chats.md Task 1): the rule that says whether a chat is PERSONAL (the assistant
stays out of it) or BUSINESS, and the case table that keeps its twins equal.

One home for the rule: Tools/n8n/audience.py — the constants and RULE_JS, a single function `decideAudience(f)` that the
n8n Decide Audience Code node embeds verbatim and that the app's C# ChatAudienceRule repeats. One contract:
Tools/n8n/fixtures/audience-cases.json — {constants, cases}; the server tests here and the app's EditMode tests run the SAME
rows (the way fixtures/schedule-cases.json feeds bot_schedule.py and ScheduleModel).
The chat's own rows have a contract of their own: Tools/n8n/fixtures/chat-identity-cases.json — {cases: [{name, channel,
chatId, code, expectCode, expectNames}]}, `code: null` = the code not given (the runner leaves it out). CHAT_IDENTITY_JS runs
every row here; the app's C# twin ChatIdentity runs the SAME rows (ChatIdentityTests) — a new row goes into that file only.

What runs — the REAL RULE_JS in node, through harness.code as a Code body (the way test_set_schedule.py runs a Validate
body). No database, no n8n server, no network, never prod.

  every_case_matches                 decideAudience answers (expectKind, expectReason) for every row — and nothing else
  constants_match_the_fixture        the module's SETTLE_MINUTES / FRESH_HOURS / WIPE_DAYS are the table's constants
  whatsapp_contact_has_one_source    no switch to the webhook's contact_name: the WhatsApp contact is the chat card's
                                     contact.FullName (Decide Audience reads it), never a name Wappi may fill from a profile
  cases_have_exactly_ten_keys_and_unique_names
                                     the contract's shape, and every row the plan names is there
  rule_js_is_pure                    no clock, no n8n global, no require — and no top-level name but `decideAudience`
  case_values_stay_in_their_domains  the typed contract the C# twin parses (true/false/null, the three owner values …)
  the_table_covers_every_outcome     all seven results on both channels; every nonhuman marker of the module has a row
  the_table_pins_the_step_order      for every two steps that can answer differently, a row on which both apply and the
                                     earlier one wins — so a twin that orders the steps differently fails the table
  rule_js_carries_the_modules_constants   the suffix list, the service ids and the Telegram chat types in RULE_JS are the module's
  missing_facts_fail_closed          a fact that is not given is unknown, never a known «no»; an unknown channel is not decided
  chat_identity_cases_have_six_keys_and_unique_names
                                     the second contract's shape: one top-level key, six keys per row, no duplicate name
  chat_identity_truth_table          CHAT_IDENTITY_JS: a messages/get row names the chat by its id or, on WhatsApp only, by its
                                     @lid code (something before the suffix, not the chat id itself); any other id is another chat
  chat_identity_js_is_pure           ASCII, no clock / n8n global, no placeholder, the module's suffix, one top-level name

    python3 Tools/n8n/tests/test_audience_rule.py
"""
import json
import re
from pathlib import Path

from harness import N8N, Case, code, module, report

FIXTURE = N8N / 'fixtures' / 'audience-cases.json'
DOC = json.loads(FIXTURE.read_text(encoding='utf-8'))
CASES = DOC['cases']
KEYS = ('name', 'channel', 'chatId', 'chatType', 'flags', 'contact', 'history', 'owner', 'expectKind', 'expectReason')
FACTS = ('channel', 'chatId', 'chatType', 'flags', 'contact', 'history', 'owner')      # what the rule reads; the rest is the answer
PLAN_CASES = ('wa_group', 'wa_group_owner_business_still_nonhuman', 'wa_newsletter', 'wa_status_broadcast', 'wa_self',
              'wa_contact', 'wa_contact_owner_business', 'wa_history', 'wa_new', 'wa_history_unknown',
              'wa_lid_no_contact_new', 'wa_lid_no_contact_history', 'wa_owner_personal_on_new',
              'tg_contact', 'tg_bot', 'tg_support', 'tg_777000_flags_unknown', 'tg_self', 'tg_channel', 'tg_group_chat',
              'tg_flags_unknown_history_true', 'tg_new', 'tg_contact_unknown_history_unknown', 'tg_owner_business_on_contact')
REACHABLE = {('personal', 'nonhuman'), ('personal', 'unknown'), ('personal', 'owner'), ('business', 'owner'),
             ('personal', 'contact'), ('personal', 'history'), ('business', 'new')}

try:
    AUD = module('audience.py')
    AUD_ERR = None
except Exception as e:          # the module is the rule's one home — without it every check fails, readably
    AUD, AUD_ERR = None, e
NO_MODULE = f'Tools/n8n/audience.py: {AUD_ERR!r}'


def decide(inputs):
    """The answers of decideAudience for a list of fact dicts, in ONE node process. The Code body is RULE_JS byte for byte
    plus a runner (a synthetic one-node workflow — the shape the real Decide node will have, so harness.code runs it the same
    way); a throw is reported per input instead of killing the run."""
    runner = ("\nreturn $input.all().map((i) => {\n"
              "  try { return { json: decideAudience(i.json) }; } catch (e) { return { json: { threw: String(e) } }; }\n"
              "});")
    w = {'nodes': [{'name': 'Decide', 'parameters': {'jsCode': AUD.RULE_JS + runner}}]}
    return [o['json'] for o in code(w, 'Decide', items=[{'json': f} for f in inputs])]


# ---------------------------------------------------------------------------------------------- the four checks of the plan
with Case('every_case_matches: decideAudience (RULE_JS, in node) answers (expectKind, expectReason) for every row — and only {kind, reason}') as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        got = decide([{k: x[k] for k in FACTS} for x in CASES])
        c.check(len(got) == len(CASES), f'{len(got)} answers for {len(CASES)} rows')
        for x, g in zip(CASES, got):
            c.check(g == {'kind': x['expectKind'], 'reason': x['expectReason']},
                    f"{x['name']}: expected {x['expectKind']}/{x['expectReason']}, got {g}")

with Case("constants_match_the_fixture: the module's SETTLE_MINUTES / FRESH_HOURS / WIPE_DAYS are the table's constants") as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        k = DOC['constants']
        c.check(set(k) == {'settleMinutes', 'freshHours', 'wipeDays'}, f'constants keys {sorted(k)}')
        c.check((AUD.SETTLE_MINUTES, AUD.FRESH_HOURS, AUD.WIPE_DAYS) == (k.get('settleMinutes'), k.get('freshHours'), k.get('wipeDays')),
                f'module {(AUD.SETTLE_MINUTES, AUD.FRESH_HOURS, AUD.WIPE_DAYS)} vs table {k}')
        c.check(all(type(v) is int and v > 0 for v in k.values()), f'constants must be positive integers: {k}')
        c.check(Path(AUD.FIXTURE).resolve() == FIXTURE.resolve(), f'FIXTURE {AUD.FIXTURE} is not {FIXTURE}')

with Case('whatsapp_contact_has_one_source: no switch to the incoming webhook\'s contact_name — Wappi fills it from the '
          'profile name too, so a stranger would read as a saved contact; the contact is the chat card\'s contact.FullName') as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        c.check(not hasattr(AUD, 'WA_CONTACT_SOURCE'), 'audience.py names a WhatsApp contact source again (WA_CONTACT_SOURCE)')
        c.check('contact_name' not in AUD.RULE_JS, 'RULE_JS reads the webhook\'s contact_name')

with Case('cases_have_exactly_ten_keys_and_unique_names: two top-level keys, ten keys per row, no duplicate name, all 24 rows of the plan') as c:
    c.check(set(DOC) == {'constants', 'cases'}, f'top-level keys {sorted(DOC)}')
    bad = [x.get('name', '?') for x in CASES if tuple(sorted(x)) != tuple(sorted(KEYS))]
    c.check(not bad, f'rows without exactly the ten keys {KEYS}: {bad}')
    names = [x.get('name') for x in CASES]
    c.check(len(set(names)) == len(names), f'duplicate names: {sorted(n for n in set(names) if names.count(n) > 1)}')
    c.check(not [n for n in PLAN_CASES if n not in names], f'rows the plan names are missing: {[n for n in PLAN_CASES if n not in names]}')
    c.check(len(CASES) >= 24, f'{len(CASES)} rows')

with Case('rule_js_is_pure: no `$(`, `require(`, `Date`, n8n global or randomness in RULE_JS; the only top-level name is decideAudience') as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        js = AUD.RULE_JS
        for needle in ('$(', 'require(', 'Date', 'Math.random'):
            c.check(needle not in js, f'RULE_JS contains {needle!r}')
        c.check(not re.search(r'\$[A-Za-z_]', js), 'RULE_JS names an n8n global ($json, $input, $env …)')
        c.check('function decideAudience(f)' in js, 'RULE_JS does not define function decideAudience(f)')
        top = re.findall(r'^(?:async\s+)?(?:function|const|let|var|class)\s+(\w+)', js, re.M)
        c.check(top == ['decideAudience'], f'top-level names {top}: the Code node that embeds RULE_JS must not meet another one')

# ---------------------------------------------------------------------------------------------- the table is a good contract
with Case('case_values_stay_in_their_domains: channel, id, type, flags {bot,support,self}, true/false/null, owner, the seven results') as c:
    for x in CASES:
        if tuple(sorted(x)) != tuple(sorted(KEYS)):
            continue            # reported by cases_have_exactly_ten_keys_and_unique_names
        p = []
        if x['channel'] not in ('whatsapp', 'telegram'):
            p.append('channel')
        if not (isinstance(x['chatId'], str) and x['chatId']):
            p.append('chatId')
        if not isinstance(x['chatType'], str):
            p.append('chatType')
        fl = x['flags']
        if fl is not None and not (isinstance(fl, dict) and set(fl) == {'bot', 'support', 'self'} and all(isinstance(v, bool) for v in fl.values())):
            p.append('flags')
        for k in ('contact', 'history'):
            if x[k] is not None and not isinstance(x[k], bool):
                p.append(k)
        if x['owner'] not in (None, 'personal', 'business'):
            p.append('owner')
        if (x['expectKind'], x['expectReason']) not in REACHABLE:
            p.append('expectKind/expectReason')
        c.check(not p, f"{x['name']}: out of its domain: {p}")

with Case('the_table_covers_every_outcome: all seven results on both channels, and a nonhuman row for every marker of the module') as c:
    for ch in ('whatsapp', 'telegram'):
        seen = {(x['expectKind'], x['expectReason']) for x in CASES if x['channel'] == ch}
        c.check(seen == REACHABLE, f'{ch}: no row for {sorted(REACHABLE - seen)}')
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        def is_nonhuman(x):
            return (x['expectKind'], x['expectReason']) == ('personal', 'nonhuman')
        for s in AUD.WA_NONHUMAN_SUFFIXES:
            c.check(any(x['channel'] == 'whatsapp' and x['chatId'].endswith(s) and is_nonhuman(x) for x in CASES), f'no WhatsApp nonhuman row ends with {s}')
        for t in AUD.TG_SERVICE_IDS:
            c.check(any(x['channel'] == 'telegram' and x['chatId'] == t and is_nonhuman(x) for x in CASES), f'no Telegram nonhuman row for the service id {t}')
        for t in AUD.TG_NONHUMAN_TYPES:
            c.check(any(x['channel'] == 'telegram' and x['chatType'] == t and is_nonhuman(x) for x in CASES), f'no Telegram nonhuman row for the chat type {t}')

# The steps of the rule, in order (`type_unknown` is step 2a: a Telegram chat type that is not the person's, a known non-human
# one excepted — that is step 1). `fires` is a reference written for the table's sake — NOT the code under test: it says which
# steps apply to a row on their own, so the test can prove the table orders every pair of steps that can disagree.
STEPS = ('nonhuman', 'flags_unknown', 'type_unknown', 'owner', 'contact', 'history', 'unknown')
SAME_ANSWER = {('flags_unknown', 'type_unknown'), ('flags_unknown', 'unknown'), ('type_unknown', 'unknown')}
# ^ all three answer personal/unknown: there is no order among them to pin


def fires(x):
    tg = x['channel'] == 'telegram'
    fl = x['flags'] or {}
    if tg:
        nonhuman = (x['chatId'] in AUD.TG_SERVICE_IDS or x['chatType'] in AUD.TG_NONHUMAN_TYPES
                    or bool(fl.get('bot') or fl.get('support') or fl.get('self')))
    else:
        nonhuman = x['chatId'].endswith(tuple(AUD.WA_NONHUMAN_SUFFIXES)) or bool(fl.get('bot') or fl.get('self'))
    return {
        'nonhuman': ('personal', 'nonhuman') if nonhuman else None,
        'flags_unknown': ('personal', 'unknown') if tg and x['flags'] is None else None,
        'type_unknown': ('personal', 'unknown') if tg and x['chatType'] != AUD.TG_PERSON_TYPE else None,
        'owner': (x['owner'], 'owner') if x['owner'] else None,
        'contact': ('personal', 'contact') if x['contact'] is True else None,
        'history': ('personal', 'history') if x['history'] is True else None,
        'unknown': ('personal', 'unknown') if x['contact'] is None or x['history'] is None else None,
    }


with Case('the_table_pins_the_step_order: for every two steps that can answer differently, a row where both apply and the earlier wins') as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        rows = [(x, fires(x)) for x in CASES]
        reference = {x['name']: next((a[s] for s in STEPS if a[s]), ('business', 'new')) for x, a in rows}
        off = [x['name'] for x, _ in rows if reference[x['name']] != (x['expectKind'], x['expectReason'])]
        c.check(not off, f'the reference rule and the table disagree on: {off}')
        for i, a in enumerate(STEPS):
            for b in STEPS[i + 1:]:
                if (a, b) in SAME_ANSWER:
                    continue
                pins = [x['name'] for x, ap in rows
                        if ap[a] and ap[b] and ap[a] != ap[b] and (x['expectKind'], x['expectReason']) == ap[a]]
                c.check(pins, f'no row pins step «{a}» above step «{b}»: a twin that swaps them would pass the table')

# ---------------------------------------------------------------------------------------------- the JS itself
with Case("rule_js_carries_the_modules_constants: the suffix list, the service ids and the Telegram chat types inside RULE_JS are the module's") as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        c.check(json.dumps(list(AUD.WA_NONHUMAN_SUFFIXES)) in AUD.RULE_JS, f'WA_NONHUMAN_SUFFIXES {AUD.WA_NONHUMAN_SUFFIXES} not in RULE_JS')
        c.check(json.dumps(list(AUD.TG_SERVICE_IDS)) in AUD.RULE_JS, f'TG_SERVICE_IDS {AUD.TG_SERVICE_IDS} not in RULE_JS')
        c.check(f'const TG_NONHUMAN_TYPES = {json.dumps(list(AUD.TG_NONHUMAN_TYPES))};' in AUD.RULE_JS, f'TG_NONHUMAN_TYPES {AUD.TG_NONHUMAN_TYPES} not declared in RULE_JS')
        c.check(f'const TG_PERSON_TYPE = {json.dumps(AUD.TG_PERSON_TYPE)};' in AUD.RULE_JS, f'TG_PERSON_TYPE {AUD.TG_PERSON_TYPE!r} not declared in RULE_JS')
        c.check(not re.search(r'__[A-Z_]+__', AUD.RULE_JS), 'RULE_JS still carries a template placeholder')

with Case('missing_facts_fail_closed: a fact that is not given reads as unknown, never as a known «no»; an unknown channel is not decided') as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        wa, tg = '77000000001@c.us', '5550000001'
        card = {'bot': False, 'support': False, 'self': False}
        unknown = {'kind': 'personal', 'reason': 'unknown'}
        probes = [
            ('whatsapp: only the id is given', {'channel': 'whatsapp', 'chatId': wa}),
            ('whatsapp: contact known, history not given', {'channel': 'whatsapp', 'chatId': wa, 'contact': False}),
            ('whatsapp: history known, contact not given', {'channel': 'whatsapp', 'chatId': wa, 'history': False}),
            ('telegram: flags not given', {'channel': 'telegram', 'chatId': tg, 'chatType': 'user', 'contact': False, 'history': False}),
            ('telegram: chat type not given', {'channel': 'telegram', 'chatId': tg, 'flags': card, 'contact': False, 'history': False}),
            ('telegram: chat type is null', {'channel': 'telegram', 'chatId': tg, 'chatType': None, 'flags': card, 'contact': False, 'history': False}),
            ('unknown channel', {'channel': 'sms', 'chatId': wa, 'contact': False, 'history': False}),
            ('no channel', {'chatId': wa, 'contact': False, 'history': False}),
        ]
        for (title, _), g in zip(probes, decide([f for _, f in probes])):
            c.check(g == unknown, f'{title}: expected personal/unknown, got {g}')

# ---------------------------------------------------------------------------------------------- the chat's own rows
# The truth table lives in ONE file both twins read: this test runs CHAT_IDENTITY_JS over it, the app's ChatIdentityTests run
# the C# ChatIdentity over the same rows. A row: (title, channel, chat id, the code given — null = not given, the code that
# counts, {id: names this chat?}).
IDENTITY_FIXTURE = N8N / 'fixtures' / 'chat-identity-cases.json'
IDENTITY_DOC = json.loads(IDENTITY_FIXTURE.read_text(encoding='utf-8'))
IDENTITY_KEYS = ('name', 'channel', 'chatId', 'code', 'expectCode', 'expectNames')
IDENTITY = tuple((c['name'], c['channel'], c['chatId'], c['code'], c['expectCode'], c['expectNames'])
                 for c in IDENTITY_DOC.get('cases', []) if isinstance(c, dict) and set(c) == set(IDENTITY_KEYS))


def identity(rows):
    """chatIdentity (CHAT_IDENTITY_JS, in node) for each (channel, chatId, code, ids) → {code, names: [bool per id]}."""
    runner = ("\nreturn $input.all().map((i) => { const x = chatIdentity(i.json.channel, i.json.chatId, i.json.code);\n"
              "  return { json: { code: x.code, names: i.json.ids.map((id) => x.names(id)) } }; });")
    w = {'nodes': [{'name': 'Identity', 'parameters': {'jsCode': AUD.CHAT_IDENTITY_JS + runner}}]}
    items = [{'json': dict({'channel': ch, 'chatId': chat, 'ids': list(ids)}, **({} if code_ is None else {'code': code_}))}
             for ch, chat, code_, ids in rows]
    return [o['json'] for o in code(w, 'Identity', items=items)]


with Case('chat_identity_cases_have_six_keys_and_unique_names: one top-level key (cases), six keys per row, no duplicate name, '
          'an answer of the right type — the contract the app\'s ChatIdentityTests read too') as c:
    rows = IDENTITY_DOC.get('cases')
    c.check(set(IDENTITY_DOC) == {'cases'}, f'top-level keys {sorted(IDENTITY_DOC)}: the contract is {{cases}}')
    c.check(isinstance(rows, list) and len(rows) >= 13, 'the truth table collapsed — every check below would pass vacuously')
    for x in rows if isinstance(rows, list) else []:
        title = x.get('name') if isinstance(x, dict) else repr(x)
        c.check(isinstance(x, dict) and set(x) == set(IDENTITY_KEYS), f'{title}: the keys must be exactly {IDENTITY_KEYS}')
        if isinstance(x, dict) and set(x) == set(IDENTITY_KEYS):
            c.check(isinstance(x['expectCode'], str), f'{title}: expectCode must be a string («» = no code counts)')
            c.check(isinstance(x['expectNames'], dict) and x['expectNames']
                    and all(isinstance(v, bool) for v in x['expectNames'].values()),
                    f'{title}: expectNames must map at least one id to true / false')
    names = [x.get('name') for x in rows if isinstance(x, dict)] if isinstance(rows, list) else []
    c.check(len(names) == len(set(names)), 'case names must be unique')
    c.check(isinstance(rows, list) and len(IDENTITY) == len(rows), 'every row of the file must reach the truth table')

with Case('chat_identity_truth_table: a row names the chat by its id or, on WhatsApp only, by its @lid code (a string ending in '
          'the suffix with something before it, not the chat id itself); any other id is another chat') as c:
    c.check(AUD is not None, NO_MODULE)
    c.check(len(IDENTITY) >= 13, f'{IDENTITY_FIXTURE.name}: {len(IDENTITY)} rows read — the table collapsed')
    if AUD is not None:
        got = identity([(ch, chat, code_, ids) for _, ch, chat, code_, _, ids in IDENTITY])
        for (title, _, _, _, want_code, ids), g in zip(IDENTITY, got):
            want = {'code': want_code, 'names': list(ids.values())}
            c.check(g == want, f'{title}: got {g}, want {want}')

with Case('chat_identity_js_is_pure: ASCII, no clock / n8n global / require, no placeholder left, the module\'s suffix; one '
          'top-level name, chatIdentity, between its BEGIN and END lines') as c:
    c.check(AUD is not None, NO_MODULE)
    if AUD is not None:
        js = AUD.CHAT_IDENTITY_JS
        for needle in ('$(', 'require(', 'Date', 'Math.random'):
            c.check(needle not in js, f'CHAT_IDENTITY_JS contains {needle!r}')
        c.check(not re.search(r'\$[A-Za-z_]', js), 'CHAT_IDENTITY_JS names an n8n global ($json, $input, $env …)')
        c.check(js.isascii(), 'CHAT_IDENTITY_JS must be ASCII')
        c.check(not re.search(r'__[A-Z_]+__', js), 'CHAT_IDENTITY_JS still carries a template placeholder')
        c.check(f'const CODE_SUFFIX = {json.dumps(AUD.CHAT_CODE_SUFFIX)};' in js and AUD.CHAT_CODE_SUFFIX == '@lid',
                f'the suffix inside CHAT_IDENTITY_JS must be the module\'s CHAT_CODE_SUFFIX ({AUD.CHAT_CODE_SUFFIX!r})')
        c.check(js.startswith(AUD.CHAT_IDENTITY_BEGIN) and js.endswith(AUD.CHAT_IDENTITY_END)
                and js.count(AUD.CHAT_IDENTITY_BEGIN) == 1 and js.count(AUD.CHAT_IDENTITY_END) == 1,
                'CHAT_IDENTITY_JS must open with its BEGIN line and close with its END line, once each')
        top = re.findall(r'^(?:async\s+)?(?:function|const|let|var|class)\s+(\w+)', js, re.M)
        c.check(top == ['chatIdentity'], f'top-level names {top}: a Code node that embeds it must not meet another one')

report()
