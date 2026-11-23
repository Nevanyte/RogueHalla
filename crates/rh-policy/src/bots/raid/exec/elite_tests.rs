//! Unit tests of the Elite executor's pure parts (P-RAID-E): the bench row read between its columns, the death prior's
//! clamps and fallbacks, and the priced pick's filters.
use super::*;
use crate::bots::raid::values::Prices;

#[test]
fn the_bench_row_is_read_between_its_columns_and_held_at_the_ends() {
    let cols = [0., 6., 12., 18.];
    let row = [0.8, 0.4, 0.2, 0.1];
    assert_eq!(row_at(&cols, &row, -1.), 0.8);
    assert_eq!(row_at(&cols, &row, 0.), 0.8);
    assert!((row_at(&cols, &row, 3.) - 0.6).abs() < 1e-12);
    assert!((row_at(&cols, &row, 9.) - 0.3).abs() < 1e-12);
    assert_eq!(row_at(&cols, &row, 18.), 0.1);
    assert_eq!(row_at(&cols, &row, 40.), 0.1);
}

#[test]
fn the_death_prior_clamps_and_reads_the_top_for_an_unknown_kind() {
    let e = Prices::LOCAL.elite;
    let spc = Prices::LOCAL.stacks_per_camp;
    // No hp held: the top of the range, never infinite.
    let none = death_prior(
        &e,
        MobTypeId::Executioner,
        Some(WeaponId::Sword),
        36.,
        spc,
        0.,
    );
    assert_eq!(none, e.death_range.1);
    // Lots held: the floor.
    let lots = death_prior(&e, MobTypeId::BloodSaint, Some(WeaponId::Axe), 600., spc, 50.);
    assert_eq!(lots, e.death_range.0);
    // A kind with no row in the table (OFF has none): the top of its range.
    let off = ElitePrices::OFF;
    assert_eq!(
        death_prior(
            &off,
            MobTypeId::Executioner,
            Some(WeaponId::Sword),
            36.,
            spc,
            1.
        ),
        off.death_range.1
    );
    // No weapon in hand: factor 1.
    let bare = death_prior(&e, MobTypeId::Archon, None, 0., spc, 1.);
    assert!((bare - 0.5).abs() < 1e-12, "{bare}");
}

#[test]
fn the_priced_pick_skips_empty_ids_and_zero_worth() {
    let p = Prices::LOCAL;
    let free = vec![String::new(); 3];
    let o = |a: &str, b: &str, c: &str| vec![a.to_owned(), b.to_owned(), c.to_owned()];
    // raceToZero is worth 0: never the priced pick, so Loot's own pick (or skip) stands.
    assert_eq!(
        best_card(&p, &o("raceToZero", "", "raceToZero"), &free, &|_| false),
        None
    );
    // A shorter taken list leaves the rest free.
    assert_eq!(
        best_card(&p, &o("slowBurn", "markedMan", "tankEngine"), &[], &|_| false),
        Some(1)
    );
}
