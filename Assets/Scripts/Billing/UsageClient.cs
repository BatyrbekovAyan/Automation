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
    public static IEnumerator FetchRoutine()
    {
        string appUserId = BillingIdentity.AppUserId;
        if (string.IsNullOrEmpty(appUserId))
        {
            Debug.LogWarning("[UsageClient] no AppUserId yet — skipping fetch");
            yield break;
        }

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
}
