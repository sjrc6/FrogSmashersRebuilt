#!/usr/bin/env python3
"""Exercise build identity in an isolated checkout; no running Steam client required."""

import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIAGNOSTICS = ROOT / ".build/bin/FrogSmashers.Tests/release/FrogSmashers.Tests.dll"


def run(command, cwd, env):
    result = subprocess.run(command, cwd=cwd, env=env, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout


def main():
    if not DIAGNOSTICS.is_file():
        raise SystemExit("Build FrogSmashers.Tests in Release first.")
    env = dict(os.environ, NUGET_PACKAGES=str(ROOT / ".build/nuget"),
               DOTNET_CLI_HOME=str(ROOT / ".build/dotnet"), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    with tempfile.TemporaryDirectory(prefix="frog-compatibility-") as temporary:
        checkout = Path(temporary)
        (checkout / "src").mkdir()
        shutil.copy2(ROOT / "global.json", checkout)
        for name in ("Core", "Network", "GGCS", "Build"):
            shutil.copytree(ROOT / "src" / name, checkout / "src" / name,
                            ignore=shutil.ignore_patterns("bin", "obj"))
        for name in ("Directory.Build.props", "Directory.Build.targets"):
            shutil.copy2(ROOT / "src" / name, checkout / "src" / name)
        project = "src/Network/FrogSmashers.Network.csproj"

        def build(*options, rid=None):
            command = ["dotnet", "build", project, "-c", "Release", "-m:1", "-nr:false",
                       "-p:NuGetAudit=false", "--ignore-failed-sources", *options]
            if rid:
                command.extend(["-r", rid])
            run(command, checkout, env)
            folder = checkout / ".build/bin/FrogSmashers.Network" / ("release_" + rid if rid else "release")
            (folder / "Content").mkdir(exist_ok=True)
            shutil.copy2(ROOT / "src/ContentBuild/content.json", folder / "Content/content.json")
            return json.loads(run(["dotnet", str(DIAGNOSTICS), "--describe-build", str(folder)], checkout, env))

        baseline = build()
        current = checkout / "current"
        (current / "Content").mkdir(parents=True)
        for name in ("FrogSmashers.Core.dll", "FrogSmashers.Network.dll", "GGCS.dll"):
            shutil.copy2(DIAGNOSTICS.parent / name, current / name)
        shutil.copy2(ROOT / "src/ContentBuild/content.json", current / "Content/content.json")
        original = json.loads(run(["dotnet", str(DIAGNOSTICS), "--describe-build", str(current)], checkout, env))
        assert baseline["NetworkIdentity"] == original["NetworkIdentity"], "relocating the checkout changed compatibility"
        assert baseline["CodeHash"] == build("-t:Rebuild")["CodeHash"], "clean rebuild changed compatibility"
        assert baseline["CodeHash"] == build("-p:DebugType=embedded")["CodeHash"], "debug packaging changed compatibility"
        for rid in ("linux-x64", "win-x64"):
            other = build("-p:DebugType=embedded", rid=rid)
            assert baseline["NetworkIdentity"] == other["NetworkIdentity"], f"{rid} changed compatibility"
        for source in (checkout / "src").rglob("*.cs"):
            source.write_bytes(source.read_bytes().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n"))
        assert baseline["CodeHash"] == build()["CodeHash"], "checkout line endings changed compatibility"

        world = checkout / "src/Core/World.cs"
        original = world.read_bytes()
        world.write_bytes(original.replace(b"TickRate = 100;", b"TickRate = 101;"))
        assert baseline["CodeHash"] != build()["CodeHash"], "simulation source edit did not change compatibility"
        world.write_bytes(original)

        rollback = checkout / "src/GGCS/RollbackTiming.cs"
        original = rollback.read_bytes()
        rollback.write_bytes(original + b"\ninternal enum CompatibilityProbe { Changed }\n")
        assert baseline["CodeHash"] != build()["CodeHash"], "rollback source edit did not change compatibility"
        rollback.write_bytes(original)
        assert baseline["CodeHash"] != build("-p:CheckForOverflowUnderflow=true")["CodeHash"], "compiler options were omitted"
        assert baseline["CodeHash"] == build()["CodeHash"], "restored inputs did not recover the original identity"
        print("PASS: clean rebuild, relocated checkout, CRLF, debug packaging and Windows/Linux target parity; "
              "simulation, rollback and compiler-option changes rejected.")


if __name__ == "__main__":
    main()
