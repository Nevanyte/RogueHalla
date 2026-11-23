# P-UI-WORLD-LABELS-1: a quest's name stands once, and no prop label lies on the player's own name and bar

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane: "Quest UI could be perfected",
  "Please do a UI optimization sweep"). Seen in the lane's Web rounds 2 and 3a (2026-11-23): giver names and quest
  markers piled up on the player's own name, bar and buff icons (picture `webround/round-3a/56-prompt-*.png` cuts
  "Scorch quest giver").
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head `9c9e4fa9c`; `WorldMarks.cs`, `WorldIndicators.cs`,
  `OverheadIndicators.cs`, `SpriteActorPresenter*.cs` and `View/ArenaView.cs` unchanged since `df64266a4`). Worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-b`.
- **D-rules touched:** D1: presentation only. Legacy has no objective-point marker with a quest name
  (`packages/client/src/render/QuestView.ts:154-165` draws a crate or diamond for a quest item, its text only while
  highlighted or tracked, `:98`) and no rule that keeps a label clear; it shows a giver name (`QuestView.ts:134`), a
  trader name (`TraderView.ts:39`) and a body label (`ContainerView.ts:199-201`, `syncLabel`: `kind === "body" ||
  highlighted`) always. R1 changes nothing legacy does. R2 hides labels legacy always shows: propose ONE
  `DEVIATIONS.md` row in the hand-off (owner signs) naming the accepted loss in the words of "What stays" and
  "Accepted" (Outcome 3). D2 n/a. D5/D6 none. D9 none: no rule, key or wire.
- **Order with P-UI-WORLD-NAMES-3:** it builds AFTER this packet (`SpriteActorPresenter.Names.cs`, `.ActorArt.cs`, the
  layout library). This packet adds one new partial of the presenter, touches no member that packet edits (`End`,
  `MergeMobNames`, `FinishNames`, `ClearNameLayout`) and adds no `Art` field.

## 1. ID and outcome

Text labels in the world fade out while they lie on the player's own name and bar, and a quest's name stands on one
point only.

**What is wrong today.** `WorldMarks` and `WorldIndicators` (`Presentation/`) place each text label from its own
object by a constant: giver name 2.1 units above the giver (glyph y 2.55, label offset -.2 - .25 from the glyph,
`WorldMarks.cs:49,51,178`), objective name .53 above its point (`ObjectivePoint`, `WorldMarks.cs:85-86`), trader name
.17 above the figure top (`:73-74`), prop labels at lifts .85 (body, ground, corpse), 1.15 (chest), 1.2 (quest item),
1.4 (cannon) (`WorldIndicators.cs:73`). Both run from `ArenaView.PresentCollections` (`View/ArenaView.cs:609-610`),
before `Arena.Present`, and read no actor. Labels are in the Default sorting layer (`WorldMarks.Order` 29000 `:22`,
`LabelOrder` 29100 `WorldIndicators.cs:17`); the actors' overhead cluster (name, bar, badges, marks) is in "Strokes",
which draws after it (`MoveOverheadToStrokes`, `SpriteActorPresenter.Overheads.cs:16`): his own name, bar and badge box
cover a label the player stands in front of. `ObjectivePoint` writes `row.Name` on every delivered objective point of
an accepted or public quest (`WorldMarks.cs:59,62,86`): "Ember Walk" stands three times round the player. Third: both
`Update`s `foreach` over `IReadOnlyList`, `WorldMarks` calls `QuestPresentation.FindRow` (a `foreach`,
`UI/MatchHud.QuestRewards.cs:555-559`) per objective, and `PresentCollections` passes the method group `MarkVisible` to
`WorldIndicators.Update` (`ArenaView.cs:610`, a new delegate each frame) where the cached `_markVisible` (set at
`:608`, two lines above) stands: a steady frame allocates today.

**Outcome (numbers in the sections below are the contract).**

1. **One name per quest (R1), and it is sticky.** The DRAWN objective points of a quest (the row passes the existing
   filter and `canSee` passes) share one name (`row.Name` through `SetLabel`, unchanged). The point that carried it in
   the last frame keeps it while it is drawn; only when it is not drawn does the first drawn point in delivered order
   of `quests.Objectives` take it (a point flickering at the edge of sight must not make the name hop). The same point
   means the same quest handle and delivered x, y within .01. Every other drawn point keeps its Mark, glyph and glyph
   plate and gets no text (`SetLabel` with null: its existing empty path). Quests are told apart by `QuestHandle.Value`;
   public carrier marks ("dashring") follow the rule. A carrier that R2 hides does NOT pass the name on. Reward drops
   keep their own text and claim nothing. `Used` still counts marks.
2. **The local cluster (R2).** `SpriteActorPresenter.TryLocalClusterRect(out Rect)` returns the world rectangle that
   bounds the LOCAL fighter's OVERHEAD cluster as last presented: the union of the bar track's bounds (only while
   `HpBack` is enabled) and the rectangles of the name (only while `NameActive`), the bounty number, the crown, the Wish
   icon, the off-hand badge and the overhead marks. The body-anchored STATUS GLYPHS are NOT part of it: a cast, a slow
   or a burn must not grow the rectangle downward (a Casting ring alone is a .59 square about 1.3 above the feet).
   False when no art is local (`IndFacts.Local`, not `Mob`), it is dead or inactive in the hierarchy,
   `WorldLabelVisibility.Hidden` is true, or the union is empty. Other actors' clusters are never in it. What grows or
   shrinks the rectangle in play (the bounty crown and number, the off-hand badge, the Wish icon, the pips and stat
   marks) is accepted: a label near it follows the two pads of Outcome 4 (tests 21 and 22).
3. **Which labels, and what stays.** Hidden-able: text and text plate of a `WorldMarks` Mark (giver name, "Turn in
   here", objective or drop name, trader name) and of a `WorldIndicators` prop label (body, chest, cannon, weapon,
   corpse, "Quest item"); none is exempt (a giver's name, "Turn in here" and a trader's name fade like any other text
   while they lie on the player's own cluster). NEVER hidden: every glyph and glyph plate (giver "!" and "?", diamonds,
   crates, dashrings), the quest item marker and plate, the trader figure, the chest ring and its seconds text. What
   stays when a name fades: the glyph and the figure; the offer card names a giver before the accept; the prompt lane
   names the action when one is in reach and counts them when there are more ("Nearby quest actions", no giver name);
   the journal (Tab map text) lists traders ("<name> · trader"); the pickup pill reads "F  take <word>" (two spaces;
   `UI/MatchHud.cs:439`; <word> is the word of the prop kind) and so keeps the cannon word, while the reload seconds
   are only on the world label. A glyph lying on the player's own name or bar (a diamond between them) is never hidden:
   accepted as the end of this packet. **Accepted:** ONE rectangle stays, so a label that lies beside the bar inside
   the cluster's bounding box is hidden although it touches no drawn part, and a focused prop's label is born hidden
   when the player stands just below the prop (a chest once it is .32 north of the player, a cannon once it is .07
   north; the pickup pill still names the action).
4. **When.** A label's rectangle is its plate: centre at the plate position, size the scale just written (`SetLabel`:
   `preferredWidth + .2` by `size * .12 + .02`, `WorldMarks.cs:183`; `Show`: `preferredWidth + .16` by `size * .1 +
   .08`, `WorldIndicators.cs:251`). A visible label starts to hide when its rectangle overlaps the cluster grown by
   `HidePad` .10 on every side; a hidden one returns only when it no longer overlaps the cluster grown by `KeepPad`
   .40. Walking is 5.3 units a second: at 60 fps a frame is under .1 unit and the band is .30. No cluster (no source
   set, or it answered false twice running, as with no local actor or a dead one): a hidden label returns. The FIRST
   false frame keeps the last rectangle: the frame after the Tab map closes has no cluster yet (names were hidden), and
   a reduced-motion player must not see every label flash. So death and no local actor release the labels on the
   second false frame, one frame later than a source with no hold would: accepted. Labels update before
   `Arena.Present`, so the rectangle is the previous frame's cluster (`ArenaView.Present` holds the only
   `_actors.Begin()`): a label may meet the new cluster for a frame before its ramp starts, and a fast walker can pass
   through a fading label.
5. **How.** Alpha moves toward 0 or 1 in `FadeSeconds` .15 s each way (`CampRespawnLabels`' own `FadeSeconds`) through
   `UiEase.FadeStep` (`RoguehallaNet/Net/Ui/UiEase.cs:30`); under `UiMotion.Reduced` it is exactly 0 or 1 on the frame
   the decision changes. Final alpha = the label's own alpha times this one, on text colour and plate colour (.92 for a
   prop plate). A faded label stays ACTIVE at alpha 0: `LabelsShown`, `Used` and active counts do not change with the
   fade.
6. **State belongs to the object, not the pooled slot.** Clearance state is kept BY OBJECT: a small fixed array keyed
   by the object's identity (a book kind and a place, equal within .01), searched linearly, no allocation. Only objects
   that do not move are keyed by position: a giver, an objective point, a drop, a prop. A trader walks (`TRADER_SPEED`
   42 units a second, `crates/rh-sim/src/constants.rs:413`: about 2 units a tick, so a delivered x, y differs at every
   delivery) and is keyed by its trade kind, not by its place. A pooled slot takes the state found for its object, so a
   mark that appears or disappears earlier in the list resets nothing. An object with no entry is born fresh (no ramp:
   hidden at alpha 0 when its label is born under the cluster, else 1). An identity is forgotten only after TWO
   consecutive frames in which it was not asked for: a label fogged or unfocused for one frame keeps its alpha; a longer
   absence is born fresh (accepted: it may pop). Never identify by a drawn position: a trader's label height follows its
   animation. A new `ArenaView` is made for every match (`Net/MatchClient.cs:101`): that is the reset of the book and
   the hold; `Begin()` forgets what is no longer asked for.
7. **Skipped, not guessed.** While `WorldLabelVisibility.Hidden` is true or the host's root is not `activeInHierarchy`
   (the Tab map deactivates the roots, yet `Update` still runs under them) no state steps and `dt` is dropped; the kept
   alpha is still written (`Keep` counts as asked), and the cluster source is not read.
8. **R3.** No label's string, key, offset, size or sorting order changes (R1 only leaves the repeated name off the
   other points); colours change only in alpha.
9. **No allocation in a steady frame on the label and mark paths** (bodies, chests, weapons, corpses, quest items,
   givers, objectives, traders): both `Update`s, the book, the hold and `TryLocalClusterRect`. `foreach` becomes indexed
   `for` (same order); `WorldMarks` finds a quest row by its own indexed loop (`FindRow` stays for other callers). Out
   of scope, built every frame in `WorldIndicatorRules`: a near chest's seconds text (`SecondsText`) and a reloading
   cannon's label (`ContainerLabel`).

## 2. Dependencies

Base `lane/ui-rework/base-3`, which already holds `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`, packets
P-UI-ALLOC-METER-1/-2) and `WorldNamesRig`. P-UI-WORLD-NAMES-3 builds after this packet.

## 3. Relevant legacy paths

- `packages/client/src/render/QuestView.ts:134`: giver name text, always shown. `:154-165`: quest item crate or
  diamond; `:98`: item label visible only while highlighted or tracked.
- `packages/client/src/render/TraderView.ts:39`: trader name text, always shown.
- `packages/client/src/render/ContainerView.ts:199-201`: container label visible when `kind === "body" || highlighted`.
- Legacy has no rule that keeps a label clear of the player's name and bar and no objective-point name: R2 is new
  presentation (see D1 above).

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/LabelClearance.cs` and `QuestNameOnce.cs` (namespace `Roguehalla.Net.Ui`, a
  `<summary>` on the first type of each file), their synced copies under
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` and the copies' `.meta`
- `client-unity/UnityProject/Assets/Scripts/Presentation/SpriteActorPresenter.LocalCluster.cs` with `.meta`
- `client-unity/RoguehallaNet.Tests/LabelClearanceTests.cs`, `QuestNameOnceTests.cs`;
  `client-unity/UnityProject/Assets/Tests/Editor/WorldLabelsEditModeTests.cs` with `.meta` (oracle)

### Public API (library)

```csharp
public readonly struct LabelBox {                       // a rectangle in world units
    public readonly float X0, Y0, X1, Y1;  public LabelBox(float x0, float y0, float x1, float y1);
    public static LabelBox Centred(float cx, float cy, float width, float height);
    public bool Meets(in LabelBox other, float pad);    // overlaps other grown by pad per side, strictly
}
public struct LabelClearance {                          // one object's state
    public const float HidePad = .10f, KeepPad = .40f, FadeSeconds = .15f;
    public float Alpha => Has ? _alpha : 1f;            // computed: default(LabelClearance).Alpha is 1
    public bool Hidden { get; private set; }  public bool Has { get; private set; }   // private setters
    public void Forget();        // Has false, Alpha 1, Hidden false
    public float Next(in LabelBox label, bool haveCluster, in LabelBox cluster, float dt, bool reduced);  // Alpha
}
public sealed class LabelClearanceBook {                // states kept by object
    public const int Capacity = 64; public const float SameSpot = .01f;
    public int Count { get; }  public void Begin();  public void Clear();   // Clear: no product caller, tests only
    public float Next(int kind, float x, float y, in LabelBox label, bool haveCluster, in LabelBox cluster,
        float dt, bool reduced);                        // find or add (kind, x, y), step once a frame, return Alpha
    public float Keep(int kind, float x, float y);      // a skipped frame: the stored Alpha (1 when none), no step
}
public struct ClusterHold {                             // the first false frame keeps the last rectangle
    public bool Read(bool answered, in LabelBox rect, out LabelBox use);  public void Reset();   // true: use it
}
public sealed class QuestNameOnce {                     // one name per quest, sticky
    public int Count { get; }  public void Begin();     // quests offered this frame; carriers become the memory
    public void Offer(uint quest, float x, float y);    // a DRAWN point, in delivered order (first pass)
    public bool Carries(uint quest, float x, float y);  // second pass: true for the point that keeps or takes it
}
```

Rules.

- `LabelClearance.Next`: when `!Has`, start fresh: `Hidden = haveCluster && label.Meets(cluster, HidePad)`, Alpha 0 or
  1. Otherwise: not Hidden and `haveCluster` and `Meets(..., HidePad)` makes it Hidden; Hidden and not (`haveCluster`
  and `Meets(..., KeepPad)`) makes it not Hidden; then `Alpha = UiEase.FadeStep(Alpha, !Hidden, dt, FadeSeconds,
  FadeSeconds, reduced)`. `Next` replaces a NaN or negative `dt` with 0 BEFORE calling `FadeStep` (`Toward` turns a
  NaN into a NaN). Always set `Has`. A NaN coordinate meets nothing. `Meets`: `other.X0 - pad < X1 && X0 < other.X1 +
  pad` and the same in y, strict. Worked numbers (cluster (0,0)-(1,1), label 1 by .3, `dt` .05, one step .3333): gap
  .05 meets at HidePad; gap .25 meets at KeepPad only; gap .45 neither; gap exactly .10 does not meet at HidePad. Born
  at gap .45 (Alpha 1), four steps at gap -.05 give .667, .333, 0, 0 (Hidden from the first). That hidden label held
  at gap .25 three steps stays 0; at gap .45 it gives .333, .667, 1, 1. Born at gap .25 it is 1 and stays 1.
  `reduced`: 0 and 1 at once.
- `LabelClearanceBook`: `Begin` clears the asked marks; an entry that was asked (by `Next` or `Keep`) since the last
  `Begin` has its misses set to 0, any other entry counts one miss and is dropped at its second (two consecutive
  frames), and `Count` counts the entries held, those in their frame of grace included. `Next` finds the entry of
  (kind, x, y) (x and y within `SameSpot`; linear) or adds a fresh one; the first ask of a frame steps it, a second ask
  of one entry in a frame returns the stored Alpha unstepped (two objects of one kind at one spot share a state). `Keep`
  marks the entry and returns its Alpha (1, nothing stored, when there is none). A full book (`Capacity`) answers as a
  fresh `LabelClearance` would and stores nothing.
- `ClusterHold.Read(true, r, ..)` remembers `r`, misses 0, uses `r`; `Read(false, ..)` counts a miss and uses the
  remembered rectangle while one is remembered and misses is 1; from the second false in a row it answers false and
  drops the memory; `Reset` forgets it.
- `QuestNameOnce`, per quest over one frame's offers: last frame's carrier (same quest, x and y within .01), if among
  them, carries, else the first offer; a quest with no offer in a frame forgets its carrier; two offers of one quest
  within .01 of each other both carry (accepted).

### Public API (presentation)

`Presentation/SpriteActorPresenter.LocalCluster.cs` (new partial; the `<summary>` goes on the delegate, the first type
of the file, and on the class; a reused `List<Rect>`; an art counts when `Stamp == _stamp`, `IndFacts.Local`, not
`IndFacts.Mob`, not `Dead`, `Root.activeInHierarchy`; valid between `End` and the next `Begin`):

```csharp
public delegate bool LocalClusterSource(out Rect rect);               // top level, namespace Roguehalla.Client
public sealed partial class SpriteActorPresenter { public bool TryLocalClusterRect(out Rect rect); }
```

`OverheadIndicators.cs` gains one internal method `AddOverheadObstacles(List<Rect> into)`: the body of
`AddIndicatorObstacles` (`OverheadIndicators.cs:224-234`) without its `_glyphs` loop (name while `NameActive`, number
while active in the hierarchy, crown, Wish icon, badge backer, `_extras.AddMarkObstacles`). `AddIndicatorObstacles`
itself (called at `SpriteActorPresenter.Overheads.cs:77`) is not touched. The bar track rectangle is
`art.HpBack.bounds` through `CombatHudPlacement.FromBounds` (as `SpriteActorPresenter.cs:81`).

### Wiring

- `WorldMarks` and `WorldIndicators` each get `public LocalClusterSource LocalCluster { get; set; }` (null: nothing
  hides), a `LabelClearanceBook` and a `ClusterHold` (non-readonly class fields). Each `Update` runs `_book.Begin()`,
  then `run = !WorldLabelVisibility.Hidden && _root.gameObject.activeInHierarchy`; when `run` it reads the source once
  through the hold and `UiMotion.Reduced` once, else labels use `Keep`. Neither class has a `Clear()` that touches the
  book or the hold; `WorldIndicators.Clear()` (`:125`, called from `View/ArenaView.Fx.cs:97`) stays as it is.
- `WorldMarks`: `Update(c, canSee)` (`:33`) stays and calls new `Update(c, canSee, dt)` with `Time.unscaledDeltaTime`; a
  `QuestNameOnce` field, `Begin()` first. Objectives (`:53-60`) take TWO passes over one private filter helper (row
  found by an indexed loop, board row, Open, public or own accepted, `canSee`): the first `Offer`s every drawn point,
  the second is the existing loop and writes the name only where `Carries` says so. `ObjectivePoint` (`:80`) gains the
  quest handle's value and the point's sim x, y; `SetLabel` (`:173`) becomes an instance method taking a kind and the
  id and applying alpha. Book kinds and ids: giver 1 at `GiverX, GiverY`; objective or drop 2 at `X, Y`; trader `3 +
  (int)t.Kind` (3 Barter, 4 Stats) with x = y = 0. Empty or null text asks for no state.
- `WorldIndicators`: `Update`'s signature (`:46`) stays; `dt = clamp(now - lastNow, 0, 1)`, 0 on the first call. The
  chest seconds text keeps `Show(l, text, at, size, colour)` unchanged (`:157`), the clearance-free path. Prop labels
  (`:74`) go through a second method `ShowProp(l, text, at, size, colour, kind, x, y)` that calls `Show`, then applies
  the book's alpha to the text colour and to the plate colour (.92 times it); kind is 16 plus `(byte)p.Kind`, x, y the
  prop's sim position.
- `View/ArenaView.cs` (798 lines, stays 798): two lines replaced in place, none added. (1) The line that creates
  `_worldIndicators` (`:166`, it also creates `_vignette`): append `_worldMarks.LocalCluster =
  _worldIndicators.LocalCluster = _actors.TryLocalClusterRect;`. (2) In `PresentCollections` the
  `_worldIndicators.Update(...)` line (`:610`): its last argument `MarkVisible` becomes `_markVisible`, the cached
  delegate set two lines above (`:608`).

## 5. Exclusive write scope

- new `RoguehallaNet/Net/Ui/LabelClearance.cs`, `QuestNameOnce.cs` (stubbed by the oracle author; the implementer fills
  them); their synced copies under `UnityProject/Assets/RoguehallaNet/Net/Ui/` and the copies' `.meta`
- `UnityProject/Assets/Scripts/Presentation/WorldMarks.cs`, `WorldIndicators.cs`, `OverheadIndicators.cs` (the one
  method); `Presentation/SpriteActorPresenter.LocalCluster.cs` with `.meta`; `View/ArenaView.cs` (the two lines)
- No existing test file changes (read; none turns red: all build both classes with no source and count marks).
- Not in scope: `SpriteActorPresenter.cs`, `.Names.cs`, `.ActorArt.cs` (P-UI-WORLD-NAMES-3), `OverheadBoxLayout.cs`,
  `CampRespawnLabels.cs`, `QuestCarrierFlags.cs`, `WorldLabelVisibility.cs`, `UI/MatchHud.QuestRewards.cs`,
  `WorldIndicatorRules.cs`, `Tools/*.cjs`; joining the name layout, two givers of one name side by side, the cannon
  key hint, a glyph lying on the player's name or bar. No change to the wire, `crates/`, `packages/`, `parity/`.

## 6. Contracts and references

- R-AUTH / ADR 0008: presentation only, every fact is a delivered one; Unity holds no rule.
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (motion: unscaled time, instant under reduced motion, no
  allocation in a steady frame; readability gate: `<summary>` before the first type, 800 lines; do not add lines to
  `View/ArenaView.cs` or `SpriteActorPresenter.cs`).
- `docs/OWNER_DECISIONS.md`: searched for the subject (world labels, quest names): no decided row; this packet binds
  no key and changes no control.

## 7. Behaviour to preserve

- No label's string, key, offset, size or sorting order changes; colours change only in alpha (R3).
- `Used`, `LabelsShown`, `MarkersShown`, `RingsShown` and active counts do not change with a fade.
- `WorldIndicators.Clear()` and the `ArenaView.Fx.cs:97` call stay as they are; the chest ring, its seconds text, the
  quest item marker and plate, every glyph and plate, the trader figure are never faded.
- Existing tests that pin this code (read in full; none changes expectation, all stay green on the stub and after):
  - `WorldIndicatorsEditModeTests` (`Assets/Tests/Editor/WorldIndicatorsEditModeTests.cs`): bodies labelled always and
    other props only when focused (`LabelsShown` 2); a fogged row gets no label, marker or ring (all 0); a quest item
    gets a marker and a focused one a label (`MarkersShown` 2, `LabelsShown` 1); a reward chest draws its ring and the
    seconds only near (`SecondsShownForTest(0)` "12s" then null; `LabelsShown` 0); the death X lives two seconds. All
    through the 7-argument `Update`, no source set.
  - `OverheadIndicatorsEditModeTests.cs:109-112` `WorldMarksWithoutCollectionsShowNothing`: `new WorldMarks(..)`,
    `Update(null, null)`, `Used` 0.
  - `UiLabFixesEditModeTests` `A_merchant_marker_stands_on_a_figure_a_quest_mark_does_not_add_one` and
    `A_merchant_is_drawn_from_a_catalog_look_with_goods_and_the_shadow_pair_of_any_figure` (`:107-135`): a merchant
    delivered through reflection `CollectionsView.Apply`, `marks.Update(view, null)`: figure enabled, goods, shadows,
    scale 1.2, glyph above the head, hidden when `Update(null, null)`.
  - `PlayerFlowsEditModeTests.cs:184-189` (`MapsAndWorldUseDeliveredPersonalAndPublicPointsAndRemoveCompletedGivers`):
    `WorldMarks.Update(_session.Collections, canSee)` counts `Used` 2 (fogged public point) then 3: marks, not names.
  - `TabMapSheetEditModeTests.cs:449` `WorldMarksAreHiddenWhileTheMapIsOpen` and `MapOpenWorldEditModeTests.cs:287`
    `TheOutlinesRootIsHiddenWithTheWorldMarks`: the arena's "World markers" / "World indicators" roots go inactive
    with `SetWorldMarksVisible(false)` (the `activeInHierarchy` skip must not disturb this).
  - `UnityProject/Tools/ui-gallery.cjs` (browser capture, header names the presenters): not a test; unaffected.
  - `AddIndicatorObstacles` callers (the signature cue placement tests) are unaffected: the method is not touched.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-WORLD-LABELS-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- The 20 `LabelClearanceTests` / `QuestNameOnceTests` and the 24 `WorldLabelsEditModeTests` below are green; no oracle
  file changed; `bash client-unity/sync-net.sh --check` clean; `View/ArenaView.cs` still 798 lines.
- Not proven by tests (implementer and reviewer check by reading): the two `ArenaView` lines; a look check at alpha
  .33 shows whether the outlined material fades its outline with the vertex alpha.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/LabelClearanceTests.cs`
- `client-unity/RoguehallaNet.Tests/QuestNameOnceTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/WorldLabelsEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/WorldLabelsEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-WORLD-LABELS-1.md`

(The stub commit holds `LabelClearance.cs`, `QuestNameOnce.cs`, their synced copies and `.meta`,
`SpriteActorPresenter.LocalCluster.cs` with `.meta`, and the stub members of `WorldMarks`, `WorldIndicators` and
`OverheadIndicators`; the implementer replaces them.)

## 9. Validation commands

- `.../bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "FullyQualifiedName~LabelClearance|FullyQualifiedName~QuestNameOnce"`
- `.../bin/unity-class.sh <worktree> WorldLabelsEditModeTests,WorldIndicatorsEditModeTests,UiLabFixesEditModeTests,OverheadIndicatorsEditModeTests`
  (and `PlayerFlowsEditModeTests`, `TabMapSheetEditModeTests`, `MapOpenWorldEditModeTests` once at the end)
- `.../bin/in.sh <worktree> bash client-unity/sync-net.sh` then `--check`
- `.../bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-WORLD-LABELS-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet, reviewer. Arithmetic is in the library with worked numbers; the risky part is the presentation
wiring (state by object, the skip rule, the two-pass quest name, no allocation). Proposed DEVIATIONS row for the
hand-off (owner signs): "World labels (a giver's, a trader's and a prop's name, 'Turn in here') fade out while they lie
on the local fighter's own name and bar; legacy shows them always. What stays: the glyph and the figure, the offer card,
the prompt lane, the journal, the pickup pill. Accepted: one rectangle, so a label beside the bar inside the cluster's
box fades too, and a focused prop's label is born hidden when the player stands just below it."

## 11. Known traps

- `LabelClearance` is a struct: a class field or array element, never a `List<struct>` or a readonly field.
- Read `preferredWidth` after `SetActive(true)`, once, for plate and rectangle alike (a label never active in the
  hierarchy measures a tenth; this one is always active when read). `Offer` only after `canSee` passes, never for
  drops; a null `row.Name` still carries.
- `SetLabel` rewrites `color` from alpha 1 each frame: apply the clearance inside it; never deactivate a label.
- The cluster is read twice a frame (once per class): accepted; no frame-count cache (static in EditMode).
- Tests are a separate assembly with no `InternalsVisibleTo`: `LocalCluster`, `TryLocalClusterRect`, the three-argument
  `Update`, `LocalClusterSource` and the library types are `public`.
- Do not edit `CardDisplayCatalog.cs` or `BakedContent.cs`. Restore the two StormInk prop assets after a Unity run.

## Notes from the oracle author
<!-- filled in below the oracle commit -->
