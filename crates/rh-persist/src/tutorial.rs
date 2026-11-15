//! The first-login tutorial marker stored at `progress.tutorial` (inside `Progress::rest`, so no schema change).
//!
//! STUB for P-TUTORIAL-1: the types are real, `parse` and `merge` are placeholders the implementer replaces.

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
pub fn parse(_v: Option<&Value>) -> Option<Tutorial> {
    None
}

/// The value to keep after a write of `new` over `stored` (monotonic rules: see the packet).
pub fn merge(_stored: Option<Tutorial>, new: Tutorial) -> Tutorial {
    new
}
