#!/usr/bin/env python3
"""T2 (F1 + F2, plan docs/audits/2026-10-09-followups/F1F2-plan.md §1.3, §1.5, §1.6): the SQL core of «the bot's line»
— Tools/n8n/clone_lineage.py — on the real staging Postgres (session temp tables `like public.*` + migration M2).

Create WA/TG becomes «ensure exactly one bound clone for this channel of this bot». Two statements decide it:
  Clone Plan        what the Create may reuse (own_id), must refuse (foreign), replace (replace_ids) and adopt
                    (pred_profiles) — the predecessor rule of §1.3, proven in the DATABASE, never by the token;
  Bind Workflow Id  binds the clone and, in ONE statement, moves the bot's memory, history, outcomes, handoffs, reply
                    ledger, chat state and chat flags from the predecessor profiles and the price-list chunks and file
                    registry from the replaced clones onto the new ones, marks the predecessor rows superseded and books
                    the replaced clones in clone_retirements — re-checking every condition on the rows it writes (the
                    V6-concurrent lesson: ≈ 10 s pass between the plan and the bind).
Plus the sweep's branch-A query (clone_retirements → DELETE), the discard-failure journal and the «bot's line» fragment
for Delete Bot Files (T5).

The Create flow is emulated the way the graph of plan §1.4 runs it: plan → (foreign: refuse) → own usable: reuse, else
create → bind with the plan's lists. Every scenario runs for WhatsApp and Telegram. Real upstream statements come from
the canonical JSONs (the proxy's Bind Profile, Create's Retire Same Bot Slot / Register Profile); the deletion invariant
uses the PRE-F1 Delete Bot Files statements (Delete Ownership, Delete Chat Memory, Delete Bot Chunks — «the bot = its
current ids»: every existing deletion path must reach everything after a move, plan §1.2 p. 6).

  L1–L19  the requirement matrix (session temp tables): F1's nine prototype cases, F2 R1–R5/R8, the refusals of the
          predecessor rule, two predecessors of one chat, the retry, legacy, sentinel, the '-1' rule, protected ids, and
          L19 — a clone booked for deletion is never offered for reuse (no live row ever drives a booked clone);
  C1–C2   races on TWO real connections over a shared fixture schema (the V6-concurrent pattern): the second bind's
          snapshot predates the first's commit. C1 — two Creates of the same profile: the loser answers bound = false
          (no clone lost, the price lists follow the bound one, the retry reuses the winner). C2 — two new profiles adopt
          one predecessor: everything lands under the first committer, nothing split or deleted, the loser claims nothing;
  M1–M8   module-only statements: the plan always answers, the sweep's branch A, the discard journal, the bot's line
          (Delete Bot Files, T5), the moved tables = Delete Chat Memory's, V1 comments, the cost on a big history, and
          M8 — the chunk moves reach the prod RAG store's expression indexes (never a scan of the store).

    python3 Tools/n8n/tests/test_clone_lineage_sql.py            # the line — must pass
    python3 Tools/n8n/tests/test_clone_lineage_sql.py --head     # the same requirement matrix against the pre-F1 canon
                                                                 # (Restamp RAG Chunks + the A1 Bind Workflow Id): fails
No n8n server, no network, never production.
"""
import concurrent.futures
import json
import re
import subprocess
import sys
import time
import uuid

from harness import N8N, ROOT, Case, PgSession, bind, module, node, report, sql_literal as L, wf

HEAD_MODE = '--head' in sys.argv
PRE_F1_REV = 'e9a2f3ad'    # the last canon before the line: Restamp RAG Chunks + the A1 Bind Workflow Id
U = '$RCAnonymousID:' + '0123456789abcdef' * 2
CH = {
    'WA': {'channel': 'whatsapp', 'create': 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'key': 'botWaId', 'other': 'botTgId',
           'fcol': 'bot_wa_id'},
    'TG': {'channel': 'telegram', 'create': 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json', 'key': 'botTgId', 'other': 'botWaId',
           'fcol': 'bot_tg_id'},
}
DBF_F = 'lmjYsdNcQA2IE5rl-Delete_Bot_Files.json'
PROXY_F = 'crWappiProxy0001-Wappi_Proxy.json'
MEMORY_TABLES = ('n8n_chat_histories', 'chat_history_events', 'conversation_outcomes', 'handoff_requests', 'reply_events',
                 'chat_reply_state', 'reply_mode_flags', 'suggestion_sets')   # learning A (2026-10-13): the panel's shown sets


def canon(name, pinned=False):
    """A canonical workflow: the pre-F1 revision in --head mode (or when pinned), else the working tree."""
    if HEAD_MODE or pinned:
        out = subprocess.run(['git', '-C', str(ROOT), 'show', f'{PRE_F1_REV}:Tools/n8n/workflows/{name}'],
                             capture_output=True, text=True, check=True).stdout
        return json.loads(out)
    return wf(name)


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')    # temp tables shadow the UNqualified names only


DBF_PIN = canon(DBF_F, pinned=True)
DELETE_FILE = canon('ZTqpumOpL1rNDOp6-Delete_File.json')
PROXY = canon(PROXY_F)
CREATE = {ch: canon(c['create']) for ch, c in CH.items()}


def m2_block(temp=True):
    """Migration M2's table block (the same text that runs on prod, `public.` dropped), aimed at session temp tables —
    or, with temp=False, at the first schema of the search path (the shared fixture schema of the race cases)."""
    text = (N8N / 'sql' / '2026-10-11-bot-lineage.sql').read_text(encoding='utf-8')
    block = text.split('-- TABLE-BEGIN')[1].split('\n', 1)[1].split('-- TABLE-END')[0].replace('public.', '')
    return block.replace('create table if not exists', 'create temp table if not exists') if temp else block


LIKE = 'create temp table {0} (like public.{0} including all);\n'
TABLES = ('installs', 'subscribers', 'bot_profiles', 'documents', 'uploaded_files') + MEMORY_TABLES
INSTALLS = """
insert into installs (install_id, secret_hash) values ('inst-A', 'h'), ('inst-B', 'h');
insert into installs (install_id, secret_hash, revoked_at) values ('inst-R', 'h', now());
"""
SETUP = ''.join(LIKE.format(t) for t in TABLES) + m2_block() + INSTALLS


def W(ch, tag):
    """A workflow id of the clone form (^[A-Za-z0-9]{8,36}$)."""
    return (ch + tag + '0' * 16)[:16]


def P(ch, tag):
    return f'{ch.lower()}-{tag}'


# ---------------------------------------------------------------- seeding

def row(pg, ch, pid, owner='inst-A', wfid=None, key='Bot0', reason=None, retired=False, sup=None, channel=None):
    gone = 'now()' if (retired or reason) else 'null'
    pg.x(f"""insert into bot_profiles (profile_id, app_user_id, channel, bot_key, owner_id, workflow_id, deleted_at, deleted_reason,
                superseded_by) values ({L(pid)}, {L(U)}, {L(channel or CH[ch]['channel'])}, {L(key)}, {L(owner)}, {L(wfid)},
                {gone}, {L(reason)}, {L(sup)});""")


def proxy_row(pg, ch, pid, inst='inst-A'):
    """The Wappi proxy's Bind Profile: the new profile is the creating install's from birth (no bot key yet)."""
    pg.q(bind(Q(PROXY, 'Bind Profile'), [pid, U, CH[ch]['channel'], inst]))


def register(pg, ch, pid, key='Bot0'):
    """Create's own session path before the seed: Retire Same Bot Slot, then Register Profile (real canon)."""
    w = CREATE[ch]
    pg.x(bind(Q(w, 'Retire Same Bot Slot'), [U, CH[ch]['channel'], key]))
    pg.x(bind(Q(w, 'Register Profile'), [pid, U, CH[ch]['channel'], key]))


def seed_star(pg, pid, version=9):
    """Seed Reply Mode's '*' row of the NEW profile (written before Clone Plan, authoritative)."""
    pg.x(f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ({L(pid)}, '*', false, {version});")


def chunks(pg, ch, wfid, n=2, fid='F1', other='-1'):
    k, o = CH[ch]['key'], CH[ch]['other']
    for i in range(n):
        pg.x(f"insert into documents (content, metadata) values ('r{i} {wfid}', jsonb_build_object('{k}', {L(wfid)}, '{o}', {L(other)}, "
             f"'fileId', {L(fid)}));")


def nchunks(pg, ch, wfid):
    return pg.q(f"select count(*)::int c from documents where metadata->>'{CH[ch]['key']}' = {L(wfid)}")[0]['c']


def file_row(pg, ch, fid, owner, wfid):
    pg.x(f"insert into uploaded_files (file_id, owner_id, {CH[ch]['fcol']}) values ({L(fid)}, {L(owner)}, {L(wfid)});")


def file_wf(pg, ch, fid):
    return pg.q(f"select {CH[ch]['fcol']} as w from uploaded_files where file_id = {L(fid)}")[0]['w']


def chat_data(pg, pid, chat, tag, age=60, mem=2, star=True, suppressed=False, version=1):
    """One chat of a profile in all seven tables Delete Chat Memory clears; `tag` marks every row, `age` (minutes) dates it."""
    t = f"now() - interval '{age} minutes'"
    msg = 'm-' + chat
    for i in range(mem):
        pg.x(f"insert into n8n_chat_histories (session_id, message) values ({L(pid + ':' + chat)}, "
             f"jsonb_build_object('type', 'human', 'content', {L(f'{tag} m{i}')}));")
    pg.x(f"""insert into chat_history_events (profile_id, chat_id, message_id, source, body, sent_at, created_at)
             values ({L(pid)}, {L(chat)}, {L(msg)}, 'client', {L(tag)}, {t}, {t});""")
    pg.x(f"""insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at,
             outcome_at, updated_at) values ({L(pid + ':' + chat)}, {L(pid)}, {L(chat)}, 'in_dialog', {L(tag)}, 1, {t}, {t}, {t});""")
    pg.x(f"""insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary, created_at, updated_at)
             values ({L(pid)}, {L(chat)}, {L(msg)}, 'order', {L(tag)}, {t}, {t});""")
    pg.x(f"""insert into reply_events (profile_id, chat_id, message_id, received_at, updated_at, state, error)
             values ({L(pid)}, {L(chat)}, {L(msg)}, {t}, {t}, 'sent', {L(tag)});""")
    pg.x(f"""insert into chat_reply_state (profile_id, chat_id, incoming_generation, newest_message_time, last_sent_message_id)
             values ({L(pid)}, {L(chat)}, 3, extract(epoch from {t})::bigint, {L(tag)});""")
    pg.x(f"""insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at, version)
             values ({L(pid)}, {L(chat)}, {L(suppressed)}, {t}, {version});""")
    if star:
        pg.x(f"""insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at, version)
                 values ({L(pid)}, '*', false, {t}, {version}) on conflict do nothing;""")


def counts(pg, pid):
    p = L(pid)
    return pg.q(f"""select (select count(*) from n8n_chat_histories where split_part(session_id, ':', 1) = {p})::int as mem,
        (select count(*) from chat_history_events where profile_id = {p})::int as hist,
        (select count(*) from conversation_outcomes where profile_id = {p})::int as outc,
        (select count(*) from handoff_requests where profile_id = {p})::int as hand,
        (select count(*) from reply_events where profile_id = {p})::int as ev,
        (select count(*) from chat_reply_state where profile_id = {p})::int as st,
        (select count(*) from reply_mode_flags where profile_id = {p} and chat_id <> '*')::int as fl,
        (select count(*) from reply_mode_flags where profile_id = {p} and chat_id = '*')::int as star""")[0]


ZERO = {'mem': 0, 'hist': 0, 'outc': 0, 'hand': 0, 'ev': 0, 'st': 0, 'fl': 0, 'star': 0}


def tags(pg, pid, chat):
    """Which source row each keyed table holds for (profile, chat) — the winner of a duplicate."""
    p, c = L(pid), L(chat)
    return pg.q(f"""select (select body from chat_history_events where profile_id = {p} and chat_id = {c}) as hist,
        (select summary from conversation_outcomes where profile_id = {p} and chat_id = {c} and session_id = {p} || ':' || {c}) as outc,
        (select summary from handoff_requests where profile_id = {p} and chat_id = {c}) as hand,
        (select error from reply_events where profile_id = {p} and chat_id = {c}) as ev,
        (select last_sent_message_id from chat_reply_state where profile_id = {p} and chat_id = {c}) as st,
        (select suppressed from reply_mode_flags where profile_id = {p} and chat_id = {c}) as fl""")[0]


def prow(pg, pid):
    r = pg.q(f"""select workflow_id, superseded_by, superseded_at is not null as sup_at, deleted_at is not null as retired, deleted_reason,
                 owner_id from bot_profiles where profile_id = {L(pid)}""")
    return r[0] if r else None


def retirements(pg):
    return pg.q("select workflow_id, profile_id, owner_id, reason, replaced_by from clone_retirements order by workflow_id")


def named_or_booked(pg, inst, wfid):
    """F1 §1.3: a clone the bot had is reachable afterwards — a row of this install still names it (Delete Ownership ok) or
    it is booked for deletion in clone_retirements."""
    v = pg.q(bind(Q(DBF_PIN, 'Delete Ownership'), ['session', inst, '[]', json.dumps([wfid])]))[0]
    booked = pg.q(f"select count(*)::int c from clone_retirements where workflow_id = {L(wfid)}")[0]['c']
    return bool(v['allowed']) or booked == 1


def delete_by_current_ids(pg, ch, inst, pid, wfid):
    """The app's delete by the bot's CURRENT ids through the PRE-F1 Delete Bot Files statements."""
    wa_p, tg_p = (pid, '-1') if ch == 'WA' else ('-1', pid)
    wa_w, tg_w = (wfid, '-1') if ch == 'WA' else ('-1', wfid)
    v = pg.q(bind(Q(DBF_PIN, 'Delete Ownership'), ['session', inst, json.dumps([wa_p, tg_p]), json.dumps([wa_w, tg_w])]))[0]
    if v['allowed']:
        pg.q(bind(Q(DBF_PIN, 'Delete Chat Memory'), [wa_p, tg_p]))
        pg.q(bind(Q(DBF_PIN, 'Delete Bot Chunks'), [wa_w, tg_w]))
    return v


def check_current_ids_reach_everything(c, pg, ch, pid, wfid, old_pids, old_wfs):
    """Plan §1.2 p. 6 / F2 R5: after the move, the PRE-F1 deletion by the bot's CURRENT ids leaves nothing — under the
    current profile and clone, and under every earlier profile and clone of the line."""
    v = delete_by_current_ids(pg, ch, 'inst-A', pid, wfid)
    c.check(v['allowed'] is True, f'Delete Ownership by the current ids: {v}')
    left = {p: counts(pg, p) for p in [pid] + list(old_pids) if counts(pg, p) != ZERO}
    c.check(left == {}, f'left after the delete by the current ids: {left}')
    chunks_left = {w: nchunks(pg, ch, w) for w in [wfid] + list(old_wfs) if nchunks(pg, ch, w)}
    c.check(chunks_left == {}, f'chunks left after the delete by the current ids: {chunks_left}')


# ---------------------------------------------------------------- the two implementations under the same matrix

class Line:
    """Tools/n8n/clone_lineage.py (plan §1.3 / §1.5)."""
    label = 'line'

    def __init__(self):
        self.m = module('clone_lineage.py')

    def plan(self, pg, ch, mode, inst, pid, key='Bot0', origin='edit', prev=''):
        return pg.q(bind(self.m.CLONE_PLAN_SQL, [mode, inst, pid, CH[ch]['channel'], key, origin, prev]))[0]

    def bind(self, pg, ch, pid, wfid, inst, own='', replace=(), preds=(), other='-1'):
        return pg.q(bind(self.m.BIND_WORKFLOW_SQL[CH[ch]['channel']],
                         [pid, wfid, U, CH[ch]['channel'], inst, own or '', json.dumps(list(replace)), json.dumps(list(preds)), other]))[0]


class Head:
    """The pre-F1 canon: no plan (Create always makes a new clone), Restamp RAG Chunks then the A1 Bind Workflow Id."""
    label = 'HEAD'

    def plan(self, pg, ch, mode, inst, pid, key='Bot0', origin='edit', prev=''):
        return {'planned': None, 'own_id': None, 'foreign': None, 'replace_ids': [], 'pred_profiles': []}

    def bind(self, pg, ch, pid, wfid, inst, own='', replace=(), preds=(), other='-1'):
        w = CREATE[ch]
        pg.x(bind(Q(w, 'Restamp RAG Chunks'), [wfid, other]))
        pg.x(bind(Q(w, 'Bind Workflow Id'), [pid, wfid, U, CH[ch]['channel'], inst]))
        return {'bound': None, 'adopted_profiles': None, 'retired_ids': None}


IMPL = Head() if HEAD_MODE else None


def impl():
    global IMPL
    if IMPL is None:
        IMPL = Line()
    return IMPL


def create(pg, ch, pid, new_wf, mode='session', inst='inst-A', key='Bot0', origin='edit', prev='', probe='gone', other='-1'):
    """Plan §1.4: Clone Plan → foreign? refuse → own usable? reuse (Reused Clone Id) : create → Bind with the plan's lists."""
    im = impl()
    plan = im.plan(pg, ch, mode, inst, pid, key, origin, prev)
    if plan.get('foreign') is True:
        return plan, None, None
    own = plan.get('own_id')
    if own and probe == 'usable':
        return plan, im.bind(pg, ch, pid, own, inst, '', plan.get('replace_ids') or [], plan.get('pred_profiles') or [], other), False
    return plan, im.bind(pg, ch, pid, new_wf, inst, own or '', plan.get('replace_ids') or [], plan.get('pred_profiles') or [], other), True


def tagged(title):
    return f'{title} [{impl().label}]'


# ---------------------------------------------------------------- the requirement matrix (both implementations)

def reconnect_after_purchase(ch):
    """F2 R1 / F1 (а3): the sweep retired the bot (trial over), the owner bought a plan and paired a NEW profile."""
    P1, P2, WF1, WF2, WFN = P(ch, 'P1'), P(ch, 'P2'), W(ch, 'old'), W(ch, 'new'), W(ch, 'nb')
    with Case(tagged(f'L1 [{ch}] reconnect after a purchase (F2 R1): the new clone takes the bot\'s memory, history, outcomes, '
                     'handoffs, ledger, state, chat flags, price lists and file registry; the prior row is superseded, the prior '
                     'clone booked')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1, reason='trial_expiry')
            chunks(pg, ch, WF1, 2, 'F1')
            chunks(pg, ch, WFN, 1, 'FN')                         # a neighbour bot's chunk
            file_row(pg, ch, 'F1', 'inst-A', WF1)
            file_row(pg, ch, 'F2', None, WF1)                    # an owner-less compat registration of the same clone
            file_row(pg, ch, 'F3', 'inst-B', WF1)                # another install's row naming it: never moved
            chat_data(pg, P1, '7701@c.us', 'old')
            mem_ids = [r['id'] for r in pg.q(f"select id from n8n_chat_histories where session_id like {L(P1 + ':%')} order by id")]
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            seed_star(pg, P2, 9)
            plan, b, created = create(pg, ch, P2, WF2, prev=P1)
            c.check(plan.get('planned') is True and plan.get('own_id') is None and plan.get('foreign') is False, f'plan {plan}')
            c.check(plan.get('pred_profiles') == [P1] and plan.get('replace_ids') == [WF1], f'plan {plan}')
            c.check(b.get('bound') is True, f'bind has no positive marker: {b}')
            c.check(b.get('adopted_profiles') == [P1] and b.get('retired_ids') == [WF1], f'bind {b}')
            c.check((nchunks(pg, ch, WF1), nchunks(pg, ch, WF2), nchunks(pg, ch, WFN)) == (0, 2, 1),
                    f'chunks old/new/neighbour = {(nchunks(pg, ch, WF1), nchunks(pg, ch, WF2), nchunks(pg, ch, WFN))}')
            c.check([file_wf(pg, ch, f) for f in ('F1', 'F2', 'F3')] == [WF2, WF2, WF1],
                    f'file registry {[file_wf(pg, ch, f) for f in ("F1", "F2", "F3")]}')
            if not HEAD_MODE:
                # V4: the owner-less compat file is still the owner's to delete — its row and its chunks name ONE clone, the
                # one the token lists now (Verify Install's w: the install's live bindings)
                w = pg.q("""select coalesce(jsonb_agg(distinct workflow_id), '[]'::jsonb) as w from bot_profiles
                            where owner_id = 'inst-A' and deleted_at is null and workflow_id is not null""")[0]['w']
                v = pg.q(bind(Q(DELETE_FILE, 'Delete File Chunks'), ['F2', 'session', 'inst-A', json.dumps(w)]))[0]
                c.check(w == [WF2] and v['allowed'] is True, f'the owner cannot delete his compat file after the move: {w} {v}')
            p1 = prow(pg, P1)
            c.check(p1 == {'workflow_id': WF1, 'superseded_by': P2, 'sup_at': True, 'retired': True, 'deleted_reason': 'trial_expiry',
                           'owner_id': 'inst-A'}, f'prior row not marked: {p1}')
            c.check(retirements(pg) == [{'workflow_id': WF1, 'profile_id': P1, 'owner_id': 'inst-A', 'reason': 'replaced',
                                         'replaced_by': WF2}], f'prior clone not booked: {retirements(pg)}')
            c.check(named_or_booked(pg, 'inst-A', WF1), 'prior clone unreachable (no row names it, not booked)')
            c.check(counts(pg, P1) == ZERO, f'left under the old profile: {counts(pg, P1)}')
            c.check(counts(pg, P2) == {'mem': 2, 'hist': 1, 'outc': 1, 'hand': 1, 'ev': 1, 'st': 1, 'fl': 1, 'star': 1},
                    f'new profile: {counts(pg, P2)}')
            star = pg.q(f"select version from reply_mode_flags where profile_id = {L(P2)} and chat_id = '*'")
            c.check(star == [{'version': 9}], f"the new '*' (seeded from the client's mode) is not authoritative: {star}")
            moved_ids = [r['id'] for r in pg.q(f"select id from n8n_chat_histories where session_id like {L(P2 + ':%')} order by id")]
            c.check(moved_ids == mem_ids, f'memory row ids not preserved: {mem_ids} -> {moved_ids}')
            # «the bot = its current ids»: the pre-F1 deletion by the CURRENT ids now reaches everything
            v = delete_by_current_ids(pg, ch, 'inst-A', P2, WF2)
            c.check(v['allowed'] is True, f'Delete Ownership {v}')
            c.check(counts(pg, P1) == ZERO and {k: n for k, n in counts(pg, P2).items() if k != 'star'} == {k: 0 for k in ZERO if k != 'star'},
                    f'after the delete: old {counts(pg, P1)} new {counts(pg, P2)}')
            c.check((nchunks(pg, ch, WF1), nchunks(pg, ch, WF2), nchunks(pg, ch, WFN)) == (0, 0, 1), 'chunks after the delete')


def same_profile_clone_gone(ch):
    P1, WF1, WF2 = P(ch, 'P1'), W(ch, 'old'), W(ch, 'new')
    with Case(tagged(f'L2 [{ch}] the same profile, its bound clone gone (F1 а1: 404 / archived): the new clone takes the old '
                     'binding\'s price lists, the old clone is booked, nothing is superseded or moved between profiles')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1)
            chunks(pg, ch, WF1, 2, 'F1')
            file_row(pg, ch, 'F1', 'inst-A', WF1)
            chat_data(pg, P1, '7701@c.us', 'own')
            plan, b, created = create(pg, ch, P1, WF2, probe='gone')
            c.check(plan.get('own_id') == WF1 and plan.get('replace_ids') == [] and plan.get('pred_profiles') == [], f'plan {plan}')
            c.check(b.get('bound') is True and b.get('retired_ids') == [WF1] and b.get('adopted_profiles') == [], f'bind {b}')
            c.check((nchunks(pg, ch, WF1), nchunks(pg, ch, WF2)) == (0, 2) and file_wf(pg, ch, 'F1') == WF2,
                    f'price lists stayed under the gone clone: {(nchunks(pg, ch, WF1), nchunks(pg, ch, WF2))}')
            p1 = prow(pg, P1)
            c.check(p1['workflow_id'] == WF2 and p1['superseded_by'] is None and not p1['retired'], f'row {p1}')
            c.check(retirements(pg) == [{'workflow_id': WF1, 'profile_id': P1, 'owner_id': 'inst-A', 'reason': 'replaced',
                                         'replaced_by': WF2}], f'retirements {retirements(pg)}')
            c.check(named_or_booked(pg, 'inst-A', WF1), 'the old clone is reachable by nobody')
            c.check(counts(pg, P1)['mem'] == 2 and counts(pg, P1)['hist'] == 1, f'own data {counts(pg, P1)}')


def same_profile_reuse(ch):
    P1, WF1, WF2 = P(ch, 'P1'), W(ch, 'old'), W(ch, 'new')
    with Case(tagged(f'L3 [{ch}] the same profile, its bound clone usable (F1 а1/а2): the plan names it, the Create reuses it — '
                     'no new clone, nothing moved or booked')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1)
            chunks(pg, ch, WF1, 2, 'F1')
            plan, b, created = create(pg, ch, P1, WF2, probe='usable')
            c.check(created is False, 'a new clone was created for a profile whose bound clone is usable')
            c.check(b.get('bound') is True and b.get('retired_ids') == [], f'bind {b}')
            c.check(prow(pg, P1)['workflow_id'] == WF1 and nchunks(pg, ch, WF1) == 2 and retirements(pg) == [],
                    f'row {prow(pg, P1)} chunks {nchunks(pg, ch, WF1)} retirements {retirements(pg)}')


def retire_same_bot_slot(ch):
    P1, P2, WF1, WF2 = P(ch, 'P1'), P(ch, 'P2'), W(ch, 'old'), W(ch, 'new')
    with Case(tagged(f'L4 [{ch}] a new profile after Retire Same Bot Slot (F1 а3, an older client: no CreateOrigin, no '
                     'PreviousProfileId): the bot key proves the predecessor; its NULL reason becomes replaced')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1)                            # live, bound
            chunks(pg, ch, WF1, 2, 'F1')
            chat_data(pg, P1, '7701@c.us', 'old')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)                                 # retires P1 with deleted_reason NULL
            plan, b, created = create(pg, ch, P2, WF2, origin='', prev='')
            c.check(plan.get('pred_profiles') == [P1] and plan.get('replace_ids') == [WF1], f'plan {plan}')
            p1 = prow(pg, P1)
            c.check(b.get('bound') is True and p1['superseded_by'] == P2 and p1['deleted_reason'] == 'replaced', f'bind {b} row {p1}')
            c.check((nchunks(pg, ch, WF1), nchunks(pg, ch, WF2)) == (0, 2), 'chunks')
            c.check(counts(pg, P1) == ZERO and counts(pg, P2)['mem'] == 2, f'old {counts(pg, P1)} new {counts(pg, P2)}')
            c.check(named_or_booked(pg, 'inst-A', WF1), 'the replaced clone is reachable by nobody')
            check_current_ids_reach_everything(c, pg, ch, P2, WF2, [P1], [WF1])


def wizard_never_adopts(ch):
    P1, P2, WF1, WF2 = P(ch, 'P1'), P(ch, 'P2'), W(ch, 'old'), W(ch, 'new')
    with Case(tagged(f'L5 [{ch}] a wizard Create (CreateOrigin=start, a NEW bot in a reused slot) never looks for a predecessor, '
                     'even with the same bot key and a named previous profile')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1, reason='trial_expiry')
            chunks(pg, ch, WF1, 2, 'F1')
            chat_data(pg, P1, '7701@c.us', 'old')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            plan, b, created = create(pg, ch, P2, WF2, origin='start', prev=P1)
            c.check(plan.get('pred_profiles') == [] and plan.get('replace_ids') == [], f'plan {plan}')
            c.check(b.get('bound') in (True, None) and prow(pg, P1)['superseded_by'] is None and nchunks(pg, ch, WF1) == 2
                    and counts(pg, P1)['mem'] == 2 and retirements(pg) == [], f'the old bot was touched: {prow(pg, P1)}')


def previous_profile_without_key(ch):
    P1, P2, WF1, WF2 = P(ch, 'P1'), P(ch, 'P2'), W(ch, 'old'), W(ch, 'new')
    with Case(tagged(f'L6 [{ch}] PreviousProfileId proves a predecessor that has no bot key (a row from before bot_key / the '
                     'proxy\'s row); a DIFFERENT bot key refuses it')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1, key=None, reason='liveness')
            chunks(pg, ch, WF1, 2, 'F1')
            chat_data(pg, P1, '7701@c.us', 'old')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            plan, b, created = create(pg, ch, P2, WF2, prev=P1)
            c.check(plan.get('pred_profiles') == [P1] and plan.get('replace_ids') == [WF1], f'plan {plan}')
            c.check(b.get('bound') is True and prow(pg, P1)['superseded_by'] == P2 and counts(pg, P2)['mem'] == 2
                    and nchunks(pg, ch, WF2) == 2, f'bind {b}')
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1, key='Bot7', reason='liveness')
            chat_data(pg, P1, '7701@c.us', 'other bot')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            plan, b, created = create(pg, ch, P2, WF2, prev=P1)
            c.check(plan.get('pred_profiles') == [] and prow(pg, P1)['superseded_by'] is None and counts(pg, P1)['mem'] == 2,
                    f'another bot\'s profile adopted: {plan}')


def refusals(ch):
    """F2 R3 + plan §1.3 — in the plan AND in the bind when the refused profiles are handed to it anyway."""
    other_ch = 'TG' if ch == 'WA' else 'WA'
    P2, WF2 = P(ch, 'P2'), W(ch, 'new')
    bad = {  # tag: (row kwargs) — each would be a predecessor but for one condition
        'xi': dict(owner='inst-B', wfid=W(ch, 'xi'), reason='trial_expiry'),                    # another install, same key
        'ol': dict(owner=None, wfid=W(ch, 'ol'), reason='trial_expiry'),                        # owner-less (legacy) row
        'ad': dict(wfid=W(ch, 'ad'), reason='app_delete'),                                      # a deleted bot
        'oc': dict(wfid=W(ch, 'oc'), reason='trial_expiry', channel=CH[other_ch]['channel']),   # another channel
        'ok': dict(wfid=W(ch, 'ok'), key='Bot7', reason='trial_expiry'),                       # another bot key
        'su': dict(wfid=W(ch, 'su'), reason='trial_expiry', sup=P(ch, 'P3')),                   # another profile took it over
    }
    with Case(tagged(f'L7 [{ch}] refusals of the predecessor rule — plan and bind: another install, an owner-less row, app_delete, '
                     'another channel, another bot key, a row another profile took over, the new profile itself, a revoked '
                     'install, a new profile someone else owns')) as c:
        with PgSession(SETUP) as pg:
            for tag, kw in bad.items():
                row(pg, ch, P(ch, tag), **kw)
                chat_data(pg, P(ch, tag), '7701@c.us', tag)
                chunks(pg, ch, kw['wfid'], 1, 'F' + tag)
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            for tag in bad:
                plan = impl().plan(pg, ch, 'session', 'inst-A', P2, 'Bot0', 'edit', P(ch, tag))
                c.check(plan.get('pred_profiles') == [] and plan.get('replace_ids') == [], f'{tag}: plan {plan}')
            plan = impl().plan(pg, ch, 'session', 'inst-A', P2, 'Bot0', 'edit', P2)                      # names itself
            c.check(plan.get('pred_profiles') == [], f'self: {plan}')
            row(pg, ch, P(ch, 'kp'), wfid=W(ch, 'kp'), reason='trial_expiry')       # a qualifying row of the same bot …
            plan = impl().plan(pg, ch, 'session', 'inst-A', P2, '', 'edit', '')    # … but no bot key and no named profile
            c.check(plan.get('pred_profiles') == [] and plan.get('replace_ids') == [], f'empty bot key, nothing named: {plan}')
            pg.x(f"update bot_profiles set deleted_at = now(), deleted_reason = 'channel_limit' where profile_id = {L(P2)};")
            plan = impl().plan(pg, ch, 'session', 'inst-A', P2, 'Bot0', 'edit', P(ch, 'kp'))   # F2 R3: the new profile is retired
            c.check(plan.get('pred_profiles') == [] and plan.get('replace_ids') == [], f'retired new profile: {plan}')
            pg.x(f"update bot_profiles set deleted_at = null, deleted_reason = null where profile_id = {L(P2)};")
            pg.x(f"delete from bot_profiles where profile_id = {L(P(ch, 'kp'))};")
            b = impl().bind(pg, ch, P2, WF2, 'inst-A', '', [kw['wfid'] for kw in bad.values()], [P(ch, t) for t in bad])
            for tag, kw in bad.items():
                r = prow(pg, P(ch, tag))
                c.check(r['superseded_by'] == kw.get('sup') and counts(pg, P(ch, tag))['mem'] == 2 and nchunks(pg, ch, kw['wfid']) == 1,
                        f'{tag}: the bind touched a refused row: {r} {counts(pg, P(ch, tag))}')
            c.check(retirements(pg) == [], f'refused clones booked: {retirements(pg)}')
        with PgSession(SETUP) as pg:                       # a revoked install: nothing to adopt, its own profile is foreign
            row(pg, ch, P(ch, 'R1'), owner='inst-R', wfid=W(ch, 'r1'), reason='trial_expiry')
            chat_data(pg, P(ch, 'R1'), '7701@c.us', 'r1')
            row(pg, ch, P(ch, 'R2'), owner='inst-R', key='Bot0')
            plan = impl().plan(pg, ch, 'session', 'inst-R', P(ch, 'R2'), 'Bot0', 'edit', P(ch, 'R1'))
            c.check(plan.get('pred_profiles') == [] and plan.get('foreign') is True, f'revoked: {plan}')
            impl().bind(pg, ch, P(ch, 'R2'), W(ch, 'r2'), 'inst-R', '', [W(ch, 'r1')], [P(ch, 'R1')])
            c.check(counts(pg, P(ch, 'R1'))['mem'] == 2 and prow(pg, P(ch, 'R1'))['superseded_by'] is None,
                    'a revoked install adopted a predecessor')
        with PgSession(SETUP) as pg:                       # the new profile belongs to another install (stale token, M5)
            row(pg, ch, P(ch, 'P1'), wfid=W(ch, 'old'), reason='trial_expiry')
            chat_data(pg, P(ch, 'P1'), '7701@c.us', 'p1')
            row(pg, ch, P2, owner='inst-B', key='Bot0')
            plan = impl().plan(pg, ch, 'session', 'inst-A', P2, 'Bot0', 'edit', P(ch, 'P1'))
            c.check(plan.get('pred_profiles') == [] and plan.get('foreign') is True, f'foreign new profile: {plan}')
            b = impl().bind(pg, ch, P2, WF2, 'inst-A', '', [W(ch, 'old')], [P(ch, 'P1')])
            c.check(b.get('bound') is False and prow(pg, P2)['workflow_id'] is None and counts(pg, P(ch, 'P1'))['mem'] == 2,
                    f'bound another install\'s profile: {b} {prow(pg, P2)}')


def two_predecessors(ch):
    P0, P1, P2 = P(ch, 'P0'), P(ch, 'P1'), P(ch, 'P2')
    WF0, WF1, WF2 = W(ch, 'p0'), W(ch, 'p1'), W(ch, 'new')
    A, B = '7701@c.us', '7702@c.us'
    for new_has_row in (False, True):
        title = ('L8 [{ch}] two predecessors with the same chat: one row per key under the new profile — the FRESHEST — no unique '
                 'violation; both memories kept in order' if not new_has_row else
                 'L9 [{ch}] the new profile\'s own row wins over both predecessors (new wins); their duplicates are dropped')
        with Case(tagged(title.format(ch=ch))) as c:
            with PgSession(SETUP) as pg:
                row(pg, ch, P0, wfid=WF0, retired=True)                     # Retire Same Bot Slot long ago (NULL reason)
                row(pg, ch, P1, wfid=WF1, reason='trial_expiry')
                chat_data(pg, P0, A, 'p0', age=300, suppressed=False, version=1)
                chat_data(pg, P0, B, 'p0b', age=280)
                chat_data(pg, P1, A, 'p1', age=100, suppressed=True, version=2)
                chunks(pg, ch, WF0, 1, 'F0')
                chunks(pg, ch, WF1, 2, 'F1')
                proxy_row(pg, ch, P2)
                register(pg, ch, P2)
                if new_has_row:
                    chat_data(pg, P2, A, 'new', age=1, suppressed=False, version=3)
                plan, b, created = create(pg, ch, P2, WF2, prev=P1)
                c.check(plan.get('pred_profiles') == [P0, P1] and plan.get('replace_ids') == sorted([WF0, WF1]), f'plan {plan}')
                c.check(b.get('bound') is True, f'bind {b}')
                want = ({'hist': 'new', 'outc': 'new', 'hand': 'new', 'ev': 'new', 'st': 'new', 'fl': False} if new_has_row else
                        {'hist': 'p1', 'outc': 'p1', 'hand': 'p1', 'ev': 'p1', 'st': 'p1', 'fl': True})
                c.check(tags(pg, P2, A) == want, f'winner {tags(pg, P2, A)} != {want}')
                c.check(tags(pg, P2, B)['hist'] == 'p0b', f'a chat only P0 had: {tags(pg, P2, B)}')
                c.check(counts(pg, P0) == ZERO and counts(pg, P1) == ZERO, f'left: {counts(pg, P0)} {counts(pg, P1)}')
                n = counts(pg, P2)
                c.check(n == {'mem': 6 + (2 if new_has_row else 0), 'hist': 2, 'outc': 2, 'hand': 2, 'ev': 2, 'st': 2, 'fl': 2,
                              'star': 1 if new_has_row else 0}, f'new profile {n}')
                mem = [r['c'] for r in pg.q(f"""select message->>'content' as c from n8n_chat_histories
                                               where session_id = {L(P2 + ':' + A)} order by id""")]
                want_mem = ['p0 m0', 'p0 m1', 'p1 m0', 'p1 m1'] + (['new m0', 'new m1'] if new_has_row else [])
                c.check(mem == want_mem, f'memory order {mem}')
                c.check((nchunks(pg, ch, WF0), nchunks(pg, ch, WF1), nchunks(pg, ch, WF2)) == (0, 0, 3), 'chunks')
                c.check([r['workflow_id'] for r in retirements(pg)] == sorted([WF0, WF1]), f'booked {retirements(pg)}')
                check_current_ids_reach_everything(c, pg, ch, P2, WF2, [P0, P1], [WF0, WF1])


def idempotent_retry(ch):
    P1, P2, WF1, WF2 = P(ch, 'P1'), P(ch, 'P2'), W(ch, 'old'), W(ch, 'new')
    with Case(tagged(f'L10 [{ch}] a retry after retryOnFail (or a repeated Create whose answer was lost) is idempotent: same answer, '
                     'nothing moved or booked twice')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1, reason='churn_grace')
            chunks(pg, ch, WF1, 2, 'F1')
            chat_data(pg, P1, '7701@c.us', 'old')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            plan, first, _ = create(pg, ch, P2, WF2, prev=P1)
            before = (counts(pg, P2), nchunks(pg, ch, WF2), retirements(pg), prow(pg, P1))
            again = impl().bind(pg, ch, P2, WF2, 'inst-A', '', plan.get('replace_ids') or [], plan.get('pred_profiles') or [])
            after = (counts(pg, P2), nchunks(pg, ch, WF2), retirements(pg), prow(pg, P1))
            c.check(again.get('bound') is True and again.get('adopted_profiles') == [P1] and again.get('retired_ids') == [WF1]
                    and again.get('booked') == 0 and again.get('restamped_chunks') == 0 and again.get('moved_memory') == 0,
                    f'retry {again}')
            c.check(before == after, f'state changed on a retry: {before} -> {after}')
            # the plan of a repeated Create (same pair, the first answer lost): the same predecessor, the clone now its own
            plan2 = impl().plan(pg, ch, 'session', 'inst-A', P2, 'Bot0', 'edit', P1)
            c.check(plan2.get('own_id') == WF2 and plan2.get('pred_profiles') == [P1], f'repeat plan {plan2}')


def legacy(ch):
    PL, PO, PQ = P(ch, 'PL'), P(ch, 'PO'), P(ch, 'PQ')
    WL, WO, WQ, WN = W(ch, 'lg'), W(ch, 'ow'), W(ch, 'lq'), W(ch, 'ln')
    with Case(tagged(f'L11 [{ch}] legacy (compat window): reuse only an owner-less binding, never replace or adopt; a profile an '
                     'install owns is foreign — and the bind refuses it too (Q3)')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, PL, owner=None, wfid=WL)
            row(pg, ch, PO, owner='inst-A', wfid=WO)
            row(pg, ch, PQ, owner=None, wfid=WQ, retired=True)
            chunks(pg, ch, WL, 2, 'FL')
            chunks(pg, ch, WQ, 1, 'FQ')
            chat_data(pg, PQ, '7701@c.us', 'q')
            pl = impl().plan(pg, ch, 'legacy', '', PL)
            c.check(pl.get('own_id') == WL and pl.get('foreign') is False and pl.get('pred_profiles') == [] and pl.get('replace_ids') == [],
                    f'legacy own {pl}')
            po = impl().plan(pg, ch, 'legacy', '', PO)
            c.check(po.get('foreign') is True and po.get('own_id') is None, f'legacy on an owned profile {po}')
            b = impl().bind(pg, ch, PL, WN, '', WL, [WQ], [PQ])          # own 404 + ids handed by a broken caller
            c.check(b.get('bound') is True and b.get('adopted_profiles') == [] and b.get('superseded_rows') == 0, f'legacy bind {b}')
            c.check((nchunks(pg, ch, WL), nchunks(pg, ch, WN), nchunks(pg, ch, WQ)) == (0, 2, 1), 'legacy chunks')
            c.check(prow(pg, PQ)['superseded_by'] is None and counts(pg, PQ)['mem'] == 2, 'legacy adopted a row')
            c.check(retirements(pg) == [{'workflow_id': WL, 'profile_id': PL, 'owner_id': None, 'reason': 'replaced', 'replaced_by': WN}],
                    f'legacy booked {retirements(pg)}')
            bo = impl().bind(pg, ch, PO, W(ch, 'hj'), '', '', [], [])
            c.check(bo.get('bound') is False and prow(pg, PO)['workflow_id'] == WO, f'legacy bound an install\'s profile: {bo} {prow(pg, PO)}')


def sentinel(ch):
    with Case(tagged(f'L12 [{ch}] a sentinel profile (-1 / empty) binds nothing and SAYS so (bound = false)')) as c:
        with PgSession(SETUP) as pg:
            for pid in ('-1', ''):
                b = impl().bind(pg, ch, pid, W(ch, 'new'), 'inst-A')
                c.check(b.get('bound') is False, f'{pid!r}: {b}')
            c.check(pg.q('select count(*)::int c from bot_profiles')[0]['c'] == 0, 'a sentinel row was written')


def minus_one_rule(ch):
    P2, WF2, OTHER = P(ch, 'P2'), W(ch, 'new'), W('TG' if ch == 'WA' else 'WA', 'oth')
    with Case(tagged(f'L13 [{ch}] the pre-F1 rule: a second channel joining a bot of the other channel takes the chunks that carry '
                     'the -1 sentinel for this channel')) as c:
        with PgSession(SETUP) as pg:
            k, o = CH[ch]['key'], CH[ch]['other']
            pg.x(f"insert into documents (content, metadata) values ('t', jsonb_build_object('{k}', '-1', '{o}', {L(OTHER)}));")
            pg.x(f"insert into documents (content, metadata) values ('u', jsonb_build_object('{k}', '-1', '{o}', 'SomeoneElse00001'));")
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            create(pg, ch, P2, WF2, other=OTHER)
            c.check(nchunks(pg, ch, WF2) == 1 and nchunks(pg, ch, '-1') == 1, f'-1 rule: {nchunks(pg, ch, WF2)} / {nchunks(pg, ch, "-1")}')


def protected(ch):
    m = None if HEAD_MODE else impl().m
    prot = m.PROTECTED if m else ['4wYitz5ek30SVNlT', '4VN3gsFaC2HUYmcc']
    T1, T2 = prot[0], prot[1]
    with Case(tagged(f'L14 [{ch}] protected ids are never reused, replaced, moved, booked or bound')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P(ch, 'PX'), wfid=T1)                         # tampered: the profile names an infra workflow
            row(pg, ch, P(ch, 'PY'), wfid=T2, reason='trial_expiry')  # a predecessor bound to one
            chunks(pg, ch, T2, 1, 'FT')
            proxy_row(pg, ch, P(ch, 'P2'))
            register(pg, ch, P(ch, 'P2'))
            px = impl().plan(pg, ch, 'session', 'inst-A', P(ch, 'PX'))
            c.check(px.get('own_id') is None, f'protected own {px}')
            p2 = impl().plan(pg, ch, 'session', 'inst-A', P(ch, 'P2'), 'Bot0', 'edit', P(ch, 'PY'))
            c.check(not ({T1, T2} & set(p2.get('replace_ids') or [])) and not ({P(ch, 'PX'), P(ch, 'PY')} & set(p2.get('pred_profiles') or [])),
                    f'protected pred {p2}')
            b = impl().bind(pg, ch, P(ch, 'P2'), T1, 'inst-A')
            c.check(b.get('bound') is False and prow(pg, P(ch, 'P2'))['workflow_id'] is None, f'bound a protected id: {b}')
            b = impl().bind(pg, ch, P(ch, 'P2'), W(ch, 'new'), 'inst-A', T1, [T2], [P(ch, 'PY')])
            c.check(nchunks(pg, ch, T2) == 1 and retirements(pg) == [] and prow(pg, P(ch, 'PY'))['superseded_by'] is None,
                    f'a protected clone was moved or booked: {retirements(pg)}')


def shared_clone(ch):
    P1, PO, P2, WS = P(ch, 'P1'), P(ch, 'PO'), P(ch, 'P2'), W(ch, 'sh')
    with Case(tagged(f'L15 [{ch}] a clone another bot\'s LIVE row drives is never replaced (plan) nor moved or booked (bind)')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WS, reason='trial_expiry')
            row(pg, ch, PO, wfid=WS, key='Bot7')                      # another bot of the same install, live
            chunks(pg, ch, WS, 2, 'FS')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            plan = impl().plan(pg, ch, 'session', 'inst-A', P2, 'Bot0', 'edit', P1)
            c.check(plan.get('replace_ids') == [] and plan.get('pred_profiles') == [], f'plan {plan}')
            impl().bind(pg, ch, P2, W(ch, 'new'), 'inst-A', '', [WS], [P1])
            c.check(nchunks(pg, ch, WS) == 2 and retirements(pg) == [] and prow(pg, PO)['superseded_by'] is None
                    and not prow(pg, PO)['retired'], f'the other bot lost its clone: {retirements(pg)} {prow(pg, PO)}')
            c.check(prow(pg, P1)['superseded_by'] is None, f'the bind adopted a row whose clone another bot drives: {prow(pg, P1)}')


def concurrent_same_profile(ch):
    P1, WFX, WF2 = P(ch, 'P1'), W(ch, 'cx'), W(ch, 'new')
    with Case(tagged(f'L16 [{ch}] a concurrent Create of the same profile bound another clone between our plan and our bind: the '
                     'binding the upsert overwrites is booked and its price lists move (no unbound clone)')) as c:
        with PgSession(SETUP) as pg:
            proxy_row(pg, ch, P1)
            register(pg, ch, P1)
            plan = impl().plan(pg, ch, 'session', 'inst-A', P1)        # nothing bound yet: own none
            pg.x(f"update bot_profiles set workflow_id = {L(WFX)} where profile_id = {L(P1)};")   # the other run's bind
            chunks(pg, ch, WFX, 2, 'FX')
            b = impl().bind(pg, ch, P1, WF2, 'inst-A', plan.get('own_id') or '', plan.get('replace_ids') or [], plan.get('pred_profiles') or [])
            c.check(b.get('bound') is True and named_or_booked(pg, 'inst-A', WFX), f'the overwritten clone is lost: {b} {retirements(pg)}')
            c.check((nchunks(pg, ch, WFX), nchunks(pg, ch, WF2)) == (0, 2), 'its price lists stayed behind')


def bind_ownership(ch):
    PB, WFB = P(ch, 'PB'), W(ch, 'b1')
    with Case(tagged(f'L17 [{ch}] Bind re-checks the row it writes: another install\'s profile (a stale token, M5) and a profile of '
                     'the other channel are refused (bound = false), never rebound')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, PB, owner='inst-B', wfid=WFB)
            b = impl().bind(pg, ch, PB, W(ch, 'hj'), 'inst-A')
            c.check(b.get('bound') is False and prow(pg, PB)['workflow_id'] == WFB, f'hijacked: {b} {prow(pg, PB)}')
            other = 'TG' if ch == 'WA' else 'WA'
            row(pg, other, P(ch, 'PC'), owner='inst-A', wfid=W(ch, 'c1'))
            b = impl().bind(pg, ch, P(ch, 'PC'), W(ch, 'c2'), 'inst-A')
            c.check(b.get('bound') is False and prow(pg, P(ch, 'PC'))['workflow_id'] == W(ch, 'c1'), f'cross-channel bind: {b}')


def rebind_clears_mark(ch):
    P1, P2 = P(ch, 'P1'), P(ch, 'P2')
    with Case(tagged(f'L18 [{ch}] a superseded row bound again loses the mark (F1); the original reason is kept for a liveness row')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=W(ch, 'old'), reason='liveness')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            create(pg, ch, P2, W(ch, 'new'), prev=P1)
            r = prow(pg, P1)
            c.check(r['superseded_by'] == P2 and r['deleted_reason'] == 'liveness', f'superseded {r}')
            impl().bind(pg, ch, P1, W(ch, 'thr'), 'inst-A')
            r = prow(pg, P1)
            c.check(r['superseded_by'] is None and not r['sup_at'] and not r['retired'] and r['workflow_id'] == W(ch, 'thr'), f'rebound {r}')


def bound_bookings(pg):
    """Open bookings of a clone a LIVE row drives — a clone the system both drives and plans to delete."""
    return [r['workflow_id'] for r in pg.q("""select r.workflow_id from clone_retirements r where r.removed_at is null
        and exists (select 1 from bot_profiles live where live.workflow_id = r.workflow_id and live.deleted_at is null)""")]


def booked_clone_never_reused(ch):
    P1, P2, WF1, WF2, WF3 = P(ch, 'P1'), P(ch, 'P2'), W(ch, 'old'), W(ch, 'new'), W(ch, 'thr')
    with Case(tagged(f'L19 [{ch}] a clone booked for deletion is never offered for reuse: the superseded profile comes back (a '
                     'stale retry of its Create) and gets a NEW clone; no live row ever drives a booked clone')) as c:
        with PgSession(SETUP) as pg:
            row(pg, ch, P1, wfid=WF1, reason='trial_expiry')
            chunks(pg, ch, WF1, 2, 'F1')
            proxy_row(pg, ch, P2)
            register(pg, ch, P2)
            create(pg, ch, P2, WF2, prev=P1)                                  # WF1 booked, P1 superseded by P2
            c.check([r['workflow_id'] for r in retirements(pg)] == [WF1], f'setup: {retirements(pg)}')
            register(pg, ch, P1)                                              # P1 is registered again
            plan, b, created = create(pg, ch, P1, WF3, probe='usable')        # WF1 still answers 200 (not yet swept)
            c.check(plan.get('own_id') is None and created is True, f'a booked clone was offered for reuse: {plan}')
            c.check(prow(pg, P1)['workflow_id'] == WF3, f'row {prow(pg, P1)}')
            c.check(bound_bookings(pg) == [], f'a live row drives a booked clone: {bound_bookings(pg)}')
            c.check(named_or_booked(pg, 'inst-A', WF1), 'the booked clone left the books')


# ---------------------------------------------------------------- races: two real connections on a shared fixture schema
# Session temp tables are invisible to another connection, so the race cases build the tables in a uniquely named
# fixture schema (own id sequences, dropped afterwards) — the V6-concurrent pattern (test_reply_memory_concurrent.py).
# The first bind runs inside an open transaction that holds its row locks while the second bind starts: the second
# statement's snapshot predates the first's commit, which is exactly the window the plan's «re-check every condition on
# the rows you write» (§1.5, the V6-concurrent lesson) is about.

SEQ_TABLES = ('documents', 'n8n_chat_histories', 'chat_history_events', 'handoff_requests')


class Shared:
    def __enter__(self):
        self.schema = 't_t2c_' + uuid.uuid4().hex[:16]
        self.main, self.first, self.second = PgSession(), PgSession(), PgSession()
        s = self.schema
        self.main.x(f'create schema {s}; set search_path = {s};')
        for t in TABLES:
            self.main.x(f'create table {t} (like public.{t} including all);')
        for t in SEQ_TABLES:                                   # LIKE copies the PUBLIC sequence as the default
            self.main.x(f"create sequence {t}_fixture_id; alter table {t} alter column id set default nextval('{t}_fixture_id');")
        self.main.x(m2_block(temp=False) + INSTALLS)
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


def race(fx, first_bind, second_bind):
    """`first_bind` runs inside an open transaction on the first connection; meanwhile `second_bind` runs on the
    second. Returns (first result, second result, whether the second waited on a lock the first held)."""
    fx.first.x('begin;')
    a = first_bind(fx.first)
    pid = fx.second.q('select pg_backend_pid() as pid')[0]['pid']
    waited = False
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(second_bind, fx.second)
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
    return a, b, waited


def race_same_profile(ch):
    P2, WF1, WA, WB = P(ch, 'P2'), W(ch, 'old'), W(ch, 'ca'), W(ch, 'cb')
    with Case(tagged(f'C1 [{ch}] two Creates of the SAME profile race (a client retry while the first still runs; both probes '
                     'said the bound clone is gone): exactly one bind wins and SAYS so, the loser answers bound = false (the '
                     'graph discards its clone) — no clone is lost and the price lists follow the bound one')) as c:
        with Shared() as fx:
            pg = fx.main
            row(pg, ch, P2, wfid=WF1)
            chunks(pg, ch, WF1, 2, 'F1')
            plan = impl().plan(pg, ch, 'session', 'inst-A', P2)            # both runs read the same plan
            own = plan.get('own_id') or ''
            a, b, waited = race(fx, lambda conn: impl().bind(conn, ch, P2, WA, 'inst-A', own),
                                lambda conn: impl().bind(conn, ch, P2, WB, 'inst-A', own))
            c.check(waited, 'the race did not happen: the second bind never waited on the first')
            bound = prow(pg, P2)['workflow_id']
            c.check(a.get('bound') is True and bound == WA, f'the first committer lost its binding: row {bound}, first {a}')
            c.check(b.get('bound') is False, f'the second bind claims success over a binding it never saw: {b}')
            c.check(nchunks(pg, ch, bound) == 2, f'price lists stranded: {[(w, nchunks(pg, ch, w)) for w in (WF1, WA, WB)]}')
            lost = [w for w in (WA, WB, WF1) if w != bound and not named_or_booked(pg, 'inst-A', w)
                    and not (w == WB and b.get('bound') is False)]          # a refused new clone is discarded by the graph
            c.check(lost == [], f'clones nobody names, books or discards: {lost}')
            again = impl().plan(pg, ch, 'session', 'inst-A', P2)            # the loser's client retries
            c.check(again.get('own_id') == bound, f'the retry does not reuse the winner: {again}')


def race_two_new_profiles(ch):
    P1, P2, P3 = P(ch, 'P1'), P(ch, 'P2'), P(ch, 'P3')
    WF1, WA, WB = W(ch, 'old'), W(ch, 'ca'), W(ch, 'cb')
    A, B = '7701@c.us', '7702@c.us'
    with Case(tagged(f'C2 [{ch}] two new profiles of the same bot adopt the same predecessor at once: everything it had lands '
                     'under ONE of them (the first committer) — nothing split, nothing deleted; the predecessor is marked '
                     'once; the loser does not claim the adoption')) as c:
        with Shared() as fx:
            pg = fx.main
            row(pg, ch, P1, wfid=WF1, reason='trial_expiry')
            chunks(pg, ch, WF1, 2, 'F1')
            chat_data(pg, P1, A, 'a')
            chat_data(pg, P1, B, 'b')
            row(pg, ch, P2, key=None)                    # two live new profiles of this install, the predecessor named
            row(pg, ch, P3, key=None)
            total = {k: v for k, v in counts(pg, P1).items() if k != 'star'}
            plans = {pid: impl().plan(pg, ch, 'session', 'inst-A', pid, '', 'edit', P1) for pid in (P2, P3)}
            a, b, waited = race(fx, lambda conn: impl().bind(conn, ch, P2, WA, 'inst-A', '', plans[P2].get('replace_ids') or [],
                                                             plans[P2].get('pred_profiles') or []),
                                lambda conn: impl().bind(conn, ch, P3, WB, 'inst-A', '', plans[P3].get('replace_ids') or [],
                                                         plans[P3].get('pred_profiles') or []))
            c.check(waited, 'the race did not happen: the second bind never waited on the first')
            n2, n3 = counts(pg, P2), counts(pg, P3)
            c.check({k: v for k, v in n2.items() if k != 'star'} == total, f'not all under the first committer: {n2} of {total}')
            c.check({k: v for k, v in n3.items() if k != 'star'} == {k: 0 for k in total}, f'split to the second: {n3}')
            c.check(counts(pg, P1) == ZERO, f'left under the predecessor: {counts(pg, P1)}')
            c.check(prow(pg, P1)['superseded_by'] == P2, f'the predecessor is marked by the loser: {prow(pg, P1)}')
            c.check(nchunks(pg, ch, WA) == 2, f'chunks {[(w, nchunks(pg, ch, w)) for w in (WF1, WA, WB)]}')
            c.check(a.get('adopted_profiles') == [P1] and b.get('adopted_profiles') == [],
                    f'adoption claimed by {a.get("adopted_profiles")} / {b.get("adopted_profiles")}')


CREATE_MATRIX = (reconnect_after_purchase, same_profile_clone_gone, same_profile_reuse, retire_same_bot_slot, wizard_never_adopts,
                 previous_profile_without_key, refusals, two_predecessors, idempotent_retry, legacy, sentinel, minus_one_rule,
                 protected, shared_clone, concurrent_same_profile, bind_ownership, rebind_clears_mark, booked_clone_never_reused,
                 race_same_profile, race_two_new_profiles)


# ---------------------------------------------------------------- module-only statements (no pre-F1 counterpart)

def plan_always_answers():
    m = impl().m
    with Case('M1 Clone Plan ALWAYS answers one row with planned = true (garbage, empty and unknown inputs included)') as c:
        with PgSession(SETUP) as pg:
            for args in (['session', '', '', 'whatsapp', '', '', ''], ['nonsense', 'inst-Z', 'nope', 'telegram', 'X', 'start', 'Y'],
                         ['legacy', '', '-1', 'whatsapp', '', '', '']):
                r = pg.q(bind(m.CLONE_PLAN_SQL, args))
                c.check(len(r) == 1 and r[0]['planned'] is True and r[0]['replace_ids'] == [] and r[0]['pred_profiles'] == [], f'{args}: {r}')


def sweep_branch_a():
    m = impl().m
    with Case('M2 the sweep\'s branch A takes booked clones older than 10 minutes — never one a LIVE row drives, never a protected '
              'or malformed id, never a removed one; least-tried first; the stamps settle 200/404 and count the rest') as c:
        with PgSession(SETUP) as pg:
            def book(wid, minutes, removed=False, attempts=0, reason='replaced'):
                pg.x(f"""insert into clone_retirements (workflow_id, reason, created_at, removed_at, attempts)
                         values ({L(wid)}, {L(reason)}, now() - interval '{minutes} minutes', {'now()' if removed else 'null'}, {attempts});""")
            book('OldClone00000001', 60)
            book('OldClone00000002', 11, attempts=3)
            book('OldClone00000003', 30, reason='discard_failed')
            book('YoungClone000001', 5)                                    # an in-flight Create
            book('GoneClone0000001', 60, removed=True)
            book(m.PROTECTED[0], 60)                                       # tampered
            book('bad id', 60)
            book('LiveDriven000001', 60)
            row(pg, 'WA', 'wa-live', wfid='LiveDriven000001')
            book('RetiredOnly00001', 60)
            row(pg, 'WA', 'wa-ret', wfid='RetiredOnly00001', reason='replaced')
            got = [r['workflow_id'] for r in pg.q(m.CLONE_RETIREMENTS_SQL)]
            c.check(got == ['OldClone00000001', 'RetiredOnly00001', 'OldClone00000003', 'OldClone00000002'], f'picked {got}')
            r = pg.q(bind(m.STAMP_RETIREMENT_REMOVED_SQL, ['OldClone00000001', '404']))
            c.check(r == [{'workflow_id': 'OldClone00000001'}], f'removed stamp {r}')
            r = pg.q(bind(m.STAMP_RETIREMENT_KEPT_SQL, ['OldClone00000003', '500']))
            c.check(r == [{'workflow_id': 'OldClone00000003', 'attempts': 1}], f'kept stamp {r}')
            r = pg.q(bind(m.STAMP_RETIREMENT_KEPT_SQL, ['OldClone00000001', '500']))
            c.check(r == [], f'a removed row was counted again: {r}')
            st = pg.q("""select workflow_id, removed_at is not null as removed, attempts, last_status from clone_retirements
                         where workflow_id in ('OldClone00000001', 'OldClone00000003') order by 1""")
            c.check(st == [{'workflow_id': 'OldClone00000001', 'removed': True, 'attempts': 0, 'last_status': '404'},
                           {'workflow_id': 'OldClone00000003', 'removed': False, 'attempts': 1, 'last_status': '500'}], f'state {st}')
            got = [r['workflow_id'] for r in pg.q(m.CLONE_RETIREMENTS_SQL)]
            c.check(got == ['RetiredOnly00001', 'OldClone00000003', 'OldClone00000002'], f'after the stamps {got}')
            pg.x("insert into clone_retirements (workflow_id, reason, created_at) select 'Bulk' || lpad(g::text, 12, '0'), 'replaced', "
                 "now() - interval '1 hour' from generate_series(1, 80) g;")
            c.check(len(pg.q(m.CLONE_RETIREMENTS_SQL)) == 50, 'more than 50 per run')


def discard_failure():
    m = impl().m
    with Case('M3 Record Discard Failure books an undeletable new clone once (discard_failed) — never a protected or malformed id') as c:
        with PgSession(SETUP) as pg:
            for _ in range(2):
                pg.q(bind(m.RECORD_DISCARD_FAILURE_SQL, ['NewClone00000001', 'wa-P2', 'inst-A']))
            pg.q(bind(m.RECORD_DISCARD_FAILURE_SQL, [m.PROTECTED[0], 'wa-P2', 'inst-A']))
            pg.q(bind(m.RECORD_DISCARD_FAILURE_SQL, ['-1', 'wa-P2', '']))
            c.check(retirements(pg) == [{'workflow_id': 'NewClone00000001', 'profile_id': 'wa-P2', 'owner_id': 'inst-A',
                                         'reason': 'discard_failed', 'replaced_by': None}], f'{retirements(pg)}')


def lineage():
    m = impl().m
    stmt = ('with recursive ' + m.lineage_cte('$1', '$2', '$3', '$4') +
            "\nselect (select coalesce(jsonb_agg(profile_id order by profile_id), '[]'::jsonb) from lineage_profiles) as profiles,"
            "\n       (select coalesce(jsonb_agg(workflow_id order by workflow_id), '[]'::jsonb) from lineage_workflows) as workflows")
    with Case('M4 the bot\'s line (Delete Bot Files, T5): every row of this install with the named rows\' bot key, both channels, plus '
              'the superseded chain — never another install\'s rows, another bot key or a protected id; legacy / revoked: the named '
              'ids only') as c:
        with PgSession(SETUP) as pg:
            row(pg, 'WA', 'wa-cur', wfid='WAcur00000000001')
            row(pg, 'WA', 'wa-old', wfid='WAold00000000001', reason='replaced', sup='wa-cur')
            row(pg, 'WA', 'wa-anc', wfid='WAanc00000000001', key=None, reason='liveness', sup='wa-old')   # no key: the chain only
            row(pg, 'WA', 'wa-slot', wfid='WAslot0000000001', reason='app_delete')                        # an earlier bot of the slot
            row(pg, 'TG', 'tg-cur', wfid='TGcur00000000001')
            row(pg, 'TG', 'tg-old', wfid=m.PROTECTED[0], reason='trial_expiry')                            # tampered binding
            row(pg, 'WA', 'wa-b', owner='inst-B', wfid='WAforeign0000001')                                 # same key, other install
            row(pg, 'WA', 'wa-7', wfid='WAseven000000001', key='Bot7')                                     # another bot
            row(pg, 'WA', 'wa-nk', wfid='WAnokey000000001', key=None)                                      # another key-less row
            got = pg.q(bind(stmt, ['session', 'inst-A', json.dumps(['wa-cur', '-1']), json.dumps(['WAcur00000000001', '-1'])]))[0]
            c.check(got == {'profiles': ['tg-cur', 'tg-old', 'wa-anc', 'wa-cur', 'wa-old', 'wa-slot'],
                            'workflows': ['TGcur00000000001', 'WAanc00000000001', 'WAcur00000000001', 'WAold00000000001',
                                          'WAslot0000000001']}, f'session line {got}')
            got = pg.q(bind(stmt, ['session', 'inst-A', json.dumps(['-1', 'tg-cur']), json.dumps(['-1', 'TGcur00000000001'])]))[0]
            c.check('wa-anc' in got['profiles'] and 'wa-7' not in got['profiles'], f'from the other channel {got}')
            got = pg.q(bind(stmt, ['legacy', '', json.dumps(['wa-cur', '-1']), json.dumps(['WAcur00000000001', None])]))[0]
            c.check(got == {'profiles': ['wa-cur'], 'workflows': ['WAcur00000000001']}, f'legacy {got}')
            row(pg, 'WA', 'wa-r', owner='inst-R', wfid='WArev00000000001')
            row(pg, 'WA', 'wa-r2', owner='inst-R', wfid='WArev00000000002', reason='trial_expiry')
            got = pg.q(bind(stmt, ['session', 'inst-R', json.dumps(['wa-r']), json.dumps(['WArev00000000001'])]))[0]
            c.check(got == {'profiles': ['wa-r'], 'workflows': ['WArev00000000001']}, f'revoked {got}')
            got = pg.q(bind(stmt, ['session', 'inst-A', json.dumps(['', 'null', 'undefined', 7]), json.dumps([m.PROTECTED[1]])]))[0]
            c.check(got == {'profiles': [], 'workflows': []}, f'sentinels / protected {got}')


def table_set_and_guards():
    m = impl().m
    with Case('M5 the moved tables are EXACTLY the tables Delete Chat Memory clears (current canon); the bind names each; PROTECTED '
              '= build-client-webhooks.PROTECTED_IDS + Set Workflow State + the support relay; CHANNELS match the Create canons') as c:
        dcm = node(wf(DBF_F), 'Delete Chat Memory')['parameters']['query']
        cleared = sorted(set(re.findall(r'DELETE FROM (?:public\.)?(\w+)', dcm, re.I)))
        c.check(sorted(m.MOVED_TABLES) == cleared, f'moved {sorted(m.MOVED_TABLES)} != cleared {cleared}')
        for ch_name, sql in m.BIND_WORKFLOW_SQL.items():
            for t in m.MOVED_TABLES:
                c.check(re.search(r'\b' + t + r'\b', sql), f'{ch_name} bind never touches {t}')
        bcw = module('build-client-webhooks.py')
        c.check(sorted(m.PROTECTED) == sorted(set(bcw.PROTECTED_IDS) | {'0DP79STI3pafXO9B', 'olmqWMfcFpCEcpiE'}), 'PROTECTED drifted')
        for ch, spec in CH.items():
            mc = m.CHANNELS[spec['channel']]
            c.check((mc['create_file'], mc['meta_key'], mc['other_meta_key'], mc['file_column']) ==
                    (spec['create'], spec['key'], spec['other'], spec['fcol']), f'{ch}: {mc}')
            reg = node(wf(spec['create']), 'Register Profile')['parameters']['options']['queryReplacement']
            c.check(f"'{spec['channel']}'" in reg and mc['profile_field'] in reg, f'{ch}: Register Profile {reg}')


def sql_comment_guard():
    m = impl().m
    guard = module('verify-sql-comments.py')
    with Case('M6 V1: every statement of the module passes verify-sql-comments (no $N in a comment or literal, no {{, no modifier, '
              'no leading =)') as c:
        stmts = m.statements()
        c.check({'Clone Plan', 'Bind Workflow Id (whatsapp)', 'Bind Workflow Id (telegram)', 'Clone Retirements',
                 'Stamp Retirement Removed', 'Stamp Retirement Kept', 'Record Discard Failure', 'Bot Line'} <= set(stmts), sorted(stmts))
        doc = {'nodes': [{'name': k, 'type': 'n8n-nodes-base.postgres', 'parameters': {'operation': 'executeQuery', 'query': v}}
                         for k, v in stmts.items()]}
        found, n = guard.check_workflow(doc)
        c.check(n == len(stmts) and found == [], f'{found}')
        for k, v in stmts.items():
            c.check('$' not in ''.join(l.split('--', 1)[1] for l in v.splitlines() if '--' in l), f'{k}: a $ in a comment')


def timing():
    """Plan §3 T2 risk: the bind's cost over the pre-F1 Bind on a big history (target < 1 s extra)."""
    m = impl().m
    head_w = {ch: canon(c['create'], pinned=True) for ch, c in CH.items()}
    with Case('M7 cost on a big history: 10 000 memory rows of the predecessor among 30 000, 5 000 chunks — the line\'s plan + bind '
              'stay within 1 s of the pre-F1 Restamp + Bind') as c:
        res = {}
        for ch in CH:
            P1, P2 = P(ch, 'P1'), P(ch, 'P2')
            seed = f"""
insert into n8n_chat_histories (session_id, message)
  select {L(P1)} || ':77' || lpad((g % 100)::text, 4, '0') || '@c.us', jsonb_build_object('type', 'human', 'content', 'x' || g)
  from generate_series(1, 10000) g;
insert into n8n_chat_histories (session_id, message)
  select 'other-' || (g % 50) || ':77' || lpad((g % 100)::text, 4, '0') || '@c.us', jsonb_build_object('type', 'ai', 'content', 'y')
  from generate_series(1, 20000) g;
insert into chat_history_events (profile_id, chat_id, message_id, source, created_at)
  select {L(P1)}, '77' || lpad((g % 100)::text, 4, '0') || '@c.us', 'm' || g, 'client', now() from generate_series(1, 3000) g;
insert into documents (content, metadata)
  select 'c', jsonb_build_object('{CH[ch]['key']}', case when g <= 200 then {L(W(ch, 'old'))} else 'Other' || lpad((g % 40)::text, 11, '0') end,
                                  '{CH[ch]['other']}', '-1') from generate_series(1, 5000) g;
analyze n8n_chat_histories; analyze chat_history_events; analyze documents; analyze bot_profiles;"""
            times = {}
            for label in ('pre-F1', 'line'):
                with PgSession(SETUP) as pg:
                    row(pg, ch, P1, wfid=W(ch, 'old'), reason='trial_expiry')
                    pg.x(seed)
                    proxy_row(pg, ch, P2)
                    register(pg, ch, P2)
                    t0 = time.perf_counter()
                    if label == 'pre-F1':
                        pg.x(bind(Q(head_w[ch], 'Restamp RAG Chunks'), [W(ch, 'new'), '-1']))
                        pg.x(bind(Q(head_w[ch], 'Bind Workflow Id'), [P2, W(ch, 'new'), U, CH[ch]['channel'], 'inst-A']))
                    else:
                        plan = pg.q(bind(m.CLONE_PLAN_SQL, ['session', 'inst-A', P2, CH[ch]['channel'], 'Bot0', 'edit', P1]))[0]
                        b = pg.q(bind(m.BIND_WORKFLOW_SQL[CH[ch]['channel']], [P2, W(ch, 'new'), U, CH[ch]['channel'], 'inst-A', '',
                                                                            json.dumps(plan['replace_ids']), json.dumps(plan['pred_profiles']), '-1']))[0]
                        c.check(b['bound'] and b['moved_memory'] == 10000 and b['restamped_chunks'] == 200, f'{ch} {b}')
                    times[label] = time.perf_counter() - t0
            res[ch] = {k: round(v, 3) for k, v in times.items()}
            c.check(times['line'] - times['pre-F1'] < 1.0, f'{ch}: {res[ch]}')
        print('      timing (s): ' + json.dumps(res))


def chunk_index_use():
    """The prod RAG store indexes documents by EXPRESSION (supabase/2026-07-02-harden-rag-store.sql: btree on
    metadata->>'botWaId' / 'botTgId'); the pre-F1 Restamp and Delete Bot Chunks compare that very expression. A jsonb
    containment (@>) cannot use those indexes and would scan the whole store, embeddings and all, on every Create."""
    m = impl().m
    ddl = [l.replace('public.', '').strip() for l in (N8N / 'supabase' / '2026-07-02-harden-rag-store.sql').read_text().splitlines()
           if re.match(r'create index if not exists documents_metadata_bot_(wa|tg)_idx', l)]
    with Case('M8 the bind\'s chunk moves can use the RAG store\'s expression indexes (prod documents_metadata_bot_wa_idx / '
              '_tg_idx) — the replaced clones\' chunks and the -1 rule alike, both channels') as c:
        c.check(len(ddl) == 2, f'index DDL not found: {ddl}')
        with PgSession(SETUP) as pg:
            pg.x('\n'.join(ddl) + '\nanalyze documents;\nset enable_seqscan = off;')
            for ch, spec in CH.items():
                other_wf = W('TG' if ch == 'WA' else 'WA', 'oth')
                plan = pg.x('explain ' + bind(m.BIND_WORKFLOW_SQL[spec['channel']],
                                              [P(ch, 'P2'), W(ch, 'new'), U, spec['channel'], 'inst-A', W(ch, 'own'), '[]', '[]', other_wf])
                            + ';')
                docs = [l for l in plan.splitlines() if 'documents' in l]
                updates = [l for l in docs if 'Update on documents' in l]
                expr = [l for l in docs if re.search(r'(Index Scan using|Bitmap Index Scan on) documents_metadata_bot_(wa|tg)_idx', l)]
                other = [l for l in docs if 'Seq Scan on documents' in l or 'documents_metadata_idx' in l]   # a scan / the GIN stub
                c.check(len(updates) == 2 and len(expr) >= 2 and not other,
                        f'{ch}: a chunk move that does not go through the expression indexes:\n' + '\n'.join(docs))


if __name__ == '__main__':
    for fn in CREATE_MATRIX:
        for ch in CH:
            fn(ch)
    if not HEAD_MODE:
        for fn in (plan_always_answers, sweep_branch_a, discard_failure, lineage, table_set_and_guards, sql_comment_guard, timing,
                   chunk_index_use):
            fn()
    report()
