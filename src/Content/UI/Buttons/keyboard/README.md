# Keyboard icons

`baked/` contains 105 standard keyboard key icons at native 1× resolution. It covers letters, digits, punctuation, modifiers, navigation keys, the numpad and F1–F12. Escape is included for menu hints and remains reserved from gameplay bindings.

`keys.json` maps MonoGame key names to a DGR sprite or a bitmap label. Dedicated key sprites retain their original artwork. Generated labels use pure black ink, DGR's `smallFont` character advances and `KeyImage` positioning rounded down to whole source pixels. Long labels extend the blank key horizontally. All images retain transparency; the renderer enlarges them by integer factors with nearest-neighbor sampling.

Rebuild or check the PNGs from the repository root with Python and Pillow:

```sh
python3 scripts/bake-keyboard-icons.py
python3 scripts/bake-keyboard-icons.py --verify
```

Then rebuild the MonoGame content with `python3 src/tools/build_content.py`. Ordinary game builds use the checked-in compiled assets.

The source key sprites are stored here; the font atlas and its character order are in `Content/Fonts/DGR`. The generator does not need the `Originals` folder. The menu renderer uses the baked images at their logical size and does not overlay text on keys.
