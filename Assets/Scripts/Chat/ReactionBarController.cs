using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Nobi.UiRoundedCorners;

/// <summary>
/// Single shared overlay showing the quick-reaction bar above a long-pressed bubble.
/// Lives on the WhatsApp messages screen panel; its root stays active so the singleton
/// is always reachable while the scrim+bar 'content' is toggled. Tapping an emoji sends
/// via ChatManager; the bubble updates itself through OnMessageReactionsChanged.
/// </summary>
public class ReactionBarController : MonoBehaviour
{
    public static ReactionBarController Instance { get; private set; }

    // WhatsApp quick-reaction set (raw unicode; converted to TMP sprites at render).
    private static readonly string[] QuickEmojis = { "👍", "❤️", "😂", "😮", "😢", "🙏" };

    [Header("Overlay")]
    [SerializeField] private GameObject content;     // scrim + bar; toggled on show/hide
    [SerializeField] private Button scrimButton;     // full-panel dismiss
    [SerializeField] private RectTransform bar;      // the pill that floats over the bubble

    [Header("Buttons (6 quick + plus)")]
    [SerializeField] private Button[] emojiButtons;  // length 6, each with a TMP label child
    [SerializeField] private Button plusButton;

    [Header("Action menu (Reply / Copy / Forward)")]
    [SerializeField] private RectTransform actionMenu;   // vertical card below the bar
    [SerializeField] private Button replyAction;
    [SerializeField] private Button copyAction;
    [SerializeField] private Button forwardAction;

    [Header("Selected highlight")]
    [SerializeField] private Color selectedTint = new Color(0.85f, 0.92f, 1f, 1f);
    [SerializeField] private Color normalTint = Color.white;

    private MessageViewModel _target;
    private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[6];

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < emojiButtons.Length && i < 6; i++)
        {
            int idx = i;
            _labels[i] = emojiButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            emojiButtons[i].onClick.AddListener(() => OnEmojiTapped(QuickEmojis[idx]));
        }
        if (plusButton != null) plusButton.onClick.AddListener(OnPlusTapped);
        if (replyAction != null) replyAction.onClick.AddListener(OnReplyTapped);
        if (copyAction != null) copyAction.onClick.AddListener(OnCopyTapped);
        if (forwardAction != null) forwardAction.onClick.AddListener(OnForwardTapped);
        if (scrimButton != null) scrimButton.onClick.AddListener(Hide);
        if (content != null) content.SetActive(false);
    }

    private void OnEnable()
    {
        EmojiPatchService.OnEmojiReady += HandleEmojiReady;
        if (ChatManager.Instance != null) ChatManager.Instance.OnChatSelected += HandleChatSelected;
        SwipeToBack.OnSlideOutComplete += Hide;
        RenderEmojiLabels();
    }

    private void OnDisable()
    {
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        if (ChatManager.Instance != null) ChatManager.Instance.OnChatSelected -= HandleChatSelected;
        SwipeToBack.OnSlideOutComplete -= Hide;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Show(MessageItemView source)
    {
        if (source == null || source.BoundVm == null || source.BubbleRect == null) return;
        _target = source.BoundVm;

        if (content != null) content.SetActive(true);
        RenderEmojiLabels();
        RefreshHighlight();

        LayoutRebuilder.ForceRebuildLayoutImmediate(bar);   // bar size valid before positioning
        PositionBarOver(source.BubbleRect);

        if (actionMenu != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(actionMenu);

            // Fix 3: refresh rounded corners now that ContentSizeFitter has given the card a real size.
            var menuRounded = actionMenu.GetComponent<ImageWithRoundedCorners>();
            if (menuRounded != null) { menuRounded.Validate(); menuRounded.Refresh(); }

            // Fix 2: position below the pressed bubble (WhatsApp-style), not below the bar.
            PositionMenuUnderBubble(source.BubbleRect);
        }

        bar.localScale = Vector3.one * 0.85f;
        bar.DOScale(1f, 0.18f).SetEase(Ease.OutBack);
    }

    public void Hide()
    {
        _target = null;
        if (content != null) content.SetActive(false);
    }

    private void OnEmojiTapped(string emoji)
    {
        if (_target != null) ChatManager.Instance?.SendReaction(_target, emoji);
        Hide();
    }

    private void OnPlusTapped()
    {
        var target = _target;   // Hide() nulls _target — capture before closing the bar
        Hide();
        if (target != null) EmojiPickerController.Instance?.Show(target);
    }

    private void OnReplyTapped()
    {
        var target = _target;   // Hide() nulls _target — capture before closing the bar
        Hide();
        if (target != null) ChatManager.Instance?.BeginReply(target);
    }

    private void OnCopyTapped()
    {
        var target = _target;
        Hide();
        if (target != null && !string.IsNullOrEmpty(target.text))
            GUIUtility.systemCopyBuffer = target.text;
    }

    private void OnForwardTapped()
    {
        Hide();
        Debug.Log("[ReactionBar] Forward — coming soon");
    }

    private void RenderEmojiLabels()
    {
        for (int i = 0; i < _labels.Length; i++)
            if (_labels[i] != null)
                _labels[i].text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(QuickEmojis[i], MissingEmojiMode.Hide);
    }

    private void RefreshHighlight()
    {
        string mine = OutgoingReaction.CurrentMyEmoji(_target);
        for (int i = 0; i < emojiButtons.Length && i < 6; i++)
        {
            var img = emojiButtons[i].targetGraphic as Image;
            if (img != null) img.color = (mine != null && QuickEmojis[i] == mine) ? selectedTint : normalTint;
        }
    }

    private void PositionBarOver(RectTransform bubble)
    {
        RectTransform parentRt = (RectTransform)bar.parent;
        Vector3[] corners = new Vector3[4];
        bubble.GetWorldCorners(corners);                       // 0=BL 1=TL 2=TR 3=BR

        Vector2 topLocal = (Vector2)parentRt.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
        float gap = 16f;
        float y = topLocal.y + gap + bar.rect.height * 0.5f;

        float halfBarW = bar.rect.width * 0.5f;
        float maxX = parentRt.rect.width * 0.5f - halfBarW - 12f;
        float x = Mathf.Clamp(topLocal.x, -maxX, maxX);

        float topLimit = parentRt.rect.height * 0.5f - bar.rect.height * 0.5f - 12f;
        if (y > topLimit)   // not enough room above — drop below the bubble
        {
            Vector2 botLocal = (Vector2)parentRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
            y = botLocal.y - gap - bar.rect.height * 0.5f;
        }

        bar.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>
    /// Places the action menu just below the pressed bubble, horizontally centered on it
    /// and clamped to stay on-screen — mirrors PositionBarOver's approach.
    /// corners[0]=BL, corners[3]=BR → bottom-center = (corners[0]+corners[3])*0.5
    /// </summary>
    private void PositionMenuUnderBubble(RectTransform bubble)
    {
        RectTransform parentRt = (RectTransform)actionMenu.parent;
        Vector3[] corners = new Vector3[4];
        bubble.GetWorldCorners(corners);   // 0=BL 1=TL 2=TR 3=BR

        Vector2 botLocal = (Vector2)parentRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
        const float gap = 16f;
        float y = botLocal.y - gap - actionMenu.rect.height * 0.5f;

        float halfMenuW = actionMenu.rect.width * 0.5f;
        float maxX = parentRt.rect.width * 0.5f - halfMenuW - 12f;
        float x = Mathf.Clamp(botLocal.x, -maxX, maxX);

        // Clamp so the menu never goes below the bottom of the overlay.
        float botLimit = -parentRt.rect.height * 0.5f + actionMenu.rect.height * 0.5f + 12f;
        y = Mathf.Max(y, botLimit);

        actionMenu.anchoredPosition = new Vector2(x, y);
    }

    private void HandleChatSelected(string chatId) => Hide();
    private void HandleEmojiReady(string spriteName) => RenderEmojiLabels();
}
