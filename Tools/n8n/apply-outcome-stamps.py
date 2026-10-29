#!/usr/bin/env python3
"""Idempotent migration: terminal OUTCOME stamps for both canonical bot templates (audit stage 0.5).

Every execution of a bot workflow used to end on whichever node happened to be last — the only
way to tell «filtered» from «suppressed» from «sent» was to know the graph. This splices ONE Code
node, `Outcome`, onto every terminal branch, so each execution ends with a single structured row
(NO dialog text, ids only — the webhook body already sits in the same execution log):

    { outcome, correlationId: $execution.id, stage: <previous node>, messageId, profileId, chatId,
      channel, transportMessageId (send response id when sent), stampedAt }

outcome ∈ filtered | suppressed | off_hours | duplicate | superseded | quota_denied | generation_failed | cancelled_owner |
          cancelled_mode | cancelled_busy | cancelled_fetch | sent | send_failed | send_unknown  (stage 0.5 + stage 3 + B1)
          | personal | personal_settling | personal_unknown | personal_nonhuman | personal_contact | personal_history |
          cancelled_personal | ignored_personal  (personal chats, 2026-10-25)

«Личные и рабочие чаты» (2026-10-25, apply-history-handoff.py wires the gate): a chat that is not a business chat ends at the
entry. After Business? false the item is the mode read's row → personal. After Decided Business? false it is Decide Audience's
answer, and the stamp carries the sub-workflow's OWN outcome when it is one of its five personal ones
(DECIDE_PERSONAL_OUTCOMES — why the chat got no answer); anything else (an error item, the mode read's row handed on when the
node failed, a missing or unknown value) → personal_unknown. cancelled_personal (the lease) and ignored_personal (Record
Outgoing) arrive as recorded rows.

B1 «расписание бота» (2026-10-14): a message the bot's SCHEDULE kept from it (outside an enabled window) takes the
«Вместе» branch — Read Reply Mode → Suppressed? → Record Suppressed — and Record Suppressed hands the mode read's
`off_hours` on; the stamp then says `off_hours` instead of `suppressed`. A «Вместе» chat inside the window keeps
`suppressed`. Only on a RECORDED suppression (Record Suppressed's own row, outcome 'suppressed'): the node continues
on error, and a failure outside the query runner hands its INPUT on (the R8 behaviour) — the mode read's row, off_hours
true but no outcome — which keeps the pre-B1 label `other:Record Suppressed` (the message was never recorded).

Branches stamped: If[false] (from != chatId), Suppressed?[true], Is Latest?[false] (older fragment
of a burst), If Quota Allows[false], HTTP Request[main] (the Wappi send returned). Nothing on the
reply path moves; the node is appended LAST so every nodes[i] index the orchestrators patch
(nodes[0] Webhook, nodes[5] AI Agent) is unchanged.

    python3 Tools/n8n/apply-outcome-stamps.py           # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-outcome-stamps.py --check   # exit 2 if a template lacks the stamp or carries a stale one
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
BOTS = {"4wYitz5ek30SVNlT-WhatsApp_Bot.json": "whatsapp", "4VN3gsFaC2HUYmcc-Telegram_Bot.json": "telegram"}
NODE = "Outcome"

# (source node, output index) -> outcome label. These are exactly the dead-end branches the
# verifiers pin (verify-message-batching / verify-telegram-parity) plus the send's own output.
# Stage 3 moved the winner/refusal/send terminals behind Record Send / Record Cancel / Claimed?; the stamp
# now sits after those (apply-reply-pipeline-v2.py wires them) — this list is the stage-0 shape kept for
# templates that predate the pipeline migration.
# Stage 4 moved the suppressed terminal behind Record Suppressed (apply-history-handoff.py wires it).
TERMINALS = [("If", 1, "filtered")]
# The personal outcomes Decide Audience (crAudienceDec001, build-decide-audience.py) answers besides «business» — the stamp
# names them after Decided Business? false (tests/test_reply_audience.py pins the list against the sub-workflow's own code).
DECIDE_PERSONAL_OUTCOMES = ("personal_settling", "personal_unknown", "personal_nonhuman", "personal_contact", "personal_history")

JS_TEMPLATE = r"""// OUTCOME STAMP (audit stage 0.5, Tools/n8n/apply-outcome-stamps.py). One structured row per
// execution end, no dialog text: the label is derived from WHICH branch reached this node.
const byPrev = { 'If': 'filtered', 'Suppressed?': 'suppressed', 'Is Latest?': 'superseded',
                 'If Quota Allows': 'quota_denied', 'HTTP Request': 'sent', 'Claimed?': 'duplicate',
                 'Is Outgoing?': 'ignored', 'Outgoing Bound?': 'filtered',
                 'Business?': 'personal', 'Decided Business?': 'personal_unknown' };
const prev = $prevNode.name;
let wh = {};
try { wh = $('Webhook').first().json || {}; } catch (e) { wh = {}; }
const m = (wh.body && Array.isArray(wh.body.messages) && wh.body.messages[0]) || {};
const resp = $input.first() ? ($input.first().json || {}) : {};
// Personal chats: a chat that is not business ends at the entry — after Business? the mode read's row (personal); after
// Decided Business? Decide Audience's answer, named by the sub-workflow's own personal outcome; anything else that reaches
// it (an error item, the mode read's row handed on when the node failed, a missing or unknown value) is personal_unknown.
const DECIDE_PERSONAL = __DECIDE_PERSONAL__;
const personal = prev === 'Business?' ? byPrev[prev]
  : (prev === 'Decided Business?' ? (DECIDE_PERSONAL.includes(resp.outcome) ? resp.outcome : byPrev[prev]) : null);
// Stage 3: the record rows (Record Send / Record Cancel) carry the event's final state explicitly.
// B1: Record Suppressed hands on the mode read's off_hours — the schedule, not the owner, kept this message from the bot.
// Only its OWN row counts (outcome 'suppressed'): a failure outside the query runner hands the mode read's row on.
const outcome = personal !== null ? personal
  : (resp.outcome === 'suppressed' && resp.off_hours === true) ? 'off_hours'
  : ((typeof resp.outcome === 'string' && resp.outcome) ? resp.outcome : (byPrev[prev] || ('other:' + prev)));
return [{ json: {
  outcome,
  correlationId: $execution.id,
  stage: prev,
  messageId: m.id ?? null,
  profileId: m.profile_id ?? null,
  chatId: m.chatId ?? null,
  channel: '__CHANNEL__',
  transportMessageId: resp.transport_message_id ?? ((prev === 'HTTP Request' && resp && resp.message_id) ? resp.message_id : null),
  transportStatus: (prev === 'HTTP Request' && resp) ? (resp.status ?? null) : null,
  stampedAt: new Date().toISOString(),
}, pairedItem: { item: 0 } }];
""".replace("__DECIDE_PERSONAL__", json.dumps(list(DECIDE_PERSONAL_OUTCOMES)))


def node_by_name(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def patch(fname, channel):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    changed = False
    js = JS_TEMPLATE.replace("__CHANNEL__", channel)
    existing = node_by_name(wf, NODE)
    if existing is None:
        # Position: below the send node, right of the gate column (cosmetic only).
        send = node_by_name(wf, "HTTP Request")
        x, y = (send["position"][0], send["position"][1] + 400) if send else (0, 0)
        wf["nodes"].append({
            "parameters": {"jsCode": js},
            "id": "0c0b0e0a-5ba1-4a7e-9f0e-0000000000ff",
            "name": NODE,
            "type": "n8n-nodes-base.code",
            "typeVersion": 2,
            "position": [x, y],
        })
        changed = True
    elif existing["parameters"].get("jsCode") != js:
        existing["parameters"]["jsCode"] = js
        changed = True
    conns = wf["connections"]
    for src, idx, _label in TERMINALS:
        outs = conns.setdefault(src, {}).setdefault("main", [])
        while len(outs) <= idx:
            outs.append([])
        target = {"node": NODE, "type": "main", "index": 0}
        if target not in outs[idx]:
            outs[idx].append(target)
            changed = True
    if changed:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def check(fname):
    wf = json.load(open(os.path.join(WF, fname), encoding="utf-8"))
    stamp = node_by_name(wf, NODE)
    ok = stamp is not None and stamp["parameters"].get("jsCode") == JS_TEMPLATE.replace("__CHANNEL__", BOTS[fname])
    for src, idx, _ in TERMINALS:
        outs = wf["connections"].get(src, {}).get("main", [])
        ok = ok and idx < len(outs) and any(c["node"] == NODE for c in outs[idx])
    return ok


def main():
    if "--check" in sys.argv:
        bad = [f for f in BOTS if not check(f)]
        print("outcome stamps:", "OK" if not bad else f"MISSING/STALE in {bad}")
        sys.exit(2 if bad else 0)
    for f, ch in BOTS.items():
        print(f"{'patched' if patch(f, ch) else 'already up to date'}  {f}")


if __name__ == "__main__":
    main()
