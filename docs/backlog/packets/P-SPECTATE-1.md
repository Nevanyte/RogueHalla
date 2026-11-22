# P-SPECTATE-1

## ID and outcome

After a final death a player can watch the rest of the match instead of only seeing the results card. The results card
offers Spectate (with Play again and Return to lobby); a spectate bar shows who is watched, with Next player, Results and
Leave match. Owner request (Nevin, 2026-11-22 chat): "When a player dies ... they are given an option to spectate or
return to lobby."

## Dependencies

`feature/unity-rework` at `e03ebffc`. No protocol change (protocol stays 23), so it does not collide with PR #80's
protocol 24.

## Relevant legacy paths

None. Legacy had no spectating (`packages/server/src/bots/brain/memory.ts:875` only mentions a dead bot reading
announcements). This is new behaviour of the live server's view, not of the game.

## Design

- **Who may spectate** (`rh-sim/src/spectate.rs`, `Sim::may_spectate` / `Sim::spectate_eye`): a viewer that is `Dead`
  and resolved (a pending Rebirth Egg is not resolved, so its owner keeps its own view and is revived normally). The
  watched player is the requested one when standing, else the next standing player in id order, wrapping. A revived
  viewer is `Alive` again and gets its own view back at once.
- **What a spectator sees**: the world as the watched player sees it (actors, mobs, status fields, projectiles, events
  and fx), including the watched player as an actor row. The viewer's own facts (self, plan, summary, inventory, quests,
  props it could pick up) stay the viewer's. It reads the world only: no state, hash, checkpoint, replay or tick-order
  change. C-OBS: the spectator receives a subset of the public snapshot, limited by a living player's sight.
- **How the choice arrives**: the existing `Target { actor }` command. Once a seat may spectate, rh-server takes a Target
  as "watch this player" (`ItemObserver::set_spectate`) and never submits it to the sim; the fx projector gets the same
  eye each step (`FxProjector::set_eye`).
- **Client** (`RoguehallaNet/Net/Spectator.cs`): asks for the killer first (roster name match), mirrors the server's
  rule over the delivered actors to know whom to follow (`Spectator.Pick`), stops when the local player stands again.
  Unity: the camera and the vision fog follow the watched player; gameplay input is not collected while spectating; the
  bar and the card's Spectate button live in `MatchHud.Spectate.cs`. The match ending returns to the results card.

## Exclusive write scope

`crates/rh-sim/src/{spectate.rs,spectate/tests.rs,lib.rs,observe.rs,item_view.rs,fx_projector/mod.rs}`,
`crates/rh-sim/src/vision/npc_cache_tests.rs` (signature only), `crates/rh-server/src/room/{commands.rs,step.rs}`,
`client-unity/RoguehallaNet/Net/Spectator.cs` (+ synced Unity copy), `client-unity/RoguehallaNet.Tests/SpectatorTests.cs`,
`client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs`, `View/ArenaView.cs`,
`UI/{MatchHud.cs,MatchHud.Legacy.cs,MatchHud.Settlement.cs,MatchHud.Spectate.cs}`.

## Acceptance checks

- `rh-sim` `spectate::tests`: only a final death may spectate; a fallen target moves to the next standing player and
  wraps; a spectator sees the watched player and who it sees, keeps its own self, and gets its own view back when revived.
- `rh-server` `room::tests` spectate test: a dead seat's Target sets the watched player and reaches no sim command.
- `SpectatorTests` (C#): the client's mirror of the rule.

## Not established

No Unity Editor or WebGL build was run for this packet (no Unity on the build host); the HUD layout needs a look in a
real build. Pickups (ground items) near the watched player are not shown to a spectator.
