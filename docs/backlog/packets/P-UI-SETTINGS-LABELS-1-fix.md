# P-UI-SETTINGS-LABELS-1-fix: the position cue of the Help window and the settings card shows from the moment the window opens

- **Serves:** not in the plan: a fault P-UI-SETTINGS-LABELS-1 (merged on the lane branch as `f9acf92bc`) left behind, seen in the UI rework lane's look round 3c (2026-11-23, Web build of `2fd46225e`). Lane work, presentation only.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `2fd46225e`)
- **D-rules touched:** D1 n/a (client presentation, no parity) · D2 n/a · D5/D6/D9 none (no game rule, no wire)

## 1. ID and outcome

In the running build the Help window, just opened, shows no position cue although its list is longer than the window
(Controls: the last visible row is "C  Give the chest panel keyboard focus..." and more rows follow; Mechanics: the last
line is cut by the frame's edge). After one turn of the mouse wheel the cue appears. The settings card's first opening
does show it in the build.

When this lands, in the Help window: whenever a tab's scroll frame is on screen and its content is taller than its
viewport, its cue (the lane's `ScrollIndicator`: a thin bar at the frame's right edge) is visible with the right handle
length and place, WITHOUT any scrolling by the player: on the frame the window opens (or the next one), after a tab
change, after a text-size change while the window is open, and after the window's size changes. When the content fits,
no cue. The settings card gets the same guarantee after it opens, after its body is refitted (text-size change while
open) and after a viewport change. Nothing else changes: no word, no key, no size, no margin, no scroll behaviour.
No polling in a steady frame: a refresh runs on an event (open, tab, scale, size) or once on the frame after one.

## 2. Dependencies

P-UI-SETTINGS-LABELS-1 and `ScrollIndicator` are on the base. `AllocationMeter` (P-UI-ALLOC-METER-1) is on the base
(`Assets/Tests/Editor/AllocationMeter.cs`).

## 3. Relevant legacy paths

None: a client-side presentation fault. The cue is a lane addition, not a legacy element.

Code read (all on the base):
- `Assets/Scripts/UI/ScrollIndicator.cs:10-46`: `ScrollIndicator` builds a track (`ScrollIndicator`, inactive at first, line 30) with a `Handle` child on the frame; it refreshes ONLY from `scroll.onValueChanged` (line 31-32); `Refresh()` (35-45) reads `viewport.rect.height`, `content.rect.height`, `verticalNormalizedPosition`, shows the track when `ScrollThumb.Compute` says the content is taller than the viewport by more than `Slack` (.5), and sets the handle's anchors `low = 1 - bottom`, `high = 1 - top`.
- `client-unity/RoguehallaNet/Net/Ui/ScrollThumb.cs:10-19`: length = clamp(viewport / content, `MinLength` .06, 1); at the top (`verticalNormalizedPosition` 1) `top` = 0, so the handle spans `1 - length .. 1`.
- `Assets/Scripts/UI/HelpGuideView.cs:261-281` `CreateTextScroll`: ends with `new ScrollIndicator(scroll, frame);` (line 279) and keeps no reference. The body is a wrapped `HudUi.Label` under a `ContentSizeFitter` (line 277) whose height is known only after a layout pass. `ShowTab` (235-243) only toggles the two frames' `activeSelf` and the tab styles; `SetVisible` (228-233) toggles the root; `ApplyViewport` (222-226) sets the card's `sizeDelta` (height `max(240, min(uiH - 24, 700))`). The view has no `Update` or `LateUpdate`.
- `Assets/Scripts/UI/ModernSettingsView.cs:100`: `new ScrollIndicator(_scroll, scrollRect);` kept in no field. `Fit` (116-129) sets the card size, calls `Canvas.ForceUpdateCanvases()` and `LayoutRebuilder.ForceRebuildLayoutImmediate(_content)`, reads the preferred height, sizes the card, refits labels if the card is active; it never refreshes the cue. `SetVisible(true)` (146-157) calls `Fit`; `SettingsChanged` (139-144) calls `Fit` when the text scale changed while open; `ApplyViewport` (109-114) calls `Fit`.
- `Assets/Scripts/UI/QuestJournalView.cs:55,104-105`: the journal keeps `_textIndicator` and `_stripIndicator` in fields; grep finds NO call to `Refresh()` on either in `QuestJournalView*.cs`: the journal's cue also depends on the ScrollRect's own event. Not in this packet's scope (see "Notes from the oracle author").

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/ScrollCueAtOpenEditModeTests.cs` (+ `.meta`)
- `docs/backlog/packets/P-UI-SETTINGS-LABELS-1-fix.md` (this file)

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/HelpGuideView.cs`: keep the two indicators in fields; refresh them at the moments of the outcome.
- `client-unity/UnityProject/Assets/Scripts/UI/ModernSettingsView.cs`: keep the indicator; refresh it at the end of `Fit` when the card is active (after the layout rebuild).
- `client-unity/UnityProject/Assets/Scripts/UI/ScrollIndicator.cs`: only if the cause lies there (say what and why; the quest journal uses it too and its tests must stay green).
- Nothing else. `MythicTextScale.cs` and `HudUi.cs` are not edited. No existing test is edited.

## 6. Contracts and references

- Rules of the lane (`.agent-runs/lane-ui-rework/specs/COMMON.md`): presentation only; HUD stage units; motion not involved; a text label never active in the hierarchy measures at a tenth of its size (do not cache a size measured from a label that is not active in the hierarchy); a refresh must not allocate in a steady frame; no `Assert.Multiple`; no `InternalsVisibleTo`; EditMode time does not advance.
- The `hud-seams.test.cjs` node test pins some `MatchHud` members: this packet touches none.

## 7. Behaviour to preserve

- No word, key, size, margin or scroll behaviour changes. The text margin that keeps lines out from under the cue (`HelpGuideView.cs:273`) stays.
- The cue still follows the ScrollRect's event while the player scrolls (`ScrollIndicator.cs:31-32` stays).
- Settings card sizing (`Fit`) and its tests stay as they are.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-SETTINGS-LABELS-1-fix --base lane/ui-rework/base-3` passes.
- `ScrollCueAtOpenEditModeTests`: all 13 cases green.
- Existing classes green, unedited: `HelpBodyEditModeTests`, `HelpGuideViewEditModeTests`, `SettingsLabelsEditModeTests`, `SettingsFooterEditModeTests`, `ModernSettingsLayoutEditModeTests`, `QuestJournalDetailsEditModeTests`, `HudLegacyLayoutEditModeTests`.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/ScrollCueAtOpenEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ScrollCueAtOpenEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-SETTINGS-LABELS-1-fix.md`

There is no stub commit: the tests compile against members that already exist (`SettingsRig`, `HelpGuideView`, `ModernSettingsView`, `AllocationMeter`).

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ScrollCueAtOpenEditModeTests` (one to three minutes)
- the same with `HelpBodyEditModeTests,HelpGuideViewEditModeTests,SettingsLabelsEditModeTests,SettingsFooterEditModeTests,ModernSettingsLayoutEditModeTests,QuestJournalDetailsEditModeTests,HudLegacyLayoutEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-SETTINGS-LABELS-1-fix --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Low risk, small change in two views: Sonnet. The hard part is the order of the player's frame (layout known only after a
pass; an inactive label measures wrong), which is why the tests drive the real order (build hidden, open, wake, canvas
update, then one more frame) and never raise the scroll event by hand.

## 11. Known traps

- A refresh before the layout pass reads the old content height: force the content's layout first (as the settings card's `Fit` does with `LayoutRebuilder.ForceRebuildLayoutImmediate`), or refresh on the frame after the event. A product `LateUpdate` that refreshes once after a flag is fine; the tests' frame calls the view's `LateUpdate`/`Update` by reflection if the view has them (see "How the tests drive a frame").
- The Help body is hidden while the other tab shows: refresh only the tab that is on screen, and refresh the other when it is shown (a label under an inactive object measures wrong). Never keep a size measured from a label that is not active in the hierarchy.
- `HelpGuideViewEditModeTests` builds a bare `HelpGuideView` on a plain object with no canvas and no `MatchHud`: a refresh must tolerate that (no null reference).
- `SetVisible(true)` is called from the HUD's key and button paths and the HUD is rebuilt many times: the indicator's listener lives on its own ScrollRect, so there is nothing to remove, but do not subscribe anything on an outside object.
- No per-frame polling: a steady frame refreshes nothing and allocates nothing (test 6).

## Notes from the oracle author

**Scratch run on the base (the cause).** The Help window built on the `SettingsRig` rig (`HelpBodyEditModeTests`' rig), opened with `PressHelp`, the real wake (`MythicTextScale.OnEnable`) and the rig's canvas update, no event raised by hand, with a listener counting the scrolls' `onValueChanged`:
- Open (Controls, text size 1): viewport 476, content 1954.07, normalized position 1, `onValueChanged` fired 0 times, cue track inactive, handle 0..1 (its initial anchors).
- Mechanics tab: viewport 476, content 2065.79, fired 0, track inactive.
- After `SetTextScale(1.5)` while open (Controls): content 3383.27, fired 0, track inactive.
- Settings card opened at 1.5 (800 high window): viewport 632, content 781.85, track inactive; after `SetTextScale(.85)` while open: content 686.51, track still inactive. The card, too, has no path of its own in EditMode.
- `HelpGuideView` has no `Update` or `LateUpdate` (reflection check).

CAUSE, in one sentence: the cue refreshes only from the ScrollRect's `onValueChanged`, which (in EditMode, and in play only when the ScrollRect's own `LateUpdate` sees changed bounds) is never raised by the view's open, tab, text-size or size changes, and `HelpGuideView.CreateTextScroll` (`HelpGuideView.cs:279`) throws the `ScrollIndicator` away so `ShowTab`, `SetVisible` and the scale/size paths cannot call `Refresh()` after the content has its laid-out height; the settings card has the same gap at the end of `Fit` (`ModernSettingsView.cs:116-129`). NOT ESTABLISHED: why the settings card's cue DID show in the running build while Help's did not (a ScrollRect `LateUpdate` event on a changed bounds probably saved the card, whose bounds change in `Fit` before its first frame; the Help body's height settles after a label wake). EditMode cannot reproduce the build's order of events, so the tests are red because no refresh at all happens in EditMode; a fix that refreshes at the moments of the outcome satisfies both.

**The settings tests are red, not guards.** The spec allowed standing guards if the card already had a path on every route. It has none in EditMode, so tests 5 (three tests) are red on the base. `AShortHelpPageShowsNoCue` is the one NAMED standing guard (it passes on the base and must stay green).

**How the tests drive a frame** (no test fires `onValueChanged` or scrolls): `Wake` (what play adds on a wake: `OnEnable` of each active `MythicTextScale` under the help root, copied from `HelpBodyEditModeTests`), then `Frame` = canvas update, the views' own `LateUpdate` and `Update` invoked by reflection IF they exist, canvas update. `Settle` = two frames: so a product refresh on the event or on the frame after it passes; one that waits longer fails. Heights are read from the rects after settling, never typed.

**Deviations from the spec**
- Test 4 (`AShortHelpPageShowsNoCue`): the spec says "a window tall enough that the content fits", but the Help card's height is capped at 700 (`HelpGuideView.cs:225`) and the Controls text is about 1954 high, so no window fits it. The test shortens the body text to "Short." while the window is still hidden (a match builds the window hidden), then opens it: no cue. It passes on the base by construction; the red tests carry the behaviour.
- Test 6 (`ASteadyFrameRefreshesNothing`) first asserts the cue is shown and right (red on the base), then runs ten more frames and requires the handle's anchors and offsets, the track's size and its visibility to stay the same, for both the Help window and the settings card. The allocation line: the views have no per-frame member on the base, so `AllocationMeter.AssertNone` runs on `LateUpdate`/`Update` of each view ONLY IF the implementer adds one (a delegate to the method, so the meter measures the method and not reflection).
- Two extra tests the spec did not list: `TheHelpCueFollowsAChangeOfTheWindowSize` (outcome 1: size change; `ApplyViewport` 900 then 500 high) and `TheSettingsCueFollowsAChangeOfTheWindowSize` (outcome 2: viewport change; 600, 1600 where the content fits and the cue must hide, 600 again).
- Settings tests use a 600 high window where the card's content must be taller than its viewport at both text sizes (premise asserted: 706 vs 432 at size 1, 782 vs 432 at 1.5), so the handle visibly changes length.

**Red tests and the assertion each fails on (base, one Unity run, 12 red, 1 green):**
- `TheHelpCueShowsWhenTheWindowOpens` x4 (Controls and Mechanics, sizes 1 and 1.5): "the cue is shown" Expected True But was False.
- `TheHelpCueFollowsATabChange`: "Mechanics after the tab change: the cue is shown".
- `TheHelpCueFollowsATextSizeChangeWhileOpen`: "Controls at text size 1: the cue is shown".
- `TheHelpCueFollowsAChangeOfTheWindowSize`: "Controls in a 900 high window: the cue is shown".
- `TheSettingsCueShowsWhenTheCardOpens` x2 (sizes 1 and 1.5): "the settings card at text size ...: the cue is shown".
- `TheSettingsCueFollowsATextSizeChangeWhileOpen`: "the card at text size 1: the cue is shown".
- `TheSettingsCueFollowsAChangeOfTheWindowSize`: "the card in a 600 high window: the cue is shown".
- `ASteadyFrameRefreshesNothing`: "the Help window has settled with its cue: the cue is shown".
- Green on the base: `AShortHelpPageShowsNoCue` (standing guard).
- Premises that hold on the base (asserted before each cue line): the frame is active in the hierarchy; content taller than viewport (Controls 1954 vs 476 at size 1, 3383 at 1.5; Mechanics 2066 and 3631; card 706 and 782 vs 432 in the 600 window, vs 632 in the 800 window).

**Scratch run (not committed).** A copy of the class with every `Assert.That` routed through a catch-and-collect helper (collect `AssertionException` messages, fail in TearDown with the list) ran on the stub. Result: every entry of every test is an assertion about the missing behaviour (the cue not shown, and the handle at its initial 0..1 instead of viewport over content, and "shorter than before" lengths equal at 1.0). No other exception: every line after the first red assertion can run. Expected values (e.g. 476/1954.07 = .2436 at size 1, 476/3383.27 = .1407 at 1.5, 432/706.02 = .612, 432/781.85 = .553) agree with the rules above.

**Existing tests that pin this code (read; green on the base, to stay green and unedited):**
- `HelpBodyEditModeTests` (`Assets/Tests/Editor/HelpBodyEditModeTests.cs`): body at its wanted size in the real order (:55), scrolls where long and no line under the cue: margin = `Width + Inset + 1` (:79), follows a text-size change (:105), `TheHelpPanesShowTheirPositionCue` (:124): raises `onValueChanged` by hand and then expects the track active: stays true after the fix.
- `HelpGuideViewEditModeTests` (`Assets/Tests/Editor/HelpGuideViewEditModeTests.cs`): content, tabs and frames active state (:38-59), body text (:70), an `ApplyViewport` case (:228); builds a bare `HelpGuideView` with no canvas (see traps).
- `SettingsLabelsEditModeTests` (`Assets/Tests/Editor/SettingsLabelsEditModeTests.cs`; also defines `SettingsEnv` and `SettingsRig`): label sizes, card height = 36 + content + 62 + 10 after a text change while open, `TheCardShowsItsPositionCue` (:517ff) raises the event by hand; `FitBaseSamples = 0` allocation samples of `ApplyViewport(same viewport)` on a settled card (:162): the fix must not allocate in `Fit` (a refresh in `Fit` must not add GC.Alloc samples).
- `SettingsFooterEditModeTests`, `ModernSettingsLayoutEditModeTests`: settings card layout and footer (use the same rig / `ApplyViewport`).
- `QuestJournalDetailsEditModeTests` (`:323`, `:353`, `:389`): the journal's own cue tests (it uses `ScrollIndicator`).
- `HudLegacyLayoutEditModeTests`: HUD layout.
None of them asserts what this packet changes (a cue visible without an event); no `oracle-amend` is needed.

**Journal gap (report, not in scope).** `QuestJournalView` keeps its two indicators but nothing in `QuestJournalView*.cs` calls `Refresh()` on them: it likely has the same first-opening gap. Not tested here.

**Look in a running build:** the lane's next look round (the Help window at rest on both tabs, the settings card first opening).
