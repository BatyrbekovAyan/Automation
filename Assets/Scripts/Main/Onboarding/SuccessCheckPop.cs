using DG.Tweening;
using UnityEngine;

/// <summary>
/// Plays a short celebratory pop (DOScale 0.9 → 1, OutBack) every time the object
/// is enabled. Attached by <c>OnboardingAuthBlocksBuilder</c> to the green check
/// disc inside each auth success sheet — the sheet is <c>SetActive(true)</c>'d on
/// every «Бот подключён!» moment, so <see cref="OnEnable"/> re-fires the pop each
/// time. Self-contained (no external refs, no Manager field) so the success moment
/// stays a scene-only concern.
/// </summary>
[DisallowMultipleComponent]
public class SuccessCheckPop : MonoBehaviour
{
    [SerializeField] private float startScale = 0.9f;
    [SerializeField] private float duration = 0.32f;

    private Tween _tween;

    private void OnEnable()
    {
        _tween?.Kill();
        transform.localScale = Vector3.one * startScale;
        _tween = transform
            .DOScale(1f, duration)
            .SetEase(Ease.OutBack)
            .SetLink(gameObject);
    }

    private void OnDisable()
    {
        _tween?.Kill();
        _tween = null;
        transform.localScale = Vector3.one;
    }
}
