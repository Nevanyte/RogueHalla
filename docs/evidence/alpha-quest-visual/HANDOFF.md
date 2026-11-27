# Quest verification hand-off

```text
PACKET:          P-ALPHA-QUEST-VISUAL-1 — Phase 2 item 11, playable alpha quest verification
BRANCH:          codex/alpha-gameplay-restoration @ 487d6b5e3; Web and Unity code at 12c13f899
GATE:            PASS (full), PostgreSQL suites, 2,849 Unity EditMode cases and release bot checks.
                 Built-in skips: CI source-witness recaptures, Valgrind and cross-architecture determinism.
                 The local browser and sealed Unity Web checks ran separately.
CHANGED:         Rust quest outcome cases and capture, Unity quest map and HUD, Warden's Cage presentation, browser replay and evidence
D-RULES:         D1, existing legacy captures preserved and quest results checked against expected effects. D2, no balance tuning in this packet.
                 D5, two ordinary local browser quests plus final Web planning and raid smoke. D6, ordinary-room play logged client-initiated closes.
                 D9, Unity reads viewer-scoped objectives and reward states from the server.
DEVIATIONS:      none
SHARED RECORDS:  proposed checkpoint entry in docs/MIGRATION.md with the all-quest proof and local Web smoke results
LEGACY DOUBTS:   none
NOT ESTABLISHED: Ordinary-room end-to-end browser play covered Lost Brother and Letters. Controlled Rust scenarios and Unity replays covered all 25.
                 The visual sweep used desktop Chromium at 1440×900 and 1280×720. Other browser engines and smaller displays were outside the sweep.
RISKS:           The replay advances transport ticks only when source ticks coincide. The authority's effect and event times are preserved.
```

The [evidence report](README.md) links the visual captures, all 25 result rows, and both normal-room receipts. The final Web build uses protocol 24 and content `cb811d19ebed6dfe`. The full gate began with the same Rust and wire changes. During its Rust steps, EditMode preflight identified layout regressions. The Unity gate step ran after the resulting C# fixes at `12c13f899`. The 57 focused cases also passed on that revision.

The full local gate log is `.agent-runs/gate/20261127T020305-codex-alpha-gameplay-restoration-full.log`.
