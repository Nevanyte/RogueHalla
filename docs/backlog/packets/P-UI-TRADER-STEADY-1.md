# P-UI-TRADER-STEADY-1: a trader's step no longer rebuilds the Traders window

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane: "make the front end as fluid as possible", "do a UI
  optimization sweep"). It removes the bursts of garbage the lane measured in the recorder section `netPoll` (the coordinator's
  open finding A).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `9c9e4fa9c`)
- **D-rules touched:** D1: presentation only; legacy rebuilds its trader buttons only when a key changes and the trader's position
  is not in the key (`packages/client/src/ui/traderPanel.ts:47-48`, the `key` and `if (key === this.key) return`), so this moves
  toward legacy; no `DEVIATIONS.md` row. D2 n/a. D5/D6/D9 none (no key is bound, no text or layout changes).
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-d`

## 1. ID and outcome

What is wrong today. Measured (two Web runs, 2026-11-23): in stretches of a match the recorder section `netPoll` allocates 7 to
25 KB of managed memory per frame and Unity creates about 100 to 130 objects per State frame; outside those stretches it stays
under 3 KB. The stretches do not follow the window kind, the zone or the actor count.

The chain, read in the code on this base (cause read, not yet run: test 1 below is its proof; if it is GREEN on the base, stop
and report, the cause is another one):

1. The server moves a visible trader on every tick (`crates/rh-sim/src/traders/runtime.rs:167`, `TRADER_SPEED`) and re-sends
   the Trade item when a merchant's position changes, so a State frame with a new `TradeView` arrives on every tick while a
   trader in sight walks (the net library builds a new `TradeView` per Trade item: `RoguehallaNet/Net/Collections.cs:601-604`).
2. `MatchHud`'s collections handler calls `RefreshPlayerFlows`, which calls `_traderDialog?.Refresh()` whether or not the window
   is open (`UI/MatchHud.PlayerFlows.cs:95-102`).
3. `TraderDialogView.Refresh` (`UI/TraderDialogView.cs:123-190`) treats a new view object as identical only when
   `TradeSignature(current) == _renderedSignature` (`:152-153`). `TradeSignature` (`:236-246`) appends every merchant's `X` and
   `Y` (`:241`), so a walking trader changes the signature on every delivery.
4. A changed signature runs `RebuildMerchants` (`:254`) and `RebuildChoices` (`:279`) (or `UpdateSelectedDetail`, `:417`):
   `HideRows` (`:468`) destroys every row and `MythicUi.Button` builds each one again (about 21 to 25 Unity objects a row; two
   merchants and three choices are five rows).

Nothing in the window shows a merchant's position: `X` and `Y` are read only at `:241`. The comment at `:149-151` says what the
identical check is for: a rebuild resets hover, keyboard focus and the colour tween. So with the window OPEN and a trader
walking, the rows are rebuilt about 20 times a second: a focused row loses its focus, a hovered row its hover, the list its
scroll position. A second cost stays after the position is out of the key: `TradeSignature` builds a string (a `StringBuilder`,
a `ToString("R")` per number) on every delivery of a new view object.

Outcome:

1. **A delivery that differs from the last built view only in merchants' positions is an identical replacement.** The rows,
   their order, their names (`Merchant_<i>`, `Choice_<i>`), the selected merchant, keyboard focus, hover, the colour state and
   the list's scroll position are untouched; no Unity object is created or destroyed; no text is written. The view object the
   rows send through is still re-bound to the new view (`_viewRef.View = current`, `:156`), exactly as the identical branch
   does today.
2. **What makes a replacement different is unchanged otherwise:** `Available`, `TradeUsed`, `BarterReadyAt`, the merchants'
   count, order, `Kind` and `Name`, every offer's `Cost`, `Gain`, `CostValue`, `GainValue` and `Label`, every stat row's `Stat`
   and `Value`, and the barter labels. Any of these changing rebuilds as today.
3. **The comparison builds no string.** Compare the fields of the new view with the view the rows were last built from (keep
   that view object in its own field; it is small). `TradeSignature` and `_renderedSignature` go. A delivery of outcome 1
   allocates nothing in `Refresh` (test 5).
4. Nothing else changes: no text, no layout number, no key, no handler. The window still refreshes while closed (a rebuild
   while closed stays as it is; this packet only stops the rebuilds that have no cause).

## 2. Dependencies

Base `lane/ui-rework/base-3` (it holds `AllocationMeter` with `Samples`, `Assets/Tests/Editor/AllocationMeter.cs`, and
`P-UI-FLOWS-LEFTOVERS-1`, which changed the scroll behaviour this packet must keep).

## 3. Relevant legacy paths
- `packages/client/src/ui/traderPanel.ts:47-48`: `key` is built from trader id, held weapons, potion count, `tradeUsed`, the offer
  labels and the pending flag; `if (key === this.key) return;` skips the rebuild. No position in the key (verified by reading).

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/TraderSteadyEditModeTests.cs` (+ `.meta`): the oracle (this packet's author).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/TraderDialogView.cs`: `Refresh`, the comparison that replaces `TradeSignature`,
  the fields it needs, and (only if test 5 shows it) the "N traders in sight" status text path. Keep the file under 800 lines
  (it has 639).
- Nothing else. No `MatchHud*` file, no net library file, no recorder file, no existing test.

## 6. Contracts and references
- Presentation only; Unity holds no game rule (R-AUTH). No wire, protocol or `crates/` change.
- Lane rules: `.agent-runs/lane-ui-rework/specs/COMMON.md` (the implementer need not read it beyond what is here: no allocation in
  a steady frame, `MatchHud` keeps its subscriptions, the `hud-seams.test.cjs` names stay; this packet touches none of them).

## 7. Behaviour to preserve
- The stale-row guard: `IsCurrentView` (`:248-252`) compares object identity of `session.Collections.Trade`, `_renderedTrade`
  and `_renderGeneration`. A moved-only delivery replaces `_renderedTrade` and re-binds `_viewRef.View` but does NOT bump
  `_renderGeneration` (as today's identical branch). Test 3 guards it.
- Rows capture `_viewRef` in their click handlers (`:262`, `:281`): keep `_viewRef.View = current` on the moved-only branch.
- `_renderedTrade` is replaced on every delivery (it is what the stale-row guard compares). The comparison needs the view the
  rows were BUILT from: keep it in its own field, set where the rebuild happens. Never compare with `_renderedTrade` after it was
  overwritten.
- `Refresh` also runs `SetState`, `UpdateButtonAvailability`, `ReflowIfLayoutChanged` and `UpdateDeadlineSummaryIfNeeded` on
  every call. The status text "N traders in sight" is built by concatenation at `:180` (`current.Merchants.Count + " traders in
  sight"`): it allocates on every call for two or more merchants, so test 5 will show it. Make that path allocation-free
  without changing any text (compare before building, or keep the built string with the count it was built for).
- The session's timeline epoch change (`:135-143`) and a null session (`:126-133`) clear the presentation: clear the built-from
  view there too, so the first delivery afterwards rebuilds.
- `float` and `double` fields are compared with `==` (the wire delivers the same bits for an unchanged value); no tolerance.
- Existing tests that move a merchant to force a rebuild (listed below) stay green.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-TRADER-STEADY-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `TraderSteadyEditModeTests`: all green.
- The classes `TraderDialogViewEditModeTests`, `TraderRowsRealOrderEditModeTests`, `TraderRowsWholeHudEditModeTests`,
  `TraderCopyEditModeTests`, `TraderWordsEditModeTests`, `FlowsLeftoversEditModeTests`, `PlayerFlowsEditModeTests` stay green.

## Existing tests that pin this code

| Test | What it asserts | Effect of this packet |
|---|---|---|
| `TraderDialogViewEditModeTests.AnIdenticalTradeReplacementKeepsTheRowsAndStillSendsThroughTheCurrentView` | same-content new view object keeps `Merchant_0` and `Choice_0` (`AreSame`), a trade still sends, a changed offer label rebuilds (`ChoiceLabel(0)` contains "Different") | unchanged, green |
| `TraderDialogViewEditModeTests.CurrentOptionsQueueEveryOrdinaryStatPickAndMetadataReplacementInvalidatesOldCallbacks` | after a re-delivery the old view's callback is refused by `TryTradeBound` (stale guard), every current pick is sent | unchanged, green |
| `TraderDialogViewEditModeTests` (the other four) | unavailable / empty / no-match states, source-order labels and signed deltas, `TradeUsed` blocks stat only, disconnect and rebind clear rows | unchanged, green |
| `TraderRowsRealOrderEditModeTests.AReopenedWindowIsRightAfterARebuildWhileClosed` (line 191: `rig.Deliver(stat, 3.5)`) | after a rebuild while closed, the reopened window's rows satisfy the height formula and the stat list stands at the top | its set-up caused the rebuild by a MOVED merchant, which no longer rebuilds: **amended** (below) |
| `TraderRowsRealOrderEditModeTests` (the others), `TraderRowsWholeHudEditModeTests.TheWholeHudRealChain` | row heights, caches (`_fitCache`, same entries after "the same trade in a new view object"), hidden window measures nothing | unchanged, green. `LaterCallsChangeNoRowAndMeasureNothingAgain` already delivers an unchanged new view object |
| `FlowsLeftoversEditModeTests.TraderListTopEditModeTests.EveryOpeningStartsAtTheTop` (line 523: `rig.Deliver(true, 3.5)`) | after a delivery while closed, the list opens at the top; reopening again at the top | stays green, but its moved merchant no longer rebuilds, so it no longer exercises "a rebuild while closed" (opening sets the list to the top by itself, `:113`, `:118`). NOT amended (outside this packet's scope); flagged for the owner |
| `FlowsLeftoversEditModeTests.TraderListTopEditModeTests.ARebuildWhileOpenKeepsThePosition` (line 566: `rig.Deliver(true, 3.5)`) | after a delivery while open, the scroll position stays .4 | stays green, but now passes because nothing rebuilds, not because a rebuild keeps the place. NOT amended; flagged for the owner (test 2 of this packet covers the same guarantee for the moved case; the "rebuild keeps the place" guarantee stays covered by `ReadList`/`PlaceList` being untouched) |
| `PlayerFlowsEditModeTests`, `TraderCopyEditModeTests`, `TraderWordsEditModeTests`, `UiLabFixesEditModeTests` | trader content, copy and words through the HUD | unchanged; none moves a merchant between deliveries (checked with a search for `X =` / `dx` deliveries) |

No test reads `TradeSignature` or `_renderedSignature` (`grep` over `Assets/`: only `TraderDialogView.cs` names them).

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/TraderSteadyEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TraderSteadyEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/TraderRowsRealOrderEditModeTests.cs` (the `oracle-amend:` commit, one line; see below)
- `docs/backlog/packets/P-UI-TRADER-STEADY-1.md`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-d TraderSteadyEditModeTests`
- the same with `TraderDialogViewEditModeTests,TraderRowsRealOrderEditModeTests,TraderRowsWholeHudEditModeTests` and
  `TraderCopyEditModeTests,TraderWordsEditModeTests,FlowsLeftoversEditModeTests,PlayerFlowsEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-TRADER-STEADY-1 --base lane/ui-rework/base-3`
  (the lane helpers are in the main checkout; the lane spec's folder is read-only for you).

## 10. Risk and suggested model tier
Sonnet. One file, one method. The risks are the stale-row guard (do not bump `_renderGeneration` on a moved-only delivery, do not
leave `_viewRef` on the old view) and the status string (a concatenation allocates on every call with two or more merchants).

## 11. Known traps
- Rows capture `_viewRef` in their click handlers: keep the re-bind line.
- Compare with the built-from view, not `_renderedTrade`.
- `Refresh` runs more than the rebuild branch: test 5 measures all of it with the window closed.
- Clear the built-from view on a null session and on a timeline-epoch change.
- No Assert.Multiple, no InternalsVisibleTo (the tests use reflection); EditMode time does not advance.

## Not established
- That the Traders window is the source of the measured bursts: read, not run. Test 1 settles it for the rebuild; whether the
  bursts are gone in a real build is for the lane's next Web measurement (the recorder's `instanceIdDeltaPerSecond` and the
  `netPoll` bytes in windows where a trader walks in sight).
- What else allocates per delivered Trade item outside this file (the map's marks, the journal): not looked at here.

## Notes from the oracle author
(filled in below once the tests have run)
