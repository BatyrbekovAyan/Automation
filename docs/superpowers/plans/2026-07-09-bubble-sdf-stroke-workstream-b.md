# Bubble SDF Stroke (Workstream B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the message bubble's fake two-rounded-rects outline with a real, uniform, self-anti-aliased SDF stroke drawn by the bubble's own shader, with a whole-pixel-snapped border width.

**Architecture:** A vendored, self-contained rounded-corners shader gains a `_BorderColor` + `_BorderWidth` and composites an inner border band via one extra SDF cutoff (`interior = AntialiasedCutoff(sd + _BorderWidth)`). A new `ImageWithRoundedCornersBordered` component drives it (radius + snapped border width via the shared `PixelSnap` helper). `MessageItemView` collapses its dual `bubbleBackground` + `Outline` rounded images into the single bordered component; the `Outline` GameObject and `MirrorSize` are retired. The little tail keeps its existing `TailOutline` sprite.

**Tech Stack:** Unity 6000.3.9f1, URP, CG/HLSL UI shader, C# (Assembly-CSharp), Nobi.UiRoundedCorners (reference only — not modified).

## Global Constraints

- Unity **6000.3.9f1**, URP; single scene; bubbles render inside a **`RectMask2D`** — the shader MUST keep stencil + `UNITY_UI_CLIP_RECT` support and the bubble Image MUST stay `Maskable = true`.
- Shader is **vendored into `Assets/`** and **self-contained** — inline the SDF helpers (do not `#include` the PackageCache `.cginc` files, which are regenerated). The package `Library/PackageCache/com.nobi.roundedcorners@…/UiRoundedCorners/RoundedCorners.shader` is the **reference** for the stencil/property/vertex boilerplate to copy verbatim; do not edit the package.
- **`half` is a reserved HLSL type** — SDF helper params use `halfSize`, never `half`.
- Border width policy: **nearest whole physical pixel**, via `PixelSnap.SnapUnits(designUnits, canvas)` (existing, from Workstream A) — `= max(1, round(units * rootCanvas.scaleFactor)) / rootCanvas.scaleFactor`. Default `designBorderUnits = 1` (today's `extraSize:2` ÷ 2 sides).
- **Border color** = `(0.851, 0.831, 0.792, 1)` (the current `Outline` image color). Bubble **fill** colors are unchanged: `incomingColor` = white, `outgoingColor` = `(0.8, 1, 0.8)` (`MessageItemView.cs:95-96`).
- Bubble corner **radius** stays **28** (`MessageItemView.cs:3661`).
- **Shader stripping:** a shader used only via `Shader.Find` at runtime is stripped from device builds unless referenced — it MUST be added to **Project Settings → Graphics → Always Included Shaders**. Verify on a real device build.
- The Editor is typically **open** during this work: compile via mcp-unity `execute_menu_item "Assets/Refresh"` then `get_console_logs` (errors); a domain reload restarts the MCP bridge (a command may time out once — retry after the reload). Prefab/scene mutation is done in the Editor; the huge Main.unity/prefab diffs after save are the project's known benign churn.
- Commits: stage the specific files + their `.meta`; per-task commit consent; work on a branch off `main` (not `main` directly).
- C# quality: `[SerializeField] private` for inspector fields; PascalCase; no placeholders.

---

### Task 1: Bordered SDF shader (`RoundedCornersBordered`)

**Files:**
- Create: `Assets/Shaders/RoundedCornersBordered.shader`
- Modify: `ProjectSettings/GraphicsSettings.asset` (add to Always Included Shaders — done via the Editor, not by hand-editing)

**Interfaces:**
- Produces: shader named `"UI/RoundedCorners/RoundedCornersBordered"` with float4 `_WidthHeightRadius` (w, h, radius*2, 0), float4 `_OuterUV`, fixed4 `_BorderColor`, float `_BorderWidth` (local rect units).

No unit test (GPU shader). Validated by Editor compile + a manual test material.

- [ ] **Step 1: Write the shader**

Create `Assets/Shaders/RoundedCornersBordered.shader`:

```hlsl
Shader "UI/RoundedCorners/RoundedCornersBordered" {
    Properties {
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        _WidthHeightRadius ("WidthHeightRadius", Vector) = (0,0,0,0)
        _OuterUV ("image outer uv", Vector) = (0, 0, 1, 1)
        _BorderColor ("Border Color", Color) = (0,0,0,1)
        _BorderWidth ("Border Width", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Stencil {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off

        Pass {
            CGPROGRAM
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _WidthHeightRadius;
            float4 _OuterUV;
            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _BorderColor;
            float _BorderWidth;
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert (appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                if (_UIVertexColorAlwaysGammaSpace)
                    if (!IsGammaSpace())
                        o.color = float4(UIGammaToLinear(o.color.xyz), o.color.w);
                return o;
            }

            // Inlined SDF (from Nobi SDFUtils). NOTE: 'half' is a reserved type — use halfSize.
            float sdfRectangle(float2 p, float2 halfSize) {
                float2 d = abs(p) - halfSize;
                return length(max(d, 0)) + min(max(d.x, d.y), 0);
            }
            float sdfRoundedRect(float2 p, float radius, float2 halfSize) {
                return sdfRectangle(p, halfSize - radius) - radius;
            }
            float aaCutoff(float dist) {
                float w = fwidth(dist) * 0.5;
                return smoothstep(w, -w, dist);
            }

            fixed4 frag (v2f i) : SV_Target {
                float2 uvS = i.uv;
                uvS.x = (uvS.x - _OuterUV.x) / (_OuterUV.z - _OuterUV.x);
                uvS.y = (uvS.y - _OuterUV.y) / (_OuterUV.w - _OuterUV.y);

                fixed4 fill = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                fill.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                float2 size = _WidthHeightRadius.xy;
                float radius = _WidthHeightRadius.z;
                float2 p = (uvS - 0.5) * size;
                float sd = sdfRoundedRect(p, radius * 0.5, size * 0.5);

                float fillAlpha = aaCutoff(sd);                 // outer AA edge
                float interior  = aaCutoff(sd + _BorderWidth);  // 1 deep inside, AA at inner border edge

                fixed4 col;
                col.rgb = lerp(_BorderColor.rgb, fill.rgb, interior);
                col.a   = min(lerp(_BorderColor.a, fill.a, interior), fillAlpha);

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: Compile-check in the Editor**

Load bridge tools: `ToolSearch` → `select:mcp__mcp-unity__execute_menu_item,mcp__mcp-unity__get_console_logs`. Run `execute_menu_item "Assets/Refresh"`, then `get_console_logs` (logType `error`).
Expected: no shader compile errors. If `half`-as-identifier or any CG error appears, fix and refresh. Confirm `Assets/Shaders/RoundedCornersBordered.shader.meta` exists.

- [ ] **Step 3: Add to Always Included Shaders**

In Unity: **Project Settings → Graphics → Always Included Shaders** → increase Size by 1 → set the new element to `UI/RoundedCorners/RoundedCornersBordered`. (This edits `ProjectSettings/GraphicsSettings.asset`.)

- [ ] **Step 4: Manual render validation**

In a scratch scene or the open scene: create a UI `Image`, add a `Material` using this shader, set `_WidthHeightRadius = (200, 120, 56, 0)` (radius 28 × 2), `_BorderColor = (0.851,0.831,0.792,1)`, `_BorderWidth = 4`, Image color = green. Confirm: a filled green rounded rect with a uniform ~4-unit warm-gray inner border, smooth corners, no clipping artifacts. Delete the scratch objects.

- [ ] **Step 5: Commit**

```bash
git add Assets/Shaders/RoundedCornersBordered.shader Assets/Shaders/RoundedCornersBordered.shader.meta \
        ProjectSettings/GraphicsSettings.asset
git commit -m "feat(ui): self-contained SDF bordered rounded-corners shader"
```

---

### Task 2: `ImageWithRoundedCornersBordered` component

**Files:**
- Create: `Assets/Scripts/Chat/ImageWithRoundedCornersBordered.cs`

**Interfaces:**
- Consumes: the Task 1 shader (by name `"UI/RoundedCorners/RoundedCornersBordered"`); `PixelSnap.SnapUnits(float, Canvas)` (Workstream A).
- Produces: component with public `float radius`, `Color borderColor`, `float designBorderUnits`, `bool enabled`, and methods `void Validate()`, `void Refresh()` (same surface `MessageItemView.RefreshCorners` drives on `ImageWithRoundedCorners`), plus `void SetBorderVisible(bool)`.

No unit test (MonoBehaviour + GPU material; the snap math is `PixelSnap`, already tested). Validated by compile + Task 6 device pass.

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/Chat/ImageWithRoundedCornersBordered.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rounded-corners UI image that also draws a uniform, self-anti-aliased SDF border via
/// UI/RoundedCorners/RoundedCornersBordered. Border width is snapped to a whole physical pixel
/// (shared PixelSnap helper). Mirrors the public surface of Nobi's ImageWithRoundedCorners
/// (radius / Validate / Refresh) so MessageItemView.RefreshCorners can drive it identically.
/// </summary>
[ExecuteInEditMode]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ImageWithRoundedCornersBordered : MonoBehaviour
{
    private static readonly int Props = Shader.PropertyToID("_WidthHeightRadius");
    private static readonly int OuterUvProp = Shader.PropertyToID("_OuterUV");
    private static readonly int BorderColorProp = Shader.PropertyToID("_BorderColor");
    private static readonly int BorderWidthProp = Shader.PropertyToID("_BorderWidth");

    public float radius = 28f;
    public Color borderColor = new Color(0.851f, 0.831f, 0.792f, 1f);
    [Tooltip("Authored per-side border width in canvas units. Snapped to whole px at runtime.")]
    public float designBorderUnits = 1f;

    private Material material;
    private Vector4 outerUV = new Vector4(0, 0, 1, 1);
    private Canvas canvas;
    [HideInInspector, SerializeField] private MaskableGraphic image;

    private void OnEnable() { Validate(); Refresh(); }

    private void OnValidate() { Validate(); Refresh(); }

    private void OnDestroy()
    {
        if (image != null) image.material = null;
        DestroyImmediate(material);
        image = null;
        material = null;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (enabled && material != null) Refresh();
    }

    public void Validate()
    {
        if (material == null)
            material = new Material(Shader.Find("UI/RoundedCorners/RoundedCornersBordered"));
        if (image == null) TryGetComponent(out image);
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (image != null) image.material = material;
        if (image is Image uiImage && uiImage.sprite != null)
            outerUV = UnityEngine.Sprites.DataUtility.GetOuterUV(uiImage.sprite);
    }

    public void Refresh()
    {
        if (material == null) return;
        var rect = ((RectTransform)transform).rect;
        material.SetVector(Props, new Vector4(rect.width, rect.height, radius * 2f, 0));
        material.SetVector(OuterUvProp, outerUV);
        material.SetColor(BorderColorProp, borderColor);
        float snapped = canvas != null ? PixelSnap.SnapUnits(designBorderUnits, canvas) : designBorderUnits;
        material.SetFloat(BorderWidthProp, snapped);
    }

    /// <summary>Hide the border (e.g. transparent sticker bubble) by zeroing its width.</summary>
    public void SetBorderVisible(bool visible)
    {
        if (material == null) Validate();
        if (material == null) return;
        float snapped = canvas != null ? PixelSnap.SnapUnits(designBorderUnits, canvas) : designBorderUnits;
        material.SetFloat(BorderWidthProp, visible ? snapped : 0f);
    }
}
```

- [ ] **Step 2: Compile-check**

`execute_menu_item "Assets/Refresh"`, then `get_console_logs` (error). Expected: no errors; `ImageWithRoundedCornersBordered.cs.meta` created.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/ImageWithRoundedCornersBordered.cs Assets/Scripts/Chat/ImageWithRoundedCornersBordered.cs.meta
git commit -m "feat(ui): ImageWithRoundedCornersBordered — SDF border, snapped width"
```

---

### Task 3: MessageItemView — collapse the dual outline into the bordered component

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (the `outline` field at :20; `RefreshCorners` at :2671; the deferred `RefreshCorners(outline)` / `MirrorSize.UpdateSize` calls at :1773, :2640-2646, :2664; the transparent/color/outline block at ~:3624-3668)

**Interfaces:**
- Consumes: `ImageWithRoundedCornersBordered` (Task 2).
- Produces: no new public surface; `bubbleBackground` now carries `ImageWithRoundedCornersBordered`; the `outline` field is removed.

This is a delicate refactor of a large file. The implementer MUST read each target region in the current file before editing (line numbers drift). No unit test — behavior verified in Task 6.

- [ ] **Step 1: Make `RefreshCorners` drive both rounded types**

`RefreshCorners` (currently `MessageItemView.cs:2671`) only handles `ImageWithRoundedCorners`. `messageImage` keeps that type (media rounding); `bubbleBackground` now uses the bordered type. Replace the method body so it refreshes whichever is present:

```csharp
private void RefreshCorners(GameObject targetObject)
{
    if (targetObject == null) return;

    if (targetObject.TryGetComponent<ImageWithRoundedCorners>(out var rounded) && rounded.enabled)
    {
        float r = rounded.radius;
        rounded.radius = 0; rounded.radius = r;   // force the shader size rebake (see note below)
        rounded.Validate();
        rounded.Refresh();
    }
    else if (targetObject.TryGetComponent<ImageWithRoundedCornersBordered>(out var bordered) && bordered.enabled)
    {
        float r = bordered.radius;
        bordered.radius = 0; bordered.radius = r;
        bordered.Validate();
        bordered.Refresh();
    }
    // Rest of the existing method (CornerBakeProp staleness rebake under the RectMask2D stencil
    // copy) stays as-is — it reads _WidthHeightRadius, which both shaders share.
}
```

Keep everything below the component refresh (the `CornerBakeProp` / MaskableGraphic rebuild logic) unchanged — both shaders expose `_WidthHeightRadius`, so it applies to either.

- [ ] **Step 2: Rewrite the transparent/color/outline block**

Find the block (currently `~MessageItemView.cs:3624-3668`) that sets `bubbleBackground.color`, toggles `outline`, and manages `bubbleRounded` + `bubbleOutlineRounded`. Replace the whole `isTransparent` handling with single-component logic:

```csharp
bool isTransparent = (currentVm.isSticker && !isPlaceholderActive) || hideBubble;

bubbleBackground.color = isTransparent
    ? Color.clear
    : (currentVm.isIncoming ? incomingColor : outgoingColor);

if (!bubbleBackground.TryGetComponent<ImageWithRoundedCornersBordered>(out var bubbleBordered))
    bubbleBordered = bubbleBackground.gameObject.AddComponent<ImageWithRoundedCornersBordered>();

if (isTransparent)
{
    bubbleBordered.enabled = false;
    bubbleBackground.material = null;   // no rounded/stencil material while transparent
}
else
{
    bubbleBordered.enabled = true;
    bubbleBordered.radius = 28f;
    bubbleBordered.borderColor = new Color(0.851f, 0.831f, 0.792f, 1f);
    bubbleBordered.designBorderUnits = 1f;
    bubbleBordered.Validate();
    bubbleBordered.Refresh();
}
bubbleBordered.SetBorderVisible(!isTransparent);
```

Delete the old `outline.SetActive(...)`, the `bubbleRounded` / `bubbleOutlineRounded` (radius 28/29) block, and the `outline.GetComponent<Image>()...material` lines. Keep the `bubbleTail` block below it unchanged (the tail keeps its own sprite/outline).

- [ ] **Step 3: Remove the `outline` references and field**

- Delete the three `RefreshCorners(outline);` calls (`~:1779, :2646, :2664`).
- Delete the `MirrorSize` sync calls: the block at `~:2638-2642` (`bubbleBackground.GetComponent<MirrorSize>()... mirror.UpdateSize()`) and the same in `FinalizeCustomVisuals` (`~:1771-1775`).
- Delete the field `public GameObject outline;` (`:20`).
- Grep to confirm zero remaining `outline` and `MirrorSize` references in the file: `grep -n "\boutline\b\|MirrorSize" Assets/Scripts/UI/MessageItemView.cs` → expect no matches.

- [ ] **Step 4: Compile-check**

`execute_menu_item "Assets/Refresh"`, `get_console_logs` (error). Expected: no errors. (The prefabs still have the `Outline` child + `MirrorSize` at this point — that's fine; they're removed in Task 4. `bubbleBackground` will gain the bordered component at runtime via the code above.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "refactor(ui): draw bubble border via ImageWithRoundedCornersBordered; drop Outline/MirrorSize wiring"
```

---

### Task 4: Bubble prefabs — retire the Outline object, wire the bordered component

**Files (Editor-driven):**
- Modify: `Assets/Prefabs/MessageTextIncoming.prefab`, `Assets/Prefabs/MessageTextOutgoing.prefab`

**Interfaces:** Consumes Task 2 component + Task 3 code.

No unit test; verified by Task 6 and absence of missing-script warnings.

- [ ] **Step 1: Edit both bubble prefabs in the Editor**

For each of `MessageTextIncoming.prefab` and `MessageTextOutgoing.prefab`, open in the Prefab editor and:
1. Select the `bubbleBackground` object → **Add Component → `ImageWithRoundedCornersBordered`**; set `radius = 28`, `borderColor = (217,212,202)` i.e. `(0.851,0.831,0.792,1)`, `designBorderUnits = 1`. Remove any old `ImageWithRoundedCorners` on `bubbleBackground` (the bordered one replaces it).
2. Delete the `Outline` child GameObject (the one driven by `MirrorSize`). Keep `TailOutline`.
3. Remove the `MirrorSize` component (it lived on `bubbleBackground`, targeting the `Outline`).
4. In the root `MessageItemView` component, confirm the `outline` field is gone (Task 3 removed it — Unity drops the serialized ref).
Save the prefab.

- [ ] **Step 2: Verify no missing scripts**

`execute_menu_item "Assets/Refresh"`, `get_console_logs` (warning + error). Expected: **no** "missing script" / "referenced script … missing" on either prefab. Confirm each `bubbleBackground` has the bordered component GUID and no leftover `MirrorSize`/`Outline` GUID.

- [ ] **Step 3: Editor smoke test**

Enter Play Mode on `Main.unity`, open a chat with incoming + outgoing text bubbles and at least one media (image) bubble. Confirm each bubble shows a uniform warm-gray border with smooth corners, and a sticker shows **no** border. Exit Play Mode.

- [ ] **Step 4: Commit**

```bash
git add Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextOutgoing.prefab
git commit -m "feat(ui): bubble prefabs use bordered component; remove Outline + MirrorSize"
```

---

### Task 5: Delete `MirrorSize`

**Files:**
- Delete: `Assets/Scripts/Chat/MirrorSize.cs` (+ `.meta`)

- [ ] **Step 1: Confirm no references**

`grep -rn "MirrorSize" Assets/Scripts Assets/Prefabs Assets/Scenes` → expect **no** matches (Task 3 removed the code refs; Task 4 removed the prefab components).

- [ ] **Step 2: Delete + refresh**

```bash
git rm Assets/Scripts/Chat/MirrorSize.cs Assets/Scripts/Chat/MirrorSize.cs.meta
```
`execute_menu_item "Assets/Refresh"`, `get_console_logs` (error). Expected: no errors, no missing-script warnings.

- [ ] **Step 3: Commit**

```bash
git commit -m "chore(ui): retire MirrorSize (bubble border now intrinsic to the shader)"
```

---

### Task 6: Verify — Editor smoke + device visual pass

**Files:** none (verification only).

- [ ] **Step 1: Full EditMode suite (no regression)**

Close the Editor, run `bash Tools/run-tests-headless.sh`. Expected: PASS, no regressions vs pre-Workstream-B count (the `PixelSnap` tests still green; no test covers the shader).

- [ ] **Step 2: Device visual pass (Android build — required, GPU/mask behavior)**

On a physical device:
1. Incoming + outgoing text bubbles show a **uniform-width** border all the way around, corners smooth (not hardened).
2. **Media** (image/video) bubbles show the same border; **sticker** bubbles show **no** border; recycle a sticker slot into a text message → border present (the old "recycled sticker loses outline" bug cannot recur — border is intrinsic).
3. Border stays crisp and steady while **scrolling** the message list (no shimmer) and renders correctly clipped at the top/bottom of the `RectMask2D` viewport (no square-corner leak, no double-draw).
4. The **tail** seam (sprite `TailOutline` meeting the SDF body) is visually clean.
5. Rotate the device → borders re-render correctly.
6. **Device build (not Editor):** the bubble border renders — confirming `RoundedCornersBordered` was NOT stripped (Always Included Shaders working).

- [ ] **Step 3: Record result**

Confirm tests green + device checklist passes. This completes Workstream B.

---

## Self-Review

**Spec coverage (spec §6):**
- §6.1 vendored self-contained shader, inline SDF, copy stencil/clip, keep Maskable → Task 1 + Global Constraints. ✔
- §6.2 stroke math `interior = AntialiasedCutoff(sd + _BorderWidth)`, transparent → border alpha/width 0, recycling bug dissolves → Task 1 shader + Task 3 `SetBorderVisible(false)` on transparent. ✔
- §6.3 `ImageWithRoundedCornersBordered`, snapped `_BorderWidth` via `PixelSnap.SnapUnits`, `designBorderUnits` default 1, border color from Outline image → Task 2. ✔
- §6.4 retire Outline + MirrorSize, collapse dual block, remove `outline` field, **keep the CornerBakeProp bake fix** → Task 3 (Step 1 preserves the bake logic; Step 2/3 remove the outline). ✔
- §6.5 tail keeps `TailOutline` sprite → Tasks 4 (keep TailOutline), 6 (seam check). ✔
- §6.6 Always Included Shaders + device build verification → Task 1 Step 3, Task 6 Step 2.6. ✔
- Media + text share `bubbleBackground` → one component (Task 3/4). ✔

**Placeholder scan:** No TBD/TODO; shader + component are complete code; refactor steps show the new code and name exact removal anchors. The MessageItemView edits reference regions by line + logic because the file is 3600+ lines and lines drift — the implementer reads current regions first (stated in Task 3). ✔

**Type consistency:** `ImageWithRoundedCornersBordered` surface (`radius`, `borderColor`, `designBorderUnits`, `Validate()`, `Refresh()`, `SetBorderVisible(bool)`) is used identically in Task 3. Shader property names (`_WidthHeightRadius`, `_OuterUV`, `_BorderColor`, `_BorderWidth`) match between Task 1 and Task 2. `PixelSnap.SnapUnits(float, Canvas)` matches Workstream A. ✔

**Risk notes:**
- The shader is unproven until Task 1 Step 2/4 — that step is deliberately first so a shader problem surfaces before any MessageItemView/prefab change.
- The `RectMask2D` stencil-material-copy interaction (the reason `RefreshCorners` re-bakes) is preserved by keeping that logic and only swapping which component types it recognizes; Task 6 Step 2.3 explicitly checks masked clipping on device.
- Border-width in the shader is in **local rect units** (same space as `size`/`radius`); `PixelSnap.SnapUnits` returns canvas units == local units for these rects, so it feeds in directly.
