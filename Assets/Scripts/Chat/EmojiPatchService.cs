using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

/// <summary>
/// Singleton MonoBehaviour that owns the missing-emoji download pipeline.
/// Initialises EmojiSpriteRegistry at startup, loads previously-cached PNGs
/// from disk, and exposes RequestEmoji() for on-demand CDN fetches.
/// Must run before ChatManager — ensured by DefaultExecutionOrder.
/// </summary>
[DefaultExecutionOrder(-10)]
public class EmojiPatchService : MonoBehaviour
{
    public static EmojiPatchService Instance { get; private set; }

    /// <summary>Fired on the main thread when a new sprite asset has been registered.</summary>
    public static event Action<string> OnEmojiReady;

    private const string CdnBase = "https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/72x72/";
    private static readonly string[] SkinTones = { "1f3fb", "1f3fc", "1f3fd", "1f3fe", "1f3ff" };

    private string CacheDir => Path.Combine(Application.persistentDataPath, "emoji_patch");

    private readonly Queue<string>   _fetchQueue    = new Queue<string>();
    private readonly HashSet<string> _queuedNames   = new HashSet<string>();
    private int  _activeDownloads;
    private bool _isProcessingQueue;
    private const int MaxConcurrentDownloads = 3;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        BuildRegistry();
        LoadDiskCache();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // -------------------------------------------------------------------------
    // Startup
    // -------------------------------------------------------------------------

    private void BuildRegistry()
    {
        var assets = Resources.LoadAll<TMP_SpriteAsset>("Sprite Assets");
        EmojiSpriteRegistry.Build(assets);
        Debug.Log($"[EmojiPatchService] Registry built with {assets.Length} sprite assets.");
    }

    private void LoadDiskCache()
    {
        if (!Directory.Exists(CacheDir)) return;

        var files = Directory.GetFiles(CacheDir, "*.png");
        int loaded = 0;

        foreach (var path in files)
        {
            string spriteName = Path.GetFileNameWithoutExtension(path);
            if (EmojiSpriteRegistry.IsKnown(spriteName)) continue;

            byte[] bytes;
            try   { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EmojiPatchService] Could not read cache file {path}: {ex.Message}");
                continue;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[EmojiPatchService] Corrupt PNG in cache: {spriteName}");
                Destroy(tex);
                continue;
            }

            var asset = BuildSpriteAsset(spriteName, tex);
            RegisterFallback(asset);
            EmojiSpriteRegistry.Register(spriteName);
            loaded++;
        }

        if (loaded > 0)
            Debug.Log($"[EmojiPatchService] Loaded {loaded} cached emoji sprites from disk.");
    }

    // -------------------------------------------------------------------------
    // TMP asset creation helpers (also used by download pipeline)
    // -------------------------------------------------------------------------

    private TMP_SpriteAsset BuildSpriteAsset(string spriteName, Texture2D tex)
    {
        var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = spriteName;
        asset.spriteSheet = tex;

        var defaultAsset = TMP_Settings.defaultSpriteAsset;
        if (defaultAsset != null)
            asset.faceInfo = defaultAsset.faceInfo;

        var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { mainTexture = tex };
        asset.material = mat;

        float h = tex.height;
        float w = tex.width;
        var glyph = new TMP_SpriteGlyph
        {
            index     = 0,
            metrics   = new GlyphMetrics(w, h, 0f, h * 0.78f, w),
            glyphRect = new GlyphRect(0, 0, tex.width, tex.height),
            scale     = 1f,
            atlasIndex = 0
        };

        var character = new TMP_SpriteCharacter(0xFFFE, asset, glyph)
        {
            name  = spriteName,
            scale = 1f
        };

        asset.spriteGlyphTable     = new List<TMP_SpriteGlyph>    { glyph };
        asset.spriteCharacterTable = new List<TMP_SpriteCharacter> { character };
        asset.UpdateLookupTables();

        return asset;
    }

    private void RegisterFallback(TMP_SpriteAsset asset)
    {
        var defaultAsset = TMP_Settings.defaultSpriteAsset;
        if (defaultAsset == null)
        {
            Debug.LogWarning("[EmojiPatchService] TMP_Settings.defaultSpriteAsset is null — cannot register fallback.");
            return;
        }

        defaultAsset.fallbackSpriteAssets ??= new List<TMP_SpriteAsset>();

        if (!defaultAsset.fallbackSpriteAssets.Contains(asset))
            defaultAsset.fallbackSpriteAssets.Add(asset);
    }

    // -------------------------------------------------------------------------
    // Download pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queue a CDN fetch for the given sprite name (lowercase hex codepoint).
    /// Safe to call repeatedly — deduplicates internally.
    /// When a base codepoint is queued, all 5 skin-tone variants are also queued.
    /// </summary>
    public void RequestEmoji(string spriteName)
    {
        if (EmojiSpriteRegistry.IsKnown(spriteName) || EmojiSpriteRegistry.IsPending(spriteName))
            return;
        if (_queuedNames.Contains(spriteName))
            return;

        EmojiSpriteRegistry.ClearFailed(spriteName);
        EmojiSpriteRegistry.MarkPending(spriteName);
        _fetchQueue.Enqueue(spriteName);
        _queuedNames.Add(spriteName);

        if (!spriteName.Contains('-'))
        {
            foreach (var tone in SkinTones)
                RequestEmoji($"{spriteName}-{tone}");
        }

        if (!_isProcessingQueue)
            StartCoroutine(DrainQueueRoutine());
    }

    private IEnumerator DrainQueueRoutine()
    {
        _isProcessingQueue = true;

        while (_fetchQueue.Count > 0)
        {
            while (_activeDownloads >= MaxConcurrentDownloads)
                yield return null;

            var spriteName = _fetchQueue.Dequeue();
            _queuedNames.Remove(spriteName);
            _activeDownloads++;
            StartCoroutine(FetchEmojiRoutine(spriteName));
        }

        while (_activeDownloads > 0)
            yield return null;

        _isProcessingQueue = false;
    }

    private IEnumerator FetchEmojiRoutine(string spriteName)
    {
        string url = $"{CdnBase}{spriteName}.png";

        using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[EmojiPatchService] Fetch failed for {spriteName} ({request.responseCode}): {request.error}");
                EmojiSpriteRegistry.MarkFailed(spriteName);
                _activeDownloads--;
                yield break;
            }

            byte[] bytes = request.downloadHandler.data;

            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);
                File.WriteAllBytes(Path.Combine(CacheDir, $"{spriteName}.png"), bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EmojiPatchService] Disk write failed for {spriteName}: {ex.Message}");
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[EmojiPatchService] Corrupt PNG from CDN for {spriteName}");
                Destroy(tex);
                EmojiSpriteRegistry.MarkFailed(spriteName);
                _activeDownloads--;
                yield break;
            }

            var asset = BuildSpriteAsset(spriteName, tex);
            RegisterFallback(asset);
            EmojiSpriteRegistry.Register(spriteName);

            Debug.Log($"[EmojiPatchService] Registered new emoji sprite: {spriteName}");
            OnEmojiReady?.Invoke(spriteName);
        }

        _activeDownloads--;
    }
}
