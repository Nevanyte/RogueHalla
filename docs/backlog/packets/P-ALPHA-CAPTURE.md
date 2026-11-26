# P-ALPHA-CAPTURE — Private Saturday alpha capture

## ID and outcome

P-ALPHA-CAPTURE serves Phase 4, item 18 of [ORDER_OF_OPERATIONS](../../ORDER_OF_OPERATIONS.md) and the owner-approved Saturday alpha plan. The public Source Arena endpoint can opt in to first-match metrics and complete authoritative replays independently. Operators can recover finalized encrypted files after a process restart.

## Dependencies

Base `195ae90d0196d16138cfcd4f99bfa74c556c9449`, the existing Source Arena admission and [replay recorder](../../../crates/rh-sim/src/replay.rs), the `age` executable on the service PATH, and an operator-provided X25519 age recipient. The offsite uploader uses the finalized files on disk and runs separately from the match process.

## Relevant legacy paths

`packages/server/src/sim/regen.ts:19` uses time since the last landed hit dealt or taken to measure combat. `packages/server/src/sim/arena.ts:385` handles sudden death. The capture hooks read the Rust sim's settled hit ledger and lifecycle. This packet ports no gameplay mechanic.

## New paths

- `crates/rh-server/src/alpha_capture/` contains capture options, metric aggregation, the bounded recorder adapter, encrypted spool writers, and their tests.
- `crates/rh-server/src/room/tests/alpha_capture.rs` checks room integration.

## Exclusive write scope

The new capture module, scoped server startup, control health, director room creation and room methods, the room tests, and this packet. The release workflow retains ownership of deployment and uploader installation.

## Contracts and references

[C-DET, C-RNG and C-OBS](../../CONTRACTS.md), [ADR 0007](../../adr/0007-determinism.md), and [P-ALPHA-SERVER-1](P-ALPHA-SERVER-1.md). Replay JSON uses the existing `rh-replay` format. Its header contains the match UUID, recording build, content hash and step configuration. Per-step hashes and inputs permit deterministic verification under the recording build.

## Behaviour to preserve

The existing input, phase, ordering and publication paths continue to decide gameplay. No additional database work, network I/O, filesystem I/O or encryption is performed by room hooks. Replay opt-in adds the existing recorder's per-step state hash on the room task. Server timing metrics include that work. The normal result and reconnect classifications keep their source data and publication behavior.

## Acceptance checks

- Source Arena startup accepts `--alpha-metrics-dir DIR --alpha-metrics-key-file FILE` for metrics and `--alpha-replay-dir DIR` for replays. Either stream uses `--alpha-capture-recipient age1…`. Both default off. A configured stream that cannot start is visible on private `/control/health` under `alphaCapture.metrics` or `alphaCapture.replays`. An available stream continues when its peer fails startup. Dev-auth joins produce no metrics.
- Generate the metrics key as exactly 32 random binary bytes. Give the Source service read access and leave group/other access clear. Use a stable key across restarts of the reporting cohort. The server writes one `metrics-<match hex>-<seat id>.json.age` per exited authenticated seat, including a seat that leaves in the lobby. Select the earliest admission per `profilePseudonym` by `admittedAtUnixMs` for first-match analysis. Both HMAC identity namespaces, durable profile ID and normalized static account name, produce fixed-length pseudonyms.
- `rh-alpha-metrics/1` includes admission, raid start, first accepted movement and attack, command counts, first settled fighter damage dealt and taken, deaths, successful resumes, disconnections and exit classification. Intake timestamps use the latest completed sim tick. `moves` includes Move and AttackMove, and `attacks` includes Attack and AttackMove. Combat ticks cover the positive landed-hit tick and later active standing ticks inside the source three-second combat window. Arena-only damage has no landed fighter row. Metrics carry no raw callsign, credential, IP address, keyboard data or command payload.
- A room that reaches the authoritative raid-end step emits `replay-<match hex>.json.age` immediately. The replay includes the normal record of room and bot commands, private seeds and profile names. Treat the age identity and decrypted data as operator-only. Lobby-only, aborted and still-running matches produce no replay file.
- Replays stop and count a drop at 144,000 captured steps or 250,000 inputs. Serialization has exact 64 MiB replay and 4 KiB metric plaintext ceilings. The separate queues accept two pending replays and 128 pending metric rows, plus one in-flight job each. Replay spool limits are 128 files and 2 GiB. Metric spool limits are 4,096 files and 32 MiB. A shed replay never yields a truncated final file.
- Use private spool directories with one server writer per stream directory on persistent storage. Writers stream JSON through `age`, sync encrypted output, atomically rename it, and sync the directory. Dot-prefixed partial files are removed by the writer on restart. The uploader can reopen finalized files and owns successful offsite delivery and local removal. Disk, queue and write failures are counted in private control health. Graceful server shutdown allows up to five seconds for the queues to finish while result writes drain.

## Validation commands

```sh
cargo test --locked -p rh-server --lib alpha_capture
cargo test --locked -p rh-server --lib alpha_capture -- --include-ignored
cargo clippy --locked -p rh-server --all-targets --all-features -- -D warnings
scripts/gate.sh --base 195ae90d0196d16138cfcd4f99bfa74c556c9449
```

The second command requires `age` and `age-keygen` on PATH. The real encryption proof completes a 40 Hz Source room, reopens its spool and checks the decrypted replay's final hash and every recorded step with the existing verifier. The private spool test also checks restart cleanup, age failure, byte and disk caps, mode and round-trip decryption.

## Risk and suggested model tier

Use the normal server security review. An enabled recorder adds per-step hash cost and can shed complete captures under disk or queue backpressure. The focused tests establish local capture and restart behavior. Production capacity, host replacement, offsite recovery and rollout require their own receipts.

## Known traps

Keep the replay spool outside the served Web tree. The recipient is public. Its decryption identity must stay with operators. Disable either stream by removing that stream's flags while keeping the shared recipient if the other is enabled. Compile the deployed server with the actual commit identity in `RH_BUILD`, and preserve that build for verification. The existing source setup and 40 Hz proof use the `source-arena` CLI spelling.
