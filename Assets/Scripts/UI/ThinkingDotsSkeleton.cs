using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Self-animating "thinking" dots for a suggestion-loading skeleton card (D-12). Three dots pulse
/// (scale + fade) in sequence to signal the AI is composing replies. Tied to the skeleton's active
/// state: it animates in OnEnable (when the skeleton is shown) and kills its tweens in OnDisable.
/// Wired by SuggestionsPanelBuilder. No networking / no Plan-04 dependencies.
/// </summary>
public class ThinkingDotsSkeleton : MonoBehaviour
{
    [SerializeField] private Graphic[] dots;          // 3 dot graphics, left → right
    [SerializeField] private float period = 0.9f;     // one pulse cycle (seconds)
    [SerializeField] private float stagger = 0.15f;   // per-dot start delay (seconds)

    void OnEnable()
    {
        if (dots == null) return;
        float half = period * 0.5f;
        for (int i = 0; i < dots.Length; i++)
        {
            Graphic dot = dots[i];
            if (dot == null) continue;
            RectTransform t = dot.rectTransform;
            t.localScale = Vector3.one * 0.6f;
            SetAlpha(dot, 0.4f);
            float delay = i * stagger;
            t.DOScale(1f, half).SetDelay(delay).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
            dot.DOFade(1f, half).SetDelay(delay).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
        }
    }

    void OnDisable()
    {
        if (dots == null) return;
        foreach (Graphic dot in dots)
        {
            if (dot == null) continue;
            dot.rectTransform.DOKill();
            dot.DOKill();
            dot.rectTransform.localScale = Vector3.one;
            SetAlpha(dot, 1f);
        }
    }

    private static void SetAlpha(Graphic g, float a)
    {
        Color c = g.color; c.a = a; g.color = c;
    }
}
