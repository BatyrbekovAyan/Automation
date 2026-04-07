using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class QuickReplyButton : MonoBehaviour
{
    [Header("References")]
    public Button mainButton;
    public Button arrowButton;
    public TextMeshProUGUI label;
    public Image backgroundImage;
    public Image arrowImage;

    public event Action<string> OnMainClicked;
    public event Action<string> OnArrowClicked;

    private string _text;

    public void Setup(string text, Color bgColor, Color textColor, Color arrowBgColor)
    {
        _text = text;
        label.text = text;
        backgroundImage.color = bgColor;
        label.color = textColor;
        arrowImage.color = textColor;

        // Arrow button background
        Image arrowBtnBg = arrowButton.GetComponent<Image>();
        if (arrowBtnBg != null) arrowBtnBg.color = arrowBgColor;

        mainButton.onClick.RemoveAllListeners();
        arrowButton.onClick.RemoveAllListeners();

        mainButton.onClick.AddListener(() => OnMainClicked?.Invoke(_text));
        arrowButton.onClick.AddListener(() => OnArrowClicked?.Invoke(_text));
    }

    public void SetText(string text)
    {
        _text = text;
        label.text = text;
    }
}
