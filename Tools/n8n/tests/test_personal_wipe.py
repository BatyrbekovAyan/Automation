#!/usr/bin/env python3
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §4.6 and decision 12,
plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 8): the Profile Lifecycle Sweep's step that, TWO DAYS
after a chat that was business turned personal, deletes what the assistant remembered about that chat — and nothing else.
A chat turns personal by the owner's decision, or by the rule: after another number (or a re-pair that brings one) the
computed business row stops holding, and the next message's computed personal decision (a contact or history of the new
number) replaces it — both start the clock personal_since; the third way, the line's move on a reconnect that lands a
personal decision over a business period, starts it inside the bind (tests/test_clone_lineage_sql.py L28 / L29).

What runs — audience.WIPE_SQL, executed on REAL SQL against session temp tables of the staging Postgres (`like public.X
including all`: the migration must be applied — pg.sh migrate), the way n8n runs it (no parameters, nothing to bind); the
chats are made personal by the module's OWN statements — OWNER_SET_SQL for the owner, CONNECTED_SQL + DECIDE_WRITE_SQL for the
rule —, so the dates the wipe counts from are the ones the webhook and Decide Audience write.
Time is moved by writing personal_since into the past (a test cannot wait two days); the rule's cases only move a clock that
runs, never start one. The cases of a corrected mistaken pairing move every moment of the profile back together (`travel`):
whether a clock is the mistaken account's is read against the connection it started on. The race cases run on TWO real
connections over a uniquely named fixture schema (a temp table is
invisible to the other connection), dropped afterwards. The workflow cases read the canonical Sweep JSON and
apply-personal-wipe.py's patch on copies; the verifier runs for real. No n8n server, no network, never prod.

  not_before_two_days                       47 h 59 min is untouched, 48 h 1 min is wiped (the boundary, both sides)
  wipes_after_two_days_keeps_dialog_counts  all eight per-chat tables are emptied for the chat; the decision row stays (the
                                            chat must stay personal) with wiped_at set; dialog_counts is never touched
  returned_within_two_days_untouched        personal → business clears the clock; personal again restarts it from now
  idempotent_second_run_deletes_nothing     a wiped chat is never selected again, whatever is written for it afterwards
  personal_again_after_a_wipe_is_wiped_again   personal → wiped → business → personal re-arms the clock and the mark (the
                                            owner's write clears wiped_at when the chat turns business)
  chat_without_data_is_still_marked_done    nothing stored → wiped_chats 1, deleted_rows 0, off the work list
  other_chats_and_star_flag_untouched       another chat, a longer chat id (no prefix match), another profile's same chat id,
                                            the bot-wide '*' flag
  star_chat_row_never_touches_the_star_flag a decision row whose chat id is '*' never deletes the bot-wide flag row
  computed_clock_counts_business_never      kind 'personal' + a clock old enough counts, the owner's and a computed one alike;
                                            no clock, or a business row (whatever personal_since says), never
  number_change_then_rule_personal_wipes_the_business_period   business on the old number (data stored) → another number →
                                            the rule decides personal: the clock starts, the data stays two days, then goes;
                                            dialog_counts never (WhatsApp and Telegram)
  rearmed_by_the_rule_after_a_wipe          wiped → business by the rule (clock and mark cleared) → personal by the rule: a
                                            new clock, wiped again two days after the LAST «personal»
  a_holding_business_row_is_never_wiped     a business decision of the current connection stays business and keeps its data
  restore_spares_the_previous_numbers_client   a client of the previous number who wrote to the mistaken number was decided
                                            personal there (the clock started); the previous number paired again within
                                            RESTORE_HOURS restores the connection, and the wipe leaves him alone — two days on,
                                            a month on; his next message decides him business again (WhatsApp and Telegram)
  late_return_is_a_number_change_and_wipes  the same return after RESTORE_HOURS is another number: the clock runs, two days on
                                            his business period is wiped (WhatsApp and Telegram)
  restore_spares_only_the_mistaken_accounts_clock   after the return every other clock runs: the owner's «личный» over a
                                            chat of the mistaken period, a clock from before the mistake, a chat of the mistaken
                                            period decided personal on the restored number — and the client himself once the
                                            restored number decides him personal too
  limit_200_oldest_first                    one run wipes the 200 oldest; the rest wait for the next run
  locked_row_is_skipped_never_waited_for    the owner takes a chat back (uncommitted) while the wipe runs: the wipe skips it
                                            (no wait, no delete), still wipes the others, and finds nothing there afterwards
  connection_report_in_flight_never_holds_the_wipe   the wipe reads the connection but never locks it: a connection report
                                            in flight neither makes it wait nor makes it skip the profile's chats
  the_work_list_can_use_the_partial_index   the planner can take the batch from the migration's chat_audience_wipe_idx
  statement_shape                           no parameters, one delete per table, never dialog_counts / the line's other
                                            tables / the decision itself, the connection only read (by the restore skip),
                                            passes verify-sql-comments (V1)
  chat_tables_follow_the_line               the per-chat tables are DERIVED from clone_lineage.MOVED_TABLES (minus the
                                            per-bot rows and the decision): exactly what WIPE_SQL deletes, what the cases
                                            seed and what the verifier derives; never dialog_counts
  sweep_node_is_wired_and_independent       one node, Every 6 Hours → it directly (not behind Sweep Config / any dry-run
                                            gate), terminal, fail-open, the sweep's Postgres credential, the FIRST branch
                                            the trigger runs (n8n v1 runs the branches top-down and an unhandled error ends
                                            the run): strictly above every other branch, in Sweep Config's column
  generator_is_idempotent_and_repairs_drift patch_sweep brings a bare canon forward to the committed JSON, repairs a stale
                                            query / disabled flag / credential / onError / position (a node moved below
                                            Sweep Config, or another branch moved above it), and a second pass changes
                                            nothing
  sibling_generators_agree                  apply-learning-a.py and apply-sweep-clone-deactivate.py --check stay green
  verifier_accepts_the_canon                verify-personal-chats.py: OK (its own tripwire self-check included)
  verifier_rejects_a_broken_sweep_on_a_scratch_copy   --dir on copies of the Sweep (a dry-run gate, a disabled node, the wipe
                                            below Sweep Config, no node, no file) exits 1 with the finding; the untouched
                                            copy passes

    python3 Tools/n8n/tests/test_personal_wipe.py
"""
import copy
import json
import re
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

from harness import N8N, Case, PgSession, bind, module, node, report, sql_literal as L, wf

AUD = module('audience.py')
VSC = module('verify-sql-comments.py')
CL = module('clone_lineage.py')

SWEEP_FILE = 'fXYpCXPKw92EzRz8-Profile_Lifecycle_Sweep.json'
NODE = 'Wipe Personal Chats'
STAR = '*'

# The per-chat tables the wipe empties — the tables the bot's line moves and Delete Chat Memory clears
# (clone_lineage.MOVED_TABLES), minus the per-bot ones (bot_schedule, audience_connections) and the decision itself
# (chat_audience). Derived, never listed: a per-chat table added to the line must be wiped too, and the cases below then
# fail until WIPE_SQL deletes from it and SEEDED seeds it.
NOT_CHAT_DATA = ('bot_schedule', 'chat_audience', 'audience_connections')
CHAT_TABLES = tuple(t for t in CL.MOVED_TABLES if t not in NOT_CHAT_DATA)
# bot_profiles: CONNECTED_SQL dates a profile's first connection by its line (audience.LINE_CONNECTED_AT_SQL)
TABLES = ('chat_audience', 'audience_connections', 'dialog_counts', 'bot_profiles') + CHAT_TABLES
SERIAL = ('n8n_chat_histories', 'chat_history_events', 'handoff_requests')
# LIKE copies the PUBLIC sequence as the id default: the temp copies get their own, so nothing shared moves
SETUP = (''.join(f'create temp table {t} (like public.{t} including all);\n' for t in TABLES)
         + ''.join(f"create temp sequence {t}_wipe_seq; alter table {t} alter column id set default nextval('{t}_wipe_seq');\n"
                   for t in SERIAL))

SEEDED = {'n8n_chat_histories': 2, 'chat_history_events': 2, 'conversation_outcomes': 1, 'handoff_requests': 1,
          'reply_events': 2, 'chat_reply_state': 1, 'suggestion_sets': 1, 'reply_mode_flags': 1}
SEEDED_TOTAL = sum(SEEDED.values())
NOTHING = {t: 0 for t in SEEDED}
HOURS = AUD.WIPE_DAYS * 24
HUMAN = {'type': 'human', 'content': 'hi'}
AI = {'type': 'ai', 'content': 'hello'}


# ------------------------------------------------------------------------------------------------ seeding and reading
def reset(pg):
    pg.x(''.join(f'delete from {t};\n' for t in TABLES))


def seed(pg, pid, chat):
    """Two memory rows, two history rows, an outcome, a handoff, two ledger rows, a state, a shown set and the chat's own flag."""
    p, ch, s = L(pid), L(chat), L(f'{pid}:{chat}')
    pg.x(f"insert into n8n_chat_histories (session_id, message) values ({s}, {L(HUMAN)}), ({s}, {L(AI)});")
    pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body) "
         f"values ({p}, {ch}, 'm1', 'client', 'hi'), ({p}, {ch}, 'm2', 'ai', 'hello');")
    pg.x(f"insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, last_history_id, last_message_at, outcome_at) "
         f"values ({s}, {p}, {ch}, 'in_dialog', 1, now(), now());")
    pg.x(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason) values ({p}, {ch}, 'm1', 'question');")
    pg.x(f"insert into reply_events (profile_id, chat_id, message_id, state) values ({p}, {ch}, 'm1', 'sent'), ({p}, {ch}, 'm2', 'sent');")
    pg.x(f"insert into chat_reply_state (profile_id, chat_id) values ({p}, {ch});")
    pg.x(f"insert into suggestion_sets (request_key, profile_id, chat_id, items) values ({L('rk-' + chat)}, {p}, {ch}, '[]');")
    pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed) values ({p}, {ch}, true);")


def held(pg, pid, chat):
    """How many rows of each per-chat table the chat still has."""
    p, ch, s = L(pid), L(chat), L(f'{pid}:{chat}')
    parts = [f"(select count(*) from n8n_chat_histories where session_id = {s})::int as n8n_chat_histories"]
    parts += [f"(select count(*) from {t} where profile_id = {p} and chat_id = {ch})::int as {t}" for t in CHAT_TABLES[1:]]
    return pg.q('select ' + ', '.join(parts))[0]


def owner_set(pg, pid, chat, kind, version):
    return pg.q(bind(AUD.OWNER_SET_SQL, [pid, [{'chatId': chat, 'kind': kind, 'version': version}]]))[0]


def age(pg, pid, chat, interval):
    """The chat has been personal for `interval` (its personal_since moved into the past)."""
    pg.x(f"update chat_audience set personal_since = now() - interval {L(interval)} "
         f"where profile_id = {L(pid)} and chat_id = {L(chat)};")


def decision(pg, pid, chat):
    r = pg.q(f"select kind, source, (personal_since is not null) as has_since, (wiped_at is not null) as wiped "
             f"from chat_audience where profile_id = {L(pid)} and chat_id = {L(chat)}")
    return r[0] if r else None


def insert_decision(pg, pid, chat, kind, source, since, reason=None, version=1):
    reason = reason or ('owner' if source == 'owner' else 'contact' if kind == 'personal' else 'new')
    since_sql = f'now() - interval {L(since)}' if since else 'null'
    pg.x(f"insert into chat_audience (profile_id, chat_id, kind, source, reason, version, decided_at, personal_since) "
         f"values ({L(pid)}, {L(chat)}, {L(kind)}, {L(source)}, {L(reason)}, {version}, now(), {since_sql});")


def wipe(pg):
    return pg.q(AUD.WIPE_SQL)[0]


def dialog_rows(pg, chat):
    return pg.q(f"select count(*)::int as n from dialog_counts where chat_id = {L(chat)}")[0]['n']


def usage(pg, chat):
    """Two days of the tariff usage for the chat (dialog_counts), today's and an older one."""
    pg.x(f"insert into dialog_counts (app_user_id, chat_id, d) values ('u-wipe', {L(chat)}, current_date), "
         f"('u-wipe', {L(chat)}, current_date - 3);")


def profile(pg, pid, channel, age):
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, created_at) values "
         f"({L(pid)}, 'u-wipe', {L(channel)}, now() - interval {L(age)});")


def connected(pg, pid, phone):
    """The app's report of a confirmed connection (audience.CONNECTED_SQL): another number restarts it, so the computed
    decisions taken before stop holding. A short sleep keeps it strictly after the statement before."""
    pg.x('select pg_sleep(0.003);')
    return pg.q(bind(AUD.CONNECTED_SQL, [pid, phone]))[0]


def decide(pg, pid, chat, kind, reason):
    """Decide Audience's write of the computed decision (audience.DECIDE_WRITE_SQL) — what the chat's next message decides."""
    pg.x('select pg_sleep(0.003);')
    return pg.q(bind(AUD.DECIDE_WRITE_SQL, [pid, chat, kind, reason]))[0]


def shift(pg, pid, chat, interval):
    """The chat's clock moved back by `interval` — only a clock that runs: the test never starts one itself."""
    pg.x(f"update chat_audience set personal_since = personal_since - interval {L(interval)} "
         f"where profile_id = {L(pid)} and chat_id = {L(chat)} and personal_since is not null;")


def travel(pg, pid, interval):
    """`interval` passes for the profile: every moment of its decisions and of its connection moves back by it, so the order
    between them stays what it was — a wipe clock against the connection it started on, a decision against the moment it was
    taken after. (A clock moved back alone would read as one that ran before the connection it started on.)"""
    pg.x(f"update chat_audience set decided_at = decided_at - interval {L(interval)}, "
         f"personal_since = personal_since - interval {L(interval)}, updated_at = updated_at - interval {L(interval)} "
         f"where profile_id = {L(pid)};"
         f"update audience_connections set connected_at = connected_at - interval {L(interval)}, "
         f"prev_connected_at = prev_connected_at - interval {L(interval)}, updated_at = updated_at - interval {L(interval)} "
         f"where profile_id = {L(pid)};")


ZERO = {'wiped_chats': 0, 'deleted_rows': 0}
# A corrected mistaken pairing: the previous number, the mistaken one, and a number the bot had before the previous one.
OLD_NUM, WRONG_NUM, EARLIER_NUM = '77010000001', '77020000002', '77030000003'
RESTORE_CHATS = {'WA': {'client': '77010000050@c.us', 'earlier': '77010000051@c.us', 'stray': '77010000052@c.us',
                        'owners': '77010000053@c.us'},
                 'TG': {'client': '5510000050', 'earlier': '5510000051', 'stray': '5510000052', 'owners': '5510000053'}}
PERSONAL_CONTACT = {'kind': 'personal', 'reason': 'contact', 'source': 'default'}
BUSINESS_NEW = {'kind': 'business', 'reason': 'new', 'source': 'default'}


# ------------------------------------------------------------------------------------------------ the race fixture
class Schema:
    """Two connections on one fixture schema with the real tables' shape; dropped on exit. A statement that would wait for a
    lock gives up after four seconds, so a wipe that waits (a lost SKIP LOCKED) fails the case instead of hanging it."""

    def __enter__(self):
        self.schema = 't_wipe_' + uuid.uuid4().hex[:16]
        self.main, self.owner = PgSession(), PgSession()
        try:
            self.main.x(f'begin; create schema {self.schema}; '
                        + ''.join(f'create table {self.schema}.{t} (like public.{t} including all); ' for t in TABLES)
                        + f"commit; set search_path = {self.schema}; set statement_timeout = '4s';")
            self.owner.x(f'set search_path = {self.schema};')
        except BaseException:
            self.__exit__()
            raise
        return self

    def __exit__(self, *a):
        try:
            self.owner.x('rollback;')
        except Exception:
            pass
        try:
            with PgSession() as pg:          # a fresh session: the main one may have died with a failed statement
                pg.x(f'drop schema if exists {self.schema} cascade;')
        finally:
            for conn in (self.main, self.owner):
                try:
                    conn.close()
                except Exception:
                    pass


# ================================================================================================ the statement, on real SQL
with PgSession(SETUP) as pg:
    with Case('not_before_two_days: 47 h 59 min is untouched, 48 h 1 min is wiped — the boundary on both sides') as c:
        reset(pg)
        pid, chat = 'p-nb', '77010000001@c.us'
        owner_set(pg, pid, chat, 'personal', 1)
        seed(pg, pid, chat)
        for since in ('0 seconds', '1 day', f'{HOURS - 1} hours 59 minutes'):
            age(pg, pid, chat, since)
            r = wipe(pg)
            c.check(r == {'wiped_chats': 0, 'deleted_rows': 0}, f'{since}: {r}')
            c.check(held(pg, pid, chat) == SEEDED, f'{since}: the data must be intact: {held(pg, pid, chat)}')
            c.check(decision(pg, pid, chat)['wiped'] is False, f'{since}: wiped_at must stay empty')
        age(pg, pid, chat, f'{HOURS} hours 1 minute')
        r = wipe(pg)
        c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL}, f'positive control, just past the boundary: {r}')
        c.check(held(pg, pid, chat) == NOTHING, f'wiped: {held(pg, pid, chat)}')

    with Case('wipes_after_two_days_keeps_dialog_counts: every per-chat table is emptied, the decision stays, dialog_counts '
              'is never touched') as c:
        reset(pg)
        pid, chat, other = 'p-wipe', '77010000002@c.us', '77010000003@c.us'
        owner_set(pg, pid, chat, 'personal', 1)
        seed(pg, pid, chat)
        age(pg, pid, chat, '3 days')
        pg.x(f"insert into dialog_counts (app_user_id, chat_id, d) values ('u-wipe', {L(chat)}, current_date), "
             f"('u-wipe', {L(chat)}, current_date - 40), ('u-wipe', {L(other)}, current_date);")
        before = decision(pg, pid, chat)
        r = wipe(pg)
        c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL}, f'counts: {r}')
        c.check(held(pg, pid, chat) == NOTHING, f'every table emptied for the chat: {held(pg, pid, chat)}')
        c.check(dialog_rows(pg, chat) == 2 and dialog_rows(pg, other) == 1, 'dialog_counts (the tariff usage) must never be touched')
        after = decision(pg, pid, chat)
        c.check(before == {'kind': 'personal', 'source': 'owner', 'has_since': True, 'wiped': False}, f'before: {before}')
        c.check(after == {'kind': 'personal', 'source': 'owner', 'has_since': True, 'wiped': True},
                f'the decision stays (the chat must stay personal), only wiped_at is set: {after}')

    with Case('returned_within_two_days_untouched: personal → business clears the clock; personal again restarts it from now') as c:
        reset(pg)
        pid, chat = 'p-ret', '77010000004@c.us'
        owner_set(pg, pid, chat, 'personal', 1)
        seed(pg, pid, chat)
        age(pg, pid, chat, f'{HOURS - 1} hours')                # a day and 23 hours later the owner changes their mind
        back = owner_set(pg, pid, chat, 'business', 2)
        c.check(back['applied'] is True and back['kind'] == 'business', f'the owner takes the chat back: {back}')
        c.check(wipe(pg) == {'wiped_chats': 0, 'deleted_rows': 0} and held(pg, pid, chat) == SEEDED,
                'a chat returned to business is never selected, whatever its past')
        c.check(decision(pg, pid, chat)['has_since'] is False, 'personal_since is cleared when the chat turns business')
        again = owner_set(pg, pid, chat, 'personal', 3)       # …and a mis-tap later it is personal once more
        c.check(again['applied'] is True and decision(pg, pid, chat)['has_since'] is True, f'personal again: {again}')
        c.check(wipe(pg) == {'wiped_chats': 0, 'deleted_rows': 0} and held(pg, pid, chat) == SEEDED,
                'the clock restarted at the second «personal»: the first stretch does not count')
        age(pg, pid, chat, f'{HOURS - 1} hours 59 minutes')
        c.check(wipe(pg)['wiped_chats'] == 0 and held(pg, pid, chat) == SEEDED, 'still inside the new two days')
        age(pg, pid, chat, f'{HOURS} hours 1 minute')
        c.check(wipe(pg)['wiped_chats'] == 1 and held(pg, pid, chat) == NOTHING, 'positive control: wiped two days after the LAST «personal»')

    with Case('idempotent_second_run_deletes_nothing: a wiped chat is never selected again, whatever is written for it after') as c:
        reset(pg)
        pid, chat = 'p-idem', '77010000005@c.us'
        owner_set(pg, pid, chat, 'personal', 1)
        seed(pg, pid, chat)
        age(pg, pid, chat, '3 days')
        c.check(wipe(pg) == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL}, 'first run')
        pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body) "
             f"values ({L(pid)}, {L(chat)}, 'm3', 'client', 'late');")
        r = wipe(pg)
        c.check(r == {'wiped_chats': 0, 'deleted_rows': 0}, f'second run: {r}')
        c.check(held(pg, pid, chat)['chat_history_events'] == 1, 'a row written after the wipe is left alone — wiped means done')

    with Case('personal_again_after_a_wipe_is_wiped_again: personal → wiped → business → personal re-arms the clock and the mark') as c:
        reset(pg)
        pid, chat = 'p-rearm', '77010000010@c.us'
        owner_set(pg, pid, chat, 'personal', 1)
        seed(pg, pid, chat)
        age(pg, pid, chat, '3 days')
        c.check(wipe(pg)['wiped_chats'] == 1 and decision(pg, pid, chat)['wiped'] is True, 'the first wipe')
        owner_set(pg, pid, chat, 'business', 2)
        c.check(decision(pg, pid, chat) == {'kind': 'business', 'source': 'owner', 'has_since': False, 'wiped': False},
                f'back to business the clock and the mark are cleared: {decision(pg, pid, chat)}')
        seed(pg, pid, chat)                                        # a business chat is remembered again
        owner_set(pg, pid, chat, 'personal', 3)
        c.check(decision(pg, pid, chat) == {'kind': 'personal', 'source': 'owner', 'has_since': True, 'wiped': False},
                f'personal again: a new clock, not wiped: {decision(pg, pid, chat)}')
        c.check(wipe(pg) == {'wiped_chats': 0, 'deleted_rows': 0} and held(pg, pid, chat) == SEEDED, 'not before two days again')
        age(pg, pid, chat, '3 days')
        c.check(wipe(pg) == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL} and held(pg, pid, chat) == NOTHING,
                'two days after the LAST «personal» the new memory is wiped too')

    with Case('chat_without_data_is_still_marked_done: nothing stored → wiped_chats 1, deleted_rows 0, off the work list') as c:
        reset(pg)
        pid, chat = 'p-empty', '77010000006@c.us'
        owner_set(pg, pid, chat, 'personal', 1)
        age(pg, pid, chat, '3 days')
        c.check(wipe(pg) == {'wiped_chats': 1, 'deleted_rows': 0}, 'a decision with nothing behind it is still done')
        c.check(decision(pg, pid, chat)['wiped'] is True and wipe(pg) == {'wiped_chats': 0, 'deleted_rows': 0},
                'and it never comes back (an empty chat would otherwise sit at the head of the queue forever)')

    with Case('other_chats_and_star_flag_untouched: another chat, a longer id, another profile, the bot-wide flag') as c:
        reset(pg)
        P, Q = 'p-oth', 'q-oth'
        A, young, business, longer, owned_by_q = ('77010000007@c.us', '77010000008@c.us', '77010000009@c.us',
                                                  '77010000007@c.us-2', '77010000007@c.us')
        owner_set(pg, P, A, 'personal', 1)
        age(pg, P, A, '3 days')
        owner_set(pg, P, young, 'personal', 1)
        age(pg, P, young, '1 day')
        insert_decision(pg, P, business, 'business', 'owner', None)
        for p, ch in ((P, A), (P, young), (P, business), (P, longer), (Q, owned_by_q)):
            seed(pg, p, ch)
        pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed) values ({L(P)}, {L(STAR)}, true), ({L(Q)}, {L(STAR)}, false);")
        r = wipe(pg)
        c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL}, f'only chat A: {r}')
        c.check(held(pg, P, A) == NOTHING, f'A wiped: {held(pg, P, A)}')
        for p, ch, why in ((P, young, 'a chat personal for one day'), (P, business, 'a business chat'),
                           (P, longer, 'a chat whose id merely STARTS with the wiped one'),
                           (Q, owned_by_q, 'the same chat id under ANOTHER profile')):
            c.check(held(pg, p, ch) == SEEDED, f'{why} must keep everything: {held(pg, p, ch)}')
        stars = pg.q("select profile_id, suppressed from reply_mode_flags where chat_id = '*' order by profile_id")
        c.check(stars == [{'profile_id': P, 'suppressed': True}, {'profile_id': Q, 'suppressed': False}],
                f"the bot-wide '*' flags must stay: {stars}")

    with Case("star_chat_row_never_touches_the_star_flag: a decision row whose chat id is '*' (the webhook accepts any chat id) "
              "must not delete the bot-wide flag") as c:
        reset(pg)
        pid = 'p-star'
        insert_decision(pg, pid, STAR, 'personal', 'owner', '3 days')
        pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed) values ({L(pid)}, {L(STAR)}, true);")
        r = wipe(pg)
        c.check(r['deleted_rows'] == 0, f"the '*' flag is the bot's default, not a chat's data: {r}")
        c.check(pg.q(f"select count(*)::int as n from reply_mode_flags where profile_id = {L(pid)} and chat_id = '*'")[0]['n'] == 1,
                "the bot-wide '*' row must survive")
        c.check(decision(pg, pid, STAR)['wiped'] is True, "the bogus row leaves the work list instead of sitting in it forever")

    with Case('computed_clock_counts_business_never: a personal decision whose clock is old enough is a candidate WHATEVER its '
              'source — the owner\'s and a computed one that replaced a business row alike; a personal decision without a clock '
              'and a business decision (whatever personal_since says) are never selected') as c:
        reset(pg)
        pid = 'p-def'
        rows = (('d-clock', 'personal', 'default', '10 days', True),      # a computed personal that replaced a business row
                ('d-young', 'personal', 'default', '1 day', False),       # …whose clock is still inside the two days
                ('d-history', 'personal', 'default', None, False),        # the normal computed «personal»: never business, no clock
                ('d-new', 'business', 'default', None, False),
                ('d-biz', 'business', 'default', '10 days', False),       # pathological: a business row with a stale personal_since
                ('o-plain', 'personal', 'owner', None, False),            # the owner confirmed a chat that was personal already
                ('o-biz', 'business', 'owner', '10 days', False))         # pathological: a business row with a stale personal_since
        for chat, kind, source, since, _ in rows:
            insert_decision(pg, pid, chat, kind, source, since)
            seed(pg, pid, chat)
        r = wipe(pg)
        c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL}, f'only the computed personal with an old clock: {r}')
        for chat, kind, source, since, gone in rows:
            if gone:
                c.check(held(pg, pid, chat) == NOTHING and decision(pg, pid, chat)['wiped'] is True,
                        f'{chat} must be wiped: {held(pg, pid, chat)} {decision(pg, pid, chat)}')
            else:
                c.check(held(pg, pid, chat) == SEEDED and decision(pg, pid, chat)['wiped'] is False, f'{chat} must be untouched')

    for ch, channel, chat in (('WA', 'whatsapp', '77010000020@c.us'), ('TG', 'telegram', '5510000020')):
        with Case(f'number_change_then_rule_personal_wipes_the_business_period [{ch}]: a chat that was business on the old number '
                  '(history and memory stored), another number connected, the next message decided personal by the rule (a '
                  'contact of the new number) — the clock starts at that decision, the data stays for two days, then every '
                  'per-chat table is emptied; dialog_counts never') as c:
            reset(pg)
            pid = f'p-num-{ch.lower()}'
            profile(pg, pid, channel, '5 days')
            connected(pg, pid, '77010000001')                              # the old number, the line's moment
            got = decide(pg, pid, chat, 'business', 'new')
            c.check(got == {'kind': 'business', 'reason': 'new', 'source': 'default'}, f'premise: a new client of the old number: {got}')
            seed(pg, pid, chat)                                            # what the writers stored while the chat was business
            usage(pg, chat)
            moved = connected(pg, pid, '77020000002')                      # «Сменить номер»: another number
            c.check(moved['restarted'] is True, f'premise: another number restarts the connection: {moved}')
            got = decide(pg, pid, chat, 'personal', 'contact')             # a contact of the new number
            c.check(got == {'kind': 'personal', 'reason': 'contact', 'source': 'default'}, f'the rule decides personal: {got}')
            c.check(decision(pg, pid, chat) == {'kind': 'personal', 'source': 'default', 'has_since': True, 'wiped': False},
                    f'the clock must start at that decision: {decision(pg, pid, chat)}')
            c.check(wipe(pg) == ZERO and held(pg, pid, chat) == SEEDED, 'the business period stays for two days')
            shift(pg, pid, chat, f'{HOURS - 1} hours 59 minutes')
            c.check(wipe(pg) == ZERO and held(pg, pid, chat) == SEEDED, 'still inside the two days')
            shift(pg, pid, chat, '2 minutes')
            r = wipe(pg)
            c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL}, f'two days on: {r}')
            c.check(held(pg, pid, chat) == NOTHING, f'every per-chat table emptied: {held(pg, pid, chat)}')
            c.check(dialog_rows(pg, chat) == 2, 'dialog_counts (the tariff usage) must never be touched')
            c.check(decision(pg, pid, chat) == {'kind': 'personal', 'source': 'default', 'has_since': True, 'wiped': True},
                    f'the decision stays personal, marked wiped: {decision(pg, pid, chat)}')

        with Case(f'rearmed_by_the_rule_after_a_wipe [{ch}]: wiped → another number → business by the rule (the clock and the '
                  'mark cleared) → stored again → another number → personal by the rule: a NEW clock — nothing before two days, '
                  'everything after') as c:
            connected(pg, pid, '77030000003')
            decide(pg, pid, chat, 'business', 'new')
            c.check(decision(pg, pid, chat) == {'kind': 'business', 'source': 'default', 'has_since': False, 'wiped': False},
                    f'business again clears the clock and the mark: {decision(pg, pid, chat)}')
            if held(pg, pid, chat) == NOTHING:                             # (a failed wipe above left the old rows in place)
                seed(pg, pid, chat)                                        # a business chat is remembered again
            connected(pg, pid, '77040000004')
            decide(pg, pid, chat, 'personal', 'history')
            c.check(decision(pg, pid, chat) == {'kind': 'personal', 'source': 'default', 'has_since': True, 'wiped': False},
                    f'personal again: a new clock, not wiped: {decision(pg, pid, chat)}')
            c.check(wipe(pg) == ZERO and held(pg, pid, chat) == SEEDED, 'not before two days again')
            shift(pg, pid, chat, f'{HOURS} hours 1 minute')
            c.check(wipe(pg) == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL} and held(pg, pid, chat) == NOTHING,
                    'two days after the LAST «personal» the new memory is wiped too')
            c.check(dialog_rows(pg, chat) == 2, 'dialog_counts still untouched')

    with Case('a_holding_business_row_is_never_wiped: a computed business decision of the current connection meets a computed '
              'personal one (the decision is written once): it stays business, no clock, nothing deleted however long it waits') as c:
        reset(pg)
        pid, chat = 'p-hold', '77010000030@c.us'
        profile(pg, pid, 'whatsapp', '5 days')
        connected(pg, pid, '77010000001')
        decide(pg, pid, chat, 'business', 'new')
        seed(pg, pid, chat)
        got = decide(pg, pid, chat, 'personal', 'contact')
        c.check(got == {'kind': 'business', 'reason': 'new', 'source': 'default'}, f'the holding business decision answers: {got}')
        pg.x(f"update chat_audience set decided_at = decided_at - interval '10 days' where profile_id = {L(pid)};")
        pg.x(f"update audience_connections set connected_at = connected_at - interval '20 days' where profile_id = {L(pid)};")
        c.check(wipe(pg) == ZERO and held(pg, pid, chat) == SEEDED
                and decision(pg, pid, chat) == {'kind': 'business', 'source': 'default', 'has_since': False, 'wiped': False},
                f'a business chat keeps everything: {decision(pg, pid, chat)}')

    # A corrected mistaken pairing: another account paired, then the previous number again within RESTORE_HOURS —
    # CONNECTED_SQL restores the earlier connection (here on one profile; through a new profile of the line:
    # tests/test_clone_lineage_sql.py L31). A client of the previous number who also wrote to the mistaken number was decided
    # there; when that account knows him, the personal decision replaced his business row and started the wipe clock. The
    # decision stops holding with the return, and the wipe must leave him alone: that clock is the mistaken account's.
    for ch, channel in (('WA', 'whatsapp'), ('TG', 'telegram')):
        client = RESTORE_CHATS[ch]['client']
        for late in (False, True):
            label = 'late_return_is_a_number_change_and_wipes' if late else 'restore_spares_the_previous_numbers_client'
            title = ('the previous number paired again only after RESTORE_HOURS — another number, a new connection: the clock the '
                     'mistaken account started runs, and two days on the client\'s stored business period is wiped (the rule of a '
                     'number change); dialog_counts never') if late else (
                     'the previous number paired again within RESTORE_HOURS restores the connection; two days on — and a month '
                     'on — the wipe leaves the client alone, everything he had is kept; his next message on the previous number '
                     'decides him business again and the clock is gone')
            with Case(f'{label} [{ch}]: a client of the previous number (business, his conversation stored) wrote to the mistaken '
                      f'number and its account knows him — decided personal there, the wipe clock started; {title}') as c:
                reset(pg)
                pid = f'p-rst-{ch.lower()}-{int(late)}'
                profile(pg, pid, channel, '5 days')
                connected(pg, pid, OLD_NUM)                                    # the previous number, the line's moment
                c.check(decide(pg, pid, client, 'business', 'new') == BUSINESS_NEW, 'premise: a client of the previous number')
                seed(pg, pid, client)                                          # his conversation, stored while he was business
                usage(pg, client)
                wrong = connected(pg, pid, WRONG_NUM)                          # the mistaken pairing
                c.check(wrong['restarted'] is True and wrong.get('restored') is False, f'premise: the mistake restarts: {wrong}')
                got = decide(pg, pid, client, 'personal', 'contact')          # he wrote to the mistaken number, which knows him
                c.check(got == PERSONAL_CONTACT
                        and decision(pg, pid, client) == {'kind': 'personal', 'source': 'default', 'has_since': True, 'wiped': False},
                        f'premise: the mistaken account\'s decision replaced his business row and started the clock: {got} '
                        f'{decision(pg, pid, client)}')
                if late:
                    pg.x(f"update audience_connections set connected_at = connected_at - interval '{AUD.RESTORE_HOURS} hours 1 minute' "
                         f"where profile_id = {L(pid)};")                      # the mistaken connection began over a day ago
                back = connected(pg, pid, OLD_NUM)                             # the previous number paired again
                c.check(back.get('restored') is (not late) and back['restarted'] is late,
                        f'premise: {"too late — a new connection" if late else "the return restores the connection"}: {back}')
                c.check(wipe(pg) == ZERO and held(pg, pid, client) == SEEDED, 'nothing is wiped before two days, whatever the clock')
                travel(pg, pid, f'{HOURS} hours 1 minute')
                r = wipe(pg)
                if late:
                    c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL} and held(pg, pid, client) == NOTHING,
                            f'two days on, a number change: his stored business period is wiped: {r} {held(pg, pid, client)}')
                    c.check(dialog_rows(pg, client) == 2, 'dialog_counts (the tariff usage) must never be touched')
                    continue
                c.check(r == ZERO and held(pg, pid, client) == SEEDED and decision(pg, pid, client)['wiped'] is False,
                        f'two days on: the previous number\'s client keeps everything: {r} {held(pg, pid, client)} '
                        f'{decision(pg, pid, client)}')
                travel(pg, pid, '30 days')
                c.check(wipe(pg) == ZERO and held(pg, pid, client) == SEEDED,
                        f'a month on too — the connection stays restored: {held(pg, pid, client)}')
                got = decide(pg, pid, client, 'business', 'new')               # his next message, on the previous number
                c.check(got == BUSINESS_NEW
                        and decision(pg, pid, client) == {'kind': 'business', 'source': 'default', 'has_since': False, 'wiped': False},
                        f'decided business again — the clock is gone: {got} {decision(pg, pid, client)}')
                c.check(wipe(pg) == ZERO and held(pg, pid, client) == SEEDED and dialog_rows(pg, client) == 2,
                        'nothing wiped, the tariff usage untouched')

    for ch, channel in (('WA', 'whatsapp'), ('TG', 'telegram')):
        x = RESTORE_CHATS[ch]
        with Case(f'restore_spares_only_the_mistaken_accounts_clock [{ch}]: after the return the wipe still takes every personal '
                  'decision whose clock is not the mistaken account\'s — the owner\'s «личный» over a chat of the mistaken period '
                  '(it carries the mistaken number), a clock that ran before the mistake (a chat that had turned personal on the '
                  'previous number), a chat of the mistaken period decided personal on the restored number; and the client himself '
                  'once the restored number decides him personal too (his clock kept: personal over personal)') as c:
            reset(pg)
            pid = f'p-rso-{ch.lower()}'
            profile(pg, pid, channel, '9 days')
            connected(pg, pid, EARLIER_NUM)                                    # the number the bot had before the previous one…
            decide(pg, pid, x['earlier'], 'business', 'new')                   # …and its client
            seed(pg, pid, x['earlier'])
            connected(pg, pid, OLD_NUM)                                        # the previous number: another number, as usual
            decide(pg, pid, x['earlier'], 'personal', 'contact')               # a contact of it: the earlier client's clock runs
            decide(pg, pid, x['client'], 'business', 'new')                    # the previous number's client
            seed(pg, pid, x['client'])
            connected(pg, pid, WRONG_NUM)                                      # the mistake
            decide(pg, pid, x['client'], 'personal', 'contact')                # the mistaken account knows him: its clock starts
            decide(pg, pid, x['earlier'], 'personal', 'contact')               # personal over personal: the earlier clock is kept
            decide(pg, pid, x['stray'], 'business', 'new')                     # a client of the mistaken account
            seed(pg, pid, x['stray'])
            decide(pg, pid, x['owners'], 'business', 'new')                    # another one, whom the owner makes personal there
            seed(pg, pid, x['owners'])
            c.check(owner_set(pg, pid, x['owners'], 'personal', 1)['applied'] is True, 'premise: the owner\'s «личный»')
            c.check(all(decision(pg, pid, x[k])['has_since'] for k in ('client', 'earlier', 'owners')),
                    f'premise: three clocks run: { {k: decision(pg, pid, x[k]) for k in x} }')
            back = connected(pg, pid, OLD_NUM)                                 # the return
            c.check(back.get('restored') is True, f'premise: the return restores the connection: {back}')
            travel(pg, pid, f'{HOURS} hours 1 minute')
            r = wipe(pg)
            got = {k: held(pg, pid, x[k]) == SEEDED for k in x}
            c.check(r == {'wiped_chats': 2, 'deleted_rows': 2 * SEEDED_TOTAL}
                    and got == {'client': True, 'earlier': False, 'stray': True, 'owners': False},
                    f'two days on: the owner\'s «личный» and the clock from before the mistake are wiped; the client of the previous '
                    f'number and the mistaken period\'s business chat are kept — {r} kept {got}')
            got = decide(pg, pid, x['stray'], 'personal', 'history')          # the mistaken period's client, on the restored number
            c.check(got == {'kind': 'personal', 'reason': 'history', 'source': 'default'}
                    and decision(pg, pid, x['stray'])['has_since'] is True, f'premise: a clock of the restored number: {got}')
            travel(pg, pid, f'{HOURS} hours 1 minute')
            r = wipe(pg)
            c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL} and held(pg, pid, x['stray']) == NOTHING
                    and held(pg, pid, x['client']) == SEEDED,
                    f'a clock of the restored number runs as usual — the client is still kept: {r}')
            got = decide(pg, pid, x['client'], 'personal', 'contact')          # the restored number decides him personal too
            c.check(got == PERSONAL_CONTACT and decision(pg, pid, x['client'])['has_since'] is True,
                    f'premise: personal over personal keeps the clock: {got} {decision(pg, pid, x["client"])}')
            r = wipe(pg)
            c.check(r == {'wiped_chats': 1, 'deleted_rows': SEEDED_TOTAL} and held(pg, pid, x['client']) == NOTHING,
                    f'the decision holds now and is no longer the mistaken account\'s: its clock runs, two days are long past: {r}')

    with Case('limit_200_oldest_first: one run wipes the 200 oldest; the rest wait for the next run') as c:
        reset(pg)
        pid = 'p-many'
        pg.x(f"insert into chat_audience (profile_id, chat_id, kind, source, reason, version, personal_since) "
             f"select {L(pid)}, 'c' || lpad(i::text, 4, '0'), 'personal', 'owner', 'owner', 1, now() - interval '9 days' + i * interval '1 minute' "
             f"from generate_series(1, 205) as i;")
        pg.x(f"insert into chat_history_events (profile_id, chat_id, message_id, source, body) "
             f"select {L(pid)}, 'c' || lpad(i::text, 4, '0'), 'm1', 'client', 'hi' from generate_series(1, 205) as i;")
        first = wipe(pg)
        c.check(first == {'wiped_chats': 200, 'deleted_rows': 200}, f'first run: {first}')
        left = [r['chat_id'] for r in pg.q(f"select chat_id from chat_audience where profile_id = {L(pid)} and wiped_at is null order by chat_id")]
        c.check(left == [f'c{i:04d}' for i in range(201, 206)], f'the five YOUNGEST wait: {left}')
        c.check(pg.q(f"select count(*)::int as n from chat_history_events where profile_id = {L(pid)}")[0]['n'] == 5,
                'only the waiting chats keep their rows')
        second = wipe(pg)
        c.check(second == {'wiped_chats': 5, 'deleted_rows': 5}, f'second run: {second}')
        c.check(wipe(pg) == {'wiped_chats': 0, 'deleted_rows': 0}, 'third run: nothing left')

# ------------------------------------------------------------------------------------------------ the race, two connections
with Case('locked_row_is_skipped_never_waited_for: the owner takes a chat back (uncommitted) while the wipe runs — the wipe '
          'leaves it alone, does not wait, still wipes the others, and finds nothing there once the owner commits') as c:
    with Schema() as s:
        P, A, B = 'p-race', 'race-a@c.us', 'race-b@c.us'
        for chat in (A, B):
            s.main.x(f"insert into chat_audience (profile_id, chat_id, kind, source, reason, version, personal_since) "
                     f"values ({L(P)}, {L(chat)}, 'personal', 'owner', 'owner', 1, now() - interval '3 days');")
            s.main.x(f"insert into suggestion_sets (request_key, profile_id, chat_id, items) values ({L('rk-' + chat)}, {L(P)}, {L(chat)}, '[]');")

        def sets_of(chat):
            return s.main.q(f"select count(*)::int as n from suggestion_sets where profile_id = {L(P)} and chat_id = {L(chat)}")[0]['n']

        s.owner.x('begin;')
        s.owner.x(bind(AUD.OWNER_SET_SQL, [P, [{'chatId': A, 'kind': 'business', 'version': 2}]]) + ';')   # in flight, not committed
        r = s.main.q(AUD.WIPE_SQL)[0]                                                                  # waits → statement timeout
        c.check(r == {'wiped_chats': 1, 'deleted_rows': 1}, f'only the unlocked chat B is wiped, the locked A is skipped: {r}')
        c.check(sets_of(A) == 1 and sets_of(B) == 0, f'A keeps its data, B lost its: {sets_of(A)} / {sets_of(B)}')
        s.owner.x('commit;')
        r = s.main.q(AUD.WIPE_SQL)[0]
        c.check(r == {'wiped_chats': 0, 'deleted_rows': 0} and sets_of(A) == 1, f'A is business now — nothing to find: {r}')
        row = s.main.q(f"select kind, personal_since is null as clear, wiped_at is null as open from chat_audience "
                       f"where profile_id = {L(P)} and chat_id = {L(A)}")[0]
        c.check(row == {'kind': 'business', 'clear': True, 'open': True}, f'the owner\'s decision stands: {row}')

with Case('connection_report_in_flight_never_holds_the_wipe: the wipe reads the profile\'s connection (a restored one spares the '
          'mistaken account\'s clocks) but never locks it — a connection report in flight (uncommitted) neither makes it wait '
          'nor makes it skip the profile\'s chats') as c:
    with Schema() as s:
        P, A = 'p-race-conn', 'race-conn@c.us'
        s.main.x(f"insert into bot_profiles (profile_id, app_user_id, channel, created_at) values "
                 f"({L(P)}, 'u-wipe', 'whatsapp', now() - interval '5 days');")
        s.main.x(f"insert into audience_connections (profile_id, phone, connected_at) values ({L(P)}, {L(OLD_NUM)}, now() - interval '5 days');")
        s.main.x(f"insert into chat_audience (profile_id, chat_id, kind, source, reason, version, personal_since) "
                 f"values ({L(P)}, {L(A)}, 'personal', 'owner', 'owner', 1, now() - interval '3 days');")
        s.main.x(f"insert into suggestion_sets (request_key, profile_id, chat_id, items) values ('rk-conn', {L(P)}, {L(A)}, '[]');")
        s.owner.x('begin;')
        s.owner.q(bind(AUD.CONNECTED_SQL, [P, WRONG_NUM]))                    # another number reported, not committed yet
        r = s.main.q(AUD.WIPE_SQL)[0]                                          # waits → statement timeout
        c.check(r == {'wiped_chats': 1, 'deleted_rows': 1}, f'the wipe runs past the locked connection row: {r}')
        s.owner.x('rollback;')

# ------------------------------------------------------------------------------------------------ the work list's index
MIGRATION = N8N / 'sql' / '2026-10-25-chat-audience.sql'


def migration_tables():
    """The migration's OWN table block (chat_audience with its partial index, audience_connections), aimed at temp tables —
    the index that ships, not whatever the staging table carries."""
    text = MIGRATION.read_text(encoding='utf-8')
    block = text.split('-- TABLE-BEGIN')[1].split('\n', 1)[1].split('-- TABLE-END')[0]
    return block.replace('create table if not exists public.', 'create temp table ').replace(' on public.', ' on ')


with Case('the_work_list_can_use_the_partial_index: the planner can take the batch from chat_audience_wipe_idx, the index the '
          'migration builds for exactly this list — the index\'s predicate stays implied by the statement\'s work list') as c:
    with PgSession(migration_tables() + ''.join(f'create temp table {t} (like public.{t} including all);\n' for t in CHAT_TABLES)) as pg:
        pg.x("insert into chat_audience (profile_id, chat_id, kind, source, reason, personal_since) "
             "select 'p-idx', 'c' || i, case when i % 3 = 0 then 'business' else 'personal' end, "
             "case when i % 2 = 0 then 'owner' else 'default' end, case when i % 2 = 0 then 'owner' else 'contact' end, "
             "case when i % 5 = 0 then now() - i * interval '1 hour' end from generate_series(1, 2000) as i;")
        pg.x('analyze chat_audience;')
        plan = pg.x('set enable_seqscan = off;\nexplain ' + AUD.WIPE_SQL + ';')
        c.check('chat_audience_wipe_idx' in plan, f'the batch does not go through the partial index:\n{plan[:1500]}')

# ------------------------------------------------------------------------------------------------ the statement as text
with Case('statement_shape: no parameters, one delete per per-chat table, never dialog_counts / the line\'s other tables / the '
          'decision itself, passes the V1 guard') as c:
    q = AUD.WIPE_SQL
    code = '\n'.join(re.sub(r'--.*$', '', line) for line in q.split('\n'))     # the comment says «never dialog_counts» on purpose
    flat = ' '.join(code.lower().split())
    deleted = sorted(re.findall(r'delete from (\w+)', flat))
    c.check(deleted == sorted(CHAT_TABLES), f'the tables deleted from: {deleted}')
    c.check(re.search(r'\$\d', q) is None and '{{' not in q and not q.lstrip().startswith('='),
            'the statement takes no parameter and is no n8n expression')
    for forbidden in ('dialog_counts', 'bot_schedule', 'delete from chat_audience', 'bot_profiles'):
        c.check(forbidden not in flat, f'{forbidden} must never appear in the wipe')
    restore = ' '.join(AUD.RESTORE_VOIDED_SQL('a').lower().split())
    c.check(f'and not {restore}' in flat and flat.count('audience_connections') == 1
            and not any(w in flat for w in ('delete from audience_connections', 'update audience_connections',
                                            'insert into audience_connections')),
            'the connection is read once — by the restore skip, audience.RESTORE_VOIDED_SQL — and never written')
    c.check(flat.count('update chat_audience') == 1 and 'wiped_at = now()' in flat, 'one update marks the chats wiped')
    c.check('for update skip locked' in flat and AUD.WIPE_BATCH == 200 and f'limit {AUD.WIPE_BATCH}' in flat,
            'locks what it takes, skips what is locked, at most 200 chats a run')
    c.check(isinstance(AUD.WIPE_DAYS, int) and AUD.WIPE_DAYS == 2 and f"'{HOURS} hours'" in flat,
            f'the delay is WIPE_DAYS written as hours ({HOURS}): a clock change never moves it')
    found = VSC.check_query(q)
    c.check(not found, f'verify-sql-comments: {found}')

with Case('chat_tables_follow_the_line: the per-chat tables are clone_lineage.MOVED_TABLES minus the per-bot rows and the '
          'decision — exactly the tables WIPE_SQL deletes from and the tables the behaviour cases seed; never dialog_counts') as c:
    code = '\n'.join(re.sub(r'--.*$', '', line) for line in AUD.WIPE_SQL.split('\n'))
    deleted = sorted(re.findall(r'delete from (\w+)', ' '.join(code.lower().split())))
    c.check(deleted == sorted(CHAT_TABLES), f'derived from the line {sorted(CHAT_TABLES)} vs deleted by the wipe {deleted}')
    c.check('dialog_counts' not in CHAT_TABLES and 'dialog_counts' not in CL.MOVED_TABLES, 'the tariff usage is never chat data')
    c.check(set(NOT_CHAT_DATA) <= set(CL.MOVED_TABLES), f'the exclusions must name tables of the line: {NOT_CHAT_DATA}')
    c.check(set(SEEDED) == set(CHAT_TABLES), f'every per-chat table must be seeded by the behaviour cases: '
                                             f'{sorted(set(CHAT_TABLES) ^ set(SEEDED))}')
    vpc = module('verify-personal-chats.py')
    c.check(tuple(vpc.CHAT_TABLES) == CHAT_TABLES, f'the verifier derives the same list: {vpc.CHAT_TABLES}')

# ================================================================================================ the Sweep node
def targets(w, src):
    return [[e['node'] for e in (out or [])] for out in (w['connections'].get(src) or {}).get('main', [])]


def sources(w, dst):
    return sorted((s, i) for s, cn in w['connections'].items() for i, out in enumerate((cn or {}).get('main') or [])
                  for e in (out or []) if e.get('node') == dst)


def reachable(w, start):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        for out in (w['connections'].get(n) or {}).get('main') or []:
            todo.extend(e['node'] for e in (out or []))
    return seen


def generator():
    return module('apply-personal-wipe.py')


def v1_order(w, src):
    """The targets of `src` in the order n8n's v1 engine runs them: sorted by canvas position, the topmost first (on a tie
    the leftmost), each branch run to its end before the next (n8n-core workflow-execute.js)."""
    names = [e['node'] for out in (w['connections'].get(src) or {}).get('main') or [] for e in (out or [])]
    return sorted(dict.fromkeys(names), key=lambda n: (node(w, n)['position'][1], node(w, n)['position'][0]))


with Case('sweep_node_is_wired_and_independent: Every 6 Hours → Wipe Personal Chats directly, terminal, fail-open, the FIRST '
          'branch the trigger runs (strictly above every other branch, in Sweep Config\'s column)') as c:
    sw = wf(SWEEP_FILE)
    names = [n['name'] for n in sw['nodes']]
    c.check(names.count(NODE) == 1, f'exactly one {NODE}: {names.count(NODE)}')
    n = node(sw, NODE)
    p = n['parameters']
    c.check(n['type'] == 'n8n-nodes-base.postgres' and p.get('operation') == 'executeQuery', f'a Postgres executeQuery node: {n["type"]}')
    c.check(p.get('query') == AUD.WIPE_SQL and not (p.get('options') or {}).get('queryReplacement'),
            'its query is audience.WIPE_SQL, with no parameters')
    c.check(n.get('onError') == 'continueRegularOutput' and not n.get('disabled'), 'fail-open and not disabled')
    c.check(n.get('credentials') == node(sw, 'Candidates')['credentials'], f'the sweep\'s own Postgres credential: {n.get("credentials")}')
    c.check(targets(sw, 'Every 6 Hours')[0].count(NODE) == 1, f'a target of Every 6 Hours: {targets(sw, "Every 6 Hours")}')
    c.check(sources(sw, NODE) == [('Every 6 Hours', 0)], f'fed by the trigger ONLY — no dry-run gate in between: {sources(sw, NODE)}')
    c.check(NODE not in reachable(sw, 'Sweep Config'), 'unreachable from Sweep Config (the dryRun flags live there)')
    c.check(not sw['connections'].get(NODE), 'terminal: nothing runs after it')
    order = v1_order(sw, 'Every 6 Hours')
    others = [x for x in order if x != NODE]
    c.check(order[0] == NODE and all(node(sw, x)['position'][1] > n['position'][1] for x in others),
            f'the first branch n8n runs from the trigger, strictly above the others (an unhandled error in an earlier branch '
            f'would end the run before it): {[(x, node(sw, x)["position"]) for x in order]}')
    c.check(n['position'][0] == node(sw, 'Sweep Config')['position'][0], f'in Sweep Config\'s column: {n["position"]}')
    c.check(set(others) == {'Sweep Config', 'Expire Learning Rows'}, f'the trigger\'s other branches: {others}')
    c.check(len({x['id'] for x in sw['nodes']}) == len(sw['nodes']), 'node ids stay unique')

with Case('generator_is_idempotent_and_repairs_drift: patch_sweep brings a bare canon to the committed JSON and fixes drift; '
          'a second pass changes nothing') as c:
    G = generator()
    canon = wf(SWEEP_FILE)
    c.check(G.check_sweep(canon) is True and G.patch_sweep(copy.deepcopy(canon)) is False, 'the committed JSON is up to date')
    bare = copy.deepcopy(canon)
    bare['nodes'] = [x for x in bare['nodes'] if x['name'] != NODE]
    bare['connections']['Every 6 Hours']['main'][0] = [e for e in bare['connections']['Every 6 Hours']['main'][0] if e['node'] != NODE]
    c.check(G.check_sweep(bare) is False, 'a canon without the node is stale')
    c.check(G.patch_sweep(bare) is True and bare == canon, 'patching a bare canon reproduces the committed JSON exactly')
    c.check(G.patch_sweep(bare) is False and G.check_sweep(bare) is True, 'and a second pass changes nothing')

    def drifted(mutate, why):
        w = copy.deepcopy(canon)
        mutate(node(w, NODE))
        c.check(G.check_sweep(w) is False, f'{why}: must be seen as stale')
        c.check(G.patch_sweep(w) is True and w == canon, f'{why}: must be repaired to the canon')

    drifted(lambda n: n['parameters'].update(query='select 1'), 'a hand-edited query')
    drifted(lambda n: n.update(disabled=True), 'a disabled node')
    drifted(lambda n: n.update(onError='stopWorkflow'), 'a changed onError')
    drifted(lambda n: n.pop('onError'), 'a removed onError')
    drifted(lambda n: n.update(credentials={'postgres': {'id': 'dev0000000000000', 'name': 'Postgres Dev'}}), 'another credential')
    drifted(lambda n: n['parameters'].update(options={'queryReplacement': 'x'}), 'a parameter list')
    # the position is the generator's: where the node sits decides WHEN n8n runs it (v1 runs the trigger's branches top-down)
    drifted(lambda n: n.update(position=[-680, 760]), 'a node moved below Sweep Config')
    drifted(lambda n: n.update(position=[0, 0]), 'a node moved anywhere else')
    raised = copy.deepcopy(canon)
    node(raised, 'Sweep Config')['position'] = [-680, -500]          # someone moved another branch above the wipe
    c.check(G.check_sweep(raised) is False and G.patch_sweep(raised) is True
            and v1_order(raised, 'Every 6 Hours')[0] == NODE and node(raised, NODE)['position'] == [-680, -500 - G.ROW],
            f'the wipe follows above the highest other branch: {node(raised, NODE)["position"]}')
    r = subprocess.run([sys.executable, str(N8N / 'apply-personal-wipe.py'), '--check'], capture_output=True, text=True)
    c.check(r.returncode == 0 and 'personal wipe: OK' in r.stdout, f'--check: {r.returncode} {r.stdout.strip()} {r.stderr.strip()[-300:]}')

with Case('sibling_generators_agree: the other owners of the Sweep stay green with the node in place') as c:
    for script in ('apply-learning-a.py', 'apply-sweep-clone-deactivate.py'):
        r = subprocess.run([sys.executable, str(N8N / script), '--check'], capture_output=True, text=True)
        c.check(r.returncode == 0, f'{script} --check: {r.returncode} {r.stdout.strip()} {r.stderr.strip()[-300:]}')

with Case('verifier_accepts_the_canon: verify-personal-chats.py is OK — its tripwire self-check included') as c:
    r = subprocess.run([sys.executable, str(N8N / 'verify-personal-chats.py')], capture_output=True, text=True)
    c.check(r.returncode == 0 and 'personal chats verify: OK' in r.stdout, f'{r.returncode} {r.stdout.strip()[-600:]} {r.stderr.strip()[-300:]}')

with Case('verifier_rejects_a_broken_sweep_on_a_scratch_copy: --dir on copies of the Sweep — a dry-run gate, a disabled node, a '
          'missing file — exits 1 with the finding; the untouched copy is the positive control') as c:
    def verify_copy(mutate):
        # the whole canon set (the verifier has a group per piece — the readers read three more files), the Sweep mutated
        with tempfile.TemporaryDirectory() as d:
            for p in (N8N / 'workflows').glob('*.json'):
                (Path(d) / p.name).write_bytes(p.read_bytes())
            w = wf(SWEEP_FILE)
            if mutate:
                mutate(w)
            (Path(d) / SWEEP_FILE).write_text(json.dumps(w, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
            return subprocess.run([sys.executable, str(N8N / 'verify-personal-chats.py'), '--dir', d], capture_output=True, text=True)

    def gate_behind_dry_run(w):
        w['connections']['Every 6 Hours']['main'][0] = [e for e in w['connections']['Every 6 Hours']['main'][0] if e['node'] != NODE]
        w['connections']['Trial/Churn Dry Run?']['main'][1].append({'node': NODE, 'type': 'main', 'index': 0})

    r = verify_copy(None)
    c.check(r.returncode == 0 and 'personal chats verify: OK' in r.stdout, f'positive control: {r.returncode} {r.stdout.strip()[-300:]}')
    r = verify_copy(gate_behind_dry_run)
    c.check(r.returncode == 1 and 'FAIL [wipe]' in r.stdout and 'ONLY from Every 6 Hours' in r.stdout
            and 'personal chats verify:' in r.stdout and 'verify: OK' not in r.stdout, f'a dry-run gate: {r.returncode} {r.stdout.strip()[-400:]}')
    r = verify_copy(lambda w: node(w, NODE).update(disabled=True))
    c.check(r.returncode == 1 and 'is disabled' in r.stdout, f'a disabled node: {r.returncode} {r.stdout.strip()[-400:]}')
    r = verify_copy(lambda w: node(w, NODE).update(position=[-680, 760]))
    c.check(r.returncode == 1 and 'must be the FIRST branch' in r.stdout,
            f'the wipe below Sweep Config: {r.returncode} {r.stdout.strip()[-400:]}')
    r = verify_copy(lambda w: w['nodes'].remove(node(w, NODE)))
    c.check(r.returncode == 1 and 'expected exactly one node' in r.stdout, f'a missing node: {r.returncode} {r.stdout.strip()[-400:]}')
    with tempfile.TemporaryDirectory() as d:
        r = subprocess.run([sys.executable, str(N8N / 'verify-personal-chats.py'), '--dir', d], capture_output=True, text=True)
        c.check(r.returncode == 1 and 'is missing' in r.stdout, f'a missing file: {r.returncode} {r.stdout.strip()[-300:]}')

report()
