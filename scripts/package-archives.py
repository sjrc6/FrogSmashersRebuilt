#!/usr/bin/env python3
"""Validate protocol identity and create release archives."""

import gzip
import hashlib
import json
import os
import subprocess
import sys
import tarfile
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RELEASES = ROOT / ".build/releases"
TARGETS = ("linux-x64", "win-x64")
PACKAGE_NAMES = {
    "linux-x64": "FrogSmashersRebuilt-linux",
    "win-x64": "FrogSmashersRebuilt-windows",
}
DEVELOPMENT_EXECUTABLES = (
    "FrogSmashers.Tests*",
    "FrogSmashers.Client.Tests*",
    "FrogSmashers.Client.GraphicsTests*",
    "FrogSmashers.Client.Automation*",
    "FrogSmashers.Updater.Tests*",
)
PROTOCOL_FIELDS = (
    "Protocol",
    "CodeHash",
    "GameplayHash",
    "NetworkIdentity",
)


def validate_packages(targets):
    builds = {}
    for target in targets:
        folder = RELEASES / f"FrogSmashersRebuilt-{target}"
        executable = "FrogSmashersRebuilt.exe" if target.startswith("win") else "FrogSmashersRebuilt"
        if not (folder / executable).is_file():
            raise SystemExit(f"{target}: game executable is missing")
        updater = "FrogSmashersUpdater.exe" if target.startswith("win") else "FrogSmashersUpdater"
        if not (folder / updater).is_file():
            raise SystemExit(f"{target}: updater executable is missing")
        docs = folder / "docs"
        expected_docs = {"controls.txt", "updating.txt"}
        if target.startswith("win"):
            expected_docs.add("IPv6-issues.txt")
        if not docs.is_dir() or {path.name for path in docs.iterdir()} != expected_docs:
            raise SystemExit(f"{target}: package docs must contain only {', '.join(sorted(expected_docs))}")
        if (folder / "IPv6-issues.txt").exists():
            raise SystemExit(f"{target}: IPv6 notice must remain in docs until an issue is detected")
        for path in folder.iterdir():
            if path.suffix in (".dll", ".so", ".pdb") or path.name.endswith((".deps.json", ".runtimeconfig.json")):
                raise SystemExit(f"{target}: unbundled runtime file beside executable: {path.name}")
        native = (
            {"SDL2.dll", "steam_api64.dll"}
            if target.startswith("win")
            else {"libSDL2-2.0.so.0", "libsteam_api.so"}
        )
        native_folder = folder / "runtimes" / target / "native"
        if not native_folder.is_dir() or {path.name for path in native_folder.iterdir()} != native:
            raise SystemExit(f"{target}: missing or unexpected SDL or Steam libraries in {native_folder}")
        if {path.name for path in (folder / "runtimes").iterdir()} != {target}:
            raise SystemExit(f"{target}: package contains another platform's native libraries")
        for pattern in DEVELOPMENT_EXECUTABLES:
            if any(folder.rglob(pattern)):
                raise SystemExit(f"{target}: development executable found in game package: {pattern}")
        build_info = ROOT / ".build/bin/FrogSmashers.Client" / f"release_{target}" / "BuildInfo.json"
        builds[target] = json.loads(build_info.read_text(encoding="utf-8"))
        manifest_hash = hashlib.sha256((folder / "Content/content.json").read_bytes()).hexdigest().upper()
        if manifest_hash != builds[target]["ContentManifestSha256"]:
            raise SystemExit(f"{target}: published content does not match the build identity")
        manifest = json.loads((folder / "Content/content.json").read_text(encoding="utf-8"))
        for asset, expected in manifest["AssetHashes"].items():
            path = folder / "Content" / asset
            if not path.is_file():
                raise SystemExit(f"{target}: missing content file: {asset}")
            if hashlib.sha256(path.read_bytes()).hexdigest() != expected.lower():
                raise SystemExit(f"{target}: content file does not match its manifest: {asset}")

    reference = next(iter(builds.values()))
    for target, build in builds.items():
        for field in PROTOCOL_FIELDS:
            if build[field] != reference[field]:
                raise SystemExit(
                    f"Cross-platform mismatch in {field}: {target}. Refusing incompatible online packages."
                )


def create_zip(folder, files, package_name, epoch):
    archive = RELEASES / f"{package_name}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in files:
            name = f"{package_name}/{path.relative_to(folder).as_posix()}"
            entry = zipfile.ZipInfo.from_file(path, name)
            if epoch is not None:
                entry.date_time = time.gmtime(max(epoch, 315532800))[:6]
            entry.external_attr = 0o100644 << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, path.read_bytes())
    return archive


def create_tar(folder, files, package_name, epoch):
    archive = RELEASES / f"{package_name}.tar.gz"
    with archive.open("wb") as raw, gzip.GzipFile(
        filename="", mode="wb", fileobj=raw, mtime=epoch, compresslevel=9
    ) as zipped:
        with tarfile.open(fileobj=zipped, mode="w") as output:
            for path in files:
                name = f"{package_name}/{path.relative_to(folder).as_posix()}"
                entry = output.gettarinfo(str(path), name)
                if epoch is not None:
                    entry.mtime = epoch
                entry.uid = entry.gid = 0
                entry.uname = entry.gname = ""
                entry.mode = 0o755 if os.access(path, os.X_OK) else 0o644
                with path.open("rb") as data:
                    output.addfile(entry, data)
    return archive


def package(target, epoch):
    folder = RELEASES / f"FrogSmashersRebuilt-{target}"
    manifest = folder / "installation.json"
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    owned = sorted(path for path in folder.rglob("*") if path.is_file() and path != manifest)
    for path in owned:
        relative = path.relative_to(folder)
        if path.is_symlink() or relative.parts[0] == "crashlogs" or any(part.startswith(".update-") for part in relative.parts):
            raise SystemExit(f"{target}: runtime files or symbolic links must not be packaged: {relative}")
    manifest.write_text(json.dumps({
        "Platform": target,
        "Commit": commit,
        "Files": {
            path.relative_to(folder).as_posix(): {
                "Sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper(),
                "Size": path.stat().st_size,
                "Executable": not target.startswith("win") and os.access(path, os.X_OK),
            }
            for path in owned
        },
    }, indent=2) + "\n", encoding="utf-8")
    files = sorted(path for path in folder.rglob("*") if path.is_file())
    package_name = PACKAGE_NAMES[target]
    if target.startswith("win"):
        archive = create_zip(folder, files, package_name, epoch)
    else:
        archive = create_tar(folder, files, package_name, epoch)
    size = archive.stat().st_size / 1048576
    print(f"{archive.relative_to(ROOT)} ({size:.1f} MiB)")


def main():
    targets = sys.argv[1:] or list(TARGETS)
    for target in targets:
        if target not in TARGETS:
            raise SystemExit(f"Unsupported target: {target}")
    validate_packages(targets)
    epoch = int(os.environ["SOURCE_DATE_EPOCH"]) if "SOURCE_DATE_EPOCH" in os.environ else None
    for target in targets:
        package(target, epoch)


if __name__ == "__main__":
    main()
