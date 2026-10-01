# LLM WRITTEN DOC


## GGCS

A C# rollback networking library based on GGRS and GGPO. It targets .NET 10 and has no package, native, Steam, MonoGame or Rust dependency. The game still uses its existing networking; GGCS is isolated until the mesh and lobby integration is ready.

## What is implemented

- A fixed roster of player inputs, grouped by owning machine. Playing machines communicate directly with each other. Multiple local players share one connection per remote machine.
- Delayed local input, repeat-last prediction or a custom predictor, snapshot restore and resimulation, and bounded independent prediction/history windows.
- Handshake, session generation and configuration checks, input acknowledgements and retransmission, packet reordering, duplication and loss recovery, bounded XOR/RLE compression, interruption/resume/timeout events, and disconnect cutoff agreement.
- RTT measurements and GGRS's 30-frame smoothing of peer frame advantage. Pacing advice is separate from the prediction limit.
- Confirmed state checksums and desync events.
- Combined confirmed spectator streams, bounded catch-up, late spectators from an externally transferred checkpoint, and a host with no local player that collects inputs and serves spectators.
- A local determinism test session that repeatedly restores and replays recent frames.

## Application boundary

Provide four small interfaces:

| Interface | Responsibility |
| --- | --- |
| `IRollbackGame<TInput, TState>` | Capture isolated state, restore it, and simulate one frame. |
| `IInputCodec<TInput>` | Encode/decode a fixed-size input with an explicit byte layout. |
| `ITransport` | Send datagrams to a peer ID and receive datagrams with their source ID. |
| `IClock` | Supply monotonic milliseconds. The default uses `Stopwatch`. |

`TInput` is an unmanaged value type. Do not serialize its raw in-memory layout: padding, platform layout and byte order should not become protocol rules. Decode should reject invalid values with `ArgumentException`, `FormatException`, `OverflowException` or `InvalidDataException`. GGCS turns those into a session failure. Other exceptions from application callbacks propagate to the caller.

`SaveState` must return an isolated or immutable snapshot. `LoadState` must not make live mutable game state share storage with the retained snapshot. The same rule applies to snapshots returned by `TryGetConfirmedState`; treat them as read-only. Optional checksums describe the captured state, not presentation state.

Gameplay may depend on `InputStatus.Disconnected`. **Gameplay must not depend on `Confirmed` versus `Predicted`, or on `isResimulation`.** Matching late input does not require a replay. Use those flags for presentation and reversible event handling only. Keep irreversible audio, UI, file writes and external actions outside the simulation.

Sessions are single-threaded. Polling, advancement and callbacks belong on the same thread and must not re-enter the session. The application owns the transport and its lifetime. `Send` must finish consuming or copy the supplied bytes before returning; received memory must remain valid until GGCS processes that datagram. Route peer IDs to admitted identities in the transport adapter. Session IDs reject stale generations; they do not authenticate senders.

## Playing session

Construct the same ordered roster on every machine. Player handles are consecutive from zero; peer IDs identify machines, not controllers or rooms.

```csharp
Player[] roster = [new(0, 10), new(1, 10), new(2, 20)];
var options = new SessionOptions
{
    FramesPerSecond = 120,
    MaxPredictionFrames = 24,
    HistoryFrames = 360,
    InputDelay = 2
};
var session = new P2PSession<MyInput, MySnapshot>(
    generation, localPeerId, roster, game, codec, transport, options,
    inputSchema: "frog-input-layout-and-simulation-configuration");
```

The host chooses a fresh nonzero `generation` for each session and distributes it along with the roster and checkpoint. `inputSchema` contributes to the handshake fingerprint; include a stable description/hash of input layout and deterministic game configuration. GGCS also checks the roster, encoded input size, simulation rate and reciprocal packet layouts. Different peers may use different input delays.

Call `Poll()` regularly, including while paused, synchronizing or waiting for inputs. It processes acknowledgements, retries, timing and events. It may restore/resimulate the game to repair an earlier prediction; it does not consume a new frame.

At the fixed simulation cadence, call `AdvanceFrame(localInputs)`. Inputs must be in `LocalPlayerHandles` order. This method also polls. Its result explains whether a frame advanced or why it waited. Do not accumulate unlimited catch-up work during a network stall.

The first attempt at a frame latches and sends its local inputs, even if that frame must wait. Repeated attempts at that same `CurrentFrame` keep those inputs unchanged. Buffer new button edges for a later simulation frame in the application. `SetInputDelay` takes effect on the next newly submitted frame; increasing delay fills the gap with the last input, and decreasing delay discards overlapping submissions.

`FramesAhead` is a smoothed estimate. `RecommendedFrameDurationMultiplier` suggests a 10% longer tick duration when ahead by at least three frames. Apply it to the local accumulator's tick duration; continue polling normally. The library never sleeps. This small slowdown follows the TF.EX integration approach; its exact scheduling is not a GGRS wire/API compatibility promise.

`CurrentFrame` is the next input frame to simulate. Snapshot frame N is the state **before** input N. `ConfirmedFrame` is the last simulated input frame that can no longer change, so its corresponding state is `ConfirmedFrame + 1`.

Missing inputs limit prediction. Global confirmation separately bounds retention and publishing. Receiving all inputs locally allows simulation to continue without waiting another RTT, while hashes and spectator inputs wait for all retained playing peers' receipt reports. Disconnect cutoffs must also agree across those peers. `HistoryFrames` must comfortably cover expected confirmation delays and repairs; exhausted history stops advancement rather than overwriting needed state.

`DisconnectPeer` removes an entire remote machine, including all its local players. The surviving peers briefly pause advancement and confirmation, exchange their frozen confirmation floors, and agree a cutoff at least as high as every floor. Each survivor acknowledges that cutoff before any publishes later frames. They then resimulate later frames as disconnected. This prevents stale receipt reports from invalidating already-published spectator inputs. `AdvanceStatus.DisconnectAgreement` identifies this wait. A machine explicitly excluded by another participant stops that generation. This is a rollback membership mechanism, not a consensus or host migration system; the game still coordinates recovery from partitions and roster changes.

## Spectators

On the playing host, call `AddSpectator(spectatorPeerId)` before simulation begins. On that spectator, create `SpectatorSession<TInput,TState>` with the same roster, generation, options/schema and initial game state, pointing at the host.

A spectator receives only confirmed inputs from the host. It never contributes to player pacing or confirmation. `AdvanceFrame()` consumes one available frame. `AdvanceAvailable()` consumes up to `SpectatorCatchUpFrames` and is suitable for catching up from an existing checkpoint. Both continue polling. The default buffering delay is zero. If `SpectatorBufferFrames` is set, use `drain: true` to consume the final buffered frames at a pause or match end.

A slow or absent observer cannot stall the players. Once its bounded input/history buffer is exhausted, it is disconnected with a diagnostic and needs a new checkpoint.

For a late spectator, transfer a state returned by `TryGetConfirmedState(startFrame, out state)` through the application's checkpoint protocol. Load it before constructing the spectator. Use the same `startFrame` in `AddSpectator` and the spectator constructor. The host must still retain the inputs beginning at that frame. Checkpoint transfer and validation are application responsibilities.

### Host without a player

Before frame zero, every playing machine calls `AddInputObserver(hostPeerId)`. Construct a `P2PSession` on the host with a local peer ID absent from the playing roster; `IsInputObserver` is then true. Advance it with an empty local input span and attach ordinary spectators to it with `AddSpectator`.

The host collects each playing machine's raw input stream and receipt reports, advances confirmed frames, and sends the combined stream to spectators. Its raw observer links are excluded from player pacing/confirmation. If an observer link is lost, the host waits for a possible agreed removal from the active peers. Without that agreement it stops its observing generation after a bounded grace period; it cannot invent a disconnect of a still-playing frog. The application coordinates reconnection/checkpoint recovery.

## Diagnostics and determinism checks

Drain `TryGetEvent` for synchronization, interruption, recovery, disconnection, exclusion, protocol errors, spectator backlog and checksum mismatches. Event queues and checksum histories are bounded. `GetNetworkStats` exposes RTT, frame advantage, pending/received/acknowledged frames, traffic counters and invalid/stale packets. Session diagnostics include prediction depth and total/largest resimulation.

Use `SyncTestSession` during offline development with checksums enabled on every snapshot. It replays the recent history after every advancement and throws `DeterminismException` at the first changed state. This catches incomplete snapshots and nondeterministic game logic without involving a network.

## Build and tests

From the repository root:

```bash
source scripts/common.sh
dotnet build src/GGCS.Tests/GGCS.Tests.csproj -c Release -m:1 -nr:false
dotnet .build/bin/GGCS.Tests/release/GGCS.Tests.dll
```

The normal `scripts/test.sh` also runs GGCS. `--no-sockets` skips the loopback UDP test in restricted environments. The suite covers model-based randomized queues/repairs, packet bounds and fuzzing, impaired 2/4/8-machine meshes, multiple local inputs with changing delays, observer/spectator roles, disconnects, clock drift/render hitches, real frog world hashes and checkpoint restarts. It asserts recovery and simulation speed as well as eventual state agreement.

Selected input-delay, prediction/rollback and timing traces come from the pinned Rust implementation. They run as embedded fixtures in the C# tests. See [reference validation](../GGCS.Tests/Reference/REFERENCE.md) for optional Rust regeneration and the upstream test results. Normal builds do not invoke Rust.

## Provenance and intentional differences

| GGCS module | Primary reference |
| --- | --- |
| `Core/DelayedInputQueue.cs`, `Core/RollbackEngine.cs` | GGRS `input_queue.rs`, `sync_layer.rs`, `frame_info.rs` |
| `Core/TimeSync.cs` | GGRS `time_sync.rs` / GGPO time synchronization |
| `Protocol/*` | GGRS `network/protocol.rs`, `messages.rs`, `compression.rs` |
| `P2PSession.cs` | GGRS `sessions/p2p_session.rs` and TF.EX fork disconnect/retention fixes |
| `SpectatorSession.cs`, `SyncTestSession.cs` | GGRS spectator and sync-test sessions |

Primary baseline: [GGRS e97e3d2](https://github.com/gschup/ggrs/tree/e97e3d2416cc68af2d2876d41180d950c2939b6e). Supplemental fixes: [TF.EX GGRS fork 12a2d47](https://github.com/Fcornaire/ggrs/tree/12a2d476653873042df38496386415124693acb9). [GGPO 7ddadef](https://github.com/pond3r/ggpo/tree/7ddadef8546a7d99ff0b3530c6056bc8ee4b9c0a) is the underlying design reference. Required notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). No code was copied from the GPL-licensed TF.EX game integration.

Deliberate C# differences:

- Synchronous save/load/advance callbacks replace Rust request cells and locks. Every simulated frame is saved; there is no sparse-saving mode.
- Inputs have a fixed explicit codec. The wire format uses bounded byte RLE after XOR deltas, independently decodable MTU-sized chunks and a session generation. It does not port Serde, Bincode or bitfield-rle, or interoperate with old protocols.
- History and pending-input capacity follow configured limits, independently of the prediction limit. All pending inputs are eligible for transmission; newer chunks are not hidden behind an old fixed-size send window.
- Globally final confirmation is separate from local prediction, protecting observer streams and disconnect corrections.
- Observer buffers are bounded. A host with no local frog is supported explicitly.
- Timing uses an injected monotonic clock; there are no spin waits, browser bindings or native code.

Still outside this library: Steam/LAN connection establishment, authentication and admission, checkpoint serialization/transfer, coordinated lobby transitions, deterministic lobby commands, host policy, event reconciliation and game UI. These remain the next integration phase. Simulated-network and Linux loopback results do not substitute for live Steam testing or Windows validation.
