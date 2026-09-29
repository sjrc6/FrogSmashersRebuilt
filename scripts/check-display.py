#!/usr/bin/env python3
"""Exercise real SDL borderless transitions under a desktop (use xvfb-run in CI)."""
from pathlib import Path
import json
import os
import subprocess

root = Path(__file__).resolve().parents[1]
out = root / ".build/checks/display-checks"
settings_root = root / ".build/display-check-settings"
settings_file = settings_root / "FrogSmashersRebuilt/settings.json"
env = dict(
    os.environ,
    LIBGL_ALWAYS_SOFTWARE="1",
    ALSOFT_DRIVERS="null",
    XDG_DATA_HOME=str(settings_root),
    DOTNET_CLI_HOME=str(root / ".build/dotnet"),
)
if "SDL_VIDEODRIVER" not in env and env.get("DISPLAY"):
    env["SDL_VIDEODRIVER"] = "x11"
exe = [
    "dotnet",
    str(root / ".build/bin/FrogSmashers.Client.Automation/release/FrogSmashers.Client.Automation.dll"),
]


def run(name, keys=(), frames=45):
    script = out / (name + "-input.json")
    script.write_text(json.dumps([dict(From=a, To=a + 1, Keys=k) for a, k in keys]))
    result = out / (name + ".json")
    proc = subprocess.run(
        exe
        + [
            "--demo",
            "--no-audio",
            "--frames",
            str(frames),
            "--input-script",
            str(script),
            "--result",
            str(result),
            "--capture",
            str(out / (name + ".png")),
        ],
        cwd=root,
        env=env,
        text=True,
        capture_output=True,
        timeout=90,
    )
    (out / (name + ".log")).write_text(proc.stdout + proc.stderr)
    assert proc.returncode == 0, (name, proc.stdout, proc.stderr)
    value = json.loads(result.read_text())
    assert value["Error"] is None and not value["HardwareModeSwitch"], value
    assert (value["WindowWidth"], value["WindowHeight"]) == (
        value["BackBufferWidth"],
        value["BackBufferHeight"],
    ), value
    print(name, value, flush=True)
    return value


def main():
    out.mkdir(parents=True, exist_ok=True)
    settings_file.parent.mkdir(parents=True, exist_ok=True)
    settings_file.write_text(json.dumps(dict(Width=800, Height=450, Fullscreen=False, VSync=False)))
    window = run("window")
    assert not window["Fullscreen"] and (window["WindowWidth"], window["WindowHeight"]) == (800, 450)
    full = run("enter-borderless", [(5, ["F11"])])
    assert full["Fullscreen"]
    desktop = (full["WindowWidth"], full["WindowHeight"])
    assert desktop != (800, 450), "Use a desktop larger than the test window."
    reopened = run("reopen-borderless")
    assert reopened["Fullscreen"] and (reopened["WindowWidth"], reopened["WindowHeight"]) == desktop
    vsync = run("borderless-vsync", [(2, ["Escape"]), (4, ["Tab"]), (6, ["Down"]), (8, ["Right"])])
    assert vsync["Fullscreen"] and (vsync["BackBufferWidth"], vsync["BackBufferHeight"]) == desktop
    cycled = run("six-toggles", [(n, ["F11"]) for n in (3, 8, 13, 18, 23, 28)])
    assert cycled["Fullscreen"] and (cycled["BackBufferWidth"], cycled["BackBufferHeight"]) == desktop
    restored = run("restore-window", [(5, ["F11"])])
    assert not restored["Fullscreen"] and (restored["WindowWidth"], restored["WindowHeight"]) == (800, 450)
    reopened = run("reopen-window")
    assert not reopened["Fullscreen"] and (reopened["BackBufferWidth"], reopened["BackBufferHeight"]) == (
        800,
        450,
    )
    print("PASS: borderless entry, saved startup, VSync, repeated transitions and window restoration")


if __name__ == "__main__":
    main()
