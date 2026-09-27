#!/usr/bin/env python3
"""R7 (iteration 2026-09-27): a cancelled or failed reply removes ONLY its own AI memory row.

The Chat Memory node writes a human + an ai row at the end of every agent run, with no mark of which run wrote
them. Record Cancel used to drop EVERY unmarked ai row above the cancelled run's watermark — including the reply a
NEWER run had already written (and possibly sent): the bot then forgot what the client really received. And a
send_failed reply stayed in memory although the client never got it.

Contract (decision of the iteration README, R7):
  * a cancel (superseded / cancelled_owner / cancelled_fetch / cancelled_mode / cancelled_busy) after the agent ran
    removes the FIRST ai row above its own watermark whose content is ITS OWN text; nothing else;
  * send_failed removes its own row the same way; send_unknown KEEPS it (for Wappi «unknown» usually means
    «delivered, answer lost»);
  * rows with additional_kwargs.source (owner_phone / owner_app / client) are never touched; human rows stay;
  * a run whose agent never ran (the media branch) removes nothing;
  * the transport's echo of a sent reply (outgoing_message_api → Record Outgoing) never adds a second row.

The REAL chain of BOTH templates (reply_chain.Run): Claim Event, Latest+Combine, Final Check, Acquire Send Lease,
Settle Send, Record Send, Cancel Label ($prevNode = the refusing gate), Record Cancel, Record Outgoing — on session
temp tables of the staging Postgres. Memory rows are written in the memory node's own flat shape.

    python3 Tools/n8n/tests/test_reply_memory.py
"""
from harness import Case, PgSession, report
from reply_chain import SETUP, TEMPLATES, Run, history, memory, msg, record_outgoing

OLD_TEXT = 'Букет из 25 роз стоит 25 000 ₸.'
NEW_TEXT = 'Букет из 25 роз стоит 25 000 ₸, доставка по городу бесплатно.'


def ai_rows(pg, ch):
    return [r['content'] for r in memory(pg, ch) if r['type'] == 'ai' and r['source'] is None]


def human_rows(pg, ch):
    return [r['content'] for r in memory(pg, ch) if r['type'] == 'human']


for ch, T in TEMPLATES.items():
    def pair(pg):
        mo = msg(ch, 'O', 100, 'Сколько стоит букет?')
        mn = msg(ch, 'N', 130, 'А с доставкой?')
        return mo, mn, Run(T, pg, mo, 'exec-O'), Run(T, pg, mn, 'exec-N')

    with Case(f'R7 [{ch}] old draft → new reply SENT → old cancelled: the new reply stays in memory (review scenario)') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(OLD_TEXT)
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT); n.final([mn, mo]); n.lease(); n.send_ok()
            o.final([mn, mo])                                         # O's final snapshot now holds the newer N
            c.check(n.outcome == 'sent' and o.outcome == 'superseded', f'n={n.outcome} o={o.outcome}')
            c.check(ai_rows(pg, ch) == [NEW_TEXT], f'ai rows={ai_rows(pg, ch)} (dropped={o.cancel.get("dropped_ai_rows")})')
            c.check(len(human_rows(pg, ch)) == 2, f'human rows={human_rows(pg, ch)}')

    with Case(f'R7 [{ch}] new reply WRITTEN but not yet sent when the old run cancels → it survives, then goes out') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(OLD_TEXT)
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT)
            o.final([mn, mo])
            n.final([mn, mo]); n.lease(); n.send_ok()
            c.check(n.outcome == 'sent' and o.outcome == 'superseded', f'n={n.outcome} o={o.outcome}')
            c.check(ai_rows(pg, ch) == [NEW_TEXT], f'ai rows={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] the newer message arrives during the OLD run\'s LLM call (claimed before its draft) → only the new reply stays') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo])
            n.claim()                                                  # N's watermark is below O's draft
            o.generate(OLD_TEXT)
            n.latest([mn, mo]); n.generate(NEW_TEXT)
            o.final([mn, mo])
            n.final([mn, mo]); n.lease(); n.send_ok()
            c.check(ai_rows(pg, ch) == [NEW_TEXT], f'ai rows={ai_rows(pg, ch)} — the unsent draft must go, the sent reply must stay')

    with Case(f'R7 [{ch}] the old run is refused at the LEASE (Send Allowed?) after the new reply went out → only its own draft goes') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(OLD_TEXT); o.final([mo])     # O's snapshot is stale: N not there yet
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT); n.final([mn, mo]); n.lease(); n.send_ok()
            o.lease()
            c.check(o.outcome == 'superseded' and ai_rows(pg, ch) == [NEW_TEXT], f'o={o.outcome} ai rows={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] two runs produced the SAME text: exactly one ai row survives (one per reply the client saw)') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(NEW_TEXT)
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT); n.final([mn, mo]); n.lease(); n.send_ok()
            o.final([mn, mo])
            c.check(ai_rows(pg, ch) == [NEW_TEXT], f'ai rows={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] the owner already answered (cancelled_owner): the draft goes, the owner\'s marked row — same text — stays') as c:
        with PgSession(SETUP) as pg:
            mo = msg(ch, 'O', 100, 'Сколько стоит букет?')
            o = Run(T, pg, mo, 'exec-O')
            o.claim(); o.latest([mo]); o.generate(OLD_TEXT)
            record_outgoing(T, pg, ch, 'own-1', OLD_TEXT, 120, wh_type='outgoing_message_phone')   # owner_phone memory row
            o.final([msg(ch, 'own-1', 120, OLD_TEXT, from_me=True), mo])
            mem = [(r['type'], r['source'], r['content']) for r in memory(pg, ch)]
            c.check(o.outcome == 'cancelled_owner', f'o={o.outcome}')
            c.check(ai_rows(pg, ch) == [] and ('ai', 'owner_phone', OLD_TEXT) in mem and len(human_rows(pg, ch)) == 1, f'memory={mem}')

    with Case(f'R7 [{ch}] send_failed: the reply the client never got leaves memory; the client\'s own row stays') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'F', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-F')
            r.claim(); r.latest([m]); r.generate(OLD_TEXT); r.final([m]); r.lease()
            r.send({'statusCode': 500, 'body': {'status': 'error', 'detail': 'boom'}})
            c.check(r.outcome == 'send_failed' and ai_rows(pg, ch) == [] and human_rows(pg, ch) == ['Сколько стоит букет?'],
                    f'outcome={r.outcome} memory={memory(pg, ch)}')

    with Case(f'R7 [{ch}] send_failed of the NEWER run after an older reply was sent: only the failed text leaves') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(OLD_TEXT); o.final([mo]); o.lease(); o.send_ok()
            n.claim(); n.latest([mn, msg(ch, 'sent-O', 110, OLD_TEXT, from_me=True), mo]); n.generate(NEW_TEXT)
            n.final([mn, msg(ch, 'sent-O', 110, OLD_TEXT, from_me=True), mo]); n.lease()
            n.send({'statusCode': 502, 'body': {'status': 'error'}})
            c.check(o.outcome == 'sent' and n.outcome == 'send_failed' and ai_rows(pg, ch) == [OLD_TEXT], f'o={o.outcome} n={n.outcome} ai={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] send_unknown (no echo found) KEEPS the reply in memory (decision: «unknown» usually means delivered)') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'U', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-U')
            r.claim(); r.latest([m]); r.generate(OLD_TEXT); r.final([m]); r.lease()
            r.send({'error': {'message': 'socket hang up'}}, reconcile_snapshot=[m])
            c.check(r.outcome == 'send_unknown' and ai_rows(pg, ch) == [OLD_TEXT], f'outcome={r.outcome} ai={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] a media message (Ask to Send Text, the agent never ran) cancelled after a newer reply → nothing removed') as c:
        with PgSession(SETUP) as pg:
            mp = msg(ch, 'P', 100, 'https://mock/photo.jpg', mtype='image')
            mn = msg(ch, 'N', 130, 'Сколько стоит букет?')
            p, n = Run(T, pg, mp, 'exec-P'), Run(T, pg, mn, 'exec-N')
            p.claim(); p.latest([mp]); p.ask_for_text('Напишите, пожалуйста, текстом.')
            n.claim(); n.latest([mn, mp]); n.generate(NEW_TEXT); n.final([mn, mp]); n.lease(); n.send_ok()
            p.final([mn, mp])
            c.check(p.outcome == 'superseded' and ai_rows(pg, ch) == [NEW_TEXT], f'p={p.outcome} ai={ai_rows(pg, ch)}')

    for echo_first in (False, True):
        with Case(f'R7 [{ch}] the transport echo of a sent reply ({"before" if echo_first else "after"} Record Send) adds no second row') as c:
            with PgSession(SETUP) as pg:
                m = msg(ch, 'E', 100, 'Сколько стоит букет?')
                r = Run(T, pg, m, 'exec-E')
                r.claim(); r.latest([m]); r.generate(OLD_TEXT); r.final([m]); r.lease()
                if echo_first:
                    record_outgoing(T, pg, ch, 'sent-E', OLD_TEXT, 101)
                r.send_ok('sent-E')
                if not echo_first:
                    record_outgoing(T, pg, ch, 'sent-E', OLD_TEXT, 101)
                h = [(x['message_id'], x['source']) for x in history(pg, ch)]
                c.check(ai_rows(pg, ch) == [OLD_TEXT] and len(memory(pg, ch)) == 2, f'memory={memory(pg, ch)}')
                c.check(h == [('E', 'client'), ('sent-E', 'ai')], f'history={h}')

    # R7 part 2 (found by the staging e2e run): pg-promise substitutes $N EVERYWHERE in the query text, SQL comments
    # included — a `$8` inside a `--` comment inlined a MULTI-LINE reply there, its second line escaped the comment
    # and the statement died with a syntax error, leaving the ledger at send_pending. Real replies are multi-line.
    MULTI = 'Здравствуйте!\nБукет из 25 роз — 25 000 ₸.\nМәтінмен жазыңызшы.'

    with Case(f'R7 [{ch}] a MULTI-LINE reply goes through Record Send (sent → ledger sent, one ai history row)') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'ML', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-ML')
            r.claim(); r.latest([m]); r.generate(MULTI); r.final([m]); r.lease(); r.send_ok('sent-ML')
            h = [(x['message_id'], x['source'], x['body']) for x in history(pg, ch)]
            c.check(r.outcome == 'sent' and ('sent-ML', 'ai', MULTI) in h, f'outcome={r.outcome} history={h}')

    with Case(f'R7 [{ch}] a MULTI-LINE reply refused by the transport (send_failed) leaves memory; the ledger says send_failed') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'MF', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-MF')
            r.claim(); r.latest([m]); r.generate(MULTI); r.final([m]); r.lease()
            r.send({'statusCode': 500, 'body': {'status': 'error'}})
            c.check(r.outcome == 'send_failed' and ai_rows(pg, ch) == [], f'outcome={r.outcome} ai={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] a MULTI-LINE draft of a cancelled run is removed; the newer reply stays') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(MULTI)
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT); n.final([mn, mo]); n.lease(); n.send_ok()
            o.final([mn, mo])
            c.check(o.outcome == 'superseded' and ai_rows(pg, ch) == [NEW_TEXT], f'o={o.outcome} ai={ai_rows(pg, ch)}')

    with Case(f'R7 [{ch}] the media branch\'s MULTI-LINE canned ask goes through Record Send') as c:
        with PgSession(SETUP) as pg:
            mp = msg(ch, 'MP', 100, 'https://mock/photo.jpg', mtype='image')
            r = Run(T, pg, mp, 'exec-MP')
            r.claim(); r.latest([mp]); r.ask_for_text(MULTI); r.final([mp]); r.lease(); r.send_ok('sent-MP')
            c.check(r.outcome == 'sent', f'outcome={r.outcome}')

report()
