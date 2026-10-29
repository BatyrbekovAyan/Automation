#!/usr/bin/env python3
"""Drives ONE bot-template run through the REAL reply pipeline, step by step, so a test can interleave several runs
of the same chat exactly as parallel n8n executions interleave them (iteration 2026-09-27, R6/R7/R9/R10).

Every step executes the node the canonical template holds — Code bodies through node (harness.code), SQL through
the node's own queryReplacement expression evaluated by n8n's engine (harness.expr) and bound into the node's own
query — against the session temp tables of a harness.PgSession. Nothing here re-states node logic; the only
synthetic parts are the transport (a snapshot a test hands in as `messages/get`, a send response) and the AI Agent
(its output text, and the two memory rows the Chat Memory node writes at the end of the agent's run).

    with PgSession(SETUP_BUSINESS) as pg:
        a = Run(TEMPLATES['WA'], pg, msg('A', 100), 'exec-A')
        a.mode(); a.claim(); a.latest(snapshot); a.generate('…'); a.final(snapshot); a.lease(); a.send_ok()

«Личные и рабочие чаты» (2026-10-25): the templates answer, record and send only in a BUSINESS chat — mode() follows the
audience gate the templates carry (Read Reply Mode → Audience Known? → Business? | Decide Audience → Decided Business?), and
the lease re-reads the decision. A test of a client's chat runs on SETUP_BUSINESS (the owner made both test chats business
chats) or seeds its own chats with business(); a chat without a decision goes through the REAL Decide Audience sub-workflow
(tests/graph_walk.py) with the Wappi stub handed to mode().
"""
import graph_walk as gw
from harness import code, expr, bind, has_node, node, wf

TEMPLATES = {'WA': wf('4wYitz5ek30SVNlT-WhatsApp_Bot.json'), 'TG': wf('4VN3gsFaC2HUYmcc-Telegram_Bot.json')}
CHATS = {'WA': ('wa-prof-rc', '77001112233@c.us'), 'TG': ('tg-prof-rc', '5550001112')}
DECIDE_AUDIENCE = {'crAudienceDec001': wf('crAudienceDec001-Decide_Audience.json')}

SETUP = """create temp table reply_events (like public.reply_events including all);
create temp table chat_reply_state (like public.chat_reply_state including all);
create temp table chat_history_events (like public.chat_history_events including all);
create temp table n8n_chat_histories (like public.n8n_chat_histories including all);
create temp table reply_mode_flags (like public.reply_mode_flags including all);
create temp table chat_audience (like public.chat_audience including all);
create temp table audience_connections (like public.audience_connections including all);"""


def _lit(v):
    return "'" + str(v).replace("'", "''") + "'"


def _business_row(ch, chat=None):
    pid, c = CHATS[ch]
    return ("insert into chat_audience (profile_id, chat_id, kind, source, reason, version) values "
            f"({_lit(pid)}, {_lit(chat or c)}, 'business', 'owner', 'owner', 1) on conflict do nothing;")


def business(pg, ch, chat=None):
    """«Личные и рабочие чаты» (2026-10-25): the owner made channel `ch`'s test chat (or `chat`) a BUSINESS chat — an owner
    decision, which always holds. The writers and readers that pass only a business chat (audience.BUSINESS_SQL) need it."""
    pg.x(_business_row(ch, chat))


# SETUP with both test chats made business chats by the owner: what every test of the reply to a CLIENT runs on.
SETUP_BUSINESS = SETUP + '\n' + '\n'.join(_business_row(ch) for ch in CHATS)


def msg(ch, mid, t, body=None, from_me=False, chat=None, mtype='chat'):
    """A Wappi row / webhook message of channel `ch`'s test chat (t=None → the webhook carries no time)."""
    pid, c = CHATS[ch]
    c = chat or c
    m = {'id': mid, 'chatId': c, 'from': c if not from_me else 'me', 'profile_id': pid, 'body': body if body is not None else f'сообщение {mid}',
         'type': mtype, 'fromMe': from_me, 'senderName': 'owner' if from_me else 'client'}
    if t is not None:
        m['time'] = t
    return m


OWN_CODE = '100000000000001@lid'        # the owner's own WhatsApp @lid code


def under_code(m, code, own=OWN_CODE):
    """A messages/get row as Wappi lists it for a WhatsApp chat kept under its @lid code
    (docs/audits/2026-10-25-personal-chats/live-checks.md): chatId is the code, the client's `from` is the code, the owner's
    `from` his own code — while the webhook of that chat names it by its number and carries the code in whatsapp_chat_id
    (a row never carries that field)."""
    r = dict(m, chatId=code)
    r['from'] = own if r.get('fromMe') else code
    r.pop('whatsapp_chat_id', None)
    return r


def _cond(w, name, j, named=None):
    return bool(expr(node(w, name)['parameters']['conditions']['conditions'][0]['leftValue'], j, named))


def _sql(w, pg, name, json_, named, exec_id, n8n_types=False):
    """The node's statement bound from its own queryReplacement. `n8n_types`: the row as n8n's Postgres node hands it on —
    bigint and numeric columns as TEXT (node-pg's default; graph_walk.typed_q) — for a row a later node maps by type."""
    n = node(w, name)
    params = expr(n['parameters']['options']['queryReplacement'], json_, named, exec_id)
    if isinstance(params, str):      # the legacy comma form (Read Reply Mode): the node splits it, empty entries dropped
        params = [p.strip() for p in params.split(',') if p]
    assert isinstance(params, list), f'{name}: queryReplacement did not evaluate to a list: {params!r}'
    sql = bind(n['parameters']['query'], params)
    rows = gw.typed_q(pg, sql) if n8n_types else pg.q(sql)
    return rows[0] if rows else {}


class Run:
    """One execution of the bot template for one webhook message. `outcome` is set once the run ends."""

    def __init__(self, w, pg, m, exec_id):
        self.w, self.pg, self.m, self.exec_id = w, pg, m, exec_id
        self.wh = {'body': {'messages': [m]}, 'webhookUrl': 'https://n8n.example/webhook/' + m['profile_id']}
        self.named = {'Webhook': self.wh}
        self.outcome = None
        self.text = None
        self.cancel = None
        self.agent_ran = False      # False on the media branch: $('AI Agent') then THROWS in n8n (unexecuted node)

    # --- Read Reply Mode → [the audience gate] → Suppressed? → [Record Suppressed → Outcome] ---------------------------
    def mode(self, fake=None):
        """The gates at message entry. «Личные и рабочие чаты»: Read Reply Mode's row carries the chat's decision — a
        personal chat ends at the Outcome stamp (Business? false); a chat without one goes to Decide Audience: the REAL
        sub-workflow (crAudienceDec001, walked by graph_walk with `fake` as Wappi, on the inputs the template maps and
        n8n type-checks), and only its business answer goes on (Decided Business?). A suppressed run («Вместе», a handoff
        pause, outside the bot's schedule) ends at Record Suppressed, then the Outcome stamp (its real Code body), and is
        never claimed; otherwise the test goes on with claim()."""
        row = _sql(self.w, self.pg, 'Read Reply Mode', {}, self.named, self.exec_id, n8n_types=True)
        self.named['Read Reply Mode'] = row
        gate = row
        if has_node(self.w, 'Audience Known?'):
            if _cond(self.w, 'Audience Known?', row, self.named):
                if not _cond(self.w, 'Business?', row, self.named):
                    return self._entry_stamp('Business?', row)
            else:
                gate = self._decide(row, fake)
                self.named['Decide Audience'] = gate
                if not _cond(self.w, 'Decided Business?', gate, self.named):
                    return self._entry_stamp('Decided Business?', gate)
        if _cond(self.w, 'Suppressed?', gate, self.named):
            self.suppressed_row = _sql(self.w, self.pg, 'Record Suppressed', gate, self.named, self.exec_id)
            self.named['Record Suppressed'] = self.suppressed_row
            self._entry_stamp('Record Suppressed', self.suppressed_row)
        return row

    def _entry_stamp(self, prev, item):
        self.stamp = code(self.w, 'Outcome', {'json': item}, self.named, prev_node=prev)[0]['json']
        self.outcome = self.stamp.get('outcome')
        return self.named['Read Reply Mode']

    def _decide(self, row, fake):
        """What the template's Decide Audience node hands on: n8n maps and type-checks its inputs first — a failure there
        is thrown outside the node's runner and it (continuing on error) hands its INPUT on, Read Reply Mode's row; then
        the sub-workflow's answer (its last node's item), or an {error} item when the sub-workflow failed."""
        n = node(self.w, 'Decide Audience')
        nodes = {k: {'all': [v], 'pair': {'json': v}} for k, v in self.named.items()}
        merged, err = gw.execute_inputs(n, row, nodes)
        self.decide_error = err
        if err is not None:
            return dict(row)
        sub = DECIDE_AUDIENCE[n['parameters']['workflowId']['value']]
        trigger = next(x for x in sub['nodes'] if x['type'] == gw.TRIGGER)
        r = gw.walk_graph(self.pg, sub, trigger['name'], items=[{'json': merged}], fake=fake or _no_wappi)
        self.decide_run = r
        if r.halt:
            return {'error': r.halt}
        out = (r.outputs.get(r.trail[-1]) or [[]])[0] if r.trail else []
        return out[0]['json'] if out else {}

    # --- claim ---------------------------------------------------------------------------
    def claim(self):
        self.claim_row = _sql(self.w, self.pg, 'Claim Event', {}, self.named, self.exec_id)
        self.named['Claim Event'] = self.claim_row
        if not _cond(self.w, 'Claimed?', self.claim_row):
            self.outcome = 'duplicate'
        return self.claim_row

    # --- Fetch Recent → Latest+Combine → Is Latest? ------------------------------------------
    def latest(self, snapshot):
        out = code(self.w, 'Latest+Combine', {'json': {'status': 'done', 'messages': [dict(x) for x in snapshot]}}, self.named)[0]['json']
        self.named['Latest+Combine'] = out
        self.combined = out.get('combinedText')
        if _cond(self.w, 'Is Latest?', out):
            self._cancel('Is Latest?', out)
        return out

    # --- the AI Agent (synthetic output) + the Chat Memory node's own two rows --------------------
    def generate(self, text=None, write_memory=True, stamp=True):
        human = self.combined if self.combined is not None else self.m.get('body', '')
        self.ai_text = text if text is not None else 'Ответ на: ' + str(human).replace('\n', ' / ')
        self.named['AI Agent'] = {'output': self.ai_text}
        self.named['Retrieve Answer'] = {'text': self.ai_text}
        self.agent_ran = True
        if write_memory:
            sid = self.m['profile_id'] + ':' + self.m['from']
            for typ, content in (('human', human), ('ai', self.ai_text)):
                self.pg.x("insert into n8n_chat_histories (session_id, message) values ("
                          f"{_lit(sid)}, jsonb_build_object('type', {_lit(typ)}, 'content', {_lit(content)}, "
                          "'additional_kwargs', '{}'::jsonb, 'response_metadata', '{}'::jsonb));")
        if stamp:                       # stamp=False: the Stamp Draft node failed (continue on error) or runs later
            self.stamp()
        return self.ai_text

    # --- Generated? → Stamp Draft (V6): the agent's memory row is tied to this execution ---------------------
    def stamp(self):
        if has_node(self.w, 'Stamp Draft'):
            self.stamp_row = _sql(self.w, self.pg, 'Stamp Draft', {'output': self.ai_text}, self.named, self.exec_id)
            self.named['Stamp Draft'] = self.stamp_row
        return getattr(self, 'stamp_row', None)

    def ask_for_text(self, text):
        """The media branch: Ask to Send Text instead of the agent (no memory rows)."""
        self.named['Ask to Send Text'] = {'text': text}

    # --- Final Fetch (or, after the lease asked for it, Recheck Fetch) → Final Check → Final OK? -------------
    def final(self, snapshot):
        out = code(self.w, 'Final Check', {'json': {'status': 'done', 'messages': [dict(x) for x in snapshot]}}, self.named,
                   strict=not self.agent_ran)[0]['json']
        self.named['Final Check'] = out
        self.text = out.get('text')
        self.recheck_pending = False
        if not _cond(self.w, 'Final OK?', out):
            self._cancel('Final OK?', out)
        return out

    # --- Acquire Send Lease → [Peer Recheck? → Wait Peer Recheck → Recheck Fetch → Final Check] → Send Allowed? ---
    def lease(self):
        row = _sql(self.w, self.pg, 'Acquire Send Lease', self.named['Final Check'], self.named, self.exec_id)
        self.lease_row = row
        self.leases = getattr(self, 'leases', 0) + 1
        self.named['Acquire Send Lease'] = row          # Final Check's second pass reads it ($('Acquire Send Lease'))
        # V5: the lease asks for ONE re-read of the chat — the test hands the fresh snapshot to final() and calls
        # lease() again (a template without the node never asks)
        if has_node(self.w, 'Peer Recheck?') and _cond(self.w, 'Peer Recheck?', row):
            self.recheck_pending = True
            return row
        if not _cond(self.w, 'Send Allowed?', row):
            self._cancel('Send Allowed?', row)
        return row

    # --- HTTP Request → Settle Send → [Send Unknown? → Reconcile] → Record Send ------------------------
    def send(self, http_response, reconcile_snapshot=None, reconcile_uuid=None):
        assert not getattr(self, 'recheck_pending', False), 'the lease asked for a re-read (Peer Recheck?): call final() + lease() again'
        st = code(self.w, 'Settle Send', {'json': http_response}, self.named)[0]['json']
        self.named['Settle Send'] = st
        final = st
        if _cond(self.w, 'Send Unknown?', st):
            fetch = {'status': 'done', 'messages': [dict(x) for x in (reconcile_snapshot or [])]}
            if reconcile_uuid is not None:
                fetch['uuid'] = reconcile_uuid
            self.named['Reconcile Fetch'] = fetch
            item = fetch
            if has_node(self.w, 'Owner Row?'):            # V7: the one history's sources for the window's rows
                item = _sql(self.w, self.pg, 'Owner Row?', fetch, self.named, self.exec_id)
                self.named['Owner Row?'] = item
            rc = code(self.w, 'Reconcile Check', {'json': item}, self.named)[0]['json']
            self.named['Reconcile Check'] = rc
            final = rc
        self.record = _sql(self.w, self.pg, 'Record Send', final, self.named, self.exec_id)
        self.outcome = self.record.get('outcome')
        return self.record

    def send_ok(self, transport_id=None):
        return self.send({'statusCode': 200, 'body': {'status': 'done', 'message_id': transport_id or ('sent-' + self.m['id'])}})

    # --- Cancel Label → Record Cancel --------------------------------------------------------
    def _cancel(self, prev, j):
        # strict = an unexecuted node throws, as n8n does; the label's own try/catch decides what that means
        lab = code(self.w, 'Cancel Label', {'json': j}, self.named, prev_node=prev, strict=not self.agent_ran)[0]['json']
        self.named['Cancel Label'] = lab
        self.cancel = _sql(self.w, self.pg, 'Record Cancel', lab, self.named, self.exec_id)
        self.outcome = self.cancel.get('outcome')

    @property
    def done(self):
        return self.outcome is not None


def _no_wappi(call):
    raise AssertionError(f'Decide Audience asked Wappi ({call["node"]} {call["url"]}): seed the chat\'s decision '
                         '(SETUP_BUSINESS / business()) or hand mode() a Wappi stub (fake=)')


def record_outgoing(w, pg, ch, mid, body, t, wh_type='outgoing_message_api', exec_id='exec-echo'):
    """The template's own outgoing branch (Record Outgoing) for a Wappi outgoing webhook — e.g. the transport's echo
    of the bot's send (outgoing_message_api) or the owner writing from the phone (outgoing_message_phone)."""
    m = dict(msg(ch, mid, t, body, from_me=True), wh_type=wh_type, to=CHATS[ch][1])
    m['chatId'] = CHATS[ch][1]
    wh = {'body': {'messages': [m]}, 'webhookUrl': 'https://n8n.example/webhook/' + m['profile_id']}
    return _sql(w, pg, 'Record Outgoing', {}, {'Webhook': wh}, exec_id)


def history(pg, ch):
    pid, c = CHATS[ch]
    return pg.q(f"select message_id, source, body from chat_history_events where profile_id = {_lit(pid)} and chat_id = {_lit(c)} order by id")


def ledger(pg, ch):
    pid, c = CHATS[ch]
    return {r['message_id']: r['state'] for r in pg.q(f"select message_id, state from reply_events where profile_id = {_lit(pid)} and chat_id = {_lit(c)} order by message_id")}


def memory(pg, ch):
    pid, c = CHATS[ch]
    return pg.q(f"select id, message->>'type' as type, message->>'content' as content, message->'additional_kwargs'->>'source' as source, "
                "message->'additional_kwargs'->>'execution_id' as execution_id, message->'additional_kwargs'->>'event_id' as event_id, "
                "message->'additional_kwargs'->>'delivery' as delivery, message->'additional_kwargs'->>'transport_message_id' as transport_message_id "
                f"from n8n_chat_histories where session_id = {_lit(pid + ':' + c)} order by id")
