//! RaidBot: the balance lab's test player (docs/backlog/BALANCE_LAB_PATCH.md section 1). A third bot policy that reads
//! only accepted `Frame14`, chooses goals with one arbiter scored in camp-equivalents per minute, and acts through the
//! Humanizer and its own paced queue of public commands. HumanBot and BrainBot are untouched. Seeded streams: tags 0-3
//! as HumanBot for the shared modules, 16 for the arbiter and 17 for the act queue.
pub mod acts;
pub mod arbiter;
pub mod bot;
pub mod exec;
pub mod goal;
pub mod percept;
pub mod quest;
pub mod refs;
pub mod spells;
pub mod values;

pub use bot::RaidBot;
