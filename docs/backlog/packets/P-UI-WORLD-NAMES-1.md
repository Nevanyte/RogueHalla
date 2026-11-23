# P-UI-WORLD-NAMES-1: the rule that keeps names apart, as a pure library, and the name pass in a file of its own

- **Serves:** not in ORDER_OF_OPERATIONS (UI rework lane). Owner direction 2026-11-22: "Currently there is a unity bug
  where the stat above a camp shimmers and glitches as a camp is moving around it" and "Please do a UI optimization
  sweep"; Web round 1 (2026-11-23): two actor names run together, world labels collide with the player's plate. First of
  two packets: a library with no caller and a move, so nothing a player sees changes. P-UI-WORLD-NAMES-2 draws the rule.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-x`)
- **D-rules touched:** D1: no legacy behaviour is touched and no drawn pixel changes, so no `DEVIATIONS.md` row here
  (the second packet proposes the one) · D2 n/a · D5/D6 none · D9: drawing only, no key bound.

Paths: `Pres/` = `client-unity/UnityProject/Assets/Scripts/Presentation`, `Lib/` = `client-unity/RoguehallaNet/Net/Ui`
(its Unity copy is `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui`, made by `bash client-unity/sync-net.sh`).
Line numbers are those of the lane base.

## 1. ID and outcome

Presentation only; nothing a player sees changes. Three parts.

**1. The move.** The name pass is 177 lines of `Pres/SpriteActorPresenter.cs` (772 lines; limit 800, and the lane rule
forbids adding a line to it). It goes to a new partial `Pres/SpriteActorPresenter.Names.cs` (a `/// <summary>` before the
type, the usings it needs, at most 800 lines, about 190). Moving, by cut and paste:
`End()` whole (`:71-80`; its other statements, `_frameLaunches.Clear()` and the eviction loop, travel unchanged: the second
packet rewrites its body), the fields `_mergeItems`, `_mergeShown`, `_mergeIds`, `_mergeScratch` (`:70`), `_stackItems`,
`_lift`, `_stackIds`, `_anchorX`, `_anchorY`, `_packFade`, `_packSize` (`:81-83`), `_packNameIds` (`:83`), `_nameRects`
(`:84`), the constant `BarClearance` (`:151`), and the methods `FighterNameRects` (`:85-103`), `FinishNames` (`:154`),
`ClearOfBars` (`:166-182`), `MergeMobNames` (`:184-245`), `StackFighterNames` (`:247-291`). No statement, comment, name or
order changes; only the wrapper, usings and summary are added. The main file ends near 595 lines. The three
`WorldLabelVisibility.Hidden` gates are untouched in behaviour: `MergeMobNames`' branch (`:191`) moves with the method;
the bar-loop bound in `Present` (`:561`) stays; the one in `OverheadIndicators.Update` (`OverheadIndicators.cs:247`) is
outside this packet.

**2. The serial key.** `public int LayoutKey;` on `Art` (`Pres/SpriteActorPresenter.ActorArt.cs`); the statement
`a.LayoutKey = ++_layoutSerial;` appended to the line `a.Root.SetActive(true); a.LastPosition = ...` (`:330`, the
non-continue branch of `Present`); `int _layoutSerial;` declared in `Names.cs`. `TryContinue` never touches it, so a
renewed handle keeps the art's key; a reused pooled art gets a fresh one. Nothing reads it but one EditMode test
(reflection). It is the one non-move statement in the main file.

**3. The library.** `Lib/OverheadBoxLayout.cs` and `Lib/OverheadCluster.cs` (namespace `Roguehalla.Net.Ui`, no caller in
this packet): the rule that lifts boxes (a name with its marks) clear of each other, with one memory per key. Rules below.

### The rules (Outcome of the library)

1. A BOX has a Key (`long`), a Class (`byte`), an origin (world units), `avoidFixed` and any number of PARTS: rectangles
   as offsets from the origin; it rises as one rigid body. A FIXED rectangle never moves in a solve (later: other actors'
   bars); every box avoids them unless its `avoidFixed` is false.
2. **Order.** Visiting order is ascending `(Class, Key)` by repeated minimum scan (no sort), equal pairs by add index. A
   box yields only to boxes visited before it; nothing in the order reads a position. Classes: local fighter 10, other
   fighter 20, elite 30, mob name 40; prompt 15, giver 25, prop 50 reserved. A Key is opaque and distinct within a solve;
   the memory follows the key, never the geometry. Convention the second packet applies: `SerialKey(n)` = n (from
   `Art.LayoutKey`: 1 to `int.MaxValue`), `CampKey(c)` = `CampKeyBase + c` (2^32; c a one-based int: at most 2^32 + 2^31),
   `FighterKey(id)` = `FighterKeyBase + id` (2^33; id a `uint`: below 2^34): the three ranges cannot meet.
3. **Placement.** `Place(B, padX)`: `L = 0`; repeat (at most 32 passes, until a pass raises L) for each part r of B
   (raised by L), each part q of each earlier box (raised by that box's TARGET in this solve) and, if `avoidFixed`, each
   fixed q: if `r.x0 - padX < q.x1 && q.x0 < r.x1 + padX && r.y0 < q.y1 + Gap && q.y0 < r.y1` then
   `L = max(L, q.y1 + Gap - r.y0)` (r.y0 unraised); result `min(L, MaxLift)`. B is measured at its base, never where it
   was drawn. `Gap`, `PadEnter`, `PadKeep`, `MaxLift` are public constants (first guesses, see "Not established"); the
   rule needs only: all positive, `PadKeep > PadEnter`, `MaxLift` at least the tallest cluster plus `Gap`.
4. **Hysteresis, memory, time.** `Solve(dt, reduced)`. A good dt is in (0, `ForgetSeconds`], never NaN. A solve with a bad
   dt clears the memory, solves every box as first sight (`LabelLift.Start`) and records no entry, so `MemoryCount` is 0
   after it. A good dt, per box in order: `w = Place(B, PadEnter)`; raised = the key has an entry with
   `Target > LabelLift.Deadband`; if raised and `w < Target - Deadband`: `wk = Place(B, PadKeep)`, and if `wk > w` then
   `w = min(wk, Target)` (the band decides whether a lift is kept, never how much). No entry: `LabelLift.Start(w)`; an
   entry: `Step(w, dt, reduced)`; then an entry is recorded for the box, a lift of 0 included. An entry not seen in a
   solve ages by dt and goes above `ForgetSeconds`. Per box `Lift` is drawn and `Target` is what later boxes avoid; both
   read 0 before a solve. A steady `Solve` allocates nothing. `LabelLift` is `Lib/LabelLift.cs` (`Start`, `Step`;
   constants `RiseUnitsPerSecond` 8, `FallUnitsPerSecond` 3, `HoldSeconds` .45, `Deadband` .03): reuse it, do not edit it.
5. **Cluster geometry.** `OverheadCluster.AddParts` turns a stack from `OverheadStack.Build` (`Lib/OverheadStack.cs`)
   into parts of the latest box, in stack order. `k = worldPerLocal * OverheadStack.UnitsPerPx` (`.026`); nameC = the
   Name element's `Y + H/2`; y is relative to the name's centre. Name: `(-w/2, -h/2, w/2, h/2)`, `w = nameWidth`,
   `h = nameHeight`. WishIcon, MarkPips, BountyNumber, StatMark: `x` from `k (X - W/2)` to `k (X + W/2)`,
   `y0 = k (Y - nameC)`, `y1 = k (Y + H - nameC)`. BountyCrown: a square of side `.9 W k` on the element's centre.
   SecondaryWeapon and StatMark: that rectangle grown `.025 * worldPerLocal` each side. Elite star (`eliteStar`): a
   square of side `EliteStarSize * EliteStarPlate * worldPerLocal`, centre x 0, centre y
   `k (nameTop - nameC) + worldPerLocal (EliteStarRise + EliteStarSize / 2)`, nameTop = Name `Y + H`. HpBack, HpFill,
   BarrierFill are not parts; no Name element adds nothing (not even the star).

### Public surface (exactly these names; the tests call them)

```csharp
/// <summary>Lifts boxes (a name and its marks) clear of each other, with one memory per key.</summary>
public sealed class OverheadBoxLayout
{
    public const float Gap = .10f, PadEnter = .16f, PadKeep = .40f, MaxLift = 2.5f, ForgetSeconds = 1f;
    public const byte ClassLocal = 10, ClassPrompt = 15, ClassFighter = 20, ClassGiver = 25, ClassElite = 30,
        ClassMob = 40, ClassProp = 50;
    public const long CampKeyBase = 1L << 32, FighterKeyBase = 1L << 33;
    public static long SerialKey(int serial);
    public static long CampKey(int camp);
    public static long FighterKey(uint actorId);
    public void Begin();   // clears boxes, parts and fixed rectangles; keeps the memory
    public int AddBox(long key, byte cls, float x, float y, bool avoidFixed);   // returns the box index
    public void AddPart(float x0, float y0, float x1, float y1);   // to the latest box (no box: nothing)
    public void AddFixed(float x0, float y0, float x1, float y1);
    public void Solve(float dt, bool reducedMotion);   // reads no clock
    public int BoxCount { get; }
    public float Lift(int box);     // drawn lift, world units up
    public float Target(int box);   // the lift it moves toward, which later boxes avoid
    public int PartCount(int box);
    public void PartRect(int box, int part, bool atTarget, out float x0, out float y0, out float x1, out float y1);
    public bool TryMemory(long key, out float value, out float target);
    public int MemoryCount { get; }
    public void Clear();   // forgets the whole memory
}
public static class OverheadCluster
{
    public const float EliteStarSize = .3f, EliteStarPlate = 1.4f, EliteStarRise = .02f;
    public static int AddParts(OverheadBoxLayout layout, List<OverheadElement> stack, bool eliteStar,
        float worldPerLocal, float nameWidth, float nameHeight);   // returns how many parts it added
}
```

`PartRect` is origin plus offset, raised by `Target(box)` if `atTarget`, else by `Lift(box)`.

## 2. Dependencies

Base `lane/ui-rework/base-3`. `AllocationProbe` (`client-unity/RoguehallaNet.Tests/AllocationProbe.cs`) is on the base.
`AllocationMeter` (Unity) is not on it and not needed: the allocation test is dotnet.

## 3. Relevant legacy paths

None: no legacy game rule. Drawing is read from `Pres/SpriteActorPresenter.cs`, `Pres/OverheadExtras.cs:236-248` (the
elite star: size `.3` at `:241`, plate `1.4` at `:246`), `Pres/OverheadIndicators.cs:338` (`+ .02f`), `Lib/OverheadStack.cs`
(`Build`, `UnitsPerPx` `.026`), `Lib/LabelLift.cs`, `Lib/NameStack.cs`, `Lib/NameMerge.cs`.
`Pres/SpriteActorPresenter.Overheads.cs:77` still calls `ClearOfBars` (`PlaceSignatureCues`): it must keep compiling and
behaving.

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/OverheadBoxLayout.cs`, `OverheadCluster.cs` (each with a `/// <summary>`), their copies
  under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` and their `.meta`
- `client-unity/UnityProject/Assets/Scripts/Presentation/SpriteActorPresenter.Names.cs` and `.meta`
- Tests (oracle, see below)

## 5. Exclusive write scope

- `Lib/OverheadBoxLayout.cs`, `Lib/OverheadCluster.cs` and their synced copies and `.meta`; no other library file
- `Pres/SpriteActorPresenter.Names.cs` (+ `.meta`); `Pres/SpriteActorPresenter.cs` (the move and the one statement of
  part 2); `Pres/SpriteActorPresenter.ActorArt.cs` (`public int LayoutKey`)
- Not in scope: `OverheadIndicators.cs`, `OverheadExtras.cs`, `SpriteActorPresenter.Overheads.cs`, `View/ArenaView*.cs`,
  `NameStack.cs`, `NameMerge.cs`, `LabelLift.cs`, `OverheadStack.cs`, `CardDisplayCatalog.cs`, `BakedContent.cs`, any
  oracle file. No `oracle-amend:` commit.
- `.meta` files Unity writes for the new Unity-side files are committed with them. Never commit the two StormInk prop
  assets a Unity run rewrites (`Assets/Resources/StormInk/Props/ColiseumTorchArt.asset`, `ColiseumWeaponRackArt.asset`).

## 6. Contracts and references

Presentation only (R-AUTH, ADR 0008): Unity holds no game rule; the library has no clock (time enters as `dt`) and no
`UnityEngine`. Readability gate: a new non-test C# file has a `/// <summary>` before its first type and at most 800
lines; `SpriteActorPresenter.cs` may not gain a line beyond the one statement of part 2 (it loses about 177).

## 7. Behaviour to preserve

The move changes no statement: `git diff -w --color-moved=zebra` shows only moved blocks, the wrapper and the appended
statement. Every class listed under "Existing tests" stays green. The library is not called by anything (no star-constant
wiring, no call from the presenter): P-UI-WORLD-NAMES-2 does that.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-WORLD-NAMES-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter "OverheadBoxLayoutTests|OverheadClusterTests"` is green.
- `NameLayoutKeyEditModeTests` is green under Unity, and the existing classes listed below stay green.
- `bash client-unity/sync-net.sh --check` passes.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/OverheadBoxLayoutTests.cs`
- `client-unity/RoguehallaNet.Tests/OverheadClusterTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/NameLayoutKeyEditModeTests.cs` (and its `.meta`)
- `docs/backlog/packets/P-UI-WORLD-NAMES-1.md`

The `stub:` commit holds `Lib/OverheadBoxLayout.cs`, `Lib/OverheadCluster.cs` (no behaviour), their synced copies and
`.meta`, and `public int LayoutKey;` on `Art`. The implementer replaces them.

## Tests (what each pins; all red on the stub at a behaviour assertion)

Test constants (`OverheadBoxLayoutTests`): `W = 1`, `H = .34`, `Dt = 1f/60f`, `Eps = .01`, `Step = H + Gap`,
`Hold = (int)(LabelLift.HoldSeconds / Dt)`. A box is one part `(-W/2, -H/2, W/2, H/2)`; a box at `x = W + g` has a gap `g`
to a box at 0. Within 1e-4. No gap sits on a pad. Each test names `Gap`, the pads, `MaxLift`, `ForgetSeconds` or a
`LabelLift` constant.

1. `TwoTouchingBoxesTheLaterInTheOrderYields`: A (`ClassElite`, key 1) at 0, B (`ClassMob`, key 2) at `g = PadEnter / 4`:
   first Solve: A lift 0, B lift = Target = `Step`; identical with B added first (read by returned index), with B at
   `-(W + g)`, and with the classes swapped (A yields); equal classes: the lower key stays.
2. `PadsGapAndJitter`: fresh, `g = PadEnter - Eps` gives `Step`, `PadEnter + Eps` gives 0. B directly above at
   `y = Step - Eps` gives `Eps`, at `Step + Eps` gives 0. On a fresh layout, 120 Solves alternating `g = PadEnter / 2` and
   the mid gap `(PadEnter + PadKeep) / 2`: always `Step`. Also `0 < PadEnter < PadKeep`, `Gap > 0`.
3. `TheKeepBandHoldsThenReleases`: fresh at the mid gap: B lift 0 and `MemoryCount` 2. At `PadEnter / 2`: five Solves read
   `min(Step, n * RiseUnitsPerSecond * Dt)`. At the mid gap, 10 Solves: `Step`. At `PadKeep + .05`: Lift = Target = `Step`
   for the first `Hold - 3` Solves, then none rises or falls by more than `FallUnitsPerSecond * Dt + 1e-4`, and the lift is
   0 by Solve `Hold + ceil(Step / (FallUnitsPerSecond * Dt)) + 3`. Separately A also holds `(-W/2, H/2 + .03, W/2, H/2 + .63)`:
   B at `PadEnter / 2` lifts `H/2 + .63 + Gap + H/2` and keeps it 10 Solves at the mid gap; fresh at the mid gap: 0.
4. `AClusterMovesAsOne`: A has the name and a star part `(-.25, H/2 - .05, .25, H/2 + .45)`; B has the name and a part
   `(-W/2, H/2 - .06, W/2, H/2 + .03)`. B at `g = PadEnter / 4`: lift `Step`; `PartRect(B, p, true, ...)` is each part at
   B's origin plus `Target`, `PartRect(B, p, false, ...)` uses `Lift` (the test makes them differ: one Solve far, one in
   contact). B's name at `x = .25 + PadEnter / 2 + W / 2` (star gap `PadEnter / 2`): lift `.45 + H/2 + Gap + H/2`; no part
   of B at target meets a part of A.
5. `FixedRectanglesLiftEveryBoxButOptOuts`: fixed `(-W/2, -.05, W/2, .05)`: B at 0 lifts `.05 + Gap + H/2`, with
   `avoidFixed` false 0; a fixed rectangle whose top is `-H/2 - Gap - Eps` lifts nothing.
6. `AFrozenOrCutClockForgets`: for each dt in 0, -1, NaN, `+Infinity`, `ForgetSeconds + .5`: after one good Solve with
   contact (B `Step`, `MemoryCount` 2), a Solve with that dt and B moved to `PadKeep + .05` reads B lift 0 and
   `MemoryCount` 0. `Clear()` in its place: `MemoryCount` 0 at once; then a good Solve with B at `PadKeep + .05` reads B
   lift 0 and `MemoryCount` 2. Two bad-dt Solves, with contact then without, read `Step` then 0. Ageing: B raised; a Solve
   of `ForgetSeconds * .5` without B keeps `TryMemory(keyB)`, and B back at `PadKeep + .05` reads `Step`; on another
   layout two Solves of `ForgetSeconds * .6` without B: `TryMemory` false, B reads 0.
7. `ReducedMotionJumpsAndKeepsTheHold`: B with an entry (a Solve at `PadKeep + .05` first) meets contact with `reduced`
   true: Lift = Target = `Step` at once; contact gone: `Step` for `Hold - 3` Solves, 0 by `Hold + 3`.
8. `TheStackTheCapAndTargets`: `n = ceil(MaxLift / Step) + 2` boxes at one place, `ClassFighter`, keys 1 to n: lift of box
   i (from 0) is `min(i * Step, MaxLift)`; the last is `MaxLift`. Targets, not drawn lifts, are avoided: A and B (key 2)
   `PadKeep + .05` apart, one Solve; then B onto A and C (key 3) added there: `Lift(B)` is `RiseUnitsPerSecond * Dt`,
   `Target(B)` is `Step`, C's lift is `2 * Step`.
9. `ASteadyFrameAllocatesNothing`: a frame is `Begin`, 40 boxes of 3 parts (some overlapping), 30 fixed rectangles,
   `Solve(Dt, false)`, numbers from arrays built outside. After one frame `BoxCount` and `MemoryCount` are 40 and some
   `Lift` is above 0; `AllocationProbe` measures 0 bytes per frame.
10. `TheMemoryFollowsTheKeyNotTheGeometry`: B (key 7) in contact: `Step`; next B at `PadKeep + .05` with two parts instead
    of one (a pack that lost a member): still `Step`; next the same box under key 8: 0, `TryMemory(8)` true.
11. `KeysAndClassesKeepTheOrderTheDesignNeeds`: `SerialKey(n) == n`, `CampKey(c) == CampKeyBase + c`,
    `FighterKey(id) == FighterKeyBase + id` (id 0 and `uint.MaxValue` too), `CampKey(1) > SerialKey(int.MaxValue)`,
    `FighterKey(0) > CampKey(int.MaxValue)`; the classes rise in the order of rule 2; a serial-keyed A beats a camp-keyed
    B of one class.

`OverheadClusterTests` (stacks from `OverheadStack.Build`; a layout with one box at origin 0; `k = 1.2 * .026`):
(a) `AnEliteGivesItsNameAndItsStar`: elite (`OverheadActorKind.Elite`, name "Gaoler"), `eliteStar` true, `worldPerLocal`
1.2, name 1.0 x .34: `AddParts` returns 2: the name `(-.5, -.17, .5, .17)` and the star `(+-half, y - half, +-half, y + half)`,
`half = EliteStarSize * EliteStarPlate * 1.2 / 2` (.252), `y = k * 5.5 + 1.2 (EliteStarRise + EliteStarSize / 2)` (.3756);
with `eliteStar` false only the name; then an elite stack with `Name` null gives 0 and `PartCount` 0 on a second box.
(b) `AFighterGivesEightPartsInStackOrder`: fighter (`OverheadActorKind.Player`, name "Ann", `NameWidthPx` 40, Bounty 40,
off-hand, `Stat1` 1, `Stat2` 2, `MarkTier` 2, `WishState` 1), `nameWidth` 1.248, `nameHeight` .34: 8 parts in stack order,
none for the bars (Within 1e-3): Name `(-.624, -.17, .624, .17)`, Wish `(-1.1856, -.2028, -.7176, .2652)`, pips
`(-.4992, .1092, .4992, .2028)`, crown a square of side .7001 centred at x 0, y .5262, bounty number
`(-.312, .7873, .312, 1.1305)` (computed from the Build numbers by the oracle author), off-hand `(-.8724, .8821, -.2508,
1.5037)`, stats `(-.3108, .8821, .3108, 1.5037)` and `(.2508, .8821, .8724, 1.5037)`; with no bounty 6 parts and the
first stat mark `(-.3108, .36, .3108, .9816)`.
(c) `TheLargestLiftCoversTheTallestCluster`: the fullest fighter (Bounty 150, off-hand, both stats, tier 4, Wish) has 8
parts, and their extent (lowest `y0` to highest `y1`) plus `Gap` is at most `MaxLift`.

`NameLayoutKeyEditModeTests` (Unity EditMode, namespace `Roguehalla.Client.Tests`; SetUp and TearDown as
`MobContinuityEditModeTests` (`Assets/Tests/Editor/MobContinuityEditModeTests.cs:14-27`), `ContinuityEnabled` saved and
restored in `finally`; a frame is `Begin`, `Present` of each id as a `RegionalMob` with key "grunt", `End`, `StepFades`; the
key is read by reflection: private `_active` as `IDictionary`, `[id]`'s public field `LayoutKey`):
`TheLayoutSerialIsGivenOncePerArtAndKeptByARenewedHandle`: ids 1 at (640, 640) and 3 at (840, 640) in one frame: both keys
above 0 and different; a second frame (clock +1 s) changes neither. `Deliver(2, 3)`, a frame with 2 at (648, 640) and 3:
`MobArt.Continued` is 1 (the premise) and id 2's key is id 1's old key. Keep the art of 2, `Deliver(3)`, step the clock
.13 and .2 s with `StepFades` (`MobArt.Destroyed` is 1), a frame with 3 and id 7 at (1040, 640): it takes that pooled art
(`ReferenceEquals`) with a key above every earlier one.

## Existing tests that pin this code (none changes its expectation; run them on the stub and after the move)

Every class whose file names `SpriteActorPresenter` (39 files, `grep -l SpriteActorPresenter` over `Assets/Tests/Editor`):
ActorOutline, ArenaPresentation, ArenaPropCue, BogTreeCrownFade, ConfirmedWishPresenter, HitboxPresentation,
HudVisualFix2, MapOpenWorld, MobContinuity, NameLegible, NameLegibleTable, NamePack, NamePackFix4, NamePackGroup2,
NamePackHover, OverheadIndicators, SpriteActorBarrier, SpriteActorSignature, UiLab, VisionPresentation, WishFxPresenter,
WishPresentationTrace (each `...EditModeTests`), StormInk{ArtistSource, Bowstring, ChampionEquipment, FireAxeAuthoring,
FireAxe, FireAxePaintedContact, FireBow, FireRoam, FireSword, HauntedFists, HolyShield, Manacles, ObservedCastRenewal}
`EditModeTests`, and `StormInkChurchCastTests`, `StormInkChurchPreviewTests`, `StormInkSwordArtistSourceTests`,
`StormInkWeaponMotionTests`; plus `StormInkSignatureCueEditModeTests` (reaches `ClearOfBars` through
`PlaceSignatureCues`, `SpriteActorPresenter.Overheads.cs:61,77`) and `CampLabelStableEditModeTests` (its own delegate).
What they assert about the name pass (verified by reading):
- `NamePackEditModeTests` (`:12`): a pack of same-named mobs shows one name through `End` -> `MergeMobNames`.
- `NamePackGroup2EditModeTests` (`:12`): a pack shows exactly one name, also engaged; it sits over the pack unless a
  member is hovered or locked; fighters' names stacked (`FighterNameRects`); camp labels read it.
- `NamePackFix4EditModeTests`: every mob name reads on every floor; a camp's mobs of one name are one pack; the Tab map
  hides names (`WorldLabelVisibility.Hidden`, `MergeMobNames` `:191`).
- `NamePackHoverEditModeTests`: the hovered mob always shows its own name.
- `NameLegibleEditModeTests` (`:361` wires `camp.NameRectSource = into => _presenter.FighterNameRects(into)`): names read
  on the ground (`FinishNames`), sort above every mob bar, an elite's name is clear of its badge and of the bars of the pack
  beside it (`ClearOfBars`).
- `NameLegibleTableEditModeTests`: `FloorLuminance` table.
- `MobContinuityEditModeTests`: creature art follows a renewed handle (the premise of the new key test).
- `OverheadIndicatorsEditModeTests`: the overhead cluster rules.
- The other classes build a presenter and call `Begin`/`Present`/`End`; they pin that `End` still evicts and clears launches.
The 800-line limit per tracked C# file is enforced by the gate (`Tools/hud-seams.test.cjs:92` for the MatchHud partials).

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "OverheadBoxLayoutTests|OverheadClusterTests"`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> NameLayoutKeyEditModeTests,MobContinuityEditModeTests,NamePackGroup2EditModeTests,NameLegibleEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-WORLD-NAMES-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. The move is mechanical but large; the rule is arithmetic with float edges (the
`r.y0 < q.y1 + Gap` test after a raise sits within an ulp of equality: the pass limit and the 1e-4 tolerance cover it).
Order of work, each its own commit: (1) the move; (2) the library, its sync copy; (3) the serial; (4) the Unity class and
the gate. Stop and report after 1 and after 2 if anything is red.

## 11. Known traps

1. Move by cut and paste, then `git diff -w --color-moved=zebra`. `Art` is a private nested class: only reflection reaches
   `LayoutKey`. No `InternalsVisibleTo`, no `Assert.Multiple`; members a test reads are `public`; time enters a test only as
   `dt`; never `GC.GetAllocatedBytesForCurrentThread` in Unity. The branch stays linear.
2. Do not wire the star constants or call the library from the presenter.
3. A box measured at its base: never from where it was drawn last frame. A lift of 0 is recorded too (it is what lets the
   keep band start from "raised" later). A bad dt records nothing.
4. `Target`, not `Lift`, is what later boxes avoid.
5. Keep-band rule: `wk` only ever replaces `w` when larger, and is capped at `Target`: the band decides whether a lift is
   kept, never how much.

## Not established

How it looks: nothing draws the rule yet. `Gap`, `PadEnter`, `PadKeep`, `MaxLift` (and the name line height) are first
guesses from geometry and the camp constants. The lane judges the look in its next Web round, after P-UI-WORLD-NAMES-2 is
merged; the constants may be tuned then (every test names them, none repeats their numbers). Not proved: that the
cluster geometry equals what `OverheadIndicators` draws, or that the star constants stay equal to the drawn ones until
the second packet makes the drawing read them; the library's allocation under Unity (dotnet only); that the move is
neutral beyond what the listed classes see.

## Notes from the oracle author

(filled in with the oracle commit)

## Notes from previous attempts
