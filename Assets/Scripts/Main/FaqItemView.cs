using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One accordion row on Profile → Поддержка: a question button with a
/// rotating chevron and a height-animated answer container. Content is
/// stamped at runtime by ProfileSubPages.Support (single source of truth
/// for the FAQ copy); the builder only constructs the geometry.
/// </summary>
public class FaqItemView : MonoBehaviour
{
    [SerializeField] private Button questionButton;
    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private RectTransform chevron;
    [SerializeField] private TextMeshProUGUI answerText;
    [SerializeField] private LayoutElement answerLayout;

    private const float AnswerBottomPadding = 30f;
    private const float ExpandDuration = 0.25f;

    private Tween _heightTween;
    private Tween _chevronTween;

    public Button QuestionButton => questionButton;
    public bool IsOpen { get; private set; }

    // Laid-out width of the answer label. 0/near-0 until the panel has had a
    // real layout pass — callers measuring the expanded height must wait for
    // this to settle, else preferredHeight is computed at width ~0 (every word
    // wraps to its own line) and blows up to thousands of px.
    public float AnswerWidth => answerText != null ? answerText.rectTransform.rect.width : 0f;

    public void SetContent(string question, string answer)
    {
        if (questionText != null) questionText.text = question;
        if (answerText != null) answerText.text = answer;
    }

    public void SetExpanded(bool open, bool instant = false)
    {
        IsOpen = open;
        if (answerLayout == null || answerText == null) return;

        float target = 0f;
        if (open)
        {
            // The answer wraps to its container width, which is settled only
            // after a layout pass — force one before reading preferredHeight.
            // (Callers doing an instant expand on first open must first wait for
            // a real width; see ProfileSubPages.Support.ExpandFirstFaqWhenLaidOut.)
            LayoutRebuilder.ForceRebuildLayoutImmediate(answerText.rectTransform);
            target = answerText.preferredHeight + AnswerBottomPadding;
        }

        float chevronAngle = open ? 180f : 0f;

        _heightTween?.Kill();
        _chevronTween?.Kill();

        if (instant)
        {
            answerLayout.preferredHeight = target;
            if (chevron != null) chevron.localEulerAngles = new Vector3(0f, 0f, chevronAngle);
            return;
        }

        _heightTween = DOTween.To(
            () => answerLayout.preferredHeight,
            h => answerLayout.preferredHeight = h,
            target, ExpandDuration).SetEase(Ease.OutCubic);

        if (chevron != null)
            _chevronTween = chevron.DOLocalRotate(new Vector3(0f, 0f, chevronAngle), ExpandDuration)
                                   .SetEase(Ease.OutCubic);
    }
}
