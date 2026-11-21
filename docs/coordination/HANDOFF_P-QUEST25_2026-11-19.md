# Top 25 quest playtest hand-off

Normal raids deal ten distinct offers. Each of the five zones contributes two of its five quests. All seats see the same initial board and each human can finish an offer once per match. The authoritative server owns the draw, placement, combat effects, reward inventory, and personal completion records. [Playtest rules](../quests.md) · [Design workbook](../quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx)

| Packet hand-off | Result |
| --- | --- |
| PACKET | P-QUEST25-BOARD-1, ATTEMPTS, REWARDS, SPECIAL, WIRE, CONTROLS, JOURNAL, WORLD-CUES and PLAYTHROUGH serve Phase 2 item 13 and the owner-approved quest exception. |
| BRANCH | `codex/top25-quests` at `1beb7e7cd`, based on `feature/unity-rework` at `24a2ce99f`. |
| GATE | PASS (full) on the gameplay commit. PostgreSQL server and placement, parity, Unity EditMode, headless smoke and release-only bot checks passed. Local log `.agent-runs/gate/20261119T012938-codex-top25-quests-full.log`. |
| CHANGED | Rust simulation, room and map admission, protocol fixtures and decoders, human and bot views, Unity controls, journal, maps and world presenters, test harnesses, design and verification records. Use `git diff --name-only 24a2ce99f 1beb7e7cd` for the complete file list. |
| D-RULES | D1 uses native scenario proofs for the approved design and the historical K1 witnesses for legacy behavior. D2 quest reward balance is playtest tuning. D5 and D10 use the owner-approved quest exception. D6 retains the existing disconnect reporting. The room tests cover independent session resume. D9 keeps game decisions and movement correction on the server. |
| DEVIATIONS | [DEV-040](../DEVIATIONS.md#entries) records personal completion, zoned selection and named rewards. |
| SHARED RECORDS | The coordinator updated [MIGRATION](../MIGRATION.md) for this local candidate. |
| LEGACY DOUBTS | None. The owner selected the workbook’s player-facing Rime Hunt objective, the Feed or Hunt feed branch, and personal completion in place of the lobby-wide winner. |
| NOT ESTABLISHED | Human balance, remote hosting performance, cross-architecture determinism, and Valgrind performance proof. This branch has been tested locally. |
| RISKS | Protocol 19 requires matching clients and servers. The checkpoint and replay formats changed. |

## Acceptance evidence

- A 32-map deterministic sweep covered all 25 definitions, exactly two physical starting givers per zone, and reachable required sites. Admission rejects disconnected candidate starts and repairs unsafe fallback spawns. The original board oracle at `bcfc393f6` is unchanged.
- Two humans complete each of the 25 quests in the same generated match, exercise each reward, and recover their individual completion records after restoration. Those scenarios control positions and encounter setup. A separate encoded-room playthrough uses normal movement, independent 30-second channels, two Stasis casts and reconnections.
- Full-slot choices, declining and looting another player's spell preserve charges and cooldowns. Focused tests cover crown theft and renewal, returning targets, simultaneous finishes, public markers, WallBuster collision and rendering, and Rebirth Egg kill and result settlement.
- Unity 6000.0.85f2 passed all 1,598 EditMode tests. In separate two-player Chrome sessions, both WebGL players saw the same ten quests, entered the raid, and kept independent acceptance state for Two Banners. [Rendered playtest and screenshot](../evidence/quest25-playtest-2026-11-19/README.md)
- The C# net library passed 2,183 tests. The JavaScript verifier passed 69,850 checks against v19, including 94 server vectors, 303 malformed inputs and 25 chains. The three documented card capture commands independently reproduced the accepted bytes. [Provenance record](../evidence/quest25-provenance-2026-11-19/README.md)

The full gate uses the fixture-regeneration, baked-artifact and coordinator flags. With the separate board `--oracle` flag, the path guard reports the six regenerated native and provenance files under `parity/`. The independent diff against the original packet confirms that its oracle and brief are byte-identical. The `parity/abilities/hash-tape.json` recorder and the pinned catalog, runtime and witness capture scripts generated those six files. [Packet and recorder commands](../backlog/packets/P-QUEST25-WIRE.md)

Reproduce the full gate with a test PostgreSQL database and the .NET SDK available.

```sh
scripts/gate.sh --base 24a2ce99f --regen-fixtures --allow-artefacts --coordinator --keep-going
```
