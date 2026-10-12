#!/usr/bin/env python3
"""T6 (Q1 B + V6-unknown, follow-ups 2026-10-11, F1F2-plan §4): the model must see WHO wrote a memory row and whether
its own earlier reply is known to have arrived.

The memory node hands the model only role + content (`additional_kwargs` never reach it — independent review
2026-10-11, pipeline-delivery-context.json). So an owner's reply (owner_app from the app, owner_phone from the phone)
read as the bot's OWN earlier answer (Q1: «Я не вижу подтверждённого времени»), and a reply whose delivery is unknown
read exactly like a delivered one (V6-unknown). Contract (plan §4.1–4.2):
  * owner rows reach the model as assistant turns «Владелец: <text>» (owner_app AND owner_phone);
  * the bot's reply with delivery unknown reaches it as «[доставка не подтверждена] <text>»;
  * a sent reply is byte for byte what the agent wrote; a refused one (send_failed) is not there at all;
  * client rows are unchanged;
  * the marks never reach the client: a reply the model opens with a mark goes out without it (Final Check), and the
    bot's own memory row is stored without it (Stamp Draft — tests/test_reply_memory.py);
  * the «Сводка» classifier labels rows by source and therefore reads the content WITHOUT the marks (no
    «Владелец: Владелец:», no «[доставка не подтверждена]» in a «Бот:» line).
T11d (owner decision 2026-10-12, evidence/Q1B3-measure.txt §6): the rule that explains the marks to the model is B plus
the re-ask guard («Клиент просто переспрашивает — это не новый заказ: …») right after «повтори её как есть», in every
copy the model reads — _core.md → the Create/Edit WA/TG maps → a Create-composed clone, and the templates'
##memory_marks## block → the system message the model receives.

Rows are written by the REAL writers — the proxy's Record Owner Send, the templates' Record Outgoing (owner_phone),
the chain Claim → Stamp Draft → Final Check → Acquire Send Lease → Settle → Record Send (reply_chain.Run) — on session
temp tables of the staging Postgres; they are read back with the memory class's own statement and turned into model
messages by the LangChain classes of the staging n8n install (tests/memory_context.js). Both channels.

    python3 Tools/n8n/tests/test_memory_context.py
"""
import base64
import contextlib
import copy
import io
import json
import os
import shutil
import subprocess
import tempfile

from harness import N8N, Case, PgSession, bind, code, expr, module, node, report, wf
from reply_chain import CHATS, SETUP, TEMPLATES, Run, _lit, history, msg, record_outgoing

OWNER = 'Владелец: '                         # the contract (plan §4.1) — the generator's constants must equal it
UNDELIVERED = '[доставка не подтверждена] '
PROXY = wf('crWappiProxy0001-Wappi_Proxy.json')
DASH = wf('2htWSV5IHO8E2CgB-Dashboard_Outcomes.json')
DRIVER = N8N / 'tests' / 'memory_context.js'
SETUP_DASH = SETUP + "\ncreate temp table conversation_outcomes (like public.conversation_outcomes including all);"
CHANNEL = {'WA': 'whatsapp', 'TG': 'telegram'}

ASK_1 = 'Можно сегодня забрать заказ?'
SENT_1 = 'Да, заказ готов.'
OWNER_APP = 'Ждём вас завтра в 15:00, спросите Асхата.'
OWNER_PHONE = 'Вход со двора, второй этаж.'
ASK_2 = 'Сколько стоит фильтр 90915-YZZE1?'
UNKNOWN_2 = 'Фильтр 90915-YZZE1 — 3200 тг.'
ASK_3 = 'А с доставкой?'
FAILED_3 = 'Доставка — 1000 тг.'
ASK_4 = 'Во сколько мне подойти?'


def record_owner_send(pg, ch, mid, text, exec_id='exec-proxy'):
    """The Wappi Proxy's Record Owner Send for an app send (op message.send) — its own query and parameters."""
    pid, chat = CHATS[ch]
    recipient = chat.replace('@c.us', '')
    named = {'Build Request': {'op': 'message.send', 'channel': CHANNEL[ch], 'profileId': pid,
                               'jsonBody': {'recipient': recipient, 'body': text}},
             'Wappi Call': {'statusCode': 200, 'body': {'status': 'done', 'message_id': mid}}}
    n = node(PROXY, 'Record Owner Send')
    params = expr(n['parameters']['options']['queryReplacement'], {}, named, exec_id)
    assert isinstance(params, list), params
    rows = pg.q(bind(n['parameters']['query'], params))
    return rows[0] if rows else {}


def stored_rows(pg, ch):
    """What PostgresChatMessageHistory.getMessages reads: SELECT message FROM <table> WHERE session_id = $1 ORDER BY id."""
    pid, chat = CHATS[ch]
    return [r['message'] for r in pg.q(f"SELECT message FROM n8n_chat_histories WHERE session_id = {_lit(pid + ':' + chat)} ORDER BY id")]


def model_view(cases):
    r = subprocess.run(['node', str(DRIVER)], input=json.dumps({'cases': cases}, ensure_ascii=False), text=True, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('memory_context.js failed: ' + r.stderr.strip()[-1500:])
    return json.loads(r.stdout)


def conversation(ch, pg):
    """sent reply → owner from the app → owner from the phone → reply with delivery unknown → reply refused (send_failed)."""
    T = TEMPLATES[ch]
    m1 = msg(ch, 'C1', 100, ASK_1)
    r1 = Run(T, pg, m1, 'exec-1')
    r1.claim(); r1.latest([m1]); r1.generate(SENT_1); r1.final([m1]); r1.lease(); r1.send_ok('sent-1')
    record_owner_send(pg, ch, 'own-app-1', OWNER_APP)
    record_outgoing(T, pg, ch, 'own-phone-1', OWNER_PHONE, 130, wh_type='outgoing_message_phone')
    m2 = msg(ch, 'C2', 140, ASK_2)
    r2 = Run(T, pg, m2, 'exec-2')
    r2.claim(); r2.latest([m2]); r2.generate(UNKNOWN_2); r2.final([m2]); r2.lease()
    r2.send({'error': {'message': 'socket hang up'}}, reconcile_snapshot=[m2])
    m3 = msg(ch, 'C3', 150, ASK_3)
    r3 = Run(T, pg, m3, 'exec-3')
    r3.claim(); r3.latest([m3]); r3.generate(FAILED_3); r3.final([m3]); r3.lease()
    r3.send({'statusCode': 500, 'body': {'status': 'error', 'detail': 'boom'}})
    return r1, r2, r3


def system_message(T):
    return node(T, 'AI Agent')['parameters']['options']['systemMessage']


with Case('T6 the marks are ONE contract: apply-history-handoff\'s constants equal the plan\'s strings') as c:
    try:
        hh = module('apply-history-handoff.py')
        c.check(getattr(hh, 'OWNER_MEMORY_PREFIX', None) == OWNER, f'OWNER_MEMORY_PREFIX={getattr(hh, "OWNER_MEMORY_PREFIX", None)!r}')
        c.check(getattr(hh, 'UNDELIVERED_MEMORY_PREFIX', None) == UNDELIVERED, f'UNDELIVERED_MEMORY_PREFIX={getattr(hh, "UNDELIVERED_MEMORY_PREFIX", None)!r}')
    except Exception as e:                    # noqa: BLE001
        c.check(False, f'cannot load apply-history-handoff.py: {e}')

VIEWS, OUTCOMES, HIST = {}, {}, {}
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'T6 [{ch}] the memory class reads the rows with the statement the test ran (fidelity of the model view)') as c:
        with PgSession(SETUP) as pg:
            runs = conversation(ch, pg)
            OUTCOMES[ch] = [r.outcome for r in runs]
            HIST[ch] = [(x['message_id'], x['source'], x['body']) for x in history(pg, ch)]
            rows = stored_rows(pg, ch)
        k = node(T, 'Chat Memory')['parameters']['contextWindowLength']
        VIEWS[ch] = model_view({ch: {'sessionId': pid + ':' + chat, 'tableName': 'n8n_chat_histories', 'rows': rows, 'k': k,
                                     'system': system_message(T), 'input': ASK_4}})[ch]
        sel = [s for s in VIEWS[ch]['statements'] if s['sql'].startswith('SELECT')]
        c.check(sel == [{'sql': 'SELECT message FROM n8n_chat_histories WHERE session_id = $1 ORDER BY id', 'params': [pid + ':' + chat]}],
                f'the class read memory with {sel}')
        c.check(OUTCOMES[ch] == ['sent', 'send_unknown', 'send_failed'], f'outcomes={OUTCOMES[ch]}')

    turns = VIEWS.get(ch, {}).get('modelMessages', [])[1:-1]        # between the system message and the new question
    assistant = [t['content'] for t in turns if t['role'] == 'assistant']

    with Case(f'T6 [{ch}] Q1 B: the owner\'s replies reach the model as «Владелец: …» (from the app AND from the phone)') as c:
        c.check(OWNER + OWNER_APP in assistant, f'owner_app turn missing or unmarked: assistant turns={assistant}')
        c.check(OWNER + OWNER_PHONE in assistant, f'owner_phone turn missing or unmarked: assistant turns={assistant}')
        c.check(OWNER_APP not in assistant and OWNER_PHONE not in assistant,
                f'an owner reply reaches the model as the bot\'s own turn (no mark): {assistant}')

    with Case(f'T6 [{ch}] V6-unknown: a reply with unknown delivery carries «[доставка не подтверждена]», a sent one is verbatim, a refused one is gone') as c:
        c.check(UNDELIVERED + UNKNOWN_2 in assistant and UNKNOWN_2 not in assistant,
                f'the unknown reply must be marked: assistant turns={assistant}')
        c.check(SENT_1 in assistant, f'the sent reply must reach the model byte for byte: {assistant}')
        c.check(not any(FAILED_3 in a for a in assistant), f'a reply the transport refused must not be in memory: {assistant}')

    with Case(f'T6 [{ch}] the whole conversation, in order: client rows unchanged, nothing else marked') as c:
        want = [('user', ASK_1), ('assistant', SENT_1), ('assistant', OWNER + OWNER_APP), ('assistant', OWNER + OWNER_PHONE),
                ('user', ASK_2), ('assistant', UNDELIVERED + UNKNOWN_2), ('user', ASK_3)]
        got = [(t['role'], t['content']) for t in turns]
        c.check(got == want, f'model turns={got}')

    with Case(f'T6 [{ch}] the one history (what the app shows) carries no marks: ai / owner bodies as sent') as c:
        bodies = {mid: (src, body) for mid, src, body in HIST.get(ch, [])}
        c.check(bodies.get('sent-1') == ('ai', SENT_1) and bodies.get('own-app-1') == ('owner_app', OWNER_APP)
                and bodies.get('own-phone-1') == ('owner_phone', OWNER_PHONE),
                f'history={HIST.get(ch)}')
        c.check(not any(OWNER.strip() in (b or '') or UNDELIVERED.strip() in (b or '') for _, _, b in HIST.get(ch, [])), f'a mark leaked into the history: {HIST.get(ch)}')


# ---------------------------------------------------------------------------------------------------------- Final Check
STRIP = [
    (OWNER + 'Ждём вас завтра в 15:00.', 'Ждём вас завтра в 15:00.'),
    (UNDELIVERED + 'Фильтр — 3200 тг.', 'Фильтр — 3200 тг.'),
    ('  ' + OWNER + UNDELIVERED + 'Фильтр — 3200 тг.', 'Фильтр — 3200 тг.'),
    ('владелец:Ждём вас.', 'Ждём вас.'),
    ('Владелец : Ждём вас.', 'Ждём вас.'),
    ('[Доставка не подтверждена]\nФильтр — 3200 тг.', 'Фильтр — 3200 тг.'),
]
KEEP = [
    'Владелец магазина — Асхат, он подтвердит заказ.',             # the word, no mark
    'Фильтр — 3200 тг. Владелец: Асхат.',                           # not leading
    'Доставка не подтверждена — уточню у владельца.',               # no brackets
    'Здравствуйте!\nВладелец: Асхат.',                              # a later line
]
for ch, T in TEMPLATES.items():
    with Case(f'T6 [{ch}] Final Check: a reply the model opens with a memory mark goes out WITHOUT it (send body, history, reconcile)') as c:
        for raw, want in STRIP:
            with PgSession(SETUP) as pg:
                m = msg(ch, 'F', 100, 'Во сколько подойти?')
                r = Run(T, pg, m, 'exec-F')
                r.claim(); r.latest([m]); r.generate(raw); out = r.final([m])
                c.check(out.get('text') == want and out.get('ok') is True, f'{raw!r}: Final Check text={out.get("text")!r} ok={out.get("ok")}')
                r.lease(); r.send_ok('sent-F')
                h = [(x['message_id'], x['source'], x['body']) for x in history(pg, ch)]
                c.check(('sent-F', 'ai', want) in h, f'{raw!r}: history={h}')
                bp = {p['name']: p['value'] for p in node(T, 'HTTP Request')['parameters']['bodyParameters']['parameters']}
                body = expr(bp['body'], {}, r.named, 'exec-F')     # the transport's `body` field, n8n's own engine
                c.check(body == want, f'{raw!r}: the client would receive {body!r}')

    with Case(f'T6 [{ch}] Final Check: a mark that does not OPEN the reply, or the bare word, is the reply\'s own text — kept') as c:
        for raw in KEEP:
            with PgSession(SETUP) as pg:
                m = msg(ch, 'K', 100, 'Кто владелец?')
                r = Run(T, pg, m, 'exec-K')
                r.claim(); r.latest([m]); r.generate(raw); out = r.final([m])
                c.check(out.get('text') == raw, f'{raw!r}: Final Check text={out.get("text")!r}')

    with Case(f'T6 [{ch}] Final Check: a reply that is ONLY a mark is no reply (generation_failed, nothing sent)') as c:
        with PgSession(SETUP) as pg:
            m = msg(ch, 'E', 100, 'Во сколько подойти?')
            r = Run(T, pg, m, 'exec-E')
            r.claim(); r.latest([m]); r.generate(OWNER.strip()); out = r.final([m])
            c.check(out.get('ok') is False and out.get('outcome') == 'generation_failed' and r.outcome == 'generation_failed',
                    f'final={out} outcome={r.outcome}')


# ---------------------------------------------------------------------------------------------------------- «Сводка»
for ch, T in TEMPLATES.items():
    pid, chat = CHATS[ch]
    with Case(f'T6 [{ch}] «Сводка» transcript: one «Владелец:» per owner row, the bot\'s lines without «[доставка не подтверждена]»') as c:
        with PgSession(SETUP_DASH) as pg:
            conversation(ch, pg)
            q = node(DASH, 'Find Changed Sessions')['parameters']['query'].replace('public.', '')
            b64 = base64.b64encode(json.dumps([pid]).encode()).decode()
            res = pg.q(bind(q, [b64]))
        sessions = (res[0]['sessions'] if res else None) or []
        tr = next((s['transcript'] for s in sessions if s['chat_id'] == chat), '') or ''
        lines = tr.split('\n')
        c.check('Владелец: ' + OWNER_APP in lines and 'Владелец: ' + OWNER_PHONE in lines, f'owner lines missing: {lines}')
        c.check('Владелец: Владелец:' not in tr, f'double owner label: {lines}')
        c.check('Бот: ' + UNKNOWN_2 in lines and UNDELIVERED.strip() not in tr, f'the classifier must read the bot\'s text without the mark: {lines}')
        c.check('Бот: ' + SENT_1 in lines and 'Клиент: ' + ASK_1 in lines, f'transcript={lines}')


# ---------------------------------------------------------------------------------------------------------- tripwires
# The structural verifiers hold the same contract when nobody runs these tests: they accept the canonical exports and
# reject every writer that lost its mark (verify-history-handoff.py) and a Final Check without the cut
# (verify-reply-pipeline.py) — on BOTH templates.
WFDIR = N8N / 'workflows'


def _mutated(fname, fn):
    tmp = tempfile.mkdtemp(prefix='t6-verify-')
    for f in os.listdir(WFDIR):
        if f.endswith('.json'):
            shutil.copy(WFDIR / f, os.path.join(tmp, f))
    w = json.loads((WFDIR / fname).read_text(encoding='utf-8'))
    before = copy.deepcopy(w)
    fn(w)
    assert w != before, f'mutation of {fname} did not apply'
    with open(os.path.join(tmp, fname), 'w', encoding='utf-8') as fh:
        json.dump(w, fh, ensure_ascii=False)
    return tmp


def _accepts(mod_file, tmp, call):
    m = module(mod_file)
    m.WF = tmp
    out = io.StringIO()
    try:
        with contextlib.redirect_stdout(out):
            call(m)
        return True, ''
    except SystemExit:
        return False, out.getvalue().strip().splitlines()[-1:]


def _node_of(w, name):
    return next(n for n in w['nodes'] if n['name'] == name)


def _q(name, old, new):
    def fn(w):
        n = _node_of(w, name)
        n['parameters']['query'] = n['parameters']['query'].replace(old, new)
    return fn


HH = module('apply-history-handoff.py')
BOT_FILES = {'WA': '4wYitz5ek30SVNlT-WhatsApp_Bot.json', 'TG': '4VN3gsFaC2HUYmcc-Telegram_Bot.json'}
with Case('T6 tripwires: the verifiers accept the canonical exports and reject every writer without its mark') as c:
    all_hh = lambda m: (m.verify_bot(BOT_FILES['WA']), m.verify_bot(BOT_FILES['TG']), m.verify_integrations())
    ok, why = _accepts('verify-history-handoff.py', str(WFDIR), all_hh)
    c.check(ok, f'canonical exports rejected by verify-history-handoff: {why}')
    ok, why = _accepts('verify-reply-pipeline.py', str(WFDIR), lambda m: (m.verify(BOT_FILES['WA']), m.verify(BOT_FILES['TG'])))
    c.check(ok, f'canonical exports rejected by verify-reply-pipeline: {why}')
    owner_sql = HH.OWNER_CONTENT_SQL % '$4'
    for ch, f in BOT_FILES.items():
        bad_hh = {
            'Record Outgoing writes the owner text without the mark': _q('Record Outgoing', owner_sql, '$4'),
            'Stamp Draft keeps the marks the model wrote': _q('Stamp Draft', "to_jsonb(regexp_replace(message->>'content', " + HH.MARKS_LEAD_SQL + ", ''))",
                                                              "to_jsonb(message->>'content')"),
            'Record Send leaves an unknown reply unmarked': _q('Record Send', "\n      || jsonb_build_object('content', " + HH.RECORD_SEND_CONTENT_SQL + ")", ''),
            'the template prompt without the memory-marks block': lambda w: _node_of(w, 'AI Agent')['parameters']['options'].__setitem__(
                'systemMessage', _node_of(w, 'AI Agent')['parameters']['options']['systemMessage'].replace(HH.MEMORY_MARKS_RULE, '')),
        }
        for label, fn in bad_hh.items():
            tmp = _mutated(f, fn)
            try:
                ok, why = _accepts('verify-history-handoff.py', tmp, lambda m: m.verify_bot(f))
                c.check(not ok, f'[{ch}] verify-history-handoff accepted: {label}')
            finally:
                shutil.rmtree(tmp, ignore_errors=True)
        cut = 'text = text.replace(new RegExp(' + json.dumps(HH.MEMORY_MARKS_LEAD_RE, ensure_ascii=False) + "), '');"
        tmp = _mutated(f, lambda w: _node_of(w, 'Final Check')['parameters'].__setitem__(
            'jsCode', _node_of(w, 'Final Check')['parameters']['jsCode'].replace(cut, '')))
        try:
            ok, why = _accepts('verify-reply-pipeline.py', tmp, lambda m: m.verify(f))
            c.check(not ok, f'[{ch}] verify-reply-pipeline accepted a Final Check without the cut')
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
    for label, fname, fn in (
            ('the proxy writes the owner_app text without the mark', 'crWappiProxy0001-Wappi_Proxy.json', _q('Record Owner Send', owner_sql, '$4')),
            ('the «Сводка» transcript keeps the marks', '2htWSV5IHO8E2CgB-Dashboard_Outcomes.json', _q('Find Changed Sessions', HH.CONTENT_NEW, HH.CONTENT_OLD)),
            ('a Create map without the rule', 'XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json',
             lambda w: _node_of(w, 'Vertical Prompt')['parameters'].__setitem__(
                 'jsCode', _node_of(w, 'Vertical Prompt')['parameters']['jsCode'].replace(' ' + json.dumps(HH.MEMORY_MARKS_TEXT, ensure_ascii=False)[1:-1], '', 1)))):
        tmp = _mutated(fname, fn)
        try:
            ok, why = _accepts('verify-history-handoff.py', tmp, lambda m: m.verify_integrations())
            c.check(not ok, f'verify-history-handoff accepted: {label}')
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


# ---------------------------------------------------------------------------------------------------------- T11d
# Owner decision 2026-10-12 (third, after T11c — evidence/Q1B3-measure.txt §6): ship the T11c candidate. The memory-marks
# rule of B gains ONE sentence, the re-ask guard, right after «повтори её как есть» (the sentence it qualifies — lesson c1,
# evidence/Q1c-measure.txt); T11b's anti-echo sentence stays OUT. The rule text is the contract (spelled out here, like
# OWNER / UNDELIVERED above), and every copy the model can read must carry it exactly once:
#   * the generator's constant (apply-history-handoff.MEMORY_MARKS_TEXT);
#   * _core.md «Память» → every vertical of the Create/Edit WA/TG maps (inject-prompts.py) → the system message a
#     Create-composed clone carries (staging/vertical-bots.py composes it the way Create's Set Fields does);
#   * the templates' ##memory_marks## block (bots with an empty / legacy business type) → the system message the model
#     receives, as the staging n8n's LangChain classes hand it over (the memory_context.js view of the T6 cases above).
RULE_HEAD = ('Реплики, которые начинаются с «Владелец:», написал сам владелец: названные в них день, время, место или человек — '
             'подтверждённая договорённость; клиент переспрашивает — повтори её как есть.')
REASK_GUARD = 'Клиент просто переспрашивает — это не новый заказ: ничего заново не оформляй и владельцу не передавай.'
RULE_V6 = ('Твой прежний ответ с пометкой «[доставка не подтверждена]» мог не дойти до клиента: не ссылайся на него как на уже '
           'прочитанный, а если клиент переспрашивает или пишет, что ответа не было, ответь заново по существу. '
           'Сами эти пометки в ответах не пиши.')
RULE = RULE_HEAD + ' ' + REASK_GUARD + ' ' + RULE_V6
RULE_B = RULE_HEAD + ' ' + RULE_V6                       # T6 canon until T11d: no guard
ANTI_ECHO = 'Время, которое назвал сам клиент, договорённостью не становится, пока бизнес не написал это время сам.'  # T11b, not shipped


def rule_findings(texts):
    """{label: text the model reads} → what is wrong with the memory-marks rule in it (empty = the owner's decision)."""
    bad = []
    for label, text in texts.items():
        if text.count(RULE) != 1:
            bad.append(f'{label}: the rule with the re-ask guard occurs {text.count(RULE)}× (want exactly once)')
        if RULE_B in text:
            bad.append(f'{label}: carries the rule WITHOUT the re-ask guard (T6 text)')
        if text.count(REASK_GUARD) != 1:
            bad.append(f'{label}: the re-ask guard occurs {text.count(REASK_GUARD)}×')
        if ANTI_ECHO in text:
            bad.append(f'{label}: carries T11b\'s anti-echo sentence (not part of the decision)')
    return bad


IP = module('inject-prompts.py')
MAPS = ('XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json', 'Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json',
        '3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json', 'TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json')


def map_texts(fname, w=None):
    prompts = IP.prompts_of_workflow(w if w is not None else wf(fname)) or {}
    return {f'{fname}/{v}': prompts.get(v, '') for v in IP.VERTICALS}


with Case('T11d the rule is the owner\'s decision: B + the re-ask guard right after «повтори её как есть», no anti-echo') as c:
    c.check(HH.MEMORY_MARKS_TEXT == RULE, f'MEMORY_MARKS_TEXT={HH.MEMORY_MARKS_TEXT!r}')
    c.check(HH.MEMORY_MARKS_RULE == '\n\n##memory_marks##\n' + RULE, 'the ##memory_marks## block must carry the same text')
    core = (N8N / 'prompts' / '_core.md').read_text(encoding='utf-8')
    para = [line for line in core.splitlines() if line.startswith('Память.')]
    c.check(len(para) == 1 and para[0].endswith(' ' + RULE), f'_core.md «Память» paragraph={para}')
    c.check(not rule_findings({'_core.md': core}), rule_findings({'_core.md': core}))

with Case('T11d every Create/Edit map (WA + TG, all 6 verticals) and every Create-composed clone carries the decided rule once') as c:
    for fname in MAPS:
        texts = map_texts(fname)
        c.check(all(texts.values()), f'{fname}: verticals missing from the PROMPTS map: {[k for k, v in texts.items() if not v]}')
        c.check(not rule_findings(texts), rule_findings(texts))
    VB = module('staging/vertical-bots.py')
    for v in IP.VERTICALS:
        composed, _ = VB.compose_prompt(v, 'worktree')
        c.check(not rule_findings({f'Create-composed {v}': composed}), rule_findings({f'Create-composed {v}': composed}))

for ch, T in TEMPLATES.items():
    with Case(f'T11d [{ch}] the template prompt ends with the decided rule, and that is the system message the model receives') as c:
        sm = system_message(T)
        c.check(sm.endswith('\n\n##memory_marks##\n' + RULE) and sm.count('##memory_marks##') == 1, f'template prompt tail={sm[-400:]!r}')
        c.check(not rule_findings({f'{ch} template': sm}), rule_findings({f'{ch} template': sm}))
        seen = (VIEWS.get(ch, {}).get('modelMessages') or [{}])[0]     # the converter sends it as the developer turn
        c.check(seen.get('role') in ('system', 'developer') and str(seen.get('content', '')).rstrip().endswith(RULE)
                and not rule_findings({f'{ch} model system message': str(seen.get('content', ''))}),
                f'the model\'s system message: role={seen.get("role")!r} tail={str(seen.get("content", ""))[-300:]!r}')

with Case('T11d negative: the check rejects the T6 rule, a missing guard, a doubled rule and T11b\'s anti-echo — and a mutated map') as c:
    c.check(rule_findings({'T6 rule': 'Память. … ' + RULE_B}), 'the T6 rule (no guard) must be rejected')
    c.check(rule_findings({'guard after V6': RULE_B + ' ' + REASK_GUARD}), 'the guard detached from «как есть» must be rejected')
    c.check(rule_findings({'twice': RULE + ' ' + RULE}), 'a doubled rule must be rejected')
    c.check(rule_findings({'T11b': RULE_HEAD + ' ' + ANTI_ECHO + ' ' + REASK_GUARD + ' ' + RULE_V6}), 'T11b\'s text must be rejected')
    c.check(not rule_findings({'decided': 'Память. … ' + RULE}), 'the decided rule must be accepted')
    w = wf(MAPS[1])
    n = _node_of(w, 'Vertical Prompt')
    js = n['parameters']['jsCode']
    n['parameters']['jsCode'] = js.replace(json.dumps(REASK_GUARD + ' ', ensure_ascii=False)[1:-1], '', 1)
    c.check(n['parameters']['jsCode'] != js, 'the mutation (strip the guard from one vertical) did not apply — the canon map has no guard')
    bad = rule_findings(map_texts(MAPS[1], w))
    c.check(len(bad) >= 1 and all('/' in b for b in bad), f'a Create TG map with one vertical stripped of the guard must be rejected: {bad}')

report()
