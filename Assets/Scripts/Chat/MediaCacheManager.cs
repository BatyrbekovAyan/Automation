using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class MediaCacheManager : MonoBehaviour
{
    public static MediaCacheManager Instance;

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

    /// <summary>
    /// Drops cached entries that belonged to a previous active bot. Called by every
    /// public method whose result is bot-scoped, so a bot switch cannot serve stale
    /// data from urlPathCache.
    /// </summary>
    private void EnsureBotScoped()
    {
        string activeBotId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : "_default";
        if (cachedUrlBotId == activeBotId) return;

        cachedUrlBotId = activeBotId;
        urlPathCache.Clear();
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
        File.WriteAllBytes(filePath, imageData);
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

        Destroy(texture);
        return null;
    }

    /// <summary>
    /// Copies the cached bytes stored under <paramref name="fromUrl"/>'s key to
    /// <paramref name="toUrl"/>'s key. The cache is keyed by MD5 of the FULL URL, so
    /// when the server re-addresses an unchanged file (recovery file_link → hosted s3
    /// URL) the bytes are already on disk but the new key misses. The caller must
    /// establish that both URLs name the same stored file (MediaUrlIdentity.SameFile)
    /// — this method only moves bytes between keys, it does not judge identity.
    /// Returns true when the destination key ends up backed by a file.
    /// </summary>
    public bool TryAliasCachedImage(string fromUrl, string toUrl)
    {
        if (string.IsNullOrEmpty(fromUrl) || string.IsNullOrEmpty(toUrl)) return false;
        if (!IsImageCached(fromUrl)) return false;

        string fromPath = GetFilePathFromUrl(fromUrl);
        string toPath = GetFilePathFromUrl(toUrl);
        if (string.Equals(fromPath, toPath, System.StringComparison.Ordinal)) return true;
        if (File.Exists(toPath)) return true;

        try
        {
            File.Copy(fromPath, toPath);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[MediaCacheManager] Cache alias failed ({fromUrl} -> {toUrl}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// URL → MD5-hashed file path under the active bot's media directory.
    /// Memoization is invalidated when the active bot changes.
    /// </summary>
    public string GetFilePathFromUrl(string url)
    {
        EnsureBotScoped();

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
