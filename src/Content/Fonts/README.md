# Fonts

These TrueType files are the maintained font sources. Edit their outlines in a font editor; glyph repairs are already part of the files.

- `Retroville-repaired.ttf`: Retroville NC, with the eight's touching inner corners reinforced.
- `ArcadeClassic-repaired.ttf`: ArcadeClassic, with solid inner joins on zero and eight and a solid tail on Q/q.
- `LiberationSans-Regular.ttf`: punctuation absent from ArcadeClassic. Its appearance is consistent on Windows and Linux.

The repairs retain the original outlines' exterior bounds, bearings, and advances. Original game font licensing follows `src/ContentBuild/ORIGINAL_LICENSE.md`; Liberation Sans notices are under `src/Notices`.

`fonts.json` specifies atlas generation and UI sizes. `MetricsSize` controls integer rounding of advances and baselines at the design size, matching the original layouts. The generator reads these metrics from the fonts; no captured Unity metrics are needed.

Run `python3 scripts/make-distance-fonts.py` after editing. It requires Pillow, FontTools, and [msdf-atlas-gen](https://github.com/Chlumsky/msdf-atlas-gen) (`6148900d59423059bafde2f51a0cb303184404bd`). Pass its executable with `--generator` if it is not on PATH. Then rebuild MonoGame content.

The distance-field atlas retains sharp contours as score text scales and animates. A conventional fixed-size SpriteFont atlas would blur when enlarged or require several raster sizes.
