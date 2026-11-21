# Top 25 quest playtest, 19 November 2026

All 25 quests completed through player inputs in authoritative Rust rooms. The winner received the named reward exactly once. A second player saw the same ten starting offers and kept the unaccepted quest after the winner finished. In a separate two-player test, both players completed each of the 25 quests and exercised their own rewards.

In the rendered protocol 20 Unity WebGL game, Chrome opened the planning details and accepted all 25 quests. It also finished **19 of 25** from the starting giver to the reward. The table identifies each full browser playthrough. [Design workbook](../../quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx) · [Quest rules](../../quests.md) · [Measurements for all 25 quests](results.csv) · [Browser observations](browser-results.json)

## Quest-by-quest results

Every row has an authoritative room completion. “Accepted” in the browser column means that Chrome reached the giver and received server confirmation of acceptance. “Completed” adds the objective and payout.

| Zone | Quest | Full WebGL playthrough | Room playthrough |
| --- | --- | --- | --- |
| Gallows | The Warden | Accepted | Defeated the Warden with a party and earned Warden’s Cage |
| Gallows | Hold the Fort | Completed (v20) | Occupied the guardhouse and earned Turret |
| Gallows | Bounty Hunter | Accepted | Killed a player with a 10 GV bounty |
| Gallows | Smuggler's Run | Completed (v20) | Picked up the parcel and carried it home |
| Gallows | Two Banners | Completed (v20) | Carried the rival banner back to the Gallows |
| Scorch | Ember Walk | Completed (v20) | Collected three ember tokens and returned |
| Scorch | The Burning Crown | Completed (v20) | Picked up the crown and banked its power |
| Scorch | Let Him Cook | Completed (v20) | Collected all three ingredients and turned them in |
| Scorch | Elite Slayer | Accepted | Defeated a Scorch elite with a party and earned Smite |
| Scorch | Tuber Eats | Completed (v20) | Delivered the rancid potato |
| Warren | Barrow Run | Completed (v20) | Visited the barrows and returned |
| Warren | The Bargain | Completed (v20) | Found two weapons and traded the spare at the hag |
| Warren | A Cruel Joke | Completed (v20) | Scared three different givers |
| Warren | A Bone to Pick | Accepted | Defeated the Barrow Archer with a party and earned Bolas |
| Warren | Feed or Hunt | Completed (v20) | Gathered feed and fed the forager three times |
| Rime | Rime Hunt | Accepted | Contributed to clearing the camp with 62 attack inputs |
| Rime | Watchtowers | Completed (v20) | Visited both towers and returned |
| Rime | Whispers in the Brush | Completed (v20) | Guided search, proximity reveal and three hidden pickups |
| Rime | Even Skeletons Get Cold | Completed (v20) | Delivered the coat |
| Rime | Lost Brother | Completed (v20) | Retrieved the relic and received both draughts |
| Sanctum | Pilgrim's Path | Completed (v20) | Completed the four-point pilgrimage |
| Sanctum | Gatecrasher | Accepted | Damaged two opponents during one lock and survived reopening |
| Sanctum | Letters | Completed (v20) | Delivered the letter |
| Sanctum | Stay A While | Completed (v20) | Completed a 30-second meditation |
| Sanctum | Trade Up | Completed (v20) | Bartered and cashed out at stage one |

## Reproduce all 25 room playthroughs

From the repository root, run the following command with Rust, Cargo and gzip installed.

```sh
bash docs/evidence/quest25-deep-playtest-2026-11-19/replay.sh
```

The replay constructs fresh game rooms and joins two to eight scripted human seats per case. It sends the recorded wire messages to the server at the original 50 ms tick intervals. It checks the shared board, acceptance, the winner’s remaining nine offers, exactly one private reward notice, the expected spell inventory and the other player’s retained offer. A separate generated-raid test verified the gameplay effects of the seven permanent rewards. [Recorded inputs](room-inputs.json.gz) · [Replay implementation](replay.rs) · [Two-player reward tests](../../../crates/rh-sim/src/quests/board/attempts/scenarios.rs)

A Luna subagent independently replayed all 2,928 recorded player inputs. Its 25 winners, witness seats and completion ticks matched the CSV. The fixtures cover seven seed values and 79 seat entries. Each row gives the winner and witness as one-based seat numbers. After each recording, the verifier also submitted a fresh acceptance sequence using the completed quest’s former handle, disconnected both players and resumed their sessions. On all 25 reconnects the winner still had nine offers, exactly one reward notice and the same inventory. The witness still had all ten offers and received no reward notice.

## Gameplay and presentation checks

- Across 256 different admitted maps, every board had ten distinct quests, ten distinct giver positions and two offers per zone. All 50 possible within-zone pairs occurred. Individual quests appeared on 90 to 123 boards.
- The three toughest combat recordings used parties who picked up actual weapons and fought the Warden, a Scorch elite and the Barrow Archer. For Bounty Hunter, one player earned a 10 GV bounty by killing three rivals before the quest owner defeated them. For Gatecrasher, two distinct opponents took damage during one lock and the player survived reopening.
- The WebGL acceptance sweep covered 21 random rooms and exchanged 26,676 incoming and 14,544 outgoing WebSocket frames. It had zero browser page exceptions. Headless Chrome logged graphics-format warnings and one music-load timeout. Neither prevented the recorded quest actions.
- Chrome played Whispers in the Brush by searching five bush patches identified on the public Frost map. Approaching the bushes revealed three tokens. Clicking each pickup advanced progress from one to three, and returning to the giver awarded Lantern Ward with two charges. The encoded room run separately used predetermined search locations.
- Chrome collected all three Ember Walk tokens, returned for Spell Shield and spent its charge. In Stay A While, it completed the 30-second meditation, received Stasis and used it. Both casts were confirmed in the protocol 20 reward slots. [Spell Shield cast](ember-walk-shield.jpg) · [Stasis cast](stasis-cast.jpg)
- Unity Editor replayed 447 player-specific quest views and checked the visible journal rows, objective markers and controls for both players. Rust decoded all 111 commands from the journal buttons, resolved the public handles and replayed the commands through the simulation. The scenarios used controlled encounter health, positions and timing, and restored both players’ completions from a saved checkpoint.
- Separate Sanctum scenarios checked duplicate waypoints, hitting the same opponent twice, stale letter delivery after a lock, meditation interrupted by an attack or a lock, and an early Trade Up cash-out. Both players could claim their own 12 Penitent stacks at the first trader.
- Two separate Chrome players joined the same live room and completed Tuber Eats in succession. After the first player delivered the potato, their board showed nine offers. The second player still saw the tenth offer unaccepted, accepted it at the giver and delivered their own potato. Both ended with nine offers. [Two-browser observations](two-browser-completion.json)
- The browser driver accepted Rime Hunt in several encounters and equipped a sword from a public drop. Those solo runs did not clear the camp. The independently replayable armed room run completed the quest using 62 attack inputs.
- In Chrome, The Bargain player collected two physical swords, accepted the quest and followed the owner’s objective marker to the hag. Trading the spare for lifesteal left the first sword equipped and cleared the quest.
- In ten additional Warren and Rime maps, both players completed each quest and 20 premature interaction attempts were rejected. An initial test stopped on Rime Hunt map 20004 because it compared a newly spawned enemy with itself. Inspection at 85.3 seconds showed three live camp enemies, IDs 216–218. The second player cleared that camp and earned Tornado. [Alternate-map results](alternate-maps.csv)

## Defects reproduced and fixed

| Player-visible failure | Verified result |
| --- | --- |
| Two Banners granted Warhorn when the rival banner was picked up. | The journal now sends the player home with the rival banner. In Chrome, the owner followed both map markers, returned home and received Warhorn. Both delivery directions are covered in Rust. [Warhorn at turn-in](two-banners-warhorn.jpg) |
| Watchtowers and Gatecrasher lost personal objective markers when untracked. Unity misclassified their single-word identifiers. | Their delivered markers appear on the minimap, tactical map and in the world. Completing the quest removes the owner’s markers. [Watchtowers tactical map](watchtowers-map.jpg) |
| Spending the last charge of a reusable spell could prevent the server publishing the owner’s updated state. | A room test earns three spells, casts Spell Shield, drops it at zero charges, picks it back up with the same cooldown deadline and receives the restored charge on time. [Room regression](../../../crates/rh-server/tests/top25_playthrough/spell_cooldowns.rs) |
| The Bargain advertised exchanges that the server or network codec rejected for higher stat indices. | Protocol 20 carries all 72 ordered exchanges between distinct stat families. In Unity Editor every one of those buttons and all nine spare-weapon trades sent the advertised choice. Rust applied each click to an identical funded player state. The owner paid 20 stacks or a spare weapon and gained 24 stacks of the selected stat. [81 journal buttons and their encoded inputs](broker-choices.csv) |

## Packet hand-off

| Field | Record |
| --- | --- |
| Packet | P-QUEST25-DEEP-PLAYTEST, Phase 2 item 13 and Phase 3 item 16. [Packet scope and oracles](../../backlog/packets/P-QUEST25-DEEP-PLAYTEST.md) |
| Branch | `codex/top25-quests`. Game code verified at `ba5b0c696`. |
| Gate | PASS (full) on protocol 20. The gate ran Rust, PostgreSQL, legacy parity, 2,186 C# tests, 1,601 Unity EditMode tests, the JS vectors, the eight-bot smoke match and the release-mode bot checks. No gate step was skipped. Separate CI and hardware checks include Linux source recaptures, Valgrind and cross-architecture determinism. Browser and WebGL evidence appears above. |
| Changes | Personal banner destination, Unity quest markers, spell cooldown and broker validators, protocol 20 fixture and decoder version selectors, and the matching simulation, room, C#, Unity and JS regressions. |
| D-rules | D1 uses the approved workbook and preserves the legacy K1 oracle. D2 balance is outside this pass. The existing D5/D10 quest exception applies. D9 keeps quest decisions on the Rust server. D6 behavior is unchanged. |
| Deviations and shared records | Existing DEV-040 covers the new board and personal completion. The coordinator updated the local migration checkpoint. [Deviation record](../../DEVIATIONS.md#entries) · [Checkpoint](../../MIGRATION.md) |
| Legacy doubts | None raised by this playtest. |
| Limits and risks | Local browser and deterministic room playtests establish the recorded paths. Solo difficulty, organic PvP frequency, remote network latency and hosted deployment remain unmeasured. The matching client and server must use protocol 20. |

The pinned Unity 6000.0.85f2 Editor ran the EditMode tests directly. The installed Unity CLI test runner stalled. The actual WebGL builds ran in Chrome against local game servers.
