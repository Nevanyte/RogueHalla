# docs

An index of this folder. For the project as a whole start at the [root README](../README.md).

## The five sources of truth

[AGENTS.md](../AGENTS.md) names them; read them before planning.

1. [MIGRATION.md](MIGRATION.md): the current checkpoint and the single status record.
2. [ORDER_OF_OPERATIONS.md](ORDER_OF_OPERATIONS.md): the phases, decisions D1 to D10 and the per-operation checklist.
3. [REQUIREMENTS.md](REQUIREMENTS.md), [CONTRACTS.md](CONTRACTS.md), [adr/](adr/README.md), [DEVIATIONS.md](DEVIATIONS.md): what must hold and where we differ from legacy.
4. [backlog/](backlog/README.md): the packets, the units of work.
5. [legacy/](legacy/INVENTORY.md): the legacy game's inventory and README.

## Folders

| Folder | What is in it |
|---|---|
| `adr/` | Architecture decision records. |
| `agent/` | The development process: packets, gate, review, models, the [readability standard](agent/READABILITY.md). |
| `agent-prompts/` | Owner planning artifacts and the prompts of earlier agent sessions. |
| `art/` | The visual art board: palette, assets, audio, licence ledger. |
| `backlog/` | The remaining-work backlog and one file per packet in `backlog/packets/`. |
| `coordination/` | Hand-offs, repair notes, design reviews and the open owner queue (`OWNER_QUEUE.md`). |
| `evidence/` | An **append-only archive of proof** (captures, reports, screenshots). Tools read it; it is not meant for reading. |
| `hosting/` | AWS staging, deployment, scaling, cost, runbooks and the control API. |
| `landmarks/` | Landmark blueprints. |
| `legacy/` | The legacy game: [inventory](legacy/INVENTORY.md), [its old root README](legacy/LEGACY_GAME_README.md), a stale map. |
| `prompts/` | Prompts and state for the Unity rework. |
| `quest-playtests/`, `quests/` | Quest playtest notes; future-only quest design (not implemented). |
| `rework/` | The Unity rework design notes: render plan, controls, vision, UI verification, performance. |
| `sweep/` | The core optimization sweep. |
| `ui/` | HUD and training-console notes. |
| `unity/` | Unity presentation contract, dials, look check, protocol gaps. |

## Top-level files

| File | What it is |
|---|---|
| `MIGRATION.md`, `ORDER_OF_OPERATIONS.md`, `PROJECT_COORDINATION.md`, `OWNER_DECISIONS.md` | Status, plan, lead ownership and lanes, and the index of owner decisions. |
| `REQUIREMENTS.md`, `CONTRACTS.md`, `DEVIATIONS.md` | Requirements, invariants, and intentional differences from legacy. |
| `GAME_DESIGN.md`, `ROGUELIKE_PLAN.md`, `COMBAT_UPGRADE_BASE.md`, `MOB_PROGRESSION.md` | Game design and the legacy overhaul plans. |
| `BOTS.md`, `BOTS_TUNING.md` | The heuristic bots and their tier tuning. |
| `BRUISER_PASSIVES.md`, `FIRE_ROAM.md`, `PLAGUE_CORPSES.md`, `SENTENCED_HUNT.md`, `SENTENCED_REDESIGN.md` | Faction mechanics. |
| `quests.md`, `quests-bot-hook.md`, `QUEST_VALUE_MODEL.md`, `quest-playtest-2026-10-28.md` | Quests: the live system, the bot interface, a future value model, a playtest. |
| `R1_GENERATOR_NOTE.md`, `R2_GENERATOR_AS_BUILT.md`, `ZONE_SHAPES_AS_BUILT.md` | The zone map generator. |
| `NETCODE_COMPETITIVE_PLAN.md`, `PLAYER_PREDICTION.md` | Netcode and prediction. |
| `VISUAL_DIRECTION.md` | Targets for the Unity client's look. |
| `CLOUDFLARE_PUBLIC.md`, `TELEMETRY.md` | Public hosting of the legacy game; balance telemetry and the dashboard. |
