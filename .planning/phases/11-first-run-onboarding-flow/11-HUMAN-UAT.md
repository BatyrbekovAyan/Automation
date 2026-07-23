---
status: diagnosed
phase: 11-first-run-onboarding-flow
source: [11-07-PLAN.md]
requirements: [ONB-01, ONB-02, ONB-03, ONB-04, ONB-05]
started: "2026-07-18"
---

# Phase 11 — Human UAT Gate: First-Run Onboarding Flow (ONB-01…05 device pass)

**Status:** OPEN (owner-run) — this gate CLOSES Phase 11.

Phase 11 is **code-complete** once the agent-side work is committed and the
EditMode suite is green: the pure onboarding logic (`OnboardingGate` /
`OnboardingPageMath` / `SuccessCtaSelector` / `FirstStepsChecklist`, 29 tests,
`11-01`), the carousel runtime (`OnboardingPager` snap ScrollRect + `OnboardingScreen`,
`11-02`), the live `Screen_Onboarding` + first-run gate (`11-03`), the interactive
«Бот подключён!» success moment (`11-04`), the «Это безопасно» trust blocks +
per-channel success sheets (`11-05`), and the «Первые шаги» derived-state checklist
card (`11-06`) all shipped with the suite green at **1165/1165**.

What **cannot** be verified headless is the thing that matters most here:
**how the onboarding looks and FEELS on a real device** — carousel paging + dot
sync, trust-block legibility on both auth panels, the success animation + price-list
deep-link, the checklist cascade + derived states, and (critically) **zero regression**
to the real auth code flows and the existing-user path. Running this is **not** a task
in the plan — it is this human gate, because visual/interaction polish and regression
are unobservable to a batch build.

> **Blocks:** this gate **closes Phase 11**. The five requirements it validates —
> **ONB-01** (carousel + gate), **ONB-02** (both-channel trust blocks), **ONB-03**
> (success moment + deep-link), **ONB-04** (checklist), **ONB-05** (zero regression) —
> are only *proven* on a green pass here. Any FAIL spins a gap-closure round via
> `/gsd-plan-phase 11 --gaps`.
>
> **Do NOT tick these on the owner's behalf.** Authoring this runbook was autonomous;
> RUNNING it is the owner gate. Every checkbox ships blank.
>
> **Self-contained:** Phase 8's own device gate (`08-21` / `08-DEVICE-UAT.md`) is still
> open and shares your device time — this runbook is complete on its own, so you can run
> either gate independently, in any order.

## Setup / Environment (prepare before you start)

- [ ] Open the project in Unity (6000.3.9f1) and load `Assets/Scenes/Main.unity`.
- [ ] Set the Game view to **1080×2400** (portrait, mobile aspect) — the project's
      calibrated review resolution. A **device build** (Android primary) is preferred for
      the auth code flows + success moment (§2/§3 need real network round-trips); Game view
      at 1080×2400 is fine for the carousel paging (§1) and checklist visuals (§4).
- [ ] Enter **Play mode** — the first-run gate resolves at startup (`BotsPage.RefreshEmptyState`
      / `Manager.LoadBots`) and the checklist/success binders resolve in `OnEnable`; outside
      Play mode the screens show their static defaults.
- [ ] **Reset to a true first run** to exercise the carousel path: **Профиль → Аккаунт →
      «Удалить все данные»** — this wipes `PlayerPrefs` (bots + `OnboardingSeen` /
      `OnboardingChecklistDone` / `FirstBotReplySeen`) by design, so onboarding re-runs. A
      **fresh install** does the same. Do NOT hand-delete individual PlayerPrefs keys — the
      wipe is the sanctioned reset.
- [ ] Have (or be able to create) at least **one authorizable channel** — a WhatsApp number
      you can pair, and/or a Telegram account (with a **cloud password** set if you want to
      exercise the 2FA branch in §2).

## How to record results

- Tick **exactly one** verdict per item where it appears (PASS / FAIL). On any **FAIL**,
  add a row to the **Defects found** table with the screen + expected-vs-actual.
- Set the **Overall** line at the end. On a green Overall, Phase 11 is complete.
- Order is amortised: §1 and §4 run on the Game view alone; §2 and §3 want a device build
  with a real channel to authorize; §5 is the regression + suite sweep.

## Copy deck (verbatim — confirm every string renders EXACTLY, formal «вы»)

| Element | Text |
|---|---|
| Слайд 1 заголовок / текст | Бот отвечает клиентам за вас / Круглосуточно, в WhatsApp и Telegram — на вашем обычном номере |
| Слайд 2 заголовок / текст | Вы решаете, сколько доверить / Полный автопилот или подтверждение каждого ответа — можно менять в любой момент |
| Слайд 3 заголовок / текст / CTA | Работает там, где ваши клиенты / Подключите WhatsApp, Telegram или оба сразу — канал выберете при создании бота / Создать бота |
| Доверие (WhatsApp) | Это безопасно — Работает через официальные «Связанные устройства» WhatsApp. Переписка остаётся у вас, отключить бота можно в любой момент. |
| Доверие (Telegram) | Это безопасно — Официальный вход Telegram: код приходит в само приложение. Переписка остаётся у вас, отключить бота можно в любой момент. |
| Успех заголовок / текст / кнопки | Бот подключён! / Осталось научить бота вашим ценам — загрузите прайс-лист, и он будет отвечать по вашим товарам / Загрузить прайс-лист · Позже |
| Чек-лист заголовок / пункты | Первые шаги / Создать бота · Подключить WhatsApp/Telegram · Загрузить прайс-лист · Получить первый ответ бота |
| Чек-лист подсказка (пункт 4) | Попросите знакомого написать вам — и посмотрите, как бот ответит |

---

## 1. Welcome carousel + first-run gate (ONB-01)

Run this immediately after the «Удалить все данные» wipe / fresh install (no bots,
`OnboardingSeen` unset).

- [ ] On first launch with **zero bots + flag unset**, the **3-slide carousel** appears —
      NOT a bare `AddBotPanel` auto-open, and NOT the Bots empty state.
- [ ] The three slides read (title / body) exactly per the copy deck:
      **1** «Бот отвечает клиентам за вас», **2** «Вы решаете, сколько доверить»,
      **3** «Работает там, где ваши клиенты» — with the verbatim body copy under each.
- [ ] Each slide's **hero** renders as intended: slide 1 = mini chat mock (customer question
      → bot answer with a price → typing indicator); slide 2 = the Авто (selected) / Вместе
      mode cards; slide 3 = WhatsApp + Telegram cards, both check-marked, with the one-line
      descriptions. Icons are real sprites (not missing/pink squares).
- [ ] Horizontal **swipe SNAPS** to each page (~0.3s, OutCubic, no half-slide rest); the
      **dot pill tracks** the active page (active dot = elongated Primary `#1B7CEB` pill).
- [ ] Tapping **«Далее»** advances slide 1→2→3; there is **NO «Пропустить»** anywhere —
      the only way forward is «Далее» / (slide 3) «Создать бота».
- [ ] On slide 3, **«Создать бота»** closes the carousel and opens the existing
      **AddBotPanel** wizard (name · business · channel) — the wizard itself is unchanged.
- [ ] **Relaunch** (stop + re-enter Play mode, or relaunch on device): the carousel does
      **NOT** reappear — `OnboardingSeen` latched on the «Создать бота» tap.
- [ ] **Existing-user check:** with **at least one bot present**, a fresh launch **NEVER**
      shows the carousel (existing users are auto-flagged from the live bot count).
- [ ] **Edge case (optional):** create a bot, then **delete all bots** via «Удалить все данные»
      → next launch **still shows the carousel** (eligibility keys off the live bot count, not a
      monotonic id counter).

## 2. «Это безопасно» trust blocks — both channels (ONB-02)

Reach the auth code panels via the wizard (or Settings → re-auth). No QR anywhere.

- [ ] In the **WhatsApp code panel**: a green **«Это безопасно»** card with a **lock icon**
      (real sprite, not a text glyph) and the body «Работает через официальные «Связанные
      устройства» WhatsApp. Переписка остаётся у вас, отключить бота можно в любой момент.»
      The card sits cleanly at the **bottom of the panel** (no overlap, legible at real size).
- [ ] In the **Telegram phone/code/2FA panel**: a green «Это безопасно» card with the body
      «Официальный вход Telegram: код приходит в само приложение. Переписка остаётся у вас,
      отключить бота можно в любой момент.»
- [ ] **No QR** and no «Связанные устройства»-style QR affordance appears on either panel —
      only the real code flows.
- [ ] The copy **reads honestly** at device size — it reassures without over-promising
      (no claim that the app can't see messages, etc.).
- [ ] **Pitfall-2 regression check — the real code flow still works end-to-end after the trust
      card landed:** WhatsApp → get pairing code → enter it in WhatsApp «Связанные устройства»
      → authorizes. Telegram → phone → code → (if cloud password set) 2FA prompt → authorizes.
      Nothing on the code panel **shifted, overlapped, or broke** because the trust card was
      injected (the card is appended as the LAST child, so the auth flow is byte-identical).

## 3. Interactive «Бот подключён!» success moment (ONB-03)

- [ ] On a **successful auth** (either channel), **«Бот подключён!»** shows with the body
      «Осталось научить бота вашим ценам — загрузите прайс-лист, и он будет отвечать по вашим
      товарам», and the **green check animates** (DOScale 0.9→1 OutBack pop).
- [ ] The success moment is **interactive — it does NOT auto-dismiss after 2s**; it waits for
      you to tap a button.
- [ ] The primary **«Загрузить прайс-лист»** deep-links into **that bot's** BotSettings →
      **«Прайс-листы»** tab (not a generic settings screen).
- [ ] **«Позже»** dismisses to the **Bots page** (the normal post-auth destination).
- [ ] **Both-channels creation:** create a bot with **both** WhatsApp + Telegram in one wizard
      pass — the success moment appears **ONCE** (after the bot exists), **not twice**.
- [ ] **Settings re-auth with files already uploaded:** re-authorize a bot that ALREADY has a
      price list uploaded — the primary CTA reads **«Открыть чаты»** instead of «Загрузить
      прайс-лист» (files-exist fallback).

## 4. «Первые шаги» derived-state checklist (ONB-04)

With exactly one bot present (mid-onboarding), check the BotsPage card.

- [ ] BotsPage shows the **«Первые шаги»** card with **«N из 4»** + a slim **progress bar**;
      the rows fade in with a **~0.05s-stagger cascade** (not all at once, not janky).
- [ ] The card **does not cover the first bot card** — the bots list is inset so the first
      card clears the banner (spacing reads clean next to the card).
- [ ] **Row 2 label matches the bot's actual channel** — «Подключить WhatsApp» for a WhatsApp
      bot, «Подключить Telegram» for a Telegram bot (a both-channel bot shows its resolved label).
- [ ] Each row **deep-links** correctly on tap:
      row 1 «Создать бота» → AddBotPanel; row 2 «Подключить …» → that bot's settings
      (General tab); row 3 «Загрузить прайс-лист» → BotSettings «Прайс-листы»; row 4
      «Получить первый ответ бота» → Chats tab, and tapping it surfaces the hint
      «Попросите знакомого написать вам — и посмотрите, как бот ответит».
- [ ] **Completed rows** render done: green circle + white tick, **strikethrough** + muted
      label, chevron hidden. (Create + connect are already done for a one-bot user.)
- [ ] Upload a price list → the **«Загрузить прайс-лист»** row flips to done and «N из 4»
      increments.
- [ ] After a **first outgoing bot reply** in a chat (an owner/bot `fromMe` message), at
      **4/4** the card **hides** — and **stays hidden on relaunch** (permanent completion latch,
      never resurrects even if the facts later change).

## 5. Zero regression (ONB-05)

- [ ] The **Bots empty state** still renders correctly (once onboarding is flagged seen and no
      bots exist — e.g. delete the last bot without wiping the flag).
- [ ] The **AddBotPanel auto-open** behaves as before **post-onboarding** (flag set): opening
      the create flow from the header `+`, the empty-state CTA, and the auto-open path all work.
- [ ] **Both auth flows** (WhatsApp pairing code; Telegram phone → code → optional 2FA) behave
      exactly as before — no shift/break from the trust card or the re-sequenced success moment.
- [ ] The **EditMode suite is green** — record the count (expected **1165/1165**): ___ / ___.

---

## Defects found

Log every FAIL here so it can spin its own gap-closure plan and stays traceable to the
requirement/source it reopens. (Empty = no defects.)

| # | Item (section + number) | Requirement | Screen — expected vs actual | Severity | → gap-closure? |
|---|-------------------------|-------------|-----------------------------|----------|----------------|
| D1 | §4 checklist visibility | ONB-04/05 | Bots page, zero bots (back out of the wizard after the carousel): expected EmptyState only; actual EmptyState AND FirstStepsCard render overlapping each other | medium | yes — hide card when no bots (EmptyState active) |
| D2 | §3 success moment | ONB-03 | Auth screen after final auth: expected a clean «Бот подключён!» moment; actual the success sheet renders stacked ON TOP of the still-visible code-entry UI (title/code field/buttons/trust card all visible beneath) | high | yes — owner decision 2026-07-18: relocate to a STANDALONE full-screen overlay outside the auth screens; single field set replaces the per-channel wa*/tg* sets |
| D3 | §4 checklist derived state | ONB-04 | Bots page right after bot creation: expected rows 1-2 checked (2 из 4); actual all rows unchecked until you tap a row, navigate away and return (refresh only happens on re-enable) | medium | yes — refresh on fact-changing events (bot created, auth done, wizard closed, upload, first-reply latch), not OnEnable alone |

## Overall result

**Overall:** ☐ PASS ☑ ISSUES

- **Result:** Round 1 run 2026-07-18 (Editor Game view + live auth) — carousel, trust blocks, and deep-links work; 3 defects logged (D1–D3), all routed to gap closure.
- **Issues found (if any):** D1 EmptyState/FirstStepsCard overlap on zero bots; D2 success sheet stacked over auth content (→ standalone overlay per owner decision); D3 checklist rows stale until re-enable.
- **EditMode suite:** 1165 / 1165

On **PASS** (all ONB-01…ONB-05 sections green), Phase 11 (First-Run Onboarding Flow) is
**complete** — reply **"approved"** to the executor checkpoint. On **ISSUES**, paste the defect
list (screen + expected vs actual) to seed a gap-closure round via `/gsd-plan-phase 11 --gaps`;
do NOT hand-patch fixes in this runbook.

---

## Round 2 re-verify (D1–D3)

> **Authoring this addendum was autonomous; RUNNING it is the owner gate — every checkbox below
> ships blank.** This is a **FOCUSED** re-verify of the three Round-1 defects (D1–D3) closed by
> gap plans **11-08** (D2 standalone success overlay) and **11-09** (D1 zero-bot visibility + D3
> live derived state), plus a zero-regression re-check — it is **NOT** a fresh full UAT (§1–§5
> above already passed on Round 1 apart from the three logged defects). Before the **D1/D3**
> checks, **reset to a true first run the sanctioned way** — **Профиль → Аккаунт → «Удалить все
> данные»** (or a fresh install); do NOT hand-delete individual PlayerPrefs keys.

### D2 re-verify — standalone «Бот подключён!» overlay (ONB-03 · closes D2 via 11-08)

Round-1 D2 (high): the «Бот подключён!» sheet rendered **stacked ON TOP of the still-visible
code-entry UI** (title / code field / buttons / trust card all showing beneath). 11-08 relocated
it to a **standalone full-screen `SuccessOverlay`** — a Canvas-level last sibling that renders
ABOVE the auth pages, with both auth hierarchies deactivated up front.

- [ ] On a **successful auth — verify BOTH a fresh creation auth AND a Settings → re-auth** —
      «Бот подключён!» shows on a **clean standalone FULL-SCREEN overlay** with **NOTHING** of the
      code UI (screen title, code field, buttons, «Это безопасно» trust card) visible beneath it.
      — **PASS** ☐  **FAIL** ☐
- [ ] The primary **«Загрузить прайс-лист»** deep-links into **that bot's «Прайс-листы»** tab;
      **«Позже»** dismisses cleanly to the **Bots** page (nothing lingers). — **PASS** ☐  **FAIL** ☐
- [ ] On a **re-auth of a bot that ALREADY has a price list**, the primary CTA reads **«Открыть
      чаты»** (files-exist fallback), not «Загрузить прайс-лист». — **PASS** ☐  **FAIL** ☐
- [ ] The **green check still pops** (DOScale 0.9→1, OutBack). — **PASS** ☐  **FAIL** ☐
- [ ] A **«both»-channel creation** (WhatsApp + Telegram in one wizard pass) shows the success
      moment **exactly ONCE** (after the bot exists), not twice. — **PASS** ☐  **FAIL** ☐

> **Round-2 repro path (D2):** reach the final auth on either channel → on success the full-screen
> «Бот подключён!» must cover everything with nothing beneath; «Позже» dismisses cleanly; repeat on
> both channels + a Settings re-auth.

### D1 re-verify — zero-bot card visibility (ONB-04/05 · closes D1 via 11-09)

Round-1 D1 (medium): with **zero bots** the EmptyState **and** the «Первые шаги» card both
rendered, **overlapping**. 11-09 added the pure `FirstStepsCardVisibility.ShouldShow(hasBots,
checklistDone)` gate (hide via CanvasGroup, root stays active) so the EmptyState owns the zero-bot
screen.

- [ ] With **ZERO bots** — finish the carousel → the wizard opens → **press back** — **ONLY the
      EmptyState renders**; the «Первые шаги» card is **NOT visible** (no overlap). — **PASS** ☐
      **FAIL** ☐
- [ ] The «Первые шаги» card **first appears once ≥1 bot exists**. — **PASS** ☐  **FAIL** ☐

> **Round-2 repro path (D1):** carousel → «Создать бота» → wizard → back → land on Боты with zero
> bots → confirm the EmptyState alone shows, no «Первые шаги» card underneath/over it.

### D3 re-verify — live derived state (ONB-04 · closes D3 via 11-09)

Round-1 D3 (medium): after bot creation the checklist rows stayed **all-unchecked until you
navigated away and back** (refresh only on re-enable). 11-09 added five fire-and-forget
`RefreshFromFacts()` hooks (bot-created, channel-authed, wizard back-out, price-list upload,
return-to-Bots) so the card is a live mirror.

- [ ] **Immediately after creating a bot** (no navigate-away-and-back), the card shows **«2 из 4»**
      with **rows 1-2 checked**. — **PASS** ☐  **FAIL** ☐
- [ ] **Uploading a price list** flips **«Загрузить прайс-лист»** to done and increments **«N из 4»**
      live, **without a tab bounce**. — **PASS** ☐  **FAIL** ☐
- [ ] After the **first outgoing bot reply** at **4/4** the card **hides** and **stays hidden on
      relaunch** (permanent completion latch). — **PASS** ☐  **FAIL** ☐

> **Round-2 repro path (D3):** create a bot → return to Боты → rows 1-2 already checked («2 из 4»)
> without navigating away; upload a price list → row 3 («Загрузить прайс-лист») checks live.

### Zero regression re-check (ONB-05)

Confirm the relocation (11-08) and the refresh hooks (11-09) broke nothing that already worked.

- [ ] **Both auth flows** behave exactly as before — WhatsApp pairing-code; Telegram phone → code →
      (optional 2FA) — **no shift/break** from the standalone-overlay relocation or the hooks. —
      **PASS** ☐  **FAIL** ☐
- [ ] The **Bots EmptyState** + the **AddBotPanel auto-open** (header `+`, empty-state CTA, zero-bot
      auto-open) behave as before. — **PASS** ☐  **FAIL** ☐
- [ ] The **EditMode suite is green** — record the count (expected **1209 / 1209**: the 1205 v1.2
      baseline + the 4 new D1 `FirstStepsCardVisibility` tests from 11-09; the plan's earlier
      "1169" figure predates the v1.2 Phase 9/10 tests that lifted the baseline from 1165 to 1205):
      ___ / ___. — **PASS** ☐  **FAIL** ☐

### Round 2 Overall

**Round 2 Overall:** ☐ PASS  ☐ ISSUES

- On a **green Round-2 (D1–D3 all PASS + zero-regression PASS)**, **Phase 11 (First-Run Onboarding
  Flow) is complete** — reply **"approved"** to the executor checkpoint.
- On **ISSUES**, paste the residual defects (screen + expected vs actual) to spin another
  gap-closure round via `/gsd-plan-phase 11 --gaps`; do NOT hand-patch fixes in this runbook.

---
*Phase: 11-first-run-onboarding-flow — device/Game-view gate for the full onboarding surface*
*(carousel `11-02/03`, trust blocks + success sheets `11-04/05`, checklist `11-06`). This gate closes the phase.*
</content>
</invoke>
