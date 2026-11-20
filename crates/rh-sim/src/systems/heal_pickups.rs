//! The four heal pickups at the centre of the grand coliseum (P-COLISEUM-HEALS-1, owner request 2026-11-19: "should
//! heal 40% HP overtime"). On a map whose coliseum is the grand one, four `ContainerKind::Heal` containers stand on the
//! four floor tiles around the coliseum centre; a standing, damaged player with no potion heal running who touches an
//! available one starts the Large potion's heal over time (`potions::DEFINITION`, legacy `consumables.ts:53-73`) and the
//! pickup returns after [`DEFINITION`]`.respawn_ms`. Same-tick competitions follow the wind emblem's legacy rule
//! (`consumables.ts:149-168`: nearest, then `tieRank`). Legacy has no such pickup (packet brief, proposed DEVIATIONS
//! row); every other map holds none, so legacy worlds keep their state, draws and hash.
use crate::constants::TILE;
use crate::math::hypot;
use crate::state::{Container, ContainerKind, Phase, World};
use crate::statics::MatchStatic;
use crate::systems::{potions, Cx};
use crate::{Arena, Vec2};

/// The pickups' rule numbers. Not folded into `content_hash` until the owner decides (packet brief).
pub(crate) struct Definition {
    /// How long a taken pickup stays unavailable (the owner's dial).
    pub(crate) respawn_ms: f64,
    /// Centre-to-pickup distance at which a body touches it (the 12 u legacy body plus 8 u).
    pub(crate) reach: f64,
}

pub(crate) const DEFINITION: Definition = Definition {
    respawn_ms: 30_000.,
    reach: 20.,
};

/// The four pickup positions (NW, NE, SW, SE: the centres of the four tiles around the grand coliseum's middle, tiles
/// (79,79), (80,79), (79,80), (80,80)) when the arena's coliseum is the grand one (its rect is `GRAND_DEFINITION.half`
/// tiles around the map centre) and the four tiles are floor, else `None` (legacy coliseum, no layers). The middle is
/// the rect's geometric centre, a tile corner (the rect is 36 tiles across); `ColiseumDef::centre` is the centre of
/// tile (`CX`, `CY`), half a tile further south-east, so it is not used.
pub(crate) fn spots(arena: &Arena) -> Option<[Vec2; 4]> {
    use crate::mapgen::coliseum::{CX, CY, GRAND_DEFINITION};
    let co = arena.map.layers()?.coliseum();
    let half = GRAND_DEFINITION.half as i32;
    let grand = crate::mapdata::TileRect {
        x: CX - half,
        y: CY - half,
        w: (2 * half) as u32,
        h: (2 * half) as u32,
    };
    if co.rect != grand {
        return None;
    }
    let c = Vec2::new(
        (f64::from(grand.x) + f64::from(grand.w) / 2.) * TILE,
        (f64::from(grand.y) + f64::from(grand.h) / 2.) * TILE,
    );
    let d = TILE / 2.;
    let at = [
        Vec2::new(c.x - d, c.y - d),
        Vec2::new(c.x + d, c.y - d),
        Vec2::new(c.x - d, c.y + d),
        Vec2::new(c.x + d, c.y + d),
    ];
    at.iter()
        .all(|p| {
            arena
                .map
                .is_floor((p.x / TILE).floor() as i64, (p.y / TILE).floor() as i64)
        })
        .then_some(at)
}

/// Construction (`initial_loot::initialize`): the four pickups, available, the last containers of the initial window,
/// after the initial loot and before `initial_birth_serial` is fixed (ids in NW, NE, SW, SE order), so the replay
/// recorder and the raid-start birth serials count past them. No RNG draw; nothing on a map without [`spots`].
pub(crate) fn place(k: &MatchStatic, w: &mut World) {
    let Some(at) = spots(&k.arena) else { return };
    for at in at {
        let id = w.next_entity_id();
        w.containers.push(Container {
            id,
            kind: ContainerKind::Heal,
            at,
            opened: false,
            items: Vec::new(),
            target: None,
            ready_at: 0.,
            stat_offer: false,
            born_tick: 0,
        });
    }
}

/// The walk-over rule, in the consumables slot after the potion Hots: for each available pickup in container order, the
/// nearest standing, damaged player with no potion heal running within [`DEFINITION`]`.reach` (ties by
/// `tie_rank_origin`) starts the Large potion's Hot; the pickup is then unavailable for `respawn_ms`. Training and the
/// non-active phases take nothing. Allocates nothing beyond the potion Hot machinery's own entry.
pub(crate) fn update(cx: &mut Cx) {
    if cx.k.config.training || cx.w.phase != Phase::Active {
        return;
    }
    let now = cx.w.server_time;
    for i in 0..cx.w.containers.len() {
        let c = &cx.w.containers[i];
        if c.kind != ContainerKind::Heal || now < c.ready_at {
            continue;
        }
        let at = c.at;
        let mut best: Option<(f64, u32, usize)> = None;
        for (slot, p) in cx.w.players.iter().enumerate() {
            if !p.standing() || p.vitals.hp >= p.vitals.max_hp || p.potions_healing != 0 {
                continue;
            }
            let pos = p.pos();
            let d = hypot(pos.x - at.x, pos.y - at.y);
            if d > DEFINITION.reach {
                continue;
            }
            let rank = crate::rng::tie_rank_origin(
                cx.k.secrets.order_salt,
                cx.w.tick,
                cx.w.actor_origin(p.id).expect("retained actor"),
            );
            let better = match best {
                None => true,
                Some((bd, br, _)) => d < bd || (d == bd && rank < br),
            };
            if better {
                best = Some((d, rank, slot));
            }
        }
        if let Some((_, _, slot)) = best {
            potions::start_hot(cx.w, slot, crate::potions::DEFINITION.large_share);
            cx.w.containers[i].ready_at = now + DEFINITION.respawn_ms;
        }
    }
}

/// `check_world`'s rule: on a map with [`spots`] exactly four `Heal` containers at them in order, none elsewhere; each
/// holds no item, is not opened, has no target, no stat offer, `born_tick` 0 and a finite clock in
/// `0..=server_time + respawn_ms`.
pub(crate) fn validate(k: &MatchStatic, w: &World) -> Result<(), String> {
    let want = spots(&k.arena);
    let mut n = 0;
    for c in w
        .containers
        .iter()
        .filter(|c| c.kind == ContainerKind::Heal)
    {
        let placed = want.is_some_and(|s| s.get(n) == Some(&c.at));
        if !placed
            || !c.items.is_empty()
            || c.opened
            || c.target.is_some()
            || c.stat_offer
            || c.born_tick != 0
            || !c.ready_at.is_finite()
            || !(0.0..=w.server_time + DEFINITION.respawn_ms).contains(&c.ready_at)
        {
            return Err(format!("invalid heal pickup {}", c.id));
        }
        n += 1;
    }
    if n != want.map_or(0, |s| s.len()) {
        return Err("heal pickups missing".into());
    }
    Ok(())
}

#[cfg(test)]
#[path = "heal_pickups_tests.rs"]
mod tests;
