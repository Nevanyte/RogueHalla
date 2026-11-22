# Card provenance refresh, 2026-11-21, merge of origin's Top 25 quests (P-CI-GREEN-11)

Same route as `docs/evidence/ci-provenance-refresh-2026-11-21-determinism/README.md`: genuine recaptures in the pinned
`node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image (emulated on this arm64 Mac), repo mounted read-only,
output to an external directory, then a recursive leaf diff against the committed corpus. Each corpus was captured once.
An empty untracked `node_modules/` was created in the worktree for the tmpfs mountpoint.

## Why

P-SYNC-ORIGIN-3 merged origin's Top 25 quests (PR 81) into the rework line as protocol 24 and took origin's provenance
files whole. Three pinned files then carried both lines' changes and no longer matched origin's pins:
`tools/determinism/compare.py`, `tools/determinism/test_compare.py` and `tools/protocol-js/check-vectors.mjs`. The chain's
lineage starts from origin's refresh (catalog `8e444cf1...`), not from P-CI-GREEN-10's.

## Catalog (`parity/card-catalog/source.json`)

`8e444cf1...` -> `f2a83fe5...`. 409 cases, same families; the only differing leaves are the three protected pins:

| file | old pin | new pin |
|---|---|---|
| `tools/determinism/compare.py` | `1434f2e8...` | `509bed93...` |
| `tools/determinism/test_compare.py` | `894e3804...` | `7d5f96ba...` |
| `tools/protocol-js/check-vectors.mjs` | `e0694291...` | `0b1c5b05...` |

## Runtime and witness

- Runtime tool `tools/card-runtime-fixtures/capture.mts`: overlay advanced (prior `8e444cf1...`, effective `f2a83fe5...`,
  three accepted leaves, evidence path of this note, lineage extended) -> `120f19dc...`. Recapture: 65 cases, 6236 steps,
  3179 private records, as before; 14 differing leaves, all provenance. `source.json` `0be9e0b6...`,
  `INPUT_SHA256.json` `571228ac...`.
- Witness tool `tools/card-runtime-witness-fixtures/capture.mts`: three overlay entries advanced (prior = old effective;
  evidence path of this note) -> `3c65eaf6...`. Recapture: 2 cases, 6 steps, extraDamage 2.891 and sourceRatio 1.08 as
  before; 22 differing leaves, all provenance. `source.json` `7a9fd006...`, `INPUT_SHA256.json` `8401446b...`.
- `FROZEN_PINS.json` untouched.
- `tools/provenance/check-card-chain.mjs`: `EXPECTED`, the lineage string, the accepted-leaf table and the evidence path
  updated; still read-only.

Checks: `node tools/provenance/check-card-chain.mjs` gives `failureCount: 0`; the three `capture.sh --check` results are
in the packet hand-off.
