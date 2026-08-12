# Suggestions Panel («Вместе» mode, messages page) — LOCKED DESIGN

Winner **P** of sketch 002 (6 rounds, locked 2026-08-07). Replaces the mint 2×2 grid
currently produced by `SuggestionsPanelBuilder`. Interactive reference:
`sources/002-suggestions-panel-redesign/index.html?v=p` (tab «P · На рамке ★»).

> **CHASSIS SUPERSEDED 2026-08-12 — sketch 003 winner A («Прямая замена»).** The CARD design
> below (bordered full-text cards, border-legend pills, tint-only recommended, header
> «‹ ✦ ПРЕДЛОЖЕНИЯ ↻», 4 states) remains LOCKED and unchanged, but the sheet no longer floats
> ABOVE the composer: the panel is now a KEYBOARD-SLOT tenant at the very bottom of the screen —
> exactly where and exactly as tall as the native keyboard, mutually exclusive with it, switched
> by a ✦ ⇄ ⌨ key inside the composer input field (like WhatsApp's in-field icons). Slot height =
> last measured keyboard height (`SuggestionSlotHeight`, fallback 780u); swaps hold
> `max(keyboard, slot)` on `KeyboardAwarePanel.VirtualBottomInset` so the composer never moves
> during a handoff (no-dip; `SuggestionSlotSwap`). Grabber, drag-to-dismiss, drag-to-expand and
> the sheet's left-edge swipe proxy died with the sheet chassis; the mic-slot ✦ toggle was
> retired for the in-field key (which survives typing — the mic-slot one vanished with text).
> Reference: `.planning/sketches/003-suggestions-keyboard-slot/` (variant A) + its README
> Decision block. Sections below describe the still-valid card/skin spec + the OLD chassis for
> history.

## Design Decisions

**Chassis — bottom sheet, FIXED height, cards scroll inside:**
- Sheet on `ThemeRole.Surface`, top corners rounded, up-shadow, sits directly above the composer.
- Chrome (top→bottom): grabber bar → header row: ✦ sparkle (PositiveInk) + «ПРЕДЛОЖЕНИЯ»
  overline label (InkTertiary, uppercase, letterspaced) + quiet refresh icon-button right
  (InkSecondary glyph, no background). **The old mint FAB is gone.**
- Card viewport below the header is a FIXED-height vertical scroll region. The sheet NEVER
  changes height between states — this preserves the panel's existing fixed-footprint
  invariant (D-12), so `SuggestionsPanel.Footprint`/message-list clearance logic stays as-is.
- Scroll affordance (all three together): the 4th card is cut off at the viewport edge, a
  bottom fade (Surface→transparent gradient overlay, input-transparent) washes the cut, and
  a thin scrollbar appears on the right. No «ещё N» counter chip (that was rejected K).

**Cards — individual, bordered, full text:**
- One full-width card per suggestion, stacked vertically. Card = `Surface` fill +
  1-unit `Border` outline, radius 42u. NO internal per-card scrolling, NO truncation/ellipsis —
  the full reply text always renders (cards grow; the SHEET scrolls).
- Whole card is a single tap target (locked earlier, D-01). Tap → text into composer,
  panel slides away.

**Intent title — legend ON the card's top border, LEFT:**
- The intent label («ЦЕНА», «ДОСТАВКА», …) sits centered ON the card's top border line,
  left-inset, like a fieldset legend. It consumes ZERO interior height — the reply text
  starts immediately at the card's top padding.
- Micro-typography: uppercase, bold, letterspaced, `InkSecondary`.
- Positions center/right/bottom were explicitly rejected (round 6) — top-LEFT only.

**Recommended card (best-first ordering):**
- Always FIRST in the stack. `PositiveBg` fill, border tinted `PositiveInk` (~45%), legend
  text `PositiveInk`, ✦ sparkle glyph inside the legend before the word.
- Tint-only emphasis — NO badge, NO numeric confidence % (older locked decision, D-07).

**States (all inside the same fixed viewport — footprint constant):**
- Loading: card-shaped skeletons with the 3-bouncing-dots motif (ThinkingDots identity).
- Empty: centered «Нет предложений» / «Напишите ответ вручную».
- Error: centered heading + hint + ghost «Обновить» button (`InputBorder` outline,
  `AccentText` label).
- Refresh tap → loading → cards.

**Interactions (kept from current implementation):**
- Panel slide-up on show ~0.25–0.28s OutCubic; slide-away on suggestion pick.
- Switching the header toggle to «Авто» hides the panel.
- Send-button pulse after a pick (sketch nicety — optional).

## Sizes (sketch CSS px ≈ dp → ×3 = scene reference units)

| Element | Sketch px | Reference units |
|---|---|---|
| Sheet total height | ~284 | **852** |
| — chrome (grabber+header) | ~38 | 114 |
| — card viewport (fixed) | 246 | **738** |
| Sheet top radius | 16 | 48 |
| Grabber | 36×4, r2 | 108×12, r6 |
| Header ✦ | 11 | 33 |
| Header label font | 9.5 | 28 (Caption tier) |
| Refresh icon-button | 26 circle, 14 glyph | 78 circle, 42 glyph |
| Card side padding (sheet) | 8 | 24 |
| Card gap | 5 | 15; **first-card top gap 9→27** (legend pokes above) |
| Card radius | 14 | 42 |
| Card border | 1 | 3 (hairline look; use 2–3u stroke) |
| Card padding | 8 top / 11 sides / 9 bottom | 24 / 33 / 27 |
| Reply text | 12.5, lh 1.36 | **38** (matches today's reply size) |
| Legend font | 8.5, ls .06em | 25–26 (Micro tier) |
| Legend inset from card left | 11 | 33 |
| Legend vertical offset | −7 (half out) | −21 |
| Legend side padding | 5 | 15 |
| ✦ in legend | 9 | 27 |
| Bottom fade height | 24 | 72 |
| Scrollbar width | 3 | 9 |

## Theme token mapping (ThemeRole)

Sheet `Surface` · card fill `Surface` · card border `Border` · legend ink `InkSecondary` ·
reply text `InkPrimary` · header label `InkTertiary` · sparkle + recommended accents
`PositiveInk` / `PositiveBg` · error retry `InputBorder` outline + `AccentText` label ·
fade gradient from `Surface`. No hardcoded colors anywhere — verified working in both
Theme_Light (today) and Theme_Dark («Чернильный») in the sketch.

## CSS patterns (from the winning variant)

```css
/* legend on the border — zero interior height */
.p-card { position:relative; }
.p-int  { position:absolute; top:-7px; left:11px; padding:0 5px;
          font-size:8.5px; font-weight:700; letter-spacing:.06em; text-transform:uppercase;
          color:var(--ink-2);
          background:linear-gradient(to bottom, var(--surface) 50%, var(--surface) 50%); }
.syn-card.top .p-int { color:var(--positive-ink);
          background:linear-gradient(to bottom, var(--surface) 50%, var(--positive-bg) 50%); }

/* fixed viewport + fade + thin bar */
.syn-fix  { height:246px; overflow-y:auto; scrollbar-width:thin; }
.syn-fade { position:absolute; left:0; right:0; bottom:0; height:24px; pointer-events:none;
            background:linear-gradient(transparent, var(--surface)); }

/* cards */
.syn-card      { background:var(--surface); border:1px solid var(--border); border-radius:14px;
                 padding:8px 11px 9px; }
.syn-card.top  { background:var(--positive-bg);
                 border-color:color-mix(in srgb, var(--positive-ink) 45%, transparent); }
```

## HTML structure (winning variant)

```html
<sheet Surface, r16 top, shadow-up>
  <grabber/>
  <header> ✦ ПРЕДЛОЖЕНИЯ <spacer/> <refresh-icon/> </header>
  <viewport fixed-height scroll>
    <card top>   <legend>✦ ЦЕНА</legend>      <text full/> </card>
    <card>       <legend>ДОСТАВКА</legend>    <text full/> </card>
    <card>       <legend>УТОЧНЕНИЕ</legend>   <text full/> </card>
    <card>       <legend>АЛЬТЕРНАТИВА</legend><text full/> </card>
  </viewport>
  <fade-overlay/>
</sheet>
```

## Unity implementation notes

- Restyle by UPDATING `SuggestionsPanelBuilder` (it owns this subtree) and re-running it;
  after rebuild, re-verify every serialized reference wired by `SuggestionsControllerWirer`
  (builders-must-rewire-consumers). Commit the scene right after the builder run.
- Viewport = `ScrollRect` (vertical) + `RectMask2D`; ensure every card child Graphic is
  Maskable (`m_Maskable:1`) or scrolling culls/janks.
- Legend "border break": Unity has no fieldset — build the legend as a small container
  with TWO stacked background strips (top half = sheet `Surface`, bottom half = the card's
  own fill: `Surface` or `PositiveBg`) + TMP label, anchored to the card's top edge,
  x = +33u from card left, centered vertically on the border (offset −21u). Match the CSS
  gradient trick exactly. RoundedCorners not needed on the strips (they're behind text).
- Fade = full-width Image with a vertical white→transparent sprite (or Gradient shader),
  `raycastTarget = false`, anchored to viewport bottom, tinted via ThemedColor to `Surface`.
- Fixed footprint: keep the existing footprint/clearance constant; states swap INSIDE the
  viewport only.
- Bind all colors via `ThemedColor` (additive, preserveAlpha ON) — never hardcode; the
  panel must flip correctly when the «Чернильный» palette lands.
- TMP glyph icons don't render in this project — refresh glyph and ✦ must be Image+sprite.

## What to Avoid (tried and rejected across the 6 rounds)

- **Mint palette** (#EAF6F0 sheet / #C9EFD9 card / #18A06B FAB) — the core complaint; all
  color must come from theme tokens.
- **2×2 grid with per-card internal scrolling** — cramped cells, text hidden behind scroll.
- **Straddling top-center intent pills («язычки»)** — busy; replaced by border-legend.
- **Chip-column titles in front of text** (variants G/B/F) — squeeze the text width.
- **Intent overline above text** (E/I/J/K original) — eats ~40u/card, forced scrolling.
- **Run-in word titles** (L gray/green, O navy) — reply starts with a word the customer
  never sees.
- **Corner-float (M), footer meta (Q), no-label (N)** — rejected; label must lead, on the border.
- **Legend at center (R), right (S), bottom border (T)** — top-left only.
- **Flat list rows** (E's look) — owner: "looks like a list"; individual cards required.
- **Any truncation/clamp of reply text** (A chips, F compact, G 2-line) — full text always.
- **Filled green recommended block** (H) — too pushy; tint-only.
- **Floating refresh FAB above the sheet** — replaced by the quiet header icon.
- **Chips row (A) / horizontal carousel (C)** — rejected form factors.
- **«ещё N» scroll counter chip** (K) — fade + cut card + scrollbar won.
- **Numeric confidence %, «recommended» badges** — locked out long before this sketch.

## Origin

Synthesized from sketch: 002 (rounds 1–6, winner P).
Source files: `sources/002-suggestions-panel-redesign/` (all 20 variants remain viewable),
themes in `sources/themes/`.
