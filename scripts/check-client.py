#!/usr/bin/env python3
"""Exercise the actual MonoGame client, fixed/render clock split, pause, replays and UDP convergence."""
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
    (out / (name + ".json")).unlink(missing_ok=True)
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
    print(f"{name}: tick {value['TickNumber']}, hash {value['Hash']}", flush=True)
    return value


def verify_render_cadence():
    hashes = []
    for fps in (60, 100, 120, 144, 165, 240):
        value = run(
            f"timing-{fps}", ["--demo", "--players", "8", "--frames", str(fps * 4), "--render-fps", str(fps)]
        )
        assert value["TickNumber"] == value["TickRate"] * 4
        hashes.append(value["Hash"])
    assert len(set(hashes)) == 1, hashes


def verify_pause():
    pause_script = out / "pause-input.json"
    pause_script.write_text(
        json.dumps(
            [
                dict(From=120, To=121, Keys=["Escape"]),
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
                ],
            )
        )
    assert pause_results[0]["Paused"] and pause_results[1]["Paused"] and not pause_results[2]["Paused"]
    assert (
        pause_results[0]["Hash"] == pause_results[1]["Hash"]
        and pause_results[0]["TickNumber"] == pause_results[1]["TickNumber"]
    )
    assert pause_results[2]["TickNumber"] > pause_results[1]["TickNumber"]


def verify_local_replay():
    record = out / "local-match.fsr"
    record.unlink(missing_ok=True)
    record.with_name(record.name + ".json").unlink(missing_ok=True)
    local = run("local-record", ["--demo", "--players", "8", "--ticks", "400", "--record", str(record)], sound=True)
    assert record.exists(), "The client did not save its recording"
    replay = run("local-replay", ["--replay", str(record), "--frames", "300"])
    assert replay["Hash"] == local["Hash"] and replay["TickNumber"] == local["TickNumber"], (local, replay)


def verify_network():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
        reserve.bind(("127.0.0.1", 0))
        port = reserve.getsockname()[1]
    common = ["--demo", "--local-players", "2", "--ticks", "480", "--port", str(port)]
    host = subprocess.Popen(
        command("udp-host", common + ["--host", "udp", "--slots", "8", "--start-players", "4"]),
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
    args = parser.parse_args()
    out.mkdir(parents=True, exist_ok=True)
    settings_file = Path(env["XDG_DATA_HOME"]) / "FrogSmashersRebuilt/settings.json"
    settings_file.parent.mkdir(parents=True, exist_ok=True)
    settings_file.write_text(json.dumps(dict(Fullscreen=False, VSync=False)))
    verify_render_cadence()
    verify_pause()
    verify_local_replay()
    if args.network:
        verify_network()
    print("PASS: client timing, pause and replay checks; results in", out)


if __name__ == "__main__":
    main()
