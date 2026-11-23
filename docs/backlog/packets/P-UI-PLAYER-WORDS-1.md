# P-UI-PLAYER-WORDS-1: the map's IN VIEW list speaks in the game's words

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the UI rework lane's chat ("When tab is clicked map layout is weird", "Please do a UI optimization sweep"); seen in Web round 1 of the lane (2026-11-23): the IN VIEW list reads "Haunted · RegionalMob x12".
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head 632ecb349; it took P-UI-QUEST-OFFER-1, which touches none of this packet's files). Lane worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-d`.
- **D-rules touched:** D1: legacy has no IN VIEW list (its left Tab-map column is a ranked player roster, `packages/client/src/ui/mapRoster.ts`) and never prints a creature kind, so this is Unity-only text outside parity; no `DEVIATIONS.md` row is proposed. D2 n/a. D5/D6 none. D9: none (a word for a code the server delivered; the client decides no outcome).

## 1. ID and outcome

Today `TacticalMapController.PresentTactics` (`client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs:532-567`) writes the IN VIEW list (`TacticalMap/TacticalInformation/ContactsPage/DeliveredContacts/Viewport/Content`, created at `:155-161`). For every snapshot actor but the own one it builds `faction + " · " + actor.Kind` (`:549`), finds its row with a `FindIndex` lambda on that string (`:550`), and prints each row as the string plus `" x"` and the count (`:553`). `actor.Kind` is the C# enum `ActorKind` (`client-unity/RoguehallaNet/Net/Views.cs:44`: Player, RegionalMob, Zombie, TrainingDummy, QuestGiver), so a creature reads "RegionalMob": an identifier. Only `Player` has a hand-written word (`:548`). The server sends every ordinary creature, elites included, as RegionalMob, so the list is two rows ("Haunted · RegionalMob x12", "Plague · RegionalMob x8"); a quest giver reads "Neutral · QuestGiver x2" (`SnapshotBuffer.Emit`, `RoguehallaNet/Net/SnapshotBuffer.cs:152`, gives a giver no faction; the controller prints "Neutral" at `:547`). The library has a faction name table (`FactionPresentation.Name`, `RoguehallaNet/Net/Ui/FactionPresentation.cs`, reached through `ShowcasePalette.GetFactionName`) and none for `ActorKind`. The rows come in the order of the lowest actor id of each group (actors arrive by ascending id) and the list is rebuilt from scratch on every snapshot, so when the lowest-id creature of one faction dies or leaves view, two rows swap places. The text is rebuilt only when `Session.SnapshotsReceived` changed (early return at `:537`, `_lastContactsSnapshot` field `:50`), at most 20 times a second, not per frame; today that build allocates a `StringBuilder`, a `List`, and a key string and a closure per non-player actor.

When this lands:

1. **A pure word table.** New class `PlayerWords` (engine-free library) gives the player-facing word for a code value. This packet fills it for `ActorKind` only. Exact words, lower case:

   | ActorKind | Singular | Plural |
   |---|---|---|
   | Player | player | players |
   | RegionalMob | creature | creatures |
   | Zombie | zombie | zombies |
   | TrainingDummy | training dummy | training dummies |
   | QuestGiver | quest giver | quest givers |
   | any other value | contact | contacts |

   "creatures" is the MAP KEY word for the grey disc; "contacts" is the map's own word. A value the table does not name never gives its identifier or a number.
2. **The row.** Every row keeps the middle dot the list already uses between the faction and the rest. A non-player group reads faction, ` · `, the word, one space, `x`, the count: `Haunted · creatures x12`, `Plague · creatures x8`, `Haunted · zombies x3`. The singular word is used for a count of 1, the plural for every other count: `Plague · creature x1`, `Haunted · zombie x1`. A group without a faction (a quest giver) drops the faction and the dot and starts with a capital: `Quest givers x2`, `Quest giver x1`. `PlayerWords.AppendContactRow` writes the row (no newline), so the format is tested without Unity.
3. **Players unchanged.** A player contact still gets its own line `<faction> · Player` (`Plague · Player`), written by the controller exactly as today (`:548`): not through `PlayerWords`, not sorted, appended where the actor is met, so before the group rows. "No other contacts in view." (`:554`), the `TrimEnd('\n')` (`:555`) and the own-actor skip (`:545`) stay. A player with no faction still reads "Neutral · Player" (`:547`).
4. **The grouping key is a value, and the rows have a fixed order.** One row per (faction, kind). The key is a value tuple: the faction as `-1` when the actor has none, else `(int)Faction`; the kind as `(int)actor.Kind`, except that a kind `PlayerWords.IsNamed` rejects becomes 255, so kinds a newer server adds merge into one `contacts` row instead of printing identical rows. The rows are ordered by that key: faction ascending (the faction-less group first), then kind ascending (the merged unnamed group last, as 255 is above every named value). A new group is inserted at its sorted place, a known group's count is changed in place, so a row never changes place because an actor with a lower id entered or left view: only a group appearing or vanishing moves the rows below it. The `FindIndex` lambda and the per-actor key string are gone: a plain loop over a local `List` of tuples. Two unknown factions still give two rows reading "Unknown" (the faction set is closed; untouched), ordered by value.
5. **No new allocation.** The words are constants. PresentTactics builds only when `SnapshotsReceived` changed: the `if (_lastContactsSnapshot == _bound.SnapshotsReceived) return;` (`:537`) precedes every allocation, and `Tick` calls PresentTactics (`:465`) only while the Tab map is open and the 30 Hz throttle allows. The build allocates per snapshot the `StringBuilder`, the `List` and the result string, and nothing per actor. A call with an unchanged snapshot count allocates nothing (oracle test 3 of the EditMode class). Skipping the build when the text is equal is not asked for.
6. **The table's shape for the next packet.** Per code type, methods named for what they return, overloaded by the enum parameter: `Singular(ActorKind)`, `Plural(ActorKind)`, `IsNamed(ActorKind)`. P-UI-PLAYER-WORDS-2 adds `Word(PropAppearance)`, `IsNamed(PropAppearance)` and a line in the dotnet sweep, changing nothing here.

## 2. Dependencies

Base `lane/ui-rework/base-3`. P-UI-TABMAP-MARKS-1 is already in the base. P-UI-ZONE-NAMES-1 edits `TacticalMapMarks.cs` only, so it shares no file with this packet. P-UI-ALLOC-METER-1 (`Assets/Tests/Editor/AllocationMeter.cs`) is in the base.

## 3. Relevant legacy paths

- `packages/client/src/ui/mapRoster.ts`: legacy's left Tab-map column is a ranked player roster; it has no IN VIEW list and prints no creature kind. Nothing to match; the text is Unity-only.

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs` (namespace `Roguehalla.Net.Ui`), its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/PlayerWords.cs` and its `.meta` (made by `bash client-unity/sync-net.sh`, then a Unity run)
- `client-unity/RoguehallaNet.Tests/PlayerWordsTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ContactRailWordsEditModeTests.cs` with `.meta`

API (the stub has the same signatures; the implementer replaces the bodies):

```csharp
/// <summary>Player-facing words for code values the server delivers; no identifier reaches a player.</summary>
public static class PlayerWords
{
    public static bool IsNamed(ActorKind kind);     // true for the five named values, false for any other byte
    public static string Singular(ActorKind kind);  // the table; "contact" for a value that is not named
    public static string Plural(ActorKind kind);    // the table; "contacts" for a value that is not named
    /// <summary>Appends "{faction} · {word} x{count}", the word singular for a count of 1 and plural otherwise;
    /// a null or empty faction writes no faction and no dot, and the word starts with a capital: no newline,
    /// nothing cleared.</summary>
    public static void AppendContactRow(System.Text.StringBuilder text, string faction, ActorKind kind, int count);
}
```

Constants and a `switch` only: no static state, no allocation but the builder's (the capital is written with `char.ToUpperInvariant` and `Append(word, 1, word.Length - 1)`, not a new string). C# 9, netstandard2.1, warnings are errors, a `/// <summary>` before the type. The Unity copy is made by `sync-net.sh`.

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs` and its synced copy and `.meta` (the implementer fills the bodies; the stub commit holds the signatures)
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs`, `PresentTactics` only: the lines from `var text = new StringBuilder();` (`:540`) to `_contactsText.text = ...;` (`:555`) (net change within ten lines)
- Not in scope: the rest of `TacticalMapController.cs` (`PresentPlan` belongs to P-UI-PLAYER-WORDS-2), `TacticalMapController.Sheet.cs`, `UI/TacticalMapMarks.cs`, every `MatchHud*.cs`, `UI/TraderDialogView.cs`, `UI/BuildDrawer.cs`, `HudContentText.cs`, `Net/Views.cs`, the node tools, `CardDisplayCatalog.cs`, `BakedContent.cs`.
- No existing test is amended. The oracle files below are read-only.

## 6. Contracts and references

- Presentation only; Rust decides every outcome (R-AUTH). No wire change, no protocol change.
- Lane rules: `.agent-runs/lane-ui-rework/specs/COMMON.md` (readability gate: a `/// <summary>` before the first type, at most 800 lines; the library is edited in `client-unity/RoguehallaNet` and synced).

## 7. Behaviour to preserve

- The code before the early return in `PresentTactics` (title, `PresentHeader`, `RefreshOwnZone`) and after `_contactsText.text` (counter guide, `ApplyLayout(true)`, quests and traders, `:556-566`) stays. The method keeps its name and no parameters: EditMode test 3 reaches it by name.
- The early return and every `_lastContactsSnapshot` write (`MarkRepaint` `:397`, `MapCollectionsChanged` `:593`, `MapStateChanged` `:598`, `:334`) stay: they make the Tab map current on the frame it opens.
- `shown` still counts every contact but the own one, player rows included; "No other contacts in view." depends on it.
- Player lines: `<faction> · Player`, unsorted, appended where met.
- The Tab-map layout (`ContactsPage`, `InViewHeading` nodes) is untouched.

### Existing tests that pin this code (all stay green; verified by reading each)

- `TabMapFlowEditModeTests.TheMapIsPaintedOnTheFrameItOpens` (`Assets/Tests/Editor/TabMapFlowEditModeTests.cs:277-300`): one Faction 2 actor of Kind 0 (Player); asserts `StringAssert.Contains("Plague · Player", ContactsText())` (`:295`) and no "No other contacts" (`:296`).
- `TacticalMapEditModeTests` (`Assets/Tests/Editor/TacticalMapEditModeTests.cs:76`, `:99-100`): `DisconnectAndRebindClearMarksAndRefreshSingleObservedContactRail` asserts `Contains("Plague · Player")` and no "No other contacts"; `:76` asserts no "No other contacts" for a delivered contact.
- `DeclutterLegendEditModeTests.TheKeyListsCampBuffAndEliteCampAndInViewStartsBelowTheLastKeyRow` (`Assets/Tests/Editor/DeclutterLegendEditModeTests.cs:47-58`): reads the nodes `ContactsPage` and `InViewHeading` only; not the text.
- `TabMapSheetEditModeTests` (`:190`, `:333-343`): the `ContactsPage` layout rect and look; not the text.
- `TabMapHeaderNoGarbageEditModeTests` (`PresentHeader` allocation, via a delegate): the pattern the new allocation test copies; not changed by this packet.
- No `Tools/*.test.cjs` reads these texts. Other grep hits for " · " are unrelated (`InspectorCardTests`, `HelpGuideViewEditModeTests`, `HudTextScaleEditModeTests`).
- No existing test turns red.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-PLAYER-WORDS-1 --base lane/ui-rework/base-3` passes.
- `dotnet test client-unity/RoguehallaNet.Tests --filter PlayerWordsTests` green; Unity class `ContactRailWordsEditModeTests` green; `bash client-unity/sync-net.sh --check` clean.
- `TabMapFlowEditModeTests`, `TacticalMapEditModeTests`, `DeclutterLegendEditModeTests`, `TabMapSheetEditModeTests`, `TabMapHeaderNoGarbageEditModeTests` still green.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/PlayerWordsTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ContactRailWordsEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ContactRailWordsEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-PLAYER-WORDS-1.md`

The `stub:` commit holds `PlayerWords.cs` (library and Unity copy, with the copy's `.meta`): the implementer edits these.

### Tests

dotnet `PlayerWordsTests` (NUnit, namespace `Roguehalla.Net.Tests`; all red on the stub, on assertions):
1. `EveryActorKindHasAPlayerWord`: for each defined `ActorKind` and for `(ActorKind)5` and `(ActorKind)255`: `Singular` and `Plural` are not null or empty, equal their `Trim()`, contain no `_` and do not match `[a-z][A-Z]`. `IsNamed` is true for each defined value (a value added to the enum without a row fails here) and false for 5 and 255. For a defined value whose identifier itself matches `[a-z][A-Z]` (RegionalMob, TrainingDummy, QuestGiver), neither word equals the identifier ignoring case, spaces kept.
2. `TheWordsAreTheOnesTheOwnerReads`: the table above, pair by pair.
3. `AnUnknownKindGetsANeutralWord`: `(ActorKind)5` and `(ActorKind)255` give `contact` and `contacts`.
4. `ARowReadsFactionWordAndCount`: the row examples of the outcome, plus ("Plague", TrainingDummy, 1) `Plague · training dummy x1`, ("Plague", TrainingDummy, 2) `Plague · training dummies x2`, ("Fire", (ActorKind)9, 4) `Fire · contacts x4`, ("Fire", (ActorKind)9, 1) `Fire · contact x1`, (null, QuestGiver, 2) `Quest givers x2`, ("", QuestGiver, 1) `Quest giver x1`, (null, RegionalMob, 1) `Creature x1`, a builder holding `"A\n"` gives `A\nHaunted · creatures x12`; no result contains `RegionalMob` and none ends in a newline.

EditMode `ContactRailWordsEditModeTests` (rig copied from `TabMapFlowEditModeTests`: `Frame(dtMs, snapshot)`, `Snapshot()`, `ContactsText()`; own player id 1; faction 1 reads "Haunted", faction 2 "Plague"):
1. `CreaturesReadAsCreaturesNotAsTheCodeName`: ids 3,4,5 Faction 1 and ids 6,7 Faction 2, all RegionalMob; map open; text is exactly `Haunted · creatures x3\nPlague · creatures x2`, no `RegionalMob`.
2. `EveryKindGetsItsOwnWordsInAFixedOrder`: id 2 Faction 1 RegionalMob; id 3 Faction 2 Player; id 4 Faction 1 Zombie; id 5 QuestGiver (`Faction = 15, Weapon = 15`); id 6 Faction 1 RegionalMob; ids 7 and 8 Faction 1 Kind 5 and 6; id 9 Faction 2 RegionalMob. Exactly `Plague · Player\nQuest giver x1\nHaunted · creatures x2\nHaunted · zombie x1\nHaunted · contacts x2\nPlague · creature x1`.
3. `AnUnchangedSnapshotAllocatesNothing`: the text is test 1's; `PresentTactics` through a delegate built once; five warm-up calls; `AllocationMeter.AssertNone(present, 200, ...)`; text unchanged. Guards the early return against a build moved above it.
4. `ARowKeepsItsPlaceWhenActorIdsChange`: delivery 1 id 3 Faction 2, ids 4, 5 Faction 1 (all RegionalMob): `Haunted · creatures x2\nPlague · creature x1`; delivery 2 id 2 Faction 2, ids 5, 6 Faction 1: same string.

## 9. Validation commands

- `in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter PlayerWordsTests`
- `in.sh <worktree> bash client-unity/sync-net.sh --check`
- `unity-class.sh <worktree> ContactRailWordsEditModeTests,TabMapFlowEditModeTests,TacticalMapEditModeTests,DeclutterLegendEditModeTests`
- `in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-PLAYER-WORDS-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Low. A pure table and one text block; implementer-sonnet, reviewer. The one subtle part is the ordered insert (tuple key, no row string).

## 11. Known traps

- Edit only the text block of `PresentTactics`. Keep the method's name and no parameters.
- The key is the value tuple: do not build the row string to compare it. Find the sorted place with a loop (`while` the stored key is below the new one), then either change the count by index (`groups[at] = (f, k, groups[at].Count + 1)`) or `Insert(at, (f, k, 1))`. Write the rows after the loop, each through `AppendContactRow`, then `"\n"`, and keep the `TrimEnd('\n')`.
- The words are lower case on purpose (a row is a phrase); only a row without a faction capitalises its first letter.
- The test assembly cannot see `internal` members: `PlayerWords` is `public static`. `AllocationMeter` is used as `TabMapHeaderNoGarbageEditModeTests` does; never set `MeterOverride`.
- Run `bash client-unity/sync-net.sh` through the wrapper after editing the library file (`--check` is in the gate). A packet branch stays linear.
- A faction-less contact that is a player still prints "Neutral · Player"; only the grouped rows drop the faction (the controller passes null for a missing faction to `AppendContactRow`, and the group key uses -1).

## Not established

- How the list looks. The look is judged in the lane's next Web round; nothing here claims it reads well on screen.
- The player lines are not sorted: they stay in the order met (ascending actor id), as today, so two player lines can still swap when the id of one changes. Only the group rows have a fixed order.
- That `PresentHeader`, `RefreshOwnZone` and the title write allocate nothing in the EditMode rig (see the scratch-run note below).

## Left for P-UI-PLAYER-WORDS-2 (not changed here)

`HudContentText.WishName` ("LastStand", "Blaze"), `MatchHud.cs` pickup prompt (`Focus.Kind.ToString()`) and build tip, `HudLateUpdate` `_session.State.ToString()`, `TraderDialogView.UpdateSelectedDetail`, `TacticalMapController.PresentPlan` (a Coliseum spawn, Zone 6, reads "Unknown"; `MapZones.Name(Zone)` has it), the quest spell names (open for the owner).

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->

## Notes from the oracle author

(filled in with the oracle commit)
