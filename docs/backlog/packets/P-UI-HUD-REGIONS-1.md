# P-UI-HUD-REGIONS-1: one table says where each new HUD region sits, and a test proves they never collide

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "make sure all in-game notifications are
  thoughtfully placed ... When a stat or quest is accepted there is a big ugly notification in middle of map".
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework`
- **D-rules touched:** D1 n/a (presentation only; the placement of toasts differs from legacy and is recorded by the packet that moves them, P-UI-TOAST-LANES-1) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
HUD rects are literals spread over about 20 files. The packets that follow add regions (two toast lanes, one ability row,
a prompt lane, a quest tracker dock, a quest offer card). Each must collide with nothing and stay out of the middle of the
screen, where the fight is. This packet adds the one place that says where each region is, as pure arithmetic tested
without Unity (`HudRegions`), and the one-line way to place a Unity rect from it (two `HudUi` overloads). **It moves
nothing on screen:** no existing HUD element changes, and no existing literal is replaced by these constants.

## 2. Dependencies
none (P-UI-INPUT-CUES-1 is already in the base).

## 3. Relevant legacy paths
No legacy TypeScript. Current Unity code the numbers come from (verified by reading):
- `client-unity/UnityProject/Assets/Scripts/UI/HudUi.cs:9` `enum HudAnchor` (the order `HudRegionAnchor` must copy);
  `:18-32` `Point(anchor)`; `:38-44` `Box(parent, name, anchor, x, y, width, height)`; `:46-54` `Place(...)` holds the rule
  (`ax = p.x < .25 ? x : p.x > .75 ? -x : x`, same for y; pivot = anchor point; a centred axis is a signed offset, right and up positive).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Legacy.cs:19` (`BarBottom 14, SlotWidth 104, SlotHeight 76, SwapWidth 28, WishSize 64, BarGap 6, ConsumablesHeight 28`),
  `:343` (`rowBottom = BarGap + ConsumablesHeight`, so the slot row sits 48 from the bottom), `:374` (QSwap chip is 28 x 40),
  `:392` (Wish tile 64 x 64), `:425` (consumables row 352 x 28, BottomCenter, 0, 0).
- `docs/rework/LEGACY_LAYOUT.md` sections 1, 4, 8, 12 (stage 1 unit = 1 CSS px; a 16:9 window is a 1422 x 800 stage).
- Evidence: `.agent-runs/lane-ui-rework/understand/hud-shell.txt` (SEAMS: HUD rects are literals in many files in two unit systems).

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs` and its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/HudRegions.cs` (+ `.meta`, written by Unity on the next run; commit it)
- `client-unity/RoguehallaNet.Tests/HudRegionsTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudRegionsEditModeTests.cs` (+ `.meta`)

## 5. Exclusive write scope
- `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs` (the `stub:` commit adds it with default-returning bodies; the implementer replaces the bodies) and its synced copy (`bash client-unity/sync-net.sh`) and `.meta`
- `client-unity/UnityProject/Assets/Scripts/UI/HudUi.cs` (the two overloads only; the `stub:` commit already adds them with empty behaviour: `Box` makes an unplaced rect, `Place` does nothing)
- nothing else. The test files and this packet are oracle files.

## 6. Contracts and references
- R-AUTH / ADR 0008: presentation only; no game rule, no change to wire, `crates/`, `packages/`.
- HUD stage units: `UI/HudUi.cs`, `UI/HudLayout.cs`. Theme colours from `UiTheme.Current` only (nothing here draws).
- Readability gate: the new non-test C# file has a `/// <summary>` before its first type and is at most 800 lines.
- Library edited at `client-unity/RoguehallaNet`, then `bash client-unity/sync-net.sh` (through `in.sh`), commit the copy.

## 7. Behaviour to preserve
- No existing HUD element moves, resizes or re-anchors. Existing literals are not replaced by the constants (later packets do that for the elements they own).
- The existing `HudUi.Box` / `Place` (7- and 6-argument) behave exactly as now; the new overloads call them.
- Every function of `HudRegions` is allocation-free (structs, constants, arithmetic). Out-of-range indexes and row counts are clamped, never thrown.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-HUD-REGIONS-1` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter HudRegionsTests` green; Unity class `HudRegionsEditModeTests` green.
- `bash client-unity/sync-net.sh --check` clean.
- Existing classes still green: `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`.

## The API (new file `Net/Ui/HudRegions.cs`, pure, namespace `Roguehalla.Net.Ui`)

Units are HUD units. A region is expressed the way `HudUi.Box` takes it: an anchor, an inset or offset per axis, a size.

```csharp
public enum HudRegionAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

public readonly struct HudStageRect   // x from the left edge, y from the TOP edge
{
    public readonly float Left, Top, Width, Height;
    public float Right { get; }  public float Bottom { get; }
    public bool Overlaps(HudStageRect other);     // strict: touching edges do not overlap
    public bool Inside(float stageWidth, float stageHeight);   // 0 <= Left, Right <= stageWidth, 0 <= Top, Bottom <= stageHeight
}

public readonly struct HudRegion      // for a left/right anchor X is the inset from that edge; top/bottom: Y the inset; centred axis: signed offset (right and up positive)
{
    public readonly HudRegionAnchor Anchor; public readonly float X, Y, Width, Height;
    public HudStageRect On(float stageWidth, float stageHeight);
}

public static class HudRegions
{
    public const float Margin = 12f;
    // centre bottom, from the bottom edge up
    public const float ConsumablesBottom = 14f, ConsumablesHeight = 28f, ConsumablesWidth = 352f;
    public const float AbilityBottom = 48f, AbilityHeight = 76f, WeaponSlotWidth = 104f, SwapWidth = 28f, TileSize = 64f, AbilityGap = 6f;
    public const float AbilityWidth = 2f * WeaponSlotWidth + SwapWidth + 3f * TileSize + 5f * AbilityGap;   // 458
    public const float PromptBottom = 130f, PromptHeight = 26f, PromptWidth = 300f, RewardDockWidth = 420f;
    public const float FeedbackBottom = 162f; public const int FeedbackMax = 2;
    // centre top
    public const float EventTop = 46f; public const int EventMax = 3;
    public const float PillHeight = 24f, PillGap = 4f, LaneMaxWidth = 460f, TopCentreGap = 8f;
    // left column
    public const float TrackerWidth = 260f, TrackerHeader = 18f, TrackerRow = 38f, TrackerGap = 8f; public const int TrackerMaxRows = 3;
    public const float OfferWidth = 280f, OfferMinHeight = 96f;
    // the middle of the screen that every region keeps out of
    public const float CombatZoneWidthShare = .46f, CombatZoneHeightShare = .44f;

    public static HudStageRect CombatZone(float stageWidth, float stageHeight);            // centred
    public static HudRegion Consumables();       // BottomCenter, 0, 14, 352 x 28
    public static HudRegion AbilityRow();        // BottomCenter, 0, 48, 458 x 76
    public static HudRegion AbilityElement(int index);   // 0 weapon 1, 1 swap, 2 weapon 2, 3 R, 4 Shift, 5 Q; BottomLeft, insets from the row's own bottom-left corner
    public static HudRegion Prompt(float width = PromptWidth);                              // BottomCenter, 0, 130, width x 26
    public static float LaneWidth(float stageWidth, float wanted = LaneMaxWidth);           // min(wanted, stageWidth - 2 * Margin)
    public static HudRegion FeedbackPill(int index, float width);                           // index 0 is the lowest; 0 <= index < FeedbackMax
    public static HudRegion EventPill(int index, float width, float topCentreBottom = EventTop - TopCentreGap);
    public static HudRegion TrackerDock(float plateChainBottom, int rows);
    public static HudRegion OfferCard(float stageHeight, float plateChainBottom, int trackerRows, float wantedHeight, float championTop);
}
```
Semantics: `EventPill(i, w, topCentreBottom)` is TopCenter, offset 0, Y = `max(EventTop, topCentreBottom + TopCentreGap) + i * (PillHeight + PillGap)`,
height 24 (`topCentreBottom` is the bottom edge, from the top of the stage, of the lowest pill at the top centre: 38 when only the
timer shows, so the default starts at 46). `FeedbackPill(i, w)` is BottomCenter, offset 0, Y = `FeedbackBottom + i * (PillHeight + PillGap)`, height 24.
`TrackerDock` is the quest tracker under the top-left plate chain (`plateChainBottom` = bottom edge, from the top of the
stage, of its lowest element); rows 0 gives height 0. `OfferCard` is the quest offer card on the left: vertically centred
when that clears the tracker, otherwise just below it; its height is cut so it ends `TrackerGap` above `championTop` (top
edge, from the top of the stage, of the bottom-left champion panel), and is never less than `OfferMinHeight`.

Unity adapter, the only existing file touched, `UI/HudUi.cs`: two overloads beside the existing ones, nothing else:
`public static RectTransform Box(Transform parent, string name, HudRegion region)` and
`public static void Place(RectTransform rect, HudRegion region)`; they map `HudRegionAnchor` to `HudAnchor` by value and call the existing `Box` / `Place`.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/HudRegionsTests.cs` (26 cases: the four-stage sweeps count once per stage)
- `client-unity/UnityProject/Assets/Tests/Editor/HudRegionsEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudRegionsEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-HUD-REGIONS-1.md`

## Existing tests that pin this code (all stay green, none changed)
- `HudLegacyLayoutEditModeTests` (`Assets/Tests/Editor/HudLegacyLayoutEditModeTests.cs`): builds the real HUD and asserts anchors, insets and sizes of the existing elements (`:139` weapon bar centred, bar 14 from the bottom, slots 104 x 76 at bottom 48, Wish tile 64, consumables 28 high; `:224` banner and toasts at the top-centre and bottom-124 anchors; `:378` every HUD graphic uses a theme colour). Pins "nothing moves": this packet must leave them green.
- `HudTextScaleEditModeTests` (`:84` builds a `HudUi.Button`): label fit under text scale; touches `HudUi` only through the existing overloads.
- No dotnet test pins `HudUi` (engine-bound). No test mentions `HudRegions`, `HudStageRect`, or `HudRegionAnchor`. No conflict with any existing test; no `oracle-amend` needed.

## 9. Validation commands
- `in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-r dotnet test client-unity/RoguehallaNet.Tests --filter HudRegionsTests`
- `unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-r HudRegionsEditModeTests,HudLegacyLayoutEditModeTests,HudTextScaleEditModeTests`
- `in.sh <worktree> bash client-unity/sync-net.sh --check`
- `in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-HUD-REGIONS-1`

## 10. Risk and suggested model tier
Low: arithmetic and two thin overloads. implementer-sonnet; reviewer (not Opus): no `rh-sim`, wire or state.

## 11. Known traps
- `HudStageRect` y is from the TOP; `HudUi.Box` insets are from the anchor's edge. A bottom anchor with inset 48 and height 76 on an 800-unit stage is Top = 676.
- A centred axis takes a signed offset (right and up positive): `Left = W/2 + X - Width/2`, `Top = H/2 - Y - Height/2`. All centre regions here use offset 0.
- `Overlaps` is strict, so a zero-height region (tracker with 0 rows) never "overlaps" something it merely touches; the tests skip disjointness against a zero-height rect.
- The plate chain's real bottom and the champion panel's real top are read by the packets that use the tracker and the offer card; this packet only takes them as numbers.
- Do not move, resize or re-anchor any existing HUD element, and do not replace existing literals with these constants.
- Do not add lines to `View/ArenaView.cs` or `Presentation/SpriteActorPresenter.cs`.

## Notes from the oracle author
Where the spec left a number open the tests pin only what the spec says, plus these choices (the implementer follows them):
- Spec choice recorded: the tracker is placed `TrackerGap` (8) or less below `plateChainBottom`: the tests pin `Y` in `[plateChainBottom, plateChainBottom + 8]`.
- Constructors: `HudStageRect(float left, float top, float width, float height)` and `HudRegion(HudRegionAnchor anchor, float x, float y, float width, float height)` are public (the tests build rects and regions by hand). The stub has them; keep them.
- `AbilityElement` sizes: weapon slots 104 x 76, tiles (R, Shift, Q) 64 x 64, swap 28 wide and 40 high (the existing chip, `MatchHud.Legacy.cs:374`); all six have Y inset 0 (bottoms on the row's bottom edge), anchor BottomLeft, X inset = running sum of widths plus 6 gaps (0, 110, 144, 254, 324, 394). The tests pin the swap height only as 0 < h <= 76.
- `TrackerDock`: anchor TopLeft, X = `Margin`, width `TrackerWidth` (260), Y >= `plateChainBottom`, height for rows 1..3 at least `TrackerHeader + rows * TrackerRow` and at most that plus `rows * TrackerGap`; rows 0 gives height 0.
- `OfferCard`: anchor TopLeft, X = `Margin`, width `OfferWidth` (280). Top is the exact vertical centre (`(stageHeight - height) / 2`) when that is at or below the tracker's bottom (and below `plateChainBottom`); otherwise Top is in `[tracker.Bottom, tracker.Bottom + TrackerGap]` (and never above `plateChainBottom`). Height is `min(wantedHeight, championTop - TrackerGap - Top)` but never below `OfferMinHeight` (when the minimum wins, the card may end below the limit: pinned by one test).
- Allocation-freedom is not tested (a stub passes it trivially); it is a review point.
- The enum-names check of the EditMode test is inside the two measuring cases: a separate test would be green on the stub.

Red on the stub (each on an assertion about behaviour; the stub returns default regions and 0 for `Right`/`Bottom`/`LaneWidth`):
- dotnet `HudRegionsTests`: all 26 cases fail on assertions (anchors default to TopLeft, widths 0, `Right`/`Bottom` 0, `LaneWidth` 0, `OnAgrees...` Left 0, etc.). Checked afterwards against a throw-away implementation (not committed): all 26 pass, so the tests are satisfiable.
- Unity `HudRegionsEditModeTests` (4 cases, all red): `BoxFromARegionMeasuresTheSameAsOn` at 1422x800 and 1280x960 fail on "the region has a size" (stub `On` returns an empty rect; once `HudRegions` is real they fail on the measured left/top of the unplaced rect until the `Box` overload places it); `PlaceFromARegionMovesAnExistingRectTheSameWay` at both stages fails on the measured `left` (Place does nothing: 661 vs 0 at 1422).
- Not tested on purpose: allocation-freedom (review point); the anchor-name order check lives only inside the EditMode cases (a dotnet copy would be green on the stub).

Existing classes run green on the stub: see "Run results" at the end.

## Run results (oracle author)
- `unity-class.sh HudRegionsEditModeTests,HudLegacyLayoutEditModeTests,HudTextScaleEditModeTests`: 63 tests, 59 passed, 4 failed: exactly the four `HudRegionsEditModeTests` cases above. `HudLegacyLayoutEditModeTests` and `HudTextScaleEditModeTests` are green on the stub.
- `dotnet test client-unity/RoguehallaNet.Tests --filter HudRegionsTests`: 26 failed, 0 passed on the stub.
- `bash client-unity/sync-net.sh --check`: clean. `HudRegions.cs.meta` (synced copy) is committed with the stub.
