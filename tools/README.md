# tools

Capture tools, checkers, proofs and test helpers. Nothing here ships in the game. Most folders are one capture tool with
its own `capture.sh`; the generated results land in `parity/` and `protocol/fixtures/`, which are oracles and are never
edited by hand.

## Capture tools (`*-fixtures`)

Each `tools/<name>-fixtures/` runs the real legacy TypeScript source in a pinned Docker image and writes (or, with
`--check`, verifies) a corpus under `parity/`:

```sh
tools/<name>-fixtures/capture.sh --check     # verify the checked-in corpus; capture.sh alone writes a new one
```

Needs Docker. The families, by name prefix (`bot-*` here means the folders `bot-*-fixtures`):

| Family | Folders | What they capture |
|---|---|---|
| Abilities | `ability-*` | Ability rules: overlap, refund, void. |
| Bots | `bot-*`, `bots-planning-*`, `brain-tactics-*`, `reflex-geometry-*`, `percept-witness-*`, `perception-*` | The legacy bot Brain: memory, reflexes, tactics, strategy, public facts, fill and tournament. |
| Cards and items | `card-*`, `cards-*`, `consumable-*`, `chest-*`, `kill-augment-*`, `loot-world-*`, `initial-loot-*`, `trophy-*` | Card catalog and runtime, consumables, chests and loot. |
| Factions and status | `faction-*`, `fire-*`, `frost-*`, `holy-demon-blood-*`, `bruiser-*`, `plague-*`, `sentenced-*`, `status-*`, `corpse-*`, `heal-barrier-*`, `barrier-view-*`, `cannon-*` | Per-faction mechanics and status effects. |
| Mobs and combat | `mob-*`, `mark-*`, `owner-combat-*`, `overkill-*`, `pack-leader-*`, `hunt-*`, `slow-starter-*`, `regen-*` | Mob behaviour, marks, combat rules. |
| World | `arena-*`, `map-*`, `terrain-*`, `initial-world-*`, `vision-*`, `walk-hint-*`, `npc-*` | Maps, terrain, vision and NPCs. |
| Quests, trade, wishes | `quest-*`, `trade-*`, `wish-*`, `knowledge-*`, `training-*` | Quest, trade and wish rules and what they publish. |
| Players and storage | `player-*`, `persist-*`, `content-*` | Player resets, persistence and content tables. |

## Checkers, proofs and helpers

| Folder | What it is |
|---|---|
| `protocol-js/` | Holds the JS decoder to the golden vectors: `node tools/protocol-js/check-vectors.mjs`. |
| `determinism/` | Compares determinism-sweep output across architectures ([README](determinism/README.md)). |
| `perfproof/` | Deterministic A/B performance proof: `python3 tools/perfproof/perfproof.py --help`. |
| `pgtest/` | Throwaway PostgreSQL for tests: `eval "$(tools/pgtest/start.sh)"`, then `tools/pgtest/stop.sh`. |
| `browser-proof/` | Chromium over WSS against `rh-server`: `node tools/browser-proof/proof.mjs`. |
| `map-bench/`, `stat-fold/`, `balance-camps/`, `hitbox-measure/`, `latency/` | Benchmarks and measurements, each with its own README or header. |
| `account-catalog/` | Exports account presentation JSON for Unity (`export.sh --check`). |
| `unity-browser-reload-proof/`, `unity-readiness/`, `scene-candidate/`, `training-console/`, `release/` | Checks tied to the Unity client and its build identity. |
| `staging-full-runtime/`, `staging-native-artifact/`, `backup-restore-proof/`, `full-runtime-account-proof/` | Staging preparation and backup proofs; nothing here deploys. |
| `art/`, `sweep/`, `provenance/`, `npc-witness-context/`, `heal-barrier-callers/`, `pve-dot/`, `slow-starter-native29/` | Palette and art checks, the sweep workflow, provenance chain checks and one-off witnesses. |

## Rules

Changes here are made inside a packet. The gate decides what a change must prove; see [AGENTS.md](../AGENTS.md).
