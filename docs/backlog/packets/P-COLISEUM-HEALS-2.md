# P-COLISEUM-HEALS-2: clients see the four coliseum heal pickups (prop kind 8, protocol 20)

- **Serves:** not in the plan: owner request 2026-11-19 (the grand coliseum drawing's four "Heals"), the second half of
  P-COLISEUM-HEALS-1 (its §1 "Split"). The owner approved pushing and merging this work, including the protocol-pin
  amendments listed in §5, on 2026-11-20 (relayed by the coordinator).
- **Implementer tier:** implementer-opus (wire + three decoders + Unity: cross-language, `rh-protocol/AGENTS.md`)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework`; stacked on `claude/project-thread-6foy8h` @ `32da0262` (= `feature/unity-rework`
  @ `d58fd1e7` + the reviewed P-COLISEUM-HEALS-1, PR #75). Gate with `--base claude/project-thread-6foy8h`.
- **D-rules touched:** D1 loose proof (no legacy counterpart; the encoding is the cannon's, pinned by golden vectors) ·
  D2 n/a · D5/D6/D9 none (no client prediction: the pickup is drawn from delivered state only; Unity holds no rule)

## 1. ID and outcome

On a grand-coliseum map every viewer who can see the coliseum centre receives the four heal pickups as props of a new
kind, **`Heal { ready_at }`, prop kind byte 8**, encoded exactly like the travel cannon (kind 6): `handle varint | x u16 |
y u16 | 8 u8 | ready_at varint` (server ms the pickup is available again; 0 when available). The sim's item view offers
them (`PropKind::Heal { ready_at }`, the same `ready_ms` clamp as the cannon), the policy projection carries them to
`WirePropKind::Heal`, the Rust, C# and JS decoders read them, and Unity draws each one with existing art: lit (a glow)
when `ready_at <= server time`, dim otherwise. The F key never targets a heal pickup (it holds no item; never the focus),
and the bots ignore them (neither a cannon nor a chest nor a container). `PROTOCOL_VERSION` 19 → **20**.

## 2. Dependencies

P-COLISEUM-HEALS-1 (in the base): `ContainerKind::Heal`, `systems/heal_pickups.rs`, the four containers at tiles
(79,79), (80,79), (79,80), (80,80) in NW, NE, SW, SE order; `item_view.rs:302-305` and `:367-368` skip them today.
Protocol 19 (P-NET-DRAWAHEAD-1a, in the base) uses Full actor bit 15 and delta actor bit 18; this packet uses **no actor
mask bit** (and must not use 15/17/18/19). The 40 Hz thread rebases to protocol 21 after this lands (coordinator's note).

## 3. Relevant legacy paths

Legacy has no heal pickup (HEALS-1 §3). The pieces reused:
- `packages/server/src/sim/consumables.ts:53-73` (the heal itself, HEALS-1).
- The cannon prop, the template of the encoding: legacy `readyAt` countdown; port `crates/rh-protocol/src/state.rs:121-124`
  `WirePropKind::Cannon { ready_at }`, `state/props.rs:93,127,160-163,199-201` (check, len, write, read),
  `crates/rh-sim/src/item_view.rs:54-57,364-366` (`PropKind::Cannon`, `ready_ms` at `:174-180`),
  `crates/rh-policy/src/delivery/collections.rs:79-81` (projection).

Port paths this packet touches:
- `crates/rh-protocol/src/state.rs:108-130` `WirePropKind` (stub appends `Heal { ready_at: u32 }`, last).
- `crates/rh-protocol/src/state/props.rs:71-230` check/len/write/read: kind 8 is read as `ready_at: varint`; any other
  byte still `BadField("prop kind")`.
- `crates/rh-protocol/src/samples.rs:1769-1798` (`state_union_props`, the cannon vector), `:7885-7902` (the decoded-JSON
  of prop rows), `malformed_server()` at `:4522`.
- `crates/rh-protocol/src/lib.rs:210` `PROTOCOL_VERSION`; `src/scene_candidate/mod.rs:26` (`matches!(PROTOCOL_VERSION, 14 | 19)`).
- `crates/rh-sim/src/item_view.rs:45-69` `PropKind` (stub appends `Heal { ready_at }`, last: `PropCue` derives `Ord`, so
  every existing kind keeps its order), `:300-305` (the skip before the visible-prop count), `:367-368`.
- `crates/rh-policy/src/delivery/collections.rs:56-95` projection; `crates/rh-policy/src/bots/perception.rs:282-290` and
  `bots/temporal_percept/brain/compose14.rs:741-749`: `Heal` rows are skipped (`continue`, as `QuestItem`).
- C#: `client-unity/RoguehallaNet/Protocol/State.cs:58` `PropKind`, `StateItem.cs:268-277` (read), `StateItems.cs:44-67`
  (check, write), `Net/Collections.cs:8` `PropAppearance`, `:405-425` (PropView), `Net/Ui/WorldIndicatorRules.cs`,
  `Protocol/Codec.cs:6-9` `ProtocolVersion`, `Protocol/SceneCandidate.cs:17`.
- JS: `crates/rh-server/web/probe.html:262` `PROTOCOL`, `:1008-1017` the prop reader, the `// ---- protocol v19 ` marker.
- Unity: `client-unity/UnityProject/Assets/Scripts/View/ArenaView.cs:436-496` (prop drawing; `_sourceTimeMs` is the server
  clock it already uses for Wish readiness at `:535`), `Presentation/WorldIndicators.cs:52-76` (labels).

Owner decisions searched (`docs/OWNER_DECISIONS.md`: "heal", "coliseum", "prop kind", "protocol"): none on the subject.

## 4. Design

**Wire (protocol 20).** `WirePropKind::Heal { ready_at: u32 }` appended last; byte 8; `len` = `varint_len(ready_at)`;
`check` accepts it; it may never be the focus's body-hint row (the existing rule already says so). A Heal row's bytes
are the cannon row's bytes with the kind byte 6 → 8. No mask bit, no new State key, no delta change (props travel in
State key 2, replaced whole). Bump `PROTOCOL_VERSION` to 20 and regenerate `protocol/fixtures/v20.json` with
`cargo run -p rh-protocol --bin protocol-fixtures` (v19.json stays as history). Two vectors are added in `samples.rs`:
- server `state_heal_props`: `ServerMsg::State { tick: 202, items: [Props { rows, focus: None }] }`, rows (handle, x, y,
  ready_at) = (1, 20352, 20352, 0), (2, 20608, 20352, 30_000), (3, 20352, 20608, u32::MAX), (4, 20608, 20608, 0): the
  four spots in 1/8 units (tile centres 2544 and 2576 times 8);
- malformedServer `state_prop_kind_unknown`: one prop row with kind byte 9 (`BadField("prop kind")` in every decoder).
The decoded-JSON helper at `samples.rs:7885` gains `Heal => 8` and `"readyAt"`.

**Sim (`item_view.rs`).** Drop the HEALS-1 skip: a visible `Heal` container becomes `PropKind::Heal { ready_at:
ready_ms(c.ready_at) }` after the vision check and **inside the visible-prop count** (`TooManyProps` counts it like any
prop). It is never the focus: the focus rule needs items (`!c.items.is_empty() || Cannon`), and a heal holds none; keep
it so. Public: position and readiness are what anyone at that spot sees (C-OBS); no owner, no taker.

**Policy.** `collections.rs` maps `PropKind::Heal { ready_at }` → `WirePropKind::Heal { ready_at }`. HumanBot perception
and the Brain's compose14 skip Heal rows (no `ContainerView`, no `PropRef`): a bot walking over one still takes it (the
rule is walking), but it never plans for it as loot or a cannon.

**C#.** `PropKind.Heal = 8`, `PropAppearance.Heal = 8` (the stub adds both), read/write/check as the cannon (ReadyAt),
`Wire.ProtocolVersion = 20`, `SceneCandidate` accepts 14 or 20, then `bash client-unity/sync-net.sh`.
`WorldIndicatorRules.HealReady(uint readyAt, double serverMs)` (stub: `false`) is the one readiness rule both Unity
presenters call: `serverMs >= readyAt`.

**JS.** `probe.html` `PROTOCOL = 20`, marker `// ---- protocol v20 `, prop kind 8 → `{ kind: 8, readyAt: r.varint() }`.

**Unity.** `ArenaView` draws `PropAppearance.Heal` with existing art (the chest sprite or a heal rune; no new asset):
when `WorldIndicatorRules.HealReady(p.ReadyAt, _sourceTimeMs)` a `glow` sprite under it and the art at full colour;
otherwise no glow and the art dimmed. No label is required (`ContainerLabel(8, …)` may return null).

**Identities.** No `CHECKPOINT_FORMAT` or `REPLAY_FORMAT` change. `OBSERVATION_VERSION` (31) and
`POLICY_CONTRACT_VERSION` (26): **not bumped** by this brief's default (no field added; a new value of an existing
field, absent on every non-grand map, so the ability hash tape and every pinned observation stay byte-identical).
Coordinator question: if the observation contract counts a new `PropKind` variant as a shape change, bump to 32/27 and
re-record `parity/abilities/hash-tape.json` as P-NET-DRAWAHEAD-1a did (`80c7f966`); the oracle does not pin either way.

## 5. Exclusive write scope

Implementation:
- `crates/rh-protocol/src/{lib.rs, state.rs, state/props.rs, samples.rs, scene_candidate/mod.rs}`;
  `protocol/fixtures/v20.json` (new, generated only).
- `crates/rh-sim/src/item_view.rs` (the `Heal` arms only).
- `crates/rh-policy/src/delivery/collections.rs`, `crates/rh-policy/src/bots/perception.rs`,
  `crates/rh-policy/src/bots/temporal_percept/brain/compose14.rs`.
- `crates/rh-server/web/probe.html`.
- `client-unity/RoguehallaNet/Protocol/{Codec.cs, State.cs, StateItem.cs, StateItems.cs, SceneCandidate.cs}`,
  `client-unity/RoguehallaNet/Net/Collections.cs`, `client-unity/RoguehallaNet/Net/Ui/WorldIndicatorRules.cs`, and their
  copies under `client-unity/UnityProject/Assets/RoguehallaNet/` (by `sync-net.sh` only).
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.cs` (heal drawing),
  `client-unity/UnityProject/Assets/Scripts/Presentation/WorldIndicators.cs` (only if a label is added).
- `tools/protocol-js/check-vectors.mjs` (optional: a `state_heal_props` line beside `state_union_props`; it is a pinned
  path of the card provenance audit only if the audit says so: run `node tools/provenance/check-card-chain.mjs`, and if
  it goes red, leave the file and name it in the hand-off).
- Docs: `crates/rh-protocol/src/lib.rs` header, `client-unity/RoguehallaNet/README.md` protocol mention.

Protocol-19 pins, amended to 20 (owner-approved 2026-11-20; change only the number and the version wording, nothing else):
- Rust: `crates/rh-protocol/tests/frost_wall_wire.rs:129-132` (`guard_the_protocol_is_19`), `tests/map_public.rs:329-331`
  (`protocol_version_is_19`), `tests/envelope.rs:34`, `crates/rh-server/src/wire/tests/frames.rs:48`,
  `crates/rh-server/tests/ending_metadata.rs:50`, `crates/rh-server/tests/protocol_18_stamp.rs:100-139` (18 refused
  naming "server speaks protocol 19, client 18" → the refused Hello names 20; the welcomed Hello is 20),
  `crates/rh-server/tests/walk_hint_wire.rs:248` (`PROTOCOL_VERSION == 19` → `>= 19`, or 20: the hint is still protocol
  19's), `crates/rh-protocol/tests/v4.rs:293-294` (comment only).
- C#: `client-unity/RoguehallaNet.Tests/Protocol18StampTests.cs` (`CodecSpeaksProtocol19`,
  `AProtocol18WelcomeIsRefusedAndA19WelcomeAccepted`: the codec speaks 20, a 19 Welcome is refused, a 20 accepted),
  `ProtocolVectorTests.cs:37` (`FixtureIsProtocolV19`), `WishBarrierProtocolTests.cs:20`.
- JS: `tools/unity-browser-reload-proof/protocol-18-stamp.test.mjs` (v19 → v20, 19 → 20), `check.mjs:66,67,102`,
  `check.test.mjs:27,38,47,52,180,181,239-240`, `normal-companion.mjs:6,22-27`, `normal-companion.test.mjs:6,13,58,76-80,142,161`,
  `wire-metadata.js:2,15,67,98`, `wire-tap-proposal.js:49-59`.

Not in scope: `crates/rh-sim/src/systems/heal_pickups.rs` and every sim rule, `observe.rs`, `schedule.rs`, `content.rs`,
`wire.rs`, any actor mask, `Cargo.lock`.

## 6. Contracts and references

C-NET (three decoders, one set of vectors; ADR 0011 append-only), C-OBS (the pickups are public: position and
readiness only; no taker), R-AUTH/ADR 0008 (Unity draws delivered state, decides nothing), C-PARITY (no sim or draw
change: every parity fixture and legacy-world hash unchanged), `rh-protocol/AGENTS.md` invariants.

## 7. Behaviour to preserve

- Every non-heal prop's bytes: `state_union_props` and every other v19 vector keep their hex in v20.json (only the
  protocol stamp in Hello/Welcome vectors moves).
- No sim state, draw or hash change; `systems::heal_pickups` tests stay green; no non-grand observation changes.
- The F focus never lands on a heal pickup.

Existing tests that pin what this packet changes (survey):
- The protocol-19 pins in §5 (version numbers only; amended by the implementer, owner-approved).
- `crates/rh-protocol/tests/vectors.rs`: generated vectors equal the committed `v<PROTOCOL_VERSION>.json` (moves to v20).
- `tools/protocol-js/check-vectors.mjs:104-108`: `state_union_props` rows (unchanged bytes, stays green).
- `client-unity/RoguehallaNet.Tests/UnionProtocolTests.cs`, `StateTests.cs`: Cannon/WindEmblem props round-trip
  (unchanged).
- `client-unity/UnityProject/Assets/Tests/Editor/ArenaPropCueEditModeTests.cs`: wind/cannon/chest silhouettes
  (unchanged; heal must not draw the cannon's barrel or the wind rune).
- `crates/rh-sim/src/systems/heal_pickups_tests.rs`: rules unchanged; nothing there reads the item view.
- `crates/rh-sim/src/systems/cannons_source_tests.rs:481`: `PropKind::Cannon { ready_at: 0 }` focus (unchanged).

## 8. Acceptance checks

- `cargo test -p rh-protocol --test heal_prop_wire`, `cargo test -p rh-policy --lib bots::heal_prop_bots_tests` green.
- `dotnet test client-unity/RoguehallaNet.Tests --filter FullyQualifiedName~HealPropProtocolTests` green;
  `bash client-unity/sync-net.sh --check` clean.
- `node --test tools/unity-browser-reload-proof/heal-prop-probe.test.mjs` green; `node tools/protocol-js/check-vectors.mjs`
  green.
- Unity EditMode `HealPropEditModeTests` green where an Editor exists (none on the oracle author's machine: not run).
- `scripts/gate.sh --quick --regen-fixtures --allow-protected --oracle packet/P-COLISEUM-HEALS-2 --base claude/project-thread-6foy8h`
  (the pins under `tools/`/tests are not protected paths; `--allow-protected` only if the gate asks for it).

## Oracle files (read-only for implementers)

- `crates/rh-protocol/tests/heal_prop_wire.rs` (`oracle:`)
- `crates/rh-policy/src/bots/heal_prop_bots_tests.rs` (`oracle:`)
- `client-unity/RoguehallaNet.Tests/HealPropProtocolTests.cs` (`oracle:`)
- `tools/unity-browser-reload-proof/heal-prop-probe.test.mjs` (`oracle:`)
- `client-unity/UnityProject/Assets/Tests/Editor/HealPropEditModeTests.cs` and its `.meta` (`oracle:`; not run here)
- `docs/backlog/packets/P-COLISEUM-HEALS-2.md` (`brief:` / `oracle:`)

Stub commit (the implementer may edit these): `crates/rh-protocol/src/state.rs`, `state/props.rs`, `samples.rs` (the
`Heal` arms), `crates/rh-sim/src/item_view.rs` (`PropKind::Heal`), `crates/rh-policy/src/delivery/collections.rs`,
`bots/perception.rs`, `bots/temporal_percept/brain/compose14.rs`, `bots/mod.rs` (the test `mod` line),
`client-unity/RoguehallaNet/Protocol/State.cs`, `Net/Collections.cs`, `Net/Ui/WorldIndicatorRules.cs` and their Unity
copies.

## 9. Validation commands

- `cargo test -p rh-protocol --test heal_prop_wire`
- `cargo test -p rh-policy --lib bots::heal_prop_bots_tests`
- `cargo run -p rh-protocol --bin protocol-fixtures`
- `dotnet test client-unity/RoguehallaNet.Tests --filter FullyQualifiedName~HealPropProtocolTests`
- `node --test tools/unity-browser-reload-proof/heal-prop-probe.test.mjs`

## 10. Risk and suggested model tier

implementer-opus / reviewer-opus: a protocol bump across three decoders and their fixtures, about twenty version pins,
and the Unity drawing. Small logic; the risk is a missed pin or a vector byte moving that should not.

## 11. Known traps

- `PropKind` and `WirePropKind` derive `Ord`/are matched exhaustively: append `Heal` last; never reorder.
- The visible-prop limit: the heal rows count toward `MAX_VISIBLE_PROPS` like any other (move the check, do not add a
  second one).
- The stub encodes a Heal row as a cannon (kind 6) and the decoders refuse 8: that is why the oracle is red; replace it.
- `scene_candidate` in Rust and C# names the protocol numbers it accepts (14 | 19): move 19 → 20 in both.
- `probe.html`'s codec marker comment is read by `check-vectors.mjs`, `normal-companion*.mjs` and the stamp test.
- Never encode a Heal as a Chest: bots would walk to it and press F for nothing.

## Notes from previous attempts
