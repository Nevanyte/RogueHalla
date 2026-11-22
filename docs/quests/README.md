# Quest design workbook

The owner approved the workbook’s `Top_25` objectives and `Spell_Reworks` rewards for normal Rust and Unity raids on November 18, 2026. The workbook retains its original filename. Other sheets record design discussions and balance estimates. [Normal raid rules and legacy K1 reference](../quests.md) · [Implementation packets](../backlog/README.md)

**Status: future design only, except the Top 25 quests above and the augment and elite rework below.** Apart from
those, nothing in this folder is implemented, scheduled or approved. It is not in `docs/ORDER_OF_OPERATIONS.md` and has no packet. Building
any of it needs owner approval and a packet in `docs/backlog/packets/`. The live quest system is documented in
[`docs/quests.md`](../quests.md).

**Implemented: the augment and elite rework from this workbook.** It is live-only: it applies when `SimConfig::augments`
or `SimConfig::elites` is `Rework` (the live server, `SimConfig::live_content()`). Legacy and parity worlds keep the
legacy numbers. Marked Man (an owner addition, 2026-11-18) is built too (P-AUG-NATIVE-5). The plan of record and its packets
are in [`../backlog/AUGMENT_ELITE_REWORK.md`](../backlog/AUGMENT_ELITE_REWORK.md).

- `FUTURE_ONLY_Quest_Design_Discussion.xlsx`: the whole 2026-11-13 discussion. It covers pricing stats in farming time
  (camp-equivalents), the quest math and a reward pricer, calibration data, quest methodology, reward feel, quests built on
  existing mechanics, the spell pool and interactions, a review of the team quest sheet with item fixes, and rejected ideas.
  The README sheet explains how to use it.
- [`../QUEST_VALUE_MODEL.md`](../QUEST_VALUE_MODEL.md): the formulas with derivations.
- [`../backlog/packets/P-BALANCE-3.md`](../backlog/packets/P-BALANCE-3.md): the walk-time measurement tool (ready, not scheduled).

All numbers are rough estimates from one `camp_bench --quick` run and one map. The coliseum values of spells are guesses.
