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
                    search (returns the vertical's documents — and, when the template at --prompts-ref routes
                    the search through the Price List Search sub-workflow (V9, 2026-09-28), runs THAT
                    workflow's own `Reconcile Prices` Code over them, so a before/after measures the real
                    pipeline). finish_reason / usage are recorded.
  panel scenarios — POSTed to the staging /webhook/SuggestReplies with the v1.5 knowledge object.

Scenario roles (h7, set v1.4): `client` → user; `business` → the bot's own earlier (delivered) reply, an unmarked assistant
turn; `owner` → the owner's reply and `bot_unknown` → the bot's reply of unknown delivery, both assistant turns opening with
the mark the memory WRITERS store at --prompts-ref (OWNER_MEMORY_PREFIX / UNDELIVERED_MEMORY_PREFIX of
apply-history-handoff.py, parsed at that ref — none before T6, so a «before» on the prod ref sees prod's unmarked memory).

    python3 Tools/n8n/eval/run-eval.py --label after                  # working-tree prompts
    python3 Tools/n8n/eval/run-eval.py --label before --prompts-ref 805311ee
    python3 Tools/n8n/eval/run-eval.py --only ap_ --repeat 3 --max-tokens 320
    python3 Tools/n8n/eval/run-eval.py --label luna --model gpt-6-luna --reasoning-effort none --skip-panel

--model swaps ONLY the auto path's model (the panel runs whatever model the staging Suggest Replies
carries — see panel-model.py). GPT-6 defaults to reasoning «medium», and Chat Completions (what the
templates' lmChatOpenAi v1.2 node calls) serves function calling ONLY at reasoning_effort=none, so a
GPT-6 run without --reasoning-effort none measures a configuration the bot cannot use.

Results: Tools/n8n/eval/results/<label>.json + .md (synthetic data only).

The deterministic checks are measured themselves by test_checks.py (right/wrong phrase table, no network) — run it
after touching a check, and bump HARNESS_VERSION. rescore.py re-scores saved runs with the current checks offline
(writes <label>.<harness>.json/.md, never overwrites); numbers from different harness versions never mix.
"""
import argparse
import ast
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
STAGING_SUGGEST = "Tools/n8n/workflows-staging/9PTyYcelRQI7bGDb-Suggest_Replies.json"
PRICE_SEARCH = "Tools/n8n/workflows/crPriceSearch001-Price_List_Search.json"
# T6 (Q1 B + V6-unknown): the memory writers put the marks into the content the model reads; their strings live here
MARKS_SRC = "Tools/n8n/apply-history-handoff.py"
MARK_NAMES = {"owner": "OWNER_MEMORY_PREFIX", "bot_unknown": "UNDELIVERED_MEMORY_PREFIX"}
KZ = set("әіңғүұқөһӘІҢҒҮҰҚӨҺ")
# USD per 1M tokens (input, cached input, output) — OpenAI Standard tier as published on
# developers.openai.com/api/docs/pricing, read 2026-09-25. Reasoning tokens are billed as output.
PRICES = {
    "gpt-4o-mini": (0.15, 0.075, 0.60),
    "gpt-5.4-mini": (0.75, 0.075, 4.50),
    "gpt-5.4": (2.50, 0.25, 15.00),
    "gpt-6-luna": (0.10, 0.01, 0.50),
    "gpt-6-sol": (2.00, 0.20, 10.00),
    "gpt-6-astra": (10.00, 1.00, 50.00),
}


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
    except (urllib.error.URLError, TimeoutError, ConnectionError) as e:   # the proxy unreachable / timed out: status 0
        return 0, str(e)


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


def memory_marks(ref):
    """h7 (T11): the memory marks the WRITERS store at `ref` — the module-level string literals of apply-history-handoff.py,
    parsed (never executed) at that ref, the way --search-ref reads the tool. A ref before T6 has no such constants: its
    owner reply and its reply of unknown delivery reach the model unmarked, exactly like the bot's own delivered reply."""
    marks = {role: "" for role in MARK_NAMES}
    try:
        tree = ast.parse(file_at(ref, MARKS_SRC))
    except (subprocess.CalledProcessError, OSError, SyntaxError):
        return marks
    found = {}
    for node in tree.body:
        if isinstance(node, ast.Assign) and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str):
            for t in node.targets:
                if isinstance(t, ast.Name):
                    found[t.id] = node.value.value
    for role, name in MARK_NAMES.items():
        marks[role] = found.get(name, "")
    return marks


# scenario role -> (chat role, mark key): `business` is the bot's own earlier reply as memory stores it (delivered, unmarked);
# `owner` is the owner's reply (owner_app / owner_phone rows), `bot_unknown` the bot's reply whose delivery is unknown
ROLES = {"client": ("user", None), "business": ("assistant", None), "owner": ("assistant", "owner"), "bot_unknown": ("assistant", "bot_unknown")}


def render_messages(sc, marks):
    """The scenario's turns as the agent's memory hands them to the model: role + content, the writers' mark (if the measured
    ref has one) at the start of the content."""
    out = []
    for role, text in sc["messages"]:
        if role not in ROLES:
            raise ValueError(f"{sc['id']}: unknown role {role!r}")
        chat_role, mark = ROLES[role]
        out.append({"role": chat_role, "content": (marks.get(mark, "") if mark else "") + text})
    return out


def search_code(ref, bot_wf):
    """V9: the template's price-list tool is either n8n's retrieve-as-tool (the hits reach the model verbatim) or the
    Price List Search sub-workflow — then the hits pass through ITS `Reconcile Prices` Code at the same ref."""
    tool = next(n for n in bot_wf["nodes"] if n["name"] == "Supabase Vector Store")
    if tool["type"] != "@n8n/n8n-nodes-langchain.toolWorkflow" or tool["parameters"]["workflowId"]["value"] != "crPriceSearch001":
        return None
    sub = json.loads(file_at(ref, PRICE_SEARCH))
    return next(n for n in sub["nodes"] if n["name"] == "Reconcile Prices")["parameters"]["jsCode"]


def model_params(ref):
    wf = json.loads(file_at(ref, BOT_WA))
    n = next(n for n in wf["nodes"] if n["name"] == "OpenAI")
    o = n["parameters"].get("options", {})
    tp = next(n for n in wf["nodes"] if n["name"] == "Supabase Vector Store")["parameters"]
    tool_desc = tp.get("toolDescription") or tp.get("description")   # retrieve-as-tool | toolWorkflow (the same text)
    handoff_desc = next(n for n in wf["nodes"] if n["name"] == "Create Handoff")["parameters"]["description"]
    return {"model": n["parameters"]["model"]["value"], "temperature": o.get("temperature", 0.2), "frequency_penalty": o.get("frequencyPenalty", 0),
            "max_tokens": o.get("maxTokens", 200), "search_desc": tool_desc, "handoff_desc": handoff_desc, "search_code": search_code(ref, wf)}


_HARNESS = None


def simulate_search(docs, system, mp):
    """The price-list search as the model receives it: the scenario's documents (the vector store's top hits), reconciled
    by the measured ref's own Code node when the template routes there — executed, never re-implemented here."""
    hits = list(docs[:5])
    if not mp.get("search_code"):
        return {"documents": hits}
    global _HARNESS
    if _HARNESS is None:
        import importlib.util
        spec = importlib.util.spec_from_file_location("harness", os.path.join(N8N, "tests", "harness.py"))
        _HARNESS = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_HARNESS)
    named = {"Guard": {"ok": True, "query": "", "filterKey": "botWaId", "botId": "evalScenario0001", "prompt": system},
             "Search Price Lists": [{"document": {"pageContent": d, "metadata": {}}, "score": 0.8} for d in hits] or [{}]}
    return _HARNESS.run_js(mp["search_code"], {"json": {}}, named)[0]["json"]


TOOLS = lambda mp: [
    {"type": "function", "function": {"name": "supabase_vector_store", "description": mp["search_desc"],
                                      "parameters": {"type": "object", "properties": {"input": {"type": "string", "description": "search query"}}, "required": ["input"]}}},
    {"type": "function", "function": {"name": "create_handoff", "description": mp["handoff_desc"],
                                      "parameters": {"type": "object", "properties": {"reason": {"type": "string", "enum": ["order", "question", "escalation"]},
                                                                                       "name": {"type": "string"}, "phone": {"type": "string"}, "summary": {"type": "string"}},
                                                     "required": ["reason", "summary"]}}},
]


class QuotaExhausted(RuntimeError):
    """OpenAI answered insufficient_quota — the run stops at once instead of scoring empty answers (the owner tops up)."""


def post_completion(req, attempts=4):
    """One completion through the staging proxy. A TRANSPORT failure (OpenAI 429 rate limit, a 5xx, the proxy itself
    unreachable) is retried with backoff — it says nothing about the model and would otherwise be scored as an empty
    answer; an exhausted quota stops the run. Returns (http status, body, retries used)."""
    for i in range(attempts + 1):
        st, d = http("POST", f"{BASE}/webhook/EvalRun", {"request": req})
        body = (d or {}).get("body") if isinstance(d, dict) else None
        err = ((body or {}).get("error") or {}) if isinstance(body, dict) else {}
        if isinstance(err, dict) and (err.get("code") == "insufficient_quota" or err.get("type") == "insufficient_quota"):
            raise QuotaExhausted(str(err.get("message", err))[:200])
        upstream = d.get("status") if isinstance(d, dict) else None
        transient = st == 0 or st >= 500 or upstream == 429 or (isinstance(upstream, int) and upstream >= 500)
        if not transient or i == attempts:
            return st, d, i
        time.sleep(min(30, 2 ** (i + 1)))
    return st, d, attempts


def run_auto(sc, know, mp, prompts, max_tokens, effort=None, drop=(), marks=None):
    vertical = prompts.get(sc["vertical"], "")
    business = sc.get("business_override", know["business"])
    system = compose(vertical, know["businessType"], sc.get("owner_prompt", ""), business, know["products"], know["services"])
    docs = sc.get("docs_override", know["docs"])
    marks = marks if marks is not None else {role: "" for role in MARK_NAMES}
    messages = [{"role": "system", "content": system}] + render_messages(sc, marks)
    tool_calls, finish, text = [], None, ""
    usage = {"prompt_tokens": 0, "completion_tokens": 0, "cached_tokens": 0, "reasoning_tokens": 0}
    dropped = set(drop)
    retries = 0
    for _round in range(4):
        # gpt-5.x takes max_completion_tokens; a parameter the model rejects is dropped and the call retried (recorded)
        req = {"model": mp["model"], "temperature": mp["temperature"], "frequency_penalty": mp["frequency_penalty"],
               "max_completion_tokens": max_tokens, "messages": messages, "tools": TOOLS(mp)}
        if effort:
            req["reasoning_effort"] = effort
        for k in dropped:
            req.pop(k, None)
        st, d, n_retry = post_completion(req)
        retries += n_retry
        body = (d or {}).get("body") if isinstance(d, dict) else None
        err = ((body or {}).get("error") or {}) if isinstance(body, dict) else {}
        if isinstance(err, dict) and err.get("code") == "unsupported_parameter" and err.get("param") and err["param"] not in dropped:
            dropped.add(err["param"])
            continue
        if st != 200 or not body or "choices" not in body:
            return {"error": f"proxy {st}: {str(d)[:200]}", "text": "", "tool_calls": tool_calls, "usage": usage, "finish_reason": None, "system_chars": len(system),
                    "dropped_params": sorted(dropped), "transport_retries": retries}
        u = body.get("usage") or {}
        usage["prompt_tokens"] += u.get("prompt_tokens", 0); usage["completion_tokens"] += u.get("completion_tokens", 0)
        usage["cached_tokens"] += (u.get("prompt_tokens_details") or {}).get("cached_tokens") or 0
        usage["reasoning_tokens"] += (u.get("completion_tokens_details") or {}).get("reasoning_tokens") or 0
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
                    result = simulate_search(docs, system, mp)
                messages.append({"role": "tool", "tool_call_id": tc["id"], "content": json.dumps(result, ensure_ascii=False)})
            continue
        text = msg.get("content") or ""
        break
    return {"text": text, "tool_calls": tool_calls, "usage": usage, "finish_reason": finish, "system_chars": len(system), "dropped_params": sorted(dropped),
            "transport_retries": retries}


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


def staging_panel_model():
    """The model the staging Suggest Replies was imported with (panel-model.py rewrites the derived file before importing it)."""
    try:
        wf = json.load(open(os.path.join(REPO, STAGING_SUGGEST), encoding="utf-8"))
        body = next(n for n in wf["nodes"] if n["name"] == "LLM")["parameters"]["jsonBody"]
        m = re.search(r'"model":"([^"]+)"', body)
        e = re.search(r'"reasoning_effort":"([^"]+)"', body)
        return (m.group(1) if m else "?") + (f" (reasoning_effort {e.group(1)})" if e else "")
    except Exception:
        return "?"


def latency_stats(xs):
    if not xs:
        return None
    xs = sorted(xs)
    return {"n": len(xs), "median": xs[len(xs) // 2], "p90": xs[min(len(xs) - 1, int(len(xs) * 0.9))], "max": xs[-1]}


# ---- deterministic checks -------------------------------------------------------------------
# h4: «18,000 тг» / «18.000 тг» are 18000 (h3 read «000»)
AMOUNT = re.compile(r"(\d{1,3}(?:[,.\s]\d{3})+(?!\d)|\d[\d\s]{2,})\s?(тг|₸|тенге|теңге)", re.I)


def digits(s): return re.sub(r"\D", "", s or "")


def knowledge_text(sc, know):
    return " ".join([sc.get("business_override", know["business"]), know["products"], know["services"], " ".join(sc.get("docs_override", know["docs"])), sc.get("owner_prompt", "")])


HARNESS_VERSION = ("h7 (2026-10-12, T11 Q1 B + V6-unknown: scenario roles `owner` / `bot_unknown` rendered with the memory marks the writers store at --prompts-ref (apply-history-handoff.py; none before T6); no_marker_leak, no_tool:<name>, not_says_already, no_delivery_doubt; on top of "
                   "h6 2026-10-09, Q1: not_mentions:X — X stated in no un-refused clause and not merely quoted as the client's words, for the client-time echo; mentions:H:MM "
                   "reads a time of day; on top of "
                   "h5 2026-10-08, V9-r2: no_conflict_claim / no_price_caveat for a price list that AGREES with the catalog; on top of "
                   "h4 2026-10-07 V9: «расхождение» + negated conflict, zero-price / elliptical free delivery, «итого по N позициям» partial, "
                   "«нужен» / the car or device by name, a word inside the installment claim, Kazakh = no Russian sentence, «18,000»; "
                   "h3 2026-09-27 R14; checks pinned by test_checks.py)")


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


# ---- h3 (R14, 2026-09-27) --------------------------------------------------------------------
# Every h3 helper answers a question about ONE place in the answer. The h2 defects were all the same shape: a
# keyword anywhere in the text decided the verdict («наличие подтвердит менеджер» resolved a price conflict,
# «менеджер» anywhere un-confirmed a firm total, «год» inside «сегодня» asked for the car), a substring hit the
# wrong word («платн» inside «бесплатн» negated every free-delivery claim, so that check could never fail), or
# all digits of the text were glued into one string («2019, 3 шт, 200» contained «3200»).
# test_checks.py holds the right/wrong phrase table these helpers are measured against — extend it first.
SENTENCE_SPLIT = re.compile(r"(?<=[.!?…])\s+|\n+")


def sentences(text):
    return [s.strip() for s in SENTENCE_SPLIT.split(text or "") if s.strip()]


# numbers are read one at a time: «3 900» = 3900, «50 мың» = 50000; «2019, 3 шт, 200» is 2019, 3 and 200
NUM = re.compile(r"(?<!\d)(\d{1,3}(?:[   ,]\d{3})+(?!\d)|\d+)(?:\s?(мың|тыс\.?|тысяч\w*|млн|миллион\w*))?", re.I)


def numbers(text):
    out = set()
    for m in NUM.finditer(text or ""):
        unit = (m.group(2) or "").lower()
        out.add(int(digits(m.group(1))) * (1_000_000 if unit.startswith(("млн", "миллион")) else 1000 if unit else 1))
    return out


CLOCK = re.compile(r"(\d{1,2})[:.](\d{2})")


def _clock_named(t, want):
    """h6: a clock time «H:MM» is named as a time — «15:00» / «15.00» (leading zero optional) and, for a full hour, «15 ч» /
    «15 часов» or a bare «в/к/до/с/на 15» that ends the phrase. None = `want` is not a clock time. Until h6 «mentions:15:00»
    read the digits as ONE number (1500) and could never pass."""
    m = CLOCK.fullmatch(want.strip())
    if not m:
        return None
    h, mm = int(m.group(1)), m.group(2)
    pats = [rf"(?<!\d)0?{h}[:.]{mm}(?!\d)"]
    if mm == "00":
        pats += [rf"(?<![\d:.])0?{h}\s*(?:ч\b|час\w*)",
                 rf"\b(?:в|к|до|с|на)\s+0?{h}(?=\s*(?:$|[.!?,;)\n]|утра|дня|вечера|ночи))"]
    return any(re.search(p, t or "", re.I) for p in pats)


def mentions(t, want):
    """mentions:N — the answer names the number N (or N thousand / N million, how «50 000» and «50 мың» are written);
    mentions:H:MM — the time of day; mentions:<word> — a case-insensitive substring."""
    clock = _clock_named(t, want)
    if clock is not None:
        return clock
    if not digits(want):
        return want.lower() in t.lower()
    n = int(digits(want))
    return bool({n, n * 1000, n * 1_000_000} & numbers(t))


# h6 (Q1, 2026-10-09): «не позже 15:00» bounds the time and «не опаздывайте / не забудьте» insist on it — neither refuses it
_CLAUSE_SPLIT = re.compile(r"[,;—–]|:(?!\d)")
_BOUND = re.compile(r"\bне\s+(?:позже|позднее|раньше|ранее|опаздыва\w*|забуд\w*)\b")
# the client's own words, quoted as such: reported speech («вы писали, что …») or «ваш запрос / ваша заявка …»
_CLIENT_WORDS = re.compile(r"\bвы\s+(?:\S+\s+){0,2}?(?:писали|написали|предлагали|предложили|хотели|указали|говорили|сказали"
                           r"|планируете|планировали|собирались)\b[^.!?]*?\bчто\b"
                           r"|\bваш(?:а|у|е|и|его|ей)?\s+(?:запрос|заявк|пожелани|просьб|предложени)\w*", re.I)
_ACCEPTS = re.compile(r"\bприня[тл]\w*|подтверждаю|подтвержд[её]н\w*|согласован\w*|договорились|записал\w*|оформил\w*|оформлен\w*"
                      r"|\bжд[её]м\b|забронир\w*", re.I)


def not_mentions(t, want):
    """not_mentions:X — the answer does not STATE X: X (as mentions: reads it) stands in no clause that does not refuse it.
    «15:00 не подтверждаю» / «Не могу подтвердить 15:00 — …» name nothing; «Нет, приходите в 15:00» (the «нет» is another
    clause), «Можете подойти в 15:00, но время подтвердит мастер» and «Приходите не позже 15:00» state it. A sentence that
    QUOTES the client («Вы писали, что придёте в 15:00 — это подтвердит мастер», «передам ваш запрос на 15:00») states nothing,
    unless it also accepts it («Ваш запрос на 15:00 принят», «…— подтверждаю», «ждём»). Built for the Q1 negative controls,
    where the CLIENT named the time and an answer serving it back as the plan is the echo the Q1 design calls the
    unacceptable error. When a control fails, read its text."""
    for s in sentences(t):
        if _CLIENT_WORDS.search(s) and not _ACCEPTS.search(s):
            continue
        for clause in _CLAUSE_SPLIT.split(s):
            if mentions(clause, want) and not re.search(r"\b(?:не|нет|ни)\b", _BOUND.sub(" ", clause.lower())):
                return False
    return True


# -- free delivery: the claim is a phrase, and its negation belongs to that phrase, not to the sentence --------
_DELIVERY = r"(?:доставк|доставл|доставим|доставят|доставит|доставлю|довез|довоз|привез|привоз)\w*"
_FREE = r"бесплатн\w*"
# gap words stay inside one clause (no , ; . ! ?) and never cross a conjunction or a preposition that makes the
# delivery word a circumstance of something else («замена масла бесплатно ПРИ доставке»)
_GAP = r"(?:\s+(?!(?:и|а|но|или|при|после|до|для|за)\b)[^\s,;.!?]+)"
FREE_DELIVERY = re.compile(rf"{_FREE}{_GAP}{{0,2}}?\s+{_DELIVERY}|{_DELIVERY}{_GAP}{{0,6}}?\s+{_FREE}")


def _negated(sentence, m):
    """The claim phrase `m` is negated IN ITS OWN CLAUSE: «не/нет/ни» inside the phrase («доставка не бесплатная»,
    «бесплатно не доставляем»), up to two words before it («у нас нет бесплатной доставки», «заявка не передана», «не
    могу зарезервировать»), or «не/нет» after it («бесплатная доставка у нас не предусмотрена», «зарезервировать товар
    не могу»). A negation in another clause negates nothing: «Доставка по городу бесплатная, не переживайте» and «Нет,
    доставка бесплатная» (the answer to «доставка платная?») are both promises. «без» is not a negation of the phrase
    («Без проблем отложу до вечера» is a promise)."""
    if re.search(r"\b(?:не|нет|ни)\b", m.group(0)):
        return True
    before = re.split(r"[,;:—–]", sentence[:m.start()])[-1]
    if re.search(r"\b(?:не|нет|ни)\b", " ".join(before.split()[-2:])):
        return True
    return re.search(r"\b(?:не|нет)\b", re.split(r"[,;:—–]", sentence[m.end():], maxsplit=1)[0]) is not None


def affirmed(low, pattern):
    """Some match of `pattern` stands UN-negated in its clause — how every «no_<promise>» check reads its phrase list.
    h2 matched the bare phrase, so the exactly-right answer of a scenario failed its own check: «Результат не
    гарантируем» (ed_guarantee), «Заявка не передана» (ap_handoff_fail), «Зарезервировать товар не могу» (ks_reserve)."""
    return any(not _negated(s, m) for s in sentences(low) for m in re.finditer(pattern, s))


# h4: an elliptical second clause that carries only «бесплатно» («Доставка 1500 тг, а при заказе от 10 000 тг бесплатно») is
# still about the delivery — unless it names a subject of its own («…, а консультация бесплатно») or negates it
_OWN_SUBJECT = re.compile(r"диагностик|консультац|пробн|урок|замер|примерк|упаковк|открытк|установк|сборк|подбор|осмотр|замен|выезд"
                          r"|обслуживан|настройк|сервис|гарант")


def _elliptical_free(s):
    d = re.search(_DELIVERY, s)
    if not d:
        return False
    rest = s[d.end():]
    for m in re.finditer(r"[,;—–]\s*(?:а|но|однако)\b[^,;]*?\bбесплатн\w*", rest):
        clause = m.group(0)
        tail = re.split(r"[,;.!?]", rest[m.end():], maxsplit=1)[0]
        if _OWN_SUBJECT.search(clause) or re.search(_DELIVERY, clause) or re.search(r"\b(?:не|нет|ни)\b", clause + " " + tail):
            continue
        return True
    return False


def free_delivery_claim(text):
    """True = the text PROMISES free delivery somewhere (an unnegated claim phrase in some sentence). The one
    question that is not a claim is an echo answered «нет» right after it: «Бесплатная доставка? Нет, от 1500 тг»."""
    ss = sentences((text or "").lower())
    for i, s in enumerate(ss):
        if s.endswith("?") and i + 1 < len(ss) and re.match(r"нет\b", ss[i + 1]):
            continue
        for m in FREE_DELIVERY.finditer(s):
            # plus the existential «бесплатной доставки, к сожалению, нет» — its «нет» may sit past a comma
            existential = re.search(r"бесплатной|доставки", m.group(0)) and re.search(r"\bнет\b", s[m.end():])
            if not (_negated(s, m) or existential):
                return True
        # h4: a ZERO price in the delivery's own clause («Доставка — 0 тг»)
        if any(re.search(_DELIVERY + r"[^0-9]{0,30}?(?<!\d)0\s?(?:тг|₸|тенге|теңге)", c) for c in re.split(r"[,;]", s)):
            return True
        if _elliptical_free(s):
            return True
    return False


def business_facts(sc, know):
    """The business's own facts: About Business, the catalog, the price lists. NOT the owner prompt — that is a
    style wish ranked below the facts (_core.md ПРИОРИТЕТ), and in gen_owner_pref_vs_fact it literally asks for
    the lie, so reading it as data would make the free-delivery check empty in its only scenario again."""
    return "\n".join([sc.get("business_override", know["business"]), know["products"], know["services"],
                      "\n".join(sc.get("docs_override", know["docs"]))])


# -- catalog vs price-list conflict --------------------------------------------------------------------------
SEARCH_TOOL = "supabase_vector_store"
_SKU = re.compile(r"\b[A-Za-z0-9]+-[A-Za-z0-9-]+\b")


def catalog_items(know):
    """(name, price digits) for every «ProductN:/ServiceN:» of the manual catalog."""
    items = []
    for block in (know.get("products", ""), know.get("services", "")):
        names = dict(re.findall(r"^((?:Product|Service)\d+): (.+)$", block, re.M))
        for key, price in re.findall(r"^((?:Product|Service)\d+) Price: (.+)$", block, re.M):
            a = AMOUNT.search(price)
            if key in names:
                items.append((names[key], digits(a.group(1) if a else price)))
    return items


def conflict_prices(sc, know):
    """[(catalog price, price-list price)] for the same SKU where the two sources disagree — read from the
    scenario's own data (ap_source_conflict: 3200 vs 3900), never a constant."""
    pairs, items = [], catalog_items(know)
    for line in sc.get("docs_override", know["docs"]):
        for seg in line.split(";"):
            a = AMOUNT.search(seg)
            if not a:
                continue
            listed = digits(a.group(1))
            for sku in _SKU.findall(seg):
                for name, price in items:
                    if sku.upper() in name.upper() and price and price != listed and (price, listed) not in pairs:
                        pairs.append((price, listed))
    return pairs


_PRICE_WORD = re.compile(r"цен|стоимост")
_CONFLICT_STRONG = re.compile(r"расхо(?:д|жд)\w*")   # h4: «расхождение» too; «не совпада…» is checked apart (its «не» IS the claim)
_CONFLICT_WEAK = re.compile(r"отлича|различа|разн|друг(?:ая|ую|ой|ое)\b|две цены|по-разному")
_SOURCE_WORD = re.compile(r"каталог|прайс|источник|файл|списк|баз[аеуы]\b")
_VERIFY = r"(?:уточн|подтверд|подтвержд|сверит|сверя|провер)"


def conflict_named(low):
    """The answer says the prices DISAGREE: «цены расходятся / расхождение / не совпадают», or a weaker «отличается / другая
    / две цены» together with the sources («в каталоге цена другая»). «В каталоге и в прайсе позиция есть» names two
    sources, not a discrepancy; «цена разная в зависимости от партии» invents a reason; h4: a NEGATED one («расхождений
    нет», «цены не расходятся») names nothing."""
    def strong(s):
        return any(not _negated(s, m) for m in _CONFLICT_STRONG.finditer(s)) or re.search(r"\bне совпада", s) is not None
    return any(_PRICE_WORD.search(s) and (strong(s) or (_CONFLICT_WEAK.search(s) and _SOURCE_WORD.search(s)))
               for s in sentences(low))


def price_caveat(low):
    """A caveat about the PRICE itself («точную цену подтвердит владелец», «цену и наличие уточнит менеджер») — never
    one about availability («цена 3200 тг, наличие подтвердит менеджер» is a caveat about stock)."""
    def about_price(gap):
        gap = re.sub(r"^\s*и\s+(?:налич|остат)\w*", "", gap)
        return not re.search(r"[,;]|налич|остат", gap)
    # anchored at EVERY occurrence of the first word: a plain finditer skips a «цену» that sits inside an earlier,
    # rejected match («цена 3200 тг и 3900 тг, цену и наличие уточнит менеджер»)
    for first, tail in ((r"цен|стоимост", rf"\w*([^.!?\n]{{0,40}}?){_VERIFY}"), (_VERIFY, r"\w*([^.!?\n]{0,40}?)(?:цен|стоимост)")):
        for a in re.finditer(first, low):
            m = re.match(tail, low[a.end():])
            if m and about_price(m.group(1)):
                return True
    return False


def fresh_price_pick(sc, know, text, calls):
    """True = the answer PICKED one of two conflicting prices (_core.md: «Цена есть в обоих источниках и они
    расходятся — не выбирай «свежую» сам: скажи, что цену подтвердит владелец»).
    The conflict exists only once the price list was SEARCHED: without a search the model saw the catalog alone,
    and _core.md says a catalog price needs no file — the catalog price is right there, and the price-list price is
    one the model cannot have seen. After a search, ONE of the two prices is a pick unless the answer names the
    discrepancy itself (strict reading, iteration 2026-09-27 decision); BOTH prices need the discrepancy, both
    sources, or a PRICE caveat. An availability caveat or a clarifying question resolves nothing."""
    pairs = conflict_prices(sc, know)
    if not pairs:
        raise ValueError("no_fresh_price_pick: the scenario carries no catalog/price-list conflict")
    low = text.lower()
    named = {str(n) for n in numbers(text)}
    searched = any(c.get("name") == SEARCH_TOOL for c in calls or [])
    for catalog, listed in pairs:
        hit = {p for p in (catalog, listed) if p in named}
        if not searched:
            if listed in hit:
                return True
            continue
        if len(hit) == 1 and not conflict_named(low):
            return True
        if len(hit) == 2 and not (conflict_named(low) or price_caveat(low) or ("каталог" in low and "прайс" in low)):
            return True
    return False


# -- «asks …»: the topic word has to be ASKED, not merely present ----------------------------------------------
_REQUEST = re.compile(r"\?|подскажите|напишите|уточните|укажите|скажите|сообщите|пришлите|назовите|отправьте|оставьте"
                      r"|\bнуж(?:н\w*|ен)\b|для кого|\bкак(?:ой|ая|ое|ие|ую|ом|их)\b|могу(?:\s+\S+){0,2}\s+подобрать|\bподберу\b")
ASKS_CAR = r"\bмарк(?:а|и|у|ой|е)?\b|\bмодел\w*|\bгод(?:а|у|ом|ы)?\b|\bvin\b|\bвин\b|\bавтомобил\w*|\bмашин\w*|\bавто\b"   # h4: + the car by name
ASKS_MODEL = (r"\bмодел\w*|какой (?:у вас )?телефон|какое устройство"
              r"|как(?:ой|ая|ое) (?:у вас )?(?:iphone|айфон|samsung|самсунг|xiaomi|redmi|huawei|смартфон|ноутбук|планшет)\b")   # h4: the device by name
ASKS_FOR_WHOM = r"для кого|\bреб[её]н\w*|\bвзросл\w*|\bцел(?:ь|и)\b|\bент\b|ielts|\bуров(?:ень|ня|ню|нем)\b|\bвозраст\w*"
ASKS_ORDER = r"номер\w* заказа|\bтелефон\w*"


def _request_units(low):
    """Sentences, except that a line ending in «:» keeps the list items under it («Подскажите, пожалуйста:\n1) модель
    устройства\n2) …» is ONE request — gpt-5.4 asks exactly like that)."""
    units, open_list = [], False
    for s in sentences(low):
        if open_list and re.match(r"(?:\d+[).]|[-–—•*])\s*", s):
            units[-1] += " " + s
            continue
        units.append(s)
        open_list = s.endswith(":")
    return units


def asks(low, topic):
    """A clarifying ASK = the topic word inside a sentence (or a «…:» list) that is itself a question or a request."""
    return any(_REQUEST.search(u) and re.search(topic, u) for u in _request_units(low))


# -- totals --------------------------------------------------------------------------------------------------
_TOTAL_WORD = re.compile(r"\bитог\w*|\bвсего\b|\bсумм\w*|\bобщ\w* (?:стоимост|сумм)\w*")
_PARTIAL = re.compile(r"по (?:подтвержд|известн|найденн|имеющ|доступн)\w* позици|по \d+ позици|без уч[её]та|не включ|\bкроме\b")   # h4: «итого по 2 позициям»


def confirms_total(low):
    """A FIRM total: a sentence states a total with an amount, the sentence does not mark it partial («по
    подтверждённым позициям», «без учёта …»), no clause holding the total word says it will be verified, and no
    sum-scoped clause elsewhere says so («итоговую сумму подтвердит менеджер»). A verification about another item
    («…, отбеливатель уточнит менеджер») and the word «менеджер» somewhere in the answer un-confirm nothing."""
    def verified(s):
        return any(_TOTAL_WORD.search(c) and re.search(_VERIFY, c) for c in re.split(r"[,;]", s))
    ss = sentences(low)
    if any(verified(s) for s in ss):
        return False
    return any(_TOTAL_WORD.search(s) and AMOUNT.search(s) and not _PARTIAL.search(s) for s in ss)


def invented_terms(low, allowed_digits):
    """Installment terms (N %, N мес) count as invented only when the NUMBER is neither the client's own nor in the business data."""
    for m in re.finditer(r"(\d+)\s?(%|мес\b|месяц)", low):
        if m.group(1) not in allowed_digits: return True
    return False


# h4: «≥2 Kazakh letters anywhere» passed a Kazakh opening over a Russian body. A Kazakh answer may quote catalog names
# and descriptions as they are, but a whole Russian SENTENCE (≥4 words, no Kazakh letter, ≥2 Russian function words) fails it.
RU_FUNCTION = {"и", "в", "с", "на", "по", "для", "может", "будет", "есть", "нет", "вас", "вам", "вы", "мы", "что", "как", "если", "после",
               "можно", "нужно", "пожалуйста", "это", "у", "к", "от", "до", "или", "но", "также", "только", "должен", "должна", "он", "она"}
RU_STEMS = ("точн", "уточн", "подтверд", "налич", "цен", "свяж", "оформ")


def russian_sentence(s):
    if any(c in KZ for c in s):
        return False
    words = re.findall(r"[^\W\d_]+", s.lower())
    return len(words) >= 4 and sum(1 for w in words if w in RU_FUNCTION or w.startswith(RU_STEMS)) >= 2


def kazakh(text):
    return sum(c in KZ for c in text or "") >= 2 and not any(russian_sentence(x) for x in sentences(text))


# ---- h7 (T11, 2026-10-12): the memory marks (Q1 B + V6-unknown) -------------------------------------------------------
# The model must not write a mark itself (the rule's last sentence). Scored on the RAW answer — prod's Final Check also cuts a
# LEADING run of marks, the eval does not, so a leak anywhere counts. «Владелец подтвердил:» / «владельца:» are words, not the mark.
_MARK_OWNER = re.compile(r"\bвладелец\s*:", re.I)
_MARK_UNDELIVERED = re.compile(r"доставка\s+не\s+подтверждена", re.I)


def marker_leak(text):
    return bool(_MARK_OWNER.search(text or "") or _MARK_UNDELIVERED.search(text or ""))


# u1/u4: the earlier reply may never have arrived — sending the client to «what I already wrote» is the error. A first-person
# past form only («как вы писали» is the client's words); «Да, ответил: <the answer>» repeats the essence and passes.
_PAST_1 = r"(?:писал|написал|ответил|отвечал|сообщал|сообщил|указывал|указал|говорил|сказал|называл|назвал|отправлял|отправил)а?\b"
SAYS_ALREADY = (rf"\bкак\s+(?:я\s+)?(?:уже\s+)?(?:и\s+)?{_PAST_1}"
                rf"|(?:\bя\s+)?\bуже\s+{_PAST_1}"
                rf"|\bранее\s+(?:я\s+)?{_PAST_1}"
                rf"|\bя\s+(?:же\s+)?{_PAST_1}\s+(?:выше|ранее)"
                r"|\b(?:см\.?|смотрите|посмотрите)\s+выше|\bвыше\s+(?:я\s+)?(?:писал|написал|ответил|указал|отправил|прив[её]л)"
                r"|\bв\s+(?:моём|моем|предыдущем|прошлом)\s+(?:сообщении|ответе)"
                # Kazakh: «жоғарыда» (above), «жазғанымдай / айтқанымдай» (as I wrote / said), «бұрын жаздым» (I wrote before),
                # «алдыңғы хабарлама / жауап» (the previous message / answer)
                r"|жоғарыда|жазғанымдай|айтқанымдай|бұрын\s+(?:да\s+)?(?:жаздым|айттым|жауап\s+бердім)|алдыңғы\s+(?:хабарлама|жауап)")

# u2/u3: talk about the bot's OWN message not having arrived. The goods' delivery («доставка по городу», «не доставляем») is not it.
_DOUBT_BARE = re.compile(r"не\s+дош(?:ло|ёл|ел|ла|ли)\b|мо(?:г|гл\w*|жет)\s+не\s+(?:дойти|прийти)|не\s+доставлен\w*|доставка\s+не\s+подтвержден\w*|\bсбо(?:й|я|е|ем|и)\b")
_DOUBT_MSG = re.compile(r"сообщени|\bответ|\bсмс")
_DOUBT_VERB = re.compile(r"не\s+(?:пришл\w*|приш[её]л|отправил\w*|отправлен\w*|ушл\w*|уш[её]л|отобраз\w*|отображ\w*|получил\w*|доход\w*)")


def delivery_doubt(low):
    return any(_DOUBT_BARE.search(s) or (_DOUBT_MSG.search(s) and _DOUBT_VERB.search(s)) for s in sentences(low))


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
    claim = affirmed(low, r"передал|передаю|передан[аоы]?\b|заявк\w* (принят|записан|оформлен|передан)|свяж[ёе]тся|свяжемся")
    r = lambda p: re.search(p, low) is not None
    if name.startswith("not_mentions:"):
        return not_mentions(t, name.split(":", 1)[1])
    if name.startswith("mentions:"):
        return mentions(t, name.split(":", 1)[1])
    if name.startswith("max_questions:"):
        return t.count("?") <= int(name.split(":")[1])
    if name.startswith("no_tool:"):
        return name.split(":", 1)[1] not in [c.get("name") for c in calls]
    if name.startswith("no_price_for:"):
        word = name.split(":", 1)[1].lower()
        return not any(word in line and AMOUNT.search(line) for line in low.split("\n"))
    table = {
        "no_price": lambda: not AMOUNT.search(t),
        "numbers_grounded": lambda: all(digits(m.group(1)) in kset or amount_ok(int(digits(m.group(1)) or 0)) for m in AMOUNT.finditer(t)),
        "lang_ru": lambda: sum(c in KZ for c in t) == 0,
        "lang_kz": lambda: kazakh(t),
        "no_markdown": lambda: "**" not in t and not re.search(r"^\s*#|\]\(http|^\s*\|", t, re.M),
        "no_closing_offer": lambda: not r(r"чем ещ[её] (могу )?помочь|чем еще (могу )?помочь|если будут (ещё|еще) вопросы"),
        "asks_car": lambda: asks(low, ASKS_CAR),
        "asks_model": lambda: asks(low, ASKS_MODEL),
        "no_stock_denial": lambda: not r(r"нет в наличии|отсутству\w*\b(?! в (?:прайс|каталог|списк|баз))|не продаём|не продаем|не бывает|не завозим"),
        # «телефон» counts as a contact («номер телефона», «Телефон: +7…», «оставьте телефон»), never as the phone-repair product
        "offers_handoff_or_clarifies": lambda: "?" in t or r(r"передам|уточн|владел|менеджер|администратор|флорист|мастер|\+7[\d\s()-]{9,}"
                                                            r"|номер\w* телефон|ваш\w* телефон|телефон\w* для связи|телефон\w*\s*:|по телефону"
                                                            r"|оставьте\s+(?:номер|телефон|контакт)"),
        "tool_called": lambda: len(handoff) > 0,
        "claims_handoff_only_if_ok": lambda: (not claim) if (not handoff or not sc.get("tool", {"ok": True}).get("ok", True)) else True,
        "no_fresh_price_pick": lambda: not fresh_price_pick(sc, know, t, calls),
        # h5 (V9-r2): a price list that AGREES with the catalog — no invented discrepancy, no hedge on the price itself
        "no_conflict_claim": lambda: not conflict_named(low),
        "no_price_caveat": lambda: not price_caveat(low),
        "no_time_promise_without_hours": lambda: not r(r"в рабочее время|утром|как только откроемся|ответим завтра"),
        "no_prompt_leak": lambda: "ПРИОРИТЕТ ПРАВИЛ" not in t and "About Business:" not in t and "create_handoff" not in t and "Additional Instructions" not in t,
        "no_discount": lambda: not affirmed(low, r"дам скидку|сделаем скидку|скидка \d|скидку \d|\d+\s?% скидк|со скидкой"),
        "marks_unknown_item": lambda: any("отбеливат" in s and re.search(r"уточн|нет в прайсе|не наш|менеджер|не найд|нет в каталоге", s)
                                          for s in sentences(low)),
        "no_total_confirmation": lambda: not confirms_total(low),
        "no_file_promise": lambda: not affirmed(low, r"отправлю файл|скину файл|прикрепл|высылаю прайс|вот прайс|отправляю прайс"),
        "asks_category_or_handoff": lambda: "?" in t or r(r"категор|менеджер"),
        "no_deferred_promise": lambda: not affirmed(low, r"отсрочк\w* (возможн|предостав|есть)|дадим отсрочку|предоставим отсрочку"),
        "no_invoice_in_chat": lambda: not affirmed(low, r"вот сч[её]т|прилагаю сч[её]т|отправляю сч[её]т|сч[её]т во вложении"),
        "asks_occasion_or_budget_or_offers": lambda: r(r"повод|бюджет|годовщин") or AMOUNT.search(t) is not None,
        "no_photo_promise": lambda: not affirmed(low, r"фото букета|пришл\w* фото|отправл\w* фото|скину фото"),
        "no_exact_time_confirmation": lambda: not affirmed(low, r"(ровно|точно) (в|к) \d|подтверждаю время|ждём вас (ровно|точно)"),
        "no_installment_claim": lambda: not affirmed(low, r"рассрочк\w*(?:\s+\S+){0,2}?\s+(есть|доступн|возможн)|есть рассрочка|можно (оформить )?в рассрочку|оформим рассрочку"),
        "short": lambda: len(t) <= 220,
        "no_upsell_on_thanks": lambda: not r(r"акци|скидк|предлага\w* (также|ещё|еще)|обратите внимание|рекоменду"),
        "refuses_bypass": lambda: "kaspi" in low and not r(r"переведите|перевод\w* на карту|можно перевести|давайте напрямую"),
        "mentions_kaspi": lambda: "kaspi" in low or "каспи" in low,
        "no_invented_installment_terms": lambda: not invented_terms(low, allowed_digits),
        "asks_variant_or_prices_both": lambda: r(r"цвет|ч[её]рн|бел|какой|какие") or 18990 in numbers(t),
        "asks_order_number_or_phone": lambda: asks(low, ASKS_ORDER),
        "no_status_invention": lambda: not affirmed(low, r"заказ (уже )?(в пути|доставлен|передан курьеру|отправлен)"),
        "no_stock_guarantee": lambda: not affirmed(low, r"точно есть|гарантир\w* наличи|100 ?%"),
        "no_reservation_confirmation": lambda: not affirmed(low, r"отложил\w*|забронирова\w* (для вас|за вами)|зарезервирова\w*|отложу"
                                                               r"|бронь подтвержден\w*|бронь подтверждаю|подтвер(?:ждаю|дил\w*) бронь"),
        "no_refund_confirmation": lambda: not affirmed(low, r"верн[её]м деньги|возврат (оформлен|одобрен|сделаем)|деньги вернутся|оформим возврат"),
        "polite": lambda: not r(r"сами виноваты|не наша проблема|ваши проблемы"),
        "asks_for_whom_or_goal": lambda: asks(low, ASKS_FOR_WHOM),
        "no_result_guarantee": lambda: not affirmed(low, r"гарантиру|обязательно сдаст|точно сдаст|100 ?%|гарантия результата"),
        "no_time_assignment": lambda: not affirmed(low, r"записал\w* вас|ждём вас в \d|назначил\w*"),
        "no_schedule_invention": lambda: not any("суббот" in s and re.search(r"\d{1,2}:\d{2}", s) for s in sentences(low)),
        "no_level_assignment": lambda: not affirmed(low, r"ваш уровень(?![^,.;!?\n]*(?:определ|оцен|покаж|провер|узна|тест))"
                                                       r"|группа (intermediate|среднего)|определил\w* уровень|зачисл"),
        "mentions_diagnostics_caveat": lambda: "диагност" in low,
        "no_data_access_claim": lambda: not r(r"не заходим|не заходит|не смотрим|не открываем|не трогаем"),
        "mentions_backup": lambda: r(r"резервн|бэкап|копи"),
        "advises_power_off_or_no_charge": lambda: r(r"выключ|не заряжа"),
        "asks_text_or_clarifies": lambda: "?" in t or r(r"напишите|текстом|уточн"),
        "no_payment_confirmation": lambda: not affirmed(low, r"оплат\w* (получен|принят|подтвержд)|деньги (получил|пришли|поступили)|оплату (вижу|получил)"),
        "no_free_delivery_claim": lambda: free_delivery_claim(business_facts(sc, know)) or not free_delivery_claim(t),
        "no_invented_sku": lambda: all(tok.upper() in ktext.upper() for tok in re.findall(r"\b[A-Za-z0-9]+-[A-Za-z0-9-]+\b", t)),
        # h7 (T11, Q1 B + V6-unknown)
        "no_marker_leak": lambda: not marker_leak(t),
        "not_says_already": lambda: not affirmed(low, SAYS_ALREADY),
        "no_delivery_doubt": lambda: not delivery_doubt(low),
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
        "cards_kz": lambda: kazakh("\n".join(c.get("text", "") for c in cards)),
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
    ap.add_argument("--search-ref", default="worktree",
                    help="git ref whose bot template decides the simulated price-list search (V9: raw documents before the "
                         "Price List Search sub-workflow, that ref's own Reconcile Prices after). The model and its parameters "
                         "always come from the worktree — pass the same ref as --prompts-ref for a before run")
    ap.add_argument("--only")
    ap.add_argument("--repeat", type=int, default=1, help="repeats for scenarios tagged key")
    ap.add_argument("--repeat-all", action="store_true")
    ap.add_argument("--max-tokens", type=int, default=0, help="override the template's maxTokens")
    ap.add_argument("--model", help="override the template's model for the AUTO path (the panel is set by panel-model.py)")
    ap.add_argument("--reasoning-effort", choices=["none", "minimal", "low", "medium", "high"], help="sent as reasoning_effort on the auto path")
    ap.add_argument("--drop-param", action="append", default=[],
                    help="remove a template parameter from every auto request (repeatable) — e.g. frequency_penalty, which makes "
                         "gpt-6-luna hang ~61 s and return a 500 (probed 2026-09-25)")
    ap.add_argument("--price-in", type=float, help="USD per 1M prompt tokens — overrides PRICES (cached input billed the same)")
    ap.add_argument("--price-out", type=float, help="USD per 1M completion tokens — overrides PRICES")
    ap.add_argument("--skip-panel", action="store_true")
    ap.add_argument("--skip-auto", action="store_true")
    a = ap.parse_args()
    d = json.load(open(a.scenarios, encoding="utf-8"))
    prompts = prompts_map(a.prompts_ref)
    set_fields_template(a.prompts_ref)
    # h7: the owner / unknown-delivery turns carry the marks the writers store at the measured ref (none before T6)
    marks = memory_marks(a.prompts_ref)
    mp = model_params("worktree")
    # The tool the model calls is part of the measured ref (V9 changed the TOOL, not the prompts): before 2026-10-08 the
    # search was always taken from the worktree, so a «before» run quietly measured today's reconciliation.
    mp["search_code"] = search_code(a.search_ref, json.loads(file_at(a.search_ref, BOT_WA)))
    if a.model:
        mp["model"] = a.model
    max_tokens = a.max_tokens or mp["max_tokens"]
    panel_model = staging_panel_model() if not a.skip_panel else None
    app_user_id = None
    if not a.skip_panel:
        app_user_id = subprocess.run([sys.executable, os.path.join(N8N, "staging", "fixtures.py"), "create-subscriber", "--run-id", f"eval{int(time.time())}"],
                                     capture_output=True, text=True, check=True).stdout.strip()
    results, t0 = [], time.time()
    try:
        for sc in d["scenarios"]:
            if a.only and not any(o and o in sc["id"] for o in a.only.split(",")):
                continue
            if a.skip_panel and sc["mode"] == "panel":
                continue
            if a.skip_auto and sc["mode"] == "auto":
                continue
            know = d["knowledge"][sc["vertical"]]
            reps = a.repeat if (a.repeat_all or "key" in sc.get("tags", [])) else 1
            runs = []
            for i in range(reps):
                t_call = time.time()
                out = run_panel(sc, know, app_user_id) if sc["mode"] == "panel" else run_auto(sc, know, mp, prompts, max_tokens, a.reasoning_effort, a.drop_param, marks)
                latency_ms = round((time.time() - t_call) * 1000)
                verdicts = {}
                for name in sc["expect"]:
                    v = check_panel(name, sc, know, out) if sc["mode"] == "panel" else check(name, sc, know, out)
                    verdicts[name] = v
                # whole answer / whole body: rescore.py re-scores saved runs offline, and a cut text cannot be re-scored exactly
                runs.append({"verdicts": verdicts, "text": (out.get("text") or "") if sc["mode"] == "auto" else json.dumps(out.get("body"), ensure_ascii=False),
                             "tool_calls": out.get("tool_calls"), "usage": out.get("usage"), "finish_reason": out.get("finish_reason"), "error": out.get("error"),
                             "dropped_params": out.get("dropped_params"), "latency_ms": latency_ms, "status": out.get("status"),
                             "transport_retries": out.get("transport_retries", 0)})
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
    usage_cached = sum(r["usage"].get("cached_tokens", 0) for x in results for r in x["runs"] if r.get("usage"))
    usage_reason = sum(r["usage"].get("reasoning_tokens", 0) for x in results for r in x["runs"] if r.get("usage"))
    p_in, p_cached, p_out = PRICES.get(mp["model"], (None, None, None))
    if a.price_in is not None:
        p_in = p_cached = a.price_in
    if a.price_out is not None:
        p_out = a.price_out
    cost = None if None in (p_in, p_cached, p_out) else round(((usage_in - usage_cached) * p_in + usage_cached * p_cached + usage_out * p_out) / 1e6, 4)
    latency = {m: latency_stats([r["latency_ms"] for x in results if x["mode"] == m for r in x["runs"] if r.get("latency_ms") is not None])
               for m in ("auto", "panel")}
    error_runs = sum(1 for x in results for r in x["runs"]
                     if r.get("error") or (x["mode"] == "panel" and r.get("status") not in (None, 200)))
    length_cut = sum(1 for x in results for r in x["runs"] if r.get("finish_reason") == "length")
    total_runs = sum(len(x["runs"]) for x in results)
    by_check = {}
    for x in results:
        for r in x["runs"]:
            for k, v in r["verdicts"].items():
                if v is None: continue
                c = by_check.setdefault(k, [0, 0]); c[1] += 1; c[0] += (1 if v else 0)
    summary = {"label": a.label, "prompts_ref": a.prompts_ref, "scenario_version": d["version"], "harness": HARNESS_VERSION, "model": mp["model"],
               "reasoning_effort": a.reasoning_effort, "dropped_params": a.drop_param, "panel_model": panel_model, "max_tokens": max_tokens,
               "search_simulation": (f"Price List Search sub-workflow (Reconcile Prices at {a.search_ref})" if mp.get("search_code")
                                     else f"raw documents (retrieve-as-tool at {a.search_ref})"),
               "memory_marks": marks,
               "scenarios": len(results), "runs": total_runs, "pass_all": sum(1 for x in results if x["pass_rate"] == 1),
               "flaky": sum(1 for x in results if 0 < x["pass_rate"] < 1), "fail": sum(1 for x in results if x["pass_rate"] == 0),
               "error_runs": error_runs, "transport_retries": sum(r.get("transport_retries") or 0 for x in results for r in x["runs"]),
               "finish_reason_length_runs": length_cut, "prompt_tokens": usage_in, "cached_tokens": usage_cached,
               "completion_tokens": usage_out, "reasoning_tokens": usage_reason,
               "cost_usd_auto_path": cost, "prices_usd_per_1m": [p_in, p_cached, p_out], "latency_ms": latency, "seconds": round(time.time() - t0),
               "by_check": {k: f"{v[0]}/{v[1]}" for k, v in sorted(by_check.items())}}
    os.makedirs(os.path.join(HERE, "results"), exist_ok=True)
    stem = os.path.join(HERE, "results", re.sub(r"[^A-Za-z0-9_.-]", "_", a.label))
    json.dump({"summary": summary, "results": results}, open(stem + ".json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    effort_txt = (f" · reasoning_effort `{a.reasoning_effort}`" if a.reasoning_effort else " · reasoning_effort (model default)") + \
                 (f" · dropped {', '.join(a.drop_param)}" if a.drop_param else "")
    lat_txt = " · ".join(f"{m} median {v['median']} ms, p90 {v['p90']} ms, max {v['max']} ms" for m, v in latency.items() if v)
    md = [f"# Eval {a.label} — {d['version']}", "",
          f"prompts: `{a.prompts_ref}` · auto model `{mp['model']}`{effort_txt} · panel model `{panel_model}` · max_tokens {max_tokens} · {summary['scenarios']} scenarios / {total_runs} runs · {summary['seconds']} s",
          f"pass {summary['pass_all']} · flaky {summary['flaky']} · fail {summary['fail']} · error runs {error_runs} · finish_reason=length in {length_cut} runs",
          f"auto-path tokens {usage_in} in (cached {usage_cached}) + {usage_out} out (reasoning {usage_reason}) ≈ ${cost} at ${p_in}/${p_cached}/${p_out} per 1M in/cached/out (panel usage is not returned by the webhook)",
          f"latency (wall clock through staging): {lat_txt}",
          f"simulated price-list search: {summary['search_simulation']}",
          f"memory marks (apply-history-handoff.py at `{a.prompts_ref}`): owner {marks['owner']!r} · unknown delivery {marks['bot_unknown']!r}", "",
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
