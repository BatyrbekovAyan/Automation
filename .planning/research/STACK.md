# Stack Research

**Domain:** AI reply-suggestions panel (semi-auto agent-assist) for a brownfield Unity 6 / C# WhatsApp business-chat app (CIS / Russian-language market). Compute via existing n8n cloud + Wappi.
**Researched:** 2026-06-23
**Confidence:** HIGH on structured-output technique and compute placement; MEDIUM on re-cluster approach and confidence calibration (research-backed but design-dependent).

> Scope: This document covers ONLY the new feature's technical approach. The host stack (Unity 6 `6000.3.9f1`, C#, URP, TMPro, DOTween, `UnityWebRequest` + coroutines, Newtonsoft.Json, n8n cloud, Wappi) is fixed — see `.planning/codebase/STACK.md`. Do not re-litigate it.

---

## TL;DR Recommendation

1. **Generate suggestions in n8n, not on-device.** n8n already holds the model credentials, the per-bot business context (products/services/prompts), and the autonomous-mode prompt logic. On-device LLM inference is a non-starter (latency, battery, model size, no Russian-strong on-device model, secret exposure). **(HIGH)**
2. **Force structured output with grammar-constrained JSON Schema** (OpenAI Structured Outputs `strict: true`, or Anthropic native JSON outputs / strict tool use), surfaced in n8n via the **AI node's "Require Specific Output Format" + Structured Output Parser**. This guarantees the `[{ text, intent_label, confidence }] × 4` shape with `intent_label` constrained to an **enum**. Do NOT rely on prompt-only "return JSON". **(HIGH)**
3. **Implement re-cluster as an LLM re-prompt steered by the picked text** (Approach A), not embedding clustering (Approach B). One round-trip, controllable diversity, no embedding infra, no second model. Embeddings add latency, cost, and infra for a job a steering prompt does better here. Keep Approach B only as a future optimization if you later cache/library replies. **(MEDIUM)**
4. **Phase the transport.** Phase 1 UI runs on **mock/stub suggestion JSON** matching the final contract (already a PROJECT decision). Phase 2 wires the live n8n call. This de-risks the single hardest integration problem: **n8n has no incoming-webhook path to the app today — it is outbound-only.** You must add a synchronous request/response webhook. **(HIGH)**
5. **Pick a fast, Russian-strong model** for the suggestion node (GPT-5.x-mini-class or Claude Haiku-class) — suggestions must feel instant; this is interactive, not batch. **(MEDIUM)**

---

## Recommended Stack

### Core Technologies (the new feature)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| n8n AI/LLM node + **Structured Output Parser** sub-node | n8n cloud (current, `bagkz.app.n8n.cloud`) | Run the suggestion-generation prompt and force the `{text, intent_label, confidence}` array shape | Already the project's automation brain; holds credentials + per-bot context. Native "Require Specific Output Format" wires a JSON-Schema-validated parser onto the AI root node. No new infra. (HIGH) |
| **OpenAI Structured Outputs** (`response_format: json_schema`, `strict: true`) **or** **Anthropic native JSON outputs** (`output_config.format` = `json_schema`) / strict tool use | Current API | Grammar-constrained generation — the model literally cannot emit non-conforming JSON or an out-of-set `intent_label` | OpenAI reports <0.1% schema-failure with strict mode; both vendors compile the schema to a token-masking grammar at decode time. Far more reliable than JSON-mode or prompt-only. (HIGH) |
| **n8n Webhook (sync) + Respond to Webhook** node | n8n cloud | New inbound endpoint the app POSTs to and **waits** on for the 4 suggestions | The app currently only *triggers* n8n (outbound, fire-and-forget). Suggestions need a request→response round-trip. This is the one genuinely new integration surface. (HIGH) |
| Fast generation model (e.g. **GPT-5.x-mini class** or **Claude Haiku class**) | Current | The model behind the suggestion node | Interactive UX needs sub-~3s perceived. Use a small/fast tier with strong Russian, not the flagship reasoning tier. Verify the exact current model ID at build time. (MEDIUM) |

### Supporting Libraries / Components (Unity side — all already in the project)

| Component | Source | Purpose | When to Use |
|-----------|--------|---------|-------------|
| `UnityWebRequest` + coroutine pattern | built-in (existing convention) | POST the incoming message + context to the new n8n webhook, await JSON | The mandated networking pattern. Follow `unity-api-integration` skill. (HIGH) |
| `Newtonsoft.Json` (`JsonConvert`) | NuGet 13.0.4 (existing) | Deserialize the suggestions array into a `SuggestionSet` model | `JsonUtility` cannot reliably do arrays-of-objects with optional/nested fields; the codebase already uses `JsonConvert` for complex responses. (HIGH) |
| Suggestion data models in `Assets/Scripts/Chat/` | new C# classes | `ReplySuggestion { string text; string intentLabel; float confidence; }`, `SuggestionSet { ReplySuggestion[] suggestions; string requestId; }` | Mirror the existing `RawMessage`/`NormalizedMessage` model-class convention; keep API response models in `Chat/`. (HIGH) |
| Existing serial/guard fetch patterns (`CrossChatResponseGuard`, `_chatFetchesInFlight`, serial download queue) | existing | Prevent the suggestion fetch from racing Wappi's known concurrency-crossing bugs | Wappi `messages/get` and `media/download` cross concurrent responses (confirmed server bugs). A suggestion fetch firing while a chat opens MUST be gated/serial. (HIGH) |
| DOTween | 2.2.8+ (existing) | Card enter/exit + re-cluster swap animation | Re-cluster replaces all 4 cards — animate the swap so it reads as a deliberate refine, not a flicker. (HIGH) |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| n8n editor (`bagkz.app.n8n.cloud`) | Build/modify the suggestion + re-cluster workflows | Existing Create/Edit-workflow webhooks are the template for request shape. Add a NEW sync webhook; don't overload the autonomous-mode flow. |
| Mock/stub JSON fixture | Phase-1 UI development with no backend | Author the exact final contract (4 items, enum labels, 0–1 confidence) once, share between the C# stub provider and the n8n node's schema. Single source of truth for the contract. |
| Unity Test Framework (EditMode) | Test JSON parsing, enum-label validation, confidence clamping, re-cluster request building | Follow the existing headless/bridge test workflow. Parsing + label-set enforcement are exactly the kind of pure logic the EditMode suite is good at. |

---

## Part 1 — Generating 4 reliable `{ text, intent_label, confidence }` suggestions

### Structured output: use grammar-constrained schema, not prompting

**Recommendation (HIGH):** Use the provider's **native grammar-constrained structured output**, exposed through n8n's "Require Specific Output Format" + **Structured Output Parser** node.

- **OpenAI:** `response_format: { type: "json_schema", json_schema: {...}, strict: true }`. Strict mode uses a context-free-grammar token mask — the model "cannot produce a non-conforming response." OpenAI reports **<0.1% schema-failure / ~99.9% compliance**. JSON Mode (`type: "json_object"`) is now **legacy** — it only guarantees *valid JSON syntax*, not your schema. Do not use it.
- **Anthropic:** native **JSON outputs** (`output_config.format` = `json_schema`) and/or **strict tool use** (`strict: true`). Same idea — schema compiled to a decode-time grammar. Now GA (the old `structured-outputs-2025-11-13` beta header is no longer required).

> Verify the exact current model IDs and the n8n node's provider support at build time. Web sources for both vendors' model *lists* were contaminated with hallucinated names; the *mechanism* (strict JSON-Schema grammar) is confirmed by both official docs. Treat the mechanism as HIGH, specific model IDs as "check the live model picker."

**Schema design (the contract):**

```jsonc
{
  "type": "object",
  "properties": {
    "suggestions": {
      "type": "array",
      "minItems": 4, "maxItems": 4,
      "items": {
        "type": "object",
        "properties": {
          "text":         { "type": "string", "description": "Ready-to-send reply in the customer's language (RU/UK/etc.)." },
          "intent_label": { "type": "string", "enum": ["price","availability","order","greeting","complaint","shipping","hours","other"] },
          "confidence":   { "type": "number", "description": "0.0–1.0 self-rated fit of this reply to the message." }
        },
        "required": ["text","intent_label","confidence"],
        "additionalProperties": false
      }
    }
  },
  "required": ["suggestions"],
  "additionalProperties": false
}
```

**Keeping `intent_label` in a controlled set (HIGH):** Put the allowed labels in the **schema `enum`** — grammar-constrained decoding then makes an out-of-set label *impossible*, not just discouraged. This is strictly stronger than listing labels in the prompt. Define the same enum on the Unity side (a C# `enum` or a validated string set) and reject/repair anything unexpected as a defensive second layer (n8n + OpenAI `$ref` quirks exist — see Pitfalls).

**Confidence field realism (MEDIUM — this is the soft spot):**
- LLM **self-reported confidence is the *best available* cheap signal** (research: avg ECE ~0.166, beats self-consistency ~0.229) — but it is still **systematically miscalibrated and skews high**. Some models rate the large majority of answers at the top of the scale. Treat `confidence` as a **relative ranking hint within the set of 4, not a trustworthy probability.**
- **Do not** show it as a precise percentage that implies real accuracy ("97% confident"). That over-promises and erodes the trust that is this product's north star.
- **Recommended UI treatment:** map confidence to a **coarse 3-bucket visual** (high / medium / low — e.g. a dot or bar), and **use it only to order the 4 cards** (highest first). The owner is the calibrator; the model just ranks.
- **Schema-level guard:** request 0.0–1.0 (JSON Schema can't enforce numeric min/max reliably across providers — see Pitfalls), then **clamp on the client**. If you want better spread, instruct the model to "spread confidences across the four so they are comparable" — but verify; it may still bunch.

**Why not few-shot-only / regex / retry-on-parse-fail:** prompt-only JSON requires parse-retry loops (latency + cost + occasional silent corruption). Grammar-constrained output removes the entire failure class. Use few-shot examples *for tone/quality*, not for *format enforcement*.

---

## Part 2 — The RE-CLUSTER mechanic (owner picks one → fresh set of 4 steered toward it)

### Approach comparison

| Approach | How it works | Latency | Cost | Determinism | Impl. complexity | Verdict |
|---|---|---|---|---|---|---|
| **(A) LLM re-prompt steered by the pick** | Send the original message + the picked reply text + "generate 4 NEW replies in the same direction/tone as this pick, varying angle/length/formality" to the **same** structured-output node | **1 LLM round-trip** (~1–3s w/ fast model) | 1 LLM call/re-cluster | Low (set temperature low + diversity instruction; same shape every time) | **Low** — reuse the same n8n node + schema; add the pick to the prompt | **RECOMMENDED** |
| **(B) Embedding similarity + clustering/re-rank** | Embed a candidate pool, embed the pick, rank/cluster candidates by cosine similarity to the pick, return top 4 | 2+ round-trips (generate pool → embed pool+pick → rank), or a pre-generated pool | Generation **+** N embedding calls (or self-host embedder) | High *if* pool is fixed; but a fixed pool defeats "fresh set of 4" | **High** — new embedding model/credential, vector math, pool generation, dedup | Avoid for v1 |
| **(C) Hybrid** | Generate a larger pool once, embed it, then on each pick re-rank the *remaining* pool toward the pick (cheap reuse) | First pick fast (rerank only); refreshes need regen | Amortized lower if many picks per chat | Medium | **Highest** | Future optimization only |

### Recommendation: Approach A (LLM re-prompt). (MEDIUM)

**Rationale:**
- **The requirement is "a fresh set of 4 re-ranked/clustered *toward* the pick."** A re-prompt that carries the picked text as steering context produces genuinely new, on-theme replies in one call. Embedding-rerank can only *reorder an existing pool* — to also be "fresh" it must regenerate the pool anyway, so you pay for generation *and* embeddings.
- **Latency is the deciding factor for a mobile chat UX.** Approach A = one fast model call. Approach B = generation + embedding + ranking, i.e. strictly more round-trips and/or a second credentialed service inside n8n. For an interactive "tap a card, watch 4 new ones slide in" loop, every extra hop hurts.
- **No new infrastructure.** Approach A reuses the exact same n8n node, schema, and transport as initial generation — the re-cluster call is the *same webhook* with an added `picked_text` (and optionally `picked_intent`) field. Approach B needs an embedding model, a vector step, and pool management inside n8n where it does not currently exist.
- **Determinism / control.** With a low temperature and an explicit diversity instruction ("4 distinct angles: shorter / warmer / more detailed / upsell"), Approach A gives predictable, controllable variation. Pure embedding similarity tends to collapse toward near-duplicates of the pick (low diversity), which is the opposite of giving the owner useful alternatives.

**When Approach B/C would win (so the decision is informed):** if you later build a **reusable reply library / saved canned replies** per bot, embedding-rank over that *persisted* library becomes cheap and meaningful (retrieve owner's real past replies similar to the pick). At that point a **hybrid** — LLM generation for novelty + embedding retrieval from the owner's reply history — is the upgrade path. Not v1.

**Concrete re-cluster prompt contract (Approach A):**
- Inputs to the webhook: `chat_id`, `incoming_message`, recent context window, `bot_context` (products/services/prompts), and on re-cluster: `picked_text`, `picked_intent`, optionally `rejected_texts` (the 3 not picked) so the new set steers *toward* the pick and *away from* the discarded directions.
- Instruction: "The owner chose the reply below. Produce 4 NEW replies aligned with its intent and tone, each a distinct variation (length / warmth / detail / call-to-action). Do not repeat the chosen reply verbatim." Same output schema.

---

## Part 3 — Where compute lives: **n8n, not on-device**

**Recommendation: generate (and re-cluster) suggestions in n8n. (HIGH)**

**Why n8n:**
- **Context already lives there.** Each bot's products/services/prompts and the autonomous-mode answering logic are already encoded in that bot's n8n workflow. Suggestions should reuse the *same* knowledge and tone the autonomous bot uses — duplicating that on-device would fork the source of truth.
- **Credentials stay server-side.** The LLM provider key belongs in n8n, never shipped in the app. (Even Wappi/n8n keys today live in gitignored `secrets.json`; an LLM key in the client is a worse exposure.)
- **On-device LLM is infeasible here:** no on-device model with strong Russian at acceptable size/latency/battery; IL2CPP/mobile constraints; model download/update burden; inconsistent results across devices. Rejected.
- **Matches existing architecture.** The app already does coroutine `UnityWebRequest` → n8n. Suggestions are the same shape of call.

**What this implies (the real work):**
1. **New inbound, synchronous webhook.** Today n8n is **outbound-only from the app's perspective** (app triggers, n8n executes; "Incoming (n8n → App): None"). For suggestions the app must POST and **wait for the response body**. Add an n8n **Webhook (Respond: "Using Respond to Webhook node")** flow that ends in a **Respond to Webhook** node returning the JSON. This is a new endpoint alongside the existing Create/Edit webhooks — do not retrofit the fire-and-forget ones.
2. **Latency budget & timeout (MEDIUM, but important).** n8n's AI Agent node adds measurable overhead on top of raw model latency (community reports 20–25s with heavy/local setups; cloud + a fast hosted model is far less but still has node overhead). Use a **fast model**, keep the prompt tight, and set the **UnityWebRequest timeout generously** (the project default is 30s; that's fine as a ceiling but design for ~2–4s typical). Show a skeleton/loading state on the 4 cards while waiting.
3. **No streaming to the client for v1.** n8n supports streaming, but the Unity `UnityWebRequest` coroutine pattern is request/response. Return the **complete set of 4** in one response; render together. (Streaming is a later polish, not v1.)
4. **Respect Wappi concurrency bugs.** The suggestion fetch is a *new* network call that can fire right when a chat opens (suggestions auto-populate on incoming message). Gate it through the existing in-flight guard so it never races a concurrent `messages/get` (which Wappi crosses). The suggestion call hits **n8n**, not Wappi, so it won't cross Wappi responses directly — but its *trigger timing* overlaps chat-open, so coordinate ordering.
5. **Idempotency / staleness.** Tag each request with a `requestId` (or `chat_id` + incoming `message_id`). If the owner sends/leaves before the response lands, drop stale responses (mirror the existing cross-chat response-guard discipline).

---

## Installation

No new client packages required — the feature is built from the existing stack. Work items, not installs:

```text
# n8n (server side)
- Add a NEW Webhook node (HTTP POST, "Respond: Using Respond to Webhook node")
- Add an AI/LLM node with "Require Specific Output Format" + Structured Output Parser (JSON Schema above)
- Provider: enable OpenAI Structured Outputs (strict json_schema) OR Anthropic JSON outputs
- Add a Respond to Webhook node returning { suggestions: [...] }
- Duplicate the flow / branch for the re-cluster variant (adds picked_text/picked_intent)

# Unity (client side) — all existing deps, just new code
- New models in Assets/Scripts/Chat/: ReplySuggestion, SuggestionSet (+ IntentLabel enum)
- New UnityWebRequest coroutine (follow unity-api-integration skill) → n8n webhook
- New webhook URL + (if any) key into secrets.json via Secrets.cs (NEVER hardcode)
- Phase 1: a stub provider returning the same JSON contract (no network)
```

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| Grammar-constrained structured output (strict json_schema) | Prompt-only "return JSON" + parse-retry | Never for production here — retries add latency/cost and still corrupt occasionally. Only if a provider/model genuinely lacks strict mode. |
| `intent_label` as schema **enum** | Free-text label + client-side normalization/mapping | Only if the label set must stay open-ended/experimental. Then validate against a known set client-side and bucket unknowns to `other`. |
| Re-cluster via LLM re-prompt (A) | Embedding rerank/cluster (B) | When ranking a **persisted** reply library (owner's saved/past replies) where regeneration is undesirable — and you accept embedding infra + latency. |
| Generate in n8n | Generate via a direct provider call from the app | Only if you stand up a thin proxy that injects the key and bot context server-side — i.e. you'd rebuild what n8n already gives you. Not worth it. |
| Fast small model (mini/haiku tier) | Flagship reasoning model | Only if suggestion quality on hard messages is unacceptable AND you can hide the extra latency. For 4 short replies, the fast tier is the right default. |
| Confidence as coarse 3-bucket ranking hint | Precise % confidence shown to owner | Never — implies a calibration the model doesn't have; undermines trust. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| JSON Mode (`type: "json_object"`) | Legacy — guarantees valid JSON *syntax* only, not your schema or the enum | Strict `json_schema` structured outputs |
| Prompt-only format enforcement + regex/parse-retry | Adds a whole failure class (retries, latency, silent corruption) | Grammar-constrained schema |
| `$ref` in the n8n Structured Output Parser JSON Schema | **n8n does not support `$ref`** in JSON schemas — schema will break | Inline the schema fully (no refs) |
| On-device LLM inference | No Russian-strong small model at acceptable latency/battery; secret exposure; device variance | n8n server-side generation |
| Embedding-cluster re-cluster for v1 | More round-trips/infra than an LLM re-prompt; tends to collapse to near-duplicates of the pick (low diversity) | LLM re-prompt steered by the pick |
| Showing raw self-reported confidence as a precise % | LLM confidence is miscalibrated and skews high; over-promising breaks trust | Coarse bucket + use it only to order the 4 cards |
| Reusing the fire-and-forget Create/Edit webhooks for suggestions | They don't return a body the app waits on | A new sync Webhook + Respond to Webhook flow |
| Firing the suggestion fetch concurrently around chat-open | Trigger timing overlaps Wappi's response-crossing window | Gate through existing in-flight guard; serialize ordering |
| `JsonUtility` for the suggestions array | Weak with arrays-of-objects / optional fields | `Newtonsoft.Json` (already the project standard) |

---

## Stack Patterns by Variant

**If suggestion quality on hard/ambiguous messages is poor with the fast model:**
- Keep the fast model for re-cluster (interactive), but allow the **initial** generation to use a slightly stronger tier, since the first set can tolerate a touch more latency than rapid re-cluster taps.
- Because the model lives in n8n, this is a node-config change — no client change.

**If owners begin saving/reusing canned replies (future):**
- Upgrade re-cluster to **Hybrid (C)**: embed the saved-reply library once; on a pick, retrieve library replies similar to the pick *and* generate fresh ones; merge to 4. This is where embeddings finally earn their keep.

**If n8n round-trip latency proves too high for the auto-populate-on-every-incoming-message requirement:**
- Debounce/coalesce: only auto-generate for the *latest* incoming message, cancel in-flight requests for superseded messages (staleness guard).
- Consider n8n streaming as a v2 polish to show the first card sooner.

---

## Version Compatibility

| Component | Compatible With | Notes |
|-----------|-----------------|-------|
| OpenAI Structured Outputs (strict json_schema) | gpt-4o-2024-08-06 and later, GPT-5.x family | First request per *unique schema* has extra latency (schema processing); subsequent same-schema requests do not — keep the schema stable to benefit from caching. |
| Anthropic JSON outputs (`output_config.format`) | Opus 4.5 / Sonnet 4.5 / Haiku 4.5 class and newer (GA; old beta header optional) | Verify exact current model IDs in the live console — public model *lists* in secondary sources were unreliable. |
| n8n Structured Output Parser | n8n cloud current; provider must support structured output | No `$ref` in schemas. Known bug class: AI Agent node can double-nest the `output` key vs the parser — test the real payload and unwrap defensively. |
| Newtonsoft.Json 13.0.4 | Unity 6 `6000.3.9f1`, IL2CPP | Already in use for complex API parsing; suggestions array fits this. |
| UnityWebRequest (30s timeout) | n8n sync webhook | 30s ceiling is safe; design for ~2–4s typical and a skeleton loading state. |

---

## Open Questions / Flags for the Roadmap

- **Exact current model IDs** (fast tier, Russian-strong) for both the suggestion and re-cluster nodes — confirm in the live provider console at implementation time (secondary sources hallucinated model names; mechanism is verified, names are not).
- **n8n round-trip latency on cloud with the chosen model** — measure early (Phase 2). It determines whether auto-populate-on-every-message is viable as specified or needs debouncing.
- **Confidence spread behavior** — empirically check whether the chosen model bunches all four confidences high; if so, drop the numeric field to pure ordering + buckets.
- **Re-cluster "away from rejected" signal** — decide whether to pass the 3 non-picked texts as negative steering; A/B the diversity outcome.

---

## Sources

- https://developers.openai.com/api/docs/guides/structured-outputs — OpenAI Structured Outputs, strict json_schema, enum support, per-schema first-request latency, function/tool strict mode (HIGH)
- https://platform.claude.com/docs/en/build-with-claude/structured-outputs — Anthropic native JSON outputs (`output_config.format`) + strict tool use, GA status (HIGH on mechanism; model *list* unreliable — MEDIUM)
- https://docs.n8n.io/integrations/builtin/cluster-nodes/sub-nodes/n8n-nodes-langchain.outputparserstructured/ and .../common-issues/ — Structured Output Parser, "Require Specific Output Format", no `$ref` support (HIGH)
- https://github.com/n8n-io/n8n/issues/20029 — AI Agent node double-nests `output` key vs parser (MEDIUM, real-world bug report)
- https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-base.respondtowebhook/ — Respond to Webhook node for synchronous responses (HIGH)
- https://github.com/n8n-io/n8n/issues/15263 and https://community.n8n.io/t/chatbot-ai-agent-latency-on-n8n-cloud/146656 — AI node latency overhead in n8n (MEDIUM)
- https://arxiv.org/html/2510.20460v1 — uncertainty estimation: self-reported confidence best-calibrated but ECE still ~0.166; miscalibration persists (MEDIUM)
- https://arxiv.org/html/2603.06604 — aligning confidence with correctness; models over-rate (MEDIUM)
- https://arxiv.org/html/2510.03777v2 (GuidedSampling) — steering LLMs toward diverse candidates at inference time (MEDIUM, supports re-prompt diversity instruction over embedding collapse)
- https://www.respan.ai/articles/openai-structured-outputs-vs-json-mode and https://crazyrouter.com/en/blog/ai-structured-output-json-mode-guide-2026 — JSON Mode now legacy; strict mode is the 2026 default (MEDIUM, secondary, corroborated by official docs)
- https://app.ailog.fr/en/blog/guides/choosing-embedding-models — multilingual embedding landscape (only relevant if Hybrid/B is pursued later) (LOW/MEDIUM, secondary)

---
*Stack research for: AI reply-suggestions panel (semi-auto) in a brownfield Unity/n8n/Wappi WhatsApp business app*
*Researched: 2026-06-23*
