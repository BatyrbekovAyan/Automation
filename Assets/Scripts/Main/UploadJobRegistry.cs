using System.Collections.Generic;

public enum UploadJobState { Uploading, Failed }

/// <summary>
/// One in-flight (or failed) price-list upload. Plain data with no Unity
/// lifetime of its own — see <see cref="UploadJobRegistry"/> for why that
/// matters.
/// </summary>
public class UploadJob
{
    public string BotName;
    public string ContentType;            // "product" | "service"
    public string FileName;               // display name shown in the row
    public string FilePath;               // picker path, re-read on retry
    public string DisplayNameOverride;    // set for gallery picks (see GalleryPhotoNamer)
    public string FileId;                 // stamped onto every RAG chunk; re-minted per attempt
    public UploadJobState State;
    public string FailureReason;          // user-facing RU; null while uploading
    public bool CanRetry;                 // false for deterministic failures (bad format, empty file)

    /// <summary>
    /// The user answered «Заменить?» for this file. Gates the post-upload delete
    /// of same-named chunks — retry re-enters the upload BELOW that question, so
    /// without carrying the answer a retry would delete someone's file silently.
    /// </summary>
    public bool ReplaceConfirmed;

    /// <summary>
    /// Set when the job is delisted (bot deleted, row dismissed). Sticky, and
    /// checked by the running coroutine: delisting cannot stop a coroutine that
    /// lives on UploadCenter and holds its own reference to this job.
    /// </summary>
    public bool Cancelled;
}

/// <summary>
/// The list of uploads currently in flight, keyed by bot + content type.
///
/// Uploads used to run as coroutines on the BotSettings MonoBehaviour, which
/// meant leaving the screen killed them: Unity stops coroutines permanently
/// when their GameObject goes inactive, so the request never reached either
/// completion branch. The row kept pulsing «Загрузка…» forever and no store
/// entry was ever written — while n8n, which does not care that the client
/// walked away, finished ingesting the file under a fileId the app had just
/// forgotten. Re-uploading then passed the duplicate check and indexed the
/// same price list a second time, with the first copy unreachable by the ✕.
///
/// So upload state lives here — owned by <see cref="UploadCenter"/>, which
/// never deactivates — and the settings screen merely renders it.
/// </summary>
public class UploadJobRegistry
{
    private readonly List<UploadJob> jobs = new();

    /// <summary>Any add/resolve/remove. Views re-render on this.</summary>
    public event System.Action OnChanged;

    public UploadJob Add(string botName, string contentType, string filePath, string fileName,
                         string displayNameOverride = null, bool replaceConfirmed = false)
    {
        if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(contentType))
            return null;

        var job = new UploadJob
        {
            BotName = botName,
            ContentType = contentType,
            FilePath = filePath,
            FileName = fileName,
            DisplayNameOverride = displayNameOverride,
            FileId = NewFileId(),
            ReplaceConfirmed = replaceConfirmed,
            State = UploadJobState.Uploading
        };
        jobs.Add(job);
        OnChanged?.Invoke();
        return job;
    }

    /// <summary>Jobs for one tab, in the order the files were picked.</summary>
    public List<UploadJob> JobsFor(string botName, string contentType)
    {
        var matches = new List<UploadJob>();
        foreach (UploadJob job in jobs)
            if (job.BotName == botName && job.ContentType == contentType)
                matches.Add(job);
        return matches;
    }

    public void MarkFailed(UploadJob job, string reason, bool canRetry)
    {
        if (job == null) return;

        job.State = UploadJobState.Failed;
        job.FailureReason = reason;
        job.CanRetry = canRetry;
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Puts a failed job back in flight. Mints a FRESH fileId: the abandoned
    /// attempt may already have inserted chunks server-side under the old one,
    /// and reusing it would make the two indistinguishable to the per-file
    /// delete.
    /// </summary>
    public void MarkUploading(UploadJob job)
    {
        if (job == null) return;

        job.State = UploadJobState.Uploading;
        job.FailureReason = null;
        job.CanRetry = false;
        job.FileId = NewFileId();
        OnChanged?.Invoke();
    }

    public bool Remove(UploadJob job)
    {
        if (job == null || !jobs.Remove(job)) return false;

        job.Cancelled = true;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Drops a deleted bot's jobs and FLAGS them cancelled. The flag is the part
    /// that matters: their upload coroutines run on UploadCenter and are not
    /// stopped by delisting, so without it a completion arriving after
    /// Bot.DeleteBot would re-create the PlayerPrefs keys the delete just removed.
    /// </summary>
    public void RemoveForBot(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;

        int removed = jobs.RemoveAll(job =>
        {
            if (job.BotName != botName) return false;
            job.Cancelled = true;
            return true;
        });
        if (removed > 0) OnChanged?.Invoke();
    }

    private static string NewFileId() => System.Guid.NewGuid().ToString();
}
