import json
AP = [{'name': 'Фильтр масляный Toyota 90915-YZZE1', 'price': '3200 тг'}]
KS = [{'name': 'Наушники JBL Tune 510BT', 'price': '18990 тг'}]
PR = [{'kind': 'service', 'name': 'Замена экрана iPhone 11 A2221', 'price': '35000 тг'}]
def c(id, docs, items=AP, expect='no CONFLICT'):
    return {'id': id, 'docs': docs if isinstance(docs, list) else [docs], 'items': items, 'expect': expect}
cases = [
 c('acc-gasket', 'Прокладка фильтра масляного 90915-YZZE1 — 300 тг'),
 c('acc-wrench', 'Ключ для масляного фильтра 90915-YZZE1 — 2500 тг'),
 c('acc-earpads', 'Амбушюры для наушников JBL Tune 510BT — 3000 тг', items=KS),
 c('acc-case', 'Чехол для наушников JBL Tune 510BT — 2000 тг', items=KS),
 c('acc-cable', 'Кабель зарядки для наушников JBL Tune 510BT — 1500 тг', items=KS),
 c('svc-part-price', 'Дисплей iPhone 11 A2221 (запчасть) — 22000 тг', items=PR),
 c('svc-other-service', 'Замена стекла экрана iPhone 11 A2221 — 18000 тг', items=PR),
]
json.dump(cases, open('cases_acc.json', 'w'), ensure_ascii=False)
