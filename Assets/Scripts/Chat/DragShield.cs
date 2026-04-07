using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(Image))]
public class DragShield : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IInitializePotentialDragHandler
{
    public ScrollRect parentScrollRect;
    public TMP_InputField inputField;

    [Header("Tap Settings")]
    public float doubleTapThreshold = 0.3f;
    public float tapTimeThreshold = 0.25f;
    public float tapMoveTolerance = 15f;
    public float doubleTapPositionTolerance = 40f;

    private bool isDragging = false;
    private bool isPointerHeld = false; // Prevents hold re-fires from acting as taps
    private float pointerDownTime;
    private Vector2 pointerDownPosition;
    private float lastTapTime = -1f;
    private Vector2 lastTapPosition;

    void Start()
    {
        if (parentScrollRect == null)
            parentScrollRect = GetComponentInParent<ScrollRect>();

        GetComponent<Image>().raycastTarget = true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isPointerHeld) return; // Ignore re-fires while finger is held down

        isDragging = false;
        isPointerHeld = true;
        pointerDownTime = Time.unscaledTime;
        pointerDownPosition = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerHeld = false;

        if (isDragging) return;
        if (inputField == null) return;

        float tapDuration = Time.unscaledTime - pointerDownTime;
        float tapMovement = Vector2.Distance(eventData.position, pointerDownPosition);

        if (tapDuration >= tapTimeThreshold || tapMovement >= tapMoveTolerance) return;

        float timeSinceLastTap = Time.unscaledTime - lastTapTime;
        float distFromLastTap = Vector2.Distance(eventData.position, lastTapPosition);

        bool isDoubleTap = lastTapTime > 0f
                        && timeSinceLastTap <= doubleTapThreshold
                        && distFromLastTap <= doubleTapPositionTolerance;

        if (isDoubleTap)
        {
            lastTapTime = -1f;
            StartCoroutine(HandleDoubleTap(eventData));
        }
        else
        {
            lastTapTime = Time.unscaledTime;
            lastTapPosition = eventData.position;
            StartCoroutine(HandleSingleTap(eventData));
        }
    }

    IEnumerator HandleSingleTap(PointerEventData eventData)
    {
        // Calculate where the finger landed BEFORE any activation changes the layout
        int caretIndex = TMP_TextUtilities.GetCursorIndexFromPosition(
            inputField.textComponent,
            eventData.position,
            eventData.pressEventCamera
        );

        // Send down + up with NO yield between them so TMP's long-press
        // coroutine starts and is immediately cancelled in the same frame
        ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerClickHandler);

        // Wait for TMP to finish its internal caret-move-to-end call
        yield return null;
        yield return null;

        // Now safely place the caret exactly where the finger was
        inputField.caretPosition = caretIndex;
        inputField.selectionAnchorPosition = caretIndex;
        inputField.selectionFocusPosition = caretIndex;
        inputField.ForceLabelUpdate();
    }

IEnumerator HandleDoubleTap(PointerEventData eventData)
{
    if (inputField.textComponent == null) yield break;
    if (string.IsNullOrEmpty(inputField.text)) yield break;

    string text = inputField.text;
    TMP_Text textComp = inputField.textComponent;
    textComp.ForceMeshUpdate();
    TMP_TextInfo textInfo = textComp.textInfo;

    // --- STEP 1: Find which LINE was tapped using Y screen position ---
    // This is far more reliable than using the caret char index to infer the line,
    // because caret index at end-of-line points PAST the last word character.
    Vector2 localPoint;
    RectTransformUtility.ScreenPointToLocalPointInRectangle(
        textComp.rectTransform,
        eventData.position,
        eventData.pressEventCamera,
        out localPoint
    );

    int tappedLine = textInfo.lineCount - 1; // Default to last line
    for (int l = 0; l < textInfo.lineCount; l++)
    {
        TMP_LineInfo li = textInfo.lineInfo[l];
        if (localPoint.y <= li.baseline + li.ascender &&
            localPoint.y >= li.baseline + li.descender)
        {
            tappedLine = l;
            break;
        }
    }

    TMP_LineInfo lineInfo = textInfo.lineInfo[tappedLine];
    int lineFirst = lineInfo.firstCharacterIndex;
    int lineLast  = lineInfo.lastCharacterIndex;

    // Clamp lineLast so we never land on \n or trailing whitespace
    while (lineLast > lineFirst && (lineLast >= text.Length || IsWordBoundary(text[lineLast])))
        lineLast--;

    if (lineFirst >= text.Length || lineLast < lineFirst)
        yield break;

    // --- STEP 2: Find the closest non-boundary character on this line ---
    // Get caret index to know which side of the line we tapped
    int tapCaret = TMP_TextUtilities.GetCursorIndexFromPosition(
        textComp, eventData.position, eventData.pressEventCamera);

    int charIndex = Mathf.Clamp(tapCaret, lineFirst, lineLast);

    // If we landed on whitespace, search left first (natural for end-of-line taps)
    if (charIndex < text.Length && IsWordBoundary(text[charIndex]))
    {
        int left = charIndex - 1;
        while (left >= lineFirst && IsWordBoundary(text[left]))
            left--;

        int right = charIndex + 1;
        while (right <= lineLast && IsWordBoundary(text[right]))
            right++;

        bool leftValid  = left >= lineFirst  && !IsWordBoundary(text[left]);
        bool rightValid = right <= lineLast  && !IsWordBoundary(text[right]);

        if (leftValid && rightValid)
            // Pick whichever real character is closer to the tap
            charIndex = (charIndex - left <= right - charIndex) ? left : right;
        else if (leftValid)  charIndex = left;
        else if (rightValid) charIndex = right;
        else yield break; // Whole line is whitespace
    }

    // --- STEP 3: Walk to full word boundaries ---
    int wordStart = charIndex;
    while (wordStart > 0 && !IsWordBoundary(text[wordStart - 1]))
        wordStart--;

    int wordEnd = charIndex;
    while (wordEnd < text.Length && !IsWordBoundary(text[wordEnd]))
        wordEnd++;

    // Cancel TMP's long-press select-all in the same frame
    ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerEnterHandler);
    ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerDownHandler);
    ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerUpHandler);
    ExecuteEvents.Execute(inputField.gameObject, eventData, ExecuteEvents.pointerClickHandler);

    yield return null;
    yield return null;
    yield return null;

    if (wordEnd > wordStart)
    {
        inputField.selectionAnchorPosition = wordStart;
        inputField.selectionFocusPosition  = wordEnd;
        inputField.caretPosition           = wordEnd;
        inputField.ForceLabelUpdate();
    }
}
    bool IsWordBoundary(char c)
    {
        return c == ' ' || c == '\n' || c == '\t'
            || c == '.' || c == ',' || c == '!'
            || c == '?' || c == ';'  || c == ':';
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (parentScrollRect != null) parentScrollRect.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;
        if (parentScrollRect != null) parentScrollRect.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (parentScrollRect != null) parentScrollRect.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isPointerHeld = false;
        isDragging = false;
        if (parentScrollRect != null) parentScrollRect.OnEndDrag(eventData);
    }

    void ResetDrag() => isDragging = false;
}