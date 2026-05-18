using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

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

    private readonly Queue<string>   _fetchQueue    = new Queue<string>();
    private readonly HashSet<string> _queuedNames   = new HashSet<string>();
    private int  _activeDownloads;
    private bool _isProcessingQueue;
    private const int MaxConcurrentDownloads = 3;

    // Fraction of the source texture size to add as transparent padding on each side.
    //
    // Calibrated empirically: atlas PNGs have substantial internal padding (~20% of
    // their 180px tile), producing a visible inter-sprite gap of ~30% of the emoji
    // width. Earlier math assumed only 5.6% atlas padding (the metric/glyphRect
    // ratio), which underestimated by ~3x.
    //
    // 0.15 means we add ~11px of transparent border to each side of the 72px source,
    // producing a 94px padded texture that, when rendered at metric 140, gives an
    // inter-sprite em-gap of ~37 units — matching what atlas sprites produce.
    //
    // Tune up to widen gaps further, down to tighten.
    private const float EmojiPaddingFraction = 0.15f;

    private string _cacheDir;
    private Shader _tmpSpriteShader;

    // -------------------------------------------------------------------------
    // Bootstrap — auto-creates the singleton before the scene loads if it is
    // not already placed as a component in the scene hierarchy.
    // -------------------------------------------------------------------------

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[EmojiPatchService]");
        go.AddComponent<EmojiPatchService>();
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _cacheDir = Path.Combine(Application.persistentDataPath, "emoji_patch");
        _tmpSpriteShader = Shader.Find("TextMeshPro/Sprite");
        if (_tmpSpriteShader == null)
            Debug.LogError("[EmojiPatchService] TextMeshPro/Sprite shader not found — emoji sprites will not render. Ensure it is in Always Included Shaders.");

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
        if (assets.Length == 0)
            Debug.LogWarning("[EmojiPatchService] No sprite assets found under Resources/Sprite Assets — emoji registry will be empty.");
        Debug.Log($"[EmojiPatchService] Registry built with {assets.Length} sprite assets.");
    }

    private void LoadDiskCache()
    {
        if (!Directory.Exists(_cacheDir)) return;

        var files = Directory.GetFiles(_cacheDir, "*.png");
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
        // The existing atlas PNGs are 180px tiles with the emoji content occupying
        // the central ~160px (10px transparent margin on each side). Twemoji's 72px
        // PNGs are edge-to-edge with no margin, so consecutive sprites visually
        // touch. We bake the equivalent margin into a padded copy of the texture so
        // our metrics can mirror the atlas exactly (width=advance=160, bearingX=0)
        // and inter-sprite spacing matches what the atlas does naturally.
        var paddedTex = CreatePaddedTexture(tex, EmojiPaddingFraction);

        var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = spriteName;
        asset.spriteSheet = paddedTex;

        // Mirror the existing atlas's FaceInfo (texture-0.asset) exactly. TMP_Text
        // renders sprites with `fontSize / spriteFace.pointSize * spriteFace.scale`
        // when pointSize > 0; otherwise it falls back to a path that scales by
        // fontFace.ascentLine / glyph.metrics.height, producing a 2-3x oversized
        // sprite. Setting pointSize/scale/baseline explicitly forces the correct path.
        var defaultAsset = TMP_Settings.defaultSpriteAsset;
        var face = defaultAsset != null ? defaultAsset.faceInfo : default;
        face.pointSize = 100;
        face.scale     = 0.86f;
        face.baseline  = -38f;
        asset.faceInfo = face;

        // Atlas-identical metric size (160) for visual size parity with existing
        // atlas-rendered sprites. bearingY = 147.2 ≈ 92% of 160 (atlas uses 148).
        // bearingX=0 and advance=width — visual inter-sprite padding comes from the
        // texture margin (EmojiPaddingFraction), not the metric.
        const float EmojiMetricSize     = 160f;
        const float EmojiMetricBearingY = 147.2f;
        var glyph = new TMP_SpriteGlyph
        {
            index     = 0,
            metrics   = new GlyphMetrics(EmojiMetricSize, EmojiMetricSize, 0f, EmojiMetricBearingY, EmojiMetricSize),
            glyphRect = new GlyphRect(0, 0, paddedTex.width, paddedTex.height),
            scale     = 1f,
            atlasIndex = 0
        };

        var character = new TMP_SpriteCharacter(0xFFFE, asset, glyph)
        {
            name  = spriteName,
            scale = 1f
        };

        asset.spriteGlyphTable.Add(glyph);
        asset.spriteCharacterTable.Add(character);

        // UpdateLookupTables BEFORE assigning material: TMP checks
        // `material != null && version == ""` and if true calls UpgradeSpriteAsset(),
        // which clears our tables and crashes on the null legacy spriteInfoList.
        // Building the lookup tables first (while material is still null) bypasses that path.
        asset.UpdateLookupTables();

        var mat = new Material(_tmpSpriteShader != null ? _tmpSpriteShader : Shader.Find("TextMeshPro/Sprite")) { mainTexture = paddedTex };
        asset.material = mat;

        return asset;
    }

    /// <summary>
    /// Returns a new Texture2D with transparent padding around the source.
    /// Used to give Twemoji's edge-to-edge PNGs the same kind of internal margin
    /// the atlas tiles have, so consecutive sprites render with natural visual gaps.
    /// </summary>
    private static Texture2D CreatePaddedTexture(Texture2D src, float paddingFraction)
    {
        int pad     = Mathf.Max(1, Mathf.RoundToInt(src.width * paddingFraction));
        int newSize = src.width + pad * 2;

        var padded = new Texture2D(newSize, newSize, TextureFormat.RGBA32, false);
        padded.name = src.name;

        // Fill transparent
        var transparent = new Color32[newSize * newSize];
        padded.SetPixels32(transparent);

        // Copy source pixels into the center
        var srcPixels = src.GetPixels32();
        padded.SetPixels32(pad, pad, src.width, src.height, srcPixels);
        padded.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        // Source texture is no longer needed
        Destroy(src);

        return padded;
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

        while (_fetchQueue.Count > 0 || _activeDownloads > 0)
        {
            while (_activeDownloads >= MaxConcurrentDownloads)
                yield return null;

            if (_fetchQueue.Count > 0)
            {
                var spriteName = _fetchQueue.Dequeue();
                _queuedNames.Remove(spriteName);
                _activeDownloads++;
                StartCoroutine(FetchEmojiRoutine(spriteName));
            }
            else
            {
                yield return null; // downloads still in-flight, nothing to dequeue yet
            }
        }

        _isProcessingQueue = false;
    }

    private IEnumerator FetchEmojiRoutine(string spriteName)
    {
        string url = $"{CdnBase}{spriteName}.png";

        using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            _activeDownloads--; // decrement here once regardless of outcome

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[EmojiPatchService] Fetch failed for {spriteName} ({request.responseCode}): {request.error}");
                EmojiSpriteRegistry.MarkFailed(spriteName);
                yield break;
            }

            byte[] bytes = request.downloadHandler.data;

            try
            {
                if (!Directory.Exists(_cacheDir))
                    Directory.CreateDirectory(_cacheDir);
                File.WriteAllBytes(Path.Combine(_cacheDir, $"{spriteName}.png"), bytes);
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
                yield break;
            }

            var asset = BuildSpriteAsset(spriteName, tex);
            RegisterFallback(asset);
            EmojiSpriteRegistry.Register(spriteName);

            Debug.Log($"[EmojiPatchService] Registered new emoji sprite: {spriteName}");
            OnEmojiReady?.Invoke(spriteName);
        }
        // No decrement here — already done inside using block
    }
}
