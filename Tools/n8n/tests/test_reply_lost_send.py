#!/usr/bin/env python3
"""R9 (iteration 2026-09-27): a send whose run died between Acquire Send Lease and Record Send must not stay
`send_pending` forever.

Nothing ever touched such a row: Claim Event re-claims only claimed/generated, the 90-s lease expiry only frees the
chat. The owner's screen showed a neutral «В обработке» for a reply that may never have reached the client.
Contract (decision of the iteration README, R9): the existing 2-minute Handoff Notifier schedule settles a
send_pending older than 10 minutes as send_unknown (an operator-visible «not confirmed»), keeps the attempt's
execution id and time, and NEVER resends — the branch has no transport node, and a send_unknown is never re-claimed.

The ledger rows are made by the REAL Claim Event + Acquire Send Lease of BOTH bot templates (reply_chain.Run); the
sweep is whatever Postgres node(s) the canonical notifier runs off its schedule trigger and that write reply_events
— found through the graph, run with the node's own query and parameters — on session temp tables.

    python3 Tools/n8n/tests/test_reply_lost_send.py
"""
from harness import Case, PgSession, bind, expr, report, wf
from reply_chain import CHATS, SETUP, TEMPLATES, Run, ledger, msg

NTFY = wf('crHandoffNtfy001-Handoff_Notifier.json')
TRIGGER = next(n['name'] for n in NTFY['nodes'] if n['type'] == 'n8n-nodes-base.scheduleTrigger')
BY_NAME = {n['name']: n for n in NTFY['nodes']}


def downstream(name, seen=None):
    seen = set() if seen is None else seen
    for outs in (NTFY['connections'].get(name, {}).get('main') or []):
        for e in outs or []:
            if e['node'] not in seen:
                seen.add(e['node'])
                downstream(e['node'], seen)
    return seen


SWEEPS = [BY_NAME[n] for n in downstream(TRIGGER)
          if BY_NAME[n]['type'] == 'n8n-nodes-base.postgres' and 'reply_events' in BY_NAME[n]['parameters']['query']]


def sweep(pg):
    out = []
    for n in SWEEPS:
        out.append(pg.q(bind(n['parameters']['query'], expr(n['parameters']['options']['queryReplacement']) or [])))
    return out


def age(pg, ch, mid, minutes, chat=None):
    pid, c = CHATS[ch]
    pg.x(f"update reply_events set updated_at = now() - interval '{minutes} minutes' where profile_id = '{pid}' and chat_id = '{chat or c}' and message_id = '{mid}';")


with Case('R9 graph: the notifier\'s schedule settles lost sends in a branch that reaches NO transport node and runs first') as c:
    c.check(len(SWEEPS) == 1, f'expected exactly one reply_events sweep off {TRIGGER!r}, found {[n["name"] for n in SWEEPS]}')
    for n in SWEEPS:
        reach = downstream(n['name'])
        c.check(not any(BY_NAME[x]['type'] == 'n8n-nodes-base.httpRequest' for x in reach), f'{n["name"]} reaches a transport node: {reach}')
        c.check(n['name'] in [e['node'] for e in NTFY['connections'][TRIGGER]['main'][0]], f'{n["name"]} must hang directly off the trigger')
        c.check('message/send' not in n['parameters']['query'] and 'lock_token' not in n['parameters']['query'], 'the sweep must not send or touch the lease')
        # executionOrder v1 runs sibling branches top-to-bottom by canvas position: the sweep must not depend on the
        # notice branch (which calls the transport and can fail) finishing first
        due = BY_NAME['Due Notifications']['position']
        c.check(n['position'][1] < due[1], f'{n["name"]} position {n["position"]} must sit above Due Notifications {due}')
        c.check(n.get('onError') == 'continueRegularOutput', f'{n["name"]} must not fail the notifier run')

for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    OTHER = C.replace('3', '4') if ch == 'WA' else C + '9'

    with Case(f'R9 [{ch}] send_pending older than 10 min → send_unknown (execution id kept); a live one, a stale claim and the live lease untouched') as c:
        with PgSession(SETUP) as pg:
            lost = Run(T, pg, msg(ch, 'LOST', 100), 'exec-dead')
            lost.claim(); lost.latest([lost.m]); lost.generate(); lost.final([lost.m]); lost.lease()     # the run dies here
            live = Run(T, pg, msg(ch, 'LIVE', 100, chat=OTHER), 'exec-live')
            live.claim(); live.latest([live.m]); live.generate(); live.final([live.m]); live.lease()     # still sending
            stale = Run(T, pg, msg(ch, 'STALE', 50), 'exec-stale')
            stale.claim()
            age(pg, ch, 'LOST', 11); age(pg, ch, 'LIVE', 2, chat=OTHER); age(pg, ch, 'STALE', 11)
            first = sweep(pg)
            rows = {r['message_id']: r for r in pg.q("select message_id, state, execution_id, error, updated_at < now() - interval '10 minutes' as kept_time from reply_events")}
            lock = pg.q(f"select lock_token from chat_reply_state where profile_id = '{P}' and chat_id = '{OTHER}'")
            c.check(rows['LOST']['state'] == 'send_unknown', f'lost={rows["LOST"]}')
            c.check(rows['LOST']['execution_id'] == 'exec-dead' and 'run_lost' in (rows['LOST']['error'] or '') and rows['LOST']['kept_time'],
                    f'the attempt must stay traceable (execution id, reason, attempt time): {rows["LOST"]}')
            c.check(rows['LIVE']['state'] == 'send_pending' and rows['STALE']['state'] == 'claimed', f'live={rows["LIVE"]} stale={rows["STALE"]}')
            c.check(lock and lock[0]['lock_token'] == 'exec-live', f'the live lease must not be released: {lock}')
            again = sweep(pg)
            c.check(first and first[0] and first[0][0].get('settled') == 1 and again and again[0][0].get('settled') == 0,
                    f'first sweep={first} second sweep={again}')

    with Case(f'R9 [{ch}] after the sweep: a re-delivery of the lost event is a duplicate (never a second send), a late real result still wins') as c:
        with PgSession(SETUP) as pg:
            lost = Run(T, pg, msg(ch, 'LOST', 100), 'exec-dead')
            lost.claim(); lost.latest([lost.m]); lost.generate(); lost.final([lost.m]); lost.lease()
            age(pg, ch, 'LOST', 11)
            sweep(pg)
            again = Run(T, pg, msg(ch, 'LOST', 100), 'exec-redelivery')
            again.claim()
            c.check(again.outcome == 'duplicate' and ledger(pg, ch).get('LOST') == 'send_unknown', f'redelivery={again.outcome} ledger={ledger(pg, ch)}')
            lost.send_ok('sent-LOST')                                   # the dead run's Record Send, had it survived
            c.check(ledger(pg, ch).get('LOST') == 'sent', f'a real result must overwrite the sweep\'s guess: {ledger(pg, ch)}')

report()
