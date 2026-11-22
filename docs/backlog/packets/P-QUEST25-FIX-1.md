# P-QUEST25-FIX-1

## ID and outcome

Fix five defects found reviewing the merged Top 25 quest board (PR #81), and run all 25 quests at the live 40 Hz step.

## Dependencies

P-QUEST25-INTEGRATE on `feature/unity-rework` at `ef8d5e9ce`.

## Relevant legacy paths

None. The Top 25 board is new design ([quest rules](../../quests.md), DEV-040).

## Exclusive write scope

`crates/rh-sim/src/lifecycle.rs`, `crates/rh-sim/src/quests/board/attempts.rs` and `attempts/{interactions,tests}.rs`,
`attempts/scenarios.rs` and `scenarios/{spells,full_slots}.rs`, `rewards/inventory.rs`, `rewards/effects.rs`,
`rewards/effects/checkpoint.rs`, `rewards/effects/tests.rs`, the new `rewards/effects/departure_and_rate_tests.rs`,
`attempts/signals.rs`, `special/egg.rs` and `egg_tests.rs`, `systems/{items,weapons,targeting,ledger}.rs`.

## Defects

1. A player who leaves (`remove_player`, `abandon_player`) left completed and pending quest attempts, a carried crown,
   reward inventory rows and Gatecrasher victims that checkpoint validation checked against live seats only, so restoring
   the match was refused. The leaver's open attempts, inventory row and fled givers now go, a carried crown drops where
   they stood, and completed records and dropped spells are checked against the roster.
2. Scaring your own giver with A Cruel Joke blocked turn-in at a remote contact (Letters, Tuber Eats, Even Skeletons Get
   Cold, The Bargain, the rival banner, a Trade Up trader), so the route timer could expire on a delivered quest.
3. Swap-Stick on a fill bot produced effect state the checkpoint refused.
4. Hook and Bolas flight and the Hook/Tornado pull moved a fixed distance per step, so at 40 Hz they were twice as fast.
   They now scale by `tick_ms / 50` (identical at 20 Hz; no content number changes).

## Acceptance checks

Seven regression tests in `attempts/tests.rs` and `effects/departure_and_rate_tests.rs`. The all-25 catalog sweep also
runs at 25 ms (`all_twenty_five_quests_complete_at_the_live_40_hz_step`), with its waits read from the match's step.
`scripts/gate.sh --quick` passes.

## Owner changes (Nevin, 2026-11-21 chat)

5. Rebirth Egg: "should bring you back with weapons". A death the egg will hatch keeps both weapons; an egg that never
   hatches drops (or clears) them then (`items::drop_kept_weapons`).
6. Bounty Hunter: "should count bots as humans". While no standing fighter has a real bounty, the standing fighter with
   the most player kills who is not hunting is marked, human or bot.
7. The Burning Crown: "should not grow unless it is claimed". Once turned in, its power and burn hold until the claim.
8. Warhorn: "all enemies within the radius attack you ... you should get a large buff per player taunted". Taunted
   players aim and attack the caster with their own weapon (`targeting::attack_taunter`), camp mobs wake onto the
   caster, and the caster gets +20% damage and 15% less damage taken per enemy (at most 60% less) for 2.5 s. The
   journal wording is unchanged (it is part of the content hash).

Still open: a Warhorn cast with no enemy in range spends the charge.
