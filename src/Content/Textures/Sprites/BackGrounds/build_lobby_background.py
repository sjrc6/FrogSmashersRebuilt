#!/usr/bin/env python3
"""Rebuild lobby_background_v1.png from the original room and title artwork.

Requires Pillow (also listed in src/tools/requirements-content.txt).
Run with: python3 path/to/build_lobby_background.py
Paths are relative to this script, so it can run from any working directory.
"""

from pathlib import Path

from PIL import Image


FOLDER = Path(__file__).resolve().parent
CANVAS_SIZE = (960, 540)
ROOM_SIZE = (290, 150)
WALL_WIDTH = 12
FLOOR_SHIFT = 6
ROOM_CROPS = {
    "bus_stop": (40, 40, 330, 190),
    "bus": (350, 40, 640, 190),
    "cafe": (40, 210, 330, 360),
    "park": (350, 210, 640, 360),
}
LAYOUT = [
    ["park", "bus_stop", "cafe"],
    ["cafe", None, "bus"],
    ["bus", "park", "bus_stop"],
]


def main():
    with Image.open(FOLDER / "join_screen_v1.png") as image:
        source = image.convert("RGBA")
    with Image.open(FOLDER.parent / "IntroAnim" / "title_bg_v2.png") as image:
        title = image.convert("RGBA")
    rooms = {name: source.crop(box) for name, box in ROOM_CROPS.items()}

    width, height = CANVAS_SIZE
    room_width, room_height = ROOM_SIZE
    half_wall = WALL_WIDTH // 2
    left = (width - 3 * room_width) // 2
    top = (height - 3 * room_height) // 2
    xs = [left + column * room_width for column in range(3)]
    ys = [top + row * room_height for row in range(3)]
    right, bottom = xs[2] + room_width, ys[2] + room_height
    canvas = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 255))
    for row, names in enumerate(LAYOUT):
        for column, name in enumerate(names):
            if name:
                canvas.paste(rooms[name], (xs[column], ys[row]))

    # Use the smallest integer scale that fills the center, then crop centrally.
    scale = max(
        (room_width + title.width - 1) // title.width,
        (room_height + title.height - 1) // title.height,
    )
    scaled = title.resize(
        (title.width * scale, title.height * scale), Image.Resampling.NEAREST
    )
    crop_x = (scaled.width - room_width) // 2
    crop_y = (scaled.height - room_height) // 2
    center = scaled.crop(
        (crop_x, crop_y, crop_x + room_width, crop_y + room_height)
    )
    canvas.paste(center, (xs[1], ys[1]))

    # Each vertical opening occupies the lowest third of its room wall.
    # A six-pixel shift puts the top of each floor wall at the image bottom.
    vertical_walls = Image.new("L", CANVAS_SIZE, 0)
    vertical_opening = room_height // 3
    floors = [
        ys[1] - half_wall + FLOOR_SHIFT,
        ys[2] - half_wall + FLOOR_SHIFT,
        bottom,
    ]
    for join_x in xs[1:]:
        vertical_walls.paste(
            255, (join_x - half_wall, top, join_x + half_wall, bottom)
        )
        for floor in floors:
            vertical_walls.paste(
                0,
                (join_x - half_wall, floor - vertical_opening, join_x + half_wall, floor),
            )

    # Horizontal openings are centered and rounded to the nearest whole pixel.
    horizontal_walls = Image.new("L", CANVAS_SIZE, 0)
    horizontal_opening = round(room_width / 3)
    for join_y in ys[1:]:
        shifted_y = join_y + FLOOR_SHIFT
        horizontal_walls.paste(
            255, (left, shifted_y - half_wall, right, shifted_y + half_wall)
        )
        for room_x in xs:
            opening_x = room_x + (room_width - horizontal_opening) // 2
            horizontal_walls.paste(
                0,
                (
                    opening_x,
                    shifted_y - half_wall,
                    opening_x + horizontal_opening,
                    shifted_y + half_wall,
                ),
            )

    canvas.paste((0, 0, 0, 255), (0, 0), vertical_walls)
    canvas.paste((0, 0, 0, 255), (0, 0), horizontal_walls)
    destination = FOLDER / "lobby_background_v1.png"
    canvas.save(destination, optimize=True)
    print(f"Saved {destination} ({width} x {height})")


if __name__ == "__main__":
    main()
