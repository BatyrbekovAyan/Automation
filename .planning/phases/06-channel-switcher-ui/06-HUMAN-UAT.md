# Phase 6 — Human UAT Gate: Channel Switcher UI (SWITCH-01…04 visual pass)

**Status:** OPEN (owner-run) — this gate CLOSES the phase.

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

- [ ] Open the project in Unity (6000.3.9f1) and load `Assets/Scenes/Main.unity`.
- [ ] Set the Game view to **1080×2400** (portrait, mobile aspect) — the project's
      calibrated review resolution.
- [ ] Enter **Play mode** (the `ChannelSwitcherView` binder resolves state in
      `OnEnable`; outside Play mode the pill shows its static default = WhatsApp
      selected).
- [ ] Have at least three bots available to exercise every branch:
      a **both-channels** bot, a **WhatsApp-only** bot, and a **Telegram-only**
      bot (auth state is what drives the muted / auto-select logic).

## Owner checklist (run in the Game view at 1080×2400)

### 1. Pill placement + styling (SWITCH-01 surface)

- [ ] The `WhatsApp | Telegram` segmented pill sits in the **TopBar centre**
      (`CenterZone`), between the bot-switcher identity (left) and the
      Авто/Вместе `ModeToggle` + new-chat button (right).
- [ ] It **matches the ModeToggle visual language**: same corner rounding
      (rounded track + rounded selected fill, no square corners), same header
      font, comparable height/touch-target, and a filled **selected** chip vs a
      transparent **unselected** chip. Selected WhatsApp = green `#25D366`
      fill + white label; selected Telegram = blue `#2AABEE` fill + white label.
- [ ] Vertical alignment reads clean next to the ModeToggle (no obvious
      high/low offset). *If it sits noticeably off, note it — the pill centres
      in the existing CenterZone slot and its y can be nudged in a polish pass.*

### 2. Switch swaps the chat list — no crossing, no flicker (SWITCH-01)

- [ ] On a **both-channels** bot, tap **Telegram**: the chat list swaps to that
      channel's chats with the full reset choreography (list clears then
      reloads) — **no crossed lists**, no half-loaded rows, no visible flicker.
- [ ] Tap back to **WhatsApp**: it swaps back cleanly. Re-tapping the
      already-selected chip is a **no-op** (no reload flash).

### 3. Unconnected channel = muted but tappable → connect empty state (SWITCH-02)

- [ ] On a **WhatsApp-only** bot, the **Telegram** chip reads clearly **MUTED**
      (~40% alpha label) yet is still obviously **tappable** (not greyed-dead).
- [ ] Tapping the muted Telegram chip **selects it** and surfaces the Telegram
      **connect empty state** (BotHasNoTelegram CTA) — not a blank screen.
- [ ] Repeat on a **Telegram-only** bot: the **WhatsApp** chip is muted, tapping
      it shows the WhatsApp connect empty state. **Both chips are always
      visible** for every bot (no hidden switcher, no dead chip).

### 4. Single-channel bot auto-selects its live channel (SWITCH-03)

- [ ] Open a **Telegram-only** bot: it opens with **Telegram already selected**
      (filled) and the **WhatsApp** chip muted — no manual tap needed.
- [ ] Open a **WhatsApp-only** bot: WhatsApp is selected, Telegram muted.

### 5. Bottom tab bar: 4 tabs, tab 0 «Чаты» (SWITCH-04, tab-index shift)

- [ ] The bottom nav shows **exactly 4 tabs** — no Telegram tab.
- [ ] Tab 0 reads **«Чаты»** (was «Whatsapp»).
- [ ] Tapping **Сводка / Bots / Profile** each lands on the **correct** screen
      (the index shift 2/3/4 → 1/2/3 did not mis-route any tab).
- [ ] There is **no** blank pink Telegram screen reachable anywhere.

### 6. Last-used channel persists across restart (SWITCH-03)

- [ ] On a both-channels bot, select **Telegram**, then **stop and re-enter Play
      mode** (or relaunch on device). The bot reopens on **Telegram** — the
      per-bot channel choice (`{botId}ActiveChatChannel`) survived the restart.

## Deferred polish follow-ups (record owner decision)

These were carried from `06-CONTEXT.md §Deferred` — decide keep / defer / cut:

- [ ] **Per-row swipe-delete affordance on Telegram.** The 05-03 guard
      (`ActiveChannelSupportsChatDelete`) already makes the delete **network call
      a safe no-op** on Telegram, so there is no correctness bug. Hiding the
      swipe **visual affordance** per-row needs `ChatItemView` / prefab surgery —
      out of this phase's scene-only scope. Decision: ______.
- [ ] **RU-localization sweep of English empty-state copy (IN-09, from
      05-REVIEW).** Any residual English strings in the per-channel empty states
      should be Russianised before store. Decision: ______.

## Resume

Owner records the outcome here and closes the phase on a green pass:

- **Result:** PASS / ISSUES → ____________________
- **Issues found (if any):** ____________________
- **Deferred-polish decisions:** ____________________

On **PASS**, Phase 6 (Channel Switcher UI) is complete. On **ISSUES**, file the
specifics above and a follow-up plan handles them before phase close.

---
*Phase: 06-channel-switcher-ui — visual/device gate for 06-02 (headless scene build committed `8f1d25f`).*
