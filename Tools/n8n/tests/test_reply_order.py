#!/usr/bin/env python3
"""R6 (iteration 2026-09-27): a message that arrives LATE (an older message whose webhook is claimed after a newer
one) must never cancel the only reply to the conversation.

The chat's `incoming_generation` is the lease's staleness check: a run may send only while no NEWER message has
been claimed since its own claim. It used to count claim ARRIVALS, while Latest+Combine / Final Check pick the
winner by MESSAGE order — so B(t=101) claimed before a late A(t=100) got generation 1, A bumped it to 2, B lost the
lease (superseded) and A had already stopped at Latest+Combine (B is newer): zero replies. The contract now is
that the generation counts NEWER messages: a claim bumps it only when its message time is strictly newer than
the newest the chat has seen (unknown time → the old always-bump rule).

Every scenario drives the REAL chain of BOTH templates (reply_chain.Run): Claim Event → Latest+Combine →
Final Check → Acquire Send Lease → Settle Send → Record Send, refusals through Cancel Label → Record Cancel, on
session temp tables of the staging Postgres; the runs are interleaved as parallel n8n executions interleave.

LID: a WhatsApp chat whose messages/get rows Wappi lists under the chat's @lid code (the webhook names the chat by its
number and carries the code in whatsapp_chat_id) is answered exactly once — Latest+Combine and Final Check take a row as the
chat's by its id or that code (audience.CHAT_IDENTITY_JS); rows under any other code stay foreign; a code that is empty,
has no @lid suffix or is the chat id itself changes nothing; Telegram has no code and stays as it was.

    python3 Tools/n8n/tests/test_reply_order.py
"""
from harness import Case, PgSession, code, report
# the test chats are business chats (the owner's decision): the templates answer only there («Личные и рабочие чаты»)
from reply_chain import CHATS, SETUP_BUSINESS as SETUP, TEMPLATES, Run, business, ledger, msg, under_code


def walk(run, snapshot):
    """A run from its claim to its end on one fixed snapshot (the chat does not change under it)."""
    if run.done:
        return run
    run.latest(snapshot)
    if run.done:
        return run
    run.generate()
    run.final(snapshot)
    if run.done:
        return run
    run.lease()
    if run.done:
        return run
    if getattr(run, 'recheck_pending', False):        # V5: the lease asked for ONE re-read — the chat is unchanged here
        run.final(snapshot)
        if run.done:
            return run
        run.lease()
        if run.done:
            return run
    run.send_ok()
    return run


def sent(runs):
    return [r for r in runs if r.outcome == 'sent']


for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]

    def two(order, wappi_order, times=(100, 101), texts=('Здравствуйте', 'Сколько стоит букет из 25 роз?')):
        """Messages A (times[0]) and B (times[1]); claims in `order` (e.g. 'BA'); the snapshot lists them in
        `wappi_order` (newest first, as messages/get does — for equal times this IS Wappi's tie-break)."""
        with PgSession(SETUP) as pg:
            ms = {'A': msg(ch, 'A', times[0], texts[0]), 'B': msg(ch, 'B', times[1], texts[1])}
            runs = {k: Run(T, pg, ms[k], 'exec-' + k) for k in ms}
            claims = {}
            for k in order:
                claims[k] = runs[k].claim()
            snap = [ms[k] for k in wappi_order]
            for k in order:
                walk(runs[k], snap)
            return runs, claims, ledger(pg, ch)

    with Case(f'R6 [{ch}] in order: A(100) claimed, then B(101) → exactly one reply, by B, answering A and B') as c:
        runs, claims, led = two('AB', 'BA')
        s = sent(runs.values())
        c.check(len(s) == 1 and s[0].m['id'] == 'B', f'sent={[r.m["id"] for r in s]} ledger={led}')
        c.check(s and 'Здравствуйте' in s[0].text and 'букет' in s[0].text, f'the reply must answer A+B: {s and s[0].text}')

    with Case(f'R6 [{ch}] REORDERED: B(101) claimed first, then the late A(100) → exactly one reply, by B (was zero)') as c:
        runs, claims, led = two('BA', 'BA')
        s = sent(runs.values())
        c.check(len(s) == 1 and s[0].m['id'] == 'B', f'sent={[r.m["id"] for r in s]} outcomes={ {k: r.outcome for k, r in runs.items()} } '
                                                      f'claims={ {k: v.get("generation") for k, v in claims.items()} }')
        c.check(s and 'Здравствуйте' in s[0].text and 'букет' in s[0].text, f'the reply must answer A+B: {s and s[0].text}')
        c.check(led.get('A') == 'superseded', f'the late A stops as superseded, never sends: {led}')

    for order in ('AB', 'BA'):
        for wappi in ('AB', 'BA'):
            with Case(f'R6 [{ch}] EQUAL timestamps, claims {order[0]}→{order[1]}, Wappi lists {wappi[0]} first → exactly one reply') as c:
                runs, claims, led = two(order, wappi, times=(100, 100))
                s = sent(runs.values())
                c.check(len(s) == 1 and s[0].m['id'] == wappi[0],
                        f'sent={[r.m["id"] for r in s]} (Latest+Combine picks {wappi[0]}) outcomes={ {k: r.outcome for k, r in runs.items()} }')

    with Case(f'R6 [{ch}] duplicates: B, A, B again, A again (re-deliveries) → one reply by B, the repeats are duplicates') as c:
        with PgSession(SETUP) as pg:
            ma, mb = msg(ch, 'A', 100), msg(ch, 'B', 101)
            b1, a1, b2, a2 = Run(T, pg, mb, 'e-b1'), Run(T, pg, ma, 'e-a1'), Run(T, pg, mb, 'e-b2'), Run(T, pg, ma, 'e-a2')
            for r in (b1, a1, b2, a2):
                r.claim()
            for r in (b1, a1, b2, a2):
                walk(r, [mb, ma])
            s = sent((b1, a1, b2, a2))
            c.check(len(s) == 1 and s[0] is b1 and b2.outcome == 'duplicate' and a2.outcome == 'duplicate',
                    f'outcomes={[r.outcome for r in (b1, a1, b2, a2)]}')

    with Case(f'R6 [{ch}] three messages claimed C→A→B (times A<B<C) → exactly one reply, by C, answering all three') as c:
        with PgSession(SETUP) as pg:
            ms = {'A': msg(ch, 'A', 100, 'раз'), 'B': msg(ch, 'B', 101, 'два'), 'C': msg(ch, 'C', 102, 'три')}
            runs = {k: Run(T, pg, ms[k], 'e-' + k) for k in ms}
            for k in 'CAB':
                runs[k].claim()
            for k in 'CAB':
                walk(runs[k], [ms['C'], ms['B'], ms['A']])
            s = sent(runs.values())
            c.check(len(s) == 1 and s[0].m['id'] == 'C', f'outcomes={ {k: r.outcome for k, r in runs.items()} }')
            c.check(s and all(w in s[0].text for w in ('раз', 'два', 'три')), f'{s and s[0].text}')

    with Case(f'R6 [{ch}] two independent chats: chat 1 reordered B→A, chat 2 one message claimed between them → one reply each') as c:
        with PgSession(SETUP) as pg:
            other = C.replace('3', '4') if ch == 'WA' else C + '9'
            business(pg, ch, chat=other)                              # the second chat is a client's too
            ma, mb, md = msg(ch, 'A', 100), msg(ch, 'B', 101), msg(ch, 'D', 50, 'другой чат', chat=other)
            rb, rd, ra = Run(T, pg, mb, 'e-b'), Run(T, pg, md, 'e-d'), Run(T, pg, ma, 'e-a')
            rb.claim(); rd.claim(); ra.claim()
            walk(rb, [mb, ma]); walk(rd, [md]); walk(ra, [mb, ma])
            c.check([r.outcome for r in (rb, rd, ra)] == ['sent', 'sent', 'superseded'], f'outcomes b/d/a={[r.outcome for r in (rb, rd, ra)]}')

    with Case(f'R6 [{ch}] guard kept: a strictly NEWER message claimed after A\'s Final Fetch refuses A\'s lease; its own run answers') as c:
        with PgSession(SETUP) as pg:
            ma, mn = msg(ch, 'A', 100), msg(ch, 'N', 101)
            ra, rn = Run(T, pg, ma, 'e-a'), Run(T, pg, mn, 'e-n')
            ra.claim()
            ra.latest([ma]); ra.generate(); ra.final([ma])          # A's final snapshot does not hold N yet
            rn.claim()                                                # N arrives now
            ra.lease()
            c.check(ra.lease_row.get('allowed') is False and ra.outcome == 'superseded', f'A lease={ra.lease_row} outcome={ra.outcome}')
            walk(rn, [mn, ma])
            c.check(rn.outcome == 'sent', f'N must answer: {rn.outcome}')

    # V5 (iteration 2026-09-28) — what used to be pinned here as an «accepted residual»: a SAME-second message S,
    # claimed by its own run after A's Final Fetch, was seen by neither freshness signal (the generation counts
    # strictly newer seconds; the snapshot's order only orders rows it holds) and A's lease sent a reply that never
    # saw S — whose own run then stopped on A's echo (a newer fromMe row): S unanswered. Strict '>' in the claim
    # stays (on '>=' equal timestamps lose BOTH replies); the LEASE now refuses a registered same-second peer
    # missing from the snapshot it decides on and asks for ONE re-read; who wins is again Wappi's order, on a
    # snapshot that holds both. The waits in the template (Wait Peer Recheck ≥ debounce + fetch + one PG node) put
    # the peer's own Latest+Combine verdict before the second lease: those orders are what these cases replay.
    def peer_setup(pg, peer_t=100, peer_chat=None, peer_type='chat', peer_body='Сколько стоит букет из 25 роз?'):
        ma = msg(ch, 'A', 100, 'Здравствуйте')
        ms_ = msg(ch, 'S', peer_t, peer_body, chat=peer_chat, mtype=peer_type)
        return ma, ms_, Run(T, pg, ma, 'e-a'), Run(T, pg, ms_, 'e-s')

    with Case(f'V5 [{ch}] a same-second message claimed after A\'s Final Fetch → A re-reads; Wappi lists S first → S answers A+S, A superseded') as c:
        with PgSession(SETUP) as pg:
            ma, ms_, ra, rs = peer_setup(pg)
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])          # A's final snapshot does not hold S
            rs.claim()                                                           # S arrives now (same second)
            ra.lease()
            c.check(ra.lease_row.get('allowed') is False and ra.lease_row.get('peer_unseen') is True and ra.lease_row.get('recheck') is True
                    and getattr(ra, 'recheck_pending', False) and not ra.done,
                    f'A\'s lease must refuse and ask for one re-read: lease={ra.lease_row} outcome={ra.outcome}')
            rs.latest([ms_, ma])                                                  # S's own run passes its Latest+Combine
            if getattr(ra, 'recheck_pending', False):
                ra.final([ms_, ma])                                               # the re-read holds S above A
            c.check(ra.outcome == 'superseded', f'A must stop as superseded: {ra.outcome} lease={ra.lease_row}')
            if not rs.done:
                rs.generate(); rs.final([ms_, ma]); rs.lease()
                if not rs.done:
                    rs.send_ok()
            c.check(rs.outcome == 'sent' and rs.text and 'Здравствуйте' in rs.text and 'букет' in rs.text,
                    f'S must answer A+S: outcome={rs.outcome} text={rs.text!r}')
            c.check(ledger(pg, ch) == {'A': 'superseded', 'S': 'sent'}, f'ledger={ledger(pg, ch)}')
            c.check(ra.cancel and ra.cancel.get('dropped_ai_rows') == 1, f'A\'s draft must leave memory: {ra.cancel}')

    with Case(f'V5 [{ch}] the same race, but the re-read lists A first (Wappi: A is the newer) → exactly one reply, by A; S superseded') as c:
        with PgSession(SETUP) as pg:
            ma, ms_, ra, rs = peer_setup(pg)
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rs.claim()
            ra.lease()
            c.check(getattr(ra, 'recheck_pending', False), f'A must re-read first: lease={ra.lease_row}')
            rs.latest([ma, ms_])                                                  # S's run stops: A is listed above it
            if getattr(ra, 'recheck_pending', False):
                ra.final([ma, ms_]); ra.lease()
            if not ra.done:
                ra.send_ok()
            c.check(ra.outcome == 'sent' and rs.outcome == 'superseded' and getattr(ra, 'leases', 0) == 2,
                    f'a={ra.outcome} s={rs.outcome} leases={getattr(ra, "leases", 0)} ledger={ledger(pg, ch)}')

    with Case(f'V5 [{ch}] the re-read still lacks S while S\'s run is alive → A refused (no second re-read), S answers both') as c:
        with PgSession(SETUP) as pg:
            ma, ms_, ra, rs = peer_setup(pg)
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rs.claim()
            ra.lease()
            rs.latest([ms_, ma])                                                  # S's own fetch holds S
            if getattr(ra, 'recheck_pending', False):
                ra.final([ma]); ra.lease()                                         # A's re-read lags behind S
            c.check(ra.outcome == 'superseded' and ra.lease_row.get('recheck') is False and not getattr(ra, 'recheck_pending', False),
                    f'A must be refused without a second re-read: a={ra.outcome} lease={ra.lease_row}')
            walk(rs, [ms_, ma])
            c.check(rs.outcome == 'sent' and 'Здравствуйте' in (rs.text or ''), f's={rs.outcome} text={rs.text!r}')

    with Case(f'V5 [{ch}] S registered but Wappi never lists it (its run already stopped) → A answers after the re-read (one reply, as before)') as c:
        with PgSession(SETUP) as pg:
            ma, ms_, ra, rs = peer_setup(pg)
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rs.claim()
            ra.lease()
            rs.latest([ma])                                                       # S's own fetch lacks S → it stops
            if getattr(ra, 'recheck_pending', False):
                ra.final([ma]); ra.lease()
            if not ra.done:
                ra.send_ok()
            c.check(ra.outcome == 'sent' and rs.outcome == 'superseded', f'a={ra.outcome} s={rs.outcome} lease={ra.lease_row}')

    with Case(f'V5 [{ch}] an album of 16 photos in one second, the 16th claimed late: a full 15-row page proves nothing → P01 answers') as c:
        with PgSession(SETUP) as pg:
            ps = [msg(ch, 'P%02d' % i, 100, 'https://mock/photo%d.jpg' % i, mtype='image') for i in range(1, 17)]
            runs = [Run(T, pg, m, 'e-p%02d' % i) for i, m in enumerate(ps, 1)]
            page = ps[:15]                                                        # messages/get: limit 15, P16 not in it
            for r in runs[:15]:
                r.claim()
            first = runs[0]
            first.latest(page); first.ask_for_text('Напишите, пожалуйста, текстом.'); first.final(page)
            runs[15].claim()                                                      # the tail registers late
            first.lease()
            c.check(first.lease_row.get('allowed') is True and first.lease_row.get('peer_unseen') is not True,
                    f'an incomplete page must not refuse: lease={first.lease_row} complete={first.named["Final Check"].get("seenComplete")}')
            if not first.done and not getattr(first, 'recheck_pending', False):
                first.send_ok()
            c.check(first.outcome == 'sent', f'P01={first.outcome}')

    with Case(f'V5 [{ch}] a 15-row page that reaches past the trigger\'s second IS complete: the rule applies') as c:
        with PgSession(SETUP) as pg:
            ma, ms_, ra, rs = peer_setup(pg)
            older = [msg(ch, 'O%02d' % i, 50 + i, 'раньше %d' % i) for i in range(14)]
            page = [ma] + older[::-1]                                             # 15 rows, the oldest well before 100
            ra.claim(); ra.latest(page); ra.generate(); ra.final(page)
            rs.claim()
            ra.lease()
            c.check(ra.named['Final Check'].get('seenComplete') is True and ra.lease_row.get('peer_unseen') is True,
                    f'complete={ra.named["Final Check"].get("seenComplete")} lease={ra.lease_row}')

    with Case(f'V5 [{ch}] guards: a late OLDER message, a peer whose run already stopped, another chat\'s same-second message → A sends at once') as c:
        for label, kw, peer_first in (('older O(99)', dict(peer_t=99), False), ('stopped peer', {}, True),
                                      ('other chat', dict(peer_chat=(C.replace('3', '4') if ch == 'WA' else C + '9')), False)):
            with PgSession(SETUP) as pg:
                ma, ms_, ra, rs = peer_setup(pg, **kw)
                ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
                rs.claim()
                if peer_first:
                    rs.latest([ma])                                               # S's run stops before A's lease
                ra.lease()
                c.check(ra.lease_row.get('allowed') is True and ra.lease_row.get('peer_unseen') is False and ra.lease_row.get('recheck') is False,
                        f'{label}: lease={ra.lease_row}')

    with Case(f'V5 [{ch}] a strictly NEWER message stays the generation\'s job (no re-read); a trigger without a time keeps the rule off') as c:
        with PgSession(SETUP) as pg:
            ma, mn, ra, rn = peer_setup(pg, peer_t=101)
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rn.claim(); ra.lease()
            c.check(ra.lease_row.get('allowed') is False and ra.lease_row.get('recheck') is False and ra.outcome == 'superseded',
                    f'newer: lease={ra.lease_row} outcome={ra.outcome}')
        with PgSession(SETUP) as pg:
            ma, mx = msg(ch, 'A', None, 'Здравствуйте'), msg(ch, 'X', None, 'Ещё вопрос')
            ra, rx = Run(T, pg, ma, 'e-a'), Run(T, pg, mx, 'e-x')
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rx.claim(); ra.lease()
            c.check(ra.lease_row.get('peer_unseen') is False and ra.lease_row.get('recheck') is False, f'no time: lease={ra.lease_row}')

    with Case(f'V5 [{ch}] «Вместе» or another run\'s live lease next to an unseen peer → cancelled_mode / cancelled_busy, never a re-read') as c:
        for label, prep, want in (("mode", lambda pg: pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ('{P}', '{C}', true, 1);"), 'cancelled_mode'),
                                  ("busy", lambda pg: pg.x(f"update chat_reply_state set lock_token = 'other-run', lock_expires_at = now() + interval '5 minutes' where profile_id = '{P}' and chat_id = '{C}';"), 'cancelled_busy')):
            with PgSession(SETUP) as pg:
                ma, ms_, ra, rs = peer_setup(pg)
                ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
                rs.claim()
                prep(pg)
                ra.lease()
                c.check(ra.outcome == want and ra.lease_row.get('recheck') is False, f'{label}: outcome={ra.outcome} lease={ra.lease_row}')

    with Case(f'V5 [{ch}] the media branch (no agent run) re-reads the same way and its refusal drops nothing') as c:
        with PgSession(SETUP) as pg:
            ma = msg(ch, 'A', 100, 'https://mock/photo.jpg', mtype='image')
            ms_ = msg(ch, 'S', 100, 'Сколько стоит букет из 25 роз?')
            ra, rs = Run(T, pg, ma, 'e-a'), Run(T, pg, ms_, 'e-s')
            ra.claim(); ra.latest([ma]); ra.ask_for_text('Напишите, пожалуйста, текстом.'); ra.final([ma])
            rs.claim()
            ra.lease()
            c.check(getattr(ra, 'recheck_pending', False), f'media: lease={ra.lease_row}')
            rs.latest([ms_, ma])
            if getattr(ra, 'recheck_pending', False):
                ra.final([ms_, ma])
            c.check(ra.outcome == 'superseded' and ra.cancel and ra.cancel.get('dropped_ai_rows') == 0, f'a={ra.outcome} cancel={ra.cancel}')
            walk(rs, [ms_, ma])
            c.check(rs.outcome == 'sent', f's={rs.outcome}')

    with Case(f'R6 [{ch}] a webhook WITHOUT a time keeps the old rule: its claim always bumps the generation') as c:
        with PgSession(SETUP) as pg:
            g1 = Run(T, pg, msg(ch, 'A', 100), 'e-a').claim().get('generation')
            g2 = Run(T, pg, msg(ch, 'X', None), 'e-x').claim().get('generation')
            g3 = Run(T, pg, msg(ch, 'Y', None), 'e-y').claim().get('generation')
            c.check((g1, g2, g3) == (1, 2, 3), f'generations={(g1, g2, g3)}')

    with Case(f'R6 [{ch}] a chat row written before the fix (no newest time) → reordered B→A still gives one reply by B') as c:
        with PgSession(SETUP) as pg:
            pg.x(f"insert into chat_reply_state (profile_id, chat_id, incoming_generation) values ('{P}', '{C}', 5);")
            ma, mb = msg(ch, 'A', 100), msg(ch, 'B', 101)
            rb, ra = Run(T, pg, mb, 'e-b'), Run(T, pg, ma, 'e-a')
            rb.claim(); ra.claim()
            walk(rb, [mb, ma]); walk(ra, [mb, ma])
            c.check([rb.outcome, ra.outcome] == ['sent', 'superseded'], f'b/a={[rb.outcome, ra.outcome]}')


# ---------------------------------------------------------------------------------------- a WhatsApp chat under its @lid code
# Wappi lists the messages/get rows of some WhatsApp chats under the chat's @lid code — chatId and the client's `from` —
# while the webhook names the chat by its number (the id of the chat list, the decisions and the reply mode) and carries the
# code in whatsapp_chat_id (docs/audits/2026-10-25-personal-chats/live-checks.md). A row is the chat's when it names the chat
# by its id or, on WhatsApp only, by that code; any other id is a crossed answer and stays foreign.
CODE, OTHER_CODE = '100000000000123@lid', '100000000000999@lid'
NOT_A_CODE = (('', 'empty'), ('100000000000123', 'without the @lid suffix'), ('@lid', 'the suffix alone'))
QUEUE = 'Здравствуйте\nСколько стоит букет из 25 роз?'


def lid_msgs(ch, code_):
    """Client messages A (100) and B (101) of channel `ch`'s test chat whose webhooks carry `code_` as whatsapp_chat_id."""
    return (dict(msg(ch, 'A', 100, 'Здравствуйте'), whatsapp_chat_id=code_),
            dict(msg(ch, 'B', 101, 'Сколько стоит букет из 25 роз?'), whatsapp_chat_id=code_))


def listed(rows, under):
    """The messages/get snapshot of `rows` (newest first) as Wappi lists it: under the id `under`, or None — the chat's own id."""
    return [under_code(m, under) if under else {k: v for k, v in m.items() if k != 'whatsapp_chat_id'} for m in rows]


def queue(ch, T, code_, snap_of):
    """A then B claimed, both walked on `snap_of(mb, ma)` → (A's run, B's run, the ledger)."""
    with PgSession(SETUP) as pg:
        ma, mb = lid_msgs(ch, code_)
        ra, rb = Run(T, pg, ma, 'e-a'), Run(T, pg, mb, 'e-b')
        ra.claim(); rb.claim()
        snap = snap_of(mb, ma)
        walk(ra, snap); walk(rb, snap)
        return ra, rb, ledger(pg, ch)


def node_out(T, name, trigger, rows):
    """`name` (Latest+Combine | Final Check) run once on `rows`, as the node of the template runs it after the given trigger."""
    named = {'Webhook': {'body': {'messages': [trigger]}}, 'AI Agent': {'output': 'Ответ'}, 'Retrieve Answer': {'text': 'Ответ'}}
    return code(T, name, {'json': {'status': 'done', 'messages': rows}}, named)[0]['json']


for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    wa = ch == 'WA'

    with Case(f'LID [{ch}] every row listed under the chat\'s @lid code, the webhooks carry it → '
              + ('exactly one reply, by B, answering A and B' if wa else 'Telegram has no code: the rows stay foreign, no reply (as before)')) as c:
        ra, rb, led = queue(ch, T, CODE, lambda mb, ma: listed([mb, ma], CODE))
        s = sent((ra, rb))
        if wa:
            c.check(len(s) == 1 and s[0] is rb and led == {'A': 'superseded', 'B': 'sent'},
                    f'sent={[r.m["id"] for r in s]} outcomes a/b={ra.outcome}/{rb.outcome} ledger={led}')
            c.check(s and 'Здравствуйте' in (s[0].text or '') and 'букет' in (s[0].text or ''), f'the reply must answer A+B: {s and s[0].text}')
        else:
            c.check(not s and led == {'A': 'superseded', 'B': 'superseded'}, f'sent={[r.m["id"] for r in s]} ledger={led}')

    with Case(f'LID [{ch}] Latest+Combine on rows under the code → '
              + ('the trigger is found and the queue combined, nothing foreign' if wa else 'every row foreign: abort (as before)')) as c:
        ma, mb = lid_msgs(ch, CODE)
        out = node_out(T, 'Latest+Combine', mb, listed([mb, ma], CODE))
        got = {k: out.get(k) for k in ('abort', 'combinedText', 'foreignFetched')}
        want = {'abort': False, 'combinedText': QUEUE, 'foreignFetched': 0} if wa else {'abort': True, 'combinedText': None, 'foreignFetched': 2}
        c.check(got == want, f'got {got}, want {want}')

    with Case(f'LID [{ch}] Final Check on rows under the code → '
              + ('the snapshot holds the trigger: ok' if wa else 'no trigger in it: cancelled_fetch (as before)')) as c:
        ma, mb = lid_msgs(ch, CODE)
        out = node_out(T, 'Final Check', mb, listed([mb, ma], CODE))
        got = (out.get('ok'), out.get('outcome'), out.get('seenIds'))
        c.check(got == ((True, None, ['B', 'A']) if wa else (False, 'cancelled_fetch', [])), f'got {got}')

    with Case(f'LID [{ch}] a crossed answer under ANOTHER code → no reply: Latest+Combine aborts (superseded), Final Check '
              'cancelled_fetch, Latest+Combine counts both rows foreign') as c:
        ra, rb, led = queue(ch, T, CODE, lambda mb, ma: listed([mb, ma], OTHER_CODE))
        c.check(not sent((ra, rb)) and led == {'A': 'superseded', 'B': 'superseded'}, f'ledger={led}')
        ma, mb = lid_msgs(ch, CODE)
        lc = node_out(T, 'Latest+Combine', mb, listed([mb, ma], OTHER_CODE))
        fc = node_out(T, 'Final Check', mb, listed([mb, ma], OTHER_CODE))
        c.check(lc.get('abort') is True and lc.get('foreignFetched') == 2 and (fc.get('ok'), fc.get('outcome')) == (False, 'cancelled_fetch'),
                f'Latest+Combine={ {k: lc.get(k) for k in ("abort", "foreignFetched")} } Final Check={(fc.get("ok"), fc.get("outcome"))}')

    with Case(f'LID [{ch}] the chat\'s rows under its code with a NEWER crossed row under another code → '
              + ('the crossed row is dropped, B answers A and B' if wa else 'all foreign, no reply (as before)')) as c:
        crossed = under_code(msg(ch, 'X', 102, 'чужое сообщение'), OTHER_CODE)
        ra, rb, led = queue(ch, T, CODE, lambda mb, ma: [crossed] + listed([mb, ma], CODE))
        s = sent((ra, rb))
        if wa:
            c.check(len(s) == 1 and s[0] is rb and 'чужое' not in (s[0].text or '') and 'букет' in (s[0].text or ''),
                    f'sent={[r.m["id"] for r in s]} text={s and s[0].text!r} ledger={led}')
            c.check(rb.named['Latest+Combine'].get('foreignFetched') == 1, f'foreignFetched={rb.named["Latest+Combine"].get("foreignFetched")}')
        else:
            c.check(not s and led == {'A': 'superseded', 'B': 'superseded'}, f'ledger={led}')

    for value, label in NOT_A_CODE + ((C, 'the chat id itself'),):
        with Case(f'LID [{ch}] whatsapp_chat_id {label} → the rule changes nothing: rows under the chat id are the chat\'s, rows '
                  'under a code (or under that value) stay foreign — in Latest+Combine and in Final Check') as c:
            ma, mb = lid_msgs(ch, value)
            unders = (None, CODE) + ((value,) if value not in ('', C) else ())
            got = {}
            for under in unders:
                lc = node_out(T, 'Latest+Combine', mb, listed([mb, ma], under))
                fc = node_out(T, 'Final Check', mb, listed([mb, ma], under))
                got[under or 'chat'] = (lc.get('abort'), lc.get('foreignFetched'), fc.get('ok'), fc.get('outcome'))
            want = {k: ((False, 0, True, None) if k == 'chat' else (True, 2, False, 'cancelled_fetch')) for k in got}
            c.check(got == want, f'got {got}')
    with Case(f'LID [{ch}] the chain on a whatsapp_chat_id that is no code (the suffix alone), rows under it → no reply, as before') as c:
        ra, rb, led = queue(ch, T, '@lid', lambda mb, ma: listed([mb, ma], '@lid'))
        c.check(not sent((ra, rb)) and led == {'A': 'superseded', 'B': 'superseded'}, f'ledger={led}')

report()
