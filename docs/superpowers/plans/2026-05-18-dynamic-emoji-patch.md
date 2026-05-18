# Dynamic Emoji Patch System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically download missing Emoji 17+ sprites from jdecked/twemoji CDN at runtime, cache them to disk, register them as TMP fallback sprite assets, and re-render affected message views — so new emoji never show as broken literal `<sprite name="…">` tags.

**Architecture:** `EmojiSpriteRegistry` (static, built at startup from all 31 sprite atlases) gates `UnicodeEmojiConverter` — known sprites get `<sprite>` tags as today; unknowns stay as raw Unicode (font fallback) and queue a CDN fetch. `EmojiPatchService` (MonoBehaviour singleton) owns the download coroutine, disk cache, and runtime TMP_SpriteAsset creation; it fires `OnEmojiReady` when a sprite lands so `MessageItemView` can re-render.

**Tech Stack:** Unity 6 / C#, TextMeshPro (`TMP_SpriteAsset`, `TMP_SpriteCharacter`, `TMP_SpriteGlyph`), `UnityWebRequest`, `System.IO.File`, jdecked/twemoji jsdelivr CDN, NUnit (existing test setup in `Assets/Tests/Editor/Chat/`)

**Spec:** `docs/superpowers/specs/2026-05-18-dynamic-emoji-patch-design.md`

---

## File Map

| File | Action |
|---|---|
| `Assets/Scripts/Chat/EmojiSpriteRegistry.cs` | **Create** — static registry of known/pending/failed sprite names |
| `Assets/Scripts/Chat/EmojiPatchService.cs` | **Create** — MonoBehaviour singleton: disk cache load, CDN fetch, TMP asset creation |
| `Assets/Scripts/Chat/UnicodeEmojiConverter.cs` | **Modify** — check registry before emitting sprite tags; add `out bool hasMissingEmojis` |
| `Assets/Scripts/UI/MessageItemView.cs` | **Modify** — track original text; subscribe `OnEmojiReady` on main message body paths |
| `Assets/Scripts/UI/ChatItemView.cs` | **Modify** — update 1 call site to `out _` |
| `Assets/Scripts/Main/ChatManager.cs` | **Modify** — update 4 call sites to `out _` |
| `Assets/Tests/Editor/Chat/EmojiSpriteRegistryTests.cs` | **Create** — unit tests for registry state machine |
| `Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs` | **Create** — unit tests for converter registry integration |

---

## Task 1: EmojiSpriteRegistry

**Files:**
- Create: `Assets/Scripts/Chat/EmojiSpriteRegistry.cs`
- Create: `Assets/Tests/Editor/Chat/EmojiSpriteRegistryTests.cs`

- [ ] **Step 1: Create the test file**

`Assets/Tests/Editor/Chat/EmojiSpriteRegistryTests.cs`:
```csharp
using NUnit.Framework;

public class EmojiSpriteRegistryTests
{
    [SetUp]
    public void SetUp() => EmojiSpriteRegistry.Reset();

    // --- Build ---

    [Test]
    public void Build_PopulatesKnownSet()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600", "1f44b" });
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1f600"));
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1f44b"));
    }

    [Test]
    public void Build_ClearsPreviousState()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });
        EmojiSpriteRegistry.MarkPending("1f600");
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f601" });
        Assert.IsFalse(EmojiSpriteRegistry.IsKnown("1f600"));
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1f600"));
    }

    [Test]
    public void UnknownName_IsNotKnown()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });
        Assert.IsFalse(EmojiSpriteRegistry.IsKnown("1faea"));
    }

    // --- MarkPending / Register ---

    [Test]
    public void MarkPending_ThenRegister_MovesToKnown()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        Assert.IsTrue(EmojiSpriteRegistry.IsPending("1faea"));
        Assert.IsFalse(EmojiSpriteRegistry.IsKnown("1faea"));

        EmojiSpriteRegistry.Register("1faea");
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1faea"));
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1faea"));
    }

    // --- MarkFailed / ClearFailed retry path ---

    [Test]
    public void MarkFailed_ClearsPending()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        EmojiSpriteRegistry.MarkFailed("1faea");
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1faea"));
        Assert.IsTrue(EmojiSpriteRegistry.IsFailed("1faea"));
    }

    [Test]
    public void ClearFailed_AllowsRetry()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        EmojiSpriteRegistry.MarkFailed("1faea");
        EmojiSpriteRegistry.ClearFailed("1faea");
        Assert.IsFalse(EmojiSpriteRegistry.IsFailed("1faea"));
        Assert.IsFalse(EmojiSpriteRegistry.IsPending("1faea"));
    }

    [Test]
    public void Register_ClearsFailedFlag()
    {
        EmojiSpriteRegistry.MarkPending("1faea");
        EmojiSpriteRegistry.MarkFailed("1faea");
        EmojiSpriteRegistry.Register("1faea");
        Assert.IsFalse(EmojiSpriteRegistry.IsFailed("1faea"));
        Assert.IsTrue(EmojiSpriteRegistry.IsKnown("1faea"));
    }
}
```

- [ ] **Step 2: Open Unity Test Runner and confirm tests are visible but failing**

Window → General → Test Runner → EditMode → expand `EmojiSpriteRegistryTests`. All tests should appear with red X (class not found). If they don't appear, ensure the file is in `Assets/Tests/Editor/Chat/` and Unity has recompiled.

- [ ] **Step 3: Create EmojiSpriteRegistry**

`Assets/Scripts/Chat/EmojiSpriteRegistry.cs`:
```csharp
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Tracks which TMP sprite names are known (in atlas), pending (fetch in flight),
/// or failed (last fetch attempt failed). Built once at startup by EmojiPatchService.
/// </summary>
public static class EmojiSpriteRegistry
{
    private static readonly HashSet<string> _known  = new HashSet<string>();
    private static readonly HashSet<string> _pending = new HashSet<string>();
    private static readonly HashSet<string> _failed  = new HashSet<string>();

    /// <summary>Build the known set from all loaded TMP sprite assets.</summary>
    public static void Build(IEnumerable<TMP_SpriteAsset> assets)
    {
        var names = new List<string>();
        foreach (var asset in assets)
        {
            if (asset?.spriteCharacterTable == null) continue;
            foreach (var ch in asset.spriteCharacterTable)
                if (!string.IsNullOrEmpty(ch.name))
                    names.Add(ch.name);
        }
        BuildFromNames(names);
    }

    /// <summary>Build the known set directly from names. Used by unit tests.</summary>
    internal static void BuildFromNames(IEnumerable<string> names)
    {
        _known.Clear();
        _pending.Clear();
        _failed.Clear();
        foreach (var n in names)
            _known.Add(n);
    }

    public static bool IsKnown(string name)   => _known.Contains(name);
    public static bool IsPending(string name) => _pending.Contains(name);
    public static bool IsFailed(string name)  => _failed.Contains(name);

    /// <summary>Mark a name as fetch-in-flight. Clears any prior failed state.</summary>
    public static void MarkPending(string name)
    {
        _pending.Add(name);
        _failed.Remove(name);
    }

    /// <summary>Move a name from pending → known once the sprite is registered.</summary>
    public static void Register(string name)
    {
        _known.Add(name);
        _pending.Remove(name);
        _failed.Remove(name);
    }

    /// <summary>Record a fetch failure. Clears pending so a retry is possible next encounter.</summary>
    public static void MarkFailed(string name)
    {
        _pending.Remove(name);
        _failed.Add(name);
    }

    /// <summary>Clear a failed state so RequestEmoji will re-queue this name.</summary>
    public static void ClearFailed(string name) => _failed.Remove(name);

    /// <summary>Reset all state. Used by unit tests.</summary>
    internal static void Reset()
    {
        _known.Clear();
        _pending.Clear();
        _failed.Clear();
    }
}
```

- [ ] **Step 4: Run tests — all should pass**

Window → General → Test Runner → EditMode → Run All (or right-click `EmojiSpriteRegistryTests` → Run). Expected: 8 green tests.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/EmojiSpriteRegistry.cs \
        Assets/Scripts/Chat/EmojiSpriteRegistry.cs.meta \
        Assets/Tests/Editor/Chat/EmojiSpriteRegistryTests.cs \
        Assets/Tests/Editor/Chat/EmojiSpriteRegistryTests.cs.meta
git commit -m "feat: add EmojiSpriteRegistry with unit tests"
```

---

## Task 2: Modify UnicodeEmojiConverter + tests

**Files:**
- Modify: `Assets/Scripts/Chat/UnicodeEmojiConverter.cs`
- Create: `Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs`

- [ ] **Step 1: Write failing tests**

`Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs`:
```csharp
using NUnit.Framework;

/// <summary>
/// Tests for the registry-aware behaviour added to UnicodeEmojiConverter.
/// These complement (do not replace) any existing converter tests.
/// </summary>
public class UnicodeEmojiConverterPatchTests
{
    [SetUp]
    public void SetUp() => EmojiSpriteRegistry.Reset();

    [Test]
    public void Convert_KnownEmoji_EmitsSpriteTag_NoMissingFlag()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀", out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1f600\">", result);
        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void Convert_UnknownEmoji_LeavesRawUnicode_SetsMissingFlag()
    {
        // Registry empty — 🫪 (1faea) is unknown
        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("🫪", out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        StringAssert.Contains("🫪", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void Convert_MixedString_SpriteTagForKnown_RawUnicodeForUnknown()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600" });

        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀🫪", out bool hasMissing);

        StringAssert.Contains("<sprite name=\"1f600\">", result);
        StringAssert.Contains("🫪", result);
        Assert.IsTrue(hasMissing);
    }

    [Test]
    public void Convert_AllKnown_MissingFlagFalse()
    {
        EmojiSpriteRegistry.BuildFromNames(new[] { "1f600", "1f44b" });

        UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀👋", out bool hasMissing);

        Assert.IsFalse(hasMissing);
    }

    [Test]
    public void Convert_EmptyRegistry_AllEmojiLeaveRawUnicode()
    {
        var result = UnicodeEmojiConverter.ConvertRealEmojisToSprites("😀👋", out bool hasMissing);

        StringAssert.DoesNotContain("<sprite", result);
        Assert.IsTrue(hasMissing);
    }
}
```

- [ ] **Step 2: Confirm tests fail**

Test Runner → Run `UnicodeEmojiConverterPatchTests`. Expected: compile error or failure (method signature doesn't match yet).

- [ ] **Step 3: Modify UnicodeEmojiConverter**

Open `Assets/Scripts/Chat/UnicodeEmojiConverter.cs`. Make these changes:

**a) Change the method signature** (line 7):
```csharp
// BEFORE
public static string ConvertRealEmojisToSprites(string input)

// AFTER
public static string ConvertRealEmojisToSprites(string input, out bool hasMissingEmojis)
```

**b) Initialise the out parameter** immediately after the null-check (after line 9):
```csharp
public static string ConvertRealEmojisToSprites(string input, out bool hasMissingEmojis)
{
    hasMissingEmojis = false;
    if (string.IsNullOrEmpty(input)) return input;
```

**c) Replace the sprite-tag emission block** (the block starting at line 72 that builds `hexName` and appends the sprite tag). Replace the entire block from `string hexName = GetHexName(emojiSequence);` through to `i = currentIdx;` with:

```csharp
                string hexName = GetHexName(emojiSequence);

                if (EmojiSpriteRegistry.IsKnown(hexName))
                {
                    // Sprite exists — emit TMP rich-text tag with spacing
                    bool needsGap = sb.Length > 0
                        && !char.IsWhiteSpace(sb[sb.Length - 1])
                        && sb[sb.Length - 1] != '>'
                        && sb[sb.Length - 1] != '​';

                    sb.Append('​');
                    if (needsGap) sb.Append("<space=0.12em>");
                    sb.Append($"<sprite name=\"{hexName}\">");
                    sb.Append('​');
                }
                else
                {
                    // Sprite missing — leave raw Unicode so font fallback renders it,
                    // and queue a background CDN fetch.
                    hasMissingEmojis = true;
                    sb.Append(input, i, currentIdx - i);
                    if (EmojiPatchService.Instance != null)
                        EmojiPatchService.Instance.RequestEmoji(hexName);
                }

                i = currentIdx;
```

- [ ] **Step 4: Run tests — all 5 should pass**

Test Runner → Run `UnicodeEmojiConverterPatchTests`. Expected: 5 green.

Note: existing call sites of `ConvertRealEmojisToSprites` will now show compile errors (missing `out` argument). That is expected and will be fixed in Tasks 5 and 6.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/UnicodeEmojiConverter.cs \
        Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs \
        Assets/Tests/Editor/Chat/UnicodeEmojiConverterPatchTests.cs.meta
git commit -m "feat: make UnicodeEmojiConverter registry-aware"
```

---

## Task 3: EmojiPatchService — startup + disk cache

**Files:**
- Create: `Assets/Scripts/Chat/EmojiPatchService.cs`

- [ ] **Step 1: Create EmojiPatchService with Awake + disk cache loading**

`Assets/Scripts/Chat/EmojiPatchService.cs`:
```csharp
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

    private readonly Queue<string>  _fetchQueue      = new Queue<string>();
    private readonly HashSet<string> _queuedNames    = new HashSet<string>();
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
    // TMP asset creation helpers (also used by download pipeline in Task 4)
    // -------------------------------------------------------------------------

    private TMP_SpriteAsset BuildSpriteAsset(string spriteName, Texture2D tex)
    {
        var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = spriteName;
        asset.spriteSheet = tex;

        // Copy face info from the default sprite asset so vertical alignment matches
        var defaultAsset = TMP_Settings.defaultSpriteAsset;
        if (defaultAsset != null)
            asset.faceInfo = defaultAsset.faceInfo;

        var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { mainTexture = tex };
        asset.material = mat;

        float h = tex.height;
        float w = tex.width;
        var glyph = new TMP_SpriteGlyph
        {
            index    = 0,
            metrics  = new GlyphMetrics(w, h, 0f, h * 0.78f, w),
            glyphRect = new GlyphRect(0, 0, tex.width, tex.height),
            scale    = 1f,
            atlasIndex = 0
        };

        var character = new TMP_SpriteCharacter(0xFFFE, asset, glyph)
        {
            name  = spriteName,
            scale = 1f
        };

        asset.spriteGlyphTable     = new List<TMP_SpriteGlyph>     { glyph };
        asset.spriteCharacterTable = new List<TMP_SpriteCharacter>  { character };
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
    // Download pipeline — stub for Task 4
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queue a CDN fetch for the given sprite name (lowercase hex codepoint).
    /// Safe to call repeatedly — deduplicates internally.
    /// When a base codepoint is queued, all 5 skin-tone variants are also queued.
    /// </summary>
    public void RequestEmoji(string spriteName)
    {
        // Implemented in Task 4
    }
}
```

- [ ] **Step 2: Verify the project compiles**

Unity console should show 0 compile errors after saving. The `RequestEmoji` stub means no fetches fire yet — that's intentional.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/EmojiPatchService.cs \
        Assets/Scripts/Chat/EmojiPatchService.cs.meta
git commit -m "feat: add EmojiPatchService with startup registry build and disk cache load"
```

---

## Task 4: EmojiPatchService — download pipeline

**Files:**
- Modify: `Assets/Scripts/Chat/EmojiPatchService.cs`

- [ ] **Step 1: Add DrainQueueRoutine and FetchEmojiRoutine** (must exist before RequestEmoji calls them)

Add these two methods inside `EmojiPatchService`, after the `RequestEmoji` stub (before the closing `}`):



```csharp
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

        // Wait for all in-flight downloads to finish before exiting
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

            // Save to disk (fire-and-forget; if it fails the in-memory sprite still works)
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
```

- [ ] **Step 2: Replace the RequestEmoji stub with the full implementation**

Replace the `RequestEmoji` method body (the `// Implemented in Task 4` stub) with:

```csharp
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

        // Proactively queue all 5 skin-tone variants for base codepoints
        if (!spriteName.Contains('-'))
        {
            foreach (var tone in SkinTones)
                RequestEmoji($"{spriteName}-{tone}");
        }

        if (!_isProcessingQueue)
            StartCoroutine(DrainQueueRoutine());
    }
```

- [ ] **Step 3: Verify compile — 0 errors in Unity console**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Chat/EmojiPatchService.cs
git commit -m "feat: add EmojiPatchService CDN download pipeline with skin-tone prefetch"
```

---

## Task 5: Update `out _` callers

Fix all call sites except MessageItemView's main message body (those get full treatment in Task 6).

**Files:**
- Modify: `Assets/Scripts/UI/ChatItemView.cs` (line 197)
- Modify: `Assets/Scripts/Main/ChatManager.cs` (lines 118, 119, 775, 788)
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (lines 165, 849, 1632, 2756)

- [ ] **Step 1: Fix ChatItemView.cs line 197**

```csharp
// BEFORE
string composed = UnicodeEmojiConverter.ConvertRealEmojisToSprites(formatted);

// AFTER
string composed = UnicodeEmojiConverter.ConvertRealEmojisToSprites(formatted, out _);
```

- [ ] **Step 2: Fix ChatManager.cs lines 118–119**

```csharp
// BEFORE
string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
string lastMsg = string.IsNullOrEmpty(chat.last_message_data) ? "" : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.last_message_data);

// AFTER
string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name, out _);
string lastMsg = string.IsNullOrEmpty(chat.last_message_data) ? "" : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.last_message_data, out _);
```

- [ ] **Step 3: Fix ChatManager.cs lines 775 and 788**

```csharp
// BEFORE (line 775)
msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw.body?.ToString());

// AFTER
msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw.body?.ToString(), out _);

// BEFORE (line 788)
msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(captionStr);

// AFTER
msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(captionStr, out _);
```

- [ ] **Step 4: Fix MessageItemView.cs peripheral call sites**

Line 165:
```csharp
// BEFORE
senderNameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(vm.senderName);

// AFTER
senderNameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(vm.senderName, out _);
```

Line 849:
```csharp
// BEFORE
string decodedName = UnicodeEmojiConverter.ConvertRealEmojisToSprites(System.Uri.UnescapeDataString(rawName));

// AFTER
string decodedName = UnicodeEmojiConverter.ConvertRealEmojisToSprites(System.Uri.UnescapeDataString(rawName), out _);
```

Line 1632:
```csharp
// BEFORE
if (downloadButtonText) downloadButtonText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(decodedName);

// AFTER
if (downloadButtonText) downloadButtonText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(decodedName, out _);
```

Line 2756:
```csharp
// BEFORE
if (documentNameText) documentNameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(decodedName);

// AFTER
if (documentNameText) documentNameText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(decodedName, out _);
```

- [ ] **Step 5: Verify 0 compile errors in Unity console**

The only remaining compile errors should be on the main body paths in MessageItemView (lines 237, 2555, 2568) — these are intentionally left for Task 6.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/ChatItemView.cs \
        Assets/Scripts/Main/ChatManager.cs \
        Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat: update peripheral ConvertRealEmojisToSprites callers to out _"
```

---

## Task 6: MessageItemView — main body re-render

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs`

- [ ] **Step 1: Add the `_mainMessageOriginalText` field**

Find the private fields section in `MessageItemView.cs` (near the top of the class, with other `private` fields). Add:

```csharp
private string _mainMessageOriginalText;
```

- [ ] **Step 2: Add the SubscribeToEmojiReady and OnEmojiReady helpers**

Add these two private methods inside `MessageItemView`. A good place is near the other event handlers:

```csharp
private void SubscribeToEmojiReady()
{
    // Avoid duplicate subscription
    EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
    EmojiPatchService.OnEmojiReady += HandleEmojiReady;
}

private void HandleEmojiReady(string spriteName)
{
    if (messageText == null || string.IsNullOrEmpty(_mainMessageOriginalText)) return;

    var reconverted = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
        _mainMessageOriginalText, out bool stillMissing);

    if (reconverted != messageText.text)
        messageText.text = reconverted;

    if (!stillMissing)
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
}
```

- [ ] **Step 3: Add unsubscribe to OnDisable**

Find `OnDisable()` in `MessageItemView`. If it doesn't exist, add it. Add the unsubscribe line:

```csharp
private void OnDisable()
{
    EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
    // (preserve any existing OnDisable content above/below this line)
}
```

- [ ] **Step 4: Fix main message body path — line 237**

Replace line 237:
```csharp
// BEFORE
string processedText = UnicodeEmojiConverter.ConvertRealEmojisToSprites(textToProcess) ?? "";

// AFTER
_mainMessageOriginalText = textToProcess;
string processedText = UnicodeEmojiConverter.ConvertRealEmojisToSprites(textToProcess, out bool hasMissingMain);
processedText ??= "";
if (hasMissingMain) SubscribeToEmojiReady();
```

- [ ] **Step 5: Fix link preview with text path — line 2555**

Replace line 2555:
```csharp
// BEFORE
messageText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(textWithoutUrl);

// AFTER
_mainMessageOriginalText = textWithoutUrl;
messageText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(textWithoutUrl, out bool hasMissingUrl);
if (hasMissingUrl) SubscribeToEmojiReady();
```

- [ ] **Step 6: Fix link preview no-image path — line 2568**

Replace lines 2567–2568:
```csharp
// BEFORE
string originalText = FormatTextWithWrappableLinks(vm.text ?? "");
messageText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(originalText);

// AFTER
string originalText = FormatTextWithWrappableLinks(vm.text ?? "");
_mainMessageOriginalText = originalText;
messageText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(originalText, out bool hasMissingLink);
if (hasMissingLink) SubscribeToEmojiReady();
```

- [ ] **Step 7: Verify 0 compile errors in Unity console**

All call sites are now updated. Run the existing test suite to confirm no regressions.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat: MessageItemView re-renders main message text when missing emoji sprite arrives"
```

---

## Task 7: Scene wiring + smoke test

**Files:**
- Manual: open `Assets/Scenes/Main.unity` in Unity Editor

- [ ] **Step 1: Add EmojiPatchService to the Manager GameObject**

In the Unity Editor:
1. Open `Assets/Scenes/Main.unity`
2. In the Hierarchy, find the GameObject that has the `Manager` component (likely named "Manager")
3. With that GameObject selected, in the Inspector click **Add Component**
4. Search for `EmojiPatchService` and add it
5. Save the scene (Ctrl+S / Cmd+S)

- [ ] **Step 2: Verify EmojiPatchService initialises on Play**

Press Play in the Unity Editor. Check the Console for:
```
[EmojiPatchService] Registry built with 31 sprite assets.
```
(On first run with an empty disk cache, no "Loaded N cached emoji sprites" line — that's expected.)

- [ ] **Step 3: Smoke test — broken emoji now renders as font fallback**

With the app running in Play mode (Game view at iPhone 12 resolution), navigate to a chat that shows the `<sprite name="1faea">` broken message.

Expected: the message now shows 🫪 as a system font emoji (raw Unicode rendering) instead of the literal `<sprite name="…">` text.

- [ ] **Step 4: Smoke test — CDN fetch fires and sprite swaps in**

After ~200–500ms (depending on connection), the console should log:
```
[EmojiPatchService] Registered new emoji sprite: 1faea
```
And the message view should visually update — 🫪 swaps from system emoji style to Twemoji style.

If the swap doesn't happen:
- Check that `EmojiPatchService.OnEmojiReady` event fires (add a temporary log in `HandleEmojiReady`)
- Check that `_mainMessageOriginalText` is non-null when `HandleEmojiReady` runs
- Verify `messageText` reference is not null in `HandleEmojiReady`

- [ ] **Step 5: Smoke test — second session uses disk cache (no flash)**

Stop Play. Press Play again. Navigate to the same message. Expected: 🫪 renders as Twemoji immediately on first frame with no font-fallback flash. Console should show:
```
[EmojiPatchService] Loaded 6 cached emoji sprites from disk.
```
(1 base + 5 skin-tone variants.)

- [ ] **Step 6: Commit scene**

```bash
git add Assets/Scenes/Main.unity
git commit -m "feat: wire EmojiPatchService into Manager scene object"
```
