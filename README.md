# Frog Smashers Rebuilt

A MonoGame DesktopGL port of **Frog Smashers**, with a 120 Hz deterministic simulation and rollback multiplayer. This is a modified game based on the original source and assets, copyright Ruan Rothmann.

## Play

Standalone packages are generated under `artifacts/`:

- **Windows x64:** extract `FrogSmashersRebuilt-win-x64.zip`, then run `FrogSmashersRebuilt.exe`.
- **Linux x64:** extract `FrogSmashersRebuilt-linux-x64.tar.gz`, then run `./FrogSmashersRebuilt`.

The packages include .NET, SDL, OpenAL and the game assets. Linux still needs working system graphics/audio libraries and an OpenGL driver. Unity is not needed.

Choose **Local Match**. Space joins keyboard 1, Right Shift joins keyboard 2, and A/Start joins a controller. Press C to add a practice CPU. Enter, or Start on an already joined controller, starts with two to eight players. Backspace removes the last seat. Tab opens settings and match rules.

| Action | Keyboard 1 | Keyboard 2 | Controller |
| --- | --- | --- | --- |
| Move / aim | WASD | Arrow keys | Left stick / D-pad |
| Jump / airborne recovery | T | M | A |
| Charge / release bat | U | Period | X |
| Tongue / grapple | Y | Comma | B |
| Strafe (hold facing) | R | N | Left shoulder |
| Drop through platform | Down + Jump | Down + Jump | Down + A |

Keyboard bindings can be changed in Settings. Escape/Start pauses local matches and their animations; Q leaves from that menu. **Online play continues while the menu is open.** F11 toggles borderless fullscreen at the desktop resolution and restores the previous window size. F3 shows diagnostics, and F4 shows collision boxes.

## Matches and presentation

- Two to eight players, free-for-all or teams, including several local seats on one online peer.
- Original arenas, charged bat attacks, tongue grappling, combo knockouts, power flies, arena progression and final Showdown.
- Original frog sprites, palette, animation frames, audio, intro/outro, score icons and in-game text. Five or more individual score entries use two columns.
- Fonts retain the original layout, with small repairs to fragile joins in the score font's `0`, `8`, `Q`/`q` and the menu font's `8`. Settings offers **Font smoothing: Off / Narrow / Normal**; Off is the current default experiment, with sharp edges and some expected shimmer during movement or scaling.
- Preserved partial embedding in one-way platforms. Official fly ownership fixes and the fork's team Showdown fix are included.
- Fixed 120 Hz gameplay with interpolated rendering. VSync and a separate rendering limit are configurable.
- Win score, number of rounds, first arena, sequential/shuffled maps and teams are selectable. Advanced custom map order is available through `--map-order`.

The simulation uses fixed-point arithmetic and custom rectangle/capsule collision queries. Exact Unity corner tolerances and final game feel still need comparison by a player. Practice CPUs are basic test opponents.

## Online and account-free testing

Choose **Online Match**, join this machine's local seats, then choose Steam or UDP. The host selects the number of network peers; each peer may bring several players, up to eight total. A private match starts once the requested peers connect. With teams enabled, AUTO alternates teams by global player slot; explicit seat selections are negotiated across peers.

**Steam:** run Steam in the same operating system as the game. Both players should open Online Match so the game connects to Steam. Host a private lobby and press I to invite friends. Accepting an invite joins the game; the displayed lobby ID also allows manual joining after receiving an invitation. Private Steam lobbies require an invite. The adapter uses Steam Networking Sockets and AppID **480**. Real Steam invites, NAT/relay connections and native Windows gameplay remain acceptance tests on actual machines/accounts.

**Localhost:** no Steam installation or account is required. Run these in separate terminals from an extracted package:

```sh
./FrogSmashersRebuilt --host udp --peers 2 --local-players 1
./FrogSmashersRebuilt --join udp:127.0.0.1 --local-players 1
```

On Windows, use `FrogSmashersRebuilt.exe` instead. Add `--demo` to both commands for CPU-driven testing. UDP hosts bind localhost by default; `--lan` enables trusted-LAN testing. UDP transport is unauthenticated. There is no mid-match joining or host migration.

All peers must use the same content and simulation/network build. Rollback predicts up to 24 ticks, uses two ticks of input delay, and checks confirmed state hashes. See [networking details](docs/NETWORKING.md).

## Build and test

Install the .NET SDK specified by `global.json`, then:

```sh
./scripts/build.sh
./scripts/run.sh
./scripts/test.sh
./scripts/publish.sh all
```

On Windows, the equivalent build command is `dotnet build FrogSmashersRebuilt.slnx -c Release -m:1`. Imported content and compiled OpenGL effects are checked in, so normal builds need neither Python, Wine nor Unity.

The console tests exercise mechanics, snapshot restoration, replays, all authored maps, 2/4/8 impaired peers, mixed local seats, clock skew, malformed packets, desync detection and separate UDP processes. `./scripts/test.sh --no-sockets` skips socket-dependent tests in restricted environments. Standalone packages also contain a `diagnostics` executable; `--replay-hash` prints the canonical cross-platform test hash and `--benchmark` measures a rollback workload.

For offscreen visual and render-cadence checks on Linux:

```sh
python3 scripts/check-client.py
./scripts/run.sh --demo --players 8 --offscreen --no-audio \
  --frames 360 --capture artifacts/example.png
```

Local input replays can be saved with `--record match.fsr` and played with `--replay match.fsr`; retain the adjacent `.json` settings file. `--help` lists all diagnostic options. A replay verifies a state hash once per simulated second.

## Source and content

| Directory | Purpose |
| --- | --- |
| `Rebuilt/Core` | Fixed-point gameplay, collision, snapshots, replays |
| `Rebuilt/Network` | Rollback, simulated links, UDP and Steam lobbies |
| `Rebuilt/Client` | MonoGame rendering, interpolation, audio, input, menus |
| `Rebuilt/Content` | Imported assets, maps, timelines and compiled effects |
| `Rebuilt/Tests` | Headless mechanics and networking checks |
| `Originals/official`, `Originals/PNone_fork` | Ignored reference clones |

The content importer reads Unity YAML, prefab overrides, sprite metadata and animation curves directly. Normal builds use the bundled generated assets. To regenerate the manifest, install `Rebuilt/tools/requirements-content.txt` and run `python3 Rebuilt/tools/import_content.py`; `--verify` checks asset hashes and references. Font, sprite-mesh, imported-audio and particle regeneration uses the isolated reference-player captures described in [Unity reference provenance](docs/UNITY_REFERENCE.md). Shader changes need `scripts/compile-effects.sh`, followed by content import to refresh hashes. See [visual fidelity audit](docs/VISUAL_EFFECTS_AUDIT.md), [audio compatibility](docs/AUDIO_COMPATIBILITY.md), [content conventions](Rebuilt/Content/README.md), and [physics compatibility](docs/PHYSICS_COMPATIBILITY.md).

Development and testing stay on the Linux filesystem. No Windows drives are needed.

## Credits and license

Original programming/design: **Ruan Rothmann**. Art/animation: **Mike Scott**. Additional art: **Ben Rausch and Stuart Coutts**. Sound: **Jason Sutherland**. Fork features and fixes: **PNone and contributors**.

This is a modified version of Frog Smashers. The original [noncommercial source license](LICENSE.md) applies to the supplied source and assets. See [full credits](Rebuilt/Content/CREDITS.txt) and [third-party notices](Rebuilt/Notices). The original notice is included unchanged in both packages.
