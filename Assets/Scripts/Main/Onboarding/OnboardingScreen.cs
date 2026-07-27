using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controller for Screen_Onboarding (the 3-slide welcome carousel). Binds pager
/// page changes to the dot pills; the slide-3 CTA sets OnboardingSeen and hands
/// off to the existing AddBotPanel wizard. No bypass affordance (CONTEXT: informative
/// slides advance only via «Далее»/«Создать бота»).
/// </summary>
public class OnboardingScreen : MonoBehaviour
{
    [SerializeField] private OnboardingPager pager;
    [SerializeField] private RectTransform[] dots;      // one per page; active dot = wider Primary pill
    [SerializeField] private Button createBotButton;    // slide-3 «Создать бота»
    // Optional «Далее» buttons (advance the pager); builder wires them to pager.GoToPage.

    // Dot metrics (builder builds each dot at DotSize×DotSize with corner radius DotSize/2).
    // All dots stay uniform circles — active/inactive differ by FILL only (owner decision
    // 2026-07-24). Never widen or localScale a dot: both distort the rounded caps.
    private const float DotSize = 28f;
    private static readonly Color DotActive = new Color(0.106f, 0.486f, 0.922f, 1f);    // #1B7CEB
    private static readonly Color DotInactive = new Color(0.106f, 0.486f, 0.922f, 0.30f);

    private void OnEnable()
    {
        if (pager != null)
        {
            pager.OnPageChanged += UpdateDots;
            UpdateDots(pager.CurrentPage);
        }
    }

    private void OnDisable()
    {
        if (pager != null) pager.OnPageChanged -= UpdateDots;
    }

    private void Start()
    {
        if (createBotButton != null) createBotButton.onClick.AddListener(OnCreateBotTapped);
    }

    private void UpdateDots(int page)
    {
        if (dots == null) return;
        for (int i = 0; i < dots.Length; i++)
            SetDotActive(dots[i], i == page); // elongate/tint active per builder-baked visuals
    }

    // Active dot = solid Primary #1B7CEB circle; inactive = the same circle at 30% alpha.
    // Size and scale are pinned uniform so the dot never renders as a stretched oval.
    private void SetDotActive(RectTransform dot, bool isActive)
    {
        if (dot == null) return;
        var img = dot.GetComponent<Image>();
        if (img != null) img.color = isActive ? DotActive : DotInactive;
        dot.localScale = Vector3.one;
        dot.sizeDelta = new Vector2(DotSize, DotSize);
    }

    /// <summary>Slide-3 «Создать бота»: flag onboarding seen and open the existing wizard.</summary>
    public void OnCreateBotTapped()
    {
        PlayerPrefs.SetInt(OnboardingKeys.Seen, 1);
        PlayerPrefs.Save();
        gameObject.SetActive(false);          // hide Screen_Onboarding
        BotsPage.Instance?.StartNewBot();     // existing path → AddBotPanel.Instance.Open()
    }
}
