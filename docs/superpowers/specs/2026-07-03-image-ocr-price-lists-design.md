# Image OCR for Price-List Uploads — Design

**Date:** 2026-07-03
**Status:** Approved (brainstorm 2026-07-03)

## Goal

Let owners upload **photos/images of menus and price boards** (jpg/jpeg/png/webp/heic)
into a bot's RAG knowledge through the existing Upload File pipeline. A huge share of
CIS small businesses have no price *file* at all — they have a photo of a menu or a
designed price image they already send to customers in WhatsApp.

## Decisions (locked during brainstorm)

1. **Image source UX**: tapping «Загрузить прайс-лист» opens a small **source sheet**:
   «Файл» (existing NativeFilePicker, image types added) / «Фото из галереи»
   (NativeGallery photo picker — reaches iPhone Photos, multi-select).
2. **Bad photo semantics**: if the vision model finds no readable price data, the
   upload **fails the row with a Russian reason** — nothing junk ever enters the RAG
   store. No ingest-and-warn middle state.
3. **Extraction approach**: **vision branch in the n8n Upload File workflow**
   (OpenAI vision node), not on-device OCR and not a dedicated OCR API. Rationale:
   prompt is tunable server-side without app-store releases; OpenAI credentials
   already exist in n8n; `Store Original File` already archives the upload, enabling
   future re-OCR of every photo from the `price-lists` bucket; one call does both OCR
   and semantic item↔price pairing. Keeps the architectural line: *the client
   normalizes what it understands; the server extracts what the client can't*
   (images join PDF on the server side).

## Client (Unity)

### Source sheet
- New small sheet inside the BotSettings screen panel (project UI conventions:
  sheet inside the panel, RoundedCorners null-sprite surfaces, thumb-zone buttons,
  1080×1920 reference units; built via an Editor `[MenuItem]` builder like the other
  BotSettings UI, scene saved after build).
- «Файл» → existing `PickMediaFile` flow; picker type list gains
  jpg/jpeg/png/webp/heic (null-MIME guard already in place).
- «Фото из галереи» → `NativeGallery.GetImagesFromGallery` (multi-select).
- Both routes feed the existing `UploadFile(path, contentType, targetButton)`
  coroutine per picked path — pending rows, replace-by-name confirm, `fileId`
  minting, `UploadedFilesStore`, and failure-reason UI unchanged.

### ImageUploadPreprocessor (new, `Assets/Scripts/Converters/`)
Single path for every image regardless of source or format:
- `NativeGallery.LoadImageAtPath(path, maxSize: 2048)` — native decode (HEIC works
  on device; Editor decodes jpg/png only, which is fine for dev).
- `EncodeToJPG(quality: 85)` → payload `image/jpeg`, filename normalized to a
  `.jpg` final extension (workflow routes on it).
- Reuse existing `ResizeEdgeRepair` (gated `max == maxSize`) against the native
  fractional-rect edge artifact; `Object.Destroy` the temp texture after encode.
- Decode failure / degenerate texture → `failReason` (deterministic, no retry)
  via `UploadFailureText`.
- Rationale for 2048/85: HEIC compatibility, ~300–800 KB uploads (well under n8n's
  16 MB webhook cap), controlled vision cost, enough resolution for dense menu text.

### UploadFile branch
New image branch beside pdf: extensions `.jpg/.jpeg/.png/.webp/.heic` → preprocessor
→ `payloadBytes` (`image/jpeg`). All conversion failures flow through the existing
try/catch + `failReason` structure.

## Workflow (Upload File, canonical JSON + dev n8n)

- **Switch**: new branch, extension in {jpg, jpeg, png, webp} (client always sends
  jpg; the rest is defensive), case-insensitive like the others.
- **Vision node**: OpenAI (`gpt-4o-mini`, temperature 0, max tokens ≥ 4000 for long
  menus) with a Russian prompt that emits *exactly* the shape the other converters
  produce: `{contentType}[N]: Название: …; Цена: …;` lines (contentType comes from
  the existing `Extract Content Type` node: `product`/`service`), section headers as
  plain lines, prices with currency as printed. If no readable price data: output
  exactly `NO_PRICE_DATA`.
- **IF gate**: output empty or contains `NO_PRICE_DATA` → respond **422**
  `{"success": false, "error": "no_price_data"}`; else Set `text` → **Merge input 0**
  (same as Extract from TXT/PDF) → existing Clean Text → Source Text → chunk (1000/150)
  → embed → Supabase insert. Chunking/retrieval behavior identical to spreadsheets.
- **Store Original File**: image branch also fans into it — every photo archived at
  `price-lists/{fileId}` (re-OCR later without user re-uploads).
- Migration: new idempotent `Tools/n8n/apply-image-ocr.py` over the canonical JSONs
  (by node name, verify via `verify_rag.py`), applied to dev n8n via MCP + publish.
  Note: the OpenAI credential on the new node is a UI dropdown (same n8n-mcp
  validator limitation as the storage nodes).

### App-side response mapping
422 + `no_price_data` → deterministic failed row, reason:
«На фото не видно цен — попробуйте более чёткий снимок» (no retry button; ✕ then
re-upload a better photo).

## Error handling & edge cases

- **Vision latency** (~5–15 s before webhook response): covered by the existing
  pending-row spinner; no timeout changes.
- **Multi-photo menus**: each photo = own row + fileId, independently deletable.
  No grouping (YAGNI).
- **Replace-by-name**: gallery names (`IMG_1234.jpg`) are unique enough; existing
  confirm flow unchanged.
- **Old app versions**: cannot pick images, so the new branch is unreachable for
  them — no compatibility window.
- **Privacy**: photos go to OpenAI — the same trust boundary as all existing bot
  content (chats and extracted documents already flow through OpenAI).

## Testing

- **EditMode**: `ImageUploadPreprocessor` — generated `Texture2D` → JPEG bytes;
  max-dimension respected; empty/corrupt input fails cleanly. (HEIC is device-only.)
- **Workflow invariants** (`verify_rag.py`): image Switch rule, store fan-out from
  the image branch, 422 gate wiring, model pinned.
- **E2E** (`test-upload-e2e.sh`): committed tiny text-bearing JPEG fixture → 200 +
  chunks deleted afterwards; blank JPEG → 422.
- **Device pass (owner GREEN)**: iPhone gallery HEIC multi-select end-to-end — pick,
  convert, upload, row settles, bot answers a price question from the photo.

## Rollout order

1. Workflow first (canonical JSONs → dev n8n → publish → e2e).
2. Client second (branch is unreachable until the picker offers images).
3. Prod: rides the existing prod-replication pass (canonical JSONs + bucket SQL +
   credential dropdowns, now including the OpenAI vision node).

## Out of scope (deferred)

- Photo grouping into one logical "menu" document.
- On-device OCR fallback / offline mode.
- Re-OCR batch job over the bucket (enabled by this design, built later).
- `.doc`/`.ods` parsing, rowspan handling (tracked separately).
