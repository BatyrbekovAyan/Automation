#!/usr/bin/env python3
"""The message-batching / debounce splice (Phase 10, BATCH-01/02).

Idempotent, by-node-name migration of the two CANONICAL bot templates
(see docs/superpowers/specs/2026-07-15-message-batching-debounce-design.md).
Splices a pre-generation debounce+combine stage onto the Phase-9 `Suppressed?`
node's FALSE branch (main[1]), before `Input type`:

    Suppressed? [main#1, not-suppressed]
        -> Debounce Wait   (n8n Wait, ~8s in-memory resume)
        -> Fetch Recent    (httpRequest GET messages/get, limit only, NO mark_all)
        -> Latest+Combine  (Code: is-latest dedupe + text combine; RE-EMITS webhook body)
        -> Is Latest?      (If: abort==true dead-ends the fragment; false -> Input type)

Only the last fragment's execution proceeds to `Input type`; earlier fragments
dead-end at `Is Latest?`. One `messages/get` fetch serves BOTH the is-latest
check and the combine. The Code node is channel-agnostic (WhatsApp `chat` OR
Telegram `text`); only the Fetch Recent base URL differs (api/sync vs tapi/sync),
derived per template from that template's own Mark Read url.

Edits Tools/n8n/workflows/{WhatsApp,Telegram}_Bot.json IN PLACE, preserving
indent=2 / ensure_ascii=False formatting.

The four spliced nodes are OWNED by this script: every run UPSERTS them (rewrites
`parameters` + node-level keys from the specs below, preserving only the stable
uuid5 `id` and the node's `position`). So a fix made HERE always materializes in
both templates, and re-running with no source change is still a byte-level no-op.
Corollary: never hand-edit the four managed nodes in the JSON — a re-run reverts it.

Live deploy + runData verification is plan 10-03 (owner gate). This script only
edits the committed JSON; it never touches the live n8n instance.
"""
import json, os, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
BOT_IDS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")

# ~8s auto-reply debounce window (< 65s -> n8n Wait resumes in memory, no DB offload).
# Single tunable per CONTEXT; tuned at the owner e2e (10-03).
DEBOUNCE_SECONDS = 8

# Fetch Recent reuses the WappiAuthToken credential already bound in both templates.
WAPPI_CRED = {"httpHeaderAuth": {"id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken"}}

# Latest+Combine Code body (channel-agnostic; re-emits webhook body). VERBATIM from
# 10-RESEARCH.md Code Examples. Raw string so the JS `'\n'` join delimiter stays a
# two-char escape (backslash+n) rather than a real newline.
#   - $('Webhook').first().json  NOT .item  -> paired-item safety across Wait+HTTP (Pitfall 1/anti-pattern)
#   - fetched.sort by time desc            -> deterministic newest (Pitfall 3)
#   - type === 'chat' || type === 'text'   -> WhatsApp + Telegram (Pitfall 4)
#   - return [{ json: { ...wh, ... } }]    -> RE-EMIT body so Input type/Download Audio/Text resolve $json.body (Pitfall 1)
LATEST_COMBINE_JS = r"""const wh = $('Webhook').first().json;
const triggeringId = wh.body.messages[0].id;

const fetched = ($json.messages || []).slice();
fetched.sort((a, b) => (b.time || 0) - (a.time || 0));

const isText = (m) => m && (m.type === 'chat' || m.type === 'text');
const incoming = fetched.filter(m => m && m.fromMe === false);
const newestIncoming = incoming[0];

let abort = false;
let combinedText = null;

if (!newestIncoming || newestIncoming.id !== triggeringId) {
  abort = true;
} else if (isText(newestIncoming)) {
  const parts = [];
  for (const m of fetched) {
    if (m.fromMe === true) break;
    if (!isText(m)) break;
    parts.push(typeof m.body === 'string' ? m.body : '');
  }
  parts.reverse();
  combinedText = parts.join('\n');
}

return [{ json: { ...wh, abort, combinedText } }];"""


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as f:
        return json.load(f)


def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)  # match source: no trailing newline


def find(nodes, name=None, type_suffix=None):
    for n in nodes:
        if name is not None and n["name"] == name:
            return n
        if type_suffix is not None and n["type"].endswith(type_suffix):
            return n
    return None


def splice(wf):
    """Insert the 4-node debounce stage on `Suppressed?` main[1]. Idempotent."""
    nodes = wf["nodes"]
    conns = wf["connections"]

    def nid(suffix):
        # Stable per-template node id so re-runs are byte-stable and both templates differ.
        return str(uuid.uuid5(uuid.NAMESPACE_DNS, wf["id"] + "-" + suffix))

    def managed(spec):
        """Upsert one script-owned node, in place, preserving its id + position.

        A guarded add (`if find(...) is None: append`) would make every later edit to a
        spec a silent no-op on templates that already carry the node. Instead the spec is
        the source of truth: an existing node is REPLACED at its current list index (so
        node order is stable) while keeping its `id` (stable uuid5) and `position` (the
        owner may have dragged it in the n8n UI). Any other hand-edit to a managed node is
        deliberately overwritten. Same input -> byte-identical output (idempotent).
        """
        existing = find(nodes, name=spec["name"])
        if existing is None:
            nodes.append(spec)
            return
        spec["id"] = existing["id"]                                  # keep the deployed id
        spec["position"] = existing.get("position", spec["position"])  # keep a UI-dragged position
        for i, n in enumerate(nodes):
            if n is existing:
                nodes[i] = spec
                break

    # (1) Derive the channel base from THIS template's own Mark Read node.
    #     WhatsApp -> https://wappi.pro/api/sync/ ; Telegram -> https://wappi.pro/tapi/sync/
    mark_read = find(nodes, name="Mark Read")
    base = mark_read["parameters"]["url"].rsplit("message/mark/read", 1)[0]

    # Offset the new nodes below the Suppressed? gate so the graph stays readable.
    sx, sy = find(nodes, name="Suppressed?")["position"]

    # (2) Upsert the 4 managed nodes (spec is source of truth; see managed()).
    managed({
        "parameters": {"amount": DEBOUNCE_SECONDS},
        "id": nid("Debounce Wait"),
        "name": "Debounce Wait",
        "type": "n8n-nodes-base.wait",
        "position": [sx, sy + 220],
        "typeVersion": 1.1,
    })

    managed({
        "parameters": {
            "method": "GET",
            "url": base + "messages/get",
            "authentication": "genericCredentialType",
            "genericAuthType": "httpHeaderAuth",
            "sendQuery": True,
            "queryParameters": {"parameters": [
                {"name": "profile_id", "value": "={{ $('Webhook').item.json.body.messages[0].profile_id }}"},
                {"name": "chat_id", "value": "={{ $('Webhook').item.json.body.messages[0].chatId }}"},
                {"name": "limit", "value": "15"},
            ]},
            "options": {},
        },
        "id": nid("Fetch Recent"),
        "name": "Fetch Recent",
        "type": "n8n-nodes-base.httpRequest",
        "typeVersion": 4.2,
        "position": [sx + 208, sy + 220],
        "credentials": WAPPI_CRED,
    })

    managed({
        "parameters": {"jsCode": LATEST_COMBINE_JS},
        "id": nid("Latest+Combine"),
        "name": "Latest+Combine",
        "type": "n8n-nodes-base.code",
        "typeVersion": 2,
        "position": [sx + 416, sy + 220],
    })

    managed({
        "parameters": {
            "conditions": {
                "options": {
                    "caseSensitive": True,
                    "leftValue": "",
                    "typeValidation": "loose",
                    "version": 2,
                },
                "conditions": [{
                    "id": nid("is-latest-cond"),
                    "leftValue": "={{ $json.abort }}",
                    "rightValue": "",
                    "operator": {"type": "boolean", "operation": "true", "singleValue": True},
                }],
                "combinator": "and",
            },
            "options": {},
        },
        "id": nid("Is Latest?"),
        "name": "Is Latest?",
        "type": "n8n-nodes-base.if",
        "typeVersion": 2.2,
        "position": [sx + 624, sy + 220],
    })

    # (3) Rewire connections (overwrite idiom — re-point the FALSE branch through the chain).
    #     Suppressed? main[0] TRUE (semi-auto) stays a dead-end; main[1] FALSE -> Debounce Wait.
    conns["Suppressed?"] = {"main": [[], [{"node": "Debounce Wait", "type": "main", "index": 0}]]}
    conns["Debounce Wait"] = {"main": [[{"node": "Fetch Recent", "type": "main", "index": 0}]]}
    conns["Fetch Recent"] = {"main": [[{"node": "Latest+Combine", "type": "main", "index": 0}]]}
    conns["Latest+Combine"] = {"main": [[{"node": "Is Latest?", "type": "main", "index": 0}]]}
    # Is Latest? main[0] TRUE (abort) dead-ends the fragment; main[1] FALSE proceeds to Input type.
    conns["Is Latest?"] = {"main": [[], [{"node": "Input type", "type": "main", "index": 0}]]}

    # (4) Inject combinedText into the Text set node with a single-message fallback. The
    #     fallback reads bare $json.body (NOT $('Webhook').item): Latest+Combine re-emits body
    #     onto the current item, matching how Input type / Download Audio read $json.body, and
    #     avoiding fragile paired-item resolution across the inserted Wait+HTTP+Code nodes.
    find(nodes, name="Text")["parameters"]["assignments"]["assignments"][0]["value"] = \
        "={{ $json.combinedText ?? $json.body.messages[0].body }}"

    return wf


def main():
    for fname in BOT_IDS:
        wf = load(fname)
        splice(wf)
        save(fname, wf)
        print(f"  spliced {fname}")
    print("done")


if __name__ == "__main__":
    main()
