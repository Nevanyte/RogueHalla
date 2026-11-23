//! P-RAID-REACH-1 oracle, live scenes (docs/backlog/packets/P-RAID-REACH-1.md). Read-only for the implementer.
//!
//! Lab seed 7's traced trap: a ranged RaidBot seat (a human seat, `bot: false`) standing at (4423, 525), where camp 50's
//! Gaoler (radius 96, 11 territory cells, home (4304, 752), 256 u away) is inside its weapon's `lock_range` and the seat
//! is far outside the camp's leash band. On the base the seat shoots the Gaoler for the rest of the match and never
//! moves, and the Gaoler walks out, gives up and heals to full. Scene: the lab's generated map and board admission,
//! `live_content` with the lifecycle off, a seat with 36 banked stacks and two large potions (as `raid_elite.rs`),
//! `Prices::LOCAL` with the quest rewards zeroed. Every command goes through `validate` + `Sim::submit`.
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat};
use rh_sim::mapdata::patience::leash_ok;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::trusted_build::TrustedBuild;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, Placement, Secrets, SightCache, Sim, SimConfig, Vec2,
    WeaponId,
};
use std::sync::Arc;

const SEED: u64 = 7;
/// The traced seat position and the camp whose Gaoler it shoots.
const TRAP_AT: (f64, f64) = (4423., 525.);
const TRAP_CAMP: u32 = 50;
/// Two sim minutes.
const TICKS: u32 = 2_400;

fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        min_players: 1,
        ..SimConfig::live_content()
    }
}

fn lab_arena(seed: u64) -> Arc<Arena> {
    use rh_sim::mapgen::{coliseum, pipeline};
    let layout = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let options = pipeline::Options {
        decor: false,
        ..Default::default()
    };
    let map = generator
        .certified_styled(seed as i64, options)
        .expect("generated")
        .expect("accepted");
    let export = rh_sim::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: u32::from_str_radix(&rh_sim::mapgen::map_hash::map_hash(&map), 16)
            .expect("a hash"),
        map,
    };
    let arena = export
        .validate()
        .expect("admitted")
        .into_arena()
        .expect("an arena");
    rh_sim::quests::board::map_admission::ensure(Arc::new(arena), Secrets::from_seed(seed))
        .expect("a board-admitted map")
}

fn placement(start: Vec2, weapon: WeaponId) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at: start,
        hp: None,
        weapon0: Some(weapon),
        weapon1: None,
    }]
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// 36 stacks (three camps of 12) of the stats the normal camps pay, a fixed walk (as `raid_elite.rs`).
fn banked(probe: &Sim) -> TrustedBuild {
    let buffs = probe.camp_buffs();
    let pays: Vec<MobTypeId> = probe
        .camp_rosters()
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_none())
        .map(|(i, r)| buffs.get(i).copied().flatten().unwrap_or(r.mob_type))
        .collect();
    let mut stacks: Vec<(MobTypeId, u64)> = Vec::new();
    for k in 0..3usize {
        let stat = pays[(k * 7 + 3) % pays.len()];
        match stacks.iter_mut().find(|(m, _)| *m == stat) {
            Some(e) => e.1 += 12,
            None => stacks.push((stat, 12)),
        }
    }
    TrustedBuild {
        stacks,
        ..Default::default()
    }
}

/// `Prices::LOCAL` with the quest rewards zeroed and the elite death table cut to the Executioner row: every other kind
/// (the Gaoler too) is then priced by the executor's fallback (the farm's death model, `death_prior`'s no-row path),
/// which is what routes this scene's seat to camp 50's Gaoler (with the full table it prices the Gaoler at a 0.73
/// death rate and walks away; measured: the same seat roams). It is a scene control, not a behaviour under test.
fn treated() -> Prices {
    let mut p = Prices {
        reward_default: 0.,
        reward_rows: &[],
        ..Prices::LOCAL
    };
    p.elite.kind_death = &[("executioner", [0.656, 0.089, 0.022, 0.011])];
    p
}

/// What the seat did to one elite camp's elite.
#[derive(Debug, Clone)]
struct Es {
    hits: u32,
    /// Hits dealt while the seat stood inside the band.
    hits_in_band: u32,
    /// The lowest share of its hp the elite stood at.
    min_share: f64,
    /// Tick the elite died (to anyone).
    died_at: Option<u32>,
    /// First tick the seat stood inside the camp's leash band (the sim's `leash_ok`, `MOB_LEASH_MARGIN`, the elite's home).
    in_band_at: Option<u32>,
}

#[derive(Debug, Default)]
struct Run {
    elites: std::collections::BTreeMap<u32, Es>,
    seat_died_at: Option<u32>,
    final_pos: (f64, f64),
    fault: bool,
}

fn run(start: Vec2, weapon: WeaponId) -> Run {
    let arena = lab_arena(SEED);
    let secrets = Secrets::from_seed(SEED);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(arena.centre(), weapon),
    );
    let build = banked(&probe);
    let mut sim = Sim::reset_active(config(), arena.clone(), secrets, &placement(start, weapon));
    let me = sim.players()[0].id;
    rh_sim::trusted_build::apply(&mut sim, me, &build).expect("the declared stacks");
    let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
    cp["world"]["players"][0]["potion_large"] = serde_json::json!(2);
    sim = Sim::restore(cp).expect("the declared stock is accepted");
    let policy: Box<dyn Policy> =
        Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(treated()));
    let ms = MatchStart::of(&sim, me, SEED);
    let mut seats = vec![Seat::new(&ms, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let data = arena.map.layers().expect("layers").data().clone();
    let mut r = Run::default();
    for tick in 0..TICKS {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        let Some(p) = sim.player(me).filter(|p| p.standing()) else {
            r.seat_died_at = Some(tick);
            break;
        };
        r.final_pos = (p.pos().x, p.pos().y);
        for e in &events {
            match e {
                Event::Hit {
                    victim,
                    by: Some(b),
                    ..
                } if *b == me => {
                    if let Some(m) = sim.mobs().iter().find(|m| m.core.id == *victim && m.elite) {
                        // Margin 0 (coordinator's oracle-amend, 2026-11-23): the sim tests a chasing mob's own
                        // position at margin 0; the 24 u margin is for its retreat and spacing steps.
                        let inside = leash_ok(&data, m.camp, m.home, p.pos(), 0.);
                        let es = r.elites.entry(m.camp).or_insert(Es {
                            hits: 0,
                            hits_in_band: 0,
                            min_share: 1.,
                            died_at: None,
                            in_band_at: None,
                        });
                        es.hits += 1;
                        es.hits_in_band += u32::from(inside);
                    }
                }
                Event::Death { id, .. } => {
                    if let Some(m) = sim.mobs().iter().find(|m| m.core.id == *id && m.elite) {
                        if let Some(es) = r.elites.get_mut(&m.camp) {
                            es.died_at.get_or_insert(tick);
                        }
                    }
                }
                _ => {}
            }
        }
        for m in sim
            .mobs()
            .iter()
            .filter(|m| m.elite && m.status == rh_sim::Status::Alive)
        {
            if let Some(es) = r.elites.get_mut(&m.camp) {
                es.min_share = es.min_share.min(m.core.vitals.hp / m.core.vitals.max_hp);
                if es.in_band_at.is_none() && leash_ok(&data, m.camp, m.home, p.pos(), 0.) {
                    es.in_band_at = Some(tick);
                }
            }
        }
    }
    r.fault = seats[0].fault.is_some();
    println!("RUN {weapon:?} from {start:?}: {r:?}");
    r
}

/// The scene's premises, green on the base: the seat starts outside the band of camp 50 and inside its weapon's lock
/// range of the Gaoler, and shoots it (so the trap is the camp's, not a seat that never engaged).
fn premises(weapon: WeaponId, r: &Run) {
    let arena = lab_arena(SEED);
    let data = arena.map.layers().expect("layers").data();
    let c = &data.camps[TRAP_CAMP as usize];
    let home = Vec2::new(c.spots[0].x, c.spots[0].y);
    let at = Vec2::new(TRAP_AT.0, TRAP_AT.1);
    assert!(
        !leash_ok(
            data,
            TRAP_CAMP,
            home,
            at,
            rh_sim::constants::MOB_LEASH_MARGIN
        ),
        "premise: the start is outside the band"
    );
    let range = match rh_sim::content::weapon(weapon).shape {
        rh_sim::content::Shape::Projectile { range, .. } => range * 0.8,
        _ => 40.,
    };
    assert!(
        dist(at, home) < range,
        "premise: inside the lock range {range}"
    );
    assert!(!r.fault, "no seat fault");
    let es = r
        .elites
        .get(&TRAP_CAMP)
        .expect("premise: the seat shoots camp 50's Gaoler");
    assert!(es.hits > 0, "{es:?}");
}

/// The fix, measured and asserted: a seat that starts in the trap steps into the band within 600 ticks (30 s) and then
/// fights from there (at least 5 of its hits land while it stands inside the band), where the base seat stands still
/// outside the band with 187 (Wand) or 143 (Bow) hits, none of them from inside it, and the Gaoler never below 0.99 of its
/// hp. The Gaoler's hp is deliberately not asserted: a seat that starts inside the band hurts it by 0.10 (Wand, 18 hits)
/// or 0.03 (Bow, 12 hits) before Sustain's leave and the death prior take it away (measured, the `--ignored` test below),
/// so any floor on the hp share would sit on a knife edge; this packet does not tune that.
fn assert_escapes_the_trap(weapon: WeaponId) {
    let r = run(Vec2::new(TRAP_AT.0, TRAP_AT.1), weapon);
    premises(weapon, &r);
    let es = &r.elites[&TRAP_CAMP];
    assert!(
        es.in_band_at.is_some_and(|t| t <= 600),
        "{weapon:?}: the seat never stepped into the band of the elite it shoots (in band at {:?}, final {:?}): {es:?}",
        es.in_band_at,
        r.final_pos
    );
    assert!(
        es.hits_in_band >= 5,
        "{weapon:?}: fewer than 5 hits from inside the band (lowest share {}): {es:?}",
        es.min_share
    );
}

/// RED on the base (Wand): see the module doc.
#[test]
fn a_wand_seat_in_the_trap_steps_into_the_band_and_hurts_the_elite() {
    assert_escapes_the_trap(WeaponId::Wand);
}

/// RED on the base (Bow).
#[test]
fn a_bow_seat_in_the_trap_steps_into_the_band_and_hurts_the_elite() {
    assert_escapes_the_trap(WeaponId::Bow);
}

/// Measurement only, `cargo test -p rh-policy --test raid_reach -- --ignored --nocapture`: the same seats starting just
/// inside the band of camp 50 (where the fix would put them), for the floors above.
#[test]
#[ignore = "measurement"]
fn measure_a_seat_starting_inside_the_band() {
    for w in [WeaponId::Wand, WeaponId::Bow] {
        run(Vec2::new(4304., 700.), w);
    }
}
