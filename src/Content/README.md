## Content

The initial port used this commit `5ae12e413851bd2910853a217a6fdcd89115737f`

- `Textures` - source images and four imported DDS textures with their compression and mipmaps
- `Fonts`- font sources and generation settings
- `UI` - menu panel artwork, generated bitmap font atlases, integer glyph metrics and button icons
- `Game` - maps, presentation scenes, sprite geometry, animations, materials, sounds, and character settings
- `Content.mgcb`- MonoGame project content 

Shaders are in `src/Client/Shaders`\
OGG/WAV are in `src/ContentBuild/Audio`


`src/ContentBuild`- mostly built assets

- `Textures`, `Effects`, and font atlases are standard MonoGame DesktopGL XNB assets loaded through `ContentManager`.
- `UI/*.json` contains bitmap font metrics in source pixels.
- `content.json` combines the authored game data and shipping asset hashes.
- `Audio` contains OGG/WAV source clips that ship directly


### Build

From the repository root:

```sh
dotnet tool restore
python3 src/tools/build_content.py
```

Builds DesktopGL XNBs for both Windows and Linux and assembles `src/ContentBuild/content.json`. Use `--rebuild` to force compilation, or `--verify` to validate the checked-in content without tools.

MGCB is pinned to 3.8.4.1. the newer builder's Linux native image importer requires glibc 2.38, See [MonoGame #9412](https://github.com/MonoGame/MonoGame/issues/9412).

On Linux, effect compilation requires Wine configured using [MonoGame's shader compiler setup](https://docs.monogame.net/errors/mgfx0001?tab=linux). Set `MGFXC_WINE_PATH` to its prefix. Windows builds compile effects directly. A configured machine can also run MGCB Editor and then `python3 src/tools/build_content.py --manifest-only` to update game data and hashes.

### Editing

Edit textures in their source formats and add new assets to `Content.mgcb`. Pixel art uses lossless color XNBs with no premultiplication or generated mipmaps. The four DDS textures retain BC3 data and mipmaps through the `NoChange` processor setting. Water distortion materials declare `PackedNormal` for their alpha/green normal channels.

Map and scene JSON can be edited directly. Coordinates use Y-up world space.
