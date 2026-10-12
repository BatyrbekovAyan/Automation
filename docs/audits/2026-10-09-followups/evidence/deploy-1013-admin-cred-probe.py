# -*- coding: utf-8 -*-
"""Self-deleting probe: can the instance's `n8n Admin API` credential do what the new Create does
(GET a workflow, POST create, activate, DELETE)? Creates a temporary probe workflow (webhook) and,
through it, a temporary dummy workflow; both are deleted. The API key never leaves the tool helpers."""
import importlib.util, json, os, sys, time, uuid, urllib.request, urllib.error

REPO = "/Users/ayan/Projects/Automation"
HERE = os.path.join(REPO, "Tools/n8n")
sys.path.insert(0, HERE)


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


uc = load("uc", "update-clones.py")
dc = load("dc", "deploy-canonical.py")
BASE = "https://n8n.choosereply.com"
TEMPLATE = "4wYitz5ek30SVNlT"

cred_id = dc.admin_credential_id(BASE)
if not cred_id:
    sys.exit("no live n8n Admin API credential on Set Workflow State")
tag = uuid.uuid4().hex[:12]
probe_path = f"crprobe-{tag}"
dummy_path = f"crprobe-dummy-{tag}"
dummy_name = f"zz crprobe dummy {tag}"
cred = {"httpHeaderAuth": {"id": cred_id, "name": "n8n Admin API"}}
resp_opts = {"timeout": 20000, "response": {"response": {"fullResponse": True, "neverError": True}}}
dummy_body = {
    "name": dummy_name,
    "nodes": [{"parameters": {"httpMethod": "POST", "path": dummy_path, "responseMode": "onReceived", "options": {}},
               "name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2.1,
               "webhookId": str(uuid.uuid4()), "position": [0, 0]}],
    "connections": {}, "settings": {"executionOrder": "v1"},
}


def http(name, method, url, x, extra=None):
    p = {"method": method, "url": url, "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
         "options": resp_opts}
    if extra:
        p.update(extra)
    return {"parameters": p, "name": name, "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
            "credentials": cred, "onError": "continueRegularOutput", "position": [x, 0]}


created_id = "$('Create Dummy').first().json.body.id"
nodes = [
    {"parameters": {"httpMethod": "POST", "path": probe_path, "authentication": "none", "responseMode": "responseNode", "options": {}},
     "name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2.1, "webhookId": str(uuid.uuid4()), "position": [0, 0]},
    http("Get Template", "GET", f"http://localhost:5678/api/v1/workflows/{TEMPLATE}", 200),
    http("Create Dummy", "POST", "http://localhost:5678/api/v1/workflows", 400,
         {"sendBody": True, "specifyBody": "json", "jsonBody": json.dumps(dummy_body)}),
    http("Activate Dummy", "POST", "=http://localhost:5678/api/v1/workflows/{{ " + created_id + " }}/activate", 600),
    http("Delete Dummy", "DELETE", "=http://localhost:5678/api/v1/workflows/{{ " + created_id + " }}", 800),
    {"parameters": {"respondWith": "json", "responseBody":
        "={{ { get: $('Get Template').first().json.statusCode ?? null, get_ok: !!($('Get Template').first().json.body || {}).nodes,"
        " create: $('Create Dummy').first().json.statusCode ?? null, created_id: " + created_id + " ?? null,"
        " activate: $('Activate Dummy').first().json.statusCode ?? null, active_after: ($('Activate Dummy').first().json.body || {}).active ?? null,"
        " delete: $('Delete Dummy').first().json.statusCode ?? null } }}", "options": {}},
     "name": "Respond", "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "position": [1000, 0]},
]
conn = {}
order = ["Webhook", "Get Template", "Create Dummy", "Activate Dummy", "Delete Dummy", "Respond"]
for a, b in zip(order, order[1:]):
    conn[a] = {"main": [[{"node": b, "type": "main", "index": 0}]]}
probe = {"name": f"zz crprobe {tag}", "nodes": nodes, "connections": conn, "settings": {"executionOrder": "v1"}}

probe_id = None
result = None
try:
    st, d = uc.call(BASE, "POST", "/workflows", probe)
    print("create probe:", st)
    if st != 200:
        sys.exit(f"probe create failed {st} {d}")
    probe_id = d["id"]
    st, d = uc.call(BASE, "POST", f"/workflows/{probe_id}/activate")
    print("activate probe:", st, d.get("active") if isinstance(d, dict) else d)
    time.sleep(2)
    req = urllib.request.Request(f"{BASE}/webhook/{probe_path}", data=b"{}", method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            result = r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        result = e.code, e.read().decode()[:500]
    print("probe answer:", result)
finally:
    if probe_id:
        # n8n flushes insights every 30 s; a flush naming an already deleted workflow fails and is retried
        # forever (seen on prod 2026-10-12 — this run deleted at once, cured by a restart). Wait it out first.
        time.sleep(35)
        print("deactivate probe:", uc.call(BASE, "POST", f"/workflows/{probe_id}/deactivate")[0])
        print("delete probe:", uc.call(BASE, "DELETE", f"/workflows/{probe_id}")[0])
    # belt and braces: nothing named like ours may remain
    left = [w for w in uc.list_workflows(BASE) if tag in (w.get("name") or "")]
    for w in left:
        print("leftover -> cleanup", w["id"], w["name"], uc.call(BASE, "POST", f"/workflows/{w['id']}/deactivate")[0],
              uc.call(BASE, "DELETE", f"/workflows/{w['id']}")[0])
    print("leftovers after cleanup:", [w["id"] for w in uc.list_workflows(BASE) if tag in (w.get("name") or "")])
