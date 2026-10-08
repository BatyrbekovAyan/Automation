import json, sys
exec(open('panel_probe.py').read().split("ITEMS = [")[0])
ITEMS = [{'kind': 'product', 'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг', 'description': 'Оригинал'}]
conv = ('product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3200; \n'
        'product[2]: Артикул: OC 91; Бренд: Knecht; Наименование: Фильтр масляный; Применимость: 90915-YZZE1; Цена: 1400; ')
p, a, f = panel(ITEMS, rag=[conv])
print('soft-only marks on a price list that AGREES with the catalog — panel rule pushed:', 'РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'])
print(f['ragChunks'])
i = a['systemPrompt'].find('РАСХОЖДЕНИЕ ЦЕН'); print(a['systemPrompt'][i:i+330])
