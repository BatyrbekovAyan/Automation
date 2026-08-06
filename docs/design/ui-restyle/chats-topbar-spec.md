# Chats-List Top Bar Redesign — Locked Spec

**Date:** 2026-08-06
**Status:** LOCKED by owner (round 2, variant A + pill + confirm-on-enable-only + sheet А2)
**Exploration pages:** `chats-layout-variants.html` (round 1), `chats-layout-round2.html` (round 2 — section «Итог» matches this spec)
**Scope:** `ChatsPanel` top bar, the «Авто» control, and `Sheet_BotSwitcher`. All sizes in 1080×1920 canvas units, all colors as `ThemeRole` tokens (both themes come for free via `ThemedColor`).

---

## 1. Semantic change (drives everything)

«Вместе» disappears from the chats-list UI. Semi-auto is the silent default state; the only control is an **«Авто» button** that switches autopilot ON/OFF.

- Storage unchanged: `<botName>ReplyMode` int, `0 = auto ON («Авто»)`, `1 = auto OFF (semi)`. Event `OnReplyModeChanged(botName, mode)` unchanged. `SemiAutoStore` fallback keeps working.
- **Read default flips**: `GetInt(key, 1)` instead of `0` — a bot that never saved a value is now semi-auto. Owner-approved consequence: existing bots with no stored value flip from auto to semi on update; bots with stored `0` keep auto.
- **Confirm asymmetry**: enabling auto (bot starts messaging real clients) opens the confirm dialog; disabling is **instant** — write + event + visual, no dialog. Today's popup fires both ways; the disable path is removed.
- Class `ReplyModeToggleBinder` is NOT renamed (scene references break on class rename); its stale docstring is updated instead.

## 2. Top bar — «Два этажа» (variant A)

`ChatsPanel/TopBar` height **250 → 400**. Background `Surface`, existing bottom hairline line object kept. The three 360-wide zones (`LeftZone`/`CenterZone`/`RightZone`) are replaced by two full-width tiers. Scroll `Content` VLG top padding **260 → 410**. Check `EmptyState`/`SyncingState` vertical offsets after the bar grows (visual pass item).

### Tier 1 — identity + behaviour (156u, below the ~100u status area)

- **Bot identity** (left, x=40): existing `BotSwitcherTitle` block retuned — avatar **88** (business tint, icon ~41), name TMP **46 semibold** `InkPrimary`, chevron **28** bound to `InkTertiary`, gap 20. Name flexes with ellipsis (fits ~600u). `BotSwitcherTitleBinder` wiring untouched (opens the sheet).
- **Auto button** (right, x=−40): NEW. Visual pill **76h**, radius 38, padding-x 30, inner gap 14; dot **18**, label «Авто» TMP **30 semibold**. Button root ~210×96 transparent (hit area ≥96).
  - **ON**: fill `PositiveBg`, label + dot `PositiveInk` (dot solid).
  - **OFF**: no fill; 3u inset ring `Border` (ring technique: outer ring image + inner `Surface` fill inset 3); label `InkSecondary`; dot hollow (4u ring `InkTertiary`).
  - Deliberately NOT `#34C759` — that fixed green stays exclusive to the bot-activation switch on «Боты».
  - Tap OFF→ON: confirm dialog. Tap ON→OFF: instant. Punch-scale feedback on tap.

### Tier 2 — channel filter (144u)

Recessed segment, stretch x 40..40, well **96h** centered:

- Well: `Background` fill, radius 48, 2u inset `Hairline` ring, padding 5.
- Two equal cells, radius 43. **Selected**: `Surface` card, label **32 semibold** `InkPrimary`, brand dot **20** full alpha (soft shadow optional — skip if no clean sprite). **Unselected**: transparent, label `InkTertiary`, brand dot 40% alpha.
- Brand color appears ONLY as the 20u dot (`Theme.Fixed` WA green / `ChannelAccent.TelegramBlue`).
- Unread count in cell («WhatsApp 4»): **28** `InkTertiary`. Phase 1: render for the **active channel only** (data on hand). Phase 2 (optional): inactive-channel count — open question whether `BotCache/{botId}/chats.json` holds both channels; do not block on it.
- Behaviour unchanged: `ChannelSwitcherModel.StateFor` logic, muted-but-tappable unconnected channel (40% alpha on cell content — the path to the connect empty-state), `SetActiveChannel` + persisted `<botId>ActiveChatChannel`, punch-scale on tap.

## 3. Confirm dialog (reuse, enable-only)

Reuse `ChatsPanel/ReplyModeConfirmPopup` (720×440). New copy:

- Title: **«Включить авто-режим?»**
- Body: **«Бот будет отвечать клиентам сам. Выключить можно в любой момент — этой же кнопкой.»**
- Buttons: «Отмена» (ghost: `Background` fill + hairline ring, `InkSecondary`) / «Включить» (`AccentFill` + `AccentOnFill`).
- Parameterize the target bot: the popup can now be invoked from the sheet for a non-active bot.

## 4. Bot sheet — «Компактный список» А2

`Sheet_BotSwitcher` restyle. Panel: `Background` token (replace hard `#F0F2F5`), top radius 56, grabber 108×12 `Border`. Title «Ваши боты» **44 semibold** `InkPrimary`, subtitle «Чаты и авто-режим» **28** `InkTertiary`. List padding x40, row gap 20.

**Row** (`BotSwitcherRow.prefab`, height **228 → 152**): `Surface`, radius 40, 2u inset `Hairline` ring, padding-x 32, gap 28.

- Avatar **100** (icon ~65).
- Name **40 semibold** `InkPrimary`.
- Subline **28** `InkTertiary`: channel dots **16** (brand color when connected; single `Border`-colored dot when none) + «N чатов[ · M новых]», or «Не подключён».
- **Selected row**: 4u inset ring `AccentFill` + left rail 10u `AccentFill`. No check badge (trailing slot belongs to the chip).
- **Auto mini-chip** (trailing): 60h, radius 30, padding-x 22, label «Авто» **26**, dot **14**; same ON/OFF token treatment as the header pill; chip root ≥88 hit area. Hidden for bots with no connected channel.
- Taps: row (outside chip) → `SetActiveBot` + close (existing). Chip → mode change for THAT bot: enable → shared popup, disable → instant.
- Sheet height becomes content-driven (~920 for 4 bots; scrolls beyond 6). `BotSwitcherSheet` slide-distance constant (1180) must follow the real panel height. Cascade row animation kept.

## 5. Out of scope (explicit)

- Conversation-screen `SemiAutoToggle` (per-chat tri-state override) — untouched; language aligned to the button in a later pass.
- Merged both-channel list (round-1 variant 5) — parked, own phase if ever.
- Hiding the switcher when only one channel is connected — rejected; muted-tappable stays (it is the connect path).
- `ChatItem` row, search bar, bottom nav, `NewChatButton` — unchanged.

## 6. Implementation notes

- **Scene is source of truth**: additive edits over the existing `TopBar` / sheet objects via a new `[MenuItem]` builder pass; never rerun old builders (`ChannelSwitcherBuilder`, `ReplyModeToggleBuilder`, `BotSwitcherSheetBuilder` are superseded for geometry). Rewire consumers via `SerializedObject`; save scene; commit scene + prefab immediately after apply.
- All new/retinted graphics get `ThemedColor` bindings (`preserveAlpha` ON). Roles used: `Surface, Background, Hairline, Border, InkPrimary, InkSecondary, InkTertiary, AccentFill, AccentOnFill, PositiveBg, PositiveInk`. No new `ThemeRole` entries.
- `ReplyModeToggleBinder` keeps class name, PlayerPrefs key, `GetMode`, and the event signature; rework is visual + the confirm asymmetry + default flip.
- Suggested order: (1) pure seams + tests (`AutoButtonModel` mapping, default read, confirm-direction decision), (2) prefab + scene builder pass, (3) binder/popup/sheet wiring, (4) editor-bridge test run + owner visual pass at 1080×2400.

## 7. Tests (EditMode, `Assets/Tests/Editor/Chat/`)

1. Auto state mapping: mode 0 → ON visuals, 1 → OFF visuals (pure seam, `ChannelSwitcherModel` style).
2. Default read is semi-auto (`GetInt(_, 1)`) — pins the deliberate flip.
3. Confirm asymmetry: OFF→ON requires confirmation path; ON→OFF writes immediately.
4. Sheet row: chip visible iff any channel connected; chip state reads that bot's mode; selected row shows ring+rail and no check.
5. Existing `ChannelSwitcherModel` tests still pass unchanged (behaviour untouched).
