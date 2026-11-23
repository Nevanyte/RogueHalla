# P-UI-TABMAP-MARKS-1: the Tab map's marks and their labels are sized for the big map

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("When tab is clicked map layout is weird.")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-3` (the integration branch `feature/unity-rework` with P-UI-TABMAP-SHEET-1,
  P-UI-TOAST-LANES-1, P-UI-QUEST-TRACKER-1, P-UI-ABILITY-ROW-1, P-UI-TABMAP-SHEET-1-fix and P-UI-ALLOC-METER-1). Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-b`.
- **D-rules touched:** D1 presentation only (loose proof, see section 3 for the legacy comparison and the proposed DEVIATIONS row) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The Tab map and the minimap share one mark painter, `UI/TacticalMapMarks.cs`. Its sizes are fixed HUD units; the Tab map
multiplies some by 1.2 (`Large ? 1.2f : 1f` at `TacticalMapMarks.cs:79`, `:419`, `:420`, `:547`) and has other Large-only
constants. The minimap is about 270 units wide, the Tab map 384 to 784: marks made for a 270-unit map sit on a map up
to 2.9 times as wide, only 1.2 times larger. Every pooled mark label (camp clock `:335`, chest seconds `:405`, camp badge text
`:427`, contact chip text `:376`, ward countdown) is 9 units; zone names are 10 (`:301`). The HUD stage scale goes down to .8
(`HudLayout.ScaleMin`, `HudLayout.cs:29`), so a 9-unit label is 7.2 px at 1024 x 768 and 8.1 px at 1280 x 720.

When this lands, the whole marks layer of the Tab map (the mesh, every pooled label and icon, every offset between them)
is drawn `k` times larger as one unit, while it still covers the map exactly. **Not one number inside `TacticalMapMarks`
changes**: sizes, offsets, the 1.2 factor, `CampDropPx`, `SelfMarkerRect` and the clock-under-the-own-ring rule stay as
they are, in the layer's own units. The minimap is untouched (`k` = 1).

### 1.1 The scale: new `client-unity/RoguehallaNet/Net/Ui/MapMarkScale.cs` (pure)

```csharp
namespace Roguehalla.Net.Ui
{
    /// <summary>How much larger than its base size the marks layer of the Tab map is drawn ...</summary>
    public static class MapMarkScale
    {
        public const float ReferenceSide = 480f;  // a map this wide (HUD units) or less draws marks at base size
        public const float MaxGrowth = 1.4f;      // the most a wide map grows them
        public const float LabelUnits = 9f;       // the pooled mark labels' font size, in the layer's units
        public const float MinLabelPx = 10f;      // the smallest a mark label may be on screen
        public const float Max = 1.6f;            // a guard: never larger than this, whatever the inputs
        public static float Layer(float mapSide, float stageScale);
    }
}
```

`Layer = min(Max, max(clamp(mapSide / ReferenceSide, 1, MaxGrowth), MinLabelPx / (LabelUnits * stageScale)))`.
A `mapSide` or `stageScale` that is zero, negative or not finite gives exactly 1. `stageScale` is screen pixels per HUD
unit: `HudLayout.Viewport(w, h).Scale` (`HudLayout.cs:41`), .8 to 1.5. In play it is the panel's `lossyScale.x`, which
equals `HudStage.PixelsPerUnit`; it is not the stage's `localScale`, because `HudStage` divides the canvas factor out of that.

| Window | Stage scale | Map side (`L.Map.W`) | Growth | Floor | `Layer` | A 9-unit label on screen |
|---|---|---|---|---|---|---|
| 1280 x 720 | .9 | 624 | 1.300 | 1.235 | 1.300 | 10.5 px |
| 1920 x 1080 | 1.35 | 624 | 1.300 | .823 | 1.300 | 15.8 px |
| 1024 x 768 | .8 | 700 | 1.400 (1.458 capped) | 1.389 | 1.400 | 10.1 px |
| 2560 x 1440 | 1.5 | 784 | 1.400 | .741 | 1.400 | 18.9 px |
| 640 x 480 (stacked) | .8 | 384 | 1.000 | 1.389 | 1.389 | 10.0 px |

(Checked against `MapScreenLayout.Compute`: 1280 x 720 and 1920 x 1080 both give a 1422 x 800 stage and a map of 624;
1024 x 768 gives 1280 x 960 and 700; 2560 x 1440 gives 1706 x 960 and 784; 640 x 480 gives 800 x 600, stacked, and 384.)

### 1.2 The layer: `UI/TacticalMapSurface.cs`

```csharp
/// <summary>How much larger than its base size the marks layer is drawn (1 on the minimap).</summary>
public float MarkScale { get; private set; }   // starts at 1
public void SetMarkScale(float k);
```
`SetMarkScale` puts the marks object (created in `Initialize`, `TacticalMapSurface.cs:46-52`, named "Delivered contacts and
personal route") at `anchorMin = (.5 - .5/k, .5 - .5/k)`, `anchorMax = (.5 + .5/k, .5 + .5/k)`, offsets zero, pivot
(.5, .5), `localScale = (k, k, 1)`. Its rect is then the surface's size divided by `k`, centred, and after the scale it
covers the surface exactly at any surface size, with no code on resize. `k = 1` gives today's transform (anchors 0 to 1,
scale 1): the minimap is untouched. A value under 1 or not finite is treated as 1. A call with the value already set does
nothing (and allocates nothing). After a change it calls `LayerChanged()` on the marks (1.3). Pointer positions keep going
through the surface's own rect (`ScreenWorld`, `TacticalMapSurface.cs:95`): hit testing does not change.

### 1.3 Zone names follow the layer: `UI/TacticalMapMarks.cs`

The zone name labels live in their own host, "Zone labels", a sibling of the marks object under the surface
(`ZoneLabelHost()`, `TacticalMapMarks.cs:268-277`), but are positioned in the marks' coordinates (`ZoneAt(region)`,
`:265`; `RefreshZoneLabels`, `:290-312`). Whenever the host exists and the marks object has a parent (the host is then a
sibling), the host copies the marks object's `anchorMin`, `anchorMax`, offsets, pivot and `localScale` when it is created
and at the start of every `RefreshZoneLabels()`. A marks object with no parent (the standalone fixtures of the existing
tests) keeps its host full-size under itself, as today (that is the `transform.parent != null ? ... : transform` line at
`:272`). A new `public void LayerChanged()` calls `RefreshZoneLabels()` and `SetVerticesDirty()` and adds one to a new
`public int LayerChanges { get; private set; }`, which the tests read; the surface calls it from `SetMarkScale`. Nothing
else in this file changes. Zone names grow with the layer (10 units become 14 at k = 1.4) and sit over contacts: say so
under RISKS in the hand-off. `OnRectTransformDimensionsChange` (`:135`) already calls `RefreshZoneLabels()`: with anchored
fractions the layer follows the surface by itself, and the host follows the layer there.

### 1.4 Who sets it: `UI/TacticalMapController.cs`

`ApplyLayout` (`TacticalMapController.cs:253`) returns early when the stage size, the mode and the text scale are unchanged
(`:260`), and every 16:9 window from 1138 x 640 up has the same 1422 x 800 stage with a different pixel scale. So the pixel
scale joins the layout cache key. In `ApplyLayout`, right after `SyncStage()` (`:256`), read `float px = _panel.lossyScale.x`
(the panel's pixels per HUD unit; with no stage given, the panel's own). A new field `_layoutPx`, set to -1f where
`_layoutScale` is (declared `:74`, reset `:191`), is compared with `Mathf.Approximately(px, _layoutPx)` beside `_layoutScale`
in the early return and written with the other key fields (`:261`). Straight after `ApplySheet(L, tab && !L.Stacked)`
(`:267`; defined in `TacticalMapController.Sheet.cs:64`) it calls `_large.SetMarkScale(MapMarkScale.Layer(L.Map.W, px))`.
That covers the Tab map and the planning screen alike (both place `_publicMap` through `ApplySheet`,
`Sheet.cs:68`). `ApplyViewport`, `RefreshMinimapVisibility` and the minimap surface are not touched; the minimap surface
is never given a scale. `ApplyLayout` runs from `Tick`, `SetOpen` and `ReadUiInput` while the panel is shown, so a
canvas scale that was stale on the first frame corrects itself on the next. Put the new line after `ApplySheet`, not by
line number (the numbers above are those of the base).

## 2. Dependencies

The base above (P-UI-TABMAP-SHEET-1 and its fix: `LayoutOwnsRect`, `ApplySheet`, `MapScreenLayout`; P-UI-ALLOC-METER-1:
`Assets/Tests/Editor/AllocationMeter.cs`, present on the base, verified).

## 3. Relevant legacy paths

- `packages/client/src/ui/planningMath.ts:148-151`: `glyphScale(size) = round(clamp(size / 640, .55, 1.25) * 100) / 100`:
  legacy marks **grow with the map** (`--pl-glyph`, written at `planning.ts:524-525`), camp markers are `22px * glyph`
  (`planning.css:216-217`), elite `30px * glyph` (`:229`), text `max(9px, 11px * text * glyph)` (`:200`) and
  `max(9px, 10.5px * text * glyph)` (`:330`). So legacy has a 9 px text floor and marks proportional to the map, from .55
  to 1.25 of base. `docs/rework/LEGACY_LAYOUT.md` section 13 ("Map internals") states the base sizes (camp 22, elite 30, reward badge 9, actors 12).
- This packet differs on purpose: the reference is 480 not 640 (growth from 1 to 1.4 over 480 to 672 units, never below
  base) and the text floor is 10 px not 9. **Propose a DEVIATIONS row in the hand-off** (the owner signs it): "Tab map marks
  grow with the map from 480 to 672 units and never shrink; mark text has a 10 px floor (legacy: glyph .55-1.25 of the map
  over 640, 9 px floor)".
- Owner decisions: `docs/OWNER_DECISIONS.md` has no row on map mark sizes (searched "mark", "Tab map", "glyph").

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/MapMarkScale.cs` and its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/MapMarkScale.cs` (+ `.meta`)
- `client-unity/RoguehallaNet.Tests/MapMarkScaleTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapMarksEditModeTests.cs` (+ `.meta`)

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/MapMarkScale.cs` (create the body; the stub exists), its synced copy and `.meta`
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapSurface.cs` (`MarkScale`, `SetMarkScale`)
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs` (the zone-label host's transform, `LayerChanged`, `LayerChanges` only)
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs` (the `_layoutPx` field and its place in the key, the `px` read and the one `SetMarkScale` call, all inside `ApplyLayout`)
- Not in scope: `RoguehallaNet/Net/Ui/MapScreenLayout.cs`, the capture tools under `UnityProject/Tools/` (they copy the frame and map rect maths, which do not change), any mark size, glyph or colour, `ApplyViewport`, `CardDisplayCatalog.cs`, `BakedContent.cs`.

## 6. Contracts and references

- Presentation only (R-AUTH): no game rule, no wire, no `crates/`. Lane rules: `.agent-runs/lane-ui-rework/specs/COMMON.md` (HUD stage units, theme colours, text through `HudUi.Label`, readability gate: a `/// <summary>` on the new type, at most 800 lines per file).
- `TacticalMapMarks.cs` is 685 lines, `TacticalMapSurface.cs` 210, `TacticalMapController.cs` 715: stay under 800.
- `MythicTextScale` (text-size setting, fit-in-box shrink) keeps working inside the layer's units.

## 7. Behaviour to preserve

- The minimap: marks object anchors (0,0)-(1,1), zero offsets, scale 1, never given a scale.
- Every number inside `TacticalMapMarks` (sizes, offsets, `CampDropPx`, the 1.2 factor, `SelfMarkerRect`).
- Hit testing (`ScreenWorld` uses the surface's own rect); the marks object is not a raycast target (`TacticalMapSurface.cs:52`) and the scaled layer must not become one.
- The surface's `Rebuild`, the fog and terrain images and the planning `SpawnMarker` buttons are siblings of the marks and are not scaled.
- Do not touch any `fontSize` to reach the floor: the transform does it (SDF text stays sharp when scaled).
- `HudStage` keeps the scale between .8 and 1.5, so `Max` never binds in play; it is a guard for odd inputs.

### Existing tests that pin this code (read on the base; none changes, no `oracle-amend:` commit is needed)

| Test | What it asserts | Why unaffected |
|---|---|---|
| `MapScreenEditModeTests.MarksDrawZoneIconsOnlyWhenTheMapCarriesRegionsAndLabelOnlyTheLargeMap` (`MapScreenEditModeTests.cs:109`) | on a bare marks object with a region map: 6 zone icons, 6 TMP texts under the host; the minimap has none | bare object, no parent: host stays under it (`:272` branch kept) |
| `HudVisualFix2EditModeTests.ZoneLabelsDrawAfterTheMarksMeshAndTheVisionFogAsTheirOwnLaterSibling` (`:224`) | six zone labels, each a direct-child subtree of the surface, after marks and fog in sibling order | the host stays a later sibling of the marks under the surface |
| `HudVisualFix2EditModeTests.ZoneLabelsCarryADarkOutline...` (`:245`) | outline material on each `ZoneLabel` | material untouched |
| `TabMapVisualFix3EditModeTests.AClearedCampsClockIsClearOfTheOwnRing...` (`:90-120`) | clock box (`CampClock0`, anchored pos and size) vs `SelfMarkerRect`, both in the marks' own space, on a bare marks object | compares inside the layer's own units; scale 1 |
| `TabMapFlowEditModeTests.AssertMarkerFollowsDrawn` (`:389-407`) | `SelfMarkerRect.center` equals the projection through `marks.rectTransform.rect` within .5 | both in the layer's own space; stays true at any `k` (it is built by the controller, so `k` > 1 there, but both sides are local) |
| `DeclutterEditModeTests` (`:33-113`) | counts of camp glyphs, clocks, badges, wards on bare marks objects | bare objects; counts only |
| `TacticalMapEditModeTests` (`:162-260`) | `CampClocksShown`, `ChestBadgeCount`, `ContactLabelCount` / text through the controller | counts and strings, not sizes |
| `TabMapSheetEditModeTests` (`TheLiveRectsEqualTheLayout` etc.) | the live rects of the sheet parts equal `MapScreenLayout` (the `PublicMap` rect, not the marks) | `PublicMap`'s rect is not scaled; only its child marks object is |

No conflict between the packet and an existing test was found. If the implementer finds one the quick gate reports,
it stops and reports (`blocked`); it does not edit the test.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-MARKS-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter MapMarkScaleTests` green (4 tests).
- Unity EditMode class `TabMapMarksEditModeTests` green (13 test cases: see Oracle files), and the classes listed in section 7 stay green.
- `bash client-unity/sync-net.sh --check` clean.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green (no `MatchHud` member is touched).

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/MapMarkScaleTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapMarksEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapMarksEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-TABMAP-MARKS-1.md`

(The `stub:` commit holds `MapMarkScale.cs` and its synced copy and `.meta`, and the no-op `MarkScale`,
`SetMarkScale`, `LayerChanged`, `LayerChanges`: the implementer replaces them.)

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter MapMarkScaleTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TabMapMarksEditModeTests,TabMapSheetEditModeTests,HudVisualFix2EditModeTests,TabMapVisualFix3EditModeTests,TabMapFlowEditModeTests,TacticalMapEditModeTests,DeclutterEditModeTests,MapScreenEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-MARKS-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Sonnet. Small, local change (one pure function, three short edits). The risks are in the transform (anchors that keep the
scaled layer exactly on the map), the layout cache key (the pixel scale must be in it, or a 16:9 resize leaves a stale
scale), and the zone host following the layer. Zone names grow with the layer (10 units become 14 at k = 1.4) and sit
over contacts: report it under RISKS.

## 11. Known traps

- Do not change any number in `TacticalMapMarks` and do not touch `fontSize`.
- `px` is the panel's `lossyScale.x`, not `_stage.localScale` (`HudStage` divides the canvas factor out of the latter).
- The anchor write on the marks fires `OnRectTransformDimensionsChange` before the scale is set: that is why `SetMarkScale` calls `LayerChanged()` afterwards (test 6 pins it).
- `ApplyLayout` early return: without `_layoutPx` in the key, a 16:9 window change (same 1422 x 800 stage) does not re-run it (test 8, step 2).
- A steady `SetMarkScale(sameValue)` must return before touching any transform or calling `LayerChanged` (no allocation; test 11, and `LayerChanges` unchanged, test 10).
- Time in EditMode does not advance: the tests use `TacticalMapController.SecondsClock`.
- Tests are a separate assembly with no `InternalsVisibleTo`: `MarkScale`, `SetMarkScale`, `LayerChanged`, `LayerChanges` are `public`.
- Keep the packet branch linear; the implementer merges `packet/P-UI-TABMAP-MARKS-1` into its own branch, not the reverse.

## Notes from the oracle author

(Written with the oracle commit.)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
