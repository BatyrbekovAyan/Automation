#!/usr/bin/env python3
"""Learning A (2026-10-13): the SQL of the learning journal, run as real SQL on session temp tables of the staging Postgres
(tests/harness.py) — Record Set (insert / dedupe / empty key), Expire Learning Rows (age), and the sets CTE of Delete Chat
Memory (the bot's LINE: a superseded sibling goes, a neighbour of another bot key stays; legacy = the named ids only).
Needs `Tools/n8n/staging/pg.sh migrate` (the public tables are the templates of the temp ones)."""
import importlib.util
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from harness import Case, PgSession, bind, node, report, wf  # noqa: E402
import test_clone_lineage_sql as LIN  # noqa: E402 — the lineage seeders + impl().bind; its cases run only under __main__

N8N = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("apply_learning_a", N8N / "apply-learning-a.py")
LA = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(LA)

# the tables Delete Chat Memory touches (as tests/test_retired_delete.py) + the learning journal
LIKE = 'create temp table {0} (like public.{0} including all);\n'
SETUP = ''.join(LIKE.format(t) for t in ('installs', 'bot_profiles', 'n8n_chat_histories', 'chat_history_events', 'handoff_requests',
                                         'reply_events', 'chat_reply_state', 'conversation_outcomes', 'reply_mode_flags', 'suggestion_sets'))
INST = 'inst-learning-a'
DBF = wf('lmjYsdNcQA2IE5rl-Delete_Bot_Files.json')
SUGGEST = wf('9PTyYcelRQI7bGDb-Suggest_Replies.json')


def Q(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')   # the temp tables shadow the UNqualified names only


def seed(pg):
    # the seeders of tests/test_retired_delete.py: installs (install_id, secret_hash); bot_profiles needs app_user_id + channel
    pg.x(f"insert into installs (install_id, secret_hash) values ('{INST}', 'h') on conflict do nothing;")
    pg.x(f"insert into bot_profiles (profile_id, app_user_id, channel, owner_id, workflow_id, bot_key) values "
         f"('p-old', 'u', 'whatsapp', '{INST}', 'wOld00000001', 'k1'), ('p-new', 'u', 'whatsapp', '{INST}', 'wNew00000001', 'k1'), "
         f"('p-other', 'u', 'whatsapp', '{INST}', 'wOth00000001', 'k2');")
    pg.x("update bot_profiles set superseded_by = 'p-new', superseded_at = now(), deleted_at = now(), deleted_reason = 'replaced' where profile_id = 'p-old';")
    for pid in ('p-old', 'p-new', 'p-other'):
        pg.x(f"insert into suggestion_sets (request_key, profile_id, chat_id, items, stage) values ('k-{pid}', '{pid}', 'c', '[]'::jsonb, 'learning');")


def line_args(mode, named):
    return [mode, INST if mode == 'session' else '', json.dumps([named, None]), json.dumps([None, None])]


with PgSession(SETUP) as pg:
    with Case('Record Set: insert, dedupe on the same key, empty key writes nothing') as c:
        sql = Q(SUGGEST, 'Record Set')
        args = ['0123456789abcdef', 'p-rs', 'c', 1754800000, 'auto_parts', 'learning', json.dumps([{"label": "Цена", "move": "Ответ", "text": "3200 тг"}]), LA.PROMPT_VERSION, None]
        c.check(len(pg.q(bind(sql, args))) == 1, 'first insert returns one row')
        c.check(pg.q(bind(sql, args)) == [], 'the same (profile, request_key) again returns nothing (on conflict do nothing)')
        c.check(pg.q("select count(*)::int as n from suggestion_sets where profile_id = 'p-rs'")[0]['n'] == 1, 'exactly one set per request key')
        pg.q(bind(sql, ['', 'p-rs', 'c', None, 'auto_parts', 'learning', '[]', LA.PROMPT_VERSION, None]))
        c.check(pg.q("select count(*)::int as n from suggestion_sets where request_key = ''")[0]['n'] == 0, 'an empty request key is never a primary key')
    with Case('Expire Learning Rows: a 181-day set and origin go, a young one stays') as c:
        pg.x("insert into suggestion_sets (request_key, profile_id, chat_id, items, created_at) values ('old', 'p-exp', 'c', '[]', now() - interval '181 days'), ('young', 'p-exp', 'c', '[]', now());")
        pg.x("insert into chat_history_events (profile_id, chat_id, message_id, source, body, origin_kind, origin_key, created_at) values ('p-exp', 'c', 'm1', 'owner_app', 't', 'none', null, now() - interval '181 days');")
        r = pg.q(LA.EXPIRE_SQL.replace('public.', ''))[0]
        c.check(r['expired_sets'] == 1 and r['expired_origins'] == 1, f'counts {r}')
        c.check(pg.q("select request_key from suggestion_sets where profile_id = 'p-exp'") == [{'request_key': 'young'}], 'young set kept')
        c.check(pg.q("select origin_kind from chat_history_events where message_id = 'm1'")[0]['origin_kind'] is None, 'origin_kind nulled (none expires too)')
    with Case('Delete Chat Memory (session): the sets of the line go — named + superseded sibling — the neighbour stays') as c:
        seed(pg)
        r = pg.q(bind(Q(DBF, 'Delete Chat Memory'), line_args('session', 'p-new')))[0]
        c.check(r['deletedSuggestionSets'] == 2, f'line = p-new + superseded p-old: {r}')
        c.check(all(isinstance(r[k], int) for k in r), f'every counter a number (Deletion Settled? reads typeof === number): {r}')
        c.check(pg.q("select count(*)::int as n from suggestion_sets where profile_id in ('p-old', 'p-new')")[0]['n'] == 0, 'both rows of the line gone')
        c.check(pg.q("select count(*)::int as n from suggestion_sets where profile_id = 'p-other'")[0]['n'] == 1, 'p-other (another bot key, same install) untouched')
    with Case('Delete Chat Memory (legacy): the named ids only') as c:
        pg.x("delete from suggestion_sets where profile_id like 'p-%'; delete from bot_profiles where profile_id like 'p-%';"); seed(pg)
        r = pg.q(bind(Q(DBF, 'Delete Chat Memory'), line_args('legacy', 'p-new')))[0]
        c.check(r['deletedSuggestionSets'] == 1 and pg.q("select count(*)::int as n from suggestion_sets where profile_id = 'p-old'")[0]['n'] == 1, f'legacy: only p-new: {r}')
    with PgSession(LIN.SETUP) as lg:   # the lineage fixture already carries suggestion_sets (its MEMORY_TABLES)
        ch = 'WA'; P1, P2 = LIN.P(ch, 'P1'), LIN.P(ch, 'P2'); WOLD, WNEW = LIN.W(ch, 'old'), LIN.W(ch, 'new')
        LIN.row(lg, ch, P1, wfid=WOLD, reason='trial_expiry')     # the retired predecessor (same bot key) with its clone
        LIN.proxy_row(lg, ch, P2)                                  # the new profile, this install's from birth
        lg.x(f"insert into suggestion_sets (request_key, profile_id, chat_id, items, created_at) values "
             f"('k-shared', '{P1}', 'c', '[]', now() - interval '1 day'), ('k-only', '{P1}', 'c', '[]', now() - interval '1 day'), "
             f"('k-shared', '{P2}', 'c', '[\"new\"]', now());")
        b = LIN.impl().bind(lg, ch, P2, WNEW, 'inst-A', '', [WOLD], [P1])
        with Case("Bind moves the predecessor's sets: moved_sets = 1 (k-only), the duplicate k-shared is dropped, the new profile's own row wins") as c:
            c.check(b.get('bound') is True and b.get('moved_sets') == 1, f'bind answer: {b}')
            rows = lg.q(f"select request_key, items::text as items from suggestion_sets where profile_id = '{P2}' order by 1")
            c.check(rows == [{'request_key': 'k-only', 'items': '[]'}, {'request_key': 'k-shared', 'items': '["new"]'}], f'new profile rows: {rows}')
            c.check(lg.q(f"select count(*)::int as n from suggestion_sets where profile_id = '{P1}'")[0]['n'] == 0, 'the predecessor keeps no set')
report()
