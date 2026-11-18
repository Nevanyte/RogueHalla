//! The public snapshot: what anyone may see of the match with no secrets (replays, spectators, results pages).

use super::*;

/// What anyone may see with no secrets: every player's public fields (for replays, spectators and results pages). Never
/// replicate this to a playing client: it ignores line of sight.
#[derive(Clone, Debug, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct PublicPlayer {
    pub effects: u16,
    pub displace_seq: u8,
    pub mark_tier: u8,
    pub brand: u8,
    pub mark_by: Option<PlayerId>,
    pub mark_tax: u8,
    pub id: PlayerId,
    pub name: String,
    pub bot: bool,
    pub faction: FactionId,
    pub x: f64,
    pub y: f64,
    pub facing: f64,
    pub hp: f64,
    pub max_hp: f64,
    /// Widened source-f32 barrier; HP/maxHP retain this diagnostic's native precision.
    pub barrier: f64,
    pub status: Status,
    pub kills: u32,
    pub bounty: u32,
}

#[derive(Clone, Debug, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct PublicSnapshot {
    pub arena: Option<crate::ArenaView>,
    pub mobs: Vec<ActorView>,
    pub camps: Vec<crate::mob_runtime::CampRoster>,
    pub tick: u32,
    pub server_time: f64,
    pub phase: Phase,
    pub winner: Option<String>,
    pub players: Vec<PublicPlayer>,
    pub fields: Vec<crate::status_runtime::FieldView>,
}

/// Checked privileged all-row snapshot. Never deliver this view to a playing client.
pub fn try_public_snapshot(sim: &Sim) -> Result<PublicSnapshot, crate::OfferError> {
    let mut players: Vec<PublicPlayer> = sim
        .players()
        .iter()
        .map(|p| {
            let vitals = public_vitals(p.vitals.hp, p.vitals.max_hp, p.vitals.barrier)?;
            Ok(PublicPlayer {
                effects: p.statuses.effects,
                displace_seq: p.motion.displace_seq,
                mark_tier: crate::systems::sentenced::public_mark_tier(&sim.w, p.id),
                brand: crate::systems::sentenced::public_mark_tier(&sim.w, p.id) * 25,
                mark_by: None,
                mark_tax: crate::systems::sentenced::mark_tax::public_percent(&sim.w, p.id),
                id: p.id,
                name: p.seat.name.clone(),
                bot: p.seat.bot,
                faction: p.faction(),
                x: p.body.x,
                y: p.body.y,
                facing: p.body.facing,
                hp: p.vitals.hp,
                max_hp: p.vitals.max_hp,
                barrier: vitals.barrier,
                status: p.status,
                kills: p.credit.kills,
                bounty: p.credit.bounty,
            })
        })
        .collect::<Result<_, crate::OfferError>>()?;
    if !players.is_sorted_by_key(|p| p.id) {
        players.sort_unstable_by_key(|p| p.id);
    }
    Ok(PublicSnapshot {
        arena: crate::systems::planning::view::arena_view(&sim.k, &sim.w),
        mobs: sim
            .mobs()
            .iter()
            .map(|mob| mob_view(&sim.w, mob, sim.server_time()))
            .collect::<Result<_, _>>()?,
        camps: sim.camp_rosters().to_vec(),
        tick: sim.tick(),
        server_time: sim.server_time(),
        phase: sim.phase(),
        winner: sim.winner().map(str::to_owned),
        players,
        fields: crate::status_runtime::observe::public(sim),
    })
}

/// Trusted privileged snapshot wrapper. Arbitrary-checkpoint diagnostics use [`try_public_snapshot`].
pub fn public_snapshot(sim: &Sim) -> PublicSnapshot {
    try_public_snapshot(sim).expect("trusted public snapshot is not representable")
}
