// Task 8 — «Ниши» (marquee + карточки) и «Три шага». Markup/content contract for
// sections/50-niches.html и sections/60-steps.html — niches.js has no pure exported
// logic (it's DOM-only: IntersectionObserver + pointermove), so what's worth pinning
// is the copy (exact strings from task-8-brief.md) and the structural invariants the
// CSS/JS depend on (reveal attributes, marquee duplication, line count).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const niches = fs.readFileSync(path.join(HERE, '../../src/sections/50-niches.html'), 'utf8');
const steps = fs.readFileSync(path.join(HERE, '../../src/sections/60-steps.html'), 'utf8');
const text = (s) => s.replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').replace(/\s+/g, ' ').trim();

// -------- 50-niches.html --------

test('ниши: секция #niches с модулем niches, заголовок дословно', () => {
  assert.match(niches, /<section class="niches[^"]*" id="niches" data-module="niches"/);
  assert.equal(text(niches.match(/<p class="eyebrow"[^>]*>([\s\S]*?)<\/p>/)[1]), 'Ниши');
  assert.match(niches, /<h2 id="ni-h" data-split>/);
});

test('ниши: лента-marquee — 6 названий + хвост, продублирована для бесшовной прокрутки', () => {
  const groups = [...niches.matchAll(/<div class="marquee-group">([\s\S]*?)<\/div>/g)].map((m) => m[1]);
  assert.equal(groups.length, 2, 'ровно 2 группы (дубликат для scaleX(-50%) луп)');
  assert.equal(groups[0], groups[1], 'группы идентичны — иначе прокрутка дёргается на стыке');
  const items = [...groups[0].matchAll(/<span class="marquee-item[^"]*">([\s\S]*?)<\/span>/g)].map((m) => text(m[1]));
  assert.deepEqual(items, [
    'Автозапчасти', 'Оптовая торговля', 'Цветы', 'Kaspi-продавцы', 'Обучение', 'Ремонт телефонов',
    'и любой бизнес, где клиенты пишут в мессенджеры',
  ]);
});

const NICHES = [
  ['Автозапчасти', 'Есть колодки на Camry 2015?', 'Подбирает по марке и году, называет артикул, оригинал и аналог из вашего прайса.'],
  ['Оптовая торговля', 'Какая цена от 10 коробок?', 'Отвечает по оптовому прайсу и собирает заявку: позиции, количество, контакты.'],
  ['Цветы', 'Соберёте букет к 18:00?', 'Знает состав и цены букетов, уточняет адрес и время — готовый заказ сразу виден вам в «Сводке».'],
  // финальное ревью, M-7: ссылка на товар — только если она есть в каталоге (бриф обещал её всегда)
  ['Kaspi-продавцы', 'Есть в наличии? Можно в рассрочку?', 'Отвечает про наличие и цены по вашему каталогу и присылает ссылку на товар, если она в нём указана.'],
  ['Обучение', 'Когда ближайший набор и сколько стоит?', 'Рассказывает о курсах, расписании и ценах, собирает заявку на пробное занятие.'],
  ['Ремонт телефонов', 'Сколько стоит замена экрана на iPhone 13?', 'Называет цену «от» по прайсу и собирает заявку на диагностику — точную цену подтверждает мастер.'],
];
const cards = niches.split(/(?=<article class="nch-card")/).slice(1);

test('ниши: ровно 6 карточек, каждая — reveal + иконка', () => {
  assert.equal(cards.length, 6);
  for (const c of cards) {
    assert.match(c, /^<article class="nch-card" data-reveal>/);
    assert.match(c, /<span class="nch-icon" aria-hidden="true"><svg viewBox="0 0 24 24">/);
  }
});

NICHES.forEach(([title, q, desc], i) => {
  test(`ниша ${i + 1} «${title}»: заголовок/вопрос/описание дословно из брифа`, () => {
    const c = cards[i];
    assert.equal(text(c.match(/<h3>([\s\S]*?)<\/h3>/)[1]), title);
    assert.equal(text(c.match(/<p class="nch-q">([\s\S]*?)<\/p>/)[1]), `«${q}»`);
    assert.equal(text(c.match(/<p class="nch-desc">([\s\S]*?)<\/p>/)[1]), desc);
  });
});

// -------- 60-steps.html --------

test('шаги: секция #start, заголовок дословно', () => {
  assert.match(steps, /<section class="steps sec" id="start"/);
  assert.equal(text(steps.match(/<p class="eyebrow"[^>]*>([\s\S]*?)<\/p>/)[1]), 'Начало работы');
  assert.equal(text(steps.match(/<h2 id="st-h"[^>]*>([\s\S]*?)<\/h2>/)[1]), 'Три шага — и бот работает.');
});

const STEPS = [
  ['01', 'Подключите номер', 'WhatsApp или Telegram по QR-коду или коду, за пару минут. Отдельная SIM не нужна.'],
  ['02', 'Загрузите прайс', 'Фото, Excel, PDF или Word; или заведите товары и услуги в приложении.'],
  ['03', 'Выберите режим', '«Авто»: бот отвечает сам. «Вместе»: предлагает варианты, отправляете вы.'],
];
const stepArticles = steps.split(/(?=<article class="stp")/).slice(1);

test('шаги: ровно 3 карточки и 2 соединительные линии, все под reveal', () => {
  assert.equal(stepArticles.length, 3);
  const lines = [...steps.matchAll(/<span class="stp-line" data-reveal aria-hidden="true"><\/span>/g)];
  assert.equal(lines.length, 2, 'линия между 01-02 и 02-03, не после 03');
});

STEPS.forEach(([num, title, desc], i) => {
  test(`шаг ${num} «${title}»: номер, заголовок и текст дословно`, () => {
    const a = stepArticles[i];
    assert.match(a, /^<article class="stp" data-reveal>/);
    assert.equal(text(a.match(/<span class="stp-num">([\s\S]*?)<\/span>/)[1]), num);
    assert.equal(text(a.match(/<h3>([\s\S]*?)<\/h3>/)[1]), title);
    assert.equal(text(a.match(/<p>([\s\S]*?)<\/p>/)[1]), desc);
  });
});

// -------- правдивость (global-constraints.md — запрещённый список) --------

test('правдивость: ни в нишах, ни в шагах нет запрещённых обещаний', () => {
  const all = text(niches) + ' ' + text(steps);
  for (const re of [/отзыв/i, /\d+\s*\+?\s*(компан|клиент)/i, /за\s+\d+\s*секунд/i, /рейтинг/i, /1С|CRM/, /казахск/i, /на\s+паузу|передаёт диалог/i]) {
    assert.ok(!re.test(all), `запрещено: ${re}`);
  }
});
