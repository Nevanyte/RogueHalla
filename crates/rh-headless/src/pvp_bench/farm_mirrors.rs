//! Compare two earned farm kits as the same faction with the same signature weapon.
//! Each JSONL pair plays on both sides, plus a second exchange using the faction Wish.
use super::{config, CombatPace, TIME_LIMIT_MS};
use rh_sim::abilities::Slot;
use rh_sim::arenas::practice_arena;
use rh_sim::mob_catalog::MobTypeId;
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
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Pair {
    id: String,
    seed: u64,
    faction: FactionId,
    weapon: WeaponId,
    companion: bool,
    target: FarmKit,
    other: FarmKit,
}

fn scene(arena: &Arc<Arena>, pair: &Pair, target_seat: usize) -> Result<Sim, String> {
    let mut cfg = config(25, CombatPace::Raid);
    cfg.mob_mode = rh_sim::mob_runtime::MobMode::Off;
    cfg.initial_loot = false;
    cfg.wish_lock_ms = 0.;
    let seats: [_; 2] = std::array::from_fn(|i| Placement {
        profile: format!("mirror-{i}"),
        faction: pair.faction,
        bot: false,
        at: Vec2::new(400. + 40. * i as f64, 320.),
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

fn exchange(mut sim: Sim, target_seat: usize, wishes: bool) -> (Verdict, u32, Option<u32>) {
    let mut first_hit = [None; 2];
    let mut landed_wishes = 0;
    let mut events = Vec::new();
    let ids = [sim.players()[0].id, sim.players()[1].id];
    let step_ms = sim.tick_ms();
    for step in 0..TIME_LIMIT_MS / step_ms {
        // Re-aim after displacements. Both fighters hold their ground and use the same commands.
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
            }
        }
        events.clear();
        sim.step_into(&mut events);
        landed_wishes += events
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
        return (
            outcome,
            landed_wishes,
            start.map(|at| (step - at) * step_ms),
        );
    }
    (Verdict::Timeout, landed_wishes, None)
}

#[derive(Serialize)]
struct Outcomes {
    id: String,
    weapon_only: [Verdict; 2],
    with_wishes: [Verdict; 2],
    landed_wishes: [u32; 2],
    victim_contact_to_death_ms: [Option<u32>; 2],
}

fn compare(arena: &Arc<Arena>, pair: Pair) -> Result<Outcomes, String> {
    let mut out = Outcomes {
        id: pair.id.clone(),
        weapon_only: [Verdict::Timeout; 2],
        with_wishes: [Verdict::Timeout; 2],
        landed_wishes: [0; 2],
        victim_contact_to_death_ms: [None; 2],
    };
    for seat in 0..2 {
        let sim = scene(arena, &pair, seat)?;
        out.weapon_only[seat] = exchange(sim.clone(), seat, false).0;
        (
            out.with_wishes[seat],
            out.landed_wishes[seat],
            out.victim_contact_to_death_ms[seat],
        ) = exchange(sim, seat, true);
    }
    Ok(out)
}

pub(super) fn run() -> Result<(), String> {
    let arena = Arc::new(practice_arena());
    let mut out = io::BufWriter::new(io::stdout().lock());
    for (i, line) in io::stdin().lock().lines().enumerate() {
        let read = line.map_err(|e| e.to_string())?;
        let pair = serde_json::from_str(&read).map_err(|e| format!("pair line {}: {e}", i + 1))?;
        let result = compare(&arena, pair).map_err(|e| format!("pair line {}: {e}", i + 1))?;
        serde_json::to_writer(&mut out, &result).map_err(|e| e.to_string())?;
        writeln!(out).map_err(|e| e.to_string())?;
    }
    out.flush().map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
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
            },
            other: FarmKit::default(),
            companion: false,
        };
        let arena = Arc::new(practice_arena());
        pair.faction = FactionId::Plague;
        pair.companion = true;
        scene(&arena, &pair, 0).unwrap();
        pair.faction = FactionId::Fire;
        pair.companion = false;
        let results = compare(&arena, pair).unwrap();
        assert_eq!(results.weapon_only, [Verdict::Target; 2]);
        assert_eq!(results.with_wishes, [Verdict::Target; 2]);
        assert!(results.landed_wishes.iter().all(|&n| n > 0));
        assert!(results
            .victim_contact_to_death_ms
            .iter()
            .all(|t| t.is_some_and(|ms| ms > 0 && ms < TIME_LIMIT_MS)));
    }
}
