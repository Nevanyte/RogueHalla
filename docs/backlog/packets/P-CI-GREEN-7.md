# P-CI-GREEN-7: card provenance refresh for `tools/protocol-js/check-vectors.mjs` (the rework's wire packets)

- **Serves:** CI green before the local integration branch is pushed (coordinator, 2026-11-20).
  - P-WIRE19-A/B, P-SYNC-ORIGIN-1 (protocol 20) and P-AUG-NATIVE-5 (State key 15) each edited
    `tools/protocol-js/check-vectors.mjs`.
  - That file is pinned in the card catalog's `protectedSha256` map, so the gate step "card provenance chain (pins
    current)" and CI's `provenance` job have been red since P-WIRE19-A.
  - The coordinator waived that one red until this single refresh. It runs after the last wire packet of the rework
    (P-AUG-NATIVE-5, integration 74).
- **Tier:** `implementer-sonnet`. Reviewer: `reviewer` (Sonnet).
- **Base:** `feature/unity-rework` after integration 74 lands (the coordinator names the commit in the dispatch).
- **Route:** exactly P-CI-GREEN-5's (`docs/backlog/packets/P-CI-GREEN-5.md`, commit `551814bc`, evidence
  `docs/evidence/ci-provenance-refresh-2026-11-17/README.md`). Origin's P-CI-GREEN-6 (`a16f3c59d`, evidence
  `docs/evidence/ci-provenance-refresh-2026-11-20/README.md`) is the latest worked example.
  1. Genuine recaptures in the pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image, with the repo
     mounted read-only and output to an external directory.
  2. A recursive leaf diff against the committed corpus.
  3. The catalog, runtime overlay, witness overlay and `check-card-chain.mjs` tables, advanced in that order.
- **Write scope:**
  - `parity/card-catalog/source.json`, `parity/card-runtime/{source.json,INPUT_SHA256.json}`,
    `parity/card-runtime-witness/{source.json,INPUT_SHA256.json}`, each only by its documented `capture.sh`.
  - `tools/card-runtime-fixtures/capture.mts` and `tools/card-runtime-witness-fixtures/capture.mts`, overlay entries
    only.
  - `tools/provenance/check-card-chain.mjs`: the `EXPECTED` and accepted-leaf tables only. It stays read-only.
  - `docs/evidence/ci-provenance-refresh-2026-11-20-check-vectors/README.md` (new).
  - This file (the hand-off section).
- **Oracle (already failing):** `node tools/provenance/check-card-chain.mjs` on the base gives `failureCount: 1`:
  `catalog-protected-sha` for `tools/protocol-js/check-vectors.mjs`, expected `aebbcc17…`, actual the file's current
  sha256.

## Must

- After recapture, the only differing catalog leaf is that one pin. Any other differing leaf: stop and report.
- Runtime and witness recaptures keep their cases and outcomes; differing leaves are provenance only. Anything else:
  stop and report.
- `FROZEN_PINS.json` stays untouched. No hand-edit of any file under `parity/`.

## Docker on this Mac

- Docker Desktop's keychain credential helper hangs here. Create a config directory of your own (for example
  `<your scratchpad>/dockercfg/config.json` containing `{"auths":{}}`). Run every docker command and every `capture.sh`
  with these exported:
  - `DOCKER_CONFIG=<that dir>`
  - `DOCKER_HOST=unix://$HOME/.docker/run/docker.sock`
- First check that `docker version` answers within a minute. If the daemon is not running, stop and report; do not try
  to start Docker Desktop.
- The image runs emulated (amd64 on arm64), which is slow: P-CI-GREEN-5 took about four hours. Capture each corpus once,
  keep the output and run long steps in the background.

## Done

- `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`).
- `tools/card-catalog-fixtures/capture.sh --check`, `tools/card-runtime-fixtures/capture.sh --check` and
  `tools/card-runtime-witness-fixtures/capture.sh --check` pass.
- `scripts/gate.sh --quick --allow-protected --regen-fixtures` passes. Use the flags P-CI-GREEN-5's hand-off says the
  path check needs.
- The evidence README is written, and the AGENTS.md hand-off report is in this file.
