#!/usr/bin/env python3
"""Build MonoGame assets and assemble the game's content manifest."""

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/Content"
OUTPUT = ROOT / "src/ContentBuild"


def encode(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False) + "\n").encode()


def assemble_game_data():
    game = SOURCE / "Game"
    data = {}
    for path in sorted(game.glob("*.json")):
        section = json.loads(path.read_text(encoding="utf-8"))
        duplicates = data.keys() & section.keys()
        if duplicates:
            raise ValueError(f"Duplicate game data in {path}: {duplicates}")
        data.update(section)
    data["Maps"] = [
        json.loads(path.read_text(encoding="utf-8")) for path in sorted((game / "Maps").glob("*.json"))
    ]
    data["PresentationScenes"] = {
        path.stem: json.loads(path.read_text(encoding="utf-8"))
        for path in sorted((game / "Scenes").glob("*.json"))
    }
    return data


def write_manifest():
    data = assemble_game_data()
    data["AssetHashes"] = {
        path.relative_to(OUTPUT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for directory in ("Audio", "Textures", "UI", "Effects")
        for path in sorted((OUTPUT / directory).rglob("*"))
        if path.is_file()
    }
    data["ContentHash"] = ""
    data["ContentHash"] = hashlib.sha256(encode(data)).hexdigest()
    verify_manifest(data)
    (OUTPUT / "content.json").write_bytes(encode(data))
    print(f'{len(data["AssetHashes"])} assets; content hash {data["ContentHash"]}')


def verify_manifest(data=None):
    if data is None:
        data = json.loads((OUTPUT / "content.json").read_text(encoding="utf-8"))
    expected = data["ContentHash"]
    unhashed = dict(data, ContentHash="")
    if hashlib.sha256(encode(unhashed)).hexdigest() != expected:
        raise ValueError("Content manifest hash does not match its contents")
    authored = {key: value for key, value in data.items() if key not in ("AssetHashes", "ContentHash")}
    if encode(authored) != encode(assemble_game_data()):
        raise ValueError("Game data is out of date; run python3 src/tools/build_content.py --manifest-only")
    hashes = data["AssetHashes"]
    for relative, expected_hash in hashes.items():
        path = OUTPUT / relative
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected_hash:
            raise ValueError(f"Missing or modified asset: {relative}")
    textures = {sprite["Path"] for sprite in data["Sprites"].values()}
    for material in data["Materials"].values():
        textures.update(material["Textures"].values())
    for scene in data["Maps"] + list(data["PresentationScenes"].values()):
        textures.update(emitter["TexturePath"] for emitter in scene["ParticleEmitters"])
    for texture in textures:
        if texture + ".xnb" not in hashes:
            raise ValueError(f"Missing compiled texture: {texture}")
    for clips in data["Sounds"].values():
        for clip in clips:
            if clip and clip not in hashes:
                raise ValueError(f"Missing sound: {clip}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="verify checked-in content without rebuilding")
    parser.add_argument(
        "--manifest-only", action="store_true", help="assemble game data after running MGCB separately"
    )
    parser.add_argument("--mgcb", type=Path, help="use a specific MGCB executable")
    parser.add_argument("--rebuild", action="store_true", help="rebuild unchanged assets as well")
    args = parser.parse_args()
    if args.verify:
        verify_manifest()
        print("Content hashes and asset references verified")
        return
    if not args.manifest_only:
        command = [str(args.mgcb.resolve())] if args.mgcb else ["dotnet", "tool", "run", "mgcb", "--"]
        command += [f'/@:{SOURCE / "Content.mgcb"}', f"/workingDir:{SOURCE}"]
        if args.rebuild:
            command.append("/rebuild")
        subprocess.run(command, cwd=ROOT, check=True)
    write_manifest()


if __name__ == "__main__":
    main()
