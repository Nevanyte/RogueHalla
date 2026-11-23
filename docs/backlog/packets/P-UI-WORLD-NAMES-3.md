# P-UI-WORLD-NAMES-3: a pack's name glides to a new carrier or size, keeps its lift, reads where it is drawn

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 (UI rework lane chat): "the stat above a camp
  shimmers and glitches as a camp is moving around it", "make the front end as fluid as possible". Left-overs of
  P-UI-WORLD-NAMES-2 (Web round 3a, 2026-11-23): a pack's name stepped about 30 px sideways on hover.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (twenty wave 4 packets on integration 87, with
  P-UI-WORLD-LABELS-1 merged: `SpriteActorPresenter.LocalCluster.cs` exists). The presenter, the name layout library and the
  name tests are as the spec read them.
- **D-rules touched:** D1: legacy never moves a name after it is made (`packages/client/src/render/MobView.ts:22,108`,
  `PlayerView.ts:65,171`) and has no pack, carrier or hover rule (Unity's, DEV-024): no legacy motion to match. Propose
  ONE added sentence for the DEV-042 row that P-UI-WORLD-NAMES-2 proposed (not in `docs/DEVIATIONS.md` yet, no new row):
  a pack's name glides when its carrier, the carrier's singled-out state or its member count changes and a lift follows a
  changed key; a name's colour follows the ground at its drawn place. D2 n/a. D5/D6 none. D9: drawing only.
- Evidence: `.agent-runs/lane-ui-rework/understand/web3-names-leftovers.txt` (its CHECK governs: picture 22's height is
  headroom, not a lift); round-3a pictures 34, 35.

## 1. ID and outcome

Presentation only (Unity draws nothing a rule decides). Units: world units (1 = 32 legacy px). Time: the `dt` of
`End(dt)`. `UiMotion.Reduced` snaps.

### What is wrong today (code of the base)
1. **The step.** `MergeMobNames` (`SpriteActorPresenter.Names.cs:131`) gives `NameMerge.PackNames`
   (`NameMerge.cs:131`) the members' name places. A hovered (`Single` 2) or locked (1) member becomes the carrier and the
   anchor is its own place, else the middle of the extent in x and the highest member's y; `art.Ind.MoveName`
   (`Names.cs:185`, `OverheadIndicators.cs:163`) writes it that frame. So hover steps the anchor, also when it falls on
   the default carrier (the lowest Id) and nothing else changes, and so does a member that joins, dies or drops out of
   sight while the carrier stays. The layout key does not change on hover (`LayoutNameKey`, `Names.cs:182`: the camp key,
   else the pack's lowest serial).
2. **The key change.** A mob beyond `camp.R + NameMerge.CampReachPx` (`NameMerge.cs:187,197`) gets `Camp` 0
   (`CampOfMob` returns -1); a camp-less pack that loses its lowest serial changes key too.
   `OverheadBoxLayout.Solve` (`OverheadBoxLayout.cs:81`) finds no memory for the new key and calls `LabelLift.Start(w)`
   (`:105`): value, target and hold are gone, so a held lift drops.
3. **The ground.** `FinishNames` (`Names.cs:115`) ends `MergeMobNames` (`:188`), so it runs before `LayoutNames` and
   samples `Surfaces.SurfaceAt(art.Ind.NameBasePosition)` (`:123`), the unlifted place; a name lifted up to `MaxLift`
   (2.5) can stand on other ground than the one its colours were chosen for.

### Outcome

1. **`NameAnchorGlide`** (new, library). Per layout key: token, last drawn anchor, residual (x, y), speed.
   `Step(key, token, targetX, targetY, dt, reduced, out x, out y)`, in order: (a) snap (residual 0, speed 0, token
   stored) when the key has no entry, or `reduced`, or dt is not in (0, `MaxDt`] (NaN too); (b) else, when `token`
   differs from the stored one, residual = last drawn - target and speed = its length / `GlideSeconds` (a residual of
   length 0 stays 0 with speed 0: no division, no NaN), token stored; (c) shrink the residual along its line by speed x
   dt, never past 0 (the change frame shrinks too); (d) drawn = target + residual, kept as last drawn. Constants:
   `GlideSeconds` .15, `MaxDt` 1 (it mirrors `OverheadBoxLayout.ForgetSeconds`). No clamp: every step, of any size,
   glides in `GlideSeconds`. `Begin()` marks entries unseen, `Step` marks its key seen, `End()` drops unseen ones (a
   name not shown the frame before snaps on return), `Clear()` drops all. `Carry(oldKey, newKey)` copies the entry of
   `oldKey` (token, last drawn, residual, speed) to `newKey` when `oldKey` has one and `newKey` has none, so the new
   key's first frame is not a first sight; true when it copied; else nothing changes. The old entry stays. The token is
   what the presenter hands in (2): the carrier's serial, whether the carrier itself is singled out, and the member
   count. Only a change of token makes a residual: a pack that walks, with the same token, is never delayed. Accepted,
   not removed: a token that flickers (a far member of a camp pack at the edge of sight, in and out every few frames)
   restarts the glide from the place drawn then, so the name rests near the middle of the two anchors and trembles
   instead of jumping the whole step: with a flip every frame at 60 fps it rests between 8/17 and 9/17 of the step,
   moves 1/17 of it (about a sixteenth) a frame, never more than a ninth. No debounce. Worked numbers at dt = 1/60:
   - A at (0,0) first sight; next frame B at (1,0): residual -1, speed 6.667, shrink .1111, drawn x .1111; +.1111 a
     frame; 1.0 on the ninth frame counting the change frame.
   - A step of 3: residual -3, speed 20, drawn x 3 - 3 + .3333 = .3333 on the change frame: the first move is dt /
     `GlideSeconds` of the step, whatever its size.
   - After the first case's change frame the target moves to 1.2: drawn x .4222 (residual -.8889 -> -.7778).
2. **Presenter wiring.** `End(float dt)` (`Names.cs:24`) calls `MergeMobNames(float dt)`, then, unless
   `WorldLabelVisibility.Hidden`, the layout branch (solve or `ApplyLifts`) and `FinishNames` (5). On a stamp not yet
   merged (a new private `int _glideStamp = -1;` compared with `_stamp`, as `_solvedStamp` (`Names.cs:21`) guards the
   layout): `_glide.Begin()` before the item loops, the Carry pass (4), the steps, then `_glide.End()` and
   `_glideStamp = _stamp`, also when no name is shown. In the final loop, per shown item with `!Elite`:
   `_glide.Step(art.LayoutNameKey, token, anchorX, anchorY, dt, UiMotion.Reduced, out gx, out gy)`, `root` =
   `_mergeScratch[i]`; set `art.GlideStamp = _stamp, GlideX = gx, GlideY = gy`; `MoveName(gx, gy)` when it differs from
   the item's x or y. The token is a long: `(art.LayoutKey * 2L + (_mergeItems[i].Single != 0 ? 1L : 0L)) * 64L +
   Math.Min(_packSize[root], 63)`: the carrier (`art.LayoutKey`, kept by a renewed handle), whether the carrier ITSELF
   is singled out (hovered or locked; a lock acts as a hover), and the member count. It catches a change of carrier, a
   hover or lock that starts or ends on the carrier itself (carrier and count stay; the anchor moves from the middle of
   the extent to the carrier's own place, half the extent: 1.0 for the tests' pair), and a join, a death or a drop out
   of sight (the count). A hovered lone mob flips the bit too; its anchor is its own place either way, so the residual
   is at most one frame's walk. Accepted hole: a member that dies while another joins in the same frame keeps the count,
   and the anchor snaps. A pack of more than 63 counts as 63. On a stamp already merged (a second `End`, with or without
   the indicators presented again) nothing steps: a shown item with `art.GlideStamp == _stamp` is moved to `GlideX,
   GlideY`. `ClearNameLayout` (`Names.cs:53`) also calls `_glide.Clear()` and sets `_glideStamp = -1`. The lift is
   unchanged: the box origin is `NameBasePosition` (`OverheadIndicators.cs:166`), now the glided anchor. Hover still
   ends at the hovered creature.
3. **`OverheadBoxLayout.Carry(oldKey, newKey)`:** copies the memory entry of `oldKey` to `newKey` when `oldKey` has one
   and `newKey` has none (value, target, hold kept; age 0); true when it copied; else nothing changes. The old entry
   stays (members of a split pack still use it; an unused one ages out).
4. **Presenter Carry**, in the same new-stamp pass, in the merge before the solve, not in `LayoutNames` (a pack's new
   carrier was hidden the frame before and gets `LayoutNameKey` only when shown; the merge sees every member). Every
   non-elite mob item, shown or hidden, has a new key: `Camp > 0 ? CampKey(Camp) : _packKey[root]`. Every new key has
   ONE source, so no result depends on the order of `_active`: the call is made only on the pack's lowest-serial member
   (the art whose `SerialKey(LayoutKey)` equals `_packKey[root]`, already computed in the merge, `Names.cs:172`), with
   that art's old key; camp keys follow the same rule. When that art has `NameKeyStamp == _stamp - 1`, `NameKeySerial
   == art.LayoutKey`, a `NameKeyPrev` that differs from the new key, AND the new key's pack has more than one member
   (`_packSize[root] > 1`), it calls `_layout.Carry(old, new)` and `_glide.Carry(old, new)`: the one place both are
   called, one source, one condition. Then every non-elite item (the hidden too: it may be the lowest serial next
   frame) records `NameKeyPrev`, `NameKeyStamp`, `NameKeySerial`. A creature that leaves a pack and stands alone has a
   pack of one: no Carry, its name starts at lift 0 and as a first sight, as on the base (the lift was the clearance of
   the pack's name; copied, it would float the stray's name up to `MaxLift`, 2.5 units, over its head for up to about
   1.3 s). Accepted, the one hop left: a pack that shrinks to one member whose key changes (its lowest-serial member
   went) starts that lone name at lift 0 and its anchor as a first sight.
5. **Ground colour.** `FinishNames` leaves `MergeMobNames` and runs in `End` after the layout branch, unless
   `WorldLabelVisibility.Hidden`. It samples `Surfaces.SurfaceAt(base + (0, lift, 0))` (`SurfaceMap.cs`, tile row =
   `floor(-y / ArenaView.GroundSquash)`), `base` = `NameBasePosition`, `lift` = `art.LayoutLift` when `art.LayoutStamp ==
   _stamp`, else 0: the place the name is DRAWN in that frame, also while its lift climbs or falls. One sample per name
   per `End`, as today. A sample outside the map (the border ring, a negative or too large tile) reads
   `SurfaceKind.None`, dark ground, and throws nothing (`RegionMap.RegionAtTile` range-checks both indices,
   `RegionMap.cs:50`). `SetNameGround` (`OverheadIndicators.cs:196`) keeps its hysteresis.

## 2. Dependencies
P-UI-WORLD-LABELS-1 (merged on the base: `SpriteActorPresenter.LocalCluster.cs`, one internal method in
`OverheadIndicators.cs`; it reads no member this packet edits). P-UI-ALLOC-METER-1 and -2 (`AllocationMeter.cs` on the
base).

## 3. Relevant legacy paths
- `packages/client/src/render/MobView.ts:22,108`, `packages/client/src/render/PlayerView.ts:65,171`: legacy places a
  name once, never moves it, and has no pack, carrier, hover or lift rule. Nothing to match; the numbers above are the
  lane's presentation choices (DEV-024, DEV-042 proposal).

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/NameAnchorGlide.cs` (library; needs a `/// <summary>` before its type) and its
  synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/NameAnchorGlide.cs` (+ `.meta`).
- Tests (oracle files, below).

## 5. Exclusive write scope
- under `client-unity/UnityProject/Assets/Scripts/Presentation/`: `SpriteActorPresenter.Names.cs` (`End`,
  `MergeMobNames`, `FinishNames`, `ClearNameLayout`, `_glide`, `_glideStamp`) and `SpriteActorPresenter.ActorArt.cs`.
- `client-unity/RoguehallaNet/Net/Ui/OverheadBoxLayout.cs` (`Carry`), `client-unity/RoguehallaNet/Net/Ui/NameAnchorGlide.cs`,
  their synced copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (`bash client-unity/sync-net.sh`)
  and the new file's `.meta`.
- Not in scope: `OverheadIndicators.cs`, `OverheadExtras.cs`, `SpriteActorPresenter.cs`,
  `SpriteActorPresenter.Overheads.cs`, `NameMerge.cs`, `NameStack.cs`, `LabelLift.cs`, `OverheadCluster.cs`,
  `View/ArenaView*.cs`, `CampRespawnLabels.cs`, `WorldMarks.cs`, `WorldIndicators.cs`, `WorldNamesRig.cs`, every existing
  test file; the lift rule, the bars filter, a look's head height, the unused `NameStack` code.
- Line budget (the gate fails a C# file above 800): `SpriteActorPresenter.cs` 596 (add none), `.Names.cs` 191,
  `.ActorArt.cs` 55, `OverheadBoxLayout.cs` 212. Keep each new file under 800.
- API (library, namespace `Roguehalla.Net.Ui`), as stubbed:
  ```csharp
  public sealed class NameAnchorGlide
  {
      public const float GlideSeconds = .15f, MaxDt = 1f;
      public void Begin(); public void End(); public void Clear();
      public void Step(long key, long token, float targetX, float targetY, float dt, bool reduced, out float x, out float y);
      public bool Carry(long oldKey, long newKey);
      public int Count { get; }                                     // entries now
      public bool TryResidual(long key, out float x, out float y);  // false when the key has no entry
  }
  // OverheadBoxLayout, beside TryMemory:
  public bool Carry(long oldKey, long newKey);
  ```
  Arrays that double when full, linear find, swap-remove in `End`, no `Dictionary`. New `Art` fields
  (`SpriteActorPresenter.ActorArt.cs`): `int GlideStamp = -1; float GlideX, GlideY; long NameKeyPrev; int NameKeyStamp =
  int.MinValue; int NameKeySerial;` (`int.MinValue`: stamp 0 must not match). The stub already has them, plus
  `MergeMobNames(float dt)` and `int _glideStamp = -1;`.

## 6. Contracts and references
- C-INPUT, C-OBS untouched (drawing only). Unity holds no rule (COMMON, lane rules). Motion: unscaled time passed in as
  `dt`, instant under reduced motion, no allocation in a steady frame.

## 7. Behaviour to preserve
- The lift rule, the bars filter, the pack anchor rule of `NameMerge.PackNames`, the carrier choice, eviction last in `End`.
- Order in `End`: merge (with dt), layout or `ApplyLifts`, `FinishNames`, then the unchanged eviction loop last.
  `LayoutNames(float)` keeps its signature.
- Existing tests that pin this code, all expected GREEN and unchanged (their `Frame` ends with `End()`, the EditMode clock
  is frozen so dt is 0, the glide snaps and `Solve(0)` clears what a Carry copied). Each read for this packet:
  - `NamePackGroup2EditModeTests`: `AnIdlePackShowsOneNameCentredOverThePackAndAboveItsHighestMember` (anchor = centre of
    the extent x, highest y); `AHoveredMemberCarriesThePacksNameOverItself` and `ALockedMemberCarriesThePacksNameOverItself`
    (the name stands at the member's own place, `Near` .02, after ONE frame at dt 0: the glide must snap);
    `HoverWinsOverLock`; `WhenTheHoverLeavesTheNameGoesBackOverThePack` (centred again after one frame); `AnEngagedPackStill...`,
    `OneEngagedMemberDoesNotMakeItShowItsOwnName`, `AnEliteKeepsItsOwnName...`; the fighter-stack test
    (`FightersStandingTogether...`); the camp-label tests. `End()` with no dt, dt 0 on a frozen clock.
  - `NamePackFix4EditModeTests`: `ACampsMobsOfOneNameShowOneNameHoweverFarTheySpread` (name x centred over the extent, .02),
    `AHoveredMemberOfASpreadCampPackCarriesTheOneName`, `TwoCampsOfTheSameMobStandingTogetherKeepTwoNames`,
    `MobsOfNoKnownCampKeepTheDistanceChain`, `WithNoCampsDeliveredTheChainStillApplies`,
    `APackNameThatWouldLandOnTheLocalFightersNameIsLiftedClearAndTheFightersNameStays`,
    `NoNameIsDrawnWhileWorldLabelsAreHiddenAndTheyReturnWhenShownAgain` (Hidden: no name active, back after).
  - `NamePackEditModeTests` (`AMobThatWalksAwayGetsItsNameBack...`, `TheSameFrameTwiceChangesNothing`, hp-bar/name
    overlap tests) and `NamePackHoverEditModeTests` (hover frames over four mobs; `End()`).
  - `NameLegibleEditModeTests`: the uniform-map ground tests (`ANameReadsOnTheGroundUnderIt`, `TheLoneIdleMob...Snow`,
    the elite and local ones, `WithoutASurfaceMapANameKeepsTheLightFace...`) and `TheNameColoursFollowAMobAcrossAZoneEdge`
    (a mob walks tile 15 to 25 across a Snow/Basalt column split: dark then light then dark; colours follow the ground
    under the name's place, lift 0 on a lone name, so the new sample place equals the old).
  - `WorldNamesEditModeTests` and `WorldNamesTimeEditModeTests` (rig tests with `End(dt)`, dt 1/60) stay green only
    because no scene of theirs changes a pack's carrier, singled-out state or member count between frames (the rig has no
    hover): `MeasurePicture` and `Settle` slide two Chain Wardens that stay two; `ARenewedHandleKeepsItsLiftAndItsPlace`
    drops an elite and renews a lone Pardoner's handle; the brutes of `OtherActorsBarsLiftANameButNeverTheLocalFightersOwn`
    are lone; `EndTwiceOnOneFrameChangesNothing` (`:344`) stays on one stamp (second `End`, then `Reindicate` and `End`:
    nothing changes). `LayoutNamesAllocatesNothing` measures `LayoutNames(float)` by reflection (signature kept).
  - `NameLayoutKeyEditModeTests` (`TheLayoutSerialIsGivenOncePerArtAndKeptByARenewedHandle`: `LayoutKey` given once per
    art, kept by a renewed handle; `End()` then `StepFades()`).
  - `OverheadBoxLayoutTests` (dotnet): `Solve`, memory, hold; `Carry` adds a member and changes none of them.
  A scene that does change a carrier, a singled-out state or a count between frames needs an `oracle-amend`: none known.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-WORLD-NAMES-3 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter "NameAnchorGlideTests|OverheadBoxLayoutCarryTests"` green;
  `bash client-unity/sync-net.sh --check` clean.
- Unity classes `WorldNamesGlideEditModeTests, WorldNamesCarryEditModeTests` green, plus the classes of section 7.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` unaffected (no `MatchHud` member touched).

## Oracle files (read-only for implementers)
(filled in at the oracle commit)

## 9. Validation commands
- `.../bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "NameAnchorGlideTests|OverheadBoxLayoutCarryTests"`
- `.../bin/unity-class.sh <worktree> WorldNamesGlideEditModeTests,WorldNamesCarryEditModeTests`
- the existing classes of section 7, three at a time.

## 10. Risk and suggested model tier
implementer-sonnet: a small stateful library class, two presenter edits (merge and finish). Hard parts: the stamp guard
(never step twice on one stamp), the one-source Carry rule (only the lowest-serial member calls it, never the order of
`_active`), and moving `FinishNames` after the layout without a second ground sample.

## 11. Known traps
- **Stamp guard.** After the first `End` the carrier's name stands at its glided anchor with `_shift` set, and a second
  merge reads that place as the item. Never step twice on one stamp; find the stored anchor by `art.GlideStamp`, not by
  `LayoutNameKey` (a second `End` sees a lone carrier whose pack key can differ).
- **Order in `End`:** merge (with dt), layout or `ApplyLifts`, `FinishNames`, then the eviction loop last.
  `_glide.Begin()` and `End()` run on every new stamp even with no name shown, or a vanished pack's entry survives.
  `MergeMobNames(float)` is found by reflection by the allocation test (private instance method, one `float` parameter).
  `_glideStamp` is found by reflection too (private instance int field, name exact).
- A pooled art is reused with a fresh `LayoutKey` (`SpriteActorPresenter.cs:154`): `NameKeySerial` stops it inheriting the
  old creature's key. The items come in the arbitrary order of `_active`: only the lowest-serial member calls Carry.
- `TextMeshPro` (3D) names measure the same asleep and awake; a `TextMeshProUGUI` does not (COMMON). World names are 3D.
- Code is read by member name, not by line.

## Notes from the oracle author
(filled in at the oracle commit)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
