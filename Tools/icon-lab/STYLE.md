# Icon set style contract

Every glyph in `glyphs/` obeys this. The contract is what makes ten icons
drawn separately read as one family; `lint.py` enforces the mechanical half
of it, and the review sheets carry the half only an eye can judge.

## Grid

- `viewBox="0 0 24 24"`, always. No other viewBox.
- **Live area is 2 → 22.** Nothing may touch the outer 2 units on any side.
  A glyph that fills its frame edge-to-edge looks larger than its neighbours
  even when the bounding boxes match.
- Longest extent of the ink should reach **17–20 units**. Shorter and the
  icon looks timid next to the rest of the set.
- Optical centre, not geometric: the rendered centroid must sit within
  0.9 units of (12, 12).

## Stroke

- `stroke="#FFFFFF"`, `stroke-width="2"`, `fill="none"`.
  **Width 2 everywhere, no exceptions** — a single 1.5 stroke makes the whole
  icon read as lighter than its row.
- `stroke-linecap="round"` and `stroke-linejoin="round"` on every stroked
  element (a default on `<svg>` or a wrapping `<g>` counts).
- Solid accents (pupils, dots, seeds) are `fill="#FFFFFF" stroke="none"`,
  radius ≥ 1.0.

## Construction

- **Fewer nodes wins.** These render at 52px (business tile) and 64px (nav).
  A 20-node path turns to mush at that size — that is a measured failure of
  the icon set this one replaces, not a style preference.
- Minimum gap between two strokes: **2 units**. Closer and they merge into a
  blob at 52px.
- No detail smaller than 2 units. No stroked shape narrower than 3 units.
- Corner radii: outer rounded rects `rx="2"`, inner/nested `rx="1"`.
  Keep the language consistent across the set.
- Prefer whole and half-unit coordinates. Fine decimals are fine when the
  geometry genuinely needs them (circles on a diagonal), but a glyph built
  from 0.5-grid coordinates stays crisp when scaled down.
- Draw the *idea*, not the object. A recognisable silhouette beats accurate
  detail every time at these sizes.

## Colour

The PNGs are pure white on transparency. **Colour lives in the app**, never
in the file: business glyphs are tinted onto their `tileColor` squircle, nav
glyphs are tinted per tab state. Never bake a colour into a glyph.

## Filled variants (nav only)

Each nav tab needs `<name>_outline.svg` (inactive) and `<name>_filled.svg`
(active). The pair must share **exactly one silhouette** — the filled icon is
the outline icon's outer edge, solidified. Anything else reads as two
different icons flickering on tap.

The reliable way to get that for free: give the *same* path both a fill and
the standard 2-wide stroke. Since the outline's visible outer edge is also
the path expanded by 1 unit, the two silhouettes match by construction.

Negative space is punched with a `<mask>` (the glyph must stay transparent
there so the app's tint shows through):

```svg
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"
     fill="none" stroke-linecap="round" stroke-linejoin="round">
  <mask id="m" maskUnits="userSpaceOnUse" x="0" y="0" width="24" height="24">
    <path d="…body…" fill="#FFFFFF" stroke="#FFFFFF" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round"/>
    <circle cx="9.2" cy="11" r="1.35" fill="#000000"/>   <!-- cutout -->
  </mask>
  <rect width="24" height="24" fill="#FFFFFF" mask="url(#m)"/>
</svg>
```

Cutouts must be **≥ 1.5 units** wide, or they close up at 64px.

Verify the pair by running `lint.py` on both: the two bounding boxes should
agree to within ~0.3 units. They are the same shape.

## Workflow

```bash
cd Tools/icon-lab
node render.js <name> 512        # one glyph to out/<name>.png
python3 lint.py <name>           # style + optical measurements
python3 sheet.py business | nav  # review sheet at true device size
```

Judge on the review sheet, at the small size. An icon that only works at
512px is not finished.
