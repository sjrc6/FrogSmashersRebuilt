# Fonts

These are the font sources.

- `Retroville-repaired.ttf`
- `ArcadeClassic-repaired.ttf`
- `LiberationSans-Regular.ttf`

Run `python3 scripts/make-distance-fonts.py` after editing. It requires Pillow, FontTools, and [msdf-atlas-gen](https://github.com/Chlumsky/msdf-atlas-gen) (`6148900d59423059bafde2f51a0cb303184404bd`). Pass its executable with `--generator` if it is not on PATH. Then rebuild MonoGame content.

`DGR/smallFont.png` is DuckGameRebuilt's bitmap font, with its character order in `DGR/smallFont.json`. It supplies the black labels in the [baked keyboard icons](../UI/Buttons/keyboard/README.md). Rebuild those with `python3 scripts/bake-keyboard-icons.py`.
