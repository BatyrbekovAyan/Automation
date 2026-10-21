import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from '../build.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const dist = fs.mkdtempSync(path.join(os.tmpdir(), 'crl-copy-'));
build({ src: path.join(ROOT, 'src'), dist, legal: path.resolve(ROOT, '../../docs/legal') });
const html = fs.readFileSync(path.join(dist, 'index.html'), 'utf8');
// &nbsp;/&#160; are the idiomatic way to author a hard space in raw HTML — decode them (plus a
// handful of other named entities that show up in running RU copy) before stripping tags, so the
// guard checks the text a browser actually renders, not its HTML source encoding.
const text = html
  .replace(/<script[\s\S]*?<\/script>/g, ' ')
  .replace(/<style[\s\S]*?<\/style>/g, ' ')
  .replace(/<[^>]+>/g, ' ')
  .replace(/&nbsp;|&#160;/g, ' ')
  .replace(/&amp;/g, '&')
  .replace(/&mdash;/g, '—')
  .replace(/&ndash;/g, '–');

test('нет запрещённых обещаний', () => {
  for (const re of [/отзыв/i, /\d[\d\s ]*\+?\s*(компан|клиентов|бизнесов)/i, /за\s+\d+\s*сек/i, /рейтинг/i, /\b1С\b|\bCRM\b/, /казахск/i, /на паузу/i])
    assert.ok(!re.test(text), `найдено: ${re}`);
});
test('цены тарифов на месте, с NBSP', () => {
  for (const p of ['9 990', '19 990', '39 900', '3 900']) assert.ok(text.includes(p), p);
});
test('юр-ссылки и форма с приёмником заявок', () => {
  assert.ok(html.includes('href="/privacy.html"') && html.includes('href="/terms.html"'));
  assert.ok(html.includes('data-endpoint="https://n8n.choosereply.com/webhook/LandingLead"'));
  assert.ok(/name="website"/.test(html) && /name="source" value="landing"/.test(html));
});
test('все восемь секций собраны, маркеров не осталось', () => {
  for (const id of ['id="top"', 'id="how"', 'id="demo"', 'id="niches"', 'id="start"', 'id="pricing"', 'id="faq"', 'id="lead"']) assert.ok(html.includes(id), id);
  assert.ok(!html.includes('__V__') && !html.includes('@sections') && !html.includes('@jsonld'));
});
test('нет внешних скриптов/стилей (самохостинг)', () => {
  assert.ok(!/(src|href)="https:\/\/(cdn\.jsdelivr\.net|fonts\.googleapis\.com|fonts\.gstatic\.com|cdnjs)/.test(html));
});

// ---- Lens fixes (2026-09-22, линза владельца) ----
// NBSP → обычный пробел: здесь проверяется формулировка, неразрывность стережёт typography.test
const plain = text.replace(/\u00a0/g, ' ').replace(/[ \t]+/g, ' ');
test('тарифы: «канал» расшифрован, «диалог» определён, возможности одинаковы во всех тарифах', () => {
  assert.ok(plain.includes('1 канал — WhatsApp или Telegram'), 'покупатель «Старта» знает, что канал один');
  assert.ok(plain.includes('3 канала — например, WhatsApp + Telegram'));
  assert.ok(plain.includes('Диалог — это один клиент за сутки'), 'главная единица цены определена');
  assert.ok(/Режимы «Авто» и «Выбрать», прайс-листы и «Сводка» есть везде/.test(plain));
  assert.ok(!plain.includes('Честные цены'), 'самооценка в заголовке тарифов');
});
test('FAQ: страхи «с вашего номера» названы, риск блокировки не обещан', () => {
  for (const q of ['Сотрудник будет отвечать всем подряд — и родным?', 'Не заблокирует ли WhatsApp мой номер?', 'А если клиент пришлёт голосовое?']) assert.ok(plain.includes(q), q);
  assert.ok(plain.includes('В групповых чатах сотрудник не отвечает.'));
  assert.ok(!/не заблокируют|гарантируем|без риска/i.test(plain), 'обещать, что номер не заблокируют, нельзя');
});
test('доверие: юрлицо и контакт в подвале, цена и «Сделано в Казахстане» на первом экране', () => {
  assert.ok(plain.includes('ТОО «Synergy Expert Group» · БИН 180940030158 · Астана'));
  assert.ok(html.includes('href="mailto:synergyexpertgroup@gmail.com"'));
  assert.match(html, /<a class="hero-price" href="#pricing">от&nbsp;9&nbsp;990&nbsp;₸\/мес/);
  assert.match(html, /<a class="hdr-price" href="#pricing">Тарифы<\/a>/, 'на телефоне меню скрыто — ссылка на цены остаётся');
  assert.match(html, /<span class="sg-store sg-store-kz">Сделано в&nbsp;Казахстане<\/span>/);
  assert.match(html, /id="leadWhy">Номер нужен только для&nbsp;связи о&nbsp;запуске/, 'зачем номер — рядом с полем');
  assert.match(html, /aria-describedby="leadWhy leadErr"/);
});
test('демо и финал: аналог, который действительно подходит к RAV4 (Filtron OE 685/1, не OP 570)', () => {
  assert.ok(!/OP\s?570/.test(plain) && plain.includes('OE 685/1'));
});

// ---- Final review (2026-09-22), M-7 ----
// The demo's dialogues are assembled by demo.js from SCENARIOS — the built HTML carries only the first
// one, so the page scan above never sees the rest. The same forbidden promises are checked in the data.
import { SCENARIOS } from '../../src/js/demo/scenarios.js';
const strings = (o) => (typeof o === 'string' ? [o] : o && typeof o === 'object' ? Object.values(o).flatMap(strings) : []);
const FORBIDDEN = [/отзыв/i, /\d[\d\s ]*\+?\s*(компан|клиентов|бизнесов)/i, /за\s+\d+\s*сек/i, /рейтинг/i, /\b1С\b|\bCRM\b/, /казахск/i, /на паузу/i,
  /переда(ёт|ет|л|м)\s+(диалог|менеджер|владельц)/i, /Передаю менеджеру/i];
test('демо-сценарии (собираются JS-ом): те же запреты, что и для текста страницы', () => {
  const all = strings(SCENARIOS).join('\n').replace(/\u00a0/g, ' ');
  assert.ok(all.length > 1000 && all.includes('OE 685/1'), 'строки сценариев собраны');
  for (const re of FORBIDDEN) assert.ok(!re.test(all), `в сценариях найдено: ${re}`);
  for (const re of FORBIDDEN) assert.ok(!re.test(plain), `на странице найдено: ${re}`);
});
test('Kaspi: ссылка на товар — только если она есть в каталоге', () => {
  assert.ok(plain.includes('присылает ссылку на товар, если она в нём указана'));
  assert.ok(!plain.includes('даёт ссылку на товар в вашем магазине'));
});
