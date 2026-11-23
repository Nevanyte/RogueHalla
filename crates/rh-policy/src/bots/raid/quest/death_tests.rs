//! P-RAID-Q3b-fix: the quest death table's range test, and the guard that the Trade Up patrol `decide.rs` documents is the
//! sim's (the way `spells.rs` guards its ranges: the sim's source is read and the lines are required, loud on a change).
use crate::bots::raid::values::Prices;
use rh_sim::quests::board::catalog::DEFINITIONS;

#[test]
fn the_quest_death_rows_are_in_range_unique_and_in_the_catalog() {
    let p = Prices::LOCAL;
    let rows = p.quest_death_rows;
    assert!(!rows.is_empty());
    for (i, (def, share)) in rows.iter().enumerate() {
        assert!(
            *share >= p.death_floor && *share <= 0.98,
            "{def}: {share} is outside death_floor..=0.98"
        );
        assert!(
            DEFINITIONS.iter().any(|d| d.id == *def),
            "{def} is not a quest of the catalog"
        );
        assert!(
            rows[..i].iter().all(|(d, _)| d != def),
            "{def} has two rows"
        );
    }
    assert!((p.quest_death_share("let_him_cook") - 7. / 189.).abs() < 1e-12);
    assert!((p.quest_death_share("hold_the_fort") - 9. / 32.).abs() < 1e-12);
    assert!((p.quest_death_share("two_banners") - p.death_floor).abs() < 1e-12);
    assert_eq!(Prices::farm_only().quest_death_rows, rows);
}

#[test]
fn the_patrol_decide_documents_is_the_sims() {
    const SIM: &str =
        include_str!("../../../../../rh-sim/src/quests/board/attempts/interactions.rs");
    const DECIDE: &str = include_str!("decide.rs");
    for line in [
        "let phase = ((now + 3000. * i as f64) % 12_000.) / 6000.;",
        "let fraction = 1. - (phase - 1.).abs();",
        "Vec2::new(72., 0.),",
    ] {
        assert!(SIM.contains(line), "interactions.rs no longer has `{line}`");
        assert!(DECIDE.contains(line), "decide.rs does not quote `{line}`");
    }
    assert!(DECIDE.contains("const PATROL_PERIOD_MS: f64 = 12_000.;"));
}
