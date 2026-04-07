using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class LinkScraper : MonoBehaviour
{
    public static LinkScraper Instance;

    // --- 1. THE DATA STRUCTURES ---
    // We add [Serializable] so Unity knows how to write this to a JSON file
    [Serializable]
    public class PreviewData
    {
        public string url; // We store the URL here so we can rebuild the dictionary later
        public string title;
        public string desc;
        public string image;
    }

    // Unity's JsonUtility cannot save Dictionaries directly, so we wrap it in a List
    [Serializable]
    private class CacheWrapper
    {
        public List<PreviewData> items = new List<PreviewData>();
    }

    private Dictionary<string, PreviewData> memoryCache = new Dictionary<string, PreviewData>();
    private string cacheFilePath;

    void Awake()
    {
        if (Instance == null) 
        {
            Instance = this;
            
            // Define the save file path on the phone's hard drive
            cacheFilePath = Path.Combine(Application.persistentDataPath, "link_metadata.json");
            
            // Wake up and load the memories!
            LoadCacheFromDisk();
        }
    }

    // ==========================================
    // DISK CACHING LOGIC
    // ==========================================
    private void LoadCacheFromDisk()
    {
        if (File.Exists(cacheFilePath))
        {
            string json = File.ReadAllText(cacheFilePath);
            CacheWrapper wrapper = JsonUtility.FromJson<CacheWrapper>(json);
            
            if (wrapper != null && wrapper.items != null)
            {
                foreach (var item in wrapper.items)
                {
                    memoryCache[item.url] = item; // Rebuild the RAM dictionary
                }
            }
        }
    }

    private void SaveCacheToDisk()
    {
        CacheWrapper wrapper = new CacheWrapper();
        wrapper.items = new List<PreviewData>(memoryCache.Values);
        
        string json = JsonUtility.ToJson(wrapper);
        File.WriteAllText(cacheFilePath, json); // Save to hard drive
    }

    // ==========================================
    // THE ROUTER
    // ==========================================
    public void FetchPreview(string url, Action<string, string, string> onComplete)
    {
        // Check RAM first (Instant load)
        if (memoryCache.TryGetValue(url, out var cachedData))
        {
            onComplete?.Invoke(cachedData.title, cachedData.desc, cachedData.image);
            return;
        }

        if (url.Contains("tiktok.com"))
        {
            StartCoroutine(ScrapeTikTokRoutine(url, onComplete));
        }
        else
        {
            StartCoroutine(ScrapeHtmlRoutine(url, onComplete));
        }
    }

    // ==========================================
    // TIKTOK O-EMBED ROUTINE
    // ==========================================
    private IEnumerator ScrapeTikTokRoutine(string url, Action<string, string, string> onComplete)
    {
        string oembedUrl = $"https://www.tiktok.com/oembed?url={url}";
        
        using UnityWebRequest www = UnityWebRequest.Get(oembedUrl);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            StartCoroutine(ScrapeHtmlRoutine(url, onComplete));
            yield break;
        }

        string json = www.downloadHandler.text;
        string title = ExtractJsonValue(json, "title");
        string image = ExtractJsonValue(json, "thumbnail_url");

        // --- NEW: Save to RAM and Hard Drive ---
        PreviewData newData = new PreviewData { url = url, title = title, desc = "TikTok - Make Your Day", image = image };
        memoryCache[url] = newData;
        SaveCacheToDisk();

        onComplete?.Invoke(newData.title, newData.desc, newData.image);
    }

    // ==========================================
    // STANDARD HTML & INSTAGRAM SCRAPER
    // ==========================================
    private IEnumerator ScrapeHtmlRoutine(string url, Action<string, string, string> onComplete)
    {
        string cleanUrl = url;
        int queryIndex = cleanUrl.IndexOf('?');
        if (queryIndex > 0 && (cleanUrl.Contains("instagram.com") || cleanUrl.Contains("tiktok.com")))
        {
            cleanUrl = cleanUrl.Substring(0, queryIndex);
        }

        using UnityWebRequest www = UnityWebRequest.Get(cleanUrl);
        www.SetRequestHeader("User-Agent", "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)");
        www.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            onComplete?.Invoke(null, null, null);
            yield break;
        }

        string html = www.downloadHandler.text;

        string title = ExtractMetaContent(html, "og:title") ?? ExtractMetaContent(html, "twitter:title");
        string desc = ExtractMetaContent(html, "og:description") ?? ExtractMetaContent(html, "twitter:description");
        string image = ExtractMetaContent(html, "og:image") ?? ExtractMetaContent(html, "twitter:image");

        if (!string.IsNullOrEmpty(image))
        {
            image = System.Net.WebUtility.HtmlDecode(image);
            image = image.Replace("\\u0026", "&"); 
        }

        if (!string.IsNullOrEmpty(image) && !image.StartsWith("http"))
        {
            try { image = new Uri(new Uri(cleanUrl), image).ToString(); } catch { }
        }

        if (string.IsNullOrEmpty(title))
        {
            Match titleMatch = Regex.Match(html, @"<title[^>]*>\s*(.+?)\s*</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success) title = titleMatch.Groups[1].Value;
        }

        if (!string.IsNullOrEmpty(title)) title = System.Net.WebUtility.HtmlDecode(title);
        if (!string.IsNullOrEmpty(desc)) desc = System.Net.WebUtility.HtmlDecode(desc);
        if (string.IsNullOrWhiteSpace(title)) title = null;

        // --- NEW: Save to RAM and Hard Drive ---
        PreviewData newData = new PreviewData { url = url, title = title, desc = desc, image = image };
        memoryCache[url] = newData;
        SaveCacheToDisk();

        onComplete?.Invoke(title, desc, image);
    }

    // ==========================================
    // HELPER METHODS
    // ==========================================
    private string ExtractMetaContent(string html, string property)
    {
        string pattern = $@"<meta[^>]+(?:property|name)=[""']{property}[""'][^>]+content=[""']([^""']+)[""']";
        Match match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;
        
        string reversePattern = $@"<meta[^>]+content=[""']([^""']+)[""'][^>]+(?:property|name)=[""']{property}[""']";
        Match reverseMatch = Regex.Match(html, reversePattern, RegexOptions.IgnoreCase);
        return reverseMatch.Success ? reverseMatch.Groups[1].Value : null;
    }

    private string ExtractJsonValue(string json, string key)
    {
        Match match = Regex.Match(json, $@"""{key}""\s*:\s*""([^""]+)""");
        if (match.Success) return Regex.Unescape(match.Groups[1].Value);
        return null;
    }
}