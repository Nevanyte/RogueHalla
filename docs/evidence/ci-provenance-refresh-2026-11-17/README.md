# Card provenance refresh, 2026-11-17 (P-CI-GREEN-5)

Same route as `docs/evidence/ci-provenance-refresh-2026-11-14/README.md` (commit `baf278747`): genuine recaptures in the
pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image, repo mounted read-only, output to an external
directory, then a recursive leaf diff against the committed corpus.

## Catalog (`parity/card-catalog/source.json`)

`9e194b56...` -> `04c2c8d0...`. 409 cases, families and inventories identical; the only differing leaves are the two
protected pins (both files changed by P-CI-GREEN-3):

| file | old pin | new pin | last commit touching the file |
|---|---|---|---|
| `tools/determinism/compare.py` | `885f7b82...` | `c5dcfa1d...` | P-CI-GREEN-3 (`e8f0d5801` merge) |
| `tools/determinism/test_compare.py` | `1926d0d6...` | `1c426f4f...` | P-CI-GREEN-3 (`e8f0d5801` merge) |

## Runtime and witness

- Runtime tool `tools/card-runtime-fixtures/capture.mts`: overlay advanced (prior `9e194b56...`, effective
  `04c2c8d0...`, two accepted leaves, evidence path of this note) -> `03411d94...`. Runtime recapture: 65 cases, 6236
  steps, 3179 private records, as before; 6 differing leaves, all provenance (overlay record: acceptedChanges,
  effectiveSha256, preservedPriorLineage, priorAcceptedEffectiveSha256; harness pin; catalog reference).
  `source.json` `2b425b02...`, `INPUT_SHA256.json` `6250fb92...`.
- Witness tool `tools/card-runtime-witness-fixtures/capture.mts`: three overlay entries advanced (prior = old effective;
  evidence path of this note) -> `5f1547d4...`. Witness recapture: 2 cases, 6 steps, extraDamage and sourceRatio as
  before; 22 differing leaves, all provenance. `source.json` `9ce22048...`, `INPUT_SHA256.json` `d8855207...`.
- The first two witness attempts died in `npm ci` inside the container (`ECONNRESET`) before any capture ran; nothing was
  written. The third attempt succeeded; no other corpus was captured twice.
- `FROZEN_PINS.json` untouched.
- `tools/provenance/check-card-chain.mjs`: `EXPECTED`, the lineage string, the leaf count and the accepted-leaf table
  updated; still read-only.

Checks after the refresh: `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`);
`tools/card-catalog-fixtures/capture.sh --check`, `tools/card-runtime-fixtures/capture.sh --check` and
`tools/card-runtime-witness-fixtures/capture.sh --check` pass (catalog `04c2c8d0...`, 409 cases; runtime `2b425b02...`, 65 cases; witness `9ce22048...`, 2 cases).
