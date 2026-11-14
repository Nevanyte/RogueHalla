# P-BROWSER-PROOF-KEYS-1: the browser proof expects state keys 1..13 for a seated player

- **Serves:** the release finish step "browser proof" (deploy of 2026-11-14).
- **Implementer tier:** implementer-sonnet. **Write scope:** `tools/browser-proof/**` and this brief only.
- **Oracle:** `node tools/browser-proof/proof.mjs` (CONTRIBUTING.md), verdict FAIL before, PASS after.

## What was stale

`proof.mjs` pinned `expectedStateKeys` at `1..12` (ADR0026, key 12 roster, commit `1d22ffc84`, 2026-11-10). Key 13, the
owner's own build summary (P-WIRE17-HUD-1, `StateItem::BuildSummary`, `crates/rh-protocol/src/state.rs:915`,
`MAX_STATE_KEYS = 13` at `:6`), was added after that (stages 1-2, `64533549b`, 2026-11-11). The server pushes it on every
Full state frame (`crates/rh-policy/src/delivery/collections.rs:741-751`); the payload is the viewer's own summary
only (`observation.summary`, C-OBS), `None` when unseated. Both proof clients are seated humans, so key 13 is expected
for them on join and on resume.

## Why the gate missed it

`scripts/gate.sh` does not run `proof.mjs`. Only CI does (`.github/workflows/rust.yml:355`, "Browser proof"); the commit
that added key 13 was not covered by a CI run of that job before the release.

## Change

One constant, `expectedStateKeys`, now `1..13`, with a comment naming `MAX_STATE_KEYS`. The script drives no spectator or
non-owner client, so no absence assertion is added (not building a new harness).

## Acceptance

`proof.mjs` against a server built from this commit prints `"verdict": "PASS"`; `scripts/gate.sh --quick` green.
