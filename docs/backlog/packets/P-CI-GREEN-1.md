# P-CI-GREEN-1: the pull request's CI runs the real suites again

- **Serves:** CI is the final word (AGENTS.md). Pull request Nevanyte/RougeHalla#65 (`feature/unity-rework` -> `main`)
  has three red jobs, all long-standing. Not a sim, wire or Unity change.
- **Implementer tier:** implementer-sonnet. **Reviewer:** reviewer.
- **Owner approval:** delegated to the coordinator 2026-11-14 for this scope only: `.github/`, `scripts/gate.sh`,
  `parity/` pins by their documented command.
- **Write scope:** `parity/card-catalog/source.json`, `parity/card-runtime/{source,INPUT_SHA256}.json`,
  `parity/card-runtime-witness/{source,INPUT_SHA256}.json`, `tools/card-runtime-fixtures/capture.mts`,
  `tools/card-runtime-witness-fixtures/capture.mts`, `tools/provenance/check-card-chain.mjs`, `scripts/gate.sh`,
  `scripts/gate.test.sh`, `.github/workflows/input-latency.yml`, `docs/agent/README.md` (the gate's step table),
  `docs/evidence/ci-provenance-refresh-2026-11-14/`, this file.
- **Oracle:** `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`); `bash scripts/gate.test.sh`;
  `scripts/gate.sh --quick --allow-protected --regen-fixtures`.

## The three failures and the fix for each

1. **`provenance` (rust.yml).** Five files changed after their pins in the catalog `protectedSha256`:
   `tools/browser-proof/proof.mjs`, `tools/protocol-js/check-vectors.mjs`, `tools/protocol-js/check-first-playable.mjs`,
   `tools/pgtest/start.sh`, `tools/pgtest/stop.sh` (each a deliberate packet edit). Because `rust`, `simulation`,
   `csharp` and `card-runtime-source` all `need: provenance`, CI has been skipping them. **Fix:** the refresh route of
   `b082dcbf1` and `8d13c297b` (docs/evidence/ci-browser-provenance-chain): recapture the catalog in the pinned
   `node:22.23.4` linux/amd64 image (`tools/card-catalog-fixtures/capture.sh`); confirm the only differing leaves are
   those five; extend the `acceptedProvenanceOverlay` in the runtime capture tool and the witness tool (prior = the old
   effective hash), recapture the runtime and witness corpora in the same image, confirm every difference is a
   provenance leaf, then update the `EXPECTED` table and the leaf table of the auditor to match. The auditor itself is
   still read-only.
2. **`report-contract` (input-latency.yml).** `profile-hub14.test.cjs` is the frozen protocol-14 proof; it needs a
   protocol-14 codec block in `crates/rh-server/web/probe.html` that no longer exists (protocol 18). The gate already
   excludes it (`scripts/gate.sh`, `UNITY_TOOL_TEST_EXCLUDE`). **Fix:** drop it from the workflow's `node --test` line,
   with a comment saying why.
3. **`perfproof`.** It compares the head to the merge-base with `main`; that base predates
   `parity/maps/seed1.map.json`, so the base build panics. **No code change.** It cannot pass on a PR whose merge-base
   predates that file. The `workflow_dispatch` input that runs it against a recent base is the `baseline` input (a SHA or `origin/<branch>`; see
   `.github/workflows/perfproof.yml`).

## Also in this packet

- **B.** The gate runs the audit (`node tools/provenance/check-card-chain.mjs`) as a cheap step, so pins cannot go
  stale silently: always in full mode; in quick mode when a path under `tools/`, `parity/card-catalog/`,
  `parity/card-runtime/`, `parity/card-runtime-witness/` or `crates/rh-server/web/` changed. A case in
  `scripts/gate.test.sh`; a row in the gate's step table.
- **E.** Run locally what the jobs `rust`, `simulation`, `csharp`, `card-runtime-source` run that the gate does not,
  and list each result. Failures found there are listed, not fixed.

## What stays red and why

`perfproof` (above). Anything section E finds is listed in the hand-off.
