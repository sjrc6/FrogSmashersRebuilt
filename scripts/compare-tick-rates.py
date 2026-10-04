#!/usr/bin/env python3
"""Compare fixed 100/120 Hz builds without modifying the working tree."""

import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / ".build/checks/tick-rates"


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, NUGET_PACKAGES=str(ROOT / ".build/nuget"),
               DOTNET_CLI_HOME=str(ROOT / ".build/dotnet"), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    with tempfile.TemporaryDirectory(prefix="frog-tick-study-") as temporary:
        checkout = Path(temporary)
        (checkout / "src").mkdir()
        shutil.copy2(ROOT / "global.json", checkout)
        for name in ("Core", "Network", "GGCS", "Tests", "Build"):
            shutil.copytree(ROOT / "src" / name, checkout / "src" / name,
                            ignore=shutil.ignore_patterns("bin", "obj"))
        for name in ("Directory.Build.props", "Directory.Build.targets"):
            shutil.copy2(ROOT / "src" / name, checkout / "src" / name)
        world = checkout / "src/Core/World.cs"
        source = world.read_text()
        if "public const int TickRate = 100;" not in source:
            raise RuntimeError("Update the study's source substitution for the current fixed tick rate.")
        for rate in (120, 100):
            world.write_text(source.replace("public const int TickRate = 100;", f"public const int TickRate = {rate};"))
            command = ["dotnet", "build", "src/Tests/FrogSmashers.Tests.csproj", "-c", "Release",
                       "-m:1", "-nr:false", "-p:NuGetAudit=false", "--ignore-failed-sources"]
            build = subprocess.run(command, cwd=checkout, env=env, capture_output=True, text=True, timeout=180)
            if build.returncode:
                raise RuntimeError(build.stdout + build.stderr)
            executable = checkout / ".build/bin/FrogSmashers.Tests/release/FrogSmashers.Tests.dll"
            result = subprocess.run(["dotnet", str(executable), "--tick-rate-study"], cwd=checkout,
                                    env=env, capture_output=True, text=True, timeout=180)
            if result.returncode:
                raise RuntimeError(result.stdout + result.stderr)
            data = json.loads(result.stdout)
            (OUTPUT / f"{rate}.json").write_text(json.dumps(data, indent=2) + "\n")
            print(f"{rate} Hz: {data['Cpu']}; saved {OUTPUT / f'{rate}.json'}", flush=True)


if __name__ == "__main__":
    main()
