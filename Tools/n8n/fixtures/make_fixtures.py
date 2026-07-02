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
