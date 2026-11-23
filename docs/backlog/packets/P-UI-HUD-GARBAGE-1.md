# P-UI-HUD-GARBAGE-1: the HUD stops rebuilding what nobody sees

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat: "make the front end as fluid as possible",
  "Please do a UI optimization sweep"). A Web measurement of 2026-11-23 showed the HUD allocating 1 to 9 KB a frame in a
  match (a stop-the-world collection on the Web); an experiment counted why.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `c8e21c0e1` when this packet was branched; the spec
  was checked on `fe3360a5c`, the line numbers below were re-read on `c8e21c0e1`). Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-c`.
- **D-rules touched:** D1: a performance change inside the Unity presentation; no shown text, colour, key or layout number
  changes, so nothing is matched against legacy and no `DEVIATIONS.md` row is proposed. D2 n/a. D5/D6 none. D9: no game rule is touched.

## 1. ID and outcome

A server tick that changes nothing the player sees allocates almost nothing in the HUD. Numbers are GC.Alloc samples per
delivered 20 Hz tick on this editor (Mono). An idle HUD frame allocates 0 today (every existing "allocates nothing" test
measures only that), but a delivery changes inputs and the next frame rebuilds texts whose inputs did not change. Base,
per tick: standing 10, walking 12, taking damage 31, a fight 16, and 252.5 while the Fire champion's roam charge moves
(20 s after a match goes Active and after every hit).

THE RULE: a write, a string or a list is made only for something that is SHOWN and has CHANGED, and nothing a player sees
goes stale; the one exception is a tooltip opened in the middle of the ramp, which may be one HUD refresh old. Six cuts;
every shown text, colour, key and layout number stays what it is today. After the cuts (the author measured, see the
bounds below) a ramp tick is about 20 and a damage tick about 6.

Causes, read on the base (all paths under `client-unity/UnityProject/Assets/Scripts/UI/`):

1. `ApplyChampionRows` (`MatchHud.IconPresentation.cs:44`) leaves early only if EVERY panel code and value is unchanged
   (`same`, :63-65). The roam charge (codes 8, 23) moves each ramp tick, so it rebuilds `ChampionPanelText.Rows`, the
   three row texts (equal, so `Set` writes nothing) and the hover tooltip (`HudContentText.AutoTooltip`, :73-76): 180.75 a call.
2. The hp label (`RefreshLegacyHud`, `MatchHud.Legacy.cs:288`, the label block :313-319) builds a StringBuilder and strings
   every dirty frame: 5 a call (14 with a bounty).
3. `RefreshOwnerPresentation` (`MatchHud.cs:316`; key and strings :321-326) builds the vitals and kills strings whenever an
   input of its key changes (hp, max hp, kills, GV, status, barrier, ring step), for two labels hidden while the plate is
   active: 9.
4. `TickFeatures` (`MatchHud.Features.cs:339`) calls `BuildDrawer.Refresh` every frame; closed, it still builds two strings
   (`BuildDrawer.cs:156-157`) whenever an input of its `SameSelf` key changes (hp, kills, GV, ...): 12.
5. `RefreshDamageReadout` (`MatchHud.IconPresentation.cs:30-31`) calls `me.Weapon.ToString()` every dirty frame: 2;
   `RefreshBuildIcons` (`MatchHud.Features.cs:420`) does the same per equipped slot on every Collections event.
6. The server re-sends the build summary as a NEW object. `RefreshBuildIcons` (`_shownSummary`, `Features.cs:27,406`) and the
   BUILD block of `RefreshCollections` (`MatchHud.cs:443`, field `_summaryShown` :464) key on the object, so each ramp tick
   rebuilds all nine GAINED cells (`BuildHudText.Cells`: a List and nine strings), the mob line, the gains header and hint
   (`RefreshGainsHeader`, `MatchHud.LeftEdge.cs:115`, keyed on the `_gainCells` list object, :119), the BUILD text, the
   cards row and the B hint: 54.75 a tick, inside `Session.Poll`. One cell really changes (Move speed).

## 2. Dependencies

`AllocationMeter` (P-UI-ALLOC-METER-1 and -2, `Assets/Tests/Editor/AllocationMeter.cs`) is on the base. Nothing else.
Not in scope here and not to be touched: the Tab map's `PresentTactics` and the minimap, `RefreshAccountMenu` (9 a call at 4
Hz), the wire decoder (8 a ramp tick), and what EditMode cannot count (text mesh rebuilds, tooltip layout, IL2CPP sizes). An
open champion tooltip is rebuilt every ramp tick (about 120 samples): accepted, no change to `HudIconHint`.

## 3. Relevant legacy paths

None. This is a presentation performance change; no legacy behaviour is read or changed (D1 above). Evidence from the lane:
`.agent-runs/lane-ui-rework/understand/exp-hud-garbage.txt` (section 8 corrects its first reading) and `perf-hud-garbage.txt`
in the main checkout.

## 4. New paths

- `client-unity/RoguehallaNet.Tests/BuildHudCellTests.cs` (dotnet)
- `client-unity/UnityProject/Assets/Tests/Editor/HudGarbageRig.cs`, `HudGarbageEditModeTests.cs`, `HudStaleEditModeTests.cs`, each with `.meta`
- library member `BuildHudText.Cell` (stubbed in the `stub:` commit; the implementer fills it)

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/`: `MatchHud.IconPresentation.cs` (`RefreshDamageReadout`,
  `ApplyChampionRows`, the weapon name helper), `MatchHud.Legacy.cs` (the hp label in `RefreshLegacyHud`; the writer call
  in `SetPlateMode`), `MatchHud.cs` (the key block of `RefreshOwnerPresentation`, `ClearOwnerPresentation`, the BUILD block
  of `RefreshCollections`, the `_summaryShown` field: calls and removals only, see Traps for the room),
  `MatchHud.Vitals.cs` (it exists, 22 lines, the eased fills of P-UI-VITALS-EASE-1: the vitals writer, its stale flags and
  `_lastFaction` join it, and its `<summary>` says so), `MatchHud.Features.cs` (`ToggleBuild`, `ApplyBuildSummary`,
  `RefreshBuildIcons`, the shown-summary fields, the BUILD key fields and the distinct count), `MatchHud.LeftEdge.cs`
  (`RefreshGainsHeader`, `_headerCells`), `BuildDrawer.cs` (`Refresh`)
- `client-unity/RoguehallaNet/Net/Ui/BuildHudText.cs` and its synced copy under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/`
- Not in scope: `TacticalMapController*.cs`, `TacticalMapMarks*.cs`, `MatchHud.Pulses.cs`, `.Notices.cs`, `.QuestRewards.cs`,
  `.PlayerFlows.cs`, `.Tutorial.cs`, `CardDisplayCatalog.cs`, `HudIconHint.cs`, `PerfMarks.cs`, `PerfRecorder.cs`,
  `TraderDialogView.cs`, `MythicTextScale.cs`, `Tools/hud-seams.test.cjs`, the wire decoder, any existing test file (none is
  amended). Shared files: `MatchHud.cs` with P-UI-FIRST-RUN-HINT-1 (landed) and P-UI-RUN-END-1 (one string literal of the
  phase table); `MatchHud.Legacy.cs` with P-UI-PLATES-OPAQUE-1 (alpha literals in `CreatePlate` and `CreateDamageReadout`, no
  line added or removed); P-UI-TEXT-FIT-1 builds last (tests, and one creation statement in `CreateDamageReadout`).

## 6. Contracts and references

R-AUTH / ADR 0008: Unity holds no game rule; everything drawn is from server observations. Lane rules
(`.agent-runs/lane-ui-rework/specs/COMMON.md`): motion and steady frames allocate nothing; `MatchHud` binds and unbinds
sessions many times, never leave a handler on an old session; the readability gate (a `<summary>` on a new C# type, at most
800 lines per file).

## 7. Behaviour to preserve

Every shown text, colour, key and layout number. The cuts below are keys on what is shown; the guard lines of the oracle
(green on the base) pin that nothing goes stale.

1. **Champion rows, two keys.** The rows (label, value, scaling texts) are rebuilt only when a code the rows display
   (0-7, 10-22), the faction, the weapon, the HUD block, the attack-speed mirror (`OwnerCombat.AsMilli`) or the code list
   changed. The mirror stays in the rows key on purpose (only the tooltip reads it; it changes rarely). The tooltip is
   rebuilt when the rows were; or when a tooltip-only code (8, 9, 23-27: `Rows` uses 8 and 9 only for `Roam`,
   `HudContentText` uses 23-27 only in its roam and scaling sentences) moved while `_damageHint.IsVisible`; or when one
   moved while it was closed (stale) and it has since opened. `_panelShownCount = -1` still means "rebuild both now".
   180.75 becomes 0 while only the roam charge moves.
2. **hp label.** `_hpValue` is rewritten only when the clamped hp or max, `Barrier`, `Bounty` or the RGB of
   `UiTheme.Current.Accent` changed since the last write, through one reused `StringBuilder`. The key is reset where
   `_lastHp` is reset (`ClearOwnerPresentation`; `Bind` reaches it).
3. **Vitals strings.** The `_vitals` and `_combatStats` texts are built only while their own label is shown (`activeSelf`;
   `_combatStats` is hidden in practice while `_vitals` is shown, so each has its own stale flag). They are built from
   remembered inputs: the `_last*` fields plus a new field `_lastFaction`, assigned on every `RefreshOwnerPresentation` call
   (outside the key's `if`; the faction stays out of the key, as today). They are made current whenever the label becomes
   shown: `SetPlateMode` writes a shown label that is stale, its text set BEFORE `SetActive(true)` so the label's
   `MythicTextScale.OnEnable` refits for the final text. Kills or GV moving alone while the plate is active allocates nothing.
4. **Drawer.** `BuildDrawer.Refresh` returns before any work (copies and text-scale line included) while the drawer is
   closed (`IsVisible` false): the gate sits inside `Refresh`, not in `TickFeatures`, which is not edited.
   `MatchHud.ToggleBuild` calls `_drawer.Refresh(_session.Collections, _session.Me, _showcaseSettings.TextScale)`, the call
   `TickFeatures` makes, once, right after `SetVisible(true)`: it never opens with old numbers (it is the only code that opens the drawer).
5. **Weapon name.** One static helper turns a `WeaponId` into its name for the two UNKEYED call sites: the champion row's
   `_damageWeapon` text (`RefreshDamageReadout`) and the slot names in `RefreshBuildIcons`. It reads a `string[]` indexed by
   the enum's integer value, filled lazily, bounds-checked; a value outside the array falls back to `ToString()`. Every
   value, defined or not, reads exactly `ToString()`. (An int index does not box.) The KEYED sites keep `ToString()`, not
   edited: `ApplyInventoryHints` and the focus change in `RefreshCollections`.
6. **Build summary, keyed on what it shows.** No key on the Summary object remains. Two consumers read the summary in one
   `RefreshCollections` call, so they keep INDEPENDENT keys (Traps):
   - `RefreshBuildIcons` / `ApplyBuildSummary` keeps copies of the values, never the decoder's arrays: whether a summary is
     present (`_gainsShown`, assigned from `summary != null`, not from `_gainCells.Count`), the nine raw gains (a raw past
     `Gains.Length` is 0, as `Cells` reads it), the mob rows by content (`MobType`, `Stat`, `Stacks`, `Total`); a GAINED cell
     (text, colour, bold or regular) is rewritten only when its own raw gain changed, or the theme's TextPrimary or
     TextSecondary changed, or it was never written; the string comes from `BuildHudText.Cell`;
   - the list the gains header reads: `_gainCells` is a PERSISTENT `BuildCell[9]`, allocated once, while a summary is
     present and `Array.Empty<BuildCell>()` while none; never rebuilt as a list. The cell writer brings element i current
     when raw i changed (the same string the label gets, labels present or not) and counts the change in an int
     `_gainCellsVersion`. `RefreshGainsHeader` learns that a cell changed from that counter, NOT from the reference compare
     it makes today, and consults it only while the strip is folded (an open header shows neither count nor hint). The write
     on a fold change always runs, reads the current array and remembers the counter; a header never written is written on
     its first call;
   - the mob line (`_mobText` text and visibility) is rebuilt only when the mob rows or the presence changed;
   - the BUILD block of `RefreshCollections` (`_build`, `_buildHint`, cards row) keeps its own small key on exactly what its
     text reads from the summary: the presence and the count of distinct `MobType` in `MobRows` (the inputs of
     `BuildHudText.ExtrasHeader`). It runs when that key changed or when it runs today (first run, `HasBuild`, cards,
     boosts), not on a new object and not on a Stacks-only change. The key's fields and the distinct count (a loop counting a
     row whose `MobType` no earlier row has: no `HashSet`, not `ExtrasHeader`) live in `MatchHud.Features.cs`; `MatchHud.cs`
     keeps the call;
   - `SetPlateMode` is called from the summary path only when `_gainsShown` flips; else `RefreshGainsHeader` alone;
   - a PRESENCE FLIP of the summary (none to some, some to none) takes the base's path: every remembered cell, the mob rows,
     the header and the hint are written as the base writes them; the array is cleared or filled, the counter reset (the
     header's remembered counter too, so the next `RefreshGainsHeader` writes) and the remembered copies invalidated, so the
     next summary writes everything.
   In the ramp one cell changes a tick (Move speed): that string is the remaining cost; the key is per cell. A folded strip
   rewrites its header and hint each ramp tick: accepted.

New library member (`client-unity/RoguehallaNet/Net/Ui/BuildHudText.cs`, namespace `Roguehalla.Net.Ui`):

```csharp
/// <summary>The GAINED cell at <paramref name="index"/> (0-8, legacy order) for one raw gain; Cells(s)[i] equals
/// Cell(i, s.Gains[i]) in every field. Throws ArgumentOutOfRangeException outside 0-8.</summary>
public static BuildCell Cell(int index, int raw)
```
`Cells` calls it for each cell; its output does not change. The `stub:` commit adds `Cell` returning `default` (and carries
the `sync-net.sh` copy of `BuildHudText.cs`); no EditMode test names `Cell`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-HUD-GARBAGE-1` passes (the lane base: add `--base lane/ui-rework/base-3`).
- The new tests below are green, and the existing classes listed under "Existing tests" stay green WITHOUT edits (the
  implementer runs all of them through `unity-class.sh`).
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (run it through the wrapper after the last edit; do not edit it).

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/BuildHudCellTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudGarbageRig.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudGarbageRig.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/HudGarbageEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudGarbageEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/HudStaleEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudStaleEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-HUD-GARBAGE-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter BuildHudCellTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> HudGarbageEditModeTests,HudStaleEditModeTests` (all helper scripts are
  under `/Users/nevinmckinnon/Documents/Projects/RogueHalla/`)
- `.../bin/in.sh <worktree> bash client-unity/sync-net.sh` after any library edit
- `.../bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-HUD-GARBAGE-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Sonnet implements, reviewer reviews. The risk is staleness: a key that omits something shown (a tooltip, a header count, a
vitals label that was hidden while its inputs moved). The oracle's "nothing stale" class pins each cut with guards green on
the base; read them before deciding a key.

## 11. Known traps

- **`SetPlateMode` must write a shown stale label itself.** `TrainingBannerEditModeTests.InTheArenaTheKillsAndGv...` and
  `TrainingBannerNotesEditModeTests.PracticeThenArenaInTheSameHudRestoresTheThreeRowPlate` call `SetPlateMode(false)` with no
  refresh and read `_combatStats.text` ("Kills ..."): a writer only in `RefreshOwnerPresentation` turns them red.
  `RefreshLegacyHud` flips the plate AFTER the vitals block, so a Dead snapshot shows the label in the same call: write on the
  show, text before `SetActive(true)`.
- Build the vitals strings from the remembered fields, not `_session.Me` (`Bind` and `ShowMenu` call `ClearOwnerPresentation`
  while `Me` may be set). Clear resets both stale flags, `_lastFaction` and the hp key on the SAME LINE as `_lastHp =
  float.NaN` (in `ClearOwnerPresentation`, `MatchHud.cs:350`), before its `Set` calls and before `RefreshLegacyHud(null, 0d)`:
  that call runs `SetPlateMode(false)` when the plate was active, and a stale flag still set would make the writer overwrite
  the waiting texts with remembered fields ("HP NaN / ..."). The same path runs from `Bind`, `ShowMenu` and every spectate
  frame (`HudOwnerRefresh` with `me == null`). Bare-label rigs (`MatchHudWishBarrier`, `OwnerVitalEase`) set loose labels, one
  may be null, and `_healthRow` is null: guard every field. `OwnerWords` is a full plate (`CreateCombatHud`) with no session:
  the plate stays inactive and both labels are shown, so the writer runs in `RefreshOwnerPresentation`, not `SetPlateMode`.
- **The summary's two consumers keep independent keys.** `RefreshBuildIcons` runs first in `RefreshCollections` and the BUILD
  block follows: a comparison and copy-update done in the first would show "unchanged" to the second, and `_build` ("Cards &
  mob bonuses . N", N counts distinct mob types) would go stale on a new mob type.
- After fold, S, none, S the folded header and hint read S's count again (a null summary makes an empty cell list: the base
  shows "GAINED . 0" and "Nothing gained yet").
- Tests call `RefreshBuildIcons` with an empty `CollectionsView` (`TilePlates`) and `RefreshCollections` on a bare HUD
  (`ShowcaseState`): with no summary it does nothing, and the summary path must not throw with null `_gainValues` entries,
  `_mobText`, `_buildBox` or strip.
- `ApplyChampionRows`: a differing VALUE at an index whose old and new code are the same code, and that code is in {8, 9,
  23-27}, is tooltip-only; a different code at an index, or a value change of any other code, rebuilds the rows. Set the
  stale flag only when the tooltip was not rebuilt; the no-panel path clears it. Opening a hint runs no refresh: the next
  dirty refresh (at most 50 ms) repairs a stale tooltip.
- The meter's own warm window of `calls` runs comes first, then up to three measured windows: prebuild at least 4 x calls + 64
  ticks per delegate, strictly rising; no `FieldInfo.SetValue` in a window.
- `hud-seams.test.cjs`: no method name twice across `MatchHud*.cs` (new members get unique names; no test reaches them),
  `MatchHud.Legacy.cs` under 420 lines, `MatchHud.cs` under 600, no tracked C# file above 800 lines. The test counts
  `split('\n')`, one more than `wc -l`; on this base Legacy.cs is 374 (wc), MatchHud.cs 571 (wc): about 44 lines free in
  Legacy.cs, 27 in MatchHud.cs. Net growth: Legacy.cs at most 25, MatchHud.cs at most 20 (its two blocks shrink to calls); the
  writer, the BUILD key and the distinct count go in `MatchHud.Vitals.cs` and `MatchHud.Features.cs` (446 lines). Re-count
  after the last edit; run the test through the wrapper.

## Existing tests that pin this code (none is amended; all must stay green)

The experiment ran these 14 under cuts 1 and 2 only (165 of 165): `ChampionPanelNumbers`, `MatchHudIconPresentation`,
`HudVisualFix`, `HudVisualFix2`, `HudVisualFix3`, `HudLegacyLayout`, `HudTextScale`, `LeftEdgeFold`, `TrainingBanner`,
`ChampionPanelNoGarbage`, `BuildHudCells`, `OwnerVitalEase`, `MatchHudWishBarrier`, `WishTile`. Not run against cuts 3, 4,
6: `TrainingBannerNotes`, `ShowcaseState`, `HudWords`, `BuildDrawer`, `LedgerWords`, `OwnerWords` and the callers
`MatchHudStatusNotes`, `MatchHudStatusTexts`, `TilePlates`, `HudVisualFix4`, `QuestOffer`, `QuestTracker`, `UiInputRouting`,
`TrainingConsoleIntegration`, `ChampionRowNameFit`, and the indirect callers `AbilityFeedback`, `PlayerFlows` (both reach
`ClearOwnerPresentation` through `ShowMenu` and `Bind`) and `TrainingHudVisibility` (opens the drawer by the real key).
(All names take the suffix `EditModeTests`.) What the ones that call the changed members assert, read in this check:

- `ChampionPanelNumbers` (:103-118, :148-182): `ApplyChampionRows` with the v17 summary draws the scaling line
  `0.3× Offense + 1× SO` and a tooltip with the weapon-scaling, Fire Roam and raw-damage sentences; without the block the line is
  blank and the Roam is `Rows`' text; `RefreshBuildIcons` + `UpdateInventoryHints` slot texts.
- `MatchHudIconPresentation` (:55-123): the damage weapon reads `Bow` after `RefreshOwnerPresentation` and `—` after
  `ClearOwnerPresentation`; rows are hidden before the first summary and then equal `ChampionPanelText.Rows`; the hint text
  contains `Auto.Detail`.
- `HudVisualFix` (:89-95): `RefreshLegacyHud` writes `_hpValue` with a rich-text bounty tag. `HudVisualFix2` (:159-176): sets
  `_panelShownCount = -1` then `ApplyChampionRows` for each faction and value (so -1 must keep meaning "rebuild now").
  `HudVisualFix3` (:87-97): `RefreshCollections` on a session's Collections. `HudTextScale` (:132-209): label texts of the
  plate (`_vitals` "Haunted · HP 1234.5 / ...", `_combatStats` "Kills 123 · GV 12345 · Alive", `_hpValue`, `_mobText`, slot
  names) fit their boxes after `SetPlateMode(true)`.
- `LeftEdgeFold` (:96-143, :347-352): `SetPlateMode`, `RefreshLegacyHud`, `RefreshBuildIcons` and `ApplyChampionRows` on a
  folded and an open plate (header count and hint follow the cells; nothing here requires a rewrite per object).
- `BuildHudCells` (:33-88): `SetPlateMode(true)`, then `RefreshBuildIcons(view)` fills the nine cells and `_mobText`.
- `MatchHudWishBarrier` (:20-132, :165-208) and `OwnerVitalEase` (:257-311): bare labels (`_vitals`, `_combatStats`, fills set by
  reflection, no `_healthRow`) fed through `RefreshOwnerPresentation` and `ClearOwnerPresentation`; `RefreshBuildIcons` slot
  names and colours. These are the null-guard cases for cut 3.
- `OwnerWords` (:75-107): a full plate with no session; `RefreshOwnerPresentation` writes `_vitals` and `_combatStats` (both labels
  shown, plate inactive).
- `TrainingBanner` (:84-86) and `TrainingBannerNotes` (:33-44): `SetPlateMode(false)` with no refresh, then read
  `_combatStats.text` (see Traps).
- `ShowcaseState` (:38-80, a bare HUD) and `TilePlates` (:246-433, an empty `CollectionsView`): `RefreshCollections` and
  `RefreshBuildIcons` with no summary must not throw.
- `ChampionPanelNoGarbage` (:44): measures with a meter that reads 0 on this editor (passes on any code; do not copy).
- `BuildDrawer`: a bare drawer; every drawer test opens the drawer before it refreshes (read).

None pins "always rebuild"; two read a plate-hidden label after `SetPlateMode` (Traps).

## The oracle

Rig (`HudGarbageRig`): the whole-HUD rig of `AbilityRowNearbyNoGarbageEditModeTests` with the v17 HUD block
(`protocol/fixtures/v17.json`, as `ChampionPanelNumbersEditModeTests.Server` reads it), a Fire player with a Wand, one inventory
State frame, 24 actors, 64 prebuilt ramp variants; every message, view and delegate is built before a window. The three
clocks are `SessionOptions.Now`, the instance field `MatchHud.HudLocalMs`, and the static `TacticalMapController.SecondsClock`
(seconds); each advances 50 ms a delivery, and no clock is read. `_nextMetricsAt` is parked at `double.MaxValue`. TearDown
restores `SecondsClock`, `UiTheme.Current`, `EventSystem.current` and `ShowcaseSettings._textScale`.

dotnet `BuildHudCellTests`: `CellEqualsCellsForEveryIndexAndEdgeRaw` (a `WireBuildSummary` whose nine `Gains` all equal raw; i
0-8; raw 0, 1, -1, 499, 500, 501, -500, 1250, 240000, 1500000: Label, Value, Dim equal `Cells(s)[i]`),
`CellRefusesAnIndexOutsideTheNine` (-1 and 9 throw).

EditMode `HudGarbageEditModeTests` (`AssertNone`, 320 calls = a multiple of 64): 1 `ChampionRowsAllocateNothingWhileOnlyTheRoamCodesMove`;
2 `AnOwnerRefreshWithOnlyTheKillsMovingAllocatesNothing`; 3 `TheHpLabelAllocatesNothingWhileItsInputsStay`; 4
`AClosedDrawerAllocatesNothingOnAnHpChange`; 5 `TheWeaponNameAllocatesNothing`; 6 `AnIdleFrameStillAllocatesNothing` (standing
guard); 13 `AnEqualSummaryInAFreshObjectAllocatesNothingInTheHud`; and the bounded tests (`AssertAtMost`, 320 calls)
`ARampTickOfTheWholeHudStaysUnderTheBound`, `TheCollectionsHandlerInTheRampStaysUnderTheBound`,
`ADamageTickOfTheWholeHudStaysUnderTheBound`.

EditMode `HudStaleEditModeTests` ("nothing stale", one class of behaviour per cut; "untouched" is proved by a marker written
into the text by hand that must survive): 7 `TheRowsAreRightAndTheTooltipIsRebuiltOnlyWhenNeeded`, 8 `TheHpLabelFollowsItsInputs`, 9
`TheVitalsStringsAreCurrentWhenTheirLabelIsShown`, 10 `TheDrawerOpensWithCurrentNumbers`, 11
`TheWeaponNameIsTheEnumNameForEveryValue` (standing guard), 12 `EachBuildPartFollowsItsInputsWhenANewSummaryArrives`.

## Oracle results on the stub (run by the oracle author)

dotnet `BuildHudCellTests` (stub `Cell` returns `default`): both tests red. `CellEqualsCellsForEveryIndexAndEdgeRaw` fails on
`label, index 0 raw 0` (expected "Offense", was null); `CellRefusesAnIndexOutsideTheNine` fails because no
`ArgumentOutOfRangeException` is thrown. On the throwaway implementation both pass (and `BuildHudTextTests`, `BuildHudHeaderTests`, 16 of 16).

EditMode, `HudGarbageEditModeTests` (11 tests): 9 red on an assertion, 2 standing guards green.
- `ChampionRowsAllocateNothingWhileOnlyTheRoamCodesMove` red: 57,840 samples in 320 calls (180.75 a call).
- `AnOwnerRefreshWithOnlyTheKillsMovingAllocatesNothing` red: 5,120 (16 a call).
- `TheHpLabelAllocatesNothingWhileItsInputsStay` red twice: 1,600 (5 a call), then with a bounty 4,480 (14 a call).
- `AClosedDrawerAllocatesNothingOnAnHpChange` red: 3,840 (12 a call); its control (open drawer allocates) is green.
- `TheWeaponNameAllocatesNothing` red: 640 (2 a call).
- `AnEqualSummaryInAFreshObjectAllocatesNothingInTheHud` red: 16,320 (51 a call). The views carry the rig's inventory: the control
  (the loop on a correct implementation) allocates nothing with it.
- `ARampTickOfTheWholeHudStaysUnderTheBound` red: 80,800 (252.5 a call) against the limit 7,680.
- `TheCollectionsHandlerInTheRampStaysUnderTheBound` red: 19,760 (61.75 a call) against 6,400.
- `ADamageTickOfTheWholeHudStaysUnderTheBound` red: 9,920 (31 a call) against 2,880.
- `AnIdleFrameStillAllocatesNothing`: green on the stub and on the throwaway (standing guard).

EditMode, `HudStaleEditModeTests` (6 tests): 5 red on assertions, 1 standing guard green. Every red entry (scratch run, below):
- `TheRowsAreRightAndTheTooltipIsRebuiltOnlyWhenNeeded`: "a closed tooltip is left alone while only the roam codes move" and
  "... after the pointer left" (the closed tooltip text is rewritten; expected the marker).
- `TheHpLabelFollowsItsInputs`: "the hp label is not rewritten while hp, max, barrier, bounty and the accent stay".
- `TheVitalsStringsAreCurrentWhenTheirLabelIsShown`: the hidden kills label is rebuilt at kills 2, kills 5, GV 3 and hp 800, and the
  hidden vitals label at hp 800.
- `TheDrawerOpensWithCurrentNumbers`: "a closed drawer is not refreshed by the frames" (reads `HP 700 / 950`, expected the first open's `HP 950 / 950`).
- `EachBuildPartFollowsItsInputsWhenANewSummaryArrives`: 53 entries, all "is not rewritten": (a) the nine cells, mob line, BUILD line, B hint,
  cards row, gains header and hint; (b) cells 0-7 and the same seven others; (c) cells, and after a Stacks change the BUILD line, B hint and
  cards row; (c) new type: cells 0-8; (e) an open header's text and hint survive a cell change and a zero cell turning non-zero.
- `TheWeaponNameIsTheEnumNameForEveryValue`: green on the stub (standing guard).

All the guard lines of tests 7 to 12 (marked "guard" in the test messages) are green on the stub and on the throwaway.

Existing classes on the stub: the 32 classes of the list above (335 tests: `ChampionPanelNumbers`, `MatchHudIconPresentation`, `HudVisualFix`..`4`,
`HudLegacyLayout`, `HudTextScale`, `LeftEdgeFold`, `TrainingBanner`, `TrainingBannerNotes`, `ChampionPanelNoGarbage`, `BuildHudCells`,
`OwnerVitalEase`, `MatchHudWishBarrier`, `WishTile`, `ShowcaseState`, `HudWords`, `BuildDrawer`, `LedgerWords`, `OwnerWords`,
`MatchHudStatusNotes`, `MatchHudStatusTexts`, `TilePlates`, `QuestOffer`, `QuestTracker`, `UiInputRouting`, `TrainingConsoleIntegration`,
`ChampionRowNameFit`, `AbilityFeedback`, `PlayerFlows`, `TrainingHudVisibility`) are 335 of 335 green. The same 32 classes ran green on the
throwaway implementation as well (together with the new classes: 350 of 351, the one red being the hazard below).

## Bounds (measured on a throwaway correct implementation)

The author wrote the six cuts as a throwaway (not committed; the patch is outside the repository), ran the new classes, and measured:

| test | base a call | M a call (throwaway) | limit a call = ceil(1.5 M) | limit in 320 calls | at most half the base |
|---|---|---|---|---|---|
| `ARampTickOfTheWholeHudStaysUnderTheBound` | 252.5 | 15.75 | 24 | 7,680 | 126.25: yes |
| `TheCollectionsHandlerInTheRampStaysUnderTheBound` | 61.75 | 12.75 | 20 | 6,400 | 30.9: yes |
| `ADamageTickOfTheWholeHudStaysUnderTheBound` | 31 | 6 | 9 | 2,880 | 15.5: yes |

On the throwaway every test of the three classes is green (16 of 16 EditMode tests, 2 of 2 dotnet).


## Notes from the oracle author

- **Scratch run** (not committed): for each new EditMode class a copy in a throwaway namespace shadowed `Assert` and `AllocationMeter` with
  catch-and-collect versions (catch `AssertionException`, write its message to a file, carry on), so no failed assertion stopped a test. On the
  stub every collected entry was an assertion about the missing behaviour (the lists above); no other exception, no set-up defect. Run on the
  throwaway implementation the collected list was empty except the three bounded tests at a limit of 0 (which gave M). That run found one
  defect of mine, fixed before the commit: in test 12 (g) the summary had no zero cell, so the muted colour was not tested (it now uses a
  summary with two zero gains).
- **Fix pass (checker finding on test 12 (g))**: the theme step used to follow a summary whose two muted cells (1 and 6) held non-zero gains, so they were rewritten by their raw change whatever the key held, and only TextPrimary was isolated. Test 12 (g) now first delivers a settled `Summary(BaseGains, ...)` (muted cells already 0), then switches the theme and delivers a second fresh equal summary: no raw value changes, so only a key that holds TextSecondary and TextPrimary recolours the cells. The per-cell colour loop is kept. The changed lines come after (a) in test 12 and so did not run on the stub (the test stops at (a)); they use only helpers the same test already calls.
- **Throwaway implementation**: the six cuts as the spec describes them (the packet's edits (i) and (ii) from the experiment, a writer for the
  vitals strings with two stale flags and `_lastFaction`, `BuildDrawer.Refresh` gated on the open drawer plus the `ToggleBuild` refresh, the
  weapon-name array, `BuildHudText.Cell`, persistent `BuildCell[9]` with a version counter, per-cell labels, a mob-rows copy, the independent BUILD
  key). It is the proof that the oracle can be satisfied, nothing more: the implementer writes their own.
- **Differences from the spec**:
  - Test 11 (`TheWeaponNameIsTheEnumNameForEveryValue`): "a fresh HUD whose first value is Sword" is a standalone `SelfView` handed to
    `RefreshDamageReadout` on the rig's HUD (the rig's player holds a Wand). The helper's array is static, so a truly fresh cache is not reachable
    from a test; the values 0-5, 6, 99 and 255 are all compared with `ToString()`.
  - Test 9 has one more red line than the spec lists: hp 800 while the plate is active leaves the hidden vitals label unchanged (the vitals
    text has no kills in it, so kills and GV alone only prove the kills label).
  - Test 7 builds the `OwnerCombatView` with `Activator.CreateInstance(typeof(OwnerCombatView), true)` and sets the compiler's
    `<AsMilli>k__BackingField` (the recipe works on this editor). The text-scale step is a guard: it sets `ShowcaseSettings._textScale` to 1.25,
    calls `ApplyTextScale`, and checks the open tooltip after a roam move.
  - Test 12 (e): the line "an open header's marker survives a zero cell turning non-zero" is red on the base like the other open-header line.
  - Test 13: the 64 views carry the rig's inventory (the control measured zero on a correct implementation).
  - `AnIdleFrameStillAllocatesNothing` settles in the ramp state with a delivered window first, then measures the HUD's `LateUpdate` alone.
- **Hazard found while running subsets (pre-existing, not caused by this packet)**: in a Unity run that selects `TrainingConsoleIntegrationEditModeTests`
  together with about thirty other classes, `EscapeClosesTrainingConsoleAndOnlyTheNextPhysicalEscapeLeavesTraining` (or
  `HigherSettingsModalUnbindsConsoleUntilANewAcceptedSnapshot`) fails after ANY class that uses `AllocationMeter`. The existing
  `AllocationMeterEditModeTests` alone and the existing `AbilityRowNearbyNoGarbageEditModeTests` alone reproduce it on the base; the 32 existing
  classes without them are 335 of 335 green, and the new classes with `TrainingConsoleIntegration` alone are green. The cause was not found (the
  profiler Recorder is the common factor). The gate runs the whole suite in its own order and the base is green there; if the full gate shows only
  that Training failure on this branch, run `TrainingConsoleIntegrationEditModeTests` alone (green) and report it to the coordinator: it is not
  the implementer's defect and no existing test or oracle file may be edited for it.
- **Not run on purpose**: the whole EditMode suite and `scripts/gate.sh` (it is for the implementer); `hud-seams.test.cjs` (no product file changed
  in the oracle commits).
- **Counts for the implementer**: the stub leaves `MatchHud.Legacy.cs` at 374 lines (wc), `MatchHud.cs` at 571, `MatchHud.Features.cs` at 446. The
  throwaway added a net 10 lines to `MatchHud.Legacy.cs`, 0 to `MatchHud.cs` and 54 to `MatchHud.Features.cs` (the vitals writer, 17 lines, went to `MatchHud.Vitals.cs`): re-count after the last edit.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
