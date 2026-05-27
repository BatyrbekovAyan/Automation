# Deferred Keyboard Dismiss Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `TMP_InputField` keyboard dismissal from `PointerDown` to `PointerUp` app-wide, with smooth focus-switching between input fields.

**Architecture:** Subclass `TMP_InputField` as `DeferredDismissInputField` that defers the actual `OnDeselect` dismissal until the Input System reports no finger pressed. Apply via `m_Script` swap on every existing instance in the scene and prefabs. Pair with `Navigation.Mode.None` on chat-panel buttons as defense-in-depth.

**Tech Stack:** Unity 6 (6000.3.9f1), TextMeshPro `TMP_InputField`, Unity Input System (`Pointer`, `Touchscreen`), Unity `EditorSceneManager` / `PrefabUtility` for one-time migration.

**Spec:** [docs/superpowers/specs/2026-05-27-deferred-keyboard-dismiss-design.md](docs/superpowers/specs/2026-05-27-deferred-keyboard-dismiss-design.md)

**Instance count discovered during planning:** 9 `TMP_InputField` instances in `Assets/Scenes/Main.unity`, 9 in `Assets/Prefabs/BotSettings.prefab`. No other prefabs contain TMP_InputField components.

---

## Task 1: Create `DeferredDismissInputField`

**Files:**
- Create: `Assets/Scripts/Chat/DeferredDismissInputField.cs`

This is the core subclass. Defers `OnDeselect`-triggered dismissal until the Input System reports no finger pressed. Smooth-switches to another `TMP_InputField` without dismissing.

- [ ] **Step 1: Create the file with the full implementation**

Write to `Assets/Scripts/Chat/DeferredDismissInputField.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// TMP_InputField subclass that defers keyboard dismissal from PointerDown to
/// PointerUp. The default TMP behavior calls DeactivateInputField the moment
/// EventSystem deselects the field — on iOS this fires resignFirstResponder
/// on finger-down, before the user has even released the tap, causing the
/// keyboard to slide down mid-gesture.
///
/// This subclass overrides OnDeselect to mark a pending dismiss and waits
/// for the Input System to report no finger pressed. If the new selection
/// is another TMP_InputField (focus-switch), the pending dismiss is cleared
/// and no animation runs.
///
/// Explicit programmatic dismissals (DeactivateInputField from AttachSheet,
/// EditableField.Blur, ChatSearchBar, etc.) bypass OnDeselect entirely and
/// keep their immediate-dismiss semantics.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DeferredDismissInputField : TMP_InputField
{
    private bool dismissPending;

    public override void OnDeselect(BaseEventData eventData)
    {
        dismissPending = true;
    }

    public override void OnSelect(BaseEventData eventData)
    {
        dismissPending = false;
        base.OnSelect(eventData);
    }

    protected override void OnDisable()
    {
        if (dismissPending)
        {
            dismissPending = false;
            base.OnDeselect(new BaseEventData(EventSystem.current));
        }
        base.OnDisable();
    }

    private void Update()
    {
        if (!dismissPending) return;
        if (IsPointerPressed()) return;

        dismissPending = false;

        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && sel.GetComponent<TMP_InputField>() != null) return;

        base.OnDeselect(new BaseEventData(EventSystem.current));
    }

    private static bool IsPointerPressed()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
        if (Pointer.current != null && Pointer.current.press.isPressed) return true;
        return false;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run from project root:

```bash
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath . \
  -quit -logFile /tmp/unity-compile.log
```

(If the Unity path is different on this machine, find via `mdfind -name Unity.app | head -1`.)

Expected: Exit code 0, no compile errors in `/tmp/unity-compile.log`. Grep for `error CS`:

```bash
grep "error CS" /tmp/unity-compile.log || echo "no compile errors"
```

Expected output: `no compile errors`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/DeferredDismissInputField.cs Assets/Scripts/Chat/DeferredDismissInputField.cs.meta
git commit -m "$(cat <<'EOF'
feat(chat): DeferredDismissInputField defers keyboard dismiss to PointerUp

Subclass of TMP_InputField. Overrides OnDeselect to mark a pending
dismissal instead of running it immediately, then polls the Input
System and only calls base.OnDeselect when the finger is released.
Skips the dismiss entirely when EventSystem.currentSelectedGameObject
is another TMP_InputField (smooth focus-switch).

No scene/prefab wiring changes yet — instances are still TMP_InputField.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Restore `Navigation.Mode.None` on chat-panel buttons

**Files:**
- Modify: `Assets/Scripts/Chat/MessagesBottomPanel.cs:21-38` (OnEnable)

Restores the regressed defense-in-depth fix from commit `a8fda3b`. Prevents the EventSystem from changing selection on `PointerDown` of the chat-panel buttons.

- [ ] **Step 1: Edit `MessagesBottomPanel.OnEnable`**

In `Assets/Scripts/Chat/MessagesBottomPanel.cs`, replace the existing `OnEnable` body. Find this block:

```csharp
    void OnEnable()
    {
        inputField.text = "";
        UpdateButtonState("");

        inputField.onValueChanged.AddListener(UpdateButtonState);
        attachButton.onClick.AddListener(OnAttachClicked);

        // Send button uses raw PointerDown for responsiveness.
        sendButton.onClick.RemoveAllListeners();
        EventTrigger trigger = sendButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = sendButton.gameObject.AddComponent<EventTrigger>();
        trigger.triggers.Clear();
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerDown;
        entry.callback.AddListener((data) => { OnSendClicked(); });
        trigger.triggers.Add(entry);
    }
```

Replace with:

```csharp
    void OnEnable()
    {
        inputField.text = "";
        UpdateButtonState("");

        inputField.onValueChanged.AddListener(UpdateButtonState);
        attachButton.onClick.AddListener(OnAttachClicked);

        // Prevent buttons from stealing EventSystem selection on PointerDown.
        // Pairs with DeferredDismissInputField: without Navigation.Mode.None,
        // tapping a button would deselect the focused input field on Down,
        // which then defers; with Mode.None the input field never receives
        // OnDeselect in the first place and the EventSystem state stays clean.
        SetNavigationNone(attachButton);
        SetNavigationNone(micButton);
        SetNavigationNone(sendButton);

        // Send button uses raw PointerDown for responsiveness.
        sendButton.onClick.RemoveAllListeners();
        EventTrigger trigger = sendButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = sendButton.gameObject.AddComponent<EventTrigger>();
        trigger.triggers.Clear();
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerDown;
        entry.callback.AddListener((data) => { OnSendClicked(); });
        trigger.triggers.Add(entry);
    }

    private static void SetNavigationNone(Button button)
    {
        if (button == null) return;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
    }
```

- [ ] **Step 2: Verify it compiles**

```bash
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath . \
  -quit -logFile /tmp/unity-compile.log
grep "error CS" /tmp/unity-compile.log || echo "no compile errors"
```

Expected: `no compile errors`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/MessagesBottomPanel.cs
git commit -m "$(cat <<'EOF'
fix(chat): restore Navigation.Mode.None on attach/mic/send buttons

Defense-in-depth pair with DeferredDismissInputField: prevents the
EventSystem from changing selection when these buttons receive
PointerDown, so the focused input field never receives OnDeselect
in the first place.

Restores the regression from a8fda3b that was lost in the working
copy. Extends from + only to + / mic / send.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Write the `InputFieldMigrator` editor utility

**Files:**
- Create: `Assets/Editor/InputFieldMigrator.cs`

One-shot tool that swaps every `TMP_InputField` instance's `m_Script` reference to `DeferredDismissInputField`. Idempotent — running it again after Tasks 4/5 should be a no-op.

Follows the project's existing convention of editor builders (`AttachSheetBuilder`, `BotSettingsRebuilder`).

- [ ] **Step 1: Create the file**

Write to `Assets/Editor/InputFieldMigrator.cs`:

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot migration utility. Walks the project's prefabs and the Main scene,
/// and swaps every TMP_InputField MonoBehaviour's m_Script reference to
/// DeferredDismissInputField. Idempotent — instances already on the new
/// script are skipped.
/// </summary>
public static class InputFieldMigrator
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Tools/Input Fields/Migrate to DeferredDismissInputField")]
    public static void Migrate()
    {
        MonoScript newScript = FindDeferredScript();
        if (newScript == null)
        {
            Debug.LogError("[InputFieldMigrator] DeferredDismissInputField MonoScript not found in project.");
            return;
        }

        int totalSwapped = 0;

        // Pass 1: prefab assets
        foreach (var prefabPath in EnumeratePrefabAssetPaths())
        {
            totalSwapped += MigratePrefab(prefabPath, newScript);
        }

        // Pass 2: Main scene
        totalSwapped += MigrateScene(newScript);

        Debug.Log($"[InputFieldMigrator] Done. Swapped {totalSwapped} TMP_InputField instance(s) to DeferredDismissInputField.");
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Input Fields/Migrate Scene Only")]
    public static void MigrateSceneOnly()
    {
        MonoScript newScript = FindDeferredScript();
        if (newScript == null)
        {
            Debug.LogError("[InputFieldMigrator] DeferredDismissInputField MonoScript not found in project.");
            return;
        }

        int swapped = MigrateScene(newScript);
        Debug.Log($"[InputFieldMigrator] Scene-only pass. Swapped {swapped} instance(s).");
        AssetDatabase.SaveAssets();
    }

    private static MonoScript FindDeferredScript()
    {
        string[] guids = AssetDatabase.FindAssets("DeferredDismissInputField t:MonoScript");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == typeof(DeferredDismissInputField))
                return script;
        }
        return null;
    }

    private static IEnumerable<string> EnumeratePrefabAssetPaths()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        var paths = new List<string>(guids.Length);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/"))
                paths.Add(path);
        }
        return paths;
    }

    private static int MigratePrefab(string prefabPath, MonoScript newScript)
    {
        var contents = PrefabUtility.LoadPrefabContents(prefabPath);
        if (contents == null) return 0;

        int swapped = 0;
        try
        {
            foreach (var input in contents.GetComponentsInChildren<TMP_InputField>(includeInactive: true))
            {
                if (input is DeferredDismissInputField) continue;
                SwapScript(input, newScript);
                swapped++;
            }

            if (swapped > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                Debug.Log($"[InputFieldMigrator] {prefabPath}: swapped {swapped}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return swapped;
    }

    private static int MigrateScene(MonoScript newScript)
    {
        var openScene = EditorSceneManager.GetActiveScene();
        bool openedHere = false;
        Scene scene;

        if (openScene.path == ScenePath && openScene.isLoaded)
        {
            scene = openScene;
        }
        else
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            openedHere = true;
        }

        int swapped = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(includeInactive: true))
            {
                if (input is DeferredDismissInputField) continue;
                SwapScript(input, newScript);
                swapped++;
            }
        }

        if (swapped > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[InputFieldMigrator] {ScenePath}: swapped {swapped}");
        }

        if (openedHere && swapped == 0)
        {
            // No changes — leave the scene in whatever state the user had it.
        }

        return swapped;
    }

    private static void SwapScript(TMP_InputField component, MonoScript newScript)
    {
        var so = new SerializedObject(component);
        var scriptProp = so.FindProperty("m_Script");
        scriptProp.objectReferenceValue = newScript;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(component);
    }
}
#endif
```

- [ ] **Step 2: Verify it compiles**

```bash
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath . \
  -quit -logFile /tmp/unity-compile.log
grep "error CS" /tmp/unity-compile.log || echo "no compile errors"
```

Expected: `no compile errors`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/InputFieldMigrator.cs Assets/Editor/InputFieldMigrator.cs.meta
git commit -m "$(cat <<'EOF'
chore(editor): InputFieldMigrator one-shot tool

Walks Assets/ prefabs and Assets/Scenes/Main.unity, swapping every
TMP_InputField's m_Script reference to DeferredDismissInputField.
Idempotent — instances already on the new script are skipped.

Menu items:
  Tools/Input Fields/Migrate to DeferredDismissInputField (full)
  Tools/Input Fields/Migrate Scene Only (chat-only test pass)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Migrate the chat scene only (Phase 1 — verify AttachSheet bug fix)

**Files:**
- Modify: `Assets/Scenes/Main.unity` (m_Script GUIDs for 9 TMP_InputField components)

Run the migrator on the scene only first, so the AttachSheet bug fix can be verified in isolation before touching the BotSettings prefab.

- [ ] **Step 1: Open Unity Editor and run the scene-only migrator**

Open Unity Editor (manual, the GUI is required for the migrator's PrefabUtility / EditorSceneManager calls), open the project, then in the menu bar:

`Tools → Input Fields → Migrate Scene Only`

Expected console output (in Unity Console window):
```
[InputFieldMigrator] Assets/Scenes/Main.unity: swapped 9
[InputFieldMigrator] Scene-only pass. Swapped 9 instance(s).
```

- [ ] **Step 2: Verify scene YAML changed**

From terminal:

```bash
git diff --stat Assets/Scenes/Main.unity
```

Expected: 1 file changed, ~18 insertions(+), ~18 deletions(-). (One m_Script line changed per of the 9 instances; YAML diffs sometimes show as separate add/remove.)

Verify the GUID is the new one — find DeferredDismissInputField's GUID:

```bash
grep -E "^guid:" Assets/Scripts/Chat/DeferredDismissInputField.cs.meta
```

Then confirm Main.unity references that GUID at least 9 times:

```bash
DEFERRED_GUID=$(grep -E "^guid:" Assets/Scripts/Chat/DeferredDismissInputField.cs.meta | awk '{print $2}')
grep -c "guid: $DEFERRED_GUID" Assets/Scenes/Main.unity
```

Expected: at least 9 (could be more if other components reference it, but for now it should be exactly 9).

And confirm the old TMP_InputField GUID has 9 fewer references in the scene than before — should be 0 if the scene only had chat-side input fields:

```bash
grep -c "guid: 2da0c512f12947e489f739169773d7ca" Assets/Scenes/Main.unity
```

Expected: 0 (all 9 scene-side instances were migrated).

- [ ] **Step 3: Verify in editor Play Mode that the chat input still works**

In Unity Editor, with `Main.unity` open, press Play. Tap the chat input field (simulated keyboard appears via `KeyboardAwarePanel`'s `k` hotkey path). Type some text. Verify:
- Send button appears when text is non-empty.
- Send button fires (check console for any error logs).
- The chat input field receives keystrokes normally.

If Play Mode doesn't reflect the changes, exit Play Mode, save the scene, and replay.

- [ ] **Step 4: Manual on-device test for AttachSheet bug**

If iOS device available:
1. Build for iOS (`Tools/Build Settings/Build`).
2. Deploy to device.
3. Open the chat screen, focus the chat input. Keyboard appears.
4. Tap the `+` button. Expected: AttachSheet slides up smoothly. Keyboard slides down. Sheet does NOT slide back down on finger release.
5. Tap a Camera/Gallery/Document tile. Expected: sheet closes, picker opens.

If no iOS device available right now, mark this as a deferred verification step and proceed; the spec §8 lists this as the primary verification.

- [ ] **Step 5: Commit the scene migration**

```bash
git add Assets/Scenes/Main.unity
git commit -m "$(cat <<'EOF'
fix(chat): migrate Main scene TMP_InputField instances to DeferredDismissInputField

Phase 1 of the deferred-dismiss rollout: scene-only migration so the
AttachSheet flicker bug is fixed in isolation before touching the
BotSettings prefab.

Swapped 9 instances via Tools/Input Fields/Migrate Scene Only.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Migrate the BotSettings prefab (Phase 2 — extend to BotSettings fields)

**Files:**
- Modify: `Assets/Prefabs/BotSettings.prefab` (m_Script GUIDs for 9 TMP_InputField components)

Now extend to the BotSettings page's input fields. Verifies smooth-switch behavior between fields.

- [ ] **Step 1: Run the full migrator**

In Unity Editor menu:

`Tools → Input Fields → Migrate to DeferredDismissInputField`

Expected console output:
```
[InputFieldMigrator] Assets/Prefabs/BotSettings.prefab: swapped 9
[InputFieldMigrator] Done. Swapped 9 TMP_InputField instance(s) to DeferredDismissInputField.
```

(Note: this run skips the 9 scene-side instances already migrated, because they're now `DeferredDismissInputField` and the `is DeferredDismissInputField` early-out fires.)

- [ ] **Step 2: Verify prefab YAML changed**

```bash
git diff --stat Assets/Prefabs/BotSettings.prefab
DEFERRED_GUID=$(grep -E "^guid:" Assets/Scripts/Chat/DeferredDismissInputField.cs.meta | awk '{print $2}')
grep -c "guid: $DEFERRED_GUID" Assets/Prefabs/BotSettings.prefab
```

Expected: 9 references to the new GUID.

```bash
grep -c "guid: 2da0c512f12947e489f739169773d7ca" Assets/Prefabs/BotSettings.prefab
```

Expected: 0.

- [ ] **Step 3: Verify EditableField behavior in editor**

In Unity Editor Play Mode:
1. Navigate to Bot Settings (tap a bot, then settings icon).
2. Tap the Name field — focus + scrim appears.
3. Tap into the Description field directly (without dismissing Name first). Verify: focus switches, scrim stays, no visible flicker.
4. Tap outside any field (on the scrim) — Blur fires, scrim hides.

If any field doesn't focus correctly or scrim behavior breaks, the issue is likely the `[DefaultExecutionOrder(-50)]` interacting with `EditableField.Update`'s polling. Confirm by adding temporary `Debug.Log` in `EditableField.HandleSelect` and re-running.

- [ ] **Step 4: Manual on-device test for smooth-switch**

If iOS device available:
1. Navigate to Bot Settings, focus Name field.
2. Tap directly into Description — verify keyboard does NOT animate down between fields.
3. Tap outside (scrim) — verify keyboard dismisses on finger-up.

- [ ] **Step 5: Commit the prefab migration**

```bash
git add Assets/Prefabs/BotSettings.prefab
git commit -m "$(cat <<'EOF'
fix(bot-settings): migrate BotSettings prefab TMP_InputField instances

Phase 2 of deferred-dismiss rollout: BotSettings page now also defers
keyboard dismissal to PointerUp. Direct field-to-field tap is now a
smooth switch with no keyboard slide-down.

Swapped 9 instances via Tools/Input Fields/Migrate to DeferredDismissInputField.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Final verification pass

**Files:** none modified

Run the full §8 verification checklist from the spec.

- [ ] **Step 1: Project-wide static check**

Confirm no stray `TMP_InputField` instances remain in scene/prefab YAML:

```bash
TMP_GUID="2da0c512f12947e489f739169773d7ca"
grep -rln "guid: $TMP_GUID" Assets/Scenes/ Assets/Prefabs/ || echo "no remaining TMP_InputField instances"
```

Expected: `no remaining TMP_InputField instances`.

If any remain, run `Tools/Input Fields/Migrate to DeferredDismissInputField` once more and inspect the console for the affected prefab path. If the migrator skipped a prefab (e.g., a nested prefab variant), migrate manually via Inspector Debug Mode (`Inspector → top-right menu → Debug`, then drag `DeferredDismissInputField` MonoScript onto the `Script` field of the affected component).

- [ ] **Step 2: Confirm Navigation.Mode.None applied**

```bash
grep -n "SetNavigationNone" Assets/Scripts/Chat/MessagesBottomPanel.cs
```

Expected: 3 lines under `OnEnable` (`SetNavigationNone(attachButton)`, `SetNavigationNone(micButton)`, `SetNavigationNone(sendButton)`).

- [ ] **Step 3: Run iOS device pass against spec §8 checklist**

For each scenario in [docs/superpowers/specs/2026-05-27-deferred-keyboard-dismiss-design.md](docs/superpowers/specs/2026-05-27-deferred-keyboard-dismiss-design.md) §8:

1. Chat input → tap `+` → sheet slides up smoothly, does not re-close on release.
2. Chat input → tap message list → keyboard stays up during tap, slides down on release.
3. Bot Settings → focus Name → tap Description → keyboard stays put, cursor switches.
4. Type → tap Send → message sends, keyboard stays up, no flicker.
5. Mic button (empty input) → no premature dismissal on Down.
6. Chat input → drag-scroll message list → keyboard dismisses on finger release.
7. Hardware Bluetooth Done → immediate dismiss as before.
8. Focus chat input → navigate to Bots page → no zombie keyboard.

For each that passes, mark the scenario number as ✓ in a comment on the merge PR. For any that fail, file a follow-up issue with the failure mode.

- [ ] **Step 4: Editor smoke test for completeness**

In Unity Editor Play Mode:
1. Focus chat input (simulated keyboard via `k`), click empty area → input deselects on mouse release, not mouse down (watch the caret blinking).
2. Toggle simulated keyboard via `k` hotkey — `KeyboardAwarePanel` still animates correctly.

- [ ] **Step 5: Final commit (if anything was tweaked during verification)**

If no changes were needed during verification, skip this commit. Otherwise:

```bash
git add -p   # review and stage only the verification-driven tweaks
git commit -m "fix(chat): verification fixes for deferred dismiss rollout"
```

---

## Notes for the implementer

- **Unity Editor required for Tasks 4–6.** The migrator uses `PrefabUtility.LoadPrefabContents` / `EditorSceneManager.OpenScene` which need a running Unity Editor process — not batch mode. Run the menu items from the Editor GUI.
- **The migrator is idempotent.** Re-running it does no harm. If a step fails partway, just re-run.
- **The InputFieldMigrator can be deleted after Task 6** if you don't expect to need it again — it's a one-shot tool. The project convention (`AttachSheetBuilder`, `BotSettingsRebuilder`) is to keep these around for re-runs, so leaving it in place is fine.
- **TMP_InputField guid recorded during planning:** `2da0c512f12947e489f739169773d7ca` (TextMeshPro package). Used in static-check `grep` commands.
- **Spec reference:** [docs/superpowers/specs/2026-05-27-deferred-keyboard-dismiss-design.md](docs/superpowers/specs/2026-05-27-deferred-keyboard-dismiss-design.md). When verifying behavior, refer to spec §6 (data flow) and §8 (verification checklist).
