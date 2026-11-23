# P-UI-TABMAP-TEXT-2: the Tab map says each camp bonus once, in the map key

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane): "When tab is clicked map layout is weird"; seen in Web round 3B, pictures 40, c3, 42 and 34.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (tree at launch `2fd46225e` or later)
- **D-rules touched:** D1 legacy draws each camp's reward as an opaque pill (icon and amount, an elite's second "Aug" pill) only when the map is shown as the Tab map (`packages/client/src/ui/planning.ts:176-194` the badge builders, hidden unless the mode is "map" at `:594` and `:691`, `planning.css:231-262`), repeats it in a hover line, and its legend lists the zones only. This packet differs: propose ONE `DEVIATIONS.md` row (the coordinator signs it; text in the design `.agent-runs/lane-ui-rework/understand/design-tabmap-camp-texts.md`, section 6). D2 n/a. D5/D6 none. D9 untouched (drawing only; the wire is unchanged).

## 1. ID and outcome

P-UI-TABMAP-TEXT-2. The Tab map prints a text beside every camp: its amount ("+3%", "+4.5") and, for an elite, a second line "Aug". Picture 40 has about 60 such texts (11 "AUG", about 49 amounts) and they lie on marks (a "+1.5%" on the next camp's gold diamond, two "AUG" on each other, "AUG" and "+3" on a camp icon). `CampTextPlacement` tries four places and falls back to the least overlap, so a crowded corner overlaps by rule.

The cause the placement cannot reach: the amount is a function of the buff kind alone. `crates/rh-sim/src/modifiers/farm.rs` `DEFINITION` has one value per stat and `observe/hud.rs camp_hud_into` adds no tier, zone or elite input, so the nine strings in the pictures (`+3` twice, `+3%` three times, `+0.75%`, `+3.75`, `+4.5`, `+1.5%`) are the whole set, printed up to six times each by rule (`CampBadgesTests.AmountTextIsThePerKillAmount`, `client-unity/RoguehallaNet.Tests/CampBadgesTests.cs:63-73`, pins them). `CampMarks.Glyphs` (`client-unity/RoguehallaNet/Net/Ui/CampMarks.cs:81-115`) sets `AmountText` (:105) and `EliteText` (:107) on the Tab map surface only, from the reward and the elite flag, never from a camp's state.

When this lands, the map stops printing what the key can say once. The MAP KEY (the left rail of the Tab map) says it, from the delivered rows, for the kinds on the map. Where the key cannot show its cells, the map keeps today's texts.

### 1.1 Learn

A new library class `CampAmountKey` (one per large `TacticalMapMarks`) learns each kind's amount from the camp rows on every `Present`, key on or not. A kind is one of nine stat codes in stat order (Offense, Soul Offence, Damage, Attack speed, Life steal, Max health, Defense, Soul Defense, Move speed; `IconKeys` equal `CampBadges.IconKey(0..8)`, `CampBadges.cs:45-46`). In index order a camp with a known `IconKey` and a non-null `AmountText`: the first of its kind marks it seen, stores the text and adds 1 to `Version`; a later camp of the kind whose text differs (ordinal) makes the kind not fixed and adds 1 (once per kind; never fixed again). Other camps are not learned and keep their rows. Members: `Kinds` (9), static `Words` and `IconKeys`, `KindOf(iconKey)` (-1 when unknown or null), `Version`, `Has(kind)`, `IsFixed(kind)`, `CellText(kind)`, `Cell(kind)` (a `CampKeyCell`: `Kind`, `IconKey`, `IconRgb`, `Text`), `Count` (kinds seen), `SlotOf(kind)` (the number of seen kinds below it; -1 when not seen), `KindAt(slot)` (-1 when none), `Apply(List<CampGlyphRow> camps, bool strip)` and `Forget()`.

### 1.2 Strip

`Apply(camps, strip)` with `strip` true (the marks' `CampFactsInKey`): every camp of a fixed kind gets `AmountText = null`, every camp gets `EliteText = null`. `IconKey`, `IconRgb`, `Elite`, `Dim`, `Clock`, `Engaged` stay, so the glyph, plate, gold diamond, clock and ring draw as today. The strip runs in `Present` right after `CampMarks.Glyphs` (`TacticalMapMarks.cs:158`), so the camp-text pass and the zone-name pass see the final rows and the first frame is final.

### 1.3 Cells

The MAP KEY gets nine kind cells, `KindCell<k>` for kind k (children "Icon" and "Text"), in two columns. The cells of the kinds seen PACK from slot 0 in kind order, with no hole: the kind with `SlotOf(k) == s` stands in column s mod 2, row s div 2. A cell shows the map's glyph (`CampRespawnLabels.Art(IconKey)` tinted `CampBadges.IconRgb`) and `CellText`: `"<amount> <word>"` when the kind is fixed, `"<word> · varies"` when not. Words: Offense, Soul Offence, Damage, Attack speed, Life steal, Max health, Defense, Soul Defense, Move speed. A cell is active only while the key is on and its kind was seen. The kinds come from the session's camp rows (`session.Collections.Camps`, one row per map camp, delivered whole by one camps state; `CampMarks.Glyphs` gives no rows unless the count equals the map's camp count, `CampMarks.cs:85`), so the set is complete at the first Present that has rows and the cells do not move after it in a match. A kind first seen later (a row that had no reward at first) shifts the cells after it by one slot, once: named and accepted. The old row "Camp buff" (`TacticalMapController.cs:219`) is the heading, retitled "Camp buff, each kill", and becomes a TEXT-ONLY row: its Icon child (:220-225) goes and its text starts at x 8, where the cells' glyphs start (the old text started at x 34, after the icon at x 14). The old row "Elite camp" (:226) keeps its gold diamond and is retitled "Elite camp: augment chest" (legacy's words, `planning.ts:190`, without "grants no fixed stat").

### 1.4 Geometry (HUD units, y down from the key root's top; `CampKeyLayout` holds the numbers)

Rows of 19: You 0, Players 19, Creatures 38, heading 57, grid rows 76, 95, 114, 133, 152, Elite camp 171, Quest giver 190, Trader 209, Quest objective 228, Public quest mark 247, Reward chest 266, Route stop 285, Walk target 304: 17 rows, root height 332 (was 237, `TacticalMapController.cs:201`). The grid's reserved height is five rows whatever the number of kinds seen (slot 8 alone in the fifth), so the key's height, the IN VIEW heading and all below never depend on the map. A cell is `floor(width / 2)` wide and 19 high: Icon 14 by 14 at (8, 2), text from x 26 to the right edge less 2. The key root's width becomes the rail's (`Left.W`); the old rows stay 250 wide. IN VIEW's heading moves from y 271 (:286) to `ViewY` 366 (`KeyTop` 34 + `KeyH` 332); the list's rect (`_contactScroll.offsetMin = (8, 112)`, `offsetMax = (-8, -(viewY + 22))`, :289) lies 112 above the rail's bottom and `ViewY` + 22 below its top, so its height is `Left.H - ViewY - 134`. `Left.H` is `mapSide + 50` (frame 8, gap 12, legend 30), `mapSide = floor(min(stageH - 176, stageW - 80 - 2 * col))` (176 = 44 chrome + 12 pad + 120 fixed), `col` 250 at text 1 and 290 at 1.5. List height before -> after: 1920 x 1080 (stage 1422 x 800) 269 -> 174; 1280 x 800 269 -> 174 (text 1.5: 265 -> 170); 980 x 2121 (stage 1225 x 2651, scale .8) 290 -> 195 (text 1.5: 210 -> 115).

### 1.5 When the key is on

`CampKeyLayout.InKey(tab, leftW, leftH)`: Tab mode, left column at least `MinWidth` 240 wide and `MinHeight` 610 high. 610 = `KeyTop` 34 + `KeyH` 332 + `ViewHead` 22 + `MinList` 110 + `ListFoot` 112: the key shows only when it leaves an IN VIEW list of at least 110 units. The key root is active exactly when `InKey` (today: `!compact`, `lh < 430f`, :284-285); there is no key without cells. `ApplyLayout` (:255) computes it before `SetMarkScale` (:272), keeps its own last value and writes `TacticalMapMarks.CampFactsInKey` only on a change; the IN VIEW heading uses `ViewY` when the key shows and 10 when not (as today). Planning: never on. `CampFactsInKey` defaults to false: a bare marks object and the minimap are unchanged. In the wide layout `Left.H >= 610` needs `stageH >= 736` and `stageW >= 640 + 2 * col` (1140 at text 1, 1220 at 1.5); both fail only where the stage scale clamps at .8, so the key hides in a window `h <= 588` px tall (before: `h <= 444`) or `w < 912` px wide (text 1.5: 976; before 768 and 832). The three canvases keep it (left columns 674, 670 and 695; 615 at 980 x 2121, text 1.5). The stacked layout (a stage under 900 units wide) follows the same height rule; its hiding heights are not derived.

### 1.6 Forget

`Forget()` (adds 1 to `Version`, clears the kinds) is called in exactly one place: `TacticalMapMarks.SetMap(null)` (`TacticalMapMarks.cs:110`); never in `ClearCampTexts` (`TacticalMapMarks.CampTexts.cs:42`), which `PlaceCampTexts` calls (:63) whenever no camp has a text (now the normal case). `Version` only grows, so a rebind cannot repeat a value.

### 1.7 What stays

The residual texts (a kind with two amounts, the planning map, a key-less window) are placed and drawn exactly as today, least-overlap fallback included (`CampTextPlacement.cs` is untouched; TEXT-3 changes that path). Zone names, clocks, ward seconds, chest seconds, the cover rule and the minimap are untouched.

### 1.8 Steady

Inputs are the rows and one layout bool; no actor, ward, clock, chest, time or position. A clear, a clock or a ward changes nothing. Named hops: (a) a kind turning "varies" shows its texts once, one placement pass (`CampTextPlacements` + 1), impossible with the shipped table; (b) `InKey` flipping when the player resizes across 610 high or 240 wide: texts and key swap in one frame, the zone names may move once; (c) the cells fill in the first Present after a bind (the first open); (d) a late kind shifts cells once (1.3).

### 1.9 Cost

`Apply` walks at most 50 camps twice per Present and allocates nothing; strings are composed only when `Version` changes. `ApplyCampKey` compares `Version`, the on flag and the root width with what it last wrote and returns; no label is measured in a steady frame. P-UI-MAP-PAINT-1's one pass per change holds.

### 1.10 What a player loses (say it in the hand-off, with its look list)

The number beside the camp, now one look away at the key (same art and colour); the word "Aug"; 95 units of IN VIEW list; the key in short or narrow windows. Wide windows 445 to 588 px tall (stage 556 to 735 units, scale clamped at .8) showed the key before and now hide it, the old texts and long list back; so do windows 768 to 911 px wide (832 to 975 at text 1.5). Look list: the three canvases at text 1.5 (the list is 115 high on the portrait one), and windows of 1000 x 560 (key hidden, texts back) and 1000 x 600 (key shown, list 124). No key is bound and nothing a key does changes (`UiInputRoutingEditModeTests` is untouched).

## 2. Dependencies

None beyond the base. `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`, P-UI-ALLOC-METER-1/2) and P-UI-TABMAP-TEXT-1 (`CampTextPlacement`, the camp-text pass) and P-UI-ZONE-NAMES-1 are on the base.

## 3. Relevant legacy paths

- `packages/client/src/ui/planning.ts:176-186` `campRewardBadge` (icon and the "+N" amount of the reward text), `:188-194` `eliteChestBadge` (title "Elite mob: drops an augment chest; grants no fixed stat", amount "Aug"), `:594` and `:691` (rewards hidden unless the mode is "map"), `:690` (the elite badge appended). `planning.css:231-262` the pill styling. Legacy's legend lists the zones only. Verified with `rg`/`grep` on 2026-11-23.
- Legacy doubt for the hand-off: the tooltip says "grants no fixed stat" (`planning.ts:190`); the elite's real worth (12 stacks for every contributor under `EliteRule::Rework`, `crates/rh-sim` `statics.rs:655`, `mob_life.rs:569-591`) is not on the wire and the key does not say it.

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/CampAmountKey.cs` (with `/// <summary>`: `CampAmountKey`, `CampKeyCell`, `CampKeyLayout`) and its copy by `bash client-unity/sync-net.sh` under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` with its `.meta`.
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.CampKey.cs` (partial, `/// <summary>` first, with `.meta`: the nine cells, `PlaceCampGrid(width)`, `ApplyCampKey()`).
- Tests: `client-unity/RoguehallaNet.Tests/CampAmountKeyTests.cs`, `client-unity/UnityProject/Assets/Tests/Editor/CampKeyEditModeTests.cs` (+ `.meta`).

## 5. Exclusive write scope

Paths under `client-unity/UnityProject/Assets/Scripts/UI/` unless noted:
- `TacticalMapMarks.CampTexts.cs` (public `CampKey`, public `CampFactsInKey`: the stub commit adds both as inert members; the implementer makes `CampFactsInKey` real), `TacticalMapMarks.cs` (`Present`: one `Apply` line after `Glyphs` at :158; `SetMap(null)` branch :110: `Forget`), `TacticalMapController.cs` (`BuildKey` :199-234, `ApplyLayout` :255-300, `PresentLarge` :27), `TacticalMapController.CampKey.cs` (new). No file passes 800 lines (the controller is 698 now).
- `client-unity/RoguehallaNet/Net/Ui/CampAmountKey.cs` (the stub holds the signatures; the implementer fills it) and its synced copy and `.meta`.
- Not in scope: `CampTextPlacement.cs`, `CampMarks.cs`, `CampBadges.cs`, `TacticalMapMarks.ZoneLabels.cs`, `.Cover.cs`, `MapScreenLayout.cs` (the node tools copy its formulas), `TacticalMapSurface.cs`, `TacticalMapController.Sheet.cs`, `.Tactics.cs`, `Tools/*.cjs`, `docs/DEVIATIONS.md`, `CardDisplayCatalog.cs`, every oracle file below.

## 6. Contracts and references

- Presentation only: Unity holds no rule; the wire, `crates/`, `packages/` are untouched. Text through `HudUi.Label`; colours from `UiTheme.Current` (the cells' text is `theme.TextPrimary`, the glyph is tinted with `CampBadges.IconRgb`). Unscaled time, nothing allocates in a steady frame.
- Readability gate: new non-test C# files have a `/// <summary>` before the first type and at most 800 lines; add no line to `View/ArenaView.cs` or `Presentation/SpriteActorPresenter.cs`.
- Design and evidence: `.agent-runs/lane-ui-rework/understand/design-tabmap-camp-texts.md`, `web3-tabmap-givers.txt` (read-only, in the main checkout).

## 7. Behaviour to preserve

- `CampTextPlacement.cs` and the placement of residual texts exactly as today; zone names, clocks, ward seconds, chest seconds, cover rule, minimap unchanged. `CampFactsInKey` defaults to false: a bare marks object (MapCover tests) and the minimap are unchanged.
- `hud-seams.test.cjs` and `tab-map-sheet.test.cjs` stay green: no `MatchHud` member moves; no new write of `WorldLabelVisibility.Hidden`. No key is bound.
- `ClearCampTexts` never forgets. `Present` runs `Apply` for the large marks only.
- The key's last child stays "Walk target (right-click)": create the nine cells between the heading row and the Elite row.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-TEXT-2` passes (the lane base is `lane/ui-rework/base-3`; add `--base lane/ui-rework/base-3`).
- The new dotnet tests and EditMode tests below are green; the five amended fixtures stay green; the classes in 9 stay green and unchanged.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green (through `in.sh`).

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/CampAmountKeyTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampKeyEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampKeyEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-TABMAP-TEXT-2.md`
- the `oracle-amend:` files (each in its own commit, listed under "Existing tests that change"): `client-unity/UnityProject/Assets/Tests/Editor/DeclutterLegendEditModeTests.cs`, `CampTextEditModeTests.cs`, `TabMapMarksEditModeTests.cs`, `ZoneNamesEditModeTests.cs`, `ZoneNamesRealOrderEditModeTests.cs` (same folder).

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter CampAmountKeyTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampKeyEditModeTests,DeclutterLegendEditModeTests,CampTextEditModeTests,TabMapMarksEditModeTests,ZoneNamesEditModeTests,ZoneNamesRealOrderEditModeTests`
- Green and unchanged, run with `unity-class.sh` before the gate: `MapCoverEditModeTests` (bare marks), `MapPaintEditModeTests`, `TabMapSheetEditModeTests`, `TabMapFlowEditModeTests`, `TabMapVisualFix3EditModeTests`, `DeclutterEditModeTests`, `ContactRailWordsEditModeTests` (they read the list's text, not the key), `MapScreenEditModeTests`; dotnet `CampTextPlacementTests`, `CampBadgesTests`, `CampGlyphsTests`. The implementer's gate run of the whole suite is final: any other red test is reported, not edited.
- `in.sh <worktree> bash client-unity/sync-net.sh` after editing the library.

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Moderate: a new library class with a strict allocation rule, and a key layout whose labels must be awake before they are measured. The hard parts are the traps below.

## 11. Known traps

- **The set-up line persists** only because the controller writes `CampFactsInKey` on a change of its own computed value; a test's `false` stays until the layout's value flips (planning does). It takes effect at the next Present.
- **Floors are measured first.** The 9 and 10.5 font-size floors of test 6 were measured by the oracle author (see "Notes from the oracle author"). Never lower a floor to get green. STOP and report if the full words of the nine fixed-kind cells or the elite row do not fit. The shorter words the design names ("Leech", "Soul Def", "Soul Off") are the owner's call and are not used without his word.
- Create the nine cells between the heading row and the Elite row, so the last child of "Key" stays "Walk target (right-click)".
- A label never active in the hierarchy measures a tenth of its size and `MythicTextScale` skips its fit until it wakes. Set a cell's text from `ApplyCampKey` (runs from `PresentLarge`, panel open), activate it, and call `Refit()` on each cell label's `MythicTextScale` after `PlaceCampGrid` resizes or moves it. `ApplyLayout` runs on a change only; `PlaceCampGrid` too.
- `CampKeyLayout.Cell`, `InKey` and `ListHeight` are the one source of the numbers; the controller and tests read them.
- Tests are a separate assembly with no `InternalsVisibleTo`: `CampKey`, `CampFactsInKey`, `Version`, the cells' names must be public or reached by reflection (the private `ApplyCampKey` is reached by `Delegate.CreateDelegate`, test 9; keep that exact name and an instance method with no parameters and no result).
- Compare texts ordinal. `Fix3Transport` is internal in `HudVisualFix3EditModeTests.cs` (same assembly, usable).
- `hud-seams.test.cjs` and `tab-map-sheet.test.cjs`: grep once that no "theme colour" test scans the Tab map panel (none was found).
- Capture tools copy layout maths (`hud-shots.cjs`, `vision-shots.cjs` copy the wide Tab-map layout): this packet leaves `MapScreenLayout` alone, so no copy changes.

## Tests this packet pins, and the existing tests that read this code

New (all red on the stub, each on an assertion):

**dotnet `CampAmountKeyTests`** (namespace `Roguehalla.Net.Tests`): 1 `TheNineKindsAreTheKeysOfCampBadges`; 2 `AFixedKindIsStrippedWhenTheKeyShowsItAndKeptWhenNot`; 3 `TheCellsAreTheShippedTablesAmounts`; 4 `ADifferingAmountMakesTheKindVaryAndKeepsEveryText`; 5 `TheLatchMovesOnlyOneWay`; 6 `AnUnknownKeyAndAMissingAmountAreLeftAlone`; 7 `TheEliteTagLeavesWithTheKeyAndTheFlagStays`; 8 `ForgetStartsOver`; 9 `VersionMovesOnlyOnAChange`; 10 `ApplyAllocatesNothing`; 11 `TheLayoutRule`; 12 `TheSeenKindsPackFromSlotZeroInKindOrder`.

**EditMode `CampKeyEditModeTests`** (namespace `Roguehalla.Client.Tests`): 1 `AFixedKindLeavesTheMapAndStandsInTheKey`; 2 `ADifferingAmountKeepsEveryTextOfItsKind`; 3 `ClearingACampAndItsClockMoveNothing`; 4 `ALateDifferingAmountMakesOnePassAndStays`; 5 `ABindForgetsTheLatch`; 6 `TheKeyIsWholeOnTheThreeCanvasesAtTextSizeOneAndOnePointFive`; 7 `ACompactWindowKeepsTheTexts`; 8 `PlanningKeepsTheTexts`; 9 `ASteadyFrameAllocatesNothing`; 10 `ALateKindShiftsTheCellsAfterItOnce`; 11 `TheKeyRowsStandWhereTheLayoutPutsThem`.

Existing tests that change, each in its own `oracle-amend:` commit, listed for the owner:
- `DeclutterLegendEditModeTests.TheKeyListsCampBuffAndEliteCampAndInViewStartsBelowTheLastKeyRow` (`DeclutterLegendEditModeTests.cs:33-60`), EXPECTATION WORDS: the two `CollectionAssert.Contains` lines (:54-55) expected "Camp buff" and "Elite camp"; they now expect "Camp buff, each kill" and "Elite camp: augment chest". The last assertion (the IN VIEW heading at or below the last key row) stays; it walks every child of "Key" and calls `Find("Text")`, which each cell has. Summary words amended. Red on the stub by design.
- `CampTextEditModeTests`, SET-UP LINE: `Marks().CampFactsInKey = false;` after `Initialize` in `BuildController` (`CampTextEditModeTests.cs:107-133`), summary words say the key is off. Without it these would be red once the feature lands: `TextsAtTheRightAndTopEdgesStayInsideTheMap`, `NothingThatTicksReplacesAText` (:558), `TheCampTextsOfARealMatchAreMeasuredWhenTheMapOpens` (:674, their elites print "Aug"), `TheZoneNamesSeeThePlacedBlocks` (:628), `ATerrainChangeInTheMiddleOfAMatchMovesNoText` (:777; the picture's two stat-2 camps pay 100: a fixed kind). The other six keep their expectations.
- `TabMapMarksEditModeTests.MarkLabelsAreAtLeastTenPixels` (`TabMapMarksEditModeTests.cs:236`): SET-UP LINE after `BuildController(window)`.
- `ZoneNamesEditModeTests` and `ZoneNamesRealOrderEditModeTests`: SET-UP LINE in each `BuildController` (:106 and :120); expectations unchanged (their picture camps carry `Text = "+3%"`, a fixed kind).

One commit per class: a class's builder line and its per-test lines are one change of one fixture; the line keeps each test in the configuration it was written for; `CampKeyEditModeTests` pins the default. These lines compile on the stub (the property exists) and the classes stay green on it.

Existing tests that read this code and stay green and unchanged: `MapCoverEditModeTests` (bare marks), `MapPaintEditModeTests`, `TabMapSheetEditModeTests`, `TabMapFlowEditModeTests`, `TabMapVisualFix3EditModeTests`, `DeclutterEditModeTests`, `ContactRailWordsEditModeTests`, `MapScreenEditModeTests`; dotnet `CampTextPlacementTests`, `CampBadgesTests` (`AmountTextIsThePerKillAmount` :63-73: the nine amounts), `CampGlyphsTests`, `CampMarksTests`.

## For the owner

- The planning screen keeps today's texts and their overlaps (legacy hides them there, `planning.ts:594`); hiding them is a one-line change of what `CampFactsInKey` means, and TEXT-3 improves the placement either way.
- The IN VIEW list is 95 units shorter (269 to 174 on 1920 x 1080; 115 on the portrait canvas at text 1.5).
- The key hides in a band of short or narrow windows (445 to 588 px tall, or 768 to 911 px wide), where the old texts and the long list come back.
- An elite's real worth (the shared stacks) is not on the wire and is not shown; the key says "augment chest".
- His list of words: "Camp buff, each kill", "Elite camp: augment chest", "varies" and the nine kind words (the repo's `BuildHudText.MobSuffix` has them lower case, "lifesteal" in one word; the key writes "Life steal").
- Proposed DEVIATIONS row (D1): see the design, section 6.

## Notes from the oracle author

- **Red on the stub, each on an assertion** (run with `unity-class.sh` and dotnet on the stub commit):
  - dotnet `CampAmountKeyTests`: all 12 red (`Kinds` 0 not 9, `Version` 0, `Count` 0, `Rows` 0, `CellText` empty, a fixed kind not stripped). The same 12 pass against a throwaway reference implementation (not committed), so every line of them can run and the numbers agree with each other.
  - `CampKeyEditModeTests`: all 11 red. First failures: 1 `CampFactsInKey` false (expected true); 2 same; 3 `CampKey.Version` 0; 4 and 5 `CampFactsInKey` false; 6 `CampFactsInKey` false at 1920x1080; 7 the key root active at 1000x480 (expected hidden); 8 `KindCell0` missing; 9 `CampKey.Version` 0; 10 `KindCell0` missing; 11 the key is 237 high (expected 332).
  - `DeclutterLegendEditModeTests.TheKeyListsCampBuffAndEliteCampAndInViewStartsBelowTheLastKeyRow`: red by design (the `oracle-amend:` words).
- **Scratch run (not committed):** one run of a copy of `CampKeyEditModeTests` in which every `Assert.That` and `AllocationMeter.AssertNone` was caught and collected. Every entry on every test was an assertion about the missing behaviour (a missing cell, the key off, a version 0, a key of 237, 12 children, a list of 269); no other exception was thrown, so every line of each test ran. It also showed the lines that happen to pass on the stub (the premises about the stage sizes 1422x800, 1280x800, 1225x2651, `Left.H` 474, 574 and 624, the old key shown at 1000x480 and 1000x560, no allocation in `Present`, `LayerChanged`).
- **Floors measured first (picture before floors).** A scratch test built the cell labels as `BuildKey` will (`HudUi.Label` 11 at the cell's rect, `offsetMin.x` 26, `offsetMax.x` -2, cell width `floor(col / 2)`, col 250 at text 1 and 290 at 1.5) and read the fitted size after `Refit()`: text 1 (box 97 wide): "+3 Offense" 11, "+3 Soul Offence" 11, "+3% Damage" 11, "+3% Attack speed" 10.34, "+0.75% Life steal" 10.83, "+3% Max health" 11, "+3.75 Defense" 11, "+4.5 Soul Defense" 10.31, "+1.5% Move speed" 9.89 (the smallest); text 1.5 (box 117 wide): 16.5, 14.23, 16.5, 12.47, 13.07, 13.91, 15.62, 12.43, 11.93 (the smallest). The floors 9 and 10.5 hold with margin; all nine fit. The elite row ("Elite camp: augment chest", x 34, 216 / 256 wide) fits at 11 and 16.5; "Camp buff, each kill" at 11 and 16.5. The not-fixed cell "Soul Defense · varies" fits at **9.10** (text 1) and **10.98** (text 1.5); "Attack speed · varies" 9.23 and 11.13; test 6 holds it only to 6. Nothing had to stop.
- **Deviations from the spec's test text:** (1) test 8 `PlanningKeepsTheTexts` uses test 1's rows (fixed kinds) instead of the four rows of `ThePlanningOrderPlacesWhenThePanelShows` (all stat 2, so a "varies" kind there), so a key that wrongly stripped in planning would be caught. (2) test 1 also checks the cell's Icon (the sprite is `CampRespawnLabels.Art(IconKey)`, the colour within 2 of the kind's `IconRgb` bytes); the spec says only "tinted". (3) tests 4 and 5 also assert `CampKey.IsFixed(2)` false where stated by the latch. (4) the dotnet test 4 asserts `Version` before the texts so it opens on a red line, and adds premise lines on the two amounts.
- **Fixture facts for the implementer:** the cell is read as `KeyRoot/KindCell<k>/Text` (its `TMP_Text`) and `KindCell<k>/Icon` (an `Image`); its rect is `anchoredPosition` (x, minus y) and `sizeDelta` with the top-left anchor, i.e. `HudUi.At(key, "KindCell" + k, x, y, w, h)`; an inactive cell is `SetActive(false)`. The test reads `ApplyCampKey` by `GetMethod("ApplyCampKey", Instance | NonPublic, no parameters)`. `CampKeyEditModeTests.UseTextScale` creates a `ShowcaseSettings` and sets `_textScale` by reflection before `Initialize`.
- **The existing classes that read this code** (listed in 9, plus `CampTextPlacementTests`, `CampBadgesTests`, `CampGlyphsTests`, `CampMarksTests`) were run on the stub: all green (147 of 159 EditMode green, the 12 red are the 11 new tests and the Declutter amend; dotnet 39 of 39).
- **Oracle commits:** `oracle:` holds the two new test files and this packet; five `oracle-amend:` commits follow, one per amended class (`DeclutterLegendEditModeTests`, `CampTextEditModeTests`, `TabMapMarksEditModeTests`, `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`). Four of the five change only a set-up line and stay green on the stub and after the feature.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
