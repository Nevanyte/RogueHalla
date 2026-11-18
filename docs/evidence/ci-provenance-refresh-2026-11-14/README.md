# Card provenance refresh, 2026-11-14 (P-CI-GREEN-1)

Same route as `docs/evidence/ci-browser-provenance-chain/` (commits `b082dcbf1`, `8d13c297b`): genuine recaptures in the
pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image, repo mounted read-only, output to an external
directory, then a recursive leaf diff against the committed corpus.

## Catalog (`parity/card-catalog/source.json`)

`e2de5226...` -> `9e194b56...`. 409 cases, families and inventories identical; the only differing leaves are the five
protected pins (each file changed by a deliberate packet edit):

| file | old pin | new pin | last commit touching the file |
|---|---|---|---|
| `tools/browser-proof/proof.mjs` | `9d9a5f64...` | `1ddd1410...` | `091d3fda9` P-BROWSER-PROOF-KEYS-1 |
| `tools/pgtest/start.sh` | `b448fe86...` | `0853f919...` | `bb2cae567`, `0726c81f4` P-AGENT-GATE-1 |
| `tools/pgtest/stop.sh` | `87458c41...` | `0971de32...` | `bb2cae567` P-AGENT-GATE-1 |
| `tools/protocol-js/check-first-playable.mjs` | `ff675e15...` | `cfbbbace...` | `fd5bf549b` P-WIRE17-HUD-1 |
| `tools/protocol-js/check-vectors.mjs` | `1173a06f...` | `aebbcc17...` | `0601e6923` P-FROST-WALL-2 |

## Runtime and witness

- Runtime tool `tools/card-runtime-fixtures/capture.mts`: overlay extended (prior `e2de5226...`, effective
  `9e194b56...`, five accepted leaves) -> `e1a7d65a...`. Runtime recapture: 65 cases, 6236 steps, 3179 private records,
  as before; 16 differing leaves, all provenance (harness pin, catalog reference, overlay record). `source.json`
  `e6c71914...`, `INPUT_SHA256.json` `4c67e83e...`.
- Witness tool `tools/card-runtime-witness-fixtures/capture.mts`: three overlay entries advanced (prior = old effective)
  -> `6b7a5910...`. Witness recapture: 2 cases, 6 steps, extraDamage and sourceRatio as before; 22 differing leaves, all
  provenance. `source.json` `783c6a57...`, `INPUT_SHA256.json` `e073f9c7...`.
- `FROZEN_PINS.json` untouched.
- `tools/provenance/check-card-chain.mjs`: `EXPECTED` and the accepted-leaf table updated; still read-only.

Checks after the refresh: `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`);
`tools/card-catalog-fixtures/capture.sh --check`, `tools/card-runtime-fixtures/capture.sh --check` and
`tools/card-runtime-witness-fixtures/capture.sh --check` pass.
