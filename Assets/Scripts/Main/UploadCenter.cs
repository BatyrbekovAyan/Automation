using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Runs price-list uploads and per-file deletes on a GameObject that nothing
/// ever deactivates.
///
/// Both used to be coroutines on BotSettings, which meant backing out of the
/// screen killed them mid-request — Unity stops a GameObject's coroutines for
/// good when it goes inactive. n8n finishes ingesting either way, so the app
/// was left showing an eternal «Загрузка…» row while the file quietly landed
/// in the RAG store under a fileId nothing on-device remembered; re-uploading
/// then sailed past the duplicate check and indexed the same price list
/// twice, the first copy permanently unreachable by the ✕.
///
/// The screen now only renders <see cref="Jobs"/> — the work outlives it.
/// </summary>
public class UploadCenter : MonoBehaviour
{
    private static UploadCenter instance;

    /// <summary>
    /// Lazily creates the host on first use. Null outside play mode so no
    /// editor-time code path can spawn a stray GameObject into the scene.
    /// </summary>
    public static UploadCenter Instance
    {
        get
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;

            instance = new GameObject(nameof(UploadCenter)).AddComponent<UploadCenter>();
            return instance;
        }
    }

    /// <summary>
    /// The host if it already exists, never creating one. Read and teardown
    /// paths use this — unsubscribing during play-mode exit must not spawn a
    /// fresh GameObject just to detach from it.
    /// </summary>
    public static UploadCenter Existing => instance != null ? instance : null;

    /// <summary>In-flight and failed uploads. The settings screen renders from this.</summary>
    public UploadJobRegistry Jobs { get; } = new UploadJobRegistry();

    /// <summary>Raised when an upload lands in the store (botName, contentType, fileId).</summary>
    public event System.Action<string, string, string> OnUploadCompleted;

    // Per-fileId guard so a double-tapped ✕ can't fire two DeleteFile calls.
    private readonly HashSet<string> deletesInFlight = new();

    private void Awake() => instance ??= this;

    ////////////////////////////////// UPLOAD //////////////////////////////////

    /// <param name="replaceConfirmed">The user answered «Заменить?» for this name. Only then
    /// may the completed upload delete the same-named file's chunks.</param>
    public void StartUpload(string botName, string contentType, string filePath, string fileName,
                            string displayNameOverride = null, bool replaceConfirmed = false)
    {
        UploadJob job = Jobs.Add(botName, contentType, filePath, fileName, displayNameOverride, replaceConfirmed);
        if (job != null) StartCoroutine(RunUpload(job));
    }

    /// <summary>Re-runs a failed upload. The registry mints a fresh fileId first.</summary>
    public void Retry(UploadJob job)
    {
        if (job == null || job.State != UploadJobState.Failed) return;

        Jobs.MarkUploading(job);
        StartCoroutine(RunUpload(job));
    }

    /// <summary>Drops a failed row the user dismissed with the ✕.</summary>
    public void Dismiss(UploadJob job) => Jobs.Remove(job);

    /// <summary>Called from Bot.DeleteBot — a late completion must not resurrect a deleted bot's keys.</summary>
    public void CancelForBot(string botName) => Jobs.RemoveForBot(botName);

    private IEnumerator RunUpload(UploadJob job)
    {
        // Resolved live rather than captured: the ids can change under a
        // re-auth between a failure and its retry.
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(job.BotName) : null;
        if (bot == null)
        {
            Debug.LogError($"[UploadFile] Bot '{job.BotName}' no longer exists — dropping upload of '{job.FileName}'.");
            Jobs.Remove(job);
            yield break;
        }

        byte[] fileData = ReadFileOrNull(job.FilePath);
        if (fileData == null)
        {
            // Transient (the picker's temp copy may just be gone) — keep retry.
            Jobs.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);
            yield break;
        }

        UploadPayload payload = UploadPayloadBuilder.Build(fileData, job.FileName, job.FilePath, job.ContentType);
        if (!payload.Ok)
        {
            // Deterministic: the same file will fail the same way, so the row
            // shows WHY (in Russian) and offers no retry — only the ✕.
            Debug.LogError($"[UploadFile] '{job.FileName}': {payload.FailReason} — upload aborted.");
            Jobs.MarkFailed(job, payload.FailReasonRu, canRetry: false);
            yield break;
        }

        WWWForm form = new();
        form.AddField("whatsappWorkflowId", bot.whatsappWorkflowId);
        form.AddField("telegramWorkflowId", bot.telegramWorkflowId);
        form.AddField("contentType", job.ContentType);
        // The workflow stamps this onto every RAG chunk (metadata.fileId), so
        // the per-file delete (✕) can later remove exactly this file's chunks.
        form.AddField("fileId", job.FileId);
        form.AddBinaryData("data", payload.Bytes, payload.Name, payload.Mime);

        // Last thing before the bytes leave: from here on n8n may ingest them
        // whatever happens to this process, so the fileId has to exist on disk.
        // Everything above this line fails without creating a single chunk.
        PendingUploadLedger.Add(job.FileId, job.BotName, job.ContentType);

        using UnityWebRequest www = UnityWebRequest.Post($"{Manager.n8nBaseUrl}/webhook/UploadFile", form);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            // Deterministic server verdicts (e.g. a photo with no visible prices)
            // retrying the same file cannot fix — surface the specific reason and
            // suppress retry, same as the client-side deterministic failures above.
            string deterministicReason = UploadFailureText.ReasonForHttpResponse(www.responseCode, www.downloadHandler?.text);
            if (deterministicReason != null)
            {
                Debug.LogError($"[UploadFile] '{job.FileName}': {deterministicReason} ({www.responseCode})");
                Jobs.MarkFailed(job, deterministicReason, canRetry: false);
                yield break;
            }

            Debug.LogError($"[UploadFile] Upload failed ({www.responseCode} {www.result}): {www.error}\n{www.downloadHandler?.text}");
            Jobs.MarkFailed(job, UploadFailureText.TapToRetry, canRetry: true);
            yield break;
        }

        // The bot can be deleted (or the row dismissed) while these bytes are on
        // the wire. Delisting the job does NOT stop this coroutine — it lives on
        // UploadCenter — so without this check the writes below would re-create
        // the very PlayerPrefs keys Bot.DeleteBot just removed. The ledger entry
        // is deliberately left behind: the launch sweep reclaims the chunks.
        if (job.Cancelled || Manager.Instance == null || Manager.Instance.FindBotByName(job.BotName) == null)
        {
            Debug.LogWarning($"[UploadFile] '{job.FileName}' completed after its bot went away — " +
                             "discarding the local record; the launch sweep will reclaim the chunks.");
            Jobs.Remove(job);
            yield break;
        }

        // Re-uploading a same-named file REPLACES it: delete the superseded
        // upload's RAG chunks (by old fileId), or stale and current prices would
        // coexist in the vector store and retrieval could quote either. The new
        // chunks are already inserted under a fresh fileId, so this is safe.
        // ONLY with consent: Retry re-enters this coroutine below the «Заменить?»
        // question, and a same-named file can appear between the pick and the
        // completion. Deleting on a bare name match would then destroy a file the
        // user never agreed to replace. Without consent both copies simply stay.
        if (job.ReplaceConfirmed)
        {
            foreach (UploadedFileEntry stale in UploadedFilesStore.FindByName(job.BotName, job.ContentType, job.FileName))
            {
                if (stale.Id == job.FileId) continue; // never target the fresh upload
                StartCoroutine(DeleteReplacedFileRoutine(job.BotName, job.ContentType, stale.Id));
            }
        }

        // Remember the upload on-device so the file survives closing/reopening the bot,
        // and so the per-file delete (✕) can target this fileId in the RAG store.
        UploadedFilesStore.Add(job.BotName, job.ContentType, new UploadedFileEntry
        {
            Id = job.FileId,
            Name = job.FileName,
            Size = fileData.Length,
            DateUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        // Recorded — no longer an orphan candidate. Deliberately after the store
        // write: a kill between the two leaves a stale entry, which the launch
        // sweep recognises as already-recorded and settles without deleting.
        PendingUploadLedger.Remove(job.FileId);

        string botName = job.BotName, contentType = job.ContentType, fileId = job.FileId;
        Jobs.Remove(job); // the pending row is now a real stored row
        OnUploadCompleted?.Invoke(botName, contentType, fileId);

        // D3: the checklist's «Загрузить прайс-лист» row derives from UploadedFilesStore —
        // refresh so it flips to done immediately (fire-and-forget, null-guarded).
        FirstStepsCard.Instance?.RefreshFromFacts();
    }

    private static byte[] ReadFileOrNull(string filePath)
    {
        try
        {
            return System.IO.File.ReadAllBytes(filePath);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[UploadFile] Could not read '{filePath}': {exception.Message}");
            return null;
        }
    }

    ////////////////////////////////// DELETE //////////////////////////////////

    /// <summary>
    /// The ✕ delete: server first, local record dropped only once the server
    /// confirms. Hosted here so backing out mid-delete can't strand it (that
    /// used to leave a latch stuck true and silently kill every later delete).
    /// </summary>
    /// <param name="onDone">(deleted, deletedChunks) — deletedChunks is -1 when the reply was unreadable.</param>
    public void DeleteFile(string botName, string contentType, string fileId, System.Action<bool, int> onDone = null)
    {
        if (string.IsNullOrEmpty(fileId) || !deletesInFlight.Add(fileId)) return;

        StartCoroutine(DeleteFileRoutine(botName, contentType, fileId, onDone));
    }

    public bool IsDeleting(string fileId) => fileId != null && deletesInFlight.Contains(fileId);

    private IEnumerator DeleteFileRoutine(string botName, string contentType, string fileId,
                                          System.Action<bool, int> onDone)
    {
        bool deleted = false;
        int deletedChunks = -1;
        yield return DeleteFileChunksRequest(fileId, (success, chunks) =>
        {
            deleted = success;
            deletedChunks = chunks;
        });
        deletesInFlight.Remove(fileId);

        // For a user-initiated delete, zero chunks means they were already gone
        // server-side — still drop the local record so the list reflects reality.
        // A failure keeps the row: the bot still knows this file.
        if (deleted) UploadedFilesStore.Remove(botName, contentType, fileId);

        onDone?.Invoke(deleted, deletedChunks);
        if (deleted && Manager.openBotSettings != null) Manager.openBotSettings.RefreshUploadedFiles();
    }

    /// <summary>
    /// Replace-on-reupload cleanup: a fresh upload with the same file name
    /// superseded this entry, so its stale RAG chunks (old prices!) must not
    /// keep answering. Keeps the record on failure so a later manual ✕ can
    /// retry the cleanup.
    /// </summary>
    private IEnumerator DeleteReplacedFileRoutine(string botName, string contentType, string staleFileId)
    {
        bool deleted = false;
        yield return DeleteFileChunksRequest(staleFileId, (success, _) => deleted = success);
        if (!deleted) yield break;

        UploadedFilesStore.Remove(botName, contentType, staleFileId);
        if (Manager.openBotSettings != null) Manager.openBotSettings.RefreshUploadedFiles();
    }

    // POST {n8nBaseUrl}/webhook/DeleteFile { fileId } — the n8n workflow removes
    // every RAG chunk tagged with this fileId, so the bot genuinely forgets the file.
    private IEnumerator DeleteFileChunksRequest(string fileId, System.Action<bool, int> callback)
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
            callback?.Invoke(false, -1);
            yield break;
        }

        callback?.Invoke(true, DeleteFileResponse.ParseDeletedChunks(request.downloadHandler?.text));
    }
}
