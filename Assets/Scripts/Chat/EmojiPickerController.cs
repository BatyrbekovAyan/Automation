using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Bottom-sheet grid of the full reaction emoji catalog, opened from the reaction
/// bar's "+". Builds one cell per ReactionEmojiCatalog entry on first show; tapping
/// a cell sends that reaction via ChatManager and closes. Missing sprites are
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
    [SerializeField] private RectTransform gridContent; // GridLayoutGroup parent for the cells
    [SerializeField] private float cellFontSize = 72f;

    private MessageViewModel _target;
    private readonly List<TextMeshProUGUI> _cellLabels = new List<TextMeshProUGUI>();
    private bool _built;

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

        BuildGridIfNeeded();
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

    private void BuildGridIfNeeded()
    {
        if (_built || gridContent == null) return;

        string[] emojis = ReactionEmojiCatalog.All;
        for (int i = 0; i < emojis.Length; i++)
        {
            string emoji = emojis[i];   // capture per cell for the closure

            var cell = new GameObject("Cell", typeof(RectTransform), typeof(Image), typeof(Button));
            cell.transform.SetParent(gridContent, false);

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

            var btn = cell.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener(() => OnEmojiTapped(emoji));
        }
        _built = true;
    }

    private void RenderCells()
    {
        string[] emojis = ReactionEmojiCatalog.All;
        for (int i = 0; i < _cellLabels.Count && i < emojis.Length; i++)
            if (_cellLabels[i] != null)
                _cellLabels[i].text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(emojis[i], MissingEmojiMode.Hide);
    }

    private void OnEmojiTapped(string emoji)
    {
        if (_target != null) ChatManager.Instance?.SendReaction(_target, emoji);
        Hide();
    }

    private void HandleChatSelected(string chatId) => Hide();
    private void HandleEmojiReady(string spriteName) { if (content != null && content.activeSelf) RenderCells(); }
}
