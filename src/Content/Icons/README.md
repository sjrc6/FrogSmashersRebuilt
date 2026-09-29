`Frog.png` is the sitting green frog, facing left.

Run `python3 scripts/make-icons.py` to produce the executable ICO, MonoGame's embedded `Icon.bmp`, and Linux PNG. Scaling uses nearest-neighbor sampling and whole source pixels when the target size permits it.

The ICO uses Pillow's standard encoder. The BMP declares explicit RGBA channel masks in a BITMAPV5HEADER because SDL's bitmap loader needs those masks to preserve transparency.
