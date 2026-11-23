# Hand-off: UI rework lane, wave 1 (2026-11-23)

LANE: the UI rework lane (session "Front-end gameplay UI rework"; worktree claims under session `lane-ui-rework`).
SERVES: not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the lane's chat: "Please do an in-depth front end
gameplay rework, please make the front end as fluid as possible, make sure all in-game notifications are thoughtfully
placed. Currently there is a unity bug where the stat above a camp shimmers and glitches as a camp is moving around
it. Quest UI could be perfected, ability and spell hub needs optimizing. When a stat or quest is accepted there is a
big ugly notification in middle of map. When tab is clicked map layout is weird. Please do a UI optimization sweep."

HANDED OVER: seven packets, all presentation only (`client-unity/` and `docs/backlog/packets/`; no wire, no crate, no
protected path). Each is green on its own quick gate and reviewed LAND by the lane's reviewer (Sonnet). The two that
were LAND WITH NOTES each have their `-fix` packet here, reviewed LAND.

| Packet | Oracle | Agent branch | Base | Review |
|---|---|---|---|---|
| P-UI-HUD-SEAMS-1 | `packet/…` @ daffd3104 | `agent/sonnet/…` @ 7d2c27ead | integration | LAND |
| P-UI-HUD-REGIONS-1 | @ 0c93ab811 | @ c63a80a56 | integration | LAND |
| P-UI-MOTION-1 | @ 663d574c8 | @ 6a0d0e148 | integration | LAND WITH NOTES (1) |
| P-UI-MOTION-1-fix | @ 140ab11c4 | @ 7519c74a9 | stacked on P-UI-MOTION-1 | LAND |
| P-UI-CAMP-LABEL-STABLE-1 | @ 2d24511cd | @ f32177fe3 | integration | LAND WITH NOTES (1) |
| P-UI-CAMP-LABEL-STABLE-1-fix | @ 39d5ef040 | @ 8c8042e75 | stacked on P-UI-CAMP-LABEL-STABLE-1 | LAND |
| P-UI-TABMAP-FLOW-1 | @ 6bc2fbba6 | @ 7c8fbd56e | integration | LAND |

HOW TO MERGE: one branch brings all seven. `lane/ui-rework/base-2` is `feature/unity-rework` @ 7f34a9b20 plus seven
`--no-ff` merges of the agent branches (SEAMS, REGIONS, MOTION, MOTION-fix, CAMP-LABEL, TABMAP-FLOW, CAMP-LABEL-fix,
in that order; none conflicted) plus this hand-off commit. The integration branch has since moved to b93f53561
(records only: `docs/MIGRATION.md`, `docs/backlog/README.md`); `git merge-tree` of the two is clean. Merging the agent
branches one by one works too: each `-fix` branch contains its base packet.

LANE GATE ON THE MERGED TREE: `scripts/gate.sh --quick` on `lane/ui-rework/base-2` @ c1f15be86 (the seven merges,
before this hand-off commit): GATE PASS (quick). Unity EditMode 2049/2049, the node tool tests, the dotnet library
tests and `sync-net.sh --check` ran. Log: `.agent-runs/gate/20261123T004929-lane-ui-rework-base-2-quick.log` in the
worktree `../RH-ui-join`. The lane ran no full gate; the coordinator's merge gate is the one that counts.

WHAT CHANGES FOR A PLAYER (the whole wave):
- The reward badge, the respawn text and the ward text above a camp glide (fast up, a short hold, slow down) when a
  fighter walks under them, instead of hopping. A badge fades at the vision edge instead of blinking, and no badge is
  drawn under the Tab map. This is the "stat above a camp shimmers" report.
- Holding the right mouse button on the Tab map steers, as on the minimap. The Tab map is painted on the frame it
  opens, the camera is no longer re-pinned while it is open, and the own marker moves with the character.
- Nothing else is visible yet. The file split (`MatchHud.TopCentre.cs`, `.ActionBar.cs`, `.Pulses.cs`, `.Notices.cs`,
  `.Visibility.cs`), the layout table (`HudRegions`) and the motion helpers (`UiEase`, `HudClock`, `HudFade`,
  `HudPulses`) are groundwork for the next wave.

OWNER APPROVALS OPEN:
- Three amended expectations of existing tests, all in P-UI-TABMAP-FLOW-1 (commits 6f2506921 and a41319019; details in
  that packet's section below). They read the minimap's marks while the Tab map is open, or both surfaces at once; a
  surface that is not on screen is no longer painted, so each test now reads a surface while it is on screen. The
  asserted values are unchanged. The owner has been told in the lane's chat; he has not answered yet.

PINNED VALUES TO RE-TAKE: none.
DEVIATIONS: none proposed in this wave.
SHARED RECORDS: proposed for the coordinator: backlog rows for the seven packets; a MIGRATION checkpoint entry for the
merge. The lane wrote neither.

A FINDING IN EXISTING CODE (not this lane's change):
`MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce` depends on
test order. On `feature/unity-rework` @ 7f34a9b20, with no lane commit, the class run alone
(`-testFilter Roguehalla.Client.Tests.MinimapCaptureEditModeTests`) gives 8 of 9: "release flushes just the final
changed point, expected 1, was 0". In the whole suite the same test passes (2000/2000 on that base). The lane did
not look for the cause.

WHAT COMES NEXT FROM THIS LANE: wave 2 is running on top of `lane/ui-rework/base-2` (P-UI-TOAST-LANES-1,
P-UI-ABILITY-ROW-1, P-UI-TABMAP-SHEET-1, P-UI-QUEST-TRACKER-1). It will come as a second hand-off on a branch that
contains this one, so merging this wave first, or both together later, both work.

NOT ESTABLISHED (the whole wave): nothing was looked at in a running build or a browser; every proof is an EditMode,
dotnet or node test. No full gate. The reviewers relied on the gate logs and did not re-run suites.

The seven packets follow, one section each.

## P-UI-HUD-SEAMS-1

PACKET: P-UI-HUD-SEAMS-1: MatchHud's top-centre, action-bar, pulse, notice and visibility code each get their own file
  (serves owner direction 2026-11-22, UI rework lane chat; not in ORDER_OF_OPERATIONS; presentation only, no wire, no
    crates)
BRANCH: agent/sonnet/P-UI-HUD-SEAMS-1 @ 7d2c27ead
ORACLE: packet/P-UI-HUD-SEAMS-1 @ daffd3104
  - d746a5386 brief
  - daffd3104 oracle
  (no stub commit: the oracle reads source text only)
BASE: feature/unity-rework (integration branch was at c0ef232a1 when the packet branched)
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-HUD-SEAMS-1` on 7d2c27ead: PASS, Unity EditMode 2000/2000,
  `hud-seams.test.cjs` 18/18. One implementer pass, green first time. The gate's node tool tests ran 203 of 204 with
  1 skipped.
ORACLE TESTS: `client-unity/UnityProject/Tools/hud-seams.test.cjs`, 18 tests, all red before the implementation. The
  existence tests fail on "MatchHud.<X>.cs must exist under Assets/Scripts/UI" (five new files: existence, members
  declared, members gone from the old files). The PlayerFlows-calls, no-duplicate-method and old-files-shrank tests fail
  on the same assertion first; behind it they check the `CreateNoticeFeed` and `TickNotices` calls, no method in two
  files, and the limits (`MatchHud.Legacy.cs` under 420 lines, `MatchHud.cs` under 600). The oracle author did not run
  the Unity classes (no C# changed on the oracle branch).
CHANGED:
  - `MatchHud.cs` (575 lines), `MatchHud.Legacy.cs` (364), `MatchHud.Features.cs`: code moved out to the new files.
  - `MatchHud.PlayerFlows.cs`: three edits: the `_orderedNotices` field removed, two init lines become
    `CreateNoticeFeed();`, the `SetRecentVisible` line becomes `TickNotices();`.
  - New `MatchHud.TopCentre.cs`, `MatchHud.ActionBar.cs`, `MatchHud.Pulses.cs`, `MatchHud.Notices.cs`,
    `MatchHud.Visibility.cs`, each with its `.meta`: the moved code. The only new members are `CreateNoticeFeed` and
    `TickNotices`.
  (all under `client-unity/UnityProject/Assets/Scripts/UI/`; 14 files, 534 insertions, 411 deletions)
WHAT CHANGES FOR A PLAYER: nothing. The packet moves code between files of the same partial class and changes no
  behaviour. It enables later HUD packets (toast lanes, the ability row, the Tab-map sheet) to each edit one concern in
  its own file, so two of them can run at once.
AMENDED EXISTING TESTS: none (no `oracle-amend:` commit on the packet branch).
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6/D9 none. DEVIATIONS: none.
REVIEW (reviewer, Sonnet): LAND, no notes. The reviewer compared the sorted line multiset of the four old files at the
  base with the nine files at the head: the only extra lines are scaffolding, the two new methods and their two call
    lines.
  Minor, not notes:
  - `MatchHud.Notices.cs:1-8` (and the other new partials): the full using list is carried, which can give unused-using
    warnings; the packet allows it.
  - The gate log's file name has the branch name but no head sha, so the reviewer could not tie it to 7d2c27ead by name.
  - Implementer's risk note, for the reviewer's attention: the `ApplyWishRow` doc comment and its `_wishRow*` cache
    fields
    stayed in `MatchHud.Legacy.cs`, because the packet's table lists only the method.
NOT ESTABLISHED: nothing was looked at in a running build. Unity play behaviour was not run by hand; the moved bodies
  are
  proven by compile plus the existing EditMode suite. The reviewer did not re-run `hud-seams.test.cjs` or any Unity
    class
  (relied on the gate log), did not read each moved body line by line, did not check field-initialiser behaviour at
    runtime,
  did not check the `.meta` GUID content, and did not tie the log to the head sha. The skipped node test in the gate is
  unnamed and unexamined. Quick gate only; the full gate has not run on this branch.

## P-UI-HUD-REGIONS-1

PACKET: P-UI-HUD-REGIONS-1: one table says where each new HUD region sits, and a test proves they never collide
  (serves owner direction 2026-11-22, UI rework lane chat, on toast and notification placement; not in
    ORDER_OF_OPERATIONS;
  presentation only, no wire, no crates)
BRANCH: agent/sonnet/P-UI-HUD-REGIONS-1 @ c63a80a56
ORACLE: packet/P-UI-HUD-REGIONS-1 @ 0c93ab811
  - 9d05feaec brief
  - ce3c44563 brief
  - ac4b8416f stub
  - aa212bfbc brief
  - 6f472d1f4 stub
  - 9a1bf8b48 oracle
  - 0c93ab811 brief
BASE: feature/unity-rework (integration branch was at c0ef232a1 when the packet branched)
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-HUD-REGIONS-1` on c63a80a56: PASS, Unity EditMode 2004/2004,
  dotnet `HudRegionsTests` 26/26 (gate log: 2387 dotnet tests passed, "Unity copy is up to date"). One implementer pass,
  green first time.
ORACLE TESTS:
  - `client-unity/RoguehallaNet.Tests/HudRegionsTests.cs`: 26 cases, all red on assertions against the stub (stub
    returns
    default regions and zero), e.g. row.Anchor expected BottomCenter, `LaneWidth(1422)` expected 460, `StageRect` Right
    expected 110. The author checked a throw-away implementation turned all 26 green, then discarded it.
  - `client-unity/UnityProject/Assets/Tests/Editor/HudRegionsEditModeTests.cs`: 4 cases red:
    `BoxFromARegionMeasuresTheSameAsOn`
    at 1422x800 and 1280x960 (region has no size), `PlaceFromARegionMovesAnExistingRectTheSameWay` at both stages
    (left expected 0 as measured, was 661 and 590). `HudLegacyLayoutEditModeTests` and `HudTextScaleEditModeTests`
    stayed green on the stub (63 tests, 59 passed, the 4 new ones failed). `sync-net --check` clean.
CHANGED:
  - `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs`: the region table filled in (constants and arithmetic).
  - `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/HudRegions.cs`: the synced copy.
  - `client-unity/UnityProject/Assets/Scripts/UI/HudUi.cs`: two lines, the `Box(region)` and `Place(region)` overloads.
WHAT CHANGES FOR A PLAYER: nothing. No HUD element uses the new constants yet and no existing literal was replaced. It
  enables the later toast, ability-row, prompt-lane, quest-tracker and offer-card packets to take their place from one
  table that a test proves has no collisions.
AMENDED EXISTING TESTS: none (the packet records that no existing test pins `HudUi` or mentions `HudRegions`).
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6/D9 none. DEVIATIONS: none (the packet says the toast placement is recorded
  by the packet that moves the toasts, P-UI-TOAST-LANES-1).
REVIEW (reviewer, Sonnet): LAND, no notes. Minor, not notes:
  - `HudRegions.cs` `AbilityElement`: the swap chip height `40f` is a bare literal; a named const (`SwapHeight`) would
    match
    the others.
  - `HudRegions.cs` `OfferCard`: if `championTop` is below the stage or the tracker is tall, the top is not clamped to
    the
    stage; the packet pins only the minimum height.
  - `HudRegions.cs` `EventPill` and `FeedbackPill` clamp the index silently, as the packet requires (clamped, not
    thrown).
NOT ESTABLISHED: nothing was looked at in a running build. Allocation-freedom is by construction (structs, constants,
  arithmetic) and not tested. The new constants are used by no real HUD element, so nothing proves the real HUD matches
  them. The reviewer did not re-run any test (relied on the gate log, which names this head), did not read the oracle
    files
  line by line, did not read the existing 6-argument `HudUi.Place` body or confirm the enum order against `HudAnchor`
  (the EditMode measure tests cover both), and did not inspect the synced copy byte for byte. The full gate has not run.
  Oracle author's choices: the anchor-name check sits inside the two EditMode measuring cases; `TrackerDock` Y is pinned
  to `plateChainBottom` through `plateChainBottom+8` (the spec left it open); the swap height is pinned only as
  0 < h <= 76. Implementer risk note: the swap chip height is 40 and `TrackerDock` sits exactly at `plateChainBottom`,
  with no gap.

## P-UI-MOTION-1

PACKET: P-UI-MOTION-1: one motion helper and one HUD clock, as new files
  (serves owner direction 2026-11-22, UI rework lane chat, "make the front end as fluid as possible"; not in
  ORDER_OF_OPERATIONS; presentation only, no wire, no crates)
BRANCH: agent/sonnet/P-UI-MOTION-1 @ 6a0d0e148
ORACLE: packet/P-UI-MOTION-1 @ 663d574c8
  - 7bc510be2 brief
  - f2968f312 stub
  - 3a40fed34 stub
  - b62ea2aa2 oracle
  - 64d452ba1 brief
  - 663d574c8 oracle
BASE: feature/unity-rework (integration branch was at c0ef232a1 when the packet branched)
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-MOTION-1` on 6a0d0e148: PASS, Unity EditMode 2013/2013, dotnet
  `UiEaseTests` and `HudClockTests` 21/21, `UiMotionEditModeTests` 13/13 (gate log: 2382 dotnet tests). One implementer
  pass, green first time.
ORACLE TESTS:
  - `RoguehallaNet.Tests/UiEaseTests.cs` (13) and `HudClockTests.cs` (8): 21 of 21 red on value assertions (e.g.
    `Clamp01(.25)` gave 0, `HudClock.Ready` stayed false, `Now` gave 0).
  - `UnityProject/Assets/Tests/Editor/UiMotionEditModeTests.cs`: 13 of 13 red on assertions (a hidden panel is not
    deactivated, no CanvasGroup is added, a pulse is not counted, the scale never exceeds 1 mid pulse, and so on).
  - Commit 663d574c8 adds that end-of-pulse ticks clear t = 1 in float32.
  No existing class pins this code; none was run green. Oracle author's changes to the spec: one staged test reordered
  (`HudClockTests.BetweenSnapshotsNowRisesWithLocalTime` contradicted the "never lower" rule); preconditions added to
    two
  spec tests that would have passed on the stub; three tests added for packet rules that had none.
CHANGED:
  - `client-unity/RoguehallaNet/Net/Ui/UiEase.cs`: easing and step formulas filled in (the stub's bodies).
  - `client-unity/RoguehallaNet/Net/Ui/HudClock.cs`: HUD clock filled in (`Now` clamped, never lower than its last
    answer).
  - Synced copies of both under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/`.
  - `client-unity/UnityProject/Assets/Scripts/UI/UiMotion.cs`: `UiMotion`, `HudFade` and `HudPulses` filled in.
  (the scripts and their `.meta` files already existed from the stub commits)
WHAT CHANGES FOR A PLAYER: nothing. No HUD code calls the helpers yet. It enables the following packets to fade panels,
  pulse tiles about their centre and animate countdowns on one clock, all instant under reduced motion.
AMENDED EXISTING TESTS: none (no `oracle-amend:` commit on the packet branch).
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6 none, D9 untouched (no outcome predicted, nothing sent). DEVIATIONS: none.
REVIEW (reviewer, Sonnet): LAND WITH NOTES, one note, fixed by P-UI-MOTION-1-fix.
  - Note 1 (`UiMotion.cs`, `HudFade` constructor, about lines 24-31): the constructor adds a CanvasGroup but does not
    apply
    the `interactive` flag, so a non-interactive fade built on an active root keeps taking the pointer until a `Set` or
    `Snap` changes state; no test covered it.
  Minor, not notes:
  - `HudFade.Tick` re-reads `_root.gameObject.activeSelf` several times per frame; cheap and allocation-free
    (`UiMotion.cs`).
  - If the owner calls `SetActive` on the root directly, `HudFade`'s `_shown` and `_alpha` go stale; a comment would say
    the owner must go through `Set` or `Snap` (`UiMotion.cs`, constructor reads `activeSelf` once).
  - `HudClock.Now` mutates `_last` on every call, so it is not a pure query; a one-line summary comment would help.
  - `UiEase.InOutSine` and `Approach` use double-precision Math calls; not a determinism concern outside rh-sim.
  - The packet says new files, but the stubs already existed, so the diff is modifications of three stubs and copies.
NOT ESTABLISHED: nothing was looked at in a running build. How the motion looks and feels is unverified, and no consumer
  exists yet. The approach curve is exponential (about 3 percent of the gap left after the stated seconds) and is
    pinned by
  behaviour only. Pulses are tested only on point-anchored rects. The reviewer did not re-run any dotnet or Unity
    test, did
  not read the oracle test bodies in full, did not check the `.meta` GUIDs, and did not run `sync-net --check` itself
  (took the gate line). The full gate has not run on this branch. Implementer risks: `HudPulses.Play` is ignored under
  reduced motion (Count stays 0); `HudPulses` assumes a point-anchored rect and a parent scale of 1, and restores a rect
  moved during a pulse to its origin at the end.

## P-UI-MOTION-1-fix

PACKET: P-UI-MOTION-1-fix: a non-interactive fade never takes the pointer, from the moment it is built
  (serves the one review note of P-UI-MOTION-1; same owner direction 2026-11-22; presentation only, no wire, no crates)
BRANCH: agent/sonnet/P-UI-MOTION-1-fix @ 7519c74a9
ORACLE: packet/P-UI-MOTION-1-fix @ 140ab11c4
  - 0dfcaecc9 brief
  - 140ab11c4 oracle
BASE: stacked on agent/sonnet/P-UI-MOTION-1 (unmerged); gate with `--base agent/sonnet/P-UI-MOTION-1`. Merge
  P-UI-MOTION-1 first.
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-MOTION-1-fix --base agent/sonnet/P-UI-MOTION-1` on 7519c74a9: PASS,
  Unity EditMode 2019/2019; the packet's own classes (`HudFadeConstructionEditModeTests`, `UiMotionEditModeTests`)
    19/19.
  One implementer pass, green first time.
ORACLE TESTS: `client-unity/UnityProject/Assets/Tests/Editor/HudFadeConstructionEditModeTests.cs`: six new tests (19
  with
  the 13 of `UiMotionEditModeTests`). On the base 5 were red on assertions, all "blocksRaycasts ... expected False,
    was True":
  `ANonInteractiveFadeOnAnActiveRootTakesNoPointerFromConstruction`,
  `ANonInteractiveFadeOnARootWithItsOwnGroupTakesNoPointerFromConstruction`,
  `SettingTheWantedStateAgainLeavesItNonBlocking`, and `AFadeBuiltOnAnInactiveRootBlocksNothing` (both parameter cases).
  `AnInteractiveFadeOnAnActiveRootKeepsThePointer` is green on the base as a guard against over-reach.
CHANGED: `client-unity/UnityProject/Assets/Scripts/UI/UiMotion.cs` (3 insertions, 1 deletion): at the end of the
  `HudFade`
  constructor `blocksRaycasts` and `interactable` are set to `_shown && _interactive`; the class summary gains one
  sentence that the owner shows and hides the root through `Set` and `Snap` only.
WHAT CHANGES FOR A PLAYER: nothing. `HudFade` has no consumer yet; the fix makes a non-interactive fade, once used,
  never
  take mouse clicks from what is behind it.
AMENDED EXISTING TESTS: none (no `oracle-amend:` commit; the 13 `UiMotionEditModeTests` stay unchanged and green).
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6/D9 none. DEVIATIONS: none.
REVIEW (reviewer, Sonnet): LAND, no notes. Minor, not notes:
  - `UiMotion.cs:15`: the added sentence makes the class summary one very long line; it could be split (wording only).
NOT ESTABLISHED: nothing was looked at in a running build. The check was EditMode only; it does not show how the quest
  tracker and toast fades of the next wave behave in a live scene. The reviewer did not re-run the Unity classes
    (relied on
  the gate log, 2019/2019), did not read the oracle file's contents beyond the packet's description, and the full gate
    has
  not run on the merged tree.

## P-UI-CAMP-LABEL-STABLE-1

PACKET: P-UI-CAMP-LABEL-STABLE-1: the stat above a camp glides instead of hopping
  (serves owner direction 2026-11-22, UI rework lane chat, the camp stat that "shimmers and glitches" as a champion
    moves
  around it; not in ORDER_OF_OPERATIONS; presentation only, no wire, no crates)
BRANCH: agent/sonnet/P-UI-CAMP-LABEL-STABLE-1 @ f32177fe3
ORACLE: packet/P-UI-CAMP-LABEL-STABLE-1 @ 2d24511cd
  - cf3c85162 brief
  - 16f24d306 stub
  - ce1026612 stub
  - 2d24511cd oracle
BASE: feature/unity-rework (integration branch was at c0ef232a1 when the packet branched)
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-CAMP-LABEL-STABLE-1` on f32177fe3: PASS, Unity EditMode 2013/2013,
  dotnet `LabelLiftTests` 11/11, `CampLabelStableEditModeTests` plus the four pinned classes 82/82 (gate log: 2372
    dotnet
  tests). One implementer pass (the implementer's own commits are `ae8bae606` LabelLift state machine and `f32177fe3`).
ORACLE TESTS:
  - `RoguehallaNet.Tests/LabelLiftTests.cs`: 11 cases, all red on value assertions (the stub returns 0).
  - `UnityProject/Assets/Tests/Editor/CampLabelStableEditModeTests.cs`: 13 cases, all red on assertions, e.g.
    `ABadgeGlidesWhenAFighterWalksUnderIt` (plaque hopped 0.46 at frame 38, limit 0.134),
    `AnObstacleThatFlickersLeavesTheBadgeStill`, `TheBadgeComesDownOnlyAfterTheHold`,
    `ABadgeThatLeavesTheRowsLingersThenFades`, `ABadgeThatEntersLaterFadesIn`,
    `BadgesAreNotDrawnWhileTheTabMapIsOpen` (BadgesUsed 1, expected 0),
    `TheWardTextStaysAboveThePlaqueOfItsCamp`. Two are partly green on the stub and carry a red new-behaviour assertion:
    `ABadgeDrawnForTheFirstTimeOverAHeadIsAlreadyClear` and `ASteadyFrameAllocatesNothing`.
  - Existing classes on the stub, 83 of 83 green: NamePackGroup2, NameLegible, CampLabelsHiddenFix4, StormInkCombatHud
    and
    TacticalMap EditMode tests.
CHANGED:
  - `client-unity/RoguehallaNet/Net/Ui/LabelLift.cs`: the lift-with-memory state machine (rise, hold, fall, reduced
    motion) filled in.
  - `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/LabelLift.cs`: the synced copy.
  - `client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs` (459 lines): per-camp state, badge
    linger (0.2 s) and fade (0.15 s), badges not drawn while the Tab map is open, the plaque sprite assigned before its
    sliced size.
WHAT CHANGES FOR A PLAYER: the reward badge, the "RESPAWN" text and the ward text above a camp rise quickly and come
  back
  down slowly, after a short hold, when a fighter walks under them, instead of hopping. A badge fades in and out at the
  vision edge instead of blinking, and no badges are drawn while the Tab map is open.
AMENDED EXISTING TESTS: none (no `oracle-amend:` commit; the packet states none was needed).
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6 none, D9 untouched. DEVIATIONS: none.
REVIEW (reviewer, Sonnet): LAND WITH NOTES, one note, fixed by P-UI-CAMP-LABEL-STABLE-1-fix.
  - Note 1 (`CampRespawnLabels.cs`, `DrawBeacons`, the `PlaqueTop` line, about 421-422): the ward text takes
    `Max(TextLift(...), top + WardGap - baseYMin)`; the plaque-driven part sits outside the `LabelLift`, so on a
      warded camp
    with a reward the ward text hops by about the plaque height (0.7 to 0.8 world units) in one frame when the badge row
    appears or disappears at the vision edge.
  Minor, not notes:
  - `CampRespawnLabels.cs` `DrawBadges`: a row with zero icons is skipped without touching its state, so a lingering
    state
    for that Index keeps drawing its old row until the linger ends; rows with no icons do not occur in practice.
  - `CampRespawnLabels.cs`: `WantedClear` still measures `plate.bounds` after setting the label to home; it depends on
    `Renderer.bounds` refreshing at once after a transform set, which is not proved.
  - `CampRespawnLabels.cs` `Update`: `_rows` is now filled before `FetchNames`; harmless, `WorldLabels` does not read
    names.
  - The `_lifts` entry of a lingering or hidden camp is forgotten only after `ForgetSeconds` of not being drawn; this
    matches the spec.
NOT ESTABLISHED: nothing was looked at in a running build. Only EditMode tests at 1/60 s frames show the glide, linger
  and
  fade. A ward text lifted over a plaque is not re-checked against fighter names above the plaque (the spec takes the
    larger
  of the two lifts). The reviewer ran no tests (relied on the gate log for f32177fe3), did not read the two oracle test
  files line by line, did not check that every named legacy test still pins the same expectation beyond the gate pass,
    did
  not confirm `Renderer.bounds` is fresh right after a transform change, did not measure allocations, and did not check
  `CombatHudPlacement.Lift`'s return type. Quick gate only. Implementer risks: per-camp state is keyed by the row Index
  (rows sharing an Index in one frame match states in order); badge draw order follows state order, not row order.

## P-UI-CAMP-LABEL-STABLE-1-fix

PACKET: P-UI-CAMP-LABEL-STABLE-1-fix: the ward text glides too when its camp's badge comes and goes
  (serves the one review note of P-UI-CAMP-LABEL-STABLE-1; same owner direction 2026-11-22; presentation only, no wire,
  no crates). The packet document is on the agent branch, not in the integration tree yet.
BRANCH: agent/sonnet/P-UI-CAMP-LABEL-STABLE-1-fix @ 8c8042e75
ORACLE: packet/P-UI-CAMP-LABEL-STABLE-1-fix @ 39d5ef040
  - d41c845f9 brief
  - 39d5ef040 oracle
BASE: stacked on agent/sonnet/P-UI-CAMP-LABEL-STABLE-1 (unmerged); gate with `--base
  agent/sonnet/P-UI-CAMP-LABEL-STABLE-1`.
  Merge P-UI-CAMP-LABEL-STABLE-1 first.
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-CAMP-LABEL-STABLE-1-fix --base
  agent/sonnet/P-UI-CAMP-LABEL-STABLE-1`
  on 8c8042e75: PASS, Unity EditMode 2018/2018; the library dotnet suite ran 27 tests OK. The implementer ran
  CampWardTextGlide, CampLabelStable, NamePackGroup2, NameLegible, CampLabelsHiddenFix4 and StormInkCombatHud: 87/87
  green. One implementer pass, green first time.
ORACLE TESTS: `client-unity/UnityProject/Assets/Tests/Editor/CampWardTextGlideEditModeTests.cs`, 5 tests, 3 red on the
  base: `TheWardTextGlidesUpWhenItsCampsBadgeAppears` (ward text hopped 0.7525 at frame 0, limit 0.1343),
  `TheWardTextHoldsWhileTheBadgeFadesAndThenGlidesDown` (dropped 0.7525 in one frame at frame 21, limit 0.051),
  `ReducedMotionJumpsButStillHolds` (dropped 1 frame after the plaque left, expected at least 24). Two are green on the
  base by design, as guards: `ABadgeThatFlickersLeavesTheWardTextStill` and `AFreshWardTextOverAPlaqueIsAlreadyClear`.
  The existing classes CampLabelStable, NamePackGroup2, NameLegible, CampLabelsHiddenFix4 and StormInkCombatHud: 82/82
  green on the base. The oracle author added no stub commit and recorded the red list in the oracle commit.
CHANGED: `client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs` (4 insertions, 3 deletions, inside
  `DrawBeacons`): the plaque gap is folded into the wanted value before the single `TextLift` call
  (`wanted = max(clear, plaque top + WardGap - home baseYMin)`), so the ward text rises and falls through the same
  `LabelLift`.
WHAT CHANGES FOR A PLAYER: on a warded camp that also has a reward badge, the ward text glides up and down when the
  badge
  appears or disappears at the vision edge, instead of hopping by about the badge's height.
AMENDED EXISTING TESTS: none (no `oracle-amend:` commit on the packet branch).
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6/D9 none. DEVIATIONS: none.
REVIEW (reviewer, Sonnet): LAND, no notes. Minor, not notes:
  - `CampRespawnLabels.cs:420`: the comment "the lift glides to it" is accurate; the `top` local is still computed
    through
    `PlaqueTop`'s out parameter, as before. No change needed.
NOT ESTABLISHED: nothing was looked at in a running build. Only EditMode tests at 1/60 s frames; how the change feels,
  for example the rise against a plaque that is still fading in, is unverified. The reviewer did not re-run any Unity or
  dotnet test (the five `CampWardTextGlideEditModeTests` pass on the strength of the gate log), did not read the oracle
  test file, did not check the `.meta` files or the sync-net copy beyond confirming the diff is one `.cs` file, and
    did not
  run the gate. Quick gate only.

## P-UI-TABMAP-FLOW-1

PACKET: P-UI-TABMAP-FLOW-1: the Tab map steers, opens painted, and costs nothing while closed
  (serves owner direction 2026-11-22, UI rework lane chat, "When tab is clicked map layout is weird ... make the front
  end as fluid as possible"; not in ORDER_OF_OPERATIONS; presentation only, no wire, no crates)
BRANCH: agent/sonnet/P-UI-TABMAP-FLOW-1 @ 7c8fbd56e
ORACLE: packet/P-UI-TABMAP-FLOW-1 @ 6bc2fbba6
  - a3a614472 brief
  - df11d008d stub (clock seam `TacticalMapController.SecondsClock` and present counters, no behaviour change)
  - 250a14022 oracle
  - 6f2506921 oracle-amend
  - a41319019 oracle-amend
  - 6bc2fbba6 oracle
BASE: feature/unity-rework (integration branch was at c0ef232a1 when the packet branched)
GATE: `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-FLOW-1` on 7c8fbd56e: PASS, Unity EditMode 2008/2008 (log
  `.agent-runs/gate/20261123T003718-agent-sonnet-P-UI-TABMAP-FLOW-1-quick.log`). The result shows two implementer
  passes. The second pass re-reviewed the first pass's commit 7c8fbd56e against outcomes 1 to 7, found nothing missing,
  made no further change, and re-ran the packet's seven classes plus `PlayerFlowsEditModeTests` (64 tests): the 8
  TabMapFlow tests passed and the only failure was the pre-existing `MinimapCaptureEditModeTests` case below. Neither
  pass's gate was red.
ORACLE TESTS: `client-unity/UnityProject/Assets/Tests/Editor/TabMapFlowEditModeTests.cs`, 8 tests, all 8 red on
  assertions on the stub: `HoldingRightOnTheBigMapSteers`, `AMinimapGestureIsReleasedOnceWhenTheMinimapHides`,
  `TheOpenMapDoesNotMoveTheCamera`, `EndingAMinimapPeekRestoresTheCameraOnce`, `AClosedMapIsNotPresented` (expected 6
  presents, was 66), `TheMapIsPaintedOnTheFrameItOpens`, `TheOwnMarkerFollowsTheDrawnPositionEveryFrame`,
  `ReleaseTransientEndsAHeldBigMapGesture` (a guard added to the spec's seven). Pre-existing red on the base:
  `MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce` ("release
  flushes just the final changed point, expected 1, was 0"); the implementer's targeted run saw it fail, and the gate
  run did not.
CHANGED:
  - `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs` (711 lines): the gestures are released on
    transitions only (new `_largeShown` flag); the camera is restored only when a minimap peek ends; a surface that is
      not
    on screen is not presented; both surfaces are repainted on the frame they become visible.
  - `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs`: `FollowSelf`, the own marker follows
    `Motion.DrawnX/DrawnY` each frame.
WHAT CHANGES FOR A PLAYER: holding the right mouse button on the Tab map steers the character as it does on the minimap
  (press, held drag, release). The Tab map is already painted on the frame it opens, the camera is no longer re-pinned
  while it is open, and the player's own marker moves with the character instead of in 30 Hz steps. The map's look is
  unchanged.
AMENDED EXISTING TESTS (owner approval needed; both change a test that read the hidden minimap, which is no longer
  painted):
  - 6f2506921 `TacticalMapEditModeTests`: `NewSnapshotInvalidatesPaintThrottleAndClearsBothObservedMapSurfaces` and
    `DisconnectAndRebindClearMarksAndRefreshSingleObservedContactRail`. Old: after `SetOpen(true); Tick();` assert
    `MinimapMarks().ContactCount` while the map is open. New: the minimap is read while it is on screen (before opening,
    and after `SetOpen(false); Tick();`). The assertions' values are unchanged.
  - a41319019 `PlayerFlowsEditModeTests.DeliveredMapPointsReplaceWithoutActorFabricationOrNameDeduplication`. Old: read
    both `TacticalMapMarks` at once with the map closed, no `Tick` or `SetOpen`. New: a local `ReadBoth` reads the
      minimap
    after `SetOpen(false); Tick();` and the big map after `SetOpen(true); Tick();`, for the same four checks.
D-RULES and DEVIATIONS: D1 n/a, D2 n/a, D5/D6 none, D9 untouched (same `OrderMapMove` path, same orders on the wire).
  DEVIATIONS: none.
REVIEW (reviewer, Sonnet): LAND, no notes. Minor, not notes:
  - `TacticalMapController.cs:446` and `:470`: `FollowDrawn` is called twice in one `Tick` on a paint frame; one call
    after
    the throttle branch would do.
  - `TacticalMapController.cs:348-349`: `RefreshDeliveredMarks` presents a surface without calling `FollowDrawn`; a
    delivery after `Tick` in the same frame can show the snapshot position for one frame.
  - `TacticalMapController.cs:381-382`: `FollowDrawn` reads `_miniRoot.gameObject` without a null check; harmless, it is
    always built.
  - `TacticalMapController.cs:378-380`: closing the big map calls `_largeGesture.Abort()` without a final move; the
    packet
    allows it.
  - `TacticalMapController.cs:407` and `:418`: the public `ReleaseMinimap()` also aborts the big map's gesture (on
    typing,
    modal ownership and Escape); intended.
NOT ESTABLISHED: nothing was looked at in a running build. No manual look in play mode; the proof is the EditMode tests.
  The implementer's risk: a path that opens or closes the panel without reaching `RefreshMinimapVisibility` or
  `ReleaseTransient` would skip the big-map gesture abort. The reviewer ran no tests (relied on the gate log), did not
  check which gate step ran the TabMapFlow tests (covered only through the whole-suite count), did not read the oracle
    test
  file or the amended tests beyond the packet, did not read `TacticalMapSurface.OnDisable` to confirm that
  `SetActive(false)` on the minimap fires `Aborted` once in a player, did not confirm the pre-existing
  `MinimapCaptureEditModeTests` case is red or green on the base, and did not check `.gitignore` for the StormInk
    assets.
  Quick gate only.
