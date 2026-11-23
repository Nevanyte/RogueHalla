# P-UI-ABILITY-MOTION-1: the action row moves every frame: sweeps, the attack timer, centred pops

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane): "make the front end as fluid as
  possible", "ability and spell hub needs optimizing"; and note 2, items 2 and 3, of the coordinator's review of wave 2
  (`reviewer-opus`, 2026-11-23): one pulse owner, the minimap clearance comment.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head 4102837d4). It holds P-UI-VITALS-EASE-1,
  P-UI-QUEST-OFFER-1 and P-UI-WISH-TILE-1. Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-y`. Gate with
  `--base lane/ui-rework/base-3`.
- **D-rules touched:** D1 presentation. The attack timer moves from a wedge in each weapon slot to a bar on the weapon in
  hand, and the press pop is 8 percent (it was 10): propose ONE `DEVIATIONS.md` row (the implementer lists it in the
  hand-off, never edits the file); legacy `packages/client/src/ui/weaponBar.ts` draws the shared attack cooldown as a
  `wb-sweep` wipe in each slot (lines 78-81 create it, 207-214 write `--cd` and `hidden` on every slot: "The cooldown is
  shared, so both slots show it"). D2 n/a. D5/D6 none. D9 untouched: the HUD clock only animates displays of delivered
  facts between snapshots, is capped 150 ms past the newest one (`HudClock.MaxAheadMs`), and nothing is sent or decided
  from it. No key is bound or changed (cues go through `InputCue`). No owner decision in `docs/OWNER_DECISIONS.md` covers
  this subject (searched: fluid, action row, ability row, spell bar, pulse, sweep, cooldown).

All paths below are under `client-unity/UnityProject/Assets/Scripts/` unless they start with `client-unity/`, `crates/`,
`packages/` or `docs/`. `UI/` is `Scripts/UI/`. Line numbers are those of the base.

## 1. ID and outcome

The three ability wedges (R, Shift, Q) and the Wish cast bar move on every frame, not 20 times a second; the attack timer
is one bar on the weapon in hand, not a dark wedge over both slots; a press pops its element about its centre, at 8
percent, through one owner (`HudPulses`); the Wish tile has one scale writer. When this lands: a late snapshot no longer
stalls the wedges (they stop 150 ms past the newest snapshot), an auto attack with a fast weapon no longer darkens both
slots, and a pressed element does not drift toward its anchor corner.

What is wrong today (verified on the base):

- The wedges and the Wish cast bar are written only from the snapshot's own time: `AbilityTile.Show` through
  `RefreshWishTile` (`UI/MatchHud.ActionBar.cs:184-190`) and the quest refresh (`UI/MatchHud.QuestRewards.cs:397-432`,
  time argument `Session.ServerTimeMs`). `AbilityTile.Cooldown` (`UI/AbilityTile.cs:128-130`) stores `ReadyAtMs` and
  `TotalMs` and nothing reads them.
- The attack timer is `_slotSweeps` (declared `UI/MatchHud.Legacy.cs:32`, built `UI/MatchHud.ActionBar.cs:70-74` as a
  radial wedge at 62 percent black), written by `RefreshCooldownSweep` (`UI/MatchHud.ActionBar.cs:216-224`, called
  `UI/MatchHud.Legacy.cs:329`) on both slots.
- A press pops the element about its anchor corner: `TickPulses` (`UI/MatchHud.Pulses.cs:47-66`) scales `localScale`
  and `HudUi.Place` sets the pivot to the anchor. `MatchHud.Pulses.cs` keeps its own list `_pulses` (line 18;
  `PulseSeconds` .15, `PulsePop` .1, line 17) beside `HudPulses` (`UI/UiMotion.cs:77-130`, `Pop` already .08), which
  `MatchHud` never uses. The Wish tile has two scale writers: `TickWishTileMotion` (`UI/MatchHud.ActionBar.cs:204-214`,
  line 213) and `TickPulses` (line 64) multiplies it. Reduced motion is read two ways (`ShowcaseSettings.Current
  .ReducedMotion` at `MatchHud.Pulses.cs:51` and `MatchHud.ActionBar.cs:208`; `UiMotion.Reduced` in `HudPulses`). The row
  ages on `Time.unscaledTime` (`MatchHud.Pulses.cs:42,50`, `MatchHud.ActionBar.cs:187,207`), which does not advance in
  EditMode.

## 2. Dependencies

P-UI-VITALS-EASE-1, P-UI-QUEST-OFFER-1, P-UI-WISH-TILE-1 and P-UI-ALLOC-METER-1/-2 (`Assets/Tests/Editor/AllocationMeter.cs`)
are in the base. `Roguehalla.Net.Ui.HudClock` (`client-unity/RoguehallaNet/Net/Ui/HudClock.cs`, synced copy
`client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/HudClock.cs`) is in the base and is not changed.

## 3. Relevant legacy paths

- `packages/client/src/ui/weaponBar.ts:78-81`: each slot builds a `wb-sweep` span, hidden. (verified by reading)
- `packages/client/src/ui/weaponBar.ts:207-214`: the shared attack cooldown writes `--cd` (degrees) and `hidden` on every
  slot, so both slots show it. This packet deliberately differs (bar on the weapon in hand only): the DEVIATIONS row above.
- `packages/client/src/ui/abilitySlot.ts`: the legacy ability wedge (the tile's look; not changed here).

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AbilityMotionEditModeTests.cs` (+ `.meta`): the oracle (oracle author).
- No new non-test file is required. A new `MatchHud.*.cs` partial is allowed only if a file would pass 800 lines.

## 5. Exclusive write scope

- `UI/MatchHud.ActionBar.cs` (226 lines now), `UI/MatchHud.Pulses.cs`, `UI/AbilityTile.cs` (`SweepAt`, `CastAt` and the
  comment of `Cooldown`), `UI/UiMotion.cs` (the `HudPulses` class only), `UI/HudLayout.cs` (the comment of item 8 only).
- `UI/MatchHud.cs` (570 lines now; it must stay under 600): the two calls in `HudLateUpdate` (lines 245-246) become
  `TickActionRow();`, plus `ResetActionClock();` in `Bind`, `Unbind` and `ResetTransientPresentation`.
- `UI/MatchHud.Legacy.cs` (374 lines now; it stays under 420): the `_slotSweeps` declaration (line 32) and the
  `RefreshCooldownSweep` call (line 329). `_attackCooldownTotal` (line 40) stays.
- Not in scope: `UI/MatchHud.QuestRewards.cs`, `.PlayerFlows.cs`, `.Features.cs`, `.Vitals.cs`, `Presentation/WishFxModel.cs`
  (the wash's edges are read from values), `AbilityFace.cs`, `CardDisplayCatalog.cs`, `BakedContent.cs`, the Wish tile's
  texts and layout, `client-unity/RoguehallaNet/`.
- New `MatchHud` method names (`TickActionRow`, `ResetActionClock`, `ActionNowMs`, `WriteAttackBars`) are declared once
  across `MatchHud*.cs` (`Tools/hud-seams.test.cjs` fails on a duplicate); `RefreshWishTile`, `RefreshCooldownSweep` and
  `TickWishTileMotion` stay in `MatchHud.ActionBar.cs`; `HudSeconds` is a property, `HudLocalMs` a field.

## 6. Contracts and references

- R-AUTH / ADR 0008 / D9: Unity decides nothing; the clock animates displays of delivered facts only.
- Motion rules of the lane (`COMMON.md`): unscaled time, instant when `UiMotion.Reduced`, nothing that allocates in a
  steady frame. `MatchHud` binds and unbinds sessions many times: never leave a handler on an old session; keep the
  `Session.CueSent` subscription (`UI/MatchHud.cs:151`) and the press pulses of P-UI-INPUT-CUES-1.
- `UiMotion.ReducedOverride` (`UI/UiMotion.cs:11`) is the test seam and the one reduced-motion source.

## 7. Behaviour to preserve

- The tile's texts, phase and frame stay on the snapshot time (their whole-second keys): only wedge fill, cast fill and the
  attack bars move per frame.
- `HudClock` semantics (`HudClock.cs`): `Now` never goes down until `Reset`; a lower server time resets itself.
- The control mapping (owner's, P-QUEST25-CONTROLS): no key is bound or changed.
- A player with reduced motion on sees no difference in the pop (`UiMotion.Reduced` is the same expression as the deleted
  read).
- `UpdatePromptLane`, which re-places the pickup pill, is not touched.
- Never write `CombatActions.localScale` (P-UI-ABILITY-ROW-1 scales it on a narrow stage).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-MOTION-1 --base lane/ui-rework/base-3` passes.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through `in.sh` passes (the existing cases need no change).
- `AbilityMotionEditModeTests` (15 tests) green; the amended `AbilityRowEditModeTests.ASwapWithoutASlotPulsesBothSlotsAndNeverTheBar`
  green; every other test of the classes listed under "Existing tests that pin this code" green and unedited.

### The design (outcome, numbers, API)

1. **One time base.** `MatchHud` gets `public System.Func<double> HudLocalMs` (local ms, default
   `Time.unscaledTimeAsDouble * 1000`; public: the test assembly has no `InternalsVisibleTo`) and `float HudSeconds =>
   (float)(HudLocalMs() / 1000d)`. Every time the row reads goes through them: `Pulse` and `TickPulses`, the one
   `_wishMotion.Observe` call of `RefreshWishTile` and `Flash` / `ReadyPulse` in `TickWishTileMotion` (seconds), and the
   clock (local ms). Nothing in the row reads `Time.unscaledTime`. `_hudClock.Now`, `SweepAt` and the delivered Wish times
   are server milliseconds.
2. **A HUD clock.** `MatchHud` owns one `Roguehalla.Net.Ui.HudClock`, `_hudClock`. A new per-frame method `TickActionRow()`
   in `UI/MatchHud.ActionBar.cs` replaces the two calls `TickWishTileMotion(); TickPulses();` in `HudLateUpdate` (after
   `TickFeatures`, which runs `TickPlayerFlows`; before `TickOwnerVitals`) and does, in order: feed the clock
   (`OnServerTime(_session.ServerTimeMs, HudLocalMs())` only when `_session != null && _session.Latest != null`: the server
   time is 0 with no snapshot); `TickWishTileMotion()`; `TickPulses()`; then, only when `_hudClock.Ready`, `now =
   _hudClock.Now(HudLocalMs())` and the fills of item 3. It works with a null session (the layout rig has none: no clock,
   no fill). Unity objects are tested with `!= null`, never `?.`: the tick guards `_wishAbilityTile` and each
   `_questTiles[i]`. A private `ResetActionClock()` (`_hudClock.Reset()`, the two previous wash values 0,
   `_attackReadyAtMs` 0, `_attackCooldownTotal` 1, attack bars off) is called from `Bind` (after `Unbind()`, before
   `ClearOwnerPresentation`; `MatchHud.cs:146-163`), from `Unbind` after `ClearPulses()` (`MatchHud.cs:189`) and from
   `ResetTransientPresentation` right after `_wishMotion.Reset()` (`MatchHud.cs:209`); VITALS-EASE's `ClearOwnerVitals`
   calls stay as they are. No `TimelineReset` subscription; `MatchHud.PlayerFlows.cs` is untouched.
3. **Wedges and bars move every frame** from `now`, without allocation, writing a value only when it differs:
   - `AbilityTile.SweepAt(double nowMs)` (new, public) writes `_sweep.fillAmount` directly and never touches `_sweepFill`
     or the wedge's `enabled` (`Show` runs every frame on the quest tiles and would fight it). It acts only while the
     tile's drawn phase is Cooling and `ReadyAtMs > 0`; fill `= TotalMs` finite and above 0 `? clamp01((ReadyAtMs - nowMs)
     / TotalMs) : 1` (the rule `AbilityFaces.Spell` and `AbilityFaces.Wish` both have for an unknown total,
     `client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs:36-77`). At or past `ReadyAtMs` it leaves the wedge on at fill 0;
     the next `Show` of a Ready face switches it off. `TickActionRow` calls it for `_wishAbilityTile` and both
     `_questTiles`: R, Shift and Q are treated alike, from the values `Cooldown(ReadyAt, RechargeMs)` stored.
   - `AbilityTile.CastAt(double nowMs, double startedMs, double landsMs)` (new, public) writes the Wish cast bar (`CastBar`,
     child `Cast`) fill `clamp01((now - started) / max(1, lands - started))` (the rule of `AbilityFaces.Wish`), only while
     the bar is on, only when it differs, never `_castFill`. `TickActionRow` reads `_session.Me?.Wish` live and calls it
     with the Wish's `Cast.StartedAt` and `Cast.LandsAt`; it writes nothing when the session, `Me`, the Wish or its `Cast`
     is null.
   - `RefreshWishTile(wish, serverTimeMs, theme)` (signature kept) calls `Show` and `Cooldown` as today, then `SweepAt` and
     `CastAt` at `ActionNowMs(serverTimeMs)` = `_hudClock.Ready ? _hudClock.Now(HudLocalMs()) : serverTimeMs`; a dirty frame
     with no new snapshot therefore leaves both where `TickActionRow` put them, and a caller with no session
     (`RefreshOwnerPresentation(me, ms)` on a bare `MatchHud`, no clock) draws from the time it passes. The tile's texts,
     phase and frame stay on the snapshot time.
   - The Wish cast bar already has its sprite (WISH-TILE); this packet gives a sprite to the attack bars only.
4. **The attack timer is a bar on the weapon in hand.** The radial `CooldownSweep` of the two slots and `_slotSweeps` go.
   Each slot gets a child `AttackBar` where the sweep was built (`_attackBars`, Image[2], built as `AbilityTile` builds
   `Cast`, `UI/AbilityTile.cs:59-63`: a box with an Image, `UiSprites.Flat()`, `theme.Accent`, no raycast, Filled,
   Horizontal, `fillOrigin` 0, fill 0, off): 3 high, 4 above the slot's bottom, inset 6 left and right. Only the active
   slot's bar can show (`_inventoryShown.ActiveSlot`, re-read every frame; slot 0 with no inventory).
   `RefreshCooldownSweep(SelfView me, double serverTimeMs)` (its call in `RefreshLegacyHud` passes the time) sets
   `_attackReadyAtMs = serverTimeMs + me.ReadyInMs`; the length `_attackCooldownTotal` is the largest `ReadyInMs` seen
   since it was last 0 (today's rule, `MatchHud.ActionBar.cs:219-220`); it then calls
   `WriteAttackBars(ActionNowMs(serverTimeMs))`. `TickActionRow` calls `WriteAttackBars(now)` every frame: `fill =
   clamp01((_attackReadyAtMs - now) / max(1, length))`, on while above 0 and only on the active slot's bar, the other off.
   Null-safe (`!= null`, never `?.`) on a `MatchHud` with no action bar built. No slot is darkened.
5. **One owner of pulses, about the centre.** `_pulses` becomes `readonly HudPulses _pulses = new HudPulses();` and the old
   list of `(RectTransform, float)` tuples is deleted. `Pulse(rect)` is `_pulses.Play(rect, HudSeconds)`, `TickPulses()` is
   `_pulses.Tick(HudSeconds)`, `ClearPulses()` is `_pulses.Clear()`; the names stay as thin members, and `PulseSeconds`,
   `PulsePop` stay as one `const float` declaration set from `HudPulses.Seconds` (.15) and `HudPulses.Pop` (.08), so the
   existing cases of `Tools/hud-seams.test.cjs` (lines 51-58 pin `InputCue`, `Pulse`, `TickPulses`, `ClearPulses`,
   `_pulses = new` and the two constants in `MatchHud.Pulses.cs`) need no change. The `ShowcaseSettings.Current
   .ReducedMotion` read in `MatchHud.Pulses.cs` is deleted: `UiMotion.Reduced` is the one source (same expression:
   `ReducedOverride ?? (Current != null && Current.ReducedMotion)`). `HudPulses` (`UI/UiMotion.cs`, class body only)
   follows a rect that something else re-places during a pulse: each entry gets `Vector2 Last`, the position the pulse
   last wrote (`Play` sets it to the rect's position; `Tick` stores the entry back after a write). In `Tick`, first: when
   `anchoredPosition` is more than .001 from `Last`, something else placed the rect, and that position becomes `Origin` and
   `Last`. `Restore` (end of a pulse, `Clear`, reduced motion) writes `Origin` only while the rect is within .001 of
   `Last`, and always resets the scale. With nothing else moving a rect the behaviour is today's.
6. **The Wish tile has one scale writer.** `TickWishTileMotion` keeps the colour wash (alpha `clamp01(.6 * flash + .4 *
   pulse)`, faction colour), reads `flash` and `pulse` from `_wishMotion` at `HudSeconds`, and writes no `localScale`.
   Before its alpha early-return it calls `Pulse(_wishTile)` once when `flash` is above the previous frame's by more than
   .3 or the ready pulse goes from 0 to above 0 (two previous-value floats, reset by `ResetActionClock`).
   `RefreshWishTile` runs after `TickActionRow` in the frame: the observing frame sees nothing, the next one pops; a ready
   pulse reads 0 at its first instant (`WishTileMotion.ReadyPulse`, `Presentation/WishFxModel.cs:256-260`: `sin(a*pi)`).
7. **Reduced motion** (`UiMotion.Reduced`): wedges and bars still move, washes stay, nothing scales (`Play` does nothing;
   `TickWishTileMotion` no longer reads the setting).
8. **The minimap clearance comment.** The doc comment of `HudLayout.MinimapBarClearance` (`UI/HudLayout.cs:35-36`) says
   that 142 is legacy's minimap rule (half of legacy's 284 wide bar, plus the 10 unit gap), that today's row is
   `HudRegions.AbilityWidth` (458) wide and `HudRegions.AbilityRowScale` shrinks it, and that the row yields to the
   minimap, whose size the owner wants kept. No value changes and the capture tools are untouched. A comment has no test:
   the reviewer reads it in the diff.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AbilityMotionEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityMotionEditModeTests.cs.meta`
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` (ONE case added: test 16; an addition, no amended expectation)
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs` (`oracle-amend:` commit, one method; see below)
- `docs/backlog/packets/P-UI-ABILITY-MOTION-1.md`

### Amended expectation, for the owner (own `oracle-amend:` commit)

`AbilityRowEditModeTests.ASwapWithoutASlotPulsesBothSlotsAndNeverTheBar` (layout rig, no session) cast `_pulses` to a list of
tuples, called `TickPulses`, wrote `Start` back and read `PulseSeconds` through the helper `MatchHudPulseSeconds`. New form:
`double local = 5000; _layoutHud.HudLocalMs = () => local;` and `var pulses = GetF(hud, "_pulses") as HudPulses`; `Swap(1)`
gives `pulses.Count == 1`; at +75 ms `TickActionRow` leaves `Slot2` above scale 1 and `Slot1` at 1; `ClearPulses` gives 0;
`Swap()` gives 2 and at +75 ms both slots' `localScale.x` are above 1 with `CombatActions.localScale` unchanged. The identity
assertions (`Rect` SameAs, names) go because `HudPulses` exposes only `Count`; the scale checks on the named slots carry the
same fact. The helper `MatchHudPulseSeconds` goes with it. The unchanged-expectation reason: the old list no longer exists.

## Existing tests that pin this code (each verified by reading)

| Test | What it asserts | Stays green? |
|---|---|---|
| `AbilityRowEditModeTests.ASwapWithoutASlotPulsesBothSlotsAndNeverTheBar` (`:398`) | `_pulses` is a `List<(RectTransform, float)>`, entries by identity, `TickPulses` at half `PulseSeconds` scales both slots >1, bar scale unchanged | NO: amended (above) |
| `AbilityRowEditModeTests.ADeliveredQuestSpellShowsNameChargesAndCountdown` | quest tile wedge on, fill about 1 at first sight, about .5 at half span, off when Ready; read after `Frame()` (`LateUpdate` + `TickPlayerFlows`) with no local-time advance, so the clock adds 0 ms | yes |
| `AbilityRowEditModeTests.AnEmptySpellTileIsADimOutline` | an empty tile has its wedge off | yes |
| `AbilityRowEditModeTests.AnUnchangedTileAllocatesNothing`, `ASteadyQuestRefreshFrameAllocatesNothing` | `AbilityTile.Show` / `RefreshQuestRewardHud` unchanged allocate nothing (`AllocationMeter.AssertNone`) | yes |
| `AbilityRowEditModeTests.TheRowHoldsSixElementsLeftToRight`, `NothingFloatsAboveTheRow`, `TheRowShrinksOnANarrowStage` | row rects from `HudRegions`; `CombatActions.localScale` is 1 wide and below 1 narrow | yes |
| `WishTileEditModeTests` (bare `MatchHud`, `RefreshOwnerPresentation(me, serverMs)`) | Wish tile texts, one-class boxes (`Key Name Seconds CooldownSweep Charges Cast` identical to the quest tiles), cast bar fills `.25` then `.5` from the time passed, off with no cast, `AnUnchangedWishRefreshAllocatesNothing` (`RefreshWishTile`) | yes: no session means no clock, so the time passed is drawn |
| `MatchHudWishBarrierEditModeTests`, `MatchHudIconPresentationEditModeTests`, `MatchHudStatusNotesEditModeTests`, `MatchHudStatusTextsEditModeTests`, `OwnerVitalEaseEditModeTests`, `HudTextScaleEditModeTests` | call `RefreshOwnerPresentation` / the layout fixtures on a bare `MatchHud` | yes |
| `UiMotionEditModeTests` (`APulseScalesAboutTheCentreWhateverThePivot`, `ARestartedPulseRestartsTheClockAndKeepsTheFirstOriginal`, `ReducedMotionPulseDoesNotScale`, `ClearRestoresEveryRect`, `ADestroyedRectIsDroppedAndNullIsIgnored`, `AnIdleTickAllocatesNothing`) | `HudPulses` alone: centred scale, restart keeps first origin, reduced does nothing, `Clear` restores, destroyed rect dropped, idle tick allocates nothing | yes: `Last` follows `Origin` when nothing else moves the rect |
| `Tools/hud-seams.test.cjs` (cases at lines 51-58 and the end of the file) | `MatchHud.Pulses.cs` declares `InputCue`, `Pulse`, `TickPulses`, `ClearPulses`, `_pulses = new`, consts `PulseSeconds` and `PulsePop`; no method name in two `MatchHud*.cs`; `MatchHud.Legacy.cs` < 420 lines, `MatchHud.cs` < 600 | yes |

No existing test mentions `_slotSweeps`, `RefreshCooldownSweep(` with an argument, or `PulsePop` (searched
`Assets/Tests` and `Tools/*.cjs`).

## Oracle: what the tests are (EditMode `AbilityMotionEditModeTests`)

Rig copied from `AbilityRowEditModeTests` (`BuildFullRig`, `Snap`, `Items`, `Frame`, `LayoutFull`, `Quests`, `Rect4`,
`Measure`, `Child`, `Call`, `GetF`, `Transport`, `TearDown` plus `UiMotion.ReducedOverride = null`). A `Snap` variant sets
`WireSelf.Wish` (a `WireWishOwner`: Ability 3, MaxCharges 1, valid per `WishWire.ValidateOwner`) and `ReadyInMs`. Time:
`_hud.HudLocalMs = () => _local` right after the rig; a time-stepping test calls `Frame()` once at the snapshot's local time
before it moves `_local`. SetUp sets `UiMotion.ReducedOverride = false`, TearDown null. The pulse list is read as
`GetF(_hud, "_pulses") as HudPulses` with `Assert.That(pulses, Is.Not.Null)`.

1. `TheWishWedgeMovesBetweenSnapshots`: Wish cooling (Charges 0, ready 5 s after the server time, recharge 10 s): fill about
   .5; +100 ms with no new snapshot about .49; it never rises; +400 ms it has stopped 150 ms past the snapshot (about .485).
   `RechargeMs` 0 with `ReadyAt` ahead leaves the wedge on at fill 1 and it stays 1 100 ms later.
2. `TheCastBarMovesBetweenSnapshots`: a ready Wish with a 1 s cast started with the snapshot: bar on (Filled, Horizontal,
   sprite non-null: guard); +100 ms fill about .1; a snapshot with no cast turns it off.
3. `AQuestSpellWedgeMovesBetweenSnapshots`: `QuestSpell1` cooling 5 s of 5 s: +100 ms wedge about .98; frames at +0, +40, +80
   ms: on each time, never rises. A spell ready 100 ms after its snapshot, 150 ms later: wedge on at fill 0; a snapshot past
   `ReadyAt` switches it off.
4. `TheAttackBarIsOnTheWeaponInHandOnly`: `ReadyInMs` 400, slot 0 active: `Slot1/AttackBar` on, fill about 1, `Slot2/AttackBar`
   off, no `CooldownSweep` under either slot; with slot 1 active the bars swap. A bar has a sprite, Filled, Horizontal,
   `theme.Accent`, `raycastTarget` false; against its slot it is 3 high, 4 above the bottom, inset 6 left and right.
5. `TheAttackBarDrainsBetweenSnapshots`: +100 ms fill about .75; a snapshot with `ReadyInMs` 0: off.
6. `APressPopsAboutTheCentre`: Wish tile, `QuestSpell1`, the potion button, `Slot1`: after the cue the stage-space centre stays
   within .01 on every 10 ms step of the next .15 s, the scale exceeds 1 at half time, and after .16 s the scale is exactly 1
   and `anchoredPosition` equals its value before the cue (within .001).
7. `APulseLeavesARePlacedPillWhereItWasPlaced`: the `Pickup` cue pulses `CombatActions/Pickup`; `TickActionRow` at +75 ms; then
   `anchoredPosition += (112, 0)` by a direct write; `TickActionRow` at +160 ms: the pill is at the written position (within
   .001), scale exactly 1, `pulses.Count` 0.
8. `TheWishTileHasOneScaleWriter`: an accepted cast (Wish ready, then Charges 0 and `ReadyAt` 10 s ahead, a `Frame()` after
   each) and a Wish press right after the observing `Frame()`: `localScale.x` never exceeds `1 + HudPulses.Pop + .001` on any
   10 ms `Frame()` of the next .15 s; `_wishGlow` on.
9. `AnAcceptedCastPopsTheWishTileOnce`: after the accepting `Frame()` and one more at the same local, `pulses.Count` is 1; .16 s
   later it is 0 and stays 0 while `_wishGlow` is enabled and the wash fades.
10. `TheReadyPulsePopsTheWishTileOnce`: Wish cooling, then ready (Charges 1, `ReadyAt` not ahead): `Frame()`, then +50 ms
    `Frame()`: `pulses.Count` is 1; .16 s later 0.
11. `ReducedMotionKeepsTheWashAndTheWedgesAndDropsThePop`: `ReducedOverride = true`, the sequence of 8 with `ReadyAt` 5 s ahead:
    wash on (`_wishGlow` enabled, alpha above 0), the wedge still moves (about .49 after 100 ms), `pulses.Count` stays 0, the
    Wish tile's scale is exactly 1 on every frame, press included.
12. `EveryPressPulseGoesThroughTheOneHudPulses`: `_pulses` has type `HudPulses`; no `MatchHud` field is a `List` of
    `ValueTuple<RectTransform, float>`; the consts `PulseSeconds` and `PulsePop` equal `HudPulses.Seconds` and `HudPulses.Pop`.
    With `ReducedOverride = true` the five cues (Wish, `QuestSpell1`, potion, `Swap(0)`, `Pickup`) leave `Count` 0; with `false`
    they leave 5 and `TickActionRow` at +160 ms leaves 0.
13. `ASteadyActionRowTickAllocatesNothing`: a cooling Wish with a cast, a cooling quest spell and an attack timer; one `Frame()`
    (the three wedges and `Slot1/AttackBar` are on), `_local` fixed, `AllocationMeter.AssertNone(tick, 20, ...)` on
    `TickActionRow` through a delegate built once.
14. `ANewSessionRestartsTheClock`: a Wish cooling at server 50000, +150 ms, `Frame()`; a second `Session` (own `Transport`,
    Welcome, first snapshot at 50100 with a Wish ready at 55100, recharge 10000), `_hud.Bind(second)`; after one `Frame()` at the
    same local the wedge is .5 (not .495), after a second still .5; +100 ms it is .49.
15. `ADirtyFrameWithoutASnapshotKeepsTheFill`: Wish cooling with a cast, `Frame()`, +100 ms, `Frame()` (wedge about .49, bar about
    .1), then `_dirty` set true by reflection and `Frame()` at the same local: both read what the previous frame left.
16. Node case in `Tools/hud-seams.test.cjs`: `MatchHud.Pulses.cs` does not contain `ShowcaseSettings` and does not declare a
    list of tuples (`/List<\s*\(/`), through the file's `code` helper (comments never match).

### Notes from the oracle author

(written after the runs: see the end of this file)

## 9. Validation commands

- `bash /Users/nevinmckinnon/Documents/Projects/RogueHalla/.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-y AbilityMotionEditModeTests,AbilityRowEditModeTests,WishTileEditModeTests,UiMotionEditModeTests,MatchHudWishBarrierEditModeTests,MatchHudIconPresentationEditModeTests`
- `.../bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-y node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `.../bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-y scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-MOTION-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Presentation only, one HUD partial family, no wire, no `crates/`. What makes it fiddly: two
writers of one fill (`Show` and `SweepAt`/`CastAt`), the order of the frame (`TickPlayerFlows` before `TickActionRow`,
`RefreshWishTile` after it), and the `HudPulses` re-placement rule.

## 11. Known traps

- `HudClock.Now` never goes down until `Reset`; a snapshot behind the extrapolated time holds the value until the new base
  passes it. `TickPlayerFlows` (the quest tiles' `Show`) runs before `TickActionRow` in `HudLateUpdate`, `RefreshWishTile`
  after it: the Wish tile re-applies `SweepAt` / `CastAt` after its own `Show`.
- `AbilityTile.Show` caches `_sweepFill` and `_castFill` and writes only on a change (`AbilityTile.cs:104-106,121-123`):
  `SweepAt` and `CastAt` must not update them, and must not toggle `enabled`, or `Show` and the tick flicker.
- `CombatActions` may be scaled on a narrow stage: never write its `localScale`.
- Time in EditMode does not advance: every time the row reads goes through `HudLocalMs`; tests drive it.
- A member a test reads or sets must be `public` (no `InternalsVisibleTo`): `HudLocalMs` is a public field.
- Unity objects: `!= null`, never `?.`.
- Not established by the tests: the look (the attack bar's 3 unit band lies within 1 unit of the `Scaling` label box; the 8
  percent centred pop; the motion) is judged in the lane's next Web round; `WishTileMotion.Observe` calls the Wish ready only
  with a charge and the ready time passed, while the tile reads Ready as soon as the ready time has passed, so the ready pulse
  and the tile can differ for one snapshot; the tile's phase, texts and frame still change at snapshot rate (the wedge can
  reach 0 up to 150 ms before the tile reads Ready); the attack bar length is today's rule (largest `ReadyInMs` since 0), so
  after a weapon swap during continuous attacking the new bar can start partly full; frame cost of the new tick is not measured.

## Notes from previous attempts
