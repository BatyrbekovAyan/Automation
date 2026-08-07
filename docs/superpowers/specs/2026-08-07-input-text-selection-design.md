# Input Text Selection — iOS-Style Select / Cut / Copy / Paste

**Date:** 2026-08-07
**Status:** Approved design, pre-implementation
**Owner ask:** "After typing text in any of the input text fields I can't select, cut, copy, paste it. I want it easy, intuitive and similar to how iPhone handles it. Especially selecting."

## Problem

Every input field in the app hides the native mobile input strip (`m_HideMobileInput: 1` — 13 fields in `Main.unity`, all of `BotSettings.prefab`, set in code at `ProfileSubPagesBuilder.cs:948`). Text is 100% Unity-rendered, so iOS never shows its native editing UI (selection pins, loupe, edit menu). Unity's `TMP_InputField` ships no touch-grade replacement: on device the user gets tap-to-place-caret, an undiscoverable 0.5 s double-tap word select, and nothing else — no handles, no menu, no clipboard access. Chat *bubbles* already have long-press copy (`ReactionBarController.cs:255`); input fields have nothing.

## Verified enablers (from `com.unity.ugui@bb329a87fcdc` source)

1. `TMP_InputField` **syncs selection into the hidden native keyboard buffer** when `m_HideMobileInput && m_SoftKeyboard.canSetSelection` — `UpdateKeyboardStringPosition()` (line 1554) with an explicit iOS/tvOS path, and a per-frame push path for Android. It also reads selection back (`UpdateStringPositionFromKeyboard`).
2. `UpdateKeyboardStringPosition()` is called from only **two pointer-path call sites** (lines 1922, 2110). Public selection setters (`selectionStringAnchorPosition` / `selectionStringFocusPosition`) do **not** push to the native buffer on iOS → programmatic selection needs one explicit sync seam (see `KeyboardSelectionSync`).
3. `DeferredDismissInputField` overrides only focus paths (`OnSelect` / `OnDeselect` / `OnPointerClick`). TMP's selection machinery (`OnPointerDown` / `OnDrag` / `OnUpdateSelected`) is untouched — an additive layer does not fight the single-focus invariant or the parked-keyboard watchdog.
4. `GUIUtility.systemCopyBuffer` is already proven on this project (bubble copy).

## Goals

- iOS-parity selection and clipboard editing in **every** `TMP_InputField`, including dynamically created ones (product/service edit sheets, support sheet), on iOS **and** Android, dark + light themes.
- Zero scene/prefab churn. Zero changes to the keyboard invariants (single-focus, parked-keyboard adoption, uniform keyboard config, "never write `.text` into a *different* field while one is focused").

## Non-goals (v1)

- Magnifier loupe (possible later; noted as v2 candidate).
- Selecting text inside chat bubbles (bubble copy already exists).
- Undo/redo.
- Android-styled toolbar: both platforms get the same iOS-style menu (deliberate).
- Spacebar-trackpad caret movement is not built — but the spike checks whether TMP's read-back sync already provides it for free.

## Behavior spec

### Gestures

| Gesture | Result |
|---|---|
| Tap | Place caret (existing TMP behavior, unchanged). |
| Long-press ~450 ms, finger within slop (~10 dp) | Select the word under the finger; if over whitespace/empty text, place caret. If the field was unfocused, focus it first through the field's normal activation path (never a bespoke path — `DeferredDismissInputField.OnSelect` must run), and apply + sync the word selection only **after focus materializes** — activation is a promise in this project (the keyboard opens a LateUpdate later), so the router defers the selection until the field reports focus and a live keyboard instance, else TMP's activation path can clobber it. Continued finger drag after commit extends the selection character-wise to the finger. Pins + menu appear on release. |
| Double-tap (< 300 ms between taps, within slop) | Select word, pins + menu on release. (TMP's internal 0.5 s double-click may set the same selection first; ours supersedes it — harmless.) |
| Finger moves past slop before the long-press timer | Gesture cancels; scrolling / TMP drag behave exactly as today. |
| TMP-originated selections (e.g. touch drag-select in the composer) | Router observes `onTextSelection` / selection state; when a non-empty selection stabilizes on pointer-up, pins + menu appear regardless of gesture origin. |
| Tap outside the selection / typing / field defocus / keyboard hides | Selection UI dismisses. |

### Selection pins

- Two teardrop handles at selection start/end: start pin stem-up, end pin stem-down (iOS convention). Head ≈ 48 reference units, touch target ≥ 132 reference units (44 pt).
- Dragging a pin adjusts that edge character-precise; pins swap roles when dragged across each other (iOS behavior). Minimum selection of 1 char while pins are up.
- Menu hides during a pin drag, reappears on release.
- Pin positions recompute every frame while visible (fields move with keyboard/scroll) from `TMP_TextInfo` character geometry.
- Auto-scroll: while dragging a pin inside a scrollable text area (`ScrollableTextArea` / `ScrollableInputField`) with the finger in an ~80-reference-unit edge band, scroll the inner content; clamped.

### Edit menu

- Floating pill anchored above the selection bounds midpoint (below if clipped by screen top or keyboard). Items: «Вырезать · Копировать · Вставить · Выделить всё», hairline separators, themed surface/ink, sizes per the `unity-ui-builder` measured scale.
- Visibility rules (`MenuPolicy`): Вырезать/Копировать require a non-empty selection; Вставить requires non-empty system clipboard; Выделить всё requires text present and not already all-selected. Caret-only invocation (long-press on empty area) shows Вставить / Выделить всё only.
- Dismiss on: action tap, outside tap, selection cleared, defocus, keyboard hide.
- Clipboard is the **system** clipboard (`GUIUtility.systemCopyBuffer`) — cross-app paste in and copy out.

### Theming

- Pins + selection highlight use the accent role. Highlight = accent at ~25% alpha, applied at runtime to each field's `selectionColor` on focus (no scene churn), re-applied on `Theme.Changed`.

## Architecture

All new code, no modifications to existing components. One runtime singleton, one overlay, four pure-C# seams.

| Unit | Kind | Responsibility |
|---|---|---|
| `TextSelectionRouter` | Lazy always-active singleton (UploadCenter pattern: `Instance` creates, `Existing` never does; play-mode only) | Watches touches; on touch-begin raycasts **all** hits and resolves the first hit whose hierarchy contains a `TMP_InputField` (robust to `ClickPassthrough` strips / shields sitting on top). Runs the long-press / double-tap state machine. Owns showing/hiding overlay + menu. Applies themed `selectionColor`. Only *observes* touches — never consumes events. |
| `SelectionOverlay` + `SelectionHandleView` | Runtime-created Canvas, sorted between `ScreenContainer` and `LoadingPanel` | Draws + positions the two pins from `TMP_TextInfo` geometry; handles receive their own drag events (no ScrollRect conflict); auto-scroll. |
| `SelectionMenuView` | Runtime-built UI under the same overlay | The pill menu; renders actions per `MenuPolicy`. |
| `WordBoundary` | Pure C# | Word range at a string index: letters/digits (Cyrillic via `char.IsLetterOrDigit`), punctuation selects the punctuation run, whitespace → caret. Surrogate-pair / ZWJ / FE0F clamps — never split an emoji. |
| `SelectionActions` | Pure C# | `(text, selAnchor, selFocus, clipboard)` → `(newText, newCaretStringIndex)` for Cut / Paste / SelectAll. Enforces `characterLimit` (silent truncation, iOS-style). All indices are string indices, surrogate-safe. |
| `MenuPolicy` | Pure C# | Action-visibility matrix (see Behavior spec). |
| `KeyboardSelectionSync` | Thin seam | After any programmatic selection/caret change while the field is focused with the keyboard open: push `RangeInt(min, len)` into the native buffer (guarded by `canSetSelection`). Prefer a public accessor to the field's `TouchScreenKeyboard`; if unavailable, contained reflection to `UpdateKeyboardStringPosition`, pinned by an EditMode test that fails if the method vanishes on a Unity upgrade. iOS needs the explicit push (verified); Android's per-frame path already covers it (calling anyway is idempotent). |

### Mutation path (cut / paste)

Compute via `SelectionActions`, then: `field.text = newText` → `field.stringPosition = newCaret` → `KeyboardSelectionSync.Push`. Writing the **focused field's own** `.text` is the safe write-through path (TMP syncs `m_SoftKeyboard.text`); the project invariant forbids writing a *different* field while one is focused, which this never does. `onValueChanged` fires → BotSettings dirty-policy sees the edit exactly as if typed → Save lights correctly.

## Risk gate: device spike (GO/NO-GO before any UI is built)

Throwaway scene + iPhone build, ~half a day. Three field archetypes: plain multi-line, `ScrollableTextArea` clone, composer clone with emoji text. On-screen log for results.

- (a) `TouchScreenKeyboard.canSetSelection` is true with hidden input on iOS.
- (b) Programmatic selection + `KeyboardSelectionSync` push → the next keystroke **replaces** the selection (native buffer honored).
- (c) Paste write-through stays in sync — typing immediately after a paste lands at the right position, no shared-buffer echo.
- (d) Composer emoji (surrogate pairs) keep string indices honest through select/cut/paste.
- Bonus check: does spacebar-trackpad already move our caret via TMP's read-back sync?

If (a) or (b) fails: fallback design is `onValueChanged` diff-correction (detect the native insert, re-apply it over the Unity-side selection). More code, decided only if the spike demands it — not a dead end.

## Testing

- **EditMode** (project seam pattern, injectable clock/raycast): `WordBoundaryTests` (RU/EN/digits/punctuation/emoji/ZWJ), `SelectionActionsTests` (cut/paste/limit/surrogate clamps), `MenuPolicyTests` (full matrix), router state-machine tests (tap vs long-press vs slop-cancel vs double-tap), `KeyboardSelectionSync` reflection-target pin.
- **Device UAT** checklist per field type — composer, `EditableField`, `ScrollableTextArea`, sheet fields — × iOS and Android: select/adjust/cut/copy/paste/cross-app paste, typing-over-selection, keyboard hide/show, theme flip.

## Rollout

1. **Phase A — spike** (half day): the four GO/NO-GO checks above, on device.
2. **Phase B — build**: router + overlay + menu + seams + EditMode suites; ships behind all fields at once.
3. **Phase C — device passes + polish**: both platforms, auto-scroll tuning, menu edge cases.

No server/n8n involvement anywhere.

## Spike verdict — 2026-08-07, owner's iPhone (development build)

**GO.** All four checks passed on device:

- **A: PASS** — `canSetSelection=True`, `canGetSelection=True` with hidden input.
- **B: PASS** — after programmatic word selection + `KeyboardSelectionSync.Push`, the next typed character **replaced** the selection (`alpha ZZy gamma`-style result observed).
- **C: PASS** — after a full-text write + `stringPosition` + push (the paste path), the next typed character landed exactly at the parked caret.
- **D: PASS** — emoji surrogate-pair string indices stayed honest through select/substring (len=4 for 😂👍).
- **Bonus:** spacebar-trackpad DOES drive our caret through TMP's read-back sync, but with end-of-gesture granularity — the Unity-side caret updates when the finger is released, not live during the slide. Acceptable; no v1 work planned on it.

Fallback design (onValueChanged diff-correction) is NOT needed. Task 9 proceeds on the primary design.
