#!/usr/bin/env python3
"""Idempotent migration: terminal OUTCOME stamps for both canonical bot templates (audit stage 0.5).

Every execution of a bot workflow used to end on whichever node happened to be last — the only
way to tell «filtered» from «suppressed» from «sent» was to know the graph. This splices ONE Code
node, `Outcome`, onto every terminal branch, so each execution ends with a single structured row
(NO dialog text, ids only — the webhook body already sits in the same execution log):

    { outcome, correlationId: $execution.id, stage: <previous node>, messageId, profileId, chatId,
      channel, transportMessageId (send response id when sent), stampedAt }

outcome ∈ filtered | suppressed | superseded | quota_denied | sent   (stage 0.5)
          send_failed | send_unknown | generation_failed              (added by stage 3, same node)

Branches stamped: If[false] (from != chatId), Suppressed?[true], Is Latest?[false] (older fragment
of a burst), If Quota Allows[false], HTTP Request[main] (the Wappi send returned). Nothing on the
reply path moves; the node is appended LAST so every nodes[i] index the orchestrators patch
(nodes[0] Webhook, nodes[5] AI Agent) is unchanged.

    python3 Tools/n8n/apply-outcome-stamps.py           # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-outcome-stamps.py --check   # exit 2 if a template lacks the stamp
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
TERMINALS = [("If", 1, "filtered"), ("Suppressed?", 0, "suppressed"), ("Is Latest?", 0, "superseded"),
             ("If Quota Allows", 1, "quota_denied"), ("HTTP Request", 0, "sent")]

JS_TEMPLATE = r"""// OUTCOME STAMP (audit stage 0.5, Tools/n8n/apply-outcome-stamps.py). One structured row per
// execution end, no dialog text: the label is derived from WHICH branch reached this node.
const byPrev = { 'If': 'filtered', 'Suppressed?': 'suppressed', 'Is Latest?': 'superseded',
                 'If Quota Allows': 'quota_denied', 'HTTP Request': 'sent' };
const prev = $prevNode.name;
let wh = {};
try { wh = $('Webhook').first().json || {}; } catch (e) { wh = {}; }
const m = (wh.body && Array.isArray(wh.body.messages) && wh.body.messages[0]) || {};
const resp = $input.first() ? ($input.first().json || {}) : {};
const outcome = byPrev[prev] || ('other:' + prev);
return [{ json: {
  outcome,
  correlationId: $execution.id,
  stage: prev,
  messageId: m.id ?? null,
  profileId: m.profile_id ?? null,
  chatId: m.chatId ?? null,
  channel: '__CHANNEL__',
  transportMessageId: (prev === 'HTTP Request' && resp && resp.message_id) ? resp.message_id : null,
  transportStatus: (prev === 'HTTP Request' && resp) ? (resp.status ?? null) : null,
  stampedAt: new Date().toISOString(),
}, pairedItem: { item: 0 } }];
"""


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
    ok = node_by_name(wf, NODE) is not None
    for src, idx, _ in TERMINALS:
        outs = wf["connections"].get(src, {}).get("main", [])
        ok = ok and idx < len(outs) and any(c["node"] == NODE for c in outs[idx])
    return ok


def main():
    if "--check" in sys.argv:
        bad = [f for f in BOTS if not check(f)]
        print("outcome stamps:", "OK" if not bad else f"MISSING in {bad}")
        sys.exit(2 if bad else 0)
    for f, ch in BOTS.items():
        print(f"{'patched' if patch(f, ch) else 'already up to date'}  {f}")


if __name__ == "__main__":
    main()
