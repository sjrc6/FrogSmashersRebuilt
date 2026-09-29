#!/usr/bin/env python3
"""Exercise the actual MonoGame client, fixed/render clock split, menus and input replays."""
from pathlib import Path
import argparse
import json
import os
import socket
import subprocess

root = Path(__file__).resolve().parents[1]
out = root / ".build/checks/client-checks"
exe = [
    "dotnet",
    str(root / ".build/bin/FrogSmashers.Client.Automation/release/FrogSmashers.Client.Automation.dll"),
]
env = dict(
    os.environ,
    SDL_VIDEODRIVER="offscreen",
    LIBGL_ALWAYS_SOFTWARE="1",
    ALSOFT_DRIVERS="null",
    XDG_DATA_HOME=str(root / ".build/client-check-settings"),
    DOTNET_CLI_HOME=str(root / ".build/dotnet"),
)


def command(name, options, sound=False):
    return (
        exe
        + ["--offscreen", "--result", str(out / (name + ".json"))]
        + ([] if sound else ["--no-audio"])
        + options
    )


def run(name, options, sound=False):
    result = subprocess.run(
        command(name, options, sound), cwd=root, env=env, text=True, capture_output=True, timeout=90
    )
    (out / (name + ".log")).write_text(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError(f"{name} exited {result.returncode}: {result.stdout}\n{result.stderr}")
    value = json.loads((out / (name + ".json")).read_text())
    assert value["Error"] is None, value
    print(name, value, flush=True)
    return value


def verify_render_cadence():
    hashes = []
    for fps in (60, 144, 240):
        value = run(
            f"timing-{fps}", ["--demo", "--players", "8", "--frames", str(fps * 4), "--render-fps", str(fps)]
        )
        assert value["TickNumber"] == 480
        hashes.append(value["Hash"])
    assert len(set(hashes)) == 1, hashes


def verify_pause():
    pause_script = out / "pause-input.json"
    pause_script.write_text(
        json.dumps(
            [
                dict(From=120, To=121, Keys=["Escape"]),
                dict(From=140, To=141, Keys=["Tab"]),
                dict(From=200, To=201, Keys=["Escape"]),
                dict(From=260, To=261, Keys=["Escape"]),
            ]
        )
    )
    pause_results = []
    for name, frames in [("pause-start", 121), ("pause-held", 240), ("pause-resumed", 300)]:
        pause_results.append(
            run(
                name,
                [
                    "--demo",
                    "--players",
                    "8",
                    "--input-script",
                    str(pause_script),
                    "--frames",
                    str(frames),
                    "--capture",
                    str(out / (name + ".png")),
                ],
            )
        )
    assert pause_results[0]["Paused"] and pause_results[1]["Paused"] and not pause_results[2]["Paused"]
    assert (
        pause_results[0]["Hash"] == pause_results[1]["Hash"]
        and pause_results[0]["TickNumber"] == pause_results[1]["TickNumber"]
    )
    assert (out / "pause-start.png").read_bytes() == (
        out / "pause-held.png"
    ).read_bytes(), "Presentation changes during local pause/settings"
    assert pause_results[2]["TickNumber"] > pause_results[1]["TickNumber"]
    return pause_script


def verify_smoke_pause(pause_script):
    for name, frames in [("smoke-pause-start", 121), ("smoke-pause-held", 240)]:
        run(
            name,
            [
                "--demo",
                "--map",
                "4",
                "--players",
                "8",
                "--input-script",
                str(pause_script),
                "--frames",
                str(frames),
                "--capture",
                str(out / (name + ".png")),
            ],
        )
    assert (out / "smoke-pause-start.png").read_bytes() == (
        out / "smoke-pause-held.png"
    ).read_bytes(), "Skyline smoke moves or changes size during pause"


def verify_title_menu():
    for name, keys, expected in [
        ("title-only", [(1, 2, ["Enter"])], "Title"),
        ("title-to-menu", [(1, 2, ["Enter"]), (5, 6, ["Enter"])], "Main"),
    ]:
        title_script = out / (name + "-input.json")
        title_script.write_text(json.dumps([dict(From=a, To=b, Keys=c) for a, b, c in keys]))
        value = run(
            name,
            ["--input-script", str(title_script), "--frames", "60", "--capture", str(out / (name + ".png"))],
        )
        assert value["Page"] == expected, value
        if expected == "Main":
            assert value["MenuBackground"] and value["Map"] == "2DownSmash" and value["Players"] == 4, value


def verify_menu_background():
    record = out / "menu-background.fsr"
    record.unlink(missing_ok=True)
    record.with_name(record.name + ".json").unlink(missing_ok=True)
    movement = out / "menu-movement.json"
    movement.write_text(json.dumps([dict(From=0, To=1000, Keys=["D", "T", "U"])]))
    hashes = []
    for fps in (60, 144):
        value = run(
            f"menu-background-{fps}",
            [
                "--no-intro", "--map", "4", "--record", str(record),
                "--render-fps", str(fps), "--frames", str(fps * 3),
            ] + (["--input-script", str(movement)] if fps == 144 else []),
        )
        assert (
            value["Page"] == "Main" and value["MenuBackground"] and value["Map"] == "2DownSmash"
            and value["Players"] == 4 and value["TickNumber"] == 360 and value["Phase"] == "Playing"
        ), value
        hashes.append(value["Hash"])
    assert len(set(hashes)) == 1, "Menu CPUs depend on keyboard input or render rate"
    assert not record.exists() and not record.with_name(record.name + ".json").exists()

    watch = out / "menu-watch-cpu-input.json"
    keys = [(frame, "Down") for frame in (1, 3, 5, 7)] + [(11, "Enter"), (120, "Escape"), (122, "Q")]
    watch.write_text(json.dumps([dict(From=frame, To=frame + 1, Keys=[key]) for frame, key in keys]))
    options = ["--no-intro", "--map", "4", "--input-script", str(watch)]
    value = run("menu-watch-cpu", options + ["--frames", "100"], sound=True)
    assert value["Page"] == "Playing" and not value["MenuBackground"] and value["Map"] == "5Skyline", value
    value = run("menu-return", options + ["--frames", "180"], sound=True)
    assert value["Page"] == "Main" and value["MenuBackground"] and value["Map"] == "2DownSmash", value


def verify_font_settings():
    for mode, edge in [(0, 0), (1, 0.5), (2, 1)]:
        settings_file = Path(env["XDG_DATA_HOME"]) / "FrogSmashersRebuilt/settings.json"
        settings = json.loads(settings_file.read_text()) if settings_file.exists() else {}
        settings["FontSmoothing"] = (mode - 1) % 3
        settings_file.parent.mkdir(parents=True, exist_ok=True)
        settings_file.write_text(json.dumps(settings))
        keys = [
            (1, ["Down"]),
            (3, ["Down"]),
            (5, ["Enter"]),
            (7, ["Up"]),
            (9, ["Up"]),
            (11, ["Up"]),
            (13, ["Up"]),
            (15, ["Right"]),
            (17, ["Down"]),
            (19, ["Enter"]),
        ]
        font_script = out / "font-settings-input.json"
        font_script.write_text(json.dumps([dict(From=a, To=a + 1, Keys=k) for a, k in keys]))
        value = run(f"font-mode-{mode}", ["--no-intro", "--input-script", str(font_script), "--frames", "24"])
        assert (
            value["Page"] == "Bindings" and value["FontSmoothing"] == mode and value["TextEdgeWidth"] == edge
        ), value
        keys += [(25, ["Escape"]), (27, ["Down"]), (29, ["Enter"]), (31, ["Escape"]), (33, ["Escape"])]
        font_script.write_text(json.dumps([dict(From=a, To=a + 1, Keys=k) for a, k in keys]))
        value = run(f"font-save-{mode}", ["--no-intro", "--input-script", str(font_script), "--frames", "36"])
        assert (
            value["Page"] == "Main" and json.loads(settings_file.read_text())["FontSmoothing"] == mode
        ), value
        value = run(f"font-reopen-{mode}", ["--no-intro", "--frames", "2"])
        assert value["FontSmoothing"] == mode and value["TextEdgeWidth"] == edge, value


def verify_local_replay():
    rows = [
        (1, 2, ["Enter"]),
        (3, 4, ["Space"]),
        (5, 6, ["RightShift"]),
        (7, 8, ["Tab"]),
        (9, 10, ["Down"]),
        (11, 12, ["Down"]),
        (13, 14, ["Right"]),
        (15, 16, ["Escape"]),
        (17, 18, ["Enter"]),
        (50, 100, ["D", "T"]),
        (80, 140, ["Left", "M"]),
        (150, 190, ["U"]),
        (200, 201, ["Escape"]),
        (203, 204, ["Escape"]),
    ]
    script = out / "local-input.json"
    script.write_text(json.dumps([dict(From=a, To=b, Keys=c) for a, b, c in rows]))
    record = out / "local-match.fsr"
    local = run(
        "local-flow",
        [
            "--no-intro",
            "--input-script",
            str(script),
            "--frames",
            "240",
            "--capture",
            str(out / "local-flow.png"),
            "--record",
            str(record),
        ],
        sound=True,
    )
    assert local["Page"] == "Playing" and local["Players"] == 2 and local["TickNumber"] > 300
    assert not local["MenuBackground"]
    assert json.loads(record.with_name(record.name + ".json").read_text())["Rules"]["WinScore"] != 2147483647
    replay = run(
        "local-replay", ["--replay", str(record), "--frames", "300", "--capture", str(out / "replay.png")]
    )
    assert replay["Hash"] == local["Hash"] and replay["TickNumber"] == local["TickNumber"]
    return record


def verify_replay_exit(record):
    rows = [
        (1, 2, ["Escape"]),
        (3, 4, ["Q"]),
        (5, 6, ["Enter"]),
        (7, 8, ["Space"]),
        (9, 10, ["RightShift"]),
        (11, 12, ["Enter"]),
    ]
    script = out / "replay-to-local-input.json"
    script.write_text(json.dumps([dict(From=a, To=b, Keys=c) for a, b, c in rows]))
    restarted = run(
        "replay-to-local", ["--replay", str(record), "--input-script", str(script), "--frames", "100"]
    )
    assert restarted["Page"] == "Playing" and restarted["Players"] == 2 and restarted["TickNumber"] > 100


def verify_arenas():
    for index in range(7):
        run(
            f"arena-{index}",
            [
                "--demo",
                "--players",
                "8",
                "--map",
                str(index),
                "--frames",
                "300",
                "--capture",
                str(out / f"arena-{index}.png"),
            ],
        )
    run(
        "five-player-hud",
        ["--demo", "--players", "5", "--frames", "180", "--capture", str(out / "five-player-hud.png")],
    )
    run("intro", ["--frames", "600", "--capture", str(out / "intro.png")], sound=True)
    run("title", ["--frames", "1680", "--capture", str(out / "title.png")], sound=True)


def verify_network():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
        reserve.bind(("127.0.0.1", 0))
        port = reserve.getsockname()[1]
    common = ["--demo", "--local-players", "2", "--ticks", "480", "--port", str(port)]
    host = subprocess.Popen(
        command("udp-host", common + ["--host", "udp", "--peers", "2"]),
        cwd=root,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    peer = subprocess.Popen(
        command("udp-peer", common + ["--join", "udp:127.0.0.1"]),
        cwd=root,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    try:
        hostlog, _ = host.communicate(timeout=45)
        peerlog, _ = peer.communicate(timeout=45)
    finally:
        for process in (host, peer):
            if process.poll() is None:
                process.kill()
                process.wait()
    (out / "udp-host.log").write_text(hostlog)
    (out / "udp-peer.log").write_text(peerlog)
    assert host.returncode == peer.returncode == 0, (hostlog, peerlog)
    a = json.loads((out / "udp-host.json").read_text())
    b = json.loads((out / "udp-peer.json").read_text())
    assert a["TickNumber"] == b["TickNumber"] == 480 and a["ConfirmedFrame"] == b["ConfirmedFrame"] == 479, (
        a,
        b,
    )
    assert a["Hash"] == b["Hash"], (a, b)
    print("Rendered UDP peers converged:", a["Hash"], flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--network", action="store_true", help="also launch two real rendered UDP processes")
    parser.add_argument("--quick", action="store_true", help="skip the seven-arena capture sweep")
    args = parser.parse_args()
    out.mkdir(parents=True, exist_ok=True)
    Path(env["XDG_DATA_HOME"]).mkdir(parents=True, exist_ok=True)
    settings_file = Path(env["XDG_DATA_HOME"]) / "FrogSmashersRebuilt/settings.json"
    settings_file.parent.mkdir(parents=True, exist_ok=True)
    settings_file.write_text(
        json.dumps(dict(Width=1280, Height=720, Fullscreen=False, VSync=False, FirstMap=0, FontSmoothing=0))
    )
    verify_render_cadence()
    pause_script = verify_pause()
    verify_smoke_pause(pause_script)
    verify_title_menu()
    verify_menu_background()
    verify_font_settings()
    record = verify_local_replay()
    verify_replay_exit(record)
    if not args.quick:
        verify_arenas()
    if args.network:
        verify_network()
    print("PASS: client input, replay and render cadence checks; captures in", out)


if __name__ == "__main__":
    main()
