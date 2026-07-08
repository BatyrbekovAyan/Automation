# Pixel-Snapped Lines & SDF Bubble Borders — Design Spec

- **Date:** 2026-07-08
- **Status:** Approved (brainstorming) — ready for implementation planning
- **Author:** Ayan + Claude
- **Scope:** Fix thin dividers/lines/borders and message-bubble outlines that render with inconsistent thickness across devices (and visibly "breathe" while scrolling).

---

## 1. Problem

UI is authored in **1080×1920 reference units**. At runtime the `CanvasScaler` multiplies every size by `canvas.rootCanvas.scaleFactor`, which is device-dependent (most phones are ~2.5–3.5×). A line authored as 1–2 reference units therefore becomes a **fractional number of physical pixels** (e.g. `2 units × 1.33 = 2.66 px`).

The GPU cannot light up a fractional number of pixel rows, so it anti-aliases the coverage across whole rows at partial opacity. The visible result depends on the line's sub-pixel offset, producing two symptoms with one root cause:

1. **Motion shimmer** — in a scrolling list the line's position is continuous, so its sub-pixel offset sweeps through every phase and the line visibly breathes between thin-sharp and thick-faint.
2. **Static inconsistency** — the "same" 1 px line looks 1 px crisp on one device and 2 px blurry on another; two identical lines on one screen can differ if they sit at different offsets.

Today only **one** object is corrected: the chat-list row `Divider` in `Assets/Prefabs/ChatItem.prefab`, via `Assets/Scripts/Chat/NativeHairline.cs`, which forces exactly 1 physical pixel (`1f / canvas.scaleFactor`). Every other divider, line, border, and the message-bubble outline is uncorrected.

## 2. Root-cause fix (the one idea)

Express every thin thickness in **whole physical pixels, derived from the root canvas scale factor**. A whole-pixel count cannot straddle rows, so it neither shimmers in motion nor varies across devices.

## 3. Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Thickness policy** | **Nearest whole physical pixel** (preserve designed weight), not force-to-1 | Keeps each line's designed visual weight (a 1-unit line stays ~its current weight, e.g. 3 px on a 3× phone) while guaranteeing a whole-pixel count. Consequence: the chat-list divider moves from its current forced-1 px to its designed ~3 px weight on high-DPI phones. |
| **Divider breadth** | **All thin-line sites app-wide** (~40 objects) | One reusable component; uniform crispness everywhere, static chrome included. |
| **Bubble outline depth** | **Real SDF stroke in the shader** | Uniform width including corners + self-anti-aliased, which the two-stacked-rounded-rects fake stroke cannot achieve. Higher effort accepted. |
| **Bubble tail** | **Keep existing `TailOutline` sprite** for v1 | SDF rounded-rect doesn't describe the triangle tail; the seam is tiny/invisible. Baking the tail into the SDF is a documented future option. |
| **`NativeHairline`** | **Retire and replace** | Single consumer; generalized by `PixelSnapLine`. |

## 4. Shared foundation

A pure static helper used by **both** workstreams so lines and bubble borders snap identically:

```csharp
// Assets/Scripts/Chat/PixelSnap.cs  (Assembly-CSharp)
public static class PixelSnap
{
    /// designUnits (canvas units) -> nearest whole physical pixel -> back to canvas units (min 1px).
    public static float SnapUnits(float designUnits, Canvas canvas)
    {
        if (canvas == null) return designUnits;
        float sf = canvas.rootCanvas.scaleFactor;      // physical px per canvas unit
        if (sf <= 0f) return designUnits;
        float px = Mathf.Max(1f, Mathf.Round(designUnits * sf));
        return px / sf;
    }
}
```

- **`rootCanvas.scaleFactor`**, not the nearest parent canvas — nested canvases report 1.0 and would defeat the snap.
- `Round` (not force-to-1) implements the "preserve designed weight" decision. `Mathf.Max(1f, …)` guarantees a line never vanishes.
- Pure and deterministic → directly unit-testable.

## 5. Workstream A — dividers / lines / borders

### 5.1 `PixelSnapLine` component

Generalizes `NativeHairline` (which only forced 1 px on the vertical axis).

```csharp
// Assets/Scripts/Chat/PixelSnapLine.cs
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class PixelSnapLine : MonoBehaviour
{
    public enum SnapAxis { Height, Width }   // horizontal rule vs vertical rule

    [SerializeField] private SnapAxis axis = SnapAxis.Height;
    [SerializeField] private float designThicknessUnits = 1f;   // authored weight (1,2,3…)

    private RectTransform rect;
    private Canvas canvas;

    void OnEnable()  { Cache(); Apply(); }
    void OnRectTransformDimensionsChange() { Apply(); }   // screen rotate/resize

    void Cache() { rect = (RectTransform)transform; canvas = GetComponentInParent<Canvas>(); }

    void Apply()
    {
        if (rect == null || canvas == null) return;
        float snapped = PixelSnap.SnapUnits(designThicknessUnits, canvas);
        var uAxis = axis == SnapAxis.Height ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal;
        // Epsilon guard: only resize when the value actually changes, so we don't
        // re-dirty layout on every rebuild pass (matters at ~40 instances, some in
        // scrolling lists). SnapUnits is idempotent, so this converges after one pass.
        float current = uAxis == RectTransform.Axis.Vertical ? rect.rect.height : rect.rect.width;
        if (Mathf.Abs(current - snapped) < 0.01f) return;
        rect.SetSizeWithCurrentAnchors(uAxis, snapped);
    }
}
```

- `designThicknessUnits` is **stored explicitly**, never read from the live size — reading the live size would re-snap the component's own output and drift. The wiring tool stamps it from each object's authored size.
- Covers horizontal dividers (snap height) **and** vertical rules like `LeftLine` / `RightLine` (snap width) — the gap `NativeHairline` could not cover.
- The **epsilon guard** skips the resize when the snapped value already matches the current size, so the ~40 instances never re-dirty layout on an unrelated rebuild pass (see §7).

### 5.2 Retire `NativeHairline`

`NativeHairline` has exactly one consumer (`ChatItem.prefab`). Migrate that `Divider` to `PixelSnapLine` (axis = Height, designThickness = its authored 1 unit), then delete `Assets/Scripts/Chat/NativeHairline.cs` (+ `.meta`). Per the thickness decision, the chat-list divider now renders its designed weight, crisp and consistent with every other line.

### 5.3 Wiring tool — `Tools/Pixel Snap/Wire All`

A `[MenuItem]` editor script under `Assets/Editor/` (following the project's builder pattern), Edit-Mode only, idempotent, saves scene + prefabs (per the project's builder-save convention). It:

1. Scans the scene and prefabs for line objects: `Divider`, `Separator`, `Line*`, `LeftLine` / `RightLine`, and prefab dividers (`Bot.prefab`, `BotSettings.prefab`, `DateSeparator.prefab`, `UnreadSeparator.prefab`, `ChatItem.prefab`).
2. **Classifies by aspect**: thin-height → `SnapAxis.Height`; thin-width → `SnapAxis.Width`. Adds `PixelSnapLine` and stamps `designThicknessUnits` from the current authored size on the thin axis (via `SerializedObject`).
3. **Flags — does not modify — ambiguous objects.** The 13 `Border` objects are not uniformly simple 1 px lines; some are rounded frames or use Unity's `Outline` effect component. The tool emits a console report listing each flagged object + its structure so we make an explicit per-object decision during planning (leave as-is, treat as a line, or route to the Workstream-B SDF treatment) rather than mis-snapping it.
4. Re-running updates existing components in place (idempotent).

### 5.4 Workstream-A file changes

| File | Change |
|---|---|
| `Assets/Scripts/Chat/PixelSnap.cs` | **New** — shared snap helper |
| `Assets/Scripts/Chat/PixelSnapLine.cs` | **New** — component |
| `Assets/Scripts/Chat/NativeHairline.cs` | **Delete** (+ `.meta`) |
| `Assets/Prefabs/ChatItem.prefab` | Swap `NativeHairline` → `PixelSnapLine` on `Divider` |
| `Assets/Editor/PixelSnapWirer.cs` | **New** — `Tools/Pixel Snap/Wire All` |
| `Assets/Scenes/Main.unity`, prefab dividers | `PixelSnapLine` added by the wiring tool |

## 6. Workstream B — bubble SDF stroke

### 6.1 Why a new shader, not an edit

The Nobi package lives in `Library/PackageCache/com.nobi.roundedcorners@…`, installed from a git URL and regenerated by Unity — edits there are lost and are not in our repo. The bordered variant is therefore **vendored into `Assets/`** and made **self-contained**: the SDF helpers we need (`rectangle`, `roundedRectangle`, `AntialiasedCutoff`) are **inlined** rather than `#include`-ing package paths that can move on a package update. The **stencil + `UNITY_UI_CLIP_RECT` boilerplate** from `RoundedCorners.shader` is copied verbatim — bubbles render inside a `RectMask2D`, so mask/clip support is mandatory, and the bordered Image must keep `Maskable = true`.

### 6.2 Stroke math

The existing fragment computes fill only. A uniform inner border of width `w` (local rect units) is one extra offset cutoff:

```
sd        = roundedRectangle(pos, radius*.5, size*.5);   // signed distance, <0 inside (unchanged)
fillAlpha = AntialiasedCutoff(sd);                        // outer AA edge (unchanged)
interior  = AntialiasedCutoff(sd + _BorderWidth);         // 1 deep inside, AA at the inner border edge
rgb       = lerp(_BorderColor.rgb, fillColor.rgb, interior);
outAlpha  = min(lerp(_BorderColor.a, fillColor.a, interior), fillAlpha);
```

- Border is **uniform width around the whole perimeter including corners** (constant SDF offset) and **self-anti-aliased on both edges** — the property the fake stroke lacked.
- `_BorderWidth == 0` collapses to a plain rounded fill (backward compatible).
- Transparent bubble (sticker / hidden) → fill and border alpha both 0. Because the border is intrinsic to the bubble draw, there is **no separate object to toggle** and the latent "recycled sticker bubble loses its outline" bug (the commented-out `outline.SetActive(true)` at `MessageItemView.cs:3641`) **cannot occur**.

### 6.3 `ImageWithRoundedCornersBordered` component

A sibling of `ImageWithRoundedCorners` with the same lifecycle (`OnEnable`, `OnRectTransformDimensionsChange`), living in `Assembly-CSharp` (`Assets/Scripts/`). It writes `_WidthHeightRadius` (w, h, radius*2, 0), `_OuterUV`, `_BorderColor`, and a **snapped** `_BorderWidth`:

```csharp
_BorderWidth = PixelSnap.SnapUnits(designBorderUnits, GetComponentInParent<Canvas>());
```

- **Same nearest-whole-pixel snap as the dividers** — SDF gives uniform + crisp corners; `PixelSnap` gives a device-consistent whole-pixel thickness.
- `designBorderUnits` defaults to **1** (today's `extraSize: 2` ÷ 2 sides). Border width is in local rect units — the same space the SDF already uses — so it feeds straight in.
- `_BorderColor` is sourced from the current `Outline` image color (the existing warm-gray border color), surfaced as a serialized field / constant on the component or `MessageItemView`.

### 6.4 Retirements & `MessageItemView` refactor

- Delete the separate **`Outline` child** and its `MirrorSize` (`Assets/Scripts/Chat/MirrorSize.cs`) from both bubble prefabs. `bubbleBackground` swaps `ImageWithRoundedCorners` → `ImageWithRoundedCornersBordered`. Net **−1 draw/material per bubble** (a small batching win).
- In `Assets/Scripts/UI/MessageItemView.cs`:
  - The dual `bubbleRounded` + `bubbleOutlineRounded` block (`3644–3668`) collapses to one bordered component (set radius, borderColor, snapped borderWidth, enabled).
  - `RefreshCorners(outline)` and `MirrorSize.UpdateSize()` in `FinalizeCustomVisuals` (`1769–1780`) drop out.
  - The `outline` serialized field (`line 20`) is removed; rewire via `SerializedObject` so no dangling reference remains (per the "builders must rewire consumers" rule).
  - The `CornerBakeProp` 0-width-bake fix (`2667–2683`) is **kept**, retargeted to the bordered component — that first-frame race is still real.
- **`MirrorSize`** has no other consumer once the bubble outline is gone; delete it (+ `.meta`) after confirming no remaining references.
- Text and media bubbles share `bubbleBackground`, so a single component covers both — no separate media path.

### 6.5 The tail seam

The SDF stroke covers the rounded-rect **body** only. The **tail** is a separate triangle sprite with its own `TailOutline` sprite. Plan: **keep `TailOutline`** and match its color/width to `_BorderColor`. The seam (straight sprite edge meeting the SDF body) is tiny and, in practice, invisible. If device testing disagrees, a follow-up can union a triangle SDF into the shader — **out of scope for v1**.

### 6.6 Build inclusion (gotcha)

A vendored shader used via `Shader.Find(...)` at runtime is **stripped from device builds unless referenced**. Ensure inclusion via **Project Settings → Graphics → Always Included Shaders** (add the bordered shader), or ship a Material asset in the project that references it. Verify on an actual device build, not just the Editor. (Related: `Nobi.UiRoundedCorners` lives in its own assembly — keep the new component in `Assembly-CSharp` and reference the shader by string name, never `Type.GetType(..., Assembly-CSharp)`.)

### 6.7 Workstream-B file changes

| File | Change |
|---|---|
| `Assets/Shaders/RoundedCornersBordered.shader` | **New** — self-contained SDF fill + border shader |
| `Assets/Scripts/Chat/ImageWithRoundedCornersBordered.cs` | **New** — component (writes color + snapped border width) |
| `Assets/Prefabs/MessageTextIncoming.prefab` | Remove `Outline` + `MirrorSize`; bordered component on `bubbleBackground` |
| `Assets/Prefabs/MessageTextOutgoing.prefab` | Same |
| `Assets/Scripts/UI/MessageItemView.cs` | Collapse outline handling to one component; remove `outline` field; keep bake fix |
| `Assets/Scripts/Chat/MirrorSize.cs` | **Delete** (+ `.meta`) after reference check |
| Project Settings → Graphics | Add bordered shader to Always Included |

## 7. Performance

Net impact: **Workstream A is negligible; Workstream B is a modest improvement.** No change lowers frame rate; the bubble change lowers per-bubble cost.

### Workstream A (dividers)

- `PixelSnapLine.Apply()` is a multiply-round-divide plus one `SetSizeWithCurrentAnchors`, run on `OnEnable` and `OnRectTransformDimensionsChange`. No allocations (the snap helper returns a float), no new draw calls, no new materials.
- The **epsilon guard** (§5.1) makes `Apply()` skip the resize when the snapped value already matches the current size, so the ~40 instances never re-dirty layout on a rebuild pass. Static chrome lines settle after startup; scrolling-list lines re-apply once on recycle.
- Effectively free.

### Workstream B (bubble SDF stroke) — per bubble, confirmed from code

| Metric | Today (2 stacked rounded rects) | After (SDF border) |
|---|---|---|
| Rounded-rect draw calls | 2 (`bubbleBackground` + `Outline`) | **1** |
| Material instances | 2 (each component `new Material(...)`) | **1** |
| GameObjects | + `Outline` (RectTransform + Image + `MirrorSize`) | **removed** |
| Overdraw over bubble area | ~2× (full Outline rect behind the bubble) | **~1×** |
| Layout-pass work | `MirrorSize.Sync()` every rebuild | **removed** |
| Fragment shader | fill only | fill + one extra `AntialiasedCutoff` + `lerp` |

- The trade is **one draw call + one material + one GameObject + a full overdraw layer** for **a few fragment ALU ops** — favorable on mobile, where draw calls and fill-rate dominate over fragment math. With 15–25 visible bubbles that is 15–25 fewer draw calls/materials on the chat screen, plus removing `MirrorSize`'s per-layout work (helps scroll) and roughly halving bubble overdraw.
- GC pressure drops slightly: 1 material allocation per bubble instead of 2 on spawn/recycle.

### Not claimed / watch items

- **Batching is unchanged.** Bubbles use per-instance materials before and after (per-rect `_WidthHeightRadius` bake) → still no bubble-to-bubble batching. No regression, no improvement. `MaterialPropertyBlock` for SRP batching is a possible future optimization, out of scope.
- **Fragment cost is higher per-pixel**; for full-width media bubbles that is more pixels, but the removed overdraw dominates.
- One extra small shader in the build; same `UNITY_UI_CLIP_RECT` / `UNITY_UI_ALPHACLIP` variants as the base — trivial size/variant impact.
- The wiring tool runs at author-time in the Editor — **zero runtime cost**.

## 8. Edge cases

- **Zero / degenerate rect** during first layout pass → snap helper returns min 1 px; shader `_BorderWidth` clamps to a whole pixel; the existing 0-width bake guard still applies.
- **Sticker / transparent bubble** → border and fill alpha 0 (Section 6.2); no separate object to desync.
- **Screen rotation / resize** → `OnRectTransformDimensionsChange` re-applies on both the line component and the bordered component.
- **Editor vs device** → `[ExecuteAlways]` keeps lines correct in the Editor; the shader border is GPU-only and must be verified on device.
- **Very high scale factors** (foldables / tablets) → `Round` keeps proportional weight; no special-casing needed.
- **`Border` frames & `Outline`-effect objects** → surfaced by the wiring tool for an explicit decision; not auto-snapped.

## 9. Testing

- **Unit (EditMode, `Assets/Tests/Editor/Chat/`):**
  - `PixelSnap.SnapUnits` across scaleFactors `{1.0, 1.33, 2.0, 2.625, 3.0}` × designUnits `{1, 2, 3}` → asserts whole-pixel physical result and `≥ 1 px`.
  - `PixelSnapLine` axis logic → given a stubbed canvas scaleFactor, asserts the correct `RectTransform.Axis` is sized to the snapped value.
- **Editor visual smoke** → run the wiring tool on a scratch copy, confirm components + `designThicknessUnits` are stamped and the pass is idempotent (second run = no diff).
- **Device verification (manual checklist, required — shader/GPU can't be unit-tested):**
  1. Scroll the chat list and message list → dividers hold steady thickness, no breathing.
  2. Bubble border reads uniform width around the whole perimeter, corners crisp.
  3. Incoming vs outgoing bubbles both correct; media bubbles bordered.
  4. Sticker / transparent bubble → no border artifact; recycle a sticker slot into a text message → border present.
  5. Tail seam invisible.
  6. Rotate device / resize → everything re-snaps.
  7. **Device build** (not Editor) → bordered shader present (not stripped).
- Run EditMode via the project test bridge (Editor open: `Temp/claude/run-tests.trigger`; closed: `Tools/run-tests-headless.sh`). Gate completion on green tests **and** a device pass (per the project execution-loop rule).

## 10. Rollout / phasing

Workstreams A and B are independent and share only `PixelSnap`. Ship in two phases:

- **Phase 1 — Workstream A (dividers):** `PixelSnap` + `PixelSnapLine` + retire `NativeHairline` + wiring tool + unit tests. Lower risk, no shader/build changes. Review the `Border`-frame report and decide each.
- **Phase 2 — Workstream B (bubble SDF stroke):** bordered shader + component + `MessageItemView` refactor + prefab retirements + Always-Included shader + device pass.

Each phase: implement → EditMode green via bridge → device pass → commit (staging `.cs` + `.meta`), with commit/push per the project's per-task consent rule.

## 11. Out of scope (v1)

- Sub-pixel **position** snapping (residual softness of a line's edge while in motion). Thickness snapping is the requested and primary fix; position snapping is a separate, harder effort (touches scroll content transforms / CanvasScaler pixel-perfect) and is deferred.
- Baking the bubble **tail** into the SDF shader.
- Any redesign of line **color** or visual weight beyond making the existing weight crisp.

## 12. Risks & open items

- **`Border`/`Line` classification** — exact per-object handling is resolved during planning from the wiring tool's report, not assumed here.
- **Shader stripping** — must be verified on a real device build (Section 6.6).
- **`MirrorSize` deletion** — gated on confirming zero remaining references outside the bubble outline.
- **Chat-list divider weight change** — intentional (forced-1 px → designed ~3 px on high-DPI); confirm it looks right on device.
