//! P-RAID-E oracle (loose proof, docs/backlog/packets/P-RAID-E.md): the RaidBot prices an elite camp, fights the elite and
//! picks the card its price table ranks highest from the chest. A `RaidBot` seated as a human (`bot: false`) on the live
//! content world, a start 256 u or less from an elite camp's members, with a declared build (normal-mob stacks through
//! `rh_sim::trusted_build::apply`, large potions through a checkpoint, as `raid_sustain.rs` does).
//!
//! The harness is `raid_sustain.rs`'s (`drive_seats_with` -> `Sim::step_into` -> `feed_seats`); every command is produced
//! by `validate` + `Sim::submit`. The treatment table is `Prices::LOCAL` with the quest rewards zeroed (quests are not
//! under test); the control is `Prices::farm_only()` (elite OFF).
use rh_policy::bots::raid::exec::elite::{best_card, death_prior};
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::mob_runtime::MobKind;
use rh_sim::reward_chests::OptionId;
use rh_sim::trusted_build::TrustedBuild;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, Secrets, SightCache,
    Sim, SimConfig, Vec2, WeaponId,
};
use std::sync::Arc;

/// Five minutes of sim time at 20 Hz.
const FIVE_MINUTES: u32 = 6_000;
const SEEDS: std::ops::RangeInclusive<u64> = 1..=6;

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

/// The quest rewards are worth nothing; elite and sustain are as `Prices::LOCAL` has them.
fn treated() -> Prices {
    Prices {
        reward_default: 0.,
        reward_rows: &[],
        ..Prices::LOCAL
    }
}

/// What the scene's camp is.
#[derive(Clone, Copy)]
enum Camp {
    /// The camp rolling this elite kind.
    Elite(MobTypeId),
    /// The first normal camp of exactly three members.
    Normal,
}

struct Scene {
    seed: u64,
    arena: Arc<Arena>,
    camp: u32,
    start: Vec2,
    weapon: WeaponId,
    /// Banked camps of stacks (12 a camp) the seat holds.
    camps: u32,
    potions: u8,
}

fn scene(seed: u64, which: Camp, weapon: WeaponId, camps: u32, potions: u8) -> Scene {
    let secrets = Secrets::from_seed(seed);
    let arena = rh_sim::quests::board::map_admission::ensure(Arc::new(map()), secrets)
        .expect("a board-admitted map");
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(arena.centre(), weapon),
    );
    let rosters = probe.camp_rosters();
    let camp = match which {
        Camp::Elite(kind) => rosters.iter().position(|r| r.elite == Some(kind)),
        Camp::Normal => rosters
            .iter()
            .enumerate()
            .position(|(i, r)| r.elite.is_none() && r.alive == 3 && i != 0),
    }
    .expect("the scene's camp") as u32;
    let members: Vec<Vec2> = probe
        .mobs()
        .iter()
        .filter(|m| m.camp == camp && m.zombie.is_none() && m.kind == MobKind::Regional)
        .map(|m| m.core.pos())
        .collect();
    let n = members.len() as f64;
    let centre = Vec2::new(
        members.iter().map(|p| p.x).sum::<f64>() / n,
        members.iter().map(|p| p.y).sum::<f64>() / n,
    );
    for d in [256., 192., 128.] {
        for k in 0..16 {
            let a = f64::from(k) * std::f64::consts::TAU / 16.;
            let start = Vec2::new(
                centre.x + rh_sim::math::cos(a) * d,
                centre.y + rh_sim::math::sin(a) * d,
            );
            if floor_line(&arena, start, centre) {
                return Scene {
                    seed,
                    arena,
                    camp,
                    start,
                    weapon,
                    camps,
                    potions,
                };
            }
        }
    }
    panic!("seed {seed}: no floor start near the camp");
}

/// `camps` normal camps banked: 12 stacks each of the stat a non-elite camp pays (a fixed walk over the camps).
fn banked(probe: &Sim, camps: u32) -> TrustedBuild {
    let buffs = probe.camp_buffs();
    let pays: Vec<MobTypeId> = probe
        .camp_rosters()
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_none())
        .map(|(i, r)| buffs.get(i).copied().flatten().unwrap_or(r.mob_type))
        .collect();
    let mut stacks: Vec<(MobTypeId, u64)> = Vec::new();
    for k in 0..camps as usize {
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

#[derive(Debug, Default)]
struct Run {
    /// First tick no elite of the scene camp stood alive.
    elite_dead: Option<u32>,
    died: Option<u32>,
    /// Tick the seat's card count rose through a chest pick.
    picked: Option<u32>,
    /// The chest's cards as the observer showed them, the index sent and the cards held before the pick.
    offer: Option<(Vec<String>, i8, Vec<String>)>,
    /// The camp was hurt by anyone (state left 0).
    touched: bool,
    /// The farthest the seat got from its start.
    moved: f64,
    cleared: Option<u32>,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
    hash: u64,
}

fn cards_held(sim: &Sim, me: rh_sim::PlayerId) -> Vec<String> {
    let b = &sim.player(me).expect("seated").build;
    b.cards().map(|c| c.as_str().to_owned()).collect()
}

fn run(s: &Scene, prices: Prices, max_ticks: u32) -> Run {
    let secrets = Secrets::from_seed(s.seed);
    let probe = Sim::reset_active(
        config(),
        s.arena.clone(),
        secrets,
        &placement(s.arena.centre(), s.weapon),
    );
    let build = banked(&probe, s.camps);
    let mut sim = Sim::reset_active(
        config(),
        s.arena.clone(),
        secrets,
        &placement(s.start, s.weapon),
    );
    let me = sim.players()[0].id;
    rh_sim::trusted_build::apply(&mut sim, me, &build).expect("the declared stacks");
    if s.potions > 0 {
        let mut checkpoint = serde_json::to_value(&sim).expect("a checkpoint");
        checkpoint["world"]["players"][0]["potion_large"] = serde_json::json!(s.potions);
        sim = Sim::restore(checkpoint).expect("the declared stock is accepted");
    }
    let policy: Box<dyn Policy> =
        Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices));
    let start = MatchStart::of(&sim, me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut observer = ItemObserver::new(me);
    let mut last_offer: Vec<String> = Vec::new();
    let mut held_before = cards_held(&sim, me);
    let mut r = Run::default();
    for tick in 0..max_ticks {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        let mut obs = Observation::default();
        observer
            .prepare_into(&sim, &[], &mut obs, None)
            .expect("an observation")
            .commit();
        if let Some(c) = obs
            .reward_chests
            .iter()
            .find(|c| c.options.iter().any(|o| matches!(o.id, OptionId::Card(_))))
        {
            last_offer = c
                .options
                .iter()
                .map(|o| o.id.as_str().into_owned())
                .collect();
        }
        let Some(p) = sim.player(me).filter(|p| p.standing()) else {
            r.died = Some(tick);
            break;
        };
        r.moved = r.moved.max(dist(p.pos(), s.start));
        if sim.camp_rosters()[s.camp as usize].state != 0 {
            r.touched = true;
        }
        if r.elite_dead.is_none()
            && sim.camp_rosters()[s.camp as usize].elite.is_some()
            && !sim
                .mobs()
                .iter()
                .any(|m| m.camp == s.camp && m.elite && m.status == rh_sim::Status::Alive)
        {
            r.elite_dead = Some(tick);
        }
        for e in &events {
            match e {
                Event::CampCleared { camp } if *camp == s.camp && r.cleared.is_none() => {
                    r.cleared = Some(tick)
                }
                Event::ChestTaken { by, pick, .. }
                    if *by == me && *pick >= 0 && r.picked.is_none() =>
                {
                    let now = cards_held(&sim, me);
                    if now.len() > held_before.len() {
                        r.picked = Some(tick);
                        r.offer = Some((last_offer.clone(), *pick, held_before.clone()));
                    }
                }
                _ => {}
            }
        }
        held_before = cards_held(&sim, me);
        if r.picked.is_some_and(|t| tick >= t + 100) {
            break;
        }
    }
    r.stats = seats[0].stats.clone();
    r.fault = seats[0].fault.clone();
    r.hash = sim.state_hash();
    r
}

fn executioner(seed: u64) -> Scene {
    scene(
        seed,
        Camp::Elite(MobTypeId::Executioner),
        WeaponId::Sword,
        3,
        2,
    )
}

/// A seat that is weak for the elite: a fresh hp bar and 24 stacks, no potion.
fn weak(seed: u64, kind: MobTypeId) -> Scene {
    scene(seed, Camp::Elite(kind), WeaponId::Sword, 2, 0)
}

fn clean(r: &Run, seed: u64) {
    assert!(r.fault.is_none(), "seed {seed}: no seat fault: {r:?}");
    assert_eq!(r.stats.fallback_decisions, 0, "seed {seed}: {r:?}");
    assert_eq!(
        r.stats.rejected, 0,
        "seed {seed}: every proposal was taken: {r:?}"
    );
}

/// The plan's test. Executioner is the kind a sword kills for sure (measured with a Farm that took the camp: 6 of 6
/// kills, 0 deaths, the chest's card taken after 5 of 6); a seat with 36 stacks and two potions kills it inside five
/// minutes, opens the chest and holds one card more.
#[test]
fn kills_a_favourable_elite_and_picks_a_card_within_five_minutes() {
    let runs: Vec<(u64, Run)> = SEEDS
        .map(|seed| (seed, run(&executioner(seed), treated(), FIVE_MINUTES)))
        .collect();
    for (seed, r) in &runs {
        clean(r, *seed);
    }
    let killed = runs.iter().filter(|(_, r)| r.elite_dead.is_some()).count();
    let picked = runs.iter().filter(|(_, r)| r.picked.is_some()).count();
    let died = runs.iter().filter(|(_, r)| r.died.is_some()).count();
    assert!(
        killed >= 4,
        "the elite died on {killed} of 6 seeds, fewer than 4: {runs:#?}"
    );
    assert!(
        picked >= 3,
        "a card was picked on {picked} of 6 seeds, fewer than 3: {runs:#?}"
    );
    assert!(
        died <= 1,
        "the seat died on {died} of 6 seeds, more than 1: {runs:#?}"
    );
    for (seed, r) in &runs {
        if let (Some(k), Some(p)) = (r.elite_dead, r.picked) {
            assert!(
                p >= k && p <= k + 400,
                "seed {seed}: the card came {} ticks after the kill (the chest lasts 300): {r:?}",
                i64::from(p) - i64::from(k)
            );
        }
    }
}

/// A seat weak for the elite does not start the fight, on every seed, and a seat that is not weak does (so the decline
/// is a choice, not a seat that never goes). The pure death prior orders kinds, weapons, stacks and potions as the bench
/// table does.
#[test]
fn declines_an_unfavourable_elite_camp_and_takes_a_favourable_one() {
    let e = &Prices::LOCAL.elite;
    let spc = Prices::LOCAL.stacks_per_camp;
    let prior = |kind, weapon, stacks: f64, held: f64| {
        death_prior(e, kind, Some(weapon), stacks, spc, held)
    };
    let fair = prior(MobTypeId::Executioner, WeaponId::Sword, 36., 1.8);
    let hard = prior(MobTypeId::AvalancheTroll, WeaponId::Sword, 24., 1.);
    assert!(
        fair < 0.15,
        "executioner, sword, 36 stacks, two potions: {fair}"
    );
    assert!(
        hard > 0.35,
        "avalanche troll, sword, 24 stacks, no potion: {hard}"
    );
    assert!(
        prior(MobTypeId::Executioner, WeaponId::Wand, 24., 1.)
            > 1.2 * prior(MobTypeId::Executioner, WeaponId::Sword, 24., 1.),
        "a wand dies more than a sword"
    );
    assert!(
        prior(MobTypeId::Executioner, WeaponId::Sword, 12., 1.)
            > prior(MobTypeId::Executioner, WeaponId::Sword, 60., 1.),
        "more stacks, fewer deaths"
    );
    assert!(
        prior(MobTypeId::Wyrmling, WeaponId::Sword, 24., 1.)
            > prior(MobTypeId::Wyrmling, WeaponId::Sword, 24., 2.2),
        "potions held, fewer deaths"
    );
    for kind in [
        MobTypeId::Wyrmling,
        MobTypeId::GlacialWarden,
        MobTypeId::AvalancheTroll,
    ] {
        for seed in 1..=4u64 {
            let r = run(&weak(seed, kind), treated(), 1_800);
            clean(&r, seed);
            assert!(
                !r.touched,
                "{}, seed {seed}: the seat started a fight with an elite it cannot win: {r:?}",
                kind.as_str()
            );
        }
    }
    let took = SEEDS
        .filter(|seed| run(&executioner(*seed), treated(), 1_800).touched)
        .count();
    assert!(
        took >= 4,
        "the favourable elite camp was fought on {took} of 6 seeds, fewer than 4"
    );
}

/// The card is the priced one, and the control table proposes nothing from this executor.
#[test]
fn the_pick_is_the_card_the_price_table_ranks_highest() {
    // Hand-built offers (names are `CardId::as_str`; rows in `Prices::LOCAL.augment_rows`).
    let p = Prices::LOCAL;
    let none = |_: &str| false;
    let free = vec![String::new(); 3];
    let offer = |a: &str, b: &str, c: &str| vec![a.to_owned(), b.to_owned(), c.to_owned()];
    assert_eq!(
        best_card(
            &p,
            &offer("slowBurn", "markedMan", "tankEngine"),
            &free,
            &none
        ),
        Some(1)
    );
    assert_eq!(
        best_card(
            &p,
            &offer("slowBurn", "markedMan", "tankEngine"),
            &free,
            &|c| c == "markedMan"
        ),
        Some(2),
        "a held card is refused, so it is not picked"
    );
    let taken = vec![String::new(), "?".to_owned(), String::new()];
    assert_eq!(
        best_card(
            &p,
            &offer("slowBurn", "markedMan", "tankEngine"),
            &taken,
            &none
        ),
        Some(2)
    );
    assert_eq!(
        best_card(
            &p,
            &offer("siphon", "eyeForAnEye", "raceToZero"),
            &free,
            &none
        ),
        Some(0),
        "ties go to the lowest index"
    );
    assert_eq!(
        best_card(&p, &offer("slowBurn", "ad1", "raceToZero"), &free, &none),
        Some(1),
        "a card with no row is worth augment_default (1.0), more than Slow Burn (0.6)"
    );
    assert_eq!(
        best_card(
            &Prices::farm_only(),
            &offer("slowBurn", "markedMan", "tankEngine"),
            &free,
            &none
        ),
        None
    );
    assert_eq!(
        best_card(
            &p,
            &offer("slowBurn", "markedMan", "tankEngine"),
            &free,
            &|_| true
        ),
        None
    );
    // The pool's mean best of three is what the table says it is (within 10%).
    let mut rows: Vec<f64> = p.augment_rows.iter().map(|(_, v)| *v).collect();
    rows.sort_by(f64::total_cmp);
    let n = rows.len();
    // The largest of a triple sorted ascending sits at index k in k(k-1)/2 of the C(n,3) triples.
    let weighted: f64 = rows
        .iter()
        .enumerate()
        .map(|(k, v)| v * (k * k.saturating_sub(1) / 2) as f64)
        .sum();
    let mean_best = weighted / (n * (n - 1) * (n - 2) / 6) as f64;
    assert!(
        (p.elite.pool_pick - mean_best).abs() <= 0.1 * mean_best,
        "pool_pick {} against the rows' mean best of three {mean_best}",
        p.elite.pool_pick
    );
    // The scenes: every card taken is the best priced of those offered and not held.
    let value = |c: &str| p.augment_value(c);
    let mut picks = 0;
    let mut discriminating = 0;
    for seed in SEEDS {
        let r = run(&executioner(seed), treated(), FIVE_MINUTES);
        let Some((offer, pick, held)) = &r.offer else {
            continue;
        };
        picks += 1;
        let chosen = &offer[*pick as usize];
        let pickable: Vec<&String> = offer
            .iter()
            .filter(|c| !held.contains(c))
            .filter(|c| {
                rh_sim::cards::CardId::ALL
                    .iter()
                    .find(|id| id.as_str() == c.as_str())
                    .is_none_or(|id| id.definition().requires.is_none())
            })
            .collect();
        let best = pickable.iter().map(|c| value(c)).fold(f64::MIN, f64::max);
        assert!(
            value(chosen) >= best,
            "seed {seed}: took {chosen} ({}) from {offer:?}, the table ranks {best} highest: {r:?}",
            value(chosen)
        );
        if value(&offer[0]) < best {
            discriminating += 1;
        }
    }
    assert!(
        picks >= 3,
        "cards were picked on {picks} of 6 seeds, fewer than 3"
    );
    assert!(
        discriminating >= 1,
        "no offer had a better card than its first (the legacy pick takes the first of equals): the scene does not tell the priced pick from Loot's"
    );
    // The control table: elite off, the camp is never touched.
    for seed in 1..=3u64 {
        let r = run(&executioner(seed), Prices::farm_only(), 1_800);
        assert!(
            !r.touched && r.picked.is_none(),
            "seed {seed}: farm_only fought an elite: {r:?}"
        );
    }
}

/// One seed replays exactly (kill tick, pick and hash), and a seat with no elite in play draws and sends the same
/// with the executor on or off.
#[test]
fn an_elite_run_replays_exactly_and_draws_nothing_without_an_elite() {
    let a = run(&executioner(2), treated(), FIVE_MINUTES);
    let b = run(&executioner(2), treated(), FIVE_MINUTES);
    clean(&a, 2);
    assert!(
        a.elite_dead.is_some() && a.picked.is_some(),
        "the replayed seed kills and picks: {a:?}"
    );
    assert_eq!(a.elite_dead, b.elite_dead, "the same kill tick");
    assert_eq!(a.offer, b.offer, "the same offer and pick");
    assert_eq!(a.stats, b.stats, "the same proposals");
    assert_eq!(a.hash, b.hash, "the same final state hash");
    let normal = |prices| run(&scene(1, Camp::Normal, WeaponId::Sword, 0, 0), prices, 900);
    let on = normal(treated());
    let off = normal(Prices {
        elite: Prices::farm_only().elite,
        ..treated()
    });
    assert!(on.stats.proposed > 0, "the seat did something: {on:?}");
    assert_eq!(on.stats, off.stats, "no elite in play: the same proposals");
    assert_eq!(on.hash, off.hash, "no elite in play: the same state");
    assert_eq!(on.cleared, off.cleared);
}
