#!/usr/bin/env python3
"""
Give the emoji sprite sheets correct FaceInfo line metrics so emoji lines have real
height and stack vertically instead of collapsing to a zero-height baseline.

WHY
---
All 31 emoji sheets (texture-0..30.asset) ship with m_AscentLine: 0 / m_DescentLine: 0,
so TMP gives an emoji line zero ascent/descent (TMP_Text.cs:4149-4151, spriteFace.pointSize
> 0 branch). Consecutive emoji-only lines then advance the baseline by 0 → they render on
top of each other (the composer "emoji rows overlap" bug; also the latent 4+ emoji-only
chat-bubble overlap).

WHAT
----
Set, on every sheet's m_FaceInfo:
  m_AscentLine  = 148   (= glyph HorizontalBearingY: glyph top above baseline, face units)
  m_DescentLine = -12   (= bearingY - height = 148 - 160: glyph bottom below baseline)
  m_Baseline    = 0     (was -38; a non-zero baseline slides the glyph off the new ascent/
                         descent box and re-introduces overlap/clipping)
m_LineHeight is left at 0 so TMP auto-derives line height from ascent-descent.

The line box span becomes ascent-descent = 160 face units = exactly the glyph height, so an
emoji line advances by the rendered emoji height and rows stack with no overlap.

NOTE: these sheets are shared with the chat bubbles, so this also changes bubble emoji line
height (emoji+text lines grow, jumbo emoji padding needs re-tuning). That is expected.

Idempotent / re-runnable (re-run after any atlas regen). LFS YAML edit only — do NOT use the
inspector "Update Sprite Asset" button. A domain reload is required for loaded instances.
"""

import glob
import os
import re

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPRITE_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "Sprite Assets")

FACE_INFO_START = "  m_FaceInfo:\n"
SIBLING_FIELD = re.compile(r"^  [A-Za-z]")  # next 2-space-indented field ends the FaceInfo block

# field name -> value to force (within the FaceInfo block only)
TARGETS = {
    # Vertically center the emoji on the composer text (SFProText-Regular SDF @ fontSize 44) so
    # it sits inline with text (caret matches) and mixed text+emoji rows == emoji-only rows.
    # Derived against the TMP layout formulas (see Tools docs / workflow): m_Baseline drops the
    # emoji ink 8.9px (its center -> the text's center at 15.64px above baseline); the line box is
    # re-centered on the text center while ascent-descent stays 150 (row spacing unchanged) so the
    # emoji box still envelopes the text box on both sides -> mixed-line height == emoji-line height.
    # NB: m_Baseline uses the FONT pointSize (224) scale factor, NOT the sprite's (100).
    # m_Baseline shifts the visible glyph WITHIN its (unchanged) line box, so it raises/lowers
    # the emoji without changing row spacing or mixed-row consistency. -55.216 centered the
    # glyph BOX on the text center, but the visible art sits low in the box (reads bottom-heavy
    # next to caps), so -30 raises it ~4px to sit on the capital's center. (Higher = poke the
    # box top more → slight overlap in stacked emoji-only rows; this is a by-eye balance.)
    "m_AscentLine": "118.35",
    "m_DescentLine": "-31.65",
    "m_Baseline": "-30",
    # Emoji render size (160 * 44/100 * 0.82 ~= 58px); matches WhatsApp composer emoji.
    "m_Scale": "0.82",
}
FIELD_RE = re.compile(r"^(    (" + "|".join(TARGETS) + r")): .*$")


def patch_file(path):
    with open(path, "r", encoding="utf-8") as handle:
        lines = handle.readlines()

    try:
        start = lines.index(FACE_INFO_START)
    except ValueError:
        return 0  # no FaceInfo block (not a sprite asset)

    # FaceInfo block runs until the next 2-space sibling field (e.g. "  m_Material:").
    end = start + 1
    while end < len(lines) and not SIBLING_FIELD.match(lines[end]):
        end += 1

    changed = 0
    for i in range(start + 1, end):
        match = FIELD_RE.match(lines[i].rstrip("\n"))
        if match:
            field = match.group(2)
            new_line = f"    {field}: {TARGETS[field]}\n"
            if lines[i] != new_line:
                lines[i] = new_line
                changed += 1

    if changed:
        with open(path, "w", encoding="utf-8") as handle:
            handle.writelines(lines)
    return changed


def main():
    sheets = sorted(
        glob.glob(os.path.join(SPRITE_DIR, "texture-*.asset")),
        key=lambda p: int(re.search(r"texture-(\d+)", p).group(1)),
    )
    if not sheets:
        raise SystemExit(f"No sprite sheets under {SPRITE_DIR!r}")

    total = 0
    for path in sheets:
        n = patch_file(path)
        total += n
        print(f"{os.path.basename(path):<16} {n} field(s) set")
    summary = ", ".join(f"{k}={v}" for k, v in TARGETS.items())
    print(f"\nFaceInfo updated on {len(sheets)} sheets — {summary}")


if __name__ == "__main__":
    main()
