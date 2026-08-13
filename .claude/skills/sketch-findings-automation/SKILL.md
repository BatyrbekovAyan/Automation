---
name: sketch-findings-automation
description: Validated UI design decisions from sketch explorations for this app — currently the LOCKED suggestions-panel («Вместе») spec: P cards, keyboard-slot chassis, and the 005-E interaction model (focus-only keyboard, panel-default slot, 3-detent handle, destination-glyph key), with Unity reference-unit sizes, theme-token mapping, and the rejected-directions record. Load whenever building, restyling, or discussing the messages-page suggestions panel UI.
---

<context>
## Project: Automation (WhatsApp/Telegram bot app)

Design decisions validated through interactive HTML sketches in `.planning/sketches/`,
curated via `/gsd-sketch-wrap-up`. The sketches render the real messages page at 360 CSS px
(= 1080 reference units) using the app's actual theme tokens, so every value here converts
to the scene at ×3.

Sketch sessions wrapped: 2026-08-07, 2026-08-13.
</context>

<design_direction>
## Overall Direction

UI follows the app's token system (`ThemeRole` / `Theme_Light` / `Theme_Dark`) — no
hardcoded palette anywhere, so screens survive the upcoming «Чернильный» dark flip.
Surfaces are calm (Surface + hairline/Border strokes); green (`PositiveInk`/`PositiveBg`)
is reserved for the "recommended/positive" semantic; identity colors (WhatsApp green,
Telegram blue, iOS-blue «Вместе» switch) never theme. Type and spacing follow the
calibrated scale in `unity-ui-builder` (body 38–42u, 4dp-grid ×3 spacing).
</design_direction>

<findings_index>
## Design Areas

| Area | Reference | Key Decision |
|------|-----------|--------------|
| Suggestions panel (messages page) | references/suggestions-panel.md | Cards LOCKED (winner P: bordered full-text cards + border-legend titles + tint-only recommended); chassis since 2026-08-12 = KEYBOARD-SLOT tenant (003 A); interaction model since 2026-08-13 = 005 E «тап + ручка + клавиша-хамелеон»: keyboard only on field focus, panel is the slot's default tenant, thread/composer taps only RAISE, 3-detent handle (collapsed / standard / full-content), ONE destination-glyph ✦⇄⌨ key at the field END (not yet implemented in Unity) |

## Theme

`sources/themes/default.css` — today's light palette (1:1 from `Theme_Light.asset`).
`sources/themes/ink-dark.css` — approved «Чернильный» dark set (1:1 from `Theme_Dark.asset`).
Both use the same CSS variable names; sketches switch themes live.

## Source Files

`sources/002-suggestions-panel-redesign/index.html` — all 20 interactive variants
(deep-link `?v=p&theme=ink-dark`, states via `&st=load|empty|error`); winning tab
«P · На рамке ★».
`sources/003-suggestions-keyboard-slot/index.html` — slot-tenant chassis exploration
(deep-link `?v=a&sl=kb|sg|none`); winner A «Прямая замена».
`sources/005-slot-collapse/index.html` — collapse/switching model, 4 owner rounds
(deep-link `?v=e&sl=sg|kb|none|sgx`); winning tab «E · Синтез: тап + ручка ★» — live
draggable handle with detent snapping.
</findings_index>

<metadata>
## Processed Sketches

- 002-suggestions-panel-redesign — INCLUDED 2026-08-07 (winner P locked)
- 001-ai-assistant-redesign — EXCLUDED 2026-08-07 (external multi-platform comparison,
  no winner judged yet; to re-offer it in a future wrap-up, delete this line)
- 003-suggestions-keyboard-slot — INCLUDED 2026-08-13 (winner A locked 2026-08-12; its
  ✦⇄⌨ switching model later superseded by 005 E, chassis stands)
- 004-slot-key-placement — EXCLUDED 2026-08-13 (no winner — pivoted into 005; the one
  validated finding, field-END key position, is folded into the 005-E spec)
- 005-slot-collapse — INCLUDED 2026-08-13 (winner E «тап + ручка + клавиша-хамелеон»,
  4 owner rounds)
</metadata>
