# P-UI-ABILITY-ROW-1: one action row: weapon 1, swap, weapon 2, R, Shift, Q

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("ability and spell hub needs optimizing")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (the integration branch `feature/unity-rework` plus P-UI-HUD-SEAMS-1,
  P-UI-HUD-REGIONS-1, P-UI-MOTION-1, P-UI-CAMP-LABEL-STABLE-1, P-UI-TABMAP-FLOW-1)
- **D-rules touched:** D1 presentation only: digit keys 1 and 2 selecting a weapon slot is legacy behaviour
  (`docs/rework/LEGACY_LAYOUT.md` key table, "Run": `1` `2` slot; `packages/client/src/ui/helpOverlay.ts:42`), so this removes a
  difference. The quest spells moving from a text cluster above the bar into the row has no legacy counterpart (legacy has no
  Shift/Q quest spells in its bar): propose a DEVIATIONS row in the hand-off. · D2 n/a · D5/D6 none · D9 untouched: nothing here
  predicts an outcome, every state drawn is a delivered fact.

## 1. ID and outcome

P-UI-ABILITY-ROW-1. The in-match action bar becomes one row of six, left to right: weapon 1 (104 x 76), swap chip, weapon 2
(104 x 76), R (the Wish tile), Shift, Q (64 x 64 each), 6 apart: 458 wide, centred, the row's bottom 48 above the stage bottom.
Shift and Q are new `AbilityTile`s (key cap, name, countdown, cooldown wedge, charge badge) that replace the two text tiles that
floated above the bar. Nothing floats above the row any more; the pickup pill and the quest-action launcher share one prompt lane.
The keys 1 and 2 pick a weapon slot. A swap pulses the two slots and never the whole bar. On a narrow stage the row shrinks so it
clears the champion panel and the minimap; the minimap keeps its size. Every rect comes from `Roguehalla.Net.Ui.HudRegions`.

What is wrong today (on the base):
- The bar (`CreateWeaponBar`, `UI/MatchHud.ActionBar.cs:55`) is 318 wide: slot 1, swap chip, slot 2, the Wish tile. The two quest
  spells (Shift, Q) are two 141 x 39 text tiles in a separate box `QuestRewards` floating above the bar at y 157..196, with a
  launcher button above them (`UI/MatchHud.QuestRewards.cs:31-45`, bottom `BarBottom + SlotHeight + ConsumablesHeight + 2 * BarGap + 27`
  = 157). They show text only ("SHIFT · Name / 2 charges · 5s"), no cooldown wedge.
- The slots carry key caps "1" and "2" (`ActionBar.cs:71`) and nothing handles those keys in a run: `Net/MatchClient.cs:466-489` handles
  R, Space (:474-477), Shift, Q, E, F. In planning, 1 and 2 pick a spawn (`UI/TacticalMapController.cs:419-422`).
- The pickup pill (`CreatePickupHint`, `ActionBar.cs:161`, a child of the bar, 230 x 22 at 116 above the bar bottom) sits at stage
  y 130..152; the space above it is where P-UI-TOAST-LANES-1 (same wave) puts the feedback toasts, from y 162.
- A Space swap pulses the whole bar (`UI/MatchHud.Pulses.cs:31-35`: `Pulse(_weaponBar)` when the command carries no slot).

## 2. Dependencies

The base above. P-UI-TOAST-LANES-1 (same wave) amends `Tools/hud-seams.test.cjs` and owns the toast lanes: do not touch either.

## 3. Relevant legacy paths

- `packages/client/src/ui/helpOverlay.ts:42`: the help text lists `1` `2` as the weapon slot keys (legacy). `docs/rework/LEGACY_LAYOUT.md`
  key table, "Run": `1` `2` slot.
- Legacy has no Shift/Q quest spells in its bar: no counterpart (see DEVIATIONS note above).
- Evidence read by the lane: `.agent-runs/lane-ui-rework/understand/ability-spell-bar.txt` (sections 1 and 2, defects 1, 10, 11, 12).

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs` (pure; namespace `Roguehalla.Net.Ui`) and its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/AbilityFace.cs` (+ `.meta`, written by the first Unity run).
- `client-unity/UnityProject/Assets/Scripts/UI/AbilityTile.cs` (+ `.meta`).
- Tests (oracle): see "Oracle files".

### Pure API (`AbilityFace.cs`)

```csharp
/// <summary>What an ability tile shows at one moment.</summary>
public enum AbilityPhase { Empty, Ready, Cooling, Casting }

/// <summary>The drawn state of one ability tile, computed from delivered facts.</summary>
public readonly struct AbilityFace
{
    public readonly AbilityPhase Phase;
    public readonly float Sweep;     // share of the tile the cooldown wedge covers, 0..1; 0 unless Cooling
    public readonly int Seconds;     // whole seconds left; 0 shows no number
    public readonly int Badge;       // charge count to show; -1 shows no badge
    public readonly float Cast;      // cast progress 0..1; -1 shows no cast bar
    public AbilityFace(AbilityPhase phase, float sweep, int seconds, int badge, float cast);
    public static AbilityFace Empty { get; }      // Phase Empty, 0, 0, -1, -1
}

public static class AbilityFaces
{
    /// <summary>A quest spell slot. totalMs is the length of the running cooldown (from CooldownSpan); 0 or less
    /// draws a full wedge while cooling.</summary>
    public static AbilityFace Spell(bool has, int charges, double readyAtMs, double nowMs, double totalMs);
}

/// <summary>Remembers how long a cooldown was when it was first seen, for abilities whose recharge time is not
/// delivered. One per tile.</summary>
public struct CooldownSpan
{
    public double Total(double readyAtMs, double nowMs);
}
```
Rules: `Spell(false, ...)` is `Empty`. With a spell: left = max(0, readyAt - now). left > 0 is `Cooling` with
`Seconds = ceil(left / 1000)` and `Sweep = totalMs > 0 ? clamp01(left / totalMs) : 1`; otherwise `Ready` with `Sweep 0`,
`Seconds 0`. `Badge = charges` in both. `Cast` is -1 (quest spells deliver no cast window). NaN times count as 0.
`CooldownSpan.Total`: the largest time left seen since `readyAt` last changed (a `readyAt` different from the last one starts a
new span at the time now left; within a span the answer never shrinks); when nothing is left it returns 0 and forgets the span.
Allocation-free.

### Additions to `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs` (and its synced copy); nothing else in that file changes

```csharp
/// <summary>The scale of the action row on a narrow stage: 1 while the centred row clears, by 8 on each side, a panel that ends
/// leftObstacleRight from the left edge (the champion panel) and one that starts rightObstacleLeft from the left edge (the
/// minimap); smaller below that; never under .6.</summary>
public static float AbilityRowScale(float stageWidth, float leftObstacleRight, float rightObstacleLeft);

public const float PromptPairWidth = 220f;
/// <summary>The prompt lane when it holds two pills: index 0 the left one, 1 the right one, 4 from the centre line.</summary>
public static HudRegion PromptPair(int index);
```
`AbilityRowScale` = clamp(2 * min(stageWidth / 2 - leftObstacleRight - 8, rightObstacleLeft - 8 - stageWidth / 2) / AbilityWidth, .6, 1);
a NaN or infinite input, or a stage width of 0 or less, gives 1. Examples: (1422, 262, 1140) = 1; (1000, 262, 684) = 352 / 458 = .769
(the minimap side binds); (875, 262, 700) = 335 / 458 = .731 (the champion side binds); (600, 262, 478) = .6.
`PromptPair(i)` = `HudRegionAnchor.BottomCenter`, x = (i == 0 ? -1 : 1) * (PromptPairWidth / 2 + 4), y = `PromptBottom`, size
`PromptPairWidth` x `PromptHeight`. (`HudRegionsTests.TheCentreBottomStackIsInsideDisjoint...` builds its region list inline, so
`PromptPair` is tested in `AbilityFaceTests`.)

### Unity API (`UI/AbilityTile.cs`)

```csharp
/// <summary>One 64-unit ability tile of the action row: key cap, name, countdown, cooldown wedge, charge badge.</summary>
public sealed class AbilityTile
{
    public AbilityTile(Transform parent, string name, HudRegion region, float raise, string keyCap, Transform hintOverlay);
    public RectTransform Root { get; }
    public UnityEngine.UI.Button Button { get; }     // carries the hover / keyboard hint; it sends nothing
    public HudIconHint Hint { get; }
    public TMP_Text KeyLabel { get; }
    public TMP_Text NameLabel { get; }
    public TMP_Text SecondsLabel { get; }
    public TMP_Text BadgeLabel { get; }
    public UnityEngine.UI.Image Sweep { get; }
    /// <summary>Draws a face. Writes only what differs from what the tile shows: a call with the same face, name and
    /// hint changes nothing and allocates nothing.</summary>
    public void Show(AbilityFace face, string name, string hint);
    /// <summary>Remembers the running cooldown (server ms it ends at, and its length; 0 and 0 when none) so that a later
    /// packet can move the wedge every frame. In this packet it only stores the two values.</summary>
    public void Cooldown(double readyAtMs, double totalMs);
    public double ReadyAtMs { get; }
    public double TotalMs { get; }
}
```
- `raise` is added to the region's bottom inset (the row's offset inside the bar).
- Children, by name: `Key/Label` (top-left, `theme.SizeTiny`, `theme.TextSecondary`), `Name/Label` (centred, two lines at most,
  wrapping, `theme.SizeTiny`, box 60 x 26 centred in the tile), `Seconds/Label` (centred over the tile, 15 units, bold),
  `Charges/Label` (bottom-right 20 x 14, `theme.SizeSmall`, `theme.Accent`, bold), `CooldownSweep` (radial wedge as the Wish
  tile's, `ActionBar.cs:121-124`: `UiTheme.With(theme.OutlineDark, .62f)`, Radial360, origin 2, counter-clockwise, inset 1). All
  labels through `HudUi.Label`; none is a raycast target. `Key`, `Name`, `Seconds` and `Charges` are the BOX objects (explicit
  size: key 30 x 12 at the top left, name 60 x 26 centred, seconds 40 x 20 centred, charges 20 x 14 at the bottom right); the text
  is the child `Label`.
- `Frame`: an `Image` child of the tile root (a thin outline, not a raycast target) whose `color` is exactly the table's frame
  colour for the phase; the oracle reads the frame colour from `Frame`'s Image and nowhere else.
- Hiding the name (Cooling, Empty) means: the `Name` box is deactivated (`SetActive(false)`) or its label text is empty. The oracle
  reads exactly that (`Name` active and a non-empty `Name/Label` text); disabling the TMP component or zeroing its alpha does not count.
- The tile's surface is one `Image` whose `color` is exactly the table's surface colour: the `Button` on the tile has
  `transition = Selectable.Transition.None`, so no colour block tints it. `UiTheme.With(colour, alpha)` (`UI/UiTheme.cs:129`) gives
  the alpha variants (the HUD's private `Tone` helper is not reachable from this class).
- Look per phase (theme colours only):

  | Phase | Surface | Frame | Name | Seconds | Badge | Wedge | Button |
  |---|---|---|---|---|---|---|---|
  | Empty | `UiTheme.With(theme.Surface, .35f)` | `UiTheme.With(theme.Border, .5f)` | "" | "" | "" | off | not interactable |
  | Ready | `theme.SurfaceRaised` | `theme.BorderStrong` | name, `theme.TextPrimary` | "" | count | off | interactable |
  | Cooling | `theme.SurfaceRaised` | `theme.Border` | hidden | whole seconds | count | on, `fillAmount = Sweep` | interactable |

  Casting is drawn as Ready in this packet (P-UI-ABILITY-MOTION-1 gives it a cast bar when the Wish moves onto the tile). The key
  cap is always shown; in Empty it is at alpha .6.
- Hint texts: with a spell, the existing `QuestSpellText(slot, key)` string (`MatchHud.QuestRewards.cs:236`) with its line break kept;
  empty with an available quest view, `"Shift · Quest spell\nEmpty: a quest reward fills this slot"` (and `"Q · ..."`); empty with no
  quest view yet, the first line alone. Key caps: `"Shift"` and `"Q"`.
- The name is `QuestPresentation.RewardName(reward)` (`MatchHud.QuestRewards.cs:294`).

## 5. Exclusive write scope

- new `client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs`, its synced copy and `.meta`
- `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs` and its synced copy (`AbilityRowScale`, `PromptPairWidth`, `PromptPair` only)
- new `client-unity/UnityProject/Assets/Scripts/UI/AbilityTile.cs` with `.meta`
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.ActionBar.cs`, `MatchHud.QuestRewards.cs`, `MatchHud.Pulses.cs` (the swap
  cue only), `MatchHud.Legacy.cs` (the bar scale line in `ApplyLegacyViewport`, :345, and any bar constant still declared there)
- `client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs` (the two digit keys only)
- the oracle files listed below are read-only for the implementer
- Not in scope: `HudLayout.cs` and the capture tools under `UnityProject/Tools/` (the minimap formula does not change),
  `Tools/hud-seams.test.cjs`, `UI/HelpGuideView.cs`, `MatchHud.PlayerFlows.cs`, `MatchHud.cs`, `MatchHud.TopCentre.cs`,
  `MatchHud.Notices.cs`, `OrderedNoticeFeed.cs`, `MatchHud.Visibility.cs`, the Tab map files, `HudTextScaleEditModeTests.cs` (its
  inventory of 63 stays; three packets of this wave add labels), the Wish tile's contents and texts (P-UI-ABILITY-MOTION-1).
- Do not add lines to `View/ArenaView.cs` or `Presentation/SpriteActorPresenter.cs`. New non-test C# files carry a `/// <summary>`
  before the first type and stay under 800 lines.

## 6. Contracts and references

- R-AUTH / ADR 0008 / D9: Unity decides no outcome; every state shown is a delivered fact. Keys 1 and 2 send `Swap(slot)` and the
  server decides; no client-side check of the active slot (the slot buttons already send the same command).
- C-INPUT: the digit keys go through `Session.Input.Swap(byte slot)` (`RoguehalaNet/Net/InputSender.cs:102`).
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (HUD stage units, theme colours only, `HudUi.Label`, unscaled time, no
  allocation in a steady frame, `MatchHud` bind/unbind safety).
- `docs/OWNER_DECISIONS.md`: searched for the action bar, quest spell slots and weapon keys; no decided row governs this subject.

## 7. Behaviour to preserve

- Object names `Slot1`, `QSwap`, `Slot2`, `WishTile`, `Consumables`, `ControlsLine`, `Pickup`, `QuestRewards`,
  `QuestActionLauncher` (path `MatchUI/HudStage/PlayerHUD/QuestRewards/QuestActionLauncher`). The consumables row and the controls
  line keep their rects and text. The Wish tile keeps its own code and tests; only its position comes from `HudRegions`.
- The constants `BarBottom`, `SlotWidth`, `SlotHeight`, `SwapWidth`, `WishSize`, `BarGap`, `ConsumablesHeight` stay declared in
  `ActionBar.cs` under their names as `const float`, and `internal static float WeaponBarWidth { get ... }` stays; they take their
  values from the `HudRegions` constants. `RowSlot(int)` returning `Rect2` and `struct Rect2` stay (`RowSlot(i)` now returns the rect of
  `HudRegions.AbilityElement(i)` raised by the row's offset). `Tools/hud-seams.test.cjs` pins all of these declarations in that
  file and runs in the full gate: do not edit it. Also pinned there: `CreateWeaponBar`, `CreateWishTile`, `CreateConsumables`,
  `CreatePickupHint`, `ApplyWishRow`, `RefreshWishTile`, `TickWishTileMotion`, `RefreshCooldownSweep`; in `MatchHud.Pulses.cs`:
  `InputCue`, `Pulse`, `TickPulses`, `ClearPulses`, `_pulses = new`, `PulseSeconds`, `PulsePop`.
- `_questSlotTiles[i]` stays the tile's root `RectTransform` (the press pulse of P-UI-INPUT-CUES-1, `Pulses.cs:26`, targets it).
  Keep the `Session.CueSent` subscription (`MatchHud.cs:153`, `:188`).
- The "Equipped" line of the reward-choice modal (`_questChoiceSpells`) keeps its exact text (`QuestSpellText` for both slots, joined as
  today with a line break).
- `HudLayout.MinimapBarClearance` and `HudLayout.MinimapSize` do NOT change (seven capture tools under `UnityProject/Tools/` copy the
  formula; the minimap formula already keeps the minimap 152 from the stage's centre line, which the row at its smallest scale, .6,
  137 on each side, always clears).
- Nothing may write `_weaponBar.localScale` except `ApplyLegacyViewport`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-ROW-1 --base lane/ui-rework/base-2` prints `GATE PASS`.
- All tests of "Oracle files" green; the existing tests listed under "Existing tests that pin this code" green except the one
  expectation amended in its own `oracle-amend:` commit.

### Outcome (the implementer builds this)

1. **One row of six**, left to right (`HudRegions.AbilityElement(0..5)`). The bar box `CombatActions` is `HudRegions.ConsumablesBottom`
   (14) above the stage bottom, `HudRegions.AbilityWidth` (458) wide and
   `HudRegions.AbilityBottom - HudRegions.ConsumablesBottom + HudRegions.AbilityHeight` (110) high, as today (a child of `_hud`, built
   in `CreateWeaponBar`); inside it element `i` is `HudRegions.AbilityElement(i)` raised by the row's offset inside the bar (34 =
   `AbilityBottom - ConsumablesBottom`). Names: `Slot1`, `QSwap`, `Slot2`, `WishTile`; the new tiles `QuestSpell1` (Shift) and
   `QuestSpell2` (Q), children of `CombatActions`.
2. **Shift and Q are `AbilityTile`s**, built in `CreateWeaponBar` with the rest of the row (every harness that builds the bar has them).
   Visible whenever the bar is, also with no spell (state Empty): the row never changes width. They are kept in a field `_questTiles`
   (array of two `AbilityTile`).
3. **Nothing floats above the row; one prompt lane.** The box `QuestRewards` (child of `_hud`, built in `InitializeQuestRewardHud`)
   keeps its name and its child `QuestActionLauncher` (same button, captions and click behaviour) and nothing else; the launcher fills
   the box. The pickup pill `Pickup` stays a child of the bar (`_weaponBar`), at the same height as the box: its inset from the bar's
   bottom is `HudRegions.PromptBottom - HudRegions.ConsumablesBottom` (116), `HudRegions.PromptHeight` (26) high.
   - Alone, each is centred and 300 wide: the box at `HudRegions.Prompt()`, the pill at the same rect seen from the stage (on an
     unscaled bar).
   - When both show, they sit side by side and neither hides: the pickup pill left, the launcher right, each `HudRegions.PromptPair(i)`
     (220 wide, 4 on each side of the centre line). The launcher is the only way back into a reward choice the player put off, so a
     pickup in reach must not hide it.
   - The placement is rewritten only when the pair state changes. On a narrow stage the pill scales with the bar and the box does not:
     accepted.
4. **1 and 2 pick a weapon slot.** In `Net/MatchClient.cs`, in the block that already guards Space with `Hud.ChestOwnsDigits` (:474-477):
   `digit1Key` sends `Session.Input.Swap(0)`, `digit2Key` sends `Session.Input.Swap(1)`, only while
   `Session.Phase == MatchPhase.Active` (in planning 1 and 2 pick a spawn, `TacticalMapController.cs:421-422`). No client-side check of
   the active slot. The help window gets no new row here (`UI/HelpGuideView.cs` belongs to a later packet): say so in the hand-off.
5. **A swap pulses the two slots, never the bar.** In `InputCue` (`Pulses.cs:31-35`): a `Swap` with a slot pulses that slot (as today);
   a `Swap` without one pulses both slots.
6. **Narrow stages: the row shrinks, the minimap keeps its size.** `ApplyLegacyViewport` (`MatchHud.Legacy.cs:338`, the line at :345)
   sets the bar's scale to
   `HudRegions.AbilityRowScale(uiWidth, ChampionLeft + HudLayout.ChampionWidth(uiWidth, narrow), uiWidth - HudLayout.MinimapGutter - minimapSize)`
   instead of 1, where `minimapSize` is `HudLayout.MinimapSize(viewport.UiWidth, viewport.UiHeight, _uiPrefs != null ? _uiPrefs.MinimapScale : 1f)`
   (the inputs the minimap's own layout uses: `TacticalMapController.ApplyViewport`, :354-358, and `ApplyRewardsAnchor`,
   `MatchHud.Legacy.cs:357`). The bar's pivot is its bottom centre, so it stays centred on its bottom edge.

### Wiring in `UI/MatchHud.QuestRewards.cs`

- `InitializeQuestRewardHud` builds the `QuestRewards` box with the launcher only. `_questSlotLabels` goes; `_questSlotKeys` stays as
  the change key.
- `RefreshQuestSlotLabels(view, serverTime)` (:218) becomes the tile refresh: it runs on every `RefreshQuestRewardHud` call with a
  session, also when the quest view is null or has no spell (both tiles Empty), and no longer depends on `showHud`. It loops over
  `view.Spells` by index (no enumerator), and builds the hint string and the "Equipped" string only when a slot's change key
  differs: a steady frame with a delivered spell allocates nothing. Each tile has its own `CooldownSpan`; after `Show` it calls the
  tile's `Cooldown(spell.ReadyAt, total)` (0 and 0 for an empty slot). It writes `_questChoiceSpells` exactly as today.
- `showHud` now only governs the `QuestRewards` box (the launcher).
- `ResetQuestRewardHud` (:270) shows both tiles Empty, clears the spans and sets both `_questSlotKeys` to null (so the next delivery
  redraws even when it equals the last one).

## Existing tests that pin this code

Each verified by reading it. Run these classes on the stub (green) and again after the implementation.

| Test | Asserts | Status |
|---|---|---|
| `HudLegacyLayoutEditModeTests.WeaponBarIsCentredWithSlotsWishTileConsumablesAndSwapChip` (:139) | bar centred, 14 above the bottom, **width 318** (:148), slots 104 x 76 at bottom 48, Wish tile 64 x 64 right of slot 2, swap chip between the slots, consumables 28 high at bottom 14 | **amended: 318 becomes 458, own `oracle-amend:` commit, for the owner** |
| `HudLegacyLayoutEditModeTests.BannerAndToastsUseTheTopCentreAndBottomOneTwentyFourAnchors` (:224) | toast bottom 124, above the slot row | unchanged, stays green |
| `HudLegacyLayoutEditModeTests.HudEdgesLandOnTheScreenEdgesAtAnyCanvasScaleAndPixelRatio` (:283) | bar centred on the screen, bar bottom 14 units | unchanged (pivot is bottom centre, so a scaled bar stays centred) |
| `HudLegacyLayoutEditModeTests.MinimapIsASquareTwelveFromTheBottomRightCorner`, `ChestPanelAnchorsRight...` | minimap size and chest anchor from `HudLayout.MinimapSize` | unchanged: the minimap formula does not change |
| `HudLegacyLayoutEditModeTests.EveryHudGraphicUsesAColourFromTheTheme`, `ARecolouredThemeReskinsEveryHudGraphic` | every HUD graphic colour is a theme token (alpha variants allowed) | unchanged, must stay green with the tiles built in `CreateWeaponBar` |
| `HudTextScaleEditModeTests` (`TheInventoryCoversTheWholeMatchHud`: 63 labels; every label has `MythicTextScale`, no Ellipsis/Truncate) | `_weaponBar`, `_slotNames`, `_wishTile`, `_pickupText` by field, `ControlsLine` by path | unchanged: keep `_pickupText` and `ControlsLine`; build every label with `HudUi.Label` |
| `PlayerFlowsEditModeTests.NearbyRewardPickupUsesTheOfferedHandleAndDropIndex` | launcher found at `MatchUI/HudStage/PlayerHUD/QuestRewards/QuestActionLauncher`, active with a nearby action, its click opens the overlay | unchanged: that path stays |
| `PlayerFlowsEditModeTests.FullQuestSlotsPresentServerChoice...` | the "Equipped" text contains `SHIFT · Hook 2 charges · 3s` and `Q · Tornado 1 charge · Ready` | unchanged: the Equipped line keeps its exact text |
| `MatchHudIconPresentationEditModeTests` (:111) | `HudStage/PlayerHUD/CombatActions/WishTile` is a Button with the R hint | unchanged |
| `MatchHudWishBarrierEditModeTests` | Wish row text and barrier fill from delivered facts (builds no bar) | unchanged |
| `HudRegionsEditModeTests` | `HudUi.Box/Place` from a region measure the same as `region.On` | unchanged |
| `ChampionPanelNoGarbageEditModeTests` | the Wish row and slots allocate nothing while inputs stay | unchanged |
| `RoguehallaNet.Tests.HudRegionsTests` | the table of `HudRegions` (ability row 458 wide, 6 elements, prompt, stack, collisions) | unchanged: only additions to `HudRegions` |
| `RoguehallaNet.Tests.InputCueTests` | `Session.CueSent` raised once per sent Cast/Swap/Pickup/UseConsumable | unchanged: Swap(0)/Swap(1) from the keys raise the same cue |
| `Tools/hud-seams.test.cjs` | declarations that must stay in `MatchHud.ActionBar.cs`, `Pulses.cs`, ... (see section 7) | unchanged, runs in the full gate |

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/AbilityFaceTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/HudLegacyLayoutEditModeTests.cs` (the single amended expectation, own commit)
- this packet

(The stub commit also holds the synced copy of the pure files and the stub `AbilityTile.cs`; those the implementer replaces.)

## 9. Validation commands

- `bash client-unity/sync-net.sh` then `bash client-unity/sync-net.sh --check`
- `dotnet test client-unity/RoguehallaNet.Tests --filter AbilityFaceTests`, and the whole `HudRegionsTests`
- Unity classes: `AbilityRowEditModeTests`, `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`, `PlayerFlowsEditModeTests`,
  `MatchHudIconPresentationEditModeTests`, `MatchHudWishBarrierEditModeTests`, `ChampionPanelNoGarbageEditModeTests`
  (through `.agent-runs/lane-ui-rework/bin/unity-class.sh`)
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (through `in.sh`)

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Presentation only, one HUD partial family, one pure file. Hard parts: the layout rig must not break
(the two fixtures build the HUD from pieces only, so the tiles must be built in `CreateWeaponBar`), zero allocation in a steady
frame (spell refresh), and the narrow-stage scale not touching the minimap.

## 11. Known traps

- `HudUi.Box` with a bottom-left anchor takes x as the inset from the left and y as the inset from the bottom; the row's elements sit
  at 34 (`AbilityBottom - ConsumablesBottom`) inside the bar plus the element's own inset.
- Read `HudRegions.AbilityElement` for the heights it returns (swap chip 40 high, tiles 64, slots 76) and use them as they are: do
  not re-centre elements by hand.
- Tests are a separate assembly with no `InternalsVisibleTo`: anything a test reads must be `public` (the oracle uses only public API
  of `AbilityTile`, object paths, and reflection on private `MatchHud` members).
- Time in EditMode does not advance: no test reads a clock; `AbilityTile.Show` and the refresh take times as arguments.
- `HudUi.Label` adds `MythicTextScale` with `FitBox`; call `MythicTextScale.Refit()` after resizing a label's box.
- `HudTextScaleEditModeTests.NoLabelOfTheMatchHudIsOptedOutOfTheSetting` fails on a HUD label without `MythicTextScale`.
- `MatchHud` binds and unbinds many times: the tiles are built once; `ResetQuestRewardHud` must leave them Empty.
- `_pickup` is active whenever the delivered view has a pickup focus (`MatchHud.cs:437-438`); its parent is the bar, so it hides with
  the bar. Its placement is rewritten only when the pair state changes.
- The Tab map, notices and top-centre files belong to other packets of the wave.

## Notes from the oracle author

Run on the stub (dotnet through `in.sh`, Unity through `unity-class.sh`): every new test is red on an assertion, none on a compile
error or an exception. The existing classes pinning this code (`HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`,
`PlayerFlowsEditModeTests`: 77 tests; `MatchHudIconPresentationEditModeTests`, `MatchHudWishBarrierEditModeTests`,
`ChampionPanelNoGarbageEditModeTests`, `HudRegionsEditModeTests`: 13; dotnet `HudRegionsTests` and `InputCueTests`: 28;
`node --test Tools/hud-seams.test.cjs`: 18) were green on the stub before the amended expectation. A scratch implementation of the
three pure functions made all 15 `AbilityFaceTests` pass (then reverted), and a scratch two-line digit handler in `MatchClient.cs` made
`DigitKeysSelectTheWeaponSlot` pass, and without its `Phase == Active` condition the test failed on the planning half (then reverted):
so the two "none" halves of that test do bite.

Red tests and the assertion each fails on (stub):

| Test | Fails on |
|---|---|
| `AbilityFaceTests` (15 tests, all red) | `Spell` returns the default struct (no Empty/Ready/Cooling, badge 0), `CooldownSpan.Total` returns 0, `AbilityRowScale` returns 1, `PromptPair` returns the 220-wide centred region |
| `TheRowHoldsSixElementsLeftToRight` (3 windows) | the row is 318 wide, expected 458 |
| `NothingFloatsAboveTheRow` (2 windows) | `QuestRewards` measures as the old 290 x 70 box, expected `HudRegions.Prompt()` |
| `APickupAndAQuestActionShareThePromptLane` | the box alone is not at the `Prompt()` rect (its set-up asserts on the delivered nearby action pass) |
| `AnEmptySpellTileIsADimOutline` | no `QuestSpell1` under `CombatActions` |
| `ADeliveredQuestSpellShowsNameChargesAndCountdown` | no `QuestSpell1` under `CombatActions` (set-up asserts on server time and delivered spell pass) |
| `TheEquippedLineStillListsBothSpells` | with a quest view and no spell the Equipped line is empty, expected `SHIFT · Empty` + line break + `Q · Empty` |
| `DigitKeysSelectTheWeaponSlot` | key 1 sends no Swap |
| `ASwapWithoutASlotPulsesBothSlotsAndNeverTheBar` | a Swap with no slot pulses the bar, not the two slots (the with-slot step passes) |
| `TheRowShrinksOnANarrowStage` | the bar's scale at 700 x 600 is 1 (the 1280 x 800 half passes) |
| `AnUnchangedTileAllocatesNothing` | `tile.Root` is null |
| `ASteadyQuestRefreshFrameAllocatesNothing` | no `QuestSpell1` under `CombatActions` |
| `TheLongestSpellNamesFitTheTile` (scale 1 and 1.5) | no `QuestSpell1` under `CombatActions` |
| `EveryTileGraphicUsesAThemeColour` | `MatchHud` has no `_questTiles` field |
| `HudLegacyLayoutEditModeTests.WeaponBarIsCentred...` (amended) | bar width is 318, expected 458 |

Deviations from the spec's test list, and what could not be proved on the stub:
- Spec test 6 (`TheEquippedLineStillListsBothSpells`) passes on the stub as described (the line exists today), so a first step was added
  that the spec's "runs also when the quest view has no spell" implies: with a quest view and no spell, the line reads
  `SHIFT · Empty` + line break + `Q · Empty`. The spec's own state (Shoulder Charge, 2 charges, Ready) is the second step.
- Spec test 10 is two tests (`AnUnchangedTileAllocatesNothing` on a tile built directly, `ASteadyQuestRefreshFrameAllocatesNothing` on the full
  rig), so each fails on its own assertion.
- Spec test 7 (digit keys), chest half: it sets `MatchHud._chestFocus` by reflection but drives the key by hand (`Send`, then the client's
  `ReadInput`, then `FlushInput`) instead of `rig.Tap`, because a `Frame()` would run the chest's own digit handler (`Choose`) against a
  chest that does not exist. Planning half: a Planning snapshot is delivered through the rig's private transport and `ReadInput` is
  called directly. The map may also keep the client's keyboard blocked in planning, so this half pins "no swap" rather than the
  `Phase == Active` condition alone; the temporary check above showed it does fail without that condition.
- `TrainingHudRig.Start(practice: false)` is used (an arena room), not training.
- The steps of tests 3, 5, 6 and the second half of 10 after the first red assertion were not run on the stub: they follow the
  fixtures of `PlayerFlowsEditModeTests` (a delivered `State` plus `Snapshot`, then the HUD's `LateUpdate` and `TickPlayerFlows`) and the set-up
  assertions that precede them pass. If one of those later steps is wrong about the code, the implementer reports it with the failing
  line instead of editing the oracle.
- `TheRowShrinksOnANarrowStage` and the layout rig rely on `HudStage.ViewportChanged` calling `ApplyLegacyViewport` (`MatchHud.Features.cs:100`),
  so `LayoutAt` alone lays out the bar scale and the minimap.
- The spec's reference to `TacticalMapController.cs:396-397` is `:421-422` on the base; the spec's `MatchHud.cs:189-192` pulse code is
  `MatchHud.Pulses.cs:31-35`.
- The test file reads `_questTiles` (an `AbilityTile[]` of two) and `_pulses` (`List<(RectTransform Rect, float Start)>`), `_slots`
  (`Button[]`), `RefreshQuestRewardHud`, `InputCue`, `ClearPulses`, `TickPulses`, `PulseSeconds`, `_questChoiceSpells`, `_chestFocus` by
  reflection: keep those names and types.

Legacy doubts: none. Proposed DEVIATIONS row (owner signs): DEV-<n>, quest spells Shift and Q are ability tiles in the action row
(legacy has no quest spells); keys 1 and 2 pick a weapon slot (this removes a difference from legacy).

Fix pass (checker findings): (1) the tile's `Frame` Image child is now named in section 4 (the test reads it, so the spec states it);
(2) the Cooling/Empty name rule is now stated as "deactivate the `Name` box or clear its text", which is what `NameShown` checks.
No test code changed, so the red/green results above stand.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
