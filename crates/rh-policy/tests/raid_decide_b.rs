//! P-RAID-Q3b oracle (loose proof, docs/backlog/packets/P-RAID-Q3b.md): a `RaidBot` seated as a HUMAN (`bot: false`) on the
//! live content world accepts the two decision quests of P-RAID-Q3's second half, Two Banners and Trade Up, and plays them
//! to their end. Every scene runs on a table that prices only the quest under test (the way `raid_decide.rs`'s Bargain scenes
//! isolate their executor), with a scripted control that plays the same quest from the sim's own observation, so a red
//! RaidBot is the bot's and a scene that cannot be played is the scene's.
//!
//! What is pinned, in the packet's words: Two Banners is accepted, a branch is chosen within a bounded time (branch 0: the
//! packet's section 9.1, branch 1 is the same route plus the walk back), the quest is turned in and paid inside a tick budget;
//! a table that makes every camp fight deadly makes the seat decline it; Trade Up is accepted, banks at least one stage on
//! every seed and never lets its timer run out on progress; where the route fits it reaches stage three; a seat attacked
//! in either quest and losing the fight leaves. The reading of a seat's quest state is its own view plus
//! `Sim::quest_attempts` (the lab's probe); nothing here is a game rule.
use rh_policy::bots::raid::values::{DeathModel, Prices, SustainPrices};
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::quests::{board::catalog::QuestId, public_rules};
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId,
    PublicCommand, QuestAttemptProbe, Secrets, SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::sync::Arc;

/// Seeds that deal `two_banners` (the old brief's list, re-measured on `coord/int-87`: seeds 1-24 below).
const BANNER_SEEDS: [u64; 6] = [1, 2, 6, 9, 12, 20];
/// Seeds that deal `trade_up` (the old brief's list, re-measured).
const TRADE_SEEDS: [u64; 6] = [7, 8, 11, 12, 16, 18];
/// The seat starts this far from the giver (as `raid_decide.rs`).
const START_DIST: f64 = 160.;
/// The view and the probe are read every this many ticks.
const READ_EVERY: u32 = 4;
/// A tick is 50 ms.
const SECOND: u32 = 20;

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
        // These fixed-map, fixed-seed scenes were measured before the spread giver layout.
        quest_board: rh_sim::statics::QuestBoard::Legacy,
        ..SimConfig::live_content()
    }
}

fn arena_of(seed: u64) -> Arc<Arena> {
    rh_sim::quests::board::map_admission::ensure(Arc::new(map()), Secrets::from_seed(seed))
        .expect("a board-admitted map")
}

fn placement(at: Vec2) -> [Placement; 1] {
    placement_hp(at, None)
}

fn placement_hp(at: Vec2, hp: Option<f64>) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at,
        hp,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }]
}

fn observe(sim: &Sim, observer: &mut ItemObserver) -> Observation {
    let mut obs = Observation::default();
    observer
        .prepare_into(sim, &[], &mut obs, None)
        .expect("an observation")
        .commit();
    obs
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// Where the board puts `def`'s giver on `seed`, or `None` when the seed does not deal it.
fn giver_of(seed: u64, def: &str) -> Option<Vec2> {
    let arena = arena_of(seed);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        Secrets::from_seed(seed),
        &placement(arena.centre()),
    );
    let me = probe.players()[0].id;
    let obs = observe(&probe, &mut ItemObserver::new(me));
    obs.quests?
        .rows
        .into_iter()
        .find(|r| r.def == def)
        .map(|r| Vec2::new(r.giver.x, r.giver.y))
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

/// A point `START_DIST` from `giver` with a straight floor line to it.
fn start_near(arena: &Arena, giver: Vec2) -> Vec2 {
    for k in 0..16 {
        let a = f64::from(k) * std::f64::consts::TAU / 16.;
        let start = Vec2::new(
            giver.x + rh_sim::math::cos(a) * START_DIST,
            giver.y + rh_sim::math::sin(a) * START_DIST,
        );
        if floor_line(arena, start, giver) {
            return start;
        }
    }
    panic!("no floor start beside {giver:?}");
}

/// A sim, its seat and the observer a scripted control reads and sends through.
struct Script {
    sim: Sim,
    me: PlayerId,
    observer: ItemObserver,
    events: Vec<Event>,
    seed: u64,
}

impl Script {
    fn new(seed: u64, start: Vec2) -> Self {
        let sim = Sim::reset_active(
            config(),
            arena_of(seed),
            Secrets::from_seed(seed),
            &placement(start),
        );
        let me = sim.players()[0].id;
        Self {
            sim,
            me,
            observer: ItemObserver::new(me),
            events: Vec::new(),
            seed,
        }
    }

    /// The seat `START_DIST` from `def`'s giver on `seed`, or `None` when the seed does not deal `def`.
    fn near_giver(seed: u64, def: &str) -> Option<Self> {
        let giver = giver_of(seed, def)?;
        Some(Self::new(seed, start_near(&arena_of(seed), giver)))
    }

    /// As `near_giver`, with the seat starting at `share` of its max hp (declared setup: `Placement::hp`).
    fn near_giver_hurt(seed: u64, def: &str, share: f64) -> Option<Self> {
        let giver = giver_of(seed, def)?;
        let start = start_near(&arena_of(seed), giver);
        let max_hp = Self::new(seed, start).sim.players()[0].vitals.max_hp;
        let sim = Sim::reset_active(
            config(),
            arena_of(seed),
            Secrets::from_seed(seed),
            &placement_hp(start, Some(max_hp * share)),
        );
        let me = sim.players()[0].id;
        Some(Self {
            sim,
            me,
            observer: ItemObserver::new(me),
            events: Vec::new(),
            seed,
        })
    }

    fn step(&mut self) -> Observation {
        self.sim.step_into(&mut self.events);
        self.events.clear();
        observe(&self.sim, &mut self.observer)
    }

    fn send(&mut self, proposal: PublicCommand) {
        if let Ok(cmd) = self.observer.resolve(&self.sim, proposal) {
            let _ = self.sim.submit(self.me, cmd);
        }
    }
}

/// What the seat's own view and the probe show at one read.
#[derive(Clone, Debug)]
struct Read {
    tick: u32,
    alive: bool,
    /// The quest's attempt (`None` before the accept, and once the timer, a death or an abandon ended it).
    attempt: Option<QuestAttemptProbe>,
    /// The quest row's objective points in the view (empty before a Two Banners branch is chosen).
    objectives: Vec<Vec2>,
    giver: Option<Vec2>,
    /// Stacks of the Penitent mob (Trade Up's reward).
    penitent: u64,
}

fn read(sim: &Sim, me: PlayerId, tick: u32, quest: QuestId, obs: &Observation) -> Option<Read> {
    let view = obs.quests.as_ref()?;
    let def = quest.definition().id;
    let row = view.rows.iter().find(|r| r.def == def);
    let player = sim.player(me)?;
    Some(Read {
        tick,
        alive: player.standing(),
        attempt: sim
            .quest_attempts(me)
            .into_iter()
            .find(|a| a.quest == quest),
        objectives: row
            .map(|r| {
                view.objectives
                    .iter()
                    .filter(|o| o.quest == r.handle)
                    .map(|o| Vec2::new(o.at.x, o.at.y))
                    .collect()
            })
            .unwrap_or_default(),
        giver: row.map(|r| Vec2::new(r.giver.x, r.giver.y)),
        penitent: player.farm.count(MobTypeId::Penitent),
    })
}

/// What a seated policy leaves behind.
#[derive(Debug)]
struct Run {
    reads: Vec<Read>,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
}

fn raid(prices: Prices) -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices))
}

/// Seat `policy` for the fighter of `s` and step `ticks`.
fn seat(mut s: Script, quest: QuestId, policy: Box<dyn Policy>, ticks: u32) -> Run {
    let start = MatchStart::of(&s.sim, s.me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&s.sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut reader = ItemObserver::new(s.me);
    let mut reads = Vec::new();
    for tick in 0..ticks {
        drive_seats_with(&mut s.sim, &mut seats, &s.events, &mut sight, |_, _| {});
        s.events.clear();
        s.sim.step_into(&mut s.events);
        feed_seats(&s.sim, &mut seats);
        if tick % READ_EVERY == 0 {
            if let Some(r) = read(&s.sim, s.me, tick, quest, &observe(&s.sim, &mut reader)) {
                reads.push(r);
            }
        }
    }
    Run {
        reads,
        stats: seats[0].stats.clone(),
        fault: seats[0].fault.clone(),
    }
}

/// The table of a scene: the quest under test is worth its `LOCAL` row, every other quest and augment is worth 0 (camps,
/// chests, roaming, sustain and the death model stay `LOCAL`), so the scene proves the decision and not that the quest
/// outbids the rest of the map (`raid_decide.rs`'s `bargain_prices`).
fn only(rows: &'static [(&'static str, f64)]) -> Prices {
    Prices {
        reward_rows: rows,
        augment_rows: &[],
        reward_default: 0.,
        augment_default: 0.,
        ..Prices::LOCAL
    }
}
fn banners_prices() -> Prices {
    only(&[("two_banners", 3.0)])
}
fn trade_prices() -> Prices {
    only(&[("trade_up", 5.6)])
}

/// A table on which every camp fight is a near-certain death (`DeathModel` of `exec/farm.rs` `death_chance`): the chance rises
/// at once, steeply, and a camp counts as many members as it holds. The quest's rows and everything else stay as `only` has them.
fn deadly_camps(base: Prices) -> Prices {
    Prices {
        death: DeathModel {
            fight_weight: 1_000.,
            death_per_ratio: 10.,
            safe_ratio: 0.,
            ..base.death
        },
        ..base
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Scripted controls
// ---------------------------------------------------------------------------------------------------------------

/// The control of Two Banners: no policy. Accept at the giver, choose `branch`, walk to the outpost, take the rival banner
/// on branch 1, walk to the end; returns the tick the quest was paid (the attempt `completed`), if it was.
fn scripted_banners(seed: u64, branch: u8, budget: u32) -> Option<u32> {
    let mut s = Script::near_giver(seed, "two_banners")?;
    for tick in 0..budget {
        let obs = s.step();
        if let Some(r) = read(&s.sim, s.me, tick, QuestId::TwoBanners, &obs) {
            if r.attempt.is_some_and(|a| a.completed) {
                return Some(tick);
            }
        }
        if tick % 5 != 0 {
            continue;
        }
        let (Some(view), Some(owner)) = (obs.quests.as_ref(), obs.me.as_ref()) else {
            continue;
        };
        let Some(row) = view.rows.iter().find(|r| r.def == "two_banners") else {
            continue;
        };
        let at = Vec2::new(owner.x, owner.y);
        let offered = |action: u8, target: u8| {
            view.actions
                .iter()
                .any(|a| a.quest == row.handle && a.action == action && a.target == target)
        };
        let proposal = if view.pending.contains(&row.handle) {
            match view
                .actions
                .iter()
                .find(|a| a.quest == row.handle && a.action == 8)
            {
                Some(a) => PublicCommand::QuestInteract {
                    handle: row.handle,
                    action: 8,
                    target: a.target,
                },
                None => continue,
            }
        } else if !row.owner_accepted {
            if row.can_accept {
                PublicCommand::QuestAccept { handle: row.handle }
            } else {
                PublicCommand::Move {
                    x: row.giver.x,
                    y: row.giver.y,
                    seq: None,
                }
            }
        } else if offered(2, branch) {
            PublicCommand::QuestInteract {
                handle: row.handle,
                action: 2,
                target: branch,
            }
        } else if offered(3, 0) {
            PublicCommand::QuestInteract {
                handle: row.handle,
                action: 3,
                target: 0,
            }
        } else if let Some(o) = view
            .objectives
            .iter()
            .filter(|o| o.quest == row.handle)
            .min_by(|a, b| dist(a.at, at).total_cmp(&dist(b.at, at)))
        {
            PublicCommand::Move {
                x: o.at.x,
                y: o.at.y,
                seq: None,
            }
        } else {
            continue;
        };
        s.send(proposal);
    }
    None
}

/// What the Trade Up control saw.
#[derive(Debug, Default)]
struct TradeControl {
    /// The tick of each barter (progress 1, 2, 3).
    barters: Vec<u32>,
    /// The tick the quest was paid, and the Penitent stacks then.
    paid: Option<(u32, u64)>,
    /// The attempt's progress in the read that first shows it paid (it stays: the oracle's `banked` reads it).
    progress_at_pay: u8,
    /// The attempt's deadline in sim ms and the sim ms of the accept.
    deadline: f64,
    accepted_at: Option<u32>,
}

/// The control of Trade Up: accept, walk to each trader's current place, barter when offered; at `stop_after` barters it cashes
/// out when offered (3 means never: the third barter pays by itself).
fn scripted_trade(seed: u64, stop_after: u8, budget: u32) -> Option<TradeControl> {
    let mut s = Script::near_giver(seed, "trade_up")?;
    let mut out = TradeControl::default();
    for tick in 0..budget {
        let obs = s.step();
        if let Some(r) = read(&s.sim, s.me, tick, QuestId::TradeUp, &obs) {
            if let Some(a) = &r.attempt {
                if a.accepted && out.accepted_at.is_none() {
                    out.accepted_at = Some(tick);
                    out.deadline = a.deadline;
                }
                while out.barters.len() < usize::from(a.progress) {
                    out.barters.push(tick);
                }
                if a.completed && out.paid.is_none() {
                    out.paid = Some((tick, r.penitent));
                    out.progress_at_pay = a.progress;
                }
            }
        }
        if out.paid.is_some() {
            break;
        }
        if tick % 5 != 0 {
            continue;
        }
        let (Some(view), Some(owner)) = (obs.quests.as_ref(), obs.me.as_ref()) else {
            continue;
        };
        let Some(row) = view.rows.iter().find(|r| r.def == "trade_up") else {
            continue;
        };
        let at = Vec2::new(owner.x, owner.y);
        let progress = out.barters.len() as u8;
        let action = |action: u8| {
            view.actions
                .iter()
                .find(|a| a.quest == row.handle && a.action == action)
        };
        let proposal = if view.pending.contains(&row.handle) {
            match action(8) {
                Some(a) => PublicCommand::QuestInteract {
                    handle: row.handle,
                    action: 8,
                    target: a.target,
                },
                None => continue,
            }
        } else if !row.owner_accepted {
            if row.can_accept {
                PublicCommand::QuestAccept { handle: row.handle }
            } else {
                PublicCommand::Move {
                    x: row.giver.x,
                    y: row.giver.y,
                    seq: None,
                }
            }
        } else if progress >= stop_after && action(4).is_some() {
            PublicCommand::QuestInteract {
                handle: row.handle,
                action: 4,
                target: 0,
            }
        } else if let Some(a) = action(3) {
            PublicCommand::QuestInteract {
                handle: row.handle,
                action: 3,
                target: a.target,
            }
        } else if let Some(o) = view
            .objectives
            .iter()
            .filter(|o| o.quest == row.handle)
            .min_by(|a, b| dist(a.at, at).total_cmp(&dist(b.at, at)))
        {
            PublicCommand::Move {
                x: o.at.x,
                y: o.at.y,
                seq: None,
            }
        } else {
            continue;
        };
        s.send(proposal);
    }
    Some(out)
}

// ---------------------------------------------------------------------------------------------------------------
// A fight lost while holding the quest
// ---------------------------------------------------------------------------------------------------------------

/// A member within this of the fighter reaches it.
const REACH: f64 = 160.;
/// The scripted walk into the camp hands the seat over once its hp is at or under this share with a member in reach.
const START_HP: f64 = 0.3;

/// What a lost-fight scene shows.
#[derive(Debug)]
struct Fight {
    /// How far the seat moved straight away from the nearest living member in the second after the handover (negative: toward).
    away_1s: f64,
    /// The seat was accepted in the quest at the handover.
    holding: bool,
}

/// Ticks the bot plays after the handover (5 s).
const FIGHT_TICKS: u32 = 5 * SECOND;

/// The normal camp with exactly four regional members of `seed` nearest to `giver`: its index and centroid (the quest's
/// timer, Trade Up's 95 s, runs during the scripted walk there).
fn four_member_camp(seed: u64, giver: Vec2) -> Option<(u32, Vec2, f64)> {
    use rh_sim::mob_runtime::MobKind;
    let probe = Sim::reset_active(
        config(),
        arena_of(seed),
        Secrets::from_seed(seed),
        &placement(arena_of(seed).centre()),
    );
    let mut best: Option<(u32, Vec2, f64)> = None;
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
        if at.len() == 4 {
            let n = 4.;
            let centre = Vec2::new(
                at.iter().map(|p| p.x).sum::<f64>() / n,
                at.iter().map(|p| p.y).sum::<f64>() / n,
            );
            // Within the walk Trade Up's timer allows, the camp this Holy fighter matches worst, then the nearest: a fight
            // the death model calls lost (a Holy seat beats a Sanctum camp and never turns).
            let plan = rh_policy::bots::planning::PlanMap::from_arena(&arena_of(seed));
            let region = rh_policy::bots::planning::zone_faction(plan.camps[ci].zone);
            let duel = rh_policy::bots::planning::matchup_share(FactionId::Holy, region, false);
            let key = |c: Vec2, _d: f64| dist(c, giver);
            if region != FactionId::Holy
                && dist(centre, giver) <= 2_500.
                && best.is_none_or(|(_, b, bd)| key(centre, duel) < key(b, bd))
            {
                best = Some((ci as u32, centre, duel));
            }
        }
    }
    best
}

/// The living regional members of `camp`.
fn members(sim: &Sim, camp: u32) -> Vec<Vec2> {
    use rh_sim::mob_runtime::MobKind;
    sim.mobs()
        .iter()
        .filter(|m| {
            m.camp == camp
                && m.kind == MobKind::Regional
                && m.zombie.is_none()
                && m.status == rh_sim::Status::Alive
        })
        .map(|m| m.core.pos())
        .collect()
}

/// The scripted prelude then a `RaidBot`: the seat accepts `def` at its giver (the script), then walks into the four-member
/// camp of the seed until the death model calls the fight lost with a member in reach, then `RaidBot` takes over for
/// `FIGHT_TICKS`. `None` when the script cannot reach that state on this seed (the prelude is a script: not the bot's).
fn lost_fight(seed: u64, def: &str, quest: QuestId, prices: Prices) -> Option<Fight> {
    let mut s = Script::near_giver_hurt(seed, def, START_HP)?;
    let (camp, centre, duel) = four_member_camp(seed, giver_of(seed, def)?)?;
    let mut accepted = false;
    for tick in 0..4_000u32 {
        let obs = s.step();
        if tick % 5 != 0 {
            continue;
        }
        let (Some(view), Some(owner)) = (obs.quests.as_ref(), obs.me.as_ref()) else {
            continue;
        };
        let row = view.rows.iter().find(|r| r.def == def)?;
        let at = Vec2::new(owner.x, owner.y);
        if !accepted {
            accepted = row.owner_accepted;
            let proposal = if row.owner_accepted {
                continue;
            } else if row.can_accept {
                PublicCommand::QuestAccept { handle: row.handle }
            } else {
                PublicCommand::Move {
                    x: row.giver.x,
                    y: row.giver.y,
                    seq: None,
                }
            };
            s.send(proposal);
            continue;
        }
        let player = s.sim.player(s.me)?;
        if !player.standing() {
            return None;
        }
        let alive = members(&s.sim, camp);
        let near = alive.iter().any(|m| dist(*m, at) <= REACH);
        // Hp-weighted members left, as `farm.rs` counts them (the sustain oracle's rule): the fight is lost once the death
        // model gives `disengage_death` or worse for finishing it.
        let left: f64 = s
            .sim
            .mobs()
            .iter()
            .filter(|m| m.camp == camp && m.status == rh_sim::Status::Alive && m.zombie.is_none())
            .map(|m| m.core.vitals.hp / m.core.vitals.max_hp)
            .sum();
        let share = player.vitals.hp / player.vitals.max_hp;
        let lost =
            rh_policy::bots::raid::exec::farm::death_chance(share, duel, left, &prices.death)
                >= SustainPrices::LOCAL.disengage_death;
        if near && lost {
            break;
        }
        s.send(PublicCommand::Move {
            x: centre.x,
            y: centre.y,
            seq: None,
        });
    }
    let holding = s
        .sim
        .quest_attempts(s.me)
        .iter()
        .any(|a| a.quest == quest && a.accepted);
    eprintln!(
        "seed {seed}: handover at sim tick {}, camp {:.0} u from the giver, seat {:?} camp {centre:?}, attempts {:?}",
        s.sim.tick(),
        dist(centre, giver_of(seed, def)?),
        s.sim.player(s.me).map(|p| p.pos()),
        s.sim.quest_attempts(s.me)
    );
    let at0 = s.sim.player(s.me).filter(|p| p.standing())?.pos();
    let nearest0 = members(&s.sim, camp)
        .into_iter()
        .min_by(|a, b| dist(*a, at0).total_cmp(&dist(*b, at0)))?;
    if dist(nearest0, at0) > REACH {
        return None;
    }
    let start = MatchStart::of(&s.sim, s.me, s.seed);
    let mut seats = vec![Seat::new(&start, raid(prices), Box::new(scripted::Idle))];
    seats[0].bind_fx(&s.sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut away_1s = f64::NEG_INFINITY;
    for tick in 0..FIGHT_TICKS {
        drive_seats_with(&mut s.sim, &mut seats, &s.events, &mut sight, |_, _| {});
        s.events.clear();
        s.sim.step_into(&mut s.events);
        feed_seats(&s.sim, &mut seats);
        let Some(p) = s.sim.player(s.me).filter(|p| p.standing()) else {
            continue;
        };
        if tick + 1 == SECOND {
            let d = dist(at0, nearest0).max(1e-6);
            let (ux, uy) = ((at0.x - nearest0.x) / d, (at0.y - nearest0.y) / d);
            let now = p.pos();
            away_1s = (now.x - at0.x) * ux + (now.y - at0.y) * uy;
        }
    }
    Some(Fight { away_1s, holding })
}

/// The seat must have moved this far away from the nearest member one second after the handover (u): a leave is a run at
/// 170 u/s, a stand at a giver or a trader is nothing (`raid_sustain.rs` asks for 30 on its leave scene).
const LEFT_BY: f64 = 30.;
/// Scenes that must be reached holding the quest (the script reaches all six on both seed lists on `coord/int-87`).
const LEAVE_SCENES_FLOOR: usize = 5;
/// Scenes the seat must leave. On the stub (no new stage: Sustain alone) it leaves on 5 of 6 for both quests: Two Banners
/// stays on seed 1 (+1.5 u in a second), Trade Up on seed 16 (-116 u: it walks into the camp). The floor is 4: one
/// scene of headroom under the stub's own count, so a stage that holds a committed walk through the fight (every
/// scene stays) is red, and a stage that costs one more scene is not.
const LEAVE_FLOOR: usize = 4;

fn leave_scenes(seeds: &[u64], def: &str, quest: QuestId, prices: Prices) {
    let mut reached = 0;
    let mut stayed = Vec::new();
    for &seed in seeds {
        let Some(f) = lost_fight(seed, def, quest, prices) else {
            continue;
        };
        eprintln!("seed {seed}: {f:?}");
        if !f.holding {
            continue;
        }
        reached += 1;
        if f.away_1s < LEFT_BY {
            stayed.push((seed, f.away_1s));
        }
    }
    assert!(
        reached >= LEAVE_SCENES_FLOOR,
        "the lost-fight scene was reached holding {def} on {reached} of {} seeds, fewer than {LEAVE_SCENES_FLOOR}",
        seeds.len()
    );
    assert!(
        reached - stayed.len() >= LEAVE_FLOOR,
        "seats holding {def} ran from a lost fight on {} of {reached} scenes, fewer than {LEAVE_FLOOR}; stayed (seed, u moved away in 1 s; need {LEFT_BY}): {stayed:?}",
        reached - stayed.len()
    );
}

/// Guard-shaped (the leave is Sustain's and works on the stub; it turns red if the new stage holds the seat): a seat that holds
/// Two Banners (accepted, no branch yet: it is at the giver's side of the map, far from the outpost) and is attacked by a camp
/// it is losing to leaves it.
#[test]
fn guard_a_seat_holding_two_banners_leaves_a_fight_it_is_losing() {
    leave_scenes(
        &BANNER_SEEDS,
        "two_banners",
        QuestId::TwoBanners,
        banners_prices(),
    );
}

/// The same for Trade Up (accepted, no barter yet).
#[test]
fn guard_a_seat_holding_trade_up_leaves_a_fight_it_is_losing() {
    leave_scenes(&TRADE_SEEDS, "trade_up", QuestId::TradeUp, trade_prices());
}

// ---------------------------------------------------------------------------------------------------------------
// Two Banners
// ---------------------------------------------------------------------------------------------------------------

/// The control (branch 0: giver -> outpost; branch 1: the same and back) pays at tick 492 (428 on seed 9) and 978 (836), from
/// 160 u away, on every seed of `BANNER_SEEDS` (measured on `coord/int-87`, debug build).
/// The bot's budget is three times the branch-0 control: it must find the giver, accept, choose and walk. 1 500 ticks = 75 s.
const BANNER_TICKS: u32 = 1_500;
/// The choice must be made within this many ticks of the accept: the seat stands at the giver from the accept (10 s).
const DECIDE_WITHIN: u32 = 10 * SECOND;
/// Of the six seeds the bot must complete the quest on this many (the control completes all six on both branches).
const BANNER_FLOOR: usize = 5;

/// What a Two Banners run shows.
#[derive(Debug, Default)]
struct Banners {
    accepted: Option<u32>,
    /// The first read at which the view shows the quest's objective: a branch is chosen (the view shows none before).
    decided: Option<u32>,
    /// The seat walked the branch-1 return leg: not ready, and the objective is the giver (`interactions.rs:395`).
    branch_one: bool,
    paid: Option<u32>,
    died: Option<u32>,
}

fn banners_of(run: &Run) -> Banners {
    let mut b = Banners::default();
    for r in &run.reads {
        if !r.alive && b.died.is_none() {
            b.died = Some(r.tick);
        }
        let Some(a) = &r.attempt else { continue };
        if a.accepted && b.accepted.is_none() {
            b.accepted = Some(r.tick);
        }
        if a.accepted && !r.objectives.is_empty() && b.decided.is_none() {
            b.decided = Some(r.tick);
        }
        if a.accepted
            && !a.ready
            && r.giver
                .is_some_and(|g| r.objectives.iter().any(|o| dist(*o, g) < 5.))
        {
            b.branch_one = true;
        }
        if a.completed && b.paid.is_none() {
            b.paid = Some(r.tick);
        }
    }
    b
}

fn banner_run(seed: u64, prices: Prices, ticks: u32) -> Run {
    let s = Script::near_giver(seed, "two_banners")
        .unwrap_or_else(|| panic!("seed {seed} deals no two_banners"));
    seat(s, QuestId::TwoBanners, raid(prices), ticks)
}

/// Guard (green on the stub): the scene is playable. The scripted control completes the quest on both branches on every seed of
/// `BANNER_SEEDS`, branch 0 inside half the bot's budget and branch 1 inside all of it.
#[test]
fn guard_a_scripted_control_completes_two_banners_on_both_branches_on_every_seed() {
    for seed in BANNER_SEEDS {
        let zero = scripted_banners(seed, 0, BANNER_TICKS);
        let one = scripted_banners(seed, 1, 2 * BANNER_TICKS);
        assert!(
            zero.is_some_and(|t| t < BANNER_TICKS / 2),
            "seed {seed}: branch 0 control paid at {zero:?}"
        );
        assert!(
            one.is_some_and(|t| t < BANNER_TICKS),
            "seed {seed}: branch 1 control paid at {one:?}"
        );
    }
}

/// A seat offered Two Banners accepts it, chooses a branch within `DECIDE_WITHIN` of the accept, and the quest is paid inside
/// `BANNER_TICKS`. Every seed's branch is printed (`--nocapture`); the choice is branch 0 on every seed (the packet's section
/// 9.1: branch 1 is the same route plus the walk back, so a price that counts minutes never takes it, and the tie-break
/// of the decision is branch 0). No command the sim refuses.
#[test]
fn raid_bot_accepts_two_banners_chooses_within_a_bounded_time_and_completes_it() {
    let mut completed = Vec::new();
    let mut report = Vec::new();
    for seed in BANNER_SEEDS {
        let run = banner_run(seed, banners_prices(), BANNER_TICKS);
        let b = banners_of(&run);
        eprintln!("seed {seed}: {b:?}");
        report.push((seed, format!("{b:?}")));
        assert_eq!(run.stats.rejected, 0, "seed {seed}: {run:?}");
        assert!(run.fault.is_none(), "seed {seed}: {:?}", run.fault);
        if let Some(accepted) = b.accepted {
            let late = b.decided.is_none_or(|d| d > accepted + DECIDE_WITHIN);
            assert!(
                !late,
                "seed {seed}: accepted at tick {accepted}, branch chosen at {:?}: undecided past {DECIDE_WITHIN} ticks",
                b.decided
            );
            assert!(
                !b.branch_one,
                "seed {seed}: the seat took branch 1 (the same route plus the walk back): {b:?}"
            );
        }
        if b.paid.is_some() {
            completed.push(seed);
        }
    }
    assert!(
        completed.len() >= BANNER_FLOOR,
        "two_banners paid on {completed:?}, fewer than {BANNER_FLOOR} of {} seeds (the control pays on all): {report:?}",
        BANNER_SEEDS.len()
    );
}

/// An idle camp at the outpost is not a fight. On the same seeds the seat accepts the quest on the normal table (a floor
/// of five of the six) and ALSO with every camp fight priced as a near-certain death: the outpost is a camp's centre, its
/// mobs never attack a seat that only walks up and stands (the sim wakes a mob only on damage or a camp mate's death),
/// and the lab's twenty-five Two Banners attempts all completed with no death. The deadly table moves only `DeathModel`.
///
/// Coordinator's oracle-amend (2026-11-23, P-RAID-Q3b-fix): the second half used to demand that the deadly table make the
/// seat DECLINE, which pinned the walk-is-a-fight pricing that packet removes. The test keeps its seeds and its floor.
#[test]
fn a_table_that_makes_every_camp_deadly_does_not_stop_two_banners() {
    let mut normal = Vec::new();
    for seed in BANNER_SEEDS {
        let run = banner_run(seed, banners_prices(), BANNER_TICKS);
        if banners_of(&run).accepted.is_some() {
            normal.push(seed);
        }
    }
    assert!(
        normal.len() >= BANNER_FLOOR,
        "normal table: the seat accepts two_banners on {normal:?}, fewer than {BANNER_FLOOR} of {}",
        BANNER_SEEDS.len()
    );
    let mut deadly = Vec::new();
    for seed in BANNER_SEEDS {
        let run = banner_run(seed, deadly_camps(banners_prices()), BANNER_TICKS);
        if banners_of(&run).accepted.is_some() {
            deadly.push(seed);
        }
    }
    assert!(
        deadly.len() >= BANNER_FLOOR,
        "deadly camps: the seat accepts two_banners on {deadly:?}, fewer than {BANNER_FLOOR} of {}: an idle camp at the outpost is priced as a fight",
        BANNER_SEEDS.len()
    );
}

// ---------------------------------------------------------------------------------------------------------------
// Trade Up
// ---------------------------------------------------------------------------------------------------------------

/// The control barters at ticks about 251, 541 and 781 (the three traders, from 160 u from the giver) and the third barter
/// pays by itself on every seed of `TRADE_SEEDS`; the deadline is 95 900 ms after the sim started (about
/// 1 900 ticks): **the route fits the timer with 56 s to spare on all six seeds** (measured on `coord/int-87`). A
/// seed where it does not fit does not exist among the seeds that deal the quest (1-24), and a doctored timer is not allowed,
/// so the "cash out because the timer will not allow the next trader" case cannot be a live scene; the live scenes pin
/// what must hold anyway (a stage banked, no progress lost to the timer, stage three where it fits).
/// The bot's run is 2 400 ticks (120 s): longer than the timer, so a timer that ran out on progress shows.
const TRADE_TICKS: u32 = 2_400;
/// The seat must have accepted within this many ticks (15 s: a 160 u walk and a think).
const ACCEPT_WITHIN: u32 = 15 * SECOND;
/// Seats that accepted and lived must bank a stage on every one; at least this many of the six seeds must be such (a seat
/// that dies on the route is the death term, not the executor's fault: but fewer than four is a seat that never plays it).
const TRADE_PLAYED_FLOOR: usize = 4;

/// What a Trade Up run shows.
#[derive(Debug, Default)]
struct Trade {
    accepted: Option<u32>,
    /// The highest barter progress seen.
    progress: u8,
    paid: Option<u32>,
    /// The barter progress at settlement identifies the banked stage.
    /// The bot's ending stacks can also include normal farming.
    banked: u8,
    died: Option<u32>,
    /// Progress was seen, nothing was paid, and the seat never died: the timer (or an abandon) took it.
    lost: bool,
}

impl Trade {
    /// The stage banked: 1, 2 or 3, 0 for none.
    fn stage(&self) -> u8 {
        self.banked
    }
}

fn trade_of(run: &Run) -> Trade {
    let mut t = Trade::default();
    for r in &run.reads {
        if !r.alive && t.died.is_none() {
            t.died = Some(r.tick);
        }
        let Some(a) = &r.attempt else { continue };
        if a.accepted && t.accepted.is_none() {
            t.accepted = Some(r.tick);
        }
        t.progress = t.progress.max(a.progress);
        if a.completed && t.paid.is_none() {
            t.paid = Some(r.tick);
            t.banked = a.progress;
        }
    }
    t.lost = t.progress > 0 && t.paid.is_none() && t.died.is_none();
    t
}

fn trade_run(seed: u64, prices: Prices) -> Run {
    let s = Script::near_giver(seed, "trade_up")
        .unwrap_or_else(|| panic!("seed {seed} deals no trade_up"));
    seat(s, QuestId::TradeUp, raid(prices), TRADE_TICKS)
}

/// Every stage pays its published amount in a real scripted turn-in.
#[test]
fn guard_a_scripted_control_plays_trade_up_at_each_cash_out_stage() {
    let payouts = public_rules().trade_up_stacks;
    for seed in TRADE_SEEDS {
        for (index, expected) in payouts.into_iter().enumerate() {
            let stage = (index + 1) as u8;
            let run = scripted_trade(seed, stage, 3_000).expect("a seed that deals trade_up");
            let (tick, stacks) = run.paid.unwrap_or((u32::MAX, 0));
            assert_eq!(stacks, expected, "seed {seed}, stage {stage}: {run:?}");
            assert_eq!(run.progress_at_pay, stage, "seed {seed}: {run:?}");
            assert_eq!(
                run.barters.len(),
                usize::from(stage),
                "seed {seed}: {run:?}"
            );
            if stage == 3 {
                assert!(
                    f64::from(tick) * 50. < run.deadline - 30_000.,
                    "seed {seed}: the full route paid at tick {tick}, deadline {} ms: {run:?}",
                    run.deadline
                );
            }
        }
    }
}

/// A seat offered Trade Up accepts it (within `ACCEPT_WITHIN`) and banks at least one stage, on every seed where it plays the
/// quest and lives, with no run in which progress was lost to the timer; and it reaches stage three on at least one seed.
/// The stage banked per seed is printed (`--nocapture`). No command the sim refuses.
#[test]
fn raid_bot_plays_trade_up_banks_a_stage_and_never_loses_progress_to_its_timer() {
    let mut played = 0;
    let mut three = Vec::new();
    let mut report = Vec::new();
    for seed in TRADE_SEEDS {
        let run = trade_run(seed, trade_prices());
        let t = trade_of(&run);
        eprintln!("seed {seed}: stage {} {t:?}", t.stage());
        report.push((seed, t.stage()));
        assert_eq!(run.stats.rejected, 0, "seed {seed}: {run:?}");
        assert!(run.fault.is_none(), "seed {seed}: {:?}", run.fault);
        assert!(
            !t.lost,
            "seed {seed}: progress {} was lost to the timer, nothing paid: {t:?}",
            t.progress
        );
        let Some(accepted) = t.accepted else {
            continue;
        };
        assert!(
            accepted <= ACCEPT_WITHIN,
            "seed {seed}: accepted at tick {accepted}, after {ACCEPT_WITHIN}"
        );
        if t.died.is_some() {
            continue;
        }
        played += 1;
        assert!(
            t.stage() >= 1 && t.paid.is_some(),
            "seed {seed}: accepted at {accepted} and lived, but banked stage {} (progress {}): {t:?}",
            t.stage(),
            t.progress
        );
        if t.stage() == 3 {
            three.push(seed);
        }
    }
    assert!(
        played >= TRADE_PLAYED_FLOOR,
        "the seat accepted and lived on {played} of {} seeds, fewer than {TRADE_PLAYED_FLOOR}: {report:?}",
        TRADE_SEEDS.len()
    );
    assert!(
        !three.is_empty(),
        "no seed reached stage three though the route fits the timer on all of them: {report:?}"
    );
}

/// The price follows the death. With every camp fight priced as a near-certain death the seat banks no more than it does on the
/// normal table, on every seed, and still never loses progress to the timer: it declines, or goes on only while the next
/// stage is worth it and cashes out otherwise. The first half (the normal table banks a stage on at least four of the
/// six seeds) is what is red on the stub.
#[test]
fn a_deadly_table_banks_no_more_than_the_normal_table_and_never_loses_progress() {
    let mut banked_normal = 0;
    for seed in TRADE_SEEDS {
        let normal = trade_of(&trade_run(seed, trade_prices()));
        let deadly = trade_of(&trade_run(seed, deadly_camps(trade_prices())));
        if normal.stage() >= 1 {
            banked_normal += 1;
        }
        assert!(
            deadly.stage() <= normal.stage() || normal.died.is_some(),
            "seed {seed}: deadly camps banked stage {}, the normal table {}: {deadly:?} / {normal:?}",
            deadly.stage(),
            normal.stage()
        );
        assert!(
            !deadly.lost,
            "seed {seed}: with deadly camps progress {} was lost to the timer: {deadly:?}",
            deadly.progress
        );
    }
    assert!(
        banked_normal >= TRADE_PLAYED_FLOOR,
        "the normal table banks a stage on {banked_normal} of {} seeds, fewer than {TRADE_PLAYED_FLOOR}",
        TRADE_SEEDS.len()
    );
}
