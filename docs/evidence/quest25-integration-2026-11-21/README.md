# Top 25 quest integration

The shared wire vectors were regenerated using the Rust encoder for protocol 23. The Rust, C# and JavaScript decoders share the version, heal and egg tags, and quest payloads. The target branch’s historical v19, v20 and v21 fixture bytes are retained.

## Card capture provenance

The card corpora below were refreshed on integration snapshot `6190c1d56d4c53eb92282605ab1ea2e74bd40ac4` against `origin/feature/unity-rework` at `5567458fbac5d58c8d2ba5c93d83afb138f25aab`. Captures used the checked-in Docker wrappers, `node:22.23.4-bookworm-slim@sha256:aaa4b5baf1ecff3b283486bb1f90405cdda225a3e6f79660d2c559fa510a7292`, `linux/amd64`, a read-only repository mount, and a separate writable output directory.

### Catalog

`parity/card-catalog/source.json` changed from baseline SHA256 `ab40ad59065713589f94c7f33dbbac3ea6cd9cc028463d90d6c6fe7fe1856214` to `8e444cf1eed6d2c0b0e1f73faec4f330221f819fe6f6991227a9b3ae3b044b9f`. The canonical 409 cases, source inventory, harness inventory, and protected inventory remain unchanged (325 entries). The three updated hashes are listed below.

| Protected leaf | From baseline | Current |
|---|---|---|
| `tools/protocol-js/check-vectors.mjs` | `aebbcc177fb42dc39676c26b5bf64ec21b142c6230805f7eaf0328b82d547b2b` | `e06942914a5a156f37936b1fea6e15057b2578385bafdbf000d1f16db392f139` |
| `tools/determinism/compare.py` | `e3fe6a69c7591e825411714218b97b4b63803a986bb0b58ef0b0152d60a01d84` | `1434f2e87a14c683f1346ba78c1dd3dcbe56be254da5883793b8d5cf13ab5cdf` |
| `tools/determinism/test_compare.py` | `85aa88b08e1a29fb96e31b25f379af4e91a89a53a4296875713991f56d996501` | `894e3804e909463235f642569c1521d8b160285ac55d7c8ac5c61bd412ebc1d5` |

The card-runtime accepted overlay preserves the historical catalog lineage from original `d50c03e471f9d11ff790cac0f31d4ab82b5bb45f1b963b41725d58814cc6f74e`, through the prior accepted catalog `ab40ad59065713589f94c7f33dbbac3ea6cd9cc028463d90d6c6fe7fe1856214`, to this captured catalog. It adds only the three listed protected-hash leaves.

### Runtime and witness

`parity/card-runtime/source.json` is SHA256 `1242c3ca67f3c8c141192e136862ffd654d02ae20c0726dcecf5fb071ce47571`; its `INPUT_SHA256.json` is `bd64eedec0a64f03eeb3d1f41d5497b4b2ed478c9a2f51fa9ea456b8c15f74ad`. The recapture retained all 65 cases, 6,236 ordinary steps, and 3,179 private records. Compared with the prior accepted corpus, changes are limited to the catalog reference and its accepted provenance record, plus the runtime capture tool's harness hash; case content is unchanged.

`parity/card-runtime-witness/source.json` is SHA256 `deb05ebce31339f377caddb7e5c19c997071fb4123605a0efe824a2ca02ac183`; its `INPUT_SHA256.json` is `dab3bf721f5d8fc8da03f1d843e2d502b72bae2a32653485741f537993bbaecb`. The recapture retained both witness cases and all six steps; the actual extra damage remains `2.891037366083104` and the observed source multiplier ratio remains `1.08`. Changes are limited to accepted metadata for the refreshed runtime source, input inventory, and capture-tool hashes; witness gameplay results remain unchanged.

The capture-tool hashes accepted by the refreshed chain are `tools/card-runtime-fixtures/capture.mts` = `a85ce3109ef4be5d22d1289ea23b2f0293be20c47296d78dc41254bd3edcfa63` and `tools/card-runtime-witness-fixtures/capture.mts` = `df1a859a9689a96a9859f079fe59d3abc78c7564a44bf05a823acf81d8f08972`. The chain auditor records the three from/to protected leaves above and returns `failureCount: 0`.

### Verification

All three pinned Docker captures reproduced the recorded bytes. To verify the committed files, run the following commands.

```sh
node tools/provenance/check-card-chain.mjs
bash tools/card-catalog-fixtures/capture.sh --check
bash tools/card-runtime-fixtures/capture.sh --check
bash tools/card-runtime-witness-fixtures/capture.sh --check
```

## Gameplay checks

All 25 recorded quests passed on protocol 23, replaying 2,928 encoded input frames. The protocol suite passed 266 tests and the JavaScript decoder passed 70,995 shared-vector assertions.

The C# net library passed 2,253 tests. Unity 6000.0.85f2 passed all 1,602 EditMode tests.

The two-seat room tests passed at both rates and cover completion visibility, elapsed meditation time, consuming Stasis and reconnecting before and after using the reward at both 20 and 40 Hz. The [recorded room inputs](../quest25-deep-playtest-2026-11-19/README.md) also exercise each quest through a real Rust room. [PR #81](https://github.com/Nevanyte/RogueHalla/pull/81) records the check results for this integration.

## Cross-architecture replay

GitHub's ARM64 and x86-64 runners each verified the other runner's replay at `6190c1d56`. Both produced replay SHA256 `20e7496c2aa7e25f0abdaed9de3f0f4b028ca104714aa478e665a6ad6925b5fa`. The updated comparison tool passed all six scenarios on those artifacts. Its 27 unit tests cover both Boolean values for the optional `top25_quests` replay setting, invalid types, and differing settings between architectures.
