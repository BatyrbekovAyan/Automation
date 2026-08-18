# Suggestions Panel: Drill Rounds + Free-Form Titles — Design

**Date:** 2026-08-18
**Status:** Approved (owner picked Approach B + defaults, this doc)
**Scope:** «Вместе» suggestions panel answer logic — n8n Suggest Replies workflow
(`9PTyYcelRQI7bGDb`, dev instance) + Unity client round plumbing. No prod touch (dormant).

## Problem

Owner's report (2026-08-18), confirmed against the implementation:

1. **Round 1 on a concrete question** — only card 1 answers it; cards 2–4 pad with
   off-topic moves. Root cause is structural: the prompt AND the Validate node AND the
   LLM strict `json_schema` all force every card's `label` to a distinct value of the
   closed 6-move enum (`Ответ/Уточнить/Вариант/К заказу/Отложить/Отказ`) — 4 cards
   literally cannot be 4 versions of the one right answer.
2. **Rounds 2+ stay generic** — a pick sends `steerTowardText`, and the НАПРАВЛЕНИЕ
   prompt block even asks for «точнее/теплее/короче», but «метки всё так же из списка,
   все разные» drags the set back to breadth every round.
3. **Trivial messages** («спасибо») return 1–2 cards by an explicit prompt rule; the
   owner wants all 4 slots useful — tone/length variants.
4. **Header** — after picking e.g. a price direction, the panel header still reads
   «ПРЕДЛОЖЕНИЯ»; the owner expects the picked direction's title («ЦЕНА») with the
   next round's cards titled inside that direction.

## Decision (Approach B — move/title split)

Each suggestion becomes `{text, label, move}`:

- **`move`** — the existing closed 6-enum, demoted to an INTERNAL taxonomy field.
  Strictly validated, **repeats allowed** within a round. Keeps three load-bearing
  consumers intact with zero migration: the 6 niche panel prompts (they instruct by
  move — «ПЕРВОЙ карточкой ставь „Уточнить"» now governs card 1's move), pickStats
  preference learning (existing `{bot}SuggestPick{move}` PlayerPrefs keys), and strict
  server validation.
- **`label`** — free-form Russian display title, 1–3 words, ≤24 chars after trim,
  distinct within a round (case-insensitive). Round-1 explore titles name topics
  («Цена», «Наличие»); drill titles name the variation axis («Коротко», «Теплее»,
  «С вопросом», «Со скидкой»).

Rejected: A (delete moves — orphans niche prompts + pickStats, weakens validation),
C (client fires 4 toned requests — 4× cost/latency).

## Behavior spec

### Round modes

Every generated set is **explore** (breadth) or **drill** (depth). The MODEL decides
per rules injected in the Assemble prompt; the server passes no explicit mode flag and
the wire REQUEST is unchanged:

- `steerTowardText` present → always **drill** toward the picked text.
- Fresh request → model counts genuinely distinct, useful directions for the client's
  last message: **≥2 → explore** (one card per direction, topic titles; 2–4 cards,
  no padding); **exactly 1 → drill immediately** (concrete question, trivial reply).

### Drill requirements

- Aim for **4 cards, all strictly inside the direction**; never re-broaden. Card 1
  remains «заметно улучшенная версия выбранного» (existing rule kept) and is the
  recommended tinted card.
- Each card varies deliberately on at least one named axis — длина / тон / формат /
  следующий шаг — and its `label` names that difference. Moves may repeat.
- Trivial messages («спасибо», «ок») are round-1 drills: 4 variants of the
  acknowledgment differing in tone/length (replaces the old «1–2 карточки» rule).
- Abstain (empty `suggestions` + `abstain:true`) remains ONLY for non-business
  messages; unchanged envelope and client mapping to the quiet Empty state.

### Header

- Round 1: «ПРЕДЛОЖЕНИЯ» (default), including auto-drill round 1.
- After a pick: header = the picked card's `label`, uppercased. Round 3+: latest
  pick's title only — no breadcrumb (the ‹ chevron communicates depth; the overline
  is 28u).
- ‹ back restores the previous round's cards AND header; new incoming / chat open /
  toggle-on / answered-run reset → default header.
- Titles and header are always Russian (owner-facing UI, like today's enum labels);
  card TEXTS keep mirroring the client's language per the existing СТИЛЬ rule.

## Wire contract

- **Request: UNCHANGED.** Frozen v1 + additive v1.1/v1.2 keys stay byte-identical;
  `SuggestRepliesPayloadTests` untouched. `steerTowardText` (picked card's text,
  server-clamped 500) already carries the drill direction.
- **Response:** suggestion objects gain `move` (enum-validated); `label` becomes
  free-form. Envelope (`v/requestSeq/error/abstain/suggestions`) unchanged.
- **Compatibility:** old app + new server — Json.NET ignores the unknown `move`,
  free-form labels render fine (legend is a plain TMP). New app + old server —
  `move` absent → RecordPick falls back to counting `label` when it happens to be
  one of the 6 enum values, else skips. No version bump needed.

## Server changes (canonical `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`)

All four node edits land in the canonical JSON (single source of truth), deployed to
dev via `build-suggest-replies.py --update 9PTyYcelRQI7bGDb`.

### Assemble (prompt restructure)

Marker discipline: the `PANEL-PROMPTS-BEGIN/END` map and `PANEL-NICHE-PUSH-BEGIN/END`
push site stay byte-identical (the injector + `verify-panel-prompts.js` must stay
green). The НИША header line is untouched — its «каким ходом отвечать» wording still
resolves against moves.

Section rewrites (draft RU wording, refined during implementation against probes):

- **ХОДЫ** → same 6 definitions, reframed: «ход — внутренняя классификация карточки,
  поле move; повторы допустимы». Drop «все метки разные» (moves may repeat).
- **NEW ЗАГОЛОВКИ** (after ХОДЫ): «label — короткий заголовок карточки для владельца:
  1–3 слова, ≤18 символов, по-русски, без кавычек/эмодзи/точки. В раунде без
  НАПРАВЛЕНИЯ — тема карточки („Цена", „Наличие"); в раунде с НАПРАВЛЕНИЕМ — чем
  карточка отличается („Коротко", „Теплее", „С вопросом"). Все label в наборе разные.
  Не используй названия ходов как заголовки.»
- **NEW РЕЖИМ** (replaces the ranking paragraph's count guidance): fresh request →
  «сначала определи, сколько СУЩЕСТВЕННО разных полезных направлений ответа есть у
  последнего сообщения. Два и больше — по одной карточке на направление (2–4, не
  добивай ради количества). Ровно одно (конкретный вопрос, простая реплика) — сразу
  4 варианта одного ответа: разная длина, тон, формат, следующий шаг.»
- **НАПРАВЛЕНИЕ** (drill) rewritten: «ВСЕ карточки строго внутри выбранного
  направления — не расширяй тему. 4 варианта, каждый осознанно отличается по одной
  из осей: длина / тон / формат / следующий шаг; label называет отличие. Карточка 1 —
  заметно улучшенная версия выбранного текста (не дословный повтор). Ходы могут
  повторяться.»
- **ТРИВИАЛЬНЫЕ** flipped: «на „спасибо/ок" дай 4 коротких варианта разного тона и
  длины (тёплый / деловой / с продолжением диалога / с следующим шагом, если сделка
  в процессе). Воздержание — только для сообщений не по адресу бизнеса.»
- **ВЫВОД**: «0–4 объектов {text, label, move}».
- ПРЕДПОЧТЕНИЯ block: unchanged (pickStats still counts ходы).

### LLM + LLM Retry (strict json_schema)

- Schema items: `required:["text","label","move"]`; `move` gets the 6-value enum;
  `label` becomes a plain string (OpenAI strict mode has no `maxLength` — length is
  Validate's job).
- LLM Retry's hardcoded correction message rewritten: moves from the list (repeats
  OK), labels — short distinct RU titles, not move names; 1–4 objects or empty array.

### Validate + Validate 2 (identical twins — edit both, keep `===`)

- Map items to `{text, label, move}`; text rules unchanged (strip markdown, ≤300,
  non-empty).
- `move` ∈ ENUM required; the distinct-MOVE check is REMOVED.
- `label`: trim, strip the same markdown chars, **clamp to 24**, non-empty required,
  distinct required after `toLowerCase()` (display is uppercase — «Цена»/«ЦЕНА»
  dupes must not slip through).
- Violation strings updated («ход вне списка», «заголовки повторяются», …); retry +
  abstain + `parsedOk` flow byte-for-byte otherwise.
- `Build Response` untouched — it passes Validate's items through, so `move` rides.

## Client changes

- **`SuggestionItem`** gains `public string move;` `SuggestRepliesDtos.SuggestReplyDto`
  gains `move`. **`MapResponse`** copies it tolerantly (absent → null); filter still
  requires only text+label non-empty.
- **`RecordPick`** counts `item.move` when non-empty; else falls back to
  `item.intentLabel` if it's one of the 6 (old-server compat, keeps the provider's
  `MoveLabels` mirror as the shared constant); else skips. Keys unchanged.
- **`SuggestionsPanel`**: `[SerializeField] private TMPro.TextMeshProUGUI headerTitle;`
  + `public void SetHeaderTitle(string title)` — null/empty → literal «ПРЕДЛОЖЕНИЯ»
  (const), else `title.Trim().ToUpperInvariant()` (Cyrillic-safe), defensively sliced
  to 26 chars. No other view change; the legend TMP renders free-form labels as-is.
- **`SuggestionRoundStack`**: entries become `(result, steer, header)`;
  `Push`/`TryPop` signatures extended; MaxDepth/Clear semantics unchanged.
- **`SuggestionsController`**:
  - New `_currentHeader` (null = default). `HandleCardTapped` resolves the tapped
    item ONCE (single scan shared with RecordPick), pushes
    `(_currentRendered, _currentSteer, _currentHeader)`, sets `_currentHeader` to the
    picked label, calls `_panel.SetHeaderTitle`.
  - `HandleBack` restores the popped header; `StartFreshRound` resets header to
    default and calls `SetHeaderTitle(null)`.
  - **Cache fresh-only**: `IssueRequest` threads `bool fresh = steerTowardText == null`
    into `OnResult`; `_cache.Store` is skipped for steered results. Rationale: a
    re-opened chat must never restore a mid-drill set whose steer/back context is
    gone (today it can). `TryRenderCached` renders under the default header.
- **`MockSuggestionsProvider`**: items gain moves (labels already free-form topic
  titles); steered set's titles become variation-style for editor parity.

## Scene wiring

`SuggestionsPanelBuilder.cs` carries uncommitted parallel-session edits (ScrollTopInset
work) — do NOT touch it now. The scene is wired by a new one-shot additive editor
wirer `Tools/Suggestions/Wire Header Title` (own file): finds the panel's existing
`Header/Title` TMP via the `SuggestionsPanel` component, assigns the new
`headerTitle` serialized field via SerializedObject, saves the scene; commit the
scene + .cs together immediately (parallel-scene-clobber rule). Follow-up (after the
parallel builder work lands): fold the same stamping into `BuildHeader` so a future
rebuild can't orphan the reference — tracked in the plan, not done in this change.

## Verification

- **EditMode** (bridge/headless, gate on `total`):
  - `SuggestRepliesMapTests`: `move` mapped; absent `move` → null; envelope rules
    unchanged.
  - `SuggestionRoundStackTests`: header rides push/pop; cap eviction keeps triples.
  - The pick-resolution rule (move preferred → enum-label fallback → skip) is
    extracted as a pure static seam on the controller (the `FoldLiveBatch` pattern)
    with its own tests; `MockSuggestionsProviderTests` updated for the new item shape.
  - Existing `SuggestRepliesPayloadTests` must pass UNCHANGED (request frozen).
- **Server gates** (dev n8n):
  - `node Tools/n8n/verify-panel-prompts.js` — niche blocks still present in the
    composed systemPrompt.
  - `probe-suggest-replies.py` updated: `card1_clarifies` asserts `move`;
    STRUCT adds move∈enum + label distinct/≤24; the trivial/filler probe now expects
    4 distinct-titled variants; new drill probe posts `steerTowardText` and asserts
    4 cards, labels ≠ move names, texts within direction (heuristic). Run before
    (baseline) and after.
- **Device pass** (owner): concrete price question → 4 on-topic cards; pick a topic
  card → header flips to its title + 4 within-direction variants; ‹ restores cards
  and header; «спасибо» → 4 tone/length variants; abstain unchanged on spam;
  header resets on new incoming message.

## Not changing

Keyboard-slot chassis and all slot/keyboard choreography; card visuals/tap flow;
no-auto-send; refresh = re-roll current round; debounce/coalescing; frozen request
payload; `SuggestionCache` class; prod instance (dormant).

## Risks

- Model uses a move name as a title → prompt forbids it explicitly; acceptable if it
  slips (still a valid title).
- Title quality/length → prompt asks ≤18, Validate clamps 24, panel slices 26.
- Drill sets less diverse than intended → probe H/new drill probe watch it;
  temperature stays 0.4.
- Round-1 explore over-eagerly drilling (model miscounts directions) → probe matrix
  keeps a multi-direction case asserting ≥2 distinct topics.

## Amendment (2026-08-18, during execution)

Two deviations from the section «Validate + Validate 2 (identical twins — edit both,
keep ===)», both shipped and review-verified:

1. **The twins are now deliberately asymmetric** (commit 94e772d). Probing found
   J2-class requests (drill variants converging on one natural title — «Завтра» for a
   closed-now hours question) burned the single retry on the strict distinct-title rule
   and returned `generation_failed` ~50% of the time. Validate (pre-retry) stays strict
   so the retry still pushes for 4 distinct titles; Validate 2 (post-retry) now dedupes
   casefolded title collisions keep-first and slices to 4 — a title collision must never
   cost the owner the whole round. Moves stay enum-hard in both. 6× targeted soak: 0
   errors; 32k-case simulation: the lenient path never diverges on sets the strict path
   accepted.
2. **ЗАГОЛОВКИ gained an axis-title clause** (Task-9 tune + 94e772d): titles for
   convergent cards name the DIFFERENCE («Коротко», «Подробнее», «Теплее»,
   «Официально»), not the shared theme, and must not literally equal a move name.

Also corrected during execution (commit 4892ba7): the plan's `StartFreshRound` block
had dropped the pre-existing `_currentRendered = null;` — restored (a pick on a
cache-restored set must not push the previous chat's round).
