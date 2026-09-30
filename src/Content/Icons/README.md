`Frog.png` is the sitting green frog, facing left.

Run `python3 scripts/make-icons.py` to produce the executable ICO, MonoGame's embedded `Icon.bmp`, and Linux PNG. The 16×16 frame crops the native 19×18 sprite from its top-left corner, cutting off the bottom and right edges. Larger frames use the largest integer enlargement that fits, centered on a transparent canvas: 1× at 24/32, 2× at 48, 3× at 64, 6× at 128 and 13× at 256.

Windows loads separate small and large window icons from the embedded ICO, using the window's DPI. At 100% these use the prepared 16×16 and 32×32 frames. MonoGame's single embedded bitmap remains the icon source on other platforms.

The ICO uses Pillow's standard encoder. The BMP declares explicit RGBA channel masks in a BITMAPV5HEADER because SDL's bitmap loader needs those masks to preserve transparency.
