#!/usr/bin/env python3
"""Drives ONE bot-template run through the REAL reply pipeline, step by step, so a test can interleave several runs
of the same chat exactly as parallel n8n executions interleave them (iteration 2026-09-27, R6/R7/R9/R10).

Every step executes the node the canonical template holds — Code bodies through node (harness.code), SQL through
the node's own queryReplacement expression evaluated by n8n's engine (harness.expr) and bound into the node's own
query — against the session temp tables of a harness.PgSession. Nothing here re-states node logic; the only
synthetic parts are the transport (a snapshot a test hands in as `messages/get`, a send response) and the AI Agent
(its output text, and the two memory rows the Chat Memory node writes at the end of the agent's run).

    with PgSession(SETUP) as pg:
        a = Run(TEMPLATES['WA'], pg, msg('A', 100), 'exec-A')
        a.claim(); a.latest(snapshot); a.generate('…'); a.final(snapshot); a.lease(); a.send_ok()
"""
from harness import code, expr, bind, has_node, node, wf

TEMPLATES = {'WA': wf('4wYitz5ek30SVNlT-WhatsApp_Bot.json'), 'TG': wf('4VN3gsFaC2HUYmcc-Telegram_Bot.json')}
CHATS = {'WA': ('wa-prof-rc', '77001112233@c.us'), 'TG': ('tg-prof-rc', '5550001112')}

SETUP = """create temp table reply_events (like public.reply_events including all);
create temp table chat_reply_state (like public.chat_reply_state including all);
create temp table chat_history_events (like public.chat_history_events including all);
create temp table n8n_chat_histories (like public.n8n_chat_histories including all);
create temp table reply_mode_flags (like public.reply_mode_flags including all);"""


def msg(ch, mid, t, body=None, from_me=False, chat=None, mtype='chat'):
    """A Wappi row / webhook message of channel `ch`'s test chat (t=None → the webhook carries no time)."""
    pid, c = CHATS[ch]
    c = chat or c
    m = {'id': mid, 'chatId': c, 'from': c if not from_me else 'me', 'profile_id': pid, 'body': body if body is not None else f'сообщение {mid}',
         'type': mtype, 'fromMe': from_me, 'senderName': 'owner' if from_me else 'client'}
    if t is not None:
        m['time'] = t
    return m


def _cond(w, name, j):
    return bool(expr(node(w, name)['parameters']['conditions']['conditions'][0]['leftValue'], j))


def _sql(w, pg, name, json_, named, exec_id):
    n = node(w, name)
    params = expr(n['parameters']['options']['queryReplacement'], json_, named, exec_id)
    if isinstance(params, str):      # the legacy comma form (Read Reply Mode): the node splits it, empty entries dropped
        params = [p.strip() for p in params.split(',') if p]
    assert isinstance(params, list), f'{name}: queryReplacement did not evaluate to a list: {params!r}'
    rows = pg.q(bind(n['parameters']['query'], params))
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

    # --- Read Reply Mode → Suppressed? → [Record Suppressed → Outcome] (B1: the schedule is one more term of the read) ---
    def mode(self):
        """The mode gate at message entry. A suppressed run («Вместе», a handoff pause, outside the bot's schedule) ends
        here — Record Suppressed, then the Outcome stamp (its real Code body) — and is never claimed; otherwise the
        test goes on with claim()."""
        row = _sql(self.w, self.pg, 'Read Reply Mode', {}, self.named, self.exec_id)
        self.named['Read Reply Mode'] = row
        if _cond(self.w, 'Suppressed?', row):
            self.suppressed_row = _sql(self.w, self.pg, 'Record Suppressed', row, self.named, self.exec_id)
            self.named['Record Suppressed'] = self.suppressed_row
            self.stamp = code(self.w, 'Outcome', {'json': self.suppressed_row}, self.named, prev_node='Record Suppressed')[0]['json']
            self.outcome = self.stamp.get('outcome')
        return row

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


def _lit(v):
    return "'" + str(v).replace("'", "''") + "'"


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
