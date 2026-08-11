using System.Reflection;
using DG.Tweening;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// A field whose editing session ends without a pointer path must not go on
/// painting its caret. Two such paths exist, and each has its own choke point:
///
///  • HIDDEN mid-edit by a tab or page switch. Every navigation path in the app
///    hides its screen with SetActive(false) — BottomTabManager.SwitchTab,
///    AddBotPanel.Close / CloseImmediate, ProfileSubPages.Close,
///    BotSettings.SetActiveTab and the Bot Settings close — so the input
///    field's OnDisable is the one choke point they all pass through.
///  • DISMISSED with nothing hidden: AttachSheet.Open deactivates the chat
///    composer to drop the keyboard and then slides the sheet up over it. No
///    GameObject is hidden, so OnDisable never runs and its release cannot
///    cover this — the sheet's own call site has to.
///
/// Mechanism being guarded: every input in this project has `Reset On
/// Deactivation` off (m_ResetOnDeActivation: 0 on all 13 fields in Main.unity),
/// and with that flag off TMP's DeactivateInputField sets
/// m_SelectionStillActive = true and deliberately skips ReleaseSelection.
/// OnFillVBO's guard is `if (!isFocused &amp;&amp; !m_SelectionStillActive) return
/// empty`, so while that flag holds the caret quad is re-emitted at its last
/// position on every later canvas rebuild — a static ghost caret in a field
/// nobody is editing. TMP's own self-heal (LateUpdate) only releases when
/// m_ResetOnDeActivation is true, and no LateUpdate runs on a disabled object
/// anyway. EditableField.ForceBlur and ChatSearchBar.ReleaseFocus already work
/// around this at their own call sites; OnDisable covers the rest.
/// </summary>
public class InputFieldHideCaretTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // TMP internals the fix leans on. Read through reflection so a Unity/uGUI
    // upgrade that renames or removes them fails loudly here (same idiom as
    // KeyboardSelectionSyncPinTests) instead of silently bringing the ghost back.
    private static readonly FieldInfo SelectionStillActive =
        typeof(TMP_InputField).GetField("m_SelectionStillActive", Private);
    private static readonly FieldInfo AllowInput =
        typeof(TMP_InputField).GetField("m_AllowInput", Private);
    private static readonly MethodInfo OnDisableMethod =
        typeof(DeferredDismissInputField).GetMethod("OnDisable", Private);

    /// <summary>True while TMP would still emit the caret quad on rebuild.</summary>
    private static bool IsCaretPainting(TMP_InputField input) =>
        (bool)SelectionStillActive.GetValue(input);

    /// <summary>
    /// Marks an editing session as live. The real path is ActivateInputField,
    /// which only materialises in play mode (it defers to LateUpdate), so
    /// EditMode has to set the flag that backs isFocused directly.
    /// </summary>
    private static void SimulateFocused(TMP_InputField input) => AllowInput.SetValue(input, true);

    private static DeferredDismissInputField BuildField(out GameObject root)
    {
        root = new GameObject("Field", typeof(RectTransform));

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(root.transform, false);

        var input = root.AddComponent<DeferredDismissInputField>();
        input.textComponent = textGo.AddComponent<TextMeshProUGUI>();

        // Mirror the shipped fields. TMP's code default is `true`, but every
        // input in this project serializes m_ResetOnDeActivation: 0 — and that
        // is exactly what makes the ghost possible, since DeactivateInputField
        // only self-releases when this is on. A field built with the code
        // default would quietly pass every test below while the app still
        // shows the ghost.
        input.resetOnDeActivation = false;
        return input;
    }

    /// <summary>
    /// A sheet wired to <paramref name="composer"/>, as AttachSheetBuilder wires
    /// the real one (SetObjectRef "inputField", bottomPanel.inputField). Awake is
    /// invoked by hand — it caches the RectTransform Open() positions, and no
    /// play mode runs it here.
    /// </summary>
    private static AttachSheet BuildAttachSheet(TMP_InputField composer, out GameObject root)
    {
        root = new GameObject("AttachSheet", typeof(RectTransform));
        var sheet = root.AddComponent<AttachSheet>();

        typeof(AttachSheet).GetField("inputField", Private).SetValue(sheet, composer);
        typeof(AttachSheet).GetMethod("Awake", Private).Invoke(sheet, null);
        return sheet;
    }

    [Test]
    public void TmpSeamsTheFixDependsOnStillExist()
    {
        Assert.IsNotNull(SelectionStillActive,
            "TMP_InputField.m_SelectionStillActive is gone — the caret-paint guard this fix clears has moved.");
        Assert.IsNotNull(AllowInput, "TMP_InputField.m_AllowInput is gone.");
        Assert.IsNotNull(OnDisableMethod, "DeferredDismissInputField.OnDisable is gone.");
    }

    // Pins the TMP behaviour the fix compensates for. If this ever fails, TMP
    // started releasing on its own and the OnDisable workaround can go.
    [Test]
    public void TmpDeactivateAloneLeavesTheCaretPainting()
    {
        var input = BuildField(out var root);
        try
        {
            SimulateFocused(input);
            input.DeactivateInputField();

            Assert.IsTrue(IsCaretPainting(input),
                "TMP no longer leaves m_SelectionStillActive set after deactivation — re-check whether " +
                "the ReleaseSelection call in DeferredDismissInputField.OnDisable is still needed.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void HidingAFocusedField_StopsPaintingTheCaret()
    {
        var input = BuildField(out var root);
        try
        {
            SimulateFocused(input);

            OnDisableMethod.Invoke(input, null); // the tab / page switch

            Assert.IsFalse(IsCaretPainting(input),
                "A field hidden mid-edit still paints its caret: it comes back as a static ghost caret " +
                "the next time the tab or page is shown.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // The dismiss the EventSystem started (OnDeselect) but the deferred path had
    // not finished when the page went away — same ghost, same release.
    [Test]
    public void HidingAFieldWithADismissPending_StopsPaintingTheCaret()
    {
        var input = BuildField(out var root);
        try
        {
            SimulateFocused(input);
            input.OnDeselect(new UnityEngine.EventSystems.BaseEventData(null));

            OnDisableMethod.Invoke(input, null);

            Assert.IsFalse(IsCaretPainting(input),
                "A field hidden with a deferred dismiss still pending keeps painting its caret.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // AttachmentPreviewScreen deactivates the caption field explicitly and
    // closes the screen on the next line, so by the time OnDisable runs the
    // field no longer reports focus — but TMP is still painting its caret, and
    // the screen reopens for the next attachment showing it.
    [Test]
    public void HidingAFieldDeactivatedJustBefore_StopsPaintingTheCaret()
    {
        var input = BuildField(out var root);
        try
        {
            SimulateFocused(input);
            input.DeactivateInputField(); // the explicit dismiss on the line before Close()
            Assert.IsTrue(IsCaretPainting(input), "precondition: TMP is still painting the caret");

            OnDisableMethod.Invoke(input, null);

            Assert.IsFalse(IsCaretPainting(input),
                "A field explicitly deactivated just before its screen closed keeps painting its caret.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ReleaseSelection is also what raises onEndEdit, which is how
    // EditableField.HandleEndEdit → Blur commits the typed value and lights
    // Save. Hiding the page mid-edit must commit, not silently discard — the
    // same reasoning as the real-dismiss path in DeferredDismissInputField.Update.
    [Test]
    public void HidingAFocusedField_CommitsThroughEndEdit()
    {
        var input = BuildField(out var root);
        try
        {
            int endEdits = 0;
            input.onEndEdit.AddListener(_ => endEdits++);
            SimulateFocused(input);

            OnDisableMethod.Invoke(input, null);

            Assert.AreEqual(1, endEdits,
                "Hiding a field mid-edit must raise onEndEdit, or the typed value is discarded.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // The other side of that gate: a field nobody was editing must stay quiet.
    // onEndEdit on every page hide would commit stale values and dirty the
    // Save button on screens the user only looked at.
    [Test]
    public void HidingAnUntouchedField_DoesNotFireEndEdit()
    {
        var input = BuildField(out var root);
        try
        {
            int endEdits = 0;
            input.onEndEdit.AddListener(_ => endEdits++);

            OnDisableMethod.Invoke(input, null);

            Assert.AreEqual(0, endEdits,
                "A field that was never focused fired onEndEdit on hide — that commits a value nobody typed.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ── AttachSheet: the dismiss where nothing is hidden ──────────────────

    // Tapping 📎 mid-message drops the keyboard with an explicit
    // DeactivateInputField and slides the sheet up. Nothing is SetActive(false),
    // so OnDisable's release never runs — and DeactivateInputField on its own
    // leaves m_SelectionStillActive set (pinned by
    // TmpDeactivateAloneLeavesTheCaretPainting above). The composer therefore
    // goes on painting its caret behind the sheet and still shows it once the
    // sheet dismisses, until the user taps the composer again (real focus) or
    // the chat screen is hidden.
    //
    // This also pins the ORDER of the two calls: ReleaseSelection must come
    // AFTER DeactivateInputField, which re-sets the flag on its way out.
    [Test]
    public void OpeningTheAttachSheet_StopsTheComposerPaintingItsCaret()
    {
        // Open() creates its slide-up tween; DOTween's edit-mode init is noisy
        // and is not the behaviour under test.
        LogAssert.ignoreFailingMessages = true;

        var input = BuildField(out var fieldRoot);
        var sheet = BuildAttachSheet(input, out var sheetRoot);
        try
        {
            SimulateFocused(input);

            sheet.Open();

            Assert.IsFalse(IsCaretPainting(input),
                "The composer still paints its caret after the attach sheet dismissed it — a static " +
                "ghost caret sits in a field nobody is editing until it is tapped again.");
        }
        finally
        {
            DOTween.Kill(sheetRoot.transform);
            Object.DestroyImmediate(sheetRoot);
            Object.DestroyImmediate(fieldRoot);
            LogAssert.ignoreFailingMessages = false;
        }
    }

    // The other half of the sheet's contract: releasing the selection must not
    // come at the cost of the dismissal itself. Open() decouples the sheet from
    // the keyboard deliberately (iOS animates the slide-down, and
    // KeyboardAwarePanel drops the input bar off its own rawKb tracking), so a
    // composer still focused behind the sheet is a regression.
    [Test]
    public void OpeningTheAttachSheet_StillEndsTheEditingSession()
    {
        LogAssert.ignoreFailingMessages = true;

        var input = BuildField(out var fieldRoot);
        var sheet = BuildAttachSheet(input, out var sheetRoot);
        try
        {
            SimulateFocused(input);

            sheet.Open();

            Assert.IsFalse(input.isFocused,
                "The composer is still focused behind the attach sheet — the keyboard never dropped.");
        }
        finally
        {
            DOTween.Kill(sheetRoot.transform);
            Object.DestroyImmediate(sheetRoot);
            Object.DestroyImmediate(fieldRoot);
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
