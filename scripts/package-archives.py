#!/usr/bin/env python3
"""Validate protocol identity and create reproducible release archives."""

import gzip
import hashlib
import json
import os
import sys
import tarfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RELEASES = ROOT / ".build/releases"
TARGETS = ("linux-x64", "win-x64")
DEVELOPMENT_EXECUTABLES = (
    "FrogSmashers.Tests*",
    "FrogSmashers.Client.Tests*",
    "FrogSmashers.Client.GraphicsTests*",
    "FrogSmashers.Client.Automation*",
)
PROTOCOL_FIELDS = (
    "CoreModuleId",
    "NetworkModuleId",
    "CoreSha256",
    "NetworkSha256",
    "ContentManifestSha256",
    "NetworkFingerprint",
)


def validate_packages(targets):
    builds = {}
    for target in targets:
        folder = RELEASES / f"FrogSmashersRebuilt-{target}"
        for pattern in DEVELOPMENT_EXECUTABLES:
            if any(folder.rglob(pattern)):
                raise SystemExit(f"{target}: development executable found in game package: {pattern}")
        builds[target] = json.loads((folder / "BuildInfo.json").read_text(encoding="utf-8"))

    reference = next(iter(builds.values()))
    for target, build in builds.items():
        for field in PROTOCOL_FIELDS:
            if build[field] != reference[field]:
                raise SystemExit(
                    f"Cross-platform mismatch in {field}: {target}. Refusing incompatible online packages."
                )


def write_file_hashes(folder):
    files = sorted(path for path in folder.rglob("*") if path.is_file() and path.name != "FILES.sha256")
    hashes = [
        f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.relative_to(folder).as_posix()}\n"
        for path in files
    ]
    (folder / "FILES.sha256").write_text("".join(hashes), encoding="utf-8", newline="\n")


def create_zip(folder, files):
    archive = RELEASES / f"{folder.name}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in files:
            name = f"{folder.name}/{path.relative_to(folder).as_posix()}"
            entry = zipfile.ZipInfo(name, (2000, 1, 1, 0, 0, 0))
            entry.external_attr = 0o100644 << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, path.read_bytes())
    return archive


def create_tar(folder, files, epoch):
    archive = RELEASES / f"{folder.name}.tar.gz"
    with archive.open("wb") as raw, gzip.GzipFile(
        filename="", mode="wb", fileobj=raw, mtime=epoch, compresslevel=9
    ) as zipped:
        with tarfile.open(fileobj=zipped, mode="w") as output:
            for path in files:
                name = f"{folder.name}/{path.relative_to(folder).as_posix()}"
                entry = output.gettarinfo(str(path), name)
                entry.mtime = epoch
                entry.uid = entry.gid = 0
                entry.uname = entry.gname = ""
                entry.mode = 0o755 if os.access(path, os.X_OK) else 0o644
                with path.open("rb") as data:
                    output.addfile(entry, data)
    return archive


def package(target, epoch):
    folder = RELEASES / f"FrogSmashersRebuilt-{target}"
    write_file_hashes(folder)
    files = sorted(path for path in folder.rglob("*") if path.is_file())
    archive = create_zip(folder, files) if target.startswith("win") else create_tar(folder, files, epoch)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_name(archive.name + ".sha256").write_text(
        f"{digest}  {archive.name}\n", encoding="utf-8", newline="\n"
    )
    size = archive.stat().st_size / 1048576
    print(f"{archive.relative_to(ROOT)} ({size:.1f} MiB) SHA256 {digest}")


def main():
    targets = sys.argv[1:] or list(TARGETS)
    for target in targets:
        if target not in TARGETS:
            raise SystemExit(f"Unsupported target: {target}")
    validate_packages(targets)
    epoch = int(os.environ.get("SOURCE_DATE_EPOCH", "946684800"))
    for target in targets:
        package(target, epoch)


if __name__ == "__main__":
    main()
