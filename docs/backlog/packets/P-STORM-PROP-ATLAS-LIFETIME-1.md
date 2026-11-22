# P-STORM-PROP-ATLAS-LIFETIME-1 — Stable sprites across Editor cleanup

## Outcome and ownership

Phase 3 item 16a, during integrated verification of the larger concealment canopy and arena art. Own the regional sprite atlas's Editor cache lifetime and one regression check in the Fire geology Editor suite. This change concerns Unity presentation and the authoring workflow. D1, D2 and D9 have no simulation changes.

## Failing oracle

The full project gate on `f078d499d` reported 1,944 of 1,945 Unity tests passing. The delivered Fire scree renderer received a different sprite instance from the atlas it had just queried. A fresh isolated run of the original test passed. Running Editor asset cleanup between lookup and placement reproduced two different instances with the same name and map coordinates. No source painting had been edited.

## Implementation and check

Keep the artist definitions referenced for the life of the Editor atlas. Resolve them before reading art values for a fresh bake so validation on initial load cannot invalidate a finished atlas. Inspector edits continue to invalidate it. Built players keep their normal lazy loading. Exercise the original Fire geology test, the deterministic Editor-cleanup reproducer, real authored-prop editing and the full gate.
