# Authoritative quest outcome matrix

The generated-map suite completes all 25 quests for two owners at both 20 Hz and 40 Hz. For each definition the two
owners start with the same public offer, accept independently, exercise the objective, receive the award, and restore their
own completed state after a checkpoint and reconnect. The 40 Hz export supplies the first owner's public frames for replay.
There are 18 spell-granting quests, 19 individually castable awards, and seven permanent rewards. Forget-Me-Not Seed is
planted and then cast again to recall. The export contains 155 phase samples, 15 distinct ready states, 74 event frames
and eight FX frames. Every quest has a settlement event. The turret impact samples 17.93 damage, Bolas applies a
1,500 ms root, and the egg records 0 HP at death followed by 41.1% maximum HP at the post-hatch sample. [Scenario suite](../../../../crates/rh-sim/src/quests/board/attempts/scenarios.rs)

| Quest | Generated seed | Objective exercised | Result checked after settlement |
|---|---:|---|---|
| The Warden | 5 | Kill the bound elite | Warden's Cage places a six-second barrier. Fence cells block bodies and melee while remaining transparent to sight. |
| Hold the Fort | 2 | Occupy the fort for 35 seconds | Turret appears and reduces an enemy's health. The first hit gets its own replay sample. |
| Bounty Hunter | 1 | Kill a marked opponent | Hook pulls the first enemy toward the caster. |
| Smuggler's Run | 4 | Collect and return the marked parcel | Shoulder Charge closes the distance, pushes the enemy away, and stuns on contact. |
| Two Banners | 1 | Complete opposite banner branches as separate owners | Warhorn forces a rival toward the caster and raises outgoing damage by at least 20% while reducing damage taken by at least 15%. |
| Ember Walk | 1 | Collect three ember tokens | Spell Shield blocks incoming Bolas, prevents the root, clears the shield, and refunds the spell charge. |
| The Burning Crown | 1 | Carry the crown across the first accumulation interval and return it | Crown Power pays the accumulated Ember Imp stack. |
| Let Him Cook | 3 | Collect three food ingredients | Chef's Kiss grants a permanent stat row. Separate modifier scenarios check selection, caps and how the bonus enters the live stat fold. |
| Elite Slayer | 4 | Kill the bound Scorch elite | Smite deals 220 true damage in the fixture, kills the selected regional mob, credits the caster despite an opponent's higher threat, and pays bonus GV. |
| Tuber Eats | 3 | Deliver the potato | Breaker's Helm converts 20% of a qualifying weapon hit to armor-bypassing damage. |
| Barrow Run | 1 | Visit the barrows and tunnel | WallBuster removes the aimed obstruction from the permanent terrain set and the current collision map. |
| The Bargain | 2 | Surrender a spare weapon at the hag | The spare weapon is gone and 11 Ember Imp stacks are granted. A separate scenario exercises payment and receipt in lifesteal stacks. |
| A Cruel Joke | 4 | Scare three quest givers | Swap-Stick exchanges both equipped weapons and returns both originals after 10 seconds. |
| A Bone to Pick | 1 | Kill the bound skeleton archer | Bolas flies to its target and applies Root on impact. |
| Feed or Hunt | 3 | Feed three zone items to the target creature | The attempt stays active after deliveries one and two. Delivery three grants 12 Ember Imp stacks. |
| Rime Hunt | 1 | Kill all three mobs in the bound regional camp | Tornado waits for its windup, then pulls the lower-max-health enemy toward its center. |
| Watchtowers | 2 | Visit the Rime watchtowers | The Scout Falcon reveals the cast site to its owner from beyond ordinary sight. The rival cannot see it from the same remote spot. |
| Whispers in the Brush | 2 | Collect three hidden tokens | Lantern Ward begins with two charges, consumes one, and reveals the cast site only to its owner from beyond ordinary sight. |
| Even Skeletons Get Cold | 4 | Deliver the coat to the scout | Warm Coat bypasses bog speed loss at the same spot where the comparison seat slows. |
| Lost Brother | 1 | Retrieve the relic | BlindRage gives a 1.2 attack-rate multiplier. StillMind clears an active stun, blocks a new stun, and consumes its separate charge. |
| Pilgrim's Path | 2 | Visit both shrines and both light bays | Cleanse removes the caster's simultaneous root and stun. A separate cast scenario verifies interruption of a forced pull. |
| Gatecrasher | 1 | Damage two opponents during one lock and survive the reopen | Rebirth Egg plants, the owner dies, then hatches at the egg with the same weapon and about 40% maximum HP when enough rivals remain. |
| Letters | 1 | Deliver the message before the gates close | Forget-Me-Not Seed plants an anchor on the first cast, then returns the owner to it and consumes the charge on the second. |
| Stay A While | 2 | Meditate for 30 uninterrupted seconds | Stasis prevents a submitted movement command, then permits movement after its 2.5-second duration. |
| Trade Up | 7 | Complete three successive trades | The terminal trade pays 29 Penitent stacks. |

[Earned spell assertions](../../../../crates/rh-sim/src/quests/board/attempts/scenarios/spells.rs) and
[spell effect tests](../../../../crates/rh-sim/src/quests/board/rewards/effects/tests.rs) cover the exact inputs,
timing and fixture conditions used above. [Permanent reward tests](../../../../crates/rh-sim/src/quests/board/rewards/permanent/tests.rs)
cover modifier folding, alternate payments and warm-coat behavior.

## Reproduce the wire playback

From the repository root, pick an unused local capture directory. Exporting only writes to these local directories.

```sh
export RH_QUEST_NATIVE_CAPTURE="$PWD/.agent-runs/quest-visual/v2/native"
export RH_QUEST_WIRE_CAPTURE="$PWD/.agent-runs/quest-visual/v2/wire"
cargo test --locked -p rh-sim --lib capture_authoritative_quest_outcomes -- --ignored
cargo test --locked -p rh-server --test top25_playthrough project_authoritative_quest_outcomes_to_wire -- --ignored
```

The catalog orders the named phases for each quest and contains the public quest record and reward slots at each sample.
The native test captures the same filtered owner observation that the room uses. The projection test uses the room's
Welcome, Snapshot, collection, event and FX encoding paths and decodes every generated binary frame. Some objectives
settle during the same authoritative step that makes them ready. Those captures move from acceptance straight to settlement.
Each phase is a sample rather than a record of every tick in between. [Native capture](../../../../crates/rh-sim/src/quests/board/attempts/scenarios/capture.rs)
and [wire projection](../../../../crates/rh-server/tests/top25_playthrough/capture.rs)

The harness sets actor positions and selected combat or status conditions to make each case deterministic. Objectives,
turn-ins, casts, weapon inputs, reward consumption, damage and resurrection then run through the actual fixed-step
simulation. The separate encoded room playthrough uses ordinary walking, quest interaction and reconnect for Stay A While,
and exercises reusable cooldowns and full-slot choices across additional quests.
[Room playthrough](../../../../crates/rh-server/tests/top25_playthrough.rs)
