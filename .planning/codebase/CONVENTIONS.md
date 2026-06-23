# Coding Conventions

**Analysis Date:** 2026-06-23

## Naming Patterns

**Files:**
- PascalCase for all C# classes: `MessageViewModel.cs`, `ChatManager.cs`, `MediaCacheManager.cs`
- Test files: `[Subject]Tests.cs` pattern (e.g., `ReplyParserTests.cs`, `OutboxStoreTests.cs`)
- Partial classes documented inline: `BotSettings.cs` + `BotSettings.Auth.cs` split for large components

**Functions:**
- PascalCase for all public and private methods: `GetBusinessIconSprite()`, `EnsureBotScoped()`, `SetReactionPreview()`
- Verb-first naming for action methods: `OpenSettings()`, `SaveImageToCache()`, `LoadImageFromCache()`
- Query methods prefix with `Get`/`Is`/`Try`: `GetMediaDirectory()`, `IsImageCached()`, `TryAliasCachedImage()`

**Variables:**
- camelCase for local variables and parameters: `tempId`, `chatId`, `fromUrl`, `toUrl`
- Single-letter loop counters acceptable: `i`, `j`
- Avoid single-letter variables otherwise

**Types and Properties:**
- PascalCase for all public properties: `whatsappProfileId`, `telegramProfileId`, `BotName`, `EditButton`
- PascalCase for types, interfaces, and enums: `MessageType`, `ChatOpenPhase`, `DeliveryStatus`
- Private fields: camelCase with optional leading underscore for clarity: `cachedUrlBotId`, `_phase`, `_refreshIssued`

## Code Style

**Formatting:**
- No automatic formatter detected in codebase; follows C# standards
- Indentation: 4 spaces (visible in all source files)
- Line length: no hard limit enforced, but typical files keep to ~100 columns for readability
- Brace style: Allman (opening brace on new line for classes/methods, K&R for inline blocks)

**Linting:**
- No ESLint/StyleCop detected; project relies on manual code review and C# conventions
- Validation hook: `validate-cs.sh` runs after every `Edit`/`Write` and checks C# quality (see `.claude/hooks/validate-cs.sh`)

## Import Organization

**Order:**
1. System and system collections: `using System;`, `using System.Collections;`, `using System.Collections.Generic;`
2. System.IO and other System namespaces: `using System.IO;`, `using System.Text;`, `using System.Security.Cryptography;`
3. UnityEngine core: `using UnityEngine;`, `using UnityEngine.UI;`
4. UnityEditor (only in `#if UNITY_EDITOR` blocks): `using UnityEditor;`
5. Third-party/plugin libraries: `using DG.Tweening;`, `using Newtonsoft.Json;`, `using Newtonsoft.Json.Linq;`
6. TMPro: `using TMPro;`
7. NUnit (test files only): `using NUnit.Framework;`
8. Project namespaces: `using Automation.BotSettingsUI;`

Example (from `Manager.cs` lines 1-11):
```csharp
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;
using System.IO;
using Automation.BotSettingsUI;
using DG.Tweening;
```

**No Path Aliases:** Import paths use full namespace hierarchy; no custom alias shortcuts detected.

## Error Handling

**Patterns:**
- Null checks inline before use: `if (string.IsNullOrEmpty(url)) return null;`
- Try-catch for file I/O and risky operations:
  ```csharp
  try
  {
      File.Copy(fromPath, toPath);
      return true;
  }
  catch (System.Exception ex)
  {
      Debug.LogWarning($"[MediaCacheManager] Cache alias failed: {ex.Message}");
      return false;
  }
  ```
- Exception type capture often uses `System.Exception` (fully qualified for clarity in some contexts)
- Debug.LogError for critical failures; Debug.LogWarning for recoverable issues
- API errors logged with status code and URL: `Debug.LogError($"[{request.responseCode}] {url}: {request.error}");`
- Return defaults on failure: null for objects, 0/empty collections for counts

## Logging

**Framework:** Debug.Log, Debug.LogWarning, Debug.LogError (no custom logger detected)

**Patterns:**
- Prefix logs with [ComponentName]: `[ClaudeTestBridge]`, `[MediaCacheManager]`
- Use string interpolation exclusively: `Debug.LogError($"[Component] Message: {variable}");`
- Structured details in method logs: `Debug.Log($"[ClaudeTestBridge] Starting EditMode test run (filter: {string.Join(", ", groups)})")`
- Test bridge logs include timestamps and phase info: `[ClaudeTestBridge] DONE — PASSED  passed={summary.passed} skipped={summary.skipped} total={summary.total}`

## Comments

**When to Comment:**
- Avoid redundant comments that restate code: `x++; // increment x` is not done
- Explain the WHY for non-obvious logic:
  ```csharp
  // The EditMode tests compile into Assembly-CSharp-Editor (no asmdef), so its dll
  // mtime identifies exactly which build of the test code a run executed.
  ```
- Document complex algorithms and state machines:
  ```csharp
  /// <summary>
  /// Three-phase chat-open state machine. Prep runs cache load and queues sync results
  /// without touching UI. Slide is the slide-in animation with all heavy main-thread
  /// work gated. Populate fires OnBatchMessagesLoaded and drains queued sync results.
  /// </summary>
  public enum ChatOpenPhase { Idle, Prep, Slide, Populate }
  ```

**JSDoc/TSDoc:**
- Use XML documentation (`/// <summary>`) for public methods and properties:
  ```csharp
  /// <summary>
  /// Returns the bot's business icon sprite, or null when no business type
  /// is set (mid-wizard) or the SO has no entry for the saved id. Cheap —
  /// PlayerPrefs read + dictionary lookup; safe to call from OnEnable.
  /// </summary>
  public Sprite GetBusinessIconSprite()
  ```
- Include usage notes and performance characteristics in `<summary>`
- Document parameters with `<paramref>` for clarity: `<paramref name="fromUrl"/>'s key`
- Document return value expectations: "Returns true when..."

## Function Design

**Size:** Keep under 30 lines when possible; extract longer logic into helper methods.
- Example: `MediaCacheManager.GetFilePathFromUrl()` and `EnsureBotScoped()` are ~10–15 lines each
- Long coroutines acceptable (API calls with setup/teardown can run 40+ lines)
- Test methods typically 5–15 lines (setup, act, assert)

**Parameters:**
- Pass primitive types and immutable data directly
- Pass callbacks via `System.Action<T>` for async continuations: `System.Action<T> callback`
- Use value objects (classes/structs) for related data to reduce parameter count
- Example: `OutboxStore.OutboxEntry` bundles `tempId`, `chatId`, `text`, `timestamp`, etc. into one object

**Return Values:**
- Return null or empty collections on failure; no exceptions thrown in utility methods unless fatal
- Boolean returns for success/failure queries: `IsImageCached()`, `TryAliasCachedImage()`
- T or null for lookups: `GetBusinessIconSprite()` returns `Sprite` or `null`
- Out parameters used occasionally: `TryGetById(id, out var entry)` (from BusinessTypesSO)

## Module Design

**Exports:**
- Singleton managers expose `Instance` static property for global access: `Manager.Instance`, `ChatManager.Instance`, `MediaCacheManager.Instance`
- Public methods on MonoBehaviours and utility classes are explicitly scoped
- Private fields hidden unless [SerializeField] for inspector access
- Properties (get/set) preferred over public fields for data access

**Barrel Files:**
- No barrel/index files detected; each class lives in its own .cs file
- Namespace organization: `Automation.BotSettingsUI` for UI builders; flat namespace for core Chat/Main scripts
- Imports are direct to class files

**Field Organization Pattern (observed in Bot.cs, Manager.cs):**
```csharp
[SerializeField] private Color backgroundActiveColor;     // Serialized fields
[SerializeField] private Image BotIconTile;

public string whatsappProfileId;                           // Public properties
public string telegramProfileId;

private RectTransform switchHandle;                        // Private fields
private Image switchBackgroundImage;

public Sprite GetBusinessIconSprite() { }                  // Methods follow

private Color GetTintColor() { }
```

## MonoBehaviour-Specific Patterns

**Lifecycle:**
- `Awake()` for initialization and self-reference caching
- `Start()` for cross-object references (after all Awake calls)
- `OnEnable()` / `OnDisable()` for event subscription/unsubscription
- [DefaultExecutionOrder] used to control execution order:
  - `[DefaultExecutionOrder(-200)]` for `MediaCacheManager` (early init)
  - `[DefaultExecutionOrder(-100)]` for `ChatManager` (before UI updates)

**SerializeField Convention:**
```csharp
[SerializeField] private GameObject WhatsappAuth;          // Camel case, private
[SerializeField] public TextMeshProUGUI BotName;           // Also public for direct access in some cases
[SerializeField] private Color backgroundActiveColor;      // Private by default
```

**Coroutine Pattern:**
- All async operations use `IEnumerator` + `yield return`
- No async/await in MonoBehaviours
- Example:
  ```csharp
  private IEnumerator SetSwitches()
  {
      yield return new WaitForEndOfFrame();
      // UI setup
  }
  ```

## Partial Classes

**Usage:** Large components split into logical files for maintainability
- `BotSettings.cs` — main config UI (5-tab interface)
- `BotSettings.Auth.cs` — partial class containing WhatsApp/Telegram auth flow (QR, code)
- Pattern: class name is `BotSettings`, each partial file is `BotSettings.{Feature}.cs`

## Event-Driven Patterns

**Manager Events (ChatManager, from CLAUDE.md):**
- Public event declarations with Action<T> signatures
- Example: `public event Action<ChatViewModel> OnChatAdded;`
- Fired by managers, subscribed to by views/controllers
- Views listen via `OnEnable()` subscribe / `OnDisable()` unsubscribe
- No polling; UI reacts to events only

**Callback Pattern:**
- Networking methods use `System.Action<T>` callbacks for results
- Example: `StartCoroutine(FetchData(param, result => HandleResult(result)))`

## Color and UI Constants

**Immutable Storage:**
- Colors defined as static readonly fields:
  ```csharp
  private static readonly Color CreateButtonDefaultColor = new Color32(0x00, 0x7A, 0xFF, 0xFF);
  private static readonly Color WhatsappBrandColor = new Color32(0x25, 0xD3, 0x66, 0xFF);
  public static readonly Color NeutralTile = new Color(0.85f, 0.85f, 0.85f);
  ```
- Hex notation via `Color32` for design-sourced colors
- RGB float notation (0–1 range) for dynamic/computed colors

## String Handling

**String Interpolation:**
- Always use `$"..."` for interpolation, never string.Format or concatenation
- Example: `$"[{request.responseCode}] {url}: {request.error}"`
- Empty/null checks: `string.IsNullOrEmpty(url)` or `string.IsNullOrWhiteSpace(content)`

## Serialization

**Data Models:**
- Marked `[Serializable]` for JsonUtility and Unity's save system
- Public fields for JSON parsing (JsonConvert, JsonUtility)
- Example: `RawMessage` with `[JsonProperty("isReply")]` for API response mapping
- PlayerPrefs for per-bot persistence: keyed by `transform.name + fieldName` (e.g., `Bot0Name`, `Bot0Products0`)

## Platform-Specific Code

**Isolation:**
- Bridge classes: `AndroidBridge`, `IOSBridge`, `IOSAudioFix` (in `Assets/Scripts/Chat/`)
- `#if UNITY_EDITOR` for editor-only code in utility classes
- Platform code never scattered through game logic; always delegated to bridges

## Third-Party Library Integration

**DOTween (Animation):**
- Static import via `using DG.Tweening;`
- Chainable API: `.DOAnchorPos()`, `.DOFade()`, `.DOPunchScale()`

**Newtonsoft.Json:**
- `using Newtonsoft.Json;` and `using Newtonsoft.Json.Linq;`
- `JsonConvert.DeserializeObject<T>()` for complex parsing
- `JsonUtility.ToJson()` for simple serialization (Unity native)

**TMPro:**
- Always used for UI text, never legacy Text component
- Rich text tags supported: `<link>`, `<sprite>`, `<b>`, `<i>`
- Custom components: `TextMeshProUGUI` on all text elements

---

*Convention analysis: 2026-06-23*
