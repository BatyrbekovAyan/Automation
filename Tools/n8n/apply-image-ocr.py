#!/usr/bin/env python3
"""Idempotent migration: image OCR branch in Upload File (2026-07-03 spec).

Adds a Switch rule for jpg/jpeg/png/webp -> OpenAI vision (gpt-4o-mini) ->
IF gate (NO_PRICE_DATA/empty -> 422) -> Image Text -> Merge(0). The image
branch also fans into Store Original File (photos archived for future
re-OCR). Edits by node name, indent=2/ensure_ascii=False, re-runnable.
"""
import json, os, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
UPLOAD = "KoTuIlk4LMrlvnWI-Upload_File.json"

EXT_EXPR = "={{ $('Set File Id').item.binary.data.fileExtension.toLowerCase() }}"
IMAGE_EXTS = ["jpg", "jpeg", "png", "webp"]

PROMPT = (
    "Ты извлекаешь прайс-лист из фотографии (меню, ценник, прайс-борд, витрина).\n"
    "Выпиши КАЖДУЮ позицию с ценой, по одной на строку, ровно в формате:\n"
    "{{ $('Extract Content Type').item.json.contentType }}[N]: Название: <название>; Цена: <число и валюта как на фото>;\n"
    "где N — порядковый номер, начиная с 1. Заголовки разделов (например «Напитки») "
    "выписывай отдельной строкой без номера. Ничего не придумывай: только то, что "
    "читается на фото. Если на фото нет ни одной читаемой цены, выведи ровно: NO_PRICE_DATA"
)

# Params verified 2026-07-03 against live n8n-mcp get_node_types for
# @n8n/n8n-nodes-langchain.openAi (resource=image, operation=analyze) — v2.3
# (typeVersion 2.3, not 1.8). options only exposes detail/maxTokens for this
# operation — no temperature field, so it's omitted (see brief Step 1 note).
VISION_PARAMS = {
    "resource": "image",
    "operation": "analyze",
    "modelId": {"__rl": True, "value": "gpt-4o-mini", "mode": "list", "cachedResultName": "gpt-4o-mini"},
    "text": "=" + PROMPT,
    "inputType": "base64",
    "binaryPropertyName": "data",
    "simplify": True,
    "options": {"detail": "high", "maxTokens": 4000},
}


def load():
    with open(os.path.join(WF, UPLOAD), encoding="utf-8") as f:
        return json.load(f)


def save(wf):
    with open(os.path.join(WF, UPLOAD), "w", encoding="utf-8") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)


def find(nodes, name):
    for n in nodes:
        if n["name"] == name:
            return n
    return None


def node_id(wf, tag):
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-" + tag))


def add_switch_rule(wf):
    rules = find(wf["nodes"], "Switch")["parameters"]["rules"]["values"]
    if any(r.get("outputKey") == "image" for r in rules):
        return False
    rules.append({
        "conditions": {
            "options": {"caseSensitive": True, "leftValue": "", "typeValidation": "strict", "version": 2},
            "conditions": [
                {"id": node_id(wf, "cond-" + ext), "leftValue": EXT_EXPR, "rightValue": ext,
                 "operator": {"type": "string", "operation": "equals"}}
                for ext in IMAGE_EXTS
            ],
            "combinator": "or",
        },
        "renameOutput": True,
        "outputKey": "image",
    })
    return True



# openAi node v2.3 with simplify:true emits ONE item whose .json value is
# itself an array of message objects:
#   $json === [{ id, type:"message", content:[{type:"output_text", text:"..."}],
#                role:"assistant" }]
# (verified directly against live execution 139 runData — the item's `json`
# field is an array, n8n does NOT unwrap it onto $json). So the extracted
# text lives at $json[0].content[0].text, NOT $json.content or
# $json.content[0].text (both undefined since $json itself is an array).
TEXT_EXPR = "={{ $json[0].content[0].text }}"


def add_nodes(wf):
    nodes = wf["nodes"]
    openai_cred = find(nodes, "Embeddings OpenAI")["credentials"]["openAiApi"]
    if find(nodes, "Extract Price Text From Image") is None:
        nodes.append({
            "parameters": dict(VISION_PARAMS),
            "type": "@n8n/n8n-nodes-langchain.openAi",
            "typeVersion": 2.3,
            "position": [1100, -224],
            "id": node_id(wf, "vision"),
            "name": "Extract Price Text From Image",
            "credentials": {"openAiApi": dict(openai_cred)},
        })
    gate = find(nodes, "Has Price Data")
    if gate is None:
        nodes.append({
            "parameters": {
                "conditions": {
                    "options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                    "conditions": [
                        {"id": node_id(wf, "cond-marker"), "leftValue": TEXT_EXPR,
                         "rightValue": "NO_PRICE_DATA",
                         "operator": {"type": "string", "operation": "notContains"}},
                        {"id": node_id(wf, "cond-notempty"), "leftValue": TEXT_EXPR,
                         "operator": {"type": "string", "operation": "notEmpty",
                                       "singleValue": True}},
                    ],
                    "combinator": "and",
                },
                "options": {},
            },
            "type": "n8n-nodes-base.if",
            "typeVersion": 2.2,
            "position": [1400, -224],
            "id": node_id(wf, "gate"),
            "name": "Has Price Data",
        })
    else:
        # Self-correcting: update any stale $json.content leftValue to the
        # verified $json.content[0].text shape, in place (preserve ids/order).
        for cond in gate["parameters"]["conditions"]["conditions"]:
            cond["leftValue"] = TEXT_EXPR
    text_node = find(nodes, "Image Text")
    if text_node is None:
        nodes.append({
            "parameters": {
                "assignments": {"assignments": [
                    {"id": node_id(wf, "assign-text"), "name": "text",
                     "value": TEXT_EXPR, "type": "string"},
                ]},
                "options": {},
            },
            "type": "n8n-nodes-base.set",
            "typeVersion": 3.4,
            "position": [1700, -288],
            "id": node_id(wf, "imagetext"),
            "name": "Image Text",
        })
    else:
        for a in text_node["parameters"]["assignments"]["assignments"]:
            if a["name"] == "text":
                a["value"] = TEXT_EXPR
    if find(nodes, "Respond No Price Data") is None:
        nodes.append({
            "parameters": {
                "respondWith": "json",
                "responseBody": "={{ { \"success\": false, \"error\": \"no_price_data\" } }}",
                "options": {"responseCode": 422},
            },
            "type": "n8n-nodes-base.respondToWebhook",
            "typeVersion": 1.4,
            "position": [1700, -128],
            "id": node_id(wf, "respond422"),
            "name": "Respond No Price Data",
        })


def wire(wf):
    conns = wf["connections"]
    switch = conns["Switch"]["main"]
    # Rule order after add_switch_rule: 0 txt, 1 pdf, 2 image, 3 fallback.
    # The fallback links were at index 2 before this migration — move them.
    image_links = [
        {"node": "Extract Price Text From Image", "type": "main", "index": 0},
        {"node": "Store Original File", "type": "main", "index": 0},
    ]
    if len(switch) == 3:  # not yet migrated
        switch.insert(2, image_links)
    elif not any(l["node"] == "Extract Price Text From Image" for l in switch[2]):
        switch[2] = image_links
    conns["Extract Price Text From Image"] = {"main": [[{"node": "Has Price Data", "type": "main", "index": 0}]]}
    conns["Has Price Data"] = {"main": [
        [{"node": "Image Text", "type": "main", "index": 0}],
        [{"node": "Respond No Price Data", "type": "main", "index": 0}],
    ]}
    conns["Image Text"] = {"main": [[{"node": "Merge", "type": "main", "index": 0}]]}


def main():
    wf = load()
    add_switch_rule(wf)
    add_nodes(wf)
    wire(wf)
    save(wf)
    print(f"  fixed {UPLOAD}")


if __name__ == "__main__":
    main()
