#!/usr/bin/env python3
"""R10 (iteration 2026-09-27): a row of ANOTHER chat (or an ambiguous window) must never confirm a send_unknown.

Reconcile Check runs after a send whose answer was lost: it looks for OUR text as an outgoing row in a fresh
messages/get of the chat and, when found, records `sent` with that row's id. It had none of Final Check's guards —
no foreign-chatId filter, no requirement that the window even holds the trigger, no «newer than the trigger», no
ambiguity rule — so a crossed Wappi/tapi response (a project-confirmed transport bug) or the same phrase elsewhere
turned an unknown send into a false `sent` with a foreign message id.

Contract: rows naming a foreign chatId are dropped; the window must contain the trigger; EXACTLY ONE distinct
outgoing row with our text, newer than the trigger in the window's order (time desc, Wappi order on ties) and not
marked as sent from the phone (tapi `from_where: 'phone'`), is adopted; anything else stays send_unknown.

Executes the REAL Reconcile Check body of BOTH templates (and, end to end, Settle Send → Reconcile Check → Record
Send on staging-PG temp tables for the crossed-window case).

    python3 Tools/n8n/tests/test_reply_reconcile.py
"""
import time

from harness import Case, PgSession, code, report
from reply_chain import CHATS, SETUP, TEMPLATES, Run, history, ledger, msg

TEXT = 'Напишите, пожалуйста, текстом — так я быстрее отвечу.'
T0 = 1_790_000_000


def reconcile(T, ch, rows, error=None, trig_time=T0):
    trig = msg(ch, 'TRIG', trig_time, 'https://mock/photo.jpg', mtype='image')
    settle = {'state': 'send_unknown', 'unknown': True, 'transportMessageId': '', 'error': 'socket hang up', 'text': TEXT,
              'sendStartedAt': T0 + 30}
    item = {'json': ({'error': error} if error else {'status': 'done', 'messages': rows})}
    return code(T, 'Reconcile Check', item, {'Settle Send': settle, 'Webhook': {'body': {'messages': [trig]}}})[0]['json']


def row(ch, mid, t, body=TEXT, from_me=True, chat=None, drop_chat=False, **extra):
    m = dict(msg(ch, mid, t, body, from_me=from_me, chat=chat), **extra)
    if drop_chat:
        m.pop('chatId')
    return m


for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    OTHER = C.replace('3', '4') if ch == 'WA' else C + '9'
    trig = row(ch, 'TRIG', T0, 'https://mock/photo.jpg', from_me=False, type='image')

    def case(title, rows, want_state, want_id=None, error=None, trig_time=T0):
        with Case(f'R10 [{ch}] {title}') as c:
            out = reconcile(T, ch, rows, error, trig_time)
            c.check(out.get('state') == want_state and (want_id is None or out.get('transportMessageId') == want_id),
                    f'got state={out.get("state")} id={out.get("transportMessageId")!r} error={out.get("error")!r}')

    case('our echo newer than the trigger → sent with the echo\'s id', [row(ch, 'ECHO', T0 + 31), trig], 'sent', 'ECHO')
    case('the same text in ANOTHER chat (a crossed row inside our window) → send_unknown',
         [row(ch, 'FOREIGN', T0 + 31, chat=OTHER), trig], 'send_unknown')
    case('a fully crossed window of the neighbour chat (its own trigger-less rows, same template text) → send_unknown',
         [row(ch, 'FOREIGN', T0 + 31, chat=OTHER), row(ch, 'F-IN', T0 + 5, 'фото', from_me=False, chat=OTHER)], 'send_unknown')
    case('a crossed window WITHOUT chatId fields (no trigger in it) → send_unknown',
         [row(ch, 'FOREIGN', T0 + 31, drop_chat=True), row(ch, 'F-IN', T0 + 5, 'фото', from_me=False, drop_chat=True)], 'send_unknown')
    case('two DIFFERENT outgoing rows with our text → ambiguous → send_unknown',
         [row(ch, 'ECHO1', T0 + 32), row(ch, 'ECHO2', T0 + 31), trig], 'send_unknown')
    case('the SAME echo row listed twice (one id) → still one candidate → sent',
         [row(ch, 'ECHO', T0 + 31), row(ch, 'ECHO', T0 + 31), trig], 'sent', 'ECHO')
    # the send window alone (time ≥ sendStartedAt − 5) cannot tell: a trigger stamped late (clocks apart) puts an
    # earlier same-text row inside it — only the window's ORDER shows it is older than the trigger
    case('the same text OLDER than the trigger yet inside the send window (trigger stamped late) → send_unknown',
         [dict(trig, time=T0 + 40), row(ch, 'OLD', T0 + 28)], 'send_unknown', trig_time=T0 + 40)
    case('our echo in rows that carry no chatId (the trigger is there) → sent',
         [row(ch, 'ECHO', T0 + 31, drop_chat=True), dict(trig, chatId=None)], 'sent', 'ECHO')
    case('a foreign row listed first, then our own echo → sent with OUR id',
         [row(ch, 'FOREIGN', T0 + 33, chat=OTHER), row(ch, 'ECHO', T0 + 31), trig], 'sent', 'ECHO')
    case('the owner typed the SAME text by hand and our echo is there too → ambiguous → send_unknown',
         [row(ch, 'MANUAL', T0 + 32), row(ch, 'ECHO', T0 + 31), trig], 'send_unknown')
    case('only a row the transport marks as sent FROM THE PHONE (tapi from_where) with our text → send_unknown',
         [row(ch, 'MANUAL', T0 + 32, from_where='phone'), trig], 'send_unknown')
    case('our echo in the same second as the trigger, listed above it (Wappi order breaks the tie) → sent',
         [row(ch, 'ECHO', T0 + 28), dict(trig, time=T0 + 28)], 'sent', 'ECHO', trig_time=T0 + 28)
    case('our echo in the same second as the trigger but listed BELOW it → not newer → send_unknown',
         [dict(trig, time=T0 + 28), row(ch, 'ECHO', T0 + 28)], 'send_unknown', trig_time=T0 + 28)
    case('no echo at all → send_unknown', [trig, row(ch, 'IN0', T0 - 60, 'Здравствуйте', from_me=False)], 'send_unknown')
    case('the reconcile fetch failed → send_unknown', None, 'send_unknown', error={'message': 'ECONNRESET'})

    with Case(f'R10 [{ch}] end to end: a crossed window after a lost send leaves the ledger send_unknown, no ai history row') as c:
        with PgSession(SETUP) as pg:
            now = int(time.time())               # Final Check stamps sendStartedAt from the real clock
            m = msg(ch, 'TRIG', now - 30, 'https://mock/photo.jpg', mtype='image')
            r = Run(T, pg, m, 'exec-R10')
            r.claim(); r.latest([m]); r.ask_for_text(TEXT); r.final([m]); r.lease()
            r.send({'error': {'message': 'socket hang up'}},
                   reconcile_snapshot=[row(ch, 'FOREIGN', now + 2, chat=OTHER), row(ch, 'F-IN', now - 25, 'фото', from_me=False, chat=OTHER)])
            led = ledger(pg, ch)
            h = [(x['message_id'], x['source']) for x in history(pg, ch)]
            last = pg.q(f"select last_sent_message_id from chat_reply_state where profile_id = '{P}' and chat_id = '{C}'")
            c.check(r.outcome == 'send_unknown' and led.get('TRIG') == 'send_unknown', f'outcome={r.outcome} ledger={led}')
            c.check(('FOREIGN', 'ai') not in h and h == [('TRIG', 'client')], f'history={h}')
            c.check(last and last[0]['last_sent_message_id'] is None, f'last_sent_message_id={last}')

report()
