using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
/// Two invariants added for the iOS shared-keyboard saga (cross-field text
/// duplication + stuck keyboard, both device-trace-proven):
///  • SINGLE FOCUS — every activation path (OnSelect AND TMP's direct
///    OnPointerClick path, which bypasses OnSelect) first silently releases
///    every other focused instance, so at most ONE field ever reads the one
///    shared native keyboard buffer. Activation is always immediate: a
///    deferred-activation scheme was tried and became the main source of
///    ownerless keyboards (its adopter could be superseded away).
///  • OWNED KEYBOARD — a smooth switch abandons the OS keyboard without
///    closing it (that is what keeps it up across switches); the reference
///    is parked, the next activation adopts it, and a watchdog (or
///    OnDisable) closes it when no adopter arrives, so the keyboard can
///    never be stranded on screen.
///
/// Explicit programmatic dismissals (DeactivateInputField from AttachSheet,
/// EditableField.Blur, ChatSearchBar, etc.) bypass OnDeselect entirely and
/// keep their immediate-dismiss semantics.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DeferredDismissInputField : TMP_InputField,
    IInitializePotentialDragHandler
{
    // ── drag scrolls, it does not select ─────────────────────────────────
    // On a touch screen, a finger dragged across a field scrolls whatever the
    // field sits in; selecting text is the long-press + pins layer
    // (Scripts/TextSelection). TMP's own drag handlers only move the
    // selection, and since the EventSystem hands a drag to the nearest
    // IDragHandler ancestor — this field — the surrounding ScrollRect never
    // sees the gesture and the form just stands still under the finger.
    // Fields inside a scrollable card (Описание/Промпт) have a DragShield
    // that wins the raycast and routes ahead of us; this covers all the rest.
    // Resolved once per gesture so a drag never switches targets midway.
    private ScrollRect dragTarget;

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        // Kill any fling in progress, exactly as ScrollRect would have.
        // Explicit null check, not `?.` — that bypasses Unity's Object
        // lifetime comparison.
        var target = DragScrollRouting.ResolveTarget(transform);
        if (target != null) target.OnInitializePotentialDrag(eventData);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        dragTarget = DragScrollRouting.ResolveTarget(transform);
        if (dragTarget == null) { base.OnBeginDrag(eventData); return; }
        dragTarget.OnBeginDrag(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        if (dragTarget == null) { base.OnDrag(eventData); return; }
        dragTarget.OnDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (dragTarget == null) { base.OnEndDrag(eventData); return; }
        dragTarget.OnEndDrag(eventData);
        dragTarget = null;
    }

    private bool dismissPending;
    private float dismissPendingSince;
    // Rapid taps can leak a stuck "pressed" flag out of the Input System on
    // iOS; without a timeout that would block a pending dismissal forever
    // (keyboard visibly stuck, Back unable to close it).
    private const float DismissPointerTimeoutSeconds = 1.5f;

    // Every live instance, for the single-focus invariant and the watchdog.
    private static readonly System.Collections.Generic.List<DeferredDismissInputField> liveInputs =
        new System.Collections.Generic.List<DeferredDismissInputField>();

    // ── orphaned-keyboard bookkeeping ────────────────────────────────
    // SilentCaretStop abandons the OS keyboard on the assumption the newly
    // tapped field adopts it. If no adopter arrives (the tap burst ends
    // outside any field), nothing references the keyboard and nothing could
    // ever close it. Park on abandon, adopt on activation, close on timeout.
    private static TouchScreenKeyboard orphanedKeyboard;
    private static float orphanedAtTime;
    private static int orphanCheckFrame = -1;
    private const float OrphanGraceSeconds = 0.35f;

    // Keyboard-lifecycle diagnostic (activate/release/park/adopt/close).
    // Both device bugs it was built for — cross-field text duplication and
    // the stuck keyboard — are confirmed fixed; flip on again if either
    // ever resurfaces.
    private const bool TraceKeyboard = false;

    private static void Trace(string message)
    {
#pragma warning disable CS0162
        if (TraceKeyboard) Debug.Log($"[KB f{Time.frameCount}] {message}");
#pragma warning restore CS0162
    }

    private string Who() =>
        transform.parent != null ? $"{transform.parent.name}/{name}" : name;

    // OnFillVBO paints the caret quad for as long as this flag is set, so it —
    // not isFocused — is the real "would this field still show a caret?"
    // question. TMP keeps it private, hence reflection (same idiom as
    // KeyboardSelectionSync); a Unity/uGUI upgrade that renames it falls back
    // to the isFocused approximation below and fails loudly in
    // InputFieldHideCaretTests rather than silently bringing the ghost back.
    private static readonly System.Reflection.FieldInfo SelectionStillActiveField =
        typeof(TMP_InputField).GetField("m_SelectionStillActive",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private bool CaretStillPainting(bool fallback) =>
        SelectionStillActiveField != null
            ? (bool)SelectionStillActiveField.GetValue(this)
            : fallback;

    protected override void OnEnable()
    {
        base.OnEnable();
        liveInputs.Add(this);
    }

    protected override void OnDisable()
    {
        liveInputs.Remove(this);

        // A press vetoed a moment ago can end HERE — the veto listener raises
        // the panel, and that raise (or a Back / tab switch) hides this field
        // before its click is ever delivered. This is one of the three places
        // the vetoed pointer is cleared (with the top of the next press and the
        // click that consumes it): reset so a recycled touch id can never make
        // the next gesture's click look like the tail of this one, which would
        // swallow it silently with no ActivationVetoed raised.
        vetoedPointerId = NoVetoedPointer;

        // Teardown with an orphan still parked: no Update will run to
        // watchdog it, so close it now.
        if (orphanedKeyboard != null)
        {
            Trace($"{Who()} OnDisable closes orphaned keyboard");
            orphanedKeyboard.active = false;
            orphanedKeyboard = null;
        }

        // Captured before the teardown below runs: DeactivateInputField clears
        // the flag behind isFocused on its way out.
        bool wasEditing = isFocused || dismissPending;

        if (dismissPending)
        {
            dismissPending = false;
            if (EventSystem.current != null)
                base.OnDeselect(new BaseEventData(EventSystem.current));
        }
        base.OnDisable();

        // A tab or page switch hides the field with SetActive(false) — every
        // navigation path in the app does (BottomTabManager.SwitchTab,
        // AddBotPanel.Close/CloseImmediate, ProfileSubPages.Close,
        // BotSettings.SetActiveTab and the Bot Settings close) — so an
        // editing session can end HERE, without ever passing the pointer paths
        // above. Both of those release the selection after deactivating; this
        // one did not, and `Reset On Deactivation` is off on every input in
        // this project, so DeactivateInputField left m_SelectionStillActive
        // set. OnFillVBO's guard is `if (!isFocused && !m_SelectionStillActive)
        // return empty`, so the caret quad went on being re-emitted at its last
        // position and the page came back showing a static ghost caret in a
        // field nobody was editing. TMP's own self-heal (LateUpdate) only
        // releases when Reset On Deactivation is on — and a disabled object
        // gets no LateUpdate regardless.
        //
        // ReleaseSelection is also what raises onEndEdit, so a page hidden
        // mid-edit now COMMITS the typed value through
        // EditableField.HandleEndEdit → Blur instead of silently discarding it
        // — the same reasoning as the real-dismiss path in Update().
        //
        // Gated on the paint flag rather than on wasEditing so that a field
        // deactivated explicitly a moment BEFORE its screen closed is covered
        // too (AttachmentPreviewScreen does exactly that with the caption
        // field: by the time we get here isFocused is already false, but the
        // caret is still painting). A field nobody touched has the flag clear
        // and stays quiet, so no onEndEdit is raised for a value nobody typed.
        // Ordered after base.OnDisable, which would otherwise re-set the flag.
        if (CaretStillPainting(fallback: wasEditing)) ReleaseSelection();
    }

    public override void OnDeselect(BaseEventData eventData)
    {
        dismissPending = true;
        dismissPendingSince = Time.unscaledTime;
    }

    // NOTE: activation must NOT clear the parked keyboard ("adopt") here.
    // Activation is a promise — the actual keyboard-open happens a LateUpdate
    // later, and a rapid next tap can cancel it before it ever opens. The
    // device trace showed exactly that: clearing on the promise discarded the
    // only handle to the still-visible keyboard, leaving it unkillable. The
    // watchdog performs the adoption instead, once a field's focus has
    // MATERIALIZED (see CloseOrphanIfAbandoned).
    public override void OnSelect(BaseEventData eventData)
    {
        dismissPending = false;
        ReleaseOtherFocusedInputs();
        Trace($"{Who()} activate (select)");
        base.OnSelect(eventData);
    }

    // ── activation veto (pointer routes only) ────────────────────────────
    /// <summary>
    /// Optional per-INSTANCE predicate consulted before a POINTER gesture is
    /// allowed to focus this field. Returning true swallows the gesture: TMP
    /// never selects and never activates the field (so no keyboard opens) and
    /// <see cref="ActivationVetoed"/> is raised instead — which is what lets a
    /// tap on a collapsed composer raise the suggestions panel rather than
    /// start an edit.
    ///
    /// Default null, so every other input in the scene and in every prefab
    /// keeps today's behaviour byte for byte; only the owner of one specific
    /// field (the message composer) installs it.
    ///
    /// Consulted on the two POINTER activation routes: TMP's OnPointerDown
    /// (which selects the field itself → OnSelect → ActivateInputField) and
    /// TMP's direct OnPointerClick path. Deliberately NOT consulted in
    /// OnSelect — every PROGRAMMATIC activation must keep working (the ⌨ key,
    /// the post-Send re-focus, the reply focus), and all of them arrive there.
    ///
    /// OUT OF SCOPE BY DESIGN: the long-press / double-tap text-selection route
    /// (TextSelectionRouter calls SetSelectedGameObject + ActivateInputField
    /// itself, bypassing both pointer overrides) is NOT vetoed. Long-pressing a
    /// collapsed composer is a request to EDIT text, so focusing there is the
    /// correct behaviour.
    /// </summary>
    public Func<bool> ActivationVeto;

    /// <summary>
    /// Raised INSTEAD of activating when <see cref="ActivationVeto"/> returns
    /// true, so the installer can turn the swallowed tap into something else.
    /// Raised exactly ONCE per vetoed gesture: the pointer-down and the
    /// pointer-click of the same tap share a single raise.
    ///
    /// "Gesture" means one finger. A genuine two-finger tap on this field is
    /// two presses and announces twice, so keep the handler IDEMPOTENT (a raise
    /// means "the tap wanted the panel", not "toggle").
    ///
    /// It is a C# event, so a subscriber that re-subscribes on every screen
    /// open MUST `-=` before `+=`; nothing here clears the list, deliberately —
    /// this field is disabled and re-enabled by ordinary tab navigation and
    /// must come back with its veto still installed.
    /// </summary>
    public event Action ActivationVetoed;

    // The pointer whose press was already vetoed and announced. One tap is
    // delivered as PointerDown → PointerUp → PointerClick, so without this the
    // click would announce the same tap a second time.
    private const int NoVetoedPointer = int.MinValue;
    private int vetoedPointerId = NoVetoedPointer;

    // Route (a): TMP's own OnPointerDown calls
    // EventSystem.SetSelectedGameObject → OnSelect → ActivateInputField, so
    // gating OnPointerClick alone would NOT keep the keyboard shut.
    // (The input module deselects the previously selected object before it
    // dispatches this event — that is module behaviour we neither cause nor
    // can suppress; the vetoed field simply never becomes the new selection.)
    public override void OnPointerDown(PointerEventData eventData)
    {
        // A fresh press always starts a fresh gesture. This is what makes a
        // left-over id harmless: a press that ends without a click (finger
        // dragged away) can never be mistaken for a later tap that reuses the
        // same pointer id.
        vetoedPointerId = NoVetoedPointer;
        if (VetoActivation(eventData)) return;
        base.OnPointerDown(eventData);
    }

    // TMP also activates directly from OnPointerClick, bypassing OnSelect —
    // the single-focus invariant must hold on this path too. Route (b) of the
    // veto lives here as well, and is checked BEFORE ReleaseOtherFocusedInputs
    // so a vetoed tap leaves every other field's focus exactly as it was.
    public override void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.pointerId == vetoedPointerId)
        {
            // Same tap whose pointer-down was already vetoed and announced.
            vetoedPointerId = NoVetoedPointer;
            return;
        }
        if (!ActivationAlreadyPromised() && VetoActivation(eventData)) return;
        ReleaseOtherFocusedInputs();
        base.OnPointerClick(eventData);
    }

    // MATERIALIZED FOCUS, read from the other side: activation is a PROMISE, so
    // between TMP's OnPointerDown (which selects this field) and the LateUpdate
    // that finally sets m_AllowInput, isFocused is still false. When the down
    // and the click of one gesture land in the SAME dispatch pass — a very fast
    // tap, or DragShield's synthetic down→up→click burst — vetoing on the click
    // would announce a swallowed tap while the keyboard that same gesture
    // already committed to is on its way up: panel AND keyboard. Being the
    // EventSystem's current selection is that in-flight marker.
    //
    // Deliberately consulted on the CLICK route only. On the DOWN route a stale
    // selection would defeat the veto outright: SilentCaretStop deactivates a
    // field without deselecting it, so the composer can sit as
    // currentSelectedGameObject long after its keyboard was handed away.
    private bool ActivationAlreadyPromised() =>
        EventSystem.current != null
        && EventSystem.current.currentSelectedGameObject == gameObject;

    // Swallows the gesture when the installed predicate says so: nothing is
    // selected, nothing is activated, and no other field's focus is touched —
    // the listener decides what the tap means instead.
    private bool VetoActivation(PointerEventData eventData)
    {
        if (ActivationVeto == null) return false;
        // Left button only, matching TMP's own activation gates (MayDrag on
        // OnPointerDown, the explicit button test in OnPointerClick).
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return false;
        // The veto REPLACES an activation; where TMP could not have activated
        // anyway there is nothing to replace, so a tap on a field a CanvasGroup
        // has locked must not silently raise the panel instead. Same gate TMP
        // opens with (MayDrag).
        if (!IsActive() || !IsInteractable()) return false;
        // Already editing HERE: a tap that only moves the caret inside the
        // field the user is typing in must never be swallowed.
        if (isFocused) return false;
        if (!ActivationVeto()) return false;

        if (eventData != null) vetoedPointerId = eventData.pointerId;
        Trace($"{Who()} activation vetoed");
        ActivationVetoed?.Invoke();
        return true;
    }

    // NOTE (2026-08-14, device-reported): there is deliberately NO "forget the
    // vetoed pointer on release" step. An earlier version cleared the id on
    // PointerUp unless a click could still reach THIS field, testing
    // ExecuteEvents.GetEventHandler<IPointerClickHandler>(raycast) == gameObject.
    // That test is wrong for the composer, whose middle is covered by DragShield:
    // the shield implements IPointerClickHandler on purpose (to absorb the
    // generic dispatch) and re-sends the tap to us as a synthetic
    // down → up → click burst carrying ITS OWN raycast. The handler walk
    // therefore resolved to the shield, the id was dropped between our down and
    // our click — and by the time the click arrived the veto predicate had
    // already flipped false, because the pointer-down veto had just raised the
    // panel. The click then fell through to base and activated the field: ONE
    // tap that both raised the panel and opened the keyboard, i.e. exactly the
    // dip the two-step entry exists to prevent, and only in the middle of the
    // field (the bare edges, which reach us directly, behaved correctly).
    //
    // A left-over id is harmless by construction: OnPointerDown clears it at the
    // top of every fresh press, OnPointerClick clears it when it consumes the
    // matching click, and OnDisable clears it for a gesture that never ends.
    // Keeping it until one of those three is what makes the veto survive an
    // intermediary that rewrites the event's raycast.

    private void Update()
    {
        CloseOrphanIfAbandoned();

        if (!dismissPending) return;
        if (IsPointerPressed()
            && Time.unscaledTime - dismissPendingSince < DismissPointerTimeoutSeconds) return;

        dismissPending = false;

        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel != null && sel != gameObject && sel.GetComponent<TMP_InputField>() != null)
        {
            // Smooth-switch: another TMP_InputField has taken focus (and on
            // iOS is now driving the shared hidden UITextField). We must NOT
            // call base.OnDeselect — its DeactivateInputField path sets
            // m_SoftKeyboard.active = false, and Unity's iOS plugin
            // (KeyboardOnScreen singleton) resigns first-responder on that
            // shared text field, which dismisses the OS keyboard the newly
            // focused field is now using. Keeping the keyboard up is the
            // whole point of this branch.
            //
            // But we also can't leave the field as-is: m_AllowInput stays
            // true, the caret blink coroutine keeps running, and the
            // deselected field visibly blinks alongside the newly focused
            // one. SilentCaretStop mimics the in-component bookkeeping side
            // of DeactivateInputField while skipping the OS-keyboard side.
            Trace($"{Who()} smooth-switch release");
            SilentCaretStop();
            return;
        }

        Trace($"{Who()} real dismiss");
        base.OnDeselect(new BaseEventData(EventSystem.current));

        // ReleaseSelection is what fires SendOnEndEdit — in this TMP version it
        // is that method's ONLY caller, and the DeactivateInputField inside
        // base.OnDeselect deliberately skips it while `Reset On Deactivation`
        // is off (which it is on every input in this project, see
        // EditableField.ForceBlur). Without this line an outside tap ended the
        // editing session without ever raising onEndEdit, so
        // EditableField.HandleEndEdit → Blur → OnCommitted never ran: the typed
        // value was never committed, the Save button never lit, and the edit
        // was silently discarded on close. The smooth-switch branch above
        // already calls it for exactly this reason (SilentCaretStop step 3) —
        // which is why the bug only showed when the next tap was NOT another
        // input field, e.g. tapping Save itself. Keyboard lifecycle is
        // untouched: base.OnDeselect has already closed and nulled
        // m_SoftKeyboard, and ReleaseSelection never looks at it.
        ReleaseSelection();
    }

    // Silences every other focused instance so at most one field reads the
    // shared native keyboard buffer (device trace: two simultaneously
    // focused fields cross-ingest each other's text).
    private void ReleaseOtherFocusedInputs()
    {
        foreach (var other in liveInputs)
        {
            if (other == null || other == this) continue;
            if (!other.isFocused && other.m_SoftKeyboard == null) continue;
            Trace($"{Who()} releases {other.Who()}");
            other.dismissPending = false;
            other.SilentCaretStop();
        }
    }

    private static void CloseOrphanIfAbandoned()
    {
        if (orphanedKeyboard == null) return;
        if (Time.frameCount == orphanCheckFrame) return;
        orphanCheckFrame = Time.frameCount;

        // Adoption happens HERE, not at activation: only once a field's focus
        // has MATERIALIZED does the (singleton) OS keyboard truly have an
        // owner whose own reference can dismiss it. Releasing the slot any
        // earlier — e.g. on the activation promise — discards the only
        // handle if that activation gets canceled by the next rapid tap.
        foreach (var field in liveInputs)
        {
            if (field != null && field.isFocused)
            {
                Trace($"orphan adopted ({field.Who()} focused)");
                orphanedKeyboard = null;
                return;
            }
        }

        if (Time.unscaledTime - orphanedAtTime < OrphanGraceSeconds) return;

        Trace("watchdog closes orphaned keyboard");
        orphanedKeyboard.active = false;
        orphanedKeyboard = null;
    }

    private static bool IsPointerPressed()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
        if (Pointer.current != null && Pointer.current.press.isPressed) return true;
        return false;
    }

    // Stops this field's caret rendering and blink without dismissing the OS
    // keyboard. Used by the smooth-switch branch where another input has
    // taken first responder on iOS's shared hidden UITextField.
    //
    // Step-by-step alignment with TMP_InputField.DeactivateInputField:
    //   1. Park + null m_SoftKeyboard (protected — accessible in subclass) so
    //      the base's `if (m_SoftKeyboard != null) { m_SoftKeyboard.active = false; ... }`
    //      branch is skipped. Without this, the OS keyboard dismisses. The
    //      parked reference lets the watchdog close the keyboard if no other
    //      field adopts it.
    //   2. DeactivateInputField() sets m_AllowInput = false unconditionally,
    //      which terminates the caret blink coroutine (its loop checks
    //      m_AllowInput). It also sets m_SelectionStillActive = true on the
    //      way out.
    //   3. ReleaseSelection() clears m_SelectionStillActive — without this,
    //      OnFillVBO's guard keeps painting the caret quad at its last
    //      position on every canvas rebuild (same root cause as the
    //      ghost-caret bug EditableField.ForceBlur addresses). It also fires
    //      SendOnEndEdit, which lets EditableField.HandleEndEdit → Blur sync
    //      wrapper state via the existing event path. Order matters:
    //      ReleaseSelection MUST run after DeactivateInputField, since
    //      DeactivateInputField re-sets m_SelectionStillActive = true.
    private void SilentCaretStop()
    {
        if (m_SoftKeyboard != null)
        {
            Trace($"{Who()} parks keyboard");
            orphanedKeyboard = m_SoftKeyboard;
            orphanedAtTime = Time.unscaledTime;
        }
        m_SoftKeyboard = null;
        DeactivateInputField();
        ReleaseSelection();
    }
}
