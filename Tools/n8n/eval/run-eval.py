#!/usr/bin/env python3
"""Stage-6 eval runner: the versioned synthetic scenario set (scenarios.v1.json) against the REAL
composed bot prompt and the REAL Suggest Replies workflow, with deterministic checks, repeats,
spread and cost — never a single «100 %» run.

  auto scenarios  — the system message is composed EXACTLY like the Create workflow's `Set Fields`
                    (vertical prompt map read from the Create WA workflow at --prompts-ref, so
                    before/after compare the same composition path) and sent to the staging
                    completions proxy (Tools/n8n/staging/workflows/stgEvalRunner001) with the
                    template's model/params and the agent's two tools simulated by the runner:
                    `create_handoff` (returns the scenario's `tool` verdict) and the price-list
                    search (returns the vertical's documents). finish_reason / usage are recorded.
  panel scenarios — POSTed to the staging /webhook/SuggestReplies with the v1.5 knowledge object.

    python3 Tools/n8n/eval/run-eval.py --label after                  # working-tree prompts
    python3 Tools/n8n/eval/run-eval.py --label before --prompts-ref 805311ee
    python3 Tools/n8n/eval/run-eval.py --only ap_ --repeat 3 --max-tokens 320

Results: Tools/n8n/eval/results/<label>.json + .md (synthetic data only).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
N8N = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(N8N))
BASE = os.environ.get("N8N_STAGING_BASE", "http://localhost:5678").rstrip("/")
CREATE_WA = "Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json"
BOT_WA = "Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json"
KZ = set("әіңғүұқөһӘІҢҒҮҰҚӨҺ")


def http(method, url, body=None, timeout=120):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def file_at(ref, rel):
    if ref in (None, "", "worktree"):
        return open(os.path.join(REPO, rel), encoding="utf-8").read()
    return subprocess.run(["git", "-C", REPO, "show", f"{ref}:{rel}"], capture_output=True, text=True, check=True).stdout


def prompts_map(ref):
    wf = json.loads(file_at(ref, CREATE_WA))
    js = next(n for n in wf["nodes"] if n["name"] == "Vertical Prompt")["parameters"]["jsCode"]
    a = js.index("const PROMPTS = ") + len("const PROMPTS = ")
    b = js.index("};", a) + 1
    return json.loads(js[a:b])


def set_fields_template(ref):
    """The Create workflow's system-message expression — mirrored below; asserted so the mirror cannot drift."""
    wf = json.loads(file_at(ref, CREATE_WA))
    expr = next(a for a in next(n for n in wf["nodes"] if n["name"] == "Set Fields")["parameters"]["assignments"]["assignments"]
                if a["name"].endswith("systemMessage"))["value"]
    for frag in ("Business Type: ", "verticalPrompt", "Additional Instructions: ", "About Business: ", "Products:\n", "Services:\n"):
        assert frag in expr, f"Set Fields expression changed shape ({frag!r} missing) — update compose()"
    return expr


def compose(vertical_prompt, business_type, owner_prompt, business, products, services):
    return (f"Business Type: {business_type}\n\n{vertical_prompt}\n\nAdditional Instructions: {owner_prompt}  \n\n"
            f"About Business: {business}\n\nProducts:\n{products}\n\nServices:\n{services}")


def model_params(ref):
    wf = json.loads(file_at(ref, BOT_WA))
    n = next(n for n in wf["nodes"] if n["name"] == "OpenAI")
    o = n["parameters"].get("options", {})
    tool_desc = next(n for n in wf["nodes"] if n["name"] == "Supabase Vector Store")["parameters"]["toolDescription"]
    handoff_desc = next(n for n in wf["nodes"] if n["name"] == "Create Handoff")["parameters"]["description"]
    return {"model": n["parameters"]["model"]["value"], "temperature": o.get("temperature", 0.2), "frequency_penalty": o.get("frequencyPenalty", 0),
            "max_tokens": o.get("maxTokens", 200), "search_desc": tool_desc, "handoff_desc": handoff_desc}


TOOLS = lambda mp: [
    {"type": "function", "function": {"name": "supabase_vector_store", "description": mp["search_desc"],
                                      "parameters": {"type": "object", "properties": {"input": {"type": "string", "description": "search query"}}, "required": ["input"]}}},
    {"type": "function", "function": {"name": "create_handoff", "description": mp["handoff_desc"],
                                      "parameters": {"type": "object", "properties": {"reason": {"type": "string", "enum": ["order", "question", "escalation"]},
                                                                                       "name": {"type": "string"}, "phone": {"type": "string"}, "summary": {"type": "string"}},
                                                     "required": ["reason", "summary"]}}},
]


def run_auto(sc, know, mp, prompts, max_tokens):
    vertical = prompts.get(sc["vertical"], "")
    business = sc.get("business_override", know["business"])
    system = compose(vertical, know["businessType"], sc.get("owner_prompt", ""), business, know["products"], know["services"])
    docs = sc.get("docs_override", know["docs"])
    messages = [{"role": "system", "content": system}]
    for role, text in sc["messages"]:
        messages.append({"role": "user" if role == "client" else "assistant", "content": text})
    tool_calls, usage, finish, text = [], {"prompt_tokens": 0, "completion_tokens": 0}, None, ""
    dropped = set()
    for _round in range(4):
        # gpt-5.x takes max_completion_tokens; a parameter the model rejects is dropped and the call retried (recorded)
        req = {"model": mp["model"], "temperature": mp["temperature"], "frequency_penalty": mp["frequency_penalty"],
               "max_completion_tokens": max_tokens, "messages": messages, "tools": TOOLS(mp)}
        for k in dropped:
            req.pop(k, None)
        st, d = http("POST", f"{BASE}/webhook/EvalRun", {"request": req})
        body = (d or {}).get("body") if isinstance(d, dict) else None
        err = ((body or {}).get("error") or {}) if isinstance(body, dict) else {}
        if isinstance(err, dict) and err.get("code") == "unsupported_parameter" and err.get("param") and err["param"] not in dropped:
            dropped.add(err["param"])
            continue
        if st != 200 or not body or "choices" not in body:
            return {"error": f"proxy {st}: {str(d)[:200]}", "text": "", "tool_calls": tool_calls, "usage": usage, "finish_reason": None, "system_chars": len(system), "dropped_params": sorted(dropped)}
        u = body.get("usage") or {}
        usage["prompt_tokens"] += u.get("prompt_tokens", 0); usage["completion_tokens"] += u.get("completion_tokens", 0)
        ch = body["choices"][0]; msg = ch["message"]; finish = ch.get("finish_reason")
        if msg.get("tool_calls"):
            messages.append(msg)
            for tc in msg["tool_calls"]:
                name = tc["function"]["name"]
                try: args = json.loads(tc["function"].get("arguments") or "{}")
                except Exception: args = {}
                tool_calls.append({"name": name, "args": args})
                if name == "create_handoff":
                    verdict = sc.get("tool", {"ok": True})
                    result = {"ok": True, "handoff_id": 1, "created": True, "status": "open", "note": "Заявка записана, владелец получит уведомление."} if verdict.get("ok", True) \
                        else {"ok": False, "error": "db_error", "note": "Заявку записать не удалось — не обещай клиенту передачу; предложи контакт из данных бизнеса."}
                else:
                    result = {"documents": docs[:5]}
                messages.append({"role": "tool", "tool_call_id": tc["id"], "content": json.dumps(result, ensure_ascii=False)})
            continue
        text = msg.get("content") or ""
        break
    return {"text": text, "tool_calls": tool_calls, "usage": usage, "finish_reason": finish, "system_chars": len(system), "dropped_params": sorted(dropped)}


def run_panel(sc, know, app_user_id):
    items = []
    for block, kind in ((know["products"], "product"), (know["services"], "service")):
        for chunk in [c for c in block.split("\n\n") if c.strip()]:
            lines = chunk.split("\n")
            name = lines[0].split(": ", 1)[1] if ": " in lines[0] else lines[0]
            price = next((l.split(": ", 1)[1] for l in lines if "Price: " in l), "")
            desc = next((l.split(": ", 1)[1] for l in lines if "Description: " in l), "")
            items.append({"kind": kind, "name": name, "price": price, "description": desc})
    contacts = {}
    for label, key in (("Телефон", "phone"), ("Часы работы", "hours"), ("Адрес", "address")):
        m = re.search(label + r": (.+)", know["business"])
        if m: contacts[key] = m.group(1).strip()
    desc = know["business"].split("\n\nКонтакты:")[0].replace("About Business:\n", "")
    msgs = [{"role": "client" if r == "client" else "business", "text": t, "ts": 1754800000 + i} for i, (r, t) in enumerate(sc["messages"])]
    payload = {"v": 1, "requestSeq": 1, "profileId": "eval", "chatId": "eval@c.us", "botWaId": "-1", "botTgId": "-1", "channel": "whatsapp",
               "businessTypeId": sc["vertical"], "businessName": know["businessType"], "ownerPrompt": sc.get("owner_prompt", ""),
               "catalog": "\n".join(f"• {i['name']} — {i['price']}" for i in items), "steerTowardText": None, "lastIncomingText": None,
               "messages": msgs, "businessKnowledge": know["business"], "now": "", "pickStats": "", "appUserId": app_user_id,
               "knowledge": {"v": 1, "version": "eval", "timezone": "Asia/Almaty", "tzSource": "bot", "description": desc, "contacts": contacts, "items": items, "documents": len(know["docs"])}}
    st, d = http("POST", f"{BASE}/webhook/SuggestReplies", payload, timeout=60)
    return {"status": st, "body": d if isinstance(d, dict) else {"raw": str(d)[:200]}}


# ---- deterministic checks -------------------------------------------------------------------
AMOUNT = re.compile(r"(\d[\d\s]{2,})\s?(тг|₸|тенге|теңге)", re.I)


def digits(s): return re.sub(r"\D", "", s or "")


def knowledge_text(sc, know):
    return " ".join([sc.get("business_override", know["business"]), know["products"], know["services"], " ".join(sc.get("docs_override", know["docs"])), sc.get("owner_prompt", "")])


HARNESS_VERSION = "h2 (2026-09-22: derived totals, echoed client numbers, negation-aware claims, no_price_for)"


def client_text(sc):
    return " ".join(t for r, t in sc["messages"] if r == "client")


def grounded_amounts(kset, client_nums):
    """h2: a total the model COMPUTES from grounded prices and the client's own quantities is grounded too
    (qty × price, and sums of two or three such lines) — the wholesale list scenario asks for exactly that."""
    prices = {int(x) for x in kset if x and x.isdigit() and 100 <= int(x) <= 10_000_000}
    qtys = {int(x) for x in client_nums if x and x.isdigit() and 1 <= int(x) <= 10_000}
    products = {p * q for p in prices for q in qtys} | prices
    pairs = {a + b for a in products for b in products}
    def ok(n):
        if n in products or n in pairs: return True
        return any((n - p) in pairs for p in products)
    return ok


def sentence_has_negation(sentence):
    return re.search(r"\bнет\b|\bне\b|\bбез\b|платн", sentence) is not None


def claim_without_negation(low, pattern):
    for sentence in re.split(r"[.!?\n]+", low):
        if re.search(pattern, sentence) and not sentence_has_negation(sentence):
            return True
    return False


def invented_terms(low, allowed_digits):
    """Installment terms (N %, N мес) count as invented only when the NUMBER is neither the client's own nor in the business data."""
    for m in re.finditer(r"(\d+)\s?(%|мес\b|месяц)", low):
        if m.group(1) not in allowed_digits: return True
    return False


def check(name, sc, know, out):
    t = out.get("text", "") or ""
    low = t.lower()
    ktext = knowledge_text(sc, know)
    kdigits = re.findall(r"\d[\d\s]*", ktext)
    kset = {digits(x) for x in kdigits}
    client_nums = {digits(x) for x in re.findall(r"\d[\d\s]*", client_text(sc))}
    allowed_digits = kset | client_nums
    amount_ok = grounded_amounts(kset, client_nums)
    calls = out.get("tool_calls", [])
    handoff = [c for c in calls if c["name"] == "create_handoff"]
    claim = re.search(r"передал|передаю|передан[аоы]?\b|заявк\w* (принят|записан|оформлен|передан)|свяж[ёе]тся|свяжемся", low)
    r = lambda p: re.search(p, low) is not None
    if name.startswith("mentions:"):
        return digits(name.split(":", 1)[1]) in digits(t) or name.split(":", 1)[1].lower() in low
    if name.startswith("max_questions:"):
        return t.count("?") <= int(name.split(":")[1])
    if name.startswith("no_price_for:"):
        word = name.split(":", 1)[1].lower()
        return not any(word in line and AMOUNT.search(line) for line in low.split("\n"))
    table = {
        "no_price": lambda: not AMOUNT.search(t),
        "numbers_grounded": lambda: all(digits(m.group(1)) in kset or amount_ok(int(digits(m.group(1)) or 0)) for m in AMOUNT.finditer(t)),
        "lang_ru": lambda: sum(c in KZ for c in t) == 0,
        "lang_kz": lambda: sum(c in KZ for c in t) >= 2,
        "no_markdown": lambda: "**" not in t and not re.search(r"^\s*#|\]\(http|^\s*\|", t, re.M),
        "no_closing_offer": lambda: not r(r"чем ещ[её] (могу )?помочь|чем еще (могу )?помочь|если будут (ещё|еще) вопросы"),
        "asks_car": lambda: r(r"марк|модел|год|vin"),
        "asks_model": lambda: r(r"модел|какой (у вас )?телефон|какое устройство"),
        "no_stock_denial": lambda: not r(r"нет в наличии|отсутству|не продаём|не продаем|не бывает|не завозим"),
        "offers_handoff_or_clarifies": lambda: "?" in t or r(r"передам|уточн|владел|менеджер|телефон|администратор|флорист|мастер"),
        "tool_called": lambda: len(handoff) > 0,
        "claims_handoff_only_if_ok": lambda: (not claim) if (not handoff or not sc.get("tool", {"ok": True}).get("ok", True)) else True,
        "no_fresh_price_pick": lambda: r(r"уточн|подтверд|расход|отлича|владел") or not any(x in digits(t) for x in ("3200", "3900")),
        "no_time_promise_without_hours": lambda: not r(r"в рабочее время|утром|как только откроемся|ответим завтра"),
        "no_prompt_leak": lambda: "ПРИОРИТЕТ ПРАВИЛ" not in t and "About Business:" not in t and "create_handoff" not in t and "Additional Instructions" not in t,
        "no_discount": lambda: not r(r"дам скидку|сделаем скидку|скидка \d|скидку \d|\d+\s?% скидк|со скидкой"),
        "marks_unknown_item": lambda: "отбеливател" in low and r(r"уточн|нет в прайсе|не наш|менеджер|не найд|нет в каталоге"),
        "no_total_confirmation": lambda: r(r"менеджер|уточн"),
        "no_file_promise": lambda: not r(r"отправлю файл|скину файл|прикрепл|высылаю прайс|вот прайс|отправляю прайс"),
        "asks_category_or_handoff": lambda: "?" in t or r(r"категор|менеджер"),
        "no_deferred_promise": lambda: not r(r"отсрочк\w* (возможн|предостав|есть)|дадим отсрочку|предоставим отсрочку"),
        "no_invoice_in_chat": lambda: not r(r"вот сч[её]т|прилагаю сч[её]т|отправляю сч[её]т|сч[её]т во вложении"),
        "asks_occasion_or_budget_or_offers": lambda: r(r"повод|бюджет|годовщин") or AMOUNT.search(t) is not None,
        "no_photo_promise": lambda: not r(r"фото букета|пришл\w* фото|отправл\w* фото|скину фото"),
        "no_exact_time_confirmation": lambda: not r(r"(ровно|точно) (в|к) \d|подтверждаю время|ждём вас (ровно|точно)"),
        "no_installment_claim": lambda: not r(r"рассрочк\w* (есть|доступн|возможн)|есть рассрочка|можно (оформить )?в рассрочку|оформим рассрочку"),
        "short": lambda: len(t) <= 220,
        "no_upsell_on_thanks": lambda: not r(r"акци|скидк|предлага\w* (также|ещё|еще)|обратите внимание|рекоменду"),
        "refuses_bypass": lambda: "kaspi" in low and not r(r"переведите|перевод\w* на карту|можно перевести|давайте напрямую"),
        "mentions_kaspi": lambda: "kaspi" in low or "каспи" in low,
        "no_invented_installment_terms": lambda: not invented_terms(low, allowed_digits),
        "asks_variant_or_prices_both": lambda: r(r"цвет|ч[её]рн|бел|какой|какие") or "18990" in digits(t),
        "asks_order_number_or_phone": lambda: r(r"номер заказа|телефон"),
        "no_status_invention": lambda: not r(r"заказ (уже )?(в пути|доставлен|передан курьеру|отправлен)"),
        "no_stock_guarantee": lambda: not r(r"точно есть|гарантир\w* наличи|100 ?%"),
        "no_reservation_confirmation": lambda: not r(r"отложил\w*|забронирова\w* (для вас|за вами)|зарезервирова\w*|бронь подтвержд|отложу"),
        "no_refund_confirmation": lambda: not r(r"верн[её]м деньги|возврат (оформлен|одобрен|сделаем)|деньги вернутся|оформим возврат"),
        "polite": lambda: not r(r"сами виноваты|не наша проблема|ваши проблемы"),
        "asks_for_whom_or_goal": lambda: r(r"для кого|реб[её]н|взросл|цель|ент|ielts|уровень|возраст"),
        "no_result_guarantee": lambda: not r(r"гарантиру|обязательно сдаст|точно сдаст|100 ?%|гарантия результата"),
        "no_time_assignment": lambda: not r(r"записал\w* вас|ждём вас в \d|назначил\w*"),
        "no_schedule_invention": lambda: not (r(r"суббот") and r(r"\d{1,2}:\d{2}")),
        "no_level_assignment": lambda: not r(r"ваш уровень|группа (intermediate|среднего)|определил\w* уровень|зачисл"),
        "mentions_diagnostics_caveat": lambda: "диагност" in low,
        "no_data_access_claim": lambda: not r(r"не заходим|не заходит|не смотрим|не открываем|не трогаем"),
        "mentions_backup": lambda: r(r"резервн|бэкап|копи"),
        "advises_power_off_or_no_charge": lambda: r(r"выключ|не заряжа"),
        "asks_text_or_clarifies": lambda: "?" in t or r(r"напишите|текстом|уточн"),
        "no_payment_confirmation": lambda: not r(r"оплат\w* (получен|принят|подтвержд)|деньги (получил|пришли|поступили)|оплату (вижу|получил)"),
        "no_free_delivery_claim": lambda: not claim_without_negation(low, r"бесплатн\w* доставк|доставка бесплатн|доставим бесплатно"),
        "no_invented_sku": lambda: all(tok.upper() in ktext.upper() for tok in re.findall(r"\b[A-Za-z0-9]+-[A-Za-z0-9-]+\b", t)),
    }
    fn = table.get(name)
    if fn is None:
        return None
    try:
        return bool(fn())
    except Exception:
        return False


def check_panel(name, sc, know, out):
    body = out.get("body") or {}
    cards = body.get("suggestions") or []
    texts = " ".join(c.get("text", "") for c in cards)
    low = texts.lower()
    kset = {digits(x) for x in re.findall(r"\d[\d\s]*", knowledge_text(sc, know))}
    allowed_digits = kset | {digits(x) for x in re.findall(r"\d[\d\s]*", client_text(sc))}
    table = {
        "abstain": lambda: out.get("status") == 200 and body.get("abstain") is True and len(cards) == 0,
        "cards": lambda: out.get("status") == 200 and 1 <= len(cards) <= 4 and not body.get("error"),
        "cards_no_upsell_on_thanks": lambda: not re.search(r"акци|скидк|рекоменду", low),
        "cards_numbers_grounded": lambda: all(digits(m.group(1)) in kset for m in AMOUNT.finditer(texts)),
        "cards_ru": lambda: sum(c in KZ for c in texts) == 0,
        "cards_kz": lambda: sum(c in KZ for c in texts) >= 2,
        "cards_first_clarifies": lambda: bool(cards) and cards[0].get("move") == "Уточнить",
        "cards_first_no_price": lambda: bool(cards) and not AMOUNT.search(cards[0].get("text", "")),
        "cards_no_invented_installment_terms": lambda: not invented_terms(low, allowed_digits),
        "cards_no_refund_confirmation": lambda: not re.search(r"верн[её]м деньги|возврат (оформлен|одобрен|сделаем)|оформим возврат", low),
    }
    fn = table.get(name)
    return None if fn is None else bool(fn())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenarios", default=os.path.join(HERE, "scenarios.v1.json"))
    ap.add_argument("--label", default="run")
    ap.add_argument("--prompts-ref", default="worktree", help="git ref for the Create WA workflow's prompt map (before/after)")
    ap.add_argument("--only")
    ap.add_argument("--repeat", type=int, default=1, help="repeats for scenarios tagged key")
    ap.add_argument("--repeat-all", action="store_true")
    ap.add_argument("--max-tokens", type=int, default=0, help="override the template's maxTokens")
    ap.add_argument("--price-in", type=float, default=0.15, help="USD per 1M prompt tokens (assumption, for the cost line)")
    ap.add_argument("--price-out", type=float, default=0.60)
    ap.add_argument("--skip-panel", action="store_true")
    a = ap.parse_args()
    d = json.load(open(a.scenarios, encoding="utf-8"))
    prompts = prompts_map(a.prompts_ref)
    set_fields_template(a.prompts_ref)
    mp = model_params("worktree")
    max_tokens = a.max_tokens or mp["max_tokens"]
    app_user_id = None
    if not a.skip_panel:
        app_user_id = subprocess.run([sys.executable, os.path.join(N8N, "staging", "fixtures.py"), "create-subscriber", "--run-id", f"eval{int(time.time())}"],
                                     capture_output=True, text=True, check=True).stdout.strip()
    results, t0 = [], time.time()
    try:
        for sc in d["scenarios"]:
            if a.only and a.only not in sc["id"]:
                continue
            if a.skip_panel and sc["mode"] == "panel":
                continue
            know = d["knowledge"][sc["vertical"]]
            reps = a.repeat if (a.repeat_all or "key" in sc.get("tags", [])) else 1
            runs = []
            for i in range(reps):
                out = run_panel(sc, know, app_user_id) if sc["mode"] == "panel" else run_auto(sc, know, mp, prompts, max_tokens)
                verdicts = {}
                for name in sc["expect"]:
                    v = check_panel(name, sc, know, out) if sc["mode"] == "panel" else check(name, sc, know, out)
                    verdicts[name] = v
                runs.append({"verdicts": verdicts, "text": (out.get("text") or "")[:600] if sc["mode"] == "auto" else json.dumps(out.get("body"), ensure_ascii=False)[:2400],
                             "tool_calls": out.get("tool_calls"), "usage": out.get("usage"), "finish_reason": out.get("finish_reason"), "error": out.get("error"),
                             "dropped_params": out.get("dropped_params")})
            passes = [all(v is not False for v in r["verdicts"].values()) for r in runs]
            rec = {"id": sc["id"], "vertical": sc["vertical"], "mode": sc["mode"], "lang": sc["lang"], "tags": sc.get("tags", []), "repeats": reps,
                   "pass_rate": sum(passes) / len(passes), "runs": runs}
            results.append(rec)
            flag = "PASS" if rec["pass_rate"] == 1 else ("FLAKY" if rec["pass_rate"] > 0 else "FAIL")
            failed = sorted({k for r in runs for k, v in r["verdicts"].items() if v is False})
            print(f"{flag:5} {sc['id']:36} {rec['pass_rate']:.2f}  {('failed: ' + ', '.join(failed)) if failed else ''}", flush=True)
    finally:
        if app_user_id:
            subprocess.run([sys.executable, os.path.join(N8N, "staging", "fixtures.py"), "delete", "--app-user-id", app_user_id], capture_output=True)
    # summary
    usage_in = sum(r["usage"]["prompt_tokens"] for x in results for r in x["runs"] if r.get("usage"))
    usage_out = sum(r["usage"]["completion_tokens"] for x in results for r in x["runs"] if r.get("usage"))
    length_cut = sum(1 for x in results for r in x["runs"] if r.get("finish_reason") == "length")
    total_runs = sum(len(x["runs"]) for x in results)
    by_check = {}
    for x in results:
        for r in x["runs"]:
            for k, v in r["verdicts"].items():
                if v is None: continue
                c = by_check.setdefault(k, [0, 0]); c[1] += 1; c[0] += (1 if v else 0)
    summary = {"label": a.label, "prompts_ref": a.prompts_ref, "scenario_version": d["version"], "harness": HARNESS_VERSION, "model": mp["model"], "max_tokens": max_tokens,
               "scenarios": len(results), "runs": total_runs, "pass_all": sum(1 for x in results if x["pass_rate"] == 1),
               "flaky": sum(1 for x in results if 0 < x["pass_rate"] < 1), "fail": sum(1 for x in results if x["pass_rate"] == 0),
               "finish_reason_length_runs": length_cut, "prompt_tokens": usage_in, "completion_tokens": usage_out,
               "cost_usd_estimate": round(usage_in / 1e6 * a.price_in + usage_out / 1e6 * a.price_out, 4), "seconds": round(time.time() - t0),
               "by_check": {k: f"{v[0]}/{v[1]}" for k, v in sorted(by_check.items())}}
    os.makedirs(os.path.join(HERE, "results"), exist_ok=True)
    stem = os.path.join(HERE, "results", re.sub(r"[^A-Za-z0-9_.-]", "_", a.label))
    json.dump({"summary": summary, "results": results}, open(stem + ".json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    md = [f"# Eval {a.label} — {d['version']}", "", f"prompts: `{a.prompts_ref}` · model `{mp['model']}` · max_tokens {max_tokens} · {summary['scenarios']} scenarios / {total_runs} runs · {summary['seconds']} s",
          f"pass {summary['pass_all']} · flaky {summary['flaky']} · fail {summary['fail']} · finish_reason=length in {length_cut} runs · tokens {usage_in}+{usage_out} ≈ ${summary['cost_usd_estimate']} (at ${a.price_in}/${a.price_out} per 1M — assumption)", "",
          "| scenario | vertical | mode | pass rate | failed checks |", "|---|---|---|---|---|"]
    for x in results:
        failed = sorted({k for r in x["runs"] for k, v in r["verdicts"].items() if v is False})
        md.append(f"| {x['id']} | {x['vertical']} | {x['mode']} | {x['pass_rate']:.2f} ({x['repeats']}×) | {', '.join(failed)} |")
    md += ["", "## By check", "", "| check | pass/total |", "|---|---|"] + [f"| {k} | {v} |" for k, v in summary["by_check"].items()]
    open(stem + ".md", "w", encoding="utf-8").write("\n".join(md) + "\n")
    print("\n" + json.dumps(summary, ensure_ascii=False, indent=1))
    print("wrote", os.path.relpath(stem + ".md", REPO))


if __name__ == "__main__":
    main()
