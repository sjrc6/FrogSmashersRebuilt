#!/usr/bin/env python3
"""Regenerate the checked-in GGCS traces using the pinned GGRS checkout."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess


REFERENCE_REVISION = "e97e3d2416cc68af2d2876d41180d950c2939b6e"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", help="Use only cached Cargo dependencies")
    parser.add_argument("--baseline-tests", action="store_true", help="Also run the original Rust unit and integration tests")
    parser.add_argument("--toolchain", default="1.87.0", help="Rustup toolchain to run (default: 1.87.0)")
    args = parser.parse_args()

    here = Path(__file__).resolve().parent
    root = here.parents[2]
    upstream = root / "Originals/Research/ggrs-upstream"
    revision = subprocess.check_output(["git", "-C", str(upstream), "rev-parse", "HEAD"], text=True).strip()
    if revision != REFERENCE_REVISION:
        raise SystemExit(f"Expected GGRS {REFERENCE_REVISION}, found {revision}")

    work = root / ".build/checks/ggcs-reference"
    source = work / "upstream"
    shutil.copytree(upstream, source, dirs_exist_ok=True, ignore=shutil.ignore_patterns(".git", "target"))
    shutil.copyfile(here / "oracle.rs", source / "src/ggcs_oracle.rs")
    library = source / "src/lib.rs"
    library.write_text(library.read_text() + "\n#[cfg(test)]\nmod ggcs_oracle;\n")

    environment = dict(os.environ, CARGO_TARGET_DIR=str(work / "target"))
    command = ["cargo", f"+{args.toolchain}", "test"]
    if args.offline:
        command.append("--offline")

    if args.baseline_tests:
        subprocess.run(command + ["--lib", "--tests"], cwd=source, env=environment, check=True)

    result = subprocess.run(
        command + ["--lib", "ggcs_oracle::ggcs_reference_trace", "--", "--nocapture"],
        cwd=source,
        env=environment,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    for line in result.stdout.splitlines():
        if line.startswith("GGCS_ORACLE:"):
            trace = json.loads(line.removeprefix("GGCS_ORACLE:"))
            trace = {"revision": revision, **trace}
            destination = here / "ggrs-traces.json"
            destination.write_text(json.dumps(trace, indent=2) + "\n")
            print(f"Wrote {destination.relative_to(root)}")
            return
    raise SystemExit("The reference test did not emit its trace")


if __name__ == "__main__":
    main()
