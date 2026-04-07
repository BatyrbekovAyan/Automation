using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public class MediaCacheManager : MonoBehaviour
{
    public static MediaCacheManager Instance;

    private string cacheDirectory;

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