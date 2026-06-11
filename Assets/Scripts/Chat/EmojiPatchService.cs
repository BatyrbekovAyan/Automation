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
    // Atlas structural reference (verified by inspecting texture-0.png):
    //   - Each atlas tile is 180×180 pixels
    //   - Emoji content occupies the central ~144px (≈80% of tile)
    //   - Transparent margin per side: ~18px (≈10% of tile)
    //
    // To structurally mirror atlas: we want 72px Twemoji source to be ~80% of the
    // padded texture, i.e. pad such that 72 / (72 + 2·pad) = 0.80 → pad = 9px →
    // fraction = 9/72 = 0.125. This gives our sprites the same emoji-to-margin
    // ratio that atlas tiles have, so inter-sprite gaps match structurally.
    //
    // Tune up to widen gaps; tune down to tighten.
    private const float EmojiPaddingFraction = 0.125f;

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
        // atlas-rendered sprites. bearingY lowered to 136 (vs atlas's 148) because
        // Twemoji centers emoji content vertically in the texture while Apple's
        // atlas renders emojis aligned lower within each tile — using the same
        // bearingY makes ours appear ~10 units higher than atlas neighbors. The
        // -12 unit offset compensates for that texture-content positioning gap.
        // bearingX=0 and advance=width — visual inter-sprite padding comes from the
        // texture margin (EmojiPaddingFraction), not the metric.
        const float EmojiMetricSize     = 164f;
        const float EmojiMetricBearingY = 140f;
        var glyph = new TMP_SpriteGlyph
        {
            index     = 0,
            metrics   = new GlyphMetrics(EmojiMetricSize, EmojiMetricSize, 0f, EmojiMetricBearingY, EmojiMetricSize),
            glyphRect = new GlyphRect(4, 0, paddedTex.width, paddedTex.height),
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
        // Twemoji's filename may differ from the codepoint sequence the sender used
        // (FE0F placement varies per emoji family) — walk the candidate names until
        // one resolves. The sprite is always registered under the *requested* name
        // so already-emitted tags and the disk cache stay consistent.
        List<string> candidates = BuildCandidateNames(spriteName);
        byte[] bytes = null;

        foreach (string candidate in candidates)
        {
            string url = $"{CdnBase}{candidate}.png";

            using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    bytes = request.downloadHandler.data;
                    break;
                }

                Debug.LogWarning($"[EmojiPatchService] Fetch failed for {spriteName} via {candidate} ({request.responseCode}): {request.error}");

                // Only a 404 means "wrong filename guess" — try the next variant.
                // Network/server errors would fail for every candidate; stop now.
                if (request.responseCode != 404) break;
            }
        }

        _activeDownloads--; // decrement once regardless of outcome

        if (bytes == null)
        {
            EmojiSpriteRegistry.MarkFailed(spriteName);
            yield break;
        }

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

    // -------------------------------------------------------------------------
    // CDN filename candidates
    // -------------------------------------------------------------------------

    /// <summary>
    /// Twemoji CDN filenames are inconsistent about U+FE0F (variation selector-16):
    /// single-codepoint emoji drop it (263a.png, not 263a-fe0f.png), most ZWJ
    /// sequences require it at RGI positions (2764-fe0f-200d-1f525.png,
    /// 1f636-200d-1f32b-fe0f.png, 1f3f3-fe0f-200d-26a7-fe0f.png) — yet some use
    /// the minimal form (1f441-200d-1f5e8.png). Senders are equally inconsistent
    /// about including FE0F, so instead of one rename rule we try a short ordered
    /// list of plausible filenames. The first entry is always the name as requested.
    /// </summary>
    internal static List<string> BuildCandidateNames(string spriteName)
    {
        var candidates = new List<string> { spriteName };

        void Add(string name)
        {
            if (!string.IsNullOrEmpty(name) && !candidates.Contains(name)) candidates.Add(name);
        }

        var parts = new List<string>(spriteName.Split('-'));
        parts.RemoveAll(p => p == "fe0f");
        string stripped = string.Join("-", parts);
        Add(stripped);

        if (!stripped.Contains("200d")) return candidates;

        // ZWJ sequence: re-insert fe0f at the positions RGI sequences use.
        // Segments are the chunks between ZWJs; a segment may carry a skin tone
        // ("1f9d1-1f3fb") and fe0f never follows a skin-tone-modified codepoint.
        string[] segments = stripped.Split(new[] { "-200d-" }, StringSplitOptions.None);

        Add(JoinSegmentsWithFe0f(segments, (seg, idx) => IsBmpCodepoint(seg)));       // 2764-fe0f-200d-1f525
        if (IsBareCodepoint(segments[segments.Length - 1]))                           // fe0f never follows a skin tone
            Add(stripped + "-fe0f");                                                  // 1f636-200d-1f32b-fe0f
        Add(JoinSegmentsWithFe0f(segments, (seg, idx) => idx == 0 && IsBareCodepoint(seg))); // 1f3f3-fe0f-200d-1f308
        Add(JoinSegmentsWithFe0f(segments, (seg, idx) => IsBareCodepoint(seg)));      // 1f3f3-fe0f-200d-26a7-fe0f

        return candidates;
    }

    private static string JoinSegmentsWithFe0f(string[] segments, Func<string, int, bool> wantsFe0f)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < segments.Length; i++)
        {
            if (i > 0) sb.Append("-200d-");
            sb.Append(segments[i]);
            if (wantsFe0f(segments[i], i)) sb.Append("-fe0f");
        }
        return sb.ToString();
    }

    /// <summary>A single codepoint with no skin-tone modifier attached.</summary>
    private static bool IsBareCodepoint(string segment) => !segment.Contains('-');

    /// <summary>
    /// A bare BMP codepoint (4 hex digits, e.g. "2764", "26d3") — the classic
    /// text-presentation symbols that RGI ZWJ sequences qualify with FE0F.
    /// </summary>
    private static bool IsBmpCodepoint(string segment) => IsBareCodepoint(segment) && segment.Length == 4;
}
