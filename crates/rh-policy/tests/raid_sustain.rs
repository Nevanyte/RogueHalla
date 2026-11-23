//! P-RAID-C oracle (loose proof, docs/backlog/packets/P-RAID-C.md): the RaidBot's sustain. A `RaidBot` seated as a human
//! (`bot: false`) on the live content world, near a camp, holding potions or low on hp, drinks within a second of its
//! hp crossing the price table's drink threshold, leaves a fight the death model says it will lose, and clears
//! four- and five-member camps a bot without sustain refuses. Every scene has a control: the same seat priced with
//! `Prices::farm_only()` (sustain off).
//!
//! The harness is `raid_bot.rs`'s (seat API: `drive_seats_with` -> `Sim::step_into` -> `feed_seats`). A seat's potions
//! are declared setup (a checkpoint with `potion_large` set, accepted by `Sim::restore`, as `potion_fact_tests.rs`
//! does); every use, heal and death below is produced by `validate` + `Sim::submit` and ordinary steps.
use rh_policy::bots::raid::values::{Prices, SustainPrices};
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::mob_runtime::MobKind;
use rh_sim::potions::Notice;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, Placement, PlayerId, Secrets, SightCache, Sim, SimConfig,
    Vec2, WeaponId,
};
use std::sync::Arc;

/// The seeds of the potion and camp scenes; the disengage scene (cheap runs, rarer deaths) uses MANY.
const SEEDS: std::ops::RangeInclusive<u64> = 1..=8;
const MANY: std::ops::RangeInclusive<u64> = 1..=14;
/// A second at 20 Hz.
const SECOND: u32 = 20;
/// A member within this of the fighter reaches it (melee and the shorter bows).
const REACH: f64 = 160.;

fn map() -> Arena {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
    );
    let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
    let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
    file.validate()
        .expect("an admitted map")
        .into_arena()
        .expect("an arena")
}

fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        min_players: 1,
        ..SimConfig::live_content()
    }
}

fn floor_line(arena: &Arena, a: Vec2, b: Vec2) -> bool {
    (0..=40).all(|i| {
        let t = f64::from(i) / 40.;
        arena.map.is_floor(
            rh_sim::map::world_to_tile(a.x + (b.x - a.x) * t),
            rh_sim::map::world_to_tile(a.y + (b.y - a.y) * t),
        )
    })
}

/// One fight: a normal camp of `members` regional members, and where the fighter starts.
struct Scene {
    seed: u64,
    arena: Arc<Arena>,
    camp: u32,
    start: Vec2,
    /// hp at the start as a share of max hp.
    hp: f64,
    /// Large potions held at the start.
    potions: u8,
}

fn placement(start: Vec2, hp: Option<f64>) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at: start,
        hp,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }]
}

/// The first camp with no elite and exactly `members` regional members, and a start `dist` from their centroid with a
/// straight floor line to it.
fn scene(seed: u64, members: usize, dist: f64, hp: f64, potions: u8) -> Scene {
    let secrets = Secrets::from_seed(seed);
    let arena = rh_sim::quests::board::map_admission::ensure(Arc::new(map()), secrets)
        .expect("a board-admitted map");
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(arena.centre(), None),
    );
    for (ci, roster) in probe.camp_rosters().iter().enumerate() {
        if roster.elite.is_some() {
            continue;
        }
        let at: Vec<Vec2> = probe
            .mobs()
            .iter()
            .filter(|m| m.camp == ci as u32 && m.kind == MobKind::Regional && m.zombie.is_none())
            .map(|m| m.core.pos())
            .collect();
        if at.len() != members {
            continue;
        }
        let n = at.len() as f64;
        let centre = Vec2::new(
            at.iter().map(|p| p.x).sum::<f64>() / n,
            at.iter().map(|p| p.y).sum::<f64>() / n,
        );
        for k in 0..16 {
            let a = f64::from(k) * std::f64::consts::TAU / 16.;
            let start = Vec2::new(
                centre.x + rh_sim::math::cos(a) * dist,
                centre.y + rh_sim::math::sin(a) * dist,
            );
            if floor_line(&arena, start, centre) {
                return Scene {
                    seed,
                    arena,
                    camp: ci as u32,
                    start,
                    hp,
                    potions,
                };
            }
        }
    }
    panic!("seed {seed}: no {members}-member normal camp with a floor start");
}

fn world(s: &Scene) -> (Sim, PlayerId) {
    let secrets = Secrets::from_seed(s.seed);
    let max_hp = {
        let sim = Sim::reset_active(
            config(),
            s.arena.clone(),
            secrets,
            &placement(s.start, None),
        );
        sim.players()[0].vitals.max_hp
    };
    let mut sim = Sim::reset_active(
        config(),
        s.arena.clone(),
        secrets,
        &placement(s.start, (s.hp < 1.).then_some(max_hp * s.hp)),
    );
    if s.potions > 0 {
        // Declared setup, as `potion_fact_tests.rs`: the stock is set in a checkpoint `Sim::restore` must accept.
        let mut checkpoint = serde_json::to_value(&sim).expect("a checkpoint");
        checkpoint["world"]["players"][0]["potion_large"] = serde_json::json!(s.potions);
        sim = Sim::restore(checkpoint).expect("the declared stock is accepted");
    }
    let me = sim.players()[0].id;
    (sim, me)
}

#[derive(Debug)]
struct Run {
    /// The tick whose step reported `CampCleared` for the scene camp.
    cleared: Option<u32>,
    /// The tick the fighter fell.
    died: Option<u32>,
    /// Ticks at which the sim accepted a potion use (`Used` or `LargeUsed` notice) and the hp share then.
    drinks: Vec<(u32, f64)>,
    /// The first tick the fighter stood below the drink threshold holding a potion with no heal running.
    crossed: Option<u32>,
    /// The first tick the death model, from the hp held and the members alive (hp-weighted, as `farm.rs` counts them),
    /// gives `SustainPrices::LOCAL.disengage_death` or worse for finishing the camp, with a member within `REACH`: the
    /// fight has turned.
    turned: Option<u32>,
    turned_at: Option<Vec2>,
    turned_from: Option<Vec2>,
    /// How far the fighter moved straight away from the nearest member in the second after `turned` (negative: toward).
    away_1s: Option<f64>,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
    hash: u64,
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn run(s: &Scene, policy: Box<dyn Policy>, max_ticks: u32, price: &Prices) -> Run {
    let threshold = SustainPrices::LOCAL.drink_below;
    let disengage = SustainPrices::LOCAL.disengage_death;
    let plan = rh_policy::bots::planning::PlanMap::from_arena(&s.arena);
    let region = rh_policy::bots::planning::zone_faction(plan.camps[s.camp as usize].zone);
    let duel = rh_policy::bots::planning::matchup_share(FactionId::Holy, region, false);
    let (mut sim, me) = world(s);
    let start = MatchStart::of(&sim, me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut r = Run {
        cleared: None,
        died: None,
        drinks: Vec::new(),
        crossed: None,
        turned: None,
        turned_at: None,
        turned_from: None,
        away_1s: None,
        stats: SeatStats::default(),
        fault: None,
        hash: 0,
    };
    for tick in 0..max_ticks {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        let Some(p) = sim.player(me).filter(|p| p.standing()) else {
            r.died = Some(tick);
            break;
        };
        let share = p.vitals.hp / p.vitals.max_hp;
        for e in &events {
            match e {
                Event::CampCleared { camp } if *camp == s.camp && r.cleared.is_none() => {
                    r.cleared = Some(tick)
                }
                Event::PotionNotice { id, notice }
                    if *id == me && matches!(notice, Notice::Used | Notice::LargeUsed) =>
                {
                    r.drinks.push((tick, share))
                }
                _ => {}
            }
        }
        if r.crossed.is_none()
            && share < threshold
            && (p.potion_large > 0 || p.potion_small > 0)
            && p.potions_healing == 0
        {
            r.crossed = Some(tick);
        }
        let at = p.pos();
        let members: Vec<_> = sim
            .mobs()
            .iter()
            .filter(|m| {
                m.camp == s.camp
                    && m.kind == MobKind::Regional
                    && m.zombie.is_none()
                    && m.status == rh_sim::Status::Alive
            })
            .collect();
        let nearest = members
            .iter()
            .map(|m| m.core.pos())
            .min_by(|a, b| dist(*a, at).total_cmp(&dist(*b, at)));
        if r.turned.is_none() && nearest.is_some_and(|m| dist(m, at) <= REACH) {
            let left: f64 = members
                .iter()
                .map(|m| m.core.vitals.hp / m.core.vitals.max_hp)
                .sum();
            let d =
                rh_policy::bots::raid::exec::farm::death_chance(share, duel, left, &price.death);
            if d >= disengage {
                r.turned = Some(tick);
                r.turned_at = Some(at);
                r.turned_from = nearest;
            }
        }
        if let (Some(t0), Some(from), Some(m)) = (r.turned, r.turned_at, r.turned_from) {
            if tick == t0 + SECOND {
                let d = dist(from, m).max(1e-6);
                let (ux, uy) = ((from.x - m.x) / d, (from.y - m.y) / d);
                r.away_1s = Some((at.x - from.x) * ux + (at.y - from.y) * uy);
            }
        }
        if r.cleared.is_some() {
            break;
        }
    }
    r.stats = seats[0].stats.clone();
    r.fault = seats[0].fault.clone();
    r.hash = sim.state_hash();
    r
}

fn raid(prices: Prices) -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices))
}

/// The control: sustain off, everything else as the farm tests price it (quest rewards and augments worth nothing).
fn control() -> Prices {
    Prices::farm_only()
}

/// The same table with sustain on.
fn sustained(base: Prices) -> Prices {
    Prices {
        sustain: SustainPrices::LOCAL,
        ..base
    }
}

/// A table that does not price a death, so the farm takes the fight it is offered (the packet's scenes need the fight
/// to happen; sustain must not depend on `death_loss` to rank first: it leaves because the model says the fight is
/// lost, `SustainPrices::disengage_death`).
fn reckless() -> Prices {
    Prices {
        death_loss: 0.,
        ..Prices::farm_only()
    }
}

const MAX_TICKS: u32 = 1_800;

fn runs(
    members: usize,
    from: f64,
    potions: u8,
    seeds: std::ops::RangeInclusive<u64>,
    prices: Prices,
) -> Vec<(u64, Run)> {
    seeds
        .map(|seed| {
            let s = scene(seed, members, from, 1.0, potions);
            (seed, run(&s, raid(prices), MAX_TICKS, &prices))
        })
        .collect()
}

/// Scene 1, Drink. A four-member camp fought on a table that takes the fight: the fighter holds three potions and, on the
/// control, never drinks one; with sustain on it drinks within a second of crossing the threshold, on every seed it
/// crosses, and never at a healthy hp.
#[test]
fn a_potion_is_drunk_within_a_second_of_hp_crossing_the_drink_threshold() {
    let off = runs(4, 100., 3, SEEDS, reckless());
    let crossed_off = off.iter().filter(|(_, r)| r.crossed.is_some()).count();
    assert!(
        crossed_off >= 7,
        "control: the scene takes the fighter below {} on {crossed_off} of 8 seeds, holding a potion: {off:#?}",
        SustainPrices::LOCAL.drink_below
    );
    assert!(
        off.iter().all(|(_, r)| r.drinks.is_empty()),
        "control: sustain off never drinks: {off:#?}"
    );
    let on = runs(4, 100., 3, SEEDS, sustained(reckless()));
    let crossed: Vec<_> = on.iter().filter(|(_, r)| r.crossed.is_some()).collect();
    assert!(
        crossed.len() >= 7,
        "sustain on: the fighter crosses the threshold holding a potion on {} of 8 seeds, fewer than 7: {on:#?}",
        crossed.len()
    );
    for (seed, r) in &crossed {
        let c = r.crossed.expect("filtered");
        assert!(
            r.drinks.iter().any(|(d, _)| *d <= c + SECOND),
            "seed {seed}: hp crossed below {} at tick {c} with potions held and no heal running: no potion use by tick \
             {}: {r:?}",
            SustainPrices::LOCAL.drink_below,
            c + SECOND
        );
    }
    for (seed, r) in &on {
        for (tick, hp) in &r.drinks {
            assert!(
                *hp <= SustainPrices::LOCAL.drink_below + 0.05,
                "seed {seed}: drank at tick {tick} with hp {hp:.2}, above the drink threshold: {r:?}"
            );
        }
        assert!(
            r.drinks.len() <= 3,
            "seed {seed}: three potions held: {r:?}"
        );
    }
}

/// Scene 2, Disengage. A four-member camp fought with no potion: the control fights on and dies on some seeds (the death
/// model says the fight is lost about 100 ticks before it does); with sustain on, the fighter walks away from the
/// nearest member within a second of the fight turning, and survives on at least two of the seeds the control died.
#[test]
fn a_fight_the_death_model_says_is_lost_is_left_within_a_second() {
    let off = runs(4, 100., 0, MANY, reckless());
    let fell: Vec<u64> = off
        .iter()
        .filter(|(_, r)| r.died.is_some())
        .map(|(s, _)| *s)
        .collect();
    assert!(
        fell.len() >= 2,
        "control: the fight kills the fighter on {fell:?}, fewer than 2 of 14 seeds: the scene is not hard: {off:#?}"
    );
    assert!(
        off.iter()
            .filter(|(s, _)| fell.contains(s))
            .all(|(_, r)| r.turned.is_some_and(|t| r.died.is_some_and(|d| d > t))),
        "control: every death comes after the model said the fight was lost: {off:#?}"
    );
    let on = runs(4, 100., 0, MANY, sustained(reckless()));
    let turned: Vec<_> = on.iter().filter(|(_, r)| r.turned.is_some()).collect();
    assert!(
        !turned.is_empty(),
        "sustain on: the fight turns on at least one seed: {on:#?}"
    );
    for (seed, r) in &turned {
        let away = r.away_1s.unwrap_or(f64::NEG_INFINITY);
        assert!(
            away >= 30.,
            "seed {seed}: the fight turned at tick {:?}; one second later the fighter has moved {away:.0} u away from \
             the nearest member (need 30 or more; control moves toward it): {r:?}",
            r.turned
        );
    }
    let kept: Vec<u64> = on
        .iter()
        .filter(|(s, r)| fell.contains(s) && r.died.is_none())
        .map(|(s, _)| *s)
        .collect();
    assert!(
        kept.len() >= 2,
        "sustain on: the fighter survives on {kept:?} of the control's death seeds {fell:?}, fewer than 2: {on:#?}"
    );
    let deaths = on.iter().filter(|(_, r)| r.died.is_some()).count();
    assert!(
        deaths < fell.len(),
        "sustain on: {deaths} deaths against the control's {}: {on:#?}",
        fell.len()
    );
}

/// Scene 3, Camps the base bot refuses. With the default prices the farm prices a four- or five-member clear as a likely
/// death and the fighter walks away; holding three potions, sustain on makes the fighter clear them, without more
/// deaths than the control's.
#[test]
fn four_and_five_member_camps_the_base_bot_refuses_are_cleared_with_potions() {
    for members in [5usize, 4] {
        let off = runs(members, 100., 3, SEEDS, control());
        let off_clears = off.iter().filter(|(_, r)| r.cleared.is_some()).count();
        let off_deaths = off.iter().filter(|(_, r)| r.died.is_some()).count();
        assert!(
            off_clears <= 1,
            "control, {members}-member camp: the base bot clears it on {off_clears} of 8 seeds (it refuses it): {off:#?}"
        );
        let on = runs(members, 100., 3, SEEDS, sustained(control()));
        let clears = on.iter().filter(|(_, r)| r.cleared.is_some()).count();
        let deaths = on.iter().filter(|(_, r)| r.died.is_some()).count();
        assert!(
            clears >= 5,
            "sustain on, {members}-member camp, three potions: cleared on {clears} of 8 seeds, fewer than 5: {on:#?}"
        );
        assert!(
            deaths <= off_deaths.max(1),
            "sustain on, {members}-member camp: {deaths} deaths against the control's {off_deaths} (one allowed): {on:#?}"
        );
    }
}

/// Scene 4, Clean play and a replay: every command is accepted, no seat fault, the policy decides every turn itself, and one
/// seed replays exactly (including its drinks).
#[test]
fn sustain_sends_only_accepted_commands_and_replays_a_seed_exactly() {
    let prices = sustained(reckless());
    let s = scene(1, 4, 100., 1.0, 3);
    let a = run(&s, raid(prices), MAX_TICKS, &prices);
    let b = run(&s, raid(prices), MAX_TICKS, &prices);
    assert!(a.fault.is_none(), "no seat fault: {a:?}");
    assert_eq!(a.stats.fallback_decisions, 0, "{a:?}");
    assert_eq!(a.stats.rejected, 0, "every proposal was taken: {a:?}");
    assert_eq!(a.stats.accepted, a.stats.proposed, "{a:?}");
    assert!(
        !a.drinks.is_empty(),
        "the seat drank (a bot that does nothing replays trivially): {a:?}"
    );
    assert_eq!(a.drinks, b.drinks, "the same drinks");
    assert_eq!(a.cleared, b.cleared, "the same clear tick");
    assert_eq!(a.stats, b.stats, "the same proposals");
    assert_eq!(a.hash, b.hash, "the same final state hash");
}
