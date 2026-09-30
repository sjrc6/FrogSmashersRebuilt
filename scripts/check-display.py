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


def run(name, keys=(), frames=45, mouse=(), menu=False):
    script = out / (name + "-input.json")
    inputs = [dict(From=a, To=a + 1, Keys=k) for a, k in keys]
    inputs += [dict(From=a, To=a + 1, MouseX=x, MouseY=y, MouseDown=down) for a, x, y, down in mouse]
    script.write_text(json.dumps(sorted(inputs, key=lambda entry: entry["From"])))
    result = out / (name + ".json")
    proc = subprocess.run(
        exe
        + [
            "--no-intro" if menu else "--demo",
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
    assert not value["AllowUserResizing"], value
    assert (value["WindowWidth"], value["WindowHeight"]) == (
        value["BackBufferWidth"],
        value["BackBufferHeight"],
    ), value
    print(name, {key: value[key] for key in ("Page", "Selected", "Fullscreen", "WindowWidth", "WindowHeight")}, flush=True)
    return value


def reset(fullscreen=False):
    settings_file.write_text(json.dumps(dict(Fullscreen=fullscreen, VSync=False)))


def verify_hover(desktop):
    open_settings = [(2, ["Down"]), (4, ["Down"]), (6, ["Enter"])]
    for fullscreen in (False, True):
        for trigger in ("Right", "F11"):
            reset(fullscreen)
            before = desktop if fullscreen else (1280, 720)
            after = (1280, 720) if fullscreen else desktop

            def point(size, x, y):
                scale = min(size[0] / 1280, size[1] / 720)
                return round((size[0] - 1280 * scale) / 2 + x * scale), round((size[1] - 720 * scale) / 2 + y * scale)

            mouse = [(8, *point(before, 640, 174), False)]
            mouse += [(11, *point(after, 640, 420), False), (12, *point(after, 642, 470), False)]
            keys = open_settings + [(10, [trigger])]
            name = f"hover-{'leave' if fullscreen else 'enter'}-{trigger.lower()}"
            value = run(name, keys, frames=18, mouse=mouse, menu=True)
            assert value["Fullscreen"] != fullscreen and value["Page"] == "Settings" and value["Selected"] == 0, value

    reset(True)
    value = run("hover-resumes", keys, mouse=mouse + [(25, 640, 322, False)], menu=True)
    assert value["Selected"] == 3, value
    reset(True)
    value = run("click-after-toggle", keys, mouse=mouse + [(25, 640, 322, True)], menu=True)
    assert value["Selected"] == 3 and abs(value["Volume"] - .70) < .001, value
    reset(True)
    value = run("keyboard-after-toggle", keys + [(25, ["Down"])], mouse=mouse, menu=True)
    assert value["Selected"] == 1, value


def main():
    out.mkdir(parents=True, exist_ok=True)
    settings_file.parent.mkdir(parents=True, exist_ok=True)
    reset()
    window = run("window")
    assert not window["Fullscreen"] and (window["WindowWidth"], window["WindowHeight"]) == (1280, 720)
    full = run("enter-borderless", [(5, ["F11"])])
    assert full["Fullscreen"]
    desktop = (full["WindowWidth"], full["WindowHeight"])
    assert desktop != (1280, 720), "Use a desktop larger than the test window."
    reopened = run("reopen-borderless")
    assert reopened["Fullscreen"] and (reopened["WindowWidth"], reopened["WindowHeight"]) == desktop
    vsync = run("borderless-vsync", [(2, ["Down"]), (4, ["Down"]), (6, ["Enter"]), (8, ["Down"]), (10, ["Right"]), (12, ["Escape"])], menu=True)
    assert vsync["Fullscreen"] and (vsync["BackBufferWidth"], vsync["BackBufferHeight"]) == desktop
    assert json.loads(settings_file.read_text())["VSync"], "VSync menu action was not applied"
    cycled = run("six-toggles", [(n, ["F11"]) for n in (3, 8, 13, 18, 23, 28)])
    assert cycled["Fullscreen"] and (cycled["BackBufferWidth"], cycled["BackBufferHeight"]) == desktop
    restored = run("restore-window", [(5, ["F11"])])
    assert not restored["Fullscreen"] and (restored["WindowWidth"], restored["WindowHeight"]) == (1280, 720)
    reopened = run("reopen-window")
    assert not reopened["Fullscreen"] and (reopened["BackBufferWidth"], reopened["BackBufferHeight"]) == (
        1280,
        720,
    )
    assert not {"Width", "Height"} & json.loads(settings_file.read_text()).keys(), "window dimensions must not be saved"
    verify_hover(desktop)
    print("PASS: fixed window size, borderless transitions, saved startup, VSync, hover, clicks and keyboard navigation")


if __name__ == "__main__":
    main()
