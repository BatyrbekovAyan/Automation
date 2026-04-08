using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Data structure for the JSON response
[System.Serializable]
public class AvatarResponse
{
    public bool existsWhatsapp;
    public string urlAvatar;
}

public class GreenApiAvatarFetcher : MonoBehaviour
{
    private string apiUrl => Secrets.Data.greenApiAvatar.apiUrl;
    private string idInstance => Secrets.Data.greenApiAvatar.idInstance;
    private string apiTokenInstance => Secrets.Data.greenApiAvatar.apiTokenInstance;

    // We use Actions to pass the result back when the coroutine finishes
    public IEnumerator GetChatAvatar(string targetChatId, Action<Texture2D> onSuccess, Action<string> onError)
    {
        string endpoint = $"{apiUrl}/waInstance{idInstance}/getAvatar/{apiTokenInstance}";
        string jsonPayload = $@"{{ ""chatId"": ""{targetChatId}"" }}";
        string avatarUrl = string.Empty;

        // Step 1: Ask Green-API for the Meta CDN URL
        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // Yield execution until the network request completes
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"API Error: {request.error}\nReceipt: {request.downloadHandler.text}");
                yield break; // Exit the coroutine early
            }

            AvatarResponse response = JsonUtility.FromJson<AvatarResponse>(request.downloadHandler.text);
            avatarUrl = response.urlAvatar;

            if (string.IsNullOrEmpty(avatarUrl))
            {
                onError?.Invoke("The avatar URL is empty. The user has no picture, or a firewall blocked the CDN.");
                yield break;
            }
            
            Debug.Log($"Successfully retrieved Meta CDN URL: {avatarUrl}");
        }

        // Step 2: Download the image texture directly from Meta's servers
        using (UnityWebRequest textureRequest = UnityWebRequestTexture.GetTexture(avatarUrl))
        {
            yield return textureRequest.SendWebRequest();

            if (textureRequest.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Failed to download texture: {textureRequest.error}");
                yield break;
            }

            // Extract the texture and trigger the success callback
            Texture2D downloadedTexture = DownloadHandlerTexture.GetContent(textureRequest);
            onSuccess?.Invoke(downloadedTexture);
        }
    }
}