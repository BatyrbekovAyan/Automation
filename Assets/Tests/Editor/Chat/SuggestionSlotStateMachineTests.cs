using NUnit.Framework;

// EditMode coverage for SuggestionSlotStateMachine — the whole slot transition table of the
// sketch-005 winner E model. Pins the rules that are invisible in the controller and expensive
// to rediscover on device: the two-step field entry (first tap raises the panel, does NOT focus),
// "a tap never hides an open panel", a pick that keeps the panel up, the single auto-raise
// (IncomingMessage from Collapsed only), and the fact that Panel/Expanded cannot survive «Авто».
// Two of the pins are CROSS-SEAM correlations rather than rows: the KeyTap destination must match
// the glyph ComposerSlotKeyModel shows, and AfterDrag's state must match the height
// SuggestionSlotDetents gives the same detent. Either half can be inverted on its own and still
// look internally consistent to its own suite — only the correlation catches it.
public class SuggestionSlotStateMachineTests
{
    private const bool SemiAuto = true;    // «Вместе» — the panel exists
    private const bool Auto = false;       // «Авто»   — no panel at all

    private static readonly SuggestionSlotState[] AllStates =
    {
        SuggestionSlotState.Collapsed,
        SuggestionSlotState.Panel,
        SuggestionSlotState.Expanded,
        SuggestionSlotState.Keyboard
    };

    // --- «Вместе»: FieldTap -------------------------------------------------

    [Test]
    public void FieldTap_FromCollapsed_RaisesThePanelWithoutFocusing()
    {
        // THE anti-dip rule: focusing here would open the keyboard underneath a panel still rising.
        SlotTransition t = SuggestionSlotStateMachine.Resolve(
            SuggestionSlotState.Collapsed, SuggestionSlotInput.FieldTap, SemiAuto);
        Assert.AreEqual(SuggestionSlotState.Panel, t.State);
        Assert.IsFalse(t.FocusField, "the first tap raises the panel only — it must never focus");
        AssertTransition(t, SuggestionSlotState.Panel);
    }

    [Test]
    public void FieldTap_FromPanel_OpensTheKeyboard()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Panel, SuggestionSlotInput.FieldTap, SemiAuto),
            SuggestionSlotState.Keyboard, focus: true);

    [Test]
    public void FieldTap_FromExpanded_OpensTheKeyboard()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Expanded, SuggestionSlotInput.FieldTap, SemiAuto),
            SuggestionSlotState.Keyboard, focus: true);

    [Test]
    public void FieldTap_WhileKeyboardUp_ReassertsFocus()
        // Already focused — a harmless re-assert, never a toggle-off.
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, SuggestionSlotInput.FieldTap, SemiAuto),
            SuggestionSlotState.Keyboard, focus: true);

    // --- «Вместе»: ThreadTap ------------------------------------------------

    [Test]
    public void ThreadTap_FromCollapsed_RaisesThePanel()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Collapsed, SuggestionSlotInput.ThreadTap, SemiAuto),
            SuggestionSlotState.Panel);

    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    public void ThreadTap_NeverLeavesAnOpenPanel(SuggestionSlotState open)
    {
        // Taps never collapse — only a downward drag of the handle does.
        SlotTransition t = SuggestionSlotStateMachine.Resolve(open, SuggestionSlotInput.ThreadTap, SemiAuto);
        Assert.AreEqual(open, t.State, "a thread tap must not hide an open panel");
        AssertTransition(t, open);
    }

    [Test]
    public void ThreadTap_WhileKeyboardUp_ReturnsTheSlotToThePanel()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, SuggestionSlotInput.ThreadTap, SemiAuto),
            SuggestionSlotState.Panel, blur: true);

    // --- «Вместе»: KeyTap ---------------------------------------------------

    [Test]
    public void KeyTap_FromCollapsed_RaisesThePanel()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Collapsed, SuggestionSlotInput.KeyTap, SemiAuto),
            SuggestionSlotState.Panel);

    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    public void KeyTap_FromAnOpenPanel_OpensTheKeyboard(SuggestionSlotState open)
        // The key shows the DESTINATION glyph, so over a panel it must deliver the keyboard.
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(open, SuggestionSlotInput.KeyTap, SemiAuto),
            SuggestionSlotState.Keyboard, focus: true);

    [Test]
    public void KeyTap_WhileKeyboardUp_ReturnsTheSlotToThePanel()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, SuggestionSlotInput.KeyTap, SemiAuto),
            SuggestionSlotState.Panel, blur: true);

    [Test]
    public void KeyTap_DeliversExactlyTheDestinationTheGlyphPromises()
    {
        // The glyph and the transition are two halves of ONE grammar living in two seams. Each half
        // is internally consistent even when inverted, so only this correlation catches a key that
        // shows ⌨ and hands back the panel.
        foreach (SuggestionSlotState state in AllStates)
        {
            bool promisesKeyboard =
                ComposerSlotKeyModel.For(state, semiAutoOn: true).Glyph == SlotKeyGlyph.Keyboard;
            bool deliversKeyboard =
                SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.KeyTap, SemiAuto).State
                    == SuggestionSlotState.Keyboard;
            Assert.AreEqual(promisesKeyboard, deliversKeyboard,
                $"the key's glyph and the KeyTap row disagree about the destination from {state}");
        }
    }

    // --- «Вместе»: Pick -----------------------------------------------------

    [Test]
    public void Pick_ChangesNothingAnywhere_AndNeverFocuses()
    {
        // Locked flow: the panel stays up so a re-clustered variant is one tap away, and picking
        // a card must never raise the keyboard. The panel leaves later, on AnsweredRun.
        foreach (SuggestionSlotState state in AllStates)
        {
            SlotTransition t = SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.Pick, SemiAuto);
            Assert.AreEqual(state, t.State, $"a pick must not move the slot (from {state})");
            Assert.IsFalse(t.FocusField, $"a pick must never open the keyboard (from {state})");
            AssertTransition(t, state);
        }
    }

    // --- «Вместе»: AnsweredRun ----------------------------------------------

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void AnsweredRun_CollapsesFromEveryState(SuggestionSlotState state)
        // Collapsing is what re-arms the auto-raise for the next incoming message.
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.AnsweredRun, SemiAuto),
            SuggestionSlotState.Collapsed);

    // --- «Вместе»: IncomingMessage ------------------------------------------

    [Test]
    public void IncomingMessage_AutoRaisesOnlyFromCollapsed()
    {
        SlotTransition t = SuggestionSlotStateMachine.Resolve(
            SuggestionSlotState.Collapsed, SuggestionSlotInput.IncomingMessage, SemiAuto);
        Assert.AreEqual(SuggestionSlotState.Panel, t.State, "the only auto-raise in the model");
        AssertTransition(t, SuggestionSlotState.Panel);
    }

    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void IncomingMessage_IsContentRefreshOnly_InEveryOtherState(SuggestionSlotState state)
    {
        // Nothing moves under the owner's finger — least of all while they are typing.
        SlotTransition t = SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.IncomingMessage, SemiAuto);
        Assert.AreEqual(state, t.State, "an arrival must not resize the slot");
        AssertTransition(t, state, refreshOnly: true);
    }

    // --- «Вместе»: KeyboardDismissed ----------------------------------------

    [Test]
    public void KeyboardDismissed_HandsTheSlotBackToThePanel()
        // The panel is the slot's DEFAULT tenant, so any blur returns it.
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(
                SuggestionSlotState.Keyboard, SuggestionSlotInput.KeyboardDismissed, SemiAuto),
            SuggestionSlotState.Panel);

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    public void KeyboardDismissed_WithoutAKeyboardUp_IsInert(SuggestionSlotState state)
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.KeyboardDismissed, SemiAuto),
            state);

    // --- Reply-mode flips ---------------------------------------------------

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void ReplyModeOn_HandsTheSlotToThePanel(SuggestionSlotState state)
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.ReplyModeOn, SemiAuto),
            SuggestionSlotState.Panel);

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void ReplyModeOff_SuppressesTheWholeSurface(SuggestionSlotState state)
    {
        // «Авто» has no suggestions surface — but the flip itself does not touch the field.
        SlotTransition t = SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.ReplyModeOff, SemiAuto);
        Assert.IsFalse(t.BlurField, "the mode flip must not blur the field on its own");
        AssertTransition(t, SuggestionSlotState.Collapsed);
    }

    // --- «Авто» -------------------------------------------------------------

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void Auto_FieldTap_FocusesNormallyFromEveryState(SuggestionSlotState state)
    {
        // The two-step entry belongs to the panel, which does not exist here — never withhold focus.
        SlotTransition t = SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.FieldTap, Auto);
        Assert.IsTrue(t.FocusField, $"«Авто» must focus the field directly (from {state})");
        AssertTransition(t, SuggestionSlotState.Keyboard, focus: true);
    }

    [Test]
    public void Auto_ThreadTap_DismissesTheKeyboard()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, SuggestionSlotInput.ThreadTap, Auto),
            SuggestionSlotState.Collapsed, blur: true);

    [Test]
    public void Auto_ThreadTap_WithNothingUp_IsInert()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Collapsed, SuggestionSlotInput.ThreadTap, Auto),
            SuggestionSlotState.Collapsed);

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void Auto_KeyTap_IsInert(SuggestionSlotState state)
        // The destination key is hidden in «Авто» — a stray call must do nothing at all.
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(state, SuggestionSlotInput.KeyTap, Auto), state);

    [Test]
    public void Auto_KeyboardDismissed_Collapses()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(
                SuggestionSlotState.Keyboard, SuggestionSlotInput.KeyboardDismissed, Auto),
            SuggestionSlotState.Collapsed);

    [Test]
    public void Auto_KeyboardDismissed_WithNothingUp_IsInert()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(
                SuggestionSlotState.Collapsed, SuggestionSlotInput.KeyboardDismissed, Auto),
            SuggestionSlotState.Collapsed);

    [TestCase(SuggestionSlotInput.Pick)]
    [TestCase(SuggestionSlotInput.AnsweredRun)]
    [TestCase(SuggestionSlotInput.IncomingMessage)]
    public void Auto_PanelOnlyInputs_AreInert(SuggestionSlotInput input)
    {
        AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, input, Auto),
            SuggestionSlotState.Keyboard);
        AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Collapsed, input, Auto),
            SuggestionSlotState.Collapsed);
    }

    [Test]
    public void Auto_ReplyModeOn_HandsTheSlotToThePanel()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, SuggestionSlotInput.ReplyModeOn, Auto),
            SuggestionSlotState.Panel);

    [Test]
    public void Auto_ReplyModeOff_Collapses()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Keyboard, SuggestionSlotInput.ReplyModeOff, Auto),
            SuggestionSlotState.Collapsed);

    // --- Illegal input ------------------------------------------------------

    [TestCase(SuggestionSlotState.Panel)]
    [TestCase(SuggestionSlotState.Expanded)]
    public void Auto_PanelState_NormalisesToCollapsed(SuggestionSlotState illegal)
    {
        // A panel cannot exist in «Авто» — a stale state must not be handed back out.
        SlotTransition t = SuggestionSlotStateMachine.Resolve(illegal, SuggestionSlotInput.Pick, Auto);
        Assert.AreEqual(SuggestionSlotState.Collapsed, t.State, "Panel/Expanded is illegal in «Авто»");
        AssertTransition(t, SuggestionSlotState.Collapsed);
    }

    [Test]
    public void UnknownState_NeverBecomesState()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve((SuggestionSlotState)99, SuggestionSlotInput.Pick, SemiAuto),
            SuggestionSlotState.Collapsed);

    [Test]
    public void UnknownInput_IsInert()
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(SuggestionSlotState.Panel, (SuggestionSlotInput)99, SemiAuto),
            SuggestionSlotState.Panel);

    [TestCase(SuggestionSlotState.Collapsed)]
    [TestCase(SuggestionSlotState.Keyboard)]
    public void Auto_UnknownInput_IsInert(SuggestionSlotState state)
        // «Авто» routes Pick/AnsweredRun/IncomingMessage through the SAME fallthrough as an
        // out-of-range cast, so pinning only those three leaves the fallthrough free to grow a
        // side effect (a focus, a collapse) that no named row would catch.
        => AssertTransition(
            SuggestionSlotStateMachine.Resolve(state, (SuggestionSlotInput)99, Auto), state);

    // --- AfterDrag ----------------------------------------------------------

    [TestCase(SlotDetent.Collapsed, SuggestionSlotState.Collapsed)]
    [TestCase(SlotDetent.Standard, SuggestionSlotState.Panel)]
    [TestCase(SlotDetent.Expanded, SuggestionSlotState.Expanded)]
    public void AfterDrag_TheDetentIsTheState(SlotDetent snapped, SuggestionSlotState expected)
        => Assert.AreEqual(expected, SuggestionSlotStateMachine.AfterDrag(snapped));

    [Test]
    public void AfterDrag_UnknownDetent_ResolvesToTheStandardDetent()
        // NOT Collapsed: SuggestionSlotDetents.HeightFor sizes an unknown detent at the STANDARD
        // height ("an unknown detent never silently collapses"), so collapsing here would leave the
        // machine calling a standard-height slot collapsed. Garbage resolves upward in this model.
        => Assert.AreEqual(SuggestionSlotState.Panel, SuggestionSlotStateMachine.AfterDrag((SlotDetent)99));

    [Test]
    public void AfterDrag_AgreesWithTheDetentHeights()
    {
        // The two seams read the same SlotDetent — a state that says "collapsed" over a non-zero
        // slot (or vice versa) is invisible until it ships, so pin the agreement, not each half.
        const float standard = 780f;
        const float expanded = 1200f;
        SlotDetent[] detents =
            { SlotDetent.Collapsed, SlotDetent.Standard, SlotDetent.Expanded, (SlotDetent)99 };

        foreach (SlotDetent detent in detents)
        {
            bool zeroHeight = SuggestionSlotDetents.HeightFor(detent, standard, expanded) <= 0f;
            bool collapsedState = SuggestionSlotStateMachine.AfterDrag(detent) == SuggestionSlotState.Collapsed;
            Assert.AreEqual(zeroHeight, collapsedState, $"height and state disagree for {detent}");
        }
    }

    // --- helper -------------------------------------------------------------

    private static void AssertTransition(
        SlotTransition actual,
        SuggestionSlotState state,
        bool focus = false,
        bool blur = false,
        bool refreshOnly = false)
    {
        Assert.AreEqual(state, actual.State, "State");
        Assert.AreEqual(focus, actual.FocusField, "FocusField");
        Assert.AreEqual(blur, actual.BlurField, "BlurField");
        Assert.AreEqual(refreshOnly, actual.ContentRefreshOnly, "ContentRefreshOnly");
    }
}
