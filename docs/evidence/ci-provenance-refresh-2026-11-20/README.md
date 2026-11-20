# Card provenance refresh, 2026-11-20 (P-CI-GREEN-6)

Same route as `docs/evidence/ci-provenance-refresh-2026-11-17/README.md` (commit `551814bc`): genuine recaptures in the
pinned `node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image (native amd64 here), repo mounted read-only,
output to an external directory, then a recursive leaf diff against the committed corpus.

Environment note: containers here reach the npm registry through a TLS-intercepting proxy, so `npm ci` inside the image
failed until the proxy CA was supplied. A `docker` wrapper outside the repo (first on `PATH`) added only
`-e npm_config_cafile`, `-e NODE_EXTRA_CA_CERTS` and a read-only bind of the CA bundle to `docker run`; the
`capture.sh` scripts, image, lockfile and mounts are unchanged. An empty `node_modules/` directory was created in the
worktree (untracked, ignored) because the read-only repo mount cannot create the tmpfs mountpoint.

## Catalog (`parity/card-catalog/source.json`)

`04c2c8d0...` -> `ab40ad59...`. 409 cases, families and inventories identical; the only differing leaves are the two
protected pins (both files changed by commit `2d01a5da`, which taught the comparator the `tick_ms` leaf, null or 25):

| file | old pin | new pin |
|---|---|---|
| `tools/determinism/compare.py` | `c5dcfa1d...` | `e3fe6a69...` |
| `tools/determinism/test_compare.py` | `1c426f4f...` | `85aa88b0...` |

## Runtime and witness

- Runtime tool `tools/card-runtime-fixtures/capture.mts`: overlay advanced (prior `04c2c8d0...`, effective `ab40ad59...`,
  two accepted leaves, evidence path of this note, lineage extended) -> `8e22485f...`. Runtime recapture: 65 cases, 6236
  steps, 3179 private records, as before; 11 differing leaves, all provenance (harness pin, catalog reference, overlay
  record). `source.json` `7cb8040b...`, `INPUT_SHA256.json` `bdc4a742...`.
- Witness tool `tools/card-runtime-witness-fixtures/capture.mts`: three overlay entries advanced (prior = old effective;
  evidence path of this note) -> `4439b018...`. Witness recapture: 2 cases, 6 steps, extraDamage and sourceRatio as
  before; 22 differing leaves, all provenance. `source.json` `cc71ff22...`, `INPUT_SHA256.json` `038aecd1...`.
- Each corpus was captured twice (once to an external directory for the leaf diff, once into `parity/` by its
  `capture.sh`); the runtime tool's lineage string was corrected between two runtime captures, and the earlier output
  was discarded. The witness tool's first run without `RH_CARD_WITNESS_DIRECTORY` wrote to a throwaway temp directory,
  since removed.
- `FROZEN_PINS.json` untouched.
- `tools/provenance/check-card-chain.mjs`: `EXPECTED`, the lineage string and the accepted-leaf table updated; still
  read-only.

Checks after the refresh: `node tools/provenance/check-card-chain.mjs` exits 0 (`failureCount: 0`); the three
`capture.sh --check` pass (catalog `ab40ad59...`, 409 cases; runtime `7cb8040b...`, 65 cases; witness `cc71ff22...`,
2 cases).
