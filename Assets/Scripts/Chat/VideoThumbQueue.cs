using System.Collections.Generic;

/// <summary>
/// Pure dedup + concurrency bookkeeping for incoming-video thumbnail extraction.
/// No UnityEngine dependency, so it is EditMode-unit-testable without the runtime.
/// The driver (ChatManager.VideoThumbs) owns the coroutines and the durable
/// cache-file check; this class only tracks which ids are queued / in-flight / done
/// and how many may run at once. Durable cross-session de-dup is the cache file,
/// not the in-memory 'known' set (which Clear() wipes on bot switch).
/// </summary>
public class VideoThumbQueue
{
    private readonly int maxConcurrent;
    private readonly Queue<string> pending = new();
    private readonly HashSet<string> known = new();      // queued OR in-flight OR completed this session
    private readonly HashSet<string> inFlight = new();

    public VideoThumbQueue(int maxConcurrent = 2)
    {
        this.maxConcurrent = maxConcurrent < 1 ? 1 : maxConcurrent;
    }

    public int InFlightCount => inFlight.Count;
    public int PendingCount => pending.Count;

    /// <summary>Queues an id unless already known (queued/in-flight/completed). Returns true if newly queued.</summary>
    public bool TryEnqueue(string messageId)
    {
        if (string.IsNullOrEmpty(messageId)) return false;
        if (!known.Add(messageId)) return false;
        pending.Enqueue(messageId);
        return true;
    }

    /// <summary>Returns up to (maxConcurrent - inFlight) ids to start now, moving them to in-flight.</summary>
    public List<string> Dispatch()
    {
        var started = new List<string>();
        while (inFlight.Count < maxConcurrent && pending.Count > 0)
        {
            string id = pending.Dequeue();
            inFlight.Add(id);
            started.Add(id);
        }
        return started;
    }

    /// <summary>Marks an in-flight id finished (success or fail), freeing a slot. Stays 'known' for the session.</summary>
    public void Complete(string messageId)
    {
        inFlight.Remove(messageId);
    }

    /// <summary>Drops all state (bot switch / chat close). The durable de-dup is the cache file.</summary>
    public void Clear()
    {
        pending.Clear();
        known.Clear();
        inFlight.Clear();
    }
}
