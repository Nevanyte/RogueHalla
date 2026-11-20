# P-CI-GREEN-6: card provenance refresh for the comparator's `tick_ms` leaf (P-TICK-1a)

- **Serves:** CI green on PR #72 (40 Hz tick, ADR 0029; owner approval to push and merge, 2026-11-20). P-TICK-1a writes
  `"tick_ms": null` into every replay config, so CI's "Compare native CI artifacts" refused it; commit `2d01a5da` on
  `claude/project-thread-baoxuk` taught `tools/determinism/compare.py` and `test_compare.py` the leaf (null or 25).
  Both files are pinned in the card catalog's `protectedSha256` map, so the gate step "card provenance chain (pins
  current)" and CI's `provenance` job are now red.
- **Tier:** `implementer-sonnet`. Reviewer: `reviewer` (Sonnet).
- **Route:** exactly P-CI-GREEN-5's (`docs/backlog/packets/P-CI-GREEN-5.md`, commit `551814bc`, evidence
  `docs/evidence/ci-provenance-refresh-2026-11-17/README.md`), which refreshed the same two files: genuine recaptures in
  the pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image, repo mounted read-only, output to an
  external directory, a recursive leaf diff against the committed corpus, then the catalog, runtime overlay, witness
  overlay and `check-card-chain.mjs` tables advanced in the same order.
- **Write scope:** as P-CI-GREEN-5, with the evidence note at `docs/evidence/ci-provenance-refresh-2026-11-20/README.md`.
- **Oracle (already failing):** `node tools/provenance/check-card-chain.mjs` on `claude/project-thread-baoxuk`:
  `failureCount: 2`, `catalog-protected-sha` for `tools/determinism/compare.py` (expected `c5dcfa1d…`, actual
  `e3fe6a69…`) and `tools/determinism/test_compare.py` (expected `1c426f4f…`, actual `85aa88b0…`).

## Must

- As P-CI-GREEN-5: only these two pins differ in the catalog; runtime and witness differ in provenance leaves only;
  `FROZEN_PINS.json` untouched; no hand-edit under `parity/`. Anything else: stop and report.

## Docker here

Linux container, native amd64. The daemon was started by the coordinator (`dockerd`); `docker ps` works. The image may
need pulling (network goes through the agent proxy). If the pull fails, stop and report the error.

## Done

`node tools/provenance/check-card-chain.mjs` exits 0; the three `capture.sh --check` pass;
`scripts/gate.sh --quick --base claude/project-thread-baoxuk` PASS; the evidence README; the AGENTS.md hand-off report.
