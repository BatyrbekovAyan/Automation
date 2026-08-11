# Vertical prompts for the suggestions panel («Вместе»)

**Date:** 2026-08-11
**Status:** Approved, ready for planning
**Scope:** Server-only — `Tools/n8n/` sources + the Suggest_Replies workflow. No Unity changes.

## Problem

The Авто reply path and the «Вместе» suggestions panel are near-parity on business data —
both read the same Supabase `documents` RAG table under the same per-bot metadata filter,
both receive the catalog, the composed business knowledge, and the owner's Промпт field.

They are **not** at parity on niche knowledge.

| | Авто | «Вместе» |
|---|---|---|
| Vertical prompt | Full ~2 KB body from `Tools/n8n/prompts/<id>.md`, injected into the workflow's `systemMessage` head at create/edit time | One hardcoded line from the `HINTS` map in the `Assemble` node |

For `auto_parts` the panel's entire niche knowledge is today:

> `перед точной ценой выясняй марку/модель/год/объём или VIN; предлагай аналоги дешевле оригинала.`

against roughly 1.4 KB of intake rules, a common-question playbook, and a НЕЛЬЗЯ list on the
Авто side. Cards are correspondingly less niche-savvy than the bot's own replies.

## Why the Авто prompts cannot simply be reused

Two mismatches make a direct copy wrong, not merely suboptimal.

**Voice.** The vertical prompts escalate to a third party — «уточнит менеджер», «Передам
владельцу, он ответит», «Передаю менеджеру, он свяжется и подтвердит». In Авто that is
correct: the bot is not the owner. In «Вместе» the **owner** presses send, so a card reading
«передам владельцу» is nonsense addressed to the client.

**`_core.md` is dead weight in the panel.** It is ~1.2 KB of Chat Memory instructions,
Supabase Vector Store *tool* usage, and reply-format rules. The panel's system prompt already
covers all of it (`ФАКТЫ`, `СТИЛЬ`, `ВЫВОД`) and partly contradicts it — `_core`'s
«Ответил на вопрос — остановись» works against the panel's six-label move taxonomy.

The portable part is the niche **body** only: intake rules, the common-question playbook, and
the НЕЛЬЗЯ list — with escalation rewritten into first person.

## Decisions

Two forks were settled before design:

1. **Separate panel files**, not derivation from the Авто prompts and not a shared-core
   refactor. The Авто prompts are shipped and device-verified; nothing in this change touches
   them. Cost: two files per vertical to keep in sync. Benefit: each says exactly what its
   mode needs, and neither is compromised to serve the other.
2. **Verification = assertions for all six + live probe on two verticals** (`auto_parts`,
   `kaspi_seller`).

## Architecture

The app already sends `businessTypeId` on every suggestions request
(`N8nSuggestionsProvider.cs:87`) and `Prep` already parses and forwards it. The entire change
is server-side.

```
app (unchanged)
  └── POST /webhook/SuggestReplies { businessTypeId, … }
        └── Prep       (unchanged — already passes businessTypeId through)
              └── Assemble   ← CHANGED: HINTS[id] one-liner → PANEL_PROMPTS[id] block
                    └── LLM  (unchanged: gpt-4o-mini, temp 0.4, max_tokens 700)
```

### Components

**1. Six new sources — `Tools/n8n/prompts/panel/<id>.md`**

One per vertical: `auto_parts`, `wholesale`, `flowers`, `kaspi_seller`, `education`,
`phone_repair`. Target ~600–900 chars each. Deliberately **not** composed with `_core.md`.

Authoring rules, applied to all six:

- **First-person escalation.** «уточню и напишу», never «передам владельцу»/«уточнит
  менеджер». The owner is the sender.
- **Every rule names the label it should produce**, tying niche knowledge to the existing
  closed taxonomy (`Ответ`, `Уточнить`, `Вариант`, `К заказу`, `Отложить`, `Отказ`).
- **No instruction implying a message over 220 characters.** Drop the Авто prompts'
  «назови 2–3 варианта» and multi-field intake blocks; a four-field intake becomes one short
  «К заказу» question.
- **Keep the НЕЛЬЗЯ list.** It is the highest-value part for grounding and it transfers
  unchanged in intent.

Illustrative — `panel/auto_parts.md`:

```
Ниша: автозапчасти.
Цену и наличие не называй, пока не известны марка, модель и год авто
(при необходимости — объём). Не хватает — это карточка «Уточнить».
Деталь не подбирается однозначно — «Уточнить»: попроси VIN.
Цену и артикул бери только из ДАННЫЕ; клиенты сверяют по артикулу,
поэтому не округляй и не выдумывай его.
«Оригинал или аналог?» — только по пометке в ДАННЫЕ; пометки нет — «Уточнить».
«Точно есть в наличии?» — прайс отстаёт от склада: не подтверждай наличие
как факт, предложи проверить («Отложить»).
Клиент готов купить — «К заказу»: имя, телефон и авто одним коротким вопросом.
Гарантия, возврат, установка — если этого нет в ДАННЫЕ, «Отложить»,
не придумывай условия.
```

**2. New injector — `Tools/n8n/inject-panel-prompts.py`**

Mirrors `Tools/n8n/inject-prompts.py` in behaviour and style:

- Target: `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`, node located **by name**
  (`"Assemble"`), never by index.
- Generates a `PANEL_PROMPTS` object between `// PANEL-PROMPTS-BEGIN` / `// PANEL-PROMPTS-END`
  markers, matching the existing `PROMPTS-BEGIN/END` convention.
- Idempotent; `--check` writes nothing and exits 2 if any target would change, 0 if current.
- Fails loudly if the node, the markers, or the expected shape are missing.

**3. `Assemble` node edit**

- `const HINTS = {…}` (hand-maintained) → the generated `PANEL_PROMPTS` map.
- `const hint = HINTS[p.businessTypeId] || ''` → `const niche = PANEL_PROMPTS[p.businessTypeId] || ''`.
- The single-line push becomes a block push, under a subordinating header.

### Placement and precedence

The niche block occupies **exactly the slot `НИША` occupies today**: after `МЕДИА` and
`ПРИВЕТСТВИЕ`, before `НАПРАВЛЕНИЕ` (the refinement steer) and `ДОП. ИНСТРУКЦИИ`
(the owner's Промпт).

Its header states subordination explicitly, mirroring how `ownerPrompt` is already framed:

> `НИША (правила твоей отрасли — применяй при выборе ходов и содержания карточек; формат, длина и грануляция из правил выше ГЛАВНЕЕ):`

This clause is load-bearing. Niche prose inevitably carries reply-shaped instructions; without
explicit subordination those will fight the panel's hard rules — the ≤220-char cap, 1–3
sentences, the closed label set, and the JSON schema. The panel's format rules must win in
every conflict.

`businessTypeId` empty or legacy (`car_service`, `dentist`, …) → **no block emitted at all**.
This matches current behaviour and is consistent with `Manager.ApplyBusinessTypeToDropdown`
deliberately sending `""` for pre-vertical bots so their prompt head is never silently
migrated.

### Error handling and cost

- Unknown or empty id → no niche block; the panel behaves exactly as it does today. No error
  path, no server-side failure mode introduced.
- The injector is the only new failure surface, and it fails at authoring time, loudly.
- Token cost: roughly +700 chars ≈ +200 input tokens per suggestions request on gpt-4o-mini.
  The panel is on-demand, not on the hot bot-reply path. `max_tokens` stays 700.
- Sanitisation is unaffected: the niche text is a server-side constant, never user input. The
  existing `БЕЗОПАСНОСТЬ` fence around the `ДАННЫЕ` block is untouched.

## Testing

**1. Idempotency gate.** `python3 Tools/n8n/inject-panel-prompts.py --check` exits 0 on a
clean tree; exits 2 if the sources and the workflow JSON have drifted apart.

**2. Composed-value assertion.** A Node.js harness — `Tools/n8n/verify-panel-prompts.js` —
executes the **real** `Assemble` `jsCode` extracted from the workflow JSON, with stubbed
`$()` / `$input`, and asserts on the produced `systemPrompt` string.

A *sentinel* is a distinctive verbatim substring of each panel prompt, declared once in a
fixture table at the top of the harness (e.g. `auto_parts` → `"попроси VIN"`). Sentinels must
be unique across the six files; the harness fails if two collide, so the table cannot silently
rot as copy is edited.

- for each of the 6 ids: `systemPrompt` contains that vertical's sentinel **and** the
  subordinating `НИША (` header;
- for `""` and for a legacy id (`car_service`): contains **no** `НИША` block and **no**
  sentinel from any vertical.

This asserts the composed **value**, not merely that the map is populated — the specific gap
that let a Phase-10 payload ship wrong through four passing verification layers.

**3. Live A/B probe.** `Tools/n8n/probe-suggest-replies.py` against the dev instance
(`http://localhost:5678`, confirmed reachable) before and after the change.

- Reuse `K_kaspi_installment` — covers the рассрочка/payment edge.
- **Add an `auto_parts` intake probe** — the vertical with the heaviest intake rules and no
  probe coverage today. It should show a first card of «Уточнить» asking for марка/модель/год
  rather than a guessed price.
- Structural checks stay hard failures; heuristic checks remain WARN-only, per the harness's
  existing contract (temperature 0.4 output is sampled, not deterministic).

## Deployment

Canonical workflow JSON committed to git, then pushed to the local dev n8n instance. Prod
(`bagkz`) is dormant and replication is parked — not touched by this change.

## Out of scope

The panel's two other asymmetries with Авто, both deliberately excluded so the A/B stays
readable:

- **RAG depth** — Авто uses `retrieve-as-tool` at topK=10 and can query repeatedly with
  model-chosen queries; the panel does one automatic retrieval at topK=5 on the client's
  trailing message run.
- **Voice transcription** — Авто transcribes audio; the panel sees `[голосовое сообщение]`
  and is instructed to ask for text.

Each warrants its own spec if wanted.
