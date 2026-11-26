# P-UI-GUARDS-2: journal, trader, map-name and settings guarantees that wave 4 loosened are tested again

- **Serves:** not in the plan: the UI rework lane's own audit of the test expectations that wave 4 amended or weakened
  (2026-11-23, 79 rows, each judged twice; the nine below were judged RESTORE: the product still holds the guarantee and
  no test states it any more).
- **Implementer tier:** implementer-sonnet (tests only: there is nothing to implement while every guard is green; see 1.4)
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (`88bbbdbe7`)
- **D-rules touched:** D1: tests only, no behaviour change, no legacy number, no `DEVIATIONS.md` row · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

1. For each of nine rows a test states the old guarantee again, in the class the row names, with the trigger the row names
   (or a better one that runs the same product path; each such change is explained under "Notes from the oracle author").
2. No product file changes. No existing assertion is removed, widened or made conditional.
3. Each restored test is a STANDING GUARD: green on the base, proved by a MUTATION RUN in a scratch state that is never
   committed (change the product line the guard protects, run the class, see the guard fail ON ITS ASSERTION, put the line
   back). The oracle author records the mutation for each test below ("Results"). A guard with no mutation that turns it red
   proves nothing.
4. If a guard is red on the base, the product does not hold the guarantee: the test is left OUT of the commits and recorded
   under "Red on the base" (code, numbers, product member); the lane lead gives it its own packet.

The rule "every new test is red on the stub" does not apply: every test is green on the base by design.

## 2. Dependencies

none (base `88bbbdbe7`). `AllocationMeter` is not used by this packet.

## 3. Relevant legacy paths

none: Unity presentation guarantees of the lane's own UI; legacy had no such surfaces. Product files read for the rows
(all under `client-unity/UnityProject/Assets/Scripts/UI/`, read-only):

- `QuestJournalView.cs:86-91` (`CreateQuestField`, the PARTICIPANTS field is `_participants`), `:354` (the field's text is `""`
  unless the row has participants), `:564` `ParticipantCopy`.
- `TraderDialogView.cs:109-120` `SetVisible` (opening sets `_listToTop`, `PlaceList(1f)`), `:125-170` `Refresh` (`SameContent`
  `:244-261` ignores merchant positions: a moved merchant rebuilds nothing; a changed offer label does), `:269` `RebuildMerchants`,
  `:294-300` `RebuildChoices` (`ReadList`, `ListChanged`), `:379-407` `ReadList` / `PlaceList`.
- `TacticalMapMarks.ZoneLabels.cs:196-215` (obstacles of the zone-name placement: camp plate `AddBox`, elite diamond, the placed
  text block `_ctRects[i]` at `:213`, the camp clock), `:50` `ZoneLabelRect`.
- `TacticalMapMarks.CampTexts.cs:31` `CampFactsInKey`, `:40` `CampTextRect(int)` (null when no block),
  `TacticalMapMarks.cs:159` (`CampKey.Apply(_camps, CampFactsInKey)` when `Large`), `TacticalMapController.cs:268-269`
  (writes the flag only on a change of `_ckFlag`).
- `ModernSettingsView.cs:59-60` (footer height 62 with a leave action, scroll frame inset `HeaderHeight` top and `10 + footer`
  bottom), `:97` (the "Training walkthrough" row), `:110-126` `ApplyViewport` / `Fit` (card height `min(content, uiH - 60)`),
  `:312-319` `FooterRow` (child of the card, anchored to its bottom, constant height).

## 4. New paths

none (new test methods and helpers inside existing test files).

## 5. Exclusive write scope

All under `client-unity/UnityProject/Assets/Tests/Editor/`: `QuestJournalDetailsEditModeTests.cs`,
`FlowsLeftoversEditModeTests.cs`, `ZoneNamesEditModeTests.cs`, `ZoneNamesRealOrderEditModeTests.cs`,
`SettingsFooterEditModeTests.cs`; and `docs/backlog/packets/P-UI-GUARDS-2.md`. Nothing under `Assets/Scripts/`, nothing under
`client-unity/RoguehallaNet/Net/`. Not in scope: `HudTextScaleEditModeTests.cs`, `HudLegacyLayoutEditModeTests.cs`, every
`CampKey*` test file.

## 6. Contracts and references

Lane rules (`COMMON.md`): tests are a separate assembly (no `InternalsVisibleTo`); a text label never awake measures a tenth of
its size, so a fit test first asserts `activeInHierarchy`; a fixture follows the order of a real match; no `Assert.Multiple`;
restore every static the class's TearDown restores. Do not copy product arithmetic into a test: assert what a player sees (a
rect, a text, a size, an active flag).

## 7. Behaviour to preserve

Everything: no product file changes.

## The rows (the audit's, re-read on the tree)

Rows 1 to 5 and 6 change lines of EXISTING test methods: each is an `oracle-amend:` commit, one per test method (kinds below).
Rows 7 to 9 are NEW test methods in `oracle:` commits.

1. **Row 1** `QuestJournalDetailsEditModeTests.NoHeadingIsTallerThanItsRowAndNothingOverlaps(1f)` (`:263-274`). `LongRow`
   (`:~720`) builds a row with `Participants = Array.Empty` so since P-UI-FLOWS-LEFTOVERS-1 the PARTICIPANTS field is hidden and
   `AssertHeadings` (`:~627`, skips `!field.gameObject.activeSelf`) never measures that heading. Restore: the row gets one
   participant (`new WireQuestParticipant { Id = new RosterId(11), Accepted = true, Progress = 3 }`), so the heading is active and
   `AssertHeadings` checks `GetPreferredValues(...).y <= rect.height + .5` for it. Added assertion: the QuestParticipants field is
   active in the hierarchy (so the guard cannot pass for the old reason). Kind: RE-STATEMENT (plus the precondition assertion).
2. **Row 2** the same method at scale 1.5 (same edit, covered by the same `oracle-amend:` commit as row 1).
3. **Row 3** `QuestJournalDetailsEditModeTests.TheHeadingsAreRightWhenTheJournalOpensAfterDeliveriesWhileHidden` (`:278-288`):
   the delivered row gets a participant before `Open()`; same precondition assertion. Kind: RE-STATEMENT.
4. **Row 4** `FlowsLeftoversEditModeTests.TraderListTopEditModeTests.EveryOpeningStartsAtTheTop` (`:~508-530`): the delivery
   while closed was `rig.Deliver(true, 3.5)` (a moved merchant: `SameContent` ignores positions, so nothing rebuilds). Restore:
   deliver the stat trade with `Offers[0].Label` changed (same merchants), so `Refresh` rebuilds while closed, then the existing
   `AssertTop`. Kind: RE-STATEMENT.
5. **Row 5** `...TraderListTopEditModeTests.ARebuildWhileOpenKeepsThePosition` (`:~555-570`): same change of delivery; both
   assertions (.4 within .02 after `Refresh` and after `LateUpdate`) stay. Kind: RE-STATEMENT.
6. **Row 6** `ZoneNamesRealOrderEditModeTests.TheNamesOfARealMatchAreMeasuredWhenTheMapOpens`,
   `.ThePlanningOrderMeasuresWhenThePanelShows`, `.AMapOpenedBeforeTheFirstFrameGivesTodaysResult` (all through
   `AssertTrueAndClear` -> `AssertClearOfEdgeMarks`, `EdgeMarks :~281-291`). The helper models each camp's amount text as standing
   to the right; the product may place it Below, Above or Left. Restore: `EdgeMarks` takes the camp plates (20.4 square, built
   here) and each camp's text block from `Marks().CampTextRect(i)` where it has a value. The count assertion becomes "four plates
   plus the blocks that exist" and a new assertion that all four camps have a block (key off, so every text is drawn). Kind:
   STRENGTHENING (a measured object replaces a modelled one); one `oracle-amend:` commit for the shared helper, which changes
   the three tests above.
7. **Row 7** NEW `ZoneNamesEditModeTests.TheNamesAreClearOfTheMarksWithTheKeyOn`: the picture (`PictureMapCamps`,
   `BuildController`), `Marks().CampFactsInKey = true` set before `DeliverCamps` and `DeliverGivers`, two frames,
   `SetMarkScale(1)`. Asserts: camps 0 and 2 (Stat 0, a kind that varies) have a text block and camps 1 and 3 (Stat 2, one amount)
   have none (the key is on); every one of the six zone names' `ZoneLabelRect` misses the four plates (20.4 squares), the two
   givers (19.6 squares) and each `CampTextRect(i)` that has a value; HAUNTED and HOLY have left spot 0. No spot-0 claim beyond that.
8. **Row 8** NEW `ZoneNamesRealOrderEditModeTests.TheNamesOfARealMatchAreClearOfTheMarksWithTheKeyOn`: `FindCamps(EdgeCamps)`,
   then the real order (`Real()`), with the flag on from the build (`BuildController` reads a class field `_keyOn`, default
   false, so no existing test changes). Asserts as row 7 on the four edge camps, plus `AssertKeptSizesAreTrue`.
9. **Row 9** NEW `SettingsFooterEditModeTests.AShortWindowKeepsTheFooterInView`: `new Alone(true)`,
   `view.ApplyViewport(new HudViewport(1f, 1280f, 400f))`, `SettingsRig.Update()`. Asserts: card height 340 (.01); Footer is a
   child of the card and 62 high; Footer, Reset and Leave inside the card's rect and at or above its bottom pad of 10; the scroll
   frame's lower edge at or above the footer's top; the viewport about 232 high and the content taller; at
   `verticalNormalizedPosition` 0 the "Training walkthrough" row lies wholly in the viewport. (The existing
   `ModernSettingsLayoutEditModeTests.ShortWindowScrollsTheBodyToBothEnds` is left as it is: its two messages say "footer" for the
   Training row; it is not in the write scope.)

## Existing tests that pin this code (each read; all stay green)

- `QuestJournalDetailsEditModeTests` (whole class): `AssertHeadings` and the heading/overlap rules at scales 1 and 1.5; the
  amended methods are the two above; `:240-257` assert the PARTICIPANTS field and heading hidden for a row with none (unchanged).
- `FlowsLeftoversEditModeTests`: `AHeldQuestWithNoParticipantsShowsNeitherHeadingNorValue` (:~55), `AParticipantShowsHeadingAndLines`
  (:~85, on/off and text only), `TheFieldOrderAndTheStackAreUnchanged` (field heights and 10-unit gaps), `TraderListTopEditModeTests`
  (`AStatListFirstOpensAtTheTop`, `AnotherTradersListStartsAtTheTop`, `ABarterListThatGrowsPastTheViewportStandsAtTheTop`,
  `ALongBarterListFirstOpensAtTheTop`), the two amended methods.
- `TraderRowsRealOrderEditModeTests` (`:191` changed-offer rebuild with the window reopened at the top; `Trade` helper `:375`) and
  `TraderSteadyEditModeTests` (a moved merchant rebuilds nothing, replaced rows still rebuild): not edited, run for safety.
- `ZoneNamesEditModeTests` (8 tests, key off through `BuildController :127`), `ZoneNamesRealOrderEditModeTests` (8 tests, key off
  `:141`), `CampTextEditModeTests` (other camps): the first two are run whole; the third is not touched.
- `SettingsFooterEditModeTests` (footer in view in the 800 rig, tall window, no-leave card, footer contents, Leave/Reset, tab
  cycle) and `ModernSettingsLayoutEditModeTests` (`ShortWindowScrollsTheBodyToBothEnds` at 1280 x 400 proves the last scrolling row
  only): run whole, not edited.

## 8. Acceptance checks

- The five touched classes run whole in Unity EditMode, all green, with the totals recorded below.
- Each new or amended guard has its mutation recorded below, and the tree is clean after each.
- `scripts/gate.sh --quick --oracle packet/P-UI-GUARDS-2 --base lane/ui-rework/base-3` prints `GATE PASS` on the implementer's branch
  (which holds no product change: the implementer verifies the oracle commits compile, run green, and files are unchanged).

## Oracle files (read-only for implementers)

All under `client-unity/UnityProject/Assets/Tests/Editor/`. Five `oracle-amend:` commits (existing test methods, listed for the owner with their kind), one `oracle:` commit.

- `QuestJournalDetailsEditModeTests.cs`: `oracle-amend` (rows 1 and 2, RE-STATEMENT) `NoHeadingIsTallerThanItsRowAndNothingOverlaps(1f)` and `(1.5f)`:
  the row is `LongRowWithParticipant`, the delivery carries `ParticipantRoster`, one added precondition assertion that the PARTICIPANTS field is
  active in the hierarchy (new helper `LongRowWithParticipant`, new static `ParticipantRoster`, `Deliver` takes an optional `roster`); `oracle-amend`
  (row 3, RE-STATEMENT) `TheHeadingsAreRightWhenTheJournalOpensAfterDeliveriesWhileHidden`: same row, roster and precondition.
- `FlowsLeftoversEditModeTests.cs`: `oracle-amend` (row 4, RE-STATEMENT) `TraderListTopEditModeTests.EveryOpeningStartsAtTheTop` (new helper
  `ChangedOffer`); `oracle-amend` (row 5, RE-STATEMENT) `TraderListTopEditModeTests.ARebuildWhileOpenKeepsThePosition`.
- `ZoneNamesRealOrderEditModeTests.cs`: `oracle-amend` (row 6, STRENGTHENING) helpers `EdgeMarks` and `AssertClearOfEdgeMarks`, which change
  `TheNamesOfARealMatchAreMeasuredWhenTheMapOpens`, `ThePlanningOrderMeasuresWhenThePanelShows` and `AMapOpenedBeforeTheFirstFrameGivesTodaysResult`
  (`TextSize` stays, now unused by `EdgeMarks`); `oracle` (row 8, new) `TheNamesOfARealMatchAreClearOfTheMarksWithTheKeyOn` with the field `_keyOn`
  read by `BuildController` (default false, reset in `SetUp`: no other test changes).
- `ZoneNamesEditModeTests.cs`: `oracle` (row 7, new) `ThePicturesNamesAreClearOfTheirMarksWithTheKeyOn`.
- `SettingsFooterEditModeTests.cs`: `oracle` (row 9, new) `AShortWindowKeepsTheFooterInView`.
- `docs/backlog/packets/P-UI-GUARDS-2.md`.

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> QuestJournalDetailsEditModeTests,FlowsLeftoversEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ZoneNamesEditModeTests,ZoneNamesRealOrderEditModeTests,SettingsFooterEditModeTests`

## 10. Risk and suggested model tier

Low: tests only. Sonnet implements nothing; if a guard is red on the base it is recorded and left out.

## 11. Known traps

- A mutation is undone with `git checkout -- <file>` before anything is committed.
- Text labels never active in the hierarchy measure a tenth of their size: assert `activeInHierarchy` before a fit.
- Key on (`CampFactsInKey = true`): the controller writes its own flag only on a change of its `_ckFlag`, already true at
  1280 x 720, so the test's write stays.
- Stat-2 camps with the key on lose their texts (`CampTextRect` null): do not index `CampTextRect(i).Value` blindly.

## Notes from the oracle author

Spec differences (the code decided):

- **The spec's participant needs a roster.** The wire refuses a quest participant that is not on the view's roster
  (`client-unity/RoguehallaNet/Protocol/StateItemsSocial.cs:408-412`, "quest participant"): a row with `Participants = { Id 11 }` and no roster throws
  `EncodeException` in `Deliver` (seen on the first run). The two journal tests therefore deliver `Roster = { 11 "Alice" }` too. The heading is the same
  field; only the value line reads "Alice" where the spec's wording had the fallback "Participant".
- **Rows 1 to 3 gain one precondition assertion** (`QuestParticipants` active in the hierarchy before `AssertHeadings`), so the guard cannot pass again
  because the field is hidden (the original loss).
- **Row 6**: `CampTextRect(i)` reads null for a camp with no block; with the key off all four camps have one, so the helper also asserts four blocks
  and `marks.Count == 4 + blocks` (the spec's count).
- **Row 7 and 8 assertions on spots**: row 7 keeps `ZoneLabelSpot(Haunted/Holy) > 0` (measured green and sensitive to the plate); row 8 does not claim
  `AssertEdgeSpots` (the spec said "not established"). Both add set-up assertions that the key really is on (camps 0 and 2 have a block, 1 and 3 none).
- **Row 9** is a new method in `SettingsFooterEditModeTests` (the spec's first alternative); `ModernSettingsLayoutEditModeTests` is outside the write scope
  and unchanged. The Training walkthrough row is `content.GetChild(childCount - 1)` (the footer is no longer in the content).
- `FlowsLeftoversEditModeTests.TraderListTopEditModeTests` is a separate class from `FlowsLeftoversEditModeTests` in that file: run it by its own name.

Results on the base `88bbbdbe7` (all green with the new tests; run by `unity-class.sh`, Unity 6000.0.85f2):

- `QuestJournalDetailsEditModeTests`, `FlowsLeftoversEditModeTests`, `TraderListTopEditModeTests`: 22 of 22.
- `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`, `SettingsFooterEditModeTests`: 31 of 31.
- Final whole-class run on the committed tree, with the neighbours: see the line "Final run" below.

No scratch catch-and-collect run: nothing here is red on the base, so no test stops early; each test ran to its end green, and the mutation runs
below show each guard's assertion fail. Red on the base: none.

Mutations (each applied alone to the product file, the class run, the file put back with `git checkout --`; `git status` clean afterwards each time):

| Guard | Mutation | Failing message |
|---|---|---|
| Rows 1, 2, 3 (three tests) | `QuestJournalView.CreateQuestField`: for `QuestParticipants` add a `LayoutElement` with `preferredHeight = 2` on the FieldLabel | `QuestParticipants heading needs 24.56 of 2 text scale 1` (1.5: `36.83 of 2`; hidden-delivery test: `36.83 of 2 after a hidden delivery`) |
| Row 4 `EveryOpeningStartsAtTheTop` | `TraderDialogView.SetVisible`: comment out `if (opening) _listToTop = true;` | `the list stands at its top when it opens after a rebuild while closed` (expected 1, was .4); the first failing step is the amended one |
| Row 5 `ARebuildWhileOpenKeepsThePosition` | `TraderDialogView.PlaceList`: `verticalNormalizedPosition = 1f` instead of `_listToTop ? 1f : read` | `after the same trader's offer changed (a rebuild), Refresh` (expected .4, was 1). The old moved-merchant trigger passes under this mutation: the amend is what makes the guard bite |
| Rows 7, 8 plates | `TacticalMapMarks.ZoneLabels.cs:211` remove `AddBox(into, p.x, p.y, box, box)` (the camp plate obstacle) | row 8: `key on: the drawn name Haunted overlaps no camp plate or text` (expected False, was True); row 7: `Holy leaves spot 0 for the givers and plates` (expected > 0, was 0). Eight older tests fail too (they pin spots) |
| Rows 7, 8 key | `TacticalMapMarks.cs:159` `CampKey.Apply(_camps, false)` | both: `set-up: the two Stat-2 camps lose theirs, the key says them` (expected False, was True): the key-on claim is real |
| Row 9 | `ModernSettingsView.cs:60` scroll inset bottom `10f` instead of `10f + _footerHeight` | `the scroll frame ends at or above the footer's top: frame [.. y -330..-36], footer [.. y -330..-268]` (three older footer tests fail too) |
| Row 9 | `ModernSettingsView.cs:314` `FooterRow` `offsetMin.y = -30f` | `the footer is 62 high with a leave action: 102` |

**Not sharp (stated, not hidden):** the text-block clause of rows 6, 7 and 8. Removing the placed text block from the zone-name obstacles
(`TacticalMapMarks.ZoneLabels.cs:213`, `into.Add(_ctRects[i])`) turns NO test red, old or new, in all 18 tests of the two classes: on these scenes the
plates and givers keep every name away from the texts anyway, so the "no name overlaps a text that stands" assertions hold on today's geometry and would
not catch that regression. The plate clause is sharp (above). A scene where a name would otherwise land on a text needs a camp layout designed for it;
that is outside this packet's rows and is left for the lane lead. Row 6's own change (reading the placed block instead of modelling it) is covered by
the green run only: the plate mutation reddens those three tests earlier, at the spot assertion.

Final run, on the tree of the last commit: QuestJournalDetails, QuestJournalView, FlowsLeftovers, TraderListTop, TraderRowsRealOrder, TraderSteady, ZoneNames, ZoneNamesRealOrder, CampText, SettingsFooter and ModernSettingsLayout EditMode classes: 134 of 134 passed, 0 failed. The five product files are untouched (`git diff 88bbbdbe7 --stat -- client-unity/UnityProject/Assets/Scripts` is empty).
