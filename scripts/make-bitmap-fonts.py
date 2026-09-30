#!/usr/bin/env python3
"""Bake the game's TrueType faces into native pixel glyphs and integer metrics."""

import argparse
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.pointInsidePen import PointInsidePen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/Content/Fonts"
OUTPUT = ROOT / "src/Content/UI"


def rasterize(font, character):
    left, top, right, bottom = font.getbbox(character, anchor="ls")
    mask = Image.new("L", (max(1, right - left), max(1, bottom - top)))
    draw = ImageDraw.Draw(mask)
    draw.fontmode = "1"
    draw.text((-left, -top), character, font=font, fill=255, anchor="ls")
    bounds = mask.getbbox()
    if bounds is None:
        return None, (0, 0)
    return mask.crop(bounds), (left + bounds[0], top + bounds[1])


def rasterize_outline(glyphs, name, scale, cap_top):
    glyph = glyphs[name]
    bounds_pen = BoundsPen(glyphs)
    glyph.draw(bounds_pen)
    if bounds_pen.bounds is None:
        return None, (0, 0)
    x0, y0, x1, y1 = bounds_pen.bounds
    left, right = math.floor(x0 * scale), math.ceil(x1 * scale)
    top, bottom = math.floor((cap_top - y1) * scale), math.ceil((cap_top - y0) * scale)
    mask = Image.new("L", (right - left, bottom - top))
    pen = PointInsidePen(glyphs, (0, 0))
    for y in range(mask.height):
        for x in range(mask.width):
            pen.setTestPoint(((left + x + 0.5) / scale, cap_top - (top + y + 0.5) / scale))
            glyph.draw(pen)
            if pen.getResult():
                mask.putpixel((x, y), 255)
    bounds = mask.getbbox()
    if bounds is None:
        return None, (0, 0)
    return mask.crop(bounds), (left + bounds[0], top + bounds[1])


def build_font(config, previous):
    source = SOURCE / config["Source"]
    font = ImageFont.truetype(str(source), config["PixelSize"])
    definition = TTFont(source)
    characters = definition.getBestCmap()
    outlines = definition.getGlyphSet()
    scale = config["PixelSize"] / definition["head"].unitsPerEm
    cap_bounds = BoundsPen(outlines)
    outlines[characters[ord("H")]].draw(cap_bounds)
    capital, (_, capital_top) = rasterize(font, "H")
    glyphs = {}
    for code in range(32, 127):
        character = chr(code)
        if code not in characters:
            glyphs[code] = previous[config["Fallback"]][code]
            continue
        if config.get("Rasterizer") == "outlines":
            name = characters[code]
            mask, offset = rasterize_outline(outlines, name, scale, cap_bounds.bounds[3])
            advance = math.floor(outlines[name].width * scale + 0.5)
        else:
            mask, (left, top) = rasterize(font, character)
            offset = (left, top - capital_top)
            advance = math.floor(font.getlength(character) + 0.5)
        glyphs[code] = (mask, offset, advance)
    definition.close()

    atlas = Image.new("RGBA", (128, 128))
    entries = {}
    x = y = 1
    row_height = 0
    for code, (mask, offset, advance) in glyphs.items():
        entry = {"Advance": advance}
        if mask is not None:
            if x + mask.width + 1 > atlas.width:
                x = 1
                y += row_height + 1
                row_height = 0
            if y + mask.height + 1 > atlas.height:
                raise ValueError("Font atlas is too small")
            ink = Image.new("RGBA", mask.size, "white")
            ink.putalpha(mask)
            atlas.paste(ink, (x, y))
            entry.update(Region=[x, y, mask.width, mask.height], Offset=list(offset))
            x += mask.width + 1
            row_height = max(row_height, mask.height)
        entries[code] = entry
    data = {
        "Texture": f'UI/{config["Name"]}',
        "LineHeight": config["LineHeight"],
        "CapHeight": capital.height,
        "Glyphs": entries,
    }
    return atlas, data, glyphs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="check the stored atlases and metrics")
    args = parser.parse_args()
    previous = {}
    for config in json.loads((SOURCE / "fonts.json").read_text())["Fonts"]:
        atlas, data, glyphs = build_font(config, previous)
        previous[config["Name"]] = glyphs
        image_path = OUTPUT / f'{config["Name"]}.png'
        data_path = image_path.with_suffix(".json")
        metadata = json.dumps(data, indent=2) + "\n"
        if args.verify:
            with Image.open(image_path) as existing:
                if existing.size != atlas.size or existing.convert("RGBA").tobytes() != atlas.tobytes():
                    raise ValueError(f"Outdated font atlas: {image_path}")
            if data_path.read_text() != metadata:
                raise ValueError(f"Outdated font metrics: {data_path}")
        else:
            OUTPUT.mkdir(parents=True, exist_ok=True)
            atlas.save(image_path, optimize=True)
            data_path.write_text(metadata, encoding="utf-8", newline="\n")
        print(f'{config["Name"]}: {len(glyphs)} glyphs, {data["CapHeight"]}-pixel capitals')


if __name__ == "__main__":
    main()
