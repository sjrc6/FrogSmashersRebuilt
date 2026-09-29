# Source layout

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