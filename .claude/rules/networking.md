---
paths:
  - "Assets/Scripts/Chat/**/*.cs"
  - "Assets/Scripts/Main/Manager.cs"
---

# Unity Networking Standards

## Coroutine Pattern (Mandatory)
```csharp
private IEnumerator FetchData(string param, System.Action<T> callback)
{
    string url = $"{BASE_URL}endpoint/{param}";
    using (var request = UnityWebRequest.Get(url))
    {
        request.SetRequestHeader("Authorization", secrets.wappiAuthToken);
        request.timeout = 30;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[{request.responseCode}] {url}: {request.error}");
            callback?.Invoke(default);
            yield break;
        }

        var data = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
        callback?.Invoke(data);
    }
}
```

## Rules
- NEVER use async/await in MonoBehaviours — always coroutines with IEnumerator
- NEVER hardcode tokens — use Secrets class
- ALWAYS set request.timeout = 30
- ALWAYS check request.result before parsing
- ALWAYS use `using` block or call Dispose() on UnityWebRequest
- ALWAYS log errors with status code and URL
- Parse JSON with JsonConvert.DeserializeObject<T>() (Newtonsoft)
- Response model classes go in Assets/Scripts/Chat/
- Use System.Action<T> callbacks for async results

## API Headers
- Wappi (WhatsApp/Telegram): `Authorization` header
- n8n: `X-N8N-API-KEY` header
- Green API: token in URL path

## POST Requests
```csharp
using (var request = new UnityWebRequest(url, "POST"))
{
    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
    request.downloadHandler = new DownloadHandlerBuffer();
    request.SetRequestHeader("Content-Type", "application/json");
    // ... same error handling pattern
}
```
