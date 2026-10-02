# LLM WRITTEN DOC


## GGCS

A C# rollback networking library based on GGRS and GGPO. It targets .NET 10 and has no package, native, Steam, MonoGame or Rust dependency. The game uses GGCS for both online lobbies and matches through the transport and simulation adapters in `src/Network`.

## What is implemented

- Registered input handles grouped by owning machine, with optional authoritative participation for live admission. Playing machines communicate directly with each other. Multiple local players share one connection per remote machine.
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
    MaxPredictionFrames = 30,
    HistoryFrames = 360,
    InputDelay = 2,
    MaxInputDelay = 238
};
var session = new P2PSession<MyInput, MySnapshot>(
    generation, localPeerId, roster, game, codec, transport, options,
    inputSchema: "frog-input-layout-and-simulation-configuration");
```

The host chooses a fresh nonzero `generation` for each session and distributes it along with the roster and checkpoint. `inputSchema` contributes to the handshake fingerprint; include a stable description/hash of input layout and deterministic game configuration. GGCS also checks the roster, encoded input size, simulation rate and reciprocal packet layouts. Personal timing is per machine, shared by its local handles. Call `SetTiming(new RollbackTiming { DelayFrames = 2, DonationFrames = 0, MaxExtraDelayFrames = 30 })` to opt into up to 30 frames of automatic extra delay. Both the game and `RollbackTiming` default that allowance to zero; a bare library session starts with `SessionOptions.InputDelay` and no automatic extra delay.

Call `Poll()` regularly, including while paused, synchronizing or waiting for inputs. It processes acknowledgements, retries, timing and events. It may restore/resimulate the game to repair an earlier prediction; it does not consume a new frame.

At the fixed simulation cadence, call `AdvanceFrame(localInputs)`. Inputs must be in `LocalPlayerHandles` order. This method also polls. Its result explains whether a frame advanced or why it waited. Do not accumulate unlimited catch-up work during a network stall.

The first attempt at a frame latches and sends its local inputs, even if that frame must wait. Repeated attempts at that same `CurrentFrame` keep those inputs unchanged. Compare `LastSubmittedFrame` before and after the attempt, then consume an individual handle's button edges only if `LastAcceptedInputFrame(handle)` equals the new submitted frame. A successful simulation step alone is not the acceptance signal. Reducing delay temporarily skips samples which would overwrite already accepted input. Keep those button edges buffered until a new sample is accepted. Increasing delay fills the gap through the configured predictor (repeat-last by default); the game clears one-shot lobby commands in that padding.

### Personal timing and pacing

Let S be the displayed simulation frame, D the effective response delay (manual plus automatic), and L the voluntary simulation lead. Local input is queued for S + D. Pacing aligns **S + D - L** between machines, instead of aligning S alone.

- **Delay** moves this machine's simulation behind its input horizon, reducing its own prediction of remote players. It does not force other machines to use the same response delay.
- **Donation** lets this machine run further ahead, accepting more prediction locally so peers need less prediction of its inputs.
- **Max extra delay** bounds automatic response delay during poor connections. Zero preserves pause-and-wait behavior at the prediction limit.

`FramesAhead` is the worst smoothed peer advantage after those offsets. `RecommendedFrameDurationMultiplier` keeps a two-frame deadband, then adds 2% tick duration per excess frame up to 10%. The smaller correction avoids all peers slowing excessively when jitter introduces a small apparent lead. Apply the multiplier to the local accumulator; continue polling normally. Pacing converges within a few ticks, rather than promising an exact instantaneous lead.

Automatic delay is opt-in. Increases require actual prediction depth to enter the last four allowed frames; RTT alone cannot trigger them. The first increase is immediate, sized from half-RTT, relative donation, base delay and current prediction pressure. Subsequent pressure-only increases wait half an RTT plus `TimeSync`'s 30-frame averaging window (at least 100 ms) for feedback. A higher RTT-based requirement can raise the target sooner. Limits always apply, and another automatic adjustment requires fresh accepted input after the previous change.

Recovery removes one extra-delay frame every 50 ms when prediction is at least three frames below the pressure threshold, the RTT estimate permits it, and pressure has been absent for half an RTT (at least 100 ms). This avoids long stretches of skipped samples from large instant reductions. Polling without advancing cannot drain or inflate the allowance repeatedly. At the combined delay/prediction limit the session still waits for input; delay cannot recover a disconnected link. Settings and effective extra delay travel with revisioned protocol progress. None changes deterministic world state or the handshake configuration.

This timing policy extends GGPO/GGRS; it is not TF.EX's ordinary frame-delay setting. There is no host minimum or response-delay donation setting. A generic GGCS session only enables adaptation when the application supplies a nonzero allowance.

`CurrentFrame` is the next input frame to simulate. Snapshot frame N is the state **before** input N. `ConfirmedFrame` is the last simulated input frame that can no longer change, so its corresponding state is `ConfirmedFrame + 1`.

Missing inputs limit prediction. Global confirmation separately bounds retention and publishing. Receiving all inputs locally allows simulation to continue without waiting another RTT, while hashes and spectator inputs wait for all retained playing peers' receipt reports. Disconnect cutoffs must also agree across those peers. `HistoryFrames` must comfortably cover expected confirmation delays and repairs; exhausted history stops advancement rather than overwriting needed state.

`DisconnectPeer` removes an entire remote machine, including all its local players. The surviving peers briefly pause advancement and confirmation, exchange their frozen confirmation floors, and agree a cutoff at least as high as every floor. Each survivor acknowledges that cutoff before any publishes later frames. They then resimulate later frames as disconnected. This prevents stale receipt reports from invalidating already-published spectator inputs. `AdvanceStatus.DisconnectAgreement` identifies this wait. A machine explicitly excluded by another participant stops that generation. This is a rollback membership mechanism, not a consensus or host migration system; the game still coordinates recovery from partitions and roster changes.

## Live participation

A normal session retains fixed input ownership. To support a live lobby, register its potential machine streams once and supply `SessionParticipation<TInput>`. Its authority handle must always participate; its selector reads a bitmask from the authority's input. The game carries versioned membership commands in that same input. Nonparticipating handles neither advance the prediction limit nor constrain confirmation/pacing. Their gameplay input status is Disconnected.

Use `connectedPeers` to distinguish links already prepared from registered dormant handles. `ConnectPeer(peer, firstFrame)` prepares a previously absent or disconnected machine using retained input history; only activate it after all necessary links and its simulation are ready. The application transfers a confirmed game checkpoint and starts the newcomer with `initialFrame` equal to that checkpoint tick. Supply a matching initial authority input and the host's `CaptureDisconnects()` result. Completed disconnect state is part of admission, even when those old peers have no frogs. Wait for `CanPreparePeer` before starting an admission during disconnect agreement.

Link IDs derive from the lobby session ID, the two peer IDs and their preparation tick. `SessionLink.Identity` exposes the same derivation for a separate spectator receiver. Prior incarnation packets cannot terminate replacement connections. The engine retains historical disconnected intervals when a handle is restarted, rather than changing finalized history.

The application owns room assignment, checkpoint transport, catch-up and activation. GGCS supplies retained inputs and deterministic participation, not automatic peer admission. Retained history remains bounded: prepare a machine only once it can load/simulate, and reject or restart a preparation that falls outside that window. Prepared inactive links must never be used as a substitute for an active player's missing input.

`ISpectatorInputCodec<TInput>` optionally packs only the active controls into a compact confirmed bundle. Its size contributes to the configuration fingerprint. The game uses this for twelve possible machine streams with at most eight frogs; generic sessions retain the default full-input encoding.

## Confirmed pauses and checkpoint continuation

`TryGetConfirmedState(stateFrame, out state)` returns an immutable checkpoint after inputs through `stateFrame - 1` are globally confirmed. It does not prove that every peer has simulated that state. Before a coordinated pause or final departure, call `ConfirmState(stateFrame)` and continue polling until it returns true. This requests a checksum exchange at that exact boundary, including when automatic periodic checksums are disabled. Peers answer requests while paused. Disconnected playing peers are excluded only after their removal is agreed; spectators never delay confirmation.

The application chooses a pause boundary at least as large as both `CurrentFrame` and `LastSubmittedFrame + 1` on every playing machine. The second condition matters when a frame has latched input but cannot yet advance. Stop new advancements at that boundary, continue polling and repairs, then transfer the agreed state.

Call `FreezeTiming(true)` while coordinating a pause. Manual edits are deferred and automatic changes stop until `FreezeTiming(false)` resumes a canceled transition. Export each surviving handle's `PendingInputCount(handle, boundary)` inputs with `TryGetSubmittedInput`, in frame order. Prefix lengths can differ from the current preference after a delay change. In the new generation, set the personal policy, restore bounded temporary delay with `RestoreExtraDelay`, then call `SeedLocalInputDelay(handle, inputs)` before advancement. Seeding preserves already accepted inputs independently of the next-sample delay. Map prefixes by stable player identity when handles change. New identities start with neutral delay frames.

Do not replay a pending button edge twice or silently discard it when restarting the session. The game adapter rejects exporting a prefix while the boundary still has an unconsumed latched submission.

## Spectators

On the playing host, call `AddSpectator(spectatorPeerId)` before simulation begins. On that spectator, create `SpectatorSession<TInput,TState>` with the same roster, generation, options/schema and initial game state, pointing at the host.

A spectator receives only confirmed inputs from the host. It never contributes to player pacing or confirmation. `AdvanceFrame()` consumes one available frame. `BufferedFrames` describes the available backlog. `AdvanceAvailable()` consumes up to `SpectatorCatchUpFrames` and is suitable for catching up from an existing checkpoint. Both continue polling. The default buffering delay is zero. If `SpectatorBufferFrames` is set, use `drain: true` to consume the final buffered frames at a pause or match end.

A slow or absent observer cannot stall the players. Once its bounded input/history buffer is exhausted, it is disconnected with a diagnostic and needs a new checkpoint.

For a late spectator, transfer a state returned by `TryGetConfirmedState(startFrame, out state)` through the application's checkpoint protocol. Load it before constructing the spectator. Use the same `startFrame` in `AddSpectator` and the spectator constructor. The host must still retain the inputs beginning at that frame. Checkpoint transfer and validation are application responsibilities.

### Host without a player

Before frame zero, every playing machine calls `AddInputObserver(hostPeerId)`. Construct a `P2PSession` on the host with a local peer ID absent from the playing roster; `IsInputObserver` is then true. Advance it with an empty local input span and attach ordinary spectators to it with `AddSpectator`. `ConfirmedInputFramesAvailable` provides a bounded backlog count for catch-up scheduling. Observer checksum exchanges validate their simulation without adding them to the playing peers' wait conditions.

The host collects each playing machine's raw input stream and receipt reports, advances confirmed frames, and sends the combined stream to spectators. Its raw observer links are excluded from player pacing/confirmation. If an observer link is lost, the host waits for a possible agreed removal from the active peers. Without that agreement it stops its observing generation after a bounded grace period; it cannot invent a disconnect of a still-playing frog. The application coordinates reconnection/checkpoint recovery.

## Diagnostics and determinism checks

Drain `TryGetEvent` for synchronization, interruption, recovery, disconnection, exclusion, protocol errors, spectator backlog and checksum mismatches. Event queues and checksum histories are bounded. `GetNetworkStats` exposes RTT, frame advantage, remote response/donation/extra-delay preferences, pending/received/acknowledged frames, traffic counters and invalid/stale packets. `NetworkStats` returns the current list of active links, including observer links. Session diagnostics include prediction depth and total/largest resimulation.

Use `SyncTestSession` during offline development with checksums enabled on every snapshot. It replays the recent history after every advancement and throws `DeterminismException` at the first changed state. This catches incomplete snapshots and nondeterministic game logic without involving a network.

## Build and tests

From the repository root:

```bash
source scripts/common.sh
dotnet build src/GGCS.Tests/GGCS.Tests.csproj -c Release -m:1 -nr:false
dotnet .build/bin/GGCS.Tests/release/GGCS.Tests.dll
```

The normal `scripts/test.sh` also runs GGCS. `--no-sockets` skips the loopback UDP test in restricted environments. The suite covers model-based randomized queues/repairs, packet bounds and fuzzing, impaired 2/4/8-machine meshes, multiple local inputs with changing delays, observer/spectator roles, disconnects, clock drift/render hitches, real frog world hashes, explicit paused-state confirmation, seeded input delays and checkpoint restarts. It asserts recovery and simulation speed as well as eventual state agreement.

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
- Inputs have a fixed explicit codec. The wire format uses bounded byte RLE after XOR deltas, independently decodable MTU-sized chunks and a session generation. Chunks are packed by encoded size with a 65,507-byte decoded limit. Queued inputs flush after session work, including stalled advance attempts; new inputs do not wait for another tick. It does not port Serde, Bincode or bitfield-rle, or interoperate with old protocols.
- History and pending-input capacity follow configured limits, independently of the prediction limit. All pending inputs are eligible for transmission; newer chunks are not hidden behind an old fixed-size send window.
- Globally final confirmation is separate from local prediction, protecting observer streams and disconnect corrections. Checksum delivery uses exact-frame acknowledgments and RTT-aware retries; acknowledgment alone does not establish matching confirmed state. The bounded immutable history is retained after delivery.
- Configuration fingerprints travel in the handshake. Input messages pack each receipt/disconnect status into four bytes and include removal-agreement fields only when needed. Timing preferences travel in periodic quality reports and on changes. Paused-peer feedback remains available without resending on every poll.
- Observer buffers are bounded. A host with no local frog is supported explicitly.
- Personal response delay, voluntary simulation lead and bounded automatic delay share an adjusted pacing horizon. Timing uses an injected monotonic clock; there are no spin waits, browser bindings or native code.

## Game integration

The library stays independent of game content, transport APIs and UI. The application supplies these parts:

- `NetworkSession` adapts GGCS frame numbers to persistent world ticks, captures/restores `IRollbackSimulation`, retains reversible events, and exposes diagnostics and confirmed checkpoints. Its `AcceptedLocalSlots` identifies which devices may consume pending button edges; `LocalInputSubmitted` means at least one local handle accepted a sample.
- `RollbackInput` carries gameplay plus tick-stamped lobby spawn, selection, color and team commands. Its explicit codec validates every field. Prediction retains gameplay while clearing lobby command edges.
- `LobbySimulation` snapshots the world, roster, cosmetic RNG, preview events and CPU retaliation state. Match simulation uses the regular world snapshot.
- `MeshLobby` prepares new connections from confirmed history while existing machines keep running. `LobbyNetworkSession` carries machine controls and authority membership in stable streams; room changes use ordinary rollback. Match start alone uses a confirmed pause and a new generation. Empty lobbies retain a neutral host authority stream.
- Steam playing machines use direct Steam Networking Sockets links. LAN/UDP uses direct datagrams; `DatagramReliability` supplies bounded, ordered reliable control messages independently from rollback input traffic. The host handles admission and sends spectators the combined confirmed stream.
- Client accumulators apply GGCS's pacing multiplier to active players. Spectators and a spectating host catch up at twice the normal cadence while more than two confirmed frames are buffered. Polling continues during pauses and stalls.

The game tests in `src/Tests` cover adapter input latching, checkpoint delay continuity, final confirmed-state barriers, deterministic lobby actions, mesh admission/cancellation, eight-machine latency/loss throughput, and separate-process UDP lobby-to-match transitions. Package identity includes the GGCS assembly as well as core, networking and content.

Simulated-network and Linux loopback results do not substitute for live Steam testing or Windows validation.
