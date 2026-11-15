# P-TUTORIAL-1: Tutorial progress on the profile (server)

- **Serves:** not in the plan: owner approval 2026-11-15 (owner request in session; barebones first-login tutorial, full pipeline). Proposed home: ORDER_OF_OPERATIONS Phase 3, item 16.
- **Implementer tier:** implementer-opus (cross-crate: rh-persist + rh-server, CLAUDE.md rule 2)
- **Reviewer:** reviewer-opus
- **Integration base:** feature/unity-rework (86dd4adc)
- **D-rules touched:** D1 n/a (no ported mechanic) · D2 n/a · D5 flagged: new player-facing design during the freeze, approved by the owner 2026-11-15 · D6 none · D9 none (nothing outcome-bearing; presentation progress only)

## 1. ID and outcome
A profile remembers how far its owner got in the first-login tutorial and whether the tutorial is due. New authenticated
route `POST /api/profile/{name}/tutorial` with body `{"version":u32>=1,"step":u32,"state":"step"|"done"|"skipped"}`
stores `progress.tutorial = {version, step, state}` inside `Progress.rest` (no migration). Every profile DTO (claim,
open, equip, purchase, tutorial) gains `"tutorial": {...}|null` and `"tutorialDue": bool`, where due = `stats.runs == 0`
and (no tutorial, or `version < TUTORIAL_VERSION`, or the current version still in state `step`). `TUTORIAL_VERSION = 1`
lives in one place on the server. Writes are monotonic: an older version is ignored; a higher version replaces; for the
same version `step` keeps the max and never undoes `done`/`skipped`; `done`/`skipped` overwrite each other or a `step`.
No GV, stats or other field changes.

## 2. Dependencies
None beyond the integration base.

## 3. Relevant legacy paths
- `packages/client/src/ui/trainingBar.ts:10`: `TRAINING_HINTS`, the only legacy onboarding (five fading lines). Legacy has no tutorial progress on the profile.
- `packages/client/src/net/profile.ts:1`: claim/ownership REST shape the Rust account API mirrors.

## 4. New paths
- `crates/rh-persist/src/tutorial.rs` (`Tutorial`, `TutorialState`, `parse`, pure `merge`).

## 5. Exclusive write scope
- `crates/rh-persist/src/lib.rs` (`pub mod tutorial`, `TutorialOutcome`, `Store::set_tutorial_by_id` using `locked_authorized` at L1132, next to `equip_by_id` L1065)
- `crates/rh-server/src/api/profile.rs` (`routes()` L27, `ProfileDto`/`dto()` L130, `shop_err` L217, new `tutorial` handler beside `equip` L619 reusing `shop_prelude` L526 and `current_profile`)
- `crates/rh-server/src/api.rs` (`unavailable_routes()` L225, module doc)
- `crates/rh-server/tests/account_api.rs`, `crates/rh-server/tests/account_api_memory.rs` (oracle files; implementer does not edit them)

## 6. Contracts and references
- C-RNG / secrets: the token is never logged or returned (as `equip`). R-ML untouched.
- `crates/rh-persist/AGENTS.md`: no schema migration (data lives in the `progress` JSON).
- Account API conventions: `x-profile-token`, JSON content type, 1 KiB body, `deny_unknown_fields`, shop rate limit and DB slot, 503 `storage_unavailable` without a database.

## 7. Behaviour to preserve
- Every existing profile route's status codes and JSON (only the two new DTO keys are added).
- A stored profile without `tutorial` reads as before; a malformed stored value reads as absent.
- No wire/protocol change: `PROTOCOL_VERSION` and `protocol/fixtures/` untouched.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-TUTORIAL-1` passes with `DATABASE_URL` set (`eval "$(tools/pgtest/start.sh)"`), so the Postgres account suites run.
- `cargo test -p rh-server --all-features --test account_api --test account_api_memory` green.

## Oracle files (read-only for implementers)
- `crates/rh-persist/tests/tutorial.rs` (`merge_is_monotonic`, `parse_tolerates_garbage`; red against the stub `merge`/`parse` in `crates/rh-persist/src/tutorial.rs`, which the implementer replaces)
- `crates/rh-server/tests/account_api.rs`: new `tutorial_progress_is_authenticated_validated_and_monotonic` + `tutorial()` helper (oracle commit); the DTO key list in `claim_open_and_token_rules` gains `tutorial`, `tutorialDue` (oracle-amend commit, for the owner)
- `crates/rh-server/tests/account_api_memory.rs`: the 503 table gains `POST /api/profile/alpha/tutorial` (oracle-amend commit, for the owner)
- Not pinned by an oracle: a `tutorial_due` unit test in `profile.rs` (the gate's lib-red rule needs a separate test file; the integration test pins `tutorialDue` for unplayed/played profiles). The implementer may add a unit test of their own with `tutorial_due(runs, Option<&Tutorial>)` and `TUTORIAL_VERSION`.

## 9. Validation commands
- `cargo test -p rh-persist -p rh-server --all-features` (with DATABASE_URL)
- `cargo clippy -p rh-persist -p rh-server --all-targets --all-features -- -D warnings`

## 10. Risk and suggested model tier
Low risk; a prior implementation exists on tag `tutorial-pre-rework` (commit `c5cef950`) and applies to this base without
conflicts. Opus only because the rules route cross-crate work there.

## 11. Known traps
- Read-merge-write must happen in one transaction under the profile row lock, or two tabs race.
- `is_none_or` needs the pinned toolchain (`rust-toolchain.toml`); check it is available.
- The DTO key-list assertion in `claim_open_and_token_rules` pins the exact key set.

## Notes from previous attempts
