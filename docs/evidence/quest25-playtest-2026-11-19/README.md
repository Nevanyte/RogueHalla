# Top 25 local WebGL playtest

On November 19, 2026, two Chrome pages ran the Unity 6000.0.85f2 WebGL player against the local Rust source-arena candidate. The build baked server content hash `7639672bbb0933c9` and spoke protocol 19. Both players entered through **Start Run** and joined the same 160 × 160 arena.

The two clients received the same ten quest definitions and the same giver coordinates during planning. Comparing each giver’s position with the region layer delivered in Welcome showed two givers in each zone.

| Zone | Givers | Offers |
| --- | ---: | --- |
| Gallows | 2 | Bounty Hunter, Two Banners |
| Scorch | 2 | Ember Walk, Elite Slayer |
| Warren | 2 | Barrow Run, A Cruel Joke |
| Rime | 2 | Rime Hunt, Lost Brother |
| Sanctum | 2 | Gatecrasher, Trade Up |

The primary player opened the journal with **J**, selected an offer to read its preview, picked a spawn, and readied up. The same board carried into the live raid. **WASD** changed the primary player’s server position, and the live tactical map displayed all ten offers. Neither page reported a JavaScript exception or WebSocket error during the check. Chrome logged WebGL format-probe warnings.

In a second two-seat run, the primary player right-clicked the tactical map to walk to the Two Banners giver, then clicked **Accept Quest** in the journal. The Rust server confirmed the acceptance for the primary player. The other player’s copy of the same offer remained unaccepted.

The test covered the rendered local two-seat flow through raid entry, movement, and personal quest acceptance. Per-quest completions, reward use, account authentication, reconnect, and remote hosting were outside this browser check.

[Planning journal screenshot](planning-journal.png)
