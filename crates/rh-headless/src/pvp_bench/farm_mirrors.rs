//! Compare two earned farm kits as the same faction with the same signature weapon.
//! Each JSONL pair plays on both sides, plus a second exchange using the faction Wish.
use super::{config, CombatPace, TIME_LIMIT_MS};
use rh_sim::abilities::Slot;
use rh_sim::arenas::practice_arena;
use rh_sim::cards::CardId;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::quests::board::catalog::RewardId;
use rh_sim::trusted_build::{apply, TrustedBuild};
use rh_sim::{validate, Arena, FactionId, Placement, RawCommand, Secrets, Sim, Vec2, WeaponId};
use serde::{Deserialize, Serialize};
use std::io::{self, BufRead, Write};
use std::sync::Arc;

#[derive(Default, Deserialize)]
#[serde(deny_unknown_fields)]
struct FarmKit {
    stacks: Vec<(MobTypeId, u64)>,
    clears: u16,
    #[serde(default)]
    cards: Vec<CardId>,
    #[serde(default)]
    rewards: Vec<RewardId>,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Pair {
    id: String,
    seed: u64,
    faction: FactionId,
    weapon: WeaponId,
    companion: bool,
    #[serde(default)]
    follow_displacement: bool,
    target: FarmKit,
    other: FarmKit,
}

fn scene(
    arena: &Arc<Arena>,
    pair: &Pair,
    target_seat: usize,
    quest_origin: Option<Vec2>,
) -> Result<Sim, String> {
    let mut cfg = config(25, CombatPace::Raid);
    cfg.mob_mode = rh_sim::mob_runtime::MobMode::Off;
    cfg.initial_loot = false;
    cfg.wish_lock_ms = 0.;
    let origin = if let Some(origin) = quest_origin {
        cfg.quests = true;
        cfg.lifecycle = rh_sim::ArenaMode::Source(rh_sim::ArenaRules {
            plan_seconds: 0.,
            first_patience_seconds: Some(300.),
            coliseum_schedule: None,
            ..Default::default()
        });
        cfg.hazards = rh_sim::terrain::Mode::Off;
        origin
    } else {
        Vec2::new(400., 320.)
    };
    let seats: [_; 2] = std::array::from_fn(|i| Placement {
        profile: format!("mirror-{i}"),
        faction: pair.faction,
        bot: false,
        at: Vec2::new(origin.x + 40. * i as f64, origin.y),
        hp: None,
        weapon0: Some(pair.weapon),
        weapon1: None,
    });
    let mut sim = Sim::reset_active(cfg, arena.clone(), Secrets::from_seed(pair.seed), &seats);
    let kit = |i| {
        if i == target_seat {
            &pair.target
        } else {
            &pair.other
        }
    };
    for i in 0..2 {
        let build = TrustedBuild {
            stacks: kit(i).stacks.clone(),
            cards: kit(i).cards.iter().map(|&id| (id, 1)).collect(),
            rewards: kit(i).rewards.clone(),
            ..Default::default()
        };
        let id = sim.players()[i].id;
        apply(&mut sim, id, &build).map_err(|reason| format!("invalid earned farm: {reason:?}"))?;
    }
    // Carry the credited clears for Holy's Demon Blood passive. A commander comparison starts with
    // one available corpse per owner, as in the original Plague B farm-route proof.
    let mut cp = serde_json::to_value(&sim).map_err(|e| e.to_string())?;
    for i in 0..2 {
        cp["world"]["players"][i]["camps_cleared"] = kit(i).clears.into();
    }
    if pair.companion {
        if pair.faction != FactionId::Plague {
            return Err("companions require Plague".into());
        }
        cp["world"]["plague_corpses"] = serde_json::Value::Array(
            (0..2)
                .map(|i| {
                    let owner = &sim.players()[i];
                    // A hatched egg leaves a consumable corpse even while its previous owner is standing.
                    serde_json::json!({"player": sim.players()[1-i].id, "x": owner.body.x,
                "y": owner.body.y, "owner_hatched": true})
                })
                .collect(),
        );
    }
    let mut sim = Sim::restore(cp).map_err(|e| e.to_string())?;
    if pair.companion {
        sim.step();
        if !sim
            .players()
            .iter()
            .all(|p| sim.mobs().iter().any(|m| m.owner == Some(p.id)))
        {
            return Err("the commander mirror did not produce both companions".into());
        }
    }
    Ok(sim)
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
enum Verdict {
    Target,
    Other,
    Simultaneous,
    Timeout,
}

#[derive(Default)]
struct Measures {
    landed_casts: u32,
    ttk_ms: Option<u32>,
    hp_margin: f64,
    target_quest_uses: u32,
}

fn exchange(mut sim: Sim, target_seat: usize, wishes: bool, pair: &Pair) -> (Verdict, Measures) {
    let mut first_hit = [None; 2];
    let mut measures = Measures::default();
    let mut events = Vec::new();
    let ids = [sim.players()[0].id, sim.players()[1].id];
    let step_ms = sim.tick_ms();
    for step in 0..TIME_LIMIT_MS / step_ms {
        // Re-aim in both seats. The pursuit screen re-closes the gap after a displacement.
        if step % (250 / step_ms) == 0 {
            for i in 0..2 {
                let me = &sim.players()[i];
                let enemy = &sim.players()[1 - i];
                let pos = enemy.pos();
                let a = rh_sim::math::atan2(pos.y - me.body.y, pos.x - me.body.x);
                let cast = wishes
                    && me.stocks.wish_ready_at <= sim.server_time()
                    && (me.faction() != FactionId::Holy || me.vitals.hp <= 0.5 * me.vitals.max_hp);
                let attack = validate(RawCommand::Attack { a, t: None }).unwrap();
                let gap = rh_sim::math::hypot(me.body.x - pos.x, me.body.y - pos.y);
                let follow = pair.follow_displacement && gap > 96.;
                let chosen = if i == target_seat {
                    &pair.target
                } else {
                    &pair.other
                };
                let quest_aim = wishes
                    && chosen.rewards.iter().any(|r| {
                        !matches!(
                            r.kind(),
                            rh_sim::quests::board::catalog::RewardKind::Permanent
                        ) && (*r != RewardId::Stasis || me.vitals.hp < 0.25 * me.vitals.max_hp)
                    })
                    && me.stocks.q_ready_at <= sim.server_time();
                if follow {
                    let point = RawCommand::Move {
                        x: pos.x,
                        y: pos.y,
                        seq: None,
                    };
                    sim.submit(ids[i], validate(point).unwrap()).unwrap();
                }
                sim.submit(ids[i], attack).unwrap();
                if cast {
                    let wish = RawCommand::Cast {
                        slot: Slot::Wish,
                        x: pos.x,
                        y: pos.y,
                        id: Some(ids[1 - i]),
                        t: None,
                        seq: None,
                    };
                    sim.submit(ids[i], validate(wish).unwrap()).unwrap();
                }
                if quest_aim {
                    let cmd = RawCommand::Cast {
                        slot: Slot::Q,
                        x: pos.x,
                        y: pos.y,
                        id: Some(ids[1 - i]),
                        t: None,
                        seq: None,
                    };
                    sim.submit(ids[i], validate(cmd).unwrap()).unwrap();
                }
            }
        }
        events.clear();
        sim.step_into(&mut events);
        measures.target_quest_uses += events.iter().filter(|e|
            matches!(e, rh_sim::Event::Toast { to: Some(id), text } if *id == ids[target_seat] && text.starts_with("Used "))).count() as u32;
        measures.landed_casts += events
            .iter()
            .filter(|event| matches!(event, rh_sim::Event::CastLanded { .. }))
            .count() as u32;
        // The same step may resolve the living winner. A resolved seat is no longer
        // "standing", so only actual death events decide a simultaneous kill.
        let died = |id| {
            events.iter().any(
                |event| matches!(event, rh_sim::Event::Death { id: victim, .. } if *victim == id),
            )
        };
        for (i, &id) in ids.iter().enumerate() {
            if first_hit[i].is_none()
                && sim.landed_hits().iter().any(|hit| {
                    hit.victim == id && hit.amount > 0. && hit.by.is_some_and(|by| by != id)
                })
            {
                first_hit[i] = Some(step);
            }
        }
        let frac = |i: usize| {
            let v = &sim.players()[i].vitals;
            v.hp / v.max_hp
        };
        measures.hp_margin = frac(target_seat) - frac(1 - target_seat);
        let t = !died(ids[target_seat]);
        let o = !died(ids[1 - target_seat]);
        let (outcome, start) = match (t, o) {
            (false, false) => (
                Verdict::Simultaneous,
                first_hit.iter().flatten().min().copied(),
            ),
            (true, false) => (Verdict::Target, first_hit[1 - target_seat]),
            (false, true) => (Verdict::Other, first_hit[target_seat]),
            (true, true) => continue,
        };
        measures.ttk_ms = start.map(|at| (step - at) * step_ms);
        return (outcome, measures);
    }
    (Verdict::Timeout, measures)
}

#[derive(Serialize)]
struct Outcomes {
    id: String,
    weapon_only: [Verdict; 2],
    with_wishes: [Verdict; 2],
    landed_wishes: [u32; 2],
    target_hp_margin: [f64; 2],
    target_quest_uses: [u32; 2],
    victim_contact_to_death_ms: [Option<u32>; 2],
}

fn compare(arena: &Arc<Arena>, pair: Pair, quest_origin: Option<Vec2>) -> Result<Outcomes, String> {
    let mut out = Outcomes {
        id: pair.id.clone(),
        weapon_only: [Verdict::Timeout; 2],
        with_wishes: [Verdict::Timeout; 2],
        landed_wishes: [0; 2],
        target_hp_margin: [0.; 2],
        target_quest_uses: [0; 2],
        victim_contact_to_death_ms: [None; 2],
    };
    for seat in 0..2 {
        let sim = scene(arena, &pair, seat, quest_origin)?;
        out.weapon_only[seat] = exchange(sim.clone(), seat, false, &pair).0;
        let (outcome, measures) = exchange(sim, seat, true, &pair);
        out.with_wishes[seat] = outcome;
        out.target_hp_margin[seat] = measures.hp_margin;
        out.target_quest_uses[seat] = measures.target_quest_uses;
        out.landed_wishes[seat] = measures.landed_casts;
        out.victim_contact_to_death_ms[seat] = measures.ttk_ms;
    }
    Ok(out)
}

pub(super) fn run(quest_arena: bool) -> Result<(), String> {
    let arena = if quest_arena {
        super::reward_cases::arena()?
    } else {
        Arc::new(practice_arena())
    };
    let origin = if quest_arena {
        Some(super::reward_cases::open_origin(&arena)?)
    } else {
        None
    };
    let mut out = io::BufWriter::new(io::stdout().lock());
    for (i, line) in io::stdin().lock().lines().enumerate() {
        let read = line.map_err(|e| e.to_string())?;
        let pair = serde_json::from_str(&read).map_err(|e| format!("pair line {}: {e}", i + 1))?;
        let result =
            compare(&arena, pair, origin).map_err(|e| format!("pair line {}: {e}", i + 1))?;
        serde_json::to_writer(&mut out, &result).map_err(|e| e.to_string())?;
        writeln!(out).map_err(|e| e.to_string())?;
    }
    out.flush().map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn granted_turret_casts_and_changes_both_seats_in_a_clear_quest_arena() {
        let arena = super::super::reward_cases::arena().unwrap();
        let origin = super::super::reward_cases::open_origin(&arena).unwrap();
        let make_pair = |rewards| Pair {
            id: "quest control".into(),
            seed: 1,
            faction: FactionId::Fire,
            weapon: WeaponId::Sword,
            companion: false,
            follow_displacement: false,
            target: FarmKit {
                rewards,
                ..Default::default()
            },
            other: FarmKit::default(),
        };
        let baseline = compare(&arena, make_pair(vec![]), Some(origin)).unwrap();
        assert_eq!(baseline.target_quest_uses, [0; 2]);
        assert!(baseline
            .victim_contact_to_death_ms
            .iter()
            .all(Option::is_some));
        let reward = compare(&arena, make_pair(vec![RewardId::Turret]), Some(origin)).unwrap();
        assert_eq!(reward.with_wishes, [Verdict::Target; 2]);
        assert!(reward.target_quest_uses.into_iter().all(|uses| uses > 0));
        assert!(
            reward.target_hp_margin.into_iter().sum::<f64>()
                > baseline.target_hp_margin.into_iter().sum::<f64>()
        );
    }

    #[test]
    fn earned_farm_changes_winner_in_both_seats_and_commander_starts_with_zombies() {
        let mut pair = Pair {
            id: "control".into(),
            seed: 1,
            faction: FactionId::Fire,
            weapon: WeaponId::Sword,
            target: FarmKit {
                stacks: vec![(MobTypeId::EmberImp, 250), (MobTypeId::IceGolem, 250)],
                clears: 12,
                ..Default::default()
            },
            other: FarmKit::default(),
            companion: false,
            follow_displacement: false,
        };
        let arena = Arc::new(practice_arena());
        pair.faction = FactionId::Plague;
        pair.companion = true;
        scene(&arena, &pair, 0, None).unwrap();
        pair.faction = FactionId::Fire;
        pair.companion = false;
        let results = compare(&arena, pair, None).unwrap();
        assert_eq!(results.weapon_only, [Verdict::Target; 2]);
        assert_eq!(results.with_wishes, [Verdict::Target; 2]);
        assert!(results.landed_wishes.iter().all(|&n| n > 0));
        assert!(results
            .victim_contact_to_death_ms
            .iter()
            .all(|t| t.is_some_and(|ms| ms > 0 && ms < TIME_LIMIT_MS)));
    }
}
