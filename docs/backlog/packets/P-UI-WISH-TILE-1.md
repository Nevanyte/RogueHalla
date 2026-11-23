# P-UI-WISH-TILE-1: the Wish tile is an AbilityTile, like Shift and Q

- **Serves:** note 2, item 1, of the coordinator's merge review of wave 2 (`reviewer-opus`, 2026-11-23): "the Wish tile is
  still hand-built in MatchHud.ActionBar.cs (CreateWishTile: its own seconds, charges, sweep, cast bar) while Shift and Q
  use AbilityTile (whose Cooldown() stores two values nobody reads): build Wish on AbilityTile with a cast bar"; and owner
  direction 2026-11-22 in the UI rework lane's chat: "ability and spell hub needs optimizing". Not in ORDER_OF_OPERATIONS.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head 632ecb349, holds P-UI-QUEST-OFFER-1); worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-y`. Gate with `--base lane/ui-rework/base-3`.
- **D-rules touched:** D1 presentation only. Legacy (`packages/client/src/ui/abilitySlot.ts`; `docs/rework/LEGACY_LAYOUT.md`
  section 4, row "Wish slot") has key cap R, icon, 10 px name, seconds, wedge, 3 px cast bar, a badge only when
  `maxCharges > 1`. This packet is closer to it than today (the name is on the tile, each fact once) but differs from
  section 4 in five places; propose ONE `DEVIATIONS.md` row (the implementer lists it in the hand-off, never edits the file):
  (1) the name is hidden while the Wish recharges (section 4 lists the 10 px name with no hide rule); (2) the icon is 16
  units in a corner (section 4: 24 x 24); (3) no blocking-reason line (section 4: the seconds slot shows "the blocking
  reason 11px red"); (4) the ready frame is not faction-coloured (section 4: "border + soft glow in the ability colour";
  the glow wash still is); (5) a Wish holding a charge shows no countdown for the next one (`abilitySlot.ts:289` shows the
  next charge's seconds in any state; here the hint card keeps it). D2 n/a. D5/D6 none. D9 untouched: the tile draws
  delivered facts and sends nothing.

All paths below are under `client-unity/UnityProject/Assets/Scripts/` unless they start with `client-unity/`, `crates/`,
`packages/` or `docs/`. `UI/` is `Scripts/UI/`.

## 1. ID and outcome

The Wish tile (key R) becomes an `AbilityTile`, the class Shift and Q use, drawn from a new pure `AbilityFaces.Wish`.
A player sees the Wish's name on the tile instead of "1/2 · 9", the countdown once (the number, not also in a name line),
a charge badge only above one charge, a wedge that shrinks while it recharges, and a cast bar that fills from the left
(today it draws full for the whole cast). The tile reads Ready exactly when the server would accept the cast.

What is wrong today (all in the base):

- `CreateWishTile` (`UI/MatchHud.ActionBar.cs:110-141`) hand-builds a second tile: `Key` (5,4, 14 x 12), `WishIcon` (24, top
  centre), `Seconds` (0,6, 64 x 24), `Name` (2,36, 60 x 12), `Charges` (42,44, 20 x 14), `CooldownSweep`, `CastBar`,
  `WishGlow`, a hover-tinted button. Fields: `_wishIcon`, `_wishSeconds`, `_wishCharges`, `_wishSweep`, `_wishCastBar`
  (`UI/MatchHud.Legacy.cs:36-38`), `_wishSummary` (`UI/MatchHud.Features.cs:33`).
- It says each fact twice. `RefreshWishTile` (`UI/MatchHud.ActionBar.cs:188-224`) writes the whole seconds into `Seconds`;
  `RefreshOwnerPresentation` (`UI/MatchHud.cs:339`) writes `WishIconText` ("1/2 · 9", `UI/MatchHud.IconPresentation.cs:79`)
  into `Name`. Countdown and stock are each shown twice, `Name` (y 36 to 48) overlaps `Charges` (y 44 to 58) at x 42 to 62,
  and the tile never names the spell.
- The cast bar draws full all cast: `_wishCastBar` is a Filled, Horizontal image that is never given a sprite
  (`UI/MatchHud.ActionBar.cs:127-129`), and a Filled image without a sprite draws its whole rect.
- `ClearOwnerPresentation` (`UI/MatchHud.cs:346-358`) resets the summary, but `RefreshLegacyHud(null, 0)` returns before
  `RefreshWishTile` (`UI/MatchHud.Legacy.cs:324-328`, the `me == null` return at 303): the seconds, wedge and bar of the last
  owner stay after a rebind. `AbilityTile.Cooldown` (`UI/AbilityTile.cs`) stores `ReadyAtMs` and `TotalMs`; nothing reads them.
- The tile's ready test is its own: `ready = remainingMs <= 0 && Charges > 0` (`UI/MatchHud.ActionBar.cs:202`); the server
  does not read charges (item 3).

## 2. Dependencies

- P-UI-QUEST-OFFER-1 (merged into `lane/ui-rework/base-3`; it did not touch `AbilityTile.cs`, `AbilityFace.cs` or
  `AbilityRowEditModeTests.cs`; it changed `MatchHud.Features.cs` by seven lines, away from `_wishSummary`).
- P-UI-ALLOC-METER-1 (`Assets/Tests/Editor/AllocationMeter.cs`, on the base).
- P-UI-ABILITY-MOTION-1 follows this packet (the lane lead rebuilds it on this tree). It must use `Sweep` and `CastBar` of
  `_wishAbilityTile` in place of `_wishSweep` and `_wishCastBar`, run `SweepAt` for the Wish tile as well as `_questTiles`,
  drop its `CreateWishTile` sprite edit (`Cast` already has the sprite), re-read `_wishMotion.Observe` (one call here,
  covering both branches), and change its test that ends "RechargeMs 0 and ReadyAt ahead leaves the wedge off": that Wish
  draws a full wedge (item 3). Leave `Cooldown(ReadyAt, RechargeMs)` as the store here, add no per-frame code, do not write
  the tile's scale.

## 3. Relevant legacy paths

- `packages/client/src/ui/abilitySlot.ts:88-92`: `left = readyAt - serverNow`; `left > 0` gives `secondsLeft = ceil(left/1000)`
  and `sweep = clamp01(left / max(1, cooldownLen))`. Legacy slot state follows `readyAt` ("a charge in hand = ready").
- `packages/client/src/ui/abilitySlot.ts:289`: the seconds slot shows the next charge's seconds in any state (deviation 5).
- `docs/rework/LEGACY_LAYOUT.md` section 4 ("4. Weapon bar", row "Wish slot"): 64 x 64, key cap R, 24 x 24 icon, 10 px
  name, seconds 15 px bold, 3 px cast bar, badge only when `maxCharges > 1`.
- The rule is the server's (Rust, read-only here): a Wish cast is refused while `now < ready_at(slot)`
  (`crates/rh-sim/src/systems/abilities/mod.rs:117`; it reads no charge count; the other refusals at lines 99-118 are a cast in
  flight, stasis, taunt, pull, the teleport grace and the cast-blocking statuses, none of which the tile judges: the cast bar
  shows a cast in flight). `ReadyAt` is when the next charge returns: `spend_charge` sets `ready = full - (max - 1) * recharge`
  and `charges_at` gives a charge once `full - now` is within `(max - 1) * recharge`
  (`crates/rh-sim/src/abilities/mod.rs:523-532`).
- `docs/OWNER_DECISIONS.md`: no row on the Wish tile (searched "Wish", "ability tile"). Frost Wall naming is
  `P-FROST-WALL-1.md` (`frost_wish = Wall`, id stays `Freeze`), rendered by `HudContentText.WishName`
  (`client-unity/RoguehallaNet/Net/Ui/HudContentText.cs:117`).

## 4. New paths

- `client-unity/RoguehallaNet.Tests/WishFaceTests.cs` (oracle).
- `client-unity/UnityProject/Assets/Tests/Editor/WishTileEditModeTests.cs` and its `.meta` (oracle).

## 5. Exclusive write scope

- `UI/AbilityTile.cs`
- `client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs` and its synced copy `UnityProject/Assets/RoguehallaNet/Net/Ui/AbilityFace.cs`
  (`bash client-unity/sync-net.sh` through the wrapper; the `.meta` exists)
- `UI/MatchHud.ActionBar.cs` (`CreateWishTile`, `RefreshWishTile`, new `ClearWishTile`, `WishNameFor`, cache fields)
- `UI/MatchHud.Legacy.cs` (the declarations of the five wish fields, which share lines 36-38 with other fields)
- `UI/MatchHud.Features.cs` (`_wishSummary`, line 33)
- `UI/MatchHud.IconPresentation.cs` (`WishIconText`, line 79)
- `UI/MatchHud.cs`, two lines only: delete `Set(_wishSummary, WishIconText(...))` in `RefreshOwnerPresentation` (line 339);
  replace `Set(_wishSummary, "—")` in `ClearOwnerPresentation` (line 354) with `ClearWishTile();`
- Not in scope: `MatchHud.Pulses.cs`, `MatchHud.QuestRewards.cs`, `Presentation/WishFxModel.cs` (its `WishTileMotion.Observe` still
  calls a Wish ready only with `Charges > 0` and `ReadyAt` passed, so the ready pulse and the tile can differ for one
  snapshot in the 0-charge moment of item 3), `ApplyWishRow`, `Tools/hud-seams.test.cjs`, `HudLegacyLayoutEditModeTests`
  (unchanged, stale message included), `AbilityRowEditModeTests` (unchanged), per-frame motion (P-UI-ABILITY-MOTION-1).
- The oracle files and the three amended tests below are the oracle author's: read-only for the implementer.

## 6. Contracts and references

- R-AUTH / D9: Unity decides nothing; the tile only draws delivered facts (`WishView`, `UI/MatchHud.cs` `RefreshOwnerPresentation`).
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (HUD units, theme colours only, `HudUi.Label`, unscaled time, no
  allocation in a steady frame, `/// <summary>` on a new type, files at most 800 lines; `ArenaView.cs` and
  `SpriteActorPresenter.cs` untouched).
- The control mapping is the owner's (`docs/backlog/packets/P-QUEST25-CONTROLS.md`): R is the Wish; this packet binds no key.

## 7. Behaviour to preserve

- `CreateWishTile(UiTheme theme)`, `RefreshWishTile(WishView?, double, UiTheme)`, `TickWishTileMotion`, `ApplyWishRow` keep their
  names and the file (`Tools/hud-seams.test.cjs:34-38` pins them); no method name is declared in two `MatchHud*.cs` files
  (`ClearWishTile` and `WishNameFor` appear nowhere else today); `MatchHud.Legacy.cs` stays under 420 lines (374), `MatchHud.cs`
  under 600 (571): this packet only removes lines from both.
- The tile's object name, parent (`_weaponBar`), rect (`BarElement(3)` = `AbilityElement(3)` raised by `RowRaise`,
  `UI/MatchHud.ActionBar.cs:38-41`), the press pulse, the hint card and `_wishHint` writer (`MatchHud.cs:340`), `_wishGlow` (built
  as today, then `SetSiblingIndex(1)`, disabled), `_wishMotion`, `TickWishTileMotion`, `_wishDetails`, `_wishCard`, and the call order
  in `RefreshLegacyHud` (`UI/MatchHud.Legacy.cs:328`).
- The quest tiles' boxes do not move; their `Show(face, name, hint)` behaves as before; `AbilityTile`'s six-argument constructor still compiles.
- `Spell` (`AbilityFaces.Spell`) is not touched.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-WISH-TILE-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper passes (a `MatchHud` member is removed).
- All oracle tests below green; the existing classes listed under "Existing tests" green (the three amended ones as amended).

## Design to implement

### Outcome rules

1. **One tile class.** `CreateWishTile(UiTheme theme)` builds
   `_wishAbilityTile = new AbilityTile(_weaponBar, "WishTile", HudRegions.AbilityElement(3), RowRaise, "R", _root, MythicGlyph.Wish)`
   and `_wishTile = _wishAbilityTile.Root`. `_wishHint = _wishAbilityTile.Hint`, then `_wishHint.Text = "R · Wish\nAwaiting server"`;
   the first draw is `Show(AbilityFace.Empty, AbilityFaces.NoWishLabel)`. Deleted: `_wishIcon`, `_wishSeconds`, `_wishCharges`,
   `_wishSweep`, `_wishCastBar`, `_wishSummary`, `WishIconText`, the hover tint. Kept: `_wishGlow`, `_wishMotion`,
   `TickWishTileMotion`, `_wishDetails`, `_wishCard`, `_wishHint` (still written in `MatchHud.cs`).
2. **Boxes, the same for all three tiles** (HUD units in the 64 x 64 tile, from the top-left; the quest tiles' existing boxes do not
   move; colours only from `UiTheme`): `Key` 5,4 30 x 12; `Name` centred 60 x 26 (y 19 to 45); `Seconds` centred 40 x 20 (y 22 to
   42); `Charges` bottom right, inset 2, 20 x 14 (x 42 to 62, y 48 to 62); `CooldownSweep` inset 1; new `Cast` bottom left, 64 x 3,
   created last. Only the Wish tile has `Icon`: 16 x 16, top right, inset 4 and 3 (x 44 to 60, y 3 to 19), a `MythicUi.Icon` of the
   constructor's glyph, created after the wedge and before the labels. No two of `Key`, `Icon`, `Name`, `Charges` share any area
   (two boxes overlap only when the shared width and the shared height are both above .01 unit); `Seconds` shares area with `Name`
   only, and the two are never shown together. All lie inside the tile.
3. **When the tile reads Ready, and what it shows**, from `AbilityFaces.Wish`. The tile reads Ready exactly when `ReadyAt` is not after
   the server time, and Cooling otherwise. A Wish with 1 of 2 charges and the second recharging reads Ready (name, badge, no
   countdown: the hint card keeps charges, maximum and time). A Wish with 0 charges reads Cooling, with the whole seconds until
   `ReadyAt` and the wedge. 0 charges with `ReadyAt` already passed (a moment between two snapshots) reads Ready, because the
   server would accept it; the badge says 0.

   | State | Name | Seconds | Wedge | Badge | Cast bar | Frame and surface |
   |---|---|---|---|---|---|---|
   | no Wish delivered | "—" | "" | off | "" | off | the Empty look: dim, button not interactable |
   | ready | the Wish's name | "" | off | stock if `MaxCharges > 1` | per cast | `BorderStrong` |
   | cooling | "" (box off) | whole seconds | on, `left / RechargeMs` | as ready | per cast | `Border` |

   The wedge, while cooling, is `left / RechargeMs` clamped to 0..1 when `RechargeMs` is a positive finite number and 1 otherwise
   (`AbilityFaces.Spell` has the same rule; it differs only for +infinity, which Spell reads as a positive total and draws 0; Spell
   is not touched). The icon is full colour when ready and alpha .3 otherwise (today .3 while recharging; legacy .55). The frame
   follows the phase only: a cast in flight no longer turns it `BorderStrong` (the cast bar shows it).
4. **The name** is `HudContentText.WishName(HudContent, ability)` ("Blaze", "LastStand", "Frost Wall" under a wall block), cached by
   `WishNameFor` against the block and the ability, so an unchanged Wish allocates no string. **The cast bar** is `Cast` in
   `AbilityTile`: `UiSprites.Flat()` sprite, `Filled`, `Horizontal`, `fillOrigin` 0, `theme.Accent`, no raycast, on only while
   `face.Cast >= 0`, `fillAmount = clamp01(face.Cast)`; written at snapshot rate, no per-frame code.
5. **`RefreshWishTile(WishView? wish, double serverTimeMs, UiTheme theme)`** keeps its signature (`theme` unused now). It returns on a
   null tile, makes its one `_wishMotion.Observe(wish, serverTimeMs, Time.unscaledTime)` call (covers a null Wish as today), then
   `_wishAbilityTile.Show(AbilityFaces.Wish(wish, serverTimeMs), AbilityFaces.WishLabel(wish, name))` (two arguments, `name` from
   `WishNameFor`, "" without a Wish) and `Cooldown(ReadyAt, RechargeMs)`, or `Cooldown(0, 0)` without a Wish. New null-safe
   `ClearWishTile()` draws the no-Wish state (`Show(AbilityFace.Empty, NoWishLabel)`, `Cooldown(0, 0)`); `ClearOwnerPresentation`
   calls it where it wrote `_wishSummary`.

### New and changed API

```csharp
// RoguehallaNet/Net/Ui/AbilityFace.cs (namespace Roguehalla.Net.Ui). AbilityFace gains no field: Wish uses Phase
// (Ready or Cooling, never Casting), Sweep, Seconds, Badge and Cast; no blocking reason is drawn (as today).
public static class AbilityFaces {
    public const string NoWishLabel = "—";
    /// <summary>The Wish's face. null: Empty. left = ReadyAt - now (NaN or infinite times count 0). left above 0:
    /// Cooling, Seconds = ceil(left / 1000) capped at int.MaxValue, Sweep = RechargeMs finite and above 0 ?
    /// clamp01(left / RechargeMs) : 1. Else Ready, Seconds 0, Sweep 0 (the server's test: ready_at not after now).
    /// Badge = MaxCharges above 1 ? Charges : -1. Cast = no cast ? -1 :
    /// clamp01((now - StartedAt) / max(1, LandsAt - StartedAt)), in any phase.</summary>
    public static AbilityFace Wish(WishView? wish, double nowMs);
    /// <summary>abilityName, or NoWishLabel when wish is null or the name is null or empty.</summary>
    public static string WishLabel(WishView? wish, string abilityName);
}
// UI/AbilityTile.cs
public AbilityTile(Transform parent, string name, HudRegion region, float raise, string keyCap,
    Transform hintOverlay, MythicGlyph? icon = null);   // the six-argument calls still compile
public void Show(AbilityFace face, string name);       // as Show(face, name, hint) but the hint is not touched
public UnityEngine.UI.Image CastBar { get; }           // child "Cast"
public UnityEngine.UI.Image Icon { get; }              // child "Icon"; null without a glyph
```

Worked (now 5000, RechargeMs 10000, MaxCharges 1): ReadyAt 9000 is Cooling, Seconds 4, Sweep .4, Badge -1; ReadyAt 5001 gives
Seconds 1, 6000 gives 1, 6001 gives 2, 25000 gives Sweep 1. ReadyAt 5000 is Ready (the server refuses only while now is before
it). A cast 4500 to 6500 gives Cast .25 at 5000. ReadyAt 4000 with Charges 0 of 2 is Ready, Badge 0, the name shown.

In `AbilityTile.Apply`: `shownName = phase == Cooling ? "" : name` (was `phase == Ready`; quest tiles pass "" when Empty, so
nothing changes for them); the icon colour (`MythicUi.GlyphColor(glyph)`, alpha 1 when Ready, .3 otherwise) is written in the block
that runs on a phase change; `_cast.enabled` and `_cast.fillAmount` are written only when they differ. `Casting` still maps to
Ready (unused here). `Show(face, name)` passes a null hint, which `Apply` skips; `Show(face, name, hint)` is unchanged.

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter WishFaceTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> WishTileEditModeTests,AbilityRowEditModeTests,HudLegacyLayoutEditModeTests,HudTextScaleEditModeTests,MatchHudIconPresentationEditModeTests,MatchHudWishBarrierEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-WISH-TILE-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Risk is moderate: one pure function, one tile class gaining an icon and a cast bar, one
hand-built tile deleted with five fields, three existing tests whose set-up reads the deleted fields. The look (16-unit corner
icon, two-line "Frost Wall", the frames) has not been seen in a render; the lane's next Web round looks at it.

## 11. Known traps

- The hint has one writer, `MatchHud.cs` (`_wishHint.Text`): `Show(face, name)` must not write it, or a text cleared there that
  equals the tile's last hint is never rewritten.
- `MatchHudWishBarrierEditModeTests` builds no tile: `RefreshWishTile` and `ClearWishTile` return on a null tile.
  `ClearOwnerPresentation` runs from `Bind`, `ShowMenu` and for a spectator: the tile shows the dash after each.
- The tile's children start as Gloss, Frame, wedge, Icon, Key, Name, Seconds, Charges, Cast: `SetSiblingIndex(1)` for the glow after
  the tile is built. The Wish button loses its hover tint and is not interactable while empty, like Shift and Q: hover still shows
  the hint; keyboard focus skips it.
- `HudUi.Label` text shrinks to fit; call `MythicTextScale.Refit()` after resizing a box. Tests are a separate assembly with no
  `InternalsVisibleTo`: a member a test reads must be `public` (the oracle reads `HudContent`, `Show`, `Cooldown`, `ReadyAtMs`,
  `TotalMs`, `Root` and the private fields `_wishAbilityTile`, `_wishHint`, `_wishGlow`, `_wishTile`, `_questTiles` by reflection:
  keep those names).
- Never edit `UI/CardDisplayCatalog.cs` or `BakedContent.cs`; do not add lines to `View/ArenaView.cs` or
  `Presentation/SpriteActorPresenter.cs`.
- A new `.meta` appears on the first Unity run; none is expected here (no new Unity script).

## Existing tests that pin this code (verified by reading)

Green and unchanged (the implementer keeps them green):

- `AbilityRowEditModeTests` (`Assets/Tests/Editor/AbilityRowEditModeTests.cs`): the row order and the `WishTile` rect
  (line 234, `TheRowHoldsSixElementsLeftToRight`); `EveryTileGraphicUsesAThemeColour` (line 536: every graphic of the quest tiles
  in Empty, Ready and Cooling is a theme colour; the new `Cast` is `Accent`); `TheLongestSpellNamesFitTheTile` (line 500:
  quest tile `Name`, `Seconds`, `Key`, `Charges` labels fit at scale 1 and 1.5); `AnUnchangedTileAllocatesNothing` (line 454:
  200 unchanged `Show(face, name, hint)` calls allocate nothing) and `ASteadyQuestRefreshFrameAllocatesNothing` (line 473).
- `HudLegacyLayoutEditModeTests` (`WeaponBarIsCentredWithSlotsWishTileConsumablesAndSwapChip`, line 140; `WishTile` rect at line 155;
  its two theme tests walk every graphic, so the Wish `Icon` (glyph colour at alpha .3 or 1; `Owns` ignores alpha) and `Cast`
  (`Accent`) must be theme tokens).
- `Tools/hud-seams.test.cjs` (lines 34-38: `CreateWishTile`, `RefreshWishTile`, `TickWishTileMotion`, `ApplyWishRow` in
  `MatchHud.ActionBar.cs`).
- `RoguehallaNet.Tests/WishFxModelTests.cs` (`WishTileMotion`; not touched).

Amended by the oracle author (each in its own `oracle-amend:` commit, for the owner to approve; the base versions read fields this
packet deletes, so they would error on the implementation):

- `HudTextScaleEditModeTests` (`Assets/Tests/Editor/HudTextScaleEditModeTests.cs:188-190`): `SetUp` builds the inventory, so every test
  of the class errors on `GetField("_wishSeconds")`. The three rows now read `Find(wishTile, "Seconds/Label")` ("125"),
  `"Name/Label"` ("LastStand", "Frost Wall") and `"Charges/Label"` ("3"); the count stays 61.
- `MatchHudIconPresentationEditModeTests.WishSummaryStaysCompactWhileFullDeliveredDescriptionRemainsFocusedAndDismissible`
  (`Assets/Tests/Editor/MatchHudIconPresentationEditModeTests.cs:106-149`): read `_wishSummary` and `WishTile/WishIcon`, asserted
  "1/2 · 1". Now reads `WishTile/Icon` and, at 5000 with ReadyAt 5500, asserts `Name/Label` "" and `Seconds/Label` "1" (the fixture's
  1 of 2 with `ReadyAt` ahead is a lock the server refuses, so Cooling); every hint line stays.
- `MatchHudWishBarrierEditModeTests` (`Assets/Tests/Editor/MatchHudWishBarrierEditModeTests.cs`): `SetUp` sets `_wishSummary` (line 35), so
  all five tests error. The amendment drops that label and line, the "0/2 · 2:05" assert (line 51) and the six `_summary` asserts
  (lines 84, 108, 114, 129, 131, 136) (the fixture builds no tile; `WishTileEditModeTests` holds those facts). The hint-card asserts stay.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/WishFaceTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/WishTileEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/WishTileEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/HudTextScaleEditModeTests.cs` (oracle-amend)
- `client-unity/UnityProject/Assets/Tests/Editor/MatchHudIconPresentationEditModeTests.cs` (oracle-amend)
- `client-unity/UnityProject/Assets/Tests/Editor/MatchHudWishBarrierEditModeTests.cs` (oracle-amend)
- `docs/backlog/packets/P-UI-WISH-TILE-1.md`

## Notes from the oracle author

(filled in with the oracle commit)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
