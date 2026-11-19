//! P-AUG-POOL-1-fix oracle: the chest-offer inputs of both real card tables (the pool ids, the faction-only pairs and
//! the Eye for an Eye weighting flag) are part of the rules identity. Perturbing any of them through the seam
//! `content_hash_of_chest_pools` moves `content_hash`, and the seam over the real tables is `content_hash` itself.
//! The digest moves with the fix; what is pinned for Legacy is the fold's inputs (the pool, no pair, no weighting).

use rh_sim::cards::{CardId, CardKind, Tuning};
use rh_sim::constants::REGISTRY;
use rh_sim::content::{
    chest_pool_tables, content_hash, content_hash_of_chest_pools, ChestPoolTable, STAGGER_WEAPONS,
};
use rh_sim::reward_chests::{Offer, INTERACTIVE, REWORK_POOL};
use rh_sim::{AugmentRule, FactionId, SimConfig};
use std::collections::BTreeMap;

fn hash(tables: &[ChestPoolTable<'_>]) -> u64 {
    content_hash_of_chest_pools(REGISTRY, STAGGER_WEAPONS, tables)
}

fn config(augments: AugmentRule) -> SimConfig {
    SimConfig {
        augments,
        ..SimConfig::default()
    }
}

/// A live augment: what `Tuning::augment_pool` admits from a pool (`kind == Augment && ready`).
fn live_augment(id: CardId) -> bool {
    let d = id.definition();
    d.kind == CardKind::Augment && d.ready
}

/// Records one perturbation: it must move the hash off `base` and differ from every earlier perturbation.
fn record(seen: &mut BTreeMap<u64, String>, base: u64, h: u64, what: String) {
    assert_ne!(
        h, base,
        "{what} does not move content_hash (the chest pool is outside the rules identity)"
    );
    if let Some(earlier) = seen.insert(h, what.clone()) {
        panic!("{what} collides with {earlier}");
    }
}

#[test]
fn the_folded_chest_inputs_are_the_real_tables_and_legacy_is_todays() {
    let [legacy, rework] = chest_pool_tables();

    // The fold reads each rule's own table, the one a world with that rule plays.
    let legacy_world = Tuning::of(&SimConfig::default());
    assert_eq!(legacy_world.rule(), AugmentRule::Legacy);
    assert_eq!(legacy, ChestPoolTable::of(legacy_world));
    let live = Tuning::of(&SimConfig::live_content());
    assert_eq!(live.rule(), AugmentRule::Rework);
    assert_eq!(rework, ChestPoolTable::of(live));
    assert_eq!(
        rework,
        ChestPoolTable::of(Tuning::of(&config(AugmentRule::Rework)))
    );

    // Legacy: today's inputs exactly (the source INTERACTIVE_AUGMENT_IDS in source order, no pair, no weighting) ...
    assert_eq!(legacy.rule, AugmentRule::Legacy);
    assert_eq!(legacy.pool, &INTERACTIVE[..]);
    assert!(legacy.faction_only.is_empty());
    assert!(!legacy.weight_eye_for_an_eye);
    // ... and they are what a Legacy chest reads: every one of the 14 is offered to anyone, nothing else is offered.
    for id in CardId::ALL {
        let expected = INTERACTIVE.contains(&id).then_some(Offer::Any);
        assert_eq!(legacy_world.offer(id), expected, "Legacy offer of {id:?}");
    }

    // Rework: its own pool, its pairs (none until P-AUG-NATIVE-4), the weighting on.
    assert_eq!(rework.rule, AugmentRule::Rework);
    assert_eq!(rework.pool, REWORK_POOL);
    assert_eq!(rework.faction_only, live.faction_only());
    assert!(rework.weight_eye_for_an_eye);
}

#[test]
fn the_seam_with_the_real_tables_is_the_content_hash() {
    assert_eq!(hash(&chest_pool_tables()), content_hash());
}

#[test]
fn adding_or_removing_one_pool_card_moves_the_content_hash() {
    let real = chest_pool_tables();
    let base = content_hash();
    let mut seen = BTreeMap::new();
    let mut added_per_table = [0usize; 2];
    for t in 0..real.len() {
        let pool = real[t].pool;
        for i in 0..pool.len() {
            let mut fewer = pool.to_vec();
            let gone = fewer.remove(i);
            let mut tables = real;
            tables[t].pool = &fewer;
            record(
                &mut seen,
                base,
                hash(&tables),
                format!("removing {gone:?} from the {:?} pool", real[t].rule),
            );
        }
        for id in CardId::ALL {
            if pool.contains(&id) || !live_augment(id) {
                continue;
            }
            let mut more = pool.to_vec();
            more.push(id);
            let mut tables = real;
            tables[t].pool = &more;
            record(
                &mut seen,
                base,
                hash(&tables),
                format!("adding {id:?} to the {:?} pool", real[t].rule),
            );
            added_per_table[t] += 1;
        }
    }
    // Non-vacuity: the five Rework additions are live augments outside the Legacy pool; Glass Cannon is outside both.
    assert!(
        added_per_table[0] >= 6,
        "only {} additions tried on the Legacy pool",
        added_per_table[0]
    );
    assert!(
        added_per_table[1] >= 1,
        "no addition tried on the Rework pool"
    );
}

#[test]
fn adding_or_changing_a_faction_pair_moves_the_content_hash() {
    let real = chest_pool_tables();
    let base = content_hash();
    let mut seen = BTreeMap::new();
    for t in 0..real.len() {
        let pool = real[t].pool;
        let (first, second) = (pool[0], pool[1]);
        for faction in FactionId::ALL {
            let mut pairs = real[t].faction_only.to_vec();
            pairs.push((first, faction));
            let mut tables = real;
            tables[t].faction_only = &pairs;
            record(
                &mut seen,
                base,
                hash(&tables),
                format!(
                    "making {first:?} {faction:?}-only in the {:?} table",
                    real[t].rule
                ),
            );
        }
        let mut pairs = real[t].faction_only.to_vec();
        pairs.push((first, FactionId::ALL[0]));
        pairs.push((second, FactionId::ALL[1]));
        let mut tables = real;
        tables[t].faction_only = &pairs;
        record(
            &mut seen,
            base,
            hash(&tables),
            format!("two faction pairs in the {:?} table", real[t].rule),
        );
    }
}

#[test]
fn flipping_the_eye_for_an_eye_weight_flag_moves_the_content_hash() {
    let real = chest_pool_tables();
    let base = content_hash();
    let mut seen = BTreeMap::new();
    for t in 0..real.len() {
        let mut tables = real;
        tables[t].weight_eye_for_an_eye = !tables[t].weight_eye_for_an_eye;
        record(
            &mut seen,
            base,
            hash(&tables),
            format!(
                "flipping the {:?} table's Eye for an Eye weighting to {}",
                real[t].rule, tables[t].weight_eye_for_an_eye
            ),
        );
    }
}

#[test]
fn which_rule_holds_which_table_is_folded() {
    let real = chest_pool_tables();
    let base = content_hash();
    let mut seen = BTreeMap::new();
    let swapped = [real[1], real[0]];
    record(
        &mut seen,
        base,
        hash(&swapped),
        "swapping the Legacy and Rework tables".to_string(),
    );
    let mut legacy_plays_rework_pool = real;
    legacy_plays_rework_pool[0].pool = real[1].pool;
    record(
        &mut seen,
        base,
        hash(&legacy_plays_rework_pool),
        "giving Legacy the Rework pool".to_string(),
    );
}
