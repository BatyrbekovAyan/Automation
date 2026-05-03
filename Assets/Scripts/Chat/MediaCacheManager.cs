using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class MediaCacheManager : MonoBehaviour
{
    public static MediaCacheManager Instance;

    private string cacheDirectory;

    private const int MaxMemorySpriteCount = 100;
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, Sprite>>> spriteMemoryCache = new();
    private readonly LinkedList<KeyValuePair<string, Sprite>> spriteAccessOrder = new();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Define the safe folder on the phone's hard drive
            cacheDirectory = Path.Combine(Application.persistentDataPath, "MediaCache");

            // If the folder doesn't exist yet, create it!
            if (!Directory.Exists(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Returns a previously-decoded sprite from memory, or null. O(1), bypasses disk + decode.
    /// </summary>
    public Sprite GetSpriteFromMemory(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!spriteMemoryCache.TryGetValue(url, out var node)) return null;

        spriteAccessOrder.Remove(node);
        spriteAccessOrder.AddFirst(node);
        return node.Value.Value;
    }

    /// <summary>
    /// Caches a decoded sprite in memory keyed by URL. LRU-evicted at MaxMemorySpriteCount.
    /// Evicted entries are dropped from the dictionary only — VMs/Images keep their references intact.
    /// </summary>
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

    /// <summary>
    /// Checks if we already have this file downloaded and saved on the phone.
    /// </summary>
    public bool IsImageCached(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        string filePath = GetFilePathFromUrl(url);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Saves the raw downloaded bytes directly to the phone's hard drive.
    /// </summary>
    public void SaveImageToCache(string url, byte[] imageData)
    {
        if (string.IsNullOrEmpty(url) || imageData == null || imageData.Length == 0) return;

        string filePath = GetFilePathFromUrl(url);
        
        // Write the bytes to the disk asynchronously so it doesn't freeze the app
        File.WriteAllBytesAsync(filePath, imageData);
    }

    /// <summary>
    /// Loads the image instantly from the phone's hard drive.
    /// </summary>
    public Texture2D LoadImageFromCache(string url)
    {
        if (!IsImageCached(url)) return null;

        string filePath = GetFilePathFromUrl(url);
        byte[] fileData = File.ReadAllBytes(filePath);

        // Create an empty texture and load the bytes into it
        Texture2D texture = new Texture2D(2, 2);
        if (texture.LoadImage(fileData))
        {
            return texture;
        }

        return null;
    }

    /// <summary>
    /// URLs contain illegal characters. This converts the URL into a safe, unique 32-character MD5 hash.
    /// </summary>
    public string GetFilePathFromUrl(string url) // <--- CHANGED TO PUBLIC
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(url);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            
            // Example: "https://example.com/image.jpg" becomes "9E107D9D372BB6826BD81D3542A419D6.jpg"
            return Path.Combine(cacheDirectory, sb.ToString() + ".jpg");
        }
    }
    
    /// <summary>
    /// Optional: Call this if you want to let the user clear their cache to save phone storage.
    /// </summary>
    public void ClearCache()
    {
        if (Directory.Exists(cacheDirectory))
        {
            DirectoryInfo dir = new DirectoryInfo(cacheDirectory);
            foreach (FileInfo file in dir.GetFiles())
            {
                file.Delete();
            }
        }
    }
}