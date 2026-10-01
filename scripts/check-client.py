#!/usr/bin/env python3
"""Exercise the actual MonoGame client, fixed/render clock split, menus and input replays."""
from pathlib import Path
import argparse
import json
import os
import socket
import subprocess
from PIL import Image, ImageChops

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


def verify_paused_pixels(first, second):
    with Image.open(out / first) as a, Image.open(out / second) as b:
        difference = ImageChops.difference(a.convert("RGB"), b.convert("RGB"))
        for left, right in ((440, 472), (808, 840)):
            difference.paste((0, 0, 0), (left, 252, right, 296))
        assert difference.getbbox() is None, "Presentation changes outside the animated pause arrows"


def verify_pause():
    pause_script = out / "pause-input.json"
    pause_script.write_text(
        json.dumps(
            [
                dict(From=120, To=121, Keys=["Escape"]),
                dict(From=130, To=131, Keys=["Down"]),
                dict(From=140, To=141, Keys=["Enter"]),
                dict(From=200, To=201, Keys=["Escape"]),
                dict(From=210, To=211, Keys=["Up"]),
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
    verify_paused_pixels("pause-start.png", "pause-held.png")
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
    verify_paused_pixels("smoke-pause-start.png", "smoke-pause-held.png")


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
    keys = [(frame, "Down") for frame in (1, 3, 5)] + [(7, "Enter"), (9, "Down"), (11, "Enter"), (120, "Escape"), (122, "Down"), (124, "Down"), (126, "Down"), (128, "Enter")]
    watch.write_text(json.dumps([dict(From=frame, To=frame + 1, Keys=[key]) for frame, key in keys]))
    options = ["--no-intro", "--map", "4", "--input-script", str(watch)]
    value = run("menu-watch-cpu", options + ["--frames", "100"], sound=True)
    assert value["Page"] == "Playing" and not value["MenuBackground"] and value["Map"] == "5Skyline", value
    value = run("menu-return", options + ["--frames", "180"], sound=True)
    assert value["Page"] == "Main" and value["MenuBackground"] and value["Map"] == "2DownSmash", value


def verify_local_replay():
    rows = [
        (1, 2, ["Enter"]), (3, 4, ["Space"]), (5, 6, ["Space"]),
        (7, 8, ["RightShift"]), (9, 10, ["RightShift"]), (11, 12, ["Escape"]),
        (13, 14, ["Down"]), (15, 16, ["Enter"]),
        (50, 100, ["D", "T"]), (80, 140, ["Left", "M"]), (150, 190, ["U"]),
        (200, 201, ["Escape"]), (203, 204, ["Escape"]),
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
    keys = [(1, "Escape"), (3, "Down"), (5, "Down"), (7, "Enter"),
            (9, "Space"), (11, "Space"), (13, "RightShift"), (15, "RightShift"),
            (17, "Escape"), (19, "Down"), (21, "Enter")]
    rows = [(frame, frame + 1, [key]) for frame, key in keys]
    script = out / "replay-to-local-input.json"
    script.write_text(json.dumps([dict(From=a, To=b, Keys=c) for a, b, c in rows]))
    restarted = run(
        "replay-to-local", ["--replay", str(record), "--input-script", str(script), "--frames", "100"]
    )
    assert restarted["Page"] == "Playing" and restarted["Players"] == 2 and restarted["TickNumber"] > 100


def verify_lobby_menus():
    settings_file = Path(env["XDG_DATA_HOME"]) / "FrogSmashersRebuilt/settings.json"
    def reset():
        settings_file.write_text(json.dumps(dict(VSync=False, Volume=0.63)))
    def key(frame, *keys):
        return dict(From=frame, To=frame + 1, Keys=list(keys))
    def pad(frame, index, *buttons):
        return dict(From=frame, To=frame + 1, Pads={str(index): list(buttons)})
    def click(frame, x, y):
        return dict(From=frame, To=frame + 1, MouseX=x, MouseY=y, MouseDown=True)
    def capture(name, rows, frames):
        reset()
        path = out / (name + "-input.json")
        path.write_text(json.dumps(rows))
        return run(name, ["--no-intro", "--input-script", str(path), "--frames", str(frames), "--capture", str(out / (name + ".png"))])

    joining = [click(1, 640, 357), key(3, "Space"), key(5, "Y"), key(9, "Space"), key(11, "RightShift"), key(13, "RightShift")]
    choosing = capture("lobby-color", joining, 8)
    assert choosing["Page"] == "Seats" and choosing["LocalDevices"] == [], choosing
    assert 0 <= choosing["LobbySlots"][0]["Player"]["Color"] < 8 and not choosing["LobbyPlayers"][0]["Alive"], choosing
    spawned = capture("lobby-spawn", joining, 60)
    assert spawned["LocalDevices"] == [0, 1] and sum(p["Alive"] for p in spawned["LobbyPlayers"]) == 2, spawned
    walking = capture("lobby-walk", joining + [dict(From=30, To=60, Keys=["D"])], 60)
    assert walking["LobbyPlayers"][0]["X"] > spawned["LobbyPlayers"][0]["X"] + 1, walking
    assert walking["LobbyTick"] == spawned["LobbyTick"], walking

    platform = [key(1, "Enter"), key(3, "Space"), key(5, "Space"), key(30, "T")]
    jumping = capture("lobby-platform-jump", platform, 33)
    assert not jumping["LobbyPlayers"][0]["CanChooseAgain"], jumping
    landed = capture("lobby-platform-land", platform, 120)
    assert landed["LobbyPlayers"][0]["CanChooseAgain"], landed
    choosing_again = platform + [key(122, "Space")]
    changed_color = capture("lobby-choose-again", choosing_again, 124)
    assert changed_color["Page"] == "Seats" and not changed_color["LobbySlots"][0]["Player"]["Spawned"], changed_color
    assert not changed_color["LobbyPlayers"][0]["Alive"], changed_color
    backed_out = capture("lobby-color-backout", choosing_again + [key(126, "U")], 128)
    assert backed_out["LobbySlots"][0]["Player"] is None and backed_out["LocalDevices"] == [], backed_out
    rejoined = capture("lobby-rejoin", choosing_again + [key(126, "U"), key(130, "Space"), key(132, "Space")], 136)
    assert rejoined["LocalDevices"] == [0] and rejoined["LobbyPlayers"][0]["Alive"], rejoined
    # Start offers color selection on the pad owner's platform and pauses elsewhere.
    controller = [key(1, "Enter"), pad(3, 0, "Start"), pad(5, 0, "Start"), pad(30, 0, "Start")]
    pad_choosing = capture("lobby-pad-choose-again", controller, 32)
    assert pad_choosing["Page"] == "Seats" and not pad_choosing["LobbySlots"][0]["Player"]["Spawned"], pad_choosing
    pad_backed_out = capture("lobby-pad-backout", controller + [pad(34, 0, "X")], 36)
    assert pad_backed_out["LobbySlots"][0]["Player"] is None, pad_backed_out

    settings = joining + [key(15, "Escape")] + [key(frame, "S") for frame in (17, 19, 21, 23)] + [key(25, "Enter")]
    settings += [key(frame, "S") for frame in (27, 29, 31)] + [key(33, "D")]
    personal = capture("personal-settings", settings, 35)
    assert personal["Page"] == "Settings" and abs(personal["Volume"] - .70) < .001, personal
    assert not any("TEAMS" in row or "WIN SCORE" in row or "MACHINES" in row for row in personal["MenuItems"]), personal
    # Mouse actions still work, and returning from personal settings preserves the party.
    returned = capture("return-to-lobby", settings + [click(37, 640, 490), key(39, "Escape")], 50)
    assert returned["Page"] == "Seats" and returned["LocalDevices"] == [0, 1], returned

    cpu = [key(1, "Enter"), key(3, "Space"), key(5, "Space"), key(7, "Escape")]
    cpu += [key(frame, "Down") for frame in (9, 11, 13)]
    cpu += [key(15, "Enter"), key(17, "Left"), key(19, "Up")]
    edge = capture("slot-edge", cpu, 21)
    assert edge["Page"] == "SlotEditor" and edge["SelectedSeat"] == 0, edge
    cpu += [key(23, "Right"), key(25, "Enter"), key(27, "Right"), key(29, "Right")]
    added = capture("slot-cpu", cpu, 31)
    assert added["Page"] == "SlotOptions" and added["LobbySlots"][1]["Type"] == 2 and added["LocalDevices"] == [0, -1], added
    cpu += [key(33, "Down"), key(35, "Enter")]
    closed = capture("slot-closed", cpu, 37)
    assert closed["Page"] == "SlotOptions" and not closed["LobbySlots"][1]["Open"] and closed["LocalDevices"] == [0], closed
    quit_lobby = cpu + [key(frame, "Escape") for frame in (39, 41, 43, 45)] + [key(47, "Up"), key(49, "Enter")]
    left = capture("quit-lobby", quit_lobby, 51)
    assert left["Page"] == "Main" and left["LocalDevices"] == [], left
    fresh = capture("fresh-lobby", quit_lobby + [key(53, "Enter")], 56)
    assert fresh["Page"] == "Seats" and fresh["LobbyTick"] < 10 and fresh["LocalDevices"] == [], fresh
    assert all(slot["Open"] and slot["Type"] == 0 and slot["Player"] is None for slot in fresh["LobbySlots"]), fresh
    assert not any(player["Alive"] for player in fresh["LobbyPlayers"]) and fresh["LobbySpawnPuffs"] == 0, fresh
    cpu += [key(39, "Enter"), key(41, "Escape"), key(43, "Escape"), key(45, "Up"), key(47, "Up"), key(49, "Enter")]
    started = capture("cpu-start-slot", cpu, 54)
    assert started["Page"] == "Playing" and started["LocalDevices"] == [0, -1] and started["Players"] == 2, started

    occupied = [key(1, "Enter"), key(3, "Space"), key(5, "Space"), key(7, "Escape")]
    occupied += [key(frame, "Down") for frame in (9, 11, 13)]
    occupied += [key(15, "Enter"), key(17, "Enter"), key(19, "Down"), key(21, "Right"), key(23, "Right"), key(25, "Right")]
    changed = capture("slot-replace-player", occupied, 27)
    assert changed["Page"] == "SlotOptions" and changed["Selected"] == 0, changed
    assert changed["LobbySlots"][0]["Type"] == 0 and changed["LobbySlots"][0]["Player"] is None, changed

    room_edges = [key(1, "Enter"), key(3, "Escape"), key(5, "Down"), key(7, "Down"), key(9, "Down"), key(11, "Enter")]
    room_edges += [key(13, "Right"), key(15, "Right"), key(17, "Right"), key(19, "Down"), key(21, "Down"), key(23, "Down")]
    bottom = capture("slot-bottom-edge", room_edges, 25)
    assert bottom["SelectedSeat"] == 7, bottom
    room_edges += [key(27, "Up"), key(29, "Left")]
    across = capture("slot-skip-center", room_edges, 31)
    assert across["SelectedSeat"] == 3, across

    preview = [key(1, "Enter"), pad(3, 0, "Start"), pad(5, 1, "Start"), pad(7, 0, "Start"), pad(8, 0, "A"), pad(9, 0, "Start"), pad(11, 1, "B")]
    paused_preview = capture("lobby-pending-paused", preview, 13)
    assert paused_preview["Page"] == "LobbyMenu" and paused_preview["Owner"] == 2, paused_preview
    assert not paused_preview["LobbySlots"][1]["Player"]["Spawned"] and paused_preview["LobbySpawnPuffs"] >= 3, paused_preview

    rows = [key(1, "Enter"), pad(3, 0, "Start"), pad(5, 0, "Start"), pad(7, 1, "Start"), pad(9, 1, "Start"), pad(10, 0, "A"), pad(11, 0, "Start"),
            key(13, "Down", "Enter"), pad(15, 1, "DPadDown", "A"), click(17, 640, 165)]
    owned = capture("lobby-menu-owner", rows, 19)
    assert owned["Page"] == "LobbyMenu" and owned["Owner"] == 2 and owned["Selected"] == 0 and owned["LocalDevices"] == [2, 3], owned
    assert not any("COLOR / TEAM" in row for row in owned["MenuItems"]), owned
    rows += [pad(21, 0, "DPadDown"), pad(23, 0, "A"), pad(31, 1, "Start"), key(33, "Escape"), pad(33, 0, "B"), pad(35, 1, "DPadDown"), pad(37, 1, "A")]
    playing = capture("controller-start", rows, 29)
    assert playing["Page"] == "Playing" and playing["Players"] == 2 and not playing["Paused"], playing
    owned = capture("match-menu-owner", rows, 39)
    assert owned["Page"] == "Settings" and owned["Owner"] == 3 and owned["Paused"], owned
    resumed = capture("controller-resume", rows + [pad(41, 1, "B"), pad(43, 1, "B")], 46)
    assert resumed["Page"] == "Playing" and resumed["Owner"] is None and not resumed["Paused"], resumed


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


def verify_lobby_network():
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as reserve:
        reserve.bind(('127.0.0.1',0)); port=reserve.getsockname()[1]
    keys=[(120,'Escape'),(122,'Down'),(124,'Down'),(126,'Down'),(128,'Enter'),(130,'Down'),(132,'Down'),(134,'Enter'),(136,'Down'),(138,'Enter'),(140,'Escape'),(142,'Right'),(144,'Enter'),(146,'Down'),(148,'Enter'),(150,'Escape'),(152,'Escape'),(154,'Escape')]
    script=out/'online-capacity-input.json'
    script.write_text(json.dumps([dict(From=f,To=f+1,Keys=[k]) for f,k in keys]))
    base=['--local-players','2','--port',str(port)]
    processes=[]
    for name,args in [
        ('forming-host',base+['--host','udp','--slots','8','--frames','260','--input-script',str(script)]),
        ('forming-peer',base+['--join','udp:127.0.0.1','--frames','290'])]:
        args+=['--capture',str(out/(name+'.png'))]
        processes.append((name,subprocess.Popen(command(name,args),cwd=root,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)))
    try:
        for name,p in processes:
            log,_=p.communicate(timeout=60);(out/(name+'.log')).write_text(log)
            assert p.returncode==0,(name,log)
        values=[json.loads((out/(n+'.json')).read_text()) for n,_ in processes]
        for value in values:
            assert value['Page']=='Seats' and value['Error'] is None,value
            assert sum(s['Open'] for s in value['LobbySlots'])==6,value
        peer=values[1]
        assert sum(s['Player'] is not None for s in peer['LobbySlots'])==4,peer
        assert sum(p['Alive'] for p in peer['LobbyPlayers'])==4,peer
        assert [p['ColorIndex'] for p in peer['LobbyPlayers'][:4]]==[0,1,2,3],peer
        print('Shared playable UDP lobby: four frogs; edited capacity six reached the connected client; neither match started automatically.')
    finally:
        for _,p in processes:
            if p.poll() is None:p.kill();p.wait()


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
    parser.add_argument("--quick", action="store_true", help="skip the seven-arena capture sweep")
    args = parser.parse_args()
    out.mkdir(parents=True, exist_ok=True)
    Path(env["XDG_DATA_HOME"]).mkdir(parents=True, exist_ok=True)
    settings_file = Path(env["XDG_DATA_HOME"]) / "FrogSmashersRebuilt/settings.json"
    settings_file.parent.mkdir(parents=True, exist_ok=True)
    settings_file.write_text(
        json.dumps(dict(Fullscreen=False, VSync=False, MatchDefaults=dict(FirstMap=0)))
    )
    verify_render_cadence()
    pause_script = verify_pause()
    verify_smoke_pause(pause_script)
    verify_title_menu()
    verify_menu_background()
    record = verify_local_replay()
    verify_replay_exit(record)
    verify_lobby_menus()
    if not args.quick:
        verify_arenas()
    if args.network:
        verify_lobby_network()
        verify_network()
    print("PASS: client input, replay and render cadence checks; captures in", out)


if __name__ == "__main__":
    main()
