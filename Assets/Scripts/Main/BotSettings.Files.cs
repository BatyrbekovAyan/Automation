using System.Collections;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// Uploaded price-list files: the per-tab "Прайс-листы" section (rows spawned
// from UploadedFilesStore) and the per-file delete flow (X → confirm popup →
// n8n DeleteFile webhook → row + local record removed). UI is baked into
// BotSettings.prefab by BotSettingsUploadedFilesBuilder; this partial only
// binds data and behavior.
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
    #endregion

    private readonly List<GameObject> spawnedProductFileRows = new();
    private readonly List<GameObject> spawnedServiceFileRows = new();
    private readonly List<GameObject> pendingProductFileRows = new();
    private readonly List<GameObject> pendingServiceFileRows = new();
    private UploadedFileEntry pendingDeleteEntry;
    private string pendingDeleteContentType;
    private GameObject pendingDeleteRow;
    private bool deleteFileInFlight;

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
    }

    // Rebuilds both tabs' file rows from the store. Cheap (a handful of rows),
    // so a full rebuild on every change keeps state trivially correct.
    public void RefreshUploadedFiles()
    {
        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        RefreshFilesTab(openBot, "product", uploadedProductFilesSection, uploadedProductFilesParent,
                        uploadedProductFileRowTemplate, spawnedProductFileRows, pendingProductFileRows);
        RefreshFilesTab(openBot, "service", uploadedServiceFilesSection, uploadedServiceFilesParent,
                        uploadedServiceFileRowTemplate, spawnedServiceFileRows, pendingServiceFileRows);
    }

    private void RefreshFilesTab(Bot openBot, string contentType, GameObject section,
                                 RectTransform rowsParent, GameObject template,
                                 List<GameObject> spawned, List<GameObject> pending)
    {
        if (section == null || rowsParent == null || template == null) return;

        foreach (var row in spawned)
            if (row != null) Destroy(row);
        spawned.Clear();
        pending.RemoveAll(row => row == null);

        var files = openBot != null
            ? UploadedFilesStore.Load(openBot.name, contentType)
            : new List<UploadedFileEntry>();

        // Pending (uploading/failed) rows are not in the store but keep the
        // section alive so in-flight feedback stays visible.
        section.SetActive(files.Count + pending.Count > 0);
        if (files.Count + pending.Count == 0) return;

        foreach (var entry in files)
        {
            var row = Instantiate(template, rowsParent);
            row.SetActive(true);
            BindFileRow(row, entry, contentType);
            spawned.Add(row);
        }

        // Stored rows first, in-flight uploads last (newest activity at the bottom).
        foreach (var row in pending)
            row.transform.SetAsLastSibling();

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
            PopupUI.WireFingerUp(removeButton, () => RequestDeleteUploadedFile(entry, contentType, row));
    }

    private void RequestDeleteUploadedFile(UploadedFileEntry entry, string contentType, GameObject row)
    {
        if (deleteFileInFlight) return;

        pendingDeleteEntry = entry;
        pendingDeleteContentType = contentType;
        pendingDeleteRow = row;

        if (deleteFileConfirmBody != null)
            deleteFileConfirmBody.text = $"Бот перестанет использовать «{entry.Name}» в ответах. Это действие необратимо.";
        if (deleteFileConfirmPopup != null) PopupUI.Show(deleteFileConfirmPopup);
    }

    private void CancelDeleteUploadedFile()
    {
        if (deleteFileConfirmPopup != null) PopupUI.Hide(deleteFileConfirmPopup);
        pendingDeleteEntry = default;
        pendingDeleteContentType = null;
        pendingDeleteRow = null;
    }

    private void ConfirmDeleteUploadedFile()
    {
        if (deleteFileConfirmPopup != null) PopupUI.Hide(deleteFileConfirmPopup);
        if (string.IsNullOrEmpty(pendingDeleteEntry.Id)) return;
        StartCoroutine(DeleteUploadedFileRoutine(pendingDeleteEntry, pendingDeleteContentType, pendingDeleteRow));
    }

    // The X-button delete flow: server first, then local record + row are
    // dropped only after the server confirms.
    private IEnumerator DeleteUploadedFileRoutine(UploadedFileEntry entry, string contentType, GameObject row)
    {
        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        if (openBot == null) yield break;

        deleteFileInFlight = true;
        bool deleted = false;
        yield return DeleteFileChunksRequest(entry.Id, success => deleted = success);
        deleteFileInFlight = false;

        if (!deleted) yield break; // keep the row — the bot still knows this file

        // deletedChunks == 0 means the chunks were already gone server-side;
        // still drop the local record so the list reflects reality.
        UploadedFilesStore.Remove(openBot.name, contentType, entry.Id);

        if (row != null)
        {
            row.SetActive(false); // VLG skips inactive children — reflow now, not end-of-frame
            Destroy(row);
        }
        RefreshUploadedFiles();
    }

    // Replace-on-reupload: a fresh upload with the same file name superseded this
    // entry, so its stale RAG chunks (old prices!) must not keep answering. No
    // popup/row here, and no deleteFileInFlight gate — it targets a different
    // fileId than any X-button delete. Keeps the record on failure so a later
    // manual X can retry the cleanup.
    public IEnumerator DeleteReplacedFileRoutine(string botName, string contentType, string staleFileId)
    {
        bool deleted = false;
        yield return DeleteFileChunksRequest(staleFileId, success => deleted = success);
        if (!deleted) yield break;

        UploadedFilesStore.Remove(botName, contentType, staleFileId);
        RefreshUploadedFiles();
    }

    ////////////////////////// UPLOAD-IN-PROGRESS ROWS //////////////////////////

    // Optimistic feedback: the row appears the moment a file is picked, with
    // pulsing dots instead of the ✕ and «Загрузка…» instead of size · date.
    // Not in UploadedFilesStore — tracked in the pending lists so
    // RefreshUploadedFiles keeps it (and the section) alive during the upload.
    public GameObject AddPendingFileRow(string contentType, string fileName)
    {
        bool isProduct = contentType == "product";
        var section = isProduct ? uploadedProductFilesSection : uploadedServiceFilesSection;
        var parent = isProduct ? uploadedProductFilesParent : uploadedServiceFilesParent;
        var template = isProduct ? uploadedProductFileRowTemplate : uploadedServiceFileRowTemplate;
        var pending = isProduct ? pendingProductFileRows : pendingServiceFileRows;
        if (section == null || parent == null || template == null) return null;

        var row = Instantiate(template, parent);
        row.SetActive(true);

        var nameLabel = row.transform.Find("Texts/Name")?.GetComponent<TextMeshProUGUI>();
        var metaLabel = row.transform.Find("Texts/Meta")?.GetComponent<TextMeshProUGUI>();
        var badgeLabel = row.transform.Find("Badge/Label")?.GetComponent<TextMeshProUGUI>();

        if (badgeLabel != null) badgeLabel.text = ExtensionBadge(fileName);
        if (nameLabel != null)
        {
            nameLabel.text = fileName;
            var dimmed = nameLabel.color; dimmed.a = 0.75f; nameLabel.color = dimmed;
        }
        if (metaLabel != null)
        {
            metaLabel.text = "Загрузка…";
            metaLabel.color = UploadingAccent;
        }
        SetRowTrailing(row, showDots: true, removeInteractable: false);

        pending.Add(row);
        section.SetActive(true);
        RebuildTabLayout(parent);
        return row;
    }

    // Upload confirmed: the pending row is replaced by the real stored row
    // (caller has already added the store entry), with a small settle pop.
    public void CompletePendingFileRow(GameObject row, string contentType)
    {
        DropPendingRow(row, contentType);
        var spawned = contentType == "product" ? spawnedProductFileRows : spawnedServiceFileRows;
        if (spawned.Count > 0 && spawned[^1] != null)
            spawned[^1].transform.DOPunchScale(Vector3.one * 0.05f, 0.25f);
    }

    // Upload failed: the row flips to a red retry state instead of vanishing —
    // tapping the row re-runs the upload, the ✕ dismisses the failed attempt.
    public void MarkPendingRowFailed(GameObject row, string contentType, System.Action retry)
    {
        if (row == null) return;

        var nameLabel = row.transform.Find("Texts/Name")?.GetComponent<TextMeshProUGUI>();
        var metaLabel = row.transform.Find("Texts/Meta")?.GetComponent<TextMeshProUGUI>();
        var badge = row.transform.Find("Badge")?.GetComponent<Image>();
        var badgeLabel = row.transform.Find("Badge/Label")?.GetComponent<TextMeshProUGUI>();

        if (nameLabel != null)
        {
            var full = nameLabel.color; full.a = 1f; nameLabel.color = full;
        }
        if (metaLabel != null)
        {
            metaLabel.text = "Не загрузилось · нажмите, чтобы повторить";
            metaLabel.color = FailedAccent;
        }
        if (badge != null) badge.color = FailedBadgeBg;
        if (badgeLabel != null) badgeLabel.color = FailedAccent;

        SetRowTrailing(row, showDots: false, removeInteractable: true, barColor: FailedAccent);

        var removeButton = row.transform.Find("RemoveButton")?.GetComponent<Button>();
        if (removeButton != null)
            PopupUI.WireFingerUp(removeButton, () => DropPendingRow(row, contentType));

        var rowButton = row.GetComponent<Button>();
        if (rowButton != null)
        {
            rowButton.interactable = true;
            PopupUI.WireFingerUp(rowButton, () =>
            {
                DropPendingRow(row, contentType);
                retry?.Invoke();
            });
        }
    }

    private void DropPendingRow(GameObject row, string contentType)
    {
        var pending = contentType == "product" ? pendingProductFileRows : pendingServiceFileRows;
        pending.Remove(row);
        if (row != null)
        {
            row.SetActive(false); // VLG skips inactive children — reflow now
            Destroy(row);
        }
        RefreshUploadedFiles();
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

    // POST {n8nBaseUrl}/webhook/DeleteFile { fileId } — the n8n workflow removes
    // every RAG chunk tagged with this fileId, so the bot genuinely forgets the
    // file. Shared by the X-button delete and the replace-on-reupload path.
    private IEnumerator DeleteFileChunksRequest(string fileId, System.Action<bool> callback)
    {
        string url = $"{Manager.n8nBaseUrl}/webhook/DeleteFile";
        string body = JsonConvert.SerializeObject(new { fileId });

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[DeleteFile] [{request.responseCode}] {url}: {request.error}\n{request.downloadHandler?.text}");
            callback?.Invoke(false);
            yield break;
        }

        callback?.Invoke(true);
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
