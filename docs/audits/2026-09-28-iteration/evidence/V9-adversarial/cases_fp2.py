import json
AP = [{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг'},
      {'name': 'Колодки тормозные передние Toyota 04465-02220', 'price': '18500 тг'}]
def c(id, docs, items=AP, expect='no CONFLICT'):
    return {'id': id, 'docs': docs if isinstance(docs, list) else [docs], 'items': items, 'expect': expect}
cases = [
 # A price list that AGREES with the catalog for the original, plus its usual cross-reference lines
 c('CHUNK-consistent-with-analogues',
   'Фильтры масляные Toyota\n'
   'Фильтр масляный Toyota 90915-YZZE1 (оригинал) — 3200 тг\n'
   'Фильтр масляный Knecht OC 91 (90915-YZZE1) — 1400 тг\n'
   'Фильтр масляный Filtron OP 563 (90915-YZZE1) — 1350 тг\n'
   'Фильтр масляный Mahle OC 217 (90915-YZZE1) — 1600 тг'),
 c('CHUNK-converter-consistent',
   'product[1]: Артикул: 90915-YZZE1; Бренд: Toyota; Наименование: Фильтр масляный; Цена: 3200; \n'
   'product[2]: Артикул: OC 91; Бренд: Knecht; Наименование: Фильтр масляный; Применимость: 90915-YZZE1; Цена: 1400; \n'
   'product[3]: Артикул: W 68/3; Бренд: MANN; Наименование: Фильтр масляный; Применимость: 90915-YZZE1; Цена: 1450; '),
 # 1C nomenclature export: «Код» is an internal 1C code, the article is in «Артикул»
 c('1C-code-and-article', 'product[1]: Код: 00-00012345; Артикул: 90915-YZZE1; Номенклатура: Фильтр масляный Toyota; Цена: 3900; ', expect='CONFLICT (true)'),
 # Bosch numbers with spaces as the line's own article
 c('bosch-spaces', 'Фильтр масляный Bosch 0 986 452 041 (90915-YZZE1) — 1900 тг'),
 # Kaspi: same model, other color/config at another price
 c('kaspi-variant', 'Наушники JBL Tune 510BT синие — 21990 тг', items=[{'name': 'Наушники JBL Tune 510BT', 'price': '18990 тг'}], expect='debatable'),
 c('kaspi-bundle', 'Наушники JBL Tune 510BT + чехол — 21990 тг', items=[{'name': 'Наушники JBL Tune 510BT', 'price': '18990 тг'}]),
 # catalog item name is the generic name + article; price list sells «Масло» with the same supplier code by mistake? (skip)
 # Wholesale: per-box vs per-piece without units in catalog
 c('ws-box', 'Средство для посуды WS-500 — коробка 4 шт — 8400 тг', items=[{'name': 'Средство для посуды WS-500', 'price': '2100'}]),
]
json.dump(cases, open('cases_fp2.json', 'w'), ensure_ascii=False)
