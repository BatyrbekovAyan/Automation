using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage 7: the «Проверить бота» row in the Основное tab. Runtime-added into the tab's existing
/// VerticalLayoutGroup right above «Удалить бота» — the prefab is deliberately untouched (no scene edit,
/// no destructive builder), so a re-run of any Bot Settings builder cannot lose it.
/// </summary>
public partial class BotSettings
{
    public const string TestRowName = "TestBotRow";
    public const float TestRowHeight = 132f;

    private GameObject testRow;
    private Image testRowImage;
    private TextMeshProUGUI testRowTitle, testRowSubline, testRowChevron;
    private bool testRowThemeBound;

    private TMP_FontAsset TestTitleFont => headerTitle != null ? headerTitle.font : null;

    private TMP_FontAsset TestBodyFont
    {
        get
        {
            var tmp = BotNameField != null ? BotNameField.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            return tmp != null ? tmp.font : TestTitleFont;
        }
    }

    private void EnsureTestRow()
    {
        if (testRow != null || General == null) return;
        var group = General.GetComponentInChildren<VerticalLayoutGroup>(true);
        if (group == null) return;
        testRow = BuildTestRow(group.transform);
        bool deleteIsSibling = deleteBotButton != null && deleteBotButton.transform.parent == group.transform;
        testRow.transform.SetSiblingIndex(deleteIsSibling ? deleteBotButton.transform.GetSiblingIndex() : group.transform.childCount - 1);
        PopupUI.WireFingerUp(testRow.GetComponent<Button>(), OpenTestSheet);
        PaintTestRow();
        if (!testRowThemeBound) { Theme.Changed += PaintTestRow; testRowThemeBound = true; }
    }

    private GameObject BuildTestRow(Transform parent)
    {
        var go = new GameObject(TestRowName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(0f, TestRowHeight);     // the group controls width, not height
        testRowImage = go.GetComponent<Image>();
        testRowImage.raycastTarget = true;
        BotTestSheet.AddRounded(go, 28f);
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = testRowImage;

        testRowTitle = BotTestSheet.BuildTmp(go.transform, "Title", BotReadinessCopy.RowTitle, 32f, FontStyles.Bold, TestTitleFont, TextAlignmentOptions.Left);
        PlaceTopLeft((RectTransform)testRowTitle.transform, 40f, 22f, 44f, 160f);
        testRowSubline = BotTestSheet.BuildTmp(go.transform, "Subline", BotReadinessCopy.RowSubline, 24f, FontStyles.Normal, TestBodyFont, TextAlignmentOptions.Left);
        PlaceTopLeft((RectTransform)testRowSubline.transform, 40f, 72f, 36f, 160f);
        testRowChevron = BotTestSheet.BuildTmp(go.transform, "Chevron", "›", 48f, FontStyles.Normal, TestTitleFont, TextAlignmentOptions.Center);
        var chevronRt = (RectTransform)testRowChevron.transform;
        chevronRt.anchorMin = chevronRt.anchorMax = new Vector2(1f, 0.5f);
        chevronRt.pivot = new Vector2(1f, 0.5f);
        chevronRt.anchoredPosition = new Vector2(-40f, 4f);
        chevronRt.sizeDelta = new Vector2(40f, 60f);
        return go;
    }

    private static void PlaceTopLeft(RectTransform rt, float left, float top, float height, float rightInset)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(left, -top);
        rt.sizeDelta = new Vector2(-(left + rightInset), height);
    }

    private void PaintTestRow()
    {
        if (testRowImage == null) return;
        testRowImage.color = Theme.Color(ThemeRole.Surface);
        testRowTitle.color = Theme.Color(ThemeRole.InkPrimary);
        testRowSubline.color = Theme.Color(ThemeRole.InkSecondary);
        testRowChevron.color = Theme.Color(ThemeRole.InkTertiary);
    }

    /// <summary>Opens the test sheet for the bot this screen edits (the sheet resolves the authed channel itself).</summary>
    public void OpenTestSheet()
    {
        var openBot = Manager.openBot;
        var bot = openBot != null ? openBot.GetComponent<Bot>() : null;
        if (bot == null) return;
        BotTestSheet.Show(openBot.name, bot, TestTitleFont, TestBodyFont);
    }
}
