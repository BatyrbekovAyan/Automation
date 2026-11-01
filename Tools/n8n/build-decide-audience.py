#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §3.1–§3.3, §4.2,
§4.3; plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 3; live checks
docs/audits/2026-10-25-personal-chats/live-checks.md) — the sub-workflow the two bot templates call for an incoming message
of a chat that has no decision yet (the templates wire it in plan Task 4):

  crAudienceDec001-Decide_Audience.json   «Decide Audience», an Execute Workflow trigger; ONE item per execution (the
                                          template calls it once per incoming message — Plan Facts reads that item)
    in   exactly nine fields: channel (whatsapp | telegram), profileId, chatId (the incoming message's `from`), chatLid
         (WhatsApp: the incoming webhook's whatsapp_chat_id — the chat's code; '' on Telegram), messageId, chatType, isBot
         (boolean), connectedAtMs (number | null) and connectionPhone (the last two: Read Reply Mode's connection row). The
         calling Execute Workflow node checks the declared types (without type conversion a numeric string for the number
         field fails the call), so the caller hands Number() of Read Reply Mode's bigint — which arrives as text — or null
    out  ONE item {audience: business | personal, outcome: business | personal_settling | personal_unknown |
         personal_nonhuman | personal_contact | personal_history}; audience is business ONLY when the row Write Decision
         answered says business

    When Executed by Another Workflow → Has Connection? (connectedAtMs a positive safe integer)
        → [Plan Facts] | [Ensure Connection (audience.ENSURE_CONNECTION_SQL: the line's moment, no number) → Plan Facts]
    Plan Facts      the stops that need nothing — a malformed input or no moment (personal_unknown), the SETTLE_MINUTES window
                    (personal_settling) —, a chat that is not a person by its input alone (RULE_JS: a WhatsApp suffix, the
                    Telegram service account, a Telegram chat type that is not a person, the webhook's is_bot, WhatsApp's
                    chat with oneself), and which facts the rest needs
    Can Decide?     a stop goes straight to Shape
    Need Card?      → Chat Card (Telegram: tapi/sync/chats/get?limit=20&offset=0&order=desc; WhatsApp:
                    api/sync/chats/filter?client_name=<the digits of the chat id>; no card for a chat known only by its
                    @lid code) → Read Card (the dialog whose id IS the chat id: Telegram's flags, contact and type,
                    WhatsApp's contact.FullName — never the webhook's contact_name)
    Need History?   (asked only when the history decides whether the chat is a client) → Recent Messages (ONE
                    messages/get, order desc, audience.HISTORY_LIMIT rows — never order asc: tapi ignores it)
    Decide          audience.RULE_JS over the facts, the history read off that page (a row is the chat's by
                    audience.CHAT_IDENTITY_JS: the chat id, or WhatsApp's @lid code); persist = a known reason
    Persist? → Write Decision (audience.DECIDE_WRITE_SQL: once — a decision already written is answered back) →
    Became Business? (the ROW says business: kind business with a source; Decide hands on verdictKind / verdictReason,
    so the item a Write Decision that throws before its statement hands on is never taken for one) → Backfill
    (audience.BACKFILL_SQL: the newest audience.RECENT_LIMIT rows of that same page, newest first, the trigger left to the
    template's normal path) → Shape
  Every Wappi read: fullResponse + neverError, 10 s, the WappiAuthToken credential, never mark_all (reading must not mark
  the chat read). Every statement continues on error; Shape judges the rows' positive fields, never «the node continued».
  0DP79STI3pafXO9B-Set_Workflow_State.json  crAudienceDec001 joins the protected infra ids. The other dependents of
      build-client-webhooks.PROTECTED_IDS — Create WA/TG (apply-clone-lifecycle.py), Delete Bot Files (apply-auth-gate.py +
      apply-history-handoff.py) and the Profile Lifecycle Sweep (apply-sweep-clone-deactivate.py) — take it from there.

The tables: sql/2026-10-25-chat-audience.sql. Successful executions are not saved: the chat list and the page it reads are
the owner's chats.

    python3 Tools/n8n/build-decide-audience.py            # write the workflow + patch Set Workflow State (re-runnable)
    python3 Tools/n8n/build-decide-audience.py --check    # exit 2 when anything is stale
Pinned by verify-personal-chats.py (group `decide`, with its own tripwire) and tests/test_decide_audience.py (the whole graph
walked on real SQL, n8n's own expression engine and a stubbed Wappi that answers asc the way Telegram's tapi really does).
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
sys.path.insert(0, HERE)
import audience as au  # noqa: E402  (the rule, the constants and the statements — one home; never copied here)


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


auth = _module("build_auth", "build-auth-workflows.py")
AHH = _module("apply_history_handoff", "apply-history-handoff.py")      # MEMORY_ROW, OWNER_CONTENT_SQL (BACKFILL_SQL's rows)

WF_ID, WF_NAME = "crAudienceDec001", "Decide Audience"
WF_FILE = f"{WF_ID}-Decide_Audience.json"
SET_STATE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
PROTECTED_ANCHOR = "  'crChatAudience01'\n]);"
TRIGGER = "When Executed by Another Workflow"
# The template's item, trimmed by the trigger to exactly these (a field the caller leaves out arrives as null). No
# contactName: the webhook's contact_name is no contact fact — Wappi fills it from the person's own profile too.
INPUTS = (("channel", "string"), ("profileId", "string"), ("chatId", "string"), ("chatLid", "string"),
          ("messageId", "string"), ("chatType", "string"), ("isBot", "boolean"), ("connectedAtMs", "number"),
          ("connectionPhone", "string"))
WAPPI_TIMEOUT_MS = 10000
TG_CARD_LIMIT = 20          # the first page of Telegram's chat list, newest first: the chat that is writing is on it
WA_LID_SUFFIX = au.CHAT_CODE_SUFFIX   # a WhatsApp chat known only by its code: chats/filter does not search codes
WA_BASE, TG_BASE = "https://wappi.pro/api/", "https://wappi.pro/tapi/"


def vid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "decide-audience:" + name))


def _fill(js):
    return (js.replace("__RULE_JS__", au.RULE_JS).replace("__CHAT_IDENTITY_JS__", au.CHAT_IDENTITY_JS)
            .replace("__SETTLE_MINUTES__", str(au.SETTLE_MINUTES)).replace("__RECENT_LIMIT__", str(au.RECENT_LIMIT))
            .replace("__HISTORY_LIMIT__", str(au.HISTORY_LIMIT)).replace("__LID_SUFFIX__", json.dumps(WA_LID_SUFFIX)))


# ------------------------------------------------------------------------------------------------------------ the moment
HAS_CONNECTION_EXPR = ("={{ typeof $json.connectedAtMs === 'number' && Number.isSafeInteger($json.connectedAtMs) "
                       "&& $json.connectedAtMs > 0 }}")
ENSURE_PARAMS = "={{ [ $json.profileId ] }}"

PLAN_FACTS_JS = _fill(r"""// DECIDE AUDIENCE / Plan Facts (generated by Tools/n8n/build-decide-audience.py — edit there, never the JSON).
// What the chat needs before the rule can decide it. The input is the template's item, trimmed to the nine fields:
// channel, profileId, chatId (the incoming message's `from`), chatLid (WhatsApp: the incoming webhook's whatsapp_chat_id —
// the code the chat's messages may be listed under; '' on Telegram), messageId, chatType, isBot, connectedAtMs and
// connectionPhone (Read Reply Mode's connection row: null when it had none — then Ensure Connection ran first, and its row
// gives the moment and the number). Stops, with no Wappi call and nothing written (Can Decide? sends them to Shape):
//   personal_unknown   a malformed input (an unknown channel, a sentinel profile, no chat or message id) or no moment;
//   personal_settling  the channel was connected less than SETTLE_MINUTES ago: what Wappi can read of the account may still
//                      be incomplete, and a decision taken on it would stand for good.
// A chat that is not a person by its input alone goes to Decide without a call — RULE_JS on the input facts: a WhatsApp
// group / channel / status suffix, the Telegram service account, a Telegram chat type that is not a person (a bot's reply
// arrives as chat_type bot), the webhook's is_bot, WhatsApp's chat with oneself (the digits of the chat id are the
// connected number's). Otherwise:
//   Telegram  the chat card gives the flags, the contact and the chat type (Read Card);
//   WhatsApp  the chat card, filtered by the digits of the chat id, gives the contact (Read Card) — except for a chat known
//             only by its code (an id ending in @lid): the filter does not search codes, so its contact is false and its
//             history decides.
// The history is asked only when it decides whether the chat is a client: RULE_JS with history false says business.
__RULE_JS__
const SETTLE_MINUTES = __SETTLE_MINUTES__;
const LID_SUFFIX = __LID_SUFFIX__;
const inp = $('When Executed by Another Workflow').first().json || {};
const text = (v) => (typeof v === 'string' ? v : '');
const digits = (s) => s.replace(/[^0-9]/g, '');
const channel = inp.channel === 'whatsapp' || inp.channel === 'telegram' ? inp.channel : '';
const profileId = text(inp.profileId), chatId = text(inp.chatId), messageId = text(inp.messageId);
const chatLid = channel === 'whatsapp' ? text(inp.chatLid) : '';
let moment = inp.connectedAtMs;
let phone = text(inp.connectionPhone);
if ($('Ensure Connection').isExecuted) {
  const row = $('Ensure Connection').first().json || {};
  const ms = row.connected_at_ms;          // a bigint: Postgres hands it on as text
  moment = (ms === null || ms === undefined || ms === '') ? NaN : Number(ms);
  phone = text(row.phone);
}
const plan = { go: false, outcome: 'personal_unknown', channel, profileId, chatId, chatLid, messageId,
               connectedAtMs: (Number.isSafeInteger(moment) && moment > 0) ? moment : null,
               clientName: '', facts: null, needCard: false, needHistory: false };
const done = () => [{ json: plan, pairedItem: { item: 0 } }];
if (!channel || !profileId || profileId === '-1' || !chatId || !messageId || plan.connectedAtMs === null) return done();
if (Date.now() < plan.connectedAtMs + SETTLE_MINUTES * 60000) {
  plan.outcome = 'personal_settling';
  return done();
}
const own = digits(phone);
const self = channel === 'whatsapp' && own !== '' && digits(chatId.split('@')[0]) === own;
const given = { channel, chatId, chatType: text(inp.chatType), flags: { bot: inp.isBot === true, support: false, self },
                contact: null, history: null, owner: null };
const decides = (facts) => decideAudience(Object.assign({}, facts, { history: false })).kind === 'business';
Object.assign(plan, { go: true, outcome: '', facts: given });
if (decideAudience(given).reason === 'nonhuman') return done();
if (channel === 'telegram') {
  plan.facts = Object.assign({}, given, { flags: null });
  plan.needCard = true;
} else if (chatId.endsWith(LID_SUFFIX)) {
  plan.facts = Object.assign({}, given, { contact: false });
  plan.needHistory = decides(plan.facts);
} else {
  plan.clientName = digits(chatId.split('@')[0]);
  plan.needCard = plan.clientName !== '';
}
return done();""")
CAN_DECIDE_EXPR = "={{ $json.go === true }}"

# -------------------------------------------------------------------------------------------------------------- the card
NEED_CARD_EXPR = "={{ $json.needCard === true }}"
CARD_URL = "={{ $json.channel === 'telegram' ? '" + TG_BASE + "sync/chats/get' : '" + WA_BASE + "sync/chats/filter' }}"
CARD_QUERY = ("={{ JSON.stringify($json.channel === 'telegram' ? { profile_id: $json.profileId, limit: " + str(TG_CARD_LIMIT)
              + ", offset: 0, order: 'desc' } : { profile_id: $json.profileId, client_name: $json.clientName }) }}")

READ_CARD_JS = _fill(r"""// DECIDE AUDIENCE / Read Card (generated by Tools/n8n/build-decide-audience.py — edit there, never the JSON).
// In: Chat Card's answer for the chat Plan Facts named. The card counts only when Wappi answered 200 / done with a dialogs
// array that is THIS profile's (the top-level uuid, when given: Wappi crosses concurrent answers), and it is the dialog
// whose id is EXACTLY the chat id — WhatsApp's filter matches the digits in a number, a chat name and a last message alike;
// Telegram's page holds the twenty newest chats. No such dialog → the facts it would give stay unknown (Telegram: no
// flags; WhatsApp: no contact): personal, not written — the next message asks again.
//   Telegram  the user card's Bot / Support / Self (one that is there and not a boolean → no flags at all) and Contact (a
//             boolean, else not known); the dialog's own type is the chat type the rule reads;
//   WhatsApp  a saved contact is a name from the phone's address book — contact.FullName, not blank. The incoming
//             webhook's contact_name is no such fact: Wappi fills it FullName → PushName → BusinessName → FirstName, so a
//             stranger carries his own profile name there.
// Then, as in Plan Facts, the history is asked only when it decides whether the chat is a client.
__RULE_JS__
const plan = $('Plan Facts').first().json || {};
const res = $input.first().json || {};
const object = (v) => !!v && typeof v === 'object' && !Array.isArray(v);
const body = object(res.body) ? res.body : {};
const dialogs = Array.isArray(body.dialogs) ? body.dialogs : null;
const counted = Number(res.statusCode) === 200 && body.status === 'done' && dialogs !== null
  && !(typeof body.uuid === 'string' && body.uuid !== '' && body.uuid !== plan.profileId);
const idOf = (d) => (typeof d.id === 'string' || typeof d.id === 'number') ? String(d.id) : '';
const card = counted ? (dialogs.find((d) => object(d) && idOf(d) === plan.chatId) || null) : null;
const facts = Object.assign({}, plan.facts);
if (plan.channel === 'telegram') {
  const user = card && object(card.user) ? card.user : null;
  const flagOk = (v) => v === undefined || typeof v === 'boolean';
  const known = user !== null && flagOk(user.Bot) && flagOk(user.Support) && flagOk(user.Self);
  facts.flags = known ? { bot: user.Bot === true, support: user.Support === true, self: user.Self === true } : null;
  facts.contact = known && typeof user.Contact === 'boolean' ? user.Contact : null;
  facts.chatType = card && typeof card.type === 'string' ? card.type : '';
} else {
  const contact = card && object(card.contact) ? card.contact : null;
  facts.contact = contact === null ? null : (typeof contact.FullName === 'string' && contact.FullName.trim() !== '');
}
const needHistory = decideAudience(Object.assign({}, facts, { history: false })).kind === 'business';
return [{ json: Object.assign({}, plan, { facts, needHistory }), pairedItem: { item: 0 } }];""")

# ----------------------------------------------------------------------------------------------------------- the history
NEED_HISTORY_EXPR = "={{ $json.needHistory === true }}"
RECENT_URL = "={{ ($json.channel === 'telegram' ? '" + TG_BASE + "' : '" + WA_BASE + "') + 'sync/messages/get' }}"
RECENT_QUERY = ("={{ JSON.stringify({ profile_id: $json.profileId, chat_id: $json.chatId, limit: " + str(au.HISTORY_LIMIT)
                + ", order: 'desc' }) }}")

DECIDE_JS = _fill(r"""// DECIDE AUDIENCE / Decide (generated by Tools/n8n/build-decide-audience.py — edit there, never the JSON).
// The rule — audience.RULE_JS, embedded verbatim — over the facts, the history read off Recent Messages' page: ONE page of
// the chat's newest messages (order desc, at most HISTORY_LIMIT rows), never order asc — Telegram's tapi ignores asc and
// re-sorts the newest page, so «the oldest message» would be the incoming one itself and an old acquaintance would look
// new. The page counts only when Wappi answered 200 / done with a messages array that is THIS profile's (the top-level
// uuid, when given) and THIS chat's: a row belongs to the chat when it names the chat by its id or, on WhatsApp only, by the
// chat's @lid code (chatLid, the webhook's whatsapp_chat_id — WhatsApp lists the messages of some chats under it):
// chatIdentity, audience.CHAT_IDENTITY_JS embedded verbatim; a row naming any other chat makes the whole page nothing (Wappi
// crosses concurrent answers). On a page that counts:
//   it does not hold the incoming message (String(row.id) === messageId)  → stale (Wappi indexes a new chat late): unknown;
//   a row, in either direction, older than the connection moment           → history true;
//   a row without a usable time                                            → unknown (it may be the old one);
//   fewer than HISTORY_LIMIT rows                                          → the page is the whole chat: history false;
//   HISTORY_LIMIT rows, all newer than the connection                      → unknown.
// Not asked, or a page that does not count → unknown. persist = the rule's reason is not unknown. The page is larger than
// what the backfill stores: the newest RECENT_LIMIT rows of it go on exactly as Wappi returned them (newest first, unsorted)
// — the start of the conversation a chat that turns business gets — and the rest only decided the history.
// The verdict leaves as verdictKind / verdictReason — never kind, reason or source, the fields of Write Decision's row.
// When Write Decision throws before its statement it hands THIS item on unchanged, and nothing in it may read as a row.
__RULE_JS__
__CHAT_IDENTITY_JS__
const HISTORY_LIMIT = __HISTORY_LIMIT__;
const RECENT_LIMIT = __RECENT_LIMIT__;
const plan = ($('Read Card').isExecuted ? $('Read Card') : $('Plan Facts')).first().json || {};
const facts = Object.assign({}, plan.facts, { history: null });
let page = [];
if ($('Recent Messages').isExecuted) {
  const res = $('Recent Messages').first().json || {};
  const body = (res.body && typeof res.body === 'object' && !Array.isArray(res.body)) ? res.body : {};
  const rows = Array.isArray(body.messages) ? body.messages : null;
  const chat = chatIdentity(plan.channel, plan.chatId, plan.chatLid);
  const ours = (m) => !(m && typeof m === 'object' && typeof m.chatId === 'string' && m.chatId !== '' && !chat.names(m.chatId));
  const counted = Number(res.statusCode) === 200 && body.status === 'done' && rows !== null
    && !(typeof body.uuid === 'string' && body.uuid !== '' && body.uuid !== plan.profileId) && rows.every(ours);
  if (counted) {
    page = rows.slice(0, RECENT_LIMIT);
    const time = (m) => (m && typeof m === 'object' ? Number(m.time) : NaN);
    const usable = (t) => Number.isFinite(t) && t > 0;
    const trigger = rows.some((m) => m && typeof m === 'object' && m.id !== undefined && m.id !== null
      && String(m.id) === plan.messageId);
    if (!trigger) facts.history = null;
    else if (rows.some((m) => usable(time(m)) && time(m) * 1000 < plan.connectedAtMs)) facts.history = true;
    else if (rows.some((m) => !usable(time(m)))) facts.history = null;
    else facts.history = rows.length < HISTORY_LIMIT ? false : null;
  }
}
const verdict = decideAudience(facts);
return [{ json: { profileId: plan.profileId, chatId: plan.chatId, messageId: plan.messageId, verdictKind: verdict.kind,
                  verdictReason: verdict.reason, persist: verdict.reason !== 'unknown', page }, pairedItem: { item: 0 } }];""")

# ------------------------------------------------------------------------------------------------- the write, the backfill
PERSIST_EXPR = "={{ $json.persist === true }}"
WRITE_PARAMS = "={{ [ $json.profileId, $json.chatId, $json.verdictKind, $json.verdictReason ] }}"
# Business only on Write Decision's ROW: DECIDE_WRITE_SQL answers kind, reason and source of the decision that holds, and
# a real row's source is 'default' or 'owner' (the table's check; a refused pair answers nulls). No node before it emits
# kind or source, so the item Write Decision hands on unchanged when it throws before its statement — Decide's — never
# passes. Shape reads the row by the same rule.
BECAME_BUSINESS_EXPR = "={{ $json.kind === 'business' && ($json.source === 'default' || $json.source === 'owner') }}"
BACKFILL_SQL = au.BACKFILL_SQL(AHH.MEMORY_ROW, AHH.OWNER_CONTENT_SQL)
# BACKFILL_SQL's own order (build-chat-audience.BACKFILL_PARAMS): profile, chat, the trigger id (left out: the template's
# normal path records it), the page as a JSON text — Decide's: the newest RECENT_LIMIT rows of the history page, exactly as
# Wappi returned them. No second messages/get.
BACKFILL_PARAMS = ("={{ [ $('Decide').first().json.profileId, $('Decide').first().json.chatId, "
                   "$('Decide').first().json.messageId, JSON.stringify($('Decide').first().json.page) ] }}")

SHAPE_JS = r"""// DECIDE AUDIENCE / Shape (generated by Tools/n8n/build-decide-audience.py — edit there, never the JSON).
// The sub-workflow's ONE output item {audience, outcome}: the calling template reads audience. Reached from four places,
// judged in this order:
//   Write Decision ran (Became Business? true through Backfill, or false): its row is the decision that holds after the
//     write, whoever wrote it — a concurrent first message of the chat may have written first, and its row wins. A row
//     is what carries a source ('default' or 'owner'): a failed write is not one — an error item, nulls (a refused
//     pair), or Decide's own item, which Write Decision hands on unchanged when it throws before its statement (it has
//     no kind and no source). Only a row with kind business answers business. A personal row says why: nonhuman /
//     contact / history; anything else — no row, the owner's own personal decision set meanwhile — is
//     personal_unknown: no reason this run computed. A failed Backfill changes nothing: the row said business.
//   Decide ran but its verdict was not written (Persist? false): a fact was not obtained → personal_unknown.
//   Otherwise Plan Facts stopped it (Can Decide? false): personal_settling or personal_unknown, as it says.
const done = (audience, outcome) => [{ json: { audience, outcome }, pairedItem: { item: 0 } }];
if ($('Write Decision').isExecuted) {
  const row = $('Write Decision').first().json || {};
  const written = row.source === 'default' || row.source === 'owner';
  if (written && row.kind === 'business') return done('business', 'business');
  if (written && row.kind === 'personal' && row.reason === 'nonhuman') return done('personal', 'personal_nonhuman');
  if (written && row.kind === 'personal' && row.reason === 'contact') return done('personal', 'personal_contact');
  if (written && row.kind === 'personal' && row.reason === 'history') return done('personal', 'personal_history');
  return done('personal', 'personal_unknown');
}
if ($('Decide').isExecuted) return done('personal', 'personal_unknown');
const plan = $('Plan Facts').first().json || {};
return done('personal', plan.outcome === 'personal_settling' ? 'personal_settling' : 'personal_unknown');"""


# ---------------------------------------------------------------------------------------------------------------- graph
def own(n):
    n["id"] = vid(n["name"])
    return n


def trigger(pos):
    return {"parameters": {"inputSource": "workflowInputs",
                           "workflowInputs": {"values": [{"name": k, "type": t} for k, t in INPUTS]}},
            "id": vid("trigger"), "name": TRIGGER, "type": "n8n-nodes-base.executeWorkflowTrigger", "typeVersion": 1.1,
            "position": pos}


def wappi_get(name, url, query, pos):
    return own(auth.http_wappi(name, "GET", url, pos, query_expr=query, timeout=WAPPI_TIMEOUT_MS))


def build():
    n, c = [], {}
    n.append(trigger([0, 300]))
    n.append(own(auth.if_true("Has Connection?", HAS_CONNECTION_EXPR, [220, 300])))
    n.append(own(auth.postgres("Ensure Connection", au.ENSURE_CONNECTION_SQL, ENSURE_PARAMS, [440, 460])))
    n.append(own(auth.code("Plan Facts", PLAN_FACTS_JS, [660, 300])))
    n.append(own(auth.if_true("Can Decide?", CAN_DECIDE_EXPR, [880, 300])))
    n.append(own(auth.if_true("Need Card?", NEED_CARD_EXPR, [1100, 200])))
    n.append(wappi_get("Chat Card", CARD_URL, CARD_QUERY, [1320, 100]))
    n.append(own(auth.code("Read Card", READ_CARD_JS, [1540, 100])))
    n.append(own(auth.if_true("Need History?", NEED_HISTORY_EXPR, [1760, 200])))
    n.append(wappi_get("Recent Messages", RECENT_URL, RECENT_QUERY, [1980, 100]))
    n.append(own(auth.code("Decide", DECIDE_JS, [2200, 200])))
    n.append(own(auth.if_true("Persist?", PERSIST_EXPR, [2420, 200])))
    n.append(own(auth.postgres("Write Decision", au.DECIDE_WRITE_SQL, WRITE_PARAMS, [2640, 100])))
    n.append(own(auth.if_true("Became Business?", BECAME_BUSINESS_EXPR, [2860, 100])))
    n.append(own(auth.postgres("Backfill", BACKFILL_SQL, BACKFILL_PARAMS, [3080, 0])))
    n.append(own(auth.code("Shape", SHAPE_JS, [3300, 400])))

    auth.edge(c, TRIGGER, "Has Connection?")
    auth.edge(c, "Has Connection?", "Plan Facts", 0)
    auth.edge(c, "Has Connection?", "Ensure Connection", 1)    # only without a moment: the row of the line's moment
    auth.edge(c, "Ensure Connection", "Plan Facts")
    auth.edge(c, "Plan Facts", "Can Decide?")
    auth.edge(c, "Can Decide?", "Need Card?", 0)
    auth.edge(c, "Can Decide?", "Shape", 1)                    # a stop: no call, nothing written
    auth.edge(c, "Need Card?", "Chat Card", 0)
    auth.edge(c, "Need Card?", "Need History?", 1)
    auth.edge(c, "Chat Card", "Read Card")
    auth.edge(c, "Read Card", "Need History?")
    auth.edge(c, "Need History?", "Recent Messages", 0)
    auth.edge(c, "Need History?", "Decide", 1)
    auth.edge(c, "Recent Messages", "Decide")
    auth.edge(c, "Decide", "Persist?")
    auth.edge(c, "Persist?", "Write Decision", 0)
    auth.edge(c, "Persist?", "Shape", 1)                       # a fact not obtained: personal, not written
    auth.edge(c, "Write Decision", "Became Business?")
    auth.edge(c, "Became Business?", "Backfill", 0)
    auth.edge(c, "Became Business?", "Shape", 1)
    auth.edge(c, "Backfill", "Shape")
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(WF_ID, WF_NAME, n, c, settings)


def text():
    return json.dumps(build(), indent=2, ensure_ascii=False) + "\n"


def protected_js_patched(js):
    """Set Workflow State's Validate with crAudienceDec001 appended to its protected list (None: nothing to change)."""
    if "const PROTECTED = new Set([" not in js or f"'{WF_ID}'" in js:
        return None
    assert js.count(PROTECTED_ANCHOR) == 1, "Set Workflow State: the protected list has an unexpected shape"
    return js.replace(PROTECTED_ANCHOR, f"  'crChatAudience01',\n  '{WF_ID}'\n]);")


def patch_set_state(write):
    path = os.path.join(WF, SET_STATE)
    wf = json.load(open(path, encoding="utf-8"))
    changed = False
    for nd in wf["nodes"]:
        new = protected_js_patched(nd["parameters"].get("jsCode", ""))
        if new is not None:
            nd["parameters"]["jsCode"] = new
            changed = True
    if changed and write:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def stale():
    out = []
    p = os.path.join(WF, WF_FILE)
    if not os.path.exists(p) or open(p, encoding="utf-8").read() != text():
        out.append(WF_FILE)
    wf = json.load(open(os.path.join(WF, SET_STATE), encoding="utf-8"))
    if not any(f"'{WF_ID}'" in nd["parameters"].get("jsCode", "") for nd in wf["nodes"]):
        out.append(SET_STATE)
    return out


def main():
    if "--check" in sys.argv:
        s = stale()
        print("decide audience:", "OK" if not s else f"STALE {s}")
        sys.exit(2 if s else 0)
    path = os.path.join(WF, WF_FILE)
    old = open(path, encoding="utf-8").read() if os.path.exists(path) else None
    new = text()
    if old != new:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(new)
    print(f"{'wrote' if old != new else 'up to date'}  {WF_FILE} ({len(build()['nodes'])} nodes)")
    print(f"{'patched' if patch_set_state(write=True) else 'up to date'}  {SET_STATE} (protected ids)")


if __name__ == "__main__":
    main()
