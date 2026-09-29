#!/usr/bin/env python3
"""Generate scalable UI font atlases and layout data from maintained TrueType assets."""

import argparse
import json
import math
import shutil
import subprocess
import tempfile
from pathlib import Path

from PIL import Image
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/Content/Fonts"
OUTPUT = ROOT / "src/Content/UI"


def round_metric(value):
    return math.floor(value + 0.5)


def generate_face(generator, source, codes, config, directory, name):
    charset = directory / f"{name}.txt"
    charset.write_text(" ".join(map(str, codes)), encoding="utf-8", newline="\n")
    image_path = directory / f"{name}.png"
    metadata_path = directory / f"{name}.json"
    subprocess.run(
        [
            str(generator),
            "-font",
            str(source),
            "-type",
            "msdf",
            "-dimensions",
            str(config["AtlasSize"]),
            str(config["AtlasSize"]),
            "-size",
            str(config["EmSize"]),
            "-pxrange",
            str(config["DistanceRange"]),
            "-pxalign",
            "off",
            "-yorigin",
            "top",
            "-seed",
            "0",
            "-threads",
            "1",
            "-charset",
            str(charset),
            "-imageout",
            str(image_path),
            "-json",
            str(metadata_path),
        ],
        check=True,
    )
    data = json.loads(metadata_path.read_text(encoding="utf-8"))
    return Image.open(image_path).convert("RGB"), data["glyphs"]


def pack_glyphs(faces, config):
    size = config["AtlasSize"]
    em_size = config["EmSize"]
    atlas = Image.new("RGB", (size, size))
    glyphs = {}
    x = y = 2
    row_height = 0
    entries = sorted(
        ((image, glyph) for image, face in faces for glyph in face),
        key=lambda entry: entry[1]["unicode"],
    )
    for image, glyph in entries:
        if "atlasBounds" not in glyph:
            continue
        area = glyph["atlasBounds"]
        plane = glyph["planeBounds"]
        left, top = math.floor(area["left"]), math.floor(area["top"])
        right, bottom = math.ceil(area["right"]), math.ceil(area["bottom"])
        width, height = right - left, bottom - top
        if x + width + 2 > size:
            x = 2
            y += row_height + 2
            row_height = 0
        if y + height + 2 > size:
            raise ValueError("Font atlas is too small for this character set")
        atlas.paste(image.crop((left, top, right, bottom)), (x, y))
        glyphs[glyph["unicode"]] = {
            "Region": [x, y, width, height],
            "Plane": [
                plane["left"] + (left - area["left"]) / em_size,
                plane["top"] + (top - area["top"]) / em_size,
                plane["right"] + (right - area["right"]) / em_size,
                plane["bottom"] + (bottom - area["bottom"]) / em_size,
            ],
        }
        x += width + 2
        row_height = max(row_height, height)
    return atlas, glyphs


def build_font(generator, config, definition):
    source = SOURCE / definition["Source"]
    font = TTFont(source)
    fallback_path = SOURCE / definition["Fallback"] if "Fallback" in definition else None
    fallback = TTFont(fallback_path) if fallback_path else None
    characters = range(32, 127)
    primary_codes = [code for code in characters if code in font.getBestCmap()]
    missing = [code for code in characters if code not in font.getBestCmap()]
    if missing and (fallback is None or any(code not in fallback.getBestCmap() for code in missing)):
        raise ValueError(f"Missing glyphs in {source.name}: {missing}")

    with tempfile.TemporaryDirectory() as temporary:
        directory = Path(temporary)
        faces = [generate_face(generator, source, primary_codes, config, directory, "primary")]
        if missing:
            faces.append(generate_face(generator, fallback_path, missing, config, directory, "fallback"))
        atlas, shapes = pack_glyphs(faces, config)
        atlas.save(OUTPUT / f'{definition["Atlas"]}.png')

    for layout in definition["Layouts"]:
        size = layout["Size"]
        metrics_size = layout["MetricsSize"]
        scale = size / metrics_size
        units = font["head"].unitsPerEm
        ascent = round_metric(font["hhea"].ascent * metrics_size / units)
        descent = round_metric(font["hhea"].descent * metrics_size / units)
        glyphs = {}
        for code in characters:
            face = font if code in primary_codes else fallback
            advance = face["hmtx"][face.getBestCmap()[code]][0]
            glyph = {"Advance": round_metric(advance * metrics_size / face["head"].unitsPerEm) * scale}
            if code in shapes:
                shape = shapes[code]
                left, top, right, bottom = shape["Plane"]
                glyph["Region"] = shape["Region"]
                glyph["Bounds"] = [left * size, top * size, (right - left) * size, (bottom - top) * size]
            glyphs[str(code)] = glyph
        result = {
            "Texture": f'UI/{definition["Atlas"]}',
            "AtlasSize": config["AtlasSize"],
            "DistanceRange": config["DistanceRange"],
            "LineHeight": layout["LineHeight"],
            "Baseline": ascent * scale,
            "CenteredBaseline": round_metric((ascent + descent) / 2) * scale,
            "Glyphs": glyphs,
        }
        path = OUTPUT / f'{layout["Name"]}.json'
        path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"{path.name}: {len(glyphs)} glyphs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--generator", type=Path)
    args = parser.parse_args()
    generator = args.generator or shutil.which("msdf-atlas-gen")
    if not generator:
        parser.error("Install msdf-atlas-gen or supply --generator")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    config = json.loads((SOURCE / "fonts.json").read_text(encoding="utf-8"))
    for definition in config["Fonts"]:
        build_font(generator, config, definition)


if __name__ == "__main__":
    main()
