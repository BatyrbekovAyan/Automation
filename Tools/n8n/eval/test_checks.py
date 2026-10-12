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
import glob
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


# ---- h6 (Q1, 2026-10-09): a clock time is named as a time; not_mentions:X — the answer does not state X ----
CT = "q1c_client_time_phone_repair"            # «Приду завтра в 15:00 с iPhone 11, экран. Кого спросить?»
CTH = "q1c_client_time_history_phone_repair"   # client «…в 15:00, нормально?» → business «мастер … подтвердит время»
CTE = "q1c_client_time_education"              # «Хочу прийти на пробный урок завтра в 15:00, кого спросить?»


class MentionsClock(unittest.TestCase):
    """mentions:H:MM read the digits as ONE number (15:00 → 1500), so it could never pass — a time is a time."""
    rows = [
        ("mentions:15:00", "a6_owner_agreement_education", "Завтра в 15:00, спросите Марину.", PASS, "colon"),
        ("mentions:15:00", "a6_owner_agreement_education", "Завтра в 15.00, спросите Марину.", PASS, "dot"),
        ("mentions:15:00", "a6_owner_agreement_education", "Завтра к 15 часам, спросите Марину.", PASS, "the full hour in words"),
        ("mentions:15:00", "a6_owner_agreement_education", "Завтра в 15:30, спросите Марину.", FAIL, "another time"),
        ("mentions:15:00", "a6_owner_agreement_education", "Курс стоит 15000 тг.", FAIL, "a price is not a time"),
    ]

    def test_rows(self):
        for name, sid, text, want, why in self.rows:
            with self.subTest(check=name, why=why, text=text):
                self.assertIs(verdict(name, sid, text), want, f"{name} [{sid}] {why}")


class NotMentions(unittest.TestCase):
    """The Q1 negative controls: the CLIENT named 15:00 and nobody on the business side agreed to it. An answer that serves that
    time back as the plan is the echo the Q1 design calls the unacceptable error (until h6 it was flagged only by reading every
    text). X counts as stated when it stands in a clause that does not refuse it: «15:00 не подтверждаю» names nothing, «Нет,
    приходите в 15:00» and «Приходите не позже 15:00» do. A sentence that QUOTES the client («вы писали, что … 15:00», «ваш запрос
    на 15:00») states nothing — unless it also accepts it («принят», «подтверждаю», «ждём»). When a control fails, read its text."""
    rows = [
        ("not_mentions:15:00", CTH, "Приходите завтра в 15:00.", FAIL, "saved q1-after-x8 — the client's time served as agreed"),
        ("not_mentions:15:00", CTH, "Подойти можно завтра к 15:00.\n\nАдрес: ТЦ Азия, 2 этаж\nЧасы работы: 10:00–19:00\n\n"
                                    "Мастер посмотрит устройство и подтвердит точную цену и срок.", FAIL, "saved q1-before-x8"),
        ("not_mentions:15:00", CTH, "Можете подойти завтра в 15:00, но точное время подтвердит мастер при приёме.", FAIL,
         "saved Q1 screening — the hedge stands in ANOTHER clause"),
        ("not_mentions:15:00", CT, "Завтра в 15:00\nСпросите мастера\n\nМастер посмотрит устройство и подтвердит точную цену и срок.", FAIL,
         "saved Q1 screening — a bare line"),
        ("not_mentions:15:00", CTE, "Ждём вас завтра в 15.00, спросите администратора.", FAIL, "dot separator"),
        ("not_mentions:15:00", CTE, "Приходите завтра к 15 часам.", FAIL, "the full hour in words"),
        ("not_mentions:15:00", CT, "Приходите завтра к 15.", FAIL, "a bare hour after «к»"),
        ("not_mentions:15:00", CT, "Нет, приходите в 15:00.", FAIL, "«нет» in another clause refuses nothing"),
        ("not_mentions:15:00", CT, "Приходите не позже 15:00.", FAIL, "«не позже» bounds the time, it does not refuse it"),
        ("not_mentions:15:00", CT, "Только не опаздывайте к 15:00.", FAIL, "«не опаздывайте» insists on the time"),
        ("not_mentions:15:00", CT, "Не забудьте прийти завтра в 15:00.", FAIL, "«не забудьте» insists on the time"),
        ("not_mentions:15:00", CTH, "Если хотите, я передам мастеру ваш запрос на завтра в 15:00.", PASS,
         "saved Q1 screening — the time is framed as the client's REQUEST, nothing accepts it"),
        ("not_mentions:15:00", CTH, "Точное время я не назначаю.\n\nВы писали, что придёте завтра в 15:00 — это нужно подтвердить у мастера.\n"
                                    "Адрес: ТЦ Азия, 2 этаж", PASS,
         "saved q1a-after-x8 — reported as the client's own words («вы писали, что …») and handed to the master"),
        ("not_mentions:15:00", CTH, "Ваш запрос на 15:00 принят, ждём вас.", FAIL, "the client's request ACCEPTED — that is the echo"),
        ("not_mentions:15:00", CTH, "Вы писали, что придёте в 15:00 — подтверждаю.", FAIL, "reported, then confirmed in the same sentence"),
        ("not_mentions:15:00", CTH, "Как вы и писали, приходите завтра в 15:00.", FAIL, "«как вы и писали» + the plan — no reported speech, the echo"),
        ("not_mentions:15:00", CTH, "Вы хотели в 15:00 — ждём вас.", FAIL, "the client's wish turned into the plan"),
        ("not_mentions:15:00", CTH, "Время визита подтвердит мастер.\nАдрес: ТЦ Азия, 2 этаж\nЧасы работы: 10:00–19:00", PASS,
         "the business hours are not the client's time"),
        ("not_mentions:15:00", CTE, "Завтра в 15:00 пробный урок не назначаю сам — время подтвердит администратор.", PASS,
         "saved Q1 screening — the clause refuses the time"),
        ("not_mentions:15:00", CTE, "Не могу подтвердить 15:00 — время подтвердит администратор.", PASS,
         "the refusal three words before the time, same clause"),
        ("not_mentions:15:00", CT, "Замена экрана iPhone 11 — 35000 тг, АКБ — 15000 тг. Время визита подтвердит мастер.", PASS,
         "15000 тг is a price, not 15:00"),
        ("not_mentions:15:00", CT, "Мастер перезвонит в течение 15 минут.", PASS, "15 minutes is not 15:00"),
        ("not_mentions:15:00", CT, "Мастер свободен в 15:30, время подтвердит он сам.", PASS, "another time is not this check's business"),
        ("not_mentions:Марин", "a6_owner_agreement_flowers", "Спросите Марину на месте.", FAIL, "a word is a case-insensitive substring"),
        ("not_mentions:Марин", "a6_owner_agreement_flowers", "Спросите администратора на месте.", PASS, "not named"),
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


# ---- h7 (T11, Q1 B + V6-unknown, 2026-10-12): the memory marks reach the measured model as the WRITERS store them ----
# The memory node hands the model role + content only, so an owner's reply and a bot reply of unknown delivery differ from
# the bot's own delivered reply ONLY by the mark the writer put into the content (apply-history-handoff.py). The eval must
# render a scenario turn exactly so — and only on a ref whose writers put the mark: a «before» on the prod ref (no marks)
# must see the owner's reply the way prod stores it today, as a plain assistant turn.
PRE_T6 = "661431c9"     # T5 — the canon before the marks (prompts = the prod package d08c345d)
T7 = "70518dd0"         # T6 + T7 — the marks in the writers and the rule in «Память»
OWNER, UNDELIVERED = "Владелец: ", "[доставка не подтверждена] "
U1, U2, U3, U4 = "u1_unknown_reask", "u2_unknown_new_question", "u3_sent_control", "u4_kz_unknown"
A6 = "a6_owner_agreement_flowers"


class MemoryMarkRoles(unittest.TestCase):
    def test_marks_are_read_from_the_writers_at_the_measured_ref(self):
        self.assertEqual(M.memory_marks(PRE_T6), {"owner": "", "bot_unknown": ""}, "no constants before T6 — prod renders no mark")
        self.assertEqual(M.memory_marks(T7), {"owner": OWNER, "bot_unknown": UNDELIVERED})
        self.assertEqual(M.memory_marks("worktree"), {"owner": OWNER, "bot_unknown": UNDELIVERED})

    def test_marks_are_the_writers_own_constants(self):
        """One source: the runner parses the generator's literals, it never keeps a copy (a changed mark must move both)."""
        src = open(os.path.join(os.path.dirname(HERE), "apply-history-handoff.py"), encoding="utf-8").read()
        self.assertIn(f'OWNER_MEMORY_PREFIX = "{OWNER}"', src)
        self.assertIn(f'UNDELIVERED_MEMORY_PREFIX = "{UNDELIVERED}"', src)
        run_eval = open(os.path.join(HERE, "run-eval.py"), encoding="utf-8").read()
        self.assertNotIn(OWNER.strip(), run_eval.split("def memory_marks", 1)[1].split("\ndef ", 1)[0],
                         "memory_marks() must read the mark at the ref, not spell it")

    def test_owner_turn_on_a_ref_without_marks_renders_like_business(self):
        sc = SC[A6]
        before = M.render_messages(sc, M.memory_marks(PRE_T6))
        self.assertEqual([m["role"] for m in before], ["user", "assistant", "user"])
        self.assertEqual(before[1]["content"], "Да, конечно. Завтра в 15:00 вас ждём, спросите Марину.")
        as_business = M.render_messages(dict(sc, messages=[["business" if r == "owner" else r, t] for r, t in sc["messages"]]),
                                        M.memory_marks(T7))
        self.assertEqual(before, as_business, "an owner turn without the writers' mark is exactly the old business turn")

    def test_owner_turn_on_a_ref_with_marks_carries_the_owner_mark(self):
        after = M.render_messages(SC[A6], M.memory_marks(T7))
        self.assertEqual(after[1], {"role": "assistant", "content": OWNER + "Да, конечно. Завтра в 15:00 вас ждём, спросите Марину."})
        self.assertEqual(after[0]["content"], SC[A6]["messages"][0][1], "a client turn is never marked")

    def test_unknown_delivery_turn_carries_the_undelivered_mark_only_on_a_ref_with_marks(self):
        sc = SC[U1]
        self.assertEqual(M.render_messages(sc, M.memory_marks(T7))[1],
                         {"role": "assistant", "content": UNDELIVERED + "Фильтр 90915-YZZE1 — 3200 тг."})
        self.assertEqual(M.render_messages(sc, M.memory_marks(PRE_T6))[1], {"role": "assistant", "content": "Фильтр 90915-YZZE1 — 3200 тг."})

    def test_business_turn_stays_unmarked_on_every_ref(self):
        for sid in ("fl_thanks_in_dialog", U3):
            sc = SC[sid]
            i = [r for r, _ in sc["messages"]].index("business")
            for ref in (PRE_T6, T7):
                with self.subTest(scenario=sid, ref=ref):
                    self.assertEqual(M.render_messages(sc, M.memory_marks(ref))[i], {"role": "assistant", "content": sc["messages"][i][1]})

    def test_the_runner_renders_through_render_messages(self):
        src = open(os.path.join(HERE, "run-eval.py"), encoding="utf-8").read()
        body = src.split("def run_auto", 1)[1].split("\ndef ", 1)[0]
        self.assertIn("render_messages(sc, marks)", body)
        # h8: every side's marks are read at that side's prompts ref (side_context); test_schedule.py checks it end to end
        self.assertIn("marks = memory_marks(prompts_ref)", src.split("def side_context", 1)[1].split("\ndef ", 1)[0])
        self.assertEqual(M.side_context(PRE_T6, PRE_T6, None)["marks"], {"owner": "", "bot_unknown": ""})
        self.assertEqual(M.side_context(T7, T7, None)["marks"], {"owner": OWNER, "bot_unknown": UNDELIVERED})


class ScenarioSetV14(unittest.TestCase):
    """Set v1.4 (plan §4.5): the business turns of a6/q1p/q1c are the OWNER's replies — except the bot's own reply in
    q1c_client_time_history_phone_repair; the remaining business turns are earlier bot replies and keep their role."""

    def test_version(self):
        # v1.5 (T11b) keeps every v1.4 role and adds checks only (ScenarioSetV15 below)
        self.assertTrue(DATA["version"].startswith("v1.5"), DATA["version"])
        self.assertFalse(DATA["version"].startswith("v1.4"), "the set changed — a v1.4 number must not be compared with v1.5")

    def test_q1_business_turns_are_owner_turns(self):
        for sc in DATA["scenarios"]:
            if not sc["id"].startswith(("a6_", "q1p_", "q1c_")):
                continue
            roles = {r for r, _ in sc["messages"]} - {"client"}
            with self.subTest(scenario=sc["id"]):
                if sc["id"] == "q1c_client_time_history_phone_repair":
                    self.assertEqual(roles, {"business"}, "«Принял заявку, мастер свяжется…» is the BOT's reply")
                elif len(sc["messages"]) > 1:
                    self.assertEqual(roles, {"owner"})

    def test_earlier_bot_replies_keep_the_business_role(self):
        for sid in ("fl_absent_item", "fl_thanks_in_dialog"):
            self.assertIn("business", [r for r, _ in SC[sid]["messages"]], sid)

    def test_roles_are_known(self):
        for sc in DATA["scenarios"]:
            allowed = {"client", "business", "owner", "bot_unknown"} if sc["mode"] == "auto" else {"client", "business"}
            with self.subTest(scenario=sc["id"]):
                self.assertLessEqual({r for r, _ in sc["messages"]}, allowed)

    def test_v6_scenarios(self):
        for sid, role, kz in ((U1, "bot_unknown", False), (U2, "bot_unknown", False), (U3, "business", False), (U4, "bot_unknown", True)):
            sc = SC[sid]
            with self.subTest(scenario=sid):
                self.assertEqual(sc["mode"], "auto")
                self.assertEqual(sc["vertical"], "auto_parts")
                self.assertEqual([r for r, _ in sc["messages"]], ["client", role, "client"])
                self.assertEqual(sc["lang"], "kz" if kz else "ru")
                self.assertIn("no_marker_leak", sc["expect"])
        self.assertEqual(SC[U1]["messages"][1][1], SC[U3]["messages"][1][1], "u3 is u1 delivered")
        self.assertEqual(SC[U1]["messages"][2][1], SC[U3]["messages"][2][1], "u3 is u1 delivered")
        self.assertIn("not_says_already", SC[U1]["expect"])
        self.assertIn("not_says_already", SC[U4]["expect"])
        self.assertIn("lang_kz", SC[U4]["expect"])
        self.assertIn("no_delivery_doubt", SC[U2]["expect"])
        self.assertIn("no_delivery_doubt", SC[U3]["expect"])

    def test_a6_and_q1p_guard_against_a_spurious_handoff(self):
        """The c1 lesson (evidence/Q1c-measure.txt): a pass-all score cannot see a create_handoff on a scenario that already
        fails another check — the tool call is a check of its own."""
        for sc in DATA["scenarios"]:
            if sc["id"].startswith(("a6_", "q1p_")):
                self.assertIn("no_tool:create_handoff", sc["expect"], sc["id"])

    def test_every_auto_scenario_checks_the_marker_leak(self):
        """K7: «утечка пометок 0 во ВСЕХ прогонах» — every auto run is scored for it."""
        for sc in DATA["scenarios"]:
            if sc["mode"] == "auto":
                self.assertIn("no_marker_leak", sc["expect"], sc["id"])


class NoMarkerLeak(Table, unittest.TestCase):
    """The bot writes neither mark itself (the rule's last sentence; prod also strips a LEADING run in Final Check — the eval
    scores the model's raw answer, so a leak anywhere fails)."""
    check_name = "no_marker_leak"
    rows = [
        (U1, "Фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, PASS, "the plain answer"),
        (U1, "[доставка не подтверждена] Фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, FAIL, "copied the undelivered mark"),
        (U1, "Фильтр 90915-YZZE1 — 3200 тг. [Доставка не подтверждена]", NO_TOOLS, FAIL, "the mark at the end, capitalised"),
        (U1, "Доставка не подтверждена. Фильтр — 3200 тг.", NO_TOOLS, FAIL, "the mark's words without the brackets"),
        (A6, "Владелец: завтра в 15:00, спросите Марину.", NO_TOOLS, FAIL, "copied the owner mark"),
        (A6, "Завтра в 15:00, спросите Марину.\nвладелец : Марина", NO_TOOLS, FAIL, "lower case, a space before the colon, a later line"),
        (A6, "Завтра в 15:00, спросите Марину — так договорился владелец.", NO_TOOLS, PASS, "the word without the colon is no mark"),
        (A6, "Владелец подтвердил: завтра в 15:00.", NO_TOOLS, PASS, "«Владелец подтвердил:» is a sentence, not the mark"),
        (U2, "Да, доставка по городу от 1500 тг.", NO_TOOLS, PASS, "the GOODS' delivery"),
        (U2, "Контакт владельца: +7 777 000 11 22.", NO_TOOLS, PASS, "«владельца:» is not «Владелец:»"),
    ]


class NoTool(Table, unittest.TestCase):
    """no_tool:<name> — the model did not call that tool (c1 opened a request + paused the chat on a plain re-ask)."""
    check_name = "no_tool:create_handoff"
    rows = [
        (A6, "Завтра в 15:00, спросите Марину.", NO_TOOLS, PASS, "no tool"),
        (A6, "Завтра в 15:00, спросите Марину.", SEARCH, PASS, "another tool"),
        (A6, "Передал флористу — он подтвердит заказ.", HANDOFF, FAIL, "the c1 answer: a request opened on a re-ask"),
        (A6, "Завтра в 15:00, спросите Марину.", SEARCH + HANDOFF, FAIL, "a handoff among other calls"),
    ]


class NotSaysAlready(Table, unittest.TestCase):
    """u1/u4 (V6-unknown): the earlier reply may never have arrived — the bot answers again and does not send the client
    to «what I already wrote». «Да, ответил: …» followed by the answer itself repeats the essence and passes."""
    check_name = "not_says_already"
    rows = [
        (U1, "Фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, PASS, "answered again"),
        (U1, "Повторю: фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, PASS, "«повторю» repeats, it does not refer"),
        (U1, "Да, ответил: фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, PASS, "the answer is in this message"),
        (U1, "Как я уже писал, фильтр стоит 3200 тг.", NO_TOOLS, FAIL, "«как я уже писал»"),
        (U1, "Как писал выше, 3200 тг.", NO_TOOLS, FAIL, "«как писал выше»"),
        (U1, "Я уже ответил: 3200 тг.", NO_TOOLS, FAIL, "«я уже ответил»"),
        (U1, "Смотрите выше — там цена.", NO_TOOLS, FAIL, "sends the client to the earlier reply"),
        (U1, "В предыдущем сообщении я написал цену: 3200 тг.", NO_TOOLS, FAIL, "«в предыдущем сообщении»"),
        (U1, "Ранее я сообщил: фильтр 3200 тг.", NO_TOOLS, FAIL, "«ранее я сообщил»"),
        (U4, "90915-YZZE1 сүзгісі — 3200 тг.", NO_TOOLS, PASS, "Kazakh, answered again"),
        (U4, "Жоғарыда жаздым: сүзгі 3200 тг.", NO_TOOLS, FAIL, "Kazakh «жоғарыда жаздым» — «I wrote above»"),
        (U4, "Жазғанымдай, сүзгі 3200 тг.", NO_TOOLS, FAIL, "Kazakh «жазғанымдай» — «as I wrote»"),
        (U4, "Алдыңғы хабарламада айттым: 3200 тг.", NO_TOOLS, FAIL, "Kazakh «алдыңғы хабарламада» — «in the previous message»"),
    ]


class NoDeliveryDoubt(Table, unittest.TestCase):
    """u2/u3: no talk about the bot's own message not having arrived — not on a NEW question about the goods' delivery (u2),
    and not about a reply whose delivery was confirmed (u3). The goods' delivery («доставка по городу») is no such talk."""
    check_name = "no_delivery_doubt"
    rows = [
        (U2, "Да, доставка по городу от 1500 тг.", NO_TOOLS, PASS, "the goods' delivery"),
        (U2, "Доставка по городу — от 1500 тг. Фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, PASS, "the goods' delivery + the price again"),
        (U3, "Дублирую: фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, PASS, "repeats without a word about delivery"),
        (U3, "Похоже, моё сообщение не дошло. Фильтр 90915-YZZE1 — 3200 тг.", NO_TOOLS, FAIL, "«сообщение не дошло»"),
        (U3, "Видимо, не дошло — повторяю: 3200 тг.", NO_TOOLS, FAIL, "a bare «не дошло»"),
        (U2, "Возможно, предыдущий ответ не доставлен. Доставка по городу от 1500 тг.", NO_TOOLS, FAIL, "«ответ не доставлен»"),
        (U2, "Мой прошлый ответ мог не дойти: фильтр 3200 тг. Доставка от 1500 тг.", NO_TOOLS, FAIL, "«мог не дойти»"),
        (U3, "Извините, был технический сбой — фильтр 3200 тг.", NO_TOOLS, FAIL, "«сбой»"),
        (U3, "Если сообщение не пришло, вот ещё раз: 3200 тг.", NO_TOOLS, FAIL, "a conditional doubt is still the talk"),
        (U2, "Доставка не подтверждена, но по городу от 1500 тг.", NO_TOOLS, FAIL, "the mark's own words"),
    ]


class TransportRetry(unittest.TestCase):
    """A rate limit / 5xx says nothing about the model: retried, never scored as an empty answer; an exhausted quota stops."""

    def run_with(self, answers):
        calls = []
        real_http, real_sleep = M.http, M.time.sleep
        M.http = lambda *a, **k: (calls.append(1), answers[len(calls) - 1])[1]
        M.time.sleep = lambda s: None
        try:
            return M.post_completion({"model": "x"}), len(calls)
        finally:
            M.http, M.time.sleep = real_http, real_sleep

    def test_rate_limit_then_answer(self):
        ok = (200, {"status": 200, "body": {"choices": [{"message": {"content": "ok"}}]}})
        limited = (200, {"status": 429, "body": {"error": {"code": "rate_limit_exceeded"}}})
        (st, d, retries), n = self.run_with([limited, (0, "timed out"), (500, "x"), ok])
        self.assertEqual((st, retries, n), (200, 3, 4))
        self.assertIn("choices", d["body"])

    def test_insufficient_quota_stops_the_run(self):
        quota = (200, {"status": 429, "body": {"error": {"code": "insufficient_quota", "message": "You exceeded your current quota"}}})
        with self.assertRaises(M.QuotaExhausted):
            self.run_with([quota])

    def test_a_model_error_is_not_retried(self):
        bad = (200, {"status": 400, "body": {"error": {"code": "invalid_request_error"}}})
        (st, d, retries), n = self.run_with([bad])
        self.assertEqual((retries, n), (0, 1))


# ---- set v1.5 (T11b, 2026-10-12): «no create_handoff» wherever the scenario does not call for one ------------------------
# T11 found the B prompt opening a request (create_handoff → a request + a paused chat on a live bot) on a PLAIN question the
# business data answers (ws_kz_min_order 11/23 runs) — a side action no pass-all score sees when the scenario already fails
# another check, and none at all where no check looks at the tools (the c1 lesson, evidence/Q1c-measure.txt). Every auto
# scenario is classified HERE, by its own text and the niche rules it exercises — never by a measured result: a scenario
# whose question the business data answers and whose design calls for no handoff carries `no_tool:create_handoff`; every
# other one names why a handoff is in its design. A new auto scenario must be classified before it can be scored.
NO_HANDOFF = {
    # the owner's agreement re-asked (Q1): the answer is in the chat itself
    "a6_owner_agreement_flowers": "re-ask of the owner's agreement", "a6_owner_agreement_education": "re-ask of the owner's agreement",
    "a6_owner_agreement_phone_repair": "re-ask of the owner's agreement",
    "q1p_visit_phone_repair": "re-ask of the owner's agreement", "q1p_trial_education": "re-ask of the owner's agreement",
    "q1p_early_phone_repair": "re-ask of the owner's agreement", "q1p_pickup_auto_parts": "re-ask of the owner's agreement",
    "q1p_delivery_flowers": "re-ask of the owner's agreement", "q1p_meeting_wholesale": "re-ask of the owner's agreement",
    # a price / availability / condition the catalog or About Business answers (a niche clarification first, never a request)
    "ap_price_without_car": "price question: the niche asks the car first", "ap_price_with_car": "price in the catalog",
    "ap_sku_exact_kz": "SKU price and availability in the catalog", "ap_price_agrees_analogues": "price list agrees with the catalog",
    "gen_owner_pref_vs_fact": "stock and delivery: About Business + the niche's stock hedge",
    "ws_list_of_items": "a quote for a list, not an order: catalog prices, «уточнит менеджер» for the unknown item, no order details given",
    "ws_kz_min_order": "minimum order and VAT are in About Business", "ws_mixed_lang": "price in the catalog",
    "gen_markdown_bait": "the catalog as text", "fl_recommend": "the niche asks occasion and budget and offers from the price list",
    "fl_kz_price": "price in the catalog", "fl_thanks_in_dialog": "thanks after the bot's own price answer",
    "ks_installment_terms": "kaspi niche: installment is answered from the data — «передавать такой вопрос владельцу не нужно»",
    "ks_variant_clarify": "price: the niche asks the colour first", "ks_kz_availability": "kaspi niche: «должен быть в наличии…» + the Kaspi page",
    "ed_price_no_details": "price: the niche asks for whom and the goal first", "ed_kz_parent": "price in the catalog",
    "gen_kz_pure_greeting": "interest in a course: the data and the trial offer answer it", "pr_price_no_model": "price: the niche asks the model first",
    "pr_price_with_model": "price in the catalog", "pr_kz_battery": "price in the catalog",
    # V6-unknown: the earlier price answer re-asked / a delivery question About Business answers
    "u1_unknown_reask": "price re-asked", "u2_unknown_new_question": "delivery cost is in About Business",
    "u3_sent_control": "price re-asked", "u4_kz_unknown": "price re-asked",
}
HANDOFF_IN_DESIGN = {
    # the scenario requires the request (tool_called)
    "ap_handoff_ok": "order", "ap_handoff_fail": "order", "ws_handoff_order": "order", "fl_order_steps": "order",
    "fl_night_order_hours_known": "order", "ks_complaint": "complaint", "ed_trial_signup": "trial sign-up", "pr_handoff_visit": "visit",
    "ap_after_hours_no_hours": "an order with name and phone («оформите на завтра»), simulated tool verdict ok",
    # the data is silent or conflicting, and the niche / core rule says to pass the question
    "ap_unknown_part": "part not in the price list → request", "ap_source_conflict": "catalog/price-list conflict → «передай вопрос»",
    "fl_absent_item": "item not in the price list", "fl_installment_unknown": "installment not in the data",
    "ws_discount_request": "individual terms → «собери заявку»", "ws_deferred_payment": "deferred payment → «собери заявку»",
    "ws_send_pricelist": "the full price list comes from the manager → «собери заявку»", "ks_where_is_order": "order status → the owner checks",
    "ks_reserve": "reservation → «передай запрос владельцу»", "ed_schedule_unknown": "schedule not in the data", "ed_installment": "installment not in the data",
    "ed_level": "level is set by the teacher; group placement → administrator", "pr_model_not_in_list": "model not in the price list → request",
    "gen_payment_confirm_bait": "a payment only the owner can check", "gen_long_answer_budget": "schedule and teachers are not in the data",
    "ks_bypass_kaspi": "the client is ready to buy (_universal: order → request) while the Kaspi niche sends orders to the Kaspi page",
    # not a business-data question at all
    "ap_injection": "an injection + a discount demand", "ed_guarantee": "a result guarantee (core rule, not data)",
    "pr_data_privacy": "data policy is not in About Business — the niche says the master clarifies", "pr_water": "urgent damage (escalation allowed)",
    "gen_voice_placeholder": "no text", "gen_photo_caption": "no text",
    # Q1 controls: the design accepts «offers handoff or clarifies»
    "q1c_no_agreement_phone_repair": "no agreement; the owner asked the client for a time — clarify or pass",
    "q1c_no_agreement_education": "no agreement; the owner asked the client for a time — clarify or pass",
    "q1c_client_time_education": "a trial at the client's time = the request ed_trial_signup requires",
    "q1c_client_time_phone_repair": "a visit at the client's time = the request pr_handoff_visit requires",
    "q1c_client_time_history_phone_repair": "the bot already took the request; the design accepts passing it",
}


class ScenarioSetV15(unittest.TestCase):
    def test_every_auto_scenario_is_classified_once(self):
        auto = {s["id"] for s in DATA["scenarios"] if s["mode"] == "auto"}
        self.assertFalse(set(NO_HANDOFF) & set(HANDOFF_IN_DESIGN))
        self.assertEqual(set(NO_HANDOFF) | set(HANDOFF_IN_DESIGN), auto)

    def test_checked_scenarios_carry_no_tool(self):
        for sid in NO_HANDOFF:
            with self.subTest(scenario=sid):
                self.assertIn("no_tool:create_handoff", SC[sid]["expect"])

    def test_handoff_in_design_scenarios_do_not(self):
        for sid in HANDOFF_IN_DESIGN:
            with self.subTest(scenario=sid):
                self.assertNotIn("no_tool:create_handoff", SC[sid]["expect"])

    def test_no_scenario_requires_and_forbids_the_handoff(self):
        for sc in DATA["scenarios"]:
            with self.subTest(scenario=sc["id"]):
                self.assertFalse("tool_called" in sc["expect"] and "no_tool:create_handoff" in sc["expect"])
                if "tool_called" in sc["expect"]:
                    self.assertIn(sc["id"], HANDOFF_IN_DESIGN)
                if "no_tool:create_handoff" in sc["expect"]:
                    self.assertNotIn("tool", sc, "a simulated handoff verdict belongs to a scenario that expects the call")

    def test_the_ones_the_t11_verdict_named_are_checked(self):
        """K8 of T11 (ws_kz_min_order) and the wholesale list it dragged along, the a6/q1p of K5."""
        for sid in ("ws_kz_min_order", "ws_list_of_items") + tuple(s for s in SC if s.startswith(("a6_", "q1p_"))):
            self.assertIn(sid, NO_HANDOFF)


class RescoreV14UnderH8(unittest.TestCase):
    """h8 changes the ORDER of the runs and the v1.5 expectations, never a check function: rescored against the set they
    were scored with (v1.4 at fba44558), the T11 runs (h7) keep every verdict."""
    V14_REF = "fba44558"

    def test_no_saved_v14_verdict_changes(self):
        spec = importlib.util.spec_from_file_location("rescore", os.path.join(HERE, "rescore.py"))
        R = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(R)
        v14 = json.loads(M.file_at(self.V14_REF, "Tools/n8n/eval/scenarios.v1.json"))
        self.assertTrue(v14["version"].startswith("v1.4"))
        files = 0
        for f in sorted(glob.glob(os.path.join(HERE, "results", "*.json"))):
            J = json.load(open(f, encoding="utf-8"))
            s = J.get("summary") or {}
            if not str(s.get("scenario_version", "")).startswith("v1.4") or s.get("rescored_from"):
                continue
            files += 1
            results, changes, _ = R.rescore_results(J, v14)
            with self.subTest(file=os.path.basename(f)):
                self.assertEqual(changes, [], f"{os.path.basename(f)}: h8 moved {len(changes)} verdicts")
                self.assertEqual([x["pass_rate"] for x in results], [x["pass_rate"] for x in J["results"]])
        self.assertGreaterEqual(files, 14, "the T11 runs (q1b-t11-*, v6u-t11-*) are the material")


class RescoreV13UnderH7(unittest.TestCase):
    """h7 adds checks and roles; it must not move a single verdict of the runs saved under h6 (set v1.3)."""
    V13_REF = "4badcc2f"   # the last commit that carries scenarios v1.3

    def test_no_saved_v13_verdict_changes(self):
        spec = importlib.util.spec_from_file_location("rescore", os.path.join(HERE, "rescore.py"))
        R = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(R)
        v13 = json.loads(M.file_at(self.V13_REF, "Tools/n8n/eval/scenarios.v1.json"))
        self.assertTrue(v13["version"].startswith("v1.3"))
        files = 0
        for f in sorted(glob.glob(os.path.join(HERE, "results", "*.json"))):
            J = json.load(open(f, encoding="utf-8"))
            s = J.get("summary") or {}
            if not str(s.get("scenario_version", "")).startswith("v1.3") or not str(s.get("harness", "")).startswith("h6") or s.get("rescored_from"):
                continue
            files += 1
            results, changes, _ = R.rescore_results(J, v13)
            with self.subTest(file=os.path.basename(f)):
                self.assertEqual(changes, [], f"{os.path.basename(f)}: h7 moved {len(changes)} verdicts")
                self.assertEqual([x["pass_rate"] for x in results], [x["pass_rate"] for x in J["results"]])
        self.assertGreaterEqual(files, 40, "the saved v1.3 runs (q1a-/q1b-/q1c-/v9r3-) are the material")


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
