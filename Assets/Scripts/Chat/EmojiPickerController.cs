using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Bottom-sheet emoji picker opened from the reaction bar's "+". On first show it builds
/// one section (header + grid) per ReactionEmojiCatalog category into a vertical list;
/// tapping a cell sends that reaction via ChatManager and closes. Missing sprites are
/// auto-requested by UnicodeEmojiConverter and filled in on EmojiPatchService.OnEmojiReady.
/// Lives on the messages screen panel; its root stays active while the Content child toggles.
/// </summary>
public class EmojiPickerController : MonoBehaviour
{
    public static EmojiPickerController Instance { get; private set; }

    [Header("Overlay")]
    [SerializeField] private GameObject content;        // scrim + sheet; toggled on show/hide
    [SerializeField] private Button scrimButton;        // tap-out dismiss
    [SerializeField] private RectTransform sheet;       // bottom sheet (slide-up anim)
    [SerializeField] private RectTransform listContent; // VerticalLayoutGroup container the sections are added to

    [Header("Grid")]
    [SerializeField] private float cellSize = 130f;
    [SerializeField] private float cellSpacing = 12f;
    [SerializeField] private int columns = 7;
    [SerializeField] private float cellFontSize = 64f;

    [Header("Section header")]
    [SerializeField] private float headerHeight = 64f;
    [SerializeField] private float headerFontSize = 32f;
    [SerializeField] private Color headerColor = new Color(0.45f, 0.45f, 0.48f);

    private MessageViewModel _target;
    private readonly List<TextMeshProUGUI> _cellLabels = new List<TextMeshProUGUI>();
    private readonly List<string> _cellEmojis = new List<string>();
    private bool _built;
    private ChatChannel _builtForChannel;   // the channel the current grid was built for

    private void Awake()
    {
        Instance = this;
        if (content != null) content.SetActive(false);
    }

    private void OnEnable()
    {
        EmojiPatchService.OnEmojiReady += HandleEmojiReady;
        if (ChatManager.Instance != null) ChatManager.Instance.OnChatSelected += HandleChatSelected;
        SwipeToBack.OnSlideOutComplete += Hide;
    }

    private void OnDisable()
    {
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        if (ChatManager.Instance != null) ChatManager.Instance.OnChatSelected -= HandleChatSelected;
        SwipeToBack.OnSlideOutComplete -= Hide;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Show(MessageViewModel target)
    {
        if (target == null) return;
        _target = target;

        BuildSectionsIfNeeded();
        RenderCells();
        if (content != null) content.SetActive(true);

        if (sheet != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
            sheet.anchoredPosition = new Vector2(sheet.anchoredPosition.x, -sheet.rect.height);
            sheet.DOAnchorPosY(0f, 0.28f).SetEase(Ease.OutCubic);
        }
    }

    public void Hide()
    {
        _target = null;
        if (content != null) content.SetActive(false);
    }

    private void BuildSectionsIfNeeded()
    {
        if (listContent == null) return;

        // D1: on Telegram the picker offers only tapi-allowed emoji; WhatsApp keeps the full
        // catalog. Rebuild when the channel differs from the grid we cached so the picker is
        // never a stale WhatsApp grid on Telegram (or vice-versa).
        ChatChannel channel = ChatManager.Instance != null
            ? ChatManager.Instance.ActiveChannel
            : ChatChannel.WhatsApp;
        if (_built && _builtForChannel == channel) return;

        if (_built) TearDownSections();

        IReadOnlyList<ReactionEmojiCatalog.Category> categories =
            channel == ChatChannel.Telegram
                ? TelegramReactionCatalog.FilterCategories()
                : ReactionEmojiCatalog.Categories;

        foreach (var category in categories)
        {
            BuildHeader(category.Name);
            BuildGrid(category.Emojis);
        }
        _built = true;
        _builtForChannel = channel;
    }

    // Detach + destroy the current sections so a channel switch rebuilds from scratch. Detach
    // this frame (SetParent(null)) so the deferred Destroy can't leave a stale grid in the
    // layout for a frame; the cell trackers are cleared to match.
    private void TearDownSections()
    {
        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            var child = listContent.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
        _cellLabels.Clear();
        _cellEmojis.Clear();
        _built = false;
    }

    private void BuildHeader(string text)
    {
        var go = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(listContent, false);
        go.GetComponent<LayoutElement>().preferredHeight = headerHeight;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = headerFontSize;
        tmp.color = headerColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
    }

    private void BuildGrid(string[] emojis)
    {
        var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        gridGo.transform.SetParent(listContent, false);

        var grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cellSize, cellSize);
        grid.spacing = new Vector2(cellSpacing, cellSpacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);
        grid.childAlignment = TextAnchor.UpperCenter;

        foreach (var emoji in emojis)
            BuildCell(gridGo.transform, emoji);
    }

    private void BuildCell(Transform parent, string emoji)
    {
        string captured = emoji;   // per-cell capture for the click closure

        var cell = new GameObject("Cell", typeof(RectTransform), typeof(Image), typeof(Button));
        cell.transform.SetParent(parent, false);

        var bg = cell.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);   // invisible, the whole cell is tappable
        bg.raycastTarget = true;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(cell.transform, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = cellFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        _cellLabels.Add(tmp);
        _cellEmojis.Add(captured);

        var btn = cell.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
        btn.onClick.AddListener(() => OnEmojiTapped(captured));
    }

    private void RenderCells()
    {
        for (int i = 0; i < _cellLabels.Count && i < _cellEmojis.Count; i++)
            if (_cellLabels[i] != null)
                _cellLabels[i].text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(_cellEmojis[i], MissingEmojiMode.Hide);
    }

    private void OnEmojiTapped(string emoji)
    {
        if (_target != null) ChatManager.Instance?.SendReaction(_target, emoji);
        Hide();
    }

    private void HandleChatSelected(string chatId) => Hide();
    private void HandleEmojiReady(string spriteName) { if (content != null && content.activeSelf) RenderCells(); }
}
