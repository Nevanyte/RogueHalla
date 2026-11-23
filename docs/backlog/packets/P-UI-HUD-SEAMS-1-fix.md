# P-UI-HUD-SEAMS-1-fix: the seams test stops pinning the members that the next packets replace

- **Serves:** note 2 of the coordinator's merge review of UI rework wave 1 (`reviewer-opus`, LAND WITH NOTES,
  2026-11-23); same owner direction as P-UI-HUD-SEAMS-1 (2026-11-22, UI rework lane; not in ORDER_OF_OPERATIONS).
- **Implementer tier:** none: the whole change is one amended expectation of an oracle file, made by the lane lead.
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (the gate compares against it with `--base`)
- **D-rules touched:** D1 n/a (a structure test of client source) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
`client-unity/UnityProject/Tools/hud-seams.test.cjs` is the oracle of P-UI-HUD-SEAMS-1 (merged). For each of the
five new `MatchHud` partial files it ran three tests. The second, "`<file>` declares each member that moved into
it", requires every moved member to keep existing under its old name for ever: `ToastBottom`, `BannerTop`,
`CreateTopAndToastPills`, `BarBottom`, `SlotWidth`, `SwapWidth`, `WishSize`, `CreateWeaponBar`, `_pulses`,
`PulseSeconds`, `PulsePop`, `TickPulses` and the rest of the `MOVED` table.

That turned a one-time proof (the split moved the code) into a permanent pin of legacy names. The next packets
replace exactly those members: P-UI-TOAST-LANES-1 had to amend two rows away, and P-UI-ABILITY-ROW-1's brief told
its implementer to keep the old bar constants and the old pulse code declared beside `HudRegions` and
`HudPulses`, which is two sources for one number.

Outcome: the per-member presence test is deleted. What stays, unchanged:
- each of the five files exists, is a `public sealed partial class MatchHud` with a `/// <summary>`, at most 800
  lines;
- the four old files (`MatchHud.Legacy.cs`, `MatchHud.cs`, `MatchHud.Features.cs`, `MatchHud.PlayerFlows.cs`) do
  not declare anything of the `MOVED` table (so the old files cannot grow back);
- `PlayerFlows` calls the two extracted notice methods;
- no method name is declared in two `MatchHud*.cs` files;
- the size caps of the old files.

After this, replacing or removing a member that moved costs no test change; only regrowth of the old files fails.

## 2. Dependencies
P-UI-HUD-SEAMS-1 (on the base).

## 3. Relevant legacy paths
None.

## 4. New paths
None.

## 5. Exclusive write scope
- `client-unity/UnityProject/Tools/hud-seams.test.cjs`: delete the test "declares each member that moved into it"
  and correct the header comment. Nothing else.
- `docs/backlog/packets/P-UI-HUD-SEAMS-1-fix.md`

## 6. Contracts and references
- The coordinator agreed to this amendment of a merged packet's oracle in its message to the lane of 2026-11-23
  ("delete the per-member presence test ... Keep: the five files exist; the old files no longer declare X; no
  method declared in two files; the size caps").

## 7. Behaviour to preserve
No product code changes. The remaining 13 tests of the file pass on the base as they did.

## 8. Acceptance checks
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`: 13 tests, all green (18 before: five removed).
- `scripts/gate.sh --quick --oracle packet/P-UI-HUD-SEAMS-1-fix --base lane/ui-rework/base-2` prints `GATE PASS`.

## Amended expectation (for the owner's list of oracle amendments)
- `hud-seams.test.cjs`, the five tests "`<file>` declares each member that moved into it" (one per new partial
  file): removed. Old: every row of `MOVED[file]` must match in that file. New: no such requirement.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `docs/backlog/packets/P-UI-HUD-SEAMS-1-fix.md`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`

## 10. Risk and suggested model tier
A five-line deletion in a test. The risk is the opposite of a regression: a later packet could now delete a moved
member by mistake and no structure test would say so; the compiler and the EditMode suite still would.

## 11. Known traps
- P-UI-TOAST-LANES-1 amends other lines of the same file in the same wave (two rows of the `MOVED` table and one
  pattern in the PlayerFlows test): the two changes merge by taking both.
