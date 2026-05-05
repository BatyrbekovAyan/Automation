---
name: unity-api-integration
description: Implement REST API endpoints in Unity following project patterns. Use when adding new API calls, webhooks, or external service integrations.
allowed-tools: Bash(find *) Read(*) Edit(*) Write(*) Glob(*) Grep(*)
---

# Unity API Integration — Standard Implementation

## Before Writing Code

1. **Check existing patterns** — Read `Assets/Scripts/Main/Manager.cs` for how current API calls are structured
2. **Check Secrets** — Read `Assets/Scripts/Main/Secrets.cs` to find available auth tokens
3. **Check existing models** — Read `Assets/Scripts/Chat/` for response model classes that can be reused

## Standard Pattern

### GET Request
```csharp
private IEnumerator GetSomething(string id, System.Action<SomeResponse> onComplete)
{
    string url = $"{BASE_URL}endpoint/{id}";

    using (UnityWebRequest request = UnityWebRequest.Get(url))
    {
        request.SetRequestHeader("Authorization", secrets.wappiAuthToken);
        request.timeout = 30;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[API Error {request.responseCode}] {url}: {request.error}");
            onComplete?.Invoke(null);
            yield break;
        }

        var response = JsonConvert.DeserializeObject<SomeResponse>(
            request.downloadHandler.text);
        onComplete?.Invoke(response);
    }
}
```

### POST Request with JSON Body
```csharp
private IEnumerator PostSomething(SomeRequest data, System.Action<SomeResponse> onComplete)
{
    string url = $"{BASE_URL}endpoint";
    string json = JsonConvert.SerializeObject(data);

    using (var request = new UnityWebRequest(url, "POST"))
    {
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", secrets.wappiAuthToken);
        request.timeout = 30;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[API Error {request.responseCode}] {url}: {request.error}");
            onComplete?.Invoke(null);
            yield break;
        }

        var response = JsonConvert.DeserializeObject<SomeResponse>(
            request.downloadHandler.text);
        onComplete?.Invoke(response);
    }
}
```

## Rules

1. **Auth**: Always from Secrets class — NEVER hardcode tokens
2. **Timeout**: Always set `request.timeout = 30`
3. **Error handling**: Check `request.result` before parsing, log with status code + URL
4. **Disposal**: Use `using` block on UnityWebRequest
5. **Async pattern**: Coroutines with `System.Action<T>` callbacks — NEVER async/await
6. **Models**: Serializable response classes in `Assets/Scripts/Chat/`
7. **Base URLs**: Reuse existing constants from Manager.cs
8. **Headers**: Wappi = `Authorization`, n8n = `X-N8N-API-KEY`, Green API = token in URL

## Response Model Template
```csharp
[System.Serializable]
public class SomeResponse
{
    public bool status;
    public string message;
    public SomeData data;
}

[System.Serializable]
public class SomeData
{
    public string id;
    public string name;
}
```

## Checklist
- [ ] Token loaded from Secrets class
- [ ] Timeout set to 30s
- [ ] Error logged with status code and URL
- [ ] UnityWebRequest properly disposed (using block)
- [ ] Response model created/reused in Chat/ directory
- [ ] Coroutine started with StartCoroutine()
- [ ] Callback handles null (error case)
