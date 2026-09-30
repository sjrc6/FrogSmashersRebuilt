#!/usr/bin/env python3
"""Bake standard keyboard icons with the DGR smallFont bitmap at native resolution."""

import argparse
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
KEYBOARD = ROOT / "src/Content/UI/Buttons/keyboard"
FONT = ROOT / "src/Content/Fonts/DGR"
TRANSPARENT = (0, 0, 0, 0)


def load_glyphs():
    atlas = Image.open(FONT / "smallFont.png").convert("RGBA")
    characters = json.loads((FONT / "smallFont.json").read_text())["Characters"]
    pixels = atlas.load()
    rectangles = []
    height = 0
    left = -1
    top = 1
    while top < atlas.height:
        for right in range(atlas.width):
            if pixels[right, top] == TRANSPARENT:
                if left == -1:
                    left = right
            elif left != -1:
                if height == 0:
                    bottom = top + 1
                    while pixels[right - 1, bottom] == TRANSPARENT:
                        bottom += 1
                    height = bottom - top
                rectangles.append((left, top, right, top + height))
                left = -1
        top += height + 1
    if len(rectangles) != len(characters):
        raise ValueError("DGR font character map does not match the atlas")
    glyphs = {}
    for character, rectangle in zip(characters, rectangles):
        glyphs.setdefault(character, atlas.crop(rectangle))
    return glyphs


def widen_key(key, width):
    if width == key.width:
        return key.copy()
    middle = key.width // 2
    result = Image.new("RGBA", (width, key.height))
    result.paste(key.crop((0, 0, middle, key.height)), (0, 0))
    strip = key.crop((middle, 0, middle + 1, key.height))
    strip_width = width - key.width + 1
    result.paste(strip.resize((strip_width, key.height), Image.Resampling.NEAREST), (middle, 0))
    result.paste(key.crop((middle + 1, 0, key.width, key.height)), (middle + strip_width, 0))
    return result


def render_label(key, glyphs, label):
    advance = sum(glyphs[character].width - 1 for character in label)
    width = max(key.width, advance + 5)
    result = widen_key(key, width)
    # Snap KeyImage's horizontal centering down to whole source pixels.
    x = (width - advance - 1) // 2
    for character in label:
        glyph = glyphs[character]
        mask = glyph.getchannel("A")
        ink = Image.new("RGBA", mask.size, (0, 0, 0, 255))
        ink.putalpha(mask)
        result.alpha_composite(ink, (x, 2))
        x += glyph.width - 1
    return result


def save_or_verify(path, image, verify):
    if verify:
        if not path.exists():
            raise ValueError(f"Missing baked icon: {path}")
        with Image.open(path) as existing:
            if existing.size != image.size or existing.convert("RGBA").tobytes() != image.tobytes():
                raise ValueError(f"Outdated baked icon: {path}")
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        image.save(path, optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="check stored images against their source artwork")
    args = parser.parse_args()
    glyphs = load_glyphs()
    key = Image.open(KEYBOARD / "key.png").convert("RGBA")
    definitions = json.loads((KEYBOARD / "keys.json").read_text())
    unexpected = {path.stem for path in (KEYBOARD / "baked").glob("*.png")} - definitions.keys()
    if unexpected:
        raise ValueError(f"Unexpected baked icons: {', '.join(sorted(unexpected))}")
    for name, definition in definitions.items():
        if "Sprite" in definition:
            image = Image.open(KEYBOARD / (definition["Sprite"] + ".png")).convert("RGBA")
        else:
            image = render_label(key, glyphs, definition["Label"])
        save_or_verify(KEYBOARD / "baked" / (name + ".png"), image, args.verify)
    action = "Verified" if args.verify else "Baked"
    print(f"{action} {len(definitions)} standard keyboard icons at 1x resolution")


if __name__ == "__main__":
    main()
