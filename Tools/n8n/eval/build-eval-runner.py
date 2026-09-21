#!/usr/bin/env python3
"""STAGING-ONLY thin completions proxy for the stage-6 eval runner. Never part of the canonical
set (Tools/n8n/workflows/) and never deployed to prod: it lives in Tools/n8n/staging/workflows/,
which derive-staging.py imports next to the canonical workflows. It forwards ONE chat-completions
request body to OpenAI with the n8n credential (no key ever leaves n8n) and returns the raw
response (finish_reason, usage, tool calls) — what the LangChain agent node hides.

    python3 Tools/n8n/eval/build-eval-runner.py            # writes staging/workflows/stgEvalRunner001-Eval_Runner.json
"""
import importlib.util
import json
import os
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
N8N = os.path.dirname(HERE)
OUT = os.path.join(N8N, "staging", "workflows", "stgEvalRunner001-Eval_Runner.json")
_spec = importlib.util.spec_from_file_location("build_auth", os.path.join(N8N, "build-auth-workflows.py"))
auth = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(auth)
WF_ID, WF_NAME = "stgEvalRunner001", "STAGING Eval Runner"
OPENAI_CRED = {"id": "WNHwKWlO2E9OClkA", "name": "OpenAi account"}   # remapped by name on staging


def nid(*p):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "eval-runner:" + ":".join(p)))


def main():
    n, c = [], {}
    n.append(auth.webhook("EvalRun", [0, 200]))
    n.append({"parameters": {"method": "POST", "url": "https://api.openai.com/v1/chat/completions", "authentication": "predefinedCredentialType",
                             "nodeCredentialType": "openAiApi", "sendBody": True, "specifyBody": "json",
                             "jsonBody": "={{ JSON.stringify($json.body.request || {}) }}",
                             "options": {"timeout": 90000, "response": {"response": {"fullResponse": True, "neverError": True, "responseFormat": "json"}}}},
              "id": nid("forward"), "name": "Forward", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2, "position": [260, 200],
              "credentials": {"openAiApi": dict(OPENAI_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"})
    n.append(auth.respond("Respond", [520, 200], body="={{ { status: $json.statusCode || 0, body: $json.body || null, error: $json.error || null } }}", code_expr=200))   # an INTEGER: a "200" string makes Express throw
    auth.edge(c, "Webhook", "Forward")
    auth.edge(c, "Forward", "Respond")
    wf = auth.workflow(WF_ID, WF_NAME, n, c, {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False})
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    print("wrote", os.path.relpath(OUT, N8N))


if __name__ == "__main__":
    main()
