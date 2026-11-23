# P-UI-QUEST-OFFER-1: F at a quest giver opens a small offer card; Enter accepts; nothing stops the fight

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("Quest UI could be perfected")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-3` (lane branch on `feature/unity-rework`: base-2 plus P-UI-ABILITY-ROW-1,
  P-UI-TOAST-LANES-1, P-UI-TABMAP-SHEET-1, P-UI-QUEST-TRACKER-1, P-UI-TABMAP-SHEET-1-fix, P-UI-ALLOC-PROOF-1 and
  integration 84, which holds Rust and records only)
- **D-rules touched:** D1: presentation and input routing. Legacy opens a quest dialog with F and accepts with Enter or 1
  (`docs/rework/LEGACY_LAYOUT.md` key table; `packages/client/src/ui/questDialog.ts:190` hint "Enter or 1 accept, Esc or
  F close"; `packages/client/src/ui/questDialogMath.ts:26-33` `questDialogKeyAction`: Enter, NumpadEnter, Digit1 and
  Numpad1 accept, Escape and KeyF close, nothing while shift is held or the dialog has no focus), and the game's own
  guide already says it (`UI/HelpGuideView.cs:61` "F: Pick up a nearby weapon or speak to a giver.", `:65` "Enter:
  Accept the quest offer opened with F.", `:116` "Walk up to a quest giver, press F, then Enter to accept."). This
  packet makes that true: F talks, Enter and numpad Enter accept, Escape and F close the card. **The owner has to see
  these three (F talking, Enter, Escape) before merge:** `P-QUEST25-CONTROLS` lists neither Enter nor Escape in play,
  nor talking with F. Legacy accepted only while the dialog had focus; here Enter accepts while the card is open whether
  or not it has focus (it never takes focus). The digit 1 is left out: digits bind nothing outside the chest picker
  (owner direction 2026-11-18, pinned by `UiInputRoutingEditModeTests.DigitKeysNoLongerSwapWeaponsOutsideChestChoice`)
  and the card does not block play: propose a DEVIATIONS row (coordinator writes it; agents do not edit
  `docs/DEVIATIONS.md`). D2 n/a. D5/D6 none. D9 untouched: accepting is the existing `QuestAccept` command
  (`session.Input.QuestAccept(handle)`).
- `docs/OWNER_DECISIONS.md` has no row on quest offer keys (searched "quest"); the controls decision is the merged packet
  `docs/backlog/packets/P-QUEST25-CONTROLS.md`.

**RISKS (repeat in the hand-off):**
1. The three keys above (F talking, Enter accepting, Escape closing) are not in the owner's control mapping.
2. The journal's Accept and the card's Accept guard against a second send separately (P-UI-JOURNAL-POLISH-1 guards the
   journal), so a player pressing both within the same second can send two accepts. The server answers the second with
   a private refusal notice, "You already accepted that quest" (`crates/rh-sim/src/quests/live.rs:676-690`,
   `AcceptRefusal::Already`); the client keeps no state for it, the existing notice feed shows it, and nothing else
   happens.
3. `HudRegions.OfferMinHeight` (96, `HudRegions.cs:65`) is outside this write scope and wins: when less than 96 units lie
   between the tracker and the floor the card covers the champion panel or the readout strip. Say so in the hand-off.

## 1. ID and outcome

The only way to accept a quest today is J (a window over three quarters of the screen that stops movement), click the
row, click ACCEPT QUEST (`UI/QuestJournalView.cs:84` button, `:319` caption, `:333` handler, `:461` `TryAcceptFrom`). The
guide promises F then Enter; F only picks up (`Net/MatchClient.cs:489`, `PickupFocus` `:543-547`) and nothing reads Enter.

This packet adds a small offer card at the left of the screen. F near a giver (when no pickup is in reach) opens it, it
shows the one offer, Enter (or the card's button) accepts, Escape or F (or its Close button) closes it. The card never
takes focus and never blocks movement, attacks or casts. It closes by itself when the server confirms the acceptance,
when the offer is gone, or half a second after the offer leaves reach. The prompt lane shows "F · Talk to <giver>" when an
offer is in reach.

### 1a. Which offer (pure): new `client-unity/RoguehallaNet/Net/Ui/QuestOffer.cs`

```csharp
/// <summary>Which quest offer the player can take where they stand.</summary>
public static class QuestOffer
{
    /// <summary>The CanAccept row whose giver is nearest (x, y) by squared distance: the first met, replaced
    /// only by a strictly nearer one (ties and NaN keep the first). Null when none; inReach counts them.</summary>
    public static QuestRowView Pick(QuestView view, double x, double y, out int inReach);
    /// <summary>The row of this handle; null when absent, or the view is null or unavailable.</summary>
    public static QuestRowView Find(QuestView view, QuestHandle handle);
    /// <summary>The card's top so that it ends at or above floor: regionTop if regionTop + regionHeight <= floor,
    /// else floor - regionHeight, never above clear (the tracker's bottom). Any NaN gives regionTop.</summary>
    public static float Lift(float regionTop, float regionHeight, float clear, float floor);
}
```
Both lookups are index loops (no enumerator). `CanAccept` is the server's whole verdict (range and line of sight
included): the client adds no rule. A null or unavailable view gives null and 0 (`QuestView.Available` is false and its
`Rows` empty then, `RoguehallaNet/Net/Collections.cs:308-331`). The caller ignores a pick that
`QuestPresentation.ShowsOffer` (`MatchHud.QuestRewards.cs:386`) rejects.

`Lift` reads: unchanged when `regionTop + regionHeight <= floor`; else `max(clear, floor - regionHeight)`; a NaN in any
argument returns `regionTop`.

### 1b. The card: new `client-unity/UnityProject/Assets/Scripts/UI/QuestOfferCard.cs`

```csharp
/// <summary>The quest offer card: one offer the player can accept where they stand. Opened and closed by the HUD;
/// it sends nothing itself.</summary>
public sealed class QuestOfferCard
{
    public const float AcceptSeconds = 2.5f, GraceSeconds = .5f;
    public QuestOfferCard(Transform parent, System.Action onAccept, System.Action onClose);
    public RectTransform Root { get; }       // named "QuestOffer"
    public bool IsOpen { get; }              // the wanted state: false the moment Close is called
    public QuestHandle? Shown { get; }
    public float WantedHeight { get; }       // the height its text needs, for HudRegions.OfferCard
    public bool Accepting { get; }           // true for AcceptSeconds after MarkAccepting, or until Close
    public bool Lapsed { get; }              // out of reach for GraceSeconds of Tick time
    public void Open(QuestRowView offer, int inReach, double serverMs);
    public void Refresh(QuestRowView offer, int inReach, double serverMs);   // same offer, new facts
    public void NoteReach(bool canAccept);   // true zeroes the grace; false lets Tick count it
    public void Close();
    public void Place(HudRegion region);
    public void MarkAccepting();
    public void Tick(float dt);              // the fade, the accepting timer and the grace: its only clock
}
```
- 280 wide (`HudRegions.OfferWidth`, `HudRegions.cs:65`), on the left. Surface `UiTheme.With(theme.Surface, .96f)`, radius
  `theme.CornerRadiusLarge`, border `theme.BorderStrong`, raycast target (a click on the card must not attack).
- Children, top to bottom, 12 padding: `Giver/Label` (`GiverLabel`, `theme.SizeTiny`, `theme.Accent`, bold);
  `Name/Label` (`Name`, 15 units, bold, `theme.TextPrimary`, wraps to two lines); `Objective/Label` (`Objective`,
  `theme.SizeSmall`, `theme.TextPrimary`, wraps); `Reward/Label` ("Reward · " + `Reward`, `theme.SizeSmall`,
  `theme.Accent`, wraps); `Ends/Label` ("Ends in " + `QuestTracker.Clock(s)`, only when `ExpiresAt > 0`,
  `theme.TextSecondary`; s is computed in the card with the clamps of the private `QuestTracker.SecondsLeft`
  (`RoguehallaNet/Net/Ui/QuestTracker.cs`, "static int SecondsLeft"): ceiling of (`ExpiresAt` - server ms) / 1000, NaN,
  infinity or negative gives 0, above 1e9 gives 1e9); `More/Label` (inReach + " offers here · J for all" when
  `inReach > 1`, `theme.TextSecondary`); a container `Footer`, 28 high, with a Button `Accept` on the left (child `Label`:
  "ACCEPT · Enter", primary look; "ACCEPTING…" and not interactable while `Accepting` or during the grace) and a small
  Button `Close` on the right (child `Label`: "Esc · close") that calls `onClose`. `Accept` calls `onAccept`. `Tick`
  rewrites the Accept caption and interactable in the frame `Accepting` ends.
- Grace: after `NoteReach(false)` the card keeps showing; `Tick` adds its `dt` to a lost-reach time and `Lapsed` turns
  true at `GraceSeconds`. The card never closes itself: the HUD reads `Lapsed`.
- No label is cut (the label system shrinks text and never truncates; `.agent-runs/lane-ui-rework/specs/COMMON.md`
  "Facts": `HudUi.Label` adds `MythicTextScale` with `FitBox`). `Place` gives the card its region's height and runs only
  when the region changed. `WantedHeight` is measured when a text, the `More` line or the text scale changes, never per
  frame: `Refresh` writes a label only when its string differs (`string.Equals`) and caches "Ends in" by whole second and
  `More` by `inReach`. The card learns the scale in `Refresh`: it compares a cached value with
  `ShowcaseSettings.Current.TextScale` (null reads as 1, as `HudUi.Label` does, `HudUi.cs` Label) and re-measures when
  they differ. In a region shorter than `WantedHeight` the label boxes shrink in proportion (the footer keeps 28),
  `MythicTextScale.Refit()` runs after the resize and the FitBox labels shrink their text: nothing is cut.
- It appears and leaves through `HudFade` (`UI/UiMotion.cs:16`; interactive, default times): build the root, then
  `Snap(false)`; its `CanvasGroup` blocks raycasts only while the card is shown. Do not call `SetActive` on the root.

### 1c. Ownership and flow: `UI/MatchHud.QuestRewards.cs`

New members, all declared in this file (names: see traps):
- `public QuestOfferCard OfferCard { get; }`, built in `InitializeQuestRewardHud` (`:34`) under `_hud` (field
  `_offerCard`). The card's root is `MatchUI/HudStage/PlayerHUD/QuestOffer`.
- `public bool TryInteract()`, the HUD's answer to F when there is nothing to pick up. In order: the card is open: close
  it, return true; an offer is in reach (`QuestOffer.Pick` with `Session.Me` X and Y on the current quest view,
  `ShowsOffer` true) and the gates hold: `OpenOffer`, return true; else false. (P-UI-QUEST-REWARD-PROMPT-1 extends this
  chain; keep it one method.)
- `public bool OfferOpen` and `bool AcceptOffer()`: sends `session.Input.QuestAccept(handle)` when the session is the one
  the card opened on and is in a match with no result, the timeline epoch is the one it opened on, the CURRENT
  `session.Collections.Quests` view (never the view object it opened on: every delivery makes a new one) holds the shown
  handle (`QuestOffer.Find`) with `CanAccept`, and the card is not `Accepting`; then `MarkAccepting()`.
- `RefreshOffer(QuestView view, bool worldAvailable)`, called from `RefreshQuestRewardHud` (`:73`) after the overlay
  block (`:96-120`) and BEFORE `showHud` (`:122`) is computed, so the card is refreshed, closed and placed even when the
  box hides. In order: tick the card (below); card open: find the shown handle; `OwnerAccepted` (the tracker and the
  server's toast take over), a row that is gone, `ShowsOffer` false or a lost gate close it at once; otherwise
  `NoteReach(row.CanAccept)`, then `Lapsed` closes it, else `Refresh` and place it. Card closed: pick the offer for the
  prompt and `NoteReach(pick != null)`.
- Placement: `HudRegions.OfferCard(_hud.rect.height, dockTop, _questTracker.Rows, card.WantedHeight, floor +
  HudRegions.TrackerGap)` (`HudRegions.cs:152`), `dockTop = PlateChainBottom() + HudRegions.TrackerGap`
  (`MatchHud.Terrain.cs:176`, as `MatchHud.PlayerFlows.cs:161` passes it to the tracker), `floor = TrackerFloor()`
  (`MatchHud.Terrain.cs:185`: the top of the readout strip while it shows, else of the champion panel, minus 8;
  `HudRegions` takes the 8 off again, hence the plus). The top is then `QuestOffer.Lift(region.Y, region.Height, tracker
  bottom, floor)`, tracker bottom = `HudRegions.TrackerDock(dockTop, rows)`'s `Y + Height` (`HudRegions.cs:144`): a card
  that would end below the floor moves up, but not above the tracker. So it never covers the champion panel or the
  readout strip, unless less than `HudRegions.OfferMinHeight` (96) lies between tracker and floor: that minimum is in a
  file outside this scope and wins (say so in the hand-off).
- One tick per frame: `RefreshQuestRewardHud` also runs from `Collections`, `Changed`, `RefreshPlayerFlows` (so
  `TickPlayerFlows`) and `OpenQuestActions` (`:303`). `RefreshOffer` ticks the card only when `Time.frameCount` differs
  from `_offerTickFrame` (start -1), with `Mathf.Min(Time.unscaledDeltaTime, .1f)`; the `OpenQuestActions` re-entry runs in
  the same frame and cannot tick twice. A test ticks the card itself.
- Gates (`OfferGatesHold(Session)`), to open and to stay open: `Active` (`MatchHud.cs:139`), `session.Phase` Active, no
  `LastResult`, `session.Me` alive (`ActorStatus.Alive`), `WorldHudVisible` (it already holds the player-flow pages, the
  reward-choice and nearby modals, the map and a focused text field, through `BlocksKeyboardInput`). Losing a gate
  closes the card. `ResetQuestRewardHud` (`:321`) closes it: `Unbind` reaches it through `BindPlayerFlows(null)`, a new
  session and a timeline reset through `RefreshQuestRewardHud` (`:78-82`).
- The prompt: card closed, gates hold, an offer in reach: the box shows and the launcher is active and interactable (so a
  view with only a non-board `CanAccept` row, Def "fixture", shows it: today `showHud` needs
  `QuestPresentation.HasRewardHud`, `:122`, `:369-373`, which is false for such a view). Its caption "F · Talk to " + the
  giver label is written only when `_questChoice` is empty and `_nearbyQuestCount` is 0; otherwise the existing caption
  and click behaviour stay ("Choose quest reward", "Nearby quest actions · n", `:136-138`) and the offer is still
  reachable with F and J. While the pickup pill shows too (`_pickup.gameObject.activeSelf`, `UpdatePromptLane` `:143`)
  the caption is "Talk to " + the label, without the key; the two sit side by side as P-UI-ABILITY-ROW-1 placed them.
  The caption is cached by (handle, label, pair). `OpenQuestActions` (`:297`) gets a branch after the choice and nearby
  ones: an offer in reach calls `OpenOffer`. After the offer leaves reach the prompt holds, not interactable, until
  `Lapsed`. With no offer in reach the captions read as before.
- No allocation in a steady frame: index loops in every per-frame row lookup (no `foreach` over an interface-typed list,
  no `QuestPresentation.FindRow` `:354`; it stays for the bind code).

### 1d. Keys

- `Net/MatchClient.cs:489`: `if (kb.fKey.wasPressedThisFrame) { if (Session.Collections.Focus.HasValue) PickupFocus();
  else Hud?.TryInteract(); }` (`ReadInput` runs only in a verified match; `PickupFocus` rechecks).
- `UI/MatchHud.Features.cs`, `ReadUiInput` (`:208`), inside the block that already requires `!typing` (`:249`; `typing`
  is `Typing`, `:81`: the selected object is a focused `TMP_InputField`) and no modifier (`if (!modified)` `:252`):
  Enter or numpad Enter (`wasPressedThisFrame`) with `OfferOpen && !dialog` calls `AcceptOffer()`. Escape, the block's
  first statement, with `OfferOpen && !aimEsc` calls `CloseOffer()`, then `_map?.ReadUiInput(typing, true)` (as the
  reward-modal branch does at `:212-218`) and returns, so that Escape does nothing else. Before it, `ReadTrainingEscape`
  and the Hub-panel Escape (`:233-245`) have already run: an Escape they consume never reaches the card. Alt+Enter stays
  fullscreen (`:229`, modified).
- The card never joins `BlocksKeyboardInput` (`:46`) or `BlocksPointerInput` (`:48`): movement, attacks and casts continue
  while it is open. No digit key does anything.

## 2. Dependencies

The lane base `lane/ui-rework/base-3` (P-UI-QUEST-TRACKER-1 for `QuestTrackerView.Rows`, `PlateChainBottom`,
`TrackerFloor`; P-UI-ABILITY-ROW-1 for the prompt lane; P-UI-ALLOC-PROOF-1 for `AllocationMeter`;
P-UI-HUD-REGIONS-1 for `HudRegions.OfferCard`, `OfferWidth`, `OfferMinHeight`). `HudRegions.OfferCard` already exists
on the base and is not touched. Stacked behind it: P-UI-QUEST-REWARD-PROMPT-1 extends `TryInteract`.

## 3. Relevant legacy paths
- `packages/client/src/ui/questDialog.ts:190`: the dialog's focus hint, "Enter or 1 accept, Esc or F close".
- `packages/client/src/ui/questDialogMath.ts:26-33`: `questDialogKeyAction`: with shift held or the dialog without focus
  nothing; Enter, NumpadEnter, Digit1, Numpad1 accept; Escape, KeyF close.
- `docs/rework/LEGACY_LAYOUT.md` key table (F talks, Enter or 1 accepts).
- No RNG, tick or same-tick behaviour is involved: all client presentation. The server side of a second accept is
  `crates/rh-sim/src/quests/live.rs:676-690` (private refusal notice), read only.

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/QuestOffer.cs` and its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/QuestOffer.cs` (with `.meta`)
- `client-unity/UnityProject/Assets/Scripts/UI/QuestOfferCard.cs` (with `.meta`)
- `client-unity/RoguehallaNet.Tests/QuestOfferTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestOfferEditModeTests.cs` (with `.meta`)

## 5. Exclusive write scope
- the four new paths above
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Features.cs` (the two key branches in `ReadUiInput` only)
- `client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs` (the F line only, `:489`)
- product files only to remove an allocation that `AbilityRowEditModeTests.AnUnchangedTileAllocatesNothing` or
  `ASteadyQuestRefreshFrameAllocatesNothing` reports once they use the meter: `UI/AbilityTile.cs`,
  `RoguehallaNet/Net/Ui/AbilityFace.cs` with its synced copy. An allocation anywhere else: stop, name the file and the
  allocation, return blocked.
- Not in scope: `UI/QuestJournalView.cs`, `UI/HelpGuideView.cs` (its text becomes true and stays),
  `MatchHud.ActionBar.cs`, `MatchHud.Pulses.cs`, `MatchHud.cs`, `MatchHud.PlayerFlows.cs`, `UI/QuestTrackerView.cs`,
  `HudRegions.cs`, `hud-seams.test.cjs`; the reward choice and nearby-actions modals (P-UI-QUEST-REWARD-PROMPT-1).

## 6. Contracts and references
- Unity holds no game rule: the card only shows delivered facts and sends the existing `QuestAccept` (R-AUTH, ADR 0008,
  C-INPUT). `CanAccept` is the server's whole verdict.
- `.agent-runs/lane-ui-rework/specs/COMMON.md` (lane rules), `docs/backlog/packets/P-QUEST25-CONTROLS.md` (the owner's
  mapping), `docs/backlog/packets/P-UI-QUEST-TRACKER-1.md` (the tracker this card sits beside).
- Evidence: `.agent-runs/lane-ui-rework/understand/quest-ui.txt` (summary section 2 "ACCEPT", defects 7, 8, 9).

## 7. Behaviour to preserve
- Digits bind nothing outside the chest picker; Space swaps, Shift and Q cast, R wishes, E drinks, F picks up when a
  pickup is in reach (`MatchClient.cs:489`, `PickupFocus`).
- The journal's accept (`QuestJournalView.TryAcceptFrom`) is unchanged.
- The prompt captions read as before with no offer in reach ("Choose quest reward", "Nearby quest actions · n", "Return
  to turn in your reward").
- `HudFade` owns the card root's active state. `MatchHud` binds and unbinds sessions many times: no handler stays on an
  old session; keep the `Session.CueSent` subscription and the press pulses.
- The HUD draws `_stage` first; the card is under `_hud` (PlayerHUD) like the tracker.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-OFFER-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper (no `MatchHud` method name
  declared twice; do not edit that test).
- The existing classes below stay green.

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter QuestOffer`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> QuestOfferEditModeTests,AbilityRowEditModeTests,PlayerFlowsEditModeTests,UiInputRoutingEditModeTests,QuestTrackerEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh` (then `--check`)

## 10. Risk and suggested model tier
implementer-sonnet; reviewer. The hard parts are the ordering inside `RefreshQuestRewardHud` (the card must refresh before
`showHud` and when the box hides), the single tick per frame, the no-allocation steady frame, and placement against the
tracker and the champion panel across three windows.

## 11. Known traps
- `MatchClient` skips world keys while `Hud.BlocksKeyboardInput` is true: F reaches `TryInteract` unless a
  keyboard-blocking panel is up; the Tab map is stopped by the `WorldHudVisible` gate in `TryInteract`.
- Enter with Alt held is fullscreen (`MatchHud.Features.cs:229`): the accept branch requires no modifier. A focused
  `Accept` button may also fire on submit: both paths end in `AcceptOffer`, whose `Accepting` guard drops a second.
- `QuestRowView` objects are replaced with each quest view: keep the handle, the session and the epoch, never the row or
  the view.
- The test assembly cannot see `internal` members: `OfferCard`, `TryInteract` and `OfferOpen` are public; the rest is
  reached by reflection as the existing tests do.
- A new `MatchHud` method name must be unique across `MatchHud*.cs` (`hud-seams.test.cjs`, "no method name is declared in
  two files"); `TryInteract`, `AcceptOffer`, `RefreshOffer`, `OpenOffer`, `CloseOffer`, `PlaceOffer` and `OfferGatesHold`
  are free on the base.
- No label is cut by TMP: call `MythicTextScale.Refit()` after you resize a label's box (the fit runs when the text
  changes, not when the box does); `HudUi.Label` grows the label's rect by `ceil(size * .45)` above and below.
- `HudFade` takes its first state from the root's `activeSelf` when built and `Set` to the state it has does nothing:
  build the root, then `Snap(false)`.
- Time in EditMode does not advance: the tests drive time through `OfferCard.Tick(dt)` only.
- `GC.GetAllocatedBytesForCurrentThread()` reads 0 in this editor: allocation tests use `AllocationMeter.AssertNone`.
- The launcher's captions are tested by `AbilityRowEditModeTests.APickupAndAQuestActionShareThePromptLane` and
  `PlayerFlowsEditModeTests.NearbyRewardPickupUsesTheOfferedHandleAndDropIndex`.
- `PlayerFlowsEditModeTests.QuestState` delivers a `CanAccept` row (Def "fixture", giver (64, 80), me at (64, 64)): any
  existing test that delivers it now has an offer in reach. Run the whole of `PlayerFlowsEditModeTests` and read any
  red one before changing code: a changed expectation goes to the owner as `oracle-amend`.

## Existing tests that pin this code (verified by reading; all green on the stub)
- `AbilityRowEditModeTests.NothingFloatsAboveTheRow` (`:242`): the `QuestRewards` box holds only the launcher as its one
  child, at `HudRegions.Prompt()`. The new card is under `PlayerHUD`, not in the box.
- `AbilityRowEditModeTests.APickupAndAQuestActionShareThePromptLane` (`:260`): nearby action with no quest row: the launcher
  and the pickup pill share the lane; no offer in reach, so captions are unchanged.
- `PlayerFlowsEditModeTests.NearbyRewardPickupUsesTheOfferedHandleAndDropIndex` (`:126`): nearby action button; no rows.
- `PlayerFlowsEditModeTests.FullQuestSlotsPresentServerChoiceAgainAfterReconnectAndSendTheChosenTuple` (`:72`): the reward
  choice modal with a `CanAccept = false` row; the choice caption keeps priority.
- `PlayerFlowsEditModeTests.JournalKeyboardOwnsSimultaneousMapAndWorldKeysAndRespectsExistingModals` (`:370`) and
  `PlanningQuestReviewPreservesMapChoicesAndFocusWhileBlockingOtherPagesAndWorldKeys` (`:393`): hold F with the journal
  open and assert no input leaks.
- `UiInputRoutingEditModeTests.DigitKeysNoLongerSwapWeaponsOutsideChestChoice` (`:323`): digits send nothing.
- `UiInputRoutingEditModeTests.ChestChoiceConsumesDigitEvenAsItReleasesChestFocus` (`:425`) and the Tab-map tests: Escape and the
  chest focus (`ReleaseChestFocus`).
- `QuestTrackerEditModeTests` (all, notably `TheReadoutStripSetsTheFloor` `:402`): the tracker and `TrackerFloor`.
- `QuestJournalPolishEditModeTests` (`:75`): the journal sends exactly one `QuestAccept`.
- `AbilityRowEditModeTests.AnUnchangedTileAllocatesNothing` and `ASteadyQuestRefreshFrameAllocatesNothing` read
  `GC.GetAllocatedBytesForCurrentThread()`, which reads 0 here: see "oracle-amend" below.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/QuestOfferTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestOfferEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestOfferEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs` (two `oracle-amend:` commits, see below)
- `docs/backlog/packets/P-UI-QUEST-OFFER-1.md`

### Existing tests amended (`oracle-amend:` commits, for the owner to approve)
`AbilityRowEditModeTests.AnUnchangedTileAllocatesNothing` and `ASteadyQuestRefreshFrameAllocatesNothing` measured with
`GC.GetAllocatedBytesForCurrentThread()`, which reads 0 in this editor (lane facts, `specs/COMMON.md`), so they passed
on any code. Each is converted, in its own commit, to `AllocationMeter.AssertNone(action, 200, what)` with the delegate
built once and the set-up assertions kept (as `specs/P-UI-ALLOC-PROOF-1.md` items 1 to 3 say). **Both are GREEN on the
base with the real meter** (measured on the stub tree, 2026-11-23): the tile's `Show` and the steady quest refresh
frame (a view with one quest spell) allocate nothing. Nothing for the implementer to remove there; if a later change of
this packet makes one red, remove the allocation (`UI/AbilityTile.cs`, `RoguehallaNet/Net/Ui/AbilityFace.cs` with its
synced copy), never loosen the test.

### What the stub commit left for the implementer to replace
- `RoguehallaNet/Net/Ui/QuestOffer.cs` and its synced copy: `Pick` returns null and 0, `Find` null, `Lift` returns
  `regionTop`.
- `UI/QuestOfferCard.cs`: every member returns a default (`Root` is null, the constructor ignores its arguments).
  The card must now be built for real: root `QuestOffer` under `_hud`.
- `UI/MatchHud.QuestRewards.cs`: `_offerCard` / `OfferCard` (a lazily made default stub card, to be built in
  `InitializeQuestRewardHud`), `OfferOpen` (false), `TryInteract()` (false), `AcceptOffer()` and `CloseOffer()` (do
  nothing). Keep the names: the tests call `OfferCard`, `OfferOpen`, `TryInteract` directly.

## Notes from the oracle author

### Tests: which are red, and on which assertion (stub tree, 2026-11-23)
dotnet `QuestOfferTests` (6 tests), all red on the stub, each on an assertion: `NoAcceptableRowGivesNullAndZero` ("the same
rows with one CanAccept row pick it"; the empty answers alone would pass on a default stub, so the test also proves the
function is not a constant), `OneAcceptableRowAmongOthersIsPickedAndOnlyAcceptableRowsAreCounted`,
`TheNearerGiverWinsAndEqualDistancesKeepTheFirstRow`, `NaNPositionsNeverThrowAndPickTheFirstAcceptableRow`,
`FindReturnsTheRowOfAHandleAndNullOtherwise`, `LiftKeepsTheCardAboveTheFloorAndBelowTheTracker` ("overrunning by 30 with
room ends at the floor"). They pass against a scratch reference implementation (not committed).

Unity `QuestOfferEditModeTests` (20 test cases from 17 test methods), all red on the stub on an assertion:
- `FOpensTheOfferCardWhenAnOfferIsInReach`: "F opens the card" (OfferOpen false).
- `FStillPicksUpWhenAPropIsInReach`: the pickup half is green on the stub; red on "F talks when nothing is in reach to pick up".
- `EnterAcceptsOnce(Enter)`, `(NumpadEnter)`, `TheCardClosesWhenTheServerConfirms`,
  `TheCardClosesHalfASecondAfterTheOfferLeavesReach`, `AShortLossOfReachKeepsTheCard`, `EscapeAndFCloseIt`,
  `TheCardDoesNotStopPlay`, `ItNeverOpensOverAModalOrTheMap`, `TheEndsAndMoreLines`, `TheTextFitsAtTextScaleOnePointFive`,
  `AcceptChecksTheCurrentView`, `AnOpenCardAllocatesNothing`, and `TheCardSitsOnTheLeftClear...` (3 windows): red on
  "set-up: F opened the card" (a set-up assertion that is itself the behaviour: F opens the card).
- `ThePromptLaneOffersTheTalk`: "an offer in reach shows the launcher".
- `TheRewardChoiceCaptionStillWins`: "the pending choice keeps the caption" (see the defect below).
- `AClosedCardWithAnOfferInReachAllocatesNothing`: "set-up: the launcher shows the offer in reach".

Existing classes run on the stub and green: `AbilityRowEditModeTests` (with the two amended tests), `PlayerFlowsEditModeTests`,
`UiInputRoutingEditModeTests`, `QuestTrackerEditModeTests`, `QuestJournalPolishEditModeTests`,
`QuestJournalViewEditModeTests`, `HudTextScaleEditModeTests`: 106 passed, the only 20 red are the new cases above.
The 126 total of that run is those classes plus `QuestOfferEditModeTests`.

### Scratch run (not committed)
For `QuestOfferEditModeTests` one scratch copy of the class ran with a catch-and-collect `Assert` (every failed assertion
recorded, the test carried on to its last line). Result: every entry (about 140 over the 20 cases) is an assertion about
the missing behaviour (no card, no caption, no command, no allocation); no other exception was thrown, so every line of
every test can run. The run found and fixed two defects of the first version of the tests: (1) the readout strip and
the open extras together leave only 56 units between tracker and floor at 1280x800 and 1920x1080 (the spec's order
"extras, then readouts, then require 96" cannot hold): test 9 now checks the extras without readouts, closes the extras
again, then delivers the readouts; (2) negative or NaN giver positions are refused by the encoder (`quest giver
position`; found by the dotnet run against the scratch reference), so the dotnet NaN case moved to the query point. Test 8
also sends the digit first, so that no Stop from a released W lands in "a digit does nothing" (by reasoning, not seen).
The assertions that follow a card that opens (`Accepting`, the labels after an accept, the geometry of test 9, the text
fit at scale 1.5) could only be run up to their first lookup of the missing card: their expected values were read once
more against the rules of this packet, not executed.

### Differences from the spec (nearest test that pins the same behaviour)
- One rig for every EditMode test: the `UiInputRoutingEditModeTests` fixture (spec: Rig A for most, Rig B, the
  `AbilityRowEditModeTests` full rig, for test 9). Both are a whole `MatchHud` on a real `MatchClient` over a fake
  transport; Rig A has a real keyboard and gives the server time 1000, so test 9 uses it too. The tracker and
  champion-panel geometry is read from the live HUD in both.
- Test 9 order (see the scratch run, defect 1): extras without readouts, extras closed, then the readout strip. In the
  open-extras state the 96-unit minimum card cannot fit between tracker and floor once readouts show; that is
  `HudRegions.OfferMinHeight` winning, as RISKS 3 says.
- Test 1 of the dotnet class carries one extra positive case (the same rows with one acceptable row), because the "no
  acceptable row" answers alone pass on a default stub.
- The text scale is set on the HUD's own `ShowcaseSettings` and on `ShowcaseSettings.Current` (the card reads the
  second, `MatchHud.ApplyTextScale` the first); both are restored in TearDown.
- Test 11 ends with F while the choice is deferred (`TryInteract` true, `OfferOpen`, caption unchanged), because its
  caption lines alone are covered by existing behaviour once the caption label exists.
- `AbilityRowEditModeTests` allocation tests: both green on the base (above).

### DEFECT in the spec found by the oracle author: the launcher has no caption label today
`InitializeQuestRewardHud` builds the launcher with `HudUi.Button(..., "", ...)` (`MatchHud.QuestRewards.cs:37`), and
`HudUi.Button` adds a `Label` child only when its label text is not empty (`HudUi.cs` Button). So
`_questActionCaption = _questActionLauncher.GetComponentInChildren<TMP_Text>(true)` (`:42`) is null and `Set(null, ...)`
(`MatchHud.cs:512`) does nothing: the launcher has never shown "Choose quest reward" or "Nearby quest actions · n"; it is a
bare rounded button. The spec's "the existing caption stays" is therefore false. This packet needs a real caption: the
implementer builds a label under the launcher (`HudUi.Label`, so it follows the text scale) in `InitializeQuestRewardHud`,
so that all captions of the prompt lane (the three old ones and "F · Talk to ..." / "Talk to ...") are drawn. The tests
find the caption as the first `TMP_Text` under `QuestRewards/QuestActionLauncher`. `UpdatePromptLane` already refits it
(`RefitLabel(_questActionCaption)`). The width of the launcher is 300, 220 beside the pill (`HudRegions.PromptWidth`,
`PromptPairWidth`): "Nearby quest actions · 12" and "Talk to <long giver label>" must still fit (FitBox shrinks).

### TRAP found by the oracle author: the steady refresh allocates for a view with a non-board row today
`QuestPresentation.HasRewardHud` -> `HasBoardRows` (`MatchHud.QuestRewards.cs:369-381`) runs `foreach` over
`view.Rows`, an `IReadOnlyList`: that allocates an enumerator every call, and `RefreshQuestRewardHud` calls it each
frame (`:122`) whenever the view has no spells, pending or actions. Measured on the stub: with the view of these tests
(one `Def "fixture"` row), `AllocationMeter.AssertNone` over `RefreshQuestRewardHud` reports an allocation, and it is gone
once `HasBoardRows` is an index loop. The two allocation tests of the packet
(`AClosedCardWithAnOfferInReachAllocatesNothing`, `AnOpenCardAllocatesNothing`) need that loop (it is in this scope:
`MatchHud.QuestRewards.cs`). `IsPending` (`:361`) and `FindRow` (`:354`) also use `foreach`; keep them off the per-frame path.

### Other facts the implementer needs
- Item keys of a state message are ascending (Props 2, Quests 10, Readouts 16): the tests deliver them in that order.
- A quest giver position must be finite and non-negative to encode (`EncodeException: quest giver position`).
- The tests run time through `OfferCard.Tick(dt)` only. The HUD adds at most 0.1 s once per `Time.frameCount`
  (`Time.frameCount` does not advance inside a test), so a test's later `Tick(.6f)` is the only large step.
- `WorldHudVisible` is a live property (`MatchHud.Visibility.cs:17`: not `BlocksKeyboardInput` and the map not open), so
  the card gate flips the instant a modal or the map opens; the tests rely on it.
- Stage sizes: 1280x800 gives a 1280x800 stage, 1920x1080 gives 1422x800, 2560x1440 gives 1707x960 (HUD units).
- Existing classes to run and keep green: `AbilityRowEditModeTests`, `PlayerFlowsEditModeTests`,
  `UiInputRoutingEditModeTests`, `QuestTrackerEditModeTests`, `QuestJournalPolishEditModeTests`,
  `QuestJournalViewEditModeTests`, `HudTextScaleEditModeTests`, plus the node test `hud-seams.test.cjs` and
  `HudLegacyLayoutEditModeTests`. A quest view with a `CanAccept` row now shows the launcher (and may open the card);
  read any red existing test before changing code.

## Notes from previous attempts
