using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class QuickReplyPanel : MonoBehaviour
{
    [Header("Layout")]
    public float spacing = 8f;
    public float buttonHeight = 60f;
    public float padding = 10f;

    [Header("Arrow Icon")]
    public Sprite arrowRightSprite;

    [Header("Colors")]
    public Color outgoingBgColor = new Color(0.07f, 0.53f, 0.45f);      // WhatsApp teal
    public Color outgoingTextColor = Color.white;
    public Color outgoingArrowBgColor = new Color(0.05f, 0.45f, 0.38f); // Slightly darker teal
    public Color incomingBgColor = Color.white;
    public Color incomingTextColor = new Color(0.13f, 0.13f, 0.13f);
    public Color incomingArrowBgColor = new Color(0.92f, 0.92f, 0.92f); // Light gray

    [Header("Font")]
    public TMP_FontAsset font;
    public float fontSize = 16f;

    [Header("Testing")]
    [Tooltip("Enable to show dummy buttons on Start for testing without API.")]
    public bool showTestButtons = false;

    public event Action<string> OnQuickReplyClicked;
    public event Action<string> OnArrowClicked;

    private List<QuickReplyButton> _buttons = new();

    void Start()
    {
        if (showTestButtons)
        {
            SetReplies(new List<(string, bool)>
            {
                ("1:00 PM works for me", false),
                ("Any time works! What's most convenient for you?", true),
                ("I can't make it today, sorry", false),
                ("No, I'm busy this afternoon", false)
            });
        }
    }

    /// <summary>
    /// Call this with 4 reply options. Each item: (text, isOutgoing).
    /// isOutgoing = true gives the green/teal style, false gives white/light style.
    /// </summary>
    public void SetReplies(List<(string text, bool isOutgoing)> replies)
    {
        Clear();
        gameObject.SetActive(true);

        // The panel itself is a vertical layout: two rows
        // Each row is a horizontal layout: two buttons

        for (int row = 0; row < 2; row++)
        {
            GameObject rowGo = new GameObject($"Row_{row}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(transform, false);

            HorizontalLayoutGroup hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            LayoutElement rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.preferredHeight = buttonHeight;

            for (int col = 0; col < 2; col++)
            {
                int index = row * 2 + col;
                if (index >= replies.Count) break;

                var (text, isOutgoing) = replies[index];
                QuickReplyButton btn = CreateButton(rowGo.transform, text, isOutgoing);
                _buttons.Add(btn);
            }
        }
    }

    public void Clear()
    {
        foreach (var btn in _buttons)
        {
            if (btn != null) Destroy(btn.gameObject);
        }
        _buttons.Clear();

        // Destroy row containers
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
    }

    public void Hide()
    {
        Clear();
        gameObject.SetActive(false);
    }

    private QuickReplyButton CreateButton(Transform parent, string text, bool isOutgoing)
    {
        Color bgColor = isOutgoing ? outgoingBgColor : incomingBgColor;
        Color textColor = isOutgoing ? outgoingTextColor : incomingTextColor;
        Color arrowBgColor = isOutgoing ? outgoingArrowBgColor : incomingArrowBgColor;

        // --- Root button container ---
        GameObject root = new GameObject("QuickReplyBtn", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);

        Image rootBg = root.GetComponent<Image>();
        rootBg.color = bgColor;
        rootBg.type = Image.Type.Sliced;

        // Round corners via a mask if available, otherwise just solid color
        // Add LayoutElement so it stretches
        LayoutElement rootLe = root.AddComponent<LayoutElement>();
        rootLe.flexibleWidth = 1f;

        // Use a horizontal layout inside the button to split text (80%) and arrow (20%)
        HorizontalLayoutGroup innerLayout = root.AddComponent<HorizontalLayoutGroup>();
        innerLayout.spacing = 0;
        innerLayout.childForceExpandWidth = false;
        innerLayout.childForceExpandHeight = true;
        innerLayout.childControlWidth = true;
        innerLayout.childControlHeight = true;
        innerLayout.padding = new RectOffset(0, 0, 0, 0);

        // --- Main text button (80%) ---
        GameObject mainBtnGo = new GameObject("MainBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        mainBtnGo.transform.SetParent(root.transform, false);

        Image mainBtnImg = mainBtnGo.GetComponent<Image>();
        mainBtnImg.color = new Color(0, 0, 0, 0); // Transparent, bg comes from root

        Button mainBtn = mainBtnGo.GetComponent<Button>();
        mainBtn.transition = Selectable.Transition.None;

        LayoutElement mainLe = mainBtnGo.AddComponent<LayoutElement>();
        mainLe.flexibleWidth = 0.8f;

        // Text inside main button
        GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(mainBtnGo.transform, false);

        TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = textColor;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = new Vector4(10, 4, 4, 4);
        if (font != null) tmp.font = font;

        // Stretch text to fill the button
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        // --- Arrow button (20%) ---
        GameObject arrowBtnGo = new GameObject("ArrowBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        arrowBtnGo.transform.SetParent(root.transform, false);

        Image arrowBtnBg = arrowBtnGo.GetComponent<Image>();
        arrowBtnBg.color = arrowBgColor;

        Button arrowBtn = arrowBtnGo.GetComponent<Button>();
        arrowBtn.transition = Selectable.Transition.None;

        LayoutElement arrowLe = arrowBtnGo.AddComponent<LayoutElement>();
        arrowLe.flexibleWidth = 0.2f;

        // Arrow icon inside
        GameObject arrowIconGo = new GameObject("ArrowIcon", typeof(RectTransform), typeof(Image));
        arrowIconGo.transform.SetParent(arrowBtnGo.transform, false);

        Image arrowIcon = arrowIconGo.GetComponent<Image>();
        arrowIcon.color = textColor;
        arrowIcon.preserveAspect = true;
        if (arrowRightSprite != null) arrowIcon.sprite = arrowRightSprite;

        RectTransform arrowIconRt = arrowIconGo.GetComponent<RectTransform>();
        arrowIconRt.anchorMin = new Vector2(0.25f, 0.25f);
        arrowIconRt.anchorMax = new Vector2(0.75f, 0.75f);
        arrowIconRt.sizeDelta = Vector2.zero;
        arrowIconRt.offsetMin = Vector2.zero;
        arrowIconRt.offsetMax = Vector2.zero;

        // --- Wire up the QuickReplyButton component ---
        QuickReplyButton qrb = root.AddComponent<QuickReplyButton>();
        qrb.mainButton = mainBtn;
        qrb.arrowButton = arrowBtn;
        qrb.label = tmp;
        qrb.backgroundImage = rootBg;
        qrb.arrowImage = arrowIcon;

        qrb.Setup(text, bgColor, textColor, arrowBgColor);

        qrb.OnMainClicked += (t) => OnQuickReplyClicked?.Invoke(t);
        qrb.OnArrowClicked += (t) => OnArrowClicked?.Invoke(t);

        return qrb;
    }
}
