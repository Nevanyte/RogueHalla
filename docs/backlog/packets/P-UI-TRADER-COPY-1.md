# P-UI-TRADER-COPY-1: the trader window speaks about traders, not about the server

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "Please do a UI optimization sweep."
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-2` (no other packet touches this file or its tests)
- **D-rules touched:** D1 presentation, player-facing strings only · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
Today the trader window's status line describes the architecture ("Server trade view · choices are server-authoritative",
"Waiting for an authoritative trade view.", "No visible merchant is present in the delivered view.") and the choice
buttons are labelled "1 · ...", "2 · ...", "3 · ..." although no digit key trades (digits belong to the chest picker).
This packet changes strings only: no layout, rule, rect, object name or behaviour. When it lands the window says
"Looking for traders…", "N traders in sight", "Trading…" and so on, and a button shows its offer alone.

Replacements (find each by its "Today" string in `client-unity/UnityProject/Assets/Scripts/UI/TraderDialogView.cs`,
line numbers are of the base):

| Where | Today | Becomes |
|---|---|---|
| `Status` initial text (:61) and `SetState` in `Initialize` (:87) | "Awaiting server trade view" | "Looking for traders…" |
| `ClearTradePresentation` on bind (:102) and on a new timeline (:138); `_empty` with no view (:285) | "Waiting for an authoritative trade view." | "Looking for traders…" |
| `_detailSummary` with no view, unavailable view, no merchants (:182-183) | three sentences | "" |
| `ClearTradePresentation` with no session (:127) | "No active Session" | "No match in progress." |
| `DetailSummary` initial text (:68) | "Offers and choices are supplied by the server." | "Pick a trader to see the offers." |
| `MerchantHeading` (:63) | "VISIBLE MERCHANTS" | "TRADERS IN SIGHT" |
| `_detailHeading` without a selection (:68 initial, :181, :421) | "Select a visible merchant" | "Traders" |
| `_empty`, view unavailable (:291) | "Trader data is unavailable." | "Traders are unavailable in this match." |
| `_empty`, no merchants / none selected (:295) | "No visible merchants in the delivered view." / "Select a visible merchant." | "No trader in sight." / "Pick a trader." (the second cannot be reached: with merchants delivered one is always selected; string edit only) |
| `_empty`, stat trader without offers (:334) | "No stat offers are present in the delivered view." | "This trader has no offers right now." |
| pending text after a trade is sent (:222) | "choice queued · awaiting server state" | "Trading…" |
| `AuthorityNote` (:84) | "Server decides whether a choice can be completed." | "" (label keeps its name, stays in the tree, empty) |
| choice button labels (:307 barter, :348 stat) | `(pick + 1) + " · " + label` | `label` alone |

Status line (`Refresh`, the block from `string authority = ...` at :168): in a match there is no prefix; in any other
state it is `StateCopy(state)` alone, whatever the view says. `StateCopy` (:593) keeps its wording except
"Session ended" becomes "Match ended".

| Case (in a match), first match wins | Status |
|---|---|
| no trade view yet | "Looking for traders…" |
| view not available | "Traders are unavailable in this match." |
| no merchants | "No trader in sight." |
| a trade is pending | "Trading…" |
| stat trade used and the selected trader is a stat trader | "Stat trade used this run." |
| stat trade used, another trader selected | "Stat trade used this run. Barter is still open." |
| otherwise | "1 trader in sight" / "N traders in sight" |

`TryTradeBound` (:209) sets the status to "Trading…" directly (it writes "Server trade view · " + the pending text today).

The summary lines of `UpdateDetailSummary` (:539-541), the barter clock (:561-563, "Next barter in 3.2s.", "Barter
ready.") and the `StatName` words stay as they are.

## 2. Dependencies
none (base `lane/ui-rework/base-2`).

## 3. Relevant legacy paths
- `packages/client/src/ui/traderPanel.ts:51-54`: the legacy description lines ("Your stat trade is complete for this
  run.", "Choose one permanent exchange for this run. These offers reflect your current stats.", "Trade the weapon in
  your hand, ...") which the Unity summary already copies; and `:54` the placeholder "Considering your build…" for a
  stat offer not yet known. Legacy has NO status line, no "visible merchants" heading and no numbered buttons
  (`:57` the button text is the label alone), so the "button shows its offer alone" rule is legacy's. Legacy has no
  string for the other states, so the spec's words stand. Legacy's "Considering your build…" is a per-button
  placeholder, not the "no offers" state of this window: the spec's "This trader has no offers right now." is kept,
  and the difference is recorded here for the reviewer (verified with `grep` of the file).
- `docs/rework/LEGACY_LAYOUT.md:295-297`: the legacy trader card.

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/TraderCopyEditModeTests.cs` (+ `.meta`), the oracle.

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/TraderDialogView.cs`
- the oracle-amend commits below (the owner approves them) are the only edits to existing tests. Not in scope:
  `UI/QuestJournalView.cs`, `MatchHud.PlayerFlows.cs`, binding a digit key, opening the window from F.

## 6. Contracts and references
- R-AUTH: Unity still draws only what the session delivers; no rule moves. Theme and text helpers (`MythicUi.Text`)
  stay; `Set(target, text)` and `SetState` stay the way to write a label.
- `TrainingHudNotesEditModeTests` skips labels named `AuthorityNote`
  (`Tests/Editor/TrainingHudNotesEditModeTests.cs:55`): the label keeps its name.

## 7. Behaviour to preserve
- Every rect, anchor, object name (`TraderDialog/Status`, `MerchantPanel/MerchantHeading`, `OfferPanel/DetailHeading`,
  `DetailSummary`, `Empty`, `AuthorityNote`, `Choice_<n>/Label`, `Merchant_<n>`), button interactability, the stale-row
  guards, the pending flag semantics (cleared on the next trade view) and the commands sent.
- Comments and exception messages may keep the words "server"/"authoritative"; player-facing strings may not
  (`grep -n "server\|Server\|authoritative\|delivered\|Session\"" TraderDialogView.cs`).

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-TRADER-COPY-1 --base lane/ui-rework/base-2` prints `GATE PASS`.
- `TraderCopyEditModeTests`, `TraderDialogViewEditModeTests`, `TrainingHudNotesEditModeTests`, `PlayerFlowsEditModeTests` green.

## Existing tests that pin this code
All in `client-unity/UnityProject/Assets/Tests/Editor/` (found by grep of every "Today" string and the lowercase tails):
- `TraderDialogViewEditModeTests.MissingUnavailableAndAvailableEmptyTradeViewsStayDistinctAndOpeningIsReadOnly`
  (:48 `Does.Contain("awaiting trade view")`, :56 `Does.Contain("unavailable")`, :62 `Does.Contain("no visible merchants")`):
  the status of a missing, unavailable and empty view. CONFLICTS at :48 and :62; :56 still passes. Amended in an
  `oracle-amend:` commit: :48 becomes "Looking for traders…", :62 becomes "No trader in sight.".
- `TraderDialogViewEditModeTests.RebindingAnUnseenSessionClearsMerchantChoicesAndPendingCopyImmediately` (:244
  `StringAssert.Contains("awaiting trade view", status)`): CONFLICT. Amended: becomes "Looking for traders…".
- `TraderDialogViewEditModeTests.DuplicateMerchantRowsLabels...` (:109-112, `Does.Contain("Same offer")` etc.) and
  `AnIdenticalTradeReplacementKeepsTheRows...` (:~86, `Does.Contain("Different")`): the button label contains the
  offer label; still pass with the label alone. :125 `Choose one permanent exchange for this run` (summary, unchanged).
- The other methods of that class assert counts, selection, commands sent and `interactable`; none reads copy.
- `TrainingHudNotesEditModeTests` (:55): skips `AuthorityNote`; unaffected.
- `PlayerFlowsEditModeTests` (:395-421) only reads `TraderDialog.IsVisible`; its ":216 '1 · same'" is the Notices
  list, not the trader: do not touch it.
- `PresentationTimelineEditModeTests:162`, `MatchHudIconPresentationEditModeTests:70`, `MatchHudWishBarrierEditModeTests`,
  `QuestJournalViewEditModeTests:305` contain "Awaiting server" for other views: unrelated.
- `UnityProject/Tools/hud-seams.test.cjs:114` names `_traderDialog.Initialize` only: unaffected.

### oracle-amend commits (for the owner to approve)
1. `MissingUnavailableAndAvailableEmptyTradeViewsStayDistinctAndOpeningIsReadOnly`: two expectations (:48, :62) changed.
2. `RebindingAnUnseenSessionClearsMerchantChoicesAndPendingCopyImmediately`: one expectation (:244) changed.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/TraderCopyEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TraderCopyEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/TraderDialogViewEditModeTests.cs` (the two amended methods only)
- `docs/backlog/packets/P-UI-TRADER-COPY-1.md`

## 9. Validation commands
- `bin/unity-class.sh <worktree> TraderCopyEditModeTests,TraderDialogViewEditModeTests,TrainingHudNotesEditModeTests`
- `scripts/gate.sh --quick --oracle packet/P-UI-TRADER-COPY-1 --base lane/ui-rework/base-2`

## 10. Risk and suggested model tier
Low: one file of string edits plus the status block. implementer-sonnet. The risk is a missed string; test 2 sweeps
every active label of the dialog for the banned words.

## 11. Known traps
- Status wording is read both from `Refresh` and from `ClearTradePresentation`/`SetState`; keep `Set` equality guard.
- "Trading…" shows from the send until the next trade view (about one round trip in play); the tests prove the text only.
- `_detailSummary` is set empty in the no-view cases but `UpdateDetailSummary` sets it again once a merchant is selected.
- The pending status must also be what `Refresh` writes while `_pending` (the same word, not a prefix).
- Reflow: labels are width-fitted by `FitHeight`; shorter copy cannot overflow.

## Oracle author notes
- No stub commit: the tests use only existing public members; strings are the only thing that changes.
- Spec test 1 is split in two methods: `TheStatusLineOfEachCase` (the seven rows) and
  `ASessionNotInAMatchShowsTheStateCopyAlone` ("Connection interrupted · resuming" after a dropped socket; "Match ended"
  after a `MatchEnded` event, with a trade view that has a merchant). Together they are the spec's test 1.
- Red on the stub (run through `unity-class.sh`, 6 new tests, all on assertions):
  - `TheStatusLineOfEachCase`: expected "Looking for traders…", was "Server trade view · awaiting trade view".
  - `ASessionNotInAMatchShowsTheStateCopyAlone`: expected "Connection interrupted · resuming", was that plus
    " · choices are server-authoritative".
  - `NoPlayerFacingTextMentionsTheServer`: lists Status, DetailSummary, Empty, AuthorityNote texts with the banned words.
  - `ChoiceButtonsCarryTheOfferAlone`: expected "Small potion for a sword", was "1 · Small potion for a sword".
  - `SendingATradeSaysTrading`: expected "Trading…", was "Server trade view · choice queued · awaiting server state".
  - `TheEmptyStatesSayItOnce`: expected centre "Looking for traders…", was "Waiting for an authoritative trade view.".
- Green on the stub: the other `TraderDialogViewEditModeTests` methods and `TrainingHudNotesEditModeTests` (9 of 15
  in the three classes run; the two amend-bound methods are green until amended, then red until implemented).
- The test for the banned words is case-insensitive over "server", "authoritative", "delivered", "session".
- Unity writes `TraderCopyEditModeTests.cs.meta`: committed with the oracle.
