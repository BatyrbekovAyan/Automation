# Runtime-editable n8n URL — Design

- **Date:** 2026-06-30
- **Status:** Approved (design)
- **Relates to:** Sub-project 1 (switchable n8n endpoint). Removes the rebuild-on-tunnel-change churn.

## Background

The app's n8n base URL comes from `secrets.json` → `n8nBaseUrl`, which is **baked into the build**
(StreamingAssets). During dev we use a cloudflared **quick tunnel** whose URL changes every time
the tunnel restarts (it died once already, producing an `NSURLErrorDomain -1003` "hostname not
found" on the device). Today, each URL change forces: edit `secrets.json` → rebuild → reinstall.
A named/stable tunnel would fix this but needs a registered domain (`ayanbatyrbekov.kz` is not
registered), which isn't available now.

## Goal

Let the developer change the n8n URL **on the device, with no rebuild.** Paste the current tunnel
URL into an in-app field; it takes effect on the next n8n call.

### Definition of done
1. With a value saved in the in-app dev field, `Manager.n8nBaseUrl` returns it (overriding
   `secrets.json`), and creating a bot hits that URL — verified by an n8n execution appearing.
2. Clearing the field falls back to `secrets.json` `n8nBaseUrl`, then the Cloud default.
3. No app rebuild is needed to switch URLs once the build includes this feature.

## Design

### Component 1 — resolution precedence (`Manager.cs`)

Add a PlayerPrefs key and make `n8nBaseUrl` consider the on-device override first:

- New const: `public const string DevN8nBaseUrlKey = "DevN8nBaseUrl";`
- Keep the existing pure helper but add a 2-arg overload (precedence: override → configured →
  Cloud default; trailing slash trimmed). The existing 1-arg version delegates, so the current
  5 `N8nBaseUrlTests` stay valid:

```csharp
public static string ResolveN8nBaseUrl(string configured) => ResolveN8nBaseUrl(null, configured);

public static string ResolveN8nBaseUrl(string overrideUrl, string configured)
{
    if (!string.IsNullOrWhiteSpace(overrideUrl)) return overrideUrl.Trim().TrimEnd('/');
    if (!string.IsNullOrWhiteSpace(configured))  return configured.Trim().TrimEnd('/');
    return "https://bagkz.app.n8n.cloud";
}
```

- `n8nBaseUrl` reads the live override from PlayerPrefs (not the cached `Secrets`) so a change
  takes effect immediately:

```csharp
public static string n8nBaseUrl =>
    ResolveN8nBaseUrl(PlayerPrefs.GetString(DevN8nBaseUrlKey, ""), Secrets.Data.n8nBaseUrl);
```

The resolution stays pure/testable (strings in, string out); only the property touches
PlayerPrefs/Secrets.

### Component 2 — in-app field (`ProfilePage.cs` + the edit popup)

Reuse the existing **Profile → Edit** popup (which already has `editNameInput`, `editEmailInput`,
`editSaveButton`, `editCancelButton`, wired via `PopupUI.WireFingerUp`).

- Add one `TMP_InputField` to the edit popup, placed below the email field, styled by duplicating
  the existing email input (consistent look). Placeholder: `n8n URL (dev) — blank = secrets/Cloud`.
- New serialized ref in `ProfilePage`: `[SerializeField] private TMP_InputField editN8nUrlInput;`
  and (to gate visibility) the input's root GameObject.
- **Visibility:** show the field only in development builds — gate on `Debug.isDebugBuild`
  (set the input root active = `Debug.isDebugBuild` when opening the popup). Requires the Unity
  build to have **Development Build** checked (already the case for on-device debugging).
- **Load** (in `OpenEditPopup`): `editN8nUrlInput.text = PlayerPrefs.GetString(Manager.DevN8nBaseUrlKey, "");`
- **Save** (in `SaveProfile`): `PlayerPrefs.SetString(Manager.DevN8nBaseUrlKey, editN8nUrlInput.text.Trim());`
  (empty string clears the override). Existing name/email save behavior is unchanged.

The override is read live by `Manager.n8nBaseUrl`, so after Save the next bot create/edit/activate
call uses the new URL with no restart.

### UI construction note
Adding the field touches the Main scene / edit-popup prefab. Build it the project's way: duplicate
the existing email `TMP_InputField`, reposition below it, set the placeholder, and wire the new
serialized reference on `ProfilePage` via `SerializedObject` — done through the Unity Editor
(manual or a small `[MenuItem]` builder), then save the scene. Follow `.claude/rules/editor-scripts.md`
and the `unity-ui-builder` skill. (See [[project_unity_builder_scene_save]], [[project_builder_rewire_consumers]].)

## Risks & edge cases
- **Field hidden if not a Development Build:** `Debug.isDebugBuild` is false in non-dev builds, so
  the field won't show — acceptable (prod hides the dev field), but the dev device build must be a
  Development Build. The override in PlayerPrefs is still honored even if the field is hidden.
- **Stale override:** if a saved override points at a dead tunnel, calls fail (same `-1003`). Fix =
  paste the new URL (or clear the field to fall back to `secrets.json`). Document this.
- **Production safety:** with no override set and empty `secrets.json` `n8nBaseUrl`, behavior is
  unchanged (Cloud default). The change is additive.
- **Don't break existing tests:** the 1-arg `ResolveN8nBaseUrl` is preserved (delegates), so the
  current 5 tests pass unchanged.

## Testing
- **EditMode (pure resolution):** add to `N8nBaseUrlTests` — override wins over configured;
  blank/whitespace override falls back to configured; both blank → Cloud default; override trailing
  slash trimmed; override trimmed of surrounding whitespace. Existing 1-arg tests remain green.
- **Manual (DoD):** Development build → Profile → Edit → paste the current tunnel URL → Save →
  create a bot → confirm an n8n execution appears at that URL. Then change the URL (new tunnel) and
  create another bot **without rebuilding** → confirm it hits the new URL. Clear the field → falls
  back to `secrets.json`.

## Out of scope
- Named/stable tunnel + domain registration (revisit if a domain becomes available).
- Any non-dev/end-user surfacing of the field.
