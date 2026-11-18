# P-CI-GREEN-2: the next three CI failures on the pull request

- **Serves:** CI is the final word (AGENTS.md); follows P-CI-GREEN-1. Owner approval delegated to the coordinator
  2026-11-14 for `.github/`, `scripts/gate.sh`, pins by their documented route.
- **Write scope:** `tools/staging-full-runtime/{test_preparation.py,README.md}`, `tools/npc-witness-context/`,
  `.github/workflows/determinism.yml`, `scripts/gate.sh`, `scripts/gate.test.sh`, `docs/agent/README.md` (gate table),
  this file.

## 1. `staging-full-runtime`: `test_actual_frozen_native_configuration_is_pinned`

- **What:** `tools/staging-full-runtime/native-pins.json` freezes the sha256 of `crates/rh-server/src/{runtime,main,lib,
  director,room}.rs` for the staging candidate `3253da049` (2026-11-09; `prepare.py:13` `BASE`). The plan is "NOT
  DEPLOYABLE" (README line 3). The test (`test_preparation.py`, `test_actual_frozen_native_configuration_is_pinned`)
  hashed the WORKING TREE. History: `docs/evidence/ci-76d-staging-pin/` and `docs/evidence/staging-preparation-ci76d/`
  record the same failure at `76d` and a reviewed refreeze to `3253da049`.
- **Since when:** 56 commits touched the five files after `3253da049` (`git log 3253da049..HEAD -- <files>`); the
  first one on this branch broke it.
- **Meant to be refreshed?** The ci76d note chose to refreeze and rejected "historical-snapshot validation" because
  CI is a shallow checkout without old objects. That reasoning no longer holds: the plan is a historical candidate and
  the deployed recipe is `hosting/technical-demo`. Refreezing to today's source would be a new staging decision nobody
  made, so not done.
- **Fix:** the test reads the bytes through git at the recorded commit (`git show 3253da049:<path>`), still checks
  that manifest, BASE, plan and README agree, and checks the digests are well formed. In a shallow checkout without
  that commit (CI) the byte comparison is skipped (`skipTest`), so CI only proves the manifest shape; the local gate
  (full clone) proves the bytes. README sentence updated.
- **Result:** both unittest discoveries from `rust.yml:99-100` pass (14 and 54 tests).

## 2. `npc-witness-context`: `ValueError: pin drift: docs/CONTRACTS.md`

- **What:** `context.json` pinned `currentDoc` (`docs/CONTRACTS.md`) and also `docs/REQUIREMENTS.md` inside each
  witness's `pins`. `run.py` mounts a frozen copy of CONTRACTS at 197926e2 over the live path; the live document is
  only recorded in the receipt (`currentNormativeDoc`). The capture scripts record sha pins of both documents
  (`tools/npc-continuity-fixtures/capture.mts:89`), so the oracle bytes depend on the content the container sees.
- **Found beyond the report:** `docs/REQUIREMENTS.md` (changed 2026-11-12, `c9c87aac1`) drifts the same way and would
  have failed the next.
- **Fix:** `run.py` no longer pins `currentDoc` (the live file is hashed into the receipt with `pinned: false`);
  `REQUIREMENTS.197926e2.md` (identical to the pin `45f4a590`) is added as `historicalRequirements` and mounted over
  `docs/REQUIREMENTS.md`; the live REQUIREMENTS pin is removed. `tests.py` gains a test that every remaining pin
  matches the tree and neither status doc is pinned.
- **Result:** `tests.py` 10 tests pass; `run.py` passes every pin check locally. The container run itself is NOT
  established: the pinned `node@sha256:aaa4b5ba...` image is listed locally but `docker image inspect` by digest says
  "No such image", so `run.py` stops at its image check.

## 3. Workflow "Native determinism": `cancelled`

- **What:** both runs (43649783114, 43655059685) ran the job `produce` for exactly 30 minutes (22:45:56 -> 23:16:11;
  23:48:32 -> 00:18:46) and were cancelled by `timeout-minutes: 30` inside "Focused simulation, policy, and
  headless tests" (the second log shows the 8-Brain test `eight_brain_seats_play_the_source_arena_deterministically`
  started only at 00:16:21, 27 minutes after the step began: the cold release build). No concurrency cancel (same
  head sha per run), no missing runner. The last green run (42464417428, 2026-11-08, before the `brain_bots` test
  existed) took 17 minutes in that step. Because the job never succeeds, `Swatinem/rust-cache` never saves, so every run
  is cold.
- **Fix:** `produce` `timeout-minutes` 30 -> 75; `verify-foreign-replay` 20 -> 45 (a cache miss there builds `rh-headless`).
- **Established:** the cause (timeout hit in the log) and the cold build time to the test run (about 27 minutes,
  measured from the log). **Not established:** that 75 suffices: the remainder after the build (the brain test about 54 s
  wall on 10 local cores, about 6 CPU minutes, so a few minutes on 2 to 4 vCPUs; the rest of the suite, parity report and
  sweep) is not measured on a runner; 75 is a margin over 27 plus an estimate, not a measurement. A cached run is
  expected to fit easily (the earlier green runs finished in 20 to 30 minutes in total), but only once one run succeeds
  and saves the cache. aarch64 timing has no log.

## Gate

`offline_pin_checks` in `scripts/gate.sh` (both modes, always: about 0.5 s) runs the two unittest discoveries and the NPC
tests; a case in `scripts/gate.test.sh` (42 cases); rows in the gate table of `docs/agent/README.md`.
