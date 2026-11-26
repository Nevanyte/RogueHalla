# Balance patch (quests, new augments, elites) with a bot built to test it: plan of record (owner-approved 2026-11-22)

The owner approved this plan in the coordinator's chat on 2026-11-22 (MIGRATION "Balance patch and test bot
(2026-11-22)"). Packets are in `docs/backlog/packets/` as `P-RAID-*` and `P-BAL-*`; each cites this file as what it
serves. The integration base when it was written: `feature/unity-rework` @ `d0712ab82` (PR 80 merged).

## Context

**Owner request, 2026-11-22:** "do a balancing patch of quests and new augments + bot redesign in order to test", and
launch a Sonnet sub-agent to search for other system improvements (backend efficiency, security, input delay).

**Owner answers the same day (question dialog):**
1. Bots: **"Lab now, live later"**. A new bot plays quests, elites and augments in headless test matches, seated as a
   human. It can be chosen as a live fill bot behind a server flag that stays off. Live bots take no quests until he
   says so.
2. Numbers: **"Apply, then report"**. The measured retune lands; he gets the before/after table and the DEVIATIONS
   rows to sign.
3. Elites: **"Yes, per-elite numbers"**. Bring each elite, and ranged weapons against them, toward the 10-20% band.
4. Depth: **"Numbers and offer rules"**. Where numbers cannot fix something, a redesign is proposed, not built.

**Why the bot comes first.** Quests and augments were priced from a design workbook whose inputs are marked
unmeasured (`docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx`, sheet Inputs: camps per minute, heal-up downtime,
deaths per minute, encounters while questing, every investment augment's growth rate). Nothing can measure them today:

- No bot can take a quest. The sim gives a bot seat an empty quest view and refuses its quest items, spells and
  choices (`crates/rh-sim/src/quest_view/mod.rs:203`, `quests/board/attempts.rs:217`, `rewards/casts.rs:55`).
- The fill bot (`HumanBot`, `crates/rh-policy/src/bots/bot.rs`) picks the nearest player, else the nearest mob, else
  roams: no retreat, heal, camp or elite valuation. Measured (`tools/balance-camps/README.md`): at minute 5, 39 of 56
  bot builds hold no stat stacks, none holds a card, and about 3 matches in 8 reach minute 5.
- The ported Brain (`BrainBot`) is pinned to frozen legacy witnesses and must not change.
- No Rust tool reports quest completion or augment pick rates.

**Already known to be off** (existing evidence):
- Elites average 17.4% deaths for the reference fighter, but Avalanche Troll is 46.7%, Wyrmling 25.6%, Gaoler 23.3%,
  Ash Drake 22.2%, and Executioner 2.2%, Blood Saint 6.7%, Archon 6.7%, Rat Ogre 8.9%; bow 47.5%, wand 25.2%.
  Augments come only from elite chests, so an augment's price varies about twenty-fold by zone and weapon.
- The workbook's own pricing puts most of the 14 new augments well under what an elite costs even with a helper
  (3.5 camp-equivalents needed; Calluses 0.9, Hazard Walker 0.75, Glacier 0.6, Stoked 0.5, Corpse Hoarder 0.5, Overflow
  0.5 before their 20-50% tuning buffs). A model estimate; the lab confirms or corrects it.

**Outcome.** A bot good enough to be the test player, a lab that measures, seams that make every later retune a
one-line Sonnet packet, and one balance patch whose numbers come from measurement.

## 1. `RaidBot`: a third bot policy (`crates/rh-policy/src/bots/raid/`, all new files)

Nothing pinned moves: `HumanBot`, `BrainBot`, the bot `Command` enum (`bots/types.rs`), `parity/policy/*` and the
`tools/bot-*` captures are untouched. No protocol, observation or policy-contract version changes.

- **Fair by construction.** Reads `Frame14` only (`InputKind::Delivered14`, as `HumanBot`), so one bot serves headless
  and live. `Frame14` already carries the quest view (`delivery/frame.rs:59`), trade, inventory and potions, camps,
  build and reward chests.
- **About five quest executors, not 25 scripts.** The server already lists each seat's legal quest actions with
  positions (`QuestView.actions`, filled by `quests/board/attempts/choices.rs`; verified). The bot walks to
  `objectives`, sends the offered `QuestInteract`, and chooses only in the decision quests; turn-in is automatic.
  - Walker + decision hook: 15 quests (Smuggler's Run, Two Banners, Ember Walk, Let Him Cook, Tuber Eats, Barrow Run,
    The Bargain, A Cruel Joke, Feed or Hunt, Watchtowers, Even Skeletons Get Cold, Lost Brother, Pilgrim's Path,
    Letters, Trade Up). Dwell: Hold the Fort, Stay A While.
  - Second wave: kill quests (The Warden, Elite Slayer, A Bone to Pick, Rime Hunt), PvP and event (Bounty Hunter,
    Gatecrasher, The Burning Crown), search (Whispers in the Brush).
- **One arbiter replaces nearest-first.** Each candidate goal (farm a camp, kill an elite, a quest step, chest, loot,
  heal or retreat, fight a player, go to the coliseum) is scored in the workbook's currency, camp-equivalents per
  minute: `(value x success chance - death chance x what dying loses) / time`. Tier and persona tables
  (`bots/types.rs`, `data/brain_tuning.json`) set risk appetite and execution quality; hysteresis stops dithering.
  The bot decides in the model's currency, so what it chooses is itself a test of the prices.
- **Values are a checked-in table** (`data/raid_values.json`: camp-equivalents per quest reward, per native augment,
  per camp and elite tier), written by hand from the `docs/QUEST_VALUE_MODEL.md` formulas.
- **Reused as is:** `RoomPathService` and its path budget, `Humanizer`, `WishModule`, `PlanModule`/`PlanMap`, `Loot`
  (chest scoring, natives included), `Knowledge` (`mob_share`, `odds`), `planning::matchup_share_under`, `mob_hunt`,
  `kite::away_point`, `memory_prefix::Memory`. **Thin adapter:** `fight::NormalTactics` with `orbwalk` and
  `fight/retreat`. **Reference only:** the elite bench's `Duelist`. **Not usable:** `reflex` (needs facts `Frame14`
  does not publish), `hazards::intents` (raw observation), `temporal_percept/*`.
- **New commands go out as `PublicCommand`** (`QuestAccept`, `QuestInteract`, `UseConsumable`, `Trade`) through the
  bot's own paced queue; the shared `Command` enum is not extended (its variants key `HumanBot`'s stats). Quest spells
  cast on the existing Q and E slots (`systems/abilities/mod.rs:98-101`).
- **Determinism:** own seeded streams on new tags beside HumanBot's 0-3; `Vec`/`BTreeMap` only; no clock.
- **Headless:** `BotKind::Raid` and a human-seat option on `run_bot_match_kind`
  (`crates/rh-headless/src/lib.rs:549-640`, which hard-codes `bot: true`).
- **Live, last and off:** a `BotBrain::Raid` choice in `crates/rh-server/src/bot_fill.rs` / `room/bots.rs`, `HumanBot`
  as fallback. A live RaidBot farms, fights elites, picks augments, drinks potions and retreats. It cannot quest:
  live fill bots are bot seats. Nothing deploys.

## 2. `balance_lab` and the evaluator (`crates/rh-headless`)

- **Lab** (`src/balance_lab.rs`, `src/bin/balance_lab.rs`, feature `balance-lab`), modelled on the elite bench
  (`src/elite_bench.rs`): JSON on stdout, tables on stderr, `--jobs N`, results folded in seed order so output is
  byte-identical. Setup as `examples/source_soak.rs` (`SimConfig::live_content()`, 8 seats), one generated admitted
  map per seed, `map_hash` recorded (the sim silently swaps a map that cannot host the board, `map_admission.rs:39-49`).
- **The observer only reads**, and needs no new event: quest state from each seat's own observation
  (`rows`: `owner_accepted`, `owner_completed`, `expires_at`; `spells`), chests from `reward_chests`, builds and native
  counters from `Build` and `cards::native::readouts`, camps and deaths from `Event::CampCleared` / `Hit` / `Death`.
- **Reports:** the workbook's inputs (camps per player per minute; walk, kill and heal-up seconds; stacks at minutes
  5, 8, 12; deaths per minute by cause; encounters while questing; match length; share reaching the coliseum); per
  quest dealt, accepted, completed, failed by timer, died, time to complete, pickup minute; per reward casts and what
  followed; per augment offered, picked, held by the winner, native growth per minute and time to cap; elites by kind
  and weapon.
- **Paired mode:** the same seeds with and without one grant (`rh_sim::trusted_build::apply`, extended to grant a quest
  reward), mean difference with a confidence interval and the seed count a 1, 2 or 3 camp-equivalent effect needs
  (estimate: about 140, 35 and 16 seeds; a 40-seed pilot measures it).
- **Evaluator** (`src/balance_model.rs`, bin `balance_eval`, inputs `tools/balance-lab/inputs.json` holding design
  intent only): a pure function of the lab report. Per quest: value, price, ratio and verdict against 0.85-1.25, the
  timer that meets the difficulty target (measured completion-time quantile), the reward value at ratio 1.0. Per
  augment: value over needed (solo, with one helper, with two) from measured growth, death rate and elite time.
- **Two read-only sim probes** are the only rh-sim additions: `Sim::gates_locked()` and a farm-stack total if none is
  public. No hash, wire or allocation impact.

## 3. Tuning seams (structure only; the content hash must not move)

- **Top 25 quests:** one table `TOP25` in a new `crates/rh-sim/src/quests/board/tuning.rs` holding every quest number
  now scattered through `attempts.rs`, `attempts/interactions.rs`, `attempts/targets.rs`, `attempts/signals.rs`,
  `rewards/awards.rs`, `rewards/effects.rs`, `rewards/permanent.rs`, `special/egg.rs`, `special/wall.rs`. Each
  `visit_rules` keeps its word order and reads the table, so the hash is provably unchanged. Not registry rows: the
  registry hashes names (`constants.rs:11-16`), so renaming would move the hash with no number changed. Route timers
  and counts stay in the `def!` rows (`board/catalog.rs:200-476`). New tests: a range per field with its reason, and
  `reward_text_numbers_match_tuning` (quest text states numbers in words and nothing checks them today).
- **Per-elite numbers:** `EliteTuning` (`mob_runtime.rs:440`) gains an empty per-kind table and `hp_mult_of(ty)` /
  `dmg_mult_of(ty)`; the four readers (`mob_runtime.rs:497`, `state.rs:1155`, `state.rs:1189`,
  `systems/sentenced.rs:543`) and the bots' estimates (`rh-policy` `planning.rs:182`, `hunt_adapter.rs:294`) use them.
  The table folds into the content hash only when non-empty.
- **Augments:** the seam exists (`REWORK_*` rows, `cards/rework_overrides.rs`, `cards/native/table.rs`). Added guard:
  an EditMode test comparing native card text in `CardDisplayCatalog.cs` with the Rust table (unguarded today).
- **Guide:** `docs/agent/BALANCE_LAB.md` (run, read, change one number, `scripts/regolden-content.sh`, re-run); fix
  the stale bot paragraph in `docs/agent/AUGMENT_REWORK.md:140-144`.

## 4. The balance patch (numbers and offer rules only)

Rules applied to the lab's table, so numbers are derived, not argued:

| Area | Rule |
|---|---|
| Quest timers | Each route timer meets its difficulty target: 0.95 easy, 0.85 medium, 0.70 hard chance to beat it. |
| Quest rewards | Move the reward's own knob (cooldown, charges, permanent size) until value / price is inside 0.85-1.25. |
| New augments | Move growth rate and cap until value / needed with one helper is at least 0.85; nothing above 1.25 solo. |
| Chest offers | Pool weights and faction offers only where a card is almost never or almost always taken. |
| Elites | Per-kind hp and damage until each elite is 10-20% for the reference fighter at `banked_12` (`camp_bench --elite`); then re-check bow and wand. |

- If bow stays outside the band after per-kind numbers, that needs a weapon factor on every mob-damage path
  (`point_damage` has no victim): a mechanic change, so it is reported as a proposal, not built.
- Anything else the rules cannot fix goes on a short redesign-proposals list for the owner.
- Each number packet proposes its DEVIATIONS row in `docs/coordination/REWORK_DEVIATIONS_PROPOSED.md` (DEV-061
  onward; unsigned until the owner signs). The hash moves through `scripts/regolden-content.sh` only; client
  (`BakedContent.cs`) and server go out together.

## Packets and waves

Per packet, as CLAUDE.md requires: brief in `docs/backlog/packets/<ID>.md`, one failing oracle on `packet/<ID>` from
`feature/unity-rework`, implementer on `agent/<model>/<ID>` in its own worktree, quick gate with `--oracle`, review,
merge, coordinator full gate in a clean detached worktree, `reviewer-opus` on every integration. At most three Rust
implementers at once. Shared files merge one at a time: `constants.rs`, `content.rs`, `tests/content.rs`,
`crates/rh-headless/Cargo.toml` and `src/lib.rs`, `raid/arbiter.rs`. The coordinator (Fable, named by the owner for
this work) writes the seam briefs and the value table's numbers, runs the lab and derives the patch; it does not
hand-tune or shepherd small changes.

| Wave | Packet | Outcome | Implementer / reviewer | Oracle (one failing test) |
|---|---|---|---|---|
| 1 | P-RAID-0 | RaidBot skeleton: policy, percept, refs, act queue, executor trait, arbiter with farm/roam/chest; stub files for the rest | Opus / reviewer-opus | In a human seat on a board world it clears a camp within 90 s; every command resolves to a handle of that frame |
| 1 | P-BAL-Q25-SEAM-1 | All Top 25 numbers in `TOP25`; hash unchanged | Opus / reviewer-opus | Source scan: no numeric `const` left in `quests/board/**` outside `tuning.rs` |
| 1 | P-BAL-LAB-1 | `balance_lab`, observation-based metrics | Sonnet / reviewer | Observer purity (final state hash equals an unobserved run) and `--jobs` byte identity |
| 2 | P-BAL-ELITE-SEAM-1 | `EliteTuning::*_of(ty)`, empty table; hash unchanged | Opus / reviewer-opus | Source scan: no `hp_mult`/`dmg_mult` read outside `mob_runtime.rs` |
| 2 | P-RAID-H1 | `BotKind::Raid`, human-seat option | Sonnet / reviewer | A Raid match on seed 1 seats `bot == false` and submits a `QuestAccept` |
| 2 | P-RAID-Q1 | Accept, walker, contact, carry, turn-in, slot choice, drop pickup | Sonnet / reviewer | Completes Watchtowers before its timer |
| 3 | P-BAL-PROBE-1 | `gates_locked()`, farm total, quest-reward grant in `trusted_build` | Opus / reviewer-opus | A granted reward appears among the seat's spells |
| 3 | P-RAID-V | The value table and loader | Sonnet / reviewer | Every reward and native has a row; the model's worked example reproduced within 10% |
| 3 | P-RAID-C | Combat and sustain adapter (fight, orbwalk, retreat, potions) | Sonnet / reviewer-opus | At 30% hp with a potion it drinks and disengages within 1 s |
| 4 | P-BAL-EVAL-1 | `balance_model`, `balance_eval`, `inputs.json` | Sonnet / reviewer | The workbook's worked example gives its ratio and timer |
| 4 | P-RAID-Q2 | Dwell quests | Sonnet / reviewer | Completes Stay A While unopposed |
| 4 | P-RAID-Q3 | Decisions (banners, Bargain, Trade Up, slot replacement) | Sonnet / reviewer | With full slots it drops the lower-value spell |
| 5 | P-RAID-E | Elite pricing, elite fight, chest pick | Opus / reviewer-opus | Kills a favourable elite and picks a chest card within 5 minutes |
| 5 | P-RAID-S | Quest spell cast rules (18 spells) | Sonnet / reviewer | Holding Cleanse while rooted, it casts within 500 ms |
| 5 | P-BAL-ELITE-BOTS-1 | Bots read per-kind elite numbers | Sonnet / reviewer | A doctored per-kind table changes `mob_max_hp` |
| — | **Pilot** | Coordinator: 40 seeds, lab + eval; first table; seed count fixed | — | — |
| 6 | P-BAL-LAB-2 | Paired mode, lock metrics, cast windows | Sonnet / reviewer | Granting a strong card to one seat gives a positive difference with its interval above 0 |
| 6 | P-RAID-Q4, P-RAID-W | Kill quests; Whispers search | Sonnet / reviewer | Completes Rime Hunt; finds three tokens inside the timer |
| 6 | P-RAID-P (after E) | PvP, event and coliseum goals | Opus / reviewer-opus | Completes Gatecrasher against two HumanBots |
| beside | P-BAL-NATIVE-TEXT-GUARD-1 (C#), P-BAL-DOC-1 (Haiku) | Text guard; guide | Sonnet, Haiku / reviewer | The new EditMode test; doc links |
| — | **Baseline** | Coordinator: full lab, paired runs, elite bench; numbers derived by the rules above | — | — |
| 7, one at a time | P-BAL-NUM-ELITE-1 | Per-kind elite numbers | Opus / reviewer-opus | `elite_tune` per-kind pins at the new values |
| 7 | P-BAL-NUM-Q25-1 | Timers, counts, reward numbers and their text | Sonnet / reviewer-opus | Range tests tightened to the evaluator's band |
| 7 | P-BAL-NUM-AUG-1 (+ C# text) | Native and override retunes, pool weights | Sonnet / reviewer-opus | `native_cards` and `rework_content_tests` pins at the new values |
| 7 | P-BAL-DEV-1 | Proposed DEVIATIONS rows | Haiku / reviewer | — |
| 8 | P-RAID-LIVE | `BotBrain::Raid` behind a flag, off by default | Opus / reviewer-opus | Flag set: a fill bot seats RaidBot with HumanBot fallback; unset: nothing changes |

About 26 packets: 9 Opus, 15 Sonnet, 2 Haiku.

## System improvements found by the Sonnet audit (reported; none started here)

The audit read the code and changed nothing. The backend is in good shape (sim step 0.1-3.3 ms mean against a 25 ms
tick; decode bounds-checked; edge limits, constant-time token checks and replication sound). Top items:

| # | Finding | Evidence | Note |
|---|---|---|---|
| 1 | **Input: the server order budget is tight against keyboard steering plus clicking.** Steering re-sends a Move every 100 ms (10 tokens/s) against 15/s, so kiting while attacking starts dropping orders after about 6 s; Aim at 40/s plus steering sits near the 60 frames/s disconnect limit. | `MatchClient.cs:36,500-506`, `input.rs:1146-1151`, `conn.rs:33` | Client-only fix. Effect inferred, not measured. |
| 2 | **Security: staging runs dev auth behind a proxy with no client-IP header**, so every client looks like 127.0.0.1 and the per-address caps and origin check never apply. | `hosting/staging-mvp/bootstrap.sh.in:114` | One line; `hosting/` needs owner approval. |
| 3 | **Efficiency: release profile sets no LTO, codegen-units or allocator.** | `Cargo.toml:17-18` | Never `panic = "abort"`; gate on the determinism CI. |
| 4 | **Input: snapshot arrival is timed after the frame drain**, about 8-16 ms of standing remote-render delay. | `RhWebSocket.jslib:23`, `Session.cs:554` | Client-only. |
| 5 | **Input: casts, pickups, swaps and potions show nothing until the server answers.** | `MatchClient.cs:444` | Presentation-only cues. |
| 6 | Security: no `cargo audit`/`cargo deny` in CI; images pinned by tag. | `.github/workflows`, Dockerfile | Protected paths. |
| 7 | Efficiency: room ticks share the tokio workers with the socket tasks. | `director/spawn.rs:201` | Measure first. |
| 8 | Efficiency: fixed Postgres pool of 16; `admit` has no timeout. | `rh-persist/src/lib.rs:217-229`, `conn.rs:190` | Small. |
| 9 | Efficiency: leaderboard rank scans the month per request; two missing indexes. | `rh-persist/src/account.rs:62-72` | Additive migration. |
| 10 | Render-clock window sized for 20 Hz; `Hello` derives `Debug` with the ticket in it. | `RenderClock.cs:17`, `rh-protocol/src/command.rs:157-171` | Trivial. |

Also noted: the roster's `bot` flag reaches every viewer (`observe/hud.rs:156`), so players can tell who is a bot.
These are offered as separate tasks after approval.

## Verification

- **Seams are neutral.** `scripts/regolden-content.sh --check` passes with no regold through waves 1-6;
  `cargo test -p rh-sim --test content --test elite_tune --test augment_rules --test hash_coverage --test no_alloc`
  and `cargo test -p rh-sim quests::board` stay green.
- **Nothing legacy moves.** Parity fixtures, the determinism sweep, `card_catalog`, the bot captures
  (`tools/bot-*-fixtures/capture.sh --check`) stay green on every merge; the hash tape's only moved line is `contentHash`.
- **The bot is a usable test player.** `balance_lab --seeds 1..65`: share of seats with stat stacks at minute 5, share
  of matches reaching the coliseum, quests completed per match, elites attempted, against the workbook's assumption
  (about 12 camps banked at minute 5) and against today's HumanBot.
- **The patch did what the rules say.** Lab and evaluator tables before and after, under
  `docs/evidence/balance-patch-2026-11/`: every quest and augment inside its band or on the proposals list;
  `camp_bench --elite --fighter both` before and after.
- **Live world holds.** `rework_at_25ms` (release), `examples/source_soak.rs` under `live_content`, mid-match
  checkpoints restore; `tests/balance_lab.rs` (release) keeps observer purity.
- **Gate.** `scripts/gate.sh --coordinator --keep-going` with pgtest in a clean detached worktree
  (`export PATH="$HOME/.cargo/bin:$PATH"`), EditMode and C# included; `reviewer-opus` on the combined tree.
- **A look.** A local match (launch entries, ports 7790/8080) with the flag on: RaidBots farm, fight an elite and
  pick an augment in the Web client.

## What this will not establish, and what stays the owner's

- Bots in human seats are not players. Human-seat rules also change kill accounting and remove Bounty Hunter's
  fill-bot fallback, so results describe "bots allowed to quest". The numbers are a measured starting point for
  playtests, not a proof of fun.
- Without reflexes the bot dodges poorly, so its death rates sit above a good player's. Quests it rarely completes
  (likely Whispers, Bounty Hunter, The Burning Crown) are reported "not measured", never as overtuned.
- The lab writes a report; a person edits `raid_values.json` in a reviewed packet. A tool that rewrites the bot's
  values from measurements comes close to what REQUIREMENTS R-ML excludes (`docs/REQUIREMENTS.md:115`), so it is not
  built without the owner.
- Live bots questing needs a hashed rh-sim seat change and the owner's word; not in this plan.
- A sim "dials index" (retunes inside a pinned range with no packet) would loosen the tuning lane's rule against
  `crates/`; proposed in the owner queue, not built.
- Asked before doing, per CLAUDE.md: any `git push` or pull request, anything under `hosting/`, `.github/`,
  `.claude/`, `scripts/gate.sh`, deleting branches. DEVIATIONS signatures are the owner's.
