#!/usr/bin/env python3
"""B1 «расписание бота» (2026-10-14; spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md
§4 B1, §1): outside the bot's schedule window the bot behaves EXACTLY as in «Вместе» — no auto-reply, the event is not
claimed, no quota is spent, the client's message still enters the one history and the bot's memory (Record Suppressed)
— and the stamp says off_hours. The schedule is one more OR term of Read Reply Mode (it outranks a chat's explicit
«Авто», like the handoff pause); no row or a disabled schedule changes nothing (absence still means «reply»). The
decision is taken at message entry only: Acquire Send Lease never reads the schedule (a window that closes during the
generation does not cancel an answer already paid for). A window across midnight is valid; its day is the day the
window STARTED in the bot's zone.

What runs — real node bodies and statements from the canonical JSONs, real SQL on session temp tables of the staging
Postgres (migration sql/2026-10-14-bot-schedule.sql applied by pg.sh migrate); no n8n server, no network, never prod:
  W   every case of Tools/n8n/fixtures/schedule-cases.json (the contract the client's ScheduleModel shares) through
      bot_schedule.WINDOW_SQL at the case's fixed instant; a disabled case is also run with NO row at all (a bot without
      a schedule — `no_row_reads_as_disabled` names that point); W3: the table has teeth — a predicate whose window
      comparison takes the end minute in fails the end cases (same-day and across midnight, a non-zero end);
  R   the same cases through the CANONICAL Read Reply Mode of both templates — its own text, the clock fixed (the
      canon's `now() at time zone s.tz` → the case's instant): suppressed = off_hours = «the window is closed»;
  G   the mode gate through reply_chain.Run.mode() on the real clock: outside → suppressed + off_hours, Record
      Suppressed writes the client's history row and the memory human row, the Outcome stamp says off_hours, nothing
      is claimed, the quota branch is unreachable, dialog_counts untouched; inside → the ordinary reply; no row /
      disabled → as today; «Вместе» inside the window keeps its label; the schedule outranks a chat's «Авто»;
      G5: the stamp says off_hours ONLY for a RECORDED suppression — a Record Suppressed that n8n failed outside the
      query runner hands its input on (R8: the mode read's row, off_hours true, no outcome) and keeps the old label;
  L   the lease never reads the schedule — its statement names no bot_schedule, and a window that closes between the
      mode read and the lease does not cancel the reply;
  V   verify-history-handoff.py and verify-telegram-parity.py reject scratch copies that drop the term, flip the absence
      rule, give Read Reply Mode an onError, disable a node of the gate, drop the off_hours stamp or its
      recorded-suppression guard, or put the schedule into the lease.

    python3 Tools/n8n/tests/test_reply_schedule.py
"""
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from harness import N8N, WF, Case, PgSession, bind, code, module, node, report, sql_literal as L, wf
from reply_chain import CHATS, SETUP as CHAIN_SETUP, TEMPLATES, Run, history, ledger, memory, msg

CASES = json.loads((N8N / 'fixtures' / 'schedule-cases.json').read_text(encoding='utf-8'))
FIELDS = ('name', 'tz', 'nowIso', 'daysMask', 'startMin', 'endMin', 'enabled', 'expectOpen')
CANON_CLOCK = 'now() at time zone s.tz'
FILES = {'WA': '4wYitz5ek30SVNlT-WhatsApp_Bot.json', 'TG': '4VN3gsFaC2HUYmcc-Telegram_Bot.json'}
try:
    BS = module('bot_schedule.py')
    BS_ERR = None
except Exception as e:          # the module is the predicate's one home — without it every W case fails, readably
    BS, BS_ERR = None, e

SETUP = CHAIN_SETUP + """
create temp table bot_schedule (like public.bot_schedule including all);
create temp table dialog_counts (like public.dialog_counts including all);"""


def fixed_clock(case):
    return f"timestamptz {L(case['nowIso'])} at time zone s.tz"


def put_schedule(pg, pid, case=None, enabled=True, days=127, start=0, end=0, tz='Asia/Almaty'):
    if case is not None:
        enabled, days, start, end, tz = case['enabled'], case['daysMask'], case['startMin'], case['endMin'], case['tz']
    pg.x(f"""insert into bot_schedule (profile_id, enabled, days_mask, start_min, end_min, tz, version, updated_at)
             values ({L(pid)}, {L(enabled)}, {days}, {start}, {end}, {L(tz)}, 1, now())
             on conflict (profile_id) do update set enabled = excluded.enabled, days_mask = excluded.days_mask,
               start_min = excluded.start_min, end_min = excluded.end_min, tz = excluded.tz;""")


def canon_read(T):
    return node(T, 'Read Reply Mode')['parameters']['query']


def read_mode_at(pg, T, pid, chat, case):
    """The CANONICAL mode read with its clock fixed at the case's instant."""
    q = canon_read(T)
    return pg.q(bind(q.replace(CANON_CLOCK, fixed_clock(case)), [pid, chat]))[0]


def minute_now(pg, tz='Asia/Almaty'):
    return pg.q(f"select (extract(hour from now() at time zone {L(tz)}) * 60 + extract(minute from now() at time zone {L(tz)}))::int as m")[0]['m']


def window_around_now(pg, inside):
    """A window two hours either side of the bot's wall time now (inside) or one starting two hours ahead (outside) —
    across midnight whenever the clock says so, every day enabled."""
    m = minute_now(pg)
    return ((m - 120) % 1440, (m + 120) % 1440) if inside else ((m + 120) % 1440, (m + 180) % 1440)


def reachable(w, start, out=None):
    seen, todo = set(), []
    outs = w['connections'].get(start, {}).get('main') or []
    for i, o in enumerate(outs):
        if out is None or i == out:
            todo.extend(e['node'] for e in (o or []))
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for o in (w['connections'].get(n, {}).get('main') or []):
            todo.extend(e['node'] for e in (o or []))
    return seen


def snapshot_of(m):
    return [dict(m)]


# ------------------------------------------------------------------------------------------------ W: the module predicate
with Case('B1 W0: the case table is the shared contract — every case has exactly the eight fields, names unique, ≥ 14 cases, '
          'and the brief\'s boundaries are all there') as c:
    c.check(len(CASES) >= 14, f'{len(CASES)} cases')
    c.check(all(tuple(sorted(x)) == tuple(sorted(FIELDS)) for x in CASES), [sorted(x) for x in CASES if tuple(sorted(x)) != tuple(sorted(FIELDS))])
    c.check(len({x['name'] for x in CASES}) == len(CASES), 'duplicate case names')
    c.check(all(x['nowIso'].endswith('Z') and isinstance(x['daysMask'], int) and 0 <= x['daysMask'] <= 127
                and 0 <= x['startMin'] <= 1439 and 0 <= x['endMin'] <= 1439 for x in CASES), 'a field out of its range')
    kinds = {'inside': any(x['enabled'] and x['expectOpen'] and x['startMin'] < x['endMin'] for x in CASES),
             'outside': any(x['enabled'] and not x['expectOpen'] for x in CASES),
             '24h': any(x['startMin'] == x['endMin'] and x['daysMask'] and x['expectOpen'] for x in CASES),
             'overnight both halves': sum(1 for x in CASES if x['startMin'] > x['endMin'] and x['expectOpen']) >= 2,
             'disabled': any(not x['enabled'] for x in CASES), 'mask 0': any(x['daysMask'] == 0 and x['enabled'] for x in CASES),
             'no row': any(x['name'].startswith('no_row') for x in CASES)}
    c.check(all(kinds.values()), kinds)

with Case('B1 W1: bot_schedule.WINDOW_SQL at each case\'s fixed instant answers the case (a disabled schedule never closes '
          'anything; start inclusive, end exclusive, 24 h, across midnight by the START day, the bot\'s own zone)') as c:
    c.check(BS is not None, f'Tools/n8n/bot_schedule.py: {BS_ERR!r}')
    if BS is not None:
        with PgSession(SETUP) as pg:
            wrong = []
            for case in CASES:
                pg.x('delete from bot_schedule;')
                put_schedule(pg, 'p-' + case['name'], case)
                got = pg.q(f"select ({BS.WINDOW_SQL(fixed_clock(case))}) as open from bot_schedule s")[0]['open']
                allows = (not case['enabled']) or got
                if allows is not case['expectOpen']:
                    wrong.append(f"{case['name']}: window {got} (enabled {case['enabled']}) expected open={case['expectOpen']}")
            c.check(not wrong, wrong)

with Case('B1 W2: the clock is a placeholder — WINDOW_SQL takes any timestamp expression and names nothing but the row `s`') as c:
    c.check(BS is not None, f'{BS_ERR!r}')
    if BS is not None:
        w = BS.WINDOW_SQL('NOW_MARK')
        c.check('NOW_MARK' in w and 'now()' not in w and 'bot_schedule' not in w, w)
        c.check(BS.OFF_HOURS_SQL() == BS.OFF_HOURS_SQL(CANON_CLOCK) and CANON_CLOCK in BS.OFF_HOURS_SQL(), 'the canon clock')

END_CASES = ('end_minute_is_outside', 'overnight_end_minute_is_outside')

with Case('B1 W3: the table pins the end minute as EXCLUSIVE for a non-zero end, same-day and across midnight — a predicate '
          'whose window comparison takes the end minute in opens both end cases (each closed ONLY by that comparison: '
          'its deciding day\'s bit is on)') as c:
    c.check(BS is not None, f'{BS_ERR!r}')
    if BS is not None:
        byname = {x['name']: x for x in CASES}
        c.check(all(n in byname for n in END_CASES), f'missing end cases: {[n for n in END_CASES if n not in byname]}')
        with PgSession(SETUP) as pg:
            caught = {}
            for n in END_CASES:
                case = byname.get(n)
                if case is None:
                    continue
                canon = BS.WINDOW_SQL(fixed_clock(case))
                t = '(' + fixed_clock(case) + ')'
                m = '(extract(hour from ' + t + ')::int * 60 + extract(minute from ' + t + ')::int)'
                inclusive = canon.replace(m + ' < s.end_min)', m + ' <= s.end_min)')
                c.check(inclusive.count(m + ' <= s.end_min)') == 2, f'{n}: the end comparisons were not found in WINDOW_SQL')
                pg.x('delete from bot_schedule;')
                put_schedule(pg, 'p-' + n, case)
                got = pg.q(f"select ({canon}) as canon, ({inclusive}) as inclusive from bot_schedule s")[0]
                caught[n] = got
                c.check(case['enabled'] and case['endMin'] > 0 and case['expectOpen'] is False, f'{n}: not an enabled, non-zero-end, closed case: {case}')
                c.check(got['canon'] is False and got['inclusive'] is True,
                        f'{n}: the canonical window {got["canon"]} (want closed), end-inclusive {got["inclusive"]} (want open — '
                        'otherwise something other than the end comparison closes the case)')

# ------------------------------------------------------------------------------------------------ R: the canonical mode read
for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    with Case(f'B1 R [{ch}] the canonical Read Reply Mode at each case\'s instant: suppressed = off_hours = «closed»; '
              'no row and a disabled row read as no schedule') as c:
        q = canon_read(T)
        c.check(q.count(CANON_CLOCK) >= 2 and 'from bot_schedule s' in q and ' as off_hours' in q,
                'the canonical mode read carries no schedule term / no off_hours column')
        if q.count(CANON_CLOCK) >= 2:
            with PgSession(SETUP) as pg:
                wrong = []
                for case in CASES:
                    pg.x('delete from bot_schedule;')
                    put_schedule(pg, P, case)
                    variants = [('row', read_mode_at(pg, T, P, C, case))]
                    if not case['enabled']:
                        pg.x('delete from bot_schedule;')
                        variants.append(('no row', read_mode_at(pg, T, P, C, case)))
                    want = case['enabled'] and not case['expectOpen']
                    for label, r in variants:
                        if r.get('suppressed') is not want or r.get('off_hours') is not want:
                            wrong.append(f"{case['name']} ({label}): {r} expected suppressed=off_hours={want}")
                c.check(not wrong, wrong)

# ------------------------------------------------------------------------------------------------ G: the gate on the real clock
for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]

    with Case(f'B1 G1 [{ch}] outside the window: «Вместе» exactly — suppressed, Record Suppressed records the client row + '
              'the memory human row, the stamp says off_hours, nothing claimed, no quota branch, no dialog counted') as c:
        with PgSession(SETUP) as pg:
            s, e = window_around_now(pg, inside=False)
            put_schedule(pg, P, start=s, end=e)
            m = msg(ch, 'G1', None, 'Здравствуйте, вы работаете?')
            run = Run(T, pg, m, 'exec-G1')
            row = run.mode()
            c.check(row.get('suppressed') is True and row.get('off_hours') is True, f'mode read {row}')
            c.check(run.outcome == 'off_hours', f'stamp {getattr(run, "stamp", None)}')
            c.check(getattr(run, 'suppressed_row', {}).get('off_hours') is True, f'Record Suppressed carries off_hours: {getattr(run, "suppressed_row", None)}')
            c.check(history(pg, ch) == [{'message_id': 'G1', 'source': 'client', 'body': 'Здравствуйте, вы работаете?'}], f'history {history(pg, ch)}')
            mem = memory(pg, ch)
            c.check(len(mem) == 1 and mem[0]['type'] == 'human' and mem[0]['source'] == 'client' and mem[0]['content'] == 'Здравствуйте, вы работаете?',
                    f'memory {mem}')
            c.check(ledger(pg, ch) == {}, f'the event must not be claimed: {ledger(pg, ch)}')
            c.check(pg.q('select count(*)::int as n from dialog_counts')[0]['n'] == 0, 'a dialog was counted')
        after = reachable(T, 'Suppressed?', out=0)
        c.check(after == {'Record Suppressed', 'Outcome'}, f'Suppressed?[true] reaches {sorted(after)} — Count Dialog / Claim Event must be unreachable')

    with Case(f'B1 G2 [{ch}] inside the window: the ordinary reply (claimed, generated, leased, sent)') as c:
        with PgSession(SETUP) as pg:
            s, e = window_around_now(pg, inside=True)
            put_schedule(pg, P, start=s, end=e)
            m = msg(ch, 'G2', 100, 'Сколько стоит букет?')
            run = Run(T, pg, m, 'exec-G2')
            row = run.mode()
            c.check(row.get('suppressed') is False and row.get('off_hours') is False and not run.done, f'mode read {row}')
            run.claim(); run.latest(snapshot_of(m)); run.generate(); run.final(snapshot_of(m)); run.lease(); run.send_ok()
            c.check(run.outcome == 'sent', f'outcome {run.outcome} cancel {run.cancel}')

    with Case(f'B1 G3 [{ch}] no row / a disabled schedule: exactly as today — «Авто» replies, «Вместе» stamps suppressed') as c:
        with PgSession(SETUP) as pg:
            r0 = Run(T, pg, msg(ch, 'G3a', 100), 'exec-G3a').mode()
            s, e = window_around_now(pg, inside=False)
            put_schedule(pg, P, enabled=False, start=s, end=e)
            r1 = Run(T, pg, msg(ch, 'G3b', 101), 'exec-G3b').mode()
            pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(P)}, '*', true, 5);")
            run = Run(T, pg, msg(ch, 'G3c', 102), 'exec-G3c')
            r2 = run.mode()
            c.check(r0 == {'suppressed': False, 'off_hours': False} and r1 == {'suppressed': False, 'off_hours': False},
                    f'no row {r0} / disabled {r1}')
            c.check(r2 == {'suppressed': True, 'off_hours': False} and run.outcome == 'suppressed', f'«Вместе» {r2} → {run.outcome}')

    with Case(f'B1 G4 [{ch}] «Вместе» inside the window keeps its label; outside it the stamp says off_hours; the schedule '
              'outranks a chat\'s explicit «Авто»; a handoff pause inside the window stays «suppressed»') as c:
        with PgSession(SETUP) as pg:
            s_in, e_in = window_around_now(pg, inside=True)
            s_out, e_out = window_around_now(pg, inside=False)
            pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(P)}, '*', true, 5);")
            put_schedule(pg, P, start=s_in, end=e_in)
            a = Run(T, pg, msg(ch, 'G4a', 100), 'exec-G4a'); a.mode()
            put_schedule(pg, P, start=s_out, end=e_out)
            b = Run(T, pg, msg(ch, 'G4b', 101), 'exec-G4b'); b.mode()
            pg.x(f"update reply_mode_flags set suppressed = false where profile_id = {L(P)};"
                 f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(P)}, {L(C)}, false, 7);")
            d = Run(T, pg, msg(ch, 'G4d', 102), 'exec-G4d'); rd = d.mode()
            put_schedule(pg, P, start=s_in, end=e_in)
            pg.x(f"insert into chat_reply_state (profile_id, chat_id, incoming_generation, handoff_paused) values ({L(P)}, {L(C)}, 1, true);")
            h = Run(T, pg, msg(ch, 'G4h', 103), 'exec-G4h'); rh = h.mode()
            c.check(a.outcome == 'suppressed' and b.outcome == 'off_hours', f'«Вместе» inside → {a.outcome}, outside → {b.outcome}')
            c.check(rd == {'suppressed': True, 'off_hours': True} and d.outcome == 'off_hours', f'chat «Авто» outside the window: {rd} → {d.outcome}')
            c.check(rh == {'suppressed': True, 'off_hours': False} and h.outcome == 'suppressed', f'handoff pause inside: {rh} → {h.outcome}')

    with Case(f'B1 G5 [{ch}] the stamp says off_hours ONLY for a RECORDED suppression: a Record Suppressed that n8n failed '
              'outside the query runner hands its INPUT on (R8: the mode read\'s row, off_hours true, no outcome) and keeps the '
              'pre-B1 label other:Record Suppressed; so does its error item; the recorded row says off_hours') as c:
        with PgSession(SETUP) as pg:
            s, e = window_around_now(pg, inside=False)
            put_schedule(pg, P, start=s, end=e)
            run = Run(T, pg, msg(ch, 'G5', 100, 'Вы сегодня открыты?'), 'exec-G5')
            row = run.mode()

            def stamp(item):
                return code(T, 'Outcome', {'json': item}, run.named, prev_node='Record Suppressed')[0]['json'].get('outcome')

            passed_on = stamp(dict(row))
            error_item = stamp({'message': 'Connection terminated unexpectedly', 'error': {'message': 'Connection terminated unexpectedly'}})
            c.check(row == {'suppressed': True, 'off_hours': True}, f'premise: the mode read outside the window {row}')
            c.check(run.outcome == 'off_hours' and run.suppressed_row.get('outcome') == 'suppressed',
                    f'the recorded suppression {run.suppressed_row} → {run.outcome}')
            c.check(passed_on == 'other:Record Suppressed', f'the passed-on mode-read row was stamped {passed_on!r} — the message was never recorded')
            c.check(error_item == 'other:Record Suppressed', f'the error item was stamped {error_item!r}')

# ------------------------------------------------------------------------------------------------ L: the lease never reads it
for ch, T in TEMPLATES.items():
    P, C = CHATS[ch]
    with Case(f'B1 L [{ch}] the lease never reads the schedule: its statement names no bot_schedule, and a window that '
              'closes after the mode read does not cancel the reply') as c:
        c.check('bot_schedule' not in node(T, 'Acquire Send Lease')['parameters']['query'], 'Acquire Send Lease reads bot_schedule')
        with PgSession(SETUP) as pg:
            s, e = window_around_now(pg, inside=True)
            put_schedule(pg, P, start=s, end=e)
            m = msg(ch, 'L1', 100, 'Есть доставка?')
            run = Run(T, pg, m, 'exec-L1')
            row = run.mode()
            run.claim(); run.latest(snapshot_of(m)); run.generate(); run.final(snapshot_of(m))
            s, e = window_around_now(pg, inside=False)
            put_schedule(pg, P, start=s, end=e)                 # the window closes while the answer is being generated
            lease = run.lease()
            c.check(row.get('off_hours') is False and lease.get('allowed') is True, f'mode {row} lease {lease}')
            run.send_ok()
            c.check(run.outcome == 'sent', f'outcome {run.outcome}')


# ------------------------------------------------------------------------------------------------ V: the verifiers, on scratch
def scratch():
    d = Path(tempfile.mkdtemp(prefix='b1-sched-'))
    for f in WF.glob('*.json'):
        shutil.copy(f, d / f.name)
    return d


def run_tool(tool, d):
    r = subprocess.run([sys.executable, str(N8N / tool), '--dir', str(d)], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def nd(w, name):
    return next(x for x in w['nodes'] if x['name'] == name)


def m_term_removed(w):
    p = nd(w, 'Read Reply Mode')['parameters']
    p['query'] = p['query'].replace(BS.OFF_HOURS_SQL(), 'false')


def m_absence_flipped(w):
    p = nd(w, 'Read Reply Mode')['parameters']
    p['query'] = p['query'].replace(' and s.enabled), false)', ' and s.enabled), true)')


def m_read_continues(w):
    nd(w, 'Read Reply Mode')['onError'] = 'continueRegularOutput'


def m_disabled(name):
    def f(w):
        nd(w, name)['disabled'] = True
    return f


def m_lease_reads_schedule(w):
    p = nd(w, 'Acquire Send Lease')['parameters']
    p['query'] = p['query'].replace("as suppressed\n), cur as (", "or " + BS.OFF_HOURS_SQL() + " as suppressed\n), cur as (", 1)


STAMP_GUARD = "(resp.outcome === 'suppressed' && resp.off_hours === true) ? 'off_hours'"


def m_stamp_dropped(w):
    p = nd(w, 'Outcome')['parameters']
    p['jsCode'] = p['jsCode'].replace(STAMP_GUARD, "false ? 'off_hours'")


def m_stamp_unguarded(w):
    p = nd(w, 'Outcome')['parameters']
    p['jsCode'] = p['jsCode'].replace(STAMP_GUARD, "resp.off_hours === true ? 'off_hours'")


def m_passthrough_dropped(w):
    p = nd(w, 'Record Suppressed')['parameters']['options']
    p['queryReplacement'] = p['queryReplacement'].replace("$('Read Reply Mode').first().json.off_hours === true", 'false')


HH_MUTATIONS = (
    ('the schedule term removed from Read Reply Mode', m_term_removed, 'Read Reply Mode'),
    ('the absence rule flipped (no row → closed)', m_absence_flipped, 'Read Reply Mode'),
    ('Read Reply Mode continues on error (fail-open)', m_read_continues, 'onError'),
    ('Read Reply Mode disabled', m_disabled('Read Reply Mode'), 'disabled'),
    ('Suppressed? disabled', m_disabled('Suppressed?'), 'disabled'),
    ('Record Suppressed disabled', m_disabled('Record Suppressed'), 'disabled'),
    ('Outcome disabled', m_disabled('Outcome'), 'disabled'),
    ('the lease reads the schedule', m_lease_reads_schedule, 'lease'),
    ('the off_hours stamp dropped from Outcome', m_stamp_dropped, 'off_hours'),
    ('the off_hours stamp without the recorded-suppression guard', m_stamp_unguarded, 'off_hours'),
    ('Record Suppressed does not carry off_hours', m_passthrough_dropped, 'off_hours'),
)

with Case('B1 V0: both verifiers pass the committed canon') as c:
    for tool, ok_line in (('verify-history-handoff.py', 'ALL HISTORY/HANDOFF ASSERTS PASSED'), ('verify-telegram-parity.py', 'ALL PARITY ASSERTS PASSED')):
        rc, out = run_tool(tool, WF)
        c.check(rc == 0 and ok_line in out, f'{tool}: exit {rc}: {out[-600:]}')

for label, mutate, needle in HH_MUTATIONS:
    for ch, fname in FILES.items():
        with Case(f'B1 V NEG [{ch}] {label}: verify-history-handoff.py fails, naming it') as c:
            if BS is None:
                c.check(False, f'{BS_ERR!r}')
                continue
            d = scratch()
            try:
                w = json.loads((d / fname).read_text(encoding='utf-8'))
                before = json.dumps(w, sort_keys=True)
                try:
                    mutate(w)
                except StopIteration:
                    pass
                c.check(json.dumps(w, sort_keys=True) != before, f'{label}: the mutation changed nothing')
                (d / fname).write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
                rc, out = run_tool('verify-history-handoff.py', d)
                fails = [l for l in out.splitlines() if l.startswith('HISTORY/HANDOFF FAIL')]
                c.check(rc != 0 and any(needle in l and fname in l for l in fails), f'exit {rc}: {out[-700:]}')
            finally:
                shutil.rmtree(d, ignore_errors=True)


def p_wa_read_differs(d):
    f = d / FILES['WA']
    w = json.loads(f.read_text(encoding='utf-8'))
    p = nd(w, 'Read Reply Mode')['parameters']
    p['query'] = p['query'].replace(' as off_hours', ' as off_hours, 1 as extra', 1)
    f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def p_wa_extra_node(d):
    f = d / FILES['WA']
    w = json.loads(f.read_text(encoding='utf-8'))
    extra = json.loads(json.dumps(nd(w, 'Suppressed?')))
    extra['name'], extra['id'] = 'Schedule Gate', 'b1-extra-node'
    w['nodes'].append(extra)
    f.write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


for label, mutate in (('WA Read Reply Mode differs from TG', p_wa_read_differs), ('a node added to the WA template', p_wa_extra_node)):
    with Case(f'B1 V NEG parity: {label} → verify-telegram-parity.py fails') as c:
        d = scratch()
        try:
            mutate(d)
            rc, out = run_tool('verify-telegram-parity.py', d)
            c.check(rc != 0 and 'PARITY FAIL' in out and 'reply mode gate' in out, f'exit {rc}: {out[-700:]}')
        finally:
            shutil.rmtree(d, ignore_errors=True)

report()
