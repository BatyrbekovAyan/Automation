using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Fires an action on true finger release, filtering out spurious PointerUp
/// events dispatched by InputSystemUIInputModule on iOS/Android mid-gesture.
///
/// The Input System occasionally emits a PointerUp followed one frame later
/// by a PointerDown while the user is continuously holding — typically when
/// a TMP_InputField loses focus and the on-screen keyboard dismisses, which
/// briefly interrupts touch delivery. A naive PointerUp listener would treat
/// that transient event as a real release.
///
/// This handler defers the action for <see cref="guardFrames"/> frames after
/// PointerUp. If a PointerDown arrives inside the guard window, the up was
/// spurious and is discarded. Only a release with no follow-up press inside
/// the window — and with the release position still inside the target rect —
/// invokes the subscribed action.
/// </summary>
[DisallowMultipleComponent]
public class DelayedFingerUpAction : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public event Action OnRealRelease;
    /// <summary>
    /// Fires synchronously inside OnPointerDown — before any guard logic.
    /// Lets owners observe the moment the press lands so they can record
    /// surrounding state (e.g. "was the keyboard already dismissing when
    /// this press arrived?") to discriminate genuine presses from synthetic
    /// PointerDowns that iOS dispatches when it cancels a touch session
    /// during keyboard dismissal and re-targets to a different object.
    /// </summary>
    public event Action OnPress;

    [Tooltip("Frames to wait after PointerUp before firing. Must be >= 1 so a " +
             "spurious Down arriving the next frame can cancel the action.")]
    [SerializeField] private int guardFrames = 2;

    private bool fingerDown;
    private RectTransform rt;

    private void Awake() => rt = transform as RectTransform;

    public void OnPointerDown(PointerEventData eventData)
    {
        fingerDown = true;
        OnPress?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!fingerDown) return;
        fingerDown = false;
        StartCoroutine(ConfirmRelease(eventData.position, eventData.pressEventCamera));
    }

    private IEnumerator ConfirmRelease(Vector2 releasePosition, Camera pressCamera)
    {
        for (int i = 0; i < guardFrames; i++) yield return null;

        // A PointerDown inside the guard window means the prior PointerUp was
        // part of a spurious Up/Down cycle — swallow it.
        if (fingerDown) yield break;

        if (rt == null) yield break;
        if (!RectTransformUtility.RectangleContainsScreenPoint(rt, releasePosition, pressCamera)) yield break;

        OnRealRelease?.Invoke();
    }
}
