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
V6 (iteration 2026-09-28): «its own» is decided by the EXECUTION, never by the text — Stamp Draft (right after the
agent) marks the row with execution_id / event_id / delivery=draft; Record Cancel and Record Send(send_failed)
delete only that row, sent / send_unknown mark it delivery=sent (+ transport id) / unknown, and a run without a
stamp touches nothing. Two residuals are pinned as documented (crossed stamps, an unstamped same-text neighbour).

The REAL chain of BOTH templates (reply_chain.Run): Claim Event, Latest+Combine, Final Check, Acquire Send Lease,
Settle Send, Record Send, Cancel Label ($prevNode = the refusing gate), Record Cancel, Record Outgoing — on session
temp tables of the staging Postgres. Memory rows are written in the memory node's own flat shape.

    python3 Tools/n8n/tests/test_reply_memory.py
"""
from harness import Case, PgSession, report
from reply_chain import CHATS, SETUP, TEMPLATES, Run, _sql, history, memory, msg, record_outgoing

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

    # V6 (iteration 2026-09-28): R7's selector «the first unmarked ai row above the watermark with this text» did not
    # tie the row to the run — another run of the chat that produced the SAME text (template phrases do) lost its
    # sent reply to this run's cancel, while this run's own draft stayed; and send_unknown kept a row nobody could tell
    # from a confirmed one. Contract: Stamp Draft (right after the agent) ties the row to the execution; Record Cancel /
    # Record Send(send_failed) delete ONLY that row; sent / send_unknown mark it (delivery sent + transport id /
    # unknown); a run without a stamp touches nothing. Two documented residuals are pinned below, not called closed.
    def marks(pg):
        return [(r['content'], r['execution_id'], r['event_id'], r['delivery'], r['transport_message_id'])
                for r in memory(pg, ch) if r['type'] == 'ai' and r['source'] is None]

    with Case(f'V6 [{ch}] reversed completion with the SAME text: the newer run\'s SENT row survives (its id, delivery sent); the old run drops only its own draft') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo])
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT); n.final([mn, mo]); n.lease(); n.send_ok('new-sent')
            sent_id = [r['id'] for r in memory(pg, ch) if r['type'] == 'ai'][0]
            record_outgoing(T, pg, ch, 'own-1', 'Да, доставка бесплатно.', 135, wh_type='outgoing_message_phone')
            o.generate(NEW_TEXT)                                        # the OLD run's agent returns last, same text
            o.final([mn, mo])
            mem = memory(pg, ch)
            row = next((r for r in mem if r['id'] == sent_id), None)
            c.check(n.outcome == 'sent' and o.outcome == 'superseded', f'n={n.outcome} o={o.outcome}')
            c.check(row is not None and row['execution_id'] == 'exec-N' and row['delivery'] == 'sent' and row['transport_message_id'] == 'new-sent',
                    f'the sent row must survive, stamped: {row} memory={[(r["id"], r["type"], r["source"], r["execution_id"]) for r in mem]}')
            c.check(o.cancel and o.cancel.get('dropped_ai_rows') == 1, f'o.cancel={o.cancel}')
            order = [(r['type'], r['source'], r['content']) for r in mem]
            c.check(order == [('human', None, n.combined), ('ai', None, NEW_TEXT), ('ai', 'owner_phone', 'Да, доставка бесплатно.'),
                              ('human', None, o.combined)], f'memory order={order}')

    with Case(f'V6 [{ch}] the row says what happened: draft after the agent, sent + transport id after a send, unknown after send_unknown') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'D', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-D')
            r.claim(); r.latest([m]); r.generate(OLD_TEXT)
            d1 = marks(pg)
            r.final([m]); r.lease(); r.send_ok('sent-D')
            d2 = marks(pg)
            c.check(d1 == [(OLD_TEXT, 'exec-D', 'D', 'draft', None)], f'after the agent: {d1} stamp={getattr(r, "stamp_row", None)}')
            c.check(d2 == [(OLD_TEXT, 'exec-D', 'D', 'sent', 'sent-D')] and r.record.get('marked_ai_rows') == 1, f'after the send: {d2} record={r.record}')
        with PgSession(SETUP) as pg:
            m = msg(ch, 'U', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-U')
            r.claim(); r.latest([m]); r.generate(OLD_TEXT); r.final([m]); r.lease()
            r.send({'error': {'message': 'socket hang up'}}, reconcile_snapshot=[m])
            c.check(r.outcome == 'send_unknown' and marks(pg) == [(OLD_TEXT, 'exec-U', 'U', 'unknown', None)],
                    f'send_unknown keeps the row and says so: outcome={r.outcome} marks={marks(pg)}')

    with Case(f'V6 [{ch}] Record Send(send_failed) removes ONLY its execution\'s row while another run\'s identical SENT row sits lower above its watermark') as c:
        # the chain cannot bring two runs of one chat to the transport side by side (that is V5's class), so the
        # failing run's Record Send is driven with the node's own statement and parameters, as the node would run it
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            n.claim()                                                   # N's watermark sits below everything below
            o.claim(); o.latest([mo]); o.generate(NEW_TEXT); o.final([mo]); o.lease(); o.send_ok('sent-O')
            echo = msg(ch, 'sent-O', 110, NEW_TEXT, from_me=True)
            n.latest([mn, echo, mo]); n.generate(NEW_TEXT)
            n.named['Final Check'] = {'text': NEW_TEXT, 'historyWatermark': int(n.claim_row.get('history_watermark') or 0)}
            rec = _sql(T, pg, 'Record Send', {'state': 'send_failed', 'transportMessageId': '', 'error': 'http 500'}, n.named, 'exec-N')
            c.check(rec.get('dropped_ai_rows') == 1, f'record={rec}')
            c.check(marks(pg) == [(NEW_TEXT, 'exec-O', 'O', 'sent', 'sent-O')], f'the other run\'s sent reply must stay: {marks(pg)}')

    with Case(f'V6 [{ch}] a run whose stamp did not happen drops NOTHING on cancel — no text fallback') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo]); o.generate(OLD_TEXT, stamp=False)      # Stamp Draft failed (DB hiccup, continue)
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT); n.final([mn, mo]); n.lease(); n.send_ok()
            o.final([mn, mo])
            c.check(o.outcome == 'superseded' and o.cancel and o.cancel.get('dropped_ai_rows') == 0 and ai_rows(pg, ch) == [OLD_TEXT, NEW_TEXT],
                    f'o={o.outcome} cancel={o.cancel} ai={ai_rows(pg, ch)}')

    with Case(f'V6 [{ch}] residual (pinned): two same-text rows stamped in REVERSE order (within one PG round trip) → one row survives, '
              f'sent by N, but it sits after the owner row written between them') as c:
        with PgSession(SETUP) as pg:
            mo, mn, o, n = pair(pg)
            o.claim(); o.latest([mo])
            n.claim(); n.latest([mn, mo]); n.generate(NEW_TEXT, stamp=False)       # W_N
            record_outgoing(T, pg, ch, 'own-1', 'Минутку.', 120, wh_type='outgoing_message_phone')
            o.generate(NEW_TEXT, stamp=False)                                       # W_O
            o.stamp(); n.stamp()                                                    # the stamps cross
            n.final([mn, mo]); n.lease(); n.send_ok('new-sent')
            o.final([mn, mo])
            types = [(r['type'], r['source']) for r in memory(pg, ch)]
            c.check(marks(pg) == [(NEW_TEXT, 'exec-N', 'N', 'sent', 'new-sent')], f'exactly one row, the sent one: {marks(pg)}')
            c.check(types == [('human', None), ('ai', 'owner_phone'), ('human', None), ('ai', None)], f'documented shift: {types}')

    with Case(f'V6 [{ch}] residual (pinned): an UNSTAMPED same-text neighbour row above the watermark takes this run\'s stamp; '
              f'no row is removed and exactly one is marked sent') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'M', 100, 'Сколько стоит букет?')
            r = Run(T, pg, m, 'exec-M')
            r.claim(); r.latest([m])
            P_, C_ = CHATS[ch]
            pg.x("insert into n8n_chat_histories (session_id, message) values ("
                 f"'{P_}:{C_}', jsonb_build_object('type', 'ai', 'content', '{NEW_TEXT}', 'additional_kwargs', '{{}}'::jsonb, 'response_metadata', '{{}}'::jsonb));")
            r.generate(NEW_TEXT); r.final([m]); r.lease(); r.send_ok('sent-M')
            mk = marks(pg)
            c.check(len(mk) == 2 and mk[0][1:] == ('exec-M', 'M', 'sent', 'sent-M') and mk[1][1:] == (None, None, None, None),
                    f'the neighbour row carries the stamp, the own row stays a phantom: {mk}')

report()
