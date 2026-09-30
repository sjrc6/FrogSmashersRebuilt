#!/usr/bin/env python3
"""Build application icons from the maintained sitting frog artwork."""

import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/Content/Icons/Frog.png"
OUTPUT = ROOT / "src/Client/Icons"
SIZES = (16, 24, 32, 48, 64, 128, 256)


def icon_frame(source, size):
    ratio = size / max(source.size)
    width, height = round(source.width * ratio), round(source.height * ratio)
    scaled = source.resize((width, height), Image.Resampling.NEAREST)
    frame = Image.new("RGBA", (size, size))
    frame.paste(scaled, ((size - width) // 2, (size - height) // 2))
    return frame


def save_alpha_bitmap(image, path):
    width, height = image.size
    pixels = image.tobytes("raw", "BGRA")
    header = bytearray(124)
    struct.pack_into("<IiiHHIIiiII", header, 0, 124, width, -height, 1, 32, 3, len(pixels), 0, 0, 0, 0)
    struct.pack_into("<IIIII", header, 40, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000, 0x73524742)
    offset = 14 + len(header)
    file_header = struct.pack("<2sIHHI", b"BM", offset + len(pixels), 0, 0, offset)
    path.write_bytes(file_header + header + pixels)


def main():
    source = Image.open(SOURCE).convert("RGBA")
    frames = [icon_frame(source, size) for size in SIZES]
    largest = frames[-1]
    OUTPUT.mkdir(parents=True, exist_ok=True)
    largest.save(
        OUTPUT / "FrogSmashersRebuilt.ico",
        format="ICO",
        sizes=[frame.size for frame in frames],
        append_images=frames[:-1],
    )
    largest.save(OUTPUT / "FrogSmashersRebuilt.png")
    save_alpha_bitmap(largest, OUTPUT / "Icon.bmp")
    print(f"Generated icons in {OUTPUT}")


if __name__ == "__main__":
    main()
