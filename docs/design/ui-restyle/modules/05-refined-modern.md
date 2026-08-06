## Module 5 — REFINED MODERN (DESIGN-SYSTEM GRADE): quiet, token-perfect craft

*Restraint with precision: tinted neutrals, weight-built hierarchy, tabular numerals — no material gimmick, which means nowhere to hide.*

**Silhouette test** — a 10%-zoom screenshot must show: white surfaces on a faintly cool canvas, 1u hairlines (never boxes-in-boxes), one accent placement, and at least one large tabular numeral block. No glass, no sculpting, no visible shadow edges.
**Exclusive structural signature** (no other module may copy) — full-bleed spine rows: list rows share one 240u text spine with hairlines inset to it, edge-to-edge, no row cards. Plus the hairline countdown ring on the pairing code.

**Signatures** (every builder must repeat these four motifs — they are the identity; without them this module ships as generic fintech):
1. **The 240u spine** — every list row's text starts at x 240; hairlines inset to it; avatars at x 48.
2. **Numerals as heroes** — dashboard totals, timers, unread counts, pairing codes set in tabular figures at display sizes; values tick with zero horizontal reflow.
3. **The countdown hairline ring** — a 3u `accent/500` ring draining around the pairing code; the one piece of ornament this style permits.
4. **The check-draw** — success is a checkmark stroke drawing itself (`DOFillAmount`), never confetti, never a sweep.

**Art direction brief** — Cool, dry, exact. Every neutral carries 3–6% of the brand's 212° blue so nothing on screen is ever dead gray; hierarchy is built from three type sizes, three weights, and two ink tiers — never from size escalation. Surfaces are white on a faintly cool canvas, separated by tint steps and whitespace, not boxes; shadows exist but you can't point at them. One accent per screen, rationed like money. Numbers — the owner's orders, timers, unread counts — are the heroes, set in tabular figures that never reflow. Touchstones: **Linear** (tinted neutrals, weight-based hierarchy), **Revolut** (calm density, money-grade numerals), **Things 3** (whitespace as structure, one blue).

**Design tokens** (reference units; 1dp = 3u)

| Token | Value | Use |
|---|---|---|
| `surface/canvas` | `#F2F5F9` | screen background (replaces `#F0F2F5`, tinted 212°) |
| `surface/card` | `#FFFFFF` | cards, rows, sheets |
| `surface/sunken` | `#E9EDF3` | input rest, segmented track, wells |
| `surface/hover` / `pressed` | `#ECF1F7` / `#E2E9F2` | state fills |
| `text/primary` | `#1A1A2E` | keep — already brand-tinted |
| `text/secondary` | `#626C7A` | 5.32:1 on white, ≥4.5:1 on canvas — labels, previews, meta ON CANVAS |
| `text/tertiary` | `#6B7484` | meta/placeholder on white `surface/card` ONLY (4.71:1); on canvas use `text/secondary`. `#8A93A3` is banned from text — non-text glyphs ≥3:1 only |
| `text/disabled` | `#1A1A2E` @ 40% α | never a special gray |
| `border/hairline` | `#1A1A2E` @ 8% α, **1u tall** | dividers |
| `border/default` | `#D9E0EA` | decorative edges only (1.33:1 — NEVER the sole boundary of an interactive) |
| `border/strong` | `#7A8699` | 2u — inputs at rest, switch-off tracks (3.69:1 on white, 3.31:1-class on canvas): the 1.4.11 boundary |
| `accent/500` | `#1B7CEB` | icons, selection borders, rings — non-text only (3.62:1 on tint fails text) |
| `accent/600` / `pressed` | `#1668CC` / `#1257A8` | button fills (white text 5.40:1), text links, ink on `accent/subtle` (4.77:1) |
| `accent/subtle` / `border` | `#E8F2FD` / `#BFD9F8` | selected fills, focus ring |
| `channel/wa` / `wa-deep` | `#25D366` / `#00A884` | identity dots/chips only / icon-grade |
| `channel/tg` | `#2AABEE` | identity only, never body text |
| `feedback/danger` / `text` / `subtle` | `#E53935` / `#C62828` / `#FCE8E6` | destruction only |
| `badge/unread` | fill `#1668CC`, count white `caption` (5.40:1) | solid circle |
| `shadow/ink` | `#223247` | every shadow layer, never black |

Dashboard outcomes — each `{bg, fg, dot}`; fg ≥ 4.5:1 on bg (all five verified; this is the master's shared set):

| Outcome | bg | fg | dot |
|---|---|---|---|
| `order_collected` | `#E8F8EE` | `#14713C` (5.52) | `#23A55A` |
| `owner_needed` | `#FCE1D0` | `#9A4E0B` (4.85) | `#F8942F` |
| `in_dialog` | `#E8F2FD` | `#1257A8` (6.28) | `#1B7CEB` |
| `client_silent` | `#EEF1F5` | `#566070` (5.61) | `#8A93A3` |
| `question_closed` | `#EADCF1` | `#7A2FA6` (5.75) | `#A348D4` |

Type (size/weight, TMP characterSpacing): `display 72/700/−2` · `title-1 54/600/−1.5` · `title-2 48/600/−1` · `headline 44/600/−0.5` (list-row primary) · `body 42/400/0` · `body-strong 42/500/0` (emphasis — never 700) · `footnote 38/400/0` · `caption 30/500/+1.5` · `micro 26/600/+2`. Weights map to the 4 project fonts: 400 → `SFProText-Regular SDF.asset`, 500 → `SFProText-Medium SDF.asset`, 600 → `SFProText-Semibold SDF.asset`, 700 → `SFProText-Bold SDF.asset` (all in `Assets/TextMesh Pro/Fonts/`). Line height 1.45× body, 1.25× display, +0.05 on Cyrillic body. Spacing: only `12/24/48/72/96` on a screen; `144` for screen-top. Radii: control `30`, pill `h/2`, card `48`, sheet-top `72`; **inner = outer − padding** (48 card − 24 pad → 24 inner). Row heights fixed: `168 / 216`.

**Material recipes**

Shadows are 2–3 sibling 9-slice sprite quads behind the surface, all `shadow/ink`, all the same sprite (they batch to one draw call). Format: spread(u)/y-offset(u)/α.

- **E0 canvas content** — no shadow. Separation = `surface/card` on `surface/canvas` (bg delta) or a 1u hairline inset 48u from both edges.
- **E1 resting card** — `+6/3/.07` + `+24/9/.04`. Radius 48, no border. Must feel printed-on, not floating.
- **E2 interactive (buttons, segmented thumb, chips)** — `+6/3/.08` + `+18/6/.05`.
- **E3 popover/dropdown** — `+12/6/.08` + `+48/24/.08` + 1u border `#1A1A2E@8%`. First tier that clearly floats.
- **E4 bottom sheet/modal** — `+12/12/.06` + `+72/48/.12`, scrim `#0E1626` @ 40%, sheet gets a 1u top inner highlight `#FFFFFF@60%`.
- **Sunken (inputs at rest — always visible, never "borderless until focus")** — fill `surface/sunken`, 2u `border/strong`, label `caption` `text/secondary` 24u above, no shadow. A field must look like a field for a non-technical owner; this is a control-north-star rule, not a taste choice.

States, uniform across every control: **rest** as listed · **pressed** = fill −8% L (`surface/pressed` or `accent/pressed`) + scale 0.97, elevation −1 tier · **selected** = `accent/subtle` fill + `accent/600` ink (text) with `accent/500` reserved for the border/icon · **focused** = 6u ring `accent/border` offset 6u · **disabled** = whole control at 40% α, elevation E0. Never desaturate the accent to disable it.

**Guardrails — do NOT**

- Do not use `#FFFFFF`-adjacent pure grays or `#000000` shadows anywhere; every neutral and shadow carries the 212° tint.
- Do not put more than 3 type sizes or 2 accent placements on one screen.
- Do not build hierarchy by size alone — adjacent tiers must differ in weight or ink color.
- Do not use weight 700 inside body content; emphasis is 500.
- Do not draw a border AND a shadow AND a bg-delta on the same surface — earn exactly one (two only at E3+; the Sunken input's fill+border pair is the sanctioned exception, because input affordance outranks minimalism).
- Do not nest cards in cards; group with `72` whitespace and `micro` overlines.
- Do not ship a single fat shadow (`0/12/.15`); every shadow is 2–3 layers of `#223247` per the E-table.
- Do not let any hairline exceed 1u or 10% α.
- Do not use proportional digits on any value that changes or aligns in a column.
- Do not give every element r `30`; radii nest by subtraction and pills are h/2.
- Do not animate anything over 0.45s, use bounce/elastic on functional controls, or overshoot a color/fade.
- Do not fix button/chip widths — RU strings grow them; system labels never truncate.
- Do not encode any delta or trend by color alone — a leading +/− sign or ▲/▼ arrow is mandatory (the sign column is free in tabular figures).
- Do not use `#8A93A3` or `accent/500` as text anywhere; do not put `text/tertiary` on the canvas.
- Do not build new systems (toasts, skeletons) — restyle the existing affordances only.

**Icons & type voice**

Icons: 2u-stroke geometric outline sprites, open terminals, on a 66u grid — drawn, not filled; chevrons `36` in `#6B7484`. Always Image + sprite (TMP glyphs never render). Type: the 4 named SFProText SDF fonts above; no new typefaces. Illustration: reuse `bot_hero.png` and existing empty-state art only; no new illustration assets without an owner decision.

**Component specs**

- **Primary button** — h `132`, r `30`, fill `accent/600`, label `body-strong` white, pad-x `72`, min-w `320` + grow (RU). One per screen.
- **Secondary button** — same box, fill `surface/card`, 2u `border/strong`, label `accent/600`. Tertiary = text-only, h `132`, no box.
- **Bot card** (`Bot.prefab`) — full-width, r `48`, E1, pad `48`; top row: avatar `144`, name `headline`, channel dot `24`; 1u hairline inset `48`; footer h `168` with the activation switch. Status text «Бот работает» `footnote` `#14713C` / «Бот на паузе» `footnote` `text/secondary`.
- **Chat row** (`ChatItem`) — h `216`, E0 full-bleed, avatar `156` at x `48`, text spine x `240` (every row shares it), name `headline`, preview `footnote` `text/secondary` 1-line tail-ellipsis, time `caption` `text/tertiary` right-aligned tabular (rows are white full-bleed — tertiary legal), unread badge `54` circle `badge/unread`. Hairline between rows inset to the spine.
- **Input field** — h `132`, r `30`, Sunken recipe (visible at rest, everywhere — including BotSettings); focus → fill white, 2u `accent/500` border + 6u ring `accent/subtle`.
- **Switch** — track `156×90` r `45`; off `#D9E0EA` fill + 2u `border/strong` (the visible boundary — an off switch must be findable); on `#25D366` (per-BOT always, never per-channel — master rule); knob `78` white E2, travel `66`; RU label swaps. Hit rect padded to ≥132×132.
- **Status pill** — h `72`, r `36`, pad-x `36`, dot `24` + `caption` in `{fg}` on `{bg}`; grows/wraps rather than truncating. Never solid-filled in lists; solid `dot` color reserved for the dashboard legend.
- **Filter chip** — h `72` visual, hit rect padded to `132` (state the raycast padding in the builder); selected = `accent/subtle` fill + `accent/600` label + `accent/500` border.
- **Reaction pill** — h `60` white E2 visual, hit rect padded to ≥`120`.
- **Segmented control (BotSettings tabs)** — h `132`, Sunken track + 2u `border/strong`, white E2 thumb; labels `caption 30/600`; if the longest RU label exceeds its segment at 30u, the control scrolls horizontally — never shrink below 30, never ellipsize.
- **Tab bar** — h `204` (baked), 1u top hairline, icon `72` sprite + `micro` label; active icon `accent/500` (3.74:1 ≥3 icon-legal) + label `#1257A8` 600 (6.50:1), inactive icon + label `#626C7A` 400.
- **Bottom sheet** — r-top `72`, E4, grabber `108×12` r `6` `#D9E0EA`, pad `72`, title `title-2`.
- **Modal** — w `888`, r `48`, E4, pad `72`, stacked full-width buttons `24` apart, destructive label `feedback/text`.
- **Empty state** — illustration `480`, `title-2` + `footnote` `text/secondary` max-w `768`, primary button `96` below.
- **Avatar** — `156` chat / `144` bot card / `108` dashboard row, circle, fallback `surface/sunken` + initial `headline` `text/secondary`.
- **Search bar** — h `132`, r `30`, Sunken recipe.

**Screen-by-screen direction**

- **Onboarding** — one `display` line, one `footnote`, one primary button per pane; the only saturated color is the channel being connected. Hero: the success moment — the check-draw in `channel/wa-deep`.
- **Bots list** — cards at E1 with `48` gaps on the tinted canvas; header `+` becomes the screen's single accent (top placement accepted: low-frequency, first-run covered by the empty-state CTA). Hero: the activation switch — largest, highest-contrast control on the card.
- **Dashboard (Сводка)** — one anchor card: period total in `display` tabular figures with the delta in `caption` outcome color + mandatory ▲/▼ or +/− sign; filter chips per spec; drill-down rows h `168` flat with hairlines on the spine. Hero: the delta ticking over without horizontal shift.
- **Chats list** — kill boxed rows; fixed `216` rows on the shared `240` spine, search bar per spec. Unread rows get FULL-strength `accent/subtle` as a full-bleed tint step (a 40%-alpha tint composites to a 9/5/1 RGB delta — invisible on 6-bit-dithered panels and in sunlight) + the badge; name stays `headline`.
- **Chat thread** — keep the doodle wallpaper (`#F5F2EA/#E5DAC6` is a trust asset); incoming bubble white E1 r `42`, outgoing `#C5EEB6` kept, quoted card = 6u left bar in sender color + `#1A1A2E@5%` well r `24`, reaction pill per spec, ticks `text/tertiary` → read `accent/500` (icon-grade). Hero: delivery ticks in tabular-time rows.
- **Bot Settings** — 5 tabs become the segmented control; sections separated by `72` whitespace + `micro` overlines, no cards-in-cards; every field Sunken-at-rest. Hero: the white E2 thumb gliding across the Sunken track between tabs — 0.20s OutCubic, the one piece of physicality this style permits itself.
- **Add-Bot wizard** — thin `6u` progress rule under the header; business-type tiles r `48` E0 with 2u `border/strong` that flips to `accent/500` border + `accent/subtle` fill + `accent/600` label when selected. Hero: the pairing code in `display` tabular digits with the countdown hairline ring.
- **Profile** — grouped white blocks r `48` E1 on canvas, rows `168`, chevron sprites `36` `#6B7484`; the wipe action uses `feedback/text` and nothing else on the screen is red.

**States (loading / error / empty)** — per the master triad: loading >300ms = restyle the EXISTING indicators only (chat-list initial sync, dashboard fetch) to a 5%-α `shadow/ink` sweep at 1.4s Linear — do NOT build a new skeleton system; failure = reuse the app's existing failure affordances (optimistic-delete rollback in `ChatManager.DeleteChat.cs`, `PopupUI`) restyled with `DOShakePosition(15u)` 0.30s — do NOT introduce a toast system; empty = illustration + one CTA (above). Loads <300ms show nothing.

**Motion & feedback** (DOTween; exits ~20% shorter than enters; color/fade never overshoots)

| Action | Tween | Duration | Ease |
|---|---|---|---|
| Press down / release | `DOScale(0.97)` / back | 0.10 / 0.15 | `OutQuad` / `OutCubic` |
| Screen enter | `DOAnchorPosY(+36→0)` + `DOFade(0→1)` | 0.30 | `OutCubic` |
| Screen exit | fade + `−24` | 0.22 | `InQuad` |
| Sheet open / close | slide + scrim `DOFade(0→.40)` | 0.28 / 0.22 | `OutCubic` / `InQuad` |
| Segment thumb / switch knob | `DOAnchorPosX` | 0.20 | `OutCubic` |
| List first paint | 30ms stagger, cap 8, y `+30` + fade | 0.25 each | `OutCubic` |
| Toggle/send feedback | keep every existing optimistic/synchronous behavior exactly as-is — this row styles the visual feedback only | — | — |
| **Peak: bot connected** | check `DOFillAmount(0→1)` 0.25 + one ring `DOScale(1→1.6)`+`DOFade(.30→0)` in channel color | 0.45 total | `OutCubic` |
| **End: settings saved** | button label cross-fades to check, holds 0.6s, reverts | 0.15 | `OutQuad` |

Nothing exceeds 0.45s.

**Unity notes — style-specific deltas only** (the universal contract lives in the master)

- **No blur anywhere in this style** — E4 uses a plain scrim `Image`; the snapshot machinery is unnecessary; skip it entirely.
- Bake **two shadow sprites** (r48 / r30 families, 128×128, 48px slice borders); `ThemeBuilderKit.AddSoftShadow(go, Elevation)` spawns the 2–3 sibling quads; `AddHairline(parent, inset)` = 1u `Image` `#1A1A2E@8%`; add a `caption`-overline helper.
- Tabular figures: TMP has no `tnum` toggle — wrap changing numerals in `<mspace=0.62em>` (starting value — verify against the SF Pro digit advance by watching a timer tick) or bake a digits-uniform-advance font variant. Applies to timers, counters, unread badges, dashboard totals.
- "No ad-hoc `fontSize`" applies to builder-emitted text only; `MessageItemView` keeps its code-tuned bubble metrics but reads its COLORS from the runtime `Theme` facade.
- Prefer bg-delta and hairlines over rounded containers: every Nobi element is a draw call — this style should sit well under the ≤20 budget.

**Accessibility floor** — the master floor (§4) applies in full; module-specific: `text/tertiary` never on canvas, never in sentences; large text (≥57u or 42u/600) and icons/borders/focus rings ≥3:1; focus ring 6u `accent/border` offset 6u, never removed. `Theme.A11y.ReduceMotion`: swap translates for 0.15s cross-fades, kill stagger and the peak ring, keep all state feedback. Russian: design against the longest real strings («Требуется владелец», «Удалить все данные»); buttons/chips/tabs min-width + grow; titles wrap to 2 lines with reserved line-height; ellipsis only on user content (names, previews); no ALL-CAPS Cyrillic labels; +0.05 line-height on body blocks. Degraded fallback (shadows/sprites unavailable): flat `surface/card` on `surface/canvas` with 1u hairlines — the layout must still read with all elevation off.

**Definition of done**

1. `grep -rn 'Hex("' Assets/Editor/ --include='*.cs' | grep -v ThemeBuilderKit` returns 0 for every restyled builder; all colors resolve through `Theme`.
2. Every text element uses one of the 9 named type tokens; no ad-hoc `fontSize` in any builder (`MessageItemView` code-metrics exempt).
3. Each screen uses ≤5 spacing values, ≤3 type sizes, exactly 1 primary action, ≤2 accent uses — counted on one screenshot per screen, attached to the PR.
4. `Tools/check-contrast.py` asserts: all 5 outcome-pill pairs, body text, button labels, tab labels ≥4.5:1; icons/borders ≥3:1 — including `text/tertiary`-on-white and `accent/600`-on-tint.
5. Every divider on screen is exactly 1u tall at ≤10% α (inspect in the Editor hierarchy).
6. All shadows are sprite-quad stacks from the 2 shared sprites; Frame Debugger shows every shadow on a screen batching into ≤2 draw calls; totals ≤45 draw calls on Screen_Bots, Screen_Dashboard, and the chat list.
7. Dashboard total, timers, unread badges, and message times tick with zero horizontal reflow (screen-record one tick); every delta carries a ▲/▼ or +/− sign.
8. Every interactive hit rect ≥132×132u — assert via an EditMode test over the rebuilt hierarchy (chips 72-visual and reaction pills 60-visual pass via padded raycast rects).
9. Every input and the switch-off state show a ≥3:1 boundary (`border/strong`) with all shadows disabled — screenshot audit.
10. Each restyled builder re-runs from scratch with zero missing serialized references and zero `transform.Find` breaks (grep log attached to the PR).
11. A grayscale screenshot of each screen still shows correct hierarchy (weight/ink carry it, not color) and every unread row is still identifiable.
12. Longest-RU-string pass at 1080×2400: no truncated system label, no fixed-width overflow, wizard and BotSettings segmented control included; each builder run committed per the master ritual (§3.8).
