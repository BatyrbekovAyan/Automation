#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""«Личные и рабочие чаты» (2026-10-25; spec docs/superpowers/specs/2026-10-25-personal-chats-design.md §4.4, §4.5, §3.3
«Подключение», owner decision 19; plan docs/superpowers/plans/2026-10-25-personal-chats.md Task 7) — the app reads and writes
the per-chat decisions through ONE webhook:

  crChatAudience01-Chat_Audience.json   POST /webhook/ChatAudience   Authorization: Bearer <session token>
      Auth Gate (token ONLY: legacy false — a new endpoint has no old client; profileIds / profileId must be the token's
      profiles, the sentinels '-1' / '' pass) → Authorized? → Validate (the ids taken again from the body: the sentinels
      dropped, the rest re-intersected with the token's profiles, de-duplicated; the limits by op — 20 profiles, 50 chats;
      a chat id is a non-empty string of at most 128 characters without whitespace and never the reply mode's wildcard
      '*'; kind personal | business; version a positive safe integer) → Valid? → three Ifs on op:
    list       {op, profileIds}          → List (audience.LIST_SQL) → Shape List
               → 200 {success:true, profiles:[{profileId, connectedAt: unix ms | null, decisions:[{chatId, kind, source,
                 reason, version}]}]} — the decisions that HOLD, so the app applies the rule only to chats without one.
    set        {op, profileId, chats}    → Set (audience.OWNER_SET_SQL: version-guarded, a chat that is not a person
               refused) → Shape Set → Respond Set
               → 200 {success:true, results:[{chatId, applied, kind, version, refused: null | 'nonhuman'}]};
               and AFTER the answer is on the wire (the app never waits for any of it), for every chat that just turned
               BUSINESS (the statement's positive became_business): Replay Items → Chat Channel (the profile's channel: the
               request names none) → Has Channel? → Recent Messages (messages/get, limit 10, order desc, never mark_all;
               the page exactly as Wappi returns it, newest first) → Replay Plan (the page counts only when it is THIS
               profile's and THIS chat's — a crossed answer is nothing; the newest message is handed to the bot again
               only when it is the client's, its own `from` is the chat — never a group member's message, which the
               templates' group guard «If» from == chatId must keep dropping —, a message the bot answers from these
               fields, and at most FRESH_HOURS old)
               → Backfill (audience.BACKFILL_SQL: the start of the conversation into the history and the memory; the
               trigger is left out only when it will be handed on — then the replayed run records it) → Replay? (the
               plan's positive word AND the backfill's committed row) → Replay (POST to the bot's OWN webhook on
               localhost, the message in the incoming-webhook shape: it answers through its normal path — claim,
               debounce, Final Check, the send lease — reply_events dedupes a message already claimed, and the template
               itself decides «Авто» against «Выбрать»).
    connected  {op, profileId}           → Channel (the profile's live row) → Channel Known? → Status (get/status of
               that channel) → Phone (build-handoff-workflows.OWNER_PHONE_JS verbatim — Telegram's authorised account
               first; the answer counts only when it echoes THIS profile, the boolean authorized true is a pairing, false
               is «not authorized») → Phone OK? → Connected (audience.CONNECTED_SQL: another number restarts the moment)
               → Shape Connected → 200 {success:true, connectedAt, restarted} — restarted true only when this call moved
               the moment (a number change: the app shows «Кто из них клиенты?» again).
      Refusals: 401 / 403 from the gate; 400 bad_request + reason (a malformed body; connected: no live row of the
      profile); 409 not_authorized (Wappi says the channel is not paired); 502 wappi_unavailable (no answer, an HTTP
      error, a crossed answer, a flag that is not a boolean); 500 db_unavailable (every statement continues on error, so
      the Shape nodes judge the rows' positive fields — never «the node continued»). bigint columns reach the Shape nodes
      as text: every number of an answer leaves as a JSON number (or null).
  0DP79STI3pafXO9B-Set_Workflow_State.json  crChatAudience01 joins the protected infra ids. The other dependents of
      build-client-webhooks.PROTECTED_IDS — Create WA/TG (apply-clone-lifecycle.py), Delete Bot Files (apply-auth-gate.py +
      apply-history-handoff.py) and the Profile Lifecycle Sweep (apply-sweep-clone-deactivate.py) — take it from there.

The tables: sql/2026-10-25-chat-audience.sql (apply BEFORE importing this workflow). Successful executions are not saved.

    python3 Tools/n8n/build-chat-audience.py            # write the workflow + patch Set Workflow State (re-runnable)
    python3 Tools/n8n/build-chat-audience.py --check    # exit 2 when anything is stale
Pinned by verify-personal-chats.py (group `webhook`, with its own tripwire) and tests/test_chat_audience.py (the whole graph
walked on real SQL, n8n's own expression engine, a stubbed Wappi and bot webhook).
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
sys.path.insert(0, HERE)
import audience as au  # noqa: E402  (the statements, FRESH_HOURS and RECENT_LIMIT — one home; never copied here)


def _module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


auth = _module("build_auth", "build-auth-workflows.py")
AHH = _module("apply_history_handoff", "apply-history-handoff.py")      # MEMORY_ROW, OWNER_CONTENT_SQL (BACKFILL_SQL's rows)
HW = _module("build_handoff_workflows", "build-handoff-workflows.py")   # OWNER_PHONE_JS — the A5 owner-number rule, ONE text

WF_ID, WF_NAME = "crChatAudience01", "Chat Audience"
WF_FILE = f"{WF_ID}-Chat_Audience.json"
PATH = "ChatAudience"
SET_STATE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
PROTECTED_ANCHOR = "  'crSetSchedule001'\n]);"
RULE = {"profiles": ["profileIds", "profileId"], "workflows": [], "appUser": [], "legacy": False}
MAX_LIST_PROFILES = 20      # the app reads at most this many profiles per list (ChatAudienceRequest.MaxListProfiles)
MAX_SET_CHATS = 50          # … and sends at most this many chats per set (ChatAudienceRequest.MaxSetChats)
MAX_CHAT_ID = 128           # … and never a longer chat id (ChatAudienceRequest.MaxChatIdLength)
WILDCARD_CHAT = "*"         # reply_mode_flags' bot-wide row — names every chat, never one
WAPPI_TIMEOUT_MS = 10000
REPLAY_TIMEOUT_MS = 10000
WA_BASE, TG_BASE = "https://wappi.pro/api/", "https://wappi.pro/tapi/"
# The bot's own webhook, inside the n8n container (Create's API calls use the same host); the path is the profile id —
# the URL Create gives Wappi (Set Wappi Webhook), so the template's If (the URL's last segment = profile_id) holds.
REPLAY_BASE = "http://localhost:5678/webhook/"
# Request STARTS are staggered 300 ms apart (n8n's HTTP batching; a slow answer can still overlap the next request —
# Wappi crosses concurrent answers, which Replay Plan's echo checks catch). The same stagger keeps a set of many chats from
# starting many bot runs at once.
BATCH = {"batch": {"batchSize": 1, "batchInterval": 300}}
# The message types the bot answers from the fields a messages/get row carries: text (WhatsApp 'chat', Telegram 'text') and
# the media it answers with «напишите текстом» (the template's Ask to Send Text). Never a voice message ('ptt': the bot
# transcribes the file of the incoming webhook's file_link, which a messages/get row does not carry), a reaction, a poll, a
# call or a service row — those are not a client waiting for an answer, or not one the bot can read.
REPLAY_TYPES = ("chat", "text", "image", "video", "document", "audio", "sticker", "location", "vcard")
# The incoming webhook's chat_type of a private dialog (wa-docs.txt / tg-docs.txt «Webhook на входящее сообщение»): a chat
# the owner made business is a person's private dialog — the owner's set refuses everything that is not a person.
PRIVATE_CHAT_TYPE = {"whatsapp": "dialog", "telegram": "user"}


def vid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "chat-audience:" + name))


# ---------------------------------------------------------------------------------------------------------------- entry
VALIDATE_JS = r"""// CHAT AUDIENCE / Validate (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON).
// The gate already refused a profile id the token does not own. Here every id is taken again from the body: the sentinels
// ('-1', '') dropped, the rest re-intersected with the token's profiles (a statement only ever sees the caller's own). The
// limits are the op's: list at most __MAX_LIST__ profile ids (de-duplicated), set at most __MAX_SET__ chats. A chat id is
// a non-empty string of at most __MAX_CHAT__ characters with no whitespace, and never '__WILDCARD__' — the reply mode's
// bot-wide row, which names every chat and is no chat. kind is personal or business; version a positive safe integer
// (digits in a string are taken too, and leave as a number: the statement reads a JSON number). The first malformed field
// refuses the whole request, naming it.
const wh = $input.first().json || {};
const b = (wh.body && typeof wh.body === 'object' && !Array.isArray(wh.body)) ? wh.body : {};
const owned = new Set((wh.crAuth && wh.crAuth.profiles) || []);
const bad = (reason) => [{ json: { ok: false, error: 'bad_request', reason }, pairedItem: { item: 0 } }];
const good = (fields) => [{ json: Object.assign({ ok: true }, fields), pairedItem: { item: 0 } }];
const mine = (x) => typeof x === 'string' && x !== '' && x !== '-1' && owned.has(x);
const op = b.op;
if (op === 'list') {
  if (!Array.isArray(b.profileIds) || b.profileIds.length > __MAX_LIST__) return bad('profileIds');
  const ids = Array.from(new Set(b.profileIds.filter(mine)));
  if (ids.length === 0) return bad('profileIds');
  return good({ op, profileIds: ids });
}
if (op !== 'set' && op !== 'connected') return bad('op');
if (!mine(b.profileId)) return bad('profileId');
if (op === 'connected') return good({ op, profileId: b.profileId });
if (!Array.isArray(b.chats) || b.chats.length === 0 || b.chats.length > __MAX_SET__) return bad('chats');
const chats = [];
for (const c of b.chats) {
  if (!c || typeof c !== 'object' || Array.isArray(c)) return bad('chats');
  const id = c.chatId;
  if (typeof id !== 'string' || id === '' || id.length > __MAX_CHAT__ || /\s/.test(id) || id === '__WILDCARD__') return bad('chatId');
  if (c.kind !== 'personal' && c.kind !== 'business') return bad('kind');
  const v = c.version;
  const version = (Number.isSafeInteger(v) && v > 0) ? v
    : ((typeof v === 'string' && /^[1-9][0-9]{0,15}$/.test(v) && Number.isSafeInteger(Number(v))) ? Number(v) : 0);
  if (!version) return bad('version');
  chats.push({ chatId: id, kind: c.kind, version });
}
return good({ op, profileId: b.profileId, chats });
""".replace("__MAX_LIST__", str(MAX_LIST_PROFILES)).replace("__MAX_SET__", str(MAX_SET_CHATS)).replace(
    "__MAX_CHAT__", str(MAX_CHAT_ID)).replace("__WILDCARD__", WILDCARD_CHAT)

IS_LIST_EXPR = "={{ $json.op === 'list' }}"
IS_SET_EXPR = "={{ $json.op === 'set' }}"
IS_CONNECTED_EXPR = "={{ $json.op === 'connected' }}"

RESPOND_401_BODY = "={{ { success: false, error: 'unauthorized', reason: $json.crAuth.reason } }}"
RESPOND_401_CODE = "={{ String($json.crAuth.reason || '').startsWith('forbidden') ? 403 : 401 }}"
RESPOND_400_BODY = "={{ { success: false, error: 'bad_request', reason: $json.reason } }}"

# ----------------------------------------------------------------------------------------------------------------- list
LIST_PARAMS = "={{ [ JSON.stringify($json.profileIds) ] }}"

SHAPE_LIST_JS = r"""// CHAT AUDIENCE / Shape List (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON).
// List continues on error: a database fault reaches here as an item carrying `message` / `error`, an empty answer as one
// empty item — never as «the node continued». Success = exactly one row per asked profile, each naming its profile and
// carrying its decisions array. bigint columns arrive as text: connectedAt and every version leave as JSON numbers.
const v = $('Validate').first().json;
const rows = $input.all().map((i) => i.json).filter((r) => r && typeof r === 'object' && Object.keys(r).length > 0);
const asked = Array.isArray(v.profileIds) ? v.profileIds : [];
const ok = rows.length > 0 && rows.length === asked.length
  && rows.every((r) => r.error === undefined && typeof r.profile_id === 'string' && Array.isArray(r.decisions));
if (!ok) return [{ json: { success: false, error: 'db_unavailable', _status: 500 }, pairedItem: { item: 0 } }];
const num = (x) => (x === null || x === undefined || x === '' || !Number.isFinite(Number(x))) ? null : Number(x);
const profiles = rows.map((r) => ({
  profileId: r.profile_id,
  connectedAt: num(r.connected_at_ms),
  decisions: r.decisions.filter((d) => d && typeof d === 'object' && typeof d.chatId === 'string').map((d) => ({
    chatId: d.chatId, kind: d.kind, source: d.source, reason: d.reason, version: num(d.version) || 0 })),
}));
return [{ json: { success: true, profiles, _status: 200 }, pairedItem: { item: 0 } }];"""

# ------------------------------------------------------------------------------------------------------------------ set
SET_PARAMS = "={{ [ $json.profileId, JSON.stringify($json.chats) ] }}"

SHAPE_SET_JS = r"""// CHAT AUDIENCE / Shape Set (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON).
// Set continues on error: a database fault reaches here as an item carrying `message` / `error`. Success = exactly one
// well-formed row per DISTINCT chat of the request (the statement answers a chat named twice once): its chat id, the
// booleans applied and became_business, a kind. The version arrives as text and leaves as a JSON number; refused is the
// statement's word or null.
const v = $('Validate').first().json;
const rows = $input.all().map((i) => i.json).filter((r) => r && typeof r === 'object' && Object.keys(r).length > 0);
const asked = new Set((Array.isArray(v.chats) ? v.chats : []).map((c) => c.chatId)).size;
const ok = rows.length > 0 && rows.length === asked
  && rows.every((r) => r.error === undefined && typeof r.chat_id === 'string' && typeof r.applied === 'boolean'
    && typeof r.became_business === 'boolean' && (r.kind === 'personal' || r.kind === 'business'));
if (!ok) return [{ json: { success: false, error: 'db_unavailable', _status: 500 }, pairedItem: { item: 0 } }];
const results = rows.map((r) => ({ chatId: r.chat_id, applied: r.applied, kind: r.kind,
  version: Number.isFinite(Number(r.version)) ? Number(r.version) : 0, refused: typeof r.refused === 'string' ? r.refused : null }));
return [{ json: { success: true, results, _status: 200 }, pairedItem: { item: 0 } }];"""

REPLAY_ITEMS_JS = r"""// CHAT AUDIENCE / Replay Items (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON).
// After the answer is on the wire: one item per chat this set just turned BUSINESS — the statement's positive
// became_business (applied, business, and no business decision held before). None, or a set the answer called a failure:
// no item, nothing more runs.
const shaped = $('Shape Set').first().json || {};
if (shaped.success !== true) return [];
const v = $('Validate').first().json;
const seen = new Set();
const out = [];
for (const it of $('Set').all()) {
  const r = it.json || {};
  if (r.became_business !== true || r.applied !== true || r.kind !== 'business') continue;
  if (typeof r.chat_id !== 'string' || r.chat_id === '' || seen.has(r.chat_id)) continue;
  seen.add(r.chat_id);
  out.push({ json: { profileId: v.profileId, chatId: r.chat_id }, pairedItem: { item: 0 } });
}
return out;"""

CHAT_CHANNEL_SQL = """-- «Личные и рабочие чаты»: the ChatAudience webhook's set, after the answer — the channel of the profile whose chat just
-- turned business, so its recent messages are read from the right Wappi base. One row: the profile id and the chat id
-- handed back, and the channel of the profile's LIVE bot_profiles row (null without one: nothing is read then).
-- Parameters, in order: profile id, chat id.
select $1::text as profile_id, $2::text as chat_id,
       (select b.channel from bot_profiles b where b.profile_id = $1::text and b.deleted_at is null) as channel"""
CHAT_CHANNEL_PARAMS = "={{ [ $json.profileId, $json.chatId ] }}"
HAS_CHANNEL_EXPR = ("={{ ($json.channel === 'whatsapp' || $json.channel === 'telegram') && typeof $json.profile_id === 'string' "
                    "&& $json.profile_id !== '' && typeof $json.chat_id === 'string' && $json.chat_id !== '' }}")
RECENT_URL = "={{ ($json.channel === 'telegram' ? '" + TG_BASE + "' : '" + WA_BASE + "') + 'sync/messages/get' }}"
# the start of a conversation: the last messages Wappi still has (audience.RECENT_LIMIT — the page Decide Audience reads too)
RECENT_QUERY = ("={{ JSON.stringify({ profile_id: $json.profile_id, chat_id: $json.chat_id, limit: " + str(au.RECENT_LIMIT)
                + ", order: 'desc' }) }}")

REPLAY_PLAN_JS = r"""// CHAT AUDIENCE / Replay Plan (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON; runs PER
// ITEM — one chat that just turned business). In: Recent Messages' answer for the chat Chat Channel named.
// The page counts only when Wappi answered 200 / done with a messages array that is THIS profile's (the top-level uuid,
// when given) and THIS chat's (no row naming another chat) — Wappi crosses concurrent answers, and a crossed page must
// never reach another chat's history or memory. It goes to the backfill exactly as Wappi returned it (newest first),
// or as nothing.
// The trigger — the message handed to the bot again (spec §4.5) — is the NEWEST message of the page, only when: it is the
// client's (fromMe the boolean false — the owner did not write after it), its OWN `from` is the chat (a private chat's
// incoming message: the templates' only group guard, «If» from == chatId, drops everything else — a group member's
// message names the member there and is never handed on, whatever the chat's decision says), a message the bot answers
// from these fields (REPLAY_TYPES: text and the media it answers with «напишите текстом»; never a voice message, whose
// file a messages/get row does not carry), not deleted, with a string id, and at most FRESH_HOURS old. Otherwise nothing
// is handed on and the whole page — that message included — goes into the history and the memory through the backfill.
// «Авто» against «Выбрать» is not decided here: the template reads the chat's mode itself.
const FRESH_HOURS = __FRESH_HOURS__;
const REPLAY_TYPES = __REPLAY_TYPES__;
const PRIVATE_CHAT_TYPE = __PRIVATE_CHAT_TYPE__;

// The incoming-webhook message the bot template reads, built from ONE messages/get row (the only place the two shapes
// meet: the live check D6 confirms the row's keys, the shape of its `from` included). from is the row's OWN `from` — the
// caller hands on only a row whose `from` is the chat (Tools/tapi/SHAPES.md: a private chat's incoming row carries
// from == chatId; Telegram's documented row with a username there is not handed on until D6 settles it), so the
// template's group guard still sees the real sender. profile_id is the profile asked — a row carries none. chat_type is
// the channel's private dialog: only a person's chat can be made business.
function replayMessage(channel, profileId, chatId, row) {
  const message = {
    wh_type: 'incoming_message',
    profile_id: profileId,
    id: String(row.id),
    body: typeof row.body === 'string' ? row.body : '',
    type: String(row.type),
    from: String(row.from),
    chatId: chatId,
    time: Number(row.time),
    caption: row.caption === undefined ? null : row.caption,
    chat_type: PRIVATE_CHAT_TYPE[channel],
    is_bot: row.is_bot === true,
  };
  if (typeof row.contact_name === 'string') message.contact_name = row.contact_name;
  return message;
}

const req = $('Chat Channel').item.json;
const profileId = req.profile_id, chatId = req.chat_id, channel = req.channel;
const res = $json || {};
const body = (res.body && typeof res.body === 'object' && !Array.isArray(res.body)) ? res.body : {};
const rows = Array.isArray(body.messages) ? body.messages : null;
const foreign = (m) => !!(m && typeof m === 'object' && typeof m.chatId === 'string' && m.chatId !== '' && m.chatId !== chatId);
const pageOk = Number(res.statusCode) === 200 && body.status === 'done' && rows !== null
  && !(typeof body.uuid === 'string' && body.uuid !== '' && body.uuid !== profileId) && !rows.some(foreign);
const page = pageOk ? rows : [];
const newest = page.length > 0 && page[0] && typeof page[0] === 'object' ? page[0] : null;
const time = newest ? Number(newest.time) : NaN;
let why = 'replay';
if (!pageOk) why = 'no_page';
else if (!newest) why = 'empty';
else if (newest.fromMe !== false) why = 'not_from_client';
else if (String(newest.from) !== chatId) why = 'not_from_chat';
else if (typeof newest.id !== 'string' || newest.id === '' || REPLAY_TYPES.indexOf(newest.type) < 0
         || newest.isDeleted === true || newest.is_deleted === true) why = 'not_replayable';
else if (!(Number.isFinite(time) && time > 0 && time >= Math.floor(Date.now() / 1000) - FRESH_HOURS * 3600)) why = 'stale';
const replay = why === 'replay';
return { json: { profileId, chatId, channel, page, triggerId: replay ? newest.id : '', replay, why,
                 message: replay ? replayMessage(channel, profileId, chatId, newest) : null } };""".replace(
    "__FRESH_HOURS__", str(au.FRESH_HOURS)).replace("__REPLAY_TYPES__", json.dumps(list(REPLAY_TYPES))).replace(
    "__PRIVATE_CHAT_TYPE__", json.dumps(PRIVATE_CHAT_TYPE))

BACKFILL_SQL = au.BACKFILL_SQL(AHH.MEMORY_ROW, AHH.OWNER_CONTENT_SQL)
BACKFILL_PARAMS = "={{ [ $json.profileId, $json.chatId, $json.triggerId, JSON.stringify($json.page) ] }}"
# Only the backfill's committed row (a number of added rows) together with the plan's positive word hands the message on —
# a failed backfill (a database fault) hands on nothing: the bot would answer without the start of the conversation.
REPLAY_EXPR = "={{ typeof $json.added === 'number' && $('Replay Plan').item.json.replay === true }}"
REPLAY_URL = "={{ '" + REPLAY_BASE + "' + encodeURIComponent($('Replay Plan').item.json.profileId) }}"
REPLAY_BODY = "={{ JSON.stringify({ messages: [ $('Replay Plan').item.json.message ] }) }}"

# ------------------------------------------------------------------------------------------------------------ connected
CHANNEL_SQL = """-- «Личные и рабочие чаты»: the ChatAudience webhook's connected — which Wappi base to ask for the profile's status. ALWAYS
-- one row: the channel of the profile's LIVE bot_profiles row, null without one (the request names no live profile).
-- Parameter: the profile id.
select (select b.channel from bot_profiles b where b.profile_id = $1::text and b.deleted_at is null) as channel"""
CHANNEL_PARAMS = "={{ [ $json.profileId ] }}"
CHANNEL_KNOWN_EXPR = "={{ $json.channel === 'whatsapp' || $json.channel === 'telegram' }}"
STATUS_URL = "={{ ($json.channel === 'telegram' ? '" + TG_BASE + "' : '" + WA_BASE + "') + 'sync/get/status' }}"
STATUS_QUERY = "={{ JSON.stringify({ profile_id: $('Validate').first().json.profileId }) }}"

PHONE_JS = r"""// CHAT AUDIENCE / Phone (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON).
// The connected number, read by the server itself from the profile's get/status — never sent by the app — by the A5 rule
// (build-handoff-workflows.OWNER_PHONE_JS, embedded verbatim: Telegram's authorised account first, else the top-level
// phone). Wappi's answer is a verdict only when it is HTTP 200 and echoes THIS profile (Wappi crosses concurrent answers):
// the boolean authorized true is a pairing, the boolean false is «not authorized» (409); anything else — no answer, an
// error, a crossed answer, a flag that is not a boolean — is no verdict (502). A pairing without a usable number still
// records the connection: the moment is kept, the number stays unknown.
__OWNER_PHONE_JS__
const v = $('Validate').first().json;
const ch = $('Channel').first().json || {};
const r = $json || {};
const status = (r.body && typeof r.body === 'object' && !Array.isArray(r.body)) ? r.body : {};
const wapi = ch.channel === 'telegram' ? 'tapi' : 'api';
let error = '';
if (Number(r.statusCode) !== 200) error = 'wappi_unavailable';
else if (status.profile_id !== v.profileId) error = 'wappi_unavailable';
else if (status.authorized === false) error = 'not_authorized';
else if (status.authorized !== true) error = 'wappi_unavailable';
return [{ json: { ok: !error, error, profileId: v.profileId, phone: error ? '' : ownerPhone(wapi, status) }, pairedItem: { item: 0 } }];
""".replace("__OWNER_PHONE_JS__", HW.OWNER_PHONE_JS)
PHONE_OK_EXPR = "={{ $json.ok === true }}"
CONNECTED_PARAMS = "={{ [ $json.profileId, $json.phone ] }}"

SHAPE_CONNECTED_JS = r"""// CHAT AUDIENCE / Shape Connected (generated by Tools/n8n/build-chat-audience.py — edit there, never the JSON).
// Reached from three places, judged in their order:
//   Channel (always one row, a `channel` column): an item without that column is a database fault (500); a null channel
//     is no live row of the profile (400 profileId) — Wappi is never asked then;
//   Phone (its verdict): not paired → 409 not_authorized, no verdict → 502 wappi_unavailable — nothing written;
//   Connected (continues on error): success only on ONE row with a positive connected_at_ms (bigint, as text) and a
//     boolean restarted — connectedAt leaves as a JSON number.
const done = (body, status) => [{ json: Object.assign({}, body, { _status: status }), pairedItem: { item: 0 } }];
const ch = $('Channel').first().json || {};
if (ch.channel !== 'whatsapp' && ch.channel !== 'telegram') {
  const fault = ch.error !== undefined || ch.message !== undefined || !Object.prototype.hasOwnProperty.call(ch, 'channel');
  return fault ? done({ success: false, error: 'db_unavailable' }, 500) : done({ success: false, error: 'bad_request', reason: 'profileId' }, 400);
}
const phone = $('Phone').first().json || {};
if (phone.ok !== true) {
  return phone.error === 'not_authorized' ? done({ success: false, error: 'not_authorized' }, 409)
                                          : done({ success: false, error: 'wappi_unavailable' }, 502);
}
const rows = $input.all().map((i) => i.json).filter((x) => x && typeof x === 'object' && Object.keys(x).length > 0);
const row = rows.length === 1 ? rows[0] : null;
const ms = row && row.connected_at_ms !== null && row.connected_at_ms !== undefined ? Number(row.connected_at_ms) : NaN;
if (!row || row.error !== undefined || !(Number.isSafeInteger(ms) && ms > 0) || typeof row.restarted !== 'boolean') {
  return done({ success: false, error: 'db_unavailable' }, 500);
}
return done({ success: true, connectedAt: ms, restarted: row.restarted }, 200);"""


# ---------------------------------------------------------------------------------------------------------------- graph
def own(n):
    n["id"] = vid(n["name"])
    return n


def respond_400(pos):
    return {"parameters": {"respondWith": "json", "responseBody": RESPOND_400_BODY, "options": {"responseCode": 400}},
            "id": vid("Respond 400"), "name": "Respond 400", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5,
            "position": pos}


def wappi_get(name, url, query, pos):
    n = own(auth.http_wappi(name, "GET", url, pos, query_expr=query, timeout=WAPPI_TIMEOUT_MS))
    return n


def replay_node(pos):
    """POST to the bot's own webhook: no credential (a localhost call carries no Wappi token), neverError, 10 s."""
    return {"parameters": {"method": "POST", "url": REPLAY_URL, "sendBody": True, "specifyBody": "json", "jsonBody": REPLAY_BODY,
                           "options": {"timeout": REPLAY_TIMEOUT_MS, "batching": json.loads(json.dumps(BATCH)),
                                       "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}},
            "id": vid("Replay"), "name": "Replay", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2, "position": pos,
            "alwaysOutputData": True, "onError": "continueRegularOutput"}


def build():
    n, c = [], {}
    wh = auth.webhook(PATH, [0, 300])
    wh["id"], wh["webhookId"] = vid("webhook:" + PATH), vid("webhookId:" + PATH)
    n.append(wh)
    n.append(own(auth.code("Auth Gate", auth.gate_code(RULE), [220, 300])))
    n.append(own(auth.if_true("Authorized?", "={{ $json.crAuth.ok }}", [440, 300])))
    n.append(own(auth.respond("Respond 401", [660, 640], body=RESPOND_401_BODY, code_expr=RESPOND_401_CODE)))
    n.append(own(auth.code("Validate", VALIDATE_JS, [660, 300])))
    n.append(own(auth.if_true("Valid?", "={{ $json.ok }}", [880, 300])))
    n.append(respond_400([1100, 640]))
    n.append(own(auth.if_true("Is List?", IS_LIST_EXPR, [1100, 300])))
    n.append(own(auth.if_true("Is Set?", IS_SET_EXPR, [1320, 420])))
    n.append(own(auth.if_true("Is Connected?", IS_CONNECTED_EXPR, [1540, 540])))
    # list
    n.append(own(auth.postgres("List", au.LIST_SQL, LIST_PARAMS, [1320, 100])))
    n.append(own(auth.code("Shape List", SHAPE_LIST_JS, [1540, 100])))
    n.append(own(auth.respond("Respond", [3300, 100])))
    # set + the tail after the answer
    n.append(own(auth.postgres("Set", au.OWNER_SET_SQL, SET_PARAMS, [1540, 320])))
    n.append(own(auth.code("Shape Set", SHAPE_SET_JS, [1760, 320])))
    n.append(own(auth.respond("Respond Set", [1980, 320])))
    n.append(own(auth.code("Replay Items", REPLAY_ITEMS_JS, [2200, 320])))
    n.append(own(auth.postgres("Chat Channel", CHAT_CHANNEL_SQL, CHAT_CHANNEL_PARAMS, [2420, 320])))
    n.append(own(auth.if_true("Has Channel?", HAS_CHANNEL_EXPR, [2640, 320])))
    recent = wappi_get("Recent Messages", RECENT_URL, RECENT_QUERY, [2860, 320])
    recent["parameters"]["options"]["batching"] = json.loads(json.dumps(BATCH))
    n.append(recent)
    plan = own(auth.code("Replay Plan", REPLAY_PLAN_JS, [3080, 320]))
    plan["parameters"]["mode"] = "runOnceForEachItem"
    n.append(plan)
    n.append(own(auth.postgres("Backfill", BACKFILL_SQL, BACKFILL_PARAMS, [3300, 320])))
    n.append(own(auth.if_true("Replay?", REPLAY_EXPR, [3520, 320])))
    n.append(replay_node([3740, 320]))
    # connected
    n.append(own(auth.postgres("Channel", CHANNEL_SQL, CHANNEL_PARAMS, [1760, 540])))
    n.append(own(auth.if_true("Channel Known?", CHANNEL_KNOWN_EXPR, [1980, 540])))
    n.append(wappi_get("Status", STATUS_URL, STATUS_QUERY, [2200, 540]))
    n.append(own(auth.code("Phone", PHONE_JS, [2420, 540])))
    n.append(own(auth.if_true("Phone OK?", PHONE_OK_EXPR, [2640, 540])))
    n.append(own(auth.postgres("Connected", au.CONNECTED_SQL, CONNECTED_PARAMS, [2860, 540])))
    n.append(own(auth.code("Shape Connected", SHAPE_CONNECTED_JS, [3080, 640])))

    auth.edge(c, "Webhook", "Auth Gate")
    auth.edge(c, "Auth Gate", "Authorized?")
    auth.edge(c, "Authorized?", "Validate", 0)
    auth.edge(c, "Authorized?", "Respond 401", 1)
    auth.edge(c, "Validate", "Valid?")
    auth.edge(c, "Valid?", "Is List?", 0)
    auth.edge(c, "Valid?", "Respond 400", 1)
    auth.edge(c, "Is List?", "List", 0)
    auth.edge(c, "Is List?", "Is Set?", 1)
    auth.edge(c, "Is Set?", "Set", 0)
    auth.edge(c, "Is Set?", "Is Connected?", 1)
    auth.edge(c, "Is Connected?", "Channel", 0)
    auth.edge(c, "Is Connected?", "Respond 400", 1)        # Validate admits only the three ops: never taken
    auth.edge(c, "List", "Shape List")
    auth.edge(c, "Shape List", "Respond")
    auth.edge(c, "Set", "Shape Set")
    auth.edge(c, "Shape Set", "Respond Set")
    auth.edge(c, "Respond Set", "Replay Items")            # the tail starts only once the answer is on the wire
    auth.edge(c, "Replay Items", "Chat Channel")
    auth.edge(c, "Chat Channel", "Has Channel?")
    auth.edge(c, "Has Channel?", "Recent Messages", 0)     # no live row: that chat's tail ends here
    auth.edge(c, "Recent Messages", "Replay Plan")
    auth.edge(c, "Replay Plan", "Backfill")
    auth.edge(c, "Backfill", "Replay?")
    auth.edge(c, "Replay?", "Replay", 0)
    auth.edge(c, "Channel", "Channel Known?")
    auth.edge(c, "Channel Known?", "Status", 0)
    auth.edge(c, "Channel Known?", "Shape Connected", 1)
    auth.edge(c, "Status", "Phone")
    auth.edge(c, "Phone", "Phone OK?")
    auth.edge(c, "Phone OK?", "Connected", 0)
    auth.edge(c, "Phone OK?", "Shape Connected", 1)
    auth.edge(c, "Connected", "Shape Connected")
    auth.edge(c, "Shape Connected", "Respond")
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return auth.workflow(WF_ID, WF_NAME, n, c, settings)


def text():
    return json.dumps(build(), indent=2, ensure_ascii=False) + "\n"


def protected_js_patched(js):
    """Set Workflow State's Validate with crChatAudience01 appended to its protected list (None: nothing to change)."""
    if "const PROTECTED = new Set([" not in js or f"'{WF_ID}'" in js:
        return None
    assert js.count(PROTECTED_ANCHOR) == 1, "Set Workflow State: the protected list has an unexpected shape"
    return js.replace(PROTECTED_ANCHOR, f"  'crSetSchedule001',\n  '{WF_ID}'\n]);")


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
        print("chat audience:", "OK" if not s else f"STALE {s}")
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
