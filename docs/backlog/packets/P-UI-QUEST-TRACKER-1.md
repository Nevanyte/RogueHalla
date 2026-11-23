# P-UI-QUEST-TRACKER-1: the quests you hold are always on screen (name, objective, clock, progress)

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("Quest UI could be perfected")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (lane branch on `feature/unity-rework`)
- **D-rules touched:** D1: presentation; legacy shows the accepted quest in its HUD quest log and a one-line tracker
  (`docs/rework/LEGACY_LAYOUT.md` section 12), Unity shows nothing outside the journal today, so this narrows a
  difference (the legacy tracker line becomes this card) · D2 n/a · D5/D6 none · D9 untouched: every value shown is a
  delivered fact; the clock is the delivered deadline minus the delivered server time.

## 1. ID and outcome

After accepting a quest nothing on screen says what to do, how far along you are, or how long is left: those facts
exist only in the journal (J), a window over three quarters of the screen that stops movement, in one muted 13-unit
status line ("Accepted · 12 / 45 s · Server deadline in 134s", `UI/QuestJournalView.cs:308`, helper `:333`).

This packet adds a small card docked at the left under the plate chain. It lists the quests the player has accepted and
not finished, shows only delivered facts, takes no input and never takes the pointer.

### Which quests, in which order (pure)

A row of `QuestView.Rows` is shown when `OwnerAccepted && !OwnerCompleted && State == QuestRowState.Open`. A row whose
deadline has passed while its state is still Open stays, with the clock at "0:00", until the server changes its state.
Sort key: tier (0 when `OwnerReady`; else 1 when `ExpiresAt > 0`; else 2), then `ExpiresAt` ascending inside tiers 0
and 1 (a ready row without a deadline sorts after ready rows with one), then the order of `Rows`. At most
`HudRegions.TrackerMaxRows` (3) rows show, fewer when the space under the dock is short; the header counts the rest.

### What a row shows (260 x 38 = `HudRegions.TrackerWidth` x `HudRegions.TrackerRow`)

Four labels, each in its own box (row-local units from the row's top-left). `Row<i>/Name` and the others are the BOX
objects (`HudUi.At(rowRect, "Name", ...)`); the text is their child `Label`, built with `HudUi.Label`.

| Box | x, y, w, h | Text | Look |
|---|---|---|---|
| `Row<i>/Name` | 8, 3, 190, 16 | `QuestTracker.Short(Name, QuestTracker.NameChars)` | `theme.SizeSmall`, bold, left, `theme.TextPrimary`; `theme.Success` when `OwnerReady` |
| `Row<i>/Clock` | 202, 3, 50, 16 | `QuestTracker.Clock(secondsLeft)` | `theme.SizeSmall`, right; `theme.TextSecondary`, `theme.Warning` at 30 s or less, `theme.Danger` at 10 s or less |
| `Row<i>/Objective` | 8, 20, 166, 15 | `QuestTracker.Short(Objective, QuestTracker.ObjectiveChars)` | `theme.SizeTiny`, left, `theme.TextSecondary` |
| `Row<i>/Progress` | 178, 20, 74, 15 | `QuestTracker.Progress(quest)` | `theme.SizeTiny`, right, `theme.TextSecondary`; "Turn in" bold in `theme.Success` |

- No label uses TMP's Ellipsis or Truncate overflow: `MythicTextScale` (`HudUi.Label` adds it with `FitBox`,
  `UI/HudUi.cs:138-167`) forces overflow mode Overflow on every refit and `HudTextScaleEditModeTests` reports a label
  that cuts (`HudTextScaleEditModeTests.cs:267`). Long text is shortened in the pure layer (`Short` ends it with "…")
  and the label's own fit shrinks what is left into its box. Set `MinSize = 8` on the `MythicTextScale` of the name and
  objective labels.
- Header (`HudRegions.TrackerHeader`, 18 high): `Header/Title` "QUESTS" (box 8, 2, 120, 14; `theme.SizeTiny`,
  `theme.Accent`, bold); `Header/More` (box 132, 2, 120, 14; right; `theme.TextSecondary`):
  `QuestTracker.Header(running, shown)`.
- One surface behind header and rows:
  `HudUi.Surface(root, UiTheme.With(theme.Surface, .82f), theme.CornerRadius, UiTheme.With(theme.Border, .6f), raycast: false)`.
  No graphic in the card is a raycast target.

### Where

`HudRegions.TrackerDock(dockTop, rows)` (on the base, `Net/Ui/HudRegions.cs:122-126`, constants `:63-64`): left 12,
260 wide, 18 + 38 per row high. `dockTop = PlateChainBottom() + HudRegions.TrackerGap`: `TrackerDock` adds no gap of
its own, the caller adds the 8. The chain is the plate (`_vitalsPanel`, top `PlateTop` = 10, `MatchHud.Legacy.cs:18`),
then the terrain row when it shows (`MatchHud.Terrain.cs:125-145`, its top = plate bottom + 4, height `_terrainHeight`),
then the Sentenced hunt card when it shows (`UI/SentencedHuntPanel.cs`, placed by `PlaceHuntPanel`,
`MatchHud.Terrain.cs:176-182`).

How many rows fit: `rows = min(wanted, TrackerMaxRows, QuestTracker.RowsThatFit(dockTop, dockFloor))`, where `dockFloor`
(from the stage top) is the top of whatever lies below, minus 8:
- the native readout strip `NativeReadouts` (`UI/MatchHud.Readouts.cs:12,24`, the field `_readoutStrip`: a text block
  anchored at x .012 to .26 and from .44 to .64 of the stage height measured from the top) while it exists, is active
  and has text: its top;
- otherwise the champion panel's top (`stage height - ChampionBottom - ChampionHeight`, `MatchHud.Legacy.cs:19`).
With no room for one row the card is hidden. Worked cases on an 800-high stage: plate closed, no terrain row: dockTop
166, floor 344 with readouts, 3 rows (166 to 298). Extras open (plate bottom 224): dockTop 232, floor 344: 2 rows and
"+1 more · J". A Sentenced player choosing a rival with the extras open (chain bottom about 459): dockTop 467; with
readouts showing the card hides (the hunt card already covers that strip today); without them the floor is 618 and 3
rows fit (467 to 599).

The tracker follows the chain when the plate grows or the hunt card appears: it is re-placed whenever `dockTop`,
`dockFloor` or the row count changes. The chain may be one frame old (the terrain row and the hunt card are placed in
`TickTerrain`, which runs after `TickPlayerFlows` in `TickFeatures`, `MatchHud.Features.cs:321,330`): accepted.

### When

Wanted while a session is in a match in phase Active, the quest view is available, at least one row qualifies and fits,
the world HUD is visible (`WorldHudVisible`, `MatchHud.Visibility.cs:17`) and no player-flow page is open: the same
gate the hunt card uses in `TickPlayerFlows` (`MatchHud.PlayerFlows.cs:159`).
- It appears and leaves through a non-interactive `HudFade` (`UI/UiMotion.cs:16-74`, default times). The root is built,
  then `Snap(false)`.
- When the HUD gate drops (map, journal, a modal, results, phase not Active) the card goes at once: `Snap(false)`.
- When the last quest leaves while the HUD is visible it fades out (`Set(false)`): during the fade the card keeps the
  rows, texts and rect it last drew, and `Rows` reports 0 at once.
- A `Refresh` with a different `Session` object than the last one (or null) snaps the card away and forgets the rows it
  drew.
- Texts change in place; a row whose face did not change is not rewritten (`QuestTrackerRow.SameFace`).

### New pure file `client-unity/RoguehallaNet/Net/Ui/QuestTracker.cs` (namespace `Roguehalla.Net.Ui`)

```csharp
public readonly struct QuestTrackerRow
{
    public readonly QuestRowView Quest;
    public readonly int SecondsLeft;       // -1 without a deadline; 0 when it has passed
    public QuestTrackerRow(QuestRowView quest, int secondsLeft);
    public bool SameFace(QuestTrackerRow other);   // same quest object and same seconds
}
public static class QuestTracker
{
    public const int MaxRows = 3, UrgentSeconds = 30, CriticalSeconds = 10, NameChars = 26, ObjectiveChars = 34;
    public static int Build(QuestView view, double serverMs, System.Collections.Generic.List<QuestTrackerRow> rows);
    public static string Clock(int secondsLeft);          // -1 "", 134 "2:14", 9 "0:09", 0 "0:00", 3725 "62:05"
    public static string Progress(QuestRowView quest);    // "Turn in", "12 / 45 s", "3", ""
    public static string Header(int running, int shown);  // "J · log" or "+2 more · J"
    public static int DwellSeconds(string def);           // 45 hold_the_fort, 30 stay_a_while, else 0
    public static string Short(string text, int maxChars);
    public static int RowsThatFit(float dockTop, float dockFloor);
}
```
- `Build` clears `rows`, adds the first `MaxRows` quests to show, and returns how many accepted quests are running; a
  null or unavailable view gives 0.
- `SecondsLeft = ExpiresAt > 0 ? max(0, ceil((ExpiresAt - serverMs) / 1000)) : -1`; NaN or infinite counts as 0.
- `Short`: text when it is at most `maxChars` long (the SAME string object); else its first `maxChars - 1` characters,
  trailing spaces removed, and "…". Null gives "".
- `RowsThatFit = clamp(floor((dockFloor - dockTop - HudRegions.TrackerHeader) / HudRegions.TrackerRow), 0, MaxRows)`; a
  NaN input gives 0. `MaxRows` equals `HudRegions.TrackerMaxRows`.
- `Build` allocates nothing once `rows` has its capacity: sort in place with an insertion sort (`List<struct>.Sort`
  needs generic code the Web build may not have; `UI/SentencedHuntPanel.cs:78-84` has the same note). The view caches
  the shortened strings per quest object, so a steady frame builds none. A quest object is replaced whenever the server
  sends a new quest view, so "the same quest object" also means the same progress and readiness.

### New Unity file `client-unity/UnityProject/Assets/Scripts/UI/QuestTrackerView.cs`

```csharp
public sealed class QuestTrackerView
{
    public QuestTrackerView(Transform parent);
    public RectTransform Root { get; }               // named "QuestTracker"
    public int Rows { get; }                          // rows wanted and showing; 0 while hidden or leaving
    public void Refresh(Session session, bool hudVisible, float dockTop, float dockFloor, float dt);
}
```
Children by name: `Header/Title`, `Header/More`, and per row `Row<i>/Name|Clock|Objective|Progress` (three rows built
once). Needs a `/// <summary>` before its type (readability gate).

### Wiring

- `UI/MatchHud.Terrain.cs`, beside `PlaceHuntPanel` (`:176`):
  - `float PlateChainBottom()`: the bottom edge, from the top of the stage, of the lowest showing element of the
    chain. Plate: `PlateTop + _vitalsPanel.sizeDelta.y`. Terrain row when active: derive its top from the plate
    (plate bottom + 4, the gap `ShowTerrain` uses, `:140`) and add `_terrainHeight`. Hunt card when active:
    `SentencedHuntPanel.Bottom`.
  - `float TrackerFloor()`: the `dockFloor` rule above.
- `UI/SentencedHuntPanel.cs`: new `public float Bottom` (its top plus its height while its root is active, else a
  negative number; fields `_top`, `_height` at `:20`, `:133`).
- `UI/MatchHud.PlayerFlows.cs`: build the view under `_hud` in `InitializePlayerFlows` (`:30-43`; the hunt panel is
  built under `_hud` too, `MatchHud.Features.cs:137`; the tests' path for the root is `PlayerHUD/QuestTracker`); in
  `TickPlayerFlows` (`:153`), after the hunt panel's refresh (`:159`),
  `_questTracker?.Refresh(_session, <the hunt card's gate>, PlateChainBottom() + HudRegions.TrackerGap, TrackerFloor(), Time.unscaledDeltaTime)`.
  P-UI-TOAST-LANES-1 removes one other line of this file in the same wave (`_orderedNotices.SetRecentVisible(false)`
  in `ShowPlayerFlow`, `:70`): do not touch that line; the two changes merge by taking both.

## 2. Dependencies
none (base `lane/ui-rework/base-2`). `HudRegions.Tracker*` are already on the base.

## 3. Relevant legacy paths
- `docs/rework/LEGACY_LAYOUT.md` section 12: the legacy HUD quest log and one-line tracker (verified by the spec author).
  No Rust or `packages/` code is touched; Rust decides every quest outcome, Unity draws delivered facts only.
- Existing Unity source of the same facts: `UI/QuestJournalView.cs:308` (status line), `:333` (`QuestProgressText`,
  the dwell numbers 45 and 30), `:415` (deadline copy).

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/QuestTracker.cs`, its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/QuestTracker.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/UI/QuestTrackerView.cs` with `.meta`
- `client-unity/RoguehallaNet.Tests/QuestTrackerTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestTrackerEditModeTests.cs` with `.meta`

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Terrain.cs` (`PlateChainBottom` and `TrackerFloor` only)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.PlayerFlows.cs` (the field, the construction, the one tick line)
- `client-unity/UnityProject/Assets/Scripts/UI/SentencedHuntPanel.cs` (`Bottom` only)
- the new files of section 4 (the implementer edits only the two non-test files and the synced copy; the test files are oracle)
- Not in scope: `UI/QuestJournalView.cs` (P-UI-JOURNAL-POLISH-1 owns it), `MatchHud.Readouts.cs` (read
  `_readoutStrip`, change nothing), `MatchHud.Features.cs`, `MatchHud.QuestRewards.cs`, `MatchHud.Notices.cs`,
  `MatchHud.TopCentre.cs`, `MatchHud.cs`, `MatchHud.Visibility.cs`, `Net/Ui/HudRegions.cs`,
  `HudTextScaleEditModeTests.cs`, `HudLegacyLayoutEditModeTests.cs` (other packets of this wave own them).

## 6. Contracts and references
- R-AUTH / ADR 0008: Unity holds no game rule; D9: no prediction here. Only delivered facts are drawn.
- COMMON lane rules: HUD stage units, text through `HudUi.Label`, theme colours from `UiTheme.Current` only, motion on
  unscaled time and instant under `ReducedMotion`, no allocation in a steady frame, readability gate (summary on the
  first type, at most 800 lines, no new lines in `ArenaView.cs` or `SpriteActorPresenter.cs`).
- `MatchHud` binds and unbinds sessions many times: the view must not hold a handler on an old session.

## 7. Behaviour to preserve
- The journal and `QuestProgressText` stay as they are. The hunt card's placement and refresh are unchanged (only
  `Bottom` is added). The terrain row's placement is unchanged. `Session.CueSent` subscription and press pulses in
  `MatchHud` stay.
- Names pinned by `client-unity/UnityProject/Tools/hud-seams.test.cjs` (`:109-125` slices `InitializePlayerFlows` and
  `TickPlayerFlows` of `MatchHud.PlayerFlows.cs`): the new lines must not break its regexes
  (`CreateNoticeFeed();` stays after `_traderDialog.Initialize` and before `AddPlayerFlowTabs(_questJournal.Root)`;
  `TickNotices();` stays in `TickPlayerFlows`; no `SetRecentVisible` in it).

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-TRACKER-1 --base lane/ui-rework/base-2` passes.
- dotnet `QuestTrackerTests` and Unity `QuestTrackerEditModeTests` green; the existing classes below stay green.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green (run it by hand: the quick gate skips it).
- `bash client-unity/sync-net.sh --check` clean.

### Existing tests that pin this code (all stay green, none changed)
- `RoguehallaNet.Tests/HudRegionsTests.cs:246-337`: `TrackerDock` geometry (disjoint from the offer card and combat
  zone, header plus rows height, clamps rows 0..3, starts within one `TrackerGap` below the plate). Asserts the region maths this packet consumes; `HudRegions.cs` is not edited.
- `UnityProject/Assets/Tests/Editor/HudRegionsEditModeTests.cs:42-44`: region rects with `TrackerDock(200f, 2)` stay inside the stage.
- `HudVisualFix3EditModeTests.cs:181`, `HudVisualFix4EditModeTests.cs:67-145`, `Ui4EditModeTests.cs:43-54`: the hunt card
  (eligibility, title, placement under the terrain row, sizing). `Bottom` is additive.
- `HudTextScaleEditModeTests.cs:267`: no HUD label uses Ellipsis or Truncate; its fixture builds only `EnsureStage`,
  `CreateTopAndToastPills`, `CreateLegacyChrome`, `CreateCombatHud`, so the tracker is not in it.
- `HudLegacyLayoutEditModeTests.cs`: window layout at 1280x800 / 1920x1080 / 2560x1440 and the theme-colour check of HUD
  graphics; same limited fixture (no tracker). The tracker still uses theme colours only.
- `PlayerFlowsEditModeTests.cs`: whole-HUD rig with a real `MatchClient`; ticks `TickPlayerFlows` and finds paths under
  `MatchUI/HudStage/PlayerHUD/...`. The tracker adds a child under `PlayerHUD`.
- `QuestJournalViewEditModeTests.cs`: journal text including "Accepted · 12 / 45 s" style status; the journal is not edited.
- `UnityProject/Tools/hud-seams.test.cjs:109-125`: see section 7.

No conflict found between the packet and any existing test. No row in `docs/OWNER_DECISIONS.md` decides this subject.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/QuestTrackerTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestTrackerEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestTrackerEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-QUEST-TRACKER-1.md`

## 9. Validation commands
- `bash client-unity/sync-net.sh` then `dotnet test client-unity/RoguehallaNet.Tests --filter QuestTrackerTests`
- Unity: EditMode class `QuestTrackerEditModeTests` (lane helper `unity-class.sh`)
- `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-TRACKER-1 --base lane/ui-rework/base-2`

## 10. Risk and suggested model tier
implementer-sonnet. Hard parts: the chain-bottom and floor arithmetic, the fade states (keeping the last face while
leaving), a steady frame that allocates nothing, labels that fit without TMP cutting.

## 11. Known traps
- `ExpiresAt` is server time in ms, 0 for untimed; `Session.ServerTimeMs` is the newest snapshot's time. Use it as it is
  (the clock steps once a second, a snapshot arrives 20 times a second).
- `QuestRowView` objects are replaced with every new quest view: never keep one across a refresh; key rows by `Handle`
  if you need identity, and compare faces with `SameFace`.
- The plate's height changes (`SetPlateMode`, the extras toggle, `MatchHud.Legacy.cs:188-209`); the terrain row and the
  hunt card come and go. Read the chain bottom and the floor every refresh; re-place only when one of them or the row
  count changed.
- `HudFade` deactivates the root when the fade-out ends: do not also call `SetActive` on it. Show and hide through
  `Set` and `Snap` only. `HudFade` takes its first state from the root's `activeSelf` when built; build the root, then `Snap(false)`.
- `HudRegion.On` takes `(stageWidth, stageHeight)`; no overload takes a `RectTransform`.
- `HudUi.Label` grows the label's rect by `ceil(size * .45)` above and below; call `MythicTextScale.Refit()` after you
  resize a label's box.
- Tests are a separate assembly with no `InternalsVisibleTo`: a member a test reads or sets is `public`.
- Time in EditMode does not advance: the view takes `dt` as a parameter.
- The tallest chain is a Sentenced player choosing among five rivals with the extras open and a terrain chip: its
  bottom is near 459 on an 800-high stage, not 250.
- `HudRegions.cs` is not in scope (P-UI-ABILITY-ROW-1 edits it this wave).

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
