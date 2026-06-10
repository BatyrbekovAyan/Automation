using System.IO;

/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) scratch-file isolation for extracted video
/// thumbnail frames.
///
/// Native extraction jobs survive their C# 30s timeout: the coroutine gives up and moves on,
/// but the abandoned native job keeps reading the remote video and eventually writes its frame
/// to whatever output path it was started with. Pointing jobs straight at the renderable
/// <c>vthumb://{id}</c> cache path let a stale/overlapping write land in (and permanently
/// poison — the on-disk file is the durable de-dup) a key the bubbles render from. Instead,
/// every attempt extracts into its own unique <c>.part</c> scratch path, and only a confirmed
/// success is promoted into the renderable path via <see cref="Commit"/>.
/// </summary>
public static class VideoThumbFiles
{
    /// <summary>
    /// Unique per-attempt scratch path beside <paramref name="finalPath"/> (same directory,
    /// so <see cref="Commit"/>'s move never crosses volumes). <paramref name="attemptSeq"/>
    /// must be unique per extraction attempt within the session.
    /// </summary>
    public static string TempPathFor(string finalPath, int attemptSeq) =>
        finalPath + "." + attemptSeq + ".part";

    /// <summary>
    /// Promotes a successfully extracted frame from its scratch path into the renderable
    /// cache path, replacing any previous file. Returns false when the scratch file is
    /// missing (native job claimed success but wrote nothing). Never throws.
    /// </summary>
    public static bool Commit(string tempPath, string finalPath)
    {
        if (string.IsNullOrEmpty(tempPath) || string.IsNullOrEmpty(finalPath)) return false;
        try
        {
            if (!File.Exists(tempPath)) return false;
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tempPath, finalPath);
            return true;
        }
        catch (IOException) { return false; }
        catch (System.UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Best-effort removal of a failed attempt's scratch file. A timed-out native job may
    /// re-create it after this runs; that orphan is harmless — it is never committed, so it
    /// can never be rendered. Never throws.
    /// </summary>
    public static void Discard(string tempPath)
    {
        if (string.IsNullOrEmpty(tempPath)) return;
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch (IOException) { }
        catch (System.UnauthorizedAccessException) { }
    }
}
