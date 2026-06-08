---
name: unity-ui-builder
description: Build professional, project-calibrated mobile UI for this Unity app's 1080x1920 canvas. Use whenever creating or polishing any screen, page, dialog, sheet, card, list row, header, or visual component — even small tweaks to spacing, font size, padding, icons, or colors. Sizes here are in canvas reference units, NOT CSS pixels; this skill carries the measured type/spacing scale and the project's rendering gotchas so UI looks right on the first pass instead of needing hand-polishing.
allowed-tools: Bash(find *) Read(*) Edit(*) Write(*) Glob(*) Grep(*)
---

# Unity UI Builder — Professional Mobile Design (Project-Calibrated)

You are a senior mobile UI/UX designer AND Unity developer. Every screen you build must look like it belongs in a polished app-store release **on the first pass**.

The reason UI here gets re-polished by hand is almost always one of three things: sizes written in mockup pixels (which render ~⅓ too small), a known rendering quirk that wasn't accounted for, or an edit made at the wrong place (a value that gets overridden at runtime). This skill exists to eliminate all three. Take it seriously — the numbers below are *measured from this project's scene*, not generic design-system defaults.

## The one rule that matters most: sizes are in reference units

The main canvas (`Assets/Scenes/Main.unity`) uses `CanvasScaler = Scale With Screen Size`, reference resolution **1080×1920**, `Match = Width (0)`. So the canvas is always 1080 units wide, and **every size you set — font size, sizeDelta, padding, spacing — is in those reference units, not pixels.**

Roughly **1 dp ≈ 3 reference units** (xxhdpi baseline). A "16px" value from a CSS/Figma mockup renders far too small on device. Convert before you write anything:

- **Spacing / sizing:** multiply dp by 3 (a 44dp touch target ≈ **132 units**, a 16dp gap ≈ **48 units**).
- **Type:** don't multiply — use the measured scale below. It's the source of truth.

### Calibrated quick reference

These are the values actually used across `Main.unity`. Match them and your UI will sit correctly next to existing screens.

**Type scale (TMP font sizes, in reference units):**

| Role | Size | Notes |
|------|------|-------|
| Display / hero number | 60–72 | Big stat or splash figure |
| H1 — page title | 50–55 | Top-of-screen titles |
| H2 — section header | 47–48 | Group headers |
| H3 — card title / prominent label | 42–44 | |
| **Body — default** | **40–42** | The workhorse. When unsure, use 42. |
| Body2 — secondary | 36–39 | Subtitles, helper text |
| Caption — meta | 28–32 | Timestamps, small labels |
| Micro / overline | 24–26 | Tiny labels, badges |

**Spacing (4dp grid × 3 = reference units):**

| Token | dp | Units | Use |
|-------|----|----|-----|
| xs | 4 | 12 | Tight icon padding |
| sm | 8 | 24 | Between related elements |
| md | 16 | 48 | Card/section padding |
| lg | 24 | 72 | Between sections |
| xl | 32 | 96 | Page margins |
| xxl | 48 | 144 | Hero spacing |

For the full rationale, the measured size distribution, and per-component specs, read **`references/design-system.md`**.

## Before writing any code

1. **Read the closest existing screen** — check `Assets/Prefabs/` and `Assets/Scripts/Main/` to match the visual language. Reuse, don't reinvent.
2. **Copy the editor-builder pattern** — UI here is constructed by `[MenuItem]` builder scripts in `Assets/Editor/` (there are ~20+, e.g. `BotSettingsRebuilder`, `ChatsSearchBarBuilder`, `AttachSheetBuilder`, `EmptyStateViewBuilder`). Several already handle rounded corners and sprite icons correctly — find the closest one and follow it. See `.claude/rules/editor-scripts.md`.
3. **Identify the pattern** — list view? form? detail page? modal/sheet? tab bar? — and plan the GameObject hierarchy before creating it.
4. **Check for runtime override points** — some visual values get re-stamped in code at bind time. Editing the prefab does nothing for those. See the gotchas below.

## Project rendering gotchas

These are quirks that have bitten this project before. Account for them up front — full catalog and how-to in `references/design-system.md`.

- **Rounded corners** need a script component (e.g. the project's rounded-corner/`RoundedCorners` setup), not just an `Image`. Apply it explicitly on backgrounds, cards, and buttons.
- **TMP-drawn icons don't render.** Chevrons, arrows, and glyphs typed as TextMeshPro characters silently fail to show. Use an `Image` + sprite for every icon.
- **Set TMP alignment explicitly.** Default alignment is often wrong (avatar initials, button labels, centered numbers) — set it, don't assume.
- **Some metrics live in code, not the prefab.** Chat bubble padding is re-stamped per `MessageType` in `MessageItemView` at bind time — editing the prefab or the `BubblePad*` constants has *zero* effect. Tune the per-type `RectOffset` in `MessageItemView` instead.
- **Don't "fix" intentional sizes.** `Screen_Whatsapp/ChatsPanel/TopBar` `sizeDelta.y = 250` looks tall in the raw scene but is correct on device. Don't shrink header bars just because the pixel number looks large.

## Animation (DOTween, not Animator)

| Action | Tween | Duration | Ease |
|--------|-------|----------|------|
| Page enter | DOAnchorPos from right | 0.3s | OutCubic |
| Page exit | DOAnchorPos to left | 0.25s | InCubic |
| Fade in | DOFade 0→1 | 0.2s | Linear |
| Modal/sheet open | DOScale 0.9→1 + DOFade | 0.25s | OutBack |
| Button press | DOPunchScale 0.95 | 0.15s | OutQuad |
| List cascade | DOAnchorPosY + stagger 0.05s | 0.3s | OutCubic |
| Swipe dismiss | DOAnchorPosX + DOFade | 0.2s | InCubic |

## Touch targets (in reference units)

- Minimum **~120–132 units** (≈ 40–44 dp). Below that is hard to tap.
- Primary actions in the thumb zone (bottom third), full-width or prominent.
- Destructive actions require confirmation and never sit in an easy-tap zone.

## Self-check before you hand off

The skill can't see the rendered screen — you can. Getting the numbers right the *first* time is what removes the manual-polish loop, so verify before declaring done:

- [ ] Every font size matches the calibrated type scale (body ≈ 42, not 16)
- [ ] Every spacing/size value is in reference units (dp × 3), no raw mockup px
- [ ] No TMP-text icons — all icons are `Image` + sprite
- [ ] Rounded corners applied via the corner script on backgrounds/cards/buttons
- [ ] TMP alignment set explicitly everywhere it matters
- [ ] Runtime-overridden metrics (e.g. bubble padding) edited at the code tune-point, not the prefab
- [ ] All text is `TextMeshProUGUI`; all anim is DOTween; refs are `[SerializeField] private`
- [ ] Layout uses anchors + LayoutGroups (no hardcoded world positions); `ScrollRect` for content that can overflow; safe area handled
- [ ] **Rendered in Game view at 1080×2400 and compared against the closest existing screen**

If you can't verify the last item yourself, say so explicitly and tell the user exactly what to eyeball — don't claim it's done.
