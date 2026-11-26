# P-OPEN-ALPHA-CI-1 — Give cold native determinism producers time to finish

- **Serves:** ORDER_OF_OPERATIONS Phase 5, item 21, staged rollout verification. Owner-approved on 2026-11-24.
- **Implementer tier:** implementer
- **Reviewer:** reviewer
- **Integration base:** open-alpha rehearsal `9bc1d1fd3fcb0afd7f6760c88a93dced8a5b90d8`
- **D-rules touched:** Existing parity and determinism checks apply. D2 harness change and D5/D6/D9 are inapplicable.

## 1. ID and outcome

Raise the Native determinism producer job limit from 75 to 120 minutes. Both cold producers in PR 92 timed out during the
focused release tests. The additional time gives both native architectures a chance to publish artifacts for comparison
and foreign replay. [Timed-out run](https://github.com/Nevanyte/RogueHalla/actions/runs/45517475994)

## 2. Dependencies

PR 92 and the assembled rehearsal candidate. Retain the existing producer matrix and dependency graph between jobs.

## 3. Relevant legacy paths

None. This changes a GitHub Actions job limit.

## 4. New paths

`docs/backlog/packets/P-OPEN-ALPHA-CI-1.md`.

## 5. Exclusive write scope

This brief and the producer timeout and rationale in `.github/workflows/determinism.yml` only. The owner approved that
protected workflow edit.

## 6. Contracts and references

C-DET and C-PARITY govern the existing checks. [Producer configuration](../../../.github/workflows/determinism.yml#L23) ·
[Earlier cold-run packet](P-CI-GREEN-2.md) · [Contracts](../../CONTRACTS.md)

## 7. Behaviour to preserve

Preserve the native runner matrix, exact checkout identity, cache settings, all existing commands, artifact publication,
strict comparison, both replay directions, and the final success gate.

## 8. Acceptance checks

- The isolated workflow oracle expects `jobs.produce.timeout-minutes` to be 120. It fails on the 75-minute baseline.
- Restore the old timeout in memory and compare the parsed workflow with the base. The job definitions must match.
- The next GitHub run must pass both producers, comparison, both foreign replays, and the Native determinism gate.

## Oracle files (read-only for implementers)

The existing workflow tests, sweep, comparison and replay inputs are the oracle. The failed GitHub run records the
baseline timeout. The local workflow assertion adds no repository test file.

## 9. Validation commands

```sh
git diff --check 9bc1d1fd3
python3 - <<'PY'
from pathlib import Path
import subprocess
import yaml

path = '.github/workflows/determinism.yml'
base = yaml.safe_load(subprocess.check_output(['git', 'show', '9bc1d1fd3:' + path], text=True))
workflow = yaml.safe_load(Path(path).read_text())
assert workflow['jobs']['produce']['timeout-minutes'] == 120
assert base['jobs']['produce']['timeout-minutes'] == 75
workflow['jobs']['produce']['timeout-minutes'] = 75
assert workflow == base
PY
```

The coordinator runs the release gate and monitors the GitHub checks on the draft PR.

## 10. Risk and suggested model tier

A cold producer can still exceed 120 minutes. The run must finish before elapsed time and the cross-architecture result
are known. This is an implementer-tier configuration change.

## 11. Known traps

The producer job limit applies to each matrix leg. Foreign replay, comparison, and the final gate have separate limits.
A canceled cold run can leave the next run without a warm release cache.
