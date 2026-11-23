# P-UI-TILE-PLATES-1: every action-row tile stands on one solid plate; an empty tile is dim, not see-through

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the UI rework lane ("ability and spell hub needs
  optimizing"); seen in lane Web round 1 (2026-11-23): the empty weapon slots and the empty Shift and Q tiles are
  see-through, world sprites and a trader's name plate show through them, and on bright ground they almost vanish; and a
  minor of the coordinator's merge review of wave 2 (empty quest tiles looked like focusable buttons).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (wave 3 of the lane on integration 86, with
  P-UI-CONTROLS-LINE-1 and P-UI-LEFT-EDGE-1 merged into it). Round 2 of wave 4: it shares `MatchHud.ActionBar.cs` with
  CONTROLS-LINE and `MatchHud.Legacy.cs` / `MatchHud.Features.cs` with LEFT-EDGE; it starts after both are merged. The
  oracle was written on `base-3` at bf3574e47; the implementer re-reads `CreateWeaponBar`, `RefreshBuildIcons` and
  `MatchHud.Legacy.cs` on the tree it gets and finds members by name, not by line.
- **D-rules touched:** D1: legacy is see-through when empty too (`packages/client/src/ui/weaponBar.css:47`
  `.wb-slot.empty { opacity: 0.5 }` on a `rgba(20, 24, 30, 0.9)` plate (line 43); `packages/client/src/ui/abilitySlot.css:44`
  `.as-slot.empty { opacity: 0.5 }`), over a dark scene; the quest tiles have no legacy counterpart. Solid plates are an
  intended difference: the hand-off proposes ONE row, `proposed DEV-<n>`: legacy fades a whole empty slot to half opacity
  (the two lines above), Unity draws a solid `SurfaceRaised` plate and dims the content instead. The owner signs it and
  the coordinator numbers it; the implementer never edits `DEVIATIONS.md`. D2 n/a. D5/D6 none. D9 untouched: presentation
  only, no wire, no game rule.

## 1. ID and outcome

The six elements of the action row (`CombatActions`, built by `MatchHud.CreateWeaponBar`,
`Assets/Scripts/UI/MatchHud.ActionBar.cs:96`): Slot1, QSwap, Slot2, WishTile, QuestSpell1, QuestSpell2. The row is drawn by
the HUD's ScreenSpaceOverlay canvas, over every world sprite and label. After this packet each element stands on one
fully opaque `SurfaceRaised` plate in every state, and an empty one is told apart by its content (dim key cap, dim name
text, no icon), never by see-through fill.

### What is wrong today (re-read on the base)

- **Weapon slots** (`Slot1`, `Slot2`): `CreateWeaponBar` adds a `CanvasGroup` to each (`MatchHud.ActionBar.cs:111`,
  `_slotGroups`, declared `MatchHud.Legacy.cs:32`); `RefreshBuildIcons` (`MatchHud.Features.cs:404`) sets its alpha to .5
  for a slot without a weapon (line 418 with inventory, line 430 without). It halves fill, shadow, gloss, frame and the
  "Empty" text.
- **Ability tiles** (`WishTile`, `QuestSpell1`, `QuestSpell2`, all `AbilityTile`, `Assets/Scripts/UI/AbilityTile.cs`): on a
  phase change `AbilityTile.Apply` (line ~88, block `first || phase != _phase`) writes `With(theme.Surface, .35f)` as
  the Empty surface (`_surface.color`), `With(theme.Border, .5f)` as the frame and `With(theme.TextSecondary, .6f)` as the
  key cap. The Shadow follows the graphic alpha.
- The swap chip (`QSwap`) has neither: it is the opaque slate. So the row has two kinds of plate.
- On mid-tone ground (Web grass, luminance .14) the empty key cap reads 2.65:1 and the frame 1.48:1.
- Stale: the `else` branch of `RefreshBuildIcons` (no inventory, after a rebind) resets icon, name, ring and group but not
  the Button colour block, so a slot that was in hand keeps `SurfaceSelected`. Half faded, nobody saw it; on a solid
  plate it would show.
- The wave 2 minor does not describe the base for an empty tile: `AbilityTile.Apply` already sets
  `_button.interactable = !empty`, and `UiFocusNavigation.Cycle` (`UI/UiFocusNavigation.cs:11`) skips controls that are not
  interactable. An empty tile is already not a Tab stop and not clickable; this packet pins that with a test and changes no
  code for it. A filled tile is a Tab stop whose click and Enter do nothing but show its hint (known, recorded, not
  fixed).

### Outcome (HUD stage units; colours only from `UiTheme.Current`; nothing moves, reduced motion has no part)

1. **One plate.** The plate of each of the six elements is drawn fully opaque in `theme.SurfaceRaised` in every state, the
   held slot excepted (item 2). Drawn colour = `Image.color`; for a ColorTint Button (slots, chip) times the colour
   block's `normalColor` and `colorMultiplier`; times every `CanvasGroup.alpha` from the image up to and including
   `CombatActions`. Alpha is 1, rgb `SurfaceRaised`. In `AbilityTile.Apply` the `_surface.color` write is deleted (the
   constructor's `SurfaceRaised` is the only value). Gloss (white .16) and Shadow stay as built. Hover keeps its tint on
   the three Button-backed elements (a click affordance), an empty weapon slot included; the tiles have none
   (`Transition.None`).
2. **The held slot keeps its surface.** A slot with a weapon and `ActiveSlot` equal to its index has `normalColor`
   `SurfaceSelected` (alpha 1) and the `ActiveWeapon` ring, as today. The `else` branch of `RefreshBuildIcons` resets each
   slot with `HudUi.ApplyColors(_slots[i], theme.SurfaceRaised)` (guarded `_slots[i] != null`, as above it).
3. **No fade mechanism.** The weapon slots' `CanvasGroup` is not built; `_slotGroups` and its two alpha writes are
   deleted. No `CanvasGroup` exists under `Slot1` or `Slot2`; none under `CombatActions` has alpha below 1.
4. **An empty element is told apart by its content, nothing else.** No frame is dimmed: an ability tile's frame is
   `theme.Border` (alpha 1) when Empty or Cooling and `theme.BorderStrong` when Ready (Casting counts as Ready); the
   slots' and chip's frame stays `Border`. Empty and Cooling therefore share plate and frame; accepted, judged in the
   next Web round. `AbilityTile` gains one place for the numbers (not dials: `docs/unity/DIALS.md` lists no HUD alpha):
   `public const float EmptyKeyAlpha = .7f; public const float EmptyTextAlpha = .8f;` and
   `public static Color EmptyTextColour(UiTheme theme)` = `UiTheme.With(theme.TextSecondary, EmptyTextAlpha)` (the stub
   commit already declares all three, `EmptyTextColour` returning `Color.clear`: the implementer fills it in).
   - Ability tile key cap (`Key/Label`): Empty = `With(theme.TextSecondary, EmptyKeyAlpha)`, else `TextSecondary`.
   - Ability tile name (`Name/Label`; the Wish tile's Empty dash is its only text, the quest tiles' box is off when
     Empty): Empty = `EmptyTextColour`, else `theme.TextPrimary`. Both written in the existing `first || phase !=
     _phase` block of `Apply`.
   - Weapon slot name (`Slot{n}/Name/Label`, the label in `_slotNames[i]`): no weapon = `EmptyTextColour`, a weapon =
     `TextPrimary`, written in both branches of `RefreshBuildIcons` beside the `Set(_slotNames[i], ...)` lines, only when
     it differs (`if (label.color != c) label.color = c;`). `CreateWeaponBar` builds the label already dim (it is born
     "Empty" and the layout rigs never run `RefreshBuildIcons`).
   - Unchanged and not part of this: the Wish icon (alpha .3 unless Ready), the absent name, seconds, badge and wedge, the
     disabled weapon icon.
5. **An empty tile is not a Tab stop and not clickable; a filled tile is a Tab stop that only shows its hint; both hint
   on hover.** Mechanism: the Button's `interactable` (`!empty`, already so), which `Selectable` navigation and
   `UiFocusNavigation.Cycle` honour. The Image keeps `raycastTarget` true and `HudIconHint` (separate handlers on the same
   object) is untouched, so hover shows the hint of an empty tile as of a filled one. A click on an empty tile still
   stops at the HUD (a raycast hit) and does nothing. No navigation mode is written. No code change; test only.
6. **Contrast** (sRGB blend, the worst case; a linear blend is higher). From `node tools/art/palette-check.cjs`, section
   6: `ui.border` on `ui.raised` 3.70, `ui.textSecondary` on it 7.77, `ui.textPrimary` on it 12.53. Computed with the same
   WCAG formula on `UiPaletteTokens`: key cap `TextSecondary` at .7 on `SurfaceRaised` 4.60 (at .6 it was 3.78); "Empty"
   text at .8: 5.54 on `SurfaceRaised`, 4.63 on `SurfaceHover` (a hovered slot); `BorderStrong` on `SurfaceRaised`
   10.12. **Below 3:1:** `Border` on `SurfaceHover` 2.98 (a hovered slot or chip, transient) and `Border` on
   `SurfaceSelected` 2.87 (the held slot's inner ring; the gold `ActiveWeapon` ring over it is 7.86). Both exist today and
   stay. Plate against ground is not computed and not claimed (about 1.9:1 on ground of luminance .09, 2.65:1 on the Web
   grass .14, below 3:1, as the R tile does today).
7. **Nothing is rewritten in a steady frame.** The tile's look is written inside its phase-change block (`Show` with an
   unchanged face writes nothing and allocates nothing). The slots' look is written by `RefreshBuildIcons`, which runs on
   collections and session-state events (`RefreshCollections`, `MatchHud.cs:414`) and on `Bind`; no per-frame method
   (`HudLateUpdate` `MatchHud.cs:242`, `TickActionRow` `MatchHud.ActionBar.cs:72`, `TickPlayerFlows`
   `MatchHud.PlayerFlows.cs:154`) writes a plate, frame or label colour.

## 2. Dependencies

P-UI-CONTROLS-LINE-1 and P-UI-LEFT-EDGE-1 merged into the lane base (see Integration base). `AllocationMeter`
(`Assets/Tests/Editor/AllocationMeter.cs`) is on the base. Nothing else.

## 3. Relevant legacy paths

- `packages/client/src/ui/weaponBar.css:43` `.wb-slot` background `rgba(20, 24, 30, 0.9)`; `:47`
  `.wb-slot.empty { opacity: 0.5; }`; `:63` the empty swatch border dashed.
- `packages/client/src/ui/abilitySlot.css:44` `.as-slot.empty { opacity: 0.5; }`; `:45` the empty slot hides its icon.
- Verified with `grep -n empty` on both files. Legacy fades the whole empty slot; the intended difference is in D1 above.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/TilePlatesEditModeTests.cs` (+ `.meta`), the oracle.

## 5. Exclusive write scope

Under `client-unity/UnityProject/Assets/` (`UI/` is `Scripts/UI/`). Find members by name, not by line.
- `UI/AbilityTile.cs`: the colour block of `Apply`, the three members of item 4 (the stub declares them), the type's doc
  comment.
- `UI/MatchHud.ActionBar.cs`: `CreateWeaponBar` only (the `CanvasGroup` line; the Name label's built colour).
- `UI/MatchHud.Features.cs`: `RefreshBuildIcons` only. `UI/MatchHud.Legacy.cs`: the `_slotGroups` declaration only.
- No dotnet test, no library file: product code gains no arithmetic.
- `Tools/*` is unchanged. `Tools/hud-seams.test.cjs` pins `CreateWeaponBar` in `MatchHud.ActionBar.cs` and file-length
  caps: run `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` once through the wrapper after the edits;
  expected green.
- Not in scope: `UI/MatchHud.cs`, `.IconPresentation.cs` (slot texts only), `.QuestRewards.cs`, `.Pulses.cs`,
  `UiMotion.cs`, `HudUi.cs`, `HudIconHint.cs`, `UiFocusNavigation.cs`, the consumables (potion button, Wind chip at alpha
  .94, pickup pill), `HudTextScaleEditModeTests.cs`, `CardDisplayCatalog.cs`, `BakedContent.cs`,
  `Presentation/SpriteActorPresenter*.cs`, `docs/DEVIATIONS.md`.

## 6. Contracts and references

- R-AUTH / D9: presentation only; no game rule, no wire. Colours only from `UiTheme.Current` (the existing test
  `AbilityRowEditModeTests.EveryTileGraphicUsesAThemeColour`, `UiTheme.Owns` ignores alpha, stays green).
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (readability gate: a new non-test file needs a `<summary>`;
  nothing here adds a non-test file).
- Evidence: `.agent-runs/lane-ui-rework/understand/web1-empty-tiles.txt` (Option A with its CHECK corrections).

## 7. Behaviour to preserve

- Hover tint on the three Button-backed elements; the held slot's `SurfaceSelected` and gold `ActiveWeapon` ring.
- The Wish icon alpha (.3 unless Ready), absent name, seconds, badge, wedge, disabled weapon icon.
- `AbilityTile.Apply` writes colours only in `first || phase != _phase`; an unchanged `Show` allocates nothing.
- `Cooldown`, `SweepAt`, `CastAt` and the attack bars are untouched.
- Empty tile: `Button.interactable` false, `Image.raycastTarget` true, hint on hover (item 5).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TILE-PLATES-1 --base lane/ui-rework/base-3` passes.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green.
- The existing tests below stay green (the three amended expectations excepted).

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/TilePlatesEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TilePlatesEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/WishTileEditModeTests.cs` (one added test)
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs` (two amended expectations, one rename)
- `docs/backlog/packets/P-UI-TILE-PLATES-1.md`

(The `stub:` commit's edit of `UI/AbilityTile.cs` is not an oracle file: the implementer replaces it.)

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TilePlatesEditModeTests,AbilityRowEditModeTests,WishTileEditModeTests`
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (through `bin/in.sh`).
- Also keep green: `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`, `MatchHudWishBarrierEditModeTests`,
  `MatchHudIconPresentationEditModeTests`, `AbilityMotionEditModeTests`.

## 10. Risk and suggested model tier

implementer-sonnet: three small edits in presentation code and one new test class already written. The only trap is the
`first || phase != _phase` block and the `_slotNames` label choice (section 11).

## 11. Known traps

- `MatchHudWishBarrierEditModeTests` builds weapon icons with no name labels or slots: keep the `!= null` guards on
  `_slotNames[i]` and `_slots[i]` in `RefreshBuildIcons`.
- `MatchHud.IconPresentation.cs:100` and `Bind` write TEXT into the first label under a slot
  (`_slots[i].GetComponentInChildren<TMP_Text>(true)`), which is `Name/Label`; the next `RefreshBuildIcons` rewrites it.
  Pick the label by `_slotNames[i]`, never by `GetComponentInChildren`. Those writes never touch the colour.
- `normalColor` after `HudUi.ApplyColors` is the plate colour; the tween does not run in EditMode, so never read the
  `CanvasRenderer`.
- `AbilityTile.Apply` writes its colours only in `first || phase != _phase`: a colour moved out of the block is rewritten
  on every `Show`. A tile going Ready to Empty must get the dim key cap and name back (test 2 walks it).
- Keep `_surface`: it is the Button's `targetGraphic`. Do not rebuild a plate through `HudUi.Surface` (it recreates Gloss
  and Frame).
- `MatchHud.ActionBar.cs` and `RefreshBuildIcons` are shared with CONTROLS-LINE, LEFT-EDGE and later packets: edit only
  the hunks named here.

## Existing tests that pin this code

Verified by reading each. Paths under `client-unity/UnityProject/Assets/Tests/Editor/`.

| Test | Asserts | Fate |
|---|---|---|
| `AbilityRowEditModeTests.AnEmptySpellTileIsADimOutline` (line 293) | Empty quest tiles: name/seconds/badge empty, Button not interactable, wedge off, **line 305 `Surface(tile).color.a < .5`** | Amended: line 305 becomes `Is.EqualTo((Color)UiTheme.Current.SurfaceRaised)`, test renamed `AnEmptySpellTileIsASolidPlateWithADimKeyCap`. Own `oracle-amend:` commit. |
| `AbilityRowEditModeTests.ADeliveredQuestSpellShowsNameChargesAndCountdown` (line 315) | Cooling/Ready faces of Shift; **line 347 `Surface(two).color.a < .5`** for the Q tile that stays empty; Ready surface equals `SurfaceRaised` (line 342) | Amended: line 347 becomes `Is.EqualTo((Color)theme.SurfaceRaised)`. Own `oracle-amend:` commit. |
| `WishTileEditModeTests.NoWishShowsADash` (line 201) via `AssertNoWishLook` (line 189) | Dash, no seconds/badge/wedge/cast bar, Button not interactable, **line 196 `Surface(tile).color.a < .5`** | Amended: line 196 becomes `Is.EqualTo((Color)UiTheme.Current.SurfaceRaised)`, message `when + ": the surface is the solid plate"`. Own `oracle-amend:` commit. |
| `AbilityRowEditModeTests.AnUnchangedTileAllocatesNothing` (454) | An unchanged `Show` allocates nothing | Stays green. |
| `AbilityRowEditModeTests.EveryTileGraphicUsesAThemeColour` (537) | Every tile graphic in Empty, Ready, Cooling is a theme colour (`UiTheme.Owns` ignores alpha) | Stays green. |
| `AbilityRowEditModeTests` others (row layout, shared prompt lane, equipped line, no digit cap, swap pulse, narrow stage, labels fit) | Layout and text, no colours | Stay green. |
| `WishTileEditModeTests.TheWishTileAndTheQuestTilesAreOneClass` (231) | Ready surfaces equal across the three tiles; frames `BorderStrong` when Ready | Stays green (Ready look unchanged). |
| `HudLegacyLayoutEditModeTests` (`AssertAllOwned`, slot rects) | Slot/tile rects; every HUD graphic is a theme colour | Stays green. |
| `MatchHudWishBarrierEditModeTests.WeaponIconsRetainSemanticTintAcrossSlotChangesAndClearMissingInventory` | Weapon icon tints across slot changes on a bare rig (`_slots`, `_slotNames` may be null) | Stays green: keep the null guards. |
| `HudTextScaleEditModeTests`, `MatchHudIconPresentationEditModeTests`, `AbilityMotionEditModeTests` | Text fit, slot texts, motion; none reads a plate alpha or a slot group | Stay green. |

Superseded history, not edited: `docs/backlog/packets/P-UI-ABILITY-ROW-1.md` lines 154 and 159 (the Empty row
`With(Surface,.35)` / `With(Border,.5)`, "key cap in Empty at alpha .6"). That file is history.

## Notes from the oracle author

Run on the stub (`unity-class.sh TilePlatesEditModeTests,WishTileEditModeTests`): 15 tests, 4 red, 11 green.

Red, each on an assertion about the missing behaviour:
- `TilePlatesEditModeTests.EverySlotAndTileStandsOnOnePlateInEveryState`: `a: as built / Slot1 plate alpha`, expected 1 but 0.5.
- `TilePlatesEditModeTests.AnEmptyTileIsToldApartByItsContentOnly`: first line `EmptyTextColour has the rgb of
  (178, 191, 206)` but the stub returns clear; behind it, key cap alpha .6 not .7 and names at `TextPrimary`.
- `TilePlatesEditModeTests.NoCanvasGroupFadesTheRow`: `a: as built: the group on Slot1 fades the row`, 0.5.
- `TilePlatesEditModeTests.AFrameDoesNotRewriteTheRowsLook`: its first half (frame leaves magenta alone) passes; the second half
  is red on `after RefreshBuildIcons the empty slot name has the rgb of ...` (the base writes no label colour). The key cap
  half (`Show(Ready)`) is green on the base, as the spec says.

Green on the stub by the lane lead's ruling (standing guards): `AnEmptyAbilityTileIsNotAControlAndStillHints`,
`AnUnchangedFaceAllocatesNothingInEveryPhase`, and `WishTileEditModeTests.TheBadgeFollowsTheDeliveredStockNotTheClock` (added in
the oracle commit, an addition and not an amended expectation).

Scratch run (not committed): a copy of `TilePlatesEditModeTests` in which a failed assertion is caught and collected, run once on the
stub. Every entry of every list was an assertion about the missing behaviour (alpha .35 / .5 plates and frames, rgb of the faded
plates, the stale `SurfaceSelected` on Slot2 in state f, key cap alpha .6, names at `TextPrimary`, the slot groups, the blended
contrast 3.78 under 4.5, an equal-not-lower slot text ratio); no exception from set-up, delivery or lookup; states d and e (the filled
ones) passed every line on the stub, so the later lines of each test were run. Tests 4 and 5 passed with no entry.

Design notes and deviations from the spec's wording:
- Test 4 hovers before it tabs, because a selection also shows a hint (`ISelectHandler`); the row is `activeInHierarchy` after `Frame()` in
  the rig, so nothing needed making visible.
- The slot name labels are checked by colour only, never by text: `MatchHud.IconPresentation.cs:100` writes a digit into the first label
  under a slot between refreshes (the trap in section 11).
- The spec's states are walked by one helper (`Walk`) shared by tests 1, 2 and 3 on one rig each; each state delivers its inventory
  through the session and asserts the delivered inventory (HasValue, both slots, `ActiveSlot`) before any look is read. State d and e
  also assert the held slot's `normalColor` is `SurfaceSelected`, which proves the refresh ran.
- Test 2's contrast block reads the colours drawn in states a and c, so it follows whatever the implementer writes, and asserts the
  spec's floors (key cap and empty slot text at least 4.5, below the filled one's; `Border` on the plate at least 3).
- Test 5 asserts the Button's `interactable` after the first `Show` so the face is known to be drawn before the allocation window.

For the owner (`oracle-amend:` commits, one each, wording in section "Existing tests that pin this code"):
`AbilityRowEditModeTests.AnEmptySpellTileIsADimOutline` renamed `AnEmptySpellTileIsASolidPlateWithADimKeyCap` (old name and new name for
the hand-off) with its surface line now `Is.EqualTo((Color)UiTheme.Current.SurfaceRaised)`;
`AbilityRowEditModeTests.ADeliveredQuestSpellShowsNameChargesAndCountdown` line 347 likewise;
`WishTileEditModeTests.NoWishShowsADash` via `AssertNoWishLook`, message `when + ": the surface is the solid plate"`.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
