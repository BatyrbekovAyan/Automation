# Bot Settings — Business & Prompt Tab Edits

**Date:** 2026-04-21
**Scope:** `Assets/Scripts/Main/BotSettings/`, `Assets/Scenes/Main.unity` (BotSettings prefab)

## Motivation

The Business and Prompt tabs in Bot Settings each render an `EditableTextArea` (multiline
card) underneath a `SectionHeader`. Two problems:

1. The card duplicates its own title via the inherited `labelText` field — redundant with
   `SectionHeader` above it.
2. The white text area is a plain `TMP_InputField` with no touch-drag scrolling. Long
   business descriptions and AI prompts overflow the visible region and can only be
   navigated by moving the caret, which is poor UX on mobile.

## Goals

- Remove the redundant inner Label from `BusinessField` and `PromptField`.
- Keep the card at its current fixed height and add touch-drag scrolling inside it when
  typed text exceeds the visible area.
- Match the existing visual style of the Business/Prompt cards; no other tabs change.

## Non-Goals

- No visible scrollbar — the drag gesture is the scroll affordance.
- No scroll-to-caret during arrow-key or tap-to-reposition; only during typing.
- No changes to single-line `EditableField` usages (BotName, WhatsappNumber, etc.).
- No changes to Product/Service cards.

## Design

### 1. Remove Inner Label

**No code change.** In the `BotSettings` prefab, delete the `Label` child GameObject inside
`BusinessField` and `PromptField`. `EditableField.labelText` is a `[SerializeField]`
with null-checks on both `get` and `set` (see `EditableField.cs:36–39`), so leaving it
unassigned is safe. The field remains on the base class for other single-line usages.

### 2. Fixed-Height Card with Touch-Drag Scrolling

#### Reuse of Existing Hierarchy

`TMP_InputField` already exposes `textViewport` (masked RectTransform) and `textComponent`
(the TMP text whose height grows with content). The design reuses both — no hierarchy
rebuild.

#### Prefab Changes

Apply to **both** `BusinessField` and `PromptField`:

- **Card root height:** unchanged — preserve the current prefab height exactly.
- **On the `TMP_InputField` GameObject, add `ScrollRect`:**
  - `viewport` → existing `textViewport` child.
  - `content` → existing `textComponent` RectTransform.
  - `horizontal = false`, `vertical = true`.
  - `scrollbar = none` (hidden).
  - `movementType = Elastic`, `elasticity = 0.1` (Unity default).
  - `inertia = true`, `decelerationRate = 0.135` (Unity default).
- **Ensure `textViewport` has `Mask` or `RectMask2D`** (TMP_InputField ships with this by
  default; verify).
- **`content` RectTransform:**
  - Anchors: top-stretch (`(0, 1)`–`(1, 1)`).
  - Pivot: `(0.5, 1)`.
  - Initial `sizeDelta.y` equal to viewport height; runtime script resizes.

#### New Script: `ScrollableTextArea.cs`

Location: `Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs`
Namespace: `Automation.BotSettingsUI`

Responsibilities (single-responsibility: content sizing + scroll sync):

```csharp
namespace Automation.BotSettingsUI
{
    [RequireComponent(typeof(EditableTextArea))]
    public class ScrollableTextArea : MonoBehaviour
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform content;
        [SerializeField] private float bottomPadding = 8f;

        private RectTransform viewport;

        private void Awake()
        {
            viewport = scrollRect.viewport;
            inputField.onValueChanged.AddListener(OnTextChanged);
            ResizeContent(inputField.text);
        }

        private void OnDestroy()
        {
            if (inputField != null)
                inputField.onValueChanged.RemoveListener(OnTextChanged);
        }

        private void OnTextChanged(string text)
        {
            float previous = content.sizeDelta.y;
            ResizeContent(text);
            if (content.sizeDelta.y > previous)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        private void ResizeContent(string text)
        {
            float width = viewport.rect.width;
            float preferred =
                inputField.textComponent.GetPreferredValues(text, width, 0f).y;
            float target = Mathf.Max(viewport.rect.height, preferred + bottomPadding);
            content.sizeDelta = new Vector2(content.sizeDelta.x, target);
        }
    }
}
```

Key points:
- Uses the same `GetPreferredValues` pattern as `ExpandableInput.cs:106–113` — consistent
  with how the project already measures TMP text.
- Auto-scrolls to the bottom (`verticalNormalizedPosition = 0f`) only when content grows,
  mirroring `ExpandableInput.cs:63–65`. Keeps the caret visible while typing without
  fighting the user's manual scroll position during editing.
- Does **not** override drag gestures. `ScrollRect` handles drag/inertia natively.

#### Why a separate component (not folded into `EditableTextArea`)

- `EditableTextArea` owns focus + commit semantics (hide header and tab bar on focus).
- `ScrollableTextArea` owns content sizing + scroll sync.
- Each stays under 50 lines; both can coexist on the same GameObject.
- `ScrollableTextArea` is reusable for any future multiline scrollable input without
  pulling in focus-scrim coupling.

### 3. Wiring in `BotSettings.cs`

**No changes.** `BusinessField` and `PromptField` remain typed as `EditableTextArea`. The
new `ScrollableTextArea` component is attached in the prefab and self-initializes via its
own `Awake`.

## Data Flow

```
User types → TMP_InputField.onValueChanged
           → ScrollableTextArea.OnTextChanged
             ├── Measure preferred height (GetPreferredValues)
             ├── Resize content RectTransform
             └── If grew → scroll to bottom
User drags on card → ScrollRect handles natively → content scrolls within viewport
```

## Testing Plan

Manual in Unity Editor + Android build at 1080×2400:

1. **Label removal**
   - Open Bot Settings → Business tab. Confirm only the `SectionHeader` title shows above
     the white card; no inner Label is visible.
   - Repeat for Prompt tab.
2. **Card sizing**
   - Card height matches the pre-change height in both tabs.
3. **Typing past the visible area**
   - Type a multi-paragraph description. Confirm the caret stays in view and the text
     scrolls up inside the card without the card itself growing.
4. **Touch-drag scroll**
   - With a card full of text, swipe up/down on the card. Confirm text scrolls with
     inertia and elasticity; caret does not jump.
5. **Focus / commit behavior unchanged**
   - Tap into the card → header and tab bar hide (existing `EditableTextArea` behavior).
   - Tap outside → header and tab bar restore; `OnCommitted` fires if text changed.
6. **Empty state**
   - Clear all text. Content shrinks back; scroll position resets to top.

## Risks & Mitigations

- **ScrollRect vs TMP_InputField drag conflict.** Native Unity pattern — `ScrollRect.OnDrag`
  and `TMP_InputField`'s caret selection drag can compete. Mitigation: `TMP_InputField`
  only processes drag when an IPointerDown hits its caret; otherwise ScrollRect wins.
  Verify in testing; if conflict surfaces, add a `ScrollClickBlocker`-style shim
  (pattern already exists in the codebase at `Assets/Scripts/Chat/ScrollClickBlocker.cs`).
- **Content pivot misconfiguration.** If the content pivot is not `(0.5, 1)`, resizing
  appears to shift text. Callout in prefab changes; verified in testing step 3.
- **Prefab drift.** Changes are applied twice (Business and Prompt). Both must be kept in
  sync; a shared prefab variant could be introduced later if maintenance becomes painful.

## Files Changed

- **New:** `Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs`
- **Modified (prefab, via Unity Editor):** `BotSettings` prefab — `BusinessField` and
  `PromptField` GameObjects (Label removed, ScrollRect added, component attached, refs
  wired).
- **Unchanged:** `EditableField.cs`, `EditableTextArea.cs`, `BotSettings.cs`.
