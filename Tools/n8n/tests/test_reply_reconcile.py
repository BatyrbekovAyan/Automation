#!/usr/bin/env python3
"""R10 (iteration 2026-09-27) + V7 (iteration 2026-09-28): a row that is not PROVABLY our echo must never confirm a
send_unknown.

Reconcile Check runs after a send whose answer was lost: it looks for OUR text as an outgoing row in a fresh
messages/get of the chat and, when found, records `sent` with that row's id. R10 gave it Final Check's guards (a
crossed Wappi/tapi response — a project-confirmed transport bug — or the same phrase elsewhere had turned an unknown
send into a false `sent` with a foreign message id). V7: acceptance was still a NEGATIVE filter — «no phone mark» —
so an unmarked manual row, a row of another profile's window, or the owner's own send from the app (proxy →
message/send, the very same API mark) with our text was adopted, and Record Send then rewrote that owner row's
history source to `ai` and its id into last_sent_message_id.

Contract: rows naming a foreign chatId or profile are dropped, a response whose uuid names another profile is a
foreign window; the window must contain the trigger; EXACTLY ONE distinct outgoing row with our text, newer than
the trigger in the window's order (time desc, Wappi order on ties) and not marked as sent from the phone (tapi
`from_where: 'phone'`, webhook type outgoing_message_phone) is the candidate; it is adopted only with POSITIVE
attribution — the transport's API mark (from_where 'api' / outgoing_message_api / a task id; never isFromAPI) or
the one history's `outgoing_api` row (Owner Row?) — and never when the history gives it to the owner (owner_app /
owner_phone) or to another reply (ai). Anything else stays send_unknown.

Executes the REAL Reconcile Check body of BOTH templates (fed like Owner Row? feeds it: the Reconcile Fetch window
as a named node, the history's sources as the input), and end to end Settle Send → Reconcile Fetch → Owner Row? →
Reconcile Check → Record Send on staging-PG temp tables.

    python3 Tools/n8n/tests/test_reply_reconcile.py
"""
import time

from harness import Case, PgSession, code, has_node, report
from reply_chain import CHATS, SETUP, TEMPLATES, Run, history, ledger, msg, record_outgoing

TEXT = 'Напишите, пожалуйста, текстом — так я быстрее отвечу.'
T0 = 1_790_000_000
API = dict(from_where='api', task_id='9f2c4a7e1b0d4c3a8e6f5a2b1c0d9e8f')   # how the transport marks an API send (tapi rows, WA webhook)


def reconcile(T, ch, rows, error=None, trig_time=T0, uuid=None, sources=None):
    trig = msg(ch, 'TRIG', trig_time, 'https://mock/photo.jpg', mtype='image')
    settle = {'state': 'send_unknown', 'unknown': True, 'transportMessageId': '', 'error': 'socket hang up', 'text': TEXT,
              'sendStartedAt': T0 + 30}
    fetch = {'error': error} if error else dict({'status': 'done', 'messages': rows}, **({'uuid': uuid} if uuid is not None else {}))
    named = {'Settle Send': settle, 'Webhook': {'body': {'messages': [trig]}}, 'Reconcile Fetch': fetch}
    item = {'json': {'sources': sources or {}}} if has_node(T, 'Owner Row?') else {'json': fetch}
    return code(T, 'Reconcile Check', item, named)[0]['json']


def row(ch, mid, t, body=TEXT, from_me=True, chat=None, drop_chat=False, **extra):
    m = msg(ch, mid, t, body, from_me=from_me, chat=chat)
    m.pop('profile_id')                       # messages/get rows carry no profile_id (0 of 617 live tapi rows) — unless a case sets one
    m.update(extra)
    if drop_chat:
        m.pop('chatId')
    return m


for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    OTHER = C.replace('3', '4') if ch == 'WA' else C + '9'
    trig = row(ch, 'TRIG', T0, 'https://mock/photo.jpg', from_me=False, type='image')

    def case(title, rows, want_state, want_id=None, error=None, trig_time=T0, uuid=None, sources=None, want_error=None, want_foreign=None, tag='R10'):
        with Case(f'{tag} [{ch}] {title}') as c:
            out = reconcile(T, ch, rows, error, trig_time, uuid, sources)
            c.check(out.get('state') == want_state and (want_id is None or out.get('transportMessageId') == want_id)
                    and (want_error is None or want_error in (out.get('error') or ''))
                    and (want_foreign is None or out.get('foreignFetched') == want_foreign),
                    f'got state={out.get("state")} id={out.get("transportMessageId")!r} error={out.get("error")!r} foreign={out.get("foreignFetched")}')

    case('our echo newer than the trigger → sent with the echo\'s id', [row(ch, 'ECHO', T0 + 31, **API), trig], 'sent', 'ECHO')
    case('the same text in ANOTHER chat (a crossed row inside our window) → send_unknown',
         [row(ch, 'FOREIGN', T0 + 31, chat=OTHER, **API), trig], 'send_unknown')
    case('a fully crossed window of the neighbour chat (its own trigger-less rows, same template text) → send_unknown',
         [row(ch, 'FOREIGN', T0 + 31, chat=OTHER, **API), row(ch, 'F-IN', T0 + 5, 'фото', from_me=False, chat=OTHER)], 'send_unknown')
    case('a crossed window WITHOUT chatId fields (no trigger in it) → send_unknown',
         [row(ch, 'FOREIGN', T0 + 31, drop_chat=True, **API), row(ch, 'F-IN', T0 + 5, 'фото', from_me=False, drop_chat=True)], 'send_unknown')
    case('two DIFFERENT outgoing rows with our text → ambiguous → send_unknown',
         [row(ch, 'ECHO1', T0 + 32, **API), row(ch, 'ECHO2', T0 + 31, **API), trig], 'send_unknown')
    case('the SAME echo row listed twice (one id) → still one candidate → sent',
         [row(ch, 'ECHO', T0 + 31, **API), row(ch, 'ECHO', T0 + 31, **API), trig], 'sent', 'ECHO')
    # the send window alone (time ≥ sendStartedAt − 5) cannot tell: a trigger stamped late (clocks apart) puts an
    # earlier same-text row inside it — only the window's ORDER shows it is older than the trigger
    case('the same text OLDER than the trigger yet inside the send window (trigger stamped late) → send_unknown',
         [dict(trig, time=T0 + 40), row(ch, 'OLD', T0 + 28, **API)], 'send_unknown', trig_time=T0 + 40)
    case('our echo in rows that carry no chatId (the trigger is there) → sent',
         [row(ch, 'ECHO', T0 + 31, drop_chat=True, **API), dict(trig, chatId=None)], 'sent', 'ECHO')
    case('a foreign row listed first, then our own echo → sent with OUR id',
         [row(ch, 'FOREIGN', T0 + 33, chat=OTHER, **API), row(ch, 'ECHO', T0 + 31, **API), trig], 'sent', 'ECHO')
    case('the owner typed the SAME text by hand and our echo is there too → ambiguous → send_unknown',
         [row(ch, 'MANUAL', T0 + 32), row(ch, 'ECHO', T0 + 31, **API), trig], 'send_unknown')
    case('only a row the transport marks as sent FROM THE PHONE (tapi from_where) with our text → send_unknown',
         [row(ch, 'MANUAL', T0 + 32, from_where='phone'), trig], 'send_unknown')
    case('our echo in the same second as the trigger, listed above it (Wappi order breaks the tie) → sent',
         [row(ch, 'ECHO', T0 + 28, **API), dict(trig, time=T0 + 28)], 'sent', 'ECHO', trig_time=T0 + 28)
    case('our echo in the same second as the trigger but listed BELOW it → not newer → send_unknown',
         [dict(trig, time=T0 + 28), row(ch, 'ECHO', T0 + 28, **API)], 'send_unknown', trig_time=T0 + 28)
    case('no echo at all → send_unknown', [trig, row(ch, 'IN0', T0 - 60, 'Здравствуйте', from_me=False)], 'send_unknown')
    case('the reconcile fetch failed → send_unknown', None, 'send_unknown', error={'message': 'ECONNRESET'})

    # --- V7: positive attribution ---------------------------------------------------------------------------------
    case('the ONLY candidate carries no transport mark at all (a manual row) → send_unknown, named unattributed',
         [row(ch, 'MANUAL', T0 + 32), trig], 'send_unknown', want_error='unattributed', tag='V7')
    case('a row naming ANOTHER profile, API-marked, with our text → dropped → send_unknown',
         [row(ch, 'OTHERP', T0 + 32, profile_id='OTHER_PROFILE', **API), trig], 'send_unknown', want_foreign=1, tag='V7')
    case('the webhook phone type with a task id (hostile mix) → the phone wins → send_unknown',
         [row(ch, 'PHONEWH', T0 + 32, wh_type='outgoing_message_phone', task_id='abc123'), trig], 'send_unknown', tag='V7')
    case('a response whose uuid names ANOTHER profile, a perfect API echo inside → foreign window → send_unknown',
         [row(ch, 'ECHO', T0 + 31, **API), trig], 'send_unknown', uuid='another-profile-0000', want_error='window of another profile',
         want_foreign=2, tag='V7')
    case('a response whose uuid names OUR profile → no objection → sent', [row(ch, 'ECHO', T0 + 31, **API), trig], 'sent', 'ECHO',
         uuid=P.upper(), tag='V7')
    case('only isFromAPI:true (tapi sets it on phone rows too) is NOT an API mark → send_unknown',
         [row(ch, 'FLAG', T0 + 32, isFromAPI=True), trig], 'send_unknown', tag='V7')
    case('the owner\'s app send (API-marked) next to our API echo → ambiguous → send_unknown',
         [row(ch, 'OWNER-APP', T0 + 32, **API), row(ch, 'ECHO', T0 + 31, **API), trig], 'send_unknown', tag='V7')
    case('a phone row next to our API echo → sent with OUR id', [row(ch, 'MANUAL', T0 + 32, from_where='phone'), row(ch, 'ECHO', T0 + 31, **API), trig],
         'sent', 'ECHO', tag='V7')
    case('a task id alone (no from_where key) is an API mark → sent', [row(ch, 'ECHO', T0 + 31, task_id='77aa'), trig], 'sent', 'ECHO', tag='V7')
    case('from_where api with an empty task id is an API mark → sent', [row(ch, 'ECHO', T0 + 31, from_where='api', task_id=''), trig], 'sent', 'ECHO', tag='V7')
    case('the history records the API-marked candidate as the OWNER\'s app send → send_unknown, named',
         [row(ch, 'OWNER-APP', T0 + 32, **API), trig], 'send_unknown', sources={'OWNER-APP': 'owner_app'}, want_error='owner_app', tag='V7')
    case('the history records the candidate as owner_phone → send_unknown',
         [row(ch, 'OWN-PH', T0 + 32, **API), trig], 'send_unknown', sources={'OWN-PH': 'owner_phone'}, tag='V7')
    case('the history already gives the candidate to another reply (ai) → send_unknown',
         [row(ch, 'AI-OTHER', T0 + 32, **API), trig], 'send_unknown', sources={'AI-OTHER': 'ai'}, tag='V7')
    case('an UNMARKED candidate the history recorded from the transport\'s outgoing_message_api webhook → sent (WA rows without marks)',
         [row(ch, 'OUT-API', T0 + 32), trig], 'sent', 'OUT-API', sources={'OUT-API': 'outgoing_api'}, tag='V7')
    case('outgoing_api of ANOTHER row does not attribute the unmarked candidate → send_unknown',
         [row(ch, 'MANUAL', T0 + 32), trig], 'send_unknown', sources={'ELSEWHERE': 'outgoing_api'}, tag='V7')

    with Case(f'R10 [{ch}] end to end: a crossed window after a lost send leaves the ledger send_unknown, no ai history row') as c:
        with PgSession(SETUP) as pg:
            now = int(time.time())               # Final Check stamps sendStartedAt from the real clock
            m = msg(ch, 'TRIG', now - 30, 'https://mock/photo.jpg', mtype='image')
            r = Run(T, pg, m, 'exec-R10')
            r.claim(); r.latest([m]); r.ask_for_text(TEXT); r.final([m]); r.lease()
            r.send({'error': {'message': 'socket hang up'}},
                   reconcile_snapshot=[row(ch, 'FOREIGN', now + 2, chat=OTHER, **API), row(ch, 'F-IN', now - 25, 'фото', from_me=False, chat=OTHER)])
            led = ledger(pg, ch)
            h = [(x['message_id'], x['source']) for x in history(pg, ch)]
            last = pg.q(f"select last_sent_message_id from chat_reply_state where profile_id = '{P}' and chat_id = '{C}'")
            c.check(r.outcome == 'send_unknown' and led.get('TRIG') == 'send_unknown', f'outcome={r.outcome} ledger={led}')
            c.check(('FOREIGN', 'ai') not in h and h == [('TRIG', 'client')], f'history={h}')
            c.check(last and last[0]['last_sent_message_id'] is None, f'last_sent_message_id={last}')

    with Case(f'V7 [{ch}] end to end: the only same-text row is the OWNER\'s app send (owner_app in the history) → send_unknown; the owner row keeps its source') as c:
        with PgSession(SETUP) as pg:
            now = int(time.time())
            m = msg(ch, 'TRIG', now - 30, 'https://mock/photo.jpg', mtype='image')
            pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at, execution_id) "
                 f"values ('{P}', '{C}', 'OWNER-APP', 'owner_app', '{TEXT}', 'chat', now(), 'exec-proxy');")
            r = Run(T, pg, m, 'exec-V7')
            r.claim(); r.latest([m]); r.ask_for_text(TEXT); r.final([m]); r.lease()
            st = r.named['Final Check']['sendStartedAt']          # the row sits inside the send window whatever the load
            r.send({'error': {'message': 'socket hang up'}}, reconcile_snapshot=[row(ch, 'OWNER-APP', st + 2, **API), m])
            led = ledger(pg, ch)
            h = sorted((x['message_id'], x['source']) for x in history(pg, ch))
            last = pg.q(f"select last_sent_message_id from chat_reply_state where profile_id = '{P}' and chat_id = '{C}'")
            c.check(r.outcome == 'send_unknown' and led.get('TRIG') == 'send_unknown', f'outcome={r.outcome} ledger={led}')
            c.check('owner_app' in (r.named.get('Reconcile Check') or {}).get('error', ''), f'the refusal must name the owner row: {r.named.get("Reconcile Check")}')
            c.check(h == [('OWNER-APP', 'owner_app'), ('TRIG', 'client')], f'history={h}')
            c.check(last and last[0]['last_sent_message_id'] is None, f'last_sent_message_id={last}')

    with Case(f'V7 [{ch}] end to end: an unmarked echo the transport\'s outgoing_message_api webhook already recorded → sent, upgraded to ai') as c:
        with PgSession(SETUP) as pg:
            now = int(time.time())
            m = msg(ch, 'TRIG', now - 30, 'https://mock/photo.jpg', mtype='image')
            r = Run(T, pg, m, 'exec-V7b')
            r.claim(); r.latest([m]); r.ask_for_text(TEXT); r.final([m]); r.lease()
            st = r.named['Final Check']['sendStartedAt']
            record_outgoing(T, pg, ch, 'OUT-API', TEXT, st + 2)             # the webhook of our own API send landed first
            r.send({'error': {'message': 'socket hang up'}}, reconcile_snapshot=[row(ch, 'OUT-API', st + 2), m])
            h = sorted((x['message_id'], x['source']) for x in history(pg, ch))
            c.check(r.outcome == 'sent' and ledger(pg, ch).get('TRIG') == 'sent', f'outcome={r.outcome}')
            c.check(h == [('OUT-API', 'ai'), ('TRIG', 'client')], f'history={h}')

report()
