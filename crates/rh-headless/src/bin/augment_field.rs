//! Read-only elite reward and native card growth trace for stock 40 Hz raids.
use rh_headless::{
    balance_lab::{match_setup, Options, SeatPolicy, SEATS},
    Env,
};
use rh_sim::cards::{native, CardId};
use rh_sim::reward_chests::{OptionId, RewardChestObserver, RewardChestView, DEFINITION};
use rh_sim::{Event, Phase, PlayerId, Sim, Vec2, WeaponId};
use serde::Serialize;
use serde_json::json;
use std::collections::{BTreeMap, BTreeSet};

type ChestKey = (u64, u64, u64);

fn chest_key(at: Vec2, expiry: f64) -> ChestKey {
    (expiry.to_bits(), at.x.to_bits(), at.y.to_bits())
}

fn camp_spot(sim: &Sim, camp: u32) -> Option<Vec2> {
    let c = sim.arena().map.layers()?.data().camps.get(camp as usize)?;
    let tile = rh_sim::constants::TILE;
    let point = if sim.arena().map.is_solid(
        (c.chest.x / tile).floor() as i64,
        (c.chest.y / tile).floor() as i64,
    ) {
        Vec2::new(c.x, c.y)
    } else {
        Vec2::new(c.chest.x, c.chest.y)
    };
    let snap = |x: f64| (f64::from(x as f32) * 8. + 0.5).floor() / 8.;
    Some(Vec2::new(snap(point.x), snap(point.y)))
}

#[derive(Default, Serialize)]
struct Offer {
    options: Vec<String>,
    visible: BTreeSet<PlayerId>,
    could_choose: BTreeSet<PlayerId>,
}

#[derive(Serialize)]
struct Drop {
    camp: u32,
    at_s: f64,
    seen: Option<Offer>,
}

#[derive(Serialize)]
struct Pick {
    seat: PlayerId,
    chest: u32,
    source: &'static str,
    option: Option<String>,
    age_s: f64,
}

#[derive(Serialize)]
struct Growth {
    seat: PlayerId,
    card: String,
    source: &'static str,
    acquired_s: f64,
    standing_s: f64,
    ended_s: f64,
    still_held: bool,
    trigger_exposure: Option<TriggerExposure>,
    rows: Vec<Row>,
}

#[derive(Default, Serialize)]
struct TriggerExposure {
    required_weapon_standing_s: f64,
    qualifying_events: u32,
}

fn exposure_weapon(card: CardId) -> Option<WeaponId> {
    match card {
        CardId::Bastion => Some(WeaponId::Shield),
        CardId::Calluses => Some(WeaponId::Fists),
        _ => None,
    }
}

#[derive(Serialize)]
struct Row {
    kind: native::ReadoutKind,
    start: f64,
    min: f64,
    peak: f64,
    last: f64,
    cap: f64,
    rises: u32,
    falls: u32,
}

impl Row {
    fn new(r: &native::Readout) -> Self {
        Self {
            kind: r.kind,
            start: r.value,
            min: r.value,
            peak: r.value,
            last: r.value,
            cap: r.max,
            rises: 0,
            falls: 0,
        }
    }

    fn sample(&mut self, r: &native::Readout) {
        self.rises += u32::from(r.value > self.last);
        self.falls += u32::from(r.value < self.last);
        self.min = self.min.min(r.value);
        self.peak = self.peak.max(r.value);
        self.last = r.value;
    }
}

fn source(view: &RewardChestView, drops: &BTreeMap<ChestKey, Drop>) -> &'static str {
    if drops.contains_key(&chest_key(view.at, view.expires_at)) || view.first_pick.is_some() {
        "elite"
    } else if view
        .options
        .iter()
        .any(|o| matches!(o.id, OptionId::Kill(_)))
    {
        "trophy"
    } else {
        "other"
    }
}

fn play(seed: u64) -> Result<serde_json::Value, String> {
    let opts = Options {
        max_ticks: 72_000,
        seats: (0..SEATS).map(|s| (s, SeatPolicy::Raid)).collect(),
        ..Options::default()
    };
    let (mut cfg, mut agents) = match_setup(&opts, seed)?;
    agents[0].bot = false;
    cfg.sim.tick_ms = 25;
    let mut env = Env::new(cfg, agents, seed);
    let ids: Vec<_> = env.sim.players().iter().map(|p| p.id).collect();
    let mut observers: Vec<_> = ids.iter().copied().map(RewardChestObserver::new).collect();
    let mut elites = BTreeMap::new();
    let mut latest_drop_expiry = 0.;
    let mut drops: BTreeMap<_, Drop> = BTreeMap::new();
    let mut discovered_picks = BTreeSet::new();
    let mut views = Vec::new();
    let mut picks = Vec::new();
    let mut growth: Vec<Growth> = Vec::new();
    let mut held: std::collections::HashMap<(PlayerId, CardId), usize> =
        std::collections::HashMap::new();
    let mut saw_active = false;
    let mut previous_standing = vec![false; ids.len()];
    let progress = loop {
        // The death step may remove a mob. Index elite ids beforehand; late respawns may add a new id.
        if saw_active {
            for mob in env.sim.mobs().iter().filter(|m| m.elite) {
                elites.insert(mob.core.id, mob.camp);
            }
        }
        let p = env.advance(&[]);
        let sim = &env.sim;
        saw_active |= sim.phase() == Phase::Active;
        let now = sim.server_time();
        let age = (now - sim.raid_started_at()).max(0.) / 1000.;
        let step_s = f64::from(sim.tick_ms()) / 1000.;
        let mut taken = Vec::new();
        let mut dropped = false;
        let mut trigger_events = Vec::new();
        for e in env.last_events() {
            match e {
                Event::Block { id, .. } => trigger_events.push((*id, CardId::Bastion)),
                Event::Hit {
                    by: Some(by),
                    weapon: Some(WeaponId::Fists),
                    passive: false,
                    amount,
                    ..
                } if *amount > 0. => trigger_events.push((*by, CardId::Calluses)),
                Event::Death { id, by, .. } if by.is_some() => {
                    if let Some((camp, at)) = elites
                        .get(id)
                        .and_then(|&c| camp_spot(sim, c).map(|at| (c, at)))
                    {
                        let expiry = now + DEFINITION.lifetime_ms;
                        drops.insert(
                            chest_key(at, expiry),
                            Drop {
                                camp,
                                at_s: age,
                                seen: None,
                            },
                        );
                        latest_drop_expiry = expiry;
                        dropped = true;
                    }
                }
                Event::ChestTaken { chest, by, pick } => taken.push((*chest, *by, *pick)),
                _ => {}
            }
        }
        let mut named = BTreeMap::new();
        if dropped || !taken.is_empty() || (now < latest_drop_expiry && p.tick % 10 == 0) {
            for (&id, observer) in ids.iter().zip(&mut observers) {
                let prepared = observer
                    .prepare_into(sim, &mut views, &mut None)
                    .map_err(|e| format!("{e:?}"))?;
                prepared.commit();
                for view in &views {
                    let key = chest_key(view.at, view.expires_at);
                    if let Some(d) = drops.get_mut(&key) {
                        let seen = d.seen.get_or_insert_with(|| Offer {
                            options: view
                                .options
                                .iter()
                                .map(|o| o.id.as_str().into_owned())
                                .collect(),
                            ..Offer::default()
                        });
                        seen.visible.insert(id);
                        if view.can_choose {
                            seen.could_choose.insert(id);
                        }
                    }
                    for (i, option) in view.options.iter().enumerate() {
                        if option.taken_by == Some(id) && discovered_picks.insert((id, key, i)) {
                            named
                                .entry((id, i))
                                .or_insert_with(Vec::new)
                                .push((source(view, &drops), option.id));
                        }
                    }
                }
            }
        }
        let mut acquired = BTreeMap::new();
        for (chest, id, index) in taken {
            let candidates = usize::try_from(index)
                .ok()
                .and_then(|i| named.remove(&(id, i)));
            let matched = candidates
                .filter(|c| c.len() == 1)
                .and_then(|c| c.into_iter().next());
            if let Some((source, OptionId::Card(card))) = matched {
                acquired.insert((id, card.as_str()), source);
            }
            picks.push(Pick {
                seat: id,
                chest,
                source: matched.map_or("unresolved", |m| m.0),
                option: matched.map(|m| m.1.as_str().into_owned()),
                age_s: age,
            });
        }
        if saw_active {
            for (seat, &id) in ids.iter().enumerate() {
                let player = sim.player(id).expect("seat");
                let readouts = native::readouts(sim, id);
                held.retain(|&(owner, card), &mut index| {
                    if owner != id || player.build.count(card) != 0 {
                        return true;
                    }
                    growth[index].still_held = false;
                    growth[index].ended_s = age;
                    false
                });
                for card in player.build.cards() {
                    let rows = || readouts.rows().iter().filter(|r| r.card == card);
                    let already_held = held.contains_key(&(id, card));
                    let index = *held.entry((id, card)).or_insert_with(|| {
                        growth.push(Growth {
                            seat: id,
                            card: card.as_str().into(),
                            acquired_s: age,
                            standing_s: 0.,
                            source: acquired
                                .get(&(id, card.as_str()))
                                .copied()
                                .unwrap_or("unresolved"),
                            ended_s: age,
                            still_held: true,
                            trigger_exposure: exposure_weapon(card)
                                .map(|_| TriggerExposure::default()),
                            rows: rows().map(Row::new).collect(),
                        });
                        growth.len() - 1
                    });
                    let g = &mut growth[index];
                    if already_held && previous_standing[seat] {
                        g.standing_s += step_s;
                    }
                    if let Some(exposure) = g.trigger_exposure.as_mut() {
                        if already_held
                            && player.standing()
                            && exposure_weapon(card) == Some(player.active_weapon())
                        {
                            exposure.required_weapon_standing_s += step_s;
                        }
                    }
                    g.ended_s = age;
                    for (sample, row) in rows().zip(&mut g.rows) {
                        row.sample(sample);
                    }
                }
                previous_standing[seat] = player.standing();
            }
        }
        for pair in trigger_events {
            if let Some(exposure) = held
                .get(&pair)
                .and_then(|&i| growth[i].trigger_exposure.as_mut())
            {
                exposure.qualifying_events += 1;
            }
        }
        if p.terminated || p.truncated {
            break p;
        }
    };
    Ok(json!({
        "seed":seed, "ticks":progress.tick, "terminated":progress.terminated, "truncated":progress.truncated,
        "state_hash":format!("{:016x}", env.sim.state_hash()), "drops":drops.into_values().collect::<Vec<_>>(),
        "picks":picks, "growth":growth,
    }))
}

fn main() -> Result<(), String> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    let [lo, hi] = args.as_slice() else {
        return Err("usage: augment_field FIRST_SEED EXCLUSIVE_END".into());
    };
    let lo = lo.parse::<u64>().map_err(|_| "invalid first seed")?;
    let hi = hi.parse::<u64>().map_err(|_| "invalid exclusive end")?;
    if lo >= hi || hi > i64::MAX as u64 {
        return Err("empty or invalid seed range".into());
    }
    println!(
        "{}",
        json!({
            "schema":"augment-field/1", "tick_ms":25, "content_hash":format!("{:016x}", rh_sim::content::content_hash()),
            "native":CardId::NATIVE.iter().map(|id| id.as_str()).collect::<Vec<_>>(),
        })
    );
    for seed in lo..hi {
        println!("{}", play(seed)?);
    }
    Ok(())
}
