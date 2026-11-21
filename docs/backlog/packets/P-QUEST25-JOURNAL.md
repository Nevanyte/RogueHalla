# P-QUEST25-JOURNAL

## ID and outcome
P-QUEST25-JOURNAL serves Phase 3, item 16 and the owner’s Top 25 activation packet 4. Unity presents the board, personal objectives, public carrier cues, nearby quest interactions, and the two quest reward slots.

## Dependencies
P-QUEST25-WIRE provides the owner collection and interaction command. The board, attempts, and rewards packets publish the data and validate every command. The controls packet binds the two casts.

## Relevant legacy paths
`packages/client/src/ui/questLog.ts`, `packages/client/src/ui/questMap.ts`, and `packages/client/src/render/QuestView.ts` establish the existing presentation.

## New paths
This packet and `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs` with its Unity metadata.

## Exclusive write scope
`QuestJournalView.cs`, `MatchHud.QuestRewards.cs`, quest sections of `MatchHud.PlayerFlows.cs` and the HUD input gate, quest presentation sections of `TacticalMapMarks.cs`, `TacticalMapController.cs`, `WorldMarks.cs`, `QuestCarrierFlags.cs`, and `ArenaView.cs`, and the corresponding EditMode tests. The owner-scoped presentation projection in `RoguehallaNet/Net/Collections.cs` and its generated Unity mirror expose the four decoded collections.

## Contracts and references
C-INPUT, C-OBS, R-AUTH, ADR 0008, and D9. D1 uses the approved Top 25 design departure for the new board and preserves legacy presentation behavior on its existing path. D2 does not apply to this presentation packet. The owner brief authorizes the Top 25 work during D5.

## Behaviour to preserve
The journal and maps use only delivered rows, coordinates and visible actor identities. Legacy quest acceptance and tracking still work. Unity sends the current server-projected action tuples and waits for the next authoritative state. A new session or timeline invalidates callbacks bound to the old collection.

## Acceptance checks
An untimed accepted quest shows active progress without awaiting a nonexistent deadline. Projected objective, turn-in, reward decision, and pickup buttons send their exact tuples. Stale callbacks are inert. Pending decisions return with the server view after reconnect. Both spell slots show the published reward, charges, and readiness. Completed rows lose their journal and map marks. Public carrier marks follow delivered actors.

## Validation commands
`bash client-unity/UnityProject/Tools/editmode.sh`, `dotnet test client-unity/RoguehallaNet.Tests`, and `scripts/gate.sh` with the integrated feature.

## Risk and suggested model tier
Unity presentation risks include modal input ownership, deadlines in server time and fog disclosure. Use the Unity implementer tier and review its contract with the wire and controls packets.

## Known traps
Pending choices may outlive the turn-in row. Action IDs and locations come from the collection. A replaced collection or a resumed Welcome can invalidate a callback before the next rendered frame.
