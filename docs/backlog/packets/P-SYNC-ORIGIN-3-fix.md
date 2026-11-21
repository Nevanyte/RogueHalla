# P-SYNC-ORIGIN-3-fix: card splash spares quest givers; `rework_at_25ms` seeds under quests; turret-only killers

- **Serves:** PR 80, the augment and elite rework (owner, 2026-11-20: "Please push and merge the new augments"). It
  settles what P-SYNC-ORIGIN-3 stopped and reported.
- **Tier:** `implementer-opus`, on `agent/opus/P-SYNC-ORIGIN-3` after `4fa0e59de`. Reviewer: `reviewer-opus`.
- **Oracle (already failing):** `cargo test --release -p rh-headless --test rework_at_25ms` on `4fa0e59de`.
  - Seed 7: at step 1200 a real checkpoint is refused with `invalid neutral quest giver body/runtime`. Overkill's splash
    picks a neutral Top 25 quest giver, and the hit gives it a contact and a target.
  - Seed 5: the match ends at step 1670, under the test's 2400-step floor.
- **Parallel work:** P-CI-GREEN-11 recaptures the card chain on `4fa0e59de` at the same time. Touching any file the card
  catalog pins (`tools/**`, `parity/**`) breaks that recapture, so this packet never does.

## Decisions (coordinator, 2026-11-21)

1. **Card splash spares quest givers.**
   - `resolve_splashes` (`crates/rh-sim/src/cards/after_damage.rs`) skips quest givers, as legacy
     `packages/server/src/sim/cardHooks.ts:252` does.
   - Apply the same skip to every other card-driven picker that can choose a mob the hit did not aim at. List each one
     in the hand-off.
   - Quest givers exist only with `top25_quests` on. So legacy, parity and headless worlds are unchanged: the hash tape,
     the parity fixtures and the determinism sweep must not move.
   - Do not add legacy's non-hostile and evading filters or its tie rank here. They change legacy-world behaviour, so
     they are a separate packet (the coordinator files it).
   - Add one rh-sim unit test: an Overkill holder kills a mob beside a quest giver, and the giver gets no damage, contact
     or target.
2. **Seed 5.** First run seed 5 under origin's own live config: augments and elites Legacy, `top25_quests` on.
   - If it also ends before step 2400, the short match is origin's quest trajectory. Replace seed 5 with the next seed
     after the current set that reaches the floor with the merged `live_content()`. Write that reason in a comment
     beside the seed list.
   - If it reaches the floor under origin's config but not under the merged one, stop and report: that is a rework ×
     quest interaction.
   - Never lower the floor, cut a checkpoint check or otherwise weaken the test.
3. **Turret-only killers get no helper share.**
   - Origin's `Award::skip_stat` keeps quest-turret kills from paying stat stacks.
   - The rework's elite helper share follows it. When the rule that sets `skip_stat` for a normal kill would set it for
     the credited killer of an elite, that killer gets no helper share. Every other contributor still gets theirs.
   - Add one rh-sim unit test.
4. **Quest target elites keep both numbers.** The Warden, A Bone to Pick and Elite Slayer targets are born with the
   Rework elite numbers, then take their quest multiplier on top: a quest target is an elite, and the elite rule sets
   its base. No change; it stays as merged.
5. **Hand-offs:**
   - Write P-SYNC-ORIGIN-3's AGENTS.md report, with its conflict list, wire moves and version moves, into
     `docs/backlog/packets/P-SYNC-ORIGIN-3.md` (its brief asked for it there).
   - Write this packet's report here.

## Write scope

- `crates/rh-sim/src/cards/after_damage.rs`, and the other card pickers named under decision 1.
- `crates/rh-sim/src/systems/mob_life.rs`, or wherever the helper share is paid (decision 3).
- New rh-sim tests for decisions 1 and 3.
- `crates/rh-headless/tests/rework_at_25ms.rs`: the seed list and its comment only.
- `docs/backlog/packets/P-SYNC-ORIGIN-3.md` (the hand-off section) and this file.

## Gate

Prefix every command with `export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"`. Then run, with no uncommitted changes:
- `scripts/gate.sh --quick --base 4fa0e59de`
- `cargo test -p rh-sim`
- `cargo test -p rh-sim --lib ability_hash_tape_matches_the_base`: every case digest must be unchanged.
- `cargo test --release -p rh-headless --test rework_at_25ms`: every seed must pass.

The coordinator runs the full gate on the merged tree.
