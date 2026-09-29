# Editable game content

This directory contains the port's maintained artwork, font sources, and game data.
The initial asset import used the original game source at commit `5ae12e413851bd2910853a217a6fdcd89115737f`. Asset rebuilds do not need Unity, the original repositories, or reference captures.

- `Textures`: source images and four imported DDS textures with their compression and mipmaps.
- `Fonts`: repaired TrueType sources and font generation settings.
- `UI`: generated distance-field atlases and complete glyph layout data.
- `Game`: maps, presentation scenes, sprite geometry, animations, materials, sounds, and character settings.
- `Content.mgcb`: the standard MonoGame content project, editable with MGCB Editor.

Shaders are in `src/Client/Shaders`. Authored OGG/WAV clips remain in `src/ContentBuild/Audio`; they are already the shipping assets and are decoded with the same playback path on both supported platforms. Converting those clips into PCM XNBs would enlarge the package without improving playback.

## Runtime content

`src/ContentBuild` contains the assets shipped with the game:

- `Textures`, `Effects`, and font atlases are standard MonoGame DesktopGL XNB assets loaded through `ContentManager`.
- `UI/*.json` contains generic distance-field font metrics.
- `content.json` combines the authored game data and shipping asset hashes.
- `Audio` contains maintained OGG/WAV source clips that ship directly. No Unity runtime audio or recorded particle trajectories are included.

Original asset licensing and attribution are in [ORIGINAL_LICENSE.md](../ContentBuild/ORIGINAL_LICENSE.md) and [CREDITS.txt](../ContentBuild/CREDITS.txt).

## Build

From the repository root:

```sh
dotnet tool restore
python3 src/tools/build_content.py
```

The tool builds DesktopGL XNBs for both Windows and Linux and assembles `src/ContentBuild/content.json`. The generated content is checked in, so ordinary C# builds need only the .NET SDK. Use `--rebuild` to force compilation, or `--verify` to validate the checked-in content without tools.

MGCB keeps its intermediate cache under `.build/obj/Content/DesktopGL` at the repository root.

MGCB is pinned to 3.8.4.1. Its output is compatible with the game's MonoGame 3.8.5.1 runtime; the newer builder's Linux native image importer requires glibc 2.38, which excludes the development machine's glibc 2.36. See [MonoGame issue 9412](https://github.com/MonoGame/MonoGame/issues/9412).

On Linux, effect compilation requires Wine configured using [MonoGame's shader compiler setup](https://docs.monogame.net/errors/mgfx0001?tab=linux). Set `MGFXC_WINE_PATH` to its prefix. Windows builds compile effects directly. A configured machine can also run MGCB Editor and then `python3 src/tools/build_content.py --manifest-only` to update game data and hashes.

## Editing

Edit textures in their source formats and add new assets to `Content.mgcb`. Pixel art uses lossless color XNBs with no premultiplication or generated mipmaps. The four DDS textures retain BC3 data and mipmaps through the `NoChange` processor setting. Water distortion materials declare `PackedNormal` for their alpha/green normal channels.

Edit map and scene JSON directly. Coordinates use Y-up world space; sprite rectangles use top-left texture coordinates. Source sprite polygons are ordinary imported asset geometry and can be changed with the other sprite properties. Numeric curves retain their authored values and tangents.

Font outline edits belong in the TTF files. See [Fonts/README.md](Fonts/README.md) for regeneration. Runtime font data contains only layout and rendering information; it contains no glyph repairs, platform fallbacks, or reference-game measurements.

OGG source clips stay compressed on disk. The game caches decoded audio when first used. Music and ambient clips retain the same source decoding and spatial mixing path as sound effects.
