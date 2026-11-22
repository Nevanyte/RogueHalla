//! RaidBot: the balance lab's test player (docs/backlog/BALANCE_LAB_PATCH.md section 1). A third bot policy that reads
//! only accepted `Frame14`, chooses goals with one arbiter scored in camp-equivalents per minute, and acts through the
//! Humanizer and its own paced queue of public commands. HumanBot and BrainBot are untouched. Seeded streams: tags 0-3
//! as HumanBot for the shared modules, 16 for the arbiter and 17 for the act queue.
pub mod acts;
pub mod arbiter;
pub mod bot;
/// The goal modules. A later packet extends the bot in its own file plus one line in `exec::registry`:
/// - **A goal kind**: every kind the plan names is already a `goal::GoalKind`; implement `goal::Executor` for it
///   (`kinds()`, `act`), register it, done. A kind the plan did not name adds one `GoalKind` line too.
/// - **A candidate source**: `Executor::propose` pushes `goal::Candidate`s priced in camp-equivalents (one normal camp
///   = 12 stat stacks = 1): `value`, `success`, `death`, `loss` (what dying costs), `minutes`. Never filter by score:
///   the arbiter ranks every candidate of every executor together.
/// - **Something that rides along any goal** (a potion, a spell, a chest pick): `Executor::ride_along`.
/// - **A new public command** (quest accept or interact, a potion, a trade): return an `acts::Act`; the queue paces it
///   and drops it when the frame no longer offers it.
/// - **A fight**: return `Step::committed` while it is on, so the arbiter does not walk away from members that follow.
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
pub mod spells;
pub mod values;

pub use bot::RaidBot;
