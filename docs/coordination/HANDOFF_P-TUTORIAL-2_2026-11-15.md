# Hand-off: P-TUTORIAL-2 (2026-11-15)

PACKET: P-TUTORIAL-2, tutorial client logic (C# net library)
BRANCH: agent/sonnet/P-TUTORIAL-2 (f5f3e33a) · ORACLE: packet/P-TUTORIAL-2 (52d5d7ac)
BASE: feature/unity-rework 86dd4adc (not stacked; the library tests use the transport fake, so the server packet P-TUTORIAL-1 is not needed to build or test)
SERVES: not in the plan: owner approval 2026-11-15 (owner request in session). D5 flagged in the packet.

GATE: `scripts/gate.sh --quick --oracle packet/P-TUTORIAL-2` → GATE PASS (quick), skipped: Unity EditMode (no Editor here).
`dotnet test client-unity/RoguehallaNet.Tests` 2156/2156 (13 oracle tests red before). `sync-net.sh --check` clean.
`--oracle-check` cannot judge C# oracles; the red-only-in-oracle evidence is in the brief's "Oracle evidence" section.

CHANGED: `RoguehallaNet/Net/Ui/TutorialFlow.cs`, `TutorialObserver.cs` (new), `Net/AccountApiService.cs` (`SaveTutorialAsync`,
`AccountMutationKind.Tutorial`), `Net/AccountFullProfile.cs` (`AccountTutorialState`, `Tutorial`, `TutorialDue`),
`UnityProject/Assets/Scripts/Net/AccountFullJson.cs` (lenient parse, mutation encode), synced Unity copies with `.meta`.

WHAT CHANGES FOR A PLAYER: nothing on its own (logic only; presentation is P-TUTORIAL-3). Wire: none (account REST JSON only).

D-RULES: D1/D2 n/a · D5 owner-approved · D6 untouched · D9 kept (steps advance only on accepted snapshots/fx).

REVIEW (reviewer, Sonnet): LAND WITH NOTES, one note.
- Note 1: the move ring sits at `min(120, rackDistance * 0.5)` toward the rack, not a flat 120 u as the brief says.
  **Resolution (lane):** accepted as intended behaviour, no fix packet. Halving keeps the ring between the player and a
  close rack instead of beyond it; the oracle does not pin the distance. Brief wording to be read as "up to 120 u".
Minor, not notes (from the review):
- `TutorialObserver.OnFx` keeps only the last own-hit victim per build; a hit between rare builds can be overwritten.
- `AccountTutorialState` also accepts a legacy `"started"` state (documented in its XML doc).
- The "nearest rack" is the nearest ground prop holding a weapon, so a dropped weapon can also match (harmless in Training).
- Thread affinity of `Session.FxReceived` not established (`_hitVictim` has no lock).

PINNED VALUES TO RE-TAKE: none.
DEVIATIONS: none for this packet (the guided tutorial row is proposed with P-TUTORIAL-3).
NOT ESTABLISHED: Unity EditMode; behaviour in an Editor build.
