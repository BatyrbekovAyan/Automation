#!/usr/bin/env python3
"""Stage 7.1 — the SAFE test dialog: the owner talks to their bot's REAL composed prompt (read from the
bot's workflow through the admin API — never a copy), with the same model settings and the same
retrieval tool, but nothing leaves: no send, no memory write, no dialog quota, and the handoff tool is
a DRY twin that only records «would have created a request». The response names the sources the
question could draw on (catalog / contacts / uploaded documents) and the action the bot would have
taken, plus the correlation id. Owner verification is the same A1 gate (profileId ∈ p, workflowId ∈ w).

Generated (edit here, re-run; ids fixed):
  crTestDialog0001-Test_Dialog.json        /webhook/TestDialog  {profileId, workflowId, channel, text, history?}
  crHandoffDry0001-Handoff_Request_Dry.json   the dry create_handoff target

    python3 Tools/n8n/build-test-dialog.py            # write workflows/*.json
    python3 Tools/n8n/build-test-dialog.py --check
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
_spec = importlib.util.spec_from_file_location("build_auth", os.path.join(HERE, "build-auth-workflows.py"))
auth = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(auth)
_spec2 = importlib.util.spec_from_file_location("hh", os.path.join(HERE, "apply-history-handoff.py"))
hh = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(hh)
TEST_ID, TEST_NAME = "crTestDialog0001", "Test Dialog"
DRY_ID, DRY_NAME = "crHandoffDry0001", "Handoff Request (dry)"
ADMIN_CRED = {"id": "__HDR_CRED__", "name": "n8n Admin API"}          # bound per instance (deploy substitutes; staging remaps by name)
OPENAI_CRED = {"id": "XVjhR1xlWrIgJjKz", "name": "OpenAi account"}   # the bot templates' LLM credential
SUPABASE_CRED = {"id": "ifCYb9DhUxoCq3pu", "name": "Supabase"}


def nid(*p):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "test-dialog:" + ":".join(p)))


VALIDATE_JS = r"""// TEST DIALOG / Validate (generated — build-test-dialog.py). Owner-scoped by the gate; defensive re-check.
const wh = $input.first().json || {};
const b = wh.body || {};
const owned = wh.crAuth || {};
const s = (v, max) => (typeof v === 'string' ? v.trim().slice(0, max) : '');
const profileId = s(b.profileId, 128), workflowId = s(b.workflowId, 64), text = s(b.text, 2000);
const channel = b.channel === 'telegram' ? 'telegram' : 'whatsapp';
if (!profileId || !workflowId || !text) return [{ json: { ok: false, error: 'bad_request' } }];
if (!(owned.profiles || []).includes(profileId) || !(owned.workflows || []).includes(workflowId)) return [{ json: { ok: false, error: 'forbidden' } }];
const history = (Array.isArray(b.history) ? b.history : []).slice(-10)
  .map(m => ({ role: (m && m.role === 'business') ? 'business' : 'client', text: s(m && m.text, 500) })).filter(m => m.text);
return [{ json: { ok: true, profileId, workflowId, channel, text, history, runId: String($execution.id),
                  filterKey: channel === 'telegram' ? 'botTgId' : 'botWaId' } }];
"""

COMPOSE_JS = r"""// TEST DIALOG / Compose: the bot's REAL system message (the AI Agent node of its own workflow) + what
// knowledge it carries. Nothing is re-composed from client data — a test that lies about the prompt is worthless.
const v = $('Validate').first().json;
const r = $input.first().json || {};
const wf = (r.body && typeof r.body === 'object') ? r.body : r;
const nodes = Array.isArray(wf.nodes) ? wf.nodes : [];
const agent = nodes.find(n => n.name === 'AI Agent') || nodes[5];
const sm = agent && agent.parameters && agent.parameters.options ? String(agent.parameters.options.systemMessage || '') : '';
if (!sm) return [{ json: { ok: false, error: 'bot_prompt_unavailable', detail: 'http ' + (r.statusCode || 0) } }];
// the transcript the model sees: the owner's synthetic history + the test message (no memory node — the bot's memory is untouched)
const lines = v.history.map(m => (m.role === 'client' ? 'Клиент: ' : 'Бизнес: ') + m.text);
const text = lines.length ? lines.join('\n') + '\nКлиент: ' + v.text : v.text;
return [{ json: { ok: true, systemMessage: sm, text, sources: {
  catalog: /Product1:|Service1:/.test(sm), contacts: /Контакты:/.test(sm), businessText: /About Business:\s*\S/.test(sm.replace(/About Business:\s*\n\s*Контакты:/, '')) } } }];
"""

SOURCES_JS = r"""// TEST DIALOG / Sources: the price-list fragments this question could draw on (same table, same per-bot scope
// as the bot's own retrieval tool), listed for the owner — never sent to the client.
const docs = [];
for (const it of $input.all()) {
  const j = it && it.json;
  if (!j || j.error) continue;
  const d = j.document || j;
  const content = d && (d.pageContent || d.content);
  if (content) docs.push({ snippet: String(content).slice(0, 160), fileId: (d.metadata || {}).fileId || null });
}
const c = $('Compose').first().json;
return [{ json: { text: c.text, systemMessage: c.systemMessage, sources: { ...c.sources, documents: docs } } }];
"""

READ_ACTIONS_SQL = """-- the dry tool's ledger for THIS run (+ the daily sweep of old rows)
with sweep as (delete from test_dialog_actions where created_at < now() - interval '1 day' returning 1)
select coalesce(json_agg(json_build_object('action', a.action, 'args', a.args) order by a.id), '[]'::json) as actions
from test_dialog_actions a where a.run_id = $1;"""

RESPOND_JS = r"""// TEST DIALOG / Build Response: the reply, the sources it could use, the action it would have taken, the
// correlation id — and an honest error when the model or the prompt read failed.
const v = $('Validate').first().json;
let reply = '';
try { reply = String($('AI Agent').first().json.output || ''); } catch (e) { reply = ''; }
let sources = {};
try { sources = $('Sources').first().json.sources || {}; } catch (e) { sources = {}; }
let actions = [];
try { actions = $input.first().json.actions || []; } catch (e) { actions = []; }
const handoff = actions.find(a => a.action === 'create_handoff');
if (!reply.trim()) return [{ json: { success: false, error: 'generation_failed', correlationId: v.runId, _status: 200 } }];
return [{ json: { success: true, reply, sources, wouldHandoff: handoff ? (handoff.args || {}) : null,
                  wouldSend: true, sent: false, quotaCounted: false, correlationId: v.runId, _status: 200 } }];
"""

DRY_TOOL_INPUTS = {"run_id": "={{ $('Validate').first().json.runId }}", **hh.FROM_AI}


def tool_node():
    schema = [{"id": k, "displayName": k, "required": False, "defaultMatch": False, "display": True, "canBeUsedToMatch": True, "type": "string", "removed": False}
              for k in DRY_TOOL_INPUTS]
    return {"parameters": {"description": hh.TOOL_DESCRIPTION, "workflowId": {"__rl": True, "value": DRY_ID, "mode": "id"},
                           "workflowInputs": {"mappingMode": "defineBelow", "value": dict(DRY_TOOL_INPUTS), "matchingColumns": [], "schema": schema,
                                              "attemptToConvertTypes": False, "convertFieldsToString": False}},
            "id": nid(TEST_ID, "tool"), "name": "Create Handoff", "type": "@n8n/n8n-nodes-langchain.toolWorkflow", "typeVersion": 2.2, "position": [1540, 420]}


def vector_node(name, mode, pos):
    p = {"mode": mode, "tableName": {"__rl": True, "value": "documents", "mode": "list", "cachedResultName": "documents"},
         "options": {"queryName": "match_documents", "metadata": {"metadataValues": [{"name": "={{ $('Validate').first().json.filterKey }}", "value": "={{ $('Validate').first().json.workflowId }}"}]}}}
    if mode == "load":
        p.update({"prompt": "={{ $('Validate').first().json.text }}", "topK": 5, "includeDocumentMetadata": True})
    else:
        p.update({"toolDescription": "Retrieve product details, prices, services, and any uploaded catalog or document content for this business. Use whenever the customer asks about products, pricing, availability, or specifics that may be in uploaded documents.",
                  "topK": 10, "useReranker": False})
    n = {"parameters": p, "id": nid(TEST_ID, name), "name": name, "type": "@n8n/n8n-nodes-langchain.vectorStoreSupabase", "typeVersion": 1.3,
         "position": pos, "credentials": {"supabaseApi": dict(SUPABASE_CRED)}}
    if mode == "load":
        n["alwaysOutputData"] = True
        n["onError"] = "continueRegularOutput"
    return n


def build_test():
    n, c = [], {}
    n.append(auth.webhook("TestDialog", [0, 300]))
    n.append(auth.code("Auth Gate", auth.gate_code({"profiles": ["profileId"], "workflows": ["workflowId"], "appUser": [], "legacy": False}), [220, 300]))
    n.append(auth.if_true("Authorized?", "={{ $json.crAuth.ok }}", [440, 300]))
    n.append(auth.respond("Respond 401", [660, 520], body="={{ { success: false, error: 'unauthorized', reason: $json.crAuth.reason } }}",
                          code_expr="={{ String($json.crAuth.reason || '').startsWith('forbidden') ? 403 : 401 }}"))
    n.append(auth.code("Validate", VALIDATE_JS, [660, 200]))
    n.append(auth.if_true("Valid?", "={{ $json.ok }}", [880, 200]))
    n.append(auth.respond("Respond 400", [1100, 420], body="={{ { success: false, error: $json.error || 'bad_request' } }}", code_expr="={{ $json.error === 'forbidden' ? 403 : 400 }}"))
    n.append({"parameters": {"method": "GET", "url": "=http://localhost:5678/api/v1/workflows/{{ $json.workflowId }}", "authentication": "genericCredentialType",
                             "genericAuthType": "httpHeaderAuth", "options": {"timeout": 15000, "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}},
              "id": nid(TEST_ID, "read"), "name": "Read Bot Workflow", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2, "position": [1100, 100],
              "credentials": {"httpHeaderAuth": dict(ADMIN_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"})
    n.append(auth.code("Compose", COMPOSE_JS, [1320, 100]))
    n.append(auth.if_true("Prompt OK?", "={{ $json.ok }}", [1540, 100]))
    n.append(auth.respond("Respond Unavailable", [1760, 300], body="={{ { success: false, error: $json.error || 'bot_prompt_unavailable', detail: $json.detail } }}", code_expr=502))
    n.append(vector_node("Probe Documents", "load", [1760, 100]))
    n.append({"parameters": {"model": "text-embedding-3-small"}, "id": nid(TEST_ID, "emb"), "name": "OpenAI Embedding", "type": "@n8n/n8n-nodes-langchain.embeddingsOpenAi",
              "typeVersion": 1.2, "position": [1760, 300], "credentials": {"openAiApi": dict(OPENAI_CRED)}})
    n.append(auth.code("Sources", SOURCES_JS, [1980, 100]))
    n.append({"parameters": {"promptType": "define", "text": "={{ $json.text }}", "options": {"systemMessage": "={{ $json.systemMessage }}"}},
              "id": nid(TEST_ID, "agent"), "name": "AI Agent", "type": "@n8n/n8n-nodes-langchain.agent", "typeVersion": 1.8, "position": [2200, 100],
              "alwaysOutputData": True, "onError": "continueRegularOutput"})
    n.append({"parameters": {"model": {"__rl": True, "mode": "list", "value": "gpt-5.4-mini"}, "options": {"frequencyPenalty": 0.2, "maxTokens": 200, "responseFormat": "text", "temperature": 0.2, "maxRetries": 2}},
              "id": nid(TEST_ID, "llm"), "name": "OpenAI", "type": "@n8n/n8n-nodes-langchain.lmChatOpenAi", "typeVersion": 1.2, "position": [2200, 320],
              "credentials": {"openAiApi": dict(OPENAI_CRED)}})
    n.append(vector_node("Supabase Vector Store", "retrieve-as-tool", [2380, 420]))
    n.append({"parameters": {"model": "text-embedding-3-small"}, "id": nid(TEST_ID, "emb2"), "name": "OpenAI Embedding Tool", "type": "@n8n/n8n-nodes-langchain.embeddingsOpenAi",
              "typeVersion": 1.2, "position": [2380, 600], "credentials": {"openAiApi": dict(OPENAI_CRED)}})
    n.append(tool_node())
    n.append(auth.postgres("Read Actions", READ_ACTIONS_SQL, "={{ [ $('Validate').first().json.runId ] }}", [2420, 100]))
    n.append(auth.code("Build Response", RESPOND_JS, [2640, 100]))
    n.append(auth.respond("Respond", [2860, 100]))
    auth.edge(c, "Webhook", "Auth Gate"); auth.edge(c, "Auth Gate", "Authorized?")
    auth.edge(c, "Authorized?", "Validate", 0); auth.edge(c, "Authorized?", "Respond 401", 1)
    auth.edge(c, "Validate", "Valid?")
    auth.edge(c, "Valid?", "Read Bot Workflow", 0); auth.edge(c, "Valid?", "Respond 400", 1)
    auth.edge(c, "Read Bot Workflow", "Compose"); auth.edge(c, "Compose", "Prompt OK?")
    auth.edge(c, "Prompt OK?", "Probe Documents", 0); auth.edge(c, "Prompt OK?", "Respond Unavailable", 1)
    auth.edge(c, "Probe Documents", "Sources"); auth.edge(c, "Sources", "AI Agent")
    auth.edge(c, "AI Agent", "Read Actions"); auth.edge(c, "Read Actions", "Build Response"); auth.edge(c, "Build Response", "Respond")
    c["OpenAI Embedding"] = {"ai_embedding": [[{"node": "Probe Documents", "type": "ai_embedding", "index": 0}]]}
    c["OpenAI Embedding Tool"] = {"ai_embedding": [[{"node": "Supabase Vector Store", "type": "ai_embedding", "index": 0}]]}
    c["OpenAI"] = {"ai_languageModel": [[{"node": "AI Agent", "type": "ai_languageModel", "index": 0}]]}
    c["Supabase Vector Store"] = {"ai_tool": [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]]}
    c["Create Handoff"] = {"ai_tool": [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]]}
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False, "executionTimeout": 60}
    return auth.workflow(TEST_ID, TEST_NAME, n, c, settings)


DRY_RECORD_SQL = """insert into test_dialog_actions (run_id, action, args)
values ($1, 'create_handoff', jsonb_build_object('reason', $2, 'name', $3, 'phone', $4, 'summary', $5))
returning id;"""
DRY_RESULT_JS = r"""// HANDOFF (DRY) / Tool Result: identical to the real tool's success so the model behaves exactly as in
// production — the request row does NOT exist and nobody was notified (the Test Dialog says so to the owner).
return [{ json: { ok: true, handoff_id: 0, created: true, status: 'open', note: 'Заявка записана, владелец получит уведомление. Можно сказать клиенту, что заявка передана.' } }];
"""


def build_dry():
    n, c = [], {}
    n.append({"parameters": {"inputSource": "workflowInputs", "workflowInputs": {"values": [{"name": k, "type": "string"} for k in ("run_id", "reason", "name", "phone", "summary")]}},
              "id": nid(DRY_ID, "trigger"), "name": "When Executed by Another Workflow", "type": "n8n-nodes-base.executeWorkflowTrigger", "typeVersion": 1.1, "position": [0, 200]})
    rec = auth.postgres("Record Intent", DRY_RECORD_SQL, "={{ [ String($json.run_id || ''), String($json.reason || 'question'), String($json.name || ''), String($json.phone || ''), String($json.summary || '') ] }}", [220, 200])
    n.append(rec)
    n.append(auth.code("Tool Result", DRY_RESULT_JS, [440, 200]))
    auth.edge(c, "When Executed by Another Workflow", "Record Intent"); auth.edge(c, "Record Intent", "Tool Result")
    return auth.workflow(DRY_ID, DRY_NAME, n, c, {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False})


def main():
    stale = []
    for wf, fname in ((build_test(), f"{TEST_ID}-Test_Dialog.json"), (build_dry(), f"{DRY_ID}-Handoff_Request_Dry.json")):
        path = os.path.join(WF, fname)
        text = json.dumps(wf, indent=2, ensure_ascii=False) + "\n"
        if "--check" in sys.argv:
            if not os.path.exists(path) or open(path, encoding="utf-8").read() != text:
                stale.append(fname)
        else:
            open(path, "w", encoding="utf-8").write(text)
            print("wrote", fname, f"({len(wf['nodes'])} nodes)")
    if "--check" in sys.argv:
        print("test dialog:", "OK" if not stale else f"STALE {stale}")
        sys.exit(2 if stale else 0)


if __name__ == "__main__":
    main()
