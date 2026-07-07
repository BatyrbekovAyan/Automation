using TMPro;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Profile → Поддержка: static FAQ accordion + the «Написать в поддержку»
// bottom sheet. Sends go through Manager.SendToTelegram (chat id + bot token
// from secrets.json); the coroutine runs on Manager so closing the page
// mid-send can't kill it. FAQ copy lives here — edit strings, not the scene.
public partial class ProfileSubPages
{
    [Header("Support page")]
    [SerializeField] private FaqItemView[] faqItems;
    [SerializeField] private Button supportCtaButton;
    [SerializeField] private GameObject supportSheetRoot;
    [SerializeField] private CanvasGroup supportSheetBackdrop;
    [SerializeField] private Button supportBackdropButton;
    [SerializeField] private RectTransform supportSheetPanel;
    [SerializeField] private FocusedFieldKeyboardLift supportSheetKeyboard;
    [SerializeField] private TMP_InputField supportMessageInput;
    [SerializeField] private TMP_InputField supportContactInput;
    [SerializeField] private Button supportSendButton;
    [SerializeField] private TextMeshProUGUI supportSendLabel;

    private static readonly (string question, string answer)[] Faq =
    {
        ("Почему бот не отвечает?",
         "Проверьте переключатель на карточке бота — он должен быть зелёным («Бот работает»). Если бот на паузе, он не отвечает на новые сообщения, пока вы его не включите."),
        ("Как подключить WhatsApp?",
         "При создании бота выберите WhatsApp и отсканируйте QR-код с телефона, на котором установлен WhatsApp, — или введите код привязки."),
        ("Не приходит код подтверждения",
         "WhatsApp выдаёт повторный код не чаще, чем раз в 2 минуты. Подождите таймер под кнопкой и запросите код заново."),
        ("Что такое режим «Вместе»?",
         "Бот предлагает варианты ответа, а отправляете их вы. Удобно, пока вы ещё проверяете, как бот отвечает клиентам."),
        ("Как загрузить прайс-лист?",
         "В настройках бота, вкладка «Промпты» → «Прайс-листы». Подойдут Excel, Word, PDF, фото и другие форматы — бот сам разберёт цены."),
    };

    private bool _sendingSupport;

    private void WireSupport()
    {
        if (faqItems != null)
        {
            for (int i = 0; i < faqItems.Length; i++)
            {
                var item = faqItems[i];
                if (item == null) continue;
                if (i < Faq.Length) item.SetContent(Faq[i].question, Faq[i].answer);
                if (item.QuestionButton != null)
                {
                    var captured = item;
                    item.QuestionButton.onClick.AddListener(() => ToggleFaqItem(captured));
                }
            }
        }

        if (supportCtaButton != null) supportCtaButton.onClick.AddListener(OpenSupportSheet);
        if (supportBackdropButton != null) supportBackdropButton.onClick.AddListener(CloseSupportSheet);
        if (supportSendButton != null) supportSendButton.onClick.AddListener(OnSendSupport);
        if (supportMessageInput != null)
            supportMessageInput.onValueChanged.AddListener(_ => RefreshSendInteractable());
    }

    private void ToggleFaqItem(FaqItemView tapped)
    {
        bool opening = !tapped.IsOpen;
        foreach (var item in faqItems)
        {
            if (item == null) continue;
            if (item == tapped) item.SetExpanded(opening);
            else if (item.IsOpen) item.SetExpanded(false);
        }
    }

    // First question open by default — shows the rows expand (mockup 04).
    private void ResetFaq()
    {
        if (faqItems == null) return;
        for (int i = 0; i < faqItems.Length; i++)
            faqItems[i]?.SetExpanded(i == 0, instant: true);
    }

    // ── Bottom sheet ────────────────────────────────────────────────────────

    private void OpenSupportSheet()
    {
        if (supportSheetRoot == null || supportSheetPanel == null) return;

        supportSheetRoot.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(supportSheetPanel);

        // Keyboard tracking stays OFF during the slide — the lift component
        // re-stamps anchoredPosition.y every frame and would kill the tween.
        // Hand over once the sheet has settled at its resting position.
        if (supportSheetKeyboard != null) supportSheetKeyboard.enabled = false;

        if (supportSheetBackdrop != null)
        {
            supportSheetBackdrop.alpha = 0f;
            supportSheetBackdrop.DOFade(1f, 0.22f).SetEase(Ease.OutQuad);
        }

        float height = supportSheetPanel.rect.height;
        supportSheetPanel.anchoredPosition = new Vector2(0f, -height);
        supportSheetPanel.DOAnchorPosY(0f, 0.25f)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                if (supportSheetKeyboard != null) supportSheetKeyboard.enabled = true;
            });

        RefreshSendInteractable();
    }

    private void CloseSupportSheet()
    {
        if (supportSheetRoot == null || supportSheetPanel == null) return;

        // Reclaim the panel from keyboard tracking before animating out.
        if (supportSheetKeyboard != null) supportSheetKeyboard.enabled = false;

        supportSheetBackdrop?.DOFade(0f, 0.2f);
        supportSheetPanel.DOAnchorPosY(-supportSheetPanel.rect.height, 0.2f)
            .SetEase(Ease.InCubic)
            .OnComplete(() => supportSheetRoot.SetActive(false));
    }

    private void RefreshSendInteractable()
    {
        if (supportSendButton == null) return;
        bool hasText = supportMessageInput != null && !string.IsNullOrWhiteSpace(supportMessageInput.text);
        supportSendButton.interactable = hasText && !_sendingSupport;
    }

    private void OnSendSupport()
    {
        if (_sendingSupport || Manager.Instance == null) return;

        string composed = SupportMessageComposer.Compose(
            supportMessageInput != null ? supportMessageInput.text : "",
            supportContactInput != null ? supportContactInput.text : "",
            Application.version,
            Application.platform.ToString(),
            SystemInfo.deviceModel);
        if (string.IsNullOrEmpty(composed)) return;

        _sendingSupport = true;
        RefreshSendInteractable();
        if (supportSendLabel != null) supportSendLabel.text = "Отправка…";

        Manager.Instance.StartCoroutine(Manager.Instance.SendToTelegram(composed, OnSupportSendFinished));
    }

    private void OnSupportSendFinished(bool ok)
    {
        _sendingSupport = false;
        if (supportSendLabel != null) supportSendLabel.text = "Отправить";
        RefreshSendInteractable();

        if (ok)
        {
            if (supportMessageInput != null) supportMessageInput.text = "";
            if (supportContactInput != null) supportContactInput.text = "";
            CloseSupportSheet();
            ShowToast(PanelFor(Page.Support), "Отправлено! Мы свяжемся с вами");
        }
        else
        {
            // Draft stays in the sheet — nothing is lost on a failed send.
            ShowToast(PanelFor(Page.Support), "Не удалось отправить — проверьте интернет");
        }
    }
}
