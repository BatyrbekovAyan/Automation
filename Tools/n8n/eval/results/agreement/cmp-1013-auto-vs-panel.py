#!/usr/bin/env python3
"""Same client message → the AUTO reply (template model/prompt via the staging EvalRun proxy) and the PANEL's card #1
(staging /webhook/SuggestReplies), judged by gpt-5.4 for «same thinking». Staging only. Writes results JSON + MD.

The panel on staging cannot reach the scenario's price-list documents (its RAG reads the real store with a dummy bot id),
so the documents are appended to the panel's knowledge description — both sides get the same facts.
"""
import copy, importlib.util, json, os, subprocess, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

REPO = "/Users/ayan/Projects/Automation"
spec = importlib.util.spec_from_file_location("runeval", os.path.join(REPO, "Tools/n8n/eval/run-eval.py"))
E = importlib.util.module_from_spec(spec); spec.loader.exec_module(E)

IDS = ["ap_price_without_car", "ap_price_with_car", "ap_unknown_part", "ap_handoff_ok",
       "ws_discount_request", "ws_kz_min_order", "ws_deferred_payment", "ws_handoff_order",
       "fl_recommend", "fl_absent_item", "fl_order_steps", "fl_kz_price", "fl_thanks_in_dialog",
       "ks_variant_clarify", "ks_where_is_order", "ks_complaint", "ks_reserve",
       "ed_price_no_details", "ed_trial_signup", "pr_water", "pr_handoff_visit", "pr_price_with_model"]
REPEATS = int(os.environ.get("REPEATS", "3"))
# Ad-hoc pairs: the owner HAS written the policy into «Промпт» — do both sides follow it the same way?
EXTRA = {"ws_discount_policy": ("ws_discount_request", "Скидок не даём ни на какой объём — цены фиксированные."),
         "ks_reserve_policy": ("ks_reserve", "Отложить товар можем до 20:00 того же дня, забрать можно в нашем пункте выдачи.")}
if os.environ.get("IDS"): IDS = os.environ["IDS"].split(",")
OUT = sys.argv[1]

d = json.load(open(os.path.join(REPO, "Tools/n8n/eval/scenarios.v1.json"), encoding="utf-8"))
SC = {s["id"]: s for s in d["scenarios"]}
for _xid, (_base, _owner) in EXTRA.items():
    SC[_xid] = dict(copy.deepcopy(SC[_base]), id=_xid, owner_prompt=_owner)
prompts = E.prompts_map("worktree")
mp = E.model_params("worktree")
marks = E.memory_marks("worktree")
app_user_id = subprocess.run([sys.executable, os.path.join(REPO, "Tools/n8n/staging/fixtures.py"), "create-subscriber", "--run-id", f"avp{int(time.time())}"],
                             capture_output=True, text=True, check=True).stdout.strip()


def panel_know(know, sc):
    k = copy.deepcopy(know)
    docs = sc.get("docs_override", know["docs"])
    if docs:
        k["business"] = k["business"].split("\n\nКонтакты:")[0] + "\n\nИз прайс-листа:\n" + "\n".join(docs) + \
                        ("\n\nКонтакты:" + know["business"].split("\n\nКонтакты:")[1] if "\n\nКонтакты:" in know["business"] else "")
    return k


JUDGE_SCHEMA = {"type": "json_schema", "json_schema": {"name": "verdict", "strict": True, "schema": {
    "type": "object", "additionalProperties": False, "required": ["action_auto", "action_panel", "verdict", "why"],
    "properties": {
        "action_auto": {"type": "string", "enum": ["answer", "clarify", "answer_and_clarify", "handoff_or_order", "defer_to_owner", "refuse", "abstain", "other"]},
        "action_panel": {"type": "string", "enum": ["answer", "clarify", "answer_and_clarify", "handoff_or_order", "defer_to_owner", "refuse", "abstain", "other"]},
        "verdict": {"type": "string", "enum": ["same", "close", "different"]},
        "why": {"type": "string"}}}}}

JUDGE_SYS = """Ты сравниваешь два ответа одного и того же бизнеса на одно сообщение клиента.
A — ответ, который бот отправил бы сам (режим «Авто»). B — первая (рекомендованная) подсказка, которую панель предлагает владельцу отправить (режим «Вместе»). «(панель молчит)» значит, что панель решила не предлагать ответ.
Владелец хочет, чтобы «Авто» думал так же, как панель. Оцени, совпадают ли A и B ПО СУТИ — действие и факты, а не формулировки:
- same: одно и то же действие и те же факты (цены, наличие, условия, что спрашивают у клиента); различаются только слова, длина, вежливые обороты.
- close: то же действие, но заметно разное наполнение: один называет цену/факт, которого нет у другого; один задаёт уточнение, другой нет; разные варианты товара.
- different: разные действия (ответить ценой против уточнить; передать владельцу/оформить против ответить; ответить против промолчать; отказать против согласиться) или противоречащие факты.
Учти: бот в «Авто» не может сам принять заказ и поэтому передаёт его владельцу, а подсказка написана от имени владельца и может сразу подтвердить заказ. Если по сути это одно и то же действие с заказом (принять/оформить заказ), считай действие одинаковым — handoff_or_order.
Так же: если B от имени владельца откладывает решение («уточню и вернусь», «проверю и напишу») или спрашивает деталь, нужную владельцу для решения (объём, номер заказа), а A передаёт этот вопрос владельцу, — это одно действие: решение за владельцем, defer_to_owner. Если же B сам соглашается или отказывает по условию, а A передаёт вопрос владельцу (или наоборот), — это разные действия.
why — одно короткое предложение по-русски: в чём главное расхождение (или «совпадают»)."""


def judge(sc, know, a, b):
    client = "\n".join(f"{'Клиент' if r == 'client' else 'Бизнес'}: {t}" for r, t in sc["messages"])
    user = f"Данные бизнеса:\n{know['business']}\nКаталог:\n{know['products']}\n{know['services']}\nПрайс:\n" + "\n".join(know["docs"]) + \
           f"\n\nПереписка:\n{client}\n\nA (Авто):\n{a}\n\nB (подсказка №1):\n{b}"
    req = {"model": "gpt-5.4", "temperature": 0, "max_completion_tokens": 300, "response_format": JUDGE_SCHEMA,
           "messages": [{"role": "system", "content": JUDGE_SYS}, {"role": "user", "content": user}]}
    st, body = E.http("POST", f"{E.BASE}/webhook/EvalRun", {"request": req})
    try:
        return json.loads(body["body"]["choices"][0]["message"]["content"])
    except Exception:
        return {"verdict": "error", "why": str(body)[:200], "action_auto": "other", "action_panel": "other"}


def one(task):
    sid, rep = task
    sc = SC[sid]; know = d["knowledge"][sc["vertical"]]
    a = E.run_auto(sc, know, mp, prompts, mp["max_tokens"], marks=marks)
    p = E.run_panel(sc, panel_know(know, sc), app_user_id)
    body = p.get("body") or {}
    cards = body.get("suggestions") or []
    b_text = cards[0]["text"] if cards else "(панель молчит)"
    v = judge(sc, know, a.get("text") or "", b_text)
    return {"id": sid, "rep": rep, "vertical": sc["vertical"], "client": " / ".join(t for r, t in sc["messages"] if r == "client"),
            "auto": a.get("text"), "auto_tools": [c["name"] for c in a.get("tool_calls") or []], "panel_status": p.get("status"),
            "panel_card1": b_text, "panel_move1": cards[0].get("move") if cards else None, "panel_cards": [c.get("text") for c in cards],
            **v}


if os.environ.get("REJUDGE"):          # re-score saved pairs with the CURRENT rubric (no new generation)
    saved = json.load(open(os.environ["REJUDGE"], encoding="utf-8"))
    def rj(r):
        sc = SC[r["id"]]; know = d["knowledge"][sc["vertical"]]
        return dict(r, **judge(sc, know, r.get("auto") or "", r["panel_card1"]))
    with ThreadPoolExecutor(max_workers=4) as ex:
        rows = list(ex.map(rj, saved))
    json.dump(rows, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    from collections import Counter
    subprocess.run([sys.executable, os.path.join(REPO, "Tools/n8n/staging/fixtures.py"), "delete", "--app-user-id", app_user_id], capture_output=True)
    print("verdicts:", Counter(r["verdict"] for r in rows)); sys.exit(0)
tasks = [(sid, r) for r in range(REPEATS) for sid in IDS]
try:
    with ThreadPoolExecutor(max_workers=4) as ex:
        rows = list(ex.map(one, tasks))
finally:
    subprocess.run([sys.executable, os.path.join(REPO, "Tools/n8n/staging/fixtures.py"), "delete", "--app-user-id", app_user_id], capture_output=True)
json.dump(rows, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
from collections import Counter
print("verdicts:", Counter(r["verdict"] for r in rows))
print("panel status:", Counter(r["panel_status"] for r in rows))
for r in rows:
    print(f"{r['id']:24} r{r['rep']} {r['verdict']:9} A:{r['action_auto']:18} B:{r['action_panel']:18} {r['why'][:110]}")
