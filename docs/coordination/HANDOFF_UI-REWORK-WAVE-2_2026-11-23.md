# Hand-off: UI rework lane, wave 2 (2026-11-23)

HANDED OVER: branch `lane/ui-rework/handoff-2` @ `7438eab35` (the head of `lane/ui-rework/base-3` when it was
gated).
BASE: `feature/unity-rework` @ `c2250e6ea`, already merged into the head (merge-base `c2250e6ea`), so the merge adds
only the lane's changes: 86 files, 77 under `client-unity/` and 9 packet documents under `docs/backlog/packets/`.
The coordinator asked for a rebase onto the integration branch. The lane merged the integration branch into its own
instead, because the lane branch is a chain of `--no-ff` packet merges whose packet and agent branches this document
names. For the coordinator's merge the effect is the same.
GATE (the lane's own): `scripts/gate.sh --quick --base feature/unity-rework`: GATE PASS (quick), Unity EditMode 2124
of 2124 (headless 2122, graphics 2), dotnet 2507 of 2507, node tool tests 201 passed and 1 skipped, library copy
check "Unity copy is up to date". The lane ran no full gate (the machine was shared).
PACKETS: nine, in merge order below: eight pipeline packets (all reviewed LAND, one LAND WITH NOTES answered by its
fix) and one fix done by hand. All are presentation or test support only.
COORDINATOR: merge the branch, run the full gate on the merged tree, have the merge reviewed, re-take the pinned
values listed below, and decide what to do with the thirteen older allocation test classes ("The allocation
finding").
OWNER: approve the amended test expectations in the table below (two are still open from wave 1), sign or refuse the
three proposed DEVIATIONS rows, and look at the pictures of the running build that the lane sent in its chat.

SERVES: not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the lane's chat (the same as wave 1): a front-end
gameplay rework, notifications thoughtfully placed, quest UI, ability and spell hub, the Tab-map layout, an
optimisation sweep. The trader copy packet serves the "UI optimization sweep" line.

HOW I CHECKED THE SCOPE: `git diff --name-only feature/unity-rework 7438eab35` lists 86 paths; 77 begin with
`client-unity/` and 9 with `docs/backlog/packets/`; the same list filtered for `crates/`, `packages/`, `protocol/`,
`parity/`, `hosting/`, `scripts/`, `CardDisplayCatalog` and `BakedContent` is empty, and `MatchClient.cs` is not in
it. No wire change follows: nothing under `protocol/` or `crates/` moved.

## What changes for a player

- Trader window (`P-UI-TRADER-COPY-1`): the window talks about traders, not the server. It says "Looking for
  traders…", "3 traders in sight", "Trading…" and "No trader in sight." instead of "authoritative trade view" and
  similar. Offer buttons show the offer alone, without the "1 · " numbering, because no digit key trades.
- Quest tracker (`P-UI-QUEST-TRACKER-1`): a small card at the left, under your health plate, always lists the quests
  you have accepted and not finished (at most three, "+N more" beyond that). Each row shows the quest name, the
  objective, a countdown that turns yellow at 30 seconds and red at 10, and your progress or "Turn in". It fades in
  and out, never takes the mouse, and goes away while the map, the journal or a window is open.
- Tab map (`P-UI-TABMAP-SHEET-1`, `-fix`): the map is one panel with a full-width header (title, the match phase
  line, and the hint) over the left list, the square map with its legend, and the right list. "Close · Tab" shows
  only when the map is in toggle mode. While it is open the phase pill, the "Quests · J" button, the edge-danger
  glows and the marks drawn in the world are hidden. The three top-right buttons and the fps pill stay.
- Notices (`P-UI-TOAST-LANES-1`): the big box in the middle of the map is gone. Notices are small one-line pills in
  two places: an event lane under the match timer (up to three lines) and a feedback lane above the ability row (up
  to two). A line fades in and out, stays 2.2 to 4.5 seconds by its length, shows "x2" when the same text repeats,
  and waits while the Tab map, journal or a window is open. The journal's Notices page keeps the history. The
  refusals read "Can't pick that up" and "That choice was refused".
- Ability row (`P-UI-ABILITY-ROW-1`): the action bar is one row of six: weapon 1, swap, weapon 2, R, Shift, Q. Shift
  and Q, the quest spells, are tiles like the others with a name, a countdown, a cooldown wedge and a charge badge;
  they no longer float above the bar as text. The pickup prompt and the quest-action button share one lane above the
  row. A swap pulses the two weapon slots, not the whole bar. On a narrow window the row shrinks to clear the
  champion panel and the minimap. The weapon slots lose their "1" and "2" key caps: no digit key swaps weapons
  (Space does), so the caps were wrong.
- No visible change: `P-UI-HUD-SEAMS-1-fix`, `P-UI-ALLOC-METER-1`, `P-UI-ALLOC-PROOF-1` (test code only).

## Seen in a running build

Web round 1 (`docs/unity/WEB_ROUND.md`): a Web build of `7438eab35`, client and server from that one tree, played in
a headed Chromium at 1920 x 1080 on ports of its own. The pictures are in the lane's scratch folder, not in the
repository; the owner got them in the lane's chat. The round was still playing when this was written. The lane lead
read the first pictures himself: the hub, the HUD at the spawn, the Tab map held, a feedback toast, a camp.

What they show:
- HUD at the spawn: the plate with its gained-stats panel top left, the timer pill top centre, three buttons and the
  frame-time pill top right, the champion panel and the "Quests · J" button bottom left, the minimap bottom right.
  Bottom centre is the action row: two weapon slots (both "Empty"), the swap chip "Space SWAP" between them, then
  the R, Shift and Q tiles, and the consumables row under it. No HUD panel overlaps another. Nothing is drawn in the
  middle of the screen.
- Feedback toast: E at full health shows one pill, "Already at full health", centred just above the action row. It
  covers nothing.
- Tab map held: one dark sheet with a gold border. The header carries the title on the left, "Coliseum 9:46" in the
  centre and "Right-click to walk · release Tab to close" on the right. Under it are the legend and the route
  controls, the square map with the six zone chips, and the weapon counters with the quest list. The plate, the
  champion panel, the action row, the minimap and the timer pill are gone; the three buttons and the frame-time pill
  stay.
- A camp: its reward badge sits above the camp's leader.

Seen and not right. None of these is a wave-2 outcome failing; each goes to a later packet of the lane:
- The Tab map's title reads as "TACTICALMAP": the display font's long L runs into the M. The string is "TACTICAL
  MAP".
- "IN VIEW" on the Tab map lists "Haunted · RegionalMob x12": an internal name reaches the player.
- Two zone names on the map are crossed by marks ("HAUNTED", "HOLY").
- While the Tab map is open, one actor's health bar in the world still shows in the margin outside the sheet; names
  and marks are hidden.
- The empty weapon slots and the empty Shift and Q tiles are see-through, so world sprites, a camp badge and an NPC
  name plate show through them. In one picture a trader's name plate ("Soul Broker") sits inside the Q tile.
- Two actor names side by side run together ("Chain Warden", "Gaoler").
- The controls line under the consumables row touches the bottom edge of the window.

Not seen when this was written: an event toast under the timer, the quest tracker card with an accepted quest, the
HUD in a fight, any window size but 1920 x 1080, any text size but 1. The steadiness of the camp badge (wave 1) can
only be judged from four stills.

## Amended test expectations (the owner's to approve)

Each row is one existing test (or tool-test row) that the lane's packets changed or removed. `oracle-amend:` commits
are on the packet branch and merged into the agent branch. "Meter only" means the measuring lines changed and every
assertion stayed.

| Packet | Test | What changed | Commit |
|---|---|---|---|
| P-UI-HUD-SEAMS-1-fix | `hud-seams.test.cjs` 5 member-presence tests | removed (18 become 13) | `3e238234f` |
| P-UI-TRADER-COPY-1 | `TraderDialogViewEditModeTests.MissingUnavailable...` | two copy expectations | `2bb6d65f8` |
| P-UI-TRADER-COPY-1 | `TraderDialogViewEditModeTests.Rebinding...` | one copy expectation | `97aec697e` |
| P-UI-TOAST-LANES-1 | `HudLegacyLayoutEditModeTests.BannerAndToasts...` | measures sticky toast | `5dfdb1c38` |
| P-UI-TOAST-LANES-1 | `HudTextScaleEditModeTests` row "Notice" | read from lane label; count 63 | `53fc1a233` |
| P-UI-TOAST-LANES-1 | `Ui4EditModeTests.RecentNoticeCardExpires...` | removed with its card | `6ca1ea66f` |
| P-UI-TOAST-LANES-1 | `hud-seams.test.cjs` `ToastBottom`, `SetRecentVisible` rows | dropped | `79a0842bd` |
| P-UI-ABILITY-ROW-1 | `HudLegacyLayoutEditModeTests.WeaponBarIsCentred...` | width 318 becomes 458 | `7182e2752` |
| P-UI-ABILITY-ROW-1 | `HudTextScaleEditModeTests.TheInventoryCovers...` | count 63 becomes 61 | `231090bab` |
| P-UI-ALLOC-PROOF-1 | `CampLabelStableEditModeTests.ASteadyFrameAllocatesNothing` | meter only | `d2df26bdb` |
| P-UI-ALLOC-PROOF-1 | `UiMotionEditModeTests.AnIdleTickAllocatesNothing` | meter only | `9e00b8380` |
| P-UI-ALLOC-PROOF-1 | `QuestTrackerEditModeTests.ASteadyFrameAllocatesNothing` | meter only | `478068cf9` |
| P-UI-ALLOC-PROOF-1 | `ToastLanesEditModeTests.ASteadyFrameWithToastsAllocatesNothing` | meter only | `6e9884b9e` |
| P-UI-ALLOC-PROOF-1 | `NameMergeTests.DecidingAllocatesNothing` | a control proves the counter sees | `56e4d29f5` |
| P-UI-ALLOC-PROOF-1 | `OwnerCombatProtocolTests.Retaining...DoesNotAllocateSteadily` | same control | `2944f43aa` |
| P-UI-ALLOC-PROOF-1 | `InputSenderTests.IdleFlushAllocatesNothing` | same control | `b8cd09344` |
| P-UI-ALLOC-PROOF-1 | `GarbageTests.SessionMeMakesNoGarbageOnTheSteadyPath` | same control | `d5c9d075b` |
| P-UI-ALLOC-PROOF-1 | `ToastLaneModelTests.ATickThatChangesNothingAllocatesNothing` | same control | `839d32d37` |

Still open from the first hand-off (wave 1, merged, the owner has not answered):

| Packet | Test | What changed | Commit |
|---|---|---|---|
| P-UI-TABMAP-FLOW-1 | two tests of `TacticalMapEditModeTests` | read a surface while on screen | `6f2506921` |
| P-UI-TABMAP-FLOW-1 | `PlayerFlowsEditModeTests.DeliveredMapPoints...` | same | `a41319019` |

Corrections of the lane's own new tests (plain `oracle:` commits, not amendments of existing tests; each was a
defect in a test line that could not run until the feature existed): `d2ee217a4` (P-UI-QUEST-TRACKER-1: a shortened
text is 33 characters, not 34), `7b33b8645` (P-UI-TABMAP-SHEET-1: the minimap frame was looked up by the wrong path;
the brief gained an addendum that the header's phase line empties with the pill), `f5edeb381` (P-UI-ABILITY-ROW-1:
state items delivered in ascending key order; plus two brief changes) and `d197a1502` (P-UI-TOAST-LANES-1: the lane
tests clear the first-run hint that `Bind` arms). `8362f1600` (P-UI-ABILITY-ROW-1) replaces the lane's own withdrawn
digit-key test with `TheWeaponSlotsCarryNoDigitCap`.

## Proposed DEVIATIONS rows (the owner signs)

The lane wrote no row into `docs/DEVIATIONS.md`; these are the packets' proposals, for the coordinator to enter.
- P-UI-TABMAP-SHEET-1: the Tab map gets one backing sheet with a full-width header (heading, phase line, hint), and
  veils the phase pill, the quests launcher, the edge glows and the world marks while open (legacy: three panels on
  a dim, the HUD visible at 28 percent).
- P-UI-TOAST-LANES-1: notices are small toasts in an event lane (top centre) and a feedback lane (bottom centre)
  instead of legacy's bottom-centre stack; the box in the middle of the map is removed.
- P-UI-ABILITY-ROW-1: the quest spells Shift and Q are ability tiles in the action row (legacy has no quest spells).
- Packets that propose none: the other six. `P-UI-QUEST-TRACKER-1` names D1 (it narrows the gap to legacy's quest
  tracker line) and proposes no row; `P-UI-TRADER-COPY-1` notes that legacy has no status line to match.

## Pinned values the coordinator re-takes on the merged tree

- Unity EditMode total: 2124 on this head (headless 2122, graphics 2) against 2049 at the integration base
  (`docs/MIGRATION.md` checkpoint "Resumed (2026-11-23)": EditMode 2049/2049, headless 2047, graphics 2). The
  difference is 75; it includes the lane's new test classes and the removal of one `Ui4EditModeTests` test.
- `HudTextScaleEditModeTests.TheInventoryCoversTheWholeMatchHud`: pinned count 61 on this head, 63 at the base (read
  from both trees). The toast packet kept it at 63; the ability row lowered it by the two key caps.
- `hud-seams.test.cjs`: 13 tests (18 at the base, five removed by `P-UI-HUD-SEAMS-1-fix`); the toast packet also
  dropped two rows of its `MOVED` table and one match. New `tab-map-sheet.test.cjs`: STATE.md reports 16 of 16 for
  both files on `a7dc34e9b`, so three tests by subtraction. Node total in the gate log of this head: 202 tests, 201
  passed, 1 skipped (wave 1's hand-off recorded 203 of 204 with one skipped).
- dotnet `RoguehallaNet.Tests`: 2507 passed on this head. I did not find the base's own total in the checkpoint.
- Library copies: six pure files are new or changed with synced copies under
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (`AbilityFace`, `HudRegions`, `MapScreenLayout`,
  `NoticeRouter`, `QuestTracker`, `ToastLaneModel`); `bash client-unity/sync-net.sh --check` ran green in the gate.
- The measured table of `P-UI-ALLOC-METER-1` is copied into its packet document (`20a1a004e`): a shared record the
  coordinator may want to cite.

## The allocation finding

In this Unity editor (6000.0.85f2, Mono, EditMode) `GC.GetAllocatedBytesForCurrentThread()` reads 0. The lane
measured it twice: 0 of 20 rounds of 20,000 iterations of allocating code, alone and inside a run of heavy classes.
Every EditMode test that proves "allocates nothing" with it passes on any code.
- `P-UI-ALLOC-METER-1` built `Assets/Tests/Editor/AllocationMeter.cs`: `AllocationMeter.AssertNone(action, calls,
  what)`, built on the Unity Test Framework's `AllocatingGCMemoryConstraint`. It saw the allocating control in 20 of
  20 rounds and the clean control in 0 of 20, alone and among heavy classes, and it checks itself on every call: an
  allocating control must register and a clean control must not, else the test fails as a broken meter.
- `P-UI-ALLOC-PROOF-1` moved the lane's four allocation tests to it (camp labels, motion helpers, quest tracker,
  toast lanes). All four are green under the working meter, and a scratch run with an injected allocation turned all
  four red. So the wave-1 claims for the camp labels and the motion helpers, and the wave-2 claims for the quest
  tracker and the toast lanes, are now tested for the first time. The scratch run is not committed.
- Still blind: the allocation tests of `AbilityRowEditModeTests`. The lane converts them in wave 3 (packet
  P-UI-QUEST-OFFER-1).
- The coordinator's to decide: thirteen older test classes outside the lane still use the blind counter:
  `AtmosphereEditModeTests`, `BogTreeLandmarkEditModeTests`, `ChampionPanelNoGarbageEditModeTests`,
  `ChurchLandmarkEditModeTests`, `DressedMapEditModeTests`, `FrostWallPresenterEditModeTests`,
  `HallLandmarkEditModeTests`, `LandmarkViewEditModeTests`, `MatchHudStatusTextsEditModeTests`,
  `RenderPassEditModeTests`, `SrgbMeshColorsEditModeTests`, `VolcanoLandmarkEditModeTests` and
  `WorldMarks2EditModeTests`. The checkpoint names P-UNITY-ALLOC-TESTS-1 for them.
- `P-UI-ALLOC-PROOF-1` also made five dotnet tests assert first that the counter sees a control allocation (under
  dotnet the counter works); the list is in the table of amended expectations.

## Left out of the first hand-off

Two changes for a player came with `P-UI-TABMAP-FLOW-1` (already merged) and were missing from the first hand-off's
list: the camera is no longer snapped to the player's own drawn position while the minimap is hidden, and releasing
a right-click steer on the minimap no longer snaps the camera.

## Where the coordinator's notes on wave 1 stand

The coordinator's merge review of wave 1 (`reviewer-opus`, LAND WITH NOTES, two notes):
- Note 1 (the minimap shows a stale picture for one frame when it comes back): becomes `P-UI-TABMAP-FLOW-1-fix` in
  wave 3. The spec is ready; it is not started. It is not in this hand-off.
- Note 2 (the seams tool test pinned legacy members one by one): done by `P-UI-HUD-SEAMS-1-fix`, the first packet
  below.
- The review's minor list gets no packet (owner direction 2026-11-13). One item, `HudPulses` restoring a start
  position, is covered by wave 3's `P-UI-ABILITY-MOTION-1`.

## Not established

- No full gate was run by the lane. The gate that counts is the coordinator's full gate on the merged tree. The
  Rust, Postgres and browser-proof suites did not run for this head at all.
- EditMode, dotnet and node only: nothing was measured in a player build. The look was checked in one Web round at
  one window size ("Seen in a running build"); the event toast lane, the quest tracker card and the HUD in a fight
  had not been seen when this was written.
- Allocation freedom is proven in EditMode with `AllocationMeter` for the four converted tests, the Tab-map header
  (`TabMapHeaderNoGarbageEditModeTests`) and nothing else of the lane; the ability row's is blind.
- Every reviewer ran no suite and relied on the gate log of its own head; none re-read the oracle tests line by
  line.
- Text fit: the quest tracker is not in the limited layout fixtures, so its fit is checked only by its own tests;
  the Tab-map header room rule is tested at 640x480 and 1280x720 at text scale 1 only; toast fit at the minimum font
  size 9 for 80-character texts is unchecked in a render.
- Toast lanes: slot glide while a lane holds more lines than its capacity is not covered by a test; event pills on a
  narrow stage are not clamped beyond the flush limit; `PotionNoticeFormatter.Format` returning null or empty was
  not checked (such a text shows no toast).
- Ability row: the visual fit of the 64-unit tiles, the two-line name labels and the pair prompt lane is unproven.
- Tab map: the three world-mark presenters are hidden by `SetActive` on their roots; a root a presenter creates
  later while the map is open would show until the map is reopened (reviewer's minor note).
- Quest tracker: the clock is the delivered `ExpiresAt` minus `Session.ServerTimeMs`; the card may lag the plate
  chain by one frame (the packet accepts it).
- Order dependence: on the integration base,
  `MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce` gives 8
  of 9 when its class runs alone; it passes in the full suite (EditMode 2124 of 2124 here). Cause unknown; already
  reported to the coordinator.
- Each packet's own quick gate ran on the tree its branch forked from, not on the lane base as it stood at merge
  time: `P-UI-ABILITY-ROW-1` forked at `87e2c576e`, `P-UI-TABMAP-SHEET-1-fix` and `P-UI-ALLOC-PROOF-1` at
  `20a1a004e` (the gate prints the merge-base it used). What covers the merged tree is the gate on the head: 2124 of
  2124.

## Risks

- `PlayerFlowsEditModeTests`, `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests` and `hud-seams.test.cjs`
  are amended by several lane packets; a coordinator packet that edits them will conflict.
- `CardDisplayCatalog.cs` and `BakedContent.cs` are untouched, so `P-BAL-NUM-AUG-1` merges beside this branch.
- The left rail of the Tab map is 24 units shorter in the wide layout, so the "MAP KEY" may hide at a stage where it
  showed before (accepted by the packet).
- The quest tracker's oracle reads `_readoutStrip` and `_championHud` by reflection: renaming them in
  `MatchHud.Readouts.cs` breaks the tests.
- A sticky toast inserted into a full lane makes the lane hold one line more until the oldest leaves; two pills
  overlap briefly.
- `ClearTradePresentation` now writes an empty detail summary; the status and the empty label carry the copy.
- The ability row packet's gate treats the packet document as an oracle file: a lane or coordinator edit of a packet
  document on an agent branch turns the gate red (met by `P-UI-ALLOC-METER-1`).
- The Web round found nothing that blocks the merge. The seven things listed under "Seen and not right" are not
  fixed in this hand-off.
- Wave 3 runs on top of this head and is not part of this hand-off: P-UI-JOURNAL-POLISH-1, P-UI-VITALS-EASE-1,
  P-UI-TABMAP-MARKS-1 (running), P-UI-QUEST-OFFER-1, P-UI-ABILITY-MOTION-1, P-UI-TABMAP-FLOW-1-fix (next).

Merge order on the lane branch (first-parent, oldest first), as `git log --first-parent` shows it: `87e2c576e`,
`575252b51`, `35d71eb35`, `70edb1752`, `a54305955`, `92b2369c7` (then the records commit `20a1a004e`), `a7dc34e9b`,
`b6496534f`, `43920733c`, and `7438eab35` (the merge of the integration branch). The nine packets follow.

## P-UI-HUD-SEAMS-1-fix

```
PACKET:          P-UI-HUD-SEAMS-1-fix — serves note 2 of the coordinator's merge review of wave 1 (not in
                 ORDER_OF_OPERATIONS; owner direction 2026-11-22)
BRANCH:          agent/fable/P-UI-HUD-SEAMS-1-fix @ 3e238234f (merge 87e2c576e); ORACLE packet/… @ 3e238234f
GATE:            PASS (quick, --base lane/ui-rework/base-2), 13 node tests green (STATE.md section 16); the EditMode
                 total of that gate is not in the files I read
CHANGED:         client-unity/UnityProject/Tools/hud-seams.test.cjs (2 insertions, 6 deletions);
                 docs/backlog/packets/P-UI-HUD-SEAMS-1-fix.md
D-RULES:         D1 n/a, D2 n/a, D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: nothing a later packet could break by deleting a moved member is now caught by a structure test;
                 the compiler and the EditMode suite still are
RISKS:           none beyond the line above
```

The lane lead did this by hand, a few lines, through the gate; there is no pipeline result file. Oracle branch
`packet/P-UI-HUD-SEAMS-1-fix` @ `3e238234f`. Review: LAND by the lane's reviewer (STATE.md section 16). The change
is an amended expectation of a merged packet's oracle, agreed by the coordinator in its message to the lane; it is
the first row of the table of amended expectations.

## P-UI-TRADER-COPY-1

```
PACKET:          P-UI-TRADER-COPY-1 — serves not in the plan: owner direction 2026-11-22 (UI optimization sweep)
BRANCH:          agent/sonnet/P-UI-TRADER-COPY-1 @ 3ae7589be (merge 575252b51); ORACLE packet/… @ 97aec697e
GATE:            PASS (quick, --oracle packet/P-UI-TRADER-COPY-1 --base lane/ui-rework/base-2); EditMode 2055/2055
CHANGED:         client-unity/UnityProject/Assets/Scripts/UI/TraderDialogView.cs
D-RULES:         D1 presentation, player-facing strings only; D2 n/a; D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: the look in a live window; "Trading…" duration is not proven beyond the text
RISKS:           ClearTradePresentation now writes an empty DetailSummary (was the clear copy) so the oracle's
                 empty-summary expectation holds; the status and the Empty label still carry the copy.
```

Review: LAND, no notes. Minor, not notes: comments in `TraderDialogView.cs` (:216, :11, :201, :189) still say
"authoritative" or "delivered" (the packet allows it); "No match in progress." goes through `ClearTradePresentation`
and so also lands in the empty label (the packet's intent); the "Pick a trader." branch (:294) is unreachable, as
the packet says. Not established by the reviewer: it did not re-run a Unity class, did not read the oracle test
bodies or the two amend commits beyond confirming the diff touches no test file, and did not check the copy's fit in
a render.
Earlier attempts: none.

## P-UI-QUEST-TRACKER-1

```
PACKET:          P-UI-QUEST-TRACKER-1 — not in the plan: owner direction 2026-11-22 (quest UI, UI rework lane)
BRANCH:          agent/sonnet/P-UI-QUEST-TRACKER-1 @ 5135e21ba (merge 35d71eb35); ORACLE packet/… @ d2ee217a4
GATE:            PASS (quick, --oracle packet/P-UI-QUEST-TRACKER-1 --base lane/ui-rework/base-2). The EditMode suite
                 ran 2066 of 2066 green.
CHANGED:         client-unity/RoguehallaNet/Net/Ui/QuestTracker.cs; its synced copy under
                 client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/; Assets/Scripts/UI/QuestTrackerView.cs;
                 Assets/Scripts/UI/MatchHud.Terrain.cs (PlateChainBottom, TrackerFloor);
                 Assets/Scripts/UI/SentencedHuntPanel.cs (Bottom)
D-RULES:         D1: presentation only. The card narrows the gap to the legacy quest tracker line. D2 n/a. D5/D6
                 untouched. D9: nothing is predicted. The clock is the delivered ExpiresAt minus
                 Session.ServerTimeMs.
DEVIATIONS:      none
SHARED RECORDS:  none. The one oracle correction (the 33 vs 34 character assertion in
                 QuestTrackerEditModeTests.LongTextsAreShortenedAndFit) was made by the lane lead on
                 packet/P-UI-QUEST-TRACKER-1 (d2ee217a4); listed in the table of amended expectations.
LEGACY DOUBTS:   none
NOT ESTABLISHED: Nothing was looked at in a real render of the card at several window sizes. The tracker is not in
                 the limited layout fixtures (HudLegacyLayout and HudTextScale), so its text fit is checked only by
                 the packet's own EditMode tests. I did not run the whole-HUD fix-3 hunt card tests by themselves;
                 the whole suite in the gate covered them.
RISKS:           The tracker follows the plate chain with up to one frame of lag, which the packet accepts.
                 TrackerFloor reads the readout strip's anchors by reflection-visible private fields
                 (_readoutStrip, _championHud), so a later renaming in MatchHud.Readouts.cs would break the oracle.
```

Review: LAND, no notes. Minor, not notes: `QuestTrackerView.Draw()` rebuilds the shortened name and objective once
per clock second (about one allocation per row per second, not on a steady frame); `QuestTracker.SecondsLeft`
returns -1 for a NaN `ExpiresAt` where the packet says 0 (unreachable from decoded data); `PlateChainBottom()` uses
`_terrainTop >= 0f` and a literal `4f` gap not shared with `ShowTerrain`; `Forget()` triggers a refit per label
(only on a session change). The reviewer did not run `hud-seams.test.cjs` (the quick gate skips it unless `Tools/`
changed) and did not read the two test files line by line. Earlier attempts: one, stopped by the implementer because
the oracle asserted both 34 and 33 characters for one text; the lane lead corrected the oracle (`d2ee217a4`).

## P-UI-TABMAP-SHEET-1

```
PACKET:          P-UI-TABMAP-SHEET-1 — serves: not in the plan: owner direction 2026-11-22 (UI rework lane, "When
                 tab is clicked map layout is weird.")
BRANCH:          agent/sonnet/P-UI-TABMAP-SHEET-1 @ 01d6368b5 (merge 70edb1752); ORACLE packet/… @ 7b33b8645
GATE:            PASS (quick, --oracle packet/P-UI-TABMAP-SHEET-1 --base lane/ui-rework/base-2): editmode
                 2064/2064, dotnet, node (21 of 21 tab-map/hud-seams), sync-net check all green
CHANGED:         client-unity/RoguehallaNet/Net/Ui/MapScreenLayout.cs (+ synced Unity copy);
                 UnityProject/Assets/Scripts/UI/TacticalMapController.cs, TacticalMapController.Sheet.cs (+ .meta),
                 TacticalMapSurface.cs, MatchHud.Visibility.cs, MatchHud.Features.cs (the three ApplyPhaseStrip
                 calls and the two phase-strip clears of TickMetrics); Scripts/View/ArenaView.MapOpen.cs;
                 Tools/hud-shots.cjs and vision-shots.cjs (tabMapRect only). Pass 2 added: the two clears go
                 through a new ClearPhaseLine helper so the map header's line empties with the pill; the header's
                 phase label and hint now follow the text-size setting.
D-RULES:         D1 presentation only: legacy's square map flanked by two columns on a dim is kept; the backing
                 sheet and full-width header are an intended difference. D2 n/a. D5/D6/D9 none.
DEVIATIONS:      proposed DEV-<n>: the Tab map gets one backing sheet with a full-width header (heading, phase
                 line, hint) and veils the phase pill, quests launcher, edge glows and world marks while open
                 (legacy: three panels on a dim, HUD visible at 28 percent).
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: No real-render look check of the sheet; the header room rule is tested at 640x480 and 1280x720 at
                 text scale 1 only (at larger text sizes it measures the labels' real unshrunk width but no test
                 pins that). The WorldMarks/WorldIndicators/QuestCarrierFlags roots are hidden by SetActive (the
                 three presenters were read by the oracle author and none reads activeInHierarchy); no
                 gate-at-draw route was needed. MinimapCaptureEditModeTests.PhysicalRightRelease... (red alone on
                 the base, order-dependent) was not touched and did not fail in the gate's whole-suite run.
RISKS:           Text size: the phase label and hint no longer use FitBox (FitBox=false, Apply(TextScale())) so
                 their preferred width equals what is drawn and the room rule is not falsely passed by a shrunk
                 label; they follow the setting through MythicUi's global Apply. The left rail is 24 units shorter
                 in the wide layout, so the MAP KEY may hide at a stage where it showed before (accepted by the
                 packet). Capture-tool copies of the Tab layout (tabMapRect) were updated; the minimap copies are
                 unchanged.
```

Review: LAND WITH NOTES, one note, answered by `P-UI-TABMAP-SHEET-1-fix` (the last-but-one packet below).
- Note 1 (`TacticalMapController.Sheet.cs:80-84`): `PresentHeader` builds its cache key by string concatenation on
  every call, about 30 times a second while the map is open, so it allocates every call and defeats the cache. Fix
  asked: compare cached fields. Done by the fix packet.
- Minor, not notes: `RefreshAroundTheMap` hides the world marks only when `_map.IsOpen` changes, so a root a
  presenter creates lazily while the map is open shows until the map is reopened; `Veil` adds a `CanvasGroup` that
  would fight a later one on the same element (none today); `ApplySheet` allocates `new[] { _contactRail,
  _counterRail }` on a look change only; the reviewer did not check that `hud-seams.test.cjs` still passes (it
  expects so). The reviewer lists nothing under not established.
Earlier attempts: one, stopped by the implementer because `TheLiveRectsEqualTheLayout` looked the minimap frame up
under the canvas instead of the stage and threw; the lane lead corrected the oracle (`7b33b8645`).

## P-UI-TOAST-LANES-1

```
PACKET:          P-UI-TOAST-LANES-1 — not in the plan: owner direction 2026-11-22 (UI rework lane chat), notices as
                 small toasts in two lanes
BRANCH:          agent/sonnet/P-UI-TOAST-LANES-1 @ 5ef246cbb (merge a54305955); ORACLE packet/… @ b801688f0
GATE:            PASS (quick, --oracle packet/P-UI-TOAST-LANES-1 --base lane/ui-rework/base-2): EditMode 2061/2061;
                 dotnet NoticeRouterTests + ToastLaneModelTests 60/60; hud-seams 18/18; sync-net --check clean. The
                 first gate run failed readability (NoticeRouter.cs had no summary on its first type); fixed and
                 re-run green (second attempt).
CHANGED:         client-unity/RoguehallaNet/Net/Ui/NoticeRouter.cs, ToastLaneModel.cs (+ synced copies under
                 UnityProject/Assets/RoguehallaNet/Net/Ui/); UnityProject/Assets/Scripts/UI/ToastLane.cs (+ .meta),
                 OrderedNoticeFeed.cs, MatchHud.Notices.cs, MatchHud.TopCentre.cs, MatchHud.cs (the _notice
                 declaration and its three uses), MatchHud.Legacy.cs (_noticePill field), MatchHud.PlayerFlows.cs
                 (the SetRecentVisible line)
D-RULES:         D1: differs from legacy on purpose (legacy stacked every toast at bottom centre, 124 up;
                 LEGACY_LAYOUT.md section 8). D2 n/a. D5/D6/D9: none (presentation only, no rules in Unity). No
                 oracle file edited.
DEVIATIONS:      proposed DEV-<n>: notices are small toasts in an event lane (top centre) and a feedback lane
                 (bottom centre) instead of legacy's bottom-centre stack; the mid-map notice box is removed (owner
                 signs)
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: Look in a real render (pill widths, tone strip, fit at MinSize 9 for 80-character texts) was not
                 checked beyond the EditMode assertions. Slot glide while a lane holds more lines than its pill
                 capacity (a sticky line inserted into a full lane) is not covered by a test. Event-lane pills
                 shifted right on a narrow stage are not clamped to the stage's right edge beyond the flush limit.
RISKS:           The lanes are drawn only while WorldHudVisible and the HUD root is active; TickNotices no longer
                 requires a session, as the legacy-layout fixture has none. A sticky line inserted into a full
                 lane makes the lane hold capacity+1 lines until the oldest leaves: pools are capacity+1 and slots
                 clamp to the region maximum, so two pills briefly overlap. The Cleared handler resets the
                 remembered sticky text so the next HudOwnerRefresh re-sets it.
```

Review: LAND, no notes. Minor, not notes: `OrderedNoticeFeed.Trim()` rebuilds the 200-line `StringBuilder` on each
append once the history is full (per event batch, not per frame); the narrow-stage shift in `ToastLane.cs:127` is
also clamped by `Min(..., stageWidth/2 - width/2)` where the spec says the full difference (matters only on absurdly
narrow stages); `SetStickyNotice` always uses tone Info (the spec names no sticky tone); `ToastLaneModel.Push`
allocates one item per new line (not per frame); "Cannon reloading" without seconds differs from the history's
"Cannon reloading · s" (named in the packet); the reviewer asked that the DEVIATIONS row travel with this hand-off
(it does).
Not established by the reviewer: the full gate; no re-run of `ToastLanesEditModeTests`, the amended layout classes
or the node tests; no line-by-line read of the router and model tests; no render; whether
`PotionNoticeFormatter.Format` can return null or empty; no check of uncommitted `.meta` files beyond the diff stat.
Earlier attempts: none stopped; the oracle check found one problem before implementation (the first-run hint armed
by `Bind` stays in the rig and breaks the pill-count assertions) and one fix pass followed (`d197a1502`).

## P-UI-ALLOC-METER-1

```
PACKET:          P-UI-ALLOC-METER-1 — not in the plan: owner direction 2026-11-22 (UI rework lane, steady frame
                 allocates nothing)
BRANCH:          agent/sonnet/P-UI-ALLOC-METER-1 @ c865103f0 (merge 92b2369c7); ORACLE packet/… @ 90d4d9d10
GATE:            PASS (quick, --oracle packet/P-UI-ALLOC-METER-1 --base lane/ui-rework/base-2): editmode total 2105,
                 passed 2105, failed 0
CHANGED:         client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs
D-RULES:         D1 n/a, D2 n/a, D5/D6/D9 untouched (test support only)
DEVIATIONS:      none
SHARED RECORDS:  proposed edit: docs/backlog/packets/P-UI-ALLOC-METER-1.md, fill the section 3 measurement table
                 with the values below. I could not commit it because the gate treats the packet file as an
                 oracle file ("oracle files changed since packet/..."). The packet's write scope allows the edit, so
                 the packet and the gate disagree. [Done by the lane lead in 20a1a004e, after the merge.]
LEGACY DOUBTS:   none
NOT ESTABLISHED: The ProfilerRecorder candidate was not measured, because the Constraints meter passed every cell
                 first. The chosen meter was measured in the heavy run (PlayerFlows, HudLegacyLayout, TabMapSheet,
                 plus the meter classes), not inside the whole suite. The gate's whole-suite run was green,
                 including ItGivesTheSameAnswerTwentyTimes.
RISKS:           The meter counts GC.Alloc profiler samples through AllocatingGCMemoryConstraint. It warms by
                 running the loop once before it measures. A caller who does not warm its own code first can get a
                 false "allocates". The self-check inside AssertNone runs two 10-call controls on every call, so it
                 is cheap.
```

Measured table (from the implementer's hand-off; Unity 6000.0.85f2 Mono, batch mode; 20 rounds; collection every
third round; 20,000 iterations; allocating control = string concat plus new object, kept alive in lists):

| Candidate | alloc alone | clean alone | alloc, heavy run | clean, heavy run | verdict |
|---|---|---|---|---|---|
| `GC.GetAllocatedBytesForCurrentThread` | 0/20 | 0/20 | 0/20 | 0/20 | blind; rejected |
| `Is.Not.AllocatingGCMemory` | 20/20 | 0/20 | 20/20 | 0/20 | CHOSEN |
| `GC.GetTotalMemory(false)` / gen0 count | 1/20, 0/20 | 0/20 | 7/20 | 0/20 | unreliable |
| `GetMonoUsedSizeLong`, no hold-off | 1/20 | 0/20 | 7/20 | 0/20 | flaky; rejected |

`GC.GetTotalAllocatedBytes` does not exist on this Mono (compile error). `ProfilerRecorder` was not measured.
Review: LAND, no notes. Minor, not notes: `Raw` runs the action `calls` extra times as a warm-up, so a stateful
action gets double the calls; the self-check runs two extra constraint measurements per `AssertNone` (cheap at 10
calls); `Raw` builds a closure outside the measured region; the measured table was not in the branch when reviewed
(the lane lead recorded it in `20a1a004e`). Not established by the reviewer: it did not see the filled table, did
not run the meter tests alone or with the heavy classes, did not check whether the profiler must be enabled in a
player or graphics run.
Earlier attempts: none.

## P-UI-ABILITY-ROW-1

```
PACKET:          P-UI-ABILITY-ROW-1 — not in the plan: owner direction 2026-11-22 (UI rework lane, "ability and
                 spell hub needs optimizing")
BRANCH:          agent/sonnet/P-UI-ABILITY-ROW-1 @ bc4df7b59 (merge a7dc34e9b); ORACLE packet/… @ 231090bab
GATE:            PASS (quick, --oracle packet/P-UI-ABILITY-ROW-1 --base lane/ui-rework/base-2): EditMode 2066/2066,
                 dotnet tests, sync-net check, readability all green
CHANGED:         Pass 2: merged packet/P-UI-ABILITY-ROW-1 (amended packet and tests) into the agent branch;
                 MatchHud.ActionBar.cs (removed the "1"/"2" key caps on Slot1/Slot2); Net/MatchClient.cs reverted to
                 lane/ui-rework/base-2 (digit handler removed). Earlier commits on the branch: AbilityFace.cs (+
                 synced copy, .meta), HudRegions.cs additions (+ copy), AbilityTile.cs (+ .meta),
                 MatchHud.ActionBar/QuestRewards/Pulses/Legacy changes (one row of six, prompt lane, swap pulses
                 both slots, narrow-stage row scale).
D-RULES:         D1 presentation only; D9 untouched, nothing predicts an outcome and no command is sent by the new
                 tiles; no key binding added (digit keys stay unbound,
                 UiInputRoutingEditModeTests.DigitKeysNoLongerSwapWeaponsOutsideChestChoice green)
DEVIATIONS:      proposed DEV-<n>: quest spells Shift and Q are ability tiles in the action row (legacy has no quest
                 spells); owner signs
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: no visual look check of the row on a real render; the gate ran with merge-base 87e2c576e6 (the
                 branch is not rebased on lane/ui-rework/base-2, which already holds P-UI-TOAST-LANES-1 and others),
                 so interaction with those merged packets is untested here, notably the shared inventory in
                 HudTextScaleEditModeTests (the packet says the merged count stays 61)
RISKS:           the branch is based on an older base than base-2: merging may need the lane lead to check
                 HudTextScaleEditModeTests BuildInventory and hud-seams.test.cjs together with the toast lanes
                 packet
```

Correction by the lane lead: the first design bound the digit keys 1 and 2 to the weapon slots. That contradicted
the owner's control mapping (merged packet P-QUEST25-CONTROLS, 2026-11-18: Space swaps weapons, digits bind nothing
outside the chest picker; pinned by `UiInputRoutingEditModeTests.DigitKeysNoLongerSwapWeaponsOutsideChestChoice`).
It was withdrawn before landing: no digit key is bound, the weapon slots lost their "1"/"2" key caps, and
`MatchClient.cs` is identical to the base (it is not in the diff of this branch). The "merged count stays 61" and
the interaction with the toast packet were checked on the merged tree: the count reads 61 in
`HudTextScaleEditModeTests` on this head, and the head's gate is green.
Review: LAND, no notes. Minor, not notes: `UpdatePromptLane` runs only from `RefreshQuestRewardHud`, so a pickup
that becomes active later in the same frame moves the pill one frame late; while a spell cools, the hint and
"Equipped" strings are rebuilt once per second (a steady frame in one second allocates nothing);
`AbilityTile.Cooldown` only stores its two values until P-UI-ABILITY-MOTION-1; a comment that the hint and the
Equipped line share one formatter would help; the reviewer listed the DEVIATIONS row and the two amended
expectations as the owner's. Not established by the reviewer: no re-run of any class; no render of the 64-unit
tiles, the two-line names or the pair prompt lane; `hud-seams.test.cjs` was skipped by the quick gate; the removed
`ActionBar.cs` members were searched in `Assets` only, not in `Tools/`; the full gate. The row's own allocation test
is still on the blind counter (section "The allocation finding").
Earlier attempts: two. The first was stopped because `APickupAndAQuestActionShareThePromptLane` delivered state
items in the wrong key order and threw in its own set-up (corrected: `f5edeb381`). The second failed the gate on the
digit-key test and on the oracle-file check (a merge commit on the packet branch counted the stub files as oracle
files); the lane lead withdrew the digit keys and rebuilt both branches without merge commits.

## P-UI-TABMAP-SHEET-1-fix

```
PACKET:          P-UI-TABMAP-SHEET-1-fix — serves the review note of P-UI-TABMAP-SHEET-1 (UI rework lane,
                 presentation only; not a Rust phase item)
BRANCH:          agent/sonnet/P-UI-TABMAP-SHEET-1-fix @ f900d45a6 (merge b6496534f); ORACLE packet/… @ 424825b7f
GATE:            PASS (quick, --oracle packet/P-UI-TABMAP-SHEET-1-fix --base lane/ui-rework/base-2): editmode
                 2107/2107 passed
CHANGED:         client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.Sheet.cs (PresentHeader compares
                 cached fields, resets them in ApplySheet); plus a merge commit taking
                 packet/P-UI-TABMAP-SHEET-1-fix (the amended TabMapHeaderNoGarbageEditModeTests.cs and the packet
                 doc)
D-RULES:         D1 n/a (presentation); D2 n/a; D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: Allocation freedom is proven by AllocationMeter.AssertNone over 200 calls in EditMode only, not in
                 a player build. Text-size setting is not a cache input, as in the base key.
RISKS:           none
```

Review: LAND, no notes. Minor, not notes: the new hint constants sit in the middle of the field block
(`TacticalMapController.Sheet.cs:20-24`); the guard compares strings with `==` (allocation-free, the packet allows
it); the gate log's `changed=4 files` counts the brief and oracle commits against `a7dc34e9b`, not the fix (the fix
is one file). Not established by the reviewer: no re-run of any test; the oracle and the other tab-map classes were
not read; that `AllocationMeter` catches the allocation rests on the oracle author's notes; that a TMP `.text`
getter allocates nothing is from the reviewer's knowledge of TMP; whether fork base `20a1a004e` versus the current
base matters at merge was not examined.
Earlier attempts: one. The first gate went red on the oracle's own set-up control, which used a meter that read 0
inside the whole suite; that is the blind-meter finding that led to `P-UI-ALLOC-METER-1`. The lane lead rebuilt both
branches on the lane base and the oracle moved to `AllocationMeter`.

## P-UI-ALLOC-PROOF-1

```
PACKET:          P-UI-ALLOC-PROOF-1 — serves owner direction 2026-11-22 (UI rework lane, UI optimization sweep); not
                 in ORDER_OF_OPERATIONS
BRANCH:          agent/sonnet/P-UI-ALLOC-PROOF-1 @ bfd83e81d (merge 43920733c); ORACLE packet/… @ bfd83e81d
GATE:            PASS (quick, --base lane/ui-rework/base-2; EditMode 2105/2105)
CHANGED:         none. The oracle author found all four converted tests green on the base, so no product file needed
                 an edit. The branch head equals packet/P-UI-ALLOC-PROOF-1.
D-RULES:         D1 n/a, D2 n/a, D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: Only that the four steady-frame paths allocate nothing in 200 calls under the working meter. Other
                 classes that still use the broken counter are out of scope.
RISKS:           none. The StormInk prop assets were restored and the tree is clean.
```

The oracle commits carry the change: four Unity tests moved to `AllocationMeter.AssertNone` and five dotnet tests
that use the counter first assert that a control allocation registers (the spec named one dotnet class; the search
found four more, `GarbageTests`, `NetTests`, `OwnerCombatProtocolTests`, `NameMergeTests`). The packet document
records that all four converted tests are green on the base and that a scratch run with `new object().GetHashCode()`
added to each measured action turned all four red (not committed).
Review: LAND, no notes. Minor, not notes: the converted `UiMotionEditModeTests` idle tick wraps `now` with `step++ %
200`, so `now` goes backwards at the wrap (harmless while pulses are idle; the packet's trap list allows it); the
claim that the tests can fail comes from the uncommitted scratch run; the five dotnet guards each copy a ten-line
helper (one per class, as the packet asks). Not established by the reviewer: no re-run; the oracle author's scratch
run was not verified; `AllocationMeter.cs` was not read for its warm-up and controls; the diffs of `NameMergeTests`,
`OwnerCombatProtocolTests` and `ToastLaneModelTests` were not read; the reviewer reviewed `20a1a004e..bfd83e81d`
because the branch has no commit beyond the packet branch.
Earlier attempts: none.

## Minor, not notes

These get no packet (owner direction 2026-11-13).
- P-UI-HUD-SEAMS-1-fix: none recorded.
- P-UI-TRADER-COPY-1: comments in `TraderDialogView.cs` still say "authoritative" and "delivered".
- P-UI-TRADER-COPY-1: "No match in progress." also lands in the empty label through `ClearTradePresentation`.
- P-UI-TRADER-COPY-1: the "Pick a trader." branch (`TraderDialogView.cs:294`) is unreachable.
- P-UI-QUEST-TRACKER-1: `Draw()` rebuilds the shortened strings once per clock second (about once a second per row).
- P-UI-QUEST-TRACKER-1: `SecondsLeft` returns -1 for a NaN `ExpiresAt` (the packet says 0); unreachable from data.
- P-UI-QUEST-TRACKER-1: `PlateChainBottom()` uses a literal `4f` gap and `_terrainTop >= 0f`, not shared with
  `ShowTerrain`.
- P-UI-QUEST-TRACKER-1: `Forget()` refits every label (session change only).
- P-UI-TABMAP-SHEET-1: world-mark roots created lazily while the map is open stay visible until it is reopened.
- P-UI-TABMAP-SHEET-1: `Veil` adds a `CanvasGroup` that would fight another one added later to the same element.
- P-UI-TABMAP-SHEET-1: `ApplySheet` allocates `new[] { _contactRail, _counterRail }` on a look change.
- P-UI-TOAST-LANES-1: `OrderedNoticeFeed.Trim()` rebuilds the 200-line builder per append once full.
- P-UI-TOAST-LANES-1: the narrow-stage shift is clamped by `Min(..., stageWidth/2 - width/2)` beyond the spec.
- P-UI-TOAST-LANES-1: `SetStickyNotice` always uses tone Info.
- P-UI-TOAST-LANES-1: `ToastLaneModel.Push` allocates one item per new line (not per frame).
- P-UI-TOAST-LANES-1: "Cannon reloading" without seconds differs from the history's "Cannon reloading · s" (named).
- P-UI-ALLOC-METER-1: `Raw` runs the action `calls` extra times as a warm-up; a stateful action gets double.
- P-UI-ALLOC-METER-1: the self-check runs two extra constraint measurements per `AssertNone`; `Raw` builds a
  closure.
- P-UI-ABILITY-ROW-1: the prompt lane can lag a pickup by one frame.
- P-UI-ABILITY-ROW-1: the hint and Equipped strings are rebuilt once per second while a spell cools.
- P-UI-ABILITY-ROW-1: `AbilityTile.Cooldown` is stored but unread until P-UI-ABILITY-MOTION-1.
- P-UI-ABILITY-ROW-1: a comment would say the hint and Equipped line share one formatter.
- P-UI-TABMAP-SHEET-1-fix: hint constants sit mid field block; `==` on strings instead of a reference check.
- P-UI-TABMAP-SHEET-1-fix: the gate log's `changed=4 files` counts brief and oracle commits, not the fix.
- P-UI-ALLOC-PROOF-1: the idle-tick test's `now` wraps backwards at 200 steps.
- P-UI-ALLOC-PROOF-1: five dotnet guards copy one helper each.
- P-UI-ALLOC-PROOF-1: the scratch run that shows the tests can fail is not committed.
