# Hand-off: UI rework lane, wave 3 (2026-11-23)

HANDED OVER: branch `lane/ui-rework/handoff-3`: the gated head `55dd593be` (the head of `lane/ui-rework/base-3` with
all fifteen packets) plus one commit that adds this document and nothing else.

BASE: `feature/unity-rework` @ `ce3841ccd` (integration 85, which holds waves 1 and 2 of the lane), merged into the
lane branch at `4102837d4` (merge-base `ce3841ccd`). The coordinator's merge therefore adds only the lane's wave 3:
111 files, 79 added and 32 modified. 95 are under `client-unity/` (8 in `RoguehallaNet`, 8 in `RoguehallaNet.Tests`,
79 in `UnityProject`) and 16 under `docs/` (15 packet documents under `docs/backlog/packets/` and the wave 2
hand-off, which gains its 69-line addendum, under `docs/coordination/`). The hand-off commit adds this document
beside it.

GATE (the lane's own): PASS (quick) on `55dd593be`, `scripts/gate.sh --quick --base feature/unity-rework
--allow-artefacts`: Unity EditMode 2302 of 2302 (headless 2300, graphics 2), dotnet 2562 of 2562, the node tool
tests 203 (202 passed, 1 skipped), the library copy current. It was the third run: runs one and two stopped at the
dotnet step on the decoder probe test that is not the lane's (`AllocationProbeTests.PlainDecode…(68)`, see "Dropped,
parked and still to come"). Log `.agent-runs/gate/20261123T044730-lane-ui-rework-base-3-quick.log` in the lane's
join worktree. No full gate.

PACKETS: fifteen, in merge order below, all through the lane's pipeline (oracle, implementer, quick gate, review).
Every one was reviewed LAND with no notes by the lane's reviewer. All are client-side: presentation, one line of
key routing (`MatchClient.cs`) and test support. Nothing moves in `crates/`, `protocol/` or `parity/`, so no wire
change follows.

COORDINATOR: merge `lane/ui-rework/handoff-3` (not `base-3`), run the full gate on the merged tree with
`--allow-artefacts`, have the merge reviewed and re-take the pinned values listed below. The flag is needed because
exactly one file in the diff is on the gate's build-artefact list:
`client-unity/UnityProject/Assets/Resources/Showcase/CinzelDecorative.asset` (deliberate: `P-UI-HEADING-GAPS-1` adds
pair-adjustment records to the display font). The help line of `scripts/gate.sh` reads as below. Without the flag
the artefact rule (`ARTEFACT_RE`) fails the gate with "build artefacts changed". The lane ran no full gate.

```
--allow-artefacts   build artefacts may change (Showcase font assets, BakedContent.cs, ShaderGraphSettings.asset)
```

OWNER: five things wait for his word, one line each:

- the three keys of the offer card (`P-UI-QUEST-OFFER-1`): F talks to a quest giver, Enter and numpad Enter accept,
  Escape closes. They are not in the control mapping of `P-QUEST25-CONTROLS`. See them BEFORE the merge.
- the amended test expectations: thirteen rows, sorted below (one dropped guarantee, one loosening).
- the proposed DEVIATIONS rows: seven, and the correction of the toast row already proposed.
- the parked question: should the quest reward choice wait for F, with digits 1 to 3 inside its window
  (`P-UI-QUEST-REWARD-PROMPT-1`)? Asked in the lane's chat; not built.
- the question whether the two left-edge panels should be folded by default: wave 4 makes them foldable and leaves
  them open unless he says otherwise.

SERVES: not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the lane's chat (the same as waves 1 and 2): a
front-end gameplay rework, with the quest UI, the ability and spell hub, the Tab-map layout and an optimisation
sweep. Seven packets answer a request, a note or a minor of the coordinator's merge reviews of waves 1 and 2 ("Where
the coordinator's notes stand").

HOW I CHECKED THE SCOPE: `git diff --name-only feature/unity-rework 55dd593be` lists 111 paths: 95 begin with
`client-unity/` and 16 with `docs/`. The same list filtered for `crates/`, `packages/`, `protocol/`, `parity/`,
`hosting/`, `scripts/`, `CardDisplayCatalog` and `BakedContent` is empty. The gate's artefact pattern matches one
path, `CinzelDecorative.asset` (226 lines added, 1 removed). `MatchClient.cs` is in the list this time: one line,
the F key, from `P-UI-QUEST-OFFER-1`.

## What changes for a player

One or two sentences per packet; what a running build showed is in the next section.

- `P-UI-JOURNAL-POLISH-1`: the quest journal leads with the live facts of the selected quest (progress, time left,
  whether it is ready to turn in) in a larger line, bright when you hold the quest, in the game's words instead of
  "Acceptance queued" or "Server deadline in 134s". The objective comes first and a brief that repeats it is hidden;
  ACCEPT becomes a disabled "ACCEPTING…" after one press, so it cannot be sent twice; an offer out of reach says
  where to go; the selected row stays marked; list rows read "OFFER · HERE", "OFFER", "ACCEPTED", "TURN IN",
  "COMPLETE" instead of "OPEN". The stripe on the rows is gone.
- `P-UI-VITALS-EASE-1`: the health and barrier bars on your plate glide to their new value instead of jumping. Lost
  health stays for a quarter of a second as a dimmer red strip behind the bar, then drains. Numbers and colours
  change at once; a respawn or full heal, and reduced motion, snap.
- `P-UI-TABMAP-MARKS-1`: the marks on the Tab map, with their clocks, badges and labels, are drawn larger as the map
  grows (up to 1.4 times, never smaller), and text on them is never smaller than 10. Zone names grow with them. The
  minimap is unchanged.
- `P-UI-TABMAP-FLOW-1-fix`: when the minimap comes back (the Tab map or a window closes) it shows the current
  picture on the first frame, not the one from before it was hidden.
- `P-UI-QUEST-OFFER-1`: F beside a quest giver, with nothing to pick up in reach, opens a small offer card at the
  left. Enter accepts, Escape or F closes it. The card takes no focus and never blocks moving, attacking or casting;
  it closes when the server confirms, when the offer is gone, or half a second after you leave reach. A prompt "F ·
  Talk to <giver>" shows when an offer is in reach. The quest launcher never had a caption: "Choose quest reward",
  "Nearby quest actions · n" and "Return to turn in your reward" are drawn for the first time, with the new "Talk
  to" captions.
- `P-UI-MAP-OPEN-WORLD-1`: while the Tab map or the planning screen is open, the world's own markings are hidden:
  health and barrier bars (one used to show in the margin beside the sheet), names, rings, status glyphs, bounty,
  badges, the Wish icon and the camp patience line. Actors and footprints stay under the dim. With the map in toggle
  mode they stay hidden until the key is pressed again.
- `P-UI-WISH-TILE-1`: the Wish tile (R) is built like Shift and Q. It shows the Wish's name instead of "1/2 · 9",
  the countdown once, a stock badge only for a Wish that can hold more than one charge, a wedge that shrinks while
  it recharges and a cast bar that fills from the left (it drew full for the whole cast). The name is hidden while
  it recharges. It reads Ready exactly when the server would accept the cast; a Wish holding a charge reads Ready
  with no countdown.
- `P-UI-PLAYER-WORDS-1`: the IN VIEW list of the Tab map speaks in the game's words: "Haunted · creatures x12",
  "Plague · creature x1", "Haunted · zombies x3", "Quest givers x2" instead of an internal name such as
  "RegionalMob". Rows have a fixed order; player lines ("Plague · Player") are unchanged.
- `P-UI-ZONE-NAMES-1`: each of the six zone names on the Tab map picks one of six spots around its icon, clear of
  camps, wards, chests, delivered points and the map edge. A name may now lie outside its zone.
- `P-UI-HEADING-GAPS-1`: headings in the display font keep a visible gap between their words: "TACTICAL MAP" no
  longer reads "TACTICALMAP", and the same holds for PLAN YOUR RUN, IN VIEW, QUESTS & TRADERS and the other display
  headings.
- `P-UI-PAIR-LANE-NARROW-1`: on a window narrower than about 1040 units the pickup pill and the quest button above
  the ability row stay a centred pair at one height and size, scaled with the row.
- `P-UI-ABILITY-MOTION-1`: the wedges of R, Shift and Q and the Wish cast bar move on every frame (they stop 150 ms
  past the newest snapshot) instead of 20 times a second. The attack timer is a thin bar on the weapon in hand, not
  a dark wedge over both weapon slots. A press pops its element about its centre by 8 percent (was 10) and no longer
  drifts toward a corner.
- No visible change: `P-UI-ABILITY-ROW-1-fix` (the frame with nearby quest actions allocates nothing),
  `P-UI-ALLOC-METER-2` and `P-UI-ALLOC-METER-3` (test code only).

## Seen in a running build

Web round 2 (`docs/unity/WEB_ROUND.md`): the Web player built from `55dd593be` (509 s, no shader error; the server
in 39 s), two sessions (Fire, then Frost) in a headed Chromium at 1920 x 1080 against a local arena, 29 stills,
the page's error list empty at the end. A sub-agent took the stills; the lane lead read the ones named below at
full size. One window size, text scale 1, stills only: nothing was judged in motion, at another size or with
reduced motion.

As designed:

- Wish tile: while it recharges it shows the whole seconds and the wedge and no name; ready it shows the name
  ("Blaze", "Frost Wall") in a gold frame. The stock badge shows for the two-charge Wish (0, 1, 2) and not for the
  one-charge Wish. A cast that leaves a charge leaves the tile Ready in the next still. The cast bar was caught
  once, as a short stub at the bottom left in the still right after a Frost Wall was placed, and was gone 300 ms
  later; Blaze Step is instant and shows none. So the bar exists and starts at the left; its run was not seen.
- Offer: the prompt "F · Talk to Sanctum quest giver" above the row; the card under the plate with giver,
  quest, objective, the reward in gold, "ACCEPT · Enter" and "Esc · close"; "ACCEPTING…" right after Enter;
  one second later the card is closed, the toast "Accepted: Stay A While" is under the timer and the tracker
  card under the plate.
- Journal: the selected row is marked; rows read "OFFER · <giver>" and "ACCEPTED · <giver>"; the status line of the
  held quest reads "Accepted · 0 / 30 s"; an offer out of reach reads "Offer · not in reach" with "Go to Rime quest
  giver to accept" beside the button.
- Vitals: three stills about 120 ms apart after a hit (253, 214 and 204 of 950): the fill matches the number in
  each; a red strip stands at the end of the fill in the first two and is gone in the third.
- Attack timer: a thin gold bar along the bottom of the weapon slot in hand, long right after a click and short 100
  ms later; no wedge over the slots.
- Tab map: one sheet. The title reads "TACTICAL MAP" with its gap (round 1, on wave 2: "TACTICALMAP"), and so do
  WEAPON COUNTERS and RUN RESULTS: the Web player applies the pair records. Marks and their text are larger than in
  round 1. The six zone names stand beside their icons on their plates. IN VIEW reads "Holy · creatures x5", "Quest
  givers x2", "Plague · creatures x19". While the map is held no name, bar or ring shows in the margin (the
  creatures in the margin had no bar at that moment, so a bar at the edge was not put to the test).

Seen and not right. None breaks what a wave 3 packet promised except the second half of item 1; items 1 and 3 show
more because of this wave. None is fixed in this hand-off:

1. Tab map text. A label right of a mark near the right edge is cut by the map frame ("AUG" reads "AUC", "+0.75%"
   loses its end) and one at the top edge loses its upper half; labels of neighbouring marks print over each other
   ("+3.75" on "+3%"). Both are older than this wave; `P-UI-TABMAP-MARKS-1` made the text larger. And against
   `P-UI-ZONE-NAMES-1`: in the second session FROST overlaps a camp mark and the plate of HAUNTED covers a mark's
   label (labels are not obstacles in that packet; a mark is). Wave 4: `P-UI-TABMAP-LABELS-1`.
2. Journal details. For a held quest with action buttons ("Abandon", "Meditate") the REWARD and PARTICIPANTS
   sections are pushed under the status line; only the top of the word REWARD shows. Older than this wave. Wave
   4: `P-UI-JOURNAL-DETAILS-1`.
3. After the accept the caption above the row reads "Nearby quest actions · 1". It names neither the action
   ("Meditate", a button in the journal) nor a key. The caption is drawn for the first time in this wave. Wave 4
   proposal: `P-UI-QUEST-ACTION-PROMPT-1` (it would use F, so the owner sees it first).
4. World labels are drawn under the HUD, as they should be, but show through what is see-through: a creature's name
   through an empty Shift tile and between the two pills under the row, a giver's label through the header of the
   build panel. Wave 4: `P-UI-TILE-PLATES-1`, `P-UI-LEFT-EDGE-1`.
5. Labels pile up around a player who stands at a giver: the giver's label lies over the player's status icons and
   name. Wave 4: `P-UI-WORLD-NAMES-1` and `-2`, then a labels packet.
6. The controls line under the row is close to invisible on sand. Wave 4: `P-UI-CONTROLS-LINE-1`.
7. Minor: the "Quests · J" launcher stays on the results screen; with nothing selected the journal shows five
   headings with no content.

## Amended test expectations (the owner's to approve)

Each row is one existing test that a packet changed; `oracle-amend:` commits are on the packet branch and merged
into the agent branch. The kind sorts them as the wave 2 review sorted the last hand-off's rows: a re-statement
forced by the new look, a strengthening, or a dropped guarantee. Test names are under the table.

| No | Packet | Kind | Commit |
|---|---|---|---|
| 1 | P-UI-JOURNAL-POLISH-1 | re-statement | `98aecaaaf` |
| 2 | P-UI-JOURNAL-POLISH-1 | re-statement | `8d76edc02` |
| 3 | P-UI-JOURNAL-POLISH-1 | re-statement | `e32673f53` |
| 4 | P-UI-VITALS-EASE-1 | re-statement | `a10c7ef0c` |
| 5 | P-UI-TABMAP-FLOW-1-fix | strengthening | `481408f91` |
| 6 | P-UI-QUEST-OFFER-1 | strengthening | `cfd1db34b` |
| 7 | P-UI-QUEST-OFFER-1 | strengthening | `4af4fb332` |
| 8 | P-UI-WISH-TILE-1 | re-statement | `717b86e2c` |
| 9 | P-UI-WISH-TILE-1 | re-statement | `a1449338a` |
| 10 | P-UI-WISH-TILE-1 | dropped guarantee | `47d98c472` |
| 11 | P-UI-ALLOC-METER-3 | loosening | `b117dd95b` |
| 12 | P-UI-ALLOC-METER-3 | re-statement | `0c54f675c` |
| 13 | P-UI-ABILITY-MOTION-1 | re-statement | `e516ce924` |

1. `QuestJournalViewEditModeTests.UntimedAcceptedQuestShowsProgressWithoutWaitingForADeadline`: its row now reads as
    not ready to turn in; both assertions stay. Forced by the new copy.
2. `QuestJournalViewEditModeTests.AcceptUsesCurrentHandleAndFreshCanAcceptThenSessionBatchesOnlyThatIntent`: status
    before the accept "Preview · time is up", after it "Accepting…". Forced by the new status words.
3. `PlayerFlowsEditModeTests.PlanningPreviewRetiresDeferredAcceptAndWaitsForFreshNativePermission`: contains
    "Accepting" instead of "Acceptance queued".
4. `MatchHudWishBarrierEditModeTests.OwnerFactsRenderAndClearAcrossWishAndSessionResetTransitions`: one
    `TickOwnerVitals` call before the empty-barrier assertion, because the barrier now eases.
5. `PlayerFlowsEditModeTests.DeliveredMapPointsReplaceWithoutActorFabricationOrNameDeduplication`: its disconnect
    case could not fail for the big map; it now opens the big map with a cue, drops the connection and asserts the
    big map's counts are 0 before any `Tick`.
6. `AbilityRowEditModeTests.AnUnchangedTileAllocatesNothing`: from the byte counter, which reads 0 in this editor,
    to `AllocationMeter.AssertNone` over 200 calls. Green on the base.
7. `AbilityRowEditModeTests.ASteadyQuestRefreshFrameAllocatesNothing`: the same change; green on the base (note 1
    (a) of the wave 2 review).
8. `HudTextScaleEditModeTests` (set-up rows): three rows read the Wish tile's `Seconds`, `Name` and `Charges` labels
    instead of the deleted fields; the pinned count stays 61.
9. `MatchHudIconPresentationEditModeTests`, test
    `WishSummaryStaysCompactWhileFullDeliveredDescriptionRemainsFocusedAndDismissible`: reads `WishTile/Icon` and
    asserts the name empty and the seconds "1" at 5000 ms instead of the "1/2 · 1" summary; every hint line stays.
10. `MatchHudWishBarrierEditModeTests` (set-up and two tests): the Wish summary label is gone by design, and with it
    seven assertions on its text ("0/2 · 2:05", "1/2 · 1", "1/2", "0/2", "—", "2/2", "—"). The hint card keeps its
    own assertion at each of those points in the same tests (stock, "elapsed labels never predict stock", "None
    delivered"). For the tile that replaced the summary, `WishTileEditModeTests` drives the HUD's own refresh and
    asserts the stock badge (1, 0, 3, none at maximum 1) and the no-Wish look after "none delivered" and after
    `ClearOwnerPresentation`; `WishFaceTests` pins the badge to the delivered charges. One fact has no successor on
    the tile: that a refresh at a later server time with the same delivered stock leaves the badge unchanged (the
    removed line "elapsed time never predicts summary stock either"). The badge is computed from the delivered
    `Charges` alone (`AbilityFaces.Wish`), so it holds by construction today; wave 4 puts the assertion back. The
    lane lead read the commit and the three test files for this row.
11. `AllocationMeterCountEditModeTests.CountAndAnswerAgreeTwentyTimes`: the witness, one run of Unity's
    `AllocatingGCMemoryConstraint`, becomes the smallest of three runs, and success needs all three to allocate. One
    extra sample in Unity's constraint no longer fails it; the meter's own count is compared as before.
12. `AllocationMeterCountEditModeTests.AssertAtMostPassesAtTheLimitAndFailsOneAbove`: the override sees the action
    three times (once per measurement), not once. Forced by the retry rule.
13. `AbilityRowEditModeTests.ASwapWithoutASlotPulsesBothSlotsAndNeverTheBar`: reads `HudPulses` instead of the old
    tuple list; the helper `MatchHudPulseSeconds` is deleted.

Additions to an existing test file (not amendments): `hud-seams.test.cjs` gains one case, "MatchHud.Pulses.cs has
one owner of pulses and one reduced-motion source (P-UI-ABILITY-MOTION-1)", in `4139b97a7`. It asserts that
`MatchHud.Pulses.cs` does not mention `ShowcaseSettings` and declares no list of tuples.

Rows 5 to 7 make a test able to fail where it could not. Row 10 is the one dropped guarantee: the summary text it
checked no longer exists by design. Row 11 loosens one comparison. The rest are re-statements.

## Proposed DEVIATIONS rows (the owner signs)

The lane wrote no row into `docs/DEVIATIONS.md`; these are the packets' proposals, for the coordinator to enter once
signed. Seven packets propose one; the other eight propose none.

- `P-UI-VITALS-EASE-1`: legacy: the health and barrier bars have a 100 ms linear width transition and no ghost
  (`packages/client/src/style.css:312`); Unity: they ease at a constant rate (falls from damage 6, every other
  move 3 bar-widths per second) and lost health lingers in a ghost fill for .25 s, then drains at 2 bar-widths
  per second.
- `P-UI-TABMAP-MARKS-1`: legacy: the glyph scale is the map size over 640, clamped to .55 to 1.25, with a 9 px floor
  for text (`packages/client/src/ui/planningMath.ts:148-151`); Unity: marks grow with the map from 480 to 672 units
  and never shrink, and mark text has a 10 px floor.
- `P-UI-QUEST-OFFER-1`: legacy: a quest dialog opens with F, and Enter or 1 accepts, Escape or F closes, only while
  the dialog has focus (`packages/client/src/ui/questDialogMath.ts:26-33`); Unity: the card accepts with Enter and
  numpad Enter whether or not it has focus, digit 1 and numpad 1 do not accept (digits bind nothing outside the
  chest picker), and the card never blocks play.
- `P-UI-MAP-OPEN-WORLD-1`: legacy: nothing in the world is hidden under the Tab map except the weapon range guide
  (`planning.css:57-63`, `RaidScene.ts:1629-1638`, DEV-016); Unity: while the Tab map or planning screen is open the
  world hides names, camp respawn labels and ward beacons, the roots "World markers", "World indicators", "Quest
  carrier flags" and "Camp patience outlines", the overhead cluster (rings, glyphs, bounty, Wish icon, badge, marks,
  TAXED cue, barrier bubble, Fire roam glow) and the health and barrier bars, and in toggle mode keeps them hidden
  until the key is pressed again. This one row covers everything earlier packets already hid; actors and footprints
  stay under the 86 percent dim.
- `P-UI-WISH-TILE-1`: legacy: the Wish slot of `docs/rework/LEGACY_LAYOUT.md` section 4 has a 24 x 24 icon, a name
  always shown, a blocking-reason line and a faction-coloured ready frame; Unity differs in five places: the name is
  hidden while recharging, the icon is 16 units in a corner, there is no blocking-reason line, the ready frame is
  not faction-coloured (the glow wash still is), and a Wish holding a charge shows no countdown for the next one
  (the hint card keeps it).
- `P-UI-ZONE-NAMES-1`: legacy: zone names are placed by a larger rule that keeps them inside the zone; Unity: a name
  takes one of six fixed spots around the zone icon that overlaps no camp, ward, chest, delivered point or map edge,
  and may leave the zone.
- `P-UI-ABILITY-MOTION-1`: legacy: the attack timer is a shared `wb-sweep` wipe drawn in both weapon slots
  (`weaponBar.ts:78-81`, :207-214); Unity: it is a bar on the weapon in hand only, and the press pop is 8 percent
  (Unity's own value was 10).

Correction to a row already proposed (the toast row of the wave 2 addendum). The clause "no red tone (refusals are
amber)" should read: "legacy's red tone and 4 to 8 s hold are for the client's own join, connection and menu
errors, which Unity does not show as toasts; refusals are plain in legacy and amber in Unity". The lane's drafter
read legacy (`packages/client/src/main.ts`): refusals of the server are shown plain, with a 1.8 s hold. With
legacy's formula every routed line of at most 80 characters would hold exactly 4 s. This is why
`P-UI-TOAST-TONES-1` was dropped.

## Pinned values the coordinator re-takes on the merged tree

- Unity EditMode total: 2302 of 2302 (headless 2300, graphics 2) in the lane's gate on `55dd593be`, against 2124 at
  the integration base (integration 85's full gate, EditMode 2124 of 2124).
- `HudTextScaleEditModeTests.TheInventoryCoversTheWholeMatchHud`: stays 61. No packet changed it;
  `P-UI-WISH-TILE-1` kept it at 61 (the three rows were re-pointed), and the test reads 61 on this head. Wave 2 had
  taken it from 63 to 61.
- `hud-seams.test.cjs`: 14 tests (13 at the integration base; one added by `P-UI-ABILITY-MOTION-1`). Node total in
  the lane's gate log of this head: 203 tests, 202 passed, 1 skipped (the wave 2 hand-off had 202, 201 and 1).
- dotnet `RoguehallaNet.Tests`: 2562 of 2562 in the lane's gate on `bab8f6f2b` and in the third gate run on
  `55dd593be` (its first two runs failed at the dotnet step on `AllocationProbeTests`, see "Dropped, parked and
  still to come"). The wave 2 hand-off reported 2507 on its head; the lane did not run dotnet on the bare
  integration base.
- Library copies: eight pure files changed or new under `client-unity/RoguehallaNet/Net/Ui/` with synced copies
  under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/`: `AbilityFace`, `BarEase`, `HudRegions`,
  `MapLabelPlacement`, `MapMarkScale`, `PlayerWords`, `QuestJournalCopy`, `QuestOffer`. Every packet gate printed
  "Unity copy is up to date"; `bash client-unity/sync-net.sh --check` is the check.
- File sizes that a test pins: `MatchHud.cs` has 572 lines against the limit of 600 in `hud-seams.test.cjs`;
  `MatchHud.QuestRewards.cs` has 593 lines against the repository limit of 800.

## The allocation meter

Wave 2 built `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`) because
`GC.GetAllocatedBytesForCurrentThread()` reads 0 in this editor (6000.0.85f2). `P-UI-ALLOC-METER-2`, asked for by
the coordinator for its byte-budget conversion, added a counting form (`Samples`, `AssertAtMost`: `GC.Alloc`
profiler samples, not bytes) and a test, `AllocationMeterCountEditModeTests.CountAndAnswerAgreeTwentyTimes`, that
compares the meter's count with one run of Unity's own `AllocatingGCMemoryConstraint`. In whole-suite runs under a
loaded machine that test failed about every second run ("expected 21, was 20"). It turned the lane base red, failed
both gate attempts of `P-UI-HEADING-GAPS-1` (rounds 12 and 3) and two gate runs of `P-UI-ABILITY-MOTION-1` (21
against 20, then 23 against 20); `P-UI-WISH-TILE-1` saw one red on a neighbouring test,
`AllocationMeterEditModeTests.ItGivesTheSameAnswerTwentyTimes`. The first three failing rounds (6, 12, 3) were
multiples of 3, the rounds that begin with `GC.Collect()`.

The lane lead ran an experiment (a scratch EditMode class, nothing committed; files in
`.agent-runs/lane-ui-rework/understand/meter-experiment/`, outside the repository): does the meter count allocations
of other threads? Four ways of reading the `GC.Alloc` recorder: A is the helper's order, copied from Unity's
constraint (`CollectFromAllThreads()`, then read `sampleBlockCount`); B reads the count before it; C never calls it
inside the measurement; D uses no thread filter. Clean loop: 1000 calls that add to a static int (expect 0);
allocating loop: 10 calls with 2 allocations each (expect 20). Run 2, 500 rounds per cell, rounds with a wrong count
(clean / allocating):

| Condition | A (the meter's order) | B | C (the cure) | D (no filter) |
|---|---|---|---|---|
| quiet | 0 / 0 | 0 / 0 | 0 / 0 | 2 / 1 |
| one thread allocating all the time | 206 / 107 | 61 / 20 | 6 / 1 | 500 / 500 |
| 2000 finalizable objects dropped before each window | 2 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |

The thread filter works (D is far off; A to C are off by at most 5), but the order of A lets a few samples of other
threads in: 206 of 500 clean windows (41 percent) under the hot thread, against about 1 percent and at most 1 sample
for C. In the gate failures the extra sample was in Unity's constraint (21) and the meter's count (20) was right.

`P-UI-ALLOC-METER-3` is the cure: no `CollectFromAllThreads()` inside a measurement; a positive reading is repeated
up to three times and the smallest count wins; a new class, `AllocationMeterNoiseEditModeTests`, runs the meter
beside a thread that allocates all the time (the old code read wrong in 9 of 100 rounds there); and the comparing
test takes the smallest of three runs of Unity's constraint (rows 11 and 12 above). The coordinator has been told.
The three meter classes ran 25 of 25 in each of five runs before the gate, which was green on its first run. What
the meter claims:

- It counts the calling thread only.
- An allocation that does not happen in every window of calls is not reported (code that allocates once per many
  calls, once per second or in some windows slips through).
- Nothing is claimed for another editor version than 6000.0.85f2.

The thirteen older classes named in the wave 2 hand-off that still use the blind counter are not in this diff. The
lane converted its own two blind tests (rows 6 and 7). The dotnet decoder-probe flake has a different mechanism (see
"Dropped, parked and still to come").

## Where the coordinator's notes stand

- Wave 1 note 1 (the minimap showed a stale picture for one frame when it came back): answered by
  `P-UI-TABMAP-FLOW-1-fix` (here).
- Wave 1 note 2 (the seams test pinned legacy members one by one): done in wave 2 by `P-UI-HUD-SEAMS-1-fix`. The
  wave 1 minor about `HudPulses` restoring a start position is covered here by `P-UI-ABILITY-MOTION-1`.
- Wave 2 note 1 (a), the two blind allocation tests of `AbilityRowEditModeTests`: `P-UI-QUEST-OFFER-1` moves both to
  the working meter (both green on the base).
- Wave 2 note 1 (b), the caption "Nearby quest actions · n" built every frame and a case no rig built:
  `P-UI-ABILITY-ROW-1-fix` builds the case in a rig, tests that frame, caches the caption and replaces four boxing
  `Nullable` comparisons with `SameHandle`.
- Wave 2 note 2, item 1 (the hand-built Wish tile): `P-UI-WISH-TILE-1` builds it on `AbilityTile` with a cast bar.
- Wave 2 note 2, items 2 and 3 (two pulse owners, two reduced-motion reads, a comment at
  `HudLayout.MinimapBarClearance`): `P-UI-ABILITY-MOTION-1` drives every press pulse through `HudPulses`, deletes
  the old list and the second reduced-motion read, and says in `HudLayout.cs` that 142 is legacy's minimap rule and
  the row yields to the minimap. The value is unchanged.
- Wave 2 minors: the narrow pair lane is taken (`P-UI-PAIR-LANE-NARROW-1`); the see-through and focusable empty
  tiles wait for `P-UI-TILE-PLATES-1` (wave 4).
- Wave 2 dropped guarantee (text-size inventory coverage): restored in wave 4 by `P-UI-TEXT-FIT-1`. The four
  corrected DEVIATIONS rows of the wave 2 addendum travel in this branch; the toast row has the correction above.

## Dropped, parked and still to come

- Dropped: `P-UI-TOAST-TONES-1` (red refusal toasts). Its drafter read legacy: the server's refusals are shown
  plain; the red tone and the 4 to 8 s hold are only for the client's own join, connection and menu errors
  (`packages/client/src/main.ts`), which Unity does not show as toasts. Making refusals red would add a deviation.
  The spec is kept in the lane's files as dropped.
- Parked for the owner: `P-UI-QUEST-REWARD-PROMPT-1`. The reward choice would wait for F instead of opening by
  itself, with digits 1 to 3 inside its window. It changes a core flow and binds digit keys; the lane lead asked in
  the lane's chat and will not build it without his word. Its old spec no longer fits the tree: it contradicts the
  caption order that `P-UI-QUEST-OFFER-1` landed and would amend nine existing tests.
- Not in this hand-off, in work as wave 4: `P-UI-CONTROLS-LINE-1`, `P-UI-TILE-PLATES-1`, `P-UI-TEXT-FIT-1`,
  `P-UI-LEFT-EDGE-1`, `P-UI-PLAYER-WORDS-2`, `P-UI-WORLD-NAMES-1` and `-2` (specs written and under check), and
  three that the Web round above asked for and that have no spec yet: `P-UI-TABMAP-LABELS-1`,
  `P-UI-JOURNAL-DETAILS-1`, `P-UI-QUEST-ACTION-PROMPT-1`.
- A flaky test that is not the lane's, already reported to the coordinator: the dotnet test
  `AllocationProbeTests.PlainDecodeAllocatesExactlyTheSnapshotItsSelfAndOneActorArrayInEveryRun(68)` (2808.032 for
  2808, run 0). It failed the lane base's gate at `6f84d6af9` and the first two of three runs on `55dd593be`: three
  of the lane base's last six gate runs stopped on it. Its mechanism differs from the meter's
  (`GC.GetAllocatedBytesForCurrentThread` is per thread). The coordinator's `P-TEST-HARDEN-1d` is on `coord/int-86`;
  the lane's gates ran without it.

## Not established

- No full gate was run by the lane; the Rust, Postgres and browser-proof suites did not run for this head. The gate
  that counts is the coordinator's on the merged tree.
- Nothing was measured in a player build; every allocation claim is `AllocationMeter` in the editor. The frame cost
  of the new per-frame ticks (action row, owner vitals, offer card) was not measured.
- The look was judged by the lane lead from the stills of one Web round (above): one window size, text scale 1,
  nothing in motion. No reviewer judged a look. Not seen at all: the run of the Wish cast bar, the slide of the
  health fill between two values, a health bar at the screen edge while the map is open, the narrow pair lane, the
  journal of a quest that is ready to turn in.
- The first `Recorder.Get` of an editor session under the meter's new read order is unmeasured, and
  `TheWarmUpRunsTheActionOnceMoreFirst` can still read 21 or 28 if all three windows of a clean action err.
- Every reviewer relied on the gate log of its own head and none read the oracle tests line by line. Each packet's
  quick gate ran on the tree it forked from, not on the lane base at merge time.
- `P-UI-JOURNAL-POLISH-1`: per-frame allocation of the open journal is unmeasured; a server that never answers an
  accept leaves "ACCEPTING…" until the next quest view, session or timeline; the distance hint assumes `Session.Me`
  is in world units.
- `P-UI-VITALS-EASE-1`: the tick before the `_dirty` gate is not exercised in play; the ghost's colour and contrast
  are unjudged; the training and spectate bind paths were not read.
- `P-UI-TABMAP-MARKS-1`: no capture tool was run; zone names grow from 10 to 14 units at the largest scale and sit
  over contacts. `P-UI-TABMAP-FLOW-1-fix`: only an EditMode rig of the HUD frame order was run, not play.
- `P-UI-QUEST-OFFER-1`: the owner's approval of the three keys; fade timing with motion enabled (tests run with
  reduced motion on); two accepts within one second from journal and card; placement at three window sizes; fit at
  text scale 1.5.
- `P-UI-ALLOC-METER-2`: one sample per allocation holds on 6000.0.85f2 only. `P-UI-ABILITY-ROW-1-fix`: how often the
  server re-sends the quests state is unmeasured.
- `P-UI-MAP-OPEN-WORLD-1`: ghosts fading when the map opens keep their overhead drawing up to .26 s; a map closed
  from `LateUpdate` returns one frame late; a name may return in the other palette once. These are accepted and
  untested.
- `P-UI-WISH-TILE-1`: for one snapshot (0 charges, `ReadyAt` passed) the ready pulse, which still needs `Charges >
  0`, and the tile can differ. `P-UI-ABILITY-MOTION-1`: after a weapon swap during continuous attacking the new bar
  can start partly full.
- `P-UI-PLAYER-WORDS-1`: player lines stay unsorted. `P-UI-ZONE-NAMES-1`: in a real match names will usually leave
  the spot they had before; a name may leave its zone; the planning spawn buttons are not obstacles.
- `P-UI-HEADING-GAPS-1`: a planning phase over 99 seconds; the 640 x 480 planning screen above text scale 1; dynamic
  display text such as quest names in the reward title (the Web player applying the pair table is settled by the Web
  round). `P-UI-PAIR-LANE-NARROW-1`: the look at the .6 scale and the fitted TMP sizes.
- Two sources disagree on one point: the pipeline result of `P-UI-ABILITY-ROW-1-fix` names oracle head `885b5d3fd`,
  and `packet/P-UI-ABILITY-ROW-1-fix` is at `27b911a43`.

## Risks

- Shared files: `MatchHud.QuestRewards.cs` (`P-UI-ABILITY-ROW-1-fix`, `P-UI-QUEST-OFFER-1`,
  `P-UI-PAIR-LANE-NARROW-1`), `MatchHud.ActionBar.cs` and `AbilityTile.cs` (`P-UI-WISH-TILE-1`,
  `P-UI-ABILITY-MOTION-1`), `MatchHud.cs` and `TacticalMapController.cs` (three packets each),
  `AbilityRowEditModeTests.cs`, `PlayerFlowsEditModeTests.cs` and `MatchHudWishBarrierEditModeTests.cs`. A
  coordinator packet that edits them will conflict.
- The artefact flag: the gate on the merged tree fails without `--allow-artefacts` because of
  `CinzelDecorative.asset`.
- The keys: F, Enter and Escape are not in the owner's control mapping, and `MatchClient.cs` changed by one line
  (with nothing to pick up, F calls `Hud?.TryInteract()`). Merging before the owner has seen them puts them in play.
- `P-UI-QUEST-OFFER-1`: ACCEPT in the journal and in the card within one second can send two accepts; the server
  answers the second with "You already accepted that quest". `HudRegions.OfferMinHeight` (96) wins: with less than
  96 units between the tracker and the floor the card covers the champion panel or the readout strip.
- `MatchHud.cs` has 572 lines against the limit of 600 that `hud-seams.test.cjs` pins; 28 more lines turn it red.
- `P-UI-PAIR-LANE-NARROW-1` adds a subscription to the stage scaler that is never removed (the scaler lives as long
  as the HUD). `P-UI-VITALS-EASE-1`: `OwnerVitals()` caches the rects it first sees.
- `P-UI-MAP-OPEN-WORLD-1`: in toggle mode a player loses bars and names until the key is pressed again (in the row
  the owner signs).
- The Tab map's larger text is cut at the map's right and top edges and prints over neighbouring text more
  visibly than before; a zone name was seen over a camp mark once ("Seen in a running build", item 1). Fixed in
  wave 4, not here.
- The lane's allocation proofs hold for editor 6000.0.85f2 only, and two flaky tests remain in the gate: the meter's
  own (rare) and the decoder probe (the coordinator's).

Merge order on the lane branch since the wave 2 hand-off (first-parent, oldest first): `5c0c4efee`, `741c6f50a`,
`ec8450339`, `804fa68fd`, then the records commit `6090bfe9b` (the addendum to the wave 2 hand-off), `632ecb349`,
`2844a831e`, `daa456b2a`, `f9d64223b`, `00863790f`, `6f84d6af9`, `4bc0b4c35`, `4102837d4` (the merge of the
integration branch), `6a6b29649`, `a28952b5e`, `bab8f6f2b`, `55dd593be`. The fifteen packets follow, in that order.
Each block is the implementer's ten-line report as the pipeline result holds it, re-wrapped; each oracle branch is
`packet/<ID>` and each agent branch `agent/sonnet/<ID>`.

## P-UI-JOURNAL-POLISH-1

```
PACKET:          P-UI-JOURNAL-POLISH-1 — serves not in the plan (owner direction 2026-11-22, quest UI polish;
                 lane base-3)
BRANCH:          agent/sonnet/P-UI-JOURNAL-POLISH-1 @ dccfb6736
GATE:            PASS (quick, --oracle packet/P-UI-JOURNAL-POLISH-1 --base lane/ui-rework/base-3; EditMode
                 2131/2131)
CHANGED:         client-unity/RoguehallaNet/Net/Ui/QuestJournalCopy.cs;
                 client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/QuestJournalCopy.cs (synced);
                 client-unity/UnityProject/Assets/Scripts/UI/QuestJournalView.cs
D-RULES:         D1 presentation only (copy and emphasis; every fact shown is delivered); D9 untouched (hint
                 distance decides nothing; server CanAccept stays the only gate)
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: per-frame allocation of the open journal is not measured (no test does); the visual look of the
                 larger status line and hint is unchecked
RISKS:           a server that never answers an accept leaves a disabled "ACCEPTING…" until the next quest view,
                 session or timeline; row stripe is gone on purpose
```
Oracle head `e32673f53`; agent head `dccfb6736`; merge `5c0c4efee`; own quick gate EditMode 2131.

Review: LAND, no notes. Minor, not notes: the status colour keeps its last value with no selection; `RenderHint`
never sees the real distance; a redundant first clause in the status comparison; the Brief field is hidden through
its parent object. Earlier attempts and gate reruns: none recorded.

## P-UI-VITALS-EASE-1

```
PACKET:          P-UI-VITALS-EASE-1 — not in the plan: owner direction 2026-11-22 (UI rework lane, fluid front end)
BRANCH:          agent/sonnet/P-UI-VITALS-EASE-1 @ 09d7d01ec
GATE:            PASS (quick, --oracle packet/P-UI-VITALS-EASE-1 --base lane/ui-rework/base-3); EditMode 2135/2135;
                 BarEaseTests 8/8; the five packet Unity classes 36/36
CHANGED:         client-unity/RoguehallaNet/Net/Ui/BarEase.cs (+ synced copy under
                 UnityProject/Assets/RoguehallaNet/Net/Ui/);
                 UnityProject/Assets/Scripts/Presentation/OwnerVitalFills.cs; UI/MatchHud.Vitals.cs; UI/MatchHud.cs
                 (four edits); UI/MatchHud.Legacy.cs (CreatePlate: HealthGhost image). No oracle file touched.
D-RULES:         D1: loose; legacy has a 100 ms linear width transition and no ghost, so this differs. D2 n/a. D5/D6
                 none. D9 untouched (presentation only).
DEVIATIONS:      proposed DEV-<n>: health and barrier bars ease at a constant rate (fall 6, other moves 3 bar-widths
                 per second) and lost health lingers in a ghost fill; legacy has a 100 ms linear width transition
                 and no ghost.
SHARED RECORDS:  none
LEGACY DOUBTS:   OwnerVitalBars.Percent divides by max(MaxHp, hp + barrier), which differs from legacy's hud.ts
                 formula. Left unchanged, as the packet says.
NOT ESTABLISHED: Not looked at in a rendered play session. The tick-before-_dirty-gate timing is not exercised in
                 play. The allocation-free steady frame is proven only by AllocationMeter in EditMode.
RISKS:           OwnerVitals() is built lazily and caches the rects it first sees. A test or caller that swaps
                 _healthBarFill after the first tick would keep writing the old rects. The oracle test that does
                 this passed.
```
Oracle head `a10c7ef0c`; agent head `09d7d01ec`; merge `741c6f50a`; own quick gate EditMode 2135.

The legacy doubt in the block (`OwnerVitalBars.Percent` divides by max(MaxHp, hp + barrier), which differs from the
formula of legacy `hud.ts`) is left unchanged on purpose and is the coordinator's to log.

Review: LAND, no notes. Minor, not notes: `OwnerVitals()` caches the rects it first sees; the ghost image is built
in three statements. Earlier attempts and gate reruns: none recorded.

## P-UI-TABMAP-MARKS-1

```
PACKET:          P-UI-TABMAP-MARKS-1 — not in the plan: owner direction 2026-11-22 (UI rework lane, "When tab is
                 clicked map layout is weird.")
BRANCH:          agent/sonnet/P-UI-TABMAP-MARKS-1 @ a14936744
GATE:            PASS (quick, --oracle packet/P-UI-TABMAP-MARKS-1 --base lane/ui-rework/base-3); EditMode 2150/2150;
                 MapMarkScaleTests 4/4; the 8 pinned Unity classes 110/110
CHANGED:         client-unity/RoguehallaNet/Net/Ui/MapMarkScale.cs (+ synced copy);
                 UnityProject/Assets/Scripts/UI/TacticalMapSurface.cs, TacticalMapMarks.cs, TacticalMapController.cs
D-RULES:         D1 presentation only, loose proof per packet section 3; D2 n/a; D5/D6/D9 none
DEVIATIONS:      proposed DEV-<n>: Tab map marks grow with the map from 480 to 672 units and never shrink; mark text
                 has a 10 px floor (legacy: glyph .55-1.25 of the map over 640, 9 px floor)
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: how the scaled marks look in a real render; no capture tools were run
RISKS:           zone names grow with the layer (10 units become 14 at k = 1.4) and sit over contacts
```
Oracle head `40dcf2314`; agent head `a14936744`; merge `ec8450339`; own quick gate EditMode 2150.

Review: LAND, no notes. Minor, not notes: formatting of the `Layer` signature line; `SetMarkScale` returns before
storing when `_marks` is null; `FollowLayer` rewrites anchors on every zone-label refresh. Earlier attempts and gate
reruns: none recorded.

## P-UI-TABMAP-FLOW-1-fix

```
PACKET:          P-UI-TABMAP-FLOW-1-fix — serves note 1 of the wave 1 merge review (UI rework lane, not in
                 ORDER_OF_OPERATIONS)
BRANCH:          agent/sonnet/P-UI-TABMAP-FLOW-1-fix @ 4e1f9346a
GATE:            PASS (quick, --base lane/ui-rework/base-3; EditMode 2173/2173)
CHANGED:         client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs
                 (RefreshMinimapVisibility only)
D-RULES:         D1 n/a (presentation); D2 n/a; D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: the real HUD frame order was not run in play; only the EditMode rig that imitates it. The big-map
                 edge was not re-read.
RISKS:           none (one extra minimap paint per show, never per frame)
```
Oracle head `44654a9d9`; agent head `4e1f9346a`; merge `804fa68fd`; own quick gate EditMode 2173.

Review: LAND, no notes. Minor, not notes: a comment at `TacticalMapController.cs:386` could say the next Tick
repaints on purpose; `FollowDrawn` follows the big map's marker when the big map is open. Earlier attempts and gate
reruns: none recorded.

## P-UI-QUEST-OFFER-1

```
PACKET:          P-UI-QUEST-OFFER-1 — not in the plan: owner direction 2026-11-22 (UI rework lane, "Quest UI could
                 be perfected")
BRANCH:          agent/sonnet/P-UI-QUEST-OFFER-1 @ 0bd2e4f0f
GATE:            PASS (quick, --oracle packet/P-UI-QUEST-OFFER-1 --base lane/ui-rework/base-3). EditMode 2188/2188.
                 QuestOffer dotnet tests 6/6. QuestOfferEditModeTests 20/20. Existing classes run alongside it:
                 155/155. hud-seams.test.cjs 13/13.
CHANGED:         client-unity/RoguehallaNet/Net/Ui/QuestOffer.cs and its synced copy under
                 UnityProject/Assets/RoguehallaNet; UI/QuestOfferCard.cs; UI/MatchHud.QuestRewards.cs;
                 UI/MatchHud.Features.cs (the two ReadUiInput branches); Net/MatchClient.cs (the F line)
D-RULES:         D1: presentation and input routing only. Legacy questDialog keys F, Enter and Escape are
                 mirrored. Digit 1 is left out. D2 n/a. D5/D6 none. D9 untouched: accepting is the existing
                 QuestAccept command.
DEVIATIONS:      proposed DEV: the card accepts with Enter and NumpadEnter whether or not it has focus (legacy
                 needed dialog focus). Digit 1 and Numpad1 do not accept (digits bind nothing outside the chest
                 picker). The card never blocks play.
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: Owner approval of the three keys (F talks, Enter accepts, Escape closes), which are not in
                 P-QUEST25-CONTROLS. No look check on a real render at the three windows. Fade timing with motion
                 enabled was not exercised (tests run with ReducedOverride on). The second-accept race (journal and
                 card within one second) is untested.
RISKS:           1) F talking, Enter accepting and Escape closing are not in the owner's control mapping. 2) The
                 journal's Accept and the card's Accept guard separately, so pressing both within a second can send
                 two accepts. The server answers the second with a private refusal notice, "You already accepted
                 that quest". 3) HudRegions.OfferMinHeight (96, outside this scope) wins: with under 96 units
                 between the tracker and the floor the card covers the champion panel or the readout strip. 4) The
                 launcher had no caption label before (the spec's defect note), so I built a HudUi.Label under it.
                 The old captions ("Choose quest reward", "Nearby quest actions · n", "Return to turn in your
                 reward") are now drawn for the first time. 5) QuestPresentation.HasBoardRows now uses an index loop
                 (no per-frame enumerator allocation). IsPending and FindRow still use foreach, off the steady path.
                 6) Both StormInk prop assets were restored after the gate and git status is clean.
```
Oracle head `4af4fb332`; agent head `0bd2e4f0f`; merge `632ecb349`; own quick gate EditMode 2188.

Review: LAND, no notes. Minor, not notes: the Escape branch does not check the Hub-panel Escape of the same frame;
`QuestOfferCard.Tick` keeps adding to `_lost` while closed; `_promptOffer` is cleared on two paths that could be
one. Earlier attempts and gate reruns: none recorded.

## P-UI-ALLOC-METER-2

```
PACKET:          P-UI-ALLOC-METER-2 — serves a coordinator request (not in ORDER_OF_OPERATIONS); owner direction
                 2026-11-22 UI optimization sweep
BRANCH:          agent/sonnet/P-UI-ALLOC-METER-2 @ 506d45fbf
GATE:            PASS (quick, --oracle packet/P-UI-ALLOC-METER-2 --base lane/ui-rework/base-3); EditMode 2202/2202.
                 The five named classes also ran alone: 87/87.
CHANGED:         client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs
D-RULES:         D1 test support only; D2 n/a; D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  proposed one sentence for COMMON.md: `AllocationMeter.AssertAtMost` exists and counts GC.Alloc
                 samples (allocations), not bytes; a byte budget has no conversion, so run `Samples` once on the
                 accepted code and use that count as the limit.
LEGACY DOUBTS:   none
NOT ESTABLISHED: the one-sample-per-allocation reading holds only for editor 6000.0.85f2. The older flake on the
                 10,000-call clean cell was not seen in these runs, so it is not shown to be gone.
RISKS:           none
```
Oracle head `54bba686d`; agent head `506d45fbf`; merge `2844a831e`; own quick gate EditMode 2202.

The block proposes one sentence for the lane's own `specs/COMMON.md`, not for a repository file.

Review: LAND, no notes. Minor, not notes: a doc comment points at a packet file that may be archived; `AssertAtMost`
runs about 60 control calls on every call. Earlier attempts and gate reruns: none recorded; its flaky test is under
"The allocation meter".

## P-UI-ABILITY-ROW-1-fix

```
PACKET:          P-UI-ABILITY-ROW-1-fix — not in ORDER_OF_OPERATIONS (note 1 of the wave 2 merge review)
BRANCH:          agent/sonnet/P-UI-ABILITY-ROW-1-fix @ f6e22af49
GATE:            PASS (quick, --base lane/ui-rework/base-3); EditMode 2201/2201; the five named classes 80/80
CHANGED:         client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs
D-RULES:         D1 n/a (no legacy equivalent); D2 n/a; D5/D6/D9 untouched
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: allocation in a player build (the meter counts editor GC.Alloc samples); how often the server
                 re-sends the quests state
RISKS:           none. Added NearbyActionsCaption() with fields _nearbyCaptionCount and _nearbyCaption, and
                 QuestPresentation.SameHandle(QuestHandle?, QuestHandle?). It replaces the four boxing Nullable
                 Equals calls (overlay test, the two in BindQuestRewardActions, WaitingForQuestChoice). The
                 QuestHandle against QuestHandle calls are unchanged.
```
Oracle head `27b911a43`; agent head `f6e22af49`; merge `daa456b2a`; own quick gate EditMode 2201.

The pipeline result file names an oracle head `885b5d3fd`; the branch `packet/P-UI-ABILITY-ROW-1-fix` is at
`27b911a43`, one `brief:` commit later, which records the scratch cause-removal runs.

Review: LAND, no notes. Minor, not notes: two new fields are declared mid-class; `Nullable.Equals(key,
_questSlotKeys[i])` at :411 was left alone; the gate log's `changed=4 files` counts packet and oracle files. Earlier
attempts: none stopped; the oracle check found one problem (the scratch cause-removal edits were not done) and one
fix pass did them. Gate reruns: none recorded.

## P-UI-MAP-OPEN-WORLD-1

```
PACKET:          P-UI-MAP-OPEN-WORLD-1 — serves owner direction 2026-11-22 (UI rework lane, Tab map layout); not in
                 ORDER_OF_OPERATIONS
BRANCH:          agent/sonnet/P-UI-MAP-OPEN-WORLD-1 @ b5cdb8bc5
GATE:            PASS (quick, --base lane/ui-rework/base-3; EditMode 2200/2200; MapOpenWorldEditModeTests,
                 NamePackFix4 and TabMapSheet classes 32/32)
CHANGED:         client-unity/UnityProject/Assets/Scripts/View/ArenaView.MapOpen.cs (fourth root "Camp patience
                 outlines"), Presentation/SpriteActorPresenter.cs (bar loop reads WorldLabelVisibility.Hidden, no
                 line added), Presentation/OverheadIndicators.cs (_hiddenByMap gate at top of Update)
D-RULES:         D1 legacy hides nothing in the world under its map; this lane is past legacy. D2 n/a. D5/D6 none.
                 D9 untouched (drawing flag only).
DEVIATIONS:      proposed DEV-<n>: while the Tab map or planning screen is open the world hides names, camp respawn
                 labels and ward beacons, the roots World markers / World indicators / Quest carrier flags / Camp
                 patience outlines, the overhead cluster (rings, glyphs, bounty, Wish icon, badge, marks, TAXED cue,
                 barrier bubble, Fire roam glow) and the health and barrier bars; legacy hides only the weapon-range
                 guide (planning.css:57-63, RaidScene.ts:1629-1638, DEV-016). Toggle-mode map keeps them hidden
                 until the key is pressed again. Actors and footprints stay under the 86 percent dim.
SHARED RECORDS:  docs/DEVIATIONS.md: the row above (owner signs).
LEGACY DOUBTS:   none
NOT ESTABLISHED: the track outline following HpBack was covered by the test passing; ghosts fading out when the map
                 opens keep their drawing up to .26 s (accepted); a map closed from LateUpdate returns one frame
                 late (accepted); name palette may flip once on return (documented).
RISKS:           none
```
Oracle head `c989ce53c`; agent head `b5cdb8bc5`; merge `f9d64223b`; own quick gate EditMode 2200.

The block cites `RaidScene.ts:1629-1638` for the weapon range guide; DEV-016 itself says `RaidScene.ts:1637-1638`.
Write the row with one of the two on purpose.

Review: LAND, no notes. Minor, not notes: the field `_hiddenByMap` sits between methods; `_hiddenByMap` stays true
after `Release`. Earlier attempts and gate reruns: none recorded.

## P-UI-WISH-TILE-1

```
PACKET:          P-UI-WISH-TILE-1 — serves note 2 item 1 of the wave-2 merge review (Wish tile on AbilityTile);
                 owner direction 2026-11-22 (ability hub). Not in ORDER_OF_OPERATIONS.
BRANCH:          agent/sonnet/P-UI-WISH-TILE-1 @ c4039308f
GATE:            PASS (quick, --oracle packet/P-UI-WISH-TILE-1 --base lane/ui-rework/base-3). The first run
                 failed once on AllocationMeterEditModeTests.ItGivesTheSameAnswerTwentyTimes ("round 1: adding
                 locals is not seen", 2200 of 2201). The class passes alone (6 of 6) and the second gate run
                 was 2201/2201: the meter's self-check looks flaky in the full suite. Nothing of mine touches
                 it. hud-seams.test.cjs 13/13; WishFaceTests and AbilityFaceTests 20/20; the six named Unity
                 classes 92/92.
CHANGED:         the files in changedFiles: AbilityFaces.Wish and WishLabel (library plus synced copy); AbilityTile
                 gained an optional glyph (corner Icon), the Cast bar (CastBar), Show(face, name), the name hidden
                 only while Cooling, and the hint written only when non-null; the hand-built Wish tile, five fields,
                 _wishSummary and WishIconText were removed; WishNameFor, RefreshWishTile and ClearWishTile now
                 drive the tile.
D-RULES:         D1 presentation only; D2 n/a; D5/D6 none; D9 untouched (the tile draws delivered facts and sends
                 nothing).
DEVIATIONS:      proposed ONE DEV row: the Wish tile differs from LEGACY_LAYOUT section 4 in five places: (1) the
                 name is hidden while recharging; (2) the icon is 16 units in a corner (legacy 24 x 24); (3) no
                 blocking-reason line; (4) the ready frame is not faction-coloured (the glow wash still is); (5) a
                 Wish holding a charge shows no countdown for the next one (the hint card keeps it).
SHARED RECORDS:  proposed: docs/DEVIATIONS.md gets the row above (the owner signs). The three oracle-amend commits
                 (HudTextScaleEditModeTests, MatchHudIconPresentationEditModeTests,
                 MatchHudWishBarrierEditModeTests) are for the owner to approve.
LEGACY DOUBTS:   none
NOT ESTABLISHED: the look in a render (16-unit corner icon, two-line "Frost Wall", the frames) has not been seen.
                 WishTileMotion.Observe still needs Charges > 0 for its ready pulse, so the pulse and the tile can
                 differ for one snapshot (a Wish with 0 charges and ReadyAt already passed). The AllocationMeter
                 flake was seen once and not reproduced.
RISKS:           P-UI-ABILITY-MOTION-1 must be rebuilt on this tree (uses _wishAbilityTile.Sweep and CastBar). The
                 AllocationMeter self-check may flake in the full suite.
```
Oracle head `45eaa2e3d`; agent head `c4039308f`; merge `00863790f`; own quick gate EditMode 2201.

The RISKS line about rebuilding `P-UI-ABILITY-MOTION-1` is stale: it landed on this tree. Ready follows the
server: `crates/rh-sim/src/systems/abilities/mod.rs` refuses a cast only while now is before `ready_at` and reads
no charge count. The lane lead first ruled that Ready should need a charge; the spec's reviser read the server and
kept its rule.

Review: LAND, no notes. Minor, not notes: `RefreshWishTile` has an unused `theme` parameter; quest tiles build a
`Cast` child that is never enabled; the gate log prints a non-fatal `fatal: Needed a single revision`. Earlier
attempts: none. Gate: the first run failed once on `AllocationMeterEditModeTests.ItGivesTheSameAnswerTwentyTimes`
("round 1: adding locals is not seen", 2200 of 2201); that class passed alone and the second run was 2201 of 2201.

## P-UI-PLAYER-WORDS-1

```
PACKET:          P-UI-PLAYER-WORDS-1 — not in ORDER_OF_OPERATIONS (owner direction 2026-11-22, UI rework lane)
BRANCH:          agent/sonnet/P-UI-PLAYER-WORDS-1 @ e48da6eda
GATE:            PASS (quick, --oracle packet/P-UI-PLAYER-WORDS-1 --base lane/ui-rework/base-3; EditMode 2206/2206)
CHANGED:         client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs;
                 client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/PlayerWords.cs (synced copy);
                 client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs (PresentTactics text
                 block only)
D-RULES:         D1 Unity-only text, outside parity; D2 n/a; D5/D6/D9 none
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: how the list looks on screen (judged in the lane's next Web round); player lines remain unsorted,
                 in the order met.
RISKS:           none
```
Oracle head `2658f39ca`; agent head `e48da6eda`; merge `6f84d6af9`; own quick gate EditMode 2206.

Review: LAND, no notes. Minor, not notes: `TacticalMapController.cs:546` still resolves the faction name for
non-players; `PlayerWords.AppendContactRow` has no guard for an empty word. Earlier attempts and gate reruns: none
recorded.

## P-UI-ZONE-NAMES-1

```
PACKET:          P-UI-ZONE-NAMES-1 — not in the plan: owner direction 2026-11-22 (Tab map layout)
BRANCH:          agent/sonnet/P-UI-ZONE-NAMES-1 @ e26cce21c
GATE:            PASS (quick, --oracle packet/P-UI-ZONE-NAMES-1 --base lane/ui-rework/base-3); EditMode 2201/2201;
                 MapLabelPlacementTests 6/6; ZoneNamesEditModeTests and the eight pinned existing classes green
CHANGED:         client-unity/RoguehallaNet/Net/Ui/MapLabelPlacement.cs (+ synced copy),
                 client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs,
                 client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.ZoneLabels.cs
D-RULES:         D1 loose proof: six fixed spots instead of legacy placeLabels rings; D2 n/a; D5/D6 none; D9
                 untouched (presentation only)
DEVIATIONS:      proposed DEV-<n>: legacy places zone names with a larger rule that keeps them inside the zone;
                 Unity picks one of six fixed spots around the zone icon that overlaps no camp, ward, chest,
                 delivered point or map edge, and may leave the zone
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: how it looks (the lane's next Web round judges it); in a real match names will usually leave spot 0
                 (givers lie 30 to 50 layer units from the icon); a name may leave its zone; the planning spawn
                 buttons are not obstacles; after a text-scale change names are re-placed at the next Present; a
                 9-unit text measured on a label MythicTextScale shrank gives a slightly smaller box; whether the
                 rect event fires between SetMarkScale's single anchor writes
RISKS:           the zone-label creation loop keeps its old quirk (label index follows region order only when
                 earlier zones are found); no .meta files were needed (stubs already had them)
```
Oracle head `4d22ec632`; agent head `e26cce21c`; merge `4bc0b4c35`; own quick gate EditMode 2201.

Review: LAND, no notes. Minor, not notes: `Present` builds the mark list twice when the marks changed; the
no-valid-spot fallback keeps the old offset but stores spot 0; a chest-seconds row adds two boxes at one centre;
`_zoneMarks` lists are pre-sized at 64. Earlier attempts and gate reruns: none recorded.

## P-UI-HEADING-GAPS-1

```
PACKET:          P-UI-HEADING-GAPS-1 — UI rework lane (headings in the display font keep the gap between their
                 words); not a numbered ORDER_OF_OPERATIONS item
BRANCH:          agent/sonnet/P-UI-HEADING-GAPS-1 @ e0ad4bf1d
GATE:            PASS (quick, --oracle packet/P-UI-HEADING-GAPS-1 --base lane/ui-rework/base-3 --allow-artefacts).
                 Run 1 of up to 4 printed "GATE PASS (quick)"; log
                 .agent-runs/gate/20261123T041618-agent-sonnet-P-UI-HEADING-GAPS-1-quick.log. EditMode: total 2239,
                 passed 2239, failed 0 (headless 2237; graphics 2). The AllocationMeterCountEditModeTests
                 intermittent did not occur, so no re-runs. No Rust crate changed, so only fmt and area checks ran.
CHANGED:         client-unity/UnityProject/Assets/Resources/Showcase/CinzelDecorative.asset (15 kern records, the
                 previous implementer's commit e0ad4bf1d). This pass changed nothing.
D-RULES:         presentation only; no parity, wire, D5/D6/D9 impact
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: The gate runs with --allow-artefacts because the Cinzel asset is on the gate's artefact list by
                 name. This is the packet's intended change. No visual check of the headings in a render was made in
                 this pass. The AllocationMeter intermittent was not seen, so this run says nothing about its fix.
RISKS:           none. Worktree restored: the two StormInk prop assets were reverted after the run, git status is
                 clean, no Unity lockfile.
```
Oracle head `35a365846`; agent head `e0ad4bf1d`; merge `6a6b29649`; own quick gate EditMode 2239.

The reviewer asked that this hand-off say the merged-tree gate needs `--allow-artefacts` (the summary does). The
result file has no oracle head for this packet; `packet/P-UI-HEADING-GAPS-1` is at `35a365846` in git.

Review: LAND, no notes. Minor, not notes: the gate log's `changed=4 files` counts brief, oracle and asset. Earlier
attempts: one, stopped after two red gate runs, both only on `CountAndAnswerAgreeTwentyTimes` (rounds 12 and 3); the
change was not touched. The third run was green, 2239 of 2239.

## P-UI-PAIR-LANE-NARROW-1

```
PACKET:          P-UI-PAIR-LANE-NARROW-1 — not in ORDER_OF_OPERATIONS (reviewer-opus merge review of UI wave 2;
                 owner direction 2026-11-22 on the ability hub)
BRANCH:          agent/sonnet/P-UI-PAIR-LANE-NARROW-1 @ b8b60ef1d
GATE:            PASS (quick, --oracle packet/P-UI-PAIR-LANE-NARROW-1 --base lane/ui-rework/base-3), first run,
                 EditMode 2248/2248, log 20261123T042427-agent-sonnet-P-UI-PAIR-LANE-NARROW-1-quick.log
CHANGED:         client-unity/RoguehallaNet/Net/Ui/HudRegions.cs;
                 client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/HudRegions.cs (synced copy);
                 client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs
D-RULES:         D1 presentation only; D2 n/a; D5/D6/D9 none. No key changed.
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: the look at the .6 scale (next Web round); TMP fitted sizes.
RISKS:           (1) Scope: besides UpdatePromptLane and the field, I added a second field `_promptLaneHooked`, a
                 method `FollowRowScale`, and a one-time subscription to `_stageScaler.ViewportChanged` inside
                 UpdatePromptLane, all in MatchHud.QuestRewards.cs. Reason: the existing
                 AbilityRowEditModeTests.NothingFloatsAboveTheRow (not amended) builds the rig at a narrow default
                 viewport (row scale .6 at the first refresh), then calls LayoutFull to 1280x800 with no Frame.
                 Without the hook the box stays at the .6 place (measured left 550, expected 490) and that test is
                 red. The packet text ("the box follows on the next Frame") did not foresee it. The scaler lives as
                 long as the HUD, so the handler is never left on an old session, but it is not unsubscribed
                 (MatchHud.cs is out of scope). Reviewer may want to move the hook into MatchHud.cs / OnDestroy in a
                 follow-up. (2) No flaky test hit; gate passed first run.
```
Oracle head `598abee5f`; agent head `b8b60ef1d`; merge `a28952b5e`; own quick gate EditMode 2248.

Beyond its named scope the implementer added a field (`_promptLaneHooked`), a small method (`FollowRowScale`) and a
one-time subscription to the stage scaler's `ViewportChanged` event inside `UpdatePromptLane`, because the existing
`AbilityRowEditModeTests.NothingFloatsAboveTheRow` resizes the stage with no frame after it. The handler is never
removed; the scaler lives as long as the HUD. The reviewer accepted it as a minor.

Review: LAND, no notes. Minor, not notes: a second field and a method beyond the packet's named scope; the
`ViewportChanged` subscription is lazy and never removed; `UpdatePromptLane` builds a `HudRegion` struct per
placement. Earlier attempts: none. Gate: first run, green.

## P-UI-ALLOC-METER-3

```
PACKET:          P-UI-ALLOC-METER-3 — not in the plan (owner direction 2026-11-22, UI optimization sweep)
BRANCH:          agent/sonnet/P-UI-ALLOC-METER-3 @ 07e6e350c
GATE:            PASS (quick, --base lane/ui-rework/base-3 --allow-artefacts) on the first run; EditMode 2276/2276;
                 log .agent-runs/gate/20261123T043342-agent-sonnet-P-UI-ALLOC-METER-3-quick.log. The flaky
                 AllocationProbeTests did not fail. The five pre-gate runs of the three meter classes (Noise, Count,
                 AllocationMeter): 25/25 green in every run.
CHANGED:         client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs
D-RULES:         D1: test support only, no legacy counterpart. D2 n/a. D5/D6/D9 none.
DEVIATIONS:      none
SHARED RECORDS:  proposed edit to COMMON.md: "the helper runs the action 2 to 4 times `calls`; a stateful rig
                 advances up to four times as far"
LEGACY DOUBTS:   none
NOT ESTABLISHED: the first Recorder.Get of an editor session under the new read order is unmeasured; the noise test
                 (real thread noise) keeps its own small false-red chance (about 1 run in 10,000 per the packet).
                 Rare allocations (not in every window) are by design not reported.
RISKS:           none
```
Oracle head `9bfd4d55f`; agent head `07e6e350c`; merge `bab8f6f2b`; own quick gate EditMode 2276.

The proposed sentence is for the lane's own `specs/COMMON.md`, not for a repository file.

Review: LAND, no notes. Minor, not notes: the class summary sentence about the recorder is less clear; the first
`Recorder.Get` of an editor session was never measured. Earlier attempts: none. Gate: first run, green; the three
meter classes ran 25 of 25 in each of five runs before it.

## P-UI-ABILITY-MOTION-1

```
PACKET:          P-UI-ABILITY-MOTION-1 — not in the plan: owner direction 2026-11-22 (fluid front end, ability hub)
                 plus reviewer-opus note 2 of wave 2
BRANCH:          agent/sonnet/P-UI-ABILITY-MOTION-1 @ c5ab9e6f0
GATE:            PASS (quick, --oracle packet/P-UI-ABILITY-MOTION-1 --base lane/ui-rework/base-3). Run 1 (log
                 20261123T043049-...-quick.log) red only on
                 AllocationMeterCountEditModeTests.CountAndAnswerAgreeTwentyTimes (21 vs 20, known flake). Run 2
                 (20261123T043509-...-quick.log) red only on the same test (23 vs 20). Run 3
                 (20261123T043916-...-quick.log) printed GATE PASS, EditMode 2252/2252. Before the gate: the 6
                 packet classes 61/61 green and hud-seams.test.cjs 14/14.
CHANGED:         UI/AbilityTile.cs (SweepAt, CastAt), UI/UiMotion.cs (HudPulses Last/Origin follow rule),
                 UI/MatchHud.cs (TickActionRow call, ResetActionClock x3), UI/MatchHud.ActionBar.cs (HudClock,
                 HudSeconds, ActionNowMs, TickActionRow, ResetActionClock, AttackBar per slot, WriteAttackBars,
                 wash-only TickWishTileMotion), UI/MatchHud.Pulses.cs (one HudPulses owner), UI/MatchHud.Legacy.cs
                 (_slotSweeps removed, call passes time), UI/HudLayout.cs (comment only)
D-RULES:         D1 presentation; D9 untouched (HudClock capped 150 ms past the newest snapshot, nothing sent or
                 decided). No key bound or changed.
DEVIATIONS:      proposed DEV-<n>: the attack timer is a bar on the weapon in hand only (legacy
                 weaponBar.ts:78-81,207-214 draws a shared wb-sweep wipe in both slots) and the press pop is 8
                 percent (was 10)
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: the look (3-unit attack bar band near the Scaling label, the 8 percent centred pop, the motion) is
                 for the lane's next Web round; frame cost of the new tick not measured; the ready pulse and tile
                 Ready can differ for one snapshot; the attack bar length is the largest-ReadyInMs-since-0 rule, so
                 after a weapon swap during continuous attacking the new bar can start partly full; the steady-tick
                 allocation test passed in all three gate runs.
RISKS:           none beyond the above; the two red gate runs were the known allocation-meter flake only. StormInk
                 props restored, git status clean.
```
Oracle head `e516ce924`; agent head `c5ab9e6f0`; merge `55dd593be`; own quick gate EditMode 2252.

The `HudPulses` Last/Origin follow rule in `UiMotion.cs` answers the wave 1 review's minor about `HudPulses`
restoring a start position.

Review: LAND, no notes. Minor, not notes: a transient reset mid-cooldown blanks the attack bar until the next sweep
refresh; `AbilityTile.SweepAt` also returns when the sweep is not enabled. Earlier attempts: none stopped. Gate:
runs one and two were red only on `CountAndAnswerAgreeTwentyTimes` (21 against 20, then 23 against 20), before the
meter cure was on that branch; run three was green, 2252 of 2252. The change was not touched between runs.

## Minor, not notes

These get no packet (owner direction 2026-11-13).

- P-UI-JOURNAL-POLISH-1: The status colour keeps its last value with no selection.
- P-UI-JOURNAL-POLISH-1: `RenderHint` never sees the real distance.
- P-UI-JOURNAL-POLISH-1: A redundant first clause in the status comparison.
- P-UI-JOURNAL-POLISH-1: The Brief field is hidden through its parent object.
- P-UI-VITALS-EASE-1: `OwnerVitals()` caches the rects it first sees.
- P-UI-VITALS-EASE-1: The ghost image is built in three statements.
- P-UI-TABMAP-MARKS-1: Formatting of the `Layer` signature line.
- P-UI-TABMAP-MARKS-1: `SetMarkScale` returns before storing when `_marks` is null.
- P-UI-TABMAP-MARKS-1: `FollowLayer` rewrites anchors on every zone-label refresh.
- P-UI-TABMAP-FLOW-1-fix: A comment at `TacticalMapController.cs:386` could say the next Tick repaints on purpose.
- P-UI-TABMAP-FLOW-1-fix: `FollowDrawn` follows the big map's marker when the big map is open.
- P-UI-QUEST-OFFER-1: The Escape branch does not check the Hub-panel Escape of the same frame.
- P-UI-QUEST-OFFER-1: `QuestOfferCard.Tick` keeps adding to `_lost` while closed.
- P-UI-QUEST-OFFER-1: `_promptOffer` is cleared on two paths that could be one.
- P-UI-ALLOC-METER-2: A doc comment points at a packet file that may be archived.
- P-UI-ALLOC-METER-2: `AssertAtMost` runs about 60 control calls on every call.
- P-UI-ABILITY-ROW-1-fix: Two new fields are declared mid-class.
- P-UI-ABILITY-ROW-1-fix: `Nullable.Equals(key, _questSlotKeys[i])` at :411 was left alone.
- P-UI-ABILITY-ROW-1-fix: The gate log's `changed=4 files` counts packet and oracle files.
- P-UI-MAP-OPEN-WORLD-1: The field `_hiddenByMap` sits between methods.
- P-UI-MAP-OPEN-WORLD-1: `_hiddenByMap` stays true after `Release`.
- P-UI-WISH-TILE-1: `RefreshWishTile` has an unused `theme` parameter.
- P-UI-WISH-TILE-1: Quest tiles build a `Cast` child that is never enabled.
- P-UI-WISH-TILE-1: The gate log prints a non-fatal `fatal: Needed a single revision`.
- P-UI-PLAYER-WORDS-1: `TacticalMapController.cs:546` still resolves the faction name for non-players.
- P-UI-PLAYER-WORDS-1: `PlayerWords.AppendContactRow` has no guard for an empty word.
- P-UI-ZONE-NAMES-1: `Present` builds the mark list twice when the marks changed.
- P-UI-ZONE-NAMES-1: The no-valid-spot fallback keeps the old offset but stores spot 0.
- P-UI-ZONE-NAMES-1: A chest-seconds row adds two boxes at one centre.
- P-UI-ZONE-NAMES-1: `_zoneMarks` lists are pre-sized at 64.
- P-UI-HEADING-GAPS-1: The gate log's `changed=4 files` counts brief, oracle and asset.
- P-UI-PAIR-LANE-NARROW-1: A second field and a method beyond the packet's named scope.
- P-UI-PAIR-LANE-NARROW-1: The `ViewportChanged` subscription is lazy and never removed.
- P-UI-PAIR-LANE-NARROW-1: `UpdatePromptLane` builds a `HudRegion` struct per placement.
- P-UI-ALLOC-METER-3: The class summary sentence about the recorder is less clear.
- P-UI-ALLOC-METER-3: The first `Recorder.Get` of an editor session was never measured.
- P-UI-ABILITY-MOTION-1: A transient reset mid-cooldown blanks the attack bar until the next sweep refresh.
- P-UI-ABILITY-MOTION-1: `AbilityTile.SweepAt` also returns when the sweep is not enabled.
