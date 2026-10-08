import json
AP = [{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг'}]
BAT = [{'name': 'Аккумулятор Varta 6CT-60', 'price': '35000 тг'}]
def c(id, docs, items=AP, expect='no CONFLICT'):
    return {'id': id, 'docs': docs if isinstance(docs, list) else [docs], 'items': items, 'expect': expect}
cases = [
 c('tail-engine-cc', 'Фильтр масляный Toyota 90915-YZZE1 для двигателя 1600'),
 c('tail-height-mm', 'Фильтр масляный 90915-YZZE1 высота 100'),
 c('tail-battery-cca', 'Аккумулятор 6CT-60 Varta Blue Dynamic 540', items=BAT),
 c('tail-qty-price-grouped-tg', '90915-YZZE1 Фильтр масляный Toyota 10 900 тг', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '900 тг'}]),
 c('tail-price-2000-is-year', 'Фильтр масляный 90915-YZZE1 2500', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '2000'}], expect='CONFLICT (miss if none)'),
 c('tail-price-equal-2000', 'Фильтр масляный 90915-YZZE1 2000', items=[{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '2500'}], expect='CONFLICT (miss if none)'),
 c('conv-price-cell-with-text-equal', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3200 тг без НДС; ', expect='no mark (price equals catalog)'),
 c('conv-price-cell-paren-equal', 'product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3 200 (с НДС); ', expect='no mark (price equals catalog)'),
 c('free-price-equal-tng-typo', 'Фильтр масляный 90915-YZZE1 — 3200 тнг', expect='no mark'),
]
json.dump(cases, open('cases_tail.json', 'w'), ensure_ascii=False)
