using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class AttachSheet : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Sheet height in canvas pixels at the 1080×2400 reference resolution.")]
    [SerializeField] private float sheetHeightCanvasPx = 700f;

    [Header("References")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private GameObject backdrop;
    [SerializeField] private Button backdropButton;
    [SerializeField] private CanvasGroup backdropGroup;
    [SerializeField] private Button cameraButton;
    [SerializeField] private Button galleryButton;
    [SerializeField] private Button documentButton;

    [Header("Tween Timings")]
    [SerializeField] private float openDuration  = 0.30f;
    [SerializeField] private float closeDuration = 0.25f;

    public event Action<AttachmentPick> OnPicked;

    /// <summary>True while the sheet is up — the suggestions slot yields to it (slot exclusivity).</summary>
    public bool IsOpen => _isOpen;

    // The sheet shares the screen's bottom region with the native keyboard and the suggestions
    // panel, and opening it collapses whichever of those held that region. Below this much
    // remaining rise the composer is visually home and the sheet may start up; the timeout is a
    // safety net, not a schedule — if something holds the region open, «+» must still respond.
    private const float ComposerSettledCanvasPx  = 8f;
    private const float MaxComposerSettleSeconds = 0.6f;

    private RectTransform     _rt;
    private bool              _isOpen;
    private Tween             _slideTween;
    private Tween             _fadeTween;
    private KeyboardAwarePanel _composerMover;   // owns the composer's rise; read-only from here
    private Coroutine         _pendingRise;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        // Found through the field rather than serialized: the mover lives on MovingArea, which is
        // the composer's ancestor but not this sheet's, and adding a scene reference would mean
        // re-running a builder over a hand-tuned scene for a value that is already reachable.
        _composerMover = inputField != null ? inputField.GetComponentInParent<KeyboardAwarePanel>() : null;

        OnPicked += pick =>
            Debug.Log($"[AttachSheet] OnPicked: kind={pick.Kind} file={pick.FileName} " +
                      $"size={pick.FileSizeBytes} mime={pick.MimeType} path={pick.Path}");
    }

    void OnEnable()
    {
        if (cameraButton   != null) cameraButton.onClick.AddListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.AddListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.AddListener(OnDocumentTapped);
        if (backdropButton != null) backdropButton.onClick.AddListener(Close);
    }

    void OnDisable()
    {
        if (cameraButton   != null) cameraButton.onClick.RemoveListener(OnCameraTapped);
        if (galleryButton  != null) galleryButton.onClick.RemoveListener(OnGalleryTapped);
        if (documentButton != null) documentButton.onClick.RemoveListener(OnDocumentTapped);
        if (backdropButton != null) backdropButton.onClick.RemoveListener(Close);

        StopPendingRise();
        _slideTween?.Kill();
        _fadeTween?.Kill();
        if (backdrop != null) backdrop.SetActive(false);

        // End the open the way Close()'s completion would have. This teardown is reached by
        // ANCESTOR deactivation (a back-swipe kills MessagesPanel), so this component's own
        // activeSelf stays true and it returns with the next chat — but the close tween that
        // normally resets all of this has just been killed above. Leaving _isOpen true then
        // strands the whole bottom region: SuggestionsController reads IsOpen as «the sheet owns
        // the slot», so its show parks forever and the suggestions panel never appears again for
        // the rest of the session. Invisible, too — the sheet is parked off-screen, so there is
        // nothing to dismiss.
        _isOpen = false;
        if (_rt != null) _rt.anchoredPosition = new Vector2(0f, -sheetHeightCanvasPx);
    }

    public void Toggle()
    {
        if (_isOpen) Close();
        else         Open();
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        // Decouple from input field: always dismiss the keyboard. iOS animates
        // the slide-down naturally; KeyboardAwarePanel's rawKb tracking drops
        // the input bar to the base by itself.
        //
        // ReleaseSelection is what stops the caret. `Reset On Deactivation` is
        // off on every input in this project, and with it off TMP's
        // DeactivateInputField sets m_SelectionStillActive = true and
        // deliberately skips the release. OnFillVBO's guard is `if (!isFocused
        // && !m_SelectionStillActive) return empty`, so the composer went on
        // re-emitting its caret quad at the last position on every canvas
        // rebuild — a static ghost caret sitting behind the sheet and still
        // there after it dismisses, until the composer is tapped again (real
        // focus) or the chat screen is hidden. The equivalent release in
        // DeferredDismissInputField.OnDisable cannot cover this path: nothing
        // is SetActive(false) here.
        //
        // Order matters — ReleaseSelection AFTER DeactivateInputField, which
        // re-sets the flag on its way out (same rule as SilentCaretStop).
        // ReleaseSelection also raises onEndEdit; the composer has no
        // onEndEdit listener (the project's only one is
        // EditableField.HandleEndEdit, on Bot Settings fields), so nothing is
        // committed here — check that still holds before adding one.
        if (inputField != null)
        {
            inputField.DeactivateInputField();
            inputField.ReleaseSelection();
        }
#if UNITY_EDITOR
        // Editor parity. On device the OS keyboard's own retreat is what drops the rise, but in the
        // Editor the height comes from KeyboardAwarePanel's SIMULATED flag, and the only other
        // place that clears it is SuggestionsController's dismissal pair — which this path does not
        // go through. Without this the rise never drains in the Editor and every «+» waits out the
        // full settle budget before opening.
        if (_composerMover != null) _composerMover.SetSimulatedKeyboard(false);
#endif

        gameObject.SetActive(true);
        if (backdrop != null) backdrop.SetActive(true);
        // The backdrop dims IMMEDIATELY, deliberately ahead of the sheet: it is what acknowledges
        // the tap, so «+» stays responsive while the composer is still on its way down.
        FadeBackdrop(1f, openDuration);

        // Sheet parks below the canvas. Width is held by the pre-existing anchors (built by
        // AttachSheetBuilder).
        _rt.sizeDelta        = new Vector2(_rt.sizeDelta.x, sheetHeightCanvasPx);
        _rt.anchoredPosition = new Vector2(0f, -sheetHeightCanvasPx);

        _slideTween?.Kill();
        StopPendingRise();
        _pendingRise = StartCoroutine(RiseWhenComposerHasSettled());
    }

    /// <summary>
    /// Start the sheet up only once the composer has finished coming DOWN. Opening the sheet
    /// evicts whoever held the bottom region — the keyboard (dismissed above) or the suggestions
    /// panel (SuggestionsController polls <see cref="IsOpen"/> and collapses its slot) — and both
    /// take a beat to drain. Riding up THROUGH a composer that is still falling reads as two
    /// surfaces fighting over the same space, which is what this sequencing removes.
    /// With nothing raised the wait falls through in the same frame, so the ordinary open is
    /// unchanged.
    /// </summary>
    private IEnumerator RiseWhenComposerHasSettled()
    {
        float deadline = Time.unscaledTime + MaxComposerSettleSeconds;
        while (ComposerStillRaised && Time.unscaledTime < deadline)
            yield return null;

        _pendingRise = null;
        // Kill by TARGET, not just the tracked handle. While the rise waits there is no tween on
        // the rect, so SheetDragDismiss's at-rest guard (DOTween.IsTweening) lets the user grab the
        // parked sheet; its snap-back is its own DOAnchorPosY on the same RectTransform, which
        // _slideTween does not reference and could therefore write the same y concurrently.
        DOTween.Kill(_rt);
        // SetTarget links the lambda tween to the RectTransform so SheetDragDismiss's
        // DOTween.IsTweening guard can see it.
        _slideTween = DOTween.To(
                () => _rt.anchoredPosition.y,
                v  => _rt.anchoredPosition = new Vector2(0f, v),
                0f,
                openDuration)
            .SetEase(Ease.OutCubic)
            .SetTarget(_rt);
    }

    // An unwired mover reads as «nothing is raised», so the sheet opens exactly as it did before
    // this sequencing existed rather than stalling for the timeout.
    private bool ComposerStillRaised =>
        _composerMover != null && _composerMover.AppliedBottomInset > ComposerSettledCanvasPx;

    private void StopPendingRise()
    {
        if (_pendingRise == null) return;
        StopCoroutine(_pendingRise);
        _pendingRise = null;
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        FadeBackdrop(0f, closeDuration);

        // A close can land while the rise is still waiting for the composer (a second «+» tap, the
        // ✦ key, a backdrop tap). Drop the wait or the sheet would slide up after being closed.
        StopPendingRise();
        _slideTween?.Kill();
        float startY = _rt.anchoredPosition.y;
        _slideTween = DOTween.To(
                () => _rt.anchoredPosition.y,
                v  => _rt.anchoredPosition = new Vector2(0f, v),
                -sheetHeightCanvasPx,
                closeDuration)
            .From(startY)
            .SetEase(Ease.OutCubic)
            .SetTarget(_rt)
            .OnComplete(() =>
            {
                gameObject.SetActive(false);
                if (backdrop != null) backdrop.SetActive(false);
            });
    }

    private void FadeBackdrop(float targetAlpha, float duration)
    {
        if (backdropGroup == null) return;
        _fadeTween?.Kill();
        _fadeTween = backdropGroup.DOFade(targetAlpha, duration).SetEase(Ease.Linear);
    }

    // ── Tile actions ────────────────────────────────────────────────

    private void OnCameraTapped()
    {
        if (NativeCamera.IsCameraBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeCamera.TakePicture(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                EmitPick(AttachmentKind.Photo, path);
            }, maxSize: 2048));
    }

    private void OnGalleryTapped()
    {
        if (NativeGallery.IsMediaPickerBusy()) return;
        Close();
        InvokeAfterClose(() =>
            NativeGallery.GetMixedMediaFromGallery(path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    EmitPick(AttachmentTypeUtil.GalleryKindFromPath(path), path);
                },
                NativeGallery.MediaType.Image | NativeGallery.MediaType.Video,
                "Select a photo or video"));
    }

    private void OnDocumentTapped()
    {
        Close();
        InvokeAfterClose(() =>
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                EmitPick(AttachmentKind.Document, path);
            }));
    }

    private void EmitPick(AttachmentKind kind, string path)
    {
        long size = 0;
        try { if (System.IO.File.Exists(path)) size = new System.IO.FileInfo(path).Length; }
        catch { size = 0; }

        var pick = new AttachmentPick
        {
            Kind          = kind,
            Path          = path,
            FileName      = System.IO.Path.GetFileName(path),
            MimeType      = AttachmentTypeUtil.MimeFromExtension(path),
            FileSizeBytes = size
        };
        OnPicked?.Invoke(pick);
    }

    private void InvokeAfterClose(Action action)
    {
        // Host the coroutine on the input field so the call survives Close()'s
        // SetActive(false) on this GameObject.
        MonoBehaviour host = inputField != null ? (MonoBehaviour)inputField : null;
        if (host != null) host.StartCoroutine(InvokeAfterCloseRoutine(action));
        else              action?.Invoke();
    }

    private IEnumerator InvokeAfterCloseRoutine(Action action)
    {
        // Wait one frame so Close's tween OnComplete (which SetActive(false)s
        // this GameObject) has landed before the native picker is invoked.
        yield return null;
        action?.Invoke();
    }
}
