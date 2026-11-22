# P-CI-GREEN-10: card provenance refresh for `tools/determinism/compare.py` and `test_compare.py`

- **Serves:** CI green for PR 80, the augment and elite rework (owner, 2026-11-20: "Please push and merge the new
  augments").
  - P-CI-GREEN-9 (`a4abce7ea`) taught `tools/determinism/compare.py` the replay config leaves `augments` and `elites`,
    and covered them in `tools/determinism/test_compare.py`. That fixed CI's "Compare native CI artifacts".
  - Both files are pinned in the card catalog's `protectedSha256` map, so CI's `provenance` job (run 44951373714) and
    the gate step "card provenance chain (pins current)" are now red.
- **Tier:** `implementer-sonnet`. Reviewer: `reviewer` (Sonnet).
- **Base:** `feature/unity-rework` at the commit that adds this brief. Origin has not touched the card chain since
  P-CI-GREEN-7 (integration 78).
- **Route:** exactly P-CI-GREEN-7's (`docs/backlog/packets/P-CI-GREEN-7.md`; evidence
  `docs/evidence/ci-provenance-refresh-2026-11-20-check-vectors/README.md`, the latest worked example).
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
  - `docs/evidence/ci-provenance-refresh-2026-11-21-determinism/README.md` (new).
  - This file (the hand-off section).
- **Oracle (already failing):** `node tools/provenance/check-card-chain.mjs` on the base gives `failureCount: 2`, both
  `catalog-protected-sha`:
  - `tools/determinism/compare.py`: expected `e3fe6a69…`, actual `a4b3c8f9…`;
  - `tools/determinism/test_compare.py`: expected `85aa88b0…`, actual `4cc8c873…`.

## Must

- After recapture, the only differing catalog leaves are those two pins. Any other differing leaf: stop and report.
- Runtime and witness recaptures keep their cases and outcomes; differing leaves are provenance only. Anything else:
  stop and report.
- `FROZEN_PINS.json` stays untouched. No hand-edit of any file under `parity/`.
- Never edit `tools/determinism/*`; those two files are the reason for this refresh.

## Docker on this Mac

- Docker Desktop is running (restarted on the owner's word, 2026-11-21). Its keychain credential helper hangs here.
  Create a config directory of your own (for example `<your scratchpad>/dockercfg/config.json` containing
  `{"auths":{}}`). Run every docker command and every `capture.sh` with these exported:
  - `DOCKER_CONFIG=<that dir>`
  - `DOCKER_HOST=unix://$HOME/.docker/run/docker.sock`
- First check that `docker version` answers within a minute. If it does not, stop and report; never start, stop or
  restart Docker Desktop, and never touch a container you did not start.
- The image runs emulated (amd64 on arm64). Capture each corpus once, keep the output, and run long steps in the
  background.

## Done

- `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`).
- `tools/card-catalog-fixtures/capture.sh --check`, `tools/card-runtime-fixtures/capture.sh --check` and
  `tools/card-runtime-witness-fixtures/capture.sh --check` pass, each with its exit status recorded.
- `scripts/gate.sh --quick --allow-protected --regen-fixtures --base <this brief's commit>` passes.
- The evidence README is written, and the AGENTS.md hand-off report is in this file.

## Hand-off

```
PACKET:          P-CI-GREEN-10 — serves CI green for PR 80 (owner, 2026-11-20: push and merge the new augments)
BRANCH:          agent/sonnet/P-CI-GREEN-10 @ HANDOFF_SHA
GATE:            PASS (quick) `scripts/gate.sh --quick --allow-protected --regen-fixtures --base 520207538`; check-card-chain failureCount 0; capture.sh --check exit 0 for card-catalog, card-runtime, card-runtime-witness (explicit exit statuses)
CHANGED:         parity/card-catalog/source.json; parity/card-runtime/{source,INPUT_SHA256}.json; parity/card-runtime-witness/{source,INPUT_SHA256}.json (all by capture.sh output); tools/card-runtime-fixtures/capture.mts and tools/card-runtime-witness-fixtures/capture.mts (overlay entries); tools/provenance/check-card-chain.mjs (EXPECTED, lineage, leaf count 1->2, leaf table, evidence path); docs/evidence/ci-provenance-refresh-2026-11-21-determinism/README.md; this file
D-RULES:         n/a (provenance only; no parity capture content changed)
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: nothing beyond the pins being correct for 520207538
RISKS:           any later edit to a pinned file needs another refresh
```

Pins: catalog `1f3de8f3...`, runtime source `453060bb...`, runtime input `296ca6a9...`, witness source `cda93ede...`,
witness input `5d9a7553...`, runtime tool `84238f11...`, witness tool `77188ce0...`. Only differing catalog leaves were
the two determinism pins; runtime 10 and witness 22 differing leaves, all provenance. Corpora captured once each,
outside the repo.
