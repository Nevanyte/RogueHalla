//! RaidBot: the balance lab's test player (docs/backlog/BALANCE_LAB_PATCH.md section 1). A third bot policy that reads
//! only accepted `Frame14`, chooses goals with one arbiter scored in camp-equivalents per minute, and acts through the
//! Humanizer and its own paced queue of public commands. HumanBot and BrainBot are untouched. Seeded streams: tags 0-3
//! as HumanBot for the shared modules, 16 for the arbiter and 17 for the act queue.
pub mod acts;
pub mod arbiter;
pub mod bot;
/// The goal modules. A later packet touches exactly these shared places, and nothing else outside its own file:
/// 1. **Its own file**, already declared: `exec/{combat,elite,sustain}.rs` or `quest/{walker,dwell,decide,hunt,pvp,
///    search}.rs`, implementing `goal::Executor` (`kinds()`, `act`, and the hooks below it needs). A goal file the plan
///    did not name adds its `pub mod` line to `exec/mod.rs` or `quest/mod.rs`.
/// 2. **One line in `exec::registry`** (`exec/mod.rs`) boxing its executor. The registry order is behaviour.
/// 3. **A `GoalKind` variant in `goal.rs`** only for a kind the plan did not name: every kind it names already exists.
///
/// `Executor`, the arbiter, `Cx` and `bot.rs` need no edit; prices are fields of `values::Prices` (P-RAID-V fills them
/// there). The hooks:
/// - **A candidate source**: `Executor::propose` pushes `goal::Candidate`s priced in camp-equivalents (one normal camp
///   = 12 stat stacks = 1): `value`, `success`, `death`, `loss` (what dying costs), `minutes`, every number read from
///   `cx.prices`. A quest candidate's value is `cx.prices.reward_value(def)` times what the stage costs or adds, an
///   augment's `cx.prices.augment_value(card)`: never a reward or augment number of its own, so a table that prices
///   them at zero (`Prices::farm_only`) turns the whole family off. Never filter by score: the arbiter ranks every
///   candidate of every executor together.
/// - **Something that rides along any goal** (a potion, a spell, a chest pick): `Executor::ride_along`.
/// - **A new public command** (quest accept or interact, a potion, a trade): return an `acts::Act`; the queue paces it
///   and drops it when the frame no longer offers it.
/// - **A fight**: return `Step::committed` while it is on, so the arbiter does not walk away from members that follow.
///   Only a kind whose job is to leave or end the fight pre-empts that (`GoalKind::preempts_commitment`: Sustain).
/// - **A decision that cannot wait for the cadence** (hp falling fast, a fight turning): `Executor::urgent` returns
///   true, and the arbiter decides and the goal thinks on this frame (otherwise strategy ticks come on the tier's
///   cadence, 1.2 to 2 s apart on average).
pub mod exec;
#[cfg(test)]
mod fix_tests;
pub mod goal;
pub mod percept;
/// Quest stages (P-RAID-Q1..Q4, -P, -W): each file is an executor of its `GoalKind::Quest*`, registered in
/// `exec::registry`; it names its quest by `goal::Target::Quest { def, stage }` (the row's definition id, never its
/// handle) and sends accept and interact as `acts::Act`s.
pub mod quest;
pub mod refs;
#[cfg(test)]
mod review_tests;
pub mod spells;
pub mod values;

pub use bot::RaidBot;
