---
status: diagnosed
phase: 05-channel-aware-chatmanager-core
source: [owner device screenshots 2026-07-14 19:38, Tools/tapi/samples probe 23366/23368/23369]
started: 2026-07-14T14:40:00Z
updated: 2026-07-14T14:40:00Z
---

# Phase 5 — Device UAT: Telegram media presentation gaps

Owner sent the missing media types to the dev Telegram account («Избранное») and
compared the app against native Telegram side-by-side. Voice (`ptt`), PDF
document, image, text, replies and reactions render correctly. Three
presentation gaps found — classification is coarse-but-correct (05-06's
defensive `video/*`→Video refinement), presentation treatments are missing.

## Tests

### 1. Animated `.tgs` sticker
expected: renders as a sticker (Telegram shows the artwork)
result: **FAIL** — renders as a document card (`AnimatedSticker.tgs 22 KB · TGS`)
evidence: probe_23366.json — `type:"document"`, `mimetype:"application/x-tgsticker"`,
`media_info 512×512`. Detection signal PERFECT (mimetype).
constraint: `.tgs` = gzipped Lottie JSON — NOT decodable in Unity without an
rlottie-class plugin. v1.1 target = sticker-bubble PLACEHOLDER (borderless square,
sticker glyph + «Стикер»), never a document card. Native animation = v2 candidate.

### 2. Video note (кружок)
expected: round, chrome-free bubble with duration badge (Telegram: autoplaying circle)
result: **FAIL** — renders as a regular video card with play overlay
evidence: probe_23368.json — `type:"video"`, `mimetype:"video/mp4"`,
`file_name:"video.mp4"`, `media_info {width:400,height:400,duration:2,is_round:false}`.
**`is_round` is UNRELIABLE — false for a genuine кружок on BOTH `messages/get`
and `messages/id/get`** (Wappi-side gap). Detection = heuristic:
`type=="video" && width==height && file_name=="video.mp4" && duration<=60`
(Telegram notes are always square, ≤60s, default-named). False positive = a
square regular video renders round — cosmetic, accepted.
v1.1 target: circular crop (RoundedCorners full radius), no filename/card chrome,
duration badge, tap-to-play (inline autoplay = polish, out of v1.1).

### 3. GIF
expected: video-style bubble with a "GIF" badge (Telegram: autoplay loop + GIF badge)
result: **FAIL** — renders as a plain video (no GIF affordance)
evidence: probe_23369.json — `type:"sticker"`, **`isGif:true`**, `mimetype:"video/mp4"`,
`media_info 320×180 duration:2`. Detection signal PERFECT (`isGif`).
v1.1 target: keep the video pipeline (thumb + tap-to-play) + "GIF" corner badge,
no filename chrome. Autoplay loop = polish, out of v1.1.

## Summary

total: 3
passed: 0
issues: 3
pending: 0
skipped: 0
blocked: 0

## Gaps

- gap: ".tgs sticker renders as document card"
  severity: cosmetic-major (wrong message kind communicated)
  status: failed
  fix_hint: TelegramMediaType rule application/x-tgsticker → Sticker + undecodable-sticker placeholder path
- gap: "video note renders as regular video card"
  severity: cosmetic-major
  status: failed
  fix_hint: heuristic isVideoNote flag through the 4-layer pipeline + circular MessageItemView treatment
- gap: "GIF renders as plain video (no badge)"
  severity: cosmetic-minor
  status: failed
  fix_hint: isGif flag through the pipeline + GIF badge overlay in MessageItemView

## Notes

- All three are Telegram-only treatments; WhatsApp rendering must stay byte-identical.
- Pipeline rule (chat-data-flow skill): each new flag must be carried
  RawMessage → NormalizedMessage → MessageViewModel → MessageItemView.
- Static webp stickers (`type:"sticker"` + `image/webp`) were NOT observed — the
  existing Sticker path + unity.webp should already handle them; verify in device UAT.
