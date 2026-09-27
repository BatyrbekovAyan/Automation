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

    python3 Tools/n8n/tests/test_reply_order.py
"""
from harness import Case, PgSession, report
from reply_chain import CHATS, SETUP, TEMPLATES, Run, ledger, msg


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

    with Case(f'R6 [{ch}] accepted residual (pinned): a SAME-second message claimed after A\'s Final Fetch does not refuse A\'s lease') as c:
        # strict '>' is deliberate: at an equal time only Wappi's order (seen by Latest+Combine / Final Check, never by
        # the claim) knows which one is newer; bumping on '>=' re-creates the zero-reply case for equal timestamps.
        with PgSession(SETUP) as pg:
            ma, ms_ = msg(ch, 'A', 100), msg(ch, 'S', 100)
            ra, rs = Run(T, pg, ma, 'e-a'), Run(T, pg, ms_, 'e-s')
            ra.claim(); ra.latest([ma]); ra.generate(); ra.final([ma])
            rs.claim()
            ra.lease()
            c.check(ra.lease_row.get('allowed') is True, f'A lease={ra.lease_row}')

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

report()
