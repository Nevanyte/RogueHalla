//! P-TUTORIAL-1 oracle: the monotonic merge and tolerant parse of the tutorial marker (pure, no database).

use rh_persist::tutorial::TutorialState::*;
use rh_persist::tutorial::{merge, parse, Tutorial, TutorialState};

fn t(version: u32, step: u32, state: TutorialState) -> Tutorial {
    Tutorial {
        version,
        step,
        state,
    }
}

#[test]
fn merge_is_monotonic() {
    assert_eq!(merge(None, t(1, 2, Step)), t(1, 2, Step));
    // step keeps the max
    assert_eq!(merge(Some(t(1, 5, Step)), t(1, 3, Step)), t(1, 5, Step));
    assert_eq!(merge(Some(t(1, 3, Step)), t(1, 5, Step)), t(1, 5, Step));
    // done/skipped are not undone by a step, and may overwrite each other or a step
    assert_eq!(merge(Some(t(1, 4, Done)), t(1, 9, Step)), t(1, 4, Done));
    assert_eq!(
        merge(Some(t(1, 4, Skipped)), t(1, 9, Step)),
        t(1, 4, Skipped)
    );
    assert_eq!(merge(Some(t(1, 4, Skipped)), t(1, 4, Done)), t(1, 4, Done));
    assert_eq!(
        merge(Some(t(1, 4, Done)), t(1, 2, Skipped)),
        t(1, 2, Skipped)
    );
    assert_eq!(merge(Some(t(1, 4, Step)), t(1, 2, Done)), t(1, 2, Done));
    // versions: an older version is ignored, a higher one replaces
    assert_eq!(merge(Some(t(2, 1, Step)), t(1, 9, Done)), t(2, 1, Step));
    assert_eq!(merge(Some(t(1, 4, Done)), t(2, 0, Step)), t(2, 0, Step));
}

#[test]
fn parse_tolerates_garbage() {
    assert_eq!(parse(None), None);
    assert_eq!(parse(Some(&serde_json::json!("x"))), None);
    assert_eq!(parse(Some(&serde_json::json!({"version":1}))), None);
    assert_eq!(
        parse(Some(
            &serde_json::json!({"version":1,"step":2,"state":"nope"})
        )),
        None
    );
    assert_eq!(
        parse(Some(
            &serde_json::json!({"version":1,"step":2,"state":"done"})
        )),
        Some(t(1, 2, Done))
    );
}
