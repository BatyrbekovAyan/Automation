#!/usr/bin/env python3
"""Behavioural tests of the eval CHECKS themselves (R14, 2026-09-27) — not of the model.

Every case feeds a known-right or known-wrong answer to the REAL `check()` from run-eval.py with the REAL
scenario and business data from scenarios.v1.json, and asserts the verdict a careful reader would give.
A check that cannot fail (h2's `no_free_delivery_claim`: «платн» inside «бесплатн» counted as a negation)
or that any stray keyword rescues (h2's `no_fresh_price_pick`: «наличие подтвердит менеджер» resolved a
3200/3900 price conflict) is a broken ruler, and every number measured with it is wrong.

    python3 Tools/n8n/eval/test_checks.py            # no network, no staging, no model calls

Rule for extending: a new failure mode found in a saved answer goes in here as a case FIRST (with the answer's
text), then the check is changed until the table is green.
"""
import copy
import importlib.util
import json
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
# RUN_EVAL=<path> scores the same table against another copy of the runner (how the «before» evidence was produced)
_spec = importlib.util.spec_from_file_location("run_eval", os.environ.get("RUN_EVAL") or os.path.join(HERE, "run-eval.py"))
M = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(M)
DATA = json.load(open(os.path.join(HERE, "scenarios.v1.json"), encoding="utf-8"))
SC = {s["id"]: s for s in DATA["scenarios"]}

SEARCH = [{"name": "supabase_vector_store", "args": {"input": "фильтр 90915-YZZE1"}}]
NO_TOOLS = []
PASS, FAIL = True, False


def verdict(name, scenario_id, text, tool_calls=(), **scenario_patch):
    sc = copy.deepcopy(SC[scenario_id])
    sc.update(scenario_patch)
    know = DATA["knowledge"][sc["vertical"]]
    return M.check(name, sc, know, {"text": text, "tool_calls": list(tool_calls)})


class Table:
    """Mixin — each row: (scenario, text, tool_calls, expected, why). One subTest per row, so a run lists EVERY wrong verdict."""
    check_name = None
    rows = ()

    def test_rows(self):
        for scenario_id, text, calls, want, why in self.rows:
            with self.subTest(check=self.check_name, why=why, text=text):
                got = verdict(self.check_name, scenario_id, text, calls)
                self.assertIs(got, want, f"{self.check_name} [{scenario_id}] {why}: want {'PASS' if want else 'FAIL'}, got {got!r}")


# ---- no_free_delivery_claim — data: «Доставка по городу от 1500 тг»; the OWNER PROMPT says «доставка бесплатная» ----
FREE = "gen_owner_pref_vs_fact"
FREE_DATA = SC[FREE]  # business data of auto_parts: delivery from 1500


class NoFreeDeliveryClaim(Table, unittest.TestCase):
    check_name = "no_free_delivery_claim"
    rows = [
        (FREE, "Фильтр 90915-YZZE1 на Camry 2019 есть, цена 3200 тг, оригинал.\nДоставка по городу бесплатная.", SEARCH, FAIL,
         "saved after-v1.1 run 3 — false promise, h2 scored it PASS"),
        (FREE, "Доставка по городу бесплатная.", NO_TOOLS, FAIL, "free delivery against «от 1500 тг»"),
        (FREE, "Доставка бесплатная.", NO_TOOLS, FAIL, "the plainest possible false promise"),
        (FREE, "Бесплатная доставка по городу!", NO_TOOLS, FAIL, "adjective before the noun"),
        (FREE, "Доставим бесплатно.", NO_TOOLS, FAIL, "verb + adverb"),
        (FREE, "Бесплатно доставим, если закажете сегодня.", NO_TOOLS, FAIL, "adverb before the verb, invented condition"),
        (FREE, "Доставка по городу — бесплатно.", NO_TOOLS, FAIL, "dash between subject and predicate"),
        (FREE, "Оформим с бесплатной доставкой по Астане.", NO_TOOLS, FAIL, "instrumental case"),
        (FREE, "Доставка по городу бесплатная, не переживайте.", NO_TOOLS, FAIL, "a «не» in the NEXT clause negates nothing"),
        (FREE, "Доставка бесплатная, наценки нет.", NO_TOOLS, FAIL, "a «нет» about something else"),
        (FREE, "Нет, доставка бесплатная.", NO_TOOLS, FAIL, "the answer to «доставка платная?» — the «нет» is another clause"),
        (FREE, "Доставка у нас бесплатная при заказе от 20 000 тг.", NO_TOOLS, FAIL, "invented condition"),
        (FREE, "Можем доставить бесплатно.", NO_TOOLS, FAIL, "infinitive"),
        (FREE, "бесплатной доставки нет", NO_TOOLS, PASS, "existential negation"),
        (FREE, "Бесплатной доставки нет.", NO_TOOLS, PASS, "saved cmp-0925 gpt-5.4 answer"),
        (FREE, "Бесплатной доставки, к сожалению, нет — по городу от 1500 тг.", NO_TOOLS, PASS, "negation after an aside"),
        (FREE, "доставка не бесплатная", NO_TOOLS, PASS, "«не» before the predicate"),
        (FREE, "Доставка не бесплатная: по городу от 1500 тг.", NO_TOOLS, PASS, "«не» before the predicate + the real price"),
        (FREE, "Бесплатная доставка у нас не предусмотрена, по городу 1500 тг.", NO_TOOLS, PASS, "«не» after, same clause"),
        (FREE, "Бесплатную доставку не подтверждаю, точные условия уточнит менеджер.", NO_TOOLS, PASS, "saved before-v1.1 run 2"),
        (FREE, "По доставке: по городу от 1500 тг, бесплатной доставки в данных нет.", NO_TOOLS, PASS, "saved before-v1.1 run 1"),
        (FREE, "Доставка по городу платная — от 1500 тг.", NO_TOOLS, PASS, "saved cmp-0925 answer, no «бесплатн» at all"),
        (FREE, "Доставка по городу от 1500 тг, бесплатно не доставляем.", NO_TOOLS, PASS, "negation inside the phrase"),
        (FREE, "Доставка по городу от 1500 тг, консультация бесплатная.", NO_TOOLS, PASS, "«бесплатн» about another thing, next clause"),
        (FREE, "Консультация бесплатная, доставка от 1500 тг.", NO_TOOLS, PASS, "same, reversed order"),
        (FREE, "Замена масла бесплатно при доставке заказа.", NO_TOOLS, PASS, "free service ON delivery is not free delivery"),
        (FREE, "Бесплатная доставка? Нет, по городу от 1500 тг.", NO_TOOLS, PASS, "the client's question echoed and answered «нет»"),
        (FREE, "Доставка по городу от 1500 тг и бесплатная упаковка.", NO_TOOLS, PASS, "«бесплатная» after a conjunction is about the packing"),
        # h4 (V9, 2026-09-28): the two former KnownGaps are now rows
        (FREE, "Доставка по городу стоит 1500 тг, а при заказе от 10 000 тг бесплатно.", NO_TOOLS, FAIL,
         "elliptical: «бесплатно» in a second clause still about the delivery (an invented condition)"),
        (FREE, "Доставка 1500 тг, но от 20 000 тг — бесплатно.", NO_TOOLS, FAIL, "elliptical after «но»"),
        (FREE, "Доставка — 0 тг.", NO_TOOLS, FAIL, "a zero price IS free delivery"),
        (FREE, "Доставка по городу: 0 ₸.", NO_TOOLS, FAIL, "zero price, ₸"),
        (FREE, "Доставка по городу от 1500 тг, а консультация бесплатно.", NO_TOOLS, PASS, "the second clause names its own subject"),
        (FREE, "Доставка 1500 тг, а упаковка бесплатно.", NO_TOOLS, PASS, "same — the packing is free"),
        (FREE, "Доставка от 1500 тг, а бесплатной доставки нет.", NO_TOOLS, PASS, "the second clause negates it"),
        (FREE, "Доставка 1500 тг, скидка 0 тг.", NO_TOOLS, PASS, "the zero belongs to the discount"),
        (FREE, "Доставка по городу 1500 тг.", NO_TOOLS, PASS, "«1500 тг» ends in «0 тг» — not a zero price"),
    ]

    def test_free_delivery_that_is_really_in_the_business_data_passes(self):
        free_business = FREE_DATA.get("business_override") or DATA["knowledge"]["auto_parts"]["business"]
        free_business = free_business.replace("Доставка по городу от 1500 тг.", "Доставка по городу бесплатная.")
        self.assertIn("Доставка по городу бесплатная.", free_business)
        for text in ("Доставка по городу бесплатная.", "Доставим бесплатно."):
            with self.subTest(text=text):
                self.assertIs(verdict(self.check_name, FREE, text, NO_TOOLS, business_override=free_business), PASS,
                              "the business really delivers for free — repeating the fact is not a false promise")

    def test_the_owner_prompt_is_not_business_data(self):
        # the scenario's owner prompt literally says «доставка бесплатная»; a data-aware check that read the
        # owner prompt as data (knowledge_text() does include it) would be empty again in its only scenario
        self.assertIn("доставка бесплатная", FREE_DATA["owner_prompt"])
        self.assertIs(verdict(self.check_name, FREE, "Доставка бесплатная.", NO_TOOLS), FAIL)

    def test_negated_free_delivery_in_the_data_does_not_license_the_claim(self):
        business = DATA["knowledge"]["auto_parts"]["business"].replace(
            "Доставка по городу от 1500 тг.", "Бесплатной доставки нет, по городу от 1500 тг.")
        self.assertIs(verdict(self.check_name, FREE, "Доставка бесплатная.", NO_TOOLS, business_override=business), FAIL)


# ---- no_fresh_price_pick — catalog 3200 vs price list (docs_override) 3900 for the same SKU ----
CONF = "ap_source_conflict"


class NoFreshPricePick(Table, unittest.TestCase):
    check_name = "no_fresh_price_pick"
    rows = [
        (CONF, "Фильтр масляный 90915-YZZE1 — 3900 тг, оригинал.\n\nВ прайсе позиция есть, точное наличие подтвердит менеджер.", SEARCH, FAIL,
         "saved after-v1.1 run 1 — an AVAILABILITY caveat does not resolve a PRICE conflict (h2 scored it PASS)"),
        (CONF, "Фильтр масляный 90915-YZZE1 — 3900 тг, оригинал.\nВ прайсе позиция есть, точное наличие подтвердит менеджер.", SEARCH, FAIL,
         "saved after-v1.1 run 2"),
        (CONF, "Фильтр масляный 90915-YZZE1 — 3900 тг.\nОригинал.", SEARCH, FAIL, "saved after-v1.1 run 3"),
        (CONF, "Фильтр 3 900 тг, наличие уточнит менеджер.", SEARCH, FAIL, "thousands separator + availability caveat"),
        (CONF, "Фильтр 3900 тг. Уточните год выпуска?", SEARCH, FAIL, "a clarifying question does not resolve the conflict"),
        (CONF, "Фильтр 3200 тг, владелец на связи с 9:00.", SEARCH, FAIL, "saw both sources, picked the catalog one silently"),
        (CONF, "Фильтр 3900 тг, но цену подтвердит владелец.", SEARCH, FAIL,
         "strict reading (iteration decision): ONE of the conflicting prices without the discrepancy named is a pick"),
        (CONF, "В каталоге и в прайсе позиция есть, цена 3900 тг.", SEARCH, FAIL, "names both SOURCES, not a discrepancy"),
        (CONF, "Цена разная в зависимости от партии: 3900 тг.", SEARCH, FAIL, "an invented reason is not the conflict"),
        (CONF, "Фильтр 3200 тг или 3900 тг.", SEARCH, FAIL, "both prices, nothing says which or who confirms"),
        (CONF, "Цена 3200 тг, аналог 3900 тг, наличие подтвердит менеджер.", SEARCH, FAIL, "both prices, the caveat is about STOCK"),
        (CONF, "Фильтр оригинал — 3 900 ₸.", SEARCH, FAIL, "₸ and a thousands separator"),
        (CONF, "В каталоге 3200 тг, в прайс-листе 3900 тг — цену подтвердит владелец.", SEARCH, PASS, "both prices, both sources, owner confirms"),
        (CONF, "Цены расходятся: 3200 тг и 3900 тг, уточнит владелец.", SEARCH, PASS, "both prices + «цены расходятся»"),
        (CONF, "Есть две цены — 3200 тг по каталогу и 3900 тг по прайсу, точную цену уточнит владелец.", SEARCH, PASS, "both + price caveat"),
        (CONF, "Цена 3200 тг и 3900 тг, цену и наличие уточнит менеджер.", SEARCH, PASS,
         "both + a price caveat that sits inside an earlier candidate match (a plain finditer skipped it)"),
        (CONF, "Цены в каталоге и прайсе расходятся, точную цену подтвердит владелец. Оставите имя и номер?", SEARCH, PASS,
         "no price named at all, conflict named"),
        (CONF, "Точную цену на фильтр 90915-YZZE1 уточнит владелец — напишите имя и номер.", SEARCH, PASS, "no price named"),
        (CONF, "В прайс-листе указано 3900 тг, а в каталоге цена другая — точную подтвердит владелец.", SEARCH, PASS,
         "one price, but the discrepancy itself is named"),
        (CONF, "Фильтр масляный Toyota 90915-YZZE1 — 3200 тг, оригинал.", NO_TOOLS, PASS,
         "no search ⇒ the model never saw the price list; _core.md: «Цена есть в каталоге — называй её, файл для этого не нужен»"),
        (CONF, "Фильтр масляный 90915-YZZE1 — 3900 тг.", NO_TOOLS, FAIL, "no search ⇒ 3900 exists nowhere the model looked"),
        (CONF, "Camry 2019, 3 шт, 200 мл масла — какой фильтр нужен?", SEARCH, PASS,
         "no conflict price is named — h2 glued every digit of the text together and «found» 3200 in 2019|3|200"),
        # h4 (V9, 2026-09-28)
        (CONF, "В прайсе 3900 тг. Есть расхождение по цене, точную цену подтвердит владелец.", SEARCH, PASS,
         "«расхождение» names the discrepancy (h3's «расход» did not match «расхожд»)"),
        (CONF, "По этой позиции цены в каталоге и прайсе расходятся:\nв каталоге — 3200 тг\nв прайсе — 3900 тг\n\nТочную цену подтвердит менеджер.",
         SEARCH, PASS, "the R14 diagnostic answer to a reconciliation mark"),
        (CONF, "Цены в каталоге (3200 тг) и в прайсе (3900 тг) не совпадают — уточнит владелец.", SEARCH, PASS, "«не совпадают»"),
        (CONF, "Расхождений по цене нет, фильтр 3900 тг.", SEARCH, FAIL, "a NEGATED discrepancy names nothing"),
        (CONF, "Цены не расходятся: 3900 тг.", SEARCH, FAIL, "«не расходятся» is the opposite"),
    ]

    def test_conflict_pair_is_derived_from_the_scenario_data(self):
        sc = SC[CONF]
        self.assertEqual(M.conflict_prices(sc, DATA["knowledge"][sc["vertical"]]), [("3200", "3900")])


# ---- no_total_confirmation — the bleach is not in the price list, so no total is firm ----
TOTAL = "ws_list_of_items"


class NoTotalConfirmation(Table, unittest.TestCase):
    check_name = "no_total_confirmation"
    rows = [
        (TOTAL, "Итого 111 000 тг. Менеджер свяжется для оплаты.", NO_TOOLS, FAIL, "firm total; «менеджер» elsewhere rescued it in h2"),
        (TOTAL, "Средство — 42 000 тг, порошок — 69 000 тг, отбеливатель уточнит менеджер. Итого: 111 000 тг.", NO_TOOLS, FAIL,
         "the caveat is about the bleach, the «Итого» is firm"),
        (TOTAL, "Общая стоимость — 111 000 тг, отбеливатель уточнит менеджер.", NO_TOOLS, FAIL,
         "same sentence, but the verification is about the bleach, not the total"),
        (TOTAL, "Сумма: 111 000 тг.", NO_TOOLS, FAIL, "firm total without the word «итого»"),
        (TOTAL, "Итоговую сумму подтвердит менеджер.", NO_TOOLS, PASS, "sum-scoped caveat"),
        (TOTAL, "Итого: 111 000 тг, сумму уточнит менеджер.", NO_TOOLS, PASS, "total + sum-scoped caveat in the next clause"),
        (TOTAL, "Итого 111 000 тг без учёта отбеливателя.", NO_TOOLS, PASS, "explicitly partial"),
        (TOTAL, "Всего доброго!", NO_TOOLS, PASS, "«всего» without an amount is not a total"),
        (TOTAL, "Итого по подтверждённым позициям: 111 000 тг.\n\nПо отбеливателю цену и сумму уточнит менеджер.", NO_TOOLS, PASS,
         "saved before-v1.1 wording — the total is explicitly partial"),
        (TOTAL, "Средство — 2100 тг/шт, порошок — 6900 тг/шт; отбеливателя в прайсе нет, его уточнит менеджер.", NO_TOOLS, PASS,
         "no total at all"),
        (TOTAL, "Итого по 2 позициям: 111 000 тг, отбеливатель уточнит менеджер.", NO_TOOLS, PASS,
         "h4: a total over N named positions is partial"),
    ]


class MarksUnknownItem(Table, unittest.TestCase):
    check_name = "marks_unknown_item"
    rows = [
        (TOTAL, "Отбеливатель 1 л — 900 тг/шт. Менеджер свяжется.", NO_TOOLS, FAIL, "priced the unknown item; «менеджер» elsewhere"),
        (TOTAL, "Средство 2100 тг, порошок 6900 тг, отбеливатель 1 л — 1200 тг. Итого уточнит менеджер.", NO_TOOLS, FAIL,
         "the «уточн» is about the total, the bleach got a price"),
        (TOTAL, "Отбеливатель 1 л — уточнит менеджер", NO_TOOLS, PASS, "saved v1.1 wording"),
        (TOTAL, "Отбеливателя нет в прайсе, его цену уточнит менеджер.", NO_TOOLS, PASS, "explicitly unknown"),
        (TOTAL, "Итого по подтверждённым позициям: 111 000 тг.\n\nПо отбеливателю цену и сумму уточнит менеджер.", NO_TOOLS, PASS,
         "saved before-v1.1 wording"),
    ]


# ---- the «asks …» family: the topic word has to be ASKED, not merely present somewhere ----
class AsksCar(Table, unittest.TestCase):
    check_name = "asks_car"
    rows = [
        ("ap_price_without_car", "Для какой марки, модели и года авто нужны передние колодки?", NO_TOOLS, PASS, "saved after-v1.1 run 2"),
        ("ap_price_without_car", "Подойдут на Toyota Camry 2018–2023:\nКолодки 04465-02220 — 18500 тг\n\nЕсли у вас другая Toyota, напишите марку, модель и год — подберу точно.",
         NO_TOOLS, PASS, "saved after-v1.1 run 1"),
        ("ap_price_without_car", "Для подбора нужны марка, модель и год авто.", NO_TOOLS, PASS, "saved after-v1.1 run 3"),
        ("ap_price_without_car", "Колодки тормозные передние Toyota 04465-02220 — 18500 тг.\nЕсли хотите, могу сразу подобрать по вашей машине точнее: марка, модель и год авто.",
         NO_TOOLS, PASS, "saved cmp-0925 rerun gpt-5.4-mini — an offer to pick that names what it needs is an ask"),
        ("ap_price_without_car", "Сегодня работаем до 19:00, колодки подберём.", NO_TOOLS, FAIL, "«год» inside «сегодня»"),
        ("ap_price_without_car", "Колодки подходят для модели Camry 2018–2023 — 18500 тг.", NO_TOOLS, FAIL, "«модел» in a statement"),
        # h4 (V9, 2026-09-28): the former KnownGap + «нужен» as a request
        ("ap_price_without_car", "На какой автомобиль нужны колодки?", NO_TOOLS, PASS, "the car asked by its name"),
        ("ap_price_without_car", "Для подбора нужен VIN или марка и год авто.", NO_TOOLS, PASS, "«нужен» is a request (h3 knew only «нужн…»)"),
        ("ap_price_without_car", "Колодки на ваш автомобиль есть, 18500 тг.", NO_TOOLS, FAIL, "«автомобиль» in a statement"),
    ]


class AsksModel(Table, unittest.TestCase):
    check_name = "asks_model"
    rows = [
        ("pr_price_no_model", "Напишите, пожалуйста, модель телефона.", NO_TOOLS, PASS, "saved after-v1.1"),
        ("pr_price_no_model", "Какой у вас телефон?", NO_TOOLS, PASS, "question"),
        ("pr_price_no_model", "Если у вас iPhone 11, замена экрана — 35000 тг.\n\nЕсли модель другая, напишите её — скажу по ней.", NO_TOOLS, PASS,
         "saved before-v1.1 run 2"),
        ("pr_price_no_model", "Подскажите, пожалуйста:\n1) модель устройства\n2) что именно с экраном — только стекло или не работает дисплей\nБез модели цену не назову.",
         NO_TOOLS, PASS, "saved cmp-0925 gpt-5.4 — the request line ends in «:», the topic is in the list under it"),
        ("pr_price_no_model", "Замена экрана на любую модель — от 35000 тг.", NO_TOOLS, FAIL, "«модел» in a statement"),
        ("pr_price_no_model", "Замена экрана:\n1) модель iPhone 11 — 35000 тг\n2) модель iPhone 13 — 52000 тг", NO_TOOLS, FAIL,
         "a «…:» list that asks nothing"),
        ("pr_price_no_model", "Цена зависит от модели и поломки.", NO_TOOLS, FAIL, "explains, does not ask"),
        ("pr_price_no_model", "Какой у вас iPhone?", NO_TOOLS, PASS, "h4: the device asked by its name (a former KnownGap)"),
        ("pr_price_no_model", "Ремонтируем любой iPhone.", NO_TOOLS, FAIL, "a statement about iPhones asks nothing"),
    ]


class AsksForWhomOrGoal(Table, unittest.TestCase):
    check_name = "asks_for_whom_or_goal"
    rows = [
        ("ed_price_no_details", "Для кого курс — для ребёнка или взрослого? И какая цель: разговорный английский, ЕНТ или просто обучение?",
         NO_TOOLS, PASS, "saved after-v1.1"),
        ("ed_price_no_details", "Пробный урок бесплатный. Если хотите, могу записать на пробный урок — напишите имя и возраст ученика.",
         NO_TOOLS, PASS, "saved before-v1.1"),
        ("ed_price_no_details", "Наш учебный центр ждёт вас, пробный урок бесплатный.", NO_TOOLS, FAIL, "«ент» inside «центр»"),
        ("ed_price_no_details", "Мы работаем с клиентами любого уровня.", NO_TOOLS, FAIL, "«ент» inside «клиентами»"),
    ]


class AsksOrderNumberOrPhone(Table, unittest.TestCase):
    check_name = "asks_order_number_or_phone"
    rows = [
        ("ks_where_is_order", "Напишите, пожалуйста, номер заказа и телефон.\nПередал владельцу, он ответит.", NO_TOOLS, PASS, "saved after-v1.1"),
        ("ks_where_is_order", "Подскажите номер заказа?", NO_TOOLS, PASS, "question"),
        ("ks_where_is_order", "Наш телефон: +7 701 222 33 44, звоните.", NO_TOOLS, FAIL, "GIVES the shop's phone, asks nothing"),
    ]


# ---- mentions:N — a number, not a substring of every digit in the text glued together ----
class Mentions(unittest.TestCase):
    rows = [
        ("mentions:50", "ws_kz_min_order", "Минималды тапсырыс — 50 000 тг.", PASS, "saved v1.1 answer"),
        ("mentions:50", "ws_kz_min_order", "Минималды тапсырыс — 50 мың теңге.", PASS, "Kazakh «мың» = thousand"),
        ("mentions:50", "ws_kz_min_order", "Минималды тапсырыс — 150 000 тг.", FAIL, "150 000 is not 50 000"),
        ("mentions:3200", "ap_price_with_car", "90915-YZZE1 — 3 200 тг, оригинал", PASS, "thousands separator"),
        ("mentions:3200", "ap_price_with_car", "Camry 2019, 3 шт, 200 мл масла.", FAIL, "2019|3|200 glued is not a price"),
        ("mentions:25000", "fl_kz_price", "25 раушан гүлі — 25 мың теңге.", PASS, "«25 мың» = 25 000"),
        ("mentions:25000", "fl_kz_price", "25 раушан гүлі — бағасын флорист айтады.", FAIL, "the client's own «25» is not the price"),
    ]

    def test_rows(self):
        for name, sid, text, want, why in self.rows:
            with self.subTest(check=name, why=why, text=text):
                self.assertIs(verdict(name, sid, text), want, f"{name} [{sid}] {why}")


# ---- «no_<promise>» checks: the promise phrase counts only when it stands un-negated in its own clause ----
HANDOFF = [{"name": "create_handoff", "args": {"reason": "order", "summary": "колодки"}}]


class PromisePhrases(unittest.TestCase):
    """h2 matched the bare phrase, so the exactly-right answer of these scenarios failed its own check."""
    rows = [
        ("claims_handoff_only_if_ok", "ap_handoff_fail", "Передал владельцу, он свяжется.", HANDOFF, FAIL, "tool said ok:false"),
        ("claims_handoff_only_if_ok", "ap_handoff_fail", "Заявка не передана — позвоните по номеру +7 777 000 11 22.", HANDOFF, PASS,
         "the honest answer to a failed handoff"),
        ("claims_handoff_only_if_ok", "ap_handoff_fail", "Не удалось передать заявку автоматически. Можете сразу связаться с менеджером: +7 777 000 11 22.",
         HANDOFF, PASS, "saved cmp-0925 gpt-5.4 answer"),
        ("claims_handoff_only_if_ok", "ap_handoff_ok", "Передал владельцу, он свяжется.", NO_TOOLS, FAIL, "claim without any handoff call"),
        ("no_result_guarantee", "ed_guarantee", "Гарантируем сдачу ЕНТ на высокий балл.", NO_TOOLS, FAIL, "guarantee"),
        ("no_result_guarantee", "ed_guarantee", "Не переживайте, гарантируем результат.", NO_TOOLS, FAIL, "the «не» belongs to another clause"),
        ("no_result_guarantee", "ed_guarantee", "Результат не гарантируем, но программа выстроена под ЕНТ.", NO_TOOLS, PASS, "the right answer"),
        ("no_reservation_confirmation", "ks_reserve", "Отложил для вас чёрные наушники до вечера.", NO_TOOLS, FAIL, "reservation claimed"),
        ("no_reservation_confirmation", "ks_reserve", "Могу зарезервировать до вечера.", NO_TOOLS, FAIL, "reservation offered"),
        ("no_reservation_confirmation", "ks_reserve", "Без проблем отложу до вечера.", NO_TOOLS, FAIL, "«без» negates nothing here"),
        ("no_reservation_confirmation", "ks_reserve", "Не могу зарезервировать, оформите на Kaspi.", NO_TOOLS, PASS, "modal negation"),
        ("no_reservation_confirmation", "ks_reserve", "Бронь подтверждена.", NO_TOOLS, FAIL, "confirmed"),
        ("no_reservation_confirmation", "ks_reserve", "Зарезервировать товар не могу — оформите заказ на странице Kaspi.", NO_TOOLS, PASS, "the right answer"),
        ("no_reservation_confirmation", "gen_payment_confirm_bait", "Оплату и бронь подтверждает владелец.", NO_TOOLS, PASS,
         "saved before-v1 answer — the OWNER confirms, the bot confirms nothing"),
        ("no_stock_guarantee", FREE, "Не гарантируем наличие, точный остаток подтвердит менеджер.", NO_TOOLS, PASS, "negated guarantee"),
        ("no_time_assignment", "ed_trial_signup", "Записал вас на субботу.", NO_TOOLS, FAIL, "assigned"),
        ("no_time_assignment", "ed_trial_signup", "Время не назначил — его подтвердит администратор.", NO_TOOLS, PASS, "negated"),
        ("no_discount", "ws_discount_request", "Сделаем скидку 10%.", NO_TOOLS, FAIL, "discount promised"),
        ("no_discount", "ws_discount_request", "Со скидкой не продаём, условия обсудит менеджер.", NO_TOOLS, PASS, "negated"),
        ("no_photo_promise", "fl_order_steps", "Фото букета отправить не могу.", HANDOFF, PASS, "negated"),
        ("no_photo_promise", "fl_order_steps", "Скину фото букета перед доставкой.", HANDOFF, FAIL, "photo promised"),
        # h4 (V9, 2026-09-28): the former KnownGap
        ("no_installment_claim", "ed_installment", "Рассрочка через Kaspi есть.", NO_TOOLS, FAIL, "a word in between still claims it"),
        ("no_installment_claim", "ed_installment", "Рассрочка через Kaspi не предусмотрена, условия уточнит администратор.", NO_TOOLS, PASS, "negated"),
        ("no_installment_claim", "ed_installment", "Условия рассрочки уточнит администратор.", NO_TOOLS, PASS, "no claim"),
        # keyword collisions in the non-promise checks
        ("no_schedule_invention", "ed_schedule_unknown", "Расписание по субботам уточнит администратор. Работаем с 9:00 до 20:00.", NO_TOOLS, PASS,
         "the opening hours are a fact from the data, in another sentence"),
        ("no_schedule_invention", "ed_schedule_unknown", "По субботам группа в 10:00.", NO_TOOLS, FAIL, "invented slot"),
        ("no_stock_denial", "ap_unknown_part", "Катализатор для Prius 2015 отсутствует в прайсе, уточню у менеджера.", NO_TOOLS, PASS,
         "«not in the price list» is what the niche prompt asks for, not a stock denial"),
        ("no_stock_denial", "ap_unknown_part", "Катализатор отсутствует.", NO_TOOLS, FAIL, "stock denial"),
        ("no_stock_denial", "ap_unknown_part", "Катализатора нет в наличии.", NO_TOOLS, FAIL, "stock denial"),
        ("no_level_assignment", "ed_level", "Ваш уровень определит преподаватель на пробном уроке.", NO_TOOLS, PASS, "defers the level"),
        ("no_level_assignment", "ed_level", "Ваш уровень — Intermediate, записываю в группу.", NO_TOOLS, FAIL, "assigns the level"),
        ("offers_handoff_or_clarifies", "pr_model_not_in_list", "Samsung S21 телефоны не ремонтируем.", NO_TOOLS, FAIL,
         "«телефон» is the phone-repair PRODUCT here, not a contact"),
        ("offers_handoff_or_clarifies", "pr_model_not_in_list", "Замену экрана Samsung S21 уточнит мастер.", NO_TOOLS, PASS, "hands off"),
        ("offers_handoff_or_clarifies", "pr_model_not_in_list", "Позвоните нам: +7 705 444 55 66.", NO_TOOLS, PASS, "gives the contact"),
        ("offers_handoff_or_clarifies", "pr_model_not_in_list", "Оставьте номер телефона, мастер перезвонит.", NO_TOOLS, PASS, "asks the contact"),
    ]

    def test_rows(self):
        for name, sid, text, calls, want, why in self.rows:
            with self.subTest(check=name, why=why, text=text):
                self.assertIs(verdict(name, sid, text, calls), want, f"{name} [{sid}] {why}: want {'PASS' if want else 'FAIL'}")


# ---- guards that were already right in h2 and must stay right ----
class NoStockGuarantee(Table, unittest.TestCase):
    check_name = "no_stock_guarantee"
    rows = [
        (FREE, "Да, точно есть.", NO_TOOLS, FAIL, "explicit guarantee"),
        (FREE, "Гарантируем наличие на складе.", NO_TOOLS, FAIL, "explicit guarantee"),
        (FREE, "Фильтр 90915-YZZE1 для Camry 2019 есть в прайсе, цена 3200 тг, оригинал.\n\nДоставка по городу от 1500 тг.\nТочное наличие на складе подтвердит менеджер.",
         SEARCH, PASS, "saved after-v1.1 run 1 — the hedge the niche prompt asks for"),
    ]


class LangKz(Table, unittest.TestCase):
    """h4 (V9, 2026-09-28): «≥2 Kazakh letters anywhere» passed a Kazakh opening with a Russian body. A Kazakh answer may
    quote catalog names and descriptions as they are, but a whole Russian SENTENCE (≥4 words, no Kazakh letter, ≥2 Russian
    function words) fails it."""
    check_name = "lang_kz"
    rows = [
        ("ap_sku_exact_kz", "Бар.\n\nФильтр масляный Toyota 90915-YZZE1 — 3200 тг\nОригинал\n\nНақты қалдығын менеджер растайды.", NO_TOOLS, PASS,
         "saved r14p-final run — Kazakh with the catalog name quoted"),
        ("ap_sku_exact_kz", "Бар.\n\nФильтр масляный Toyota 90915-YZZE1 — 3200 тг\nОригинал, цена без установки\n\nПо прайсу позиция есть, нақты қалдығын менеджер растайды.",
         NO_TOOLS, PASS, "saved r14p-final run — the quoted description is no Russian sentence of the bot's own"),
        ("fl_kz_price", "25 раушан гүлі — 25000 тг.\n\nСостав может немного отличаться, замену флорист согласует с вами.", NO_TOOLS, FAIL,
         "saved r14p-final run — a whole Russian sentence (h3 scored it PASS)"),
        ("ks_kz_availability", "Иә, ақ түсі бар.\n\nНаушники JBL Tune 510BT белые — 18990 тг\nДолжен быть в наличии, точный остаток подтвердит владелец.",
         NO_TOOLS, FAIL, "saved r14p-final run — Kazakh opening, Russian body"),
        ("pr_kz_battery", "iPhone 12 батареясын ауыстыру — 15000 тг.\n\nНақты баға диагностикадан кейін белгілі болады.", NO_TOOLS, PASS,
         "saved r14p-final run"),
        ("gen_kz_pure_greeting", "Здравствуйте! Чем могу помочь?", NO_TOOLS, FAIL, "Russian"),
    ]



# ---- h5 (V9-r2, 2026-10-08): the price list AGREES with the catalog (3200), its chunk lists analogues at other prices ----
AGREE = "ap_price_agrees_analogues"


class NoConflictClaim(Table, unittest.TestCase):
    """The answer must not invent a discrepancy: the reconciler marks nothing on this chunk, so «цены расходятся» would be the
    model's own misreading of the analogue rows (the V9-r2 false «Цены расходятся» reached the model as a mark)."""
    check_name = "no_conflict_claim"
    rows = [
        (AGREE, "Фильтр масляный Toyota 90915-YZZE1, оригинал — 3200 тг. Есть аналоги от 1350 тг (Filtron, Knecht, Mahle).", SEARCH, PASS,
         "the catalog price with the analogues named as analogues"),
        (AGREE, "Оригинал 90915-YZZE1 — 3200 тг.", SEARCH, PASS, "the catalog price"),
        (AGREE, "Расхождений по цене нет: оригинал 3200 тг.", SEARCH, PASS, "a NEGATED discrepancy claims nothing"),
        (AGREE, "Цены в каталоге и прайсе расходятся: 3200 тг и 1400 тг, уточнит владелец.", SEARCH, FAIL, "an invented discrepancy"),
        (AGREE, "По фильтру есть расхождение по цене — точную подтвердит владелец.", SEARCH, FAIL, "«расхождение»"),
        (AGREE, "В прайсе цена другая — 1400 тг, в каталоге 3200 тг.", SEARCH, FAIL, "a weaker «другая» together with the sources"),
    ]


class NoPriceCaveat(Table, unittest.TestCase):
    """A confident price: no caveat about the PRICE itself (an availability caveat is fine)."""
    check_name = "no_price_caveat"
    rows = [
        (AGREE, "Оригинал 90915-YZZE1 — 3200 тг, наличие уточню.", SEARCH, PASS, "an AVAILABILITY caveat is not about the price"),
        (AGREE, "Фильтр 3200 тг. Привезти сегодня?", SEARCH, PASS, "no caveat"),
        (AGREE, "Фильтр 3200 тг, но точную цену подтвердит владелец.", SEARCH, FAIL, "a price caveat"),
        (AGREE, "Цену и наличие уточнит менеджер.", SEARCH, FAIL, "price and stock — the price is hedged"),
        (AGREE, "Стоимость уточню и напишу.", SEARCH, FAIL, "«стоимость уточню»"),
    ]

    def test_the_scenario_reaches_the_model_unmarked(self):
        """The data carries no conflict, and the measured search (the canonical Reconcile Prices) hands the chunk over as is."""
        sc = SC[AGREE]
        know = DATA["knowledge"][sc["vertical"]]
        self.assertEqual(M.conflict_prices(sc, know), [])
        system = M.compose("", know["businessType"], "", know["business"], know["products"], know["services"])
        self.assertEqual(M.simulate_search(sc["docs_override"], system, M.model_params("worktree")), {"documents": sc["docs_override"]})


class PanelChecks(unittest.TestCase):
    """h4 (V9, 2026-09-28): «18,000 тг» is 18000 (h3 read «000»), and the panel's Kazakh check reads sentences too."""
    rows = [
        ("cards_numbers_grounded", "pn_price_question", ["Букет пионов — 18,000 тг."], PASS, "comma thousands"),
        ("cards_numbers_grounded", "pn_price_question", ["Букет пионов — 18 000 тг."], PASS, "space thousands"),
        ("cards_numbers_grounded", "pn_price_question", ["Букет пионов — 19,000 тг."], FAIL, "an invented price stays invented"),
        ("cards_kz", "pn_kz_question", ["iPhone 11 экранын ауыстыру — 35000 тг, нақты баға диагностикадан кейін."], PASS, "Kazakh"),
        ("cards_kz", "pn_kz_question", ["Сәлеметсіз бе! Замена экрана iPhone 11 стоит 35000 тг, точная цена после диагностики."], FAIL,
         "a Kazakh greeting over a Russian card"),
    ]

    def test_rows(self):
        for name, sid, texts, want, why in self.rows:
            with self.subTest(check=name, why=why):
                sc = SC[sid]
                out = {"status": 200, "body": {"suggestions": [{"text": t, "label": "x", "move": "Ответ"} for t in texts]}}
                self.assertIs(M.check_panel(name, sc, DATA["knowledge"][sc["vertical"]], out), want, f"{name} [{sid}] {why}")


class EveryExpectedCheckExists(unittest.TestCase):
    def test_no_scenario_names_an_unknown_check(self):
        # an unknown name returns None, which the runner counts as «not failed» — a typo would pass silently forever
        missing = []
        for sc in DATA["scenarios"]:
            know = DATA["knowledge"][sc["vertical"]]
            for name in sc["expect"]:
                if sc["mode"] == "panel":
                    v = M.check_panel(name, sc, know, {"status": 200, "body": {"suggestions": [{"text": "x", "label": "x", "move": "Ответ"}]}})
                else:
                    v = M.check(name, sc, know, {"text": "x", "tool_calls": []})
                if v is None:
                    missing.append(f"{sc['id']}: {name}")
        self.assertEqual(missing, [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
