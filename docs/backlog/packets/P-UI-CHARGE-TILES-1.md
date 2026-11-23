# P-UI-CHARGE-TILES-1: a spell that holds a charge reads Ready

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("ability and spell hub needs optimizing"). Found by the lane's reading for P-UI-ABILITY-FEEDBACK-1 and confirmed by a checked reading on 2026-11-23: a quest spell that still holds a charge draws as cooling although a press casts.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head `fe3360a5c`: `df64266a4` plus P-UI-FIRST-RUN-HINT-1 and integration 87; neither touches a file of this packet). Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-r`.
- **D-rules touched:** D1: legacy's charged slot is `ready` when no lock (`readyAt`) is ahead (`packages/client/src/ui/abilitySlot.ts:94-95, 122`) and then also draws the next charge's countdown and wedge (`:100-110`; `AbilitySlotView.update` writes both whenever they are above 0, `:289-306`). Quest spells have no legacy counterpart, and their `ready_at` is the next charge's arrival, not a lock. Unity draws neither wedge nor number for a held charge, as the Wish tile does once its lock has passed. ONE `DEVIATIONS.md` row is proposed for the quest spell tile (hand-off only; Outcome 9). D2 n/a. D5/D6 none. D9 untouched: the face is computed from delivered `Charges` and `ReadyAt`; nothing is sent or decided.

## 1. ID and outcome

P-UI-CHARGE-TILES-1. A quest spell that still holds a charge draws as Ready, not as cooling.

**What is wrong today.** `AbilityFaces.Spell` (`client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs:34-46`) returns Cooling whenever `readyAt - now` is above 0 and uses `charges` only for `Badge`. `RefreshQuestTiles` (`client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs:417-460`, the call at `:445`) feeds it each slot's wire `Charges` and `ReadyAt`. The server refuses a quest spell for its cooldown only at 0 charges: `cast()` first banks every charge whose `ready_at` has passed (`recharge`, `crates/rh-sim/src/quests/board/rewards/casts.rs:11-20`, called at `:64`), then refuses only `owned.charges == 0` (`:65`); `ready_at` is the time the NEXT charge arrives. Lantern Ward (2 charges, 60 s recharge; the only quest spell with more than one: `Owned::new`, `crates/rh-sim/src/quests/board/rewards/inventory.rs:18-32`) after one cast is (1 charge, `ready_at` ahead): a press casts, yet the tile draws Cooling: dim frame, wedge, number, name hidden (`UI/AbilityTile.cs` `Apply`, `:97-135`). Since P-UI-ABILITY-FEEDBACK-1, `CastReadiness.Spell` (`client-unity/RoguehallaNet/Net/Ui/CastReadiness.cs:32-35`) treats a charge in hand as castable (`charges > 0` passed as `chargeInHand` to `Judge`, `:40-50`), so that press pops and shows no line while the tile says "not ready". The Wish tile is right: its `ReadyAt` is the server's cast gate (`AbilityFaces.Wish`, `AbilityFace.cs:55-77`).

Reachable server states by `casts.rs` (a cast at full sets `ready_at = now + cooldown` and `charges -= 1`, `:115-117`; below full `ready_at` is left; `recharge` adds a charge at `ready_at` and moves it on one cooldown while below max, else to now, `:11-20`). Max 1: (1, `ready_at` at or before now) and (0, ahead) only, plus the instant (0, at or before now) before the tick banks it. Max 2 (Lantern Ward): (2, past), (1, ahead), (0, ahead). (1, ahead) with max 1 and (2, ahead) are unreachable.

**Outcome (what is true when this packet lands).**

1. **The rule.** `Spell(has, charges, readyAtMs, nowMs, totalMs)`: `!has` is `AbilityFace.Empty`; `left` is `Finite(readyAt) - Finite(now)` as today. When `left > 0` AND `charges <= 0` the face is Cooling exactly as today (Sweep `totalMs > 0 ? Clamp01(left / totalMs) : 1`, Seconds `ceil(left / 1000)`, Badge `charges`, Cast -1). In every other case the face is `Ready`, Sweep 0, Seconds 0, Badge `charges` (as given), Cast -1. A negative `charges` counts as none for the phase. Worked: `Spell(true, 1, 5200, 1000, 10000)` is Ready, 0, 0, Badge 1 (base: Cooling, .42, 5, 1); `Spell(true, 0, 5200, 1000, 10000)` is Cooling, Seconds 5, Sweep .42, Badge 0; `Spell(true, 2, 0, 1000, 60000)` is Ready, Badge 2. Rewrite the `<summary>` of `Spell` to state the rule; the `Sweep` doc "0 unless Cooling" stays true.
2. **A maximum of one charge behaves as today in every reachable state:** (1, past) Ready badge 1; (0, ahead) Cooling; (0, past) Ready badge 0. Only unreachable inputs change.
3. **The tile.** `AbilityTile` is not edited. A held charge draws its Ready look: `Name` box active with the spell's name, frame `BorderStrong`, `CooldownSweep` disabled, `Seconds` text empty, `Charges` text the count, button interactable. No wedge and no number while a charge is held.
4. **Tile and key agree.** For every case of the feedback matrix: a Cooling tile's `CastReadiness` line starts with "Ready in " and its press does not pop; a line starting "Ready in " means a Cooling tile; a press that pops means a Ready tile; an Empty tile neither pops nor shows a line; a cast-bar tile reads "Casting". The lines "Casting" (bit 4096), "Stunned" and "Silenced" on a Ready tile are effects the tile never drew: not part of the agreement. This is narrower than "a press that shows a line is not a Ready tile": that sentence is false for those lines.
5. **The hover hint keeps the next charge's time.** It is built by `QuestSpellText` (`MatchHud.QuestRewards.cs:469-477`) from `key.Seconds` (`ceil(max(0, readyAt - serverTime) / 1000)`, `:430-435`), never from the face: a held Lantern Ward hints `SHIFT · Lantern Ward` + line break + `1 charge · 42s`; with the stock full and `ready_at` past, `2 charges · Ready`. No edit; pinned by a new test.
6. **The span stays fed.** `RefreshQuestTiles` calls `_questSpans[i].Total` and `tile.Cooldown` in every state (`:444-447`); keep it. After the second cast the wedge starts at its true share, not full. Real cycle (`CooldownSpan` fed each step): (2, 0, now 1000) Ready, badge 2; cast: (1, 62000, now 2000) Ready, badge 1, total 60000; cast: (0, 62000, now 5000) Cooling, Seconds 57, Sweep .95, badge 0; recharge: (1, 122000, now 62000) Ready, badge 1; full: (2, 122000, now 122000) Ready, badge 2, `Total` 0.
7. **Pops.** Only the Wish has a ready pulse (`WishTileMotion.ReadyPulse`, `Presentation/WishFxModel.cs:221-273`, edge `Charges > 0 && serverMs >= ReadyAt`, `MatchHud.TickWishTileMotion`). A quest tile has none: its only pop is the press pop (`InputCue`), decided by `CastReadiness` (`MatchHud.Pulses.cs:45-56`). So a quest spell going from one charge to two changes the badge and nothing else (no pop, the frame and name unchanged); none to one changes the face from Cooling to Ready and pops nothing, today and after. The Wish edges, which this packet does not touch: none to one pulses (pinned by `WishFxModelTests.TileFlashesOnAcceptedCastAndPulsesWhenReadyAgain` and `AbilityMotionEditModeTests.TheReadyPulsePopsTheWishTileOnce`); one to two does not (pinned by nothing today: new test 6 of `ChargeFaceTests`).
8. **Not in this packet, said plainly:** a wedge on a ready tile for the next charge (legacy draws one; it would change what "wedge on" means for every reader of the tile, the Wish included). Recorded as a possible later packet, as is a quest-tile ready pulse. The Wish tile and `AbilityFaces.Wish` do not change.
9. **DEVIATIONS row to propose (hand-off), for the quest spell tile only:** "A quest spell holding a charge reads Ready with its badge and draws neither the next charge's wedge nor its number; the hover hint carries the time. Legacy's charged slot is ready when no lock is ahead and then also draws the next charge's countdown and wedge (`abilitySlot.ts:94-110, 289-306`); quest spells have no legacy counterpart and their `ready_at` is the next charge's arrival, not a lock." The Wish's own deviation is P-UI-WISH-TILE-1's; this row refers to it and does not restate it.
10. **Owner question for the hand-off:** the hint's wording `1 charge · 42s` stays as it is. A change of word is the owner's.

## 2. Dependencies

- P-UI-ABILITY-FEEDBACK-1-fix lands BEFORE this packet (it amends ONE test of `AbilityMotionEditModeTests`, `TheWishTileHasOneScaleWriter`, and adds tests to `ToastLaneHoldTests`). This packet's amendments in `AbilityMotionEditModeTests` (8 and 9 below) are other test methods; re-read that file on the integrated base before applying them.
- `AllocationMeter` (`client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs`) is on the base (checked).

## 3. Relevant legacy paths

- `packages/client/src/ui/abilitySlot.ts:90-110`: a charged ability's state follows `readyAt` ("a charge in hand = ready"), the numbers time the next charge (`secondsLeft`, `sweep` from `next`); `:122` state is `ready` unless blocked.
- `packages/client/src/ui/abilitySlot.ts:289-306`: `AbilitySlotView.update` writes the seconds text, the stock badge and the sweep angle whenever they are above 0.
- Server (the facts the tile must follow, not legacy): `crates/rh-sim/src/quests/board/rewards/casts.rs:11-20, 64-65, 115-117`; `inventory.rs:18-32`.
- Legacy has no quest spells, so the tile is a deviation (Outcome 9).

## 4. New paths

- `client-unity/RoguehallaNet.Tests/ChargeFaceTests.cs` (oracle)
- `client-unity/UnityProject/Assets/Tests/Editor/ChargeTilesEditModeTests.cs` and its `.meta` (oracle)

## 5. Exclusive write scope

Implementer: ONLY
- `client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs` (`AbilityFaces.Spell` and its doc only) and its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/AbilityFace.cs` (`bash client-unity/sync-net.sh` through the wrapper; the `.meta` exists; no new file in the library).

Not in scope: `UI/AbilityTile.cs`, `CastReadiness.cs` and `CastReadinessTests`, `UI/MatchHud.QuestRewards.cs`, `.Pulses.cs`, `.ActionBar.cs`, `Presentation/WishFxModel.cs`, `AbilityFaces.Wish`, `CardDisplayCatalog.cs`, `BakedContent.cs`, `Tools/hud-seams.test.cjs`, `docs/DEVIATIONS.md`. No test in `Tools/*.test.cjs` reads a tile.

## 6. Contracts and references

- R-AUTH / D9: Unity decides nothing; the face is drawn from delivered `Charges` and `ReadyAt`.
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (tests are a separate assembly; time does not advance in EditMode; no allocation in a steady frame).

## 7. Behaviour to preserve

- Wish tile and `AbilityFaces.Wish` unchanged; `TheWishAndASpellDifferOnlyByHoldingACharge` pins the one difference.
- `CastReadiness` and `CastReadinessTests` unchanged and green (read, not run: `TheLineAgreesWithTheFace` feeds `Spell(true, 1, readyAt ahead)`, which now is Ready, and takes the `else` branch, `Goes(reading)`, which the reading satisfies because `chargeInHand` is still passed).
- `PlayerFlowsEditModeTests:82` and `AbilityRowNearbyNoGarbageEditModeTests:119` still deliver the unreachable (2, 2500); they assert hint and equipped text only ("SHIFT · Hook 2 charges · 3s"), which do not change. `AbilityRowEditModeTests.TheEquippedLineStillListsBothSpells` stays green.
- `AbilityTile.Apply` draws Cooling from the phase only; a Ready face with a Sweep above 0 draws no wedge. Do not add one, and do not return a Sweep from a Ready `Spell` face.
- `CastReadiness.Spell` keeps `charges > 0` as `chargeInHand`; redundant for quest spells after this packet. Leave it.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-CHARGE-TILES-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter "ChargeFaceTests|AbilityFaceTests|WishFaceTests|CastReadinessTests"` green.
- Unity classes `ChargeTilesEditModeTests,AbilityFeedbackEditModeTests,AbilityRowEditModeTests,AbilityMotionEditModeTests,AbilityRowNearbyNoGarbageEditModeTests` green.

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter ChargeFaceTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ChargeTilesEditModeTests,AbilityFeedbackEditModeTests`

## 10. Risk and suggested model tier

implementer-sonnet: a one-function change. The risk is in the tests that deliver an unreachable state and are amended (below), and in leaving `RefreshQuestTiles` alone.

## 11. Known traps

- **Wire limits** (`StateItem.cs:15, 157`): a spell with 0 charges needs `ReadyAt` above 0; `Slot` is 0 or 1; `Reward` at most 48. A fixture that breaks them throws at `Encode`.
- Feed `CooldownSpan` in every state: do not skip `_questSpans[i].Total` for a Ready face (Outcome 6 depends on it).
- Do not return a Sweep from a Ready `Spell` face.
- Run the sync before the first Unity run. Find methods by name, not by line. A tracked C# file above 800 lines fails the gate.
- Change `Spell` first: the library tests run in seconds.

## Existing tests that pin this code

Each was read on the base. "Amend" rows are the owner-approved changes made by the oracle author, one `oracle-amend:` commit each (kind F: fixture, the unreachable (2, ahead) or (1, ahead) becomes the reachable (0, ahead); E: expectation).

| Test | What it asserts today | Action |
|---|---|---|
| `AbilityFaceTests.NoSpellIsEmptyWithNoBadgeAndNoCastBar` | `Spell(false, ...)` is Empty | stays green |
| `AbilityFaceTests.ASpellWithNoTimeLeftIsReadyWithItsChargesAndNoNumber` | charges 2 with `readyAt` at or before now is Ready, Badge 2 | stays green |
| `AbilityFaceTests.ACoolingSpellShowsWholeSecondsRoundedUpAndTheShareOfTheTotalLeft` | `Spell(true, 1, 5200, 1000, 10000)` Cooling, 5 s, .42, Badge 1; two Seconds rounding calls | amend 1 (F, E): charges 1 to 0 in the three calls; Badge 1 to 0 |
| `AbilityFaceTests.ACoolingSpellWithNoKnownTotalDrawsAFullWedgeAndALongerLeftThanTotalClampsToOne` | total 0 / negative gives Sweep 1; left above total clamps | amend 2 (F): charges 0 |
| `AbilityFaceTests.NanTimesCountAsZero` | NaN readyAt is Ready; NaN now with 5 s left is Cooling | amend 3 (F): only the second call, charges 0 |
| `AbilityFaceTests.ASpellFaceFromASpanDrawsAWedgeThatShrinksFromOneToZero` | the wedge from a `CooldownSpan` shrinks 1 to 0 | amend 4 (F): charges 0 in the three calls |
| `WishFaceTests.AWishTheServerWouldRefuseReadsCoolingAndShrinksTheWedge` | the closing loop: Wish and Spell have equal Phase, Seconds, Sweep for charges 0, 1, 2 | amend 5 (E): the loop runs for charges `{ 0 }`; the difference at 1 and 2 is new test 5 of `ChargeFaceTests` |
| `AbilityRowEditModeTests.ADeliveredQuestSpellShowsNameChargesAndCountdown` | Lantern-like (2, 6000) at 1000: badge "2", 5 s, name hidden, wedge, frame `Border`; at 7000 ready look, badge "2" | amend 6 (F, E): first delivery `Charges = 0`, first badge assertion reads "0"; after `Snap(12, 7000)` and before its `Frame()` deliver `Items(12, Quests(new[] { new WireQuestSpell { Slot = 0, Reward = 3, Charges = 1, ReadyAt = 6000 } }))`, last badge assertion reads "1" |
| `AbilityRowEditModeTests.ASteadyQuestRefreshFrameAllocatesNothing` | the refresh of a cooling spell allocates nothing | amend 7 (F): `Charges = 0` |
| `AbilityRowEditModeTests.TheEquippedLineStillListsBothSpells` | equipped line "SHIFT · Shoulder Charge 2 charges · Ready" | stays green |
| `AbilityMotionEditModeTests.AQuestSpellWedgeMovesBetweenSnapshots` | the wedge of a cooling spell moves between snapshots, stays on past ready until a snapshot | amend 8 (F): `Charges = 0` in both deliveries |
| `AbilityMotionEditModeTests.ASteadyActionRowTickAllocatesNothing` | the action-row tick allocates nothing with three wedges on | amend 9 (F): `Charges = 0` for both spells |
| `AbilityRowNearbyNoGarbageEditModeTests.ACoolingQuestSpellAllocatesNothing` | a cooling spell (2, 6000) plus a ready spell allocate nothing; the equipped line contains "2 charges · 5s" | amend 10 (F, E): slot 0 `Charges = 0`; "2 charges · 5s" becomes "0 charges · 5s" |
| `AbilityFeedbackEditModeTests.TheLineIsTheOneTheFunctionGivesForEveryPhaseTheTileShows` | the 24-case matrix, case "charge in hand, next recharging" (`Spell(i, 1, 52100)`) expects tile Cooling, 3 s, no line, pops | amend 11 (E): that case expects `Face.Ready`, 0 seconds (the seconds are read only for a Cooling case, so the 0 is a record); `FaceOf` unchanged; the matrix stays 24 cases |
| `CastReadinessTests` (whole class, incl. `TheLineAgreesWithTheFace`, `:164-165, :177-203`) | the key's line from delivered facts | not in scope; stays green |
| `PlayerFlowsEditModeTests` (`:82`), `AbilityRowNearbyNoGarbageEditModeTests` (`:119`) | hint and equipped text of the unreachable (2, 2500) | stay green |
| `WishFxModelTests.TileFlashesOnAcceptedCastAndPulsesWhenReadyAgain`, `AbilityMotionEditModeTests.TheReadyPulsePopsTheWishTileOnce` | Wish pulses none to one | not touched |

The amend commits come one per test; none of them is an implementer's to make, and the implementer must not edit them.

## Oracle files (read-only for implementers)

(filled in by the oracle commit)

## Notes from the oracle author

(filled in by the oracle commit)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
