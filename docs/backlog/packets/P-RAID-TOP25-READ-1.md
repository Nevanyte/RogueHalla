# P-RAID-TOP25-READ-1: the quest bots read the quest numbers from the sim's table, not from hand copies

- **Serves:** not in the plan: owner direction (balance patch, `docs/backlog/BALANCE_LAB_PATCH.md`): it must land before any
  quest retune, which would otherwise drift the bots unseen.
- **Implementer tier:** implementer-opus (cross-crate: rh-sim and rh-policy)
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-86` @ `29d412b12`.
- **D-rules touched:** D1 n/a (no number changes) · D2 n/a · D5/D6/D9 none
- **Decisions in `docs/OWNER_DECISIONS.md`:** none found for this subject (searched "bot", "quest", "TOP25").

## 1. ID and outcome

`RaidBot`'s quest executors (`crates/rh-policy/src/bots/raid/quest/`) copy quest numbers from `TOP25`
(`crates/rh-sim/src/quests/board/tuning.rs`, `pub(crate)`) by hand. After this packet rh-sim has ONE small public read-only
item, `rh_sim::quests::public_rules() -> QuestPublicRules` (a plain `Copy` struct built from `TOP25` at each call), and
rh-policy reads every number below through it and keeps no copy. No number changes; no behaviour changes.

### The copies (verified at the base, lines are base lines)

| # | File:line | Copy | Source of truth | Player-visible? |
|---|---|---|---|---|
| 1 | `walker.rs:63` | `TIMER_BASE_S = 15.` | `TOP25.route_slack_ms` 15_000 ms (`tuning.rs:147`) | No (the journal shows the countdown, not the formula) |
| 2 | `walker.rs:253` | inline `/ 1.5` | `TOP25.route_pace` 1500 (`tuning.rs:146`) | No (as 1) |
| 3 | `dwell.rs:52` | `HOLD_S = 45.` | `TOP25.fort_ms` | Yes: objective text "for 45 seconds" (`tuning_tests.rs:454-` pins text to the field) |
| 4 | `dwell.rs:53` | `STAY_S = 30.` | `TOP25.meditate_ms` | Yes, same |
| 5 | `dwell.rs:63` | `FORT_RANGE = 72.` (used `dwell.rs:301`, `FORT_RANGE * 4.`) | `TOP25.fort_range` | No (in-game text does not give it). **Missed by the reviews.** |
| 6 | `decide.rs:26` | `COST_STACKS = 20.` | `TOP25.bargain_cost` | Yes: reward text (`tuning_tests.rs` pins `bargain_cost`, `bargain_stacks` to the text) |
| 7 | `decide.rs:27` | `GAIN_STACKS = 24.` | `TOP25.bargain_stacks` | Yes, same |
| 8 | `walker.rs:53`, `dwell.rs:59`, `decide.rs:37` | `STAND_U = 32.` (three copies) | independent hand number, exactly half of `TOP25.contact_range` (= `QUEST_TURN_IN_RANGE` 64, `constants.rs:391`); the comments say "the server's contact range is 64 u" | contact range: documented (`docs/quests.md` 77), not in game text |

Checked and left: `walker.rs` `SAFE_SLACK` 1.2 and `dwell.rs` `SAFE_SLACK` (bot margin, duplicated but not a sim number),
`DROP_NEAR_U` 160, `INSIDE_U` 48 (bot heuristic under `fort_range`; independent), `HOLD_PATIENCE_BASE_S`, the ban and
stall timers, `MONK_DANGER_U`, the 0.8 / 0.7 / 0.85 success guesses (marked design guesses in the code), `LOW_HP`,
`BARGAIN_TO_HUD` (a stat-slot mapping, not a quest number), `SPELL_SLOTS` (already `quest/mod.rs:19`, a bot rule that
mirrors the 2-slot rule; not in `TOP25`). Client side (NOT in scope, another session owns it): `QuestTracker.cs`
`DwellSeconds` and `QuestJournalView.cs:335-336` hard-code the same 45 and 30: the same drift on the client.

## 2. Dependencies

Base `coord/int-86`. Later packet P-RAID-Q3b adds decisions to `decide.rs` after this one. P-RAID-E (in review) owns
`exec/elite.rs` and appends to `values.rs`: this packet touches neither.

## 3. Relevant legacy paths

None. The Top 25 board is Rust-native (`docs/quests.md`); the oracle is the current Rust code at the base.

## Fairness (C-OBS, C-RNG)

A bot may know the published rules of a quest, as a player reads them in the quest text. `public_rules()` is a read-only
accessor of static design numbers: content, like `DEFINITIONS`. It reads no world, no observation, no RNG stream, no state
hash; nothing in it is reachable from a `Command`, and it adds no state, wire or observation field (C-OBS, C-RNG hold).
Which numbers a player sees: `fort_ms`, `meditate_ms`, `bargain_cost`, `bargain_stacks` (quest and reward text).
`contact_range` is documented and felt (the accept and turn-in menu appear within it). **Not player-visible in game:
`fort_range`, `route_pace`, `route_slack_ms`.** The route deadline itself (`expires_at`) is visible, and the walker only
uses the formula to infer a route length from the timer before accepting. **Decision flagged for the coordinator** (see
section 12): the default in force exposes these three, so the walker and the dwell stay free of copies; if the owner rules
them hidden, drop those three fields, the route test and the `FORT_RANGE` entry of the oracle (each is its own test or line).

## 4. New paths

- `crates/rh-sim/src/quests/board/public_rules_tests.rs` (oracle), `crates/rh-policy/src/bots/raid/quest/public_rules_tests.rs`
  (oracle).

## 5. Exclusive write scope

- rh-sim: `crates/rh-sim/src/quests/board/tuning.rs` (`QuestPublicRules`, `public_rules`) and the re-export line in
  `crates/rh-sim/src/quests/mod.rs`. The stub commit already adds both, returning today's numbers; the implementer keeps them.
- rh-policy: `quest/walker.rs`, `quest/dwell.rs`, `quest/decide.rs`, and `quest/mod.rs` for one shared helper (e.g.
  `pub(super) fn stand_u() -> f64`, a local rule "half the contact range", over `public_rules().contact_range`).
- NOT `values.rs`, `exec/`, `Cargo.lock`, any test other than the two oracle files.

## 6. Contracts and references

C-OBS, C-RNG (above), C-TICK (nothing in the tick), C-PARITY (no sim number moves), content hash unchanged
(`scripts/regolden-content.sh --check` or the content registry test). Pattern for the source scan: `decide_fix_tests.rs:606`
(`decide_declares_none_of_the_moved_constants`); `crates/rh-sim/tests/top25_review_notes.rs`.

## 7. Behaviour to preserve

Every number is the same: 45 s, 30 s, 20, 24, 15 s, 1.5, 72, and a stand distance of exactly 32 (64 / 2). The existing
tests are behaviour to keep green and unedited: `walker_tests.rs`, `dwell_tests.rs`, `decide_tests.rs`,
`decide_fix_tests.rs` (none names the constants; the one scan there, `decide_declares_none_of_the_moved_constants`, still
holds), `decide_fix2_tests.rs`, `tests/raid_quests.rs`, `raid_dwell.rs`, `raid_decide*.rs`, the rh-sim quest tests and
`tuning_tests.rs`. Floating point: `stand_u()` = `contact_range / 2.` is exactly 32; the walker's estimate must stay
`((ms/1000 - slack_s) / (pace/1000) / 60).max(0.1)` (agrees with today to 1e-12).

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-86 --oracle packet/P-RAID-TOP25-READ-1`. For a change under `crates/rh-sim` the
  quick gate runs the whole rh-sim suite: about 25 minutes. Plan for it; iterate on `cargo test --locked -p rh-policy` and
  `cargo test --locked -p rh-sim --lib quests::board::tuning` first.
- `cargo test --locked -p rh-headless --test raid_seats`.
- `python3 scripts/readability_check.py` (no arguments) passes.
- `scripts/regolden-content.sh --check` (content hash unmoved), if quick.

## 9. Validation commands

As above; the oracle commands: `cargo test --locked -p rh-policy --lib public_rules` and
`cargo test --locked -p rh-sim --lib public_rules`.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Risk: low (no number moves); the traps are about visibility and the hash.

## 11. Known traps

- Do not make `TOP25` `pub`. `public_rules()` returns a copy; nothing mutable, no reference to the static.
- Adding a field to `QuestPublicRules` needs a line in the rh-sim oracle (exhaustive destructuring, no `..`).
- The stub already adds two seams the oracle calls: `walker::timer_spend_minutes(ms: u64) -> f64` and
  `dwell::dwell_seconds(def: &str) -> f64`. Keep the names and signatures; make them read `public_rules()`.
- `STAND_U` appears three times: do not turn it into a new constant; the scan rejects `= 32.;` in all three files.
- The content hash must not move: `public_rules()` is a read of the table, not a fold.
- `decide.rs` was just changed by P-RAID-Q3-fix2: rebase nothing, work on the base.
- Do not edit the existing tests; they are the behaviour pin.

## 12. Decisions made here, and flags for the coordinator

- One accessor returning a `Copy` struct (not `pub TOP25`), built per call from the static; a function, not a `const`, so a
  retune of `TOP25` is the only edit.
- `STAND_U` stays a local rule ("stand at half the contact range") over the read number.
- `SPELL_SLOTS` and the stat-slot map are not quest-table numbers; left.
- **Flag (owner's call):** exposing `fort_range`, `route_pace`, `route_slack_ms`, which no in-game text states. Default in
  force: exposed (design numbers, the same class as `contact_range`, inferred from a deadline the player sees).
- Client drift (`QuestTracker.cs`, `QuestJournalView.cs:335-336`): out of scope, same copy on the client side.

## Oracle files (read-only for implementers)

- `crates/rh-sim/src/quests/board/public_rules_tests.rs`: every public field equals its `TOP25` field (exhaustive
  destructuring) and holds today's numbers. Green on the stub (it pins; a retune changes the second test on purpose).
- `crates/rh-policy/src/bots/raid/quest/public_rules_tests.rs`: source scans (red on the base by assertion: no declared
  `HOLD_S`, `STAY_S`, `FORT_RANGE`, `COST_STACKS`, `GAIN_STACKS`, `TIMER_BASE_S`, no inline `/ 1.5 /`, no `STAND_U` const or
  literal `32.`, each file reads `public_rules()`) and two behaviour pins (walker timer estimate, dwell seconds, equal to
  what the rules give; green on the stub).

## NOT ESTABLISHED

- That the bots' estimates stay right after a retune (only that they follow the table).
- That the three non-visible numbers may be exposed (flag above).
- That the client's 45 and 30 agree after a retune (out of scope).
- The behaviour pins cannot detect a retune on this base (the numbers are equal); they catch a copy that diverges later.

## Coordinator's answers (2026-11-23)

1. **`fort_range`, `route_pace` and `route_slack_ms` are exposed**, as the default has it. The ruling: they are static
   content, the same for every match and every seat, and the bots hold all three by hand today, so the accessor
   changes what a bot can drift from, not what it knows. Nothing per-match, per-seat or random is exposed (C-OBS,
   C-RNG). A player meets them as the deadline in the journal and the ring of the fort; that they are not written
   out as numbers in game text is recorded for the owner in the MIGRATION checkpoint as a decision taken, with the
   clean cut this brief describes if he rules them hidden.
2. **The two seams** (`walker::timer_spend_minutes`, `dwell::dwell_seconds`) keep their names and signatures.
- **Base.** `coord/int-86` has gained the elite executor since this packet branched (`exec/elite.rs`, `ElitePrices`);
  nothing in this packet's scope is touched by it. The gate takes `--base coord/int-86`.
- **The content check** (`scripts/regolden-content.sh --check`, or the content registry test the gate runs) is an
  acceptance step: the hash must not move.
