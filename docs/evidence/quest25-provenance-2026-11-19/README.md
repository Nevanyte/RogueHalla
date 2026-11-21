# Card provenance refresh, 2026-11-19 (Quest25 v19)

Genuine recaptures used the documented card capture wrappers from the repository root. Each wrapper used `node:22.23.4-bookworm-slim@sha256:aaa4b5baf1ecff3b283486bb1f90405cdda225a3e6f79660d2c559fa510a7292` on `linux/amd64`, mounted the repository read-only at `/repo`, mounted its external output directory writable at `/out`, and placed fresh `node_modules` on tmpfs. The workspace source `tools/protocol-js/check-vectors.mjs` was checked against the catalog output immediately before acceptance.

## Catalog

`parity/card-catalog/source.json`: `04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2` -> `5ecda053f87639336b9375c223ccde57d530f42b486f26b62ccc69cb9010e210`. The 409 cases, families, source and harness inventories, and protected-pin inventory were unchanged. The recursive leaf diff contains exactly the v19 checker pin:

| JSON pointer leaf | Previous | Recaptured |
|---|---|---|
| `/protectedSha256/tools~1protocol-js~1check-vectors.mjs` | `"aebbcc177fb42dc39676c26b5bf64ec21b142c6230805f7eaf0328b82d547b2b"` | `"e06942914a5a156f37936b1fea6e15057b2578385bafdbf000d1f16db392f139"` |

## Runtime and witness

- Runtime: `parity/card-runtime/source.json` `2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340` -> `78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb`; `INPUT_SHA256.json` `6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557` -> `7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57`. The recapture retained 65 cases, 6236 steps, and 3179 private records. Recursive leaf diffs (13 source leaves, 13 input leaves) are limited to the catalog reference and accepted overlay metadata plus the runtime capture tool harness pin:

| JSON pointer leaf | Previous | Recaptured |
|---|---|---|
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/0/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/0/from` | `"885f7b8289eb0b9f7e54fccc632c684b59c3e30bd14382de49d5f7931b727b14"` | `"aebbcc177fb42dc39676c26b5bf64ec21b142c6230805f7eaf0328b82d547b2b"` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/0/leaf` | `"/protectedSha256/tools/determinism/compare.py"` | `"/protectedSha256/tools/protocol-js/check-vectors.mjs"` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/0/to` | `"c5dcfa1d4e0ddf3f22475082bd40e4af80a3c99d98cc0128ce10b825822d3b7a"` | `"e06942914a5a156f37936b1fea6e15057b2578385bafdbf000d1f16db392f139"` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/1/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `<missing>` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/1/from` | `"1926d0d6706f73d5379687600d127490ffd14b0227dbd1a9d0bddaa240425a0e"` | `<missing>` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/1/leaf` | `"/protectedSha256/tools/determinism/test_compare.py"` | `<missing>` |
| `/provenance/acceptedProvenanceOverlay/acceptedChanges/1/to` | `"1c426f4ffe118e1680c0d63ebf085827d2eaa32616b85600a01ef9c098daaa8d"` | `<missing>` |
| `/provenance/acceptedProvenanceOverlay/effectiveSha256` | `"04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2"` | `"5ecda053f87639336b9375c223ccde57d530f42b486f26b62ccc69cb9010e210"` |
| `/provenance/acceptedProvenanceOverlay/preservedPriorLineage` | `"catalog original d50c03e471f9d11ff790cac0f31d4ab82b5bb45f1b963b41725d58814cc6f74e -> accepted 9e76292b7ae74d305da2729a2ceba63965e64aa9fd670ab85568ae7909270507 -> accepted 2ddb8f85114ff6e8e70c2d70b8207ed3f32c71431845f3cce1007be494e39792 -> accepted e2de5226d31fc7fe4f23d8cd0552ad73be44855bb3109ebed8042b2fd899b9c3 -> accepted 9e194b56fa5e435c6546440d3f22ba40d671dc79d259936f3db90d26ad1e96ca -> this effectiveSha256; earlier exact leaf transitions/receipts remain in docs/evidence/ci-card-catalog-protocol13, docs/evidence/ci-browser-provenance-chain, docs/evidence/browser-provenance14 and docs/evidence/ci-provenance-refresh-2026-11-14; this hop admits only the two listed current-to-final protected leaves"` | `"catalog original d50c03e471f9d11ff790cac0f31d4ab82b5bb45f1b963b41725d58814cc6f74e -> accepted 9e76292b7ae74d305da2729a2ceba63965e64aa9fd670ab85568ae7909270507 -> accepted 2ddb8f85114ff6e8e70c2d70b8207ed3f32c71431845f3cce1007be494e39792 -> accepted e2de5226d31fc7fe4f23d8cd0552ad73be44855bb3109ebed8042b2fd899b9c3 -> accepted 9e194b56fa5e435c6546440d3f22ba40d671dc79d259936f3db90d26ad1e96ca -> accepted 04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2 -> this effectiveSha256; earlier exact leaf transitions/receipts remain in docs/evidence/ci-card-catalog-protocol13, docs/evidence/ci-browser-provenance-chain, docs/evidence/browser-provenance14 and docs/evidence/ci-provenance-refresh-2026-11-14; this hop admits only the one listed current-to-final protected leaf"` |
| `/provenance/acceptedProvenanceOverlay/priorAcceptedEffectiveSha256` | `"9e194b56fa5e435c6546440d3f22ba40d671dc79d259936f3db90d26ad1e96ca"` | `"04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2"` |
| `/provenance/harnessInputs/2/sha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/provenance/immutableReferences/0/sha256` | `"04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2"` | `"5ecda053f87639336b9375c223ccde57d530f42b486f26b62ccc69cb9010e210"` |

Runtime input inventory leaf diff:

| JSON pointer leaf | Previous | Recaptured |
|---|---|---|
| `/acceptedProvenanceOverlay/acceptedChanges/0/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/acceptedProvenanceOverlay/acceptedChanges/0/from` | `"885f7b8289eb0b9f7e54fccc632c684b59c3e30bd14382de49d5f7931b727b14"` | `"aebbcc177fb42dc39676c26b5bf64ec21b142c6230805f7eaf0328b82d547b2b"` |
| `/acceptedProvenanceOverlay/acceptedChanges/0/leaf` | `"/protectedSha256/tools/determinism/compare.py"` | `"/protectedSha256/tools/protocol-js/check-vectors.mjs"` |
| `/acceptedProvenanceOverlay/acceptedChanges/0/to` | `"c5dcfa1d4e0ddf3f22475082bd40e4af80a3c99d98cc0128ce10b825822d3b7a"` | `"e06942914a5a156f37936b1fea6e15057b2578385bafdbf000d1f16db392f139"` |
| `/acceptedProvenanceOverlay/acceptedChanges/1/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `<missing>` |
| `/acceptedProvenanceOverlay/acceptedChanges/1/from` | `"1926d0d6706f73d5379687600d127490ffd14b0227dbd1a9d0bddaa240425a0e"` | `<missing>` |
| `/acceptedProvenanceOverlay/acceptedChanges/1/leaf` | `"/protectedSha256/tools/determinism/test_compare.py"` | `<missing>` |
| `/acceptedProvenanceOverlay/acceptedChanges/1/to` | `"1c426f4ffe118e1680c0d63ebf085827d2eaa32616b85600a01ef9c098daaa8d"` | `<missing>` |
| `/acceptedProvenanceOverlay/effectiveSha256` | `"04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2"` | `"5ecda053f87639336b9375c223ccde57d530f42b486f26b62ccc69cb9010e210"` |
| `/acceptedProvenanceOverlay/preservedPriorLineage` | `"catalog original d50c03e471f9d11ff790cac0f31d4ab82b5bb45f1b963b41725d58814cc6f74e -> accepted 9e76292b7ae74d305da2729a2ceba63965e64aa9fd670ab85568ae7909270507 -> accepted 2ddb8f85114ff6e8e70c2d70b8207ed3f32c71431845f3cce1007be494e39792 -> accepted e2de5226d31fc7fe4f23d8cd0552ad73be44855bb3109ebed8042b2fd899b9c3 -> accepted 9e194b56fa5e435c6546440d3f22ba40d671dc79d259936f3db90d26ad1e96ca -> this effectiveSha256; earlier exact leaf transitions/receipts remain in docs/evidence/ci-card-catalog-protocol13, docs/evidence/ci-browser-provenance-chain, docs/evidence/browser-provenance14 and docs/evidence/ci-provenance-refresh-2026-11-14; this hop admits only the two listed current-to-final protected leaves"` | `"catalog original d50c03e471f9d11ff790cac0f31d4ab82b5bb45f1b963b41725d58814cc6f74e -> accepted 9e76292b7ae74d305da2729a2ceba63965e64aa9fd670ab85568ae7909270507 -> accepted 2ddb8f85114ff6e8e70c2d70b8207ed3f32c71431845f3cce1007be494e39792 -> accepted e2de5226d31fc7fe4f23d8cd0552ad73be44855bb3109ebed8042b2fd899b9c3 -> accepted 9e194b56fa5e435c6546440d3f22ba40d671dc79d259936f3db90d26ad1e96ca -> accepted 04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2 -> this effectiveSha256; earlier exact leaf transitions/receipts remain in docs/evidence/ci-card-catalog-protocol13, docs/evidence/ci-browser-provenance-chain, docs/evidence/browser-provenance14 and docs/evidence/ci-provenance-refresh-2026-11-14; this hop admits only the one listed current-to-final protected leaf"` |
| `/acceptedProvenanceOverlay/priorAcceptedEffectiveSha256` | `"9e194b56fa5e435c6546440d3f22ba40d671dc79d259936f3db90d26ad1e96ca"` | `"04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2"` |
| `/harnessInputs/2/sha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/immutableReferences/0/sha256` | `"04c2c8d0bf4547e2e1db0050179c92e9d1458d80cb908e5ade5b049f8819c9f2"` | `"5ecda053f87639336b9375c223ccde57d530f42b486f26b62ccc69cb9010e210"` |

- Witness: `parity/card-runtime-witness/source.json` `9ce220485e6b505c5af37ee28a9900d716881ddfbc41cbfa2882ebd32ae0532c` -> `b5eae76f97f50b47e87fbfcd98f7d8acbb3ab10637f6ac30efc7638629e56dbc`; `INPUT_SHA256.json` `d88552075b22b31b9080402308e368c3bd8ea54d106b528c584f8a9aa43e8be5` -> `50eb1f6484f2b679dce588e0d3a566341efc4a2720cf8a6d5a772175283c0498`. The recapture retained 2 cases and 6 steps; `extraDamage=2.891037366083104` and `sourceRatio=1.08` were unchanged. Recursive leaf diffs (22 source leaves, 22 input leaves) are limited to accepted provenance overlays, their pinned-input metadata, and the witness capture tool harness pin:

| JSON pointer leaf | Previous | Recaptured |
|---|---|---|
| `/provenance/acceptedProvenanceOverlay/0/effectiveSha256` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` | `"78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb"` |
| `/provenance/acceptedProvenanceOverlay/0/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/acceptedProvenanceOverlay/0/priorAcceptedEffectiveSha256` | `"e6c71914af6c07c80573181b53ec6466c65f605ff3982ce53c4e77ba20c4f827"` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` |
| `/provenance/acceptedProvenanceOverlay/1/effectiveSha256` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` | `"7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57"` |
| `/provenance/acceptedProvenanceOverlay/1/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/acceptedProvenanceOverlay/1/priorAcceptedEffectiveSha256` | `"4c67e83e8997c615b71cd54f3d3ea8648c274a0df377f90b6294864024ec16cf"` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` |
| `/provenance/acceptedProvenanceOverlay/2/effectiveSha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/provenance/acceptedProvenanceOverlay/2/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/acceptedProvenanceOverlay/2/priorAcceptedEffectiveSha256` | `"e1a7d65a6d234910b63b9efdf52eb6e5ce5efd646af19b00006fb28b0699482c"` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` |
| `/provenance/actualPinnedInputs/1/acceptedProvenanceOverlay/effectiveSha256` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` | `"7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57"` |
| `/provenance/actualPinnedInputs/1/acceptedProvenanceOverlay/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/actualPinnedInputs/1/bytes` | `25360` | `25107` |
| `/provenance/actualPinnedInputs/1/sha256` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` | `"7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57"` |
| `/provenance/actualPinnedInputs/3/acceptedProvenanceOverlay/effectiveSha256` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` | `"78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb"` |
| `/provenance/actualPinnedInputs/3/acceptedProvenanceOverlay/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/actualPinnedInputs/3/bytes` | `53035290` | `53035091` |
| `/provenance/actualPinnedInputs/3/sha256` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` | `"78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb"` |
| `/provenance/actualPinnedInputs/6/acceptedProvenanceOverlay/effectiveSha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/provenance/actualPinnedInputs/6/acceptedProvenanceOverlay/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/provenance/actualPinnedInputs/6/bytes` | `7941` | `7742` |
| `/provenance/actualPinnedInputs/6/sha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/provenance/harnessInputs/1/sha256` | `"5f1547d45f6a3854ff5fa6a98d5ec87efac69764a867d6fa103c4bdd957555f3"` | `"ed38e5b41f23becfcc742e79209f6d517e0f5dd4e30957250aca79e3ee9d9524"` |

Witness input inventory leaf diff:

| JSON pointer leaf | Previous | Recaptured |
|---|---|---|
| `/acceptedProvenanceOverlay/0/effectiveSha256` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` | `"78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb"` |
| `/acceptedProvenanceOverlay/0/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/acceptedProvenanceOverlay/0/priorAcceptedEffectiveSha256` | `"e6c71914af6c07c80573181b53ec6466c65f605ff3982ce53c4e77ba20c4f827"` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` |
| `/acceptedProvenanceOverlay/1/effectiveSha256` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` | `"7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57"` |
| `/acceptedProvenanceOverlay/1/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/acceptedProvenanceOverlay/1/priorAcceptedEffectiveSha256` | `"4c67e83e8997c615b71cd54f3d3ea8648c274a0df377f90b6294864024ec16cf"` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` |
| `/acceptedProvenanceOverlay/2/effectiveSha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/acceptedProvenanceOverlay/2/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/acceptedProvenanceOverlay/2/priorAcceptedEffectiveSha256` | `"e1a7d65a6d234910b63b9efdf52eb6e5ce5efd646af19b00006fb28b0699482c"` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` |
| `/actualPinnedInputs/1/acceptedProvenanceOverlay/effectiveSha256` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` | `"7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57"` |
| `/actualPinnedInputs/1/acceptedProvenanceOverlay/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/actualPinnedInputs/1/bytes` | `25360` | `25107` |
| `/actualPinnedInputs/1/sha256` | `"6250fb9277f6f016527ddf256a85e2b004a1035b977105bd1f2ea112c773f557"` | `"7b43ad7c50c5abffe38dc8ea4c93565f605960bb9a93ed3a7d16f30ee2c05b57"` |
| `/actualPinnedInputs/3/acceptedProvenanceOverlay/effectiveSha256` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` | `"78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb"` |
| `/actualPinnedInputs/3/acceptedProvenanceOverlay/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/actualPinnedInputs/3/bytes` | `53035290` | `53035091` |
| `/actualPinnedInputs/3/sha256` | `"2b425b02b41163eb6725aab767fcc029f716677d47ba66ae778a42b21d87b340"` | `"78864e722d864faed38a1bae6195fd2a3ff1bc9cb69e8e111fb91c7ee603e5bb"` |
| `/actualPinnedInputs/6/acceptedProvenanceOverlay/effectiveSha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/actualPinnedInputs/6/acceptedProvenanceOverlay/evidence` | `"docs/evidence/ci-provenance-refresh-2026-11-17/README.md"` | `"docs/evidence/quest25-provenance-2026-11-19/README.md"` |
| `/actualPinnedInputs/6/bytes` | `7941` | `7742` |
| `/actualPinnedInputs/6/sha256` | `"03411d9440b1c7b799ee971d801638b2305f27598852f719e85c912e0ddce607"` | `"a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7"` |
| `/harnessInputs/1/sha256` | `"5f1547d45f6a3854ff5fa6a98d5ec87efac69764a867d6fa103c4bdd957555f3"` | `"ed38e5b41f23becfcc742e79209f6d517e0f5dd4e30957250aca79e3ee9d9524"` |

`tools/card-runtime-witness-fixtures/FROZEN_PINS.json` remains byte-identical (`7ed1086915fafd5fa129669270d6d53090f4b81052660a18318df60cab8b3023`). The documentation overlay remains unchanged. The runtime capture tool now hashes to `a72e598eac84cb655677ba5d8e429b6d67928260b867b6c336ea2948656479d7`; witness capture tool changed from `5f1547d45f6a3854ff5fa6a98d5ec87efac69764a867d6fa103c4bdd957555f3` to `ed38e5b41f23becfcc742e79209f6d517e0f5dd4e30957250aca79e3ee9d9524`.

## Verification

The chain auditor passed on the quest branch on 2026-11-19 with `failureCount: 0`. The three Docker-based capture checks still need to run from the coordinator's Docker-enabled host context; this subagent environment cannot access the Docker API socket.

```sh
node tools/provenance/check-card-chain.mjs
bash tools/card-catalog-fixtures/capture.sh --check
bash tools/card-runtime-fixtures/capture.sh --check
bash tools/card-runtime-witness-fixtures/capture.sh --check
```

The independent recaptures reproduced the tracked bytes for all 409 catalog cases, all 65 runtime cases and both witness cases. Run the three `--check` commands before treating the refresh as fully verified.
