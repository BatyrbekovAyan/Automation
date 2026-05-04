using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Bottom-sheet controller. Animates a panel up from the bottom of the screen
/// and back down. The panel must be BOTTOM-ANCHORED (pivot Y = 0, anchorMin/Max
/// Y = 0) so the offscreen position is one panel-height BELOW the shown position.
/// Top- or center-anchored panels would slide up/sideways instead — Task 14's
/// editor builder enforces this layout.
/// </summary>
public class BotSwitcherSheet : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup backdropGroup;
    [SerializeField] private Button backdropButton;
    [SerializeField] private RectTransform sheetPanel;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private BotSwitcherRowView rowPrefab;

    [Header("Animation")]
    [SerializeField] private float openDurationSeconds = 0.3f;
    [SerializeField] private float closeDurationSeconds = 0.25f;

    // Used only as a defensive backup when sheetPanel.rect.height is 0 at Awake time
    // (e.g., the layout hasn't computed yet). Task 14's builder sets a real size so
    // this fallback rarely fires. 1200 is comfortably larger than any phone height
    // so the panel is guaranteed to start offscreen.
    private const float FallbackPanelHeight = 1200f;

    private bool isAnimating;
    private float panelHiddenY;
    private float panelShownY;

    private void Awake()
    {
        if (sheetPanel != null)
        {
            panelShownY = sheetPanel.anchoredPosition.y;
            // Hide below the screen by the sheet's height.
            panelHiddenY = panelShownY - (sheetPanel.rect.height > 0 ? sheetPanel.rect.height : FallbackPanelHeight);
            sheetPanel.anchoredPosition = new Vector2(sheetPanel.anchoredPosition.x, panelHiddenY);
        }
        if (backdropGroup != null)
        {
            backdropGroup.alpha = 0f;
            backdropGroup.blocksRaycasts = false;
        }
        if (backdropButton != null)
        {
            backdropButton.onClick.RemoveAllListeners();
            backdropButton.onClick.AddListener(Close);
        }
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Kills any in-flight tweens and resets the animating flag so we don't
    /// lock the sheet if the GameObject is deactivated mid-animation
    /// (parent SetActive, scene unload, etc.).
    /// </summary>
    private void OnDisable()
    {
        if (sheetPanel != null) sheetPanel.DOKill();
        if (backdropGroup != null) backdropGroup.DOKill();
        isAnimating = false;
    }

    public void Open()
    {
        if (isAnimating) return;

        gameObject.SetActive(true);
        PopulateRows();

        isAnimating = true;
        if (backdropGroup != null)
        {
            backdropGroup.blocksRaycasts = true;
            backdropGroup.DOFade(0.4f, openDurationSeconds);
        }
        if (sheetPanel != null)
        {
            sheetPanel.DOAnchorPosY(panelShownY, openDurationSeconds)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => isAnimating = false);
        }
        else
        {
            isAnimating = false;
        }
    }

    public void Close()
    {
        if (isAnimating) return;
        isAnimating = true;

        if (backdropGroup != null)
        {
            backdropGroup.DOFade(0f, closeDurationSeconds);
            backdropGroup.blocksRaycasts = false;
        }
        if (sheetPanel != null)
        {
            sheetPanel.DOAnchorPosY(panelHiddenY, closeDurationSeconds)
                .SetEase(Ease.InCubic)
                .OnComplete(() =>
                {
                    isAnimating = false;
                    gameObject.SetActive(false);
                });
        }
        else
        {
            isAnimating = false;
            gameObject.SetActive(false);
        }
    }

    private void PopulateRows()
    {
        if (rowContainer == null || rowPrefab == null) return;

        foreach (Transform existing in rowContainer)
        {
            Destroy(existing.gameObject);
        }

        Transform botsRoot = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (botsRoot == null) return;

        string activeBotId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : "";

        for (int i = 0; i < botsRoot.childCount; i++)
        {
            Bot bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;

            var row = Instantiate(rowPrefab, rowContainer);
            row.transform.localScale = Vector3.one;
            row.Bind(bot, isSelected: bot.transform.name == activeBotId, tapHandler: HandleRowTap);
        }
    }

    private void HandleRowTap(string botId)
    {
        if (ChatManager.Instance != null) ChatManager.Instance.SetActiveBot(botId);
        Close();
    }
}
