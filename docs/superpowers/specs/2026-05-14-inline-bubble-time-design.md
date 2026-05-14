# Inline Time on Message Bubbles — Design

**Date:** 2026-05-14
**Status:** Approved, pending implementation plan
**Author:** Brainstorm session with user

## Problem

Message bubbles in `Screen_Whatsapp/ChatsPanel` always stack the `Time` (timestamp + delivery tick) under the message text. Even when the text is short and the bubble has horizontal room, the time wraps to its own line, stretching the bubble vertically and producing a visually clunky result.

Reference target behavior — WhatsApp:

- **Short text** — bubble stays single-line; time sits inline to the right of the text.
- **Long text, last line has room** — time sits on the last line, to the right of the trailing word.
- **Long text, last line is full** — time wraps to a new line; bubble grows by one line.

This applies to every bubble that has wrappable text: text-only messages, image/video captions, and link-preview text. Audio, document, and image-only (no caption) bubbles keep their current layouts.

## Approach

Use the **TMP space-reservation pattern**: append a `<space={timeWidth}px>` tag at the end of the wrappable text and absolutely position the time at the bubble's bottom-right corner. TMP wraps the trailing `<space>` as if it were a normal glyph, so all three cases above fall out of TMP's wrapping logic without custom layout code.

Alternative approaches considered and rejected:

- **Custom horizontal "last-line + time" layout** — required pre-measuring via `textInfo.ForceMeshUpdate()` and branching on last-line width. Two code paths, layout-flicker risk, fragile across status updates and caption layouts.
- **Custom `ILayoutGroup` / `ILayoutElement` component** — most thorough, also the most code and the biggest blast radius. Overkill for a behavior the space-reservation trick nails in ~20 lines.

The space-reservation trick is what WhatsApp Web, Telegram Web, and Discord all do.

## Scope of changes

- **`Assets/Scripts/UI/MessageItemView.cs`** — all code changes live here.
- **No prefab edits.** The `Time` RectTransform is reconfigured at runtime in `Awake`; existing `MessageTextOut`/`MessageTextInc` prefabs are not opened.

## Design

### 1. Float the Time object (one-time setup per bubble)

A new `ConfigureFloatingTime()` runs once from `Awake()`:

- `timeText.GetComponent<LayoutElement>().ignoreLayout = true` — the bubble's `VerticalLayoutGroup` no longer reserves vertical space for time.
- Anchor `timeText` to bottom-right of `Bubble`: `anchorMin = anchorMax = (1, 0)`, `pivot = (1, 0)`.
- Position is now controlled by `anchoredPosition`, not `timeText.margin`. The existing per-layout offsets (`new Vector4(0, 0, 12, -2)` etc. in `ApplyDynamicLayout`) get translated into `Vector2` anchored positions through a small mapping in `ApplyDynamicLayout`.

### 2. New helpers in `MessageItemView`

```
float MeasureTimeWidth()
    → timeText.GetPreferredValues(timeText.text, ∞, ∞).x + 8f
    (8px is the visual breathing gap between text and time)

string StripTrailingReservation(string s)
    → if s ends with "<space=...>", remove that single trailing tag

void ApplyInlineTimeReservation(TextMeshProUGUI target)
    → early-exit if isJumboEmoji, target inactive, or empty text
    → target.text = StripTrailingReservation(target.text) + $"<space={MeasureTimeWidth()}px>"
```

### 3. Wiring into the `Bind()` flow

`Bind()` order today: set processed text → media setup → `RefreshTimeAndTick()` → `ApplyDynamicLayout()` → `AdjustTextBubbleSize()`.

Insert one call between `RefreshTimeAndTick()` and `AdjustTextBubbleSize()`:

```csharp
ApplyInlineTimeReservation(messageText);
```

`AdjustTextBubbleSize()` is unchanged. It already measures `messageText.text` via `GetPreferredValues(..., availableWidth, ∞)`, which naturally accounts for the trailing `<space>`. Both "short text → bubble widens" and "long text → wrap as needed" emerge from existing TMP wrapping. No new width math needed.

### 4. Status-change re-application

`HandleStatusChanged` already mutates `timeText` when the delivery tick changes (`tick_pending` / `tick_sent` / `tick_double_blue` / `tick_failed`). Tick sprite widths are not guaranteed identical, so after the tick update:

```csharp
ApplyInlineTimeReservation(messageText);
LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
```

Cost: one TMP measure plus a string replace at the end of the message text — negligible.

### 5. Excluded layouts (no behavior change)

| Layout | Why excluded |
|---|---|
| Jumbo emoji | Already returns early in `AdjustTextBubbleSize`; reservation helper mirrors that early-exit. |
| Audio / Voice | No wrappable text. |
| Document | Fixed two-line ellipsized filename; current time placement stays. |
| Image / Video without caption | Time stays as the dark-card overlay on the image; the existing `useCardLayout` branch in `ApplyDynamicLayout` owns this case. Reservation only runs when `messageText` is active and non-empty. |
| Expired / Download / Loading card states | Already use `useCardLayout = true`; same exclusion. |

## Edge cases

| Case | Behavior |
|---|---|
| Short text-only message | Single-line bubble grows wide enough to fit `text + <space>`; time visually sits on the right of the line. |
| Long text, last line has room | `<space>` stays on last line; time visually next to last word. |
| Long text, last word fills line | TMP wraps `<space>` to a new line; bubble grows by one line; time sits on that line. |
| Single very long unbreakable word | Existing `SplitLongWord` logic plus reservation continues to work. |
| Caption under image/video | Caption gets reservation; image height + caption height stack normally; time sits on caption's last line. |
| Link preview with text | Same as caption — reservation applies to `messageText`. |
| Status update (pending → sent → read) | Tick changes → reservation recomputed → bubble width adjusts if tick width changed. |
| Emoji sprites in text | Reservation appended **after** `UnicodeEmojiConverter.ConvertRealEmojisToSprites`; TMP processes `<space>` and `<sprite>` tags independently. |
| Sender name shown (group chat) | Sender name is a separate child of `Bubble`; reservation only modifies `messageText`. Sender-name layout is unchanged. |

## Testing plan

Manual session in Unity Editor, Game view at iPhone 12 / 1080×2400:

1. Short text-only outgoing → time inline on the right of text.
2. Two-line outgoing, last line short → time on last line.
3. Outgoing where last word fully fills the line → `<space>` wraps; time on a new line.
4. Repeat cases 1–3 incoming (no tick) → verify reserved width differs correctly.
5. Image + short caption → time on caption's line.
6. Image + long caption that wraps → time on caption's last line.
7. Image only (no caption) → time stays as dark-card overlay (unchanged behavior).
8. Audio message → time unchanged.
9. Outgoing message status transitions (pending → sent → delivered → read) → bubble redraws cleanly, no flicker.

## Risks & mitigations

- **TMP `<space>` interactions with other rich-text tags** — mitigated by appending the tag at the very end of the string and after emoji-sprite conversion. TMP processes rich-text tags sequentially and independently.
- **Tick sprite width variance** — mitigated by recomputing the reservation on every status change.
- **User text accidentally ending with `<space=...>`** — extremely unlikely in chat content, and `StripTrailingReservation` only strips a single trailing tag (so a malicious one would be removed, which is harmless).

## Out of scope

- Reaction pills, reply previews, forwarded headers (no plans to change their positioning).
- Group sender-name color/styling.
- Tick sprite redesign — only the placement math reacts to width changes.
