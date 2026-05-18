# Dynamic Emoji Patch System

**Date:** 2026-05-18
**Status:** Approved for implementation

## Problem

The emoji sprite atlas (`texture-0` through `texture-30`, ~3,750 sprites) covers Emoji 1.0–16.0.
Emoji 17.0 additions (e.g. `1faea` 🫪 distorted face) are absent. When `UnicodeEmojiConverter`
emits `<sprite name="1faea">` and TMP finds no match in the fallback chain, it renders the literal
tag as text — visually broken.

The atlas will fall behind with every Unicode release (~yearly). A static patch-per-release
workflow doesn't scale; a dynamic runtime fetch does.

## Chosen Approach

**On-demand CDN fetch with font-fallback-until-ready.**

- Source: `https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/72x72/{name}.png`
  (jdecked/twemoji v17.0.2, Unicode 17.0 compliant, confirmed HTTP 200 for all tested codepoints)
- Naming: lowercase hex, dash-separated for sequences — identical to existing atlas convention
- Unknown emoji: render as raw Unicode (system font fallback) immediately, swap to Twemoji
  sprite once the PNG is fetched and registered
- Disk cache: `persistentDataPath/emoji_patch/{name}.png` — permanent, reloaded at startup
- Skin-tone variants: when any variant of a new base emoji is requested, queue all 5 variants
  (`{base}-1f3fb` through `{base}-1f3ff`) proactively
- Failure: `MarkFailed()` in registry; retry silently on next encounter of that emoji

## Components

### `EmojiSpriteRegistry` — new static class
`Assets/Scripts/Chat/EmojiSpriteRegistry.cs`

Single source of truth for sprite availability. Built once at startup from all loaded
`TMP_SpriteAsset` objects.

**State:**
- `HashSet<string> _known` — sprite names present in atlas or already registered as fallback
- `HashSet<string> _pending` — fetch in flight
- `HashSet<string> _failed` — last fetch attempt failed (cleared by `MarkFailed`, retry on next encounter)

**API:**
```csharp
static void Build(IEnumerable<TMP_SpriteAsset> assets);
static bool IsKnown(string name);
static bool IsPending(string name);
static void MarkPending(string name);
static void Register(string name);   // moves from pending → known
static void MarkFailed(string name); // clears pending, adds to failed (allows retry)
static void ClearFailed(string name);
```

---

### `EmojiPatchService` — new MonoBehaviour singleton
`Assets/Scripts/Chat/EmojiPatchService.cs`

Owns the download pipeline. Must initialise before `ChatManager` starts loading messages
(use `[DefaultExecutionOrder(-10)]`).

**`Awake()`:**
1. `Resources.LoadAll<TMP_SpriteAsset>("Sprite Assets")` → `EmojiSpriteRegistry.Build()`
2. Scan `persistentDataPath/emoji_patch/` → for each cached `.png`:
   `Texture2D.LoadImage(bytes)` → `BuildSpriteAsset()` → `RegisterFallback()` → `EmojiSpriteRegistry.Register()`

**`RequestEmoji(string spriteName)`:**
- Guard: skip if `IsKnown`, `IsPending`, or already in `_fetchQueue`
- `MarkPending(spriteName)`
- Enqueue; start `DrainQueueRoutine()` if not running
- If `spriteName` has no `-` suffix (base codepoint), also enqueue 5 skin-tone variants

**Scene placement:** Add `EmojiPatchService` as a component on the same persistent root GameObject
as `Manager`. No separate prefab needed.

**`DrainQueueRoutine()`:**
- Max 3 concurrent coroutines (`_activeDownloads` counter)
- Per emoji: `UnityWebRequest.Get(url)`, timeout 30s
- On success:
  - `File.WriteAllBytes(cachePath, bytes)` (wrapped in try/catch)
  - `BuildSpriteAsset(name, texture)` → `RegisterFallback()` → `EmojiSpriteRegistry.Register(name)`
  - `OnEmojiReady?.Invoke(name)`
- On failure (network error or 404):
  - `EmojiSpriteRegistry.MarkFailed(name)`

**`BuildSpriteAsset(string name, Texture2D tex)`:**
Creates a `TMP_SpriteAsset` with one glyph/character entry. Face info (scale, baseline) copied
from `TMP_Settings.defaultSpriteAsset` for visual consistency.

**`RegisterFallback(TMP_SpriteAsset asset)`:**
Guard: if `TMP_Settings.defaultSpriteAsset == null`, log warning and return.
Append to `TMP_Settings.defaultSpriteAsset.fallbackSpriteAssets` (same pattern as
`ChatTicksFallbackRegistrar`).

**Event:**
```csharp
public static event Action<string> OnEmojiReady;
```

---

### `UnicodeEmojiConverter` — modified
`Assets/Scripts/Chat/UnicodeEmojiConverter.cs`

**Signature change:**
```csharp
// Before
public static string ConvertRealEmojisToSprites(string input)

// After
public static string ConvertRealEmojisToSprites(string input, out bool hasMissingEmojis)
```

**Logic change** (in the emoji-found branch, before appending the sprite tag):
```
hexName = GetHexName(emojiSequence)

if EmojiSpriteRegistry.IsKnown(hexName) || EmojiSpriteRegistry.IsPending(hexName):
    append <sprite name="hexName">         // known: renders as sprite
                                           // pending: TMP will find it once registered
else:
    append raw Unicode chars               // font fallback renders it
    hasMissingEmojis = true
    EmojiSpriteRegistry.ClearFailed(hexName)   // no-op if not failed; ensures retry
    EmojiPatchService.Instance.RequestEmoji(hexName)
```

`RequestEmoji` already guards against double-queuing (checks `IsPending`), so
calling it every time an unknown emoji is encountered is safe — it's a no-op
after the first call per name.

Callers that don't need the flag use a discard: `Convert(text, out _)`.

---

### `MessageItemView` — modified
`Assets/Scripts/UI/MessageItemView.cs`

**New field:**
```csharp
private string _originalText;
```

**`SetText(string text)` (or equivalent bind method):**
```csharp
_originalText = text;
var converted = UnicodeEmojiConverter.ConvertRealEmojisToSprites(text, out bool hasMissing);
_label.text = converted;

if (hasMissing)
    EmojiPatchService.OnEmojiReady += OnEmojiReady;
```

**`OnEmojiReady(string name)`:**
```csharp
var reconverted = UnicodeEmojiConverter.ConvertRealEmojisToSprites(_originalText, out bool stillMissing);
if (reconverted != _label.text)
    _label.text = reconverted;
if (!stillMissing)
    EmojiPatchService.OnEmojiReady -= OnEmojiReady;
```

**`OnDisable()`:**
```csharp
EmojiPatchService.OnEmojiReady -= OnEmojiReady;
```

## Data Flow

```
App launch
  EmojiPatchService.Awake()
    → build registry from 31 atlases (~3,750 names)
    → load disk cache → register as TMP fallbacks
    → [chat UI renders — returning users see Twemoji immediately]

Message received
  MessageItemView.SetText(body)
    → Convert(body) → known emojis: sprite tags | unknown: raw Unicode
    → tmp.text = converted  [renders instantly — font fallback for unknowns]
    → if hasMissing: subscribe OnEmojiReady

  [background coroutine, max 3 concurrent]
  EmojiPatchService downloads PNG from jsdelivr
    → save to disk
    → create TMP_SpriteAsset → register fallback → update registry
    → OnEmojiReady.Invoke(name)
    → queue 5 skin-tone variants

  MessageItemView.OnEmojiReady(name)
    → re-run Convert(_originalText)
    → if result changed: update tmp.text  [Twemoji swaps in]
    → if no more missing: unsubscribe
```

## Error Handling

| Scenario | Behaviour |
|---|---|
| CDN fetch failure / 404 | `MarkFailed()` — raw Unicode persists; retries silently on next encounter |
| Partial skin-tone batch failure | Each variant retries independently; base + successful variants show Twemoji |
| `Texture2D.LoadImage` returns false | Log warning, skip disk write, `MarkFailed()` |
| `defaultSpriteAsset` is null | Log warning, no-op in `RegisterFallback()` |
| Disk write failure (storage full) | Try/catch: register in-memory only; re-fetches next launch |
| ZWJ sequence with no CDN match | 404 → `MarkFailed()` → font fallback permanently for this session |
| Registry called before `Build()` | `IsKnown()` returns false → raw Unicode + fetch queued; soft ordering constraint |

## Testing

**Unit tests (pure C#)**
- `EmojiSpriteRegistry`: state transitions — build, IsKnown, MarkPending→Register, MarkFailed→retry path
- `UnicodeEmojiConverter`: known emoji → sprite tag + `hasMissingEmojis` false; unknown → raw Unicode + flag true; mixed string → both correct

**Manual Play Mode checklist**
1. Fresh install: 🫪 in message → font fallback renders immediately → swaps to Twemoji within ~500ms
2. Second session (cached): 🫪 → Twemoji on first frame, no flash
3. Offline: unknown emoji → font fallback persists, no crash
4. Back online: scroll message out and back → Twemoji appears
5. Skin-tone variant: 🫪🏽 → base + 5 variants all resolve
6. Null `defaultSpriteAsset` → warning logged, no NullReferenceException

## Files Changed

| File | Change |
|---|---|
| `Assets/Scripts/Chat/EmojiSpriteRegistry.cs` | **New** |
| `Assets/Scripts/Chat/EmojiPatchService.cs` | **New** |
| `Assets/Scripts/Chat/UnicodeEmojiConverter.cs` | Modified — registry check + `out bool hasMissingEmojis` |
| `Assets/Scripts/UI/MessageItemView.cs` | Modified — `_originalText` + `OnEmojiReady` subscription on main message body paths (lines 237, 2555, 2568); other call sites updated to `out _` |
| `Assets/Scripts/UI/ChatItemView.cs` | Modified — updated to `out _` (chat list preview; font fallback acceptable for rare Emoji 17 in preview) |
| `Assets/Scripts/Main/ChatManager.cs` | Modified — 4 call sites updated to `out _` (chat name, last message preview, message body normalisation) |

### Caller strategy

| Call site | Strategy | Reason |
|---|---|---|
| `MessageItemView` main message body (×3) | `out bool hasMissing` + subscribe `OnEmojiReady` | Visible in chat view — swap to Twemoji when ready |
| `MessageItemView` sender name, filename, download button (×4) | `out _` | Peripheral UI — font fallback acceptable |
| `ChatItemView` last-message preview | `out _` | Small preview text — font fallback acceptable |
| `ChatManager` chat name / last message | `out _` | Chat list row — font fallback acceptable |
