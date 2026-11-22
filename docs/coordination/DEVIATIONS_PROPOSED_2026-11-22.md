# Proposed DEVIATIONS rows from 2026-11-22 onward (unsigned)

Rows proposed by the packets of the balance programme (plan of record `docs/backlog/BALANCE_LAB_PATCH.md`, owner
approval 2026-11-22) and by the lanes that run beside it. They continue the numbering of
`docs/coordination/REWORK_DEVIATIONS_PROPOSED.md` (DEV-042 to DEV-060), in the `DEVIATIONS.md` format. Every Owner field
is blank: the owner signs or strikes each row, and the coordinator then moves it into `docs/DEVIATIONS.md`. Nothing
here is signed by its author. Integration numbers are those in `docs/MIGRATION.md`.

## Client input

### DEV-061 (client): keyboard steering pacing
- **Legacy:** a held direction re-sends its Move order every 100 ms (`packages/client/src/net/keyboardMovement.ts:21`
  `STEER_REPEAT_MS`); an aim-only frame goes at most every 25 ms (legacy `uplinkPace.ts` had a similar stand-back).
- **Port:** a held direction re-sends when the last goal is within 24 u (goal consumption), on a heading change over
  20° with a clear goal, or on a 500 ms safety repeat; an aim-only frame stands back from 25 ms to 50 ms once 36 frames
  left in the last second (`SteerPacer` in `client-unity/RoguehallaNet/Net/OrderBudget.cs`, `InputSender.cs`,
  `MatchClient.cs`). Client pacing only: the same goals and the same server rules; the server's order budget is unchanged.
- **Reason:** the server's order budget (15 a second, burst 30) dropped orders while kiting and attacking, attacks
  included. Found by the read-only audit the owner asked for on 2026-11-22.
- **Re-validation:** `client-unity/RoguehallaNet.Tests/SteerPacingTests.cs`, a scripted 30 s kite with four clicks a
  second through a copy of the server's budget: orders 18.0 to 13.0 a second, dropped orders 62 to 0, peak frames a
  second 39 / 49 / 55 to 37 / 40 / 45 at 60 / 144 / 240 fps. Feel in a real client is not established: the reviewer of
  integration 82 found a held direction cannot stop for good, and that the likeliest fault is a brief pause at the goal
  while speed-boosted (a 94 ms margin at 255 u/s).
- **Introduced by:** P-NET-STEER-1 (client-input lane; integration 82). **Reversibility:** reversible (restore the
  100 ms rule in `SteerPacer.Decide`). **Owner:** unsigned.
