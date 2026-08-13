# Sketch Wrap-Up Summary

**Latest wrap-up:** 2026-08-13 · **Skill output:** `./.claude/skills/sketch-findings-automation/`

---

## Wrap-up 2026-08-13 — slot chassis + interaction model

**Sketches processed:** 3 (2 included, 1 excluded); 006/007 left unprocessed (other
sessions, round 1, no winners yet).
**Design areas:** Suggestions panel (messages page) — same single area, reference updated
in place.

### Included

| # | Name | Winner | Design Area |
|---|------|--------|-------------|
| 003 | suggestions-keyboard-slot | A «Прямая замена» (locked 2026-08-12) | Suggestions panel |
| 005 | slot-collapse | E «тап + ручка + клавиша-хамелеон» (locked 2026-08-13, 4 owner rounds) | Suggestions panel |

### Excluded

| # | Name | Reason |
|---|------|--------|
| 004 | slot-key-placement | No winner — the model pivot (keyboard became focus-driven) dissolved the key's panel⇄keyboard role mid-exploration; the one validated finding (field-END key position) is folded into the 005-E spec. |

### Key Decisions (005 E, supersedes 003-A's switching model; chassis + P cards stand)

Keyboard exists only while the field is focused; the panel is the slot's DEFAULT tenant;
collapsed (composer flush to the bottom) is reachable only via the handle. Thread taps and
lowered-composer taps only RAISE the panel (no field focus from collapsed). Handle on the
panel's top edge = free drag + snap to 3 detents: collapsed / standard (keyboard height) /
expanded (full card content, capped). ONE morphing key at the field END with
destination-glyph grammar (panel up → ⌨ neutral; else → ✦ tinted); hidden in «Авто».
Open question: auto-raise on incoming message. NOT yet implemented in Unity.

Full spec + rejected directions + Unity delta notes:
`.claude/skills/sketch-findings-automation/references/suggestions-panel.md`.

---

## Wrap-up 2026-08-07 — panel cards + skin

**Sketches processed:** 2 (1 included, 1 excluded)
**Design areas:** Suggestions panel (messages page)

### Included

| # | Name | Winner | Design Area |
|---|------|--------|-------------|
| 002 | suggestions-panel-redesign | P (locked 2026-08-07) | Suggestions panel |

### Excluded

| # | Name | Reason |
|---|------|--------|
| 001 | ai-assistant-redesign | «Артель» full-app identity candidate for the external multi-platform comparison (`docs/ui-redesign-prompt.md`); no winner judged — nothing validated to package. Re-offer once the comparison is decided. |

### Design Direction

Token-driven (ThemeRole; light + «Чернильный» dark verified), calm Surface+Border cards,
green reserved for the recommended/positive semantic, fixed-footprint panel architecture
preserved.

### Key Decisions

Winner P composite (6 rounds): fixed-height Surface bottom sheet (~852u; grabber +
«✦ ПРЕДЛОЖЕНИЯ» header + quiet refresh icon, no FAB) → full-width bordered cards with FULL
reply text scrolling INSIDE a fixed 738u viewport (cut card + bottom fade + thin scrollbar
as the affordance) → intent titles as border-legends (top-left, zero interior height) →
recommended card first with PositiveBg/PositiveInk tint + ✦ (no badge, no numeric %).
