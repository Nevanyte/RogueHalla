# P-ALPHA-PLAN-QUESTS-1: show public quest names during planning

- **Serves:** open alpha gameplay restoration, owner direction 2026-11-26; Unity presentation only
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `codex/alpha-gameplay-restoration` @ `dbaa701be`
- **D-rules touched:** D1 n/a (presentation only); D2 n/a; D5/D6/D9 none

## 1. ID and outcome
The planning screen shows every currently public quest by name and, when available, the giver's map zone. It uses the
server-delivered unknown count to say how many more will be revealed at raid start. The client does not infer, reveal or
invent hidden quest content.

## 2. Dependencies
- The server delivers the public quest board and `UnknownCount` in the existing quest view.
- The alpha server policy makes all ten quests public at raid start; the planning view carries five names and an unknown
  count of five.

## 3. Relevant legacy paths
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs:140-150` builds the planning decision rail.
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs:513-550` presents the active plan and its decisions.
- `client-unity/UnityProject/Assets/RoguehallaNet/Net/Collections.cs:308-340` exposes delivered quest rows and unknown count.

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/TacticalMapEditModeTests.cs` — planning visibility regression.
- `client-unity/RoguehallaNet.Tests/MapScreenTests.cs` — wide planning column layout regression.
- This packet brief.

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TacticalMapEditModeTests.cs`
- `client-unity/RoguehallaNet/Net/Ui/MapScreenLayout.cs` and its synced Unity copy
- `client-unity/RoguehallaNet.Tests/MapScreenTests.cs`
- `docs/backlog/packets/P-ALPHA-PLAN-QUESTS-1.md`

## 6. Contracts and references
- `client-unity/AGENTS.md`: Unity owns presentation only; outcomes and quest visibility come from server observations.
- `docs/CONTRACTS.md` C-OBS: viewers receive only observations they are allowed to see.

## 7. Behaviour to preserve
- Quest names and the unknown count come exclusively from the delivered `QuestView`.
- Keep the planning spawn, boon, ready and route interactions unchanged.
- Never display unrevealed quest names or details.

## 8. Acceptance checks
- EditMode regressions at 1280×720 and 1440×900 deliver five public named rows and an unknown count of five, then check
  all five names and “5 more revealed at raid start” fit the board without scrolling.
- Pure layout tests check the quest and decision columns flank the map without overlap at both desktop sizes.
- Giver zone appears only when it can be derived from the public map region plane and delivered giver coordinates.
- Run the targeted Unity EditMode regression and `scripts/gate.sh --quick --oracle packet/P-ALPHA-PLAN-QUESTS-1`.

## 9. Validation commands
- Unity EditMode tests through `bash client-unity/UnityProject/Tools/editmode.sh`.
- `scripts/gate.sh --quick --oracle packet/P-ALPHA-PLAN-QUESTS-1`.

## 10. Risk and suggested model tier
Sonnet. The visible list uses an independently scrollable panel so it can display all public names within the existing
planning rail.

## 11. Known traps
- `UnknownCount` is server-owned visibility data. Do not derive it from total quest capacity or hidden definitions.
- Do not edit server quest spread or visibility policy in this presentation packet.
