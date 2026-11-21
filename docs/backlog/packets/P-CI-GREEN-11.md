# P-CI-GREEN-11: card provenance refresh on the merged tree (P-SYNC-ORIGIN-3)

- **Serves:** CI green for PR 80, the augment and elite rework (owner, 2026-11-20: "Please push and merge the new
  augments").
  - P-SYNC-ORIGIN-3 merged origin's Top 25 quests (PR 81, `ef8d5e9ce`) into the rework line as protocol 24. As its
    brief's decision 6 ordered, it took origin's provenance files whole (catalog, runtime and witness JSONs, the two
    `capture.mts` overlays, `check-card-chain.mjs`) and merged the pinned files by content.
  - Three pinned files now differ from origin's pins, because each carries both lines' changes.
- **Tier:** `implementer-sonnet`. Reviewer: `reviewer` (Sonnet).
- **Base:** `packet/P-CI-GREEN-11` (P-SYNC-ORIGIN-3's merged tree `4fa0e59de` plus this brief).
- **Route:** exactly P-CI-GREEN-10's (`docs/backlog/packets/P-CI-GREEN-10.md`; evidence
  `docs/evidence/ci-provenance-refresh-2026-11-21-determinism/README.md`, the latest worked example).
  1. Genuine recaptures in the pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image, with the repo
     mounted read-only and output to an external directory.
  2. A recursive leaf diff against the committed corpus.
  3. The catalog, runtime overlay, witness overlay and `check-card-chain.mjs` tables, advanced in that order.
  - The lineage now starts from origin's chain (its own refresh for PR 81), not from P-CI-GREEN-10's. Read the
    `check-card-chain.mjs` on the base before editing its tables.
- **Write scope:**
  - `parity/card-catalog/source.json`, `parity/card-runtime/{source.json,INPUT_SHA256.json}`,
    `parity/card-runtime-witness/{source.json,INPUT_SHA256.json}`, each only by its documented `capture.sh`.
  - `tools/card-runtime-fixtures/capture.mts` and `tools/card-runtime-witness-fixtures/capture.mts`, overlay entries
    only.
  - `tools/provenance/check-card-chain.mjs`: the `EXPECTED` and accepted-leaf tables only.
  - `docs/evidence/ci-provenance-refresh-2026-11-21-merge-quests/README.md` (new).
  - This file (the hand-off section).
- **Oracle (already failing):** `node tools/provenance/check-card-chain.mjs` on the base gives `failureCount: 3`, all
  `catalog-protected-sha`:
  - `tools/determinism/compare.py`: expected `1434f2e8…`, actual `509bed93…`;
  - `tools/determinism/test_compare.py`: expected `894e3804…`, actual `7d5f96ba…`;
  - `tools/protocol-js/check-vectors.mjs`: expected `e0694291…`, actual `0b1c5b05…`.

## Must

- After recapture, the only differing catalog leaves are those three pins. Any other differing leaf: stop and report.
- Runtime and witness recaptures keep their cases and outcomes; differing leaves are provenance only. Anything else:
  stop and report.
- `FROZEN_PINS.json` stays untouched. No hand-edit of any file under `parity/`.
- Never edit `tools/determinism/*` or `tools/protocol-js/*`; those files are the reason for this refresh.

## Docker on this Mac

- Docker Desktop is running. Its keychain credential helper hangs here. Create a config directory of your own (for
  example `<your scratchpad>/dockercfg/config.json` containing `{"auths":{}}`). Run every docker command and every
  `capture.sh` with these exported:
  - `DOCKER_CONFIG=<that dir>`
  - `DOCKER_HOST=unix://$HOME/.docker/run/docker.sock`
- First check that `docker version` answers within a minute. If it does not, stop and report; never start, stop or
  restart Docker Desktop, and never touch a container you did not start.
- `tools/card-catalog-fixtures/capture.sh` is not executable: run it as `bash tools/card-catalog-fixtures/capture.sh`.
- The image runs emulated (amd64 on arm64). Capture each corpus once, keep the output, and run long steps in the
  background.

## Done

- `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`).
- `bash tools/card-catalog-fixtures/capture.sh --check`, `tools/card-runtime-fixtures/capture.sh --check` and
  `tools/card-runtime-witness-fixtures/capture.sh --check` pass, each with its exit status recorded.
- `scripts/gate.sh --quick --allow-protected --regen-fixtures --base e180967ac` passes.
- The evidence README is written, and the AGENTS.md hand-off report is in this file.
