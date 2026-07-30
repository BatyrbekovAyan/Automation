using System.Collections.Generic;

/// <summary>
/// When the launch sweep may forget a pending upload.
///
/// The sweep exists to reclaim RAG chunks whose fileId nothing on-device
/// remembers. But it can also land INSIDE the ingest window — the app is killed
/// after the POST, relaunched seconds later, and n8n has not finished embedding
/// yet. The DeleteFile webhook then matches zero chunks while its parallel
/// branch still removes the archived original from the price-lists bucket, so
/// treating "HTTP 200" as reclaimed would destroy the archive AND leave the
/// chunks — which land moments later — orphaned for good.
///
/// So a zero-chunk delete is retried on later launches. Bounded, because a
/// deterministically-failed upload legitimately owns a fileId with no chunks at
/// all, and its entry would otherwise be immortal.
/// </summary>
public static class UploadSweepPolicy
{
    public const int MaxAttempts = 3;

    /// <param name="deletedChunks">Rows the webhook reported deleting; -1 when unknown.</param>
    /// <param name="attempts">Sweeps made for this fileId so far, including this one.</param>
    public static bool ShouldSettle(bool requestSucceeded, int deletedChunks, int attempts,
                                    int maxAttempts = MaxAttempts)
    {
        if (!requestSucceeded) return false;   // offline at launch — retry next time
        if (deletedChunks > 0) return true;    // genuinely reclaimed

        // Zero or unknown: either we beat the ingest, or there was never
        // anything to delete. Give it a few launches to settle either way.
        return attempts >= maxAttempts;
    }
}

/// <summary>Reads the DeleteFile webhook's reply: { success, fileId, deletedChunks }.</summary>
public static class DeleteFileResponse
{
    /// <summary>Rows deleted, or -1 when the body is missing, malformed, or lacks the field.</summary>
    public static int ParseDeletedChunks(string body)
    {
        if (string.IsNullOrEmpty(body)) return -1;

        try
        {
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(body);
            Newtonsoft.Json.Linq.JToken token = parsed["deletedChunks"];
            if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null) return -1;
            return token.ToObject<int>(); // throws on a non-numeric value — caught below
        }
        catch (System.Exception)
        {
            return -1;
        }
    }
}

/// <summary>
/// Every price-list name already spoken for on a tab — stored uploads AND the
/// ones still in flight.
///
/// Gallery photos are named from a clock stamp accurate to the minute, and the
/// set they were de-duped against came from the store alone. The store is only
/// written on completion, so picking a second photo while the first was still
/// uploading produced two files with the identical name — and the second
/// completion's replace cleanup then deleted the first one's chunks and its
/// archived original, with no prompt, because the same store-only lookup backs
/// the «Заменить?» gate.
/// </summary>
public static class UploadNameSet
{
    public static HashSet<string> TakenNames(List<UploadedFileEntry> stored, List<UploadJob> inFlight)
    {
        var names = new HashSet<string>();

        if (stored != null)
            foreach (UploadedFileEntry entry in stored)
                if (!string.IsNullOrEmpty(entry.Name)) names.Add(entry.Name);

        if (inFlight != null)
            foreach (UploadJob job in inFlight)
                if (job != null && !string.IsNullOrEmpty(job.FileName)) names.Add(job.FileName);

        return names;
    }
}
