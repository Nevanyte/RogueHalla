//! P-RAID-S-fix2 oracle, live scene (docs/backlog/packets/P-RAID-S-fix2.md). Read-only for the implementer.
//!
//! A seat holding Smite must not cast it at a camp's elite from outside the camp's leash band: the elite it wakes
//! chases to the band's edge, gives up and walks home to heal (`mob_ai.rs` `fight`), so the cast (220 damage, a 20 s
//! cooldown) is wasted. It casts once it stands where the elite can reach it, or when the elite is already fighting
//! at its side (within `DEFEND_U`).
//!
//! Scene: lab seed 7, camp 50 (the Gaoler's camp: radius 96, 11 territory cells, elite spot (4304, 752)), the seat
//! started at (4423, 525) where `tests/raid_reach.rs` traces it: 256 u from the Gaoler, far outside the band and
//! inside Smite's 450 u. The build is that of `raid_reach_fix.rs` plus the Smite award. Every command goes through
//! `validate` + `Sim::submit`; the Smite casts are read off the seat's own commands.
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{
    drive_seats_with, feed_seats, scripted, InputKind, InputPreview, InputRefusal,
    InputRequirements, MatchStart, Policy, PolicyFinish, PolicyInput, PolicySpec, PolicyStats,
    Seat,
};
use rh_sim::mapdata::patience::leash_ok;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::quests::board::catalog::RewardId;
use rh_sim::trusted_build::TrustedBuild;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, Observation, Placement, PublicCommand, Secrets, SightCache,
    Sim, SimConfig, Vec2, WeaponId,
};
use std::sync::{Arc, Mutex};

const SEED: u64 = 7;
const CAMP: u32 = 50;
const START: (f64, f64) = (4423., 525.);
const TICKS: u32 = 2_400;
/// `exec/farm.rs` `DEFEND_U`: a mob engaged this close to the seat has reached it.
const DEFEND_U: f64 = 96.;

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

/// 36 stacks of the stats the normal camps pay (as `raid_reach_fix.rs`), plus the Smite award.
fn build(probe: &Sim) -> TrustedBuild {
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
        rewards: vec![RewardId::Smite],
        ..Default::default()
    }
}

/// `raid_reach_fix.rs`'s elite prices: rewards zeroed and the elite death table cut to the Executioner row, so the
/// Gaoler is priced by the executor's fallback and the seat takes the fight (a scene control, not a behaviour under test).
fn prices() -> Prices {
    let mut p = Prices {
        reward_default: 0.,
        reward_rows: &[],
        ..Prices::LOCAL
    };
    p.elite.kind_death = &[("executioner", [0.656, 0.089, 0.022, 0.011])];
    p
}

/// A seat's `RaidBot` that logs every `Cast` it proposes (as `raid_spells.rs`'s `Spy`).
struct Spy {
    bot: RaidBot,
    casts: Arc<Mutex<Vec<PublicCommand>>>,
}

impl Policy for Spy {
    fn input_requirements(&self) -> InputRequirements {
        self.bot.input_requirements()
    }
    fn input_kind(&self) -> InputKind {
        self.bot.input_kind()
    }
    fn check_input(&self, input: InputPreview<'_>) -> Result<(), InputRefusal> {
        self.bot.check_input(input)
    }
    fn decide_input_into(
        &mut self,
        input: PolicyInput<'_>,
        out: &mut Vec<PublicCommand>,
    ) -> Result<(), InputRefusal> {
        let before = out.len();
        let r = self.bot.decide_input_into(input, out);
        self.casts.lock().unwrap().extend(
            out[before..]
                .iter()
                .filter(|c| matches!(c, PublicCommand::Cast { .. }))
                .copied(),
        );
        r
    }
    fn spec(&self) -> PolicySpec {
        self.bot.spec()
    }
    fn stats(&self) -> Option<PolicyStats> {
        self.bot.stats()
    }
    fn reset(&mut self, m: &MatchStart) {
        self.bot.reset(m)
    }
    fn finish(&mut self, result: Option<&rh_sim::RunResult>) -> Option<PolicyFinish> {
        self.bot.finish(result)
    }
    fn decide(&mut self, obs: &Observation) -> Vec<PublicCommand> {
        self.bot.decide(obs)
    }
}

#[derive(Debug, Default)]
struct Out {
    /// (tick, seat-to-elite distance) of every Smite the seat sent while outside the band with no elite within DEFEND_U.
    from_outside: Vec<(u32, f64)>,
    /// (tick, seat-to-elite distance) of every Smite the seat sent from inside the band or with an elite beside it.
    from_inside: Vec<(u32, f64)>,
    /// Ticks the seat stood inside the band (the scene's premise: it gets there).
    ticks_in_band: u32,
    seat_died_at: Option<u32>,
    fault: bool,
}

fn run(weapon: WeaponId) -> Out {
    let arena = lab_arena(SEED);
    let secrets = Secrets::from_seed(SEED);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(arena.centre(), weapon),
    );
    let build = build(&probe);
    let start = Vec2::new(START.0, START.1);
    let mut sim = Sim::reset_active(config(), arena.clone(), secrets, &placement(start, weapon));
    let me = sim.players()[0].id;
    rh_sim::trusted_build::apply(&mut sim, me, &build).expect("the declared build");
    let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
    cp["world"]["players"][0]["potion_large"] = serde_json::json!(2);
    sim = Sim::restore(cp).expect("the declared stock is accepted");
    let casts = Arc::new(Mutex::new(Vec::new()));
    let policy = Spy {
        bot: RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices()),
        casts: casts.clone(),
    };
    let ms = MatchStart::of(&sim, me, SEED);
    let mut seats = vec![Seat::new(&ms, Box::new(policy), Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let data = arena.map.layers().expect("layers").data().clone();
    let def = &data.camps[CAMP as usize];
    let home = {
        let s = &def.spots[usize::try_from(def.elite_spot).expect("camp 50 has an elite spot")];
        Vec2::new(s.x, s.y)
    };
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut o = Out::default();
    for tick in 0..TICKS {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        let sent: Vec<PublicCommand> = std::mem::take(&mut *casts.lock().unwrap());
        if let Some(p) = sim.player(me).filter(|p| p.standing()) {
            let at = p.pos();
            let in_band = leash_ok(&data, CAMP, home, at, 0.);
            let elite_gap = sim
                .mobs()
                .iter()
                .filter(|m| m.camp == CAMP && m.elite && m.status == rh_sim::Status::Alive)
                .map(|m| dist(m.core.pos(), at))
                .fold(f64::MAX, f64::min);
            if in_band {
                o.ticks_in_band += 1;
            }
            for _ in &sent {
                // "Engaged within DEFEND_U": read as an elite beside the seat (the percept's engaged flag is a
                // subset of it, so this exemption is the looser one).
                if in_band || elite_gap <= DEFEND_U {
                    o.from_inside.push((tick, elite_gap));
                } else {
                    o.from_outside.push((tick, elite_gap));
                }
            }
        }
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        if sim.player(me).filter(|p| p.standing()).is_none() {
            o.seat_died_at = Some(tick);
            break;
        }
    }
    o.fault = seats[0].fault.is_some();
    println!("RUN {weapon:?}: {o:?}");
    o
}

fn assert_smite_only_where_the_elite_can_reach(weapon: WeaponId) {
    let o = run(weapon);
    assert!(!o.fault, "no seat fault");
    assert!(
        o.from_outside.is_empty(),
        "{weapon:?}: {} Smite cast(s) sent from outside camp 50's band with no elite within {DEFEND_U} u (tick, \
         distance to the elite): {:?} ({o:?})",
        o.from_outside.len(),
        o.from_outside
    );
    assert!(
        !o.from_inside.is_empty(),
        "{weapon:?}: the seat never cast Smite from inside the band, so the test proves nothing ({o:?})"
    );
}

/// RED at `coord/int-88`: the seat casts at the first think, 256 u from the Gaoler and outside its band.
#[test]
fn a_wand_seat_casts_smite_at_the_camp_50_elite_only_from_where_it_can_reach() {
    assert_smite_only_where_the_elite_can_reach(WeaponId::Wand);
}

/// RED at `coord/int-88`.
#[test]
fn a_bow_seat_casts_smite_at_the_camp_50_elite_only_from_where_it_can_reach() {
    assert_smite_only_where_the_elite_can_reach(WeaponId::Bow);
}

/// The rule is for every weapon: a spell's range does not depend on the weapon in hand.
#[test]
fn a_sword_seat_casts_smite_at_the_camp_50_elite_only_from_where_it_can_reach() {
    assert_smite_only_where_the_elite_can_reach(WeaponId::Sword);
}
