# Image OCR for Price-List Uploads — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Owners upload photos of menus/price boards (jpg/png/webp/heic); a vision branch in the n8n Upload File workflow extracts entity-labeled price text into the existing RAG pipeline, with a 422 fail-with-reason path for unreadable photos.

**Architecture:** Client decodes any image via NativeGallery native decode, downscales to 2048px, re-encodes JPEG, and uploads through the existing `UploadFile` coroutine. The workflow routes `jpg` to an OpenAI vision node (gpt-4o-mini) whose prompt emits the same `{contentType}[N]: Название: …; Цена: …;` lines the other converters produce; empty/`NO_PRICE_DATA` output responds 422 and the app shows a no-retry Russian failure reason. Spec: `docs/superpowers/specs/2026-07-03-image-ocr-price-lists-design.md`.

**Tech Stack:** Unity 6 C# (coroutines, NativeGallery, NativeFilePicker), n8n (local dev at localhost:5678 via n8n-mcp; canonical JSONs in `Tools/n8n/workflows/`), OpenAI vision, Python 3 migration scripts, NUnit EditMode tests.

## Global Constraints

- Rollout order: **workflow first (Tasks 1–4), client second (Tasks 5–7)** — the client branch must stay unreachable until the workflow handles images.
- Client always uploads `image/jpeg` with a `.jpg`-final filename; MaxDimension **2048**, JPEG quality **85**.
- Vision model pinned: **gpt-4o-mini**, temperature 0; no-data marker is exactly `NO_PRICE_DATA`; error response is HTTP **422** body `{"success": false, "error": "no_price_data"}`.
- Canonical workflow JSONs are the source of truth: migrations are idempotent Python scripts editing by **node name** (never index), `indent=2, ensure_ascii=False`, verified by `Tools/n8n/verify_rag.py`.
- Unity: run EditMode tests after every code task (mcp-unity `run_tests` with EXACT class-name filter, or the test bridge); new .cs files need Assets/Refresh before they exist to the compiler; stage `.cs` + `.meta` together.
- Every user-facing string is Russian; failure reasons go through `UploadFailureText` (Assets/Scripts/Main/).
- Commits: one per task, message style `feat(scope): …`, ending with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- `Assets/Scripts/Main/BotSettings.Auth.cs` has recent uncommitted refactors in the working tree — always re-read the current file before editing; line numbers in this plan are approximate.

---

### Task 1: Image test fixtures

**Files:**
- Create: `Tools/n8n/fixtures/price-fixture.jpg` (white 800×600, black text: `Чай 5000 тг`, `Кофе 7000 тг`)
- Create: `Tools/n8n/fixtures/blank.jpg` (pure white 800×600)
- Create: `Tools/n8n/fixtures/make_fixtures.py` (regeneration script, committed for reproducibility)

**Interfaces:**
- Produces: fixture paths consumed by Task 3's e2e cases.

- [ ] **Step 1: Ensure Pillow is available**

Run: `python3 -c "import PIL" 2>/dev/null || pip3 install --user pillow`

- [ ] **Step 2: Write the generator**

```python
#!/usr/bin/env python3
"""Regenerate the e2e image fixtures. Committed so fixtures are reproducible."""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))

def font(size):
    for path in ("/System/Library/Fonts/Supplemental/Arial.ttf",
                 "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()

img = Image.new("RGB", (800, 600), "white")
d = ImageDraw.Draw(img)
d.text((60, 80), "ПРАЙС-ЛИСТ", font=font(48), fill="black")
d.text((60, 220), "Чай 5000 тг", font=font(56), fill="black")
d.text((60, 340), "Кофе 7000 тг", font=font(56), fill="black")
img.save(os.path.join(HERE, "price-fixture.jpg"), quality=90)

Image.new("RGB", (800, 600), "white").save(os.path.join(HERE, "blank.jpg"), quality=90)
print("fixtures written")
```

- [ ] **Step 3: Generate and eyeball**

Run: `mkdir -p Tools/n8n/fixtures && python3 Tools/n8n/fixtures/make_fixtures.py && ls -la Tools/n8n/fixtures/`
Expected: both .jpg files exist, price-fixture.jpg ≈ 20–60 KB. Open price-fixture.jpg (`open Tools/n8n/fixtures/price-fixture.jpg`) — text must be crisp and readable.

- [ ] **Step 4: Commit**

```bash
git add Tools/n8n/fixtures/
git commit -m "test(n8n): image OCR e2e fixtures (text-bearing + blank JPEG)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Workflow migration script + invariants

**Files:**
- Create: `Tools/n8n/apply-image-ocr.py`
- Modify: `Tools/n8n/verify_rag.py` (add `check_image_ocr()`, wire into `all`)
- Modify (by running the script): `Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json`

**Interfaces:**
- Consumes: canonical Upload File JSON node names — `Switch`, `Merge`, `Store Original File`, `Extract Content Type`, `Embeddings OpenAI` (source of the `openAiApi` credential ref), `Respond Unsupported Type`.
- Produces: new nodes named exactly `Extract Price Text From Image` (openAi vision), `Has Price Data` (if), `Image Text` (set), `Respond No Price Data` (respondToWebhook 422). Task 4 mirrors these names onto the live instance; `verify_rag.py image` asserts them.

- [ ] **Step 1: Verify the OpenAI node's exact parameter names**

Before writing node params, on the live dev instance run n8n-mcp `get_node_types` with `[{"nodeId": "@n8n/n8n-nodes-langchain.openAi", "resource": "image", "operation": "analyze"}]`. Confirm the parameter names used below (`modelId`, `text`, `inputType`, `binaryPropertyName`, `options.maxTokens`, `options.detail`) and the current typeVersion; adjust the script's `VISION_PARAMS` if they differ. The spec pins temperature 0 — if the analyze-image options expose a temperature field, add `"temperature": 0` to `VISION_PARAMS["options"]`; if not, omit it (the prompt's "ничего не придумывай" instruction plus extraction-style output keeps determinism adequate) and note the omission in the task report.

- [ ] **Step 2: Write `Tools/n8n/apply-image-ocr.py`**

```python
#!/usr/bin/env python3
"""Idempotent migration: image OCR branch in Upload File (2026-07-03 spec).

Adds a Switch rule for jpg/jpeg/png/webp -> OpenAI vision (gpt-4o-mini) ->
IF gate (NO_PRICE_DATA/empty -> 422) -> Image Text -> Merge(0). The image
branch also fans into Store Original File (photos archived for future
re-OCR). Edits by node name, indent=2/ensure_ascii=False, re-runnable.
"""
import json, os, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
UPLOAD = "KoTuIlk4LMrlvnWI-Upload_File.json"

EXT_EXPR = "={{ $('Set File Id').item.binary.data.fileExtension.toLowerCase() }}"
IMAGE_EXTS = ["jpg", "jpeg", "png", "webp"]

PROMPT = (
    "Ты извлекаешь прайс-лист из фотографии (меню, ценник, прайс-борд, витрина).\n"
    "Выпиши КАЖДУЮ позицию с ценой, по одной на строку, ровно в формате:\n"
    "{{ $('Extract Content Type').item.json.contentType }}[N]: Название: <название>; Цена: <число и валюта как на фото>;\n"
    "где N — порядковый номер, начиная с 1. Заголовки разделов (например «Напитки») "
    "выписывай отдельной строкой без номера. Ничего не придумывай: только то, что "
    "читается на фото. Если на фото нет ни одной читаемой цены, выведи ровно: NO_PRICE_DATA"
)

VISION_PARAMS = {
    "resource": "image",
    "operation": "analyze",
    "modelId": {"__rl": True, "value": "gpt-4o-mini", "mode": "list", "cachedResultName": "gpt-4o-mini"},
    "text": "=" + PROMPT,
    "inputType": "base64",
    "binaryPropertyName": "data",
    "simplify": True,
    "options": {"detail": "high", "maxTokens": 4000},
}


def load():
    with open(os.path.join(WF, UPLOAD), encoding="utf-8") as f:
        return json.load(f)


def save(wf):
    with open(os.path.join(WF, UPLOAD), "w", encoding="utf-8") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)


def find(nodes, name):
    for n in nodes:
        if n["name"] == name:
            return n
    return None


def node_id(wf, tag):
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-" + tag))


def add_switch_rule(wf):
    rules = find(wf["nodes"], "Switch")["parameters"]["rules"]["values"]
    if any(r.get("outputKey") == "image" for r in rules):
        return False
    rules.append({
        "conditions": {
            "options": {"caseSensitive": True, "leftValue": "", "typeValidation": "strict", "version": 2},
            "conditions": [
                {"id": node_id(wf, "cond-" + ext), "leftValue": EXT_EXPR, "rightValue": ext,
                 "operator": {"type": "string", "operation": "equals"}}
                for ext in IMAGE_EXTS
            ],
            "combinator": "or",
        },
        "renameOutput": True,
        "outputKey": "image",
    })
    return True


def add_nodes(wf):
    nodes = wf["nodes"]
    openai_cred = find(nodes, "Embeddings OpenAI")["credentials"]["openAiApi"]
    if find(nodes, "Extract Price Text From Image") is None:
        nodes.append({
            "parameters": dict(VISION_PARAMS),
            "type": "@n8n/n8n-nodes-langchain.openAi",
            "typeVersion": 1.8,
            "position": [1100, -224],
            "id": node_id(wf, "vision"),
            "name": "Extract Price Text From Image",
            "credentials": {"openAiApi": dict(openai_cred)},
        })
    if find(nodes, "Has Price Data") is None:
        nodes.append({
            "parameters": {
                "conditions": {
                    "options": {"caseSensitive": True, "leftValue": "", "typeValidation": "loose", "version": 2},
                    "conditions": [
                        {"id": node_id(wf, "cond-marker"), "leftValue": "={{ $json.content }}",
                         "rightValue": "NO_PRICE_DATA",
                         "operator": {"type": "string", "operation": "notContains"}},
                        {"id": node_id(wf, "cond-notempty"), "leftValue": "={{ $json.content }}",
                         "operator": {"type": "string", "operation": "notEmpty",
                                       "singleValue": True}},
                    ],
                    "combinator": "and",
                },
                "options": {},
            },
            "type": "n8n-nodes-base.if",
            "typeVersion": 2.2,
            "position": [1400, -224],
            "id": node_id(wf, "gate"),
            "name": "Has Price Data",
        })
    if find(nodes, "Image Text") is None:
        nodes.append({
            "parameters": {
                "assignments": {"assignments": [
                    {"id": node_id(wf, "assign-text"), "name": "text",
                     "value": "={{ $json.content }}", "type": "string"},
                ]},
                "options": {},
            },
            "type": "n8n-nodes-base.set",
            "typeVersion": 3.4,
            "position": [1700, -288],
            "id": node_id(wf, "imagetext"),
            "name": "Image Text",
        })
    if find(nodes, "Respond No Price Data") is None:
        nodes.append({
            "parameters": {
                "respondWith": "json",
                "responseBody": "={{ { \"success\": false, \"error\": \"no_price_data\" } }}",
                "options": {"responseCode": 422},
            },
            "type": "n8n-nodes-base.respondToWebhook",
            "typeVersion": 1.4,
            "position": [1700, -128],
            "id": node_id(wf, "respond422"),
            "name": "Respond No Price Data",
        })


def wire(wf):
    conns = wf["connections"]
    switch = conns["Switch"]["main"]
    # Rule order after add_switch_rule: 0 txt, 1 pdf, 2 image, 3 fallback.
    # The fallback links were at index 2 before this migration — move them.
    image_links = [
        {"node": "Extract Price Text From Image", "type": "main", "index": 0},
        {"node": "Store Original File", "type": "main", "index": 0},
    ]
    if len(switch) == 3:  # not yet migrated
        switch.insert(2, image_links)
    elif not any(l["node"] == "Extract Price Text From Image" for l in switch[2]):
        switch[2] = image_links
    conns["Extract Price Text From Image"] = {"main": [[{"node": "Has Price Data", "type": "main", "index": 0}]]}
    conns["Has Price Data"] = {"main": [
        [{"node": "Image Text", "type": "main", "index": 0}],
        [{"node": "Respond No Price Data", "type": "main", "index": 0}],
    ]}
    conns["Image Text"] = {"main": [[{"node": "Merge", "type": "main", "index": 0}]]}


def main():
    wf = load()
    add_switch_rule(wf)
    add_nodes(wf)
    wire(wf)
    save(wf)
    print(f"  fixed {UPLOAD}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Add `check_image_ocr()` to `Tools/n8n/verify_rag.py`**

Append before the `__main__` block, and add `if which in ("image", "all"): check_image_ocr()` to it:

```python
def check_image_ocr():
    wf = load(UPLOAD); ns = wf["nodes"]; conns = wf["connections"]
    sw = find(ns, name="Switch")
    rules = sw["parameters"]["rules"]["values"]
    image = [r for r in rules if r.get("outputKey") == "image"]
    assert len(image) == 1, "Switch has no image rule"
    exts = {c["rightValue"] for c in image[0]["conditions"]["conditions"]}
    assert exts == {"jpg", "jpeg", "png", "webp"}, f"image rule exts wrong: {exts}"
    assert image[0]["conditions"]["combinator"] == "or", "image rule must OR its extensions"
    for c in image[0]["conditions"]["conditions"]:
        assert ".toLowerCase()" in c["leftValue"], "image rule must be case-insensitive"

    vision = find(ns, name="Extract Price Text From Image")
    assert vision is not None, "vision node missing"
    assert "gpt-4o-mini" in json.dumps(vision["parameters"].get("modelId", "")), "vision model not pinned"
    assert "NO_PRICE_DATA" in vision["parameters"]["text"], "prompt missing the no-data marker"
    assert "contentType" in vision["parameters"]["text"], "prompt missing entity token"

    branches = conns["Switch"]["main"]
    assert len(branches) == 4, f"Switch must have 4 outputs, got {len(branches)}"
    img_targets = {l["node"] for l in branches[2]}
    assert img_targets == {"Extract Price Text From Image", "Store Original File"}, \
        f"image branch targets wrong: {img_targets}"
    assert branches[3][0]["node"] == "Respond Unsupported Type", "fallback lost its 415 wiring"

    gate = find(ns, name="Has Price Data")
    assert gate is not None, "Has Price Data gate missing"
    assert conns["Has Price Data"]["main"][0][0]["node"] == "Image Text", "gate true branch wrong"
    assert conns["Has Price Data"]["main"][1][0]["node"] == "Respond No Price Data", "gate false branch wrong"
    assert conns["Image Text"]["main"][0][0]["node"] == "Merge", "Image Text must feed Merge(0)"
    assert conns["Image Text"]["main"][0][0]["index"] == 0, "Image Text must feed Merge INPUT 0"

    r422 = find(ns, name="Respond No Price Data")
    assert r422["parameters"]["options"].get("responseCode") == 422, "no-data response not 422"
    assert "no_price_data" in r422["parameters"]["responseBody"], "no-data response missing error code"
```

- [ ] **Step 4: Run migration + verify + idempotency**

Run: `python3 Tools/n8n/apply-image-ocr.py && python3 Tools/n8n/verify_rag.py all && python3 Tools/n8n/apply-image-ocr.py && python3 Tools/n8n/verify_rag.py all && git diff --stat Tools/n8n/workflows/`
Expected: `VERIFY OK: all` twice (all pre-existing checks must still pass — especially `check_upload_response`, whose Switch-branch indexes shift); only `KoTuIlk4LMrlvnWI-Upload_File.json` changed. Note: `check_upload_response` asserts `branches[2][0]["node"] == "Respond Unsupported Type"` — update that assertion to `branches[-1][0]` as part of this step (fallback is now index 3).

- [ ] **Step 5: Commit**

```bash
git add Tools/n8n/apply-image-ocr.py Tools/n8n/verify_rag.py Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json
git commit -m "feat(n8n): image OCR branch in Upload File — vision extraction + 422 no_price_data gate

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: E2E image cases

**Files:**
- Modify: `Tools/n8n/test-upload-e2e.sh`

**Interfaces:**
- Consumes: Task 1 fixtures; existing `upload`/`check`/`delete_file` shell functions.
- Produces: cases 5–7 run by Task 4 against the live instance.

- [ ] **Step 1: Add image cases before the final summary block**

```bash
FIXTURES="$(cd "$(dirname "$0")/fixtures" && pwd)"
FILE_ID_IMG="e2e-${STAMP}-img"

echo "== 5. text-bearing photo → 200 + ingested =="
code="$(upload "$FILE_ID_IMG" "$FIXTURES/price-fixture.jpg" "menu.jpg" "image/jpeg")"
check "image upload" 200 "$code" '"success":true'

echo "== 6. blank photo → 422 no_price_data =="
code="$(upload "e2e-${STAMP}-blank" "$FIXTURES/blank.jpg" "blank.jpg" "image/jpeg")"
check "blank photo" 422 "$code" '"error":"no_price_data"'

echo "== 7. image chunks + stored original delete =="
code="$(delete_file "$FILE_ID_IMG")"
check "delete image" 200 "$code" '"deletedChunks"'
```

(Case 7 asserts only that `deletedChunks` is present — vision output length varies, so the chunk count is not pinned. Vision adds ~5–15 s to case 5; no timeout flags needed, curl default is fine.)

- [ ] **Step 2: Syntax-check**

Run: `bash -n Tools/n8n/test-upload-e2e.sh`
Expected: no output. (Do NOT run the suite yet — the live instance gets the branch in Task 4.)

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/test-upload-e2e.sh
git commit -m "test(n8n): e2e image cases — vision 200, blank 422, image delete

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Apply to live dev n8n, publish, run e2e

**Files:** none (live instance state + execution evidence)

**Interfaces:**
- Consumes: Task 2 node definitions (names/params must match the canonical JSON exactly).
- Produces: a published Upload File workflow that Tasks 5–7's client work can be tested against.

- [ ] **Step 1: Mirror the migration onto the live instance via n8n-mcp `update_workflow`** (workflowId `KoTuIlk4LMrlvnWI`)

Operations, in one call: `setNodeParameter` on `Switch` path `/rules` with the full migrated rules object from the canonical JSON; `addNode` for each of the four new nodes (copy `parameters`/`type`/`typeVersion`/`position` verbatim from the canonical JSON, WITHOUT the credentials field — the MCP validator may reject predefined credentials, same as the supabaseApi nodes); `addConnection` ops: Switch(sourceIndex 2)→Extract Price Text From Image, Switch(sourceIndex 2)→Store Original File, Extract Price Text From Image→Has Price Data, Has Price Data(sourceIndex 0)→Image Text, Has Price Data(sourceIndex 1)→Respond No Price Data, Image Text→Merge(targetIndex 0). **Check first** whether the Switch rules replacement already rewires the fallback: after the call, `get_workflow_details` must show Switch main[3] → Respond Unsupported Type; if the fallback links landed on the wrong index, fix with removeConnection/addConnection ops.

- [ ] **Step 2: Bind the OpenAI credential (user action)**

Ask the user to open Upload File in the n8n UI, select the `OpenAi account` credential on `Extract Price Text From Image`, and save. (Known n8n-mcp validator limitation — UI dropdown only. If `addNode` accepted the credential inline — openAiApi IS a declared credential on this node type, unlike httpRequest — this step is a no-op; verify via a test execution instead.)

- [ ] **Step 3: Publish**

n8n-mcp `publish_workflow` for `KoTuIlk4LMrlvnWI`. Expected: `{"success": true, ...}`.

- [ ] **Step 4: Run the full e2e**

Run: `Tools/n8n/test-upload-e2e.sh http://localhost:5678`
Expected: 8/8 PASS (cases 1–4 regression + 5–7 image). Then inspect the case-5 execution via n8n-mcp `search_executions` + `get_execution` (nodeNames `["Extract Price Text From Image", "Store Original File"]`): vision output must contain `Чай` and `5000`, store node must return a `Key`.

- [ ] **Step 5: Record state**

No commit (no repo files changed). Confirm `verify_rag.py all` still passes locally, and note in the final report that prod replication now also includes this branch + its OpenAI credential binding.

---

### Task 5: `ImageUploadPreprocessor` (client, TDD)

**Files:**
- Create: `Assets/Scripts/Converters/ImageUploadPreprocessor.cs`
- Test: `Assets/Tests/Editor/Chat/ImageUploadPreprocessorTests.cs`

**Interfaces:**
- Produces: `public static byte[] ImageUploadPreprocessor.ToJpegPayload(string filePath)` — returns JPEG bytes or **null** when the file is missing/undecodable/degenerate. Constants `ImageUploadPreprocessor.MaxDimension == 2048`, `ImageUploadPreprocessor.JpegQuality == 85`. Task 6 consumes exactly this.

- [ ] **Step 1: Check `ResizeEdgeRepair`'s real signature**

Run: `grep -rn "class ResizeEdgeRepair" Assets/Scripts/ && grep -n "public static" $(grep -rln "class ResizeEdgeRepair" Assets/Scripts/)` and read `Assets/Tests/Editor/Chat/ResizeEdgeRepairTests.cs` for the call shape. The code below assumes `ResizeEdgeRepair.Repair(Texture2D texture, int maxSize)`; adapt the one call site if the signature differs (it is gated internally on `max(width,height) == maxSize`; if the gate lives at the call site in ChatManager/Manager, replicate that gating here).

- [ ] **Step 2: Write the failing tests**

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class ImageUploadPreprocessorTests
{
    private static string TempPng(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (i % 7 == 0) ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = Path.Combine(Path.GetTempPath(), $"imgprep_{System.Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        return path;
    }

    [Test]
    public void ToJpegPayload_ValidPng_ReturnsDecodableJpeg()
    {
        string path = TempPng(320, 240);
        try
        {
            byte[] jpeg = ImageUploadPreprocessor.ToJpegPayload(path);
            Assert.IsNotNull(jpeg);
            Assert.Greater(jpeg.Length, 100);
            Assert.AreEqual(0xFF, jpeg[0]); // JPEG SOI marker
            Assert.AreEqual(0xD8, jpeg[1]);
            var decoded = new Texture2D(2, 2);
            Assert.IsTrue(decoded.LoadImage(jpeg));
            Assert.AreEqual(320, decoded.width);
            Assert.AreEqual(240, decoded.height);
            Object.DestroyImmediate(decoded);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void ToJpegPayload_MissingFile_ReturnsNull()
    {
        Assert.IsNull(ImageUploadPreprocessor.ToJpegPayload(
            Path.Combine(Path.GetTempPath(), "does_not_exist_12345.jpg")));
    }

    [Test]
    public void ToJpegPayload_CorruptBytes_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), $"imgprep_corrupt_{System.Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try { Assert.IsNull(ImageUploadPreprocessor.ToJpegPayload(path)); }
        finally { File.Delete(path); }
    }
}
```

(No downscale assertion: NativeGallery's Editor fallback skips native resize — maxSize is enforced on device only. That's the device-pass's job.)

- [ ] **Step 3: Run tests, verify they fail to compile** (`ImageUploadPreprocessor` not found)

Assets/Refresh (mcp-unity `execute_menu_item` `Assets/Refresh`), then mcp-unity `run_tests` filter `ImageUploadPreprocessorTests`. Expected: compile error or 0 found — the type doesn't exist yet. (Watch for the new-file import quirk: if Unity claims the TEST type doesn't exist after implementation too, delete the .cs+.meta and recreate.)

- [ ] **Step 4: Implement**

```csharp
using UnityEngine;

/// <summary>
/// One path for every picker-provided image regardless of source or format:
/// native decode (HEIC works on device; the Editor fallback covers png/jpg),
/// downscale to MaxDimension, re-encode as JPEG. Returns null when the file
/// is missing or undecodable — callers turn that into a failed upload row.
/// </summary>
public static class ImageUploadPreprocessor
{
    public const int MaxDimension = 2048;
    public const int JpegQuality = 85;

    public static byte[] ToJpegPayload(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return null;

        Texture2D texture = NativeGallery.LoadImageAtPath(filePath, MaxDimension, markTextureNonReadable: false);
        if (texture == null) return null;
        try
        {
            if (texture.width < 8 || texture.height < 8) return null; // degenerate decode
            ResizeEdgeRepair.Repair(texture, MaxDimension); // guards the native fractional-rect edge artifact
            return texture.EncodeToJPG(JpegQuality);
        }
        finally
        {
            Object.Destroy(texture);
        }
    }
}
```

(Adapt the `ResizeEdgeRepair` call per Step 1's findings. `Object.Destroy` is deferred in EditMode — if tests leak textures, switch to `Object.DestroyImmediate` under `#if UNITY_EDITOR`.)

- [ ] **Step 5: Refresh, run tests to green**

Assets/Refresh → `run_tests` filter `ImageUploadPreprocessorTests`. Expected: 3/3 pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Converters/ImageUploadPreprocessor.cs Assets/Scripts/Converters/ImageUploadPreprocessor.cs.meta \
        Assets/Tests/Editor/Chat/ImageUploadPreprocessorTests.cs Assets/Tests/Editor/Chat/ImageUploadPreprocessorTests.cs.meta
git commit -m "feat(upload): ImageUploadPreprocessor — decode/downscale/JPEG for photo price lists

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Upload branch + 422 mapping (client)

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings.Auth.cs` (UploadFile branch chain + response handling; RE-READ the file first — it changed recently)
- Modify: `Assets/Scripts/Main/UploadFailureText.cs` (two new strings + response classification; read its current shape and 5 existing tests first, follow that pattern)
- Test: extend the existing `UploadFailureText` test class (same file the background task created)

**Interfaces:**
- Consumes: `ImageUploadPreprocessor.ToJpegPayload` (Task 5); existing `MarkPendingRowFailed(GameObject row, string contentType, System.Action retry, string reason = null)` where `retry: null` renders the no-retry deterministic state.
- Produces: image files upload as `image/jpeg`; HTTP 422 + `no_price_data` renders «На фото не видно цен — попробуйте более чёткий снимок» with no retry.

- [ ] **Step 1: Write failing tests for the new `UploadFailureText` members**

Follow the existing test style in the `UploadFailureText` test file; add:

```csharp
[Test]
public void PhotoUndecodable_IsRussianAndNonEmpty()
{
    StringAssert.Contains("фото", UploadFailureText.PhotoUndecodable.ToLower());
}

[Test]
public void NoPriceDataOnPhoto_IsRussianAndNonEmpty()
{
    StringAssert.Contains("не видно цен", UploadFailureText.NoPriceDataOnPhoto);
}

[Test]
public void ReasonForHttpResponse_422NoPriceData_MapsToPhotoReason()
{
    string reason = UploadFailureText.ReasonForHttpResponse(422, "{\"success\":false,\"error\":\"no_price_data\"}");
    Assert.AreEqual(UploadFailureText.NoPriceDataOnPhoto, reason);
}

[Test]
public void ReasonForHttpResponse_OtherCodes_ReturnsNull()
{
    Assert.IsNull(UploadFailureText.ReasonForHttpResponse(500, "boom"));
    Assert.IsNull(UploadFailureText.ReasonForHttpResponse(422, "{\"error\":\"something_else\"}"));
}
```

- [ ] **Step 2: Run to verify failure** (`run_tests` with the existing UploadFailureText test class name). Expected: compile error — members missing.

- [ ] **Step 3: Implement in `UploadFailureText`**

```csharp
public const string PhotoUndecodable =
    "Не удалось прочитать фото — попробуйте другой снимок.";

public const string NoPriceDataOnPhoto =
    "На фото не видно цен — попробуйте более чёткий снимок.";

/// Deterministic server verdicts that retrying the same file cannot fix.
/// Returns null when the response is not one of them (caller keeps the
/// generic retryable failure path).
public static string ReasonForHttpResponse(long responseCode, string responseBody)
{
    if (responseCode == 422 && responseBody != null && responseBody.Contains("no_price_data"))
        return NoPriceDataOnPhoto;
    return null;
}
```

(Match the existing class's member style — if its strings are properties or static readonly, follow suit.)

- [ ] **Step 4: Add the image branch to `UploadFile` in BotSettings.Auth.cs**

Inside the existing try/catch branch chain, after the `.docx` branch and before the unsupported `else`:

```csharp
else if (fileExtension.Equals(".jpg") || fileExtension.Equals(".jpeg") || fileExtension.Equals(".png")
    || fileExtension.Equals(".webp") || fileExtension.Equals(".heic"))
{
    // Photos of menus/price boards: decode (HEIC included on device),
    // downscale, re-encode JPEG; the workflow's vision branch extracts text.
    payloadBytes = ImageUploadPreprocessor.ToJpegPayload(filePath);
    if (payloadBytes == null)
        failReason = UploadFailureText.PhotoUndecodable;
    else
    {
        payloadName = fileExtension.Equals(".jpg") ? fileName : fileName + ".jpg";
        payloadMime = "image/jpeg";
    }
}
```

- [ ] **Step 5: Map the 422 response in the same coroutine's failure handling**

In the `www.result != UnityWebRequest.Result.Success` block (re-read the current shape first — the failure-reason refactor already touched it), before the generic retryable path:

```csharp
string deterministicReason = UploadFailureText.ReasonForHttpResponse(www.responseCode, www.downloadHandler?.text);
if (deterministicReason != null)
{
    Debug.LogError($"[UploadFile] '{fileName}': {deterministicReason} ({www.responseCode})");
    MarkPendingRowFailed(pendingRow, contentType, null, deterministicReason);
    yield break;
}
```

(Keep the existing 415/unsupported handling and generic retry path unchanged after it.)

- [ ] **Step 6: Refresh, run the UploadFailureText test class + full EditMode suite**

Expected: new tests pass; full suite green (currently 658 + new tests). Use mcp-unity full run and parse `~/Library/Application Support/SynergySoft/Automation/TestResults.xml` if the MCP call times out client-side.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Main/BotSettings.Auth.cs Assets/Scripts/Main/UploadFailureText.cs Assets/Tests/Editor/Chat/*.cs
git commit -m "feat(upload): image branch (photos → JPEG → vision) + 422 no_price_data row reason

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Source sheet «Файл / Фото из галереи»

**Files:**
- Create: `Assets/Editor/BotSettingsUploadSourceSheetBuilder.cs` (`[MenuItem]` builder)
- Modify: `Assets/Scripts/Main/BotSettings.cs` (SerializeField refs for the sheet; picker-type fields jpg/png/webp/heic)
- Modify: `Assets/Scripts/Main/BotSettings.Auth.cs` (open sheet instead of picker; two source handlers)
- Modify: `Assets/Scenes/Main.unity` (built UI, saved scene)

**Interfaces:**
- Consumes: existing `UploadPriceList()`/`UploadServiceList()` → `PickMediaFile(contentType, targetButton)` flow; `UploadFile(path, contentType, targetButton)` coroutine.
- Produces: `ShowUploadSourceSheet(string contentType, Button targetButton)`; sheet buttons call `PickDocumentFile()` (existing picker incl. image types) and `PickPhotosFromGallery()` (NativeGallery multi-select).

**Before starting:** read `.claude/skills/unity-ui-builder/SKILL.md`, `.claude/rules/editor-scripts.md`, and `.claude/rules/ui-scripts.md`. Non-negotiables from project memory: sheet lives INSIDE the BotSettings screen panel (not canvas root); RoundedCorners with null sprite (never UISprite.psd); sizes in 1080×1920 reference units; builder is idempotent delete-and-rebuild, Edit-Mode only, no Undo grouping, SAVE THE SCENE after building; rewire ALL serialized consumers via SerializedObject.

- [ ] **Step 1: Extend picker fields + handlers in BotSettings.cs / BotSettings.Auth.cs**

BotSettings.cs fields block gains:

```csharp
private string jpg;
private string png;
private string webp;
private string heic;
```

BotSettings.Auth.cs — `InitializeFilePickerTypes()` gains:

```csharp
jpg = NativeFilePicker.ConvertExtensionToFileType("jpg"); // also covers .jpeg
png = NativeFilePicker.ConvertExtensionToFileType("png");
webp = NativeFilePicker.ConvertExtensionToFileType("webp");
heic = NativeFilePicker.ConvertExtensionToFileType("heic");
```

and the `fileTypes` arrays (both `#if` branches) append `jpg, png, webp, heic` (the null-filter already guards unknown MIMEs).

- [ ] **Step 2: Split entry points**

```csharp
private string pendingUploadContentType;
private Button pendingUploadButton;

private void UploadPriceList()
{
    ShowUploadSourceSheet("product", UploadProductsPriceListButton);
}

private void UploadServiceList()
{
    ShowUploadSourceSheet("service", UploadServicesPriceListButton);
}

private void ShowUploadSourceSheet(string contentType, Button targetButton)
{
    pendingUploadContentType = contentType;
    pendingUploadButton = targetButton;
    UploadSourceSheet.Show(); // component created by the builder in Step 4
}

// Wired to the sheet's «Файл» button.
public void OnUploadSourceFilePressed()
{
    UploadSourceSheet.Hide();
    InitializeFilePickerTypes();
    PickMediaFile(pendingUploadContentType, pendingUploadButton);
}

// Wired to the sheet's «Фото из галереи» button.
public void OnUploadSourceGalleryPressed()
{
    UploadSourceSheet.Hide();
    string contentType = pendingUploadContentType;
    Button targetButton = pendingUploadButton;
    NativeGallery.GetImagesFromGallery(paths =>
    {
        if (paths == null) return; // cancelled
        foreach (string path in paths)
            StartCoroutine(UploadFile(path, contentType, targetButton));
    }, "Выберите фото прайс-листа");
}
```

(`UploadSourceSheet` is a `[SerializeField]` reference on BotSettings to the sheet's small controller component — the builder creates both. If the existing BotSettings sheets (e.g. ItemEditSheet) expose a different show/hide idiom, follow that idiom instead; check `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs` first.)

- [ ] **Step 3: Sheet controller component**

Create alongside the other BotSettings UI primitives (`Assets/Scripts/Main/BotSettings/UploadSourceSheet.cs`), following `ItemEditSheet`'s show/hide + scrim pattern (DOTween slide 0.25–0.3s, FocusScrim, tap-outside-to-close). Content: title «Загрузить прайс-лист», two full-width option buttons («Файл», «Фото из галереи») with 88px+ touch targets, «Отмена» below.

- [ ] **Step 4: Builder**

`Assets/Editor/BotSettingsUploadSourceSheetBuilder.cs` with `[MenuItem("Tools/BotSettings/Build Upload Source Sheet")]`: idempotent delete-and-rebuild of the sheet under the BotSettings panel, wires the two buttons to `OnUploadSourceFilePressed`/`OnUploadSourceGalleryPressed` via persistent listeners, assigns the `UploadSourceSheet` SerializeField on every BotSettings prefab instance via SerializedObject, then `EditorSceneManager.MarkSceneDirty` + save. Run it via mcp-unity `execute_menu_item`, then SAVE THE SCENE (`File/Save` menu item), then verify via console logs there were no dangling-reference warnings.

- [ ] **Step 5: Refresh, compile, full suite**

Assets/Refresh → full EditMode run. Expected: all green (no behavior tests for the sheet — UI verified in Step 6).

- [ ] **Step 6: Editor smoke + device pass (user GREEN)**

Editor Play Mode at 1080×2400: tap «Загрузить прайс-лист» → sheet slides up; «Файл» opens picker (Editor shows a file dialog); «Отмена» closes. Then hand to the owner for the device pass: iPhone gallery HEIC multi-select → rows settle → bot answers a price question from the photo; blank photo → failed row with «На фото не видно цен…» and no retry button.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Main/BotSettings.cs Assets/Scripts/Main/BotSettings.Auth.cs \
        Assets/Scripts/Main/BotSettings/UploadSourceSheet.cs Assets/Scripts/Main/BotSettings/UploadSourceSheet.cs.meta \
        Assets/Editor/BotSettingsUploadSourceSheetBuilder.cs Assets/Editor/BotSettingsUploadSourceSheetBuilder.cs.meta \
        Assets/Scenes/Main.unity
git commit -m "feat(bot-settings): Файл/Галерея source sheet — photo price lists reach iPhone Photos

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Docs + final verification

**Files:**
- Modify: `CLAUDE.md` (UploadFile webhook bullet: add image formats + vision branch + 422; Converters bullet: add ImageUploadPreprocessor)
- Modify: `Tools/n8n/README.md` (Upload File table row: vision branch + 422 no_price_data)

- [ ] **Step 1: Update both docs** — one sentence each, matching the shipped behavior exactly (extension list jpg/jpeg/png/webp/heic client-side, jpg/jpeg/png/webp workflow-side, gpt-4o-mini, 422 `no_price_data`, photos archived to `price-lists/{fileId}`).

- [ ] **Step 2: Full EditMode suite + e2e once more**

Run both; expected: suite green, e2e 8/8.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md Tools/n8n/README.md
git commit -m "docs: image OCR tier — formats, vision branch, 422 contract

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
