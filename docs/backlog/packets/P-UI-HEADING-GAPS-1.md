# P-UI-HEADING-GAPS-1: headings in the display font keep the gap between their words

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the UI rework lane's chat ("When tab is clicked map layout is weird"); seen in Web round 1 (2026-11-23): the Tab map title reads TACTICALMAP, its right column's heading QUESTS &TRADERS.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head 632ecb349). Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-r`.
- **D-rules touched:** D1: legacy has no such face (nothing under `packages/` uses Cinzel; its page font is `system-ui`) and its Tab map has no title (`planning.ts:581` hides the head in map mode): no legacy word gap to match, no DEVIATIONS row. D2 n/a. D5/D6/D9 none (font data: no rule, no wire).
- **Gate:** `CinzelDecorative.asset` is on the gate's build-artefact list (`scripts/gate.sh`, `ARTEFACT_RE`). EVERY gate command of this packet carries `--allow-artefacts`: `scripts/gate.sh --quick --oracle packet/P-UI-HEADING-GAPS-1 --base lane/ui-rework/base-3 --allow-artefacts`. The coordinator's gate on the merged tree needs `--allow-artefacts` for CinzelDecorative.asset too (hand-off line). The asset is the only artefact in the diff.

## 1. ID and outcome

HUD and launch headings set in the display font (Cinzel Decorative) keep a visible gap between their words. Today the face's capitals have swash feet and tails that run past their advance, the static atlas has no glyph pair adjustments (`CinzelDecorative.asset` line 2651: `m_GlyphPairAdjustmentRecords: []`), and a space is only 0.25 em: the L of TACTICAL MAP overhangs its advance by 0.525 em, so the ink gap L|M is 18 - 4.83 - 37.80 = -24.63 units = -0.342 em (the foot runs under the M); the ampersand before T gives -0.165 em. After the packet, at every word boundary of every Display string in the Oracle table the ink gap is between 0.1 and 0.6 em, by adding 15 pair records (glyph then space) to the asset. No string and no C# changes. P-UI-HEADING-GAPS-1 lands as one edited asset line plus the new test file.

`HudUi.Label` sets `label.font = UiTheme.Current.Font(role)` (`HudUi.cs:138-145`); for `UiFontRole.Display` that is `Resources/Showcase/CinzelDecorative` (`UiTheme.DisplayFontResource`, `UiTheme.cs:58`; `Font` at `UiTheme.cs:78-88`, falls back to Nunito when the resource is missing). The asset is static (point size 72, 1000 units per em, 95 ASCII glyphs, `m_AtlasPopulationMode: 0` line 107, `m_ShouldReimportFontFeatures: 0` line 2654). `Editor/UiFontBuilder.cs` builds only the Nunito assets, so nothing regenerates this asset. TMP applies a pair whose second glyph is a space: `TMP_Text.cs` 4300-4313 adds the first record's `xAdvance` to the first glyph when the next element is a Character, key `second << 16 | first` (`TMP_FontAsset.cs` 1007); the lookup is built from the serialized list when the asset loads.

### Numbers (the spec's, recomputed from the asset's glyph table on 2026-11-23)

`CinzelDecorative.asset` holds exactly these 15 glyph pair records, in this order (first glyph index ascending, as `SortGlyphPairAdjustmentRecords` sorts): first glyph as listed with the listed `m_XAdvance`, second glyph index 3 (the space) with all values 0, every other value 0, `m_FeatureLookupFlags` 0. Rule: every glyph whose ink overhangs its advance by more than 0.06 em (4.32 units) gets value = overhang + 0.05 em (3.6 units), 2 decimals. Entries, glyph index, overhang (1/1000 em), overhang (asset units), xAdvance written:

    & 9 326 23.484 27.08 | A 36 234 16.859 20.46 | G 42 271 19.516 23.12
    K 46 290 20.875 24.48 | L 47 525 37.797 41.4 | M 48 181 13.031 16.63
    N 49 214 15.406 19.01 | Q 52 636 45.781 49.38 | R 53 296 21.313 24.91
    U 56 121 8.719 12.32 | V 57 251 18.078 21.68 | W 58 251 18.078 21.68
    X 59 146 10.516 14.12 | Z 61 360 25.922 29.52 | q 84 461 33.188 36.79

Decided: the room after the overhang is 0.05 em (not the evidence's 0.04), so the tightest gap after a kerned glyph (N) clears the 0.1 em floor by .013 em.

- The gap after a kerned glyph is the same for all 15: ink gap = (21.6 + bearingX of the next glyph) / 72 em. Before each capital: A .291, B .301, C .345, D .200, E F G H K L O R Z .360, I .251, J .217, M .233, N .113, P .359, Q U .350, S .342, T .211, V W .291, X .129, Y .260; before & .364, before a digit .335 to .367. Smallest: N (.113) and X (.129); no ASCII glyph after a kerned one gives more than .42 em.
- At every word boundary of every string in the Oracle table, built as the client builds it, the ink gap is 0.1 to 0.6 em. Hand-computed after the change: smallest .142 (LOBBY MAP Y|M, unchanged), largest .540 (bold CONTROLS & GUIDE &|G; before .530; RESUME A SAVED RUN D|R stays .539).
- A boundary whose first glyph is not in the table does not move: MAP KEY P|K .355, LOBBY MAP Y|M .142, START RUN T|R .468 (bold), THE ARENA E|A .385 (bold) stay within 0.01 em.
- The asset's diff is one line replaced by 226 (the key line and 15 records of 15 lines). Its `.meta` and every other file under `Showcase/` are untouched.

### What else moves (widths in HUD units at the built size, bold and spacing included, against 88 percent of the box; `MythicTextScale` shrinks only above that)
TACTICAL MAP (20) 152.2 to 163.7 (header 1156; 392 at 640 x 480); PLAN YOUR RUN (20) 166.6 to 178.8 (title 228 at 640 x 480, see Risks); IN VIEW, WEAPON COUNTERS, QUESTS & TRADERS (13) 54.0 to 57.5, 139.3 to 142.7, 129.6 to 134.5 (rail box 226); YOU DIED (44 bold, spacing 6, set at `MatchHud.Legacy.cs:277`) 277.0 to 284.6 (box 380, limit 334: at text scale 1.17 it now shrinks, was 1.21); CONTROLS & GUIDE (17 bold, spacing 2) 203.4 to 209.8 (box 400); SIGN-IN & ACCESS, RESUME A SAVED RUN, RUN RESULTS (20 bold, 1.5) 219.6 to 232.4, 256.4 to 262.1, 152.4 to 157.7 (boxes 376, 416, 456); hub WEAPON COUNTERS (18 bold) 211.7 to 216.5 (box 280); TRAINING CONSOLE (`MythicUi` heading, argument 20, built 30, autosizes) 318.7 to 328.3. All fit: no string is cut. One label with content-decided text is not in the table: the quest reward choice title (`MatchHud.QuestRewards.cs` lines 69 and 324, a `MythicUi.Text` heading, built 30; "Choose a quest reward" or a quest name and reward). It widens for any name with a table glyph before a space; it wraps and autosizes down to 0.72, so none is cut. `LaunchUi.Text` sets Truncate on Display labels; every width is inside its box.

## 2. Dependencies
none. Base `lane/ui-rework/base-3` (took P-UI-QUEST-OFFER-1, which touches none of this packet's files).

## 3. Relevant legacy paths
- `packages/` has no Cinzel and no matching Tab map title: the legacy page font is `system-ui`; `planning.ts:581` hides the head in map mode. Nothing to match (D1 above).
- Client code (read only): `Scripts/UI/HudUi.cs:138-145` (Label), `Scripts/UI/UiTheme.cs:58,78-88`, `Scripts/UI/TacticalMapController.cs:102,105` (heading and PlanTimer), `Scripts/UI/MatchHud.Legacy.cs:270-278` (CreateDeathLine, `characterSpacing = 6f` on line 277), `RoguehallaNet/Net/Ui/MapScreenLayout.cs:27,120` (planning title 40 high).

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/DisplayFontGapsEditModeTests.cs` and its `.meta` (oracle author; no product code is added).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Resources/Showcase/CinzelDecorative.asset`: the one line (Outcome above): the implementer's only product change and the only artefact in the diff.
- Not in scope: all C# under `Assets/Scripts` (`UiTheme.cs`, `HudUi.cs`, `LaunchUi.cs`; the Tab map controller and marks belong to other packets), the Nunito assets, `BakedContent.cs`, `ShaderGraphSettings.asset`, the Cinzel `.meta`, `Tools/editmode.sh`, `scripts/gate.sh`, the oracle files.

## 6. Contracts and references
- Presentation only (lane rules, COMMON.md); R-AUTH: no game rule. No wire change.

## 7. Behaviour to preserve
- Every Display string with no table glyph before a space keeps its width (Outcome: boundaries not in the table stay within 0.01 em).
- No `wordSpacing`, `characterSpacing` or string change; kern no Y and no lowercase glyph but q.
- Existing tests that measure these labels stay green (list below).

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-HEADING-GAPS-1 --base lane/ui-rework/base-3 --allow-artefacts` prints GATE PASS (run through `in.sh`).
- `git diff --stat lane/ui-rework/base-3 -- client-unity/UnityProject/Assets/Resources` shows one file, 226 insertions, 1 deletion.
- `unity-class.sh <worktree> DisplayFontGapsEditModeTests` all green.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/DisplayFontGapsEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/DisplayFontGapsEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-HEADING-GAPS-1.md`

## Oracle: `DisplayFontGapsEditModeTests` (what each test asserts)
SetUp/TearDown copy `ChampionRowNameFitEditModeTests` (save and set `UiTheme.Current = UiTheme.Placeholder()`, a canvas; destroy and restore) without a `MatchHud`. A case builds `HudUi.Label(HudUi.At(canvas, "Box", 0,0,1200,80), "Text", text, size, Accent, MidlineLeft, bold, UiFontRole.Display)`, sets `characterSpacing`, `ForceMeshUpdate()`, reads `textInfo.characterInfo`. A boundary is a space with non-space neighbours; `inkEnd = last.origin + (bx + w) * last.scale`, `inkStart = next.origin + bx * next.scale`, `gapEm = (inkStart - inkEnd) / (pointSize * next.scale)`. No time is involved; no test writes the font asset.
1. `EveryWordBoundaryKeepsAGapBetweenATenthAndSixTenthsOfAnEm`, one case per row: font name is "CinzelDecorative", boundary count equals the row's, every gap in [0.1, 0.6]. Rows (text, size, boundaries): TACTICAL MAP 20 (1); LOBBY MAP 20 (1); PLAN YOUR RUN 20 (2); MAP KEY 13 (1); IN VIEW 13 (1); WEAPON COUNTERS 13 (1); QUESTS & TRADERS 13 (2); YOU DIED 44 bold spacing 6 (1); START RUN 17 bold (1); CONTROLS & GUIDE 17 bold spacing 2 (2); ACCOUNT COSMETICS 27 (1); THE FIELD GUIDE 27 (2); QUEST JOURNAL 27 (1); TRAINING CONSOLE 20 (1); NEARBY QUEST ACTIONS 18 (2); RESUME A SAVED RUN (3), SIGN-IN & ACCESS (2), RUN RESULTS (1) at 20 bold spacing 1.5; WEAPON COUNTERS 18 bold (1); THE ARENA 17 bold (1); Missing in action 26 bold (2); No result 26 bold (1); Chest 1 / 3 at 30 spacing 2 (3). Red today (hand-computed em): TACTICAL MAP -0.342, QUESTS & TRADERS -0.165, PLAN YOUR RUN -0.004, IN VIEW 0.027, TRAINING CONSOLE 0.024, WEAPON COUNTERS 13 0.081, SIGN-IN & ACCESS 0.085; the others are green on the base and guard the 0.6 limit and the count.
2. `ThePairTableHoldsExactlyTheSpecsRecords`: `font.fontFeatureTable.glyphPairAdjustmentRecords` has Count 15; each table entry exists with xAdvance within .01, other value fields 0, second glyph index 3 all zero, flags 0; then the rule over `font.characterTable` (overhang > 0.06 * pointSize has a record with overhang + 0.05 * pointSize). Red on the base (Count 0).
3. `AGlyphWithoutAFootKeepsItsGap`: MAP KEY 13 .355, LOBBY MAP 20 .142, START RUN 17 bold .468, THE ARENA 17 bold .385, each within 0.01 em. Green on the base (guards against a blanket widening and kerning Y).
4. `ThePlanningHeadingLeavesRoomForTheWidestTimer`: `MapScreenLayout.Compute(640, 480, Planning).Title.W` is 228; heading "PLAN YOUR RUN" 20 and timer "40s" 26, both Display, in a 228 x 40 box as `TacticalMapController.cs:102,105` builds them; both fonts "CinzelDecorative"; fontSize 20 and 26; heading width 178.8 within 1.0 (red on base: 166.6); then `Wide(heading) + Wide(timer) <= 228`.

## Existing tests and tools that measure these labels (none turns red; no `oracle-amend`)
- `TabMapSheetEditModeTests.TheHeaderDropsWhatDoesNotFit` (`TabMapSheetEditModeTests.cs:256`, with `AssertHeaderFits` line 291 and `Wide` line 176): the phase and hint rule reads the heading's preferred width, so a wider heading moves both sides together; the 1280 x 720 case, where heading, phase and hint all show, must still hold with the heading 11 units wider.
- `TabMapHeaderNoGarbageEditModeTests.AnUnchangedHeaderAllocatesNothing` (line 108) and `AChangeStillReachesTheHeader` (line 126): `AllocationMeter.AssertNone`; a pair lookup is a dictionary read.
- `HudTextScaleEditModeTests` (DeathLine at line 197): the fit is computed, no width fixed.
- `TabMapFlowEditModeTests` (title not empty), `DeclutterLegendEditModeTests`, `TabMapVisualFix3EditModeTests` (vertical geometry), `HudLegacyLayoutEditModeTests`, `LaunchScreenEditModeTests`, `HubAtriumViewEditModeTests`, `MatchSettlementEditModeTests`: no width asserted. No `Tools/*.test.cjs` copies the font or these widths.

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-r DisplayFontGapsEditModeTests` (and the existing classes above).
- The asset is one 2 MB line with LF endings: edit it only with this script (`python3 add.py <asset>`), not with an editor that rewraps.

```python
import sys
rows = [(9, 27.08), (36, 20.46), (42, 23.12), (46, 24.48), (47, 41.40), (48, 16.63), (49, 19.01), (52, 49.38),
        (53, 24.91), (56, 12.32), (57, 21.68), (58, 21.68), (59, 14.12), (61, 29.52), (84, 36.79)]
V = "        m_GlyphValueRecord:\n          m_XPlacement: 0\n          m_YPlacement: 0\n          m_XAdvance: %s\n"
REC = ("    - m_FirstAdjustmentRecord:\n        m_GlyphIndex: %d\n" + V + "          m_YAdvance: 0\n"
       "      m_SecondAdjustmentRecord:\n        m_GlyphIndex: 3\n" + V % "0" + "          m_YAdvance: 0\n"
       "      m_FeatureLookupFlags: 0\n")
text = open(sys.argv[1], newline="").read()
old = "    m_GlyphPairAdjustmentRecords: []\n"
assert text.count(old) == 1
new = "    m_GlyphPairAdjustmentRecords:\n" + "".join(REC % (g, "%g" % v) for g, v in rows)
open(sys.argv[1], "w", newline="").write(text.replace(old, new))
```

## 10. Risk and suggested model tier
implementer-sonnet: one scripted edit of a data line; the risk is the artefact handling (traps) and the planning title (below).
- The planning title and `PlanTimer` share one box (228 wide at 640 x 480). The heading now ends at 178.8; a two-digit timer ("40s", 44.9 at 26) fits with about 4 units to spare (16 before), a three-digit one ("100s", 56) would meet it. Both scale with the text-size setting (`MythicTextScale`, 0.85 to 1.5): the kern moves their collision from a text scale of about 1.08 to about 1.02 at widths under 900. The lane accepts that; test 4 pins the margin at scale 1. PLAN YOUR RUN shrinks from text scale 1.12 (was 1.20).
- Residual: Y, a, y, x, k, v, w, r (overhang 0.011 to 0.041 em) before a word starting with N or X leave 0.02 to 0.06 em; no client string has such a pair.
- Not established: how it looks (the lane's next Web round judges the headings: Tab map title at 1920 x 1080, right rail); a planning phase over 99 s; the planning screen at 640 x 480 and a text scale above 1; that the Web player applies the pair table as the editor does; widths of dynamic Display text (quest names in the reward choice title) beyond "none is cut".

## 11. Known traps
- **COMMON.md says to restore Showcase font assets with `git checkout --`. Not this one.** Commit the edit alone (`P-UI-HEADING-GAPS-1: pair adjustments in the Cinzel asset`) BEFORE the first Unity run. `unity-class.sh` and `editmode.sh` revert a tracked file that was clean before a run and dirty after: with the edit committed, a Unity rewrite is reverted to the commit and the script prints `restored CinzelDecorative.asset`; if that line appears, say so in the hand-off (the edit is then not stable). Uncommitted, the edit is not protected.
- Before each commit `git status --short` shows the asset and nothing else of `Showcase/`: a Unity run rewrites `NunitoBold.asset` and `NunitoRegular.asset` and may touch `BakedContent.cs`, `ShaderGraphSettings.asset` and the two Coliseum art assets: restore them. Add files by name.
- Gate flag `--allow-artefacts` on every run, and on the coordinator's merged-tree gate.
- Why the edit survives: the asset is static, TMP rebuilds the pair lookup from the serialized list at load and re-imports features only for dynamic assets, and no commit has touched the file since 2026-11-08.
- Bold labels add `boldSpacing` (7, 0.07 em) after every character and `characterSpacing` its value per 100 em: each widens a boundary by twice that, so each test row is built exactly as its row says.
- A Display string added later is not in the table: add a row. P-UI-PLAYER-WORDS-1 may rename the Tab map headings; the table holds fixed copies of the strings and tests the font.
- The look is not tested (gaps are outline distances; the SDF atlas has 8 units of padding).

## Notes from the oracle author
- No `stub:` commit: the tests use only existing public members (no product signature is added).
- Red on the base (run with `unity-class.sh`, 2026-11-23): `EveryWordBoundary...` red for TACTICAL MAP (-0.342 em), PLAN YOUR RUN (-0.004, 0.014), IN VIEW (0.027), WEAPON COUNTERS 13 (0.081), QUESTS & TRADERS (-0.165 at the second boundary), TRAINING CONSOLE (0.024), SIGN-IN & ACCESS (0.085): all on the `Is.InRange(0.1, 0.6)` assertion, values equal the hand-computed ones. The other 16 rows are green. `ThePairTableHoldsExactlyTheSpecsRecords` red on "exactly 15 pair records" (Count 0). `ThePlanningHeadingLeavesRoomForTheWidestTimer` red on the heading width (166.65, wants 178.8 within 1); its last line (sum <= 228) is green on the base (211.5), as the spec says (16 units of room before, about 4 after). `AGlyphWithoutAFootKeepsItsGap` green on the base.
- Scratch run 1 (catch-and-collect, not committed, the scratch class deleted): every soft failure is one of the assertions above (gap range, record count, per-glyph record, rule record, heading width); the table test collected 31 = 1 count + 15 records + 15 rule glyphs, so the rule set on the asset is exactly the packet's 15 glyphs. No other exception: every line of every test ran.
- Scratch run 2: the asset edited by the packet's script and committed temporarily (then dropped with `git reset --hard HEAD~1`; the tree and the asset are clean against the base), both classes: 58 of 58 green, so every row is in 0.1 to 0.6 em, the four untouched gaps are within 0.01 of the spec, the heading is within 1.0 of 178.8 and heading plus "40s" fit 228. The individual gap values and widths were not printed, so a deviation under the test's own bands (for example from the hand-computed bold and spacing terms) is not reported.
- Existing classes run on the stub, all green: TabMapSheetEditModeTests, TabMapHeaderNoGarbageEditModeTests, HudTextScaleEditModeTests, TabMapFlowEditModeTests, DeclutterLegendEditModeTests, TabMapVisualFix3EditModeTests (49 of 49). HudLegacyLayout, LaunchScreen, HubAtriumView, MatchSettlement were not run (they assert no width, and the asset is unchanged on the stub); the implementer runs them after the edit.
- `ThePairTableHoldsExactlyTheSpecsRecords` reads `GlyphPairAdjustmentRecord` (struct, `UnityEngine.TextCore.LowLevel`) from `font.fontFeatureTable.glyphPairAdjustmentRecords`; flags are compared through `featureLookupFlags`.
