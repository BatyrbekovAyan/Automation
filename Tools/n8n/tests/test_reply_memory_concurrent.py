#!/usr/bin/env python3
"""V6-concurrent (independent review 2026-10-11, P2): a memory row is stamped by EXACTLY ONE execution.

`Stamp Draft` (V6, iteration 2026-09-28) ties the agent's memory row to its run right after the agent. It picked the
row as `min(id)` of the unstamped same-text rows in a nested select and the outer UPDATE checked only that id: two
runs of one chat that produced the same text (template phrases do) chose the SAME row, the second waited on the
first's row lock and then OVERWROTE its execution_id. The newer run's send then marked nothing, and the older run's
cancel deleted the physical row of the newer run's reply (review evidence: pipeline-concurrent-stamp.json, WA + TG).

Contract (criterion of the review):
  * ownership is atomic and exclusive — a row another execution already holds (stamped, or locked by its stamp in
    flight) is never re-stamped: the loser of the race takes the NEXT matching row of its own, or none at all (and
    then touches nothing in memory, like any failed stamp);
  * the row still has to be this run's by what the database can tell: the content is exactly this agent's output,
    above this run's watermark, no writer's mark (`source`), not stamped;
  * Record Send / Record Cancel act only on the row carrying their own execution id — so a cancel removes only its
    own row and a send marks only its own.

Real canonical Stamp Draft / Claim / Latest+Combine / Final Check / Acquire Send Lease / Record Send / Record Cancel
of BOTH templates (reply_chain.Run) on TWO real PostgreSQL connections of the staging database: the tables live in a
uniquely named fixture schema (own sequences, dropped afterwards) because session temp tables are invisible to the
other connection. The first stamp runs inside an open transaction that holds its row lock while the second stamp
runs; whether the second WAITED on that lock is reported, the verdict is on ownership.

D runs the same race over the statements the review named — the pre-fix nested min(id) and its «min → max» variant —
and requires the checks to catch them; E pins the structural tripwire in verify-reply-pipeline.py (canonical
accepted; pre-fix, min → max, a pick without the lock and an UPDATE without the re-check rejected, WA and TG).

Residual kept (pinned, same as test_reply_memory.py): between two rows of the SAME text the stamps follow the order
in which the runs reach the database, not which agent wrote which row — when the older run stamps first it takes
the newer run's physical row; each run still owns exactly one row, the refused run deletes only its own, and exactly
one row — the sent one — survives (one position later in memory).

    python3 Tools/n8n/tests/test_reply_memory_concurrent.py
"""
import concurrent.futures
import contextlib
import copy
import io
import json
import os
import shutil
import tempfile
import time
import uuid

from harness import Case, PgSession, module, report
from reply_chain import TEMPLATES, Run, _sql, memory, msg

TEXT = 'Точная цена — после диагностики.'
TABLES = ('reply_events', 'chat_reply_state', 'chat_history_events', 'n8n_chat_histories', 'reply_mode_flags')


class Fixture:
    """Three connections (main + two racers) on one fixture schema; everything dropped on exit."""

    def __enter__(self):
        self.schema = 't_v6c_' + uuid.uuid4().hex[:16]
        self.main, self.first, self.second = PgSession(), PgSession(), PgSession()
        s = self.schema
        self.main.x(f'create schema {s}; set search_path = {s};')
        for t in TABLES:
            self.main.x(f'create table {t} (like public.{t} including all);')
        for t in ('chat_history_events', 'n8n_chat_histories'):        # LIKE copies the PUBLIC sequence as default
            self.main.x(f"create sequence {t}_fixture_id; alter table {t} alter column id set default nextval('{t}_fixture_id');")
        for conn in (self.first, self.second):
            conn.x(f"set search_path = {s}; set statement_timeout = '15s';")
        return self

    def __exit__(self, *a):
        for conn in (self.first, self.second):
            try:
                conn.x('rollback;')
            except Exception:
                pass
        try:
            self.main.x(f'drop schema if exists {self.schema} cascade;')
        finally:
            for conn in (self.main, self.first, self.second):
                conn.close()


def race(T, fx, holder, other):
    """`holder` stamps inside an open transaction on the first connection (its row lock held); meanwhile `other`
    stamps on the second connection. Returns (holder stamp, other stamp, other waited on a lock)."""
    fx.first.x('begin;')
    a = _sql(T, fx.first, 'Stamp Draft', {'output': holder.ai_text}, holder.named, holder.exec_id)
    pid = fx.second.q('select pg_backend_pid() as pid')[0]['pid']
    waited = False
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(_sql, T, fx.second, 'Stamp Draft', {'output': other.ai_text}, other.named, other.exec_id)
        try:
            for _ in range(500):                      # until it ends or waits (≤ ~10 s on a loaded machine), never a hang
                if pending.done():
                    break
                st = fx.main.q(f'select wait_event_type from pg_stat_activity where pid = {pid}')
                if st and st[0]['wait_event_type'] == 'Lock':
                    waited = True
                    break
                time.sleep(0.02)
        finally:
            fx.first.x('commit;')
        b = pending.result(timeout=20)
    for run, st in ((holder, a), (other, b)):
        run.stamp_row = st
        run.named['Stamp Draft'] = st
    return a, b, waited


def ai_rows(fx, ch):
    return [r for r in memory(fx.main, ch) if r['type'] == 'ai' and r['source'] is None]


def ai_id(fx, ch):
    return ai_rows(fx, ch)[-1]['id']


def owners(fx, ch):
    return {r['id']: r['execution_id'] for r in ai_rows(fx, ch)}


def two_runs(fx, ch, T, old_writes=True):
    """OLD claimed first, NEW (newer client message) second; both agents return the SAME text. NEW's agent writes its
    memory pair first, then OLD's. Neither stamp has run yet. Returns (old, new, mo, mn, physical row of each)."""
    mo, mn = msg(ch, 'OLD', 100), msg(ch, 'NEW', 130)
    old, new = Run(T, fx.main, mo, 'exec-old'), Run(T, fx.main, mn, 'exec-new')
    old.claim(); old.latest([mo])
    new.claim(); new.latest([mn, mo])
    new.generate(TEXT, stamp=False)
    new_phys = ai_id(fx, ch)
    old.generate(TEXT, write_memory=old_writes, stamp=False)
    old_phys = ai_id(fx, ch) if old_writes else None
    return old, new, mo, mn, new_phys, old_phys


def finish(fx, ch, old, new, mo, mn):
    """NEW goes out; OLD's final snapshot holds the newer client message → superseded (Record Cancel)."""
    new.final([mn, mo]); new.lease(); new.send_ok('confirmed-new-send')
    old.final([mn, mo])


def summary(fx, ch):
    return [(r['id'], r['type'], r['execution_id'], r['delivery']) for r in memory(fx.main, ch)]


def scenario_a(T, ch, log):
    """Review order: NEW stamps under its lock while OLD stamps."""
    bad = []
    with Fixture() as fx:
        old, new, mo, mn, new_phys, old_phys = two_runs(fx, ch, T)
        sn, so, waited = race(T, fx, new, old)
        after = owners(fx, ch)
        log(f'A: rows new={new_phys} old={old_phys}; stamps new={sn.get("draft_row_id")} old={so.get("draft_row_id")} '
            f'second-waited={waited}; owners after both={after}')
        if not (sn.get('draft_row_id') == new_phys and so.get('draft_row_id') == old_phys):
            bad.append(f'each run must stamp its own row: new→{sn} (row {new_phys}) old→{so} (row {old_phys})')
        if after != {new_phys: 'exec-new', old_phys: 'exec-old'}:
            bad.append(f'a stamped row changed hands: owners={after}')
        finish(fx, ch, old, new, mo, mn)
        mem = memory(fx.main, ch)
        log(f'A: record_new={new.record} cancel_old={old.cancel} memory={summary(fx, ch)}')
        if not (new.outcome == 'sent' and new.record.get('marked_ai_rows') == 1):
            bad.append(f'the send must mark exactly its row: new={new.outcome} record={new.record}')
        if not (old.outcome == 'superseded' and old.cancel.get('dropped_ai_rows') == 1):
            bad.append(f'the cancel must drop exactly its row: old={old.outcome} cancel={old.cancel}')
        row = next((r for r in mem if r['id'] == new_phys), None)
        if not (row is not None and row['execution_id'] == 'exec-new' and row['delivery'] == 'sent'
                and row['transport_message_id'] == 'confirmed-new-send'):
            bad.append(f'NEW\'s physical row must survive, sent: {row} memory={summary(fx, ch)}')
        if any(r['id'] == old_phys for r in mem):
            bad.append(f'OLD\'s own draft must be gone: {summary(fx, ch)}')
        if [r for r in ai_rows(fx, ch) if r['execution_id'] is None]:
            bad.append(f'an ai row was left without an owner: {summary(fx, ch)}')
        if len([r for r in mem if r['type'] == 'human']) != 2:
            bad.append(f'human rows must stay: {summary(fx, ch)}')
    return bad


def scenario_b(T, ch, log):
    """Reverse order: OLD stamps under its lock while NEW stamps."""
    bad = []
    with Fixture() as fx:
        old, new, mo, mn, new_phys, old_phys = two_runs(fx, ch, T)
        so, sn, waited = race(T, fx, old, new)
        after = owners(fx, ch)
        log(f'B: rows new={new_phys} old={old_phys}; stamps old={so.get("draft_row_id")} new={sn.get("draft_row_id")} '
            f'second-waited={waited}; owners after both={after}')
        if {so.get('draft_row_id'), sn.get('draft_row_id')} != {new_phys, old_phys}:
            bad.append(f'the two runs must own two distinct rows: old→{so} new→{sn}')
        if not (after.get(so.get('draft_row_id')) == 'exec-old' and after.get(sn.get('draft_row_id')) == 'exec-new'):
            bad.append(f'a stamp was overwritten: owners={after} old→{so} new→{sn}')
        finish(fx, ch, old, new, mo, mn)
        log(f'B: record_new={new.record} cancel_old={old.cancel} memory={summary(fx, ch)}')
        if not (new.outcome == 'sent' and new.record.get('marked_ai_rows') == 1):
            bad.append(f'the send must mark exactly its row: new={new.outcome} record={new.record}')
        if not (old.outcome == 'superseded' and old.cancel.get('dropped_ai_rows') == 1):
            bad.append(f'the cancel must drop exactly its row: old={old.outcome} cancel={old.cancel}')
        surv = ai_rows(fx, ch)
        if not (len(surv) == 1 and surv[0]['id'] == sn.get('draft_row_id') and surv[0]['execution_id'] == 'exec-new'
                and surv[0]['delivery'] == 'sent'):
            bad.append(f'exactly NEW\'s sent row must survive: {summary(fx, ch)}')
        # residual (pinned): OLD reached the database first and took the lower same-text row — NEW's physical one
        if not (so.get('draft_row_id') == new_phys and sn.get('draft_row_id') == old_phys):
            bad.append(f'documented residual shape changed (update the docs if it was closed): old→{so} new→{sn}')
    return bad


def scenario_c(T, ch, log):
    """One candidate: OLD's memory pair was never written; NEW holds the only matching row."""
    bad = []
    with Fixture() as fx:
        old, new, mo, mn, new_phys, _ = two_runs(fx, ch, T, old_writes=False)
        sn, so, waited = race(T, fx, new, old)
        after = owners(fx, ch)
        log(f'C: row new={new_phys}; stamps new={sn.get("draft_row_id")} old={so.get("draft_row_id")} '
            f'second-waited={waited}; owners after both={after}')
        if not (sn.get('draft_row_id') == new_phys and so.get('draft_row_id') is None):
            bad.append(f'the loser must stamp nothing: new→{sn} old→{so}')
        if after != {new_phys: 'exec-new'}:
            bad.append(f'NEW\'s row changed hands: owners={after}')
        finish(fx, ch, old, new, mo, mn)
        log(f'C: record_new={new.record} cancel_old={old.cancel} memory={summary(fx, ch)}')
        if not (new.record.get('marked_ai_rows') == 1 and old.cancel.get('dropped_ai_rows') == 0):
            bad.append(f'the send marks its row, the loser deletes nothing: record={new.record} cancel={old.cancel}')
        surv = ai_rows(fx, ch)
        if not (len(surv) == 1 and surv[0]['id'] == new_phys and surv[0]['execution_id'] == 'exec-new'
                and surv[0]['delivery'] == 'sent'):
            bad.append(f'NEW\'s sent row must survive: {summary(fx, ch)}')
    return bad


SCENARIOS = (
    ('A (review order): NEW stamps under its lock while OLD stamps — each run gets ITS OWN row; the send marks only '
     'NEW\'s, the cancel deletes only OLD\'s', scenario_a),
    ('B (reverse order): OLD stamps under its lock while NEW stamps — two distinct owners, nobody overwritten; exactly '
     'one row survives, the sent one (residual: the stamps follow lock order between same-text rows)', scenario_b),
    ('C (one candidate): the row NEW holds is never taken over — OLD stamps nothing, its cancel deletes nothing, '
     'NEW\'s row goes out marked', scenario_c),
)

for ch, T in TEMPLATES.items():
    for title, fn in SCENARIOS:
        with Case(f'V6c [{ch}] {title}') as c:
            for v in fn(T, ch, lambda line, ch=ch: print(f'  [{ch}] {line}')):
                c.check(False, v)


# D: the checks above reject the shapes the review named — the pre-fix statement (nested min(id), the outer UPDATE
# checks only the id) and its «min → max» variant — on the real two-connection race. The variants are derived from
# the canonical statement, so they track it: dropping the lock and the outer re-check restores the 2026-09-28 SQL.
LOCKED_PICK = 'select id from n8n_chat_histories'
LOCK_CLAUSE = '\n              order by id\n              limit 1\n              for update skip locked)'
OUTER_RECHECK = ("\n    and session_id = $1 || ':' || $2 and id > $5::bigint and message->>'type' = 'ai'"
                 "\n    and (message->'additional_kwargs'->>'source') is null"
                 "\n    and (message->'additional_kwargs'->>'execution_id') is null"
                 "\n    and message->>'content' = $6")


def variant(T, agg):
    w = copy.deepcopy(T)
    n = next(x for x in w['nodes'] if x['name'] == 'Stamp Draft')
    q = n['parameters']['query']
    assert LOCKED_PICK in q and LOCK_CLAUSE in q and OUTER_RECHECK in q, 'canonical Stamp Draft changed shape: update D'
    n['parameters']['query'] = q.replace(OUTER_RECHECK, '').replace(LOCK_CLAUSE, ')').replace(
        LOCKED_PICK, f'select {agg}(id) from n8n_chat_histories', 1)
    return w


for agg in ('min', 'max'):
    with Case(f'V6c [WA] D: the checks reject the {"pre-fix nested min(id)" if agg == "min" else "«min → max»"} statement '
              f'(its race re-assigns or loses a row)') as c:
        w = variant(TEMPLATES['WA'], agg)
        found = []
        for title, fn in SCENARIOS:
            v = fn(w, 'WA', lambda line: None)
            found += [f'{title[:1]}: {x}' for x in v]
        print(f'  [WA] D {agg}(id): {len(found)} violation(s) found, e.g. {found[:2]}')
        c.check(any(x.startswith('C: ') for x in found) and any(x.startswith('A: ') for x in found),
                f'the {agg}(id) variant passed the race checks: {found}')


# E: the structural tripwire (verify-reply-pipeline.py) holds the shape even when nobody runs the race: it accepts the
# canonical templates and rejects the pre-fix statement, «min → max», a pick without the lock and an UPDATE without
# the re-check — on BOTH templates.
with Case('V6c E: verify-reply-pipeline accepts the canonical Stamp Draft and rejects the unlocked / un-rechecked shapes') as c:
    vrp = module('verify-reply-pipeline.py')
    tmp = tempfile.mkdtemp(prefix='v6c-verify-')
    try:
        vrp.WF = tmp
        for ch, fname in (('WA', vrp.BOTS[0]), ('TG', vrp.BOTS[1])):
            T = TEMPLATES[ch]

            def accepts(w):
                with open(os.path.join(tmp, fname), 'w', encoding='utf-8') as f:
                    json.dump(w, f)
                out = io.StringIO()
                try:
                    with contextlib.redirect_stdout(out):
                        vrp.verify(fname)
                    return True, ''
                except SystemExit:
                    return False, out.getvalue().strip().splitlines()[-1:]

            def with_query(fn):
                w = copy.deepcopy(T)
                n = next(x for x in w['nodes'] if x['name'] == 'Stamp Draft')
                n['parameters']['query'] = fn(n['parameters']['query'])
                return w

            ok, why = accepts(copy.deepcopy(T))
            c.check(ok, f'[{ch}] canonical template rejected: {why}')
            bad = {'pre-fix nested min(id)': variant(T, 'min'), '«min → max»': variant(T, 'max'),
                   'pick without the lock': with_query(lambda q: q.replace(LOCK_CLAUSE, '\n              order by id\n              limit 1)')),
                   'UPDATE without the re-check': with_query(lambda q: q.replace(OUTER_RECHECK, ''))}
            for label, w in bad.items():
                ok, why = accepts(w)
                c.check(not ok, f'[{ch}] the tripwire accepted the {label} statement')
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

report()
