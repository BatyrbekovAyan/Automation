#!/usr/bin/env node
// The bot template's two agent tools exactly as n8n hands them to the model — built with n8n's OWN code from the staging
// install (n8n-workflow nodeNameToToolName, @n8n/ai-utilities $fromAI → zod, @langchain/core convertToOpenAITool with the
// strict:false the lmChatOpenAi node passes). Reads files only; never talks to the running stand. One JSON line per tool.
//
//   node Tools/n8n/eval/n8n-tools.js Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
//   node Tools/n8n/eval/n8n-tools.js --default-headers     # the headers n8n adds to every OpenAI chat-model call (AiConfig)
//
// run-eval.py's n8n_tools() is the Python mirror the --request n8n mode sends; test_checks.py N8nRequest compares the two.
const os = require('os');
const path = require('path');

const home = (process.env.N8N_STAGING_HOME || '~/.n8n-staging').replace(/^~(?=\/|$)/, os.homedir());
const nm = path.join(home, 'app', 'node_modules');
const req = (p) => require(path.join(nm, p));
if (process.argv[2] === '--default-headers') {
  // lmChatOpenAi passes these as the client's defaultHeaders (n8n 2.36.7: a getter, no env var — every install sends them)
  req('reflect-metadata');
  console.log(JSON.stringify(new (req('@n8n/config/dist/configs/ai.config.js').AiConfig)().openAiDefaultHeaders));
  process.exit(0);
}
const ai = req('@n8n/ai-utilities');
const wf = req('n8n-workflow');
const tools = req('@langchain/core/tools');
const fc = req('@langchain/core/utils/function_calling');

const template = require(path.resolve(process.argv[2]));
for (const name of ['Supabase Vector Store', 'Create Handoff']) {
  const node = template.nodes.find((n) => n.name === name);
  const schema = ai.createZodSchemaFromArgs(ai.extractFromAIParameters(node.parameters));
  const tool = new tools.DynamicStructuredTool({ schema, name: wf.nodeNameToToolName(node), description: node.parameters.description, func: async () => '' });
  console.log(JSON.stringify(fc.convertToOpenAITool(tool, { strict: false })));
}
