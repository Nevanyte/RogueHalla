# P-ALPHA-JOIN-CAPACITY: seat a first-join burst within available capacity

- **Serves:** ORDER_OF_OPERATIONS phase 4, item 20 (staging load at playtest scale)
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus
- **Integration base:** deployed staging revision `dbaa701be`
- **D-rules touched:** D1 and D2 n/a (server admission); D5 and D9 n/a; D6 keeps existing admission and disconnect logging

## 1. ID and outcome

P-ALPHA-JOIN-CAPACITY covers the Saturday 16-player playtest. During staging's 21:08–21:12 UTC test window, bursts
of eight legitimate joins to a cold process left two or three players per burst with
`RateLimited: quest map unavailable; try again shortly`. Staging recorded seven matching rejections alongside normal
token verification. The ordinary join path now seats an authenticated burst in as many available lobbies as it needs.

## 2. Dependencies

The deployed Source arena with per-match quest board map preparation.

## 3. Relevant legacy paths

- `packages/client/src/net/connection.ts:119-122`: clients join or create a Colyseus lobby.
- `packages/server/src/rooms/RaidRoom.ts:421-434`: authenticate the callsign before taking a seat. The regression is in
  the Rust director's board preparation path, so no legacy gameplay numbers enter the oracle.

## 4. New paths

- `docs/backlog/packets/P-ALPHA-JOIN-CAPACITY.md`

## 5. Exclusive write scope

- `crates/rh-server/src/director.rs`
- `crates/rh-server/src/director/admission.rs`
- `crates/rh-server/src/director/spawn.rs`
- `crates/rh-server/src/director/tests/admission.rs` (oracle commit)

## 6. Contracts and references

- C-NET and C-HOST cover compatible Hello/Welcome behavior, room capacity, and drain.
- C-OBS and C-RNG keep board preparation and its secrets on the server.
- `crates/rh-server/src/director/admission.rs:140-242`: ordinary joins check for a lobby and leave the director lock to
  prepare a board room. Before this change, every join missing the lobby could start its own preparation.
- `crates/rh-server/src/director/spawn.rs:5-6,108-128`: preparation uses a process-wide two-worker bound
  and returns an immediately refused request when a worker is unavailable.

## 7. Behaviour to preserve

Keep credential, profile, and fenced lease checks. Keep the single director lock on the available-lobby path and
release it before map preparation. Keep seat and room limits, pre-admit/control semantics, drain and cancellation
handling, the global map-worker bound, and the existing quest-map error for actual preparation failures. Build each
new room with one independent match secret set and the existing map validation path.

Existing tests in `crates/rh-server/src/director/tests/admission.rs` cover lobby-close retries, the leave deadline,
canceled leases and joins, and storage errors. `crates/rh-server/tests/runtime_admission.rs` covers pre-admission in
Source and both ordinary and hosted training. `crates/rh-server/tests/control.rs` covers admit restoration and drain
of an unclaimed room.

## 8. Acceptance checks

- Sixteen concurrent valid ordinary arrivals to an empty two-room Source arena director each receive a Welcome.
  Exactly two rooms seat eight players apiece. A further valid arrival receives the normal capacity refusal.
- The oracle fails by assertion against the deployed code and passes with the fix.
- The coordinator runs the integrated full gate.

## Oracle files (read-only for implementers)

- `crates/rh-server/src/director/tests/admission.rs`

## 9. Validation commands

- `cargo test -p rh-server --lib director::tests`
- `cargo test -p rh-server --test runtime_admission --test control`
- `cargo fmt --all --check`

## 10. Risk and suggested model tier

Admission concurrency is security-sensitive. Keep synchronization out of paths that can already reserve a seat.
Review how the guard and global permit behave if the admitting task is canceled while its blocking worker runs.

## 11. Known traps

- Recheck room, profile, and drain state after waiting and again after preparation. A successful competing admission
  can fill the last seat or make its profile unavailable.
- A blocking worker can survive cancellation of its awaiting future. Dropping the creation guard while that worker
  still runs would let the next ordinary join start redundant work.
- Release the guard before the first seat's lease or room reply is awaited.
