//! P-RAID-C-fix oracle (loose proof, docs/backlog/packets/P-RAID-C-fix.md): a leave the `RaidBot` began must not outlive
//! the seat's Sustain goal. Once another goal holds the seat, an earlier leave does not come back at a later hp dip the
//! death model does not call lost; plus a guard that `SustainPrices::LOCAL`'s copies of sim numbers match the sim.
//!
//! The harness is `raid_sustain.rs`'s (copied: that file is not edited). The seat's policy is shared through a mutex so
//! the test can read `RaidBot::goal()` every tick; the hp edits are declared setup (a checkpoint `Sim::restore` accepts,
//! as `raid_sustain.rs` does for potions). Every command still goes through `validate` + `Sim::submit`.
use rh_policy::bots::raid::goal::{GoalKind, Target};
use rh_policy::bots::raid::values::{Prices, SustainPrices};
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{
    drive_seats_with, feed_seats, scripted, InputKind, InputPreview, InputRefusal,
    InputRequirements, MatchStart, Policy, PolicyFinish, PolicyInput, PolicySpec, PolicyStats,
    Seat,
};
use rh_sim::mob_runtime::MobKind;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, Placement, PlayerId, Secrets, SightCache, Sim, SimConfig,
    Vec2, WeaponId,
};
use std::sync::{Arc, Mutex};

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
    start: Vec2,
    /// hp at the start as a share of max hp.
    hp: f64,
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
fn scene(seed: u64, members: usize, dist: f64, hp: f64) -> Scene {
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
                    start,
                    hp,
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
    let sim = Sim::reset_active(
        config(),
        s.arena.clone(),
        secrets,
        &placement(s.start, (s.hp < 1.).then_some(max_hp * s.hp)),
    );
    let me = sim.players()[0].id;
    (sim, me)
}

/// The seat's `RaidBot`, shared so the test can read its goal between ticks.
struct Probe(Arc<Mutex<RaidBot>>);

impl Policy for Probe {
    fn input_requirements(&self) -> InputRequirements {
        self.0.lock().unwrap().input_requirements()
    }
    fn input_kind(&self) -> InputKind {
        self.0.lock().unwrap().input_kind()
    }
    fn check_input(&self, input: InputPreview<'_>) -> Result<(), InputRefusal> {
        self.0.lock().unwrap().check_input(input)
    }
    fn decide_input_into(
        &mut self,
        input: PolicyInput<'_>,
        out: &mut Vec<rh_sim::PublicCommand>,
    ) -> Result<(), InputRefusal> {
        self.0.lock().unwrap().decide_input_into(input, out)
    }
    fn spec(&self) -> PolicySpec {
        self.0.lock().unwrap().spec()
    }
    fn stats(&self) -> Option<PolicyStats> {
        self.0.lock().unwrap().stats()
    }
    fn reset(&mut self, m: &MatchStart) {
        self.0.lock().unwrap().reset(m)
    }
    fn finish(&mut self, result: Option<&rh_sim::RunResult>) -> Option<PolicyFinish> {
        self.0.lock().unwrap().finish(result)
    }
    fn decide(&mut self, obs: &rh_sim::Observation) -> Vec<rh_sim::PublicCommand> {
        self.0.lock().unwrap().decide(obs)
    }
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// A table that does not price a death, so the farm takes the fight it is offered, with sustain on.
fn prices() -> Prices {
    Prices {
        death_loss: 0.,
        sustain: SustainPrices::LOCAL,
        ..Prices::farm_only()
    }
}

/// Seeds of the sweep (the stale leave shows on some of them), ticks per run (two minutes of play), and how far from every
/// living regional member a bot is when no fight can be on (a leave needs a member within its weapon's reach: 240 units
/// melee, a bow's range and 60 more).
const SEEDS: std::ops::RangeInclusive<u64> = 1..=8;
const TICKS: u32 = 2_400;
const FAR_U: f64 = 600.;

/// What one seed's run did with its leaves.
#[derive(Debug, Default)]
struct Run {
    /// Ticks at which a leave (Sustain on a camp) ended for another goal while the bot held `ready_hp` or more: the
    /// hand-over the note starts from.
    handed_over: Vec<u32>,
    /// Leaves adopted from another goal that the note's mechanism explains: `(tick, hp, nearest regional member, goal
    /// before, camp left)`. A leave the death model called lost names the camp the bot was working (a Farm's own camp)
    /// and has a member within reach.
    stale: Vec<(u32, f64, f64, String, u32)>,
}

fn run(seed: u64) -> Run {
    let s = scene(seed, 4, 100., 0.5);
    let prices = prices();
    let (mut sim, me) = world(&s);
    {
        // Declared setup, as `raid_sustain.rs`: two large potions in a checkpoint `Sim::restore` must accept.
        let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
        cp["world"]["players"][0]["potion_large"] = serde_json::json!(2);
        sim = Sim::restore(cp).expect("the declared stock is accepted");
    }
    let start = MatchStart::of(&sim, me, s.seed);
    let bot = Arc::new(Mutex::new(
        RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices),
    ));
    let mut seats = vec![Seat::new(
        &start,
        Box::new(Probe(bot.clone())),
        Box::new(scripted::Idle),
    )];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut r = Run::default();
    let mut before = (GoalKind::Roam, Target::None);
    for tick in 0..TICKS {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        let Some(p) = sim.player(me).filter(|p| p.standing()) else {
            break;
        };
        let hp = p.vitals.hp / p.vitals.max_hp;
        let now = {
            let b = bot.lock().unwrap();
            let (g, _, _) = b.goal();
            (g.kind, g.target.clone())
        };
        if now == before {
            continue;
        }
        let near = sim
            .mobs()
            .iter()
            .filter(|m| m.kind == MobKind::Regional && m.zombie.is_none())
            .filter(|m| m.status == rh_sim::Status::Alive)
            .map(|m| dist(m.core.pos(), p.pos()))
            .fold(f64::INFINITY, f64::min);
        let leaving_before = before.0 == GoalKind::Sustain && matches!(before.1, Target::Camp(_));
        if leaving_before && now.0 != GoalKind::Sustain && hp >= SustainPrices::LOCAL.ready_hp {
            r.handed_over.push(tick);
        }
        if let (GoalKind::Sustain, Target::Camp(left)) = (now.0, &now.1) {
            if before.0 != GoalKind::Sustain {
                let worked = match (&before.0, &before.1) {
                    (GoalKind::Farm, Target::Camp(c)) => Some(*c),
                    _ => None,
                };
                if worked.is_some_and(|c| c != *left) || near > FAR_U {
                    r.stale.push((
                        tick,
                        hp,
                        near,
                        format!("{:?}/{:?}", before.0, before.1),
                        *left,
                    ));
                }
            }
        }
        before = now;
    }
    r
}

/// A leave belongs to the Sustain goal that holds it. A four-member camp is fought at hp 0.5 with two large potions (the
/// bot leaves, drinks, and its hp passes `ready_hp` with a member still in reach, so Farm takes the seat back); from then
/// on no Sustain goal on a camp is adopted from another goal unless it names the camp the bot is working and a member is
/// in reach, as a leave the death model called lost would. On the base the leave of the camp first left comes back over a
/// Farm goal on another camp (the note: `leaving` survives the hand-over and `propose` offers it at every strategy tick
/// while hp is below `ready_hp`, at 30 camp-equivalents a minute, beating every goal and the heal-up).
#[test]
fn a_leave_does_not_outlive_the_sustain_goal_that_held_it() {
    let runs: Vec<(u64, Run)> = SEEDS.map(|seed| (seed, run(seed))).collect();
    let handovers: usize = runs.iter().map(|(_, r)| r.handed_over.len()).sum();
    assert!(
        handovers >= 3,
        "the scene hands a leave over at hp {} or more {handovers} times in all, fewer than 3: it does not exercise the \
         note: {runs:#?}",
        SustainPrices::LOCAL.ready_hp
    );
    let stale: Vec<_> = runs
        .iter()
        .filter(|(_, r)| !r.stale.is_empty())
        .map(|(seed, r)| (*seed, &r.stale))
        .collect();
    assert!(
        stale.is_empty(),
        "a Sustain goal on a camp was adopted from another goal, naming a camp the bot was not working (or with no \
         regional member within {FAR_U} u); (seed, [(tick, hp, nearest member, goal before, camp)]): {stale:#?}; every \
         run: {runs:#?}"
    );
}

/// `SustainPrices::LOCAL` copies four numbers of the sim by hand; a sim retune must show here, not as a quietly wrong
/// death model. (Green on the base: the packet adds the guard, not a fix.)
#[test]
fn the_sustain_prices_match_the_sim_numbers_they_copy() {
    let p = SustainPrices::LOCAL;
    let d = &rh_sim::potions::DEFINITION;
    assert_eq!(p.large_share, d.large_share, "large potion share of max hp");
    assert_eq!(p.small_share, d.small_share, "small potion share of max hp");
    assert_eq!(
        p.heal_seconds * 1_000.,
        d.heal_ms,
        "the seconds a potion's heal runs"
    );
    assert_eq!(
        p.regen_per_second,
        rh_sim::constants::PLAYER_REGEN_SHARE_OUT,
        "the hp share a second the sim regenerates out of combat"
    );
}
