# P-UI-PLAYER-WORDS-2: no code name reaches a player anywhere in the match HUD

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 in the UI rework lane's chat ("Please do a UI optimization sweep"); seen in Web round 1 of the lane (2026-11-23) on the Tab map ("RegionalMob x12", fixed by P-UI-PLAYER-WORDS-1) and found by the lane's investigation in fifteen more places. Evidence: `understand/web1-tabmap-inview.txt` rows 5 to 16 (lane folder), `docs/backlog/packets/P-UI-PLAYER-WORDS-1.md` ("Left for P-UI-PLAYER-WORDS-2"), and a sweep for every enum `ToString()` or enum joined to a string.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head a4a1bc455 when this packet was written); it holds all of wave 3, P-UI-ABILITY-MOTION-1 included. Nothing else is running or pending before this packet.
- **D-rules touched:** D1: legacy words three of these texts and this packet takes its words (`abilities.ts` ability names, `traders.ts` stat names, `glossary.ts` tag chips); prop, connection and status words have no legacy line (Unity-only, no difference, no DEVIATIONS row). For the three legacy-worded texts no row is proposed either: Unity takes legacy's words. D2 n/a. D5/D6 none. D9 none (words for server codes). No wire, protocol, `crates/` or `packages/` change.
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-r`

## 1. ID and outcome

C# prints an enum's identifier when it is joined to a string. `PlayerWords` (`client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs`) names `ActorKind` only (evidence rows 1 to 4 landed with P-UI-PLAYER-WORDS-1). After this packet every row below reads in the game's words. "Table" is a new `PlayerWords` method: no row has a name the code can read as content (the Welcome's HUD block carries numbers and the Frost Wall flag only; `HudContentText.Wish(...).Name` repeats R1's names, pinned equal by a dotnet test). Gates are named; none needs a new cache.

- **R1 Wish name**, `HudContentText.WishName` (`RoguehallaNet/Net/Ui/HudContentText.cs:117`, used by `MatchHud.FormatWishCard` `MatchHud.cs:362` ("R · LastStand · 1/1", "Casting LastStand", lines 366 and 378) and `MatchHud.WishNameFor` `MatchHud.ActionBar.cs:223` (tile name, hint)): "LastStand" and "Blaze" read "Last Stand" and "Blaze Step"; "Frost Wall" as today. Table `Word(AbilityId)`. Gates: `WishTextKey` (`MatchHud.cs:386`), the `WishNameFor` cache.
- **R2 Pickup prompt and tip**, `MatchHud.RefreshCollections` (`MatchHud.cs:436`, `Focus.Value.Kind.ToString()`): the word after "F  take " is "Wind emblem", not "WindEmblem"; a weapon keeps its name. Table `Word(PropAppearance)`. Gate: the `_focusShown` compare (`MatchHud.cs:430`).
- **R3 Build tip (B)**, `RefreshCollections` (`MatchHud.cs:459`, `text.Append((BoostId)i).Append('+')`): "MaxHp+2  AttackSpeed+1" reads "Max health +2  Attack speed +1" (word, space, plus sign, count; two spaces between entries). Table `Word(BoostId)`, also behind `BoostLabel` (`MatchHud.Chest.cs:86`; its private `BoostNames` array at `:25` is deleted). Gate: `buildChanged`.
- **R4 Trader detail heading**, `TraderDialogView.UpdateSelectedDetail` (`TraderDialogView.cs:378`, `merchant.Name + " · " + merchant.Kind`): "The Ledger · Stats" reads "The Ledger · Stat trader" or "... · Weapon barter"; `MerchantLabel` (`:571`) uses the same call. Table `Word(TradeKind)`. Gate: a selection or rebuild.
- **R5 Connection state.** `MatchHud.HudLateUpdate` else-branch (`MatchHud.cs:271`, `_session.State.ToString()`) writes the plate banner and status and, through `banner`, the first line of the result note (`MatchHud.cs:312`): "Idle", "Connecting", "HelloSent", "Ended", "Detached" read "Not connected", "Connecting…", "Connecting…", "The match has ended.", "Disconnected". `TraderDialogView.StateCopy` default (`:600`, `state.ToString()`, run on every `Refresh`, `:168`) reaches only Idle, Connecting, HelloSent and Detached: its explicit cases stay (Reconnecting, Failed, Rejected, and Ended as "Match ended"), so "The match has ended." reaches a player only in the HUD banner, the plate status and the result note. Table `Word(SessionState)`. Gate: the `_bannerState`/`_bannerAttempt`/`_bannerPhase` compare (`MatchHud.cs:258`). Play never shows Detached: `MatchClient` calls `Hud.ShowMenu()`, which unbinds the HUD and, through `BindPlayerFlows(null)`, the trader view, before it calls `Session.Detach()`. The row stays in the table; the tests reach it with `Detach()` on a bound session.
- **R6 Plate stats line** (`MatchHud.RefreshOwnerPresentation`, `MatchHud.cs:325`, `" · " + me.Status`) **and Ledger owner line** (`BuildDrawer.Refresh`, `BuildDrawer.cs:157`, `me.Status + "  ·  " + me.Weapon ...`): "Mia" reads "Missing in action". Table `Word(ActorStatus)`. Gates: `_lastStatus` (`MatchHud.cs:322`); `SameSelf` (`BuildDrawer.cs`).
- **R7 Trader stat lines**, `TraderDialogView.StatName` (`:576`; used at `:322`): "AD", "AP", "armor", "shield", "move speed" read "Offense", "Soul Offence", "Defense", "Soul Defense", "movement speed" (Rust `Stat::name`, `traders.ts`; "shield" is the game's "Soul Defense"). Table `Word(TradeStat)`. Gate: `RebuildChoices` on a new signature.
- **R8 Card tags**, `ChestOptionText.Describe` (`UnityProject/Assets/Scripts/UI/ChestOptionText.cs:20`, chest kind line via `MatchHud.FillChestCard` `MatchHud.Chest.cs:105`, which writes `kind + "  ·  " + tags.ToUpperInvariant()`) and `BuildDrawer` (`CreateCard` tag line `BuildDrawer.cs:104-105`, `RefreshVisuals` detail `:221`): "OnHit, Dot, Physical" reads "On-hit, Damage over time, Physical" (the card tile line is upper-cased by `CreateCard` as today). Table `CardTags(string)`. Gates: a changed chest signature; the drawer's build and selection.

**Left on purpose** (each named under the hand-off's LEGACY DOUBTS or NOT ESTABLISHED): the Wish fallback row in `ApplyWishRow` (`MatchHud.ActionBar.cs`, no content block; unreachable in play because an owner snapshot follows the Welcome and `Welcome.Hud` is always read; one line in a file CONTROLS-LINE-1 and TILE-PLATES-1 also edit); the Zone 6 plan row in `PresentPlan` (unreachable: the wire refuses a plan option with Zone outside 1 to 5, `Protocol/StateItem.cs` "zone"); the quest spell names WallBuster, BlindRage and StillMind (authored; the server's quest texts use them; the lane lead asks the owner whether they change); the quest item prompt's word (a quest item reads "Quest item"; legacy: "take <quest label>"); weapon, faction and zone names (already words; an unnamed weapon prints a number, `Word(WeaponId)` would have the same shape); the Ledger boost strip "AD AP ARM SHD HP AS SPD LS" (a 36 unit cell: needs a layout); the reason numbers on the Notices page ("Pickup refused by server (3)" is a deliberate log, three tests pin it).

### The tables (constants and a `switch`; the last word of each line is the neutral one for an undefined value)

- `AbilityId`: Freeze "Freeze", Swarm "Swarm", Trial "Trial", Blaze "Blaze Step", LastStand "Last Stand"; neutral "Wish".
- `ActorStatus`: Lobby "Lobby", Alive "Alive", Dead "Dead", Mia "Missing in action"; neutral "Unknown".
- `PropAppearance`: Ground "Weapon", Chest "Chest", Body "Body", Corpse "Corpse", WindEmblem "Wind emblem", Cannon "Travel cannon", QuestItem "Quest item", Heal "Heal pickup", RebirthEgg "Rebirth Egg"; neutral "Item".
- `BoostId`: Ad "Offense", Ap "Soul Offence", Armor "Defense", Shield "Soul Defense", MaxHp "Max health", AttackSpeed "Attack speed", MoveSpeed "Move speed", Lifesteal "Lifesteal"; neutral "Bonus".
- `TradeKind`: Barter "Weapon barter", Stats "Stat trader"; neutral "Trader".
- `TradeStat`: MaxHp "max HP", Ad "Offense", Ap "Soul Offence", AttackSpeed "attack speed", MoveSpeed "movement speed", Armor "Defense", Shield "Soul Defense"; neutral "stat".
- `SessionState`: Idle "Not connected", Connecting "Connecting…", HelloSent "Connecting…", InMatch "In the match", Reconnecting "Reconnecting…", Ended "The match has ended.", Rejected "Connection rejected", Failed "Connection failed", Detached "Disconnected"; neutral "Unknown". The ellipsis is U+2026.
- Card tags: True_ "True", Dot "Damage over time", OnHit "On-hit", Crit "Critical strike"; Physical, Magic, Returned, Slow, Heal, Block, Ramping, Execute, Area, Wish as written (the catalog holds these fourteen tags and no other); an unnamed tag "Other".

### The sweep rule (what `EveryAddedEnumHasAPlayerWord` pins)

For each added enum, every defined value plus one undefined: `IsNamed` is true for defined and false for undefined; each word is non-empty, equals its `Trim()`, has no `_` and no lower-case letter directly followed by an upper-case one; it equals the identifier ignoring case only for a test allow-list (Freeze, Swarm, Trial, Chest, Body, Corpse, Lifesteal, Lobby, Alive, Dead), so a leak fails. Tags: the same rules with their own allow-list, the ten tags written as they are (Physical, Magic, Returned, Slow, Heal, Block, Ramping, Execute, Area, Wish); the "equals its identifier" rule therefore bites only the four translated tags (True_, Dot, OnHit, Crit).

### Fit (`HudUi.Label` texts shrink, none is cut)

Longer by at most: Wish tile name (60 x 26, wraps) +5 ("Blaze Step"), same on card and hint; pickup prompt +7 (21 characters; today's "F  swap Shield for Fists" is 24); build tip entry +11; banner, plate status, result note +15 (20 characters; today's "Planning — choose your spawn" is 28); `_combatStats` +14 (40 in all; sample 28); Ledger status +14; trader heading +7 (34); Ledger card tag line +14 (34 characters on card 65; card 56 grows +24, to 33); chest kind line +14 (46 characters on card 65). The Ledger tag line is the tight one (a 9 to 13 unit font in a box about 88 by 27 units at the narrowest cell): `LedgerWordsEditModeTests.GUARD_EveryCardTagLineFits` measures it at text scales 1 and 1.5.

## 2. Dependencies

The lane base `lane/ui-rework/base-3` (a4a1bc455). `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`, P-UI-ALLOC-METER-1) is on it. No other packet is needed first. Ordering (the lane lead's order for wave 4): round 1, side by side with P-UI-CONTROLS-LINE-1 (edits `MatchHud.ActionBar.cs`, `MatchHud.Tutorial.cs`) and P-UI-LEFT-EDGE-1 (edits `MatchHud.Legacy.cs`, `.Features.cs`, `.Terrain.cs`, `UiDisplayPreferences.cs` and its own new files); this packet shares no file with either. Round 2 is P-UI-TILE-PLATES-1 (adds one test to `WishTileEditModeTests`, amends `AbilityRowEditModeTests`) and round 3 is P-UI-TEXT-FIT-1 (edits `HudTextScaleEditModeTests`, `HudLegacyLayoutEditModeTests`); neither shares a file with this packet.

## 3. Relevant legacy paths

D1 for three texts (verified by the lane lead in the spec; the oracle author did not re-read `packages/`, which is read-only and not needed for these words):
- `packages/shared/src/abilities.ts` ability names ("Blaze Step", "Last Stand").
- `packages/shared/src/traders.ts` stat names ("Offense", "Soul Offence", "Defense", "Soul Defense", "movement speed"); Rust `Stat::name` repeats them.
- `packages/client/src/ui/glossary.ts` tag chips ("On-hit", "Damage over time", "Critical strike").
Prop, connection and status words have no legacy line.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/OwnerWordsEditModeTests.cs`, `HudWordsEditModeTests.cs`, `TraderWordsEditModeTests.cs`, `LedgerWordsEditModeTests.cs` (each with its `.meta`).
- No new non-test file: `PlayerWords.cs` exists.

## 5. Exclusive write scope

- Library, with synced copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (run `bash client-unity/sync-net.sh` through the lane's `in.sh`; no new `.meta`): `client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs`, `client-unity/RoguehallaNet/Net/Ui/HudContentText.cs` (`WishName` only).
- `client-unity/UnityProject/Assets/Scripts/UI/`: `MatchHud.cs` (four lines: the `HudLateUpdate` banner else-branch `:271`, the `_combatStats` line `:325`, the pickup `what` `:438`, the build tip `:459`; stays under 600 lines, 572 today, no new method), `MatchHud.Chest.cs` (delete `BoostNames`; `BoostLabel` returns `PlayerWords.Word(id)`), `TraderDialogView.cs` (`UpdateSelectedDetail`, `MerchantLabel`, `StatName`, `StateCopy`), `BuildDrawer.cs` (`Refresh` status, `CreateCard` tags, `RefreshVisuals` detail; `GlyphFor` keeps the raw tags; the `CreateCard` tag and description rects only as `GUARD_EveryCardTagLineFits`' remedy allows), `ChestOptionText.cs` (add `using Roguehalla.Net.Ui;`).
- Tests: `client-unity/RoguehallaNet.Tests/PlayerWordsTests.cs` (cases appended); the four new `Assets/Tests/Editor/*WordsEditModeTests.cs` with `.meta`; the amended tests listed below; this packet.
- **Not in scope:** `MatchHud.ActionBar.cs`, `.Features.cs`, `.IconPresentation.cs`, `TacticalMapController*.cs`, `OrderedNoticeFeed.cs`, `Presentation/**`, `HudTextScaleEditModeTests.cs`, `hud-seams.test.cjs`, `CardDisplayCatalog.cs`.

## 6. Contracts and references

- Presentation only: Unity holds no game rule; every outcome comes from server observations (R-AUTH, ADR 0008). No change to wire, layout, keys or colours.
- `client-unity/UnityProject/Assets/Scripts/UI/CardDisplayCatalog.cs` is never edited (a Rust test reads it); its `Tags` stay raw.
- Readability gate: `MatchHud.cs` stays under 600 lines (572 today); no method added to a `MatchHud*.cs` file (`hud-seams.test.cjs` rejects a name declared twice).
- Owner decisions: the control mapping is untouched (no key is bound or changed). Searched `docs/OWNER_DECISIONS.md` for these words: no row governs them.

## 7. Behaviour to preserve

- Proper names stay: Freeze, Swarm, Trial, Frost Wall, weapon names, quest spell names.
- `StateCopy` keeps its four explicit cases ("Connection interrupted · resuming", "Connection failed", "Connection rejected", "Match ended"). `TraderCopyEditModeTests` pins "Match ended" and bans "server", "authoritative", "delivered", "session" in dialog text; the new words contain none.
- The Reconnecting banner keeps "Reconnecting… (n/m)" (`GUARD_ReconnectingKeepsItsBanner`).
- `CardDisplayCatalog.Tags` stay raw: `BuildDrawer.GlyphFor` and `BuildDrawerEditModeTests.DisplayCatalogExactlyMirrorsRustCardIdsTitlesDescriptionsAndTags` read them.
- Old defects, left: the pickup prompt ignores a change of held weapon while the focus stays; a focused Body ignores its weapon hint (`BodyHint`).
- No allocation per frame: `Word` allocates nothing (meter test); `CardTags` runs only behind R8's gates.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-PLAYER-WORDS-2` prints `GATE PASS` (run through `in.sh`, once at the end).
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through `in.sh` passes (no method added to a `MatchHud*.cs` file; `MatchHud.cs` under 600 lines).
- The four amended tests below pass with their new expectations; the guards listed under "Existing tests that stay green" stay green.
- Hand-off: LEGACY DOUBTS / NOT ESTABLISHED name the "Left on purpose" list above. For P-UI-TEXT-FIT-1 (runs last): the fit samples of `HudTextScaleEditModeTests` keep the old words after this packet (this packet does not edit that class); its `PlateCombatStats` sample (`:153`, "Kills 123 · GV 12345 · Alive") becomes the 40-character text, its `ConnectionBanner`/`PlateStatus` samples (`:145`, `:151`) and `PickupHint` sample (`:186`) take the new words.

## Existing tests that pin what this packet changes (verified by reading each)

Turn red, each amended alone in its own `oracle-amend:` commit (new expectation in brackets):
- `client-unity/RoguehallaNet.Tests/WishTextTests.cs` `UnderFreezeEveryWishStringIsTodays` (:136) and `UnderTheWallEveryOtherWishIsUnchanged` (:178): `WishName(hud, a) == a.ToString()` for every ability (the five words, from a table in the test). The other `WishName` lines (:88, :111, :151, :163) are Freeze or Frost Wall and stay true.
- `UnityProject/Assets/Tests/Editor/MatchHudWishBarrierEditModeTests.cs` `OwnerFactsRenderAndClearAcrossWishAndSessionResetTransitions`: :84 and :95 read "Casting Blaze · 2" (["Casting Blaze Step · 2"]); :101 `DoesNotContain("Casting Blaze ·")` would go vacuous (["Casting Blaze Step ·"]; its `Contains("Casting Blaze")` stays true). The later `DoesNotContain("Casting Blaze")` lines (:112 and the last) stay valid; :79 and the other `Contains("Blaze")` lines stay.
- `MatchHudIconPresentationEditModeTests.WishSummaryStaysCompactWhileFullDeliveredDescriptionRemainsFocusedAndDismissible` (:129): "Casting Blaze · 2" (["Casting Blaze Step · 2"]).
- `TraderDialogViewEditModeTests.DuplicateMerchantRowsLabelsAndSignedStatDeltasRenderInSourceOrder` (:120, :121): "AD  -1.25" and "AD  0.5" (["Offense  -1.25"], ["Offense  0.5"]); :122 "max HP  -12" stays.

Stay green (read and checked; each is a guard for this change):
- `TraderCopyEditModeTests` (`ASessionNotInAMatchShowsTheStateCopyAlone` pins "Connection interrupted · resuming" and "Match ended"; `NoPlayerFacingTextMentionsTheServer` bans four words in dialog text).
- `BuildDrawerEditModeTests` (`DisplayCatalogExactlyMirrorsRust...` reads raw `display.Tags`; the detail tests use `StartsWith(title)`); `AugmentUi2Tests.TheChestPanelShowsANativeOptionByNameAndLeavesOtherKindsAlone` (Kind, Title, Text only; no Tags assertion).
- `WishTileEditModeTests.TheLongestWishNameFits` (reads `HudContentText.WishName`, so measures the new names; no amendment); `AbilityRowEditModeTests`, `ShowcaseStateEditModeTests` (`Contains("BUILD  ")`), `PromptPairNarrowEditModeTests`, `HudTextScaleEditModeTests` (old-word samples; measure nothing of the new strings), `HelpGuideViewEditModeTests` (glossary words), `ConnectionCopyTests` (`ConnectionCopy.ForState` is not touched).
- `hud-seams.test.cjs` (names and the 600-line cap).

## 9. Validation commands

- `bash .agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter PlayerWords` (and `WishText`).
- `bash .agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> OwnerWordsEditModeTests,HudWordsEditModeTests,TraderWordsEditModeTests` (and `LedgerWordsEditModeTests`, `TraderDialogViewEditModeTests`, `MatchHudWishBarrierEditModeTests`, `MatchHudIconPresentationEditModeTests`, `TraderCopyEditModeTests`, `BuildDrawerEditModeTests`, `WishTileEditModeTests`).
- `in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`.
- `in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-PLAYER-WORDS-2` once at the end.

## 10. Risk and suggested model tier

implementer-sonnet, reviewer. Many small call sites in six files and one library file; the only real risk is the fit of the longer words (Ledger tag line, chest kind line) and an enum word that leaks through a path the tests do not reach.

## 11. Known traps

- Call each `Word` with its enum type (a bare `byte` does not compile). Add no method to a `MatchHud*.cs` file: `hud-seams.test.cjs` rejects a name declared twice and caps `MatchHud.cs` under 600 lines (572 today).
- The tests are a separate assembly with no `InternalsVisibleTo`: `PlayerWords`, its new methods and `ChestOptionText` are `public`.
- `TradeKind` and `TradeStat` live in `Roguehalla.Net.Protocol` (`Protocol/State.cs:247-248`): add the `using` to `PlayerWords.cs`. `PropAppearance`, `AbilityId`, `BoostId` are in `Roguehalla.Net` (`Net/Collections.cs`), `ActorStatus` in `Net/Views.cs`, `SessionState` in `Net/Session.cs:32`.
- `CardTags` splits on ',', trims, skips a blank piece, joins with ", "; "" for null or blank. `HudContentText.WishName` ends `: PlayerWords.Word(ability)`. `ChestOptionText.Describe` returns `PlayerWords.CardTags(entry.Tags)` as `Tags` (the file is linked into the dotnet project: no UnityEngine).
- Keep `switch` statements (no dictionary, no allocation).
- `CardDisplayCatalog.Tags` stay raw; never edit that file; translate at the two display sites and in `ChestOptionText` only.
- The fit guard's remedy (only if `GUARD_EveryCardTagLineFits` is red after the feature): in `BuildDrawer.CreateCard` raise the `CardTags` rect (`.05` to `.24` top) and lower the `CardDescription` rect to match (`.22` to `.26` bottom), then re-run the guard. The detail line is a scroll text and grows without limit.
- A rig restores `UiTheme.Current`, `EventSystem.current`, `ShowcaseSettings`: copy that; no `MeterOverride`.

## Oracle files (read-only for implementers)

(Filled in with the `oracle:` commit.)

## Notes from the oracle author

(Filled in with the `oracle:` commit.)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
