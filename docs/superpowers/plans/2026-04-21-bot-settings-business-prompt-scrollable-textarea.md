# Bot Settings — Business & Prompt Scrollable Textarea Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the redundant inner Label from `BusinessField` and `PromptField` in Bot Settings, and give both cards a fixed-height scroll-by-touch multiline input.

**Architecture:** A new runtime component `ScrollableTextArea` sits alongside `EditableTextArea` and drives a `ScrollRect` wrapped around the existing `TMP_InputField` `textViewport` and `textComponent`. An editor builder tool applies the prefab changes (delete Label, add ScrollRect, attach and wire the new component) idempotently — matching the established `BotSettingsConfirmChangePopupBuilder` pattern.

**Tech Stack:** Unity 6 (6000.3.9f1), C#, TMPro, Unity UI (`ScrollRect`, `TMP_InputField`), `UnityEditor.PrefabUtility`.

**Spec:** `docs/superpowers/specs/2026-04-21-bot-settings-business-prompt-scrollable-textarea-design.md`

---

## File Structure

- **Create:** `Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs` — runtime MonoBehaviour that measures text height and drives the ScrollRect content size.
- **Create:** `Assets/Editor/BotSettingsScrollableTextAreaBuilder.cs` — editor tool under `Tools/BotSettings/Build Scrollable Business+Prompt` that applies the prefab surgery.
- **Modified (by the builder tool at edit time):** `Assets/Prefabs/BotSettings.prefab` — `BusinessField` and `PromptField` GameObjects (Label removed, ScrollRect added on `TMP_InputField`, `ScrollableTextArea` attached with wired refs).
- **Unchanged:** `EditableField.cs`, `EditableTextArea.cs`, `BotSettings.cs`, `Main.unity`.

---

## Testing Strategy

Unity's Test Framework is not wired up in this project. The established workflow (see CLAUDE.local.md and existing editor builders) is: write code → verify it compiles → apply prefab via editor tool → exercise in Play mode at 1080×2400. Each task below includes a concrete verification step that matches this reality.

---

## Task 1: Create `ScrollableTextArea` runtime script

**Files:**
- Create: `Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs`

- [ ] **Step 1: State expected behavior**

The component must:
1. On `Awake`, subscribe to `TMP_InputField.onValueChanged` and do an initial `ResizeContent` so the scroll content matches existing text.
2. On text change: compute `preferredH = inputField.textComponent.GetPreferredValues(text, viewport.rect.width, 0f).y`, set `content.sizeDelta.y = max(viewport.rect.height, preferredH + bottomPadding)`, and if content grew, set `scrollRect.verticalNormalizedPosition = 0f` so the caret stays visible.
3. On `OnDestroy`, unsubscribe.
4. Fail loudly via `Debug.LogError` if any serialized ref is unassigned (bad prefab wiring).

- [ ] **Step 2: Create the file with the exact implementation**

Write to `Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Fixed-height multiline input with touch-drag scrolling. Attach
    /// alongside EditableTextArea on a card whose TMP_InputField hosts a
    /// ScrollRect over the TMP textViewport / textComponent. Resizes the
    /// scroll content to measured text height and auto-scrolls to the caret
    /// as text grows. Mirrors the GetPreferredValues pattern in
    /// Chat/ExpandableInput.cs.
    /// </summary>
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
            if (scrollRect == null || inputField == null || content == null)
            {
                Debug.LogError($"[ScrollableTextArea] Missing references on {name}.");
                return;
            }

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
            var previous = content.sizeDelta.y;
            ResizeContent(text);
            if (content.sizeDelta.y > previous)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        private void ResizeContent(string text)
        {
            var width = viewport.rect.width;
            var preferred = inputField.textComponent.GetPreferredValues(text, width, 0f).y;
            var target = Mathf.Max(viewport.rect.height, preferred + bottomPadding);
            content.sizeDelta = new Vector2(content.sizeDelta.x, target);
        }
    }
}
```

- [ ] **Step 3: Verify the file was created and parses as valid C#**

Run:

```bash
test -f Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs && echo "EXISTS"
grep -c "class ScrollableTextArea" Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs
```

Expected output: `EXISTS` then `1`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/ScrollableTextArea.cs
git commit -m "$(cat <<'EOF'
feat: add ScrollableTextArea for fixed-height scrollable multiline input

Companion to EditableTextArea: drives a ScrollRect wrapped around the
TMP_InputField textViewport/textComponent, resizing scroll content to
measured text height and auto-scrolling to the caret as text grows.
Reuses the GetPreferredValues measurement pattern from
Chat/ExpandableInput.cs.

Refs: docs/superpowers/specs/2026-04-21-bot-settings-business-prompt-scrollable-textarea-design.md

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Create the editor builder tool

**Files:**
- Create: `Assets/Editor/BotSettingsScrollableTextAreaBuilder.cs`

- [ ] **Step 1: State expected behavior**

The builder must:
1. Load `Assets/Prefabs/BotSettings.prefab` via `PrefabUtility.LoadPrefabContents`.
2. For each of `BusinessField` and `PromptField` (public `EditableTextArea` refs on `BotSettings`):
   - If the field already carries a `ScrollableTextArea`, skip it (idempotent).
   - Destroy the inner Label GameObject referenced by `EditableField.labelText` and clear that serialized ref.
   - Read the field's `TMP_InputField` (from `EditableField.input`) and its `textViewport` + `textComponent`.
   - Add a `ScrollRect` to the `TMP_InputField` GameObject with `viewport = textViewport`, `content = textComponent.rectTransform`, `horizontal = false`, `vertical = true`, `movementType = Elastic`, `elasticity = 0.1`, `inertia = true`, `decelerationRate = 0.135`, `scrollSensitivity = 1`.
   - Configure `textComponent.rectTransform`: `anchorMin = (0, 1)`, `anchorMax = (1, 1)`, `pivot = (0.5, 1)`.
   - `AddComponent<ScrollableTextArea>()` on the field root and wire its private `scrollRect`, `inputField`, `content` serialized fields.
3. Save the prefab via `PrefabUtility.SaveAsPrefabAsset` and unload.
4. Log a summary: either `Prefab updated` or `Nothing to do — already converted`.
5. Wrap all UnityEditor references in `#if UNITY_EDITOR` / `#endif` (project convention — see `Assets/.claude/rules/editor-scripts.md`).

- [ ] **Step 2: Create the file with the exact implementation**

Write to `Assets/Editor/BotSettingsScrollableTextAreaBuilder.cs`:

```csharp
#if UNITY_EDITOR
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor maintenance for BotSettings.prefab.
///
/// For each of BusinessField and PromptField:
///   1. Deletes the inner Label GameObject (redundant with SectionHeader).
///   2. Wraps the TMP_InputField's textViewport + textComponent in a
///      ScrollRect so the fixed-height card supports touch-drag scrolling.
///   3. Attaches ScrollableTextArea with wired references for runtime
///      content-size sync.
///
/// Skips anything already converted; safe to re-run.
/// </summary>
public static class BotSettingsScrollableTextAreaBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    [MenuItem("Tools/BotSettings/Build Scrollable Business+Prompt")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[BotSettings] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var settings = prefabRoot.GetComponent<BotSettings>();
            if (settings == null)
            {
                Debug.LogError("[BotSettings] BotSettings component not found on prefab root.");
                return;
            }

            var modified = false;
            modified |= ConvertField(settings.BusinessField, nameof(settings.BusinessField));
            modified |= ConvertField(settings.PromptField, nameof(settings.PromptField));

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"[BotSettings] Prefab updated at {PrefabPath}");
            }
            else
            {
                Debug.Log("[BotSettings] Nothing to do — already converted.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool ConvertField(EditableTextArea field, string fieldName)
    {
        if (field == null)
        {
            Debug.LogWarning($"[BotSettings] {fieldName} not wired on BotSettings prefab.");
            return false;
        }

        if (field.GetComponent<ScrollableTextArea>() != null)
            return false; // Already converted; idempotent skip.

        var fieldSo = new SerializedObject(field);
        var labelProp = fieldSo.FindProperty("labelText");
        var inputProp = fieldSo.FindProperty("input");
        if (inputProp == null || inputProp.objectReferenceValue == null)
        {
            Debug.LogError($"[BotSettings] {fieldName}.input not wired; aborting.");
            return false;
        }

        var input = (TMP_InputField)inputProp.objectReferenceValue;
        var viewport = input.textViewport;
        var textComponent = input.textComponent;
        if (viewport == null || textComponent == null)
        {
            Debug.LogError($"[BotSettings] {fieldName} TMP_InputField missing textViewport/textComponent.");
            return false;
        }

        // 1. Delete the inner Label.
        if (labelProp != null && labelProp.objectReferenceValue is TextMeshProUGUI label)
        {
            Object.DestroyImmediate(label.gameObject);
            labelProp.objectReferenceValue = null;
            fieldSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // 2. Configure content RT (top-stretch, pivot top).
        var contentRt = textComponent.rectTransform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);

        // 3. Add ScrollRect on the TMP_InputField GameObject.
        var inputGo = input.gameObject;
        var scroll = inputGo.GetComponent<ScrollRect>();
        if (scroll == null)
            scroll = inputGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.1f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 1f;
        scroll.viewport = viewport;
        scroll.content = contentRt;

        // 4. Attach ScrollableTextArea on the field root and wire refs.
        var sta = field.gameObject.AddComponent<ScrollableTextArea>();
        var staSo = new SerializedObject(sta);
        staSo.FindProperty("scrollRect").objectReferenceValue = scroll;
        staSo.FindProperty("inputField").objectReferenceValue = input;
        staSo.FindProperty("content").objectReferenceValue = contentRt;
        staSo.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[BotSettings] Converted {fieldName} to ScrollableTextArea.");
        return true;
    }
}
#endif
```

- [ ] **Step 3: Verify the file was created and parses as valid C#**

Run:

```bash
test -f Assets/Editor/BotSettingsScrollableTextAreaBuilder.cs && echo "EXISTS"
grep -c "MenuItem(\"Tools/BotSettings/Build Scrollable Business+Prompt\")" Assets/Editor/BotSettingsScrollableTextAreaBuilder.cs
```

Expected output: `EXISTS` then `1`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/BotSettingsScrollableTextAreaBuilder.cs
git commit -m "$(cat <<'EOF'
chore(editor): add BotSettingsScrollableTextAreaBuilder

Idempotent Tools/BotSettings menu entry that, for each of BusinessField
and PromptField: deletes the inner Label, adds a ScrollRect around the
TMP_InputField's textViewport+textComponent, and attaches
ScrollableTextArea with wired references. Mirrors the
BotSettingsConfirmChangePopupBuilder pattern.

Refs: docs/superpowers/specs/2026-04-21-bot-settings-business-prompt-scrollable-textarea-design.md

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Run the builder and commit the updated prefab

**Files:**
- Modify (by Unity Editor action): `Assets/Prefabs/BotSettings.prefab`

- [ ] **Step 1: Open Unity Editor**

Open `Automation.sln` in Unity Hub (version 6000.3.9f1). Wait for Unity to finish compiling.

- [ ] **Step 2: Confirm there are no compile errors**

In the Unity Console, verify zero red errors. If any, stop and fix compile errors before continuing — the menu won't appear otherwise.

- [ ] **Step 3: Run the builder**

In Unity's top menu, click: **Tools → BotSettings → Build Scrollable Business+Prompt**.

- [ ] **Step 4: Verify console log**

Expected Console output:
```
[BotSettings] Converted BusinessField to ScrollableTextArea.
[BotSettings] Converted PromptField to ScrollableTextArea.
[BotSettings] Prefab updated at Assets/Prefabs/BotSettings.prefab
```

If instead you see `Nothing to do — already converted`, the prefab was already modified in a previous run — that's fine, proceed.

If you see `not wired on BotSettings prefab` or `input not wired`, open `BotSettings.prefab` in the editor, confirm `BusinessField` / `PromptField` serialized refs point at `EditableTextArea` GameObjects whose `input` field is assigned, then re-run.

- [ ] **Step 5: Spot-check the prefab in the Unity inspector**

Open `Assets/Prefabs/BotSettings.prefab`. In the hierarchy:
- Expand `BusinessField`. Verify:
  - No child GameObject named `Label` remains.
  - `ScrollableTextArea` component is on the root with `scrollRect`, `inputField`, `content` all populated.
  - The TMP_InputField child has a `ScrollRect` component.
- Expand `PromptField`. Verify the same structure.

- [ ] **Step 6: Commit the prefab change**

Close the prefab and return to the terminal. Run:

```bash
git status Assets/Prefabs/BotSettings.prefab
git diff --stat Assets/Prefabs/BotSettings.prefab
```

Expected: the prefab file appears as modified.

Then:

```bash
git add Assets/Prefabs/BotSettings.prefab
git commit -m "$(cat <<'EOF'
chore(prefab): apply scrollable textarea to Business and Prompt fields

Ran Tools/BotSettings/Build Scrollable Business+Prompt: removed inner
Label from BusinessField and PromptField, added ScrollRect over the
TMP_InputField text viewport, and attached ScrollableTextArea with
wired refs.

Refs: docs/superpowers/specs/2026-04-21-bot-settings-business-prompt-scrollable-textarea-design.md

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Manual functional verification in Play mode

**Files:**
- None (verification only).

This task validates every acceptance criterion from the spec's Testing Plan. Record results in the commit message of any follow-up fix if issues are found.

- [ ] **Step 1: Enter Play mode in the Main scene**

Open `Assets/Scenes/Main.unity`. Switch Game view to 1080×2400 (per `CLAUDE.local.md`). Press Play.

- [ ] **Step 2: Navigate to Bot Settings → Business tab**

From the Bots list, open a bot whose settings you can edit. Tap the **Business** tab.

- [ ] **Step 3: Confirm no inner Label (acceptance criterion 1)**

Expected: the white card shows only the multiline input and whatever `SectionHeader` text sits above it. There is no duplicate "Label" text inside the card above the input.

- [ ] **Step 4: Confirm card height is unchanged (acceptance criterion 2)**

Compare to a pre-change screenshot or a git stash if available. Expected: the card occupies the same vertical region as before.

- [ ] **Step 5: Type past the visible area (acceptance criterion 3)**

Tap into the card. Type a long description (paste several paragraphs). Expected: caret stays in view; text scrolls **inside** the card; the card itself does **not** grow.

- [ ] **Step 6: Touch-drag to scroll (acceptance criterion 4)**

With the card full of text, swipe up and down on the card. Expected: text scrolls with inertia and a small elastic bounce at the edges; caret does not jump.

- [ ] **Step 7: Confirm focus / commit still works (acceptance criterion 5)**

- On tap into the card: the page header and the bot-settings tab bar hide (this is `EditableTextArea.OnFullScreenFocusRequested`).
- On tap outside: header and tab bar restore. If the text changed, `Manager.Instance.EnableSave()` fires — the Save button in the top-right becomes enabled.

- [ ] **Step 8: Empty state (acceptance criterion 6)**

Select all and delete. Expected: the scroll content shrinks back to the viewport height; scroll position sits at the top; no scrollbars appear.

- [ ] **Step 9: Repeat Steps 3 – 8 for the Prompt tab**

All eight checks must pass in the **Prompt** tab as well.

- [ ] **Step 10: Exit Play mode**

Press Play again to exit. If any check failed, open a new debugging cycle — do not commit until all checks pass. If all checks pass, no further commit is needed; the feature is complete on this branch.

---

## Rollback

If the prefab edit needs to be undone:

```bash
git revert <commit-hash-of-task-3>
```

The runtime script and editor tool can stay in place — without wiring in the prefab, they are inert. To fully undo the feature, also revert the commits from Tasks 1 and 2.
