# P-CI-GREEN-3: the last layer of pull request #65's CI

- **Serves:** CI is the final word (AGENTS.md); follows P-CI-GREEN-1 and -2. Approval delegated to the coordinator
  2026-11-14 for `.github/`, `scripts/gate.sh`.
- **Write scope:** `tools/determinism/{compare.py,test_compare.py}`, `scripts/gate.sh`, `scripts/gate.test.sh`,
  `docs/agent/README.md` (gate table), `docs/evidence/ci-witness-refresh-2026-11-14/`, the derived witness files the
  `rust` job's `capture.sh --check` steps name (only if derived, see item B), this file.

## A. "Compare native CI artifacts": `replay config keys outside the recognized optional leaves`

`compare.py` `REPLAY_SIM_KEYS` lacked the `SimConfig` leaves added since it was written; `SimConfig` serializes every
field (no `skip_serializing`). Missing: `walk_hints`, `hitboxes`, `mob_spacing`, `regen_in_combat`, `camp_buffs`,
`frost_wish`. Unknown keys fail on purpose (a new config field must be looked at). Across targets the whole replay is
compared by `first_difference` (keys, order, values), so every new leaf is compared for equality. The new leaves are
also shape-checked (null or the one named string; `hitboxes` one of two; `consumables`/`walk_hints` bool).

Found beyond the report: `test_compare.py` was red at base (44 errors, 1 failure): the fixture replay lacked
`secrets.reward_salt`, and the validator required the "optional" leaves it said a replay may omit. Fixed both (fixture;
validator checks optional leaves when present). The tests now ride in the gate's offline step.
