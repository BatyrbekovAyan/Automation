#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""«Личные и рабочие чаты» — the rule that says whether a chat is PERSONAL or BUSINESS, in ONE place (spec
docs/superpowers/specs/2026-10-25-personal-chats-design.md §3, plan docs/superpowers/plans/2026-10-25-personal-chats.md).

The assistant answers, suggests and stores only in BUSINESS chats. A chat it cannot show to be a new client is PERSONAL: a
saved contact, a conversation that existed before the channel was connected, and everything that is not a person at all (a
group, a channel, a bot, the service account, the chat with oneself). The server decides ONCE — on the first incoming message
of a chat that has no decision yet — and writes the decision down; afterwards only the owner changes it. What is not known
(Wappi did not answer, the chat card was not on the page) is PERSONAL and is not written down: the next message asks again.

The rule — RULE_JS, `decideAudience(f)` → {kind: 'personal' | 'business', reason}; the first step that applies wins:
  1. not a person → personal / nonhuman
       WhatsApp  the chat id ends with one of WA_NONHUMAN_SUFFIXES (a group, a channel, the status and broadcast lists), or
                 flags.bot / flags.self (the chat with oneself);
       Telegram  the chat id is one of TG_SERVICE_IDS (777000, the service account), or the chat type is one of
                 TG_NONHUMAN_TYPES ('channel', 'chat' — a channel, a group), or any of flags.bot / flags.support / flags.self;
  2. Telegram without the chat card (flags null) → personal / unknown: a bot cannot be ruled out. WhatsApp has no card to
     miss, so there `flags: null` reads as all false;
  2a. Telegram with a chat type other than TG_PERSON_TYPE ('user') → personal / unknown. ANY other value counts: '' (not
     given), another spelling or case ('User'), a literal nobody has seen yet. Such a chat cannot be classified, and what
     cannot be classified is never written down; only the two known non-human types are decided (step 1). WhatsApp does not
     read the chat type at all;
  3. the owner's own decision ('personal' | 'business'), if there is one → {owner} / owner;
  4. a saved contact → personal / contact;
  5. a conversation before the channel was connected → personal / history;
  6. contact or history not known (null, or not given at all) → personal / unknown;
  7. otherwise → business / new. Only a KNOWN «no» on both facts makes a new person.
An id ending in '@lid' (a WhatsApp number that did not resolve) is a person like any other: its history tells. A channel that
is neither 'whatsapp' nor 'telegram' is not decided at all (personal / unknown) — the C# twin cannot even say it.

Twins: the n8n `Decide Audience` Code node embeds RULE_JS verbatim; the app's ChatAudienceRule.Decide repeats it in C#. Both run
every row of FIXTURE — {constants: {settleMinutes, freshHours, wipeDays}, cases: [{name, channel, chatId, chatType, flags,
contact, history, owner, expectKind, expectReason}]} — so a new case goes into that one file and nowhere else.
tests/test_audience_rule.py guards the rule, the table and the constants.

The decisions in SQL (plan Task 2; schema sql/2026-10-25-chat-audience.sql: chat_audience, audience_connections). The
statements the n8n nodes run over the two tables are written here once — except the two that belong to the bot's line:
the move on a reconnect is part of clone_lineage.py's BIND_WORKFLOW_SQL (Create WA/TG's Bind Workflow Id), the delete
with the bot part of apply-history-handoff.py's DELETE_NEW_TAIL (Delete Bot Files' Delete Chat Memory). The generators
paste them, never copy them:
  VALID_SQL(a, c)            «this decision holds»: the owner's always, a computed one only while taken on the current
                             connection (decided_at >= connected_at; another number moves connected_at);
  AUDIENCE_SQL(p, c)         the kind of the decision that holds, else NULL (no decision) — a scalar subquery;
  BUSINESS_SQL(p, c)         true only for a holding BUSINESS decision — what every writer and reader asks;
  LINE_CONNECTED_AT_SQL(p)   the line's moment: the earliest created_at along the superseded_by chain, now() without a row;
  ENSURE_CONNECTION_SQL      the profile's connection row, created with the line's moment when the app has not reported one;
  DECIDE_WRITE_SQL           a computed decision, written once (it replaces only a computed row that no longer holds);
  OWNER_SET_SQL              the owner's decisions, version-guarded, refused for a chat that is not a person;
  CONNECTED_SQL              the app reports a connection: another number restarts the moment;
  LIST_SQL                   what the app mirrors: the moment and the holding decisions of each profile;
  BACKFILL_SQL(row, owner)   when a chat turns business, the recent messages it lacks go into the history and the memory.
  WIPE_SQL                   the Sweep's step: WIPE_DAYS after the OWNER made a chat personal, what the assistant stored
                             about it is deleted (never dialog_counts) and the decision is marked wiped.
tests/test_audience_sql.py runs every one of them but the last on real SQL (staging Postgres), the race of two first messages
included; tests/test_personal_wipe.py runs WIPE_SQL (and the Sweep node that carries it) the same way.

    python3 Tools/n8n/audience.py              # print RULE_JS
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

SETTLE_MINUTES = 30     # after a channel is connected, a chat without a decision gets no reply, no suggestion and no decision
                        # for this long (what Wappi can read of the account's history may be incomplete at first)
FRESH_HOURS = 24        # a message that waited for the owner's decision is handed to the assistant again only while younger
WIPE_DAYS = 2           # the data of a chat the owner made personal is wiped after this many days
WIPE_BATCH = 200        # chats the Sweep wipes per run (every 6 hours); the oldest first, the rest wait for the next run
WA_CONTACT_SOURCE = "webhook"   # where the WhatsApp «saved contact» fact comes from: "webhook" (contact_name of the incoming
                                # message) | "chats_filter" (contact.FullName of the chat list); live check D1,
                                # docs/audits/2026-10-25-personal-chats/live-checks.md
WA_NONHUMAN_SUFFIXES = ("@g.us", "@newsletter", "@broadcast")   # a group; a channel; the status and broadcast lists
TG_SERVICE_IDS = ("777000",)                                    # the Telegram service account (login codes and the like)
TG_NONHUMAN_TYPES = ("channel", "chat")                         # Telegram chat types that are not a person: a channel; a group
TG_PERSON_TYPE = "user"                                         # the Telegram chat type of a private dialog — the only person
FIXTURE = os.path.join(HERE, "fixtures", "audience-cases.json")

# ASCII only on purpose: the same text is embedded in n8n Code nodes and mirrored in C#. Nothing here may read a clock, the
# network or an n8n global (tests/test_audience_rule.py: rule_js_is_pure), and the only name it leaves at the top level of a
# Code node is `decideAudience`. The lists and the person type are the module's constants above, written in below.
RULE_JS = r"""// AUDIENCE RULE - is this chat PERSONAL (the assistant stays out) or BUSINESS (a client)? ONE function, two runners:
// the Decide Audience Code node embeds this text verbatim (Tools/n8n/audience.py) and the app's ChatAudienceRule.Decide is its
// C# twin; both run every row of Tools/n8n/fixtures/audience-cases.json. Pure: no clock, no network, no n8n globals.
//
// f = { channel,   'whatsapp' | 'telegram'
//       chatId,    string
//       chatType,  string; Telegram: 'user' = a person, 'channel' | 'chat' = not a person, anything else ('' = not given
//                  included) = not recognised; WhatsApp: not used
//       flags,     { bot, support, self } | null; Telegram: null = the chat card was not received, WhatsApp: null = all false
//       contact,   true | false | null; a saved contact, null = not known
//       history,   true | false | null; a conversation before the connection, null = not known
//       owner }    null | 'personal' | 'business'; the owner's own decision, if there is one
// returns { kind: 'personal' | 'business', reason: 'nonhuman' | 'owner' | 'contact' | 'history' | 'new' | 'unknown' }
// A fact that is not given is not known: only a known "no" on both contact and history makes a new person.
function decideAudience(f) {
  const WA_NONHUMAN_SUFFIXES = __WA_NONHUMAN_SUFFIXES__;
  const TG_SERVICE_IDS = __TG_SERVICE_IDS__;
  const TG_NONHUMAN_TYPES = __TG_NONHUMAN_TYPES__;
  const TG_PERSON_TYPE = __TG_PERSON_TYPE__;
  const personal = (reason) => ({ kind: 'personal', reason: reason });
  const telegram = f.channel === 'telegram';
  // A channel that is neither is not decided: step 1 would not know which of its two halves applies.
  if (!telegram && f.channel !== 'whatsapp') return personal('unknown');
  const id = f.chatId == null ? '' : String(f.chatId);
  const type = typeof f.chatType === 'string' ? f.chatType : '';
  const flags = (f.flags && typeof f.flags === 'object') ? f.flags : null;
  const flag = (name) => !!(flags && flags[name]);

  // 1. Not a person: never a client, whatever the owner says.
  const nonhuman = telegram
    ? TG_SERVICE_IDS.indexOf(id) >= 0 || TG_NONHUMAN_TYPES.indexOf(type) >= 0 || flag('bot') || flag('support') || flag('self')
    : WA_NONHUMAN_SUFFIXES.some((suffix) => id.endsWith(suffix)) || flag('bot') || flag('self');
  if (nonhuman) return personal('nonhuman');
  // 2. Telegram without the chat card: a bot cannot be ruled out.
  if (telegram && flags === null) return personal('unknown');
  // 2a. Telegram with a chat type we do not recognise ('' included, any other spelling or case): it cannot be classified.
  if (telegram && type !== TG_PERSON_TYPE) return personal('unknown');
  // 3. The owner's own decision.
  if (f.owner === 'personal' || f.owner === 'business') return { kind: f.owner, reason: 'owner' };
  // 4-5. A saved contact, or a conversation that existed before the connection.
  if (f.contact) return personal('contact');
  if (f.history) return personal('history');
  // 6. Contact or history not known.
  if (f.contact !== false || f.history !== false) return personal('unknown');
  // 7. A new person.
  return { kind: 'business', reason: 'new' };
}"""
for _name, _value in (("WA_NONHUMAN_SUFFIXES", list(WA_NONHUMAN_SUFFIXES)), ("TG_SERVICE_IDS", list(TG_SERVICE_IDS)),
                      ("TG_NONHUMAN_TYPES", list(TG_NONHUMAN_TYPES)), ("TG_PERSON_TYPE", TG_PERSON_TYPE)):
    RULE_JS = RULE_JS.replace("__" + _name + "__", json.dumps(_value))


# ================================================================================================ the decisions, in SQL
# Every statement below is pasted into an n8n Postgres node, whose query pg-promise formats on the client: the V1 rule holds
# (verify-sql-comments.py) — no dollar sign followed by a digit inside a comment or a literal, no double braces, no
# pg-promise modifier; the parameters are named in words. The fragments take the caller's SQL expressions (a placeholder, a
# column) and keep aliases of their own (aud, aud_conn, aud_line, aud_lp, aud_lq) so they never capture a caller's alias.
# A JSON parameter is a JSON text (the generators JSON.stringify it); anything that is not an array of the expected
# elements is no element. Times leave as unix ms (`connected_at_ms`, a bigint — n8n's Postgres node may hand it over as a
# string, so a consumer reads it with Number()).

def _lit(s):
    """A Python string as a standard SQL literal — the module's constants written into SQL."""
    assert "$" not in s, "never a dollar sign in a Postgres node's literal (pg-promise formats the whole query text)"
    return "'" + s.replace("'", "''") + "'"


def _ms(expr):
    return "(extract(epoch from " + expr + ") * 1000)::bigint"


def _json_array(param):
    """A JSON-text parameter as a jsonb array — anything else (an object, a scalar, null) is the empty array."""
    return ("(case when jsonb_typeof(" + param + "::jsonb) = 'array' then " + param + "::jsonb else '[]'::jsonb end)")


def _real_profile(param):
    """A profile id that names a profile — never empty, never the app's '-1' sentinel (a channel without a profile)."""
    return "nullif(" + param + ", '') is not null and " + param + " <> '-1'"


def _nonhuman_id_sql(chat):
    """«This chat is not a person» by the id's SHAPE alone: a WhatsApp group / channel / status-or-broadcast suffix, the
    Telegram service account (the module's WA_NONHUMAN_SUFFIXES and TG_SERVICE_IDS). What only the chat card tells (a
    Telegram bot, a channel, the chat with oneself) reaches the table as a recorded reason 'nonhuman'."""
    parts = ["right(%s, %d) = %s" % (chat, len(s), _lit(s)) for s in WA_NONHUMAN_SUFFIXES]
    parts.append("%s in (%s)" % (chat, ", ".join(_lit(i) for i in TG_SERVICE_IDS)))
    return "(" + " or ".join(parts) + ")"


def VALID_SQL(a="a", c="c"):
    """«This decision holds» — `a` a chat_audience row, `c` the profile's audience_connections row (null-extended when
    there is none: then every decision holds). The owner's decision always holds; a computed one only while it was taken
    on the current connection — another number (CONNECTED_SQL) moves connected_at forward and so voids the older
    computed rows without touching them."""
    return "(" + a + ".source = 'owner' or " + a + ".decided_at >= coalesce(" + c + ".connected_at, '-infinity'))"


def AUDIENCE_SQL(profile="$1", chat="$2"):
    """The kind of the chat's decision that holds — 'personal' | 'business' — else NULL (no decision: the caller decides
    or treats the chat as not business). A scalar subquery; `profile` / `chat` are SQL expressions."""
    return ("(select aud.kind from chat_audience aud\n"
            "      left join audience_connections aud_conn on aud_conn.profile_id = aud.profile_id\n"
            "      where aud.profile_id = " + profile + " and aud.chat_id = " + chat + "\n"
            "        and " + VALID_SQL("aud", "aud_conn") + ")")


def BUSINESS_SQL(profile="$1", chat="$2"):
    """True only for a holding BUSINESS decision; a personal, a missing or a voided decision is false. Every writer of the
    history and the memory and every reader that feeds the AI asks this — never «not personal»."""
    return "coalesce(" + AUDIENCE_SQL(profile, chat) + " = 'business', false)"


def LINE_CONNECTED_AT_SQL(profile="$1"):
    """The moment the bot's line was first connected on this channel: the earliest bot_profiles.created_at among the profile
    and the profiles it superseded, transitively (an older row's superseded_by names the profile that took its place —
    clone_lineage's bind), of the same channel. A reconnect (a new profile of the line) keeps the moment; a bot deleted and
    created anew is a new line. now() when the profile has no row. UNION (not UNION ALL) makes a cycle terminate."""
    return ("coalesce((with recursive aud_line as (\n"
            "        select aud_lp.profile_id, aud_lp.channel, aud_lp.created_at from bot_profiles aud_lp\n"
            "        where aud_lp.profile_id = " + profile + "\n"
            "        union\n"
            "        select aud_lq.profile_id, aud_lq.channel, aud_lq.created_at from bot_profiles aud_lq\n"
            "        join aud_line on aud_lq.superseded_by = aud_line.profile_id and aud_lq.channel = aud_line.channel)\n"
            "      select min(aud_line.created_at) from aud_line), now())")


ENSURE_CONNECTION_SQL = """-- «Личные и рабочие чаты»: the moment this profile's channel counts as connected. The app reports each confirmed
-- connection (CONNECTED_SQL); when the first message of a chat comes before that report, the row is created here — the
-- line's moment, the number not known. ALWAYS one row {connected_at_ms, phone}. An existing row is handed back unchanged:
-- the conflict locks it and writes every column back as it is, which is what lets two first messages racing here both
-- read the one moment (a plain re-read in the same statement could not see a row the other run committed meanwhile — one
-- snapshot per statement). A sentinel profile ('' or the app's '-1') writes nothing and answers nulls.
-- Parameter: the profile id.
with ensured as (
  insert into audience_connections as c (profile_id, phone, connected_at)
  select $1, null, """ + LINE_CONNECTED_AT_SQL("$1") + """
  where """ + _real_profile("$1") + """
  on conflict (profile_id) do update set connected_at = c.connected_at
  returning c.connected_at, c.phone
)
select """ + _ms("(select connected_at from ensured)") + """ as connected_at_ms,
       (select phone from ensured) as phone"""


_DECIDE_STALE = "a.source = 'default' and not exists (select 1 from aud_conn c where " + VALID_SQL("a", "c") + ")"

DECIDE_WRITE_SQL = """-- «Личные и рабочие чаты»: write the computed (default) decision of a chat — once — and answer the decision that holds.
-- The pair must be one the rule gives: business only as new, personal as nonhuman / contact / history; a business
-- decision for a chat id that is not a person (a group, a channel, the service account) is never written. Anything else
-- writes nothing and answers nulls (the caller reads that as «not business»).
-- An existing row is replaced only when it is a computed one that no longer holds (taken before the current connection:
-- another number). The owner's row and a computed row that still holds are kept: the conflict locks them and writes every
-- column back unchanged, then hands them back. That is how the second of two first messages racing here reads the FIRST
-- one's decision: the conflict waits for the first insert to commit and returns the committed row — a re-read in the same
-- statement could not see it (one snapshot per statement), and a conflict that updates nothing returns nothing.
-- ALWAYS one row: kind, reason, source of the decision that holds after the write.
-- Parameters, in order: profile id, chat id, kind, reason.
with aud_conn as (
  select (select c0.connected_at from audience_connections c0 where c0.profile_id = $1) as connected_at
), decided as (
  insert into chat_audience as a (profile_id, chat_id, kind, source, reason, decided_at, updated_at)
  select $1, $2, $3, 'default', $4, now(), now()
  where """ + _real_profile("$1") + """ and nullif($2, '') is not null
    and (($3 = 'business' and $4 = 'new') or ($3 = 'personal' and $4 in ('nonhuman', 'contact', 'history')))
    and not ($3 = 'business' and """ + _nonhuman_id_sql("$2") + """)
  on conflict (profile_id, chat_id) do update set
    kind       = case when """ + _DECIDE_STALE + """ then excluded.kind else a.kind end,
    reason     = case when """ + _DECIDE_STALE + """ then excluded.reason else a.reason end,
    decided_at = case when """ + _DECIDE_STALE + """ then excluded.decided_at else a.decided_at end,
    updated_at = case when """ + _DECIDE_STALE + """ then excluded.updated_at else a.updated_at end
  returning a.kind, a.reason, a.source
)
select (select kind from decided) as kind, (select reason from decided) as reason, (select source from decided) as source"""


OWNER_SET_SQL = """-- «Личные и рабочие чаты»: the owner's decisions, several chats at once (the ChatAudience webhook's set). One answer row
-- per chat, in the order of the request (a chat named twice: once, at its first place, with its highest version):
-- {chat_id, applied, kind, version, refused, became_business}. kind / version are the server's after the statement —
-- the client adopts them when applied is false.
--   refused 'nonhuman'  the chat is not a person — by its id (a group, a channel, the status / broadcast list, the service
--                       account) or by a recorded reason nonhuman: never written, whatever kind was asked;
--   applied             no row yet, a computed row (even one that holds), or an owner row of an OLDER version;
--   personal_since      business (or no row) → personal stamps now; personal → personal keeps it; → business clears it;
--   wiped_at            cleared whenever personal_since changes and when the chat turns business;
--   became_business     applied, business, and the decision that held before was not business (no row, or a computed row
--                       that no longer holds, counts as not business) — the webhook then backfills the conversation.
-- Malformed elements (no chat id, a kind that is neither, a version that is not a positive integer) give no row.
-- Parameters, in order: profile id, JSON array of {chatId, kind, version}.
with req as (
  select e.x->>'chatId' as chat_id, e.x->>'kind' as kind,
         case when e.x->>'version' ~ '^[1-9][0-9]{0,15}$' then (e.x->>'version')::bigint end as version, e.ord
  from jsonb_array_elements(""" + _json_array("$2") + """) with ordinality as e(x, ord)
  where """ + _real_profile("$1") + """
    and jsonb_typeof(e.x) = 'object' and jsonb_typeof(e.x->'chatId') = 'string' and e.x->>'chatId' <> ''
    and e.x->>'kind' in ('personal', 'business')
    and jsonb_typeof(e.x->'version') = 'number' and e.x->>'version' ~ '^[1-9][0-9]{0,15}$'
), want as (
  select r.chat_id, (array_agg(r.kind order by r.version desc, r.ord desc))[1] as kind, max(r.version) as version,
         min(r.ord) as ord
  from req r group by r.chat_id
), cur as (
  select w.chat_id, w.kind, w.version, w.ord, a.kind as old_kind, a.version as old_version,
         coalesce(a.profile_id is not null and """ + VALID_SQL("a", "c") + """, false) as old_valid,
         (""" + _nonhuman_id_sql("w.chat_id") + """ or coalesce(a.reason = 'nonhuman', false)) as refused
  from want w
  left join chat_audience a on a.profile_id = $1 and a.chat_id = w.chat_id
  left join audience_connections c on c.profile_id = $1
), up as (
  insert into chat_audience as t (profile_id, chat_id, kind, source, reason, version, decided_at, personal_since, wiped_at,
                                  updated_at)
  select $1, u.chat_id, u.kind, 'owner', 'owner', u.version, now(), case when u.kind = 'personal' then now() end, null, now()
  from cur u where not u.refused
  on conflict (profile_id, chat_id) do update set
    kind = excluded.kind, source = 'owner', reason = 'owner', version = excluded.version, decided_at = excluded.decided_at,
    personal_since = case when excluded.kind = 'business' then null
                          when t.kind = 'personal' then t.personal_since
                          else excluded.decided_at end,
    wiped_at = case when excluded.kind = 'personal' and t.kind = 'personal' then t.wiped_at end,
    updated_at = excluded.updated_at
  where t.reason <> 'nonhuman' and (t.source = 'default' or t.version < excluded.version)
  returning t.chat_id, t.kind, t.version
)
select c.chat_id,
       u.chat_id is not null as applied,
       coalesce(u.kind, c.old_kind, 'personal') as kind,
       coalesce(u.version, c.old_version, 0) as version,
       case when c.refused then 'nonhuman' end as refused,
       (u.chat_id is not null and u.kind = 'business' and not coalesce(c.old_valid and c.old_kind = 'business', false))
         as became_business
from cur c left join up u on u.chat_id = c.chat_id
order by c.ord"""


def _digits(expr):
    """The digits of a number, null when there are none — how a number is compared and stored."""
    return "nullif(regexp_replace(coalesce(" + expr + ", ''), '[^0-9]', '', 'g'), '')"


_NUMBER_CHANGES = "excluded.phone is not null and excluded.phone is distinct from " + _digits("c.phone")   # filled in or another
_ANOTHER_NUMBER = "excluded.phone is not null and " + _digits("c.phone") + " <> excluded.phone"         # a KNOWN number replaced

CONNECTED_SQL = """-- «Личные и рабочие чаты»: the app reports a confirmed connection of the profile's channel; the number is the one the
-- server read from get/status (only its digits count; empty = not known). No row: created with the line's moment and the
-- number. A row without a number takes the number and keeps its moment. The same number, or none given, changes nothing.
-- ANOTHER number is a new connection (owner decision 19): the moment becomes now — every computed decision taken before
-- it stops holding (VALID_SQL), the owner's stay — and the new number is kept.
-- ALWAYS one row: connected_at_ms of the row after it (null for a sentinel profile, which writes nothing).
-- Parameters, in order: profile id, the connected number.
with num as (
  select """ + _digits("$2") + """ as phone
), up as (
  insert into audience_connections as c (profile_id, phone, connected_at)
  select $1, num.phone, """ + LINE_CONNECTED_AT_SQL("$1") + """ from num
  where """ + _real_profile("$1") + """
  on conflict (profile_id) do update set
    connected_at = case when """ + _ANOTHER_NUMBER + """ then now() else c.connected_at end,
    phone        = case when """ + _NUMBER_CHANGES + """ then excluded.phone else c.phone end,
    updated_at   = case when """ + _NUMBER_CHANGES + """ then now() else c.updated_at end
  returning c.connected_at
)
select """ + _ms("(select connected_at from up)") + """ as connected_at_ms"""


LIST_SQL = """-- «Личные и рабочие чаты»: what the app mirrors (the ChatAudience webhook's list). For every profile asked — each once, in
-- the order asked, sentinels and non-strings skipped — the moment of its connection (connected_at_ms, null without a row)
-- and the decisions that HOLD (VALID_SQL: the owner's, and the computed ones taken on the current connection) as
-- decisions [{chatId, kind, source, reason, version}], ordered by chat id.
-- Parameter: JSON array of the profile ids.
with ids as (
  select e.x #>> '{}' as profile_id, min(e.ord) as ord
  from jsonb_array_elements(""" + _json_array("$1") + """) with ordinality as e(x, ord)
  where jsonb_typeof(e.x) = 'string' and e.x #>> '{}' not in ('', '-1')
  group by e.x #>> '{}'
)
select i.profile_id,
       """ + _ms("c.connected_at") + """ as connected_at_ms,
       coalesce((select jsonb_agg(jsonb_build_object('chatId', a.chat_id, 'kind', a.kind, 'source', a.source,
                                                     'reason', a.reason, 'version', a.version) order by a.chat_id)
                 from chat_audience a where a.profile_id = i.profile_id and """ + VALID_SQL("a", "c") + """),
                '[]'::jsonb) as decisions
from ids i left join audience_connections c on c.profile_id = i.profile_id
order by i.ord"""


def BACKFILL_SQL(memory_row, owner_content_sql):
    """When a chat turns business (Decide Audience's decision, or the owner's set), the start of the conversation the
    assistant never saw — the recent messages Wappi still has — goes into the history and the memory, so it knows the owner
    already wrote to the client first. `memory_row` / `owner_content_sql` are apply-history-handoff.py's MEMORY_ROW and
    OWNER_CONTENT_SQL (that module imports this one, so they come in as arguments): the rows have exactly the form the live
    writers store — a client message as Record Suppressed writes it (history 'client', memory 'human'), an owner message as
    Record Outgoing writes the owner's phone message (history 'owner_phone', memory 'ai' opening with «Владелец: »).

    The input contract: the JSON parameter is a Wappi messages/get page exactly as returned with order=desc —
    NEWEST FIRST. The statement stores it REVERSED (oldest first), by the page position alone and never by time: Wappi's own
    order is what breaks equal timestamps (a same-second burst, an album), and a message without a parseable time keeps its
    place instead of jumping to the front. The time only fills sent_at. The history ids and the memory row ids both follow
    that order (the memory node reads memory by id)."""
    assert memory_row.count("%s") == 4 and owner_content_sql.count("%s") == 1, "MEMORY_ROW takes four values, OWNER_CONTENT_SQL one"
    client_row = memory_row % ("'human'", "m.body", "'client'", "m.id")
    owner_row = memory_row % ("'ai'", owner_content_sql % "m.body", "'owner_phone'", "m.id")
    return """-- «Личные и рабочие чаты»: the start of a conversation that just turned business. Writes ONLY while the chat's holding
-- decision is business. Skips the trigger message (it goes on through the normal path, which records it) and every
-- message whose id the chat's history already has (the assistant's replies, the app's and the phone's sends recorded
-- while the chat was business); elements without a string id or a boolean fromMe are skipped too. The page comes as
-- Wappi's messages/get returns it with order=desc — NEWEST FIRST — and the rest is stored REVERSED, oldest first, by the
-- page position alone (never by time: Wappi's order breaks equal timestamps, and a message without a parseable time keeps
-- its place); history ids and memory row ids both follow it. The owner's message (fromMe) becomes an owner_phone history
-- row + an ai memory row that opens with the owner mark, the client's a client history row + a human memory row — both
-- carrying source and message_id. A media message stores its type and caption, never its payload. The time only fills
-- sent_at. ALWAYS one row: added = the history rows written.
-- Parameters, in order: profile id, chat id, the trigger message id (empty = none), JSON array of {id, fromMe, body,
-- type, time} = a messages/get page, newest first (caption optional; time in unix seconds).
with aud_ok as (
  select """ + BUSINESS_SQL("$1", "$2") + """ as business
), src as (
  select e.x, e.ord from jsonb_array_elements(""" + _json_array("$4") + """) with ordinality as e(x, ord)
  where jsonb_typeof(e.x) = 'object' and jsonb_typeof(e.x->'id') = 'string' and e.x->>'id' <> ''
    and jsonb_typeof(e.x->'fromMe') = 'boolean'
), msgs as (
  select distinct on (s.x->>'id') s.x->>'id' as id, s.ord, (s.x->'fromMe') = 'true'::jsonb as from_me,
         case when s.x->>'type' in ('chat', 'text') then left(coalesce(s.x->>'body', ''), 4000)
              else '[' || coalesce(nullif(s.x->>'type', ''), 'media') || ']'
                   || coalesce(' ' || left(nullif(s.x->>'caption', ''), 1000), '') end as body,
         coalesce(s.x->>'type', '') as msg_type,
         case when s.x->>'time' ~ '^[0-9]{1,11}$' then (s.x->>'time')::bigint end as t
  from src s
  where (select business from aud_ok)
    and s.x->>'id' is distinct from nullif($3, '')
    and not exists (select 1 from chat_history_events h
                    where h.profile_id = $1 and h.chat_id = $2 and h.message_id = s.x->>'id')
  order by s.x->>'id', s.ord
), ev as (
  insert into chat_history_events (profile_id, chat_id, message_id, source, body, msg_type, sent_at)
  select $1, $2, m.id, case when m.from_me then 'owner_phone' else 'client' end, m.body, m.msg_type, to_timestamp(m.t)
  from msgs m
  order by m.ord desc
  on conflict (profile_id, chat_id, message_id) do nothing
  returning message_id
), mem as (
  insert into n8n_chat_histories (session_id, message)
  select $1 || ':' || $2, case when m.from_me then """ + owner_row + """
                          else """ + client_row + """ end
  from msgs m
  where m.id in (select message_id from ev)
    and not exists (select 1 from n8n_chat_histories x
                    where x.session_id = $1 || ':' || $2 and x.message->'additional_kwargs'->>'message_id' = m.id)
  order by m.ord desc
  returning 1
)
select (select count(*) from ev)::int as added"""


# The step is a node of the Profile Lifecycle Sweep (apply-personal-wipe.py): no parameters, nothing to bind. The delay is
# WIPE_DAYS written as hours, so a clock change never moves it by an hour; the batch is WIPE_BATCH. Both numbers are written into
# the statement's own comment as well, so the node reads on its own in the n8n editor. Unqualified table names, as in every
# statement of this module (the tests run them against temp tables first on the search path).
WIPE_SQL = """-- «Личные и рабочие чаты»: the Profile Lifecycle Sweep's step «Wipe Personal Chats» (every 6 hours; it does not depend on a
-- dry-run flag). """ + str(WIPE_DAYS) + """ days after the OWNER made a chat personal, what the assistant stored about that chat is deleted —
-- the protection against a mis-tap: a chat the owner takes back sooner keeps everything.
-- The work list is chat_audience: source 'owner' (a computed decision never has data to wipe), kind 'personal', the clock
-- personal_since set and old enough, not wiped yet — the oldest first, """ + str(WIPE_BATCH) + """ at most per run (the partial index
-- chat_audience_wipe_idx is built for exactly this). The rows are locked, and a row another statement holds is skipped, never
-- waited for: the owner's write that takes a chat back may be in flight, and that chat is left alone (it is business by the
-- next run, so it is not selected again).
-- For each chat: its rows in n8n_chat_histories (session_id = profile id, a colon, chat id), chat_history_events,
-- conversation_outcomes, handoff_requests, reply_events, chat_reply_state, suggestion_sets and its OWN row of reply_mode_flags
-- (never the bot-wide row, chat id '*'). Never dialog_counts (the tariff usage), never the decision itself, never the per-bot
-- rows (bot_schedule, audience_connections). Then wiped_at = now(): the chat is done, whatever is written for it afterwards.
-- One statement: all of it happens or none of it.
-- ALWAYS one row: wiped_chats (decisions marked) and deleted_rows (rows deleted over all the tables). No parameters.
with batch as (
  select a.profile_id, a.chat_id
  from chat_audience a
  where a.source = 'owner' and a.kind = 'personal' and a.wiped_at is null
    and a.personal_since <= now() - interval '""" + str(WIPE_DAYS * 24) + """ hours'
  order by a.personal_since, a.profile_id, a.chat_id
  limit """ + str(WIPE_BATCH) + """
  for update skip locked
), hist as (
  delete from n8n_chat_histories h using batch b
  where h.session_id = b.profile_id || ':' || b.chat_id
  returning 1
), events as (
  delete from chat_history_events e using batch b
  where e.profile_id = b.profile_id and e.chat_id = b.chat_id
  returning 1
), outcomes as (
  delete from conversation_outcomes o using batch b
  where o.profile_id = b.profile_id and o.chat_id = b.chat_id
  returning 1
), handoffs as (
  delete from handoff_requests r using batch b
  where r.profile_id = b.profile_id and r.chat_id = b.chat_id
  returning 1
), ledger as (
  delete from reply_events v using batch b
  where v.profile_id = b.profile_id and v.chat_id = b.chat_id
  returning 1
), states as (
  delete from chat_reply_state s using batch b
  where s.profile_id = b.profile_id and s.chat_id = b.chat_id
  returning 1
), sets as (
  delete from suggestion_sets g using batch b
  where g.profile_id = b.profile_id and g.chat_id = b.chat_id
  returning 1
), flags as (
  delete from reply_mode_flags f using batch b
  where f.profile_id = b.profile_id and f.chat_id = b.chat_id and f.chat_id <> '*'
  returning 1
), marked as (
  update chat_audience a set wiped_at = now(), updated_at = now()
  from batch b
  where a.profile_id = b.profile_id and a.chat_id = b.chat_id
  returning 1
)
select (select count(*) from marked)::int as wiped_chats,
       ((select count(*) from hist) + (select count(*) from events) + (select count(*) from outcomes)
        + (select count(*) from handoffs) + (select count(*) from ledger) + (select count(*) from states)
        + (select count(*) from sets) + (select count(*) from flags))::int as deleted_rows"""


if __name__ == "__main__":
    print(RULE_JS)
