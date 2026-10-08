import json
WS = [{'name': 'Средство для посуды WS-500', 'price': '2100 тг/шт'}]
def c(id, docs, items=WS, expect='no CONFLICT'):
    return {'id': id, 'docs': [docs], 'items': items, 'expect': expect}
cases = [
 c('unit-za-korobku', 'Средство для посуды WS-500 — 8400 тг за коробку (4 шт)'),
 c('unit-conv-label', 'product[2]: Артикул: WS-500; Наименование: Средство для посуды 5 л; Цена за коробку: 8400; '),
 c('unit-conv-label-both', 'product[2]: Артикул: WS-500; Наименование: Средство для посуды 5 л; Цена за шт: 2100; Цена за коробку: 8400; '),
]
json.dump(cases, open('cases_unit.json', 'w'), ensure_ascii=False)
