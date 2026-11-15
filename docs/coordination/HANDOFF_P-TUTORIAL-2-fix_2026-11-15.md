# Hand-off: P-TUTORIAL-2-fix (2026-11-15)

PACKET:          P-TUTORIAL-2-fix — serves "not in the plan: owner approval 2026-11-15" (owner asked for the fix after a live playthrough)
BRANCH:          agent/sonnet/P-TUTORIAL-2-fix @ c44a9feb · ORACLE: packet/P-TUTORIAL-2-fix @ b22bcb08
BASE:            stacked on claude/serene-pasteur-m3kd4y @ f36eb0b9 (P-TUTORIAL-1..3 combined, unmerged)
GATE:            PASS (quick, --base claude/serene-pasteur-m3kd4y --oracle packet/P-TUTORIAL-2-fix) [skipped: Unity EditMode, no Editor]
                 dotnet test client-unity/RoguehallaNet.Tests 2171/2171 (3 oracle tests red before); sync-net --check clean
CHANGED:         client-unity/RoguehallaNet/Net/Ui/TutorialFlow.cs (SetRing: ring always RingDistance 120 u from spawn along the rack direction) + synced Unity copy
D-RULES:         D1/D2 n/a · D5 owner-approved · D6 none · D9 kept (presentation only)
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: Unity EditMode / Editor behaviour
RISKS:           none

Why: a headless playthrough against a live Training server (rh-server --runtime training + Postgres) showed step 1
completing at spawn: the Training rack is 96 u from spawn, the old `min(120, rackDistance/2)` put the ring centre 48 u
away = RingRadius, and the inclusive check passed. This reverses the coordinator's acceptance of review note 1 in
HANDOFF_P-TUTORIAL-2_2026-11-15.md.

Live re-verification after the fix (same harness, two fresh callsigns): step 1 now needs walking (0.4-0.5 s, player
starts 120 u from the ring centre), every step advances on server data, total ~1.7 s, profile ends
`tutorial={"state":"done","step":6,"version":1}`, `tutorialDue=false`. The ring now overlaps the first rack slot, so
step 2 can complete the moment the player presses F on arrival (it still needs the pickup).

REVIEW (reviewer, Sonnet): LAND. Minor, not notes:
- The new comment says "from the spawn"; the code measures from the first observed position (same thing in practice).
- The live-playthrough acceptance is not in the gate evidence (it is recorded here).
Coordinator observation (not a defect in this packet): a Wish that takes a target (e.g. Trial, Freeze) cast at empty
ground does nothing, so step 5's copy could tell the player to aim at the dummy. Candidate copy change for later.
