# P-UI-HUD-SEAMS-1: MatchHud's top-centre, action-bar, pulse, notice and visibility code each get their own file

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane: "in-depth front end gameplay rework ... UI optimization sweep"). An enabling refactor: the packets after it (toast lanes, the ability row, the Tab-map sheet) each edit one concern, and two of them run at once only if those concerns are in separate files.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 n/a · D2 n/a · D5/D6/D9 none. No behaviour change of any kind.

## 1. ID and outcome
`MatchHud` is one `public sealed partial class` across 16 files under `client-unity/UnityProject/Assets/Scripts/UI/`. Four of them mix concerns: `MatchHud.Legacy.cs` (647 lines), `MatchHud.cs` (647), `MatchHud.Features.cs` (482), `MatchHud.PlayerFlows.cs` (172). This packet MOVES code, unchanged, into five new partial files. Same class, same member names, same signatures, same bodies, same order of calls: every existing test (many reach private members by reflection) stays green without an edit. The player sees nothing different.

## 2. Dependencies
none (base `feature/unity-rework` already holds P-UI-INPUT-CUES-1: `Session.CueSent` subscription and the press pulses).

## 3. Relevant legacy paths
None: this is a client-side code move; no legacy TypeScript is involved.

Where the code sits at the base (verified by reading; line numbers of `packet/P-UI-HUD-SEAMS-1`, all under `client-unity/UnityProject/Assets/Scripts/UI/`):

| New file | What moves into it |
|---|---|
| `MatchHud.TopCentre.cs` | from `MatchHud.Legacy.cs`: `CreateTopAndToastPills` (:88), `SyncTopCentre` (:109), `PlacePhasePill` (:612, with the doc comment above it at :607-611), and the constant line `internal const float ToastBottom = 124f, BannerTop = 12f;` (:22, one declaration, both names move); from `MatchHud.Features.cs`: `ApplyPhaseStrip` (:203) |
| `MatchHud.ActionBar.cs` | from `MatchHud.Legacy.cs`: the constant line `internal const float BarBottom ..., ConsumablesHeight = 28f;` (:19, one declaration), the section comment "weapon bar (section 4)" (:322) with `RowSlot` (:324) and `struct Rect2` (:336) and `WeaponBarWidth` (:338) (only the bar uses them), `ApplyWishRow` (:300), `CreateWeaponBar` (:340), `CreateWishTile` (:390), `CreateConsumables` (:423), `CreatePickupHint` (:446), `RefreshWishTile` (:528), `TickWishTileMotion` (:570), `RefreshCooldownSweep` (:582) |
| `MatchHud.Pulses.cs` | from `MatchHud.cs`: the comment, constants `PulseSeconds`, `PulsePop` and field `_pulses` (:173-176), `InputCue` (:178), `Pulse` (:197), `TickPulses` (:205), `ClearPulses` (:226) |
| `MatchHud.Notices.cs` | from `MatchHud.cs`: `RotateNoticeEvents` (:232) and the field `Action<ServerMsg.Events> _boundNoticeEvents;` (:19) (used only by `RotateNoticeEvents` and by `Unbind` at :256-257, same class: move it, `Unbind` keeps working); from `MatchHud.PlayerFlows.cs`: the field `OrderedNoticeFeed _orderedNotices;` (:14) and, as two new private methods called from the old places, `CreateNoticeFeed()` (the two lines at :37-38 `_orderedNotices = gameObject.AddComponent<OrderedNoticeFeed>(); _orderedNotices.Initialize(_root, ClosePlayerFlows);`, called from `InitializePlayerFlows` at the same position) and `TickNotices()` (the line at :159 `_orderedNotices?.SetRecentVisible(_session != null && WorldHudVisible);`, called from `TickPlayerFlows` at the same position). The `ServerNotices` property (:26), `PlayerFlowVisible`, `PlayerFlowFocus`, `ShowPlayerFlow`, `ClosePlayerFlows` and every other use of `_orderedNotices` stay where they are |
| `MatchHud.Visibility.cs` | from `MatchHud.Legacy.cs`: `RefreshHudElementVisibility` (:593, with its doc comment), `SetActiveIfChanged` (:601); from `MatchHud.Features.cs`: the fields `_worldHudVisible`, `_worldHudFocus` (:37-38; only these methods and `SyncTopCentre` use them, no initialiser depends on another field), `WorldHudVisible` (:39), `RefreshWorldHudVisibility` (:393), `RememberWorldHudFocus` (:412), `RefreshHeaderActionsVisibility` (:227) |

## 4. New paths
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.TopCentre.cs`, `MatchHud.ActionBar.cs`, `MatchHud.Pulses.cs`, `MatchHud.Notices.cs`, `MatchHud.Visibility.cs`, each with its `.meta` (Unity writes it on the first run; commit it).
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` (the oracle).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Legacy.cs`, `MatchHud.cs`, `MatchHud.Features.cs`, `MatchHud.PlayerFlows.cs`
- the five new files above with their `.meta`
- nothing else (the oracle `hud-seams.test.cjs` and this packet are read-only for the implementer). No library change, so no `sync-net.sh`.

## 6. Contracts and references
- Presentation only; no wire, protocol, crate, `packages/` change. Readability gate: a new non-test C# file has a `/// <summary>` before its first type and at most 800 lines.
- Lane rule: keep the `Session.CueSent` subscription and the press pulses; no handler may stay on an old session.

## 7. Behaviour to preserve
- Everything. Bodies are moved verbatim (no reformat, no rename, no reorder). Comments and doc comments move with their member; no comment about the move is added.
- Rules for the move:
  - Each new file: the same `using` lines the moved code needs (and no unused extras that would warn is a plus, not required), namespace `Roguehalla.Client`, `public sealed partial class MatchHud` (the modifiers of the existing files), a `/// <summary>` line before the type saying in one sentence what the file holds, at most 800 lines.
  - A member that only the moved methods use moves with them; a member used elsewhere stays.
  - The two extracted methods `CreateNoticeFeed` and `TickNotices` are the only new members. Each is the moved lines, verbatim, called from exactly the place the lines were.
  - After the move the four old files no longer declare the moved members.
- Reflection in existing tests: member names must not change (see section 11 for the list).

## 8. Acceptance checks
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` passes (through `bin/in.sh <worktree>`).
- `scripts/gate.sh --quick --oracle packet/P-UI-HUD-SEAMS-1` prints `GATE PASS` (it runs the whole EditMode suite, about 2000 tests, no test edited).
- `wc -l` on each new file is at most 800; `MatchHud.Legacy.cs` under 420 lines, `MatchHud.cs` under 600.

## Existing tests that pin this code (none is changed; all must stay green with no edit)
Read at the base. They reach moved members by reflection (names must not change) or exercise the behaviour:
- `HudLegacyLayoutEditModeTests`, `BuildHudCellsEditModeTests`, `HudTextScaleEditModeTests`, `HudVisualFixEditModeTests`, `HudVisualFix2EditModeTests`, `HudVisualFix4EditModeTests`, `ChampionRowNameFitEditModeTests`, `HudPhasePillNarrowEditModeTests`, `BannerPillAfterResultFix4EditModeTests`, `MatchHudStatusNotesEditModeTests`, `HudVisualFix3EditModeTests`, `ChampionPanelNumbersEditModeTests`, `ChampionPanelNoGarbageEditModeTests`, `MatchHudStatusTextsEditModeTests` call `CreateTopAndToastPills` by reflection (after `EnsureStage`) and assert HUD rects, texts and layout at several window sizes.
- `BannerPillAfterResultFix4EditModeTests` (:55) and `HudVisualFix4EditModeTests` (:187) call `SyncTopCentre` by reflection and assert the banner/toast pill state.
- `ChampionPanelNumbersEditModeTests` (:134, :146, :150) and `ChampionPanelNoGarbageEditModeTests` (:86, a delegate made from the method) call `ApplyWishRow` by reflection: row contents and zero allocation per frame.
- `ChampionPanelNumbersEditModeTests` (:163, :174), `HudTextScaleEditModeTests` (:129) and `TrainingHudRig` (:212) read the private field `_weaponBar` (declared in `MatchHud.Legacy.cs`, stays there).
- `UiInputRoutingEditModeTests` (:490-498) and `MatchClientAccountBindingEditModeTests` (:205) call `RefreshWorldHudVisibility` by reflection: the world HUD hides under modals and shows again.
- `PlayerFlowsEditModeTests` reads `ServerNotices` (notice feed entries, clears on epoch change, an old handler cannot repopulate a cleared epoch): this exercises `RotateNoticeEvents`, `CreateNoticeFeed` and `TickNotices` through the real session.
- `TrainingHudVisibilityEditModeTests`, `TrainingHudRig` consumers: the world HUD, plate, champion panel and hotbar visibility under the training console (`WorldHudVisible`, `RefreshHudElementVisibility`).
- Tests also name `EnsureStage`, `CreateLegacyChrome`, `CreateCombatHud`, `InitializeLayoutReview`, `_stage`, `_chromeStage`, `_notice`, `_noticePill` by reflection: these stay where they are, unrenamed.
- No existing test names `_pulses`, `InputCue`, `TickPulses`, `Pulse` or the bar constants (`BarBottom`, `SlotWidth` ...) directly; several source comments in tests mention `MatchHud.Legacy.cs` line numbers (comments only; not asserted).
- `scripts/gate.sh` runs every `client-unity/UnityProject/Tools/*.test.cjs` in "Unity tool tests (node --test)" (gate.sh:100-106), so the new oracle runs in the gate.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `docs/backlog/packets/P-UI-HUD-SEAMS-1.md` (the packet itself)

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- compile and a quick check: `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> HudLegacyLayoutEditModeTests,PlayerFlowsEditModeTests,UiInputRoutingEditModeTests`
- once at the end: `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-HUD-SEAMS-1`

## 10. Risk and suggested model tier
Low behaviour risk, moderate mechanical risk (a member left behind or declared twice is a compile error; a field initialiser order change would be silent). implementer-sonnet; the reviewer diffs the moved bodies against the base (a verbatim move shows as the same lines removed and added).

## 11. Known traps
- Private members reached by reflection: `_stage`, `_chromeStage`, `_weaponBar`, `_notice`, `_noticePill`, `CreateTopAndToastPills`, `CreateLegacyChrome`, `CreateCombatHud`, `EnsureStage`, `InitializeLayoutReview` and the moved methods: names must not change. A partial class keeps them reachable from any file.
- Constants used by both a moved and a remaining method stay accessible (same class): move them with the concern that owns them, as the table says. `QuestRewards.cs:33` uses `BarBottom`, `SlotHeight`, `ConsumablesHeight`, `BarGap`; `ApplyLegacyViewport` (stays in `MatchHud.Legacy.cs`) calls the moved `PlacePhasePill`: fine in one class.
- `#if` blocks and regions: none exist in the `MatchHud*.cs` files at the base; keep it so.
- Field initialisers run in textual order per file, file order undefined: `_worldHudVisible = true` and `_pulses = new List` depend on nothing else.
- `_boundNoticeEvents` is also read at `Unbind` (`MatchHud.cs:256-257`): fine, same class.
- Do the move with a script or whole-method cuts, then compile (`unity-class.sh <worktree> HudLegacyLayoutEditModeTests`) before the gate. Restore the two StormInk prop assets after a Unity run. Add files to commits by name.
- Oracle check 4 (no method declared in two files) looks at declarations at 8-space member indentation: do not leave a copy of a moved method behind.

## Notes from the oracle author
- No stub commit: the oracle reads source text, so it needs no signature to compile.
- The decls in checks 2 and 3 match the real declarations (regexes in the test file). `RowSlot`, `Rect2`, `WeaponBarWidth` and the fields `_worldHudVisible`, `_worldHudFocus`, `_boundNoticeEvents`, `_orderedNotices` are added to the spec's table because only the moved code uses them; the oracle pins them too.
- Red/green record: see "Oracle result" below.
- Each member regex was checked against the base declarations by reading the four old files (the line numbers in section 3 were re-verified at the base).
- The oracle strips `//` comments before matching, so prose and doc comments never satisfy or break a check. The "two files" check (check 4) expects at least 21 `MatchHud*.cs` files (16 at the base plus the five new ones).
- Ordering is pinned for the extracted notice methods: `CreateNoticeFeed();` sits after `_traderDialog.Initialize` and before `AddPlayerFlowTabs(_questJournal.Root)`; `TickNotices();` replaces the `SetRecentVisible` line inside `TickPlayerFlows`.

## Oracle result
`node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` on the base plus this commit: 18 tests, 0 pass, 18 fail. Each fails on an assertion: for the five files, "`MatchHud.<X>.cs` must exist under Assets/Scripts/UI" (file-exists assertion); the PlayerFlows, duplicate-name and shrink tests fail on the same existence assertion first, and would then fail on `CreateNoticeFeed()`/`TickNotices()` not being called, on duplicates left behind, and on `MatchHud.Legacy.cs` (647 lines, limit 420) / `MatchHud.cs` (647, limit 600). No stub is needed: the oracle reads text only. Existing Unity classes are untouched by this commit (only a new `.cjs` and this packet change).

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
