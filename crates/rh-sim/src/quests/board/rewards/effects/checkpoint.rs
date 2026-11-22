//! Admission of serialized Top25 spell rows. Runtime effects always use finite map geometry and bounded clocks.
use super::*;

pub(crate) fn validate(k: &crate::statics::MatchStatic, w: &World) -> Result<(), String> {
    let Some(s) = state(w) else { return Ok(()) };
    let now = w.server_time;
    let player = |id| w.player(id).is_some_and(|p| !p.seat.bot);
    let known = |id| w.known_actor(id);
    let point = |p: Vec2| {
        (0. ..k.arena.map.w as f64 * crate::constants::TILE).contains(&p.x)
            && (0. ..k.arena.map.h as f64 * crate::constants::TILE).contains(&p.y)
    };
    let deadline = |v: f64, ms: f64| v.is_finite() && v > 0. && v <= now + ms;
    // Each seat can earn one charge of Tornado. Even if all copies change hands,
    // the 600 ms windup ends before any spent charge can recharge.
    if s.tornadoes.len() > crate::constants::MAX_PLAYERS {
        return Err("too many pending Top25 tornadoes".into());
    }
    if s.buffs.iter().enumerate().any(|(i, r)| {
        !player(r.id)
            || !deadline(
                r.until,
                match r.effect {
                    Buff::Stasis => TOP25.stasis_ms,
                    Buff::Shield { .. } => TOP25.shield_ms,
                    Buff::BlindRage => TOP25.blind_rage_ms,
                    Buff::StillMind => TOP25.still_mind_ms,
                    Buff::Warhorn { .. } => TOP25.warhorn_ms,
                    Buff::Cleansed => TOP25.tornado_windup_ms,
                },
            )
            || s.buffs[..i].iter().any(|p| {
                p.id == r.id
                    && std::mem::discriminant(&p.effect) == std::mem::discriminant(&r.effect)
            })
            || match r.effect {
                Buff::Shield { refund_at } => {
                    refund_at != r.until - TOP25.shield_ms + Award::SpellShield.cooldown_ms()
                }
                Buff::Warhorn { count } => count == 0,
                _ => false,
            }
    }) || s.control.iter().enumerate().any(|(i, r)| {
        !known(r.id)
            || !deadline(r.until, TOP25.warhorn_ms)
            || s.control[..i].iter().any(|p| p.id == r.id)
            || match r.effect {
                Control::Pull { to } => !point(to),
                Control::Taunt { source } => source == r.id || !player(source),
            }
    }) || s.tornadoes.iter().any(|r| {
        !player(r.owner)
            || !point(r.at)
            || !deadline(r.lands, TOP25.tornado_windup_ms)
            || !r.max_hp.is_finite()
            || r.max_hp <= 0.
    }) || s.swaps.iter().enumerate().any(|(i, r)| {
        r.a == r.b
            || !player(r.a)
            // Swap-Stick may target any seat, fill bots included.
            || w.player(r.b).is_none()
            || r.slot_a > 1
            || r.slot_b > 1
            || !deadline(r.until, TOP25.swap_ms)
            || s.swaps[..i]
                .iter()
                .any(|p| [p.a, p.b].contains(&r.a) || [p.a, p.b].contains(&r.b))
    }) || s
        .seeds
        .iter()
        .enumerate()
        .any(|(i, (id, p))| !player(*id) || !point(*p) || s.seeds[..i].iter().any(|r| r.0 == *id))
    {
        return Err("invalid Top25 spell effect state".into());
    }
    if s.blocked_trails.iter().enumerate().any(|(i, row)| {
        !player(row.id)
            || row.effect == 0
            || row.effect > w.entity_counter
            || !deadline(row.until, crate::abilities::BLAZE_TRAIL_MS)
            || s.blocked_trails[..i]
                .iter()
                .any(|other| other.id == row.id && other.effect == row.effect)
    }) {
        return Err("invalid Top25 blocked trail state".into());
    }
    if s.shots.iter().enumerate().any(|(i, shot)| {
        !player(shot.owner)
            || !point(shot.at)
            || !point(shot.origin)
            || !rt::geometry_scalar_is_safe(shot.end.x)
            || !rt::geometry_scalar_is_safe(shot.end.y)
            || distance(shot.origin, shot.end) > shot.kind.parameters().0 + 1e-6
            || distance(shot.origin, shot.at) + distance(shot.at, shot.end)
                > distance(shot.origin, shot.end) + 1e-6
            || s.shots[..i]
                .iter()
                .filter(|p| p.owner == shot.owner && p.kind == shot.kind)
                .count()
                >= 2
    }) {
        return Err("invalid Top25 spell flight".into());
    }
    terrain::validate(k, w, &s.deployments)
}
