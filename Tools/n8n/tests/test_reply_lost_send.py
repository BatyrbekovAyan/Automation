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

T7 (follow-ups 2026-10-11, F1F2-plan §3 T7): the same sweep also settles the dead run's memory DRAFT — the row Stamp
Draft tied to the run stays `delivery: draft` forever when the run dies, and the model then reads it as an ordinary
reply of its own. Reached the lease (its event send_unknown) → delivery unknown + «[доставка не подтверждена] »; died
before the lease (claim > 10 min old, or the event re-claimed by another execution and the draft > 10 min old) → the
row goes. Only on a positive fact from reply_events. Plus M3 (sql/2026-10-12-memory-marks-backfill.sql) — the marks for
rows written before T6.

    python3 Tools/n8n/tests/test_reply_lost_send.py
"""
import json

from harness import N8N, Case, PgSession, bind, expr, module, report, wf
from reply_chain import CHATS, SETUP, TEMPLATES, Run, history, ledger, memory, msg, record_outgoing

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


# ------------------------------------------------------------------------------------------------------------------
# T7 (follow-ups 2026-10-11, F1F2-plan §3 T7): a memory DRAFT (Stamp Draft's delivery 'draft') whose run died must not
# stay in the bot's memory as an ordinary reply of its own. The same sweep node, the same tick:
#   * the run reached the lease and was lost (R9 settles its event send_unknown — this tick or an earlier one) →
#     the row becomes delivery 'unknown' with the undelivered mark (the transport may have taken it);
#   * the run died BEFORE the lease (its claim is claimed/generated for > 10 min, or another run re-claimed the event
#     and the draft itself is > 10 min old) → the row goes (the client never saw it: Record Cancel's meaning).
# Only on a POSITIVE fact from reply_events (state + the same execution id + age; a re-claim names another execution
# and is itself proof the draft's run held the claim > 10 min); no event row → nothing. A live run's draft, a sent or
# already-unknown row, another execution's draft, owner rows and client rows are never touched.
OWNER = 'Владелец: '
UNDELIVERED = '[доставка не подтверждена] '
TEXT = 'Букет из 25 роз — 25 000 ₸.'


def mem_rows(pg, ch):
    return [(r['type'], r['source'], r['content'], r['execution_id'], r['delivery']) for r in memory(pg, ch)]


def age_draft(pg, ch, exec_id, minutes):
    pid, c = CHATS[ch]
    pg.x(f"update n8n_chat_histories set created_at = now() - interval '{minutes} minutes' where session_id = '{pid}:{c}' "
         f"and message->'additional_kwargs'->>'execution_id' = '{exec_id}';")


with Case('T7 graph: the dead drafts are settled by the SAME R9 node (one node per tick, no new branch, no params)') as c:
    mem_nodes = [n['name'] for n in NTFY['nodes'] if n['type'] == 'n8n-nodes-base.postgres' and 'n8n_chat_histories' in n['parameters']['query']]
    c.check(mem_nodes == [s['name'] for s in SWEEPS], f'memory is touched by {mem_nodes}, the R9 sweep is {[s["name"] for s in SWEEPS]}')
    for n in SWEEPS:
        c.check(n['parameters']['options'].get('queryReplacement') == '={{ [] }}', f'{n["name"]} params: {n["parameters"]["options"]}')

for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    OTHER = C.replace('3', '4') if ch == 'WA' else C + '9'

    with Case(f'T7 [{ch}] the draft of a run R9 settles (lost after the lease) becomes delivery unknown + the mark, stamped by its run') as c:
        with PgSession(SETUP) as pg:
            lost = Run(T, pg, msg(ch, 'LOST', 100, 'Сколько стоит букет?'), 'exec-dead')
            lost.claim(); lost.latest([lost.m]); lost.generate(TEXT); lost.final([lost.m]); lost.lease()     # the run dies here
            age(pg, ch, 'LOST', 11)
            out = sweep(pg)
            rows = mem_rows(pg, ch)
            c.check(ledger(pg, ch).get('LOST') == 'send_unknown', f'ledger={ledger(pg, ch)}')
            c.check(('ai', None, UNDELIVERED + TEXT, 'exec-dead', 'unknown') in rows, f'memory={rows}')
            c.check(('human', None, 'Сколько стоит букет?', None, None) in rows, f'the client row must stay: {rows}')
            again = sweep(pg)
            c.check(mem_rows(pg, ch) == rows, f'a second tick must change nothing: {mem_rows(pg, ch)}')
            c.check(out and out[0] and out[0][0].get('settled') == 1 and again[0][0].get('settled') == 0, f'first={out} again={again}')
            lost.send_ok('sent-LOST')                     # the dead run's Record Send after all: the real result wins, mark off
            c.check(('ai', None, TEXT, 'exec-dead', 'sent') in mem_rows(pg, ch) and ledger(pg, ch).get('LOST') == 'sent',
                    f'a late real «sent» must take the mark off: {mem_rows(pg, ch)} ledger={ledger(pg, ch)}')

    with Case(f'T7 [{ch}] a draft whose event an EARLIER tick already settled send_unknown is marked on the next tick') as c:
        with PgSession(SETUP) as pg:
            lost = Run(T, pg, msg(ch, 'OLD', 100), 'exec-old')
            lost.claim(); lost.latest([lost.m]); lost.generate(TEXT); lost.final([lost.m]); lost.lease()
            pg.x(f"update reply_events set state = 'send_unknown', error = 'run_lost: before T7' where profile_id = '{P}' and chat_id = '{C}' and message_id = 'OLD';")
            sweep(pg)
            c.check(('ai', None, UNDELIVERED + TEXT, 'exec-old', 'unknown') in mem_rows(pg, ch), f'memory={mem_rows(pg, ch)}')

    with Case(f'T7 [{ch}] the draft of a run that died BEFORE the lease (claimed 11 min ago) is removed; the client row stays') as c:
        with PgSession(SETUP) as pg:
            dead = Run(T, pg, msg(ch, 'DEAD', 100, 'А с доставкой?'), 'exec-dead')
            dead.claim(); dead.latest([dead.m]); dead.generate(TEXT)                                         # dies before Final Check
            age(pg, ch, 'DEAD', 11)
            sweep(pg)
            rows = mem_rows(pg, ch)
            c.check(not any(r[0] == 'ai' for r in rows) and ('human', None, 'А с доставкой?', None, None) in rows, f'memory={rows}')
            c.check(ledger(pg, ch).get('DEAD') == 'claimed', f'the event itself is never touched here: {ledger(pg, ch)}')

    with Case(f'T7 [{ch}] the draft of a run whose event ANOTHER run re-claimed is removed once it is old; the re-claimer\'s live draft stays') as c:
        with PgSession(SETUP) as pg:
            x = Run(T, pg, msg(ch, 'RC', 100), 'exec-x')
            x.claim(); x.latest([x.m]); x.generate(TEXT)
            age(pg, ch, 'RC', 11); age_draft(pg, ch, 'exec-x', 11)
            y = Run(T, pg, msg(ch, 'RC', 100), 'exec-y')                                                    # Wappi re-delivers the event
            y.claim(); y.latest([y.m]); y.generate(TEXT + ' Доставка — 1000 ₸.')
            c.check(y.outcome is None and y.claim_row.get('claimed') is True, f'the re-delivery must re-claim the stale event: {y.claim_row}')
            sweep(pg)
            ai = [r for r in mem_rows(pg, ch) if r[0] == 'ai']
            c.check(ai == [('ai', None, TEXT + ' Доставка — 1000 ₸.', 'exec-y', 'draft')], f'memory ai rows={ai}')

    with Case(f'T7 [{ch}] negatives: a live run\'s draft, a sent row, an unknown row, another execution\'s fresh draft, owner rows — untouched') as c:
        with PgSession(SETUP) as pg:
            sent = Run(T, pg, msg(ch, 'S', 100, 'Цена?'), 'exec-s')
            sent.claim(); sent.latest([sent.m]); sent.generate('Цена — 25 000 ₸.'); sent.final([sent.m]); sent.lease(); sent.send_ok('sent-S')
            unk = Run(T, pg, msg(ch, 'U', 110, 'Есть доставка?', chat=OTHER), 'exec-u')
            unk.claim(); unk.latest([unk.m]); unk.generate('Доставка есть.'); unk.final([unk.m]); unk.lease()
            unk.send({'error': {'message': 'socket hang up'}}, reconcile_snapshot=[unk.m])
            record_outgoing(T, pg, ch, 'own-1', 'Ждём вас в 15:00.', 120, wh_type='outgoing_message_phone')
            live = Run(T, pg, msg(ch, 'L', 130, 'А завтра?'), 'exec-live')
            live.claim(); live.latest([live.m]); live.generate('Завтра тоже работаем.')                     # a live run, 2 min in
            age(pg, ch, 'L', 2)
            # another execution's FRESH draft under a re-claimed event (its run may still be finishing): untouched
            other = Run(T, pg, msg(ch, 'RC', 140), 'exec-a')
            other.claim(); other.latest([other.m])
            pg.x(f"update reply_events set execution_id = 'exec-b' where profile_id = '{P}' and chat_id = '{C}' and message_id = 'RC';")
            other.generate('Свежий черновик.')
            # a send_pending row past 10 min whose draft belongs to ANOTHER execution: never marked unknown
            p2 = Run(T, pg, msg(ch, 'P2', 150), 'exec-p2')
            p2.claim(); p2.latest([p2.m]); p2.generate('Чужой черновик.'); p2.final([p2.m]); p2.lease()
            pg.x(f"update reply_events set execution_id = 'exec-someone-else' where profile_id = '{P}' and chat_id = '{C}' and message_id = 'P2';")
            age(pg, ch, 'P2', 11)
            before = sorted(mem_rows(pg, ch), key=str)
            hist_before = history(pg, ch)
            sweep(pg)
            after = sorted(mem_rows(pg, ch), key=str)
            changed = [r for r in after if r not in before] + [r for r in before if r not in after]
            c.check(changed == [], f'rows changed: {changed}')
            c.check(history(pg, ch) == hist_before, 'the one history must never change here')
            c.check(ledger(pg, ch).get('P2') == 'send_unknown', f'the lost send itself is still settled: {ledger(pg, ch)}')

    with Case(f'T7 [{ch}] no event row → nothing: a draft whose event was deleted (or never recorded) stays as it is') as c:
        with PgSession(SETUP) as pg:
            d = Run(T, pg, msg(ch, 'NE', 100), 'exec-ne')
            d.claim(); d.latest([d.m]); d.generate(TEXT)
            pg.x(f"delete from reply_events where profile_id = '{P}' and chat_id = '{C}' and message_id = 'NE';")
            age_draft(pg, ch, 'exec-ne', 30)
            before = mem_rows(pg, ch)
            sweep(pg)
            c.check(mem_rows(pg, ch) == before, f'memory={mem_rows(pg, ch)}')

# ------------------------------------------------------------------------------------------------------------------
# M3 (the backfill of the marks for rows written before T6): one owner mark per owner row, one undelivered mark per
# unknown row, idempotent, nothing else touched; the strings are apply-history-handoff.py's constants.
M3 = (N8N / 'sql' / '2026-10-12-memory-marks-backfill.sql')
with Case('T7 M3: the backfill marks old owner / unknown rows once, is idempotent, touches nothing else; same strings as the writers') as c:
    text = M3.read_text(encoding='utf-8') if M3.exists() else ''
    c.check(bool(text), f'{M3.name} is missing')
    hh = module('apply-history-handoff.py')
    c.check(getattr(hh, 'OWNER_MEMORY_PREFIX', None) == OWNER and getattr(hh, 'UNDELIVERED_MEMORY_PREFIX', None) == UNDELIVERED,
            'the backfill strings must be the writers\' constants')
    block = text.split('-- BACKFILL-BEGIN')[1].split('-- BACKFILL-END')[0].replace('public.', '') if '-- BACKFILL-BEGIN' in text else ''
    c.check(f"'{OWNER}'" in block and f"'{UNDELIVERED}'" in block and f'left(coalesce(message->>\'content\', \'\'), {len(OWNER)})' in block
            and f'left(coalesce(message->>\'content\', \'\'), {len(UNDELIVERED)})' in block, 'the backfill must use the writers\' strings and their lengths')
    if block:
        sid = 'prof-m3:77001112233@c.us'
        rows = [
            ('o1', {'type': 'ai', 'content': 'Ждём вас в 15:00.', 'additional_kwargs': {'source': 'owner_phone', 'message_id': 'o1'}}),
            ('o2', {'type': 'ai', 'content': 'Спасибо!', 'additional_kwargs': {'source': 'owner_app', 'message_id': 'o2'}}),
            ('o3', {'type': 'ai', 'content': OWNER + 'Уже помечено.', 'additional_kwargs': {'source': 'owner_app', 'message_id': 'o3'}}),
            ('u1', {'type': 'ai', 'content': 'Фильтр — 3200 тг.', 'additional_kwargs': {'execution_id': 'e1', 'delivery': 'unknown'}}),
            ('u2', {'type': 'ai', 'content': UNDELIVERED + 'Уже помечено.', 'additional_kwargs': {'execution_id': 'e2', 'delivery': 'unknown'}}),
            ('s1', {'type': 'ai', 'content': 'Отправлено.', 'additional_kwargs': {'execution_id': 'e3', 'delivery': 'sent'}}),
            ('d1', {'type': 'ai', 'content': 'Черновик.', 'additional_kwargs': {'execution_id': 'e4', 'delivery': 'draft'}}),
            ('n1', {'type': 'ai', 'content': 'Старая строка бота.', 'additional_kwargs': {}}),
            ('h1', {'type': 'human', 'content': 'Клиент.', 'additional_kwargs': {'source': 'client', 'message_id': 'h1'}}),
            ('h2', {'type': 'human', 'content': 'Владелец пишет?', 'additional_kwargs': {}}),
        ]
        ins = '\n'.join(f"insert into n8n_chat_histories (session_id, message) values ('{sid}', '{json.dumps(dict(m, response_metadata={}), ensure_ascii=False)}'::jsonb);"
                        for _, m in rows)
        with PgSession(SETUP) as pg:
            pg.x(ins)
            pg.x(block)
            first = [r['c'] for r in pg.q(f"select message->>'content' as c from n8n_chat_histories where session_id = '{sid}' order by id")]
            pg.x(block)
            second = [r['c'] for r in pg.q(f"select message->>'content' as c from n8n_chat_histories where session_id = '{sid}' order by id")]
        want = [OWNER + 'Ждём вас в 15:00.', OWNER + 'Спасибо!', OWNER + 'Уже помечено.', UNDELIVERED + 'Фильтр — 3200 тг.',
                UNDELIVERED + 'Уже помечено.', 'Отправлено.', 'Черновик.', 'Старая строка бота.', 'Клиент.', 'Владелец пишет?']
        c.check(first == want, f'after the backfill: {first}')
        c.check(second == first, f'a second run must change nothing: {second}')

# ------------------------------------------------------------------------------------------------------------------
# The structural tripwire (verify-history-handoff.check_lost_send_sweep) holds the T7 rules when nobody runs these
# scenarios: it accepts the canonical sweep and rejects every loosened variant — and the behaviour above catches the
# same variants (a tripwire the scenarios would not also catch pins something unproven).
vhh = module('verify-history-handoff.py')
CANON = SWEEPS[0]['parameters']['query'] if SWEEPS else ''
LOOSE = {
    'the sweep makes an event re-claimable': ("set state = 'send_unknown'", "set state = 'claimed'"),
    'unknown mark without the same execution': (" and d.event_state = 'send_unknown' and d.event_run_id = d.run_id", " and d.event_state = 'send_unknown'"),
    'a dead claim dropped without its age': (" and d.event_state in ('claimed', 'generated') and d.event_at < now() - interval '10 minutes')",
                                             " and d.event_state in ('claimed', 'generated'))"),
    'a re-claimed draft dropped while fresh': (" and d.created_at < now() - interval '10 minutes'", ''),
    'drafts of any delivery': ("\n     and h.message->'additional_kwargs'->>'delivery' = 'draft'", ''),
    'owner rows included': ("\n     and (h.message->'additional_kwargs'->>'source') is null", ''),
}
with Case('T7 tripwire: verify-history-handoff accepts the canonical sweep and rejects every loosened variant') as c:
    try:
        vhh.check_lost_send_sweep(CANON)
    except AttributeError:
        c.check(False, 'verify-history-handoff.py has no check_lost_send_sweep (the T7 rules are not pinned)')
    except AssertionError as e:
        c.check(False, f'canonical sweep rejected: {e}')
    for label, (old, new) in LOOSE.items():
        c.check(old in CANON, f'{label}: the canonical sweep lacks {old!r} (update the mutation)')
        bad = CANON.replace(old, new)
        try:
            vhh.check_lost_send_sweep(bad)
            c.check(False, f'{label}: the tripwire accepted it')
        except AssertionError:
            pass
        except AttributeError:
            pass


def sweep_with(pg, query):
    return [pg.q(bind(query, []))]


with Case('T7 tripwire cross-check: the scenarios catch the loosened sweeps too (WA)') as c:
    T = TEMPLATES['WA']
    P, C = CHATS['WA']
    for label in ('a dead claim dropped without its age', 'a re-claimed draft dropped while fresh', 'unknown mark without the same execution'):
        old, new = LOOSE[label]
        bad = CANON.replace(old, new)
        with PgSession(SETUP) as pg:
            if label == 'a dead claim dropped without its age':
                live = Run(T, pg, msg('WA', 'L', 130, 'А завтра?'), 'exec-live')
                live.claim(); live.latest([live.m]); live.generate('Завтра тоже работаем.')
                sweep_with(pg, bad)
                seen = not any(r[2] == 'Завтра тоже работаем.' for r in mem_rows(pg, 'WA'))
            elif label == 'a re-claimed draft dropped while fresh':
                o = Run(T, pg, msg('WA', 'RC', 140), 'exec-a')
                o.claim(); o.latest([o.m])
                pg.x(f"update reply_events set execution_id = 'exec-b' where profile_id = '{P}' and chat_id = '{C}' and message_id = 'RC';")
                o.generate('Свежий черновик.')
                sweep_with(pg, bad)
                seen = not any(r[2] == 'Свежий черновик.' for r in mem_rows(pg, 'WA'))
            else:
                p2 = Run(T, pg, msg('WA', 'P2', 150), 'exec-p2')
                p2.claim(); p2.latest([p2.m]); p2.generate('Чужой черновик.'); p2.final([p2.m]); p2.lease()
                pg.x(f"update reply_events set execution_id = 'exec-someone-else' where profile_id = '{P}' and chat_id = '{C}' and message_id = 'P2';")
                age(pg, 'WA', 'P2', 11)
                sweep_with(pg, bad)
                seen = any(r[2] == UNDELIVERED + 'Чужой черновик.' for r in mem_rows(pg, 'WA'))
        c.check(seen, f'{label}: the scenario did not catch it')

report()
