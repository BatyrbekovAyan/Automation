---
name: sketch-findings-automation
description: Validated UI design decisions from sketch explorations for this app — currently the LOCKED suggestions-panel («Вместе») redesign spec with Unity reference-unit sizes, theme-token mapping, and the rejected-directions record. Load whenever building, restyling, or discussing the messages-page suggestions panel UI.
---

<context>
## Project: Automation (WhatsApp/Telegram bot app)

Design decisions validated through interactive HTML sketches in `.planning/sketches/`,
curated via `/gsd-sketch-wrap-up`. The sketches render the real messages page at 360 CSS px
(= 1080 reference units) using the app's actual theme tokens, so every value here converts
to the scene at ×3.

Sketch sessions wrapped: 2026-08-07.
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
| Suggestions panel (messages page) | references/suggestions-panel.md | Cards LOCKED (winner P: bordered full-text cards + border-legend titles + tint-only recommended); chassis since 2026-08-12 = KEYBOARD-SLOT tenant (sketch 003 winner A): panel sits in the keyboard's slot, mutually exclusive with it, ✦⇄⌨ key inside the composer field |

## Theme

`sources/themes/default.css` — today's light palette (1:1 from `Theme_Light.asset`).
`sources/themes/ink-dark.css` — approved «Чернильный» dark set (1:1 from `Theme_Dark.asset`).
Both use the same CSS variable names; sketches switch themes live.

## Source Files

`sources/002-suggestions-panel-redesign/index.html` — all 20 interactive variants
(deep-link `?v=p&theme=ink-dark`, states via `&st=load|empty|error`); winning tab
«P · На рамке ★».
</findings_index>

<metadata>
## Processed Sketches

- 002-suggestions-panel-redesign — INCLUDED 2026-08-07 (winner P locked)
- 001-ai-assistant-redesign — EXCLUDED 2026-08-07 (external multi-platform comparison,
  no winner judged yet; to re-offer it in a future wrap-up, delete this line)
</metadata>
