//! Replayable administrative seat lifecycle, below the Sim facade.
use crate::{
    hooks::{self, HookArgs, Hx, OutCause},
    state::{Phase, PlayerId, World},
    systems::{self, Cx},
};
/// `tick_ms`: the match's step (`SimConfig::tick_ms`), for the vision refresh of a returning seat.
pub(crate) fn dropped(w: &mut World, tick_ms: f64, id: PlayerId, dropped: bool) {
    let now = w.server_time;
    let active = w.phase == Phase::Active;
    let held = active || w.phase == Phase::Planning;
    let locked = w.rt.gates_locked;
    if let Some(i) = w.idx(id) {
        let p = &mut w.players[i];
        if dropped {
            if p.seat.dropped_at.is_none() && held {
                p.seat.dropped_at = Some(now);
            }
            p.inputs.clear();
        } else {
            p.seat.dropped_at = None;
            if active {
                crate::vision::refresh_player(p, w.tick, tick_ms, locked);
            }
        }
        systems::weapons::release_trigger(p, now);
    }
}
fn removed_hook(cx: &mut Cx, id: PlayerId) {
    hooks::fire(
        &mut Hx {
            stats: Default::default(),
            fx: None,
            k: cx.k,
            w: cx.w,
            ev: cx.ev,
        },
        HookArgs::FighterOut {
            id,
            cause: OutCause::Removed,
        },
    );
}
pub(crate) fn remove(cx: &mut Cx, id: PlayerId) {
    let index = cx.w.idx(id);
    if index.is_none() && cx.k.config.lifecycle.rules().is_none() {
        return;
    }
    if let Some(i) = index {
        if cx.w.phase == Phase::Active {
            systems::settle::down_player(cx.k, cx.w, cx.ev, None, i, crate::Outcome::Mia);
        } else if cx.w.phase == Phase::Planning {
            systems::settle::miss_in_planning(cx.w, cx.ev, i);
        }
    }
    if let Some(death) = crate::quests::board::special::egg::remove(cx.w, id) {
        systems::settle::resolve_egg_death(cx.w, cx.ev, id, death);
    }
    removed_hook(cx, id);
    crate::quests::board::rewards::effects::departed(cx.w, id);
    cx.w.players.remove(id);
    systems::planning::leave(cx.w, cx.ev, id);
    let standing = cx.w.players.iter().filter(|p| p.standing()).count();
    crate::quests::board::special::egg::tick(cx, standing);
}
pub(crate) fn abandon(cx: &mut Cx, id: PlayerId) -> u32 {
    let index = cx.w.idx(id);
    if index.is_none() && cx.k.config.lifecycle.rules().is_none() {
        return 0;
    }
    let gv = index.map_or(0, |i| {
        let p = &mut cx.w.players[i];
        let gv = if p.resolved {
            0
        } else {
            let g = p.credit.gv_parts;
            g.kills
                .saturating_add(g.mobs)
                .saturating_add(g.bounty)
                .saturating_add(g.quest)
        };
        p.resolved = true;
        gv
    });
    if matches!(cx.w.phase, Phase::Active | Phase::Planning) {
        cx.w.rt.voided = true;
    }
    // A void run is frozen. This administrative path returns GV without emitting a RunResult.
    cx.w.rebirth_eggs.clear();
    removed_hook(cx, id);
    crate::quests::board::rewards::effects::departed(cx.w, id);
    cx.w.players.remove(id);
    systems::planning::leave(cx.w, cx.ev, id);
    gv
}
