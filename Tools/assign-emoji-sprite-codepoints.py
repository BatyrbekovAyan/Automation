#!/usr/bin/env python3
"""
Assign real Unicode codepoints to single-codepoint emoji sprites.

WHY
---
This project renders emoji as TMP <sprite name="hex"> tags (UnicodeEmojiConverter
+ EmojiPatchService). Every baked sprite in Assets/Resources/Sprite Assets/texture-*.asset
is keyed by NAME with m_Unicode = 65534 (0xFFFE) by design, so the name-tag pipeline
resolves them. An editable TMP_InputField, however, shows raw typed text and can only
resolve a typed emoji by its CODEPOINT (TMP_Text.GetTextElement -> default sprite asset,
TMP_Text.cs:6255). With every sprite at 0xFFFE, a typed emoji never matches -> it renders
as a tofu box and looks like "emoji don't work" in the chat composer.

WHAT
----
For each sprite whose NAME is a single Unicode scalar (one hex group, optionally with a
trailing "-fe0f" variation selector, e.g. "1f602" or "2764-fe0f"), set m_Unicode to that
real codepoint. Multi-codepoint names (flags "1f1ee-1f1f3", keycaps "0023-fe0f-20e3",
ZWJ sequences, skin-tone-modified) are left at 0xFFFE — they can't be a single codepoint
and still resolve in chat bubbles via the name pipeline.

This ONLY edits m_Unicode integer values. Names, glyph indices, atlas textures, and the
name-based pipeline are untouched, so chat-bubble rendering is unaffected; the change is
purely additive (typed emoji codepoints now resolve to the existing color atlas).

Idempotent and safe to re-run (e.g. after an atlas regen). Within a single sheet, a real
codepoint is assigned at most once (first occurrence wins) to avoid duplicate lookup keys.
"""

import glob
import os
import re

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPRITE_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "Sprite Assets")

# One hex group (a single Unicode scalar), optionally followed by the FE0F
# emoji-presentation variation selector. TMP looks up the BASE codepoint, so
# "2764-fe0f" (red heart) maps to U+2764.
SINGLE_CODEPOINT = re.compile(r"^([0-9a-f]{1,6})(?:-fe0f)?$")

NO_GLYPH = "65534"  # 0xFFFE — TMP's "name-based, no codepoint" sentinel


def remap_file(path):
    with open(path, "r", encoding="utf-8") as handle:
        lines = handle.readlines()

    assigned = set()          # real codepoints already used in THIS sheet
    pending_unicode_line = None
    mapped = 0

    for index, line in enumerate(lines):
        # Sprite-character fields are indented 4 spaces; the asset/material
        # m_Name lines are indented 2 spaces, so this never touches them.
        if line.startswith("    m_Unicode:"):
            pending_unicode_line = index
        elif line.startswith("    m_Name:") and pending_unicode_line is not None:
            name = line.split("m_Name:", 1)[1].strip()
            match = SINGLE_CODEPOINT.match(name)
            if match:
                codepoint = int(match.group(1), 16)
                current = lines[pending_unicode_line].split("m_Unicode:", 1)[1].strip()
                if current == NO_GLYPH and codepoint != 0xFFFE and codepoint not in assigned:
                    lines[pending_unicode_line] = f"    m_Unicode: {codepoint}\n"
                    assigned.add(codepoint)
                    mapped += 1
            pending_unicode_line = None

    if mapped:
        with open(path, "w", encoding="utf-8") as handle:
            handle.writelines(lines)
    return mapped


def main():
    sheets = sorted(
        glob.glob(os.path.join(SPRITE_DIR, "texture-*.asset")),
        key=lambda p: int(re.search(r"texture-(\d+)", p).group(1)),
    )
    if not sheets:
        raise SystemExit(f"No sprite sheets found under {SPRITE_DIR!r}")

    total = 0
    for path in sheets:
        mapped = remap_file(path)
        total += mapped
        print(f"{os.path.basename(path):<16} mapped {mapped}")
    print(f"\nTOTAL single-codepoint emoji given real codepoints: {total}")


if __name__ == "__main__":
    main()
