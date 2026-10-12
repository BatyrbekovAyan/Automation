// Behavioural driver for T6 (Q1 B + V6-unknown, follow-ups 2026-10-11, F1F2-plan §4): what the MODEL sees of the
// bot's memory. Offline — no model, no network, no database connection.
//
// The bot templates' «Chat Memory» node (@n8n/n8n-nodes-langchain memoryPostgresChat 1.3) builds
// PostgresChatMessageHistory({pool, sessionId, tableName}) wrapped in BufferWindowMemory({memoryKey: 'chat_history',
// returnMessages: true, inputKey: 'input', outputKey: 'output', k: contextWindowLength}); the Tools Agent puts the
// system message, that history and the human input before the model, and lmChatOpenAi (1.2, Chat Completions) turns
// LangChain messages into OpenAI messages with convertMessagesToCompletionsMessageParams — which carries ONLY role and
// content: `additional_kwargs` (source, delivery, execution id) never reach the model (independent review 2026-10-11,
// pipeline-delivery-context.json). This driver runs those very classes from the staging n8n install (= the prod
// version), resolved the way the memory node resolves them. The pool is the one synthetic part: it answers the class's
// own SELECT with the rows the test read from Postgres with that same statement, and refuses any other statement — so
// a change of the class's query (table, order, columns) fails here instead of passing on a stale assumption.
//
//   node memory_context.js < {cases: {<name>: {sessionId, tableName, rows: [message jsonb, …], k, system, input, model}}}
//   -> {<name>: {statements: [{sql, params}], history: [{type, content}], modelMessages: [{role, content}, …]}}
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');

const N = process.env.N8N_MODULES || path.join(os.homedir(), '.n8n-staging/app/node_modules');
const LC = path.join(N, '@n8n/n8n-nodes-langchain');            // the memory node's own resolution root
const from = (id, root) => require(require.resolve(id, { paths: [root] }));
const { PostgresChatMessageHistory } = from('@langchain/community/stores/message/postgres', LC);
const { BufferWindowMemory } = from('@langchain/classic/memory', LC);
const communityDir = path.dirname(require.resolve('@langchain/community/stores/message/postgres', { paths: [LC] }));
const { SystemMessage, HumanMessage } = from('@langchain/core/messages', communityDir);
const openaiMain = require.resolve('@langchain/openai', { paths: [LC] });
const { convertMessagesToCompletionsMessageParams } = require(path.join(path.dirname(openaiMain), 'converters/completions.cjs'));

function recordingPool(c, statements) {
  const select = `SELECT message FROM ${c.tableName} WHERE session_id = $1 ORDER BY id`;
  return {
    query: async (sql, params) => {
      statements.push({ sql: String(sql).replace(/\s+/g, ' ').trim(), params: params || [] });
      if (/^\s*CREATE TABLE IF NOT EXISTS /i.test(sql)) return { rows: [] };
      if (sql === select) {
        if (!params || params[0] !== c.sessionId) throw new Error('memory read for another session: ' + JSON.stringify(params));
        return { rows: c.rows.map((message) => ({ message })) };
      }
      throw new Error('statement the test did not run against Postgres: ' + sql);
    },
  };
}

async function runCase(c) {
  const statements = [];
  const chatHistory = new PostgresChatMessageHistory({ pool: recordingPool(c, statements), sessionId: c.sessionId, tableName: c.tableName });
  const memory = new BufferWindowMemory({ memoryKey: 'chat_history', chatHistory, returnMessages: true, inputKey: 'input', outputKey: 'output', k: c.k });
  const vars = await memory.loadMemoryVariables({ input: c.input });
  const history = vars.chat_history;
  const messages = [new SystemMessage(c.system), ...history, new HumanMessage(c.input)];
  const modelMessages = convertMessagesToCompletionsMessageParams({ messages, model: c.model || 'gpt-5.4-mini' });
  return {
    statements,
    history: history.map((m) => ({ type: m._getType ? m._getType() : m.getType(), content: m.content })),
    modelMessages: modelMessages.map((m) => ({ role: m.role, content: m.content })),
  };
}

(async () => {
  const input = JSON.parse(fs.readFileSync(0, 'utf8'));
  const out = {};
  for (const [name, c] of Object.entries(input.cases)) out[name] = await runCase(c);
  process.stdout.write(JSON.stringify(out));
})().catch((e) => { process.stderr.write(String(e && e.stack || e)); process.exit(1); });
