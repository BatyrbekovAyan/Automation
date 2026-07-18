---
status: open
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
|   |                         |             |                             |          |                |

## Overall result

**Overall:** ☐ PASS ☐ ISSUES

- **Result:** ______________________________________________
- **Issues found (if any):** ______________________________________________
- **EditMode suite:** ______ / ______

On **PASS** (all ONB-01…ONB-05 sections green), Phase 11 (First-Run Onboarding Flow) is
**complete** — reply **"approved"** to the executor checkpoint. On **ISSUES**, paste the defect
list (screen + expected vs actual) to seed a gap-closure round via `/gsd-plan-phase 11 --gaps`;
do NOT hand-patch fixes in this runbook.

---
*Phase: 11-first-run-onboarding-flow — device/Game-view gate for the full onboarding surface*
*(carousel `11-02/03`, trust blocks + success sheets `11-04/05`, checklist `11-06`). This gate closes the phase.*
</content>
</invoke>
