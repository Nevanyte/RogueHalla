# P-UI-TOAST-LANES-1: notices become small toasts in two lanes; the box in the middle of the map goes

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "make sure all in-game notifications are
  thoughtfully placed ... When a stat or quest is accepted there is a big ugly notification in middle of map".
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (the lane branch; the integration branch is `feature/unity-rework`)
- **D-rules touched:** D1: differs from legacy on purpose (legacy stacked every toast at the bottom centre, 124 up:
  `docs/rework/LEGACY_LAYOUT.md` section 8). The hand-off proposes a `DEVIATIONS.md` row (owner signs). D2 n/a. D5/D6/D9 none.
- Evidence: `.agent-runs/lane-ui-rework/understand/notifications.txt` (main checkout, read-only).

## 1. ID and outcome

Every server notice (quest accepted, quest complete, stat trade, kill-stack grant, potion notices, pickup and chest
refusals, wind emblem, cannon notices) is appended to `OrderedNoticeFeed` (`UI/OrderedNoticeFeed.cs`) and shown in
`RecentServerNotices` (created at `OrderedNoticeFeed.cs:48`): a framed scroll box at canvas fractions (.405,.735)-(.665,.845),
about 499 x 119 px at 1080p in the upper middle of the screen. It shows the whole numbered history, stays 8 s
(`RecentSeconds`, `:32`), appears and vanishes with no fade, takes the mouse and the wheel, and its timer runs while it is
hidden. The pill built for toasts (`ToastStack`, `MatchHud.TopCentre.cs:30`, bottom centre) is fed only by the first-run
hint and the resume status (`_feedback`) and the pickup chip covers it.

When this lands:

1. **`RecentServerNotices` is removed.** Nothing but the lanes below shows a new notice in play. The Notices page (J,
   Notices tab) keeps the list.
2. **Two toast lanes**, each a column of small pills, placed from `HudRegions` (`RoguehallaNet/Net/Ui/HudRegions.cs`):
   - **Event lane**, top centre under the match timer: slot `s` is `HudRegions.EventPill(s, width, topCentreBottom)`
     (`HudRegions.cs:114`); slot 0 is the top one, so the oldest line is on top and the newest at the bottom; at most
     `HudRegions.EventMax` (3) lines. `topCentreBottom` is the bottom edge of the top-centre pill that is active (the timer
     pill or the connection banner: `BannerTop + its sizeDelta.y`; 0 when neither shows, and `EventPill` then starts at
     `EventTop`). The value may be one frame old.
   - **Feedback lane**, bottom centre above the ability row: slot `s` is `HudRegions.FeedbackPill(s, width)`
     (`HudRegions.cs:110`); slot 0 is the LOWEST pill, so the oldest line (and the sticky line of item 6) is nearest the
     ability row and a newer line appears above it; at most `HudRegions.FeedbackMax` (2) lines.
   - A pill is `HudRegions.PillHeight` (24) high. Its width is
     `min(HudRegions.LaneWidth(stageWidth), ceil(preferredTextWidth / MythicTextScale.WidthShare) + 28)`: the label inside is
     inset 14 on each side and its fit wants the text within 88 percent of its box. Set the pill's width BEFORE the label's
     text, or call the label's `MythicTextScale.Refit()` after resizing (the fit runs when the text changes, not when the box
     does).
   - Text: one line, `theme.SizeBody`, through `HudUi.Label`, `MinSize = 9` on its `MythicTextScale`. Nothing is cut by TMP:
     `MythicTextScale` forces overflow mode Overflow and `HudTextScaleEditModeTests` reports a label that uses Ellipsis or
     Truncate. A text longer than `NoticeRouter.MaxChars` (80) is shortened in the router (it ends in "…"); the label's fit
     shrinks what is left into the pill.
   - Surface, border and corner radius from the theme as the old toast pill (`HudUi.Surface(..., raycast: false)`,
     `MatchHud.TopCentre.cs:31`). A 3-unit strip at the left edge carries the tone colour (`UiTheme`: info = `Accent`,
     good = `Success`, warn = `Warning`, bad = `Danger`).
   - Nothing in a lane takes the pointer: no raycast target, `CanvasGroup.blocksRaycasts` false.
   - On a stage so narrow that an event pill's left edge would be left of the limit `PlacePhasePill` computes for the timer
     pill (`MatchHud.TopCentre.cs:85`: the plate's right edge plus 8, `PlateLeft + HudLayout.PlateWidth(uiWidth) + 8`), that
     pill is moved right by the difference.
3. **A line's life** (pure model): fade in `0.12 s`, hold, fade out `0.25 s`. Hold is
   `clamp(2.2 + 0.045 * max(0, characters - 20), 2.2, 4.5)` seconds.
   - **Same text again:** a notice with the same text as a line still on screen or waiting does not add a line: that line
     shows ` x2`, ` x3`, ... after its text and its hold starts again.
   - **Same family again:** a notice whose route carries a `Key` replaces the text of the line on screen or waiting that has
     the same key, in place, with no counter, and restarts its hold. Kill-stack grants use one key.
   - **A full lane:** a new line waits. The oldest line on screen that is not sticky starts its fade out as soon as it has
     been fully visible for `MinShowSeconds` (1 s), and the first waiting line takes the freed slot. At most `MaxWaiting` (6)
     lines wait; a seventh drops the oldest waiting one (it is in the Notices page).
   - A line that changes slot glides there in 0.12 s. Reduced motion: no fade, no glide (alpha 0 or 1, position jumps).
4. **Lanes pause while hidden.** While the world HUD is not visible (`WorldHudVisible` false: Tab map, journal, any modal,
   results) the lanes are not drawn and no line's timer runs; notices that arrive wait in the model and are shown, oldest
   first, when the HUD is back.
5. **Which lane, tone and family** is one table in the pure router (below).
6. **The first-run hint and the resume status** (`_feedback`, written by `MatchHud.Tutorial.cs:45` and
   `MatchHud.Resume.cs:183`) are a *sticky* line in the feedback lane: it stays while the text is set, changes in place when
   the text changes, and fades out when it is cleared. The old `ToastStack` pill, its `Notice` label and the fields
   `_notice` and `_noticePill` are removed. Every place that cleared `_notice` (`ResetTransientPresentation`
   `MatchHud.cs:208`, `ShowMenu` `:200`) clears the sticky line.
7. **The history is bounded**: `OrderedNoticeFeed` keeps the newest 200 entries. Numbering stays the absolute count
   ("201 · text"). `Entries` returns what is kept.
8. Player wording for the two refusals: `"Can't pick that up"` and `"That choice was refused"` (the Notices page keeps
   today's line with the reason code, `OrderedNoticeFeed.cs:117-118`).
9. Unchanged: the Notices page and its tests of order, duplicates and epochs; sound; the connection banner and the timer
   pill; what the server sends.

## 2. Dependencies

`HudRegions` (P-UI-HUD-REGIONS-1), `UiMotion.ReducedOverride`, and the `MatchHud.Notices.cs` / `MatchHud.TopCentre.cs`
extraction (P-UI-HUD-SEAMS-1): all on the base. Another packet of the wave (P-UI-ABILITY-ROW-1) amends another test of
`HudLegacyLayoutEditModeTests`; P-UI-QUEST-TRACKER-1 adds three other lines to `MatchHud.PlayerFlows.cs`; merges take both.

## 3. Relevant legacy paths

- `docs/rework/LEGACY_LAYOUT.md` section 8: legacy stacked all toasts bottom centre, 124 up. Intentional difference (D1).
- `packages/` is not touched; no legacy rule is involved (presentation only, every outcome drawn from server observations).

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/NoticeRouter.cs`, `client-unity/RoguehallaNet/Net/Ui/ToastLaneModel.cs` (pure, namespace
  `Roguehalla.Net.Ui`) and their synced copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (with `.meta`).
- `client-unity/UnityProject/Assets/Scripts/UI/ToastLane.cs` (+ `.meta`).

### API (pure layer)

`NoticeRouter.cs`:
```csharp
public enum NoticeLane : byte { None = 0, Event = 1, Feedback = 2 }
public enum NoticeTone : byte { Info = 0, Good = 1, Warn = 2, Bad = 3 }
public readonly struct NoticeRoute { public readonly NoticeLane Lane; public readonly NoticeTone Tone;
    public readonly string Text; public readonly string Key; }     // Text null when the Unity side supplies the words (potions)
public static class NoticeRouter
{
    public const int MaxChars = 80;
    public const string KillStackKey = "kill-stack";
    public static NoticeRoute Route(WireEvent item);          // Lane None for kinds that show no toast
    public static float HoldSeconds(string text);             // the clamp formula above
    public static string Short(string text);                  // at most MaxChars characters, the last one "…" when cut
}
```
The table (`EventKind` in `Protocol/WireEvents.cs:7`, `PotionNotice` in `Net/Views.cs:43`: Stored=1, BagFull=2, Used=3,
Blocked=4, Busy=5, Full=6, Empty=7, LargeStored=8, LargeUsed=9, CampBagFull=10):

| Event | Lane | Tone | Text | Key |
|---|---|---|---|---|
| `Toast` with null or empty text | None | | | |
| `Toast` starting "Quest complete" | Event | Good | the server's text | |
| `Toast` starting "Quest accepted" or "Accepted:" | Event | Info | the server's text | |
| `Toast` starting "Stat trade" | Event | Good | the server's text | |
| `Toast` containing "kill stack" | Event | Good | the server's text | `KillStackKey` |
| any other `Toast` | Event | Info | the server's text | |
| `PotionNotice` `Stored`, `LargeStored` | Event | Good | null | |
| `PotionNotice` `Used`, `LargeUsed` | Feedback | Good | null | |
| `PotionNotice` `BagFull`, `CampBagFull`, `Blocked`, `Busy`, `Full`, `Empty` | Feedback | Warn | null | |
| `PotionNotice` any other value | None | | | |
| `PickupRefused` | Feedback | Warn | "Can't pick that up" | |
| `ChestRefused` | Feedback | Warn | "That choice was refused" | |
| `WindEmblem` | Event | Info | "Wind emblem" | |
| `CannonNotice` code 1 | Feedback | Warn | "Cannon sealed" | |
| `CannonNotice` code 2 | Feedback | Warn | "Cannot enter cannon" | |
| `CannonNotice` code 3 | Event | Info | "Cannon blast" | |
| `CannonNotice` other | Feedback | Info | "Cannon reloading · Ns" (no number when the seconds are absent) | |
| every other kind | None | | | |

- Prefix and contains tests are ordinal and case-sensitive, on the text as delivered. The server words a single grant
  "…: +1 kill stack · …" and several "…: +2 kill stacks · …" (`crates/rh-sim/src/modifiers/farm_text.rs`): "kill stack"
  matches both. The kill-stack test is applied AFTER the prefix tests of the rows above it only where the table order says
  so: a text starting "Quest complete" is Good without a key even if it also contained "kill stack" (table order).
- The cannon words are copies of `OrderedNoticeFeed.TryText` (`OrderedNoticeFeed.cs:120-124`, private, Unity assembly). Read
  there: code 1 "Cannon sealed", 2 "Cannot enter cannon", 3 "Cannon blast", otherwise `"Cannon reloading · " + CannonSeconds +
  "s"` (a null `ushort?` prints as nothing, so history shows "Cannon reloading · s"). The router differs on purpose in one
  place: when the seconds are absent it shows "Cannon reloading" with no number and no stray "s" (the table above). The code
  wins elsewhere; the words above equal the table.
- `PotionNoticeFormatter` (Unity assembly) gives a potion notice its words: the router returns lane and tone and a null
  text, and `MatchHud.Notices.cs` takes the words from `PotionNoticeFormatter.Format`.
- Server texts are passed through `Short`.

`ToastLaneModel.cs`:
```csharp
public readonly struct ToastLine { public readonly string Text; public readonly int Count; public readonly NoticeTone Tone;
    public readonly float Alpha; public readonly float Slot; public readonly bool Sticky; }   // Slot 0 = the lane's first position, fractional while gliding
public sealed class ToastLaneModel
{
    public const float FadeInSeconds = .12f, FadeOutSeconds = .25f, GlideSeconds = .12f, MinShowSeconds = 1f;
    public const int MaxWaiting = 6;
    public ToastLaneModel(int capacity);
    public void Push(string text, NoticeTone tone, string key = null);   // null or empty text is ignored
    public void SetSticky(string text, NoticeTone tone);            // null or empty clears it; a sticky line takes slot 0 and counts toward capacity
    public void Tick(float dt, bool visible, bool reducedMotion);   // timers run only while visible
    public void Clear();
    public int Count { get; }                                       // lines that are drawn (alpha may be 0 on the first frame)
    public ToastLine Line(int index);                               // index 0 is the oldest drawn line
    public int Waiting { get; }
}
```
`ToastLine.Text` already carries the " xN" suffix. No allocation in `Tick` when nothing changes (fixed arrays or pre-sized
lists; a line's display text is built only when its text or count changes).

### New Unity file `UI/ToastLane.cs`

`public sealed class ToastLane` (not a MonoBehaviour): built with the stage, a name, the lane kind and a capacity; owns its
pooled pills, built once, inactive, each label with a build-time box `HudRegions.LaneMaxWidth` wide (so the text-scale
matrix can fit-test it before any toast shows); `Present(ToastLaneModel model, float stageWidth, float topCentreBottom, float
minLeft)` places and paints them (position from `HudRegions`, slot glide from `ToastLine.Slot`, alpha on a `CanvasGroup` per
pill, text and tone strip only when they change; `minLeft` is the narrow-stage rule, 0 for the feedback lane);
`SetVisible(bool)`. Object names: `EventLane` and `FeedbackLane` under the HUD stage, pills `Toast0`, `Toast1`, ... with a
child label `Text`.

## 5. Exclusive write scope

- new files of section 4 (library, synced copies, `ToastLane.cs`, `.meta` files)
- `UI/OrderedNoticeFeed.cs`, `UI/MatchHud.Notices.cs`, `UI/MatchHud.TopCentre.cs`
- `UI/MatchHud.cs`: only the `_notice` declaration (`:22`) and its three uses (`ShowMenu` `:200`,
  `ResetTransientPresentation` `:208`, `HudOwnerRefresh` `:278`); `UI/MatchHud.Legacy.cs`: only the `_noticePill` field
  (`:51`); `UI/MatchHud.PlayerFlows.cs`: only the `SetRecentVisible` line (`:70`); `UI/MatchHud.Tutorial.cs`,
  `UI/MatchHud.Resume.cs`: only if a line there touches `_notice` directly (today none does: they write `_feedback`)
- Not in scope: `UI/UiMotion.cs`, `HudRegions.cs`, `MatchHud.ActionBar.cs`, `MatchHud.QuestRewards.cs`,
  `MatchHud.Visibility.cs`, `MatchHud.Features.cs`, the Tab map files (other packets of this wave own them).
- Oracle files are read-only (listed below). The `stub:` commit's stubs are the implementer's to replace.

### Wiring

- `MatchHud.TopCentre.cs`: `CreateTopAndToastPills` keeps its name; it no longer builds `ToastStack` and instead calls a new
  `CreateToastLanes()` (defined in `MatchHud.Notices.cs`), so the lanes exist wherever the top-centre pills do (the existing
  layout fixtures build the HUD from `CreateTopAndToastPills`, `CreateLegacyChrome` and `CreateCombatHud` and never call
  `CreateNoticeFeed`). `SyncTopCentre` no longer toggles the old pill and exposes the active top-centre pill's bottom edge.
  The constant `ToastBottom` goes.
- `MatchHud.Notices.cs`: owns the two models and the two lanes. `CreateNoticeFeed()` (name kept) subscribes to the feed:
  every entry it accepts is routed with `NoticeRouter.Route` and pushed. `TickNotices()` (name and signature kept: a node test
  matches the literal call `TickNotices();`) ticks both models with `NoticeDtOverride ?? Time.unscaledDeltaTime`,
  `WorldHudVisible` and `UiMotion.Reduced`, then presents both lanes. `public static float? NoticeDtOverride` is the test seam
  (null in play). A new `public void SetStickyNotice(string text)` replaces the writes to `_notice`.
- `OrderedNoticeFeed.cs`: remove the recent card, `RecentSeconds`, `SetRecentVisible`; add `public event Action<WireEvent>
  Accepted` (raised for each entry it accepts) and `public event Action Cleared` (raised from `Clear()`), and the 200 cap.
  `MatchHud.Notices.cs` clears both models on `Cleared`, so the two places that clear the feed on bind and on a timeline
  reset (`MatchHud.PlayerFlows.cs:89`, `:147`) need no edit.
- `MatchHud.PlayerFlows.cs`: delete the one call `_orderedNotices.SetRecentVisible(false)` (`:70`). Nothing else there.

## 6. Contracts and references

- Presentation only: no wire, protocol, `crates/`, `packages/`, `parity/` change (C-AUTH: Unity holds no game rule).
- `docs/agent` lane rules (`.agent-runs/lane-ui-rework/specs/COMMON.md`): `HudUi.Label` for text, theme colours only,
  unscaled time, instant under reduced motion, no steady-frame allocation, new C# file has a `/// <summary>` and at most 800
  lines, no line added to `View/ArenaView.cs` or `Presentation/SpriteActorPresenter.cs`.

## 7. Behaviour to preserve

- The Notices page, its text and its tests of order, duplicates and epochs (`PlayerFlowsEditModeTests` uses only
  `ServerNotices.Entries` and `ServerNotices.Root`; `Entries` order and the text of every entry are unchanged below the 200 cap).
- Sound, the connection banner and the timer pill, what the server sends.
- `MatchHud` binds and unbinds sessions many times: both models are cleared whenever the feed is (the `Cleared` event); keep
  the `Session.CueSent` subscription and the press pulses of P-UI-INPUT-CUES-1.
- `hud-seams.test.cjs` names: `CreateTopAndToastPills`, `CreateNoticeFeed`, `TickNotices`, `RotateNoticeEvents`, the field
  `_orderedNotices` keep their names and files.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TOAST-LANES-1 --base lane/ui-rework/base-2` passes.
- `dotnet test client-unity/RoguehallaNet.Tests --filter "NoticeRouterTests|ToastLaneModelTests"` green.
- Unity EditMode `ToastLanesEditModeTests` green; `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`,
  `Ui4EditModeTests`, `PlayerFlowsEditModeTests`, `HudRegionsEditModeTests` green; `node --test
  client-unity/UnityProject/Tools/hud-seams.test.cjs` green.
- `bash client-unity/sync-net.sh --check` clean.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/NoticeRouterTests.cs` (dotnet, 33 cases: one per table row, hold, shorten)
- `client-unity/RoguehallaNet.Tests/ToastLaneModelTests.cs` (dotnet, the lane state machine)
- `client-unity/UnityProject/Assets/Tests/Editor/ToastLanesEditModeTests.cs` and its `.meta` (13 EditMode tests)
- amended in their own `oracle-amend:` commits: `client-unity/UnityProject/Assets/Tests/Editor/HudLegacyLayoutEditModeTests.cs`,
  `client-unity/UnityProject/Assets/Tests/Editor/HudTextScaleEditModeTests.cs`,
  `client-unity/UnityProject/Assets/Tests/Editor/Ui4EditModeTests.cs`, `client-unity/UnityProject/Tools/hud-seams.test.cjs`
- stubs (`stub:` commit, the implementer replaces them): `client-unity/RoguehallaNet/Net/Ui/NoticeRouter.cs`,
  `ToastLaneModel.cs`, their synced copies and `.meta` files, and `MatchHud.NoticeDtOverride` / `SetStickyNotice` in
  `UI/MatchHud.Notices.cs`.

## Existing tests this changes (amended by the oracle author, each in its own `oracle-amend:` commit; listed for the owner)

Every other reference was searched for (`RecentServerNotices`, `SetRecentVisible`, `RecentSeconds`, `ToastStack`,
`ToastBottom`, `_noticePill`, `_notice`, `"Notice"` in `Assets/Tests/Editor` and `Tools/*.test.cjs`); these are all there is:

1. `HudLegacyLayoutEditModeTests.BannerAndToastsUseTheTopCentreAndBottomOneTwentyFourAnchors`
   (`Assets/Tests/Editor/HudLegacyLayoutEditModeTests.cs:224`). Asserted: banner top 12 and centred; `ToastStack` bottom 124
   and centred; toast clears the slot row. Now: banner part unchanged; the toast part asserts `FeedbackLane/Toast0` exists and,
   with a sticky notice set (`SetStickyNotice`, a tick of `TickNotices` with `NoticeDtOverride`), measures at bottom
   `HudRegions.FeedbackBottom` and centred, above the slot row. Renamed `BannerAndStickyToastUseTheTopCentreAndFeedbackAnchors`.
   (P-UI-ABILITY-ROW-1 amends another test of this file in the same wave.)
2. `HudTextScaleEditModeTests` (`:147`): the row `Add("Notice", (TMP_Text)GetField("_notice"), ...)` becomes `Add("Notice",
   Find(stage, "FeedbackLane/Toast0/Text"), <same sample text>)`. One row for one row: the inventory stays 63.
3. `Ui4EditModeTests.RecentNoticeCardExpiresButTheHistoryKeepsEveryNotice` (`Assets/Tests/Editor/Ui4EditModeTests.cs:79`):
   asserted the card (`SetRecentVisible`, `RecentServerNotices`, `RecentSeconds == 8`). Removed; the history is covered by
   `TheHistoryKeepsTheNewestTwoHundred`.
4. `client-unity/UnityProject/Tools/hud-seams.test.cjs` (`:27` the `ToastBottom` row; `:124` the
   `SetRecentVisible(_session != null && WorldHudVisible)` match): both dropped. Line `:119` (flows must not call
   `SetRecentVisible` inline) stays.

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "NoticeRouterTests|ToastLaneModelTests"`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ToastLanesEditModeTests,HudLegacyLayoutEditModeTests,HudTextScaleEditModeTests,Ui4EditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`

## 10. Risk and suggested model tier

Sonnet implementer: a pure state machine (tested in seconds with dotnet) plus presentation wiring. The hard parts are the
lane model's rules in a full lane, and layout that must equal `HudRegions` to 0.5 units.

## 11. Known traps

- `Events()` runs for every batch; routing must not allocate per event beyond the line it creates.
- `EveryHudGraphicUsesAColourFromTheTheme` checks every HUD graphic: the tone strip colours must be theme colours.
- The top-centre pills are mutually exclusive (`SyncTopCentre`): `topCentreBottom` is the bottom of whichever is active (the
  banner is 28 high at `BannerTop` 12 at build time (`MatchHud.TopCentre.cs:20`; spec said 40, the code wins), the phase pill
  26 at build time and resized by `ApplyPhaseStrip`). `PlacePhasePill` moves the timer pill only in x. Read `sizeDelta.y`.
- `HudUi.Label` grows a label's rect by `ceil(size * .45)` above and below and adds `MythicTextScale` with `FitBox`: the pill
  stays 24 high; do not fight the grown label rect.
- A sticky line and capacity: with a sticky line present the feedback lane shows at most one more line.
- `HudRegion.On` and `HudRegions.CombatZone` take `(stageWidth, stageHeight)`; no overload takes a `RectTransform`.
- Tests are a separate assembly with no `InternalsVisibleTo`: `NoticeDtOverride` and `SetStickyNotice` are public.
- Time in EditMode does not advance: only `NoticeDtOverride` drives it (and `UiMotion.ReducedOverride`); the tests restore both.
- `HudFade` takes its first state from the root's `activeSelf` when built; use `Set`/`Snap` only (if the implementer uses it).
- Capture tools copy `HudLayout.MinimapSize` and the Tab-map layout: this packet changes neither.
- Run `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` yourself if a `MatchHud` member moves.

## Notes from the oracle author

Red on the stub, and on what:
- dotnet `NoticeRouterTests` and `ToastLaneModelTests`: all 60 cases fail on assertions (lane / text / hold / `Count` are the
  stub defaults). The tests were also run against a throw-away reference implementation (not committed): all 60 pass, so the
  timelines in them are consistent with a model that follows section 1 item 3.
- `ToastLanesEditModeTests`: all 13 fail on assertions; 12 on "EventLane / FeedbackLane exists under the HUD stage" (the
  lanes do not exist), `TheHistoryKeepsTheNewestTwoHundred` on `Expected: 200 But was: 250`. Only the first half of each test
  could be run against the stub; the pill-level assertions (positions, alpha, activeInHierarchy of pills, sticky feedback via
  `_feedback`) were written from the code but not run against an implementation. If one is wrong about the rig and the
  implementer is sure, report it instead of working around it.
- The amended tests are red on the stub for the same reason (no `FeedbackLane/Toast0`).

Where the spec and the code differ, or I chose:
- The spec says the banner is 40 high; the code builds it 28 high (`MatchHud.TopCentre.cs:20`). The tests compute
  `topCentreBottom` as `12 + sizeDelta.y` of the active banner or phase pill, so either height works.
- Cannon reloading without seconds: the router gives "Cannon reloading" (history shows today's "Cannon reloading · s").
- Time: the EditMode tests drive lane time through `MatchHud.NoticeDtOverride` only; `Frame()` sets `_dirty` and invokes
  `LateUpdate`, so `TickNotices` runs through `TickFeatures` -> `TickPlayerFlows` (`MatchHud.PlayerFlows.cs:157`).
- Model details pinned beyond the spec's wording (all follow from it): a same-text or keyed push never makes the line flicker
  (alpha stays 1); the sticky line keeps `Slot` 0 and the next line is `Slot` 1; a line pushed while hidden has alpha 0 until
  the lane is visible; reduced motion shows a line at once and a waiting line takes a freed slot after the first second.
- `ToastsWaitWhileTheTabMapIsOpen` uses `TacticalMap.SetOpen(true)` (public) to make `WorldHudVisible` false.
- The `HudTextScaleEditModeTests` amendment keeps the id "Notice" for the lane label row.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
