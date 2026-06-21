#!/usr/bin/env python3
"""
Render emoji "decorator" codepoints as nothing in the chat composer, so a typed
multi-codepoint sequence shows its clean BASE emoji instead of base + tofu/swatch.

WHY
---
TMP resolves glyphs one codepoint at a time, so a sequence like skin-toned 👍🏽
(U+1F44D + U+1F3FD) renders as the base 👍 (mapped by assign-emoji-sprite-codepoints.py)
followed by a stray skin-tone swatch; ❤️ (U+2764 + U+FE0F) renders the heart followed by
a □ for the variation selector; ZWJ families render their parts joined by □. The "decorator"
codepoints (skin-tone modifiers, variation selectors, ZWJ, combining keycap) are what produce
the swatch/tofu. There is no way to combine a sequence into one sprite in an editable
TMP_InputField without breaking the caret, so the clean approach is to make those decorators
render as NOTHING — the base emoji then stands alone.

WHAT
----
Add ONE zero-size / zero-advance sprite glyph to texture-0.asset (the TMP default sprite
asset, searched first by codepoint with fallbacks) and map every decorator codepoint to it.
Because texture-0 is searched before its fallback sheets, this also OVERRIDES the skin-tone
swatch sprites that live in the fallback sheets. Pure codepoint rendering — the raw string
is untouched, so the caret/selection stay correct. Chat bubbles are unaffected: they resolve
combined sequences by NAME ("1f44d-1f3fd"), never these bare codepoints.

NOT covered: country-flag regional indicators (U+1F1E6–1F1FF) — a flag has no base emoji to
fall back to, so it is intentionally left alone (would otherwise vanish entirely).

Idempotent and safe to re-run (e.g. after an atlas regen). Only ADDS one glyph + the decorator
character entries to texture-0; never edits existing glyphs/indices (avoids the duplicate-index
"same key" crash).
"""

import os
import re

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX0 = os.path.join(REPO_ROOT, "Assets", "Resources", "Sprite Assets", "texture-0.asset")

CHAR_TABLE_END = "  m_GlyphTable:\n"          # decorator chars are inserted just before this
# m_GlyphTable is immediately followed by the spriteInfoList field — the zero-size glyph
# MUST be inserted before it (i.e. as the last m_GlyphTable entry), NOT before
# fallbackSpriteAssets (which sits after spriteInfoList and would orphan the glyph).
GLYPH_TABLE_END_PREFIX = "  spriteInfoList:"
MARKER_NAME = "zw-200d"                         # idempotency sentinel

# Decorator codepoints to swallow (render as nothing), grouped for clarity.
DECORATORS = (
    [0x200D]                              # zero-width joiner
    + list(range(0xFE00, 0xFE0F + 1))    # variation selectors VS1..VS16 (incl. FE0F emoji presentation)
    + [0x20E3]                           # combining enclosing keycap
    + list(range(0x1F3FB, 0x1F3FF + 1))  # Fitzpatrick skin-tone modifiers
)


def glyph_block(index):
    return [
        f"  - m_Index: {index}\n",
        "    m_Metrics:\n",
        "      m_Width: 0\n",
        "      m_Height: 0\n",
        "      m_HorizontalBearingX: 0\n",
        "      m_HorizontalBearingY: 0\n",
        "      m_HorizontalAdvance: 0\n",
        "    m_GlyphRect:\n",
        "      m_X: 0\n",
        "      m_Y: 0\n",
        "      m_Width: 0\n",
        "      m_Height: 0\n",
        "    m_Scale: 1\n",
        "    m_AtlasIndex: 0\n",
        "    m_ClassDefinitionType: 0\n",
        "    sprite: {fileID: 0}\n",
    ]


def char_block(codepoint, glyph_index):
    return [
        "  - m_ElementType: 2\n",
        f"    m_Unicode: {codepoint}\n",
        f"    m_GlyphIndex: {glyph_index}\n",
        "    m_Scale: 1\n",
        f"    m_Name: zw-{codepoint:04x}\n",
    ]


def main():
    with open(TEX0, "r", encoding="utf-8") as handle:
        lines = handle.readlines()

    if any(f"m_Name: {MARKER_NAME}" in line for line in lines):
        print("Already applied (found decorator chars) — nothing to do.")
        return

    char_end = lines.index(CHAR_TABLE_END)
    glyph_end = next(i for i, line in enumerate(lines) if line.startswith(GLYPH_TABLE_END_PREFIX))

    # Max existing glyph index lives in the glyph table (between the two markers).
    max_index = max(
        int(re.match(r"  - m_Index: (\d+)", line).group(1))
        for line in lines[char_end:glyph_end]
        if line.startswith("  - m_Index:")
    )
    new_index = max_index + 1

    chars = []
    for cp in DECORATORS:
        chars.extend(char_block(cp, new_index))

    # Insert the glyph first (it sits later in the file) so the char-table index stays valid.
    lines[glyph_end:glyph_end] = glyph_block(new_index)
    lines[char_end:char_end] = chars

    with open(TEX0, "w", encoding="utf-8") as handle:
        handle.writelines(lines)

    print(f"Added zero-size glyph m_Index={new_index} and {len(DECORATORS)} decorator chars to texture-0.asset")
    print(f"Decorators hidden: {', '.join(f'{cp:04x}' for cp in DECORATORS)}")


if __name__ == "__main__":
    main()
