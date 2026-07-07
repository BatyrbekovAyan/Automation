using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Profile → Конфиденциальность: live media-cache size + the two local
// clear actions. Size scan runs on THIS component so ChatManager's
// StopAllCoroutines (inside ClearAllLocalHistory) can't kill it mid-flight.
public partial class ProfileSubPages
{
    [Header("Privacy page")]
    [SerializeField] private Button mediaCacheButton;
    [SerializeField] private TextMeshProUGUI mediaCacheLabel;
    [SerializeField] private TextMeshProUGUI mediaCacheValue;
    [SerializeField] private Button historyButton;

    private Coroutine _mediaSizeRoutine;
    private long _mediaCacheBytes = -1;

    private void WirePrivacy()
    {
        if (mediaCacheButton != null) mediaCacheButton.onClick.AddListener(ConfirmClearMedia);
        if (historyButton != null) historyButton.onClick.AddListener(ConfirmClearHistory);
    }

    private void RefreshPrivacyPage()
    {
        if (mediaCacheValue != null) mediaCacheValue.text = "…";
        SetMediaRowEnabled(false); // disabled until the scan reports a non-empty cache

        if (ChatManager.Instance == null) return;
        if (_mediaSizeRoutine != null) StopCoroutine(_mediaSizeRoutine);
        _mediaSizeRoutine = StartCoroutine(ChatManager.Instance.ComputeMediaCacheSize(OnMediaCacheSize));
    }

    private void OnMediaCacheSize(long bytes)
    {
        _mediaSizeRoutine = null;
        _mediaCacheBytes = bytes;
        if (mediaCacheValue != null) mediaCacheValue.text = CacheSizeFormatter.FormatBytes(bytes);
        SetMediaRowEnabled(bytes > 0);
    }

    private void SetMediaRowEnabled(bool enabled)
    {
        if (mediaCacheButton != null) mediaCacheButton.interactable = enabled;
        if (mediaCacheLabel != null) mediaCacheLabel.color = enabled ? InkColor : DisabledColor;
        if (mediaCacheValue != null) mediaCacheValue.color = enabled ? MutedColor : DisabledColor;
    }

    private void ConfirmClearMedia() => ShowConfirm(
        "Очистить кэш медиа?",
        "Фото и видео из чатов будут скачаны заново при просмотре.",
        "Очистить",
        ClearMediaNow);

    private void ClearMediaNow()
    {
        long freed = _mediaCacheBytes;
        if (ChatManager.Instance != null) ChatManager.Instance.ClearAllMediaCaches();
        ShowToast(PanelFor(Page.Privacy), $"Освобождено {CacheSizeFormatter.FormatBytes(freed)}");
        RefreshPrivacyPage();
    }

    private void ConfirmClearHistory() => ShowConfirm(
        "Очистить историю чатов?",
        "Переписка в WhatsApp и Telegram не удаляется — очищаются только локальные копии в приложении.",
        "Очистить",
        ClearHistoryNow);

    private void ClearHistoryNow()
    {
        if (ChatManager.Instance != null) ChatManager.Instance.ClearAllLocalHistory();
        ShowToast(PanelFor(Page.Privacy), "История чатов очищена");
    }
}
