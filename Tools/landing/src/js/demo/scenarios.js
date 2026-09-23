// Scripted conversations for «Попробуйте сами» — pure data, no DOM.
// Every amount is written with U+00A0 between digit groups and before ₸, and before
// the em dash (so a line never starts with «—»). The strings are shown verbatim.
//   id       — stable key (tabs, Сводка rows)
//   niche    — full tab label; short — its mobile form (must be contained in niche)
//   channel  — 'wa' | 'tg'; times — [question, answer, follow-up] clock labels
//   source   — what the bot checked: kind 'xlsx'|'pdf'|'jpg'|'catalog', file title,
//              and `find` — what it found there (the «нашёл в прайсе: …» line)
//   cards    — 3–4 suggestions, exactly one `best` (the one «Авто» sends)
//   outcome  — the Сводка status the dialog ends in
const deepFreeze = (o) => { Object.values(o).forEach((v) => { if (v && typeof v === 'object') deepFreeze(v); }); return Object.freeze(o); };

export const SCENARIOS = deepFreeze([
  {
    id: 'parts', niche: 'Автозапчасти', short: 'Запчасти', name: 'Айгерим', channel: 'wa',
    times: ['09:41', '09:42', '09:44'],
    source: { kind: 'xlsx', title: 'Прайс_запчасти.xlsx', find: 'масляный фильтр, RAV4 2019, 2.0' },
    question: 'Здравствуйте! Масляный фильтр на RAV4 2019, 2.0 бензин есть?',
    cards: [
      { title: 'Цена и артикул', text: 'Есть оригинал 04152-YZZA1 — 4 900 ₸. Подходит на ваш RAV4 2019, 2.0.', best: true },
      { title: 'Дешевле', text: 'Есть аналог Filtron OE 685/1 — 2 400 ₸, тоже подходит на ваш RAV4.' },
      { title: 'Про замену', text: 'Установку можем сделать у нас — время и цену работы уточню у мастера.' },
      { title: 'Оформить?', text: 'Отложить фильтр на ваше имя? Напишите, когда удобно забрать.' },
    ],
    follow: 'Отлично, беру. Заеду после обеда',
    outcome: { status: 'order', label: 'Заявка собрана' },
  },
  {
    id: 'flowers', niche: 'Цветы', short: 'Цветы', name: 'Дана', channel: 'tg',
    times: ['12:05', '12:06', '12:08'],
    source: { kind: 'pdf', title: 'Букеты_сентябрь.pdf', find: 'розы, 25 шт., доставка' },
    question: 'Добрый день! Сколько стоит букет из 25 роз с доставкой на сегодня?',
    cards: [
      { title: 'Цена и доставка', text: '25 красных роз — 22 500 ₸, доставка по городу — 2 000 ₸. Итого 24 500 ₸.', best: true },
      { title: 'Дешевле', text: 'Можно букет из 15 роз — 13 500 ₸, доставка та же — 2 000 ₸.' },
      { title: 'Про время', text: 'Время доставки уточню у флориста и сразу напишу вам.' },
      { title: 'Оформить?', text: 'Оформляем? Напишите адрес и удобное время — передам флористу.' },
    ],
    follow: 'Давайте 25. Адрес сейчас пришлю',
    outcome: { status: 'dialog', label: 'В диалоге' },
  },
  {
    id: 'kaspi', niche: 'Kaspi-продавец', short: 'Kaspi', name: 'Руслан', channel: 'wa',
    times: ['15:32', '15:33', '15:35'],
    source: { kind: 'catalog', title: 'Каталог товаров', find: 'JBL Tune 520BT, наличие' },
    question: 'Здравствуйте! Наушники JBL Tune 520BT ещё есть? Можно в рассрочку?',
    cards: [
      { title: 'Цена и наличие', text: 'Есть в каталоге — 21 990 ₸, наличие подтвердим при заказе. Рассрочка — через наш магазин на Kaspi.', best: true },
      { title: 'Дешевле', text: 'Есть JBL Tune 510BT — 16 490 ₸, он тоже в каталоге.' },
      { title: 'Про рассрочку', text: 'Условия рассрочки видны в карточке товара — пришлю вам ссылку.' },
      { title: 'Оформить?', text: 'Оформим заказ? Пришлю ссылку на товар в нашем магазине.' },
    ],
    follow: 'Да, скиньте ссылку, спасибо!',
    outcome: { status: 'closed', label: 'Вопрос закрыт' },
  },
  {
    id: 'repair', niche: 'Ремонт телефонов', short: 'Ремонт', name: 'Тимур', channel: 'tg',
    times: ['18:10', '18:11', '18:13'],
    source: { kind: 'jpg', title: 'Фото прайса ремонта', find: 'дисплей, iPhone 13' },
    question: 'Разбил экран на iPhone 13. Сколько стоит замена и сколько ждать?',
    cards: [
      { title: 'Цена и срок', text: 'Замена дисплея на iPhone 13 — от 45 000 ₸, обычно 1–2 часа. Точно — после осмотра.', best: true },
      { title: 'Дешевле', text: 'Есть вариант с неоригинальным дисплеем — от 24 000 ₸. Мастер покажет разницу.' },
      { title: 'Про гарантию', text: 'Условия гарантии уточню у мастера и напишу вам.' },
      { title: 'Записать?', text: 'Записать на диагностику? Напишите удобное время — мастер подтвердит.' },
    ],
    follow: 'Запишите на завтра, в 11:00',
    outcome: { status: 'owner', label: 'Нужен владелец' },
  },
]);
