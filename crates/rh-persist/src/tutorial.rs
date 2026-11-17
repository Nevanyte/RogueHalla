//! The first-login tutorial marker stored at `progress.tutorial` (inside `Progress::rest`, so no schema change).

use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum TutorialState {
    Step,
    Done,
    Skipped,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct Tutorial {
    pub version: u32,
    pub step: u32,
    pub state: TutorialState,
}

/// Parses a stored `progress.tutorial` value; anything malformed reads as absent.
pub fn parse(v: Option<&Value>) -> Option<Tutorial> {
    v.and_then(|v| Tutorial::deserialize(v).ok())
}

/// The value to keep after a write of `new` over `stored`: older versions are ignored, a newer version replaces, and for
/// the same version `done`/`skipped` are never undone by a `step` while a `step` only moves forward.
pub fn merge(stored: Option<Tutorial>, new: Tutorial) -> Tutorial {
    let Some(old) = stored else {
        return new;
    };
    if old.version != new.version {
        return if new.version > old.version { new } else { old };
    }
    match (old.state, new.state) {
        (TutorialState::Step, TutorialState::Step) => Tutorial {
            step: old.step.max(new.step),
            ..old
        },
        (TutorialState::Done | TutorialState::Skipped, TutorialState::Step) => old,
        _ => new,
    }
}
