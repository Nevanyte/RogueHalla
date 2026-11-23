# P-UI-PLATES-OPAQUE-1: no world name reads through a standing text plate of the match HUD: every such plate is opaque

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the UI rework lane ("Please do a UI optimization
  sweep", "make sure all in-game notifications are thoughtfully placed"). Seen in the lane's Web round 3a (2026-11-23): a
  creature's name and a camp badge read through the gains panel at the top left.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head c8e21c0e1 when branched: P-UI-FIRST-RUN-HINT-1,
  integration 87 and P-UI-ABILITY-FEEDBACK-1-fix have landed). Implementer worktree: `/Users/nevinmckinnon/Documents/Projects/RH-ui-y`.
- **D-rules touched:** D1: legacy's plates are translucent too (`packages/client/src/style.css:16` defines `--plate` at .58,
  used at `:297` for the plate and `:339` for the phase pill; `:461-462` banners .96; `:489` toasts .92;
  `ui/championCombatHud.css:14` .9; `ui/chrome.css:46` ping .7; `ui/terrainChips.css:7` chip .72), so D1 does not ask for
  translucency: it is a look, not behaviour. The hand-off proposes ONE row, `proposed DEV-<n>`: Unity's standing text
  plates are opaque so no world text reads through text, where legacy has a plate (.58 to .96); the tracker, the offer
  card and the spectate bar have none in legacy. The owner signs it; the implementer never edits `DEVIATIONS.md`.
  D2 n/a. D5/D6 none. D9 untouched: alpha values only, no rule, no wire.

Evidence (lane lead's files, not needed to implement): `.agent-runs/lane-ui-rework/understand/web3-panel-seethrough.txt`,
pictures `webround/round-3a/22-both-open.png`, `10-hud-spawn.png`, `56-prompt-two-actions.png`; model packet
`docs/backlog/packets/P-UI-TILE-PLATES-1.md` with `TilePlatesEditModeTests.cs` (its helper `Drawn`).

## 1. ID and outcome

The match HUD is one ScreenSpaceOverlay canvas (`MatchUI`, `MatchHud.Initialize`) composited after the camera, so what shows
through a plate is its fill alpha alone. Its text plates are filled at literals below 1. In round 3a a pale name ("Cinder
Hound") and a camp badge behind the top-left plate read through it: pixel (52,55,59) on a bare plate of (23,28,38), x 152-268,
y 59-73, so also inside the first gained-cell row (.8 cells over a .94 plate leak 1.2 percent). A white name under a .94 plate
is about 1.5:1 (raised) to 1.9:1, under .97 1.45, under .99 still 1.15: only alpha 1 is free of a ghost, and the brightest
thing behind a plate is a white name, so no alpha below 1 is safe. The action tiles and `HudUi.Button` are opaque already.

After this packet every standing text plate listed below is drawn with alpha 1 whenever it is shown and settled.

Fill alphas today (surface, node, the member that writes the literal; lines read on the base):
- Vitals plate `VitalsPanel` (`CreatePlate`, `MatchHud.Legacy.cs:97`): `Tone(Surface, .94)`. Champion panel
  `ChampionCombatHud` (`CreateDamageReadout`, `MatchHud.Legacy.cs:220`): .94, with a focus Button (normal tint white, hover
  `SurfaceHover` alpha 255).
- Ping pill `PerformancePill`, under `ChromeStage` (`CreateLegacyChrome`, `MatchHud.Legacy.cs:74`): `Tone(SurfaceRaised, .85)`.
  Banner pill `ConnectionBanner` .96 (`MatchHud.TopCentre.cs:21`) and phase pill `PhaseTimerPill` .94 (`:26`), both in
  `CreateTopAndToastPills`.
- Toast pills `EventLane/Toast<i>` and `FeedbackLane/Toast<i>` (`ToastLane` constructor, `ToastLane.cs:48`): `With(Surface, .97)`.
  The constructor builds capacity + 1 pooled pills per lane, shown or not, and deactivates each; `Present` activates only the
  pills that have a line, and each pill has a fade CanvasGroup (alpha 1 until `Present` sets it).
- Quest tracker `QuestTracker` (`QuestTrackerView` constructor, `QuestTrackerView.cs:37`): .82, frame ring
  `With(Border, .6)`, `HudFade`. Wind chip `CombatActions/Consumables/Wind` (`CreateConsumables`, `MatchHud.ActionBar.cs:189`):
  `Tone(SurfaceRaised, .94)`.
- Terrain chips `TerrainChips/Chip<n>` (`TerrainChipAt`, `MatchHud.Terrain.cs:60`) and the home-ground note backing
  `TerrainChips/Note` (`ShowTerrain`, `MatchHud.Terrain.cs:135`): raw (10,12,16) at .8, no sprite; the note toggles by
  `Image.enabled`.
- Quest offer card `QuestOffer` .96 (`QuestOfferCard.cs:45`); hunt card `SentencedHunt` .93 (`SentencedHuntPanel.Initialize`,
  `SentencedHuntPanel.cs:38`); spectate bar `SpectateBar` .94 (`CreateSpectateBar`, `MatchHud.Spectate.cs:24`).
- On the token alpha (`UiPaletteTokens.Surface` alpha 240 = .94): the chest reward panel `Rewards` (`CreateChestPanel`,
  `MatchHud.Chest.cs:29`: `MatchHud.Panel` (`MatchHud.cs:519`) fills `ShowcasePalette.Panel` = `theme.Surface`; over the world on
  the right while a chest is open); the tutorial step card `TutorialCard` (`TutorialView.Initialize`, `TutorialView.cs:41`,
  `theme.Surface`, no scrim; bottom left, in practice).
- Own canvas: `InspectorCard` (`InspectorCardView` constructor, `InspectorCardView.cs:29`, black .78; its overlay canvas comes
  from `ArenaView.CreateInspector`, sorting order -1; the hover card at the top right, over the world).

Nothing rewrites a plate's fill after it is built. Nearby writers are not plates: `RefreshLegacyHud` (the health fill), Button
colour blocks, `HudFade` and `CanvasGroup.alpha` (tracker, offer card, toast pills: motion), `Veil` (the phase pill, 0 or 1),
the note's `Image.enabled`, the chip Outline colour, the toast tone strip. The HUD ignores `UiTheme.Changed` (its one
subscriber is `TacticalMapSurface.Rebuild`): a recolour leaves every plate as built, and the alpha falls back only if a
builder reads the token's alpha again. The standing guard of the oracle pins that.

### The change

1. One constant in `UI/HudUi.cs`, read at every site below: `public const float PlateAlpha = 1f;`, the first member of
   `HudUi` (`public static class HudUi` is at `HudUi.cs:16`; `Point` follows at `:18`). Doc comment: not a dial (no value
   below 1 is safe); `docs/unity/DIALS.md` gets no row.
   ```csharp
   /// <summary>Fill alpha of a text plate over the world. Not a dial: below 1 a white name reads through.</summary>
   public const float PlateAlpha = 1f;
   ```
2. Each site writes `HudUi.PlateAlpha` where it wrote a literal: `Tone(theme.Surface, HudUi.PlateAlpha)`,
   `UiTheme.With(..., HudUi.PlateAlpha)`, `new Color(10f / 255f, 12f / 255f, 16f / 255f, HudUi.PlateAlpha)`. Token-alpha sites
   write `UiTheme.With(theme.Surface, HudUi.PlateAlpha)`: the tutorial step card in `TutorialView`, and the chest panel by
   setting the root `Image.color` after `Panel(...)` returns (one added line).
3. Nothing else changes: no rgb, no frame colour or alpha (the tracker's ring stays .6), no Gloss, radius, size, place, key
   or word. A Shadow follows the fill alpha, so a plate's shadow gets 6 percent denser; accepted. `MatchHud.Legacy.cs` gains
   and loses no line (`hud-seams.test.cjs` caps it under 420; it has 374).

OUT (do not touch): over a scrim: results card (`ModalBackdrop`), help card (`theme.Scrim` root), Tab map sheet and rails
(`TacticalMap/Scrim` .86/.84), reward choice and nearby quest actions (overlay black .68), accounts card (alpha 1). The theme
token (`docs/art/palette.json` `ui.panelAlpha`, `UiPaletteTokens.cs`). On a plate: gained cells (.8, `MatchHud.Legacy.cs:141`),
champion rows (.7, `:229`), chest tier chips and key caps, tone strips, every `HudUi.Button`, the six action tiles. No text:
health trough, fills and ghost, edge glows, the minimap frame. Never shown: `WishPanel`. Already opaque: the `HudIconHint`
tooltip. No plate (bare text): the native readouts strip and the lobby roster (`LobbyRoster`). A launch-menu card: the tutorial
welcome card (`TutorialWelcome`, same file as the tutorial card: leave its statement). The death line (`DeathLine`,
`CreateDeathLine`, .9) keeps its fill: its plate stands on the player's own fall, which the `DeathResultsDelay` of 1.5 s
before the results card exists to show, and legacy draws no plate there (`style.css:371-383`); a later packet may drop the
plate. A `CanvasGroup` fade is motion; the rule is about the settled fill. The home-ground note keeps its shape (a full-width
band toggled by `Image.enabled`, fed to `HudTextScaleEditModeTests` "TerrainHomeNote"): only its alpha changes.

Windows opened by the player stay at the token alpha: quest journal, trader dialog, notice history, build drawer, training
console, settings card (they have no scrim; see "For the owner").

## 2. Dependencies

The lane base only. No library file, no dotnet test, no stub (the new tests read only members that exist today; none names
`HudUi.PlateAlpha`: they assert the number .999).

## 3. Relevant legacy paths

- `packages/client/src/style.css:16` (`--plate` .58), `:297` (plate), `:339` (phase pill), `:371-383` (no death plate),
  `:461-462` (banners .96), `:489` (toasts .92); `ui/championCombatHud.css:14` (.9); `ui/chrome.css:46` (ping .7);
  `ui/terrainChips.css:7` (chip .72). Legacy is translucent: an intended difference (proposed DEV row above).

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/PlatesOpaqueEditModeTests.cs` (+ `.meta`)

## 5. Exclusive write scope

Under `client-unity/UnityProject/Assets/`; find members by name, the running packets move lines:
- `Scripts/UI/HudUi.cs` (the constant only).
- `Scripts/UI/MatchHud.Legacy.cs`: `CreateLegacyChrome` (ping pill), `CreatePlate`, `CreateDamageReadout` (the
  `HudUi.Surface(_championHud, ...)` statement): one literal each, no line added or removed.
- `Scripts/UI/MatchHud.TopCentre.cs` (`CreateTopAndToastPills`), `ToastLane.cs` (constructor), `QuestTrackerView.cs`
  (constructor), `MatchHud.ActionBar.cs` (`CreateConsumables`, the Wind chip only), `MatchHud.Terrain.cs` (`TerrainChipAt`,
  `ShowTerrain`: the two colour literals), `QuestOfferCard.cs` (constructor: the surface statement only),
  `SentencedHuntPanel.cs` (`Initialize`), `MatchHud.Spectate.cs` (`CreateSpectateBar`), `MatchHud.Chest.cs`
  (`CreateChestPanel`, one added line), `TutorialView.cs` (`Initialize`, the `TutorialCard` `HudUi.Surface` call only),
  `InspectorCardView.cs` (constructor).
- Not in scope: `docs/art/palette.json`, `UiPaletteTokens.cs`, `HudUi.Surface`, `HudUi.Button`, `MatchHud.Panel`
  (`MatchHud.cs`), `MythicUi.Panel`, the windows and modals of OUT (`QuestJournalView.cs`, `TraderDialogView.cs`,
  `OrderedNoticeFeed.cs` are P-UI-FLOWS-LEFTOVERS-1's, `BuildDrawer.cs` P-UI-HUD-GARBAGE-1's), `CreateDeathLine`, the
  `TutorialWelcome` statement of `TutorialView.cs`, `MatchHud.Tutorial.cs`, `View/ArenaView.cs`, `Tools/hud-seams.test.cjs`,
  `docs/DEVIATIONS.md`, `docs/unity/DIALS.md`. No existing test changes.
- Order by member and file: `MatchHud.Legacy.cs`: P-UI-HUD-GARBAGE-1 edits `RefreshLegacyHud` and `SetPlateMode`; this
  packet's literals are in other members, and it builds after that packet lands and re-reads the file.
  `CreateTopAndToastPills` here; `SyncTopCentre` (same file) in P-UI-RUN-END-1, which builds after this packet.
  `CreateDamageReadout` here and in P-UI-TEXT-FIT-1 (another statement; it merges last). `QuestOfferCard.cs`: this packet
  edits only the `HudUi.Surface` statement; P-UI-TEXT-FIT-1 takes the `_sizes` lines. `HudUi.cs`: this constant first in the
  class; a helper P-UI-SETTINGS-LABELS-1 may add goes after `Label`.

## 6. Contracts and references

- R-AUTH / D9: presentation only. Colours only from `UiTheme.Current` (`HudLegacyLayoutEditModeTests.EveryHudGraphicUsesAColourFromTheTheme`
  must stay green: `UiTheme.Owns` ignores alpha). COMMON lane rules (`.agent-runs/lane-ui-rework/specs/COMMON.md`).

## 7. Behaviour to preserve

- Every rgb, frame, gloss, radius, size, place, key and word. The ring of the tracker stays .6. The note stays a full-width
  band toggled by `Image.enabled`; the chip Outline keeps `useGraphicAlpha = false`; chip and note stay sprite-less `Image`s.
- The champion Button multiplies the Image colour by its normal colour (white) and, on hover, by an opaque `SurfaceHover`:
  alpha 1 on the Image is enough. Leave the colour block alone.
- The HUD still ignores `UiTheme.Changed` for its plates (the standing guard).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-PLATES-OPAQUE-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `PlatesOpaqueEditModeTests` all green; `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green (through the
  wrapper).
- These existing classes read the code and stay green (verified by reading, each with what it asserts):
  - `HudVisualFix3EditModeTests` (ice chip, home note; `AssertReadable`, line ~149): backing `Image.color.a >= .6`, and the
    text over that backing laid on white keeps 3:1 (higher at alpha 1).
  - `HudLegacyLayoutEditModeTests.EveryHudGraphicUsesAColourFromTheTheme` and `ARecolouredThemeReskinsEveryHudGraphic` (line
    ~383-420): every HUD graphic's rgb is a theme colour; ignores alpha; no terrain row is built there.
  - `ToastLanesEditModeTests` (`:279`): the settled toast group alpha is at least .99.
  - `QuestTrackerEditModeTests.ItFadesIn` (`:352-355`): the tracker's group alpha rises to 1; the file's `Tone` helper (`:264`)
    compares text colours only.
  - `HudTextScaleEditModeTests` (a HUD label fit; "TerrainHomeNote", "InspectorCard"), `QuestOfferEditModeTests` (offer card
    behaviour; no plate alpha), `InspectorCardViewEditModeTests` (Show/Hide, Lines, Text; no alpha),
    `HudVisualFix4EditModeTests` and `Ui4EditModeTests` (hunt card text and placement; no alpha).
  - No existing test asserts a plate alpha below 1 (searched `color.a`, `.a,`, the literals .82 .94 .96 .97 in
    `Tests/`): none turns red. `StormHubArtworkEditModeTests` asserts `< 1` on the hub, which is untouched.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/PlatesOpaqueEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/PlatesOpaqueEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-PLATES-OPAQUE-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> PlatesOpaqueEditModeTests,HudVisualFix3EditModeTests,HudLegacyLayoutEditModeTests,ToastLanesEditModeTests,QuestTrackerEditModeTests,HudTextScaleEditModeTests,QuestOfferEditModeTests,InspectorCardViewEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-PLATES-OPAQUE-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Sonnet: sixteen one-literal edits plus one constant and one added line. Risk: a missed pooled surface (toast pills not shown,
the note when `Image.enabled` is false), the chest panel's token alpha, editing a shared builder (`HudUi.Surface`,
`MatchHud.Panel`) instead of the site.

## 11. Known traps

- Do not clamp inside `HudUi.Surface`, `MatchHud.Panel` or `MythicUi.Panel`: they build the modals, windows and hub, which
  stay as they are. The edit is at each site, through the constant.
- A toast pill is built once, in the `ToastLane` constructor, so that edit covers pills not yet shown.
- `TickTerrain`, `TickTutorial`, `TickSpectate` and `SyncTopCentre` rewrite their own visibility (a test shows directly last).
- Never `SetActive` the tracker or offer card in a test (`HudFade` owners show through `Set` and `Snap`).
- `MatchHud.Legacy.cs` gains and loses no line. Tabs and trailing spaces: one literal replaced in place.
- A packet branch stays linear: never merge another branch into `packet/P-UI-PLATES-OPAQUE-1`.

## For the owner (visible changes and follow-ups)

(a) The tracker, the terrain band and chips, the ping pill and the inspector card lose their see-through look; no key, word or
layout number moves. (b) Six windows the player opens (quest journal, trader window, notice history, build drawer, training
console, settings card) have NO scrim, are filled at the theme's .94 and can still let a name through. The follow-up is the
theme's panel alpha, one token (`ui.panelAlpha` and the generated `UiPaletteTokens.cs`), in its own packet after this one has
been seen in a build. (c) The home-ground slab is 294 units wide (`TerrainWidth`) against about 170 of text and becomes a solid
band; the follow-up is a panel sprite and frame switched with the note (a layout change: not this packet). (d) The inspector
card now hides what stands under it at the top right. (e) The death line keeps its .9 plate over the fall. The next Web round
looks at the scene of `22-both-open.png` (x 152-268, y 59-73 within 2 levels of the plate or cell colour), the home-ground band
over lava, the tracker, and a hover at the top right. Proposed DEV row: see D-rules above.

## Notes from the oracle author

- **Tests (21 cases, one class `PlatesOpaqueEditModeTests`):** ten opacity tests, each `[TestCase(false)] [TestCase(true)]`
  on `recoloured` (20 cases), plus the standing guard `TheHudIgnoresAThemeChangeForItsPlates`. No stub, no library file, no
  dotnet test: nothing names `HudUi.PlateAlpha` (the tests assert drawn alpha >= .999).
- **Red on the stub (20), each on its "is see-through: drawn alpha <a>" assertion** (set-up lines all pass): plate, champion,
  Wind, phase, tutorial card, chest .94 (tutorial card and chest .235 recoloured); ping .85; banner .96; toasts .97 (shown and
  pooled: EventLane has 4 pills, FeedbackLane 3, two shown in FeedbackLane because the first-run hint stands beside the
  refusal); tracker .82; offer .96; ice chip and note .8; hunt .93; spectate bar .94; inspector .78. **Green on the base (1):**
  the guard, by design (it pins that the HUD ignores `UiTheme.Changed`; it must stay green after the change).
- **Scratch run done** (collect-all, not committed): `AssertOpaque` / `AssertDrawnOpaque` caught `AssertionException` into a
  list that `TearDown` failed with. Every entry of every test was a "see-through" line with the values above; every set-up
  assertion (theme alpha 240/60, session Reconnecting or Failed after `Close`, tracker `Rows >= 1`, `OpenOffer` true,
  `HuntPanel.IsVisible`, spectate bar, chest panel, first-run pill and refusal pill found by text) passed, and no other
  exception occurred; so every line of each test ran.
- **Deviations from the spec text:** none in design. Details the spec left open: the offer's giver is placed at (70, 64) beside
  the rig's player (64, 64) so `OpenOffer` finds it in reach (`QuestOfferEditModeTests.Offer` puts it at 330, 320 for a player
  at 320, 320); the chest is placed at (64, 64) for the same reason; paths are looked up as `_hud.transform.Find(...)`
  (`MatchUI/...` is a child of the HUD object), exact, never `HudTestPaths.Find`. `Frame()` does not set `_dirty` (not needed).
- **Existing classes run on the stub, all green (133/133):** `HudVisualFix3EditModeTests`, `HudLegacyLayoutEditModeTests`,
  `ToastLanesEditModeTests`, `QuestTrackerEditModeTests`, `HudTextScaleEditModeTests`, `QuestOfferEditModeTests`,
  `InspectorCardViewEditModeTests`, `HudVisualFix4EditModeTests`, `Ui4EditModeTests`; and `node --test
  client-unity/UnityProject/Tools/hud-seams.test.cjs` (14/14).

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
