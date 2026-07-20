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

    // D1: on the Telegram channel the WhatsApp bar's 😂/😮 are REACTION_INVALID on tapi, so the
    // quick 6 come from the Telegram-allowed set instead (both arrays are length 6, so the
    // index-keyed buttons/labels stay valid). Read at click/render time — never cached in the
    // Awake closure — so a channel switch is reflected without re-wiring the buttons.
    private string[] ActiveQuickEmojis =>
        ChatManager.Instance != null && ChatManager.Instance.ActiveChannel == ChatChannel.Telegram
            ? TelegramReactionCatalog.QuickEmojis
            : QuickEmojis;

    // Horizontal inset the floating bar/menu keep from the screen edges — matches the bubbles'.
    private const float EdgePadding = 40f;
    // Vertical gap between the pressed bubble and each floating panel (bar above, menu below).
    private const float PanelGap = 16f;
    // Inset the panels keep from the top/bottom of the overlay before they're considered off-screen.
    private const float VerticalLimitInset = 12f;

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
    private MessageItemView _sourceView;   // the pressed row, re-rendered one frame after Hide (D2-view / WR-01)
    private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[6];
    private Canvas _liftedCanvas;        // the pressed row's sort-override, raising it above the scrim
    private RectTransform _liftedRowRt;  // the pressed row, floated up when the menu would overflow
    private Vector2 _liftedRowOrigPos;   // its layout-driven position, restored on Hide
    private Vector2 _liftedRowTargetPos; // the floated-up position, re-pinned each frame while shown
    private bool _rowFloated;            // true while the row is held above its layout slot

    // Incoming bubbles sit on the left, outgoing on the right; the floating panels align to match.
    private bool IsTargetIncoming => _target != null && _target.isIncoming;

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < emojiButtons.Length && i < 6; i++)
        {
            int idx = i;
            _labels[i] = emojiButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            emojiButtons[i].onClick.AddListener(() => OnEmojiTapped(ActiveQuickEmojis[idx]));
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

    // Hold the floated-up row against the scroll content's VerticalLayoutGroup, which would
    // otherwise re-stamp it back into its slot on any relayout (e.g. a live message arriving)
    // while the overlay is open — same per-frame pin KeyboardAwarePanel uses.
    private void LateUpdate()
    {
        if (_rowFloated && _liftedRowRt != null)
            _liftedRowRt.anchoredPosition = _liftedRowTargetPos;
    }

    public void Show(MessageItemView source)
    {
        if (source == null || source.BoundVm == null || source.BubbleRect == null) return;
        _target = source.BoundVm;
        _sourceView = source;

        LiftRow(source);   // raise the pressed message above the dark scrim so it stays bright

        if (content != null) content.SetActive(true);
        RenderEmojiLabels();
        RefreshHighlight();

        // Rebuild both panels first so their real sizes are known before any positioning — the
        // float-up below needs the menu's height to know whether the menu would overflow.
        LayoutRebuilder.ForceRebuildLayoutImmediate(bar);
        if (actionMenu != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(actionMenu);

            // Refresh rounded corners now that ContentSizeFitter has given the card a real size.
            var menuRounded = actionMenu.GetComponent<ImageWithRoundedCorners>();
            if (menuRounded != null) { menuRounded.Validate(); menuRounded.Refresh(); }
        }

        // When the bubble sits too low for the menu to fit below it, float the whole pressed row
        // up (works even for the last message, where there's nothing below to scroll into view)
        // so the menu lands below with proper spacing instead of overlapping the bubble.
        ApplyLiftIfNeeded(source.BubbleRect);

        PositionBarOver(source.BubbleRect);
        if (actionMenu != null) PositionMenuUnderBubble(source.BubbleRect);

        bar.localScale = Vector3.one * 0.85f;
        bar.DOScale(1f, 0.18f).SetEase(Ease.OutBack);
    }

    public void Hide()
    {
        _target = null;
        UnliftRow();
        if (content != null) content.SetActive(false);

        // D2-view (08-REVIEW WR-01): the pill can render under the lifted overrideSorting Canvas
        // that UnliftRow just queued for deferred destroy. Re-render the pressed bubble ONE FRAME
        // later — after that Canvas is gone — so the mesh regenerates on the root canvas and the
        // stale pill self-heals. The data-layer dedup guard would otherwise swallow every future
        // reaction update (the VM data is already correct). Channel-agnostic + idempotent ⇒ WhatsApp unaffected.
        if (_sourceView != null) { StartCoroutine(RefreshSourceNextFrame(_sourceView)); _sourceView = null; }
    }

    private System.Collections.IEnumerator RefreshSourceNextFrame(MessageItemView view)
    {
        yield return null;                       // let the deferred Canvas destroy land
        if (view != null) view.RefreshReactionsVisual();
    }

    // Raises the long-pressed row's whole hierarchy above the dimming scrim (overrideSorting) so it
    // reads as highlighted; UnliftRow drops it back into the normal scroll-content order.
    private void LiftRow(MessageItemView source)
    {
        UnliftRow();
        if (source == null) return;
        var canvas = source.GetComponent<Canvas>();
        if (canvas == null) canvas = source.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 5;   // above the scrim (the overlay renders at order 0)
        _liftedCanvas = canvas;

        // Remember the row's layout-driven position so any float-up can be undone on Hide.
        _liftedRowRt = (RectTransform)source.transform;
        _liftedRowOrigPos = _liftedRowRt.anchoredPosition;
    }

    private void UnliftRow()
    {
        _rowFloated = false;
        if (_liftedRowRt != null)
        {
            _liftedRowRt.anchoredPosition = _liftedRowOrigPos;   // drop the row back into the list
            _liftedRowRt = null;
        }
        if (_liftedCanvas == null) return;
        // Destroy the Canvas (don't just disable overrideSorting): a leftover nested Canvas keeps
        // the row's graphics in its own registry, which the root GraphicRaycaster won't raycast —
        // leaving the row dead to taps/swipes/long-press. Destroying it re-registers the graphics
        // to the parent canvas. Deferred destroy lands within the frame, long before any re-press.
        Destroy(_liftedCanvas);
        _liftedCanvas = null;
    }

    /// <summary>
    /// Floats the pressed row up by just enough for the action menu to fit below the bubble when
    /// the bubble sits too low. The lift moves the row inside the (layout-driven) scroll content;
    /// LateUpdate re-pins it against any relayout while the overlay is open, and UnliftRow restores
    /// the original position on dismiss. The list content shares the canvas at scale 1 with the
    /// overlay, so a parent-local Y delta moves the row by the same amount.
    /// </summary>
    private void ApplyLiftIfNeeded(RectTransform bubble)
    {
        if (_liftedRowRt == null || actionMenu == null || bubble == null) return;

        RectTransform parentRt = (RectTransform)actionMenu.parent;
        Vector3[] corners = new Vector3[4];
        bubble.GetWorldCorners(corners);   // 0=BL 1=TL 2=TR 3=BR

        float topY = parentRt.InverseTransformPoint((corners[1] + corners[2]) * 0.5f).y;
        float botY = parentRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f).y;

        float topLimit = parentRt.rect.height * 0.5f - bar.rect.height * 0.5f - VerticalLimitInset;
        float botLimit = -parentRt.rect.height * 0.5f + actionMenu.rect.height * 0.5f + VerticalLimitInset;

        float lift = ReactionBarLayout.LiftToFitMenu(
            topY, botY, bar.rect.height, actionMenu.rect.height, PanelGap, topLimit, botLimit);
        if (lift <= 0.5f) return;

        _liftedRowTargetPos = new Vector2(_liftedRowOrigPos.x, _liftedRowOrigPos.y + lift);
        _liftedRowRt.anchoredPosition = _liftedRowTargetPos;
        _rowFloated = true;
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
        var quick = ActiveQuickEmojis;
        for (int i = 0; i < _labels.Length; i++)
            if (_labels[i] != null)
                _labels[i].text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(quick[i], MissingEmojiMode.Hide);
    }

    private void RefreshHighlight()
    {
        string mine = OutgoingReaction.CurrentMyEmoji(_target);
        var quick = ActiveQuickEmojis;
        for (int i = 0; i < emojiButtons.Length && i < 6; i++)
        {
            var img = emojiButtons[i].targetGraphic as Image;
            // VS16-insensitive compare (D2 root cause A) so a stored base ❤ still highlights the
            // qualified ❤️ quick-bar button.
            if (img != null) img.color = (mine != null && ReactionEmoji.SameEmoji(quick[i], mine)) ? selectedTint : normalTint;
        }
    }

    /// <summary>
    /// Places the quick-emoji bar just above the pressed bubble, edge-aligned to the message's
    /// side (left for incoming, right for outgoing) and clamped on-screen. Drops below the bubble
    /// only when there's no room above.
    /// </summary>
    private void PositionBarOver(RectTransform bubble)
    {
        RectTransform parentRt = (RectTransform)bar.parent;
        Vector3[] corners = new Vector3[4];
        bubble.GetWorldCorners(corners);                       // 0=BL 1=TL 2=TR 3=BR

        float topY = parentRt.InverseTransformPoint((corners[1] + corners[2]) * 0.5f).y;
        float y = topY + PanelGap + bar.rect.height * 0.5f;

        float topLimit = parentRt.rect.height * 0.5f - bar.rect.height * 0.5f - VerticalLimitInset;
        if (y > topLimit)   // not enough room above — drop below the bubble
        {
            float botY = parentRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f).y;
            y = botY - PanelGap - bar.rect.height * 0.5f;
        }

        float x = SideAlignedX(parentRt, corners, bar.rect.width);
        bar.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>
    /// Places the action menu just below the pressed bubble, edge-aligned to the message's side
    /// (so the narrow card lines up with the bubble instead of floating off-center). The bubble
    /// has already been floated up by ApplyLiftIfNeeded when needed, so the bottom clamp here is
    /// only a last-resort safety. corners[0]=BL, corners[3]=BR → bottom-center = (BL+BR)*0.5.
    /// </summary>
    private void PositionMenuUnderBubble(RectTransform bubble)
    {
        RectTransform parentRt = (RectTransform)actionMenu.parent;
        Vector3[] corners = new Vector3[4];
        bubble.GetWorldCorners(corners);   // 0=BL 1=TL 2=TR 3=BR

        float botY = parentRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f).y;
        float y = botY - PanelGap - actionMenu.rect.height * 0.5f;

        float botLimit = -parentRt.rect.height * 0.5f + actionMenu.rect.height * 0.5f + VerticalLimitInset;
        y = Mathf.Max(y, botLimit);

        float x = SideAlignedX(parentRt, corners, actionMenu.rect.width);
        actionMenu.anchoredPosition = new Vector2(x, y);
    }

    // Side-aligned anchored-x for a floating panel: its near edge meets the bubble's near edge
    // (left for incoming, right for outgoing), clamped on-screen. corners[0]=BL, corners[3]=BR.
    private float SideAlignedX(RectTransform parentRt, Vector3[] corners, float panelWidth)
    {
        float bubbleLeftX = parentRt.InverseTransformPoint(corners[0]).x;
        float bubbleRightX = parentRt.InverseTransformPoint(corners[3]).x;
        return ReactionBarLayout.SideAlignedCenterX(
            bubbleLeftX, bubbleRightX, panelWidth, parentRt.rect.width, EdgePadding, IsTargetIncoming);
    }

    private void HandleChatSelected(string chatId) => Hide();
    private void HandleEmojiReady(string spriteName) => RenderEmojiLabels();
}
