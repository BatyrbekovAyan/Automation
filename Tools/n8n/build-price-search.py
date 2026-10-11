#!/usr/bin/env python3
"""V9 (iteration 2026-09-28) — price reconciliation AT SEARCH TIME: the manual catalog and the price lists are equal
sources (_core.md), but a model that has just called the price-list search answers from the tool result (the R14
conflict scenario: 0/8). Owner decision (iteration README): reconcile where the search happens, take the catalog from
the clone's PUBLISHED prompt, keep no catalog table and never rewrite `documents`.

Generated / patched here (edit here, re-run; ids fixed):
  crPriceSearch001-Price_List_Search.json   the sub-workflow every price-list search goes through: Guard (ids,
                                            filter key, query) → Search Price Lists (the same Supabase vector
                                            search the tool did: `documents`, topK 10, filtered by the bot's own
                                            workflow id under botWaId / botTgId; fail-open) → Reconcile Prices
                                            (RECONCILE_JS over every hit against the catalog parsed from the
                                            caller's prompt) → {documents: [...]} to the model.
  4wYitz5ek30SVNlT-WhatsApp_Bot.json        «Supabase Vector Store» (retrieve-as-tool, no post-processing possible)
  4VN3gsFaC2HUYmcc-Telegram_Bot.json        becomes a toolWorkflow of the SAME name (the model's tool name and the
                                            prompts' «инструмент Supabase Vector Store» stay), same id/position/
                                            index (Create/Edit patch nodes[0]/nodes[5] by index), same description;
                                            it passes the query ($fromAI 'input' — the old tool's argument), the
                                            clone's own id + filter key, and `$('AI Agent').params…systemMessage` —
                                            the prompt the running (published) clone answers with. Its embedding
                                            sub-node moves into the sub-workflow.
  0DP79STI3pafXO9B-Set_Workflow_State.json  crPriceSearch001 joins the protected infra ids.
Shared (imported by build-test-dialog.py for the Test Dialog's tool, and by apply-suggest-v2.py for the panel's
Assemble — ONE function text everywhere): RECONCILE_JS, tool_node().
V9-r2 (2026-10-08, adversarial review — docs/audits/2026-09-28-iteration/evidence/V9-adversarial/): the firm «Цены
расходятся» only for a POSITIVELY identified item whose row does not carry the catalog price; everything else (analogues,
cross-references, accessories, kits, packs, other currencies, bare numbers) is at most the soft mark; all hits of ONE
search are reconciled together (`reconcileAll` — a row at the catalog price settles the article); a mark-like text inside
a document line is cut off. Tests: tests/test_price_search.py, tests/test_suggest_price.py.
V9-r3 (2026-10-11, independent review of 2026-10-09 — docs/audits/2026-10-09-independent-review-evidence/suggestions-review.md
S1/S2): a pack is comparable only when the row and the catalog item name the SAME packs with the SAME quantities (S1 —
«упаковка 2 шт» 6400 against «упаковка 10 шт» 32000 was a firm conflict); a row settles its article for the other rows
only when it is the item itself (the firm mark's identity rules) with the catalog number as its PRICE, and a stock /
quantity number is never one of the row's amounts (S2 — a filter wrench at 3200, or «Остаток: 3200», erased a genuine
conflict). Evidence: docs/audits/2026-10-09-followups/evidence/V9r3-*.txt.

    python3 Tools/n8n/build-price-search.py            # write / patch (re-runnable; a second run changes nothing)
    python3 Tools/n8n/build-price-search.py --check    # exit 2 when anything is stale
"""
import copy
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
SEARCH_ID, SEARCH_NAME = "crPriceSearch001", "Price List Search"
SEARCH_FILE = f"{SEARCH_ID}-Price_List_Search.json"
TEMPLATES = {"4wYitz5ek30SVNlT-WhatsApp_Bot.json": "botWaId", "4VN3gsFaC2HUYmcc-Telegram_Bot.json": "botTgId"}
SET_STATE = "0DP79STI3pafXO9B-Set_Workflow_State.json"
TOOL_NAME = "Supabase Vector Store"          # = the model's tool name «Supabase_Vector_Store»; the prompts name it so
OLD_EMBEDDING = "OpenAI Embedding"           # the retired retrieve-as-tool's embedding sub-node (templates)
OPENAI_CRED = {"id": "XVjhR1xlWrIgJjKz", "name": "OpenAi account"}   # the bot templates' credentials
SUPABASE_CRED = {"id": "ifCYb9DhUxoCq3pu", "name": "Supabase"}
TOP_K = 10
# the retrieve-as-tool's description and argument, verbatim — the model sees the same tool as before
TOOL_DESCRIPTION = ("Retrieve product details, prices, services, and any uploaded catalog or document content for this business. "
                    "Use whenever the customer asks about products, pricing, availability, or specifics that may be in uploaded documents.")
QUERY_FROM_AI = "={{ $fromAI('input', 'Query to search for. Required', 'string') }}"
# the clone's own prompt as the running (published) version holds it — `.params` reads the node's parameters, no run data
PROMPT_OF_AGENT = "={{ $('AI Agent').params.options.systemMessage }}"
INPUTS = ("query", "botId", "filterKey", "prompt")


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "price-search:" + ":".join(parts)))


RECONCILE_BEGIN, RECONCILE_END = "// PRICE-RECONCILE-BEGIN", "// PRICE-RECONCILE-END"
RECONCILE_JS = r"""// PRICE-RECONCILE-BEGIN (generated by Tools/n8n/build-price-search.py — edit there)
// V9 (iteration 2026-09-28): the manual catalog and the price lists are EQUAL sources (_core.md), but a model that has
// just called the price-list search answers from the tool result — a catalog/price-list conflict was quoted as the
// price-list price 0/8 times. So the search result is reconciled with the catalog BEFORE the model sees it: a price-list
// line naming a catalog item's ARTICLE at a different price gets a line «[Сверка с каталогом: …]» right under it (the
// model follows such a line 6/6, R14 diagnostic). Nothing is stored: the same function runs on every search (the bot's
// tool, the Test Dialog's tool, the panel's Assemble) over the text as read, so a corrected catalog clears the mark.
// Matching is deliberately narrow — a wrong «Цены расходятся» overrides a right answer, and the V9-r2 adversarial review
// (2026-10-08) found it on price lists that AGREE with the catalog. The firm «Цены расходятся» needs ALL of:
//   * the article — an item takes part only when its NAME carries an article-like token (≥5 chars with a digit, and a
//     Latin letter or a -/ joined code of ≥6 digits; never a grade/size/spec — 5W-30, 205/55R16, 128GB, 500ml,
//     16GB/512GB —, a year range or a phone) and its price is ONE number (optional currency and «/unit»; never «от», a
//     range or «договорная»); the token must be a WHOLE token of the line (a longer code is another article);
//   * a priced line — the converter's «Цена…: N;» columns (ALL of them — опт/розн), else a labelled price after the
//     token, else the currency amount after it (up to the next article), else — on a one-article line — the one amount
//     before it; «от/до», a range, two amounts after the article or a price both before and after it is AMBIGUOUS; a
//     bare number at the END of the line (PDF/DOCX/RTF text, no currency, no label) is at most the SOFT mark — it is as
//     often a stock / qty / spec column; a bare year («Camry 2019») is no price at all;
//   * a price that differs — the catalog number among the AMOUNTS of the article's part of the line (codes and specs
//     blanked: «по акции 3200», «РРЦ: 3200», «Цена: 3200 тг без НДС», a PDF «3200 150») = no conflict, no mark on THAT
//     row; a stock / quantity number («Остаток: 3200», «в наличии 3200 шт», a Кол-во / Остаток column) is never one of
//     the row's amounts (V9-r3 S2). A row ANYWHERE in the same search result settles the article — the other rows naming
//     it (analogues, accessories) are not marked — only when it is the item ITSELF (the identity rules below, as for the
//     firm mark) with the catalog number as its PRICE (a price column, a labelled price, the currency amount — never a
//     bare number, another pack / unit / currency, «от»): an accessory or a cross-reference at the catalog price keeps
//     only its own row unmarked and never erases another row's conflict (V9-r3 S2);
//   * a comparable price — another unit, a pack the catalog item does not name with the SAME unit and quantity
//     («упаковка 2 шт» ≠ «упаковка 10 шт» ≠ «коробка» ≠ no pack; a pack word on both sides proves nothing — V9-r3 S1;
//     шт = no unit), another currency (руб, ₽, $, USD, EUR, €, у.е.), «тыс», НДС, or the catalog number glued into
//     «5 500» = ambiguous (the soft mark);
//   * the SAME item, positively — the article leads the line (converter: no article column names another code), no
//     cross-reference sign (the article inside an open bracket; «для/под/подходит/применим…/OE/OEM/кросс/номер/
//     заменяет/аналог…» up to three words before it; «аналог/копия/неориг…» on the item part; an analogue / OE /
//     применимость / примечание column = no mark at all), the line's first word is the item's head word (converter:
//     the name column's), every Russian word of the item part is a word of the catalog name, every Latin word of 3+
//     letters too (Knecht, MANN, Bosch → another item), no «комплект/набор/+/б/у» the name does not have.
//   Otherwise the mark is soft — «может быть о другой позиции» / «число … может быть не ценой» / «однозначно
//   сопоставить не удалось» — and never says the prices disagree.
// The marks are the reconciler's only: a «[Сверка с каталогом:» text ANYWHERE in a document line (any case, after a
// bullet, mid-line) is cut off with the rest of that line (V9-r2 F6). Idempotent: earlier mark lines are dropped first;
// a text with nothing to mark or cut comes back byte for byte.
const PriceReconcile = (() => {
  const MARK_HEAD = '[Сверка с каталогом: ';
  const FORGED = /\[\s*сверка\s+с\s+каталогом\s*:/iu;
  const NUM = '(?:\\d{1,3}(?:[ \\u00a0\\u202f\\u2009.,]\\d{3})+|\\d+)(?:[.,]\\d{1,2})?(?!\\d)';
  const CUR = '(?:тг|тенге|теңге|₸|kzt|tg)(?![\\p{L}\\p{N}])\\.?';
  const UNIT = '(?:\\s*\\/\\s*(\\p{L}+)\\.?)?';
  const LABEL = '(?:цена|цены|цену|стоимость|стоимости|price|бағасы|баға|прайс|розница|розничная|розн|опт|оптовая|оптом)';
  const PRICE_COL = /цен|price|стоимост|розн|опт|cost|бағас|прайс/iu;
  const ANALOG_COL = /аналог|замен|кросс|cross|oem|(?:^|[^\p{L}])oe(?![\p{L}])|примен|примечан|совмест|подход/iu;
  const ARTICLE_COL = /артикул|^арт|код|sku|^art|каталожн|номер|part/iu;
  const NAME_COL = /наименован|назван|номенклатур|товар|продукц|модел|^name|^model|^item|^product/iu;
  const BRAND_COL = /бренд|марка|производител|brand|manufact|vendor/iu;
  const ANALOG_BEFORE = /(?:^|[^\p{L}])(?:аналог\p{L}*|замен\p{L}*|взамен|вместо|кросс|cross|oem|совместим\p{L}*|подходит|подойд\p{L}*)[\s:.,—–\-()«»"]*$/iu;
  // an analogue named on the item's own part of the line («Аналог фильтра Toyota 90915-…», «JS (неоригинал) …»)
  const ANALOG_WORD = /(?:^|[^\p{L}])(?:аналог\p{L}*|копи[яи]|неориг\p{L}*|заменител\p{L}*|реплик\p{L}*)(?![\p{L}])/iu;
  // V9-r2: a cross-reference word up to three words before the article — the row names the catalog's number for another item
  const CROSS_KW = /^(?:для|под|подходит|подойд\p{L}*|применим\p{L}*|применяем\p{L}*|oe|oem|кросс\p{L}*|cross|номер\p{L}*|заменяет|замен\p{L}*|взамен|вместо|совместим\p{L}*|аналог\p{L}*)$/iu;
  const KIT = /(?:^|[^\p{L}])(?:комплект\p{L}*|набор\p{L}*|kit|set|б\/у|б\.у\.|бу|уценк\p{L}*|восстановлен\p{L}*|used|refurb\p{L}*)(?![\p{L}])|\+/iu;
  // V9-r3 (S1): a pack is a unit AND a quantity — «упаковка 2 шт», «коробка (4 шт)», «10 шт в упаковке», «4 шт/уп», «за 2 шт»
  const PACK_WORD = /(?:^|[^\p{L}])(упак\p{L}*|уп|компл\p{L}*|коробк\p{L}*|короб|пачк\p{L}*|пач|блок\p{L}*|ящик\p{L}*|рулон\p{L}*|мешок|мешк\p{L}*|бухт\p{L}*|pack|box)(?![\p{L}])/iu;
  const PER_N = /(?:^|[^\p{L}])за\s*([2-9]|\d{2,})\s*(?:шт|штук\p{L}*|ед)(?![\p{L}])/iu;
  const COUNT = '(\\d+(?:[.,]\\d+)?)\\s*(шт|штук\\p{L}*|ед|pcs|pc|кг|гр|г|мл|л|м)\\.?(?![\\p{L}])';
  const COUNT_AFTER = new RegExp('^[\\s:=(—–\\-,]*(?:по\\s*)?' + COUNT, 'iu');
  const COUNT_BEFORE = new RegExp(COUNT + '\\s*(?:в|на|\\/)?\\s*$', 'iu');
  const PACK_KIND = [[/^(?:упак|уп$|pack)/, 'упак'], [/^компл/, 'компл'], [/^(?:короб|box)/, 'короб'], [/^пач/, 'пачка'], [/^блок/, 'блок'],
    [/^ящик/, 'ящик'], [/^рулон/, 'рулон'], [/^меш/, 'мешок'], [/^бухт/, 'бухта']];
  // V9-r3 (S2): a stock / quantity number is never a price — «Остаток: 3200», «в наличии 3200 шт», «Кол-во: 2»
  const QTY_WORD = '(?:остат\\p{L}*|кол-?во|кол\\.|количеств\\p{L}*|в\\s+наличии|наличи\\p{L}*|на\\s+склад\\p{L}*|склад\\p{L}*|stock|qty|quantity)';
  const QTY_COL = /остат|кол-?во|количеств|налич|склад|stock|qty|quantit|в\s*упаков|в\s*коробк|кратност/iu;
  const FOREIGN = /(?:^|[^\p{L}])(?:руб\p{L}*|rub|usd|eur|долл\p{L}*|евро|у\.\s?е)(?![\p{L}])|[₽$€]/iu;
  const SCALE = /(?:^|[^\p{L}])(?:тыс\p{L}*|млн|мың)(?![\p{L}])/iu;
  const VAT = /(?:^|[^\p{L}])(?:ндс|vat|ққс)(?![\p{L}])/iu;
  const PIECE = new Set(['', 'шт', 'штука', 'штук', 'ед', 'единица', 'pcs', 'pc']);
  const LABEL_WORD = /^(?:цен\p{L}*|стоимост\p{L}*|опт\p{L}*|розн\p{L}*|рознич\p{L}*|дилер\p{L}*|ррц|мрц|закуп\p{L}*|акци\p{L}*|price|retail|wholesale|dealer|баға\p{L}*)$/iu;
  const LINE_PREFIX = /^\s*(?:row|строка)\s+\d+:/iu;   // a DOCX table row «Row 2: …»
  const BOUND_BEFORE = /(?:^|[^\p{L}])(?:от|до|from|to|бастап|дейін|свыше|более|менее)\s*$/iu;
  const RANGE_BEFORE = /\d\s?[-–]\s?$/u;
  const RANGE_AFTER = /^\s?[-–]\s?\d/u;
  const TOKEN = /[A-Za-z0-9](?:[A-Za-z0-9]|[-\/](?=[A-Za-z0-9]))*/g;
  const SPEC = [/^\d+W-?\d+$/i, /^\d{3}\/\d{2}Z?R\d{2}C?$/i, /^\d{2,3}\/\d{2}-\d{2}$/, /^R\d{2}C?$/i,
    /^\d+(?:[.,]\d+)?X\d+(?:[.,]\d+)?$/i, /^(?:19|20)\d{2}[-\/](?:19|20)?\d{2}$/,
    /^\d+(?:[.,]\d+)?(?:ML|L|G|KG|MG|MM|CM|M|GB|TB|MB|W|KW|V|AH|MAH|HZ|IN|INCH|PCS|PC|MP)$/i];
  // a -/ joined spec («16GB/512GB», «12V-7AH»): every part a number or a number with a unit, at least one with a unit
  const SPEC_PART = /^\d+(?:[.,]\d+)?(ML|L|G|KG|MG|MM|CM|M|GB|TB|MB|W|KW|V|AH|MAH|HZ|IN|INCH|PCS|PC|MP|K|X)?$/i;
  const STOP = new Set(['для', 'или', 'при', 'без', 'как', 'это', 'все', 'тг', 'тенге', 'шт', 'руб', 'цена', 'цены', 'стоимость',
    'артикул', 'арт', 'код', 'наименование', 'название', 'товар', 'товара', 'позиция', 'розница', 'розн', 'опт', 'оптом',
    'остаток', 'количество', 'кол', 'наличие', 'наличии', 'оригинал', 'аналог', 'price', 'sku', 'art', 'code', 'item', 'name',
    'новый', 'новая', 'новое', 'есть', 'нет']);
  const LATIN_STOP = new Set(['price', 'sku', 'art', 'code', 'item', 'name', 'kzt', 'row', 'pcs', 'original', 'orig', 'genuine', 'new', 'oem']);
  // match = the catalog number is on the row (that row is not marked); `settles` = it also settles the article for the others
  const NONE = { kind: 'none' }, MATCH = { kind: 'match', settles: false }, AMB = { kind: 'ambiguous' };
  const str = (v) => (v === undefined || v === null) ? '' : String(v);
  const toNum = (s) => Number(str(s).replace(/[.,]\d{1,2}$/, '').replace(/\D/g, ''));
  const norm = (s) => str(s).toLowerCase().replace(/ё/g, 'е');
  const codeKey = (t) => t.toUpperCase().replace(/[-\/]/g, '');
  const isArticle = (t) => {
    if (t.length < 5 || !/\d/.test(t)) return false;
    if (SPEC.some(re => re.test(t))) return false;
    const parts = t.split(/[-\/]/);
    if (parts.length > 1 && parts.every(x => SPEC_PART.test(x)) && parts.some(x => /[A-Za-z]$/.test(x))) return false;
    if (/[A-Za-z]/.test(t)) return true;
    const sep = (t.match(/[-\/]/g) || []).length;
    return sep >= 1 && sep <= 2 && t.replace(/\D/g, '').length >= 6;
  };
  const tokensOf = (line) => {
    const out = [];
    const re = new RegExp(TOKEN.source, 'g');
    let m;
    while ((m = re.exec(line))) if (isArticle(m[0])) out.push({ text: m[0], key: codeKey(m[0]), start: m.index, end: m.index + m[0].length });
    return out;
  };
  // codes blanked first (a code's letters — «YZZE», «BT» — are not a word both sides would share)
  const blankCodes = (s) => str(s).replace(new RegExp(TOKEN.source, 'g'), (m) => /\d/.test(m) ? ' '.repeat(m.length) : m);
  const plainWords = (s) => (norm(blankCodes(s)).match(/\p{L}{3,}/gu) || []).filter(w => !STOP.has(w));
  const latinWords = (s) => (blankCodes(s).match(/[A-Za-z]{3,}/g) || []).map(w => w.toLowerCase()).filter(w => !LATIN_STOP.has(w));
  const headOf = (name) => { const ws = plainWords(name); return ws.find(w => /[Ѐ-ӿ]/.test(w)) || ws[0] || ''; };
  const sameWord = (x, y) => {
    if (x === y) return true;
    if (x.length < 4 || y.length < 4) return false;
    let n = 0;
    while (n < x.length && n < y.length && x[n] === y[n]) n++;
    return n >= Math.min(5, Math.min(x.length, y.length) - 1);
  };
  const san = (s, max) => str(s).replace(/[\[\]]/g, (c) => c === '[' ? '(' : ')').replace(/\s+/g, ' ').trim().slice(0, max || 120);
  const parseCatalogPrice = (s) => {
    const m = str(s).trim().match(new RegExp('^(' + NUM + ')\\s*(?:' + CUR + ')?' + UNIT + '$', 'iu'));
    return m ? { n: toNum(m[1]), unit: m[2] ? norm(m[2]) : '' } : null;
  };
  const catalogFromItems = (list) => {
    const out = [];
    for (const it of Array.isArray(list) ? list : []) {
      const name = san(it && it.name, 120), priceText = san(it && it.price, 40);
      const price = parseCatalogPrice(priceText);
      if (!name || !price || !price.n) continue;
      const keys = Array.from(new Set(tokensOf(name).map(t => t.key)));
      if (!keys.length) continue;
      out.push({ kind: (it && it.kind) === 'service' ? 'service' : 'product', name, priceText, price, keys, head: headOf(name) });
    }
    return out;
  };
  // the catalog the bot really runs: the «ProductN: / ProductN Price:» lines of its published system prompt
  const catalogFromPrompt = (prompt) => {
    const byKey = new Map();
    const at = (kind, n) => { const k = kind + n; if (!byKey.has(k)) byKey.set(k, { kind: kind === 'Service' ? 'service' : 'product' }); return byKey.get(k); };
    for (const raw of str(prompt).split('\n')) {
      const line = raw.replace(/\r$/, '');
      let m = line.match(/^(Product|Service)(\d+): (.+)$/);
      if (m) { at(m[1], m[2]).name = m[3]; continue; }
      m = line.match(/^(Product|Service)(\d+) Price: (.*)$/);
      if (m) at(m[1], m[2]).price = m[3];
    }
    return catalogFromItems(Array.from(byKey.values()));
  };
  // a legacy panel client sends only «• name — price» lines
  const catalogFromLegacy = (text) => catalogFromItems(str(text).split('\n').map(l => {
    const m = l.match(/^\s*•\s*(.+)\s+—\s+([^—]+?)\s*$/);
    return m ? { kind: 'product', name: m[1], price: m[2] } : null;
  }).filter(Boolean));
  const currencies = (seg) => {
    const re = new RegExp('(?<![\\p{L}\\p{N}\\-\\/])(' + NUM + ')\\s*(' + CUR + ')' + UNIT, 'giu');
    const out = [];
    let m;
    while ((m = re.exec(seg))) {
      const before = seg.slice(0, m.index), rest = seg.slice(m.index + m[1].length);
      out.push({ n: toNum(m[1]), text: (m[1] + ' ' + m[2] + (m[3] ? '/' + m[3] : '')).replace(/\s+/g, ' ').trim(), unit: m[3] ? norm(m[3]) : '', index: m.index,
                 ambiguous: RANGE_BEFORE.test(before) || BOUND_BEFORE.test(before) || RANGE_AFTER.test(rest) });
    }
    return out;
  };
  const labelled = (seg) => {
    const re = new RegExp('(?<![\\p{L}])(' + LABEL + ')[^\\S\\n]*(?:,\\s*(?:тг|₸|kzt|тенге)\\.?|\\((?:тг|₸|kzt|тенге)\\))?\\s*[:=—–-]\\s*(' + NUM + ')\\s*((?:' + CUR + ')?)', 'giu');
    const out = [];
    let m;
    while ((m = re.exec(seg))) {
      const rest = seg.slice(m.index + m[0].length - m[3].length);
      out.push({ label: norm(m[1]), n: toNum(m[2]), text: (m[2] + (m[3] ? ' ' + m[3] : '')).trim(), unit: '', index: m.index, ambiguous: !m[3] && RANGE_AFTER.test(rest) });
    }
    return out;
  };
  // the numbers of the article's part of a line, codes and specs blanked (90915-YZZE1, 5W-30, 205/55R16, 04465-02220):
  // every amount as written («3 200» = 3200) and every bare digit run of a space-glued number («5 500» → 5, 500)
  const amountsOf = (s) => {
    const t = str(s).replace(new RegExp(TOKEN.source, 'g'), (m) => (/[A-Za-z\/]/.test(m) || /(?:^|-)0\d/.test(m)) ? ' '.repeat(m.length) : m);
    const whole = new Set(), parts = new Set();
    const re = new RegExp(NUM, 'g');
    let m;
    while ((m = re.exec(t))) whole.add(toNum(m[0]));
    for (const d of t.replace(/(\d)[   ](?=\d)/g, '$1').match(/\d+/g) || []) parts.add(Number(d));
    return { whole, parts };
  };
  // V9-r3 (S2): the number after a stock / quantity word is blanked before the row's amounts are read
  const QTY_NUM = new RegExp('(?<![\\p{L}])' + QTY_WORD + '[^\\S\\n]*[:=—–\\-]?[^\\S\\n]*(?:' + NUM + ')(?![^\\S\\n]*(?:\\d|' + CUR + '))', 'giu');
  const dropQty = (s) => str(s).replace(QTY_NUM, (m) => ' '.repeat(m.length));
  // V9-r3 (S1): the packs a text names, as «unit:quantity» («упак:2шт», «короб:?» when no quantity is stated, «за:2шт»);
  // two sides are comparable only with the SAME signature — a pack word on both sides is not enough
  const packsOf = (s) => {
    const t = str(s), out = new Set();
    const qty = (q) => q ? Number(q[1].replace(',', '.')) + (/^(?:шт|штук|ед|pcs|pc)/i.test(q[2]) ? 'шт' : norm(q[2]).replace(/^гр$/, 'г')) : '?';
    let m;
    const re = new RegExp(PACK_WORD.source, 'giu');
    while ((m = re.exec(t))) {
      const w = norm(m[1]), from = m.index + m[0].length - m[1].length, at = m.index + m[0].length;
      const kind = (PACK_KIND.find(k => k[0].test(w)) || [null, w])[1];
      const q = t.slice(at, at + 24).split(/[;|\n]/)[0].match(COUNT_AFTER) || t.slice(Math.max(0, from - 16), from).split(/[;|\n]/).pop().match(COUNT_BEFORE);
      out.add(kind + ':' + qty(q));
    }
    const per = new RegExp(PER_N.source, 'giu');
    while ((m = per.exec(t))) out.add('за:' + Number(m[1]) + 'шт');
    return Array.from(out).sort().join(',');
  };
  const CONVERTER = /^\s*[\p{L}_][\p{L}\p{N}_]*\[\d+\]:\s/u;
  const singlePrice = (v) => {
    const m = str(v).trim().match(new RegExp('^(' + NUM + ')\\s*((?:' + CUR + ')?)' + UNIT + '\\s*$', 'iu'));
    return m ? { n: toNum(m[1]), text: (m[1] + (m[2] ? ' ' + m[2] : '') + (m[3] ? '/' + m[3] : '')).trim(), unit: m[3] ? norm(m[3]) : '' } : null;
  };
  const inBracket = (s) => (str(s).match(/\(/g) || []).length > (str(s).match(/\)/g) || []).length;
  // «для» in «Средство для посуды WS-500» is the item's own name, not a cross-reference: a word the catalog name has is skipped
  const crossBefore = (s, group) => (str(s).split(/[;|\t]/).pop().match(/[\p{L}\p{N}][\p{L}\p{N}\-\/.]*/gu) || []).slice(-4)
    .some(w => { const x = norm(w.replace(/\.+$/, '')); return CROSS_KW.test(x) && !group.some(it => (norm(it.name).match(/[\p{L}\p{N}]+/gu) || []).includes(x)); });
  const unitKey = (u) => PIECE.has(norm(u)) ? '' : norm(u).slice(0, 3);
  // the line is POSITIVELY about the catalog item: its first word is the item's head word (or the name's first word),
  // every Russian word of the item part is a word of the name, every Latin word of 3+ letters too, no kit / used word
  const identified = (part, latin, kit, group) => {
    const ws = plainWords(part);
    if (!ws.length) return false;
    const lat = latinWords(latin);
    return group.some(it => {
      const nameWs = plainWords(it.name), nameLat = new Set(latinWords(it.name));
      if (![it.head, nameWs[0]].filter(Boolean).some(h => sameWord(h, ws[0]))) return false;
      if (!ws.every(w => !/[Ѐ-ӿ]/.test(w) || nameWs.some(n => sameWord(n, w)))) return false;
      if (!lat.every(w => nameLat.has(w))) return false;
      return !(KIT.test(kit) && !KIT.test(it.name));
    });
  };
  // one article on one line → { kind: 'none' | 'match' | 'conflict' | 'ambiguous', prices, firm, item, sure }
  const analyse = (rawLine, tok, toks, group) => {
    const line = rawLine.replace(LINE_PREFIX, (m) => ' '.repeat(m.length));
    const prevEnd = toks.filter(t => t.end <= tok.start).reduce((a, t) => Math.max(a, t.end), 0);
    const next = toks.find(t => t.start >= tok.end);
    const own = line.slice(prevEnd, tok.start), after = line.slice(tok.end, next ? next.start : line.length);
    const leading = !toks.some(t => t.end <= tok.start);
    // the article's own part of the line: after a previous article, only what follows that article's last number
    let scope = (leading ? own : own.replace(/^[\s\S]*\d/u, '')) + ' ' + after, prices = null, sure = true, shape = leading, cross = false, item = null;
    // V9-r3 (S2): the catalog number among the row's amounts (stock / quantity numbers left out) keeps THIS row unmarked;
    // whether it also settles the article for the other rows is decided at the end — the item itself, by its PRICE
    let hit = false, amountText = scope;
    const done = (r) => hit ? MATCH : r;
    const atCatalog = (s) => group.some(it => amountsOf(s).whole.has(it.price.n));
    if (CONVERTER.test(line)) {
      const body = line.slice(line.indexOf(']:') + 2);
      const cols = body.split(';').map(part => {
        const i = part.indexOf(': ');
        return i >= 0 ? { label: part.slice(0, i).trim(), value: part.slice(i + 2).trim() } : { label: '', value: part.trim() };
      }).filter(c => c.value);
      const ownCol = cols.find(c => tokensOf(c.value).some(t => t.key === tok.key));
      if (ownCol) {
        if (ownCol.label && ANALOG_COL.test(ownCol.label)) return NONE;
        const head = ownCol.value.slice(0, tokensOf(ownCol.value).find(t => t.key === tok.key).start);
        if (ANALOG_BEFORE.test(head)) return NONE;
        cross = inBracket(head) || crossBefore(head, group) || ANALOG_WORD.test(ownCol.value);
      }
      const priceCols = cols.filter(c => c.label && PRICE_COL.test(c.label) && /\d/.test(c.value));
      if (priceCols.length) {
        scope = body;                                                   // a converter row is ONE item: all its columns
        amountText = cols.filter(c => !(c.label && QTY_COL.test(c.label) && !PRICE_COL.test(c.label)))   // a stock / qty column is no amount
          .map(c => (c.label ? c.label + ': ' : '') + c.value).join('; ');
        hit = atCatalog(amountText);
        const articles = cols.filter(c => c.label && ARTICLE_COL.test(c.label) && !PRICE_COL.test(c.label) && !ANALOG_COL.test(c.label));
        if (articles.length && !articles.some(c => tokensOf(c.value).some(t => t.key === tok.key))) cross = true;   // the row's own article is another code
        const parsed = priceCols.map(c => { const p = singlePrice(c.value); return p ? Object.assign(p, { label: c.label }) : null; });
        if (parsed.some(p => !p)) return done(AMB);
        prices = parsed;
        shape = true;
        const nameCol = cols.find(c => c.label && NAME_COL.test(c.label) && !PRICE_COL.test(c.label));
        const brand = cols.filter(c => c.label && BRAND_COL.test(c.label)).map(c => c.value).join(' ');
        item = nameCol ? { part: nameCol.value, latin: nameCol.value + ' ' + brand, kit: nameCol.value } : null;
      }
    }
    if (!prices) {
      if (ANALOG_BEFORE.test(line.slice(0, tok.start))) return NONE;
      amountText = dropQty(scope);
      hit = atCatalog(amountText);                                     // «по акции 3200», «РРЦ: 3200», PDF «3200 150»
      cross = cross || inBracket(own) || crossBefore(own, group);
      const lab = labelled(after), cur = currencies(after);
      const pre = leading ? currencies(line.slice(0, tok.start)) : [];
      let at = after.length;
      if (lab.length) {
        if (lab.some(p => p.ambiguous)) return done(AMB);
        prices = lab; at = lab[0].index;
      } else if (cur.length) {
        if (cur.length > 1 || cur[0].ambiguous || pre.length) return done(AMB);
        prices = [cur[0]]; at = cur[0].index;
      } else if (toks.length === 1 && pre.length) {
        if (pre.length > 1 || pre[0].ambiguous) return done(AMB);
        prices = [pre[0]];
      } else if (toks.length === 1) {
        const tail = dropQty(after).match(new RegExp('(?<![\\p{L}\\p{N}\\-\\/])(' + NUM + ')\\s*;?\\s*$', 'u'));   // «… Остаток: 3200» is no tail price
        if (tail && tail[1].replace(/\D/g, '').length >= 3) {
          const n = toNum(tail[1]);
          const b = after.slice(0, tail.index);
          if (n >= 1950 && n <= 2035 && /^\d{4}$/.test(tail[1])) return done(NONE);   // a bare year («Camry 2019») is no price
          if (RANGE_BEFORE.test(b) || BOUND_BEFORE.test(b)) return done(AMB);
          prices = [{ n, text: tail[1].trim(), unit: '' }]; at = tail.index; sure = false;   // a bare number: maybe a stock / qty / spec column
        }
      }
      // a price is named on the line, but none that can be pinned to this article
      if (!prices) return done((labelled(line).length || currencies(line).length) ? AMB : NONE);
      cross = cross || ANALOG_WORD.test(own + ' ' + after.slice(0, at));
      item = { part: plainWords(own).length ? own : after.slice(0, at), latin: own + ' ' + after.slice(0, at), kit: own + ' ' + after };
    }
    // a price that cannot be compared with the catalog's: another currency, thousands, VAT, another unit, and (V9-r3 S1) a
    // pack the catalog item does not name with the SAME unit and quantity — no pack on either side is the same pack
    if (FOREIGN.test(scope) || SCALE.test(scope) || VAT.test(scope)) return done(AMB);
    const rowPacks = packsOf(scope);
    if (!group.some(it => packsOf(it.name + ' ' + it.priceText) === rowPacks)) return done(AMB);
    const units = new Set(group.map(it => unitKey(it.price.unit)));
    if (prices.some(p => !units.has(unitKey(p.unit)))) return done(AMB);
    const same = shape && !cross && !!item && identified(item.part, item.latin, item.kit, group);
    // V9-r3 (S2): the row settles its article only as the item ITSELF (the firm mark's rules) with the catalog number as its
    // PRICE — a price column / labelled / currency amount, never a bare number; an accessory or a cross-reference at the
    // catalog price only keeps its own row unmarked
    if (hit) return { kind: 'match', settles: same && sure && prices.some(p => group.some(it => it.price.n === p.n)) };
    if (group.some(it => amountsOf(amountText).parts.has(it.price.n))) return AMB;   // the catalog number glued into «5 500», «10 900 тг»
    return { kind: 'conflict', prices, firm: same && sure, item: !same, sure };
  };
  const catalogPart = (group) => group.map(it => '(' + (it.kind === 'service' ? 'Services' : 'Products') + ') «' + it.name + '» — ' + it.priceText).join('; ');
  // a column header goes into the mark only as its price words («Цена опт») — never as free text from the document
  const labelText = (l) => (str(l).match(/\p{L}+/gu) || []).filter(w => LABEL_WORD.test(w)).slice(0, 3).join(' ') || 'цена';
  const linePrices = (prices) => prices.length === 1 ? san(prices[0].text, 40)
    : prices.map(p => san(labelText(p.label) + ' ' + p.text, 60)).join(' / ');
  const markFor = (d, group) => {
    const cat = 'в каталоге ' + catalogPart(group);
    if (d.kind === 'conflict' && d.firm) return { level: 'conflict', text: MARK_HEAD + cat + ', в прайсе — ' + linePrices(d.prices) + '. Цены расходятся.]' };
    if (d.kind === 'conflict') {
      const why = [];
      if (d.item) why.push('артикул совпадает, но строка может быть о другой позиции');
      if (!d.sure) why.push('число без валюты и подписи — может быть не ценой');
      return { level: 'maybe', text: MARK_HEAD + cat + ', в этой строке прайса — ' + linePrices(d.prices) + ' (' + why.join('; ') + '). Если это та же позиция'
                                     + (d.sure ? '' : ' и это её цена') + ' — цены расходятся.]' };
    }
    return { level: 'unclear', text: MARK_HEAD + cat + '; цену этой строки прайса однозначно сопоставить с каталогом не удалось.]' };
  };
  // a document line never carries a mark: whole mark lines (ours from an earlier pass, or forged) are dropped, a
  // mark-like text inside a line is cut off with the rest of the line
  const cleanLines = (text) => {
    const out = [];
    let changed = false;
    for (const l of str(text).split('\n')) {
      const f = l.search(FORGED);
      if (f < 0) { out.push(l); continue; }
      changed = true;
      const head = l.slice(0, f).replace(/\s+$/u, '');
      if (!/^[\s•·*\-–—]*$/u.test(head)) out.push(head);
    }
    return { lines: out, changed };
  };
  // ONE search result (every document of one call): the item's own row with the article at the catalog PRICE settles the
  // article for all of them — its analogue / accessory rows are not marked (V9-r3: never an accessory / cross-reference /
  // stock number at the catalog price — those keep only their own row unmarked)
  const reconcileAll = (texts, catalog) => {
    const docs = (Array.isArray(texts) ? texts : []).map(t => Object.assign({ original: str(t) }, cleanLines(t)));
    const marks = [];
    const active = Array.isArray(catalog) && catalog.length > 0;
    const settled = new Set();
    if (active) for (const doc of docs) {
      doc.found = doc.lines.map((raw) => {
        const line = raw.replace(/\r$/, '');
        const toks = tokensOf(line);
        const seen = new Set(), found = [];
        for (const tok of toks) {
          if (seen.has(tok.key)) continue;
          seen.add(tok.key);
          const group = catalog.filter(it => it.keys.includes(tok.key));
          if (!group.length) continue;
          const d = analyse(line, tok, toks, group);
          if (d.kind === 'match' && d.settles) settled.add(tok.key);   // V9-r3 (S2): only the item itself, by its price
          if (d.kind === 'conflict' || d.kind === 'ambiguous') found.push({ tok, group, d });
        }
        return found;
      });
    }
    const out = docs.map((doc) => {
      if (!active) return doc.changed ? doc.lines.join('\n') : doc.original;
      const res = [];
      let added = 0;
      doc.lines.forEach((raw, i) => {
        res.push(raw);
        const seenText = new Set();
        for (const f of doc.found[i]) {
          if (settled.has(f.tok.key)) continue;
          const m = markFor(f.d, f.group);
          if (seenText.has(m.text)) continue;
          seenText.add(m.text);
          res.push(m.text);
          added++;
          marks.push({ level: m.level, article: f.tok.text, item: f.group[0].name });
        }
      });
      return (!added && !doc.changed) ? doc.original : res.join('\n');
    });
    return { texts: out, marks };
  };
  const reconcileText = (text, catalog) => { const r = reconcileAll([text], catalog); return { text: r.texts[0], marks: r.marks }; };
  // the marks a text carries (after the window was cut): conflict = the firm «… Цены расходятся.]»
  const marksIn = (text) => str(text).split('\n').filter(l => l.startsWith(MARK_HEAD))
    .map(l => ({ level: l.endsWith('. Цены расходятся.]') ? 'conflict' : (l.includes('однозначно сопоставить') ? 'unclear' : 'maybe') }));
  return { catalogFromPrompt, catalogFromItems, catalogFromLegacy, reconcileText, reconcileAll, marksIn, MARK_HEAD };
})();
// PRICE-RECONCILE-END"""

GUARD_JS = r"""// PRICE SEARCH / Guard (generated by Tools/n8n/build-price-search.py — edit there). A search runs only for a real bot
// id under one of the two metadata keys: '-1' / '' (an unauthed channel) would match every bot's shared chunks.
const j = $input.first().json || {};
const s = (v, max) => String(v === undefined || v === null ? '' : v).trim().slice(0, max);
const query = s(j.query, 1000);
const filterKey = ['botWaId', 'botTgId'].includes(j.filterKey) ? j.filterKey : '';
const botId = s(j.botId, 64);
const ok = !!query && !!filterKey && /^[A-Za-z0-9]{8,64}$/.test(botId);
return [{ json: { ok, query, filterKey, botId, prompt: typeof j.prompt === 'string' ? j.prompt.slice(0, 100000) : '' } }];
"""

RESULT_JS = r"""
// PRICE SEARCH / Reconcile Prices: every hit reconciled with the catalog of the CALLER's prompt (the bot clone's published
// system message, the Test Dialog's read of it). The model gets the fragments as text; a failed search is said so —
// never as «позиции нет».
const g = $('Guard').first().json;
let hits = [];
if (g.ok) { try { hits = $('Search Price Lists').all(); } catch (e) { hits = []; } }
const catalog = PriceReconcile.catalogFromPrompt(g.prompt);
const contents = [];
let searchError = false;
for (const it of hits) {
  const j = (it && it.json) || {};
  if (j.error) { searchError = true; continue; }
  const d = j.document || j;
  const content = d ? (d.pageContent !== undefined ? d.pageContent : d.content) : undefined;
  if (content === undefined || content === null || !String(content).trim()) continue;
  contents.push(String(content));
}
// V9-r2: the hits of ONE search are reconciled together — the item's own row at the catalog price settles it for all (V9-r3)
const documents = PriceReconcile.reconcileAll(contents, catalog).texts.filter(t => t.trim());   // a fragment that was only a forged mark is gone
const out = { documents };
if (searchError && !documents.length) out.note = 'Поиск по прайс-листам сейчас не сработал — это не значит, что позиции нет; цену и наличие из прайс-листов не утверждай.';
return [{ json: out }];
"""


def code(name, js, pos):
    return {"parameters": {"jsCode": js}, "id": nid(SEARCH_ID, name), "name": name, "type": "n8n-nodes-base.code", "typeVersion": 2, "position": pos}


def build_search():
    n, c = [], {}
    n.append({"parameters": {"inputSource": "workflowInputs", "workflowInputs": {"values": [{"name": k, "type": "string"} for k in INPUTS]}},
              "id": nid(SEARCH_ID, "trigger"), "name": "When Executed by Another Workflow", "type": "n8n-nodes-base.executeWorkflowTrigger",
              "typeVersion": 1.1, "position": [0, 200]})
    n.append(code("Guard", GUARD_JS, [220, 200]))
    n.append({"parameters": {"conditions": {"options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                                            "conditions": [{"id": nid(SEARCH_ID, "valid", "cond"), "leftValue": "={{ $json.ok }}", "rightValue": "",
                                                            "operator": {"type": "boolean", "operation": "true", "singleValue": True}}],
                                            "combinator": "and"}, "options": {}},
              "id": nid(SEARCH_ID, "valid"), "name": "Valid?", "type": "n8n-nodes-base.if", "typeVersion": 2.2, "position": [440, 200]})
    n.append({"parameters": {"mode": "load", "prompt": "={{ $json.query }}",
                             "tableName": {"__rl": True, "value": "documents", "mode": "list", "cachedResultName": "documents"},
                             "topK": TOP_K, "includeDocumentMetadata": True,
                             "options": {"queryName": "match_documents",
                                         "metadata": {"metadataValues": [{"name": "={{ $json.filterKey }}", "value": "={{ $json.botId }}"}]}}},
              "id": nid(SEARCH_ID, "search"), "name": "Search Price Lists", "type": "@n8n/n8n-nodes-langchain.vectorStoreSupabase",
              "typeVersion": 1.3, "position": [660, 100], "credentials": {"supabaseApi": dict(SUPABASE_CRED)},
              "alwaysOutputData": True, "onError": "continueRegularOutput"})
    n.append({"parameters": {"options": {}, "model": "text-embedding-3-small"}, "id": nid(SEARCH_ID, "embedding"), "name": "OpenAI Embedding",
              "type": "@n8n/n8n-nodes-langchain.embeddingsOpenAi", "typeVersion": 1.2, "position": [660, 320], "credentials": {"openAiApi": dict(OPENAI_CRED)}})
    n.append(code("Reconcile Prices", RECONCILE_JS + "\n" + RESULT_JS, [900, 200]))
    def edge(src, dst, out=0):
        outs = c.setdefault(src, {"main": []})["main"]
        while len(outs) <= out:
            outs.append([])
        outs[out].append({"node": dst, "type": "main", "index": 0})

    edge("When Executed by Another Workflow", "Guard")
    edge("Guard", "Valid?")
    edge("Valid?", "Search Price Lists", 0)
    edge("Valid?", "Reconcile Prices", 1)
    edge("Search Price Lists", "Reconcile Prices")
    c["OpenAI Embedding"] = {"ai_embedding": [[{"node": "Search Price Lists", "type": "ai_embedding", "index": 0}]]}
    settings = {"executionOrder": "v1", "saveDataSuccessExecution": "none", "saveDataErrorExecution": "all", "saveManualExecutions": False}
    return {"id": SEARCH_ID, "name": SEARCH_NAME, "active": False, "nodes": n, "connections": c, "pinData": {},
            "settings": settings, "staticData": None, "meta": None, "tags": []}


def tool_node(bot_id_expr, filter_key_expr, prompt_expr, node_id, position):
    """The price-list search as the agent's tool: a toolWorkflow named like the retired retrieve-as-tool."""
    values = {"query": QUERY_FROM_AI, "botId": bot_id_expr, "filterKey": filter_key_expr, "prompt": prompt_expr}
    schema = [{"id": k, "displayName": k, "required": False, "defaultMatch": False, "display": True, "canBeUsedToMatch": True,
               "type": "string", "removed": False} for k in INPUTS]
    return {"parameters": {"description": TOOL_DESCRIPTION, "workflowId": {"__rl": True, "value": SEARCH_ID, "mode": "id"},
                           "workflowInputs": {"mappingMode": "defineBelow", "value": values, "matchingColumns": [], "schema": schema,
                                              "attemptToConvertTypes": False, "convertFieldsToString": False}},
            "id": node_id, "name": TOOL_NAME, "type": "@n8n/n8n-nodes-langchain.toolWorkflow", "typeVersion": 2.2, "position": position}


def template_tool(wf, filter_key):
    old = next(x for x in wf["nodes"] if x["name"] == TOOL_NAME)
    return tool_node("={{ $workflow.id }}", filter_key, PROMPT_OF_AGENT, old["id"], old["position"])


def patch_template(fname):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    before = json.dumps(wf, sort_keys=True, ensure_ascii=False)
    i = next(k for k, x in enumerate(wf["nodes"]) if x["name"] == TOOL_NAME)
    wf["nodes"][i] = template_tool(wf, TEMPLATES[fname])          # in place: the index of every other node is unchanged
    wf["nodes"] = [x for x in wf["nodes"] if x["name"] != OLD_EMBEDDING]
    wf["connections"].pop(OLD_EMBEDDING, None)
    wf["connections"][TOOL_NAME] = {"ai_tool": [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]]}
    assert wf["nodes"][0]["name"] == "Webhook" and wf["nodes"][5]["name"] == "AI Agent", f"{fname}: Create/Edit patch nodes[0]/nodes[5] by index"
    if json.dumps(wf, sort_keys=True, ensure_ascii=False) == before:
        return False
    write(path, wf)
    return True


PROTECTED_ANCHOR = "  'crHandoffDry0001'\n]);"


def patch_set_state():
    path = os.path.join(WF, SET_STATE)
    wf = json.load(open(path, encoding="utf-8"))
    ch = False
    for n in wf["nodes"]:
        js = n["parameters"].get("jsCode", "")
        if "const PROTECTED = new Set([" in js and f"'{SEARCH_ID}'" not in js:
            assert js.count(PROTECTED_ANCHOR) == 1, "Set Workflow State: the protected list has an unexpected shape"
            n["parameters"]["jsCode"] = js.replace(PROTECTED_ANCHOR, f"  'crHandoffDry0001',\n  '{SEARCH_ID}'\n]);")
            ch = True
    if ch:
        write(path, wf)
    return ch


def write(path, wf):
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(wf, fh, indent=2, ensure_ascii=False)
        fh.write("\n")


def search_text():
    return json.dumps(build_search(), indent=2, ensure_ascii=False) + "\n"


def check():
    stale = []
    p = os.path.join(WF, SEARCH_FILE)
    if not os.path.exists(p) or open(p, encoding="utf-8").read() != search_text():
        stale.append(SEARCH_FILE)
    for fname, key in TEMPLATES.items():
        wf = json.load(open(os.path.join(WF, fname), encoding="utf-8"))
        tool = next((x for x in wf["nodes"] if x["name"] == TOOL_NAME), None)
        if tool is None or tool != template_tool(wf, key) or any(x["name"] == OLD_EMBEDDING for x in wf["nodes"]) \
                or wf["connections"].get(TOOL_NAME) != {"ai_tool": [[{"node": "AI Agent", "type": "ai_tool", "index": 0}]]} \
                or OLD_EMBEDDING in wf["connections"]:
            stale.append(fname)
    wf = json.load(open(os.path.join(WF, SET_STATE), encoding="utf-8"))
    if not any(f"'{SEARCH_ID}'" in n["parameters"].get("jsCode", "") for n in wf["nodes"]):
        stale.append(SET_STATE)
    return stale


def main():
    if "--check" in sys.argv:
        stale = check()
        print("price search:", "OK" if not stale else f"STALE {stale}")
        sys.exit(2 if stale else 0)
    p = os.path.join(WF, SEARCH_FILE)
    text = search_text()
    if not os.path.exists(p) or open(p, encoding="utf-8").read() != text:
        open(p, "w", encoding="utf-8").write(text)
        print("wrote     ", SEARCH_FILE)
    else:
        print("up to date", SEARCH_FILE)
    for fname in TEMPLATES:
        print("patched   " if patch_template(fname) else "up to date", fname)
    print("patched   " if patch_set_state() else "up to date", SET_STATE)


if __name__ == "__main__":
    main()
