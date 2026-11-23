# P-UI-ZONE-NAMES-1: a zone's name on the map stays clear of the marks in that zone

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane: "When tab is clicked map layout is
  weird"); Web round 1 found HAUNTED and HOLY crossed by giver and camp marks.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-3` (head 632ecb349; it took P-UI-QUEST-OFFER-1, which touches none of this
  packet's files). Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-a`.
- **D-rules touched:** D1: legacy moves a zone name off the camp boxes (`packages/client/src/ui/planningMath.ts:223`
  `placeLabels(wishes, obstacles, size)`, called from `packages/client/src/ui/planning.ts:705-725` `relayout`): the nearest
  free spot on rings of 12 to 120 px, inside its zone, run when the layout changes and never per frame; quest givers are
  not obstacles there. This packet differs: six fixed spots, more obstacles, no inside-zone test. Proposed
  `DEVIATIONS.md` row (the coordinator numbers it, the owner signs it; agents do not edit `docs/DEVIATIONS.md`):
  "legacy places zone names with a larger rule that keeps them inside the zone; Unity picks one of six fixed spots
  around the zone icon that overlaps no camp, ward, chest, delivered point or map edge, and may leave the zone".
  D2 n/a. D5/D6 none. D9 untouched (presentation only). `docs/OWNER_DECISIONS.md` has no row on map zone names.
- Evidence: `.agent-runs/lane-ui-rework/understand/web1-tabmap-zone-names.txt` (Option A), the picture
  `20-tabmap-held.png`, `docs/backlog/packets/P-UI-TABMAP-MARKS-1.md`.

## 1. ID and outcome

Today `TacticalMapMarks.RefreshZoneLabels()` (`client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs:306-329`)
puts each of the six names at `ZoneAt(region) + (0, -24)` layer units (line 327): one constant, no read of any mark. It runs
from `SetMap` (:108-117), `LayerChanged` (:129) and `OnRectTransformDimensionsChange` (:150), never from `Present` (:161),
so a giver that arrives later cannot change a name. The layer is drawn `k` (1.3 to 1.4) times larger (P-UI-TABMAP-MARKS-1)
while a world point keeps its place, so in layer units marks sit closer to a name. In the picture (`20-tabmap-held.png`)
HAUNTED has two giver diamonds (19.6 units square), a camp glyph with its "+3" and a "+3%" on or just above it; HOLY has
a camp shield and a "+3%" on it. The class is `sealed` and 702 lines (a C# file above 800 fails the repository). Only
`Large` marks (Tab map, planning map) draw names; the minimap draws none (`MapScreenEditModeTests` pins it).

After this packet, in layer units (the marks object's space, y up; `k` times larger on screen) a pure function chooses
each name's place among six spots, today's first. No mark moves; none is hidden. The picture's givers lie 30 to 50 layer
units from their zone's icon, so in a real match names will usually move off spot 0; the hand-off says so.

### 1.1 Six spots (`MapLabelPlacement.Spot`)

I = `ZoneAt(region)`. The name box `w x h` is the name label's own measured size (rule 1.9), `hw = w / 2`, `hh = h / 2`;
`drop = max(24, 17 + hh)` (24 while `h` is 14 or less). Centre offsets from I: 0 below `(0, -drop)` (today's `(0, -24)` while
`h` is 14 or less), 1 above `(0, drop)`, 2 right `(17 + hw, 0)`, 3 left `(-(17 + hw), 0)`, 4 below right `(17 + hw, -20)`,
5 below left `(-(17 + hw), -20)`. No spot touches the 26 x 26 icon box (gap 4 or more). Worked, I = (100, 100), `w` 58,
`h` 14, rects `(X, Y, W, H)` (Y is the bottom edge, y up): 0 `(71, 69, 58, 14)`, 1 `(71, 117, 58, 14)`, 2 `(117, 93, 58, 14)`,
3 `(25, 93, 58, 14)`, 4 `(117, 73, 58, 14)`, 5 `(25, 73, 58, 14)`; with `h` 30: 0 `(71, 53, 58, 30)`, 1 `(71, 117, 58, 30)`.

### 1.2 The marks a name must clear (one list for all six zones; `Choose` ignores the far ones)

`s = 1.2`, `p = At(world)` (`TacticalMapMarks.cs:274-278`); a camp under a contact still counts (a contact moves; `ContactNear`
:331 is not used). Each entry is either a drawn shape of `OnPopulateMesh` (:560-654, box sizes copied from it) or a label (its
measured size, rule 1.9).

- Camp rows (`_camps`; `DrawCamps` :540-558, `RefreshBadgeTexts` :464-488, `RefreshCampClocks` :339-372): box `17 s` at `p` if
  `IconKey != null` (:549) else `6 s + 4` (:550, `Marker` adds 4); elite: box `6 s + 4` at `p + (0, 13.5)` (:555, `IconSize * .5
  + 5 s` = 7.5 + 6); label `AmountText`: align -1, `x = p.x + 21.2`, `cy = p.y + 2` (`RewardSlot` :436 = `11 s`, + 8 at :475);
  label `EliteText`: same `x`, `cy = p.y + 2 + (AmountText != null ? 14.4 : 0)` (`row * 12 s`, :476); label `Clock` that
  `ChestBadges.Replaces(_chestRows, Index)` (`Net/Ui/ChestBadges.cs:69`) does not replace: align 0 at `p + (0, -16)` (:360).
- Wards (`_wards`; :478-485, `DrawBadges` :527-537): box `8 s + 4` at `p + (-13.2, 2)` (`WardSlot` :437); label `Countdown`:
  align 1, `x = p.x - 21.2`, `cy = p.y + 2`.
- Chests (`_chests`, :591): box `11 s + 4`; chest seconds (`_chestRows`, :589-590, `ChestTextAt` :410): the mesh box
  `(len * 6 + 8) s` by `12 s` at `p + (0, -15)` and the seconds label's measured box at the same centre.
- Delivered points (`_delivered`, :592-618) inside the layer rect: box `size * s + 4`, size Objective 11, TrackedObjective 13,
  PublicObjective 14, QuestReward 9, RebirthEgg 10, HatchingEgg 14, Crown 16, Smuggler 14, Fort 16, Meditation 15, Bounty 16,
  Giver 13, Merchant 13 (the sizes of `OnPopulateMesh`, in one private `DeliveredSize(PointKind)`).
- Plus the four edge slabs of the layer rect (`AddEdges`): a spot off the map is overlapped.
- Left out: contacts, chips, marked holders, quest carriers, the own ring, ping, route, zone icons, the engaged ring, other
  names, the planning spawn buttons (decided: they are not obstacles; the own ring, carriers, holders and contacts move at
  snapshot rate), and the clock that moves under the own ring when the player stands on the camp (:361-368, not modelled).

### 1.3 `Choose`

First free spot wins, least overlap when none is free, and **no hopping**: a spot that overlaps nothing is kept even when an
earlier spot is free again; when the current spot is overlapped and none is free, the name moves only to a spot with a
strictly smaller overlap (contract in the code block below).

### 1.4 When a pass runs

A pass rebuilds the list, calls `Choose` per zone with a tile and its stored spot, adds one to `ZoneLabelPlacements`, then
moves the six names and calls `SetVerticesDirty()` if a spot changed. Never per frame, never from `OnPopulateMesh` or
`Update`. Four causes:

- `SetMap`: stored spots reset to none, then (with a map) a forced pass.
- `LayerChanged()`: a forced pass.
- `Present`, after its lists are rebuilt: a pass only if the new list differs from the last storing pass's.
- `OnRectTransformDimensionsChange`: a pass only if the layer rect's width or height differs from the one the last pass of any
  cause saw; it chooses from the stored spot and moves the label, but stores nothing, so a rect that is narrowed and restored
  brings the original spot back. The other three store the result and the list they saw.

Without `Large`, a map or a region plane nothing runs. A bare marks object (no parent, session or mark;
`MapScreenEditModeTests.MarksDrawZoneIconsOnlyWhenTheMapCarriesRegionsAndLabelOnlyTheLargeMap`) works: no exception, six
names; a name whose spot 0 is clear of the layer rect takes spot 0 (on that fixture's 300 x 300 rect COLISEUM's is not: it
takes another spot). The list is compared element by element with exact floats, no tolerance; it ends with the six names'
sizes, so a text-scale change is seen by the next `Present`, not before.

### 1.5 What follows the spot

`ZoneLabelN.anchoredPosition` is the spot's centre. The dark plate of `OnPopulateMesh` (:577) is the rect centred between I and
the name's centre `(dx, dy)` from I, width `104 + |dx|`, height `32 + |dy|`: for spot 0 exactly today's `p + (0, -12)`,
104 x 56. Drawn as before (`GlyphXY`, `Disc`, ink alpha .6). Size, font, material, host, sibling order and the 120 x 18
box stay.

### 1.6 Public reads in `TacticalMapMarks`

`int ZoneLabelPlacements`, `int ZoneLabelSpot(int region)` (the spot the name is on now; -1: no name), `Rect?
ZoneLabelRect(int region)` (that spot's rect; null: no name), `Rect? ZonePlateRect(int region)`.

### 1.7 A steady frame allocates nothing

The pass uses `for` over the class's own pooled `List<MapRect>`, never `foreach` over `session.Collections.*`, and measures
nothing it has measured (rule 1.9).

### 1.8 File size

`TacticalMapMarks` becomes `public sealed partial class`. To the new `TacticalMapMarks.ZoneLabels.cs` (a `/// <summary>` above
the type; it starts with `using MapRect = Roguehalla.Net.Ui.MapRect;`, because `TacticalMapMarks.cs` imports both
`Roguehalla.Net.Protocol` and `Roguehalla.Net.Ui`, and each has a `MapRect`: `Protocol/MapTypes.cs:47` is a ushort tile
rectangle, `Net/Ui/MapScreenLayout.cs:6` the float one) move, unchanged: fields `_zoneLabels`, `_zoneLabelHost`; methods
`FollowLayer`, `ZoneAt`, `ZoneLabelHost`, `RefreshZoneLabels`. Staying: `_zoneFound`, `_zoneX`, `_zoneY`, `ZoneIconCount`,
`SetMap`, `LayerChanged`, `OnDestroy`, `OnRectTransformDimensionsChange`, `OnPopulateMesh`. `RefreshZoneLabels` takes the kind
of pass (forced, rect event, present): after it has made, shown or hidden the labels it runs the pass of 1.4 and then sets
the positions. Edits in the main file: `partial` (both declarations `public sealed partial class`, `[RequireComponent]` on
one); `SetMap` (reset, then the forced kind); `LayerChanged` (forced); `OnRectTransformDimensionsChange` (rect-event kind);
the end of `Present` (build the list, compare, and only when it differs `RefreshZoneLabels` with the present kind) before
`SetVerticesDirty()` (:255); the plate line (:577). `MapLabelPlacement.cs` imports no `Protocol` namespace.

### 1.9 Sizes are measured, not guessed

A real label's rect comes from the label's own preferred size at the size in force: `GetPreferredValues(text, 0f, 0f)` of the
label, as `MythicTextScale` measures. The zone names are measured on their own labels; the camp amount and elite texts, ward
countdowns, clocks and chest seconds (all `HudUi.Label`, 9 units, `UiFontRole.Display`) on the first label of their pool
(`BadgeText(0)` :440, `_campClocks[0]`, `_chestTexts[0]`; an empty pool means nothing of that kind is on screen, so no entry).
The result is cached: a name by its label's `fontSize`, a 9-unit text by the pair (text, `fontSize`) in one dictionary that is
cleared past 256 entries, so a steady pass measures nothing. The size in force is the base size times the text scale unless
`MythicTextScale` shrank the label to fit its box; a shrunk measuring label gives a box a little smaller than a wider text
would take (not established). The pure functions take rects and numbers, never characters; the one per-character number left
is the chest box's `len * 6 + 8`, copied from the mesh.

### 1.10 The pure type: new `client-unity/RoguehallaNet/Net/Ui/MapLabelPlacement.cs` (namespace `Roguehalla.Net.Ui`, uses `MapRect`)

```csharp
/// <summary>Where a zone's name goes on the map: six fixed spots around its icon, first free one wins.</summary>
public static class MapLabelPlacement
{
    public const int Spots = 6;
    public const float IconHalf = 13f, Gap = 4f, Drop = 24f, SideDrop = 20f, EdgeSlab = 1000f;
    public static MapRect Spot(int spot, float iconX, float iconY, float w, float h);   // 1.1; bad spot, or w or
    // h not finite or not above 0: W = H = 0
    /// valid rect: X, Y, W, H finite, W and H above 0. A mark that is not valid is ignored; a spot that is not
    /// valid is never returned. overlap(spot) = sum of intersection areas with the valid marks (touching: 0).
    /// current outside 0..spots.Count - 1 means none yet. Returns -1 when no spot is valid, else: 1. current valid
    /// and overlap 0: current. 2. the lowest valid spot with overlap 0. 3. the lowest valid spot with the least
    /// overlap, unless current is valid and its overlap is not larger than that: then current.
    public static int Choose(IReadOnlyList<MapRect> spots, IReadOnlyList<MapRect> marks, int current);
    public static void AddBox(List<MapRect> into, float cx, float cy, float w, float h);   // bad size: nothing
    public static void AddText(List<MapRect> into, float x, float cy, float w, float h, int align);
    // a w x h box about cy; align -1: left edge at x, 0: centred on x, 1: right edge at x; bad size: nothing
    public static void AddEdges(List<MapRect> into, float xMin, float yMin, float width, float height);
    // four EdgeSlab-thick boxes just outside (left and right: height + 2 EdgeSlab tall; bottom and top: width +
    // 2 EdgeSlab wide); nothing when width or height is not finite or not above 0
}
```

(`MapRect.Overlaps` at `MapScreenLayout.cs:12` is strict: touching is not overlap.)

## 2. Dependencies

`lane/ui-rework/base-3` only. P-UI-TABMAP-MARKS-1 (merged: the layer scale `MapMarkScale`, `TacticalMapSurface.SetMarkScale`
:34-45, which already calls `LayerChanged`) and P-UI-ALLOC-METER-1 (merged: `Assets/Tests/Editor/AllocationMeter.cs`).

## 3. Relevant legacy paths

- `packages/client/src/ui/planningMath.ts:223` `placeLabels`: nearest free spot on rings of 12 to 120 px, zone-contained (verified with `rg`).
- `packages/client/src/ui/planning.ts:285` the zone-name elements; `:705-725` `relayout` builds the wishes and the obstacles
  (camp boxes, marks) and calls `placeLabels`; `:496, 526, 696, 868, 933` its callers (layout changes, not per frame).

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/MapLabelPlacement.cs` (and its synced copy `UnityProject/Assets/RoguehallaNet/Net/Ui/MapLabelPlacement.cs` + `.meta`)
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.ZoneLabels.cs` (+ `.meta`)
- `client-unity/RoguehallaNet.Tests/MapLabelPlacementTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesEditModeTests.cs` (+ `.meta`)

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/MapLabelPlacement.cs` (the stub exists; replace it), its synced copy (`bash client-unity/sync-net.sh`) and `.meta`
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs` (rule 1.8 only), `TacticalMapMarks.ZoneLabels.cs` and `.meta`
- This packet file (the oracle author's and the implementer's notes only)
- Not in scope: `TacticalMapSurface.cs`, `TacticalMapController*.cs` (P-UI-PLAYER-WORDS-1), `MapZones.cs`, `CampMarks.cs`, any mark
  size or colour, `ArenaView*`, `SpriteActorPresenter.cs`, every `MatchHud*.cs`, `UiMotion.cs`, `HudLayout.cs`,
  `AllocationMeter.cs`, `AbilityRowEditModeTests.cs`, `CardDisplayCatalog.cs`, `BakedContent.cs`, `Tools/*`, any existing test.
  No existing test changes: no `oracle-amend:` commit.

## 6. Contracts and references

- Presentation only (R-AUTH, D9): no wire, no `crates/`, no game rule. `client-unity/RoguehallaNet` is engine-free.
- Readability gate: the new `TacticalMapMarks.ZoneLabels.cs` and `MapLabelPlacement.cs` have a `/// <summary>` before their
  first type and at most 800 lines. HUD text goes through `HudUi.Label`; colours come from `UiTheme.Current`.
- Motion rule: nothing allocates in a steady frame (`AllocationMeter.AssertNone`).

## 7. Behaviour to preserve

- Spot 0 with `h` at most 14 is exactly today's `ZoneAt(region) + (0, -24)`; the plate for spot 0 is today's `p + (0, -12)`, 104 x 56.
- A name's size (120 x 18 box), font, material, host (`"Zone labels"`, a later sibling of the marks and the fog), sibling order.
- The minimap draws no names; a bare marks object (no parent) still creates six names under itself.
- No mark moves, none is hidden.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ZONE-NAMES-1 --base lane/ui-rework/base-3` passes.
- `dotnet test client-unity/RoguehallaNet.Tests --filter MapLabelPlacementTests` green.
- Unity EditMode `ZoneNamesEditModeTests` green; these existing classes stay green: `TabMapMarksEditModeTests`,
  `TabMapSheetEditModeTests`, `HudVisualFix2EditModeTests`, `TabMapVisualFix3EditModeTests`, `TabMapFlowEditModeTests`,
  `TacticalMapEditModeTests`, `DeclutterEditModeTests`, `MapScreenEditModeTests`.
- `bash client-unity/sync-net.sh --check` clean.

### Existing tests that pin this code (all verified by reading; none changes)

- `TabMapMarksEditModeTests.ZoneNamesFollowTheLayer` (`TabMapMarksEditModeTests.cs:297-318`): at 1280x720, 1920x1080, 1024x768
  with no mark delivered, each `ZoneLabelN` sits at the zone's tile point, 24 layer units under it (x and y within half a
  pixel), the host takes the layer's transform, and six names are drawn. Pins spot 0 = `(0, -24)` with no mark.
- `TabMapMarksEditModeTests.TheZoneHostFollowsAHandScaledLayer` (:320-): the host copies a hand-scaled layer after `LayerChanged()`.
- `TabMapMarksEditModeTests.MarkLabelsAreAtLeastTenPixels` (:150-): every active `ZoneLabel` label is at least 10 px on screen.
- `MapScreenEditModeTests.MarksDrawZoneIconsOnlyWhenTheMapCarriesRegionsAndLabelOnlyTheLargeMap` (`MapScreenEditModeTests.cs:109-128`):
  a bare `Large` marks object on a 300 x 300 rect with a 6 x 6 region map has `ZoneIconCount` 6 and six `TMP_Text` children;
  a non-`Large` one has none; a map without regions has `ZoneIconCount` 0.
- `HudVisualFix2EditModeTests.ZoneLabelsDrawAfterTheMarksMeshAndTheVisionFogAsTheirOwnLaterSibling` (`HudVisualFix2EditModeTests.cs:224-242`):
  the six `ZoneLabel` are not children of the marks mesh and draw after it and the fog (a direct child of the surface).
- `HudVisualFix2EditModeTests.ZoneLabelsCarryADarkOutlineSoTheyReadOverTheCreamSelfRing` (:245-): the six labels' shared material has the dark outline.
- Other classes construct `TacticalMapMarks` (`TacticalMapEditModeTests`, `DeclutterEditModeTests`, `TabMapVisualFix3EditModeTests`,
  `TabMapFlowEditModeTests`, `MinimapReturnEditModeTests`, `PlayerFlowsEditModeTests`) and must keep working with the new pass.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/MapLabelPlacementTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-ZONE-NAMES-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter MapLabelPlacementTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ZoneNamesEditModeTests,TabMapMarksEditModeTests,MapScreenEditModeTests,HudVisualFix2EditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- the gate: `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-ZONE-NAMES-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. The pure type is small. The risk is in `TacticalMapMarks`: a pass that allocates in a steady
frame, a pass at the wrong time (per frame, or from the rect event storing a half-written rect), and measured sizes.

## 11. Known traps

- The first `SetMap` runs before layout (rect 0 x 0): no slabs, spot 0; the rect event repeats the pass. Build nothing without
  a map or `Large` (`At()` needs `_map`).
- Stored spots survive `Present(null)`, session rebinds and every pass; only `SetMap` resets them. `SetMarkScale` writes anchors
  one by one: a rect-event pass may see a half-written rect and moves the label for that moment only; it never stores, so the
  final `LayerChanged` decides from the last stored spot.
- Sizes come from labels (1.9). `ToUpperInvariant()` allocates: read the name's text from its label, never rebuild it.
- `OnPopulateMesh`'s `1.2f` and mark sizes are copied in 1.2: a later change changes both.
- `ZoneLabelHost` parents the labels under the marks' parent (a sibling), or under the marks themselves when there is no parent.

## Not established (the hand-off says each)

- How it looks: the lane's next Web round judges it, after the packet is merged on the lane branch.
- A name may sit as far from its icon as the six spots allow, so a name whose icon is near a zone border may lie outside its zone.
- The planning screen's spawn buttons are not obstacles; a name may lie under one.
- After a text-scale change the names are re-placed at the next `Present`, not at once.
- A 9-unit text measured on a label that `MythicTextScale` shrank gives a box a little smaller than a wider text takes.
- Whether the rect event fires between the single anchor writes of `SetMarkScale` (the tests do not rely on it).

## Notes from the oracle author

**Stub commit** (the implementer replaces or edits these; they are not oracle files): `MapLabelPlacement.cs` (library and synced
copy with `.meta`), `TacticalMapMarks.ZoneLabels.cs` (+ `.meta`; the four reads only), `partial` on `TacticalMapMarks.cs`.

**Measured in the editor on the stub (1280 x 720, 6 x 6 tiles, layer 624 units wide at k = 1, 445.7 at k = 1.4), 2026-11-23.**

- Box constants of rule 1.2, checked against `OnPopulateMesh` / `DrawCamps` / `RefreshBadgeTexts` of the base: camp plate `17 s`
  (:549), no-reward dot `6 s + 4` (`Marker` adds 4, :550), elite diamond `6 s + 4` at `+13.5` (`IconSize * .5 + 5 s` = 7.5 + 6,
  :555), amount text `x = p.x + 11 s + 8 = p.x + 21.2`, `cy = p.y + 2` (`RewardSlot` :436, +8 at :475), elite text `+ row * 12 s =
  14.4` (:476), ward box `8 s + 4` at `p + (-13.2, 2)` (`WardSlot` :437; `DrawBadges` :535), ward text `x = p.x - 21.2`
  (:484), chest box `11 s + 4` (:591), chest seconds mesh box `(len * 6 + 8) s` by `12 s` at `p + (0, -15)` (:590, :410), clock
  at `p + (0, -16)` (:360), delivered sizes :598-616 (Giver `13 s` + 4 = 19.6, camp plate 20.4 are the tests' own).
- Name labels (font 10, box 120 x 18): FIRE 22.31, HAUNTED 52.13, PLAGUE 39.69, FROST 33.28, HOLY 27.62, COLISEUM 54.15 wide, all
  13.49 high at text scale 1 (so `h` is under 14 and spot 0 is today's `(0, -24)`). `preferredWidth` equals
  `GetPreferredValues(text, 0, 0)` for every name. At text scale 1.5 `MythicTextScale` sets the font to 13.35 (not 15: the 18 high
  box shrinks it): 29.79, 69.60, 53.00, 44.43, 36.87, 72.29 wide (each 1.335 times), 18.00 high (so `drop` = 26).
- 9-unit texts (font 9, 12.14 high): "+3" 8.86, "+3%" 15.02, "+4.5" 15.67, "Aug" 20.66, a ward countdown or clock "88" 9.94, "2:05"
  17.10, "12" 8.08 wide. ("Aug", ward and clock texts were measured on `CampBadgeText0` with the text set by hand; no elite camp,
  ward or chest is delivered by the tests.)
- The picture's state at k = 1, overlap area of each spot with the test-built marks and map edges (0 = free):
  HAUNTED `0=705.2 1=0 2=0 3=222.9 4=122.0 5=202.7`; HOLY `0=272.3 1=0 2=0 3=0 4=180.8 5=275.2`.
  At k = 1.4: HAUNTED `0=715.5 1=0 2=0 3=213.5 4=168.6 5=136.8`; HOLY `0=133.8 1=0 2=14.4 3=49.5 4=76.1 5=178.5`.
  So spot 1 (above) is free in both zones at both scales and the offsets of rule 1.1 need no change. Spot 0 of the other four
  zones is free of every mark and inside the layer at both scales (so they stay on spot 0 in the picture).
- The picture's camps round to world ushorts (53,89), (31,83), (147,86), (134,86) (HAUNTED then HOLY, the order of the table in
  the test); givers at world (43.08, 89.23) and (46.77, 87.08) at k = 1 offsets; the tests compute their rects from the world points.
- The rect event: with a counter in `OnRectTransformDimensionsChange` (scratch, removed), writing `rectTransform.offsetMax`
  fires it at once (one call per write), `Canvas.ForceUpdateCanvases()` adds none. Test 7 does not depend on the force.
- The stub's `LayerChanged` allocates nothing (`AllocationMeter.AssertNone` passes on it), so a pass that allocates is the
  implementation's own.

**Differences from the lane spec's test text.** (1) `ZoneLabelSpot` is -1 on the stub, so `Is.Not.EqualTo(0)` would pass on it:
the tests assert `Is.GreaterThan(0)` (spot 0 is left for a spot 1 to 5) and the first red line of tests 1 and 3 is set-up or
that. (2) Every test that delivers marks first asserts the delivery reached the layer (`CampMarkerCount`, `QuestGiverCount`),
so a delivery that silently draws nothing cannot pass a test. (3) The picture's marks are delivered after a first build that
reads the layer width at k = 1 (a second build puts the four camps on the map); the scale is set by hand after the paint frames
(a `Frame()` re-applies the controller's scale). (4) Test 1 asserts 10 test-built marks (2 givers, 4 camp plates, 4 amount texts).
(5) `ZoneLabelRect` of the stub is null, so assertions on a rect use `ZoneLabelRect(..) ?? default` after asserting it has a
value; nothing in the tests throws on the stub.

**Red on the stub, and on what** (`unity-class.sh ... ZoneNamesEditModeTests`: 8 failed; dotnet `MapLabelPlacementTests`: 6 failed):
`ThePicturesNamesAreClearOfTheirMarks` (spot 0 of HAUNTED overlaps at least two marks: expected >= 2, was 0, because `Spot` is a
zero rect), `ANameWithNothingNearItIsWhereItIsToday` (Fire on spot 0: expected 0, was -1), `ANameKeepsItsSpotWhileItIsFree`
(HAUNTED has a spot: expected >= 1, was -1), `TheBoxesAreTheTextAndThePlateFollows` (Fire has a name rect), `AQuietFrameAllocatesNothingAndPlacesNothing`
(`ZoneLabelPlacements` > 0, was 0), `ThePlanningMapAvoidsToo` (spot 0 of HAUNTED overlaps both givers: expected 2, was 0),
`ANarrowedAndRestoredRectBringsTheSpotBack` (no mark so Fire on spot 0: expected 0, was -1), `TextScaleOnePointFiveKeepsNamesOffTheirIcons`
(Fire has a name rect at scale 1). dotnet: `SpotsAreTheWorkedTable` (spot 0 X expected 71 was 0), `TheFirstFreeSpotWinsAndAFreeCurrentIsKept`,
`NoFreeSpotTakesTheSmallestOverlap` (expected 1 was -1), `BadInputsAreSafe` (current 9: expected 0 was -1), `TheBuildersAddTheRightBoxes`
(AddBox adds one box: expected 1 was 0), `ChoosingAllocatesNothing` (Spot(0,100,100,58,14) X expected 71 was 0).

**Scratch runs (not committed).** (a) dotnet: a reference implementation of `MapLabelPlacement` written in the working tree
made all six `MapLabelPlacementTests` pass (70 ms), then was removed: the six tests can be met. (b) EditMode: with a catch-and-collect
helper (`Assert.That` replaced by a call that keeps the `AssertionException` message and carries on, failing in `TearDown` with the
list) and the reference `MapLabelPlacement` in place, each of the eight tests ran to its last line on the stub. Every collected
entry was an assertion about the missing behaviour (a name rect, spot, plate or pass count that the stub does not give: 4, 72,
7, 3, 12, 54, 40 and 1 entries); no other exception occurred; the set-up assertions (deliveries, camp and giver counts, spot 0
overlapping the marks, the narrowed rect, the planning map drawing the givers) all held. The expected values were read once
more against the rules and the worked numbers above. The Tab map's own set-up `Frame()` needs no change for the planning map.

**Existing tests that pin this code stay green on the stub:** `TabMapMarksEditModeTests`, `TabMapSheetEditModeTests`,
`HudVisualFix2EditModeTests`, `TabMapVisualFix3EditModeTests`, `TabMapFlowEditModeTests`, `TacticalMapEditModeTests`,
`DeclutterEditModeTests`, `MapScreenEditModeTests`: 110 passed, none failed (with the 8 new red, 118 run).

**Hand-off lines the implementer's report repeats:** names will usually leave spot 0 in a real match (the picture's givers lie 30
to 50 layer units from the icon); the planning spawn buttons are not obstacles; a name may leave its zone; nothing here
says how it looks (the lane's Web round judges it); a proposed `DEVIATIONS.md` row is in the routing block above.

## Notes from previous attempts
