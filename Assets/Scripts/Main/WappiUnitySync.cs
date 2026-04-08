using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class WappiUnitySync : MonoBehaviour
{
    [Header("Wappi Configuration")]
    public string apiToken = "";
    public string profileId = "39c779ea-57ff";
    public string chatId = "77022889848@c.us";

    [Header("Settings")]
    public int messageLimit = 20;

    // Use a button in the Inspector to trigger the sync
    [ContextMenu("Fetch Chat History")]
    public async void FetchHistory()
    {
        Debug.Log("--- Starting Wappi Sync ---");
        if (string.IsNullOrEmpty(apiToken))
            apiToken = Secrets.Data.wappiAuthToken;
        await GetChatHistory();
    }

    private async Task GetChatHistory()
    {
        // string url = $"https://wappi.pro/api/sync/messages/get?profile_id={profileId}&chat_id={chatId}&limit={messageLimit}";
        string url = $"https://wappi.pro/api/sync/messages/all/get?profile_id={profileId}&limit={messageLimit}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", apiToken);
            
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
// This will print the actual reason from Wappi (e.g., "invalid chatId")
                Debug.LogError($"Wappi Error Details: {request.downloadHandler.text}");
                Debug.LogError($"HTTP Error: {request.error}");                
                return;
            }

            var text = request.downloadHandler.text;
            System.IO.File.WriteAllText(
                Application.persistentDataPath + "/response.txt",
                text
            );
            Debug.Log("Saved to: " + Application.persistentDataPath);
            
            
            JObject json = JObject.Parse(request.downloadHandler.text);
            JArray messages = (JArray)json["messages"];

            if (messages == null) return;

            foreach (var msg in messages)
            {
// Print the whole message to see what's actually inside
                Debug.Log($"Full Message Data: {msg.ToString()}");

                // Check 'wh_type' which is common for call events in Wappi
                string whType = msg["wh_type"]?.ToString();
                string type = msg["type"]?.ToString();

                if (whType == "incoming_call" || type == "call")
                {
                    Debug.Log("<color=red>CALL DETECTED!</color>");
                    string from = msg["from"]?.ToString();
                    Debug.Log($"Call from: {from}");
                }
            }
        }
    }

    private async Task DownloadMedia(string messageId)
    {
        string url = $"https://wappi.pro/api/sync/message/media/download?profile_id={profileId}&message_id={messageId}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", apiToken);

            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
print("1");
            if (request.result == UnityWebRequest.Result.Success)
            {
                print("2");
                byte[] results = request.downloadHandler.data;
                string savePath = Path.Combine(Application.persistentDataPath, $"sticker_{messageId}.webp");

                // Check if response is JSON (Base64) or raw binary
                string rawText = request.downloadHandler.text;
                if (rawText.StartsWith("{\"body\":"))
                {
                    print("3");
                    var mediaJson = JObject.Parse(rawText);
                    results = Convert.FromBase64String(mediaJson["body"].ToString());
                }

                File.WriteAllBytes(savePath, results);
                Debug.Log($"<color=green>Saved Sticker to:</color> {savePath}");
            }
            else
            {
                Debug.LogError($"Wappi Error Details: {request.downloadHandler.text}");
                Debug.LogError($"HTTP Error: {request.error}");                
            }
        }
    }
}