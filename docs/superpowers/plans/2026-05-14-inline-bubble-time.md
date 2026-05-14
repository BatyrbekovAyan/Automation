# Inline Time on Message Bubbles — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `timeText` in message bubbles flow inline with the message text (WhatsApp-style): short text → inline; long text with room on last line → on last line; long text without room → on a new wrapped line.

**Architecture:** Float `timeText` out of the bubble's `VerticalLayoutGroup` by setting `LayoutElement.ignoreLayout = true` and anchoring it to bottom-right of `Bubble`. Append a `<space={timeWidth}px>` TMP rich-text tag at the end of every wrappable message text. TMP's wrapping logic naturally produces all three target behaviors without custom layout code.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, TextMeshPro, URP. No test framework in this project — verification is manual in the Unity Editor (Game view, iPhone 12, 1080×2400).

**Spec:** [docs/superpowers/specs/2026-05-14-inline-bubble-time-design.md](../specs/2026-05-14-inline-bubble-time-design.md)

---

## File Structure

All changes are confined to one file:

- **Modify:** `Assets/Scripts/UI/MessageItemView.cs`
  - New fields for cached state
  - New private methods: `ConfigureFloatingTime`, `PositionFloatingTime`, `MeasureTimeWidth`, `StripTrailingReservation`, `ApplyInlineTimeReservation`
  - Call sites added in `Awake`, `AdjustTextBubbleSize`, `ApplyDynamicLayout`, `SetDeliveryStatus`

No prefab edits. No new files. No new tests (project has no test framework; verification is in the editor).

---

## Pre-flight

- [ ] **Step 1: Confirm Unity Editor will open**

Run: `ls /Users/ayan/Projects/Automation/Assets/Scenes/Main.unity`
Expected: file exists.

- [ ] **Step 2: Confirm starting branch state is clean**

Run: `git status`
Expected: `nothing to commit, working tree clean` (the spec was committed in the brainstorming step).

---

## Task 1: Add the floating-time infrastructure (no behavior change yet)

This task reconfigures `timeText` to be absolutely anchored to the bottom-right of `Bubble`, but does **not** yet append `<space>` to the message text. After this task, bubbles will look almost identical to today (some pixel-level drift may appear in the time position — that's OK; we tune in Task 5).

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs:94-104` (Awake)
- Modify: `Assets/Scripts/UI/MessageItemView.cs:480-687` (ApplyDynamicLayout)
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (add new methods near the end of the class, just before `RefreshTimeAndTick` at line 2692)

- [ ] **Step 1: Add field to track whether floating-time has been configured**

In the field block near the top of the class (around line 78–88), add:

```csharp
private bool floatingTimeConfigured = false;
private Vector2 lastFloatingTimePosition = Vector2.zero;
```

- [ ] **Step 2: Add `ConfigureFloatingTime()` method**

Add this method at the bottom of the class, immediately **before** `private void RefreshTimeAndTick()` (currently at line 2692):

```csharp
// Reconfigures timeText to be absolutely positioned at bottom-right of Bubble
// so it no longer reserves vertical space in the bubble's VerticalLayoutGroup.
// Runs once per bubble instance; position offsets are applied later by
// PositionFloatingTime() inside ApplyDynamicLayout.
private void ConfigureFloatingTime()
{
    if (timeText == null) return;
    if (floatingTimeConfigured) return;

    var le = timeText.GetComponent<LayoutElement>();
    if (le == null) le = timeText.gameObject.AddComponent<LayoutElement>();
    le.ignoreLayout = true;

    var rt = timeText.rectTransform;
    rt.anchorMin = new Vector2(1f, 0f);
    rt.anchorMax = new Vector2(1f, 0f);
    rt.pivot = new Vector2(1f, 0f);

    if (timeBackground != null)
    {
        var bgRt = timeBackground.transform as RectTransform;
        if (bgRt != null)
        {
            bgRt.anchorMin = new Vector2(1f, 0f);
            bgRt.anchorMax = new Vector2(1f, 0f);
            bgRt.pivot = new Vector2(1f, 0f);
        }
    }

    floatingTimeConfigured = true;
}
```

- [ ] **Step 3: Add `PositionFloatingTime()` helper**

Add this method directly below `ConfigureFloatingTime()`:

```csharp
// Sets timeText.rectTransform.anchoredPosition relative to the
// bottom-right corner of Bubble. rightInset and bottomInset are
// positive values; a rightInset of 12 means 12px in from the right edge.
private void PositionFloatingTime(float rightInset, float bottomInset)
{
    if (timeText == null) return;
    var rt = timeText.rectTransform;
    var pos = new Vector2(-rightInset, bottomInset);
    rt.anchoredPosition = pos;
    lastFloatingTimePosition = pos;

    if (timeBackground != null)
    {
        var bgRt = timeBackground.transform as RectTransform;
        if (bgRt != null) bgRt.anchoredPosition = pos;
    }
}
```

- [ ] **Step 4: Call `ConfigureFloatingTime()` from `Awake()`**

In `Awake()` (line 94–104), after the existing initialization, add a call to `ConfigureFloatingTime()`. The complete method should read:

```csharp
void Awake()
{
    rectTransform = GetComponent<RectTransform>();
    audioSource = GetComponent<AudioSource>();
    if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

    if (downloadButton != null)
    {
        downloadButtonText = downloadButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    ConfigureFloatingTime();
}
```

- [ ] **Step 5: Replace `timeText.margin` writes inside `ApplyDynamicLayout` with `PositionFloatingTime` calls**

Inside `ApplyDynamicLayout` (line 480–687), every occurrence of `timeText.margin = new Vector4(0, 0, X, Y);` should be replaced with `PositionFloatingTime(X, -Y);` **and the `timeText.margin = Vector4.zero` reset at line 497 should be kept** (it zeroes TMP-internal padding so the absolute position alone controls placement).

Mapping table (apply each one):

| Line | Old | New |
|---|---|---|
| 497 | `timeText.margin = new Vector4(0, 0, 0, 0);` | *keep — resets internal TMP padding* |
| 516 | `timeText.margin = new Vector4(0, 0, 6, -2);` | `PositionFloatingTime(6f, 2f);` |
| 526 | `timeText.margin = new Vector4(0, 0, 6, -2);` | `PositionFloatingTime(6f, 2f);` |
| 555 | `timeText.margin = new Vector4(0, 0, 6, -2);` | `PositionFloatingTime(6f, 2f);` |
| 566 | `timeText.margin = new Vector4(0, 0, 18, 4);` | `PositionFloatingTime(18f, -4f);` |
| 577 | `timeText.margin = new Vector4(0, 0, 10, 4);` | `PositionFloatingTime(10f, -4f);` |
| 588 | `timeText.margin = new Vector4(0, 0, -4, -8);` | `PositionFloatingTime(-4f, 8f);` |
| 607 | `timeText.margin = new Vector4(0, 0, 6, -2);` | `PositionFloatingTime(6f, 2f);` |
| 622 | `timeText.margin = new Vector4(0, 0, 6, -2);` | `PositionFloatingTime(6f, 2f);` |
| 663 | `timeText.margin = new Vector4(0, 0, 6, -2);` | `PositionFloatingTime(6f, 2f);` |
| 669 | `timeText.margin = new Vector4(0, 0, 12, -2);` | `PositionFloatingTime(12f, 2f);` |
| 684 | `timeText.margin = new Vector4(0, 0, 12, -2);` | `PositionFloatingTime(12f, 2f);` |

The transformation rule is: `Vector4(0, 0, X, Y)` → `PositionFloatingTime(X, -Y)`. The Y is flipped because TMP margin's "bottom" grew the text downward (negative pulls text down), whereas `anchoredPosition.y` is positive-up from the bottom anchor.

The `timeText.overflowMode = TextOverflowModes.Overflow;` at line 587 stays — it isn't position-related.

- [ ] **Step 6: Compile-check in Unity Editor**

Open the project in Unity Hub. Wait for the script reload (bottom-right spinner finishes). Open the **Console** tab.

Expected: zero compile errors, zero new warnings related to `MessageItemView.cs`.

- [ ] **Step 7: Visual smoke test (no behavior change)**

In the Unity Editor:
1. Open `Assets/Scenes/Main.unity`.
2. Enter Play mode.
3. Navigate to an existing chat that has visible text-only messages.

Expected: bubbles still look essentially the same as before. Time positioning may have shifted by a few pixels (because absolute positioning ≠ TMP margin), but time is still visible at the bottom-right of each bubble, and bubbles still grow vertically to fit time below text. This is intentional — Task 3 introduces the inline behavior.

If the time appears completely missing or massively offset: stop and re-check Step 5's mapping for the layout type that was visible (use Inspector to read `timeText.rectTransform.anchoredPosition` on a live bubble).

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
refactor(chat): float timeText with absolute positioning

Switches timeText from VerticalLayoutGroup child to an absolutely-anchored
overlay at the bubble's bottom-right corner. No visual change yet — this is
the infrastructure for the upcoming inline-time behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add measurement and reservation helpers (no behavior change yet)

Adds three pure helpers that the next task will wire in. After this task, nothing visible changes.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (add methods directly below `PositionFloatingTime`)

- [ ] **Step 1: Add `MeasureTimeWidth()`**

Add directly below `PositionFloatingTime()`:

```csharp
// Returns the pixel width that needs to be reserved at the end of a
// wrappable text so timeText fits inline. Includes an 8px visual gap
// between the trailing word and the time.
private float MeasureTimeWidth()
{
    if (timeText == null) return 0f;
    if (string.IsNullOrEmpty(timeText.text)) return 0f;
    float w = timeText.GetPreferredValues(timeText.text, Mathf.Infinity, Mathf.Infinity).x;
    return w + 8f;
}
```

- [ ] **Step 2: Add `StripTrailingReservation()`**

Add directly below `MeasureTimeWidth()`:

```csharp
// Removes a single trailing TMP <space=...> tag if present. Used to
// scrub the previous reservation before appending a fresh one, so the
// text doesn't accumulate stacked space tags across re-binds and
// status-change re-renders.
private static string StripTrailingReservation(string input)
{
    if (string.IsNullOrEmpty(input)) return input;
    if (input.Length == 0 || input[input.Length - 1] != '>') return input;
    int openIdx = input.LastIndexOf("<space=", System.StringComparison.Ordinal);
    if (openIdx < 0) return input;
    int closeIdx = input.IndexOf('>', openIdx);
    if (closeIdx != input.Length - 1) return input;
    return input.Substring(0, openIdx);
}
```

- [ ] **Step 3: Add `ApplyInlineTimeReservation()`**

Add directly below `StripTrailingReservation()`:

```csharp
// Appends a TMP <space={width}px> tag to the end of target.text so the
// last line reserves horizontal room for the inline timestamp. TMP's
// wrap logic treats the space as a regular glyph, so a full last line
// pushes the space to a new line (and timeText, anchored at bottom-right,
// follows visually).
private void ApplyInlineTimeReservation(TextMeshProUGUI target)
{
    if (target == null) return;
    if (!target.gameObject.activeSelf) return;
    if (string.IsNullOrEmpty(target.text)) return;
    if (isJumboEmoji) return;

    float w = MeasureTimeWidth();
    if (w <= 0f) return;

    string baseText = StripTrailingReservation(target.text);
    target.text = $"{baseText}<space={w:0.##}px>";
}
```

- [ ] **Step 4: Compile-check in Unity Editor**

Switch focus to Unity. Wait for reload.

Expected: zero compile errors. Helpers exist but no caller yet, so no behavior change.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(chat): add inline-time reservation helpers

Pure helpers (MeasureTimeWidth, StripTrailingReservation,
ApplyInlineTimeReservation) — no callers yet, no behavior change.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Wire the reservation into `AdjustTextBubbleSize` (this is where the visual change happens)

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs:713-744` (start of `AdjustTextBubbleSize`)

- [ ] **Step 1: Call `ApplyInlineTimeReservation` at the top of `AdjustTextBubbleSize`**

Locate `AdjustTextBubbleSize()` at line 713. The current method opens like:

```csharp
void AdjustTextBubbleSize()
{
    LayoutElement textLayout = messageText.GetComponent<LayoutElement>();
    if (textLayout == null) textLayout = messageText.gameObject.AddComponent<LayoutElement>();

    LayoutElement timeLayout = null;
    if (timeText != null)
    {
        timeLayout = timeText.GetComponent<LayoutElement>();
        if (timeLayout == null) timeLayout = timeText.gameObject.AddComponent<LayoutElement>();
    }

    if (isJumboEmoji)
    {
```

Insert a call to `ApplyInlineTimeReservation` between the LayoutElement guards and the jumbo-emoji check. The result should read:

```csharp
void AdjustTextBubbleSize()
{
    LayoutElement textLayout = messageText.GetComponent<LayoutElement>();
    if (textLayout == null) textLayout = messageText.gameObject.AddComponent<LayoutElement>();

    LayoutElement timeLayout = null;
    if (timeText != null)
    {
        timeLayout = timeText.GetComponent<LayoutElement>();
        if (timeLayout == null) timeLayout = timeText.gameObject.AddComponent<LayoutElement>();
    }

    // Reserve trailing space in the wrappable text so the floating timeText
    // sits inline. Skipped automatically for jumbo emoji / empty / inactive text.
    ApplyInlineTimeReservation(messageText);

    if (isJumboEmoji)
    {
```

This call must happen **before** the `messageText.GetPreferredValues(...)` measurements further down so that the `<space>` is included in the measured width.

- [ ] **Step 2: Compile-check in Unity Editor**

Switch to Unity, wait for reload.

Expected: zero compile errors.

- [ ] **Step 3: Visual verification — short text-only outgoing**

In Play mode, open a chat. Send (or look at an existing) short outgoing message — e.g. "Hi".

Expected: bubble fits "Hi 00:24 ✓✓" on a single line. Time is to the right of the text on the same row.

If time is still on its own row below: confirm `ApplyInlineTimeReservation` is being reached — set a breakpoint or `Debug.Log` at the top of the method and re-bind a chat.

If the bubble is far too wide: `MeasureTimeWidth()` is returning a much-too-large value. Inspect `timeText.text` in Inspector and re-measure.

- [ ] **Step 4: Visual verification — two-line text, last line short**

Send/look at a message whose text wraps to two lines and where the last line has obvious room (e.g. "This message is a little long but the last line is short.").

Expected: time appears at the right end of the last line, not below it.

- [ ] **Step 5: Visual verification — text where last line fills the width**

Send/look at a message whose last word fills the line so the time can't fit beside it (e.g. one long sentence that wraps exactly so the wrap word lands at the right edge).

Expected: bubble has one extra line at the bottom containing only the time on the right. This is the "natural wrap of `<space>`" case.

- [ ] **Step 6: Visual verification — incoming bubble (no tick)**

Look at any incoming text message.

Expected: time inline with text. The reserved space is narrower than for outgoing (no tick sprite) — bubble width should snug correctly.

- [ ] **Step 7: Visual verification — image-only (no caption)**

Look at a media bubble with no caption.

Expected: time still appears as the dark-card overlay on the image (unchanged behavior). The `ApplyInlineTimeReservation` guard `if (string.IsNullOrEmpty(target.text)) return;` skips the reservation since `messageText` is inactive/empty for these.

- [ ] **Step 8: Visual verification — image with caption**

Look at a media bubble with a short caption.

Expected: time appears inline on the caption's line (when room) or on a new wrapped line (when not).

- [ ] **Step 9: Visual verification — audio message**

Look at an audio message.

Expected: time unchanged from previous build.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
feat(chat): inline timeText on the last line of message bubbles

Appends a TMP <space=Xpx> reservation to wrappable bubble text so the
floating timeText sits next to the trailing word when there is room,
and wraps to a new line otherwise. Mirrors WhatsApp behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Re-apply the reservation on delivery-status changes

When a tick sprite changes (pending → sent → delivered → read → failed), `timeText.text` is rewritten by `RefreshTimeAndTick()`. The tick sprite widths are not guaranteed identical, so the reserved space must be recomputed.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs:2701-2707` (SetDeliveryStatus)

- [ ] **Step 1: Re-apply reservation in `SetDeliveryStatus`**

The current method (around line 2701–2707) reads:

```csharp
private void SetDeliveryStatus(DeliveryStatus newStatus)
{
    if (currentVm == null || currentVm.isIncoming) return;
    currentVm.deliveryStatus = newStatus;
    RefreshTimeAndTick();
    UpdateRetryButton(newStatus == DeliveryStatus.Failed);
}
```

Replace it with:

```csharp
private void SetDeliveryStatus(DeliveryStatus newStatus)
{
    if (currentVm == null || currentVm.isIncoming) return;
    currentVm.deliveryStatus = newStatus;
    RefreshTimeAndTick();
    UpdateRetryButton(newStatus == DeliveryStatus.Failed);

    // Tick width may have changed — recompute the reserved space and
    // ask the layout to redraw so the bubble width re-snaps if needed.
    ApplyInlineTimeReservation(messageText);
    if (rectTransform != null) LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
}
```

- [ ] **Step 2: Compile-check in Unity Editor**

Wait for reload.

Expected: zero compile errors.

- [ ] **Step 3: Visual verification — status transition cycle**

Send a new outgoing message while the network is briefly slow, or pull-to-refresh a chat with recent unread messages. Watch a message cycle through Pending → Sent → Delivered → Read.

Expected:
- During each transition, the tick sprite swaps in-place.
- Bubble width re-snaps if the tick sprite width changed.
- No layout flicker that exceeds one frame.

- [ ] **Step 4: Visual verification — failed-state retry button**

Force a failure by sending while offline (or another reliable way per project — `OutboxStore` retry path).

Expected:
- Failed tick appears inline.
- Tapping the time area triggers retry (unchanged behavior — `UpdateRetryButton` still owns this).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
fix(chat): recompute inline time reservation on status change

Tick sprites may have differing widths (pending vs sent vs read vs failed).
SetDeliveryStatus now re-applies the <space> reservation and requests a
layout rebuild so the bubble width re-snaps when the tick width changes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Visual calibration pass

The mappings in Task 1, Step 5 translate the previous TMP margins to anchored positions one-for-one. Because the semantics differ slightly (TMP margin shrinks the internal text rect, while `anchoredPosition` offsets the whole component from the anchor), the time may sit a few pixels off from where it sat before in some layouts. This task is the tuning pass.

**Files:**
- Modify (likely): `Assets/Scripts/UI/MessageItemView.cs:480-687` (ApplyDynamicLayout — the `PositionFloatingTime` calls only)

- [ ] **Step 1: Inventory of layouts to verify**

In Unity Play mode, walk through and screenshot each of the following with the iPhone 12 viewport (1080×2400):

1. Text-only outgoing — short, two-line short last, two-line full last.
2. Text-only incoming — same three cases.
3. Image + short caption (incoming, outgoing).
4. Image + multi-line caption (incoming, outgoing).
5. Image without caption (incoming, outgoing).
6. Video + caption.
7. Audio message.
8. Voice message.
9. Document with no caption.
10. Document with caption.
11. Link preview with text (long URL).
12. Failed-state outgoing.
13. Jumbo single-emoji message.
14. Group sender-name visible (if reachable in current data).

- [ ] **Step 2: Compare each layout against `main` (the branch before this work)**

For any layout where time is visibly off (more than ~2px from where it was on `main`), adjust the corresponding `PositionFloatingTime(rightInset, bottomInset)` call in `ApplyDynamicLayout`.

Tuning rule of thumb:
- Time too far left → reduce `rightInset` by 2–4px.
- Time too far right (clipping outside bubble) → increase `rightInset` by 2–4px.
- Time too low (clipping the bubble bottom) → increase `bottomInset`.
- Time too high (floating over text) → reduce `bottomInset`.

- [ ] **Step 3: Specific layouts to double-check**

- **Chat / no link card / not jumbo** (line 583–589 region): old code used `Vector4(0, 0, -4, -8)` — these negatives intentionally pushed time outside the messageText's internal padding. With absolute positioning we may want positive values instead (e.g. `PositionFloatingTime(12f, 8f)`). Try this if the default mapping looks wrong.
- **Image without caption** (line 669 region): the dark-card overlay (`timeBackground`) and time both anchor to bottom-right. Confirm they overlap correctly and the card visually frames the time as it did before.
- **Jumbo emoji hideBubble** (line 562–569 region): with `hideBubble = true` the bubble background is invisible but time still renders. Confirm time still appears at the expected white-on-transparent location.

- [ ] **Step 4: Commit any tuning changes**

If no tuning was needed, skip the commit. Otherwise:

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "$(cat <<'EOF'
fix(chat): tune inline timeText offsets to match prior pixel placement

Calibration after switching timeText from TMP margin to absolute
anchoredPosition. Adjusts per-layout right/bottom insets where the
new positioning drifted from the previous visual.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Final end-to-end pass

- [ ] **Step 1: Re-run the full testing-plan from the spec**

Open Unity, run the scene, walk through all 9 cases from the spec's testing plan section. Note any regression.

- [ ] **Step 2: Inspect the console**

Confirm: no new warnings or errors related to `MessageItemView`, `TextMeshPro`, or `LayoutRebuilder` during the playthrough.

- [ ] **Step 3: Review the diff vs `main`**

Run: `git log --oneline main..HEAD`

Expected: 3–5 commits, all scoped to `Assets/Scripts/UI/MessageItemView.cs` plus the design+plan docs in `docs/superpowers/`.

Run: `git diff main..HEAD -- Assets/Scripts/UI/MessageItemView.cs | wc -l`

Expected: typically under 200 lines of diff.

- [ ] **Step 4: Done**

The feature is complete when:
- Short text bubbles are visibly more compact (no extra row for time).
- Multi-line bubbles place time on the last line when there is room.
- Multi-line bubbles wrap time to a new line when the last line is full.
- All other bubble types (audio, voice, document-no-caption, image-no-caption, jumbo emoji) look identical to before.
- No console errors.

---

## Self-Review

**Spec coverage check:**
- Float Time object — Task 1 ✓
- New helpers (MeasureTimeWidth, StripTrailingReservation, ApplyInlineTimeReservation) — Task 2 ✓
- Wiring into Bind() flow — Task 3 (via `AdjustTextBubbleSize`, which is called from all three Bind paths at lines 446, 1545, 2457) ✓
- Status-change re-application — Task 4 ✓
- Excluded layouts respected — handled by early-exit in `ApplyInlineTimeReservation` (active+non-empty+not-jumbo) and by leaving non-text layouts in `ApplyDynamicLayout` untouched ✓
- All edge cases from spec — covered in Task 3 (steps 3–9) and Task 5 (step 1 inventory) ✓
- Testing plan — Tasks 3, 4, 6 walk through every case from spec section 8 ✓

**Placeholder scan:** No TODO/TBD/"similar to" references in this plan. All code blocks are complete.

**Type consistency:** Method names `ConfigureFloatingTime`, `PositionFloatingTime`, `MeasureTimeWidth`, `StripTrailingReservation`, `ApplyInlineTimeReservation` are used consistently throughout. Field names `floatingTimeConfigured`, `lastFloatingTimePosition` used consistently. Vector2/Vector4/RectOffset semantics explicitly noted where they diverge (Task 1 Step 5).

**Risk noted:** The transformation rule `Vector4(0, 0, X, Y)` → `PositionFloatingTime(X, -Y)` is a pragmatic translation; pixel-perfect equivalence isn't guaranteed because TMP-margin and anchored-position have different semantics. Task 5 is explicitly the calibration pass for this.
