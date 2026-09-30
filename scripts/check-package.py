#!/usr/bin/env python3
"""Launch a published Linux or Windows (Wine) game under X11; use xvfb-run in CI."""
from pathlib import Path
from PIL import ImageChops, ImageGrab
import argparse
import os
import re
import subprocess
import time

root = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("platform", choices=("linux-x64", "win-x64"))
    platform = parser.parse_args().platform
    out = root / ".build/checks/published-window" / platform
    out.mkdir(parents=True, exist_ok=True)
    folder = root / ".build/releases" / ("FrogSmashersRebuilt-" + platform)
    env = dict(
        os.environ,
        LIBGL_ALWAYS_SOFTWARE="1",
        ALSOFT_DRIVERS="null",
        XDG_DATA_HOME=str(out / "settings"),
        DOTNET_BUNDLE_EXTRACT_BASE_DIR=str(out / "extracted"),
    )
    if platform == "win-x64":
        def windows_path(path):
            return subprocess.check_output(["winepath", "-w", str(path)], text=True).strip()

        command = ["wine", windows_path(folder / "FrogSmashersRebuilt.exe")]
        env["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = windows_path(out / "extracted")
        env["WINEDEBUG"] = "+loaddll"
    else:
        command = [str(folder / "FrogSmashersRebuilt")]

    log_path = out / "launch.log"
    with log_path.open("w") as log:
        process = subprocess.Popen(
            command + ["--demo", "--players", "8"], cwd="/tmp", env=env, stdout=log, stderr=subprocess.STDOUT
        )
        try:
            window = None
            for _ in range(200):
                if process.poll() is not None:
                    raise RuntimeError(f"Game exited {process.returncode}: " + log_path.read_text()[-5000:])
                tree = subprocess.check_output(["xwininfo", "-root", "-tree"], text=True)
                window = re.search(r'(0x[0-9a-f]+) "Frog Smashers Rebuilt".*? (\d+)x(\d+)', tree)
                if window and (int(window[2]), int(window[3])) == (1280, 720):
                    break
                time.sleep(.1)
            assert window, "Game window did not appear"
            assert (int(window[2]), int(window[3])) == (1280, 720), window[0]
            time.sleep(1.5)
            first = ImageGrab.grab()
            time.sleep(1)
            second = ImageGrab.grab()
            second.save(out / "game.png")
            assert len(second.convert("RGB").getcolors(second.width * second.height)) > 100, "No rendered artwork"
            assert ImageChops.difference(first, second).getbbox() is not None, "Game did not animate"
            assert process.poll() is None, "Game exited after opening its window"

            log_text = log_path.read_text()
            if platform == "win-x64":
                loaded = [line for line in log_text.splitlines() if "Loaded" in line]
                assert any("runtimes" in line and "SDL2.dll" in line for line in loaded), "Packaged SDL not loaded"
                assert any("extracted" in line and "openal.dll" in line for line in loaded), "Bundled OpenAL not loaded"
            else:
                loaded = Path(f"/proc/{process.pid}/maps").read_text()
                assert str(folder / "runtimes/linux-x64/native/libSDL2-2.0.so.0") in loaded, "Packaged SDL not loaded"
                assert any("extracted" in line and "libopenal.so" in line for line in loaded.splitlines()), "Bundled OpenAL not loaded"
            assert "Audio output: basic stereo" in log_text and "Audio unavailable" not in log_text, log_text[-3000:]
            print(f"PASS {platform}: 1280x720, animated match, packaged SDL and bundled OpenAL, launch from /tmp")
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)


if __name__ == "__main__":
    main()
