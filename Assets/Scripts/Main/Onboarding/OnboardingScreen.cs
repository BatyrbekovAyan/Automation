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
    [SerializeField] private RectTransform[] dots;      // one per page; active dot = elongated Primary pill
    [SerializeField] private Button createBotButton;    // slide-3 «Создать бота»
    // Optional «Далее» buttons (advance the pager); builder wires them to pager.GoToPage.

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

    // Active dot = wider Primary #1B7CEB pill; inactive = short muted pill. The builder
    // bakes both a wide + narrow width; here just toggle a child/scale flag it wired.
    private void SetDotActive(RectTransform dot, bool isActive)
    {
        if (dot == null) return;
        var img = dot.GetComponent<Image>();
        if (img != null) img.color = isActive ? new Color(0.106f, 0.486f, 0.922f, 1f) /*#1B7CEB*/
                                              : new Color(0.106f, 0.486f, 0.922f, 0.30f);
        dot.localScale = isActive ? new Vector3(2.4f, 1f, 1f) : Vector3.one; // elongate active
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
