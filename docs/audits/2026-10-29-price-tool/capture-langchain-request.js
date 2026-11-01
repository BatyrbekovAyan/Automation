#!/usr/bin/env node
// The HTTP request the bot's AI Agent sends to OpenAI, built by n8n's OWN code from the staging install (n8n 2.36.7: the
// Tools Agent V1 prompt, @langchain/classic createToolCallingAgent + AgentExecutor, @langchain/openai ChatOpenAI with the
// fields lmChatOpenAi v1.2 passes, the template's two toolWorkflow tools) — sent to a LOCAL capture server, never to OpenAI
// (a dummy key; the server answers with a canned «ok» completion). Writes the captured body + headers (authorization dropped).
//
//   node capture-langchain-request.js <input.json> <out.json>
//   input.json: {"template": "<bot template json>", "system": "<system message>", "history": [["human"|"ai", text], …], "input": "<question>"}
const fs = require('fs');
const http = require('http');
const os = require('os');
const path = require('path');

const home = (process.env.N8N_STAGING_HOME || '~/.n8n-staging').replace(/^~(?=\/|$)/, os.homedir());
const nlc = path.join(home, 'app', 'node_modules', '@n8n', 'n8n-nodes-langchain', 'dist');
const from = [path.join(nlc, 'nodes/agents/Agent/agents/ToolsAgent/V1'), path.join(nlc, 'nodes/llms/LMChatOpenAi'), path.join(nlc, 'utils')];
const r = (m) => require(require.resolve(m, { paths: from }));
const { ChatOpenAI } = r('@langchain/openai');
const { createToolCallingAgent, AgentExecutor } = r('@langchain/classic/agents');
const { ChatPromptTemplate } = r('@langchain/core/prompts');
const { HumanMessage, AIMessage } = r('@langchain/core/messages');
const { DynamicStructuredTool } = r('@langchain/core/tools');
const ai = r('@n8n/ai-utilities');
const wf = r('n8n-workflow');
r('reflect-metadata');
const { AiConfig } = r('@n8n/config/dist/configs/ai.config.js');

const [inputPath, outPath] = process.argv.slice(2);
const spec = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
const template = JSON.parse(fs.readFileSync(spec.template, 'utf8'));
const node = (name) => template.nodes.find((n) => n.name === name);
const opts = node('OpenAI').parameters.options;

let captured = null;
const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    const headers = { ...req.headers };
    delete headers.authorization;
    captured = { method: req.method, url: req.url, headers, body: JSON.parse(body) };
    res.setHeader('content-type', 'application/json');
    res.end(JSON.stringify({ id: 'chatcmpl-capture', object: 'chat.completion', created: 0, model: 'gpt-5.4-mini-2026-03-17',
      choices: [{ index: 0, message: { role: 'assistant', content: 'ok' }, finish_reason: 'stop' }],
      usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 } }));
  });
});

server.listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  try {
    // lmChatOpenAi v1.2 supplyData(): includedOptions + modelKwargs.response_format + supportsStrictToolCalling false
    const model = new ChatOpenAI({
      apiKey: 'sk-capture-dummy-not-a-key', model: node('OpenAI').parameters.model.value,
      frequencyPenalty: opts.frequencyPenalty, maxTokens: opts.maxTokens, temperature: opts.temperature,
      maxRetries: 0, configuration: { defaultHeaders: new AiConfig().openAiDefaultHeaders, baseURL: `http://127.0.0.1:${port}/v1` },
      modelKwargs: opts.responseFormat ? { response_format: { type: opts.responseFormat } } : {}, supportsStrictToolCalling: false,
    });
    // toolWorkflow v2.2: nodeNameToToolName + the $fromAI parameters as a zod object (WorkflowToolService.createStructuredTool)
    const tools = ['Supabase Vector Store', 'Create Handoff'].map((name) => new DynamicStructuredTool({
      name: wf.nodeNameToToolName(node(name)), description: node(name).parameters.description,
      schema: ai.createZodSchemaFromArgs(ai.extractFromAIParameters(node(name).parameters)), func: async () => '',
    }));
    // ToolsAgent V1 prepareMessages(): system, chat history, human input, scratchpad
    const prompt = ChatPromptTemplate.fromMessages([['system', '{system_message}'], ['placeholder', '{chat_history}'],
      ['human', '{input}'], ['placeholder', '{agent_scratchpad}']]);
    const agent = createToolCallingAgent({ llm: model, tools, prompt, streamRunnable: false });
    const executor = AgentExecutor.fromAgentAndTools({ agent, tools, maxIterations: 10 });
    const chatHistory = spec.history.map(([role, text]) => (role === 'human'
      ? new HumanMessage({ content: text, additional_kwargs: {}, response_metadata: {} })
      : new AIMessage({ content: text, additional_kwargs: {}, response_metadata: {}, tool_calls: [], invalid_tool_calls: [] })));
    await executor.invoke({ input: spec.input, system_message: spec.system, chat_history: chatHistory });
    fs.writeFileSync(outPath, JSON.stringify(captured, null, 1));
    console.log('captured', captured ? Object.keys(captured.body).join(',') : 'NOTHING');
  } catch (e) {
    console.error('capture failed:', e && e.message);
    process.exitCode = 1;
  } finally {
    server.close();
  }
});
