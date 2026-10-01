# GGRS reference checks

GGCS follows GGRS's rollback and input delivery design. It has its own C# API and packet format; these checks do not claim wire compatibility or identical session scheduling.

## Sources checked

| Source | Revision | Reference tests run |
| --- | --- | --- |
| `Originals/Research/ggrs-upstream` | `e97e3d2416cc68af2d2876d41180d950c2939b6e` | 64 unit tests and 54 integration tests passed |
| `Originals/Research/ggrs` (TF.EX fork) | `12a2d476653873042df38496386415124693acb9` | Both three-peer disconnect regression tests passed |

The Rust checks ran with Rust 1.87.0. The integration tests require localhost UDP socket access. Both source trees were copied into `.build/checks/ggcs-reference` before building, leaving the research checkouts unchanged. Cargo generated a lockfile in each temporary copy.

## Repeatable traces

`ggrs-traces.json` was produced by running `oracle.rs` inside the pinned upstream GGRS crate. The normal C# test suite embeds this fixture and compares GGCS against it. Rust is needed only to regenerate the fixture:

```bash
python3 src/GGCS.Tests/Reference/regenerate.py --offline
```

Omit `--offline` to download missing Cargo dependencies. `--toolchain` selects another installed Rust toolchain; the default is `1.87.0`. `--baseline-tests` also runs the upstream suite and the added trace test. No Rust build is part of the game or GGCS build.

The fixture contains:

- 45 commands covering initial input delay, increasing delay with repeated input, decreasing delay with discarded duplicate frames, and returning to zero delay.
- 180 time synchronization samples, including the initially empty averaging window, negative values and repeated wraparound of the 30-frame window.
- 23 prediction and correction commands, checking each simulated input and its status, the rollback frame, replayed frames, and the resulting saved game state. Matching late input does not trigger a replay; incorrect input restores the state before the first incorrect frame.

The small trace game uses an integer rolling hash as its state. It is intentionally independent of the game client, renderer, transport and serialization code.

## Lessons that matter for the C# implementation

### Input delay and input sampling

Submitted game frames and delayed input frames are separate sequences. An increase in delay repeats the last input to fill the gap; those filler frames must also reach remote machines. A decrease drops inputs whose delayed frame is already occupied. A stalled simulation must not repeatedly consume the local input for the same frame.

### Rollback and retention

Save the state before applying a frame's inputs. On a mismatch, restore the earliest incorrect frame and replay to the current frame. Inputs used during the original simulation must remain available for comparison, even when their prediction happened to match.

The permitted prediction depth and retained history are separate limits. The TF.EX fork demonstrates why: after one of three peers disappears, the two survivors may have received different final input frames. They must agree on the smaller cutoff and replay with disconnected inputs after that frame. If the required history is gone, the session must report a persistent failure rather than continue with different states.

GGRS upstream treats locally available inputs as confirmed. GGCS reserves final confirmation for frames acknowledged by every retained playing peer. Simulation can still run ahead within the prediction window. Receipt reports alone are insufficient when choosing a disconnect cutoff: one survivor can have an older view of another survivor's received inputs even though that survivor has already published a later frame.

GGCS therefore briefly pauses advancement and confirmation during disconnect agreement. Each survivor advertises its frozen committed frame. The agreed cutoff is the maximum of those frames, and every survivor must acknowledge the same removal mask and cutoff before publication resumes. Earlier departures retain their original cutoffs when a later departure adds to the mask. This protects checksums and spectator streams from corrections to already-published state. It is an intentional difference from upstream, tested with stale receipt reports, simultaneous departures and repeated departures.

### Pacing

GGRS averages both peers' reported frame advantages over 30 frames and recommends half their difference. The transport estimates where the remote simulation is now using measured round-trip time. A fixed limit based only on input acknowledgements turns ordinary latency into a simulation speed limit. Pacing should remain separate from the limit on missing input predictions.

### Spectators and hosts without a frog

Spectators consume a combined stream of confirmed inputs and do not participate in players' confirmation or pacing. The game host can publish this stream even while spectating if it receives each playing machine's inputs as an observer. Its observer links must remain outside the active players' wait conditions. The observing host also waits for the active peers' committed-frame reports; raw receipt reports can contain delayed future inputs that have not been simulated yet. GGRS's ordinary spectator session alone cannot perform this host role because it only receives an already-combined stream from another player.

An observer link failure cannot remove a still-playing frog from the host's spectator stream. The observing host allows a bounded interval for an agreed active-player departure to arrive; otherwise it stops and requests a new checkpoint. Malformed traffic from a spectator or observer only closes that nonplaying link. Both paths have independent regressions.

### Membership and partitions

Membership remains fixed during a rollback generation. Joining players and changing the playing roster require the game to coordinate a confirmed checkpoint and start a new generation. A peer that learns it has been disconnected must not silently continue as an active member of that generation. The rollback protocol is not a general consensus or host migration system; the game still owns admission and restart policy.

## Remaining scope

These reference checks cover selected algorithms and known regressions. They do not replace GGCS's own high-latency, loss, jitter, backpressure, multi-local-player, spectator and malformed-packet tests. Steam connection establishment and lobby checkpoint coordination are application responsibilities, covered separately by the game tests in `src/Tests`. Live Steam behavior still requires testing with real accounts.


### Application boundary regressions

The normal C# suite also exercises integration rules that are outside the pinned Rust trace: seeding an input-delay prefix before a new generation, requesting a final checksum while paused with periodic checksums disabled, and obtaining observer backlog without making that observer part of player pacing. The game adapter suite verifies that a failed advancement consumes local button edges only on its first submission, and that a checkpoint restart retains inputs which were accepted before the pause but belong to later delayed frames.
