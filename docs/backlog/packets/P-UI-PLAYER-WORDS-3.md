# P-UI-PLAYER-WORDS-3: where legacy has a word for the same thing on the same surface, the HUD prints it

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane) and AGENTS.md (legacy behaviour is
  the oracle; an intentional difference gets a DEVIATIONS row). The wave 4 check found that P-UI-PLAYER-WORDS-2 replaced
  internal names by words that differ from legacy's, without anyone choosing the difference.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-3` at `fdd42f54a` (lane worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-y`)
- **D-rules touched:** D1: presentation only. This packet moves the chest card's boost words (`packages/shared/src/chestBoosts.ts:28-31`)
  and the Ended word to legacy's. No DEVIATIONS row is proposed; the proposed row 2 of the P-UI-PLAYER-WORDS-2 hand-off
  (the four boost words) is WITHDRAWN; its row 1 (status words) stays. KEPT rows wait for the owner (DEV-023, DEV-029 and DEV-018
  are signed and cover the GAINED strip text, Frost Wall and the inspector). D2 n/a. D5/D6 none. D9 none (no rule, key or input).

## 1. ID and outcome

The rule (lane lead): where the legacy game has a word for the same thing ON THE SAME SURFACE, the match HUD prints that word,
letter for letter. A word from another legacy surface is not a counterpart. Where the lane lead keeps a Unity word although a
same-surface legacy word exists, the row is KEPT (his recorded exception, 2026-11-23): unchanged, listed for the owner.

Today `PlayerWords` (`client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs`) holds one boost table, `Word(BoostId)` (:173-187), read
on two Unity surfaces: the chest option button (`MatchHud.cs:504`, through `BoostLabel`, `MatchHud.Chest.cs:85`) and the B-key
build tip (`MatchHud.cs:459`, chest-boost pick counts). Only the chest card has a legacy counterpart: it prints "Max health",
"Attack speed", "Move speed", "Lifesteal" where legacy's card title says "Max HP", "Attack Speed", "Move Speed", "Leech". The build
tip has no same-surface legacy word and keeps the table. Second, `Word(SessionState.Ended)` (:271) is "The match has ended." where
legacy's status line says "Run over" (`packages/client/src/ui/hud.ts:54`); Unity's arena line already prints "Run over"
(`Indicators.cs:194`).

When this lands: the chest option button reads the four legacy titles; the Ended banner pill, plate status and result note read
"Run over"; nothing else changes.

## 2. Dependencies

none (base `lane/ui-rework/base-3`).

## 3. Relevant legacy paths

- `packages/shared/src/chestBoosts.ts:24-31`: `CHEST_BOOST_DEFS` titles: Offense, Soul Offence, Defense, Soul Defense, Max HP, Attack Speed, Move Speed, Leech.
- `packages/client/src/ui/hud.ts:54`: the status line, `"Run over"` / `` `Run over — ${s.winner} is victorious` ``.
- `packages/shared/src/abilities.ts:175,185,199,212,226`: Wish `name:` Freeze, Swarm, Trial, Blaze Step, Last Stand (shown by `abilitySlot.ts:274-276`).
- `packages/server/src/sim/consumables.ts:178`: container label `"Wind emblem · walk over it"` (the word before " · " is the Wind emblem row's Legacy).
- `packages/shared/src/traders.ts:13-14`: trader stat words max HP, Offense, Soul Offence, attack speed, movement speed, Defense, Soul Defense (offer label `server/src/sim/traders.ts:54`, shown `ui/traderPanel.ts:54`).
- `packages/shared/src/glossary.ts:20-72` `term:` (and `tagLabel` :100-102; chip `ui/chestMath.ts:252`): the 14 tag words.
- `packages/client/src/ui/buildModel.ts:171-179` (`label:`) and `ui/buildHud.ts:154-157` (`FULL_BUILD_LABELS`, used :132): GAINED words.
- Verified by reading on 2026-11-23 against the base `packages/`: every line above says what is quoted.

### THE TABLE

U is the word today, L legacy's on the counterpart surface. Verdicts: SAME; DIFFERENT (changed here); DOUBT (L looks wrong;
matched as it stands, flagged); NEARBY (same word on another legacy surface; listed, not pinned); NONE (no legacy counterpart;
unchanged; owner list); KEPT (a same-surface L differs; unchanged; owner list).

- **T1 Wish names**, `AbilityId` (wish tile, card, hint; `HudContentText.WishName`, `HudContentText.cs:117`): U Freeze, Swarm,
  Trial, Blaze Step, Last Stand = L. SAME. "Frost Wall" (`HudContentText.cs:71,119`): NONE in `packages/`; DEV-029 (`DEVIATIONS.md:55`)
  fixes it; legacy's Frost passive "Wall of Ice" (`ui/hub/championGuide.ts:90`) is the hub guide, another surface.
- **T2 Status**, `ActorStatus` (plate stats line `MatchHud.cs:325`, Ledger owner line `BuildDrawer.cs:158`): U Lobby, Alive, Dead,
  Missing in action. L prints the server code `me.status` ("lobby", "alive", "dead", "mia"; `hud.ts:83`). KEPT: capitals, and Missing in
  action in substance. "LEFT THE RUN" (`results.ts:11`) is another surface. Row 1 of -2's proposed DEVIATIONS stands.
- **T3 Props**, `PropAppearance` (pickup prompt `MatchHud.cs:440`, "F  take <word>"). Legacy's prompt is `take ${label}`
  (`ui/weaponBar.ts:187-191`), label from `scenes/RaidScene.ts:814`.
  - Chest "Chest", Cannon "Travel cannon": KEPT. L "Open chest", "Enter cannon", so legacy prints "take Open chest": a LEGACY DOUBT.
    The lane lead recommends to the owner "Open chest" and "Enter cannon" without "take"; nothing changes until he says.
  - Wind emblem: SAME (word before " · "); the suffix is a hint Unity prints nowhere (owner list).
  - Weapon, Body: NEARBY (`ContainerView.ts:28`). Heal pickup: KEPT (L "Camp potion"/"Small potion", `shared/src/consumables.ts:6-7`).
    Quest item: NONE. Corpse: NONE. Rebirth Egg: NONE.
- **T4 Boosts**, `BoostId`, two Unity surfaces. **Chest card** (new `PlayerWords.ChestTitle(BoostId)`): Ad "Offense", Ap "Soul Offence",
  Armor "Defense", Shield "Soul Defense" = L, SAME; MaxHp "Max health" to **"Max HP"**, AttackSpeed "Attack speed" to **"Attack Speed"**,
  MoveSpeed "Move speed" to **"Move Speed"**, Lifesteal "Lifesteal" to **"Leech"**: DIFFERENT. The card's " x1.2" suffix
  (`shared/src/rewards.ts:161`) is not a table word; Unity prints none (owner list). **Build tip** (`Word(BoostId)`, unchanged, read at
  `MatchHud.cs:459`): NONE, all eight words; `Word(BoostId)` stays by the lane lead's ruling, not because it matches. Not pinned; its
  tests are standing guards that hold its words.
- **T5 DOUBT:** "Soul Offence" (T4 Ap, T6 Ap) is legacy's spelling beside "Offense" and "Soul Defense". Matched as it is, flagged.
- **T6 Trader stats**, `TradeStat`: max HP, Offense, Soul Offence, attack speed, movement speed, Defense, Soul Defense = L. SAME.
- **T7 Trade kind**, `TradeKind`: Weapon barter, Stat trader. NONE.
- **T8 Card tags**, `CardTag` (14): Physical, Magic, True, Damage over time, On-hit, Returned, Slow, Heal, Block, Ramping, Execute,
  Critical strike, Area, Wish = L `term:`. SAME in words. Capitals: legacy's chip is not upper-cased (`chestPanel.css:92-100`);
  Unity's callers upper-case it (`MatchHud.Chest.cs:109`, `BuildDrawer.cs:105`). KEPT for the chest card (capitals only).
- **T9 Session**, `SessionState` (banner pill `MatchHud.cs:271`, plate status, result note `MatchHud.cs:311`): **Ended** "The match has
  ended." to **"Run over"**: DIFFERENT. Connecting/HelloSent "Connecting…": KEPT (L "Joining run…", `client/src/main.ts:122`, the loading
  overlay). Reconnecting: KEPT (L pill "Connection lost — reconnecting…", `ui/banner.ts:5`); no caller reaches `Word(Reconnecting)`.
  Not connected, In the match, Connection rejected, Connection failed, Disconnected: NONE.
- **T10 Contact rail**, `ActorKind` (Tab map IN VIEW): NONE (`mapRoster.ts:51` is a ranked roster; minimap legend says "mobs",
  `ui/minimap.ts:179`; "creature" against "mob" is on the owner list).
- **T11 GAINED strip** (`BuildHudText.CellLabels`, `BuildHudText.cs:31-32`; copy `MatchHud.Legacy.cs:88`): Offense, SO, Damage gained,
  Attack speed, Life steal, Health gained, Defense, Soul Defense, Move speed. Legacy's strip prints no label; the visible word is the
  tooltip's first words, `FULL_BUILD_LABELS` (`buildHud.ts:154-157`). Six agree (Offense, Damage gained, Attack speed, Life steal, Health
  gained, Move speed): SAME. Three differ (SO, Defense, Soul Defense against "Soul Offence", "Defense gained", "Soul Defense gained"):
  KEPT; DEV-023 (`DEVIATIONS.md:49`) fixes the strip's text as names beside the icons.

### Code change

1. In `PlayerWords.cs` (library file and synced copy): a new `ChestTitle(BoostId)` after `Word(BoostId)`, same shape, the T4 titles and
   "Bonus" for any other byte, under the comment line `// Legacy chest-card titles: packages/shared/src/chestBoosts.ts:24-31.`;
   and one `case` line, `SessionState.Ended` returns "Run over".
2. In `MatchHud.Chest.cs:85`, `BoostLabel` returns `PlayerWords.ChestTitle(id)` (one product caller `MatchHud.cs:504`, one test caller
   `Ui4EditModeTests`). `Word(BoostId)` stays; the build tip keeps reading it. `ChestOptionText` reads no boost word.
3. Nothing is longer: "Run over", "Max HP", "Leech" are shorter; "Attack Speed" and "Move Speed" keep their characters. No box is
   resized, no fit test added. The chest option label is not a `HudUi.Label` (loop at `MatchHud.Chest.cs:73` autosizes to 60 percent).
4. No key, no behaviour, no layout change. Left on purpose: `TraderDialogView.StateCopy` "Match ended" (:587), `ConnectionCopy.cs:49`
   "The match has ended.", `MatchHud.cs:55` "Match active"/"Match ended" (the phase table).

## 4. New paths

- `client-unity/RoguehallaNet.Tests/LegacyWordsTests.cs` (oracle).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/PlayerWords.cs` (`ChestTitle`, its comment line, `Word(SessionState)`'s `Ended` line) and its synced
  copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/PlayerWords.cs` (run `sync-net.sh`; the `.meta` exists).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Chest.cs`: line 85 only (`BoostLabel`'s body). No member renamed or moved.
- Not in scope: every other `MatchHud*.cs`, `TraderDialogView.cs`, `ConnectionCopy.cs`, `BuildHudText.cs`, `HudTextScaleEditModeTests.cs`,
  `hud-seams.test.cjs`, `docs/DEVIATIONS.md`, `CardDisplayCatalog.cs`, and the files of the packets running beside this one.

## 6. Contracts and references

- Presentation only (no wire, no `crates/`). `sync-net.sh --check` is in the gate.

## 7. Behaviour to preserve

- Strings of rows SAME, DOUBT, NEARBY, NONE and KEPT are untouched. `Word(BoostId)`, `IsNamed`, neutral "Bonus", "Soul Offence" stay.
  Only `Word(SessionState.Ended)` changes among session words. "Run over" has no full stop. Case-sensitive: "Max HP", "Attack Speed",
  "Move Speed", "Leech".

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-PLAYER-WORDS-3 --base lane/ui-rework/base-3` prints GATE PASS.
- Hand-off NOT ESTABLISHED says: the plate can read "Match ended" (phase strip, `MatchHud.cs:55`) and then "Run over" for the same end
  (a code path, not seen in play); the Ended pill hides the winner line (`SyncTopCentre`, `MatchHud.TopCentre.cs:34`); P-UI-TEXT-FIT-1's
  spec names the old Ended word as a longest-text input and must re-read the base.

### For the owner (not changed here)

(a) Status words (T2). (b) "Match" or "run" (T9): 51 string literals say run, 26 match on the base; after this packet 52 and 25; the
phase table's "Match active"/"Match ended" (`MatchHud.cs:55`) is the lane lead's open question. (c) Chest and cannon prompt with the
recommendation, Heal pickup, Quest item, Wind emblem suffix (T3); tag capitals (T8; dropping `.ToUpperInvariant()` at
`MatchHud.Chest.cs:109` would match legacy); the GAINED three (T11); "Soul Offence" (T5); "creature" against "mob" (T10); the card's
missing x1.2 suffix. (j) The same boost reads "Leech" on the chest card and "Lifesteal" in the build tip (likewise "Max HP"/"Max
health", "Attack Speed"/"Attack speed", "Move Speed"/"Move speed"): he may want one word. (k) No legacy counterpart: Frost Wall (DEV-029),
Corpse, Rebirth Egg, Stat trader, Weapon barter, contact rail words, Not connected, In the match, Connection rejected/failed,
Disconnected, the Ledger strip "AD AP ARM SHD HP AS SPD LS" (`BuildDrawer.cs:23`). (l) Seen, not changed: `Indicators.StatLabel` (:115, no
caller); `InspectorCard.StatLabels` (:36, DEV-018). (m) While Ended the banner pill shows and the phase pill hides, so the winner line
`Indicators.PhaseLine` builds is hidden (`MatchHud.TopCentre.cs:34`): a later packet.

## Existing tests that pin this code

- `RoguehallaNet.Tests/PlayerWordsTests.TheWordsAreTheOnesTheGameReads` (:147): asserts every table word; line 161 asserts Ended "The
  match has ended." (amended); lines 155-156 assert `Word(BoostId)` (NOT edited).
- `PlayerWordsTests.EveryAddedEnumHasAPlayerWord` (:125): word rules for every table; `LeaveAsIs` (:97) lists "Lifesteal". Green, untouched.
- `HudWordsEditModeTests.TheBannerSpeaksInWords` (:168-170): banner, plate status and result note after MatchEnded (amended to "Run over").
- `Ui4EditModeTests.ChestBoostsReadAsStatNamesNotEnumNames` (:35-37): `MatchHud.BoostLabel` words (amended).
- `MatchHudTrophyEditModeTests.DecodedGainOnlyChangeRefreshesActualHudAndOrdinaryChooseKeepsHandleAndIndex` (:53): option 2 button text
  "Lifesteal" (amended to "Leech").
- Standing guards, green and still green: `HudWordsEditModeTests.TheBuildTipNamesTheBoosts` (:143) and the `Word(BoostId)` line hold the build
  tip; `HudWordsEditModeTests.ThePickupPromptNamesTheProp` holds Chest and Cannon; `ConnectionCopyTests.EndedIsNotANetworkErrorAndStatesMapToTheRightCopy`,
  `TraderCopyEditModeTests` (:93), `HudStatusTextTests` (:44-48) hold the other Ended words; `BuildHudTextTests` :33 pins all nine
  GAINED labels; `HudLegacyLayoutEditModeTests` :194, `InspectorCardTests` :57-58, `OwnerWordsEditModeTests`, `TraderWordsEditModeTests`.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/LegacyWordsTests.cs`
- `client-unity/RoguehallaNet.Tests/PlayerWordsTests.cs` (new test `TheChestCardTitlesAreTheOnesTheGameReads` in the `oracle:` commit; line 161 in an `oracle-amend:` commit)
- `client-unity/UnityProject/Assets/Tests/Editor/HudWordsEditModeTests.cs` (`oracle-amend:`, :168-170)
- `client-unity/UnityProject/Assets/Tests/Editor/Ui4EditModeTests.cs` (`oracle-amend:`, :35-37)
- `client-unity/UnityProject/Assets/Tests/Editor/MatchHudTrophyEditModeTests.cs` (`oracle-amend:`, :53)
- `docs/backlog/packets/P-UI-PLAYER-WORDS-3.md`

### Amended expectations (for the owner; each a move toward legacy)

1. `PlayerWordsTests.cs:161` Ended "The match has ended." to "Run over" (kind: ruling B, legacy `hud.ts:54`).
2. `HudWordsEditModeTests.cs:168-170` banner and plate status to "Run over"; result note to "Run over\nNo authoritative result received." (ruling B).
3. `Ui4EditModeTests.cs:35-37` "Max health", "Attack speed", "Lifesteal" to "Max HP", "Attack Speed", "Leech" (ruling A, chest card).
4. `MatchHudTrophyEditModeTests.cs:53` "Lifesteal" to "Leech" (ruling A).

## 9. Validation commands

- `dotnet test client-unity/RoguehallaNet.Tests --filter "LegacyWordsTests|PlayerWordsTests"` (via in.sh).
- Unity: `unity-class.sh <worktree> HudWordsEditModeTests,Ui4EditModeTests,MatchHudTrophyEditModeTests`.
- `bash client-unity/sync-net.sh` after editing the library file.

## 10. Risk and suggested model tier

Low: three string edits and one new function; implementer-sonnet. The risk is changing the wrong table: `Word(BoostId)` must stay.

## 11. Known traps

- Edit the library file, never its Unity copy by hand.
- `Word(BoostId)` is not edited. No second per-surface table, no caller special case. If a test seems to need one, stop and report.
- Only `Word(SessionState.Ended)` changes among the session words; `ConnectionCopy.cs:49`, `TraderDialogView.cs:587`, `MatchHud.cs:55` stay.
- Each amend commit touches only its own lines; the branch stays linear.

## Notes from the oracle author

(filled when the oracle is committed)

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
