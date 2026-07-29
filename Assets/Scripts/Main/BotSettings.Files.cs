using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Uploaded price-list files: the per-tab "Прайс-листы" section and the
// per-file delete flow (✕ → confirm popup → n8n DeleteFile webhook → row +
// local record removed). UI is baked into BotSettings.prefab by
// BotSettingsUploadedFilesBuilder; this partial only binds data and behavior.
//
// Rows are rendered from two sources and OWN neither: finished uploads come
// from UploadedFilesStore, in-flight and failed ones from UploadCenter.Jobs.
// The screen used to hold in-flight state itself, which is why leaving it
// mid-upload stranded the row forever — see UploadJobRegistry.
public partial class BotSettings
{
    #region Serialized — Uploaded files (wired by BotSettingsUploadedFilesBuilder)
    [SerializeField] private GameObject uploadedProductFilesSection;
    [SerializeField] private RectTransform uploadedProductFilesParent;
    [SerializeField] private GameObject uploadedProductFileRowTemplate;
    [SerializeField] private GameObject uploadedServiceFilesSection;
    [SerializeField] private RectTransform uploadedServiceFilesParent;
    [SerializeField] private GameObject uploadedServiceFileRowTemplate;
    [SerializeField] private GameObject deleteFileConfirmPopup;
    [SerializeField] private Button deleteFileConfirmButton;
    [SerializeField] private Button deleteFileCancelButton;
    [SerializeField] private TextMeshProUGUI deleteFileConfirmBody;
    [SerializeField] private GameObject replaceFileConfirmPopup;
    [SerializeField] private Button replaceFileConfirmButton;
    [SerializeField] private Button replaceFileCancelButton;
    [SerializeField] private TextMeshProUGUI replaceFileConfirmBody;
    #endregion

    private readonly List<GameObject> spawnedProductFileRows = new();
    private readonly List<GameObject> spawnedServiceFileRows = new();
    private UploadedFileEntry pendingDeleteEntry;
    private string pendingDeleteContentType;
    private bool replacePopupBusy;
    private bool? replaceDecision;
    private bool uploadEventsBound;

    private static readonly string[] RuMonthsShort =
        { "янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" };

    // Matches BotSettingsRebuilder's palette (Primary / Danger / light danger fill).
    private static readonly Color UploadingAccent = new Color32(0x1B, 0x7C, 0xEB, 0xFF);
    private static readonly Color FailedAccent = new Color32(0xE5, 0x39, 0x35, 0xFF);
    private static readonly Color FailedBadgeBg = new Color32(0xFD, 0xEC, 0xEC, 0xFF);

    private void WireUploadedFiles()
    {
        if (deleteFileConfirmButton != null)
            PopupUI.WireFingerUp(deleteFileConfirmButton, ConfirmDeleteUploadedFile);
        if (deleteFileCancelButton != null)
            PopupUI.WireFingerUp(deleteFileCancelButton, CancelDeleteUploadedFile);
        if (replaceFileConfirmButton != null)
            PopupUI.WireFingerUp(replaceFileConfirmButton, () => ResolveReplaceDecision(replace: true));
        if (replaceFileCancelButton != null)
            PopupUI.WireFingerUp(replaceFileCancelButton, () => ResolveReplaceDecision(replace: false));

        // The source sheet raises these when the user picks a source. Subscribe
        // at runtime (mirrors the ItemEditSheet event wiring above) so the
        // pending upload context set in ShowUploadSourceSheet is consumed by the
        // right handler. Guarded because the sheet may not be baked into an
        // older prefab yet — the upload buttons then fall back to the picker.
        if (uploadSourceSheet != null)
        {
            uploadSourceSheet.OnFilePressed += OnUploadSourceFilePressed;
            uploadSourceSheet.OnGalleryPressed += OnUploadSourceGalleryPressed;
        }

        BindUploadCenter();
    }

    // Uploads outlive this screen, so their progress arrives as events rather
    // than as the return of a coroutine we own.
    private void BindUploadCenter()
    {
        if (uploadEventsBound || UploadCenter.Instance == null) return;

        UploadCenter.Instance.Jobs.OnChanged += RefreshUploadedFiles;
        UploadCenter.Instance.OnUploadCompleted += OnUploadCompleted;
        uploadEventsBound = true;
    }

    private void UnbindUploadCenter()
    {
        UploadCenter center = UploadCenter.Existing;
        if (!uploadEventsBound || center == null) return;

        center.Jobs.OnChanged -= RefreshUploadedFiles;
        center.OnUploadCompleted -= OnUploadCompleted;
        uploadEventsBound = false;
    }

    // An upload that finished while this bot's settings are open settles into
    // its stored row with a small pop — the list is the upload confirmation.
    // Located by fileId, not by "the last row": other uploads may still be in
    // flight below it, and those rows are rendered after the stored ones.
    private void OnUploadCompleted(string botName, string contentType, string fileId)
    {
        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        if (openBot == null || openBot.name != botName) return;

        // Stored rows are spawned first and in store order, so the entry's
        // index in the store is its index in the spawned list.
        int index = UploadedFilesStore.Load(botName, contentType).FindIndex(entry => entry.Id == fileId);
        if (index < 0) return;

        var spawned = contentType == "product" ? spawnedProductFileRows : spawnedServiceFileRows;
        if (index < spawned.Count && spawned[index] != null)
            spawned[index].transform.DOPunchScale(Vector3.one * 0.05f, 0.25f);
    }

    // Leaving the screen kills this coroutine mid-question, so the latch it
    // holds has to be released here or every later same-named upload would
    // wait forever on a popup that is no longer on screen.
    private void ResetReplacePopupState()
    {
        if (replaceFileConfirmPopup != null)
        {
            // Instant, not PopupUI.Hide: the whole screen is going inactive, so
            // an animated close would just leave a tween running on a dead
            // hierarchy. PopupUI.Show re-initializes everything on the next open.
            replaceFileConfirmPopup.transform.DOKill();
            replaceFileConfirmPopup.SetActive(false);
        }
        replacePopupBusy = false;
        replaceDecision = null;
    }

    // Uploading a file whose name is already in the list REPLACES the old
    // version's knowledge — ask first. One popup serves all uploads: concurrent
    // same-named picks queue on replacePopupBusy and are asked one at a time.
    public IEnumerator RequestReplaceFileDecision(string fileName, System.Action<bool> onDecided)
    {
        while (replacePopupBusy) yield return null;
        replacePopupBusy = true;
        replaceDecision = null;

        if (replaceFileConfirmBody != null)
            replaceFileConfirmBody.text = $"«{fileName}» уже есть в списке. Заменить? Бот будет отвечать по новой версии.";

        if (replaceFileConfirmPopup != null)
            PopupUI.Show(replaceFileConfirmPopup);
        else
            replaceDecision = true; // popup not baked yet — keep the old replace behavior

        while (!replaceDecision.HasValue) yield return null;

        replacePopupBusy = false;
        onDecided?.Invoke(replaceDecision.Value);
    }

    private void ResolveReplaceDecision(bool replace)
    {
        if (replaceFileConfirmPopup != null) PopupUI.Hide(replaceFileConfirmPopup);
        replaceDecision = replace;
    }

    // Rebuilds both tabs' file rows from the store and the in-flight registry.
    // Cheap (a handful of rows), so a full rebuild on every change keeps state
    // trivially correct.
    public void RefreshUploadedFiles()
    {
        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        RefreshFilesTab(openBot, "product", uploadedProductFilesSection, uploadedProductFilesParent,
                        uploadedProductFileRowTemplate, spawnedProductFileRows);
        RefreshFilesTab(openBot, "service", uploadedServiceFilesSection, uploadedServiceFilesParent,
                        uploadedServiceFileRowTemplate, spawnedServiceFileRows);
    }

    private void RefreshFilesTab(Bot openBot, string contentType, GameObject section,
                                 RectTransform rowsParent, GameObject template,
                                 List<GameObject> spawned)
    {
        if (section == null || rowsParent == null || template == null) return;

        foreach (var row in spawned)
            if (row != null) Destroy(row);
        spawned.Clear();

        var files = openBot != null
            ? UploadedFilesStore.Load(openBot.name, contentType)
            : new List<UploadedFileEntry>();

        // In-flight/failed uploads are not in the store — they live in the
        // registry and keep the section alive so their feedback stays visible.
        var jobs = openBot != null && UploadCenter.Existing != null
            ? UploadCenter.Existing.Jobs.JobsFor(openBot.name, contentType)
            : new List<UploadJob>();

        section.SetActive(files.Count + jobs.Count > 0);
        if (files.Count + jobs.Count == 0) return;

        // Stored rows first, in-flight uploads last (newest activity at the bottom).
        foreach (var entry in files)
        {
            var row = Instantiate(template, rowsParent);
            row.SetActive(true);
            BindFileRow(row, entry, contentType);
            spawned.Add(row);
        }

        foreach (var job in jobs)
        {
            var row = Instantiate(template, rowsParent);
            row.SetActive(true);
            BindJobRow(row, job);
            spawned.Add(row);
        }

        RebuildTabLayout(rowsParent);
    }

    private void BindFileRow(GameObject row, UploadedFileEntry entry, string contentType)
    {
        var badgeLabel = row.transform.Find("Badge/Label")?.GetComponent<TextMeshProUGUI>();
        var nameLabel = row.transform.Find("Texts/Name")?.GetComponent<TextMeshProUGUI>();
        var metaLabel = row.transform.Find("Texts/Meta")?.GetComponent<TextMeshProUGUI>();
        var removeButton = row.transform.Find("RemoveButton")?.GetComponent<Button>();

        if (badgeLabel != null) badgeLabel.text = ExtensionBadge(entry.Name);
        if (nameLabel != null) nameLabel.text = entry.Name;
        if (metaLabel != null) metaLabel.text = FormatFileMeta(entry);

        if (removeButton != null)
            PopupUI.WireFingerUp(removeButton, () => RequestDeleteUploadedFile(entry, contentType));
    }

    ////////////////////////// UPLOAD-IN-PROGRESS ROWS //////////////////////////

    // Optimistic feedback: the row appears the moment a file is picked, with
    // pulsing dots instead of the ✕ and «Загрузка…» instead of size · date.
    // A failure flips it red rather than making it vanish — a network failure
    // (CanRetry) shows the tap-to-retry hint and re-runs the upload on tap; a
    // deterministic one shows the specific reason instead, because retrying a
    // wrong format or an empty file can only fail again.
    private void BindJobRow(GameObject row, UploadJob job)
    {
        var badge = row.transform.Find("Badge")?.GetComponent<Image>();
        var badgeLabel = row.transform.Find("Badge/Label")?.GetComponent<TextMeshProUGUI>();
        var nameLabel = row.transform.Find("Texts/Name")?.GetComponent<TextMeshProUGUI>();
        var metaLabel = row.transform.Find("Texts/Meta")?.GetComponent<TextMeshProUGUI>();
        var removeButton = row.transform.Find("RemoveButton")?.GetComponent<Button>();
        var rowButton = row.GetComponent<Button>();

        bool failed = job.State == UploadJobState.Failed;

        if (badgeLabel != null)
        {
            badgeLabel.text = ExtensionBadge(job.FileName);
            if (failed) badgeLabel.color = FailedAccent;
        }
        if (badge != null && failed) badge.color = FailedBadgeBg;

        if (nameLabel != null)
        {
            nameLabel.text = job.FileName;
            var color = nameLabel.color;
            color.a = failed ? 1f : 0.75f;
            nameLabel.color = color;
        }
        if (metaLabel != null)
        {
            metaLabel.text = failed ? job.FailureReason ?? UploadFailureText.TapToRetry : "Загрузка…";
            metaLabel.color = failed ? FailedAccent : UploadingAccent;
            if (failed) metaLabel.textWrappingMode = TextWrappingModes.Normal;
        }

        SetRowTrailing(row, showDots: !failed, removeInteractable: failed,
                       barColor: failed ? FailedAccent : (Color?)null);

        if (removeButton != null && failed)
            PopupUI.WireFingerUp(removeButton, () => UploadCenter.Instance?.Dismiss(job));

        if (rowButton != null)
        {
            rowButton.interactable = failed && job.CanRetry;
            if (failed && job.CanRetry)
                PopupUI.WireFingerUp(rowButton, () => UploadCenter.Instance?.Retry(job));
        }

        // A specific reason can wrap to a second line — release the baked
        // fixed row height so the card grows to fit instead of the meta text
        // clipping past its bottom edge (minHeight keeps it from shrinking).
        if (failed && job.FailureReason != null && row.TryGetComponent(out LayoutElement rowLayout))
            rowLayout.preferredHeight = -1f;
    }

    ////////////////////////////// PER-FILE DELETE //////////////////////////////

    private void RequestDeleteUploadedFile(UploadedFileEntry entry, string contentType)
    {
        if (UploadCenter.Existing != null && UploadCenter.Existing.IsDeleting(entry.Id)) return;

        pendingDeleteEntry = entry;
        pendingDeleteContentType = contentType;

        if (deleteFileConfirmBody != null)
            deleteFileConfirmBody.text = $"Бот перестанет использовать «{entry.Name}» в ответах. Это действие необратимо.";
        if (deleteFileConfirmPopup != null) PopupUI.Show(deleteFileConfirmPopup);
    }

    private void CancelDeleteUploadedFile()
    {
        if (deleteFileConfirmPopup != null) PopupUI.Hide(deleteFileConfirmPopup);
        pendingDeleteEntry = default;
        pendingDeleteContentType = null;
    }

    // Server first: the local record and the row are dropped only after the
    // server confirms, and the request itself runs on UploadCenter so leaving
    // the screen mid-delete can't strand it.
    private void ConfirmDeleteUploadedFile()
    {
        if (deleteFileConfirmPopup != null) PopupUI.Hide(deleteFileConfirmPopup);
        if (string.IsNullOrEmpty(pendingDeleteEntry.Id)) return;

        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        if (openBot == null) return;

        UploadCenter.Instance?.DeleteFile(openBot.name, pendingDeleteContentType, pendingDeleteEntry.Id);
    }

    // The trailing 48-unit slot holds both the ✕ bars and the pulsing dots, so
    // switching states never shifts the row layout.
    private static void SetRowTrailing(GameObject row, bool showDots, bool removeInteractable,
                                       Color? barColor = null)
    {
        var removeButton = row.transform.Find("RemoveButton");
        if (removeButton == null) return;

        var x1 = removeButton.Find("X1");
        var x2 = removeButton.Find("X2");
        var dots = removeButton.Find("Dots");

        if (x1 != null) x1.gameObject.SetActive(!showDots);
        if (x2 != null) x2.gameObject.SetActive(!showDots);
        if (dots != null) dots.gameObject.SetActive(showDots);

        if (barColor.HasValue)
        {
            if (x1 != null && x1.TryGetComponent(out Image bar1)) bar1.color = barColor.Value;
            if (x2 != null && x2.TryGetComponent(out Image bar2)) bar2.color = barColor.Value;
        }

        if (removeButton.TryGetComponent(out Button button)) button.interactable = removeInteractable;
    }

    private static string ExtensionBadge(string fileName)
    {
        string ext = System.IO.Path.GetExtension(fileName ?? "");
        if (string.IsNullOrEmpty(ext)) return "DOC";
        ext = ext.TrimStart('.').ToUpperInvariant();
        return ext.Length > 4 ? ext.Substring(0, 4) : ext;
    }

    private static string FormatFileMeta(UploadedFileEntry entry)
    {
        string size = FormatFileSize(entry.Size);
        string date = FormatFileDate(entry.DateUnixMs);
        return string.IsNullOrEmpty(date) ? size : $"{size} · {date}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024f * 1024f):0.#} МБ";
        if (bytes >= 1024) return $"{bytes / 1024f:0} КБ";
        return $"{bytes} Б";
    }

    private static string FormatFileDate(long unixMs)
    {
        if (unixMs <= 0) return "";
        var date = System.DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();
        return $"{date.Day} {RuMonthsShort[date.Month - 1]}";
    }
}
