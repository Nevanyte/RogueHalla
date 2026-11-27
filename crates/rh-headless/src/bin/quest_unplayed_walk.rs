//! Offline generated-map walk and acceptance probe for quests without a RaidBot executor.
//! No fixture teleport, no scripted damage, and no gameplay edits.
use rh_headless::{balance_lab, Agent, Env};
use rh_sim::constants::{PLAYER_RADIUS, PLAYER_SPEED};
use rh_sim::pathfind::Pathfinder;
use rh_sim::quest_view::QuestObserver;
use rh_sim::quests::board::catalog::QuestId;
use rh_sim::{validate, Phase, RawCommand, Sim, TileMap, Vec2};
use serde_json::{json, Value};

const UNPLAYED: [QuestId; 9] = [
    QuestId::TheWarden,
    QuestId::BountyHunter,
    QuestId::TheBurningCrown,
    QuestId::EliteSlayer,
    QuestId::ABoneToPick,
    QuestId::FeedOrHunt,
    QuestId::RimeHunt,
    QuestId::WhispersInTheBrush,
    QuestId::Gatecrasher,
];
// quests/board/mod.rs FIRST_BOARD_DEF; immutable giver provenance ids.
const BOARD_DEF_OFFSET: u64 = 256;

fn distance(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}
fn path_distance(pf: &mut Pathfinder, map: &TileMap, a: Vec2, b: Vec2) -> Option<f64> {
    if distance(a, b) <= 1.0 {
        return Some(0.);
    }
    let path = pf.find_path(map, a, b, PLAYER_RADIUS);
    if path.is_empty() {
        return None;
    }
    let length = std::iter::once(a)
        .chain(path.iter().copied())
        .zip(path.iter().copied())
        .map(|(x, y)| distance(x, y))
        .sum();
    assert!(
        length + 1e-6 >= distance(a, b),
        "path shorter than straight line"
    );
    Some(length)
}
fn ordered_objectives(tokens: &[Vec2], q: QuestId) -> Vec<Vec2> {
    if q == QuestId::FeedOrHunt {
        // Interactions store independent item bits: all three can be gathered
        // before one creature visit and three successive native feed actions.
        assert_eq!(tokens.len(), 4);
        let mut order: Vec<Vec2> = tokens.iter().skip(1).copied().collect();
        order.push(tokens[0]);
        order
    } else {
        tokens.to_vec()
    }
}
fn route(
    pf: &mut Pathfinder,
    map: &TileMap,
    giver: Vec2,
    tokens: &[Vec2],
    q: QuestId,
) -> Option<f64> {
    let order = ordered_objectives(tokens, q);
    let mut total = 0.;
    let mut prev = giver;
    for &at in &order {
        total += path_distance(pf, map, prev, at)?;
        prev = at;
    }
    total += path_distance(pf, map, prev, giver)?;
    Some(total)
}

fn straight_route(giver: Vec2, tokens: &[Vec2], q: QuestId) -> f64 {
    let order = ordered_objectives(tokens, q);
    let mut total = 0.;
    let mut prev = giver;
    for at in order {
        total += distance(prev, at);
        prev = at;
    }
    total + distance(prev, giver)
}
fn walk_accept(
    base: &Sim,
    giver: Vec2,
    quest: QuestId,
) -> (Option<f64>, &'static str, Option<f64>, Option<Sim>) {
    let mut sim = base.clone();
    let id = sim.players()[0].id;
    let mut observer = QuestObserver::new(id);
    let start = sim.server_time();
    if sim
        .submit(
            id,
            validate(RawCommand::Move {
                x: giver.x,
                y: giver.y,
                seq: None,
            })
            .unwrap(),
        )
        .is_err()
    {
        return (None, "move_rejected", None, None);
    }
    for tick in 0..8_000 {
        let prepared = observer.prepare(&sim).expect("quest observation");
        let view = prepared.view.clone();
        prepared.commit();
        if let Some(row) = view.rows.iter().find(|r| r.def == quest.definition().id) {
            if row.can_accept {
                let cmd = observer.resolve_accept(&sim, row.handle);
                if sim.submit(id, cmd).is_err() {
                    return (None, "accept_rejected", None, None);
                }
                sim.step();
                if let Some(attempt) = sim
                    .quest_attempts(id)
                    .into_iter()
                    .find(|a| a.quest == quest && a.accepted)
                {
                    let timer = (attempt.deadline > 0.)
                        .then_some((attempt.deadline - sim.server_time()) / 1000.);
                    return (
                        Some((sim.server_time() - start) / 1000.),
                        "accepted",
                        timer,
                        Some(sim),
                    );
                }
                return (None, "accept_unconfirmed", None, None);
            }
        }
        if !sim.player(id).is_some_and(|p| p.standing()) {
            return (None, "died", None, None);
        }
        if sim.phase() != Phase::Active {
            return (None, "phase_ended", None, None);
        }
        sim.step();
        if tick == 7_999 {
            return (None, "timeout_200s", None, None);
        }
    }
    unreachable!()
}

fn feed_bits(sim: &Sim, offer: u32) -> Option<u8> {
    let cp = serde_json::to_value(sim).expect("checkpoint");
    cp["world"]["quests"]["board"]["attempts"]
        .as_array()?
        .iter()
        .find(|a| a["offer"].as_u64() == Some(u64::from(offer)))?["bits"]
        .as_u64()
        .map(|n| n as u8)
}

fn feed_all_then_deliver(mut sim: Sim, offer: u32, tokens: &[Vec2], giver: Vec2) -> Value {
    use rh_sim::mob_catalog::MobTypeId;
    let id = sim.players()[0].id;
    let start = sim.server_time();
    let deadline = sim
        .quest_attempts(id)
        .into_iter()
        .find(|a| a.quest == QuestId::FeedOrHunt)
        .map(|a| a.deadline)
        .unwrap_or(0.);
    let before = sim.players()[0].farm.count(MobTypeId::EmberImp);
    let mut observer = QuestObserver::new(id);
    let mut actions = 0u32;
    let mut stage = "items";
    let mut reason = "running";
    'route: for target in [1u8, 2, 3, 0, 0, 0] {
        stage = if target == 0 { "feed" } else { "items" };
        let prior_progress = sim
            .quest_attempts(id)
            .into_iter()
            .find(|a| a.quest == QuestId::FeedOrHunt)
            .map_or(0, |a| a.progress);
        let mut completed = false;
        for tick in 0..8_000 {
            if !sim.player(id).is_some_and(|p| p.standing()) {
                reason = "died";
                break 'route;
            }
            if sim.phase() != Phase::Active {
                reason = "phase_ended";
                break 'route;
            }
            if sim
                .quest_attempts(id)
                .into_iter()
                .all(|a| a.quest != QuestId::FeedOrHunt)
            {
                reason = if sim.server_time() >= deadline {
                    "timer"
                } else {
                    "attempt_lost"
                };
                break 'route;
            }
            if target == 0 {
                if sim
                    .quest_attempts(id)
                    .into_iter()
                    .any(|a| a.quest == QuestId::FeedOrHunt && a.progress > prior_progress)
                {
                    completed = true;
                    break;
                }
            } else if feed_bits(&sim, offer).is_some_and(|bits| bits & (1 << target) != 0) {
                completed = true;
                break;
            }
            let prepared = observer.prepare(&sim).expect("quest observation");
            let view = prepared.view.clone();
            prepared.commit();
            let at = if target == 0 {
                let Some(point) = view.objectives.first().map(|o| o.at) else {
                    reason = "creature_missing";
                    break 'route;
                };
                point
            } else {
                tokens[target as usize]
            };
            if view
                .actions
                .iter()
                .any(|a| a.action == 3 && a.target == target)
                && tick % 4 == 0
            {
                let cmd = validate(RawCommand::QuestInteract {
                    quest_id: Some(offer),
                    action: 3,
                    target,
                })
                .unwrap();
                if sim.submit(id, cmd).is_err() {
                    reason = "interact_rejected";
                    break 'route;
                }
                actions += 1;
            } else if tick % 12 == 0 {
                let cmd = validate(RawCommand::Move {
                    x: at.x,
                    y: at.y,
                    seq: None,
                })
                .unwrap();
                if sim.submit(id, cmd).is_err() {
                    reason = "move_rejected";
                    break 'route;
                }
            }
            sim.step();
        }
        if !completed {
            reason = "stage_timeout";
            break;
        }
    }
    if reason == "running" {
        stage = "return";
        for tick in 0..8_000 {
            if !sim.player(id).is_some_and(|p| p.standing()) {
                reason = "died";
                break;
            }
            if sim.phase() != Phase::Active {
                reason = "phase_ended";
                break;
            }
            if sim
                .quest_attempts(id)
                .into_iter()
                .any(|a| a.quest == QuestId::FeedOrHunt && (a.turned_in || a.completed))
            {
                reason = "turned_in";
                break;
            }
            if sim
                .quest_attempts(id)
                .into_iter()
                .all(|a| a.quest != QuestId::FeedOrHunt)
            {
                reason = if sim.server_time() >= deadline {
                    "timer"
                } else {
                    "attempt_lost"
                };
                break;
            }
            if tick % 12 == 0 {
                let cmd = validate(RawCommand::Move {
                    x: giver.x,
                    y: giver.y,
                    seq: None,
                })
                .unwrap();
                if sim.submit(id, cmd).is_err() {
                    reason = "move_rejected";
                    break;
                }
            }
            sim.step();
            if tick == 7_999 {
                reason = "return_timeout";
            }
        }
    }
    let after = sim.players()[0].farm.count(MobTypeId::EmberImp);
    json!({"result":reason,"stage":stage,"elapsed_s":(sim.server_time()-start)/1000.,
        "timer_s":(deadline-start)/1000.,"progress":sim.quest_attempts(id).into_iter()
            .find(|a|a.quest==QuestId::FeedOrHunt).map(|a|a.progress),
        "ember_imp_delta":after.saturating_sub(before),"interact_inputs":actions,
        "item_tokens":tokens.len().saturating_sub(1)})
}
fn main() {
    let args: Vec<_> = std::env::args().skip(1).collect();
    let [lo, hi] = args.as_slice() else {
        eprintln!("usage: quest_unplayed_walk FIRST_SEED EXCLUSIVE_END");
        std::process::exit(2)
    };
    let lo: u64 = lo.parse().expect("first seed");
    let hi: u64 = hi.parse().expect("exclusive end");
    assert!(lo > 0 && hi > lo && hi <= i64::MAX as u64);
    for seed in lo..hi {
        let opts = balance_lab::Options {
            max_ticks: 52_000,
            seats: (0..balance_lab::SEATS)
                .map(|s| (s, balance_lab::SeatPolicy::Raid))
                .collect(),
            ..Default::default()
        };
        let (mut cfg, mut agents) = balance_lab::match_setup(&opts, seed).expect("admitted map");
        cfg.sim.tick_ms = 25;
        agents[0].bot = false;
        agents[0].agent = Agent::External;
        let mut env = Env::new(cfg, agents, seed);
        for _ in 0..6_000 {
            env.advance(&[Vec::new()]);
            if env.sim.phase() == Phase::Active {
                break;
            }
        }
        assert_eq!(env.sim.phase(), Phase::Active);
        let base = &env.sim;
        let cp = serde_json::to_value(base).expect("checkpoint");
        let offers = cp["world"]["quests"]["offers"]
            .as_array()
            .expect("board offers");
        let map = &base.arena().map;
        let from = base.players()[0].core.pos();
        let mut pf = Pathfinder::default();
        for offer in offers {
            let Some(def) = offer["def"].as_u64() else {
                continue;
            };
            let Some(&quest) = def
                .checked_sub(BOARD_DEF_OFFSET)
                .and_then(|i| QuestId::ALL.get(i as usize))
            else {
                continue;
            };
            if !UNPLAYED.contains(&quest) {
                continue;
            }
            let giver: Vec2 = serde_json::from_value(offer["at"].clone()).expect("giver");
            let tokens: Vec<Vec2> =
                serde_json::from_value(offer["tokens"].clone()).expect("tokens");
            let approach = path_distance(&mut pf, map, from, giver);
            let fixed_route = if tokens.is_empty() {
                None
            } else {
                route(&mut pf, map, giver, &tokens, quest)
            };
            let fixed_straight =
                (!tokens.is_empty()).then(|| straight_route(giver, &tokens, quest));
            if let (Some(path), Some(straight)) = (fixed_route, fixed_straight) {
                assert!(
                    path + 1e-6 >= straight,
                    "objective route shorter than straight legs"
                );
            }
            let (actual, accept, accepted_timer, accepted_sim) = walk_accept(base, giver, quest);
            let feed = if quest == QuestId::FeedOrHunt {
                accepted_sim.map(|sim| {
                    feed_all_then_deliver(sim, offer["id"].as_u64().unwrap() as u32, &tokens, giver)
                })
            } else {
                None
            };
            let map_hash = map
                .layers()
                .map(|l| format!("{:08x}", l.legacy_map_hash_u32()));
            let row: Value = json!({"seed":seed,"map_hash":map_hash,"quest":format!("{quest:?}"),
                "content_hash":format!("{:016x}",rh_sim::content::content_hash()),"tick_ms":base.tick_ms(),
                "faction":format!("{:?}",base.players()[0].core.vitals.faction.faction_id().unwrap()),
                "tokens":tokens.len(),"approach_path_u":approach,
                "approach_straight_u":distance(from,giver),
                "approach_to_exact_giver_s_at_base_speed":approach.map(|d|d/PLAYER_SPEED),
                "fixed_objective_path_u":fixed_route,
                "fixed_objective_straight_u":fixed_straight,
                "fixed_point_route_s_at_base_speed":fixed_route.map(|d|d/PLAYER_SPEED),
                "walk_and_accept_s":actual,"accept":accept,"accepted_timer_s":accepted_timer,
                "feed_trial":feed});
            println!("{row}");
        }
    }
}
