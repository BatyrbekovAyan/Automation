## Module 3 — NEUMORPHISM (SOFT UI): controls carved into one calm slab

**Status: archived exploration** — not shortlisted for v1 (finalists: Spatial, Liquid Glass, Refined Modern). Kept build-ready for the style-swap kit; every number below is verified.

*One continuous slab; controls are carved into it, not placed on it — and because shadow contrast tops out at 1.73:1 (decoration only), borders, labels, and the accent carry every meaning.*

**Silhouette test** — a 10%-zoom screenshot of any screen must show: zero white pixels, one continuous `#E2E5E9` slab edge-to-edge, no card-on-background rectangles — only soft extrusions and debossed trays, plus at most two saturated marks (blue accent, green switch).
**Exclusive structural signature** (no other module may copy) — the debossed tray: every list and tab bar is carved INTO the slab as one inset container, and raised↔inset latching is the app's selection language. This module never fires a specular sweep (that belongs to Liquid Glass).

**Art direction brief** — One continuous slab of cool blue-grey plastic, lit from the top-left, with the UI pressed and extruded out of it. Nothing floats, nothing is a "card on a background" — everything IS the background, sculpted. The feeling is *quiet instrument panel*: a good Nest thermostat, the Nothing Phone widget language, the Apple Watch Timer dial. Saturation is rationed — surfaces never carry any, so the blue accent and the green activation switch land like lit indicator lamps. Ink is dark, heavy, short-lined; it does all the hierarchy work. WhatsApp green and Telegram blue appear only as small filled badges under white glyphs — never as surfaces or text. **Sculpting ratio (hard rule): roughly 20% of surface area is sculpted; 80% is flat slab. If a screen sculpts more, it's costume** — the CRED failure mode this module exists to avoid.

**Design tokens**

Base derived from the app's own primary `#1B7CEB` = hsl(212°, 84%, 51%) → surface hue 212°, S 15%, L 90%. All ratios below are computed against `Surface.Base`.

| Token | Value | Note |
|---|---|---|
| `Surface.Base` | `#E2E5E9` | THE material. Screen bg, cards, buttons, panels — identical hex everywhere. |
| `Surface.Sunken` | `#DCDFE4` | Well interiors only (−2 L). |
| `Surface.Raised` | `#E6E9ED` | Convex gradient top stop (+2 L). |
| `Shadow.Light` | `#FCFDFF` @ 95% | Highlight leg. Offset toward top-left. |
| `Shadow.Dark` | `#A6B0C2` @ 90% | Shade leg (hue held, L −19). Offset bottom-right. **1.73:1 — decoration only, never a boundary.** |
| `Ink.Primary` | `#1A1A2E` | 13.5:1 AAA. Headings, body, labels. |
| `Ink.Secondary` | `#5A5C61` | 5.30:1 on base, 5.01:1 on Sunken. Sub-labels, meta, **placeholders**. |
| `Ink.Tertiary` | `#62656C` | 4.62:1 on base. Timestamps/meta on `Surface.Base` only — never on Sunken, never body. `#6B6E75` (4.04:1) is demoted to non-text decoration. |
| `Accent.Stroke` | `#1B7CEB` | 3.24:1 — passes 1.4.11. Strokes, rings, selection. Never text. |
| `Accent.Ink` | `#1560C0` | 4.79:1. Accent text + the primary-button fill (white label 6.06:1). |
| `Focus.Ring` | `#0F4FA8` | 6.15:1 vs base, 3.55:1 vs the shade band — test both; the band is the trap. |
| `Border.Control` | `#6B7280` | 3.83:1. Mandatory 3u stroke on every interactive surface. |
| `Border.Hairline` | `#A7B2BE` | 3u. Inert panel edges + list dividers only. |
| `Switch.On` | `#25D366` | Activation/switch grooves ON — per-BOT, never per-channel (master rule). Fill delta vs slab is 1.57:1: the boundary is the kept 3u `Border.Control`, not the fill. |
| `Channel.WA` | badge fill `#25D366`, text ink `#00734F` (4.66:1) | White glyph on green badge only — never white text (1.98:1). |
| `Channel.TG` | badge fill `#2AABEE`, text ink `#0D6291` (5.24:1) | Same rule. |
| `Danger` | `#B32721` (5.15:1) | Delete flows — flat filled, never sculpted. |
| `Status.OrderCollected` | ink `#14733A` / well `#DEE7E1` | 4.69:1 |
| `Status.OwnerNeeded` | ink `#9A4E0C` / well `#E9E4DC` | 4.78:1 |
| `Status.InDialog` | ink `#1560C0` / well `#DDE2EB` | 4.66:1 |
| `Status.ClientSilent` | ink `#4A5260` / well `#DFE2E6` | 6.06:1 |
| `Status.QuestionClosed` | ink `#0F6B62` / well `#DCE5E5` | 4.96:1 |

(Status wells are slab-derived — the sanctioned substitute for the master's shared pill set, verified ≥4.5:1; every pill always carries dot + text label.)

Radii: chip 36 · control 42 · card 48 · panel/sheet 72 · avatar full. Elevation ladder (offset `d` / shadow-rect expansion per side, spread 0): **E1** controls 12/24 · **E2** cards 24/48 · **E3** sheets and modals 36/72. Minimum gap between two extruded siblings = 2× expansion.

**Material recipes**

Light source is fixed top-left, globally. In Unity anchored-position terms: light leg offset `(−d, +d)`, dark leg `(+d, −d)`.

- **RAISED (rest):** four layers, bottom-up — (1) `ShadowDark`: 9-slice pre-blurred sprite, tint `#A6B0C2` @ 0.90, rect = surface +expansion/side, offset `(+d, −d)`; (2) `ShadowLight`: same sprite, `#FCFDFF` @ 0.95, offset `(−d, +d)`; (3) `Surface`: Image `Surface.Base` + rounded corners + vertex-color gradient 145°, `#E6E9ED` → `#DEE1E6`; (4) 3u stroke `Border.Control` (interactive) or `Border.Hairline` (inert).
- **INSET (pressed / well):** outer pair alpha 0; `ShadowInner` overlay (inner-falloff 9-slice sprite, dark hugging top-left inner edge) alpha 1 at 0.7×d geometry; fill `Surface.Sunken`; gradient reversed; stroke unchanged. This is the permanent resting state of every input, search bar, toggle groove and pairing-code well.
- **SHALLOW INSET (quoted cards, reaction pills):** the inset recipe with the inner-shadow overlay at 0.5×d geometry (d = 6) — alpha and sprite unchanged, geometry only.
- **SELECTED (latched):** inset recipe + stroke swaps to 6u `Accent.Stroke` + a filled glyph (check/dot in `Accent.Ink`). Depth alone never encodes selection.
- **DISABLED:** all shadows alpha 0, fill `Surface.Base`, 3u `Border.Hairline`, content at `Ink.Secondary`. Never a shallow extrusion — that reads "far away," not "off."
- **FOCUS:** geometry unchanged + 6u `Focus.Ring` outline at 6u offset. Never a glow.
- **Grain:** one 128×128 tiling noise sprite overlay at 3% alpha on full-screen slabs only — never under text below 54u. Kills the plastic-CGI flatness.

**Guardrails — do NOT**

- Do NOT give any element a background hex different from its parent — that is a card with a shadow, not an extrusion. (Status wells and channel badges are the enumerated exceptions.)
- Do NOT use pure `#FFFFFF`/`#000000` shadow tints; only the derived `#FCFDFF`/`#A6B0C2`.
- Do NOT flip the light direction on even one element; top-left is global law.
- Do NOT sculpt chat bubbles, list rows, or anything over imagery or the wallpaper.
- Do NOT make the primary CTA, activation switch, or delete confirm neumorphic — they stay flat and saturated.
- Do NOT encode any state through depth alone — accent stroke + glyph/label change are mandatory companions.
- Do NOT place extruded siblings closer than 2× the shadow expansion.
- Do NOT ship an icon-only interactive control, EXCEPT the universally learned trio — `+`, send, back — each at ≥144u hit rect; every other control carries a text label.
- Do NOT render the disabled state as a shallow extrusion; shadows go to zero.
- Do NOT tween shadow offsets or rects; crossfade pre-built layers' alpha only.
- Do NOT let any surface fill exceed 25% saturation.
- Do NOT use `UnityEngine.UI.Shadow` on TMP text anywhere in this style.
- Do NOT fire a specular sweep anywhere — press-latch is this module's native physics; the sweep is Liquid Glass vocabulary.
- Do NOT sculpt more than ~20% of any screen's area — count it when a screen feels busy.

**Icons & type voice**

Icons: geometric outline sprites, 3u stroke, rounded joins, on a 66u grid; latched/selected states swap to the filled variant in `Accent.Ink`. Always Image + sprite (TMP glyphs never render). Type: the 4 project fonts `SFProText-{Regular,Medium,Semibold,Bold} SDF.asset`; SemiBold for names/CTAs, Regular body; no new typefaces. Illustration: reuse `bot_hero.png` and existing sprites only — flat, inside inset wells; no new illustration assets without an owner decision.

**Component specs**

Width rule: full-width controls = parent − 96u margins (984 at reference width); intrinsic-width controls (chips, secondary CTAs) size to content with 48u side padding. Interactive HIT rects ≥144×144u (visuals may be smaller only where the padded raycast rect is stated).

| Component | Spec |
|---|---|
| Primary button | 984×156u, r42, **flat `Accent.Ink` fill** — never sculpted; label 44 SemiBold `#FFFFFF` (6.06:1). |
| Secondary button | 984×144u, r42, RAISED E1, 3u `Border.Control`, label 42 SemiBold `Ink.Primary`. |
| Bot card | 984×312u, r48, RAISED E2, 96u gaps, 48u padding; name 47 SemiBold, status meta 36; footer activation switch flat and saturated, never sculpted. |
| Chat row | **FLAT**, 1032×204u rows inside ONE inset tray; 3u hairline dividers; avatar 132u. Zero per-row shadows. |
| Input field | 984×144u, r42, INSET at rest, placeholder `Ink.Secondary` 42 (5.01:1 on Sunken), caret `Accent.Stroke`; focus adds the ring. |
| Toggle/switch | groove 168×96u INSET r48, **hit rect padded to ≥144×144**; knob 84u RAISED E1; ON = knob travels +72u **and** groove fills `Switch.On` `#25D366` (activation — per-bot, never per-channel; profile toggles same recipe) **and** RU label swaps («Бот работает»/«Бот на паузе»). 3u `Border.Control` kept in BOTH states — the border is the 1.4.11 boundary, not the fill. |
| Status pill | min-height 64u (grows to 104u when the label wraps to 2 lines — min-height, never fixed), r36, flat `Status.*` well + 3u border in its ink @ 40%, 12u ink-colored dot at left cap, label 32 Medium in status ink. Non-interactive except dashboard filters. |
| Filter chip (dashboard) | raised↔inset latch key, h 144u, size-to-content + 48u padding; latched = SELECTED recipe. |
| Tab bar (BotSettings) | one INSET tray 1032×156u r42; each tab's hit rect ≥144×144; selected tab = RAISED E1 tile inside it + 6u accent underline 48u wide; unselected flat, label 36 Medium. |
| Bottom sheet | r72 top corners, RAISED E3, scrim `#1A1A2E` @ 46%, grab handle 96×12u in a shallow inset slot. |
| Modal/dialog | 936u wide, r72, RAISED E3; destructive confirm = flat `Danger` fill, cancel = secondary button. |
| Empty state | one 936×720u INSET well r72 holding flat illustration + 42 body + primary button below the well. |
| Avatar | circle, 3u hairline, seated in a 6u inset socket — set into the panel, not stuck on it. |

**Screen-by-screen direction**

- **Onboarding** — full-bleed slab + grain; channel choice = two 468×468u RAISED E2 tiles with WA/TG badge glyphs. Hero: on connect the chosen tile presses in, latches inset, accent ring fades on 0.12s after the geometry.
- **Bots list** — cards RAISED E2 at 96u gaps; header `+` a 144u raised circle (icon-only sanctioned; top placement accepted — low-frequency, first-run covered by the empty-state CTA). Hero: the activation switch — flat, saturated, the most legible control on screen.
- **Dashboard (Сводка)** — five outcome tiles RAISED E1 in a 2+3 grid, count 60 Bold, delta 36 in status ink with a leading +/− sign; drill-down rows FLAT inside one tray. Hero: the filter-chip latch — the chip presses in over 0.12s, HOLDS inset, and the tray rows re-cascade; the one place a physical control changes the data the owner sees.
- **Chats list** — search bar a permanently inset well pinned in the TopBar (284u); rows flat in one tray; unread badge flat `Accent.Ink` circle, count 30 white.
- **Chat thread** — **the slab stops at the transcript edge; that boundary is the style statement.** Keep the doodle wallpaper (`#F5F2EA`/`#E5DAC6` — LOCKED trust asset) and keep the green outgoing bubble `#C5EEB6`: incoming near-white flat + hairline, outgoing `#C5EEB6` flat — the learned WhatsApp grammar is the product's pitch, never restyle it away. Quoted cards and reaction pills use the shallow-inset recipe. Only the composer well and the 144u raised send/attach buttons (send = sanctioned icon-only) are neumorphic. Replacing the wallpaper with slab+grain is an OWNER CHECKPOINT (master §5) and must be trivially revertible (`AssignChatBackground.cs` — only the assignment changes). Hero: the send button press-in.
- **Bot Settings** — the style's best screen: tray tab bar, every field a permanent well, steppers as raised ± pair flanking an inset number well, product/service cards RAISED E1 at 72u gaps. Hero: the Prompts text area as one deep 936×720u well.
- **Add-Bot wizard** — business-type tiles 312×312u raised (hit rects ≥312 ✓); selected → inset + accent stroke + check. Pairing code in a wide inset well, 72 Bold, +8% tracking.
- **Profile** — sub-page rows flat inside grouped trays; all toggles use the groove switch. «Удалить все данные» = flat `Danger`-stroked row, never sculpted.

**States (loading / error / empty)** — per the master triad, in this material: loading >300ms = content children replaced by `Surface.Sunken` blocks pulsing alpha 4–8% inside the resting geometry (surfaces never pulse; restyle the EXISTING indicators only — chat-list sync, dashboard fetch); failure = inline retry row flat in the tray + the existing rollback affordances restyled; empty = the inset well (above). Offline = DISABLED recipe on the affected pane + a 32u status line in `Ink.Secondary`.

**Motion & feedback**

Animate shadow-layer **alpha only** — never tween offsets or resize shadow rects (full canvas rebuild per frame).

| Action | Tween | Duration | Ease |
|---|---|---|---|
| Press down | outer pair `DOFade`→0 + inner `DOFade`→1 + `DOScale` 0.985 + content `DOAnchorPos` (+3,−3) | 0.12s | OutQuad |
| Release | reverse crossfade + scale back | 0.22s | OutBack (clay rebounds slower) |
| Toggle latch | knob `DOAnchorPosX` +72u; groove fill `DOColor`→`Switch.On` | 0.24s | OutCubic |
| Filter-chip latch | press-in 0.12s → hold inset; tray rows re-cascade (stagger 0.03s) | 0.12 + 0.30s | OutQuad / OutCubic |
| Sheet open | `DOAnchorPosY` rise + E3 shadows `DOFade` 0→1 | 0.25s | OutBack |
| Page enter | `DOFade` + 24u rise | 0.30s | OutCubic |
| List cascade | rows stagger 0.03s, same rise | 0.30s | OutCubic |
| **Peak — bot connected** | tile press-in 0.12s → hold 0.08s → release 0.22s + accent ring latch 0.12s after the geometry — the latch IS the celebration; no sweep | 0.54s total | OutCubic |
| End — settings saved | whole panel inset-pulse via crossfade 0→1→0 | 0.28s | InOutSine |

**Unity notes — style-specific deltas only** (the universal contract lives in the master)

- Bake 3 pre-blurred rounded-rect falloff sprites (r42 controls, r48 cards, r72 panels; 128×128, 48px slice borders) + 2 inner-shadow variants + the noise tile — under 1 MB total. Every shadow is a sibling `Image` on the default UI material: ALL shadow quads on a screen batch into one draw call; light and dark legs are the same sprite tinted per token.
- `ThemeBuilderKit.AddNeumorphicSurface(go, radius, Elevation e)` emits `ShadowDark`/`ShadowLight`/`ShadowInner(α0)`/`Surface` children; `AddVerticalGradient(go, top, bottom)` is the shared ~40-line `BaseMeshEffect` writing corner vertex colors (composes with the Nobi shader; no material permutation).
- Runtime color stampers (`BotStatusPill`, `DashboardStatusInfo`, `MessageItemView`) read the RUNTIME `Theme` facade in `Assets/Scripts/Theme/` — never the editor assembly (master §3.6).
- Keep `ImageWithRoundedCorners` for surfaces (each = 1 draw call; budget ≤20/screen). Press feedback = alpha crossfade of pre-built layers — Profiler must show no per-frame layout rebuild during a press.

**Accessibility floor** — the master floor (§4) applies in full; module-specific: component boundaries (WCAG 1.4.11 ≥3:1) are carried by `Border.Control` (3.83:1) or `Accent.Stroke` (3.24:1) — never by shadows (ceiling 1.73:1). Focus ring passes against both the base (6.15:1) AND the shade band (3.55:1) — test both. Touch HIT rects ≥144×144u with ≥24u separation (this module tightens the master's 132 floor). `Theme.A11y.ReduceMotion`: kill scale, rise, and cascade; keep the 0.12s alpha crossfade so state stays visible. `Theme.A11y.ReduceTransparency`/`HighContrast`: all shadow layers to alpha 0, 6u `Border.Control` on every surface, selected = `Accent.Ink` fill + white label — the layout must already work flat. Russian: buttons size to content with 48u side padding and a 42→36 auto-size floor; «Бот на паузе» and the five outcome labels wrap to two lines (containers grow) rather than truncate; wells grow vertically, never clip.

**Definition of done**

1. `grep -rn 'Hex("' Assets/Editor/ --include='*.cs' | grep -v ThemeBuilderKit` returns 0 for every restyled builder; all colors resolve through `Theme`.
2. Screenshot-sampled: every neumorphic surface's fill hex is identical to its parent's on all 8 screens (status wells + channel badges exempt).
3. All shadow quads on a screen share one sprite texture + default UI material; Frame Debugger shows them in a single batch.
4. Every interactive surface has a 3u ≥3:1 stroke and a visible text label (icon-only trio `+`/send/back exempt at ≥144u hit rects); an EditMode test asserts every interactive HIT rect (raycast target incl. padding, not the visual) ≥144×144.
5. Every input, search bar, toggle groove, and pairing-code well renders inset at rest — verified across all 5 BotSettings tabs.
6. Both shadow legs on every element point the same way (light top-left); a full screenshot sweep finds zero exceptions.
7. Chat thread, chats list, and dashboard drill-down rows contain zero per-row shadows; the chat wallpaper and outgoing-green bubble are unchanged (or an owner checkpoint approving the change is on record).
8. `Tools/check-contrast.py` (committed) reads the hexes from `ThemeAsset` and asserts every pair in this module's tables; it passes in the DoD audit.
9. Primary CTA, activation switch, and delete confirm are flat saturated fills on every screen they appear.
10. DEVICE-GATED (owner UAT): draw calls ≤45 and Canvas rebuild <2 ms on Screen_Bots and Screen_Whatsapp at 1080×2400 on a mid-range Android device.
11. With all shadow layers forced to alpha 0, every screen remains fully navigable and every control identifiable.
12. Each builder run is followed by an immediate scene commit with the payload verified per the master ritual (§3.8); longest RU strings render unclipped at 1080×2400.
