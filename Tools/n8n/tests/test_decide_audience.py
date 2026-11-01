#!/usr/bin/env python3
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §3.1–§3.3, §4.2,
§4.3; plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 3; live checks
docs/audits/2026-10-25-personal-chats/live-checks.md): the sub-workflow crAudienceDec001 «Decide Audience»
(build-decide-audience.py) — what the two bot templates call for an incoming message of a chat that has no decision yet.
It collects the facts from Wappi, applies the rule (audience.RULE_JS), writes the decision once (audience.DECIDE_WRITE_SQL)
and, when the chat turns business, stores the start of the conversation (audience.BACKFILL_SQL):

  in   {channel, profileId, chatId, chatLid, messageId, chatType, isBot, connectedAtMs, connectionPhone} — exactly these
  out  ONE item {audience: business | personal, outcome: business | personal_settling | personal_unknown | personal_nonhuman |
       personal_contact | personal_history} — business only when the row Write Decision answered says business

Walks the CANONICAL graph from «When Executed by Another Workflow» (tests/graph_walk.py: n8n's executionOrder v1; the REAL
Code bodies, Ifs and expressions through n8n's own engine; the REAL statements on session temp tables of the staging
Postgres) with a stubbed Wappi:
  chats/get (Telegram)     the first `limit` dialogs, newest first
  chats/filter (WhatsApp)  every dialog whose id, name or last message holds client_name — the filter matches all three
  messages/get             order desc: the newest `limit` rows, newest first. order asc: WhatsApp the OLDEST `limit` rows;
                           Telegram the newest `limit` rows RE-SORTED oldest first — tapi ignores asc (live check
                           2026-10-28) — so a return to asc fails these scenarios (A1 proves it on copies of the canon)
No n8n server, no network, never production. A scenario runs on both channels wherever both apply.

  the window and the moment
    settling_stops_before_any_call_and_writes_nothing, settling_boundary_29_and_31_minutes,
    missing_connection_is_created_with_the_line_moment, missing_connection_of_a_young_line_is_settling,
    moment_as_text_is_read_from_the_row, malformed_input_is_unknown_no_calls
  not a person — no Wappi call at all, written as personal / nonhuman
    wa_newsletter_nonhuman_no_calls (every WhatsApp non-person suffix), wa_self_nonhuman, wa_is_bot_nonhuman_no_calls,
    tg_777000_no_calls, tg_channel_type_no_calls, tg_chat_type_bot_nonhuman_no_calls, tg_is_bot_nonhuman_no_calls
  the chat card
    wa_contact_written_without_history_call, tg_contact_written_without_history_call, tg_bot_from_chat_card,
    wa_card_picks_the_chat_among_filter_matches, wa_contact_name_is_not_read, wa_lid_chat_no_card_history_decides,
    wa_dialog_without_contact_is_unknown_not_written, tg_dialog_not_on_page_is_personal_unknown_not_written,
    card_failure_is_unknown_not_written
  a node that throws before its work (it hands its INPUT items on: no input may pass for the node's own answer)
    passthrough_ensure_connection_is_unknown_not_written, passthrough_chat_card_is_unknown_not_written,
    passthrough_recent_messages_is_unknown_not_written, passthrough_write_decision_is_unknown_no_backfill,
    passthrough_backfill_keeps_business
  the history page (ONE messages/get, order desc, HISTORY_LIMIT rows; the backfill keeps the newest RECENT_LIMIT of them)
    wa_new_person_business_after_one_history_call, tg_new_person_business_after_one_history_call,
    wa_old_chat_personal_history, tg_old_chat_personal_history, short_page_without_old_row_is_business,
    full_page_all_new_is_unknown_not_written, ten_newer_rows_then_an_older_one_is_history,
    twelve_message_new_conversation_is_business, page_without_trigger_is_unknown_not_written, empty_history_is_unknown,
    foreign_row_page_is_unknown_not_written, wa_rows_under_the_code_count_as_this_chat (history and backfill),
    row_without_time_is_unknown_not_written, history_500_is_personal_unknown_not_written
  the write and the backfill
    business_backfills_the_owners_first_message, backfill_uses_the_history_page_no_second_call,
    backfill_failure_keeps_business, write_failure_is_personal_unknown, second_run_reads_the_first_decision,
    owner_decision_set_meanwhile_is_kept
  the requests, the canon, the verifier
    the_requests, history_page_is_one_the_app_asks, A1 a_return_to_asc_fails_these_scenarios, no_url_carries_mark_all,
    protected_and_generated,
    V0 / V NEG verify-personal-chats.py --only decide on the canon and on scratch copies

    python3 Tools/n8n/tests/test_decide_audience.py
"""
import copy
import json
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from graph_walk import walk_graph
from harness import N8N, WF, Case, bind, module, node, report, sql_literal as L
from rc_events_chain import Session

FILE = 'crAudienceDec001-Decide_Audience.json'
WF_ID = 'crAudienceDec001'
TRIGGER = 'When Executed by Another Workflow'
GEN = N8N / 'build-decide-audience.py'
VERIFY = N8N / 'verify-personal-chats.py'
AUD = module('audience.py')
OWNER_MARK = module('apply-history-handoff.py').OWNER_MEMORY_PREFIX
PAGE = AUD.HISTORY_LIMIT        # the history page Decide Audience reads (the backfill keeps its newest AUD.RECENT_LIMIT rows)
WA_BASE, TG_BASE = 'https://wappi.pro/api/', 'https://wappi.pro/tapi/'
WAPPI_CRED = {'httpHeaderAuth': {'id': 'EuhhqAaV56DpoqAN', 'name': 'WappiAuthToken'}}
INPUTS = (('channel', 'string'), ('profileId', 'string'), ('chatId', 'string'), ('chatLid', 'string'),
          ('messageId', 'string'), ('chatType', 'string'), ('isBot', 'boolean'), ('connectedAtMs', 'number'),
          ('connectionPhone', 'string'))

TABLES = ('bot_profiles', 'chat_audience', 'audience_connections', 'chat_history_events', 'n8n_chat_histories')
# LIKE copies the PUBLIC sequence as the id default: the temp copies get their own, so nothing shared moves
SETUP = ("set timezone = 'UTC';\n"
         + ''.join(f'create temp table {t} (like public.{t} including all);\n' for t in TABLES)
         + ''.join(f"create temp sequence {t}_dec_seq; alter table {t} alter column id set default nextval('{t}_dec_seq');\n"
                   for t in ('chat_history_events', 'n8n_chat_histories')))

OWN = '77019990000'            # the connected number: WhatsApp's chat with oneself carries these digits
CH = {'WA': {'channel': 'whatsapp', 'pid': 'wa-dec-p1', 'other': 'wa-dec-foreign', 'chat': '77011234567@c.us',
             'chat2': '77017654321@c.us', 'lid': '100000000000123@lid', 'type': 'dialog', 'base': WA_BASE},
      'TG': {'channel': 'telegram', 'pid': 'tg-dec-p1', 'other': 'tg-dec-foreign', 'chat': '5512345678',
             'chat2': '5587654321', 'lid': '', 'type': 'user', 'base': TG_BASE}}
BUSINESS = {'audience': 'business', 'outcome': 'business'}


def personal(why):
    return {'audience': 'personal', 'outcome': 'personal_' + why}


def canon():
    return json.loads((WF / FILE).read_text(encoding='utf-8'))


def nd(w, name):
    return next(x for x in w['nodes'] if x['name'] == name)


# ---------------------------------------------------------------------------------------------------- Wappi
def wa_row(chat, mid, from_me, text, t, type_='chat', code=None, **extra):
    """A WhatsApp messages/get row (wa-docs.txt «Получить сообщения из чата»; the keys the live check read). `code` = the
    chat's @lid code: Wappi lists the messages of some chats under it — the row's chatId and the client's `from` carry it."""
    key = code or chat
    r = {'id': mid, 'type': type_, 'from': OWN + '@c.us' if from_me else key, 'to': key if from_me else OWN + '@c.us',
         'fromMe': from_me, 'senderName': 'Владелец' if from_me else 'Клиент', 'time': t, 'body': text, 'stanzaId': '',
         'chatId': key, 'isForwarded': False, 'isReply': False, 'caption': '', 'isRead': False, 'delivery_status': 'delivered',
         's3Info': {}, 'isEdited': False, 'isFromAPI': False, 'isDeleted': False}
    r.update(extra)
    return r


def tg_row(chat, mid, from_me, text, t, type_='text', code=None, **extra):
    """A Telegram messages/get row (Tools/tapi/SHAPES.md: a private chat's incoming row carries from == chatId, the owner's
    own row the profile user's id)."""
    r = {'id': mid, 'type': type_, 'from': '996500000001' if from_me else chat, 'to': chat if from_me else '996500000001',
         'fromMe': from_me, 'senderName': 'Владелец' if from_me else 'Клиент', 'time': t, 'body': text, 'stanzaId': '',
         'chatId': chat, 'isForwarded': False, 'isReply': False, 'caption': '', 'delivery_status': 'delivered', 's3Info': {}}
    r.update(extra)
    return r


def row_of(ch):
    return wa_row if ch == 'WA' else tg_row


def wa_dialog(chat, full_name='', name='', last='', lid=''):
    """A WhatsApp chats/filter dialog (wa-docs.txt «Получить список чатов с фильтром»; the live check: the list names the chat
    by its number and keeps the code in chat.LID). contact.FullName — a name from the phone's address book — is the contact;
    PushName is the person's own profile name, a stranger has one too."""
    return {'id': chat, 'contact': {'Found': True, 'FirstName': '', 'FullName': full_name, 'PushName': 'Клиент',
                                    'BusinessName': ''},
            'chat': {'VerifiedName': None, 'LID': lid}, 'group': None, 'isGroup': False, 'name': name,
            'last_message_data': last, 'unread_count': 1}


def tg_dialog(chat, contact=False, bot=False, support=False, self_=False, type_='user'):
    """A Telegram chats/get dialog (tg-docs.txt «Получить список чатов»; the live check: Contact / Bot / Support / Self and the
    type match what the phone shows)."""
    return {'id': chat, 'user': {'Self': self_, 'Contact': contact, 'MutualContact': contact, 'Deleted': False, 'Bot': bot,
                                 'Support': support, 'FirstName': 'Тест', 'LastName': '', 'Username': '', 'Phone': ''},
            'type': type_, 'name': '', 'unread_count': 1, 'isDeleted': False, 'last_message_id': '1'}


def stranger(ch, chat):
    """The chat card of a person who is not a saved contact."""
    return wa_dialog(chat) if ch == 'WA' else tg_dialog(chat)


def saved(ch, chat):
    """The chat card of a saved contact."""
    return wa_dialog(chat, full_name='Айгерим') if ch == 'WA' else tg_dialog(chat, contact=True)


class Fake:
    """Wappi for the two reads of Decide Audience. `dialogs` = {profile: [dialogs, newest first]} — chats/get answers the
    first `limit` of them (order asc: the oldest first), chats/filter those whose id, name or last message holds
    client_name (all of them without one). `pages` = {(profile, chat id): [rows NEWEST FIRST]} — messages/get with order
    desc answers the newest `limit` rows; with order asc WhatsApp answers the OLDEST `limit` rows, Telegram the newest
    `limit` rows re-sorted oldest first (tapi's real asc). `faults` = {node: answer or callable(call) -> answer}; an
    answer is {statusCode, body} or {throw: message}. `served` records what each read handed back."""

    def __init__(self, dialogs=None, pages=None, faults=None):
        self.dialogs, self.pages, self.faults, self.served = dialogs or {}, pages or {}, faults or {}, []

    def __call__(self, call):
        n, q, url = call['node'], call['query'] or {}, call['url']
        if n in self.faults:
            f = self.faults[n]
            return f(call) if callable(f) else f
        pid = q.get('profile_id')
        if n == 'Chat Card':
            ds = list(self.dialogs.get(pid, []))
            if url == TG_BASE + 'sync/chats/get':
                if q.get('order') == 'asc':
                    ds.reverse()
                off, lim = int(q.get('offset') or 0), int(q.get('limit') or 200)
                ds = ds[off:off + lim]
            elif url == WA_BASE + 'sync/chats/filter':
                needle = str(q.get('client_name') or '')
                ds = [d for d in ds if needle in ' '.join(str(d.get(k) or '') for k in ('id', 'name', 'last_message_data'))]
            else:
                raise AssertionError(f'Chat Card asked {url}')
            self.served.append((n, [d['id'] for d in ds]))
            return {'statusCode': 200, 'body': {'status': 'done', 'detail': 'Chats fetched', 'dialogs': ds, 'total_count': len(ds)}}
        if n == 'Recent Messages':
            if url not in (WA_BASE + 'sync/messages/get', TG_BASE + 'sync/messages/get'):
                raise AssertionError(f'Recent Messages asked {url}')
            rows = list(self.pages.get((pid, q.get('chat_id')), []))
            lim = int(q.get('limit') or 50)
            if q.get('order') == 'asc':
                rows = list(reversed(rows[:lim])) if url.startswith(TG_BASE) else list(reversed(rows))[:lim]
            else:
                rows = rows[:lim]
            self.served.append((n, [m.get('id') for m in rows]))
            return {'statusCode': 200, 'body': {'status': 'done', 'timestamp': int(time.time()), 'messages': rows,
                                                'total_count': len(rows), 'uuid': pid}}
        raise AssertionError(f'unexpected HTTP node {n}')


# ---------------------------------------------------------------------------------------------------- seeding + reading
def profile(pg, pid, channel, age='3 days'):
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, created_at) values "
         f"({L(pid)}, 'u-dec', {L(channel)}, 'inst-dec', now() - interval {L(age)});")


def conn(pg, pid):
    r = pg.q(f"select phone, (extract(epoch from connected_at) * 1000)::bigint as ms from audience_connections "
             f"where profile_id = {L(pid)}")
    return r[0] if r else None


def connect(pg, pid, ago='3 days', phone=OWN):
    """The connection row the app reported (Read Reply Mode hands its moment and number in); its moment, in unix ms."""
    pg.x(f"insert into audience_connections (profile_id, phone, connected_at) values ({L(pid)}, {L(phone)}, now() - interval {L(ago)});")
    return conn(pg, pid)['ms']


def created_ms(pg, pid):
    return pg.q(f"select (extract(epoch from created_at) * 1000)::bigint as ms from bot_profiles where profile_id = {L(pid)}")[0]['ms']


def decision(pg, pid, chat):
    r = pg.q(f"select kind, source, reason from chat_audience where profile_id = {L(pid)} and chat_id = {L(chat)}")
    return r[0] if r else None


def history(pg, pid, chat):
    return [(r['message_id'], r['source']) for r in pg.q(
        f"select message_id, source from chat_history_events where profile_id = {L(pid)} and chat_id = {L(chat)} order by id")]


def memory(pg, pid, chat):
    return [r['message'] for r in pg.q(f"select message from n8n_chat_histories where session_id = {L(pid + ':' + chat)} order by id")]


def counts(pg):
    return {t: pg.q(f'select count(*)::int as n from {t}')[0]['n']
            for t in ('chat_audience', 'audience_connections', 'chat_history_events', 'n8n_chat_histories')}


def inputs(C, moment, chat=None, mid='m-trig', chat_type=None, is_bot=False, lid=None, phone=OWN, **extra):
    """The template's item for Decide Audience (plan Task 4: the incoming webhook's message + Read Reply Mode's row)."""
    d = {'channel': C['channel'], 'profileId': C['pid'], 'chatId': chat or C['chat'], 'chatLid': C['lid'] if lid is None else lid,
         'messageId': mid, 'chatType': C['type'] if chat_type is None else chat_type, 'isBot': is_bot,
         'connectedAtMs': moment, 'connectionPhone': phone}
    d.update(extra)
    return d


# ---------------------------------------------------------------------------------------------------- one execution
CALLS = []                      # every request of every canonical walk: no_url_carries_mark_all reads them all


def decide(pg, inp, fake=None, faults=None, workflow=None, throws=None):
    r = walk_graph(pg, workflow or canon(), TRIGGER, items=[{'json': inp}], fake=fake or Fake(), faults=faults or {},
                   throws=throws or {})
    if workflow is None:
        CALLS.extend(r.calls)
    return r


def answer(r):
    """What the calling template receives: n8n hands back the LAST node's items — it must be Shape, with exactly ONE item
    carrying exactly {audience, outcome}. Anything else → None (the check reading it fails, printing the run)."""
    if r.halt or not r.trail or r.trail[-1] != 'Shape':
        return None
    out = (r.outputs.get('Shape') or [[]])[0]
    return out[0]['json'] if len(out) == 1 and set(out[0]['json']) == {'audience', 'outcome'} else None


def wappi(r):
    return [x['node'] for x in r.calls]


def statements(r):
    """The Postgres nodes that ran. An unknown verdict must reach none (with the moment handed in): DECIDE_WRITE_SQL would
    refuse the pair anyway, so only this shows Persist? keeping a needless statement (≈ 1.9 s on prod) off the hot path."""
    return [e[1] for e in r.events if e[0] == 'sql']


def now_s():
    return int(time.time())


def old_s(moment_ms, days=2):
    """A message time (unix seconds) `days` before the connection moment."""
    return moment_ms // 1000 - days * 86400


NOTHING = {'chat_audience': 0, 'chat_history_events': 0, 'n8n_chat_histories': 0}


def nothing_written(pg):
    return {k: v for k, v in counts(pg).items() if k != 'audience_connections'} == NOTHING


# ---------------------------------------------------------------------------------------------------- shared scenarios
def new_person(pg, ch, page=None, card=None):
    """A new client's first message, connected three days ago: the moment, and Wappi answering a stranger's card and a
    page that holds only the trigger."""
    C = CH[ch]
    profile(pg, C['pid'], C['channel'])
    moment = connect(pg, C['pid'])
    trig = row_of(ch)(C['chat'], 'm-trig', False, 'здравствуйте, розы есть?', now_s() - 60)
    fake = Fake(dialogs={C['pid']: [card if card is not None else stranger(ch, C['chat'])]},
                pages={(C['pid'], C['chat']): page if page is not None else [trig]})
    return moment, fake


def old_chat(pg, ch, workflow=None):
    """A stranger the owner wrote with before the connection: 12 messages, the 4 oldest from before it, the trigger newest.
    The newest page holds the old rows; WhatsApp's honest «limit 1, asc» would answer the oldest row only — no trigger; tapi's
    asc re-sorts the newest page, and a «limit 1, asc» read there is the trigger itself — the chat would look new."""
    C, row = CH[ch], row_of(ch)
    profile(pg, C['pid'], C['channel'])
    moment = connect(pg, C['pid'])
    rows = [row(C['chat'], f'm-new{k}', k % 2 == 1, f'новое {k}', now_s() - 60 * (k + 1)) for k in range(8)]
    rows[0] = row(C['chat'], 'm-trig', False, 'привет, как дела?', now_s() - 30)
    rows += [row(C['chat'], f'm-old{k}', k % 2 == 0, f'старое {k}', old_s(moment, 2 + k)) for k in range(4)]
    fake = Fake(dialogs={C['pid']: [stranger(ch, C['chat'])]}, pages={(C['pid'], C['chat']): rows})
    return decide(pg, inputs(C, moment), fake, workflow=workflow), fake


def conversation(pg, ch, workflow=None, throws=None):
    """A new client the owner wrote to first after the connection: the page (newest first) is the trigger, the client's
    second message, the owner's reply, the client's first message. The backfill stores the three before the trigger,
    oldest first; the trigger goes on through the template's normal path."""
    C, row = CH[ch], row_of(ch)
    page = [row(C['chat'], 'm-trig', False, 'а доставка есть?', now_s() - 60),
            row(C['chat'], 'm-c2', False, 'на завтра можно?', now_s() - 120),
            row(C['chat'], 'm-own', True, 'Здравствуйте! Розы есть.', now_s() - 300),
            row(C['chat'], 'm-c1', False, 'есть розы?', now_s() - 600)]
    moment, fake = new_person(pg, ch, page=page)
    return decide(pg, inputs(C, moment), fake, workflow=workflow, throws=throws), page


CONVERSATION_HISTORY = [('m-c1', 'client'), ('m-own', 'owner_phone'), ('m-c2', 'client')]


def conversation_memory_ok(mem):
    return ([(m.get('type'), m.get('content'), (m.get('additional_kwargs') or {}).get('source')) for m in mem]
            == [('human', 'есть розы?', 'client'), ('ai', OWNER_MARK + 'Здравствуйте! Розы есть.', 'owner_phone'),
                ('human', 'на завтра можно?', 'client')])


# ================================================================================================ the window and the moment
for ch, C in CH.items():
    P, row = C['pid'], row_of(ch)
    with Case(f'settling_stops_before_any_call_and_writes_nothing [{ch}]: connected 10 minutes ago — no Wappi call, no '
              'statement, personal_settling straight from Plan Facts to Shape') as c:
        with Session(SETUP) as pg:
            profile(pg, P, C['channel'])
            moment = connect(pg, P, '10 minutes')
            fake = Fake(dialogs={P: [stranger(ch, C['chat'])]}, pages={(P, C['chat']): [row(C['chat'], 'm-trig', False, 'привет', now_s())]})
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('settling'), f'{r}')
            c.check(not r.calls and not statements(r), f'calls {wappi(r)} statements {statements(r)}')
            c.check(r.trail == [TRIGGER, 'Has Connection?', 'Plan Facts', 'Can Decide?', 'Shape'], f'{r.trail}')
            c.check(nothing_written(pg), f'{counts(pg)}')

    with Case(f'settling_boundary_29_and_31_minutes [{ch}]: 29 minutes after the connection nothing is asked; 31 minutes '
              f'after it the chat is decided (a new person → business)') as c:
        for ago, expect, asked in (('29 minutes', personal('settling'), []), ('31 minutes', BUSINESS, ['Chat Card', 'Recent Messages'])):
            with Session(SETUP) as pg:
                profile(pg, P, C['channel'])
                moment = connect(pg, P, ago)
                fake = Fake(dialogs={P: [stranger(ch, C['chat'])]},
                            pages={(P, C['chat']): [row(C['chat'], 'm-trig', False, 'привет', now_s() - 5)]})
                r = decide(pg, inputs(C, moment), fake)
                c.check(answer(r) == expect and wappi(r) == asked, f'{ago}: {answer(r)} {wappi(r)} {r}')

    with Case(f'missing_connection_is_created_with_the_line_moment [{ch}]: no moment handed in → Ensure Connection creates the '
              'connection row with the line\'s moment (the profile\'s created_at) and no number, and the chat is decided on it; '
              'a second run reads the same row; a moment handed in never runs Ensure Connection') as c:
        with Session(SETUP) as pg:
            profile(pg, P, C['channel'], '3 days')
            moment, fake = None, Fake(dialogs={P: [stranger(ch, C['chat'])]},
                                      pages={(P, C['chat']): [row(C['chat'], 'm-trig', False, 'привет', now_s() - 60)]})
            r = decide(pg, inputs(C, None, phone=None), fake)
            c.check(answer(r) == BUSINESS and r.ran('Ensure Connection'), f'{answer(r)} {r}')
            c.check(conn(pg, P) == {'phone': None, 'ms': created_ms(pg, P)}, f'the row: {conn(pg, P)} vs {created_ms(pg, P)}')
            again = decide(pg, inputs(C, None, mid='m-2', phone=None), Fake(dialogs={P: [stranger(ch, C['chat2'])]}))
            c.check(again.ran('Ensure Connection') and conn(pg, P) == {'phone': None, 'ms': created_ms(pg, P)},
                    f'the second run: {conn(pg, P)} {again}')
            handed = decide(pg, inputs(C, created_ms(pg, P), chat=C['chat2'], mid='m-3'),
                            Fake(dialogs={P: [stranger(ch, C['chat2'])]},
                                 pages={(P, C['chat2']): [row(C['chat2'], 'm-3', False, 'добрый день', now_s() - 30)]}))
            c.check(answer(handed) == BUSINESS and not handed.ran('Ensure Connection'), f'moment handed in: {handed}')

    with Case(f'missing_connection_of_a_young_line_is_settling [{ch}]: the profile was created 10 minutes ago and no moment is '
              'handed in → the line\'s moment is 10 minutes old: settling, no Wappi call, only the connection row written') as c:
        with Session(SETUP) as pg:
            profile(pg, P, C['channel'], '10 minutes')
            r = decide(pg, inputs(C, None, phone=None), Fake(dialogs={P: [stranger(ch, C['chat'])]}))
            c.check(answer(r) == personal('settling') and not r.calls and statements(r) == ['Ensure Connection'], f'{r}')
            c.check(conn(pg, P) == {'phone': None, 'ms': created_ms(pg, P)} and nothing_written(pg), f'{counts(pg)} {conn(pg, P)}')

    with Case(f'malformed_input_is_unknown_no_calls [{ch}]: an unknown channel, the app\'s sentinel profile, no chat id, no '
              'message id → personal_unknown, no Wappi call, nothing written') as c:
        with Session(SETUP) as pg:
            profile(pg, P, C['channel'])
            moment = connect(pg, P)
            fake = Fake(dialogs={P: [stranger(ch, C['chat'])]}, pages={(P, C['chat']): [row(C['chat'], 'm-trig', False, 'x', now_s())]})
            for label, inp in (('channel sms', inputs(C, moment, channel='sms')),
                               ('no channel', inputs(C, moment, channel=None)),
                               ('profile -1', inputs(C, moment, profileId='-1')),
                               ('no chat id', inputs(C, moment, chatId='')),
                               ('no message id', inputs(C, moment, mid=''))):
                r = decide(pg, inp, fake)
                c.check(answer(r) == personal('unknown') and not r.calls, f'{label}: {answer(r)} {wappi(r)} {r}')
            c.check(nothing_written(pg), f'{counts(pg)}')

    with Case(f'moment_as_text_is_read_from_the_row [{ch}]: a moment handed in as text (Postgres hands a bigint on as one) is '
              'not a number — Ensure Connection reads the connection row and the chat is decided on its moment') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, str(moment)), fake)
            c.check(answer(r) == BUSINESS and r.ran('Ensure Connection'), f'{answer(r)} {r}')
            c.check(conn(pg, P)['ms'] == moment, f'the row is unchanged: {conn(pg, P)}')


# ================================================================================================ not a person
with Case('wa_newsletter_nonhuman_no_calls: a WhatsApp channel, the status / broadcast list and a group are not a person by '
          'their id alone — no Wappi call, written personal / nonhuman') as c:
    C = CH['WA']
    for chat in ('120363000000000002@newsletter', 'status@broadcast', '120363000000000001@g.us'):
        with Session(SETUP) as pg:
            profile(pg, C['pid'], C['channel'])
            moment = connect(pg, C['pid'])
            r = decide(pg, inputs(C, moment, chat=chat), Fake())
            c.check(answer(r) == personal('nonhuman') and not r.calls, f'{chat}: {r}')
            c.check(decision(pg, C['pid'], chat) == {'kind': 'personal', 'source': 'default', 'reason': 'nonhuman'},
                    f'{chat}: {decision(pg, C["pid"], chat)}')
    c.check(len(AUD.WA_NONHUMAN_SUFFIXES) == 3, f'a new suffix needs a row here: {AUD.WA_NONHUMAN_SUFFIXES}')

with Case('wa_self_nonhuman: the chat with oneself — the digits of the chat id are the connected number\'s (as written by the '
          'app, any punctuation) — is not a person: no Wappi call, written; the number of Ensure Connection\'s row counts too') as c:
    C = CH['WA']
    with Session(SETUP) as pg:
        profile(pg, C['pid'], C['channel'])
        moment = connect(pg, C['pid'], phone=OWN)
        r = decide(pg, inputs(C, moment, chat=OWN + '@c.us', phone='+7 (701) 999-00-00'), Fake())
        c.check(answer(r) == personal('nonhuman') and not r.calls, f'{r}')
        c.check(decision(pg, C['pid'], OWN + '@c.us') == {'kind': 'personal', 'source': 'default', 'reason': 'nonhuman'}, 'written')
        r = decide(pg, inputs(C, None, chat=OWN + '@c.us', mid='m-2', phone=None), Fake())
        c.check(answer(r) == personal('nonhuman') and r.ran('Ensure Connection') and not r.calls, f'by the row\'s number: {r}')
        r = decide(pg, inputs(C, moment, chat=C['chat'], phone=''), Fake(dialogs={C['pid']: [stranger('WA', C['chat'])]},
                   pages={(C['pid'], C['chat']): [wa_row(C['chat'], 'm-trig', False, 'x', now_s() - 9)]}))
        c.check(answer(r) == BUSINESS, f'no number known: nobody is oneself — {answer(r)}')

C = CH['TG']
for label, chat, chat_type, is_bot in (('tg_777000_no_calls', '777000', 'user', False),
                                       ('tg_channel_type_no_calls', '5550100001', 'channel', False),
                                       ('tg_channel_type_no_calls (a group)', '5550100002', 'chat', False),
                                       ('tg_chat_type_bot_nonhuman_no_calls', '5550100004', 'bot', True),
                                       ('tg_chat_type_bot_nonhuman_no_calls (the type alone)', '5550100005', 'bot', False),
                                       ('tg_is_bot_nonhuman_no_calls', '5550100006', 'user', True)):
    with Case(f'{label}: the input alone says it is not a person (the service account, a chat type that is not a person — a '
              f'bot\'s reply arrives with chat_type bot —, the webhook\'s is_bot) — no Wappi call, written personal / nonhuman') as c:
        with Session(SETUP) as pg:
            profile(pg, C['pid'], C['channel'])
            moment = connect(pg, C['pid'])
            r = decide(pg, inputs(C, moment, chat=chat, chat_type=chat_type, is_bot=is_bot), Fake())
            c.check(answer(r) == personal('nonhuman') and not r.calls, f'{r}')
            c.check(decision(pg, C['pid'], chat) == {'kind': 'personal', 'source': 'default', 'reason': 'nonhuman'},
                    f'{decision(pg, C["pid"], chat)}')

# ================================================================================================ the chat card
for ch, C in CH.items():
    P, row = C['pid'], row_of(ch)
    with Case(f'{ch.lower()}_contact_written_without_history_call: the card says a saved contact (WhatsApp: contact.FullName, '
              'Telegram: user.Contact) → personal / contact after ONE request — the history is not asked') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch, card=saved(ch, C['chat']))
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('contact') and wappi(r) == ['Chat Card'], f'{answer(r)} {wappi(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'personal', 'source': 'default', 'reason': 'contact'}, 'written')
            c.check(history(pg, P, C['chat']) == [] and memory(pg, P, C['chat']) == [], 'a personal chat stores nothing')

    with Case(f'card_failure_is_unknown_not_written [{ch}]: the card read fails (HTTP 500, no answer, status error, no dialogs, '
              'a crossed answer naming another profile) → personal_unknown, the history is not asked, nothing written') as c:
        for label, f in (('http 500', {'statusCode': 500, 'body': {'status': 'error'}}), ('no answer', {'throw': 'ETIMEDOUT'}),
                         ('status error', {'statusCode': 200, 'body': {'status': 'error', 'dialogs': [stranger(ch, C['chat'])]}}),
                         ('no dialogs', {'statusCode': 200, 'body': {'status': 'done'}}),
                         ('crossed', {'statusCode': 200, 'body': {'status': 'done', 'uuid': C['other'], 'dialogs': [stranger(ch, C['chat'])]}})):
            with Session(SETUP) as pg:
                moment, fake = new_person(pg, ch)
                fake.faults['Chat Card'] = f
                r = decide(pg, inputs(C, moment), fake)
                c.check(answer(r) == personal('unknown') and wappi(r) == ['Chat Card'] and not statements(r) and nothing_written(pg), f'{label}: {r}')

with Case('tg_bot_from_chat_card: the webhook says a person (chat_type user, is_bot false) but the card says user.Bot → '
          'personal / nonhuman after the card alone; the same for user.Support and user.Self') as c:
    C = CH['TG']
    for label, card in (('Bot', tg_dialog(C['chat'], bot=True)), ('Support', tg_dialog(C['chat'], support=True)),
                        ('Self', tg_dialog(C['chat'], self_=True)), ('a channel type', tg_dialog(C['chat'], type_='channel'))):
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, 'TG', card=card)
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('nonhuman') and wappi(r) == ['Chat Card'], f'{label}: {r}')
            c.check(decision(pg, C['pid'], C['chat']) == {'kind': 'personal', 'source': 'default', 'reason': 'nonhuman'}, label)

with Case('tg_dialog_not_on_page_is_personal_unknown_not_written: the first page of chats/get (20 dialogs) does not hold the '
          'chat → no flags: personal_unknown, the history is not asked, nothing written; a flag that is not a boolean is no card') as c:
    C = CH['TG']
    with Session(SETUP) as pg:
        others = [tg_dialog(str(5560000000 + k)) for k in range(21)]
        moment, fake = new_person(pg, 'TG', card=others[0])
        fake.dialogs[C['pid']] = others + [tg_dialog(C['chat'])]           # the 22nd: past the first page
        r = decide(pg, inputs(C, moment), fake)
        c.check(answer(r) == personal('unknown') and wappi(r) == ['Chat Card'] and not statements(r) and nothing_written(pg), f'{r}')
        c.check(fake.served and len(fake.served[0][1]) == 20, f'premise — the page held 20 dialogs: {fake.served}')
        odd = tg_dialog(C['chat'])
        odd['user']['Bot'] = 1
        fake.dialogs[C['pid']] = [odd]
        r = decide(pg, inputs(C, moment, mid='m-2'), fake)
        c.check(answer(r) == personal('unknown') and wappi(r) == ['Chat Card'] and not statements(r) and nothing_written(pg), f'Bot = 1: {r}')

with Case('wa_card_picks_the_chat_among_filter_matches: chats/filter by the digits also matches a chat NAMED with them and '
          'a chat whose last message holds them, both saved contacts; the card is the dialog whose id IS the chat id — a '
          'stranger here → the history decides → business') as c:
    C = CH['WA']
    digits = C['chat'].split('@')[0]
    with Session(SETUP) as pg:
        moment, fake = new_person(pg, 'WA')
        fake.dialogs[C['pid']] = [wa_dialog('77010000001@c.us', full_name='Сосед', name=f'Сосед {digits}'),
                                  wa_dialog('77010000002@c.us', full_name='Брат', last=f'запиши номер {digits}'),
                                  wa_dialog(C['chat']), wa_dialog('77010000003@c.us', full_name='Чужой')]
        r = decide(pg, inputs(C, moment), fake)
        c.check(fake.served and fake.served[0][1] == ['77010000001@c.us', '77010000002@c.us', C['chat']],
                f'premise — the filter matched three dialogs, the exact one third: {fake.served}')
        c.check(answer(r) == BUSINESS and wappi(r) == ['Chat Card', 'Recent Messages'], f'{answer(r)} {r}')
        fake.dialogs[C['pid']] = [wa_dialog('77010000001@c.us', name=f'Сосед {digits}'), wa_dialog(C['chat'], full_name='Мама')]
        r = decide(pg, inputs(C, moment, chat=C['chat'], mid='m-2'), fake)
        c.check(answer(r) == BUSINESS, f'the chat is already decided: the first decision stands — {answer(r)}')
    with Session(SETUP) as pg:
        moment, fake = new_person(pg, 'WA')
        fake.dialogs[C['pid']] = [wa_dialog('77010000001@c.us', name=f'Сосед {digits}'), wa_dialog(C['chat'], full_name='Мама')]
        r = decide(pg, inputs(C, moment), fake)
        c.check(answer(r) == personal('contact') and wappi(r) == ['Chat Card'], f'the exact dialog is a saved contact: {r}')
    with Session(SETUP) as pg:
        moment, fake = new_person(pg, 'WA')
        fake.dialogs[C['pid']] = [wa_dialog('77010000001@c.us', full_name='Сосед', name=f'Сосед {digits}')]
        r = decide(pg, inputs(C, moment), fake)
        c.check(answer(r) == personal('unknown') and wappi(r) == ['Chat Card'] and not statements(r) and nothing_written(pg),
                f'the chat itself not among the matches → no contact fact: {r}')

with Case('wa_contact_name_is_not_read: the template\'s item may carry more than the nine inputs (contact_name, a profile '
          'name Wappi fills for a stranger too) — the trigger keeps only the nine and a stranger\'s card makes him business') as c:
    C = CH['WA']
    with Session(SETUP) as pg:
        moment, fake = new_person(pg, 'WA')
        r = decide(pg, inputs(C, moment, contactName='Иван Петров', contact_name='Иван Петров', senderName='Иван'), fake)
        c.check(answer(r) == BUSINESS, f'{answer(r)} {r}')
        trig = (r.outputs.get(TRIGGER) or [[]])[0]
        c.check(trig and sorted(trig[0]['json']) == sorted(n for n, _ in INPUTS), f'the trigger hands on: {trig}')

with Case('wa_lid_chat_no_card_history_decides: a chat known only by its code (an id ending in @lid) gets no card request — '
          'the filter cannot find a code; its contact is false and its history decides: new → business, old → personal / '
          'history') as c:
    C = CH['WA']
    lid = '100000000000777@lid'
    with Session(SETUP) as pg:
        profile(pg, C['pid'], C['channel'])
        moment = connect(pg, C['pid'])
        fake = Fake(pages={(C['pid'], lid): [wa_row(lid, 'm-trig', False, 'добрый день', now_s() - 60)]})
        r = decide(pg, inputs(C, moment, chat=lid, lid=''), fake)
        c.check(answer(r) == BUSINESS and wappi(r) == ['Recent Messages'], f'new: {r}')
        old = '100000000000888@lid'
        fake.pages[(C['pid'], old)] = [wa_row(old, 'm-trig2', False, 'привет', now_s() - 60),
                                       wa_row(old, 'm-old', True, 'давно не виделись', old_s(moment))]
        r = decide(pg, inputs(C, moment, chat=old, mid='m-trig2', lid=''), fake)
        c.check(answer(r) == personal('history') and wappi(r) == ['Recent Messages'], f'old: {r}')
        c.check(decision(pg, C['pid'], lid)['reason'] == 'new' and decision(pg, C['pid'], old)['reason'] == 'history', 'both written')

# ================================================================================================ the history page
for ch, C in CH.items():
    P, row = C['pid'], row_of(ch)
    with Case(f'{ch.lower()}_new_person_business_after_one_history_call: a stranger\'s card and a page holding only the '
              'trigger → business / new after the card and ONE messages/get') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == BUSINESS and wappi(r) == ['Chat Card', 'Recent Messages'], f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'business', 'source': 'default', 'reason': 'new'}, 'written')
            c.check(history(pg, P, C['chat']) == [], 'the trigger is left to the template (Claim Event records it)')

    with Case(f'{ch.lower()}_old_chat_personal_history: two messages of the newest page are older than the connection (either '
              'direction counts) → personal / history; nothing of the chat is stored') as c:
        with Session(SETUP) as pg:
            r, fake = old_chat(pg, ch)
            c.check(answer(r) == personal('history') and wappi(r) == ['Chat Card', 'Recent Messages'], f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'personal', 'source': 'default', 'reason': 'history'}, 'written')
            c.check(history(pg, P, C['chat']) == [] and memory(pg, P, C['chat']) == [], 'nothing stored')

    with Case(f'short_page_without_old_row_is_business [{ch}]: {PAGE - 1} rows (fewer than the page holds: the whole chat), '
              'the trigger among them, none older than the connection → business') as c:
        with Session(SETUP) as pg:
            page = [row(C['chat'], 'm-trig', False, 'и ещё вопрос', now_s() - 10)]
            page += [row(C['chat'], f'm{k}', k % 2 == 0, f'сообщение {k}', now_s() - 60 * (k + 1)) for k in range(PAGE - 2)]
            moment, fake = new_person(pg, ch, page=page)
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == BUSINESS, f'{answer(r)} {r}')

    with Case(f'full_page_all_new_is_unknown_not_written [{ch}]: a full page ({PAGE} rows, and a chat of {PAGE + 2}), every row newer '
              'than the connection → the page may not be the whole chat: personal_unknown, nothing written') as c:
        for total in (PAGE, PAGE + 2):
            with Session(SETUP) as pg:
                page = [row(C['chat'], 'm-trig', False, 'алло', now_s() - 5)]
                page += [row(C['chat'], f'm{k}', k % 2 == 0, f'сообщение {k}', now_s() - 60 * (k + 1)) for k in range(total - 1)]
                moment, fake = new_person(pg, ch, page=page)
                r = decide(pg, inputs(C, moment), fake)
                c.check(answer(r) == personal('unknown') and not statements(r) and nothing_written(pg), f'{total} rows: {answer(r)} {r}')

    with Case(f'ten_newer_rows_then_an_older_one_is_history [{ch}]: ten messages after the connection (the trigger the newest) '
              'and an eleventh from before it — the history page reaches the old row: personal / history, written, nothing '
              'stored (a page of ten would end on new rows and leave the chat undecided for good)') as c:
        with Session(SETUP) as pg:
            page = [row(C['chat'], 'm-trig', False, 'алло', now_s() - 5)]
            page += [row(C['chat'], f'm{k}', k % 2 == 0, f'сообщение {k}', now_s() - 60 * (k + 1)) for k in range(9)]
            moment, fake = new_person(pg, ch, page=page)
            page.append(row(C['chat'], 'm-old', True, 'давнее', old_s(moment)))      # the eleventh: before the connection
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('history') and wappi(r) == ['Chat Card', 'Recent Messages'], f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'personal', 'source': 'default', 'reason': 'history'}, 'written')
            c.check(history(pg, P, C['chat']) == [] and memory(pg, P, C['chat']) == [], 'a personal chat stores nothing')

    with Case(f'twelve_message_new_conversation_is_business [{ch}]: a new client wrote twelve messages after the connection '
              '(the owner answered in between), none before it — the page holds the whole chat: business / new, written; the '
              f'backfill stores the newest {AUD.RECENT_LIMIT} rows of that page but the trigger, oldest first') as c:
        with Session(SETUP) as pg:
            page = [row(C['chat'], 'm-trig', False, 'и ещё вопрос', now_s() - 5)]
            page += [row(C['chat'], f'm{k:02d}', k % 3 == 1, f'сообщение {k}', now_s() - 60 * (k + 1)) for k in range(11)]
            moment, fake = new_person(pg, ch, page=page)
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == BUSINESS and r.ran('Backfill'), f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'business', 'source': 'default', 'reason': 'new'}, 'written')
            kept = [m['id'] for m in page[1:AUD.RECENT_LIMIT]][::-1]      # the newest RECENT_LIMIT rows, the trigger left out
            c.check([mid for mid, _ in history(pg, P, C['chat'])] == kept,
                    f'the backfill keeps the newest {AUD.RECENT_LIMIT} rows of the page: {history(pg, P, C["chat"])} vs {kept}')

    with Case(f'page_without_trigger_is_unknown_not_written [{ch}]: the page does not hold the incoming message (Wappi indexes '
              'a new chat with a delay) → stale: personal_unknown, nothing written') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch, page=[row(C['chat'], 'm-earlier', True, 'Здравствуйте!', now_s() - 300)])
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('unknown') and not statements(r) and nothing_written(pg), f'{answer(r)} {r}')

    with Case(f'empty_history_is_unknown [{ch}]: an empty messages array holds no trigger either → personal_unknown') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch, page=[])
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('unknown') and not statements(r) and nothing_written(pg), f'{answer(r)} {r}')

    with Case(f'foreign_row_page_is_unknown_not_written [{ch}]: a row naming another chat makes the whole page nothing (Wappi '
              'crosses concurrent answers) → personal_unknown, nothing written, nothing backfilled') as c:
        with Session(SETUP) as pg:
            page = [row(C['chat'], 'm-trig', False, 'привет', now_s() - 10), row(C['chat2'], 'x-1', False, 'чужое', now_s() - 20)]
            moment, fake = new_person(pg, ch, page=page)
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('unknown') and not statements(r) and nothing_written(pg), f'{answer(r)} {r}')

    with Case(f'row_without_time_is_unknown_not_written [{ch}]: a row without a usable time may be the old one — with no row '
              'shown older, the history is not known → personal_unknown, nothing written') as c:
        with Session(SETUP) as pg:
            page = [row(C['chat'], 'm-trig', False, 'привет', now_s() - 10), row(C['chat'], 'm-x', True, 'а это?', None)]
            moment, fake = new_person(pg, ch, page=page)
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('unknown') and not statements(r) and nothing_written(pg), f'{answer(r)} {r}')

    with Case(f'history_500_is_personal_unknown_not_written [{ch}]: the history read fails (HTTP 500, no answer, status error, '
              'no messages array, a crossed answer naming another profile) → personal_unknown, nothing written') as c:
        trig = row(C['chat'], 'm-trig', False, 'привет', now_s() - 10)
        for label, f in (('http 500', {'statusCode': 500, 'body': {'status': 'error'}}), ('no answer', {'throw': 'ETIMEDOUT'}),
                         ('status error', {'statusCode': 200, 'body': {'status': 'error', 'messages': [trig], 'uuid': P}}),
                         ('no messages', {'statusCode': 200, 'body': {'status': 'done', 'uuid': P}}),
                         ('crossed', {'statusCode': 200, 'body': {'status': 'done', 'messages': [trig], 'uuid': C['other']}})):
            with Session(SETUP) as pg:
                moment, fake = new_person(pg, ch)
                fake.faults['Recent Messages'] = f
                r = decide(pg, inputs(C, moment), fake)
                c.check(answer(r) == personal('unknown') and not statements(r) and nothing_written(pg) and r.halt is None, f'{label}: {answer(r)} {r}')

with Case('wa_rows_under_the_code_count_as_this_chat: Wappi lists the messages of some chats under the chat\'s @lid code '
          '(chatId and the client\'s from); with the code handed in (chatLid) they ARE this chat — the history counts them and '
          'the backfill stores them under the number; without it, or under a third id, the page is nothing') as c:
    C, P = CH['WA'], CH['WA']['pid']
    code = C['lid']
    with Session(SETUP) as pg:
        profile(pg, P, C['channel'])
        moment = connect(pg, P)
        fake = Fake(dialogs={P: [wa_dialog(C['chat'], lid=code)]},
                    pages={(P, C['chat']): [wa_row(C['chat'], 'm-trig', False, 'привет', now_s() - 10, code=code),
                                            wa_row(C['chat'], 'm-old', True, 'с днём рождения', old_s(moment), code=code)]})
        r = decide(pg, inputs(C, moment, lid=code), fake)
        c.check(answer(r) == personal('history'), f'history under the code: {answer(r)} {r}')
    with Session(SETUP) as pg:
        profile(pg, P, C['channel'])
        moment = connect(pg, P)
        page = [wa_row(C['chat'], 'm-trig', False, 'а доставка?', now_s() - 10, code=code),
                wa_row(C['chat'], 'm-own', True, 'Здравствуйте!', now_s() - 120, code=code),
                wa_row(C['chat'], 'm-c1', False, 'есть розы?', now_s() - 300, code=code)]
        fake = Fake(dialogs={P: [wa_dialog(C['chat'], lid=code)]}, pages={(P, C['chat']): page})
        r = decide(pg, inputs(C, moment, lid=code), fake)
        c.check(answer(r) == BUSINESS, f'business under the code: {answer(r)} {r}')
        c.check(history(pg, P, C['chat']) == [('m-c1', 'client'), ('m-own', 'owner_phone')] and history(pg, P, code) == [],
                f'the backfill stores them under the number: {history(pg, P, C["chat"])} {history(pg, P, code)}')
        for label, lid, rows in (('the code not handed in', '', page),
                                 ('a row under a third id', code, page + [wa_row('100000000000999@lid', 'x', False, 'чужое', now_s() - 400)])):
            fake.pages[(P, C['chat2'])] = [dict(m, id=m['id'] + '-2') for m in rows]
            r = decide(pg, inputs(C, moment, chat=C['chat2'], mid='m-trig-2', lid=lid),
                       Fake(dialogs={P: [wa_dialog(C['chat2'])]}, pages=fake.pages))
            c.check(answer(r) == personal('unknown') and decision(pg, P, C['chat2']) is None, f'{label}: {answer(r)} {r}')

# ================================================================================================ the write and the backfill
for ch, C in CH.items():
    P = C['pid']
    with Case(f'business_backfills_the_owners_first_message [{ch}]: the owner wrote to the new client first; when the chat '
              'turns business the messages before the trigger reach the history and the memory — the owner\'s with the owner '
              'mark — and the trigger is left to the template\'s normal path') as c:
        with Session(SETUP) as pg:
            r, page = conversation(pg, ch)
            c.check(answer(r) == BUSINESS and r.ran('Backfill'), f'{answer(r)} {r}')
            c.check(history(pg, P, C['chat']) == CONVERSATION_HISTORY, f'history: {history(pg, P, C["chat"])}')
            mem = memory(pg, P, C['chat'])
            c.check(conversation_memory_ok(mem), f'memory: {mem}')
            own = next((m for m in mem if (m.get('additional_kwargs') or {}).get('message_id') == 'm-own'), {})
            c.check(own.get('additional_kwargs') == {'source': 'owner_phone', 'message_id': 'm-own'}, f'the owner row: {own}')

    with Case(f'backfill_uses_the_history_page_no_second_call [{ch}]: the backfill is handed the very page the history fact '
              'read — ONE messages/get in the whole run, Backfill after Write Decision, the page exactly as Wappi returned it '
              '(newest first) and the trigger id') as c:
        with Session(SETUP) as pg:
            r, page = conversation(pg, ch)
            c.check(wappi(r).count('Recent Messages') == 1 and wappi(r) == ['Chat Card', 'Recent Messages'], f'{wappi(r)}')
            c.check(r.at('sql', 'Write Decision') is not None and r.at('sql', 'Write Decision') < r.at('sql', 'Backfill'),
                    f'the order: {r.events}')
            handed = (r.outputs.get('Decide') or [[{}]])[0][0].get('json', {})
            c.check(handed.get('page') == page and handed.get('messageId') == 'm-trig',
                    f'Decide hands the page on unsorted: {[m.get("id") for m in handed.get("page") or []]}')

    with Case(f'backfill_failure_keeps_business [{ch}]: a failed Backfill changes nothing — the row said business') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake, faults={'Backfill': 'deadlock detected'})
            c.check(answer(r) == BUSINESS and r.ran('Backfill') and r.halt is None, f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'business', 'source': 'default', 'reason': 'new'}, 'written')

    with Case(f'write_failure_is_personal_unknown [{ch}]: Write Decision fails → no row says business: personal_unknown, no '
              'backfill, nothing written') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake, faults={'Write Decision': 'connection terminated unexpectedly'})
            c.check(answer(r) == personal('unknown') and not r.ran('Backfill') and nothing_written(pg), f'{answer(r)} {r}')

    with Case(f'second_run_reads_the_first_decision [{ch}]: a second message of the same new chat that started before the '
              'first decision was written reads the FIRST decision (the write keeps it), whatever its own facts say') as c:
        row = row_of(ch)
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            first = decide(pg, inputs(C, moment), fake)
            fake.dialogs[P] = [saved(ch, C['chat'])]                          # meanwhile saved as a contact
            second = decide(pg, inputs(C, moment, mid='m-trig2'), fake)
            c.check(answer(first) == BUSINESS and answer(second) == BUSINESS, f'{answer(first)} {answer(second)} {second}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'business', 'source': 'default', 'reason': 'new'}, 'unchanged')
        with Session(SETUP) as pg:
            first, fake = old_chat(pg, ch)
            moment = conn(pg, P)['ms']
            fake.pages[(P, C['chat'])] = [row(C['chat'], 'm-trig2', False, 'ну что?', now_s() - 5)]
            second = decide(pg, inputs(C, moment, mid='m-trig2'), fake)
            c.check(answer(first) == personal('history') and answer(second) == personal('history'),
                    f'{answer(first)} {answer(second)} {second}')
            c.check(decision(pg, P, C['chat'])['reason'] == 'history' and history(pg, P, C['chat']) == [], 'unchanged, nothing stored')

    with Case(f'owner_decision_set_meanwhile_is_kept [{ch}]: the owner decided while this run collected facts — the write keeps '
              'the owner\'s row: his business answers business (and backfills), his personal is personal_unknown (no reason '
              'of this run\'s)') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            pg.q(bind(AUD.OWNER_SET_SQL, [P, [{'chatId': C['chat'], 'kind': 'personal', 'version': 5}]]))
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == personal('unknown') and not r.ran('Backfill'), f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'personal', 'source': 'owner', 'reason': 'owner'}, 'the owner\'s row stays')
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch, card=saved(ch, C['chat']))
            pg.q(bind(AUD.OWNER_SET_SQL, [P, [{'chatId': C['chat'], 'kind': 'business', 'version': 5}]]))
            r = decide(pg, inputs(C, moment), fake)
            c.check(answer(r) == BUSINESS and r.ran('Backfill'), f'{answer(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'business', 'source': 'owner', 'reason': 'owner'}, 'the owner\'s row stays')

# ================================================================================================ a node that throws before its work
# When a node that continues on error throws OUTSIDE its runner — credentials, connection setup, a parameter expression —
# n8n-core 2.36.5 hands its INPUT items on, unchanged (graph_walk.step_throw): the one failure without an error item. So no
# node's input may ever pass for that node's own answer. Every continue-on-error node of Decide Audience, both channels.
THROWN = 'getCredentials: Credentials could not be decrypted'
for ch, C in CH.items():
    P, row = C['pid'], row_of(ch)
    with Case(f'passthrough_ensure_connection_is_unknown_not_written [{ch}]: Ensure Connection throws before its statement — '
              'the trigger\'s item comes out of it, and it holds no moment: personal_unknown, no Wappi call, nothing written') as c:
        with Session(SETUP) as pg:
            profile(pg, P, C['channel'])
            fake = Fake(dialogs={P: [stranger(ch, C['chat'])]}, pages={(P, C['chat']): [row(C['chat'], 'm-trig', False, 'привет', now_s() - 60)]})
            r = decide(pg, inputs(C, None, phone=None), fake, throws={'Ensure Connection': THROWN})
            c.check(('throw', 'Ensure Connection') in r.events, f'premise — the node threw: {r.events}')
            c.check(answer(r) == personal('unknown') and not r.calls and not statements(r), f'{answer(r)} {r}')
            c.check(conn(pg, P) is None and nothing_written(pg), f'{counts(pg)} {conn(pg, P)}')

    with Case(f'passthrough_chat_card_is_unknown_not_written [{ch}]: Chat Card throws before its request — the plan comes out '
              'of it, and it is no card: personal_unknown, no history call, nothing written') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake, throws={'Chat Card': THROWN})
            c.check(('throw', 'Chat Card') in r.events, f'premise — the node threw: {r.events}')
            c.check(answer(r) == personal('unknown') and not r.calls and not statements(r) and nothing_written(pg), f'{answer(r)} {r}')

    with Case(f'passthrough_recent_messages_is_unknown_not_written [{ch}]: Recent Messages throws before its request — the plan '
              'comes out of it, and it is no page: personal_unknown, nothing written') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake, throws={'Recent Messages': THROWN})
            c.check(('throw', 'Recent Messages') in r.events, f'premise — the node threw: {r.events}')
            c.check(answer(r) == personal('unknown') and wappi(r) == ['Chat Card'] and not statements(r) and nothing_written(pg),
                    f'{answer(r)} {r}')

    with Case(f'passthrough_write_decision_is_unknown_no_backfill [{ch}]: Write Decision throws before its statement — Decide\'s '
              'item comes out of it, and it is no row: personal_unknown, no Backfill, no decision written') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake, throws={'Write Decision': THROWN})
            c.check(('throw', 'Write Decision') in r.events, f'premise — the node threw: {r.events}')
            c.check(answer(r) == personal('unknown') and not r.ran('Backfill') and not statements(r) and nothing_written(pg),
                    f'{answer(r)} statements {statements(r)} {counts(pg)} {r}')

    with Case(f'passthrough_backfill_keeps_business [{ch}]: Backfill throws before its statement — the decision row is written '
              'and stands: business, nothing backfilled') as c:
        with Session(SETUP) as pg:
            r, page = conversation(pg, ch, throws={'Backfill': THROWN})
            c.check(('throw', 'Backfill') in r.events, f'premise — the node threw: {r.events}')
            c.check(answer(r) == BUSINESS and statements(r) == ['Write Decision'], f'{answer(r)} {statements(r)} {r}')
            c.check(decision(pg, P, C['chat']) == {'kind': 'business', 'source': 'default', 'reason': 'new'}
                    and history(pg, P, C['chat']) == [] and memory(pg, P, C['chat']) == [], f'{decision(pg, P, C["chat"])} {counts(pg)}')

with Case('wa_dialog_without_contact_is_unknown_not_written: the chat\'s dialog is found but carries no contact object — its '
          'absence stays unknown (reading it as «not a contact» would answer a contact on a glitch): personal_unknown, no '
          'history call, nothing written') as c:
    C = CH['WA']
    with Session(SETUP) as pg:
        card = wa_dialog(C['chat'])
        card.pop('contact')
        moment, fake = new_person(pg, 'WA', card=card)
        r = decide(pg, inputs(C, moment), fake)
        c.check(answer(r) == personal('unknown') and wappi(r) == ['Chat Card'] and not statements(r) and nothing_written(pg),
                f'{answer(r)} {r}')

with Case('wa_is_bot_nonhuman_no_calls: the incoming WhatsApp webhook says is_bot → not a person by its input alone: no Wappi '
          'call, written personal / nonhuman like every non-person') as c:
    C = CH['WA']
    with Session(SETUP) as pg:
        moment, fake = new_person(pg, 'WA')
        r = decide(pg, inputs(C, moment, is_bot=True), fake)
        c.check(answer(r) == personal('nonhuman') and not r.calls, f'{answer(r)} {r}')
        c.check(decision(pg, C['pid'], C['chat']) == {'kind': 'personal', 'source': 'default', 'reason': 'nonhuman'},
                f'{decision(pg, C["pid"], C["chat"])}')

# ================================================================================================ the requests
for ch, C in CH.items():
    P = C['pid']
    with Case(f'the_requests [{ch}]: the card — WhatsApp chats/filter by the digits of the chat id, Telegram chats/get limit 20 / '
              f'offset 0 / order desc; the history — messages/get of THIS chat, limit {PAGE}, order desc; both GET with the Wappi '
              'credential, fullResponse + neverError, 10 s, never mark_all') as c:
        with Session(SETUP) as pg:
            moment, fake = new_person(pg, ch)
            r = decide(pg, inputs(C, moment), fake)
            cc = next((x for x in r.calls if x['node'] == 'Chat Card'), None)
            rm = next((x for x in r.calls if x['node'] == 'Recent Messages'), None)
            want_card = ((WA_BASE + 'sync/chats/filter', {'profile_id': P, 'client_name': C['chat'].split('@')[0]}) if ch == 'WA'
                         else (TG_BASE + 'sync/chats/get', {'profile_id': P, 'limit': 20, 'offset': 0, 'order': 'desc'}))
            c.check(cc and (cc['url'], cc['query']) == want_card, f'the card: {cc and (cc["url"], cc["query"])}')
            c.check(rm and (rm['url'], rm['query']) == (C['base'] + 'sync/messages/get',
                                                        {'profile_id': P, 'chat_id': C['chat'], 'limit': PAGE, 'order': 'desc'}),
                    f'the history: {rm and (rm["url"], rm["query"])}')
            for x in (cc, rm):
                if x:
                    resp = (x['options'].get('response') or {}).get('response') or {}
                    c.check(x['method'] == 'GET' and x['credentials'] == WAPPI_CRED and resp.get('fullResponse') is True
                            and resp.get('neverError') is True and x['options'].get('timeout') == 10000 and x['body'] is None,
                            f'{x["node"]}: {x}')

with Case('history_page_is_one_the_app_asks: audience.HISTORY_LIMIT is larger than the backfill\'s RECENT_LIMIT, no larger than '
          'the page the app itself asks on every chat open (ChatManager.MessagesPerPage, through the Wappi Proxy\'s messages.get) '
          'and inside the proxy\'s own bound for messages.get') as c:
    import re as _re
    cm = (N8N.parents[1] / 'Assets' / 'Scripts' / 'Main' / 'ChatManager.cs').read_text(encoding='utf-8')
    app = _re.search(r'public static int MessagesPerPage = (\d+);', cm)
    bound = _re.search(r"'messages\.get':.*?limit: \['limit', 'int', (\d+),", (N8N / 'build-auth-workflows.py').read_text(encoding='utf-8'))
    c.check(app and bound, f'premise — the app\'s page and the proxy\'s bound are readable: {bool(app)} {bool(bound)}')
    if app and bound:
        c.check(AUD.RECENT_LIMIT < PAGE <= int(app.group(1)) <= int(bound.group(1)),
                f'RECENT_LIMIT {AUD.RECENT_LIMIT} < HISTORY_LIMIT {PAGE} <= the app\'s page {app.group(1)} <= the proxy\'s bound {bound.group(1)}')

# ================================================================================================ A1: asc would fail
def with_query(w, old, new):
    w = copy.deepcopy(w)
    p = nd(w, 'Recent Messages')['parameters']
    if old not in p['jsonQuery']:
        raise AssertionError(f'the history query lost {old!r}: {p["jsonQuery"]}')
    p['jsonQuery'] = p['jsonQuery'].replace(old, new)
    return w


with Case('A1 a_return_to_asc_fails_these_scenarios: on copies of the canon whose history read asks order asc — the same limit, '
          'or limit 1 (the plan\'s «oldest message») — the old-chat and the backfill scenarios break on BOTH channels: '
          'WhatsApp\'s honest asc hands the oldest rows (no trigger), tapi\'s re-sorted newest page makes a limit 1 read the '
          'trigger itself (an old chat looks new) and turns the backfill\'s order around') as c:
    w = canon()
    copies = (('order asc', with_query(w, "order: 'desc'", "order: 'asc'")),
              ('limit 1, order asc', with_query(w, f"limit: {PAGE}, order: 'desc'", "limit: 1, order: 'asc'")))
    for label, copy_ in copies:
        for ch, C in CH.items():
            broken = []
            with Session(SETUP) as pg:
                r, _ = old_chat(pg, ch, workflow=copy_)
                if answer(r) != personal('history'):
                    broken.append(f'old chat → {answer(r)}')
            with Session(SETUP) as pg:
                r, _ = conversation(pg, ch, workflow=copy_)
                if answer(r) != BUSINESS or history(pg, C['pid'], C['chat']) != CONVERSATION_HISTORY:
                    broken.append(f'backfill → {answer(r)} {history(pg, C["pid"], C["chat"])}')
            c.check(broken, f'{label} [{ch}]: both scenarios still pass on the copy — the stub does not model asc')

# ================================================================================================ the canon, the generator, the verifier
with Case('no_url_carries_mark_all: not one request of any scenario above carried mark_all (reading a chat must never mark it '
          'read) or asked order asc') as c:
    c.check(len(CALLS) >= 60, f'only {len(CALLS)} requests were recorded — the scenarios did not run')
    c.check(not [x for x in CALLS if 'mark_all' in json.dumps([x['url'], x['query'], x['body']])], 'mark_all was sent')
    c.check(not [x for x in CALLS if 'asc' in json.dumps([x['url'], x['query']])], 'order asc was asked')

with Case('protected_and_generated: crAudienceDec001 is a protected infra id (build-client-webhooks.PROTECTED_IDS → '
          'clone_lineage.PROTECTED, Set Workflow State, the dependents regenerated from it, staging/bootstrap.py ACTIVE_CANONICAL '
          'before the staging-only eval runner); the generator is up to date; successful executions are not saved; the '
          'trigger declares exactly the nine inputs') as c:
    bcw, cl, boot = module('build-client-webhooks.py'), module('clone_lineage.py'), module('staging/bootstrap.py')
    c.check(WF_ID in bcw.PROTECTED_IDS and WF_ID in cl.PROTECTED and WF_ID in boot.ACTIVE_CANONICAL,
            'missing from PROTECTED_IDS / clone_lineage.PROTECTED / ACTIVE_CANONICAL')
    if WF_ID in boot.ACTIVE_CANONICAL:
        c.check(boot.ACTIVE_CANONICAL.index(WF_ID) < boot.ACTIVE_CANONICAL.index('stgEvalRunner001'),
                f'ACTIVE_CANONICAL: {boot.ACTIVE_CANONICAL[-4:]}')
    for f, names in (('0DP79STI3pafXO9B-Set_Workflow_State.json', ('Validate',)),
                     ('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', ('Clone Plan', 'Bind Workflow Id')),
                     ('Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', ('Clone Plan', 'Bind Workflow Id')),
                     ('lmjYsdNcQA2IE5rl-Delete_Bot_Files.json', ('List Bot Originals', 'Retire Bot Profiles', 'Delete Bot Chunks',
                                                                 'Delete Chat Memory')),
                     ('fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json', ('Clone Retirements',))):
        w = json.loads((WF / f).read_text(encoding='utf-8'))
        missing = [n for n in names if f"'{WF_ID}'" not in json.dumps(node(w, n)['parameters'], ensure_ascii=False)]
        c.check(not missing, f'{f}: {missing} do not carry {WF_ID}')
    r = subprocess.run([sys.executable, str(GEN), '--check'], capture_output=True, text=True)
    c.check(r.returncode == 0, f'--check exit {r.returncode}: {r.stdout}{r.stderr}')
    w = canon()
    c.check(w['id'] == WF_ID and w['settings'].get('saveDataSuccessExecution') == 'none', f'{w["id"]} {w["settings"]}')
    t = nd(w, TRIGGER)
    c.check(t['type'] == 'n8n-nodes-base.executeWorkflowTrigger' and t['parameters'].get('inputSource') == 'workflowInputs'
            and t['parameters']['workflowInputs']['values'] == [{'name': n, 'type': k} for n, k in INPUTS],
            f'the inputs: {t["parameters"]}')


def run_verifier(directory=None):
    r = subprocess.run([sys.executable, str(VERIFY), '--only', 'decide'] + (['--dir', str(directory)] if directory else []),
                       capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


with Case('V0 verify-personal-chats.py --only decide passes the committed canon (its own tripwire self-check included)') as c:
    rc_, out = run_verifier()
    c.check(rc_ == 0 and 'personal chats verify: OK' in out and 'decide:' in out, f'exit {rc_}: {out[-900:]}')


def _cond(name, expr):
    def m(w):
        nd(w, name)['parameters']['conditions']['conditions'][0]['leftValue'] = expr
    return m


def _query(name, old, new):
    def m(w):
        p = nd(w, name)['parameters']
        if old not in p['jsonQuery']:
            raise AssertionError(f'{name}: no {old!r}')
        p['jsonQuery'] = p['jsonQuery'].replace(old, new)
    return m


def _extra_postgres(w):
    extra = copy.deepcopy(nd(w, 'Write Decision'))
    extra.update(name='Read Decision Again', id='00000000-0000-0000-0000-00000000d3c1', position=[0, 900])
    w['nodes'].append(extra)
    w['connections']['Decide'] = {'main': [[{'node': 'Read Decision Again', 'type': 'main', 'index': 0}]]}
    w['connections']['Read Decision Again'] = {'main': [[{'node': 'Persist?', 'type': 'main', 'index': 0}]]}


# The plan's negatives and the live checks' three, each on a scratch copy of the exports; each must fail the CLI.
SCRATCH = (
    ('neverError removed from the card read', lambda w: nd(w, 'Chat Card')['parameters']['options']['response']['response'].pop('neverError')),
    ('neverError removed from the history read', lambda w: nd(w, 'Recent Messages')['parameters']['options']['response']['response'].pop('neverError')),
    ('mark_all in a URL', lambda w: nd(w, 'Recent Messages')['parameters'].__setitem__(
        'url', nd(w, 'Recent Messages')['parameters']['url'].replace("'sync/messages/get'", "'sync/messages/get?mark_all=true'"))),
    ('Persist? by «the node passed»', _cond('Persist?', '={{ $json !== undefined }}')),
    ('an extra Postgres node', _extra_postgres),
    ('order asc in an HTTP node', _query('Recent Messages', "order: 'desc'", "order: 'asc'")),
    ('a contactName input', lambda w: nd(w, TRIGGER)['parameters']['workflowInputs']['values'].append({'name': 'contactName', 'type': 'string'})),
    ('the WhatsApp card without client_name', _query('Chat Card', ", client_name: $json.clientName", "")),
    ('Decide emits the row\'s kind (a gate could take its item for the row)', lambda w: nd(w, 'Decide')['parameters'].__setitem__(
        'jsCode', nd(w, 'Decide')['parameters']['jsCode'].replace('verdictKind: verdict.kind,', 'kind: verdict.kind, verdictKind: verdict.kind,'))),
    ('Became Business? without the row\'s source', _cond('Became Business?', "={{ $json.kind === 'business' }}")),
)

for label, mutate in SCRATCH:
    with Case(f'V NEG {label}: the verifier fails on a scratch copy') as c:
        d = Path(tempfile.mkdtemp(prefix='dec-verify-'))
        try:
            for f in WF.glob('*.json'):
                shutil.copy(f, d / f.name)
            w = json.loads((d / FILE).read_text(encoding='utf-8'))
            before = json.dumps(w, sort_keys=True)
            mutate(w)
            c.check(json.dumps(w, sort_keys=True) != before, 'the mutation changed nothing')
            (d / FILE).write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            rc_, out = run_verifier(d)
            c.check(rc_ != 0 and 'FAIL [decide]' in out, f'exit {rc_}: {out[-700:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)

report()
