#!/usr/bin/env python3
"""Idempotent migration (S1, S3–S8, audit stage 5) of the Suggest Replies workflow:

  Prep     — the v1.5 `knowledge` object (descriptions, contacts as fields, zone, version) composed
             under a BUDGET that protects contacts and marks what was cut; legacy text fields get the
             same protection; the retrieval query recovers the SUBJECT of a short follow-up from the
             previous turns (+ the picked direction); SKU-like tokens are extracted for an exact match;
             «now» is computed from the SERVER clock in the business's zone (default + hedge for legacy).
             R12 (2026-09-27): the subject is computed BEFORE the catalog; the item the question is about keeps its
             whole description, every other cut ends with «…» and is flagged (incl. the client's 600 / 2500 clamps).
             R13 (2026-09-27): the query reserves the newest client turn(s) and the direction and trims OLD context
             first; exact tokens go newest first (codes before bare numbers); Exact Match ranks rows by token order.
             V8 (2026-09-28): the history × burst merge (the BATCH-03 legacy section, now owned between STAGE5-MERGE
             markers) aligns the clamped history turns and the unclamped burst lines in conversation ORDER — a turn
             > 500, a multi-line turn or a bare media caption no longer lands after the newest question.
             V9 (2026-09-28, panel part): `clientLang` from the newest client message (kz / mixed / ru).
             V9 (price reconciliation): `catalogItems` — the v1.5 items as {kind, name, price} for Assemble's check.
  Gate     — the daily counter is idempotent per request key (a timeout retry is not a second request).
  RAG      — the vector-store nodes fail OPEN (a search error is not «позиции нет»); an `Exact Match`
             Postgres lookup runs only when SKU-like tokens exist.
  Assemble — knowledge block + incompleteness flags, exact matches, RAG-error notice, zone hedge.
             V9 (price reconciliation, build-price-search.py's RECONCILE_JS — the SAME function the bot's and the
             Test Dialog's search run): every RAG / exact-match row naming a catalog article at another price gets a
             «[Сверка с каталогом: …]» line, and «РАСХОЖДЕНИЕ ЦЕН» tells the model what that line means.
             V9-r2 (2026-10-08): the RAG / exact windows are cut at LINE boundaries BEFORE the marks (a mark never
             displaces a row nor stands without one), RAG + exact rows are ONE search result (`reconcileAll`), and the
             hard «РАСХОЖДЕНИЕ ЦЕН» follows only a firm mark in the window — soft marks get «СВЕРКА ЦЕН».
             V9: «ЯЗЫК КЛИЕНТА — КАЗАХСКИЙ» after the niche block for a Kazakh client; ТРИВИАЛЬНЫЕ no longer overrides
             ВОЗДЕРЖАНИЕ (an emotional reaction without a request is not a thank-you).
  Validate — V9: a Kazakh client's Russian card fails the set before the one retry (strict); Validate 2 drops Russian
             cards when a Kazakh one remains, never an error (lenient) — the same asymmetry as the titles.
  Build Response — for an authenticated caller the refusal kind rides the wire (subscription / daily
             cap / unknown account / bad request); legacy callers keep the opaque generation_failed.
  Settings — executionTimeout 20 s (needs EXECUTIONS_TIMEOUT on the instance).

    python3 Tools/n8n/apply-suggest-v2.py            # patch workflows/ in place (re-runnable)
    python3 Tools/n8n/apply-suggest-v2.py --check
"""
import importlib.util
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
_ps_spec = importlib.util.spec_from_file_location("price_search", os.path.join(HERE, "build-price-search.py"))
ps = importlib.util.module_from_spec(_ps_spec)
_ps_spec.loader.exec_module(ps)
WF = os.path.join(HERE, "workflows")
FNAME = "9PTyYcelRQI7bGDb-Suggest_Replies.json"
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
EXECUTION_TIMEOUT_SEC = 20

# V8 (iteration 2026-09-28): the history × burst merge of Prep. It used to be a hand-written legacy section (BATCH-03)
# that no generator owned; it is taken over here, between markers, and anchored once on the exact legacy text.
MERGE_BEGIN, MERGE_END = "// STAGE5-MERGE-BEGIN", "// STAGE5-MERGE-END"
MERGE_LEGACY = """// Burst-combine (BATCH-03 gap fix): anchor RAG + lastClientMessage on the whole trailing
// client run (consecutive client messages since the last owner/bot reply), not only the
// final fragment — mirrors the reply path's Latest+Combine run-walk semantics.
const runParts = [];
for (let i = messages.length - 1; i >= 0; i--) {
  const m = messages[i];
  if (m.role !== 'client') break;
  if (m.text && m.text.trim()) runParts.unshift(m.text.trim());
}
// Per-line merge: lastIncomingText now carries the client's WHOLE accumulated burst
// (possibly multi-line) — append each line not already present in the trailing run.
if (lastIncomingText && lastIncomingText.trim()) {
  const seen = new Set(runParts);
  for (const rawLine of lastIncomingText.split('\\n')) {
    const line = rawLine.trim();
    if (line && !seen.has(line)) { runParts.push(line); seen.add(line); }
  }
}"""
MERGE_BLOCK = r"""// STAGE5-MERGE-BEGIN (generated by Tools/n8n/apply-suggest-v2.py — edit there)
// Burst-combine (BATCH-03 gap fix): anchor RAG + lastClientMessage on the whole trailing client run (consecutive client
// messages since the last owner/bot reply), not only the final fragment — mirrors the reply path's Latest+Combine.
// V8 (2026-09-28): the client sends that run TWICE, in two shapes — `messages` (every turn clamped to HISTORY_TURN_MAX,
// media as «[фото] caption») and `lastIncomingText` (the whole burst: fragments joined by '\n', unclamped, bare captions).
// A whole-turn ≠ line comparison appended a clamped turn's full line, every line of a multi-line turn and a bare caption
// AFTER the real newest question, which then fell out of trailingText. The two are aligned in conversation ORDER with a
// cursor instead: a turn takes its place where its lines match the burst (the last line of a clamped turn by prefix, the
// media placeholder stripped); burst lines between two matches are the clamped turn's hidden tail (glued back onto it)
// or a desync (dropped — that turn is in `messages` already); only lines after the last match — what the history has
// not caught up with yet — are appended, de-duplicated (a re-delivered fragment). No burst: the history run as before.
const HISTORY_TURN_MAX = 500;   // = N8nSuggestionsProvider.MaxTextChars: a turn that long was clamped by the client
const MEDIA_PLACEHOLDER = /^\[(фото|видео|голосовое сообщение|документ|стикер|сообщение)\]\s*/;   // = N8nSuggestionsProvider.MediaText
const runTurns = [];
for (let i = messages.length - 1; i >= 0; i--) {
  const m = messages[i];
  if (m.role !== 'client') break;
  if (m.text && m.text.trim()) runTurns.unshift({ text: m.text.trim(), clamped: m.text.length >= HISTORY_TURN_MAX });
}
const runParts = runTurns.map(t => t.text);
if (lastIncomingText && lastIncomingText.trim()) {
  const burst = lastIncomingText.split('\n').map(s => s.trim()).filter(Boolean);
  const linesOf = (s) => s.split('\n').map(x => x.trim()).filter(Boolean);
  let cur = 0, prev = null;
  for (const t of runTurns) {
    t.media = (t.text.match(MEDIA_PLACEHOLDER) || [''])[0];
    t.lines = linesOf(t.text.slice(t.media.length));
    t.at = -1;
    for (let j = cur; t.lines.length && t.at < 0 && j + t.lines.length <= burst.length; j++) {
      if (t.lines.every((h, k) => burst[j + k] === h || (t.clamped && k === t.lines.length - 1 && burst[j + k].startsWith(h)))) t.at = j;
    }
    if (t.at < 0) { if (t.lines.length) prev = null; continue; }  // unmatched: the history text stays, nothing consumed
                                                                  // (a caption-less media turn has no burst line to break the tail)
    if (prev && prev.clamped) prev.tail = burst.slice(cur, t.at);   // the hidden tail of the clamped turn just before
    cur = t.at + t.lines.length;
    prev = t;
  }
  runParts.length = 0;
  for (const t of runTurns) {
    // a clamped turn is rebuilt whole from the burst (its media placeholder kept); any other keeps its history text
    runParts.push(t.at >= 0 && t.clamped ? t.media + burst.slice(t.at, t.at + t.lines.length).concat(t.tail || []).join('\n') : t.text);
  }
  // a line already seen — in a turn or EARLIER in the burst (the client drops only a repeat of the burst's tail, so a
  // re-delivered older fragment or pack lands here) — is a repeat, never the newest message
  const seen = new Set(runParts.concat(runParts.flatMap(linesOf), burst.slice(0, cur)));
  for (const line of burst.slice(cur)) {
    if (!seen.has(line)) { runParts.push(line); seen.add(line); }
  }
}
// STAGE5-MERGE-END"""

PREP_BEGIN, PREP_END = "// STAGE5-PREP-BEGIN", "// STAGE5-PREP-END"
PREP_BLOCK = r"""// STAGE5-PREP-BEGIN (generated by Tools/n8n/apply-suggest-v2.py — edit there)
const K = (b.knowledge && typeof b.knowledge === 'object') ? b.knowledge : null;
const flags = [];
const t = (v, max) => String(v === undefined || v === null ? '' : v).trim().slice(0, max);
// --- S4: the retrieval query recovers the SUBJECT of a short follow-up; exact tokens for SKUs ---
// (computed FIRST: R12 composes the catalog around the item the question is about)
// R13: the latest client turn(s) and the picked direction are RESERVED; OLD context is trimmed first (the query used to
// be cut from its END, which dropped the very question), and the tokens go newest first.
const QUERY_BUDGET = 900, TRAIL_BUDGET = 500, STEER_BUDGET = 300, MIN_PART = 40;
// newest-first packing: whole parts from the newest end while they fit; the first one that does not fit keeps its head
// (never cutting a word — an article code — in half) when at least MIN_PART chars are left; older parts are dropped
const newestFirst = (parts, max) => {
  const kept = [];
  let used = 0;
  for (let i = parts.length - 1; i >= 0; i--) {
    const part = parts[i], sep = kept.length ? 1 : 0;
    if (used + sep + part.length <= max) { kept.unshift(part); used += sep + part.length; continue; }
    const room = max - used - sep;
    if (room >= MIN_PART) {
      let head = part.slice(0, room);
      const k = head.search(/\s\S*$/);
      if (/\S/.test(part[room] || '') && k > 0) head = head.slice(0, k);
      if (head.trim()) kept.unshift(head.trimEnd());
    }
    break;
  }
  return kept;
};
const trailParts = newestFirst(runParts, TRAIL_BUDGET);   // a long series keeps its NEWEST messages (also lastClientMessage)
const trailingText = trailParts.join('\n');
// V9 (2026-09-28): the client's language, from its NEWEST message (the run when that one has no letters — an emoji):
// 'kz' = a Kazakh-specific letter and no Russian-only word, 'mixed' = both, 'ru' = no Kazakh-specific letter (Kazakh
// written without them — «Салем, бар ма?» — stays unenforced: the СТИЛЬ rule still asks for the client's language).
// Only 'kz' is enforced (Assemble's rule + the strict Validate); a mixed message keeps today's behaviour.
const KZ_LETTER = /[әғқңөұүһі]/i;
const RU_ONLY = new Set(['сколько', 'что', 'как', 'есть', 'это', 'можно', 'нужно', 'нужен', 'нужна', 'нужны', 'где', 'когда', 'почему',
  'пожалуйста', 'спасибо', 'здравствуйте', 'привет', 'вы', 'вас', 'мне', 'меня', 'мы', 'я', 'и', 'в', 'на', 'за', 'с', 'по', 'для', 'из',
  'или', 'но', 'еще', 'ещё', 'какой', 'какая', 'какие', 'цена', 'стоит', 'стоимость', 'если', 'уже', 'только', 'очень', 'хочу', 'будет',
  'ли', 'вот', 'тоже', 'сейчас', 'сегодня', 'завтра']);
const langOf = (s) => {
  const words = String(s || '').toLowerCase().match(/\p{L}+/gu) || [];
  if (!words.some(w => KZ_LETTER.test(w))) return 'ru';
  return words.some(w => RU_ONLY.has(w)) ? 'mixed' : 'kz';
};
const newestTurn = trailParts.length ? trailParts[trailParts.length - 1] : '';
const clientLang = langOf(/\p{L}/u.test(newestTurn) ? newestTurn : trailingText);
const FOLLOW_UP = /^(а |и |ну |да,? |ок,? |хорошо,? )?(сколько|скок|цена|стоит|почём|почем|есть|наличи|можно|когда|где|какой|какая|какие|такой|такая|это|эта|этот|его|её|их|подойд|подход|доставк|срок)/i;
const wordCount = trailingText.trim().split(/\s+/).filter(Boolean).length;
const isShort = trailingText.trim().length <= 40 || wordCount <= 4 || FOLLOW_UP.test(trailingText.trim());
const ctx = [];
if (isShort) {
  let i = messages.length - 1;
  while (i >= 0 && messages[i].role === 'client') i--;
  let taken = 0, bizTaken = false;
  for (; i >= 0 && taken < 3; i--) {
    const mm = messages[i];
    if (!mm.text || !mm.text.trim()) continue;
    if (mm.role === 'business') { if (bizTaken) continue; bizTaken = true; ctx.unshift('[бизнес] ' + mm.text.trim()); taken++; }
    else { ctx.unshift('[клиент] ' + mm.text.trim()); taken++; }
  }
}
const steerLine = (steerTowardText && steerTowardText.trim()) ? '[направление] ' + steerTowardText.trim().slice(0, STEER_BUDGET) : '';
const fixed = [steerLine, trailingText].filter(Boolean);
const ctxKept = newestFirst(ctx, QUERY_BUDGET - fixed.join('\n').length - (fixed.length ? 1 : 0));
queryText = ctxKept.concat(fixed).join('\n');
const skuOf = (s) => (String(s || '').match(/[A-Za-z0-9][A-Za-z0-9\-\/]{4,}/g) || []).filter(x => /\d/.test(x) && !/^\d{7,}$/.test(x)).map(x => x.toUpperCase());
// the question's own codes first (its newest message first, then the direction), the older context after; within each,
// article codes before bare numbers — a price such as 18500 must not take a code's slot
const codesFirst = (parts) => { const all = parts.flatMap(skuOf); return all.filter(x => !/^\d+$/.test(x)).concat(all.filter(x => /^\d+$/.test(x))); };
const exactTokens = Array.from(new Set(codesFirst(trailParts.slice().reverse().concat(steerLine ? [steerLine] : []))
  .concat(codesFirst(ctxKept.slice().reverse())))).slice(0, 3);
// --- S1/S2 + R12: ONE knowledge contract, composed under a budget that never drops contacts. The item the question
// is about keeps its WHOLE description; everything shortened ends with «…» and is flagged (Assemble turns the flags
// into a rule) — a silent cut would present a partial condition list as complete ---
const DESC_BUDGET = 1200, ITEM_BUDGET = 3000, PIN_BUDGET = 2400, MIN_CUT = 30;
const ITEM_DESC_MAX = 600;          // = SuggestionKnowledge.MaxDescriptionChars: a description that long was clamped by the client
const LEGACY_CATALOG_MAX = 2500;    // = N8nSuggestionsProvider.MaxCatalogChars: a legacy catalog that long was clamped by the client
const cut = (s, max) => s.length <= max ? s : s.slice(0, max - 1).trimEnd() + '…';
let contactsText = '', documentsCount = 0, knowledgeVersion = '';
// V9: the catalog as data for Assemble's price check (every item, never budget-cut); a legacy client → null, Assemble
// reads its «• name — price» text instead
let catalogItems = null;
let catalog = String(b.catalog || '');
let businessKnowledge = String(b.businessKnowledge || '');
if (K) {
  const c = K.contacts || {};
  const lines = [];
  const add = (label, v) => { v = t(v, 200); if (v) lines.push(label + ': ' + v); };
  add('Телефон', c.phone); add('Часы работы', c.hours); add('Адрес', c.address); add('Instagram', c.instagram); add('Email', c.email);
  contactsText = lines.join('\n');
  let desc = t(K.description, 4000);
  if (desc.length > DESC_BUDGET) { desc = desc.slice(0, DESC_BUDGET) + '…'; flags.push('description_truncated'); }
  const items = (Array.isArray(K.items) ? K.items : []).filter(x => x && t(x.name, 120)).map((x, i) => {
    const raw = String(x.description === undefined || x.description === null ? '' : x.description).trim();
    return { i, name: t(x.name, 120), price: t(x.price, 40), raw,
             full: raw.length >= ITEM_DESC_MAX ? raw.slice(0, ITEM_DESC_MAX - 1).trimEnd() + '…' : raw };
  });
  // relevance to the question: its article codes (exact), then its words in the item's NAME — the latest turn and the
  // picked direction weigh more than the earlier context; Russian endings match through a shared stem
  const norm = (s) => String(s || '').toLowerCase().replace(/ё/g, 'е');
  const STOP = new Set(['сколько', 'стоит', 'стоят', 'стоимость', 'цена', 'цену', 'цены', 'почем', 'есть', 'наличии', 'наличие', 'можно',
    'какой', 'какая', 'какие', 'какое', 'когда', 'где', 'это', 'этот', 'эта', 'эти', 'нужен', 'нужна', 'нужно', 'нужны', 'хочу', 'хотел',
    'хотела', 'пожалуйста', 'здравствуйте', 'здравствуй', 'привет', 'добрый', 'спасибо', 'подскажите', 'скажите', 'будет', 'будут',
    'сегодня', 'завтра', 'сейчас', 'тенге', 'вообще', 'тоже', 'также', 'еще', 'клиент', 'бизнес', 'направление']);
  const wordsOf = (s) => (norm(s).match(/[\p{L}\p{N}]+/gu) || []).filter(w => (w.length >= 4 || /\d/.test(w)) && !STOP.has(w));
  const sameWord = (x, y) => {
    if (x === y) return true;
    if (/\d/.test(x) || /\d/.test(y)) return false;
    let n = 0;
    while (n < x.length && n < y.length && x[n] === y[n]) n++;
    return n >= Math.min(5, Math.min(x.length, y.length) - 1);
  };
  const hasCode = (hay, code) => /^\d+$/.test(code) ? new RegExp('(^|[^0-9])' + code + '([^0-9]|$)').test(hay) : hay.includes(code);
  const asked = trailingText + '\n' + (steerTowardText || '');
  const mainWords = Array.from(new Set(wordsOf(asked)));
  const ctxWords = Array.from(new Set(wordsOf(ctx.join('\n')))).filter(w => !mainWords.includes(w));
  const codes = Array.from(new Set(skuOf(asked + '\n' + ctx.join('\n'))));
  for (const it of items) {
    const hay = (it.name + ' ' + it.full).toUpperCase(), nw = wordsOf(it.name), dw = wordsOf(it.full);
    let s = 0;
    for (const code of codes) if (hasCode(hay, code)) s += 5;
    for (const q of mainWords) s += nw.some(w => sameWord(q, w)) ? 2 : (dw.some(w => sameWord(q, w)) ? 1 : 0);
    for (const q of ctxWords) s += nw.some(w => sameWord(q, w)) ? 1 : 0;
    it.s = s;
  }
  const line = (it, d) => '• ' + it.name + (it.price ? ' — ' + it.price : '') + (d ? ' — ' + d : '');
  // pin WHOLE relevance levels, best first: variants sharing a name are never split, a word every item shares pins nothing
  const pinned = new Set();
  let pinCost = 0;
  for (const lv of Array.from(new Set(items.map(it => it.s).filter(s => s > 0))).sort((x, y) => y - x)) {
    const group = items.filter(it => it.s === lv);
    const cost = group.reduce((n, it) => n + line(it, it.full).length + 1, 0);
    if (pinCost + cost > PIN_BUDGET) break;
    group.forEach(it => pinned.add(it));
    pinCost += cost;
  }
  const render = (descOf) => items.map(it => line(it, descOf(it))).join('\n');
  let text = render(it => it.full);
  let shortened = items.some(it => it.full !== it.raw);
  if (text.length > ITEM_BUDGET) {
    // 1) the pinned keep everything; the others share the rest — the longest per-item cut that fits
    const at = (cap) => render(it => pinned.has(it) ? it.full : cut(it.full, cap));
    let lo = MIN_CUT, hi = ITEM_DESC_MAX, best = 0;
    while (lo <= hi) { const mid = (lo + hi) >> 1; if (at(mid).length <= ITEM_BUDGET) { best = mid; lo = mid + 1; } else hi = mid - 1; }
    if (best) {
      text = at(best);
      shortened = shortened || items.some(it => !pinned.has(it) && cut(it.full, best) !== it.full);
    } else {
      // 2) not even that: the others lose their descriptions — pinned first, then by relevance, then in catalog order
      flags.push('descriptions_dropped');
      const order = items.slice().sort((x, y) => (pinned.has(y) - pinned.has(x)) || (y.s - x.s) || (x.i - y.i));
      const keep = new Set();
      let used = 0;
      for (const it of order) {
        const n = line(it, pinned.has(it) ? it.full : '').length + (keep.size ? 1 : 0);
        if (used + n > ITEM_BUDGET) continue;
        keep.add(it);
        used += n;
      }
      if (keep.size < items.length) flags.push('catalog_incomplete');
      text = items.filter(it => keep.has(it)).map(it => line(it, pinned.has(it) ? it.full : '')).join('\n');
      shortened = items.some(it => pinned.has(it) && it.full !== it.raw);
    }
  }
  if (shortened) flags.push('item_description_truncated');
  catalog = text;
  businessKnowledge = (desc ? 'About Business:\n' + desc : '') + (contactsText ? '\n\nКонтакты:\n' + contactsText : '');
  documentsCount = Number(K.documents) || 0;
  knowledgeVersion = t(K.version, 32);
  catalogItems = (Array.isArray(K.items) ? K.items : []).filter(x => x && t(x.name, 120))
    .map(x => ({ kind: x.kind === 'service' ? 'service' : 'product', name: t(x.name, 120), price: t(x.price, 40) }));
} else {
  // legacy client: the contacts block is the TAIL of businessKnowledge — protect it, trim the description
  const raw = businessKnowledge;
  const marker = '\n\nКонтакты:\n';
  const idx = raw.indexOf(marker);
  const descPart = idx >= 0 ? raw.slice(0, idx) : raw;
  contactsText = idx >= 0 ? raw.slice(idx + marker.length).slice(0, 600) : '';
  let desc = descPart.slice(0, DESC_BUDGET);
  if (descPart.length > DESC_BUDGET) { desc += '…'; flags.push('description_truncated'); }
  businessKnowledge = desc + (contactsText ? marker + contactsText : '');
  // a legacy client clamps its catalog at LEGACY_CATALOG_MAX without a mark (mid-line, mid-price): a catalog that long was cut
  if (catalog.length >= LEGACY_CATALOG_MAX) { catalog = catalog.slice(0, ITEM_BUDGET - 1).trimEnd() + '…'; flags.push('catalog_incomplete'); }
}
// --- S7: «now» from the SERVER clock in the business's zone; the phone's clock is never trusted ---
const IANA = /^[A-Za-z]+(?:\/[A-Za-z0-9_+\-]+){1,2}$/;
let tz = (K && typeof K.timezone === 'string' && IANA.test(K.timezone)) ? K.timezone : null;
let tzSource = tz ? 'bot' : 'default';
if (!tz) tz = 'Asia/Almaty';
const RU_DAYS = { Sun: 'воскресенье', Mon: 'понедельник', Tue: 'вторник', Wed: 'среда', Thu: 'четверг', Fri: 'пятница', Sat: 'суббота' };
const fmtNow = (zone) => {
  const d = new Date();
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(d);
  const g = (k) => (parts.find(p => p.type === k) || {}).value || '';
  const wd = new Intl.DateTimeFormat('en-US', { timeZone: zone, weekday: 'short' }).format(d);
  return g('year') + '-' + g('month') + '-' + g('day') + ' ' + g('hour') + ':' + g('minute') + ', ' + (RU_DAYS[wd] || wd);
};
let serverNow = '';
try { serverNow = fmtNow(tz); } catch (e) { tz = 'Asia/Almaty'; tzSource = 'default'; try { serverNow = fmtNow(tz); } catch (e2) { serverNow = ''; } }
const requestKey = String(b.requestKey || '').replace(/[^0-9a-fA-F]/g, '').slice(0, 32);
// STAGE5-PREP-END"""

PREP_RETURN_OLD = """return [{ json: {
  v: 1, requestSeq, invalid, appUserId,
  profileId: String(b.profileId || ''),
  chatId: String(b.chatId || ''),
  botWaId, botTgId, channel, businessTypeId, businessName, ownerPrompt, catalog, businessKnowledge, now, pickStats,
  steerTowardText, lastIncomingText, messages, queryText, skipRag
} }];"""
PREP_RETURN_V1 = """return [{ json: {
  v: 1, requestSeq, invalid, appUserId,
  profileId: String(b.profileId || ''),
  chatId: String(b.chatId || ''),
  botWaId, botTgId, channel, businessTypeId, businessName, ownerPrompt, catalog, businessKnowledge, now: serverNow, pickStats,
  steerTowardText, lastIncomingText, messages, queryText, trailingText, skipRag,
  knowledgeFlags: flags, contactsText, documentsCount, knowledgeVersion, tz, tzSource, exactTokens, requestKey
} }];"""
# V9: + clientLang (Assemble's language rule, the validators)
PREP_RETURN_V2 = """return [{ json: {
  v: 1, requestSeq, invalid, appUserId,
  profileId: String(b.profileId || ''),
  chatId: String(b.chatId || ''),
  botWaId, botTgId, channel, businessTypeId, businessName, ownerPrompt, catalog, businessKnowledge, now: serverNow, pickStats,
  steerTowardText, lastIncomingText, messages, queryText, trailingText, skipRag,
  knowledgeFlags: flags, contactsText, documentsCount, knowledgeVersion, tz, tzSource, exactTokens, requestKey, clientLang
} }];"""
# V9 (price reconciliation): + catalogItems
PREP_RETURN_NEW = """return [{ json: {
  v: 1, requestSeq, invalid, appUserId,
  profileId: String(b.profileId || ''),
  chatId: String(b.chatId || ''),
  botWaId, botTgId, channel, businessTypeId, businessName, ownerPrompt, catalog, businessKnowledge, now: serverNow, pickStats,
  steerTowardText, lastIncomingText, messages, queryText, trailingText, skipRag,
  knowledgeFlags: flags, contactsText, documentsCount, knowledgeVersion, tz, tzSource, exactTokens, requestKey, clientLang,
  catalogItems
} }];"""

# V1 (2026-09-28): an SQL comment never names a parameter as `$` + digit — pg-promise fills `$N` inside comments too
# (verify-sql-comments.py guards every Postgres node).
GATE_SQL = """-- «Вместе» subscription gate + daily anti-abuse cap (Task 17a; owner decision 2026-08-26,
-- spec §5.3). ONE statement does lookup, increment and verdict, so a hammering client
-- cannot slip between a read and a write. Parameters, in order: appUserId, requestKey ('' from a legacy
-- client).
--
-- Suggestions are FREE: this NEVER touches dialog_counts. Over-quota is deliberately
-- ALLOWED -- the panel IS the fallback the quota gate routes people into. What is refused:
-- an `expired` subscriber, an id with NO row at all, and anything past the daily cap.
--
-- S3 (stage 5): the SAME request key within 10 minutes (a retry after the server deadline, a
-- re-open with an unchanged tail) is served without a second increment — a timeout is never
-- counted twice. The counter row is only ever created for an entitled caller.
with me as (
  select status from subscribers where app_user_id = $1
), today as (
  select (now() at time zone 'Asia/Almaty')::date d   -- same day boundary as dialog_counts
), entitled as (
  select (select status from me) in ('active','trialing','grace') as ok
), key as (
  insert into suggestion_request_keys (app_user_id, request_key, counted_at)
  select $1, $2, now() where (select ok from entitled) and $2 <> ''
  on conflict (app_user_id, request_key) do update set counted_at = now()
    where suggestion_request_keys.counted_at < now() - interval '10 minutes'
  returning 1
), bump as (
  insert into suggestion_counts (app_user_id, d, n)
  select $1, t.d, 1 from today t
  where (select ok from entitled) and ($2 = '' or exists (select 1 from key))
  on conflict (app_user_id, d) do update set n = suggestion_counts.n + 1
  returning n
), cur as (
  select coalesce((select n from bump), (select n from suggestion_counts where app_user_id = $1 and d = (select d from today)), 0) as n
)
select
  exists(select 1 from me) as known,
  (select status from me) as status,
  (select n from cur) as n,
  -- 100/day/account. `n` is the count INCLUDING this request, so request 101 is the first
  -- refused one. NULL never leaks: cur coalesces to 0, entitled is a real boolean.
  ((select ok from entitled) and (select n from cur) <= 100) as allowed;"""
GATE_PARAMS = "={{ [ $json.appUserId, $json.requestKey || '' ] }}"

EXACT_SQL = """-- S4 (stage 5): an article / model code must match EXACTLY, not by embedding proximity — a
-- semantically «close» part is the wrong part. Runs once per request and only when the query
-- carries SKU-like tokens; the same per-bot metadata scope as the vector search.
-- R13: the tokens arrive NEWEST FIRST. A row ranks by the first token it matches and the rows are
-- dealt round-robin (every token's newest row, then every token's next one), so the newest article
-- leads and an older code or a price cannot take all three slots. A bare number matches that number
-- only, never inside a longer one (15000 is not 150000).
select content
from (
  select d.id, d.content, m.rank, row_number() over (partition by m.rank order by d.id desc) as turn
  from documents d
  cross join lateral (
    select min(u.ord) as rank
    from unnest($3::text[]) with ordinality as u(tok, ord)
    where u.tok <> ''
      and case when u.tok !~ '[^0-9]' then d.content ~ ('(?<![0-9])' || u.tok || '(?![0-9])')
               else d.content ilike '%' || u.tok || '%' end
  ) m
  where ((d.metadata->>'botWaId' = $1 and $1 not in ('-1', '')) or (d.metadata->>'botTgId' = $2 and $2 not in ('-1', '')))
    and m.rank is not null
) r
order by turn, rank, id desc
limit 3;"""
EXACT_PARAMS = "={{ (g => [ g.botWaId || '', g.botTgId || '', '{' + (g.exactTokens || []).map(x => '\"' + String(x).replace(/[\"\\\\{}]/g, '') + '\"').join(',') + '}' ])($('Gate Decision').first().json) }}"

ASSEMBLE_RAG_OLD = """let ragChunks = '';
if (!p.skipRag) {
  const parts = [];
  for (const it of $input.all()) {
    const c = it && it.json && it.json.document && it.json.document.pageContent;
    if (c && String(c).trim()) parts.push(String(c).trim());
  }
  ragChunks = parts.join('\\n---\\n').slice(0, 4000);
}"""
ASSEMBLE_BEGIN, ASSEMBLE_END = "// STAGE5-ASSEMBLE-BEGIN", "// STAGE5-ASSEMBLE-END"
ASSEMBLE_RAG_NEW = """// STAGE5-ASSEMBLE-BEGIN (generated by Tools/n8n/apply-suggest-v2.py — edit there)
""" + ps.RECONCILE_JS + """
// V9: the catalog the owner keeps in the app (a legacy client: its «• name — price» lines) checks every price-list row
const priceCatalog = Array.isArray(p.catalogItems) ? PriceReconcile.catalogFromItems(p.catalogItems) : PriceReconcile.catalogFromLegacy(p.catalog);
// V9-r2 (F5): the windows are cut at LINE boundaries on the price-list text itself, BEFORE the marks are added — a row the
// window held before V9 is never pushed out by a mark, a mark never stands without its row; the marks get their own
// bounded room, and when it overflows whole rows WITH their marks go from the end. The RAG chunks and the exact-match rows
// are ONE search result: a row with the article at the catalog price settles it in both blocks.
const RAG_CAP = 4000, EXACT_CAP = 2000, MARK_ROOM = 1500, SEP = '\\n---\\n';
const clipRows = (parts, cap) => {
  const kept = [];
  let used = 0;
  for (const part of parts) {
    const sep = kept.length ? SEP.length : 0;
    if (used + sep + part.length <= cap) { kept.push(part); used += sep + part.length; continue; }
    const room = cap - used - sep;
    const cut = room > 0 ? part.slice(0, room + 1).lastIndexOf('\\n') : -1;
    if (cut > 0) kept.push(part.slice(0, cut));
    else if (!kept.length) kept.push(part.slice(0, cap));            // one row longer than the whole window: cut, as before V9
    break;
  }
  return kept;
};
const fitMarked = (texts, budget) => {
  const units = [];
  for (const l of texts.join(SEP).split('\\n')) {
    if (l.startsWith(PriceReconcile.MARK_HEAD) && units.length) units[units.length - 1].push(l); else units.push([l]);
  }
  let size = units.reduce((a, u) => a + u.join('\\n').length + 1, 0) - 1;
  while (units.length && size > budget) size -= units.pop().join('\\n').length + 1;
  while (units.length && units[units.length - 1][0] === '---') units.pop();
  return units.map(u => u.join('\\n')).join('\\n');
};
let ragChunks = '', ragError = false, exactMatches = '';
const ragParts = [], exactParts = [];
if (!p.skipRag) {
  const pull = (name) => { try { return $(name).all(); } catch (e) { return []; } };
  for (const it of [...pull('Retrieve RAG'), ...pull('Retrieve RAG TG')]) {
    if (it && it.json && it.json.error) { ragError = true; continue; }   // the search failed — not «позиции нет»
    const c = it && it.json && it.json.document && it.json.document.pageContent;
    if (c && String(c).trim()) ragParts.push(String(c).trim());
  }
  for (const it of pull('Exact Match')) {
    if (it && it.json && it.json.error) { ragError = true; continue; }
    const c = it && it.json && it.json.content;
    if (c && String(c).trim()) exactParts.push(String(c).trim());
  }
  const ragKept = clipRows(ragParts, RAG_CAP), exactKept = clipRows(exactParts, EXACT_CAP);
  const rec = PriceReconcile.reconcileAll(ragKept.concat(exactKept), priceCatalog).texts;
  ragChunks = fitMarked(rec.slice(0, ragKept.length).filter(t => t.trim()), RAG_CAP + MARK_ROOM);
  exactMatches = fitMarked(rec.slice(ragKept.length).filter(t => t.trim()), EXACT_CAP + MARK_ROOM);
}
// V9-r2 (F4): the rules follow the marks that are IN the window — the hard rule only for a firm «Цены расходятся»
const priceMarks = PriceReconcile.marksIn(ragChunks + '\\n' + exactMatches);
const priceConflicts = priceMarks.filter(m => m.level === 'conflict').length, priceSoftMarks = priceMarks.length - priceConflicts;
const kflags = Array.isArray(p.knowledgeFlags) ? p.knowledgeFlags : [];
// STAGE5-ASSEMBLE-END"""

CATALOG_LINE_V1 = """if (kflags.includes('catalog_incomplete') || kflags.includes('descriptions_dropped')) { L.push('КАТАЛОГ СОКРАЩЁН: в ДАННЫЕ попала не вся информация о позициях (' + kflags.join(', ') + ') — если нужной детали нет, «Уточнить» или «Отложить», не додумывай.'); }"""
# R12: a shortened description (client clamp or server budget) is marked with «…» and flagged item_description_truncated
CATALOG_LINE_V2 = """if (kflags.includes('catalog_incomplete') || kflags.includes('descriptions_dropped') || kflags.includes('item_description_truncated')) { L.push('КАТАЛОГ СОКРАЩЁН: в ДАННЫЕ попала не вся информация о позициях (' + kflags.join(', ') + ') — если нужной детали нет, «Уточнить» или «Отложить», не додумывай. Строка каталога, оборванная на «…», неполна: условий из её скрытой части не утверждай и её цену не называй окончательной.'); }"""

ASSEMBLE_PROMPT_OLD = """L.push('НЕПОЛНОТА: каталог и прайс могут быть неполными. Отсутствие позиции в ДАННЫЕ не значит «нет в наличии» — не утверждай отсутствие, лучше уточни наличие.');"""
ASSEMBLE_PROMPT_NEW = """L.push('НЕПОЛНОТА: каталог и прайс могут быть неполными. Отсутствие позиции в ДАННЫЕ не значит «нет в наличии» — не утверждай отсутствие, лучше уточни наличие.');
""" + CATALOG_LINE_V2 + """
if (kflags.includes('description_truncated')) { L.push('ОПИСАНИЕ БИЗНЕСА СОКРАЩЕНО: условия, которых нет в показанной части, не утверждай.'); }
if (ragError) { L.push('ПОИСК ПО ДОКУМЕНТАМ НЕДОСТУПЕН: выдержки из прайса не получены из-за ошибки поиска — это НЕ значит, что позиции нет; про наличие и цену из файлов отвечай «Уточнить» или «Отложить».'); }
if (exactMatches) { L.push('ТОЧНЫЕ СОВПАДЕНИЯ: в блоке exactMatches — строки прайса, где артикул/модель из вопроса найдены дословно; цену и артикул бери оттуда, не из похожих строк.'); }
if (p.tzSource === 'default') { L.push('ЧАСОВОЙ ПОЯС БИЗНЕСА НЕ ЗАДАН: время в ДАННЫЕ приблизительное (по умолчанию Азия/Алматы) — «сейчас открыты/закрыты» утверждай осторожно, лучше «Уточнить».'); }"""

# V9 (price reconciliation): what a «[Сверка с каталогом: …]» line under a price-list row means (the panel's ДАННЫЕ carry it
# only when a row and the catalog disagree on an article's price, or the row's price could not be pinned)
PRICE_RULE_BEGIN, PRICE_RULE_END = "// STAGE5-PRICE-RULE-BEGIN", "// STAGE5-PRICE-RULE-END"
PRICE_RULE_ANCHOR = """if (exactMatches) { L.push('ТОЧНЫЕ СОВПАДЕНИЯ: в блоке exactMatches — строки прайса, где артикул/модель из вопроса найдены дословно; цену и артикул бери оттуда, не из похожих строк.'); }
"""
PRICE_RULE = """// STAGE5-PRICE-RULE-BEGIN (generated by Tools/n8n/apply-suggest-v2.py — edit there)
// V9-r2 (F4): the hard rule only for a firm mark; a soft one (another item may be meant, the row's price could not be
// pinned) never makes the model refuse the catalog price
if (priceConflicts) { L.push('РАСХОЖДЕНИЕ ЦЕН: под строкой прайса стоит пометка «[Сверка с каталогом: … Цены расходятся.]» — для этой позиции каталог и прайс называют разные цены. Ни одну из цен не называй окончательной и «свежую» сам не выбирай: карточка «Уточнить» или «Отложить» (цену подтвердишь), можно назвать обе цены, указав источник каждой.'); }
if (priceSoftMarks) { L.push('СВЕРКА ЦЕН: пометка «[Сверка с каталогом: …]» без вывода «Цены расходятся.» значит, что строка прайса может быть о другой позиции (аналог, аксессуар, комплект) или её цену не удалось однозначно сопоставить с каталогом (другая единица, фасовка, валюта, число без подписи). Цену из каталога называй как цену каталога; цену такой строки не выдавай за цену позиции из каталога — если нужна именно она, уточни, о какой позиции речь.'); }
// STAGE5-PRICE-RULE-END
"""

ASSEMBLE_TIME_OLD = """if (p.now) { L.push(''); L.push('ВРЕМЯ: сейчас ' + p.now + ' (локальное время владельца). Вопросы вида «вы сейчас работаете» сверяй с часами работы из ДАННЫЕ; если часов в ДАННЫЕ нет — «Уточнить» или «Отложить».'); }"""
ASSEMBLE_TIME_NEW = """if (p.now) { L.push(''); L.push('ВРЕМЯ: сейчас ' + p.now + ' (время бизнеса, серверные часы, зона ' + (p.tz || 'Asia/Almaty') + '). Вопросы вида «вы сейчас работаете» сверяй с часами работы из ДАННЫЕ; если часов в ДАННЫЕ нет — «Уточнить» или «Отложить».'); }"""

ASSEMBLE_FENCED_OLD = """  lastClientMessage: p.queryText || '',
  steerTowardText: p.steerTowardText || null,
  now: p.now || ''
};"""
ASSEMBLE_FENCED_NEW = """  exactMatches: exactMatches,
  lastClientMessage: p.trailingText || p.queryText || '',
  steerTowardText: p.steerTowardText || null,
  now: p.now || '',
  documents: p.documentsCount || 0
};"""

# V9 (iteration 2026-09-28, panel part): ТРИВИАЛЬНЫЕ ended with «воздержание только для сообщений не по адресу бизнеса»,
# which contradicted ВОЗДЕРЖАНИЕ's own example (the owner's N2_banter_abstain gate, 68e1f3eb) — gpt-4o-mini followed the
# LAST rule and answered «ахаха ну ты даёшь, красавчик 😂» like a thank-you. Narrowed to agree; the policy is unchanged.
TRIVIAL_TAIL_OLD = "Это ответ клиенту бизнеса, НЕ повод для пустого массива — воздержание только для сообщений не по адресу бизнеса.');"
TRIVIAL_TAIL_NEW = ("Благодарность, «ок» и подтверждение — ответ клиенту бизнеса, НЕ повод для пустого массива. Но шутка, комплимент или "
                    "эмоциональная реакция без вопроса, просьбы и благодарности («ахаха ну ты даёшь, красавчик 😂») — не благодарность: "
                    "для неё действует ВОЗДЕРЖАНИЕ, карточек нет.');")
# V9: the СТИЛЬ rule (the client's language) sat mid-prompt, before the niche block's Russian quotes and the Russian ДАННЫЕ,
# and nothing enforced it — a Kazakh question got four Russian cards. For a Kazakh client: a rule AFTER the niche block…
NICHE_PUSH_END = "// PANEL-NICHE-PUSH-END\n"
ASSEMBLE_LANG_BEGIN, ASSEMBLE_LANG_END = "// STAGE5-LANG-BEGIN", "// STAGE5-LANG-END"
ASSEMBLE_LANG = """// STAGE5-LANG-BEGIN (generated by Tools/n8n/apply-suggest-v2.py — edit there)
if (p.clientLang === 'kz') { L.push(''); L.push('ЯЗЫК КЛИЕНТА — КАЗАХСКИЙ: text КАЖДОЙ карточки целиком по-казахски (цифры, артикулы и названия позиций из ДАННЫЕ — как есть; русские формулировки из правил выше и из НИШИ передай по смыслу по-казахски); label — по-русски, это заголовок для владельца. Карточка с русским text будет отклонена.'); }
// STAGE5-LANG-END
"""
# …and the validators: STRICT before the one retry (a Russian card fails the set, the reason reaches LLM Retry), LENIENT
# after it (Russian cards are dropped when a Kazakh one remains, otherwise all are kept — never an error): the same
# asymmetry as the titles. A card is «Russian» when it has no Kazakh-specific letter in three or more Cyrillic words, so a
# short «Бар, 35000 тг» does not fail a set.
VALIDATE_LANG_BEGIN, VALIDATE_LANG_END = "// STAGE5-LANG-VALIDATE-BEGIN", "// STAGE5-LANG-VALIDATE-END"
LANG_HELPERS = """// STAGE5-LANG-VALIDATE-BEGIN (generated by Tools/n8n/apply-suggest-v2.py — edit there)
// V9: a Kazakh client (Prep.clientLang) gets Kazakh card texts; label stays Russian (owner-facing)
let clientLang = 'ru';
try { clientLang = String($('Prep').first().json.clientLang || 'ru'); } catch (e) { clientLang = 'ru'; }
const notKazakh = (s) => !/[әғқңөұүһі]/i.test(String(s)) && (String(s).match(/[а-яё]+/gi) || []).length >= 3;
"""
VALIDATE_OLD = """const ok = abstain || (parsedOk && items.length >= 1 && items.length <= 4 && allValid && distinct);
let violation = '';
if (!ok) {
  if (!parsedOk) violation = 'ответ не распознан';
  else if (items.length > 4) violation = 'больше 4 вариантов, получено ' + items.length;
  else if (!allValid) violation = 'ход вне списка, пустой текст или пустой заголовок';
  else violation = 'заголовки повторяются — все label должны быть разными';
}"""
VALIDATE_NEW = LANG_HELPERS + """const langOk = clientLang !== 'kz' || !items.some(i => notKazakh(i.text));
const ok = abstain || (parsedOk && items.length >= 1 && items.length <= 4 && allValid && distinct && langOk);
let violation = '';
if (!ok) {
  if (!parsedOk) violation = 'ответ не распознан';
  else if (items.length > 4) violation = 'больше 4 вариантов, получено ' + items.length;
  else if (!allValid) violation = 'ход вне списка, пустой текст или пустой заголовок';
  else if (!distinct) violation = 'заголовки повторяются — все label должны быть разными';
  else violation = 'карточки не на языке клиента: клиент пишет по-казахски — text КАЖДОЙ карточки целиком по-казахски (label по-русски)';
}
// STAGE5-LANG-VALIDATE-END"""
VALIDATE2_OLD = "items = kept.slice(0, 4);\n"
VALIDATE2_NEW = LANG_HELPERS + """if (clientLang === 'kz') { const kz = kept.filter(i => !notKazakh(i.text)); if (kz.length) kept.splice(0, kept.length, ...kz); }
items = kept.slice(0, 4);
// STAGE5-LANG-VALIDATE-END
"""

BUILD_OLD = """if (j.invalid || !j.ok) {
  // Task 17a: a subscription/cap refusal reuses the EXISTING error envelope the client
  // already renders, and NOTHING MORE. The gate's `gateReason` deliberately does NOT ride
  // the wire (review N-4): /webhook/SuggestReplies is unauthenticated, so telling a caller
  // apart `unknown_account` / `subscription_expired` / `daily_cap` would turn a guessed
  // app_user_id into an oracle for whether that account exists and what state it is in.
  // The reason stays on Gate Decision's output, where the execution log keeps it for
  // debugging; the client never read it (SuggestRepliesResponse has no such field).
  return [{ json: { v: 1, requestSeq, suggestions: [], error: 'generation_failed' } }];
}"""
BUILD_NEW = """if (j.invalid || !j.ok) {
  // S8 (stage 5): the refusal KIND rides the wire for an AUTHENTICATED caller (A1 token — the
  // account is proven, so subscription / daily_cap / unknown_account are no oracle any more).
  // A legacy token-less caller keeps the opaque envelope (review N-4 reasoning still applies to it).
  let authMode = 'legacy';
  try { authMode = String($('Auth Gate').first().json.crAuth.mode || 'legacy'); } catch (e) { authMode = 'legacy'; }
  let error = 'generation_failed';
  if (authMode === 'session') {
    if (j.invalid) error = 'bad_request';
    else if (j.gateReason) {
      const r = String(j.gateReason);
      error = r === 'daily_cap' ? 'daily_cap' : r === 'unknown_account' ? 'unknown_account'
            : r.startsWith('subscription_') ? 'subscription' : r === 'missing_app_user_id' ? 'bad_request' : 'generation_failed';
    }
  }
  return [{ json: { v: 1, requestSeq, suggestions: [], error } }];
}"""


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "suggest-v2:" + ":".join(parts)))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def splice(js, begin, end, block):
    """Replace the marker-wrapped region [begin … end] of `js` with `block` (which carries both markers)."""
    a, b_ = js.index(begin), js.index(end) + len(end)
    body = block[:block.index(end) + len(end)]
    return js[:a] + body + js[b_:]


def set_edges(wf, src, outputs):
    want = {"main": [[{"node": t, "type": "main", "index": 0} for t in outs] for outs in outputs]}
    if wf["connections"].get(src) != want:
        wf["connections"][src] = want
        return True
    return False


def patch():
    path = os.path.join(WF, FNAME)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    # Prep
    prep = node(wf, "Prep")
    js = prep["parameters"]["jsCode"]
    if PREP_BEGIN in js:
        a, b_ = js.index(PREP_BEGIN), js.index(PREP_END) + len(PREP_END)
        new = js[:a] + PREP_BLOCK + js[b_:]
    else:
        anchor = "const requestSeq = (b.requestSeq === undefined || b.requestSeq === null) ? 0 : b.requestSeq;"
        assert anchor in js, "Prep: requestSeq anchor missing"
        # the legacy `const catalog/businessKnowledge` declarations become `let` (the block re-assigns them)
        new = js.replace("const catalog = String(b.catalog || '').slice(0, 2500);", "// (stage 5: catalog / businessKnowledge are composed below under a budget)") \
                .replace("const appUserId = String(b.appUserId || '').slice(0, 128);",
                         "// A1/stage 5: the token's account is the identity; a body-less legacy call may still name it; NONE → invalid before any DB round-trip\n"
                         "const appUserId = String(b.appUserId || (($json.crAuth && $json.crAuth.appUserId) || '')).slice(0, 128);\n"
                         "if (!appUserId) invalid = true;") \
                .replace("const businessKnowledge = String(b.businessKnowledge || '').slice(0, 1200);", "") \
                .replace("let queryText = runParts.join('\\n').slice(0, 500);", "let queryText = runParts.join('\\n').slice(0, 500);   // the trailing client run (kept as trailingText below)")
        new = new.replace(anchor, PREP_BLOCK + "\n" + anchor)
    if PREP_RETURN_OLD in new:
        new = new.replace(PREP_RETURN_OLD, PREP_RETURN_NEW)
    if PREP_RETURN_V1 in new:                       # V9: an already-migrated Prep gains clientLang
        new = new.replace(PREP_RETURN_V1, PREP_RETURN_NEW)
    if PREP_RETURN_V2 in new:                       # V9 (price reconciliation): … and catalogItems
        new = new.replace(PREP_RETURN_V2, PREP_RETURN_NEW)
    # V8: the history × burst merge (owned between markers; the legacy hand-written text is replaced exactly once)
    if MERGE_BEGIN in new:
        a, b_ = new.index(MERGE_BEGIN), new.index(MERGE_END) + len(MERGE_END)
        new = new[:a] + MERGE_BLOCK + new[b_:]
    else:
        assert new.count(MERGE_LEGACY) == 1, "Prep: the legacy burst-merge section is not where V8 expects it"
        new = new.replace(MERGE_LEGACY, MERGE_BLOCK)
    assert new.index(MERGE_END) < new.index("let queryText") < new.index(PREP_BEGIN), "Prep: merge → queryText → stage-5 block order"
    assert PREP_RETURN_NEW in new and "let queryText" in new, "Prep: return shape"
    if new != js:
        prep["parameters"]["jsCode"] = new
        ch = True
    # Gate
    gate = node(wf, "Suggestion Gate")
    if gate["parameters"]["query"] != GATE_SQL or gate["parameters"]["options"].get("queryReplacement") != GATE_PARAMS:
        gate["parameters"]["query"] = GATE_SQL
        gate["parameters"]["options"]["queryReplacement"] = GATE_PARAMS
        ch = True
    # RAG nodes fail open
    for name in ("Retrieve RAG", "Retrieve RAG TG"):
        n = node(wf, name)
        for k, v in (("onError", "continueRegularOutput"), ("alwaysOutputData", True)):
            if n.get(k) != v:
                n[k] = v
                ch = True
    # Exact match branch
    if node(wf, "Has Exact Tokens?") is None:
        wf["nodes"].append({"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                                          "conditions": [{"id": nid("has-exact", "cond"), "leftValue": "={{ ($('Gate Decision').first().json.exactTokens || []).length > 0 }}", "rightValue": "",
                                                                          "operator": {"type": "boolean", "operation": "true", "singleValue": True}}], "combinator": "and"}, "options": {}},
                            "id": nid("has-exact"), "name": "Has Exact Tokens?", "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": [1040, 120], "executeOnce": True})
        ch = True
    if node(wf, "Exact Match") is None:
        wf["nodes"].append({"parameters": {"operation": "executeQuery", "query": EXACT_SQL, "options": {"queryReplacement": EXACT_PARAMS}},
                            "id": nid("exact-match"), "name": "Exact Match", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6, "position": [1220, 260],
                            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput", "executeOnce": True})
        ch = True
    em = node(wf, "Exact Match")
    if em["parameters"]["query"] != EXACT_SQL or em["parameters"]["options"].get("queryReplacement") != EXACT_PARAMS:
        em["parameters"]["query"] = EXACT_SQL
        em["parameters"]["options"]["queryReplacement"] = EXACT_PARAMS
        ch = True
    ch |= set_edges(wf, "Retrieve RAG", [["Has Exact Tokens?"]])
    ch |= set_edges(wf, "Retrieve RAG TG", [["Has Exact Tokens?"]])
    ch |= set_edges(wf, "Has Exact Tokens?", [["Exact Match"], ["Assemble"]])
    ch |= set_edges(wf, "Exact Match", [["Assemble"]])
    # Assemble
    asm = node(wf, "Assemble")
    js = asm["parameters"]["jsCode"]
    new = js
    if ASSEMBLE_RAG_OLD in new:
        new = new.replace(ASSEMBLE_RAG_OLD, ASSEMBLE_RAG_NEW)
    elif ASSEMBLE_BEGIN in new:                     # V9 (price reconciliation): the owned block is re-spliced
        new = splice(new, ASSEMBLE_BEGIN, ASSEMBLE_END, ASSEMBLE_RAG_NEW)
    if ASSEMBLE_PROMPT_OLD in new and "КАТАЛОГ СОКРАЩЁН" not in new:
        new = new.replace(ASSEMBLE_PROMPT_OLD, ASSEMBLE_PROMPT_NEW)
    if CATALOG_LINE_V1 in new:                      # R12: an already-migrated Assemble carries the V1 rule
        new = new.replace(CATALOG_LINE_V1, CATALOG_LINE_V2)
    if PRICE_RULE_BEGIN in new:
        new = splice(new, PRICE_RULE_BEGIN, PRICE_RULE_END, PRICE_RULE)
    else:
        assert new.count(PRICE_RULE_ANCHOR) == 1, "Assemble: the ТОЧНЫЕ СОВПАДЕНИЯ rule (the price rule's anchor) is missing"
        new = new.replace(PRICE_RULE_ANCHOR, PRICE_RULE_ANCHOR + PRICE_RULE)
    if ASSEMBLE_TIME_OLD in new:
        new = new.replace(ASSEMBLE_TIME_OLD, ASSEMBLE_TIME_NEW)
    if ASSEMBLE_FENCED_OLD in new:
        new = new.replace(ASSEMBLE_FENCED_OLD, ASSEMBLE_FENCED_NEW)
    # V9 (panel): ТРИВИАЛЬНЫЕ agrees with ВОЗДЕРЖАНИЕ; the Kazakh rule right after the niche block
    if TRIVIAL_TAIL_OLD in new:
        new = new.replace(TRIVIAL_TAIL_OLD, TRIVIAL_TAIL_NEW)
    if ASSEMBLE_LANG_BEGIN in new:
        new = splice(new, ASSEMBLE_LANG_BEGIN, ASSEMBLE_LANG_END, ASSEMBLE_LANG)
    else:
        assert new.count(NICHE_PUSH_END) == 1, "Assemble: the PANEL-NICHE-PUSH-END marker (inject-panel-prompts.py) is missing"
        new = new.replace(NICHE_PUSH_END, NICHE_PUSH_END + ASSEMBLE_LANG)
    for frag in ("STAGE5-ASSEMBLE-BEGIN", CATALOG_LINE_V2, "серверные часы", "exactMatches: exactMatches", TRIVIAL_TAIL_NEW, ASSEMBLE_LANG,
                 ASSEMBLE_RAG_NEW, PRICE_RULE_ANCHOR + PRICE_RULE):
        assert frag in new, f"Assemble: {frag} missing"
    assert TRIVIAL_TAIL_OLD not in new and new.index(NICHE_PUSH_END) < new.index(ASSEMBLE_LANG_BEGIN), "Assemble: V9 shape"
    if new != js:
        asm["parameters"]["jsCode"] = new
        ch = True
    # V9 (panel): the language is enforced — strict before the retry, lenient after it
    for name, old_, block in (("Validate", VALIDATE_OLD, VALIDATE_NEW), ("Validate 2", VALIDATE2_OLD, VALIDATE2_NEW)):
        vn = node(wf, name)
        js = vn["parameters"]["jsCode"]
        if VALIDATE_LANG_BEGIN in js:
            new = splice(js, VALIDATE_LANG_BEGIN, VALIDATE_LANG_END, block)
        else:
            assert js.count(old_) == 1, f"{name}: the V9 anchor is not where it is expected"
            new = js.replace(old_, block)
        assert block in new, f"{name}: V9 block missing"
        if new != js:
            vn["parameters"]["jsCode"] = new
            ch = True
    # Build Response
    br = node(wf, "Build Response")
    js = br["parameters"]["jsCode"]
    if BUILD_OLD in js:
        br["parameters"]["jsCode"] = js.replace(BUILD_OLD, BUILD_NEW)
        ch = True
    assert "S8 (stage 5)" in br["parameters"]["jsCode"], "Build Response shape"
    # settings
    if wf.get("settings", {}).get("executionTimeout") != EXECUTION_TIMEOUT_SEC:
        wf.setdefault("settings", {})["executionTimeout"] = EXECUTION_TIMEOUT_SEC
        ch = True
    if ch:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return ch


def check():
    wf = json.load(open(os.path.join(WF, FNAME), encoding="utf-8"))
    ok = PREP_BLOCK in node(wf, "Prep")["parameters"]["jsCode"] and PREP_RETURN_NEW in node(wf, "Prep")["parameters"]["jsCode"] \
        and MERGE_BLOCK in node(wf, "Prep")["parameters"]["jsCode"] and MERGE_LEGACY not in node(wf, "Prep")["parameters"]["jsCode"] \
        and node(wf, "Suggestion Gate")["parameters"]["query"] == GATE_SQL and node(wf, "Exact Match") is not None \
        and node(wf, "Exact Match")["parameters"]["query"] == EXACT_SQL and "STAGE5-ASSEMBLE-BEGIN" in node(wf, "Assemble")["parameters"]["jsCode"] and CATALOG_LINE_V2 in node(wf, "Assemble")["parameters"]["jsCode"] \
        and "S8 (stage 5)" in node(wf, "Build Response")["parameters"]["jsCode"] and wf.get("settings", {}).get("executionTimeout") == EXECUTION_TIMEOUT_SEC \
        and TRIVIAL_TAIL_NEW in node(wf, "Assemble")["parameters"]["jsCode"] and ASSEMBLE_LANG in node(wf, "Assemble")["parameters"]["jsCode"] \
        and ASSEMBLE_RAG_NEW in node(wf, "Assemble")["parameters"]["jsCode"] and PRICE_RULE_ANCHOR + PRICE_RULE in node(wf, "Assemble")["parameters"]["jsCode"] \
        and VALIDATE_NEW in node(wf, "Validate")["parameters"]["jsCode"] and VALIDATE2_NEW in node(wf, "Validate 2")["parameters"]["jsCode"] \
        and all(node(wf, n).get("onError") == "continueRegularOutput" for n in ("Retrieve RAG", "Retrieve RAG TG"))
    return ok


def main():
    if "--check" in sys.argv:
        ok = check()
        print("suggest v2:", "OK" if ok else "MISSING/STALE")
        sys.exit(0 if ok else 2)
    print(f"{'patched' if patch() else 'up to date'}  {FNAME}")


if __name__ == "__main__":
    main()
