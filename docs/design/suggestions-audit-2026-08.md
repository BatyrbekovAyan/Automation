# Reply-Suggestions Audit («Вместе» mode) — 2026-08-10

Deep evidence-based audit of the full suggestions loop: app pipeline, n8n generation,
UI, and owner journeys. Benchmarked against Gmail Smart Reply/Compose, Superhuman,
Intercom copilot inbox, Front — adapted to a one-handed owner answering WhatsApp/Telegram
customers mid-task. **13 live probes** were run against the dev Suggest Replies workflow
(localhost:5678, results in this doc; raw JSON in the audit session scratchpad).

Scope: the messages-page suggestions sheet (winner-P chassis, LOCKED). Not the
Bot-Settings prompt chips.

---

## 1. Verdict

The engineering skeleton is excellent and the happy path already works: sequencing/staleness
guards are airtight (`SuggestionSequenceGuard`, captured-chat discard), burst debounce is
correct after three hard-won rounds, Kazakh mirroring works, catalog-priced questions get
accurate, well-toned, genuinely intent-diverse cards in ~3s server-side. The **weakest link
is trust at the exact moment it matters most: when the model doesn't know something, it
confidently invents it — and puts the invention in the green "recommended" slot.** Live
probes produced a fabricated street address + closing hours, a fabricated "yes, we're open
right now", a false "no installments" from a Kaspi seller, and a false "no tulips in stock" —
all as card 1. The root cause is structural, not model quality: the suggestions payload never
receives the business description, contacts, or hours that the Авто-mode bot already gets
(`ComposeBusinessKnowledge` → `Business` field), and the grounding rule only covers prices.
Second weakest: the loop's *feel* — every chat open regenerates from scratch behind a
3–7s skeleton, and a card tap leaves the sheet up (screen fully occupied by
keyboard + sheet) instead of sliding away as the locked spec intends. Fix grounding, fix the
tap hand-off, add cache — and this is genuinely a best-in-class assist.

---

## 2. Fact base — the actual state machine

### Request lifecycle (app)

| Trigger | Path | Behavior |
|---|---|---|
| Chat open (Вместе) | `SuggestionsController.RestoreForActiveChat` [SuggestionsController.cs:108-130](../../Assets/Scripts/Chat/SuggestionsController.cs) | cancel pending window → read `SemiAutoStore.IsOn` → show panel + `IssueRequest(null,null)` — **fresh LLM call every open, skeleton every time** |
| Toggle ON | `HandleToggle` :134-153 | persist tri-state override + fire-and-forget `SyncReplyMode` → show + request |
| Toggle OFF | :147-152 | seq bump (supersede in-flight) + cancel window + hide |
| Incoming message | `HandleLive` :214-224 → `FoldLiveBatch` :253-277 | fragments accumulate in `_pendingIncomingText`; 2.5s `IncomingDebounceGate` window re-arms per fragment; outgoing echo = run boundary (clears pending + cancels window) |
| Window settles | `DebounceLoop` :296-311 | ONE coalesced `IssueRequest(null, pendingBurst)`; pending deliberately survives the fire |
| Card tap | `HandleCardTapped` :201-210 | composer `.text` overwrite + `ActivateInputField()` + re-cluster `IssueRequest(steer)`; **sheet stays open, goes to skeleton** |
| Manual refresh / error retry | :349-352 | immediate request |
| Result | `OnResult` :190-197 | discarded unless `_semiAutoOn` AND seq newest AND chat unchanged (`SuggestionSequenceGuard.IsCurrent`) |

Provider (`N8nSuggestionsProvider`): coroutine on always-active ChatManager, waits
`WaitForChatFetchesDrain()` (0s when idle), re-resolves + `TryGetRecentMessages(chatId, 12)`
with chat-mismatch guard → POST `/webhook/SuggestReplies`, timeout 30s
[N8nSuggestionsProvider.cs:52-104](../../Assets/Scripts/Chat/N8nSuggestionsProvider.cs).
Payload: `v, requestSeq, profileId, chatId, botWaId, botTgId, channel, businessTypeId,
businessName, ownerPrompt≤500, catalog≤1500 (products+services "• name — price"),
steerTowardText, lastIncomingText (accumulated burst), messages ≤12 ×500ch` with RU media
placeholders (`MediaText` :207-223). Response mapping is lenient 1–4, >4 clamped, 0/error →
Error status (:241-258).

### Generation (n8n `9PTyYcelRQI7bGDb`, verified ACTIVE on dev)

`Webhook → Prep → If invalid? → If skipRag? → [Supabase RAG topK5, WA/TG branch by channel]
→ Assemble → LLM (gpt-4o-mini, temp 0.4, max_tokens 700, strict json_schema) → Validate
(exactly 4, labels from closed 6-enum, pairwise-distinct, text non-empty, markdown stripped,
≤300ch) → retry once (temp 0.2 + violation feedback) → generation_failed envelope`.

Prep re-derives `queryText` = trailing client run (server-side walk) merged per-line with the
client's accumulated burst. The system prompt (Assemble node): moves taxonomy
(Ответ/Уточнить/Вариант/К заказу/Отложить/Отказ), "card 1 = what you'd send yourself",
relevance-to-last-message rule, grounding rule (**prices/availability/terms from ДАННЫЕ
only**), style rule (mirror RU/KZ + ты/вы, 1–3 sentences ≤220ch, ≤1 emoji), one-line vertical
hint from a hardcoded 6-entry `HINTS` map, steer block, ownerPrompt block, injection fence,
trivial-message rule. Fenced user data: `businessName, catalog, ragChunks≤4000, messages,
lastClientMessage, steerTowardText`.

**What the model NEVER sees** (but the Авто-mode bot does, via `ComposeBusinessKnowledge` →
`Business` form field, [Manager.cs:889-911](../../Assets/Scripts/Main/Manager.cs) +
:3405/:3563/:3809): the business **description**, **Телефон / Часы работы / Адрес /
Instagram / Email**, the full vertical prompt (`Tools/n8n/prompts/<id>.md`), and the current
time.

### Mode plumbing (verified)

Per-bot default: `ReplyModeToggleBinder.GetMode` — **unset reads Semi** («Вместе» is the
default for new bots) [ReplyModeToggleBinder.cs:63-65](../../Assets/Scripts/UI/ReplyModeToggleBinder.cs);
enable-auto confirms, disable is instant (locked asymmetry). Per-chat: `SemiAutoStore`
tri-state inherit/off/on. Server suppression: `Manager.SyncReplyMode` fire-and-forget +
re-assert-on-open heal for explicit overrides only (WR-01)
[SuggestionsController.cs:118-123]. **Both `Suggest Replies` and `Set Reply Mode` are
deployed and ACTIVE on dev n8n (checked live via API 2026-08-10)** — the "SetReplyMode
deploy open" note in older memory is stale. (Prod replication is a separate, owner-parked
track.)

### Legacy overlap

`QuickReplyPanel`/`QuickReplyButton` are the pre-v1.0 4-button grid: referenced only by
`MessagesBottomPanel.quickReplyPanel` [MessagesBottomPanel.cs:18] + 2 scene objects; **no
caller ever invokes `SetReplies` with live data** — dead code, safe to delete with its scene
objects.

---

## 3. Findings, ranked (owner impact × frequency)

Grades: effort S ≤ ½ day, M ≤ 3 days, L > 3 days.

### Group A — Trust / AI content

**F1. The recommended card confidently fabricates business facts the payload doesn't contain.**
*Evidence:* live probes — B: «Наш адрес: ул. Цветочная, 10. Сегодня работаем до 19:00» (both
invented) as card 1; J: «Да, мы работаем! Вы можете подъехать» (no hours, no clock in
payload) as card 1. Prompt's ФАКТЫ rule (Assemble node) covers only «цены, наличие и
условия». *Why it matters:* the green tint says "send this"; a one-handed owner mid-task
will. A customer sent to a nonexistent address is the single worst outcome for an app whose
north star is trust+control. Address/hours are among the most frequent SMB questions —
high frequency × catastrophic impact. *Fix:* F2 (data) + prompt rewrite: "ЛЮБОЙ факт о
бизнесе (адрес, часы, телефон, наличие, условия, политика) — только из ДАННЫЕ; нет факта →
карточка обязана быть «Уточнить» или «Отложить»". Effort **S** (prompt) — but only honest
together with F2. Risk: low.

**F2. Grounding data gap: suggestions are blind to what the app already knows.**
*Evidence:* `BuildPayloadJson` sends `businessName + ownerPrompt + catalog` only
[N8nSuggestionsProvider.cs:148-180]; the Авто bot additionally gets description + the
contacts block ([Manager.cs:889-911], device-verified per CLAUDE.md) and the full vertical
prompt. So the same customer question gets a correct answer in Авто and a fabrication/shrug
in Вместе. *Fix:* provider reads the six PlayerPrefs values (same keys as
`LoadContactFields`, Manager.cs:929) and calls the pure
`ComposeBusinessKnowledge(description, phone, hours, address, instagram, email)`
(Manager.cs:889); send as new additive key `businessKnowledge` (≤1200ch clamp); Prep passes
through; Assemble adds it to the fenced ДАННЫЕ. Additive wire change — v1 contract intact.
Effort **S/M** (client S + workflow S + tests). Risk: low. **This is the single
highest-value change in the audit.**

**F3. Absence-in-data is asserted as absence-in-reality (false negatives that lose sales).**
*Evidence:* probe A: «тюльпанов нет в наличии» (catalog just doesn't list them) as card 1;
probe K: «у нас нет возможности оформить рассрочку» from a *Kaspi seller* — contradicting
the vertical's core selling point and the vertical hint itself. *Why:* false "no" loses the
sale silently; the owner may tap it while rushing. *Fix:* prompt clause: «Каталог и прайс
могут быть НЕПОЛНЫМИ. Отсутствие позиции в ДАННЫЕ ≠ отсутствие в наличии — в этом случае
«Уточнить»/«Отложить», не утверждай "нет"»; harden the kaspi_seller hint into a stated fact
(«рассрочка через Kaspi всегда доступна — оформление в магазине на Kaspi»). Effort **S**.
Risk: low.

**F4. Ranking is confidence-blind — the honest card sits at #2 when the model can't know.**
*Evidence:* probes B/D/J: the correct «Уточнить» option exists but ranks below a fabricated
«Ответ». Prompt says "card 1 = what you'd send" with no uncertainty policy. *Fix:* prompt:
«Если для уверенного «Ответ» не хватает данных — первой ставь «Уточнить» (или «Отложить»);
«Ответ» первой ТОЛЬКО когда каждый факт в нём взят из ДАННЫЕ». Pairs with the locked
tint-not-% display: ranking honesty IS the confidence display. Effort **S**. Risk: model
over-asks clarifying questions — watch via M-metrics, tune wording.

**F5. Re-cluster card 1 echoes the picked text back.**
*Evidence:* probe H: card 1 ≈ the steer text verbatim; the owner already has that text in
the composer, so the refresh adds nothing. *Fix:* steer block addition: «Не повторяй
выбранный текст дословно — карточка 1 = заметно улучшенная версия (точнее/теплее/короче),
остальные — развитие и следующий шаг». Effort **S**. Risk: none.

**F6. Media messages produce over-confident cards about content the model can't see.**
*Evidence:* probe D («[фото] такой есть в наличии?»): card 1 answers "Да, у нас есть…" about
a photo it never saw; honest «Уточнить» ranks 2nd. Probe C (voice) degrades gracefully but
generically. *Fix (near-term):* prompt: «Если последнее сообщение — [фото]/[голосовое
сообщение] без текста вопроса, первой карточкой уточняй содержимое; не утверждай ничего о
том, что на фото/в голосовом». Effort **S**. (Transcription = bold bet B2.) Risk: none.

**F7. Minor style defects.** Greeting repeated in all 4 cards (probe G: «Сәлем!» ×4);
occasional filler card from the forced-distinct rule (probe E's tone-deaf upsell «Вариант»
to an angry customer; probe M's generic «Отложить»). *Fix:* prompt: «Приветствие — максимум
в одной карточке, и только если клиент поздоровался первым и вы ещё не здоровались»; and see
Q2 (allow 3 cards when the 4th is filler). Effort **S**.

**F8. Context ceilings are tight for real threads.** History = last 12 msgs ×500ch
(`MaxMessages` [N8nSuggestionsProvider.cs:25] AND server `slice(-12)` in Prep — change both);
`ownerPrompt` clamped 500 (chips-composed prompts now easily exceed this — silent mid-line
truncation); catalog 1500. *Fix:* 12→24 messages, prompt 500→2000, catalog 1500→2500. Cost
at gpt-4o-mini prices is negligible. Effort **S**. Risk: none measurable.

### Group B — Timing & lifecycle

**F9. No caching: every chat open regenerates behind a skeleton.**
*Evidence:* `RestoreForActiveChat` → `IssueRequest` unconditionally
[SuggestionsController.cs:124-129]; `IssueRequest` → `ShowSkeleton()` :179. Measured
generation 2.5–3.7s + network. Morning-rush across 10 chats = 10 paid calls + 10 waits for
suggestions that usually haven't changed. Gmail/Front show drafts instantly. *Fix:* per-chat
result memo `{chatId → (lastMsgId/ts, SuggestionResult)}` in the controller (session-scope,
no persistence needed); on open, if history tail unchanged → `Render(cached)` instantly and
**skip the request entirely**; else skeleton as today. Invalidate on incoming/outgoing echo.
Effort **M**. Risk: stale-cache bugs — the tail-id check is the guard; seq guard already
covers races.

**F10. Time-to-usable-suggestion after a customer message: ~5.5–8.5s of bouncing dots.**
*Breakdown:* 2.5s debounce (correct, keep) + LLM 2.5–3.7s measured + network/drain. During
it the sheet clears to skeleton — including cards the owner may be reading. *Fix (inside
locked chassis — states already swap inside the viewport):* stale-while-revalidate rendering:
on a NEW-message re-request keep previous cards visible, dimmed ~60% alpha + the refresh icon
spinning; swap when the result lands. The seq guard already prevents wrong-chat renders; the
dim state signals "these answer the previous message". Effort **M**. Risk: owner taps a
dimmed stale card — acceptable (it inserts into composer, nothing sends); dimming + instant
un-dim on arrival mitigates.

**F11. In-flight results land after the owner already answered manually.**
*Evidence:* outgoing echo cancels only the debounce *window* — `fold.Cancel` →
`_debounce.Cancel()` [SuggestionsController.cs:219]; an already-fired request's `_requestSeq`
is NOT bumped, and `OnResult` checks only seq+chat :190-197 → cards for the answered burst
render seconds after the owner's own reply. *Fix:* in `HandleLive`, when the fold reports an
outgoing boundary, also `_requestSeq++` (supersede in-flight) — one line, mirrors the
toggle-off path :148. Effort **S**. Risk: none (a post-reply set is noise by the feature's
own definition, :245-247).

**F12. Post-send the sheet stays up showing variants of the already-sent reply.**
*Evidence:* card tap → re-cluster set renders; owner edits + sends; nothing hides or clears
the panel (send path in `MessagesBottomPanel` has no suggestions interplay; `HandleLive` echo
only cancels the window). The owner is left with 4 alternatives to a message that's already
gone. *Fix:* on outgoing echo, soft-dismiss via the existing `SetSheetOpen(false)` (routes
the list floor correctly — hard constraint honored); next incoming re-opens with fresh cards.
Effort **S**. Risk: owner who wants the sheet persistent can re-open via the composer ✦
toggle; watch in device pass.

### Group C — Interaction & UX

**F13. Card tap leaves the sheet open — locked spec says it slides away — and the edit
moment buries the customer's message.**
*Evidence:* `HandleCardTapped` [SuggestionsController.cs:201-210] never hides;
the locked winner-P spec states "Tap → text into composer, panel slides away"
(sketch-findings `references/suggestions-panel.md` §Cards + §Interactions). With the
keyboard up (tap calls `ActivateInputField`), keyboard (~850u) + composer (~204u+) + sheet
(852u) ≈ the full 1920u screen: **the owner edits a reply they cannot compare against the
question**. Gmail/Superhuman always keep the thread visible while editing. *Fix:* in
`HandleCardTapped`, after the composer write, call the soft dismiss (`SetSheetOpen(false)`);
keep issuing the re-cluster — it renders into the hidden panel (render-while-hidden works;
`RenderCards` only needs `activeInHierarchy` for the fade coroutine
[SuggestionsPanel.cs:127]) and is available on ✦ re-open. This *implements* the locked spec.
Effort **S**. Risk: re-cluster becomes invisible-by-default — that's Q4.

**F14. Composer write violates the iOS shared-keyboard-buffer discipline.**
*Evidence:* `_bottomPanel.inputField.text = replyText` then `ActivateInputField()`
[SuggestionsController.cs:205-206]. If the composer is **already focused** (owner was typing,
then taps a card — common), this writes `.text` into a focused TMP field — the exact
forbidden pattern from the input invariants (CLAUDE.md; `BotSettings.Prompts.cs` exists
specifically to do blur → wait-a-frame → write). Also no `KeyboardSelectionSync.Push` after
the programmatic caret move (TextSelection invariant #1; grep confirms only TextSelection/*
call it) — a stale native caret makes the next keystroke edit the wrong position
(device-verified bug class, 2026-08-07). *Fix:* focused-composer branch: release focus →
yield one frame → write → `ActivateInputField()`; after focus materializes, route the caret
via `KeyboardSelectionSync.Push`. Unfocused branch (panel tap with keyboard closed) is safe
today. Effort **S/M** + mandatory device pass. Risk: none if it mirrors the
BotSettings.Prompts coroutine exactly.

**F15. Chats-list gives zero triage signal for «Вместе» chats waiting on the owner.**
*Evidence:* list rows show unread badges only; nothing distinguishes "bot already answered"
(Авто) from "bot is deliberately silent, customer is waiting for YOU" (Вместе). With Semi as
the default for new bots, this is every chat. Intercom/Front inboxes lead with "needs you"
state. *Fix sketch (list row, not the locked panel):* 18u hollow-lamp dot (reuse
`ReplyModeToggleBinder.PaintChip` visual language, hollow = proposing) left of the timestamp
on rows where the effective mode is Semi AND last message is incoming; disappears once
answered. Effort **M** (row prefab + `ChatItemView` bind + mode lookup). Risk: row visual
noise — gate behind the same NotifPrefs family if contentious.

**F16. When the app is closed, nobody knows a customer is waiting.**
*Evidence:* no OS push anywhere (no notification package in `Packages/manifest.json`, no
scripts); the in-app cue (`NotificationFx`, gated by `IncomingNotifyPolicy`) fires only with
the app open. In Вместе the bot deliberately never answers — so an away owner = a silently
ignored customer, the exact failure the app exists to prevent. *Why ranked here and not #1:*
it's an ecosystem gap, not a defect of the loop itself, and it needs a product decision.
*Options:* (a) real OS push (infra: FCM/APNs + server hook at the workflow's `Suppressed?`
dead-end — **L**); (b) zero-infra nudge: the n8n suppression branch sends a WhatsApp
message **to the owner's own number** («Клиент ждёт ответа: <chat>») via the existing Wappi
profile — **M**, ships in days, uses the channel the owner already lives in; (c) accept +
document. Recommend (b) behind a Profile toggle. See Q3.

**F17. Small polish.** (a) Theme flip while cards shown leaves stale colors until next
render — `SuggestionCard.ApplyColors` runs only at `Setup`
[SuggestionCard.cs:40-53]; re-apply on `Theme.Changed` in `OnEnable`, effort **S**. (b)
Empty-state «Нет предложений» is a dead end while the header refresh sits far above it — add
the same ghost «Обновить» as the error state, **S**. (c) 30s timeout is a long skeleton;
server p95 ≈ 4s → drop `www.timeout` to 15 [N8nSuggestionsProvider.cs:93], **S**. (d)
`QuickReplyPanel` dead code removal, **S**.

### Group D — Reliability (mostly good news)

**F18. The concurrency story is genuinely solid** — monotonic seq + captured-chat guard
(`SuggestionSequenceGuard`), provider drain + post-drain re-resolve + request-scoped history
fetch, debounce cancel at all four lifecycle sites, `Time.realtimeSinceStartup` clock
discipline, lenient 1–4 response mapping with client-side Take(4) trust boundary. Keep. The
only lifecycle hole found is F11.

**F19. Suppression sync is fire-and-forget with a self-heal, but the failure window is
invisible.** `SyncReplyMode` never retries (by design, [Manager.ReplyModeSync.cs:108-121]);
a failed per-chat «Вместе» write means the bot may keep auto-replying while the UI shows
Вместе until the next chat open re-asserts (explicit overrides only). Accepted v1 design
(SUP-02/WR-01) — flag only: consider a silent re-assert also on app foreground. Effort
**S**. Risk: server write storm — cap to once per chat per session.

**F20. Dev-only fragility, not product:** tunnel-URL rotation (-1003) surfaces as the error
state; prod n8n cloud is stable. No action beyond the existing rotate-tunnel discipline.

---

## 4. "Do these first" — top 5

### #1 Ground the model in everything the app already knows (F1+F2+F3, prompt+payload)

**Client** (`N8nSuggestionsProvider`): read the six values with the same PlayerPrefs keys
`LoadContactFields` uses (Manager.cs:929) + `{botName}Business`; call
`Manager.ComposeBusinessKnowledge(...)` (pure static, Manager.cs:889); add to the DTO as
additive key `businessKnowledge` (clamp ≤1200) + `now` (device local time, `"yyyy-MM-dd
HH:mm"` + day-of-week, server can't know the owner's TZ). Additive keys keep the frozen-v1
identity test pattern (mirror the botTgId/channel precedent, SuggestRepliesDtos.cs:51-53).

**Workflow** (`Prep`): pass through `businessKnowledge` (slice 1200) and `now`.
**(`Assemble`)** add to fenced data: `businessKnowledge`, `now`; prompt diff:

```
- L.push('ФАКТЫ (ГРАУНДИНГ): Цены, наличие и условия — только из блока ДАННЫЕ (каталог и
-   выдержки из прайса). Если факта нет — карточка становится «Уточнить» или «Отложить».
-   Никогда не выдумывай цифры.');
+ L.push('ФАКТЫ (ГРАУНДИНГ): ЛЮБОЙ факт о бизнесе — цены, наличие, адрес, часы работы,
+   телефон, условия оплаты/доставки, скидки — только из блока ДАННЫЕ (описание бизнеса,
+   контакты, каталог, выдержки из прайса). Если факта в ДАННЫЕ нет — карточка обязана быть
+   «Уточнить» или «Отложить». Никогда не выдумывай цифры, адреса и часы.');
+ L.push('НЕПОЛНОТА: каталог и прайс могут быть неполными. Отсутствие позиции в ДАННЫЕ ≠
+   «нет в наличии» — не утверждай отсутствие, уточняй.');
+ L.push('ВРЕМЯ: сейчас ' + p.now + '. Вопросы «работаете ли сейчас» сверяй с часами работы
+   из ДАННЫЕ; если часов нет в ДАННЫЕ — «Уточнить»/«Отложить».');
```
Harden kaspi_seller hint: `'оплата и рассрочка — всегда через оформление в магазине на
Kaspi (рассрочка доступна у любого продавца Kaspi); в переписке оплату не принимаем.'`

**Verify:** re-run probes B/J/K/A — all four must stop fabricating; B must surface the real
address once the owner has filled contacts. Deploy: `build-suggest-replies.py --update
9PTyYcelRQI7bGDb` (owner-run per n8n discipline). Effort M total. **This single change
converts the worst probe failures into correct answers, because the data exists.**

### #2 Uncertainty-aware ranking + steer/media/greeting prompt block (F4+F5+F6+F7a)

One prompt edit, four clauses (final wording drafts in the findings above): uncertain →
«Уточнить» first; never echo steer verbatim; media placeholders → clarify-first, no claims
about unseen content; greeting at most once and only if unopened. Verify: probes D, H, G, B
re-run; add a "greeting twice" assert to the probe script. Effort S. Deploy same command.

### #3 Card tap = safe write + sheet dismissal (F13+F14)

`HandleCardTapped` becomes a small coroutine on the controller:
1. If `inputField.isFocused`: `ReleaseSelection`-style deactivate → `yield return null`
   (one frame — the BotSettings.Prompts pattern verbatim).
2. Write `inputField.text = replyText`.
3. `ActivateInputField()`; once focus materializes, sync the caret through
   `KeyboardSelectionSync.Push` (caret at end = `(len,len)`).
4. `SetSheetOpen(false)` — the locked "slides away" behavior; list floor animates via the
   existing route (hard constraint).
5. `IssueRequest(steer)` unchanged — renders into the hidden panel; ✦ re-opens it.
Device pass items: tap with keyboard already open (buffer resurrection / caret jump), tap
with TextSelection pins up in the composer (pins must clear), rapid double-tap two cards.
Effort S/M. No UI geometry changes — nothing to spec in reference units.

### #4 Instant cards on chat open — session cache (F9)

Controller-level memo: `Dictionary<string,(string tailMsgId, SuggestionResult result)>`.
On `RestoreForActiveChat`: if memo hit AND `TryGetRecentMessages` tail id matches →
`_panel.Render(cached)` and **no request**; else current behavior. Store on every `OnResult`
keep; invalidate the entry inside `FoldLiveBatch` consumers (any incoming or outgoing for
that chat). Bot-switch clears the whole memo (`ResetForNoOpenChat`). EditMode tests: hit,
miss-on-new-message, miss-on-manual-reply, bot-switch flush. Effort M. Expected effect:
morning-rush re-opens render in <100ms, ~60-80% fewer LLM calls.

### #5 Lifecycle tidy: supersede + soft-dismiss on outgoing echo (F11+F12)

In `HandleLive`, on fold outgoing boundary: `_requestSeq++` (kills in-flight renders) and
`SetSheetOpen(false)` (sheet rests while answered; next incoming re-opens via the existing
show path). Two lines + 2 EditMode tests (in-flight result discarded after echo; sheet
hidden state after echo). Effort S.

---

## 5. Quick wins (≤1 day each)

| Win | Where | Effort |
|---|---|---|
| Prompt clauses of #1/#2 (even before payload work — stops fabricated addresses via "no fact → Уточнить») | Assemble node | 2h |
| Kaspi hint hardening | Assemble `HINTS` | 15min |
| `_requestSeq++` on outgoing echo (F11) | SuggestionsController | 1h |
| History 12→24, ownerPrompt 500→2000, catalog 1500→2500 (F8) | provider consts + Prep slices | 2h |
| Timeout 30→15s (F17c) | N8nSuggestionsProvider.cs:93 | 5min |
| Theme re-apply on `Theme.Changed` (F17a) | SuggestionCard | 1h |
| Empty-state «Обновить» ghost button (F17b) | SuggestionsPanelBuilder | 2h |
| Delete QuickReplyPanel/Button + scene objects (F17d) | Chat/ + scene | 2h |
| Probe script → committed regression harness (`Tools/n8n/probe-suggest-replies.py`) run before every prompt deploy | Tools/n8n | 3h |

## 6. Bold bets

**B1. Owner-voice mirroring.** Harvest the owner's own outgoing texts (client already holds
them; sample ~30 recent `role=business` turns across chats, dedupe, ≤1500ch) → payload
`ownerStyleSample`; prompt: «Пиши в манере этих примеров владельца (длина фраз, эмодзи,
обращение)». Cost: M (client sampling + prompt). Payoff: the #1 gap vs "generic AI" —
suggestions start sounding like *this* shop; directly lifts sent-as-is rate. Risk: style
sample includes typos/slang — acceptable, it's the owner's voice.

**B2. Voice-note transcription.** On `[голосовое сообщение]` tail: server pulls the audio
via Wappi `message/media/download` (strictly serial per the crossing bug) → Whisper via the
existing OpenAI cred → transcript joins `queryText`. Cost: M/L (new server branch + media
handling + latency +2-4s). Payoff: voice is a huge share of CIS business chats; today those
chats get generic cards (probe C). Gate: only when last message is voice.

**B3. Follow-up nudge cards.** The Dashboard already classifies `client_silent` outcomes.
When the owner opens such a chat (or via a daily sweep), the sheet's first card becomes a
«Отложить»-class follow-up («Добрый день! Актуален ли ещё букет к пятнице?»). Cost: M
(reuse DashboardOutcomes plumbing + a prompt mode). Payoff: recovered sales — the only
feature here that *creates* revenue instead of saving time; no competitor in this niche has
it.

## 7. Metrics — proving helpfulness

Nothing is instrumented today (no analytics SDK in the project; verified). Five measures:

1. **Sent-as-is rate** — sends where composer text == last tapped card text (per-send string
   compare at `MessagesBottomPanel` send; equality after trim).
2. **Edit-then-send rate** + edit distance bucket (0 / ≤10ch / >10ch) — same hook.
3. **Tap-rank distribution** — which card index/label gets tapped (is card 1 earning its
   tint? target >55%; if «Уточнить» never gets tapped the ranking policy needs work).
4. **Manual-override rate** — sends in Вместе chats with cards visible but NO tap (the
   politest form of "suggestions weren't good enough").
5. **Time-to-reply delta** — median (send ts − last incoming ts) in Вместе chats vs the
   owner's pre-feature baseline; the headline number for the product.

**Cheapest instrumentation:** a `SuggestTelemetry` fire-and-forget POST (clone the
`SyncReplyMode` routine pattern) → tiny n8n webhook → Supabase table
`suggest_events(event, bot, chatHash, rank, label, editDist, dtMs, ts)`. Client hooks: 3
call sites (result rendered, card tapped, message sent). ~1 day total, zero hot-path risk.
Review weekly against probes; kill or double-down on re-cluster (Q4) with real data.

## 8. Open product questions (max 5, with recommendations)

**Q1. On card tap, should the sheet slide away (locked-spec text) or stay for re-cluster?**
Recommend: **slide away** (fix #3) — the composer hand-off is the job; re-cluster renders
hidden and ✦ re-opens. Requires no unlock (the spec already says "slides away"; today's
stay-open behavior is the deviation).

**Q2. Keep "exactly 4, all labels distinct"?** The distinct rule guarantees one filler card
in tense/simple situations (probes E/M) and the client already renders 1–4
(MapResponse:249-257). Recommend: **allow 3–4** — server validation accepts 3 when the model
marks the 4th impossible; labels stay distinct. Small server change; watch tap-rank metric.

**Q3. Away-owner awareness (F16):** recommend the zero-infra **message-to-self nudge** (b)
behind a Profile toggle, deferred until after top-5; real push only if the nudge proves the
demand. Needs your call on whether a bot messaging its own owner feels acceptable.

**Q4. Re-cluster: keep, change, or kill?** Each pick = a paid call whose result the owner
usually never sees (post-#3 it renders hidden). Recommend: **keep + instrument** (metric 3
tells us if re-opened re-cluster sets ever get tapped); if <5% engagement after 2 weeks,
kill it and save the call.

**Q5. Model upgrade?** Measured 2.5–3.7s on gpt-4o-mini with the current prompt. Recommend:
**stay on mini** until #1/#2 land and metrics exist — the probe failures were grounding
failures, not capability failures; re-probe after, and only then consider a stronger model
for the suggestions path.

## 9. Device-pass checklist (fold into the pending winner-P pass)

Existing winner-P items: drag feel (detents, rubber-band, midpoint settle), left-edge swipe
proxy (horizontal→back, vertical→scroll, taps pass through), ✦ toggle slot in the composer
row, drag-to-dismiss >25% via SetSheetOpen.

Add from this audit:
- [ ] Card tap with the keyboard ALREADY open: no text resurrection, caret lands at end,
      next keystroke edits the right position (F14; iPhone mandatory, test RU + emoji text).
- [ ] Card tap while TextSelection pins are up in the composer → pins clear, no ghost menu.
- [ ] Rapid double-tap two different cards → composer holds the second text, one keyboard.
- [ ] New message arrives while sheet open: skeleton (or post-F10 dim) never yanks a card
      mid-tap; tap during transition does nothing harmful.
- [ ] Owner sends manually → sheet behavior (post-#5: soft-dismiss; pre-#5: verify stale
      cards at least don't overlay the new-message flow).
- [ ] Composer multi-line growth: sheet rides the top edge without gaps/overlap
      (`SetComposerHeight` path), including while the expand detent is active.
- [ ] Keyboard open/close cycle with sheet open: list floor + sheet + composer never
      overlap (ExpandableInput ExtraBottomOffset interplay).
- [ ] Dark «Чернильный»: PositiveBg recommended tint legible; border-legend pill reads on
      both card fills; skeleton dots visible on Surface.
- [ ] Theme flip while cards displayed (Profile → back): colors correct (post-F17a).
- [ ] Telegram chat in Вместе: cards arrive (botTgId RAG branch), channel-correct profile.
- [ ] Airplane mode: error state + «Обновить» retry works; timeout feels acceptable (15s
      post-fix).
- [ ] Low-end Android: sheet slide 60fps with 4 full-text cards; per-render
      Instantiate×4 jank check; ThinkingDots battery over a 5-min idle sheet.
- [ ] Вместе chat opened from Dashboard drill-down (`DashboardPage.OpenChat`): panel
      restores + request fires exactly once.

---

*Audit sources: SuggestionsController.cs, N8nSuggestionsProvider.cs, SuggestionsPanel.cs,
SuggestionCard.cs, SuggestionSequenceGuard.cs, IncomingDebounceGate.cs, SemiAutoStore.cs,
SuggestRepliesDtos.cs, ChatManager.{Suggestions,RecentMessages}.cs, Manager.ReplyModeSync.cs,
ReplyModeToggleBinder.cs, SemiAutoToggle.cs, SheetDragHandle.cs, SuggestionsSheetSwipeProxy.cs,
QuickReplyPanel.cs, Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json +
SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json, sketch-findings-automation skill, 13 live probes
(2026-08-10, dev n8n localhost:5678).*
