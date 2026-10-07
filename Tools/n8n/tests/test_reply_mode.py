#!/usr/bin/env python3
"""R4 (iteration 2026-09-27): a stale or replayed «Авто» must never lift a NEWER handoff pause or close a request
created after the owner's decision.

Replays Set Reply Mode exactly as the canonical graph runs it — the REAL Validate Code body, the REAL
queryReplacement expression (n8n's own engine) and the REAL `Upsert Reply Mode` SQL — against session temp tables in
the local staging Postgres; handoff requests are created by the REAL `Upsert Handoff` SQL of Handoff Request. Every
scenario runs for a WhatsApp-shaped and a Telegram-shaped chat (Set Reply Mode is the one shared path).

Contract (decision of the iteration README, R4):
  * only an APPLIED chat-scoped «Авто» resolves handoffs — a stale (older) or replayed (same) version has NO side
    effect on the pause or on the requests;
  * an applied «Авто» closes only requests created no later than the command's version (the client's ms clock) and
    keeps the chat paused while a request created after the decision is still open;
  * a bot-wide '*' write never lifts a chat's pause;
  * the owner's new explicit «Авто» still resumes the chat (legit path).

R4-clock (iteration 2026-09-28): the version is the phone's wall clock (max(now, last+1)) and was ALSO the boundary of
what an «Авто» closes — a phone running fast closed a request created AFTER a decision that waited in the offline
queue. Reachable form (refuter): the server is effectively «Авто» and unpaused in the gap (no row / an older «Авто»
row; the owner's undelivered «Вместе» was overtaken by «Авто» in the queue), the AI creates a request, the queued
«Авто» lands. Contract now:
  * the version ONLY orders (unchanged guard, replay, adopt-server, floor version);
  * a client that dates its decision sends `decisionAgeMs` (measured as the request leaves; two readings of one
    clock) and the boundary is the SERVER's now − age — a queued fast-clock «Авто» leaves the newer request open, an
    immediate «Авто» on a fast OR slow clock closes the requests that exist;
  * no field (legacy and pre-R4-clock clients) or a malformed / negative one → the version dates the decision,
    byte-for-byte the previous behaviour; a floor-version probe is never sent with an age;
  * documented residual: the age excludes the request's own one-way latency, so a request created within that
    latency after the real tap is still closed (pinned below, so the limit stays visible).

    python3 Tools/n8n/tests/test_reply_mode.py
"""
import json
import time

from harness import Case, PgSession, bind, code, expr, module, node, report, wf

SRM = wf('SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json')
HREQ = wf('crHandoffReq0001-Handoff_Request.json')

SETUP = """create temp table reply_mode_flags (like public.reply_mode_flags including all);
create temp table chat_reply_state (like public.chat_reply_state including all);
create temp table handoff_requests (like public.handoff_requests including all);"""

MIN = 60_000
CHATS = {'WA': ('wa-prof-r4', '77001112233@c.us'), 'TG': ('tg-prof-r4', '5550001112')}


ABSENT = object()


def set_reply_mode(pg, pid, chat, suppressed, version, age=ABSENT, pids=None):
    """One Set Reply Mode call past the auth gate: Validate → queryReplacement → Upsert. Returns the per-profile row
    (all rows when `pids` names several profiles). `age` = the body's decisionAgeMs (R4-clock), absent by default."""
    body = {'profileIds': pids or [pid], 'chatId': chat, 'suppressed': suppressed, 'version': version}
    if age is not ABSENT:
        body['decisionAgeMs'] = age
    v = code(SRM, 'Validate', {'json': {'body': body}})[0]['json']
    assert v.get('invalid') is False, v
    up = node(SRM, 'Upsert Reply Mode')
    params = expr(up['parameters']['options']['queryReplacement'], v)
    rows = pg.q(bind(up['parameters']['query'], params))
    return rows if pids else rows[0]


def handoff(pg, pid, chat, msg_id, created_ms):
    """The AI's create_handoff (Upsert Handoff), then the request's creation time pinned to `created_ms`."""
    up = node(HREQ, 'Upsert Handoff')
    pg.q(bind(up['parameters']['query'], [pid, chat, msg_id, 'order', json.dumps({}), 'synthetic']))
    pg.x(f"update handoff_requests set created_at = to_timestamp({created_ms} / 1000.0) where source_message_id = '{msg_id}';")


def mode_row(pg, pid, chat, suppressed, version):
    pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ('{pid}', '{chat}', {str(suppressed).lower()}, {version}) "
         f"on conflict (profile_id, chat_id) do update set suppressed = excluded.suppressed, version = excluded.version;")


def state(pg, pid, chat):
    paused = pg.q(f"select handoff_paused from chat_reply_state where profile_id = '{pid}' and chat_id = '{chat}'")
    reqs = pg.q(f"select source_message_id, status from handoff_requests where profile_id = '{pid}' and chat_id = '{chat}' order by id")
    return (paused[0]['handoff_paused'] if paused else False), {r['source_message_id']: r['status'] for r in reqs}


for ch, (P, C) in CHATS.items():
    NOW = int(time.time() * 1000)

    with Case(f'R4 [{ch}] A: a delayed OLDER «Авто» (v100 after v200) changes nothing — pause and the request stay') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW - 10 * MIN)                      # the server already holds v200
            handoff(pg, P, C, 'A1', NOW - 5 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - 20 * MIN)          # v100 arrives late
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is False, f'a stale version must not apply: {row}')
            c.check(paused is True and reqs == {'A1': 'open'}, f'stale «Авто» had side effects: paused={paused} {reqs}')

    with Case(f'R4 [{ch}] A2: a stale «Авто» newer than the request but older than the server\'s «Вместе» closes nothing') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, True, NOW)
            handoff(pg, P, C, 'A2', NOW - 5 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - MIN)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is False and row['suppressed'] is True, f'the server\'s newer «Вместе» must be reported: {row}')
            c.check(paused is True and reqs == {'A2': 'open'}, f'paused={paused} {reqs}')

    with Case(f'R4 [{ch}] B: a replay of the SAME applied version after a NEW handoff keeps the new request open and paused') as c:
        with PgSession(SETUP) as pg:
            first = set_reply_mode(pg, P, C, False, NOW - 10 * MIN)        # the owner's «Авто», applied; the answer got lost
            handoff(pg, P, C, 'B1', NOW - 2 * MIN)                         # the client asks for a human again
            row = set_reply_mode(pg, P, C, False, NOW - 10 * MIN)          # the queue re-sends the same version
            paused, reqs = state(pg, P, C)
            c.check(first['applied'] is True and row['applied'] is False, f'first={first} replay={row}')
            c.check(paused is True and reqs == {'B1': 'open'}, f'the replay resolved a newer request: paused={paused} {reqs}')

    with Case(f'R4 [{ch}] C: an «Авто» decided BEFORE the request (queued offline, delivered after it) leaves the request open') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, True, NOW - 60 * MIN)
            handoff(pg, P, C, 'C1', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - 10 * MIN)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True, f'the desire itself is newer than the stored row and applies: {row}')
            c.check(paused is True and reqs == {'C1': 'open'}, f'a decision older than the request resolved it: paused={paused} {reqs}')

    with Case(f'R4 [{ch}] D: the owner\'s NEW explicit «Авто» resumes the chat (closes the request, lifts the pause)') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW - 60 * MIN)
            handoff(pg, P, C, 'D1', NOW - 5 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is False and reqs == {'D1': 'done'}, f'legit resume broken: {row} paused={paused} {reqs}')

    with Case(f'R4 [{ch}] E: a legit «Авто» resolves the older request but a request created AFTER the tap keeps the chat paused') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW - 60 * MIN)
            handoff(pg, P, C, 'E1', NOW - 5 * MIN)
            handoff(pg, P, C, 'E2', NOW - MIN // 2)
            row = set_reply_mode(pg, P, C, False, NOW - MIN)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs == {'E1': 'done', 'E2': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4 [{ch}] F: a bot-wide \'*\' «Авто» never lifts a chat\'s pause nor closes its request') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'F1', NOW - 5 * MIN)
            row = set_reply_mode(pg, P, '*', False, NOW)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs == {'F1': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4 [{ch}] G: a heal that repeats the ORIGINAL decision version on chat open closes nothing newer') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW - 1440 * MIN)                     # the owner resumed this chat a day ago
            handoff(pg, P, C, 'G1', NOW - MIN)                               # the AI hands the chat over again
            row = set_reply_mode(pg, P, C, False, NOW - 1440 * MIN)          # chat open re-asserts the SAME version
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is False and paused is True and reqs == {'G1': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4 [{ch}] H: the client\'s floor-version «Авто» (no row on the server) writes the row but resolves no request') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'H1', NOW - MIN)
            row = set_reply_mode(pg, P, C, False, 1)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs == {'H1': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4 [{ch}] I: a 16-digit version (Validate\'s maximum) neither overflows nor fails') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'I1', NOW - MIN)
            row = set_reply_mode(pg, P, C, False, 9999999999999999)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is False and reqs == {'I1': 'done'}, f'{row} paused={paused} {reqs}')

# ---- R4-clock (iteration 2026-09-28): the decision is dated on the SERVER's clock from the client's decision age ----
CLOCK_CHATS = {'WA': (('wa-prof-r4c', 'wa-prof-r4c2'), '77001112244@c.us'), 'TG': (('tg-prof-r4c', 'tg-prof-r4c2'), '5550001144')}
for ch, ((P, P2), C) in CLOCK_CHATS.items():
    NOW = int(time.time() * 1000)
    FAST = 5 * MIN                                     # the phone's clock runs 5 min ahead

    with Case(f'R4-clock [{ch}] J: a fast-clock «Авто» decided BEFORE the request and delivered after it leaves the request open') as c:
        with PgSession(SETUP) as pg:
            # the reachable path: the server holds an older «Авто» (no pause) in the gap — the owner's «Вместе» never
            # reached it — so the AI hands over at NOW−2m; the owner's tap at real NOW−4m waited in the offline queue
            mode_row(pg, P, C, False, NOW - 60 * MIN)
            handoff(pg, P, C, 'J1', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST, age=4 * MIN)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and row['version'] == NOW - 4 * MIN + FAST, f'the desire still orders by its version: {row}')
            c.check(paused is True and reqs == {'J1': 'open'}, f'a decision older than the request resolved it: paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] J2: same, with NO row on the server (absence = «Авто»)') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'J2', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST, age=4 * MIN)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs == {'J2': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] K: the reviewer\'s literal repro — an IMMEDIATE «Авто» on a fast clock closes the existing request (legit)') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'K1', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW + FAST, age=0)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is False and reqs == {'K1': 'done'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] L: an immediate «Авто» on a SLOW clock closes the existing request too (it used to stay paused)') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW - 60 * MIN)
            handoff(pg, P, C, 'L1', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - FAST, age=0)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is False and reqs == {'L1': 'done'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] M: a replay of J\'s version with a fresh age after a NEWER request changes nothing') as c:
        with PgSession(SETUP) as pg:
            first = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST, age=0)
            handoff(pg, P, C, 'M1', NOW - MIN)
            row = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST, age=0)
            paused, reqs = state(pg, P, C)
            c.check(first['applied'] is True and row['applied'] is False, f'first={first} replay={row}')
            c.check(paused is True and reqs == {'M1': 'open'}, f'a replay resolved a newer request: paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] B6: a clock fixed after a fast tap — the version is last+1 (minutes ahead), the label is the real tap') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW + 2 * MIN)                         # the previous tap, stamped by the fast clock
            handoff(pg, P, C, 'B61', NOW - MIN)
            row = set_reply_mode(pg, P, C, False, NOW + 2 * MIN + 1, age=2 * MIN)   # tapped at real NOW−2m, delivered now
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs == {'B61': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] N: the floor-version probe (version 1, never sent with an age) resolves nothing') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'N1', NOW - MIN)
            row = set_reply_mode(pg, P, C, False, 1)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs == {'N1': 'open'}, f'{row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] O: the current client (version, NO decisionAgeMs) keeps the previous behaviour byte-for-byte') as c:
        with PgSession(SETUP) as pg:
            mode_row(pg, P, C, False, NOW - 60 * MIN)
            handoff(pg, P, C, 'O1', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is False and reqs == {'O1': 'done'},
                    f'compat: without the field the version dates the decision, as before: {row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] P: one request for two profiles on a fast clock — both applied, both newer requests stay open') as c:
        with PgSession(SETUP) as pg:
            handoff(pg, P, C, 'P1', NOW - 2 * MIN)
            handoff(pg, P2, C, 'P2', NOW - 2 * MIN)
            rows = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST, age=4 * MIN, pids=[P, P2])
            (pa, ra), (pb, rb) = state(pg, P, C), state(pg, P2, C)
            c.check(len(rows) == 2 and all(r['applied'] is True for r in rows), f'rows={rows}')
            c.check(pa is True and pb is True and ra == {'P1': 'open'} and rb == {'P2': 'open'}, f'{pa} {ra} / {pb} {rb}')

    with Case(f'R4-clock [{ch}] Q: a malformed or negative age is ignored (the version decides), a huge age closes nothing') as c:
        with PgSession(SETUP) as pg:
            for i, bad in enumerate((-60_000, '4m', 1.5, None, True)):
                handoff(pg, P, C, f'Q{i}', NOW - 2 * MIN)
                row = set_reply_mode(pg, P, C, False, NOW - 4 * MIN + FAST + i, age=bad)
                paused, reqs = state(pg, P, C)
                c.check(row['applied'] is True and reqs.get(f'Q{i}') == 'done',
                        f'age={bad!r}: must fall back to the version (closes as before): {row} {reqs}')
            handoff(pg, P, C, 'Qhuge', NOW - 2 * MIN)
            row = set_reply_mode(pg, P, C, False, NOW + 60 * MIN, age='9999999999999999')
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and paused is True and reqs.get('Qhuge') == 'open', f'{row} paused={paused} {reqs}')

    with Case(f'R4-clock [{ch}] R (documented residual): a request created within the delivery latency after the real tap is still closed') as c:
        with PgSession(SETUP) as pg:
            # the age is measured when the request leaves the phone; the server reads its clock a one-way latency later
            # (1.5 s here), so the decision is dated that much late — a request created 1 s after the real tap closes
            tap = NOW - 4 * MIN
            handoff(pg, P, C, 'R1', tap + 1_000)
            row = set_reply_mode(pg, P, C, False, tap + FAST, age=(int(time.time() * 1000) - tap) - 1_500)
            paused, reqs = state(pg, P, C)
            c.check(row['applied'] is True and reqs == {'R1': 'done'}, f'the residual changed — re-document it: {row} {reqs}')

# The structural tripwire in verify-reply-mode.py must reject the pre-R4 shape (side effects keyed off `req`), so a
# later "simplification" back to it cannot pass the verifier even when nobody runs this suite.
PRE_R4_SIDE_EFFECTS = """), unpause as (
  update chat_reply_state c set handoff_paused = false
  from req where c.profile_id = req.profile_id and c.chat_id = req.chat_id and req.chat_id <> '*' and req.suppressed = false
  returning 1
), closed as (
  update handoff_requests h set status = 'done', done_at = now(), updated_at = now()
  from req where h.profile_id = req.profile_id and h.chat_id = req.chat_id and req.chat_id <> '*' and req.suppressed = false and h.status <> 'done'
  returning 1
)
select r.profile_id as "profileId","""

with Case('R4 verifier: the structural tripwire rejects the pre-R4 side effects, the version-dated boundary, and accepts the canonical SQL') as c:
    vrm = module('verify-reply-mode.py')
    canon = vrm.sql(node(SRM, 'Upsert Reply Mode')['parameters']['query'])
    head, tail = canon.split('), resumed as (', 1)
    old = head + PRE_R4_SIDE_EFFECTS + tail.split('select r.profile_id as "profileId",', 1)[1]
    half = canon.replace("  from resumed r\n  where h.profile_id", "  from req r\n  where h.profile_id")
    try:
        vrm.check_resume_side_effects(canon)
    except AssertionError as e:
        c.check(False, f'canonical SQL rejected: {e}')
    # R4-clock: the boundary is the decision time the Validate computed — dating it by the version again must fail
    by_version = canon.replace('<= r.decided', '<= r.version').replace('> r.decided', '> r.version')
    no_param = canon.replace(', $5::bigint as decided', '')
    for label, bad in (('pre-R4', old), ('closed reads req', half), ('boundary = version (pre-R4-clock)', by_version),
                       ('no decision parameter', no_param)):
        c.check(bad != canon, f'{label}: mutation did not apply')
        try:
            vrm.check_resume_side_effects(bad)
            c.check(False, f'{label}: the tripwire accepted it')
        except AssertionError:
            pass

report()
