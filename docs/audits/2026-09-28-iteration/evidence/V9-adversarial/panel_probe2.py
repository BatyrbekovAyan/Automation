import json, sys
sys.path.insert(0, '.')
exec(open('panel_probe.py').read().split("ITEMS = [")[0])
ITEMS = [{'kind': 'product', 'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг', 'description': 'Оригинал'}]
consistent = ('Фильтр масляный Toyota 90915-YZZE1 (оригинал) — 3200 тг\n'
              'Фильтр масляный Knecht OC 91 (90915-YZZE1) — 1400 тг')
p, a, f = panel(ITEMS, rag=[consistent], exact=[consistent])
print('rule pushed:', 'РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'])
print(f['ragChunks'])
conv = ('product[1]: Артикул: 90915-YZZE1; Наименование: Фильтр масляный Toyota; Цена: 3200; \n'
        'product[2]: Артикул: C1139; Наименование: Фильтр масляный Sakura; Применяемость: 90915-YZZE1; Цена: 1400; \n'
        'product[3]: Артикул: W68/3; Наименование: Фильтр масляный MANN; Кросс: 90915-YZZE1; Цена: 1450; \n'
        'product[4]: Артикул: SM108; Наименование: Фильтр масляный Knecht; Цена: от 1300; Аналог: 90915-YZZE1;')
p, a, f = panel(ITEMS, rag=[conv])
print('\nconverter, analogue columns properly labelled — rule pushed:', 'РАСХОЖДЕНИЕ ЦЕН' in a['systemPrompt'])
print(f['ragChunks'])
