#!/usr/bin/env node
/**
 * Composed-value gate for the «Вместе» panel's niche prompts.
 *
 *   node Tools/n8n/verify-panel-prompts.js
 *
 * Executes the REAL `Assemble` jsCode out of the canonical Suggest_Replies workflow with
 * stubbed $()/$input, and asserts on the systemPrompt string the LLM node would actually
 * receive. Asserting that PANEL_PROMPTS is populated is NOT enough -- a payload can pass
 * every structural layer while the composed value is wrong (2026-07 Phase-10 lesson).
 *
 * A SENTINEL is a distinctive verbatim substring of one panel prompt. Sentinels must be
 * unique across the six files; a collision fails the run, so this table cannot silently
 * rot as the Russian copy is edited.
 *
 * Exit 0 = all checks pass, 1 = any failure.
 */
const fs = require('fs');
const path = require('path');

const ROOT = __dirname;
const WORKFLOW = path.join(ROOT, 'workflows', '9PTyYcelRQI7bGDb-Suggest_Replies.json');
const PANEL_DIR = path.join(ROOT, 'prompts', 'panel');

const SENTINELS = {
  auto_parts:   'попроси VIN',
  wholesale:    'неверная цена в опте стоит дорого',
  flowers:      'текст открытки',
  kaspi_seller: 'оформление только через Kaspi',
  education:    'имя и возраст ученика',
  phone_repair: 'выключить устройство, не заряжать',
};

const NICHE_HEADER = 'НИША (';
const LEGACY_ID = 'car_service';   // pre-vertical id; the app sends "" for these, but a
                                   // stray value must also produce no block.

let failures = 0;
function check(name, cond, detail) {
  if (cond) { console.log(`  OK    ${name}`); return; }
  failures++;
  console.log(`  FAIL  ${name}${detail ? `\n        ${detail}` : ''}`);
}

// --- extract the real Assemble code -----------------------------------------
const wf = JSON.parse(fs.readFileSync(WORKFLOW, 'utf8'));
const assemble = wf.nodes.find((n) => n.name === 'Assemble');
if (!assemble) { console.error('ERROR: Assemble node not found'); process.exit(1); }
const CODE = assemble.parameters.jsCode;

function runAssemble(businessTypeId) {
  const p = {
    v: 1, requestSeq: 1, invalid: false,
    profileId: 'probe', chatId: 'probe@c.us',
    botWaId: '-1', botTgId: '-1', channel: 'whatsapp',
    businessTypeId,
    businessName: 'Тест', ownerPrompt: 'Всегда предлагай доставку.',
    catalog: '• Товар — 100 тг',
    businessKnowledge: 'About Business:\nТест.',
    now: '2026-08-11 12:00, вторник',
    pickStats: 'Ответ:3',
    steerTowardText: null, lastIncomingText: null,
    messages: [{ role: 'client', text: 'Сколько стоит?', ts: 1754800000 }],
    queryText: 'Сколько стоит?', skipRag: true,
  };
  const $ = (name) => {
    if (name === 'Prep') return { first: () => ({ json: p }) };
    throw new Error(`unexpected $(${name}) in Assemble`);
  };
  const $input = { all: () => [] };
  // new Function is deliberate and is the point of this harness: it runs the REAL Assemble
  // body so the assertions see what n8n composes, not a re-implementation that could drift.
  // CODE is the committed workflow JSON in this repo -- never user input -- and it is passed
  // as a whole body, never interpolated into. Do not "fix" this by parsing the code as text.
  // eslint-disable-next-line no-new-func
  return new Function('$', '$input', CODE)($, $input)[0].json.systemPrompt;
}

// --- 1. sentinel table integrity --------------------------------------------
console.log('sentinel table');
const ids = Object.keys(SENTINELS);
const seen = new Map();
for (const id of ids) {
  const s = SENTINELS[id];
  if (seen.has(s)) { check(`${id}: sentinel unique`, false, `collides with ${seen.get(s)}`); }
  else { seen.set(s, id); check(`${id}: sentinel unique`, true); }
}
for (const id of ids) {
  const file = path.join(PANEL_DIR, `${id}.md`);
  const body = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
  check(`${id}: sentinel present in panel/${id}.md`, body.includes(SENTINELS[id]));
  // A sentinel must identify ONE vertical: it may not appear in any other panel file.
  for (const other of ids) {
    if (other === id) continue;
    const ob = fs.readFileSync(path.join(PANEL_DIR, `${other}.md`), 'utf8');
    if (ob.includes(SENTINELS[id])) {
      check(`${id}: sentinel absent from panel/${other}.md`, false, 'sentinel is not distinctive');
    }
  }
}

// --- 2. composed systemPrompt carries the niche block ------------------------
console.log('\ncomposed systemPrompt (per vertical)');
for (const id of ids) {
  const sp = runAssemble(id);
  check(`${id}: contains sentinel`, sp.includes(SENTINELS[id]),
        `sentinel "${SENTINELS[id]}" missing from composed prompt`);
  check(`${id}: contains subordinating НИША header`, sp.includes(NICHE_HEADER));
  // Placement contract: the niche block sits BEFORE the steer and the owner's Промпт,
  // both of which are meant to be read against it.
  const iNiche = sp.indexOf(NICHE_HEADER);
  const iOwner = sp.indexOf('ДОП. ИНСТРУКЦИИ ВЛАДЕЛЬЦА');
  check(`${id}: НИША precedes ДОП. ИНСТРУКЦИИ`, iNiche >= 0 && iOwner > iNiche,
        `niche@${iNiche} owner@${iOwner}`);
}

// --- 3. empty / legacy ids emit NO niche block -------------------------------
console.log('\ncomposed systemPrompt (empty + legacy id)');
for (const id of ['', LEGACY_ID]) {
  const label = id === '' ? '(empty)' : id;
  const sp = runAssemble(id);
  check(`${label}: no НИША block`, !sp.includes(NICHE_HEADER));
  const leaked = ids.filter((v) => sp.includes(SENTINELS[v]));
  check(`${label}: no vertical sentinel leaks`, leaked.length === 0, `leaked: ${leaked.join(', ')}`);
  // The rest of the system prompt must still be intact.
  check(`${label}: base rules still present`, sp.includes('ФАКТЫ (ГРАУНДИНГ)') && sp.includes('ВЫВОД:'));
}

console.log(failures === 0 ? '\nPASS' : `\nFAIL (${failures})`);
process.exit(failures === 0 ? 0 : 1);
