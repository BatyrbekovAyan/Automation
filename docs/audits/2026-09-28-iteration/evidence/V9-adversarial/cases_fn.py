import json
AP = [{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг'}]
KS = [{'name': 'Наушники JBL Tune 510BT', 'price': '18990 тг'}]
def c(id, docs, items=AP, expect='CONFLICT'):
    return {'id': id, 'docs': docs if isinstance(docs, list) else [docs], 'items': items, 'expect': expect}
cases = [
 c('docx-table-row', '[Table]\nRow 1: Артикул; Наименование; Цена;\nRow 2: 90915-YZZE1; Фильтр масляный Toyota; 3900;'),
 c('docx-table-row-tg', 'Row 2: 90915-YZZE1; Фильтр масляный Toyota; 3900 тг;'),
 c('rtf-tabs', '90915-YZZE1\tФильтр масляный Toyota\t3900'),
 c('xml-kaspi', 'kaspi catalog offers offer [1] sku: 90915-YZZE1;\nkaspi catalog offers offer [1] model: Фильтр масляный Toyota 90915-YZZE1;\nkaspi catalog offers offer [1] price: 3900;'),
 c('xml-kaspi-model', 'kaspi catalog offers offer [1] model: JBL Tune 510BT;\nkaspi catalog offers offer [1] price: 17990;', items=KS),
 c('xml-commerceml', 'Товары Товар [1] Артикул: 90915-YZZE1;\nТовары Товар [1] Наименование: Фильтр масляный Toyota;\nПредложения Предложение [1] Цены Цена ЦенаЗаЕдиницу: 3900;'),
 c('pdf-article-last', 'Фильтр масляный Toyota 3900 90915-YZZE1'),
 c('pdf-price-next-line', 'Фильтр масляный Toyota 90915-YZZE1\n3 900'),
 c('csv-header-price-tg-paren', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена (тг): 3 900; '),
 c('csv-price-with-currency-symbol', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3 900,00 ₸; '),
 c('csv-price-header-only-tenge', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Тенге: 3900; '),
 c('csv-price-header-sum', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Сумма: 3900; '),
 c('csv-price-header-stoim-abbr', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Стоим.: 3900; '),
 c('excel-double-culture', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3899,99999999; '),
 c('ocr-row', 'product[1]: Название: Фильтр масляный Toyota 90915-YZZE1; Цена: 3 900 ₸;'),
 c('ocr-row-ruble-sign', 'product[1]: Название: Фильтр масляный Toyota 90915-YZZE1; Цена: 3900₸/шт;'),
 c('cyrillic-lookalike-in-doc', 'Фильтр масляный 90915-УZZЕ1 — 3900 тг'),
 c('lowercase-code', 'фильтр масляный 90915-yzze1 — 3900 тг'),
 c('catalog-nbsp-price', 'Фильтр масляный 90915-YZZE1 — 3900 тг', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3 200 ₸'}]),
 c('catalog-price-tenge-word', 'Фильтр масляный 90915-YZZE1 — 3900 тг', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тенге за шт'}]),
 c('catalog-price-prefix-cur', 'Фильтр масляный 90915-YZZE1 — 3900 тг', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '₸3200'}]),
 c('catalog-name-newline', 'Фильтр масляный 90915-YZZE1 — 3900 тг', items=[{'name': 'Фильтр масляный Toyota\n90915-YZZE1', 'price': '3200'}]),
 c('catalog-article-in-desc-only', 'Фильтр масляный 90915-YZZE1 — 3900 тг', items=[{'name': 'Фильтр масляный Toyota', 'price': '3200', 'desc': 'арт. 90915-YZZE1'}]),
 c('numeric-only-article', 'Ремень 6PK1200 — 5200 тг', items=[{'name': 'Ремень 6PK1200', 'price': '4800'}]),
 c('numeric-only-article2', 'Подшипник 6205 — 1500 тг', items=[{'name': 'Подшипник 6205', 'price': '1200'}], expect='limitation'),
]
json.dump(cases, open('cases_fn.json', 'w'), ensure_ascii=False)
