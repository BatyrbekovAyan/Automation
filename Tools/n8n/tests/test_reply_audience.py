#!/usr/bin/env python3
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §4.2, §4.3; plan
docs/superpowers/plans/2026-10-25-personal-chats.md Task 4): the two bot templates decide at message entry whether a chat is a
client's, and touch nothing of a chat that is not — no answer, no history, no memory, no ledger, no dialog counted, no Wappi
call (Mark Read would mark the owner's own chat read on his phone).

  Webhook → Is Incoming? → If → Read Reply Mode (ONE statement: the mode, the schedule, the chat's decision that holds —
  audience.AUDIENCE_SQL — and the profile's connection moment and number) → Audience Known?
     [personal | business] → Business? → [business] Suppressed? … | [personal] Outcome «personal»
     [no decision]         → Decide Audience (crAudienceDec001, build-decide-audience.py) → Decided Business? (only the
                             sub-workflow's own answer: audience AND outcome business) → [business] Suppressed? …
                             | [else] Outcome — the sub-workflow's own personal outcome, anything else personal_unknown
  Suppressed? reads Read Reply Mode's row (after Decide Audience the item is the sub-workflow's answer).
  Acquire Send Lease re-reads the decision (audience.BUSINESS_SQL): a chat turned personal while the reply was being
  prepared gets no send and asks for no re-read; Cancel Label names it cancelled_personal, Record Cancel records it.
  Record Outgoing (the owner's sends from the phone, the API echo) records business chats only; anything else is
  ignored_personal (a group keeps ignored_group).

What runs — no n8n server, no network, never production:
  * the REAL graph of both templates from their Webhook (tests/graph_walk.py: n8n's own expression and filter engine, the
    real statements on session temp tables of the staging Postgres); its Execute Workflow node runs the REAL Decide
    Audience sub-workflow with the inputs the template maps — type-checked by n8n's own validateFieldType — against a
    stubbed Wappi (chats/filter, chats/get, messages/get);
  * the REAL reply chain beyond the claim (tests/reply_chain.py: Run.mode() follows the same gate) for the lease and the
    sends;
  * verify-history-handoff.py / verify-telegram-parity.py on scratch copies with one broken gate each.

  RM  read_mode_returns_audience_in_one_row
  P   personal_writes_nothing (a personal decision of the owner's and a computed one), personal_in_vybrat_writes_nothing
  D   undecided_goes_to_decide_then_business_continues (the graph to the claim; the chain to «sent»),
      undecided_personal_ends_with_its_outcome, decide_gets_the_nine_inputs_typed, missing_connection_passes_null,
      raw_bigint_fails_the_call, decide_passthrough_is_never_business, decide_answer_matrix, decide_outcomes_are_the_stamps
  S   suppressed_reads_the_mode_row_after_decide (and the pre-change expression would answer a «Выбрать» chat)
  L   lease_refuses_a_chat_turned_personal, lease_refuses_a_voided_decision, personal_lease_asks_no_recheck,
      cancel_label_names_personal_first
  O   record_outgoing_skips_personal_and_undecided
  V   check_audience_gate negatives: a write reachable from Audience Known?[false] around Decided Business?[true], Read Reply
      Mode without audience, an extra Postgres node between If and Audience Known? (+ a Wappi call on the personal branch,
      the outcome guard dropped, Suppressed? reading $json); parity: the WhatsApp gate differs from the Telegram one

    python3 Tools/n8n/tests/test_reply_audience.py
"""
import copy
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import reply_chain as rc
from graph_walk import walk_graph
from harness import N8N, WF, Case, code, expr, module, node, report, sql_literal as L, wf
from rc_events_chain import Session
from reply_chain import CHATS, TEMPLATES, Run, history, ledger, memory, msg

FILES = {'WA': '4wYitz5ek30SVNlT-WhatsApp_Bot.json', 'TG': '4VN3gsFaC2HUYmcc-Telegram_Bot.json'}
CHANNEL = {'WA': 'whatsapp', 'TG': 'telegram'}
SUB_FILE = 'crAudienceDec001-Decide_Audience.json'
SUB_ID = 'crAudienceDec001'
SUB = wf(SUB_FILE)
SUB_TRIGGER = 'When Executed by Another Workflow'
OWN = '77019990000'                 # the connected number
LID = '100000000000123@lid'         # the WhatsApp chat's code (the incoming webhook's whatsapp_chat_id)
WA_BASE, TG_BASE = 'https://wappi.pro/api/', 'https://wappi.pro/tapi/'
GATE = ('Read Reply Mode', 'Audience Known?', 'Business?', 'Decide Audience', 'Decided Business?')
FIVE = ('personal_settling', 'personal_unknown', 'personal_nonhuman', 'personal_contact', 'personal_history')

EXTRA = ('bot_profiles', 'bot_schedule', 'dialog_counts')
# LIKE copies the PUBLIC sequence as the id default: the temp copies get their own, so nothing shared moves
SETUP = (rc.SETUP + '\n' + ''.join(f'create temp table {t} (like public.{t} including all);\n' for t in EXTRA)
         + ''.join(f"create temp sequence {t}_aud_seq; alter table {t} alter column id set default nextval('{t}_aud_seq');\n"
                   for t in ('chat_history_events', 'n8n_chat_histories')))
WATCHED = ('chat_history_events', 'n8n_chat_histories', 'reply_events', 'dialog_counts', 'chat_reply_state', 'reply_mode_flags')


def now_s():
    return int(time.time())


# ---------------------------------------------------------------------------------------------------- Wappi
def card(ch, saved=False):
    """The chat's dialog as Decide Audience's Chat Card reads it (live check D1/D2): WhatsApp chats/filter — a saved contact
    is contact.FullName; Telegram chats/get — user.Contact, the flags, the type."""
    pid, chat = CHATS[ch]
    if ch == 'WA':
        return {'id': chat, 'contact': {'Found': True, 'FirstName': '', 'FullName': 'Айгерим' if saved else '', 'PushName': 'Клиент',
                                        'BusinessName': ''}, 'chat': {'VerifiedName': None, 'LID': LID}, 'group': None, 'isGroup': False,
                'name': '', 'last_message_data': '', 'unread_count': 1}
    return {'id': chat, 'user': {'Self': False, 'Contact': saved, 'MutualContact': saved, 'Deleted': False, 'Bot': False,
                                 'Support': False, 'FirstName': 'Тест', 'LastName': '', 'Username': '', 'Phone': ''},
            'type': 'user', 'name': '', 'unread_count': 1, 'isDeleted': False, 'last_message_id': '1'}


def row(ch, mid, t, from_me=False, body='сообщение'):
    """A messages/get row of the test chat."""
    pid, chat = CHATS[ch]
    return {'id': mid, 'chatId': chat, 'from': chat if not from_me else OWN + '@c.us', 'fromMe': from_me, 'time': t, 'body': body,
            'type': 'chat' if ch == 'WA' else 'text', 'caption': ''}


class Wappi:
    """Wappi for Decide Audience's two reads. `card` = the chat's dialog (None: not on the page); `page` = messages/get rows,
    NEWEST FIRST; `fail` = {node: answer}. Every call is recorded; any other request fails the test."""

    def __init__(self, ch, card=None, page=(), fail=None):
        self.ch, self.card, self.page, self.fail, self.calls = ch, card, list(page), fail or {}, []

    def __call__(self, call):
        self.calls.append(call)
        n, q, url = call['node'], call['query'] or {}, call['url']
        if n in self.fail:
            return self.fail[n]
        if n == 'Chat Card':
            want = TG_BASE + 'sync/chats/get' if self.ch == 'TG' else WA_BASE + 'sync/chats/filter'
            assert url == want, f'Chat Card asked {url}, not {want}'
            ds = [self.card] if self.card is not None else []
            return {'statusCode': 200, 'body': {'status': 'done', 'detail': 'Chats fetched', 'dialogs': ds, 'total_count': len(ds)}}
        if n == 'Recent Messages':
            assert url == (TG_BASE if self.ch == 'TG' else WA_BASE) + 'sync/messages/get', f'Recent Messages asked {url}'
            rows = self.page[:int(q.get('limit') or 50)]
            return {'statusCode': 200, 'body': {'status': 'done', 'messages': rows, 'total_count': len(rows), 'uuid': q.get('profile_id')}}
        raise AssertionError(f'unexpected HTTP node {n}: {url}')

    def nodes(self):
        return [c['node'] for c in self.calls]


def no_wappi(call):
    raise AssertionError(f'a Wappi call on a path that must make none: {call["node"]} {call["url"]}')


# ---------------------------------------------------------------------------------------------------- seeding + reading
def connect(pg, ch, ago='3 days', phone=OWN):
    """The bot's line (its bot_profiles row) and the connection row the app reported; returns the moment in unix ms."""
    pid, _ = CHATS[ch]
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, created_at) values "
         f"({L(pid)}, 'u-aud', {L(CHANNEL[ch])}, 'inst-aud', now() - interval {L(ago)}) on conflict do nothing;")
    pg.x(f"insert into audience_connections (profile_id, phone, connected_at) values ({L(pid)}, {L(phone)}, now() - interval {L(ago)}) "
         "on conflict (profile_id) do update set phone = excluded.phone, connected_at = excluded.connected_at;")
    return pg.q(f"select (extract(epoch from connected_at) * 1000)::bigint as ms from audience_connections where profile_id = {L(pid)}")[0]['ms']


def decide_row(pg, ch, kind, source='owner', reason=None, ago=None, chat=None, profile=None):
    """A chat_audience row: the owner's decision (always holds) or a computed one (holds while taken on the current
    connection); `ago` = how long before now it was taken."""
    pid, c = CHATS[ch]
    reason = reason or ('owner' if source == 'owner' else ('new' if kind == 'business' else 'contact'))
    when = f"now() - interval {L(ago)}" if ago else 'now()'
    pg.x(f"insert into chat_audience (profile_id, chat_id, kind, source, reason, version, decided_at) values "
         f"({L(profile or pid)}, {L(chat or c)}, {L(kind)}, {L(source)}, {L(reason)}, {1 if source == 'owner' else 0}, {when}) "
         "on conflict (profile_id, chat_id) do update set kind = excluded.kind, source = excluded.source, reason = excluded.reason, "
         "version = excluded.version, decided_at = excluded.decided_at;")


def decision(pg, ch, chat=None):
    pid, c = CHATS[ch]
    r = pg.q(f"select kind, source, reason from chat_audience where profile_id = {L(pid)} and chat_id = {L(chat or c)}")
    return r[0] if r else None


def counts(pg):
    return {t: pg.q(f'select count(*)::int as n from {t}')[0]['n'] for t in WATCHED}


NOTHING = {t: 0 for t in WATCHED}


def incoming(ch, mid='m-trig', t=None, body='Здравствуйте, сколько стоит букет?', chat=None, chat_type=None, is_bot=False):
    """An incoming webhook message (live check D6: the keys Wappi sends — chat_type, is_bot, whatsapp_chat_id on WhatsApp)."""
    m = msg(ch, mid, t if t is not None else now_s() - 60, body, chat=chat)
    m.update({'wh_type': 'incoming_message', 'chat_type': chat_type or ('dialog' if ch == 'WA' else 'user'), 'is_bot': is_bot})
    if ch == 'WA':
        m['whatsapp_chat_id'] = LID
    return m


def outgoing(ch, mid, body, wh_type, chat=None):
    """The owner's send as Wappi reports it to the bot: outgoing_message_phone (from the phone) / outgoing_message_api."""
    pid, c = CHATS[ch]
    m = msg(ch, mid, now_s() - 30, body, from_me=True, chat=chat)
    m.update({'wh_type': wh_type, 'to': chat or c, 'chatId': chat or c})
    return m


def walk(pg, ch, m, wappi=None, template=None, throws=None, stop=('Debounce Wait',)):
    """ONE execution of the template from its Webhook (the walk ends at `stop`); Decide Audience runs the real sub-workflow."""
    T = template if template is not None else TEMPLATES[ch]
    req = {'body': {'messages': [m]}, 'webhookUrl': 'https://n8n.example/webhook/' + m['profile_id']}
    return walk_graph(pg, T, 'Webhook', webhook=req, fake=wappi if wappi is not None else no_wappi, throws=throws or {},
                      subs={SUB_ID: SUB}, stop=stop)


def out(run, name):
    return [i['json'] for i in ((run.outputs.get(name) or [[]])[0] or [])]


def stamp(run):
    o = out(run, 'Outcome')
    return o[0] if len(o) == 1 else None


def sub_run(run):
    runs = (getattr(run, 'subs', None) or {}).get('Decide Audience') or []
    return runs[0] if runs else None


def sub_input(run):
    s = sub_run(run)
    got = out(s, SUB_TRIGGER) if s is not None else []
    return got[0] if got else None


def sub_answer(run):
    got = out(run, 'Decide Audience')
    return got[0] if got else None


ENTRY = ['Webhook', 'Is Incoming?', 'If', 'Read Reply Mode', 'Audience Known?']
TO_CLAIM = ['Suppressed?', 'Claim Event', 'Claimed?', 'Debounce Wait']


# ---------------------------------------------------------------------------------------------------- RM: the mode read
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'read_mode_returns_audience_in_one_row [{ch}]: ONE row {{suppressed, off_hours, audience, connected_at_ms, '
              'connection_phone} — no decision → NULL; the owner\'s personal / business; a computed decision taken before the '
              'current connection (another number) → NULL, taken after it → its kind; the owner\'s decision always holds; '
              'another profile\'s row is not this one; the moment as n8n hands a bigint (text) and the number') as c:
        with Session(SETUP) as pg:
            def read():
                r = walk(pg, ch, incoming(ch), stop=('Audience Known?', 'Suppressed?'))
                rows = out(r, 'Read Reply Mode')
                c.check(not r.halt and len(rows) == 1, f'Read Reply Mode must hand on exactly one row: {r} {rows}')
                return rows[0] if rows else {}

            r0 = read()
            c.check(set(r0) == {'suppressed', 'off_hours', 'audience', 'connected_at_ms', 'connection_phone'}, f'columns {sorted(r0)}')
            c.check(r0.get('audience') is None and r0.get('connected_at_ms') is None and r0.get('connection_phone') is None,
                    f'no decision, no connection: {r0}')
            ms = connect(pg, ch)
            r1 = read()
            c.check(r1.get('connected_at_ms') == str(ms) and r1.get('connection_phone') == OWN and r1.get('audience') is None,
                    f'the connection row (a bigint arrives as text): {r1} want {ms}')
            decide_row(pg, ch, 'personal')
            c.check(read().get('audience') == 'personal', 'the owner\'s personal decision')
            decide_row(pg, ch, 'business')
            c.check(read().get('audience') == 'business', 'the owner\'s business decision')
            pg.x(f"update audience_connections set connected_at = now() where profile_id = {L(pid)};")      # another number
            decide_row(pg, ch, 'business', source='default', reason='new', ago='1 day')
            c.check(read().get('audience') is None, 'a computed decision of the previous connection must not hold')
            decide_row(pg, ch, 'business', source='owner', ago='1 day')
            c.check(read().get('audience') == 'business', 'the owner\'s decision holds whatever the connection')
            pg.x(f"update chat_audience set decided_at = now() + interval '1 second', source = 'default', reason = 'new', version = 0 "
                 f"where profile_id = {L(pid)};")
            c.check(read().get('audience') == 'business', 'a computed decision taken on the current connection holds')
            pg.x(f"delete from chat_audience where profile_id = {L(pid)};")
            decide_row(pg, ch, 'business', profile='other-profile')
            c.check(read().get('audience') is None, 'another profile\'s decision for the same chat id must not leak')
            direct = pg.q(f"select {module('audience.py').AUDIENCE_SQL(L(pid), L(chat))} as a")[0]['a']
            c.check(read().get('audience') == direct, f'the column must be audience.AUDIENCE_SQL itself: {direct}')

# ---------------------------------------------------------------------------------------------------- P: personal
for ch, T in TEMPLATES.items():
    for label, seed in (('the owner\'s personal decision', lambda pg, ch=ch: decide_row(pg, ch, 'personal')),
                        ('a computed personal decision (a saved contact)', lambda pg, ch=ch: decide_row(pg, ch, 'personal', source='default',
                                                                                                          reason='contact'))):
        with Case(f'personal_writes_nothing [{ch}] {label}: from the Webhook straight to the Outcome stamp «personal» — no new row in '
                  'chat_history_events, n8n_chat_histories, reply_events, dialog_counts, chat_reply_state; no Wappi call; '
                  'Decide Audience never called') as c:
            with Session(SETUP) as pg:
                connect(pg, ch)
                seed(pg)
                r = walk(pg, ch, incoming(ch))
                c.check(r.trail == ENTRY + ['Business?', 'Outcome'], f'trail {r.trail} halt {r.halt}')
                s = stamp(r)
                c.check(s is not None and s.get('outcome') == 'personal' and s.get('stage') == 'Business?', f'stamp {s}')
                c.check(counts(pg) == NOTHING, f'rows written: {counts(pg)}')
                c.check(r.calls == [] and sub_run(r) is None, f'calls {r.calls} sub {sub_run(r)}')

    with Case(f'personal_in_vybrat_writes_nothing [{ch}]: a personal chat in «Выбрать» (the bot-wide row suppressed) still ends at '
              '«personal» — Record Suppressed (which writes the client\'s message into the history and the memory) never runs') as c:
        with Session(SETUP) as pg:
            pid, chat = CHATS[ch]
            connect(pg, ch)
            decide_row(pg, ch, 'personal')
            pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(pid)}, '*', true, 5);")
            r = walk(pg, ch, incoming(ch))
            c.check(r.trail == ENTRY + ['Business?', 'Outcome'] and (stamp(r) or {}).get('outcome') == 'personal',
                    f'trail {r.trail} stamp {stamp(r)}')
            c.check(counts(pg) == dict(NOTHING, reply_mode_flags=1), f'rows written: {counts(pg)}')

# ---------------------------------------------------------------------------------------------------- D: Decide Audience
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'undecided_goes_to_decide_then_business_continues [{ch}] (the graph): a new person\'s first message → Decide '
              'Audience (the real sub-workflow: the card, the history page) writes business / new → Decided Business? → '
              'Suppressed? → Claim Event claims it') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            m = incoming(ch)
            wappi = Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'], body=m['body'])])
            r = walk(pg, ch, m, wappi)
            c.check(r.trail == ENTRY + ['Decide Audience', 'Decided Business?'] + TO_CLAIM, f'trail {r.trail} halt {r.halt}')
            c.check(sub_answer(r) == {'audience': 'business', 'outcome': 'business'}, f'answer {sub_answer(r)}')
            c.check(decision(pg, ch) == {'kind': 'business', 'source': 'default', 'reason': 'new'}, f'decision {decision(pg, ch)}')
            c.check(ledger(pg, ch) == {m['id']: 'claimed'}, f'ledger {ledger(pg, ch)}')
            c.check([x['source'] for x in history(pg, ch)] == ['client'], f'history {history(pg, ch)}')
            c.check(wappi.nodes() == ['Chat Card', 'Recent Messages'], f'Wappi calls {wappi.nodes()}')
            c.check(not any('mark_all' in json.dumps(x.get('query') or {}) for x in wappi.calls), 'mark_all in a read')

    with Case(f'undecided_goes_to_decide_then_business_continues [{ch}] (the chain): reply_chain.Run.mode() runs the same gate and '
              'the real sub-workflow; the reply then goes the ordinary way to «sent» — the lease reads the decision Decide '
              'Audience wrote; the next message of the chat goes straight through Business?') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            m = incoming(ch)
            run = Run(T, pg, m, 'exec-D1')
            run.mode(fake=Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'], body=m['body'])]))
            c.check(not run.done and (run.named.get('Decide Audience') or {}).get('outcome') == 'business', f'mode {run.outcome} {run.named.get("Decide Audience")}')
            run.claim(); run.latest([m]); run.generate(); run.final([m]); run.lease()
            if not run.done:
                run.send_ok()
            c.check(run.outcome == 'sent' and run.lease_row.get('allowed') is True, f'outcome {run.outcome} lease {getattr(run, "lease_row", None)}')
            m2 = incoming(ch, 'm-next', body='А доставка есть?')
            r2 = Run(T, pg, m2, 'exec-D1b')
            r2.mode(fake=no_wappi)
            c.check(not r2.done and 'Decide Audience' not in r2.named and r2.named['Read Reply Mode'].get('audience') == 'business',
                    f'the second message must not ask again: {r2.named}')

    for label, seed, wappi_of, want, calls in (
            ('a saved contact', lambda pg: None, lambda m: Wappi(ch, card=card(ch, saved=True)), ('personal', 'contact'), ['Chat Card']),
            ('a conversation before the connection', lambda pg: None,
             lambda m: Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'], body=m['body']), row(ch, 'old', now_s() - 5 * 86400, True)]),
             ('personal', 'history'), ['Chat Card', 'Recent Messages']),
            ('the first 30 minutes after the connection', lambda pg: pg.x(
                f"update audience_connections set connected_at = now() - interval '10 minutes' where profile_id = {L(pid)};"),
             lambda m: Wappi(ch), None, []),
            ('Wappi did not answer the card', lambda pg: None,
             lambda m: Wappi(ch, fail={'Chat Card': {'statusCode': 500, 'body': {'status': 'error'}}}), None, ['Chat Card'])):
        with Case(f'undecided_personal_ends_with_its_outcome [{ch}] {label}: Decided Business? false → the Outcome stamp names '
                  'Decide Audience\'s own outcome; only the decision itself may be written, nothing else') as c:
            with Session(SETUP) as pg:
                connect(pg, ch)
                seed(pg)
                m = incoming(ch)
                wappi = wappi_of(m)
                r = walk(pg, ch, m, wappi)
                ans = sub_answer(r) or {}
                c.check(r.trail == ENTRY + ['Decide Audience', 'Decided Business?', 'Outcome'], f'trail {r.trail} halt {r.halt}')
                c.check(ans.get('audience') == 'personal' and ans.get('outcome') in FIVE, f'answer {ans}')
                s = stamp(r) or {}
                c.check(s.get('outcome') == ans.get('outcome') and s.get('stage') == 'Decided Business?', f'stamp {s} answer {ans}')
                d = decision(pg, ch)
                c.check((d['kind'], d['reason']) == want if want else d is None, f'decision {d}')
                c.check(counts(pg) == NOTHING and wappi.nodes() == calls, f'rows {counts(pg)} calls {wappi.nodes()}')

    with Case(f'undecided_personal_ends_with_its_outcome [{ch}] (the chain): reply_chain.Run.mode() stops at the same stamp') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            run = Run(T, pg, incoming(ch), 'exec-D2')
            run.mode(fake=Wappi(ch, card=card(ch, saved=True)))
            c.check(run.done and run.outcome == 'personal_contact', f'outcome {run.outcome}')
            c.check(counts(pg) == NOTHING, f'rows {counts(pg)}')

    with Case(f'decide_gets_the_nine_inputs_typed [{ch}]: the sub-workflow receives exactly its nine declared inputs — the channel, '
              'the profile, the chat = from, the code (WhatsApp: whatsapp_chat_id; Telegram: \'\'), the message id, the chat type, '
              'isBot a boolean, connectedAtMs a NUMBER (Postgres hands the bigint on as text), the connected number') as c:
        with Session(SETUP) as pg:
            ms = connect(pg, ch)
            m = incoming(ch)
            r = walk(pg, ch, m, Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'])]))
            got = sub_input(r)
            want = {'channel': CHANNEL[ch], 'profileId': pid, 'chatId': chat, 'chatLid': LID if ch == 'WA' else '', 'messageId': m['id'],
                    'chatType': m['chat_type'], 'isBot': False, 'connectedAtMs': ms, 'connectionPhone': OWN}
            c.check(got == want, f'inputs {got} want {want}')
            c.check(got is not None and isinstance(got.get('connectedAtMs'), int) and not isinstance(got.get('connectedAtMs'), bool)
                    and got.get('isBot') is False, f'types {got}')
            dn = node(T, 'Decide Audience')
            declared = [(v['name'], v['type']) for v in node(SUB, SUB_TRIGGER)['parameters']['workflowInputs']['values']]
            mapping = dn['parameters']['workflowInputs']
            c.check([(s['id'], s['type']) for s in mapping['schema']] == declared and set(mapping['value']) == {k for k, _ in declared},
                    f'the mapping must cover exactly the sub-workflow\'s declared inputs: {mapping.get("schema")}')
            c.check(dn['parameters']['workflowId'].get('value') == SUB_ID, dn['parameters']['workflowId'])

    with Case(f'missing_connection_passes_null [{ch}]: no connection row yet (the app has not reported it) — connectedAtMs and '
              'connectionPhone go as null (the declared types accept it), Decide Audience creates the row with the line\'s moment') as c:
        with Session(SETUP) as pg:
            pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, created_at) values ({L(pid)}, 'u-aud', "
                 f"{L(CHANNEL[ch])}, 'inst-aud', now() - interval '3 days');")
            m = incoming(ch)
            r = walk(pg, ch, m, Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'])]))
            got = sub_input(r) or {}
            c.check(got.get('connectedAtMs') is None and got.get('connectionPhone') is None, f'inputs {got}')
            c.check((sub_answer(r) or {}).get('outcome') == 'business' and decision(pg, ch) == {'kind': 'business', 'source': 'default', 'reason': 'new'},
                    f'answer {sub_answer(r)} decision {decision(pg, ch)} trail {r.trail}')

    if ch == 'TG':
        with Case('decide_gets_the_nine_inputs_typed [TG] a bot\'s reply (chat_type bot, is_bot true): isBot true — personal / nonhuman '
                  'without a single Wappi call') as c:
            with Session(SETUP) as pg:
                connect(pg, ch)
                r = walk(pg, ch, incoming(ch, chat_type='bot', is_bot=True), Wappi(ch))
                c.check((sub_input(r) or {}).get('isBot') is True and (stamp(r) or {}).get('outcome') == 'personal_nonhuman', f'{sub_input(r)} {stamp(r)}')
                c.check(r.calls == [] and decision(pg, ch) == {'kind': 'personal', 'source': 'default', 'reason': 'nonhuman'}, f'{r.calls} {decision(pg, ch)}')

    with Case(f'raw_bigint_fails_the_call [{ch}]: on a copy whose mapping hands Read Reply Mode\'s bigint on as it arrives (text), '
              'n8n\'s type check refuses the call before the sub-workflow starts — the node hands its input on, the stamp says '
              'personal_unknown, nothing is decided (why the template takes Number() of it)') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            bad = copy.deepcopy(T)
            vals = node(bad, 'Decide Audience')['parameters']['workflowInputs']['value'] if any(n['name'] == 'Decide Audience' for n in bad['nodes']) else {}
            vals['connectedAtMs'] = "={{ $('Read Reply Mode').first().json.connected_at_ms }}"
            r = walk(pg, ch, incoming(ch), Wappi(ch, card=card(ch)), template=bad)
            c.check(sub_run(r) is None and ('throw', 'Decide Audience') in r.events, f'events {r.events}')
            c.check((stamp(r) or {}).get('outcome') == 'personal_unknown' and decision(pg, ch) is None and counts(pg) == NOTHING,
                    f'stamp {stamp(r)} decision {decision(pg, ch)} rows {counts(pg)}')

    with Case(f'decide_passthrough_is_never_business [{ch}]: Decide Audience throwing outside its runner hands Read Reply Mode\'s '
              'row on (n8n-core) — Decided Business? reads the sub-workflow\'s own fields, so that row (even one saying audience '
              'business) and an error item never pass; a sub-workflow that fails answers an error item → personal_unknown') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            r = walk(pg, ch, incoming(ch), Wappi(ch, card=card(ch)), throws={'Decide Audience': 'getWorkflowInfo: boom'})
            c.check(r.trail == ENTRY + ['Decide Audience', 'Decided Business?', 'Outcome'] and (stamp(r) or {}).get('outcome') == 'personal_unknown',
                    f'trail {r.trail} stamp {stamp(r)}')
            c.check(decision(pg, ch) is None and counts(pg) == NOTHING, f'{decision(pg, ch)} {counts(pg)}')
            r2 = walk(pg, ch, incoming(ch, 'm-2'), Wappi(ch, card=card(ch)), throws={'Shape': 'the sub-workflow failed'})
            c.check((sub_answer(r2) or {}).get('error') and (stamp(r2) or {}).get('outcome') == 'personal_unknown', f'{sub_answer(r2)} {stamp(r2)}')
        cond = node(T, 'Decided Business?')['parameters']['conditions']['conditions'][0]['leftValue'] if any(n['name'] == 'Decided Business?' for n in T['nodes']) else '={{ true }}'
        for label, item, want in (('the sub-workflow\'s business answer', {'audience': 'business', 'outcome': 'business'}, True),
                                  ('audience business without its outcome', {'audience': 'business'}, False),
                                  ('outcome business without audience', {'outcome': 'business'}, False),
                                  ('a contradictory answer', {'audience': 'personal', 'outcome': 'business'}, False),
                                  ('Read Reply Mode\'s row handed on (saying business)', {'suppressed': False, 'off_hours': False,
                                   'audience': 'business', 'connected_at_ms': '1', 'connection_phone': OWN}, False),
                                  ('an error item', {'error': 'boom'}, False), ('an empty item', {}, False)):
            c.check(bool(expr(cond, item)) is want, f'Decided Business? on {label}: want {want}')

    with Case(f'decide_answer_matrix [{ch}]: the Outcome stamp after Decided Business? false carries the sub-workflow\'s own outcome '
              'when it is one of its five personal ones; an error item, the mode read\'s row handed on, a missing or unknown value '
              '→ personal_unknown; after Business? false → personal') as c:
        named = {'Webhook': {'body': {'messages': [incoming(ch)]}}}
        for item, want in ([({'audience': 'personal', 'outcome': o}, o) for o in FIVE]
                           + [({'audience': 'personal', 'outcome': 'business'}, 'personal_unknown'),
                              ({'audience': 'personal', 'outcome': 'personal_maybe'}, 'personal_unknown'),
                              ({'audience': 'personal'}, 'personal_unknown'), ({'error': 'boom'}, 'personal_unknown'), ({}, 'personal_unknown'),
                              ({'suppressed': True, 'off_hours': True, 'audience': None, 'connected_at_ms': None, 'connection_phone': None},
                               'personal_unknown'),
                              ({'outcome': 'suppressed', 'off_hours': True}, 'personal_unknown')]):
            got = code(T, 'Outcome', {'json': item}, named, prev_node='Decided Business?')[0]['json'].get('outcome')
            c.check(got == want, f'after Decided Business? {item} → {got!r}, want {want!r}')
        got = code(T, 'Outcome', {'json': {'suppressed': False, 'off_hours': False, 'audience': 'personal'}}, named, prev_node='Business?')[0]['json']
        c.check(got.get('outcome') == 'personal', f'after Business? → {got.get("outcome")!r}')

with Case('decide_outcomes_are_the_stamps: the five personal outcomes the stamp knows are exactly the ones Decide Audience\'s Shape '
          'and Plan Facts can answer (one contract, both sides)') as c:
    sub_js = '\n'.join(n['parameters'].get('jsCode', '') for n in SUB['nodes'] if n['type'] == 'n8n-nodes-base.code')
    produced = set(re.findall(r"'(personal_[a-z]+)'", sub_js))
    c.check(produced == set(FIVE), f'Decide Audience answers {sorted(produced)}')
    stamps = module('apply-outcome-stamps.py')
    c.check(tuple(getattr(stamps, 'DECIDE_PERSONAL_OUTCOMES', ())) == FIVE, f'apply-outcome-stamps: {getattr(stamps, "DECIDE_PERSONAL_OUTCOMES", None)}')

# ---------------------------------------------------------------------------------------------------- S: «Выбрать»
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'suppressed_reads_the_mode_row_after_decide [{ch}]: a new person\'s first message in «Выбрать» — Decide Audience makes '
              'the chat business, Suppressed? reads Read Reply Mode\'s row (not the sub-workflow\'s answer) → Record Suppressed '
              'writes the client\'s message to the history and the memory, the stamp says suppressed, nothing is claimed') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(pid)}, '*', true, 5);")
            m = incoming(ch)
            r = walk(pg, ch, m, Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'], body=m['body'])]))
            c.check(r.trail == ENTRY + ['Decide Audience', 'Decided Business?', 'Suppressed?', 'Record Suppressed', 'Outcome'],
                    f'trail {r.trail} halt {r.halt}')
            c.check((stamp(r) or {}).get('outcome') == 'suppressed', f'stamp {stamp(r)}')
            c.check([x['source'] for x in history(pg, ch)] == ['client'] and [x['type'] for x in memory(pg, ch)] == ['human'],
                    f'history {history(pg, ch)} memory {memory(pg, ch)}')
            c.check(ledger(pg, ch) == {} and decision(pg, ch) == {'kind': 'business', 'source': 'default', 'reason': 'new'},
                    f'ledger {ledger(pg, ch)} decision {decision(pg, ch)}')
        with Session(SETUP) as pg:
            connect(pg, ch)
            pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(pid)}, '*', true, 5);")
            old = copy.deepcopy(T)
            node(old, 'Suppressed?')['parameters']['conditions']['conditions'][0]['leftValue'] = '={{ $json.suppressed }}'
            m = incoming(ch)
            r = walk(pg, ch, m, Wappi(ch, card=card(ch), page=[row(ch, m['id'], m['time'], body=m['body'])]), template=old)
            c.check('Claim Event' in r.trail, f'premise: with Suppressed? reading $json the «Выбрать» chat would be answered: {r.trail}')

# ---------------------------------------------------------------------------------------------------- L: the lease
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'lease_refuses_a_chat_turned_personal [{ch}]: the owner makes the chat personal while the reply is being prepared — '
              'Acquire Send Lease refuses (personal), no re-read, no send; Cancel Label → cancelled_personal, Record Cancel '
              'records it and drops this run\'s draft from memory; a business chat (control) is sent') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            decide_row(pg, ch, 'business')
            m = incoming(ch, 'L1', 100)
            ok = Run(T, pg, m, 'exec-L1')
            ok.mode(); ok.claim(); ok.latest([m]); ok.generate(); ok.final([m]); ok.lease()
            if not ok.done:
                ok.send_ok()
            c.check(ok.outcome == 'sent' and ok.lease_row.get('personal') is False, f'control: {ok.outcome} {ok.lease_row}')
            m2 = incoming(ch, 'L2', 200)
            run = Run(T, pg, m2, 'exec-L2')
            run.mode(); run.claim(); run.latest([m2]); run.generate(); run.final([m2])
            pg.x(f"update chat_audience set kind = 'personal', version = 2, personal_since = now() where profile_id = {L(pid)};")
            run.lease()
            lr = run.lease_row
            c.check(lr.get('allowed') is False and lr.get('personal') is True and lr.get('recheck') is False, f'lease {lr}')
            c.check(run.outcome == 'cancelled_personal' and not hasattr(run, 'record'), f'outcome {run.outcome}')
            c.check(ledger(pg, ch).get('L2') == 'cancelled_personal', f'ledger {ledger(pg, ch)}')
            c.check(run.cancel and run.cancel.get('recorded') is True and run.cancel.get('dropped_ai_rows') == 1, f'cancel {run.cancel}')

    with Case(f'lease_refuses_a_voided_decision [{ch}]: a computed business decision stops holding when another number is connected '
              'while the reply is prepared — cancelled_personal') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            decide_row(pg, ch, 'business', source='default', reason='new')
            m = incoming(ch, 'V1', 100)
            run = Run(T, pg, m, 'exec-V1')
            run.mode(); run.claim(); run.latest([m]); run.generate(); run.final([m])
            pg.x(f"update audience_connections set connected_at = now() + interval '1 second', phone = '77010000000' where profile_id = {L(pid)};")
            run.lease()
            c.check(run.outcome == 'cancelled_personal' and run.lease_row.get('personal') is True, f'{run.outcome} {run.lease_row}')

    with Case(f'personal_lease_asks_no_recheck [{ch}]: a same-second peer missing from the snapshot (V5) would ask for a re-read — '
              'not when the chat has turned personal: refused at once') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            decide_row(pg, ch, 'business')
            ma, ms_ = incoming(ch, 'A', 100, 'Здравствуйте'), incoming(ch, 'S', 100, 'Сколько стоит букет?')
            ra, rs = Run(T, pg, ma, 'e-a'), Run(T, pg, ms_, 'e-s')
            ra.mode(); ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rs.mode(); rs.claim()
            pg.x(f"update chat_audience set kind = 'personal', version = 2 where profile_id = {L(pid)};")
            ra.lease()
            lr = ra.lease_row
            c.check(lr.get('peer_unseen') is True and lr.get('recheck') is False and lr.get('allowed') is False, f'lease {lr}')
            c.check(ra.outcome == 'cancelled_personal' and not getattr(ra, 'recheck_pending', False), f'{ra.outcome}')

    with Case(f'cancel_label_names_personal_first [{ch}]: after Send Allowed? a personal lease is cancelled_personal before any other '
              'reason (mode, busy, superseded keep theirs)') as c:
        named = {'Webhook': {'body': {'messages': [incoming(ch)]}}, 'Claim Event': {'history_watermark': 0}}
        for item, want in (({'personal': True, 'suppressed': True, 'lock_busy': True}, 'cancelled_personal'),
                           ({'personal': True}, 'cancelled_personal'), ({'personal': False, 'suppressed': True}, 'cancelled_mode'),
                           ({'personal': False, 'lock_busy': True}, 'cancelled_busy'), ({'personal': False}, 'superseded')):
            got = code(T, 'Cancel Label', {'json': item}, named, prev_node='Send Allowed?')[0]['json'].get('outcome')
            c.check(got == want, f'{item} → {got!r}, want {want!r}')

# ---------------------------------------------------------------------------------------------------- O: Record Outgoing
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'record_outgoing_skips_personal_and_undecided [{ch}]: the owner writing from the phone (and the API echo) is recorded '
              'only in a business chat — personal, undecided or a voided computed decision → ignored_personal, nothing written; '
              'business → recorded (owner_phone: history + the owner-marked memory row; outgoing_api: history only)') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            res = {}
            for label, seed in (('undecided', None), ('personal', 'personal')):
                if seed:
                    decide_row(pg, ch, seed)
                for wh in ('outgoing_message_phone', 'outgoing_message_api'):
                    res[(label, wh)] = rc.record_outgoing(T, pg, ch, f'{label}-{wh}', 'Хорошо, жду вас завтра в 15:00', 120, wh_type=wh)
            c.check(all(v.get('outcome') == 'ignored_personal' and v.get('remembered') is False for v in res.values()), f'{res}')
            c.check(counts(pg) == NOTHING, f'rows {counts(pg)}')
            decide_row(pg, ch, 'business', source='default', reason='new', ago='1 day')        # taken before this connection?
            pg.x(f"update audience_connections set connected_at = now() where profile_id = {L(pid)};")
            v = rc.record_outgoing(T, pg, ch, 'voided', 'Да', 120, wh_type='outgoing_message_phone')
            c.check(v.get('outcome') == 'ignored_personal', f'a voided computed decision: {v}')
            decide_row(pg, ch, 'business')
            p = rc.record_outgoing(T, pg, ch, 'biz-phone', 'Хорошо, жду вас завтра в 15:00', 120, wh_type='outgoing_message_phone')
            a = rc.record_outgoing(T, pg, ch, 'biz-api', 'Ответ через API', 121, wh_type='outgoing_message_api')
            c.check(p.get('outcome') == 'recorded_owner_phone' and p.get('remembered') is True, f'phone {p}')
            c.check(a.get('outcome') == 'recorded_outgoing_api' and a.get('remembered') is False, f'api {a}')
            c.check([(x['message_id'], x['source']) for x in history(pg, ch)] == [('biz-phone', 'owner_phone'), ('biz-api', 'outgoing_api')],
                    f'history {history(pg, ch)}')
            mem = memory(pg, ch)
            c.check(len(mem) == 1 and mem[0]['source'] == 'owner_phone' and mem[0]['content'].startswith('Владелец: '), f'memory {mem}')

    with Case(f'record_outgoing_skips_personal_and_undecided [{ch}] (the graph): an outgoing webhook of a personal chat goes Is Outgoing? → '
              'Outgoing Bound? → Record Outgoing → the stamp «ignored_personal»') as c:
        with Session(SETUP) as pg:
            connect(pg, ch)
            decide_row(pg, ch, 'personal')
            r = walk(pg, ch, outgoing(ch, 'o-1', 'Привет, это я', 'outgoing_message_phone'))
            c.check(r.trail == ['Webhook', 'Is Incoming?', 'Is Outgoing?', 'Outgoing Bound?', 'Record Outgoing', 'Outcome']
                    and (stamp(r) or {}).get('outcome') == 'ignored_personal', f'trail {r.trail} stamp {stamp(r)}')
            c.check(counts(pg) == NOTHING, f'rows {counts(pg)}')

    if ch == 'WA':
        with Case('record_outgoing_skips_personal_and_undecided [WA] a group keeps its own label (ignored_group), nothing written') as c:
            with Session(SETUP) as pg:
                connect(pg, ch)
                m = outgoing(ch, 'g-1', 'Всем привет', 'outgoing_message_phone', chat='120363000000000000@g.us')
                r = rc._sql(T, pg, 'Record Outgoing', {}, {'Webhook': {'body': {'messages': [m]}}}, 'exec-g')
                c.check(r.get('outcome') == 'ignored_group' and counts(pg) == NOTHING, f'{r} {counts(pg)}')


# ---------------------------------------------------------------------------------------------------- V: the verifiers on scratch
def scratch():
    d = Path(tempfile.mkdtemp(prefix='aud-gate-'))
    for f in WF.glob('*.json'):
        shutil.copy(f, d / f.name)
    return d


def run_tool(tool, d):
    r = subprocess.run([sys.executable, str(N8N / tool), '--dir', str(d)], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def nd(w, name):
    return next(x for x in w['nodes'] if x['name'] == name)


def link(*targets):
    return [{'node': t, 'type': 'main', 'index': 0} for t in targets]


def m_write_around_decided(w):
    w['connections']['Decide Audience'] = {'main': [link('Decided Business?', 'Record Suppressed')]}


def m_no_audience_column(w):
    p = nd(w, 'Read Reply Mode')['parameters']
    head = p['query'].index(module('audience.py').AUDIENCE_SQL('$1', '$2'))
    tail = p['query'].index(' as audience,', head) + len(' as audience,')
    p['query'] = p['query'][:head] + 'null::text as no_audience,' + p['query'][tail:]


def m_extra_postgres(w):
    extra = copy.deepcopy(nd(w, 'Count Dialog'))
    extra['name'], extra['id'] = 'Extra Read', 'aud-extra-read'
    w['nodes'].append(extra)
    w['connections']['Read Reply Mode'] = {'main': [link('Extra Read')]}
    w['connections']['Extra Read'] = {'main': [link('Audience Known?')]}


def m_mark_read_on_personal(w):
    w['connections']['Business?']['main'][1] = link('Outcome', 'Mark Read')


def m_no_outcome_guard(w):
    nd(w, 'Decided Business?')['parameters']['conditions']['conditions'][0]['leftValue'] = "={{ $json.audience === 'business' }}"


def m_suppressed_reads_json(w):
    nd(w, 'Suppressed?')['parameters']['conditions']['conditions'][0]['leftValue'] = '={{ $json.suppressed }}'


GATE_MUTATIONS = (
    ('a write node reachable from Audience Known?[false] around Decided Business?[true]', m_write_around_decided, 'audience gate'),
    ('Read Reply Mode without the audience column', m_no_audience_column, 'audience'),
    ('an extra Postgres node between If and Audience Known?', m_extra_postgres, 'Audience Known?'),
    ('Mark Read (a Wappi call) on the personal branch', m_mark_read_on_personal, 'audience gate'),
    ('Decided Business? without the outcome guard', m_no_outcome_guard, 'Decided Business?'),
    ('Suppressed? reads $json', m_suppressed_reads_json, 'Suppressed?'),
)

with Case('V0 verify-history-handoff.py and verify-telegram-parity.py pass the committed canon') as c:
    for tool, ok_line in (('verify-history-handoff.py', 'ALL HISTORY/HANDOFF ASSERTS PASSED'), ('verify-telegram-parity.py', 'ALL PARITY ASSERTS PASSED')):
        rc_, text = run_tool(tool, WF)
        c.check(rc_ == 0 and ok_line in text, f'{tool}: exit {rc_}: {text[-700:]}')

for label, mutate, needle in GATE_MUTATIONS:
    for ch, fname in FILES.items():
        with Case(f'V NEG [{ch}] {label}: verify-history-handoff.py fails, naming the file') as c:
            d = scratch()
            try:
                w = json.loads((d / fname).read_text(encoding='utf-8'))
                before = json.dumps(w, sort_keys=True)
                mutate(w)
                c.check(json.dumps(w, sort_keys=True) != before, f'{label}: the mutation changed nothing')
                (d / fname).write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
                rc_, text = run_tool('verify-history-handoff.py', d)
                fails = [ln for ln in text.splitlines() if ln.startswith('HISTORY/HANDOFF FAIL')]
                c.check(rc_ != 0 and any(needle in ln and fname in ln for ln in fails), f'exit {rc_}: {text[-700:]}')
            except (StopIteration, KeyError, ValueError, IndexError) as e:
                c.check(False, f'the mutation does not apply to the canon: {type(e).__name__}: {e}')
            finally:
                shutil.rmtree(d, ignore_errors=True)


def p_wa_gate_differs(d):
    f = d / FILES['WA']
    w = json.loads(f.read_text(encoding='utf-8'))
    nd(w, 'Business?')['parameters']['conditions']['conditions'][0]['leftValue'] = "={{ $json.audience !== 'personal' }}"
    f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def p_wa_decide_differs(d):
    f = d / FILES['WA']
    w = json.loads(f.read_text(encoding='utf-8'))
    nd(w, 'Decide Audience')['parameters']['workflowId']['value'] = 'crSomethingElse1'
    f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


for label, mutate in (('the WhatsApp Business? differs from the Telegram one', p_wa_gate_differs),
                      ('the WhatsApp Decide Audience calls another workflow', p_wa_decide_differs)):
    with Case(f'V NEG parity: {label} → verify-telegram-parity.py fails on the audience gate') as c:
        d = scratch()
        try:
            mutate(d)
            rc_, text = run_tool('verify-telegram-parity.py', d)
            c.check(rc_ != 0 and 'PARITY FAIL' in text and 'audience gate' in text, f'exit {rc_}: {text[-700:]}')
        except (StopIteration, KeyError, ValueError, IndexError) as e:
            c.check(False, f'the mutation does not apply to the canon: {type(e).__name__}: {e}')
        finally:
            shutil.rmtree(d, ignore_errors=True)

report()
