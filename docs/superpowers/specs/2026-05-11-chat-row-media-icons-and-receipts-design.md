# Chat Row — Media-Type Icons + Read-Receipt Ticks (Phase 2 of WhatsApp parity)

## Problem

Each chat row currently shows just the raw `last_message_data` text as its preview. Real WhatsApp prefixes the preview with two visual signals:

1. **Media-type icon** when the last message is a photo/video/voice/document/etc. — e.g. `📷 Photo`, `🎤 0:14`, `📄 invoice.pdf`. Lets the user know at a glance what kind of message was last sent without opening the chat.
2. **Read-receipt ticks** when the last message is *outgoing* — gray `✓` sent, gray `✓✓` delivered, blue `✓✓` read. One of the most recognizable WhatsApp visual elements.

Both are absent. Phase 1 added the unread badge; Phase 2 closes the preview-line gap.

The Wappi `/chats/filter` response already returns the three fields needed (confirmed by the user's earlier API sample): `last_message_type`, `last_message_delivery_status`, `last_message_sender.isMe`. The current `ChatDialog` model drops all three.

## Goal

Render WhatsApp-faithful media-type icons and read-receipt ticks in every chat row's last-message preview on `Screen_Whatsapp`, using:

- **Media icons:** the project's existing `UnicodeEmojiConverter` + `EmojiOne` TMP sprite asset (📷 📹 🎤 📄 📍). Zero new assets — these emoji are already in EmojiOne and the conversion path is well-established.
- **Tick icons:** a NEW tiny custom TMP Sprite Asset (`ChatTicks`) with three programmatically-generated PNGs. Necessary because:
  - U+2713 `✓` is not in any project SF Pro Text SDF font asset (verified by grep on the `.asset` files).
  - U+2713 `✓` is not in the EmojiOne sprite asset (verified).
  - A custom sprite asset is the only path that guarantees rendering across iOS and Android with no font-fallback dependency.

## Non-goals

- No changes to the unread badge from Phase 1.
- No per-message read-receipt UI inside the chat thread itself (`MessageItemView`) — preview line only.
- No localization of fallback labels — we rely on Wappi's `last_message_data` to provide the text portion.
- No "sticker" emoji glyph (EmojiOne lacks a clean one; we render the text as-is).
- No hand-sourced art — the builder generates the three tick PNGs from a simple anti-aliased line-drawing routine. Users who want a different visual style can replace the PNGs after the fact (the sprite asset will continue to reference the same filenames).

## Data model — Wappi response (confirmed)

The user's earlier `/chats/filter` sample (text message):

```json
{
  "id": "77472714618@c.us",
  "last_message_delivery_status": "read",
  "last_message_id": "3A3A11795E74705F43A4",
  "last_message_type": "chat",
  "last_message_sender": { "isMe": true, "id": "...", "number": "...", "pushname": "..." },
  "last_message_data": "Оплатил",
  "unread_count": 0
}
```

Three new fields to capture:

- `last_message_type` (string, lowercase Wappi value)
- `last_message_delivery_status` (string, lowercase Wappi value)
- `last_message_sender` (nested object) — we only read `isMe`

`JsonUtility` will ignore unrequested fields on `last_message_sender` (id, number, pushname) without warning.

## Target file changes

Twelve files total. Four modify, three create-code, five generated-assets (via builder).

### 1. `Assets/Scripts/Chat/ChatDialog.cs` — add three fields

After the existing `public string last_message_id;` line, append:

```csharp
public string last_message_type;
public string last_message_delivery_status;
public ChatSender last_message_sender;
```

### 2. `Assets/Scripts/Chat/ChatSender.cs` — NEW tiny serializable model

```csharp
using System;

[Serializable]
public class ChatSender
{
    public bool isMe;
}
```

Other Wappi-returned fields on `last_message_sender` (id, number, pushname) are intentionally ignored — we only consume `isMe`.

### 3. `Assets/Scripts/Chat/ChatPreviewFormatter.cs` — NEW pure static composition helper

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class ChatPreviewFormatter
{
    // Sprite asset name and sprite names — must match what
    // ChatTicksSpriteAssetBuilder generates at Assets/Resources/Sprite Assets/ChatTicks.asset.
    private const string TickAssetName = "ChatTicks";
    private const string TickSent = "tick_sent";
    private const string TickDouble = "tick_double";
    private const string TickDoubleBlue = "tick_double_blue";

    // One-time discovery log per unknown Wappi value.
    private static readonly HashSet<string> LoggedUnknownTypes = new();
    private static readonly HashSet<string> LoggedUnknownStatuses = new();

    /// <summary>
    /// Composes a TMP-tagged preview string in WhatsApp format:
    ///   [tick-sprite]? [media-emoji]? [text]
    /// 
    /// Returns the formatted string. Emoji-to-sprite conversion (for the media
    /// emoji) is the caller's responsibility — run
    /// UnicodeEmojiConverter.ConvertRealEmojisToSprites on the result. The tick
    /// sprite tag references the ChatTicks asset directly and is not affected
    /// by the emoji converter (the converter only acts on Unicode emoji ranges,
    /// not on TMP tag syntax).
    /// </summary>
    public static string Format(string rawText, string type, string deliveryStatus, bool isMine)
    {
        var tick = isMine ? GetTickSprite(deliveryStatus) : null;
        var emoji = GetMediaEmoji(type);
        var text = rawText ?? string.Empty;

        if (tick == null && emoji == null) return text;

        var sb = new System.Text.StringBuilder(text.Length + 48);
        if (tick != null) { sb.Append(tick); sb.Append(' '); }
        if (emoji != null) { sb.Append(emoji); sb.Append(' '); }
        sb.Append(text);
        return sb.ToString();
    }

    private static string GetTickSprite(string status)
    {
        if (string.IsNullOrEmpty(status)) return null;
        switch (status.ToLowerInvariant())
        {
            case "sent":      return $"<sprite=\"{TickAssetName}\" name=\"{TickSent}\">";
            case "delivered": return $"<sprite=\"{TickAssetName}\" name=\"{TickDouble}\">";
            case "read":      return $"<sprite=\"{TickAssetName}\" name=\"{TickDoubleBlue}\">";
            default:
                if (LoggedUnknownStatuses.Add(status))
                    Debug.LogWarning($"[ChatPreviewFormatter] Unknown delivery status: '{status}'");
                return null;
        }
    }

    private static string GetMediaEmoji(string type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        switch (type.ToLowerInvariant())
        {
            case "chat":
            case "text":     return null;
            case "image":
            case "photo":    return "📷";
            case "video":    return "📹";
            case "voice":
            case "ptt":      return "🎤";
            case "audio":    return "🎵";
            case "document": return "📄";
            case "location": return "📍";
            case "sticker":  return null; // EmojiOne lacks a clean sticker glyph.
            default:
                if (LoggedUnknownTypes.Add(type))
                    Debug.LogWarning($"[ChatPreviewFormatter] Unknown message type: '{type}'");
                return null;
        }
    }
}
```

**Why static + pure:** zero state, easy to reason about, idiomatic match for the existing `UnicodeEmojiConverter` static helper.

**Why `HashSet<string>` for dedup logging:** a chat list can be large; without dedup an unknown Wappi value would log on every parse. One-per-value-per-session surfaces issues without flooding.

**Why sprite tags vs raw Unicode for ticks:** verified the project's SF Pro Text SDF font assets lack the `✓` (U+2713) glyph, and EmojiOne lacks the "2713" sprite. Sprite asset is the only path that renders reliably.

### 4. `Assets/Scripts/UI/ChatViewModel.cs` — expose three new properties + mutator

Three new properties after the existing `LastMessageId`:

```csharp
public string LastMessageType { get; private set; }
public string LastMessageDeliveryStatus { get; private set; }
public bool IsLastMessageMine { get; private set; }
```

Update the constructor signature (all defaults, so existing single call site keeps compiling during incremental tasks):

```csharp
public ChatViewModel(string chatId, string title, string avatarUrl,
                     string lastMessage, long lastTime, int unreadCount = 0,
                     string lastMessageId = null,
                     string lastMessageType = null,
                     string lastMessageDeliveryStatus = null,
                     bool isLastMessageMine = false)
```

Body assigns each new parameter. Add a single mutator with change-detection:

```csharp
public void UpdateLastMessageMeta(string type, string deliveryStatus, bool isMine)
{
    bool changed =
        LastMessageType != type ||
        LastMessageDeliveryStatus != deliveryStatus ||
        IsLastMessageMine != isMine;

    if (!changed) return;

    LastMessageType = type;
    LastMessageDeliveryStatus = deliveryStatus;
    IsLastMessageMine = isMine;
    NotifyUpdated();
}
```

**Why one mutator for three fields:** they always arrive together from `ParseChatsJson` (same JSON object). A single change-detection + single `NotifyUpdated` avoids redundant view repaints.

**Why `NotifyUpdated` and not `OnLastMessageChanged`:** the new event introduced in Phase 1's fix (commit `709774b`) is reserved for *bump-to-top* events. A status change from `delivered` → `read` must NOT reorder the chat list; only fire the general-update event so the view repaints in place.

### 5. `Assets/Scripts/Main/ChatManager.cs` — wire fields through `ParseChatsJson`

Inside the existing `foreach (var chat in response.dialogs)` loop:

**Create path** — extend the `new ChatViewModel(...)` call:

```csharp
bool isMine = chat.last_message_sender != null && chat.last_message_sender.isMe;
var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime,
                               unreadCount: chat.unread_count,
                               lastMessageId: chat.last_message_id,
                               lastMessageType: chat.last_message_type,
                               lastMessageDeliveryStatus: chat.last_message_delivery_status,
                               isLastMessageMine: isMine);
```

**Merge path** — after the existing `existingVm.UpdateLastMessageId(chat.last_message_id);` line:

```csharp
bool mergedIsMine = chat.last_message_sender != null && chat.last_message_sender.isMe;
existingVm.UpdateLastMessageMeta(chat.last_message_type, chat.last_message_delivery_status, mergedIsMine);
```

The `null` guard on `last_message_sender` is defensive — Wappi's contract is "the field is present per dialog" but a missing object would otherwise NPE on `.isMe`.

### 6. `Assets/Scripts/UI/ChatItemView.cs` — compose preview through the formatter

Update the existing `UpdatePreviewText(string rawMessage)` method. Current shape (around line 167):

```csharp
private void UpdatePreviewText(string rawMessage)
{
    if (string.IsNullOrEmpty(rawMessage))
    {
        lastMessageText.text = "";
        return;
    }
    // ... LRU cache lookup with key = chatId + "_" + rawMessage ...
    // ... if not cached: SplitLongWord truncation ...
    lastMessageText.text = slicedText;
}
```

Replace the body so it:

1. Calls `ChatPreviewFormatter.Format(rawMessage, vm?.LastMessageType, vm?.LastMessageDeliveryStatus, vm?.IsLastMessageMine ?? false)` to produce a string with raw Unicode emoji (📷 etc) and TMP sprite tags (for ticks).
2. Runs `UnicodeEmojiConverter.ConvertRealEmojisToSprites(formatted)` to convert Unicode emoji to EmojiOne sprite tags. The tick sprite tags pass through unchanged (the converter walks character-by-character; `<sprite="ChatTicks" name="...">` is ASCII).
3. Uses the resulting string for everything downstream — the LRU cache lookup, `SplitLongWord` truncation, and final `lastMessageText.text` assignment.

The LRU cache key changes from `chatId + "_" + rawMessage` to `chatId + "_" + finalFormatted`. Same `rawMessage` can produce different output depending on `type/status/mine`; the new key correctly invalidates when any of those change.

**Pipeline order matters.** The formatter must run BEFORE the emoji converter so that the Unicode media emoji (📷 etc) gets converted in the same pass as any emoji already inside `rawMessage`. The tick sprite tag is ASCII and the converter ignores it (verified by reading `UnicodeEmojiConverter.cs:14-89` — only Unicode codepoints inside the `IsEmoji` ranges are touched).

### 7. `Assets/Editor/ChatTicksSpriteAssetBuilder.cs` — NEW editor builder

Generates three 64×64 monochrome PNG ticks and assembles them into a single TMP Sprite Asset at `Assets/Resources/Sprite Assets/ChatTicks.asset`.

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates three tick sprite PNGs (sent, delivered, read-blue) and packages
/// them into a TMP Sprite Asset referenced by ChatPreviewFormatter.
///
/// Outputs:
///   Assets/Images/Chat/Ticks/tick_sent.png         (gray single ✓)
///   Assets/Images/Chat/Ticks/tick_double.png       (gray double ✓✓)
///   Assets/Images/Chat/Ticks/tick_double_blue.png  (WhatsApp-blue double ✓✓)
///   Assets/Resources/Sprite Assets/ChatTicks.asset (TMP_SpriteAsset)
///
/// Idempotent — re-running overwrites existing assets.
/// </summary>
public static class ChatTicksSpriteAssetBuilder
{
    private const int TextureSize = 64;
    private const float StrokeRadius = 4f;  // anti-aliased line "half-thickness"
    private const string TicksFolder = "Assets/Images/Chat/Ticks";
    private const string SpriteAssetFolder = "Assets/Resources/Sprite Assets";
    private const string SpriteAssetPath = "Assets/Resources/Sprite Assets/ChatTicks.asset";

    private static readonly Color32 Gray = new Color32(0x99, 0x99, 0x99, 0xFF);
    private static readonly Color32 Blue = new Color32(0x34, 0xB7, 0xF1, 0xFF);

    [MenuItem("Tools/Chat List/Generate Tick Sprites")]
    public static void Build()
    {
        EnsureFolder(TicksFolder);
        EnsureFolder(SpriteAssetFolder);

        WritePng($"{TicksFolder}/tick_sent.png",        DrawSingleTick(Gray));
        WritePng($"{TicksFolder}/tick_double.png",      DrawDoubleTick(Gray));
        WritePng($"{TicksFolder}/tick_double_blue.png", DrawDoubleTick(Blue));

        AssetDatabase.Refresh();
        ImportAsSprite($"{TicksFolder}/tick_sent.png");
        ImportAsSprite($"{TicksFolder}/tick_double.png");
        ImportAsSprite($"{TicksFolder}/tick_double_blue.png");

        BuildSpriteAsset();

        Debug.Log($"[ChatTicksSpriteAssetBuilder] Generated 3 ticks and {SpriteAssetPath}");
    }

    // ---- PNG generation ----

    private static Texture2D DrawSingleTick(Color32 color)
    {
        var tex = NewTransparent();
        // ✓ is two line segments. Geometry chosen to fit centered inside 64x64
        // with ~6px stroke (anti-aliased via distance-falloff in DrawLine).
        // Short stroke: (16, 30) → (28, 18). Long stroke: (28, 18) → (50, 46).
        DrawLine(tex, new Vector2(16, 30), new Vector2(28, 18), color);
        DrawLine(tex, new Vector2(28, 18), new Vector2(50, 46), color);
        tex.Apply();
        return tex;
    }

    private static Texture2D DrawDoubleTick(Color32 color)
    {
        var tex = NewTransparent();
        // Front tick (right) - same shape as single, shifted right by ~4px.
        DrawLine(tex, new Vector2(20, 30), new Vector2(32, 18), color);
        DrawLine(tex, new Vector2(32, 18), new Vector2(54, 46), color);
        // Back tick (left, behind) - smaller, behind the right one.
        DrawLine(tex, new Vector2(10, 30), new Vector2(22, 18), color);
        DrawLine(tex, new Vector2(22, 18), new Vector2(38, 38), color);
        tex.Apply();
        return tex;
    }

    private static Texture2D NewTransparent()
    {
        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var clear = new Color32(0, 0, 0, 0);
        var pixels = new Color32[TextureSize * TextureSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
        tex.SetPixels32(pixels);
        return tex;
    }

    private static void DrawLine(Texture2D tex, Vector2 a, Vector2 b, Color32 color)
    {
        // Anti-aliased line drawing: for each pixel in the bounding box,
        // compute distance to the line segment and apply soft falloff.
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - StrokeRadius - 1));
        int maxX = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + StrokeRadius + 1));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - StrokeRadius - 1));
        int maxY = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + StrokeRadius + 1));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float distance = DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), a, b);
                if (distance >= StrokeRadius + 1f) continue;
                float coverage = Mathf.Clamp01(StrokeRadius + 0.5f - distance);
                if (coverage <= 0f) continue;

                var existing = tex.GetPixel(x, y);
                float existingA = existing.a;
                float newA = (color.a / 255f) * coverage;
                float outA = newA + existingA * (1f - newA);
                if (outA < 0.001f) continue;

                var outColor = new Color(
                    (color.r / 255f * newA + existing.r * existingA * (1f - newA)) / outA,
                    (color.g / 255f * newA + existing.g * existingA * (1f - newA)) / outA,
                    (color.b / 255f * newA + existing.b * existingA * (1f - newA)) / outA,
                    outA);
                tex.SetPixel(x, y, outColor);
            }
        }
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        Vector2 projection = a + t * ab;
        return Vector2.Distance(p, projection);
    }

    // ---- Asset I/O ----

    private static void EnsureFolder(string path)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
    }

    private static void WritePng(string path, Texture2D tex)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static void ImportAsSprite(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = TextureSize;  // 1 unit = 1 sprite
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
    }

    private static void BuildSpriteAsset()
    {
        // Load the three sprites we just generated.
        var sentSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{TicksFolder}/tick_sent.png");
        var doubleSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{TicksFolder}/tick_double.png");
        var blueSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{TicksFolder}/tick_double_blue.png");
        if (sentSprite == null || doubleSprite == null || blueSprite == null)
        {
            Debug.LogError("[ChatTicksSpriteAssetBuilder] Could not load generated sprites.");
            return;
        }

        // Pack into an atlas texture (1 row, 3 columns).
        var atlas = new Texture2D(TextureSize * 3, TextureSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        atlas.SetPixels32(new Color32[TextureSize * 3 * TextureSize]); // transparent
        atlas.SetPixels(0,             0, TextureSize, TextureSize, sentSprite.texture.GetPixels());
        atlas.SetPixels(TextureSize,   0, TextureSize, TextureSize, doubleSprite.texture.GetPixels());
        atlas.SetPixels(TextureSize*2, 0, TextureSize, TextureSize, blueSprite.texture.GetPixels());
        atlas.Apply();

        var atlasPath = $"{SpriteAssetFolder}/ChatTicks_Atlas.png";
        File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(atlasPath);
        var atlasImporter = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
        atlasImporter.textureType = TextureImporterType.Sprite;
        atlasImporter.spriteImportMode = SpriteImportMode.Multiple;
        atlasImporter.mipmapEnabled = false;
        atlasImporter.alphaIsTransparency = true;
        atlasImporter.isReadable = true;
        atlasImporter.SaveAndReimport();
        var atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

        // Build TMP Sprite Asset.
        TMP_SpriteAsset spriteAsset;
        if (File.Exists(SpriteAssetPath))
        {
            spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpriteAssetPath);
        }
        else
        {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(spriteAsset, SpriteAssetPath);
        }

        spriteAsset.spriteSheet = atlasTexture;
        // Inherit material from the EmojiOne sprite asset (guaranteed to exist
        // in this project). Falls back to TMP_Settings default if unavailable.
        var emojiOne = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(
            "Assets/TextMesh Pro/Resources/Sprite Assets/EmojiOne.asset");
        spriteAsset.material = emojiOne != null
            ? new Material(emojiOne.material) { mainTexture = atlasTexture }
            : null;

        var glyphTable = new List<TMP_SpriteGlyph>(3);
        var charTable = new List<TMP_SpriteCharacter>(3);
        AddGlyph(glyphTable, charTable, 0u, "tick_sent",         0,           TextureSize);
        AddGlyph(glyphTable, charTable, 1u, "tick_double",       TextureSize, TextureSize);
        AddGlyph(glyphTable, charTable, 2u, "tick_double_blue",  TextureSize*2, TextureSize);

        spriteAsset.spriteGlyphTable = glyphTable;
        spriteAsset.spriteCharacterTable = charTable;
        spriteAsset.UpdateLookupTables();

        EditorUtility.SetDirty(spriteAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void AddGlyph(List<TMP_SpriteGlyph> glyphs, List<TMP_SpriteCharacter> chars,
                                  uint index, string name, int atlasX, int size)
    {
        var glyph = new TMP_SpriteGlyph
        {
            index = index,
            sprite = null,
            glyphRect = new UnityEngine.TextCore.GlyphRect(atlasX, 0, size, size),
            metrics = new UnityEngine.TextCore.GlyphMetrics(size, size, 0, size, size),
            scale = 1f,
            atlasIndex = 0,
        };
        glyphs.Add(glyph);

        var character = new TMP_SpriteCharacter(0u, glyph)
        {
            name = name,
        };
        chars.Add(character);
    }
}
#endif
```

**Key implementation choices:**

- **`TextureSize = 64`** — small enough to be cheap, large enough that the anti-aliased line at preview size (rendered around 36-40pt in the prefab) looks crisp. The sprite asset is rendered with bilinear filtering so it scales smoothly.
- **Stroke geometry hand-tuned** for visual balance. Single tick is 14×28 within a 64×64 frame; double tick has a back-stroke ~4px behind the front for the WhatsApp "double" look. Geometry derived from inspecting WhatsApp's actual icons at high zoom and approximating proportions.
- **Anti-aliasing via distance-to-segment** — each pixel computes its perpendicular distance to the line; coverage is `clamp01(stroke_radius + 0.5 - distance)`. This produces ~1-pixel of edge softness without needing supersampling.
- **Alpha blending** preserves cross-stroke continuity for the double-tick (where the two strokes overlap).
- **Atlas: `ChatTicks_Atlas.png`** — single 192×64 PNG with three sub-sprites laid out left-to-right. Generated alongside the individual PNGs for clarity (you can open `ChatTicks_Atlas.png` and see all three side-by-side). The TMP Sprite Asset references the atlas, not the individual PNGs.
- **`Assets/Resources/Sprite Assets/`** — matches the existing project location (`EmojiOne` lives in `Assets/TextMesh Pro/Resources/Sprite Assets/`, but the project also uses `Assets/Resources/Sprite Assets/` for auto-generated atlas chunks). Either works; chose the latter to avoid touching the vendored TextMesh Pro folder.
- **Sprite name match** between PNG filenames, TMP Sprite Asset character entries, and `ChatPreviewFormatter` constants: `tick_sent` / `tick_double` / `tick_double_blue`. Any rename requires changes in all three places.
- **`spriteAsset.material = TMP_SpriteAsset.GetDefaultSpriteAsset()?.material`** — borrows the default sprite-rendering material (uses TMP's built-in sprite shader). Avoids having to instantiate one.
- **`UpdateLookupTables()`** — rebuilds the hash-based name → glyph lookup that `<sprite name="...">` resolution depends on. Without this, TMP returns the missing-sprite glyph.
- **Idempotency** — if `ChatTicks.asset` already exists, the builder updates it in place instead of creating a duplicate. PNG overwrites are file-level (the same path is written), so re-running produces the same result.

**The builder is a one-time menu click.** Once `ChatTicks.asset` exists, future runs of `Tools → Chat List → Add Unread Badge To ChatItem` (the Phase 1 builder) do not interact with it. The two builders are independent.

### 8-10. Generated assets

The builder produces (idempotently):

- `Assets/Images/Chat/Ticks/tick_sent.png`
- `Assets/Images/Chat/Ticks/tick_double.png`
- `Assets/Images/Chat/Ticks/tick_double_blue.png`
- `Assets/Resources/Sprite Assets/ChatTicks.asset`
- `Assets/Resources/Sprite Assets/ChatTicks_Atlas.png`

These are tracked in git (so the build artifact is checked in alongside the builder).

## Behavior matrix

| `type` | `status` | `isMine` | Output (before `UnicodeEmojiConverter`) |
|---|---|---|---|
| `chat` | `null` | `false` | `Hello` |
| `chat` | `null` | `true` | `Hello` |
| `chat` | `sent` | `true` | `<sprite="ChatTicks" name="tick_sent"> Hello` |
| `chat` | `delivered` | `true` | `<sprite="ChatTicks" name="tick_double"> Hello` |
| `chat` | `read` | `true` | `<sprite="ChatTicks" name="tick_double_blue"> Hello` |
| `image` | `null` | `false` | `📷 Photo` |
| `image` | `read` | `true` | `<sprite="ChatTicks" name="tick_double_blue"> 📷 Photo` |
| `voice` | `null` | `false` | `🎤 0:14` |
| `video` | `delivered` | `true` | `<sprite="ChatTicks" name="tick_double"> 📹 Video` |
| `document` | `read` | `true` | `<sprite="ChatTicks" name="tick_double_blue"> 📄 invoice.pdf` |
| `sticker` | `null` | `false` | `Sticker` (no emoji) |
| `unknown_type_x` | `read` | `true` | `<sprite="ChatTicks" name="tick_double_blue"> Hello` + warn-log once |
| `chat` | `weird_status_y` | `true` | `Hello` + warn-log once |
| `chat` | `read` | `false` | `Hello` (received message; status ignored) |

After `UnicodeEmojiConverter` runs, the Unicode emoji (📷📹🎤📄📍🎵) become `<sprite name="<hex>">` from EmojiOne. The tick sprite tags pass through unchanged because they don't contain Unicode emoji codepoints.

## Bump-to-top behavior (regression check)

`UpdateLastMessageMeta` calls `NotifyUpdated()` only, NOT the `OnLastMessageChanged` event introduced in Phase 1's fix (commit `709774b`). This preserves the invariant: only `UpdateLastMessage` (which represents a genuine new-message arrival) bumps the row to the top via `SetAsFirstSibling()`. Status flips like `delivered` → `read` repaint in place.

## Edge cases

- **`last_message_sender == null`**: treat as not-mine. `null` guard in both `ParseChatsJson` paths.
- **`last_message_type == null`**: no emoji prefix. Text passes through unchanged.
- **`last_message_delivery_status == null` AND `isMine == true`**: no tick. Treated as "status unknown" (could be a chat with no sent messages, or a server-side gap).
- **`rawText == null`**: formatter returns empty string. `UpdatePreviewText` early-returns with `lastMessageText.text = ""`.
- **`ChatTicks` sprite asset missing at runtime** (builder never run): TMP renders the missing-sprite placeholder (typically a question mark in a box). Visible immediately, easy to fix by running the menu item once.
- **Sticker without emoji**: text passes through unchanged.
- **Status change on a chat already at top of list**: row repaints in place (no `SetAsFirstSibling`). No flicker.
- **LRU cache pressure**: existing 500-entry cap; key cardinality increases modestly (chat list × formatted-string-variations). Typical values stay well under cap.

## Testing

Manual on device (1080×2400 game-view) after the builder run + all code tasks:

1. Run `Tools → Chat List → Generate Tick Sprites` from the Unity menu. Confirm Console logs success. Inspect `Assets/Resources/Sprite Assets/ChatTicks_Atlas.png` — three monochrome checkmarks should be visible.
2. Connect Wappi bot. Find chats representing each `last_message_type` value (chat, image, video, voice, document, location, sticker). Send some yourself and receive some from another phone.
3. Verify each preview row matches the behavior matrix above.
4. **Tick rendering check**: verify the tick sprites render as crisp checkmarks (not question marks, not missing-sprite glyph). If they fail to render, the sprite asset wasn't loaded — verify the file exists at `Assets/Resources/Sprite Assets/ChatTicks.asset` and that its `Lookup Tables` were updated.
5. **Color check**: the read-blue tick should be WhatsApp blue (`#34B7F1`); the other two gray (`#999999`).
6. **Received-message rule**: open a chat where the LATEST message was received from the other person. Confirm no tick appears for that row regardless of `delivery_status`.
7. **Status change live**: send a message from the app, wait for the other phone to read it. Wait for next sync. Confirm the row's tick changes from gray ✓✓ → blue ✓✓ **without** the row jumping to the top.
8. **Unknown type discovery**: check Console for `[ChatPreviewFormatter] Unknown message type: 'X'` warnings. Add any new types to the mapping table; commit as a follow-up.
9. **Emoji-in-text passthrough**: verify chats where the original `last_message_data` contains real emoji (e.g., 👰🏻‍♀️ in chat names) still render correctly.

## Risks

- **Tick sprite visual style**: programmatic drawing may produce a less-polished result than hand-crafted art. If the look is unsatisfactory after testing, you can either (a) tune the geometry constants in `ChatTicksSpriteAssetBuilder` and re-run, or (b) replace the three PNGs by hand at the same paths (the TMP Sprite Asset will re-import them on next refresh).
- **Wappi `last_message_type` values differ from assumed list**: caught by the unknown-type log.
- **Wappi `last_message_delivery_status` values differ**: caught by the unknown-status log.
- **`last_message_sender` schema drift**: if Wappi changes the field shape, `JsonUtility` silently sets `ChatSender.isMe = false`. Symptom: all rows render as not-mine. Visible immediately during testing.
- **`TMP_SpriteAsset.GetDefaultSpriteAsset()` returns null**: rare but possible if the project's TMP_Settings hasn't been initialized. The builder falls back to a `null` material; the sprite asset still works (TMP uses a default material at render time).

## Out of scope (future phases)

- Per-message read-receipt UI inside `MessageItemView` (the thread view).
- Localized fallback labels (currently rely on Wappi's `last_message_data`).
- Sticker preview thumbnail (would require fetching media + new prefab layout).
- "Typing..." / "Recording audio..." preview text (requires Wappi presence data).
- Replacement of the programmatic tick PNGs with hand-crafted art (always possible later by overwriting the files).
