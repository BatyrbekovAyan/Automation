#!/usr/bin/env python3
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §3.2, §3.3, §4.1,
§4.3, §4.6; plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 2): the per-chat decisions on the server — the
migration (sql/2026-10-25-chat-audience.sql: chat_audience, audience_connections, reply_events' 'cancelled_personal') and
the statements the later n8n tasks run against them (Tools/n8n/audience.py), executed here on REAL SQL. The bot's line is
pinned elsewhere: the move on a reconnect (clone_lineage.py) by tests/test_clone_lineage_sql.py, the delete with the bot
(apply-history-handoff.py) by tests/test_retired_delete.py.

What runs — the module's statements, bound the way n8n's queryReplacement binds them (harness.bind), against session temp
tables of the staging Postgres (`like public.X including all`: the migration must be applied — pg.sh migrate); the race
cases on TWO real connections over a uniquely named fixture schema (a temp table is invisible to the other connection),
dropped afterwards. BACKFILL_SQL is composed with apply-history-handoff.py's own MEMORY_ROW and OWNER_CONTENT_SQL, exactly
as the generators will compose it. No n8n server, no network, never prod.

  default_written_once                    a computed decision is written once; a second one changes nothing; a pair the
                                          rule never yields (business/contact, a business «not a person») writes nothing
  race_two_first_messages_one_decision    two connections, personal/history against business/new: one row, BOTH answers are
                                          the written row (the second waited on the first's uncommitted insert)
  race_two_first_messages_one_moment      the same race on ENSURE_CONNECTION_SQL: one row, both answers carry the moment
  race_two_reports_of_a_new_number_one_restart   two connections report the same new number: the first says restarted,
                                          the second (waiting on its lock) finds the moved moment and does not
  owner_outranks_default_and_default_never_overwrites_owner
  owner_version_guard                     an older or replayed version is not applied, the answer names the server's version;
                                          one answer per chat in the request's order, malformed elements skipped
  number_change_voids_defaults_keeps_owner   another number → the computed rows stop holding (AUDIENCE_SQL NULL), the
                                          owner's stays; DECIDE_WRITE_SQL then replaces the stale row
  same_or_empty_phone_keeps_the_moment    CONNECTED_SQL: no row → the line's moment; a missing number is filled in; the same
                                          or an empty number changes nothing
  connected_says_restarted_only_when_it_moved_the_moment   restarted = THIS statement moved connected_at (another known
                                          number on an existing row); a first row, a filled-in number, the same / no number,
                                          a sentinel: false
  owner_set_refuses_nonhuman              by '@g.us' / '@newsletter' / '@broadcast', by 777000, by a recorded reason
  personal_since_rules                    business → personal stamps now, personal → personal keeps, → business clears;
                                          wiped_at clears with them
  owner_set_became_business               true only for an APPLIED business whose previous holding kind was not business
  line_moment_follows_superseded_by       the earliest created_at along the superseded_by chain (same channel), now() without
                                          a row, a cycle terminates
  ensure_connection_always_one_row        created once with the line's moment and no number; an existing row comes back
                                          unchanged; a sentinel profile → one row of nulls, nothing written
  list_returns_only_valid_decisions_and_the_moment
  backfill_adds_missing_in_time_order_with_owner_mark
  backfill_writes_nothing_for_a_personal_or_undecided_chat
  migration_postchecks                    RLS on, no anon/authenticated grants, the staging tables = the file's DDL,
                                          reply_events takes 'cancelled_personal' and still refuses 'bogus', a re-run is a
                                          no-op, pg.sh migrates the file after the bot schedule
  statements_pass_the_sql_guard           verify-sql-comments finds nothing in any statement (V1)
  audience_module_never_imports_apply_history_handoff   (that module imports audience.py — an import back is a cycle)
  fixtures_clean_both_tables              staging/fixtures.py delete clears both tables of a fixture profile; the mock sweep
                                          names them too

    python3 Tools/n8n/tests/test_audience_sql.py
"""
import ast
import concurrent.futures
import json
import re
import subprocess
import sys
import time
import uuid

from harness import N8N, Case, PgSession, bind, module, report, sql_literal as L, sql_run

MIGRATION = N8N / 'sql' / '2026-10-25-chat-audience.sql'
PG_SH = N8N / 'staging' / 'pg.sh'
FIXTURES = N8N / 'staging' / 'fixtures.py'
AUDIENCE_PY = N8N / 'audience.py'

AUD = module('audience.py')
AHH = module('apply-history-handoff.py')
VSC = module('verify-sql-comments.py')

TABLES = ('bot_profiles', 'chat_audience', 'audience_connections', 'chat_history_events', 'n8n_chat_histories')
# LIKE copies the PUBLIC sequence as the id default: the temp copies get their own, so nothing shared moves
SETUP = (''.join(f'create temp table {t} (like public.{t} including all);\n' for t in TABLES)
         + ''.join(f"create temp sequence {t}_aud_seq; alter table {t} alter column id set default nextval('{t}_aud_seq');\n"
                   for t in ('chat_history_events', 'n8n_chat_histories')))

CH = {'WA': {'channel': 'whatsapp', 'chat': '77011234567@c.us', 'chat2': '77017654321@c.us', 'chat3': '77015550101@c.us',
             'chat4': '77015550202@c.us', 'chat5': '77015550303@c.us'},
      'TG': {'channel': 'telegram', 'chat': '5512345678', 'chat2': '5587654321', 'chat3': '5550101010',
             'chat4': '5550202020', 'chat5': '5550303030'}}
OLD_STATES = ('claimed', 'generated', 'send_pending', 'sent', 'send_failed', 'send_unknown', 'superseded', 'cancelled_owner',
              'cancelled_mode', 'cancelled_busy', 'cancelled_fetch', 'generation_failed', 'quota_denied', 'suppressed')


# ------------------------------------------------------------------------------------------------ the statements, bound
def decide(pg, pid, chat, kind, reason):
    return pg.q(bind(AUD.DECIDE_WRITE_SQL, [pid, chat, kind, reason]))


def owner_set(pg, pid, chats):
    return pg.q(bind(AUD.OWNER_SET_SQL, [pid, chats]))


def connected(pg, pid, phone):
    return pg.q(bind(AUD.CONNECTED_SQL, [pid, phone]))


def ensure(pg, pid):
    return pg.q(bind(AUD.ENSURE_CONNECTION_SQL, [pid]))


def listed(pg, pids):
    return pg.q(bind(AUD.LIST_SQL, [pids]))


def backfill_sql():
    return AUD.BACKFILL_SQL(AHH.MEMORY_ROW, AHH.OWNER_CONTENT_SQL)


def backfill(pg, pid, chat, trigger, msgs):
    return pg.q(bind(backfill_sql(), [pid, chat, trigger, msgs]))


def audience(pg, pid, chat):
    return pg.q(bind('select ' + AUD.AUDIENCE_SQL() + ' as kind', [pid, chat]))[0]['kind']


def business(pg, pid, chat):
    return pg.q(bind('select ' + AUD.BUSINESS_SQL() + ' as b', [pid, chat]))[0]['b']


def line_ms(pg, pid):
    return pg.q(bind('select (extract(epoch from ' + AUD.LINE_CONNECTED_AT_SQL() + ') * 1000)::bigint as ms', [pid]))[0]['ms']


# ------------------------------------------------------------------------------------------------ seeding and reading
def profile(pg, pid, channel, age, superseded_by=None):
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, created_at, superseded_by) values "
         f"({L(pid)}, 'u-audience-test', {L(channel)}, now() - interval {L(age)}, {L(superseded_by)});")


def created_ms(pg, pid):
    return pg.q(f"select (extract(epoch from created_at) * 1000)::bigint as ms from bot_profiles where profile_id = {L(pid)}")[0]['ms']


def now_ms(pg):
    return pg.q('select (extract(epoch from clock_timestamp()) * 1000)::bigint as ms')[0]['ms']


def row(pg, pid, chat):
    r = pg.q(f"select kind, source, reason, version, decided_at, personal_since, wiped_at, updated_at from chat_audience "
             f"where profile_id = {L(pid)} and chat_id = {L(chat)}")
    return r[0] if r else None


def conn_row(pg, pid):
    r = pg.q(f"select phone, (extract(epoch from connected_at) * 1000)::bigint as ms, updated_at from audience_connections "
             f"where profile_id = {L(pid)}")
    return r[0] if r else None


def n_rows(pg, table, pid):
    return pg.q(f"select count(*)::int as n from {table} where profile_id = {L(pid)}")[0]['n']


def tick(pg):
    """Statements autocommit one by one, each with its own now(); a short sleep keeps two of them strictly apart."""
    pg.x('select pg_sleep(0.003);')


def dec(kind, reason, source):
    return {'kind': kind, 'reason': reason, 'source': source}


NULL_DEC = {'kind': None, 'reason': None, 'source': None}


# ------------------------------------------------------------------------------------------------ the race fixture
class Race:
    """Three connections (main + two racers) on one fixture schema with the real tables' shape; dropped on exit. The
    schema and its tables are created in ONE transaction: a failure (a table that does not exist yet) leaves nothing."""
    TABLES = ('bot_profiles', 'chat_audience', 'audience_connections')

    def __enter__(self):
        self.schema = 't_aud_' + uuid.uuid4().hex[:16]
        self.main, self.first, self.second = PgSession(), PgSession(), PgSession()
        try:
            self.main.x(f'begin; create schema {self.schema}; '
                        + ''.join(f'create table {self.schema}.{t} (like public.{t} including all); ' for t in self.TABLES)
                        + f'commit; set search_path = {self.schema};')
            for conn in (self.first, self.second):
                conn.x(f"set search_path = {self.schema}; set statement_timeout = '15s';")
        except BaseException:
            self.__exit__()
            raise
        return self

    def __exit__(self, *a):
        for conn in (self.first, self.second):
            try:
                conn.x('rollback;')
            except Exception:
                pass
        try:
            with PgSession() as pg:          # a fresh session: the main one may have died with a failed statement
                pg.x(f'drop schema if exists {self.schema} cascade;')
        finally:
            for conn in (self.main, self.first, self.second):
                try:
                    conn.close()
                except Exception:
                    pass


def race(fx, first_sql, second_sql):
    """`first_sql` runs inside an open transaction on the first connection (its insert uncommitted); meanwhile
    `second_sql` runs on the second. The first commits once the second waits on its lock (or after ~10 s, never a hang).
    Returns (first answer, second answer, the second waited on a lock)."""
    fx.first.x('begin;')
    a = fx.first.q(first_sql)
    pid = fx.second.q('select pg_backend_pid() as pid')[0]['pid']
    waited = False
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(fx.second.q, second_sql)
        try:
            for _ in range(500):
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
    return a, b, waited


# ================================================================================================ decisions
for ch, C in CH.items():
    P = f'{ch.lower()}-aud-p1'
    with Case(f'default_written_once [{ch}]: a computed decision is written once — a second one changes nothing; a pair the '
              'rule never yields writes nothing and answers nulls') as c:
        with PgSession(SETUP) as pg:
            first = decide(pg, P, C['chat'], 'personal', 'contact')
            c.check(first == [dec('personal', 'contact', 'default')], f'the first decision: {first}')
            before = row(pg, P, C['chat'])
            tick(pg)
            second = decide(pg, P, C['chat'], 'business', 'new')
            c.check(second == [dec('personal', 'contact', 'default')], f'the second decision must read the first: {second}')
            c.check(row(pg, P, C['chat']) == before and n_rows(pg, 'chat_audience', P) == 1,
                    f'the row moved: {before} → {row(pg, P, C["chat"])}')
            c.check(audience(pg, P, C['chat']) == 'personal' and business(pg, P, C['chat']) is False, 'AUDIENCE/BUSINESS')
            wrong = []
            for kind, reason in (('business', 'contact'), ('business', 'history'), ('business', 'nonhuman'), ('personal', 'new'),
                                 ('personal', 'unknown'), ('personal', 'owner'), ('business', 'owner'), ('maybe', 'new')):
                got = decide(pg, P, C['chat2'], kind, reason)
                if got != [NULL_DEC]:
                    wrong.append(f'{kind}/{reason}: {got}')
            c.check(not wrong and row(pg, P, C['chat2']) is None, f'inconsistent pairs must write nothing: {wrong}')
            for sentinel in ('', '-1'):
                c.check(decide(pg, sentinel, C['chat2'], 'business', 'new') == [NULL_DEC], f'profile {sentinel!r} must write nothing')
            c.check(decide(pg, P, '', 'business', 'new') == [NULL_DEC], 'an empty chat id must write nothing')
            nonhuman = '120363000000000001@g.us' if ch == 'WA' else '777000'
            c.check(decide(pg, P, nonhuman, 'business', 'new') == [NULL_DEC] and row(pg, P, nonhuman) is None,
                    'a business decision for a chat that is not a person is never written')
            c.check(decide(pg, P, nonhuman, 'personal', 'nonhuman') == [dec('personal', 'nonhuman', 'default')],
                    'not a person → personal/nonhuman is written')

for label, (first, second, ch) in {'personal/history first': (('personal', 'history'), ('business', 'new'), 'WA'),
                                   'business/new first': (('business', 'new'), ('personal', 'history'), 'TG')}.items():
    with Case(f'race_two_first_messages_one_decision [{ch}, {label}]: two connections decide the same new chat — one row, '
              'both answers are the written row') as c:
        P, chat = f'{ch.lower()}-race-p1', CH[ch]['chat']
        with Race() as fx:
            a, b, waited = race(fx, bind(AUD.DECIDE_WRITE_SQL, [P, chat, *first]), bind(AUD.DECIDE_WRITE_SQL, [P, chat, *second]))
            want = dec(first[0], first[1], 'default')
            rows = fx.main.q(f"select kind, reason, source from chat_audience where profile_id = {L(P)}")
            c.check(rows == [want], f'one row, the first decision: {rows}')
            c.check(a == [want], f'the first answer: {a}')
            c.check(b == [want], f'the second answer must be the written row, not its own nor nulls: {b}')
            c.check(waited, "the second never waited on the first's uncommitted insert — the race was not exercised")

with Case('race_two_first_messages_one_moment: two connections ensure the same missing connection row — one row, both '
          'answers carry the line\'s moment') as c:
    P = 'wa-race-moment'
    with Race() as fx:
        fx.main.x(f"insert into bot_profiles (profile_id, app_user_id, channel, created_at) values "
                  f"({L(P)}, 'u-audience-test', 'whatsapp', now() - interval '3 days');")
        moment = fx.main.q(f"select (extract(epoch from created_at) * 1000)::bigint as ms from bot_profiles where profile_id = {L(P)}")[0]['ms']
        a, b, waited = race(fx, bind(AUD.ENSURE_CONNECTION_SQL, [P]), bind(AUD.ENSURE_CONNECTION_SQL, [P]))
        want = [{'connected_at_ms': moment, 'phone': None}]
        c.check(a == want and b == want, f'both answers must carry the moment: {a} / {b} (moment {moment})')
        c.check(fx.main.q(f"select count(*)::int as n from audience_connections where profile_id = {L(P)}")[0]['n'] == 1, 'one row')
        c.check(waited, "the second never waited on the first's uncommitted insert — the race was not exercised")

with Case('race_two_reports_of_a_new_number_one_restart: two connections report the same NEW number for a profile at once — '
          'the first moves the moment and says restarted, the second (waiting on its lock) finds the number already there: '
          'the same moment, restarted false') as c:
    P = 'tg-race-restart'
    with Race() as fx:
        fx.main.x(f"insert into bot_profiles (profile_id, app_user_id, channel, created_at) values "
                  f"({L(P)}, 'u-audience-test', 'telegram', now() - interval '3 days');")
        fx.main.q(bind(AUD.CONNECTED_SQL, [P, '77010000001']))        # the old number, the line's moment
        old = fx.main.q(f"select (extract(epoch from connected_at) * 1000)::bigint as ms from audience_connections "
                        f"where profile_id = {L(P)}")[0]['ms']
        a, b, waited = race(fx, bind(AUD.CONNECTED_SQL, [P, '77020000002']), bind(AUD.CONNECTED_SQL, [P, '+7 702 000 00 02']))
        c.check(waited, "the second never waited on the first's uncommitted update — the race was not exercised")
        c.check(len(a) == 1 and a[0]['restarted'] is True and a[0]['connected_at_ms'] > old, f'the first moved it: {a} (old {old})')
        c.check(b == [{'connected_at_ms': a[0]['connected_at_ms'] if a else None, 'restarted': False}],
                f'the second must find the moved moment and not say restarted: {b} vs {a}')

for ch, C in CH.items():
    P = f'{ch.lower()}-aud-p1'
    with Case(f'owner_outranks_default_and_default_never_overwrites_owner [{ch}]') as c:
        with PgSession(SETUP) as pg:
            profile(pg, P, C['channel'], '3 days')
            connected(pg, P, '77010000001')
            decide(pg, P, C['chat'], 'business', 'new')
            got = owner_set(pg, P, [{'chatId': C['chat'], 'kind': 'personal', 'version': 1000}])
            c.check(got == [{'chat_id': C['chat'], 'applied': True, 'kind': 'personal', 'version': 1000, 'refused': None,
                             'became_business': False}], f'the owner over a holding default: {got}')
            r = row(pg, P, C['chat'])
            c.check(r and (r['kind'], r['source'], r['reason'], r['version']) == ('personal', 'owner', 'owner', 1000), f'row {r}')
            tick(pg)
            again = decide(pg, P, C['chat'], 'business', 'new')
            c.check(again == [dec('personal', 'owner', 'owner')], f'a computed decision must answer the owner\'s: {again}')
            c.check(row(pg, P, C['chat']) == r, f'the owner\'s row changed: {r} → {row(pg, P, C["chat"])}')
            tick(pg)
            connected(pg, P, '77010000002')          # another number: the owner's decision still holds, still wins
            tick(pg)
            c.check(audience(pg, P, C['chat']) == 'personal', 'the owner\'s decision must hold after a number change')
            again = decide(pg, P, C['chat'], 'business', 'new')
            c.check(again == [dec('personal', 'owner', 'owner')] and row(pg, P, C['chat']) == r,
                    f'after a number change the owner\'s row must stay untouched: {again} {row(pg, P, C["chat"])}')
            decide(pg, P, C['chat2'], 'personal', 'contact')
            got = owner_set(pg, P, [{'chatId': C['chat2'], 'kind': 'business', 'version': 5}])
            c.check(got and got[0]['applied'] is True and got[0]['kind'] == 'business' and got[0]['became_business'] is True,
                    f'the owner applies over a computed row whatever its version: {got}')

    with Case(f'owner_version_guard [{ch}]: an older or replayed version is not applied and the answer names the server\'s '
              'version; one answer per chat in the request\'s order; malformed elements are skipped') as c:
        with PgSession(SETUP) as pg:
            got = owner_set(pg, P, [{'chatId': C['chat'], 'kind': 'personal', 'version': 2000}])
            c.check(got and got[0]['applied'] is True and got[0]['version'] == 2000, f'first: {got}')
            before = row(pg, P, C['chat'])
            stale = owner_set(pg, P, [{'chatId': C['chat'], 'kind': 'business', 'version': 1500}])
            replay = owner_set(pg, P, [{'chatId': C['chat'], 'kind': 'business', 'version': 2000}])
            want = [{'chat_id': C['chat'], 'applied': False, 'kind': 'personal', 'version': 2000, 'refused': None,
                     'became_business': False}]
            c.check(stale == want, f'stale: {stale}')
            c.check(replay == want, f'replay: {replay}')
            c.check(row(pg, P, C['chat']) == before, f'the row moved: {before} → {row(pg, P, C["chat"])}')
            newer = owner_set(pg, P, [{'chatId': C['chat'], 'kind': 'business', 'version': 3000}])
            c.check(newer == [{'chat_id': C['chat'], 'applied': True, 'kind': 'business', 'version': 3000, 'refused': None,
                               'became_business': True}], f'newer: {newer}')
            dup = owner_set(pg, P, [{'chatId': C['chat2'], 'kind': 'personal', 'version': 4000},
                                    {'chatId': C['chat3'], 'kind': 'personal', 'version': 4000},
                                    {'chatId': C['chat2'], 'kind': 'business', 'version': 5000}])
            c.check([(r['chat_id'], r['applied'], r['kind'], r['version']) for r in dup]
                    == [(C['chat2'], True, 'business', 5000), (C['chat3'], True, 'personal', 4000)],
                    f'one answer per chat, the highest version, at its first position: {dup}')
            bad = owner_set(pg, P, [{'chatId': '', 'kind': 'business', 'version': 9}, {'chatId': C['chat4'], 'kind': 'other', 'version': 9},
                                    {'chatId': C['chat4'], 'kind': 'business', 'version': 0},
                                    {'chatId': C['chat4'], 'kind': 'business', 'version': 1.5},
                                    {'chatId': C['chat4'], 'kind': 'business', 'version': '7'},
                                    {'chatId': C['chat4'], 'kind': 'business'}, 'not-an-object',
                                    {'chatId': C['chat5'], 'kind': 'business', 'version': 9}])
            c.check([r['chat_id'] for r in bad] == [C['chat5']] and row(pg, P, C['chat4']) is None,
                    f'malformed elements must be skipped: {bad}')
            c.check(owner_set(pg, '', [{'chatId': C['chat4'], 'kind': 'business', 'version': 9}]) == []
                    and owner_set(pg, '-1', [{'chatId': C['chat4'], 'kind': 'business', 'version': 9}]) == [],
                    'a sentinel profile writes nothing')
            c.check(owner_set(pg, P, {'chatId': C['chat4']}) == [] and owner_set(pg, P, []) == [], 'not an array → no rows')

    with Case(f'number_change_voids_defaults_keeps_owner [{ch}]: another number makes the computed rows stop holding, the '
              'owner\'s holds; a new computed decision then replaces the stale row') as c:
        with PgSession(SETUP) as pg:
            profile(pg, P, C['channel'], '3 days')
            first = connected(pg, P, '77010000001')[0]
            first_moment = first['connected_at_ms']
            c.check(first_moment == created_ms(pg, P) and first['restarted'] is False,
                    f'the first connection takes the line\'s moment, nothing restarted: {first}')
            decide(pg, P, C['chat'], 'business', 'new')
            owner_set(pg, P, [{'chatId': C['chat2'], 'kind': 'personal', 'version': 100}])
            decide(pg, P, C['chat3'], 'personal', 'contact')
            c.check((audience(pg, P, C['chat']), audience(pg, P, C['chat2']), audience(pg, P, C['chat3']))
                    == ('business', 'personal', 'personal'), 'before the number change')
            owner_row = row(pg, P, C['chat2'])
            tick(pg)
            changed = connected(pg, P, '77010000002')[0]
            moment = changed['connected_at_ms']
            c.check(moment > first_moment and moment <= now_ms(pg) and changed['restarted'] is True,
                    f'another number restarts the moment and says so: {first_moment} → {changed}')
            tick(pg)
            c.check(audience(pg, P, C['chat']) is None and business(pg, P, C['chat']) is False,
                    'a computed decision of the old number must not hold')
            c.check(audience(pg, P, C['chat3']) is None, 'the old number\'s personal default must not hold either')
            c.check(audience(pg, P, C['chat2']) == 'personal' and row(pg, P, C['chat2']) == owner_row, 'the owner\'s holds, untouched')
            l = listed(pg, [P])
            c.check(l and [d['chatId'] for d in l[0]['decisions']] == [C['chat2']], f'LIST names only what holds: {l}')
            again = decide(pg, P, C['chat'], 'personal', 'history')
            c.check(again == [dec('personal', 'history', 'default')], f'the stale row must be replaced: {again}')
            fresh = pg.q(f"select a.decided_at >= c.connected_at as ok from chat_audience a join audience_connections c "
                         f"using (profile_id) where a.profile_id = {L(P)} and a.chat_id = {L(C['chat'])}")
            c.check(fresh == [{'ok': True}] and audience(pg, P, C['chat']) == 'personal', f'the new decision holds: {fresh}')
            c.check(decide(pg, P, C['chat3'], 'business', 'new') == [dec('business', 'new', 'default')], 'stale contact replaced')
            l = listed(pg, [P])
            c.check(l and sorted(d['chatId'] for d in l[0]['decisions']) == sorted([C['chat'], C['chat2'], C['chat3']]),
                    f'everything holds again: {l}')

    with Case(f'same_or_empty_phone_keeps_the_moment [{ch}]: CONNECTED_SQL fills a missing number and keeps the moment; the '
              'same or an empty number changes nothing; another number restarts it') as c:
        with PgSession(SETUP) as pg:
            profile(pg, P, C['channel'], '3 days')
            moment = created_ms(pg, P)
            ensure(pg, P)
            c.check(conn_row(pg, P)['phone'] is None and conn_row(pg, P)['ms'] == moment, f'ensured: {conn_row(pg, P)}')
            tick(pg)
            got = connected(pg, P, '+7 (701) 111-22-33')
            filled = conn_row(pg, P)
            c.check(got == [{'connected_at_ms': moment, 'restarted': False}] and filled['phone'] == '77011112233'
                    and filled['ms'] == moment, f'a missing number is filled in, the moment kept: {got} {filled}')
            for same in ('77011112233', '7 701 111 22 33', '', None):
                tick(pg)
                got = connected(pg, P, same)
                c.check(got == [{'connected_at_ms': moment, 'restarted': False}] and conn_row(pg, P) == filled,
                        f'{same!r} must change nothing: {got} {conn_row(pg, P)}')
            Q = f'{ch.lower()}-aud-q1'
            profile(pg, Q, C['channel'], '2 days')
            got = connected(pg, Q, '77012223344')
            c.check(got == [{'connected_at_ms': created_ms(pg, Q), 'restarted': False}] and conn_row(pg, Q)['phone'] == '77012223344',
                    f'no row → the line\'s moment and the number: {got} {conn_row(pg, Q)}')
            tick(pg)
            got = connected(pg, P, '77019998877')
            c.check(got[0]['connected_at_ms'] > moment and got[0]['restarted'] is True and conn_row(pg, P)['phone'] == '77019998877',
                    f'another number: {got} {conn_row(pg, P)}')
            c.check(connected(pg, '', '77019998877') == [{'connected_at_ms': None, 'restarted': False}]
                    and connected(pg, '-1', '7701') == [{'connected_at_ms': None, 'restarted': False}]
                    and n_rows(pg, 'audience_connections', '-1') == 0,
                    'a sentinel profile: one row of nulls (restarted false), nothing written')

    with Case(f'connected_says_restarted_only_when_it_moved_the_moment [{ch}]: true exactly when this statement moved '
              'connected_at — another KNOWN number on an existing row; a first row (with or without a number), a filled-in '
              'number, the same digits, an empty or missing number: false, and a repeat of the new number: false') as c:
        with PgSession(SETUP) as pg:
            R = f'{ch.lower()}-aud-r1'
            profile(pg, R, C['channel'], '4 days')
            seen = [connected(pg, R, None)[0]]                         # a first row without a number
            tick(pg)
            seen.append(connected(pg, R, '+7 701 000 00 01')[0])     # the number filled in
            tick(pg)
            seen.append(connected(pg, R, '77010000001')[0])          # the same digits
            tick(pg)
            seen.append(connected(pg, R, '')[0])                     # none given
            tick(pg)
            seen.append(connected(pg, R, '77020000002')[0])          # another number: the moment moves
            tick(pg)
            seen.append(connected(pg, R, '+7 702 000 00 02')[0])     # the new number again
            c.check([s['restarted'] for s in seen] == [False, False, False, False, True, False], f'restarted: {seen}')
            c.check(len({s['connected_at_ms'] for s in seen[:4]}) == 1 and seen[4]['connected_at_ms'] > seen[0]['connected_at_ms']
                    and seen[5]['connected_at_ms'] == seen[4]['connected_at_ms'], f'the moments: {seen}')
            S = f'{ch.lower()}-aud-s1'
            profile(pg, S, C['channel'], '1 day')
            c.check(connected(pg, S, '77030000003')[0]['restarted'] is False, 'a first row WITH a number is not a restart')

with Case('owner_set_refuses_nonhuman: by suffix (@g.us, @newsletter, @broadcast), by 777000 and by a recorded reason — '
          'never applied, never written; the person in the same request is applied') as c:
    for ch, nonhuman_ids, recorded, person in (
            ('WA', ['120363000000000001@g.us', '120363000000000002@newsletter', 'status@broadcast'], '77015550000@c.us', '77016660000@c.us'),
            ('TG', ['777000'], '5000000001', '5000000002')):
        P = f'{ch.lower()}-aud-nonhuman'
        with PgSession(SETUP) as pg:
            decide(pg, P, recorded, 'personal', 'nonhuman')
            rec_row = row(pg, P, recorded)
            asked = nonhuman_ids + [recorded, person]
            got = owner_set(pg, P, [{'chatId': x, 'kind': 'business', 'version': 10} for x in asked])
            c.check([r['chat_id'] for r in got] == asked, f'[{ch}] one answer per chat in the request order: {got}')
            for r in got:
                if r['chat_id'] == person:
                    c.check(r['applied'] is True and r['refused'] is None and r['became_business'] is True, f'[{ch}] the person: {r}')
                else:
                    c.check((r['applied'], r['refused'], r['kind'], r['became_business']) == (False, 'nonhuman', 'personal', False),
                            f'[{ch}] refused: {r}')
            c.check(all(row(pg, P, x) is None for x in nonhuman_ids), f'[{ch}] nothing written for a not-a-person id')
            c.check(row(pg, P, recorded) == rec_row, f'[{ch}] the recorded row moved: {rec_row} → {row(pg, P, recorded)}')
            got = owner_set(pg, P, [{'chatId': x, 'kind': 'personal', 'version': 11} for x in nonhuman_ids + [recorded]])
            c.check(all(r['refused'] == 'nonhuman' and r['applied'] is False for r in got)
                    and all(row(pg, P, x) is None for x in nonhuman_ids) and row(pg, P, recorded) == rec_row,
                    f'[{ch}] «personal» is refused too: the owner does not move a non-person at all: {got}')

for ch, C in CH.items():
    P = f'{ch.lower()}-aud-p1'
    with Case(f'personal_since_rules [{ch}]: business → personal stamps now, personal → personal keeps, → business clears; '
              'wiped_at clears whenever personal_since changes and when the chat turns business') as c:
        with PgSession(SETUP) as pg:
            chat = C['chat']
            owner_set(pg, P, [{'chatId': chat, 'kind': 'personal', 'version': 1}])
            r1 = row(pg, P, chat)
            c.check(r1['personal_since'] is not None and r1['wiped_at'] is None, f'no row → personal: {r1}')
            pg.x(f"update chat_audience set wiped_at = now() where profile_id = {L(P)} and chat_id = {L(chat)};")   # the sweep ran
            wiped = row(pg, P, chat)['wiped_at']
            tick(pg)
            owner_set(pg, P, [{'chatId': chat, 'kind': 'personal', 'version': 2}])
            r2 = row(pg, P, chat)
            c.check(r2['personal_since'] == r1['personal_since'] and r2['wiped_at'] == wiped and wiped is not None,
                    f'personal → personal keeps both: {r1} → {r2}')
            tick(pg)
            owner_set(pg, P, [{'chatId': chat, 'kind': 'business', 'version': 3}])
            r3 = row(pg, P, chat)
            c.check(r3['personal_since'] is None and r3['wiped_at'] is None, f'→ business clears both: {r3}')
            tick(pg)
            owner_set(pg, P, [{'chatId': chat, 'kind': 'personal', 'version': 4}])
            r4 = row(pg, P, chat)
            c.check(r4['personal_since'] is not None and r4['personal_since'] > r1['personal_since'] and r4['wiped_at'] is None,
                    f'business → personal stamps a new moment: {r1} → {r4}')
            decide(pg, P, C['chat2'], 'business', 'new')
            owner_set(pg, P, [{'chatId': C['chat2'], 'kind': 'personal', 'version': 1}])
            c.check(row(pg, P, C['chat2'])['personal_since'] is not None, 'a computed business → owner personal stamps now')
            decide(pg, P, C['chat3'], 'personal', 'contact')
            owner_set(pg, P, [{'chatId': C['chat3'], 'kind': 'personal', 'version': 1}])
            r5 = row(pg, P, C['chat3'])
            c.check(r5['source'] == 'owner' and r5['personal_since'] is None and r5['wiped_at'] is None,
                    f'a computed personal → owner personal keeps it empty (nothing was ever stored): {r5}')

    with Case(f'owner_set_became_business [{ch}]: true only for an APPLIED business whose previous holding kind was not '
              'business (no row and a row that no longer holds count as not business)') as c:
        with PgSession(SETUP) as pg:
            profile(pg, P, C['channel'], '3 days')
            connected(pg, P, '77010000001')
            decide(pg, P, C['chat3'], 'business', 'new')
            decide(pg, P, C['chat4'], 'personal', 'history')
            tick(pg)
            connected(pg, P, '77010000002')                        # chat3 / chat4's computed rows stop holding
            tick(pg)
            decide(pg, P, C['chat2'], 'personal', 'history')       # taken on the new number: holds
            owner_set(pg, P, [{'chatId': C['chat5'], 'kind': 'business', 'version': 1}])
            got = {r['chat_id']: r for r in owner_set(pg, P, [
                {'chatId': C['chat'], 'kind': 'business', 'version': 10},      # no row
                {'chatId': C['chat2'], 'kind': 'business', 'version': 10},     # a holding computed personal
                {'chatId': C['chat3'], 'kind': 'business', 'version': 10},     # a computed business of the old number: stale
                {'chatId': C['chat4'], 'kind': 'business', 'version': 10},     # a computed personal of the old number: stale
                {'chatId': C['chat5'], 'kind': 'business', 'version': 10}])}   # the owner's business
            c.check(got[C['chat']]['became_business'] is True, f'no row → business: {got[C["chat"]]}')
            c.check(got[C['chat2']]['became_business'] is True, f'holding computed personal → business: {got[C["chat2"]]}')
            c.check(got[C['chat3']]['became_business'] is True and got[C['chat4']]['became_business'] is True,
                    f'a computed row that no longer holds → business: {got[C["chat3"]]} {got[C["chat4"]]}')
            c.check(got[C['chat5']]['applied'] is True and got[C['chat5']]['became_business'] is False,
                    f'owner business → owner business: {got[C["chat5"]]}')
        with PgSession(SETUP) as pg:
            decide(pg, P, C['chat'], 'business', 'new')                        # holding computed business
            owner_set(pg, P, [{'chatId': C['chat2'], 'kind': 'personal', 'version': 1}])
            got = {r['chat_id']: r for r in owner_set(pg, P, [
                {'chatId': C['chat'], 'kind': 'business', 'version': 10},
                {'chatId': C['chat2'], 'kind': 'business', 'version': 10}])}
            c.check(got[C['chat']]['applied'] is True and got[C['chat']]['became_business'] is False,
                    f'a HOLDING computed business → owner business: {got[C["chat"]]}')
            c.check(got[C['chat2']]['became_business'] is True, f'owner personal → business: {got[C["chat2"]]}')
            stale = owner_set(pg, P, [{'chatId': C['chat3'], 'kind': 'business', 'version': 5},
                                      {'chatId': C['chat3'], 'kind': 'business', 'version': 5}])
            refused = owner_set(pg, P, [{'chatId': C['chat3'], 'kind': 'business', 'version': 4}])
            c.check(stale[0]['became_business'] is True and refused[0]['applied'] is False and refused[0]['became_business'] is False,
                    f'not applied → never became business: {stale} {refused}')

    with Case(f'line_moment_follows_superseded_by [{ch}]: the earliest created_at along the chain of profiles that this '
              'profile superseded (same channel); now() without a row; a cycle terminates') as c:
        with PgSession(SETUP) as pg:
            other = 'telegram' if C['channel'] == 'whatsapp' else 'whatsapp'
            p1, p2, p3 = f'{ch}-line-1', f'{ch}-line-2', f'{ch}-line-3'
            profile(pg, p1, C['channel'], '5 days', superseded_by=p2)
            profile(pg, p2, C['channel'], '3 days', superseded_by=p3)
            profile(pg, p3, C['channel'], '1 day')
            profile(pg, f'{ch}-line-other-channel', other, '20 days', superseded_by=p3)     # never the same channel's line
            profile(pg, f'{ch}-unrelated', C['channel'], '30 days', superseded_by=f'{ch}-unrelated-next')
            m1 = created_ms(pg, p1)
            c.check((line_ms(pg, p3), line_ms(pg, p2), line_ms(pg, p1)) == (m1, m1, m1),
                    f'the line\'s moment: {line_ms(pg, p3)} {line_ms(pg, p2)} {line_ms(pg, p1)} vs {m1}')
            t0 = now_ms(pg)
            nobody = line_ms(pg, f'{ch}-nobody')
            c.check(abs(nobody - t0) < 60000, f'no row → now(): {nobody} vs {t0}')
            ca, cb = f'{ch}-cycle-a', f'{ch}-cycle-b'
            profile(pg, ca, C['channel'], '7 days', superseded_by=cb)
            profile(pg, cb, C['channel'], '6 days', superseded_by=ca)
            c.check(line_ms(pg, cb) == created_ms(pg, ca), 'a cycle terminates and still answers the earliest')
            c.check(ensure(pg, p3) == [{'connected_at_ms': m1, 'phone': None}], f'ENSURE takes the line\'s moment: {ensure(pg, p3)}')
            c.check(connected(pg, p2, '77010000009') == [{'connected_at_ms': m1, 'restarted': False}],
                    'CONNECTED on a fresh row takes it too')

    with Case(f'ensure_connection_always_one_row [{ch}]: created once with the line\'s moment and no number, an existing row '
              'comes back unchanged, a sentinel profile answers one row of nulls') as c:
        with PgSession(SETUP) as pg:
            profile(pg, P, C['channel'], '4 days')
            got = ensure(pg, P)
            c.check(got == [{'connected_at_ms': created_ms(pg, P), 'phone': None}], f'created: {got}')
            before = conn_row(pg, P)
            tick(pg)
            again = ensure(pg, P)
            c.check(again == got and conn_row(pg, P) == before, f'an existing row comes back unchanged: {again} {conn_row(pg, P)}')
            connected(pg, P, '77013334455')
            c.check(ensure(pg, P) == [{'connected_at_ms': created_ms(pg, P), 'phone': '77013334455'}], 'ENSURE hands the number back')
            tick(pg)
            connected(pg, P, '77015556677')
            c.check(ensure(pg, P)[0]['connected_at_ms'] > created_ms(pg, P), 'ENSURE never moves a moment back')
            for sentinel in ('', '-1', None):
                c.check(ensure(pg, sentinel) == [{'connected_at_ms': None, 'phone': None}], f'{sentinel!r}: one row of nulls')
            c.check(n_rows(pg, 'audience_connections', '-1') == 0 and n_rows(pg, 'audience_connections', '') == 0, 'nothing written')

with Case('list_returns_only_valid_decisions_and_the_moment: every profile asked, once, in the order asked; the moment (null '
          'without a row); only the decisions that hold, as {chatId, kind, source, reason, version}') as c:
    with PgSession(SETUP) as pg:
        p1, p2 = 'wa-list-1', 'tg-list-2'
        profile(pg, p1, 'whatsapp', '3 days')
        profile(pg, p2, 'telegram', '2 days')
        connected(pg, p1, '77010000001')
        decide(pg, p1, 'b@c.us', 'business', 'new')
        decide(pg, p1, 'a@c.us', 'personal', 'contact')
        owner_set(pg, p1, [{'chatId': 'c@c.us', 'kind': 'business', 'version': 77}])
        tick(pg)
        connected(pg, p1, '77010000002')                     # the two computed rows stop holding
        tick(pg)
        decide(pg, p1, 'd@c.us', 'personal', 'history')      # holds
        decide(pg, p2, '5550001', 'business', 'new')         # p2 has no connection row: every computed row holds
        got = listed(pg, [p1, 'unknown-profile', p1, '', '-1', 7, None, p2])
        c.check([r['profile_id'] for r in got] == [p1, 'unknown-profile', p2], f'the profiles: {[r["profile_id"] for r in got]}')
        by = {r['profile_id']: r for r in got}
        c.check(by[p1]['connected_at_ms'] == conn_row(pg, p1)['ms'] and by['unknown-profile']['connected_at_ms'] is None
                and by[p2]['connected_at_ms'] is None, f'the moments: {got}')
        c.check(by[p1]['decisions'] == [{'chatId': 'c@c.us', 'kind': 'business', 'source': 'owner', 'reason': 'owner', 'version': 77},
                                        {'chatId': 'd@c.us', 'kind': 'personal', 'source': 'default', 'reason': 'history', 'version': 0}],
                f'p1 decisions: {by[p1]["decisions"]}')
        c.check(by['unknown-profile']['decisions'] == [] and by[p2]['decisions'] == [
            {'chatId': '5550001', 'kind': 'business', 'source': 'default', 'reason': 'new', 'version': 0}], f'{got}')
        c.check(listed(pg, {'a': 1}) == [] and listed(pg, []) == [], 'not an array → no rows')


# ================================================================================================ backfill
def wappi_page(trigger_time=1006):
    """A messages/get page exactly as Wappi returns it with order=desc — NEWEST FIRST (BACKFILL_SQL's input contract),
    already in the {id, fromMe, body, type, time} shape. The ids sort neither like the page nor like its reverse, so only
    storing the page reversed puts the rows in the conversation's order: b1 · m2 (recorded) · k3 · z4 · a5 · m6 (the
    trigger)."""
    t = trigger_time
    return [
        {'id': 'm6', 'fromMe': False, 'body': 'а на завтра будет?', 'type': 'chat', 'time': t},
        {'id': 'a5', 'fromMe': True, 'body': '/9j/4AAQSkZJRgABAQ-base64-payload', 'type': 'image', 'caption': 'прайс', 'time': t - 1},
        {'id': 'z4', 'fromMe': False, 'body': 'есть розы?', 'type': 'chat', 'time': t - 2},
        {'id': 'k3', 'fromMe': True, 'body': 'Здравствуйте! Чем помочь?', 'type': 'chat', 'time': t - 3},
        {'id': 'm2', 'fromMe': True, 'body': 'Ответ ИИ', 'type': 'chat', 'time': t - 4},
        {'id': 'b1', 'fromMe': False, 'body': 'привет', 'type': 'chat', 'time': t - 5},
        {'fromMe': False, 'body': 'без id', 'type': 'chat', 'time': t - 6},
        {'id': 'c0', 'body': 'без fromMe', 'type': 'chat', 'time': t - 7},
        'not-an-object',
    ]


def stored_order(pg, pid, chat):
    """The message ids in the order the database holds them: the history by its id, the memory by its id (what the memory
    node reads) — both must follow the conversation."""
    return ([r['message_id'] for r in history(pg, pid, chat)],
            [m['message']['additional_kwargs']['message_id'] for m in memory(pg, pid, chat)])


def history(pg, pid, chat):
    return pg.q(f"select message_id, source, body, msg_type, extract(epoch from sent_at)::bigint as t from chat_history_events "
                f"where profile_id = {L(pid)} and chat_id = {L(chat)} order by id")


def memory(pg, pid, chat):
    return pg.q(f"select id, message from n8n_chat_histories where session_id = {L(pid + ':' + chat)} order by id")


for ch, C in CH.items():
    P = f'{ch.lower()}-aud-p1'
    with Case(f'backfill_adds_missing_in_time_order_with_owner_mark [{ch}]: the trigger and every id already in the chat\'s '
              'history are skipped; the rest goes in oldest first — the newest-first page reversed, by position, never by '
              'time — owner rows with «Владелец: », both rows carry source + message_id') as c:
        with PgSession(SETUP) as pg:
            chat = C['chat']
            decide(pg, P, chat, 'business', 'new')
            pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at) "
                 f"values ({L(P)}, {L(chat)}, 'm2', 'ai', 'Ответ ИИ', 'chat', to_timestamp(1002));")
            got = backfill(pg, P, chat, 'm6', wappi_page())
            c.check(got == [{'added': 4}], f'added: {got}')
            h = history(pg, P, chat)
            c.check([(r['message_id'], r['source'], r['body'], r['t']) for r in h] == [
                ('m2', 'ai', 'Ответ ИИ', 1002), ('b1', 'client', 'привет', 1001),
                ('k3', 'owner_phone', 'Здравствуйте! Чем помочь?', 1003), ('z4', 'client', 'есть розы?', 1004),
                ('a5', 'owner_phone', '[image] прайс', 1005)], f'history (by id = write order): {h}')
            mem = memory(pg, P, chat)
            c.check([(m['message']['type'], m['message']['content'], m['message']['additional_kwargs']) for m in mem] == [
                ('human', 'привет', {'source': 'client', 'message_id': 'b1'}),
                ('ai', 'Владелец: Здравствуйте! Чем помочь?', {'source': 'owner_phone', 'message_id': 'k3'}),
                ('human', 'есть розы?', {'source': 'client', 'message_id': 'z4'}),
                ('ai', 'Владелец: [image] прайс', {'source': 'owner_phone', 'message_id': 'a5'})], f'memory (by id): {mem}')
            c.check(all(m['message'].get('response_metadata') == {} for m in mem), 'response_metadata')
            c.check(all('m6' not in json.dumps(x, ensure_ascii=False) for x in (h, mem)), 'the trigger must not be written')
            c.check(not any('base64' in json.dumps(x, ensure_ascii=False) for x in (h, mem)), 'a media payload must never be stored')
            c.check(backfill(pg, P, chat, 'm6', wappi_page()) == [{'added': 0}] and len(history(pg, P, chat)) == 5
                    and len(memory(pg, P, chat)) == 4, 'a second run adds nothing')
            # the SAME form the live writers store: Record Suppressed (a client row in «Вместе») and Record Outgoing (the
            # owner from the phone) — their rows next to the backfilled ones carry the same keys, roles and marks
            supp = AHH.RECORD_SUPPRESSED_SQL
            pg.q(bind(supp, [P, chat, 'm9', 'клиент в «Вместе»', 'chat', 1009, 'exec-1', False]))
            pg.q(bind(AHH.RECORD_OUTGOING_SQL, [P, chat, 'm10', 'владелец с телефона', 'chat', 1010, 'outgoing_message_phone', 'exec-2']))
            by_id = {m['message']['additional_kwargs']['message_id']: m['message'] for m in memory(pg, P, chat)}
            shape = lambda m: (m['type'], sorted(m), sorted(m['additional_kwargs']), m['additional_kwargs']['source'], m['response_metadata'])
            c.check(shape(by_id['b1']) == shape(by_id['m9']), f'client rows differ from Record Suppressed: {by_id["b1"]} / {by_id["m9"]}')
            c.check(shape(by_id['k3']) == shape(by_id['m10']) and by_id['m10']['content'].startswith(AHH.OWNER_MEMORY_PREFIX)
                    and by_id['k3']['content'].startswith(AHH.OWNER_MEMORY_PREFIX),
                    f'owner rows differ from Record Outgoing: {by_id["k3"]} / {by_id["m10"]}')
        with PgSession(SETUP) as pg:
            chat = C['chat2']
            decide(pg, P, chat, 'business', 'new')
            # a same-second burst on a newest-first page: Wappi's order breaks the tie, so «первое» (the oldest, last on the
            # page) goes first — a time sort would keep the page's order inside the second and put «второе» before it
            page = [{'id': 'x3', 'fromMe': True, 'body': 'третье', 'type': 'chat', 'time': 2002},
                    {'id': 'x2', 'fromMe': False, 'body': 'второе', 'type': 'chat', 'time': 2001},
                    {'id': 'x9', 'fromMe': False, 'body': 'первое', 'type': 'chat', 'time': '2001'},
                    {'id': 'x9', 'fromMe': False, 'body': 'первое (повтор)', 'type': 'chat', 'time': 2001}]
            got = backfill(pg, P, chat, None, page)
            c.check(got == [{'added': 3}] and [(r['message_id'], r['body']) for r in history(pg, P, chat)]
                    == [('x9', 'первое'), ('x2', 'второе'), ('x3', 'третье')],
                    f'no trigger: everything new goes in, a repeated id once, a same-second burst oldest first: '
                    f'{got} {history(pg, P, chat)}')
            c.check(stored_order(pg, P, chat) == (['x9', 'x2', 'x3'], ['x9', 'x2', 'x3']),
                    f'history ids and memory ids follow the conversation: {stored_order(pg, P, chat)}')
        with PgSession(SETUP) as pg:
            chat = C['chat3']
            decide(pg, P, chat, 'business', 'new')
            # messages whose time is missing or does not parse sit in the MIDDLE of the page: they keep their place (a
            # time sort would move them to the front) and their sent_at stays empty
            page = [{'id': 'y4', 'fromMe': False, 'body': 'четвёртое', 'type': 'chat', 'time': 3004},
                    {'id': 'y3', 'fromMe': True, 'body': 'третье', 'type': 'chat'},
                    {'id': 'y2', 'fromMe': False, 'body': 'второе', 'type': 'chat', 'time': 'вчера'},
                    {'id': 'y1', 'fromMe': True, 'body': 'первое', 'type': 'chat', 'time': 3001}]
            got = backfill(pg, P, chat, None, page)
            h = history(pg, P, chat)
            c.check(got == [{'added': 4}] and [(r['message_id'], r['source'], r['t']) for r in h]
                    == [('y1', 'owner_phone', 3001), ('y2', 'client', None), ('y3', 'owner_phone', None), ('y4', 'client', 3004)],
                    f'a message without a parseable time keeps its page position: {got} {h}')
            c.check(stored_order(pg, P, chat) == (['y1', 'y2', 'y3', 'y4'], ['y1', 'y2', 'y3', 'y4']),
                    f'history ids and memory ids follow the conversation: {stored_order(pg, P, chat)}')
            # the parameter is a JSON text (the generators JSON.stringify it): a JSON scalar or object is no array of messages
            c.check(backfill(pg, P, chat, 'x9', json.dumps('not-an-array')) == [{'added': 0}]
                    and backfill(pg, P, chat, 'x9', {'id': 'x3'}) == [{'added': 0}], 'not an array → nothing')

    with Case(f'backfill_writes_nothing_for_a_personal_or_undecided_chat [{ch}]') as c:
        with PgSession(SETUP) as pg:
            profile(pg, P, C['channel'], '3 days')
            connected(pg, P, '77010000001')
            decide(pg, P, C['chat'], 'personal', 'contact')
            owner_set(pg, P, [{'chatId': C['chat3'], 'kind': 'personal', 'version': 1}])
            decide(pg, P, C['chat4'], 'business', 'new')
            tick(pg)
            connected(pg, P, '77010000002')                  # chat4's computed business no longer holds
            tick(pg)
            for chat, label in ((C['chat'], 'computed personal'), (C['chat2'], 'undecided'), (C['chat3'], 'owner personal'),
                                (C['chat4'], 'stale computed business')):
                got = backfill(pg, P, chat, 'm6', wappi_page())
                c.check(got == [{'added': 0}] and history(pg, P, chat) == [] and memory(pg, P, chat) == [],
                        f'{label}: {got} {history(pg, P, chat)} {memory(pg, P, chat)}')
            group = '120363000000000001@g.us' if ch == 'WA' else '777000'
            decide(pg, P, group, 'personal', 'nonhuman')
            c.check(backfill(pg, P, group, None, wappi_page()) == [{'added': 0}] and history(pg, P, group) == [], 'not a person')


# ================================================================================================ the migration
def block_ddl():
    """The migration's OWN table block, aimed at session temp tables (the DDL that ships)."""
    text = MIGRATION.read_text(encoding='utf-8')
    block = text.split('-- TABLE-BEGIN')[1].split('\n', 1)[1].split('-- TABLE-END')[0]
    return block.replace('create table if not exists public.', 'create temp table ').replace(' on public.', ' on ')


SHAPE_SQL = """select json_build_object(
  'columns', (select json_agg(json_build_array(a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull,
                                               pg_get_expr(d.adbin, d.adrelid)) order by a.attnum)
              from pg_attribute a left join pg_attrdef d on d.adrelid = a.attrelid and d.adnum = a.attnum
              where a.attrelid = '{rel}'::regclass and a.attnum > 0 and not a.attisdropped),
  'constraints', (select json_agg(pg_get_constraintdef(oid) order by pg_get_constraintdef(oid)) from pg_constraint
                  where conrelid = '{rel}'::regclass),
  'indexes', (select json_agg(x.d order by x.d) from (select regexp_replace(pg_get_indexdef(indexrelid), ' ON [a-z_0-9]+\\.', ' ON ') as d
              from pg_index where indrelid = '{rel}'::regclass) x)) as shape"""

with Case('migration_postchecks: RLS on, no grants to anon/authenticated, the staging tables carry the file\'s DDL, the '
          'reply_events CHECK takes cancelled_personal and still refuses bogus, a re-run changes nothing, pg.sh migrates it') as c:
    with PgSession(block_ddl()) as pg:
        for t in ('chat_audience', 'audience_connections'):
            temp = pg.q(SHAPE_SQL.format(rel=f'pg_temp.{t}'))[0]['shape']
            pub = pg.q(SHAPE_SQL.format(rel=f'public.{t}'))[0]['shape']
            c.check(temp == pub, f'public.{t} differs from the migration\'s DDL (stale staging table?): file {temp} / staging {pub}')
        rls = pg.q("select relname, relrowsecurity from pg_class where oid in ('public.chat_audience'::regclass, "
                   "'public.audience_connections'::regclass) order by relname")
        c.check(rls == [{'relname': 'audience_connections', 'relrowsecurity': True}, {'relname': 'chat_audience', 'relrowsecurity': True}],
                f'RLS: {rls}')
        pol = pg.q("select count(*)::int as n from pg_policies where tablename in ('chat_audience', 'audience_connections')")
        c.check(pol == [{'n': 0}], f'deny-all = no policy: {pol}')
        grants = pg.q("select grantee, table_name, privilege_type from information_schema.role_table_grants where table_schema = "
                      "'public' and table_name in ('chat_audience', 'audience_connections') and grantee in ('anon', 'authenticated')")
        c.check(grants == [], f'client roles must hold nothing: {grants}')
        priv = pg.q("select bool_or(has_table_privilege(r, t, p)) as any from unnest(array['anon', 'authenticated']) r, "
                    "unnest(array['public.chat_audience', 'public.audience_connections']) t, "
                    "unnest(array['select', 'insert', 'update', 'delete']) p")
        c.check(priv == [{'any': False}], f'has_table_privilege: {priv}')
        idx = pg.q("select pg_get_indexdef(indexrelid) as d from pg_index where indrelid = 'public.chat_audience'::regclass "
                   "and not indisprimary")
        c.check(len(idx) == 1 and '(personal_since)' in idx[0]['d'] and "source = 'owner'" in idx[0]['d']
                and "kind = 'personal'" in idx[0]['d'] and 'wiped_at IS NULL' in idx[0]['d'], f'the wipe index: {idx}')
        chk = pg.q("select conname, pg_get_constraintdef(oid) as d from pg_constraint where conrelid = 'public.reply_events'::regclass "
                   "and contype = 'c'")
        c.check(len(chk) == 1 and chk[0]['conname'] == 'reply_events_state_check'
                and all(f"'{s}'" in chk[0]['d'] for s in OLD_STATES + ('cancelled_personal',)), f'reply_events CHECK: {chk}')
        pg.x("create temp table reply_events (like public.reply_events including all); create temp table aud_err (msg text);")
        pg.x("insert into reply_events (profile_id, chat_id, message_id, state) values ('p', 'c', 'm1', 'cancelled_personal');")
        pg.x("do $do$ begin insert into reply_events (profile_id, chat_id, message_id, state) values ('p', 'c', 'm2', 'bogus'); "
             "insert into aud_err values ('accepted'); exception when check_violation then insert into aud_err values ('refused'); "
             "end $do$;")
        c.check(pg.q("select state from reply_events order by message_id") == [{'state': 'cancelled_personal'}]
                and pg.q("select msg from aud_err") == [{'msg': 'refused'}], 'reply_events: cancelled_personal in, bogus out')
        pg.x("do $do$ begin insert into chat_audience (profile_id, chat_id, kind, source, reason) values ('p', 'c', 'neither', 'owner', 'owner'); "
             "exception when check_violation then insert into aud_err values ('kind'); end $do$;")
        pg.x("do $do$ begin insert into chat_audience (profile_id, chat_id, kind, source, reason) values ('p', 'c', 'personal', 'app', 'owner'); "
             "exception when check_violation then insert into aud_err values ('source'); end $do$;")
        pg.x("do $do$ begin insert into chat_audience (profile_id, chat_id, kind, source, reason) values ('p', 'c', 'personal', 'owner', 'unknown'); "
             "exception when check_violation then insert into aud_err values ('reason'); end $do$;")
        pg.x("do $do$ begin insert into chat_audience (profile_id, chat_id, source, reason) values ('p', 'c', 'owner', 'owner'); "
             "exception when not_null_violation then insert into aud_err values ('kind null'); end $do$;")
        c.check(sorted(r['msg'] for r in pg.q("select msg from aud_err")) == ['kind', 'kind null', 'reason', 'refused', 'source'],
                f'the chat_audience checks: {pg.q("select msg from aud_err")}')
    snap = ("select json_build_object('oid', (select oid from pg_constraint where conrelid = 'public.reply_events'::regclass and "
            "conname = 'reply_events_state_check'), 'tables', (select json_agg(c.oid order by c.relname) from pg_class c where "
            "c.oid in ('public.chat_audience'::regclass, 'public.audience_connections'::regclass)))::text;\n")
    out = sql_run(snap + MIGRATION.read_text(encoding='utf-8') + '\n' + snap)          # inside BEGIN … ROLLBACK
    marks = [json.loads(l) for l in out.splitlines() if l.strip().startswith('{')]
    c.check(len(marks) == 2 and marks[0] == marks[1], f'a re-run must change nothing (the CHECK not re-created): {marks}')
    pg_text = PG_SH.read_text(encoding='utf-8')
    c.check(MIGRATION.name in pg_text and pg_text.index(MIGRATION.name) > pg_text.index('2026-10-14-bot-schedule.sql'),
            'staging/pg.sh must migrate the file after the bot schedule')


# ================================================================================================ guards
with Case('statements_pass_the_sql_guard: verify-sql-comments finds nothing in any statement or fragment (V1: no $ + digit '
          'in a comment or literal, no {{, no pg-promise modifier, no leading =)') as c:
    stmts = {'ENSURE_CONNECTION_SQL': AUD.ENSURE_CONNECTION_SQL, 'DECIDE_WRITE_SQL': AUD.DECIDE_WRITE_SQL,
             'OWNER_SET_SQL': AUD.OWNER_SET_SQL, 'CONNECTED_SQL': AUD.CONNECTED_SQL, 'LIST_SQL': AUD.LIST_SQL,
             'BACKFILL_SQL': backfill_sql(), 'AUDIENCE_SQL': 'select ' + AUD.AUDIENCE_SQL(),
             'BUSINESS_SQL': 'select ' + AUD.BUSINESS_SQL(), 'LINE_CONNECTED_AT_SQL': 'select ' + AUD.LINE_CONNECTED_AT_SQL(),
             'VALID_SQL': 'select 1 from chat_audience a, audience_connections c where ' + AUD.VALID_SQL()}
    for name, q in stmts.items():
        found = VSC.check_query(q)
        c.check(not found, f'{name}: {found}')
        c.check(isinstance(q, str) and not q.lstrip().startswith('='), f'{name} must be a plain query string')
    c.check(AUD.VALID_SQL() == "(a.source = 'owner' or a.decided_at >= coalesce(c.connected_at, '-infinity'))", AUD.VALID_SQL())
    c.check(AUD.AUDIENCE_SQL("$7", "$8").count('$7') == 1 and AUD.BUSINESS_SQL("x.p", "x.c").count('x.p') == 1,
            'the fragments place the caller\'s expressions exactly once')

with Case('audience_module_never_imports_apply_history_handoff: apply-history-handoff.py imports audience.py, so an import '
          'back would be a cycle — BACKFILL_SQL takes MEMORY_ROW / OWNER_CONTENT_SQL as arguments instead') as c:
    src = AUDIENCE_PY.read_text(encoding='utf-8')
    imported = set()
    for n in ast.walk(ast.parse(src)):
        if isinstance(n, ast.Import):
            imported |= {a.name for a in n.names}
        elif isinstance(n, ast.ImportFrom):
            imported.add(n.module)
    c.check(imported <= {'json', 'os', 're'}, f'audience.py imports {sorted(imported)}')
    c.check(not re.search(r'importlib|__import__|spec_from_file_location|exec\(', src), 'no dynamic import in audience.py')

with Case('fixtures_clean_both_tables: staging/fixtures.py delete clears a fixture profile\'s rows of both tables; the mock '
          'sweep names both') as c:
    run = uuid.uuid4().hex[:12]
    uid, pid = f'probe-{run}', f'probe-aud-{run}'
    clean = (f"delete from public.chat_audience where profile_id = {L(pid)}; "
             f"delete from public.audience_connections where profile_id = {L(pid)};")
    try:
        with PgSession() as pg:
            pg.x(f"insert into public.chat_audience (profile_id, chat_id, kind, source, reason) values ({L(pid)}, 'x@c.us', 'business', 'default', 'new'); "
                 f"insert into public.audience_connections (profile_id, phone, connected_at) values ({L(pid)}, null, now());")
        r = subprocess.run([sys.executable, str(FIXTURES), 'delete', '--app-user-id', uid, '--profile-id', pid],
                           capture_output=True, text=True)
        with PgSession() as pg:
            left = pg.q(f"select (select count(*) from public.chat_audience where profile_id = {L(pid)})::int + "
                        f"(select count(*) from public.audience_connections where profile_id = {L(pid)})::int as n")
        m = re.search(r'deleted rows: (\d+)', r.stdout)
        c.check(r.returncode == 0 and left == [{'n': 0}], f'exit {r.returncode}, left {left}: {r.stdout} {r.stderr}')
        c.check(m and int(m.group(1)) >= 2, f'the two rows must be counted: {r.stdout}')
    finally:
        with PgSession() as pg:
            pg.x(clean)
    text = FIXTURES.read_text(encoding='utf-8')
    sweep = text.split('elif a.cmd == "sweep-mock-profiles":')[1].split('elif a.cmd ==')[0]
    c.check('chat_audience' in sweep and 'audience_connections' in sweep and 'to_regclass' in sweep,
            'sweep-mock-profiles must clear both tables (once the migration is applied)')

report()
