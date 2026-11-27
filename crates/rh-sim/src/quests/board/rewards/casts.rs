//! Cast timing and short-lived effects for quest spells. Terrain and resurrection effects delegate to the special lane.
use super::awards::Award;
use super::{
    effects,
    inventory::{owner_mut, state_mut, Owned},
};
use crate::fighter::FighterIx;
use crate::state::{Phase, PlayerId};
use crate::systems::Cx;

fn recharge(spell: &mut Owned, now: f64) {
    while spell.charges < spell.max_charges && spell.ready_at > 0. && spell.ready_at <= now {
        spell.charges += 1;
        spell.ready_at = if spell.charges < spell.max_charges {
            spell.ready_at + spell.award.cooldown_ms()
        } else {
            now
        };
    }
}

pub(super) fn update(cx: &mut Cx) {
    let now = cx.w.server_time;
    if let Some(s) = state_mut(cx.w) {
        for o in &mut s.owners {
            for spell in o.slots.iter_mut().flatten() {
                recharge(spell, now);
            }
        }
        for loot in &mut s.loot {
            recharge(&mut loot.spell, now);
        }
    }
}

pub(super) fn cast(
    cx: &mut Cx,
    ix: FighterIx,
    slot: u8,
    x: f64,
    y: f64,
    target: Option<PlayerId>,
) -> bool {
    if cx.w.phase != Phase::Active
        || !(1..=2).contains(&slot)
        || !crate::status_runtime::geometry_scalar_is_safe(x)
        || !crate::status_runtime::geometry_scalar_is_safe(y)
    {
        return false;
    }
    let id = cx.w.core(ix).id;
    let Some(p) = cx.w.player(id) else {
        return false;
    };
    if !p.standing() || p.seat.bot || effects::stasis(cx.w, id) {
        return false;
    }
    let now = cx.w.server_time;
    let Some(mut owned) =
        super::inventory::owner(cx.w, id).and_then(|o| o.slots[(slot - 1) as usize])
    else {
        return false;
    };
    recharge(&mut owned, now);
    if owned.charges == 0 {
        return false;
    }
    let award = owned.award;
    let cleanse = award == Award::Cleanse;
    let stunned = crate::status_runtime::has(
        &p.statuses.instances,
        crate::status_runtime::StatusKind::Stun,
        now,
    );
    let silenced = crate::status_runtime::has(
        &p.statuses.instances,
        crate::status_runtime::StatusKind::Silence,
        now,
    );
    if silenced
        || (stunned && !matches!(award, Award::Cleanse | Award::StillMind))
        || (!cleanse && (effects::taunt(cx.w, id).is_some() || effects::pulled(cx.w, id)))
        || p.statuses.cast.is_some()
        || cx
            .w
            .abilities
            .as_ref()
            .and_then(|a| a.teleported_at)
            .is_some_and(|at| now - at < crate::abilities::TELEPORT_GRACE_MS)
        || (award == Award::SpellShield && effects::shielding(cx.w, id))
    {
        return false;
    }
    let aim = crate::Vec2::new(x, y);
    let landed = match award {
        Award::WallBuster => {
            cx.s.quest_spell_aim = Some(aim);
            super::super::special::wall_buster(cx, id)
        }
        Award::RebirthEgg => super::super::special::plant_egg(cx, id, aim),
        _ => effects::cast(cx, ix, award, aim, target),
    };
    if !landed {
        return false;
    }
    let cooldown = award.cooldown_ms();
    let planting = award == Award::ForgetMeNotSeed && effects::seed_planted(cx.w, id);
    if !planting {
        let o = owner_mut(cx.w, id).expect("cast owner has equipped award");
        let slot = &mut o.slots[(slot - 1) as usize];
        if cooldown == 0. {
            *slot = None;
        } else {
            if owned.charges == owned.max_charges {
                owned.ready_at = now + cooldown;
            }
            owned.charges -= 1;
            *slot = Some(owned);
        }
    }
    if planting {
        crate::quests::notices::owner(cx.ev, id, "Seed planted. Cast it again to return.");
    } else {
        crate::quests::notices::owner(cx.ev, id, format!("Used {}", award.name()));
    }
    true
}
