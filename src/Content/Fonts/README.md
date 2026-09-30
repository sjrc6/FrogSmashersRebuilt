# Fonts

The original game's Retroville NC and ArcadeClassic faces are baked into native pixel glyphs for the UI.

- `Retroville-repaired.ttf`
- `ArcadeClassic-repaired.ttf`

`fonts.json` sets the raster sizes: 10 for Retroville and 14 for ArcadeClassic. Both produce seven-pixel capitals. ArcadeClassic is sampled directly from its outlines because its TrueType hinting distorts some digits at this size. Its missing punctuation is copied from Retroville during baking. Each atlas contains printable ASCII, solid white ink and transparent space. Glyph offsets, advances and line heights are whole source pixels.

Run `python3 scripts/make-bitmap-fonts.py` after editing. It requires Pillow and FontTools. Use `--verify` to check the stored PNGs and JSON, then rebuild MonoGame content with `python3 src/tools/build_content.py`.

Menu text uses two screen pixels per source pixel at 1280×720 and three at 1920×1080, equivalent to a 640×360 UI grid. Other output sizes round to a whole screen-pixel scale, and positions snap to that glyph grid. Menu text keeps a fixed size and wraps longer labels.

Score text uses a ten-pixel capital height at 720p before camera and animation scaling. Its size and position remain continuous so camera zoom, floating awards and death messages do not jump between whole-glyph sizes. Both fonts use nearest-neighbor sampling and ordinary sprite blending; there is no font shader or smoothing setting.

`DGR/smallFont.png` is DuckGameRebuilt's bitmap font, with its character order in `DGR/smallFont.json`. It supplies the black labels in the [baked keyboard icons](../UI/Buttons/keyboard/README.md). Rebuild those with `python3 scripts/bake-keyboard-icons.py`.
