# P-UI-RUN-END-1: nothing that cannot act stays on the results screen, and the end of a run reads "Run over"

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat: "make sure all in-game notifications are thoughtfully placed", "Please do a UI optimization sweep"). Seen in the lane's Web round 3a (2026-11-23): the launcher "Quests · J" and the top pill stayed on the results screen.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head 76ea9c0f9 when `packet/P-UI-RUN-END-1` was branched; P-UI-HUD-GARBAGE-1 and P-UI-PLATES-OPAQUE-1 are in it)
- **D-rules touched:** D1: presentation only. Legacy shows the end of a run on the plate as "Run over" or "Run over — <winner> is victorious" (`packages/client/src/ui/hud.ts:54`), keeps its whole HUD under the results panel (`packages/client/src/ui/results.ts:83-111`, no scrim) and hides its quest launcher and the J key once the phase is neither active nor planning (`packages/client/src/ui/questLog.ts:45`, `:126-135`, `:299-307`). A DEVIATIONS row is proposed (section 7). · D2 n/a · D5/D6 none · D9 untouched: no key is bound and nothing a key does changes.

## 1. ID and outcome

When a run is over, `MatchHud.HudOwnerRefresh` (`client-unity/UnityProject/Assets/Scripts/UI/MatchHud.cs:279`) shows the results card (`_results`: a full-window scrim of alpha 150 with the card) and `BlocksKeyboardInputCore` (`MatchHud.Features.cs:51`) hides the plate, action row, minimap and toasts. Three things are left, all drawn under the scrim:

1. **The launcher.** `RefreshJournalLauncher` (`MatchHud.PlayerFlows.cs:163`, the line is `:166`) sets `_journalButton` active from `PlayerFlowLauncherAllowed && !PlayerFlowVisible`. Neither reads `_results`, the session's result or the state, so "Quests · J" stays at the bottom left. The scrim (`raycastTarget` true, no dismiss surface) swallows a click on it, and J (`MatchHud.Features.cs:267`, `ReadUiInput`) opens `DefaultPlayerFlowPage` (`MatchHud.PlayerFlows.cs:56`; the Notices page, 2, once the state is not InMatch) while the label says Quests. It also shows in the 1.5 s before a defeat's card.
2. **The top pill.** `SyncTopCentre` (`MatchHud.TopCentre.cs:34`) treats every state but InMatch as connection status: for Ended `showBanner` is true (`:39`), so the banner pill ("Run over" since P-UI-PLAYER-WORDS-3) stands over the card and repeats it. `phase = !showBanner && ...` (`:47`) is then false, so the phase pill never shows. That pill carries the winner: `TickMetrics` (`MatchHud.Features.cs:181`, four times a second; `runOver` at `:195`) builds "Run over — <winner> is victorious" when `_matchEnded` is set. So no surface shows the winner after the end.
3. **The word.** The phase table `Phases` (`MatchHud.cs:55`) says "Match ended" for the ended phase: while the state is InMatch and the snapshot phase is Ended, the plate and `_banner` read "Match ended" (`MatchHud.cs:264`) and the phase pill reads "Run over". Legacy's status line has one word for the end.

Definitions: `runOver` = `_session != null && _session.State == SessionState.Ended` on the HUD's bound session. "The card" = `_results != null && _results.gameObject.activeSelf`. "A result exists" = `_session.LastResult != null`.

When this packet lands:

1. **Launcher.** Hidden from the moment a result exists for the session until the player spectates, AND whenever the card is up. One condition on fields, written inline in `RefreshJournalLauncher`: `SetActive(PlayerFlowLauncherAllowed && !PlayerFlowVisible && !(the card) && !(_session != null && _session.LastResult != null && !Spectating))`. `Spectating` is the property at `MatchHud.Spectate.cs:39`. No new member, and nothing in `PlayerFlowLauncherAllowed` (`MatchHud.PlayerFlows.cs:58`), which also gates `ShowPlayerFlow` and the J key (the key works as today). The rule does not need the card for the result term, so the launcher goes the frame the result arrives (inside a defeat's 1.5 s delay, before the card), stays gone with the card up and after a flow closes, and is back while the player spectates, card hidden (`StartSpectating`/`StopSpectating`, `MatchHud.Spectate.cs:47`, `:62`, reach `RefreshJournalLauncher` through `RefreshWorldHudVisibility` (`MatchHud.Visibility.cs:104`); `TickPlayerFlows` calls it every frame). With no result and no card it shows as today; under a card with no result (Failed, Rejected, or Ended without a Result) it is hidden by the card term.
2. **Top pill, keyed on the state.** In `SyncTopCentre`, `bool runOver = _session != null && _session.State == SessionState.Ended;` and, after the Rejected-with-result line (`:45`), `if (runOver) showBanner = false;`. The phase pill is on when `!showBanner && _hud active && strip text not empty && (!runOver || _matchEnded)`. The rule reads `_session` only: the Fix4 fixtures build the HUD from pieces and leave `_client` null. It needs no `LaunchPending` clause (`MatchClient.LaunchPending` holds only in Idle, Connecting, HelloSent, InMatch and Reconnecting, never Ended). Reconnecting, Rejected, Failed and InMatch keep today's result.
3. **What the top shows in each interval of an end:**
   - State InMatch, snapshot phase Ended (the server flips the phase first): no banner; the strip reads "Run over" (no winner yet); phase pill on. Unchanged.
   - State Ended after a MatchEnded event, with or without a Result: no banner; phase pill on; strip "Run over — <winner> is victorious" when the event named a winner who is in the roster, else "Run over" (draw, no winner, winner not seated). The strip is current on the first frame (item 5). With no Result (the real no-result case) the card's note carries "Run over\nNo authoritative result received." (`MatchHud.cs:310`).
   - Practice (`ConfirmedPractice`): no edit. `TickMetrics` answers the practice branch before the phase line (`MatchHud.Features.cs:197`), so the top keeps "PRACTICE · NO RUN REWARDS" on the phase pill after the end. Legacy keeps its training banner for the same reason (`hud.ts:41-42`). No test.
   - State Ended with no MatchEnded event on a bound HUD: only a test reaches it (`Session.Leave()` while the HUD is bound; the product unbinds first). Defensive: no banner, no phase pill, strip empty.
   - State Ended before the event: `Session.Receive` sets Ended, then raises the event in one call: no HUD frame lies between, so no rule.
   The rule does not read the card: no flicker in a defeat's 1.5 s delay, nor when J hides the card behind a flow.
4. **The word.** `Phases[2]` is "Run over"; `_banner`, `_plateStatus` and the phase strip then agree for the ended phase. `Phases[1]` stays "Match active" (legacy hides its status row in the active phase, `hud.ts:199`). `Phases[0]` and `Phases[3]` are not touched. `ConnectionCopy.ForState(Ended)` ("The match has ended.") is not changed: `RefreshAccountMenu` (`MatchHud.Accounts.cs`) shows it on the launch menu through `LaunchCopy.SessionFailureText` when a retained Ended session is still bound. The two words for the end go into the hand-off as a note.
5. **A fresh strip.** `NoteMatchEnded` (`MatchHud.Features.cs:163`) sets `_nextMetricsAt = 0d` when it sees a MatchEnded event, so `TickMetrics` (it runs before `SyncTopCentre` in `TickFeatures`, `:322`) writes the run-over line on the next frame, not up to .25 s later. `TickMetrics` is not edited.
6. No allocation in a steady frame: every change reads fields and writes no string. Nothing moves; no colour, size or placement changes.

## 2. Dependencies

P-UI-HUD-GARBAGE-1 and P-UI-PLATES-OPAQUE-1 are merged on the base (checked: `git log` of `lane/ui-rework/base-3`). The members named here were re-read on that base; the spec's line numbers were off by small amounts and are corrected above.

## 3. Relevant legacy paths

- `packages/client/src/ui/hud.ts:41-42` (training banner first in `statusLine`), `:54` ("Run over" / "Run over — <winner> is victorious"), `:199` (`this.status.hidden = active`).
- `packages/client/src/ui/results.ts:83-111`: results panel, whole HUD kept under it, no scrim.
- `packages/client/src/ui/questLog.ts:45`, `:126-135`, `:299-307`: launcher and J hidden once the phase is neither active nor planning.
- Evidence: `.agent-runs/lane-ui-rework/understand/web3-results-leftovers.txt`, `webround/round-3a/90-results.png`.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/RunEndEditModeTests.cs` and its `.meta` (oracle).
- This packet.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.PlayerFlows.cs`: `RefreshJournalLauncher`, one line. Keep it declared after `TickPlayerFlows` (`hud-seams.test.cjs` slices the file between them).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.TopCentre.cs`: `SyncTopCentre` only (the `runOver` local and two lines; `CreateTopAndToastPills` is not in scope).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.cs`: one string literal, `Phases[2]` (line 55); no line added or removed (`hud-seams.test.cjs` counts `split('\n')`, one more than `wc -l`, and caps the file under 600; it is 569 lines by `wc -l` on this base).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Features.cs`: `NoteMatchEnded`, one statement.
- Not in scope: `QuestJournalView.cs`, `TraderDialogView.cs`, `OrderedNoticeFeed.cs`, the journal and Traders tab texts after the end (P-UI-FLOWS-LEFTOVERS-1), `PlayerWords.cs`, `ConnectionCopy.cs`, `MatchHud.Visibility.cs`, `MatchHud.Settlement.cs`, `MatchHud.Spectate.cs`, `CreateTopAndToastPills`, `Tools/hud-seams.test.cjs`, the J key, `PlayerFlowLauncherAllowed`, `TickMetrics`. No library change: no dotnet test, no `sync-net.sh`.

## 6. Contracts and references

- Presentation only; Unity holds no game rule. No wire, protocol or `crates/` change.
- `MatchHud` binds and unbinds sessions many times: nothing here keeps a handler.
- COMMON.md rules (lane): `Spectating`, `_results`, `LastResult` are read as fields; text through existing labels.

## 7. Behaviour to preserve

- The J key works as today (opens `DefaultPlayerFlowPage`, the Notices page after the end; close returns to the card). No key is bound or changed.
- Reconnecting, Rejected, Failed and InMatch keep their banner/pill result; Rejected with a result still hides the banner (Fix4).
- `Phases[0]`, `Phases[1]`, `Phases[3]` unchanged. `ConnectionCopy.ForState(Ended)` unchanged.
- Practice keeps "PRACTICE · NO RUN REWARDS" on the phase pill.

DEVIATIONS (for the wave hand-off; the coordinator signs): this packet removes cases (a) and (b) of P-UI-PLAYER-WORDS-3's proposed row 1 (the phase table's "Match ended"; no winner shown), so that row is withdrawn or reworded to case (c), the result note legacy lacks. One row is proposed for what still differs: the end of a run sits on a top pill, not on the plate (hidden under the card); the launcher leaves when a result arrives and returns only while the player spectates, so a dead player in a running match loses it (legacy keeps it while the phase is active); under any card, with or without a result, it is hidden. No row for practice.

Notes for the owner (hand-off, no packet): (1) the two words for the end, "The match has ended." on the launch menu and "Run over" in the HUD; (2) the winner's line stands on the phase pill under the card's scrim, so it is dimmed; (3) the active phase's word stays "Match active"; (4) J after the end still opens the Notices page, with no launcher shown.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-RUN-END-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `RunEndEditModeTests` all green; the existing classes of "Existing tests that pin this code" stay green.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper passes after the edits.

## Existing tests that pin this code (read; none turns red)

- `HudWordsEditModeTests.TheBannerSpeaksInWords` (`Tests/Editor/HudWordsEditModeTests.cs:152`): reads `_banner` and `_plateStatus` after a MatchEnded: "Run over"; `ResultNote.text` "Run over\nNo authoritative result received."; `GUARD_ReconnectingKeepsItsBanner` (`:180`) banner starts "Reconnecting… (".
- `HudVisualFix4EditModeTests.ARejectedJoinWithNoResultStillShowsItsPill` (`:193`) and `NoJoinStatusPillIsShownOverAResultsCard` (`:~200`): Rejected, banner pill active with no result, inactive with one. Built from pieces, `_client` null, `_session` set by reflection.
- `BannerPillAfterResultFix4EditModeTests.ADroppedConnectionAfterAResultStillShowsItsPill` (`:44`): Failed or Reconnecting after a result keeps its pill.
- `PlayerFlowsEditModeTests` (journal and J key, launcher in the planning/reconnecting cases, e.g. `:379`, `:402`, `:486`): J opens/closes flows and the launcher is inactive while a flow is open; no state Ended case.
- `MatchSettlementEditModeTests` (reads `LastResult` through a `MatchClient`, no launcher or pill).
- `TabMapSheetEditModeTests.OnlyTheChromeSurvivesTheMap` (`:402`): launcher and phase pill kept in an Active match, map open (no result; unchanged).
- `HudVisualFixEditModeTests.TheTimerPillIsWideEnoughForItsLine` (`:~76`) and `HudPhasePillNarrowEditModeTests`: pill width from `ApplyPhaseStrip`; no state read.
- `ControlsLineEditModeTests`, `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests` build the launcher from pieces and never set a session.
- No test, node tool or capture script reads "Match ended"; `TraderCopyEditModeTests:93` is the Traders tab's own word.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/RunEndEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/RunEndEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-RUN-END-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> RunEndEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> HudWordsEditModeTests,HudVisualFix4EditModeTests,BannerPillAfterResultFix4EditModeTests,PlayerFlowsEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`

## 10. Risk and suggested model tier

Sonnet: four one-line edits in four files, each pinned by a test. Risk is in the frame order (`SyncTopCentre` runs before `HudOwnerRefresh`, so the pill must never read the card) and in the 600-line cap of `MatchHud.cs`.

## 11. Known traps

- Keep `RefreshJournalLauncher` declared after `TickPlayerFlows`; `SyncTopCentre` stays in `MatchHud.TopCentre.cs`.
- `MatchHud.cs` changes one literal only: a line added breaks the 600-line cap.
- Do not key the pill on `_results`: `ShowPlayerFlow` hides the card and a defeat's card is 1.5 s late (flicker). The launcher rule reads `LastResult` (set on delivery by `Session`) so its order does not matter.
- `TickMetrics` runs before `SyncTopCentre`; `_phaseStrip.text` is the truth for "not empty". The `_matchEnded` term keeps an Ended session without an event from showing a stale in-match line for up to .25 s; `Bind` and `NoteSessionState(InMatch)` clear `_matchEnded`, so the pill rule needs no reset.
- The winner comes from the roster: seat it before the event, or the line is the bare "Run over" (`WinnerLabel`, `MatchHud.Features.cs:171`, caches a name only once found).
- The phase pill sits under the 150-alpha scrim, so the winner line is dimmed. `ApplyPhaseStrip` sizes the pill from a label that may never have been active in the hierarchy (COMMON.md): this packet changes no width logic and no test asserts a width.
- Shared files with P-UI-PLATES-OPAQUE-1 (merged) and P-UI-HUD-GARBAGE-1 (merged): other hunks.

## Notes from the oracle author

No stub commit: every test reads the HUD by reflection and the public `ShowPlayerFlow`, `ReadUiInput`, `ServerNotices`, so nothing needed a signature.

`RunEndEditModeTests` (`Tests/Editor/RunEndEditModeTests.cs`, 17 cases). Spec test numbers in brackets. Two deviations from the spec's wording, same behaviour pinned: the spec's test 12 guards are four separate tests (`GUARD_ReconnectingStillShowsItsPill`, `GUARD_FailedStillShowsItsPill`, `GUARD_ARejectedJoinWithAResultStillHidesItsPill`, `GUARD_ARejectedJoinWithNoResultStillShowsItsPill`); the spec's test 3 reaches Spectate with no MatchEnded event (Spectate needs State InMatch).

Red on the stub (first failing assertion in the real run; the later red lines are in the scratch run below):
- `ADefeatHidesTheBannerAndShowsTheRunOverLine` [1]: "no banner over a finished run (before the card)", expected False was True.
- `TheTopPillDoesNotFollowTheCardAcrossAFlow` [5]: "no banner behind the flow", expected False was True.
- `ADrawOrAnUnknownWinnerReadsBareRunOver` (2 cases) [6]: "the line is shown", expected True was False (phase pill; the banner is on).
- `TheStripIsCurrentOnTheFrameTheEndArrives` [7]: strip expected "Run over — Quill is victorious" was "FORMING MATCH".
- `AnEndWithTheEventAndNoResultShowsRunOverOnTheTop` [8]: "no banner over the card".
- `AnEndWithoutTheEventShowsNoTopPill` [9]: "no banner over an ended run".
- `TheLauncherLeavesAtTheDefeatResultAndStaysGone` [2]: "the launcher is gone with the result, before the card".
- `TheLauncherReturnsWhileSpectatingAndLeavesWithTheCardAgain` [3]: "the launcher leaves with the card again" (its GUARD line, the launcher while spectating, is green).
- `JOpensTheNoticesAfterTheEndAndCloseReturnsToACardWithoutALauncher` [4]: "no launcher with the card back" (the J lines are green).
- `TheEndedPhaseWordIsRunOver` [10]: banner text expected "Run over" was "Match ended".

Green on the stub (guards): `GUARD_TheEndedTermKeepsAStaleLineOffTheTop` [9], `GUARD_TheOtherPhaseWordsAreUntouched` [11], and the four `GUARD_` tests of [12].

Scratch run (not committed): a copy of the class with every `Assert.That` replaced by a catch-and-collect helper, one run on the stub. Every collected entry was an assertion about the missing behaviour and no other exception occurred: [1] four (banner on, phase pill off, each before and with the card); [5] four (banner on, phase pill off, each behind the flow and with the card back); [6] two per case (phase pill off, banner on); [7] two (strip stale, phase pill off); [8] three (banner on, phase pill off, launcher on); [9] one (banner on); [2] two (launcher on before the card and under it); [3] one; [4] one; [10] two (banner and plate read "Match ended"). Each test's set-up assertions, the strip lines of [1] and the guard lines passed. Note for the implementer: the pill off in [6], [7] and [8] is a consequence of the banner being on, so one `showBanner = false` rule turns several lines green at once.

Existing classes run on the stub, all green: HudWordsEditModeTests, HudVisualFix4EditModeTests, BannerPillAfterResultFix4EditModeTests (21 tests); PlayerFlowsEditModeTests, MatchSettlementEditModeTests, TabMapSheetEditModeTests, HudVisualFixEditModeTests, HudPhasePillNarrowEditModeTests (49 tests).

Not established by the oracle: practice after the end (no test; the rule leaves it as is); `hud-seams.test.cjs` (the implementer runs it).

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
