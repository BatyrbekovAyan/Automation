using System.Collections;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Fetches the Task 11 usage/quota snapshot from n8n's <c>/webhook/GetUsage</c> and applies it
/// to <see cref="UsageStore"/>. Static entry point (no MonoBehaviour of its own) so any caller
/// with a coroutine host — <c>Manager.PreloadSecretsThenInitBilling</c> at boot,
/// <c>BotsPage.OnEnable</c> when the Bots tab becomes visible — can fire it directly via
/// <c>StartCoroutine(UsageClient.FetchRoutine())</c>.
///
/// Request shape mirrors DashboardPage.FetchRoutine / N8nSuggestionsProvider (raw JSON body,
/// explicit Content-Type — Unity's libcurl transport stamps
/// application/x-www-form-urlencoded on a bodyless/default POST and n8n's webhook 415s a
/// non-JSON content type otherwise). GetUsage carries no auth (v1 posture: the appUserId IS
/// the secret, same posture as this app's other webhooks — see CLAUDE.md), so no API key
/// header is sent, only Manager.n8nBaseUrl for the base URL (same source every other n8n
/// webhook call in this app already uses).
/// </summary>
public static class UsageClient
{
    // There are now five trigger sites (boot, the Bots tab becoming visible, the «Боты»
    // billing strip, the «Подписка» page opening, and after a top-up/restore), several of
    // which can land in the same frame — a tab switch that opens the page, or a fast
    // double-tap. Overlapping fetches were harmless (the read is idempotent and Apply just
    // replaces the snapshot) but they doubled the request load and let an older response
    // land last, so the guard collapses a burst to one in-flight read.
    private static bool _inFlight;

    /// <summary>
    /// Pure decision seam: a fetch starts only when no other one is running. Refuse rather
    /// than queue — the response carries the CURRENT usage, so a coalesced second call
    /// would return the same thing the in-flight one is about to deliver.
    /// </summary>
    public static bool ShouldStart(bool inFlight) => !inFlight;

    internal static void ResetSeamsForTests() => _inFlight = false;

    public static IEnumerator FetchRoutine()
    {
        if (!ShouldStart(_inFlight)) yield break;

        string appUserId = BillingIdentity.AppUserId;
        if (string.IsNullOrEmpty(appUserId))
        {
            Debug.LogWarning("[UsageClient] no AppUserId yet — skipping fetch");
            yield break;
        }

        _inFlight = true;
        // try/finally (never try/catch — `yield return` is illegal inside a try that has a
        // catch). The finally also runs when Unity DISPOSES the iterator, which is what a
        // StopCoroutine, a disabled host or a destroyed host all reduce to — so the flag
        // cannot survive a coroutine that never reached its own end.
        try
        {
            string url = $"{Manager.n8nBaseUrl}/webhook/GetUsage";
            string body = JsonConvert.SerializeObject(new { appUserId });

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");   // REQUIRED (see Global Constraints)
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[UsageClient] fetch failed [{req.responseCode}] {req.error} — keeping cached usage");
                yield break;
            }

            var snapshot = UsageStore.Parse(req.downloadHandler.text);
            if (snapshot == null || !snapshot.success)
            {
                Debug.LogWarning("[UsageClient] fetch returned an unusable body — keeping cached usage");
                yield break;
            }

            UsageStore.Apply(snapshot);
        }
        finally
        {
            _inFlight = false;
        }
    }
}
