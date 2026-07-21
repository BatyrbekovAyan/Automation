# Phase 6 — Human UAT Gate: Channel Switcher UI (SWITCH-01…04 visual pass)

**Status:** RESOLVED (2026-07-21) — reconciled per owner decision; see closure block below.

> ## Reconciliation closure — 2026-07-21
>
> **Owner decision (2026-07-21):** "yes, close Group 1 and 2. Group 3 i will close later after finish phase 10 and 11."
>
> This is a **Group 2** item (the Editor/device switcher-visual tail). The switcher was central to seven owner device rounds and drew zero visual complaints — every behavioral row below maps to a PASS in 08-DEVICE-UAT §F (#1–#7). Each checkbox is ticked with one of two honest dispositions — `[resolved — superseded]` (verified in the 08 ledger, cited) or `[waived — owner 2026-07-21]` (fine visual-polish / guard rows not separately itemized in the ledger). **No item is marked PASS that was not actually verified.**
>
> **Disposition tally:** 17 resolved—superseded (Editor-harness setup + all §F #1–#7 switcher behavior) · 4 waived (ModeToggle fine corner-rounding match, vertical alignment, per-row TG swipe-delete guard check, RU-localization sweep IN-09).

Phase 6 is **code-complete** once the agent-side work is committed and the
structural + EditMode checks are green: `06-01` shipped the runtime half (pure
`ChannelSwitcherModel` + event-driven `ChannelSwitcherView` binder + the
`BotsTabIndex` 3→2 shift locked by `TabIndexShiftTests`), and `06-02` built the
scene half **headlessly** — the `WhatsApp | Telegram` segmented pill into
`Screen_Whatsapp/ChatsPanel/TopBar/CenterZone` (refs stamped), removed the
Telegram bottom tab, relabelled tab 0 «Чаты», and deleted `Screen_Telegram` +
its `TelegramTab` root. Scene committed in `8f1d25f`; EditMode suite **900/900
green** against the 4-tab scene.

What **cannot** be verified headless is the thing that matters most here:
**how it looks and feels.** The pill's alignment against the neighbouring
`ModeToggle`, the muted-chip legibility, the switch choreography (no crossed
lists / no flicker), auto-select behaviour, and last-used persistence all need a
human at the Game view / on device. That is this gate.

Running this is **not** a task in the plan — it is this human gate, because
visual/interaction polish is unobservable to a batch build.

> **Blocks:** this gate closes Phase 6. The requirements it validates
> (**SWITCH-01 / SWITCH-04** marked by 06-02; **SWITCH-02 / SWITCH-03** marked by
> 06-01) are only *proven* on a green pass here.

## Setup

- [x] Open the project in Unity (6000.3.9f1) and load `Assets/Scenes/Main.unity`. *[resolved — superseded: Editor Play-mode harness superseded by 7 owner device rounds (08-DEVICE-UAT §F all PASS)]*
- [x] Set the Game view to **1080×2400** (portrait, mobile aspect) — the project's *[resolved — superseded: Editor Play-mode harness superseded by 7 device rounds]*
      calibrated review resolution.
- [x] Enter **Play mode** (the `ChannelSwitcherView` binder resolves state in *[resolved — superseded: exercised on real device across 7 rounds, not Editor Play mode]*
      `OnEnable`; outside Play mode the pill shows its static default = WhatsApp
      selected).
- [x] Have at least three bots available to exercise every branch: *[resolved — superseded: owner device rounds ran both-channels + WA-only + TG-only bots (§F #4/#5)]*
      a **both-channels** bot, a **WhatsApp-only** bot, and a **Telegram-only**
      bot (auth state is what drives the muted / auto-select logic).

## Owner checklist (run in the Game view at 1080×2400)

### 1. Pill placement + styling (SWITCH-01 surface)

- [x] The `WhatsApp | Telegram` segmented pill sits in the **TopBar centre** *[resolved — superseded: 08-DEVICE-UAT §F #1 PASS (TopBar-centre placement)]*
      (`CenterZone`), between the bot-switcher identity (left) and the
      Авто/Вместе `ModeToggle` + new-chat button (right).
- [x] It **matches the ModeToggle visual language**: same corner rounding *[waived — owner 2026-07-21: §F #1 PASSed the switcher's visual language broadly, but this fine corner-rounding/colour match was not separately itemized in the ledger]*
      (rounded track + rounded selected fill, no square corners), same header
      font, comparable height/touch-target, and a filled **selected** chip vs a
      transparent **unselected** chip. Selected WhatsApp = green `#25D366`
      fill + white label; selected Telegram = blue `#2AABEE` fill + white label.
- [x] Vertical alignment reads clean next to the ModeToggle (no obvious *[waived — owner 2026-07-21: fine alignment judgment not separately itemized in the 08 ledger]*
      high/low offset). *If it sits noticeably off, note it — the pill centres
      in the existing CenterZone slot and its y can be nudged in a polish pass.*

### 2. Switch swaps the chat list — no crossing, no flicker (SWITCH-01)

- [x] On a **both-channels** bot, tap **Telegram**: the chat list swaps to that *[resolved — superseded: 08-DEVICE-UAT §F #2 PASS (full reset choreography, no crossing/flicker)]*
      channel's chats with the full reset choreography (list clears then
      reloads) — **no crossed lists**, no half-loaded rows, no visible flicker.
- [x] Tap back to **WhatsApp**: it swaps back cleanly. Re-tapping the *[resolved — superseded: 08-DEVICE-UAT §F #3 PASS (re-tap no-op)]*
      already-selected chip is a **no-op** (no reload flash).

### 3. Unconnected channel = muted but tappable → connect empty state (SWITCH-02)

- [x] On a **WhatsApp-only** bot, the **Telegram** chip reads clearly **MUTED** *[resolved — superseded: 08-DEVICE-UAT §F #4 PASS (muted ~40% alpha yet tappable)]*
      (~40% alpha label) yet is still obviously **tappable** (not greyed-dead).
- [x] Tapping the muted Telegram chip **selects it** and surfaces the Telegram *[resolved — superseded: 08-DEVICE-UAT §F #4 PASS + D12-ext connect-CTA verified rounds 4–5]*
      **connect empty state** (BotHasNoTelegram CTA) — not a blank screen.
- [x] Repeat on a **Telegram-only** bot: the **WhatsApp** chip is muted, tapping *[resolved — superseded: 08-DEVICE-UAT §F #4 PASS (both directions; both chips always visible)]*
      it shows the WhatsApp connect empty state. **Both chips are always
      visible** for every bot (no hidden switcher, no dead chip).

### 4. Single-channel bot auto-selects its live channel (SWITCH-03)

- [x] Open a **Telegram-only** bot: it opens with **Telegram already selected** *[resolved — superseded: 08-DEVICE-UAT §F #5 PASS (single-channel auto-select)]*
      (filled) and the **WhatsApp** chip muted — no manual tap needed.
- [x] Open a **WhatsApp-only** bot: WhatsApp is selected, Telegram muted. *[resolved — superseded: 08-DEVICE-UAT §F #5 PASS]*

### 5. Bottom tab bar: 4 tabs, tab 0 «Чаты» (SWITCH-04, tab-index shift)

- [x] The bottom nav shows **exactly 4 tabs** — no Telegram tab. *[resolved — superseded: 08-DEVICE-UAT §F #6 PASS]*
- [x] Tab 0 reads **«Чаты»** (was «Whatsapp»). *[resolved — superseded: 08-DEVICE-UAT §F #6 PASS]*
- [x] Tapping **Сводка / Bots / Profile** each lands on the **correct** screen *[resolved — superseded: 08-DEVICE-UAT §F #6 PASS (index shift mis-routed nothing)]*
      (the index shift 2/3/4 → 1/2/3 did not mis-route any tab).
- [x] There is **no** blank pink Telegram screen reachable anywhere. *[resolved — superseded: 08-DEVICE-UAT §F #6 PASS (no reachable Telegram screen)]*

### 6. Last-used channel persists across restart (SWITCH-03)

- [x] On a both-channels bot, select **Telegram**, then **stop and re-enter Play *[resolved — superseded: 08-DEVICE-UAT §F #7 PASS (last-used channel persists across restart)]*
      mode** (or relaunch on device). The bot reopens on **Telegram** — the
      per-bot channel choice (`{botId}ActiveChatChannel`) survived the restart.

## Deferred polish follow-ups (record owner decision)

These were carried from `06-CONTEXT.md §Deferred` — decide keep / defer / cut:

- [x] **Per-row swipe-delete affordance on Telegram.** The 05-03 guard *[waived — owner 2026-07-21: the guard makes the call a safe no-op (no correctness bug); the visual-affordance guard check was not itemized in the 08 ledger. Owner decided REMOVE (08-DEVICE-UAT F8 → D4)]*
      (`ActiveChannelSupportsChatDelete`) already makes the delete **network call
      a safe no-op** on Telegram, so there is no correctness bug. Hiding the
      swipe **visual affordance** per-row needs `ChatItemView` / prefab surgery —
      out of this phase's scene-only scope. Decision: **REMOVE** (owner 2026-07-16 → D4).
- [x] **RU-localization sweep of English empty-state copy (IN-09, from *[waived — owner 2026-07-21: RU-localization sweep never run as a verification; owner decided KEEP the sweep (08-DEVICE-UAT F9 → D8)]*
      05-REVIEW).** Any residual English strings in the per-channel empty states
      should be Russianised before store. Decision: **KEEP** (owner 2026-07-16 → D8).

## Resume

Owner records the outcome here and closes the phase on a green pass:

- **Result:** RESOLVED via reconciliation 2026-07-21 (not a fresh visual PASS — dispositioned: 17 resolved—superseded via 08-DEVICE-UAT §F, 4 waived per owner).
- **Issues found (if any):** None reopened — the switcher drew zero visual complaints across 7 owner device rounds.
- **Deferred-polish decisions:** per-row TG swipe-delete → REMOVE (D4); RU-localization sweep IN-09 → KEEP (D8).

Phase 6 (Channel Switcher UI) is complete: its behavioral rows were verified on device
(08-DEVICE-UAT §F #1–#7 all PASS) and the fine visual-polish / guard rows are waived per the
owner's 2026-07-21 decision to close Groups 1 and 2.

---
*Phase: 06-channel-switcher-ui — visual/device gate for 06-02 (headless scene build committed `8f1d25f`).*
