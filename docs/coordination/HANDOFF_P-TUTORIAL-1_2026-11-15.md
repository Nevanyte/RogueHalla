# Hand-off: P-TUTORIAL-1 (2026-11-15)

PACKET: P-TUTORIAL-1, tutorial progress on the profile (server)
BRANCH: agent/opus/P-TUTORIAL-1 (568e58cc) · ORACLE: packet/P-TUTORIAL-1 (1c16a225)
BASE: feature/unity-rework 86dd4adc (not stacked)
SERVES: not in the plan: owner approval 2026-11-15 (owner request in session). D5 flagged in the packet.

GATE (this cloud container, runs as root, Linux): `scripts/gate.sh --quick --keep-going --base 86dd4adc --oracle packet/P-TUTORIAL-1`
→ `GATE FAIL (1 step): rh-server Postgres suite`. Every other step green (path/oracle checks, rustfmt, provenance,
staging/NPC pins, clippy rh-persist and rh-server all features, tests rh-persist, tests rh-server).
The one failing test is `result_recovery::database_stopped_during_a_match_ticks_continue_and_the_result_lands_after_restart`
(`crates/rh-server/tests/result_recovery.rs:1043`: "pg_ctl: cannot be run as root"). It is environmental: the test stops
its own cluster with pg_ctl, which PostgreSQL refuses for uid 0; this diff does not touch result_recovery, tools/pgtest or
the spool. (It also needed the en_US.UTF-8 locale, which was generated in the container.) Because cargo stops at the
first failing binary, `rewind`, `runtime_admission`, `sigterm`, `snapshot_delta`, `transport`,
`trophy_status_los_transport` did not run with `--features postgres` here (they passed without it). **The coordinator's
merge gate, run as a non-root user, is the evidence for that step.**
Inside the same gate run: `account_api` 20/20 (incl. `tutorial_progress_is_authenticated_validated_and_monotonic`),
`account_api_memory` 1/1, rh-persist `tests/tutorial.rs` 2/2, `tutorial_due` unit test green.
`--oracle-check` on packet/P-TUTORIAL-1: GATE ORACLE-CHECK PASS (red on the stub in oracle files only).

CHANGED: `crates/rh-persist/src/tutorial.rs` (parse, monotonic merge), `crates/rh-persist/src/lib.rs`
(`TutorialOutcome`, `Store::set_tutorial_by_id`: one transaction under `locked_authorized`, write only on change),
`crates/rh-server/src/api/profile.rs` (route `POST /api/profile/{name}/tutorial`, `TutorialBody`, `bad_tutorial`,
`TUTORIAL_VERSION = 1`, `tutorial_due`, DTO `tutorial`/`tutorialDue`), `crates/rh-server/src/api.rs` (503 route list, doc).

WHAT CHANGES FOR A PLAYER / ON THE WIRE: profile JSON gains `tutorial` and `tutorialDue`; a new authenticated account
route. No game protocol change (`PROTOCOL_VERSION` and fixtures untouched). No migration (data in `progress` JSON).
oracle-amend commits (listed for the owner): the DTO key list in `claim_open_and_token_rules` gains the two keys; the 503
table in `account_api_memory.rs` gains the new route.

D-RULES: D1/D2 n/a · D5 owner-approved · D6 untouched · D9 n/a (no outcome-bearing data).

REVIEW (reviewer-opus): LAND. Minor, not notes:
- `version` is not capped at `TUTORIAL_VERSION`; a client can store a large version and stay "not due" (own profile only).
- Unknown `state` → `bad_tutorial`, missing/wrong-typed field → `bad_request` (both 400), as `equip` splits errors.
- `TUTORIAL_VERSION` is crate-visible only; expose it (e.g. `/info`) if a client ever needs it.
- No by-name `set_tutorial` variant (not needed).
- CONTRACTS account-API section not checked for a new-route entry (coordinator/doc follow-up).

PINNED VALUES TO RE-TAKE: none. DEVIATIONS: the guided-tutorial row is proposed in HANDOFF_P-TUTORIAL-3.
