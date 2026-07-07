using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Screen_Dashboard controller (Variant B). Fetches conversation outcomes from the
/// DashboardOutcomes webhook, caches to disk, and renders the hero funnel + status
/// rows + recent заявки. Period and bot filters are client-side (no refetch).
/// </summary>
public class DashboardPage : MonoBehaviour
{
    [Header("Segmented period control")]
    [SerializeField] private Button todayButton, weekButton, monthButton;
    [SerializeField] private RectTransform periodHighlight;

    [Header("Bot chips")]
    [SerializeField] private Transform chipsRow;
    [SerializeField] private GameObject chipPrefabHost;   // an inactive template chip

    [Header("Hero")]
    [SerializeField] private TextMeshProUGUI heroCount, heroDelta, heroSubtitle;
    [SerializeField] private RectTransform funnelBar;     // children segments sized by weight
    [SerializeField] private Transform legendRoot;        // 5 legend rows

    [Header("Status rows")]
    [SerializeField] private Transform statusRowsRoot;    // 5 rows: dot/label/count/chevron

    [Header("Recent заявки")]
    [SerializeField] private Transform recentRoot;
    [SerializeField] private GameObject rowTemplate;      // inactive conversation-row template

    [Header("States")]
    [SerializeField] private GameObject loadingState, emptyState;

    [Header("Drill-down list panel")]
    [SerializeField] private RectTransform listPanel;     // slide-in sub-page shell
    [SerializeField] private Button listBackButton;
    [SerializeField] private TextMeshProUGUI listTitle;
    [SerializeField] private Transform listRoot;

    private readonly List<DashboardOutcome> _all = new();
    private DashboardPeriod _period = DashboardPeriod.Today;
    private string _botFilter;               // null = all bots
    private bool _fetching;
    private const int TruncatedRefetchCap = 5;

    private void Awake()
    {
        if (todayButton) todayButton.onClick.AddListener(() => SetPeriod(DashboardPeriod.Today));
        if (weekButton)  weekButton.onClick.AddListener(() => SetPeriod(DashboardPeriod.Week));
        if (monthButton) monthButton.onClick.AddListener(() => SetPeriod(DashboardPeriod.Month));
        if (listBackButton) listBackButton.onClick.AddListener(CloseStatusList);
        if (listPanel) listPanel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _all.Clear();
        _all.AddRange(DashboardStore.Load());     // instant paint from cache
        BuildChips();
        Render();

        long now = NowMs();
        if (DashboardRefreshGate.ShouldFetch(DashboardStore.LastFetchMs, now))
            StartCoroutine(FetchRoutine(0));
        else if (_all.Count == 0)
            SetLoading(true);                     // first-ever open, no cache
    }

    // ---- data ----------------------------------------------------------------

    private List<string> AuthedProfiles()
    {
        var list = new List<string>();
        var parent = Manager.Instance != null ? Manager.Instance.BotsRoot : null;  // public Transform
        if (parent == null) return list;
        foreach (Transform t in parent)
        {
            var bot = t.GetComponent<Bot>();
            if (bot == null) continue;
            string pid = bot.whatsappProfileId;
            if (!string.IsNullOrEmpty(pid) && pid != Bot.UnauthedProfileSentinel)
                list.Add(pid);
        }
        return list;
    }

    private Dictionary<string, string> ProfileToBot()
    {
        var map = new Dictionary<string, string>();
        var parent = Manager.Instance != null ? Manager.Instance.BotsRoot : null;  // public Transform
        if (parent == null) return map;
        foreach (Transform t in parent)
        {
            var bot = t.GetComponent<Bot>();
            if (bot == null) continue;
            if (!string.IsNullOrEmpty(bot.whatsappProfileId) &&
                bot.whatsappProfileId != Bot.UnauthedProfileSentinel)
                map[bot.whatsappProfileId] = t.name;
        }
        return map;
    }

    private IEnumerator FetchRoutine(int attempt)
    {
        if (_fetching && attempt == 0) yield break;
        _fetching = true;

        var profiles = AuthedProfiles();
        if (profiles.Count == 0) { _fetching = false; SetLoading(false); Render(); yield break; }

        string url = $"{Manager.n8nBaseUrl}/webhook/DashboardOutcomes";
        string body = JsonConvert.SerializeObject(new { profileIds = profiles });

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");   // REQUIRED (see Global Constraints)
        req.timeout = 30;
        yield return req.SendWebRequest();

        _fetching = false;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Dashboard] fetch failed [{req.responseCode}] {req.error}");
            SetLoading(false);
            Render();                                 // keep cached data
            yield break;
        }

        var parsed = DashboardResponse.Parse(req.downloadHandler.text);
        if (parsed == null || !parsed.success) { SetLoading(false); Render(); yield break; }

        _all.Clear();
        _all.AddRange(parsed.outcomes);
        DashboardStore.Save(_all, NowMs());
        SetLoading(false);
        BuildChips();
        Render();

        // Drain a large backlog in one visit.
        if (parsed.truncated && attempt + 1 < TruncatedRefetchCap)
            StartCoroutine(FetchRoutine(attempt + 1));
    }

    // ---- rendering -----------------------------------------------------------

    private void Render()
    {
        var rows = DashboardMetrics.FilterByProfile(_all, _botFilter).ToList();
        var w = DashboardMetrics.ComputeWindow(_period, NowMs(), TodayStartMs());

        int orders = DashboardMetrics.CountOrders(rows, w);
        int prev = DashboardMetrics.CountOrdersPrev(rows, w);
        int[] counts = DashboardMetrics.StatusCounts(rows, w);

        if (heroCount) heroCount.text = orders.ToString();
        if (heroDelta) SetDelta(orders - prev);
        if (heroSubtitle)
        {
            int active = counts.Sum();
            heroSubtitle.text = $"{active} {Plural(active, "диалог", "диалога", "диалогов")}";
        }
        RenderFunnel(counts);
        RenderLegend(counts);
        RenderStatusRows(counts);
        RenderRecent(DashboardMetrics.Recent(rows, w, 5));

        bool empty = _all.Count == 0 && !_fetching;
        if (emptyState) emptyState.SetActive(empty);
    }

    private void RenderFunnel(int[] counts)
    {
        if (funnelBar == null) return;
        for (int i = 0; i < funnelBar.childCount && i < counts.Length; i++)
        {
            var seg = funnelBar.GetChild(i) as RectTransform;
            var le = seg.GetComponent<LayoutElement>() ?? seg.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = counts[i];                 // proportional segment
            var img = seg.GetComponent<Image>();
            if (img) img.color = DashboardStatusInfo.FgColor(DashboardStatusInfo.Ordered[i]);
            seg.gameObject.SetActive(counts[i] > 0);
        }
    }

    private void RenderStatusRows(int[] counts)
    {
        if (statusRowsRoot == null) return;
        for (int i = 0; i < statusRowsRoot.childCount && i < counts.Length; i++)
        {
            var row = statusRowsRoot.GetChild(i);
            var status = DashboardStatusInfo.Ordered[i];
            var label = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
            var count = row.Find("Count")?.GetComponent<TextMeshProUGUI>();
            var dot = row.Find("Dot")?.GetComponent<Image>();
            if (label) label.text = DashboardStatusInfo.Label(status);
            if (count) count.text = counts[i].ToString();
            if (dot) dot.color = DashboardStatusInfo.FgColor(status);
            var btn = row.GetComponent<Button>();
            if (btn) { btn.onClick.RemoveAllListeners();
                       var s = status; btn.onClick.AddListener(() => OpenStatusList(s)); }
        }
    }

    // The hero-card funnel legend: per-status count + colored dot (mockup shows counts
    // here, distinct from the tappable status rows). Row children: Dot/Label/Count.
    private void RenderLegend(int[] counts)
    {
        if (legendRoot == null) return;
        for (int i = 0; i < legendRoot.childCount && i < counts.Length; i++)
        {
            var row = legendRoot.GetChild(i);
            var status = DashboardStatusInfo.Ordered[i];
            var label = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
            var count = row.Find("Count")?.GetComponent<TextMeshProUGUI>();
            var dot = row.Find("Dot")?.GetComponent<Image>();
            if (label) label.text = DashboardStatusInfo.Label(status);
            if (count) count.text = counts[i].ToString();
            if (dot) dot.color = DashboardStatusInfo.FgColor(status);
        }
    }

    private void RenderRecent(List<DashboardOutcome> recent)
    {
        SpawnRows(recentRoot, rowTemplate, recent);
    }

    private void SetDelta(int delta)
    {
        string sign = delta > 0 ? "+" : "";
        heroDelta.text = delta == 0 ? "—" : $"{sign}{delta} к пред.";
        heroDelta.color = delta >= 0
            ? DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected)
            : DashboardStatusInfo.FgColor(OutcomeStatus.OwnerNeeded);
    }

    // ---- filters, chips, drill-down ------------------------------------------

    public void SetPeriod(DashboardPeriod p) { _period = p; MovePeriodHighlight(); Render(); }

    public void SetBotFilter(string profileIdOrNull) { _botFilter = profileIdOrNull; Render(); }

    private void MovePeriodHighlight()
    {
        // Snap the segmented highlight under the active button (DOTween 0.2s OutCubic).
        Button target = _period == DashboardPeriod.Today ? todayButton
                      : _period == DashboardPeriod.Week ? weekButton : monthButton;
        if (periodHighlight != null && target != null)
            periodHighlight.DOAnchorPosX(((RectTransform)target.transform).anchoredPosition.x, 0.2f)
                .SetEase(DG.Tweening.Ease.OutCubic);
    }

    private void BuildChips()
    {
        if (chipsRow == null || chipPrefabHost == null) return;
        // Clear previous (keep the inactive template).
        for (int i = chipsRow.childCount - 1; i >= 0; i--)
        {
            var c = chipsRow.GetChild(i).gameObject;
            if (c != chipPrefabHost) Destroy(c);
        }
        var map = ProfileToBot();
        // Chips hidden entirely with ≤1 bot.
        chipsRow.gameObject.SetActive(map.Count > 1);
        if (map.Count <= 1) return;

        AddChip("Все боты", null, _botFilter == null);
        foreach (var kv in map)
        {
            string botName = PlayerPrefs.GetString(kv.Value + "Name", kv.Value);
            AddChip(botName, kv.Key, _botFilter == kv.Key);
        }
    }

    private void AddChip(string text, string profileId, bool on)
    {
        var go = Instantiate(chipPrefabHost, chipsRow);
        go.SetActive(true);
        var lbl = go.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl) lbl.text = text;
        var img = go.GetComponent<Image>();
        if (img) img.color = on ? DashboardStatusInfo.FgColor(OutcomeStatus.InDialog) : Color.white;
        var btn = go.GetComponent<Button>();
        if (btn) { btn.onClick.AddListener(() => SetBotFilter(profileId)); btn.onClick.AddListener(BuildChips); }
    }

    public void OpenStatusList(OutcomeStatus status)
    {
        if (listPanel == null) return;
        var rows = DashboardMetrics.FilterByProfile(_all, _botFilter)
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.lastMessageAt).ToList();
        if (listTitle) listTitle.text = DashboardStatusInfo.Label(status);
        SpawnRows(listRoot, rowTemplate, rows);
        listPanel.gameObject.SetActive(true);
        listPanel.SetAsLastSibling();
        listPanel.anchoredPosition = new Vector2(CanvasWidth(), listPanel.anchoredPosition.y);
        listPanel.DOAnchorPosX(0f, 0.3f).SetEase(DG.Tweening.Ease.OutCubic);
    }

    private void CloseStatusList()
    {
        if (listPanel == null) return;
        listPanel.DOAnchorPosX(CanvasWidth(), 0.25f).SetEase(DG.Tweening.Ease.InCubic)
            .OnComplete(() => listPanel.gameObject.SetActive(false));
    }

    private float CanvasWidth()
    {
        var c = GetComponentInParent<Canvas>();
        return c != null ? ((RectTransform)c.transform).rect.width : 1080f;
    }

    // ---- row spawning + deep-link --------------------------------------------

    private void SpawnRows(Transform root, GameObject template, List<DashboardOutcome> rows)
    {
        if (root == null || template == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i).gameObject;
            if (c != template) Destroy(c);
        }
        var map = ProfileToBot();
        bool showBotTag = _botFilter == null && map.Count > 1;
        foreach (var r in rows)
        {
            var go = Instantiate(template, root);
            go.SetActive(true);
            BindRow(go, r, showBotTag, map);
        }
    }

    private void BindRow(GameObject go, DashboardOutcome r, bool showBotTag, Dictionary<string,string> map)
    {
        var name = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        var summary = go.transform.Find("Summary")?.GetComponent<TextMeshProUGUI>();
        var pill = go.transform.Find("Pill")?.GetComponent<Image>();
        var pillLabel = go.transform.Find("Pill/Label")?.GetComponent<TextMeshProUGUI>();
        var botTag = go.transform.Find("BotTag")?.GetComponent<TextMeshProUGUI>();
        var avatar = go.transform.Find("Avatar")?.GetComponent<Image>();
        var avatarInitial = go.transform.Find("Avatar/Initial")?.GetComponent<TextMeshProUGUI>();

        string display = ChatDisplayName(r.chatId);
        if (name) name.text = display;
        if (summary) summary.text = r.summary;
        if (pill) pill.color = DashboardStatusInfo.BgColor(r.Status);
        if (pillLabel) { pillLabel.text = DashboardStatusInfo.Label(r.Status);
                         pillLabel.color = DashboardStatusInfo.FgColor(r.Status); }
        if (botTag) { botTag.gameObject.SetActive(showBotTag);
            if (showBotTag && map.TryGetValue(r.profileId, out var bn))
                botTag.text = PlayerPrefs.GetString(bn + "Name", bn); }
        ApplyAvatar(avatar, avatarInitial, r.chatId, display);

        var btn = go.GetComponent<Button>();
        if (btn) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => OpenChat(r)); }
    }

    public void OpenChat(DashboardOutcome r)
    {
        string botName = SessionChatMap.ResolveBotName(ProfileToBot(), r.profileId);
        if (string.IsNullOrEmpty(botName) || ChatManager.Instance == null) return;

        if (ChatManager.Instance.CurrentBotId != botName)
            ChatManager.Instance.SetActiveBot(botName);

        var tabs = FindFirstObjectByType<BottomTabManager>();
        if (tabs != null) tabs.SwitchTab(BottomTabManager.WhatsAppTabIndex);

        // Deferred one frame so the WhatsApp tab's own sync/list settles; if the chat
        // isn't present we just land on that bot's list (no error popup). Hosted on
        // ChatManager (always active): SwitchTab above just deactivated Screen_Dashboard,
        // and Unity refuses to start a coroutine on an inactive GameObject.
        ChatManager.Instance.StartCoroutine(OpenChatDeferred(r.chatId));
    }

    private IEnumerator OpenChatDeferred(string chatId)
    {
        yield return null;
        ChatManager.Instance.SelectChat(chatId);
    }

    private void OnDisable()
    {
        // Screen_Dashboard toggles via SetActive, which stops our coroutines mid-flight
        // and skips their post-yield cleanup. Reset the fetch guard so leaving during a
        // load can't wedge _fetching=true and freeze every later refresh. (The deep-link
        // coroutine deliberately lives on ChatManager, so it survives this.)
        StopAllCoroutines();
        _fetching = false;
    }

    // Deterministic avatar (mirror ChatItemView) + display-name fallback.
    private static readonly string[][] AvatarColors = {
        new[]{"#CFE9E4","#00A884"}, new[]{"#D6E4FB","#1FA2FF"}, new[]{"#EADCF1","#A348D4"},
        new[]{"#FCE1D0","#F8942F"}, new[]{"#FCE2EC","#E14781"} };

    private void ApplyAvatar(Image bg, TextMeshProUGUI initial, string chatId, string display)
    {
        int hash = 0; foreach (char c in chatId ?? "") hash += c;
        var pair = AvatarColors[Mathf.Abs(hash) % AvatarColors.Length];
        if (bg && ColorUtility.TryParseHtmlString(pair[0], out var b)) bg.color = b;
        if (initial) {
            initial.text = string.IsNullOrEmpty(display) ? "?" : display.Substring(0, 1).ToUpper();
            if (ColorUtility.TryParseHtmlString(pair[1], out var f)) initial.color = f;
        }
    }

    private string ChatDisplayName(string chatId)
    {
        // Prefer the live chat-list title; fall back to the phone number from the id.
        if (ChatManager.Instance != null &&
            ChatManager.Instance.TryGetChatTitle(chatId, out var title) && !string.IsNullOrEmpty(title))
            return title;
        return WappiRecipient.FromChatId(chatId);   // strips @c.us → digits
    }

    // ---- helpers -------------------------------------------------------------

    private void SetLoading(bool on) { if (loadingState) loadingState.SetActive(on); }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long TodayStartMs()
    {
        DateTime midnight = DateTime.Now.Date;                 // local midnight
        return new DateTimeOffset(midnight).ToUnixTimeMilliseconds();
    }

    private static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }
}
