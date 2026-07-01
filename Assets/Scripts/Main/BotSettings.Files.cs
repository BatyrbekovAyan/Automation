using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    private UploadedFileEntry pendingDeleteEntry;
    private string pendingDeleteContentType;
    private GameObject pendingDeleteRow;
    private bool deleteFileInFlight;

    private static readonly string[] RuMonthsShort =
        { "янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" };

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
                        uploadedProductFileRowTemplate, spawnedProductFileRows);
        RefreshFilesTab(openBot, "service", uploadedServiceFilesSection, uploadedServiceFilesParent,
                        uploadedServiceFileRowTemplate, spawnedServiceFileRows);
    }

    private void RefreshFilesTab(Bot openBot, string contentType, GameObject section,
                                 RectTransform rowsParent, GameObject template, List<GameObject> spawned)
    {
        if (section == null || rowsParent == null || template == null) return;

        foreach (var row in spawned)
            if (row != null) Destroy(row);
        spawned.Clear();

        var files = openBot != null
            ? UploadedFilesStore.Load(openBot.name, contentType)
            : new List<UploadedFileEntry>();

        section.SetActive(files.Count > 0);
        if (files.Count == 0) return;

        foreach (var entry in files)
        {
            var row = Instantiate(template, rowsParent);
            row.SetActive(true);
            BindFileRow(row, entry, contentType);
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

    // POST {n8nBaseUrl}/webhook/DeleteFile { fileId } — the n8n workflow removes
    // every RAG chunk tagged with this fileId, so the bot genuinely forgets the
    // file. Local record + row are dropped only after the server confirms.
    private IEnumerator DeleteUploadedFileRoutine(UploadedFileEntry entry, string contentType, GameObject row)
    {
        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        if (openBot == null) yield break;

        deleteFileInFlight = true;
        string url = $"{Manager.n8nBaseUrl}/webhook/DeleteFile";
        string body = JsonConvert.SerializeObject(new { fileId = entry.Id });

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;
        yield return request.SendWebRequest();
        deleteFileInFlight = false;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[DeleteFile] [{request.responseCode}] {url}: {request.error}\n{request.downloadHandler?.text}");
            yield break; // keep the row — the bot still knows this file
        }

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
