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
       Telegram  the chat id is one of TG_SERVICE_IDS (777000, the service account), or the chat type is neither '' nor
                 'user' (a channel, a group), or any of flags.bot / flags.support / flags.self;
  2. Telegram without the chat card (flags null) → personal / unknown: a bot cannot be ruled out. WhatsApp has no card to
     miss, so there `flags: null` reads as all false;
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

    python3 Tools/n8n/audience.py              # print RULE_JS
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

SETTLE_MINUTES = 30     # after a channel is connected, a chat without a decision gets no reply, no suggestion and no decision
                        # for this long (what Wappi can read of the account's history may be incomplete at first)
FRESH_HOURS = 24        # a message that waited for the owner's decision is handed to the assistant again only while younger
WIPE_DAYS = 2           # the data of a chat the owner made personal is wiped after this many days
WA_CONTACT_SOURCE = "webhook"   # where the WhatsApp «saved contact» fact comes from: "webhook" (contact_name of the incoming
                                # message) | "chats_filter" (contact.FullName of the chat list); live check D1,
                                # docs/audits/2026-10-25-personal-chats/live-checks.md
WA_NONHUMAN_SUFFIXES = ("@g.us", "@newsletter", "@broadcast")   # a group; a channel; the status and broadcast lists
TG_SERVICE_IDS = ("777000",)                                    # the Telegram service account (login codes and the like)
FIXTURE = os.path.join(HERE, "fixtures", "audience-cases.json")

# ASCII only on purpose: the same text is embedded in n8n Code nodes and mirrored in C#. Nothing here may read a clock, the
# network or an n8n global (tests/test_audience_rule.py: rule_js_is_pure), and the only name it leaves at the top level of a
# Code node is `decideAudience`. The two lists are the module's tuples above, written in by the replacements below.
RULE_JS = r"""// AUDIENCE RULE - is this chat PERSONAL (the assistant stays out) or BUSINESS (a client)? ONE function, two runners:
// the Decide Audience Code node embeds this text verbatim (Tools/n8n/audience.py) and the app's ChatAudienceRule.Decide is its
// C# twin; both run every row of Tools/n8n/fixtures/audience-cases.json. Pure: no clock, no network, no n8n globals.
//
// f = { channel,   'whatsapp' | 'telegram'
//       chatId,    string
//       chatType,  string; '' = unknown (Telegram: 'user' | 'channel' | 'chat' ...)
//       flags,     { bot, support, self } | null; Telegram: null = the chat card was not received, WhatsApp: null = all false
//       contact,   true | false | null; a saved contact, null = not known
//       history,   true | false | null; a conversation before the connection, null = not known
//       owner }    null | 'personal' | 'business'; the owner's own decision, if there is one
// returns { kind: 'personal' | 'business', reason: 'nonhuman' | 'owner' | 'contact' | 'history' | 'new' | 'unknown' }
// A fact that is not given is not known: only a known "no" on both contact and history makes a new person.
function decideAudience(f) {
  const WA_NONHUMAN_SUFFIXES = __WA_NONHUMAN_SUFFIXES__;
  const TG_SERVICE_IDS = __TG_SERVICE_IDS__;
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
    ? TG_SERVICE_IDS.indexOf(id) >= 0 || (type !== '' && type !== 'user') || flag('bot') || flag('support') || flag('self')
    : WA_NONHUMAN_SUFFIXES.some((suffix) => id.endsWith(suffix)) || flag('bot') || flag('self');
  if (nonhuman) return personal('nonhuman');
  // 2. Telegram without the chat card: a bot cannot be ruled out.
  if (telegram && flags === null) return personal('unknown');
  // 3. The owner's own decision.
  if (f.owner === 'personal' || f.owner === 'business') return { kind: f.owner, reason: 'owner' };
  // 4-5. A saved contact, or a conversation that existed before the connection.
  if (f.contact) return personal('contact');
  if (f.history) return personal('history');
  // 6. Contact or history not known.
  if (f.contact !== false || f.history !== false) return personal('unknown');
  // 7. A new person.
  return { kind: 'business', reason: 'new' };
}""".replace("__WA_NONHUMAN_SUFFIXES__", json.dumps(list(WA_NONHUMAN_SUFFIXES))).replace("__TG_SERVICE_IDS__", json.dumps(list(TG_SERVICE_IDS)))


if __name__ == "__main__":
    print(RULE_JS)
