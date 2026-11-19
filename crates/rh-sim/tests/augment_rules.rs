//! P-AUG-RULES-1 oracle (integration half; the in-crate half is `src/cards/tuning_tests.rs`).
//!
//! The elite and augment rework (owner direction 2026-11-18, `docs/backlog/AUGMENT_ELITE_REWORK.md`) is live-only behind
//! two `SimConfig` rules. This packet adds the rules and the seams with EMPTY rework tables, so its proof is:
//! 1. the rules' encoding: `Legacy` by default (writes nothing, folds nothing), `Rework` in the live world, `"rework"` in
//!    JSON, and a statics identity of its own (no collision with any other config tag);
//! 2. the seams answer legacy under both rules (`cards::Tuning`, `mob_runtime::EliteTuning`);
//! 3. equivalence: the same match under `Legacy` and under `Rework` plays out the same (events and world), and differs
//!    only in the config and the digest;
//! 4. a `Rework` checkpoint holding fold cards with a ranged packet in flight restores and plays on identically.
use rh_sim::arenas::practice_arena;
use rh_sim::cards::{CardId, Tuning};
use rh_sim::mob_runtime::{EliteTuning, MobMode};
use rh_sim::reward_chests::{augment_pool, conditional_pool, INTERACTIVE};
use rh_sim::{
    validate, AugmentRule, Command, EliteRule, Event, FactionId, Placement, PlayerId, RawCommand,
    Secrets, Sim, SimConfig, Vec2, WeaponId,
};
use serde_json::{json, Value};
use std::collections::BTreeMap;

fn cmd(raw: RawCommand) -> Command {
    validate(raw).expect("a valid test command")
}

fn with_rules(mut config: SimConfig, augments: AugmentRule, elites: EliteRule) -> SimConfig {
    config.augments = augments;
    config.elites = elites;
    config
}

// ---------------------------------------------------------------------------------------------------------------------
// 1. The rules' encoding.
// ---------------------------------------------------------------------------------------------------------------------

#[test]
fn both_rules_default_to_legacy_and_the_live_world_selects_rework() {
    let default = SimConfig::default();
    assert_eq!(default.augments, AugmentRule::Legacy);
    assert_eq!(default.elites, EliteRule::Legacy);
    assert_eq!(AugmentRule::default(), AugmentRule::Legacy);
    assert_eq!(EliteRule::default(), EliteRule::Legacy);
    let live = SimConfig::live_content();
    assert_eq!(
        live.augments,
        AugmentRule::Rework,
        "live_content() plays the augment rework"
    );
    assert_eq!(
        live.elites,
        EliteRule::Rework,
        "live_content() plays the elite rework"
    );
}

#[test]
fn rework_is_the_string_rework_in_json_and_legacy_writes_nothing() {
    let default = serde_json::to_value(SimConfig::default()).unwrap();
    assert_eq!(default["augments"], Value::Null, "Legacy writes nothing");
    assert_eq!(default["elites"], Value::Null, "Legacy writes nothing");
    let rework = with_rules(SimConfig::default(), AugmentRule::Rework, EliteRule::Rework);
    let v = serde_json::to_value(&rework).unwrap();
    assert_eq!(v["augments"], json!("rework"));
    assert_eq!(v["elites"], json!("rework"));
    let back: SimConfig = serde_json::from_value(v.clone()).unwrap();
    assert_eq!(back, rework, "the JSON round trip keeps both rules");
    // Each rule alone round-trips (the two fields are independent).
    for (a, e) in [
        (AugmentRule::Rework, EliteRule::Legacy),
        (AugmentRule::Legacy, EliteRule::Rework),
    ] {
        let c = with_rules(SimConfig::default(), a, e);
        let back: SimConfig = serde_json::from_value(serde_json::to_value(&c).unwrap()).unwrap();
        assert_eq!(back, c);
    }
    // An absent field reads as Legacy (checkpoints from before the fields restore unchanged).
    let mut old = default.clone();
    old.as_object_mut().unwrap().remove("augments");
    old.as_object_mut().unwrap().remove("elites");
    let back: SimConfig = serde_json::from_value(old).unwrap();
    assert_eq!(back, SimConfig::default());
    // JSON refuses anything else, the hasher's tags and other fields' values included.
    for bad in [
        json!("Rework"),
        json!("legacy"),
        json!("themed"),
        json!("wall"),
        json!("augments=rework"),
        json!("elites=rework"),
        json!(1),
    ] {
        for field in ["augments", "elites"] {
            let mut c = default.clone();
            c[field] = bad.clone();
            assert!(
                serde_json::from_value::<SimConfig>(c).is_err(),
                "{field} = {bad} was admitted"
            );
        }
    }
}

/// The base of the identity test: a default world stepped once. Only its statics config changes below.
fn identity_base() -> Value {
    let mut sim = Sim::reset_active(
        SimConfig::default(),
        practice_arena(),
        Secrets::from_seed(81),
        &[Placement {
            profile: "aug-rules-identity".into(),
            faction: FactionId::Holy,
            bot: false,
            at: Vec2::new(400., 320.),
            hp: None,
            weapon0: None,
            weapon1: None,
        }],
    );
    sim.step();
    serde_json::to_value(&sim).unwrap()
}

/// The hasher folds values, not field names (`statics.rs`, `RegenInCombat`): every one of the six config switches whose
/// default folds nothing must give its non-default value a word of its own, or two different worlds share a digest.
/// All 64 combinations of the six, on one fixed world, are 64 different state hashes.
#[test]
fn every_combination_of_the_silent_config_switches_is_its_own_statics_identity() {
    use rh_sim::archetype::MobSpacing;
    use rh_sim::{CampBuffRule, FrostWish, RegenInCombat};
    let base = identity_base();
    let switches: [(&str, Value, Value); 6] = [
        (
            "mob_spacing",
            serde_json::to_value(MobSpacing::Auto).unwrap(),
            serde_json::to_value(MobSpacing::Off).unwrap(),
        ),
        (
            "regen_in_combat",
            serde_json::to_value(RegenInCombat::Legacy).unwrap(),
            serde_json::to_value(RegenInCombat::Off).unwrap(),
        ),
        (
            "camp_buffs",
            serde_json::to_value(CampBuffRule::Legacy).unwrap(),
            serde_json::to_value(CampBuffRule::Themed).unwrap(),
        ),
        (
            "frost_wish",
            serde_json::to_value(FrostWish::Freeze).unwrap(),
            serde_json::to_value(FrostWish::Wall).unwrap(),
        ),
        (
            "augments",
            serde_json::to_value(AugmentRule::Legacy).unwrap(),
            serde_json::to_value(AugmentRule::Rework).unwrap(),
        ),
        (
            "elites",
            serde_json::to_value(EliteRule::Legacy).unwrap(),
            serde_json::to_value(EliteRule::Rework).unwrap(),
        ),
    ];
    let mut seen: BTreeMap<u64, String> = BTreeMap::new();
    for mask in 0u32..64 {
        let mut cp = base.clone();
        let mut name = Vec::new();
        for (bit, (field, off, on)) in switches.iter().enumerate() {
            let set = mask & (1 << bit) != 0;
            cp["statics"]["config"][*field] = if set { on.clone() } else { off.clone() };
            if set {
                name.push(*field);
            }
        }
        let name = format!("non-default: {name:?}");
        let sim = Sim::restore(cp).unwrap_or_else(|e| panic!("{name}: {e:?}"));
        assert_eq!(
            serde_json::to_value(&sim).unwrap()["world"],
            base["world"],
            "{name}: only the config changed"
        );
        let h = sim.state_hash();
        if let Some(other) = seen.insert(h, name.clone()) {
            panic!("`{other}` and `{name}` share the statics digest (state hash {h:#018x})");
        }
    }
    assert_eq!(seen.len(), 64);
    // The two defaults fold nothing: the all-default restore is the base world's own hash.
    let unchanged = Sim::restore(base.clone()).unwrap().state_hash();
    assert_eq!(
        seen.get(&unchanged).map(String::as_str),
        Some("non-default: []")
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// 2. The seams answer legacy under both rules.
// ---------------------------------------------------------------------------------------------------------------------

fn base_build(cards: &[CardId]) -> rh_sim::cards::Build {
    rh_sim::cards::Build::from_counts(cards.iter().map(|c| (*c, 1)))
}

#[test]
fn the_card_table_is_the_legacy_catalog_under_both_rules() {
    use rh_sim::constants as c;
    let configs = [
        SimConfig::default(),
        with_rules(SimConfig::default(), AugmentRule::Rework, EliteRule::Legacy),
        SimConfig::live_content(),
    ];
    let fold = rh_sim::stat_fold::default_definition();
    let pickers: Vec<Vec<rh_sim::cards::Build>> = vec![
        vec![],
        vec![base_build(&[])],
        vec![base_build(&[CardId::SlowBurn, CardId::Ad1])],
        vec![
            base_build(&[CardId::SlowBurn, CardId::Ap2, CardId::Gambler]),
            base_build(&[CardId::SlowBurn, CardId::Armor1, CardId::Haste3]),
        ],
        vec![base_build(&INTERACTIVE)],
    ];
    for config in &configs {
        let t = Tuning::of(config);
        let rule = config.augments;
        assert_eq!(t.rule(), rule, "the table knows its rule");
        for id in CardId::ALL {
            assert_eq!(
                t.def(id),
                id.definition(),
                "{rule:?} {id:?}: empty overrides are the catalog"
            );
        }
        let n = t.numbers();
        assert_eq!(n.plant_feet_bonus, c::PLANT_FEET_BONUS, "{rule:?}");
        assert_eq!(n.gambler_up_chance, c::GAMBLER_CHANCE, "{rule:?}");
        assert_eq!(n.gambler_down_chance, c::GAMBLER_CHANCE, "{rule:?}");
        assert_eq!(n.gambler_up, c::GAMBLER_UP, "{rule:?}");
        assert_eq!(n.gambler_down, c::GAMBLER_DOWN, "{rule:?}");
        assert_eq!(n.wishful_budget_ms, c::WISHFUL_BUDGET_MS, "{rule:?}");
        assert_eq!(n.wishful_window_ms, c::WISHFUL_WINDOW_MS, "{rule:?}");
        assert_eq!(n.wishful_floor_share, c::WISHFUL_FLOOR_SHARE, "{rule:?}");
        assert_eq!(n.outgoing_row_cap, c::OUTGOING_ROW_CAP, "{rule:?}");
        assert_eq!(
            n.outgoing_row_cap, fold.outgoing_row_cap,
            "{rule:?}: the fold's G1 clamp"
        );
        assert_eq!(n.hp_race_cap, c::HP_RACE_CAP, "{rule:?}");
        assert_eq!(
            n.hp_race_cap, fold.hp_race_cap,
            "{rule:?}: the fold's G4 clamp"
        );
        assert_eq!(n.third_time_every, 3, "{rule:?}");
        assert_eq!(n.third_time_extra, 1., "{rule:?}");
        assert_eq!(t.pool(), &INTERACTIVE[..], "{rule:?}");
        for p in &pickers {
            assert_eq!(t.augment_pool(p), augment_pool(p), "{rule:?} {p:?}");
            assert_eq!(t.conditional_pool(p), conditional_pool(p), "{rule:?} {p:?}");
        }
    }
}

#[test]
fn the_elite_table_is_the_legacy_constants_under_both_rules() {
    use rh_sim::constants as c;
    for rule in [EliteRule::Legacy, EliteRule::Rework] {
        let e = EliteTuning::of(rule);
        assert_eq!(e.k, c::ELITE_K, "{rule:?}");
        assert_eq!(e.hp_mult, c::ELITE_HP_MULT, "{rule:?}");
        assert_eq!(e.dmg_mult, c::ELITE_DMG_MULT, "{rule:?}");
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// 3. Equivalence: the same match under both rules.
// ---------------------------------------------------------------------------------------------------------------------

/// The one-camp PvE fixture (an elite camp, `fire-1`); the players stand inside the camp's room.
fn pve_arena() -> rh_sim::Arena {
    let data = serde_json::from_str(include_str!("fixtures/mob-pve-map.json")).unwrap();
    rh_sim::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: 0,
        map: data,
    }
    .validate()
    .unwrap()
    .into_arena()
    .unwrap()
}

fn pve_config() -> SimConfig {
    SimConfig {
        quests: false,
        consumables: true,
        mob_mode: MobMode::Pve,
        ..Default::default()
    }
}

fn placements() -> Vec<Placement> {
    vec![
        Placement {
            profile: "aug-rules-bow".into(),
            faction: FactionId::Fire,
            bot: false,
            at: Vec2::new(200., 200.),
            hp: None,
            weapon0: Some(WeaponId::Bow),
            weapon1: None,
        },
        Placement {
            profile: "aug-rules-sword".into(),
            faction: FactionId::Holy,
            bot: false,
            at: Vec2::new(236., 176.),
            hp: None,
            weapon0: Some(WeaponId::Sword),
            weapon1: None,
        },
    ]
}

/// Grants `cards` to `pid` through the real door: one declared chest per card at the holder's feet (as
/// `hash_coverage.rs` `card_choices` does), each taken with `Choose` (the grant re-folds the build). Returns the events.
fn grant_by_chests(sim: Sim, pid: PlayerId, cards: &[CardId]) -> (Sim, Vec<Vec<Event>>) {
    let mut sim = sim;
    let mut out = Vec::new();
    for card in cards {
        let mut cp = serde_json::to_value(&sim).unwrap();
        let chest_id = u32::try_from(cp["world"]["entity_counter"].as_u64().unwrap()).unwrap() + 1;
        let at = {
            let p = sim.player(pid).unwrap();
            json!({"x": p.body.x, "y": p.body.y})
        };
        let chest = json!({
            "id": chest_id, "at": at,
            "expires_at": sim.server_time() + rh_sim::reward_chests::DEFINITION.lifetime_ms,
            "held_until_tick": 0, "options": [{"Card": card.as_str()}, null, null],
            "taken": [null, null, null], "pickers": [], "camp": null, "owner": null
        });
        cp["world"]["reward_chests"]
            .as_array_mut()
            .unwrap()
            .push(chest);
        cp["world"]["entity_counter"] = chest_id.into();
        sim = Sim::restore(cp).expect("a declared chest restores");
        sim.submit(
            pid,
            cmd(RawCommand::Choose {
                chest_id: Some(chest_id),
                pick: 0,
            }),
        )
        .unwrap();
        let events = sim.step();
        assert!(
            events
                .iter()
                .any(|e| matches!(e, Event::ChestTaken { chest, by, pick: 0 } if *chest == chest_id && *by == pid)),
            "{card:?} was not granted"
        );
        assert!(sim.player(pid).unwrap().build.owns(*card), "{card:?}");
        out.push(events);
    }
    (sim, out)
}

/// The nearest standing mob's angle from a player, if any.
fn aim(sim: &Sim, pid: PlayerId) -> Option<f64> {
    let me = sim.player(pid)?.pos();
    sim.mobs()
        .iter()
        .filter(|m| m.core.vitals.hp > 0.)
        .map(|m| {
            let at = m.core.pos();
            let (dx, dy) = (at.x - me.x, at.y - me.y);
            (dx * dx + dy * dy, rh_sim::math::atan2(dy, dx))
        })
        .min_by(|a, b| a.0.total_cmp(&b.0))
        .map(|(_, a)| a)
}

/// One scripted tick: both players aim at their nearest mob, attack every 7 ticks and release 3 ticks later.
fn drive_tick(sim: &mut Sim, t: u32) -> Vec<Event> {
    for pid in [PlayerId(1), PlayerId(2)] {
        if let Some(a) = aim(sim, pid) {
            let _ = sim.submit(pid, cmd(RawCommand::Aim { a }));
            if t % 7 == 0 {
                let _ = sim.submit(pid, cmd(RawCommand::Attack { a, t: None }));
            }
            if t % 7 == 3 {
                let _ = sim.submit(pid, cmd(RawCommand::Release { a, t: None }));
            }
        }
    }
    sim.step()
}

/// The checkpoint with the two rule fields taken out of its config (everything else is compared).
fn without_rules(sim: &Sim) -> Value {
    let mut v = serde_json::to_value(sim).unwrap();
    let config = v["statics"]["config"].as_object_mut().unwrap();
    config.remove("augments");
    config.remove("elites");
    v
}

const FOLD_CARDS: [CardId; 6] = [
    CardId::Committed,
    CardId::MachineGun,
    CardId::Gambler,
    CardId::ArcaneOverload,
    CardId::PlantYourFeet,
    CardId::ThirdTimesTheCharm,
];

/// Equivalence over a seed sweep: the same seed and the same inputs under `Legacy` and under `Rework` (both rules)
/// give the same events on every tick and the same world, with the empty rework tables; only the config and therefore
/// the digest (so every state hash) differ. The run grants fold cards (Committed, Machine Gun, Arcane Overload) and
/// coin/streak/stillness cards (Gambler, Third Time, Plant Your Feet) to the bow holder and Here Comes the Boom to the
/// sword holder, then fights the elite camp. Floors (not on every seed): the elite is hurt on at least one seed.
#[test]
fn legacy_and_empty_rework_play_the_same_match() {
    const TICKS: u32 = 240;
    let mut elite_hurt_seeds = 0;
    for seed in [11u64, 12, 13] {
        let mut sims = [AugmentRule::Legacy, AugmentRule::Rework].map(|a| {
            let rule = if a == AugmentRule::Legacy {
                EliteRule::Legacy
            } else {
                EliteRule::Rework
            };
            let sim = Sim::reset_active(
                with_rules(pve_config(), a, rule),
                pve_arena(),
                Secrets::from_seed(seed),
                &placements(),
            );
            let (sim, e1) = grant_by_chests(sim, PlayerId(1), &FOLD_CARDS);
            let (sim, e2) = grant_by_chests(sim, PlayerId(2), &[CardId::HereComesTheBoom]);
            (sim, e1, e2)
        });
        assert_eq!(
            sims[0].1, sims[1].1,
            "seed {seed}: the bow holder's grant events"
        );
        assert_eq!(
            sims[0].2, sims[1].2,
            "seed {seed}: the sword holder's grant events"
        );
        assert!(
            sims[0].0.mobs().iter().any(|m| m.elite),
            "seed {seed}: the fixture's elite camp spawned"
        );
        let mut hurt = false;
        for t in 0..TICKS {
            let legacy = drive_tick(&mut sims[0].0, t);
            let rework = drive_tick(&mut sims[1].0, t);
            assert_eq!(legacy, rework, "seed {seed}, tick {t}: the events differ");
            assert_ne!(
                sims[0].0.state_hash(),
                sims[1].0.state_hash(),
                "seed {seed}, tick {t}: the Rework world folds no word of its own (same digest as Legacy)"
            );
            hurt |= sims[0]
                .0
                .mobs()
                .iter()
                .any(|m| m.elite && m.core.vitals.hp < m.core.vitals.max_hp);
            // The elite's sheet (hp, max hp) is the same in both worlds on every tick.
            let elites = |s: &Sim| -> Vec<(f64, f64)> {
                s.mobs()
                    .iter()
                    .filter(|m| m.elite)
                    .map(|m| (m.core.vitals.hp, m.core.vitals.max_hp))
                    .collect()
            };
            assert_eq!(
                elites(&sims[0].0),
                elites(&sims[1].0),
                "seed {seed}, tick {t}: elite hp"
            );
        }
        elite_hurt_seeds += u32::from(hurt);
        let rework_cp = serde_json::to_value(&sims[1].0).unwrap();
        assert_eq!(rework_cp["statics"]["config"]["augments"], json!("rework"));
        assert_eq!(rework_cp["statics"]["config"]["elites"], json!("rework"));
        assert_eq!(
            without_rules(&sims[0].0),
            without_rules(&sims[1].0),
            "seed {seed}: the worlds differ beyond the two rules"
        );
    }
    assert!(
        elite_hurt_seeds >= 1,
        "the sweep never hurt the elite: the equivalence did not exercise elite combat"
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// 4. A Rework checkpoint with fold cards and a ranged packet in flight.
// ---------------------------------------------------------------------------------------------------------------------

/// A live-rule world whose bow holder holds Committed, Machine Gun and Arcane Overload fires; a checkpoint taken while a
/// ranged packet is in flight (a projectile, or a ranged pending action, both checked against the packet envelope by
/// the restore) restores under `Rework`, and the restored match plays on exactly as the original.
#[test]
fn a_rework_checkpoint_with_fold_cards_and_a_ranged_packet_in_flight_restores() {
    let config = with_rules(pve_config(), AugmentRule::Rework, EliteRule::Rework);
    let sim = Sim::reset_active(config, pve_arena(), Secrets::from_seed(21), &placements());
    let (mut sim, _) = grant_by_chests(
        sim,
        PlayerId(1),
        &[
            CardId::Committed,
            CardId::MachineGun,
            CardId::ArcaneOverload,
        ],
    );
    let mut in_flight = None;
    for t in 0..200 {
        drive_tick(&mut sim, t);
        let cp = serde_json::to_value(&sim).unwrap();
        let projectiles = cp["world"]["projectiles"]
            .as_array()
            .is_some_and(|a| !a.is_empty());
        if projectiles {
            in_flight = Some(cp);
            break;
        }
    }
    let cp = in_flight.expect("the bow holder never had a projectile in flight in 200 ticks");
    let mut restored = Sim::restore(cp.clone()).expect("the Rework checkpoint is refused");
    assert_eq!(
        restored.config().augments,
        AugmentRule::Rework,
        "the restore kept the rule"
    );
    assert_eq!(
        restored.config().elites,
        EliteRule::Rework,
        "the restore kept the rule"
    );
    assert_eq!(restored.state_hash(), sim.state_hash());
    for t in 0..60 {
        let a = drive_tick(&mut sim, 1000 + t);
        let b = drive_tick(&mut restored, 1000 + t);
        assert_eq!(a, b, "tick {t} after the restore: events");
        assert_eq!(
            sim.state_hash(),
            restored.state_hash(),
            "tick {t} after the restore"
        );
    }
}
