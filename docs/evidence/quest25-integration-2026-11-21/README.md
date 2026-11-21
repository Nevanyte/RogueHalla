# Top 25 quest integration

The shared wire vectors were regenerated using the Rust encoder for protocol 23. The Rust, C# and JavaScript decoders share the version, heal and egg tags, and quest payloads. The target branch’s historical v19, v20 and v21 fixture bytes are retained.

## Card capture provenance

The target snapshot is `5567458fb`. The quest branch extends `tools/protocol-js/check-vectors.mjs` to check the owner’s quest state. This checker is one of the catalog capture’s protected files. The integration regenerated all three dependent corpora with their canonical pinned linux/amd64 Docker capture scripts.

```sh
bash tools/card-catalog-fixtures/capture.sh
bash tools/card-runtime-fixtures/capture.sh
bash tools/card-runtime-witness-fixtures/capture.sh
node tools/provenance/check-card-chain.mjs
```

The catalog capture reproduced 409 cases. Its sole changed JSON leaf was the checker pin under `protectedSha256`, from `aebbcc177fb42dc39676c26b5bf64ec21b142c6230805f7eaf0328b82d547b2b` to `e06942914a5a156f37936b1fea6e15057b2578385bafdbf000d1f16db392f139`. The runtime capture reproduced 65 cases, 6,236 ordinary steps and 3,179 private records. The witness reproduced both cases and six ordinary steps. Re-running all three Docker captures in `--check` mode matched the committed files byte for byte. The prior catalog and runtime hashes are recorded alongside their replacements in the capture tools.

## Gameplay checks

All 25 recorded quests passed on protocol 23, replaying 2,928 encoded input frames. The protocol suite passed 266 tests and the JavaScript decoder passed 70,995 shared-vector assertions.

The C# net library passed 2,253 tests. Unity 6000.0.85f2 passed all 1,602 EditMode tests.

The two-seat room tests passed at both rates and cover completion visibility, elapsed meditation time, consuming Stasis and reconnecting before and after using the reward at both 20 and 40 Hz. The [recorded room inputs](../quest25-deep-playtest-2026-11-19/README.md) also exercise each quest through a real Rust room. [PR #81](https://github.com/Nevanyte/RogueHalla/pull/81) records the check results for this integration.
