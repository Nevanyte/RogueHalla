# Unplayed quest walking and Feed or Hunt completion

The stock RaidBot has no executor for The Warden, Bounty Hunter, The Burning Crown, Elite Slayer, A Bone to Pick, Feed or Hunt, Rime Hunt, Whispers in the Brush, or Gatecrasher. Existing authoritative quest scenarios prove interactions and grants, but their `Raid::arrange` directly relocates players; several scripted fights root a target or set it to 0.1 HP. Their ticks cannot measure a natural route. This offline probe makes **no game or live policy change**.

## What was measured

`quest_unplayed_walk` admits the normal generated eight-seat map and board at 25 ms/40 Hz, with one human-input focal seat and seven tagged RaidBot fills. At the first active frame it clones the simulation once per offered quest. The cloned other seats issue no further input; native mobs, terrain, collisions, quest timers and rewards continue. The focal player submits only commands admitted by `validate` and `Sim::submit`. It walks to the publicly shown giver and accepts through `QuestObserver`. The reported walk/accept time starts at the first active frame, excludes lobby/planning, and includes the acceptance tick. None of these walks teleport or script damage. A live room with moving competitors could differ.

The tool also computes a path through each fixed objective point and back to the giver using the authoritative pathfinder and the source base movement speed of **170 units/s**. This is an exact-point geometry estimate, not actual post-accept time: interaction can occur within a contact radius, combat and slowdown can extend travel, and hidden objectives may require discovery. Objective coordinates are read from the benchmark checkpoint **only after selection for offline analysis**; they are not a public pre-accept choice input. Bounty Hunter and Gatecrasher have no fixed route to price. Whispers' three points are hidden until nearby, so its exact-point route assumes knowledge a player initially lacks.

Across calibration seeds 1–20, the nine quests appeared 69 times on 20 maps and all 69 giver approaches accepted. On untouched seeds 21–100, they appeared 277 times on 80 maps and all 277 accepted. The [approach summary](approach_summary.csv) gives each quest's count, measured walk/accept time, exact-giver path time and fixed-point route estimate. Median calibration walk/accept ranged from 11.4 seconds for Gatecrasher to 27.7 seconds for Elite Slayer. **Only Feed or Hunt was played through its objective and grant**; the other eight still need human decisions, natural fights, pursuit, carrying, hidden search, or a live coliseum lock. No payoff premium is assigned to them from path geometry.

## Feed or Hunt: actual native route

The authoritative interaction stores each gathered item independently. A player can collect all three Warren items, visit the creature once, submit three feed interactions, then return to the giver. The older scenario alternates item and creature for test coverage; that longer itinerary is **not required** and was excluded. The probe follows the efficient order with validated Move and QuestInteract inputs, checks the live quest progress and deadline, and reads the actual Ember Imp stack grant. Native mobs remain active and the player issues no attack command.

All **10/10 calibration** and **26/26 held-out** offered maps turned in before their actual accepted timer, with no death or timer failure. Every grant was exactly **12 Ember Imp stacks**, six interaction inputs, and three units of quest progress. The initial concern that the timer priced too short a route was caused by my incorrect alternating itinerary; these native completions do **not** justify a timer repair.

| Cohort | Feed offers | Median giver walk + accept | Median accepted-to-grant | Median departure-to-grant | Neutral 25–50% band outcomes |
|---|---:|---:|---:|---:|---|
| Calibration, seeds 1–20 | 10 | 20.6 s | 24.7 s | 49.2 s | 8 below, 0 within, 2 above |
| Held-out, seeds 21–100 | 26 | See [per-seed trials](feed_trials.csv) | See per-seed trials | 34.5 s | 13 below, 5 within, 8 above |

One representative normal clear is 12 same-stat stacks. At the neutral **1.5 clears/minute** opportunity rate, the 12-stack grant has value ratio `40 / departure_to_grant_seconds`. Thus a 25–50% premium requires **26.67–32.00 seconds total**, including the giver approach. This is stack-equivalent accounting, not a combat valuation of Ember Imp damage. The per-seed CSV records both this neutral ratio and a second ratio using the historical 80-map **0–5-minute outside-quest clear rate** for the focal faction: Fire 2.119, Sentenced 1.866, Plague 1.679, Frost 1.036, Holy 0.935 clears/minute. That reference is [archived separately](../field-lot14/reference.jsonl.gz); it is not a same-map counterfactual for the inert-seat probe.

### Public pre-walk choice check

Before reading held-out outcomes, the public giver path was grouped by its distance divided by 170 units/s: **cheap ≤10 s**, **middle >10–20 s**, **long >20 s**. The objective locations were not used. A second public estimate added the calibration median **24.7 s** accepted leg to that giver-path time; it did not refit on held-out maps.

| Visible giver bin | Held-out offers | Actual below +25% | Actual +25–50% | Actual above +50% | Median actual total |
|---|---:|---:|---:|---:|---:|
| Cheap ≤10 s | 6 | 1 | 1 | 4 | 21.2 s |
| Middle >10–20 s | 9 | 3 | 3 | 3 | 29.1 s |
| Long >20 s | 11 | 9 | 1 | 1 | 41.7 s |

The public giver-plus-24.7-second rule predicted at least +25% for **3/26** held-out offers; all three actually exceeded +50% (6.8, 7.7 and 21.8 seconds total). Two were predicted inside the 26.67–32.00-second target window. This is a conservative, low-coverage signal from only three selected maps; it misses ten other favorable outcomes and cannot guarantee an individual route. Choosing by the hidden post-accept item geometry would not be a valid pre-walk policy.

## Validation and limits

- Every one of the 100 generated map boards matched the independent `quest_balance --board-only` offer identities. The pathfinder's length was asserted at least as long as the straight segment for every leg.
- The final 20-map run repeated byte-for-byte. Targeted clippy with `-D warnings` and `cargo fmt --check --all` passed. The offline tool source is commit `3b54acc9f` on game-source parent `30b951d32`; the standalone release binary SHA-256 was `f91c95653c13cceb5de8201fe0b941f523309a6e179a8495b263fd17a64f26b1`.
- Both raw files carry content hash `cb811d19ebed6dfe`, map hash, benchmark seed, 25 ms step, path geometry, observed acceptance, timer and (for Feed) grant outcome. They contain no RNG state or simulation state hashes. [Calibration raw rows](walks_seed1_20.jsonl) · [Held-out raw rows](walks_seed21_100.jsonl).
- The route probe does not play against moving opponents after the first active frame, does not attack mobs, and cannot measure the other eight rewards' situational PvP or PvE value. Its neutral and faction rates are opportunity-cost references, not paired counterfactual matches. There were zero deaths in the observed Feed routes; that is an outcome of this limited setup, not a survival guarantee.

Reproduce the raw cohorts from this source with `CARGO_TARGET_DIR=/tmp/quest-unplayed-target cargo build --release -p rh-headless --bin quest_unplayed_walk`, then run `/tmp/quest-unplayed-target/release/quest_unplayed_walk 1 21` and `... 21 101`. The tool uses no laboratory feature flag. The independent board check uses `cargo run --release -p rh-headless --bin quest_balance -- 1 101 --board-only`. No live quest or bot change follows from this probe.
