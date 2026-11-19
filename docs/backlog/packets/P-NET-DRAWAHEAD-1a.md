# P-NET-DRAWAHEAD-1a: the walk hint on the wire (protocol 19)

- **Serves:** not in the plan: owner approval 2026-11-19 ("Yes please add draw ahead back in", project thread "Cut delay
  on other players"; P-NET-DELAY-1 "Known traps" named this packet). First half of the draw-ahead; P-NET-DRAWAHEAD-1b
  (the Unity client) depends on it.
- **Implementer tier:** implementer-opus (wire, cross-crate: rh-sim, rh-policy, rh-protocol, rh-server, C#, JS)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ `06ebf2e9` (contains P-NET-DELAY-1)
- **D-rules touched:** D1 loose proof (the sim's hint already matches legacy `walkHint.ts`; this packet only carries it);
  the oracle is a round trip through the production publish path · D2 n/a · D5/D6/D9 none (presentation data; the
  client's use of it is 1b, presentation only)
- **Shared-file packet:** `observe.rs`, the protocol and its three decoders, `wire.rs`, the fixtures. Nothing else that
  touches them merges beside it.
- **Gate:** `--regen-fixtures` is required (new `protocol/fixtures/v19.json`). Finish with
  `scripts/gate.sh --quick --regen-fixtures --oracle packet/P-NET-DRAWAHEAD-1a`.
- **Branches:** this packet's oracle is `packet/P-NET-DRAWAHEAD-1a` (a pointer at the last 1a commit).
  `packet/P-NET-DRAWAHEAD-1` carries 1a and then 1b stacked on it; implement 1a from `packet/P-NET-DRAWAHEAD-1a`, so the
  C# tests the gate runs on a codec change do not include 1b's red oracle.

## 1. ID and outcome

`P-NET-DRAWAHEAD-1a`. Every remote PLAYER the viewer is delivered carries the sim's walk hint on the wire: `walk_dir`
(the heading of the walk the server will continue, 256ths of a turn) and `walk_ticks` (whole ticks the server is committed
to keep walking that way, a lower bound). Mobs never carry one; the self record does not need one. Protocol 18 becomes 19;
the Rust, C# and JS decoders read it from Full and delta frames. When this lands, a client can draw a walking remote player
past its newest sample exactly as far as the server has promised (1b).

## 2. Dependencies

None (base has P-NET-DELAY-1). 1b depends on this.

## 3. Relevant legacy paths (verified with `rg`)

- `packages/server/src/sim/walkHint.ts:1-62`: the two presentation mirrors per player; `:44` zeroes every player's ticks
  on a teleport/run end; `:55-62` direction hysteresis and the tick count. Ported already as
  `crates/rh-sim/src/systems/walk_hint.rs` (`update`, `zero_all`), stored as `Player.walk_dir`/`walk_ticks`
  (`crates/rh-sim/src/state.rs:604,606`), saved, restored, hashed and checked (`checkpoint.rs:709` refuses
  `walk_ticks > WALK_HINT_MAX_TICKS`).
- `packages/shared/src/schema.ts:26-35`: `PlayerExtraState.walkDir` (uint8, "meaningful only while walkTicks > 0") and
  `walkTicks` (uint8, 0..WALK_HINT_MAX_TICKS): legacy synced both to every client in the room's state (players only;
  `MobState` has neither).
- `packages/shared/src/constants.ts:21-35`: `EXTRAPOLATE_MS = TICK_MS`, `MOB_EXTRAPOLATE_MS = 25`,
  `WALK_HINT_MAX_TICKS = 3` (owner decision 2: "so a destination never leaks"), `WALK_DIR_HYSTERESIS = 3`.
- `packages/shared/src/interp.ts:4-12`: `encodeWalkDir`/`decodeWalkDir` (`d * PI / 128`).
- `packages/client/src/net/interpolation.ts:93-101`: the client keeps `{t, dir, ticks}` per player from the same patch
  as that player's sample; `packages/client/src/net/remoteDraw.ts:1-21` is the consumer (1b).
- Gate in this sim: walk hints run only where `SimConfig.walk_hints` or the gate preset says so
  (`crates/rh-sim/src/schedule.rs:63-170`; `Gates::PRODUCTION`, `TRAINING`; `SimConfig::live_content()` sets
  `walk_hints: Some(true)`, `statics.rs:448`). `SimConfig::default()` has `None`, so headless, parity and capture worlds
  keep every hint 0 and their wire bytes unchanged.

## Established facts

### Wire design (follow exactly)

- `rh_protocol::WireActor` gains `pub walk_dir: u8` and `pub walk_ticks: u8` (the stub commit adds them; every
  constructor sets 0). `QActor` gains the same two bytes (`from_wire`, `to_wire`, `NEW` = 0, 0).
- **Full frame:** a new actor mask bit `AM_WALK = 1 << 15` (ADR 0011, append only; bit 15 is the first unused bit,
  `masks.rs:49-80`), present only when `walk_ticks != 0`, followed by two bytes `walk_dir, walk_ticks` after the mark
  plate. Absent decodes to `(0, 0)`. `AM_KNOWN` gains it; `full_len` and `full_mask` count it.
- **Delta frame:** a new actor-delta bit `AD_WALK = 1 << 18` (first unused, `delta.rs:115-138`), set when the pair
  `(walk_dir, walk_ticks)` differs from the base, followed by both bytes; `AD_ALL` becomes `0x7_ffff`. A delta that
  stops a walk sends `AD_WALK` with `(0, 0)` and the tracker's actor reads `(0, 0)`.
- `walk_ticks` is a plain u8 count. Do not pack it into bits or assume a maximum of 3: the parallel 40 Hz tick work
  (ADR 0029 as named by the coordinator; not in this base) lets it run 0..6 at 25 ms ticks. The client converts ticks to
  ms with the Welcome's tick rate. Decoders refuse nothing new beyond truncation and the mask bits: any `walk_dir` and
  any `walk_ticks` decode.
- **Canonical form:** `walk_dir` is meaningless while `walk_ticks == 0` (legacy `schema.ts:28`). The observation puts
  `walk_dir = 0` whenever `walk_ticks == 0` (the sim's `Player.walk_dir` keeps its value for hysteresis; only the public
  copy is zeroed). The encoder's actor check (`actor_looks_bad`/`QActor::checked`, `encode/snapshot.rs:69-91`) refuses
  `walk_ticks == 0 && walk_dir != 0` ("walk hint"), so a decoded actor equals `WireActor::quantized` of what was encoded
  (the codec contract, `actor.rs:224-234`). A Full frame never carries `AM_WALK` with ticks 0; a decoder that meets one
  (only a hand-made frame) accepts it as is (no new refusal).
- **Mobs:** the decoders do NOT refuse a hint on a non-player actor. Precedent: `offhand` and `build_marks` are
  player-only facts and `indicator_fault` (`actor.rs:72-92`) does not look at `kind`. The producer is what keeps mobs at
  `(0, 0)`: `IndicatorFacts::mob` style, the observation never sets a hint on a non-player row.
- **Self record:** unchanged (no hint).

### Observation and projection path (where the bytes come from)

- `crates/rh-sim/src/observe.rs:138-167` `ActorView` gains the public pair (e.g. `pub walk_dir: u8, pub walk_ticks: u8`,
  or a small `Copy` struct), filled at the player row builders (`observe.rs:893`, `:1066`; the `vision.rs:373`
  `actor_with` helper) from `Player.walk_dir`/`walk_ticks` with the canonical zeroing above; mobs, dummies, zombies and
  givers `(0, 0)`. Serialized with the observation (serde derives).
- `crates/rh-sim/src/systems/walk_hint.rs:1-2` doc ("No gameplay, input, observer or policy consumes this state") must
  say that the public observation now carries the pair to the wire; nothing in the sim or a policy decides on it.
- `crates/rh-policy/src/delivery/projection.rs:147-174` `wire_actor` copies the pair into `WireActor` (the one
  observation-to-wire conversion; `crates/rh-server/src/wire.rs:10` imports it).
- `crates/rh-server/src/wire.rs:159-178` `same_view` (the `ActorMemo` key) must compare the new fields, or a memo hit
  returns a stale hint: a real trap, the memo test `the_actor_memo_is_always_a_fresh_conversion`
  (`wire/tests/snapshot.rs`) should gain a "walk" edit.
- C-OBS: the pair is derived only from the delivered actor's own path, capped at `WALK_HINT_MAX_TICKS`, zero while the
  player is hidden (`walk_hint.rs` `compute` returns zero for `status::hidden`), and legacy synced it to every client.
  It reveals at most three ticks of heading, never the destination (owner decision 2 in legacy). No DEVIATIONS row.

### Identities

| Identity | Today | After | Why |
|---|---|---|---|
| Protocol | 18 | **19** | New mask bits and bytes: incompatible. |
| Observation | 30 | **31** | `ActorView` gains a field; `docs/CONTRACTS.md:519-524`: "Any new observation field ... bumps `OBSERVATION_VERSION`" (precedents: 29 -> 30 P-FROST-WALL-2, 26 -> 27 P-WIRE17-HUD-2c). |
| Policy contract | 25 | **26** | The projected policy frame (`wire_actor`, `rh-policy/src/delivery/`) carries the pair; every observation bump so far moved it too (`crates/rh-policy/src/lib.rs:40-55` history). Add the version-26 line there: "bots do not decide on it". |
| Checkpoint | 32 | 32 | `walk_dir`/`walk_ticks` are already saved, restored and hashed (`checkpoint.rs:709`, `tests/hash_coverage.rs`). |
| Replay | 25 | 25 | No input or format change. |
| Content hash | `7a542a13187f034c` | unchanged | No content; `rh-sim` does not depend on `rh-protocol`. |

### Existing tests that pin what this packet changes (survey; each is an old oracle, amended by the implementer as listed)

These are oracles of landed packets, not of this one; the gate's `--oracle packet/P-NET-DRAWAHEAD-1a` lets the
implementer edit them. Each amendment below is mechanical and **needs the coordinator's sign-off for the owner** (they
change expectations of landed oracles); nothing else in them may change.

**The protocol-18 stamp oracles (P-PROTOCOL-18-STAMP-1).** Kept as history where they still hold; current-version pins
move to 19, previous-version refusals to 18. File names stay (history of the packet that wrote them).

| File | Test | At 19 | Amendment |
|---|---|---|---|
| `crates/rh-protocol/tests/protocol_18_stamp.rs` | `protocol_version_is_18` | red (17 != 19 style) | delete this one test (this packet's oracle pins 19) |
| same | `v18_fixture_exists_and_its_hello_and_welcome_say_18` | green (reads `v18.json`, kept) | none: history |
| same | `v17_and_v18_differ_only_in_the_version` | green (two history files) | none: history |
| `crates/rh-server/tests/protocol_18_stamp.rs` | `a_protocol_17_hello_is_refused_with_protocol_mismatch` | red (reason names 19) | send 18, expect `"server speaks protocol 19, client 18"`; rename `a_protocol_18_hello_is_refused...` |
| same | `a_protocol_18_hello_is_welcomed_with_protocol_18` | red | send 19, expect 19; rename `..._19_...` |
| `client-unity/RoguehallaNet.Tests/Protocol18StampTests.cs` | `CodecSpeaksProtocol18` | red | 19, rename `CodecSpeaksProtocol19` |
| same | `V18FixtureExistsAndItsHelloSays18` | green (history file) | none |
| same | `AProtocol17WelcomeIsRefusedAndA18WelcomeAccepted` | red ("the Hello says 18") | 18 refused, 19 accepted; rename |
| `tools/unity-browser-reload-proof/protocol-18-stamp.test.mjs` | all three | red (`probe.PROTOCOL`, marker, v18 decode refused at `probe.html:391`, metadata parser `PROTOCOL`) | move to 19 / `v19` / `v19.json` |

**Other version pins (18 -> 19).** `crates/rh-protocol/tests/envelope.rs:34`; `crates/rh-protocol/tests/map_public.rs:330`
(`protocol_version_is_18`, rename); `crates/rh-protocol/tests/frost_wall_wire.rs:131`; `crates/rh-server/src/wire/tests/frames.rs:48`;
`crates/rh-server/tests/ending_metadata.rs:50`; `client-unity/RoguehallaNet.Tests/ProtocolVectorTests.cs:37`
(`FixtureIsProtocolV18`, rename); `client-unity/RoguehallaNet.Tests/WishBarrierProtocolTests.cs:20`;
`tools/unity-browser-reload-proof/check.test.mjs:27,38,180,181,239,240`; `tools/unity-browser-reload-proof/normal-companion.test.mjs:6,13,58,76,77,80,142,161`.

**Digest pins moved by the version u16.** `crates/rh-protocol/tests/map_dressing.rs:532-561`
(`a_map_without_dressing_keeps_today_s_bytes`: `WELCOME_DIGEST`, `SHIPPED_DIGEST`, `ALL_DIGEST`, `GROUND_BOTH_DIGEST`
digest Welcome hexes that contain the version; they moved at 18 the same way, see `:553`). Move to the v19 values; the
lengths stay.

**Mask-bit pins (the new bits are exactly the ones these refusal tests use as "unknown").**

| File:line | Pins | Amendment |
|---|---|---|
| `crates/rh-protocol/tests/v4.rs:382` | `AD_ALL == 0x3_ffff` | `0x7_ffff`, and add `(AD_WALK, 18)` to the bit table above it |
| `crates/rh-protocol/tests/v4.rs:291-317` `undefined_mask_bits_are_refused_never_skipped` | hand-made frames with Full actor mask bit 15 (`0x80 0x80 0x02`) and delta actor bit 18 (`0x80 0x80 0x10`) as undefined | bit 16 (`0x80 0x80 0x04`) and bit 19 (`0x80 0x80 0x20`); comment "(protocol 19: the first bits no field uses)" |
| `crates/rh-protocol/src/tests.rs:5-12` | Full mask bit numbers pinned (first five only) | add `AM_WALK == 1 << 15` (optional) |
| `crates/rh-protocol/src/samples.rs:5409-5410` `actor_reserved_mask_bit` (`1 << 15`) | generator, implementer's file | first undefined bit 16 is already `actor_unknown_mask_bit_after_kind` (`:5461`); use `1 << 17` |
| `crates/rh-protocol/src/samples.rs:7256-7258` `delta_actor_mask_unknown_bit` (bit 18) | generator | bit 19 |
| `crates/rh-server/web/probe.html:798` | `mask & ~0x3ffff` (delta) and `:728` `mask & ~0x7fff` (Full) | `0x7ffff`, `0xffff`, plus the reads |
| `client-unity/RoguehallaNet/Protocol/WireActor.cs:40-42,96,240` | `MKnown`, `MAll = 0x3ffff` | add `MWalk` bits, reads, writes |

**Observation and policy identity pins (30 -> 31, 25 -> 26).** `crates/rh-sim/src/observe/panel_tooltip_tests.rs:322`,
`observe/panel_tests.rs:472`, `observe/hud_clock_tests.rs:224`, `reward_chests/trophy_observer_tests.rs:259`,
`crates/rh-sim/tests/frost_wall_observe.rs:372`; `crates/rh-policy/src/frost_tests.rs:84` (policy 25);
`crates/rh-policy/tests/{frost_wall_delivery,quest_carrier_delivery,hud_roam_terms,hud_clock_delivery,hud_delivery,hud_panel_delivery}.rs`
(each pins observation 30 and policy 25; append "31/26: P-NET-DRAWAHEAD-1a" to the message).

**Struct literals without `..`** (compile-only, done by the stub commit): `crates/rh-protocol/src/snapshot_read.rs:276`,
`delta/actor.rs:102`, `delta/diff.rs:190`, `tests/size.rs:110`, `crates/rh-policy/src/delivery/projection.rs:149`.
`ActorView` literals in tests (`crates/rh-server/src/wire/tests.rs:72` `view`, and any other) gain the fields when the
implementer adds them.

**Generated / follow the constant (no edit).** `crates/rh-protocol/tests/vectors.rs` (compares encoding with the
committed `v<PROTOCOL_VERSION>.json`); `tools/protocol-js/check-*.mjs` (read the version from probe.html's
`const PROTOCOL`); `client-unity/RoguehallaNet.Tests/Util.cs` (opens `v<ProtocolVersion>.json`).

### Must move: production sites (18 -> 19), as at P-PROTOCOL-18-STAMP-1

`crates/rh-protocol/src/lib.rs` (`PROTOCOL_VERSION`), `crates/rh-protocol/src/scene_candidate/mod.rs:26`
(`14 | 18` -> `14 | 19`), `client-unity/RoguehallaNet/Protocol/Codec.cs` (`ProtocolVersion`, doc),
`client-unity/RoguehallaNet/Protocol/SceneCandidate.cs:17` (`!=18` -> `!=19`), `crates/rh-server/web/probe.html:18,25,262`
(prose, marker `// ---- protocol v19 `, `const PROTOCOL = 19`), `tools/unity-browser-reload-proof/{wire-metadata.js:2,15,67,98,
normal-companion.mjs:6,22,24,27, check.mjs:66,67,102, wire-tap-proposal.js:49,52,55,59}`. History comments naming 18 stay.

## 4. New paths

- `protocol/fixtures/v19.json` (generated; `v18.json` stays byte-identical as history).
- At least one new golden vector in `crates/rh-protocol/src/samples.rs`: a Full snapshot with a walking player
  (`AM_WALK`), a delta that sets and one that clears the hint (`AD_WALK`), so the C# and JS decoders are held to the
  bytes, not only the Rust round trip.

## 5. Exclusive write scope

- `crates/rh-protocol/src/{lib.rs, actor.rs, masks.rs, delta.rs, delta/*.rs, encode/*.rs, snapshot_read.rs,
  samples.rs, scene_candidate/mod.rs, tests.rs}` and `crates/rh-protocol/tests/` amendments listed above.
- `crates/rh-sim/src/observe.rs` (+ `observe/` row builders and tests that build `ActorView`), `crates/rh-sim/src/vision.rs`
  (only if `actor_with` builds the row), `crates/rh-sim/src/systems/walk_hint.rs` (doc comment only), the rh-sim
  identity pins listed above.
- `crates/rh-policy/src/{lib.rs (POLICY_CONTRACT_VERSION and its history line), delivery/projection.rs, delivery/frame.rs}`
  and the rh-policy pins listed above.
- `crates/rh-server/src/wire.rs` (`same_view`), `crates/rh-server/src/wire/tests/*.rs`, `crates/rh-server/web/probe.html`,
  `crates/rh-server/tests/{protocol_18_stamp.rs, ending_metadata.rs}` (amendments above).
- `client-unity/RoguehallaNet/Protocol/{WireActor.cs, Codec.cs, SceneCandidate.cs}` (+ any delta reader file) and their
  Unity copies via `bash client-unity/sync-net.sh`; `client-unity/RoguehallaNet.Tests/{Protocol18StampTests.cs,
  ProtocolVectorTests.cs, WishBarrierProtocolTests.cs}` (amendments above).
- `tools/unity-browser-reload-proof/*` (the version moves above), `tools/protocol-js/check-vectors.mjs` (only to name the
  new vectors if it lists vectors by name).
- `protocol/fixtures/v19.json` (generated only).

Out: `client-unity/RoguehallaNet/Net/**` (1b), `client-unity/UnityProject/Assets/Scripts/**`, `docs/CONTRACTS.md`,
`docs/MIGRATION.md` (coordinator), `hosting/**`, every older fixture.

## 6. Contracts and references

C-NET, C-OBS (above), C-PARITY (capture worlds keep hints off: no parity fixture moves), ADR 0002/0010/0011 (append-only
masks), `crates/rh-protocol/AGENTS.md` (three decoders, one branch), `docs/CONTRACTS.md:519-524` (observation field rule).

## 7. Behaviour to preserve

- Every wire byte of a frame whose actors all have `walk_ticks == 0` except the version u16 of Hello/Welcome: that is
  every existing golden vector (no sample actor walks today). Check by the P-PROTOCOL-18 method: `v19.json` equals
  `v18.json` with `011200 -> 011300`, `811200 -> 811300`, header and debug text 18 -> 19, plus only the new vectors and
  the two moved refusal vectors.
- Parity, the state hash, checkpoints and replays: unchanged (no sim state or tick order touched).
- The memo (`ActorMemo`) is still a pure cache.

## 8. Acceptance checks

- `scripts/gate.sh --quick --regen-fixtures --oracle packet/P-NET-DRAWAHEAD-1a` passes (JS vectors, reload-proof tests,
  C# tests and the Unity copy check, rh-sim/rh-policy/rh-protocol/rh-server tests).
- The oracle (below) is green; `git diff packet/P-NET-DRAWAHEAD-1a -- protocol/fixtures/v18.json` is empty.

## Oracle files (read-only for implementers)

- `crates/rh-server/tests/walk_hint_wire.rs`
- `docs/backlog/packets/P-NET-DRAWAHEAD-1a.md`

Implementer-replaceable (stub commit): the two `WireActor` fields in `crates/rh-protocol/src/actor.rs` and the
`walk_dir: 0, walk_ticks: 0` lines in the literals listed above; `public byte WalkDir, WalkTicks;` in
`client-unity/RoguehallaNet/Protocol/WireActor.cs` (declared, not yet decoded: 1b's oracle builds actors with them).

The oracle, `walk_hint_wire.rs` (one test, `a_walking_player_s_hint_round_trips_full_and_delta_frames_and_no_mob_carries_one`):
an open 64 x 40 room, `walk_hints: Some(true)`, a training world (so four practice dummies, kind 3, stand by the
spawns), a viewer and a walker. The walker is ordered due south from where it stands (heading 64/256), stopped, then
ordered due east (heading 0). Every tick the viewer's frames go through the production path
(`rh_server::wire::Publisher::frames` -> `SnapshotData` -> `ConnEncoder` -> `SnapshotTracker`), with a Full frame forced
mid-walk. It asserts: a standing walker decodes `(0, 0)`; the walking walker decodes `(64, 3)` on the forced Full frame and
on at least 20 delta frames; the delta of the stop tick clears it to `(0, 0)`; walking east, deltas set `(0, 3)`; every
decoded non-player actor is `(0, 0)` over at least 50 such rows; and `PROTOCOL_VERSION == 19`.

Measured on the stub tree with a scratch diagnostic that read the sim's own `Player.walk_dir`/`walk_ticks` from the
serialized sim (removed, never committed): the sim's hint is `(64, 3)` from the tick of the order on every one of the 40
walking ticks, `(64, 0)` on the stop tick (the public copy must be `(0, 0)`), `(0, 3)` on every east tick; the four
dummies are delivered on every tick; the stop tick's frame is a delta.

Red today at `walk_hint_wire.rs:200`:

```
assertion `left == right` failed: tick 2 (Delta frame): a player walking a clear lane south carries (64, 3)
  left: (0, 0)
 right: (64, 3)
```

## 9. Validation commands

- `cargo test -p rh-server --test walk_hint_wire`
- `cargo run -p rh-protocol --bin protocol-fixtures`, `bash client-unity/sync-net.sh`
- `cargo test -p rh-protocol`, `cargo test -p rh-sim`, `cargo test -p rh-policy`, `cargo test -p rh-server`
- `dotnet test client-unity/RoguehallaNet.Tests`
- `node tools/protocol-js/check-vectors.mjs` (and the other `check-*.mjs`), `node --test tools/unity-browser-reload-proof/*.test.mjs`

## 10. Risk and suggested model tier

Wide and cross-language: five crates' worth of pins, three decoders, and two refusal vectors that sit exactly on the new
bits. Opus (CLAUDE.md rule 2: cross-crate and wire).

## 11. Known traps

- `same_view` in `wire.rs` is the memo key: forget the new fields and the hint freezes at whatever the actor first showed.
- The unknown-bit refusal vectors and `v4.rs` frames use bits 15 (Full) and 18 (delta), the bits this packet defines:
  move them, or they stop being refusals and the vectors silently change meaning.
- The varint Full actor mask grows to three bytes for a walking player (bit 15); `full_len` must count it
  (`MAX_ACTOR_LEN` in `delta.rs` must still cover the longest record: two more bytes).
- `walk_dir` while `walk_ticks == 0`: zero it in the observation, refuse it in the encoder, never "repair" it in the codec.
- Delta encoder: the bit compares the pair, so a heading change under hysteresis with equal ticks still sends.
- `zero_all` runs after Teleported/RunEnded (`walk_hint.rs:224`): a teleport's frame carries `(0, 0)` (not a stale hint).
- Regenerate, never hand-edit `v19.json`; run `sync-net.sh` after the C# change; do not commit `BakedContent.cs`.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
