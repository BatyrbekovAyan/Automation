#!/usr/bin/env python3
"""STAGING scenarios for «Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md,
plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 9): the real bot templates with Decide Audience, the ChatAudience
webhook, the Wappi Proxy, Suggest Replies, «Сводка», Create / Delete Bot Files and the Sweep's wipe on the private n8n and
Postgres; Wappi is the MOCK, answering the way the live checks saw the real service
(docs/audits/2026-10-25-personal-chats/live-checks.md): WhatsApp's chat card from chats/filter by the digits of the number
(contact.FullName, chat.LID), Telegram's from chats/get (type, user flags), the newest page of messages/get (tapi ignores
order=asc, WhatsApp answers an empty asc page for a brand-new chat), a WhatsApp chat whose rows Wappi lists under its @lid code,
a Telegram bot's message (chat_type bot, is_bot), the owner's message to himself (outgoing_message_phone), HTTP 500 per route.

Every scenario states the correct behaviour and reads it back: the Outcome stamp and Decide Audience's answer in the bot's
execution, the decision and connection rows, what is stored about the chat (history, memory, «Сводка», the reply ledger),
and the mock's recorded calls (the fact reads, the sends). A scenario that makes a chat business ends in a real reply — the
staging bot's model answers, as in e2e-reply.py. The staging bots stgwa001 / stgtg001 are shared: a scenario sets the
profile's connection moment itself (the first-minutes window is SETTLE_MINUTES of audience.py), and moves time by shifting
timestamps in the database — it never sleeps for minutes.

    python3 Tools/n8n/staging/e2e-audience.py                  # all (WA + TG)
    python3 Tools/n8n/staging/e2e-audience.py --only contact   # substring filter
    python3 Tools/n8n/staging/e2e-audience.py --channel tg     # one channel
    python3 Tools/n8n/staging/e2e-audience.py --list

Requires the environment of e2e.py (pg.sh started, the Wappi mock running, bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001).
The mock is reset once at the start (never between scenarios: the reconnect and delete scenarios mint profiles through
profile.add, and a reset restarts the mock's id counter); each scenario clears its own profile's faults, chat cards and codes.
"""
import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(1, os.path.dirname(HERE))
from e2e import (Ctx, execution, http, latest_executions, mock, mock_total, node_output, ran,  # noqa: E402
                 wait_executions_settle, workflow_id_by_name, BASE, MOCK, WA_PROFILE, TG_PROFILE)
import audience  # noqa: E402  (the rule's own numbers: the first minutes, a waiting message's freshness, the wipe)


def _load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), os.path.join(HERE, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


e2e_reply = _load("e2e-reply")      # event(): the incoming webhook, with the personal chats' fields
e2e_auth = _load("e2e-auth")        # Owner (a session token), hook / proxy, the bot's line (line_owner, line_create), probes

# channel → (Wappi channel, staging profile, staging bot workflow name, the private dialog's chat_type in the webhook)
CHANNELS = {"wa": ("whatsapp", WA_PROFILE, f"STAGING WA {WA_PROFILE}", "dialog"),
            "tg": ("telegram", TG_PROFILE, f"STAGING TG {TG_PROFILE}", "user")}
OWN = {"wa": "77010000099", "tg": "77010000098"}         # the connected number (digits) of each staging profile
OWN_TG_USER = "996500000099"                              # the Telegram account's own user id (its «Избранное»)
OWN_LID = "100000000000001@lid"                           # the owner's own WhatsApp code
FACT_PATHS = ("chats/filter", "chats/get", "messages/get", "get/status")
CHAT_TABLES = ("history", "memory", "outcomes", "handoffs", "ledger", "state", "sets", "flags")


def psql(sql):
    out = subprocess.run([os.path.join(HERE, "pg.sh"), "psql", "-tAc", sql], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def fixture(*args):
    return e2e_auth.fixture(*args)


_VERSION = [0]


def version():
    """The owner's desired-state version: the app's ms clock, strictly increasing within the run."""
    _VERSION[0] = max(_VERSION[0] + 1, int(time.time() * 1000))
    return _VERSION[0]


# ---------------------------------------------------------------------------------------------------- Wappi shapes
def chat_id(ch, ctx, tag):
    """A run-scoped chat a card request finds by its digits: a WhatsApp number, a Telegram user id."""
    n = int(hashlib.sha1((tag + ctx.run_id + ch).encode()).hexdigest(), 16) % 10_000_000
    return f"7702{n:07d}@c.us" if ch == "wa" else f"56{n:07d}"


def lid_of(ctx, tag):
    n = int(hashlib.sha1(("lid" + tag + ctx.run_id).encode()).hexdigest(), 16) % 10 ** 11
    return f"1000{n:011d}@lid"


def wa_card(chat, full_name="", push="Клиент", lid="", found=True):
    """A WhatsApp chats/filter dialog (whatsmeow ContactInfo in `contact`: a name from the phone's address book is FullName,
    PushName is the person's own profile name; the code in chat.LID)."""
    return {"id": chat, "contact": {"Found": found, "FirstName": full_name.split(" ")[0] if full_name else "",
                                    "FullName": full_name, "PushName": push, "BusinessName": "", "RedactedPhone": ""},
            "chat": {"VerifiedName": None, "LID": lid}, "group": None, "isGroup": False, "name": full_name or push,
            "last_message_data": "", "unread_count": 1, "isDeleted": False, "isArchived": False}


def tg_card(chat, contact=False, bot=False, support=False, self_=False, type_="user"):
    """A Telegram chats/get dialog (the live check: Contact / Bot / Support / Self and the type as the phone shows them)."""
    return {"id": chat, "type": type_, "name": "", "unread_count": 1, "isDeleted": False,
            "user": {"Self": self_, "Contact": contact, "MutualContact": contact, "Deleted": False, "Bot": bot, "Support": support,
                     "FirstName": "Тест", "LastName": "", "Username": "", "Phone": ""}}


def stranger(ch, chat, lid=""):
    return wa_card(chat, lid=lid) if ch == "wa" else tg_card(chat)


def saved_contact(ch, chat, lid=""):
    return wa_card(chat, full_name="Айгерим Сохранённая", lid=lid) if ch == "wa" else tg_card(chat, contact=True)


def own_sender(ch):
    return OWN[ch] + "@c.us" if ch == "wa" else OWN_TG_USER


def row(ch, chat, mid, from_me, body, t, mtype=None):
    """A messages/get row as the mock stores it (the chat's own id; a chat given a code is served under it)."""
    return {"id": mid, "chatId": chat, "from": own_sender(ch) if from_me else chat, "fromMe": from_me,
            "type": mtype or ("chat" if ch == "wa" else "text"), "body": body, "time": t,
            "senderName": "Владелец" if from_me else "Клиент"}


# ---------------------------------------------------------------------------------------------------- reading back
def decision(pid, chat):
    return psql(f"select kind || '|' || source || '|' || reason from chat_audience where profile_id = '{pid}' and chat_id = '{chat}';")


def connection(pid):
    """(the moment − the profile's created_at in seconds, the number) of the profile's connection row, or None."""
    r = psql(f"select round(extract(epoch from (c.connected_at - b.created_at)))::int || '|' || coalesce(c.phone, '-') "
             f"from audience_connections c join bot_profiles b on b.profile_id = c.profile_id where c.profile_id = '{pid}';")
    return tuple(r.split("|")) if r else None


def stored(pid, chat):
    """What the server keeps about one chat, table by table (CHAT_TABLES order)."""
    q = (f"select (select count(*) from chat_history_events where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
         f"(select count(*) from n8n_chat_histories where session_id = '{pid}:{chat}') || '|' || "
         f"(select count(*) from conversation_outcomes where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
         f"(select count(*) from handoff_requests where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
         f"(select count(*) from reply_events where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
         f"(select count(*) from chat_reply_state where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
         f"(select count(*) from suggestion_sets where profile_id = '{pid}' and chat_id = '{chat}') || '|' || "
         f"(select count(*) from reply_mode_flags where profile_id = '{pid}' and chat_id = '{chat}');")
    return dict(zip(CHAT_TABLES, (int(x) for x in psql(q).split("|"))))


NOTHING = dict.fromkeys(CHAT_TABLES, 0)


def history(pid, chat):
    return psql(f"select coalesce(string_agg(message_id || ':' || source, ',' order by id), '') from chat_history_events "
                f"where profile_id = '{pid}' and chat_id = '{chat}';")


def memory(pid, chat):
    return psql(f"select coalesce(string_agg((message->>'type') || ':' || coalesce(message->'additional_kwargs'->>'source', 'agent'), ',' "
                f"order by id), '') from n8n_chat_histories where session_id = '{pid}:{chat}';")


def ledger(pid, chat, mid):
    return psql(f"select state from reply_events where profile_id = '{pid}' and chat_id = '{chat}' and message_id = '{mid}';")


def dialogs_counted(app_user_id):
    return int(psql(f"select count(*) from dialog_counts where app_user_id = '{app_user_id}';") or 0)


def calls_since(since, pid, *paths):
    """The mock calls FOR this profile since `since` whose path holds one of `paths` (all of them when none is given)."""
    st, d = mock("GET", f"/__mock/calls?since={since}")
    out = []
    for c in (d or {}).get("calls", []):
        q, b = c.get("query") or {}, c.get("body") if isinstance(c.get("body"), dict) else {}
        if (q.get("profile_id") or b.get("profile_id")) != pid or c.get("dropped"):
            continue
        if paths and not any(p in c["path"] for p in paths):
            continue
        out.append(c)
    return out


def recipient(c):
    b = c.get("body") if isinstance(c.get("body"), dict) else {}
    return str(b.get("recipient") or b.get("=recipient") or "")


def sends_to(since, pid, chat):
    """The transport sends of this profile that reached `chat` (a WhatsApp recipient is the number without @c.us)."""
    want = {chat, chat.split("@")[0]}
    return [c for c in calls_since(since, pid, "message/send", "message/reply") if recipient(c) in want]


def touched(since, pid, chat, *mids):
    """Every transport action on `chat` but a fact read — a send or typing (the recipient is the chat), a read receipt (the
    message id is one of `mids`): a personal chat gets none."""
    want = {chat, chat.split("@")[0]}
    out = []
    for c in calls_since(since, pid, "message/send", "message/reply", "mark/read", "typing"):
        b = c.get("body") if isinstance(c.get("body"), dict) else {}
        if recipient(c) in want or str(b.get("message_id") or "") in mids:
            out.append(c)
    return out


def fact_reads(since, pid):
    """(path tail, the query without profile_id) of the profile's fact reads since `since`."""
    out = []
    for c in calls_since(since, pid, *FACT_PATHS):
        out.append((c["path"].split("/sync/")[-1], {k: v for k, v in (c.get("query") or {}).items() if k != "profile_id"}))
    return out


def reads_clean(reads):
    """A fact read never marks the chat read and never asks the oldest-first page."""
    return all("mark_all" not in q and q.get("order") != "asc" for _, q in reads)


# ---------------------------------------------------------------------------------------------------- one scenario
class Stand:
    """One scenario on a channel's staging bot (or, with `profile`, a profile of the scenario's own that no bot serves):
    the bot answers in «Авто», no schedule, the profile's connection as the scenario sets it — `connected` = how long ago
    the channel was connected (the number OWN), None = no connection row at all (Decide Audience's Ensure Connection then
    creates it with the line's moment, `line_age` ago) — and the profile's mock state cleared."""

    def __init__(self, ctx, ch, tag, connected="2 hours", line_age=None, profile=None):
        self.ctx, self.ch, self.tag = ctx, ch, tag
        self.channel, staging_pid, wf_name, self.chat_type = CHANNELS[ch]
        self.pid = profile or staging_pid
        self.wf_id = workflow_id_by_name(wf_name) if profile is None else None
        if profile is None and not self.wf_id:
            raise RuntimeError(f"{wf_name} not imported on staging")
        ctx.bot(self.pid, self.channel)
        psql(f"do $$ begin if to_regclass('public.bot_schedule') is not null then delete from bot_schedule where profile_id = '{self.pid}'; end if; end $$;")
        if line_age:
            psql(f"update bot_profiles set created_at = now() - interval '{line_age}' where profile_id = '{self.pid}';")
        if connected is None:
            psql(f"delete from audience_connections where profile_id = '{self.pid}';")
        else:
            psql(f"insert into audience_connections (profile_id, phone, connected_at) values ('{self.pid}', '{OWN[ch]}', now() - interval '{connected}') "
                 f"on conflict (profile_id) do update set phone = excluded.phone, connected_at = excluded.connected_at, updated_at = now();")
        mock("POST", "/__mock/audience", {"profile_id": self.pid, "clear_faults": True, "dialogs": [], "lid_rows": {}})
        self.mark()

    def mark(self):
        self.since_exec = max([int(e["id"]) for e in latest_executions(self.wf_id, 0, 50)] or [0]) if self.wf_id else 0
        self.since_calls = mock_total()

    def chat(self, tag):
        return chat_id(self.ch, self.ctx, self.tag + tag)

    def cards(self, *dialogs, lid_rows=None, fail=None):
        body = {"profile_id": self.pid, "dialogs": list(dialogs)}
        if lid_rows is not None:
            body["lid_rows"] = lid_rows
        if fail:
            body["fail"] = fail
        mock("POST", "/__mock/audience", body)

    def history(self, chat, rows):
        mock("POST", "/__mock/messages", {"profile_id": self.pid, "chat_id": chat, "messages": rows})

    def incoming(self, chat, rows):
        for r in rows:
            mock("POST", "/__mock/incoming", {"profile_id": self.pid, "chat_id": chat, "message": r})

    def post(self, chat, mid, body, t, **kw):
        fields = {"chat_type": self.chat_type, "is_bot": False}
        fields.update(kw)
        st, _ = http("POST", f"{BASE}/webhook/{self.pid}", e2e_reply.event(self.pid, chat, mid, body, t, **fields), timeout=30)
        return st

    def post_raw(self, message):
        st, _ = http("POST", f"{BASE}/webhook/{self.pid}", {"messages": [message]}, timeout=30)
        return st

    def settle(self, n=1, wait_s=180):
        rows = wait_executions_settle(self.wf_id, self.since_exec, n, wait_s)
        out = []
        for r in rows:
            d = execution(r["id"])
            out.append({"id": r["id"], "status": d.get("status"), "outcome": (node_output(d, "Outcome") or {}).get("outcome"),
                        "decide": node_output(d, "Decide Audience") if ran(d, "Decide Audience") else None,
                        "webhook": ((node_output(d, "Webhook") or {}).get("body") or {}).get("messages", [{}])[0], "data": d})
        return out

    def one(self, wait_s=180):
        """Post-and-settle helper: the ONE execution since the mark (None when there is none)."""
        ex = self.settle(1, wait_s)
        return ex[-1] if ex else {}

    def sends(self, chat):
        return sends_to(self.since_calls, self.pid, chat)


def summary(e):
    return {"outcome": (e or {}).get("outcome"), "decide": (e or {}).get("decide"), "status": (e or {}).get("status")}


def own_profile(ctx, pid):
    """An install that owns `pid` (A1: ownership = bot_profiles.owner_id): the ChatAudience webhook, the proxy, Suggest Replies
    and «Сводка» answer only for the token's profiles."""
    o = e2e_auth.Owner("au", ctx.run_id)
    psql(f"update bot_profiles set owner_id = '{o.install_id}' where profile_id = '{pid}';")
    o.refresh()
    return o


def audience_set(o, pid, *chats):
    """The owner's set: (chat, kind) pairs, each with a fresh version. Returns (http, body)."""
    return e2e_auth.hook("ChatAudience", o.token, {"op": "set", "profileId": pid,
                                                   "chats": [{"chatId": c, "kind": k, "version": version()} for c, k in chats]})


def audience_list(o, *pids):
    st, d = e2e_auth.hook("ChatAudience", o.token, {"op": "list", "profileIds": list(pids)})
    return st, {p["profileId"]: p for p in ((d or {}).get("profiles") or [])} if isinstance(d, dict) else {}


def audience_connected(o, pid):
    return e2e_auth.hook("ChatAudience", o.token, {"op": "connected", "profileId": pid})


def paired(pid, ch, phone):
    """The profile paired at Wappi with `phone` (get/status: authorized, the number; Telegram's authorised account)."""
    mock("POST", "/__mock/profile", {"profile_id": pid, "authorized": True, "phone": phone,
                                     **({"account_phone": phone} if ch == "tg" else {})})


def wait_rows(sql, want, timeout=20):
    """Poll until `sql` answers `want` (a write after a webhook's answer lands a moment later)."""
    end = time.time() + timeout
    got = psql(sql)
    while got != want and time.time() < end:
        time.sleep(0.5)
        got = psql(sql)
    return got


def no_new_execution(s, quiet_s=8):
    time.sleep(quiet_s)
    return [e for e in latest_executions(s.wf_id, s.since_exec, 20)]


SCENARIOS = []


def scenario(id_, title):
    def deco(fn):
        SCENARIOS.append((id_, title, fn))
        return fn
    return deco


def for_channels(id_, title):
    def deco(fn):
        for ch in ("wa", "tg"):
            SCENARIOS.append((f"{id_}[{ch}]", title, (lambda c, ch=ch: fn(c, ch))))
        return fn
    return deco


# ================================================================================================ who writes first
@for_channels("contact_no_reply_nothing_written", "a saved contact writes first (WhatsApp: contact.FullName on the chat card; "
              "Telegram: user.Contact) → personal / contact is written once; no reply, no read receipt, no typing; nothing about "
              "the chat is stored (history, memory, «Сводка», ledger); the only Wappi call is the card read — no history read")
def contact_no_reply(ctx, ch):
    s = Stand(ctx, ch, "ct")
    chat, mid, t = s.chat("c"), f"ct-{ctx.run_id}", int(time.time())
    s.cards(stranger(ch, s.chat("other")), saved_contact(ch, chat))
    s.history(chat, [row(ch, chat, mid, False, "Привет, ты дома?", t)])
    dialogs = dialogs_counted(ctx.app_user_id)
    s.post(chat, mid, "Привет, ты дома?", t, **({"contact_name": "Айгерим Сохранённая"} if ch == "wa" else {}))
    e = s.one(60)
    reads = fact_reads(s.since_calls, s.pid)
    card = ("chats/filter", {"client_name": chat.split("@")[0]}) if ch == "wa" else \
        ("chats/get", {"limit": "20", "offset": "0", "order": "desc"})
    ok = (e.get("outcome") == "personal_contact" and e.get("decide") == {"audience": "personal", "outcome": "personal_contact"}
          and decision(s.pid, chat) == "personal|default|contact" and stored(s.pid, chat) == NOTHING
          and not touched(s.since_calls, s.pid, chat, mid) and reads == [card] and dialogs_counted(ctx.app_user_id) == dialogs)
    return ok, {**summary(e), "decision": decision(s.pid, chat), "stored": stored(s.pid, chat), "reads": reads,
                "touched": len(touched(s.since_calls, s.pid, chat, mid))}


@for_channels("new_person_replies", "a new person (not in the contacts — WhatsApp: the card's contact with an empty FullName, the "
              "webhook's contact_name his own profile name; Telegram: user.Contact false) writes first after the first minutes, no "
              "conversation before the connection → business / new is written and the AI replies once. The profile starts with NO "
              "connection row: Decide Audience's Ensure Connection creates it with the line's moment (bot_profiles.created_at), on the "
              "stand's task runners")
def new_person(ctx, ch):
    s = Stand(ctx, ch, "np", connected=None, line_age="2 hours")
    chat, mid, t = s.chat("c"), f"np-{ctx.run_id}", int(time.time())
    s.cards(stranger(ch, chat))
    s.history(chat, [row(ch, chat, mid, False, "Здравствуйте! Сколько стоит букет из 25 роз?", t)])
    s.post(chat, mid, "Здравствуйте! Сколько стоит букет из 25 роз?", t, **({"contact_name": "Клиент"} if ch == "wa" else {}))
    e = s.one()
    reads = fact_reads(s.since_calls, s.pid)
    decide_reads = [r for r in reads if r[0] != "messages/get" or r[1].get("limit") == "10"]
    ok = (e.get("outcome") == "sent" and e.get("decide") == {"audience": "business", "outcome": "business"}
          and decision(s.pid, chat) == "business|default|new" and connection(s.pid) == ("0", "-")
          and len(s.sends(chat)) == 1 and ledger(s.pid, chat, mid) == "sent" and history(s.pid, chat).startswith(f"{mid}:client")
          and reads_clean(reads) and ("messages/get", {"chat_id": chat, "limit": "10", "order": "desc"}) in decide_reads)
    return ok, {**summary(e), "decision": decision(s.pid, chat), "connection(moment−created s, phone)": connection(s.pid),
                "sends": len(s.sends(chat)), "ledger": ledger(s.pid, chat, mid), "history": history(s.pid, chat), "reads": decide_reads}


@scenario("wa_never_saved_number_writes_first_replies_once[wa]", "a WhatsApp number that was never in the address book writes "
          "first (the card's contact present, FullName / PushName empty, Found false), no conversation before the connection, "
          "after the first minutes → business; the AI replies exactly once — the re-delivered webhook is a duplicate")
def never_saved(ctx):
    s = Stand(ctx, "wa", "ns")
    chat, mid, t = s.chat("c"), f"ns-{ctx.run_id}", int(time.time())
    s.cards(wa_card(chat, full_name="", push="", found=False))
    s.history(chat, [row("wa", chat, mid, False, "Добрый день, вы работаете сегодня?", t)])
    s.post(chat, mid, "Добрый день, вы работаете сегодня?", t)
    first = s.one()
    s.post(chat, mid, "Добрый день, вы работаете сегодня?", t)          # the transport re-delivers the same message
    ex = s.settle(2)
    outcomes = sorted(x["outcome"] or "?" for x in ex)
    ok = (first.get("outcome") == "sent" and first.get("decide") == {"audience": "business", "outcome": "business"}
          and decision(s.pid, chat) == "business|default|new" and outcomes == ["duplicate", "sent"] and len(s.sends(chat)) == 1)
    return ok, {**summary(first), "decision": decision(s.pid, chat), "outcomes": outcomes, "sends": len(s.sends(chat))}


@for_channels("old_chat_no_reply", "a person who is not a contact but wrote before the connection (the newest page holds rows "
              "older than the moment) → personal / history; no reply, nothing stored. On Telegram the mock answers order=asc the "
              "way tapi does (the newest page, re-sorted): a regression to «the oldest message» would see only the trigger and "
              "answer an old acquaintance — the scenario would fail")
def old_chat(ctx, ch):
    s = Stand(ctx, ch, "oc")
    chat, mid, t = s.chat("c"), f"oc-{ctx.run_id}", int(time.time())
    s.cards(stranger(ch, chat))
    s.history(chat, [row(ch, chat, mid, False, "Привет! Как дела?", t),
                     row(ch, chat, f"oc0-{ctx.run_id}", True, "С днём рождения!", t - 10 * 86400),
                     row(ch, chat, f"oc1-{ctx.run_id}", False, "Спасибо!", t - 10 * 86400 + 60)])
    s.post(chat, mid, "Привет! Как дела?", t)
    e = s.one(60)
    reads = fact_reads(s.since_calls, s.pid)
    ok = (e.get("outcome") == "personal_history" and e.get("decide") == {"audience": "personal", "outcome": "personal_history"}
          and decision(s.pid, chat) == "personal|default|history" and stored(s.pid, chat) == NOTHING
          and not touched(s.since_calls, s.pid, chat, mid) and reads_clean(reads)
          and ("messages/get", {"chat_id": chat, "limit": "10", "order": "desc"}) in reads)
    return ok, {**summary(e), "decision": decision(s.pid, chat), "stored": stored(s.pid, chat), "reads": reads}


@for_channels("settle_window_no_reply_no_write", "inside the first minutes after the connection (the line's moment a minute ago, "
              "no connection row: Ensure Connection creates it) a new person's message gets no reply, asks Wappi nothing, writes "
              "no decision and stores nothing; once the window is over (the moment moved back past it) the next message decides "
              "business and is answered")
def settle_window(ctx, ch):
    s = Stand(ctx, ch, "sw", connected=None, line_age="1 minute")
    chat, t = s.chat("c"), int(time.time())
    m1, m2 = f"sw1-{ctx.run_id}", f"sw2-{ctx.run_id}"
    s.cards(stranger(ch, chat))
    s.history(chat, [row(ch, chat, m1, False, "Здравствуйте, есть пионы?", t)])
    s.post(chat, m1, "Здравствуйте, есть пионы?", t)
    e1 = s.one(60)
    reads1, conn1, dec1, stored1 = fact_reads(s.since_calls, s.pid), connection(s.pid), decision(s.pid, chat), stored(s.pid, chat)
    psql(f"update audience_connections set connected_at = now() - interval '{audience.SETTLE_MINUTES + 1} minutes' "
         f"where profile_id = '{s.pid}';")
    s.mark()
    s.incoming(chat, [row(ch, chat, m2, False, "А сколько стоят?", t + 2)])
    s.post(chat, m2, "А сколько стоят?", t + 2)
    e2 = s.one()
    ok = (e1.get("outcome") == "personal_settling" and e1.get("decide") == {"audience": "personal", "outcome": "personal_settling"}
          and reads1 == [] and conn1 == ("0", "-") and dec1 == "" and stored1 == NOTHING
          and e2.get("outcome") == "sent" and decision(s.pid, chat) == "business|default|new" and len(s.sends(chat)) == 1)
    return ok, {"first": summary(e1), "reads_first": reads1, "connection_first": conn1, "decision_first": dec1, "stored_first": stored1,
                "second": summary(e2), "decision": decision(s.pid, chat), "sends": len(s.sends(chat))}


@for_channels("wappi_failure_then_next_message_decides", "Wappi answers HTTP 500 — first to the chat card, then to the history "
              "read — so the fact is not known: those messages are personal (no reply, nothing stored) and NO decision is "
              "written; the next message asks again, decides business and is answered")
def wappi_failure(ctx, ch):
    s = Stand(ctx, ch, "wf")
    chat, t = s.chat("c"), int(time.time())
    m1, m2, m3 = (f"wf{i}-{ctx.run_id}" for i in (1, 2, 3))
    card_route = "chats/filter" if ch == "wa" else "chats/get"
    s.cards(stranger(ch, chat), fail={card_route: 1})
    s.history(chat, [row(ch, chat, m1, False, "Добрый день!", t)])
    s.post(chat, m1, "Добрый день!", t)
    e1 = s.one(60)
    after1 = (decision(s.pid, chat), stored(s.pid, chat))
    mock("POST", "/__mock/audience", {"profile_id": s.pid, "fail": {"messages/get": 1}})
    s.mark()
    s.incoming(chat, [row(ch, chat, m2, False, "Вы на связи?", t + 2)])
    s.post(chat, m2, "Вы на связи?", t + 2)
    e2 = s.one(60)
    after2 = (decision(s.pid, chat), stored(s.pid, chat))
    s.mark()
    s.incoming(chat, [row(ch, chat, m3, False, "Хочу заказать букет", t + 4)])
    s.post(chat, m3, "Хочу заказать букет", t + 4)
    e3 = s.one()
    ok = (e1.get("outcome") == "personal_unknown" and after1 == ("", NOTHING)
          and e2.get("outcome") == "personal_unknown" and after2 == ("", NOTHING)
          and e3.get("outcome") == "sent" and decision(s.pid, chat) == "business|default|new" and len(s.sends(chat)) == 1)
    return ok, {"card_500": summary(e1), "history_500": summary(e2), "after_each": [after1[0], after2[0]],
                "next": summary(e3), "decision": decision(s.pid, chat), "sends": len(s.sends(chat))}


# ================================================================================================ the owner's decisions
def conversation(ch, chat, ctx, tag, trig_age_s=120, mtype=None):
    """A client's question, the owner's answer, and the client's fresh message waiting for the answer (newest first)."""
    t = int(time.time())
    trig = f"{tag}t-{ctx.run_id}"
    rows = [row(ch, chat, trig, False, "А доставка завтра к 10 будет?", t - trig_age_s, mtype),
            row(ch, chat, f"{tag}o-{ctx.run_id}", True, "Здравствуйте! Розы есть.", t - trig_age_s - 600),
            row(ch, chat, f"{tag}f-{ctx.run_id}", False, "Есть розы?", t - trig_age_s - 1200)]
    return trig, rows


@for_channels("make_business_fresh_replies_once", "a chat decided personal (a saved contact) whose client is waiting: the owner "
              "makes it business (ChatAudience set) → the start of the conversation reaches the history and the memory (the "
              "owner's answer with the owner mark), the waiting message (2 minutes old) is handed to the bot again and answered "
              "exactly once; the same set again replays nothing. WhatsApp: the rows are listed under the number, the card names "
              "the chat's code — the replayed message carries it as whatsapp_chat_id")
def make_business_fresh(ctx, ch):
    s = Stand(ctx, ch, "mb")
    o = own_profile(ctx, s.pid)
    chat, code = s.chat("c"), lid_of(ctx, "mb")
    trig, rows = conversation(ch, chat, ctx, "mb")
    s.cards(saved_contact(ch, chat, lid=code if ch == "wa" else ""))
    s.history(chat, rows)
    s.post(chat, trig, rows[0]["body"], rows[0]["time"])
    e0 = s.one(60)
    s.mark()
    st, d = audience_set(o, s.pid, (chat, "business"))
    e = s.one()
    msg = e.get("webhook") or {}
    s2 = (s.since_exec, s.since_calls)
    s.mark()
    st2, d2 = audience_set(o, s.pid, (chat, "business"))
    again = no_new_execution(s)
    s.since_exec, s.since_calls = s2
    res = (d or {}).get("results") or [{}]
    want_msg = {"chatId": chat, "from": chat, "id": trig, **({"whatsapp_chat_id": code} if ch == "wa" else {})}
    ok = (e0.get("outcome") == "personal_contact" and st == 200 and res[0].get("applied") is True and res[0].get("kind") == "business"
          and all(msg.get(k) == v for k, v in want_msg.items()) and (ch == "wa" or "whatsapp_chat_id" not in msg)
          and e.get("outcome") == "sent" and len(s.sends(chat)) == 1 and ledger(s.pid, chat, trig) == "sent"
          and history(s.pid, chat).startswith(f"{rows[2]['id']}:client,{rows[1]['id']}:owner_phone,{trig}:client")
          and memory(s.pid, chat).startswith("human:client,ai:owner_phone")
          and st2 == 200 and ((d2 or {}).get("results") or [{}])[0].get("applied") is True and not again)
    return ok, {"first": summary(e0), "set": (st, res), "replayed": {k: msg.get(k) for k in ("id", "chatId", "from", "whatsapp_chat_id")},
                "run": summary(e), "sends": len(s.sends(chat)), "ledger": ledger(s.pid, chat, trig), "history": history(s.pid, chat),
                "memory": memory(s.pid, chat), "same_set_again": (st2, len(again))}


@scenario("wa_lid_rows_make_business_backfills_and_replays[wa]", "a WhatsApp chat Wappi lists under its @lid code (the list "
          "entry under the number with chat.LID = the code, every messages/get row under the code, the webhook with chatId = "
          "the number and whatsapp_chat_id = the code), decided personal (a saved contact), made business by the owner → "
          "ChatAudience reads the card, backfills the conversation under the NUMBER (history + memory, nothing under the code), "
          "sends exactly one replay with chatId = from = the number and whatsapp_chat_id = the code, and the replayed run — whose "
          "reads list the chat under the code, which the bot takes as the chat's — answers exactly once: outcome sent, one send, "
          "the ledger under the number says sent")
def lid_rows(ctx):
    s = Stand(ctx, "wa", "lid")
    o = own_profile(ctx, s.pid)
    chat, code = s.chat("c"), lid_of(ctx, "lid")
    trig, rows = conversation("wa", chat, ctx, "ld")
    s.cards(saved_contact("wa", chat, lid=code), lid_rows={chat: code})      # the owner's rows carry his own code (OWN_LID)
    s.history(chat, rows)
    s.post(chat, trig, rows[0]["body"], rows[0]["time"], whatsapp_chat_id=code, contact_name="Айгерим Сохранённая")
    e0 = s.one(60)
    s.mark()
    st, d = audience_set(o, s.pid, (chat, "business"))
    ex = s.settle(1, 180)
    time.sleep(3)
    ex = s.settle(1, 30)
    replays = [x for x in ex if (x.get("webhook") or {}).get("id") == trig]
    msg = (replays[0].get("webhook") if replays else {}) or {}
    cards = [r for r in fact_reads(s.since_calls, s.pid) if r[0] == "chats/filter"]
    hist, hist_code = history(s.pid, chat), history(s.pid, code)
    mem, mem_code = memory(s.pid, chat), memory(s.pid, code)
    led = ledger(s.pid, chat, trig)
    ok = (e0.get("outcome") == "personal_contact" and st == 200 and ((d or {}).get("results") or [{}])[0].get("applied") is True
          and cards == [("chats/filter", {"client_name": chat.split("@")[0]})]
          and hist.startswith(f"{rows[2]['id']}:client,{rows[1]['id']}:owner_phone") and hist_code == ""
          and mem.startswith("human:client,ai:owner_phone") and mem_code == ""
          and len(replays) == 1 and len(ex) == 1
          and msg.get("chatId") == chat and msg.get("from") == chat and msg.get("whatsapp_chat_id") == code
          and replays[0].get("outcome") == "sent" and led == "sent" and len(s.sends(chat)) == 1)
    last = ((replays[0]["data"].get("data") or {}).get("resultData") or {}).get("lastNodeExecuted") if replays else None
    return ok, {"first": summary(e0), "set": st, "card_reads": cards, "history": hist, "history_under_code": hist_code,
                "memory": mem, "memory_under_code": mem_code, "replays": len(replays), "executions": len(ex),
                "replayed": {k: msg.get(k) for k in ("id", "chatId", "from", "whatsapp_chat_id")},
                "replayed_run": {"outcome": replays[0].get("outcome") if replays else None, "ledger": led, "last_node": last},
                "sends": len(s.sends(chat))}


@for_channels("make_business_stale_no_reply", f"a personal chat whose client wrote {audience.FRESH_HOURS + 1} hours ago: the owner "
              "makes it business → the conversation is backfilled (the old message included), nothing is handed to the bot, no reply")
def make_business_stale(ctx, ch):
    s = Stand(ctx, ch, "ms")
    o = own_profile(ctx, s.pid)
    chat = s.chat("c")
    trig, rows = conversation(ch, chat, ctx, "ms", trig_age_s=(audience.FRESH_HOURS + 1) * 3600)
    s.cards(saved_contact(ch, chat))
    s.history(chat, rows)
    s.post(chat, trig, rows[0]["body"], rows[0]["time"])
    e0 = s.one(60)
    s.mark()
    st, d = audience_set(o, s.pid, (chat, "business"))
    hist = wait_rows(f"select count(*) from chat_history_events where profile_id = '{s.pid}' and chat_id = '{chat}';", "3")
    after = no_new_execution(s, 6)
    ok = (e0.get("outcome") == "personal_contact" and st == 200 and decision(s.pid, chat) == "business|owner|owner"
          and hist == "3" and history(s.pid, chat) == f"{rows[2]['id']}:client,{rows[1]['id']}:owner_phone,{trig}:client"
          and memory(s.pid, chat) == "human:client,ai:owner_phone,human:client" and not after and not s.sends(chat))
    return ok, {"first": summary(e0), "set": st, "history": history(s.pid, chat), "memory": memory(s.pid, chat),
                "bot_runs_after": len(after), "sends": len(s.sends(chat))}


@for_channels("make_personal_cancels_inflight", "a business chat; while the AI composes the answer the owner makes the chat "
              "personal → the send lease refuses (cancelled_personal), nothing is sent, the reply's draft leaves the memory")
def make_personal_inflight(ctx, ch):
    s = Stand(ctx, ch, "mp")
    o = own_profile(ctx, s.pid)
    chat = s.chat("c")
    s.cards(stranger(ch, chat))
    st0, _ = audience_set(o, s.pid, (chat, "business"))     # business before the client writes: nothing waits, nothing replays
    time.sleep(2)
    s.mark()
    t, mid = int(time.time()), f"mp-{ctx.run_id}"
    s.history(chat, [row(ch, chat, mid, False, "Сколько стоит букет из 25 роз?", t)])
    s.post(chat, mid, "Сколько стоит букет из 25 роз?", t)
    time.sleep(10.5)                                           # past the debounce and the fetch: the answer is being composed
    st1, d1 = audience_set(o, s.pid, (chat, "personal"))
    e = s.one()
    ai = psql(f"select count(*) from n8n_chat_histories where session_id = '{s.pid}:{chat}' and message->>'type' = 'ai' "
              f"and (message->'additional_kwargs'->>'source') is null;")
    ok = (st0 == 200 and st1 == 200 and ((d1 or {}).get("results") or [{}])[0].get("applied") is True
          and e.get("outcome") == "cancelled_personal" and ledger(s.pid, chat, mid) == "cancelled_personal"
          and not s.sends(chat) and ai == "0" and decision(s.pid, chat) == "personal|owner|owner")
    return ok, {"run": summary(e), "ledger": ledger(s.pid, chat, mid), "sends": len(s.sends(chat)), "ai_drafts": ai,
                "decision": decision(s.pid, chat)}


# ================================================================================================ not a person
@scenario("tg_bot_777000_channel_no_reply[tg]", "Telegram: a bot's message (the webhook's chat_type bot, is_bot true), the service "
          "account 777000, a channel post, and a bot known only from its card (user.Bot) → personal / nonhuman each, written; "
          "no reply, nothing stored; the first three ask Wappi nothing; the owner cannot make 777000 business (refused nonhuman)")
def tg_nonhuman(ctx):
    s = Stand(ctx, "tg", "nh")
    o = own_profile(ctx, s.pid)
    bot, svc, chan, card_bot = s.chat("bot"), "777000", "-100" + s.chat("chan"), s.chat("cbot")
    psql(f"delete from chat_audience where profile_id = '{s.pid}' and chat_id in ('{svc}');")
    s.cards(tg_card(card_bot, bot=True))
    t = int(time.time())
    cases = [(bot, {"chat_type": "bot", "is_bot": True}), (svc, {"chat_type": "user", "is_bot": False}),
             (chan, {"chat_type": "channel", "is_bot": False}), (card_bot, {"chat_type": "user", "is_bot": False})]
    res, reads = {}, {}
    for i, (chat, kw) in enumerate(cases):
        mid = f"nh{i}-{ctx.run_id}"
        s.history(chat, [row("tg", chat, mid, False, "/start" if i == 0 else "Код входа: 12345", t + i)])
        s.mark()
        s.post(chat, mid, "Код входа: 12345", t + i, **kw)
        e = s.one(60)
        reads[chat] = fact_reads(s.since_calls, s.pid)
        res[chat] = (e.get("outcome"), decision(s.pid, chat), stored(s.pid, chat) == NOTHING, len(touched(s.since_calls, s.pid, chat, mid)))
    st, d = audience_set(o, s.pid, (svc, "business"))
    refused = ((d or {}).get("results") or [{}])[0]
    ok = (all(v == ("personal_nonhuman", "personal|default|nonhuman", True, 0) for v in res.values())
          and reads[bot] == [] and reads[svc] == [] and reads[chan] == []
          and reads[card_bot] == [("chats/get", {"limit": "20", "offset": "0", "order": "desc"})]
          and st == 200 and refused.get("applied") is False and refused.get("refused") == "nonhuman"
          and decision(s.pid, svc) == "personal|default|nonhuman")
    return ok, {"per_chat(outcome, decision, nothing_stored, transport_actions)": res, "reads": reads, "owner_set_777000": refused}


@scenario("wa_newsletter_no_reply[wa]", "WhatsApp: a channel (@newsletter), the status list (status@broadcast) and a group id "
          "(@g.us) — should Wappi ever deliver them with from == chatId — are personal / nonhuman by the id alone: no Wappi "
          "call, no reply, nothing stored; the owner cannot make the channel business (refused nonhuman)")
def wa_nonhuman(ctx):
    s = Stand(ctx, "wa", "nl")
    o = own_profile(ctx, s.pid)
    n = s.chat("n").split("@")[0]
    chats = [f"120363{n}@newsletter", "status@broadcast", f"120364{n}@g.us"]
    psql(f"delete from chat_audience where profile_id = '{s.pid}' and chat_id = 'status@broadcast';")
    t = int(time.time())
    res = {}
    for i, chat in enumerate(chats):
        mid = f"nl{i}-{ctx.run_id}"
        s.history(chat, [row("wa", chat, mid, False, "Новость дня", t + i)])
        s.mark()
        s.post(chat, mid, "Новость дня", t + i, chat_type="channel" if i == 0 else "dialog")
        e = s.one(60)
        res[chat] = (e.get("outcome"), decision(s.pid, chat), stored(s.pid, chat) == NOTHING, fact_reads(s.since_calls, s.pid),
                     len(touched(s.since_calls, s.pid, chat, mid)))
    st, d = audience_set(o, s.pid, (chats[0], "business"))
    refused = ((d or {}).get("results") or [{}])[0]
    ok = (all(v == ("personal_nonhuman", "personal|default|nonhuman", True, [], 0) for v in res.values())
          and st == 200 and refused.get("applied") is False and refused.get("refused") == "nonhuman")
    return ok, {"per_chat(outcome, decision, nothing_stored, reads, transport_actions)": res, "owner_set_channel": refused}


@for_channels("self_message_not_recorded", "the owner writes to himself («Избранное»): it arrives as outgoing_message_phone with "
              "is_me 1 and the chat = his own number (Telegram: his own user id) → recorded nowhere (the chat has no business "
              "decision), no decision written, no reply")
def self_message(ctx, ch):
    s = Stand(ctx, ch, "sf")
    chat = OWN["wa"] + "@c.us" if ch == "wa" else OWN_TG_USER
    psql(f"delete from chat_audience where profile_id = '{s.pid}' and chat_id = '{chat}';")
    t, mid = int(time.time()), f"sf-{ctx.run_id}"
    msg = e2e_reply.event(s.pid, chat, mid, "Купить молоко", t, from_me=True, chat_type=s.chat_type, is_bot=False,
                          **({"whatsapp_chat_id": OWN_LID} if ch == "wa" else {}))["messages"][0]
    msg.update({"wh_type": "outgoing_message_phone", "is_me": 1, "to": chat, "senderName": "Владелец"})
    s.post_raw(msg)
    e = s.one(60)
    ok = (e.get("outcome") == "ignored_personal" and decision(s.pid, chat) == "" and stored(s.pid, chat) == NOTHING
          and not touched(s.since_calls, s.pid, chat, mid) and fact_reads(s.since_calls, s.pid) == [])
    return ok, {**summary(e), "decision": decision(s.pid, chat), "stored": stored(s.pid, chat)}


# ================================================================================================ the other writers and readers
def dedicated(ctx, ch, tag):
    """A profile of this scenario alone (no bot serves it): the scenario's assertions span the profile's chats."""
    return Stand(ctx, ch, tag, profile=f"stgau{tag}{ch}{ctx.run_id[:6]}")


@for_channels("owner_app_send_in_personal_not_recorded", "the owner's messages from the app (Wappi Proxy message.send) go out in "
              "every chat, but only a BUSINESS chat records them (owner_app history + the marked memory row); a personal chat and a "
              "chat without a decision record nothing")
def owner_app_send(ctx, ch):
    s = dedicated(ctx, ch, "oa")
    o = own_profile(ctx, s.pid)
    personal, business, undecided = s.chat("p"), s.chat("b"), s.chat("u")
    st0, _ = audience_set(o, s.pid, (personal, "personal"), (business, "business"))
    time.sleep(2)
    s.mark()
    out = {}
    for chat in (personal, undecided, business):
        st, d = e2e_auth.proxy(o.token, {"op": "message.send", "channel": s.channel, "profile_id": s.pid,
                                         "recipient": chat.split("@")[0] if ch == "wa" else chat, "body": "Спасибо, до встречи"})
        out[chat] = (st, bool((d or {}).get("message_id")))
    got = wait_rows(f"select count(*) from chat_history_events where profile_id = '{s.pid}' and chat_id = '{business}' "
                    f"and source = 'owner_app';", "1")
    time.sleep(2)
    ok = (st0 == 200 and all(v == (200, True) for v in out.values()) and got == "1"
          and history(s.pid, business).endswith(":owner_app") and memory(s.pid, business) == "ai:owner_app"
          and stored(s.pid, personal) == NOTHING and stored(s.pid, undecided) == NOTHING
          and len(calls_since(s.since_calls, s.pid, "message/send")) == 3)
    return ok, {"sends(http, id)": out, "business": (history(s.pid, business), memory(s.pid, business)),
                "personal": stored(s.pid, personal), "undecided": stored(s.pid, undecided)}


def suggest(o, ch, pid, chat):
    since = max([int(e["id"]) for e in latest_executions("9PTyYcelRQI7bGDb", 0, 50)] or [0])
    body = {"v": 1, "requestSeq": 1, "profileId": pid, "chatId": chat, "botWaId": "-1", "botTgId": "-1", "channel": CHANNELS[ch][0],
            "businessTypeId": "flowers", "businessName": "Цветы Астана", "ownerPrompt": "", "catalog": "• Букет 25 роз — 25000 тг",
            "messages": [{"role": "client", "text": "Сколько стоит букет из 25 роз?", "ts": int(time.time())}],
            "appUserId": o.app_user_id, "requestKey": hashlib.md5((pid + chat + str(version())).encode()).hexdigest()}
    st, d = e2e_auth.hook("SuggestReplies", o.token, body)
    rows = wait_executions_settle("9PTyYcelRQI7bGDb", since, 1, 90)
    ex = execution(rows[-1]["id"]) if rows else {}
    return st, d, ex


@for_channels("suggest_refused_for_personal", "Suggest Replies refuses a personal chat and a chat without a decision on the server "
              "(personal_chat for the session caller): the model never runs and nothing is counted; a business chat gets cards")
def suggest_personal(ctx, ch):
    s = dedicated(ctx, ch, "sg")
    o = own_profile(ctx, s.pid)
    personal, business, undecided = s.chat("p"), s.chat("b"), s.chat("u")
    audience_set(o, s.pid, (personal, "personal"), (business, "business"))
    counted = lambda: psql(f"select coalesce(sum(n), 0) from suggestion_counts where app_user_id = '{o.app_user_id}';")   # noqa: E731
    res = {}
    for chat in (personal, undecided):
        st, d, ex = suggest(o, ch, s.pid, chat)
        res[chat] = (st, (d or {}).get("error"), len((d or {}).get("suggestions") or []), ran(ex, "LLM"),
                     (node_output(ex, "Gate Decision") or {}).get("gateReason"), counted())
    st, d, ex = suggest(o, ch, s.pid, business)
    cards = (d or {}).get("suggestions") or []
    ok = (all(v == (200, "personal_chat", 0, False, "personal_chat", "0") for v in res.values())
          and st == 200 and 1 <= len(cards) <= 4 and not (d or {}).get("error") and ran(ex, "LLM") and counted() == "1")
    return ok, {"refused(http, error, cards, llm_ran, gate, counted)": res, "business": (st, len(cards), (d or {}).get("error"), counted())}


@for_channels("dashboard_hides_personal", "«Сводка» shows a business chat's outcome and leaves out a personal one — its verdict and "
              "its open handoff alike — at once (not when its data is wiped); made business again, it comes back")
def dashboard(ctx, ch):
    s = dedicated(ctx, ch, "db")
    o = own_profile(ctx, s.pid)
    business, personal = s.chat("b"), s.chat("p")
    audience_set(o, s.pid, (business, "business"), (personal, "personal"))
    for chat, summary_ in ((business, "Заказ: 25 роз"), (personal, "Личная договорённость")):
        psql(f"insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at, outcome_at) "
             f"values ('{s.pid}:{chat}', '{s.pid}', '{chat}', 'in_dialog', '{summary_}', 0, now() - interval '1 hour', now() - interval '1 hour');")
    psql(f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason, summary) values "
         f"('{s.pid}', '{personal}', 'db-{ctx.run_id}', 'order', 'Личный заказ');")

    def shown():
        st, d = e2e_auth.hook("DashboardOutcomes", o.token, {"profileIds": [s.pid]})
        return st, sorted((r.get("chatId"), r.get("source")) for r in ((d or {}).get("outcomes") or []))
    st1, rows1 = shown()
    audience_set(o, s.pid, (personal, "business"))
    st2, rows2 = shown()
    ok = (st1 == 200 and rows1 == [(business, "classifier")]
          and st2 == 200 and rows2 == sorted([(business, "classifier"), (personal, "event")]))
    return ok, {"personal": (st1, rows1), "made_business": (st2, rows2)}


@for_channels("wipe_after_two_days", "the Sweep's «Wipe Personal Chats» (the derived node, run on the real n8n): a chat the owner "
              f"made personal more than {audience.WIPE_DAYS} days ago loses what the assistant stored about it (memory, history, "
              "«Сводка», handoffs, ledger, chat state, shown sets, its own reply-mode row) and is marked wiped; one still inside "
              "that limit keeps everything; the bot-wide reply-mode row and the tariff usage stay")
def wipe(ctx, ch):
    s = dedicated(ctx, ch, "wp")
    old, recent = s.chat("old"), s.chat("new")
    # a day past the limit and a day inside it
    for label, chat, since in (("old", old, f"{audience.WIPE_DAYS + 1} days"), ("new", recent, f"{audience.WIPE_DAYS - 1} days")):
        fixture("set-audience", "--profile-id", s.pid, "--chat-id", chat, "--kind", "personal")
        psql(f"update chat_audience set personal_since = now() - interval '{since}' where profile_id = '{s.pid}' and chat_id = '{chat}';")
        tag = f"wp{label}{ctx.run_id[:6]}"
        psql(f"insert into n8n_chat_histories (session_id, message) values ('{s.pid}:{chat}', '{{\"type\": \"human\", \"content\": \"x\"}}'::jsonb), "
             f"('{s.pid}:{chat}', '{{\"type\": \"ai\", \"content\": \"y\"}}'::jsonb);"
             f"insert into chat_history_events (profile_id, chat_id, message_id, source, body) values ('{s.pid}', '{chat}', '{tag}', 'client', 'x');"
             f"insert into conversation_outcomes (session_id, profile_id, chat_id, outcome, summary, last_history_id, last_message_at, outcome_at) "
             f"values ('{s.pid}:{chat}', '{s.pid}', '{chat}', 'in_dialog', 's', 0, now(), now());"
             f"insert into handoff_requests (profile_id, chat_id, source_message_id, reason) values ('{s.pid}', '{chat}', '{tag}', 'question');"
             f"insert into reply_events (profile_id, chat_id, message_id, state) values ('{s.pid}', '{chat}', '{tag}', 'sent');"
             f"insert into chat_reply_state (profile_id, chat_id, incoming_generation) values ('{s.pid}', '{chat}', 1);"
             f"insert into suggestion_sets (request_key, profile_id, chat_id, items) values ('{tag}', '{s.pid}', '{chat}', '[]'::jsonb);"
             f"insert into reply_mode_flags (profile_id, chat_id, suppressed, version) values ('{s.pid}', '{chat}', true, 3);"
             f"insert into dialog_counts (app_user_id, chat_id, d) values ('{ctx.app_user_id}', '{chat}', (now() at time zone 'Asia/Almaty')::date) "
             f"on conflict do nothing;")
    before = (stored(s.pid, old), stored(s.pid, recent))
    wid = None
    try:
        wid, url = e2e_auth.probe_workflow(ctx.run_id, e2e_auth.SWEEP_FILE, ("Wipe Personal Chats",), "Wipe Personal Chats",
                                           f"wp{ch}{int(time.time())}")
        ex = e2e_auth.run_probe(wid, url)
    finally:
        e2e_auth.drop_probe(wid)
    out = (e2e_auth.node_items(ex, "Wipe Personal Chats") or [{}])[0]
    wiped = psql(f"select chat_id || ':' || (wiped_at is not null) from chat_audience where profile_id = '{s.pid}' order by chat_id;")
    bot_wide = psql(f"select count(*) from reply_mode_flags where profile_id = '{s.pid}' and chat_id = '*';")
    usage = psql(f"select count(*) from dialog_counts where app_user_id = '{ctx.app_user_id}' and chat_id in ('{old}', '{recent}');")
    full = dict.fromkeys(CHAT_TABLES, 1)
    full["memory"] = 2
    ok = (before == (full, full) and int(out.get("wiped_chats") or 0) >= 1 and int(out.get("deleted_rows") or 0) >= 9
          and stored(s.pid, old) == NOTHING and stored(s.pid, recent) == full
          and set(wiped.split("\n")) == {f"{old}:true", f"{recent}:false"} and bot_wide == "1" and usage == "2")
    return ok, {"node": out, "old": stored(s.pid, old), "recent": stored(s.pid, recent), "wiped": wiped, "bot_wide_flag": bot_wide,
                "usage_rows": usage}


# ================================================================================================ the bot's line
def line_bot(ctx, ch, tag):
    """A bot of its own (Create through the real webhook): an install, its first profile, its first clone."""
    o, p1 = e2e_auth.line_owner({"run_id": ctx.run_id}, f"{tag}{ch}", ch)
    key = f"Bot-{tag}{ch}-{ctx.run_id}"
    st, d, w1, _ = e2e_auth.line_create(o, ch, p1, key)
    return o, p1, key, w1, (st, d)


def decisions_of(o, pid):
    st, by = audience_list(o, pid)
    p = by.get(pid) or {}
    return st, p.get("connectedAt"), sorted((x["chatId"], x["kind"], x["source"], x["reason"]) for x in p.get("decisions") or [])


@for_channels("reconnect_keeps_decisions", "a reconnect (a NEW profile of the same bot: Create from settings, the same number) keeps "
              "the owner's marks, the computed decisions and the connection moment: the bind moves them onto the new profile, "
              "the app's connected report for it says restarted false and the moment is the first connection's")
def reconnect(ctx, ch):
    o, p1, key, w1, setup = line_bot(ctx, ch, "rk")
    if not w1:
        return False, f"setup create: {setup}"
    try:
        paired(p1, ch, OWN[ch])
        c1 = audience_connected(o, p1)
        a, b, c = chat_id(ch, ctx, "rka"), chat_id(ch, ctx, "rkb"), chat_id(ch, ctx, "rkc")
        audience_set(o, p1, (a, "business"), (b, "personal"))
        psql(f"insert into chat_audience (profile_id, chat_id, kind, source, reason) values ('{p1}', '{c}', 'personal', 'default', 'contact');")
        before = decisions_of(o, p1)
        p2 = o.add_profile(CHANNELS[ch][0])
        st2, d2, w2, _ = e2e_auth.line_create(o, ch, p2, key)
        paired(p2, ch, OWN[ch])
        c2 = audience_connected(o, p2)
        after = decisions_of(o, p2)
        left = psql(f"select (select count(*) from chat_audience where profile_id = '{p1}') || '|' || "
                    f"(select count(*) from audience_connections where profile_id = '{p1}');")
    finally:
        e2e_auth.drop_clones(*o.profiles)
    want = sorted([(a, "business", "owner", "owner"), (b, "personal", "owner", "owner"), (c, "personal", "default", "contact")])
    moment = (c1[1] or {}).get("connectedAt")
    ok = (c1[0] == 200 and (c1[1] or {}).get("restarted") is False and before == (200, moment, want)
          and st2 == 200 and w2 and (d2 or {}).get("adoptedProfiles") == [p1]
          and c2[0] == 200 and (c2[1] or {}).get("restarted") is False and (c2[1] or {}).get("connectedAt") == moment
          and after == (200, moment, want) and left == "0|0")
    return ok, {"connected#1": c1, "before": before, "create#2": (st2, (d2 or {}).get("adoptedProfiles")), "connected#2": c2,
                "after": after, "left_on_old(decisions|connection)": left}


@for_channels("delete_bot_wipes_decisions", "deleting the bot (Delete Bot Files from the app) deletes its chat decisions and its "
              "connection moment with the rest of its line; another profile's decisions stay")
def delete_bot(ctx, ch):
    o, p1, key, w1, setup = line_bot(ctx, ch, "dk")
    if not w1:
        return False, f"setup create: {setup}"
    other = dedicated(ctx, ch, "dko")
    keep = other.chat("k")
    fixture("set-audience", "--profile-id", other.pid, "--chat-id", keep, "--kind", "personal")
    try:
        paired(p1, ch, OWN[ch])
        audience_connected(o, p1)
        a, b = chat_id(ch, ctx, "dka"), chat_id(ch, ctx, "dkb")
        audience_set(o, p1, (a, "business"), (b, "personal"))
        psql(f"insert into chat_audience (profile_id, chat_id, kind, source, reason) values ('{p1}', '{chat_id(ch, ctx, 'dkc')}', "
             f"'personal', 'default', 'history');")
        before = psql(f"select (select count(*) from chat_audience where profile_id = '{p1}') || '|' || "
                      f"(select count(*) from audience_connections where profile_id = '{p1}');")
        ids = {"botWaId": w1 if ch == "wa" else "-1", "botTgId": w1 if ch == "tg" else "-1",
               "waProfileId": p1 if ch == "wa" else "-1", "tgProfileId": p1 if ch == "tg" else "-1", "appUserId": o.app_user_id}
        st, d = e2e_auth.hook("DeleteBotFiles", o.token, ids)
        after = psql(f"select (select count(*) from chat_audience where profile_id = '{p1}') || '|' || "
                     f"(select count(*) from audience_connections where profile_id = '{p1}');")
    finally:
        e2e_auth.drop_clones(*o.profiles)
    counters = {k: (d or {}).get(k) for k in ("success", "deletedAudience", "deletedConnections")}
    ok = (before == "3|1" and st == 200 and counters == {"success": True, "deletedAudience": 3, "deletedConnections": 1}
          and after == "0|0" and decision(other.pid, keep) == "personal|owner|owner")
    return ok, {"before(decisions|connection)": before, "delete": (st, counters), "after": after,
                "another_profile": decision(other.pid, keep)}


@for_channels("number_change_voids_defaults", "another number connected to the same profile (the app's connected report; the "
              "server reads the number from get/status) restarts the connection: restarted true, the computed decisions stop "
              "holding (gone from the list; the next message of such a chat is undecided again — inside the new first minutes, "
              "no reply), the owner's decision holds (its chat is still answered)")
def number_change(ctx, ch):
    s = Stand(ctx, ch, "nc")
    o = own_profile(ctx, s.pid)
    computed, contact, owners = s.chat("c"), s.chat("k"), s.chat("o")
    psql(f"insert into chat_audience (profile_id, chat_id, kind, source, reason, decided_at) values "
         f"('{s.pid}', '{computed}', 'business', 'default', 'new', now() - interval '1 hour'), "
         f"('{s.pid}', '{contact}', 'personal', 'default', 'contact', now() - interval '1 hour') "
         f"on conflict (profile_id, chat_id) do nothing;")
    fixture("set-audience", "--profile-id", s.pid, "--chat-id", owners, "--kind", "business")
    other_number = "77020000077"
    paired(s.pid, ch, other_number)
    st, d = audience_connected(o, s.pid)
    lst = decisions_of(o, s.pid)
    t = int(time.time())
    s.cards(stranger(ch, computed))
    s.mark()
    m1 = f"nc1-{ctx.run_id}"
    s.history(computed, [row(ch, computed, m1, False, "Вы получили моё сообщение?", t)])
    s.post(computed, m1, "Вы получили моё сообщение?", t)
    e1 = s.one(60)
    s.mark()
    m2 = f"nc2-{ctx.run_id}"
    s.history(owners, [row(ch, owners, m2, False, "Сколько стоит букет из 25 роз?", t + 1)])
    s.post(owners, m2, "Сколько стоит букет из 25 роз?", t + 1)
    e2 = s.one()
    conn = psql(f"select phone || '|' || (now() - connected_at < interval '5 minutes') from audience_connections where profile_id = '{s.pid}';")
    mine = [x for x in lst[2] if x[0] in (computed, contact, owners)]
    # the staging profile also holds the owner's decisions of earlier scenarios: after another number every decision that
    # still holds is an owner's
    ok = (st == 200 and (d or {}).get("restarted") is True and conn == f"{other_number}|true"
          and lst[0] == 200 and mine == [(owners, "business", "owner", "owner")] and all(x[2] == "owner" for x in lst[2])
          and e1.get("outcome") == "personal_settling" and not s.sends(computed)
          and e2.get("outcome") == "sent" and len(s.sends(owners)) == 1)
    return ok, {"connected": (st, d), "connection(phone|fresh)": conn, "list(this scenario's chats)": mine,
                "list_sources": sorted({x[2] for x in lst[2]}), "voided_chat_next_message": summary(e1),
                "owners_chat": summary(e2), "sends": (len(s.sends(computed)), len(s.sends(owners)))}


# ================================================================================================ run
READY_PATHS = ("ChatAudience", WA_PROFILE, TG_PROFILE)


def registered(path):
    """Whether n8n serves the POST webhook `path` now: a GET answers «Did you mean to make a POST request?» (no execution).
    n8n still starting — a refused connection, a timeout, a page that is not JSON — is «not yet», never the end of the run."""
    try:
        return "Did you mean" in str(http("GET", f"{BASE}/webhook/{path}", timeout=10)[1] or {})
    except (OSError, ValueError):
        return False


def wait_ready(timeout=120):
    """bootstrap.py reports «ready» while n8n is still registering the production webhooks: wait until the ChatAudience
    webhook and both staging bots are registered. Returns the paths still not registered."""
    deadline = time.time() + timeout
    while True:
        missing = [p for p in READY_PATHS if not registered(p)]
        if not missing or time.time() > deadline:
            return missing
        time.sleep(2)


def sweep_stale_mock_profiles():
    st, state = http("GET", f"{MOCK}/__mock/state")
    live = sorted((state or {}).get("profiles", {}).keys()) if st == 200 else None
    if live is None:
        raise SystemExit(f"Wappi mock not reachable at {MOCK} ({st})")
    print("   pre-run:", fixture("sweep-mock-profiles", *[x for p in live for x in ("--live", p)]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--channel", choices=["wa", "tg"])
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for id_, title, _ in SCENARIOS:
            print(f"{id_:52} {title[:110]}")
        return
    st, _ = http("GET", f"{BASE}/healthz")
    if st != 200:
        sys.exit(f"staging n8n not reachable at {BASE}")
    st, _ = mock("GET", "/__mock/state")
    if st != 200:
        sys.exit(f"wappi mock not reachable at {MOCK}")
    missing = wait_ready()
    if missing:
        print(f"   not registered after the wait: {missing} — the scenarios that need them will fail", flush=True)
    mock("POST", "/__mock/reset")
    sweep_stale_mock_profiles()
    ctx = Ctx()
    ctx.identity()
    passed = failed = 0
    try:
        for id_, title, fn in SCENARIOS:
            if a.only and a.only not in id_:
                continue
            if a.channel and f"[{a.channel}]" not in id_:
                continue
            t0 = time.time()
            try:
                ok, detail = fn(ctx)
            except Exception as e:
                ok, detail = False, f"EXCEPTION {e!r}"
            passed += ok
            failed += (not ok)
            print(f"{'PASS' if ok else 'FAIL'}  {id_} ({time.time() - t0:.0f}s) — {title}", flush=True)
            print(f"      {json.dumps(detail, ensure_ascii=False, default=str) if not isinstance(detail, str) else detail}", flush=True)
    finally:
        ctx.cleanup()
        for o in e2e_auth.OWNERS:
            try:
                fixture(*(["delete", "--app-user-id", o.app_user_id] + [x for p in o.profiles for x in ("--profile-id", p)]))
            except Exception as e:
                print(f"   cleanup {o.tag} failed:", e)
        print(f"   cleanup: {len(e2e_auth.OWNERS)} owner fixture(s)")
        e2e_auth.flush_deferred_deletes()
    print(f"\n{passed} pass, {failed} fail")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
