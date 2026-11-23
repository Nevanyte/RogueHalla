# P-UI-MAP-RASTER-1: the map's terrain picture is built once per map, and patched when terrain is destroyed

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane: "make the front end as fluid as possible"). It removes the one long frame the lane's worst-frame measurement found in the HUD's map code.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head `e8217e2a3`: wave 4 on integration 87; P-UI-MAP-PAINT-1 has merged and did not touch `TacticalMapSurface.cs`)
- **D-rules touched:** D1 n/a (presentation only; no legacy parity capture) · D2 n/a · D5/D6/D9 none (no game rule, nothing under `crates/`, no wire change)

Paths: `Scripts` = `client-unity/UnityProject/Assets/Scripts`, `Net` = `client-unity/RoguehallaNet`.

## 1. ID and outcome

**Measured** (Web build of `c8e21c0e1`, the lane's recorder; `.agent-runs/lane-ui-rework/webround/round-3b/perf/records.json`, record 0, window 4): one frame of 68.4 ms in a walk window (scripts 66.1, hud 64.6, **hudMap 64.1**, render 2.2; the frames before and after 2.1 and 2.2 ms), with 7,004,160 managed bytes in `hudMap` (1,710 pages of 4,096). Its context: one snapshot and one State frame, the local seat dead, the terrain map object NOT changed, no wall. `hudMap` brackets exactly `_map?.Tick()` (`Scripts/UI/MatchHud.Features.cs:339`).

**What that frame was** (arithmetic): `TerrainRaster.Build` (`Scripts/UI/TacticalMapSurface.cs`, the `Build` method, about :175-235 after the stub's counters) for the 160 x 160 map draws 6 texels a tile (`TexelsFor`: 1024 / 160), a 960 x 960 picture: a `Color32` array of 3,686,400 bytes, plus `ZoneWeights.Bake` (`Net/Net/Ui/ZoneWeights.cs:16`): eight float planes of 320 x 320, 3,276,800 bytes. Together 6,963,200 bytes, 1,700 pages. Nothing else under `_map.Tick()` allocates MB.

**Why it was built again** (read in the code; the oracle's first two tests are the proof):

1. `TerrainRaster` keeps a picture only while a surface holds it: `Release` at zero references destroys the texture and drops the cache entry; `Acquire` matches by the map OBJECT and the theme object (`TacticalMapSurface.cs`, `Acquire`/`Release`).
2. `TacticalMapController.Bind` has no early return and sets both surfaces' maps to null (`Scripts/UI/TacticalMapController.cs:333-344`, the nulling at :344): both let go and the picture is destroyed. The next `Tick` sets the map again (`:454`) and `Build` runs.
3. Callers of `Bind`, through `MatchHud.RefreshLaunchConfirmation` (`Scripts/UI/MatchHud.Accounts.cs:116-127`; it binds `confirmed ? session : null`): `MatchHud.Bind`; `ShowMenu` (`_map?.Bind(null)` directly); `MatchClient` on Ended, Failed or Rejected (`Scripts/Net/MatchClient.cs:122`), on the first verified snapshot (`:141`: it binds the session again after a timeline reset) and in the timeline-reset handler (`:143-149`, `Bind(null)` since `_modeConfirmed` is false). A timeline reset has two sources in `Net/Net/Session.cs`: a Welcome (the reconnect) and a snapshot source cut (tick advances, server time does not; it runs in one poll with the snapshot handler).
4. So only two paths lose a held picture in a running match: the Ended call (`Bind` with the same session) and the timeline-reset pair (`Bind(null)`, then `Bind(session)`). WHICH fired in the measured frame is not established. A reconnect's Welcome delivers a new `MapBlob` object, which rule 2 below covers. `ShowMenu` parks the last picture between matches.

**A destroyed-terrain delivery builds the whole picture in the middle of a fight**, for every player: a `DestroyedTerrainState` with new cells makes `CollectionsView.Apply` hand out `_baseMap.WithDestroyedTerrain(cells)` (`Net/Net/Collections.cs:467-473`), a new `MapBlob` with a cloned tile array and the SAME `Regions` array (`Net/Protocol/MapBlob.cs:140-157`). The surface sees a new object and builds all 921,600 texels again although a handful of tiles changed. Terrain is destroyed by a quest spell and a quest reward effect (`crates/rh-sim/src/quests/board/{special/wall,rewards/effects/terrain}.rs`). The recorder counted no terrain change in 70 windows: this cost is derived.

**Legacy** draws its minimap terrain into a cached offscreen canvas and repaints it only for a new map: `packages/client/src/ui/minimap.ts:1` (header: "a quiet, cached terrain layer"), `:102` (`private readonly layer = document.createElement("canvas")`), `:332-338` (`setMap`: "Repaints the static layer for a (new) map", `paintMapBase(g, map, ...)`), `:501` (`c.drawImage(this.layer, 0, 0)` each paint). Legacy has no destroyed-terrain mechanic on its minimap to patch.

### The outcome

The packet removes the MANAGED whole build (the 64 ms and 7 MB of the measured frame) at a rebind and replaces it by a patch at a terrain change. A derive still rebuilds the mip chain and uploads the whole picture once per terrain change: native work the oracle does not bound (see "Not established").

1. **A picture outlives a rebind.** When the last holder of a picture lets go, it is PARKED (one spare, at most) instead of destroyed, ONLY when its theme is the object `UiTheme.Current` at that moment and no other picture of the same theme, `W`, `H` and equal regions is held. A picture of any other theme is destroyed at its last release, as on the base. An `Acquire` that matches the spare takes it back: no build, the same `Entry` and `Texture2D`. Parking a second picture destroys the first spare; a whole build destroys the spare before it builds, so at most one picture that nobody holds is ever alive. The `UiTheme.Current` setter (`Scripts/UI/UiTheme.cs:22-25`) assigns the new theme BEFORE it raises `Changed`, and a surface's `Rebuild` (release, then acquire) runs inside `Changed`: the release sees the new theme as Current, so the old picture is destroyed, not parked: a theme change leaves nothing parked. Nothing under `Assets/Scripts` assigns `UiTheme.Current` (grep, 2026-11-23), so in a player build every picture is of the current theme and parks.
2. **Equal terrain is the same picture.** A held picture or the spare matches a map when the theme is the same object and the map is the same object OR has the same `W`, `H`, equal `Tiles` (byte for byte) and equal `Regions` (the same array, both null, or equal bytes). So a new `MapBlob` with the same terrain (a reconnect's Welcome) re-uses the picture. The entry then holds the newest map object.
3. **Changed tiles are patched.** When no picture matches but one of the same theme, `W`, `H` and equal regions exists (held or spare: "kin"), the new picture is DERIVED from it: mip 0 is copied, only the texels of tiles within ONE tile of a tile whose value differs are computed again (by exactly the arithmetic of `Build`, so the result equals a whole build texel for texel), the mips are rebuilt, and the zone weight planes are shared by reference, not baked again. A kin that was the spare is destroyed after the derive. When the last holder of the old picture moves to the new one, the old one is destroyed, not parked (rule 1: its kin is held).
4. **A theme change still builds whole** (the colours differ), as today.
5. **Counters for the tests** (public static, read-only from outside): `TerrainRaster.FullBuilds`, `DerivedBuilds`, `LastDerivedTexels` (texels computed by the last derive), a `bool HasSpare`, and `void DropSpare()` (destroys the spare; tests call it in SetUp and TearDown). The stub commit already declares them (`FullBuilds` counts, the rest return defaults); the implementer keeps these names and makes them true.
6. Nothing a player sees changes: the same pixels, texture settings (`RGBA32`, mips, trilinear, clamp, aniso 2, `HideAndDontSave`, readable) and the aspect fitter rule in `TacticalMapSurface.Rebuild`.

**Cost accepted.** Each picture keeps its six zone weight planes (shared by pictures derived from it). For a 160 x 160 map: the texture with its mips is 4,915,200 bytes and exists twice while readable (a CPU and a GPU copy); the six planes are 2,457,600 bytes; about 12.3 MB a picture. Between matches ONE picture stays parked (`ShowMenu` unbinds) until the next map's whole build destroys it BEFORE building, so the peak is unchanged but for the planes. During a derive two pictures are alive until the second surface has moved.

## 2. Dependencies

The lane base `lane/ui-rework/base-3` as it stands. No other packet.

## 3. Relevant legacy paths

- `packages/client/src/ui/minimap.ts:1`, `:102`, `:332-338`, `:501`: the cached terrain layer, repainted for a new map only (verified by reading, 2026-11-23).

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/MapRasterEditModeTests.cs` (+ `.meta`): oracle, classes `MapRasterEditModeTests` (fixture a) and `MapRasterControllerEditModeTests` (fixture b).

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapSurface.cs`: the `TerrainRaster` class; `TacticalMapSurface.Rebuild` only if the new rules need it. Under 800 lines (226 on the base, the stub adds a dozen).
- Nothing else: not `TacticalMapController*.cs`, `MatchHud*.cs` or `MatchClient.cs`, no library or recorder file, no merged packet document, no existing test. (The repeated `Bind` stays; it now costs nothing for the picture.)

## 6. Contracts and references

- Lane rules `COMMON.md`: presentation only; no change to the wire, `crates/`, `packages/`. R-AUTH: Unity holds no game rule (this is a picture of a delivered map).
- Readability gate: the file keeps its `/// <summary>` and stays under 800 lines; `TacticalMapSurface.cs` is not a protected non-split file.

## 7. Behaviour to preserve

- The same pixels for the same map and theme: a derived picture equals a whole build texel for texel (mip 0, and the mips).
- Texture settings (`RGBA32`, mips, trilinear, clamp, aniso 2, `HideAndDontSave`, readable); `Entry.Tinted` and `TexelsPerTile` for every picture (a derived entry carries them over).
- `TerrainRaster.Release` of a picture of a theme that is not `UiTheme.Current` destroys it at the last release (existing pin `MapScreenEditModeTests.RasterIsSharedPerMapAndThemeAndFreedOnLastRelease`).
- Design notes (binding where they say "must"):
  - Factor the body of `Build`'s texel loop into one function used by the whole build and the derive (must: the same code path, or the texel-for-texel rule cannot hold).
  - The reach of one tile: `MapSmoothing.Coverage` (`Net/Net/Ui/MapZones.cs:71-80`) reads the 2 x 2 tile centres nearest to a texel, so a changed tile T changes texels only inside tiles T-1 to T+1 (clamped to the map). The zone tint reads only `Regions`, which a kin shares. Mark the dirty tiles in a scratch (a `bool[W * H]` is fine), then shade every texel of each marked tile once. `LastDerivedTexels` = marked tiles x texels per tile squared.
  - Copy mip 0 without a managed array of the picture's size: `kin.Texture.GetPixelData<Color32>(0)` into the new texture's `GetPixelData<Color32>(0)` (`CopyFrom`), write the dirty texels into that native array, then `Apply(true, false)`. Rows are stored bottom-up exactly as `Build` writes them (`outRow = th - 1 - row`). (The oracle author's scratch run, below, confirmed this works in 6000.0.85f2.)
  - `Tiles` and `Regions` of a terrain map are never written after construction (`WithDestroyedTerrain` clones; the walk map's retained buffer, `Net/Net/LocalMotion.Walls.cs:82-95`, never reaches a surface: the controller passes `_bound.TerrainMap`). Rule 2 relies on that. `Entry.Tinted` and `TexelsPerTile` carry over to a derived entry.
  - Liveness (must): a cache hit, a kin and the spare each need a live texture (`Texture != null`); a spare whose texture was destroyed is dropped, and `HasSpare` is true only for a spare with a live texture. Destroying the spare clears the field BEFORE the destroy call. `Release` ignores an entry whose reference count is already zero or less. Parking compares `entry.Theme` to `UiTheme.Current` with `ReferenceEquals`, once, at the last release.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-MAP-RASTER-1` passes (the lane gate; the whole EditMode suite runs inside it).
- The new classes `MapRasterEditModeTests` and `MapRasterControllerEditModeTests` are green; the existing classes listed below stay green UNEDITED, in one Unity run (they share the static cache).
- Reviewer to confirm by reading: the derive allocates no managed array of the picture's size (the oracle's heap test bounds the managed heap under 1 MiB; see "Notes from the oracle author").

### Existing tests that pin what this packet changes (read; none is edited)

`MapScreenEditModeTests` (`Assets/Tests/Editor/MapScreenEditModeTests.cs`) is the only class that names `TerrainRaster` (grep of `Assets/Tests` and `Assets/Scripts`, 2026-11-23). It uses a fresh `UiTheme.Skin()` that is never `Current`, so nothing there is parked:
- `TintedTerrainShowsEachZoneHueAndKeepsWallsBrighterThanTheTint` (`:42`, Acquire at `:48`, Release at `:64`): the tinted raster has `Tinted`, width `w * k`, zone hues lean toward the zone colour, a wall is lighter than its tint, `mipmapCount > 1`, `k >= 2`.
- `MapWithoutARegionPlaneFallsBackToTheNeutralLook` (`:68`, Acquire at `:71`): no regions: `Tinted` false and a texel equals `SurfaceSunken`.
- `RasterIsSharedPerMapAndThemeAndFreedOnLastRelease` (`:83-90`): two Acquires of one map and theme return the SAME entry; the texture lives after the first release and `== null` after the last. The standing pin of "destroyed with the last user" for a theme that is not Current.

Rigs whose surfaces share the cache under `UiTheme.Current` and may now be handed a parked picture of an equal map (none reads a texture, none names `TerrainRaster`): `MapPaintEditModeTests`, `MapCoverEditModeTests`, `TabMap*`, `ZoneNames*`, `TacticalMapEditModeTests`, `Minimap*`, `HudVisualFix3EditModeTests`, `HudVisualFix4EditModeTests`, `LeftEdgeFoldEditModeTests`, `QuestTrackerEditModeTests`, `HudTextScaleEditModeTests`, `StormBoundCurrentEditModeTests`. A listed class that fails after a throwaway implementation is reported, not edited (an `oracle-amend:` with a stated kind is the owner's).

No existing expectation changes: there is no `oracle-amend:` commit.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/MapRasterEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/MapRasterEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-MAP-RASTER-1.md`

## 9. Validation commands

- `bash .agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> MapRasterEditModeTests,MapRasterControllerEditModeTests,MapScreenEditModeTests,MapPaintEditModeTests`
- `bash .agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-MAP-RASTER-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Sonnet. One file, a cache with reference counts and a derive with a precise dirty-tile rule. The hard parts: texel-for-texel equality between a whole build and a derive (one shared texel function), the parking rules (Current theme only, one spare, destroyed before a whole build), and the unconfirmed native-texture calls (settled in the oracle author's scratch run).

## 11. Known traps

- The cache and the spare are static and outlive a test and a rig. Another class may now get a parked picture of an equal map and theme: correct (same pixels) unless it counts textures or expects the old one destroyed. Run the listed classes on the implementation and report any that fail; do not edit them.
- A spare parked before a theme change keeps its old theme until the next whole build destroys it. A theme object changed IN PLACE keeps matching (as today). `GetPixelData` needs a readable texture: keep `Apply(true, false)`. In EditMode destroy with `DestroyImmediate`, as `Release` does. Compare `Regions` by reference first.
- When two kin are held at once the derive's kin is the implementer's choice, but `LastDerivedTexels` is counted against the kin it used; the oracle's counts are written so that exactly one kin is alive (a test releases a derived picture before it derives a second one from the original).

## Not established

- Which path lost the picture in the measured frame (Ended, or the timeline-reset pair of a snapshot source cut): the recorder has no session-state column. The chain is read; oracle test 1 proves that a rebind rebuilds on the base.
- What a derive costs in a Web build (mip rebuild and upload of 960 x 960: native work the oracle does not bound). Follow-up, the lane's next Web measurement: the frame of a destroyed-terrain delivery stays under 16.7 ms on the lane's machine with vsync on. Until then the mid-fight case is NOT claimed as fixed.
- That the two call sites of the shared texel function give bit-identical floats under IL2CPP (settled under Mono by the oracle's texel-for-texel tests only).
- What the large surface shows after a theme change while the Tab map is closed (it unsubscribes while disabled): not looked at; the theme changes only in tests.

## Oracle: the tests, and what is red on the stub

Fixture (a) `MapRasterEditModeTests` (`TerrainRaster` used directly; SetUp drops the spare and sets `UiTheme.Current` to the fixture's `Skin()`; maps are 16 x 16, 8 texels a tile, 64 texels a tile squared). Fixture (b) `MapRasterControllerEditModeTests` (the real `TacticalMapController` rig copied from `MapPaintEditModeTests`, Welcome map 16 x 16 with region bands, walls at (5,5) and (6,5), a low at (10,9); surfaces `Minimap/TerrainFrame` and `TacticalMap/MapFrame/PublicMap`). "Reference picture": a whole build under a fresh `UiTheme.Skin()` (never Current), compared mip 0, mip 1 and the last mip texel for texel after a premise line that the two themes' colours are equal.

Red on the stub (11 tests, each on a behaviour assertion; run with `unity-class.sh`, 2026-11-23):

| Test | First red assertion |
|---|---|
| (b) `ARebindKeepsTheTerrainPicture` | the minimap shows the same picture object (the old texture is destroyed by `Bind`; expected same as the destroyed one) |
| (b) `AnUnbindAndRebindKeepsThePicture` | the same |
| (b) `ADestroyedTerrainDeliveryPatchesBothSurfaces` | "no whole build": `FullBuilds` rose by one |
| (a) `AnEqualMapObjectReusesThePicture` | the last release of a Current-theme picture parks it (`HasSpare` false) |
| (a) `APictureHeldTwiceIsParkedAtTheSecondRelease` | held twice: one entry (an equal map object is another entry) |
| (a) `ADestroyedCellIsPatchedNotRebuilt` | "no whole build" (`FullBuilds` + 1) |
| (a) `ARestoredMapIsPatchedBack` | "premise: B is derived from A" (`DerivedBuilds` + 0) |
| (a) `CellsAtTheBorderAndInALineStillMatchAWholeBuild` | "(i) no whole build" |
| (a) `TheOldPictureIsNotKeptBesideTheNewOne` | "premise: two holders of one picture" (an equal map object is another entry) |
| (a) `OnlyOnePictureWaitsUnheld` | "A is parked" (`HasSpare` false) |
| (a) `ADeriveAllocatesLittleManagedMemory` | "one derive grows the managed heap by 7000064 bytes" (expected under 1,048,576) |

Green on the stub and after (standing guards): `TheFirstPictureIsAWholeBuild`, `AThemeChangeStillBuildsWhole`, `AnotherMapBuildsWhole`, `APictureOfAnotherThemeIsDestroyedAtItsLastRelease`, `AThemeChangeLeavesNothingParked`.

Existing classes run on the stub in the same Unity run and all green, unedited: `MapScreenEditModeTests`, `MapPaintEditModeTests`, `MapCoverEditModeTests`, `TabMapFlowEditModeTests`, `TacticalMapEditModeTests` (84 tests in the run: 73 passed, the 11 above failed). The other classes of the list above were not run by the oracle author: the implementer runs them after the implementation.

## Notes from the oracle author

- **Scratch run (every line can run).** One run per new class with a catch-and-collect `Assert` shadow in a scratch namespace (a copy of the file, deleted, not committed): every line of every test ran on the stub; the collected failures were all assertions about the missing behaviour (the lists above), no other exception, none from the set-up or the deliveries. Lines that can only be tried once the derive exists (the texel-for-texel comparisons `Differs`, which compare the picture with a whole build of the same map under another `Skin()`) already ran and pass on the stub, where every picture is a whole build: they are standing equalities, and they bite as soon as a derive writes a wrong texel.
- **Heap meter (test 9, `ADeriveAllocatesLittleManagedMemory`) is in.** `UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()` checks out on its control: a kept `new byte[4 * 1024 * 1024]` reads 4,198,400 to 4,210,688 bytes in three rounds (and `GC.GetTotalMemory(false)` reads the same). The test repeats the control on every run (at least 3 MB) after a `GC.Collect()`, then asserts that one derive of a 160 x 160 map (6 texels a tile) grows the managed heap by under 1,048,576 bytes. **The whole build's reading:** 7,000,064 bytes in the stub's run (a probe of three whole builds read 26.6, 21.3 and 7.0 MB: the first two include heap growth; the steady figure is 7.0 MB, the 6.96 MB of the arithmetic above). So the reviewer's by-reading check is "no managed array of the picture's size" in addition.
- **Native calls, settled in a probe run (6000.0.85f2, deleted).** On a new readable `RGBA32` texture with mips: `GetPixelData<Color32>(0)` is a writable view (a write is seen after `Apply`); `GetPixelData<Color32>(0).CopyFrom(other.GetPixelData<Color32>(0))` copies mip 0; `Apply(true, false)` rebuilds the mips (mip 1 read the copied colour) and the texture stays readable (`isReadable` true, 7 mips for 64 x 64).
- **Spec split into more tests than numbered.** Spec test 3 is two tests here (`AnEqualMapObjectReusesThePicture` and `APictureHeldTwiceIsParkedAtTheSecondRelease`). Test 5 and 7 acquire under the fixture's Current theme (they need parking); tests 4, 6, 9 and the guards use a fresh `Skin()`.
- **Two kin at once.** In test 6 the line picture is released before the corner is derived, so exactly one kin (A) is alive for each count; with both alive the derive's kin would be the implementer's choice and the count would depend on it.
- **Counters are cumulative across the whole run** (static, never reset): every test reads them as a difference.
- **No `oracle-amend:`.** No existing expectation changes.
- **Test file shape.** `RasterFixtures` (internal static class) holds the maps and the picture comparison; it shares the file with the two fixtures.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
