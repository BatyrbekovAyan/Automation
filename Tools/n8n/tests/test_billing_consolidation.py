#!/usr/bin/env python3
"""A2 «защита платящих» (2026-10-14): a reinstall's first RevenueCat event that carries no plan (a top-up) no longer leaves
the paying owner on trial — the alias merge hands the plan of an ACTIVE alias of a paid tier over to a recipient that is
still trialing without a period, in the same upsert that moves the balance; and the merge gains the re-key primitive
(REKEY_CTE) that RevenueCat Events renders with install = NULL (re-keys nothing) and Task A3's identity-heal will render
with the token's install. Spec docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md §2.4, §3 A2.

What runs — no n8n server, no network, never production (tests/rc_events_chain.py): the REAL statements of the canonical
RevenueCat Events JSON (= billing_consolidation.py's texts, written by apply-billing-consolidation.py), bound from the item
the real Map Event makes of a RevenueCat body through the node's own queryReplacement (n8n's engine), on session temp
tables of the local staging Postgres — node by node (Q: the Consolidate Aliases statement alone) and through the whole
graph (G, and the Block 1 history T1–T5 / A1–A3 of probe-billing.py's Task 16 part, now offline). «Not rewritten» is
measured by the row's physical version (xmin). The race (R) runs the statement on TWO real connections; the same race
over the statement without `for update` must credit twice (non-vacuous). The verifier half (verify-billing-consolidation.py)
is exercised at the end: the canonical set passes, and each one-mutation copy of RevenueCat Events fails it.

    python3 Tools/n8n/tests/test_billing_consolidation.py
"""
import contextlib
import copy
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from harness import N8N, WF, Case, code, module, node, report, sql_literal
from rc_events_chain import CANON as W, RC_FILE, SETUP, RaceFixture, Session, bound_sql, deliver, ok_answer, race

BC = module('billing_consolidation.py')
guard = module('verify-sql-comments.py')
GEN = N8N / 'apply-billing-consolidation.py'
VERIFY = N8N / 'verify-billing-consolidation.py'
CONS = 'Consolidate Aliases'

U = '$RCAnonymousID:' + 'a2a2a2a2b3b3b3b3' * 2        # the paying owner after the reinstall — the recipient
OLD = '$RCAnonymousID:' + 'c4c4c4c4d5d5d5d5' * 2       # the same owner before it — an alias
OLD2 = '$RCAnonymousID:' + 'e6e6e6e6f7f7f7f7' * 2      # an older identity of the same owner — another alias
PERIOD_MS = 4102444800000                               # 2100-01-01T00:00:00Z — a plan-carrying event's own period
PLUS30, PLUS10, PLUS40, PLUS60 = (f"now() + interval '{d} days'" for d in (30, 10, 40, 60))
TODAY = "(now() at time zone 'Asia/Almaty')::date"


# ---------------------------------------------------------------------------------------------------- fixtures
def event(type_, eid, app_user_id=U, aliases=(OLD,), **kw):
    """A RevenueCat envelope as a reinstall delivers it: the event under the NEW id, the old one in original_app_user_id AND
    aliases[] (live shape, execs 3150/3151/3153 of 2026-08-26)."""
    e = {'type': type_, 'app_user_id': app_user_id, 'original_app_user_id': aliases[0] if aliases else app_user_id,
         'aliases': [app_user_id, *aliases], 'store': 'APP_STORE', 'environment': 'SANDBOX', 'event_timestamp_ms': 1760400000000}
    if eid is not None:
        e['id'] = eid
    e.update(kw)
    return {'api_version': '1.0', 'event': e}


def topup(eid, **kw):
    return event('NON_RENEWING_PURCHASE', eid, product_id='topup.dialogs.500', **kw)


def renewal(eid, ent='tier_start', product='sub.start.month', exp=PERIOD_MS, **kw):
    return event('RENEWAL', eid, entitlement_ids=[ent], product_id=product, expiration_at_ms=exp, **kw)


def transfer(eid, frm, to):
    return {'api_version': '1.0', 'event': {'id': eid, 'type': 'TRANSFER', 'transferred_from': frm, 'transferred_to': to,
                                             'store': 'APP_STORE', 'environment': 'SANDBOX'}}


def seed(pg, uid, plan, status, balance=0, period='null', product=None):
    pg.x(f"insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, product_id) values "
         f"({sql_literal(uid)}, {sql_literal(plan)}, {sql_literal(status)}, {balance}, {period}, {sql_literal(product)});")


def dialogs(pg, uid, n, days_back=0, tag='c'):
    pg.x("insert into dialog_counts (app_user_id, chat_id, d) values " + ", ".join(
        f"({sql_literal(uid)}, '{tag}{i}', {TODAY} - {days_back})" for i in range(n)) + " on conflict do nothing;")


def sub(pg, uid):
    """One subscribers row: its version, the plan columns, the balance; `period` as text (UTC session), `clamped` = the
    period is not in the future, `started` = trial_started_at set. None when there is no row."""
    r = pg.q("select xmin::text as ver, plan, status, topup_balance as bal, current_period_end::text as period, product_id as product, "
             "(trial_started_at is not null) as started, (current_period_end <= now()) as clamped, "
             "abs(extract(epoch from current_period_end - now())) < 120 as near_now "
             f"from subscribers where app_user_id = {sql_literal(uid)}")
    return r[0] if r else None


def plan_of(pg, uid):
    s = sub(pg, uid)
    return s and (s['plan'], s['status'], s['bal'], s['period'], s['product'])


def used(pg, uid):
    """Dialogs of the current month (Asia/Almaty) — the window Count Dialog and Get Usage count."""
    return pg.q(f"select count(*)::int as n from dialog_counts where app_user_id = {sql_literal(uid)} and "
                f"date_trunc('month', d) = date_trunc('month', {TODAY})")[0]['n']


def versions(pg):
    """Every row's physical version in the tables the alias merge writes — unchanged = not rewritten at all."""
    return {t: pg.q(f"select xmin::text as ver, * from {t} order by 2, 3") for t in ('subscribers', 'dialog_counts', 'bot_profiles')}


def item_of(body):
    wh = {'body': body, 'headers': {}, 'params': {}, 'query': {}}
    return code(W, 'Map Event', item={'json': {}}, named={'Webhook': wh}, strict=True)[0]['json'], wh


def merge_sql(body, w=W):
    """The Consolidate Aliases statement of `w` for the item the canonical Map Event makes of `body` — what the node sends."""
    item, wh = item_of(body)
    assert item.get('alias_ids'), f'premise: the event must name an alias to merge: {item}'
    return bound_sql(w, CONS, item, {'Webhook': wh})


def merge(pg, body, w=W):
    """Run the Consolidate Aliases statement alone; its result row."""
    return pg.q(merge_sql(body, w))[0]


def with_query(name, query):
    """A deep copy of the canonical graph whose node `name` holds `query`."""
    w = copy.deepcopy(W)
    node(w, name)['parameters']['query'] = query
    return w


def heal_graph(install):
    """The graph with the CTE chain rendered for a NAMED install — what Task A3's identity-heal will send (same chain, the
    install instead of NULL); the event payload parameters are RevenueCat's."""
    return with_query(CONS, BC.CONSOLIDATE_SQL(dict(BC.RC_CONSOLIDATE, install=sql_literal(install) + '::text')))


# ---------------------------------------------------------------------------------------------------- M: module ↔ canon
with Case('A2 M0: the canon holds the module\'s texts — apply-billing-consolidation.py --check exits 0, both nodes equal '
          'billing_consolidation.py byte for byte') as c:
    r = subprocess.run([sys.executable, str(GEN), '--check'], capture_output=True, text=True)
    c.check(r.returncode == 0 and 'OK' in r.stdout, f'--check exit {r.returncode}: {r.stdout}{r.stderr}')
    c.check(node(W, CONS)['parameters']['query'] == BC.CONSOLIDATE_SQL(BC.RC_CONSOLIDATE), 'Consolidate Aliases != module')
    c.check(node(W, 'Transfer Subscriber')['parameters']['query'] == BC.TRANSFER_SQL, 'Transfer Subscriber != module')

with Case('A2 M1: every rendering is clean SQL text — no token left, nothing verify-sql-comments refuses, the RevenueCat '
          'renderings use exactly the parameters their hand-authored arrays supply (8 / 3)') as c:
    for label, q, want in (('consolidate', BC.CONSOLIDATE_SQL(BC.RC_CONSOLIDATE), 8), ('transfer', BC.TRANSFER_SQL, 3),
                           ('heal-shaped', BC.CONSOLIDATE_SQL(dict(BC.RC_CONSOLIDATE, install='$9::text')), 9)):
        c.check(not re.search(r'<[A-Z_]+>', q), f'{label}: an unrendered token')
        c.check(guard.check_query(q) == [], f'{label}: verify-sql-comments: {guard.check_query(q)}')
        hi = max(int(m.group(0)[1:]) for k, a, b, _ in guard.lex(q) if k == 'code' for m in guard.PLACEHOLDER_TOKEN.finditer(q[a:b]))
        c.check(hi == want, f'{label}: highest placeholder ${hi}, expected ${want}')
    for name, rep in ((CONS, BC.RC_CONSOLIDATE_REPLACEMENT), ('Transfer Subscriber', BC.RC_TRANSFER_REPLACEMENT)):
        c.check(node(W, name)['parameters']['options']['queryReplacement'] == rep, f'{name}: queryReplacement drifted from the module')
    c.check(BC.RC_CONSOLIDATE['install'] == 'null::text', 'RevenueCat Events must render the re-key with install = SQL NULL')


# ---------------------------------------------------------------------------------------------------- Q: the statement
with Case('A2 Q1 top-up first after a reinstall, NO recipient row: alias business/active/+30d/500 → the recipient row is '
          'created business/active/<the alias\'s period>/sub.business.month/500 (trial_started_at set); the alias retired '
          'expired/0/clamped; nothing re-keyed') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        period = sub(pg, OLD)['period']
        out = merge(pg, topup('evt-q1'))
        u, o = sub(pg, U), sub(pg, OLD)
        c.check(u and (u['plan'], u['status'], u['bal'], u['period'], u['product'], u['started']) ==
                ('business', 'active', 500, period, 'sub.business.month', True), f'recipient {u} (alias period {period})')
        c.check(o and (o['status'], o['bal'], o['clamped'], o['near_now']) == ('expired', 0, True, True), f'alias {o}')
        c.check((out['plan_after'], out['balance_after'], out['rekeyed_rows'], out['retired_rows'], out['event_id']) ==
                ('business', 500, 0, 1, 'evt-q1'), f'result {out}')

with Case('A2 Q2 the recipient is a fresh install\'s seed trial/trialing/NULL/0 → business/active/<the alias\'s period>/'
          'sub.business.month/500') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        period = sub(pg, OLD)['period']
        merge(pg, topup('evt-q2'))
        c.check(plan_of(pg, U) == ('business', 'active', 500, period, 'sub.business.month'), f'recipient {sub(pg, U)}')

with Case('A2 Q3 the recipient has its own plan (start/active/+10d/40) → its plan, status, period and SKU are untouched; the '
          'balance is summed (540); the alias is retired') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'start', 'active', 40, PLUS10, 'sub.start.month')
        before = sub(pg, U)
        merge(pg, topup('evt-q3'))
        u = sub(pg, U)
        c.check((u['plan'], u['status'], u['period'], u['product'], u['bal']) ==
                ('start', 'active', before['period'], 'sub.start.month', 540), f'recipient {before} -> {u}')
        c.check(sub(pg, OLD)['status'] == 'expired' and sub(pg, OLD)['bal'] == 0, f'alias {sub(pg, OLD)}')

with Case('A2 Q4 the alias is EXPIRED (business/expired/+30d/500) → its plan is NOT handed over (the recipient stays '
          'trial/trialing/NULL); the balance still moves and the alias is retired (only retire + sum)') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'expired', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        merge(pg, topup('evt-q4'))
        c.check(plan_of(pg, U) == ('trial', 'trialing', 500, None, None), f'recipient {sub(pg, U)}')
        c.check((sub(pg, OLD)['status'], sub(pg, OLD)['bal'], sub(pg, OLD)['clamped']) == ('expired', 0, True), f'alias {sub(pg, OLD)}')

with Case('A2 Q5 an event WITH a plan (RENEWAL tier_start) → the merge hands nothing over (the recipient is still '
          'trial/trialing/NULL after the node; the balance moved); through the graph the event\'s own plan wins — '
          'start/active/its period/sub.start.month') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        merge(pg, renewal('evt-q5'))
        c.check(plan_of(pg, U) == ('trial', 'trialing', 500, None, None), f'after the node: {sub(pg, U)}')
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        r = deliver(pg, renewal('evt-q5'))
        c.check(ok_answer(r) and CONS in r.trail and 'Upsert Subscriber' in r.trail, f'{r}')
        c.check(plan_of(pg, U) == ('start', 'active', 500, '2100-01-01 00:00:00+00', 'sub.start.month'), f'through the graph: {sub(pg, U)}')

with Case('A2 Q6 replay → ZERO row versions: the same statement again finds the alias set settled — no subscribers, '
          'dialog_counts or bot_profiles row is rewritten (every xmin unchanged); its result reports nothing done') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        dialogs(pg, OLD, 3)
        pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id) values ('p-q6', {sql_literal(OLD)}, 'whatsapp', 'inst-A');")
        merge(pg, topup('evt-q6'))
        before = versions(pg)
        out = merge(pg, topup('evt-q6'))
        c.check(versions(pg) == before, f'the replay rewrote a row:\n{before}\n->\n{versions(pg)}')
        c.check((out['retired_rows'], out['carried_rows'], out['balance_after'], out['plan_after'], out['rekeyed_rows']) ==
                (0, 0, None, None, 0), f'result {out}')

EMPTY_READ = ("alter table pg_temp.rc_event_ledger rename to rc_event_ledger_real; "
              "create temp table rc_event_ledger (like pg_temp.rc_event_ledger_real including all);",
              "drop table pg_temp.rc_event_ledger; alter table pg_temp.rc_event_ledger_real rename to rc_event_ledger;")
with Case('A2 Q6b replay through the GRAPH, past the gate (its read sees nothing): the merge is settled and the upsert finds '
          'the id recorded — still 200, every row byte-identical (xmin), the plan stays carried') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        dialogs(pg, OLD, 2)
        first = deliver(pg, topup('evt-q6b'))
        before = versions(pg)
        again = deliver(pg, topup('evt-q6b'), faults={'Dedup Gate': EMPTY_READ})
        c.check(ok_answer(first) and ok_answer(again) and CONS in again.trail and 'Upsert Subscriber' in again.trail,
                f'premise: the replay must reach the merge and the upsert: {again}')
        c.check(versions(pg) == before, f'the replay rewrote a row:\n{before}\n->\n{versions(pg)}')
        c.check(plan_of(pg, U)[:3] == ('business', 'active', 1000), f'recipient {sub(pg, U)}')

with Case('A2 Q7 re-key: RevenueCat Events renders install = NULL → the live bot_profiles rows under the alias are untouched '
          '(same xmin, same app_user_id); positive control — the SAME chain rendered for install inst-A (Task A3\'s shape) '
          'moves exactly inst-A\'s live rows under an alias: not another install\'s, not a retired one, not a row of a '
          'non-alias id; its replay moves nothing') as c:
    rows_sql = "select xmin::text as ver, profile_id, app_user_id from bot_profiles order by profile_id"
    fixture = ("insert into bot_profiles (profile_id, app_user_id, channel, owner_id, deleted_at) values "
               f"('p-mine', {sql_literal(OLD)}, 'whatsapp', 'inst-A', null), "
               f"('p-mine-2', {sql_literal(OLD2)}, 'telegram', 'inst-A', null), "
               f"('p-other-install', {sql_literal(OLD)}, 'telegram', 'inst-B', null), "
               f"('p-retired', {sql_literal(OLD)}, 'whatsapp', 'inst-A', now()), "
               f"('p-not-alias', '$RCAnonymousID:{'9' * 32}', 'whatsapp', 'inst-A', null), "
               f"('p-legacy', {sql_literal(OLD)}, 'whatsapp', null, null);")
    body = topup('evt-q7', aliases=(OLD, OLD2))
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        pg.x(fixture)
        before = pg.q(rows_sql)
        out = merge(pg, body)
        c.check(pg.q(rows_sql) == before and out['rekeyed_rows'] == 0, f'RevenueCat re-keyed: {before} -> {pg.q(rows_sql)}')
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        pg.x(fixture)
        heal = heal_graph('inst-A')
        out = merge(pg, body, heal)
        got = {r['profile_id']: r['app_user_id'] for r in pg.q(rows_sql)}
        c.check(out['rekeyed_rows'] == 2 and got == {'p-mine': U, 'p-mine-2': U, 'p-other-install': OLD, 'p-retired': OLD,
                                                     'p-not-alias': '$RCAnonymousID:' + '9' * 32, 'p-legacy': OLD},
                f'heal-shaped re-key: {out["rekeyed_rows"]} rows, {got}')
        before = pg.q(rows_sql)
        out = merge(pg, body, heal)
        c.check(pg.q(rows_sql) == before and out['rekeyed_rows'] == 0, f'the heal replay re-keyed again: {out}')

with Case('A2 Q8 several aliases: the ACTIVE one with the LATEST period is handed over (an active NULL-period alias loses '
          'to a dated one — nulls last); the balances of all of them are summed; all are retired') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'start', 'active', 100, PLUS10, 'sub.start.month')
        seed(pg, OLD2, 'network', 'active', 200, PLUS40, 'sub.network.year')
        seed(pg, '$RCAnonymousID:' + '8' * 32, 'business', 'active', 300, 'null', 'sub.business.month')
        period = sub(pg, OLD2)['period']
        merge(pg, topup('evt-q8', aliases=(OLD, OLD2, '$RCAnonymousID:' + '8' * 32)))
        c.check(plan_of(pg, U) == ('network', 'active', 600, period, 'sub.network.year'), f'recipient {sub(pg, U)}')
        c.check(all(sub(pg, x)['status'] == 'expired' and sub(pg, x)['bal'] == 0 for x in (OLD, OLD2, '$RCAnonymousID:' + '8' * 32)),
                'every alias must be retired')

for label, plan, status, period in (('trialing WITH a period (trialing/+5d)', 'trial', 'trialing', "now() + interval '5 days'"),
                                     ('expired (trial/expired/-1d)', 'trial', 'expired', "now() - interval '1 day'"),
                                     ('in grace (business/grace/+2d)', 'business', 'grace', "now() + interval '2 days'")):
    with Case(f'A2 Q9 the recipient is {label} → no plan is handed over (only a trialing recipient WITHOUT a period takes '
              f'one: nothing is ever downgraded or overwritten); the balance moves') as c:
        with Session(SETUP) as pg:
            seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
            seed(pg, U, plan, status, 0, period)
            before = sub(pg, U)
            merge(pg, topup('evt-q9'))
            u = sub(pg, U)
            c.check((u['plan'], u['status'], u['period'], u['product'], u['bal']) ==
                    (before['plan'], before['status'], before['period'], before['product'], 500), f'{before} -> {u}')

for label, plan, status in (('in grace', 'business', 'grace'), ('trialing', 'business', 'trialing'),
                            ('active with plan none (an unknown entitlement: base quota 0 against the trial\'s 150)', 'none', 'active')):
    with Case(f'A2 Q10 the alias is {label} → it is no paid subscription: nothing is handed over, the balance moves') as c:
        with Session(SETUP) as pg:
            seed(pg, OLD, plan, status, 500, PLUS30, 'sub.business.month')
            seed(pg, U, 'trial', 'trialing', 0)
            merge(pg, topup('evt-q10'))
            c.check(plan_of(pg, U) == ('trial', 'trialing', 500, None, None), f'recipient {sub(pg, U)}')

with Case('A2 Q11 the ACTIVE alias holds NO balance and the recipient has no row → the row is created with the alias\'s plan '
          'at a zero balance (the insert no longer waits for money: the plan would vanish with the retired alias)') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 0, PLUS30, 'sub.business.month')
        period = sub(pg, OLD)['period']
        out = merge(pg, topup('evt-q11'))
        c.check(plan_of(pg, U) == ('business', 'active', 0, period, 'sub.business.month') and out['plan_after'] == 'business',
                f'recipient {sub(pg, U)} result {out}')

with Case('A2 Q12 nothing to hand over (the alias already retired, no balance) and NO recipient row → the row is created '
          'trial/trialing/0 with trial_started_at (what Create seeds); a second run writes nothing') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'expired', 0, "now() - interval '1 day'", 'sub.business.month')
        merge(pg, topup('evt-q12'))
        u = sub(pg, U)
        c.check(u and (u['plan'], u['status'], u['bal'], u['period'], u['started']) == ('trial', 'trialing', 0, None, True), f'{u}')
        before = versions(pg)
        merge(pg, topup('evt-q12'))
        c.check(versions(pg) == before, 'the second run rewrote a row')

with Case('A2 Q13 EXPIRATION (an event without a plan) under the new id with an active alias → the merge hands the plan '
          'over, the event\'s own status follows in Upsert Subscriber: business/expired/<the alias\'s period> — the owner '
          'keeps the churn grace of his period instead of an instant-sweep trial/expired/NULL') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 0, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        period = sub(pg, OLD)['period']
        r = deliver(pg, event('EXPIRATION', 'evt-q13'))
        c.check(ok_answer(r) and plan_of(pg, U) == ('business', 'expired', 0, period, 'sub.business.month'), f'{r} / {sub(pg, U)}')

with Case('A2 Q14 the paid usage (I-4) still moves with the money, unchanged: the alias\'s dialogs of the current month are '
          'copied to the recipient, last month\'s are not; a replay copies nothing (zero versions)') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        dialogs(pg, OLD, 3)
        dialogs(pg, OLD, 2, days_back=40, tag='old')
        out = merge(pg, topup('evt-q14'))
        c.check(used(pg, U) == 3 and out['carried_rows'] == 3, f'used {used(pg, U)} result {out}')
        before = versions(pg)
        merge(pg, topup('evt-q14'))
        c.check(versions(pg) == before, 'the replay rewrote a row')


# ---------------------------------------------------------------------------------------------------- G: the graph
with Case('A2 G1 the whole delivery — a top-up first after a reinstall, no recipient row: 200; the owner is '
          'business/active/<the alias\'s period>/sub.business.month with 1000 (500 moved + the top-up\'s 500); the alias '
          'retired; ONE ledger row') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        period = sub(pg, OLD)['period']
        r = deliver(pg, topup('evt-g1'))
        c.check(ok_answer(r) and r.trail[-3:] == ['If Has Payload?', 'Upsert Subscriber', 'Respond 200'], f'{r}')
        c.check(plan_of(pg, U) == ('business', 'active', 1000, period, 'sub.business.month'), f'owner {sub(pg, U)}')
        c.check((sub(pg, OLD)['status'], sub(pg, OLD)['bal']) == ('expired', 0), f'alias {sub(pg, OLD)}')
        c.check([x['event_id'] for x in pg.q('select event_id from rc_event_ledger')] == ['evt-g1'], 'ledger')

with Case('A2 G2 the same delivery when Create already seeded the owner trial/trialing (the usual order: a bot first, then '
          'the top-up) → business/active/<the alias\'s period>/1000') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 0)
        period = sub(pg, OLD)['period']
        r = deliver(pg, topup('evt-g2'))
        c.check(ok_answer(r) and plan_of(pg, U) == ('business', 'active', 1000, period, 'sub.business.month'), f'{r} / {sub(pg, U)}')


# ---------------------------------------------------------------------------------------------------- T/A: Block 1 history
# probe-billing.py Part 6 (Task 16/17, a live probe) — its cases T1–T5, A1/A2 (and A3 in the race section below), offline,
# through the canonical graph: the Block 1 guarantees hold after A2.
with Case('A2 history T1 TRANSFER happy path: a paid source with a top-up and 4 dialogs → the destination (own top-up 500) gets '
          'business/active/the period/1000 and used 4 (the snapshot was accepted); the source expired/0/clamped to ~now; '
          'T1b a replay changes nothing; T1c empty arrays are a clean no-op') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, U, 'trial', 'trialing', 500)
        dialogs(pg, OLD, 4)
        period = sub(pg, OLD)['period']
        r = deliver(pg, transfer('evt-t1', [OLD], [U]))
        c.check(ok_answer(r) and 'Transfer Subscriber' in r.trail, f'{r}')
        c.check(plan_of(pg, U) == ('business', 'active', 1000, period, 'sub.business.month') and used(pg, U) == 4,
                f'destination {sub(pg, U)} used {used(pg, U)}')
        o = sub(pg, OLD)
        c.check((o['status'], o['bal'], o['near_now']) == ('expired', 0, True), f'source {o}')
        before = versions(pg)
        again = deliver(pg, transfer('evt-t1', [OLD], [U]))
        c.check(ok_answer(again) and plan_of(pg, U)[:3] == ('business', 'active', 1000) and used(pg, U) == 4,
                f'T1b replay: {again} / {sub(pg, U)}')
        c.check(versions(pg)['dialog_counts'] == before['dialog_counts'], 'T1b replay rewrote usage rows')
        empty = deliver(pg, transfer('evt-t1c', [], []))
        c.check(empty.answer == (200, {'success': True, 'noop': True}), f'T1c: {empty}')

with Case('A2 history T2 a RENEWAL under the new id landed BEFORE the TRANSFER: the older source snapshot does NOT roll the '
          'destination back (its +60d period byte-identical); the money still moves') as c:
    with Session(SETUP) as pg:
        seed(pg, U, 'business', 'active', 0, PLUS60, 'sub.business.month')
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        before = sub(pg, U)
        deliver(pg, transfer('evt-t2', [OLD], [U]))
        u = sub(pg, U)
        c.check((u['plan'], u['status'], u['bal'], u['period']) == ('business', 'active', 500, before['period']), f'{before} -> {u}')
        c.check((sub(pg, OLD)['status'], sub(pg, OLD)['bal']) == ('expired', 0), f'source {sub(pg, OLD)}')

with Case('A2 history T3 an EXPIRED source with a LATER period vs a LIVE trialing destination: the snapshot is refused (still '
          'trial/trialing/NULL) and so is the usage (200 dialogs stay behind); the money moves') as c:
    with Session(SETUP) as pg:
        seed(pg, U, 'trial', 'trialing', 500)
        seed(pg, OLD, 'business', 'expired', 500, PLUS30, 'sub.business.month')
        dialogs(pg, OLD, 200)
        deliver(pg, transfer('evt-t3', [OLD], [U]))
        c.check(plan_of(pg, U) == ('trial', 'trialing', 1000, None, None) and used(pg, U) == 0, f'{sub(pg, U)} used {used(pg, U)}')
        o = sub(pg, OLD)
        c.check((o['status'], o['bal'], o['clamped']) == ('expired', 0, True), f'source {o}')

with Case('A2 history T4 a top-up-only source with a NULL period: retiring it leaves 3 days of grace (the period clamped to ~now, '
          'never NULL — NULL is an instant sweep candidate)') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'trial', 'trialing', 500)
        deliver(pg, transfer('evt-t4', [OLD], [U]))
        c.check(plan_of(pg, U) == ('trial', 'trialing', 500, None, None), f'destination {sub(pg, U)}')
        o = sub(pg, OLD)
        c.check(o['period'] is not None and (o['status'], o['bal'], o['near_now']) == ('expired', 0, True), f'source {o}')

with Case('A2 history T5 multi-id arrays: the snapshot goes to EVERY transferred_to id, the money to the first only (both sources '
          'summed); every source retired with grace') as c:
    X, Y = '$RCAnonymousID:' + '1' * 32, '$RCAnonymousID:' + '2' * 32
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        seed(pg, OLD2, 'trial', 'trialing', 500)
        deliver(pg, transfer('evt-t5', [OLD, OLD2], [X, Y]))
        c.check(plan_of(pg, X)[:3] == ('business', 'active', 1000) and plan_of(pg, Y)[:3] == ('business', 'active', 0),
                f'X {sub(pg, X)} Y {sub(pg, Y)}')
        c.check(all((sub(pg, s)['status'], sub(pg, s)['bal'], sub(pg, s)['near_now']) == ('expired', 0, True) for s in (OLD, OLD2)),
                f'sources {sub(pg, OLD)} {sub(pg, OLD2)}')

with Case('A2 history A1+A2 alias merge (the real reinstall shape): a RENEWAL under the new id naming the old one → the balance and '
          'the month\'s 3 dialogs move (the 40-day-old one does not), the event\'s own period reaches the row (the payload '
          'survives the merge), the old identity is retired with grace; A2 a replay of the alias set (the next event) '
          'credits nothing and rewrites no alias or usage row') as c:
    with Session(SETUP) as pg:
        seed(pg, OLD, 'business', 'active', 500, PLUS30, 'sub.business.month')
        dialogs(pg, OLD, 3)
        dialogs(pg, OLD, 1, days_back=40, tag='lastmonth')
        r = deliver(pg, renewal('evt-a1', ent='tier_business', product='sub.business.month'))
        c.check(ok_answer(r) and plan_of(pg, U) == ('business', 'active', 500, '2100-01-01 00:00:00+00', 'sub.business.month')
                and used(pg, U) == 3, f'{r} / {sub(pg, U)} used {used(pg, U)}')
        o = sub(pg, OLD)
        c.check((o['status'], o['bal'], o['near_now']) == ('expired', 0, True), f'old {o}')
        before = versions(pg)
        again = deliver(pg, renewal('evt-a2', ent='tier_business', product='sub.business.month'))
        c.check(ok_answer(again) and plan_of(pg, U)[:3] == ('business', 'active', 500) and used(pg, U) == 3, f'{again} / {sub(pg, U)}')
        after = versions(pg)
        c.check(after['dialog_counts'] == before['dialog_counts'] and
                [r for r in after['subscribers'] if r['app_user_id'] == OLD] == [r for r in before['subscribers'] if r['app_user_id'] == OLD],
                'the alias replay rewrote the old identity or a usage row')


# ---------------------------------------------------------------------------------------------------- R: the race
def race_merge(w, seed_rows, body):
    """Two deliveries of the same alias-bearing event race through `w`'s Consolidate Aliases statement on two connections.
    Returns (recipient (plan, status, balance), alias (status, balance), the wait B was in)."""
    with RaceFixture(tables=('subscribers', 'dialog_counts', 'bot_profiles')) as fx:
        fx.main.x(seed_rows)
        stmt = merge_sql(body, w)
        waited = race(fx, stmt, stmt)
        u = fx.main.q(f'select plan, status, topup_balance from subscribers where app_user_id = {sql_literal(U)}')
        o = fx.main.q(f'select status, topup_balance from subscribers where app_user_id = {sql_literal(OLD)}')
        return (tuple(u[0].values()) if u else None), (tuple(o[0].values()) if o else None), waited


SEED_ACTIVE = ("insert into subscribers (app_user_id, plan, status, topup_balance, current_period_end, product_id) values "
               f"({sql_literal(OLD)}, 'business', 'active', 500, now() + interval '30 days', 'sub.business.month');")
SEED_TOPUP_ONLY = ("insert into subscribers (app_user_id, plan, status, topup_balance) values "
                   f"({sql_literal(OLD)}, 'trial', 'trialing', 500);")
for label, seed_rows, body, want in (
        ('(history A3) the probe\'s burst: a top-up-only alias, a RENEWAL', SEED_TOPUP_ONLY, renewal('evt-r1', ent='tier_business'),
         ('trial', 'trialing', 500)),
        ('a top-up first with an ACTIVE alias (the plan handed over once)', SEED_ACTIVE, topup('evt-r2'), ('business', 'active', 500)),
        ('the same, the recipient already seeded trial/trialing', SEED_ACTIVE +
         f"insert into subscribers (app_user_id, plan, status) values ({sql_literal(U)}, 'trial', 'trialing');",
         topup('evt-r3'), ('business', 'active', 500))):
    with Case(f'A2 R {label}: delivery B blocks on A\'s lock of the alias row; after A commits B sees it retired — the '
              f'balance credited ONCE, the plan handed over once') as c:
        u, o, waited = race_merge(W, seed_rows, body)
        c.check(waited != '', 'premise: B must have WAITED on A\'s lock (no race happened)')
        c.check(u == want and o == ('expired', 0), f'recipient {u} alias {o} (expected {want}, expired/0) — B waited on {waited!r}')

NO_LOCK = with_query(CONS, node(W, CONS)['parameters']['query'].replace('    and app_user_id <> $1\n  for update\n',
                                                                        '    and app_user_id <> $1\n'))
with Case('A2 R non-vacuous: the SAME race over the statement WITHOUT `for update` credits TWICE (1000) — the lock-produced '
          'sum is what R rules out') as c:
    c.check(node(NO_LOCK, CONS)['parameters']['query'] != node(W, CONS)['parameters']['query'], 'premise: the mutation changed nothing')
    u, o, waited = race_merge(NO_LOCK, SEED_ACTIVE, topup('evt-r4'))
    c.check(u is not None and u[2] == 1000, f'without the lock: recipient {u} alias {o} waited {waited!r} (expected the double credit 1000)')


# ---------------------------------------------------------------------------------------------------- V: the verifier
def run_verifier(*args):
    r = subprocess.run([sys.executable, str(VERIFY), *args], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


@contextlib.contextmanager
def canon_copy():
    d = Path(tempfile.mkdtemp(prefix='a2-verify-'))
    try:
        for p in WF.glob('*.json'):
            shutil.copy(p, d / p.name)
        yield d
    finally:
        shutil.rmtree(d)


def nd(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def q_sub(label, name, old, new):
    """A mutation of node `name`'s query: the first `old` becomes `new` (the anchor must exist — no vacuous mutation)."""
    def m(w):
        p = nd(w, name)['parameters']
        assert old in p['query'], f'mutation anchor missing: {old!r}'
        p['query'] = p['query'].replace(old, new, 1)
    m.__name__ = 'm_' + label
    return m


# a comment edited in the n8n editor and re-exported (the text is no longer the module's)
m_consolidate_hand_edit = q_sub('consolidate_hand_edit', CONS, 'СКЛЕЙКА ИДЕНТИЧНОСТЕЙ', 'Склейка идентичностей')
m_transfer_hand_edit = q_sub('transfer_hand_edit', 'Transfer Subscriber', 'МЕЖАККАУНТНЫЙ', 'межаккаунтный')
m_consolidate_unlocked = q_sub('consolidate_unlocked', CONS, '    and app_user_id <> $1\n  for update\n', '    and app_user_id <> $1\n')
m_carry_any_status = q_sub('carry_any_status', CONS, "where status = 'active' and plan in", 'where plan in')
m_carry_any_plan = q_sub('carry_any_plan', CONS, "and plan in ('start', 'business', 'network') and $3::text is null",
                         'and $3::text is null')
m_carry_ignores_event_plan = q_sub('carry_ignores_event_plan', CONS, "and plan in ('start', 'business', 'network') and $3::text is null",
                                   "and plan in ('start', 'business', 'network')")
m_carry_overwrites_recipient = q_sub('carry_overwrites_recipient', CONS, "    plan = case when subscribers.status = 'trialing' and subscribers.current_period_end is null\n"
                                           "                     and exists (select 1 from plan_source) then excluded.plan",
                                     '    plan = case when exists (select 1 from plan_source) then excluded.plan')
m_update_ungated = q_sub('update_ungated', CONS, "  where excluded.topup_balance > 0 or (subscribers.status = 'trialing' and subscribers.current_period_end is null\n"
                               "                     and exists (select 1 from plan_source))\n", '')
m_rekey_by_parameter = q_sub('rekey_by_parameter', CONS, 'and b.owner_id = null::text', 'and b.owner_id = $1')
m_transfer_carry_ungated = q_sub('transfer_carry_ungated', 'Transfer Subscriber', 'from accepted a, dialog_counts dc', 'from to_ids a, dialog_counts dc')


def m_consolidate_disabled(w):
    nd(w, CONS)['disabled'] = True


def m_transfer_disabled(w):
    nd(w, 'Transfer Subscriber')['disabled'] = True


def m_replacement_reordered(w):           # plan and status swapped: the SQL would read the status as the event's plan
    o = nd(w, CONS)['parameters']['options']
    o['queryReplacement'] = o['queryReplacement'].replace('($json.plan ?? null), ($json.status ?? null)',
                                                          '($json.status ?? null), ($json.plan ?? null)')


def m_consolidate_bypassed(w):            # If Needs Consolidation?[TRUE] straight into the upsert: no merge at all
    w['connections']['If Needs Consolidation?']['main'][0] = [{'node': 'If Has Payload?', 'type': 'main', 'index': 0}]


def m_consolidate_fail_open(w):           # a failed merge would go on into the upsert with the error item
    nd(w, CONS)['onError'] = 'continueRegularOutput'


def m_consolidate_other_database(w):
    nd(w, CONS)['credentials'] = {'postgres': {'id': 'devPostgres0001', 'name': 'Postgres dev'}}


# (mutation, a fragment of the assert that must name it) — the RIGHT assert fires, not merely «some failure in the file»
MUTATIONS = [(m_consolidate_hand_edit, 'Consolidate Aliases: query differs from billing_consolidation.py'),
             (m_transfer_hand_edit, 'Transfer Subscriber: query differs from billing_consolidation.py'),
             (m_consolidate_unlocked, '`locked` must take the alias rows FOR UPDATE'),
             (m_carry_any_status, "`plan_source` must take only status = 'active'"),
             (m_carry_any_plan, '`plan_source` must take only a paid tier'),
             (m_carry_ignores_event_plan, '`plan_source` must apply only when the event carries no plan'),
             (m_carry_overwrites_recipient, '`credited` hands `plan` over outside the take predicate'),
             (m_update_ungated, '`credited` must update only when there is money or a plan to hand over'),
             (m_rekey_by_parameter, 'the re-key must compare owner_id with SQL NULL'),
             (m_transfer_carry_ungated, 'Transfer Subscriber: the usage carry must drive off `accepted`'),
             (m_consolidate_disabled, 'Consolidate Aliases is disabled'),
             (m_transfer_disabled, 'Transfer Subscriber is disabled'),
             (m_replacement_reordered, 'Consolidate Aliases: queryReplacement'),
             (m_consolidate_bypassed, 'Consolidate Aliases: entered from []'),
             (m_consolidate_fail_open, "Consolidate Aliases: onError 'continueRegularOutput'"),
             (m_consolidate_other_database, "Consolidate Aliases: credential 'devPostgres0001'")]

CONTROL_RC, CONTROL_OUT = run_verifier()
with Case('A2 V0: the canonical set passes verify-billing-consolidation (exit 0)') as c:
    c.check(CONTROL_RC == 0 and 'ALL BILLING CONSOLIDATION ASSERTS PASSED' in CONTROL_OUT, f'exit {CONTROL_RC}:\n{CONTROL_OUT[-900:]}')

for fn, fragment in MUTATIONS:
    with Case(f'A2 V NEG {fn.__name__[2:]}: the one-mutation copy fails verify-billing-consolidation, naming RevenueCat Events '
              f'and the assert that catches it') as c:
        with canon_copy() as d:
            p = d / RC_FILE
            w = json.loads(p.read_text(encoding='utf-8'))
            before = json.dumps(w, sort_keys=True)
            fn(w)
            c.check(json.dumps(w, sort_keys=True) != before, 'premise: the mutation changed nothing')
            p.write_text(json.dumps(w, ensure_ascii=False, indent=2), encoding='utf-8')
            rc, out = run_verifier('--dir', str(d))
        fails = [l for l in out.splitlines() if l.startswith(f'BILLING CONSOLIDATION FAIL [{RC_FILE}]')]
        c.check(CONTROL_RC == 0, f'control exit {CONTROL_RC} — a verifier red on everything detects nothing')
        c.check(rc == 1 and any(fragment in l for l in fails), f'exit {rc}; expected a failure naming {fragment!r}; got {fails[:3]}')

report()
