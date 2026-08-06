---
sketch: 002
name: suggestions-panel-redesign
question: "What form factor and skin should the reply-suggestions panel («Вместе» mode) have on the messages page?"
winner: null
tags: [suggestions, chat, panel, restyle]
---

# Sketch 002: Suggestions Panel Redesign

## Design Question

The owner dislikes how the current suggestions panel looks (mint 2×2 grid sheet built by
`SuggestionsPanelBuilder`). Which form factor reads best for 4 AI reply suggestions above
the composer — and is the problem the mint palette, the grid, or both?

## How to View

open .planning/sketches/002-suggestions-panel-redesign/index.html

Deep links: `?v=a|b|c|d|now`, `&theme=ink-dark`, `&st=load|empty|error`.

## Variants

- **Сейчас (baseline)** — faithful reproduction of today's panel: mint sheet #EAF6F0, 2×2 white
  cards with internal scroll, straddling intent pills, mint FAB. Not a candidate — the reference.
- **A: Чипы** — one row of tappable chips floating over the wallpaper, no sheet. Smallest
  footprint; recommended chip = green fill + ✦; intent labels dropped; refresh = trailing circle.
- **B: Список** — surface sheet with grabber + «✦ ПРЕДЛОЖЕНИЯ» header; full-width rows with
  intent chips; recommended row = 2 lines + green rail + wash, others 1-line ellipsis; quiet
  refresh icon in the header.
- **C: Карусель** — one large fully readable card, others behind horizontal swipe (snap +
  dots); recommended = first card, green border + ✦.
- **D: Родная сетка** — today's exact geometry (2×2 + FAB) reskinned to the app token palette:
  white cards, hairline borders, intent label inside, green only on the recommended card,
  neutral FAB. Isolates "palette vs layout".

Round 2 — «4 строки» refinements (owner asked to explore the 4-row form factor):

- **E: Полный текст** — sheet rows with intent overline + full reply text (≤3 lines), no
  truncation, rail + wash on the recommended. Tallest (~⅓ of the screen) but everything reads.
- **F: Компакт** — all four rows single-line with inline intent chips; height close to today's
  panel; recommended = bold + rail + wash.
- **G: Карточки** — no sheet: four full-width row-cards floating on the wallpaper, recommended
  = green card + ✦; refresh circle above the stack.
- **H: Акцент** — recommended reply as a filled green action block (white text), the other
  three as quiet single-line rows.

Round 3 — synthesis (owner liked E's sheet/overline/full-text/refresh + G's individual cards;
disliked E's flat list look, G's inline chips and truncation; suggested a fixed-height sheet
with inner scrolling):

- **I: Всё видно** — the synthesis at natural height: bordered cards with intent overline +
  full text on E's sheet; all four visible, tallest option.
- **J: Фикс + скролл** — same cards in a fixed ~285 sheet; cards scroll inside; the affordance
  is a cut-off card + bottom fade + thin scrollbar. Matches the panel's fixed-footprint
  architecture (D-12) — the sheet never changes height between states.
- **K: Фикс, спокойный** — fixed sheet with borderless soft-fill cards and an «ещё N» counter
  chip (tap scrolls) instead of the fade.

Round 4 — title placement on K's chassis (owner picked K but the intent overline costs too
much height; each option below saves ~13px/card, so all four cards fit the capped sheet with
no scrolling for typical content):

- **L: В строке** — run-in title: small colored uppercase word at the start of the text line,
  text flows around it (not a chip column like G had).
- **M: В углу** — tiny title floated in the card's top-right corner, like a bubble timestamp.
- **N: Без ярлыка** — no intent titles at all; the recommended card keeps only ✦ + green fill.

Round 5 — owner switched preference to J (bordered cards, fixed sheet + inner scroll) and
rejected L/M/N placements; title-placement round on J's chassis:

- **O: Слово-акцент** — L's run-in placement recolored to the «Чернильный» navy accent
  (`AccentText`) on every card; green stays exclusive to the recommended card (✦ + fill).
- **P: На рамке** — legend-style title sitting ON the card's top border (fieldset look),
  zero interior height; only possible on J's bordered cards.
- **Q: Внизу** — tiny title bottom-right after the text, like a signature; reply text starts
  immediately at the top.

Round 6 (final) — owner picked P; the border-legend moved around the frame to confirm:

- **R: Центр рамки** — legend centered on the top border, plaque/engraving style.
- **S: Рамка справа** — legend on the top border, right side; line starts stay clear.
- **T: Нижняя рамка** — legend on the bottom border, right; signature-under-the-reply feel
  (interacts with the scroll cut: the last card's label sits in the fade).

## What to Look For

- Which one you'd want under your thumb mid-conversation (chat visibility vs reply readability).
- A drops intent labels; B truncates rows 2–4 to one line; C hides options 2–4 behind swipe;
  D keeps internal card scrolling. Which trade-off feels acceptable?
- Flip the theme to «Чернильный» — variants A–D restyle automatically via tokens; the mint
  baseline deliberately clashes (it's hardcoded, like the real builder today).
- Everything is tappable: pick a suggestion (fills the composer, panel slides away), refresh
  (skeleton dots), switch «Вместе ⇄ Авто» in the header, cycle states from the toolbar.

## Grounding

All geometry converted from `SuggestionsPanelBuilder.cs` reference units (÷3); theme tokens
from `Theme_Light.asset` / `Theme_Dark.asset`; mode-switch colors from `ReplyModeToggleBinder`.
Locked decisions respected: no numeric confidence %, no recommended badge (tint only),
whole card = single tap target, fixed footprint per state.
