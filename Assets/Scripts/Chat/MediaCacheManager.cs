using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class MediaCacheManager : MonoBehaviour
{
    public static MediaCacheManager Instance;

    private const int MaxMemorySpriteCount = 100;
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, Sprite>>> spriteMemoryCache = new();
    private readonly LinkedList<KeyValuePair<string, Sprite>> spriteAccessOrder = new();

    // Memoized URL → cache-file path. Keyed by (botId, url) so a bot switch does not
    // serve another bot's file. Cleared on bot change.
    private readonly Dictionary<string, string> urlPathCache = new();
    private string cachedUrlBotId;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Per-bot media directory: {ChatManager.GetCacheRoot()}/media/.
    /// Created on first access.
    /// </summary>
    private string GetMediaDirectory()
    {
        string root = ChatManager.Instance != null
            ? ChatManager.Instance.GetCacheRoot()
            : Path.Combine(Application.persistentDataPath, "BotCache", "_default");

        string mediaDir = Path.Combine(root, "media");
        if (!Directory.Exists(mediaDir)) Directory.CreateDirectory(mediaDir);
        return mediaDir;
    }

    public Sprite GetSpriteFromMemory(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!spriteMemoryCache.TryGetValue(url, out var node)) return null;

        spriteAccessOrder.Remove(node);
        spriteAccessOrder.AddFirst(node);
        return node.Value.Value;
    }

    public void StoreSpriteInMemory(string url, Sprite sprite)
    {
        if (string.IsNullOrEmpty(url) || sprite == null) return;

        if (spriteMemoryCache.TryGetValue(url, out var existing))
        {
            spriteAccessOrder.Remove(existing);
            spriteAccessOrder.AddFirst(existing);
            return;
        }

        var node = new LinkedListNode<KeyValuePair<string, Sprite>>(
            new KeyValuePair<string, Sprite>(url, sprite));
        spriteAccessOrder.AddFirst(node);
        spriteMemoryCache[url] = node;

        while (spriteMemoryCache.Count > MaxMemorySpriteCount)
        {
            var tail = spriteAccessOrder.Last;
            spriteAccessOrder.RemoveLast();
            spriteMemoryCache.Remove(tail.Value.Key);
        }
    }

    public bool IsImageCached(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        string filePath = GetFilePathFromUrl(url);
        return File.Exists(filePath);
    }

    public void SaveImageToCache(string url, byte[] imageData)
    {
        if (string.IsNullOrEmpty(url) || imageData == null || imageData.Length == 0) return;

        string filePath = GetFilePathFromUrl(url);
        File.WriteAllBytesAsync(filePath, imageData);
    }

    public Texture2D LoadImageFromCache(string url)
    {
        if (!IsImageCached(url)) return null;

        string filePath = GetFilePathFromUrl(url);
        byte[] fileData = File.ReadAllBytes(filePath);

        Texture2D texture = new Texture2D(2, 2);
        if (texture.LoadImage(fileData))
        {
            return texture;
        }

        return null;
    }

    /// <summary>
    /// URL → MD5-hashed file path under the active bot's media directory.
    /// Memoization is invalidated when the active bot changes.
    /// </summary>
    public string GetFilePathFromUrl(string url)
    {
        string activeBotId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : "_default";

        if (cachedUrlBotId != activeBotId)
        {
            urlPathCache.Clear();
            cachedUrlBotId = activeBotId;
        }

        if (urlPathCache.TryGetValue(url, out var cachedPath)) return cachedPath;

        using MD5 md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(url);
        byte[] hashBytes = md5.ComputeHash(inputBytes);

        StringBuilder sb = new StringBuilder(hashBytes.Length * 2);
        for (int i = 0; i < hashBytes.Length; i++)
            sb.Append(hashBytes[i].ToString("X2"));

        string path = Path.Combine(GetMediaDirectory(), sb.ToString() + ".jpg");
        urlPathCache[url] = path;
        return path;
    }

    /// <summary>
    /// Clear the active bot's media cache. Used by ChatManager.PurgeCacheForBot
    /// when needed; routine deletion of a non-active bot wipes the directory directly.
    /// </summary>
    public void ClearCache()
    {
        string mediaDir = GetMediaDirectory();
        if (Directory.Exists(mediaDir))
        {
            DirectoryInfo dir = new DirectoryInfo(mediaDir);
            foreach (FileInfo file in dir.GetFiles())
            {
                file.Delete();
            }
        }
        urlPathCache.Clear();
    }
}
