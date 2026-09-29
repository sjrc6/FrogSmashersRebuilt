# Frog Smashers Rebuilt

A MonoGame port of Frog Smashers for Windows and Linux. Up to eight players with local and online (rollback) multiplayer. 

### Controls

| Action | Keyboard one | Keyboard two | Controller |
| --- | --- | --- | --- |
| Move / aim | WASD | Arrows | Left stick / D-pad |
| Jump / recovery | T | M | A |
| Charge / release bat | U | Period | X |
| Tongue / grapple | Y | Comma | B |
| Strafe | R | N | Left shoulder |
| Drop through platform | Down + Jump | Down + Jump | Down + A |

F11 -  fullscreen\
F3 - timing/network information\
F4 - collision geometry

### Build

- Install the .NET SDK specified by `global.json`:

```sh
dotnet build FrogSmashersRebuilt.slnx -c Release -m:1
dotnet run --project src/Client -c Release --no-build
```

```sh
./scripts/test.sh
./scripts/publish.sh all
```

### Source layout

| Directory | Contents |
| --- | --- |
| `src/Core` |  gameplay, collision, state, snapshots, replay |
| `src/Network` | rollback, peer transport, UDP and Steam lobbies |
| `src/Client` | MonoGame entry, screens, presentation, graphics, audio, input |
| `src/Content` |  art, fonts, game data, and MGCB project |
| `src/ContentBuild` | compiled content and original audio |
| `src/Tests` | simulation and networking tests |
| `src/Client.Tests` | input, audio, and particle tests |
| `src/Client.GraphicsTests` | isolated presentation/reference checks |
| `src/Client.Automation` | scripted input, captures |

### Credits and license
[CREDITS.txt](CREDITS.txt) \
Original programming/design: **Ruan Rothmann**. \
Art/animation: **Mike Scott**. \
Additional art: **Ben Rausch and Stuart Coutts**. \
Sound: **Jason Sutherland**. \
[Fork](https://github.com/PNone/frogsmashers) features and fixes: **PNone**.

