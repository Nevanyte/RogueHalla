# Card provenance refresh, 2026-11-21, determinism comparator pins (P-CI-GREEN-10)

Same route as `docs/evidence/ci-provenance-refresh-2026-11-20-check-vectors/README.md`: genuine recaptures in the pinned
`node:22.23.4-bookworm-slim@sha256:aaa4b5ba...` linux/amd64 image (emulated on this arm64 Mac), repo mounted read-only,
output to an external directory, then a recursive leaf diff against the committed corpus. Base: `feature/unity-rework`
@ `520207538`. An empty untracked `node_modules/` was created in the worktree for the tmpfs mountpoint; each corpus
was captured once.

## Why

`tools/determinism/compare.py` and `tools/determinism/test_compare.py` are pinned in the catalog's `protectedSha256`
map. P-CI-GREEN-9 (`a4abce7ea`) edited both on purpose (the replay config leaves `augments` and `elites`), so the
provenance chain step was red until this refresh.

## Catalog (`parity/card-catalog/source.json`)

`08eedc97...` -> `1f3de8f3...`. 409 cases, families as before; the only differing leaves are the two protected pins:

| file | old pin | new pin |
|---|---|---|
| `tools/determinism/compare.py` | `e3fe6a69...` | `a4b3c8f9...` |
| `tools/determinism/test_compare.py` | `85aa88b0...` | `4cc8c873...` |

## Runtime and witness

- Runtime tool `tools/card-runtime-fixtures/capture.mts`: overlay advanced (prior `08eedc97...`, effective `1f3de8f3...`,
  two accepted leaves, evidence path of this note, lineage extended) -> `84238f11...`. Recapture: 65 cases, 6236 steps,
  3179 private records, as before; 10 differing leaves, all provenance. `source.json` `453060bb...`,
  `INPUT_SHA256.json` `296ca6a9...`.
- Witness tool `tools/card-runtime-witness-fixtures/capture.mts`: three overlay entries advanced (prior = old effective;
  evidence path of this note) -> `77188ce0...`. Recapture: 2 cases, 6 steps, extraDamage 2.891 and sourceRatio 1.08 as
  before; 22 differing leaves, all provenance. `source.json` `cda93ede...`, `INPUT_SHA256.json` `5d9a7553...`.
- `FROZEN_PINS.json` untouched.
- `tools/provenance/check-card-chain.mjs`: `EXPECTED`, the lineage string, the leaf count (1 -> 2) and the accepted-leaf
  table updated; still read-only.

Checks: `node tools/provenance/check-card-chain.mjs` gives `failureCount: 0`; the three `capture.sh --check` results are
in the packet hand-off.
