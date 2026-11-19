Packet ID and what this change serves (an ORDER_OF_OPERATIONS phase/item, or "not in the plan"); then the hand-off report from AGENTS.md:

```
PACKET:          <ID> — serves <ORDER_OF_OPERATIONS phase/item, or "not in the plan: owner approval needed">
BRANCH:          <branch> @ <short sha>
GATE:            PASS (full) [skipped: …] | FAIL (attempt n/2)
CHANGED:         <files>
D-RULES:         <parity standard used (D1); harness verdict or n/a (D2); anything touching D5/D6/D9>
DEVIATIONS:      none | proposed DEV-<n>: <one line>
SHARED RECORDS:  none | proposed edits: <file: change>
LEGACY DOUBTS:   none | packages/<file>:<line> — <what was unclear>
NOT ESTABLISHED: <what this evidence does not prove>
RISKS:           none | <one line each>
```
