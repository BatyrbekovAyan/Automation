#!/usr/bin/env python3
"""Idempotent migration: Upload File / Delete File hardening (2026-07-02).

Edits Tools/n8n/workflows/*.json IN PLACE, by node name (never index),
preserving indent=2 / ensure_ascii=False formatting. Re-runnable: no-op twice.

1. Switch extension matching becomes case-insensitive (.toLowerCase()) — the
   app sends the ORIGINAL filename, so "MENU.PDF" used to fall into the 415
   fallback even though the payload was a perfectly good PDF.
2. "Store Original File" (Upload File): archives the uploaded binary to the
   private Supabase Storage bucket `price-lists`, object key = the app-minted
   fileId. This makes re-indexing possible (re-chunk / re-embed / re-extract)
   without asking users to re-upload. Dead-end branch + onError continue —
   a storage hiccup must never fail the upload itself.
3. "Delete Stored Original" (Delete File): removes that object when the file's
   chunks are deleted. onError continue — files uploaded before the bucket
   existed have no object (404) and must still delete their chunks fine.

The bucket must exist: see supabase/2026-07-02-price-list-originals-bucket.sql.
"""
import json, os, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
UPLOAD = "KoTuIlk4LMrlvnWI-Upload_File.json"
DELETE = "ZTqpumOpL1rNDOp6-Delete_File.json"

# Supabase project host is baked into the credential, which HTTP Request nodes
# can't read from expressions — so the storage URL carries it literally (the
# ref is not a secret; auth lives in the supabaseApi credential).
STORAGE_URL_BASE = "https://mnwsdbqvehrkeqwnwpqb.supabase.co/storage/v1/object/price-lists/"


def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as f:
        return json.load(f)


def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)  # match source: no trailing newline


def find(nodes, name):
    for n in nodes:
        if n["name"] == name:
            return n
    return None


def fix_upload(wf):
    nodes = wf["nodes"]; conns = wf["connections"]

    # 1. Case-insensitive extension routing.
    for rule in find(nodes, "Switch")["parameters"]["rules"]["values"]:
        for cond in rule["conditions"]["conditions"]:
            if "fileExtension" in cond["leftValue"] and ".toLowerCase()" not in cond["leftValue"]:
                cond["leftValue"] = cond["leftValue"].replace(
                    ".fileExtension }}", ".fileExtension.toLowerCase() }}")

    # 2. Archive the original upload to Supabase Storage, keyed by fileId.
    supabase_cred = find(nodes, "Supabase Vector Store")["credentials"]["supabaseApi"]
    if find(nodes, "Store Original File") is None:
        nodes.append({
            "parameters": {
                "method": "POST",
                "url": "=" + STORAGE_URL_BASE + "{{ $('Extract File Id').item.json.fileId }}",
                "authentication": "predefinedCredentialType",
                "nodeCredentialType": "supabaseApi",
                "sendHeaders": True,
                "headerParameters": {"parameters": [{"name": "x-upsert", "value": "true"}]},
                "sendBody": True,
                "contentType": "binaryData",
                "inputDataFieldName": "data",
                "options": {},
            },
            "type": "n8n-nodes-base.httpRequest",
            "typeVersion": 4.2,
            "position": [816, -224],
            "id": str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-store-original")),
            "name": "Store Original File",
            "credentials": {"supabaseApi": dict(supabase_cred)},
            "onError": "continueRegularOutput",
        })
    # Fan out from BOTH supported Switch branches (txt=0, pdf=1) as a dead-end
    # sibling of the extract nodes; fallback (2) stays wired to the 415 only.
    switch_out = conns["Switch"]["main"]
    for branch_index in (0, 1):
        links = switch_out[branch_index]
        if not any(l.get("node") == "Store Original File" for l in links):
            links.append({"node": "Store Original File", "type": "main", "index": 0})
    return wf


def fix_delete(wf):
    nodes = wf["nodes"]; conns = wf["connections"]
    if find(nodes, "Delete Stored Original") is None:
        upload_cred = find(load(UPLOAD)["nodes"], "Store Original File")["credentials"]["supabaseApi"]
        wx, wy = find(nodes, "Webhook")["position"]
        nodes.append({
            "parameters": {
                "method": "DELETE",
                "url": "=" + STORAGE_URL_BASE + "{{ $json.body.fileId }}",
                "authentication": "predefinedCredentialType",
                "nodeCredentialType": "supabaseApi",
                "options": {},
            },
            "type": "n8n-nodes-base.httpRequest",
            "typeVersion": 4.2,
            "position": [wx + 220, wy - 192],
            "id": str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-delete-original")),
            "name": "Delete Stored Original",
            "credentials": {"supabaseApi": dict(upload_cred)},
            "onError": "continueRegularOutput",
        })
    links = conns["Webhook"]["main"][0]
    if not any(l.get("node") == "Delete Stored Original" for l in links):
        links.append({"node": "Delete Stored Original", "type": "main", "index": 0})
    return wf


def main():
    wf = load(UPLOAD); fix_upload(wf); save(UPLOAD, wf); print(f"  fixed {UPLOAD}")
    wf = load(DELETE); fix_delete(wf); save(DELETE, wf); print(f"  fixed {DELETE}")
    print("done")


if __name__ == "__main__":
    main()
