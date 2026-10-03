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


def run(name, options, sound=False, expect_error=False):
    result = subprocess.run(
        command(name, options, sound), cwd=root, env=env, text=True, capture_output=True, timeout=90
    )
    (out / (name + ".log")).write_text(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError(f"{name} exited {result.returncode}: {result.stdout}\n{result.stderr}")
    value = json.loads((out / (name + ".json")).read_text())
    assert (value["Error"] is not None) == expect_error, value
    print(name, value, flush=True)
    return value


def verify_connections_and_repeat():
    settings_file = Path(env["XDG_DATA_HOME"]) / "FrogSmashersRebuilt/settings.json"
    initial_settings = settings_file.read_bytes()
    def key(frame, *keys):
        return dict(From=frame, To=frame + 1, Keys=list(keys))

    def capture(name, inputs, frames, options=None):
        settings_file.write_bytes(initial_settings)
        script = out / (name + "-input.json")
        script.write_text(json.dumps(inputs))
        return run(name, (options or ["--no-intro"]) + [
            "--input-script", str(script), "--frames", str(frames),
            "--capture", str(out / (name + ".png"))])

    held = [dict(From=0, To=80, Keys=["Tab"])]
    shown = capture("connections-eight", held, 60, ["--demo", "--players", "8"])
    hidden = capture("connections-released", held, 90, ["--demo", "--players", "8"])
    assert shown["ConnectionsVisible"] and len(shown["ConnectionPlayers"]) == 8, shown
    assert shown["TickNumber"] == 120 and not hidden["ConnectionsVisible"], (shown, hidden)
    lobby = [key(1, "Enter"), key(3, "U"), key(5, "OemPeriod"), dict(From=7, To=30, Keys=["Tab"])]
    shown = capture("connections-lobby", lobby, 25)
    assert shown["Page"] == "Seats" and len(shown["ConnectionPlayers"]) == 2, shown
    assert all(p["Prediction"] == 0 and p["Ping"] == 0 for p in shown["ConnectionPlayers"]), shown
    with Image.open(out / "connections-lobby.png") as panel:
        first = panel.crop((686, 326, 862, 344))
        second = panel.crop((686, 370, 862, 388))
        assert ImageChops.difference(first, second).getbbox() is None, "Lobby hints obscure connection text"
    editor = [key(1, "Enter"), key(3, "Escape"), key(5, "Down"), key(7, "Down"),
              key(9, "Enter"), dict(From=11, To=30, Keys=["Tab"])]
    edited = capture("connections-slot-editor", editor, 25)
    assert edited["Page"] == "SlotEditor" and not edited["ConnectionsVisible"], edited

    settings = [key(1, "Down"), key(3, "Down"), key(5, "Enter")]
    volume = settings + [key(7, "Down"), key(9, "Down"), key(11, "Down")]
    for name, action in [("key", dict(Keys=["Left"])), ("pad", dict(Pads={0: ["DPadLeft"]}))]:
        changed = capture("repeat-volume-" + name,
                          volume + [dict(From=13, To=110, **action)], 108)
        assert changed["Volume"] == 0, changed
    toggled = capture("no-repeat-toggle", settings + [key(7, "Down"), dict(From=9, To=90, Keys=["Right"])], 95)
    assert "VSYNC: ON" in toggled["MenuItems"], toggled
    rollback = settings + [key(frame, "Down") for frame in (7, 9, 11, 13, 15, 17, 19)] + [key(21, "Enter")]
    adjusted = capture("repeat-rollback", rollback + [dict(From=23, To=80, Keys=["Right"])], 85)
    assert adjusted["Page"] == "Rollback" and len(adjusted["MenuItems"]) == 4, adjusted
    assert adjusted["MenuItems"][0] != "DELAY: 16.7 MS", adjusted
    settings_file.write_bytes(initial_settings)
    print("PASS: Tab visibility, eight-player layout, local lobby, slot editor, numeric repeat and single-press toggles.")


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
            difference.paste((0, 0, 0), (left, 310, right, 336))
        assert difference.getbbox() is None, "Presentation changes outside the animated pause arrows"


def verify_pause():
    pause_script = out / "pause-input.json"
    pause_script.write_text(
        json.dumps(
            [
                dict(From=120, To=121, Keys=["Escape"]),
                dict(From=140, To=141, Keys=["Enter"]),
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
    for name, keys, expected, options in [
        ("title-only", [(1, 2, ["Enter"])], "Title", []),
        ("title-to-menu", [(1, 2, ["Enter"]), (5, 6, ["Enter"])], "Main", []),
        ("title-map-to-menu", [(1, 2, ["Enter"]), (5, 6, ["Enter"])], "Main", ["--map", "5Skyline"]),
    ]:
        title_script = out / (name + "-input.json")
        title_script.write_text(json.dumps([dict(From=a, To=b, Keys=c) for a, b, c in keys]))
        value = run(
            name,
            options + ["--input-script", str(title_script), "--frames", "60", "--capture", str(out / (name + ".png"))],
        )
        assert value["Page"] == expected, value
        if expected == "Main":
            assert value["MenuBackground"] and value["Map"] == "2DownSmash" and value["Players"] == 4, value
        assert value["MatchSettings"]["FirstMap"] == (4 if options else 0), value


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
            and value["MatchSettings"]["FirstMap"] == 4
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
    assert value["MatchSettings"]["FirstMap"] == 4, value


def verify_local_replay():
    rows = [
        (1, 2, ["Enter"]), (3, 4, ["U"]), (5, 6, ["U"]),
        (7, 8, ["OemPeriod"]), (9, 10, ["OemPeriod"]), (11, 12, ["Escape"]),
        (15, 16, ["Enter"]),
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
            (9, "U"), (11, "U"), (13, "OemPeriod"), (15, "OemPeriod"),
            (17, "Escape"), (21, "Enter")]
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

    joining = [click(1, 640, 357), key(3, "U"), key(5, "Y"), key(9, "U"), key(11, "OemPeriod"), key(13, "OemPeriod")]
    choosing = capture("lobby-color", joining, 8)
    assert choosing["Page"] == "Seats" and choosing["LocalDevices"] == [], choosing
    assert 0 <= choosing["LobbySlots"][0]["Player"]["Color"] < 8 and not choosing["LobbyPlayers"][0]["Alive"], choosing
    spawned = capture("lobby-spawn", joining, 60)
    assert spawned["LocalDevices"] == [0, 1] and sum(p["Alive"] for p in spawned["LobbyPlayers"]) == 2, spawned
    walking = capture("lobby-walk", joining + [dict(From=30, To=60, Keys=["D"])], 60)
    assert walking["LobbyPlayers"][0]["X"] > spawned["LobbyPlayers"][0]["X"] + 1, walking
    assert walking["LobbyTick"] == spawned["LobbyTick"], walking

    platform = [key(1, "Enter"), key(3, "U"), key(5, "U"), key(30, "T")]
    jumping = capture("lobby-platform-jump", platform, 33)
    assert not jumping["LobbyPlayers"][0]["CanChooseAgain"], jumping
    landed = capture("lobby-platform-land", platform, 120)
    assert landed["LobbyPlayers"][0]["CanChooseAgain"], landed
    choosing_again = platform + [key(122, "U")]
    changed_color = capture("lobby-choose-again", choosing_again, 124)
    assert changed_color["Page"] == "Seats" and not changed_color["LobbySlots"][0]["Player"]["Spawned"], changed_color
    assert not changed_color["LobbyPlayers"][0]["Alive"], changed_color
    backed_out = capture("lobby-color-backout", choosing_again + [key(126, "T")], 128)
    assert backed_out["LobbySlots"][0]["Player"] is None and backed_out["LocalDevices"] == [], backed_out
    rejoined = capture("lobby-rejoin", choosing_again + [key(126, "T"), key(130, "U"), key(132, "U")], 136)
    assert rejoined["LocalDevices"] == [0] and rejoined["LobbyPlayers"][0]["Alive"], rejoined
    # Attack joins/spawns and offers color selection on the starting platform; Start always opens the menu.
    controller = [key(1, "Enter"), pad(3, 0, "X"), pad(5, 0, "X"), pad(30, 0, "X")]
    pad_choosing = capture("lobby-pad-choose-again", controller, 32)
    assert pad_choosing["Page"] == "Seats" and not pad_choosing["LobbySlots"][0]["Player"]["Spawned"], pad_choosing
    pad_backed_out = capture("lobby-pad-backout", controller + [pad(34, 0, "A")], 36)
    assert pad_backed_out["LobbySlots"][0]["Player"] is None, pad_backed_out

    settings = joining + [key(15, "Escape")] + [key(frame, "S") for frame in (17, 19, 21)] + [key(25, "Enter")]
    settings += [key(frame, "S") for frame in (27, 29, 31)] + [key(33, "D")]
    personal = capture("personal-settings", settings, 35)
    assert personal["Page"] == "Settings" and abs(personal["Volume"] - .70) < .001, personal
    assert not any("TEAMS" in row or "WIN SCORE" in row or "MACHINES" in row for row in personal["MenuItems"]), personal
    # Mouse actions still work, and returning from personal settings preserves the party.
    returned = capture("return-to-lobby", settings + [click(37, 640, 449), key(39, "Escape")], 50)
    assert returned["Page"] == "Seats" and returned["LocalDevices"] == [0, 1], returned

    cpu = [key(1, "Enter"), key(3, "U"), key(5, "U"), key(7, "Escape")]
    cpu += [key(frame, "Down") for frame in (9, 11)]
    cpu += [key(15, "Enter"), key(17, "Left"), key(19, "Up")]
    edge = capture("slot-edge", cpu, 21)
    assert edge["Page"] == "SlotEditor" and edge["SelectedSeat"] == 0, edge
    cpu += [key(23, "Right"), dict(From=25, To=30, Keys=["Enter"]), key(27, "Left")]
    added = capture("slot-cpu", cpu, 31)
    assert added["Page"] == "SlotEditor" and added["LobbySlots"][1]["Type"] == 5 and added["LocalDevices"] == [0, -1], added
    cycled = capture("slot-cycle-away-from-cpu", cpu + [key(33, "Enter")], 35)
    assert cycled["LobbySlots"][1]["Type"] == 1 and cycled["LobbySlots"][1]["Player"] is None, cycled
    all_cpus = capture("slot-apply-cpu", cpu + [key(33, "Tab")], 35)
    assert sum(slot["Player"] is not None and slot["Player"]["Cpu"] for slot in all_cpus["LobbySlots"]) == 7, all_cpus
    all_local = capture("slot-apply-local", cpu + [key(33, "Tab"), key(35, "Enter"), key(37, "Tab")], 39)
    assert all(slot["Type"] == 1 for slot in all_local["LobbySlots"]) and all_local["LocalDevices"] == [0], all_local
    cpu += [key(33, "U")] + [key(frame, "Enter") for frame in (35, 37, 39, 41)]
    local = capture("slot-local", cpu, 43)
    assert local["Page"] == "SlotEditor" and local["LobbySlots"][1]["Type"] == 1 and local["LocalDevices"] == [0], local
    quit_lobby = cpu + [key(frame, "Escape") for frame in (45, 47, 49)] + [key(51, "Up"), key(53, "Enter")]
    left = capture("quit-lobby", quit_lobby, 54)
    assert left["Page"] == "Main" and left["LocalDevices"] == [], left
    fresh = capture("fresh-lobby", quit_lobby + [key(55, "Enter")], 58)
    assert fresh["Page"] == "Seats" and fresh["LobbyTick"] < 10 and fresh["LocalDevices"] == [], fresh
    assert all(slot["Open"] and slot["Type"] == 1 and slot["Player"] is None for slot in fresh["LobbySlots"]), fresh
    assert not any(player["Alive"] for player in fresh["LobbyPlayers"]) and fresh["LobbySpawnPuffs"] == 0, fresh
    cpu += [key(45, "Enter"), key(47, "Escape"), key(49, "Up"), key(51, "Up"), key(53, "Enter")]
    started = capture("cpu-start-slot", cpu, 60)
    assert started["Page"] == "Playing" and started["LocalDevices"] == [0, -1] and started["Players"] == 2, started

    occupied = [key(1, "Enter"), key(3, "U"), key(5, "U"), key(7, "Escape")]
    occupied += [key(frame, "Down") for frame in (9, 11)]
    occupied += [key(15, "Enter"), key(17, "Enter")]
    unchanged = capture("slot-occupied", occupied, 19)
    assert unchanged["Page"] == "SlotEditor" and unchanged["LobbySlots"][0]["Player"]["Id"] == 0, unchanged
    changed = capture("slot-backout", occupied + [key(21, "U")], 23)
    assert changed["LobbySlots"][0]["Player"] is None, changed

    for controllers, expected in ((1, [0, 1, 2]), (7, [0, 2, 3, 4, 5, 6, 7, 8]), (8, list(range(2, 10)))):
        hints = capture(f"join-hints-{controllers}", [key(1, "Enter"), dict(From=0, To=30, Pads={str(i): [] for i in range(controllers)})], 25)
        assert hints["JoinHintDevices"] == expected, hints
    online = [key(1, "Down"), key(3, "Enter")]
    choices = capture("online-choices", online, 5)
    assert choices["MenuItems"] == ["CREATE LOBBY", "JOIN LOBBY", "BACK"], choices
    assert choices["MenuBackground"] and choices["LobbySlots"] is None, choices
    backed = capture("online-back", online + [key(7, "Escape")], 9)
    assert backed["Page"] == "Main" and backed["MenuBackground"] and backed["LobbySlots"] is None, backed
    join = online + [key(7, "Down"), key(9, "Enter")]
    joining = capture("join-lobby-options", join, 11)
    assert joining["MenuItems"] == ["BROWSE STEAM LOBBIES", "JOIN CLIPBOARD LOBBY", "BROWSE LAN LOBBIES", "JOIN UDP", "BACK"], joining
    assert joining["MenuBackground"] and joining["LobbySlots"] is None, joining
    udp = join + [key(13, "Down"), key(15, "Down"), key(17, "Down"), key(19, "Enter")]
    direct = capture("join-udp-options", udp, 21)
    assert direct["Page"] == "JoinUdp" and "JOIN CLIPBOARD ADDRESS" in direct["MenuItems"], direct
    lan = join + [key(13, "Down"), key(15, "Down"), key(17, "Enter")]
    browsing = capture("browse-lan-options", lan, 21)
    assert browsing["Page"] == "BrowseLan" and browsing["MenuBackground"] and browsing["LobbySlots"] is None, browsing
    returned = capture("browse-lan-back", lan + [key(23, "Escape")], 25)
    assert returned["Page"] == "JoinLobby" and returned["MenuBackground"], returned
    creation = capture("create-lobby", online + [key(7, "Enter")], 9)
    assert creation["Page"] == "CreateLobby" and creation["MenuItems"][:2] == ["MAX PLAYERS: 8", "TYPE: PUBLIC"], creation

    controller_online = [pad(1, 0, "DPadDown"), pad(3, 0, "A"), pad(5, 0, "A")]
    controller_creation = capture("controller-create-lobby", controller_online, 8)
    assert controller_creation["Page"] == "CreateLobby" and controller_creation["HintDevice"] == 2, controller_creation
    controller_joining = capture("controller-join-lobby", controller_online + [pad(9, 0, "B"), pad(11, 0, "DPadDown"), pad(13, 0, "A")], 16)
    assert controller_joining["Page"] == "JoinLobby" and controller_joining["HintDevice"] == 2, controller_joining
    preview_cycle = [key(1, "Enter"), key(3, "Escape"), key(5, "Down"), key(7, "Down"), key(11, "Enter"),
                     dict(From=13, To=20, Keys=["Enter"]), key(15, "Left"), key(17, "Right")]
    preview_returned = capture("slot-preview-past-cpu", preview_cycle, 23)
    assert preview_returned["LobbySlots"][0]["Type"] == 1 and preview_returned["LobbySlots"][0]["Player"] is None, preview_returned

    room_edges = [key(1, "Enter"), key(3, "Escape"), key(5, "Down"), key(7, "Down"), key(11, "Enter")]
    room_edges += [key(13, "Right"), key(15, "Right"), key(17, "Right"), key(19, "Down"), key(21, "Down"), key(23, "Down")]
    bottom = capture("slot-bottom-edge", room_edges, 25)
    assert bottom["SelectedSeat"] == 7, bottom
    room_edges += [key(27, "Up"), key(29, "Left")]
    across = capture("slot-skip-center", room_edges, 31)
    assert across["SelectedSeat"] == 3, across

    preview = [key(1, "Enter"), pad(3, 0, "X"), pad(5, 0, "Start")]
    paused_preview = capture("lobby-start-opens-menu", preview, 7)
    assert paused_preview["Page"] == "LobbyMenu" and not paused_preview["LobbySlots"][0]["Player"]["Spawned"], paused_preview
    assert paused_preview["HintDevice"] == 2, paused_preview
    spawned_after_menu = capture("lobby-attack-spawns", preview + [key(9, "Escape"), pad(11, 0, "X")], 13)
    assert spawned_after_menu["Page"] == "Seats" and spawned_after_menu["LobbySlots"][0]["Player"]["Spawned"], spawned_after_menu
    start_on_platform = capture("lobby-start-on-platform", controller[:3] + [pad(30, 0, "Start")], 32)
    assert start_on_platform["Page"] == "LobbyMenu" and start_on_platform["LobbySlots"][0]["Player"]["Spawned"], start_on_platform

    rows = [key(1, "Enter"), pad(3, 0, "X"), pad(5, 0, "X"), pad(7, 1, "X"), pad(9, 1, "X"), pad(11, 0, "Start"), key(13, "Down")]
    shared_keyboard = capture("lobby-menu-shared-keyboard", rows, 15)
    assert shared_keyboard["Page"] == "LobbyMenu" and shared_keyboard["Selected"] == 1 and shared_keyboard["HintDevice"] == 0, shared_keyboard
    assert shared_keyboard["LocalDevices"] == [2, 3] and not any("COLOR / TEAM" in row for row in shared_keyboard["MenuItems"]), shared_keyboard
    rows += [pad(17, 1, "DPadDown")]
    shared_pad = capture("lobby-menu-shared-controller", rows, 19)
    assert shared_pad["Selected"] == 2 and shared_pad["HintDevice"] == 3, shared_pad
    rows += [click(21, 640, 365)]
    shared_mouse = capture("lobby-menu-shared-mouse", rows, 23)
    assert shared_mouse["Page"] == "Settings" and shared_mouse["HintDevice"] == 0, shared_mouse
    rows += [pad(25, 0, "B"), pad(27, 1, "DPadUp"), pad(29, 1, "DPadUp"), pad(31, 1, "DPadUp"), pad(33, 1, "A")]
    playing = capture("controller-start", rows, 35)
    assert playing["Page"] == "Playing" and playing["Players"] == 2 and not playing["Paused"], playing
    rows += [pad(37, 1, "Start"), key(41, "Enter")]
    shared_pause = capture("match-menu-shared-keyboard", rows, 43)
    assert shared_pause["Page"] == "Settings" and shared_pause["HintDevice"] == 0 and shared_pause["Paused"], shared_pause
    resumed = capture("controller-resume", rows + [pad(45, 0, "B"), pad(47, 1, "B")], 49)
    assert resumed["Page"] == "Playing" and not resumed["Paused"], resumed


def verify_player_menu():
    def key(frame, name):
        return dict(From=frame, To=frame + 1, Keys=[name])
    def click(frame, x, y):
        return dict(From=frame, To=frame + 1, MouseX=x, MouseY=y, MouseDown=True)
    def capture(name, rows, frames):
        path = out / (name + "-input.json")
        path.write_text(json.dumps(rows))
        return run(name, ["--no-intro", "--input-script", str(path), "--frames", str(frames), "--capture", str(out / (name + ".png"))])
    party = [key(1, "Enter"), key(3, "U"), key(5, "U"), key(7, "OemPeriod"), key(9, "OemPeriod")]
    players = party + [key(11, "Escape")] + [key(frame, "Down") for frame in (13, 15, 17, 19)] + [key(21, "Enter")]
    highlighted = capture("players-highlight", players, 24)
    assert highlighted["Page"] == "ViewPlayers" and highlighted["PlayerActions"]["Accept"] == "BACK OUT", highlighted
    removed = capture("players-direct-backout", players + [key(25, "Enter")], 28)
    assert removed["Page"] == "ViewPlayers" and removed["LocalDevices"] == [1], removed
    mouse = players + [click(25, 640, 310)]
    selected = capture("players-click-selects", mouse, 28)
    assert selected["Page"] == "ViewPlayers" and selected["LocalDevices"] == [0, 1], selected
    mouse += [click(29, 640, 419), click(33, 640, 405), click(37, 640, 423)]
    empty = capture("players-click-actions", mouse, 40)
    assert empty["Page"] == "LobbyMenu" and empty["LocalDevices"] == [] and "RESUME" not in empty["MenuItems"], empty
    cpu = [key(1, "Enter"), key(3, "Escape"), key(5, "Down"), key(7, "Down"), key(9, "Enter"),
           key(11, "Enter"), key(13, "Escape"), key(15, "Down"), key(17, "Down"), key(19, "Enter")]
    highlighted_cpu = capture("players-cpu-highlight", cpu, 22)
    assert highlighted_cpu["PlayerActions"] == dict(Accept=None, Remove="KICK", DisabledReason=None), highlighted_cpu
    kicked = capture("players-cpu-kick", cpu + [key(23, "U")], 26)
    assert kicked["Page"] == "ViewPlayers" and kicked["LocalDevices"] == [], kicked
    paused = party + [key(11, "Escape"), key(13, "Enter"), key(17, "Escape")]
    pause = capture("pause-without-resume", paused, 20)
    assert pause["Page"] == "Playing" and pause["Paused"] and "RESUME" not in pause["MenuItems"], pause
    resumed = capture("pause-click-back", paused + [click(21, 720, 448)], 24)
    assert resumed["Page"] == "Playing" and not resumed["Paused"], resumed
    readonly = capture("players-match-readonly", paused + [key(21, "Down"), key(23, "Enter"), key(25, "Enter")], 28)
    assert readonly["Page"] == "ViewPlayers" and readonly["PlayerActions"]["DisabledReason"] == "LOBBY ONLY" and readonly["LocalDevices"] == [0, 1], readonly
    print("PASS: direct player actions, clickable footer, CPU kick, read-only match list and pause without Resume.")


def verify_player_network_actions():
    def key(frame, name):
        return dict(From=frame, To=frame + 1, Keys=[name])
    for unspectate in (False, True):
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
            reserve.bind(("127.0.0.1", 0))
            port = reserve.getsockname()[1]
        host_rows = [key(160, "Escape")] + [key(t, "Down") for t in (162, 164, 166, 168)] + [key(170, "Enter"), key(172, "Down")]
        guest_rows = [key(160, "Escape"), key(162, "Down"), key(164, "Enter"), key(166, "Down"), key(180, "Enter"), key(220, "Enter")]
        if unspectate:
            guest_rows += [key(260, "Enter")]
        processes = []
        label = "return" if unspectate else "spectate"
        for name, rows, options, frames in [
            ("players-host-" + label, host_rows, ["--host", "udp", "--local-players", "1"], 390),
            ("players-guest-" + label, guest_rows, ["--join", "udp:127.0.0.1", "--local-players", "2"], 500),
        ]:
            path = out / (name + "-input.json")
            path.write_text(json.dumps(rows))
            args = options + ["--port", str(port), "--frames", str(frames), "--input-script", str(path), "--capture", str(out / (name + ".png"))]
            processes.append((name, subprocess.Popen(command(name, args), cwd=root, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)))
        try:
            values = []
            for name, process in processes:
                log, _ = process.communicate(timeout=60)
                (out / (name + ".log")).write_text(log)
                assert process.returncode == 0, (name, log)
                value = json.loads((out / (name + ".json")).read_text())
                assert value["Error"] is None and value["Page"] == "ViewPlayers", value
                values.append(value)
            host, guest = values
            expected = "SPECTATE" if unspectate else "UNSPECTATE"
            assert guest["PlayerActions"] == dict(Accept=expected, Remove=None, DisabledReason=None), guest
            assert guest["Selected"] == 1, guest
            if unspectate:
                assert guest["LobbySpectators"] == [] and guest["LobbySlots"][1]["Player"]["Id"] == 1 and not guest["LobbySlots"][1]["Player"]["Spawned"], guest
            else:
                assert len(guest["LobbySpectators"]) == 1 and guest["LobbySpectators"][0]["Id"] == 1, guest
            assert host["PlayerActions"] == dict(Accept=expected, Remove="KICK", DisabledReason=None), host
        finally:
            for _, process in processes:
                if process.poll() is None:
                    process.kill()
                    process.wait()
    print("PASS: guest directly backs out, spectates and unspectates; host gets both actions; selection follows the player.")


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


def verify_online_creation():
    def key(frame, name):
        return dict(From=frame, To=frame + 1, Keys=[name])
    actions = [(1, "Enter"), (3, "Escape"), (5, "Down"), (7, "Down"), (11, "Enter"),
               (13, "Enter"), (15, "Escape"), (17, "Down"), (19, "Down"), (21, "Down"), (23, "Enter"),
               (25, "Enter"), (27, "Left"), (29, "Down"), (31, "Left"), (33, "Left"), (35, "Left"),
               (37, "Left"), (39, "Down"), (41, "Enter")]
    rows = [key(frame, name) for frame, name in actions]
    script = out / "open-local-cpu-input.json"
    script.write_text(json.dumps(rows))
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
        reserve.bind(("0.0.0.0", 0))
        port = reserve.getsockname()[1]
        failed = run("create-lobby-failure", ["--no-intro", "--port", str(port), "--input-script", str(script), "--frames", "60"], expect_error=True)
        assert failed["Page"] == "Seats" and failed["LobbySlots"][0]["Player"]["Cpu"], failed
        assert all(slot["Type"] in (1, 5) for slot in failed["LobbySlots"]), failed
    created = run("create-lobby-keeps-cpu", ["--no-intro", "--port", str(port), "--input-script", str(script), "--frames", "60"])
    assert created["Page"] == "Seats" and created["LobbySlots"][0]["Player"]["Cpu"], created
    assert [slot["Type"] for slot in created["LobbySlots"]] == [5, 0, 0, 0, 4, 4, 4, 4], created
    script.write_text(json.dumps(rows + [key(61, "Escape"), key(63, "Up"), key(67, "Enter")]))
    returned = run("quit-online-returns-main", ["--no-intro", "--port", str(port), "--input-script", str(script), "--frames", "70"])
    assert returned["Page"] == "Main" and returned["LobbySlots"] is None and returned["LocalDevices"] == [], returned
    print("CPU preserved when creating online; chosen capacity applied; failed creation restores local rooms; Quit Lobby returns to main.")


def verify_lobby_network():
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as reserve:
        reserve.bind(('127.0.0.1',0)); port=reserve.getsockname()[1]
    keys=[(120,'Escape'),(122,'Down'),(124,'Down'),(128,'Enter'),
          (130,'Down'),(132,'Down'),(134,'Right'),(138,'Right'),(140,'Right'),(142,'Right'),(144,'Right'),
          (148,'Right'),(152,'Right'),(154,'Right'),(156,'Right'),(158,'Right'),(164,'Escape'),(166,'Escape')]
    script=out/'online-capacity-input.json'
    script.write_text(json.dumps([dict(From=f,To=f+1,Keys=[k]) for f,k in keys] + [dict(From=136,To=147,Keys=['Enter']),dict(From=150,To=161,Keys=['Enter'])]))
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


def verify_lobby_color_respawn():
    script = out / "lobby-color-respawn-input.json"
    script.write_text(json.dumps([
        dict(From=frame, To=frame + 1, Keys=[key])
        for frame, key in [(240, "U"), (300, "Y"), (360, "U"),
                           (480, "U"), (540, "Y"), (600, "U")]
    ]))
    for name, frames, spawned in [("lobby-guest-choosing", 330, False),
                                  ("lobby-guest-respawned", 720, True)]:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
            reserve.bind(("127.0.0.1", 0))
            port = reserve.getsockname()[1]
        host = subprocess.Popen(command(name + "-host", ["--host", "udp", "--port", str(port)]),
                                cwd=root, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            value = run(name, ["--join", "udp:127.0.0.1", "--port", str(port),
                               "--input-script", str(script), "--frames", str(frames)])
            assert value["Page"] == "Seats" and value["LobbyProgress"] is None, value
            guests = [(room, slot["Player"]) for room, slot in enumerate(value["LobbySlots"])
                      if slot["Player"] is not None and slot["Player"]["Peer"] == 1]
            assert len(guests) == 1, value
            room, player = guests[0]
            assert player["Spawned"] == spawned and value["LobbyPlayers"][room]["Alive"] == spawned, value
        finally:
            host.kill()
            host.wait()
    print("PASS: an already spawned guest can choose colors and respawn repeatedly without changing membership.")


def verify_spectator_direct_join():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
        reserve.bind(("127.0.0.1", 0))
        port = reserve.getsockname()[1]
    script = out / "spectator-direct-join-input.json"
    script.write_text(json.dumps([dict(From=180, To=181, Keys=["OemPeriod"])]))
    for pending in (True, False):
        name = "spectator-join-pending" if pending else "spectator-join-complete"
        host = subprocess.Popen(command(name + "-host", ["--host", "udp", "--port", str(port)]),
                                cwd=root, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            value = run(name, ["--join", "udp:127.0.0.1", "--port", str(port), "--spectate",
                               "--input-script", str(script), "--frames", "181" if pending else "350",
                               "--capture", str(out / (name + ".png"))])
            assert value["Page"] == "Seats", value
            if pending:
                assert value["LobbyProgress"] is not None and "JOINING..." in value["LobbyRoomStatus"], value
                assert value["LobbySpawnPuffs"] >= 1, value
                slots = value["PresentedLobbySlots"]
            else:
                assert value["LobbySpectators"] == [] and value["LobbyProgress"] is None, value
                slots = value["LobbySlots"]
            assert any(slot["Player"] is not None and slot["Player"]["Peer"] == 1 and slot["Player"]["Id"] == 1 for slot in slots), value
        finally:
            host.kill()
            host.wait()
    print("PASS: spectator joins with another keyboard directly; preview and progress appear immediately and clear on completion.")


def verify_cpu_network():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reserve:
        reserve.bind(("127.0.0.1", 0))
        port = reserve.getsockname()[1]
    actions = [(90, "Escape"), (92, "Down"), (94, "Down"), (96, "Enter"),
               (98, "Right"), (100, "Right")]
    actions += [(frame, "Enter") for frame in (102, 104, 106, 108, 110)]
    actions += [(112, "Escape"), (114, "Escape")]
    script = out / "udp-cpu-edit-input.json"
    script.write_text(json.dumps([dict(From=f, To=f + 1, Keys=[key]) for f, key in actions]))
    common = ["--local-players", "1", "--port", str(port), "--ticks", "480"]
    processes = []
    try:
        for name, options in [
            ("udp-cpu-host", ["--host", "udp", "--start-players", "3", "--input-script", str(script)]),
            ("udp-cpu-guest", ["--join", "udp:127.0.0.1"]),
        ]:
            process = subprocess.Popen(command(name, common + options), cwd=root, env=env,
                                       text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            processes.append((name, process))
        values = []
        for name, process in processes:
            log, _ = process.communicate(timeout=60)
            (out / (name + ".log")).write_text(log)
            assert process.returncode == 0, (name, log)
            value = json.loads((out / (name + ".json")).read_text())
            assert value["Error"] is None and value["Players"] == 3, value
            assert value["TickNumber"] == 480 and value["ConfirmedFrame"] == 479, value
            assert value["MatchPeerSlots"] == [[0, 1], [2]], value
            values.append(value)
        assert values[0]["Hash"] == values[1]["Hash"], values
        print("PASS: host created CPU through slot editor; both clients reached the same confirmed CPU match state.")
    finally:
        for _, process in processes:
            if process.poll() is None:
                process.kill()
                process.wait()


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
        json.dumps(dict(Fullscreen=False, VSync=False))
    )
    verify_connections_and_repeat()
    verify_render_cadence()
    pause_script = verify_pause()
    verify_smoke_pause(pause_script)
    verify_title_menu()
    verify_menu_background()
    record = verify_local_replay()
    verify_replay_exit(record)
    verify_lobby_menus()
    verify_player_menu()
    if not args.quick:
        verify_arenas()
    if args.network:
        verify_player_network_actions()
        verify_online_creation()
        verify_lobby_network()
        verify_lobby_color_respawn()
        verify_spectator_direct_join()
        verify_cpu_network()
        verify_network()
    print("PASS: client input, replay and render cadence checks; captures in", out)


if __name__ == "__main__":
    main()
