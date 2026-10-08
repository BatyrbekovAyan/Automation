import json
AP = [{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг'},
      {'name': 'Колодки тормозные передние Toyota 04465-02220', 'price': '18500 тг'},
      {'kind': 'service', 'name': 'Замена масла', 'price': '5000 тг'}]
def c(id, docs, items=AP, expect='no CONFLICT'):
    return {'id': id, 'docs': docs if isinstance(docs, list) else [docs], 'items': items, 'expect': expect}
cases = [
 # --- analogue / cross-reference lines (different item, same OEM number) ---
 c('FP-analog-paren-OEM', 'Фильтр масляный Knecht OC 91 (Toyota 90915-YZZE1) — 1400 тг'),
 c('FP-analog-primenimost-free', 'Фильтр масляный MANN W 68/3, применимость: Toyota 90915-YZZE1 — 1400 тг'),
 c('FP-analog-podhodit-free', 'Фильтр масляный Sakura C 1139, подходит для Toyota 90915-YZZE1 — 1400 тг'),
 c('FP-analog-dlya', 'Фильтр масляный Bosch для 90915-YZZE1 — 1400 тг'),
 c('FP-analog-conv-primenimost', 'product[5]: Наименование: Фильтр масляный MANN W 68/3; Применимость: Toyota 90915-YZZE1; Цена: 1400; '),
 c('FP-analog-conv-primechanie', 'product[6]: Наименование: Фильтр масляный Sakura; Примечание: заменяет 90915-YZZE1; Цена: 1400; '),
 c('FP-analog-conv-OE-first', 'product[2]: OE номер: 90915-YZZE1; Артикул: W 68/3; Наименование: Фильтр масляный MANN; Цена: 1400; '),
 c('FP-analog-conv-nomerOE', 'product[2]: Номер OE: 90915-YZZE1; Бренд: MANN; Наименование: Фильтр масляный; Цена: 1400; '),
 c('FP-analog-conv-kross-in-name', 'product[4]: Наименование: Фильтр масляный Sakura (кросс 90915-YZZE1); Цена: 1400; '),
 c('FP-analog-conv-OEM-col', 'product[4]: Бренд: Sakura; Номер: C1139; OEM: 90915-YZZE1; Наименование: Фильтр масляный; Цена: 1400; '),
 # --- kit / set containing the article ---
 c('FP-kit-single-article', 'Комплект ТО Toyota: фильтр масляный 90915-YZZE1 + масло 5W-30 4л — 15000 тг'),
 c('FP-kit-conv', 'product[9]: Артикул: 90915-YZZE1-KIT; Наименование: Фильтр масляный + прокладка 90915-YZZE1 комплект; Цена: 3900; '),
 # --- pack vs piece (catalog price has no unit) ---
 c('FP-pack-free', 'Свеча зажигания NGK BKR6E-11 — 6000 тг/компл', items=[{'name': 'Свеча зажигания NGK BKR6E-11', 'price': '1500'}]),
 c('FP-pack-conv', 'product[3]: Артикул: BKR6E-11; Наименование: Свеча зажигания NGK; Цена за упаковку: 6000; В упаковке: 4 шт; ',
   items=[{'name': 'Свеча зажигания NGK BKR6E-11', 'price': '1500'}]),
 c('FP-pack-2pcs', 'Фильтр масляный 90915-YZZE1 — 6400 тг за 2 шт'),
 c('FP-pack-paren', 'Фильтр масляный 90915-YZZE1 (упаковка 10 шт) — 32000 тг'),
 # --- currency other than tenge ---
 c('FP-rub-conv', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена, руб.: 450; '),
 c('FP-usd-conv', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена (USD): 7; '),
 c('FP-usd-free', 'Фильтр масляный 90915-YZZE1 — Цена: 7 USD'),
 c('FP-rub-free', 'Фильтр масляный 90915-YZZE1 — цена 450 руб.'),
 # --- thousands abbreviation ---
 c('FP-thousands-conv', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный; Цена, тыс. тг: 3,2; '),
 c('FP-thousands-free', 'Фильтр масляный 90915-YZZE1 — цена: 3,2 тыс. тг'),
 # --- the catalog price IS on the line, but not in a recognised column ---
 c('FP-RRP-conv', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Опт: 2500; РРЦ: 3200; '),
 c('FP-MRC-conv', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена дилер: 2500; МРЦ: 3200; '),
 c('FP-recommended-conv', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Оптовая: 2500; Рекомендуемая: 3200; '),
 c('FP-RRP-free-labelled', 'Фильтр масляный 90915-YZZE1 — опт: 2500, РРЦ: 3200'),
 c('FP-promo-labelled', 'Фильтр масляный 90915-YZZE1. Цена: 3900 тг, по акции 3200 тг'),
 c('FP-promo-labelled2', 'Фильтр масляный 90915-YZZE1 — Цена: 3900 тг (со скидкой 3200 тг)'),
 c('FP-old-new-labelled', 'Фильтр масляный 90915-YZZE1 — Цена: 3900 тг, новая цена: 3200 тг'),
 # --- PDF text: trailing stock / qty columns ---
 c('FP-pdf-trailing-stock', '90915-YZZE1 Фильтр масляный Toyota 3200 150'),
 c('FP-pdf-qty-grouped', '90430-12031 Шайба сливной пробки Toyota 5 500', items=[{'name': 'Шайба сливной пробки Toyota 90430-12031', 'price': '500'}]),
 c('FP-pdf-price-then-qty', '90915-YZZE1 Фильтр масляный Toyota 3 200 12'),
 c('FP-pdf-code-name-price-sum', '90915-YZZE1 Фильтр масляный Toyota 2 3200 6400'),
 # --- used / refurbished ---
 c('FP-used', 'Фильтр масляный 90915-YZZE1 б/у — 1500 тг'),
 # --- VAT ---
 c('FP-vat', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный; Цена с НДС: 3584; ', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг'}]),
]
json.dump(cases, open('cases_fp.json', 'w'), ensure_ascii=False)
