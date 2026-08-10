using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using InputPointer = UnityEngine.InputSystem.Pointer;

/// App-wide iOS-style text-selection layer. One always-active singleton
/// (UploadCenter pattern: Instance creates, Existing never does) that
/// OBSERVES pointer input via the new Input System (Pointer.current — the
/// repo idiom, see MessageBubbleLongPress) and never consumes events, so
/// taps, typing, scrolling and every existing gesture behave exactly as
/// before. It runs the long-press/double-tap machine over whatever
/// TMP_InputField sits under the finger (raycast through ClickPassthrough
/// strips), drives the pins + menu on the runtime overlay, and routes every
/// programmatic selection change through KeyboardSelectionSync so the hidden
/// native keyboard buffer stays honest (spike-verified 2026-08-07, all
/// checks PASS — see the 2026-08-07 spec).
///
/// Runs BEFORE default-order scripts (notably DeferredDismissInputField):
/// pressing our pins/menu makes the EventSystem deselect the field, and the
/// deferred-dismiss machinery would close the keyboard on release. Running
/// first lets EnforceOwnUiSelection re-select the field and clear the
/// pending dismissal before it can fire.
[DefaultExecutionOrder(-50)]
public class TextSelectionRouter : MonoBehaviour
{
    static TextSelectionRouter _instance;
    public static TextSelectionRouter Existing => _instance;
    public static TextSelectionRouter Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying)
            {
                var go = new GameObject("TextSelectionRouter", typeof(TextSelectionRouter));
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var _ = Instance;
        Debug.Log("[TextSelection] Router bootstrapped");   // build-verification beacon (Editor + Xcode console)
    }

    static readonly FieldInfo KbField = typeof(TMP_InputField).GetField(
        "m_SoftKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);

    // Constructed in Awake — Screen.dpi is forbidden in MonoBehaviour field
    // initializers (UnityException), and a throwing initializer would poison
    // every field declared after it.
    SelectionGestureMachine _machine;

    SelectionOverlay _overlay;
    SelectionMenuView _menu;
    readonly List<RaycastResult> _hits = new List<RaycastResult>();

    TMP_InputField _pressField;        // field under the current press
    TMP_InputField _activeField;       // field owning the visible selection UI
    TMP_InputField _pendingField;      // long-pressed while unfocused; select after focus materializes
    Vector2 _pendingPos;
    float _pendingDeadline;
    bool _applyingEdit;                // our own mutation → not an external text change
    bool _menuPendingOnRelease;
    bool _ownUiPressActive;            // current press began on our pins/menu
    bool _extendArmed;                 // long-press drag-extension unlocked by real movement
    Vector2 _commitPos;                // finger position at long-press/double-tap commit
    float _slopPixels;
    int _lastAnchor = -1;
    int _lastFocus = -1;
    int _intendedAnchor = -1;          // the selection the router owns while pins are up
    int _intendedFocus = -1;
    string _intendedText;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _slopPixels = 10f * (Screen.dpi > 0 ? Screen.dpi : 160f) / 160f;   // 10 dp
        _machine = new SelectionGestureMachine(0.45f, 0.3f, _slopPixels);
        Theme.Changed += OnThemeChanged;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        Theme.Changed -= OnThemeChanged;
    }

    void OnThemeChanged()
    {
        if (_menu != null && _menu.IsVisible) _menu.ApplyTheme();
        if (_activeField != null) ApplySelectionTint(_activeField);
    }

    // ---------- input pump (new Input System) ----------

    void Update()
    {
        if (_machine == null) return;   // destroyed-duplicate edge: Awake early-returned
        var pointer = InputPointer.current;
        if (pointer == null) return;

        float now = Time.unscaledTime;
        Vector2 pos = pointer.position.ReadValue();

        if (pointer.press.wasPressedThisFrame)
            HandlePress(pos, now);
        else if (pointer.press.isPressed)
        {
            HandleGesture(_machine.Move(pos, now), pos);
            HandleGesture(_machine.Tick(now), pos);
            if (_machine.LongPressActive && _pressField != null && _pendingField == null)
            {
                // iOS parity: the committed word selection holds until the
                // finger actually MOVES; only then does drag-extension start
                // (a stationary hold must not collapse the selection to the
                // finger's character).
                if (!_extendArmed && (pos - _commitPos).sqrMagnitude > _slopPixels * _slopPixels)
                    _extendArmed = true;
                if (_extendArmed)
                    ExtendSelectionTo(_pressField, pos);
            }
        }
        else if (pointer.press.wasReleasedThisFrame)
        {
            _ownUiPressActive = false;
            HandleGesture(_machine.Release(pos, now), pos);
            if (_menuPendingOnRelease)
            {
                _menuPendingOnRelease = false;
                EnforceIntendedSelection();   // undo any same-frame clobber BEFORE the menu reads the selection
                ShowMenuForActiveField();
            }
        }

        EnforceOwnUiSelection();
        EnforceIntendedSelection();
        ProcessPendingFocusSelect();
        WatchExternalSelection();
        WatchFieldLifecycle();
    }

    void LateUpdate()
    {
        if (_overlay != null && _overlay.HandlesVisible && _activeField != null)
        {
            // TMP's keyboard read-back can clobber the selection between our
            // Update and this point (device-visible as a one-frame pin blink
            // at the tap position) — re-assert right before positioning.
            EnforceIntendedSelection();
            RepositionHandles();
        }
    }

    void HandlePress(Vector2 pos, float now)
    {
        _pressField = ResolvePress(pos, out bool overOwnUi);
        if (overOwnUi)
        {
            _ownUiPressActive = true;                // pins/menu handle their own input
            return;
        }

        var result = _machine.Press(pos, now);
        if (_pressField == null && result != SelectionGestureMachine.Result.DoubleTap)
            DismissAll();                            // outside tap
        else
            HandleGesture(result, pos);
    }

    // ---------- raycast ----------

    /// One raycast resolves both questions: is the press on our own overlay
    /// UI, and which TMP_InputField (if any) is under the finger. Walks ALL
    /// hits so ClickPassthrough strips / shields sitting on top of a field
    /// don't hide it.
    TMP_InputField ResolvePress(Vector2 screenPos, out bool overOwnUi)
    {
        overOwnUi = false;
        if (EventSystem.current == null) return null;

        var ped = new PointerEventData(EventSystem.current) { position = screenPos };
        _hits.Clear();
        EventSystem.current.RaycastAll(ped, _hits);

        TMP_InputField field = null;
        for (int i = 0; i < _hits.Count; i++)
        {
            var hitGo = _hits[i].gameObject;
            if (_overlay != null && hitGo.GetComponentInParent<SelectionOverlay>() != null)
            {
                overOwnUi = true;
                return null;
            }
            if (field == null)
            {
                var candidate = hitGo.GetComponentInParent<TMP_InputField>();
                if (candidate != null && candidate.interactable) field = candidate;
            }
        }
        return field;
    }

    // ---------- gesture handling ----------

    void HandleGesture(SelectionGestureMachine.Result result, Vector2 pos)
    {
        switch (result)
        {
            case SelectionGestureMachine.Result.LongPress:
            case SelectionGestureMachine.Result.DoubleTap:
                if (_pressField == null) break;
                _extendArmed = false;
                _commitPos = pos;
                if (!_pressField.isFocused)
                {
                    // Normal activation path — DeferredDismissInputField.OnSelect
                    // must run (single-focus invariant). The selection is applied
                    // only after focus MATERIALIZES (activation is a promise).
                    EventSystem.current.SetSelectedGameObject(_pressField.gameObject);
                    _pressField.ActivateInputField();
                    _pendingField = _pressField;
                    _pendingPos = pos;
                    _pendingDeadline = Time.unscaledTime + 1f;
                }
                else
                {
                    SelectWordAt(_pressField, pos);
                }
                _menuPendingOnRelease = true;
                break;

            case SelectionGestureMachine.Result.Tap:
                // iOS parity: a tap dismisses the selection UI wherever it
                // lands — inside the active field it collapses to the tapped
                // caret (TMP already moved it on pointer-down).
                DismissAll();
                break;

            case SelectionGestureMachine.Result.Cancel:
                _menuPendingOnRelease = false;
                break;
        }
    }

    void ProcessPendingFocusSelect()
    {
        if (_pendingField == null) return;
        if (Time.unscaledTime > _pendingDeadline) { _pendingField = null; return; }
        bool keyboardReady = Application.isEditor || KbField?.GetValue(_pendingField) != null;
        if (_pendingField.isFocused && keyboardReady)
        {
            SelectWordAt(_pendingField, _pendingPos);
            _pendingField = null;
        }
    }

    // ---------- selection ops ----------

    void SelectWordAt(TMP_InputField field, Vector2 screenPos)
    {
        int stringIndex = StringIndexAt(field, screenPos);
        var (start, end) = WordBoundary.WordRangeAt(field.text, stringIndex);
        ApplySelectionTint(field);
        _activeField = field;

        if (start == end)
        {
            field.stringPosition = start;
            KeyboardSelectionSync.Push(field);
            _overlay?.HideHandles();
            ClearIntent();
        }
        else
        {
            field.selectionStringAnchorPosition = start;
            field.selectionStringFocusPosition = end;
            KeyboardSelectionSync.Push(field);
            EnsureOverlay();
            _overlay.ShowHandles();
            SetIntent(field);
        }
        RememberSelection(field);
    }

    void ExtendSelectionTo(TMP_InputField field, Vector2 screenPos)
    {
        if (_activeField != field) return;
        int stringIndex = StringIndexAt(field, screenPos);
        if (stringIndex == field.selectionStringFocusPosition) return;
        field.selectionStringFocusPosition = stringIndex;
        KeyboardSelectionSync.Push(field);
        if (field.selectionStringAnchorPosition != field.selectionStringFocusPosition)
        {
            EnsureOverlay();
            _overlay.ShowHandles();
            SetIntent(field);
        }
        RememberSelection(field);
    }

    void OnHandleDragged(SelectionHandleView handle, Vector2 screenPos)
    {
        if (_activeField == null) return;
        _menu?.Hide();
        int dragIndex = StringIndexAt(_activeField, screenPos);
        int anchor = _activeField.selectionStringAnchorPosition;
        int focus = _activeField.selectionStringFocusPosition;
        int lo = Mathf.Min(anchor, focus);
        int hi = Mathf.Max(anchor, focus);

        if (handle.IsStart) lo = Mathf.Min(dragIndex, hi - 1);   // min 1 char selected
        else hi = Mathf.Max(dragIndex, lo + 1);

        _activeField.selectionStringAnchorPosition = lo;
        _activeField.selectionStringFocusPosition = hi;
        KeyboardSelectionSync.Push(_activeField);
        SetIntent(_activeField);
        RememberSelection(_activeField);
        AutoScrollTowards(_activeField, screenPos);
    }

    void OnHandleDragEnded(SelectionHandleView handle) => ShowMenuForActiveField();

    int StringIndexAt(TMP_InputField field, Vector2 screenPos)
    {
        int charIndex = TMP_TextUtilities.GetCursorIndexFromPosition(
            field.textComponent, screenPos, FieldCamera(field), out CaretPosition side);
        var info = field.textComponent.textInfo;
        if (info.characterCount == 0) return 0;
        charIndex = Mathf.Clamp(charIndex, 0, info.characterCount - 1);
        var characterInfo = info.characterInfo[charIndex];
        int stringIndex = side == CaretPosition.Right
            ? characterInfo.index + characterInfo.stringLength
            : characterInfo.index;
        return WordBoundary.ClampToCharBoundary(field.text, stringIndex);
    }

    static Camera FieldCamera(TMP_InputField field)
    {
        var canvas = field.GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
    }

    // ---------- menu ----------

    void ShowMenuForActiveField()
    {
        if (_activeField == null) return;
        EnsureOverlay();
        bool hasSelection = _activeField.selectionStringAnchorPosition != _activeField.selectionStringFocusPosition;
        var items = SelectionMenuPolicy.Visible(
            hasSelection,
            clipboardHasText: !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer),
            textLength: _activeField.text.Length,
            allSelected: hasSelection && SelectionSpan(_activeField) == _activeField.text.Length,
            readOnly: _activeField.readOnly);
        if (items == SelectionMenuItems.None) return;
        var (top, bottom) = SelectionScreenBounds(_activeField);
        _menu.Show(items, top, bottom, _activeField.textComponent.font);
    }

    void OnMenuItem(SelectionMenuItems item)
    {
        if (_activeField == null) return;
        var field = _activeField;
        int anchor = field.selectionStringAnchorPosition;
        int focus = field.selectionStringFocusPosition;

        switch (item)
        {
            case SelectionMenuItems.Copy:
                GUIUtility.systemCopyBuffer = SelectionActions.CopyText(field.text, anchor, focus);
                _menu.Hide();                        // iOS keeps the selection after Copy
                break;

            case SelectionMenuItems.Cut:
                GUIUtility.systemCopyBuffer = SelectionActions.CopyText(field.text, anchor, focus);
                ApplyEdit(field, SelectionActions.Cut(field.text, anchor, focus));
                break;

            case SelectionMenuItems.Paste:
                ApplyEdit(field, SelectionActions.Paste(
                    field.text, anchor, focus, GUIUtility.systemCopyBuffer, field.characterLimit));
                break;

            case SelectionMenuItems.SelectAll:
                field.selectionStringAnchorPosition = 0;
                field.selectionStringFocusPosition = field.text.Length;
                KeyboardSelectionSync.Push(field);
                SetIntent(field);
                RememberSelection(field);
                EnsureOverlay();
                _overlay.ShowHandles();
                ShowMenuForActiveField();
                break;
        }
    }

    void ApplyEdit(TMP_InputField field, SelectionEdit edit)
    {
        _applyingEdit = true;
        field.text = edit.NewText;                   // focused-field write-through (safe by invariant)
        field.stringPosition = edit.NewCaret;
        KeyboardSelectionSync.Push(field);
        _applyingEdit = false;
        _menu?.Hide();
        _overlay?.HideHandles();
        ClearIntent();
        RememberSelection(field);
    }

    /// While our pins/menu are engaged, the EventSystem must keep treating
    /// the active field as the selected object — a press on any overlay
    /// element deselects it (module behavior), and DeferredDismissInputField
    /// would then close the OS keyboard when its deferred dismissal fires.
    /// Re-selecting runs the field's OnSelect, which clears that pending
    /// dismissal; this component's execution order guarantees it happens
    /// before the dismissal check each frame. TMP skips re-activation while
    /// the field is still focused, so caret and selection are untouched.
    void EnforceOwnUiSelection()
    {
        if (_activeField == null || EventSystem.current == null) return;
        bool ownUiEngaged = _ownUiPressActive
            || (_menu != null && _menu.IsVisible)
            || (_overlay != null && _overlay.HandlesVisible);
        if (!ownUiEngaged) return;
        if (EventSystem.current.currentSelectedGameObject != _activeField.gameObject)
            EventSystem.current.SetSelectedGameObject(_activeField.gameObject);
    }

    // ---------- watching ----------

    void SetIntent(TMP_InputField field)
    {
        _intendedAnchor = field.selectionStringAnchorPosition;
        _intendedFocus = field.selectionStringFocusPosition;
        _intendedText = field.text;
    }

    void ClearIntent()
    {
        _intendedAnchor = -1;
        _intendedFocus = -1;
        _intendedText = null;
    }

    /// On iOS with the keyboard open, TMP overwrites its Unity-side selection
    /// from the native keyboard EVERY frame (UpdateStringPositionFromKeyboard)
    /// — a race that collapses a programmatic selection right after the
    /// finger lifts. While our pins are up, the router OWNS the selection:
    /// drift from the intended range with UNCHANGED text is clobber, not user
    /// input (typing changes the text and dismisses; taps dismiss in the same
    /// frame's pump, before this runs), so re-assert and re-push.
    void EnforceIntendedSelection()
    {
        if (_activeField == null || _intendedAnchor < 0) return;
        if (_overlay == null || !_overlay.HandlesVisible) return;
        if (_machine.IsPressed && !_machine.LongPressActive && !_ownUiPressActive) return;
        if (_activeField.text != _intendedText) return;   // real edit — lifecycle watcher dismisses
        if (_activeField.selectionStringAnchorPosition == _intendedAnchor &&
            _activeField.selectionStringFocusPosition == _intendedFocus) return;
        _activeField.selectionStringAnchorPosition = _intendedAnchor;
        _activeField.selectionStringFocusPosition = _intendedFocus;
        KeyboardSelectionSync.Push(_activeField);
        RememberSelection(_activeField);
    }

    /// A TMP-originated selection (e.g. drag-select in the composer) also
    /// deserves pins + menu, whatever gesture created it.
    void WatchExternalSelection()
    {
        if (_activeField == null || _machine.IsPressed || _applyingEdit) return;
        int anchor = _activeField.selectionStringAnchorPosition;
        int focus = _activeField.selectionStringFocusPosition;
        if (anchor == _lastAnchor && focus == _lastFocus) return;
        RememberSelection(_activeField);
        if (anchor != focus)
        {
            EnsureOverlay();
            _overlay.ShowHandles();
            SetIntent(_activeField);
            ShowMenuForActiveField();
        }
    }

    /// Selection UI lives only while its field is focused (focus loss also
    /// covers OS-keyboard dismissal and screen switches). Typing over a
    /// selection collapses it → dismiss.
    void WatchFieldLifecycle()
    {
        if (_activeField == null) return;
        if (!_activeField.isFocused) { DismissAll(); return; }
        if (_overlay != null && _overlay.HandlesVisible && SelectionSpan(_activeField) == 0)
            DismissAll();
    }

    void RememberSelection(TMP_InputField field)
    {
        _lastAnchor = field.selectionStringAnchorPosition;
        _lastFocus = field.selectionStringFocusPosition;
    }

    static int SelectionSpan(TMP_InputField field) =>
        Mathf.Abs(field.selectionStringFocusPosition - field.selectionStringAnchorPosition);

    // ---------- overlay plumbing ----------

    void EnsureOverlay()
    {
        if (_overlay != null) return;
        _overlay = SelectionOverlay.Create();
        _overlay.StartHandle.DragMoved += OnHandleDragged;
        _overlay.EndHandle.DragMoved += OnHandleDragged;
        _overlay.StartHandle.DragEnded += OnHandleDragEnded;
        _overlay.EndHandle.DragEnded += OnHandleDragEnded;
        _menu = SelectionMenuView.Build(_overlay.MenuRoot);
        _menu.ItemTapped += OnMenuItem;
    }

    void RepositionHandles()
    {
        var (startTop, startBottom, endTop, endBottom) = SelectionEdgeWorldCorners(_activeField);
        _overlay.PositionHandle(_overlay.StartHandle, startTop, startBottom);
        _overlay.PositionHandle(_overlay.EndHandle, endTop, endBottom);
    }

    (Vector3, Vector3, Vector3, Vector3) SelectionEdgeWorldCorners(TMP_InputField field)
    {
        var info = field.textComponent.textInfo;
        int lo = Mathf.Min(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
        int hi = Mathf.Max(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
        var textTransform = field.textComponent.transform;

        var (startX, startTopY, startBottomY) = CaretMetrics(info, lo, leftEdge: true);
        var (endX, endTopY, endBottomY) = CaretMetrics(info, hi, leftEdge: false);
        return (textTransform.TransformPoint(new Vector3(startX, startTopY)),
                textTransform.TransformPoint(new Vector3(startX, startBottomY)),
                textTransform.TransformPoint(new Vector3(endX, endTopY)),
                textTransform.TransformPoint(new Vector3(endX, endBottomY)));
    }

    static (float x, float top, float bottom) CaretMetrics(TMP_TextInfo info, int stringIndex, bool leftEdge)
    {
        if (info.characterCount == 0) return (0, 0, 0);
        int charIndex = 0;
        for (int i = 0; i < info.characterCount; i++)
        {
            charIndex = i;
            if (info.characterInfo[i].index >= stringIndex) break;
        }
        var characterInfo = info.characterInfo[charIndex];
        bool useRightEdge = !leftEdge && characterInfo.index < stringIndex;
        float x = useRightEdge ? characterInfo.xAdvance : characterInfo.origin;
        return (x, characterInfo.ascender, characterInfo.descender);
    }

    (Vector2 top, Vector2 bottom) SelectionScreenBounds(TMP_InputField field)
    {
        var (startTop, startBottom, endTop, endBottom) = SelectionEdgeWorldCorners(field);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, startTop);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(null, endTop);
        Vector2 c = RectTransformUtility.WorldToScreenPoint(null, startBottom);
        Vector2 d = RectTransformUtility.WorldToScreenPoint(null, endBottom);
        return (new Vector2((a.x + b.x) / 2f, Mathf.Max(a.y, b.y)),
                new Vector2((c.x + d.x) / 2f, Mathf.Min(c.y, d.y)));
    }

    // ---------- misc ----------

    void AutoScrollTowards(TMP_InputField field, Vector2 screenPos)
    {
        var scroll = field.textComponent.GetComponentInParent<ScrollRect>();
        if (scroll == null || !scroll.vertical) return;
        var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        var corners = new Vector3[4];
        viewport.GetWorldCorners(corners);
        float bottomY = RectTransformUtility.WorldToScreenPoint(null, corners[0]).y;
        float topY = RectTransformUtility.WorldToScreenPoint(null, corners[1]).y;
        const float bandPixels = 60f;
        const float speed = 1.2f;
        if (screenPos.y > topY - bandPixels)
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + speed * Time.unscaledDeltaTime);
        else if (screenPos.y < bottomY + bandPixels)
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - speed * Time.unscaledDeltaTime);
    }

    void ApplySelectionTint(TMP_InputField field)
    {
        var accent = Theme.Color(ThemeRole.AccentFill);
        field.selectionColor = new Color(accent.r, accent.g, accent.b, 0.25f);
    }

    void DismissAll()
    {
        _menu?.Hide();
        _overlay?.HideHandles();
        _menuPendingOnRelease = false;
        _pendingField = null;
        _activeField = null;
        _lastAnchor = -1;
        _lastFocus = -1;
        ClearIntent();
    }
}
