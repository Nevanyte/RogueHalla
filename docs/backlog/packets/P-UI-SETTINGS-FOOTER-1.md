# P-UI-SETTINGS-FOOTER-1: the settings card's footer (Reset to defaults, Leave match) stands fixed at the card's bottom, always in view

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane, "do a UI optimization sweep"). It is the follow-up
  P-UI-SETTINGS-LABELS-1 names: that packet gives the card's texts their real size, which pushes the whole footer row below the
  fold on first opening.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (contains P-UI-SETTINGS-LABELS-1: `SettingsLabelsEditModeTests` is on it)
- **D-rules touched:** D1 loose proof only (owner direction 2026-11-10: tests of this packet are the proof; no capture) · D2 n/a (presentation
  only, no `rh-sim`, no wire) · D5/D6/D9 none. One DEVIATIONS row is proposed (see section 6).

## 1. ID and outcome

The settings card (`ModernSettingsView`) is a box at the top right: a heading row (`HeaderHeight` 36), then `SettingsScroll`, a masked
viewport holding `SettingsContent`, a vertical layout group with every row. Today `FooterRow()` adds the LAST row to that content
(`ModernSettingsView.cs:97`, `:309-317`): "Footer", 62 high with a leave action (36 without), holding the line "Saved in this browser.",
the button "Reset to defaults" and the button "Leave match". The card's height is the content's, capped at the window's height minus 60
(`ApplyViewport` `:108-113`, `Fit` `:115-128`). On a 16:9 window (uiH 800) the cap is 740, the viewport 694; with P-UI-SETTINGS-LABELS-1
the content is about 768 at text size 1 and about 843 at 1.5, so the whole footer lies below the fold on first opening and a player who
opens settings to leave the match sees no Leave button until they scroll.

When this packet lands:

1. **The footer leaves the scrolling content.** It is a child of the card (`_root`, object `ModernSettings`) itself and stands at the
   card's bottom, above the card's 10-unit bottom pad, at every scroll position, text size, card width and card height. Its object
   names are unchanged: `Footer`, with `Saved` (and its `Label`), `Reset` and `Leave` under it. Its size (62 high with a leave action,
   36 without), its texts and its three children's places inside it are unchanged. It keeps the side padding the rows have
   (`CardPadSide` = 14 on both sides): the Footer rect spans the card's width minus 2 x 14.
2. **The body ends above it.** `SettingsScroll`'s bottom inset becomes 10 + the footer's height (today `HudUi.Inset(_root, "SettingsScroll",
   0, HeaderHeight, 0, 10)` at `:59`). A **rule line** stands on the footer's top edge so the cut of the scrolling body reads as a
   border: a child of `Footer` named `Rule`, a Graphic, 1 unit high, anchored along the footer's top edge (its top edge IS the footer's
   top edge), spanning the footer's width, with the look of the rows' rules (`ControlRow` `:196-202`: `HudUi.Fill(row, "Rule",
   Tone(theme.Border, .25f))`, anchors (0,0)-(1,0), `sizeDelta` (0,1); the footer's rule is the same colour, anchored to the top).
3. **The card is as tall as it needs.** `Fit` adds the footer's height: card height = `HeaderHeight` + the content's preferred height +
   the footer's height + 10, capped as today. On a window tall enough nothing scrolls and the card looks as before (same rows, same
   order, the footer last).
4. **Focus order is unchanged.** `UiFocusNavigation.Cycle` (`Assets/Scripts/UI/UiFocusNavigation.cs:11-27`) orders by HIERARCHY
   (`root.GetComponentsInChildren<Selectable>` in depth-first order). So the footer must be created AFTER `SettingsScroll` in the card
   (it is, if it is built where `FooterRow()` is called today, at the end of `Initialize`): the cycle's last two stops stay Reset then
   Leave.
5. Nothing else changes: no word, no key, no behaviour of Reset or of the two-click Leave, no row, no size of a row, no text-size rule.
   What P-UI-SETTINGS-LABELS-1 added (the fit after a text-size change while open, the position cue on the scroll frame
   (`ScrollIndicator`, `:99`), the note rows) keeps working; the cue stands on the scroll frame and so ends above the footer.
6. `client-unity/UnityProject/Tools/hud-shots.cjs:263` clicks Leave match by coordinates: see "Notes from the oracle author".

## 2. Dependencies

P-UI-SETTINGS-LABELS-1 merged in the base (`SettingsLabelsEditModeTests.cs` and its `SettingsRig` / `SettingsEnv` are on `lane/ui-rework/base-3`).

## 3. Relevant legacy paths

- `packages/client/src/ui/settingsPanel.ts:118-126`: the footer (`sp-foot`: the "Saved in this browser." line and the "Reset to defaults"
  button) is appended as the LAST child of the card.
- `packages/client/src/ui/chrome.css:65-81`: `.settings-panel` is ONE `overflow-y: auto` box, `max-height: calc(var(--ui-h) - 60px)`: so
  legacy's footer scrolls with the card (no fixed footer; legacy has no Leave button in this panel). `chrome.css:161-169`: `.sp-foot` is
  a flex row with `margin-top: 10px; padding-top: 10px; border-top: 1px solid #2b323c` (a rule line on its top edge, which this packet
  also gives Unity's fixed footer).
- So legacy keeps its footer inside the scrolling panel; Unity's card is shorter than legacy's on the same window because of its cap and
  its added rows, so the same structure hides more. Fixing the footer at the card's bottom is an intentional difference: propose ONE
  `DEVIATIONS.md` row (the owner signs it; not in this packet's scope): "Settings card: the footer (Saved line, Reset, Leave match) is
  fixed at the card's bottom outside the scrolling body, legacy scrolls it with the panel."

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/SettingsFooterEditModeTests.cs` (+ `.meta`): the oracle.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/ModernSettingsView.cs` (`Initialize`: where the footer is created and the scroll's inset;
  `FooterRow`; `Fit`). Keep the file under 800 lines (400 today).
- `client-unity/UnityProject/Tools/hud-shots.cjs`: the one coordinate of outcome 6, only if it must change.
- Nothing else. Not `HudUi.cs`, not `HelpGuideView.cs`, not `MythicTextScale.cs`, no `MatchHud*` file, no oracle file.

## 6. Contracts and references

- Presentation only: Unity holds no game rule; no wire, protocol or `crates/` change.
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md`: HUD units, theme colours only (a test checks every HUD graphic uses a theme
  colour: the Rule uses `Tone(theme.Border, ...)` like the rows), text through `HudUi.Label`, no allocation in a steady frame.
- Proposed DEVIATIONS row (owner signs): see section 3.
- `UiFocusNavigation.Cycle` orders by hierarchy (section 1, item 4).

## 7. Behaviour to preserve

- The footer's three texts: "Saved in this browser.", "Reset to defaults", "Leave match"; the first Leave click reads "Click again to
  leave" and does not leave; the second within 3 s leaves (`LeaveClicked`, `:321-325`); opening the card disarms (`SetVisible`, `:149`).
- Reset resets the display options (`ResetDisplayOptions`, `:347-351`).
- The footer's three children's places INSIDE the footer: `Saved` at (0, 8) 150 x 14 (`HudUi.At`), `Reset` top right, 4 down, 120 x 22,
  `Leave` top left at (0, 34) 140 x 22 (`:313-316`). They are anchored relative to the row, so the footer's inner width must equal the
  content's inner width (card width minus 2 x `CardPadSide`), or Reset (anchored top right) moves.
- P-UI-SETTINGS-LABELS-1's fit: `RefitLabels` collects the card's box-fitted labels under `_root` (`:130-136`); the footer's labels stay
  under `_root` and so are still refitted.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-SETTINGS-FOOTER-1` prints `GATE PASS`.
- Every test of `SettingsFooterEditModeTests` green; `SettingsLabelsEditModeTests`, `ModernSettingsLayoutEditModeTests`,
  `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`, `UiInputRoutingEditModeTests`, `PlatesOpaqueEditModeTests` stay green;
  the node tests under `Tools/` (`node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` etc.) if a file under `Tools/` changed.

## Existing tests that pin this code

- `ModernSettingsLayoutEditModeTests.CardIsAnchoredTopRightAtLegacyWidthAndCapsItsHeight` (:67-79): card anchors, 320 / 276 width, height
  <= uiH - 60. Unaffected.
- `ModernSettingsLayoutEditModeTests.ShortWindowScrollsTheBodyToBothEnds` (:81-102): at uiH 400 the card is 340 high; the content is taller
  than the viewport; `lastRow = content.GetChild(childCount-1)`: it is "clipped at the top" and "reachable at the bottom". TODAY the last row is
  the footer; after this packet it is the Training walkthrough row. Both assertions still hold in principle (the viewport is smaller, the
  content as tall): re-read after the implementation; if it needs an edit it goes through an `oracle-amend:` commit by the lane lead, not
  by the implementer.
- `ModernSettingsLayoutEditModeTests.CardHasTheLegacySectionsAndControls` (:104-111): the card's texts, including "Saved in this browser."
  and "Reset to defaults", are found anywhere under the card (inactive included): unaffected by moving the footer.
- `SettingsLabelsEditModeTests.TheOpenCardFollowsATextSizeChange` (:493-511), line 509: `h2 == 36 + content.rect.height + 10 (within 1)`
  "the card is its header, its content and 10". After this packet the content excludes the footer, so the card is 36 + content + 62 + 10:
  this line is **RE-STATED** in an `oracle-amend:` commit (kind: re-statement, the asserted total is the same card height; only the terms move:
  the footer's 62 leaves the content term).
- `SettingsLabelsEditModeTests.TheLastRowIsStillReachable` (:402-422): last = `content.GetChild(childCount-1)` must lie wholly inside the viewport at
  the bottom, first inside at the top; card 740 high at its cap. Holds before and after (the last row is then the Replay row).
- `SettingsLabelsEditModeTests.ASettledFitWritesNothingAndAllocatesNoMore` (:427-460): row heights by `content.GetChild(i)`, the settled
  labels (`FitBox`, at wanted size, clean) at least 20, `ApplyViewport` allocates at most `FitBaseSamples` (0). The footer's labels leave the
  content but stay under `_root`; the allocation budget must hold with the extra rect arithmetic in `Fit` (no allocation: constants only).
- `SettingsLabelsEditModeTests.EveryLabelIsRightAtTheFirstOpenAfterAChangedEvent` / `AssertCardRight` (:204-259): at least 30 fitted labels
  under the card, each at the size a refit gives: includes the footer's labels (a footer built outside `_root` would escape; it must not).
- `SettingsLabelsEditModeTests.TheCardShowsItsPositionCue` (:516-535): the cue on `SettingsScroll/ScrollIndicator` shows while the content is
  taller than the viewport, hides when it fits (uiH 1600). Holds.
- `HudLegacyLayoutEditModeTests.SettingsIsAnAnchoredCardAndHelpIsAPeripheralFiveSixtyCard` (:204-211): the card's top 48, right 12, width 320,
  height <= uiH - 60 at three windows. Unaffected. `ControlsLineEditModeTests` builds the same card (`Initialize` with a leave action, :62-67) for
  its own layout checks: unaffected.
- `UiInputRoutingEditModeTests` (:508, :619-622) and `TrainingConsoleIntegrationEditModeTests` (:692, :698): find `MatchUI/ModernSettings` and
  read `activeSelf` only. Unaffected.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/SettingsFooterEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/SettingsFooterEditModeTests.cs.meta`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> SettingsFooterEditModeTests,SettingsLabelsEditModeTests,ModernSettingsLayoutEditModeTests,HudLegacyLayoutEditModeTests,HudTextScaleEditModeTests,UiInputRoutingEditModeTests,PlatesOpaqueEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-SETTINGS-FOOTER-1`
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (through `in.sh`) when a `MatchHud` member moves (none should).

## 10. Risk and suggested model tier

implementer-sonnet, reviewer. One file; the traps are layout ones (inner width, the cap, the hidden fit).

## 11. Known traps

- The footer's children are placed with `HudUi.At` and `HudUi.Button` anchors RELATIVE to the row: keep the footer's inner width equal to the
  content's inner width, or Reset (anchored top right) moves.
- `Row(...)` (`:188-194`) parents to `_content` and adds a `LayoutElement`: the footer must not be built by a helper that parents it to the
  content. Build its rect under `_root` (e.g. `HudUi.Box(_root, "Footer", HudAnchor.BottomLeft/BottomRight stretch ...)` or a rect with
  anchors (0,0)-(1,0), pivot (.5,0), offsets 14 / 10, height 62 or 36) and give it the rule by hand, or give the helper a parent parameter
  without changing any other row's call.
- `Fit` runs with the card hidden too (it then lays out nothing: P-UI-SETTINGS-LABELS-1's trap): the footer's height is a constant of the
  build (62 or 36), never a measured value. Record it in a field when the footer is built.
- `RefitLabels` collects labels under `_root`: keep the footer under `_root`.
- The scroll's `RectMask2D` must not cut the footer: it is not under the viewport.
- Order of children under `_root`: create the footer after `SettingsScroll` (focus order, item 4 of the outcome). The `ScrollIndicator`
  stays on the scroll frame.
- No `Assert.Multiple`; no `InternalsVisibleTo`; EditMode time does not advance.
- A text label never active in the hierarchy measures at a tenth of its size: the tests open the card first.

## Notes from the oracle author

- Names the tests fix (so the implementer and reviewer know the contract): the footer is found by the object name `Footer` ANYWHERE under the
  card and must be a DIRECT child of the card (`Footer.parent == ModernSettings`); `Reset` and `Leave` and `Saved/Label` stay under it; the rule
  is `Footer/Rule` (a direct child, a `Graphic`, same colour as the first `SettingRow/Rule`, 0.5 to 1.5 high, its top edge on the footer's top
  edge, spanning the footer's width within .5).
- Spec deviation: none of substance. The spec's "stub" step: the tests compile against the existing public surface
  (`ModernSettingsView.Root`, `SetVisible`, `ApplyViewport`, `IsVisible`, `HeaderHeight`, `CardPadSide`, `Initialize` with a leave action,
  `UiFocusNavigation.Cycle`, `SettingsRig`, `SettingsEnv`), so there is no `stub:` commit.
- `TheFooterStaysWhileTheBodyScrolls` is run at text sizes 1 and 1.5 (spec: top, middle, bottom). `ATallWindowShowsTheWholeCardWithoutScrolling`
  and `TheBodyEndsAboveTheFooter` run at 1 (the rig's other scales are covered by test 1).
- The Leave guard drives no clock: in EditMode `Time.unscaledTime` is constant, `_leaveArmedUntil` starts at -1, so the first click arms
  (t > -1) and the second finds t <= t + 3: the test says so by its two clicks. No existing test covers the two clicks; the new guard does.
- Existing test pins and the DEVIATIONS row are listed in sections 3 and "Existing tests that pin this code".
