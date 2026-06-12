# Attach sheet — WhatsApp-style circle grid redesign

Date: 2026-06-12
Status: approved (option A of four mocked directions)

## Goal

Restyle the chat attachment picker (`AttachSheet`) from a flat white rectangle with
tinted-square placeholder tiles into a WhatsApp-style bottom sheet: rounded top
corners, grabber pill, dimmed backdrop, and a row of three solid-color circles
with white glyph icons and labels. Behavior (3 actions: Camera / Gallery /
Document, native pickers, slide-up tween) is unchanged.

## Visual spec (1080×1920 canvas reference units)

| Element | Value |
|---|---|
| Sheet height | 440 (serialized `sheetHeightCanvasPx`, re-stamped by builder) |
| Sheet background | white, top-only rounded corners radius 60 (`ImageWithIndependentRoundedCorners`) |
| Grabber | 108×12 pill, color (0.78, 0.78, 0.80), centered in a 72-high area |
| Backdrop | full-screen black image, alpha 0.38, faded via `CanvasGroup` 0→1 on open |
| Circle tiles | 180 diameter (`ImageWithRoundedCorners` radius 90), one per action |
| Tile colors | Camera #E84545 · Gallery #7B5BD8 · Document #4A90E2 (existing tints) |
| Icons | white glyph sprites, 84×84, `Image` + sprite (TMP glyphs don't render) |
| Labels | TMP 32, color (0.45, 0.45, 0.48), centered, 24 below circle |
| Side padding | 72; bottom padding 96 (home indicator baked in, house convention) |
| Press feedback | Button ColorTint on circle image |

## Structure

Sheet and backdrop stay under `MovingArea` (current scene placement — moved
there from canvas root after the original build; rebuild must preserve the
existing parent). Backdrop changes from a transparent click-catcher that
excluded the sheet region to a full-screen dim layered just below the sheet.

## Files

- `Assets/Editor/AttachSheetBuilder.cs` — rebuilt to the new design; loads the
  three glyph sprites, forces Sprite import settings, wires all refs including
  `sheetHeightCanvasPx` and the new `backdropGroup`.
- `Assets/Scripts/Chat/AttachSheet.cs` — adds optional `backdropGroup`
  CanvasGroup fade (in with `openDuration`, out with `closeDuration`).
- `Assets/Images/Icons/Attach/{AttachCamera,AttachGallery,AttachDocument}.png`
  — new white glyph sprites (generated, 256×256).

## Verification

Run `Tools/Attach Sheet/Build`, open a chat, tap “+”: sheet slides up with
dimmed chat behind, three colored circles with white icons, rounded top, grabber.
Tap backdrop to dismiss (dim fades out). Compare in Game view at 1080×2400.
