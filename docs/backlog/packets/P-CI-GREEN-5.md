# P-CI-GREEN-5: card provenance refresh for the two comparator files of P-CI-GREEN-3

- **Serves:** CI green before `feature/unity-rework` merges into `main` (owner, 2026-11-16: "Yes, both"). Integration 55
  merged P-CI-GREEN-3, which changed `tools/determinism/compare.py` and `tools/determinism/test_compare.py`; both are
  pinned in the card catalog's `protectedSha256` map, so the gate step "card provenance chain (pins current)" and CI's
  `provenance` job are red on `coord/int-55`.
- **Tier:** `implementer-sonnet`. Reviewer: `reviewer` (Sonnet).
- **Route:** exactly the one used by P-CI-GREEN-1, written up in
  `docs/evidence/ci-provenance-refresh-2026-11-14/README.md` (read it first; commit `baf278747` is the worked example):
  genuine recaptures in the pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image, repo mounted
  read-only, output to an external directory, a recursive leaf diff against the committed corpus, then the catalog,
  runtime overlay, witness overlay and `check-card-chain.mjs` tables advanced in the same order.
- **Write scope:** `parity/card-catalog/source.json`, `parity/card-runtime/{source.json,INPUT_SHA256.json}`,
  `parity/card-runtime-witness/{source.json,INPUT_SHA256.json}` (each only by its documented `capture.sh`),
  `tools/card-runtime-fixtures/capture.mts` and `tools/card-runtime-witness-fixtures/capture.mts` (overlay entries
  only), `tools/provenance/check-card-chain.mjs` (`EXPECTED` and accepted-leaf tables only; it stays read-only),
  `docs/evidence/ci-provenance-refresh-2026-11-17/README.md` (new), this file (hand-off section).
- **Oracle (already failing):** `node tools/provenance/check-card-chain.mjs` on `coord/int-55` @ `a1770f1dc`:
  `failureCount: 2`, `catalog-protected-sha` for `tools/determinism/compare.py` (expected `885f7b82…`, actual
  `c5dcfa1d…`) and `tools/determinism/test_compare.py` (expected `1926d0d6…`, actual `1c426f4f…`).

## Must

- The only differing catalog leaves after recapture are these two pins. Any other differing leaf: stop and report.
- Runtime and witness recaptures keep their cases and outcomes (counts as in the 2026-11-14 note); differing leaves are
  provenance only. Anything else: stop and report.
- `FROZEN_PINS.json` untouched. No hand-edit of any file under `parity/`.

## Docker on this Mac

Docker Desktop's keychain credential helper hangs here. The image is already pulled. Run every docker command (and
every `capture.sh`) with `DOCKER_CONFIG=/private/tmp/claude-501/-Users-nevinmckinnon--claude-spares-spare-BY6i07/57933ca7-3309-4c14-9487-3f0c1a664437/scratchpad/dockercfg`
and `DOCKER_HOST=unix://$HOME/.docker/run/docker.sock` exported. The image runs emulated (amd64 on arm64): slow, so
capture each corpus once and keep the output.

## Done

`node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`); `tools/card-catalog-fixtures/capture.sh
--check`, `tools/card-runtime-fixtures/capture.sh --check`, `tools/card-runtime-witness-fixtures/capture.sh --check`
pass; `scripts/gate.sh --quick --base coord/int-55 --oracle packet/P-CI-GREEN-5` PASS; the evidence README in the
2026-11-14 note's format; the AGENTS.md hand-off report.
