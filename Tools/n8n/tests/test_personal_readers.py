#!/usr/bin/env python3
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §4.3 «Личное не
сохраняется нигде», §4.2 «Панель подсказок», §5.8; plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 5): the three
server places outside the bot templates that write or read a chat pass a chat only when it is a decided BUSINESS chat
(audience.BUSINESS_SQL: a decision that holds and says business). A personal chat, a chat without a decision yet and a
computed decision another number voided are not business, and for them:

  * the Wappi Proxy's Record Owner Send (the owner's sends from the app — owner_app, after the response) writes neither the
    history nor the memory;
  * Suggest Replies' Suggestion Gate refuses the request as personal_chat before ANY counter row is written
    (suggestion_request_keys, suggestion_counts); Build Response names that refusal to an authenticated session caller only,
    a legacy caller keeps the opaque generation_failed; a gate that could not read the chat (a database error) no longer
    opens the panel;
  * Dashboard Outcomes leaves the chat out of the classifier's input (Find Changed Sessions — its memory never reaches the
    «Сводка» LLM) and out of what the app gets (Fetch Outcomes — the classifier's verdicts and the open handoffs alike).

A business chat behaves exactly as before: every business case also runs the node of the canon BEFORE this change (PRE_REV,
read with git show) on the same data and must give the same rows.

What runs — the REAL node bodies of the canonical exports: the SQL bound from the node's own queryReplacement, evaluated by
n8n's expression engine (harness.expr), on session temp tables of the staging Postgres (`like public.X including all` — the
migration sql/2026-10-25-chat-audience.sql must be applied: pg.sh migrate); the Code nodes Gate Decision and Build Response
through node. The decisions are written by audience.py's own statements (CONNECTED_SQL, DECIDE_WRITE_SQL, OWNER_SET_SQL).
Both channels. No n8n server, no network, never prod.

  audience_premise                                     the six chats of each channel are decided as the cases assume
  owner_app_in_personal_or_undecided_writes_nothing    personal (owner / computed), undecided, voided: no history row, no
                                                       memory row; the answer says business = false
  owner_app_in_business_writes_history_and_memory      owner / computed business: the owner_app row and the marked memory row,
                                                       byte for byte what the canon before wrote
  suggest_refused_for_personal_and_undecided_counts_nothing   active / expired / unknown account, with and without a request
                                                       key: refused, personal_chat, not one counter row; session → the
                                                       named refusal, legacy → generation_failed
  suggest_gate_failure_refuses                         a database error (an error item, or the input handed on) refuses:
                                                       audience_unknown → generation_failed for every caller
  suggest_business_unchanged                           allowed / the same key again / a legacy call / expired / unknown /
                                                       over the cap: the same verdicts and counters as the canon before
  dashboard_skips_personal_in_classifier_and_fetch     only the business sessions reach the classifier (a chat id with a
                                                       colon of its own included — the session splits at its FIRST colon),
                                                       only business chats come back (verdicts and handoffs); the business
                                                       rows equal the canon's
  statements_pass_the_sql_guard                        verify-sql-comments finds nothing in the changed statements

    python3 Tools/n8n/tests/test_personal_readers.py
"""
import base64
import json
import subprocess

from harness import ROOT, Case, PgSession, bind, code, expr, module, node, report, sql_literal as L, wf

PRE_REV = '35812046'          # the canon before this change — what «unchanged» is measured against
PROXY_F = 'crWappiProxy0001-Wappi_Proxy.json'
SUGGEST_F = '9PTyYcelRQI7bGDb-Suggest_Replies.json'
DASH_F = '2htWSV5IHO8E2CgB-Dashboard_Outcomes.json'

AUD = module('audience.py')
VSC = module('verify-sql-comments.py')


def pre(fname):
    out = subprocess.run(['git', '-C', str(ROOT), 'show', f'{PRE_REV}:Tools/n8n/workflows/{fname}'],
                         capture_output=True, text=True, check=True).stdout
    return json.loads(out)


NOW = {f: wf(f) for f in (PROXY_F, SUGGEST_F, DASH_F)}
OLD = {f: pre(f) for f in (PROXY_F, SUGGEST_F, DASH_F)}

TABLES = ('bot_profiles', 'chat_audience', 'audience_connections', 'chat_history_events', 'n8n_chat_histories',
          'conversation_outcomes', 'handoff_requests', 'subscribers', 'suggestion_counts', 'suggestion_request_keys')
# LIKE copies the PUBLIC sequence as the id default: the temp copies get their own, so nothing shared moves
SETUP = (''.join(f'create temp table {t} (like public.{t} including all);\n' for t in TABLES)
         + ''.join(f"create temp sequence {t}_pr_seq; alter table {t} alter column id set default nextval('{t}_pr_seq');\n"
                   for t in ('chat_history_events', 'n8n_chat_histories', 'handoff_requests')))

BUSINESS = ('business_owner', 'business_default')
NOT_BUSINESS = ('personal_owner', 'personal_default', 'undecided', 'voided')
CH = {
    'WA': {'channel': 'whatsapp', 'pid': 'wa-readers-p1',
           'chats': {'business_owner': '77011110001@c.us', 'business_default': '77011110002@c.us',
                     'personal_owner': '77011110003@c.us', 'personal_default': '77011110004@c.us',
                     'undecided': '77011110005@c.us', 'voided': '77011110006@c.us'}},
    'TG': {'channel': 'telegram', 'pid': 'tg-readers-p1',
           'chats': {'business_owner': '5511110001', 'business_default': '5511110002', 'personal_owner': '5511110003',
                     'personal_default': '5511110004', 'undecided': '5511110005', 'voided': '5511110006'}},
}
COLON_CHAT = 'odd:chat'        # a chat id that carries a colon of its own (the dashboard case: the session splits at the FIRST one)


# ------------------------------------------------------------------------------------------------ the decisions
def tick(pg):
    """Statements autocommit one by one, each with its own now(); a short sleep keeps two of them strictly apart."""
    pg.x('select pg_sleep(0.003);')


def seed_audience(pg, ch):
    """The six chats of channel `ch`, decided by audience.py's own statements: the owner's business and personal, a computed
    business (new) and personal (contact) taken on the current connection, nothing for `undecided`, and a computed business
    taken BEFORE the number changed — it no longer holds (`voided`)."""
    pid, chats = CH[ch]['pid'], CH[ch]['chats']
    pg.q(bind(AUD.CONNECTED_SQL, [pid, '77010000001']))
    pg.q(bind(AUD.DECIDE_WRITE_SQL, [pid, chats['voided'], 'business', 'new']))
    tick(pg)
    pg.q(bind(AUD.CONNECTED_SQL, [pid, '77010000002']))             # another number: a new connection
    tick(pg)
    pg.q(bind(AUD.DECIDE_WRITE_SQL, [pid, chats['business_default'], 'business', 'new']))
    pg.q(bind(AUD.DECIDE_WRITE_SQL, [pid, chats['personal_default'], 'personal', 'contact']))
    pg.q(bind(AUD.OWNER_SET_SQL, [pid, [{'chatId': chats['business_owner'], 'kind': 'business', 'version': 1},
                                        {'chatId': chats['personal_owner'], 'kind': 'personal', 'version': 1}]]))


def is_business(pg, pid, chat):
    return pg.q(bind('select ' + AUD.BUSINESS_SQL() + ' as b', [pid, chat]))[0]['b']


# ------------------------------------------------------------------------------------------------ the proxy
def owner_send(w, pg, ch, chat, mid, text):
    """The Wappi Proxy's Record Owner Send for an app send — WhatsApp message.send to the bare number (the proxy adds
    @c.us), Telegram message.reply naming the chat in meta — with the node's own query and parameters."""
    pid = CH[ch]['pid']
    if ch == 'WA':
        br = {'op': 'message.send', 'channel': 'whatsapp', 'profileId': pid, 'meta': {},
              'jsonBody': {'recipient': chat.replace('@c.us', ''), 'body': text}}
    else:
        br = {'op': 'message.reply', 'channel': 'telegram', 'profileId': pid, 'meta': {'chat_id': chat, 'origin_kind': 'typed'},
              'jsonBody': {'body': text, 'message_id': 'q-' + mid}}
    named = {'Build Request': br, 'Wappi Call': {'statusCode': 200, 'body': {'status': 'done', 'message_id': mid}}}
    n = node(w, 'Record Owner Send')
    params = expr(n['parameters']['options']['queryReplacement'], {}, named, 'exec-proxy-' + mid)
    assert isinstance(params, list) and params[1] == chat, f'the proxy keyed the send by {params!r}'
    rows = pg.q(bind(n['parameters']['query'], params))
    return rows[0] if rows else {}


def history(pg, pid, chat):
    return pg.q(f"select message_id, source, body, msg_type, execution_id, origin_kind, origin_index, origin_key, origin_edit, "
                f"origin_top1, origin_set from chat_history_events where profile_id = {L(pid)} and chat_id = {L(chat)} order by id")


def memory(pg, pid, chat):
    return [r['message'] for r in pg.q(f"select message from n8n_chat_histories where session_id = {L(pid + ':' + chat)} order by id")]


def proxy_run(w, ch, kind):
    """One send into chat `kind` on a freshly seeded session: (the answer row, the history rows, the memory rows)."""
    pid, chat = CH[ch]['pid'], CH[ch]['chats'][kind]
    with PgSession(SETUP) as pg:
        seed_audience(pg, ch)
        ans = owner_send(w, pg, ch, chat, 'own-' + kind, 'Ждём вас завтра в 15:00.')
        return ans, history(pg, pid, chat), memory(pg, pid, chat), pg.q('select count(*)::int as n from chat_history_events')[0]['n'], \
            pg.q('select count(*)::int as n from n8n_chat_histories')[0]['n']


# ------------------------------------------------------------------------------------------------ the panel gate
SUBSCRIBERS = """insert into subscribers (app_user_id, plan, status) values
  ('u-active', 'business', 'active'), ('u-expired', 'business', 'expired'), ('u-capped', 'start', 'active');
insert into suggestion_counts (app_user_id, d, n) values ('u-capped', (now() at time zone 'Asia/Almaty')::date, 100);"""


def prep_json(ch, chat, app_user, key):
    """What Prep hands the gate (the fields the gate, Gate Decision and Build Response read)."""
    return {'v': 1, 'requestSeq': 7, 'invalid': False, 'appUserId': app_user, 'requestKey': key, 'profileId': CH[ch]['pid'],
            'chatId': chat, 'pickStats': '', 'messages': [{'role': 'client', 'text': 'Сколько стоит?'}]}


def gate(w, pg, prep):
    n = node(w, 'Suggestion Gate')
    params = expr(n['parameters']['options']['queryReplacement'], prep)
    assert isinstance(params, list), f'the gate parameters are not a list: {params!r}'
    rows = pg.q(bind(n['parameters']['query'], params))
    return rows[0] if rows else {}


def gate_decision(w, prep, row):
    return code(w, 'Gate Decision', {'json': row}, {'Prep': prep})[0]['json']


def build_response(w, gd, mode):
    return code(w, 'Build Response', {'json': gd}, {'Auth Gate': {'crAuth': {'mode': mode}}})[0]['json']


def counters(pg):
    return pg.q("select (select count(*) from suggestion_request_keys)::int as keys, "
                "(select count(*) from suggestion_counts where app_user_id <> 'u-capped')::int as days, "
                "(select coalesce(sum(n), 0) from suggestion_counts where app_user_id <> 'u-capped')::int as n, "
                "(select n from suggestion_counts where app_user_id = 'u-capped') as capped")[0]


GATE_STEPS = [   # (app user, request key): the business scenario, run in order on one session
    ('u-active', 'b0001'),        # allowed, counted
    ('u-active', 'b0001'),        # the same key again (a retry after the deadline): served, not counted twice
    ('u-active', ''),             # a legacy client without a key: counted
    ('u-expired', 'b0002'),       # refused: subscription_expired
    ('u-nobody', 'b0003'),        # refused: unknown_account
    ('u-capped', 'b0004'),        # refused: daily_cap (n = 101)
]


def gate_run(w, ch, kind):
    """The business scenario on a freshly seeded session: per step the verdict row (without the new `business` column),
    Gate Decision's verdict and the counters after it."""
    out = []
    chat = CH[ch]['chats'][kind]
    with PgSession(SETUP) as pg:
        seed_audience(pg, ch)
        pg.x(SUBSCRIBERS)
        for app_user, key in GATE_STEPS:
            prep = prep_json(ch, chat, app_user, key)
            row = gate(w, pg, prep)
            gd = gate_decision(w, prep, row)
            out.append(({k: row.get(k) for k in ('known', 'status', 'n', 'allowed')}, row.get('business'),
                        {'gateAllowed': gd.get('gateAllowed'), 'gateReason': gd.get('gateReason')}, counters(pg)))
    return out


# ------------------------------------------------------------------------------------------------ the dashboard
def b64(ids):
    return base64.b64encode(json.dumps(ids).encode()).decode()


def seed_dashboard(pg, ch):
    """For every chat of channel `ch` (and the colon chat, an owner business decision): two memory rows, a classifier verdict
    and an open handoff request."""
    pid = CH[ch]['pid']
    pg.q(bind(AUD.OWNER_SET_SQL, [pid, [{'chatId': COLON_CHAT, 'kind': 'business', 'version': 1}]]))
    for kind, chat in list(CH[ch]['chats'].items()) + [('business_colon', COLON_CHAT)]:
        sid = pid + ':' + chat
        for typ, text in (('human', 'Клиент спрашивает: ' + kind), ('ai', 'Ответ бота: ' + kind)):
            pg.x("insert into n8n_chat_histories (session_id, message) values ("
                 f"{L(sid)}, jsonb_build_object('type', {L(typ)}, 'content', {L(text)}, 'additional_kwargs', '{{}}'::jsonb, "
                 "'response_metadata', '{}'::jsonb));")
        pg.x("insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at, "
             f"outcome_at) values ({L(sid)}, {L(pid)}, {L(chat)}, 'in_dialog', {L('итог: ' + kind)}, 0, now(), now());")
        pg.x("insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values "
             f"({L(pid)}, {L(chat)}, {L('src-' + kind)}, 'order', {L('заявка: ' + kind)});")


def dash_query(w, name):
    return node(w, name)['parameters']['query'].replace('public.', '')   # the temp tables shadow the UNqualified names only


def dash_read(pg, w, pid):
    """(the classifier's input: sessions by chat id, its total; what Fetch Outcomes returns: rows by chat id). Both nodes
    only read, so the canon before and today's run on the SAME data — and the same clock — in one session."""
    found = pg.q(bind(dash_query(w, 'Find Changed Sessions'), [b64([pid])]))[0]
    fetched = pg.q(bind(dash_query(w, 'Fetch Outcomes'), [b64([pid])]))
    return {s['chat_id']: s for s in (found.get('sessions') or [])}, found.get('total'), {r['chatId']: r for r in fetched}


def dash_run(ch):
    """dash_read for today's canon and for the canon before, on one seeded session."""
    pid = CH[ch]['pid']
    with PgSession(SETUP) as pg:
        seed_audience(pg, ch)
        seed_dashboard(pg, ch)
        return dash_read(pg, NOW[DASH_F], pid), dash_read(pg, OLD[DASH_F], pid)


# ================================================================================================ the cases
for ch in CH:
    pid, chats = CH[ch]['pid'], CH[ch]['chats']
    with Case(f'audience_premise [{ch}]: the six chats are decided as the cases assume (business = owner / computed business only)') as c:
        with PgSession(SETUP) as pg:
            seed_audience(pg, ch)
            got = {kind: is_business(pg, pid, chat) for kind, chat in chats.items()}
        c.check(got == {k: k in BUSINESS for k in chats}, f'BUSINESS_SQL per chat: {got}')

    # -------------------------------------------------------------------------------------------- the proxy
    with Case(f'owner_app_in_personal_or_undecided_writes_nothing [{ch}]: personal (owner / computed), undecided, voided — '
              'no history row, no memory row, the answer says business = false') as c:
        for kind in NOT_BUSINESS:
            ans, hist, mem, n_hist, n_mem = proxy_run(NOW[PROXY_F], ch, kind)
            c.check(ans == {'business': False, 'recorded': False, 'remembered': False}, f'{kind}: the answer {ans}')
            c.check(hist == [] and mem == [] and n_hist == 0 and n_mem == 0,
                    f'{kind}: written history={hist} memory={mem} (tables: {n_hist} / {n_mem} rows)')

    with Case(f'owner_app_in_business_writes_history_and_memory [{ch}]: owner / computed business — the owner_app row and the '
              'marked memory row, exactly what the canon before wrote') as c:
        for kind in BUSINESS:
            ans, hist, mem, _, _ = proxy_run(NOW[PROXY_F], ch, kind)
            c.check(ans == {'business': True, 'recorded': True, 'remembered': True}, f'{kind}: the answer {ans}')
            c.check(len(hist) == 1 and hist[0]['message_id'] == 'own-' + kind and hist[0]['source'] == 'owner_app'
                    and hist[0]['body'] == 'Ждём вас завтра в 15:00.', f'{kind}: history {hist}')
            c.check(len(mem) == 1 and mem[0].get('type') == 'ai' and mem[0].get('content') == 'Владелец: Ждём вас завтра в 15:00.'
                    and mem[0].get('additional_kwargs') == {'source': 'owner_app', 'message_id': 'own-' + kind},
                    f'{kind}: memory {mem}')
            old_ans, old_hist, old_mem, _, _ = proxy_run(OLD[PROXY_F], ch, kind)
            c.check(hist == old_hist and mem == old_mem, f'{kind}: differs from the canon before: history {hist} vs {old_hist}, '
                                                          f'memory {mem} vs {old_mem}')
            c.check({k: ans.get(k) for k in ('recorded', 'remembered')} == old_ans, f'{kind}: answer {ans} vs the canon {old_ans}')

    # -------------------------------------------------------------------------------------------- the panel gate
    with Case(f'suggest_refused_for_personal_and_undecided_counts_nothing [{ch}]: active / expired / unknown account, with and '
              'without a key — refused as personal_chat, not one counter row; session → personal_chat, legacy → '
              'generation_failed') as c:
        with PgSession(SETUP) as pg:
            seed_audience(pg, ch)
            pg.x(SUBSCRIBERS)
            for kind in NOT_BUSINESS:
                for app_user, key in (('u-active', 'aa01'), ('u-active', ''), ('u-expired', 'aa02'), ('u-nobody', 'aa03'),
                                      ('u-capped', 'aa04')):
                    prep = prep_json(ch, chats[kind], app_user, key)
                    row = gate(NOW[SUGGEST_F], pg, prep)
                    c.check(row.get('business') is False and row.get('allowed') is False,
                            f'{kind} / {app_user} / key {key!r}: the verdict row {row}')
                    gd = gate_decision(NOW[SUGGEST_F], prep, row)
                    c.check(gd.get('gateAllowed') is False and gd.get('gateReason') == 'personal_chat',
                            f'{kind} / {app_user}: Gate Decision {gd.get("gateAllowed")} / {gd.get("gateReason")}')
                    s, lg = build_response(NOW[SUGGEST_F], gd, 'session'), build_response(NOW[SUGGEST_F], gd, 'legacy')
                    c.check(s == {'v': 1, 'requestSeq': 7, 'suggestions': [], 'error': 'personal_chat'}, f'{kind}: session answer {s}')
                    c.check(lg == {'v': 1, 'requestSeq': 7, 'suggestions': [], 'error': 'generation_failed'}, f'{kind}: legacy answer {lg}')
            got = counters(pg)
            c.check(got == {'keys': 0, 'days': 0, 'n': 0, 'capped': 100}, f'counter rows written: {got}')

    # -------------------------------------------------------------------------------------------- the dashboard
    with Case(f'dashboard_skips_personal_in_classifier_and_fetch [{ch}]: only the business sessions reach the classifier, only '
              'business chats come back (verdicts and handoffs) — the business rows equal the canon\'s') as c:
        business = {chats[k] for k in BUSINESS} | {COLON_CHAT}
        (sessions, total, fetched), (old_sessions, old_total, old_fetched) = dash_run(ch)
        c.check(set(sessions) == business and total == len(business), f'classifier input: {sorted(sessions)} (total {total})')
        c.check(all(s['profile_id'] == pid and s['session_id'] == pid + ':' + chat for chat, s in sessions.items()),
                f'the session split: {[(s["session_id"], s["profile_id"], s["chat_id"]) for s in sessions.values()]}')
        leaked = [k for k in NOT_BUSINESS if any(k in (s.get('transcript') or '') for s in sessions.values())]
        c.check(not leaked, f'a non-business chat\'s memory reached the classifier: {leaked}')
        c.check(set(fetched) == business, f'«Сводка» rows: {sorted(fetched)}')
        c.check(set(old_sessions) == set(chats.values()) | {COLON_CHAT} and old_total == len(old_sessions),
                f'premise: the canon before read every chat: {sorted(old_sessions)} (total {old_total})')
        c.check(sessions == {k: v for k, v in old_sessions.items() if k in business},
                f'business sessions differ from the canon\'s: {sessions} vs {old_sessions}')
        c.check(fetched == {k: v for k, v in old_fetched.items() if k in business},
                f'business rows differ from the canon\'s: {fetched} vs {old_fetched}')

    with Case(f'suggest_business_unchanged [{ch}]: allowed / the same key again / a legacy call / expired / unknown / over the '
              'cap — the same verdicts and counters as the canon before') as c:
        for kind in BUSINESS:
            now_steps, old_steps = gate_run(NOW[SUGGEST_F], ch, kind), gate_run(OLD[SUGGEST_F], ch, kind)
            c.check([s[1] for s in now_steps] == [True] * len(GATE_STEPS), f'{kind}: business column {[s[1] for s in now_steps]}')
            want = [(True, None, {'keys': 1, 'days': 1, 'n': 1, 'capped': 100}),
                    (True, None, {'keys': 1, 'days': 1, 'n': 1, 'capped': 100}),
                    (True, None, {'keys': 1, 'days': 1, 'n': 2, 'capped': 100}),
                    (False, 'subscription_expired', {'keys': 1, 'days': 1, 'n': 2, 'capped': 100}),
                    (False, 'unknown_account', {'keys': 1, 'days': 1, 'n': 2, 'capped': 100}),
                    (False, 'daily_cap', {'keys': 2, 'days': 1, 'n': 2, 'capped': 101})]
            got = [(s[2]['gateAllowed'], s[2]['gateReason'], s[3]) for s in now_steps]
            c.check(got == want, f'{kind}: steps {got}')
            c.check([(s[0], s[2], s[3]) for s in now_steps] == [(s[0], s[2], s[3]) for s in old_steps],
                    f'{kind}: differs from the canon before: {now_steps} vs {old_steps}')
        gd = gate_decision(NOW[SUGGEST_F], prep_json(ch, chats['business_owner'], 'u-expired', 'x'),
                           {'business': True, 'known': True, 'status': 'expired', 'n': 0, 'allowed': False})
        c.check(build_response(NOW[SUGGEST_F], gd, 'session').get('error') == 'subscription', f'expired → {gd}')

with Case('suggest_gate_failure_refuses: a database error (an error item, or the input handed on) refuses — audience_unknown, '
          'generation_failed for every caller, never a panel') as c:
    prep = prep_json('WA', CH['WA']['chats']['business_owner'], 'u-active', 'f0001')
    for label, row in (('an error item', {'error': 'connection refused'}), ('the input handed on', dict(prep))):
        gd = gate_decision(NOW[SUGGEST_F], prep, row)
        c.check(gd.get('gateAllowed') is False and gd.get('gateReason') == 'audience_unknown', f'{label}: {gd}')
        for mode in ('session', 'legacy'):
            r = build_response(NOW[SUGGEST_F], gd, mode)
            c.check(r == {'v': 1, 'requestSeq': 7, 'suggestions': [], 'error': 'generation_failed'}, f'{label} / {mode}: {r}')

with Case('statements_pass_the_sql_guard: verify-sql-comments finds nothing in the changed statements') as c:
    for fname, name in ((PROXY_F, 'Record Owner Send'), (SUGGEST_F, 'Suggestion Gate'), (DASH_F, 'Find Changed Sessions'),
                        (DASH_F, 'Fetch Outcomes')):
        findings = VSC.check_query(node(NOW[fname], name)['parameters']['query'])
        c.check(findings == [], f'{name}: {findings}')

report()
